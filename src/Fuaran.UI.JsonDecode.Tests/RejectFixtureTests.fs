module Fuaran.UI.JsonDecode.Tests.RejectTests

// ============================================================================
//  Reject-fixture acceptance against the corpus.
//
//  Each reject fixture is a malformed wire payload in `wire-format-fixtures/
//  reject/<id>.json`; the manifest carries its `decoder` (node/op),
//  `expectedErrorCode`, and `expectedPath`. The suite decodes the payload via
//  the named entry point and asserts the structured `DecodeError` carries the
//  expected Code + a Path with the expected prefix — the same conformance
//  contract the Wave 9 TS decoder asserts, driven entirely off the corpus.
//
//  Source authoring lives in `RejectFixtures.fs` (the data table the corpus is
//  generated from); this file only consumes the emitted corpus.
// ============================================================================

open Expecto
open Fuaran.UI.Ops.JsonDecode

let private corpus = Corpus.load ()
let private corpusRoot = fst corpus

let private rejectEntries = snd corpus |> List.filter (fun e -> e.Kind = "reject")

let private checkError
    (e: Corpus.FixtureEntry)
    (expectedCode: string)
    (expectedPath: string)
    (err: DecodeError)
    : unit =
    Expect.equal err.Code expectedCode (sprintf "Code matches (path was '%s')" err.Path)

    Expect.isTrue (err.Path.StartsWith expectedPath) (sprintf "Path '%s' starts with '%s'" err.Path expectedPath)

    // Node-side rejects carry an expected-shape recovery hint (the
    // decoder-quality invariant); op-side rejects assert only Code + Path.
    if e.Decoder = "node" then
        Expect.isSome err.ExpectedShape "ExpectedShape hint populated"

let private rejectTest (e: Corpus.FixtureEntry) : Test =
    testCase (sprintf "reject — %s (%s)" e.Description e.Id) (fun () ->
        let input = Corpus.readPayload corpusRoot e.InputFile

        let expectedCode =
            match e.ExpectedErrorCode with
            | Some c -> c
            | None -> failtestf "reject fixture %s has no expectedErrorCode in manifest" e.Id

        let expectedPath =
            match e.ExpectedPath with
            | Some p -> p
            | None -> failtestf "reject fixture %s has no expectedPath in manifest" e.Id

        // Decode via the manifest-declared entry point; assert it rejected.
        match e.Decoder with
        | "op" ->
            match decodeOp input with
            | Ok _ -> failtestf "Expected decode to fail with %s; got Ok" expectedCode
            | Error err -> checkError e expectedCode expectedPath err
        | "node" ->
            match decodeNode input with
            | Ok _ -> failtestf "Expected decode to fail with %s; got Ok" expectedCode
            | Error err -> checkError e expectedCode expectedPath err
        | other -> failtestf "reject fixture %s has unknown decoder '%s'" e.Id other)

[<Tests>]
let rejectFixtures =
    rejectEntries
    |> List.map rejectTest
    |> testList "Fuaran.UI.Ops.JsonDecode — reject fixtures (corpus)"
