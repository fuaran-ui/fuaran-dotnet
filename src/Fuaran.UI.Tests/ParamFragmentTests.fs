module Fuaran.UI.Tests.ParamFragmentTests

open Expecto
open Fuaran.UI
open Fuaran.UI.Types

// ============================================================================
//  Phase 180 — the artifact-function core (Fuaran.UI.Fragment): hole
//  declaration, derivable signature, value-space validation, partial
//  application (curry), totality, and the two-axis effect-class join.
// ============================================================================

/// Non-null box (F# 10 nullness: `box` yields `obj | null`; the hole API wants
/// a non-null `obj`).
let private bx (v: 'a) : obj = box v |> Unchecked.nonNull

let private body: Node<unit> = Fuaran.markdown "tpl" "template body"

let private pf: ParamFragment<unit> =
    { Name = FragmentId "card"
      Holes =
        [ HoleDecl.Value("title", HoleValueSpace.StringLen(1, 40), None)
          HoleDecl.Value("tone", HoleValueSpace.Enum [ "info"; "warn" ], Some(bx "info"))
          HoleDecl.Slot("content", None)
          HoleDecl.Repeat("rows", HoleValueSpace.IntRange(1, 10)) ]
      Body = body
      Effect = EffectClass.pureDeterministic }

[<Tests>]
let tests =
    testList
        "Fragment.ArtifactFunction"
        [ test "effect-class join is the componentwise widest (pure ∘ impure = impure)" {
              let impure =
                  { HostEffect = HostEffect.WritesHost
                    Determinism = DeterminismSource.Clock }

              let joined = EffectClass.join EffectClass.pureDeterministic impure
              Expect.equal joined.HostEffect HostEffect.WritesHost "host effect widens"
              Expect.equal joined.Determinism DeterminismSource.Clock "determinism widens"
              // commutative
              Expect.equal (EffectClass.join impure EffectClass.pureDeterministic) joined "join is commutative"
          }

          test "effect-class covers: a declared class narrower than actual is flagged" {
              let actual =
                  { HostEffect = HostEffect.ReadsHost
                    Determinism = DeterminismSource.Deterministic }

              Expect.isFalse (EffectClass.covers EffectClass.pureDeterministic actual) "pure cannot cover ReadsHost"
              Expect.isTrue (EffectClass.covers actual EffectClass.pureDeterministic) "ReadsHost covers pure"
          }

          test "derivable signature enumerates holes + value-spaces + required flags" {
              let sg = Fragment.signature pf
              Expect.equal sg.Name "card" "name"
              Expect.equal (List.length sg.Holes) 4 "four holes"
              let title = sg.Holes |> List.find (fun h -> h.Name = "title")
              Expect.equal title.Kind "value" "title is a value hole"
              Expect.isTrue title.Required "title has no default -> required"
              let tone = sg.Holes |> List.find (fun h -> h.Name = "tone")
              Expect.isFalse tone.Required "tone has a default -> optional"
              let content = sg.Holes |> List.find (fun h -> h.Name = "content")
              Expect.equal content.Kind "slot" "content is a slot"
          }

          test "required holes are the no-default ones (+ slots + repeats)" {
              let req = Fragment.requiredHoles pf |> Set.ofList
              Expect.equal req (Set.ofList [ "title"; "content"; "rows" ]) "tone (defaulted) is not required"
          }

          test "value-space validation: in-range ok, out-of-range + enum rejected" {
              Expect.isOk (Fragment.validateValueArg pf "title" (bx "Hello")) "in-length string ok"
              Expect.isError (Fragment.validateValueArg pf "title" (bx "")) "empty string rejected (min 1)"
              Expect.isOk (Fragment.validateValueArg pf "tone" (bx "warn")) "enum member ok"
              Expect.isError (Fragment.validateValueArg pf "tone" (bx "danger")) "non-enum rejected"
              Expect.isError (Fragment.validateValueArg pf "content" (bx "x")) "slot is not a value"
          }

          test "totality: a bounded Repeat is total; an unbounded one is refused" {
              Expect.isTrue (Fragment.isTotal pf) "IntRange repeat count is total"

              let bad =
                  { pf with
                      Holes = [ HoleDecl.Repeat("rows", HoleValueSpace.AnyString) ] }

              Expect.isFalse (Fragment.isTotal bad) "AnyString repeat count is unbounded -> not total"
          }

          test "partial application (curry) binds a subset, narrows the fragment" {
              match Fragment.curry pf (Map.ofList [ "title", bx "Fixed" ]) with
              | Ok narrower ->
                  // The bound hole becomes optional (its value is now the default).
                  let req = Fragment.requiredHoles narrower |> Set.ofList
                  Expect.isFalse (req.Contains "title") "title is now bound (no longer required)"
                  Expect.isTrue (req.Contains "content") "content remains open"
              | Error e -> failtestf "curry should succeed: %s" e
          }

          test "curry rejects a value-space violation and a non-value hole" {
              Expect.isError (Fragment.curry pf (Map.ofList [ "title", bx "" ])) "empty title violates StringLen"
              Expect.isError (Fragment.curry pf (Map.ofList [ "content", bx "x" ])) "content is a slot, not a value"
              Expect.isError (Fragment.curry pf (Map.ofList [ "nope", bx 1 ])) "unknown hole rejected"
          }

          test "a zero-hole fragment is the degenerate fixed-body case" {
              let fixedBody = { pf with Holes = [] }
              Expect.isEmpty (Fragment.requiredHoles fixedBody) "no holes -> no required args"
              Expect.isTrue (Fragment.isTotal fixedBody) "trivially total"
          } ]
