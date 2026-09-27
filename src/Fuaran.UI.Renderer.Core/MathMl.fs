module Fuaran.UI.Renderer.MathMl

// ============================================================================
//  Phase 658 — the deterministic LaTeX→MathML translator for `NodeKind.Math`.
//
//  A pure, Fable-safe, total function shared by BOTH F# renderers (the Feliz
//  client renderer and the Feliz.ViewEngine server renderer emit byte-identical
//  markup from it) — the render-tier analogue of the wire codec's single
//  canonical serialisation, mirroring the `DrawingSvg` builder. The TypeScript
//  `@fuaran-ui/renderer` `mathMl` module is a byte-for-byte port; the shared
//  oracle is the fixture table in `fuaran-dotnet/docs/MATH-DEGRADATION.md`.
//
//  It implements a small, CLOSED expression subset (superscript / subscript /
//  the four operators + `=` / parentheses / identifiers / numbers / `\frac` /
//  a fixed Greek table — see the design doc; Phase 1853 adds fixed tables of
//  logic / relation / set operators, three symbol constants, named terms
//  (`\mathit` / `\mathrm` / `\text`), `,` `.` `|`, fixed spacing, and the
//  `[ ]` / `\{ \}` fences). In-subset input translates to
//  native MathML that every modern browser lays out with real superscripts and
//  fractions WITHOUT JavaScript. Anything outside the subset returns `None`, and
//  the renderer falls back to today's raw-source span. It NEVER throws on any
//  input (the never-crash rule): unparseable input is `None`, not an error.
//
//  Determinism: no randomness, no clock, no environment dependence — a pure
//  function of (source, display). The in-subset alphabet contains no `<`, `>`,
//  or `&`, so the emitted MathML never needs HTML-escaping by construction; the
//  only character references it ever emits are the two fixed, complete `&lt;` /
//  `&gt;` table entries for `\lt` / `\gt`.
// ============================================================================

open System.Text
open Fuaran.UI.Types

// ─── Greek command table (closed set — see the design doc) ──────────────────

let private greek (name: string) : string option =
    match name with
    | "alpha" -> Some "α"
    | "beta" -> Some "β"
    | "gamma" -> Some "γ"
    | "delta" -> Some "δ"
    | "epsilon" -> Some "ε"
    | "zeta" -> Some "ζ"
    | "eta" -> Some "η"
    | "theta" -> Some "θ"
    | "iota" -> Some "ι"
    | "kappa" -> Some "κ"
    | "lambda" -> Some "λ"
    | "mu" -> Some "μ"
    | "nu" -> Some "ν"
    | "xi" -> Some "ξ"
    | "pi" -> Some "π"
    | "rho" -> Some "ρ"
    | "sigma" -> Some "σ"
    | "tau" -> Some "τ"
    | "phi" -> Some "φ"
    | "chi" -> Some "χ"
    | "psi" -> Some "ψ"
    | "omega" -> Some "ω"
    | "Gamma" -> Some "Γ"
    | "Delta" -> Some "Δ"
    | "Theta" -> Some "Θ"
    | "Lambda" -> Some "Λ"
    | "Xi" -> Some "Ξ"
    | "Pi" -> Some "Π"
    | "Sigma" -> Some "Σ"
    | "Phi" -> Some "Φ"
    | "Psi" -> Some "Ψ"
    | "Omega" -> Some "Ω"
    | _ -> None

// ─── Operator command table → `<mo>` (closed set — Phase 1853, see the design
//     doc). Logic, relations and set operators. `\lt` / `\gt` are the ONLY
//     entries whose value is markup-significant: they are fixed, pre-escaped
//     character references, so the emitted alphabet still never carries a bare
//     `<`, `>` or `&` (the escaping-floor closure argument in the design doc). ──

