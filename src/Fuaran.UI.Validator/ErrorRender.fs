module Fuaran.UI.Validator.ErrorRender

// ============================================================================
//  Finding rendering.
//
//  Two output shapes:
//
//    1. Plain — one line per finding, formatted as
//         `severity file:line:col code: message`
//       For human consumption from a terminal / CI log.
//
//    2. JSON  — newline-delimited JSON, one finding per line, with the §4d
//       AI-recovery fields populated where applicable. Downstream consumers
//       (the apply-engine; AI consumer) parse this to
//       drive AI retries.
//
//  The JSON shape is stable: renames are breaking. The shipping payload is
//  intentionally flat — easier for an LLM consumer to reason about than a
//  nested object graph.
// ============================================================================

open System.Text.Json
open Fuaran.UI.Validator.Findings

let private severityToken (s: Severity) =
    match s with
    | Error -> "error"
    | Warning -> "warning"

let renderPlain (finding: Finding) : string =
    let head =
        sprintf
            "%s %s:%d:%d %s: %s"
            (severityToken finding.Severity)
            finding.Location.File
            finding.Location.Line
            finding.Location.Column
            finding.Code
            finding.Message

    match finding.AvailableFields, finding.Suggestion with
    | Some available, Some s -> sprintf "%s\n  available: %s\n  suggestion: %s" head (String.concat ", " available) s
    | Some available, None -> sprintf "%s\n  available: %s" head (String.concat ", " available)
    | None, Some s -> sprintf "%s\n  suggestion: %s" head s
    | None, None -> head

let renderJson (finding: Finding) : string =
    use buffer = new System.IO.MemoryStream()

    do
        use jw = new Utf8JsonWriter(buffer)
        jw.WriteStartObject()
        jw.WriteString("severity", severityToken finding.Severity)
        jw.WriteString("code", finding.Code)
        jw.WriteString("file", finding.Location.File)
        jw.WriteNumber("line", finding.Location.Line)
        jw.WriteNumber("column", finding.Location.Column)
        jw.WriteString("message", finding.Message)

        match finding.AvailableFields with
        | Some available ->
            jw.WriteStartArray("available_fields")

            for a in available do
                jw.WriteStringValue a

            jw.WriteEndArray()
        | None -> ()

        match finding.Suggestion with
        | Some s -> jw.WriteString("suggestion", s)
        | None -> ()

        jw.WriteEndObject()

    buffer.ToArray() |> System.Text.Encoding.UTF8.GetString

type OutputFormat =
    | Plain
    | Json

let renderAll (format: OutputFormat) (findings: Finding seq) : string =
    let renderer =
        match format with
        | Plain -> renderPlain
        | Json -> renderJson

    findings |> Seq.map renderer |> String.concat "\n"
