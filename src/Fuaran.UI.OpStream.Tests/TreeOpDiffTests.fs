module Fuaran.UI.OpStream.Tests.TreeOpDiffTests

// ─── Phase 152 Track A: TreeOp-emitting diff round-trip ─────────
//
// The load-bearing contract: `diff a b` folded through `Apply.apply`
// against `a` reconstructs `b` (canonical-JSON equality — the closure-
// safe surface). Plus the coarse-floor classification cases: leaf
// content change, child add / remove / reorder, kind-discriminator
// change, nested change, style change, identity, and Batch-wrapping.

open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.OpStream.Tests.TestSupport

let private canonical (n: Node<TestMsg>) : string = CanonicalJson.encodeNode n

/// Fold `diff a b` through the apply engine against `a` and assert the
/// result is canonical-equal to `b`. Returns the op list for further
/// shape assertions.
let private roundTrip (a: Node<TestMsg>) (b: Node<TestMsg>) : TreeOp<TestMsg> list =
    let ops = TreeOpDiff.diff a b

    let folded =
        ops
        |> List.fold
            (fun (t: Node<TestMsg>) op ->
                match Apply.apply op t with
                | Ok t' -> t'
                | Error e -> failwithf "apply failed for %A: %A" op e)
            a

    Expect.equal (canonical folded) (canonical b) "diff(a,b) folded through apply must reconstruct b"
    ops

let private dash (children: Node<TestMsg> list) : Node<TestMsg> =
    Fuaran.dashboard
        "dash"
        { Defaults.dashboard<TestMsg> with
            Children = children }

