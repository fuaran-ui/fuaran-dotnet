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

// ─── Destination policy — typed egress allowlists ──────────────────────────
//
//  Everything above answers "is this URL SAFE TO HAVE". Nothing above answers
//  "is this DESTINATION one the composition declared", and only the second
//  question closes exfiltration. `https://collector.example/?s=<bound state>`
//  passes every check in this file: the scheme is allowlisted, the host is
//  well-formed, there is no script anywhere in it. Put it in an `Image.src`
//  and the browser contacts it with NO user act at all — rendering IS the
//  request — carrying whatever the tree interpolated into the query string.
//
//  So the floor gains a second, orthogonal gate: a scheme allowlist says what
//  a URL may BE, and an origin allowlist says where it may GO. Both are
//  positive lists; neither substitutes for the other, and this one runs after
//  the other because there is no point asking where an unsafe URL points.
//
//  Two shapes are deliberate and worth stating, because both look like
//  omissions:
//
//    - A rule names a HOST, never a scheme and never a path. Scheme is already
//      reduced to the allowlisted set above, and every "scheme wildcard"
//      spelling anyone reaches for (`*://`, `http*://`, `https?://`) parses
//      differently on different hosts — which makes the wildcard itself the
//      vulnerability. Path scoping is likewise refused: a path is not a
//      security boundary, and a policy that appears to bound one invites
//      reliance on a bound it does not have.
//    - The policy is HOST-CONSTRUCTED and ENCODE-ONLY. There is a canonical
//      projection (`encodeEgressPolicy`) so a composition can publish its
//      declared egress for review and diff, and there is deliberately NO
//      decoder — see that function's note. A policy a tree could supply is a
//      policy a hostile tree can widen, which is not a policy.

/// The classes of destination a rule can be scoped to. Closed by construction:
/// a policy can say something only about a class this DU can name.
[<RequireQualifiedAccess>]
type EgressClass =
    /// A rendered `href` the user must ACT on — a link, a markdown anchor.
    | Hyperlink
    /// A rendered `src` the browser fetches with NO user act: an image, a
    /// stylesheet, a media element. THE exfiltration class — a destination
    /// here is contacted merely by rendering the tree, which is why it is
    /// scoped separately from `Hyperlink` rather than folded in with it.
    | Media
    /// A third-party DOCUMENT the browser fetches with no user act and then
    /// EXECUTES in its own browsing context (Phase 1111 — `NodeKind.Embed`).
    ///
    /// Scoped separately from `Media` rather than folded into it, and the
    /// separation is the whole of the phase's egress design. `Media` is
    /// fetch-and-display: the bytes are decoded by the user agent's own codec
    /// and reach no scripting context. An embed is fetch-and-EXECUTE: the bytes
    /// are a document that runs, makes its own requests, and can be granted
    /// script and its own origin. A composition that declared a CDN for image
    /// egress has said nothing about which documents it will run, and a class
    /// that conflated the two would let the first declaration answer the second
    /// question.
    | Embed
    /// A navigation the tree asks for (`Action.Navigate`, `PushState`).
    | Route
    /// A file download the tree asks for.
    | Download
    /// A file READ the tree asks for. It carries no URL of its own — see
    /// the note on the effect seam's classification — but it is scoped here
    /// so a policy can speak about it in the same vocabulary.
    | FileRead

module EgressClass =

    /// Stable lowercase name — the wire spelling, and what a refusal records.
    let name (cls: EgressClass) : string =
        match cls with
        | EgressClass.Hyperlink -> "hyperlink"
        | EgressClass.Media -> "media"
        | EgressClass.Embed -> "embed"
        | EgressClass.Route -> "route"
        | EgressClass.Download -> "download"
        | EgressClass.FileRead -> "fileRead"

    /// Every class, in wire order. Used by `allowOrigin` when a rule is
    /// declared without a class scope (which means "every class").
    let all: EgressClass list =
        [ EgressClass.Hyperlink
          EgressClass.Media
          EgressClass.Embed
          EgressClass.Route
          EgressClass.Download
          EgressClass.FileRead ]

    /// Parse a wire spelling. Case-insensitive on the caller's behalf; an
    /// unknown name is `None` rather than a silently-ignored rule, because a
    /// policy that quietly drops a class it did not understand is broader than
    /// the one its author wrote.
    let parse (s: string) : EgressClass option =
        if isNull s then
            None
        else
            let k = s.Trim().ToLowerInvariant()
            all |> List.tryFind (fun c -> (name c).ToLowerInvariant() = k)

