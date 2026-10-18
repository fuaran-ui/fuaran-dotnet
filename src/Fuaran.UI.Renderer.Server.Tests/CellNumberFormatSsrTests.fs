module Fuaran.UI.Renderer.Server.Tests.CellNumberFormatSsrTests

// ============================================================================
//  Phase 1941 — a server-rendered figure does not depend on the server's
//  culture, and is the text the hydrating bundle writes.
//
//  The `CellFormat` projection used to read the CURRENT culture on .NET, so a
//  de-DE host server-rendered `1,50` where the bundle hydrated `1.50`, and its
//  `SignificantDigits` / default-`Number` arms wrote .NET's exponent forms
//  (`1.23E+03`, `1.23457e+06`) where the bundle writes `1230` and `1234567.5`.
//  Each case renders a `Metric` through the server renderer AND the email
//  projection, under en-US and under de-DE, and asserts the invariant,
//  JavaScript-identical text. The corpus's `formatting` family pins the same
//  projection value by value; this suite pins that the RENDERERS reach it
//  whatever culture the process runs under.
// ============================================================================

open System.Globalization
open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server
open Fuaran.UI.Ops

let private metric (format: string) (value: string) : Node<obj> =
    let json =
        sprintf
            """{"id":"m","kind":{"$type":"Metric","format":%s,"label":"Figure","value":{"$type":"Static","value":%s}}}"""
            format
            value

    match JsonDecode.decodeNodeObj json with
    | Ok node -> node
    | Error e -> failwithf "the Metric fixture does not decode: %A" e

/// Run `f` with the thread's current culture (and UI culture) set to `name`.
let private under (name: string) (f: unit -> 'T) : 'T =
    let culture = CultureInfo.CurrentCulture
    let uiCulture = CultureInfo.CurrentUICulture

    try
        CultureInfo.CurrentCulture <- CultureInfo name
        CultureInfo.CurrentUICulture <- CultureInfo name
        f ()
    finally
        CultureInfo.CurrentCulture <- culture
        CultureInfo.CurrentUICulture <- uiCulture

/// (case, format wire, value wire, the text the bundle writes)
let private cases =
    [ "fixed places", """{"$type":"Number","decimals":2}""", "1.5", "1.50"
      "default places, fraction", """{"$type":"Number"}""", "1234567.5", "1234567.5"
      "significant digits", """{"$type":"SignificantDigits","digits":3}""", "1234", "1230"
      "percent", """{"$type":"Percent","decimals":1}""", "0.125", "12.5%"
      "an exact tie", """{"$type":"Number","decimals":2}""", "0.125", "0.13" ]

let private contains (needle: string) (haystack: string) =
    haystack.Contains(">" + needle + "<", System.StringComparison.Ordinal)

[<Tests>]
let tests =
    testList
        "Phase 1941 — the CellFormat projection is culture-invariant on the server"
        [ for culture in [ "en-US"; "de-DE" ] do
              for (name, format, value, expected) in cases do
                  test (sprintf "%s under %s renders %s (SSR and email)" name culture expected) {
                      let node = metric format value

                      let html, email =
                          under culture (fun () ->
                              Render.render BindingResolver.empty node, Email.render BindingResolver.empty node)

                      Expect.isTrue (contains expected html) (sprintf "SSR did not render %s: %s" expected html)
                      Expect.isTrue (contains expected email) (sprintf "email did not render %s: %s" expected email)
                  }

          test "the de-DE probe can fail: the culture is really switched" {
              // The falsifier for every case above: a culture switch that did not
              // take would make them pass vacuously.
              let text = under "de-DE" (fun () -> (1.5).ToString("F2"))
              Expect.equal text "1,50" "the probe's culture switch did not take effect"
          } ]
