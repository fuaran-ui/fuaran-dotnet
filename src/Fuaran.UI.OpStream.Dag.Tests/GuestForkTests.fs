module Fuaran.UI.OpStream.Dag.Tests.GuestForkTests

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.Merge
open Fuaran.UI.OpStream.Dag.InMemory
open Fuaran.UI.OpStream.TestSupport

// ============================================================================
//  Phase 267 — guest op-stream fork + host-convergence anchor (§4o).
//
//  A mounted guest forks its own `guest-<scopeId>` stream, anchored to the host
//  `Mount` creation op so the merge reconciles the two over a well-defined merge
//  base (the Mount op, Core's `Dag.mergeBase`). These tests exercise every
//  acceptance criterion: distinct guest stream / host stream untouched;
//  genesis-as-DAG-child-of-Mount-op with the Mount op as the merge base;
//  interior replay from the guest stream alone; an overlap is refused, or
//  settled by the host's declared precedence, never silently; per-guest
//  provenance.
// ============================================================================

/// Style the left / right dashboard pane to a tone (a `(NodeId, style.tone)` cell).
let private styleLeft (t: ToneVariant) : TreeOp<TestMsg> =
    TreeOp.UpdateStyle(NodeId "left", { Defaults.style with Tone = t })

let private styleRight (t: ToneVariant) : TreeOp<TestMsg> =
    TreeOp.UpdateStyle(NodeId "right", { Defaults.style with Tone = t })

/// The host op that instantiates the guest — inserts the mount region. Its
/// content hash is the anchor the guest genesis links to.
let private insMount: TreeOp<TestMsg> =
    TreeOp.InsertChild(NodeId "dash", Fuaran.markdown "mount-g1" "guest region")

let private childIds (tree: Node<TestMsg>) : string list =
    match tree.Kind with
    | NodeKind.Box(spec) -> spec.Children |> List.map (fun c -> let s = c.Id in s)
    | _ -> []

let private records (sink: IDagOpStreamSink<TestMsg>) (streamId: string) : DagOpRecord<TestMsg> list =
    sink.Records streamId |> Async.RunSynchronously

