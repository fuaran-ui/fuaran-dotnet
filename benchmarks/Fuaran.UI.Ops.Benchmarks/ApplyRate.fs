module Fuaran.UI.Ops.Benchmarks.ApplyRate

open System
open System.Diagnostics
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types

// ============================================================================
//  ApplyRate (Phase 2060) — what `Apply.apply` costs on the two paths the
//  phase reshaped: a tree built progressively, one `InsertChild` per node, and
//  a single field op addressed deep in a tree.
//
//   - `progressive.<n>` — `Streaming.applyFold (Streaming.lowerTree t)
//     (Streaming.skeletonRoot t)` over a balanced tree of n nodes (stacks of
//     ten, so ~4 levels for 10,000 nodes), timed whole. The per-node figure is
//     the one to read across sizes: flat in n is linear build, rising with n is
//     a walk of the whole tree per insert.
//   - `field.<op>` — one op against the deepest leaf of a 24-level chain whose
//     every level also carries nine leaf siblings (~240 nodes), so the target
//     sits last in preorder and every walk that finds it pays for the tree.
//
//  Stopwatch-based like `TreeDiffRate`, warm-up first, so a phase outcome's
//  before/after figures come from one command:
//  `dotnet run -c Release -- apply-rate [iterations]`.
// ============================================================================

let private leaf (id: string) : Node<unit> =
    Fuaran.heading
        id
        { Defaults.heading with
            Text = TextSource.Literal id }

let private stackOf (id: string) (children: Node<unit> list) : Node<unit> =
    Fuaran.stack
        id
        { Defaults.stack with
            Children = children }

/// A balanced tree of exactly `n` nodes: a stack per internal node, ten
/// children each, filled breadth-first; the remainder are leaves.
let balanced (n: int) : Node<unit> =
    let fanout = 10
    // Node i's children are fanout*i + 1 .. fanout*i + fanout (heap order).
    let rec build (i: int) : Node<unit> =
        let first = fanout * i + 1

        if first >= n then
            leaf $"n{i}"
        else
            [ first .. min (n - 1) (first + fanout - 1) ]
            |> List.map build
            |> stackOf $"n{i}"

    build 0

/// The deep tree the field ops address: a chain of `depth` stacks, each holding
/// nine leaves and then the next level; the deepest node is the leaf `target`.
let private deepTree (depth: int) : Node<unit> =
    let rec level (d: int) : Node<unit> =
        let siblings = [ for s in 1..9 -> leaf $"d{d}-s{s}" ]

        if d = depth then
            stackOf $"d{d}" (siblings @ [ leaf "target" ])
        else
            stackOf $"d{d}" (siblings @ [ level (d + 1) ])

    level 1

let private count (n: Node<unit>) : int =
    Fuaran.Core.Tree.count Introspect.nodeWitness n

/// Mean µs and bytes per `run`, after `warmUps` untimed runs.
let private time (warmUps: int) (iterations: int) (run: unit -> unit) : float * float =
    for _ in 1..warmUps do
        run ()

    GC.Collect()
    let allocBefore = GC.GetAllocatedBytesForCurrentThread()
    let sw = Stopwatch.StartNew()

    for _ in 1..iterations do
        run ()

    sw.Stop()
    let allocAfter = GC.GetAllocatedBytesForCurrentThread()
    sw.Elapsed.TotalMilliseconds * 1000.0 / float iterations, float (allocAfter - allocBefore) / float iterations

/// One progressive build of `n` nodes: (nodes, mean µs per build, µs per node,
/// bytes per build). Fails loudly when the build does not reproduce the tree,
/// so a figure can never be the cost of an early refusal.
let progressive (n: int) (iterations: int) : int * float * float * float =
    let tree = balanced n
    let ops = Streaming.lowerTree tree
    let skeleton = Streaming.skeletonRoot tree

    match Streaming.applyFold ops skeleton with
    | Ok rebuilt when count rebuilt = count tree -> ()
    | Ok _ -> failwith "progressive build did not reproduce the tree"
    | Error e -> failwith $"progressive build refused: {e.Message}"

    let meanUs, allocB =
        // The verifying build above is the warm-up.
        time 0 iterations (fun () -> Streaming.applyFold ops skeleton |> ignore)

    count tree, meanUs, meanUs / float (count tree), allocB

/// The single field ops against the deep tree's last preorder leaf.
let fieldOps () : (string * TreeOp<unit>) list =
    let target = NodeId "target"

    [ "update_prop", TreeOp.UpdateProp(target, "text", PropValue.Native(box "edited" |> Unchecked.nonNull))
      "update_style",
      TreeOp.UpdateStyle(
          target,
          { Defaults.style with
              Tone = ToneVariant.Warning }
      )
      "edit_node", TreeOp.EditNode(target, (leaf "target-edit").Kind)
      "update_tooltip", TreeOp.UpdateTooltip(target, None) ]

/// (nodes, depth) of the deep tree, and per op (name, mean µs, bytes).
let field (iterations: int) : int * (string * float * float) list =
    let tree = deepTree Fuaran.UI.WireLimits.MaxDepth

    let rows =
        fieldOps ()
        |> List.map (fun (name, op) ->
            match Apply.apply op tree with
            | Ok _ -> ()
            | Error e -> failwith $"field op {name} refused: {e.Message}"

            let meanUs, allocB =
                time (max 1 (iterations / 10)) iterations (fun () -> Apply.apply op tree |> ignore)

            name, meanUs, allocB)

    count tree, rows
