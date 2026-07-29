module Fuaran.UI.Tests.RenderEntrySeam

// ============================================================================
//  Two gaps the first Custom-node consumer hosts surfaced, both on the raw
//  render path rather than in the orchestration tier that was assumed to be
//  driving it.
//
//   A. `renderWithSourcesInScopeAndSink` — the render-entry matrix had no cell
//      combining a runtime SCOPE with a telemetry SINK: the scope-aware entry
//      hard-coded `TelemetrySink = None`, so a scoped host silently lost every
//      render-failure event as the price of state isolation.
//
//   B. `GuestSeam` — the `Mount` arm handed the guest the HOST runtime and
//      bridged its dispatch through the mount's `OnBubble` UNWRAPPED, so a
//      rendered guest was not default-deny; the capability gate applied only
//      where an orchestration tier authored the mount itself.
//
//  Both are pinned through a .NET-observable path, which needs saying because
//  Feliz's .NET-side `ReactElement` is opaque (the constraint
//  AccessibilityTests / ErrorBoundaryTests / StateStoreScopingTests document):
//  we cannot read the emitted DOM. What we CAN observe is the seams the
//  renderer calls INTO while rendering — `IFuaranRuntime.TryLoadGuest`, the
//  telemetry sink, and the installed `GuestSeam` — all of which are reached
//  eagerly, before the final element evaluation a .NET Feliz shim defers. Every
//  assertion below is against one of those.
//
//  The probes are deliberately two-sided: each "the gate fired" case is paired
//  with a pass-through case proving the same wiring DOES carry the effect when
//  the policy allows it. A one-sided drop assertion cannot distinguish a
//  working gate from broken plumbing.
// ============================================================================

open System.Collections.Generic
open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Runtime
open Fuaran.UI.Telemetry.Abstractions

// ─── Shared fixtures ───────────────────────────────────────────────────────

