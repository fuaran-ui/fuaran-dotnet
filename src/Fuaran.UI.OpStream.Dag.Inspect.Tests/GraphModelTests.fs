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
          } ]
