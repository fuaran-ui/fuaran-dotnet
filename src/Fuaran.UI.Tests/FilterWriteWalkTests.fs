module Fuaran.UI.Tests.FilterWriteWalk

// ============================================================================
//  Phase 1785 — the binding walk records a filter WRITE.
//
//  Before this phase the walk answered "where does this tree write?" on the
//  State channel only (`writeBackTargetOf`), so a control driving a FILTER — a
//  `Select` whose `values` binds `Binding.Filter`, the corpus's
//  `multiselect-chip-list-param` — was recorded as a read alone and projected by
//  the wiring graph as an ungrounded consumer: the tree looked broken and was
//  not.
//
//  Four things are under test, and the first is the one that decides the rest:
//
//   1. THE CLOSED LIST OF POSITIONS, pinned as a table. A position is on the
//      list iff the reference host's write-back path (`writeBackTo`, reached
//      directly or through `fieldChange`) actually writes there. The table
//      carries the two positions that are deliberately OFF it — `Stepper` and
//      `Toast` — beside the eight that are on, so removing an exclusion is as
//      red as removing an inclusion, and adding a position to the vocabulary is
//      a deliberate edit here rather than a silent widening.
//
//   2. THE DESTINATION FUNCTION mirrors the renderer's arms, including the two
//      places it deliberately differs from its State twin.
//
//   3. THE CHIP GUARD. A `Filters` chip writes its DECLARED name, not whatever
//      its value slot binds, so the cross-reading shape (declare `alpha`, read
//      `beta`) must not record a write to `beta`. This is the one way the
//      derivation could name the WRONG filter, so it is tested in the direction
//      that catches it.
//
//   4. THAT `collect` DID NOT MOVE. Every shipped rule reads `TreeBindingFacts`,
//      and the whole design of the change is that the new fact rides beside it;
//      the corpus-wide rule differential is the measurement, and this is its
//      structural half, run on every tree the table builds.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types

type private Msg = NoOp

/// The filter writes a tree records, sorted so the assertion does not depend on
/// walk order (which is a property of how the tree is spelled).
let private filterWritesOf (root: Node<Msg>) : (string * string) list =
    (BindingWalk.collectFacts root).FilterWrites |> List.sort

/// The empty table a Transform's static source stands on; the pipeline here is
/// empty, so the rows are irrelevant and only the PARAM's binding is under test.
let private emptyTable: Fuaran.Core.Table = { Schema = []; Columns = [] }

let private dashboard (children: Node<Msg> list) : Node<Msg> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<Msg> with
            Children = children }

// ── the trees, one per write-back position ──────────────────────────────────

let private selectValue () =
    Fuaran.select
        "sel"
        { Defaults.select<Msg> with
            Value = Binding.Filter("region", None) }

let private selectValues () =
    Fuaran.select
        "sel"
        { Defaults.select<Msg> with
            Multiple = Some true
            Values = Some(Binding.Filter("depts", None)) }

let private tabsActiveIndex () =
    Fuaran.tabs
        "tabs"
        { Defaults.tabs<Msg> with
            ActiveIndex = Binding.Filter("pane", None) }

let private tabsActiveTag () =
    Fuaran.tabs
        "tabs"
        { Defaults.tabs<Msg> with
            ActiveTag = Some(Binding.Filter("tag", None)) }

let private disclosureOpen () =
    Fuaran.disclosure
        "disc"
        { Defaults.disclosure<Msg> with
            Open = Binding.Filter("expanded", None) }

let private modalOpen () =
    Fuaran.modal
        "modal"
        { Defaults.modal<Msg> with
            Open = Binding.Filter("shown", None) }

let private formFieldValue () =
    Fuaran.form
        "form"
        { Defaults.form<Msg> with
            Fields =
                [ { Defaults.formField<Msg> with
                      Id = "q"
                      Kind = FormFieldKind.Text(Some(Binding.Filter("query", None)), None) } ] }

let private editableGridSource () =
    Fuaran.grid
        "grid"
        id
        { Defaults.grid<Fuaran.Core.Row, Msg> with
            Editable = true
            Source = Binding.Filter("rows", None) }

