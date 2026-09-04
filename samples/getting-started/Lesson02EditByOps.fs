module Samples.GettingStarted.Lesson02EditByOps

// ============================================================================
//  LESSON 2 — Edit the tree, don't regenerate it.
//
//  The obvious way to change an AI-authored interface is to ask the model for a
//  new one. It is also the wrong way, for three reasons that have nothing to do
//  with cost: the model may change parts you did not ask about, you cannot say
//  what changed, and you cannot undo it.
//
//  A `TreeOp` is the alternative — a typed, addressed edit. "Set the label of
//  `sales-revenue` to Net revenue" is one op against one node. It applies
//  deterministically, it fails BY NAME when it does not fit, and it is small
//  enough to log, review, reverse and replay (Lesson 3).
//
//  The model's job shrinks accordingly: emit the OP, not the page. That is a far
//  smaller thing to get right, and a far smaller thing to check.
// ============================================================================

open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types

module Canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson

/// Rename one metric's label. Nothing else in the tree is named, so nothing
/// else can move.
let renameRevenue: TreeOp<obj> =
    TreeOp.UpdateProp(NodeId "sales-revenue", "Label", PropValue.Wire(Fuaran.Core.JStr "Net revenue"))

/// Re-tone the same metric. Two ops, applied in order, are a two-step edit whose
/// intermediate state is a real tree you can inspect.
let warnOnRevenue: TreeOp<obj> =
    TreeOp.UpdateProp(NodeId "sales-revenue", "Tone", PropValue.Wire(Fuaran.Core.JStr "Warning"))

/// An op addressing a node that is not there. It is REFUSED, and the refusal
/// names the node — it does not silently no-op, and it does not throw.
let addressesNothing: TreeOp<obj> =
    TreeOp.UpdateProp(NodeId "no-such-node", "Label", PropValue.Wire(Fuaran.Core.JStr "…"))

let private applyAll (ops: TreeOp<obj> list) (tree: Node<obj>) : Result<Node<obj>, Fuaran.UI.Ops.Types.ApplyError> =
    ops
    |> List.fold (fun acc op -> acc |> Result.bind (Fuaran.UI.Ops.Apply.apply op)) (Ok tree)

let run () =
    let before = Lesson01Authoring.dashboard

    match applyAll [ renameRevenue; warnOnRevenue ] before with
    | Error e -> printfn "unexpected apply failure: %A" e
    | Ok after ->
        // The diff is the point. Everything except the two addressed fields is
        // the same object it was — not "re-derived to the same value", the same
        // object, because an op rebuilds only the spine down to its target.
        printfn "Two typed ops applied. What changed:"
        printfn ""

        let changedLines =
            let b = Canon.encodeNode before
            let a = Canon.encodeNode after
            // A crude field-level diff is enough to make the point visible.
            let split (s: string) =
                s.Split([| "},{" |], System.StringSplitOptions.None)

            Array.zip (split b) (split a) |> Array.filter (fun (x, y) -> x <> y)

        for (b, a) in changedLines do
            printfn "  before: %s" b
            printfn "  after:  %s" a

        printfn ""
        printfn "Every other node in the tree is byte-identical."

    // A refusal is a value, not an exception. An orchestrator reads the error,
    // tells the model what was wrong, and asks again — a loop that converges,
    // rather than a crash that needs a human.
    match Fuaran.UI.Ops.Apply.apply addressesNothing before with
    | Ok _ -> printfn "the bad op unexpectedly succeeded"
    | Error e ->
        printfn ""
        printfn "An op that addresses nothing is refused by name:"
        printfn "  %A" e