[<Tests>]
let tests =
    testList
        "TreeOpDiff (Phase 152 Track A)"
        [ test "identical trees diff to no ops" {
              let a = dash [ Fuaran.markdown "m" "hello" ]
              Expect.isEmpty (TreeOpDiff.diff a a) "no change → no ops"
          }

          test "leaf text change round-trips via granular UpdateProp (not EditNode)" {
              let a = dash [ Fuaran.markdown "m" "before" ]
              let b = dash [ Fuaran.markdown "m" "after" ]
              let ops = roundTrip a b
              // A single text-field change is now a granular UpdateProp, not a
              // wholesale EditNode.
              Expect.isTrue
                  (ops
                   |> List.exists (function
                       | TreeOp.UpdateProp(NodeId "m", "Text", _) -> true
                       | _ -> false))
                  "the changed text field emits UpdateProp(\"Text\")"

              Expect.isFalse
                  (ops
                   |> List.exists (function
                       | TreeOp.EditNode _ -> true
                       | _ -> false))
                  "a single field change does not wholesale-replace the kind"
          }

          test "Metric single-scalar change → just that UpdateProp" {
              let metric tone =
                  Fuaran.metric
                      "rev"
                      { Defaults.metric with
                          Label = TextSource.Literal "Revenue"
                          Tone = tone }

              let a = dash [ metric ToneVariant.Default ]
              let b = dash [ metric ToneVariant.Brand ]
              let ops = roundTrip a b

              // Only Tone drifted → exactly one UpdateProp("Tone"), no others.
              let updateProps =
                  ops
                  |> List.choose (function
                      | TreeOp.UpdateProp(NodeId "rev", field, _) -> Some field
                      | _ -> None)

              Expect.equal updateProps [ "Tone" ] "only the changed scalar field is patched"

              Expect.isFalse
                  (ops
                   |> List.exists (function
                       | TreeOp.EditNode _ -> true
                       | _ -> false))
                  "no wholesale EditNode for a single-field change"
          }

          test "Metric Source binding change → granular ReplaceBinding (round-trips)" {
              let metric src =
                  Fuaran.metric
                      "rev"
                      { Defaults.metric with
                          Label = TextSource.Literal "Revenue"
                          Value = src }

              let a = dash [ metric (Binding.Static(Some 1.0)) ]
              let b = dash [ metric (Binding.Static(Some 2.0)) ]
              let ops = roundTrip a b

              Expect.isTrue
                  (ops
                   |> List.exists (function
                       | TreeOp.ReplaceBinding(NodeId "rev", "Value", _) -> true
                       | _ -> false))
                  "a covered binding slot emits ReplaceBinding"

              Expect.isFalse
                  (ops
                   |> List.exists (function
                       | TreeOp.EditNode _ -> true
                       | _ -> false))
                  "no wholesale EditNode for a covered binding-slot change"
          }

          test "container scalar change (Stack.Orientation) → granular UpdateProp" {
              let stack orientation =
                  Fuaran.stack
                      "row"
                      { Defaults.stack<TestMsg> with
                          Orientation = orientation
                          Children = [ Fuaran.markdown "k" "kid" ] }

              let a = dash [ stack Orientation.Vertical ]
              let b = dash [ stack Orientation.Horizontal ]
              let ops = roundTrip a b

              Expect.isTrue
                  (ops
                   |> List.exists (function
                       | TreeOp.UpdateProp(NodeId "row", "Orientation", _) -> true
                       | _ -> false))
                  "a container scalar field is patched granularly"

              Expect.isFalse
                  (ops
                   |> List.exists (function
                       | TreeOp.EditNode _ -> true
                       | _ -> false))
                  "the container is not wholesale-replaced (children + own fields preserved)"
          }

          test "uncovered field (Metric.Trend option) falls back to the EditNode floor" {
              // Trend is an optional binding slot — deliberately NOT covered by
              // the field-level extractor, so a Trend change must fall back to
              // EditNode via the verify guard and still round-trip.
              let metric trend =
                  Fuaran.metric
                      "rev"
                      { Defaults.metric with
                          Label = TextSource.Literal "Revenue"
                          Trend = trend }

              let a = dash [ metric None ]
              let b = dash [ metric (Some(Binding.Static(Some 0.1))) ]
              let ops = roundTrip a b

              Expect.isTrue
                  (ops
                   |> List.exists (function
                       | TreeOp.EditNode(NodeId "rev", _) -> true
                       | _ -> false))
                  "an uncovered field change falls back to EditNode (verify guard)"
          }

          test "child insertion round-trips" {
              let a = dash [ Fuaran.markdown "l" "left" ]
              let b = dash [ Fuaran.markdown "l" "left"; Fuaran.markdown "r" "right" ]
              let ops = roundTrip a b

              Expect.isTrue
                  (ops
                   |> List.exists (function
                       | TreeOp.InsertChild(NodeId "dash", _) -> true
                       | _ -> false))
                  "new child is inserted under dash"
          }

          test "child removal round-trips" {
              let a = dash [ Fuaran.markdown "l" "left"; Fuaran.markdown "r" "right" ]
              let b = dash [ Fuaran.markdown "l" "left" ]
              let ops = roundTrip a b

              Expect.isTrue
                  (ops
                   |> List.exists (function
                       | TreeOp.RemoveNode(NodeId "r") -> true
                       | _ -> false))
                  "the dropped child is removed"
          }

          test "child reorder round-trips via ReorderChildren" {
              let a = dash [ Fuaran.markdown "l" "left"; Fuaran.markdown "r" "right" ]
              let b = dash [ Fuaran.markdown "r" "right"; Fuaran.markdown "l" "left" ]
              let ops = roundTrip a b

              Expect.isTrue
                  (ops
                   |> List.exists (function
                       | TreeOp.ReorderChildren(NodeId "dash", [ NodeId "r"; NodeId "l" ]) -> true
                       | _ -> false))
                  "sibling reorder emits ReorderChildren in b's order"
          }

          test "simultaneous add + remove + reorder round-trips" {
              let a =
                  dash [ Fuaran.markdown "a" "A"; Fuaran.markdown "b" "B"; Fuaran.markdown "c" "C" ]
              // drop b, add d, and reorder.
              let b =
                  dash [ Fuaran.markdown "d" "D"; Fuaran.markdown "c" "C"; Fuaran.markdown "a" "A" ]

              roundTrip a b |> ignore
          }

          test "cross-parent move round-trips via MoveNode (identity-preserving)" {
              let withKids id kids =
                  Fuaran.stack
                      id
                      { Defaults.stack<TestMsg> with
                          Children = kids }

              // 'x' moves from s1 to s2.
              let a = dash [ withKids "s1" [ Fuaran.markdown "x" "moving" ]; withKids "s2" [] ]
              let b = dash [ withKids "s1" []; withKids "s2" [ Fuaran.markdown "x" "moving" ] ]
              let ops = roundTrip a b

              Expect.isTrue
                  (ops
                   |> List.exists (function
                       | TreeOp.MoveNode(NodeId "x", NodeId "s2") -> true
                       | _ -> false))
                  "a cross-parent move emits MoveNode to the new parent"

              // It must NOT be expressed as a destroy+recreate (which would
              // lose the moved node's DOM identity / focus / scroll).
              Expect.isFalse
                  (ops
                   |> List.exists (function
                       | TreeOp.RemoveNode(NodeId "x") -> true
                       | _ -> false))
                  "the moved node is relocated, not removed"
          }

          test "a move with a simultaneous content change round-trips" {
              let withKids id kids =
                  Fuaran.stack
                      id
                      { Defaults.stack<TestMsg> with
                          Children = kids }

              let a = dash [ withKids "s1" [ Fuaran.markdown "x" "old" ]; withKids "s2" [] ]
              let b = dash [ withKids "s1" []; withKids "s2" [ Fuaran.markdown "x" "new" ] ]
              // Moved AND its text changed — both must round-trip.
              roundTrip a b |> ignore
          }

          test "kind-discriminator change round-trips (markdown → stack)" {
              let a = dash [ Fuaran.markdown "x" "leaf" ]

              let b =
                  dash
                      [ Fuaran.stack
                            "x"
                            { Defaults.stack<TestMsg> with
                                Children = [ Fuaran.markdown "inner" "deep" ] } ]

              let ops = roundTrip a b

              Expect.isTrue
                  (ops
                   |> List.exists (function
                       | TreeOp.EditNode(NodeId "x", _) -> true
                       | _ -> false))
                  "kind swap emits EditNode (bringing the new subtree)"
          }

          test "nested content change recurses (no wholesale parent replace)" {
              let a =
                  dash
                      [ Fuaran.stack
                            "s"
                            { Defaults.stack<TestMsg> with
                                Children = [ Fuaran.markdown "m" "old" ] } ]

              let b =
                  dash
                      [ Fuaran.stack
                            "s"
                            { Defaults.stack<TestMsg> with
                                Children = [ Fuaran.markdown "m" "new" ] } ]

              let ops = roundTrip a b
              // The stack's own content is unchanged → no EditNode on dash or s;
              // only the inner markdown is replaced.
              Expect.isFalse
                  (ops
                   |> List.exists (function
                       | TreeOp.EditNode(NodeId "dash", _)
                       | TreeOp.EditNode(NodeId "s", _) -> true
                       | _ -> false))
                  "unchanged containers are not wholesale-replaced"

              Expect.isTrue
                  (ops
                   |> List.exists (function
                       | TreeOp.UpdateProp(NodeId "m", "Text", _) -> true
                       | _ -> false))
                  "only the changed leaf is patched (granular UpdateProp)"
          }

          test "style change round-trips (UpdateStyle)" {
              let baseMd = Fuaran.markdown "m" "text"

              let toned =
                  { baseMd with
                      Style =
                          { baseMd.Style with
                              Tone = ToneVariant.Brand } }

              let a = dash [ baseMd ]
              let b = dash [ toned ]
              let ops = roundTrip a b

              Expect.isTrue
                  (ops
                   |> List.exists (function
                       | TreeOp.UpdateStyle(NodeId "m", _) -> true
                       | _ -> false))
                  "style drift emits UpdateStyle"

              // Refinement: a style-only change (kind shape unchanged) must
              // NOT wholesale-replace the node — no EditNode is emitted.
              Expect.isFalse
                  (ops
                   |> List.exists (function
                       | TreeOp.EditNode _ -> true
                       | _ -> false))
                  "style-only change does not re-send the kind via EditNode"
          }

          test "diffBatched wraps a multi-op diff in a single atomic Batch" {
              let a = dash [ Fuaran.markdown "l" "left" ]
              let b = dash [ Fuaran.markdown "l" "LEFT"; Fuaran.markdown "r" "right" ]

              match TreeOpDiff.diffBatched a b with
              | [ TreeOp.Batch inner ] -> Expect.isGreaterThan (List.length inner) 1 "≥2 ops wrapped in one Batch"
              | other -> failtestf "expected a single Batch, got %A" other

              // The batched form also round-trips.
              match Apply.apply (TreeOp.Batch(TreeOpDiff.diff a b)) a with
              | Ok folded -> Expect.equal (canonical folded) (canonical b) "batched diff reconstructs b"
              | Error e -> failtestf "batch apply failed: %A" e
          }

          test "diffBatched passes a single op through unwrapped" {
              let a = dash [ Fuaran.markdown "m" "before" ]
              let b = dash [ Fuaran.markdown "m" "before"; Fuaran.markdown "n" "new" ]
              // one InsertChild only.
              match TreeOpDiff.diffBatched a b with
              | [ TreeOp.InsertChild _ ] -> ()
              | [ TreeOp.Batch _ ] -> failtest "single op should not be wrapped in Batch"
              | other -> failtestf "expected a lone InsertChild, got %A" other
          }

          // ── Task 17: emitted UpdateProp payloads are log-faithful (Wire) ─────
          //
          // Before task 17 every field emitted `PropValue.Native` — apply-exact
          // in-process, but a non-scalar Native (a TextSource, a variant DU, …)
          // serialises to the `"<opaque>"` sentinel in the op log, so the op could
          // not be replayed from the log. These assert the wire-encodable fields
          // now emit `PropValue.Wire` and serialise faithfully via `encodeOp`.

          test "a TextSource.Literal change emits a log-faithful Wire payload" {
              let a = dash [ Fuaran.markdown "m" "before" ]
              let b = dash [ Fuaran.markdown "m" "after" ]
              let ops = roundTrip a b

              let payload =
                  ops
                  |> List.tryPick (function
                      | TreeOp.UpdateProp(NodeId "m", "Text", pv) -> Some pv
                      | _ -> None)

              match payload with
              | Some(PropValue.Wire(JStr "after")) -> ()
              | other -> failtestf "expected Wire(JStr \"after\"), got %A" other

              // The op-log serialisation carries the real text, not the sentinel.
              match payload with
              | Some pv ->
                  let json = CanonicalJson.encodeOp (TreeOp.UpdateProp(NodeId "m", "Text", pv))
                  Expect.isTrue (json.Contains "after") "the serialised op carries the literal text"
                  Expect.isFalse (json.Contains "<opaque>") "no opaque sentinel in the serialised op"
              | None -> failtest "expected an UpdateProp(\"Text\")"
          }

          test "a variant-DU change (Metric.Tone) emits Wire with the canonical case name" {
              let metric tone =
                  Fuaran.metric
                      "rev"
                      { Defaults.metric with
                          Label = TextSource.Literal "Revenue"
                          Tone = tone }

              let a = dash [ metric ToneVariant.Default ]
              let b = dash [ metric ToneVariant.Brand ]
              let ops = roundTrip a b

              match
                  ops
                  |> List.tryPick (function
                      | TreeOp.UpdateProp(NodeId "rev", "Tone", pv) -> Some pv
                      | _ -> None)
              with
              | Some(PropValue.Wire(JStr "Brand")) -> ()
              | other -> failtestf "expected Wire(JStr \"Brand\"), got %A" other
          }

          test "an int field change (Heading.Level) emits Wire JInt" {
              let heading lvl =
                  Fuaran.heading
                      "h"
                      { Defaults.heading with
                          Level = lvl
                          Text = TextSource.Literal "Title" }

              let a = dash [ heading 1 ]
              let b = dash [ heading 3 ]
              let ops = roundTrip a b

              match
                  ops
                  |> List.tryPick (function
                      | TreeOp.UpdateProp(NodeId "h", "Level", pv) -> Some pv
                      | _ -> None)
              with
              | Some(PropValue.Wire(JInt 3)) -> ()
              | other -> failtestf "expected Wire(JInt 3), got %A" other
          }

          // ── Task 18: the serialise → replay leg the in-process verify can't see ─
          //
          // `roundTrip` above applies the diff's ops IN-PROCESS (typed payloads take
          // Apply's unbox fast path). These pin the LOG leg: every emitted op must
          // `encodeOp → decodeOp → apply` to the same tree — the property that makes
          // "log-replayable diffs" true, and that catches (a) a `Some → None` option
          // transition boxing to a JSON null the decoder refuses, and (b) a Native
          // payload's "<opaque>" sentinel silently re-ingesting as literal data.

          test "diff ops survive the full log round-trip (encodeOp → decodeOp → apply)" {
              // Node<obj> trees: decodeOp yields TreeOp<obj>, so the replay fold is
              // typed at obj — same shape the op-stream replay engine uses.
              let dashO (children: Node<obj> list) : Node<obj> =
                  Fuaran.dashboard
                      "dash"
                      { Defaults.dashboard with
                          Children = children }

              let pairs: (string * Node<obj> * Node<obj>) list =
                  [ "text literal", dashO [ Fuaran.markdown "m" "before" ], dashO [ Fuaran.markdown "m" "after" ]

                    "variant DU + int",
                    dashO
                        [ Fuaran.heading
                              "h"
                              { Defaults.heading with
                                  Level = 1
                                  Text = TextSource.Literal "T" } ],
                    dashO
                        [ Fuaran.heading
                              "h"
                              { Defaults.heading with
                                  Level = 3
                                  Text = TextSource.Literal "T"
                                  Variant = HeadingVariant.Eyebrow } ]

                    "optional field Some → Some",
                    dashO
                        [ Fuaran.callout
                              "c"
                              { Defaults.callout with
                                  Body = TextSource.Literal "body"
                                  Heading = Some(TextSource.Literal "old") } ],
                    dashO
                        [ Fuaran.callout
                              "c"
                              { Defaults.callout with
                                  Body = TextSource.Literal "body"
                                  Heading = Some(TextSource.Literal "new") } ]

                    "structural insert + remove + reorder",
                    dashO [ Fuaran.markdown "x" "X"; Fuaran.markdown "y" "Y" ],
                    dashO [ Fuaran.markdown "z" "Z"; Fuaran.markdown "y" "Y" ] ]

              for (label, a, b) in pairs do
                  let ops = TreeOpDiff.diff a b

                  let replayed =
                      ops
                      |> List.fold
                          (fun (t: Node<obj>) op ->
                              let json = CanonicalJson.encodeOp op

                              match Fuaran.UI.Ops.JsonDecode.decodeOp json with
                              | Error e -> failtestf "%s: decodeOp failed on logged op %s — %s" label json e.Message
                              | Ok decoded ->
                                  match Apply.apply decoded t with
                                  | Ok t' -> t'
                                  | Error e -> failtestf "%s: replay apply failed for %s — %A" label json e)
                          a

                  Expect.equal
                      (CanonicalJson.encodeNode replayed)
                      (CanonicalJson.encodeNode b)
                      (sprintf "%s: replaying the encoded op log reconstructs b" label)
          }

          test "a Some → None option transition takes the EditNode floor, never a null UpdateProp" {
              let calloutO heading : Node<obj> =
                  Fuaran.callout
                      "c"
                      { Defaults.callout with
                          Body = TextSource.Literal "body"
                          Heading = heading }

              let dashO child : Node<obj> =
                  Fuaran.dashboard
                      "dash"
                      { Defaults.dashboard with
                          Children = [ child ] }

              let a = dashO (calloutO (Some(TextSource.Literal "gone")))
              let b = dashO (calloutO None)
              let ops = TreeOpDiff.diff a b

              // Never an UpdateProp for the un-settable field: boxing None is a CLR
              // null that canonical-encodes as JSON null — an op replay must reject.
              Expect.isFalse
                  (ops
                   |> List.exists (function
                       | TreeOp.UpdateProp(_, "Heading", _) -> true
                       | _ -> false))
                  "no UpdateProp(Heading) for a Some → None transition"

              Expect.isTrue
                  (ops
                   |> List.exists (function
                       | TreeOp.EditNode(NodeId "c", _) -> true
                       | _ -> false))
                  "the transition takes the wholesale EditNode floor"

              // And the floor itself is log-replayable end-to-end.
              let replayed =
                  ops
                  |> List.fold
                      (fun (t: Node<obj>) op ->
                          match Fuaran.UI.Ops.JsonDecode.decodeOp (CanonicalJson.encodeOp op) with
                          | Error e -> failtestf "floor op failed to decode: %s" e.Message
                          | Ok decoded ->
                              match Apply.apply decoded t with
                              | Ok t' -> t'
                              | Error e -> failtestf "floor op failed to apply: %A" e)
                      a

              Expect.equal
                  (CanonicalJson.encodeNode replayed)
                  (CanonicalJson.encodeNode b)
                  "floor round-trips via the log"
          }

          test "a logged Native payload replays as a loud decode error, not silent corruption" {
              // A non-scalar Native serialises as the "<opaque>" sentinel; §16's
              // bare-string leniency would otherwise re-ingest it as live literal
              // text. The decoder's sentinel gate must refuse it by name.
              let native =
                  TreeOp.UpdateProp(
                      NodeId "m",
                      "Text",
                      PropValue.Native(box (TextSource.Literal "x") |> Unchecked.nonNull)
                  )

              let json = CanonicalJson.encodeOp native
              Expect.isTrue (json.Contains "<opaque>") "a non-scalar Native serialises as the opaque sentinel"

              match Fuaran.UI.Ops.JsonDecode.decodeOp json with
              | Error e -> Expect.stringContains e.Message "sentinel" "the rejection names the reserved-sentinel rule"
              | Ok op -> failtestf "expected a decode rejection for the opaque sentinel, got %A" op
          } ]
