module Fuaran.UI.OpStream.Dag.Tests.DagCorpus

open System.IO
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions

// ============================================================================
//  DAG wire-format conformance corpus (Phase 178).
//
//  A corpus under `wire-format-fixtures/dag/` — the linear corpus
//  (`nodes/` / `ops/` / `reject/` + `manifest.json`) is untouched. Each fixture
//  is a `dag-record-round-trip`: decode `inputFile`, re-encode, assert
//  byte-equal. The committed bytes ARE the F# `DagWire.encodeRecord` output, so
//  the corpus pins both conformant hosts (F# + the TS `@fuaran-ui/ops` codec)
//  to the same canonical bytes. Since Phase 2645 every `hash` is Core's
//  `Dag.nodeId` (`DagOpRecord.nodeId`), which `ConformanceTests` recomputes.
//
//  Regenerate (from fuaran-dotnet/):
//      dotnet run --project src/Fuaran.UI.OpStream.Dag.Tests -- --emit-dag-corpus ..\wire-format-fixtures
// ============================================================================

/// The canonical fixtures, as `(id, description, record)`. All `obj`-typed and
/// closure-free so the nested op round-trips through `JsonDecode.decodeOp`.
let fixtures: (string * string * DagOpRecord<obj>) list =
    let ts = System.DateTimeOffset.FromUnixTimeSeconds

    // A genesis (no-parent) record.
    let genesis =
        DagOpRecord.create
            "s1"
            []
            (TreeOp.RemoveNode(NodeId "n1"))
            None
            (Actor.Human "u1")
            (ts 1700000000L)
            OpResultEnvelope.Success

    // A single-parent child (a linear step off the genesis).
    let child =
        DagOpRecord.create
            "s1"
            [ genesis.Hash ]
            (TreeOp.RemoveNode(NodeId "n2"))
            (Some "prompt-1")
            (Actor.Agent("claude", "4.8", "planner"))
            (ts 1700000060L)
            OpResultEnvelope.Success

    // A merge node is an ordinary node with two parents whose op is its
    // reconciliation SCRIPT as a `Batch` (Phase 2645): empty where the fold needs
    // no script, the child-order rule's reorder where it interleaves appends.
    let merge =
        DagOpRecord.create
            "s1"
            [ child.Hash; genesis.Hash ]
            (TreeOp.Batch [])
            None
            (Actor.Human "merge")
            (ts 1700000120L)
            OpResultEnvelope.Success

    let mergeScript =
        DagOpRecord.create
            "s1"
            [ genesis.Hash; child.Hash ]
            (TreeOp.Batch [ TreeOp.ReorderChildren(NodeId "root", [ NodeId "a"; NodeId "b" ]) ])
            (Some "prompt-2")
            (Actor.Agent("claude", "4.8", "reconciler"))
            (ts 1700000180L)
            OpResultEnvelope.Success

    [ "dag-genesis", "Genesis DAG record (no parents)", genesis
      "dag-linear-step", "Single-parent DAG record (linear step, promptId present, agent actor)", child
      "dag-merge", "Two-parent merge node whose script is empty (an empty Batch); parents in author order", merge
      "dag-merge-script",
      "Two-parent merge node whose script is the child-order rule's reorder (a one-op Batch); attributed to an agent with a prompt",
      mergeScript ]

let emit (root: string) : unit =
    let dagDir = Path.Combine(root, "dag")
    Directory.CreateDirectory dagDir |> ignore

    // The retired fixture (the tombstoned record) goes with the family it pinned.
    for path in Directory.GetFiles dagDir do
        File.Delete path

    let manifestEntries = ResizeArray<string>()

    for (id, description, record) in fixtures do
        let json = DagWire.encodeRecord CanonicalJson.encodeOp record
        File.WriteAllText(Path.Combine(dagDir, id + ".json"), json)

        manifestEntries.Add(
            sprintf
                "    {\n      \"id\": \"%s\",\n      \"kind\": \"dag-record-round-trip\",\n      \"inputFile\": \"%s.json\",\n      \"expectedFile\": \"%s.json\",\n      \"description\": \"%s\"\n    }"
                id
                id
                id
                description
        )

    let manifest =
        "{\n"
        + "  \"version\": 1,\n"
        + "  \"description\": \"Fuaran DAG-record wire-format conformance corpus (Phase 178; re-addressed by Phase 1144; re-minted by Phase 2645, the cut). dag-record-round-trip: decode inputFile, re-encode, assert byte-equal to expectedFile. Since Phase 2645 a record is a Fuaran.Core DAG node over the linear chain's provenance envelope, and its hash is Core's Dag.nodeId: the chain's SHA-256 hash function over (the sorted parents joined by a comma, Actor.encode(actor) + '|' + StreamEntry.encode({op, ts, promptId, result})), so every host also recomputes each fixture's hash from its own fields. A merge node is an ordinary node whose op is its reconciliation script as a Batch. outcomeHash and tombstoned were retired (a pruned node leaves the live set for a sink's skeleton); a record carrying either is a pre-2645 record whose address does not recompute. The op field nests the canonical TreeOp wire form and the actor field the canonical typed-actor form, both verbatim. See WIRE_FORMAT.md.\",\n"
        + "  \"fixtures\": [\n"
        + System.String.Join(",\n", manifestEntries)
        + "\n  ]\n}\n"

    File.WriteAllText(Path.Combine(dagDir, "manifest.json"), manifest)