/// OFF the list: the walk treats `ActiveStep` as a State write-back position,
/// and the reference renderer only ever RESOLVES it — it never calls the
/// write-back path for a stepper.
let private stepperActiveStep () =
    Fuaran.stepper
        "step"
        { Defaults.stepper<Msg> with
            ActiveStep = Binding.Filter("stage", None) }

/// OFF the list, for the same reason: a `Toast`'s dismiss button carries no
/// handler in the reference renderer, so `Open` is read and never written.
let private toastOpen () =
    Fuaran.toast
        "toast"
        { Defaults.toast with
            Open = Binding.Filter("visible", None) }
    : Node<Msg>

/// (position, tree, the writes it must record). One row per write-back position
/// the walk knows about — `noteWriteBackOf`'s callers plus the two that keep
/// their state-only `noteWriteBack`.
let private positions: (string * Node<Msg> * (string * string) list) list =
    [ "Select.value", selectValue (), [ "sel", "region" ]
      "Select.values", selectValues (), [ "sel", "depts" ]
      "Tabs.activeIndex", tabsActiveIndex (), [ "tabs", "pane" ]
      "Tabs.activeTag", tabsActiveTag (), [ "tabs", "tag" ]
      "Disclosure.open", disclosureOpen (), [ "disc", "expanded" ]
      "Modal.open", modalOpen (), [ "modal", "shown" ]
      "Form field value", formFieldValue (), [ "form", "query" ]
      "editable DataGrid.source", editableGridSource (), [ "grid", "rows" ]
      "Stepper.activeStep (OFF the list)", stepperActiveStep (), []
      "Toast.open (OFF the list)", toastOpen (), [] ]

