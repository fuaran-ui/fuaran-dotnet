module Fuaran.UI.OpStream.Dag.Tests.MergeEntryPointTests

open System
open System.Collections.Generic
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
//  Phase 2062 — merge cost stops growing with shared history, and merge has
//  one entry point.
//
//  The LCA is a two-colour paint now, so it is checked against the definition
//  it replaced (the maximal elements of the common ancestor set, computed the
//  old way) over seeded random DAGs, and its walk is counted on a long chain.
//  `DagMerge.mergeLookups` is the one merge: the sink faces, the trunk loop and
//  guest convergence are wrappers over it, so the tests below hold each face
//  to it and pin the two things the wrappers newly carry — the context's
//  validator policy on the trunk path, and a trunk retry that fetches only what
//  the moved trunk added since its one load.
// ============================================================================

let private now = timestamp 8_000L

/// The pre-2062 `DagTopology.lca`, verbatim in effect: the common ancestor set,
/// reduced to the elements no other common element descends from.
let private referenceLca (getParents: string -> string list) (a: string) (b: string) : LcaResult =
    let reach = DagTopology.reachable getParents
    let common = Set.intersect (reach a) (reach b)

    let maximal =
        common
        |> Set.filter (fun c -> not (common |> Set.exists (fun d -> d <> c && (reach d).Contains c)))
        |> Set.toList

    match maximal with
    | [] -> LcaResult.None
    | [ single ] -> LcaResult.Unique single
    | many -> LcaResult.Ambiguous(List.sort many)

/// A seeded random DAG of `n` nodes: each node takes one to three parents from
/// the nodes before it (a few take none — extra roots), and now and then a
/// parent no node defines, which the relation must treat as a parentless node.
let private randomDag (seed: int) (n: int) : (string -> string list) * string list =
    let rng = Random seed
    let parents = Dictionary<string, string list>()
    let names = [ for i in 0 .. n - 1 -> $"n{i:D3}" ]

    names
    |> List.iteri (fun i name ->
        let ps =
            if i = 0 || rng.Next 12 = 0 then
                []
            else
                [ for _ in 1 .. 1 + rng.Next 3 ->
                      if rng.Next 40 = 0 then
                          $"ghost{rng.Next 3}"
                      else
                          names[rng.Next i] ]
                |> List.distinct

        parents[name] <- ps)

    let getParents h =
        match parents.TryGetValue h with
        | true, ps -> ps
        | false, _ -> []

    getParents, names

let private toneOp (id: NodeId) (tone: ToneVariant) : TreeOp<TestMsg> =
    TreeOp.UpdateStyle(id, { Defaults.style with Tone = tone })

/// At most one Brand pane per dashboard — a defect only a merge of two legal
/// branches can introduce (the `ValidatorGateTests` shape).
let private brandSiblingValidator: MergeValidator<TestMsg> =
    fun tree ->
        match tree.Kind with
        | NodeKind.Box spec ->
            let brand =
                spec.Children
                |> List.filter (fun c -> (c.Style |> Option.defaultValue Defaults.style).Tone = ToneVariant.Brand)

            if List.length brand > 1 then
                brand
                |> List.map (fun c ->
                    { Code = "TESTBRAND001"
                      NodeId = c.Id
                      Facet = "style.tone"
                      Message = "two Brand panes" })
            else
                []
        | _ -> []

/// A base `a`, then branch A restyling the left pane and branch B the right
/// one, each to `tone`.
let private disjointFixture (tone: ToneVariant) =
    let sink = InMemoryDagSink.create<TestMsg> ()
    let initial = buildDashboard ()
    let a = stepRecord "s" None (toneOp rightChildId ToneVariant.Default) 1L
    add sink a
    let branchA = stepRecord "s" (Some a) (toneOp leftChildId tone) 2L
    let branchB = stepRecord "s" (Some a) (toneOp rightChildId tone) 3L
    add sink branchA
    add sink branchB
    sink, initial, a, branchA, branchB

