module Fuaran.UI.JsonDecode.Tests.StoredEmission

// ============================================================================
//  Phase 1910 — the stored-emission sample: the reference host's leg.
//
//  `wire-format-fixtures/stored-emissions/` is a fixed sample of REAL model
//  emissions with the answer every conformant node decoder gives each one. The
//  authored families probe what they were written to probe; none reaches the
//  defects models actually produce, which is where two hosts part company
//  unnoticed. The TypeScript host certifies the same declaration from its own
//  suite, so a change to either decoder that moves its answer on a real emission
//  reddens that host's gate.
//
//  Measured over the whole stored evaluation corpus (12,707 unique emissions),
//  this host's strict decoder and the TypeScript host's accept exactly the same
//  set. The one-time disagreement was this host's decode-time recovery, and
//  Phase 1923 ruled it out of decode: decode is strict, and repair is the
//  separate `JsonDecode.repair` (WIRE_FORMAT.md §28).
//
//  So this leg asserts, per fixture:
//
//    * the DEFAULT (strict) decoder gives the declared verdict, and names no
//      repair;
//    * `repair` returns the declared outcome — the applied ids, or the refusal
//      token — and the strict decode of what it returns is the declared
//      `repairedVerdict`;
//    * the opt-in `Recovery.Lenient` decoder agrees with repair-then-decode, so
//      the transitional path cannot drift from the function it is defined as.
//
//  Counter-sensitive (the lenient decode records `Reliance`), so the list runs
//  sequenced beside the other recovery suites.
// ============================================================================

open System.IO
open System.Text.Json
open Expecto
open Fuaran.UI.KindPolicy
open Fuaran.UI.Ops

type private StoredEmission =
    { Id: string
      InputFile: string
      Verdict: string
      Code: string option
      Path: string option
      RepairOutcome: string
      Applied: string list
      Reason: string option
      RepairedVerdict: string option }

/// A JSON string's value, refusing null rather than letting it through as a string.
let private text (v: JsonElement) : string =
    match v.GetString() with
    | null -> failwith "stored-emissions/manifest.json: a string member is null"
    | s -> s

let private optStr (el: JsonElement) (name: string) : string option =
    match el.TryGetProperty name with
    | true, v when v.ValueKind = JsonValueKind.String -> Some(text v)
    | _ -> None

let private familyDir () =
    Path.Combine(Fuaran.Tests.CorpusRoot.find (), "stored-emissions")

/// A declared decode answer: the string `"accept"`, or `{code, path}`.
let private verdictOf (v: JsonElement) : string =
    match v.ValueKind with
    | JsonValueKind.String -> if text v = "accept" then "accepted" else text v
    | _ -> sprintf "%s at %s" (text (v.GetProperty "code")) (text (v.GetProperty "path"))

let private load () : string * StoredEmission list =
    let root = Fuaran.Tests.CorpusRoot.find ()

    use doc =
        JsonDocument.Parse(File.ReadAllText(Path.Combine(familyDir (), "manifest.json")))

    let kind = text (doc.RootElement.GetProperty("kind"))

    if kind <> "stored-emission-sample" then
        failwithf "stored-emissions/manifest.json declares kind '%s'" kind

    let fixtures =
        [ for el in doc.RootElement.GetProperty("fixtures").EnumerateArray() do
              { Id = text (el.GetProperty("id"))
                InputFile = text (el.GetProperty("inputFile"))
                Verdict = text (el.GetProperty("verdict"))
                Code = optStr el "expectedErrorCode"
                Path = optStr el "expectedPath"
                RepairOutcome = text (el.GetProperty("repair").GetProperty("outcome"))
                Applied =
                  match el.GetProperty("repair").TryGetProperty "applied" with
                  | true, a -> [ for x in a.EnumerateArray() -> text x ]
                  | _ -> []
                Reason = optStr (el.GetProperty "repair") "reason"
                RepairedVerdict =
                  match el.GetProperty("repair").TryGetProperty "repairedVerdict" with
                  | true, v -> Some(verdictOf v)
                  | _ -> None } ]

    root, fixtures

