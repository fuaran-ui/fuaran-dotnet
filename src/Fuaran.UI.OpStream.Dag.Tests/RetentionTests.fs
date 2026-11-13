module Fuaran.UI.OpStream.Dag.Tests.RetentionTests

open System
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.InMemory
open Fuaran.UI.OpStream.Dag.Merge
open Fuaran.UI.OpStream.TestSupport

// ============================================================================
//  Retention MECHANISM (Phase 178 acceptance #4) + resume-coordinate
//  forward-compat (task 6).
//
//  Fixture: a linear-ish DAG  g ── a ── b ── c  plus a side branch  a ── d
//  off `a`. Reachability and checkpoints are Core's, over the sink's records.
// ============================================================================

let private brand =
    TreeOp.UpdateStyle(
        leftChildId,
        { Defaults.style with
            Tone = ToneVariant.Brand }
    )

let private success =
    TreeOp.UpdateStyle(
        rightChildId,
        { Defaults.style with
            Tone = ToneVariant.Success }
    )

let private fixture () =
    let sink = InMemoryDagSink.create<TestMsg> ()
    let g = stepRecord "s" None brand 1L
    let a = stepRecord "s" (Some g) success 2L
    let b = stepRecord "s" (Some a) (TreeOp.RemoveNode rightChildId) 3L

    let c =
        stepRecord "s" (Some b) (TreeOp.InsertChild(dashboardId, Fuaran.markdown "x" "X")) 4L

    let d = stepRecord "s" (Some a) (TreeOp.RemoveNode leftChildId) 5L
    add sink g
    add sink a
    add sink b
    add sink c
    add sink d
    sink, g, a, b, c, d

/// Core's DAG over the sink's live records (synchronous).
let private dagOf (sink: IDagOpStreamSink<TestMsg>) : Fuaran.Core.Dag.T<StreamEntry<TestMsg>> =
    sink.Records "s" |> Async.RunSynchronously |> DagOpRecord.toDag

let private okOr (what: string) (r: Result<'T, 'E>) : 'T =
    match r with
    | Ok v -> v
    | Error e -> failtestf "%s failed: %A" what e

