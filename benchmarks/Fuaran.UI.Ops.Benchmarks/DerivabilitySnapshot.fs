module Fuaran.UI.Ops.Benchmarks.DerivabilitySnapshot

open System
open System.IO
open System.Text.Json
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.OpStream.Dag.Merge

// ============================================================================
//  DerivabilitySnapshot (Phase 2292) — how often Fuaran.Core's footprint
//  refuses a merge the tier's own three-way merge makes, while folding the
//  two branches' ops reaches the same tree in either order.
//
//  For a triad (base, a, b) two verdicts are taken:
//
//   - the UI's: `TreeMerge.merge3Way base a b`, over the trees;
//   - Core's: `Dag.conflicts` over the two branches' OPS with the shipped
//     projection `TreeOpFootprint.ofOp`. No conflict means the fold composes the
//     branches, and both apply orders are run to see whether they agree.
//
//  Each triad lands in one cell. The number this exists to print is
//  `derivability.overapprox_share`: of the triads the UI auto-merges, the share
//  Core refuses although both apply orders reach the UI's merged tree — the
//  footprint's over-approximation, measured. Every change to the projection (or
//  to Core's independence clauses) re-measures it against the baseline recorded
//  in STABILITY.md under Phase 2292.
//
//  Two populations, reported separately and pooled:
//
//   - corpus: every triad in the wire corpus's `merge-conformance/manifest.json`
//     (`FUARAN_WIRE_FIXTURES`, else the clone beside this repository), each
//     branch's ops read off `TreeOpDiff.diffBatched base branch`;
//   - generated: seed 20261016, branches of one to three ops off a small tree,
//     each op applied to the tree its branch has reached.
//
//  `dotnet run -c Release -- derivability-snapshot [trials]` (default 20000).
//  Deterministic: a seeded `System.Random` and no clock.
// ============================================================================

type Cell =
    /// Core composes; the fold is the UI's merged tree.
    | Identical
    /// Core composes and the orders agree, on a tree other than the UI's.
    | DerivableElsewhere
    /// Core composes, and the two apply orders disagree.
    | OrderSensitive
    /// Core refuses; both orders reach the UI's merged tree. The over-approximation.
    | ConflictButFoldAgrees
    /// Core refuses; both orders agree, on a tree other than the UI's.
    | ConflictFoldDiffers
    /// Core refuses; the two orders disagree.
    | ConflictOrderSensitive
    /// Core refuses; an order does not apply.
    | ConflictFoldFaults
    /// Core composes, and an order does not apply.
    | Fault
    /// The UI refuses and Core composes.
    | UiRefusesCoreComposes
    /// Both refuse.
    | BothRefuse

let private allCells =
    [ Identical
      DerivableElsewhere
      OrderSensitive
      ConflictButFoldAgrees
      ConflictFoldDiffers
      ConflictOrderSensitive
      ConflictFoldFaults
      Fault
      UiRefusesCoreComposes
      BothRefuse ]

let private uiMerges (c: Cell) =
    match c with
    | UiRefusesCoreComposes
    | BothRefuse -> false
    | _ -> true

let private canon (n: Node<obj>) = CanonicalJson.encodeNode n

let private applySeq (ops: TreeOp<obj> list) (start: Node<obj>) : Result<Node<obj>, ApplyError> =
    ops |> List.fold (fun acc op -> acc |> Result.bind (Apply.apply op)) (Ok start)

let private shapeName (c: Fuaran.Core.MergeConflict<TreeOp<obj>>) =
    match c.Shape with
    | Fuaran.Core.MergeConflictShape.ConcurrentUpdate -> "ConcurrentUpdate"
    | Fuaran.Core.MergeConflictShape.InsertPositionClash -> "InsertPositionClash"
    | Fuaran.Core.MergeConflictShape.MoveVsRemove -> "MoveVsRemove"
    | Fuaran.Core.MergeConflictShape.SlotClash slot -> "SlotClash(" + slot + ")"

