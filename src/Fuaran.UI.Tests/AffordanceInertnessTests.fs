module Fuaran.UI.Tests.AffordanceInertness

// ============================================================================
//  Phase 924 — the decode-side inertness report.
//
//  Two things are pinned here, and the first is the more important:
//
//  1. THE DERIVATION SEAM. The consequence table is keyed entirely by names
//     the two existing tables own — `WireSurvivability`'s `HostOnly` cases and
//     `SlotCapability`'s `HostOnlyByDesign` slots — and the completeness tests
//     assert BOTH directions, so a new host-only case or slot cannot ship
//     without its decoded consequence stated, and a stale row cannot linger
//     after its subject is renamed or reclassified. That is the whole defence
//     against the second list this phase exists to avoid.
//
//  2. THE REPORT. A decoded chart carrying the `onPointClick` sentinel is
//     inert; the same chart with the Phase 933 default is silent; a
//     fully-declarative tree reports nothing at all.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types

type private Msg = NoOp

let private node (id: string) (kind: NodeKind<Msg>) : Node<Msg> =
    { Id = id
      Kind = kind
      State = None
      Style = None
      Accessibility = None
      Motion = Defaults.Motion.none
      ExtraAttributes = None
      Tooltip = None }

let private stack (id: string) (children: Node<Msg> list) : Node<Msg> =
    node
        id
        (NodeKind.Box(
            { Layout = BoxLayout.Flex(Orientation.Vertical, false, None)
              Role = BoxRole.Group
              Heading = None
              Children = children
              KeepTogether = false
              BreakBefore = false }
        ))

/// A grid carrying one column, spelled the way a DECODED grid is.
let private gridWith (id: string) (col: ColumnErased<Msg>) : Node<Msg> =
    node
        id
        (NodeKind.DataGrid(
            { SortStateKey = None
              PageSize = None
              PageStateKey = None
              EditStateKey = None
              DefaultSort = None
              Source = Binding.Static(Some Seq.empty)
              RowKey = None
              RowKeyField = Some "id"
              Columns = [ col ]
              OnRowClick = None
              Editable = false
              Reorderable = false
              StaticRows = None
              KeepRowsTogether = false
              RepeatHeader = false }
        ))

let private column (kind: CellKindErased<Msg>) : ColumnErased<Msg> =
    { Label = "Col"
      Value = None
      Field = None
      Sortable = None
      Editable = None
      Format = CellFormat.None
      Kind = kind
      Width = ColumnWidth.Auto }

let private chartNode (id: string) (onPointClick: (Fuaran.Core.Row -> Action<Msg>) option) : Node<Msg> =
    node
        id
        (NodeKind.Chart(
            { Defaults.chart<Msg> with
                Source = Binding.Static(Some Seq.empty)
                XField = "x"
                YFields = [ "y" ]
                OnPointClick = onPointClick }
        ))

/// The shape a decoded closure takes — inert, and re-encoding to the sentinel.
let private deadHandler: 'a -> Action<Msg> = fun _ -> Action.Chain []

let private subjects (findings: AffordanceInertness.InertAffordance list) =
    findings |> List.map _.Subject |> Set.ofList

