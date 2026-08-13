module Fuaran.UI.JsonDecode.Tests.TransformSourceLeniency

// Phase 815 — the Transform `source` slot's organic-demand leniencies. Three
// model families independently emitted `{"$type":"State","defaultValue":
// [{row},…]}` as a Transform source (the Tier-D pilot count badge,
// 2026-08-13); the host bridge now normalises a State/Static/Bound WRAPPER
// (unwrap to its carried data — initial-snapshot semantics) and ROW-MAJOR
// data (transpose to canonical columnar) before Fuaran.Core's ColumnCodec
// decodes. These tests pin both leniencies, their composition (the observed
// shape uses both at once), the canonical form's indifference, and that an
// empty wrapper still fails didactically.

open Expecto
open Fuaran.UI.Ops
open Fuaran.UI.OpStream.Abstractions

let private decodeOk (label: string) (json: string) =
    match JsonDecode.decodeNodeObj json with
    | Ok n -> n
    | Error e -> failtestf "%s: decode failed: %A" label e

/// A count-badge node whose label is Transform-bound, with the source slot
/// spelled `%s`.
let private badgeWith (source: string) =
    sprintf
        """{"id":"count-badge","kind":{"$type":"Badge","label":{"$type":"Bound","binding":{"$type":"Transform","pipeline":[],"source":%s}},"variant":"Default"}}"""
        source

let private canonicalColumnar =
    """{"columns":{"medication":["Amoxicillin","Ibuprofen"],"quantity":[20,50]}}"""

let private rowMajor =
    """[{"medication":"Amoxicillin","quantity":20},{"medication":"Ibuprofen","quantity":50}]"""

[<Tests>]
let tests =
    testList
        "Fuaran.UI.Ops.JsonDecode — Transform source leniencies (Phase 815)"
        [ test "the observed pilot shape — a State wrapper around row-major rows — decodes to the canonical source" {
              let observed =
                  badgeWith (sprintf """{"$type":"State","defaultValue":%s,"key":"request-log"}""" rowMajor)

              let canonical = badgeWith canonicalColumnar
              let fromObserved = decodeOk "state-wrapped row-major" observed
              let fromCanonical = decodeOk "canonical columnar" canonical

              Expect.equal
                  (CanonicalJson.encodeNode fromObserved)
                  (CanonicalJson.encodeNode fromCanonical)
                  "both spellings decode to the SAME node (one wire dialect, not two)"
          }

          test "a Static wrapper around a canonical columnar source unwraps" {
              let wrapped =
                  badgeWith (sprintf """{"$type":"Static","value":%s}""" canonicalColumnar)

              let bare = badgeWith canonicalColumnar

              Expect.equal
                  (CanonicalJson.encodeNode (decodeOk "static-wrapped" wrapped))
                  (CanonicalJson.encodeNode (decodeOk "bare" bare))
                  "the Static wrapper is transparent"
          }

          test "a bare row-major array transposes to columnar" {
              let fromRows = decodeOk "bare row-major" (badgeWith rowMajor)
              let fromCols = decodeOk "columnar" (badgeWith canonicalColumnar)

              Expect.equal
                  (CanonicalJson.encodeNode fromRows)
                  (CanonicalJson.encodeNode fromCols)
                  "row-major transposes to the same decoded source"
          }

          test "RAGGED row-major (an absent cell) still errors didactically — the leniency covers uniform rows" {
              // The null fill surfaces as a mixed-type column, so Core's
              // schema-inference didactic fires and names the remedy. This is
              // deliberate: silently guessing a ragged column's type would
              // trade a loud teachable error for a quiet wrong one.
              let ragged =
                  """[{"medication":"Amoxicillin","quantity":20},{"medication":"Ibuprofen"}]"""

              match JsonDecode.decodeNodeObj (badgeWith ragged) with
              | Ok _ -> failtest "ragged rows must not decode silently"
              | Error e ->
                  Expect.isTrue ((sprintf "%A" e).Contains "schema") "the didactic names the explicit-schema remedy"
          }

          test "a State wrapper carrying NO data still errors didactically" {
              match JsonDecode.decodeNodeObj (badgeWith """{"$type":"State","key":"request-log"}""") with
              | Ok _ -> failtest "an empty State wrapper must not decode as a Transform source"
              | Error e ->
                  Expect.isTrue
                      ((sprintf "%A" e).Contains "columns")
                      "the error still names the missing canonical field"
          } ]
