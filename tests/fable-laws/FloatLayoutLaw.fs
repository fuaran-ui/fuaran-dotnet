module FableLaws.FloatLayoutLaw

// ============================================================================
//  Law 12 (Phase 2318): the AI wire and the SVG coordinate lay a float out
//  with the same bytes on both pipelines, over the cases
//  `Fuaran.UI.Tests/FloatLayoutCases.fs` pins.
//
//  The defect this law exists for shows under Fable only: `JsonHost` wrote
//  `string f`, which is JS's `1e-7` where .NET writes `1E-07`. Both surfaces
//  now go through `Fuaran.Core.FloatLayout.finite`, and the cases are linked
//  here rather than restated, so the .NET suite and this law judge the same
//  expected bytes.
//
//  Output discipline as everywhere in this harness: ASCII counts and the
//  cases' ASCII names, never the strings themselves.
// ============================================================================

module Cases = Fuaran.UI.Tests.FloatLayoutCases

type FloatLayoutLaw = { Lines: string list; Violations: int }

let floatLayoutLaw () : FloatLayoutLaw =
    let refutations = Cases.refutations ()

    let header =
        "FLOATLAYOUT ai-wire + svg cases="
        + string (List.length Cases.jsonNumberCases + List.length Cases.svgNumberCases)
        + " failed="
        + string (List.length refutations)

    { Lines = header :: (refutations |> List.map (fun r -> "  refuted: " + r))
      Violations = List.length refutations }