/// One allowed destination. Hosts only — no scheme, no port, no path.
type EgressOrigin =
    /// Exactly this host. `example.com` matches `example.com` and nothing
    /// else — not `a.example.com`, not `notexample.com`.
    | ExactHost of host: string
    /// This host and any subdomain of it. `example.com` matches
    /// `example.com` and `a.b.example.com`; it never matches
    /// `notexample.com`, because the match requires a label boundary. This is
    /// the "registrable suffix" spelling — a suffix, not a substring, and not
    /// a wildcard.
    | HostSuffix of suffix: string

/// One rule: an origin, and the classes it is declared FOR.
type EgressRule =
    {
        Origin: EgressOrigin
        /// The classes this origin is allowed for. An EMPTY list allows no
        /// class — a rule that names nothing permits nothing, which is the
        /// only reading consistent with a positive list. Use
        /// `EgressClass.all` to mean "every class".
        Classes: EgressClass list
    }

/// A typed egress allowlist.
type EgressPolicy =
    {
        Rules: EgressRule list
        /// When `true`, EVERY network origin is permitted and `Rules` is not
        /// consulted at all.
        ///
        /// This is the escape hatch, and it is a FIELD rather than the absence
        /// of rules on purpose: an empty allowlist must read as "nothing is
        /// declared", never as "everything is fine". Those are opposite
        /// postures and the empty list is what a half-built policy looks like,
        /// so conflating them would make the failure mode of forgetting to
        /// declare anything indistinguishable from deciding not to. It is
        /// `false` in `denyNonLocalEgress` and `true` only in
        /// `permissiveEgress`, which is named so reaching it is greppable.
        AllowAnyOrigin: bool
        /// Whether SAME-ORIGIN destinations (a relative path, a fragment, an
        /// empty URL) are permitted. `true` in both shipped policies: a tree
        /// pointing at its own host has not left, and denying it would make
        /// ordinary in-app links unrenderable. A host serving several tenants
        /// from one origin sets this `false` and declares what it means.
        AllowLocal: bool
        /// Whether destinations with no network host (`mailto:`, `tel:`) are
        /// permitted. `false` by default: `mailto:` IS an egress channel — a
        /// body parameter carries arbitrary text off the machine — and it has
        /// no host for a rule to name, so it cannot be allowlisted, only
        /// permitted wholesale.
        AllowNonNetwork: bool
    }

/// What a URL resolves to, once the scheme floor has accepted it.
[<RequireQualifiedAccess>]
type Destination =
    /// Same-origin: a relative path, a fragment, an empty URL.
    | Local
    /// An absolute network destination at this host — lowercased, with
    /// userinfo, port and any trailing dot removed.
    | Remote of host: string
    /// A scheme with no network host for a rule to name (`mailto:`, `tel:`).
    | NonNetwork of scheme: string
    /// The scheme floor rejected the URL, or it declares a network scheme with
    /// no extractable host.
    | Rejected

/// Why a destination was refused, or that it was not.
[<RequireQualifiedAccess>]
type EgressVerdict =
    /// Accepted. Carries the NORMALISED URL to emit — the same string
    /// `sanitizeUrl` would have returned, so an accepting call site needs no
    /// second pass.
    | Allowed of url: string
    /// `sanitizeUrl` rejected it before policy was ever consulted.
    | UnsafeUrl
    /// A network destination whose host this policy does not declare for this
    /// class. Carries the HOST ONLY — never the path or query, which is
    /// exactly where an exfiltrated payload would be sitting.
    | UndeclaredOrigin of host: string * cls: EgressClass
    /// A same-origin destination under a policy that denies local egress.
    | LocalDenied of cls: EgressClass
    /// A hostless scheme under a policy that denies non-network egress.
    | NonNetworkDenied of scheme: string * cls: EgressClass

