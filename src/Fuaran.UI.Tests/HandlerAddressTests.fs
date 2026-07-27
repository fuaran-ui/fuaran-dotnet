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
// ============================================================================

open System.IO
open Expecto

open Fuaran.UI
open Fuaran.UI.Ops.JsonDecode

let private corpusDir () : string option =
    let rec climb (dir: DirectoryInfo option) =
        match dir with
        | None -> None
        | Some d ->
            let candidate = Path.Combine(d.FullName, "wire-format-fixtures", "nodes")

            if Directory.Exists candidate then
                Some candidate
            else
                climb (Option.ofObj d.Parent)

    climb (Some(DirectoryInfo(System.AppContext.BaseDirectory)))

let private fixtures () : (string * string) list =
    match corpusDir () with
    | None -> []
    | Some d ->
        let fileName (p: string) =
            Path.GetFileName p |> Option.ofObj |> Option.defaultValue p

        Directory.GetFiles(d, "*.json")
        |> Array.toList
        |> List.sortBy fileName
        |> List.map (fun p -> fileName p, (File.ReadAllText p).Trim())

/// The `DuplicateNodeId` defects pre-emit validation finds in one decoded tree.
let private duplicateIdDefects (json: string) : (string * int) list =
    match decodeNodeObj json with
    | Error _ -> []
    | Ok node ->
        match PreEmitValidate.validate node with
        | Ok() -> []
        | Error defects ->
            defects
            |> List.choose (function
                | PreEmitValidate.PreEmitDefect.DuplicateNodeId(id, count) -> Some(id, count)
                | _ -> None)

[<Tests>]
let handlerAddressTests =
    testList
        "Phase 689 — handler addressing"
        [ test "the corpus fixtures carrying a duplicate NodeId are pre-emit DEFECTS, not legal trees" {
              let all = fixtures ()

              if List.isEmpty all then
                  skiptest "wire-format-fixtures/nodes not found"

              let offenders =
                  all
                  |> List.choose (fun (name, json) ->
                      match duplicateIdDefects json with
                      | [] -> None
                      | ds -> Some(name, ds))

              // Recorded so the number is visible in the run, not just asserted.
              printfn "── Phase 689 duplicate-NodeId scan: %d of %d fixtures ──" offenders.Length all.Length

              for name, ds in offenders do
                  printfn "   %s → %s" name (ds |> List.map (fun (i, c) -> sprintf "%s ×%d" i c) |> String.concat ", ")

              Expect.isNonEmpty
                  offenders
                  "pre-emit validation should refuse the duplicate-id fixtures — if it accepts them, ids really are not unique and (nodeId, slot) addressing is dead"
          } ]
