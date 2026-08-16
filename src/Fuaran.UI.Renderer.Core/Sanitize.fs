module Fuaran.UI.Renderer.Sanitize

// ============================================================================
//  Fuaran — render-time sanitization contract.
//
//  The Author API (`Node.withExtraAttribute`) gates with a
//  data-* / aria-* prefix allowlist, but the typed-tree's record-with
//  syntax (`{ node with ExtraAttributes = Some ... }`) bypasses that gate.
//  Wire decode is similarly best-effort — a malformed AI
//  emission that constructs ExtraAttributes through the JSON decoder OR a
//  malicious adapter that hand-builds a Node<'Msg> can still smuggle keys
//  into the renderer.
//
//  This module is the render-time enforcement floor. Every string the
//  renderer pours into a DOM attribute, URL prop, or raw-HTML sink first
//  passes through one of these functions — so the tree-emission layer is
//  no longer the only gate. The functions are pure, Fable-portable, and
//  testable in isolation (the XSS-payload corpus lives
//  in `Fuaran.UI.Tests/SanitizeTests.fs`).
//
//  Threat model summary (see `fuaran-dotnet/SANITIZATION.md` for the full doc):
//    1. ExtraAttributes — the KEY passes a positive character allowlist
//       (`[A-Za-z0-9-]` only) on top of the data-*/aria-* prefix rule, so
//       `on*` handlers, `style`, and any key carrying `=`, quote chars,
//       `<`, `>`, `/`, whitespace or control bytes are dropped; the
//       surviving key is emitted TRIMMED. The VALUE rejects C0 control
//       bytes (except tab) and `<` / `>`; quote characters are NOT
//       rejected in a value — both renderers escape attribute values
//       (React's encoder client-side, Feliz.ViewEngine's `Interop.mkAttr`
//       server-side), and that escaping is what makes a quote in a value
//       inert. Attribute NAMES are escaped by neither, which is why the
//       key gate is an allowlist rather than a denylist and why the SSR
//       emission site re-checks it (`isSafeAttributeName`).
//    2. URL props — block `javascript:`, `vbscript:`, raw `data:` schemes.
//       Allow http/https/mailto/tel + same-origin relative paths.
//    3. Markdown raw-HTML output — strip `<script>` / `<iframe>` / `<object>`
//       elements + `on*=` attributes + `javascript:` hrefs before the HTML
//       reaches `dangerouslySetInnerHTML`. Since Phase 292 the markdown HTML
//       comes from Fuaran's own deterministic GFM renderer (`Markdown.toHtml`),
//       which escapes by construction and never passes raw HTML through, so
//       this sweep is now defence-in-depth over a much smaller surface (it was
//       the primary gate when the source was the npm `marked` library).
//
//  The `NodeKind.Custom` runtime renderer is a HOST trust boundary, not
//  an AI-emission surface — see `fuaran-dotnet/SANITIZATION.md` "Custom-renderer
//  trust boundary". The host's `RegisterCustomRenderer` closure is
//  expected to do its own escaping; this module does not police it.
// ============================================================================

open System

// Every public entry here `isNull`-tests a `string` parameter as its first act —
// the defence-in-depth floor documented above, since a hand-built or
// wire-decoded record can carry a null the type says cannot exist. F# 10's
// nullness checker rejects that test on a non-nullable `string` (FS3261). This
// project already declares the posture project-wide via `<Nullable>disable</Nullable>`,
// but that property is the ENTRY project's under Fable — a nullable-enabled entry
// (e.g. Fuaran.UI.ServerDriven) transpiles these sources with nullness ON and the
// file stops compiling. The file-scoped suppression makes the posture travel with
// the source, per the obj-erasure `#nowarn` precedents in Fuaran.UI/Types.fs +
// Fuaran.fs. Do NOT drop the `isNull` guards — they are the contract.
#nowarn "3261"

// ─── ExtraAttributes key/value sanitization ────────────────────────────────

