module Fuaran.UI.Tests.AgGridParity

// ============================================================================
//  Phase 1611 — the two grid BACKENDS answer the same declared tree the same
//  way, and this file is where that stops being an assertion.
//
//  Six declarative grid behaviours reached the wire between Phases 861 and
//  1125. The AG Grid adapter read none of them: `sortable` was boxed `true` on
//  every column it emitted, and `pageSize` / `pageStateKey` / `editStateKey` /
//  `reorderable` / `transferInKey` / `transferOutKey` / `exportable` did not
//  appear in the file at all. So the SAME decoded tree behaved differently on
//  the two backends, and which behaviour a reader got depended only on whether
//  the host happened to have wired an adapter.
//
//  ── What makes this test able to fail ────────────────────────────────────
//  The phase's own subject is fields that were INERT, so a test that passed
//  because the field was never exercised would be the defect wearing a green
//  tick. Three things are done about that, deliberately:
//
//    1. The inputs are the SHIPPED CORPUS's grid fixtures, decoded through the
//       shipped decoder — not specs written here to suit the assertion. A
//       fixture is what an emitter actually emits.
//    2. Every behaviour is required to be HONOURED by the first-party leg in at
//       least one fixture (`the corpus exercises every behaviour`). A behaviour
//       no fixture declares is one this file proves nothing about, and that is
//       reported rather than silently passed.
//    3. The detectors are shown to FIRE. A parity walk that found no grids, a
//       refusal predicate that never says no, and a per-column narrowing that
//       returns the same answer whatever the column declares would each pass
//       every assertion above vacuously; each is pinned by its own test against
//       a case whose answer is known.
//
//  ── The rule being asserted ──────────────────────────────────────────────
//  For every field a grid DECLARES: the AG backend either agrees with the
//  first-party backend, or it declines with a NAMED reason. What it may never
//  do is decline silently — which is exactly the state this phase found, and
//  the one an unnamed refusal would restore.
// ============================================================================

open System.IO
open Expecto

open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Ops.JsonDecode
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.AgGridPlan

/// The corpus root — the `wire-format-fixtures/` clone, wherever it sits above
/// the test binary. Same climb as the other corpus-driven suites.
let private corpusRoot () : string option =
    let rec climb (dir: DirectoryInfo option) =
        match dir with
        | None -> None
        | Some d ->
            let candidate = Path.Combine(d.FullName, "wire-format-fixtures")

            if Directory.Exists(Path.Combine(candidate, "nodes")) then
                Some candidate
            else
                climb (Option.ofObj d.Parent)

    climb (Some(DirectoryInfo(System.AppContext.BaseDirectory)))

/// Every `nodes/` fixture, decoded. A fixture that does not decode is a defect
/// in a different suite (`GeneratedLayerTests` owns the corpus's round trip),
/// so it is skipped here rather than double-reported.
let private decodedFixtures () : (string * Node<obj>) list =
    match corpusRoot () with
    | None -> []
    | Some root ->
        let dir = Path.Combine(root, "nodes")

        if not (Directory.Exists dir) then
            []
        else
            // `Option.ofObj` on the two `Path` reads rather than a `null`
            // pattern: under this repo's nullness settings they are
            // `string | null`, and a bare use is FS3261.
            let stem (path: string) =
                Path.GetFileNameWithoutExtension path
                |> Option.ofObj
                |> Option.defaultValue path

            Directory.GetFiles(dir, "*.json")
            |> Array.toList
            |> List.sortBy stem
            |> List.choose (fun path ->
                match decodeNodeObj ((File.ReadAllText path).Trim()) with
                | Ok node -> Some(stem path, node)
                | Error _ -> None)

/// Every `DataGrid` in a tree, with the node id the refusal line would name.
///
/// Grids carrying `staticRows` are excluded on purpose: `Render.fs` routes that
/// leg to `renderTable` before the adapter is consulted at all, so the adapter
/// never sees one and a parity claim about it would be about a path that does
/// not exist.
let private gridsIn (node: Node<obj>) : (string * GridSpec<obj>) list =
    DebugGlobal.walkNodes node
    |> List.choose (fun n ->
        match n.Kind with
        | NodeKind.DataGrid spec when spec.StaticRows.IsNone -> Some(n.Id, spec)
        | _ -> None)

