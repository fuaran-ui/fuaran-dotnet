module Fuaran.UI.JsonDecode.Tests.SpecProjectionTests

// ============================================================================
//  The drift guard for WIRE_FORMAT.md's marker-block projections (Phase 699).
//
//  Same posture as the stale-schema guard beside it: the generated surface and
//  the committed document must agree, and the failure message names the command
//  that reconciles them. What it guards is the class the schema guard cannot see
//  — the spec's own mechanical PROSE, which no byte-parity leg reads and which
//  had drifted on every one of its five surfaces before this phase.
//
//  Three assertions, and the second and third are what stop the first from
//  passing vacuously:
//    1. No drift — every managed block matches what idl.json + manifest.json
//       currently say.
//    2. Every managed block is present and unique, every annotation key names a
//       live subject, every count selector names a real manifest kind. These are
//       structural DEFECTS: `reconcile` raises rather than reporting them,
//       because `--project-spec` cannot fix a marker that is not there and a
//       silent pass would leave a table unmanaged while looking guarded.
//    3. The generated bodies are non-trivial — a projection that emitted nothing
//       would satisfy (1) against a document from which the tables had been
//       deleted, forever.
// ============================================================================

open Expecto

let private corpusRoot = Corpus.findRoot ()

[<Literal>]
let private regenCommand =
    "dotnet run --project src/Fuaran.UI.JsonDecode.Tests -- --project-spec <corpus-dir>"

[<Tests>]
let tests =
    testList
        "WIRE_FORMAT.md marker-block projection (Phase 699)"
        [ testCase "every managed block is present, unique, and fully annotated"
          <| fun () ->
              // `reconcile` raises ProjectionDefect on a missing/duplicated
              // block, an orphaned annotation key, or an unknown count kind.
              // Letting it propagate would report the exception type; catching
              // it lets the message say what to do about it.
              try
                  SpecProjection.reconcile corpusRoot |> ignore
              with SpecProjection.ProjectionDefect messages ->
                  failtestf
                      "The WIRE_FORMAT.md projection contract is broken:\n  - %s"
                      (String.concat "\n  - " messages)

          testCase "the committed spec matches the projection"
          <| fun () ->
              match SpecProjection.check corpusRoot with
              | [] -> ()
              | drift ->
                  failtestf
                      "WIRE_FORMAT.md has drifted from its generated sources (idl.json / manifest.json):\n  - %s\n\nRegenerate with:\n  %s"
                      (String.concat "\n  - " drift)
                      regenCommand

          testCase "the projection carries real content"
          <| fun () ->
              let _, rebuilt, _ = SpecProjection.reconcile corpusRoot
              // A vacuous projection (empty bodies) would agree with a document
              // whose tables had been emptied, and keep agreeing forever. Pin
              // that the emitted document still names the vocabulary.
              for token in [ "`Mount`"; "`Switch`"; "`Icon`"; "ToneVariant"; "Identity default" ] do
                  Expect.stringContains
                      rebuilt
                      token
                      (sprintf
                          "the reconciled WIRE_FORMAT.md no longer mentions %s — the projection emitted nothing"
                          token) ]