/// Positive character allowlist for an HTML attribute NAME: ASCII letters,
/// digits, and `-`. Everything else — `=`, `"`, `'`, `` ` ``, `<`, `>`, `/`,
/// space, tab, newline, C0 controls, and any non-ASCII byte — is rejected.
///
/// This is a REJECTION gate, not an escape, because HTML has no escape for an
/// illegal character in an attribute name: a space inside a name simply starts
/// a NEW attribute, and an `=` starts its value. So
/// `data-x=1 onmouseover=alert(1) z` is not a mangled attribute name, it is
/// three attributes, one of them a live event handler. Neither renderer escapes
/// the name (React's attribute encoder and Feliz.ViewEngine's `Interop.mkAttr`
/// both escape only the VALUE), so dropping the entry is the only sound
/// response.
///
/// Exported so the emission sites that write a name verbatim can re-check it as
/// defence in depth rather than trusting upstream validation alone.
let isSafeAttributeName (name: string) : bool =
    if isNull name || name = "" then
        false
    else
        let mutable ok = true

        for ch in name do
            let allowed =
                (ch >= 'a' && ch <= 'z')
                || (ch >= 'A' && ch <= 'Z')
                || (ch >= '0' && ch <= '9')
                || ch = '-'

            if not allowed then
                ok <- false

        ok

/// Allowlist predicate for an ExtraAttributes key. The data-* / aria-* prefix
/// rule the smart-ctor enforces, plus explicit rejects for `on*` handlers and
/// `style`, plus `isSafeAttributeName` over the whole trimmed key — without
/// that last check a key like `data-x=1 onmouseover=alert(1) z` satisfies the
/// `data-` prefix and smuggles a live event handler into server-rendered HTML.
///
/// Note the predicate answers "is this key admissible", judged on its TRIMMED
/// form; `sanitizeExtraAttributes` is what emits the trimmed key, so a caller
/// using this predicate directly must trim before emission too.
let isAllowedExtraAttributeKey (key: string) : bool =
    if isNull key then
        false
    else
        let trimmed = key.Trim()

        if trimmed = "" then
            false
        elif trimmed.StartsWith("on", StringComparison.OrdinalIgnoreCase) then
            // Explicit reject: any `on*` event-handler attribute, even if
            // an author hand-constructed it through the record-with hatch.
            false
        elif trimmed.Equals("style", StringComparison.OrdinalIgnoreCase) then
            // CSS injection vector (`expression()`, `url(javascript:...)`
            // in legacy browsers, plus content-spoofing). Out of scope.
            false
        elif not (isSafeAttributeName trimmed) then
            // Attribute-NAME injection: any character that could terminate the
            // name and open a second attribute at the emission site.
            false
        else
            trimmed.StartsWith("data-", StringComparison.Ordinal)
            || trimmed.StartsWith("aria-", StringComparison.Ordinal)

/// Reject values containing C0 control / NUL bytes (tab excepted) or angle
/// brackets. Quote characters are deliberately NOT rejected here: both
/// renderers escape attribute VALUES (React's attribute encoder client-side,
/// Feliz.ViewEngine's `Interop.mkAttr` server-side), so a quote in a value is
/// inert. This check is the defence-in-depth floor UNDER that escaping, not a
/// replacement for it — do not drop the escaping on the strength of this
/// predicate.
let isSafeExtraAttributeValue (value: string) : bool =
    if isNull value then
        false
    else
        let mutable ok = true

        for ch in value do
            // C0 control chars (including NUL, line breaks) — attribute
            // injection vector.
            if int ch < 0x20 && ch <> '\t' then
                ok <- false
            elif ch = '<' || ch = '>' then
                ok <- false

        ok

/// Filter a candidate ExtraAttributes map down to the entries that pass
/// both predicates, re-keyed on the TRIMMED key. Used at render time so the
/// renderer never trusts a hand-built ExtraAttributes map.
///
/// The re-key is load-bearing: the predicate judges `key.Trim()`, so emitting
/// the original would emit a string the gate never inspected — leading/trailing
/// whitespace around an otherwise-valid name is exactly the residue an
/// emission site would write verbatim. Two keys differing only in surrounding
/// whitespace collapse to one entry (last wins in the map's ordinal key order);
/// that is a deliberate, deterministic normalisation, not an accident.
let sanitizeExtraAttributes (attrs: Map<string, string>) : Map<string, string> =
    attrs
    |> Map.fold
        (fun acc k v ->
            if isAllowedExtraAttributeKey k && isSafeExtraAttributeValue v then
                Map.add (k.Trim()) v acc
            else
                acc)
        Map.empty

// ─── URL-scheme sanitization ───────────────────────────────────────────────

/// Schemes the renderer accepts for `href` / `src` props. Same-origin
/// relative paths (`/foo`, `./foo`, `foo`, `#frag`) are accepted as a
/// separate branch — they have no scheme to validate.
let private allowedUrlSchemes =
    Set.ofList [ "http"; "https"; "mailto"; "tel"; "ftp"; "sftp" ]