[<Tests>]
let tests =
    testList
        "filter write-back walk (Phase 1785)"
        [

          // ── 1. the closed list ────────────────────────────────────────────

          test "the closed list of write-back positions is exactly this table" {
              for (name, tree, expected) in positions do
                  Expect.equal (filterWritesOf tree) (List.sort expected) (name + ": recorded filter writes")
          }

          test "an unwritable slot on a listed position records nothing" {
              // The falsifier for the table above: every row would also pass if
              // the walk recorded a write for any binding at all. A `Query`
              // binding is not a write-back target on either channel, so a
              // listed position holding one must record nothing.
              let inert =
                  Fuaran.select
                      "sel"
                      { Defaults.select<Msg> with
                          Value = Binding.Query("options", string, None) }

              Expect.isEmpty (filterWritesOf inert) "a Query-bound value slot commits to no filter"
          }

          test "a read that is NOT a write-back position records nothing" {
              // A `Heading` reads a filter through its text and writes nowhere;
              // it must not become a control.
              let reader =
                  Fuaran.heading
                      "h"
                      { Defaults.heading with
                          Text = TextSource.Bound(Binding.Filter("region", None)) }

              Expect.isEmpty (filterWritesOf reader) "a display read is not a write"
          }

          // ── 2. the destination function ───────────────────────────────────

          test "filterWriteTargetOf mirrors the renderer's write-back arms" {
              Expect.equal
                  (BindingWalk.filterWriteTargetOf (Binding.Filter("region", None): Binding<string>))
                  (Some "region")
                  "a Filter slot commits to its own name"

              // WIDER than `isWriteBackTarget`, deliberately: the renderer's
              // arm is `Binding.Filter(name, _)`, so a carried default changes
              // nothing about whether the write happens.
              Expect.equal
                  (BindingWalk.filterWriteTargetOf (Binding.Filter("region", Some "all"): Binding<string>))
                  (Some "region")
                  "a carried default does not stop the write"

              Expect.equal
                  (BindingWalk.filterWriteTargetOf (Binding.State("k", None): Binding<string>))
                  None
                  "a State slot commits to the state store, which is the other function's answer"

              Expect.equal
                  (BindingWalk.filterWriteTargetOf (Binding.Static(Some "x"): Binding<string>))
                  None
                  "a static slot is the inert-control condition"
          }

          test "a Local buffer never commits to a filter" {
              // `writeBackTo` never receives a `Local` — a buffer's commit is
              // flushed by `commitLocalTo`, which CONSTRUCTS a `Binding.State`,
              // so there is no path by which a buffer reaches the filter store.
              // Asserted over a buffer whose re-sync source IS a filter, which
              // is the shape that would wrongly pass if the recursion were
              // copied from `writeBackTargetOf`.
              let buffered: Binding<string> =
                  Binding.Local(LocalFlushTrigger.OnBlur, id, Binding.Filter("region", None), None, Ok, None, None)

              Expect.equal
                  (BindingWalk.filterWriteTargetOf buffered)
                  None
                  "a buffer over a filter source still commits through the state arm"
          }

          // ── 3. the chip guard ─────────────────────────────────────────────

          test "a Filters chip records no filter write, even when it reads another chip's name" {
              // The renderer writes `FilterStore.set spec.Name`, not the value
              // slot's binding, so reading the slot here would name `beta` for a
              // chip that writes `alpha`. The declaration is already carried by
              // `TreeBindingFacts.DeclaredFilters`.
              let crossChip =
                  Fuaran.filters
                      "chips"
                      [ { Name = "alpha"
                          Label = TextSource.Literal "alpha"
                          Kind = FormFieldKind.Text(Some(Binding.Filter("beta", None)), None) } ]

              let facts = BindingWalk.collectFacts crossChip

              Expect.isEmpty facts.FilterWrites "a chip's value slot is not a filter-write position"

              Expect.equal
                  facts.Bindings.DeclaredFilters
                  [ "chips", "alpha" ]
                  "the chip's real destination is its declared name, and it is already recorded"
          }

          // ── 4. `collect` did not move ─────────────────────────────────────

          test "collect returns exactly collectFacts' Bindings half, on every tree above" {
              for (name, tree, _) in positions do
                  Expect.equal
                      (BindingWalk.collect tree)
                      (BindingWalk.collectFacts tree).Bindings
                      (name + ": the published facts are the composite's own half")
          }

          // ── the projection the walk now grounds ───────────────────────────

          test "a Select driving a filter projects as a CONTROL, and grounds its reader" {
              // The motivating shape, hand-built so the assertion does not
              // depend on the corpus being present: a multi-select writing
              // `depts`, and a second node whose Transform param reads it.
              let tree =
                  dashboard
                      [ selectValues ()
                        Fuaran.grid
                            "dept-grid"
                            id
                            { Defaults.grid<Fuaran.Core.Row, Msg> with
                                Source =
                                    Binding.Transform(
                                        TransformSource.Data(Fuaran.Core.DataSource.Embedded emptyTable),
                                        [],
                                        Some
                                            [ { Name = "depts"
                                                From = Binding.Filter("depts", None) } ]
                                    ) } ]

              let g = WiringGraph.project tree

              let controls =
                  g.Controls
                  |> List.filter (fun c -> c.Channel = WiringGraph.WiringChannel.Filter)
                  |> List.map (fun c -> c.NodeId, c.Name, WiringGraph.ControlKind.name c.Kind)

              Expect.equal controls [ "sel", "depts", "filter-write-back" ] "the writing Select is the control"

              Expect.isTrue
                  (g.Edges
                   |> List.exists (fun e ->
                       e.Channel = WiringGraph.WiringChannel.Filter
                       && e.Name = "depts"
                       && e.Control = "sel"
                       && e.Consumer = "dept-grid"))
                  "the control drives the reader that names its filter"

              // The self-loop guard: the Select reads the same slot it writes,
              // and an edge from it to itself would assert it drives a reader.
              Expect.isFalse
                  (g.Edges |> List.exists (fun e -> e.Control = "sel" && e.Consumer = "sel"))
                  "a control's read of its own slot is not consumption"

              Expect.isEmpty
                  (g.Unresolved
                   |> List.filter (fun u ->
                       match u with
                       | WiringGraph.UnresolvedWiring.UngroundedConsumer r ->
                           r.Channel = WiringGraph.WiringChannel.Filter
                       | _ -> false))
                  "nothing on the filter channel is ungrounded any more"
          }

          test "the corpus fixture this phase exists for is grounded, and only it moved" {
              // The other side of the row Phase 1785 removed from
              // `WiringGraphTests`' ungrounded-edge table. Asserted over the
              // shared corpus rather than a hand-built twin, because the claim is
              // about a document nobody here authored.
              match Fuaran.Tests.CorpusRoot.tryFind () with
              | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
              | Some root ->
                  let path = System.IO.Path.Combine(root, "nodes", "multiselect-chip-list-param.json")

                  if not (System.IO.File.Exists path) then
                      skiptest "the corpus does not carry this fixture"
                  else

                      let g =
                          match Ops.JsonDecode.decodeNodeObj (System.IO.File.ReadAllText path) with
                          | Ok node -> WiringGraph.project node
                          | Error e -> failtestf "the fixture failed to decode: %s at %s" e.Code e.Path

                      Expect.isTrue
                          (g.Controls
                           |> List.exists (fun c ->
                               c.NodeId = "dept-chip"
                               && c.Channel = WiringGraph.WiringChannel.Filter
                               && c.Kind = WiringGraph.ControlKind.FilterWriteBack))
                          "the multi-select that writes `depts` is a control"

                      Expect.isTrue
                          (g.Edges
                           |> List.exists (fun e -> e.Control = "dept-chip" && e.Consumer = "dept-grid"))
                          "and it grounds the grid param that reads it"

                      Expect.isEmpty
                          (g.Unresolved
                           |> List.filter (fun u ->
                               match u with
                               | WiringGraph.UnresolvedWiring.UngroundedConsumer r ->
                                   r.Channel = WiringGraph.WiringChannel.Filter
                               | _ -> false))
                          "so nothing in this fixture is ungrounded on the filter channel"
          }

          test "a filter-writing control is not EXPRESS, so it is never reported undriven" {
              // Read off the shipped rules like every other entry in that split:
              // no rule fires on a control whose value slot happens to be a
              // filter nothing else reads, and a host may legitimately furnish
              // the slot. Reporting every one would bury the decorative-chip
              // finding the case exists to surface.
              Expect.isFalse
                  (WiringGraph.ControlKind.isExpress WiringGraph.ControlKind.FilterWriteBack)
                  "a write-back position is not a declaration to drive"

              let lone = WiringGraph.project (dashboard [ selectValues () ])

              Expect.isEmpty
                  (lone.Unresolved
                   |> List.filter (fun u ->
                       match u with
                       | WiringGraph.UnresolvedWiring.UndrivenControl c ->
                           c.Kind = WiringGraph.ControlKind.FilterWriteBack
                       | _ -> false))
                  "a filter-writing control that nothing reads is an ordinary tree"
          } ]

