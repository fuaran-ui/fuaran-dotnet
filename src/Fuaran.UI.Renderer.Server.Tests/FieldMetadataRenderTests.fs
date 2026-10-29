module Fuaran.UI.Renderer.Server.Tests.FieldMetadataRenderTests

// ============================================================================
//  Phase 2209 — a bound field's unit and label at the read sites
//  (WIRE_FORMAT §3.6.27), one fixture set for both tiers of this host.
//
//  Each case below is a wire document carrying the id the spec's parity table
//  names (`fm-*`); fuaran-ts's renderer suite carries the same documents under
//  the same ids, so the two renderers certify one rule over one set. The
//  server tier is asserted on its HTML; the browser tier, which cannot render
//  to a string on .NET, is asserted on the shared function its arm dispatches
//  through (`FieldDisplay.withColumnHeaders` — the bound grid is the client's,
//  and this tier serves its placeholder). The `fm-plain` cases are the
//  byte-identity half: a field stating no metadata renders exactly what the
//  same document renders with no field read at all.
// ============================================================================

open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server
open Fuaran.UI.Ops

let private contains (needle: string) (haystack: string) =
    haystack.Contains(needle, System.StringComparison.Ordinal)

/// One row; `mass` states a label and a unit, `speed` an EMPTY label and a unit.
let private metaSource =
    """{"columns":{"mass":{"validity":[true],"values":[12.5]},"speed":{"validity":[true],"values":[3.5]}},"schema":[{"label":"Payload mass","name":"mass","type":"float","unit":"kg"},{"label":"","name":"speed","type":"float","unit":"m/s"}]}"""

/// The same row with fields that state nothing.
let private plainSource =
    """{"columns":{"mass":{"validity":[true],"values":[12.5]},"speed":{"validity":[true],"values":[3.5]}},"schema":[{"name":"mass","type":"float"},{"name":"speed","type":"float"}]}"""

let private massOnly (metadata: bool) =
    if metadata then
        """{"columns":{"mass":{"validity":[true],"values":[12.5]}},"schema":[{"label":"Payload mass","name":"mass","type":"float","unit":"kg"}]}"""
    else
        """{"columns":{"mass":{"validity":[true],"values":[12.5]}},"schema":[{"name":"mass","type":"float"}]}"""

let private transform (source: string) =
    sprintf """{"$type":"Transform","pipeline":[],"source":%s}""" source

let private grid (id: string) (label: string) (source: string) =
    sprintf
        """{"id":"%s","kind":{"$type":"DataGrid","columns":[{"field":"mass","kind":{"$type":"Text"},"label":"%s"}],"source":%s}}"""
        id
        label
        (transform source)

let private chart (id: string) (xTitle: string option) (source: string) =
    let title =
        match xTitle with
        | Some t -> sprintf ""","xTitle":"%s" """ t
        | None -> ""

    sprintf
        """{"id":"%s","kind":{"$type":"Chart","kind":"Line","source":%s,"stacked":false%s,"xField":"mass","yFields":["speed"]}}"""
        id
        (transform source)
        title

let private metric (id: string) (label: string) (format: string option) (metadata: bool) =
    let fmt =
        match format with
        | Some f -> sprintf ""","format":%s""" f
        | None -> ""

    sprintf
        """{"id":"%s","kind":{"$type":"Metric"%s,"label":"%s","value":%s}}"""
        id
        fmt
        label
        (transform (massOnly metadata))

let private a11y (id: string) (metadata: bool) =
    sprintf
        """{"accessibility":{"label":%s,"role":"region"},"id":"%s","kind":{"$type":"Box","children":[],"layout":{"$type":"Auto"},"role":"Group"}}"""
        (transform (massOnly metadata))
        id

let private decode (json: string) : Node<obj> =
    match JsonDecode.decodeNodeObj json with
    | Ok node -> node
    | Error e -> failwithf "decode failed: %A\n%s" e json

let private html (json: string) =
    Render.render BindingResolver.empty (decode json)

let private gridSpec (json: string) =
    match (decode json).Kind with
    | NodeKind.DataGrid spec -> spec
    | k -> failwithf "not a grid: %A" k

/// The browser tier's header cells read `withColumnHeaders` over the fields the
/// grid's own evaluation returned (`Render.fs` `renderGrid`, `AgGridAdapter`).
let private clientHeaders (json: string) : string list =
    let spec = gridSpec json

    let _, fields =
        BindingResolver.resolveRowsWithFields BindingResolver.empty spec.Source

    (BindingResolver.FieldDisplay.withColumnHeaders fields spec).Columns
    |> List.map _.Label

let private titlesOf (source: string) =
    let _, fields =
        BindingResolver.resolveRowsWithFields BindingResolver.empty (gridSpec (grid "g" "" source)).Source

    fields |> List.map BindingResolver.FieldDisplay.columnTitle

