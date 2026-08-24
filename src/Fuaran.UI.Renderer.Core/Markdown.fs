module Fuaran.UI.Renderer.Markdown

// ============================================================================
//  Fuaran — deterministic GFM markdown renderer (Phase 292).
//
//  ONE renderer, shared by every Fuaran host. This module lives in
//  `Fuaran.UI.Renderer.Core` (the FSharp.Core-only spine, Phase 138) so the
//  Fable client renderer (`Fuaran.UI.Renderer`) and the .NET server renderer
//  (`Fuaran.UI.Renderer.Server`) run *the same code* — F#-side SSR↔CSR parity
//  is therefore by construction, not by spec-matching. It replaces the old
//  split where the client used the npm `marked` library and the server used
//  Markdig (two different renders of the same node → hydration-mismatch risk).
//
//  TARGET: the GFM spec (github.github.com/gfm). Pragmatic bar — implement the
//  GFM feature set common-case correct; pathological CommonMark edge cases fall
//  to the escaped-literal fallback (nothing renders *wrong*, just plainly).
//
//  THE THREE BUCKETS (the contract — also pinned in fuaran-dotnet/docs/WIRE_FORMAT.md
//  §12 and fuaran-dotnet/docs/MARKDOWN.md):
//
//    IN  — CommonMark core: ATX + setext headings, paragraphs, emphasis/strong,
//          inline + fenced + indented code, blockquotes, ordered/unordered
//          lists, thematic breaks, inline AND reference links, images, `<url>`
//          autolinks, hard/soft breaks, HTML entities, backslash escapes; PLUS
//          GFM: tables, strikethrough, task-list items, bare-URL autolinks.
//
//    OUT — raw/inline HTML is ESCAPED, never passed through (consistent with
//          GFM's tagfilter; a security win — no raw-HTML injection surface).
//          Engine-dependent extensions — math (`$…$`/KaTeX), Mermaid — would
//          need host-specific render engines → different DOM per host → they
//          would BREAK the byte-parity this renderer exists to guarantee, so
//          they live as a `Custom` node / client-only post-hydration pass
//          (math: Phase 293), permanently. Not a TODO.
//
//    DEFERRED — demand-gated, escaped-literal until then: emoji shortcodes,
//          footnotes, heading auto-anchors, sub/superscript, definition lists.
//          The full named-HTML-entity table (only the common subset decodes
//          today). Adding any later is a graceful upgrade, not a break.
//
//  CANONICAL OUTPUT FORMAT (the cross-host byte-parity contract — TS + Python
//  reproduce this exactly; the §11.1-style gate diffs against it):
//    * Text escaping: `&` `<` `>` `"` only (cmark's set); `'` is left literal.
//    * Every top-level block emits its element followed by a single `\n`.
//    * Container blocks (blockquote, list, list item) wrap their rendered
//      children, each child already carrying its trailing `\n`.
//    * Fenced/indented code: `<pre><code[ class="language-LANG"]>` + escaped
//      content (always ending in `\n`) + `</code></pre>` — identical to the
//      Phase 290 `CodeBlock` node's deterministic `<pre><code>` shape.
//    * Tables route to the `fuaran-table` / `fuaran-table-header` /
//      `fuaran-table-row` / `fuaran-table-cell` class vocabulary — identical
//      to the `Table` NodeKind render (so a markdown table and a `Table` node
//      look the same and share its parity coverage).
//    * No regex anywhere (engine semantics differ across hosts → a parity
//      hazard); manual scanning only.
//
//  SANITIZATION: the renderer escapes by construction — every text run is
//  HTML-escaped, raw HTML never passes through, and every link/image URL goes
//  through the scheme floor. The `Sanitize.sanitizeMarkdownHtml`
//  contract (SANITIZATION.md) is still applied as defence-in-depth, but its
//  surface is now far smaller (no library output to police). See SANITIZATION.md.
//
//  DESTINATION POLICY (Phase 1032). The scheme floor answers "is this URL safe
//  to have"; it does not answer "is this destination one the composition
//  declared". `toHtmlWithEgress` consults a `Sanitize.EgressPolicy` for every
//  link (`EgressClass.Hyperlink`) and image (`EgressClass.Media`) destination,
//  and a refused one renders the Phase 1026 refusal shape — the inert
//  `about:blank#fuaran-egress-refused` href plus a `data-fuaran-egress-refused`
//  marker naming the class and the host, never the path or the query.
//
//  `toHtml` SURVIVES AS THE PERMISSIVE CASE — `toHtmlWithEgress
//  Sanitize.permissiveEgress`, byte-for-byte. Three reasons, and they are the
//  same reasons every other posture inversion here is reached BY NAME:
//    * the corpus is a cross-host byte-parity contract, so flipping the pure
//      function's default would rewrite existing fixtures in five hosts in one
//      act — a mass churn is exactly where a real divergence hides;
//    * `toHtml` is published surface on an Apache-2.0 package, and a host
//      author who wants the pure function should reach it deliberately rather
//      than meet a silent behaviour swap;
//    * keeping it named makes an unpolicied markdown render greppable, which is
//      the property the refusal shape exists to give.
//  What makes the guarantee AMBIENT is not this function's default but the
//  renderer call sites: the client tier, the SSR tier and the email projection
//  all pass their context's `EgressPolicy`, whose own default denies.
//
//  THE SCHEME FLOOR'S OWN ANSWER IS UNCHANGED. A URL the floor rejects
//  (`javascript:`, an unknown scheme, a protocol-relative reference) still
//  renders the bare `about:blank` it has rendered since Phase 292, with no
//  marker — see `markdownDestination` for why that is a decision rather than an
//  inconsistency.
// ============================================================================

open System
open System.Text

// `escapeHtml` / `toHtml` open with an `isNull` test on their `string` input —
// the same defence-in-depth floor `Sanitize.fs` documents (a hand-built or
// wire-decoded record can carry a null the type says cannot exist), which F# 10's
// nullness checker rejects on a non-nullable `string` (FS3261). The project-wide
// `<Nullable>disable</Nullable>` does not travel under Fable — there the ENTRY
// project's setting governs the whole source graph — so the suppression is
// file-scoped here. See the note in `Sanitize.fs`.
#nowarn "3261"

// ─── Fable-safe primitives ──────────────────────────────────────────────────
// (Avoid BCL surface Fable doesn't reliably cover — `String(char,int)` and
//  `Char.ConvertFromUtf32` — so the renderer transpiles cleanly to JS.)

/// `n` copies of char `c` as a string.
let private repeatChar (c: char) (n: int) : string =
    if n <= 0 then "" else String.replicate n (string c)

/// A Unicode code point as a string (UTF-16, surrogate-paired above the BMP).
let private fromCodePoint (cp: int) : string =
    if cp <= 0xFFFF then
        string (char cp)
    else
        let c = cp - 0x10000
        string (char (0xD800 + (c >>> 10))) + string (char (0xDC00 + (c &&& 0x3FF)))

// ─── HTML escaping ──────────────────────────────────────────────────────────

/// Escape a text run for HTML body / attribute context. Matches cmark's set:
/// `&` `<` `>` `"`. `'` is intentionally left literal (cmark does the same),
/// which keeps the output deterministic and minimal.
let escapeHtml (s: string) : string =
    if isNull s || s = "" then
        ""
    else
        let sb = StringBuilder(s.Length + 16)

        for ch in s do
            match ch with
            | '&' -> sb.Append "&amp;" |> ignore
            | '<' -> sb.Append "&lt;" |> ignore
            | '>' -> sb.Append "&gt;" |> ignore
            | '"' -> sb.Append "&quot;" |> ignore
            | c -> sb.Append c |> ignore

        sb.ToString()