// ============================================================================
//  Phase 1801 — the STATE half of the same closed list, and the premise the
//  shard got wrong.
//
//  Phase 1785 applied one membership rule — a position is a write-back position
//  iff the reference host's `writeBackTo` can be shown to write it on a user act
//  — and applied it to the FILTER channel only. `Stepper.activeStep` and
//  `Toast.open` failed that rule and were excluded there, while the STATE
//  projection beside them went on recording both: one closed list, asserted two
//  ways. These tests pin the state half, so removing a position from one channel
//  and not the other is as red as removing it from neither.
//
//  The shard's own second premise — that each "projects as a CONTROL that drives
//  whatever reads that key" in the wiring graph — is REFUTED and the refutation
//  is asserted here rather than only narrated. `WiringGraph.ofFacts` builds its
//  State controls from `StateKeys.Writes`, the reader-TAGGED `Action.SetState`
//  list, and deliberately never reads the untagged `WriteKeys` set these
//  positions reached (its own comment says why: a `Set<string>` has no writing
//  node to put on an edge). So no stepper was ever a control, the
//  `drives(control, consumer)` hazard Phase 1780 inherits never existed on this
//  path, and `WiringGraph.render`'s bytes do not move for this change — they
//  could not have.
// ============================================================================

/// The state keys a tree can be shown to write — `WriteKeys`, the untagged set
/// the four "nothing writes this key" rules read.
let private writeKeysOf (root: Node<Msg>) : string list =
    (BindingWalk.collect root).StateKeys.WriteKeys |> Set.toList |> List.sort

