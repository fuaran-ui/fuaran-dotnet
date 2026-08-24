module Fuaran.UI.ServerDriven.Tests.ValidationTests

// ─── Phase 152 Track C: the G1 inbound trust boundary ──────────────
//
// The trust boundary is the non-negotiable default-deny gate on the
// inbound (nodeId, event, payload) path. These assert each of the four
// checks (node-exists / event-legitimate / payload-in-bounds /
// dispatch-gated) rejects what it must, and that a clean event resolves
// the right typed Action. Pure + headless — no transport, no renderer.

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation

/// Distinguishable messages so we can assert the resolved Action.
type Msg =
    | Clicked
    | Picked of string option
    | Submitted
    | Toggled of bool
    | TabChosen of int
    | StepChosen of int
    | RegionFiltered of string option
    | NameFiltered of string

let private allow: Action<Msg> -> bool = fun _ -> true
let private deny: Action<Msg> -> bool = fun _ -> false

/// A tree: a dashboard holding a button, a static-option select, a form,
/// a disclosure and a tabs node — one of each interactive kind, plus a
/// non-interactive markdown leaf.
let private opt v : SelectOption = { Value = v; Label = v }

let private tree: Node<Msg> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<Msg> with
            Children =
                [ Fuaran.button
                      "btn"
                      { Defaults.button<Msg> with
                          OnClick = Action.Dispatch Clicked }
                  Fuaran.select
                      "sel"
                      { Defaults.select<Msg> with
                          Source = Binding.Static(Some [ opt "a"; opt "b" ])
                          OnChange = Some(fun v -> Action.Dispatch(Picked v)) }
                  Fuaran.form
                      "frm"
                      { Defaults.form<Msg> with
                          OnSubmit = Action.Dispatch Submitted }
                  Fuaran.disclosure
                      "disc"
                      { Defaults.disclosure<Msg> with
                          OnToggle = Some(fun b -> Action.Dispatch(Toggled b)) }
                  Fuaran.tabs
                      "tab"
                      { Defaults.tabs<Msg> with
                          OnSelect = Some(fun i -> Action.Dispatch(TabChosen i)) }
                  Fuaran.stepper
                      "stp"
                      { Defaults.stepper<Msg> with
                          OnSelect = Some(fun i -> Action.Dispatch(StepChosen i)) }
                  Fuaran.filters
                      "flt"
                      [ { Name = "region"
                          Label = TextSource.Literal "Region"
                          Kind =
                            FormFieldKind.SegmentedChoice(
                                Binding.Static(Some [ opt "north"; opt "south" ]),
                                Some(Binding.Static None),
                                Some(fun v -> Action.Dispatch(RegionFiltered v)),
                                Orientation.Horizontal
                            ) }
                        { Name = "name"
                          Label = TextSource.Literal "Name"
                          Kind =
                            FormFieldKind.Text(
                                Some(Binding.Static(Some "")),
                                Some(fun v -> Action.Dispatch(NameFiltered v))
                            ) } ]
                  Fuaran.markdown "md" "just text" ] }

let private ev nodeId event payload =
    { ConnId = "c1"
      NodeId = nodeId
      Event = event
      Payload = Map.ofList payload
      LastSeq = 0 }

/// Assert the resolved action dispatches the expected Msg.
let private expectDispatch (r: Result<ValidatedEvent<Msg>, RejectReason>) (expected: Msg) (label: string) =
    match r with
    | Ok { Action = Some(Action.Dispatch m) } -> Expect.equal m expected label
    | other -> failtestf "%s — expected Ok(Dispatch %A), got %A" label expected other

