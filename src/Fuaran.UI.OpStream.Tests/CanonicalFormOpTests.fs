module Fuaran.UI.OpStream.Tests.CanonicalFormOpTests

// ─── §16 canonical-form projection across the WHOLE op codec ────────────
//
// `CanonicalJson` is the tier's POLICY encoder: the canonical wire spells
// as ABSENCE three things the type system can also spell explicitly — a
// control `value` that is exactly its context's auto-binding
// (`Filter(<chip name>, None)` on a chip, `State(<field id>, <typed
// placeholder>)` on a form field), an all-default `SemanticStyle`, and an
// all-`None` `StateBehaviour`. `Introspect.canonicalForm` performs that
// projection, and every op-codec appender that splices a Node-bearing
// payload MUST route through it.
//
// The defect these tests pin: `stateBehaviourAppender` did not, while
// `StateBehaviour` carries `onLoading` / `onEmpty` **Node** payloads — so a
// `TreeOp.UpdateState` whose state node was a `Filters` strip with an
// explicit self-referential auto-binding emitted PRE-canonical bytes (the
// `value` key present) on the one path that skipped the projection.
//
// Found by the Python↔F# fuzz-sample exchange, which hit it at only ~1-4
// per 600 samples — probabilistic, so these deterministic pins exist so a
// regression fails on every run rather than on an unlucky draw. One test
// per appender that splices a Node-bearing payload, so a future appender
// added without the projection has a sibling to be measured against.

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Tests.TestSupport

/// A `Filters` strip whose single chip carries an EXPLICIT `value` binding
/// that is exactly the chip's own auto binding — `Filter(<own name>, None)`.
/// WIRE_FORMAT.md §"filters-unification": "the encoder symmetrically
/// **omits** a `value` that is exactly that auto binding".
let private filtersWithExplicitAutoBinding (id: string) : Node<TestMsg> =
    Fuaran.filters
        id
        [ { Name = "status"
            Label = TextSource.Literal "Status"
            Kind = FormFieldKind.Text(Some(Binding.Filter("status", None)), None) } ]

/// The canonical rendering of that same strip — the chip with NO `value`
/// key. Anything that emits the strip must produce exactly these bytes.
let private canonicalStripBytes =
    CanonicalJson.encodeNode (filtersWithExplicitAutoBinding "chips")

let private stateWith (node: Node<TestMsg> option) (empty: Node<TestMsg> option) : StateBehaviour<TestMsg> =
    { OnLoading = node
      OnEmpty = empty
      OnError = None }

/// The chip's auto binding on the wire. Its presence anywhere in an
/// encoding is the defect; its absence is the canonical form.
let private explicitValueMarker = "\"value\":{\"$type\":\"Filter\""

[<Tests>]
let tests =
    testList
        "CanonicalJson §16 canonical form — every Node-bearing op payload"
        [
          // The pin for the defect. `UpdateState` is the only op whose Node
          // payload rides inside a `StateBehaviour`, and it was the only
          // appender bypassing the projection.
          test "UpdateState canonicalises a Filters node in state.onLoading" {
              let op =
                  TreeOp.UpdateState(NodeId "target", stateWith (Some(filtersWithExplicitAutoBinding "chips")) None)

              let encoded = CanonicalJson.encodeOp op

              Expect.isFalse
                  (encoded.Contains explicitValueMarker)
                  "state.onLoading must be emitted in §16 canonical form — the chip's own auto binding is spelled as ABSENCE, never as an explicit `value`"

              Expect.stringContains
                  encoded
                  canonicalStripBytes
                  "the spliced onLoading node must be byte-identical to the strip's own canonical encoding"
          }

          test "UpdateState canonicalises a Filters node in state.onEmpty" {
              let op =
                  TreeOp.UpdateState(NodeId "target", stateWith None (Some(filtersWithExplicitAutoBinding "chips")))

              let encoded = CanonicalJson.encodeOp op

              Expect.isFalse
                  (encoded.Contains explicitValueMarker)
                  "state.onEmpty must be emitted in §16 canonical form"

              Expect.stringContains
                  encoded
                  canonicalStripBytes
                  "the spliced onEmpty node must be byte-identical to the strip's own canonical encoding"
          }

          // The fuzz exchange found this inside nested `Batch` ops as often
          // as at the top level — the projection must survive the recursion.
          test "UpdateState nested in a Batch is canonicalised too" {
              let inner =
                  TreeOp.UpdateState(NodeId "target", stateWith (Some(filtersWithExplicitAutoBinding "chips")) None)

              let encoded = CanonicalJson.encodeOp (TreeOp.Batch [ TreeOp.Batch [ inner ] ])

              Expect.isFalse (encoded.Contains explicitValueMarker) "the §16 projection must hold at every Batch depth"
          }

          // A non-auto binding is NOT the auto shape and must survive: the
          // omission rule keys on the EXACT auto binding, so a chip bound to
          // a different filter keeps its `value` on the wire. Without this,
          // "canonicalise" could be satisfied by deleting every value.
          test "UpdateState keeps a value binding that is NOT the chip's auto binding" {
              let strip =
                  Fuaran.filters
                      "chips"
                      [ { Name = "status"
                          Label = TextSource.Literal "Status"
                          Kind = FormFieldKind.Text(Some(Binding.Filter("someOtherFilter", None)), None) } ]

              let encoded =
                  CanonicalJson.encodeOp (TreeOp.UpdateState(NodeId "target", stateWith (Some strip) None))

              Expect.stringContains
                  encoded
                  explicitValueMarker
                  "a value binding that is not the chip's own auto binding is meaningful and must stay on the wire"
          }

          // The two appenders that already routed correctly, pinned beside
          // the one that did not — so the trio is checked as a set and a new
          // Node-bearing appender has a shape to match.
          test "InsertChild / ReplaceRoot canonicalise their Node payload" {
              let strip = filtersWithExplicitAutoBinding "chips"

              for name, op in
                  [ "InsertChild", TreeOp.InsertChild(NodeId "parent", strip)
                    "ReplaceRoot", TreeOp.ReplaceRoot strip ] do
                  let encoded = CanonicalJson.encodeOp op

                  Expect.isFalse
                      (encoded.Contains explicitValueMarker)
                      (sprintf "%s must emit its Node payload in §16 canonical form" name)
          }

          test "EditNode canonicalises its NodeKind payload" {
              let encoded =
                  CanonicalJson.encodeOp (
                      TreeOp.EditNode(NodeId "target", (filtersWithExplicitAutoBinding "chips").Kind)
                  )

              Expect.isFalse
                  (encoded.Contains explicitValueMarker)
                  "EditNode.newKind must be emitted in §16 canonical form"
          }

          // The whole point of the projection: the op codec and the node
          // codec must agree on what canonical means, since a host decodes
          // one and re-encodes through the other.
          test "an op-spliced node is byte-identical to the node codec's own output" {
              let strip = filtersWithExplicitAutoBinding "chips"

              Expect.stringContains
                  (CanonicalJson.encodeOp (TreeOp.ReplaceRoot strip))
                  (CanonicalJson.encodeNode strip)
                  "the op codec must splice exactly what the node codec emits"
          } ]