// ─── Destination policy at the link / image seam ────────────────────────────

/// The `href` / `src` a markdown destination emits under `policy`, plus the
/// refusal markers to splice in as trailing attributes.
///
/// Three verdict groups, and the middle one is a deliberate decision rather
/// than an oversight:
///
///   * ALLOWED — the normalised URL, no marker. Identical to what
///     `Sanitize.sanitizeUrlOrBlank` returned before this seam existed, so a
///     permissive render is byte-for-byte what it always was.
///   * UNSAFE (the SCHEME FLOOR refused) — the bare `about:blank`, no marker.
///     The floor's answer is a different fact from a policy refusal: it says
///     "this URL is not safe to render at all", it has said it in that exact
///     spelling in every conformant host since Phase 292, and it is pinned by
///     the shared sanitization corpus. Re-spelling it here would churn that
///     corpus inside a change about EGRESS — mixing two decisions into one set
///     of bytes, which is where a genuine divergence hides. The floor is
///     unchanged; only the questions it never asked get the new shape.
///   * REFUSED BY POLICY — the Phase 1026 refusal: the inert
///     `about:blank#fuaran-egress-refused` plus a marker naming the class and,
///     where there is one, the host. Never the path or the query: the query
///     string of a refused exfiltration attempt is the payload itself.
let private markdownDestination
    (policy: Sanitize.EgressPolicy)
    (cls: Sanitize.EgressClass)
    (url: string)
    : string * (string * string) list =
    match Sanitize.checkDestination policy cls url with
    | Sanitize.EgressVerdict.Allowed safe -> safe, []
    | Sanitize.EgressVerdict.UnsafeUrl -> "about:blank", []
    | refused ->
        Sanitize.egressRefusalUrl,
        (match Sanitize.egressRefusalMarker refused with
         | Some kv -> [ kv ]
         | None -> [])

/// Render refusal markers as trailing HTML attributes. Emitted LAST on the
/// element so an adopting host's diff against the pre-1032 bytes is a pure
/// suffix — every attribute that was there is still where it was.
let private egressAttrs (markers: (string * string) list) : string =
    let sb = StringBuilder()

    for (k, v) in markers do
        sb.Append(' ').Append(k).Append("=\"").Append(escapeHtml v).Append('"')
        |> ignore

    sb.ToString()

// ─── Entity decoding (common subset; the rest is DEFERRED) ──────────────────

/// The common named-entity subset we decode to their characters before
/// re-escaping. GFM decodes the full HTML5 named-entity table (~2000 names);
/// that table is DEFERRED — names outside this set pass through literally
/// (and so render escaped, e.g. `&copyright;` → `&amp;copyright;`).
let private namedEntities =
    [ "amp", "&"
      "lt", "<"
      "gt", ">"
      "quot", "\""
      "apos", "'"
      "nbsp", " "
      "copy", "©"
      "reg", "®"
      "trade", "™"
      "hellip", "…"
      "mdash", "—"
      "ndash", "–"
      "lsquo", "‘"
      "rsquo", "’"
      "ldquo", "“"
      "rdquo", "”"
      "deg", "°"
      "plusmn", "±"
      "times", "×"
      "divide", "÷"
      "frac12", "½"
      "frac14", "¼"
      "frac34", "¾"
      "sup2", "²"
      "sup3", "³"
      "middot", "·"
      "bull", "•"
      "dagger", "†"
      "euro", "€"
      "pound", "£"
      "cent", "¢"
      "yen", "¥"
      "sect", "§"
      "para", "¶" ]
    |> Map.ofList

/// Try to decode an entity reference starting at `text[i]` (which is `&`).
/// Returns `Some (decodedChars, nextIndex)` or `None` (leave the `&` literal).
let private tryDecodeEntity (text: string) (i: int) : (string * int) option =
    // i points at '&'. Find the ';'.
    let semi = text.IndexOf(';', i)

    if semi < 0 || semi = i + 1 then
        None
    else
        let body = text.Substring(i + 1, semi - i - 1)

        if body.StartsWith "#" then
            // Numeric: &#NN; (decimal) or &#xHH; / &#XHH; (hex).
            let isHex = body.Length > 1 && (body[1] = 'x' || body[1] = 'X')
            let digits = if isHex then body.Substring 2 else body.Substring 1

            if digits = "" then
                None
            else
                let mutable code = 0
                let mutable ok = true

                for c in digits do
                    if isHex then
                        if c >= '0' && c <= '9' then
                            code <- code * 16 + int c - int '0'
                        elif c >= 'a' && c <= 'f' then
                            code <- code * 16 + int c - int 'a' + 10
                        elif c >= 'A' && c <= 'F' then
                            code <- code * 16 + int c - int 'A' + 10
                        else
                            ok <- false
                    else if c >= '0' && c <= '9' then
                        code <- code * 10 + int c - int '0'
                    else
                        ok <- false

                if not ok then
                    None
                else
                    // CommonMark: invalid / null code points map to U+FFFD.
                    let cp = if code = 0 || code > 0x10FFFF then 0xFFFD else code
                    Some(fromCodePoint cp, semi + 1)
        else
            match Map.tryFind body namedEntities with
            | Some s -> Some(s, semi + 1)
            | None -> None

// ─── Inline AST ─────────────────────────────────────────────────────────────

type private Inline =
    | Text of string // literal text — escaped on render
    | Raw of string // verbatim HTML — already safe / escaped, emitted as-is
    | Emphasis of Inline list
    | Strong of Inline list
    | Strikethrough of Inline list
    | SoftBreak
    | HardBreak

let private isAsciiPunct (c: char) : bool =
    (c >= '!' && c <= '/')
    || (c >= ':' && c <= '@')
    || (c >= '[' && c <= '`')
    || (c >= '{' && c <= '~')

// Explicit whitespace/digit classes (NOT Char.IsWhiteSpace / Char.IsDigit) so
// F#, TS, and Python classify identically — the BCL/JS/Python Unicode sets
// differ at the edges (NEL, BOM, Unicode digits), which would be a parity
// hazard. This fixed set is the cross-host contract.
let private isUnicodeWhitespace (c: char) : bool =
    // space (32) + tab/LF/VT/FF/CR (9-13) — the cross-host whitespace set.
    let n = int c in
    c = ' ' || (n >= 9 && n <= 13)

let private isDigit (c: char) : bool = c >= '0' && c <= '9'

// ASCII-only case fold (NOT Char.ToLowerInvariant) so F#, TS, and Python case-fold
// reference labels identically — the BCL invariant-culture table and JS
// String.toLowerCase disagree at edge code points (Turkish-İ-class, certain
// Greek/Cyrillic), which would resolve a reference link on one host and not
// another → divergent HTML. ASCII A–Z → a–z is the cross-host contract (Phase 299).
let private asciiLower (c: char) : char =
    if c >= 'A' && c <= 'Z' then char (int c + 32) else c

// ─── Reference-link definitions ─────────────────────────────────────────────

/// Normalise a link label for matching: trim, collapse internal whitespace,
/// lowercase (case-insensitive, per CommonMark).
let private normLabel (s: string) : string =
    let sb = StringBuilder(s.Length)
    let mutable inWs = false

    for ch in s.Trim() do
        if isUnicodeWhitespace ch then
            inWs <- true
        else
            if inWs then
                sb.Append ' ' |> ignore

            inWs <- false
            sb.Append(asciiLower ch) |> ignore

    sb.ToString()

// ─── Inline tokenizer + delimiter stack ─────────────────────────────────────

type private Delim =
    { Char: char
      mutable Count: int
      CanOpen: bool
      CanClose: bool
      mutable Active: bool
      // index into the token ResizeArray
      Pos: int }

