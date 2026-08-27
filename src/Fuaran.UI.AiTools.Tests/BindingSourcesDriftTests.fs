module Fuaran.UI.AiTools.Tests.BindingSourcesDriftTests

// ============================================================================
//  Phase 213 — the drift guard.
//
//  `Fuaran.UI.AiTools` used to carry a HAND-DUPLICATED copy of the renderer's
//  binding-source record (`BindingProbeSources`), because AiTools must stay
//  free of `Fuaran.UI.Renderer`. The copy drifted: it never gained `Locale`
//  (Phase 102), `ComputedContext` (137), `CapabilityInvoker` (283), `Now`
//  (765) or `I18nResolver`. Nothing failed when it drifted — that is the whole
//  problem. Phase 213 promoted the record into `Fuaran.UI` (the package both
//  already depend on) so there is one field set; these tests are what makes a
//  re-split, or a field added on only one side, FAIL rather than pass quietly.
//
//  Both assertions are REFLECTIVE on purpose. A statically-typed comparison
//  would stop compiling under the regression it is meant to catch, and a build
//  error in an unrelated file is a much worse signal than a named test failure
//  that says exactly what diverged.
//
//  Note this TEST project references `Fuaran.UI.Renderer.Core` — the
//  emission-agnostic spine (FSharp.Core + Fable.Core, no Feliz / React). The
//  shipping `Fuaran.UI.AiTools` package still references no renderer at all;
//  that boundary is asserted below rather than assumed.
// ============================================================================

open System.Reflection
open Expecto
open Microsoft.FSharp.Reflection
open Fuaran.UI.AiTools.Seams

/// The canonical field set, pinned. Adding a field to
/// `Fuaran.UI/BindingSources.fs` is a deliberate act — it widens what every
/// host must furnish and what the introspection surface can see — so it lands
/// here in the same change, which is the moment to ask whether the probe's
/// declining arms in `BindingProbe.fs` should still decline.
let private canonicalFields =
    [ "QueryResults"
      "State"
      "Filters"
      "Selections"
      "ComputedContext"
      "I18n"
      "I18nResolver"
      "Locale"
      "CapabilityInvoker"
      "Now" ]

let private introspectionSourcesType =
    FSharpType.GetRecordFields(typeof<IntrospectionContext>)
    |> Array.find (fun (p: PropertyInfo) -> p.Name = "Sources")
    |> _.PropertyType

[<Tests>]
let bindingSourcesDriftTests =
    testList
        "Phase 213 — introspection / renderer BindingSources unification"
        [ test "the introspection source shape IS the renderer's, not a copy of it" {
              Expect.equal
                  introspectionSourcesType
                  typeof<Fuaran.UI.Renderer.BindingResolver.BindingSources>
                  "IntrospectionContext.Sources and the renderer resolver's BindingSources must be ONE type — re-declaring either as its own record reopens the drift Phase 213 closed"

              Expect.equal
                  introspectionSourcesType
                  typeof<Fuaran.UI.BindingSources>
                  "the one type is the canonical record promoted into Fuaran.UI (FSharp.Core only), not a renderer type AiTools reaches for"
          }

          test "the canonical record carries every field the renderer resolves against" {
              let actual =
                  FSharpType.GetRecordFields(typeof<Fuaran.UI.BindingSources>)
                  |> Array.map _.Name
                  |> List.ofArray

              Expect.equal
                  actual
                  canonicalFields
                  "the canonical BindingSources field set changed — update `canonicalFields` here in the same change, and re-read BindingProbe.fs's declining arms while you are there"

              // Named separately because this is the field that actually
              // drifted, and a diff on a ten-item list is easy to skim past.
              Expect.contains
                  actual
                  "Locale"
                  "`Locale` is the field the hand-duplicated probe record silently dropped (Phase 102 landed it renderer-side only)"
          }

          test "the empty introspection context IS the renderer's empty sources" {
              Expect.equal
                  (box emptyContext.Sources)
                  (box Fuaran.UI.Renderer.BindingResolver.empty)
                  "a second set of identity-defaults is the same drift class as a second record"
          }

          test "the shipping AiTools assembly still references no renderer" {
              // Acceptance criterion 3 of the phase. Asserted on the SHIPPING
              // assembly (`Fuaran.UI.AiTools`), never on this test assembly,
              // which references Renderer.Core deliberately.
              let referenced =
                  typeof<IntrospectionContext>.Assembly.GetReferencedAssemblies()
                  |> Array.map (fun a -> a.Name |> Option.ofObj |> Option.defaultValue "")
                  |> Array.filter _.StartsWith("Fuaran.UI.Renderer")

              Expect.isEmpty
                  referenced
                  "Fuaran.UI.AiTools must not reference any Fuaran.UI.Renderer* assembly — the shared shape lives in Fuaran.UI, which is what let the cycle break"
          } ]
