module Fuaran.UI.Validator.NumberFieldRangeCheck

// ============================================================================
//  Number-field range check.
//
//  FUARAN051 (Warning) — a ranged number field whose `value` is a static
//  literal (`Binding.Static (Some <lit>)`) outside the declared `[min, max]`
//  interval. The renderer would pass the literal straight to the browser,
//  which then either clamps it to `min` / `max` (HTML form validation) or
//  rejects the form submission silently — neither is what the author intended.
//
//  Mirror of `ScalarRangeCheck` (FUARAN050) but at the form-field call site
//  rather than the `progress` ctor call site. Advisory only — emits a Warning
//  so it doesn't fail the build during incremental adoption.
//
//  Static-detectable shapes (min / max are positional `float option`s — the
//  constructors are curried, so a named `~min:` argument cannot be written):
//
//    FormFieldKind.rangedNumber (Binding.Static (Some 1900.0)) onChange (Some 1979.0) (Some 2028.0) None
//    FormFieldKind.rangedNumberDeclarative (Binding.Static (Some 1900.0)) (Some 1979.0) (Some 2028.0) None
//    FormFieldKind.RangedNumber(Some (FieldValue.ofNumber (Binding.Static (Some 1900.0))), None, Some 1979.0, Some 2028.0, None)
//
//  Non-detectable shapes (no finding): a `value` bound to a query / state /
//  computed source (no compile-time value), or a non-literal `min` / `max`.
// ============================================================================

open FSharp.Compiler.Syntax
open Fuaran.UI.Validator.Findings
open Fuaran.UI.Validator.Syntax

type private RangedNumberCall =
    { Location: Location
      ValueLiteral: float option
      MinLiteral: float option
      MaxLiteral: float option }

/// The literal inside a `Some <lit>` bound.
let private boundLiteral (expr: SynExpr) : float option =
    applied "Some" expr |> Option.bind firstNumericLiteral

/// The static value of a `Binding.Static (Some <lit>)` value binding.
let private valueLiteral (expr: SynExpr) : float option =
    staticPayload expr |> Option.bind firstNumericLiteral

/// The (value, min, max) argument expressions of a ranged-number constructor
/// call, by shape.
let private rangedArgs (head: SynExpr) (args: SynExpr list) =
    let at i = List.tryItem i args

    if isQualified "FormFieldKind" "rangedNumber" head then
        Some(at 0, at 2, at 3)
    elif isQualified "FormFieldKind" "rangedNumberDeclarative" head then
        Some(at 0, at 1, at 2)
    elif isQualified "FormFieldKind" "RangedNumber" head then
        match args with
        | [ tuple ] ->
            match unwrap tuple with
            | SynExpr.Tuple(exprs = items) ->
                let item i = List.tryItem i items
                // Phase 2177 — the case's value slot is the wire-value binding, so
                // a direct construction spells the typed literal through the
                // `FieldValue.ofNumber` eraser; read through it to the literal.
                let value =
                    item 0
                    |> Option.bind (applied "Some")
                    |> Option.map (fun v -> applied "ofNumber" v |> Option.defaultValue v)

                Some(value, item 2, item 3)
            | _ -> None
        | _ -> None
    else
        None

let private scan (sources: ParsedSource list) : RangedNumberCall list =
    sources
    |> collectApps (fun file head args ->
        rangedArgs head args
        |> Option.map (fun (value, min, max) ->
            { Location = mkLocation file head.Range
              ValueLiteral = value |> Option.bind valueLiteral
              MinLiteral = min |> Option.bind boundLiteral
              MaxLiteral = max |> Option.bind boundLiteral }))

let check (sources: ParsedSource list) : Finding list =
    let allCalls = scan sources

    let findings =
        allCalls
        |> List.choose (fun call ->
            match call.ValueLiteral, call.MinLiteral, call.MaxLiteral with
            | Some v, minOpt, maxOpt ->
                let belowMin =
                    match minOpt with
                    | Some m -> v < m
                    | None -> false

                let aboveMax =
                    match maxOpt with
                    | Some m -> v > m
                    | None -> false

                if belowMin || aboveMax then
                    let rangeJson =
                        let minPart =
                            match minOpt with
                            | Some m -> sprintf "\"min\":%g," m
                            | None -> ""

                        let maxPart =
                            match maxOpt with
                            | Some m -> sprintf "\"max\":%g" m
                            | None -> "\"max\":null"

                        sprintf "{\"kind\":\"decimalRange\",%s%s}" minPart maxPart

                    let suggestion =
                        match minOpt, maxOpt with
                        | Some lo, Some hi -> sprintf "set value to a number in [%g, %g]" lo hi
                        | Some lo, None -> sprintf "set value to a number >= %g" lo
                        | None, Some hi -> sprintf "set value to a number <= %g" hi
                        | None, None -> "no range declared"

                    let base' =
                        create
                            Warning
                            "FUARAN051"
                            call.Location
                            (sprintf
                                "FormFieldKind.rangedNumber: the Binding.Static value literal %g is outside the declared [Min, Max] range. The renderer will pass the literal through to the browser's <input type=number>, which will clamp or reject the field on submission. supportedRange=%s"
                                v
                                rangeJson)

                    Some(withRecovery [] (Some suggestion) base')
                else
                    None
            | None, _, _ -> None)


    findings
