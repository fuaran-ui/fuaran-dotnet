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
//  Output discipline as everywhere in this harness: ASCII counts and the
//  cases' ASCII names, never the strings themselves.
// ============================================================================

module Sanitize = Fuaran.UI.Renderer.Sanitize
module Cases = Fuaran.UI.Tests.SanitizeCases

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
