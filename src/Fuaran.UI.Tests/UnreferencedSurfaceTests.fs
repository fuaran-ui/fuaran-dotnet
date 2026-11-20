module Fuaran.UI.Tests.UnreferencedSurfaceTests

// ============================================================================
//  Phase 2100 — public functions that had no non-test caller, decided.
//
//  The two behaviours the decision found wrong, each fixed with a test:
//    - `Fragment.validateValueArg` checks a `Repeat` hole's count (its doc said
//      it did not) while `Fragment.curry` refuses to bind one; the doc now says
//      what the code does, and `curry`'s refusal names why.
//    - `WireSurvivability.steerable` included non-survivable cases with no
//      alternative to steer toward; it now holds exactly what its doc says.
//  And the one kept preset with no test: `validateWithRegistryAndPolicy`.
//  STABILITY.md (0.105.0, Phase 2100) records every disposition.
// ============================================================================

open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types

let private bx (v: 'a) : obj = box v |> Unchecked.nonNull

let private pf: ParamFragment<unit> =
    { Defaults.fragmentDecl with
        Name = "rows-card"
        Holes =
            [ HoleDecl.Value("title", HoleValueSpace.StringLen(1, 40), None)
              HoleDecl.Repeat("rows", HoleValueSpace.IntRange(1, 10)) ]
        Body = Fuaran.markdown "tpl" "body" }

[<Tests>]
let tests =
    testList
        "Unreferenced public surface (Phase 2100)"
        [ test "validateValueArg checks a Repeat hole's count against its count space" {
              Expect.isOk (Fragment.validateValueArg pf "rows" (bx 3)) "an in-range count is admitted"
              Expect.isError (Fragment.validateValueArg pf "rows" (bx 11)) "a count above the space is refused"
              Expect.isError (Fragment.validateValueArg pf "rows" (bx 0)) "a count below the space is refused"
          }

          test "curry refuses a Repeat hole, and says it is a repeat hole" {
              match Fragment.curry pf (Map.ofList [ "rows", bx 3 ]) with
              | Ok _ -> failtest "a repeat count has no default to curry into"
              | Error e ->
                  Expect.stringContains e "repeat hole" "the refusal names the hole's kind"
                  Expect.stringContains e "'rows'" "and the hole"

              match Fragment.curry pf (Map.ofList [ "missing", bx 1 ]) with
              | Ok _ -> failtest "an unknown hole is refused"
              | Error e -> Expect.stringContains e "no hole named 'missing'" "an unknown hole says so"
          }

          test "steerable is exactly the non-survivable cases that name an alternative" {
              let expected =
                  WireSurvivability.all
                  |> List.filter (fun c ->
                      c.Verdict <> WireSurvivability.Survivability.Survivable
                      && Option.isSome c.Alternative)

              Expect.equal WireSurvivability.steerable expected "the doc's set, in table order"

              Expect.isTrue
                  (WireSurvivability.steerable
                   |> List.forall (fun c -> Option.isSome c.Alternative))
                  "every steerable case has somewhere to steer to"

              // The case that exposed it: a partial case with no declarative twin.
              Expect.isFalse
                  (WireSurvivability.steerable
                   |> List.exists (fun c -> c.Case = "Action.ReadFileBody"))
                  "Action.ReadFileBody names no alternative, so it is not steerable"
          }

          test "validateWithRegistryAndPolicy is validateWith over both, and runs both" {
              let tree: Node<unit> = Fuaran.markdown "m" "text"

              let policy = Fuaran.UI.KindPolicy.DecodePolicy.admitting "box-only" [ "Box" ]

              let registry = CustomRegistry.Empty

              let both = PreEmitValidate.validateWithRegistryAndPolicy registry policy tree

              Expect.equal
                  both
                  (PreEmitValidate.validateWith
                      { PreEmitValidate.ValidateOptions.defaults with
                          Registry = Some registry
                          Policy = policy }
                      tree)
                  "the preset is its options"

              let codes =
                  match both with
                  | Ok() -> []
                  | Error ds ->
                      ds
                      |> List.map (fun d ->
                          let code, _, _ = PreEmitValidate.describe d
                          code)

              Expect.contains codes "FUARAN104" "the kind-admission lint ran under the policy"
          } ]