[<Tests>]
let tests =
    testList
        "Validation (G1 trust boundary)"
        [ // ── (a) node existence ──
          test "unknown node id is rejected" {
              match validate allow tree (ev "ghost" "click" []) with
              | Error(RejectReason.UnknownNode "ghost") -> ()
              | other -> failtestf "expected UnknownNode, got %A" other
          }

          // ── (b) event legitimacy ──
          test "an illegitimate event for the kind is rejected" {
              // A markdown leaf accepts no events.
              match validate allow tree (ev "md" "click" []) with
              | Error(RejectReason.IllegitimateEvent("md", "click", _)) -> ()
              | other -> failtestf "expected IllegitimateEvent, got %A" other

              // A button accepts click, not change.
              match validate allow tree (ev "btn" "change" []) with
              | Error(RejectReason.IllegitimateEvent("btn", "change", _)) -> ()
              | other -> failtestf "expected IllegitimateEvent for btn change, got %A" other
          }

          // ── (c) payload bounds ──
          test "a select value outside its static options is rejected" {
              match validate allow tree (ev "sel" "change" [ "value", LiveValue.Str "zzz" ]) with
              | Error(RejectReason.PayloadOutOfBounds("sel", _)) -> ()
              | other -> failtestf "expected PayloadOutOfBounds, got %A" other
          }

          test "a select value within its static options is accepted" {
              expectDispatch
                  (validate allow tree (ev "sel" "change" [ "value", LiveValue.Str "a" ]))
                  (Picked(Some "a"))
                  "select 'a' resolves OnChange (Some a)"
          }

          test "a select cleared to none (null value) is accepted" {
              expectDispatch
                  (validate allow tree (ev "sel" "change" [ "value", LiveValue.Null ]))
                  (Picked None)
                  "null value resolves OnChange None"
          }

          // ── (d) dispatch policy gate ──
          test "a denied action is rejected even when structurally valid" {
              match validate deny tree (ev "btn" "click" []) with
              | Error(RejectReason.DispatchDenied("btn", _)) -> ()
              | other -> failtestf "expected DispatchDenied, got %A" other
          }

          // ── happy paths per interactive kind ──
          test "button click resolves OnClick" {
              expectDispatch (validate allow tree (ev "btn" "click" [])) Clicked "button click"
          }

          test "form submit resolves OnSubmit" {
              expectDispatch (validate allow tree (ev "frm" "submit" [])) Submitted "form submit"
          }

          test "disclosure click resolves OnToggle (defaults open=true)" {
              expectDispatch
                  (validate allow tree (ev "disc" "click" []))
                  (Toggled true)
                  "disclosure default-open toggle"

              expectDispatch
                  (validate allow tree (ev "disc" "change" [ "open", LiveValue.Bool false ]))
                  (Toggled false)
                  "disclosure explicit open=false"
          }

          test "tabs click with an index resolves OnSelect" {
              expectDispatch
                  (validate allow tree (ev "tab" "click" [ "index", LiveValue.Num 2.0 ]))
                  (TabChosen 2)
                  "tabs index 2"
          }

          test "stepper click with an index resolves OnSelect" {
              expectDispatch
                  (validate allow tree (ev "stp" "click" [ "index", LiveValue.Num 1.0 ]))
                  (StepChosen 1)
                  "stepper index 1"
          }

          test "stepper click without an index no-ops (Action = None)" {
              match validate allow tree (ev "stp" "click" []) with
              | Ok { Action = None } -> ()
              | other -> failtestf "expected Ok(Action = None), got %A" other
          }

          // ── Filters: name-addressed resolution ──
          test "a segmented filter click resolves the named filter's OnChange" {
              expectDispatch
                  (validate
                      allow
                      tree
                      (ev "flt" "click" [ "name", LiveValue.Str "region"; "value", LiveValue.Str "north" ]))
                  (RegionFiltered(Some "north"))
                  "segmented filter pick"
          }

          test "a segmented filter change cleared to empty resolves None" {
              expectDispatch
                  (validate allow tree (ev "flt" "change" [ "name", LiveValue.Str "region"; "value", LiveValue.Str "" ]))
                  (RegionFiltered None)
                  "segmented filter clear"
          }

          test "a text filter input resolves the named filter's OnChange" {
              expectDispatch
                  (validate allow tree (ev "flt" "input" [ "name", LiveValue.Str "name"; "value", LiveValue.Str "ada" ]))
                  (NameFiltered "ada")
                  "text filter input"
          }

          test "a segmented filter value outside its static options is rejected" {
              match
                  validate allow tree (ev "flt" "change" [ "name", LiveValue.Str "region"; "value", LiveValue.Str "x" ])
              with
              | Error(RejectReason.PayloadOutOfBounds("flt", _)) -> ()
              | other -> failtestf "expected PayloadOutOfBounds, got %A" other
          }

          test "a filter name the node does not declare is rejected" {
              match
                  validate allow tree (ev "flt" "change" [ "name", LiveValue.Str "ghost"; "value", LiveValue.Str "a" ])
              with
              | Error(RejectReason.PayloadOutOfBounds("flt", _)) -> ()
              | other -> failtestf "expected PayloadOutOfBounds for forged filter name, got %A" other
          }

          test "a filters event without a name no-ops (Action = None)" {
              match validate allow tree (ev "flt" "change" [ "value", LiveValue.Str "north" ]) with
              | Ok { Action = None } -> ()
              | other -> failtestf "expected Ok(Action = None), got %A" other
          }

          // ── reject-reason descriptions are log-safe (no payload values) ──
          //
          // Phase 787. The previous test here HAND-CONSTRUCTED a reason carrying
          // `'zzz' not among the select's options` and asserted only that the
          // node id survived — so it passed while `describe` was echoing the very
          // value its doc comment promised to withhold. The poison now goes in
          // through `validate`, the way a user's input actually arrives, and the
          // assertion is about what comes OUT.
          //
          // The idiom is `docs/ACTION-LOG-PRIVACY.md`'s poison scan: a
          // distinctive string in every value position, asserted absent from the
          // describer — and the fixture proved non-vacuous, so a scan that
          // stopped feeding poison (or a describer that returned nothing) cannot
          // pass by saying nothing at all.
          test "PayloadOutOfBounds describes without leaking the submitted value" {
              let poison = "PZN-4f1c-payload-poison"

              let describeReject label r =
                  match r with
                  | Error(reason: RejectReason) -> RejectReason.describe reason
                  | other -> failtestf "%s — expected a reject, got %A" label other

              // (1) a select value outside its static options.
              let selDetail =
                  validate allow tree (ev "sel" "change" [ "value", LiveValue.Str poison ])
                  |> describeReject "select bounds"

              // (2) a filter NAME the node does not declare — client-supplied text,
              //     not an author-declared name, so it is withheld too.
              let ghostDetail =
                  validate allow tree (ev "flt" "change" [ "name", LiveValue.Str poison; "value", LiveValue.Str "a" ])
                  |> describeReject "forged filter name"

              // (3) a segmented filter value outside its declared options.
              let fltDetail =
                  validate
                      allow
                      tree
                      (ev "flt" "change" [ "name", LiveValue.Str "region"; "value", LiveValue.Str poison ])
                  |> describeReject "filter option bounds"

              for label, d in
                  [ "select bounds", selDetail
                    "forged filter name", ghostDetail
                    "filter option bounds", fltDetail ] do
                  Expect.isFalse (d.Contains poison) (label + " — the submitted value must not reach the log line")

              // Non-vacuity, both halves. The describers still SAY something
              // diagnosable — the node id on all three, and the author-declared
              // filter name on the one where it is grade B — so a describer that
              // returned "" would fail here rather than pass the poison check by
              // emitting nothing.
              for label, d in
                  [ "select bounds", selDetail
                    "forged filter name", ghostDetail
                    "filter option bounds", fltDetail ] do
                  Expect.isTrue (d.Length > 0) (label + " — describe must not be empty")

              Expect.stringContains selDetail "sel" "select reject names the node"
              Expect.stringContains ghostDetail "flt" "forged-name reject names the node"
              Expect.stringContains fltDetail "region" "the author-declared filter name is grade B and stays"

              // And the poison really was in the input — so this test cannot pass
              // by feeding an event that never carried it.
              let fed = ev "sel" "change" [ "value", LiveValue.Str poison ]

              Expect.equal
                  (Map.tryFind "value" fed.Payload)
                  (Some(LiveValue.Str poison))
                  "the fixture carries the poison"
          } ]
