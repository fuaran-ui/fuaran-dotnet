module FableLaws.SanitizeLaw

// ============================================================================
//  Law 11 (Phase 2010): the renderer's markdown floor gives the same answer on
//  both pipelines over the cases `Fuaran.UI.Tests/SanitizeCases.fs` pins.
//
//  The defect this law exists for shows on ONE pipeline only. Under Fable a
//  string lowercase is `toLowerCase()`, which turns U+0130 into two units, so a
//  scan index taken from a lowered copy and applied to the original lands one
//  position early per such character (Core fixed its own copy of the floor as
//  fuaran-core#291). The .NET suite runs the same cases and stayed green while
//  the browser's floor did not, which is why the cases are linked here rather
//  than restated.
//
//  `sanitizeKitLaw` below is the second port of the census row for
//  `Conformance.sanitizeLawsAt`: the pinned kit's family over the same floor.
//
//  Output discipline as everywhere in this harness: ASCII counts and the
//  cases' ASCII names, never the strings themselves.
// ============================================================================

module Sanitize = Fuaran.UI.Renderer.Sanitize
module Cases = Fuaran.UI.Tests.SanitizeCases
module CoreConf = Fuaran.Core.Conformance

type SanitizeLaw = { Lines: string list; Violations: int }

let sanitizeLaw () : SanitizeLaw =
    let refutations =
        [ for name, input, expected in Cases.literalCases do
              let reason =
                  try
                      let out = Sanitize.sanitizeMarkdownHtml input

                      if out <> expected then
                          Some "output differs from the pinned answer"
                      else
                          match Cases.floorBreach out with
                          | Some breach -> Some breach
                          | None ->
                              if Sanitize.sanitizeMarkdownHtml out <> out then
                                  Some "not idempotent"
                              else
                                  None
                  with _ ->
                      Some "threw"

              match reason with
              | Some r -> yield "  refuted: " + name + ": " + r
              | None -> () ]

    let header =
        "SANITIZE markdown floor cases="
        + string (List.length Cases.literalCases)
        + " failed="
        + string (List.length refutations)

    { Lines = header :: refutations
      Violations = List.length refutations }

/// The census enrolment name for the FABLE port of `Conformance.sanitizeLawsAt` -
/// `CoreConformanceCensus.fs` carries it as the row's second port, and the line below prints it.
let sanitizeFablePort =
    "sanitizeLawsAt certifies through the Fable law harness over the renderer's floor"

/// The kit's sanitisation family over the renderer's floor, on this pipeline: the same recording
/// witness and the same judgement the .NET port runs (`SanitizeCases.judge`), with the stated limit
/// that file's header gives. Counts only, so the two pipelines' lines byte-compare.
let sanitizeKitLaw (seed: int) (iterations: int) : SanitizeLaw =
    let witness, recorder = Cases.recordingWitness ()
    let verdict = Cases.judge (CoreConf.sanitizeLawsAt witness seed iterations) recorder

    let header =
        "KIT "
        + sanitizeFablePort
        + " laws="
        + string verdict.Laws
        + " held="
        + string verdict.Held
        + " statedLimit="
        + string verdict.StatedLimit
        + " inputs="
        + string verdict.Inputs
        + " refuted="
        + string (List.length verdict.Refutations)

    { Lines = header :: (verdict.Refutations |> List.map (fun r -> "  refuted: " + r))
      Violations = List.length verdict.Refutations }