[<Tests>]
let fieldMetadataRenderTests =
    testList
        "field metadata at the read sites (2209, WIRE_FORMAT 3.6.27)"
        [ testList
              "the derived texts"
              [ test "column title, per the spec's table" {
                    Expect.equal (titlesOf metaSource) [ Some "Payload mass (kg)"; Some "speed (m/s)" ] "meta titles"
                    Expect.equal (titlesOf plainSource) [ None; None ] "a field stating nothing has no title"

                    Expect.equal
                        (titlesOf
                            """{"columns":{"ratio":{"validity":[true],"values":[0.5]}},"schema":[{"name":"ratio","type":"float","unit":"1"}]}""")
                        [ None ]
                        "the dimensionless unit shows nothing"

                    Expect.equal
                        (titlesOf
                            """{"columns":{"mass":{"validity":[true],"values":[1.0]}},"schema":[{"label":"Payload mass","name":"mass","type":"float"}]}""")
                        [ Some "Payload mass" ]
                        "a label alone"
                } ]

          testList
              "DataGrid header (read site 1)"
              [ test "fm-header-derived — an empty header takes the column title" {
                    Expect.equal (clientHeaders (grid "fm-header-derived" "" metaSource)) [ "Payload mass (kg)" ] ""
                }
                test "fm-header-explicit — the author's header renders verbatim" {
                    Expect.equal (clientHeaders (grid "fm-header-explicit" "Mass (t)" metaSource)) [ "Mass (t)" ] ""
                }
                test "fm-header-plain — a field stating nothing leaves the header as it was" {
                    Expect.equal (clientHeaders (grid "fm-header-plain" "" plainSource)) [ "" ] ""
                }
                test "a grid whose source is not a Transform has no fields, and its spec is untouched" {
                    let spec =
                        gridSpec
                            """{"id":"g","kind":{"$type":"DataGrid","columns":[{"field":"mass","kind":{"$type":"Text"},"label":""}],"source":{"$type":"Static","value":[{"mass":1}]}}}"""

                    let _, fields =
                        BindingResolver.resolveRowsWithFields BindingResolver.empty spec.Source

                    Expect.isEmpty fields "no Transform, no bound field"

                    Expect.isTrue
                        (obj.ReferenceEquals(BindingResolver.FieldDisplay.withColumnHeaders fields spec, spec))
                        "the spec is returned as it came"
                } ]

          testList
              "Chart axis titles (read site 2)"
              [ test "fm-axis-derived — both absent titles take their fields' column titles" {
                    let out = html (chart "fm-axis-derived" None metaSource)
                    Expect.isTrue (contains ">Payload mass (kg)<" out) "x title from the field"
                    Expect.isTrue (contains ">speed (m/s)<" out) "y title from the first y field"
                }
                test "fm-axis-explicit — a declared title wins" {
                    let out = html (chart "fm-axis-explicit" (Some "Mass") metaSource)
                    Expect.isTrue (contains ">Mass<" out) "the declared x title"
                    Expect.isFalse (contains "Payload mass (kg)" out) "the field never overrides a declared title"
                    Expect.isTrue (contains ">speed (m/s)<" out) "the absent y title is still filled"
                }
                test "fm-axis-plain — fields stating nothing keep the capitalised fallback, byte for byte" {
                    let out = html (chart "fm-axis-plain" None plainSource)
                    Expect.isTrue (contains ">Mass<" out) "the capitalised x fallback"
                    Expect.isTrue (contains ">Speed<" out) "the capitalised y fallback"

                    // The same document with the rows as a static source: no field
                    // is read at all, so this is the render before 2209.
                    let staticJson =
                        """{"id":"fm-axis-plain","kind":{"$type":"Chart","kind":"Line","source":{"$type":"Static","value":[{"mass":12.5,"speed":3.5}]},"stacked":false,"xField":"mass","yFields":["speed"]}}"""

                    Expect.equal out (html staticJson) "byte-identical to a render that reads no field"
                } ]

          testList
              "Metric (read site 3)"
              [ test "fm-metric-unit — the read value carries the unit" {
                    let out = html (metric "fm-metric-unit" "Payload" None true)
                    Expect.isTrue (contains "fuaran-metric-value\">12.5 kg<" out) out
                    Expect.isTrue (contains "fuaran-metric-label\">Payload<" out) "the explicit caption"
                }
                test "fm-metric-percent — a format that states its own unit takes no second one" {
                    let out =
                        html (metric "fm-metric-percent" "Payload" (Some """{"$type":"Percent"}""") true)

                    Expect.isFalse (contains "kg" out) out
                }
                test "fm-metric-caption — an empty caption takes the field's display name" {
                    let out = html (metric "fm-metric-caption" "" None true)
                    Expect.isTrue (contains "fuaran-metric-label\">Payload mass<" out) out
                }
                test "fm-plain — a metric over a field stating nothing renders as before" {
                    let out = html (metric "fm-plain" "" None false)
                    Expect.isTrue (contains "fuaran-metric-value\">12.5<" out) out
                    Expect.isTrue (contains "fuaran-metric-label\"><" out) "the empty caption stays empty"
                }
                test "an unresolved value takes no unit — no value was read" {
                    let json =
                        """{"id":"m","kind":{"$type":"Metric","label":"x","value":{"$type":"Transform","pipeline":[{"$type":"limit","n":0}],"source":"""
                        + massOnly true
                        + "}}}"

                    Expect.isFalse (contains "kg" (html json)) "the em-dash stands alone"
                } ]

          testList
              "accessible name (read site 4)"
              [ test "fm-a11y-unit — a name read from a Transform says its unit" {
                    let out = html (a11y "fm-a11y-unit" true)
                    Expect.isTrue (contains "aria-label=\"12.5 kg\"" out) out
                }
                test "fm-plain — a name over a field stating nothing is unchanged" {
                    let out = html (a11y "fm-plain" false)
                    Expect.isTrue (contains "aria-label=\"12.5\"" out) out
                } ] ]
