module Fuaran.UI.ServerDriven.Tests.ActionInvocationDriverTests

// ─── Phase 889: the server-driven user-action emission point ────────────────
//
// `Driver.step` is the emission point on this path, and the choice of site is
// the substance of these tests rather than an implementation detail:
//
//   * `interpret` is reached only for PERMITTED actions, so a denial recorded
//     there would not exist. `step` sees both branches.
//   * both interpreters recurse through `Action.Chain`, so a record minted
//     inside the recursion gives N records for one gesture. `step` is outside
//     the recursion, so a chain is one invocation.
//   * `FormBuffer.step` runs its OWN G1 call for a form submit and never
//     reaches `Driver.step` on that branch, so it needs the same treatment —
//     covered at the foot of this file.

open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.ActionInvocation
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation
open Fuaran.UI.ServerDriven.Driver

type Model = int

type Msg =
    | Inc
    | Noop

let private update (msg: Msg) (m: Model) : Model =
    match msg with
    | Inc -> m + 1
    | Noop -> m

let private view (m: Model) : Node<Msg> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<Msg> with
            Children =
                [ Fuaran.button
                      "inc"
                      { Defaults.button<Msg> with
                          OnClick = Action.Dispatch Inc }
                  Fuaran.markdown "count" (string m)
                  Fuaran.button
                      "chain"
                      { Defaults.button<Msg> with
                          OnClick = Action.Chain [ Action.Dispatch Inc; Action.Dispatch Inc ] }
                  Fuaran.button
                      "nav"
                      { Defaults.button<Msg> with
                          OnClick = Action.Navigate "/next?email=user@example.com" } ] }

let private stubRender (n: Node<Msg>) : string = $"<f id='{n.Id}'/>"

let private ev nodeId event : LiveEvent =
    { ConnId = "c1"
      NodeId = nodeId
      Event = event
      Payload = Map.empty
      LastSeq = 0 }

/// A session whose services record through `collector`, correlating with
/// whatever `context ()` currently returns.
let private sessionWith
    (canDispatch: Action<Msg> -> bool)
    (collector: ActionInvocationSink.Collector)
    (context: unit -> Map<string, string>)
    =
    let services: DriverServices<Msg> =
        { DriverServices.createPermissive stubRender with
            CanDispatch = canDispatch
            ActionRecording =
                Some
                    { Sink = collector :> IActionInvocationSink
                      CorrelationContext = context } }

    init services update view 0

let private permissive (collector: ActionInvocationSink.Collector) =
    sessionWith (fun _ -> true) collector (fun () -> Map.ofList [ ActionInvocation.interactionIdKey, "turn-1" ])

[<Tests>]
let tests =
    testList
        "Phase 889 — server-driven user-action records"
        [ test "recording is OFF by default — DriverServices.create wires no sink" {
              let services: DriverServices<Msg> = DriverServices.create stubRender
              Expect.isNone services.ActionRecording "an unconfigured host keeps no user-action log"
          }

          test "a dispatched action produces EXACTLY ONE record with the expected fields" {
              let collector = ActionInvocationSink.Collector()
              let s2, out = step (permissive collector) (ev "inc" "click")

              Expect.equal s2.Model 1 "the action actually ran"
              Expect.isNone out.Rejected "not rejected"

              match collector.Recorded with
              | [ r ] ->
                  Expect.equal r.Action "Dispatch" "the constructor, redacted"
                  Expect.equal r.NodeId (Some "inc") "the node the gesture was attached to"
                  Expect.equal r.Event (Some "click") "the DOM event name — the other half of the affordance"
                  Expect.equal r.Outcome ActionOutcome.Dispatched "dispatched"
                  Expect.equal r.Path DispatchPath.ServerDriven "the path is recorded, not inferred"

                  Expect.equal
                      r.Provenance
                      AffordanceProvenance.TreeDeclared
                      "the server-driven path synthesises no affordances"

                  Expect.equal r.InteractionId (Some "turn-1") "the Phase 330 id, reused not minted"
                  Expect.isNone r.Payload "redacted by default"
              | other -> failtestf "expected exactly one record, got %i: %A" (List.length other) other
          }

          test "a CHAIN is ONE record, not one per constituent" {
              // The trap the phase names: both interpreters recurse through
              // `Action.Chain`, so emitting inside the recursion would give two
              // records here with no way to tell a chain from two clicks.
              let collector = ActionInvocationSink.Collector()
              let s2, _ = step (permissive collector) (ev "chain" "click")

              Expect.equal s2.Model 2 "the chain genuinely ran both arms"
              Expect.equal (List.length collector.Recorded) 1 "…and is ONE gesture"
              Expect.equal collector.Recorded.Head.Action "Chain" "named as a chain, constituents not enumerated"
          }

          test "a DENIAL is recorded, not dropped" {
              let collector = ActionInvocationSink.Collector()

              let denying = sessionWith (fun _ -> false) collector (fun () -> Map.empty)

              let s2, out = step denying (ev "inc" "click")

              Expect.equal s2.Model 0 "default-deny still holds"

              match out.Rejected with
              | Some(RejectReason.DispatchDenied("inc", _)) -> ()
              | other -> failtestf "expected DispatchDenied, got %A" other

              match collector.Recorded with
              | [ r ] ->
                  Expect.equal r.Action "Dispatch" "the gate's own log-safe description"
                  Expect.equal r.NodeId (Some "inc") "the node"

                  match r.Outcome with
                  | ActionOutcome.Denied reason -> Expect.stringContains reason "Dispatch" "the reason names the action"
                  | other -> failtestf "expected Denied, got %A" other

                  Expect.isNone r.InteractionId "an empty correlation context yields no id, not a fabricated one"
              | other -> failtestf "expected exactly one denial record, got %A" other
          }

          test "a reject with NO resolved action records nothing — there is nothing to name" {
              // A forged node id never resolved to an action, so an
              // `ActionInvocation` for it would have no action to describe.
              // Those stay on the always-on Phase 212 `OnReject` audit sink.
              let collector = ActionInvocationSink.Collector()
              let _, out = step (permissive collector) (ev "ghost" "click")

              match out.Rejected with
              | Some(RejectReason.UnknownNode "ghost") -> ()
              | other -> failtestf "expected UnknownNode, got %A" other

              Expect.isEmpty collector.Recorded "no action, no action record"
          }

          test "the correlation context is read PER STEP, never captured" {
              // The defect this prevents: `DriverServices` is built once per
              // connection while the interaction id changes every turn, so a
              // captured map stamps the first turn's id onto every later one —
              // a correlation worse than none, because it looks right.
              let collector = ActionInvocationSink.Collector()
              let mutable turn = 1

              let s =
                  sessionWith (fun _ -> true) collector (fun () ->
                      Map.ofList [ ActionInvocation.interactionIdKey, sprintf "turn-%i" turn ])

              let s1, _ = step s (ev "inc" "click")
              turn <- 2
              let _ = step s1 (ev "inc" "click")

              Expect.equal
                  (collector.Recorded |> List.map _.InteractionId)
                  [ Some "turn-1"; Some "turn-2" ]
                  "each step read the host's CURRENT id"
          }

          test "the recorded Navigate carries its path, not its query string" {
              let collector = ActionInvocationSink.Collector()
              let _ = step (permissive collector) (ev "nav" "click")

              Expect.equal
                  collector.Recorded.Head.Action
                  "Navigate(/next)"
                  "a route's query string is user data and this log is durable"
          }

          test "an unwired host pays nothing and records nothing" {
              let services: DriverServices<Msg> =
                  { DriverServices.createPermissive stubRender with
                      ActionRecording = None }

              let s2, out = step (init services update view 0) (ev "inc" "click")
              Expect.equal s2.Model 1 "behaviour is unchanged with recording off"
              Expect.isNone out.Rejected "unchanged"
          } ]

