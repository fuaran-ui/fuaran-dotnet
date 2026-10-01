module Fuaran.UI.JsonDecode.Tests.Repair

// ============================================================================
//  Phase 1923 — deliberate repair (WIRE_FORMAT.md §28): the reference host's
//  certification of the `repair/` corpus family, and the properties that make
//  `repair` a separate act rather than a decode behaviour.
//
//  The family is HAND-AUTHORED (`repair/manifest.json`), like `decode-policy/`
//  and `stored-emissions/`. Each case names an input, the STRICT decoder's
//  answer to it, and what `repair` must return — the repaired text byte for
//  byte with the applied ids, or the refusal token. The TypeScript host
//  certifies the same declaration, so the two agree byte-for-byte on every case.
//
//  Counter-sensitive (the purity test reads `Reliance`, and the lenient-path
//  equivalence writes it), so the list runs sequenced beside the other
//  recovery suites.
// ============================================================================

open System.IO
open System.Text
open System.Text.Json
open Expecto
open Fuaran.UI.KindPolicy
open Fuaran.UI.Ops

type private Case =
    { Id: string
      InputFile: string
      Strict: string
      Outcome: string
      Applied: string list
      ExpectedFile: string option
      RepairedDecodes: string option
      Reason: string option }

let private text (v: JsonElement) : string =
    match v.GetString() with
    | null -> failwith "repair/manifest.json: a string member is null"
    | s -> s

let private optStr (el: JsonElement) (name: string) : string option =
    match el.TryGetProperty name with
    | true, v when v.ValueKind = JsonValueKind.String -> Some(text v)
    | _ -> None

/// A declared decode answer: the string `"accept"`, or `{code, path}`.
let private verdictOf (v: JsonElement) : string =
    match v.ValueKind with
    | JsonValueKind.String -> text v
    | _ -> sprintf "%s at %s" (text (v.GetProperty "code")) (text (v.GetProperty "path"))

type private Family =
    { Root: string
      CatalogueVersion: int
      Catalogue: string list
      Refusals: string list
      Hosts: Map<string, string>
      HostVersions: Map<string, int>
      Cases: Case list }

let private load () : Family =
    let root = Fuaran.Tests.CorpusRoot.find ()

    use doc =
        JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "repair", "manifest.json")))

    let r = doc.RootElement

    if text (r.GetProperty "kind") <> "repair-catalogue" then
        failwith "repair/manifest.json: unexpected kind"

    { Root = root
      CatalogueVersion = r.GetProperty("catalogueVersion").GetInt32()
      Catalogue = [ for c in r.GetProperty("catalogue").EnumerateArray() -> text c ]
      Refusals = [ for c in r.GetProperty("refusals").EnumerateArray() -> text c ]
      Hosts =
        r.GetProperty("hostStatements").EnumerateObject()
        |> Seq.map (fun p -> p.Name, text p.Value)
        |> Map.ofSeq
      HostVersions =
        r.GetProperty("hostCatalogueVersions").EnumerateObject()
        |> Seq.map (fun p -> p.Name, p.Value.GetInt32())
        |> Map.ofSeq
      Cases =
        [ for el in r.GetProperty("cases").EnumerateArray() do
              { Id = text (el.GetProperty "id")
                InputFile = text (el.GetProperty "inputFile")
                Strict = verdictOf (el.GetProperty "strict")
                Outcome = text (el.GetProperty "outcome")
                Applied =
                  match el.TryGetProperty "applied" with
                  | true, a -> [ for x in a.EnumerateArray() -> text x ]
                  | _ -> []
                ExpectedFile = optStr el "expectedFile"
                RepairedDecodes =
                  match el.TryGetProperty "repairedDecodes" with
                  | true, v -> Some(verdictOf v)
                  | _ -> None
                Reason = optStr el "reason" } ] }