/// Every bound grid in the corpus, labelled `fixture/nodeId`.
let private corpusGrids: (string * GridSpec<obj>) list =
    decodedFixtures ()
    |> List.collect (fun (fixture, node) -> gridsIn node |> List.map (fun (id, spec) -> $"{fixture}/{id}", spec))

let private sources = BindingResolver.empty

/// A commit destination as something comparable. `Binding<Row seq>` carries
/// accessor closures, so it supports no equality constraint at all — and the
/// claim being made is about WHICH SLOT is written, which is exactly what this
/// keeps and what the closures are not.
let private destinationOf (binding: Binding<Row seq> option) : string option =
    binding
    |> Option.map (fun b ->
        match b with
        | Binding.State(key, _) -> "state:" + key
        | Binding.Filter(name, _) -> "filter:" + name
        | Binding.Query(name, _, _) -> "query:" + name
        | other -> sprintf "%A" (other.GetType().Name))

/// The parity rule, as one predicate so the assertion and its go-red probe
/// cannot drift apart: the AG backend agrees, or it declines with a reason
/// somebody could act on.
let private parityHolds (firstParty: Disposition) (ag: Disposition) : bool =
    match firstParty, ag with
    | a, b when a = b -> true
    | Disposition.Honoured, Disposition.Inert reason -> reason.Trim() <> ""
    | _ -> false

/// Is a refusal reason one a reader could act on?
///
/// Deliberately a crude shape test rather than a vocabulary check: what makes a
/// refusal useful is that it EXPLAINS — what could not be reached, and what the
/// other backend does instead — and no keyword list can tell an explanation
/// from a label that happens to contain the keyword. A length-and-word-count
/// floor cannot be met by "not supported", which is the shape this exists to
/// keep out, and every reason the plan actually carries clears it comfortably.
let private reasonIsActionable (reason: string) : bool =
    let words = reason.Split([| ' ' |], System.StringSplitOptions.RemoveEmptyEntries)

    reason.Trim().Length >= 60 && words.Length >= 12

