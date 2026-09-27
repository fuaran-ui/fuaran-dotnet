module Fuaran.UI.Renderer.CodeHighlight

// ============================================================================
//  Phase 1854 — the deterministic highlighting tier for `NodeKind.CodeBlock`.
//
//  A pure, Fable-safe, total function shared by BOTH F# renderers (the Feliz
//  client renderer and the Feliz.ViewEngine server renderer emit byte-identical
//  markup from it), built the way Phase 658 built `MathMl` for `Math`. The
//  TypeScript `@fuaran-ui/renderer` `codeHighlight` module is a byte-for-byte
//  port; the shared oracle is the fixture table in
//  `fuaran-dotnet/docs/CODE-HIGHLIGHT.md`, which is normative.
//
//  It is LEXICAL, not a parser: each grammar is a fixed keyword table, a fixed
//  type table and two string-form switches, over one shared set of ML-family
//  delimiter rules (nesting `(* *)` comments, `//` line comments, strings,
//  character literals, numbers, an operator-character table). No regular
//  expression is used, so nothing can behave differently between .NET and JS:
//  every decision is a comparison of UTF-16 code units, which both platforms
//  index identically.
//
//  Output: class-only spans (`tok-kw` / `tok-com` / `tok-str` / `tok-num` /
//  `tok-op` / `tok-ty`) around HTML-escaped text; colours come from the
//  reference stylesheet, never inline. A token NEVER spans a line break: the
//  text of every token is split at `\n`, and each non-empty line segment gets
//  its own span, so a multi-line comment or string is closed and reopened at
//  each line boundary. A language with no grammar returns `None` and the
//  renderer keeps today's escaped `<pre><code>` bytes exactly. It never throws.
// ============================================================================

open System.Text

/// The attribute value a highlighted `<code>` carries as `data-highlighted`, so
/// a richer client-only highlighter can recognise the deterministic tier and
/// skip or replace it.
[<Literal>]
let TierMarker = "deterministic"

/// One grammar: the per-language tables. Everything else — the comment,
/// string, character, number and operator rules — is the shared ML-family
/// lexical core below, so adding a grammar is adding a row, not code.
type Grammar =
    {
        /// The canonical language name (`fstar`, `fsharp`).
        Name: string
        /// Words rendered as `tok-kw`.
        Keywords: Set<string>
        /// Words rendered as `tok-ty` (built-in type and effect names).
        Types: Set<string>
        /// `"""…"""` is a string with no escapes (F#).
        TripleQuotedStrings: bool
        /// `@"…"` is a string whose only escape is a doubled `""` (F#).
        VerbatimStrings: bool
    }

/// F* — keyword and type tables per docs/CODE-HIGHLIGHT.md §3.1.
let fstar: Grammar =
    { Name = "fstar"
      Keywords =
        Set.ofList
            [ "abstract"
              "and"
              "as"
              "assert"
              "assert_norm"
              "assume"
              "attributes"
              "begin"
              "by"
              "calc"
              "class"
              "decreases"
              "effect"
              "else"
              "end"
              "ensures"
              "exception"
              "exists"
              "false"
              "forall"
              "friend"
              "fun"
              "function"
              "if"
              "in"
              "include"
              "inline"
              "inline_for_extraction"
              "instance"
              "introduce"
              "irreducible"
              "layered_effect"
              "let"
              "logic"
              "match"
              "module"
              "new"
              "new_effect"
              "noeq"
              "noextract"
              "of"
              "open"
              "opaque"
              "private"
              "rec"
              "reflectable"
              "reifiable"
              "reify"
              "requires"
              "returns"
              "sub_effect"
              "then"
              "total"
              "true"
              "try"
              "type"
              "unfold"
              "unopteq"
              "val"
              "when"
              "with" ]
      Types =
        Set.ofList
            [ "Div"
              "Dv"
              "GTot"
              "Ghost"
              "Lemma"
              "ML"
              "Pure"
              "ST"
              "Stack"
              "Tot"
              "Type"
              "Type0"
              "Type1"
              "bool"
              "int"
              "list"
              "nat"
              "option"
              "pos"
              "prop"
              "squash"
              "string"
              "unit" ]
      TripleQuotedStrings = false
      VerbatimStrings = false }

