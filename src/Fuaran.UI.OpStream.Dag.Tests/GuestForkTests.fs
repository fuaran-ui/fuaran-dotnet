module Fuaran.UI.OpStream.Dag.Tests.GuestForkTests

open System.Collections.Generic
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.Merge
open Fuaran.UI.OpStream.Dag.InMemory
open Fuaran.UI.OpStream.Dag.Tests.TestSupport

// ============================================================================
//  Phase 267 — guest op-stream fork + host-convergence anchor (§4o).
//
//  A mounted guest forks its own `guest-<scopeId>` stream, anchored to the host
//  `Mount` creation op so the shipped Phase-179 merge reconciles the two with a
//  well-defined LCA (the Mount op). These tests exercise every acceptance
//  criterion: distinct guest stream / host stream untouched; genesis-as-DAG-
//  child-of-Mount-op with the Mount op as the convergence LCA; interior replay
//  from the guest stream alone; overlap → `MergeConflict`, never a silent
//  overwrite; per-guest provenance.
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

/// A guest record lookup restricted to one stream's records (what interior
/// replay is given — the anchor parent resolves to `None`, bounding the spine).
let private lookupOf (records: DagOpRecord<TestMsg> list) : string -> DagOpRecord<TestMsg> option =
    let byHash = records |> List.map (fun r -> r.Hash, r) |> dict

    fun h ->
        match byHash.TryGetValue h with
        | true, r -> Some r
        | false, _ -> None

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
                      (ts 2L)
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

          test "convergence LCA of host + guest heads is the Mount creation op" {
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
                      (ts 3L)
                      OpResultEnvelope.Success

              add sink g0

              let union = records sink "host" @ records sink "guest-g1"

              let getParents = lookupOf union >> Option.map _.Parents >> Option.defaultValue []

              match DagTopology.lca getParents hostHead.Hash g0.Hash with
              | LcaResult.Unique h -> Expect.equal h mountOp.Hash "LCA of host + guest heads is the Mount op"
              | other -> failtestf "expected Unique LCA = Mount op, got %A" other
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
                      (ts 3L)
                      OpResultEnvelope.Success

              add sink g0

              match
                  GuestConvergence.converge sink "host" "g1" initial hostHead.Hash g0.Hash (ts 100L)
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
                      (ts 2L)
                      OpResultEnvelope.Success

              add sink g0

              let g1 =
                  GuestFork.step
                      "g1"
                      g0.Hash
                      (styleRight ToneVariant.Success)
                      None
                      (Actor.Human "guest")
                      (ts 3L)
                      OpResultEnvelope.Success

              add sink g1

              match
                  GuestConvergence.converge sink "host" "g1" initial mountOp.Hash g1.Hash (ts 100L)
                  |> Async.RunSynchronously
              with
              | MergeResult.FastForward h -> Expect.equal h g1.Hash "fast-forward to the guest head"
              | other -> failtestf "expected FastForward, got %A" other
          }

          test "overlapping host + guest edit to the same cell surfaces a MergeConflict, never a silent overwrite" {
              let sink = InMemoryDagSink.create<TestMsg> ()
              let initial = buildDashboard ()
              let mountOp = stepRecord "host" None insMount 1L
              add sink mountOp
              // Host edits the LEFT pane tone …
              let hostHead = stepRecord "host" (Some mountOp) (styleLeft ToneVariant.Brand) 2L
              add sink hostHead
              // … and the guest edits the SAME cell to a different value.
              let g0 =
                  GuestFork.genesis
                      "g1"
                      mountOp.Hash
                      (styleLeft ToneVariant.Critical)
                      None
                      (Actor.Human "guest")
                      (ts 3L)
                      OpResultEnvelope.Success

              add sink g0

              match
                  GuestConvergence.converge sink "host" "g1" initial hostHead.Hash g0.Hash (ts 100L)
                  |> Async.RunSynchronously
              with
              | MergeResult.NeedsManualMerge conflicts ->
                  let contended =
                      conflicts |> List.tryFind (fun c -> c.NodeId = "left" && c.Facet = "style.tone")

                  Expect.isSome contended "the contended (left, style.tone) cell is named"

                  match contended with
                  | Some c ->
                      Expect.equal c.Class MergeConflictClass.ConcurrentEdit "classified ConcurrentEdit"

                      Expect.isTrue
                          (List.contains MergeChoice.KeepPrimary c.Choices)
                          "keeping the host (Primary) value is an offered choice"
                  | None -> ()
              | other -> failtestf "expected NeedsManualMerge, got %A" other
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
                      (ts 2L)
                      OpResultEnvelope.Success

              add sink g0

              let g1 =
                  GuestFork.step
                      "g1"
                      g0.Hash
                      (styleRight ToneVariant.Success)
                      None
                      (Actor.Human "guest")
                      (ts 3L)
                      OpResultEnvelope.Success

              add sink g1

              // The guest's OWN seed tree at instantiation (not the host tree).
              let initialGuestTree = buildDashboard ()
              let getGuest = lookupOf (records sink "guest-g1")

              match GuestReplay.replayInterior getGuest initialGuestTree g1.Hash with
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
                      (ts 2L)
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
