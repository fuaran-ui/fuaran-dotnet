module Fuaran.UI.Tests.DispatchGate

// ============================================================================
//  Phase 119 — renderer dispatch policy-gate seam on IFuaranRuntime.
//  Phase 782 — the default INVERTED to deny, and the descriptor set closed.
//
//  The renderer's `runAction` consults `IFuaranRuntime.CanDispatch` before
//  every wire-survivable host effect. These tests pin the decision helper
//  `Render.applyDispatchGate` (the exact code path runAction drives) without a
//  browser render:
//
//   1. Default runtimes REFUSE every descriptor (deny-by-default), and the
//      named permissive opt-in restores the old posture. This test previously
//      asserted the opposite; changing it IS Phase 782, not collateral damage.
//   2. A host that denies a descriptor: the effect does NOT run and a
//      diagnostic is emitted via Warn.
//   3. A host that allows: the effect runs and no diagnostic is emitted.
//   4. The gate is per-descriptor — a host can deny AiTool while allowing
//      Navigate.
//   5. The four previously-ungated actions (Notify / SetState /
//      WriteToClipboard / CommitLocal) now have descriptors and are refused by
//      an unconfigured host, permitted by a configured allow.
//   6. Host-reserved State keys are unaddressable from a tree-originated write
//      even when the gate ALLOWS — the namespace is structural, not policy.
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
        [ test "default runtimes REFUSE every gated descriptor (Phase 782)" {
              let everyDescriptor =
                  [ ActionDescriptor.Call "/api/x"
                    ActionDescriptor.Navigate "/home"
                    ActionDescriptor.AiTool "anything"
                    ActionDescriptor.ReadFileBody "f"
                    ActionDescriptor.ApplyTreeOp "{}"
                    ActionDescriptor.Notify "channel"
                    ActionDescriptor.SetState "k"
                    ActionDescriptor.WriteToClipboard
                    ActionDescriptor.CommitLocal "n" ]

              // Enumerated, not sampled: a descriptor added later without a
              // deny default should fail here, which is the whole point of
              // listing the closed set rather than three representatives.
              for d in everyDescriptor do
                  Expect.isFalse
                      (Runtime.diagnostic.CanDispatch d)
                      (sprintf "diagnostic refuses %s" (ActionDescriptor.describe d))

                  Expect.isFalse
                      ((MutableRuntime() :> IFuaranRuntime).CanDispatch d)
                      (sprintf "MutableRuntime refuses %s" (ActionDescriptor.describe d))
          }

          test "the permissive opt-in is the ONE named way back to the old posture" {
              let everyDescriptor =
                  [ ActionDescriptor.Call "/api/x"
                    ActionDescriptor.Navigate "/home"
                    ActionDescriptor.AiTool "anything"
                    ActionDescriptor.ReadFileBody "f"
                    ActionDescriptor.ApplyTreeOp "{}"
                    ActionDescriptor.Notify "channel"
                    ActionDescriptor.SetState "k"
                    ActionDescriptor.WriteToClipboard
                    ActionDescriptor.CommitLocal "n" ]

              for d in everyDescriptor do
                  Expect.isTrue
                      (Runtime.permissive.CanDispatch d)
                      (sprintf "Runtime.permissive allows %s" (ActionDescriptor.describe d))

                  Expect.isTrue
                      ((MutableRuntime.Permissive() :> IFuaranRuntime).CanDispatch d)
                      (sprintf "MutableRuntime.Permissive allows %s" (ActionDescriptor.describe d))
          }

          test "every wire-survivable action has a descriptor and a readable label" {
              // The four Phase 782 additions. `describe` feeds the deny
              // diagnostic, so a descriptor with no label is a silent denial.
              Expect.equal
                  (ActionDescriptor.describe (ActionDescriptor.Notify "alerts"))
                  "Notify(alerts)"
                  "Notify label"

              Expect.equal
                  (ActionDescriptor.describe (ActionDescriptor.SetState "theme"))
                  "SetState(theme)"
                  "SetState label"

              Expect.equal
                  (ActionDescriptor.describe ActionDescriptor.WriteToClipboard)
                  "WriteToClipboard"
                  "clipboard label carries no payload"

              Expect.equal
                  (ActionDescriptor.describe (ActionDescriptor.CommitLocal "n7"))
                  "CommitLocal(n7)"
                  "CommitLocal label"
          }

          test "the four newly-gated actions: unconfigured refuses, configured allow permits" {
              let newlyGated =
                  [ ActionDescriptor.Notify "alerts"
                    ActionDescriptor.SetState "theme"
                    ActionDescriptor.WriteToClipboard
                    ActionDescriptor.CommitLocal "n7" ]

              for d in newlyGated do
                  let denying = GatingRuntime(fun _ -> false)
                  let mutable ranUnderDeny = 0

                  Render.applyDispatchGate (denying :> IFuaranRuntime) d (fun () -> ranUnderDeny <- ranUnderDeny + 1)

                  Expect.equal ranUnderDeny 0 (sprintf "%s skipped when denied" (ActionDescriptor.describe d))

                  Expect.equal
                      denying.Warnings.Count
                      1
                      (sprintf "%s deny is RECORDED, not silent" (ActionDescriptor.describe d))

                  let allowing = GatingRuntime(fun _ -> true)
                  let mutable ranUnderAllow = 0

                  Render.applyDispatchGate (allowing :> IFuaranRuntime) d (fun () -> ranUnderAllow <- ranUnderAllow + 1)

                  Expect.equal ranUnderAllow 1 (sprintf "%s runs when allowed" (ActionDescriptor.describe d))
          }

          test "host-reserved State keys are unaddressable from a tree write, even under an ALLOW gate" {
              // The namespace is structural: `treeStateWrite` never consults the
              // gate. A host that allows everything still cannot be talked into
              // letting a decoded tree overwrite its own slot.
              let runtime = GatingRuntime(fun _ -> true)
              let mutable wrote = 0

              Render.treeStateWrite (runtime :> IFuaranRuntime) "host.session-token" (fun () -> wrote <- wrote + 1)

              Expect.equal wrote 0 "a host-reserved key is never written from a tree"
              Expect.equal runtime.Warnings.Count 1 "the refusal is recorded"
              Expect.stringContains runtime.Warnings[0] "host-reserved" "the diagnostic names the reason"

              // The ordinary case is untouched — this is a namespace, not a ban
              // on writing state.
              let ok = GatingRuntime(fun _ -> true)
              let mutable okWrote = 0
              Render.treeStateWrite (ok :> IFuaranRuntime) "theme" (fun () -> okWrote <- okWrote + 1)
              Expect.equal okWrote 1 "an unreserved key writes normally"
              Expect.equal ok.Warnings.Count 0 "no diagnostic for an ordinary key"

              // And the prefix is a prefix, not a substring: a key that merely
              // CONTAINS "host." is not reserved.
              let near = GatingRuntime(fun _ -> true)
              let mutable nearWrote = 0
              Render.treeStateWrite (near :> IFuaranRuntime) "my.host.thing" (fun () -> nearWrote <- nearWrote + 1)
              Expect.equal nearWrote 1 "'host.' mid-key is not the reserved namespace"
          }

          test "a javascript: route cannot reach a host router — client action path" {
              // `Render.treeNavigate` IS the Navigate arm of `runAction` (the arm
              // is one call to it), so this drives the shipping code path, not a
              // restatement of it.
              let unsafeRoutes =
                  [ "javascript:alert(1)"
                    "JaVaScRiPt:alert(1)"
                    "  javascript:alert(1)"
                    "vbscript:msgbox(1)"
                    "//evil.example/x"
                    "\\\\evil.example/x" ]

              for route in unsafeRoutes do
                  let runtime = GatingRuntime(fun _ -> true) // gate WIDE OPEN
                  let reached = ResizeArray<string>()

                  Render.treeNavigate (runtime :> IFuaranRuntime) route reached.Add

                  Expect.equal reached.Count 0 (sprintf "'%s' never reaches the host router" route)
                  Expect.equal runtime.Warnings.Count 1 (sprintf "'%s' refusal is recorded" route)

                  Expect.stringContains runtime.Warnings[0] "not a safe URL" "the diagnostic says why it was refused"

              // A legitimate route reaches the router, SANITISED (trimmed), and
              // only when the gate allows it.
              let ok = GatingRuntime(fun _ -> true)
              let okReached = ResizeArray<string>()
              Render.treeNavigate (ok :> IFuaranRuntime) "  /reports/42  " okReached.Add
              Expect.equal (List.ofSeq okReached) [ "/reports/42" ] "an ordinary route arrives sanitised"
              Expect.equal ok.Warnings.Count 0 "no diagnostic for a safe allowed route"

              // …and a safe route still obeys the gate — sanitisation is not a
              // second way to say yes.
              let denied = GatingRuntime(fun _ -> false)
              let deniedReached = ResizeArray<string>()
              Render.treeNavigate (denied :> IFuaranRuntime) "/reports/42" deniedReached.Add
              Expect.equal deniedReached.Count 0 "a safe route is still gated"
              Expect.stringContains denied.Warnings[0] "denied by policy gate" "the gate, not the sanitiser, refused"
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
