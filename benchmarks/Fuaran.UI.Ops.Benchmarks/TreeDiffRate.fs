module Fuaran.UI.Ops.Benchmarks.TreeDiffRate

open System
open System.Diagnostics
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.OpStream.Replay

// ============================================================================
//  TreeDiffRate (Phase 2063) — the cost of `TreeOpDiff.diff` over a ~2,000-node
//  tree, per edit shape.
//
//  The tree is a dashboard root over 40 vertical stacks of 49 leaves each
//  (headings, markdown and badges in rotation): 1 + 40 + 1,960 = 2,001 nodes.
//  Each scenario diffs that tree against a copy carrying exactly one edit:
//
//   - `identical`     — no edit (the floor: every node is visited and compared).
//   - `one_field`     — one heading's text changes, deep in the middle stack.
//   - `one_container` — one stack's heading changes (a container own-field: the
//                       candidate check used to encode the stack's whole subtree).
//   - `one_move`      — one leaf moves to another stack (the cross-parent move
//                       pre-pass and its ancestor check).
//
//  Stopwatch-based like `AppendRate`, warm-up first, so the before/after figures
//  in a phase outcome are reproducible with one command:
//  `dotnet run -c Release -- tree-diff [iterations]`.
// ============================================================================

[<Literal>]
let private Stacks = 40

[<Literal>]
let private LeavesPerStack = 49

let private leaf (s: int) (i: int) : Node<unit> =
    let id = $"leaf-{s}-{i}"

    match i % 3 with
    | 0 ->
        Fuaran.heading
            id
            { Defaults.heading with
                Text = TextSource.Literal $"Heading {s}.{i}" }
    | 1 -> Fuaran.markdown id $"Body text for {s}.{i}"
    | _ ->
        Fuaran.badge
            id
            { Defaults.badge with
                Label = TextSource.Literal $"Badge {s}.{i}" }

let private stackOf (s: int) (leaves: Node<unit> list) : Node<unit> =
    Fuaran.stack
        $"stack-{s}"
        { Defaults.stack with
            Children = leaves }

/// Build the 2,001-node tree, with `edit` applied to each leaf before it is
/// placed and `place` deciding which stack each leaf lands in.
let private build
    (editLeaf: Node<unit> -> Node<unit>)
    (place: string -> int -> int)
    (editStack: Node<unit> -> Node<unit>)
    =
    let leaves =
        [ for s in 0 .. Stacks - 1 do
              for i in 0 .. LeavesPerStack - 1 do
                  let n = editLeaf (leaf s i)
                  yield place n.Id s, n ]

    let stacks =
        [ for s in 0 .. Stacks - 1 ->
              leaves
              |> List.filter (fun (home, _) -> home = s)
              |> List.map snd
              |> stackOf s
              |> editStack ]

    Fuaran.dashboard
        "root"
        { Defaults.dashboard<unit> with
            Children = stacks }

let private baseTree = build id (fun _ s -> s) id

/// Count the nodes of a tree (the scenario label quotes it).
let rec private count (n: Node<unit>) : int =
    match Fuaran.UI.Ops.Introspect.getChildren n.Kind with
    | None -> 1
    | Some kids -> 1 + (kids |> List.sumBy count)

type Scenario = { Name: string; After: Node<unit> }

let private midStack = Stacks / 2
let private midLeaf = $"leaf-{midStack}-24"

let all: Scenario list =
    [ { Name = "identical"; After = baseTree }
      { Name = "one_field"
        After =
          build
              (fun n ->
                  if n.Id = midLeaf then
                      Fuaran.heading
                          n.Id
                          { Defaults.heading with
                              Text = TextSource.Literal "Edited heading" }
                  else
                      n)
              (fun _ s -> s)
              id }
      { Name = "one_container"
        After =
          build id (fun _ s -> s) (fun st ->
              if st.Id = $"stack-{midStack}" then
                  match st.Kind with
                  | NodeKind.Box spec ->
                      { st with
                          Kind =
                              NodeKind.Box
                                  { spec with
                                      Heading = Some(TextSource.Literal "Edited section") } }
                  | _ -> st
              else
                  st) }
      { Name = "one_move"
        After = build id (fun nid s -> if nid = "leaf-3-10" then 30 else s) id } ]

let nodeCount () : int = count baseTree

/// Mean wall-time (µs) and allocation (bytes) per `TreeOpDiff.diff baseTree
/// scenario.After`, plus the op count of one diff (a sanity line: the edit shape
/// the scenario claims is the edit the diff found).
let measure (scenario: Scenario) (iterations: int) : float * float * int =
    let ops = TreeOpDiff.diff baseTree scenario.After

    for _ in 1 .. max 1 (iterations / 10) do
        TreeOpDiff.diff baseTree scenario.After |> ignore

    GC.Collect()
    let allocBefore = GC.GetAllocatedBytesForCurrentThread()
    let sw = Stopwatch.StartNew()

    for _ in 1..iterations do
        TreeOpDiff.diff baseTree scenario.After |> ignore

    sw.Stop()
    let allocAfter = GC.GetAllocatedBytesForCurrentThread()
    let meanUs = sw.Elapsed.TotalMilliseconds * 1000.0 / float iterations
    let allocB = float (allocAfter - allocBefore) / float iterations
    meanUs, allocB, List.length ops