type private Tok =
    | TNode of Inline
    | TDelim of Delim

/// Scan a backtick code span starting at `text[i]` (a backtick). Returns
/// `Some (Inline, nextIndex)` or `None` (no closing run → treat ticks literal).
let private scanCodeSpan (text: string) (i: int) : (Inline * int) option =
    let n = text.Length
    // Count opening backticks.
    let mutable j = i

    while j < n && text[j] = '`' do
        j <- j + 1

    let openLen = j - i
    // Find a closing run of EXACTLY openLen backticks.
    let mutable k = j
    let mutable closeStart = -1

    while k < n && closeStart < 0 do
        if text[k] = '`' then
            let mutable m = k

            while m < n && text[m] = '`' do
                m <- m + 1

            if m - k = openLen then
                closeStart <- k

                k <- m
            else
                k <- m
        else
            k <- k + 1

    if closeStart < 0 then
        None
    else
        let raw = text.Substring(j, closeStart - j)
        // CommonMark code-span content normalisation: line endings → space,
        // and a single leading+trailing space is stripped iff the content is
        // not all spaces and has both.
        let collapsed = raw.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ')

        let trimmed =
            if
                collapsed.Length >= 2
                && collapsed[0] = ' '
                && collapsed[collapsed.Length - 1] = ' '
                && collapsed.Trim() <> ""
            then
                collapsed.Substring(1, collapsed.Length - 2)
            else
                collapsed

        Some(Raw("<code>" + escapeHtml trimmed + "</code>"), closeStart + openLen)

/// Is `c` a URL-ish scheme char?
let private isSchemeChar (c: char) : bool =
    (c >= 'a' && c <= 'z')
    || (c >= 'A' && c <= 'Z')
    || (c >= '0' && c <= '9')
    || c = '+'
    || c = '.'
    || c = '-'

/// Try an autolink `<scheme:...>` or `<email>` at `text[i]` (`<`).
let private scanAutolink (policy: Sanitize.EgressPolicy) (text: string) (i: int) : (Inline * int) option =
    let close = text.IndexOf('>', i)

    if close < 0 then
        None
    else
        let body = text.Substring(i + 1, close - i - 1)

        if body = "" || body.Contains " " || body.Contains "<" then
            None
        else
            // URI autolink: scheme:rest where scheme is 2..32 chars.
            let colon = body.IndexOf ':'

            let looksUri =
                colon >= 2
                && colon <= 32
                && (let mutable ok = true

                    for idx in 0 .. colon - 1 do
                        if not (isSchemeChar body[idx]) then
                            ok <- false

                    ok && (let c0 = body[0] in (c0 >= 'a' && c0 <= 'z') || (c0 >= 'A' && c0 <= 'Z')))

            let looksEmail =
                not looksUri
                && body.Contains "@"
                && not (body.Contains ":")
                && body.IndexOf '@' > 0

            if looksUri then
                let safe, markers = markdownDestination policy Sanitize.EgressClass.Hyperlink body

                Some(
                    Raw(
                        "<a href=\""
                        + escapeHtml safe
                        + "\""
                        + egressAttrs markers
                        + ">"
                        + escapeHtml body
                        + "</a>"
                    ),
                    close + 1
                )
            elif looksEmail then
                // An email autolink has no URL of its own — the `mailto:` is
                // the renderer's, so the policy is asked about the destination
                // the renderer is about to emit. On acceptance the ORIGINAL
                // bytes are emitted rather than the normalised form, so a
                // permissive render is unchanged to the byte.
                match Sanitize.checkDestination policy Sanitize.EgressClass.Hyperlink ("mailto:" + body) with
                | Sanitize.EgressVerdict.Allowed _ ->
                    Some(Raw("<a href=\"mailto:" + escapeHtml body + "\">" + escapeHtml body + "</a>"), close + 1)
                | refused ->
                    let markers =
                        match Sanitize.egressRefusalMarker refused with
                        | Some kv -> [ kv ]
                        | None -> []

                    Some(
                        Raw(
                            "<a href=\""
                            + escapeHtml Sanitize.egressRefusalUrl
                            + "\""
                            + egressAttrs markers
                            + ">"
                            + escapeHtml body
                            + "</a>"
                        ),
                        close + 1
                    )
            else
                None

/// Parse a link/image destination + optional title starting just after the
/// opening `(`. Returns `Some (url, titleOpt, indexAfterCloseParen)` or None.
let private scanInlineDestination (text: string) (start: int) : (string * string option * int) option =
    let n = text.Length
    let mutable i = start

    while i < n && (text[i] = ' ' || text[i] = '\n' || text[i] = '\t') do
        i <- i + 1

    // Destination: <...> form or a run of non-space chars (balanced parens).
    let mutable url = ""
    let mutable ok = true

    if i < n && text[i] = '<' then
        let close = text.IndexOf('>', i)

        if close < 0 then
            ok <- false
        else
            url <- text.Substring(i + 1, close - i - 1)
            i <- close + 1
    else
        let mutable depth = 0
        let sb = StringBuilder()
        let mutable go = true

        while go && i < n do
            let c = text[i]

            if c = ' ' || c = '\t' || c = '\n' then
                go <- false
            elif c = '(' then
                depth <- depth + 1
                sb.Append c |> ignore
                i <- i + 1
            elif c = ')' then
                if depth = 0 then
                    go <- false
                else
                    depth <- depth - 1
                    sb.Append c |> ignore
                    i <- i + 1
            elif c = '\\' && i + 1 < n && isAsciiPunct text[i + 1] then
                sb.Append text[i + 1] |> ignore
                i <- i + 2
            else
                sb.Append c |> ignore
                i <- i + 1

        url <- sb.ToString()

    // Optional whitespace then title in "...", '...', or (...).
    let mutable title = None

    if ok then
        let mutable j = i

        while j < n && (text[j] = ' ' || text[j] = '\t' || text[j] = '\n') do
            j <- j + 1

        if j < n && (text[j] = '"' || text[j] = '\'') then
            let q = text[j]
            let tClose = text.IndexOf(q, j + 1)

            if tClose >= 0 then
                title <- Some(text.Substring(j + 1, tClose - j - 1))
                i <- tClose + 1

                while i < n && (text[i] = ' ' || text[i] = '\t' || text[i] = '\n') do
                    i <- i + 1

    if not ok then None
    elif i < n && text[i] = ')' then Some(url, title, i + 1)
    else None

// Forward reference: parseInlines is recursive (link text is parsed as inlines).
// Declared via a mutable ref so the tokenizer can call back into it.
let mutable private parseInlinesRef
    : (Sanitize.EgressPolicy -> Map<string, string * string option> -> string -> Inline list) =
    fun _ _ _ -> []

/// Find the index of the matching `]` for a `[` at `open0`, honouring
/// backslash escapes and nested brackets. Returns -1 if unbalanced.
let private matchBracket (text: string) (open0: int) : int =
    let n = text.Length
    let mutable i = open0 + 1
    let mutable depth = 1
    let mutable result = -1

    while i < n && result < 0 do
        let c = text[i]

        if c = '\\' && i + 1 < n then
            i <- i + 2
        elif c = '`' then
            // Skip code spans so brackets inside them don't count.
            match scanCodeSpan text i with
            | Some(_, nxt) -> i <- nxt
            | None -> i <- i + 1
        elif c = '[' then
            depth <- depth + 1
            i <- i + 1
        elif c = ']' then
            depth <- depth - 1
            if depth = 0 then result <- i else i <- i + 1
        else
            i <- i + 1

    result

