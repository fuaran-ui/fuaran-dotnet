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
//  this host with `Recovery.Off` and the TypeScript host accept exactly the same
//  set. The only disagreement is this host's default recovery (fuaran#850's
//  implied node close, fuaran#855's uniqueness-gated over-close), which the
//  specification does not describe — an open specification question, recorded
//  in the family's `openQuestions`, not a defect of either host.
//
//  So this leg asserts two things per fixture:
//
//    * with `Recovery.Off`, the declared verdict (the cross-host answer);
//    * with the DEFAULT policy, the declared verdict too, unless the fixture
//      names a `referenceRecovery` — in which case the default decoder accepts
//      it by exactly that recovery and no other. A real emission this host
//      starts repairing without a declaration is a new divergence, and it goes
//      red here rather than surfacing in the next evaluation run.
//
//  Counter-sensitive (the default decode records `Reliance`), so the list runs
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
      Recovery: string option }

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

let private load () : string * StoredEmission list * string list =
    let root = Fuaran.Tests.CorpusRoot.find ()

    use doc =
        JsonDocument.Parse(File.ReadAllText(Path.Combine(familyDir (), "manifest.json")))

    let kind = text (doc.RootElement.GetProperty("kind"))

    if kind <> "stored-emission-sample" then
        failwithf "stored-emissions/manifest.json declares kind '%s'" kind

    let recoveries =
        [ for q in doc.RootElement.GetProperty("openQuestions").EnumerateArray() do
              for r in q.GetProperty("recoveries").EnumerateArray() do
                  text r ]

    let fixtures =
        [ for el in doc.RootElement.GetProperty("fixtures").EnumerateArray() do
              { Id = text (el.GetProperty("id"))
                InputFile = text (el.GetProperty("inputFile"))
                Verdict = text (el.GetProperty("verdict"))
                Code = optStr el "expectedErrorCode"
                Path = optStr el "expectedPath"
                Recovery = optStr el "referenceRecovery" } ]

    root, fixtures, recoveries

let private strict = DecodePolicy.admitAll |> DecodePolicy.withRecovery Recovery.Off

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
    let root, fixtures, recoveries = load ()

    let perFixture =
        [ for fx in fixtures do
              test (sprintf "%s: %s" fx.Id (expected fx)) {
                  let text =
                      File.ReadAllText(Path.Combine(root, fx.InputFile.Replace('/', Path.DirectorySeparatorChar)))

                  Expect.equal
                      (answer (JsonDecode.decodeNodeWithPolicy strict text))
                      (expected fx)
                      "with recovery off, the declared cross-host verdict"

                  let outcome = JsonDecode.decodeNodeWithOutcome DecodePolicy.admitAll text

                  match fx.Recovery with
                  | None ->
                      Expect.equal (answer outcome.Result) (expected fx) "the default decoder gives the same verdict"
                      Expect.isEmpty outcome.Recovered "the default decoder repaired nothing"
                  | Some recovery ->
                      Expect.equal
                          (answer outcome.Result)
                          "accepted"
                          "the default decoder accepts the declared recovery-class emission"

                      Expect.equal outcome.Recovered [ recovery ] "by exactly the declared recovery"
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

          test "every declared recovery is one the open question names" {
              let named = fixtures |> List.choose (fun f -> f.Recovery)
              Expect.isNonEmpty named "the recovery class is sampled"

              for r in named do
                  Expect.contains recoveries r "a recovery the open question lists"

              Expect.containsAll
                  recoveries
                  [ JsonDecode.Reliance.ImpliedNodeClose; JsonDecode.Reliance.OverCloseUnique ]
                  "the open question names this host's two recoveries by their Reliance ids"
          }

          yield! perFixture ]