// ─── The form-submit leg ────────────────────────────────────────────────────

type private FormMsg =
    | Submitted
    | Committed of string

let private formUpdate (_: FormMsg) (m: int) : int = m + 1

let private formView (m: int) : Node<FormMsg> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<FormMsg> with
            Children =
                [ Fuaran.form
                      "f1"
                      { Defaults.form<FormMsg> with
                          Fields = []
                          OnSubmit = Action.Dispatch Submitted }
                  Fuaran.markdown "count" (string m) ] }

let private formRender (n: Node<FormMsg>) : string = $"<f id='{n.Id}'/>"

let private formEv: LiveEvent =
    { ConnId = "c1"
      NodeId = "f1"
      Event = "submit"
      Payload = Map.empty
      LastSeq = 0 }

let private formSession (canDispatch: Action<FormMsg> -> bool) (collector: ActionInvocationSink.Collector) =
    let services: DriverServices<FormMsg> =
        { DriverServices.createPermissive formRender with
            CanDispatch = canDispatch
            ActionRecording =
                Some
                    { Sink = collector :> IActionInvocationSink
                      CorrelationContext = fun () -> Map.empty } }

    init services formUpdate formView 0

[<Tests>]
let formTests =
    testList
        "Phase 889 — the form-submit leg has its own G1 call, so its own emission"
        [ test "a submitted form records its OnSubmit action" {
              let collector = ActionInvocationSink.Collector()
              let s2, out = FormBuffer.step None (formSession (fun _ -> true) collector) formEv

              Expect.isNone out.Rejected "accepted"
              Expect.equal s2.Model 1 "the submit ran"

              match collector.Recorded with
              | [ r ] ->
                  Expect.equal r.Action "Dispatch" "the OnSubmit action"
                  Expect.equal r.NodeId (Some "f1") "the form node"
                  Expect.equal r.Event (Some "submit") "the submit event"
                  Expect.equal r.Outcome ActionOutcome.Dispatched "dispatched"
              | other -> failtestf "expected one record, got %A" other
          }

          test "a form submit DENIED at G1 is recorded — this leg never reaches Driver.step" {
              let collector = ActionInvocationSink.Collector()
              let s2, out = FormBuffer.step None (formSession (fun _ -> false) collector) formEv

              Expect.equal s2.Model 0 "no mutation"

              match out.Rejected with
              | Some(RejectReason.DispatchDenied("f1", _)) -> ()
              | other -> failtestf "expected DispatchDenied, got %A" other

              match collector.Recorded with
              | [ r ] ->
                  match r.Outcome with
                  | ActionOutcome.Denied _ -> ()
                  | other -> failtestf "expected Denied, got %A" other
              | other -> failtestf "expected one denial record, got %A" other
          } ]