[<Tests>]
let tests =
    testList
        "AgGridParity"
        [
          // ── The walk itself, before anything is concluded from it ────────
          test "the corpus yields bound grids to compare" {
              Expect.isSome (corpusRoot ()) "wire-format-fixtures/ not found — the corpus clone is missing"

              Expect.isGreaterThan
                  (List.length corpusGrids)
                  5
                  "the fixture walk collapsed — every parity assertion below would pass over an empty list"
          }

          test "the corpus exercises every declarative grid behaviour" {
              // Without this, a behaviour no fixture declares is one the parity
              // assertion below quantifies over vacuously — and the six fields
              // this phase is about were inert precisely because nothing
              // exercised them.
              let unexercised =
                  AgGridPlan.all
                  |> List.filter (fun behaviour ->
                      corpusGrids
                      |> List.forall (fun (_, spec) ->
                          firstPartyDisposition sources spec behaviour <> Disposition.Honoured))
                  |> List.map AgGridPlan.name

              Expect.isEmpty
                  unexercised
                  "no corpus fixture declares these behaviours in a form the first-party grid acts on, so this suite \
                   proves nothing about them — add a fixture rather than dropping the behaviour"
          }

          // ── The rule ─────────────────────────────────────────────────────
          test "every declared field is honoured on both backends, or refused by name on the adapter" {
              let offenders =
                  [ for label, spec in corpusGrids do
                        for behaviour in AgGridPlan.all do
                            let firstParty = firstPartyDisposition sources spec behaviour
                            let ag = agDisposition sources spec behaviour

                            if not (parityHolds firstParty ag) then
                                yield $"{label}: {AgGridPlan.name behaviour} — first-party %A{firstParty}, AG %A{ag}" ]

              Expect.isEmpty
                  offenders
                  "a grid field renders on one backend and is dropped on the other, or is declined with no reason — \
                   the choice of backend must be invisible to the emitted tree"
          }

          test "a field the adapter declines is declined with a reason a reader can act on" {
              // Not a restatement of the rule above: that one admits any
              // non-blank reason, and a reason that names neither the field nor
              // a remedy is a silent drop with punctuation.
              let thin =
                  [ for label, spec in corpusGrids do
                        for behaviour, reason in agRefusals sources spec do
                            if not (reasonIsActionable reason) then
                                yield $"{label}: {AgGridPlan.name behaviour} — {reason}" ]

              Expect.isEmpty thin "a refusal reason must say what could not be reached and why"

              // The predicate itself, shown refusing the shape it exists to
              // keep out — otherwise the emptiness above could be emptiness of
              // the check rather than of the findings.
              Expect.isFalse (reasonIsActionable "not supported") "a label is not a reason"
              Expect.isFalse (reasonIsActionable "") "an absent reason is the silent drop"
              Expect.isTrue (reasonIsActionable AgGridPlan.agNoTransfer) "the shipped reasons must clear their own bar"
          }

          test "the refusal line names the field and the node" {
              let line = refusalLine "sprint-todo" (Behaviour.TransferInKey, agNoTransfer)

              Expect.stringContains line "transferInKey" "the wire field name is what a reader would grep for"
              Expect.stringContains line "sprint-todo" "a page with four grids on it needs the node id"
          }

          // ── The three detectors, shown firing ────────────────────────────
          test "the parity predicate refuses a SILENT drop" {
              // The failure this phase found, stated as the thing the rule must
              // reject: the first-party leg acts, the adapter does not, and the
              // adapter says nothing.
              Expect.isFalse
                  (parityHolds Disposition.Honoured Disposition.NotDeclared)
                  "a backend that drops a declared field without a word must fail the rule"

              Expect.isFalse
                  (parityHolds Disposition.Honoured (Disposition.Inert "   "))
                  "an empty reason is a silent drop wearing a type"

              Expect.isTrue
                  (parityHolds Disposition.Honoured (Disposition.Inert AgGridPlan.agNoTransfer))
                  "a NAMED refusal is the sanctioned answer, not a failure"
          }

          test "the adapter really does decline cross-grid transfer, and says so" {
              // `transfer-board` is the corpus's three-grid board. If the
              // detector below ever returns an empty list, every transfer
              // assertion in this file has become vacuous.
              let transferGrids =
                  corpusGrids
                  |> List.filter (fun (_, spec) -> spec.TransferInKey.IsSome || spec.TransferOutKey.IsSome)

              Expect.isNonEmpty transferGrids "no corpus grid declares a transfer key — the refusal is untested"

              for label, spec in transferGrids do
                  let declared =
                      [ Behaviour.TransferInKey; Behaviour.TransferOutKey ]
                      |> List.filter (fun b -> firstPartyDisposition sources spec b = Disposition.Honoured)

                  for behaviour in declared do
                      match agDisposition sources spec behaviour with
                      | Disposition.Inert reason ->
                          Expect.stringContains
                              reason
                              "drop-zone registry"
                              $"{label}: the refusal must name what is missing"
                      | other ->
                          failtestf "%s: %s was not refused by the adapter (%A)" label (AgGridPlan.name behaviour) other
          }

          test "per-column sortable follows the DECLARATION, not `true`" {
              // The concrete defect, pinned on the prop rather than on the
              // disposition: `sortable` was `box true` on every column this
              // adapter emitted, so a column declaring `sortable: false` and
              // every column of a grid naming no `sortStateKey` carried an
              // affordance the wire had refused.
              let opted =
                  corpusGrids
                  |> List.tryFind (fun (_, spec) ->
                      spec.SortStateKey.IsSome
                      && spec.Columns |> List.exists (fun c -> c.Sortable = Some false))

              match opted with
              | None -> failtest "no corpus grid opts a column out of sorting — the narrowing is untested"
              | Some(label, spec) ->
                  let flags = columnSortable spec

                  Expect.equal
                      (List.length flags)
                      (List.length spec.Columns)
                      $"{label}: one flag per column or the zip with columnDefs is wrong"

                  Expect.isTrue (flags |> List.contains true) $"{label}: the sortable columns must stay sortable"
                  Expect.isTrue (flags |> List.contains false) $"{label}: the opted-out column must NOT be sortable"

                  for index, col in List.indexed spec.Columns do
                      if col.Sortable = Some false then
                          Expect.isFalse (List.item index flags) $"{label}: column {index} declares sortable:false"
                      elif col.Field.IsNone then
                          Expect.isFalse (List.item index flags) $"{label}: a field-less column has nothing to sort by"
          }

          test "a grid with no sortStateKey offers no sortable column at all" {
              // The other half of the same defect, and the commoner one: every
              // grid in the corpus that names no sort state key had every column
              // sortable on this backend.
              let unsorted =
                  corpusGrids |> List.filter (fun (_, spec) -> spec.SortStateKey.IsNone)

              Expect.isNonEmpty unsorted "no corpus grid omits sortStateKey — this assertion is vacuous"

              for label, spec in unsorted do
                  Expect.isFalse
                      (columnSortable spec |> List.contains true)
                      $"{label}: a grid naming no sort state key has nowhere to write a sort, so it offers none"
          }

          test "per-column editable narrows the grid-level declaration" {
              let narrowed =
                  corpusGrids
                  |> List.tryFind (fun (_, spec) ->
                      spec.Editable && spec.Columns |> List.exists (fun c -> c.Editable = Some false))

              match narrowed with
              | None -> failtest "no corpus grid opts a column out of editing — the narrowing is untested"
              | Some(label, spec) ->
                  let flags = columnEditable spec

                  Expect.isTrue (flags |> List.contains true) $"{label}: an editable grid must have editable columns"

                  for index, col in List.indexed spec.Columns do
                      if col.Editable = Some false then
                          Expect.isFalse (List.item index flags) $"{label}: column {index} declares editable:false"
          }

          test "a grid that is not editable has no editable column" {
              let readOnly = corpusGrids |> List.filter (fun (_, spec) -> not spec.Editable)

              Expect.isNonEmpty readOnly "no corpus grid is read-only — this assertion is vacuous"

              for label, spec in readOnly do
                  Expect.isFalse
                      (columnEditable spec |> List.contains true)
                      $"{label}: a grid that does not declare itself editable draws no input"
          }

          // ── The plan's concrete decisions ────────────────────────────────
          test "the pagination bar is on only where the spec declares it" {
              for label, spec in corpusGrids do
                  let plan = AgGridPlan.plan sources 40 spec

                  match plan.Pagination, paginates spec && not (hostPages spec) with
                  | Some(key, size, _), true ->
                      Expect.equal (Some key) spec.PageStateKey $"{label}: the pager writes the declared key"
                      Expect.equal (Some size) spec.PageSize $"{label}: the page size is the declared one"
                  | None, false -> ()
                  | actual, expected -> failtestf "%s: pagination %A but the declaration says %b" label actual expected
          }

          test "a host-paged grid is refused by name rather than mis-paged" {
              // The one pagination case AG's client-side row model cannot state
              // honestly: it derives its pager from the rows it holds, so over a
              // host's page it would report a one-page total. Built here rather
              // than found, because the corpus carries no host-paged grid — and
              // said so, rather than being left unexercised.
              let hostPaged =
                  corpusGrids
                  |> List.tryPick (fun (_, spec) ->
                      match spec.PageStateKey, spec.PageSize with
                      | Some key, Some _ ->
                          Some
                              { spec with
                                  Source = Binding.Query("ledger", (fun raw -> unbox raw), Some [ key ]) }
                      | _ -> None)

              match hostPaged with
              | None -> failtest "no corpus grid declares pagination — the host-paged refusal cannot be built"
              | Some spec ->
                  Expect.isTrue (hostPages spec) "the constructed grid must actually host-page, or this proves nothing"

                  Expect.equal
                      (firstPartyDisposition sources spec Behaviour.PageStateKey)
                      Disposition.Honoured
                      "the first-party grid pages a host-paged source with a count-less pager"

                  match agDisposition sources spec Behaviour.PageStateKey with
                  | Disposition.Inert reason ->
                      Expect.stringContains reason "host-paged" "the refusal must name the shape it declined"
                  | other -> failtestf "the adapter did not refuse a host-paged grid (%A)" other

                  Expect.isNone
                      (AgGridPlan.plan sources 40 spec).Pagination
                      "a refused pagination must also leave AG's pagination bar off"
          }

          test "the sort and page descriptors are the shapes the wire fixes" {
              // The round trip is only a round trip if the adapter writes what
              // the resolver reads. Both shapes are Phase 818/862's, and both
              // are written by the first-party leg as literals — so this is
              // where the two spellings are held together.
              Expect.equal
                  (sortDescriptorJVal (Some(1, SortDirection.Desc)))
                  (JObj [ "column", JInt 1; "direction", JStr "desc" ])
                  "the sort descriptor is {column, direction}"

              Expect.equal
                  (sortDescriptorJVal None)
                  (JObj [])
                  "the cleared sort is an EMPTY descriptor, never an absent key — an absent key reads as \
                   'not yet sorted' and would re-apply defaultSort"

              Expect.equal (pageDescriptorJVal 3) (JObj [ "page", JInt 3 ]) "the page descriptor is {page}"
              Expect.equal (pageDescriptorJVal 0) (JObj [ "page", JInt 1 ]) "pages are 1-based; 0 is not a page"

              // The reader's own predicate, run over what the writer produces.
              let written =
                  { BindingResolver.empty with
                      State = Map.ofList [ "k", (JObj [ "page", JInt 4 ] :> obj) ] }

              Expect.equal (BindingResolver.readPageDescriptor written "k") 4 "the resolver reads back what is written"
          }

          test "the effective sort is the resolver's, not a second rule" {
              let spec =
                  corpusGrids
                  |> List.tryPick (fun (_, spec) -> if spec.DefaultSort.IsSome then Some spec else None)

              match spec with
              | None -> failtest "no corpus grid declares defaultSort — the seeding is untested"
              | Some spec ->
                  Expect.equal
                      (AgGridPlan.plan sources 10 spec).Sort
                      (BindingResolver.effectiveSortDescriptor spec.SortStateKey spec.DefaultSort sources)
                      "the plan must not compute a sort of its own"

                  Expect.isSome (AgGridPlan.plan sources 10 spec).Sort "a declared defaultSort seeds the sort model"
          }

          test "the commit destinations are the shipped ones" {
              for label, spec in corpusGrids do
                  let plan = AgGridPlan.plan sources 10 spec

                  Expect.equal
                      (destinationOf plan.EditCommit)
                      (destinationOf (BindingResolver.editDestination spec.Editable spec.EditStateKey spec.Source))
                      $"{label}: the adapter must commit where Phase 863 says, not where it likes"

                  Expect.equal
                      (destinationOf plan.ReorderCommit)
                      (destinationOf (BindingResolver.reorderDestination spec.Reorderable spec.EditStateKey spec.Source))
                      $"{label}: the reorder handle is drawn exactly where a reorder has somewhere to go"
          }

          test "a grid declaring nothing this backend declines reports nothing" {
              // The quiet-console property. A refusal channel that spoke on
              // every grid would be scrolled past within a day.
              let plain =
                  corpusGrids
                  |> List.filter (fun (_, spec) ->
                      spec.TransferInKey.IsNone && spec.TransferOutKey.IsNone && not (hostPages spec))

              Expect.isNonEmpty plain "every corpus grid declares something refused — the quiet path is untested"

              for label, spec in plain do
                  let refusals =
                      agRefusals sources spec
                      |> List.filter (fun (b, _) ->
                          // An incomplete pagination declaration is declined by
                          // BOTH backends and is the tree's defect, not this
                          // backend's; it is reported on either path.
                          b <> Behaviour.PageSize
                          && b <> Behaviour.PageStateKey
                          && b <> Behaviour.EditStateKey
                          && b <> Behaviour.Reorderable)

                  Expect.isEmpty refusals $"{label}: this grid should have produced no adapter-specific refusal"
          } ]
