module Fuaran.UI.JsonDecode.Tests.FormattingArtifact

// ============================================================================
//  The generated number-FORMATTING conformance family (Phase 1941) —
//  `formatting/manifest.json`, a self-enumerated family (WIRE_FORMAT §27.7)
//  whose normative sentence is §27.8.2's `formatting` entry.
//
//  The question it answers: given a number and a `CellFormat`, what TEXT does a
//  conformant host render? It is the projection behind a `Metric` value and
//  trend, a `LabelValueRow` value and a grid cell, and the reference renderers
//  share it (`Fuaran.UI.Renderer.Formatting.formatNumber`). The answer is the
//  hydrating JavaScript's, because a server-rendered figure the browser
//  re-renders differently is a hydration mismatch on every page that uses it:
//  `String(x)`, `x.toFixed(d)` and `Number(x.toPrecision(d)).toString()`.
//
//  The vectors sit at the values where a host's native formatter goes wrong:
//  exponent form far below 1e21 (`%g`, .NET `"G<n>"`), a tie rounded to even
//  rather than away from zero, a negative zero, a negative amount that rounds
//  to zero, a current-culture decimal comma.
//
//  THE PROOF IS HERE. `toJson` builds each vector's `Metric` wrapper, decodes it
//  through the reference decoder, re-encodes it and checks the format's
//  canonical spelling survived, checks the value token decoded to the intended
//  double bit for bit, and RAISES when `formatNumber` disagrees with the
//  authored expectation. So the family cannot publish a claim the reference
//  host does not meet, and its `format` objects are the reference codec's own
//  spelling.
//
//  Co-emitted by `Corpus.emit`, and writable alone with
//  `--emit-formatting [<dir>]`. A stale-artefact guard in `FormattingTests`
//  asserts byte-equality with the committed file, and reads the committed
//  vectors back through the reference decoder.
// ============================================================================

open System
open System.Globalization
open System.IO
open System.Text.Encodings.Web
open System.Text.Json

open Fuaran.UI.Types
open Fuaran.UI.Ops.JsonDecode

[<Literal>]
let artifactId = "https://fuaran.dev/wire-format/v1/formatting/manifest.json"

/// The family's directory at the corpus root; the family is named by it.
[<Literal>]
let directory = "formatting"

[<Literal>]
let fileName = "manifest.json"

type Vector =
    { Id: string
      Value: float
      Format: CellFormat
      ExpectedText: string
      Description: string }

let private v id value format expected description =
    { Id = id
      Value = value
      Format = format
      ExpectedText = expected
      Description = description }

