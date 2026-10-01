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

          testCase "a bound value on a multi-select is WRONG_TYPE at value" (fun _ ->
              let input =
                  """{"id":"t","kind":{"$type":"Select","label":"Tags","multiple":true,"source":{"$type":"Static","value":[]},"value":{"$type":"Static","value":"red"},"values":{"$type":"State","key":"tags"}}}"""

              Expect.equal (refusal input) ("WRONG_TYPE", "$.kind.value") "refused, not dropped")

          testCase "a single-select without value is MISSING_FIELD at value" (fun _ ->
              let input =
                  """{"id":"t","kind":{"$type":"Select","label":"Region","multiple":false,"source":{"$type":"Static","value":[]}}}"""

              Expect.equal (refusal input) ("MISSING_FIELD", "$.kind.value") "multiple:false is a single-select")

          testCase "an unreadable multiple is its own defect, not a missing value" (fun _ ->
              let input =
                  """{"id":"t","kind":{"$type":"Select","label":"Tags","multiple":"yes","source":{"$type":"Static","value":[]},"values":{"$type":"State","key":"tags"}}}"""

              Expect.equal (refusal input) ("WRONG_TYPE", "$.kind.multiple") "the presence rule is not applied on top") ]
