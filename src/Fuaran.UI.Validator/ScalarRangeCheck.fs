module Fuaran.UI.Validator.ScalarRangeCheck

// ============================================================================
//  Scalar value-space check.
//
//  The value-dimension analogue of the categorical "declare closed enums as
//  oneOf" recommendation. A `Fuaran.progress` Fraction has a known closed
//  domain — `[0, 1]` per ProgressSpec (§4k Q3.4: "Indeterminate = true with a
//  Caveat when no honest 0..1 bound exists"). A statically-knowable Fraction
//  literal that falls outside `[0, 1]` is almost always a unit error (an
//  author passing a 0..100 percentage, or a raw count) that the renderer would
//  silently clamp or overflow.
//
//  Advisory only — emits a Warning (FUARAN050), never an Error, so it does not
//  fail the build and stays safe for incremental adoption. The finding carries
//  the `supportedRange` JSON and a recovery suggestion (use an honest 0..1
//  value, or `Indeterminate = true`) in the §4d AI-recovery shape.
//
//  Only fires on a static literal. A Fraction bound to a query / state /
//  computed source carries no compile-time value to range-check and is left
//  alone (the runtime AIField bounded decoder enforces those at the boundary).
// ============================================================================

open Fuaran.UI.Validator.AstWalker
open Fuaran.UI.Validator.Findings

/// The known-bounded domain of a progress fraction.
let private fractionMin = 0.0
let private fractionMax = 1.0

let check (calls: FuaranCall list) : Finding list =
    calls
    |> List.choose (fun c ->
        match c.Ctor, c.ProgressDetail with
        | "progress", Some { FractionLiteral = Some v } when v < fractionMin || v > fractionMax ->
            let base' =
                create
                    Warning
                    "FUARAN050"
                    c.Location
                    (sprintf
                        "Fuaran.progress Fraction literal %g is outside the known-bounded domain [0, 1]. A progress fraction is a bounded scalar — declare an honest 0..1 value, or set Indeterminate = true with a Caveat when no honest bound exists. supportedRange={\"kind\":\"decimalRange\",\"min\":0,\"max\":1}"
                        v)

            Some(withRecovery [] (Some "set Fraction to a value in [0, 1], or use Indeterminate = true") base')
        | _ -> None)