/// Network schemes — the ones that reach a host a rule can name. A scheme the
/// floor allows but that is absent here (`mailto`, `tel`) is `NonNetwork`.
let private networkSchemes = Set.ofList [ "http"; "https"; "ftp"; "sftp" ]

/// Lowercase, trim, and drop a single trailing root dot (`example.com.` and
/// `example.com` are the same host to a resolver, so they must be the same
/// host to a policy — otherwise the dotted spelling walks straight past an
/// exact rule).
let private normalizeHost (h: string) : string =
    if isNull h then
        ""
    else
        let t = h.Trim().ToLowerInvariant()

        if t.EndsWith(".", StringComparison.Ordinal) then
            t.Substring(0, t.Length - 1)
        else
            t

/// Extract the host from an absolute URL's authority, WHATWG-style: `\` counts
/// as `/` when locating the authority, userinfo before the LAST `@` is
/// discarded, a port is dropped, and an IPv6 literal keeps its brackets.
///
/// The `LastIndexOf '@'` is load-bearing rather than fussy: `https://good.example@evil.example/x`
/// is a request to `evil.example`, and a naive first-`@` split reads it as the
/// opposite — which is the classic credential-confusion spelling an allowlist
/// exists to refuse.
let private authorityHost (url: string) : string option =
    let colon = url.IndexOf ':'

    if colon < 0 then
        None
    else
        let mutable i = colon + 1
        let mutable slashes = 0

        while i < url.Length && (url[i] = '/' || url[i] = '\\') do
            slashes <- slashes + 1
            i <- i + 1

        if slashes < 2 then
            None
        else
            let start = i
            let mutable j = i

            let isAuthorityEnd (c: char) =
                c = '/' || c = '\\' || c = '?' || c = '#'

            while j < url.Length && not (isAuthorityEnd url[j]) do
                j <- j + 1

            let authority = url.Substring(start, j - start)

            let afterUserInfo =
                let at = authority.LastIndexOf '@'

                if at >= 0 then authority.Substring(at + 1) else authority

            if afterUserInfo = "" then
                None
            elif afterUserInfo.StartsWith("[", StringComparison.Ordinal) then
                let close = afterUserInfo.IndexOf ']'

                if close < 0 then
                    None
                else
                    Some(afterUserInfo.Substring(0, close + 1).ToLowerInvariant())
            else
                let port = afterUserInfo.IndexOf ':'

                let h =
                    if port >= 0 then
                        afterUserInfo.Substring(0, port)
                    else
                        afterUserInfo

                let n = normalizeHost h
                if n = "" then None else Some n

/// Resolve a URL to the destination a policy reasons about. Runs the scheme
/// floor FIRST — there is nothing to say about where an unsafe URL points.
let classifyDestination (url: string) : Destination =
    match sanitizeUrl url with
    | None -> Destination.Rejected
    | Some safe ->
        if safe = "" then
            Destination.Local
        else
            match extractScheme safe with
            // No scheme reaching here is same-origin: `sanitizeUrl` has
            // already refused every protocol-relative spelling, which is the
            // one schemeless shape that leaves the origin.
            | None, _ -> Destination.Local
            | Some scheme, _ when networkSchemes.Contains scheme ->
                match authorityHost safe with
                | Some h -> Destination.Remote h
                | None -> Destination.Rejected
            | Some scheme, _ -> Destination.NonNetwork scheme

/// Does this rule's origin match this host?
let private originMatches (origin: EgressOrigin) (host: string) : bool =
    match origin with
    | ExactHost h ->
        let h = normalizeHost h
        h <> "" && h = host
    | HostSuffix s ->
        let s = normalizeHost s
        s <> "" && (host = s || host.EndsWith("." + s, StringComparison.Ordinal))