/// Build a link/image inline from already-resolved parts. `isImage` chooses
/// `<img …/>` vs `<a …>…</a>`. The label inlines are rendered for the link
/// body; for an image the alt text is the plain-text flattening.
let rec private renderInlinesImpl (nodes: Inline list) : string =
    let sb = StringBuilder()

    for node in nodes do
        match node with
        | Text t -> sb.Append(escapeHtml t) |> ignore
        | Raw h -> sb.Append h |> ignore
        | Emphasis inner -> sb.Append("<em>").Append(renderInlinesImpl inner).Append("</em>") |> ignore
        | Strong inner ->
            sb.Append("<strong>").Append(renderInlinesImpl inner).Append("</strong>")
            |> ignore
        | Strikethrough inner -> sb.Append("<del>").Append(renderInlinesImpl inner).Append("</del>") |> ignore
        | SoftBreak -> sb.Append("\n") |> ignore
        | HardBreak -> sb.Append("<br />\n") |> ignore

    sb.ToString()

/// Flatten inlines to plain text (for image alt attributes).
let rec private plainText (nodes: Inline list) : string =
    let sb = StringBuilder()

    for node in nodes do
        match node with
        | Text t -> sb.Append t |> ignore
        | Raw _ -> () // code spans / nested anchors contribute no alt text here
        | Emphasis inner
        | Strong inner
        | Strikethrough inner -> sb.Append(plainText inner) |> ignore
        | SoftBreak
        | HardBreak -> sb.Append " " |> ignore

    sb.ToString()

/// Detect a GFM bare-URL autolink (`http://`, `https://`, `www.`) at a word
/// boundary starting at `i`. Returns `Some (Inline, nextIndex)` or None.
let private scanBareAutolink (policy: Sanitize.EgressPolicy) (text: string) (i: int) : (Inline * int) option =
    let n = text.Length

    let starts (p: string) =
        i + p.Length <= n && text.Substring(i, p.Length) = p

    let scheme =
        if starts "https://" then Some "https://"
        elif starts "http://" then Some "http://"
        elif starts "www." then Some "www."
        else None

    match scheme with
    | None -> None
    | Some _ ->
        // Consume until whitespace or a `<`.
        let mutable j = i

        while j < n && not (isUnicodeWhitespace text[j]) && text[j] <> '<' do
            j <- j + 1

        // Trim trailing punctuation that is unlikely to be part of the URL.
        let isTrailingPunct c =
            c = '.'
            || c = ','
            || c = ';'
            || c = ':'
            || c = '!'
            || c = '?'
            || c = ')'
            || c = '"'
            || c = '\''

        while j > i && isTrailingPunct text[j - 1] do
            j <- j - 1

        if j <= i + 4 then
            None
        else
            let raw = text.Substring(i, j - i)
            let href = if raw.StartsWith "www." then "http://" + raw else raw
            let safe, markers = markdownDestination policy Sanitize.EgressClass.Hyperlink href

            Some(
                Raw(
                    "<a href=\""
                    + escapeHtml safe
                    + "\""
                    + egressAttrs markers
                    + ">"
                    + escapeHtml raw
                    + "</a>"
                ),
                j
            )

/// Tokenize an inline string into a token list, resolving code spans,
/// autolinks, links, images, escapes, entities, and breaks; delimiter runs
/// of `* _ ~` become `TDelim` for the emphasis pass.
let private tokenize
    (policy: Sanitize.EgressPolicy)
    (refs: Map<string, string * string option>)
    (text: string)
    : ResizeArray<Tok> =
    let toks = ResizeArray<Tok>()
    let n = text.Length
    let mutable i = 0
    // Accumulate literal text; flush as a Text node when we hit a special char.
    let pending = StringBuilder()

    let flush () =
        if pending.Length > 0 then
            toks.Add(TNode(Text(pending.ToString())))
            pending.Clear() |> ignore

    let prevChar () = if i = 0 then ' ' else text[i - 1]

    while i < n do
        let c = text[i]

        match c with
        | '\\' when i + 1 < n && text[i + 1] = '\n' ->
            // Backslash at line end → hard break.
            flush ()
            toks.Add(TNode HardBreak)
            i <- i + 2
        | '\\' when i + 1 < n && isAsciiPunct text[i + 1] ->
            pending.Append text[i + 1] |> ignore
            i <- i + 2
        | '`' ->
            match scanCodeSpan text i with
            | Some(node, nxt) ->
                flush ()
                toks.Add(TNode node)
                i <- nxt
            | None ->
                pending.Append c |> ignore
                i <- i + 1
        | '&' ->
            match tryDecodeEntity text i with
            | Some(s, nxt) ->
                pending.Append s |> ignore
                i <- nxt
            | None ->
                pending.Append c |> ignore
                i <- i + 1
        | '<' ->
            match scanAutolink policy text i with
            | Some(node, nxt) ->
                flush ()
                toks.Add(TNode node)
                i <- nxt
            | None ->
                // Raw inline HTML is OUT — escape the `<` and move on.
                pending.Append c |> ignore
                i <- i + 1
        | '!' when i + 1 < n && text[i + 1] = '[' ->
            // Image.
            let close = matchBracket text (i + 1)

            match close with
            | -1 ->
                pending.Append c |> ignore
                i <- i + 1
            | _ ->
                let labelText = text.Substring(i + 2, close - (i + 2))

                if close + 1 < n && text[close + 1] = '(' then
                    match scanInlineDestination text (close + 2) with
                    | Some(url, titleOpt, after) ->
                        flush ()
                        let alt = plainText (parseInlinesRef policy refs labelText)
                        let safe, markers = markdownDestination policy Sanitize.EgressClass.Media url

                        let titleAttr =
                            match titleOpt with
                            | Some t -> " title=\"" + escapeHtml t + "\""
                            | None -> ""

                        toks.Add(
                            TNode(
                                Raw(
                                    "<img src=\""
                                    + escapeHtml safe
                                    + "\" alt=\""
                                    + escapeHtml alt
                                    + "\""
                                    + titleAttr
                                    + egressAttrs markers
                                    + " />"
                                )
                            )
                        )

                        i <- after
                    | None ->
                        pending.Append c |> ignore
                        i <- i + 1
                else
                    // Reference image: ![alt][ref] or ![ref].
                    let refLabel, consumedTo =
                        if close + 1 < n && text[close + 1] = '[' then
                            let r2 = matchBracket text (close + 1)

                            if r2 > 0 then
                                let inner = text.Substring(close + 2, r2 - (close + 2))
                                (if inner.Trim() = "" then labelText else inner), r2 + 1
                            else
                                labelText, close + 1
                        else
                            labelText, close + 1

                    match Map.tryFind (normLabel refLabel) refs with
                    | Some(url, titleOpt) ->
                        flush ()
                        let alt = plainText (parseInlinesRef policy refs labelText)
                        let safe, markers = markdownDestination policy Sanitize.EgressClass.Media url

                        let titleAttr =
                            match titleOpt with
                            | Some t -> " title=\"" + escapeHtml t + "\""
                            | None -> ""

                        toks.Add(
                            TNode(
                                Raw(
                                    "<img src=\""
                                    + escapeHtml safe
                                    + "\" alt=\""
                                    + escapeHtml alt
                                    + "\""
                                    + titleAttr
                                    + egressAttrs markers
                                    + " />"
                                )
                            )
                        )

                        i <- consumedTo
                    | None ->
                        pending.Append c |> ignore
                        i <- i + 1
        | '[' ->
            let close = matchBracket text i

            match close with
            | -1 ->
                pending.Append c |> ignore
                i <- i + 1
            | _ ->
                let labelText = text.Substring(i + 1, close - (i + 1))

                let makeLink url titleOpt =
                    flush ()
                    let inner = renderInlinesImpl (parseInlinesRef policy refs labelText)
                    let safe, markers = markdownDestination policy Sanitize.EgressClass.Hyperlink url

                    let titleAttr =
                        match titleOpt with
                        | Some t -> " title=\"" + escapeHtml t + "\""
                        | None -> ""

                    toks.Add(
                        TNode(
                            Raw(
                                "<a href=\""
                                + escapeHtml safe
                                + "\""
                                + titleAttr
                                + egressAttrs markers
                                + ">"
                                + inner
                                + "</a>"
                            )
                        )
                    )

                if close + 1 < n && text[close + 1] = '(' then
                    match scanInlineDestination text (close + 2) with
                    | Some(url, titleOpt, after) ->
                        makeLink url titleOpt
                        i <- after
                    | None ->
                        pending.Append c |> ignore
                        i <- i + 1
                else
                    // Reference link: [text][ref], [text][], or [ref].
                    let refLabel, consumedTo =
                        if close + 1 < n && text[close + 1] = '[' then
                            let r2 = matchBracket text (close + 1)

                            if r2 > 0 then
                                let inner = text.Substring(close + 2, r2 - (close + 2))
                                (if inner.Trim() = "" then labelText else inner), r2 + 1
                            else
                                labelText, close + 1
                        else
                            labelText, close + 1

                    match Map.tryFind (normLabel refLabel) refs with
                    | Some(url, titleOpt) ->
                        makeLink url titleOpt
                        i <- consumedTo
                    | None ->
                        pending.Append c |> ignore
                        i <- i + 1
        | '*'
        | '_'
        | '~' ->
            // Build a delimiter run.
            let mutable j = i

            while j < n && text[j] = c do
                j <- j + 1

            let runLen = j - i
            let before = prevChar ()
            let after = if j < n then text[j] else ' '
            let beforeWs = isUnicodeWhitespace before
            let afterWs = isUnicodeWhitespace after
            let beforePunct = isAsciiPunct before
            let afterPunct = isAsciiPunct after
            let leftFlank = not afterWs && (not afterPunct || beforeWs || beforePunct)
            let rightFlank = not beforeWs && (not beforePunct || afterWs || afterPunct)

            let canOpen, canClose =
                if c = '_' then
                    (leftFlank && (not rightFlank || beforePunct)), (rightFlank && (not leftFlank || afterPunct))
                else
                    leftFlank, rightFlank

            flush ()

            if c = '~' && runLen <> 2 then
                // Only `~~` participates in strikethrough; other runs literal.
                toks.Add(TNode(Text(repeatChar c runLen)))
            else
                toks.Add(
                    TDelim
                        { Char = c
                          Count = runLen
                          CanOpen = canOpen
                          CanClose = canClose
                          Active = true
                          Pos = toks.Count }
                )

            i <- j
        | '\n' ->
            // Hard break if the line ended with ≥2 spaces; else soft break.
            // Either way the trailing spaces are stripped (CommonMark).
            let s = pending.ToString()
            let trimmedEnd = s.TrimEnd ' '
            let hard = s.Length - trimmedEnd.Length >= 2
            pending.Clear().Append trimmedEnd |> ignore
            flush ()
            toks.Add(TNode(if hard then HardBreak else SoftBreak))
            i <- i + 1
        | ('h' | 'w') when
            (let p = prevChar () in i = 0 || isUnicodeWhitespace p || p = '(' || p = '*' || p = '_' || p = '~')
            ->
            // GFM bare-URL autolink (http(s):// / www.) at a word boundary.
            match scanBareAutolink policy text i with
            | Some(node, nxt) ->
                flush ()
                toks.Add(TNode node)
                i <- nxt
            | None ->
                pending.Append c |> ignore
                i <- i + 1
        | _ ->
            pending.Append c |> ignore
            i <- i + 1

    flush ()
    toks

