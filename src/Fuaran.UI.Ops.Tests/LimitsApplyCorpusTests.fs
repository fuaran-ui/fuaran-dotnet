module Fuaran.UI.Tests.LimitsApplyCorpus

// ============================================================================
//  Certifies this host against the shared `apply/limits-apply.json` family
//  (Phase 2141): every op that can grow a tree is checked on its result, and
//  one that takes the tree past WireLimits is refused with `LimitExceeded`;
//  and against `apply/duplicate-ids-apply.json` (Phase 2172): an op that
//  leaves an id it installed held twice is refused with `DuplicateNodeId`.
//
//  Each vector's tree and op are decoded by this host's own wire decoder and
//  applied. An absent corpus is a FAILURE, not a skip: a suite that certified
//  against nothing would read green.
// ============================================================================

open System.IO
open System.Text.Json
open Expecto
open Fuaran.UI
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types

/// A JSON string member, refusing a null rather than carrying one.
let private str (e: JsonElement) : string =
    match e.GetString() with
    | null -> failwith "expected a JSON string"
    | s -> s

type private Vector =
    { Id: string
      Tree: string
      Op: string
      Verdict: string
      Code: string option }

let private loadVectors (familyId: string) : int * Vector list =
    let root = Fuaran.Tests.CorpusRoot.find ()

    let manifest =
        JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "apply", "manifest.json")))

    let declared =
        manifest.RootElement.GetProperty("families").EnumerateArray()
        |> Seq.find (fun f -> str (f.GetProperty("id")) = familyId)

    let file = str (declared.GetProperty("file"))
    let count = declared.GetProperty("vectors").GetInt32()
    let doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "apply", file)))

    let vectors =
        doc.RootElement.GetProperty("vectors").EnumerateArray()
        |> Seq.map (fun v ->
            let input = v.GetProperty("input")
            let expected = v.GetProperty("expected")

            { Id = str (v.GetProperty("id"))
              Tree = str (input.GetProperty("tree"))
              Op = str (input.GetProperty("op"))
              Verdict = str (expected.GetProperty("verdict"))
              Code =
                match expected.TryGetProperty("code") with
                | true, c -> Some(str c)
                | _ -> None })
        |> List.ofSeq

    count, vectors

let private codeToken (code: ApplyErrorCode) : string =
    match code with
    | ApplyErrorCode.LimitExceeded -> "LimitExceeded"
    | ApplyErrorCode.DuplicateNodeId -> "DuplicateNodeId"
    | other -> sprintf "%A" other

/// Every vector of `familyId` holds on this host.
let private certify (familyId: string) =
    let declared, vectors = loadVectors familyId
    Expect.equal (List.length vectors) declared "the manifest's vector count matches the family file"

    for v in vectors do
        let tree =
            match JsonDecode.decodeNodeObj v.Tree with
            | Ok t -> t
            | Error e -> failtestf "%s: the tree did not decode: %A" v.Id e

        let op =
            match JsonDecode.decodeOp v.Op with
            | Ok o -> o
            | Error e -> failtestf "%s: the op did not decode: %A" v.Id e

        match v.Verdict, Apply.apply op tree with
        | "accept", Ok _ -> ()
        | "accept", Error err -> failtestf "%s: expected accept, refused with %A" v.Id err.Code
        | "reject", Error err -> Expect.equal (Some(codeToken err.Code)) v.Code (sprintf "%s: the refusal code" v.Id)
        | "reject", Ok _ -> failtestf "%s: expected a %A refusal, the op applied" v.Id v.Code
        | other, _ -> failtestf "%s: unknown verdict %s" v.Id other

[<Tests>]
let tests =
    testList
        "Fuaran.UI.Ops apply/ corpus families"
        [ test "every limitsApply vector holds on this host" { certify "limitsApply" }
          test "every duplicateIdsApply vector holds on this host" { certify "duplicateIdsApply" } ]
