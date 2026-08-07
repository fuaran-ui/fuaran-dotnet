module Fuaran.UI.ServerDriven.Tests.DriverTests

// ─── Phase 152 Track C: the per-connection driver ──────────────
//
// End-to-end through the server-side Elmish loop: an inbound LiveEvent is
// validated (G1), its action interpreted, update run, the tree re-rendered,
// diffed and lowered to DomPatches. A counter app (model = int) is the
// fixture; a stub renderer stands in for the host's Render.render so the
// tests assert the op→patch mapping without a real HTML renderer.

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
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

/// A counter: a button that increments, a markdown showing the count (the node
/// that changes on each step), and a button that navigates (client-only arm).
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
                      "nav"
                      { Defaults.button<Msg> with
                          OnClick = Action.Navigate "/next" } ] }

let private stubRender (n: Node<Msg>) : string =
    let s = n.Id
    $"<f id='{s}'/>"

let private ev nodeId event =
    { ConnId = "c1"
      NodeId = nodeId
      Event = event
      Payload = Map.empty
      LastSeq = 0 }

let private session () =
    init (DriverServices.createPermissive stubRender) update view 0

[<Tests>]
let tests =
    testList
        "Driver (per-connection server loop)"
        [ test "a button click runs update and patches the changed node" {
              let s2, out = step (session ()) (ev "inc" "click")

              Expect.equal s2.Model 1 "model incremented"
              Expect.isNone out.Rejected "not rejected"
              // Only the count markdown changed → one targeted ReplaceFragment.
              Expect.equal
                  out.Patches
                  [ DomPatch.ReplaceFragment("count", "<f id='count'/>") ]
                  "one targeted patch for the changed leaf"

              Expect.isEmpty out.Effects "no client effects"
          }

          test "stepping twice accumulates model state across the connection" {
              let s1, _ = step (session ()) (ev "inc" "click")
              let s2, _ = step s1 (ev "inc" "click")
              Expect.equal s2.Model 2 "state persists in the session"
          }

          test "a client-only arm lowers to a ClientEffect with no model change" {
              let s2, out = step (session ()) (ev "nav" "click")

              Expect.equal s2.Model 0 "navigate does not touch the model"
              Expect.equal out.Effects [ ClientEffect.Navigate "/next" ] "navigate lowered to a ClientEffect"
              Expect.isEmpty out.Patches "no tree change → no patches"
          }

          // ─── Phase 782 — the gate default inverted on this path too ────────
          //
          // `DriverServices.create` returned `CanDispatch = fun _ -> true` until
          // Phase 782, so an unconfigured server-driven host validated every
          // inbound LiveEvent against a policy that permitted everything. Every
          // OTHER test in this file now names `createPermissive` deliberately;
          // this one pins what plain `create` does.
          test "DriverServices.create DENIES by default; createPermissive is the named opt-in" {
              let defaultServices: DriverServices<Msg> = DriverServices.create stubRender
              Expect.isFalse (defaultServices.CanDispatch(Action.Dispatch Inc)) "the default gate refuses"

              let permissiveServices: DriverServices<Msg> =
                  DriverServices.createPermissive stubRender

              Expect.isTrue (permissiveServices.CanDispatch(Action.Dispatch Inc)) "the named opt-in allows"

              // …and the refusal is a real refusal end-to-end, not just a flag.
              let s0 = init defaultServices update view 0
              let s2, out = step s0 (ev "inc" "click")
              Expect.equal s2.Model 0 "an unconfigured host mutates nothing"

              match out.Rejected with
              | Some(RejectReason.DispatchDenied("inc", _)) -> ()
              | other -> failtestf "expected DispatchDenied from the default gate, got %A" other
          }

          test "a denied action leaves the session unchanged (default-deny)" {
              let denyServices =
                  { DriverServices.createPermissive stubRender with
                      CanDispatch = fun _ -> false }

              let s0 = init denyServices update view 0
              let s2, out = step s0 (ev "inc" "click")

              Expect.equal s2.Model 0 "no state mutation on reject"
              Expect.isEmpty out.Patches "no patches on reject"

              match out.Rejected with
              | Some(RejectReason.DispatchDenied("inc", _)) -> ()
              | other -> failtestf "expected DispatchDenied, got %A" other
          }

          test "an unknown node is rejected with no state change" {
              let s2, out = step (session ()) (ev "ghost" "click")
              Expect.equal s2.Model 0 "unchanged"

              match out.Rejected with
              | Some(RejectReason.UnknownNode "ghost") -> ()
              | other -> failtestf "expected UnknownNode, got %A" other
          }

          test "OnApply receives the ops applied this step (FGP 5)" {
              let captured = ResizeArray<TreeOp<Msg>>()

              let services =
                  { DriverServices.createPermissive stubRender with
                      OnApply = fun ops -> captured.AddRange ops }

              let s0 = init services update view 0
              let _, _ = step s0 (ev "inc" "click")

              Expect.isNonEmpty (List.ofSeq captured) "ops were emitted to the sink hook"
          } ]