let private lookupsOf (sink: IDagOpStreamSink<TestMsg>) =
    let byHash =
        sink.Records "s"
        |> Async.RunSynchronously
        |> List.map (fun r -> r.Hash, r)
        |> dict

    let getRec h =
        match byHash.TryGetValue h with
        | true, r -> Some r
        | false, _ -> None

    getRec, (fun h -> getRec h |> Option.map _.Parents |> Option.defaultValue [])

let private resultKey (result: MergeResult<TestMsg>) : string =
    match result with
    | MergeResult.Merged(record, tree) -> $"merged {record.Hash} {canonical tree}"
    | MergeResult.NeedsManualMerge cells -> $"conflict {MergeConflict.encodeEnvelope cells}"
    | other -> sprintf "%A" other

[<Tests>]
let tests =
    testList
        "Dag.MergeEntryPoint"
        [ test "lca is the maximal common ancestors, on seeded random DAGs" {
              let mutable ambiguous = 0
              let mutable none = 0

              for seed in 1..40 do
                  let getParents, names = randomDag seed 48
                  let rng = Random(seed * 7919)

                  for _ in 1..40 do
                      let a = names[rng.Next names.Length]
                      let b = names[rng.Next names.Length]
                      let expected = referenceLca getParents a b

                      match expected with
                      | LcaResult.Ambiguous _ -> ambiguous <- ambiguous + 1
                      | LcaResult.None -> none <- none + 1
                      | LcaResult.Unique _ -> ()

                      Expect.equal (DagTopology.lca getParents a b) expected $"seed {seed}: lca {a} {b}"

              // The property is vacuous unless the generator reaches both
              // non-unique answers.
              Expect.isGreaterThan ambiguous 20 "criss-cross pairs were generated"
              Expect.isGreaterThan none 5 "pairs with no common ancestor were generated"
          }

          test "lca paints the divergence, not the shared history beneath it" {
              // A 5,000-record chain, then two three-record branches off its tip.
              let parents = Dictionary<string, string list>()
              parents["c0"] <- []

              for i in 1..4_999 do
                  parents[$"c{i}"] <- [ $"c{i - 1}" ]

              parents["a1"] <- [ "c4999" ]
              parents["a2"] <- [ "a1" ]
              parents["a3"] <- [ "a2" ]
              parents["b1"] <- [ "c4999" ]
              parents["b2"] <- [ "b1" ]
              parents["b3"] <- [ "b2" ]
              let mutable calls = 0

              let getParents h =
                  calls <- calls + 1

                  match parents.TryGetValue h with
                  | true, ps -> ps
                  | false, _ -> []

              Expect.equal (DagTopology.lca getParents "a3" "b3") (LcaResult.Unique "c4999") "the fork is the base"
              Expect.isLessThan calls 20 "the walk stops just below the fork"
          }

          test "the sink faces are mergeLookups over the stream's records" {
              for tone in [ ToneVariant.Success; ToneVariant.Brand ] do
                  let sink, initial, _, branchA, branchB = disjointFixture tone
                  let getRec, getParents = lookupsOf sink

                  let viaLookups =
                      DagMerge.mergeLookups
                          recordAuthor
                          getRec
                          getParents
                          "s"
                          initial
                          branchA.Hash
                          branchB.Hash
                          now
                          MergeContext.defaults

                  let viaSink =
                      DagMerge.merge recordAuthor sink "s" initial branchA.Hash branchB.Hash now
                      |> Async.RunSynchronously

                  Expect.equal (resultKey viaLookups.Result) (resultKey viaSink) $"merge, {tone}"

                  let gated =
                      { MergeContext.defaults<TestMsg> with
                          Policy = MergePolicy.gated brandSiblingValidator }

                  let viaLookupsGated =
                      DagMerge.mergeLookups
                          recordAuthor
                          getRec
                          getParents
                          "s"
                          initial
                          branchA.Hash
                          branchB.Hash
                          now
                          gated

                  let viaSinkGated =
                      DagMerge.mergeGated
                          recordAuthor
                          sink
                          "s"
                          initial
                          branchA.Hash
                          branchB.Hash
                          now
                          (MergePolicy.gated brandSiblingValidator)
                      |> Async.RunSynchronously

                  Expect.equal (resultKey viaLookupsGated.Result) (resultKey viaSinkGated.Result) $"mergeGated, {tone}"
          }

          test "mergeGatedWith's policy argument wins over the context's Policy" {
              let sink, initial, _, branchA, branchB = disjointFixture ToneVariant.Brand

              let context =
                  { MergeContext.defaults<TestMsg> with
                      Policy = MergePolicy.gated brandSiblingValidator }

              let outcome =
                  DagMerge.mergeGatedWith
                      recordAuthor
                      sink
                      "s"
                      initial
                      branchA.Hash
                      branchB.Hash
                      now
                      MergePolicy.lenient
                      context
                  |> Async.RunSynchronously

              match outcome.Result with
              | MergeResult.Merged _ -> ()
              | other -> failtestf "a lenient argument merges regardless of the context, got %A" other
          }

          test "the trunk merge honours the context's validator Policy" {
              let sink, initial, a, branchA, branchB = disjointFixture ToneVariant.Brand
              sink.TryAdvanceHead("s", None, branchA.Hash) |> Async.RunSynchronously |> ignore

              let context =
                  { MergeContext.defaults<TestMsg> with
                      Policy = MergePolicy.gated brandSiblingValidator }

              match
                  DagMerge.mergeIntoTrunkWith recordAuthor sink "s" initial branchB.Hash now 3 context
                  |> Async.RunSynchronously
              with
              | TrunkMergeOutcome.Conflicted cells ->
                  Expect.isTrue
                      (cells |> List.forall (fun c -> c.Class = MergeConflictClass.CombinedCycle))
                      "the introduced defect refuses the trunk merge"

                  Expect.equal (sink.Head "s" |> Async.RunSynchronously) (Some branchA.Hash) "the trunk did not move"
              | other -> failtestf "expected a gated refusal, got %A" other

              ignore a
          }

          test "a trunk retry fetches what the moved trunk added since the one load" {
              let inner = InMemoryDagSink.create<TestMsg> ()
              let initial = buildDashboard ()
              let a = stepRecord "s" None (toneOp rightChildId ToneVariant.Default) 1L
              add inner a
              inner.TryAdvanceHead("s", None, a.Hash) |> Async.RunSynchronously |> ignore
              let branch = stepRecord "s" (Some a) (toneOp leftChildId ToneVariant.Success) 2L
              add inner branch
              // A concurrent writer's trunk step, landed AFTER the merge loaded
              // the stream and before it read the trunk head.
              let concurrent =
                  stepRecord "s" (Some a) (toneOp rightChildId ToneVariant.Critical) 3L

              let mutable loads = 0

              let sink =
                  { new IDagOpStreamSink<TestMsg> with
                      member _.Add r = inner.Add r
                      member _.TryGet(s, h) = inner.TryGet(s, h)
                      member _.Head s = inner.Head s
                      member _.TryAdvanceHead(s, e, n) = inner.TryAdvanceHead(s, e, n)
                      member _.Parents(s, h) = inner.Parents(s, h)
                      member _.Reachable(s, h) = inner.Reachable(s, h)
                      member _.Lca(s, x, y) = inner.Lca(s, x, y)
                      member _.Heads s = inner.Heads s
                      member _.Tombstone(s, h) = inner.Tombstone(s, h)
                      member _.Streams() = inner.Streams()

                      member _.Records s =
                          async {
                              loads <- loads + 1
                              let! records = inner.Records s

                              if loads = 1 then
                                  do! inner.Add concurrent
                                  let! _ = inner.TryAdvanceHead("s", Some a.Hash, concurrent.Hash)
                                  ()

                              return records
                          } }

              match
                  DagMerge.mergeIntoTrunkWith recordAuthor sink "s" initial branch.Hash now 3 MergeContext.defaults
                  |> Async.RunSynchronously
              with
              | TrunkMergeOutcome.Advanced head ->
                  Expect.equal loads 1 "the stream was loaded once"
                  let tree = replaySpine inner "s" initial head

                  let expected = initial |> applyOk a.Op |> applyOk concurrent.Op |> applyOk branch.Op

                  Expect.equal (canonical tree) (canonical expected) "the merge carries the concurrent trunk step"
              | other -> failtestf "expected the trunk to advance, got %A" other
          } ]
