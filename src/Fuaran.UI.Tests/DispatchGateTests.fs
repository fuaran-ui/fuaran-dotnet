module Fuaran.UI.Tests.DispatchGate

// ============================================================================
//  Phase 119 — renderer dispatch policy-gate seam on IFuaranRuntime.
//
//  The renderer's `runAction` consults `IFuaranRuntime.CanDispatch` before the
//  gated host effects (Call / Navigate / AiTool). These tests pin the
//  decision helper `Render.applyDispatchGate` (the exact code path runAction
//  drives) without a browser render:
//
//   1. Default runtimes allow every descriptor (allow-by-default — existing
//      hosts behave unchanged).
//   2. A host that denies a descriptor: the effect does NOT run and a
//      diagnostic is emitted via Warn.
//   3. A host that allows: the effect runs and no diagnostic is emitted.
//   4. The gate is per-descriptor — a host can deny AiTool while allowing
//      Navigate.
//
//  All of this is pure .NET (no Feliz render), so it exercises the same
//  decision logic the Fable pipeline runs.
// ============================================================================

open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Runtime

/// A runtime whose CanDispatch is driven by a supplied predicate, recording
/// every Warn message. All other members delegate to the diagnostic runtime.
type private GatingRuntime(canDispatch: ActionDescriptor -> bool) =
    let warnings = ResizeArray<string>()
    member _.Warnings = warnings

    interface IFuaranRuntime with
        member _.Call(endpoint, onResult) =
            Runtime.diagnostic.Call(endpoint, onResult)

        member _.Notify(channel, payload) =
            Runtime.diagnostic.Notify(channel, payload)

        member _.Navigate(route) = Runtime.diagnostic.Navigate(route)
        member _.SetState(key, value) = Runtime.diagnostic.SetState(key, value)

        member _.InvokeAiTool(toolName, args) =
            Runtime.diagnostic.InvokeAiTool(toolName, args)

        member _.WriteToClipboard(text) =
            Runtime.diagnostic.WriteToClipboard(text)

        member _.ReadFileBody(file, encoding, onRead) =
            Runtime.diagnostic.ReadFileBody(file, encoding, onRead)

        member _.Warn(message) = warnings.Add message
        member _.LayoutObserver = None
        member _.TryRenderCustom(_, _, _) = None
        member _.TryGetCustomRenderer(_, _) = None
        member _.CanDispatch(action) = canDispatch action
        member _.TryLoadGuest(_) = None

[<Tests>]
let tests =
    testList
        "Phase 119 — renderer dispatch policy gate"
        [ test "default runtimes allow every gated descriptor" {
              Expect.isTrue (Runtime.diagnostic.CanDispatch(ActionDescriptor.AiTool "anything")) "AiTool allowed"
              Expect.isTrue (Runtime.diagnostic.CanDispatch(ActionDescriptor.Navigate "/home")) "Navigate allowed"
              Expect.isTrue (Runtime.diagnostic.CanDispatch(ActionDescriptor.Call "/api/x")) "Call allowed"

              Expect.isTrue
                  ((MutableRuntime() :> IFuaranRuntime).CanDispatch(ActionDescriptor.AiTool "t"))
                  "Mutable allows"
          }

          test "allow: the host effect runs and no diagnostic is emitted" {
              let runtime = GatingRuntime(fun _ -> true)
              let mutable ran = 0

              Render.applyDispatchGate (runtime :> IFuaranRuntime) (ActionDescriptor.AiTool "send") (fun () ->
                  ran <- ran + 1)

              Expect.equal ran 1 "effect ran exactly once when allowed"
              Expect.equal runtime.Warnings.Count 0 "no diagnostic on allow"
          }

          test "deny: the host effect is skipped and a diagnostic is emitted" {
              let runtime = GatingRuntime(fun _ -> false)
              let mutable ran = 0

              Render.applyDispatchGate (runtime :> IFuaranRuntime) (ActionDescriptor.AiTool "dangerous") (fun () ->
                  ran <- ran + 1)

              Expect.equal ran 0 "effect skipped when denied"
              Expect.equal runtime.Warnings.Count 1 "one diagnostic emitted on deny"
              Expect.stringContains runtime.Warnings[0] "denied by policy gate" "diagnostic names the deny"
              Expect.stringContains runtime.Warnings[0] "AiTool(dangerous)" "diagnostic names the descriptor"
          }

          test "the gate is per-descriptor — deny AiTool while allowing Navigate" {
              // A standalone host's hydrated allowlist: AiTool denied, nav free.
              let runtime =
                  GatingRuntime(fun d ->
                      match d with
                      | ActionDescriptor.AiTool _ -> false
                      | _ -> true)

              let mutable navRan = 0
              let mutable toolRan = 0

              Render.applyDispatchGate (runtime :> IFuaranRuntime) (ActionDescriptor.Navigate "/x") (fun () ->
                  navRan <- navRan + 1)

              Render.applyDispatchGate (runtime :> IFuaranRuntime) (ActionDescriptor.AiTool "y") (fun () ->
                  toolRan <- toolRan + 1)

              Expect.equal navRan 1 "Navigate allowed → ran"
              Expect.equal toolRan 0 "AiTool denied → skipped"
              Expect.equal runtime.Warnings.Count 1 "only the AiTool deny emitted a diagnostic"
          } ]
