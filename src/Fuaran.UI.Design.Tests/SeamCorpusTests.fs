module Fuaran.UI.Design.Tests.SeamCorpusTests

// Phase 2295 — the design-provider seam certified against the corpus (`design/`,
// DESIGN_SEAM.md §7). Every `design-seam-round-trip` row decodes with its named entry point and
// re-encodes byte-identically to its expectedFile; every `design-seam-reject` row refuses with
// exactly its code at exactly its path; and the family on disk is exactly the family the manifest
// enumerates, so a vector removed from the manifest (or a file left behind without a row) goes
// red. Reads the corpus through the one resolver every corpus-reading suite uses, and fails
// rather than skips when it is absent (`requiresCorpus` in test-suites.json).

open System.IO
open System.Text.Json
open Expecto
open Fuaran.UI.Design.SeamCodec

type private Row =
    { Id: string
      Kind: string
      Decoder: string
      InputFile: string
      ExpectedFile: string option
      ExpectedErrorCode: string option
      ExpectedPath: string option }

let private roundTripKind = "design-seam-round-trip"
let private rejectKind = "design-seam-reject"
let private familyDirectory = "design"

let private corpusRoot () = Fuaran.Tests.CorpusRoot.find ()

let private rows () : Row list =
    let root = corpusRoot ()
    use doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")))

    let str (row: JsonElement) (name: string) =
        match row.TryGetProperty name with
        | true, v when v.ValueKind = JsonValueKind.String -> v.GetString() |> Option.ofObj
        | _ -> None

    doc.RootElement.GetProperty("fixtures").EnumerateArray()
    |> Seq.filter (fun r -> str r "kind" = Some roundTripKind || str r "kind" = Some rejectKind)
    |> Seq.map (fun r ->
        let required name =
            str r name
            |> Option.defaultWith (fun () -> failwithf "a design-seam row lacks '%s'" name)

        { Id = required "id"
          Kind = required "kind"
          Decoder = required "decoder"
          InputFile = required "inputFile"
          ExpectedFile = str r "expectedFile"
          ExpectedErrorCode = str r "expectedErrorCode"
          ExpectedPath = str r "expectedPath" })
    |> List.ofSeq

let private read (relative: string) =
    File.ReadAllText(Path.Combine(corpusRoot (), relative))

let private certify (row: Row) =
    let input = read row.InputFile

    match reencode row.Decoder input, row.Kind with
    | None, _ -> failtestf "%s names decoder '%s', which the codec does not have" row.Id row.Decoder
    | Some(Ok bytes), k when k = roundTripKind ->
        let expected =
            row.ExpectedFile
            |> Option.defaultWith (fun () -> failtestf "%s is a round-trip row with no expectedFile" row.Id)
            |> read

        Expect.equal bytes expected $"%s{row.Id} re-encodes byte-identically to its expectedFile"
    | Some(Error e), k when k = roundTripKind ->
        failtestf
            "%s should round-trip, but was refused: %s at %s (%s)"
            row.Id
            (SeamErrorCode.name e.Code)
            e.Path
            e.Message
    | Some(Error e), _ ->
        Expect.equal (Some(SeamErrorCode.name e.Code)) row.ExpectedErrorCode $"%s{row.Id} refuses with its named code"
        Expect.equal (Some e.Path) row.ExpectedPath $"%s{row.Id} refuses at its named path"
    | Some(Ok _), _ -> failtestf "%s should be refused with %A, but decoded" row.Id row.ExpectedErrorCode

[<Tests>]
let tests =
    testList
        "design seam corpus"
        [ test "every vector in the family certifies" {
              let rows = rows ()
              Expect.isNonEmpty rows "the manifest enumerates the design-seam family"

              for row in rows do
                  certify row
          }

          test "the family on disk is exactly the family the manifest enumerates" {
              let root = corpusRoot ()

              let onDisk =
                  Directory.GetFiles(Path.Combine(root, familyDirectory), "*.json")
                  |> Array.map (fun f -> familyDirectory + "/" + Path.GetFileName f)
                  |> Set.ofArray

              let named =
                  rows ()
                  |> List.collect (fun r -> r.InputFile :: Option.toList r.ExpectedFile)
                  |> Set.ofList

              Expect.equal (Set.difference onDisk named) Set.empty "no file in design/ lacks a manifest row"
              Expect.equal (Set.difference named onDisk) Set.empty "no manifest row names a missing file"
          }

          test "the family covers every decoder, a round-trip and a refusal each where one applies" {
              let rows = rows ()

              for decoder in decoders do
                  Expect.isTrue
                      (rows |> List.exists (fun r -> r.Decoder = decoder && r.Kind = roundTripKind))
                      $"%s{decoder} has a round-trip vector"

                  Expect.isTrue
                      (rows |> List.exists (fun r -> r.Decoder = decoder && r.Kind = rejectKind))
                      $"%s{decoder} has a reject vector"

              for code in
                  [ "DESIGN_MISSING_MEMBER"
                    "DESIGN_UNKNOWN_INTERACTION"
                    "DESIGN_SYSTEM_MISMATCH" ] do
                  Expect.isTrue
                      (rows |> List.exists (fun r -> r.ExpectedErrorCode = Some code))
                      $"a vector is refused as %s{code}"
          } ]
