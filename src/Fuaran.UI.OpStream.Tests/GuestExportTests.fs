module Fuaran.UI.OpStream.Tests.GuestExportTests

open System
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.InMemory
open Fuaran.UI.OpStream.Replay

// ============================================================================
//  Phase 274 (§4o) — portable guest export ("the iframe you can serialise").
//
//  Covers: the linear OpRecordWire codec round-trip; the GuestExportBundle
//  document encode/decode (byte-identical re-encode); export-from-sink;
//  replay-on-import reconstructing the exported state byte-identically;
//  the identity remap (fresh scope + NodeId rebase, hash chain re-derived,
//  lineage verbatim); the collision guard; and chain tamper-evidence.
// ============================================================================

let private ts (unix: int64) : DateTimeOffset = DateTimeOffset.FromUnixTimeSeconds unix

/// The guest's tree at mount time: a dashboard with two markdown panes.
let private initialTree: Node<obj> =
    Fuaran.dashboard
        "g-dash"
        { Defaults.dashboard<obj> with
            Children = [ Fuaran.markdown "g-left" "Left pane"; Fuaran.markdown "g-right" "Right pane" ] }

let private style: SemanticStyle =
    { Tone = Success
      Weight = Standard
      Emphasis = Normal
      Role = StyleRole.None
      Voice = FontVoice.Default }

/// The guest's op-stream: insert a note, restyle it, drop the right pane.
let private guestOps: TreeOp<obj> list =
    [ TreeOp.InsertChild(NodeId "g-dash", 2, Fuaran.markdown "g-note" "A note")
      TreeOp.UpdateStyle(NodeId "g-note", style)
      TreeOp.RemoveNode(NodeId "g-right") ]

/// Chain the ops into records under `guest-<scopeId>` with real hashes and
/// per-op lineage (a prompt id on the first record, an agent on the second).
let private buildRecords (scopeId: string) : OpRecord<obj> list =
    let streamId = GuestStream.streamId scopeId

    let lineage =
        [ Some "prompt-1", Actor.Human "alice"
          None, Actor.Agent("claude", "fable-5", "agent-7")
          None, Actor.Human "alice" ]

    (([], HashChain.genesisPreviousHash), List.zip guestOps lineage)
    ||> List.fold (fun (acc, previousHash) (op, (promptId, actor)) ->
        let sequence = List.length acc + 1
        let timestamp = ts (1_700_000_000L + int64 sequence)

        let hash =
            HashChain.computeHash previousHash op sequence timestamp actor promptId OpResultEnvelope.Success

        let record =
            { StreamId = streamId
              Sequence = sequence
              PreviousHash = previousHash
              Hash = hash
              Op = op
              PromptId = promptId
              Actor = actor
              Timestamp = timestamp
              ResultEnvelope = OpResultEnvelope.Success }

        record :: acc, hash)
    |> fst
    |> List.rev

let private sourceBundle () : GuestExportBundle =
    GuestExport.bundle "guest-a" initialTree (buildRecords "guest-a")

let private encodeTree (tree: Node<obj>) : string = CanonicalJson.encodeNode tree

[<Tests>]
let opRecordWireTests =
    testList
        "Phase274.OpRecordWire"
        [ test "a record round-trips through the canonical envelope byte-identically" {
              let record = buildRecords "guest-a" |> List.item 1
              let encoded = OpRecordWire.encodeRecord record

              match OpRecordWire.decodeRecord GuestExport.decodeOpString encoded with
              | Error e -> failtestf "decode failed: %s" e
              | Ok decoded ->
                  Expect.equal
                      (OpRecordWire.encodeRecord decoded)
                      encoded
                      "re-encoding the decoded record reproduces the same bytes"

                  Expect.equal decoded.PromptId record.PromptId "promptId carries over"
                  Expect.equal decoded.Actor record.Actor "the typed actor carries over"
                  Expect.equal decoded.Hash record.Hash "the chain hash carries over"
          }

          test "the op-stream JSONL round-trips in order" {
              let records = buildRecords "guest-a"
              let jsonl = OpRecordWire.toJsonl records

              Expect.equal (jsonl.Split('\n').Length) records.Length "one line per record"

              match OpRecordWire.ofJsonl GuestExport.decodeOpString jsonl with
              | Error e -> failtestf "JSONL decode failed: %s" e
              | Ok decoded ->
                  Expect.equal
                      (decoded |> List.map _.Sequence)
                      (records |> List.map _.Sequence)
                      "sequence order survives"
          } ]

