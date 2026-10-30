/// The `operability/` corpus family (WIRE_FORMAT §31.7), computed by the
/// reference: every tree is decoded from its wire JSON by the reference decoder,
/// re-encoded canonically, derived, and every instruction resolved over the
/// derivation. Emitted with `FUARAN_REGEN=1`; otherwise compared byte for byte
/// with the committed file.
module Fuaran.UI.Operability.Tests.Vectors

open System
open System.IO
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Operability

/// The family directory and its one vectors file, relative to the corpus root.
let FamilyDir = "operability"
let FileName = "operability-vectors.json"

let private fail (what: string) : 'a = failwith what

/// Decode authored wire JSON with the reference decoder, or fail loudly.
let decode (json: string) : Node<obj> =
    match JsonDecode.decodeNodeObj json with
    | Ok node -> node
    | Error e -> fail (sprintf "authored tree refused: %s at %s (%s)" e.Code e.Path e.Message)

/// The canonical re-encoding of authored wire JSON.
let canonicalOf (json: string) : string = Generated.encodeNode (decode json)

let private parse (text: string) : JVal =
    match Canonical.read text with
    | Ok v -> v
    | Error e -> fail (sprintf "canonical text unreadable: %A" e)

let affordancesOf (tree: Cases.Tree) : OperableAffordance<obj> list =
    Operability.derive tree.MountId (decode tree.Json)

let outcomeOf (case: Cases.Resolve) : Outcome =
    Operability.resolve case.Instruction (affordancesOf case.Tree)

let private optional (key: string) (value: JVal option) =
    match value with
    | Some v -> [ key, v ]
    | None -> []

let private description =
    "The operable surface of a tree (WIRE_FORMAT.md 31, Phase 2517). SELF-ENUMERATED (27.7): operability/manifest.json names this file, so the directory is one corpus family, operability. Emitted by the reference host (fuaran-dotnet: FUARAN_REGEN=1 over Fuaran.UI.Operability.Tests); the inputs are authored there and every answer is the reference's. wire.roundTrip: decode canonical and re-encode it; the bytes must be equal (31.1). wire.reject: decode input; the decode error's code must equal expectedErrorCode and its path start with expectedPath (31.2). trees: decode tree with the host's codec, derive its affordances under mountId (absent: no mount) and compare the canonical encoding of the list with affordances byte for byte (31.3, 31.7). resolve: resolve instruction over the derivation of the named tree and compare the canonical encoding of the outcome with expected (31.4, 31.5); stratum names the calibration stratum the case was recast from."

/// The whole family file, as the reference computes it.
let emit () : string =
    let roundTrips =
        Cases.roundTrips
        |> List.map (fun c -> JObj [ "id", JStr c.Id; "canonical", JStr(canonicalOf c.Json) ])

    let rejects =
        Cases.rejects
        |> List.map (fun c ->
            match JsonDecode.decodeNodeObj c.Json with
            | Ok _ -> fail (sprintf "%s: the reference ACCEPTED a document the family says it refuses" c.Id)
            | Error e ->
                JObj
                    [ "id", JStr c.Id
                      "input", JStr c.Json
                      "expectedErrorCode", JStr e.Code
                      "expectedPath", JStr e.Path ])

    let trees =
        Cases.trees
        |> List.map (fun t ->
            JObj(
                [ "id", JStr t.Id
                  "tree", parse (canonicalOf t.Json)
                  "affordances", Operability.affordancesJson (affordancesOf t) ]
                @ optional "mountId" (t.MountId |> Option.map JStr)
            ))

    let resolves =
        Cases.resolves
        |> List.map (fun c ->
            JObj
                [ "id", JStr c.Id
                  "stratum", JStr c.Stratum
                  "tree", JStr c.Tree.Id
                  "instruction", JStr c.Instruction
                  "expected", Operability.outcomeJson (outcomeOf c) ])

    let file =
        JObj
            [ "version", JInt 1
              "description", JStr description
              "wire", JObj [ "roundTrip", JArr roundTrips; "reject", JArr rejects ]
              "trees", JArr trees
              "resolve", JArr resolves ]

    Operability.canonical file + "\n"

let familyPath (corpusRoot: string) =
    Path.Combine(corpusRoot, FamilyDir, FileName)

let regenerating = Environment.GetEnvironmentVariable "FUARAN_REGEN" = "1"
