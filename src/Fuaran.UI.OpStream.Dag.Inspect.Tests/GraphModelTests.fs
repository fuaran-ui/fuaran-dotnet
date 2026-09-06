module Fuaran.UI.OpStream.Dag.Inspect.Tests.GraphModelTests

open Expecto
open Fuaran.UI.OpStream.Dag.Inspect
open Fuaran.UI.OpStream.Dag.Inspect.Tests.InspectCorpus

// ============================================================================
//  DagGraphModel — branch/merge render-model classification (Phase 186 task 1).
// ============================================================================

let private graphOf (c: Corpus) : DagGraph =
    let records = c.Sink.Records "s" |> Async.RunSynchronously
    DagGraphModel.build "s" records

let private node (g: DagGraph) (hash: string) : DagGraphNode =
    match DagGraphModel.tryNode g hash with
    | Some n -> n
    | None -> failtestf "node %s absent from graph" hash

[<Tests>]
let tests =
    testList
        "DagGraphModel"
        [ test "genesis node is the sole root, classified Genesis at depth 0" {
              let c = build ()
              let g = graphOf c
              Expect.equal g.Roots [ c.A.Hash ] "single genesis root"
              let a = node g c.A.Hash
              Expect.equal a.Role DagNodeRole.Genesis "role Genesis"
              Expect.equal a.Depth 0 "depth 0"
          }

          test "the genesis is a branch point with three children" {
              let c = build ()
              let g = graphOf c
              let a = node g c.A.Hash
              Expect.equal a.ChildCount 3 "brA + brB + brC"
              Expect.isTrue a.IsBranchPoint "≥2 children ⇒ branch point"
              Expect.isFalse a.IsLeaf "has children ⇒ not a leaf"
          }

          test "single-parent branches are Linear at depth 1" {
              let c = build ()
              let g = graphOf c

              for h in [ c.BranchA.Hash; c.BranchB.Hash; c.BranchC.Hash ] do
                  let n = node g h
                  Expect.equal n.Role DagNodeRole.Linear "one parent ⇒ Linear"
                  Expect.equal n.Depth 1 "one step off genesis"
          }

          test "the merge node has two parents, role Merge, deepest layer" {
              let c = build ()
              let g = graphOf c
              let m = node g c.Merge.Hash
              Expect.equal m.Role DagNodeRole.Merge "two parents ⇒ Merge"
              Expect.equal (List.length m.Parents) 2 "two parents"
              Expect.equal m.Depth 2 "below both depth-1 parents"
              Expect.isTrue m.IsLeaf "the live head is a leaf"
          }

          test "leaves are exactly the childless tips (merge + dangling branchC)" {
              let c = build ()
              let g = graphOf c
              Expect.equal (Set.ofList g.Leaves) (Set.ofList [ c.Merge.Hash; c.BranchC.Hash ]) "merge + branchC"
          }

          test "merge edges mark exactly one primary (the replay spine) parent" {
              let c = build ()
              let g = graphOf c
              let mergeEdges = g.Edges |> List.filter (fun e -> e.Child = c.Merge.Hash)
              Expect.equal (List.length mergeEdges) 2 "two parent edges"
              let primary = mergeEdges |> List.filter _.IsPrimary
              Expect.equal (List.length primary) 1 "exactly one primary edge"
              Expect.equal primary.Head.Parent c.BranchA.Hash "primary parent is branchA (author order)"
          }

          test "nodes are ordered by depth then hash (deterministic render order)" {
              let c = build ()
              let g = graphOf c
              let depths = g.Nodes |> List.map _.Depth
              Expect.equal depths (List.sort depths) "depth-monotone node order"
          }

          test "an empty stream builds an empty graph" {
              let g = DagGraphModel.build<TestMsg> "empty" []
              Expect.isEmpty g.Nodes "no nodes"
              Expect.isEmpty g.Edges "no edges"
              Expect.isEmpty g.Roots "no roots"
              Expect.isEmpty g.Leaves "no leaves"
          }

          // ── Cyclic input (Phase 1525) ────────────────────────────────────
          //
          // A well-formed DAG cannot contain a cycle — a content address folds in
          // its parents, so a node is never its own ancestor. That is a property
          // of records the tier MINTED, and `build` is handed a LIST: replicated,
          // hand-assembled or out-of-band-edited input can carry a cycle whose
          // members each look perfectly ordinary on their own.
          //
          // The old depth pass recursed parent-wards with a memo written only on
          // the way out, so a cycle recursed until the stack ran out — a
          // `StackOverflowException`, which .NET does not let anyone catch, from a
          // pure projection function. Nothing above it could report, retry, or
          // even log; the process simply ended.

          test "a cyclic record set is REPORTED, not looped on" {
              let c = build ()
              let records = c.Sink.Records "s" |> Async.RunSynchronously

              // Two nodes naming each other as parent, keeping their stored
              // hashes — what a corrupt or forged pair actually looks like.
              let x = records |> List.find (fun r -> r.Hash = c.BranchA.Hash)
              let y = records |> List.find (fun r -> r.Hash = c.BranchB.Hash)

              let cyclic = [ { x with Parents = [ y.Hash ] }; { y with Parents = [ x.Hash ] } ]

              match DagGraphModel.tryBuild "s" cyclic with
              | Ok graph -> failtestf "expected a cycle report, built a graph with %d nodes" graph.Nodes.Length
              | Error cycle ->
                  Expect.equal cycle.StreamId "s" "names the stream"

                  Expect.equal
                      (Set.ofList cycle.Unresolved)
                      (Set.ofList [ x.Hash; y.Hash ])
                      "names exactly the nodes it could not place"

                  let described = DagGraphCycle.describe cycle
                  Expect.stringContains described "CYCLIC" "the rendering says what is wrong"
                  Expect.stringContains described x.Hash "…and names the nodes"
          }

          test "`build` REFUSES a cyclic set by name rather than returning a wrong depth" {
              let c = build ()
              let records = c.Sink.Records "s" |> Async.RunSynchronously
              let x = records |> List.find (fun r -> r.Hash = c.BranchA.Hash)
              let y = records |> List.find (fun r -> r.Hash = c.BranchB.Hash)

              let cyclic = [ { x with Parents = [ y.Hash ] }; { y with Parents = [ x.Hash ] } ]

              let message =
                  try
                      DagGraphModel.build "s" cyclic |> ignore
                      failtest "GO-RED FAILED: build accepted a cyclic record set"
                  with :? System.InvalidOperationException as ex ->
                      ex.Message

              Expect.stringContains message "CYCLIC" "refuses by name"
          }

          test "a node BEHIND a cycle is reported too, and an unrelated node still resolves" {
              // The sweep reports every node it could not settle — the cycle plus
              // everything downstream of it. It deliberately does not claim to
              // have isolated the cycle itself: "these are the nodes I could not
              // place" is what a Kahn sweep actually knows, and naming a single
              // culprit from it would be a guess dressed as a finding.
              let c = build ()
              let records = c.Sink.Records "s" |> Async.RunSynchronously
              let x = records |> List.find (fun r -> r.Hash = c.BranchA.Hash)
              let y = records |> List.find (fun r -> r.Hash = c.BranchB.Hash)
              let genesis = records |> List.find (fun r -> r.Hash = c.A.Hash)
              let downstream = records |> List.find (fun r -> r.Hash = c.Merge.Hash)

              let cyclic =
                  [ genesis
                    { x with Parents = [ y.Hash ] }
                    { y with Parents = [ x.Hash ] }
                    downstream ] // parents [branchA; branchB] — both in the cycle

              match DagGraphModel.tryBuild "s" cyclic with
              | Ok _ -> failtest "expected a cycle report"
              | Error cycle ->
                  Expect.equal
                      (Set.ofList cycle.Unresolved)
                      (Set.ofList [ x.Hash; y.Hash; downstream.Hash ])
                      "the cycle, and the node behind it"

                  Expect.isFalse
                      (List.contains genesis.Hash cycle.Unresolved)
                      "the genesis, which is not behind the cycle, resolved normally"
          }

          test "the Kahn pass reproduces the recursive pass's depths on a deep chain" {
              // The depths are the contract; the algorithm is not. A 5,000-node
              // chain is also where the old recursion sat one input away from the
              // stack limit for a reason having nothing to do with cycles.
              let c = build ()
              let records = c.Sink.Records "s" |> Async.RunSynchronously
              let seed = records |> List.find (fun r -> r.Hash = c.A.Hash)

              let chain =
                  [ for i in 0..4999 ->
                        { seed with
                            Hash = sprintf "n%04d" i
                            Parents = (if i = 0 then [] else [ sprintf "n%04d" (i - 1) ]) } ]

              let g = DagGraphModel.build "deep" chain
              Expect.equal g.Nodes.Length 5000 "every node placed"

              match DagGraphModel.tryNode g "n4999" with
              | Some n -> Expect.equal n.Depth 4999 "the tail sits at depth 4999"
              | None -> failtest "tail node absent"
          } ]
