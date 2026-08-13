module Fuaran.UI.JsonDecode.Tests.IdempotenceProperties

// ============================================================================
//  Phase 101 — generative / property-based wire-format fuzzer (properties).
//
//  Complements the fixed-corpus round-trip suite (RoundTripTests.fs) by
//  asserting the canonical byte-stable round-trip invariant over the GENERATED
//  tree-space the 45+10 curated fixtures can't reach:
//
//      encode (decode (encode x)) = encode x        (WIRE_FORMAT.md §1)
//
//  Two FsCheck properties (≥1000 cases each, with shrinking via the
//  Generators.shrink* functions so a failure reports a minimal failing tree),
//  plus a coverage assertion that every DU arm is actually generated (the
//  acceptance-criterion "if no counterexample is found, the generator's
//  case-coverage is asserted").
//
//  A discovered counterexample would be promoted to a permanent corpus fixture
//  in Fixtures.fs + regenerated into wire-format-fixtures/ (the forward-coupling
//  rule). None found at ship time — coverage is asserted instead.
// ============================================================================

open Expecto
open FsCheck
open FsCheck.FSharp
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions

/// The canonical byte-stable round-trip property for a generated Node.
let private nodeRoundTrips (n: Node<obj>) : bool =
    let wire = CanonicalJson.encodeNode n

    match JsonDecode.decodeNodeObj wire with
    | Ok decoded -> CanonicalJson.encodeNode decoded = wire
    | Error _ -> false

let private opRoundTrips (op: TreeOp<obj>) : bool =
    let wire = CanonicalJson.encodeOp op

    match JsonDecode.decodeOp wire with
    | Ok decoded -> CanonicalJson.encodeOp decoded = wire
    | Error _ -> false

/// Throw-on-failure config at ≥1000 cases (FsCheck reports + shrinks the
/// counterexample on failure, which surfaces as an Expecto test failure).
let private config1000 = Config.QuickThrowOnFailure.WithMaxTest(1000)

[<Tests>]
let idempotence =
    testList
        "Fuaran.UI.Ops.JsonDecode — generative idempotence (Phase 101)"
        [ testCase "Node: encode→decode→encode is byte-stable over 1000 generated trees" (fun () ->
              Check.One(config1000, Prop.forAll Generators.nodeArb nodeRoundTrips))

          testCase "TreeOp: encode→decode→encode is byte-stable over 1000 generated ops" (fun () ->
              Check.One(config1000, Prop.forAll Generators.opArb opRoundTrips)) ]

// ─── DU-arm coverage assertion ───────────────────────────────────────────────
//
// Every `$type` discriminator the wire format can carry must appear in the
// generated sample (the acceptance-criterion case-coverage check). Generated
// containers emit all of their sub-arms (a form carries one field of every
// FormFieldKind + an OnSubmit chaining all nine Action arms; a Filters carries
// every FilterKind; a grid carries every CellKindErased + a spread of
// CellFormat), so a single sample of each top kind covers the nested arms; the
// 4000-node / 1000-op sample size makes the top-level + binding-arm coverage
// reliable.

