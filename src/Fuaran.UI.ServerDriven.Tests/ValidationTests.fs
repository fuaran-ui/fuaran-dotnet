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
                          OnSelect = (fun i -> Action.Dispatch(StepChosen i)) }
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
          test "reject reasons describe without leaking payload" {
              let d =
                  RejectReason.describe (RejectReason.PayloadOutOfBounds("sel", "'zzz' not among the select's options"))

              Expect.stringContains d "sel" "names the node"
          } ]