/// Is this host declared for this class by this policy?
let isDeclaredOrigin (policy: EgressPolicy) (cls: EgressClass) (host: string) : bool =
    let host = normalizeHost host

    host <> ""
    && (policy.AllowAnyOrigin
        || policy.Rules
           |> List.exists (fun r -> List.contains cls r.Classes && originMatches r.Origin host))

/// The whole check: scheme floor, then destination policy, for one class.
let checkDestination (policy: EgressPolicy) (cls: EgressClass) (url: string) : EgressVerdict =
    match classifyDestination url with
    | Destination.Rejected -> EgressVerdict.UnsafeUrl
    | Destination.Local ->
        if policy.AllowLocal then
            EgressVerdict.Allowed(sanitizeUrl url |> Option.defaultValue "")
        else
            EgressVerdict.LocalDenied cls
    | Destination.NonNetwork scheme ->
        if policy.AllowNonNetwork then
            EgressVerdict.Allowed(sanitizeUrl url |> Option.defaultValue "")
        else
            EgressVerdict.NonNetworkDenied(scheme, cls)
    | Destination.Remote host ->
        if isDeclaredOrigin policy cls host then
            EgressVerdict.Allowed(sanitizeUrl url |> Option.defaultValue "")
        else
            EgressVerdict.UndeclaredOrigin(host, cls)

/// Log-safe description of a verdict. Carries the HOST and the CLASS, never
/// the URL — the same discipline the effect seam's denial record keeps, and for
/// the same reason: a refusal record outlives the session, and the query string
/// of a refused exfiltration attempt is the payload itself.
let describeEgressVerdict (v: EgressVerdict) : string =
    match v with
    | EgressVerdict.Allowed _ -> "destination allowed"
    | EgressVerdict.UnsafeUrl -> "destination refused: the URL is not safe to render"
    | EgressVerdict.UndeclaredOrigin(host, cls) ->
        sprintf "destination refused: origin '%s' is not declared for '%s' egress" host (EgressClass.name cls)
    | EgressVerdict.LocalDenied cls ->
        sprintf "destination refused: this policy denies same-origin '%s' egress" (EgressClass.name cls)
    | EgressVerdict.NonNetworkDenied(scheme, cls) ->
        sprintf
            "destination refused: scheme '%s' has no origin to declare for '%s' egress"
            scheme
            (EgressClass.name cls)

/// The `href` / `src` a REFUSED destination renders as.
///
/// Deliberately NOT the bare `about:blank` that `sanitizeUrlOrBlank` emits: a
/// silent neuter is indistinguishable from an authoring mistake, and the whole
/// point of the 782 posture is that "nothing happened" and "this was refused"
/// are different facts. The fragment is inert in every browser and greppable in
/// a rendered document.
[<Literal>]
let egressRefusalUrl = "about:blank#fuaran-egress-refused"

/// The attribute name an emission site attaches beside a refused destination.
/// Passes `isSafeAttributeName` and the `data-` prefix rule by construction, so
/// it survives `sanitizeExtraAttributes` unchanged.
[<Literal>]
let egressRefusalAttribute = "data-fuaran-egress-refused"

/// The refusal marker for a verdict, or `None` when the destination was
/// allowed. The VALUE names the class and — where there is one — the host; it
/// never carries the URL, for the reason `describeEgressVerdict` gives.
let egressRefusalMarker (v: EgressVerdict) : (string * string) option =
    match v with
    | EgressVerdict.Allowed _ -> None
    | EgressVerdict.UnsafeUrl -> Some(egressRefusalAttribute, "unsafe-url")
    | EgressVerdict.UndeclaredOrigin(host, cls) -> Some(egressRefusalAttribute, EgressClass.name cls + ":" + host)
    | EgressVerdict.LocalDenied cls -> Some(egressRefusalAttribute, EgressClass.name cls + ":local")
    | EgressVerdict.NonNetworkDenied(scheme, cls) -> Some(egressRefusalAttribute, EgressClass.name cls + ":" + scheme)