/// Run the CommonMark emphasis algorithm (simplified) over the token list,
/// collapsing matched `*`/`_` runs into Emphasis/Strong and `~~` into
/// Strikethrough. Unmatched delimiters degrade to literal text.
let private processEmphasis (toks: ResizeArray<Tok>) : Inline list =
    // Walk closers left→right; for each, find the nearest preceding opener of
    // the same char that can open. Wrap the nodes between them.
    let mutable closerIdx = 0

    while closerIdx < toks.Count do
        match toks[closerIdx] with
        | TDelim closer when closer.CanClose && closer.Active && closer.Count > 0 ->
            // Search backwards for a matching opener.
            let mutable openerIdx = closerIdx - 1
            let mutable found = -1

            while openerIdx >= 0 && found < 0 do
                match toks[openerIdx] with
                | TDelim opener when opener.Char = closer.Char && opener.CanOpen && opener.Active && opener.Count > 0 ->
                    // Rule-of-3: if either can both open and close, the sum of
                    // the two run lengths must not be a multiple of 3 unless
                    // both are multiples of 3. (CommonMark.)
                    let sumOk =
                        if (opener.CanClose || closer.CanOpen) && closer.Char <> '~' then
                            not ((opener.Count + closer.Count) % 3 = 0)
                            || (opener.Count % 3 = 0 && closer.Count % 3 = 0)
                        else
                            true

                    if sumOk then
                        found <- openerIdx
                    else
                        openerIdx <- openerIdx - 1
                | _ -> openerIdx <- openerIdx - 1

            if found < 0 then
                // No opener; if it also can't open, deactivate to literal.
                if not closer.CanOpen then
                    closer.Active <- false
                    toks[closerIdx] <- TNode(Text(repeatChar closer.Char closer.Count))

                closerIdx <- closerIdx + 1
            else
                let opener =
                    match toks[found] with
                    | TDelim d -> d
                    | _ -> failwith "unreachable"

                let useCount =
                    if closer.Char = '~' then 2
                    elif opener.Count >= 2 && closer.Count >= 2 then 2
                    else 1

                // Collect the inner nodes between opener and closer.
                let inner = ResizeArray<Inline>()

                for k in found + 1 .. closerIdx - 1 do
                    match toks[k] with
                    | TNode nd -> inner.Add nd
                    | TDelim d when d.Count > 0 -> inner.Add(Text(repeatChar d.Char d.Count))
                    | TDelim _ -> ()

                let wrapped =
                    if closer.Char = '~' then Strikethrough(List.ofSeq inner)
                    elif useCount = 2 then Strong(List.ofSeq inner)
                    else Emphasis(List.ofSeq inner)

                // Remove the consumed tokens (found+1 .. closerIdx) and replace
                // with the wrapped node; decrement delimiter counts.
                opener.Count <- opener.Count - useCount
                closer.Count <- closer.Count - useCount

                // Rebuild: keep [0..found] (opener possibly drained), insert
                // wrapped, then keep closer (possibly drained) .. end.
                let rebuilt = ResizeArray<Tok>()

                for k in 0 .. found - 1 do
                    rebuilt.Add toks[k]

                if opener.Count > 0 then
                    rebuilt.Add(TNode(Text(repeatChar opener.Char opener.Count)))

                rebuilt.Add(TNode wrapped)

                if closer.Count > 0 then
                    // Keep the closer as an active delimiter for further matching.
                    closer.Active <- closer.Count > 0
                    rebuilt.Add(TNode(Text(repeatChar closer.Char closer.Count)))

                for k in closerIdx + 1 .. toks.Count - 1 do
                    rebuilt.Add toks[k]

                toks.Clear()
                toks.AddRange rebuilt
                // Re-scan from the opener position (positions shifted).
                closerIdx <- found
        | _ -> closerIdx <- closerIdx + 1

    // Flatten remaining tokens to Inline list (leftover delimiters → text).
    [ for t in toks do
          match t with
          | TNode nd -> yield nd
          | TDelim d when d.Count > 0 -> yield Text(repeatChar d.Char d.Count)
          | TDelim _ -> () ]

