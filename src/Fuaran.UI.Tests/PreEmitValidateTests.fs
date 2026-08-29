module Fuaran.UI.Tests.PreEmitValidate

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.PreEmitValidate

// ============================================================================
//  Tests for the pre-emit tree-invariant walker.
//
//  Per the §4g op vocabulary's NodeId-uniqueness contract + the wire
//  shape's empty-string-rejection, `Fuaran.UI.PreEmitValidate.validate`
//  is the canonical pre-submission gate. These tests exercise the
//  defect classes the walker can report.
//
//  Every tree below is a NEGATIVE fixture: the malformed tabs shape IS the
//  test input, constructed so `validate` can be asserted to report it. The
//  build-time validator sees the same defect at the same source and is
//  correct to — so the two tab-shape codes are suppressed for this file.
//  Narrowing this to the individual call sites would need a pragma per
//  fixture and would rot as tests are added; the whole file is fixtures.
// fuaran-validator: disable FUARAN047, FUARAN048 — negative-test fixtures
// ============================================================================

type private Msg = NoOp

let private dashboard id children : Node<Msg> =
    Fuaran.dashboard
        id
        { Defaults.dashboard<Msg> with
            Children = children }

let private markdown id text : Node<Msg> = Fuaran.markdown id text

// ─── Phase 932 fixtures — FUARAN098, a `SetState` writing an unread key ───

/// A button whose click writes `key`, standing beside whatever `readers` the test
/// wants. The button is the only writer; what reads the key — if anything — is
/// the variable under test.
let private setStateTree (key: string) (readers: Node<Msg> list) : Node<Msg> =
    let writer =
        Fuaran.button
            "writer"
            { Defaults.button<Msg> with
                Label = TextSource.Literal "Go"
                OnClick = Action.SetState(key, Some(Fuaran.Core.JBool true), None) }

    dashboard "root" (writer :: readers)

/// Only the FUARAN098 defects. The fixtures deliberately carry unrelated shapes
/// (a bare grid, a default-cased switch), so asserting `Ok()` would couple these
/// tests to rules they are not about.
let private noReaderDefects (tree: Node<Msg>) : PreEmitDefect list =
    match PreEmitValidate.validate tree with
    | Ok() -> []
    | Error ds ->
        ds
        |> List.filter (function
            | PreEmitDefect.SetStateNoReader _ -> true
            | _ -> false)

// ─── FUARAN102 fixtures — a hardcoded date where `Now` was meant ───

/// A `Fact` — the plainest "one labelled datum" node — with both slots under
/// test. `Now`-bound values are expressed by passing a bound `TextSource`.
let private fact (id: string) (label: TextSource) (value: TextSource) : Node<Msg> =
    let node: Node<Msg> = Fuaran.fact id "" ""

    { node with
        Kind =
            NodeKind.Fact(
                { Defaults.fact with
                    Label = label
                    Value = value }
            ) }

let private staleDateDefects (tree: Node<Msg>) : PreEmitDefect list =
    match PreEmitValidate.validate tree with
    | Ok() -> []
    | Error ds ->
        ds
        |> List.filter (function
            | PreEmitDefect.DateLiteralWhereNowPlausible _ -> true
            | _ -> false)

// ─── FUARAN103 fixtures — a `Switch` on a key nothing can write ───

let private noWriterDefects (tree: Node<Msg>) : PreEmitDefect list =
    match PreEmitValidate.validate tree with
    | Ok() -> []
    | Error ds ->
        ds
        |> List.filter (function
            | PreEmitDefect.SwitchKeyNoWriter _ -> true
            | _ -> false)

/// The plainest possible reader: a metric bound to `key` on the State channel.
let private stateReader (id: string) (key: string) : Node<Msg> =
    Fuaran.metric
        id
        { Defaults.metric with
            Label = TextSource.Literal "M"
            Value = Binding.State(key, Some 0.0) }

/// A `Switch` whose branch SELECTOR reads `key` — a `Binding` since Phase 768,
/// and the read surface a naive rule misses.
let private switchReader (id: string) (key: string) : Node<Msg> =
    Fuaran.switch
        id
        { Defaults.switch<Msg> with
            On = Binding.State(key, None)
            Cases =
                [ { Match = "on"
                    Child = markdown (id + "-on") "on" } ]
            Default = markdown (id + "-off") "off" }

// ─── Phase 865 fixtures — FUARAN105, a Transform over an unfillable source ───
//
// The charter's §3.1 shape: a grid carrying rows on its OWN `defaultValue`
// beside a badge deriving a count from the same key without one. Under the
// shipped resolver `Binding.State`'s default is a per-reader FALLBACK rather
// than a slot seed, so the badge's Transform starts from `emptySource` and
// renders zero forever — which is what these fixtures pin.

let private inertSourceDefects (tree: Node<Msg>) : PreEmitDefect list =
    match PreEmitValidate.validate tree with
    | Ok() -> []
    | Error ds ->
        ds
        |> List.filter (function
            | PreEmitDefect.TransformSourceInert _ -> true
            | _ -> false)

/// The canonical post-818 derivation idiom: `groupBy` + `count`, which is the
/// pipeline the sighted emissions carried. Its content is immaterial to the
/// rule — the rule keys on the SOURCE — but a real pipeline keeps the fixture
/// recognisable as the emission it stands for.
let private groupCount: Fuaran.Core.Transform list =
    [ Fuaran.Core.Transform.GroupBy(
          [ "team" ],
          [ { Name = "n"
              Fn = Fuaran.Core.AggFn.Count
              Of = "team" } ]
      ) ]

/// Two rows, row-major — the shape `HostPrelude.TransformLive` transposes.
let private memberRows: Fuaran.Core.JVal =
    Fuaran.Core.JArr
        [ Fuaran.Core.JObj [ "team", Fuaran.Core.JStr "Ops" ]
          Fuaran.Core.JObj [ "team", Fuaran.Core.JStr "Ops" ] ]

/// A badge whose text derives from a Transform over `$state.<key>`.
/// `sourceDefault` is the Transform source slot's OWN carried default — the
/// single thing that decides whether the pipeline runs over real rows or over
/// `TransformLive.emptySource`.
let private derivedBadge (id: string) (key: string) (sourceDefault: Fuaran.Core.JVal option) : Node<Msg> =
    let source: Binding<Fuaran.Core.JVal> = Binding.State(key, sourceDefault)

    // The decoder derives the initial snapshot from the source's carried
    // default; mirrored here so the fixture is the tree a decode would produce.
    let initial =
        match sourceDefault with
        | Some data ->
            match HostPrelude.TransformLive.initialSource data with
            | Ok ds -> ds
            | Error _ -> HostPrelude.TransformLive.emptySource
        | None -> HostPrelude.TransformLive.emptySource

    Fuaran.badge
        id
        { Defaults.badge with
            Label = TextSource.Bound(Binding.Transform(TransformSource.Live(source, initial), groupCount, None)) }

/// A grid sourced from `$state.<key>`, carrying the rows in its own default —
/// the OTHER half of the charter's pair. Deliberately NOT editable: an editable
/// grid over a direct State source is a write destination, and that would make
/// the fixture prove something about writers rather than about seeding.
let private seedingGrid (id: string) (key: string) : Node<Msg> =
    { Id = id
      Kind =
        NodeKind.DataGrid(
            { SortStateKey = None
              PageSize = None
              PageStateKey = None
              EditStateKey = None
              DefaultSort = None
              Source =
                Binding.State(
                    key,
                    Some(Seq.ofList [ (Map.ofList [ "team", Unchecked.nonNull (box 1) ]: Fuaran.Core.Row) ])
                )
              RowKey = None
              RowKeyField = Some "id"
              Columns =
                [ { Label = "Team"
                    Value = None
                    Field = Some "team"
                    Sortable = None
                    Editable = None
                    Format = CellFormat.None
                    Kind = CellKindErased.Text
                    Width = ColumnWidth.Auto } ]
              OnRowClick = None
              Editable = false
              Reorderable = false
              StaticRows = None }
        )
      State = None
      Style = None
      Accessibility = None
      ExtraAttributes = None
      Motion = None }

// ─── Phase 1075 fixtures — the seeding rule, FUARAN106 and FUARAN107 ───

/// A grid sourced from an ARBITRARY row feed — the parametrised twin of
/// `seedingGrid`, so a fixture can put the SAME data on a grid row-major and on
/// a Transform columnar and assert the two do not read as a disagreement.
let private gridWithSource (id: string) (source: Binding<Fuaran.Core.Row seq>) : Node<Msg> =
    match (seedingGrid id "unused").Kind with
    | NodeKind.DataGrid spec ->
        { Id = id
          Kind = NodeKind.DataGrid { spec with Source = source }
          State = None
          Style = None
          Accessibility = None
          ExtraAttributes = None
          Motion = None }
    | _ -> failwith "seedingGrid must produce a DataGrid"

/// `memberRows` as the typed ROW-MAJOR feed a grid's `source` carries — the
/// same two rows, spelled the way the other slot cannot spell them.
let private memberRowsTyped: Fuaran.Core.Row seq =
    Seq.ofList
        [ (Map.ofList [ "team", Unchecked.nonNull (box "Ops") ]: Fuaran.Core.Row)
          (Map.ofList [ "team", Unchecked.nonNull (box "Ops") ]: Fuaran.Core.Row) ]

/// `memberRows` as the canonical columnar `DataSource` a Transform's `Data`
/// arm carries.
let private memberTable: Fuaran.Core.DataSource =
    match HostPrelude.TransformLive.initialSource memberRows with
    | Ok ds -> ds
    | Error _ -> failwith "memberRows must decode as a table"

/// A badge deriving over an EMBEDDED table rather than a state key — the
/// second inline copy in FUARAN107's subject pair.
let private embeddedBadge (id: string) (table: Fuaran.Core.DataSource) : Node<Msg> =
    Fuaran.badge
        id
        { Defaults.badge with
            Label = TextSource.Bound(Binding.Transform(TransformSource.Data table, groupCount, None)) }

let private seedConflicts (tree: Node<Msg>) : PreEmitDefect list =
    match PreEmitValidate.validate tree with
    | Ok() -> []
    | Error ds ->
        ds
        |> List.filter (function
            | PreEmitDefect.ConflictingStateSeeds _ -> true
            | _ -> false)

let private duplicateTables (tree: Node<Msg>) : PreEmitDefect list =
    match PreEmitValidate.validate tree with
    | Ok() -> []
    | Error ds ->
        ds
        |> List.filter (function
            | PreEmitDefect.DuplicateInlineTable _ -> true
            | _ -> false)

// ─── Phase 864 fixtures — the declared-rule family (FUARAN099/100/101) ───
//
// Every rule below reasons about a slot the author DECLARED, so the fixtures
// are all forms carrying a `Rule`. The three codes divide by what the walk can
// know: FUARAN099 is decidable from the tree alone (an Error), the other two
// turn on what a host might honour (Warnings).

let private ruleDefects (tree: Node<Msg>) : PreEmitDefect list =
    match PreEmitValidate.validate tree with
    | Ok() -> []
    | Error ds ->
        ds
        |> List.filter (function
            | PreEmitDefect.CompareKeyUnreachable _
            | PreEmitDefect.RuleSlotUnhonourable _
            | PreEmitDefect.CompareDuplicatesBound _ -> true
            | _ -> false)

/// A rule carrying only the slots a test sets. Every slot optional, so the
/// builder starts from "constrains nothing" and each fixture adds exactly one
/// thing — which keeps a fixture's defect attributable to the slot it declared.
let private emptyRule: FieldRule =
    { Compare = None
      Format = None
      MaxLength = None
      Message = None
      MinLength = None
      Pattern = None }

/// A form of `fields`, State-bound so nothing here trips FUARAN069 as well and
/// muddies the assertion.
let private ruleForm (fields: FormField<Msg> list) : Node<Msg> =
    Fuaran.form
        "frm"
        { Defaults.form<Msg> with
            Fields = fields }

let private ruleField (id: string) (kind: FormFieldKind<Msg>) (rule: FieldRule option) : FormField<Msg> =
    { Defaults.formField<Msg> with
        Id = id
        Kind = kind
        Rule = rule }

/// The plainest writable text control: State-bound under its own id, which is
/// also what makes it OWN that key for FUARAN099's purposes.
let private textKind (id: string) : FormFieldKind<Msg> =
    FormFieldKind.Text(Some(Binding.State(id, Some "")), None)