/// The authored vectors. Ordinal-sorted by id on the way out, so an addition
/// lands as one clean insert.
let all: Vector list =
    [ v "none-1e21" 1e21 CellFormat.None "1e+21" "String(x) turns exponential at 1e21, lower-case and signed"
      v
          "none-1e20"
          1e20
          CellFormat.None
          "100000000000000000000"
          "below 1e21 String(x) is positional, where a host's general format goes exponential"
      v "none-negative-zero" -0.0 CellFormat.None "0" "String(-0) is 0: a negative zero carries no sign"
      v
          "none-shortest-round-trip"
          0.30000000000000004
          CellFormat.None
          "0.30000000000000004"
          "the shortest digits that round-trip, not a rounded display"
      v "none-small-positional" 0.000001234 CellFormat.None "0.000001234" "from 1e-6 String(x) stays positional"
      v
          "none-small-exponential"
          1.234e-7
          CellFormat.None
          "1.234e-7"
          "below 1e-6 String(x) is exponential, with no zero-padded exponent"
      v
          "number-default-fraction"
          1234567.5
          (CellFormat.Number(None, None))
          "1234567.5"
          "a non-integer with no declared decimals is String(x), never %g's 1.23457e+06"
      v
          "number-default-integer"
          1234.0
          (CellFormat.Number(None, None))
          "1234"
          "an integer with no declared decimals is toFixed(0)"
      v
          "number-default-negative-zero"
          -0.0
          (CellFormat.Number(None, None))
          "0"
          "a negative zero is an integer, and toFixed(0) does not sign it"
      v "number-default-1e21" 1e21 (CellFormat.Number(None, None)) "1e+21" "toFixed answers String(x) from 1e21 up"
      v "number-fixed-2" 1234.5 (CellFormat.Number(Some 2, None)) "1234.50" "fixed places, no grouping, a point"
      v
          "number-fixed-tie-away"
          0.125
          (CellFormat.Number(Some 2, None))
          "0.13"
          "an exact binary tie rounds away from zero, not to even"
      v
          "number-fixed-binary-below-tie"
          1.005
          (CellFormat.Number(Some 2, None))
          "1.00"
          "1.005 is stored just below the tie, so it rounds down"
      v
          "number-fixed-negative-to-zero"
          -0.001
          (CellFormat.Number(Some 2, None))
          "-0.00"
          "a negative amount keeps its sign when it rounds to zero"
      v
          "number-fixed-negative-zero"
          -0.0
          (CellFormat.Number(Some 2, None))
          "0.00"
          "a negative zero is not less than zero, so it is unsigned"
      v
          "number-fixed-0-tie"
          2.5
          (CellFormat.Number(Some 0, None))
          "3"
          "toFixed(0) of an exact half rounds up in magnitude"
      v "number-fixed-1e21" 1e21 (CellFormat.Number(Some 2, None)) "1e+21" "declared places do not apply from 1e21 up"
      v "percent-default" 0.4567 (CellFormat.Percent(None, None)) "45.7%" "one place by default, after the x100"
      v "percent-fixed-1" 0.125 (CellFormat.Percent(Some 1, None)) "12.5%" "declared places on the scaled ratio"
      v
          "percent-fixed-0-tie"
          0.005
          (CellFormat.Percent(Some 0, None))
          "1%"
          "0.005 x 100 is exactly 0.5, and the tie rounds up"
      v "percent-negative-zero" -0.0 (CellFormat.Percent(None, None)) "0.0%" "a negative zero ratio renders unsigned"
      v
          "currency-code-prefix"
          1234.5
          (CellFormat.Currency("USD", None))
          "USD 1234.50"
          "the cell vocabulary's currency is the ISO code, a space and two places"
      v
          "significant-3-of-1234"
          1234.0
          (CellFormat.SignificantDigits 3)
          "1230"
          "three significant digits of 1234 are 1230, never 1.23E+03"
      v
          "significant-3-of-1234567.5"
          1234567.5
          (CellFormat.SignificantDigits 3)
          "1230000"
          "rounded to three digits, then spelled by String(x)"
      v
          "significant-10-of-1234567.5"
          1234567.5
          (CellFormat.SignificantDigits 10)
          "1234567.5"
          "more digits than the value needs adds no trailing zeros"
      v
          "significant-3-small"
          0.000001234
          (CellFormat.SignificantDigits 3)
          "0.00000123"
          "a small value stays positional from 1e-6"
      v
          "significant-2-tie"
          0.125
          (CellFormat.SignificantDigits 2)
          "0.13"
          "an exact tie at the last significant digit rounds away from zero"
      v "significant-negative-zero" -0.0 (CellFormat.SignificantDigits 3) "0" "a negative zero is 0"
      v
          "significant-1e21"
          1e21
          (CellFormat.SignificantDigits 3)
          "1e+21"
          "String(x) of the rounded value is exponential from 1e21" ]
    |> List.sortWith (fun a b -> String.CompareOrdinal(a.Id, b.Id))

/// The canonical wire spelling of a cell format — the reference encoder's,
/// checked by `prove` below rather than trusted.
let formatJson (format: CellFormat) : string =
    match format with
    | CellFormat.None -> """{"$type":"None"}"""
    | CellFormat.Number(None, None) -> """{"$type":"Number"}"""
    | CellFormat.Number(Some d, None) -> sprintf """{"$type":"Number","decimals":%d}""" d
    | CellFormat.Percent(None, None) -> """{"$type":"Percent"}"""
    | CellFormat.Percent(Some d, None) -> sprintf """{"$type":"Percent","decimals":%d}""" d
    | CellFormat.Currency(code, None) -> sprintf """{"$type":"Currency","code":"%s"}""" code
    | CellFormat.SignificantDigits d -> sprintf """{"$type":"SignificantDigits","digits":%d}""" d
    | other -> failwithf "the formatting family carries no %A vector" other

/// The JSON number token of a vector's value: the shortest round-trip spelling,
/// with a negative zero written `-0.0` so that every host's JSON reader keeps
/// its sign (a bare `-0` reads as an integer zero in some).
let valueToken (value: float) : string =
    if value = 0.0 && Double.IsNegative value then
        "-0.0"
    else
        value.ToString("R", CultureInfo.InvariantCulture)

/// The vector as a one-node document: a `Metric` whose `value` is the number
/// and whose `format` is the format, the projection's first-class home.
let private wrapper (vector: Vector) : string =
    sprintf
        """{"id":"v","kind":{"$type":"Metric","format":%s,"label":"","value":{"$type":"Static","value":%s}}}"""
        (formatJson vector.Format)
        (valueToken vector.Value)

