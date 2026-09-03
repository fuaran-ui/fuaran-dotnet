module Fuaran.UI.Tests.HandlerAddress

// ============================================================================
//  Phase 689 — the handler-address measurement.
//
//  The programme doc recorded "node ids are not unique" as settled evidence,
//  on the strength of 4 of 85 corpus fixtures repeating an id. This asks the
//  tier whether that is authoring or defect, rather than inferring it from the
//  fixture bytes:
//
//   - `PreEmitValidate` declares `DuplicateNodeId` a defect, and the session
//     bundle path REFUSES a tree carrying one (`WIRE_FORMAT.md` §14 step 6);
//   - the validator's `FUARAN001` is an **Error**, not a warning, with the
//     reason stated as "§4g op-target stability";
//   - every `TreeOp` addresses by `NodeId` alone, so a duplicate makes
//     `UpdateProp` / `RemoveNode` / `MoveNode` ambiguous by construction.
//
//  If pre-emit refuses exactly those 4, then id uniqueness is an invariant the
//  corpus violates — not a property the language lacks — and `(nodeId, slot)`
//  is a legal handler address after all.
//
//  It refused exactly those 4, so Phase 695 fixed the fixtures (they were
//  authoring artefacts — the generator composed one sample child into several
//  containers) and wrote the invariant into `WIRE_FORMAT.md` §8, which had
//  listed present / empty / absent / wrong-type and said nothing about
//  uniqueness. The assertion below is INVERTED from 689's: the measurement has
//  become the guard, so a fifth duplicate cannot arrive the way the first four
//  did — silently, because the round-trip family asks only that encode(decode x)
//  reproduces x, which an invalid tree satisfies as readily as a valid one.
// ============================================================================

open System.IO
open Expecto

open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops.JsonDecode

/// The corpus root — the `wire-format-fixtures/` clone, wherever it sits above
/// the test binary.
let private corpusRoot () : string option =
    let rec climb (dir: DirectoryInfo option) =
        match dir with
        | None -> None
        | Some d ->
            let candidate = Path.Combine(d.FullName, "wire-format-fixtures")

            if Directory.Exists(Path.Combine(candidate, "nodes")) then
                Some candidate
            else
                climb (Option.ofObj d.Parent)

    climb (Some(DirectoryInfo(System.AppContext.BaseDirectory)))

let private fixturesIn (family: string) : (string * string) list =
    match corpusRoot () with
    | None -> []
    | Some root ->
        let d = Path.Combine(root, family)

        if not (Directory.Exists d) then
            []
        else
            let fileName (p: string) =
                Path.GetFileName p |> Option.ofObj |> Option.defaultValue p

            Directory.GetFiles(d, "*.json")
            |> Array.toList
            |> List.filter (fun p -> fileName p <> "manifest.json")
            |> List.sortBy fileName
            |> List.map (fun p -> $"{family}/{fileName p}", (File.ReadAllText p).Trim())

/// The `DuplicateNodeId` defects pre-emit validation finds in one tree. Asking the
/// tier rather than re-walking the JSON is deliberate: §8.1's isolation boundaries
/// (`Mount`, `FragmentRef`) and the pre-expansion `FragmentDecl` rule live in
/// `PreEmitValidate`, and a guard that re-implemented them could agree with itself
/// while disagreeing with the language.
let private duplicatesIn (node: Node<obj>) : (string * int) list =
    match PreEmitValidate.validate node with
    | Ok() -> []
    | Error defects ->
        defects
        |> List.choose (function
            | PreEmitValidate.PreEmitDefect.DuplicateNodeId(id, count) -> Some(id, count)
            | _ -> None)

let private duplicateIdDefects (json: string) : (string * int) list =
    match decodeNodeObj json with
    | Error _ -> []
    | Ok node -> duplicatesIn node

/// Every `Node` an op carries. Three cases embed one directly; `EditNode` embeds a
/// bare `NodeKind`, which still has children, so it is wrapped in a synthetic root
/// whose id cannot collide with an authored one.
let rec private nodesUnder (op: TreeOp<obj>) : Node<obj> list =
    match op with
    | TreeOp.ReplaceRoot n -> [ n ]
    | TreeOp.InsertChild(_, child) -> [ child ]
    | TreeOp.Batch ops -> ops |> List.collect nodesUnder
    | TreeOp.EditNode(_, kind) ->
        [ { Id = $"$editnode-root-{System.Guid.NewGuid()}"
            Kind = kind
            State = None
            Style = None
            Accessibility = None
            Motion = None
            ExtraAttributes = None
            Tooltip = None } ]
    | _ -> []

let private duplicateIdDefectsInOp (json: string) : (string * int) list =
    match decodeOp json with
    | Error _ -> []
    | Ok op -> op |> nodesUnder |> List.collect duplicatesIn

[<Tests>]
let handlerAddressTests =
    testList
        "Phase 689 — handler addressing"
        [ test "no corpus fixture repeats a NodeId within one tree (§8.1 uniqueness)" {
              // `ops/` is scanned as well, and not for completeness' sake: `op-replaceroot`
              // embeds the whole `composite-root` tree, so it carried the duplicate the 689
              // measurement reported against `nodes/composite-root` — and nothing counted it
              // there, because the op families were never in the scan.
              let nodeFixtures = fixturesIn "nodes"
              let opFixtures = fixturesIn "ops"

              if List.isEmpty nodeFixtures then
                  skiptest "wire-format-fixtures/nodes not found"

              let scanned =
                  (nodeFixtures |> List.map (fun (n, j) -> n, duplicateIdDefects j))
                  @ (opFixtures |> List.map (fun (n, j) -> n, duplicateIdDefectsInOp j))

              let offenders =
                  scanned
                  |> List.choose (function
                      | _, [] -> None
                      | name, ds -> Some(name, ds))

              let render ds =
                  ds |> List.map (fun (i, c) -> sprintf "%s ×%d" i c) |> String.concat ", "

              // Recorded so the scanned count is visible in the run, not just asserted — a
              // guard that silently scanned nothing would otherwise read exactly as green as
              // one that scanned everything.
              printfn "── §8.1 duplicate-NodeId scan: %d offenders in %d fixtures ──" offenders.Length scanned.Length

              for name, ds in offenders do
                  printfn "   %s → %s" name (render ds)

              let report =
                  offenders
                  |> List.map (fun (name, ds) -> sprintf "%s (%s)" name (render ds))
                  |> String.concat "; "

              Expect.isEmpty
                  offenders
                  $"NodeIds are unique within a tree (WIRE_FORMAT.md §8.1): pre-emit validation refuses these, every TreeOp addresses by NodeId alone, and the teleport-bundle path refuses the tree outright — so a fixture carrying one is a corpus defect, not a legal document. Offenders: {report}"
          } ]
