module Fuaran.UI.JsonDecode.Tests.TransformSourceLeniency

// Phase 815 — the Transform `source` slot's organic-demand leniencies. Three
// model families independently emitted `{"$type":"State","defaultValue":
// [{row},…]}` as a Transform source (the Tier-D pilot count badge,
// 2026-08-13); the host bridge normalises a Static/Bound WRAPPER (unwrap to
// its carried data — snapshot semantics) and ROW-MAJOR data (transpose to
// canonical columnar) before Fuaran.Core's ColumnCodec decodes.
//
// Phase 818 upgraded the STATE-shaped source's semantics in place: it is now
// PRESERVED as `TransformSource.Live` (re-evaluated when the state key
// changes) and round-trips byte-for-byte — the canonical spelling, not a
// shorthand normalised away. Its initial snapshot still derives through the
// same 815 normalisation, so evaluation over the defaults is byte-identical
// to the snapshot era (pinned below via the resolver), and the didactics
// (ragged rows, an empty State wrapper) are unchanged.

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
        """{"id":"count-badge","kind":{"$type":"Badge","label":{"$type":"Bound","binding":{"$type":"Transform","pipeline":[],"source":%s}},"variant":"Neutral"}}"""
        source

let private canonicalColumnar =
    """{"columns":{"medication":["Amoxicillin","Ibuprofen"],"quantity":[20,50]}}"""

let private rowMajor =
    """[{"medication":"Amoxicillin","quantity":20},{"medication":"Ibuprofen","quantity":50}]"""

[<Tests>]
let tests =
    testList
        "Fuaran.UI.Ops.JsonDecode — Transform source leniencies (Phase 815)"
        [ test
              "the observed pilot shape — a State source around row-major rows — is PRESERVED and round-trips byte-for-byte (Phase 818)" {
              let observed =
                  badgeWith (sprintf """{"$type":"State","defaultValue":%s,"key":"request-log"}""" rowMajor)

              let fromObserved = decodeOk "state-wrapped row-major" observed

              // One wire dialect: the State-shaped source IS canonical now —
              // re-encode reproduces it byte-for-byte (the binding reference
              // survives decode, so a runtime can re-evaluate it live).
              Expect.equal
                  (CanonicalJson.encodeNode fromObserved)
                  observed
                  "canonical re-encode reproduces the State-shaped source byte-for-byte"
          }

          test
              "live-vs-snapshot equivalence — the State-sourced Transform evaluates its defaults to the SAME cell the canonical columnar snapshot yields" {
              // The 815 tests pinned that both spellings decoded to one node;
              // 818 preserves the binding, so the equivalence moves to the
              // EVALUATION: over an unwritten state store, the live source
              // resolves through its carried defaultValue and must yield
              // exactly what the canonical columnar snapshot yields — which is
              // what keeps SSR output byte-identical to the snapshot era.
              let pipeline =
                  """[{"$type":"groupBy","aggs":[{"fn":"count","name":"n","of":"medication"}],"keys":[]}]"""

              let badgeWithPipeline (source: string) =
                  sprintf
                      """{"id":"count-badge","kind":{"$type":"Badge","label":{"$type":"Bound","binding":{"$type":"Transform","pipeline":%s,"source":%s}},"variant":"Neutral"}}"""
                      pipeline
                      source

              let labelBinding (n: Fuaran.UI.Types.Node<obj>) : Fuaran.UI.Types.Binding<string> =
                  match n.Kind with
                  | Fuaran.UI.Types.NodeKind.Badge b ->
                      (match b.Label with
                       | Fuaran.UI.Types.TextSource.Bound binding -> binding
                       | other -> failtestf "expected a Bound label, got %A" other)
                  | other -> failtestf "expected a Badge, got %A" other

              let live =
                  decodeOk
                      "live state-sourced"
                      (badgeWithPipeline (
                          sprintf """{"$type":"State","defaultValue":%s,"key":"request-log"}""" rowMajor
                      ))
                  |> labelBinding

              let snapshot =
                  decodeOk "canonical columnar" (badgeWithPipeline canonicalColumnar)
                  |> labelBinding

              let resolveText b =
                  match
                      Fuaran.UI.Renderer.BindingResolver.resolveScalarText Fuaran.UI.Renderer.BindingResolver.empty b
                  with
                  | Fuaran.UI.Renderer.BindingResolver.Resolved s -> s
                  | other -> failtestf "expected Resolved, got %A" other

              Expect.equal (resolveText live) (resolveText snapshot) "live-over-defaults == snapshot evaluation"
              Expect.equal (resolveText live) "2" "the count badge derives 2 from two rows"
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
