module Fuaran.UI.ServerDriven.Tests.NavigateBoundRoute

// ============================================================================
//  Phase 1536 — `Action.Navigate` over a `TextSource`, RESOLVED then GATED.
//
//  Why the server-driven tier and not the client renderer. `Render.runAction`
//  is `private` and the render path is Fable-only, so the client emission point
//  is not reachable from a .NET runner (the constraint `ActionInvocationClient`
//  records). `Driver.interpret` IS reachable, and it performs the identical
//  two-step: resolve the `TextSource` through the host's resolver, then apply
//  the egress policy to the RESOLVED string, then lower. So the ordering claim
//  is pinned here on the path where it can be executed, and the client arm is
//  the same shape read against the same helper.
//
//  Each test in the first list is written to GO RED under the swapped order —
//  gate first, resolve second — and the file says at each one how. That is not
//  decoration: an assertion that only fails when the feature is absent
//  entirely would pass against a host that checked the template and navigated
//  to the value, which is the exact defect this phase exists to prevent.
// ============================================================================

open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Driver
open Fuaran.UI.ServerDriven.Validation
open Fuaran.UI.Renderer

type private Msg = Noop

let private boundRoute: Action<Msg> =
    Action.Navigate(TextSource.Bound(Binding.State("route", None)), NavigateTarget.Self)

/// One button carrying the action under test. `interpret` is private, so the
/// action is reached through `step` — the same entry point a real connection
/// uses, which is the stronger place to assert from anyway.
let private viewOf (action: Action<Msg>) (_: int) : Node<Msg> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<Msg> with
            Children =
                [ Fuaran.button
                      "nav"
                      { Defaults.button<Msg> with
                          OnClick = action } ] }

/// A `DriverServices` whose `ResolveText` reads the supplied State map — the
/// same wiring a real host performs from its render context.
let private servicesWithState (state: (string * obj) list) =
    let sources =
        { BindingResolver.empty with
            State = Map.ofList state }

    { DriverServices.createPermissive (fun (n: Node<Msg>) -> $"<f id='{n.Id}'/>") with
        ResolveText = BindingResolver.resolveTextSource sources }

let private click: LiveEvent =
    { ConnId = "c1"
      NodeId = "nav"
      Event = "click"
      Payload = Map.empty
      LastSeq = 0 }

/// Dispatch the action through a one-step session and return its effects.
let private effectsOf (state: (string * obj) list) (action: Action<Msg>) =
    let session =
        init (servicesWithState state) (fun (_: Msg) (m: int) -> m) (viewOf action) 0

    let _, out = step session click
    out.Effects