[<Tests>]
let bundleTests =
    testList
        "Phase274.GuestExportBundle"
        [ test "the bundle document decodes back and re-encodes byte-identically" {
              let bundle = sourceBundle ()
              let doc = GuestExport.encode bundle

              match GuestExport.decode doc with
              | Error e -> failtestf "bundle decode failed: %s" e
              | Ok decoded ->
                  Expect.equal (GuestExport.encode decoded) doc "encode ∘ decode is the identity on the document"
                  Expect.equal decoded.ScopeId "guest-a" "the scope id carries over"
                  Expect.equal decoded.Records.Length 3 "all records carry over"
          }

          test "an unsupported formatVersion is refused" {
              let doc =
                  (GuestExport.encode (sourceBundle ())).Replace("\"formatVersion\":1", "\"formatVersion\":99")

              match GuestExport.decode doc with
              | Error e -> Expect.stringContains e "unsupported formatVersion" "the guard names the problem"
              | Ok _ -> failtest "formatVersion 99 must be refused"
          }

          test "export reads the whole guest stream out of a sink" {
              let sink = InMemorySink<obj>() :> IOpStreamSink<obj>
              let records = buildRecords "guest-a"

              for r in records do
                  sink.Append r |> Async.RunSynchronously

              let bundle = GuestExport.export sink "guest-a" initialTree |> Async.RunSynchronously

              Expect.equal bundle.Records.Length 3 "every journalled record is in the bundle"

              Expect.equal
                  (GuestExport.encode bundle)
                  (GuestExport.encode (sourceBundle ()))
                  "the sink export equals the hand-assembled bundle"
          }

          test "replay reconstructs the exported state byte-identically" {
              let bundle = sourceBundle ()

              let expected =
                  match Replay.applyTo initialTree bundle.Records with
                  | Ok t -> t
                  | Error e -> failtestf "replay failed: %A" e

              match GuestImport.reconstruct bundle with
              | Error e -> failtestf "reconstruct failed: %s" e
              | Ok tree ->
                  Expect.equal (encodeTree tree) (encodeTree expected) "canonical encodings agree byte-for-byte"
          }

          test "a tampered op breaks the chain and reconstruction refuses it" {
              let bundle = sourceBundle ()

              let tampered =
                  { bundle with
                      Records =
                          bundle.Records
                          |> List.map (fun r ->
                              if r.Sequence = 2 then
                                  { r with
                                      Op = TreeOp.RemoveNode(NodeId "g-left") }
                              else
                                  r) }

              match GuestImport.reconstruct tampered with
              | Error e -> Expect.stringContains e "hash-chain" "the refusal cites the chain"
              | Ok _ -> failtest "a tampered record must not reconstruct"
          } ]

[<Tests>]
let importTests =
    testList
        "Phase274.GuestImport"
        [ test "remap rebases scope + every NodeId, re-derives the chain, and keeps the lineage verbatim" {
              let bundle = sourceBundle ()
              let remapped = GuestImport.remap "fresh" (GuestImport.prefixIds "fresh") bundle

              Expect.equal remapped.ScopeId "fresh" "the bundle carries the new scope id"

              for r in remapped.Records do
                  Expect.equal r.StreamId "guest-fresh" "records re-key to the new guest stream"

              match Verify.chain remapped.Records with
              | Error e -> failtestf "the re-derived chain must verify: %A" e
              | Ok() -> ()

              let ids =
                  Introspect.allNodeIds remapped.InitialTree |> List.map (fun (NodeId raw) -> raw)

              Expect.all ids (fun id -> id.StartsWith "fresh.") "every initial-tree id is namespaced"

              List.zip bundle.Records remapped.Records
              |> List.iter (fun (before, after) ->
                  Expect.equal after.PromptId before.PromptId "prompt lineage is verbatim"
                  Expect.equal after.Actor before.Actor "actor attribution is verbatim"
                  Expect.equal after.Timestamp before.Timestamp "timestamps are verbatim")
          }

          test "replay commutes with the remap — the imported guest is the same tree under new ids" {
              let bundle = sourceBundle ()
              let remapped = GuestImport.remap "fresh" (GuestImport.prefixIds "fresh") bundle

              let viaRemappedReplay =
                  match GuestImport.reconstruct remapped with
                  | Ok t -> t
                  | Error e -> failtestf "remapped reconstruct failed: %s" e

              let viaTreeRemap =
                  match GuestImport.reconstruct bundle with
                  | Ok t -> GuestImport.mapTreeIds (GuestImport.prefixIds "fresh") t
                  | Error e -> failtestf "source reconstruct failed: %s" e

              Expect.equal
                  (encodeTree viaRemappedReplay)
                  (encodeTree viaTreeRemap)
                  "replaying remapped ops equals remapping the replayed tree"
          }

          test "remapped ids are disjoint from a host that doesn't share the fresh scope prefix" {
              let hostTree: Node<obj> =
                  Fuaran.dashboard
                      "host-root"
                      { Defaults.dashboard<obj> with
                          Children = [ Fuaran.markdown "g-left" "host's own g-left" ] }

              let remapped =
                  GuestImport.remap "fresh" (GuestImport.prefixIds "fresh") (sourceBundle ())

              let imported =
                  match GuestImport.reconstruct remapped with
                  | Ok t -> t
                  | Error e -> failtestf "reconstruct failed: %s" e

              let hostIds = Introspect.allNodeIds hostTree |> Set.ofList
              let guestIds = Introspect.allNodeIds imported |> Set.ofList

              Expect.isTrue
                  (Set.isEmpty (Set.intersect hostIds guestIds))
                  "the host's ids (including a clashing 'g-left') never collide with the imported guest's"
          }

          test "load appends the records into the importing sink and refuses an occupied scope" {
              let sink = InMemorySink<obj>() :> IOpStreamSink<obj>

              let remapped =
                  GuestImport.remap "fresh" (GuestImport.prefixIds "fresh") (sourceBundle ())

              match GuestImport.load sink remapped |> Async.RunSynchronously with
              | Error e -> failtestf "first load must succeed: %s" e
              | Ok _ ->
                  let latest = sink.LatestSequence "guest-fresh" |> Async.RunSynchronously

                  Expect.equal latest 3 "the importing host's journal holds the guest's provenance"

              match GuestImport.load sink remapped |> Async.RunSynchronously with
              | Error e -> Expect.stringContains e "already holds" "the collision guard names the occupied stream"
              | Ok _ -> failtest "loading into an occupied scope must be refused"
          } ]