/// Public inline parser: string → Inline list. Used recursively for link text.
let private parseInlines
    (policy: Sanitize.EgressPolicy)
    (refs: Map<string, string * string option>)
    (text: string)
    : Inline list =
    text |> tokenize policy refs |> processEmphasis

// Wire the forward reference now that parseInlines exists.
parseInlinesRef <- parseInlines

let private renderInline
    (policy: Sanitize.EgressPolicy)
    (refs: Map<string, string * string option>)
    (text: string)
    : string =
    parseInlines policy refs text |> renderInlinesImpl

// ─── Block parsing ──────────────────────────────────────────────────────────

type private Align =
    | None_
    | Left
    | Center
    | Right

type private Block =
    | Heading of level: int * text: string
    | Paragraph of text: string
    | ThematicBreak
    | FencedCode of lang: string * content: string
    | IndentedCode of content: string
    | BlockQuote of Block list
    | BulletList of tight: bool * items: ListItem list
    | OrderedList of start: int * tight: bool * items: ListItem list
    | Table of headers: string list * aligns: Align list * rows: string list list

and private ListItem =
    { Task: bool option // None = not a task item; Some checked
      Blocks: Block list }

/// Count leading spaces (tabs expand to 4).
let private leadingIndent (s: string) : int =
    let mutable n = 0
    let mutable i = 0
    let mutable go = true

    while go && i < s.Length do
        if s[i] = ' ' then
            n <- n + 1
            i <- i + 1
        elif s[i] = '\t' then
            n <- n + (4 - (n % 4))
            i <- i + 1
        else
            go <- false

    n

let private isBlank (s: string) : bool = s.Trim() = ""

/// Is this line a thematic break? (≥3 of `-`, `*`, or `_`, optionally spaced.)
let private isThematicBreak (line: string) : bool =
    let t = line.Trim().Replace(" ", "").Replace("\t", "")

    t.Length >= 3
    && (t |> Seq.forall (fun c -> c = '-')
        || t |> Seq.forall (fun c -> c = '*')
        || t |> Seq.forall (fun c -> c = '_'))

/// Try to parse an ATX heading. Returns Some (level, text).
let private tryAtxHeading (line: string) : (int * string) option =
    let t = line.TrimStart([| ' ' |])

    if leadingIndent line >= 4 then
        None
    else
        let mutable lvl = 0

        while lvl < t.Length && lvl < 7 && t[lvl] = '#' do
            lvl <- lvl + 1

        if lvl = 0 || lvl > 6 then
            None
        elif lvl < t.Length && t[lvl] <> ' ' && t[lvl] <> '\t' then
            None
        else
            // Strip optional closing run of #.
            let body = t.Substring(lvl).Trim()
            let stripped = body.TrimEnd('#').TrimEnd([| ' '; '\t' |])

            let final =
                if body <> "" && body |> Seq.forall (fun c -> c = '#') then
                    ""
                else
                    stripped

            Some(lvl, final)

/// A GFM table delimiter row: cells of `:?-+:?` separated by `|`.
let private parseAlignRow (line: string) : Align list option =
    let trimmed = line.Trim()

    if not (trimmed.Contains "-") then
        None
    else
        let body = trimmed.Trim('|')
        let cells = body.Split('|') |> Array.map (fun s -> s.Trim())

        if cells.Length = 0 then
            None
        else
            let mutable ok = true
            let aligns = ResizeArray<Align>()

            for cell in cells do
                let core = cell

                if core = "" then
                    ok <- false
                else
                    let left = core.StartsWith ":"
                    let right = core.EndsWith ":"
                    let dashes = core.Trim(':')

                    if dashes = "" || not (dashes |> Seq.forall (fun c -> c = '-')) then
                        ok <- false
                    else
                        aligns.Add(
                            match left, right with
                            | true, true -> Center
                            | true, false -> Left
                            | false, true -> Right
                            | false, false -> None_
                        )

            if ok then Some(List.ofSeq aligns) else None

/// Split a table row into cells on unescaped `|`, honouring `\|`.
let private splitTableRow (line: string) : string list =
    let t = line.Trim()
    let body = if t.StartsWith "|" then t.Substring 1 else t

    let body2 =
        if body.EndsWith "|" && not (body.EndsWith "\\|") then
            body.Substring(0, body.Length - 1)
        else
            body

    let cells = ResizeArray<string>()
    let sb = StringBuilder()
    let mutable i = 0

    while i < body2.Length do
        let c = body2[i]

        if c = '\\' && i + 1 < body2.Length && body2[i + 1] = '|' then
            sb.Append '|' |> ignore
            i <- i + 2
        elif c = '|' then
            cells.Add(sb.ToString().Trim())
            sb.Clear() |> ignore
            i <- i + 1
        else
            sb.Append c |> ignore
            i <- i + 1

    cells.Add(sb.ToString().Trim())
    List.ofSeq cells

/// Extract reference-link definitions from the line array, returning the map
/// and the residual lines (definitions removed). A definition is
/// `[label]: destination "optional title"` possibly spanning the title onto
/// the next line — we handle the single-line common case.
let private extractRefDefs (lines: string[]) : Map<string, string * string option> * string[] =
    let refs = System.Collections.Generic.Dictionary<string, string * string option>()
    let kept = ResizeArray<string>()

    for line in lines do
        let t = line.Trim()
        let mutable handled = false

        if t.StartsWith "[" && leadingIndent line < 4 then
            let close = t.IndexOf ']'

            if close > 1 && close + 1 < t.Length && t[close + 1] = ':' then
                let label = t.Substring(1, close - 1)
                let rest = t.Substring(close + 2).Trim()

                if rest <> "" && not (label.Contains "]") then
                    // destination = first token; optional title in quotes after.
                    let spaceIdx = rest.IndexOfAny([| ' '; '\t' |])

                    let url, titlePart =
                        if spaceIdx < 0 then
                            rest, ""
                        else
                            rest.Substring(0, spaceIdx), rest.Substring(spaceIdx).Trim()

                    let urlClean =
                        if url.StartsWith "<" && url.EndsWith ">" then
                            url.Substring(1, url.Length - 2)
                        else
                            url

                    let title =
                        if titlePart.Length >= 2 && (titlePart[0] = '"' || titlePart[0] = '\'') then
                            let q = titlePart[0]
                            let tc = titlePart.IndexOf(q, 1)

                            if tc > 0 then
                                Some(titlePart.Substring(1, tc - 1))
                            else
                                None
                        else
                            None

                    refs[normLabel label] <- (urlClean, title)
                    handled <- true

        if not handled then
            kept.Add line

    let map = refs |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq
    map, kept.ToArray()