/// What the reference host renders for a vector, read through the reference
/// decoder: the wrapper decodes, its format is the vector's, its value is the
/// vector's to the bit, and the format's spelling survives a re-encode.
let rendered (vector: Vector) : string =
    let json = wrapper vector

    let node =
        match decodeNodeObj json with
        | Ok n -> n
        | Error e -> failwithf "formatting vector '%s' does not decode: %s" vector.Id e.Message

    let reencoded = Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode node

    // `None` is the field's default, which the canonical encoder omits; every
    // other format must come back in exactly the spelling the family writes.
    let spelled =
        match vector.Format with
        | CellFormat.None -> not (reencoded.Contains "\"format\":")
        | format -> reencoded.Contains("\"format\":" + formatJson format)

    if not spelled then
        failwithf
            "formatting vector '%s': the format's spelling is not the reference encoder's — %s"
            vector.Id
            reencoded

    match node.Kind with
    | NodeKind.Metric spec ->
        if formatJson spec.Format <> formatJson vector.Format then
            failwithf "formatting vector '%s' decoded format %A" vector.Id spec.Format

        match Fuaran.UI.Renderer.BindingResolver.resolveScalarFloat Fuaran.UI.BindingSources.empty spec.Value with
        | Fuaran.UI.Renderer.BindingResolver.Resolved value when
            BitConverter.DoubleToInt64Bits value = BitConverter.DoubleToInt64Bits vector.Value
            ->
            Fuaran.UI.Renderer.Formatting.formatNumber spec.Format value
        | other ->
            failwithf "formatting vector '%s': value token %s resolved to %A" vector.Id (valueToken vector.Value) other
    | other -> failwithf "formatting vector '%s' decoded to %A" vector.Id other

let private description =
    "The Fuaran number-FORMATTING conformance family (WIRE_FORMAT.md 27.8.2). Each vector is "
    + "(value, format, expected text): `value` is a JSON number (a negative zero is written `-0.0`), "
    + "`format` a canonical `CellFormat`, and `expectedText` the text every conformant host renders for "
    + "that number under that format - the projection behind a Metric value and trend, a "
    + "LabelValueRow value and a grid cell. The text is the hydrating JavaScript's, so a server-rendered "
    + "figure and its hydration agree byte for byte: `None` and `DateTime` are `String(x)`; `Number` is "
    + "`x.toFixed(d)`, or with no declared places `x.toFixed(0)` for an integer and `String(x)` "
    + "otherwise; `Percent` is `(x * 100).toFixed(d)` plus `%` (one place by default); `Currency` is "
    + "the code, a space and `x.toFixed(2)`; `SignificantDigits` is `Number(x.toPrecision(d)).toString()`. "
    + "The text depends on no locale. Generated from the reference host's own formatter, which PROVES "
    + "every vector, through its decoder, before this file is written."

/// Render the artefact.
let toJson () : string =
    let opts =
        JsonWriterOptions(Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping)

    use stream = new MemoryStream()
    use w = new Utf8JsonWriter(stream, opts)

    w.WriteStartObject()
    w.WriteNumber("version", 1)
    w.WriteString("$id", artifactId)
    w.WriteString("description", description)
    w.WriteStartArray("vectors")

    for vector in all do
        // THE PROOF. The reference host renders the vector and must agree with
        // the authored expectation, or nothing is written.
        let produced = rendered vector

        if produced <> vector.ExpectedText then
            failwithf
                "formatting vector '%s' violates its own expectation:\n  value    → %s\n  format   → %s\n  expected → %s\n  produced → %s"
                vector.Id
                (valueToken vector.Value)
                (formatJson vector.Format)
                vector.ExpectedText
                produced

        w.WriteStartObject()
        w.WriteString("id", vector.Id)
        w.WritePropertyName("value")
        w.WriteRawValue(valueToken vector.Value)
        w.WritePropertyName("format")
        w.WriteRawValue(formatJson vector.Format)
        w.WriteString("expectedText", vector.ExpectedText)
        w.WriteString("description", vector.Description)
        w.WriteEndObject()

    w.WriteEndArray()
    w.WriteEndObject()
    w.Flush()

    Text.Encoding.UTF8.GetString(stream.ToArray()) + "\n"

/// Write the family into a corpus directory.
let write (corpusRoot: string) : string =
    let dir = Path.Combine(corpusRoot, directory)
    Directory.CreateDirectory dir |> ignore
    let path = Path.Combine(dir, fileName)
    File.WriteAllText(path, toJson ())
    path