/// Schemes the renderer ALWAYS rejects, regardless of caller intent.
let private rejectedUrlSchemes = Set.ofList [ "javascript"; "vbscript"; "file" ]

let private trimAndLower (s: string) : string = s.Trim().ToLowerInvariant()

/// §19 rule 1 — normalise a URL string exactly as the WHATWG URL Standard's basic
/// URL parser does before it parses anything, ASCII-exact, in this order:
///
///   1. remove leading and trailing C0 control or space — ALL of U+0000–U+0020,
///      not merely the whitespace subset;
///   2. remove every U+0009 / U+000A / U+000D from anywhere in what remains.
///
/// This is deliberately NOT `String.Trim()`. A native trim answers a different
/// question in every language — .NET, JS, Go and Rust leave U+001C–U+001F where
/// Python removes them; JS keeps U+0085 where the other four drop it — and all of
/// them remove non-ASCII whitespace (U+00A0, U+2028, …) that the parser keeps.
/// The floor's whole purpose is that a tree vetted on one host is safe on
/// another, so the normalisation has to be defined by the parser that will
/// actually consume the string rather than by the host's standard library.
///
/// Step 2 is those three code points ONLY: the parser removes U+000B and U+000C
/// at the edges (step 1) and KEEPS them in the interior, so `/<VT>/host/x` is an
/// ordinary same-origin path and must stay one.
let private normalizeUrlForFloor (s: string) : string =
    let isC0OrSpace (c: char) = c <= ' '
    let mutable lo = 0
    let mutable hi = s.Length - 1

    while lo <= hi && isC0OrSpace s[lo] do
        lo <- lo + 1

    while hi >= lo && isC0OrSpace s[hi] do
        hi <- hi - 1

    let sb = System.Text.StringBuilder(hi - lo + 1)

    for i in lo..hi do
        match s[i] with
        | '\t'
        | '\n'
        | '\r' -> ()
        | c -> sb.Append c |> ignore

    sb.ToString()

/// Split a URL into `(schemeOpt, rest)`. A URL without a `:` (e.g. a
/// relative path, a fragment, an empty string) returns `(None, url)`.
/// Whitespace and control chars inside the scheme region defeat the
/// match (some classic XSS payloads use `java\tscript:` to evade naïve
/// prefix checks — we normalise by stripping ASCII whitespace + C0
/// controls from the scheme candidate before classifying).
let private extractScheme (url: string) : string option * string =
    if isNull url then
        None, ""
    else
        // Look for the first ':' BEFORE any '/'. A relative path like
        // "foo/bar:baz" has no scheme; "foo:bar" does.
        let mutable colonIdx = -1
        let mutable slashIdx = -1
        let mutable i = 0

        while i < url.Length && colonIdx < 0 && slashIdx < 0 do
            let ch = url[i]

            if ch = ':' then
                colonIdx <- i
            elif ch = '/' || ch = '?' || ch = '#' then
                slashIdx <- i

            i <- i + 1

        if colonIdx < 0 || (slashIdx >= 0 && slashIdx < colonIdx) then
            None, url
        else
            // Strip whitespace + control chars from the scheme candidate
            // so `java\tscript`, ` javascript`, `JAVASCRIPT` all classify
            // as `javascript`.
            let raw = url.Substring(0, colonIdx)

            let cleaned =
                raw |> Seq.filter (fun ch -> int ch > 0x20) |> Seq.toArray |> System.String

            Some(trimAndLower cleaned), url

/// `true` when a schemeless URL is PROTOCOL-RELATIVE — it starts with two
/// slash-ish characters, in any mix of `/` and `\`.
///
/// All four spellings (`//host`, `/\host`, `\\host`, `\/host`) resolve
/// off-origin, because WHATWG URL parsing treats `\` as `/` for a special
/// scheme: the browser normalises the pair to `//` and reads what follows as an
/// AUTHORITY, not a path. Phase 298 closed the first two; the backslash-leading
/// pair fell through to the "no scheme → relative, allowed" arm, so a `Link`
/// href of `\\evil.example/x` rendered as a live off-origin link and an
/// `Image.src` became an off-origin request leaking the Referer.
///
/// A SINGLE leading backslash (`\evil.example`) is deliberately not caught: the
/// same WHATWG rule reads it as `/evil.example`, an ordinary same-origin path,
/// which is exactly what the `/`-spelling is allowed to be.
let private isProtocolRelative (url: string) : bool =
    let slashish (c: char) = c = '/' || c = '\\'
    url.Length >= 2 && slashish url[0] && slashish url[1]

