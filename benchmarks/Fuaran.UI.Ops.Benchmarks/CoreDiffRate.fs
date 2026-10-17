module Fuaran.UI.Ops.Benchmarks.CoreDiffRate

open System
open System.Diagnostics
open System.IO
open System.Text.Json
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Replay

// ============================================================================
//  CoreDiffRate (Phase 2304) — the cost of `TreeOpDiff.diff` over the corpus
//  pairs and a 10,001-node tree, and the size of the script it emits.
//
//  Phase 2304 puts the diff on Fuaran.Core's `Diff.toOpsContainedWith`, which
//  relocates every survivor by `MoveNode` where the tier's own diff re-sent a
//  relocated subtree as `RemoveNode` + `InsertChild` whenever its new parent
//  was new or was the root. This benchmark prices both halves of that trade:
//  wall time and allocation per diff, and the op count and canonical bytes of
//  the script, with the relocation families counted apart so the
//  identity-preserving form is costed rather than assumed.
//
//   - `corpus`            — one sweep over the corpus pairs: every
//                           `diff/goldens.json` pair in both directions, and
//                           every node fixture against itself and against its
//                           cyclic neighbour re-rooted at its id. Skipped, and
//                           said so, when the corpus is not found
//                           (`FUARAN_WIRE_FIXTURES`, else the clone beside the
//                           repository).
//   - `identical`         — the 10,001-node tree against itself.
//   - `one_field`         — one leaf's text changes.
//   - `one_move`          — one leaf moves to another stack.
//   - `regroup`           — the relocation-heavy pair: ten stacks (990 leaves
//                           below them) each move into a NEW group container,
//                           which the tier's own diff could only re-send whole.
//   - `scatter`           — every tenth leaf (990 leaves) moves to the next
//                           stack, a relocation both diffs express as `MoveNode`.
//
//  `dotnet run -c Release -- core-diff [iterations]`.
// ============================================================================

[<Literal>]
let private Stacks = 100

[<Literal>]
let private LeavesPerStack = 99

let private leaf (s: int) (i: int) : Node<obj> =
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

let private stack (id: string) (kids: Node<obj> list) : Node<obj> =
    Fuaran.stack id { Defaults.stack with Children = kids }

let private root (kids: Node<obj> list) : Node<obj> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<obj> with
            Children = kids }

/// The leaves of stack `s`, edited by `editLeaf`.
let private leavesOf (editLeaf: Node<obj> -> Node<obj>) (s: int) : Node<obj> list =
    [ for i in 0 .. LeavesPerStack - 1 -> editLeaf (leaf s i) ]

let private baseTree =
    root [ for s in 0 .. Stacks - 1 -> stack $"stack-{s}" (leavesOf id s) ]

let rec private count (n: Node<obj>) : int =
    match Introspect.getChildren n.Kind with
    | None -> 1
    | Some kids -> 1 + (kids |> List.sumBy count)

let nodeCount () : int = count baseTree

type Scenario =
    { Name: string
      Pairs: (Node<obj> * Node<obj>) list }

let private oneField =
    let target = "leaf-50-48"

    root
        [ for s in 0 .. Stacks - 1 ->
              stack
                  $"stack-{s}"
                  (leavesOf
                      (fun n ->
                          if n.Id = target then
                              Fuaran.heading
                                  n.Id
                                  { Defaults.heading with
                                      Text = TextSource.Literal "Edited heading" }
                          else
                              n)
                      s) ]

let private oneMove =
    let moved = leaf 3 10

    root
        [ for s in 0 .. Stacks - 1 ->
              let kids = leavesOf id s |> List.filter (fun n -> n.Id <> moved.Id)
              stack $"stack-{s}" (if s = 30 then kids @ [ moved ] else kids) ]

let private regroup =
    root
        [ for s in 0 .. Stacks - 1 ->
              let st = stack $"stack-{s}" (leavesOf id s)
              if s < 10 then stack $"group-{s}" [ st ] else st ]

let private scatter =
    let movesAway (i: int) = i % 10 = 0

    root
        [ for s in 0 .. Stacks - 1 ->
              let kept =
                  leavesOf id s |> List.indexed |> List.filter (fun (i, _) -> not (movesAway i))

              let prev = (s + Stacks - 1) % Stacks

              let arriving =
                  leavesOf id prev |> List.indexed |> List.filter (fun (i, _) -> movesAway i)

              stack $"stack-{s}" ((kept @ arriving) |> List.map snd) ]

// ─── the corpus pairs ───────────────────────────────────────────────────────

let private corpusRoot () : string option =
    let fromEnv =
        match Environment.GetEnvironmentVariable "FUARAN_WIRE_FIXTURES" with
        | Null -> []
        | NonNull "" -> []
        | NonNull v -> [ v ]

    let repo = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))

    fromEnv @ [ Path.Combine(repo, "..", "wire-format-fixtures") ]
    |> List.tryFind (fun r -> Directory.Exists(Path.Combine(r, "nodes")))

