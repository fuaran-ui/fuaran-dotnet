module Samples.GettingStarted.Lesson01Authoring

// ============================================================================
//  LESSON 1 — A user interface is a value.
//
//  There is no template language here, and no component to instantiate. You
//  build a typed tree with ordinary functions, and the canonical encoder turns
//  it into JSON that any conformant host can render. Because it is a value, you
//  can hold it in a variable, put it in a list, return it from a function, send
//  it over a socket, and compare two of them for equality — none of which is
//  true of a rendered view.
//
//  What to notice in the output: the JSON has no code in it. Not "no code we
//  execute" — no code at all. A tree can carry a `SetState` action or a
//  declarative data pipeline, both of which are DATA the host interprets. It
//  cannot carry a function, which is why an untrusted emission is safe to
//  render (Lesson 4).
// ============================================================================

open Fuaran.UI
open Fuaran.UI.Types

module Canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson

/// A small sales dashboard. Every constructor here is a plain function over a
/// typed record, so a wrong field name is a compile error rather than a blank
/// area on a page.
let dashboard: Node<obj> =
    Fuaran.dashboard
        "sales"
        { Defaults.dashboard with
            Children =
                [ Fuaran.heading
                      "sales-title"
                      { Level = 1
                        Text = TextSource.Literal "Q4 sales"
                        Variant = HeadingVariant.Standard }
                  Fuaran.gridLayout
                      "sales-kpis"
                      { Defaults.gridLayout<obj> with
                          Cols = 3
                          Children =
                              [ Fuaran.metric
                                    "sales-revenue"
                                    { Defaults.metric with
                                        Label = TextSource.Literal "Revenue"
                                        Value = Binding.Static(Some 142500.0)
                                        Format = CellFormat.Currency "GBP"
                                        Tone = ToneVariant.Brand }
                                Fuaran.metric
                                    "sales-orders"
                                    { Defaults.metric with
                                        Label = TextSource.Literal "Orders"
                                        Value = Binding.Static(Some 1284.0)
                                        Format = CellFormat.Number(Some 0) }
                                Fuaran.metric
                                    "sales-conversion"
                                    { Defaults.metric with
                                        Label = TextSource.Literal "Conversion"
                                        Value = Binding.Static(Some 0.043)
                                        Format = CellFormat.Percent(Some 1)
                                        Tone = ToneVariant.Success } ] }
                  Fuaran.callout
                      "sales-note"
                      { Defaults.callout with
                          Tone = ToneVariant.Info
                          Heading = Some(TextSource.Literal "Where this came from")
                          Body =
                              TextSource.Literal "This whole page is one value. The JSON below is all a renderer needs." } ] }

let run () =
    printfn "The tree, as canonical wire JSON:"
    printfn ""
    printfn "%s" (Canon.encodeNode dashboard)
    printfn ""

    // The encoder is canonical: the same tree always produces the same bytes,
    // with object keys in a fixed order and floats in a pinned format. That is
    // what makes a tree hashable, cacheable, diffable and comparable ACROSS
    // hosts — the property Lesson 3 leans on to replay a session exactly.
    let once = Canon.encodeNode dashboard
    let twice = Canon.encodeNode dashboard
    printfn "Encoded twice, byte-identical: %b" (once = twice)

    // And it renders to HTML with no browser, no bundler and no hydration —
    // the same tree the client would draw.
    let html = Fuaran.UI.Renderer.Server.Render.renderStatic dashboard
    printfn "Server-rendered HTML: %d characters, starting %s…" html.Length (html.Substring(0, min 60 html.Length))
