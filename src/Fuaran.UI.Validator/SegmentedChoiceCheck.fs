module Fuaran.UI.Validator.SegmentedChoiceCheck

// ============================================================================
//  Segmented-choice option-count check.
//
//  FUARAN045 (Warning) — a segmented choice whose `options` argument is a
//  statically-detectable `Binding.Static (Some [ ... ])` list literal with more
//  than 7 items. Segmented controls work best with ≤5 visible options; >7
//  should reach for `FormFieldKind.Choice` (dropdown) instead — the
//  visible-options trade-off inverts past that point.
//
//  Mirror of `NumberFieldRangeCheck` (FUARAN051) at the smart-ctor call site.
//  Advisory only — Warning, not Error, so the build still passes during
//  incremental adoption / experimentation.
//
//  Static-detectable shapes (`options` is the first argument / tuple item):
//
//    FormFieldKind.segmentedChoice (Binding.Static (Some [ a; b; ... ])) value onChange Orientation.Horizontal
//    FormFieldKind.segmentedChoiceDeclarative (Binding.Static (Some [ ... ])) value Orientation.Vertical
//    FormFieldKind.SegmentedChoice(Binding.Static (Some [ ... ]), Some value, None, Orientation.Vertical)
//
//  Non-detectable shapes (no finding): `options` bound to a query / state /
//  computed source (no compile-time count), or a `let`-bound list (the walk
//  does not chase let-bindings).
// ============================================================================

open FSharp.Compiler.Syntax
open Fuaran.UI.Validator.Findings
open Fuaran.UI.Validator.Syntax

[<Literal>]
let private OptionCountWarningThreshold = 7

type private SegmentedChoiceCall =
    { Location: Location
      OptionCount: int option }

/// The `options` argument of a segmented-choice constructor call.
let private optionsArg (head: SynExpr) (args: SynExpr list) : SynExpr option option =
    if
        isQualified "FormFieldKind" "segmentedChoice" head
        || isQualified "FormFieldKind" "segmentedChoiceDeclarative" head
    then
        Some(List.tryHead args)
    elif isQualified "FormFieldKind" "SegmentedChoice" head then
        match args with
        | [ tuple ] ->
            match unwrap tuple with
            | SynExpr.Tuple(exprs = first :: _) -> Some(Some first)
            | _ -> Some None
        | _ -> None
    else
        None

let private scan (sources: ParsedSource list) : SegmentedChoiceCall list =
    sources
    |> collectApps (fun file head args ->
        optionsArg head args
        |> Option.map (fun options ->
            { Location = mkLocation file head.Range
              OptionCount =
                options
                |> Option.bind staticPayload
                |> Option.bind listItems
                |> Option.map List.length }))

let check (sources: ParsedSource list) : Finding list =
    let allCalls = scan sources

    let findings =
        allCalls
        |> List.choose (fun call ->
            match call.OptionCount with
            | Some count when count > OptionCountWarningThreshold ->
                let base' =
                    create
                        Warning
                        "FUARAN045"
                        call.Location
                        (sprintf
                            "FormFieldKind.segmentedChoice: %d options is more than the recommended maximum of %d for a visible-options exclusive-choice surface. Reach for FormFieldKind.Choice (dropdown) instead — the segmented-control trade-off inverts past 7 options."
                            count
                            OptionCountWarningThreshold)

                let suggestion =
                    sprintf
                        "use FormFieldKind.Choice (dropdown) for %d options; SegmentedChoice is sized for ≤5 visible options"
                        count

                Some(withRecovery [] (Some suggestion) base')
            | _ -> None)


    findings