let private decode (text: string) : Node<obj> option =
    match JsonDecode.decodeNodeObj text with
    | Ok n -> Some n
    | Error _ -> None

let private corpusPairs (dir: string) : (Node<obj> * Node<obj>) list =
    let nodes =
        Directory.GetFiles(Path.Combine(dir, "nodes"), "*.json")
        |> Array.sort
        |> Array.choose (fun p -> decode (File.ReadAllText p))

    use doc =
        JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "diff", "goldens.json")))

    let goldens =
        [ for section in [ "goldens"; "divergent" ] do
              for entry in doc.RootElement.GetProperty(section).EnumerateArray() do
                  match
                      decode (entry.GetProperty("before").GetRawText()),
                      decode (entry.GetProperty("after").GetRawText())
                  with
                  | Some b, Some a ->
                      yield b, a
                      yield a, b
                  | _ -> () ]

    let fixtures =
        [ for i in 0 .. nodes.Length - 1 do
              let a = nodes.[i]
              yield a, a

              yield
                  a,
                  { nodes.[(i + 1) % nodes.Length] with
                      Id = a.Id } ]

    goldens @ fixtures

/// The scenarios, the corpus sweep first when the corpus is present. The
/// second component says why the sweep is absent, when it is.
let scenarios () : Scenario list * string option =
    let tree =
        [ { Name = "identical"
            Pairs = [ baseTree, baseTree ] }
          { Name = "one_field"
            Pairs = [ baseTree, oneField ] }
          { Name = "one_move"
            Pairs = [ baseTree, oneMove ] }
          { Name = "regroup"
            Pairs = [ baseTree, regroup ] }
          { Name = "scatter"
            Pairs = [ baseTree, scatter ] } ]

    match corpusRoot () with
    | Some dir ->
        { Name = "corpus"
          Pairs = corpusPairs dir }
        :: tree,
        None
    | None -> tree, Some "corpus not found (set FUARAN_WIRE_FIXTURES); the corpus sweep is skipped"

/// What one diff of the scenario emits: ops, canonical bytes, and the ops of
/// each relocation-relevant family.
type ScriptShape =
    { Ops: int
      Bytes: int
      Moves: int
      Inserts: int
      Removes: int
      Pairs: int
      RoundTrips: int }

let rec private flatten (op: TreeOp<obj>) : TreeOp<obj> list =
    match op with
    | TreeOp.Batch inner -> inner |> List.collect flatten
    | other -> [ other ]

let shape (scenario: Scenario) : ScriptShape =
    let scripts = scenario.Pairs |> List.map (fun (a, b) -> a, b, TreeOpDiff.diff a b)

    let ops = scripts |> List.collect (fun (_, _, s) -> s |> List.collect flatten)

    let isMove =
        function
        | TreeOp.MoveNode _ -> true
        | _ -> false

    let isInsert =
        function
        | TreeOp.InsertChild _ -> true
        | _ -> false

    let isRemove =
        function
        | TreeOp.RemoveNode _ -> true
        | _ -> false

    let roundTrips =
        scripts
        |> List.filter (fun (a, b, s) ->
            let folded =
                s
                |> List.fold (fun t op -> t |> Option.bind (fun t -> Apply.apply op t |> Result.toOption)) (Some a)

            match folded with
            | Some t -> CanonicalJson.encodeNode t = CanonicalJson.encodeNode b
            | None -> false)
        |> List.length

    { Ops = List.length ops
      Bytes = ops |> List.sumBy (fun op -> (CanonicalJson.encodeOp op).Length)
      Moves = ops |> List.filter isMove |> List.length
      Inserts = ops |> List.filter isInsert |> List.length
      Removes = ops |> List.filter isRemove |> List.length
      Pairs = List.length scripts
      RoundTrips = roundTrips }

/// Mean wall time (µs) and allocation (bytes) per diff of every pair of the
/// scenario, warm-up first.
let measure (scenario: Scenario) (iterations: int) : float * float =
    let run () =
        for a, b in scenario.Pairs do
            TreeOpDiff.diff a b |> ignore

    for _ in 1 .. max 1 (iterations / 10) do
        run ()

    GC.Collect()
    let allocBefore = GC.GetAllocatedBytesForCurrentThread()
    let sw = Stopwatch.StartNew()

    for _ in 1..iterations do
        run ()

    sw.Stop()
    let allocAfter = GC.GetAllocatedBytesForCurrentThread()
    sw.Elapsed.TotalMilliseconds * 1000.0 / float iterations, float (allocAfter - allocBefore) / float iterations
