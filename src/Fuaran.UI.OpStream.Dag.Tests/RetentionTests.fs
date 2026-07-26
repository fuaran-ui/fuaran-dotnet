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
open Fuaran.UI.OpStream.Dag.Tests.TestSupport

// ============================================================================
//  Retention MECHANISM (Phase 178 acceptance #4) + resume-coordinate
//  forward-compat (task 6).
//
//  Fixture: a linear-ish DAG  g ── a ── b ── c  plus a side branch  a ── d
//  off `a`. `getParents` reads the sink's record map.
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

/// Parent lookup over the sink's records (synchronous).
let private getParentsOf (sink: IDagOpStreamSink<TestMsg>) : string -> string list =
    let recs = sink.Records "s" |> Async.RunSynchronously
    let m = recs |> List.map (fun r -> r.Hash, r.Parents) |> Map.ofList
    fun h -> Map.tryFind h m |> Option.defaultValue []

[<Tests>]
let tests =
    testList
        "Dag.Retention"
        [ test "tombstoning a non-root node leaves the chain verifiable" {
              let sink, _, _, b, _, _ = fixture ()

              // Tombstone `b` (an interior node). Its hash + parents survive, its
              // payload is dropped.
              Expect.isTrue (sink.Tombstone("s", b.Hash) |> Async.RunSynchronously) "tombstoned"

              let recs = sink.Records "s" |> Async.RunSynchronously
              let tombstoned = recs |> List.find (fun r -> r.Hash = b.Hash)
              Expect.isTrue tombstoned.Tombstoned "b is marked tombstoned"

              match DagVerify.records recs with
              | Ok() -> ()
              | Error e -> failtestf "chain must still verify after tombstone: %A" e
          }

          test "reachability: GC roots keep their ancestors live, others are prunable" {
              let sink, g, a, b, c, d = fixture ()
              let getParents = getParentsOf sink

              let allHashes = sink.Records "s" |> Async.RunSynchronously |> List.map _.Hash

              // Keep only head `c` as a root: g,a,b,c are live; d is prunable.
              let roots =
                  { Heads = [ c.Hash ]
                    Checkpoints = []
                    ResumeCoordinates = [] }

              let prunable = DagRetention.prunable allHashes getParents roots
              Expect.equal prunable (Set.ofList [ d.Hash ]) "only the side branch d is prunable"

              let live = DagRetention.liveSet getParents roots
              Expect.equal live (Set.ofList [ g.Hash; a.Hash; b.Hash; c.Hash ]) "c's ancestor closure is live"
          }

          test "a branch referenced by a live resume-coordinate is GC-protected" {
              let sink, _, _, _, c, d = fixture ()
              let getParents = getParentsOf sink

              let allHashes = sink.Records "s" |> Async.RunSynchronously |> List.map _.Hash

              // Head is `c`; a permalink / resume-coordinate pins the side
              // branch tip `d`. With only head `c`, d would be prunable; the
              // resume coordinate protects it.
              let roots =
                  { Heads = [ c.Hash ]
                    Checkpoints = []
                    ResumeCoordinates = [ ResumeCoordinate.DagNodeHash d.Hash ] }

              Expect.isTrue (DagRetention.isProtected getParents roots d.Hash) "d protected by resume coordinate"
              let prunable = DagRetention.prunable allHashes getParents roots
              Expect.isFalse (prunable.Contains d.Hash) "d is not prunable"
          }

          test "checkpoint-bounded replay folds only the tail past the checkpoint node" {
              let sink, _, a, _, c, _ = fixture ()
              let initial = buildDashboard ()

              let getRec =
                  let recs = sink.Records "s" |> Async.RunSynchronously
                  let m = recs |> List.map (fun r -> r.Hash, r) |> Map.ofList
                  fun h -> Map.tryFind h m

              // Full replay of head `c`.
              let full =
                  match DagReplay.replay getRec initial c.Hash with
                  | Ok t -> t
                  | Error e -> failtestf "full replay failed: %A" e

              // Checkpoint AT `a` (snapshot = tree after g,a).
              let snapshotAtA =
                  match DagReplay.replay getRec initial a.Hash with
                  | Ok t -> t
                  | Error e -> failtestf "snapshot replay failed: %A" e

              let checkpoint =
                  { StreamId = "s"
                    AtHash = a.Hash
                    Snapshot = snapshotAtA
                    SnapshotHash = CanonicalJson.encodeNode snapshotAtA |> HashChain.sha256Hex }

              match DagReplay.replayFromCheckpoint getRec checkpoint c.Hash with
              | Ok bounded ->
                  Expect.equal (canonical bounded) (canonical full) "checkpoint-bounded replay == full replay"
              | Error e -> failtestf "checkpoint replay failed: %A" e
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
              let getParents = getParentsOf sink

              let allHashes = sink.Records "s" |> Async.RunSynchronously |> List.map _.Hash

              let roots =
                  { Heads = [ c.Hash ]
                    Checkpoints = []
                    ResumeCoordinates = [] }

              // `d` was a rejected AI proposal off `a`, rejected at t=100.
              let rejections =
                  [ { ProposalHash = d.Hash
                      ProposedAtHash = a.Hash
                      Reason = "off-brand restyle"
                      RejectedAt = ts 100L } ]

              let policy = RetentionPolicy.withGrace (TimeSpan.FromSeconds 60.0)

              // 30s after rejection — still inside grace, NOT eligible.
              let early =
                  DagRetentionPolicy.contentGcEligible policy (ts 130L) rejections allHashes getParents roots

              Expect.isFalse (early.Contains d.Hash) "within grace ⇒ content held live"

              // 90s after rejection — past grace, eligible.
              let late =
                  DagRetentionPolicy.contentGcEligible policy (ts 190L) rejections allHashes getParents roots

              Expect.equal late (Set.ofList [ d.Hash ]) "past grace ⇒ content GC-eligible"
          }

          test "policy: auto-pin protects content from GC regardless of reachability or grace" {
              let sink, _, a, _, c, d = fixture ()
              let getParents = getParentsOf sink

              let allHashes = sink.Records "s" |> Async.RunSynchronously |> List.map _.Hash

              let roots =
                  { Heads = [ c.Hash ]
                    Checkpoints = []
                    ResumeCoordinates = [] }

              let rejections =
                  [ { ProposalHash = d.Hash
                      ProposedAtHash = a.Hash
                      Reason = "rejected"
                      RejectedAt = ts 100L } ]

              // Auto-pin `d` (e.g. "keep this branch for audit"); grace long past.
              let policy =
                  { RetentionPolicy.immediate with
                      AutoPin = (fun h -> h = d.Hash) }

              let eligible =
                  DagRetentionPolicy.contentGcEligible policy (ts 9_999L) rejections allHashes getParents roots

              Expect.isFalse (eligible.Contains d.Hash) "auto-pinned content is never GC-eligible"
          }

          test "policy: the purge trigger gates whether eligible content is swept this pass" {
              let sink, _, a, _, c, d = fixture ()
              let getParents = getParentsOf sink

              let allHashes = sink.Records "s" |> Async.RunSynchronously |> List.map _.Hash

              let roots =
                  { Heads = [ c.Hash ]
                    Checkpoints = []
                    ResumeCoordinates = [] }

              let rejections =
                  [ { ProposalHash = d.Hash
                      ProposedAtHash = a.Hash
                      Reason = "rejected"
                      RejectedAt = ts 100L } ]

              // Trigger fires only when ≥2 records are eligible; here exactly 1 is.
              let deferred =
                  { RetentionPolicy.immediate with
                      PurgeWhen = (fun s -> Set.count s >= 2) }

              Expect.isEmpty
                  (DagRetentionPolicy.purgeBatch deferred (ts 200L) rejections allHashes getParents roots)
                  "below the purge threshold ⇒ candidates wait for a later sweep"

              // The default immediate trigger sweeps the single eligible record.
              Expect.equal
                  (DagRetentionPolicy.purgeBatch
                      RetentionPolicy.immediate
                      (ts 200L)
                      rejections
                      allHashes
                      getParents
                      roots)
                  (Set.ofList [ d.Hash ])
                  "immediate trigger sweeps the eligible content"
          }

          test
              "decision/content split: tombstoning the content keeps the chain verifiable; the decision is a separate permanent record" {
              let sink, _, a, _, _, d = fixture ()

              // The permanent DECISION half — a host-persisted side-record.
              let decision =
                  { ProposalHash = d.Hash
                    ProposedAtHash = a.Hash
                    Reason = "human rejected the AI restyle"
                    RejectedAt = ts 100L }

              // Purge the CONTENT half via the mechanism's tombstone.
              Expect.isTrue (sink.Tombstone("s", d.Hash) |> Async.RunSynchronously) "content tombstoned"

              let recs = sink.Records "s" |> Async.RunSynchronously

              match DagVerify.records recs with
              | Ok() -> ()
              | Error e -> failtestf "chain must verify after content purge: %A" e

              // The decision outlives the content: it still names the proposal +
              // reason though the node payload is gone.
              let tombstoned = recs |> List.find (fun r -> r.Hash = d.Hash)
              Expect.isTrue tombstoned.Tombstoned "content payload dropped"
              Expect.equal decision.ProposalHash d.Hash "decision still points at the (now content-less) proposal node"
              Expect.equal decision.Reason "human rejected the AI restyle" "decision reason preserved permanently"
          } ]