[<Tests>]
let tests =
    testList
        "Phase267.GuestFork"
        [ test "guest stream keying round-trips scope id, host streams are not guest streams" {
              Expect.equal (GuestStream.streamId "g1") "guest-g1" "streamId prefixes the scope id"
              Expect.isTrue (GuestStream.isGuestStream "guest-g1") "guest stream recognised"
              Expect.isFalse (GuestStream.isGuestStream "host") "host stream is not a guest stream"
              Expect.equal (GuestStream.tryScopeOf "guest-g1") (Some "g1") "scope id projects back out"
              Expect.equal (GuestStream.tryScopeOf "host") None "host stream carries no guest scope"
          }

          test "guest genesis is a DAG child of the Mount op; host stream holds only host ops" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let mountOp = stepRecord "host" None insMount 1L
              add sink mountOp

              let g0 =
                  GuestFork.genesis
                      "g1"
                      mountOp.Hash
                      (styleLeft ToneVariant.Critical)
                      None
                      (Actor.Human "guest")
                      (timestamp 2L)
                      OpResultEnvelope.Success

              add sink g0

              Expect.equal g0.StreamId "guest-g1" "guest genesis lands on the guest stream"
              Expect.equal g0.Parents [ mountOp.Hash ] "guest genesis is a DAG child of the Mount creation op"

              Expect.equal
                  (records sink "host" |> List.map _.Hash)
                  [ mountOp.Hash ]
                  "host stream contains only host ops"

              Expect.equal
                  (records sink "guest-g1" |> List.map _.StreamId |> List.distinct)
                  [ "guest-g1" ]
                  "guest ops append to the distinct guest stream"
          }

          test "the merge base of host + guest heads is the Mount creation op" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let mountOp = stepRecord "host" None insMount 1L
              add sink mountOp
              let hostHead = stepRecord "host" (Some mountOp) (styleRight ToneVariant.Success) 2L
              add sink hostHead

              let g0 =
                  GuestFork.genesis
                      "g1"
                      mountOp.Hash
                      (styleLeft ToneVariant.Critical)
                      None
                      (Actor.Human "guest")
                      (timestamp 3L)
                      OpResultEnvelope.Success

              add sink g0

              let union = DagOpRecord.toDag (records sink "host" @ records sink "guest-g1")

              Expect.equal
                  (Fuaran.Core.Dag.mergeBase union hostHead.Hash g0.Hash)
                  (Some mountOp.Hash)
                  "the merge base of host + guest heads is the Mount op"
          }

          test "disjoint host + guest edits converge via the shipped merge into one tree" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let initial = buildDashboard ()
              let mountOp = stepRecord "host" None insMount 1L
              add sink mountOp
              let hostHead = stepRecord "host" (Some mountOp) (styleRight ToneVariant.Success) 2L
              add sink hostHead

              let g0 =
                  GuestFork.genesis
                      "g1"
                      mountOp.Hash
                      (styleLeft ToneVariant.Critical)
                      None
                      (Actor.Human "guest")
                      (timestamp 3L)
                      OpResultEnvelope.Success

              add sink g0

              match
                  GuestConvergence.converge sink "host" "g1" initial hostHead.Hash g0.Hash (timestamp 100L)
                  |> Async.RunSynchronously
              with
              | MergeResult.Merged(record, tree) ->
                  Expect.equal record.Parents [ hostHead.Hash; g0.Hash ] "merge node parents = [host head; guest head]"
                  Expect.equal record.StreamId "host" "merge node lands on the host stream"

                  let expected =
                      initial
                      |> applyOk insMount
                      |> applyOk (styleRight ToneVariant.Success)
                      |> applyOk (styleLeft ToneVariant.Critical)

                  Expect.equal
                      (canonical tree)
                      (canonical expected)
                      "merged tree carries both host + guest edits over the shared Mount base"
              | other -> failtestf "expected Merged, got %A" other
          }

          test "host makes no op past the Mount — the guest branch fast-forwards" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let initial = buildDashboard ()
              let mountOp = stepRecord "host" None insMount 1L
              add sink mountOp

              let g0 =
                  GuestFork.genesis
                      "g1"
                      mountOp.Hash
                      (styleLeft ToneVariant.Critical)
                      None
                      (Actor.Human "guest")
                      (timestamp 2L)
                      OpResultEnvelope.Success

              add sink g0

              let g1 =
                  GuestFork.step
                      "g1"
                      g0.Hash
                      (styleRight ToneVariant.Success)
                      None
                      (Actor.Human "guest")
                      (timestamp 3L)
                      OpResultEnvelope.Success

              add sink g1

              match
                  GuestConvergence.converge sink "host" "g1" initial mountOp.Hash g1.Hash (timestamp 100L)
                  |> Async.RunSynchronously
              with
              | MergeResult.FastForward h -> Expect.equal h g1.Hash "fast-forward to the guest head"
              | other -> failtestf "expected FastForward, got %A" other
          }

          test
              "an overlapping host + guest edit to the same cell is refused without precedence, never silently overwritten" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let initial = buildDashboard ()
              let mountOp = stepRecord "host" None insMount 1L
              add sink mountOp
              // Host edits the LEFT pane tone ...
              let hostHead = stepRecord "host" (Some mountOp) (styleLeft ToneVariant.Brand) 2L
              add sink hostHead
              // ... and the guest edits the SAME cell to a different value.
              let g0 =
                  GuestFork.genesis
                      "g1"
                      mountOp.Hash
                      (styleLeft ToneVariant.Critical)
                      None
                      (Actor.Human "guest")
                      (timestamp 3L)
                      OpResultEnvelope.Success

              add sink g0

              match
                  GuestConvergence.convergeWith
                      MergeContext.defaults
                      sink
                      "host"
                      "g1"
                      initial
                      hostHead.Hash
                      g0.Hash
                      (timestamp 100L)
                  |> Async.RunSynchronously
              with
              | MergeResult.Refused(ReconcileFault.Refused refusals) ->
                  Expect.isTrue
                      (refusals
                       |> List.exists (fun r -> r.Class = RefusalClass.Conflict && r.Conflict.Address = "left"))
                      "the contended left pane is named as a Conflict"
              | other -> failtestf "expected Refused, got %A" other
          }

          test "under the host's precedence the overlap merges to the host's value, recorded in the script" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let initial = buildDashboard ()
              let mountOp = stepRecord "host" None insMount 1L
              add sink mountOp
              let hostHead = stepRecord "host" (Some mountOp) (styleLeft ToneVariant.Brand) 2L
              add sink hostHead

              let g0 =
                  GuestFork.genesis
                      "g1"
                      mountOp.Hash
                      (styleLeft ToneVariant.Critical)
                      None
                      (Actor.Human "guest")
                      (timestamp 3L)
                      OpResultEnvelope.Success

              add sink g0

              match
                  GuestConvergence.converge sink "host" "g1" initial hostHead.Hash g0.Hash (timestamp 100L)
                  |> Async.RunSynchronously
              with
              | MergeResult.Merged(record, tree) ->
                  let expected = initial |> applyOk insMount |> applyOk (styleLeft ToneVariant.Brand)
                  Expect.equal (canonical tree) (canonical expected) "the host's value wins"

                  match record.Op with
                  | TreeOp.Batch script ->
                      Expect.contains
                          (script |> List.map CanonicalJson.encodeOp)
                          (CanonicalJson.encodeOp (styleLeft ToneVariant.Brand))
                          "the re-asserted host op is in the merge node's script"
                  | other -> failtestf "a merge node's op is its script, got %A" other
              | other -> failtestf "expected Merged, got %A" other
          }

          test "a guest interior is reconstructed from its own stream alone" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let mountOp = stepRecord "host" None insMount 1L
              add sink mountOp

              let g0 =
                  GuestFork.genesis
                      "g1"
                      mountOp.Hash
                      (styleLeft ToneVariant.Critical)
                      None
                      (Actor.Human "guest")
                      (timestamp 2L)
                      OpResultEnvelope.Success

              add sink g0

              let g1 =
                  GuestFork.step
                      "g1"
                      g0.Hash
                      (styleRight ToneVariant.Success)
                      None
                      (Actor.Human "guest")
                      (timestamp 3L)
                      OpResultEnvelope.Success

              add sink g1

              // The guest's OWN seed tree at instantiation (not the host tree).
              let initialGuestTree = buildDashboard ()

              match GuestReplay.replayInterior (records sink "guest-g1") initialGuestTree g1.Hash with
              | Ok tree ->
                  let expected =
                      initialGuestTree
                      |> applyOk (styleLeft ToneVariant.Critical)
                      |> applyOk (styleRight ToneVariant.Success)

                  Expect.equal (canonical tree) (canonical expected) "interior = the guest's own ops over its own seed"
                  // The host-side Mount op (which inserts `mount-g1`) is NOT followed
                  // through the anchor — proving the interior came from the guest
                  // stream alone.
                  Expect.equal (childIds tree) [ "left"; "right" ] "interior excludes the host-side Mount insert"
              | Error e -> failtestf "replayInterior failed: %A" e
          }

          test "a guest-stream merge node replays through the interior fold to the merged tree" {
              // Phase 2043 made the guest fold the DAG fold; since Phase 2645 that is
              // Core's closure fold, so a merge node on a guest stream is addressed
              // like any node and replays to exactly the tree the merge derived.
              let mountOp = stepRecord "host" None insMount 1L

              let g0 =
                  GuestFork.genesis
                      "g1"
                      mountOp.Hash
                      (styleLeft ToneVariant.Critical)
                      None
                      (Actor.Human "guest")
                      (timestamp 2L)
                      OpResultEnvelope.Success

              let ga =
                  GuestFork.step
                      "g1"
                      g0.Hash
                      (styleRight ToneVariant.Success)
                      None
                      (Actor.Human "guest")
                      (timestamp 3L)
                      OpResultEnvelope.Success

              let gb =
                  GuestFork.step
                      "g1"
                      g0.Hash
                      (TreeOp.InsertChild(NodeId "dash", Fuaran.markdown "extra" "Extra"))
                      None
                      (Actor.Human "guest")
                      (timestamp 4L)
                      OpResultEnvelope.Success

              let seed = buildDashboard ()
              let guest = [ g0; ga; gb ]

              match
                  (DagMerge.mergeDag
                      MergeContext.defaults
                      g0.StreamId
                      seed
                      (DagOpRecord.toDag guest)
                      ga.Hash
                      gb.Hash
                      (timestamp 5L))
                      .Result
              with
              | MergeResult.Merged(record, tree) ->
                  Expect.equal (DagOpRecord.recomputeHash record) record.Hash "the merge node is content-addressed"
                  Expect.equal record.Parents [ ga.Hash; gb.Hash ] "the merge node's parents are the two guest heads"

                  match GuestReplay.replayInterior (guest @ [ record ]) seed record.Hash with
                  | Ok replayed ->
                      Expect.equal (canonical replayed) (canonical tree) "the interior fold reaches the merged tree"
                  | Error e -> failtestf "replayInterior failed: %A" e
              | other -> failtestf "expected Merged, got %A" other
          }

          test "a guest op resolves its (scope, prompt) provenance; a host op carries no guest scope" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let mountOp = stepRecord "host" None insMount 1L
              add sink mountOp

              let g0 =
                  GuestFork.genesis
                      "g1"
                      mountOp.Hash
                      (styleLeft ToneVariant.Critical)
                      (Some "prompt-42")
                      (Actor.Human "guest")
                      (timestamp 2L)
                      OpResultEnvelope.Success

              Expect.equal (GuestFork.provenance g0) (Some "g1", Some "prompt-42") "guest op resolves (scope, prompt)"
              Expect.equal (GuestFork.provenance mountOp) (None, None) "host op has no guest scope"
          }

          test "guest fork rides only the DAG packages — no orchestration dependency leaks in" {
              // A structural smoke check: the whole guest-fork surface is reachable
              // from the DAG packages alone (Abstractions + Dag.Abstractions +
              // Dag.Merge), which the light/linear path never references (rung-4).
              let scoped = GuestStream.streamId "region"
              Expect.isTrue (GuestStream.isGuestStream scoped) "keying reachable from the linear abstractions"
          } ]
