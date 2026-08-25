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
          } ]