/// F# — keyword and type tables per docs/CODE-HIGHLIGHT.md §3.2.
let fsharp: Grammar =
    { Name = "fsharp"
      Keywords =
        Set.ofList
            [ "abstract"
              "and"
              "as"
              "assert"
              "base"
              "begin"
              "class"
              "const"
              "default"
              "delegate"
              "do"
              "done"
              "downcast"
              "downto"
              "elif"
              "else"
              "end"
              "exception"
              "extern"
              "false"
              "finally"
              "fixed"
              "for"
              "fun"
              "function"
              "global"
              "if"
              "in"
              "inherit"
              "inline"
              "interface"
              "internal"
              "lazy"
              "let"
              "match"
              "member"
              "module"
              "mutable"
              "namespace"
              "new"
              "null"
              "of"
              "open"
              "or"
              "override"
              "private"
              "public"
              "rec"
              "return"
              "static"
              "struct"
              "then"
              "to"
              "true"
              "try"
              "type"
              "upcast"
              "use"
              "val"
              "when"
              "while"
              "with"
              "yield" ]
      Types =
        Set.ofList
            [ "array"
              "bool"
              "byte"
              "char"
              "decimal"
              "double"
              "exn"
              "float"
              "float32"
              "int"
              "int16"
              "int32"
              "int64"
              "int8"
              "list"
              "nativeint"
              "obj"
              "option"
              "sbyte"
              "seq"
              "single"
              "string"
              "uint"
              "uint16"
              "uint32"
              "uint64"
              "uint8"
              "unativeint"
              "unit"
              "voption" ]
      TripleQuotedStrings = true
      VerbatimStrings = true }

// ASCII-only lowering: the language tag is matched case-insensitively over
// A-Z only, so no platform's Unicode case tables can make the two ports differ.
let private asciiLower (s: string) : string =
    let sb = StringBuilder()

    for c in s do
        if c >= 'A' && c <= 'Z' then
            sb.Append(string (char (int c + 32))) |> ignore
        else
            sb.Append(string c) |> ignore

    sb.ToString()

/// The grammar a CodeBlock `language` tag selects, or `None` (plain output).
let grammarFor (language: string) : Grammar option =
    match asciiLower language with
    | "fstar"
    | "fst" -> Some fstar
    | "fsharp"
    | "fs"
    | "f#" -> Some fsharp
    | _ -> None

// ─── The shared ML-family lexical core ──────────────────────────────────────

type private Cls =
    | Plain
    | Kw
    | Com
    | Str
    | Num
    | Op
    | Ty

let private classOf (c: Cls) : string =
    match c with
    | Kw -> "tok-kw"
    | Com -> "tok-com"
    | Str -> "tok-str"
    | Num -> "tok-num"
    | Op -> "tok-op"
    | Ty -> "tok-ty"
    | Plain -> ""

let private isDigit (c: char) = c >= '0' && c <= '9'

