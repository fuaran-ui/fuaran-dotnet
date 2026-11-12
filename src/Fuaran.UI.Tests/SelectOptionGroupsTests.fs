module Fuaran.UI.Tests.SelectOptionGroupsTests

// ============================================================================
//  Phase 2571 — a `Select` carries optional option groups (WIRE_FORMAT §3.6.30).
//
//  The grouping lives on the `Select` spec and names options by `value`;
//  `SelectOption` is unchanged, so every existing option literal still compiles.
//  The cross-host half is the corpus: `nodes/select-option-groups*.json` and
//  `reject/reject-2571-*`.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.JsonDecode
open Fuaran.UI.OpStream.Abstractions

let private roundTrip (json: string) : string =
    match decodeNodeObj json with
    | Ok n -> CanonicalJson.encodeNode n
    | Error e -> failtestf "decode refused %s: %s at %s" json e.Message e.Path

let private refusal (json: string) : string * string =
    match decodeNodeObj json with
    | Ok _ -> failtestf "decode accepted %s" json
    | Error e -> e.Code, e.Path

let private grouped (source: string) (groups: string) : string =
    sprintf
        """{"id":"s","kind":{"$type":"Select","groups":%s,"label":"Chart","source":%s,"value":{"$type":"Static","value":"a"}}}"""
        groups
        source

let private staticAB =
    """{"$type":"Static","value":[{"label":"A","value":"a"},{"label":"B","value":"b"}]}"""

[<Tests>]
let tests =
    testList
        "Phase 2571 — Select option groups"
        [ testCase "an existing { Label; Value } option literal still compiles and means what it meant" (fun _ ->
              // The member is on the Select, never on SelectOption: this literal
              // is the shape every consumer writes, and it must not gain FS0764.
              let option: SelectOption = { Label = "UK"; Value = "uk" }
              Expect.equal (option.Label, option.Value) ("UK", "uk") "unchanged")

          testCase "Defaults.select carries no groups, and an ungrouped select's bytes do not move" (fun _ ->
              Expect.isEmpty Defaults.select<obj>.Groups "no grouping by default"

              let json =
                  """{"id":"s","kind":{"$type":"Select","label":"Chart","source":{"$type":"Static","value":[{"label":"A","value":"a"}]},"value":{"$type":"Static","value":"a"}}}"""

              Expect.equal (roundTrip json) json "byte-identical, no groups key synthesised")

          testCase "groups round-trip byte-identically, each group's values in the order written" (fun _ ->
              let json =
                  grouped staticAB """[{"label":"Two","values":["b"]},{"label":"One","values":["a"]}]"""

              Expect.equal (roundTrip json) json "byte-identical")

          testCase "an explicit empty groups list is the absent member" (fun _ ->
              let json = grouped staticAB "[]"
              Expect.isFalse ((roundTrip json).Contains "groups") "normalised away (absent and [] are one fact)")

          testCase "a value named by two groups is refused at the second naming" (fun _ ->
              Expect.equal
                  (refusal (grouped staticAB """[{"label":"One","values":["a"]},{"label":"Two","values":["b","a"]}]"""))
                  ("WRONG_TYPE", "$.kind.groups[1].values[1]")
                  "WRONG_TYPE at the duplicate")

          testCase "a value named twice by one group is refused too" (fun _ ->
              Expect.equal
                  (refusal (grouped staticAB """[{"label":"One","values":["a","a"]}]"""))
                  ("WRONG_TYPE", "$.kind.groups[0].values[1]")
                  "WRONG_TYPE at the repeat")

          testCase "over static options, a value naming no option is refused" (fun _ ->
              Expect.equal
                  (refusal (grouped staticAB """[{"label":"One","values":["a","zz"]}]"""))
                  ("WRONG_TYPE", "$.kind.groups[0].values[1]")
                  "WRONG_TYPE at the unknown value")

          testCase "over a bound source the decoder cannot judge membership, and does not" (fun _ ->
              let json =
                  grouped """{"$type":"State","key":"charts"}""" """[{"label":"One","values":["zz"]}]"""

              match decodeNodeObj json with
              | Ok n ->
                  match n.Kind with
                  | NodeKind.Select s -> Expect.equal s.Groups [ { Label = "One"; Values = [ "zz" ] } ] "kept"
                  | other -> failtestf "expected a Select, got %A" other
              | Error e -> failtestf "a bound source refused: %s at %s" e.Message e.Path) ]
