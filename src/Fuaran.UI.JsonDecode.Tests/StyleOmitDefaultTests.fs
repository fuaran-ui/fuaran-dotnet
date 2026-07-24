module Fuaran.UI.JsonDecode.Tests.StyleOmitDefault

// Phase 460 — the stylistic spec fields (`format` / `tone` / `weight` /
// `emphasis` / `width`) are omitted-when-default on BOTH boundaries: the decoder
// restores the identity default on absence (`CellFormat.None` /
// `ToneVariant.Default` / `StyleWeight.Standard` / `Emphasis.Normal` /
// `ColumnWidth.Auto`), and the encoder emits each only when non-default — exactly
// as `role`/`voice` do inside `SemanticStyle` (Phase 147). These tests pin (1)
// absent ⇒ identity default AND the symmetric encoder omission — proven by showing
// the minimal emission and the explicit-default emission decode to the SAME node
// and both re-encode to the byte-minimal form, (2) explicit defaults keep decoding
// (read-compat), and (3) the curated decode-only lenient-ingest aliases
// canonicalise to the DU case names, while `StyleWeight` is deliberately not
// aliased (`Bold` rejected).

open Expecto
open Fuaran.UI.Ops
open Fuaran.UI.OpStream.Abstractions

let private decodeOk (label: string) (json: string) =
    match JsonDecode.decodeNodeObj json with
    | Ok n -> n
    | Error e -> failtestf "%s: decode failed: %A" label e

/// Two emissions decode to the same node iff their canonical re-encodings match.
let private sameDecodedNode (label: string) (minimal: string) (explicit: string) =
    Expect.equal
        (CanonicalJson.encodeNode (decodeOk (label + " (minimal)") minimal))
        (CanonicalJson.encodeNode (decodeOk (label + " (explicit)") explicit))
        (label
         + ": the minimal (omitted-default) emission decodes to the same node as the explicit-default one")

let private reEncoded (label: string) (json: string) =
    CanonicalJson.encodeNode (decodeOk label json)

