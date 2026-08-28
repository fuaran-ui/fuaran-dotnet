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
// to the snapshot era (pinned below via the resolver), and the ragged-rows
// didactic is unchanged.
//
// Phase 1085 retired ONE of those didactics: an empty State wrapper (a source
// naming a key and carrying no data) is no longer refused. Under Phase 1075's
// seeding rule a sibling reader's declaration fills the slot, so the bare form
// is a live source over the empty initial snapshot — the same start Selection
// and Query sources always had. The pin below is inverted rather than deleted.

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

          // Phase 1085 — the INVERSION of the Phase-822 pin that stood here.
          // A State wrapper carrying no data used to be refused as
          // un-unwrappable (`reject/reject-transform-source-empty-wrapper`,
          // retired with this change). It is now the direct spelling of "I read
          // this key and carry no data of my own" — the one FUARAN106's own
          // remedy text tells an author to write, and the one the shared-source
          // charter's §3.1 pair was written in.
          test "a State wrapper carrying NO data decodes as a LIVE source over the empty snapshot" {
              let n =
                  decodeOk "bare State" (badgeWith """{"$type":"State","key":"request-log"}""")

              match n.Kind with
              | Fuaran.UI.Types.NodeKind.Badge spec ->
                  match spec.Label with
                  | Fuaran.UI.Types.TextSource.Bound(Fuaran.UI.Types.Binding.Transform(source, _, _)) ->
                      match source with
                      | Fuaran.UI.Types.TransformSource.Live(Fuaran.UI.Types.Binding.State(key, dv), initial) ->
                          Expect.equal key "request-log" "the preserved binding names the key"
                          Expect.isNone dv "and carries no data of its own"

                          Expect.equal
                              initial
                              Fuaran.UI.HostPrelude.TransformLive.emptySource
                              "the initial snapshot is the empty table, as a Selection / Query source already was"
                      | other -> failtestf "expected a Live State source, got %A" other
                  | other -> failtestf "unexpected badge label: %A" other
              | k -> failtestf "unexpected kind: %A" k

              // Canonical, not a shorthand: the preserved binding IS the wire
              // dialect, so the bare spelling re-encodes to its own bytes.
              Expect.equal
                  (CanonicalJson.encodeNode n)
                  (badgeWith """{"$type":"State","key":"request-log"}""")
                  "the bare spelling round-trips byte-for-byte"
          }

          test "the bare spelling and \"defaultValue\": [] are ONE wire dialect, not two" {
              // Both say "I read this key and carry no data of my own", so they
              // must decode to the same live source. The empty array keeps its
              // meaning as a genuinely empty live collection; it is no longer
              // the only way to write the absence.
              let bare = decodeOk "bare" (badgeWith """{"$type":"State","key":"request-log"}""")

              let empty =
                  decodeOk "empty array" (badgeWith """{"$type":"State","defaultValue":[],"key":"request-log"}""")

              match bare.Kind, empty.Kind with
              | Fuaran.UI.Types.NodeKind.Badge b, Fuaran.UI.Types.NodeKind.Badge e ->
                  match b.Label, e.Label with
                  | Fuaran.UI.Types.TextSource.Bound(Fuaran.UI.Types.Binding.Transform(bs, _, _)),
                    Fuaran.UI.Types.TextSource.Bound(Fuaran.UI.Types.Binding.Transform(es, _, _)) ->
                      let initialOf s =
                          match s with
                          | Fuaran.UI.Types.TransformSource.Live(_, i) -> Some i
                          | _ -> None

                      Expect.equal (initialOf bs) (initialOf es) "both spellings start from the same initial snapshot"
                  | _ -> failtest "both labels must be Transform-bound"
              | _ -> failtest "both must be Badge nodes"

              // Each still re-encodes to ITSELF — the preserved binding is the
              // wire dialect, so neither spelling is normalised into the other.
              Expect.notEqual
                  (CanonicalJson.encodeNode bare)
                  (CanonicalJson.encodeNode empty)
                  "the two spellings round-trip byte-for-byte, each to its own bytes"
          } ]