let private requiredNodeTokens: string list =
    [ // NodeKind — the four behavioural categories are flat on the wire and
      // carry no own $type; only the structural primitives have a discriminator
      "\"$type\":\"Custom\""
      "\"$type\":\"ErrorBoundary\""
      "\"$type\":\"FragmentDecl\""
      "\"$type\":\"FragmentRef\""
      // LayoutKind — Phase 390: the retired Dashboard/Stack/GridLayout/Card
      // kinds all collapse to `Box` on the wire (role + layout are payload
      // fields, not the $type discriminator).
      "\"$type\":\"Box\""
      "\"$type\":\"SplitPanel\""
      "\"$type\":\"Tabs\""
      "\"$type\":\"Stepper\""
      "\"$type\":\"SummaryList\""
      "\"$type\":\"Disclosure\""
      // DisplayKind
      "\"$type\":\"Heading\""
      "\"$type\":\"Markdown\""
      "\"$type\":\"Metric\""
      "\"$type\":\"Badge\""
      "\"$type\":\"Sparkline\""
      "\"$type\":\"Callout\""
      "\"$type\":\"Progress\""
      "\"$type\":\"Skeleton\""
      // Phase 821 — the standalone icon-only display kind.
      "\"$type\":\"Icon\""
      "\"$type\":\"LabelValueRow\""
      // InputKind (Filters renders as an items array, no own $type)
      "\"$type\":\"Form\""
      "\"$type\":\"Button\""
      "\"$type\":\"FileUpload\""
      "\"$type\":\"Select\""
      // VisKind
      "\"$type\":\"DataGrid\""
      "\"$type\":\"Chart\""
      // Phase 393 — `Table` is retired; a static read-only table now emits `$type:DataGrid`
      // with a `staticRows` field (covered by the DataGrid token + the `table-1` fixture).
      "\"$type\":\"Map\""
      // FormFieldKind
      "\"$type\":\"Text\""
      "\"$type\":\"Number\""
      "\"$type\":\"Checkbox\""
      "\"$type\":\"Choice\""
      "\"$type\":\"TextArea\""
      "\"$type\":\"RangedNumber\""
      "\"$type\":\"SegmentedChoice\""
      // FilterKind
      "\"$type\":\"Range\""
      // CellKindErased
      "\"$type\":\"Numeric\""
      "\"$type\":\"Editable\""
      "\"$type\":\"ButtonGroup\""
      "\"$type\":\"Link\""
      "\"$type\":\"Pill\""
      // CellFormat
      "\"$type\":\"Currency\""
      "\"$type\":\"Percent\""
      "\"$type\":\"SignificantDigits\""
      // Phase 819 — the Duration + cell RelativeTime formats (the grid
      // column spread carries both deterministically).
      "\"$type\":\"Duration\""
      "\"$type\":\"RelativeTime\""
      // Binding
      "\"$type\":\"Static\""
      "\"$type\":\"Query\""
      "\"$type\":\"Filter\""
      "\"$type\":\"Selection\""
      "\"$type\":\"State\""
      "\"$type\":\"Computed\""
      "\"$type\":\"I18n\""
      "\"$type\":\"Local\""
      // Action (only Navigate is reliably top-level; the rest live in the
      // all-arms OnSubmit chain a generated form carries)
      "\"$type\":\"Dispatch\""
      "\"$type\":\"Call\""
      "\"$type\":\"Notify\""
      "\"$type\":\"Navigate\""
      "\"$type\":\"SetState\""
      "\"$type\":\"AiTool\""
      "\"$type\":\"Chain\""
      "\"$type\":\"CommitLocal\""
      "\"$type\":\"WriteToClipboard\""
      "\"$type\":\"ReadFileBody\"" ]

let private requiredOpTokens: string list =
    [ "\"$type\":\"EditNode\""
      "\"$type\":\"UpdateProp\""
      "\"$type\":\"ReplaceBinding\""
      "\"$type\":\"UpdateStyle\""
      "\"$type\":\"UpdateState\""
      "\"$type\":\"InsertChild\""
      "\"$type\":\"RemoveNode\""
      "\"$type\":\"MoveNode\""
      "\"$type\":\"ReorderChildren\""
      "\"$type\":\"Batch\"" ]

let private missingTokens (haystack: string) (tokens: string list) : string list =
    tokens |> List.filter (fun t -> not (haystack.Contains t))

let private nodeSampleProp (nodes: Node<obj> list) : bool =
    let haystack = nodes |> List.map CanonicalJson.encodeNode |> String.concat "\n"

    match missingTokens haystack requiredNodeTokens with
    | [] -> true
    | missing -> failtestf "DU arms never generated across the Node sample: %A" missing

let private opSampleProp (ops: TreeOp<obj> list) : bool =
    let haystack = ops |> List.map CanonicalJson.encodeOp |> String.concat "\n"

    match missingTokens haystack requiredOpTokens with
    | [] -> true
    | missing -> failtestf "TreeOp arms never generated across the op sample: %A" missing

[<Tests>]
let coverage =
    let cfg = Config.QuickThrowOnFailure.WithMaxTest(1)

    testList
        "Fuaran.UI.Ops.JsonDecode — generator DU-arm coverage (Phase 101)"
        [ testCase "every Node-side DU arm is generated at least once" (fun () ->
              let nodeListArb = Arb.fromGen (Gen.listOfLength 4000 Generators.genNode)
              Check.One(cfg, Prop.forAll nodeListArb nodeSampleProp))

          testCase "every TreeOp arm is generated at least once" (fun () ->
              let opListArb = Arb.fromGen (Gen.listOfLength 1000 Generators.genOp)
              Check.One(cfg, Prop.forAll opListArb opSampleProp)) ]
