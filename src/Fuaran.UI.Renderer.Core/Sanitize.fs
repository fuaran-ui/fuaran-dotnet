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
//  Threat model summary (see `fuaran/SANITIZATION.md` for the full doc):
//    1. ExtraAttributes — drop `on*` event handlers, `style`, anything
//       outside data-*/aria-*. Reject keys/values containing `<`, `>`,
//       quote chars, or NULs (defence in depth — React's attribute
//       encoder already escapes, but `prop.custom`'s contract is "emit
//       verbatim" so we don't lean on it).
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
//  an AI-emission surface — see `fuaran/SANITIZATION.md` "Custom-renderer
//  trust boundary". The host's `RegisterCustomRenderer` closure is
//  expected to do its own escaping; this module does not police it.
// ============================================================================

open System

// ─── ExtraAttributes key/value sanitization ────────────────────────────────

/// Allowlist predicate for an ExtraAttributes key. The same data-* / aria-*
/// rule the smart-ctor enforces, plus a value-side guard against
/// the keys we know are dangerous even if they happen to start with
/// `data-` (none today, but the rejection list is the future-proof shape).
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
        else
            trimmed.StartsWith("data-", StringComparison.Ordinal)
            || trimmed.StartsWith("aria-", StringComparison.Ordinal)

/// Reject values containing control / NUL bytes or angle brackets. React's
/// attribute encoder already escapes these for the common case, but
/// `prop.custom` is a "render verbatim" contract — we don't lean on it.
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
/// both predicates. Used at render time so the renderer never trusts a
/// hand-built ExtraAttributes map.
let sanitizeExtraAttributes (attrs: Map<string, string>) : Map<string, string> =
    attrs
    |> Map.filter (fun k v -> isAllowedExtraAttributeKey k && isSafeExtraAttributeValue v)

// ─── URL-scheme sanitization ───────────────────────────────────────────────

/// Schemes the renderer accepts for `href` / `src` props. Same-origin
/// relative paths (`/foo`, `./foo`, `foo`, `#frag`) are accepted as a
/// separate branch — they have no scheme to validate.
let private allowedUrlSchemes =
    Set.ofList [ "http"; "https"; "mailto"; "tel"; "ftp"; "sftp" ]

/// Schemes the renderer ALWAYS rejects, regardless of caller intent.
let private rejectedUrlSchemes = Set.ofList [ "javascript"; "vbscript"; "file" ]

let private trimAndLower (s: string) : string = s.Trim().ToLowerInvariant()

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

/// Returns the sanitized URL or `None` if the URL's scheme is rejected.
/// `data:` is rejected by default for href/src (image data: URLs are a
/// known XSS vector when fed into SVG); callers that need data: URLs
/// must use the `Trust.raw` opt-in seam (not in
/// this commit's surface).
let sanitizeUrl (url: string) : string option =
    if isNull url then
        None
    else
        let trimmed = url.Trim()

        if trimmed = "" then
            // Empty href / src — caller's choice; renderer passes it through
            // (React renders `href=""` as a same-page link, which is the
            // documented HTML behaviour).
            Some trimmed
        else
            match extractScheme trimmed with
            | None, _ when trimmed.StartsWith "//" || trimmed.StartsWith "/\\" ->
                // Protocol-relative URL (`//host/path`) — has no scheme, so the schemeless branch would
                // otherwise admit it, but the browser resolves it to an OFF-ORIGIN `https://host/path`,
                // defeating the same-origin intent. `/\` is browsers' lenient normalisation of `//`.
                // Reject (Phase 298).
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
/// `fuaran/SANITIZATION.md`.
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
