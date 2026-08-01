module Fuaran.UI.Ops.Tests.OpNotation

// The erasure-sentinel cases box in-process values into the obj-erased op slots
// (`Binding.Static<obj>`, `PropValue.Native`); the boxing seam trips F# 10
// nullness (FS3261) exactly as the tier's own obj-erasure files do.
#nowarn "3261"

// ============================================================================
//  Op-diff review notation goldens (Phase 381).
//
//  Renders the FULL `wire-format-fixtures/ops/*` corpus through
//  `OpNotation.render` and asserts each line against the committed golden
//  (`op-notation.golden.json`, sibling of this file).
//
//  The golden is a COVERAGE LOCK, mirroring the survivability-table pattern
//  Phase 378 used for `WIRE_FORMAT.md` §5.1: it is keyed by fixture name and
//  asserted as a whole, so
//    - a corpus fixture with no golden entry fails (the computed set gains a
//      row the golden lacks),
//    - a golden entry whose fixture vanished fails (the drift guard), and
//    - a corpus fixture whose BYTES changed fails (the golden embeds the source
//      op, same guard shape the apply-parity golden uses).
//  A new op fixture therefore cannot ship without a notation line reviewed and
//  committed alongside it.
//
//  Exhaustive `TreeOp` / `Binding` coverage is enforced one layer lower, by the
//  compiler: `OpNotation`'s matches are exhaustive, so a new case fails the
//  BUILD. This suite adds the complementary check the compiler cannot make —
//  that the corpus actually exercises every op case, so the goldens are a real
//  sample of the vocabulary rather than an accidental subset.
//
//  Regenerating: delete `op-notation.golden.json`, re-run, review the diff,
//  commit. The notation is a public-facing contract the moment the goldens are
//  committed — a re-baseline is a deliberate act, not a build step.
// ============================================================================

open System
open System.IO
open System.Text.Json
open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types

/// Walk up from the test binary to the workspace-root `wire-format-fixtures/`
/// corpus (the same climb the apply-parity suite uses).
let private findCorpusRoot () : string =
    let rec climb (dir: DirectoryInfo | null) : string option =
        match dir with
        | null -> None
        | d ->
            let candidate = Path.Combine(d.FullName, "wire-format-fixtures", "manifest.json")

            if File.Exists candidate then
                Some(Path.Combine(d.FullName, "wire-format-fixtures"))
            else
                climb d.Parent

    match climb (DirectoryInfo(AppContext.BaseDirectory)) with
    | Some root -> root
    | None ->
        failwithf
            "wire-format-fixtures/manifest.json not found walking up from %s. The op-notation suite requires the Fuaran workspace checkout."
            AppContext.BaseDirectory

/// (fixtureName, opJson) for every `ops/*.json`, ordered by name (deterministic).
let private corpusOps () : (string * string) list =
    let opsDir = Path.Combine(findCorpusRoot (), "ops")

    Directory.GetFiles(opsDir, "*.json")
    |> Array.map (fun path ->
        let name =
            match Path.GetFileNameWithoutExtension path with
            | null -> path
            | n -> n

        name, File.ReadAllText(path).Trim())
    |> Array.sortBy fst
    |> Array.toList

let private goldenPath =
    Path.Combine(__SOURCE_DIRECTORY__, "op-notation.golden.json")

let private serializerOptions = JsonSerializerOptions(WriteIndented = true)

/// The golden's exact shape — a JSON array, indented, in fixture-name order.
let private serializeGolden (rows: (string * string * string) list) : string =
    let arr =
        rows
        |> List.map (fun (name, op, notation) ->
            {| name = name
               op = op
               notation = notation |})

    JsonSerializer.Serialize(arr, serializerOptions)

/// Decode one corpus op and project it. A decode failure is recorded rather
/// than thrown, so the golden shows honestly which fixtures the notation never
/// sees (and a fixture that STOPS decoding fails the golden comparison).
let private notationOf (opJson: string) : string =
    match JsonDecode.decodeOp opJson with
    | Error e -> "DECODE_ERR:" + e.Message
    | Ok op -> OpNotation.render op