/// Returns the sanitized URL or `None` if the URL's scheme is rejected.
/// `data:` is rejected by default for href/src (image data: URLs are a
/// known XSS vector when fed into SVG); callers that need data: URLs
/// must use the `Trust.raw` opt-in seam (not in
/// this commit's surface).
let sanitizeUrl (url: string) : string option =
    if isNull url then
        None
    else
        // §19 rule 1 — the URL Standard's own pre-parse normalisation, NOT `.Trim()`.
        // See `normalizeUrlForFloor`. Rule 1's output is also what gets EMITTED on
        // acceptance, so an accepted URL carrying an interior tab loses it — which is
        // what the browser would have parsed anyway.
        let trimmed = normalizeUrlForFloor url

        if trimmed = "" then
            // Empty href / src — caller's choice; renderer passes it through
            // (React renders `href=""` as a same-page link, which is the
            // documented HTML behaviour).
            Some trimmed
        else
            match extractScheme trimmed with
            | None, _ when isProtocolRelative trimmed ->
                // Protocol-relative URL (`//host/path`) — has no scheme, so the schemeless branch would
                // otherwise admit it, but the browser resolves it to an OFF-ORIGIN `https://host/path`,
                // defeating the same-origin intent. Reject (Phase 298; the backslash spellings
                // `\\host` / `\/host` added by Phase 784 — see `isProtocolRelative`).
                None
            | None, _ ->
                // No scheme → relative / fragment / same-origin. Allowed.
                Some trimmed
            | Some scheme, _ when rejectedUrlSchemes.Contains scheme -> None
            | Some scheme, _ when allowedUrlSchemes.Contains scheme -> Some trimmed
            | Some _, _ ->
                // Unknown scheme — reject by default. Conservative posture;
                // adding a scheme to the allowlist is a one-line additive
                // change for hosts that need it.
                None

/// Convenience: returns the URL itself if accepted, or the literal string
/// `"about:blank"` if rejected. Used by renderer call sites that have to
/// emit *some* href to keep the link element valid.
let sanitizeUrlOrBlank (url: string) : string =
    sanitizeUrl url |> Option.defaultValue "about:blank"

// ─── Markdown raw-HTML sanitization ────────────────────────────────────────

