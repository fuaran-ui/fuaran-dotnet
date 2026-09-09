module Fuaran.UI.OpStream.Dag.Tests.ConformanceTests

open System.IO
open Expecto
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions

// ============================================================================
//  DAG corpus conformance — Leg A (F# == corpus).
//
//  For each committed DAG fixture: assert the committed bytes equal the current
//  F# encoder's output (no silent drift), and that decode→re-encode round-trips
//  byte-identically. Gated on the workspace `wire-format-fixtures/dag/` corpus
//  being present (absent in a single-repo checkout — the linear JsonDecode
//  suite gates the same way). Leg B (TS == corpus) runs in the fuaran-ts repo.
// ============================================================================

// Phase 1647 — resolved through the ONE corpus-root resolver rather than a
// `__SOURCE_DIRECTORY__` climb: the source path is fixed at compile time, so in
// a git worktree it named a directory that does not exist and this suite
// skipped silently.
let private corpusDir =
    Fuaran.Tests.CorpusRoot.tryFind ()
    |> Option.map (fun root -> Path.Combine(root, "dag"))
    |> Option.defaultValue (Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "..", "wire-format-fixtures", "dag"))

let private decodeOp (s: string) : Result<TreeOp<obj>, string> =
    JsonDecode.decodeOp s |> Result.mapError (sprintf "%A")

[<Tests>]
let tests =
    testList
        "Dag.Conformance"
        [ test "committed DAG corpus matches the F# encoder + round-trips" {
              if not (Directory.Exists corpusDir) then
                  // Single-repo checkout — the workspace corpus is absent.
                  skiptest "wire-format-fixtures/dag corpus absent (single-repo checkout)"
              else
                  for (id, _description, record) in DagCorpus.fixtures do
                      let path = Path.Combine(corpusDir, id + ".json")
                      Expect.isTrue (File.Exists path) (sprintf "fixture %s present in the corpus" id)
                      let committed = File.ReadAllText(path).TrimEnd('\n', '\r')

                      // 1. The committed bytes == the current encoder's output.
                      Expect.equal
                          (DagWire.encodeRecord CanonicalJson.encodeOp record)
                          committed
                          (sprintf "%s encoder == committed bytes" id)

                      // 2. decode → re-encode round-trips byte-identically.
                      match DagWire.decodeRecord decodeOp committed with
                      | Ok decoded ->
                          Expect.equal
                              (DagWire.encodeRecord CanonicalJson.encodeOp decoded)
                              committed
                              (sprintf "%s decode→re-encode is byte-stable" id)
                      | Error e -> failtestf "%s decode failed: %s" id e
          } ]
