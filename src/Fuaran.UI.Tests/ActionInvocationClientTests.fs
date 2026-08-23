module Fuaran.UI.Tests.ActionInvocationClient

// ============================================================================
//  Phase 889 — the CLIENT half of the user-action record.
//
//  What can be tested here, and what cannot. `runAction` is `private` and the
//  render path is Fable-only, so the client emission point itself is not
//  reachable from a .NET test runner — the same constraint `DispatchGateTests`
//  records, and the reason `applyDispatchGate` was made public in the first
//  place. So these tests pin the two things that ARE reachable and that the
//  client record is built out of:
//
//   1. the gate helpers' OUTCOME, which is how `runActionAs` classifies a
//      gesture as Dispatched vs Denied without consulting any gate twice. The
//      `Result`-returning variants are new in 889; the `unit` originals
//      delegate to them, so the pre-889 behaviour is the same code path;
//   2. the correlation-key spelling, which is duplicated across two tiers by
//      necessity and would otherwise drift in silence.
//
//  The end-to-end "one record per client gesture" assertion lives on the
//  server-driven path (`Fuaran.UI.ServerDriven.Tests`), where the emission
//  point is reachable.
// ============================================================================

open Expecto
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Runtime
open Fuaran.UI.Ops.ActionInvocation

/// Records Warn messages; `CanDispatch` is driven by the supplied predicate.
type private GatingRuntime(canDispatch: ActionDescriptor -> bool) =
    let warnings = ResizeArray<string>()
    member _.Warnings: string list = List.ofSeq warnings

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
        member _.TryRenderCustomInScope(_, _, _, _) = None
        member _.TryGetCustomRendererInScope(_, _, _) = None
        member _.CanDispatch(action) = canDispatch action
        member _.TryLoadGuest(_) = None

[<Tests>]
let tests =
    testList
        "Phase 889 — the client gate outcomes the record is built from"
        [ test "an allowed gate runs the effect and reports Ok" {
              let runtime = GatingRuntime(fun _ -> true)
              let mutable ran = false

              let outcome =
                  Render.applyDispatchGateOutcome runtime (ActionDescriptor.Notify "toast") (fun () -> ran <- true)

              Expect.isTrue ran "the effect ran"
              Expect.equal outcome (Ok()) "and the gate reports it ran"
              Expect.isEmpty runtime.Warnings "no diagnostic on allow"
          }

          test "a denied gate skips the effect and reports the reason the record carries" {
              let runtime = GatingRuntime(fun _ -> false)
              let mutable ran = false

              let outcome =
                  Render.applyDispatchGateOutcome runtime (ActionDescriptor.Notify "toast") (fun () -> ran <- true)

              Expect.isFalse ran "the effect did NOT run"

              match outcome with
              | Error reason -> Expect.stringContains reason "dispatch denied by policy gate" "the reason is log-safe"
              | Ok() -> failtest "expected a refusal"
          }

          test "the unit-returning original is byte-identical in behaviour, warning included" {
              // 889 reimplemented `applyDispatchGate` on top of the outcome
              // variant. If the delegation ever diverged, the whole pre-889
              // dispatch path would change under a telemetry phase.
              let a = GatingRuntime(fun _ -> false)
              let b = GatingRuntime(fun _ -> false)

              Render.applyDispatchGate a (ActionDescriptor.SetState "k") ignore

              Render.applyDispatchGateOutcome b (ActionDescriptor.SetState "k") ignore
              |> ignore

              Expect.equal a.Warnings b.Warnings "same diagnostic, verbatim"
              Expect.isNonEmpty a.Warnings "and there IS one to compare"
          }

          test "a host-reserved State key is a REFUSAL, and now a reportable one" {
              let runtime = GatingRuntime(fun _ -> true)
              let mutable wrote = false

              let refused =
                  Render.treeStateWriteOutcome runtime (StateKeys.HostReservedPrefix + "secret") (fun () ->
                      wrote <- true)

              Expect.isFalse wrote "the write did not happen"

              match refused with
              | Error reason -> Expect.stringContains reason "host-reserved" "the reason names why"
              | Ok() -> failtest "expected a refusal"

              let allowed =
                  Render.treeStateWriteOutcome runtime "ordinary.key" (fun () -> wrote <- true)

              Expect.isTrue wrote "an ordinary key still writes"
              Expect.equal allowed (Ok()) "…and reports Ok"
          }

          test "an unsafe route is refused, and the REPORTED reason drops the query string" {
              // The `Warn` keeps the full route — a developer's own console is
              // a different surface from a durable log. The recorded reason
              // must not.
              let runtime = GatingRuntime(fun _ -> true)
              let mutable navigated = None

              // Phase 1026 — `permissiveEgress` isolates the SCHEME floor here:
              // the policy admits every destination, so a refusal can only have
              // come from the floor, which is what this test is about.
              let outcome =
                  Render.treeNavigateOutcome
                      runtime
                      Sanitize.permissiveEgress
                      "javascript:steal()?token=SECRETVALUE"
                      (fun r -> navigated <- Some r)

              Expect.isNone navigated "nothing navigated"

              match outcome with
              | Error reason ->
                  Expect.isFalse (reason.Contains "SECRETVALUE") "the recorded reason carries no query string"
                  Expect.stringContains reason "not safe to render" "and still says what happened"
              | Ok() -> failtest "expected a refusal"
          }

          test "Phase 1026 — an UNDECLARED origin is refused, and the reason names the host, not the path" {
              // The complement of the test above: the URL is perfectly safe by
              // the scheme floor, and the DEFAULT policy still refuses it —
              // which is the whole of what 1026 changed. The recorded reason
              // must name the host (so an operator can act) and never the query
              // (which is where an exfiltrated payload sits).
              let runtime = GatingRuntime(fun _ -> true)
              let mutable navigated = None

              let outcome =
                  Render.treeNavigateOutcome
                      runtime
                      Sanitize.denyNonLocalEgress
                      "https://collector.example/collect?token=SECRETVALUE"
                      (fun r -> navigated <- Some r)

              Expect.isNone navigated "an undeclared origin never reaches the host router"

              match outcome with
              | Error reason ->
                  Expect.isFalse (reason.Contains "SECRETVALUE") "the recorded reason carries no query string"
                  Expect.stringContains reason "collector.example" "it names the host that was refused"
                  Expect.stringContains reason "route" "and the class it was refused for"
              | Ok() -> failtest "expected a refusal"
          }

          test "the interaction-id key is spelled the same in both tiers" {
              // The renderer cannot reference the server-driven driver and the
              // driver cannot reference the Feliz renderer, so the well-known
              // key is duplicated by necessity. This is what stops the
              // duplication drifting: two tiers reading different keys out of
              // one host's context would each report `None` and look correct.
              Expect.equal
                  Render.promptIdKey
                  ActionInvocation.interactionIdKey
                  "the renderer's key and the record's key are one key"
          } ]