let private operatorCommand (name: string) : string option =
    match name with
    | "forall" -> Some "∀"
    | "exists" -> Some "∃"
    | "neg"
    | "lnot" -> Some "¬"
    | "land"
    | "wedge" -> Some "∧"
    | "lor"
    | "vee" -> Some "∨"
    | "Rightarrow"
    | "implies" -> Some "⇒"
    | "Leftrightarrow"
    | "iff" -> Some "⇔"
    | "to"
    | "rightarrow" -> Some "→"
    | "mapsto" -> Some "↦"
    | "vdash" -> Some "⊢"
    | "le"
    | "leq" -> Some "≤"
    | "ge"
    | "geq" -> Some "≥"
    | "ne"
    | "neq" -> Some "≠"
    | "equiv" -> Some "≡"
    | "in" -> Some "∈"
    | "notin" -> Some "∉"
    | "subseteq" -> Some "⊆"
    | "subset" -> Some "⊂"
    | "cup" -> Some "∪"
    | "cap" -> Some "∩"
    | "times" -> Some "×"
    | "cdot" -> Some "⋅"
    | "lt" -> Some "&lt;"
    | "gt" -> Some "&gt;"
    | _ -> None

// ─── Symbol constants → `<mi>` (closed set — Phase 1853). Ordinary symbols,
//     not operators, so they are atoms and may carry scripts. ─────────────────

let private symbolCommand (name: string) : string option =
    match name with
    | "top" -> Some "⊤"
    | "bot" -> Some "⊥"
    | "emptyset" -> Some "∅"
    | _ -> None

// ─── Spacing commands → `<mspace>` with a fixed width (closed set — Phase
//     1853). `\,` `\:` `\;` are LaTeX's 3/18, 4/18 and 5/18 em; `\ ` is the
//     interword space; `\quad` is 1em. ───────────────────────────────────────

let private spaceCommand (name: string) : string option =
    match name with
    | "," -> Some "0.1667em"
    | ":" -> Some "0.2222em"
    | ";" -> Some "0.2778em"
    | " " -> Some "0.3333em"
    | "quad" -> Some "1em"
    | _ -> None

// ─── Parser state — a mutable index + failure flag over the source string.
//     Deliberately imperative and Fable-safe (mutable record fields), and
//     structurally mirrors the TypeScript port so byte-identity is obvious. ──

type private P =
    { Src: string
      Len: int
      mutable I: int
      mutable Ok: bool }

let private isDigit (c: char) = c >= '0' && c <= '9'

