module Fuaran.UI.Renderer.MathMl

// ============================================================================
//  Phase 658 — the deterministic LaTeX→MathML translator for `DisplayKind.Math`.
//
//  A pure, Fable-safe, total function shared by BOTH F# renderers (the Feliz
//  client renderer and the Feliz.ViewEngine server renderer emit byte-identical
//  markup from it) — the render-tier analogue of the wire codec's single
//  canonical serialisation, mirroring the `DrawingSvg` builder. The TypeScript
//  `@fuaran-ui/renderer` `mathMl` module is a byte-for-byte port; the shared
//  oracle is the fixture table in `fuaran/docs/MATH-DEGRADATION.md`.
//
//  It implements a small, CLOSED expression subset (superscript / subscript /
//  the four operators + `=` / parentheses / identifiers / numbers / `\frac` /
//  a fixed Greek table — see the design doc). In-subset input translates to
//  native MathML that every modern browser lays out with real superscripts and
//  fractions WITHOUT JavaScript. Anything outside the subset returns `None`, and
//  the renderer falls back to today's raw-source span. It NEVER throws on any
//  input (the never-crash rule): unparseable input is `None`, not an error.
//
//  Determinism: no randomness, no clock, no environment dependence — a pure
//  function of (source, display). The in-subset alphabet contains no `<`, `>`,
//  or `&`, so the emitted MathML never needs HTML-escaping by construction.
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

let private skipWs (p: P) : unit =
    while p.I < p.Len
          && (p.Src[p.I] = ' ' || p.Src[p.I] = '\t' || p.Src[p.I] = '\n' || p.Src[p.I] = '\r') do
        p.I <- p.I + 1

let private fail (p: P) : string =
    p.Ok <- false
    ""

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
            let inner = parseSequence p '}'

            if not p.Ok || p.I >= p.Len || p.Src[p.I] <> '}' then
                fail p
            else
                p.I <- p.I + 1
                inner // a `{…}` group is invisible; `parseSequence` already wrapped multi-child in <mrow>
        elif c = '(' then
            p.I <- p.I + 1
            let inner = parseSequence p ')'

            if not p.Ok || p.I >= p.Len || p.Src[p.I] <> ')' then
                fail p
            else
                p.I <- p.I + 1
                "<mrow><mo>(</mo>" + inner + "<mo>)</mo></mrow>"
        elif c = '\\' then
            // a command: backslash + a run of letters
            let start = p.I + 1
            let mutable j = start

            while j < p.Len && isLetter p.Src[j] do
                j <- j + 1

            let name = p.Src.Substring(start, j - start)
            p.I <- j

            if name = "frac" then
                let num = parseAtom p
                let den = parseAtom p

                if not p.Ok then
                    fail p
                else
                    "<mfrac>" + num + den + "</mfrac>"
            else
                match greek name with
                | Some g -> "<mi>" + g + "</mi>"
                | None -> fail p
        else
            fail p

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

// a run of atoms/operators until end-of-input or an unconsumed `stop` char.
// `stop = '\000'` means "to end-of-input" (no closing delimiter expected).
and private parseSequence (p: P) (stop: char) : string =
    let parts = ResizeArray<string>()
    let mutable looping = true

    while looping && p.Ok do
        skipWs p

        if p.I >= p.Len then
            // ran out: a failure iff we were expecting a closing `stop`
            if stop <> '\000' then
                fail p |> ignore

            looping <- false
        else
            let c = p.Src[p.I]

            if stop <> '\000' && c = stop then
                looping <- false // leave `stop` unconsumed for the caller
            elif c = '+' then
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
            elif c = ')' || c = '}' then
                // an unmatched closer (the matched case is handled by `c = stop`)
                fail p |> ignore
                looping <- false
            else
                let atom = parseScripted p
                parts.Add atom

    if not p.Ok then "" else String.concat "" parts

/// Translate a LaTeX `source` in the closed subset (see
/// `fuaran/docs/MATH-DEGRADATION.md`) to a native MathML fragment string, or
/// `None` when the input is outside the subset (the renderer then falls back to
/// the raw-source span). Total — never throws on any input.
let translate (source: string) (display: MathDisplay) : string option =
    let p =
        { Src = source
          Len = source.Length
          I = 0
          Ok = true }

    let body = parseSequence p '\000'

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