let private lenient =
    DecodePolicy.admitAll |> DecodePolicy.withRecovery Recovery.Lenient

let private answer (r: Result<_, JsonDecode.DecodeError>) : string =
    match r with
    | Ok _ -> "accepted"
    | Error e -> sprintf "%s at %s" e.Code e.Path

let private expected (fx: StoredEmission) : string =
    match fx.Verdict, fx.Code, fx.Path with
    | "accept", _, _ -> "accepted"
    | "reject", Some code, Some path -> sprintf "%s at %s" code path
    | v, _, _ -> failwithf "%s: verdict '%s' without a code and path" fx.Id v

[<Tests>]
let tests =
    let root, fixtures = load ()

    let perFixture =
        [ for fx in fixtures do
              test (sprintf "%s: %s" fx.Id (expected fx)) {
                  let text =
                      File.ReadAllText(Path.Combine(root, fx.InputFile.Replace('/', Path.DirectorySeparatorChar)))

                  let outcome = JsonDecode.decodeNodeWithOutcome DecodePolicy.admitAll text
                  Expect.equal (answer outcome.Result) (expected fx) "the strict default decoder: the declared verdict"
                  Expect.isEmpty outcome.Recovered "the default decoder repaired nothing"

                  let lenientOutcome = JsonDecode.decodeNodeWithOutcome lenient text

                  match JsonDecode.repair text, fx.RepairOutcome with
                  | Repair.RepairOutcome.Repaired(repaired, applied), "repaired" ->
                      Expect.equal applied fx.Applied "repair applies exactly the declared ids"

                      Expect.equal
                          (Some(answer (JsonDecode.decodeNode repaired)))
                          fx.RepairedVerdict
                          "the strict decode of the repaired text"

                      Expect.equal
                          (answer lenientOutcome.Result)
                          (answer (JsonDecode.decodeNode repaired))
                          "Lenient = repair, then strict decode"

                      Expect.equal lenientOutcome.Recovered applied "and names what repair applied"
                  | Repair.RepairOutcome.NotRepairable reason, "not-repairable" ->
                      Expect.equal (Some reason) fx.Reason "the declared refusal token"

                      Expect.equal
                          (answer lenientOutcome.Result)
                          (expected fx)
                          "Lenient cannot decode what repair refuses"
                  | got, want -> failtestf "declared %s, repair returned %A" want got
              } ]

    testSequenced
    <| testList
        "Fuaran.UI.Ops.JsonDecode — the stored-emission sample (Phase 1910)"
        [ test "the sample is present, not empty, and carries both verdicts" {
              // A sample that shrank to nothing would certify nothing and stay green.
              Expect.isGreaterThanOrEqual fixtures.Length 30 "at least 30 sampled emissions"
              Expect.exists fixtures (fun f -> f.Verdict = "accept") "an accept"
              Expect.exists fixtures (fun f -> f.Verdict = "reject") "a reject"
          }

          test "every emission file in the family directory is declared, and nothing else" {
              let onDisk =
                  Directory.GetFiles(familyDir ())
                  |> Array.map Path.GetFileName
                  |> Array.filter (fun f -> f <> "manifest.json" && f <> "README.md")
                  |> Array.sort
                  |> List.ofArray

              let declared =
                  fixtures
                  |> List.map (fun f -> f.InputFile.Split('/') |> Array.last)
                  |> List.sort

              Expect.equal onDisk declared "the directory holds exactly the declared emissions"
          }

          test "the repair class is sampled, by both catalogue ids" {
              for id in Repair.RepairId.catalogue do
                  Expect.exists fixtures (fun f -> f.Applied = [ id ]) (sprintf "a fixture repaired by %s" id)
          }

          yield! perFixture ]
