/// Phase 2517 — the renderer stamps the affordance (§31.8), and the
/// affordance-provider seam answers from the tree.
module Fuaran.UI.Operability.Tests.RendererTests

open Expecto
open Fuaran.UI.Renderer

let private ssr (json: string) =
    Fuaran.UI.Renderer.Server.Render.render BindingResolver.empty (Vectors.decode json)

[<Tests>]
let tests =
    testList
        "Phase 2517 — the rendered DOM carries the operable surface"
        [ test "a select that declares an affordance is annotated" {
              let html = ssr Cases.ticket.Json
              Expect.stringContains html "data-ai-name=\"country\"" "addressable by its name"
              Expect.stringContains html "data-fuaran-shape=\"choice\"" "its shape"
              Expect.stringContains html "data-fuaran-controllable=\"true\"" "settable"
              Expect.stringContains html "aria-description=\"Say:" "the spoken sentence"
              Expect.stringContains html "data-ai-name=\"reference\"" "a declaring form field"
              Expect.stringContains html "data-ai-name=\"submit\"" "a declaring button"
          }

          test "a node that declares nothing, or withdraws itself, is not annotated" {
              let html = ssr Cases.ticket.Json
              Expect.isFalse (html.Contains "data-ai-name=\"currency\"") "no affordance member: byte-unchanged"
              Expect.isFalse (html.Contains "data-ai-name=\"pin\"") "neverControllable: not addressable"
              Expect.isFalse (html.Contains "data-ai-name=\"delete\"") "neverControllable button"
          }

          test "the derived provider answers getAffordances from the tree" {
              Affordances.clearProviders ()

              let remove =
                  Affordances.registerDerivedProvider "ticket" (fun () -> Vectors.decode Cases.ticket.Json)

              try
                  let enumeration = Affordances.enumerate None
                  let m = enumeration.Modules |> List.exactlyOne
                  let country = m.Fields |> List.find (fun f -> f.Id = "country")
                  Expect.isTrue country.Controllable "settable"
                  Expect.equal country.Aliases [ "britain", "uk"; "america", "us" ] "aliases ride their options"
                  Expect.equal country.Values (Some(Affordances.ValueHint.OneOf [ "uk"; "fr"; "us" ])) "the option set"
                  Expect.isFalse (m.Fields |> List.exists (fun f -> f.Id = "pin")) "withdrawn fields are absent"

                  Expect.contains
                      (m.Commands |> List.map _.Phrase)
                      "place the order"
                      "a button is a module-level command"
              finally
                  remove ()
                  Affordances.clearProviders ()
          } ]
