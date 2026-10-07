module Fuaran.UI.Validator.FormatCoherenceCheck

// ============================================================================
//  Format coherence check (Phase 102).
//
//  FUARAN061 (Error) — a locale-aware currency `Format` whose ISO-4217 code is
//  a blank string literal. A `Format.Currency ""` (or the smart-ctor
//  `localeFormat.currency ""`) is an incoherent combination: the renderer
//  hands the code to `Intl.NumberFormat({ style: 'currency', currency: "" })`,
//  which throws a `RangeError` at render time (and the .NET fallback emits a
//  stray bare-code prefix). The typed surface keeps `isoCode` mandatory, so the
//  only static incoherence is an *empty / whitespace* literal — exactly what
//  this rule rejects at build time, per the phase's "Currency without an ISO
//  code" acceptance criterion.
//
//  Static-detectable shapes:
//
//    Format.Currency ""
//    Format.Currency "   "
//    localeFormat.currency ""
//    binding.format src (Format.Currency "") locale.ambient   // nested
//
//  Non-detectable shapes (no finding):
//
//    - `isoCode` is a non-literal expression (a `let`-bound value, a function
//      result) — no compile-time string to inspect.
//    - A non-blank but invalid code (e.g. "ZZ") — the typed surface can't
//      distinguish a real ISO-4217 code from a typo without a currency table;
//      that's a runtime `RangeError`, out of scope for the static rule.
// ============================================================================

open Fuaran.UI.Validator.Findings
open Fuaran.UI.Validator.Syntax

type private CurrencyCall =
    { Location: Location
      IsoLiteral: string option }

/// `Format.Currency "<iso>"` (the DU case) or `localeFormat.currency "<iso>"`.
let private scan (sources: ParsedSource list) : CurrencyCall list =
    sources
    |> collectApps (fun file head args ->
        if
            (isQualified "Format" "Currency" head
             || isQualified "localeFormat" "currency" head)
            && not args.IsEmpty
        then
            Some
                { Location = mkLocation file head.Range
                  IsoLiteral = List.tryHead args |> Option.bind literalString }
        else
            None)

let check (sources: ParsedSource list) : Finding list =
    let allCalls = scan sources

    let findings =
        allCalls
        |> List.choose (fun call ->
            match call.IsoLiteral with
            | Some iso when System.String.IsNullOrWhiteSpace iso ->
                let base' =
                    create
                        Error
                        "FUARAN061"
                        call.Location
                        "Format.Currency was given a blank ISO-4217 currency code. The renderer passes it to Intl.NumberFormat({ style: 'currency', currency: '' }), which throws a RangeError at render time. Supply a valid ISO-4217 code (e.g. \"GBP\", \"USD\", \"EUR\")."

                Some(
                    withRecovery
                        []
                        (Some "Replace the empty string with a valid ISO-4217 currency code, e.g. \"GBP\".")
                        base'
                )
            | _ -> None)


    findings
