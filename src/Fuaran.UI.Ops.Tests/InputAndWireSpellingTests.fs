module Fuaran.UI.Tests.InputAndWireSpelling

// ============================================================================
//  Wire-spelling acceptance on `UpdateProp.path`, and the Input family's
//  field-level UpdateProp surface.
//
//  Both close defects a controlled edit measurement surfaced (2026-07-26), where
//  two independent model families were refused on ops that were correct in
//  intent:
//
//    * `UpdateProp.path` resolved PascalCase record-field names whilst the whole
//      rest of the wire format is camelCase, so `"subtext"` was refused
//      `FieldNotFound` where `"Subtext"` applied. An author consistent with the
//      wire everywhere else was penalised for that consistency.
//    * The entire `NodeKind.Input` family reported `NotSupportedYet` for every
//      top-level path, so a button's label could not be changed without swapping
//      the whole node via `EditNode` — whilst `Introspect.availableFields` was
//      advertising `Label` as available, sending the author into a retry loop.
//
//  The last test in this file is the standing guard against a recurrence of the
//  second class: anything the hint advertises must be reachable.
//
//  Assertion style: `TextSource` and `Node<'Msg>` carry function-typed elements,
//  so neither supports structural equality. Every assertion below therefore
//  compares a projection (the literal string, the printed tree) rather than the
//  value itself.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops

type Msg =
    | Submitted
    | Exported

