module Fuaran.UI.Tests.CoreWalkAdoption

// ============================================================================
//  Phase 2060 — the apply path's walks are Fuaran.Core's.
//
//  `Introspect`'s lookups are Fuaran.Core.Tree combinators over Core's keyed
//  walk, the field ops resolve and rewrite in one `Tree.updateNode` pass, and
//  the §21 limits are derived incrementally where an op only adds. These tests
//  pin the decisions that move made rather than re-proving the suites around
//  them (the ops suite, the apply vectors and the limit fixtures stand
//  unchanged):
//
//   - which node an id names on a tree that holds it twice — Core's order,
//     keyed positions before structural children, for every lookup and edit;
//   - that limits carried across ops refuse exactly what a whole-tree walk
//     would.
//
//  Phase 2623 adds what the keyed walk costs per node it visits: one field op
//  on the 24-deep tree allocates no more than it did before the walk was
//  Core's, and `NodeChildren`'s unlabelled read names the nodes its labelled
//  slots name, in the same order, at every reach.
// ============================================================================

open System
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops

let private markdown (nodeId: string) (text: string) : Node<unit> = Fuaran.markdown nodeId text

let private box (nodeId: string) (children: Node<unit> list) : Node<unit> =
    Fuaran.dashboard
        nodeId
        { Defaults.dashboard<unit> with
            Children = children }

/// A root holding the id "dup" twice: once as its `state.onLoading` alternative
/// (a keyed position) and once as its structural child.
let private repeated: Node<unit> =
    { box "root" [ markdown "dup" "structural" ] with
        State =
            Some
                { Defaults.stateBehaviour<unit> with
                    OnLoading = Some(markdown "dup" "keyed") } }

let private textOf (node: Node<unit>) : string option =
    match node.Kind with
    | NodeKind.Markdown spec ->
        match spec.Text with
        | TextSource.Literal text -> Some text
        | _ -> None
    | _ -> None

let private keyedCopy (root: Node<unit>) : Node<unit> =
    root.State |> Option.bind _.OnLoading |> Option.get

let private structuralCopy (root: Node<unit>) : Node<unit> =
    Introspect.getChildren root.Kind |> Option.get |> List.head

/// A Box chain `depth` levels deep; the deepest node is `<prefix>1`.
let rec private chain (prefix: string) (depth: int) : Node<unit> =
    if depth <= 1 then
        box (prefix + "1") []
    else
        box (sprintf "%s%d" prefix depth) [ chain prefix (depth - 1) ]

/// The `apply-rate` benchmark's deep tree: a chain of `depth` boxes, each holding
/// nine leaves and then the next level; the deepest node is the leaf `target`,
/// last in preorder, so a walk that finds it visits the whole tree.
let private deepTree (depth: int) : Node<unit> =
    let rec level (d: int) : Node<unit> =
        let siblings = [ for s in 1..9 -> markdown (sprintf "d%d-s%d" d s) "leaf" ]

        if d = depth then
            box (sprintf "d%d" d) (siblings @ [ markdown "target" "leaf" ])
        else
            box (sprintf "d%d" d) (siblings @ [ level (d + 1) ])

    level 1

/// Mean bytes allocated on this thread per call of `run`, after a warm-up.
let private allocatedPerCall (run: unit -> unit) : int64 =
    for _ in 1..50 do
        run ()

    let iterations = 200
    let before = GC.GetAllocatedBytesForCurrentThread()

    for _ in 1..iterations do
        run ()

    (GC.GetAllocatedBytesForCurrentThread() - before) / int64 iterations

/// One node holding a node in every envelope position: two ordered children,
/// both `state` alternatives and an envelope `fallback`.
let private envelopeHolder: Node<unit> =
    { box "holder" [ markdown "c0" "a"; markdown "c1" "b" ] with
        State =
            Some
                { Defaults.stateBehaviour<unit> with
                    OnLoading = Some(markdown "loading" "l")
                    OnEmpty = Some(markdown "empty" "e") }
        Fallback = Some(markdown "fb" "f") }

