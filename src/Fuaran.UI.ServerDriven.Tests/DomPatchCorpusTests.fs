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
// a content op the prop map does not cover hits the `reRender id` arm (3
// representatives prove it); Batch flattens; an absent node lowers to nothing.
// The granular arms (Phase 2526) are the second block: a mapped prop lowers to
// `SetText` / `SetAttr` / `RemoveAttr` and emits no `ReplaceFragment`.

open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.ServerDriven

let private stubRender (n: Node<obj>) : string =
    let s = n.Id
    $"<f id='{s}'/>"

let private dash (children: Node<obj> list) : Node<obj> =
    Fuaran.dashboard
        "dash"
        { Defaults.dashboard<obj> with
            Children = children }

let private other (children: Node<obj> list) : Node<obj> =
    Fuaran.dashboard
        "other"
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

      // `Tree` is the POST-apply tree — the DOM index is derived from where the
      // node landed, since the op no longer carries one.
      { Name = "MoveNode → identity-preserving DomPatch.MoveNode"
        Tree = dash [ Fuaran.markdown "n" "world"; other [ Fuaran.markdown "m" "hello" ] ]
        Ops = [ TreeOp.MoveNode(NodeId "m", NodeId "other") ]
        Expected = [ DomPatch.MoveNode("m", "other", 0) ] }

      { Name = "InsertChild → InsertFragment with the rendered child"
        Tree =
          dash
              [ Fuaran.markdown "m" "hello"
                Fuaran.markdown "n" "world"
                Fuaran.markdown "fresh" "new" ]
        Ops = [ TreeOp.InsertChild(NodeId "dash", Fuaran.markdown "fresh" "new") ]
        Expected = [ DomPatch.InsertFragment("dash", 2, "<f id='fresh'/>") ] }

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
        Ops = [ TreeOp.Batch [ TreeOp.RemoveNode(NodeId "m"); TreeOp.MoveNode(NodeId "n", NodeId "dash") ] ]
        Expected = [ DomPatch.RemoveNode "m"; DomPatch.MoveNode("n", "dash", 1) ] }

      { Name = "a content op for an absent node lowers to nothing"
        Tree = tree
        Ops = [ TreeOp.UpdateStyle(NodeId "ghost", Defaults.style) ]
        Expected = [] } ]

// ─── Phase 2526: the granular arms ──────────────────────────────────────────

let private literal (s: string) : PropValue = PropValue.Wire(JStr s)

let private heading (text: string) : Node<obj> =
    Fuaran.heading
        "h"
        { Defaults.heading with
            Text = TextSource.Literal text }

let private leadHeading: Node<obj> =
    Fuaran.heading
        "h"
        { Defaults.heading with
            Variant = HeadingVariant.Lead }

let private badge: Node<obj> =
    Fuaran.badge
        "b"
        { Defaults.badge with
            Label = TextSource.Literal "new" }

let private button: Node<obj> =
    Fuaran.button
        "go"
        { Defaults.button<obj> with
            Label = TextSource.Literal "new" }

let private namedLink: Node<obj> =
    Fuaran.linkSpec
        "l"
        { Defaults.link with
            Label = TextSource.Literal "new"
            Rel = Some "external" }

let private blankLink: Node<obj> =
    Fuaran.linkSpec
        "l"
        { Defaults.link with
            Target = Some "_blank" }

let private labelledIcon: Node<obj> =
    Fuaran.iconSpec
        "i"
        { Defaults.icon with
            Icon = "moon"
            Label = Some "A moon" }