[<Tests>]
let tests =
    testList
        "Phase 1536 — Action.Navigate resolves before it is gated"
        [ test "a bound route resolving to a javascript: URL is REFUSED on the resolved value" {
              // The declaration is a `Bound` binding; the VALUE is the hostile
              // string. Gate-then-resolve would judge something that is not a
              // URL at all, find nothing to refuse, and then hand the shim
              // `javascript:alert(1)` — so this assertion goes red under the
              // swap even though the policy here is `createPermissive`, which
              // permits every destination it is ASKED about. The scheme floor
              // is not a policy knob; it refuses this string under any policy,
              // which is precisely why it must be shown the string.
              let effects = effectsOf [ "route", box "javascript:alert(1)" |> nonNull ] boundRoute
              Expect.isEmpty effects "no navigation effect crosses to the shim"
          }

          test "a bound route resolving to a safe path lowers the RESOLVED value, not the declaration" {
              // The mirror of the test above, and the half that stops the
              // refusal being achieved by refusing everything. It also pins
              // WHICH string crosses: a host that lowered the declaration would
              // ship the encoded binding, and a host that lowered the template
              // would ship an empty route.
              let effects = effectsOf [ "route", box "/orders/42" |> nonNull ] boundRoute

              Expect.equal
                  effects
                  [ ClientEffect.Navigate("/orders/42", NavigateTarget.Self) ]
                  "the resolved destination is what crosses"
          }

          test "a bound route whose source is absent navigates NOWHERE" {
              // `resolveTextSource` renders an unresolved binding as the empty
              // string, which is right for a LABEL and wrong for a destination:
              // `location.href = ""` reloads the current document with its query
              // and fragment stripped. A host that lowered it would perform a
              // navigation the author never asked for.
              let effects = effectsOf [] boundRoute
              Expect.isEmpty effects "an unresolved route is not a destination"
          }

          test "a literal route is unchanged by the widening, and its target rides only when Blank" {
              let self =
                  effectsOf [] (Action.Navigate(TextSource.Literal "/docs", NavigateTarget.Self))

              let blank =
                  effectsOf [] (Action.Navigate(TextSource.Literal "/docs", NavigateTarget.Blank))

              Expect.equal self [ ClientEffect.Navigate("/docs", NavigateTarget.Self) ] "literal route, current context"
              Expect.equal blank [ ClientEffect.Navigate("/docs", NavigateTarget.Blank) ] "literal route, new context"
          }

          test "the Blank target reaches the shim as a member, and Self does not" {
              // The omit-at-default rule on the EFFECT channel, which is a
              // different wire from the document's. A shim written before this
              // phase keeps receiving byte-identical instructions for the case
              // it already handled.
              Expect.equal
                  (ClientEffect.encode (ClientEffect.Navigate("/docs", NavigateTarget.Self)))
                  """{"kind":"Navigate","route":"/docs"}"""
                  "Self is omitted — the pre-1536 bytes"

              Expect.equal
                  (ClientEffect.encode (ClientEffect.Navigate("/docs", NavigateTarget.Blank)))
                  """{"kind":"Navigate","route":"/docs","target":"Blank"}"""
                  "Blank rides"
          }

          // ─── The recorded invocation ──────────────────────────────────────
          test "the recorded description of a bound route discloses no binding argument" {
              // Phase 889's record describes the action as DECLARED, because no
              // resolver is in scope where it is built. A literal route is
              // scrubbed to its path exactly as before; a bound one prints
              // `<bound>` rather than the binding's spelling, whose arguments
              // (an i18n arg bag, say) can carry user data.
              Expect.equal
                  (Fuaran.UI.Ops.ActionInvocation.ActionInvocation.describe (
                      Action.Navigate(TextSource.Literal "/orders/42?email=a@b.c#tok", NavigateTarget.Self): Action<Msg>
                  ))
                  "Navigate(/orders/42)"
                  "a literal route keeps its pre-1536 scrubbed description"

              Expect.equal
                  (Fuaran.UI.Ops.ActionInvocation.ActionInvocation.describe (boundRoute: Action<Msg>))
                  "Navigate(<bound>)"
                  "a bound route names no binding argument"
          }

          // ─── The analysis walk ────────────────────────────────────────────
          test "a bound route is COUNTED as a binding use of the tree" {
              // `usesOfAction` is what a consumption rule reasons from, and a
              // rule reasoning from the absence of this use would be reasoning
              // from a surface the walk never looked at.
              let uses = BindingWalk.usesOfAction (boundRoute: Action<Msg>)
              Expect.equal (List.length uses) 1 "the route's binding is one use"
          }

          test "the in-place navigation shortcut declines a Blank target" {
              // Swapping this session's tree for a navigation that opens a
              // DIFFERENT context would be wrong twice over: the reader would
              // see this page change AND get a new one. It is declined for the
              // shortcut, not dropped — the ordinary path lowers the effect.
              let effects =
                  effectsOf [] (Action.Navigate(TextSource.Literal "/elsewhere", NavigateTarget.Blank))

              Expect.equal
                  effects
                  [ ClientEffect.Navigate("/elsewhere", NavigateTarget.Blank) ]
                  "a Blank navigation lowers as an effect"
          } ]