/// A node of every kind that holds a node, each with its positions filled, and
/// the labels `NodeChildren.slots` gave it before Phase 2623.
let private holders: (Node<unit> * string list) list =
    [ envelopeHolder,
      [ "Box.children[0]"
        "Box.children[1]"
        "state.onLoading"
        "state.onEmpty"
        "fallback" ]
      Fuaran.errorBoundary
          "eb"
          { Child = markdown "eb-child" "c"
            Fallback = markdown "eb-fallback" "f" },
      [ "ErrorBoundary.child"; "ErrorBoundary.fallback" ]
      { Fuaran.switch
            "sw"
            { Defaults.switch<unit> with
                Cases =
                    [ { Child = markdown "case0" "0"
                        Match = Some "a"
                        When = None }
                      { Child = markdown "case1" "1"
                        Match = Some "b"
                        When = None } ]
                Default = markdown "sw-default" "d" } with
          Fallback = Some(markdown "sw-fb" "f") },
      [ "fallback"
        "Switch.cases[0].child"
        "Switch.cases[1].child"
        "Switch.default" ]
      { Fuaran.fragmentRef "ref" "frag" with
          Kind =
              NodeKind.FragmentRef
                  { Defaults.fragmentRef with
                      Name = "frag"
                      Args =
                          Some(
                              Map.ofList
                                  [ "zeta", FragmentArg.SlotArg(markdown "arg-z" "z")
                                    "count", FragmentArg.Int 3
                                    "alpha", FragmentArg.SlotArg(markdown "arg-a" "a") ]
                          ) } },
      [ "FragmentRef.args[alpha]"; "FragmentRef.args[zeta]" ]
      Fuaran.fragmentDecl
          "decl"
          { Defaults.fragmentDecl<unit> with
              Name = "frag"
              Body = markdown "body" "b" },
      [ "FragmentDecl.body[0]" ]
      { markdown "leaf" "x" with
          Fallback = Some(markdown "leaf-fb" "f") },
      [ "fallback" ] ]

let private reaches: (string * NodeChildren.Reach) list =
    [ "structural", NodeChildren.Reach.structural
      "kindHeld", NodeChildren.Reach.kindHeld
      "fragmentScope", NodeChildren.Reach.fragmentScope
      "keyed", NodeChildren.Reach.keyed
      "nonStructural", NodeChildren.Reach.nonStructural ]

let private applyAll (ops: TreeOp<unit> list) (root: Node<unit>) =
    ops |> List.fold (fun acc op -> acc |> Result.bind (Apply.apply op)) (Ok root)