/// Strip `<script>` / `<iframe>` / `<object>` / `<embed>` / `<link>` /
/// `<meta>` / `<form>` elements (open tags + balanced bodies) from a
/// chunk of HTML, plus inline `on*=` event-handler attributes, plus
/// `javascript:` / `vbscript:` href / src values. Approximate: this is
/// NOT a full HTML parser, but since Phase 292 the Fuaran render path feeds
/// it the deterministic GFM renderer's output (a known, escaped-by-
/// construction shape), so the substring sweep is sufficient defence in
/// depth on top of that renderer's own escaping.
///
/// Hosts that need full DOMPurify-level sanitization layer it consumer-
/// side over the markdown emission — Fuaran's renderer-side pass is the
/// floor, not the ceiling. The contract is documented in
/// `fuaran-dotnet/SANITIZATION.md`.
///
/// ⚠️ PRECONDITION (Phase 303): this is a *defence-in-depth* sweep over the Fuaran GFM renderer's
/// own already-escaped-by-construction output — NOT a general-purpose HTML sanitizer. It is an
/// approximate substring scanner with the usual gaps (it anchors `on*=` on leading whitespace and
/// splits attributes on the first `=`/quote), sound for the renderer's narrow output shape but
/// bypassable on arbitrary untrusted HTML. Do NOT call it as the sole sanitizer for HTML from an
/// untrusted source — route such input through a real sanitizer (DOMPurify-class) first.
let sanitizeMarkdownHtml (html: string) : string =
    if isNull html || html = "" then
        ""
    else
        let mutable result = html

        // Remove balanced dangerous element blocks. Iterates so nested /
        // sibling occurrences are all caught.
        let dangerousElements =
            [ "script"; "iframe"; "object"; "embed"; "form"; "link"; "meta" ]

        for tag in dangerousElements do
            let openTag = "<" + tag
            let closeTag = "</" + tag + ">"

            let mutable keepGoing = true

            while keepGoing do
                let i = result.IndexOf(openTag, StringComparison.OrdinalIgnoreCase)

                if i < 0 then
                    keepGoing <- false
                else
                    let j = result.IndexOf(closeTag, i, StringComparison.OrdinalIgnoreCase)

                    if j >= 0 then
                        result <- result.Remove(i, j + closeTag.Length - i)
                    else
                        // Unbalanced open tag — strip from the open `<` to
                        // the next `>` so we don't leave a half-tag.
                        let endBracket = result.IndexOf('>', i)

                        if endBracket >= 0 then
                            result <- result.Remove(i, endBracket - i + 1)
                        else
                            // No closing `>` at all — drop the tail.
                            result <- result.Substring(0, i)

                            keepGoing <- false

        // Strip inline `on*="..."` event handlers. Scan for ` on` (with a
        // leading whitespace) that occurs INSIDE a tag's start-tag interior
        // (between an unescaped `<` and its `>`) and remove up to the matching
        // closing quote. Conservative — matches `onclick=`, `onload=`, etc.
        //
        // The tag-interior anchor is load-bearing: without it the scan matches
        // the leading-whitespace-`on<letter>` pattern in ordinary prose — the
        // English words "one", "only", "once", "onto", "online", … — and the
        // boolean-attribute branch below then deletes the word from body text.
        // Because `sanitizeMarkdownHtml` runs over the deterministic
        // `Markdown.toHtml` output (raw HTML already escaped by construction),
        // a real event-handler attribute can only appear inside a tag the
        // renderer itself emitted, so restricting the scan to tag interiors is
        // both correct and removes the body-text false positive.
        let stripEventHandlers (input: string) : string =
            let mutable s = input
            let mutable keepGoing = true

            while keepGoing do
                let lower = s.ToLowerInvariant()
                let mutable found = -1
                let mutable i = 0
                let mutable insideTag = false

                while i < lower.Length - 3 && found < 0 do
                    let ch = lower[i]

                    if ch = '<' then
                        insideTag <- true
                    elif ch = '>' then
                        insideTag <- false
                    elif
                        insideTag
                        && (ch = ' ' || ch = '\t' || ch = '\n')
                        && lower[i + 1] = 'o'
                        && lower[i + 2] = 'n'
                        && (Char.IsLetter lower[i + 3])
                    then
                        found <- i

                    i <- i + 1

                if found < 0 then
                    keepGoing <- false
                else
                    // Find the `=` then the value's terminator (matching
                    // quote OR space OR `>` for unquoted values).
                    let eq = s.IndexOf('=', found)
                    let nextSpace = s.IndexOfAny([| ' '; '\t'; '\n'; '>' |], found + 1)

                    if eq < 0 || (nextSpace >= 0 && nextSpace < eq) then
                        // No `=` — likely a boolean attribute like `onload`.
                        // Strip the attribute name only.
                        let stopAt = if nextSpace >= 0 then nextSpace else s.Length

                        s <- s.Remove(found, stopAt - found)
                    else
                        // Skip leading whitespace after `=`
                        let mutable v = eq + 1

                        while v < s.Length && (s[v] = ' ' || s[v] = '\t') do
                            v <- v + 1

                        let stopAt =
                            if v < s.Length && (s[v] = '\'' || s[v] = '"') then
                                let q = s[v]
                                let close = s.IndexOf(q, v + 1)
                                if close >= 0 then close + 1 else s.Length
                            else
                                // Unquoted — stop at next whitespace or `>`.
                                let candidate = s.IndexOfAny([| ' '; '\t'; '\n'; '>' |], v)

                                if candidate >= 0 then candidate else s.Length

                        s <- s.Remove(found, stopAt - found)

            s

        result <- stripEventHandlers result

        // Strip `javascript:` / `vbscript:` URLs in href / src values.
        let dangerousProtocols = [ "javascript:"; "vbscript:" ]

        for proto in dangerousProtocols do
            let mutable keepGoing = true

            while keepGoing do
                let i = result.ToLowerInvariant().IndexOf(proto, StringComparison.Ordinal)

                if i < 0 then
                    keepGoing <- false
                else
                    // Replace with `about:blank` so the surrounding attribute
                    // remains structurally valid.
                    result <- result.Substring(0, i) + "about:blank" + result.Substring(i + proto.Length)

        result