/// Phase 861 — a bound grid with the sort declarations under test. Columns are
/// `(label, field, sortable)`; a `rowKeyField` keeps FUARAN078 out of the way.
let private sortGrid
    (sortStateKey: string option)
    (defaultSortColumn: int option)
    (columns: (string * string option * bool option) list)
    : Node<Msg> =
    { Id = "sorted"
      Kind =
        NodeKind.DataGrid(
            { SortStateKey = sortStateKey
              PageSize = None
              PageStateKey = None
              EditStateKey = None
              DefaultSort =
                defaultSortColumn
                |> Option.map (fun c ->
                    { Column = c
                      Direction = SortDirection.Asc })
              Source = Binding.Static(Some Seq.empty)
              RowKey = None
              RowKeyField = Some "id"
              Columns =
                columns
                |> List.map (fun (label, field, sortable) ->
                    { Label = label
                      Value = None
                      Field = field
                      Sortable = sortable
                      Editable = None
                      Format = CellFormat.None
                      Kind = CellKindErased.Text
                      Width = ColumnWidth.Auto })
              OnRowClick = None
              Editable = false
              Reorderable = false
              StaticRows = None }
        )
      State = None
      Style = None
      Accessibility = None
      Motion = Defaults.Motion.none
      ExtraAttributes = None }

/// Phase 863 — a bound grid with the edit declarations under test. Columns are
/// `(label, editable)`.
let private editGrid
    (gridEditable: bool)
    (editStateKey: string option)
    (source: Binding<Fuaran.Core.Row seq>)
    (columns: (string * bool option) list)
    : Node<Msg> =
    { Id = "edited"
      Kind =
        NodeKind.DataGrid(
            { SortStateKey = None
              PageSize = None
              PageStateKey = None
              EditStateKey = editStateKey
              DefaultSort = None
              Source = source
              RowKey = None
              RowKeyField = Some "id"
              Columns =
                columns
                |> List.map (fun (label, editable) ->
                    { Label = label
                      Value = None
                      Field = Some "note"
                      Sortable = None
                      Editable = editable
                      Format = CellFormat.None
                      Kind = CellKindErased.Text
                      Width = ColumnWidth.Auto })
              OnRowClick = None
              Editable = gridEditable
              Reorderable = false
              StaticRows = None }
        )
      State = None
      Style = None
      Accessibility = None
      Motion = Defaults.Motion.none
      ExtraAttributes = None }

/// Phase 862 — a data-bound grid with the paging declarations under test and
/// nothing else that could raise a defect (no columns, so no FUARAN077; a
/// `rowKeyField`, so no FUARAN078). Built once so each test varies exactly the
/// axis it names.
let private pagedGrid
    (pageSize: int option)
    (pageStateKey: string option)
    (source: Binding<Fuaran.Core.Row seq>)
    : Node<Msg> =
    { Id = "paged"
      Kind =
        NodeKind.DataGrid(
            { SortStateKey = None
              PageSize = pageSize
              PageStateKey = pageStateKey
              EditStateKey = None
              DefaultSort = None
              Source = source
              RowKey = None
              RowKeyField = Some "id"
              Columns = []
              OnRowClick = None
              Editable = false
              Reorderable = false
              StaticRows = None }
        )
      State = None
      Style = None
      Accessibility = None
      Motion = Defaults.Motion.none
      ExtraAttributes = None }

// ─── Phase 727 fixtures — the accessibility family (FUARAN109/110/111) ───
//
// Every fixture is built through the real smart constructors, so each node
// carries the language's OWN per-kind `Defaults.Accessibility.*` value — which
// is the rules' input, not a detail of the fixture. A node hand-built as a
// record would carry `Accessibility = None` and silently exercise nothing.

/// A button with `label` as its structural name, carrying the constructor's own
/// accessibility default (`Role = Button`) — the shape an F# author gets free.
let private a11yButton (id: string) (label: string) : Node<Msg> =
    Fuaran.button
        id
        { Defaults.button<Msg> with
            Label = TextSource.Literal label
            OnClick = Action.Chain [] }

/// The same button with a BOUND structural label — the shape no pre-emit walk
/// can resolve, and which the family therefore never judges.
let private boundLabelButton (id: string) : Node<Msg> =
    Fuaran.button
        id
        { Defaults.button<Msg> with
            Label = TextSource.Bound(Binding.State("caption", None))
            OnClick = Action.Chain [] }

/// Replace a node's accessibility trait — the author override the rules read.
let private withTrait (a: Accessibility) (n: Node<Msg>) : Node<Msg> = { n with Accessibility = Some a }

/// A trait declaring `label` and nothing else.
let private traitLabel (binding: Binding<string>) : Accessibility =
    { Defaults.Accessibility.empty with
        Label = Some binding }

/// Only the Phase 727 defects. The fixtures deliberately carry unrelated shapes
/// (a default `Select` is inert, a bare `Form` has no fields), so asserting
/// `Ok()` would couple these tests to rules they are not about.
let private a11yDefects (tree: Node<Msg>) : PreEmitDefect list =
    match PreEmitValidate.validate tree with
    | Ok() -> []
    | Error ds ->
        ds
        |> List.filter (function
            | PreEmitDefect.InteractiveWithoutAccessibleName _
            | PreEmitDefect.DanglingAccessibilityReference _
            | PreEmitDefect.EmptyAccessibilityDeclaration _ -> true
            | _ -> false)

/// Those defects as their published codes — what a host actually reads.
let private a11yCodes (tree: Node<Msg>) : string list =
    a11yDefects tree
    |> List.map (fun d ->
        let code, _, _ = PreEmitValidate.describe d
        code)

/// The severity `describe` publishes for a defect — the family's whole posture
/// question, asserted rather than asserted-about-in-a-comment.
let private severityOf (d: PreEmitDefect) : DefectSeverity =
    let _, severity, _ = PreEmitValidate.describe d
    severity