// F# 10 nullness types `box _` as `obj | null`; these payloads are always
// non-null (the test controls them), mirroring OpsApplyTests' helper.
let private nn (value: 'T) : obj = box value |> Unchecked.nonNull

let private literalOf (ts: TextSource) : string =
    match ts with
    | TextSource.Literal s -> s
    | other -> failtestf "Expected TextSource.Literal, got %A" other

let private someLiteralOf (ts: TextSource option) : string =
    match ts with
    | Some t -> literalOf t
    | None -> failtest "Expected Some TextSource, got None"

let private revenueMetric: Node<Msg> =
    Fuaran.metric
        "revenue-metric"
        { Defaults.metric with
            Label = TextSource.Literal "Revenue"
            Value = Binding.Static 142500.0
            Format = CellFormat.Currency "GBP"
            Tone = ToneVariant.Brand
            Subtext = Some(TextSource.Literal "vs last quarter") }

let private exportButton: Node<Msg> =
    Fuaran.button
        "export-button"
        { Defaults.button<Msg> with
            Label = TextSource.Literal "Export"
            Variant = ButtonVariant.Secondary }

let private regionSelect: Node<Msg> =
    Fuaran.select
        "region-select"
        { Defaults.select<Msg> with
            Label = TextSource.Literal "Region" }

let private evidenceUpload: Node<Msg> =
    Fuaran.fileUpload
        "evidence-upload"
        { Defaults.fileUpload<Msg> with
            Label = TextSource.Literal "Attach evidence" }

let private signupForm: Node<Msg> =
    Fuaran.form
        "signup-form"
        { Defaults.form<Msg> with
            SubmitLabel = TextSource.Literal "Sign up"
            Fields =
                [ { Defaults.formField<Msg> with
                      Id = "name"
                      Label = TextSource.Literal "Name" } ] }

let private controlPanel: Node<Msg> =
    Fuaran.dashboard
        "control-panel"
        { Defaults.dashboard<Msg> with
            Children = [ revenueMetric; exportButton; regionSelect; evidenceUpload; signupForm ] }

let private applyTo (root: Node<Msg>) (op: TreeOp<Msg>) : Node<Msg> =
    match Apply.apply op root with
    | Ok updated -> updated
    | Error err -> failtestf "Expected Ok, got %A: %s" err.Code err.Message

let private update (nodeId: string) (path: string) (value: 'T) (root: Node<Msg>) : Node<Msg> =
    applyTo root (TreeOp.UpdateProp(NodeId nodeId, path, PropValue.Native(nn value)))

let private kindAt (root: Node<Msg>) (nodeId: string) : NodeKind<Msg> =
    match Introspect.findNode (NodeId nodeId) root with
    | Some node -> node.Kind
    | None -> failtestf "No node '%s'" nodeId

let private buttonAt (root: Node<Msg>) (nodeId: string) : ButtonSpec<Msg> =
    match kindAt root nodeId with
    | NodeKind.Button( spec) -> spec
    | other -> failtestf "Expected a Button at '%s', got %A" nodeId other

let private selectAt (root: Node<Msg>) (nodeId: string) : SelectSpec<Msg> =
    match kindAt root nodeId with
    | NodeKind.Select( spec) -> spec
    | other -> failtestf "Expected a Select at '%s', got %A" nodeId other

let private formAt (root: Node<Msg>) (nodeId: string) : FormSpec<Msg> =
    match kindAt root nodeId with
    | NodeKind.Form( spec) -> spec
    | other -> failtestf "Expected a Form at '%s', got %A" nodeId other

let private metricAt (root: Node<Msg>) (nodeId: string) : MetricSpec =
    match kindAt root nodeId with
    | NodeKind.Metric( spec) -> spec
    | other -> failtestf "Expected a Metric at '%s', got %A" nodeId other

[<Tests>]
let wireSpellingTests =
    testList
        "UpdateProp — the camelCase wire spelling is accepted"
        [ test "the camelCase spelling both model families emitted now applies" {
              // Verbatim the refused op: `path:"subtext"` against a Metric.
              let op =
                  TreeOp.UpdateProp(NodeId "revenue-metric", "subtext", PropValue.Native(nn "vs prior quarter"))

              match Apply.apply op controlPanel with
              | Error err -> failtestf "camelCase path refused: %A %s" err.Code err.Message
              | Ok updated ->
                  Expect.equal
                      (someLiteralOf (metricAt updated "revenue-metric").Subtext)
                      "vs prior quarter"
                      "Subtext written"
          }

          test "the PascalCase spelling still applies — a widening, not a swap" {
              let op =
                  TreeOp.UpdateProp(NodeId "revenue-metric", "Subtext", PropValue.Native(nn "vs prior quarter"))

              match Apply.apply op controlPanel with
              | Error err -> failtestf "PascalCase path refused: %A %s" err.Code err.Message
              | Ok _ -> ()
          }

          test "both spellings produce the identical tree" {
              // Compared as printed structure: Node<'Msg> has no structural
              // equality (its handlers are functions), but the printed form is
              // deterministic and is sensitive to every field that matters here.
              let camel = controlPanel |> update "revenue-metric" "tone" ToneVariant.Critical
              let pascal = controlPanel |> update "revenue-metric" "Tone" ToneVariant.Critical
              Expect.equal (sprintf "%A" camel) (sprintf "%A" pascal) "Spelling is not semantics"
          }

          test "a genuinely unknown field is still FieldNotFound" {
              // Canonicalising the first character must not turn a real
              // not-a-field into a silent no-op.
              let op =
                  TreeOp.UpdateProp(NodeId "revenue-metric", "nonesuch", PropValue.Native(nn "x"))

              match Apply.apply op controlPanel with
              | Ok _ -> failtest "Expected FieldNotFound"
              | Error err -> Expect.equal err.Code ApplyErrorCode.FieldNotFound "Unknown stays unknown"
          }

          test "a nested camelCase path resolves too" {
              let updated = controlPanel |> update "signup-form" "fields[0].label" "Full name"

              Expect.equal
                  (literalOf (formAt updated "signup-form").Fields.Head.Label)
                  "Full name"
                  "Nested field written"
          }

          test "a redundant leading 'kind.' prefix is accepted — the wire nests under it" {
              // The other family's shape. `path` is rooted inside the kind spec,
              // but the serialised tree puts those fields under a `kind` object,
              // so addressing them by the path they visibly occupy is a fair
              // reading of the same document.
              let updated =
                  controlPanel |> update "revenue-metric" "kind.subtext" "vs prior quarter"

              Expect.equal
                  (someLiteralOf (metricAt updated "revenue-metric").Subtext)
                  "vs prior quarter"
                  "kind-prefixed path written"
          }

          test "'kind.' prefixed and bare paths produce the identical tree" {
              let prefixed =
                  controlPanel |> update "revenue-metric" "kind.tone" ToneVariant.Critical

              let bare = controlPanel |> update "revenue-metric" "tone" ToneVariant.Critical
              Expect.equal (sprintf "%A" prefixed) (sprintf "%A" bare) "The prefix is redundant, not semantic"
          }

          test "a bare 'kind' path with nothing after it is still refused" {
              // Stripping applies only to a LEADING segment with something after
              // it: `kind` alone addresses the spec container wholesale, which is
              // EditNode's job, and must not silently become a no-op.
              let op =
                  TreeOp.UpdateProp(NodeId "revenue-metric", "kind", PropValue.Native(nn "x"))

              match Apply.apply op controlPanel with
              | Ok _ -> failtest "Expected a refusal for a bare 'kind' path"
              | Error _ -> ()
          }

          test "a non-leading 'Kind' segment is untouched by the prefix strip" {
              // `Fields[i].Kind` is closure-bearing and deliberately never
              // addressable; the strip must not accidentally make it reachable.
              let op =
                  TreeOp.UpdateProp(NodeId "signup-form", "fields[0].kind", PropValue.Native(nn "x"))

              match Apply.apply op controlPanel with
              | Ok _ -> failtest "Expected a refusal for a closure-bearing nested field"
              | Error _ -> ()
          } ]

[<Tests>]
let inputFieldUpdateTests =
    testList
        "UpdateProp — the Input family is field-addressable"
        [ test "'label' changes a Button's label — the ordinary edit" {
              let updated = controlPanel |> update "export-button" "label" "Start free trial"
              Expect.equal (literalOf (buttonAt updated "export-button").Label) "Start free trial" "Label written"
          }

          test "a Button's variant and tooltip are writable" {
              let updated =
                  controlPanel
                  |> update "export-button" "variant" ButtonVariant.Primary
                  |> update "export-button" "tooltip" "Download a CSV"

              let spec = buttonAt updated "export-button"
              Expect.equal spec.Variant ButtonVariant.Primary "Variant written"
              Expect.equal (someLiteralOf spec.Tooltip) "Download a CSV" "Tooltip written"
          }

          test "editing one Button leaves its siblings untouched" {
              // The collateral-damage property: a surgical op must stay surgical.
              let updated = controlPanel |> update "export-button" "label" "Start free trial"
              Expect.equal (literalOf (selectAt updated "region-select").Label) "Region" "Sibling Select unchanged"
              Expect.equal (literalOf (formAt updated "signup-form").SubmitLabel) "Sign up" "Sibling Form unchanged"
          }

          test "Select.label and Select.placeholder are writable" {
              let updated =
                  controlPanel
                  |> update "region-select" "label" "Territory"
                  |> update "region-select" "placeholder" "Pick one"

              let spec = selectAt updated "region-select"
              Expect.equal (literalOf spec.Label) "Territory" "Label written"
              Expect.equal (someLiteralOf spec.Placeholder) "Pick one" "Placeholder written"
          }

          test "FileUpload.multiple is writable" {
              let updated = controlPanel |> update "evidence-upload" "multiple" true

              match kindAt updated "evidence-upload" with
              | NodeKind.FileUpload( spec) -> Expect.isTrue spec.Multiple "Multiple written"
              | other -> failtestf "Expected a FileUpload, got %A" other
          }

          test "Form.submitLabel is writable" {
              let updated = controlPanel |> update "signup-form" "submitLabel" "Create account"
              Expect.equal (literalOf (formAt updated "signup-form").SubmitLabel) "Create account" "SubmitLabel written"
          } ]

[<Tests>]
let divisionOfLabourTests =
    testList
        "UpdateProp — the op division of labour is preserved, not widened"
        [ test "a closure-bearing field reports NotSupportedYet, not FieldNotFound" {
              // OnClick is an Action. It must not report UnknownField (which
              // would claim the field does not exist), and must not silently
              // accept a value it cannot represent.
              let op =
                  TreeOp.UpdateProp(NodeId "export-button", "onClick", PropValue.Native(nn "doSomething"))

              match Apply.apply op controlPanel with
              | Ok _ -> failtest "Expected NotSupportedYet for a closure-bearing field"
              | Error err -> Expect.equal err.Code ApplyErrorCode.PathNotSupportedYet "Names the right refusal"
          }

          test "a Binding slot reports NotSupportedYet — ReplaceBinding owns it" {
              let op =
                  TreeOp.UpdateProp(NodeId "region-select", "value", PropValue.Native(nn "north"))

              match Apply.apply op controlPanel with
              | Ok _ -> failtest "Expected NotSupportedYet for a Binding slot"
              | Error err -> Expect.equal err.Code ApplyErrorCode.PathNotSupportedYet "Names the right refusal"
          }

          test "no field the Button hint advertises is unreachable" {
              // The standing guard. A hint that names a field UpdateProp cannot
              // find is worse than no hint: it manufactures the retry it exists
              // to prevent, which is exactly what both model families hit.
              for field in Introspect.availableFields exportButton.Kind do
                  let op =
                      TreeOp.UpdateProp(NodeId "export-button", field, PropValue.Native(nn "probe"))

                  match Apply.apply op controlPanel with
                  | Ok _ -> ()
                  | Error err ->
                      // A TypeMismatch is fine — the probe value is a bare
                      // string. FieldNotFound is not: that is the hint lying.
                      Expect.notEqual
                          err.Code
                          ApplyErrorCode.FieldNotFound
                          $"available_fields advertises '%s{field}' but UpdateProp cannot find it"
          } ]