/// The one-call render seam: the URL to emit, plus the attributes that record a
/// refusal in the document itself. An emission site adopts this by replacing its
/// `sanitizeUrlOrBlank` call and splicing the returned attribute list — which is
/// the whole adoption, per call site.
let sanitizeUrlForEgress (policy: EgressPolicy) (cls: EgressClass) (url: string) : string * (string * string) list =
    let verdict = checkDestination policy cls url

    match verdict with
    | EgressVerdict.Allowed safe -> safe, []
    | refused ->
        egressRefusalUrl,
        (match egressRefusalMarker refused with
         | Some kv -> [ kv ]
         | None -> [])

/// The `embed` SCHEME floor (Phase 1111) — §19-class, and deliberately NOT §19.
///
/// `https` is the only accepted scheme. Everything else is refused, and the two
/// exclusions worth naming are the ones §19 accepts:
///
///   * `http` — an embed is fetched and then EXECUTED, so a document delivered
///     over a channel any intermediary can rewrite is an intermediary's script
///     running in a frame this page created.
///   * a SCHEMELESS (relative) reference — it names a same-origin document,
///     which is exactly the shape where `AllowSameOrigin` together with
///     `AllowScripts` lets the framed document reach its own frame element and
///     remove the sandbox attribute. A host that wants to compose its own
///     content has a kind for that; this kind is for the uncooperative third
///     party.
///
/// One accepted scheme and NO positional test, which is the second reason this
/// is its own function rather than a parameter on the §19 floor: rule 5's
/// protocol-relative check exists because a schemeless reference is otherwise
/// admitted, and a class that admits none cannot inherit that rule's evasion
/// surface. Rule 1's normalisation is still shared — it is what makes the
/// scheme extraction see the string the parser will see.
///
/// `None` means REFUSED, and the caller drops the attribute rather than
/// substituting anything: an `<iframe>` with no `src` is a well-defined empty
/// frame that fetches nothing, where a refusal URL in an embed's `src` would be
/// a frame that renders a page the author never named.
let sanitizeEmbedSrc (url: string) : string option =
    if isNull url then
        None
    else
        let normalized = normalizeUrlForFloor url

        if normalized = "" then
            None
        else
            match extractScheme normalized with
            | Some "https", _ -> Some normalized
            | _ -> None

/// The one-call embed render seam: the `src` to emit (or `None` to omit the
/// attribute), plus the attributes that record a refusal in the document.
///
/// Two gates in order, exactly as `sanitizeUrlForEgress` runs them: the scheme
/// floor above says what the URL may BE, then the destination policy says where
/// it may GO — under `EgressClass.Embed`, never `Media`.
let sanitizeEmbedSrcForEgress (policy: EgressPolicy) (url: string) : string option * (string * string) list =
    match sanitizeEmbedSrc url with
    | None -> None, [ egressRefusalAttribute, EgressClass.name EgressClass.Embed + ":unsafe-url" ]
    | Some safe ->
        match checkDestination policy EgressClass.Embed safe with
        | EgressVerdict.Allowed emitted -> Some emitted, []
        | refused ->
            None,
            (match egressRefusalMarker refused with
             | Some kv -> [ kv ]
             | None -> [])

// ─── Shipped policies ──────────────────────────────────────────────────────

/// Deny every destination that leaves the origin.
///
/// THE DEFAULT FOR A DECODED (WIRE) TREE. An emission cannot declare its own
/// egress, so absent a host's declaration it gets none — the same
/// default-deny-then-reach-for-permissive-by-name inversion the dispatch gate
/// and the effect registry already take.
let denyNonLocalEgress: EgressPolicy =
    { Rules = []
      AllowAnyOrigin = false
      AllowLocal = true
      AllowNonNetwork = false }

