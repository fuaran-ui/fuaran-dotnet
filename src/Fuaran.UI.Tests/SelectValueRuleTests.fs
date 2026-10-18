module Fuaran.UI.Tests.SelectValueRuleTests

// ============================================================================
//  Phase 1962 — a multi-select `Select` carries `values` and no `value`.
//
//  `SelectSpec.Value` is an OPTION because the IDL's per-field optionality
//  cannot state "required unless `multiple` is true". The rule is therefore
//  enforced where the siblings are visible — the constructors and the decoder —
//  and these cases pin both halves from the host side. The cross-host half is
//  the corpus: `nodes/multiselect-1.json`, `lenient/lenient-1962-*`,
//  `reject/reject-1962-*` (WIRE_FORMAT §3.2).
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.JsonDecode
open Fuaran.UI.OpStream.Abstractions

let private options: Binding<SelectOption list> =
    Binding.Static(
        Some
            [ ({ Value = "red"; Label = "Red" }: SelectOption)
              ({ Value = "green"; Label = "Green" }: SelectOption) ]
    )

let private specOf (n: Node<obj>) : SelectSpec<obj> =
    match n.Kind with
    | NodeKind.Select s -> s
    | other -> failtestf "expected a Select, got %A" other

let private roundTrip (json: string) : string =
    match decodeNodeObj json with
    | Ok n -> CanonicalJson.encodeNode n
    | Error e -> failtestf "decode refused %s: %s at %s" json e.Message e.Path

let private codesOf (n: Node<obj>) : string list =
    match PreEmitValidate.validate n with
    | Ok() -> []
    | Error ds -> ds |> List.map (fun d -> let code, _, _ = PreEmitValidate.describe d in code)

let private refusal (json: string) : string * string =
    match decodeNodeObj json with
    | Ok _ -> failtestf "decode accepted %s" json
    | Error e -> e.Code, e.Path

[<Tests>]
let tests =
    testList
        "Phase 1962 — the single/multi Select value rule"
        [ testCase "multiSelect carries values and no value" (fun _ ->
              let n =
                  Fuaran.multiSelect "tags" (TextSource.Literal "Tags") options (Binding.State("tags", None)) (fun _ ->
                      Action.Chain [])

              Expect.isNone (specOf n).Value "a multi-select has no single-select value"

              Expect.isFalse
                  ((CanonicalJson.encodeNode n).Contains "\"value\":{")
                  "the canonical multi-select emits no value key")

          testCase "select drops a value handed to a multi-select" (fun _ ->
              let n =
                  Fuaran.select
                      "tags"
                      { Defaults.select with
                          Label = TextSource.Literal "Tags"
                          Source = options
                          Multiple = Some true
                          Values = Some(Binding.State("tags", None)) }

              Expect.isNone (specOf n).Value "the Defaults placeholder is cleared on a multi-select")

          testCase "select fills an absent value on a single-select with no selection" (fun _ ->
              let n =
                  Fuaran.select
                      "region"
                      { Defaults.select with
                          Label = TextSource.Literal "Region"
                          Source = options
                          Value = None }

              match (specOf n).Value with
              | Some(Binding.Static None) -> ()
              | other -> failtestf "a single-select always carries value; got %A" other)

          testCase "the placeholder value on a multi-select normalises away" (fun _ ->
              let input =
                  """{"id":"t","kind":{"$type":"Select","label":"Tags","multiple":true,"source":{"$type":"Static","value":[]},"value":{"$type":"Static"},"values":{"$type":"State","key":"tags"}}}"""

              let expected =
                  """{"id":"t","kind":{"$type":"Select","label":"Tags","multiple":true,"source":{"$type":"Static","value":[]},"values":{"$type":"State","key":"tags"}}}"""

              Expect.equal (roundTrip input) expected "placeholder in, clean form out"
              Expect.equal (roundTrip expected) expected "the clean form is a fixed point")

          testCase "a bound value on a multi-select normalises away too" (fun _ ->
              // Models that followed the pre-1962 type wrote a REAL binding beside
              // `values` (14 of the 16 stored emissions carrying both); a
              // multi-select never reads it, so it is dropped, not refused.
              let input =
                  """{"id":"t","kind":{"$type":"Select","label":"Tags","multiple":true,"source":{"$type":"Static","value":[]},"value":{"$type":"State","defaultValue":"red","key":"primary"},"values":{"$type":"State","key":"tags"}}}"""

              let expected =
                  """{"id":"t","kind":{"$type":"Select","label":"Tags","multiple":true,"source":{"$type":"Static","value":[]},"values":{"$type":"State","key":"tags"}}}"""

              Expect.equal (roundTrip input) expected "dropped, not refused")

          testCase "a MALFORMED value on a multi-select still refuses" (fun _ ->
              let input =
                  """{"id":"t","kind":{"$type":"Select","label":"Tags","multiple":true,"source":{"$type":"Static","value":[]},"value":{"$type":"Nope"},"values":{"$type":"State","key":"tags"}}}"""

              Expect.equal (fst (refusal input)) "UNKNOWN_DU_CASE" "decoded before it is dropped")

          testCase "the pre-emit validator names a constructed multi-select carrying a value (FUARAN164)" (fun _ ->
              let n: Node<obj> =
                  { Fuaran.multiSelect
                        "tags"
                        (TextSource.Literal "Tags")
                        options
                        (Binding.State("tags", None))
                        (fun _ -> Action.Chain []) with
                      Kind =
                          NodeKind.Select(
                              { Defaults.select with
                                  Label = TextSource.Literal "Tags"
                                  Source = options
                                  Multiple = Some true
                                  Values = Some(Binding.State("tags", None)) }
                          ) }

              let codes = codesOf n

              Expect.contains codes "FUARAN164" "the record literal kept Defaults.select's value"

              let clean =
                  Fuaran.multiSelect "tags" (TextSource.Literal "Tags") options (Binding.State("tags", None)) (fun _ ->
                      Action.Chain [])

              let cleanCodes = codesOf clean

              Expect.isFalse (List.contains "FUARAN164" cleanCodes) "multiSelect carries no value")

          testCase "a single-select without value is MISSING_FIELD at value" (fun _ ->
              let input =
                  """{"id":"t","kind":{"$type":"Select","label":"Region","multiple":false,"source":{"$type":"Static","value":[]}}}"""

              Expect.equal (refusal input) ("MISSING_FIELD", "$.kind.value") "multiple:false is a single-select")

          testCase "an unreadable multiple is its own defect, not a missing value" (fun _ ->
              let input =
                  """{"id":"t","kind":{"$type":"Select","label":"Tags","multiple":"yes","source":{"$type":"Static","value":[]},"values":{"$type":"State","key":"tags"}}}"""

              Expect.equal (refusal input) ("WRONG_TYPE", "$.kind.multiple") "the presence rule is not applied on top") ]