/// The op's case name — the coverage axis the corpus is asserted against.
let private caseName (op: TreeOp<obj>) : string =
    match op with
    | TreeOp.EditNode _ -> "EditNode"
    | TreeOp.UpdateProp _ -> "UpdateProp"
    | TreeOp.ReplaceBinding _ -> "ReplaceBinding"
    | TreeOp.UpdateStyle _ -> "UpdateStyle"
    | TreeOp.UpdateState _ -> "UpdateState"
    | TreeOp.InsertChild _ -> "InsertChild"
    | TreeOp.RemoveNode _ -> "RemoveNode"
    | TreeOp.MoveNode _ -> "MoveNode"
    | TreeOp.ReorderChildren _ -> "ReorderChildren"
    | TreeOp.ReplaceRoot _ -> "ReplaceRoot"
    | TreeOp.Batch _ -> "Batch"

/// Every case the corpus must exercise — the same list, in DU declaration order.
let private allCases =
    [ "EditNode"
      "UpdateProp"
      "ReplaceBinding"
      "UpdateStyle"
      "UpdateState"
      "InsertChild"
      "RemoveNode"
      "MoveNode"
      "ReorderChildren"
      "ReplaceRoot"
      "Batch" ]

[<Tests>]
let tests =
    testList
        "Phase 381 — op-diff review notation"
        [ test "every corpus op renders to the committed notation golden" {
              let ops = corpusOps ()

              Expect.isGreaterThan (List.length ops) 0 "no op fixtures found under wire-format-fixtures/ops"

              let computed = ops |> List.map (fun (name, op) -> name, op, notationOf op)
              let computedJson = serializeGolden computed

              if not (File.Exists goldenPath) then
                  File.WriteAllText(goldenPath, computedJson)

                  failtestf
                      "op-notation golden did not exist — generated it at %s. Review the notation lines and commit it; re-run to assert against it."
                      goldenPath
              else
                  let golden = File.ReadAllText(goldenPath).Replace("\r\n", "\n")
                  let computedNorm = computedJson.Replace("\r\n", "\n")

                  Expect.equal
                      computedNorm
                      golden
                      "The op notation diverged from the committed golden. A NEW corpus fixture needs its notation line reviewed and committed here; a CHANGED line means the notation itself moved — delete op-notation.golden.json, re-run to regenerate, then review + commit."
          }

          // The other direction of the coverage lock: a golden row whose fixture
          // is gone, or whose fixture bytes drifted, is a stale contract.
          test "golden op bytes still match the corpus (drift guard)" {
              if File.Exists goldenPath then
                  let ops = corpusOps () |> Map.ofList
                  use doc = JsonDocument.Parse(File.ReadAllText goldenPath)

                  for el in doc.RootElement.EnumerateArray() do
                      let name =
                          match el.GetProperty("name").GetString() with
                          | null -> ""
                          | n -> n

                      let goldenOp =
                          match el.GetProperty("op").GetString() with
                          | null -> ""
                          | o -> o

                      match Map.tryFind name ops with
                      | Some corpusOp ->
                          Expect.equal
                              goldenOp
                              corpusOp
                              (sprintf "golden op '%s' drifted from the corpus fixture — regenerate the golden" name)
                      | None -> failtestf "golden references fixture '%s' not present in the corpus" name
          }

          test "the corpus exercises every TreeOp case" {
              let exercised =
                  corpusOps ()
                  |> List.choose (fun (_, opJson) ->
                      match JsonDecode.decodeOp opJson with
                      | Ok op -> Some(caseName op)
                      | Error _ -> None)
                  |> Set.ofList

              let missing = allCases |> List.filter (fun c -> not (Set.contains c exercised))

              Expect.isEmpty
                  missing
                  (sprintf
                      "op cases with no corpus fixture, so no notation golden: %s. Add a fixture (corpus repo) or the notation for these cases ships unreviewed."
                      (String.concat ", " missing))
          }

          // ── Determinism ────────────────────────────────────────────────────

          test "rendering is deterministic — the same op renders identically" {
              for _, opJson in corpusOps () do
                  Expect.equal (notationOf opJson) (notationOf opJson) "notation is not a pure function of the op"
          }

          test "rendering is key-order independent" {
              // The same UpdateProp payload with its object fields authored in
              // two different orders. The canonical scalar path Ordinal-sorts
              // keys, so both must project to one line.
              let ordered =
                  TreeOp.UpdateProp(
                      NodeId "grid-1",
                      "Columns[0]",
                      PropValue.Wire(JObj [ "align", JStr "end"; "label", JStr "Spend"; "width", JInt 120 ])
                  )

              let shuffled =
                  TreeOp.UpdateProp(
                      NodeId "grid-1",
                      "Columns[0]",
                      PropValue.Wire(JObj [ "width", JInt 120; "label", JStr "Spend"; "align", JStr "end" ])
                  )

              Expect.equal
                  (OpNotation.render ordered)
                  (OpNotation.render shuffled)
                  "notation depends on the source field order — it must project the canonical (Ordinal-sorted) form"

              Expect.equal
                  (OpNotation.render ordered)
                  "grid-1: Columns[0] → {\"align\":\"end\",\"label\":\"Spend\",\"width\":120}"
                  "canonical payload projection"
          }

          // ── Erasure sentinels ──────────────────────────────────────────────

          test "erased values render as the existing sentinels" {
              let computed =
                  TreeOp.ReplaceBinding(NodeId "metric-1", "Value", Binding.Computed(fun (_: obj) -> box 1.0))

              Expect.equal
                  (OpNotation.render computed)
                  "metric-1: Value → <closure>"
                  "a host-only Computed binding must render as the wire's closure sentinel"

              let native =
                  TreeOp.UpdateProp(NodeId "metric-1", "Tone", PropValue.Native(box (ToneVariant.Brand)))

              Expect.equal
                  (OpNotation.render native)
                  "metric-1: Tone → <opaque>"
                  "a non-scalar Native payload must render as the wire's opaque sentinel"
          }

          test "bindings name their form, so a binding replacement is not read as a plain value" {
              let query =
                  TreeOp.ReplaceBinding(
                      NodeId "revenue-kpi",
                      "Source",
                      Binding.Query("netRevenue", (fun (o: obj) -> o), None)
                  )

              Expect.equal (OpNotation.render query) "revenue-kpi: Source → $query.netRevenue" "query binding notation"

              let stat =
                  TreeOp.ReplaceBinding(NodeId "revenue-kpi", "Source", Binding.Static(Some(box 99.5)))

              Expect.equal (OpNotation.render stat) "revenue-kpi: Source → static 99.5" "static binding notation"
          }

          test "a batch indents its inner ops" {
              let batch =
                  TreeOp.Batch
                      [ TreeOp.RemoveNode(NodeId "metric-1")
                        TreeOp.Batch [ TreeOp.MoveNode(NodeId "markdown-1", NodeId "card-1") ] ]

              Expect.equal
                  (OpNotation.render batch)
                  (String.concat
                      "\n"
                      [ "batch (2 ops):"
                        "  metric-1: - node"
                        "  batch (1 ops):"
                        "    markdown-1: move → parent card-1" ])
                  "nested batch indentation"
          }

          // ── Record / turn framing ──────────────────────────────────────────

          test "a stream tail renders as a change-log with actor + turn framing" {
              let tail =
                  [ OpNotation.OpFrame.agent 12 "claude-fable-5" "",
                    [ TreeOp.ReplaceBinding(
                          NodeId "revenue-kpi",
                          "Source",
                          Binding.Query("netRevenue", (fun (o: obj) -> o), None)
                      )
                      TreeOp.UpdateProp(NodeId "revenue-kpi", "Label", PropValue.Wire(JStr "Net revenue"))
                      TreeOp.InsertChild(NodeId "channel-grid", Fuaran.metric "margin-kpi" Defaults.metric) ]
                    OpNotation.OpFrame.human 13 "andrew", [ TreeOp.RemoveNode(NodeId "margin-kpi") ] ]

              Expect.equal
                  (OpNotation.renderTail tail)
                  (String.concat
                      "\n"
                      [ "turn 12 · agent claude-fable-5 · 3 ops:"
                        "  revenue-kpi: Source → $query.netRevenue"
                        "  revenue-kpi: Label → \"Net revenue\""
                        "  channel-grid: + child Metric \"margin-kpi\""
                        ""
                        "turn 13 · human andrew · 1 op:"
                        "  margin-kpi: - node" ])
                  "op-stream tail change-log framing"
          }

          test "an unattributed frame omits the actor segment rather than asserting one" {
              Expect.equal
                  (OpNotation.renderTurn OpNotation.OpFrame.empty [ TreeOp.RemoveNode(NodeId "metric-1") ])
                  (String.concat "\n" [ "1 op:"; "  metric-1: - node" ])
                  "empty frame degrades to the op count alone"
          } ]