/// Permit every destination.
///
/// The posture for a HAND-AUTHORED tree, where the author is the trust
/// boundary. Named rather than default so reaching it is a deliberate,
/// greppable act — the pattern every other gate inversion in this codebase
/// follows.
let permissiveEgress: EgressPolicy =
    { Rules = []
      AllowAnyOrigin = true
      AllowLocal = true
      AllowNonNetwork = true }

/// Declare an origin for a set of classes. An empty class list is taken as
/// EVERY class — the ergonomic reading of "allow this origin", distinct from
/// an `EgressRule` whose `Classes` is empty, which permits nothing. The two
/// readings are deliberately split across the constructor and the record: the
/// record is data and says exactly what it lists; the helper is a convenience
/// and says what a caller writing one line means.
let allowOrigin (origin: EgressOrigin) (classes: EgressClass list) (policy: EgressPolicy) : EgressPolicy =
    let classes = if List.isEmpty classes then EgressClass.all else classes

    { policy with
        Rules = policy.Rules @ [ { Origin = origin; Classes = classes } ] }

/// Whether a policy permits anything beyond its own origin. Cheap answer to the
/// question a manifest reader asks first.
let hasNonLocalEgress (policy: EgressPolicy) : bool =
    policy.AllowAnyOrigin
    || policy.AllowNonNetwork
    || policy.Rules |> List.exists (fun r -> not (List.isEmpty r.Classes))

// ─── Manifest projection ───────────────────────────────────────────────────

let private jsonEscape (s: string) : string =
    let sb = System.Text.StringBuilder(s.Length + 2)

    for ch in s do
        match ch with
        | '"' -> sb.Append "\\\"" |> ignore
        | '\\' -> sb.Append "\\\\" |> ignore
        | '\n' -> sb.Append "\\n" |> ignore
        | '\r' -> sb.Append "\\r" |> ignore
        | '\t' -> sb.Append "\\t" |> ignore
        | c when int c < 0x20 -> sb.Append("\\u").Append((int c).ToString("x4")) |> ignore
        | c -> sb.Append c |> ignore

    sb.ToString()

let private jsonString (s: string) : string = "\"" + jsonEscape s + "\""

/// Canonical, deterministic JSON projection of a policy — the field a
/// composition manifest carries so declared egress is reviewable and diffable
/// beside everything else the composition may do.
///
/// Deterministic by sorting: rules by `(match, origin)` and classes by their
/// wire order, so two runs over the same policy produce the same bytes and a
/// manifest diff shows a policy change rather than a list reshuffle.
///
/// **Encode only, and that is the design rather than an omission.** There is no
/// decoder here and there must not be one on this seam: a policy that can
/// arrive as data is a policy a hostile emission can widen, and an allowlist
/// that its own subject can edit is not an allowlist. A policy is constructed
/// by the HOST, in host code. This projection exists so a reviewer can read
/// what the host declared — not so a tree can declare it.
let encodeEgressPolicy (policy: EgressPolicy) : string =
    let ruleJson (r: EgressRule) =
        let matchKind, origin =
            match r.Origin with
            | ExactHost h -> "exact", normalizeHost h
            | HostSuffix s -> "suffix", normalizeHost s

        let classes =
            EgressClass.all
            |> List.filter (fun c -> List.contains c r.Classes)
            |> List.map (EgressClass.name >> jsonString)
            |> String.concat ","

        (matchKind, origin),
        sprintf "{\"classes\":[%s],\"match\":%s,\"origin\":%s}" classes (jsonString matchKind) (jsonString origin)

    let rules =
        policy.Rules
        |> List.map ruleJson
        |> List.sortBy fst
        |> List.map snd
        |> String.concat ","

    sprintf
        "{\"allowAnyOrigin\":%b,\"allowLocal\":%b,\"allowNonNetwork\":%b,\"rules\":[%s]}"
        policy.AllowAnyOrigin
        policy.AllowLocal
        policy.AllowNonNetwork
        rules

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