let private isLetter (c: char) =
    (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')

// the named-term argument alphabet (`\mathit` / `\mathrm` / `\text`), besides
// the escaped underscore `\_`
let private isNameChar (c: char) = isLetter c || isDigit c

let private skipWs (p: P) : unit =
    while p.I < p.Len
          && (p.Src[p.I] = ' ' || p.Src[p.I] = '\t' || p.Src[p.I] = '\n' || p.Src[p.I] = '\r') do
        p.I <- p.I + 1

let private fail (p: P) : string =
    p.Ok <- false
    ""

// true when the source continues with `s` at the current index
let private startsAt (p: P) (s: string) : bool =
    p.I + s.Length <= p.Len && p.Src.Substring(p.I, s.Length) = s

// The command at `p.I` (which holds a `\`): its name and the index just past
// it. The name is a run of ASCII letters, or else exactly one non-letter
// character (a control symbol such as `\,` or `\{`); a bare trailing `\` has
// the empty name. Does not consume.
let private commandAt (p: P) : string * int =
    let start = p.I + 1

    if start >= p.Len then
        ("", start)
    elif isLetter p.Src[start] then
        let mutable j = start

        while j < p.Len && isLetter p.Src[j] do
            j <- j + 1

        (p.Src.Substring(start, j - start), j)
    else
        (string p.Src[start], start + 1)

// A named-term argument: `{` + one or more of [A-Za-z0-9] or the escaped
// underscore `\_` (emitted as `_`) + `}`, nothing else (no whitespace inside; a
// bare `_` is a subscript in LaTeX, so it is out of subset here rather than read
// differently from the KaTeX tier). Returns the argument text, or fails.
let private parseNameArg (p: P) : string =
    skipWs p

    if not p.Ok || p.I >= p.Len || p.Src[p.I] <> '{' then
        fail p
    else
        let sb = StringBuilder()
        let mutable j = p.I + 1
        let mutable scanning = true

        while scanning && j < p.Len do
            if isNameChar p.Src[j] then
                sb.Append(string p.Src[j]) |> ignore
                j <- j + 1
            elif p.Src[j] = '\\' && j + 1 < p.Len && p.Src[j + 1] = '_' then
                sb.Append("_") |> ignore
                j <- j + 2
            else
                scanning <- false

        if sb.Length = 0 || j >= p.Len || p.Src[j] <> '}' then
            fail p
        else
            p.I <- j + 1
            sb.ToString()

// Forward-declared mutual recursion via `let rec … and …`.
let rec private parseAtom (p: P) : string =
    skipWs p

    if not p.Ok || p.I >= p.Len then
        fail p
    else
        let c = p.Src[p.I]

        if isDigit c then
            let start = p.I

            while p.I < p.Len && isDigit p.Src[p.I] do
                p.I <- p.I + 1
            // one optional decimal point, only when a digit follows it
            if p.I + 1 < p.Len && p.Src[p.I] = '.' && isDigit p.Src[p.I + 1] then
                p.I <- p.I + 1

                while p.I < p.Len && isDigit p.Src[p.I] do
                    p.I <- p.I + 1

            "<mn>" + p.Src.Substring(start, p.I - start) + "</mn>"
        elif isLetter c then
            p.I <- p.I + 1
            "<mi>" + string c + "</mi>"
        elif c = '{' then
            p.I <- p.I + 1
            let inner, count = parseSequence p "}"

            if not p.Ok || not (startsAt p "}") || count = 0 then
                fail p
            else
                p.I <- p.I + 1
                // a `{…}` group is invisible: one element stands bare, several are
                // one `<mrow>` (so a script / fraction argument is ONE child)
                if count = 1 then inner else "<mrow>" + inner + "</mrow>"
        elif c = '(' then
            p.I <- p.I + 1
            parseFenced p ")" "(" ")"
        elif c = '[' then
            p.I <- p.I + 1
            parseFenced p "]" "[" "]"
        elif c = '\\' then
            let name, next = commandAt p
            p.I <- next

            if name = "{" then
                parseFenced p "\\}" "{" "}"
            elif name = "frac" then
                let num = parseAtom p
                let den = parseAtom p

                if not p.Ok then
                    fail p
                else
                    "<mfrac>" + num + den + "</mfrac>"
            elif name = "mathit" then
                let arg = parseNameArg p

                if not p.Ok then
                    fail p
                else
                    "<mi mathvariant=\"italic\">" + arg + "</mi>"
            elif name = "mathrm" then
                let arg = parseNameArg p

                if not p.Ok then
                    fail p
                else
                    "<mi mathvariant=\"normal\">" + arg + "</mi>"
            elif name = "text" then
                let arg = parseNameArg p

                if not p.Ok then fail p else "<mtext>" + arg + "</mtext>"
            else
                match greek name with
                | Some g -> "<mi>" + g + "</mi>"
                | None ->
                    match symbolCommand name with
                    | Some s -> "<mi>" + s + "</mi>"
                    | None -> fail p
        else
            fail p

// a fenced group, its opener already consumed: a sequence up to `close`, which
// must be present → `<mrow><mo>{openMo}</mo>…<mo>{closeMo}</mo></mrow>`
and private parseFenced (p: P) (close: string) (openMo: string) (closeMo: string) : string =
    let inner, _ = parseSequence p close

    if not p.Ok || not (startsAt p close) then
        fail p
    else
        p.I <- p.I + close.Length
        "<mrow><mo>" + openMo + "</mo>" + inner + "<mo>" + closeMo + "</mo></mrow>"

// atom + optional sub/super scripts (either order, at most one of each)
and private parseScripted (p: P) : string =
    let baseAtom = parseAtom p

    if not p.Ok then
        fail p
    else
        let mutable sub = ""
        let mutable sup = ""
        let mutable hasSub = false
        let mutable hasSup = false
        let mutable looping = true

        while looping && p.Ok do
            skipWs p

            if p.I < p.Len && p.Src[p.I] = '^' && not hasSup then
                p.I <- p.I + 1
                sup <- parseAtom p
                hasSup <- true
            elif p.I < p.Len && p.Src[p.I] = '_' && not hasSub then
                p.I <- p.I + 1
                sub <- parseAtom p
                hasSub <- true
            else
                looping <- false

        if not p.Ok then
            fail p
        elif hasSub && hasSup then
            "<msubsup>" + baseAtom + sub + sup + "</msubsup>"
        elif hasSup then
            "<msup>" + baseAtom + sup + "</msup>"
        elif hasSub then
            "<msub>" + baseAtom + sub + "</msub>"
        else
            baseAtom

// a run of atoms/operators until end-of-input or an unconsumed `stop` string,
// with the number of elements it holds. `stop = ""` means "to end-of-input"
// (no closing delimiter expected).
and private parseSequence (p: P) (stop: string) : string * int =
    let parts = ResizeArray<string>()
    let mutable looping = true

    while looping && p.Ok do
        skipWs p

        if p.I >= p.Len then
            // ran out: a failure iff we were expecting a closing `stop`
            if stop <> "" then
                fail p |> ignore

            looping <- false
        elif stop <> "" && startsAt p stop then
            looping <- false // leave `stop` unconsumed for the caller
        else
            let c = p.Src[p.I]

            if c = '+' then
                parts.Add "<mo>+</mo>"
                p.I <- p.I + 1
            elif c = '-' then
                parts.Add "<mo>−</mo>"
                p.I <- p.I + 1
            elif c = '*' then
                parts.Add "<mo>⋅</mo>"
                p.I <- p.I + 1
            elif c = '/' then
                parts.Add "<mo>/</mo>"
                p.I <- p.I + 1
            elif c = '=' then
                parts.Add "<mo>=</mo>"
                p.I <- p.I + 1
            elif c = ',' then
                parts.Add "<mo separator=\"true\">,</mo>"
                p.I <- p.I + 1
            elif c = '.' then
                // the quantifier dot. A `.` directly followed by a digit that the
                // number rule did not consume (`.5`, `x.5`, `1.2.3`) is out of subset.
                if p.I + 1 < p.Len && isDigit p.Src[p.I + 1] then
                    fail p |> ignore
                    looping <- false
                else
                    parts.Add "<mo>.</mo>"
                    p.I <- p.I + 1
            elif c = '|' then
                parts.Add "<mo>|</mo>"
                p.I <- p.I + 1
            elif c = ')' || c = '}' || c = ']' then
                // an unmatched closer (the matched case is handled by `stop`)
                fail p |> ignore
                looping <- false
            elif c = '\\' then
                let name, next = commandAt p

                match operatorCommand name with
                | Some op ->
                    parts.Add("<mo>" + op + "</mo>")
                    p.I <- next
                | None ->
                    match spaceCommand name with
                    | Some w ->
                        parts.Add("<mspace width=\"" + w + "\"></mspace>")
                        p.I <- next
                    | None ->
                        if name = "}" then
                            // an unmatched `\}`
                            fail p |> ignore
                            looping <- false
                        else
                            parts.Add(parseScripted p)
            else
                parts.Add(parseScripted p)

    if not p.Ok then
        ("", 0)
    else
        (String.concat "" parts, parts.Count)

/// Translate a LaTeX `source` in the closed subset (see
/// `fuaran-dotnet/docs/MATH-DEGRADATION.md`) to a native MathML fragment string, or
/// `None` when the input is outside the subset (the renderer then falls back to
/// the raw-source span). Total — never throws on any input.
let translate (source: string) (display: MathDisplay) : string option =
    let p =
        { Src = source
          Len = source.Length
          I = 0
          Ok = true }

    let body, _ = parseSequence p ""

    if not p.Ok || p.I < p.Len || body = "" then
        None
    else
        let disp =
            match display with
            | MathDisplay.Block -> "block"
            | MathDisplay.Inline -> "inline"

        let sb = StringBuilder()

        sb.Append "<math xmlns=\"http://www.w3.org/1998/Math/MathML\" display=\""
        |> ignore

        sb.Append disp |> ignore
        sb.Append "\">" |> ignore
        sb.Append body |> ignore
        sb.Append "</math>" |> ignore
        Some(sb.ToString())