let private isLetter (c: char) =
    (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')

let private isIdentStart (c: char) = isLetter c || c = '_'

let private isIdentChar (c: char) =
    isLetter c || isDigit c || c = '_' || c = '\''

/// The operator-character table (§2.6). `.` is deliberately absent (member
/// access would otherwise dominate the output), as are brackets, `,` `;` `#`.
let private operatorChars = "!$%&*+-/:<=>?@^|~\\"

let private isOperatorChar (c: char) = operatorChars.IndexOf c >= 0

let private startsAt (s: string) (i: int) (p: string) : bool =
    i + p.Length <= s.Length && s.Substring(i, p.Length) = p

// HTML text escaping — the same `& < >` floor both renderers apply to text
// content, so a plain run is byte-identical to today's escaped output.
let private appendEscaped (sb: StringBuilder) (s: string) : unit =
    for c in s do
        match c with
        | '&' -> sb.Append("&amp;") |> ignore
        | '<' -> sb.Append("&lt;") |> ignore
        | '>' -> sb.Append("&gt;") |> ignore
        | _ -> sb.Append(string c) |> ignore

// Emit one token's text, split at `\n` so no span ever contains a line break.
let private emit (sb: StringBuilder) (cls: Cls) (text: string) : unit =
    let segments = text.Split('\n')

    for k in 0 .. segments.Length - 1 do
        if k > 0 then
            sb.Append("\n") |> ignore

        let seg = segments[k]

        if seg <> "" then
            match cls with
            | Plain -> appendEscaped sb seg
            | _ ->
                sb.Append("<span class=\"").Append(classOf cls).Append("\">") |> ignore
                appendEscaped sb seg
                sb.Append("</span>") |> ignore

// `(* … *)` from `i` (which holds `(*` and not `(*)`), nesting by depth.
// Inside a comment `(*)` is skipped as a unit and changes no depth. An
// unterminated comment runs to the end of the block.
let private blockCommentEnd (s: string) (i: int) : int =
    let n = s.Length
    let mutable depth = 1
    let mutable j = i + 2

    while j < n && depth > 0 do
        if startsAt s j "(*)" then
            j <- j + 3
        elif startsAt s j "*)" then
            depth <- depth - 1
            j <- j + 2
        elif startsAt s j "(*" then
            depth <- depth + 1
            j <- j + 2
        else
            j <- j + 1

    j

// `//` to the end of the line (the `\n` itself is not part of the comment).
let private lineCommentEnd (s: string) (i: int) : int =
    let n = s.Length
    let mutable j = i + 2

    while j < n && s[j] <> '\n' do
        j <- j + 1

    j

// `"…"` with backslash escapes; unterminated runs to the end of the block.
let private stringEnd (s: string) (i: int) : int =
    let n = s.Length
    let mutable j = i + 1
    let mutable fin = false

    while j < n && not fin do
        let c = s[j]

        if c = '\\' then
            j <- j + 2
        elif c = '"' then
            j <- j + 1
            fin <- true
        else
            j <- j + 1

    if j > n then n else j

// `@"…"` — the only escape is a doubled `""`.
let private verbatimEnd (s: string) (i: int) : int =
    let n = s.Length
    let mutable j = i + 2
    let mutable fin = false

    while j < n && not fin do
        if s[j] = '"' then
            if j + 1 < n && s[j + 1] = '"' then
                j <- j + 2
            else
                j <- j + 1
                fin <- true
        else
            j <- j + 1

    j

// `"""…"""` — no escapes; ends at the first closing `"""`.
let private tripleEnd (s: string) (i: int) : int =
    let n = s.Length
    let mutable j = i + 3
    let mutable fin = false

    while j < n && not fin do
        if startsAt s j "\"\"\"" then
            j <- j + 3
            fin <- true
        else
            j <- j + 1

    j

// A character literal at `i` (which holds `'`): `'c'` for one non-backslash,
// non-newline unit, or `'\…'` closed by the first `'` at offset 3..11 with no
// newline before it. Anything else is not a literal (0 = no match) and the
// quote is plain text — an F# / F* type variable such as `'a`.
let private charLiteralEnd (s: string) (i: int) : int =
    let n = s.Length

    if i + 2 < n && s[i + 1] <> '\\' && s[i + 1] <> '\n' && s[i + 2] = '\'' then
        i + 3
    elif i + 1 < n && s[i + 1] = '\\' then
        let mutable k = i + 3
        let mutable found = 0

        while found = 0 && k < n && k <= i + 11 && s[k - 1] <> '\n' do
            if s[k] = '\'' then
                found <- k + 1

            k <- k + 1

        found
    else
        0

// A number from a digit at `i`: letters, digits and `_` continue it (suffixes,
// hex and binary digits); `.` continues it only before a digit (so `1..2` is a
// range); `-` / `+` continue it only straight after `e` / `E`, before a digit,
// in a number that is not `0x` / `0X` hexadecimal.
let private numberEnd (s: string) (i: int) : int =
    let n = s.Length

    let isHex = s[i] = '0' && i + 1 < n && (s[i + 1] = 'x' || s[i + 1] = 'X')

    let mutable j = i + 1
    let mutable fin = false

    while j < n && not fin do
        let d = s[j]

        if isLetter d || isDigit d || d = '_' then
            j <- j + 1
        elif d = '.' && j + 1 < n && isDigit s[j + 1] then
            j <- j + 2
        elif
            (d = '-' || d = '+')
            && not isHex
            && (s[j - 1] = 'e' || s[j - 1] = 'E')
            && j + 1 < n
            && isDigit s[j + 1]
        then
            j <- j + 2
        else
            fin <- true

    j

let private identEnd (s: string) (i: int) : int =
    let n = s.Length
    let mutable j = i + 1

    while j < n && isIdentChar s[j] do
        j <- j + 1

    j

// A maximal run of operator characters, stopping before a `//` comment opener
// or (in a grammar with verbatim strings) an `@"` string opener.
let private operatorEnd (g: Grammar) (s: string) (i: int) : int =
    let n = s.Length
    let mutable j = i + 1

    while j < n
          && isOperatorChar s[j]
          && not (startsAt s j "//")
          && not (g.VerbatimStrings && startsAt s j "@\"") do
        j <- j + 1

    j

// The class and end of the token starting at `i`. The order of the arms is the
// normative precedence (§2.1).
let private tokenAt (g: Grammar) (s: string) (i: int) : Cls * int =
    let c = s[i]

    if startsAt s i "(*)" then
        Op, i + 3
    elif startsAt s i "(*" then
        Com, blockCommentEnd s i
    elif startsAt s i "//" then
        Com, lineCommentEnd s i
    elif g.TripleQuotedStrings && startsAt s i "\"\"\"" then
        Str, tripleEnd s i
    elif g.VerbatimStrings && startsAt s i "@\"" then
        Str, verbatimEnd s i
    elif c = '"' then
        Str, stringEnd s i
    elif c = '\'' then
        match charLiteralEnd s i with
        | 0 -> Plain, i + 1
        | j -> Str, j
    elif isDigit c then
        Num, numberEnd s i
    elif isIdentStart c then
        let j = identEnd s i
        let word = s.Substring(i, j - i)

        if g.Keywords.Contains word then Kw, j
        elif g.Types.Contains word then Ty, j
        else Plain, j
    elif isOperatorChar c then
        Op, operatorEnd g s i
    else
        Plain, i + 1

/// Tokenise `code` under `g` and return the inner markup of the `<code>`
/// element: escaped text and class-only spans, adjacent same-class tokens
/// coalesced into one run before the per-line split. Total and pure.
let highlightWith (g: Grammar) (code: string) : string =
    let sb = StringBuilder()
    let n = code.Length
    let mutable i = 0
    let mutable runCls = Plain
    let mutable runStart = 0

    while i < n do
        let cls, j = tokenAt g code i

        if cls <> runCls then
            if i > runStart then
                emit sb runCls (code.Substring(runStart, i - runStart))

            runCls <- cls
            runStart <- i

        i <- j

    if n > runStart then
        emit sb runCls (code.Substring(runStart, n - runStart))

    sb.ToString()

/// The highlighted inner markup for a CodeBlock, or `None` when `language` has
/// no grammar — the renderer then emits today's escaped text unchanged.
let highlight (language: string) (code: string) : string option =
    grammarFor language |> Option.map (fun g -> highlightWith g code)
