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

/// Node rejects whose refusal is produced by a `Fuaran.Core` codec and
/// surfaced through the host's cross-pillar `coreError` wrap — `WRONG_TYPE`
/// with NO `ExpectedShape` (Core's `ColumnError` carries no UI-layer shape
/// hint; the wrap deliberately does not invent one). Named, never counted —
/// the same posture as `SchemaConformance.schemaInexpressibleRejects`.
///
/// - `reject-transform-source-empty-wrapper` (fuaran#815 / Phase 822): the
///   un-unwrappable State wrapper reaches Core's columnar codec verbatim, so
///   the refusal is Core's, not this decoder's.
///
/// Each entry is asserted hint-LESS below — the inverse pin. If the wrap ever
/// gains a recovery hint, this fails and the list shrinks deliberately rather
/// than the exemption quietly outliving its reason.
let private coreWrappedHintlessRejects: Set<string> =
    set [ "reject-transform-source-empty-wrapper" ]

let private checkError
    (e: Corpus.FixtureEntry)
    (expectedCode: string)
    (expectedPath: string)
    (err: DecodeError)
    : unit =
    Expect.equal err.Code expectedCode (sprintf "Code matches (path was '%s')" err.Path)

    Expect.isTrue (err.Path.StartsWith expectedPath) (sprintf "Path '%s' starts with '%s'" err.Path expectedPath)

    // Phase 1073 — the ruled bare-enum reject-path spelling, pinned.
    //
    // The prefix assertion above cannot catch a spurious `.$type`: a host that
    // reports `$.style.tone.$type` where the corpus says `$.style.tone` passes it,
    // and three hosts did exactly that for the corpus's whole life. Prefix matching
    // stays (see below), so this is the guard that makes the ruling enforceable.
    //
    // WIRE_FORMAT.md §6: `$type` appears in a path only when the DISCRIMINATOR is at
    // fault. A bare enum carries no discriminator on the wire, so a `.$type` suffix
    // there names a JSON member the document does not contain and an author cannot
    // repair at. The corpus already distinguishes the two populations correctly, so
    // the corpus's own expectation is the oracle: the emitted path may only end in
    // `.$type` where the fixture's `expectedPath` does.
    //
    // Deliberately NOT an equality assertion over the whole family. 67 of the 73
    // reject fixtures match exactly, and the 6 that do not are legitimate: four
    // `reject-binding-*` cases where the corpus records the author-facing SLOT
    // (`$.kind.trend`) and the decoder reports the wrong-typed position inside it
    // (`$.kind.trend.value`), and the two `reject-limit-*-depth` cases where §21
    // explicitly licenses naming the position at which the limit was breached while
    // the corpus records `$` for the whole refused document. A host that is MORE
    // precise than the corpus's stated slot is over-specifying, not diverging — the
    // defect this phase found was a suffix naming a position that does not exist at
    // all, which is a different thing and is what this pins.
    if not (expectedPath.EndsWith ".$type") then
        Expect.isFalse
            (err.Path.EndsWith ".$type")
            (sprintf
                "reject fixture %s: corpus expects '%s' (a bare-enum position, no discriminator on the wire) but the decoder reported '%s'. A `.$type` suffix here names a JSON member the document does not contain — see WIRE_FORMAT.md §6 and use `unknownEnumCase`, not `unknownDuCase`."
                e.Id
                expectedPath
                err.Path)

    // Node-side rejects carry an expected-shape recovery hint (the
    // decoder-quality invariant); op-side rejects assert only Code + Path.
    // Core-wrapped refusals are the named exception (inverse-pinned above).
    if e.Decoder = "node" then
        if coreWrappedHintlessRejects.Contains e.Id then
            Expect.isNone err.ExpectedShape "core-wrapped reject stays hint-less (inverse pin)"
        else
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