[<Tests>]
let tests =
    testList
        "Fuaran.UI.Ops.JsonDecode — stylistic omit-when-default + lenient aliases (Phase 460)"
        [ test "Metric with only label+source decodes to identity-default style" {
              let minimal =
                  """{"id":"m","kind":{"$type":"Metric","label":"Revenue","value":{"$type":"Static","value":1234.5}}}"""

              let explicit =
                  """{"id":"m","kind":{"$type":"Metric","emphasis":"Normal","format":{"$type":"None"},"label":{"$type":"Literal","text":"Revenue"},"tone":"Default","weight":"Standard","value":{"$type":"Static","value":1234.5}}}"""

              sameDecodedNode "Metric" minimal explicit
              // Encoder symmetry: the default-styled tree re-encodes byte-minimal —
              // the stylistic fields are omitted, and the minimal form round-trips to
              // itself.
              let bytes = reEncoded "metric-minimal" minimal
              Expect.isFalse (bytes.Contains "\"tone\":\"Default\"") "default tone omitted on encode"
              Expect.isFalse (bytes.Contains "\"weight\":\"Standard\"") "default weight omitted on encode"
              Expect.isFalse (bytes.Contains "\"emphasis\":\"Normal\"") "default emphasis omitted on encode"
              Expect.isFalse (bytes.Contains "\"format\":") "default format omitted on encode"

              Expect.equal
                  bytes
                  minimal
                  "the byte-minimal Metric round-trips to itself (encode(decode(minimal)) = minimal)"
          }

          test "Callout with tone omitted decodes to ToneVariant.Default" {
              let minimal =
                  """{"id":"c","kind":{"$type":"Callout","body":{"$type":"Literal","text":"Live data is delayed."},"dismissable":false}}"""

              let explicit =
                  """{"id":"c","kind":{"$type":"Callout","body":{"$type":"Literal","text":"Live data is delayed."},"dismissable":false,"tone":"Default"}}"""

              sameDecodedNode "Callout" minimal explicit
          }

          test "Progress with tone omitted decodes to ToneVariant.Default" {
              let minimal =
                  """{"id":"p","kind":{"$type":"Progress","fraction":{"$type":"Static","value":0.42},"indeterminate":false}}"""

              let explicit =
                  """{"id":"p","kind":{"$type":"Progress","fraction":{"$type":"Static","value":0.42},"indeterminate":false,"tone":"Default"}}"""

              sameDecodedNode "Progress" minimal explicit
          }

          test "Toast with tone omitted decodes to ToneVariant.Default" {
              let minimal =
                  """{"id":"t","kind":{"$type":"Toast","dismissable":true,"message":{"$type":"Literal","text":"Saved"},"open":{"$type":"Static","value":true}}}"""

              let explicit =
                  """{"id":"t","kind":{"$type":"Toast","dismissable":true,"message":{"$type":"Literal","text":"Saved"},"open":{"$type":"Static","value":true},"tone":"Default"}}"""

              sameDecodedNode "Toast" minimal explicit
          }

          test "Grid column with format+width omitted decodes to None/Auto" {
              let minimal =
                  """{"id":"g","kind":{"$type":"DataGrid","columns":[{"kind":{"$type":"Text"},"label":"Channel"}],"editable":false,"source":{"$type":"Static","value":"<opaque>"}}}"""

              let explicit =
                  """{"id":"g","kind":{"$type":"DataGrid","columns":[{"format":{"$type":"None"},"kind":{"$type":"Text"},"label":"Channel","width":{"$type":"Auto"}}],"editable":false,"source":{"$type":"Static","value":"<opaque>"}}}"""

              sameDecodedNode "DataGrid column" minimal explicit
          }

          test "Node SemanticStyle with tone/weight/emphasis omitted decodes to defaults" {
              // `role` non-default keeps the style object present; tone/weight/emphasis omitted.
              let minimal =
                  """{"id":"h","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"style":{"role":"Data"}}"""

              let explicit =
                  """{"id":"h","kind":{"$type":"Markdown","text":{"$type":"Literal","text":"x"}},"style":{"emphasis":"Normal","role":"Data","tone":"Default","weight":"Standard"}}"""

              sameDecodedNode "SemanticStyle" minimal explicit
          }

          test "explicit-default emissions keep decoding (read-compat)" {
              // A fully explicit-default Metric must still decode (never regress the
              // pre-460 wire that always wrote the fields).
              let explicit =
                  """{"id":"m","kind":{"$type":"Metric","emphasis":"Normal","format":{"$type":"None"},"label":{"$type":"Literal","text":"R"},"tone":"Default","weight":"Standard","value":{"$type":"Static","value":1.0}}}"""

              decodeOk "explicit-default Metric" explicit |> ignore
          }

          test "lenient ToneVariant aliases decode identically to their canonical case" {
              // Equivalence form (robust to omit-on-default: `Neutral`→`Default` is
              // omitted on encode, so the shared canonical output is the minimal one).
              let aliasToCanonical =
                  [ "Positive", "Success"
                    "Danger", "Critical"
                    "Negative", "Critical"
                    "Neutral", "Default" ]

              let mk tone =
                  sprintf
                      """{"id":"c","kind":{"$type":"Callout","body":{"$type":"Literal","text":"x"},"dismissable":false,"tone":"%s"}}"""
                      tone

              for aliasIn, canonical in aliasToCanonical do
                  Expect.equal
                      (reEncoded ("tone-alias-" + aliasIn) (mk aliasIn))
                      (reEncoded ("tone-canonical-" + canonical) (mk canonical))
                      (sprintf "tone alias %s decodes + canonicalises identically to %s" aliasIn canonical)
          }

          test "lenient Emphasis aliases canonicalise to the DU case names" {
              let aliasToCanonical =
                  [ "Strong", "Loud"; "Bold", "Loud"; "Subtle", "Quiet"; "Muted", "Quiet" ]

              for aliasIn, canonical in aliasToCanonical do
                  let json =
                      sprintf
                          """{"id":"m","kind":{"$type":"Metric","emphasis":"%s","label":{"$type":"Literal","text":"R"},"value":{"$type":"Static","value":1.0}}}"""
                          aliasIn

                  Expect.stringContains
                      (reEncoded ("emphasis-alias-" + aliasIn) json)
                      (sprintf "\"emphasis\":\"%s\"" canonical)
                      (sprintf "emphasis alias %s re-encodes as the canonical %s" aliasIn canonical)
          }

          test "StyleWeight is not aliased — 'Bold' is UNKNOWN_DU_CASE" {
              let json =
                  """{"id":"m","kind":{"$type":"Metric","label":{"$type":"Literal","text":"R"},"weight":"Bold","value":{"$type":"Static","value":1.0}}}"""

              match JsonDecode.decodeNodeObj json with
              | Ok _ -> failtest "expected 'weight':'Bold' to be rejected (StyleWeight has no aliases)"
              | Error e ->
                  Expect.equal
                      e.Code
                      "UNKNOWN_DU_CASE"
                      (sprintf "weight 'Bold' rejected as UNKNOWN_DU_CASE (path %s)" e.Path)
          } ]