let private nn (value: 'T) : obj = box value |> Unchecked.nonNull

let private mkNode (id: string) (kind: NodeKind<obj>) : Node<obj> =
    { Id = id
      Kind = kind
      State = None
      Style = None
      Accessibility = None
      Motion = None
      ExtraAttributes = None }

let private benignBox (id: string) : Node<obj> =
    mkNode
        id
        (NodeKind.Box
            { Layout = BoxLayout.Auto
              Role = BoxRole.Dashboard
              Heading = None
              Children = [] })

let private mountNode (id: string) (scopeId: string) (onBubble: (obj -> Action<obj>) option) : Node<obj> =
    mkNode
        id
        (NodeKind.Mount
            { ScopeId = scopeId
              Inputs = None
              Channel =
                { Direction = ChannelDirection.OutOnly
                  MessageShape = None }
              OnBubble = onBubble
              Capabilities = [ "read" ] })

/// In-memory render-failure recorder (same shape as ErrorBoundaryTests' —
/// the Tests project does not reference `Telemetry.Default`).
type private RecordingSink() =
    let failures = ResizeArray<RenderFailureTelemetry>()

    member _.Failures: IReadOnlyList<RenderFailureTelemetry> = failures :> _

    interface IFuaranTelemetrySink with
        member _.RecordOpApply _ = ()
        member _.RecordDeny _ = ()
        member _.RecordRenderFailure tel = failures.Add tel
        member _.RecordProviderCall _ = ()
        member _.RecordCacheStat _ = ()
        member _.RecordValidateOutcome _ = ()

/// An `IFuaranRuntime` whose guest-loader is supplied per test. `loaded`
/// accumulates the scope ids it was consulted with, which is how a test tells
/// WHICH runtime instance the renderer handed to a guest. Everything else
/// delegates to the diagnostic runtime.
let private guestLoaderRuntime (loaded: ResizeArray<string>) (load: string -> Node<obj> option) : IFuaranRuntime =
    { new IFuaranRuntime with
        member _.Call(e, o) = Runtime.diagnostic.Call(e, o)
        member _.Notify(c, p) = Runtime.diagnostic.Notify(c, p)
        member _.Navigate(r) = Runtime.diagnostic.Navigate(r)
        member _.SetState(k, v) = Runtime.diagnostic.SetState(k, v)
        member _.InvokeAiTool(t, a) = Runtime.diagnostic.InvokeAiTool(t, a)
        member _.WriteToClipboard(t) = Runtime.diagnostic.WriteToClipboard(t)

        member _.ReadFileBody(f, e, o) =
            Runtime.diagnostic.ReadFileBody(f, e, o)

        member _.Warn(m) = Runtime.diagnostic.Warn(m)
        member _.LayoutObserver = None
        member _.TryRenderCustom(_, _, _) = None
        member _.TryGetCustomRenderer(_, _) = None
        member _.CanDispatch(_) = true

        member _.TryLoadGuest(scopeId) =
            loaded.Add scopeId
            load scopeId }

/// Render, tolerating the .NET Feliz shim's deferred element evaluation. Every
/// seam this file asserts on is reached BEFORE that evaluation, so swallowing
/// it loses no signal (same tolerance StateStoreScopingTests documents).
let private renderTolerantly (f: unit -> 'a) : unit =
    try
        f () |> ignore
    with _ ->
        ()

// ============================================================================
//  A. The scope × sink render entry.
//
//  The observable: a `Switch` reads its state key through `ctx.Scope` — the
//  SCOPED store when the entry set one, the process-global store otherwise —
//  and the matched case is a `Mount` whose loader THROWS. The throw is caught
//  by the per-node guard, which emits through `ctx.TelemetrySink`. So the
//  presence of that mount's node id in the sink proves BOTH threads at once:
//  the scoped value was visible (or the switch would have taken its benign
//  default) and the sink was wired (or the failure would have been swallowed).
//
//  Assert on WHICH node ids arrive, never on how MANY. Under .NET the Feliz
//  shim also throws while constructing elements, so the guard emits for
//  ordinary nodes too and a count is not a discriminator. (That same shim
//  throw is why this is observable at all — it is noise here and signal
//  nowhere else, so it is filtered by node id and by the loader's own
//  message.)
// ============================================================================

let private throwingScope = "res-throwing-guest"

let private switchOverScopedKey (stateKey: string) : Node<obj> =
    mkNode
        "res-switch"
        (NodeKind.Switch
            { StateKey = stateKey
              Cases =
                [ { Match = "boom"
                    Child = mountNode "res-boom-mount" throwingScope None } ]
              Default = benignBox "res-default" })

let private throwingLoaderRuntime () : IFuaranRuntime =
    guestLoaderRuntime (ResizeArray<string>()) (fun scopeId ->
        if scopeId = throwingScope then
            failwith "guest loader exploded"
        else
            None)

/// Did the sink receive the loader explosion attributed to the mount the
/// switch selects only when the SCOPED value is visible?
let private sawBoomMount (sink: RecordingSink) : bool =
    sink.Failures
    |> Seq.exists (fun f -> f.NodeId = "res-boom-mount" && f.ErrorMessage.Contains "guest loader exploded")

[<Tests>]
let scopedSinkEntryTests =
    testSequenced
    <| testList
        "render-entry matrix — scope × telemetry sink"
        [ test "renderWithSourcesInScopeAndSink threads BOTH the scope and the sink" {
              StateStore.reset ()
              StateStore.resetAllScopes ()
              (StateStore.forScope "res-scope-a").Set("res-key", nn "boom")

              let sink = RecordingSink()

              try
                  renderTolerantly (fun () ->
                      Render.renderWithSourcesInScopeAndSink
                          "res-scope-a"
                          BindingResolver.empty
                          (throwingLoaderRuntime ())
                          (sink :> IFuaranTelemetrySink)
                          ignore
                          (switchOverScopedKey "res-key"))

                  Expect.isTrue (sawBoomMount sink) "the loader failure under the SCOPED switch value reached the sink"

                  let boom = sink.Failures |> Seq.find (fun f -> f.NodeId = "res-boom-mount")

                  Expect.equal boom.CaughtBy RenderFailureSource.PerNodeGuard "caught by the per-node guard"
              finally
                  StateStore.reset ()
                  StateStore.resetAllScopes ()
          }

          test "the scoped read does not fall back to the global store" {
              // The probe's other half: same tree, same sink, value present ONLY
              // in the process-global store. If the entry's scoping were a no-op
              // the switch would match and this would fail exactly like the case
              // above — so a zero here is the scope working, not the tree being
              // inert.
              StateStore.reset ()
              StateStore.resetAllScopes ()
              StateStore.set "res-key" (nn "boom")

              let sink = RecordingSink()

              try
                  renderTolerantly (fun () ->
                      Render.renderWithSourcesInScopeAndSink
                          "res-scope-empty"
                          BindingResolver.empty
                          (throwingLoaderRuntime ())
                          (sink :> IFuaranTelemetrySink)
                          ignore
                          (switchOverScopedKey "res-key"))

                  Expect.isFalse
                      (sawBoomMount sink)
                      "the switch took its default — the global value was invisible from the scope"
              finally
                  StateStore.reset ()
                  StateStore.resetAllScopes ()
          }

          test "the sink-less scope entry cannot report the same failure (the gap this closes)" {
              // `renderWithSourcesInScope` hard-codes `TelemetrySink = None`, so
              // the IDENTICAL failure the first case recorded goes nowhere. This
              // is the FGP-5 coverage gap stated as an executable fact, so a
              // future edit that quietly re-adds a sink to that entry — or drops
              // it from the new one — turns a test red rather than passing
              // silently.
              StateStore.reset ()
              StateStore.resetAllScopes ()
              (StateStore.forScope "res-scope-a").Set("res-key", nn "boom")

              let sink = RecordingSink()

              try
                  renderTolerantly (fun () ->
                      Render.renderWithSourcesInScope
                          "res-scope-a"
                          BindingResolver.empty
                          (throwingLoaderRuntime ())
                          ignore
                          (switchOverScopedKey "res-key"))

                  Expect.equal sink.Failures.Count 0 "the scope-only entry has no sink to deliver to"
              finally
                  StateStore.reset ()
                  StateStore.resetAllScopes ()
          }

          test "passing Map.empty-equivalent state leaves the entry byte-equivalent to the unscoped pair" {
              // A scope whose store is empty resolves exactly what the unscoped
              // entry would: the merge is `scopeSnapshot over sources.State`, so
              // an empty scope contributes nothing. Pins that the new entry adds
              // isolation without changing resolution for a host that has not
              // written anything scoped yet.
              StateStore.reset ()
              StateStore.resetAllScopes ()
              let sink = RecordingSink()

              try
                  renderTolerantly (fun () ->
                      Render.renderWithSourcesInScopeAndSink
                          "res-scope-quiet"
                          BindingResolver.empty
                          (throwingLoaderRuntime ())
                          (sink :> IFuaranTelemetrySink)
                          ignore
                          (switchOverScopedKey "res-key"))

                  Expect.isFalse (sawBoomMount sink) "no scoped value, no match — the default branch rendered"
              finally
                  StateStore.reset ()
                  StateStore.resetAllScopes ()
          } ]

// ============================================================================
//  B. The guest capability seam.
//
//  Two things must hold, and the second is the security-shaped one:
//
//   1. With NO seam installed the Mount arm behaves exactly as before — the
//      guest gets the host runtime, the bubble is unwrapped. No silent change
//      for existing hosts.
//   2. With a seam installed, BOTH the guest's runtime and the guest's dispatch
//      pass the host's policy.
//
//  Observing (2)'s dispatch leg needs a two-level mount, because a guest's
//  `Dispatch` is only invoked by an event handler (browser-only) OR by a nested
//  mount bubbling through it. So: host mounts `outer`, whose guest tree is
//  itself a mount (`inner`), whose guest tree is a benign box. The seam hands
//  us `inner`'s raw bubble; calling it runs `inner`'s `OnBubble` in the GUEST
//  context, whose `Dispatch` is whatever the renderer installed for `outer` —
//  the gated function if the seam is on the path, the raw one if it is not.
// ============================================================================

let private outerScope = "res-outer-guest"
let private innerScope = "res-inner-guest"

/// host → Mount(outer) → [loader] → Mount(inner) → [loader] → Box
let private nestedMountHost () =
    let host =
        mountNode "res-outer" outerScope (Some(fun _ -> Action.Dispatch(nn "bubbled-to-host")))

    let load (scopeId: string) : Node<obj> option =
        if scopeId = outerScope then
            Some(mountNode "res-inner" innerScope (Some(fun _ -> Action.Dispatch(nn "bubbled-from-inner"))))
        elif scopeId = innerScope then
            Some(benignBox "res-inner-body")
        else
            None

    host, load

[<Tests>]
let guestSeamTests =
    testSequenced
    <| testList
        "Mount guest capability seam"
        [ test "with no seam installed the guest gets the HOST runtime (unchanged behaviour)" {
              Render.clearGuestSeam ()
              StateStore.resetAllScopes ()
              let hostCalls = ResizeArray<string>()
              let host, load = nestedMountHost ()

              try
                  renderTolerantly (fun () ->
                      Render.renderWithSourcesAndSink
                          BindingResolver.empty
                          (guestLoaderRuntime hostCalls load)
                          (RecordingSink() :> IFuaranTelemetrySink)
                          ignore
                          host)

                  Expect.equal
                      (List.ofSeq hostCalls)
                      [ outerScope; innerScope ]
                      "both mounts resolved through the ONE host runtime — nothing was wrapped"
              finally
                  Render.clearGuestSeam ()
                  StateStore.resetAllScopes ()
          }

          test "an installed seam wraps the runtime the guest renders against" {
              Render.clearGuestSeam ()
              StateStore.resetAllScopes ()
              let hostCalls = ResizeArray<string>()
              let wrappedCalls = ResizeArray<string>()
              let seenContexts = ResizeArray<Render.GuestSeamContext>()
              let host, load = nestedMountHost ()

              let seam: Render.GuestSeam =
                  { WrapRuntime =
                      fun ctx _hostRuntime ->
                          seenContexts.Add ctx
                          // A different runtime instance entirely — so a guest
                          // that reached the host one would record in the WRONG
                          // list, which is the assertion below.
                          guestLoaderRuntime wrappedCalls load
                    GateBubble = fun _ raw -> raw }

              try
                  Render.installGuestSeam seam

                  renderTolerantly (fun () ->
                      Render.renderWithSourcesAndSink
                          BindingResolver.empty
                          (guestLoaderRuntime hostCalls load)
                          (RecordingSink() :> IFuaranTelemetrySink)
                          ignore
                          host)

                  Expect.equal
                      (List.ofSeq hostCalls)
                      [ outerScope ]
                      "the HOST runtime resolved only the host's own mount"

                  Expect.equal
                      (List.ofSeq wrappedCalls)
                      [ innerScope ]
                      "the guest resolved its nested mount through the WRAPPED runtime"

                  Expect.equal
                      (seenContexts |> Seq.map _.ScopeId |> List.ofSeq)
                      [ outerScope; innerScope ]
                      "the seam saw each mount's own scope id"

                  Expect.equal seenContexts[0].Capabilities [ "read" ] "the seam saw the mount's declared capabilities"

                  Expect.equal
                      seenContexts[0].Channel.Direction
                      ChannelDirection.OutOnly
                      "the seam saw the mount's declared channel direction"
              finally
                  Render.clearGuestSeam ()
                  StateStore.resetAllScopes ()
          }

          test "a pass-through gate leaves the guest's bubble reaching the host" {
              // The control case. Without it, the drop assertion below could not
              // tell a working gate from a bubble path that never worked.
              Render.clearGuestSeam ()
              StateStore.resetAllScopes ()
              let mutable hostDispatched = 0
              let mutable innerRaw: (obj -> unit) option = None
              let host, load = nestedMountHost ()

              let seam: Render.GuestSeam =
                  { WrapRuntime = fun _ rt -> rt
                    GateBubble =
                      fun ctx raw ->
                          if ctx.ScopeId = innerScope then
                              innerRaw <- Some raw

                          raw }

              try
                  Render.installGuestSeam seam

                  renderTolerantly (fun () ->
                      Render.renderWithSourcesAndSink
                          BindingResolver.empty
                          (guestLoaderRuntime (ResizeArray<string>()) load)
                          (RecordingSink() :> IFuaranTelemetrySink)
                          (fun _ -> hostDispatched <- hostDispatched + 1)
                          host)

                  Expect.isSome innerRaw "the seam was consulted for the nested guest's bubble"
                  innerRaw.Value(nn "ping")

                  Expect.equal
                      hostDispatched
                      1
                      "the inner bubble crossed the outer boundary and reached the host's dispatch"
              finally
                  Render.clearGuestSeam ()
                  StateStore.resetAllScopes ()
          }

          test "a denying gate stops the guest's bubble at the boundary" {
              Render.clearGuestSeam ()
              StateStore.resetAllScopes ()
              let mutable hostDispatched = 0
              let mutable denied = 0
              let mutable innerRaw: (obj -> unit) option = None
              let host, load = nestedMountHost ()

              let seam: Render.GuestSeam =
                  { WrapRuntime = fun _ rt -> rt
                    GateBubble =
                      fun ctx raw ->
                          if ctx.ScopeId = innerScope then
                              innerRaw <- Some raw
                              raw
                          else
                              // Deny at the OUTER boundary — the guest's dispatch
                              // is the gated function, so nothing crosses.
                              fun _ -> denied <- denied + 1 }

              try
                  Render.installGuestSeam seam

                  renderTolerantly (fun () ->
                      Render.renderWithSourcesAndSink
                          BindingResolver.empty
                          (guestLoaderRuntime (ResizeArray<string>()) load)
                          (RecordingSink() :> IFuaranTelemetrySink)
                          (fun _ -> hostDispatched <- hostDispatched + 1)
                          host)

                  Expect.isSome innerRaw "the seam was consulted for the nested guest's bubble"
                  innerRaw.Value(nn "ping")

                  Expect.equal denied 1 "the outer gate saw the guest's dispatch"
                  Expect.equal hostDispatched 0 "and refused it — the host never saw the message"
              finally
                  Render.clearGuestSeam ()
                  StateStore.resetAllScopes ()
          }

          test "clearGuestSeam restores the ungated default" {
              Render.clearGuestSeam ()

              Render.installGuestSeam
                  { WrapRuntime = fun _ rt -> rt
                    GateBubble = fun _ raw -> raw }

              Expect.isSome (Render.currentGuestSeam ()) "installed"
              Render.clearGuestSeam ()
              Expect.isNone (Render.currentGuestSeam ()) "cleared — a later host is not gated by a stale policy"
          } ]
