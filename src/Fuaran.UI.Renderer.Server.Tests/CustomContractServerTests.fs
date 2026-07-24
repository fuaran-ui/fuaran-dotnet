module Fuaran.UI.Renderer.Server.Tests.CustomContract

// ============================================================================
//  Phase 164 — server Custom-contract registration (HTML-string assertions).
//
//  The server renderer emits an HTML string on plain .NET, so unlike the client
//  pipeline (whose ReactElement is opaque) the decode-FAILURE placeholder is
//  directly assertable here: a tampered prop bag must render the labelled
//  placeholder NAMING the failing key, not a blank box. One contract value
//  drives the server registration, the decode, AND the recorded hash.
// ============================================================================

open Expecto
open Feliz.ViewEngine
open Fuaran.UI
open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Renderer.Server

let private contains (needle: string) (haystack: string) =
    haystack.Contains(needle, System.StringComparison.Ordinal)

type private Spark = { Points: string; Label: string }

let private encode (p: Spark) : Map<string, JVal> =
    Map.ofList [ "points", JStr p.Points; "label", JStr p.Label ]

let private decode (bag: Map<string, JVal>) : Result<Spark, CustomDecodeError> =
    match Map.tryFind "points" bag, Map.tryFind "label" bag with
    | Some(JStr pts), Some(JStr lbl) -> Ok { Points = pts; Label = lbl }
    | Some _, Some _ -> Error(CustomDecodeError.forKey "points" "expected string values")
    | None, _ -> Error(CustomDecodeError.forKey "points" "missing required key")
    | _, None -> Error(CustomDecodeError.forKey "label" "missing required key")

let private sample = { Points = "0,1 2,3"; Label = "trend" }

let private contract =
    CustomContract.create "sample" "sparkline" encode decode sample [] HashStrictness.StrictReplay

// The host render fn receives the typed props already decoded by the contract.
let private render (p: Spark) : ReactElement =
    Html.div [ prop.className "sample-sparkline"; prop.text p.Label ]

let private reg = Registry.empty |> Registry.registerContract contract render

let private renderBag (bag: Map<string, JVal>) : string =
    match Registry.tryRender contract.ModuleId contract.ComponentId reg with
    | Some closure -> Feliz.ViewEngine.Render.htmlView (closure bag)
    | None -> failtest "the contract renderer must be registered"

[<Tests>]
let tests =
    testList
        "Phase 164 — server Custom-contract registration"
        [ test "registerContract renders the decoded payload on the Ok path" {
              let html = renderBag (encode sample)
              Expect.isTrue (contains "sample-sparkline" html) "the host render fn ran on the decoded props"
              Expect.isTrue (contains "trend" html) "the decoded label reached the DOM"
          }

          test "a tampered prop bag renders the labelled decode-error placeholder (not a blank box)" {
              let tampered = encode sample |> Map.remove "points"
              let html = renderBag tampered
              Expect.isTrue (contains "fuaran-custom-decode-error" html) "decode-error placeholder class present"

              Expect.isTrue
                  (contains "data-fuaran-custom-decode-error=\"points\"" html)
                  "the failing key is named on the placeholder element"

              Expect.isTrue (contains "decode error (key" html) "the human-readable placeholder text names the failure"
          }

          test "registerContract records the contract's derived hash for bounded-escape verification" {
              match Registry.tryHash contract.ModuleId contract.ComponentId reg with
              | Some h -> Expect.equal h contract.Hash.Hash "the server records the contract-derived hash"
              | None -> failtest "expected the contract hash to be recorded on the server registry"
          } ]