[<Tests>]
let statefulPositions =
    testList
        "Phase 1801 — the state half of the closed list"
        [ test "a state-bound write-back position contributes its key" {
              // The positive control, and the falsifier for the two exclusions
              // below: without it, a walk that recorded NOTHING on this channel
              // would pass them both.
              let disc =
                  Fuaran.disclosure
                      "disc"
                      { Defaults.disclosure<Msg> with
                          Open = Binding.State("expanded", None) }

              Expect.equal (writeKeysOf disc) [ "expanded" ] "a Disclosure's open slot is written by the header toggle"

              let modal =
                  Fuaran.modal
                      "modal"
                      { Defaults.modal<Msg> with
                          Open = Binding.State("shown", None) }

              Expect.equal (writeKeysOf modal) [ "shown" ] "a Modal's open slot is written by its close gesture"
          }

          test "Stepper.activeStep is off the write-back set on BOTH channels" {
              // The renderer resolves `ActiveStep` to mark the active step and
              // its step-header click runs `OnSelect` or nothing; it reaches
              // `writeBackTo` on no path. Recording a write here claimed a key
              // had a writer when nothing in the tree could be shown to write
              // it — which is what the four `WriteKeys` rules reason from.
              let stepper =
                  Fuaran.stepper
                      "step"
                      { Defaults.stepper<Msg> with
                          ActiveStep = Binding.State("stage", None) }

              Expect.isEmpty (writeKeysOf stepper) "a stepper writes no state key"
              Expect.isEmpty (filterWritesOf (stepperActiveStep ())) "and no filter name"
          }

          test "Toast.open is off the write-back set on BOTH channels" {
              // A `Toast`'s dismiss button carries no handler at all in the
              // reference renderer, so `Open` decides the `hidden` attribute
              // and is written by nothing.
              let toast =
                  Fuaran.toast
                      "toast"
                      { Defaults.toast with
                          Open = Binding.State("visible", None) }
                  : Node<Msg>

              Expect.isEmpty (writeKeysOf toast) "a toast writes no state key"
              Expect.isEmpty (filterWritesOf (toastOpen ())) "and no filter name"
          }

          test "a Stepper's OnSelect is still an opaque writer" {
              // The exclusion is a claim about the SLOT, not about the closure.
              // A present `OnSelect` may dispatch any action at all, so the
              // write-side rules must still stand down for the whole tree — and
              // a session that read the exclusion as "a stepper writes nothing"
              // would have taken that with it.
              let wired =
                  Fuaran.stepper
                      "step"
                      { Defaults.stepper<Msg> with
                          ActiveStep = Binding.State("stage", None)
                          OnSelect = Some(fun _ -> Action.Chain []) }

              Expect.isTrue (BindingWalk.collect wired).StateKeys.OpaqueWriter "a closure may write anything"
          }

          test "no renderer-driven key is a control in the wiring graph — and none ever was" {
              // The shard's premise, checked against the tree rather than taken
              // at face value. `WiringGraph` builds State controls from
              // `StateKeys.Writes` — the reader-tagged `Action.SetState` list —
              // and never from `WriteKeys`, which is where a stepper's key went.
              // So this assertion passed before the change as well; it is here
              // so that a future projection reaching for the untagged set goes
              // red at the moment it does, rather than handing Phase 1780's
              // `drives` predicate wiring no user can operate.
              let tree =
                  dashboard
                      [ Fuaran.stepper
                            "step"
                            { Defaults.stepper<Msg> with
                                ActiveStep = Binding.State("stage", None) }
                        Fuaran.heading
                            "h"
                            { Defaults.heading with
                                Text = TextSource.Bound(Binding.State("stage", None)) } ]

              let graph = WiringGraph.project tree

              Expect.isEmpty
                  (graph.Controls |> List.filter (fun c -> c.Name = "stage"))
                  "a stepper does not drive the key it displays"

              Expect.isNonEmpty
                  (graph.Consumers |> List.filter (fun c -> c.Name = "stage"))
                  "though the heading that reads it is a consumer — so the filter above is not vacuous"
          } ]