let private granularCorpus: Case list =
    [ { Name = "UpdateProp on a Heading's text → SetText"
        Tree = heading "new"
        Ops = [ TreeOp.UpdateProp(NodeId "h", "Text", literal "new") ]
        Expected = [ DomPatch.SetText("h", "new") ] }

      { Name = "UpdateProp on a Badge's label → SetText"
        Tree = badge
        Ops = [ TreeOp.UpdateProp(NodeId "b", "Label", literal "new") ]
        Expected = [ DomPatch.SetText("b", "new") ] }

      { Name = "UpdateProp on a Button's label → SetText"
        Tree = button
        Ops = [ TreeOp.UpdateProp(NodeId "go", "Label", literal "new") ]
        Expected = [ DomPatch.SetText("go", "new") ] }

      { Name = "UpdateProp on a Link's label → SetText"
        Tree = namedLink
        Ops = [ TreeOp.UpdateProp(NodeId "l", "Label", literal "new") ]
        Expected = [ DomPatch.SetText("l", "new") ] }

      { Name = "UpdateProp on a Link's rel → SetAttr"
        Tree = namedLink
        Ops = [ TreeOp.UpdateProp(NodeId "l", "Rel", literal "external") ]
        Expected = [ DomPatch.SetAttr("l", "rel", "external") ] }

      { Name = "UpdateProp clearing a Link's rel → RemoveAttr"
        Tree = Fuaran.linkSpec "l" Defaults.link
        Ops = [ TreeOp.UpdateProp(NodeId "l", "Rel", PropValue.Native(Unchecked.defaultof<obj>)) ]
        Expected = [ DomPatch.RemoveAttr("l", "rel") ] }

      { Name = "UpdateProp on a Link's target moves the rel it decides"
        Tree = blankLink
        Ops = [ TreeOp.UpdateProp(NodeId "l", "Target", literal "_blank") ]
        Expected =
          [ DomPatch.SetAttr("l", "target", "_blank")
            DomPatch.SetAttr("l", "rel", "noopener noreferrer") ] }

      { Name = "UpdateProp on an Icon's label sets the name and clears aria-hidden"
        Tree = labelledIcon
        Ops = [ TreeOp.UpdateProp(NodeId "i", "Label", literal "A moon") ]
        Expected =
          [ DomPatch.SetAttr("i", "role", "img")
            DomPatch.SetAttr("i", "aria-label", "A moon")
            DomPatch.RemoveAttr("i", "aria-hidden") ] }

      { Name = "UpdateProp on a prop the map does not cover keeps the floor"
        Tree = heading "new"
        Ops = [ TreeOp.UpdateProp(NodeId "h", "Level", PropValue.Wire(JInt 3)) ]
        Expected = [ DomPatch.ReplaceFragment("h", "<f id='h'/>") ] } ]

/// The arms that read the node as it stood: a script, from its old tree.
type private ScriptCase =
    { Name: string
      Before: Node<obj>
      Ops: TreeOp<obj> list
      Expected: DomPatch list }

let private scriptCorpus: ScriptCase list =
    [ { Name = "a text-only EditNode → SetText"
        Before = heading "old"
        Ops = [ TreeOp.EditNode(NodeId "h", (heading "new").Kind) ]
        Expected = [ DomPatch.SetText("h", "new") ] }

      { Name = "an EditNode that moves more than the text keeps the floor"
        Before = heading "old"
        Ops = [ TreeOp.EditNode(NodeId "h", leadHeading.Kind) ]
        Expected = [ DomPatch.ReplaceFragment("h", "<f id='h'/>") ] }

      { Name = "the UpdateState the diff pairs with an EditNode costs a stateless node nothing"
        Before = heading "old"
        Ops =
          [ TreeOp.EditNode(NodeId "h", (heading "new").Kind)
            TreeOp.UpdateState(NodeId "h", Defaults.stateBehaviour<obj>) ]
        Expected = [ DomPatch.SetText("h", "new") ] }

      { Name = "two adjacent re-renders of one node ship the last"
        Before = heading "old"
        Ops =
          [ TreeOp.UpdateProp(NodeId "h", "Level", PropValue.Wire(JInt 3))
            TreeOp.UpdateStyle(NodeId "h", Defaults.style) ]
        Expected = [ DomPatch.ReplaceFragment("h", "<f id='h'/>") ] } ]

[<Tests>]
let tests =
    testList
        "DomPatch conformance corpus (Phase 158 QW6)"
        [ for case in corpus @ granularCorpus ->
              test case.Name { Expect.equal (Lowering.lower stubRender case.Tree case.Ops) case.Expected case.Name }
          for case in scriptCorpus ->
              test case.Name {
                  Expect.equal (Lowering.lowerScript stubRender case.Before case.Ops) case.Expected case.Name
              } ]