/// Parse a sequence of lines into blocks. Recursive for blockquote / list item
/// contents. `refs` carries reference-link definitions for inline parsing.
let rec private parseBlocks (lines: string[]) : Block list =
    let blocks = ResizeArray<Block>()
    let n = lines.Length
    let mutable i = 0

    while i < n do
        let line = lines[i]

        if isBlank line then
            i <- i + 1
        elif isThematicBreak line && (tryAtxHeading line).IsNone then
            blocks.Add ThematicBreak
            i <- i + 1
        else
            match tryAtxHeading line with
            | Some(lvl, txt) ->
                blocks.Add(Heading(lvl, txt))
                i <- i + 1
            | None ->
                let indent = leadingIndent line
                let trimmedStart = line.TrimStart([| ' '; '\t' |])

                // Fenced code block.
                if trimmedStart.StartsWith "```" || trimmedStart.StartsWith "~~~" then
                    let fence = if trimmedStart.StartsWith "```" then "```" else "~~~"
                    let info = trimmedStart.Substring(3).Trim()
                    let lang = if info = "" then "" else info.Split([| ' '; '\t' |])[0]
                    let content = ResizeArray<string>()
                    let mutable j = i + 1
                    let mutable closed = false

                    while j < n && not closed do
                        let l = lines[j]

                        if l.TrimStart([| ' '; '\t' |]).StartsWith fence && l.Trim().TrimEnd(fence[0]) = "" then
                            closed <- true
                            j <- j + 1
                        else
                            // Strip up to `indent` leading spaces from content.
                            content.Add(l)
                            j <- j + 1

                    blocks.Add(FencedCode(lang, String.concat "\n" (List.ofSeq content)))
                    i <- j

                // Indented code block (≥4 spaces).
                elif indent >= 4 then
                    let content = ResizeArray<string>()
                    let mutable j = i

                    while j < n && (leadingIndent lines[j] >= 4 || isBlank lines[j]) do
                        let l = lines[j]
                        content.Add(if isBlank l then "" else l.Substring(min 4 (l.Length)))
                        j <- j + 1

                    // Trim trailing blank lines.
                    while content.Count > 0 && content[content.Count - 1] = "" do
                        content.RemoveAt(content.Count - 1)

                    blocks.Add(IndentedCode(String.concat "\n" (List.ofSeq content)))
                    i <- j

                // Blockquote.
                elif trimmedStart.StartsWith ">" then
                    let inner = ResizeArray<string>()
                    let mutable j = i

                    while j < n && lines[j].TrimStart([| ' '; '\t' |]).StartsWith ">" do
                        let l = lines[j].TrimStart([| ' '; '\t' |]).Substring 1
                        inner.Add(if l.StartsWith " " then l.Substring 1 else l)
                        j <- j + 1

                    blocks.Add(BlockQuote(parseBlocks (inner.ToArray())))
                    i <- j

                // GFM table: current line has `|` and the NEXT line is an
                // alignment row with matching column count.
                elif
                    line.Contains "|"
                    && i + 1 < n
                    && (parseAlignRow lines[i + 1]).IsSome
                    && (let a = (parseAlignRow lines[i + 1]).Value
                        a.Length = (splitTableRow line).Length)
                then
                    let headers = splitTableRow line
                    let aligns = (parseAlignRow lines[i + 1]).Value
                    let rows = ResizeArray<string list>()
                    let mutable j = i + 2

                    while j < n && not (isBlank lines[j]) && lines[j].Contains "|" do
                        rows.Add(splitTableRow lines[j])
                        j <- j + 1

                    blocks.Add(Table(headers, aligns, List.ofSeq rows))
                    i <- j

                // List (ordered or unordered).
                elif isListMarker trimmedStart then
                    let listBlock, next = parseList lines i
                    blocks.Add listBlock
                    i <- next

                // Setext heading: a paragraph line followed by `===` / `---`.
                else
                    let para = ResizeArray<string>()
                    let mutable j = i
                    let mutable stop = false
                    let mutable setext = 0

                    while j < n && not stop do
                        let l = lines[j]

                        if isBlank l then
                            stop <- true
                        elif
                            j > i
                            && leadingIndent l < 4
                            && (let tt = l.Trim() in

                                tt <> ""
                                && (tt |> Seq.forall (fun c -> c = '=') || tt |> Seq.forall (fun c -> c = '-')))
                        then
                            setext <- (if l.Trim()[0] = '=' then 1 else 2)
                            stop <- true
                            j <- j + 1
                        elif
                            j > i
                            && (isThematicBreak l
                                || (tryAtxHeading l).IsSome
                                || l.TrimStart([| ' ' |]).StartsWith ">"
                                || isListMarker (l.TrimStart([| ' '; '\t' |])))
                        then
                            // Interrupting construct ends the paragraph.
                            stop <- true
                        else
                            // Keep trailing spaces (significant for hard breaks);
                            // strip only leading indentation.
                            para.Add(l.TrimStart([| ' '; '\t' |]))
                            j <- j + 1

                    let text = (String.concat "\n" (List.ofSeq para)).TrimEnd([| ' '; '\t'; '\n' |])

                    if setext > 0 && text <> "" then
                        blocks.Add(Heading(setext, text))
                    elif text <> "" then
                        blocks.Add(Paragraph text)

                    i <- j

    List.ofSeq blocks

and private isListMarker (s: string) : bool =
    if s = "" then
        false
    elif
        (s[0] = '-' || s[0] = '*' || s[0] = '+')
        && s.Length >= 2
        && (s[1] = ' ' || s[1] = '\t')
    then
        true
    else
        // ordered: digits then '.' or ')' then space
        let mutable k = 0

        while k < s.Length && k < 9 && isDigit s[k] do
            k <- k + 1

        k > 0
        && k + 1 < s.Length
        && (s[k] = '.' || s[k] = ')')
        && (s[k + 1] = ' ' || s[k + 1] = '\t')

and private parseList (lines: string[]) (start: int) : Block * int =
    let n = lines.Length
    // Determine ordered vs bullet from the first marker.
    let first = lines[start].TrimStart([| ' '; '\t' |])
    let ordered = isDigit first[0]

    let startNum =
        if ordered then
            let mutable k = 0

            while k < first.Length && isDigit first[k] do
                k <- k + 1

            match Int32.TryParse(first.Substring(0, k)) with
            | true, v -> v
            | _ -> 1
        else
            1

    let items = ResizeArray<ListItem>()
    let mutable i = start
    let mutable tight = true
    let mutable sawBlankBetween = false

    let markerWidth (s: string) : int =
        // returns the content offset after the marker + its trailing space
        if not ordered then
            2
        else
            let mutable k = 0

            while k < s.Length && isDigit s[k] do
                k <- k + 1

            k + 2 // digits + delimiter + one space

    let mutable go = true

    while go && i < n do
        let raw = lines[i]
        let trimmed = raw.TrimStart([| ' '; '\t' |])
        let baseIndent = leadingIndent raw

        if isBlank raw then
            // Blank line: could be between items (loose) or end of list.
            // Look ahead: if the next non-blank continues the list, mark loose.
            sawBlankBetween <- true
            i <- i + 1
        elif isListMarker trimmed && (ordered = isDigit trimmed[0]) && baseIndent < 4 then
            if sawBlankBetween && items.Count > 0 then
                tight <- false

            sawBlankBetween <- false
            // Gather this item's lines: the marker line + subsequent lines
            // indented past the marker, until the next sibling marker or a
            // blank-then-dedent.
            let contentOffset = baseIndent + markerWidth trimmed
            let itemLines = ResizeArray<string>()
            // First line content after marker.
            let afterMarker =
                let mw = markerWidth trimmed
                if trimmed.Length > mw then trimmed.Substring mw else ""

            // Task-list check on the first content.
            let task, firstContent =
                if afterMarker.StartsWith "[ ]" then
                    Some false, afterMarker.Substring(3).TrimStart([| ' ' |])
                elif afterMarker.StartsWith "[x]" || afterMarker.StartsWith "[X]" then
                    Some true, afterMarker.Substring(3).TrimStart([| ' ' |])
                else
                    Option.None, afterMarker

            itemLines.Add firstContent
            i <- i + 1
            let mutable inItem = true

            while inItem && i < n do
                let l = lines[i]

                if isBlank l then
                    // tentatively part of item; decide when we see next content
                    itemLines.Add ""
                    i <- i + 1
                elif leadingIndent l >= contentOffset then
                    itemLines.Add(l.Substring(min contentOffset l.Length))
                    i <- i + 1
                elif isListMarker (l.TrimStart([| ' '; '\t' |])) && leadingIndent l < 4 then
                    inItem <- false
                elif leadingIndent l > 0 && not (isListMarker (l.TrimStart([| ' '; '\t' |]))) then
                    // lazy continuation
                    itemLines.Add(l.Trim())
                    i <- i + 1
                else
                    inItem <- false

            // Trim trailing blanks within the item.
            while itemLines.Count > 0 && itemLines[itemLines.Count - 1] = "" do
                itemLines.RemoveAt(itemLines.Count - 1)
                sawBlankBetween <- true

            // A blank line *inside* an item also makes the list loose.
            if itemLines |> Seq.exists (fun l -> l = "") then
                tight <- false

            let itemBlocks = parseBlocks (itemLines.ToArray())
            items.Add { Task = task; Blocks = itemBlocks }
        else
            go <- false

    let block =
        if ordered then
            OrderedList(startNum, tight, List.ofSeq items)
        else
            BulletList(tight, List.ofSeq items)

    block, i

