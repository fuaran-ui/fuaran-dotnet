module Fuaran.UI.JsonDecode.Tests.FormattingTests

// ============================================================================
//  Phase 1941 — the reference host's leg of the `formatting` family.
//
//  Two claims. The COMMITTED `formatting/manifest.json` is byte-identical to a
//  fresh emission (a formatter change that moves a pinned text fails for the
//  author who made it), and every committed vector, read back from the FILE
//  through the reference decoder, renders its `expectedText`. The second reads
//  the file rather than reusing `FormattingArtifact.all`, because reading the
//  file is the path every other host takes, and an emitter must never be its
//  own only reader.
// ============================================================================

open System.IO
open System.Text.Json
open Expecto

open Fuaran.UI.Types
open Fuaran.UI.Ops.JsonDecode

let private corpusRoot = Corpus.findRoot ()

let private artifactPath =
    Path.Combine(corpusRoot, FormattingArtifact.directory, FormattingArtifact.fileName)

let private regenerate =
    "regenerate with `dotnet run --project src/Fuaran.UI.JsonDecode.Tests -- --emit-formatting`"

/// The committed vectors, each as (id, raw value token, raw format object,
/// expected text).
let private committed () : (string * string * string * string) list =
    if not (File.Exists artifactPath) then
        failwithf "%s is missing from the corpus — %s" artifactPath regenerate

    use doc = JsonDocument.Parse(File.ReadAllText artifactPath)

    [ for el in doc.RootElement.GetProperty("vectors").EnumerateArray() ->
          string (el.GetProperty("id").GetString()),
          el.GetProperty("value").GetRawText(),
          el.GetProperty("format").GetRawText(),
          string (el.GetProperty("expectedText").GetString()) ]

[<Tests>]
let tests =
    testList
        "WIRE_FORMAT §27.8.2 — the formatting family (reference host)"
        [ testCase "committed formatting/manifest.json is byte-identical to a fresh emission" (fun () ->
              Expect.equal
                  (File.ReadAllText artifactPath)
                  (FormattingArtifact.toJson ())
                  ("wire-format-fixtures/formatting/manifest.json is stale — " + regenerate))

          testCase "every committed vector renders its expected text through the reference decoder" (fun () ->
              let vectors = committed ()
              Expect.isNonEmpty vectors "the family carries no vectors"

              let failures =
                  [ for (id, value, format, expected) in vectors do
                        let json =
                            sprintf
                                """{"id":"v","kind":{"$type":"Metric","format":%s,"label":"","value":{"$type":"Static","value":%s}}}"""
                                format
                                value

                        let produced =
                            match decodeNodeObj json with
                            | Ok { Kind = NodeKind.Metric spec } ->
                                match
                                    Fuaran.UI.Renderer.BindingResolver.resolveScalarFloat
                                        Fuaran.UI.BindingSources.empty
                                        spec.Value
                                with
                                | Fuaran.UI.Renderer.BindingResolver.Resolved v ->
                                    Fuaran.UI.Renderer.Formatting.formatNumber spec.Format v
                                | other -> sprintf "<unresolved %A>" other
                            | Ok other -> sprintf "<decoded %A>" other.Kind
                            | Error e -> "<decode error " + e.Message + ">"

                        if produced <> expected then
                            yield sprintf "%s: expected %s, produced %s" id expected produced ]

              Expect.isEmpty failures "the reference host disagrees with the committed family")

          testCase "the family's negative zeros are read back with their sign" (fun () ->
              // The falsifier for the `-0.0` spelling: a reader that lost the sign
              // would still pass the vectors whose text is `0`, so this asserts the
              // decode itself.
              let negativeZeros =
                  committed () |> List.filter (fun (_, value, _, _) -> value = "-0.0")

              Expect.isNonEmpty negativeZeros "the family pins no negative zero"

              for (id, value, _, _) in negativeZeros do
                  let json =
                      sprintf
                          """{"id":"v","kind":{"$type":"Metric","label":"","value":{"$type":"Static","value":%s}}}"""
                          value

                  match decodeNodeObj json with
                  | Ok { Kind = NodeKind.Metric spec } ->
                      match
                          Fuaran.UI.Renderer.BindingResolver.resolveScalarFloat
                              Fuaran.UI.BindingSources.empty
                              spec.Value
                      with
                      | Fuaran.UI.Renderer.BindingResolver.Resolved v ->
                          Expect.isTrue (v = 0.0 && System.Double.IsNegative v) (id + ": the sign was lost")
                      | other -> failtestf "%s: %A" id other
                  | other -> failtestf "%s: %A" id other) ]