[<Tests>]
let coreWalkAdoptionTests =
    testList
        "Fuaran.UI.Ops walks through Fuaran.Core (Phase 2060)"
        [ testList
              "a repeated id names the KEYED copy first, as Core's keyed engine does"
              [ test "findNode answers the state alternative, not the structural child" {
                    let found = Introspect.findNode (NodeId "dup") repeated |> Option.bind textOf
                    Expect.equal found (Some "keyed") "Core's traversal visits keyed positions first"
                }

                test "a field op edits the copy findNode names" {
                    match
                        Apply.apply (TreeOp.UpdateTooltip(NodeId "dup", Some(TextSource.Literal "hint"))) repeated
                    with
                    | Error e -> failtestf "UpdateTooltip refused: %s" e.Message
                    | Ok updated ->
                        Expect.isSome (keyedCopy updated).Tooltip "the keyed copy is the one edited"
                        Expect.isNone (structuralCopy updated).Tooltip "the structural copy is untouched"
                }

                test "allNodeIds lists the keyed copy before the structural one" {
                    Expect.equal
                        (Introspect.allNodeIds repeated)
                        [ NodeId "root"; NodeId "dup"; NodeId "dup" ]
                        "every copy is enumerated, in Core's keyed-walk preorder"
                }

                test "a unique-id tree is unaffected by the order" {
                    let unique =
                        { box "root" [ markdown "child" "structural" ] with
                            State =
                                Some
                                    { Defaults.stateBehaviour<unit> with
                                        OnLoading = Some(markdown "alt" "keyed") } }

                    Expect.equal
                        (Introspect.findNode (NodeId "child") unique |> Option.bind textOf)
                        (Some "structural")
                        "each id names its one node"

                    Expect.equal
                        (Introspect.findNode (NodeId "alt") unique |> Option.bind textOf)
                        (Some "keyed")
                        "each id names its one node"
                } ]

          testList
              "the single-pass field ops"
              [ test "an UpdateProp refusal leaves the tree's answer a refusal, not a partial edit" {
                    let tree = box "root" [ markdown "m" "text" ]

                    match
                        Apply.apply
                            (TreeOp.UpdateProp(
                                NodeId "m",
                                "noSuchField",
                                PropValue.Native(box "x" |> Unchecked.nonNull)
                            ))
                            tree
                    with
                    | Error e -> Expect.equal e.Code ApplyErrorCode.FieldNotFound "the field refusal is the op's answer"
                    | Ok _ -> failtest "an unknown field was applied"
                }

                test "a field op on an absent id is NodeNotFound" {
                    match Apply.apply (TreeOp.UpdateTooltip(NodeId "ghost", None)) (box "root" []) with
                    | Error e -> Expect.equal e.Code ApplyErrorCode.NodeNotFound "absent target"
                    | Ok _ -> failtest "an absent target was edited"
                }

                test "isAncestorOf follows the path to the descendant" {
                    let tree = box "a" [ box "b" [ box "c" [] ]; box "d" [] ]
                    Expect.isTrue (Introspect.isAncestorOf (NodeId "a") (NodeId "c") tree) "a is above c"
                    Expect.isTrue (Introspect.isAncestorOf (NodeId "b") (NodeId "c") tree) "b is above c"
                    Expect.isTrue (Introspect.isAncestorOf (NodeId "c") (NodeId "c") tree) "a node is its own"
                    Expect.isFalse (Introspect.isAncestorOf (NodeId "d") (NodeId "c") tree) "d is beside c"
                    Expect.isFalse (Introspect.isAncestorOf (NodeId "a") (NodeId "ghost") tree) "absent"
                } ]

          testList
              "limits derived incrementally refuse what the whole-tree walk refuses"
              [ test "an insert past MaxDepth after carried field edits is still refused" {
                    // Built progressively to MaxDepth exactly, so every result's
                    // metrics are remembered; the field edits carry them across, and
                    // the insert under the deepest node is judged from them.
                    let depth = WireLimits.MaxDepth
                    let tree = chain "n" depth

                    let ops =
                        Streaming.lowerTree tree
                        @ [ TreeOp.UpdateTooltip(NodeId "n1", Some(TextSource.Literal "deepest"))
                            TreeOp.UpdateTooltip(NodeId(sprintf "n%d" depth), None) ]

                    match applyAll ops (Streaming.skeletonRoot tree) with
                    | Error e -> failtestf "the field edits were refused: %s" e.Message
                    | Ok edited ->
                        match Apply.apply (TreeOp.InsertChild(NodeId "n1", box "over" [])) edited with
                        | Error e -> Expect.equal e.Code ApplyErrorCode.LimitExceeded "one level past MaxDepth"
                        | Ok _ -> failtest "an insert past MaxDepth was applied"
                }

                test "a progressive build to exactly MaxDepth is accepted and one more level is refused" {
                    let tree = chain "p" WireLimits.MaxDepth
                    let ops = Streaming.lowerTree tree

                    match Streaming.applyFold ops (Streaming.skeletonRoot tree) with
                    | Error e -> failtestf "a build to exactly MaxDepth was refused: %s" e.Message
                    | Ok built ->
                        Expect.equal
                            (Introspect.allNodeIds built)
                            (Introspect.allNodeIds tree)
                            "the build reproduces the tree"

                        match Apply.apply (TreeOp.InsertChild(NodeId "p1", box "over" [])) built with
                        | Error e -> Expect.equal e.Code ApplyErrorCode.LimitExceeded "one level past MaxDepth"
                        | Ok _ -> failtest "an insert past MaxDepth was applied"
                }

                test "an insert onto a tree already past MaxDepth is refused, as the walk refused it" {
                    // A tree built outside apply can already be over the limit; an
                    // insert anywhere in it produces a result still over, and the
                    // result is what the limit judges.
                    let overDeep =
                        box "root" [ chain "deep" (WireLimits.MaxDepth + 1); box "shallow" [] ]

                    // A refused ReplaceRoot measures `overDeep` and remembers what it
                    // measured, so the insert below is judged incrementally.
                    Expect.isError
                        (Apply.apply (TreeOp.ReplaceRoot overDeep) (box "seed" []))
                        "a ReplaceRoot past MaxDepth is refused"

                    match Apply.apply (TreeOp.InsertChild(NodeId "shallow", box "leaf" [])) overDeep with
                    | Error e -> Expect.equal e.Code ApplyErrorCode.LimitExceeded "the result is past MaxDepth"
                    | Ok _ -> failtest "an insert into an over-deep tree was applied"
                } ]

          testList
              "what the keyed walk costs per node it visits (Phase 2623)"
              [ test "one field op on the 24-deep tree allocates what Core's rewrite costs, not a slot record per visit" {
                    let tree = deepTree WireLimits.MaxDepth
                    let op = TreeOp.UpdateTooltip(NodeId "target", Some(TextSource.Literal "hint"))

                    Expect.equal
                        (List.length (Introspect.allNodeIds tree))
                        241
                        "the benchmark's tree: 24 levels of ten, and the target"

                    Expect.isOk (Apply.apply op tree) "the op applies"

                    let perOp = allocatedPerCall (fun () -> Apply.apply op tree |> ignore)

                    // Phase 2060 measured 221 KB per op once the walks were Core's
                    // (44 KB before). Phase 2623 measured 62 KB: the keyed read and
                    // the unchanged keyed write-back now allocate nothing per
                    // node, and what remains is Core's `Tree.updateNode` itself,
                    // which allocates 61 KB on this tree over the STRUCTURAL
                    // witness alone. The bound sits just above that floor, so a
                    // per-visit cost coming back fails here.
                    Expect.isLessThanOrEqual perOp (64L * 1024L) (sprintf "bytes per op: %d" perOp)
                }

                test "the unlabelled read allocates nothing at a node holding only an ordered list" {
                    let node = box "plain" [ markdown "a" "a"; markdown "b" "b" ]

                    let perRead =
                        allocatedPerCall (fun () ->
                            NodeChildren.children NodeChildren.Reach.keyed node |> ignore
                            NodeChildren.children NodeChildren.Reach.nonStructural node |> ignore)

                    Expect.equal perRead 0L "no slot record, label or closure per visit"
                }

                test "the labelled slots keep their labels and their order" {
                    for node, labels in holders do
                        Expect.equal
                            (NodeChildren.slots node |> List.map _.Label)
                            labels
                            (sprintf "the labels of %s" node.Id)
                }

                test "the unlabelled read names the labelled slots' nodes, in order, at every reach" {
                    for node, _ in holders do
                        for name, reach in reaches do
                            let read = NodeChildren.children reach node
                            let labelled = NodeChildren.slotsIn reach node |> List.map _.Node

                            Expect.equal
                                (read |> List.map _.Id)
                                (labelled |> List.map _.Id)
                                (sprintf "%s at %s" node.Id name)

                            Expect.isTrue
                                (List.forall2 (fun a b -> LanguagePrimitives.PhysicalEquality a b) read labelled)
                                (sprintf "%s at %s: the same node objects" node.Id name)
                }

                test "putting the read back answers the node, and a changed list is written" {
                    for node, _ in holders do
                        for name, reach in reaches do
                            let read = NodeChildren.children reach node

                            Expect.isTrue
                                (LanguagePrimitives.PhysicalEquality (NodeChildren.replace reach node read) node)
                                (sprintf "%s at %s: unchanged positions answer the node" node.Id name)

                            let renamed = read |> List.map (fun n -> { n with Id = n.Id + "-renamed" })
                            let written = NodeChildren.replace reach node renamed

                            Expect.equal
                                (NodeChildren.children reach written |> List.map _.Id)
                                (renamed |> List.map _.Id)
                                (sprintf "%s at %s: a changed list is written back" node.Id name)

                            Expect.equal
                                (NodeChildren.slots written |> List.length)
                                (NodeChildren.slots node |> List.length)
                                (sprintf "%s at %s: arity preserved" node.Id name)
                } ] ]