/// The cell a triad lands in, and — for a Core refusal — the conflict shapes it named.
let classify
    (b: Node<obj>)
    (a: Node<obj>)
    (bb: Node<obj>)
    (dA: TreeOp<obj> list)
    (dB: TreeOp<obj> list)
    : Cell * string =
    let conflicts = Fuaran.Core.Dag.conflicts TreeOpFootprint.ofOp dA dB

    let shapes =
        conflicts
        |> List.map shapeName
        |> List.distinct
        |> List.sort
        |> String.concat "+"

    let ab = applySeq (dA @ dB) b
    let ba = applySeq (dB @ dA) b

    match TreeMerge.merge3Way b a bb with
    | Error _ ->
        (if conflicts.IsEmpty then
             UiRefusesCoreComposes
         else
             BothRefuse),
        shapes
    | Ok merged ->
        match conflicts.IsEmpty, ab, ba with
        | true, Ok x, Ok y when canon x = canon y ->
            (if canon x = canon merged then
                 Identical
             else
                 DerivableElsewhere),
            ""
        | true, Ok _, Ok _ -> OrderSensitive, ""
        | true, _, _ -> Fault, ""
        | false, Ok x, Ok y when canon x = canon y ->
            (if canon x = canon merged then
                 ConflictButFoldAgrees
             else
                 ConflictFoldDiffers),
            shapes
        | false, Ok _, Ok _ -> ConflictOrderSensitive, shapes
        | false, _, _ -> ConflictFoldFaults, shapes

// ─── corpus triads ──────────────────────────────────────────────────────────

let private corpusRoot () : string option =
    let fromEnv =
        match Environment.GetEnvironmentVariable "FUARAN_WIRE_FIXTURES" with
        | Null -> []
        | NonNull "" -> []
        | NonNull v -> [ v ]

    let repo = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))

    fromEnv @ [ Path.Combine(repo, "..", "wire-format-fixtures") ]
    |> List.map (fun r -> Path.Combine(r, "merge-conformance"))
    |> List.tryFind (fun d -> File.Exists(Path.Combine(d, "manifest.json")))

let private decodeTree (path: string) : Node<obj> =
    match JsonDecode.decodeNodeObj (File.ReadAllText path) with
    | Ok n -> n
    | Error e -> failwithf "%s does not decode: %s" path (JsonDecode.DecodeError.render e)

/// Every triad in the merge-conformance manifest, as (base, a, b, ops a, ops b); `None` when the
/// corpus is not beside this repository.
let corpusTriads () =
    corpusRoot ()
    |> Option.map (fun root ->
        use manifest =
            JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")))

        [ for arrayName in [ "fixtures"; "refusalFixtures"; "totalityFixtures" ] do
              match manifest.RootElement.TryGetProperty arrayName with
              | true, fixtures ->
                  for f in fixtures.EnumerateArray() do
                      let file (k: string) =
                          Path.Combine(root, f.GetProperty(k).GetString() |> nonNull)

                      let b = decodeTree (file "baseFile")
                      let a = decodeTree (file "aFile")
                      let bb = decodeTree (file "bFile")
                      b, a, bb, TreeOpDiff.diffBatched b a, TreeOpDiff.diffBatched b bb
              | _ -> () ])

// ─── generated triads ───────────────────────────────────────────────────────

let private leaf (id: string) (text: string) : Node<obj> = Fuaran.markdown id text

let private boxOf (id: string) (kids: Node<obj> list) : Node<obj> =
    Fuaran.box id { Defaults.box with Children = kids }

let private baseTree () : Node<obj> =
    boxOf
        "root"
        [ leaf "c0" "zero"
          leaf "c1" "one"
          boxOf "sub" [ leaf "s0" "sub-zero" ]
          leaf "c2" "two" ]

let private childIds = [| "c0"; "c1"; "c2"; "s0" |]
let private containers = [| "root"; "sub" |]

let private tones =
    [| ToneVariant.Brand; ToneVariant.Success; ToneVariant.Critical |]