/// The corpus bytes, decoded as UTF-8 exactly (no BOM handling, no newline
/// translation) — the family asserts byte-exact output.
let private readText (root: string) (rel: string) : string =
    let bytes =
        File.ReadAllBytes(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)))

    UTF8Encoding(false, true).GetString bytes

let private answer (r: Result<_, JsonDecode.DecodeError>) : string =
    match r with
    | Ok _ -> "accept"
    | Error e -> sprintf "%s at %s" e.Code e.Path

let private lenient =
    DecodePolicy.admitAll |> DecodePolicy.withRecovery Recovery.Lenient

[<Tests>]
let tests =
    let fam = load ()

    let perCase =
        [ for c in fam.Cases do
              test (sprintf "%s: %s" c.Id c.Outcome) {
                  let input = readText fam.Root c.InputFile

                  Expect.equal
                      (answer (JsonDecode.decodeNode input))
                      c.Strict
                      "the strict (default) decoder gives the declared answer"

                  match JsonDecode.repair input, c.Outcome with
                  | Repair.RepairOutcome.Repaired(repaired, applied), "repaired" ->
                      let expectedFile =
                          c.ExpectedFile |> Option.defaultWith (fun () -> failwith "no expectedFile")

                      Expect.equal repaired (readText fam.Root expectedFile) "the repaired text, byte for byte"
                      Expect.equal applied c.Applied "exactly the declared catalogue ids"

                      match c.RepairedDecodes with
                      | Some v ->
                          Expect.equal (answer (JsonDecode.decodeNode repaired)) v "the repaired text decodes strictly"
                      | None -> failwith "a repaired case declares repairedDecodes"
                  | Repair.RepairOutcome.NotRepairable reason, "not-repairable" ->
                      Expect.equal (Some reason) c.Reason "the declared refusal token"
                  | got, want -> failtestf "declared %s, got %A" want got
              } ]

    testSequenced
    <| testList
        "Fuaran.UI.Ops.Repair — deliberate repair (WIRE_FORMAT.md §28, Phase 1923)"
        [ test "the family is present and exercises every outcome" {
              Expect.isGreaterThanOrEqual fam.Cases.Length 20 "at least 20 cases"

              for id in Repair.RepairId.catalogue do
                  Expect.exists fam.Cases (fun c -> c.Applied = [ id ]) (sprintf "a case repaired by %s" id)

              for r in fam.Refusals do
                  Expect.exists fam.Cases (fun c -> c.Reason = Some r) (sprintf "a case refused as %s" r)

              Expect.exists
                  fam.Cases
                  (fun c -> c.Outcome = "repaired" && c.Applied.IsEmpty)
                  "the identity case (well-formed input)"
          }

          test "the declared catalogue, version and refusals are this host's" {
              Expect.equal fam.Catalogue Repair.RepairId.catalogue "the catalogue, in order"
              Expect.equal fam.CatalogueVersion Repair.CatalogueVersion "the catalogue version"

              Expect.equal
                  (List.sort fam.Refusals)
                  (List.sort
                      [ Repair.Refusal.LimitExceeded
                        Repair.Refusal.NotInCatalogue
                        Repair.Refusal.OverCloseAmbiguous
                        Repair.Refusal.OverCloseNoCleanCandidate
                        Repair.Refusal.OverCloseBounds
                        Repair.Refusal.WrongTypeCloseAmbiguous
                        Repair.Refusal.WrongTypeCloseNoCandidate ])
                  "the refusal tokens"

              Expect.equal (Map.tryFind "fuaran-dotnet" fam.Hosts) (Some "implements") "this host implements repair"

              Expect.equal
                  (Map.tryFind "fuaran-dotnet" fam.HostVersions)
                  (Some Repair.CatalogueVersion)
                  "this host declares the catalogue version it implements (§28.5)"
          }

          test "wrong-type-close composes with implied-node-close, in that order, and nothing else does" {
              // §28.2.3: the one composition the catalogue states. A case pins it,
              // and no case applies any other pair.
              Expect.exists
                  fam.Cases
                  (fun c -> c.Applied = [ Repair.RepairId.WrongTypeClose; Repair.RepairId.ImpliedNodeClose ])
                  "a case completed by implied-node-close"

              for c in fam.Cases do
                  if c.Applied.Length > 1 then
                      Expect.equal
                          c.Applied
                          [ Repair.RepairId.WrongTypeClose; Repair.RepairId.ImpliedNodeClose ]
                          (sprintf "%s: the only stated composition" c.Id)
          }

          test "the ids are continuous with the Reliance vocabulary" {
              Expect.equal Repair.RepairId.ImpliedNodeClose JsonDecode.Reliance.ImpliedNodeClose "implied-node-close"
              Expect.equal Repair.RepairId.OverCloseUnique JsonDecode.Reliance.OverCloseUnique "over-close-unique"
          }

          test "every file in repair/ is declared, and nothing else" {
              let onDisk =
                  Directory.GetFiles(Path.Combine(fam.Root, "repair"))
                  |> Array.map Path.GetFileName
                  |> Array.filter (fun f -> f <> "manifest.json" && f <> "README.md")
                  |> Array.sort
                  |> List.ofArray

              let declared =
                  fam.Cases
                  |> List.collect (fun c -> c.InputFile :: Option.toList c.ExpectedFile)
                  |> List.filter (fun f -> f.StartsWith "repair/")
                  |> List.map (fun f -> f.Substring "repair/".Length)
                  |> List.distinct
                  |> List.sort

              Expect.equal onDisk declared "the directory holds exactly the declared inputs and outputs"
          }

          test "repair is pure — it writes no Reliance counter" {
              let before = JsonDecode.Reliance.snapshot ()

              for c in fam.Cases do
                  JsonDecode.repair (readText fam.Root c.InputFile) |> ignore

              Expect.equal (JsonDecode.Reliance.snapshot ()) before "no counter moved"
          }

          test "repair is idempotent — a repaired text needs nothing further" {
              for c in fam.Cases do
                  match JsonDecode.repair (readText fam.Root c.InputFile) with
                  | Repair.RepairOutcome.Repaired(t, _) ->
                      Expect.equal (JsonDecode.repair t) (Repair.RepairOutcome.Repaired(t, [])) c.Id
                  | Repair.RepairOutcome.NotRepairable _ -> ()
          }

          test "Recovery.Lenient is repair then strict decode — one implementation" {
              let canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode

              for c in fam.Cases do
                  let input = readText fam.Root c.InputFile
                  let outcome = JsonDecode.decodeNodeObjWithOutcome lenient input

                  match JsonDecode.repair input with
                  | Repair.RepairOutcome.Repaired(t, applied) ->
                      match outcome.Result, JsonDecode.decodeNodeObj t with
                      | Ok a, Ok b -> Expect.equal (canon a) (canon b) (sprintf "%s: the same tree" c.Id)
                      | Error a, Error b -> Expect.equal a.Code b.Code (sprintf "%s: the same refusal" c.Id)
                      | a, b -> failtestf "%s: lenient and repair-then-decode disagree: %A / %A" c.Id a b

                      Expect.equal outcome.Recovered applied (sprintf "%s: Recovered names what repair applied" c.Id)
                  | Repair.RepairOutcome.NotRepairable _ ->
                      Expect.isError outcome.Result (sprintf "%s: not repairable, so not decodable" c.Id)
                      Expect.isEmpty outcome.Recovered c.Id
          }

          test "the strict default never names a repair" {
              for c in fam.Cases do
                  let outcome =
                      JsonDecode.decodeNodeWithOutcome DecodePolicy.admitAll (readText fam.Root c.InputFile)

                  Expect.isEmpty outcome.Recovered (sprintf "%s: the default decoder repaired nothing" c.Id)
          }

          yield! perCase ]
