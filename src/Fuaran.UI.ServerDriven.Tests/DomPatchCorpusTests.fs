module Fuaran.UI.ServerDriven.Tests.DomPatchCorpusTests

// ─── Phase 158 QW6: DomPatch conformance corpus ───────────────────────────────
//
// A named golden corpus locking the TreeOp → DomPatch lowering — the cheap gate
// against silent patch-lowering drift (the DomPatch analogue of the Phase 142
// SSR class+ARIA parity corpus). Each case is `(name, newTree, ops, expected)`;
// the corpus asserts `Lowering.lower stubRender newTree ops = expected`. The
// `DomPatchCorpus` Build target runs this project as a standalone CI gate.
//
// Every lowering branch is represented: the four structural ops lower directly;
// the five content ops (EditNode / UpdateProp / UpdateStyle / UpdateState /
// ReplaceBinding) all hit the same `reRender id` arm (3 representatives prove
// it); Batch flattens; an absent node lowers to nothing.

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.ServerDriven

let private stubRender (n: Node<obj>) : string =
    let (NodeId s) = n.Id
    $"<f id='{s}'/>"

let private dash (children: Node<obj> list) : Node<obj> =
    Fuaran.dashboard
        "dash"
        { Defaults.dashboard<obj> with
            Children = children }

let private tree = dash [ Fuaran.markdown "m" "hello"; Fuaran.markdown "n" "world" ]

type private Case =
    { Name: string
      Tree: Node<obj>
      Ops: TreeOp<obj> list
      Expected: DomPatch list }

let private corpus: Case list =
    [ { Name = "RemoveNode → DomPatch.RemoveNode"
        Tree = tree
        Ops = [ TreeOp.RemoveNode(NodeId "m") ]
        Expected = [ DomPatch.RemoveNode "m" ] }

      { Name = "ReorderChildren → DomPatch.ReorderChildren (ids → raw strings)"
        Tree = tree
        Ops = [ TreeOp.ReorderChildren(NodeId "dash", [ NodeId "n"; NodeId "m" ]) ]
        Expected = [ DomPatch.ReorderChildren("dash", [ "n"; "m" ]) ] }

      { Name = "MoveNode → identity-preserving DomPatch.MoveNode"
        Tree = tree
        Ops = [ TreeOp.MoveNode(NodeId "m", NodeId "other", 2) ]
        Expected = [ DomPatch.MoveNode("m", "other", 2) ] }

      { Name = "InsertChild → InsertFragment with the rendered child"
        Tree = tree
        Ops = [ TreeOp.InsertChild(NodeId "dash", 1, Fuaran.markdown "fresh" "new") ]
        Expected = [ DomPatch.InsertFragment("dash", 1, "<f id='fresh'/>") ] }

      { Name = "EditNode → ReplaceFragment of the re-rendered node"
        Tree = tree
        Ops = [ TreeOp.EditNode(NodeId "m", (Fuaran.markdown "m" "x").Kind) ]
        Expected = [ DomPatch.ReplaceFragment("m", "<f id='m'/>") ] }

      { Name = "UpdateProp → ReplaceFragment of the re-rendered node"
        Tree = tree
        Ops =
          [ TreeOp.UpdateProp(NodeId "m", "Text", PropValue.Native(Unchecked.nonNull (box (TextSource.Literal "x")))) ]
        Expected = [ DomPatch.ReplaceFragment("m", "<f id='m'/>") ] }

      { Name = "UpdateStyle → ReplaceFragment of the re-rendered node"
        Tree = tree
        Ops = [ TreeOp.UpdateStyle(NodeId "n", Defaults.style) ]
        Expected = [ DomPatch.ReplaceFragment("n", "<f id='n'/>") ] }

      { Name = "Batch flattens to its inner ops, in order"
        Tree = tree
        Ops = [ TreeOp.Batch [ TreeOp.RemoveNode(NodeId "m"); TreeOp.MoveNode(NodeId "n", NodeId "dash", 0) ] ]
        Expected = [ DomPatch.RemoveNode "m"; DomPatch.MoveNode("n", "dash", 0) ] }

      { Name = "a content op for an absent node lowers to nothing"
        Tree = tree
        Ops = [ TreeOp.UpdateStyle(NodeId "ghost", Defaults.style) ]
        Expected = [] } ]

[<Tests>]
let tests =
    testList
        "DomPatch conformance corpus (Phase 158 QW6)"
        [ for case in corpus ->
              test case.Name { Expect.equal (Lowering.lower stubRender case.Tree case.Ops) case.Expected case.Name } ]
