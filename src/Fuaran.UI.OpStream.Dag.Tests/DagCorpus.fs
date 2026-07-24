module Fuaran.UI.OpStream.Dag.Tests.DagCorpus

open System.IO
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions

// ============================================================================
//  DAG wire-format conformance corpus (Phase 178).
//
//  An ADDITIVE corpus under `wire-format-fixtures/dag/` — the linear corpus
//  (`nodes/` / `ops/` / `reject/` + `manifest.json`) is untouched. Each fixture
//  is a `dag-record-round-trip`: decode `inputFile`, re-encode, assert
//  byte-equal. The committed bytes ARE the F# `DagWire.encodeRecord` output, so
//  the corpus pins both conformant hosts (F# + the TS `@fuaran-ui/ops` codec)
//  to the same canonical bytes.
//
//  Regenerate (from fuaran/):
//      dotnet run --project src/Fuaran.UI.OpStream.Dag.Tests -- --emit-dag-corpus ..\wire-format-fixtures
// ============================================================================

/// The canonical fixtures, as `(id, description, record)`. All `obj`-typed and
/// closure-free so the nested op round-trips through `JsonDecode.decodeOp`.
let fixtures: (string * string * DagOpRecord<obj>) list =
    let ts = System.DateTimeOffset.FromUnixTimeSeconds

    // A genesis (no-parent) record.
    let genesis =
        DagOpRecord.create "s1" [] (TreeOp.RemoveNode(NodeId "n1")) None "u1" (ts 1700000000L) OpResultEnvelope.Success

    // A single-parent child (a linear step off the genesis).
    let child =
        DagOpRecord.create
            "s1"
            [ genesis.Hash ]
            (TreeOp.RemoveNode(NodeId "n2"))
            (Some "prompt-1")
            "u1"
            (ts 1700000060L)
            OpResultEnvelope.Success

    // A merge node — two parents (author order), committed to an outcome hash.
    let merge =
        DagOpRecord.createMerge
            "s1"
            [ child.Hash; genesis.Hash ]
            (TreeOp.Batch [])
            (String.replicate 64 "c")
            None
            "merge"
            (ts 1700000120L)
            OpResultEnvelope.Success

    // A tombstoned record — payload pruned, hash + parents preserved.
    let tombstoned =
        { child with
            Op = TreeOp.Batch []
            OutcomeHash = None
            Tombstoned = true }

    [ "dag-genesis", "Genesis DAG record (no parents)", genesis
      "dag-linear-step", "Single-parent DAG record (linear step, promptId present)", child
      "dag-merge", "Two-parent merge node (outcome hash; author-order parents)", merge
      "dag-tombstone", "Tombstoned record (payload pruned, hash + parents preserved)", tombstoned ]

/// Emit the DAG corpus to `<root>/dag/` (payloads + `dag/manifest.json`).
let emit (root: string) : unit =
    let dagDir = Path.Combine(root, "dag")
    Directory.CreateDirectory dagDir |> ignore

    let manifestEntries = ResizeArray<string>()

    for (id, description, record) in fixtures do
        let json = DagWire.encodeRecord record
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
        + "  \"description\": \"Fuaran DAG-record wire-format conformance corpus (Phase 178, additive). dag-record-round-trip: decode inputFile, re-encode, assert byte-equal to expectedFile. The op field nests the canonical TreeOp wire form. See fuaran/docs/WIRE_FORMAT.md.\",\n"
        + "  \"fixtures\": [\n"
        + System.String.Join(",\n", manifestEntries)
        + "\n  ]\n}\n"

    File.WriteAllText(Path.Combine(dagDir, "manifest.json"), manifest)