let private voices = [| FontVoice.Display; FontVoice.Structural |]
let private freshIds = [| "i0"; "i1"; "i2"; "i3"; "i4"; "i5" |]
let private freshTexts = [| ""; "alpha"; "beta" |]

/// One op against `tree`: an insert, a field-level restyle (tone or voice), a removal, a move, a
/// reorder of the root, or a kind edit.
let private genOp (rng: Random) (tree: Node<obj>) : TreeOp<obj> =
    let pick (arr: 'a[]) = arr[rng.Next arr.Length]

    match rng.Next 7 with
    | 0 -> TreeOp.InsertChild(NodeId(pick containers), leaf (pick freshIds) (pick freshTexts))
    | 1 ->
        TreeOp.PatchStyle(
            NodeId(pick childIds),
            { StyleDelta.empty with
                Tone = Some(pick tones) }
        )
    | 2 ->
        TreeOp.PatchStyle(
            NodeId(pick childIds),
            { StyleDelta.empty with
                Voice = Some(pick voices) }
        )
    | 3 -> TreeOp.RemoveNode(NodeId(pick childIds))
    | 4 -> TreeOp.MoveNode(NodeId(pick childIds), NodeId(pick containers))
    | 5 ->
        let kids =
            Introspect.getChildren tree.Kind
            |> Option.defaultValue []
            |> List.map (fun c -> c.Id)

        TreeOp.ReorderChildren(NodeId "root", kids |> List.sortBy (fun _ -> rng.Next()) |> List.map NodeId)
    | _ ->
        let id = pick childIds
        TreeOp.EditNode(NodeId id, (leaf id (pick [| "p"; "q"; "r" |])).Kind)

/// A branch of one to three ops, each applied to the tree the branch has reached; `None` when a
/// drawn op does not apply.
let private genBranch (rng: Random) (start: Node<obj>) : (TreeOp<obj> list * Node<obj>) option =
    let n = 1 + rng.Next 3

    let rec go i (ops: TreeOp<obj> list) (t: Node<obj>) =
        if i = n then
            Some(List.rev ops, t)
        else
            let op = genOp rng t

            match Apply.apply op t with
            | Ok t' -> go (i + 1) (op :: ops) t'
            | Error _ -> None

    go 0 [] start

/// The seed the generated population is drawn from.
let seed = 20261016

/// Every generated triad with both branches applicable, from `trials` draws.
let generatedTriads (trials: int) =
    let rng = Random seed
    let b = baseTree ()

    [ for _ in 1..trials do
          match genBranch rng b, genBranch rng b with
          | Some(dA, a), Some(dB, bb) -> b, a, bb, dA, dB
          | _ -> () ]

// ─── the report ─────────────────────────────────────────────────────────────

/// The lines `derivability-snapshot` prints for one population.
let report (name: string) (triads: (Node<obj> * Node<obj> * Node<obj> * TreeOp<obj> list * TreeOp<obj> list) list) =
    let classified =
        triads |> List.map (fun (b, a, bb, dA, dB) -> classify b a bb dA dB)

    let count (c: Cell) =
        classified |> List.filter (fun (k, _) -> k = c) |> List.length

    let merged = classified |> List.filter (fst >> uiMerges) |> List.length

    [ yield sprintf "derivability.%s.triads = %d" name (List.length classified)
      yield sprintf "derivability.%s.ui_merges = %d" name merged
      for c in allCells do
          yield sprintf "derivability.%s.cell.%A = %d" name c (count c)
      for shapes, n in
          classified
          |> List.filter (fun (k, _) -> k = ConflictButFoldAgrees)
          |> List.countBy snd
          |> List.sortBy (fun (s, n) -> -n, s) do
          yield sprintf "derivability.%s.overapprox_shapes.%s = %d" name shapes n
      yield
          sprintf
              "derivability.%s.overapprox_share = %s"
              name
              (if merged = 0 then
                   "n/a"
               else
                   sprintf "%.4f" (float (count ConflictButFoldAgrees) / float merged)) ]