[<Tests>]
let tests =
    testList
        "Fuaran.UI.PreEmitValidate"
        [ test "validate passes a clean tree with unique non-empty NodeIds" {
              let tree =
                  dashboard "root" [ markdown "summary" "Hello"; markdown "details" "Body" ]

              match PreEmitValidate.validate tree with
              | Ok() -> ()
              | Error defects -> failtestf "Expected Ok, got defects: %A" defects
          }

          test "validate flags duplicate NodeIds with the offending id + count" {
              let tree =
                  dashboard
                      "root"
                      [ markdown "duplicate" "one"
                        markdown "duplicate" "two"
                        markdown "duplicate" "three" ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  let dup =
                      defects
                      |> List.tryPick (fun d ->
                          match d with
                          | PreEmitDefect.DuplicateNodeId(id, count) -> Some(id, count)
                          | _ -> None)

                  Expect.equal dup (Some("duplicate", 3)) "DuplicateNodeId reports id and count"
              | Ok() -> failtest "Expected Error, got Ok"
          }

          test "validate flags empty NodeId strings" {
              let tree = dashboard "" [ markdown "child" "ok" ]

              match PreEmitValidate.validate tree with
              | Error defects -> Expect.contains defects PreEmitDefect.EmptyNodeId "EmptyNodeId reported"
              | Ok() -> failtest "Expected Error, got Ok"
          }

          test "validate collects all defects in one pass (no short-circuit)" {
              let tree =
                  dashboard "" [ markdown "dup" "First duplicate"; markdown "dup" "Second duplicate" ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  Expect.contains defects PreEmitDefect.EmptyNodeId "EmptyNodeId surfaced"

                  Expect.isTrue
                      (defects
                       |> List.exists (function
                           | PreEmitDefect.DuplicateNodeId("dup", _) -> true
                           | _ -> false))
                      "DuplicateNodeId surfaced"
              | Ok() -> failtest "Expected Error with multiple defects, got Ok"
          }

          // ──────────────────────────────────────────────────────────────
          // Tab-shape invariants: header / tag length match children;
          // ActiveTag presence requires TabTags presence.

          test "validate flags FUARAN047 when TabHeaders.Length ≠ Children.Length" {
              let tabsNode =
                  Fuaran.tabs
                      "results-tabs"
                      { Defaults.tabs<Msg> with
                          Children = [ markdown "overview" "Overview"; markdown "detail" "Detail" ]
                          TabHeaders =
                              Some
                                  [ { Defaults.tabHeader with
                                        Label = TextSource.Literal "Overview" } ] }

              let tree = dashboard "root" [ tabsNode ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  let header =
                      defects
                      |> List.tryPick (fun d ->
                          match d with
                          | PreEmitDefect.TabHeaderCountMismatch(id, hc, cc) -> Some(id, hc, cc)
                          | _ -> None)

                  Expect.equal
                      header
                      (Some("results-tabs", 1, 2))
                      "TabHeaderCountMismatch reports nodeId + 1 header + 2 children"
              | Ok() -> failtest "Expected FUARAN047 defect, got Ok"
          }

          test "validate flags FUARAN048 when TabTags.Length ≠ Children.Length" {
              let tabsNode =
                  Fuaran.tabs
                      "results-tabs"
                      { Defaults.tabs<Msg> with
                          Children = [ markdown "overview" "Overview"; markdown "detail" "Detail" ]
                          TabTags = Some [ "overview" ] }

              let tree = dashboard "root" [ tabsNode ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  let tag =
                      defects
                      |> List.tryPick (fun d ->
                          match d with
                          | PreEmitDefect.TabTagCountMismatch(id, tc, cc) -> Some(id, tc, cc)
                          | _ -> None)

                  Expect.equal
                      tag
                      (Some("results-tabs", 1, 2))
                      "TabTagCountMismatch reports nodeId + 1 tag + 2 children"
              | Ok() -> failtest "Expected FUARAN048 defect, got Ok"
          }

          test "validate flags FUARAN049 when ActiveTag is Some but TabTags is None" {
              let tabsNode =
                  Fuaran.tabs
                      "results-tabs"
                      { Defaults.tabs<Msg> with
                          Children = [ markdown "overview" "Overview" ]
                          ActiveTag = Some(Binding.Static(Some "overview")) }

              let tree = dashboard "root" [ tabsNode ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.TabActiveTagWithoutTags "results-tabs")
                      "TabActiveTagWithoutTags surfaced for results-tabs"
              | Ok() -> failtest "Expected FUARAN049 defect, got Ok"
          }

          test "validate passes a TabsSpec where TabHeaders + TabTags + ActiveTag align" {
              let tabsNode =
                  Fuaran.tabs
                      "results-tabs"
                      { Defaults.tabs<Msg> with
                          Children = [ markdown "overview" "Overview"; markdown "detail" "Detail" ]
                          TabHeaders =
                              Some
                                  [ { Defaults.tabHeader with
                                        Label = TextSource.Literal "Overview" }
                                    { Defaults.tabHeader with
                                        Label = TextSource.Literal "Detail" } ]
                          TabTags = Some [ "overview"; "detail" ]
                          // State-bound so the tag write-back default keeps the
                          // handler-free tabs live (FUARAN069 stays silent).
                          ActiveTag = Some(Binding.State("activeTab", Some "overview")) }

              let tree = dashboard "root" [ tabsNode ]

              match PreEmitValidate.validate tree with
              | Ok() -> ()
              | Error defects -> failtestf "Expected Ok for aligned Tabs spec, got: %A" defects
          }

          // ── FUARAN069 — inert-control check (Phase 426 write-back default) ──

          test "validate flags FUARAN069 for a handler-free form field over a non-writable binding" {
              let field: FormField<Msg> =
                  { Defaults.formField<Msg> with
                      Id = "inert-name"
                      Kind = FormFieldKind.Text(Some(Binding.Static(Some "")), None) }

              let formNode =
                  Fuaran.form
                      "frm"
                      { Defaults.form<Msg> with
                          Fields = [ field ] }

              let tree = dashboard "root" [ formNode ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.InertControl("frm", "FormField(inert-name)"))
                      "InertControl surfaced for the static-bound handler-free field"
              | Ok() -> failtest "Expected FUARAN069 defect, got Ok"
          }

          test "validate passes a handler-free form field whose value is State-bound (write-back target)" {
              let field: FormField<Msg> =
                  { Defaults.formField<Msg> with
                      Id = "profile-name"
                      Kind = FormFieldKind.Text(Some(Binding.State("profileName", Some "")), None) }

              let formNode =
                  Fuaran.form
                      "frm"
                      { Defaults.form<Msg> with
                          Fields = [ field ] }

              let tree = dashboard "root" [ formNode ]

              match PreEmitValidate.validate tree with
              | Ok() -> ()
              | Error defects -> failtestf "Expected Ok for a State-bound handler-free field, got: %A" defects
          }

          test "validate flags FUARAN069 for a dismissable modal with no OnDismiss and a static Open" {
              let modalNode =
                  Fuaran.modal
                      "confirm"
                      { Defaults.modal<Msg> with
                          Children = [ markdown "body" "Sure?" ] }

              let tree = dashboard "root" [ modalNode ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.InertControl("confirm", "Modal"))
                      "InertControl surfaced for the undismissable-in-practice modal"
              | Ok() -> failtest "Expected FUARAN069 defect, got Ok"
          }

          test "validate flags FUARAN082 for a Switch with duplicate match values" {
              let switchNode =
                  Fuaran.switch
                      "sw"
                      { Defaults.switch<Msg> with
                          On = Binding.State("view", None)
                          Cases =
                              [ { Match = "a"
                                  Child = markdown "c1" "one" }
                                { Match = "a"
                                  Child = markdown "c2" "two" } ]
                          Default = markdown "def" "none" }

              let tree = dashboard "root" [ switchNode ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.DuplicateSwitchMatch("sw", "a"))
                      "DuplicateSwitchMatch surfaced for the repeated match value"
              | Ok() -> failtest "Expected FUARAN082 defect, got Ok"
          }

          test "validate flags FUARAN083 for a Switch with an empty stateKey" {
              let switchNode =
                  Fuaran.switch
                      "sw"
                      { Defaults.switch<Msg> with
                          Cases =
                              [ { Match = "details"
                                  Child = markdown "c1" "one" } ]
                          Default = markdown "def" "none" }

              let tree = dashboard "root" [ switchNode ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.UngroundedSwitchStateKey "sw")
                      "UngroundedSwitchStateKey surfaced for the empty stateKey"
              | Ok() -> failtest "Expected FUARAN083 defect, got Ok"
          }

          test "validate passes a Switch with distinct match values" {
              let switchNode =
                  Fuaran.switch
                      "sw"
                      { Defaults.switch<Msg> with
                          On = Binding.State("view", None)
                          Cases =
                              [ { Match = "details"
                                  Child = markdown "c1" "one" }
                                { Match = "summary"
                                  Child = markdown "c2" "two" } ]
                          Default = markdown "def" "none" }

              // The button is not incidental: a `Switch` selecting on a key
              // nothing can write is itself a finding (FUARAN103), so a fixture
              // asserting a CLEAN tree has to be a complete one.
              let writer =
                  Fuaran.button
                      "set-view"
                      { Defaults.button<Msg> with
                          Label = TextSource.Literal "Details"
                          OnClick = Action.SetState("view", Some(Fuaran.Core.JStr "details"), None) }

              let tree = dashboard "root" [ writer; switchNode ]

              match PreEmitValidate.validate tree with
              | Ok() -> ()
              | Error defects -> failtestf "Expected Ok for distinct-match Switch, got: %A" defects
          }

          test "validate passes handler-free tabs whose ActiveIndex is State-bound" {
              let tabsNode =
                  Fuaran.tabs
                      "panes"
                      { Defaults.tabs<Msg> with
                          Children = [ markdown "overview" "Overview" ]
                          ActiveIndex = Binding.State("activePane", Some 0) }

              let tree = dashboard "root" [ tabsNode ]

              match PreEmitValidate.validate tree with
              | Ok() -> ()
              | Error defects -> failtestf "Expected Ok for State-bound handler-free tabs, got: %A" defects
          }

          // ── FUARAN070 / FUARAN071 — Selection edge checks (Phase 427) ──

          test "validate flags FUARAN070 for a Binding.Selection naming a NodeId absent from the tree" {
              let detail =
                  Fuaran.metric
                      "detail"
                      { Defaults.metric with
                          Label = TextSource.Literal "Selected"
                          Value = Binding.Selection("no-such-grid", (fun (raw: obj) -> unbox raw), None, None) }

              let tree = dashboard "root" [ detail ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.DanglingSelection("detail", "no-such-grid"))
                      "DanglingSelection surfaced with reader + missing target"
              | Ok() -> failtest "Expected FUARAN070 defect, got Ok"
          }

          test "validate flags FUARAN071 for a Binding.Selection over a non-producer node" {
              let detail =
                  Fuaran.metric
                      "detail"
                      { Defaults.metric with
                          Label = TextSource.Literal "Selected"
                          Value = Binding.Selection("summary", (fun (raw: obj) -> unbox raw), None, None) }

              let tree = dashboard "root" [ markdown "summary" "Not a grid"; detail ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.SelectionOverNonProducer("detail", "summary"))
                      "SelectionOverNonProducer surfaced with reader + non-producer target"
              | Ok() -> failtest "Expected FUARAN071 defect, got Ok"
          }

          // ── Tidy-Up (Phase 932 follow-on) — the three read surfaces the shared
          // walk missed. Each of these trees passed validation BEFORE the
          // `BindingWalk` widening, because the reader sat somewhere `collect`
          // never descended into with `inUses` set. A dangling `Binding.Selection`
          // is the probe: FUARAN070 fires only if the walk actually reached it.
          //
          // The suite could not answer the blast-radius question on its own —
          // `PreEmitValidateTests` contained no `StateBehaviour` and no `SlotArg`
          // tree at all, so a green gate over the old walk was vacuous for two of
          // the three. These are that coverage.

          test "validate flags FUARAN070 for a dangling Selection inside a StateBehaviour OnEmpty subtree" {
              let orphan =
                  Fuaran.metric
                      "empty-detail"
                      { Defaults.metric with
                          Label = TextSource.Literal "Selected"
                          Value = Binding.Selection("no-such-grid", (fun (raw: obj) -> unbox raw), None, None) }

              let body =
                  { markdown "body" "Body" with
                      State =
                          Some
                              { OnEmpty = Some orphan
                                OnError = None
                                OnLoading = None } }

              match PreEmitValidate.validate (dashboard "root" [ body ]) with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.DanglingSelection("empty-detail", "no-such-grid"))
                      "an OnEmpty subtree is a real reader — the walk must descend into it"
              | Ok() -> failtest "Expected FUARAN070 from the OnEmpty subtree, got Ok"
          }

          test "validate flags FUARAN070 for a dangling Selection inside an OnLoading subtree" {
              let orphan =
                  Fuaran.metric
                      "loading-detail"
                      { Defaults.metric with
                          Label = TextSource.Literal "Selected"
                          Value = Binding.Selection("no-such-grid", (fun (raw: obj) -> unbox raw), None, None) }

              let body =
                  { markdown "body" "Body" with
                      State =
                          Some
                              { OnEmpty = None
                                OnError = None
                                OnLoading = Some orphan } }

              match PreEmitValidate.validate (dashboard "root" [ body ]) with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.DanglingSelection("loading-detail", "no-such-grid"))
                      "OnLoading is the sibling surface — both arms or neither"
              | Ok() -> failtest "Expected FUARAN070 from the OnLoading subtree, got Ok"
          }

          test "validate flags FUARAN070 for a dangling Selection inside a FragmentArg.SlotArg subtree" {
              let orphan =
                  Fuaran.metric
                      "slot-detail"
                      { Defaults.metric with
                          Label = TextSource.Literal "Selected"
                          Value = Binding.Selection("no-such-grid", (fun (raw: obj) -> unbox raw), None, None) }

              let refNode =
                  { Fuaran.fragmentRef "ref1" "card" with
                      Kind =
                          NodeKind.FragmentRef
                              { Name = "card"
                                Args = Some(Map.ofList [ "slot", FragmentArg.SlotArg orphan ]) } }

              match PreEmitValidate.validate (dashboard "root" [ refNode ]) with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.DanglingSelection("slot-detail", "no-such-grid"))
                      "a whole Node passed as a slot argument is not invisible to validation"
              | Ok() -> failtest "Expected FUARAN070 from the SlotArg subtree, got Ok"
          }

          test "validate flags FUARAN070 for a Switch whose SELECTOR is a dangling Selection" {
              // Phase 768 made `On` any Binding precisely so a branch could follow
              // the clicked row with no writer (closing 032/c6). That makes the
              // selector a Selection READ, and a dangling one is FUARAN070's case.
              let sw =
                  Fuaran.switch
                      "sw"
                      { Defaults.switch<Msg> with
                          On = Binding.Selection("no-such-grid", (fun (raw: obj) -> unbox (raw: obj)), None, None)
                          Cases =
                              [ { Match = "on"
                                  Child = markdown "sw-on" "on" } ]
                          Default = markdown "sw-off" "off" }

              match PreEmitValidate.validate (dashboard "root" [ sw ]) with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.DanglingSelection("sw", "no-such-grid"))
                      "the Switch selector is a read, not a literal — the four-phase-stale comment's cost"
              | Ok() -> failtest "Expected FUARAN070 from the Switch selector, got Ok"
          }

          // ── The FOURTH surface of that Tidy-Up: bindings carried by an ACTION.
          //
          // `callsOfAction` harvested `Action.Call` endpoints and dropped the
          // rest, and `recordStateAction` routed a `SetState`'s `valueFrom` to
          // the STATE projection only — so a `valueFrom` binding on any other
          // channel contributed no usage at all, and the consumption-union
          // rules reasoned from a surface they had never looked at.
          //
          // Coverage first, because green was VACUOUS here in exactly the way
          // the earlier pass warned: `Fuaran.UI.Tests` held not one
          // `Action.SetState` carrying a `valueFrom` before these tests — the
          // only two in the repo were a corpus fixture builder and a decode
          // round-trip assertion, neither of which runs the validator.

          test "validate flags FUARAN070 for a dangling Selection inside an Action's valueFrom" {
              // The shape the corpus already carried and nothing reported:
              // `button-setstate-valuefrom` writes `chosen-id` from
              // `Binding.Selection("orders-grid", …)` — a wire fixture whose
              // isolated tree holds no such node.
              let writer =
                  Fuaran.button
                      "picker"
                      { Defaults.button<Msg> with
                          Label = TextSource.Literal "Track this order"
                          OnClick =
                              Action.SetState(
                                  "chosen-id",
                                  None,
                                  Some(Binding.Selection("no-such-grid", (fun (raw: obj) -> unbox raw), None, None))
                              ) }

              match PreEmitValidate.validate (dashboard "root" [ writer ]) with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.DanglingSelection("picker", "no-such-grid"))
                      "a dispatch-time read is still a read — the walk must descend an action's valueFrom"
              | Ok() -> failtest "Expected FUARAN070 from the Action valueFrom, got Ok"
          }

          test "FUARAN074 go-red check: a chip consumed ONLY by an Action's valueFrom is not decorative" {
              // The other direction, and the one that matters more: widening a
              // read surface must REMOVE false accusations as well as find real
              // defects. A button writing state from `$filters.dept` consumes
              // that chip — the chip is the reason the button has anything to
              // write.
              let chip =
                  Fuaran.filters
                      "chips"
                      [ { Name = "dept"
                          Label = TextSource.Literal "dept"
                          Kind = FormFieldKind.Text(Some(Binding.Filter("dept", None)), None) } ]

              let writer =
                  Fuaran.button
                      "apply"
                      { Defaults.button<Msg> with
                          Label = TextSource.Literal "Apply"
                          OnClick = Action.SetState("applied-dept", None, Some(Binding.Filter("dept", None))) }

              let defects =
                  match PreEmitValidate.validate (dashboard "root" [ chip; writer ]) with
                  | Ok() -> []
                  | Error ds -> ds

              Expect.isFalse
                  (List.contains (PreEmitDefect.DecorativeFilter("chips", "dept")) defects)
                  "the button's valueFrom consumes the chip"
          }

          test "an Action's valueFrom is folded into each projection exactly ONCE" {
              // The hazard the widening introduces rather than closes.
              // `Seeds`, `InlineTables` and `TransformInertSources` are LISTS,
              // so folding the `valueFrom` into the state projection twice —
              // once on the action arm and once through the usage walk — would
              // report FUARAN105/106/107 twice for one slot, and the duplicate
              // would look like two defects in one tree.
              let writer =
                  Fuaran.button
                      "derive"
                      { Defaults.button<Msg> with
                          Label = TextSource.Literal "Derive"
                          OnClick =
                              Action.SetState(
                                  "team-count",
                                  None,
                                  Some(
                                      Binding.Transform(
                                          TransformSource.Live(
                                              Binding.State("members", None),
                                              HostPrelude.TransformLive.emptySource
                                          ),
                                          groupCount,
                                          None
                                      )
                                  )
                              ) }

              let facts = BindingWalk.collect (dashboard "root" [ writer ])

              Expect.equal
                  facts.StateKeys.TransformInertSources
                  [ "derive", "members" ]
                  "one inert source, recorded once"

              Expect.equal
                  (facts.Uses
                   |> List.filter (fun u ->
                       match u.Use with
                       | BindingWalk.BindingUse.TransformStateSource _ -> true
                       | _ -> false)
                   |> List.length)
                  1
                  "one usage, recorded once"
          }

          // ── FUARAN072 / FUARAN073 — Call result-target checks (Phase 428) ──

          test "validate flags FUARAN072 for a Call into a Query no reader binds (orphan fetch)" {
              let fetchButton =
                  Fuaran.button
                      "fetch"
                      { Defaults.button<Msg> with
                          Label = TextSource.Literal "Fetch"
                          OnClick = Action.Call("/api/orders", None, Some(CallResultTarget.Query "orders")) }

              let tree = dashboard "root" [ fetchButton ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.OrphanQueryFetch("fetch", "orders"))
                      "OrphanQueryFetch surfaced for the readerless query target"
              | Ok() -> failtest "Expected FUARAN072 defect, got Ok"
          }

          test "validate passes a Call into a Query a reader binds" {
              let fetchButton =
                  Fuaran.button
                      "fetch"
                      { Defaults.button<Msg> with
                          Label = TextSource.Literal "Fetch"
                          OnClick = Action.Call("/api/orders", None, Some(CallResultTarget.Query "orders")) }

              let reader =
                  Fuaran.metric
                      "orders-metric"
                      { Defaults.metric with
                          Label = TextSource.Literal "Orders"
                          Value = Binding.Query("orders", (fun (raw: obj) -> unbox raw), None) }

              let tree = dashboard "root" [ fetchButton; reader ]

              match PreEmitValidate.validate tree with
              | Ok() -> ()
              | Error defects -> failtestf "Expected Ok for a read query target, got: %A" defects
          }

          test "validate flags FUARAN073 for a Call with neither onResult nor into (dropped result)" {
              let fireButton =
                  Fuaran.button
                      "fire"
                      { Defaults.button<Msg> with
                          Label = TextSource.Literal "Ping"
                          OnClick = Action.Call("/api/ping", None, None) }

              let tree = dashboard "root" [ fireButton ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.CallResultDropped("fire", "/api/ping"))
                      "CallResultDropped surfaced for the fire-and-forget call"
              | Ok() -> failtest "Expected FUARAN073 defect, got Ok"
          }

          test "validate passes a master-detail pair (Selection over a DataGrid node)" {
              let grid: Node<Msg> =
                  { Id = "orders-grid"
                    Kind =
                      NodeKind.DataGrid(
                          { SortStateKey = None
                            PageSize = None
                            PageStateKey = None
                            EditStateKey = None
                            DefaultSort = None
                            Source = Binding.Static(Some Seq.empty)
                            RowKey = None
                            RowKeyField = Some "id"
                            Columns = []
                            OnRowClick = None
                            Editable = false
                            Reorderable = false
                            StaticRows = None }
                      )
                    State = None
                    Style = None
                    Accessibility = None
                    Motion = Defaults.Motion.none
                    ExtraAttributes = None }

              let detail =
                  Fuaran.metric
                      "detail"
                      { Defaults.metric with
                          Label = TextSource.Literal "Selected"
                          Value = Binding.Selection("orders-grid", (fun (raw: obj) -> unbox raw), None, None) }

              let tree = dashboard "root" [ grid; detail ]

              match PreEmitValidate.validate tree with
              | Ok() -> ()
              | Error defects -> failtestf "Expected Ok for a valid master-detail pair, got: %A" defects
          }

          // ── Phase 640 — schema-grounded chart validation (FUARAN086–089) ──

          test "a chart field absent from an embedded table's schema fires ChartFieldUngrounded" {
              let table: Fuaran.Core.Table =
                  { Schema =
                      [ "quarter", Fuaran.Core.ColumnType.StringType
                        "revenue", Fuaran.Core.ColumnType.FloatType ]
                    Columns = [] }

              let chart: Node<Msg> =
                  Fuaran.chart
                      "cht"
                      { Defaults.chart<Msg> with
                          Kind = ChartKind.Bar
                          Source =
                              Binding.Transform(TransformSource.Data(Fuaran.Core.DataSource.Embedded table), [], None)
                          XField = "quarter"
                          YFields = [ "revenu" ] } // typo — absent from the schema

              match PreEmitValidate.validate chart with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.ChartFieldUngrounded("cht", "revenu"))
                      "the typo'd y-field is ungrounded"
              | Ok() -> failtest "Expected ChartFieldUngrounded"
          }

          test "a grounded chart over an embedded table passes; a non-empty pipeline is unknowable and passes" {
              let table: Fuaran.Core.Table =
                  { Schema =
                      [ "quarter", Fuaran.Core.ColumnType.StringType
                        "revenue", Fuaran.Core.ColumnType.FloatType ]
                    Columns = [] }

              let grounded: Node<Msg> =
                  Fuaran.chart
                      "cht"
                      { Defaults.chart<Msg> with
                          Kind = ChartKind.Bar
                          Source =
                              Binding.Transform(TransformSource.Data(Fuaran.Core.DataSource.Embedded table), [], None)
                          XField = "quarter"
                          YFields = [ "revenue" ] }

              match PreEmitValidate.validate grounded with
              | Ok() -> ()
              | Error defects -> failtestf "Expected Ok for a grounded chart, got: %A" defects

              // The same typo'd field behind a Derive pipeline: the output
              // schema is not statically derivable, so grounding must NOT fire.
              let piped: Node<Msg> =
                  Fuaran.chart
                      "cht2"
                      { Defaults.chart<Msg> with
                          Kind = ChartKind.Bar
                          Source =
                              Binding.Transform(
                                  TransformSource.Data(Fuaran.Core.DataSource.Embedded table),
                                  [ Fuaran.Core.Transform.Derive("variance", Fuaran.Core.ColExpr.Col "revenue") ],
                                  None
                              )
                          XField = "quarter"
                          YFields = [ "variance" ] }

              match PreEmitValidate.validate piped with
              | Ok() -> ()
              | Error defects -> failtestf "Expected Ok for a piped chart (unknowable schema), got: %A" defects
          }

          test "a string-typed y-field and a string-typed scatter x fire ChartFieldTypeMismatch" {
              let table: Fuaran.Core.Table =
                  { Schema =
                      [ "name", Fuaran.Core.ColumnType.StringType
                        "score", Fuaran.Core.ColumnType.FloatType ]
                    Columns = [] }

              let badY: Node<Msg> =
                  Fuaran.chart
                      "cht"
                      { Defaults.chart<Msg> with
                          Kind = ChartKind.Bar
                          Source =
                              Binding.Transform(TransformSource.Data(Fuaran.Core.DataSource.Embedded table), [], None)
                          XField = "name"
                          YFields = [ "name" ] } // string column as a value series

              match PreEmitValidate.validate badY with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.ChartFieldTypeMismatch("cht", "name", "string"))
                      "string y-field flagged"
              | Ok() -> failtest "Expected ChartFieldTypeMismatch for the y-field"

              let badScatterX: Node<Msg> =
                  Fuaran.chart
                      "cht2"
                      { Defaults.chart<Msg> with
                          Kind = ChartKind.Scatter
                          Source =
                              Binding.Transform(TransformSource.Data(Fuaran.Core.DataSource.Embedded table), [], None)
                          XField = "name" // scatter x must be numeric
                          YFields = [ "score" ] }

              match PreEmitValidate.validate badScatterX with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.ChartFieldTypeMismatch("cht2", "name", "string"))
                      "string scatter x flagged"
              | Ok() -> failtest "Expected ChartFieldTypeMismatch for the scatter x"
          }

          test "a multi-series pie fires ChartPieSeriesShape; stacked Line fires ChartStackedMeaningless" {
              let pie: Node<Msg> =
                  Fuaran.chart
                      "pie"
                      { Defaults.chart<Msg> with
                          Kind = ChartKind.Pie
                          YFields = [ "a"; "b" ] }

              match PreEmitValidate.validate pie with
              | Error defects ->
                  Expect.contains defects (PreEmitDefect.ChartPieSeriesShape("pie", 2)) "two-series pie flagged"
              | Ok() -> failtest "Expected ChartPieSeriesShape"

              let stackedLine: Node<Msg> =
                  Fuaran.chart
                      "ln"
                      { Defaults.chart<Msg> with
                          Kind = ChartKind.Line
                          YFields = [ "a" ]
                          Stacked = true }

              match PreEmitValidate.validate stackedLine with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.ChartStackedMeaningless("ln", "Line"))
                      "stacked Line flagged as dead intent"
              | Ok() -> failtest "Expected ChartStackedMeaningless"
          }

          // ── Phase 882 — the temporal axis's grounding rule (FUARAN097) ──
          //
          // The temporal axis is DECLARED, so the declaration is groundable —
          // which is the whole reason it is a declaration rather than an
          // inference. These are the accept/reject pair the rule turns on.

          test "FUARAN097: a temporal x-axis over a non-date column is REFUSED, not coerced" {
              let table: Fuaran.Core.Table =
                  { Schema =
                      [ "quarter", Fuaran.Core.ColumnType.StringType
                        "revenue", Fuaran.Core.ColumnType.FloatType ]
                    Columns = [] }

              let bad: Node<Msg> =
                  Fuaran.chart
                      "cht"
                      { Defaults.chart<Msg> with
                          Kind = ChartKind.Line
                          Source =
                              Binding.Transform(TransformSource.Data(Fuaran.Core.DataSource.Embedded table), [], None)
                          XField = "quarter" // a STRING column under a date axis
                          YFields = [ "revenue" ]
                          XScale = Some ChartXScale.Temporal }

              match PreEmitValidate.validate bad with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.ChartTemporalXNotDate("cht", "quarter", "string"))
                      "a string column under a temporal axis is refused"

                  let code, severity, message =
                      describe (PreEmitDefect.ChartTemporalXNotDate("cht", "quarter", "string"))

                  Expect.equal code "FUARAN097" "stable code"

                  Expect.equal
                      severity
                      DefectSeverity.Error
                      "an Error: silent coercion would draw every point on 1970-01-01"

                  Expect.stringContains message "temporal x-axis" "the message names what was declared"
                  Expect.stringContains message "drop xScale" "and states the two ways out"
              | Ok() -> failtest "Expected ChartTemporalXNotDate"

              // A NUMERIC column is refused for the same reason — the rule is
              // "not a date", not "not a string": an int column of 20260115 is
              // exactly the plausible near-miss a coercion would have swallowed.
              let numericX: Node<Msg> =
                  Fuaran.chart
                      "cht2"
                      { Defaults.chart<Msg> with
                          Kind = ChartKind.Line
                          Source =
                              Binding.Transform(
                                  TransformSource.Data(
                                      Fuaran.Core.DataSource.Embedded
                                          { Schema =
                                              [ "stamp", Fuaran.Core.ColumnType.IntType
                                                "revenue", Fuaran.Core.ColumnType.FloatType ]
                                            Columns = [] }
                                  ),
                                  [],
                                  None
                              )
                          XField = "stamp"
                          YFields = [ "revenue" ]
                          XScale = Some ChartXScale.Temporal }

              match PreEmitValidate.validate numericX with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.ChartTemporalXNotDate("cht2", "stamp", "int"))
                      "an int column is not a date either"
              | Ok() -> failtest "Expected ChartTemporalXNotDate for the int column"
          }

          test "FUARAN097 accepts date and timestamp columns, and stays silent where the schema is unknowable" {
              let dated (t: Fuaran.Core.ColumnType) : Node<Msg> =
                  Fuaran.chart
                      "cht"
                      { Defaults.chart<Msg> with
                          Kind = ChartKind.Line
                          Source =
                              Binding.Transform(
                                  TransformSource.Data(
                                      Fuaran.Core.DataSource.Embedded
                                          { Schema = [ "day", t; "sessions", Fuaran.Core.ColumnType.FloatType ]
                                            Columns = [] }
                                  ),
                                  [],
                                  None
                              )
                          XField = "day"
                          YFields = [ "sessions" ]
                          XScale = Some ChartXScale.Temporal }

              // BOTH date-ish types pass: a timestamp's time-of-day is discarded
              // by the lowering, which is a documented narrowing rather than a
              // type mismatch.
              for t in [ Fuaran.Core.ColumnType.DateType; Fuaran.Core.ColumnType.TimestampType ] do
                  match PreEmitValidate.validate (dated t) with
                  | Ok() -> ()
                  | Error defects -> failtestf "Expected Ok for a %A x column, got: %A" t defects

              // A TEMPORAL SCATTER over a date column must not trip FUARAN087
              // either: its x is read as dates, so "not numeric" is the wrong
              // question. Without that narrowing a correctly-authored
              // time-series scatter would be refused for the column it declared.
              let temporalScatter: Node<Msg> =
                  Fuaran.chart
                      "sct"
                      { Defaults.chart<Msg> with
                          Kind = ChartKind.Scatter
                          Source =
                              Binding.Transform(
                                  TransformSource.Data(
                                      Fuaran.Core.DataSource.Embedded
                                          { Schema =
                                              [ "day", Fuaran.Core.ColumnType.DateType
                                                "latency", Fuaran.Core.ColumnType.FloatType ]
                                            Columns = [] }
                                  ),
                                  [],
                                  None
                              )
                          XField = "day"
                          YFields = [ "latency" ]
                          XScale = Some ChartXScale.Temporal }

              match PreEmitValidate.validate temporalScatter with
              | Ok() -> ()
              | Error defects -> failtestf "Expected Ok for a temporal scatter, got: %A" defects

              // UNKNOWABLE SCHEMA: a host `Static` row source carries no schema,
              // so the declaration passes ungrounded — the fuaran-core#90 rule,
              // refuse only what is PROVABLY wrong. A validator that guessed
              // here would refuse every legitimate bound date axis.
              let ungrounded: Node<Msg> =
                  Fuaran.chart
                      "cht3"
                      { Defaults.chart<Msg> with
                          Kind = ChartKind.Line
                          Source = Binding.Static(Some Seq.empty)
                          XField = "day"
                          YFields = [ "sessions" ]
                          XScale = Some ChartXScale.Temporal }

              match PreEmitValidate.validate ungrounded with
              | Ok() -> ()
              | Error defects -> failtestf "Expected Ok for an unknowable source, got: %A" defects
          }

          // ── Phase 781: the walk is depth-bounded ────────────────────────────
          //
          // This walk was plainly recursive with no counter. Measured, it dies on
          // the .NET default 1 MB stack at 294 levels in Release and 151 in
          // Debug — with a `StackOverflowException`, which .NET cannot catch, so
          // the "every defect in one list" contract could not be honoured at all
          // past that point: there was no list, and no process.
          //
          // The fixture is built ITERATIVELY. Building it with a recursive
          // helper would overflow while constructing the INPUT, which tests the
          // test rather than the walker.

          test "FUARAN091: a tree past MaxDepth is reported, once, instead of overflowing" {
              let mutable tree: Node<Msg> = Fuaran.markdown "leaf" "x"

              for i in 1 .. WireLimits.MaxDepth do
                  tree <- dashboard (sprintf "d%d" i) [ tree ]

              match PreEmitValidate.validate tree with
              | Ok() -> failtest "Expected MaxDepthExceeded"
              | Error defects ->
                  let depthDefects =
                      defects
                      |> List.filter (fun d ->
                          match d with
                          | PreEmitDefect.MaxDepthExceeded _ -> true
                          | _ -> false)

                  match depthDefects with
                  | [ PreEmitDefect.MaxDepthExceeded(_, limit) ] ->
                      Expect.equal limit WireLimits.MaxDepth "the defect carries the limit it breached"
                  | other ->
                      failtestf "expected exactly one MaxDepthExceeded (not one per over-deep node), got %A" other

                  let code, severity, _ = describe depthDefects.Head
                  Expect.equal code "FUARAN091" "stable code"
                  Expect.equal severity DefectSeverity.Error "an over-deep tree is an error, not an advisory"
          }

          test "a tree exactly at MaxDepth validates cleanly" {
              // The limit must admit what it claims to admit — otherwise a guard
              // and a decoder that refuses everything look the same.
              let mutable tree: Node<Msg> = Fuaran.markdown "leaf" "x"

              for i in 1 .. WireLimits.MaxDepth - 1 do
                  tree <- dashboard (sprintf "d%d" i) [ tree ]

              match PreEmitValidate.validate tree with
              | Ok() -> ()
              | Error defects -> failtestf "a tree exactly at MaxDepth was rejected: %A" defects
          }

          // ─── Phase 862: the two authored shapes that declare paging that
          // does not page. The decorative-pager shape needs no rule — the pager
          // is renderer-owned, so it cannot be wired to nothing.

          test "FUARAN093: pageSize without pageStateKey is an error" {
              let grid = pagedGrid (Some 20) None (Binding.Static(Some Seq.empty))

              match PreEmitValidate.validate grid with
              | Error defects ->
                  Expect.contains defects (PreEmitDefect.PageSizeWithoutPageKey "paged") "the defect is raised"
                  let code, severity, _ = describe (PreEmitDefect.PageSizeWithoutPageKey "paged")
                  Expect.equal code "FUARAN093" "stable code"
                  Expect.equal severity DefectSeverity.Error "a page size that pages nothing is dead intent"
              | Ok() -> failtest "Expected FUARAN093, got Ok"
          }

          test "FUARAN093 go-red check: the same grid WITH a pageStateKey is clean" {
              // The rule must admit what it claims to admit. Without this the
              // test above passes for a validator that rejects every grid.
              let grid =
                  pagedGrid (Some 20) (Some "members-page") (Binding.Static(Some Seq.empty))

              match PreEmitValidate.validate grid with
              | Ok() -> ()
              | Error defects -> failtestf "a correctly paged grid was rejected: %A" defects
          }

          test "FUARAN096: client-side paging over a query that already pages host-side warns" {
              let hostPaged =
                  Binding.Query("members", (fun (_: obj) -> Seq.empty), Some [ "members-page" ])

              let grid = pagedGrid (Some 20) (Some "members-page") hostPaged

              match PreEmitValidate.validate grid with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.DoublePagedGrid("paged", "members-page"))
                      "the double-paging defect is raised"

                  let code, severity, _ =
                      describe (PreEmitDefect.DoublePagedGrid("paged", "members-page"))

                  Expect.equal code "FUARAN096" "stable code"

                  Expect.equal
                      severity
                      DefectSeverity.Warning
                      "the renderer resolves this correctly, so it is a caution rather than a break"
              | Ok() -> failtest "Expected FUARAN096, got Ok"
          }

          test "FUARAN096 go-red check: a query depending on some OTHER key does not warn" {
              // The rule keys off the page key specifically. A query that
              // depends on a filter is the ordinary shape and must not be
              // reported as double-paged.
              //
              // Asserted as "no DoublePagedGrid" rather than "no defects at
              // all": a bare grid naming a filter also trips FUARAN031's
              // dangling-filter rule, which is orthogonal to paging. Widening
              // this to a clean-tree assertion would make it fail for a reason
              // that has nothing to do with what it tests — which is exactly
              // what it did on first run, and is why it is written this way.
              let filterDriven =
                  Binding.Query("members", (fun (_: obj) -> Seq.empty), Some [ "region-filter" ])

              let grid = pagedGrid (Some 20) (Some "members-page") filterDriven

              let defects =
                  match PreEmitValidate.validate grid with
                  | Ok() -> []
                  | Error ds -> ds

              let doublePaged =
                  defects
                  |> List.filter (function
                      | PreEmitDefect.DoublePagedGrid _ -> true
                      | _ -> false)

              Expect.isEmpty doublePaged "a filter-driven query is not double paging"
          }

          // ─── Phase 861: a sort declaration that cannot be honoured. The
          // narrowing rule is DIRECTIONAL — a column may turn a behaviour off,
          // never on — so the widening attempt is refused rather than ignored.

          test "FUARAN094: a column declaring sortable=true under a grid with no sortStateKey is refused" {
              let grid = sortGrid None None [ ("Month", Some "month", Some true) ]

              match PreEmitValidate.validate grid with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.UnhonourableSort("sorted", SortDefect.NoSortStateKey "Month"))
                      "the widening attempt is reported"

                  let code, severity, _ =
                      describe (PreEmitDefect.UnhonourableSort("sorted", SortDefect.NoSortStateKey "Month"))

                  Expect.equal code "FUARAN094" "stable code"
                  Expect.equal severity DefectSeverity.Error "a sort that cannot happen is an error"
              | Ok() -> failtest "Expected FUARAN094, got Ok"
          }

          test "FUARAN094: a defaultSort past the column set is refused" {
              let grid = sortGrid (Some "s") (Some 5) [ ("Month", Some "month", None) ]

              match PreEmitValidate.validate grid with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.UnhonourableSort("sorted", SortDefect.DefaultSortColumnOutOfRange(5, 1)))
                      "the out-of-range declared order is reported"
              | Ok() -> failtest "Expected FUARAN094, got Ok"
          }

          test "FUARAN094 go-red check: the honourable shapes are clean" {
              // The rule must admit what it claims to admit. Two shapes that
              // LOOK like the refused ones and are legitimate: a column opting
              // OUT under a grid with no sort key (narrowing is always allowed,
              // even to no effect), and a declared order with no sort key at
              // all (an initial order without interactive re-sorting).
              let optOut = sortGrid None None [ ("Month", Some "month", Some false) ]
              let declaredOnly = sortGrid None (Some 0) [ ("Month", Some "month", None) ]

              for g, label in [ optOut, "column opting out"; declaredOnly, "declared order, no sort key" ] do
                  let defects =
                      match PreEmitValidate.validate g with
                      | Ok() -> []
                      | Error ds -> ds

                  let sortDefects =
                      defects
                      |> List.filter (function
                          | PreEmitDefect.UnhonourableSort _ -> true
                          | _ -> false)

                  Expect.isEmpty sortDefects (sprintf "%s is legitimate" label)
          }

          // ─── Phase 863: the write side's twin. Same directional rule, and a
          // second shape — an editable column with nowhere for the edit to go.

          test "FUARAN095: a column declaring editable=true under a non-editable grid is refused" {
              let grid = editGrid false None (Binding.State("rows", None)) [ ("Note", Some true) ]

              match PreEmitValidate.validate grid with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.UneditableColumnDeclared("edited", "Note", EditDefect.GridNotEditable))
                      "the widening attempt is reported"

                  let code, severity, _ =
                      describe (PreEmitDefect.UneditableColumnDeclared("edited", "Note", EditDefect.GridNotEditable))

                  Expect.equal code "FUARAN095" "stable code"
                  Expect.equal severity DefectSeverity.Error "an edit that cannot happen is an error"
              | Ok() -> failtest "Expected FUARAN095, got Ok"
          }

          test "FUARAN095: an editable column with no reachable destination is refused" {
              // Editable grid, editable column, but a Query source and no
              // editStateKey — the decoded-and-inert shape census row #27 names.
              let grid =
                  editGrid true None (Binding.Query("rows", (fun (_: obj) -> Seq.empty), None)) [ ("Note", Some true) ]

              match PreEmitValidate.validate grid with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.UneditableColumnDeclared("edited", "Note", EditDefect.NoReachableDestination))
                      "the unreachable destination is reported"
              | Ok() -> failtest "Expected FUARAN095, got Ok"
          }

          test "FUARAN095 go-red check: a declared editStateKey makes the same grid legitimate" {
              // The one-field difference between this and the test above is the
              // whole of what Phase 863 adds. It also pins the FUARAN090
              // widening: a declared destination means the grid is no longer
              // inert, so the older rule must stop firing too — otherwise 863
              // would ship a field that reports itself as dead intent.
              let grid =
                  editGrid
                      true
                      (Some "stock-adjustments")
                      (Binding.Query("rows", (fun (_: obj) -> Seq.empty), None))
                      [ ("Note", Some true) ]

              let defects =
                  match PreEmitValidate.validate grid with
                  | Ok() -> []
                  | Error ds -> ds

              Expect.isEmpty
                  (defects
                   |> List.filter (function
                       | PreEmitDefect.UneditableColumnDeclared _ -> true
                       | _ -> false))
                  "a declared destination is reachable"

              Expect.isEmpty
                  (defects
                   |> List.filter (function
                       | PreEmitDefect.InertEditableGrid _ -> true
                       | _ -> false))
                  "FUARAN090 must not fire once a destination is declared"
          }

          test "FUARAN095 go-red check: a column opting OUT is always legitimate" {
              let grid =
                  editGrid false None (Binding.State("rows", None)) [ ("Note", Some false) ]

              let defects =
                  match PreEmitValidate.validate grid with
                  | Ok() -> []
                  | Error ds -> ds

              Expect.isEmpty
                  (defects
                   |> List.filter (function
                       | PreEmitDefect.UneditableColumnDeclared _ -> true
                       | _ -> false))
                  "narrowing is allowed even to no effect"
          }

          // ─── Phase 932: FUARAN098 — a `SetState` writing a key nothing reads.
          // 866's fake-affordance property over the authored tree. Every test
          // below has a go-red partner in which the SAME write IS read, because a
          // rule that fires on everything is indistinguishable from one that
          // works right up until someone writes a legitimate tree.

          test "FUARAN098: a SetState writing a key nothing reads warns" {
              let tree = setStateTree "draft" [ markdown "copy" "nothing reads it" ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  Expect.contains defects (PreEmitDefect.SetStateNoReader("writer", "draft")) "the defect is raised"

                  let code, severity, _ = describe (PreEmitDefect.SetStateNoReader("writer", "draft"))

                  Expect.equal code "FUARAN098" "stable code"

                  Expect.equal
                      severity
                      DefectSeverity.Warning
                      "a key may legitimately be written for a HOST to read, which is why this cannot be an Error"
              | Ok() -> failtest "Expected FUARAN098, got Ok"
          }

          test "FUARAN098 go-red check: a plain State-bound reader clears it" {
              let tree = setStateTree "draft" [ stateReader "shown" "draft" ]
              Expect.isEmpty (noReaderDefects tree) "a bound reader makes the write real"
          }

          test "FUARAN098 go-red check: a Switch SELECTOR counts as a reader" {
              // The regression that matters most. `SwitchSpec.On` became a
              // Binding in Phase 768, and the shared walk still described it as a
              // literal string — so a button driving a Switch, the canonical
              // honest affordance in this language, would have been reported as
              // fake.
              let tree = setStateTree "tab" [ switchReader "sw" "tab" ]
              Expect.isEmpty (noReaderDefects tree) "the branch selector reads the key"
          }

          test "FUARAN098 go-red check: a grid's pageStateKey counts as a reader" {
              // A plain STRING the renderer reads, with no Binding to see.
              let tree =
                  setStateTree
                      "members-page"
                      [ pagedGrid (Some 20) (Some "members-page") (Binding.Static(Some Seq.empty)) ]

              Expect.isEmpty (noReaderDefects tree) "the pager reads the key it is named with"
          }

          test "FUARAN098 go-red check: a Transform's live State SOURCE counts as a reader" {
              // The slot Phase 865 deliberately left out of `Reads`, folded in
              // once the blast radius was measured (nothing in the corpus and
              // nothing in either suite changed verdict). It had to be folded:
              // the REACTIVE walk has subscribed this slot since Phase 818
              // (`Render.keysOfBinding`'s `TransformSource.Live` arm), so a
              // `SetState` on the source key re-evaluates the pipeline and
              // re-renders every reader — and the rule beside it was calling
              // that same write invisible.
              //
              // Both source shapes are one read. Whether the slot carries its
              // own `defaultValue` decides FUARAN105's verdict (does the
              // pipeline start from real rows or from `emptySource`); it never
              // decides whether the key is READ.
              let defaulted =
                  setStateTree "members" [ derivedBadge "count" "members" (Some memberRows) ]

              let defaultLess = setStateTree "members" [ derivedBadge "count" "members" None ]

              Expect.isEmpty (noReaderDefects defaulted) "a Transform over $state.members reads members"

              Expect.isEmpty
                  (noReaderDefects defaultLess)
                  "a default-less source is FUARAN105's subject and still a reader"
          }

          test "FUARAN098: a host-reserved key is exempt" {
              // Such a write is REFUSED at dispatch on every path (Phase 782), so
              // its defect is that it is unaddressable, not that it is unread —
              // a different finding, in a different place. Exempted through the
              // guard's own prefix rather than a second list beside it.
              let tree =
                  setStateTree (StateKeyPolicy.HostReservedPrefix + "secret") [ markdown "copy" "x" ]

              Expect.isEmpty (noReaderDefects tree) "the host-reserved namespace is not this rule's business"
          }

          test "FUARAN098: an OPAQUE reader stands the rule down for the whole tree" {
              // A `Computed` closure is handed the entire state bag, so absence
              // of a visible read proves nothing. Refuse only what is PROVABLY
              // wrong (the fuaran-core#90 rule).
              let opaque =
                  Fuaran.metric
                      "computed"
                      { Defaults.metric with
                          Label = TextSource.Literal "M"
                          Value = Binding.Computed(fun _ -> 0.0) }

              let tree = setStateTree "draft" [ opaque ]

              Expect.isEmpty (noReaderDefects tree) "an unprovable claim is not a finding"
          }

          // ─── Phase 765: FUARAN102 — a hardcoded date where `Now` was meant.
          // The heuristic is deliberately conservative, so most of these tests
          // are go-red partners: what it must NOT say is the larger half of what
          // makes a candidate finding worth reading.

          test "FUARAN102: a present-tense label beside a hardcoded date warns" {
              let tree =
                  dashboard "root" [ fact "asof" (TextSource.Literal "Today") (TextSource.Literal "2026-08-24") ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.DateLiteralWhereNowPlausible("asof", "2026-08-24"))
                      "the defect is raised, carrying the offending literal"

                  let code, severity, _ =
                      describe (PreEmitDefect.DateLiteralWhereNowPlausible("asof", "2026-08-24"))

                  Expect.equal code "FUARAN102" "stable code"

                  Expect.equal
                      severity
                      DefectSeverity.Warning
                      "a candidate finding — the author may have meant the stated date"
              | Ok() -> failtest "Expected FUARAN102, got Ok"
          }

          test "FUARAN102: the cue and the date may share one literal" {
              let tree =
                  dashboard
                      "root"
                      [ fact "asof" (TextSource.Literal "Status") (TextSource.Literal "Last updated 2026-08-24") ]

              Expect.equal
                  (staleDateDefects tree)
                  [ PreEmitDefect.DateLiteralWhereNowPlausible("asof", "Last updated 2026-08-24") ]
                  "one node, one finding, whichever slot carries the pairing"
          }

          test "FUARAN102 go-red check: a Now-bound value clears it" {
              // The shipped remedy. A `Now` binding carries no literal at all, so
              // the rule cannot reach it — which is the whole reason the check is
              // lexical rather than semantic.
              let nowValue = TextSource.Bound(Binding.Now(fun _ -> ""))

              let tree = dashboard "root" [ fact "asof" (TextSource.Literal "Today") nowValue ]
              Expect.isEmpty (staleDateDefects tree) "the host furnishes the instant — nothing to warn about"
          }

          test "FUARAN102 go-red check: a date with no present-tense cue is silent" {
              // An obviously-historical date. The distinction is made by the CUE,
              // not by comparing against a clock: the validator is pure, and a
              // rule whose verdict moved with the calendar would pass in CI today
              // and fail next quarter for no reconstructable reason.
              let tree =
                  dashboard "root" [ fact "founded" (TextSource.Literal "Founded") (TextSource.Literal "1994-03-02") ]

              Expect.isEmpty (staleDateDefects tree) "a stated historical date is exactly what a Fact is for"
          }

          test "FUARAN102 go-red check: a present-tense cue with no date is silent" {
              let tree =
                  dashboard "root" [ fact "asof" (TextSource.Literal "Today") (TextSource.Literal "17 open") ]

              Expect.isEmpty (staleDateDefects tree) "half the pairing is ordinary prose"
          }

          test "FUARAN102 go-red check: a digit run that is not a date is silent" {
              // `12345-67-890` has the shape and none of the semantics: the month
              // is out of range and the neighbours are digits.
              let tree =
                  dashboard "root" [ fact "part" (TextSource.Literal "Today") (TextSource.Literal "12345-67-890") ]

              Expect.isEmpty (staleDateDefects tree) "a part number is not a date"
          }

          test "FUARAN102 go-red check: prose is out of scope" {
              // `Markdown` is deliberately not a covered kind — long-form prose is
              // where a legitimately historical date sits beside a present-tense
              // sentence, and firing there would make the rule untrustworthy.
              let tree =
                  dashboard "root" [ markdown "copy" "Today we are still shipping what we planned on 2026-01-05." ]

              Expect.isEmpty (staleDateDefects tree) "narrative is not a labelled datum"
          }

          // ─── Phase 768: FUARAN103 — a `Switch` selecting on a key nothing can
          // write. Every test has a go-red partner in which the same selector IS
          // writable, because the rule reasons from an ABSENCE and an absence is
          // only evidence where the walk can see everything.

          test "FUARAN103: a Switch on a key nothing writes warns" {
              let tree = dashboard "root" [ switchReader "sw" "occupancyTier" ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.SwitchKeyNoWriter("sw", "occupancyTier"))
                      "the defect is raised"

                  let code, severity, _ =
                      describe (PreEmitDefect.SwitchKeyNoWriter("sw", "occupancyTier"))

                  Expect.equal code "FUARAN103" "stable code"

                  Expect.equal
                      severity
                      DefectSeverity.Warning
                      "the HOST may write the key, and the validator cannot see the host"
              | Ok() -> failtest "Expected FUARAN103, got Ok"
          }

          test "FUARAN103 go-red check: a SetState writer clears it" {
              // The canonical honest affordance: a button writes the key the
              // Switch selects on.
              let tree = setStateTree "tab" [ switchReader "sw" "tab" ]
              Expect.isEmpty (noWriterDefects tree) "a declared writer makes the branch reachable"
          }

          test "FUARAN103 go-red check: a control write-back slot counts as a writer" {
              // A Select bound to the key writes it back when no handler is
              // supplied — a writer with no `Action` anywhere in the tree.
              let picker =
                  Fuaran.select
                      "picker"
                      { Defaults.select<Msg> with
                          Label = TextSource.Literal "Tier"
                          Source = Binding.Static(Some [])
                          Value = Binding.State("tier", None) }

              let tree = dashboard "root" [ picker; switchReader "sw" "tier" ]
              Expect.isEmpty (noWriterDefects tree) "the write-back default writes the slot it is bound to"
          }

          test "FUARAN103 go-red check: an OPAQUE writer stands the rule down" {
              // A grid's row-click handler is a closure over the row: an arbitrary
              // action, so an arbitrary write. Under it the absence of a visible
              // writer proves nothing, and refusing an unprovable claim is how a
              // Warning stays worth reading.
              let grid = sortGrid None None [ ("Ward", Some "ward", None) ]

              let opaqueGrid =
                  match grid.Kind with
                  | NodeKind.DataGrid spec ->
                      { grid with
                          Kind =
                              NodeKind.DataGrid
                                  { spec with
                                      OnRowClick = Some(fun _ -> Action.Navigate "/x") } }
                  | _ -> grid

              let tree = dashboard "root" [ opaqueGrid; switchReader "sw" "occupancyTier" ]
              Expect.isEmpty (noWriterDefects tree) "an unprovable claim is not a finding"
          }

          test "FUARAN103: a host-reserved key is exempt" {
              let tree =
                  dashboard "root" [ switchReader "sw" (StateKeyPolicy.HostReservedPrefix + "theme") ]

              Expect.isEmpty (noWriterDefects tree) "the host writes its own namespace by definition"
          }

          test "FUARAN103 go-red check: an EMPTY key is FUARAN083's case, not this one" {
              let tree = dashboard "root" [ switchReader "sw" "" ]

              Expect.isEmpty (noWriterDefects tree) "a malformed selector and an unreachable one are different findings"
          }

          // ─── Phase 865: FUARAN105 — a Transform over a State source nothing
          // can fill. Every test has a go-red partner, because the rule reasons
          // from an ABSENCE (no carried default, no writer) and an absence is
          // only evidence where the walk can see everything.

          test "FUARAN105: a Transform over a default-less State source warns" {
              let tree = dashboard "root" [ derivedBadge "count" "members" None ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.TransformSourceInert("count", "members"))
                      "the defect is raised"

                  let code, severity, _ =
                      describe (PreEmitDefect.TransformSourceInert("count", "members"))

                  Expect.equal code "FUARAN105" "stable code"

                  Expect.equal
                      severity
                      DefectSeverity.Warning
                      "the HOST may populate the key, and the validator cannot see the host"
              | Ok() -> failtest "Expected FUARAN105, got Ok"
          }

          test "FUARAN105: the charter's §3.1 pair — a seeding grid DOES rescue the badge (Phase 1075)" {
              // THE INVERSION, and it is the whole of Phase 1075 read through
              // this rule. Under 865's per-reader fallback the grid's default was
              // never written into the store, so the badge beside it derived from
              // the empty table and rendered zero forever — and this assertion
              // read `Expect.contains`. Under the seeding rule the grid's
              // declaration seeds `$state.members`, the badge's Transform
              // resolves against it, and the pair means what it looks like it
              // means. The rule widens to the charter §6 wording it was written
              // with: it fires where NOTHING in the tree seeds the key.
              //
              // Keeping the old assertion would have left two rules contradicting
              // each other in the same binary — the resolver saying the slot is
              // filled and the validator saying it can never be.
              let tree =
                  dashboard "root" [ seedingGrid "grid" "members"; derivedBadge "count" "members" None ]

              Expect.isEmpty (inertSourceDefects tree) "a sibling reader's declared default seeds the slot"
          }

          test "FUARAN105 go-red check: the sibling seed must be on the SAME key" {
              // The partner to the inversion above. Standing down on any seed
              // anywhere would make the rule unfalsifiable; it stands down on a
              // seed for THIS key.
              let tree =
                  dashboard "root" [ seedingGrid "grid" "other"; derivedBadge "count" "members" None ]

              Expect.contains
                  (inertSourceDefects tree)
                  (PreEmitDefect.TransformSourceInert("count", "members"))
                  "a seed on a different key fills a different slot"
          }

          test "FUARAN105 go-red check: the source's OWN default clears it" {
              // The decoder derives the Transform's initial snapshot from the
              // source binding's carried default, so the pipeline runs over real
              // rows and the silent zero cannot arise.
              let tree = dashboard "root" [ derivedBadge "count" "members" (Some memberRows) ]

              Expect.isEmpty (inertSourceDefects tree) "a carried default makes the initial snapshot real"
          }

          test "FUARAN105 go-red check: a SetState writer clears it" {
              // A default-less source is fine when the channel changes: the Live
              // transform re-derives on the write, so the empty first snapshot is
              // a starting state rather than a permanent wrong answer.
              let tree = setStateTree "members" [ derivedBadge "count" "members" None ]

              Expect.isEmpty (inertSourceDefects tree) "a declared writer makes the source fillable"
          }

          test "FUARAN105 go-red check: an OPAQUE writer stands the rule down" {
              // A grid's row-click handler is a closure over the row: an arbitrary
              // action, so an arbitrary write. Under it "nothing writes this key"
              // is unprovable rather than false.
              let grid = sortGrid None None [ ("Team", Some "team", None) ]

              let opaqueGrid =
                  match grid.Kind with
                  | NodeKind.DataGrid spec ->
                      { grid with
                          Kind =
                              NodeKind.DataGrid
                                  { spec with
                                      OnRowClick = Some(fun _ -> Action.Navigate "/x") } }
                  | _ -> grid

              let tree = dashboard "root" [ opaqueGrid; derivedBadge "count" "members" None ]
              Expect.isEmpty (inertSourceDefects tree) "an unprovable claim is not a finding"
          }

          test "FUARAN105: a host-reserved key is exempt" {
              let tree =
                  dashboard "root" [ derivedBadge "count" (StateKeyPolicy.HostReservedPrefix + "members") None ]

              Expect.isEmpty (inertSourceDefects tree) "the host fills its own namespace by definition"
          }

          test "FUARAN105 go-red check: an EMPTY key is not this rule's subject" {
              let tree = dashboard "root" [ derivedBadge "count" "" None ]

              Expect.isEmpty (inertSourceDefects tree) "a malformed source and an unfillable one are different findings"
          }

          test "FUARAN105 go-red check: a Data source is not its subject" {
              // `TransformSource.Data` is the columnar / `ref` shape. It names no
              // state key, so there is no slot to be unfillable.
              let badge =
                  Fuaran.badge
                      "count"
                      { Defaults.badge with
                          Label =
                              TextSource.Bound(
                                  Binding.Transform(
                                      TransformSource.Data HostPrelude.TransformLive.emptySource,
                                      groupCount,
                                      None
                                  )
                              ) }

              Expect.isEmpty (inertSourceDefects (dashboard "root" [ badge ])) "an embedded table names no slot"
          }

          // ─── Phase 1075: FUARAN106 — two declarations of one seeded slot.
          // Decidable from the tree alone, so it is an Error; every test has a
          // go-red partner because the whole risk of an Error rule here is that
          // it reads two SPELLINGS of one table as a disagreement.

          test "FUARAN106: two readers declaring DIFFERENT defaults for one key is an Error" {
              let other =
                  Fuaran.Core.JArr [ Fuaran.Core.JObj [ "team", Fuaran.Core.JStr "Research" ] ]

              let tree =
                  dashboard
                      "root"
                      [ derivedBadge "a" "members" (Some memberRows)
                        derivedBadge "b" "members" (Some other) ]

              match seedConflicts tree with
              | [ PreEmitDefect.ConflictingStateSeeds(key, first, second) ] ->
                  Expect.equal key "members" "the contested key"
                  Expect.equal first "a" "the declaration that wins — first in walk order"
                  Expect.equal second "b" "the declaration that is silently discarded"

                  let code, severity, _ =
                      describe (PreEmitDefect.ConflictingStateSeeds(key, first, second))

                  Expect.equal code "FUARAN106" "stable code"

                  Expect.equal
                      severity
                      DefectSeverity.Error
                      "both declarations are in the tree — the disagreement needs no host to decide it"
              | other -> failtestf "Expected exactly one FUARAN106, got %A" other
          }

          test "FUARAN106 go-red check: two readers declaring the SAME default agree" {
              let tree =
                  dashboard
                      "root"
                      [ derivedBadge "a" "members" (Some memberRows)
                        derivedBadge "b" "members" (Some memberRows) ]

              Expect.isEmpty (seedConflicts tree) "agreement is not a conflict"
          }

          test "FUARAN106 go-red check: ROW-MAJOR and COLUMNAR spellings of one table agree" {
              // The reason this rule can afford to be an Error. A grid carries
              // rows as an array of row objects; a Transform's live source
              // carries the same data canonically columnar. Comparing the raw
              // values would refuse the most idiomatic shape the pack teaches,
              // so both are normalised through the transpose + columnar decode
              // the decode-time snapshot already uses.
              let tree =
                  dashboard
                      "root"
                      [ gridWithSource "grid" (Binding.State("members", Some memberRowsTyped))
                        derivedBadge "count" "members" (Some memberRows) ]

              Expect.isEmpty (seedConflicts tree) "one table spelled two ways is one table"
          }

          test "FUARAN106 go-red check: one declaration and one bare reader is the shape we want" {
              let tree =
                  dashboard "root" [ seedingGrid "grid" "members"; derivedBadge "count" "members" None ]

              Expect.isEmpty (seedConflicts tree) "declaring once and reading everywhere is the point of the rule"
          }

          test "FUARAN106: a host-reserved key is exempt" {
              // The seeding pass refuses to seed a host slot at all (Phase 782),
              // so two declarations there contest a slot neither can fill — a
              // different defect, not this one.
              let key = StateKeyPolicy.HostReservedPrefix + "members"

              let other =
                  Fuaran.Core.JArr [ Fuaran.Core.JObj [ "team", Fuaran.Core.JStr "Research" ] ]

              let tree =
                  dashboard "root" [ derivedBadge "a" key (Some memberRows); derivedBadge "b" key (Some other) ]

              Expect.isEmpty (seedConflicts tree) "the tree cannot seed a host slot, so it cannot contest one"
          }

          // ─── Phase 1075: FUARAN107 — two inline copies of one table. The lint
          // the charter asks for, and the rule that names the emission the
          // charter was written about.

          test "FUARAN107: a grid and a Transform each carrying the same rows warns" {
              let tree =
                  dashboard
                      "root"
                      [ gridWithSource "grid" (Binding.Static(Some memberRowsTyped))
                        embeddedBadge "count" memberTable ]

              match duplicateTables tree with
              | [ PreEmitDefect.DuplicateInlineTable(first, second, seedKey) ] ->
                  Expect.equal first "grid" "the earlier copy"
                  Expect.equal second "count" "the later copy"
                  Expect.isNone seedKey "neither copy declares a key yet — that is the remedy, not the state"

                  let code, severity, _ =
                      describe (PreEmitDefect.DuplicateInlineTable(first, second, seedKey))

                  Expect.equal code "FUARAN107" "stable code"

                  Expect.equal severity DefectSeverity.Warning "identical is not the same claim as meant-to-be-one"
              | other -> failtestf "Expected exactly one FUARAN107, got %A" other
          }

          test "FUARAN107: the remedy names the key when one copy already declares it" {
              let tree =
                  dashboard
                      "root"
                      [ gridWithSource "grid" (Binding.State("members", Some memberRowsTyped))
                        embeddedBadge "count" memberTable ]

              match duplicateTables tree with
              | [ PreEmitDefect.DuplicateInlineTable(_, _, Some key) ] ->
                  Expect.equal key "members" "the seeded key the second copy should point at"
              | other -> failtestf "Expected a FUARAN107 naming the key, got %A" other
          }

          test "FUARAN107 go-red check: two readers of ONE key are sharing, not duplicating" {
              // The shape Phase 1075 exists to make possible. Both copies name
              // `members`, so there is one source however many readers point at
              // it — reporting it would name the fix as the defect.
              let tree =
                  dashboard
                      "root"
                      [ gridWithSource "grid" (Binding.State("members", Some memberRowsTyped))
                        derivedBadge "count" "members" (Some memberRows) ]

              Expect.isEmpty (duplicateTables tree) "one shared name is one table"
          }

          test "FUARAN107 go-red check: DIFFERENT tables are not copies" {
              let otherTable =
                  match
                      HostPrelude.TransformLive.initialSource (
                          Fuaran.Core.JArr [ Fuaran.Core.JObj [ "team", Fuaran.Core.JStr "Research" ] ]
                      )
                  with
                  | Ok ds -> ds
                  | Error _ -> failwith "fixture"

              let tree =
                  dashboard
                      "root"
                      [ gridWithSource "grid" (Binding.Static(Some memberRowsTyped))
                        embeddedBadge "count" otherTable ]

              Expect.isEmpty (duplicateTables tree) "two tables that differ are two tables"
          }

          test "FUARAN107 go-red check: two EMPTY sources are not duplicated data" {
              // Every unpopulated live Transform decodes to `emptySource`, so
              // pairing empties would fire on trees carrying no inline data.
              let tree =
                  dashboard
                      "root"
                      [ embeddedBadge "a" HostPrelude.TransformLive.emptySource
                        embeddedBadge "b" HostPrelude.TransformLive.emptySource ]

              Expect.isEmpty (duplicateTables tree) "an empty table is not a copy of anything"
          }

          // ─── Phase 864: FUARAN099/100/101 — the declared-rule family. Every
          // test has a go-red partner, because all three reason from something
          // the author did NOT do (name a reachable key, pick a control that can
          // honour the slot, leave the bound in one place) and an absence is only
          // evidence where the walk can see everything.

          test "FUARAN099: a compare against a key no field owns and nothing writes is an Error" {
              let rule =
                  { emptyRule with
                      Compare =
                          Some
                              { Against = Binding.State("hireStartDate", None)
                                Op = CompareOp.Gte } }

              let tree =
                  dashboard "root" [ ruleForm [ ruleField "end-date" (textKind "end-date") (Some rule) ] ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.CompareKeyUnreachable("frm", "end-date", "hireStartDate"))
                      "the defect is raised"

                  let code, severity, _ =
                      describe (PreEmitDefect.CompareKeyUnreachable("frm", "end-date", "hireStartDate"))

                  Expect.equal code "FUARAN099" "stable code"

                  Expect.equal
                      severity
                      DefectSeverity.Error
                      "a dangling state key is decidable from the tree alone, so it is refused rather than warned"
              | Ok() -> failtest "Expected FUARAN099, got Ok"
          }

          test "FUARAN099 go-red check: a sibling field owning the key clears it" {
              // The canonical cross-field shape: the operand names a sibling
              // field's id, and the auto-bind puts that field's value in State
              // under exactly that key.
              let rule =
                  { emptyRule with
                      Compare =
                          Some
                              { Against = Binding.State("start-date", None)
                                Op = CompareOp.Gte } }

              let tree =
                  dashboard
                      "root"
                      [ ruleForm
                            [ ruleField "start-date" (textKind "start-date") None
                              ruleField "end-date" (textKind "end-date") (Some rule) ] ]

              Expect.isEmpty (ruleDefects tree) "the sibling's value is what the predicate reads"
          }

          test "FUARAN099 go-red check: a host-reserved key is exempt" {
              let rule =
                  { emptyRule with
                      Compare =
                          Some
                              { Against = Binding.State(StateKeyPolicy.HostReservedPrefix + "tenant", None)
                                Op = CompareOp.Eq } }

              let tree =
                  dashboard "root" [ ruleForm [ ruleField "tenant" (textKind "tenant") (Some rule) ] ]

              Expect.isEmpty (ruleDefects tree) "the host writes its own namespace by definition"
          }

          test "FUARAN100: a format on a TextArea is dead intent and warns" {
              let rule =
                  { emptyRule with
                      Format = Some TextFormat.Email }

              let kind = FormFieldKind.TextArea(Some(Binding.State("notes", Some "")), None, 4)

              let tree = dashboard "root" [ ruleForm [ ruleField "notes" kind (Some rule) ] ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  let expected =
                      PreEmitDefect.RuleSlotUnhonourable("frm", "notes", RuleSlot.Format, "TextArea")

                  Expect.contains defects expected "the defect is raised"

                  let code, severity, _ = describe expected
                  Expect.equal code "FUARAN100" "stable code"

                  Expect.equal
                      severity
                      DefectSeverity.Warning
                      "the projection is the host's, so refusing the tree would decide it for every host"
              | Ok() -> failtest "Expected FUARAN100, got Ok"
          }

          test "FUARAN100: a pattern on a Checkbox is dead intent and warns" {
              let rule =
                  { emptyRule with
                      Pattern = Some "[0-9]+" }

              let kind = FormFieldKind.Checkbox(Some(Binding.State("agree", Some false)), None)

              let tree = dashboard "root" [ ruleForm [ ruleField "agree" kind (Some rule) ] ]

              Expect.contains
                  (ruleDefects tree)
                  (PreEmitDefect.RuleSlotUnhonourable("frm", "agree", RuleSlot.Pattern, "Checkbox"))
                  "a checkbox has no string to match"
          }

          test "FUARAN100 go-red check: the same slots on a Text control are honourable" {
              let rule =
                  { emptyRule with
                      Format = Some TextFormat.Email
                      Pattern = Some ".+@example\\.com"
                      MinLength = Some 3
                      MaxLength = Some 64 }

              let tree =
                  dashboard "root" [ ruleForm [ ruleField "email" (textKind "email") (Some rule) ] ]

              Expect.isEmpty (ruleDefects tree) "Text honours every string-shaped slot"
          }

          test "FUARAN100 go-red check: a length pair on a TextArea is honourable — only `format` is not" {
              let rule =
                  { emptyRule with
                      MinLength = Some 10
                      MaxLength = Some 500 }

              let kind = FormFieldKind.TextArea(Some(Binding.State("notes", Some "")), None, 4)

              let tree = dashboard "root" [ ruleForm [ ruleField "notes" kind (Some rule) ] ]

              Expect.isEmpty (ruleDefects tree) "a textarea has a length; it has no input type"
          }

          test "FUARAN101: a literal compare duplicating the control's own min warns" {
              let rule =
                  { emptyRule with
                      Compare =
                          Some
                              { Against = Binding.Static(Some(Fuaran.Core.JFloat 1.0))
                                Op = CompareOp.Gte } }

              let kind =
                  FormFieldKind.RangedNumber(Some(Binding.State("qty", Some 1.0)), None, Some 1.0, Some 99.0, None)

              let tree = dashboard "root" [ ruleForm [ ruleField "qty" kind (Some rule) ] ]

              match PreEmitValidate.validate tree with
              | Error defects ->
                  let expected =
                      PreEmitDefect.CompareDuplicatesBound("frm", "qty", "RangedNumber.min")

                  Expect.contains defects expected "the defect is raised"

                  let code, severity, _ = describe expected
                  Expect.equal code "FUARAN101" "stable code"

                  Expect.equal
                      severity
                      DefectSeverity.Warning
                      "a literal operand is legal — it is just the shape that collapses the rule/bound distinction"
              | Ok() -> failtest "Expected FUARAN101, got Ok"
          }

          test "FUARAN101 go-red check: a BINDING operand is the whole point and never warns" {
              // The distinction the rule exists to protect: a rule slot does not
              // duplicate a control bound BECAUSE its operand reads something
              // that changes, where the control's bound is a literal.
              let rule =
                  { emptyRule with
                      Compare =
                          Some
                              { Against = Binding.State("floor", None)
                                Op = CompareOp.Gte } }

              let kind =
                  FormFieldKind.RangedNumber(Some(Binding.State("qty", Some 1.0)), None, Some 1.0, Some 99.0, None)

              let tree =
                  dashboard
                      "root"
                      [ ruleForm [ ruleField "floor" (textKind "floor") None; ruleField "qty" kind (Some rule) ] ]

              Expect.isEmpty (ruleDefects tree) "reading a sibling is what the rule slot is for"
          }

          test "FUARAN101 go-red check: a literal compare on a control with NO bound is not a duplicate" {
              let rule =
                  { emptyRule with
                      Compare =
                          Some
                              { Against = Binding.Static(Some(Fuaran.Core.JFloat 1.0))
                                Op = CompareOp.Gte } }

              let kind =
                  FormFieldKind.RangedNumber(Some(Binding.State("qty", Some 1.0)), None, None, None, None)

              let tree = dashboard "root" [ ruleForm [ ruleField "qty" kind (Some rule) ] ]

              Expect.isEmpty (ruleDefects tree) "there is no second source to disagree with"
          }

          test "FUARAN101 go-red check: the OPPOSITE bound is not the one duplicated" {
              // `gte` names a lower bound, so a control declaring only `max`
              // duplicates nothing — the rule is direction-aware rather than
              // firing on the mere presence of any bound.
              let rule =
                  { emptyRule with
                      Compare =
                          Some
                              { Against = Binding.Static(Some(Fuaran.Core.JFloat 1.0))
                                Op = CompareOp.Gte } }

              let kind =
                  FormFieldKind.RangedNumber(Some(Binding.State("qty", Some 1.0)), None, None, Some 99.0, None)

              let tree = dashboard "root" [ ruleForm [ ruleField "qty" kind (Some rule) ] ]

              Expect.isEmpty (ruleDefects tree) "a lower-bound compare and an upper bound are different claims"
          }

          // ── Phase 727 — the accessibility family ──────────────────────────

          test "FUARAN109 flags an interactive node that reaches a screen reader with no name" {
              let tree = dashboard "root" [ a11yButton "save" "" ]

              match a11yDefects tree with
              | [ PreEmitDefect.InteractiveWithoutAccessibleName(nodeId, kind, slot) ] ->
                  Expect.equal nodeId "save" "the offending node"
                  Expect.equal kind "Button" "the wire kind, as a host reads it"
                  Expect.equal slot "label" "the naming slot that is empty"
              | other -> failtestf "Expected one FUARAN109, got %A" other
          }

          test "FUARAN109/110/111 are all WARNING severity — the family's recorded posture" {
              // The decision the phase records: advisory on adoption, because
              // the rules run on every existing emission the moment they ship
              // and a gate that is red on arrival is one people step over.
              let unnamed = PreEmitDefect.InteractiveWithoutAccessibleName("n", "Button", "label")

              let dangling =
                  PreEmitDefect.DanglingAccessibilityReference("n", "labelledBy", "gone")

              let empty = PreEmitDefect.EmptyAccessibilityDeclaration("n", "label")

              for d in [ unnamed; dangling; empty ] do
                  Expect.equal (severityOf d) DefectSeverity.Warning "advisory, not refusal"
          }

          test "FUARAN109 go-red check: a named interactive node is silent" {
              let tree = dashboard "root" [ a11yButton "save" "Save" ]

              Expect.isEmpty (a11yDefects tree) "the structural label supplies the accessible name"
          }

          test "FUARAN109 go-red check: a whitespace-only name is still no name" {
              // Admitting `\" \"` would make the rule evadable by a space, which
              // is worse than not having it — the tree would then carry a green
              // gate saying it had been checked.
              let tree = dashboard "root" [ a11yButton "save" "   " ]

              Expect.equal (a11yCodes tree) [ "FUARAN109" ] "whitespace is empty"
          }

          test "FUARAN109 go-red check: a BOUND label is never judged" {
              // It resolves at render time from data no pre-emit walk can see,
              // so calling it empty would be a guess. The family errs towards
              // silence — the same restraint FUARAN108 shows for Media.Label.
              let tree = dashboard "root" [ boundLabelButton "save" ]

              Expect.isEmpty (a11yDefects tree) "an unresolvable name is not an absent one"
          }

          test "FUARAN109 trap: a blank label WITH a declared accessibility.label is not flagged" {
              // The Phase 717 trap, stated as a test. A button whose structural
              // label is blank and whose trait names it is odd-looking and
              // perfectly announced: the browser's name computation is
              // trait label → aria-labelledby target → text content, and the
              // first arm is satisfied. Flagging it would be exactly the false
              // positive an audit cannot afford.
              let tree =
                  dashboard "root" [ a11yButton "save" "" |> withTrait (traitLabel (Binding.Static(Some "Save"))) ]

              Expect.isEmpty (a11yDefects tree) "a declared name IS a name"
          }

          test "FUARAN109 trap: a blank label with a RESOLVING labelledBy is not flagged" {
              let tree =
                  dashboard
                      "root"
                      [ markdown "caption" "Save the document"
                        a11yButton "save" ""
                        |> withTrait
                            { Defaults.Accessibility.empty with
                                LabelledBy = Some "caption" } ]

              Expect.isEmpty (a11yDefects tree) "the second arm of the name computation is satisfied"
          }

          test "FUARAN109 is derived from the language's defaults: a Link is not audited" {
              // `Fuaran.link` passes `Defaults.Accessibility.none`, so the
              // language does not declare a Link interactive and the rule does
              // not reach it — even though a Link has a structural `label` slot
              // that is just as empty. The pin is one-directional by design:
              // read the default, never a table beside it.
              let tree = dashboard "root" [ Fuaran.link "docs" "https://example.invalid" "" ]

              Expect.isEmpty (a11yDefects tree) "the language's own default is the gate"
          }

          test "FUARAN109 covers Select, Form and FileUpload, each by its own naming slot" {
              let select =
                  Fuaran.select
                      "choice"
                      { Defaults.select<Msg> with
                          Label = TextSource.Literal ""
                          Value = Binding.State("choice", None) }

              let form =
                  Fuaran.form
                      "details"
                      { Defaults.form<Msg> with
                          SubmitLabel = TextSource.Literal "" }

              let upload =
                  Fuaran.fileUpload
                      "attach"
                      { Defaults.fileUpload<Msg> with
                          Label = TextSource.Literal "" }

              let slots =
                  a11yDefects (dashboard "root" [ select; form; upload ])
                  |> List.map (function
                      | PreEmitDefect.InteractiveWithoutAccessibleName(nodeId, _, slot) -> nodeId, slot
                      | other -> failtestf "Expected FUARAN109, got %A" other)

              Expect.equal
                  slots
                  [ "choice", "label"; "details", "submitLabel"; "attach", "label" ]
                  "a form is named through its submit button; the others through their own label"
          }

          test "FUARAN110 flags an accessibility reference naming a node the tree does not carry" {
              let tree =
                  dashboard
                      "root"
                      [ a11yButton "save" "Save"
                        |> withTrait
                            { Defaults.Accessibility.empty with
                                LabelledBy = Some "no-such-node" } ]

              match a11yDefects tree with
              | [ PreEmitDefect.DanglingAccessibilityReference(nodeId, slot, target) ] ->
                  Expect.equal nodeId "save" "the referring node"
                  Expect.equal slot "labelledBy" "the slot"
                  Expect.equal target "no-such-node" "the missing target"
              | other -> failtestf "Expected one FUARAN110, got %A" other
          }

          test "FUARAN110 covers describedBy as well as labelledBy" {
              let tree =
                  dashboard
                      "root"
                      [ a11yButton "save" "Save"
                        |> withTrait
                            { Defaults.Accessibility.empty with
                                DescribedBy = Some "missing-help" } ]

              Expect.equal (a11yCodes tree) [ "FUARAN110" ] "both reference slots are judged"
          }

          test "FUARAN110 go-red check: a reference that resolves is silent" {
              let tree =
                  dashboard
                      "root"
                      [ markdown "help" "Saves your work"
                        a11yButton "save" "Save"
                        |> withTrait
                            { Defaults.Accessibility.empty with
                                DescribedBy = Some "help" } ]

              Expect.isEmpty (a11yDefects tree) "the target is in the tree"
          }

          test "FUARAN111 flags a declared accessibility slot left empty" {
              let tree =
                  dashboard "root" [ a11yButton "save" "Save" |> withTrait (traitLabel (Binding.Static(Some ""))) ]

              match a11yDefects tree with
              | [ PreEmitDefect.EmptyAccessibilityDeclaration(nodeId, slot) ] ->
                  Expect.equal nodeId "save" "the offending node"
                  Expect.equal slot "label" "the empty slot"
              | other -> failtestf "Expected one FUARAN111, got %A" other
          }

          test "FUARAN111 flags a Static slot with no value at all" {
              let tree =
                  dashboard "root" [ a11yButton "save" "Save" |> withTrait (traitLabel (Binding.Static None)) ]

              Expect.equal (a11yCodes tree) [ "FUARAN111" ] "declared and valueless is declared and empty"
          }

          test "FUARAN111 is what closes the hole FUARAN109's declared-name escape opens" {
              // The pair's whole reason for shipping together. An empty declared
              // label satisfies `declaresAccessibleName`, so it SILENCES
              // FUARAN109 — the defect suppresses its own detection — while the
              // renderer drops the empty `aria-label` and the element is named
              // by nothing. Exactly one code fires, and it is the right one.
              let tree =
                  dashboard "root" [ a11yButton "save" "" |> withTrait (traitLabel (Binding.Static(Some " "))) ]

              Expect.equal (a11yCodes tree) [ "FUARAN111" ] "109 is silenced; 111 catches what silenced it"
          }

          test "FUARAN111 owns an EMPTY reference slot — 110 does not double-report it" {
              // An empty `labelledBy` names nothing and so is trivially
              // dangling too. Reporting one value under two codes is noise
              // rather than coverage, so the reference collector excludes it.
              let tree =
                  dashboard
                      "root"
                      [ a11yButton "save" "Save"
                        |> withTrait
                            { Defaults.Accessibility.empty with
                                LabelledBy = Some "" } ]

              Expect.equal (a11yCodes tree) [ "FUARAN111" ] "one value, one finding"
          }

          test "FUARAN111 go-red check: a real declared name is silent" {
              let tree =
                  dashboard "root" [ a11yButton "save" "Save" |> withTrait (traitLabel (Binding.Static(Some "Save"))) ]

              Expect.isEmpty (a11yDefects tree) "a declared name that names something"
          }

          test "FUARAN111 go-red check: a non-Static label binding is never judged" {
              let tree =
                  dashboard
                      "root"
                      [ a11yButton "save" "Save"
                        |> withTrait (traitLabel (Binding.State("caption", None))) ]

              Expect.isEmpty (a11yDefects tree) "it resolves at runtime; emptiness is unprovable here"
          }

          test "the accessibility family is silent on a clean tree" {
              let tree =
                  dashboard
                      "root"
                      [ markdown "help" "Saves your work"
                        a11yButton "save" "Save"
                        |> withTrait
                            { Defaults.Accessibility.empty with
                                Role = Some AriaRole.Button
                                Label = Some(Binding.Static(Some "Save the document"))
                                DescribedBy = Some "help" } ]

              Expect.isEmpty (a11yDefects tree) "nothing to report"
          } ]