[<Tests>]
let tests =
    testList
        "Phase 924 — decoded-tree affordance inertness"
        [
          // ── the derivation seam ─────────────────────────────────────────
          test "every WireSurvivability HostOnly case carries a decoded consequence" {
              let missing =
                  WireSurvivability.all
                  |> List.filter (fun c -> c.Verdict = WireSurvivability.Survivability.HostOnly)
                  |> List.map _.Case
                  |> List.filter (fun case -> not (AffordanceInertness.bySubject.ContainsKey case))

              Expect.isEmpty
                  missing
                  (sprintf
                      "host-only case(s) with no decoded consequence — add an AffordanceInertness.family row: %A"
                      missing)
          }

          test "every SlotCapability HostOnlyByDesign slot carries a decoded consequence" {
              let missing =
                  SlotCapability.all
                  |> List.filter (fun r ->
                      match r.Posture with
                      | SlotCapability.SlotPosture.HostOnlyByDesign _ -> true
                      | _ -> false)
                  |> List.map _.Slot
                  |> List.filter (fun slot -> not (AffordanceInertness.bySubject.ContainsKey slot))

              Expect.isEmpty
                  missing
                  (sprintf
                      "host-only slot(s) with no decoded consequence — add an AffordanceInertness.family row: %A"
                      missing)
          }

          test "the consequence table names no phantom subject" {
              let known =
                  (WireSurvivability.all
                   |> List.filter (fun c -> c.Verdict = WireSurvivability.Survivability.HostOnly)
                   |> List.map _.Case)
                  @ (SlotCapability.all
                     |> List.filter (fun r ->
                         match r.Posture with
                         | SlotCapability.SlotPosture.HostOnlyByDesign _ -> true
                         | _ -> false)
                     |> List.map _.Slot)
                  |> Set.ofList

              let phantom =
                  AffordanceInertness.family
                  |> List.map _.Subject
                  |> List.filter (fun s -> not (known.Contains s))

              Expect.isEmpty
                  phantom
                  (sprintf
                      "consequence row(s) naming no host-only case or slot (stale after a rename or a reclassification?): %A"
                      phantom)

              let all = AffordanceInertness.family |> List.map _.Subject
              Expect.equal (List.length all) (List.length (List.distinct all)) "no duplicate consequence rows"
          }

          // ── the two motivating instances ────────────────────────────────
          test "a decoded chart carrying the onPointClick sentinel reports inert" {
              let findings = AffordanceInertness.report (chartNode "chart" (Some deadHandler))

              let chartFinding =
                  findings
                  |> List.tryFind (fun f -> f.Node = "chart" && f.Subject = "ChartSpec.onPointClick")

              Expect.isSome chartFinding "a decoded chart's point click is inert and must be reported"
              Expect.equal chartFinding.Value.Verdict AffordanceInertness.DecodedVerdict.Inert "verdict is Inert"
          }

          test "a chart with the Phase 933 default reports nothing" {
              let findings = AffordanceInertness.report (chartNode "chart" None)

              Expect.isEmpty
                  findings
                  "an omitted onPointClick IS the declarative form — the node publishes the clicked datum under its own id"
          }

          test "Action.Dispatch — no wire payload by construction — reports inert with the substrate alternative" {
              let button =
                  node
                      "btn"
                      (NodeKind.Button(
                          { Defaults.button<Msg> with
                              Label = TextSource.Literal "Go"
                              OnClick = Action.Chain [ Action.Dispatch NoOp ] }
                      ))

              let findings = AffordanceInertness.report button

              let dispatch = findings |> List.tryFind (fun f -> f.Subject = "Action.Dispatch")

              Expect.isSome dispatch "a decoded Dispatch carries a boxed sentinel and must be reported"
              Expect.equal dispatch.Value.Node "btn" "attributed to the node hosting the gesture"

              // The alternative is READ from the survivability table, never
              // restated here — so this asserts the derivation, not a string.
              Expect.equal
                  dispatch.Value.Alternative
                  (WireSurvivability.byCase
                   |> Map.tryFind "Action.Dispatch"
                   |> Option.bind _.Alternative)
                  "the alternative comes from WireSurvivability, not from a copy"

              Expect.isSome dispatch.Value.Alternative "and the table does name one"
          }

          // ── the whole-case grid-cell erasures ───────────────────────────
          test "the host-only grid cell kinds and the custom cell format report" {
              let tree =
                  stack
                      "root"
                      [ gridWith "g1" (column (CellKindErased.Link((fun _ -> "/x"), (fun _ -> TextSource.Literal "l"))))
                        gridWith "g2" (column (CellKindErased.Checkbox((fun _ -> false), Some deadHandler)))
                        gridWith
                            "g3"
                            (column (
                                CellKindErased.Custom(fun _ ->
                                    node "x" (NodeKind.Markdown { Text = TextSource.Literal "" }))
                            ))
                        gridWith
                            "g4"
                            { column CellKindErased.Text with
                                Format = CellFormat.Custom(fun _ -> "") } ]

              let found = subjects (AffordanceInertness.report tree)

              for expected in
                  [ "CellKindErased.Link"
                    "CellKindErased.Checkbox"
                    "CellKindErased.Custom"
                    "CellFormat.Custom" ] do
                  Expect.isTrue (found.Contains expected) (sprintf "%s must be reported on a decoded tree" expected)
          }

          test "Progress is Degraded with a column field and Inert without — the one context-dependent row" {
              let progress = CellKindErased.Progress((fun _ -> 0.0), None)

              let verdictOf (col: ColumnErased<Msg>) =
                  AffordanceInertness.report (gridWith "g" col)
                  |> List.tryFind (fun f -> f.Subject = "CellKindErased.Progress")
                  |> Option.map _.Verdict

              Expect.equal
                  (verdictOf
                      { column progress with
                          Field = Some "pct" })
                  (Some AffordanceInertness.DecodedVerdict.Degraded)
                  "a neighbouring declarative `field` recovers the fill"

              Expect.equal
                  (verdictOf (column progress))
                  (Some AffordanceInertness.DecodedVerdict.Inert)
                  "without one, every bar fills to zero"
          }

          test "Binding.Computed reports, attributed to the reading node" {
              let metric =
                  node
                      "m"
                      (NodeKind.Metric(
                          { Defaults.metric with
                              Label = TextSource.Literal "Total"
                              Value = Binding.Computed(fun _ -> 0.0) }
                      ))

              let computed =
                  AffordanceInertness.report metric
                  |> List.filter (fun f -> f.Subject = "Binding.Computed")

              Expect.equal (List.length computed) 1 "reported exactly once, attributed to the reader"
              Expect.equal computed.Head.Node "m" "attributed to the reading node"
          }

          // ── the negative cases ──────────────────────────────────────────
          test "a survivable, fully-declarative tree reports nothing" {
              let tree =
                  stack
                      "root"
                      [ chartNode "chart" None
                        gridWith
                            "grid"
                            { column CellKindErased.Text with
                                Field = Some "dept" }
                        node
                            "btn"
                            (NodeKind.Button(
                                { Defaults.button<Msg> with
                                    Label = TextSource.Literal "Save"
                                    OnClick = Action.SetState("saved", Some(Fuaran.Core.JBool true), None) }
                            ))
                        node
                            "frm"
                            (NodeKind.Form(
                                { Defaults.form with
                                    Fields =
                                        [ { Defaults.formField with
                                              Id = "name"
                                              Label = TextSource.Literal "Name"
                                              Kind = FormFieldKind.Text(Some(Binding.State("name", Some "")), None) } ]
                                    SubmitLabel = TextSource.Literal "Save" }
                            )) ]

              Expect.isEmpty (AffordanceInertness.report tree) "nothing in a wire-survivable tree is inert"
          }

          test "a Fine subject is classified but never reported" {
              // `Binding.Query`'s accessor decodes to an identity projection —
              // the host-fed value flows through, so nothing was lost. It is in
              // the sweep (silence must be a recorded verdict, not an omission)
              // and it must never produce a finding.
              let queried =
                  node
                      "m"
                      (NodeKind.Metric(
                          { Defaults.metric with
                              Label = TextSource.Literal "Total"
                              Value = Binding.Query("totals", (fun o -> unbox o), None) }
                      ))

              Expect.equal
                  (AffordanceInertness.bySubject.["Binding.Query.accessor"].Verdict)
                  AffordanceInertness.DecodedVerdict.Fine
                  "classified Fine in the sweep"

              Expect.isEmpty (AffordanceInertness.report queried) "and never reported"
          }

          test "every reported finding carries a decoded consequence in prose" {
              let findings =
                  AffordanceInertness.report (
                      stack
                          "root"
                          [ chartNode "chart" (Some deadHandler)
                            gridWith
                                "g"
                                (column (CellKindErased.Link((fun _ -> "/x"), (fun _ -> TextSource.Literal "l")))) ]
                  )

              Expect.isNonEmpty findings "the probe tree carries inert affordances"

              for f in findings do
                  Expect.isTrue (f.Consequence.Length > 0) "every finding says what a consumer actually gets"
                  Expect.notEqual f.Verdict AffordanceInertness.DecodedVerdict.Fine "a Fine subject is never emitted"
          } ]
