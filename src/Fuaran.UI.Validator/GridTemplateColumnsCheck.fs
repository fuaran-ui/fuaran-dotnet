module Fuaran.UI.Validator.GridTemplateColumnsCheck

// ============================================================================
//  Grid template-columns advisory check.
//
//  FUARAN046 (Warning) — a `Fuaran.gridLayoutTemplated` call whose verbatim
//  `templateColumns` string is structurally equivalent to the typed `Cols: int`
//  shape. The canonical equivalent-to-Cols pattern is `repeat(N, 1fr)` — the
//  default `Fuaran.gridLayout` emission. Authors reaching for the escape
//  hatch to express something the typed shape already covers spend the
//  unbounded-string review-tax for no expressivity gain.
//
//  Static-detectable shapes:
//
//    Fuaran.gridLayoutTemplated "id" "repeat(5, 1fr)" spec
//    Fuaran.gridLayoutTemplated "id" "repeat(12, 1fr)" Defaults.gridLayout
//    Fuaran.gridLayoutTemplated "id" "  repeat(5, 1fr)  " spec   (whitespace-tolerant)
//
//  Non-detectable shapes (no finding):
//
//    - The templateColumns argument is a let-bound name / parameter
//      (the walker does not chase let-bindings).
//    - The string is a sprintf / interpolated literal whose result is
//      `repeat(N, 1fr)`.
//    - The call uses the lowered record-update shape directly
//      (`{ Defaults.gridLayout with TemplateColumns = Some "repeat(N, 1fr)" }`).
//
//  Anti-pattern coverage rationale:
//
//    The string-escape shape was picked over a typed CSS-grammar DU
//    deliberately — keeping the typed surface lean and getting the gap
//    closed for irregular grids. The structural detection catches the most
//    common eval-quality regression (the `repeat(N, 1fr)` equivalence)
//    without trying to type-check arbitrary CSS. Other regressions —
//    `auto` columns paired with `1fr`, `repeat(auto-fit, ...)` without
//    `minmax`, etc. — stay in the migration doc's rule-of-thumb guidance
//    until a real eval-quality issue justifies expanding the check.
// ============================================================================

open System.Text.RegularExpressions
open Fuaran.UI.Validator.Findings
open Fuaran.UI.Validator.Syntax

let private repeatOneFrRegex =
    Regex(@"^\s*repeat\(\s*\d+\s*,\s*1fr\s*\)\s*$", RegexOptions.Compiled ||| RegexOptions.IgnoreCase)


type private GridTemplatedCall =
    { Location: Location
      TemplateColumns: string option }

/// `Fuaran.gridLayoutTemplated "id" "<templateColumns>" spec` — the template is
/// the second argument.
let private scan (sources: ParsedSource list) : GridTemplatedCall list =
    sources
    |> collectApps (fun file head args ->
        if isQualified "Fuaran" "gridLayoutTemplated" head && not args.IsEmpty then
            Some
                { Location = mkLocation file head.Range
                  TemplateColumns = List.tryItem 1 args |> Option.bind literalString }
        else
            None)

let check (sources: ParsedSource list) : Finding list =
    let allCalls = scan sources

    let findings =
        allCalls
        |> List.choose (fun call ->
            match call.TemplateColumns with
            | Some s when repeatOneFrRegex.IsMatch s ->
                let base' =
                    create
                        Warning
                        "FUARAN046"
                        call.Location
                        (sprintf
                            "Fuaran.gridLayoutTemplated: templateColumns %A is equivalent to the typed Cols-based emission. Use Fuaran.gridLayout with `Cols = N` instead — the typed shape avoids the unbounded-string escape's review tax for no expressivity gain."
                            s)

                let suggestion =
                    "use Fuaran.gridLayout with the typed Cols field; reach for gridLayoutTemplated only when the sizing function (1fr 2fr, 100px repeat(...), min-content max-content, auto-fit minmax) can't be expressed by Cols"

                Some(withRecovery [] (Some suggestion) base')
            | _ -> None)


    findings