// ─── Block rendering ────────────────────────────────────────────────────────

let private alignAttr (a: Align) : string =
    match a with
    | None_ -> ""
    | Left -> " align=\"left\""
    | Center -> " align=\"center\""
    | Right -> " align=\"right\""

let rec private renderBlocks
    (policy: Sanitize.EgressPolicy)
    (refs: Map<string, string * string option>)
    (blocks: Block list)
    : string =
    let sb = StringBuilder()

    for b in blocks do
        sb.Append(renderBlock policy refs b) |> ignore

    sb.ToString()

and private renderBlock
    (policy: Sanitize.EgressPolicy)
    (refs: Map<string, string * string option>)
    (b: Block)
    : string =
    match b with
    | ThematicBreak -> "<hr />\n"
    | Heading(lvl, txt) ->
        "<h"
        + string lvl
        + ">"
        + renderInline policy refs txt
        + "</h"
        + string lvl
        + ">\n"
    | Paragraph txt -> "<p>" + renderInline policy refs txt + "</p>\n"
    | FencedCode(lang, content) ->
        let cls =
            if lang = "" then
                ""
            else
                " class=\"language-" + escapeHtml lang + "\""

        "<pre><code" + cls + ">" + escapeHtml content + "\n</code></pre>\n"
    | IndentedCode content -> "<pre><code>" + escapeHtml content + "\n</code></pre>\n"
    | BlockQuote inner -> "<blockquote>\n" + renderBlocks policy refs inner + "</blockquote>\n"
    | Table(headers, aligns, rows) ->
        let sb = StringBuilder()
        sb.Append "<table class=\"fuaran-table\"><thead><tr>" |> ignore

        headers
        |> List.iteri (fun idx h ->
            let a = if idx < aligns.Length then aligns[idx] else None_

            sb
                .Append("<th class=\"fuaran-table-header\"")
                .Append(alignAttr a)
                .Append(">")
                .Append(renderInline policy refs h)
                .Append("</th>")
            |> ignore)

        sb.Append "</tr></thead><tbody>" |> ignore

        for row in rows do
            sb.Append "<tr class=\"fuaran-table-row\">" |> ignore

            for idx in 0 .. headers.Length - 1 do
                let cell = if idx < row.Length then row[idx] else ""
                let a = if idx < aligns.Length then aligns[idx] else None_

                sb
                    .Append("<td class=\"fuaran-table-cell\"")
                    .Append(alignAttr a)
                    .Append(">")
                    .Append(renderInline policy refs cell)
                    .Append("</td>")
                |> ignore

            sb.Append "</tr>" |> ignore

        sb.Append "</tbody></table>\n" |> ignore
        sb.ToString()
    | BulletList(tight, items) -> "<ul>\n" + renderItems policy refs tight items + "</ul>\n"
    | OrderedList(startNum, tight, items) ->
        let startAttr =
            if startNum = 1 then
                ""
            else
                " start=\"" + string startNum + "\""

        "<ol" + startAttr + ">\n" + renderItems policy refs tight items + "</ol>\n"

and private renderItems
    (policy: Sanitize.EgressPolicy)
    (refs: Map<string, string * string option>)
    (tight: bool)
    (items: ListItem list)
    : string =
    let sb = StringBuilder()

    for item in items do
        let checkbox =
            match item.Task with
            | Option.None -> ""
            | Some false -> "<input class=\"fuaran-task-checkbox\" disabled=\"\" type=\"checkbox\" /> "
            | Some true -> "<input class=\"fuaran-task-checkbox\" checked=\"\" disabled=\"\" type=\"checkbox\" /> "

        let liClass =
            if item.Task.IsSome then
                " class=\"fuaran-task-item\""
            else
                ""

        if tight then
            // Tight: render block contents inline (paragraphs unwrapped).
            let inner =
                item.Blocks
                |> List.map (fun blk ->
                    match blk with
                    | Paragraph txt -> renderInline policy refs txt
                    | other -> "\n" + renderBlock policy refs other)
                |> String.concat ""

            sb.Append("<li" + liClass + ">" + checkbox + inner + "</li>\n") |> ignore
        else
            sb.Append(
                "<li"
                + liClass
                + ">\n"
                + checkbox
                + renderBlocks policy refs item.Blocks
                + "</li>\n"
            )
            |> ignore

    sb.ToString()

// ─── Public entry point ─────────────────────────────────────────────────────

/// Render a GFM markdown `source` string to deterministic HTML, consulting
/// `policy` for every link (`Hyperlink`) and image (`Media`) destination.
///
/// The output is the cross-host byte-parity contract — every Fuaran host
/// (F#/Fable, `Renderer.Server`, TS, Go, Rust, Python) reproduces it exactly,
/// under the same policy. Safe to feed to `dangerouslySetInnerHTML` / a
/// raw-HTML sink: escaped by construction, raw HTML never passes through, URLs
/// pass the scheme floor. The result still passes through
/// `Sanitize.sanitizeMarkdownHtml` as defence-in-depth (now a near-no-op).
///
/// **This is the entry point a host rendering a DECODED tree wants**, with
/// `Sanitize.denyNonLocalEgress` or its own declaration. The renderer tiers
/// call it with their context's `EgressPolicy`, so a decoded markdown body no
/// longer reaches an undeclared origin without any caller opt-in on the path.
let toHtmlWithEgress (policy: Sanitize.EgressPolicy) (source: string) : string =
    if isNull source || source = "" then
        ""
    else
        // Normalise line endings; split into lines.
        let normalized = source.Replace("\r\n", "\n").Replace('\r', '\n')
        // Convert trailing-≥2-space line ends to hard breaks by marking them:
        // the tokenizer turns `\n` into SoftBreak; we pre-insert a HardBreak
        // signal by replacing "  \n" runs. Simpler: handle per-line in blocks —
        // here we keep it line-based and let inline soft/hard breaks be decided
        // by trailing spaces preserved in paragraph text.
        let rawLines = normalized.Split('\n')
        let refs, lines = extractRefDefs rawLines
        let blocks = parseBlocks lines
        let html = renderBlocks policy refs blocks
        Sanitize.sanitizeMarkdownHtml html

/// Render a GFM markdown `source` string to deterministic HTML under the
/// PERMISSIVE destination policy — every destination the scheme floor accepts
/// is emitted as authored.
///
/// This is the pure `string -> string` form the corpus has pinned since Phase
/// 292, and it is unchanged to the byte. It is the correct entry point for a
/// HAND-AUTHORED body, where the author is the trust boundary. For a DECODED
/// body it is not: reach `toHtmlWithEgress` with a policy. Naming the
/// permissive posture rather than defaulting to it is the same inversion
/// `Sanitize.permissiveEgress` and `Runtime.permissive` take — an unpolicied
/// render should be visible in the host's own source.
let toHtml (source: string) : string =
    toHtmlWithEgress Sanitize.permissiveEgress source