[<Tests>]
let tests =
    testList
        "Dag.Retention"
        [ test "pruning a leaf moves it to the skeleton and leaves the live set verifiable" {
              let sink, _, a, _, _, d = fixture ()

              // Prune `d` (a side-branch tip). Its address + parents survive in the
              // skeleton; its payload leaves the live set.
              Expect.isTrue (sink.Tombstone("s", d.Hash) |> Async.RunSynchronously) "pruned"

              let recs = sink.Records "s" |> Async.RunSynchronously
              Expect.isFalse (recs |> List.exists (fun r -> r.Hash = d.Hash)) "d left the live records"

              let skeleton = sink.Skeleton "s" |> Async.RunSynchronously

              Expect.equal
                  (skeleton |> List.map (fun n -> n.Hash, n.Parents))
                  [ d.Hash, [ a.Hash ] ]
                  "d is in the skeleton"

              Expect.equal
                  (sink.Parents("s", d.Hash) |> Async.RunSynchronously)
                  [ a.Hash ]
                  "Parents still answers for a pruned node"

              match DagVerify.records recs with
              | Ok() -> ()
              | Error e -> failtestf "chain must still verify after pruning: %A" e
          }

          test "compaction: pruning the history behind a checkpoint keeps the live tail readable and replayable" {
              let sink, g, a, b, c, _ = fixture ()

              // A checkpoint at b, the history behind it pruned (Core's compaction keeps b).
              let cp =
                  DagFold.checkpointAt (buildDashboard ()) (dagOf sink) b.Hash
                  |> okOr "checkpoint"

              for h in [ g.Hash; a.Hash ] do
                  Expect.isTrue (sink.Tombstone("s", h) |> Async.RunSynchronously) "pruned behind the checkpoint"

              // Verify-on-read resolves b's pruned parent against the skeleton.
              let live = sink.Records "s" |> Async.RunSynchronously
              Expect.isFalse (live |> List.exists (fun r -> r.Hash = a.Hash)) "a left the live set"

              let expected =
                  DagFold.replayTo (buildDashboard ()) (DagOpRecord.toDag [ g; a; b; c ]) c.Hash
                  |> okOr "full replay"

              let fromCheckpoint =
                  DagFold.replayFrom cp (DagOpRecord.toDag live) c.Hash
                  |> okOr "replay from the checkpoint"

              Expect.equal (canonical fromCheckpoint) (canonical expected) "the tail replays from the checkpoint"
          }

          test "reachability: GC roots keep their ancestors live, others are prunable" {
              let sink, _, _, _, c, d = fixture ()
              let dag = dagOf sink

              // Keep only head `c` as a root: g,a,b,c are live; d is prunable.
              let roots =
                  { Heads = [ c.Hash ]
                    Checkpoints = []
                    ResumeCoordinates = [] }

              let prunable =
                  Fuaran.Core.Dag.prunable
                      (Fuaran.Core.Dag.Reach.ofDag dag)
                      (DagRetention.rootHashes roots |> Set.toList)
                  |> okOr "prunable"

              Expect.equal prunable [ d.Hash ] "only the side branch d is prunable"
          }

          test "a branch referenced by a live resume-coordinate is GC-protected" {
              let sink, _, _, _, c, d = fixture ()
              let dag = dagOf sink

              // Head is `c`; a permalink / resume-coordinate pins the side
              // branch tip `d`. With only head `c`, d would be prunable; the
              // resume coordinate protects it.
              let roots =
                  { Heads = [ c.Hash ]
                    Checkpoints = []
                    ResumeCoordinates = [ ResumeCoordinate.DagNodeHash d.Hash ] }

              let prunable =
                  Fuaran.Core.Dag.prunable
                      (Fuaran.Core.Dag.Reach.ofDag dag)
                      (DagRetention.rootHashes roots |> Set.toList)
                  |> okOr "prunable"

              Expect.isEmpty prunable "d is not prunable, and nothing else is"
          }

          test "checkpoint-bounded replay folds only the tail past the checkpoint node" {
              let sink, _, a, _, c, _ = fixture ()
              let initial = buildDashboard ()
              let dag = dagOf sink

              let full = DagFold.replayTo initial dag c.Hash |> okOr "full replay"
              let checkpoint = DagFold.checkpointAt initial dag a.Hash |> okOr "checkpointAt"
              Expect.isOk (DagFold.verifyCheckpoint checkpoint dag) "an honest checkpoint verifies"

              let bounded = DagFold.replayFrom checkpoint dag c.Hash |> okOr "replayFrom"
              Expect.equal (canonical bounded) (canonical full) "checkpoint-bounded replay == full replay"
          }

          test "TAMPER: a checkpoint whose State was swapped is REFUSED, not replayed" {
              let sink, g, a, _, _, _ = fixture ()
              let initial = buildDashboard ()
              let dag = dagOf sink

              let honest = DagFold.checkpointAt initial dag a.Hash |> okOr "checkpointAt"

              // A DIFFERENT tree (the state at `g`, one op earlier) is substituted
              // for the state while the checkpoint keeps the honest seal.
              let forged = DagFold.replayTo initial dag g.Hash |> okOr "forged replay"

              Expect.notEqual (canonical forged) (canonical honest.State) "the forged state really is a different tree"

              let tampered = { honest with State = forged }

              match DagFold.verifyCheckpoint tampered dag with
              | Error(Fuaran.Core.Dag.CheckpointBreak.Seal(expected, got)) ->
                  Expect.notEqual expected got "the stored and recomputed seals differ"
              | other -> failtestf "expected a Seal break, got %A" other
          }

          test "TAMPER: a genuine checkpoint moved to a DIFFERENT DAG node is REFUSED" {
              // Cross-position substitution: the state and seal are both genuine;
              // only the `Node` moved. The seal binds the node, so it must fail.
              let sink, _, a, b, _, _ = fixture ()
              let initial = buildDashboard ()
              let dag = dagOf sink

              let moved =
                  { (DagFold.checkpointAt initial dag a.Hash |> okOr "checkpointAt") with
                      Node = b.Hash }

              match DagFold.verifyCheckpoint moved dag with
              | Error(Fuaran.Core.Dag.CheckpointBreak.Seal _) -> ()
              | other -> failtestf "expected a Seal break, got %A" other
          }

          // Phase 2652 — one checkpoint seal in the tier. A DAG checkpoint is Core's strict
          // `Sealed.seal` at the anchor `{ Seq = 0; PrevHash = node }`: the linear seal
          // (`CheckpointSeal.seal`) at sequence zero hung from the node, with no second pre-image.
          // The fixed vector is WIRE_FORMAT.md §5.3.6's worked example.
          test "a DAG checkpoint's hash is Core's strict seal, the linear seal at sequence zero from its node" {
              let node = "4624bd23859bd2de3ccdfebaa9f460cc4a8a962e8630a2ce2baa6f543f646192"

              let state =
                  match
                      Fuaran.UI.Ops.JsonDecode.decodeNodeObj
                          """{"id":"markdown-1","kind":{"$type":"Markdown","text":"Updated hourly."}}"""
                  with
                  | Ok n -> n
                  | Error e -> failtestf "state decode failed: %A" e

              let atNode = DagFold.sealAt node state

              let core =
                  Fuaran.Core.Sealed.seal
                      Fuaran.Core.SnapshotMode.Strict
                      StreamEntry.hashFn
                      CanonicalJson.encodeNode
                      { Seq = 0; PrevHash = node }
                      state

              Expect.equal atNode.Hash core.Seal "the DAG seal is Core's strict Sealed.seal at (0, node)"
              Expect.equal atNode.Hash (CheckpointSeal.seal node 0 state) "the DAG seal is the linear seal"

              Expect.equal
                  atNode.Hash
                  "8d28ad3ab53318a0d6e15e809b12c1415bfbf514b8aca952ec5427cad048d63c"
                  "the WIRE_FORMAT.md 5.3.6 worked example, byte for byte"

              // And a checkpoint the fold takes carries the same seal over the state it folded.
              let sink, _, a, _, _, _ = fixture ()

              let taken =
                  DagFold.checkpointAt (buildDashboard ()) (dagOf sink) a.Hash
                  |> okOr "checkpointAt"

              Expect.equal
                  taken.Hash
                  (CheckpointSeal.seal a.Hash 0 taken.State)
                  "a folded checkpoint seals the same way"
          }

          test "resume coordinate is linear↔DAG forward-compatible behind one envelope shape" {
              // The SAME envelope record carries either coordinate variant — the
              // Phase-177 linear shape upgrades to a content-addressed DAG node
              // with no shape change.
              let linear: ResumeCoordinate = ResumeCoordinate.LinearOpIndex 5
              let dag: ResumeCoordinate = ResumeCoordinate.DagNodeHash "deadbeef"

              // Linear coordinate contributes no DAG GC root; DAG coordinate does.
              Expect.equal (ResumeCoordinate.dagRoot linear) None "linear op-index is not a DAG root"
              Expect.equal (ResumeCoordinate.dagRoot dag) (Some "deadbeef") "DAG node hash is a GC root"

              // Both live in one GcRoots.ResumeCoordinates list — same field type.
              let roots =
                  { Heads = []
                    Checkpoints = []
                    ResumeCoordinates = [ linear; dag ] }

              Expect.equal (DagRetention.rootHashes roots) (Set.ofList [ "deadbeef" ]) "only the DAG coordinate roots"
          }

          // ── Retention POLICY tier (Phase 179) ──────────────────────────────
          //
          // The side branch `d` (off `a`) is unreachable when only head `c` is a
          // GC root — the mechanism marks it prunable. The policy tier decides
          // whether its CONTENT is GC-eligible NOW, under grace / auto-pin, while
          // the rejection DECISION is kept permanently.

          test "policy: rejected content is GC-eligible only after the grace window elapses" {
              let sink, _, a, _, c, d = fixture ()
              let dag = dagOf sink

              let roots =
                  { Heads = [ c.Hash ]
                    Checkpoints = []
                    ResumeCoordinates = [] }

              // `d` was a rejected AI proposal off `a`, rejected at t=100.
              let rejections =
                  [ { ProposalHash = d.Hash
                      ProposedAtHash = a.Hash
                      Reason = "off-brand restyle"
                      RejectedAt = timestamp 100L } ]

              let policy = RetentionPolicy.withGrace (TimeSpan.FromSeconds 60.0)

              // 30s after rejection — still inside grace, NOT eligible.
              let early =
                  DagRetentionPolicy.contentGcEligible policy (timestamp 130L) rejections dag roots
                  |> okOr "contentGcEligible"

              Expect.isFalse (early.Contains d.Hash) "within grace ⇒ content held live"

              // 90s after rejection — past grace, eligible.
              let late =
                  DagRetentionPolicy.contentGcEligible policy (timestamp 190L) rejections dag roots
                  |> okOr "contentGcEligible"

              Expect.equal late (Set.ofList [ d.Hash ]) "past grace ⇒ content GC-eligible"
          }

          test "policy: auto-pin protects content from GC regardless of reachability or grace" {
              let sink, _, a, _, c, d = fixture ()
              let dag = dagOf sink

              let roots =
                  { Heads = [ c.Hash ]
                    Checkpoints = []
                    ResumeCoordinates = [] }

              let rejections =
                  [ { ProposalHash = d.Hash
                      ProposedAtHash = a.Hash
                      Reason = "rejected"
                      RejectedAt = timestamp 100L } ]

              // Auto-pin `d` (e.g. "keep this branch for audit"); grace long past.
              let policy =
                  { RetentionPolicy.immediate with
                      AutoPin = (fun h -> h = d.Hash) }

              let eligible =
                  DagRetentionPolicy.contentGcEligible policy (timestamp 9_999L) rejections dag roots
                  |> okOr "contentGcEligible"

              Expect.isFalse (eligible.Contains d.Hash) "auto-pinned content is never GC-eligible"
          }

          test "policy: the purge trigger gates whether eligible content is swept this pass" {
              let sink, _, a, _, c, d = fixture ()
              let dag = dagOf sink

              let roots =
                  { Heads = [ c.Hash ]
                    Checkpoints = []
                    ResumeCoordinates = [] }

              let rejections =
                  [ { ProposalHash = d.Hash
                      ProposedAtHash = a.Hash
                      Reason = "rejected"
                      RejectedAt = timestamp 100L } ]

              // Trigger fires only when ≥2 records are eligible; here exactly 1 is.
              let deferred =
                  { RetentionPolicy.immediate with
                      PurgeWhen = (fun s -> Set.count s >= 2) }

              Expect.isEmpty
                  (DagRetentionPolicy.purgeBatch deferred (timestamp 200L) rejections dag roots
                   |> okOr "purgeBatch")
                  "below the purge threshold ⇒ candidates wait for a later sweep"

              // The default immediate trigger sweeps the single eligible record.
              Expect.equal
                  (DagRetentionPolicy.purgeBatch RetentionPolicy.immediate (timestamp 200L) rejections dag roots
                   |> okOr "purgeBatch")
                  (Set.ofList [ d.Hash ])
                  "immediate trigger sweeps the eligible content"
          }

          test
              "decision/content split: pruning the content keeps the chain verifiable; the decision is a separate permanent record" {
              let sink, _, a, _, _, d = fixture ()

              // The permanent DECISION half — a host-persisted side-record.
              let decision =
                  { ProposalHash = d.Hash
                    ProposedAtHash = a.Hash
                    Reason = "human rejected the AI restyle"
                    RejectedAt = timestamp 100L }

              // Purge the CONTENT half via the mechanism's tombstone.
              Expect.isTrue (sink.Tombstone("s", d.Hash) |> Async.RunSynchronously) "content tombstoned"

              let recs = sink.Records "s" |> Async.RunSynchronously

              match DagVerify.records recs with
              | Ok() -> ()
              | Error e -> failtestf "chain must verify after content purge: %A" e

              // The decision outlives the content: it still names the proposal +
              // reason though the node payload is gone.
              Expect.isFalse (recs |> List.exists (fun r -> r.Hash = d.Hash)) "content payload dropped"

              Expect.isTrue
                  (sink.Skeleton "s"
                   |> Async.RunSynchronously
                   |> List.exists (fun n -> n.Hash = d.Hash))
                  "the node's address survives in the skeleton"

              Expect.equal decision.ProposalHash d.Hash "decision still points at the (now content-less) proposal node"
              Expect.equal decision.Reason "human rejected the AI restyle" "decision reason preserved permanently"
          } ]
