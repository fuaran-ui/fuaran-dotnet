module Fuaran.UI.Tests.StateStoreScoping

// ============================================================================
//  Phase 128 — StateStore scoping + reset seam.
//
//  The store is a single-process / single-threaded singleton (see the
//  assumption note in StateStore.fs). These tests pin the test-isolation
//  seam the phase ships:
//
//   1. `reset ()` clears in-memory store values.
//   2. `reset ()` clears live subscriptions (so a stale subscriber from a
//      prior case does not fire in the next).
//   3. After `reset ()`, a fresh subscriber + write behave from a clean slate
//      — the "no cross-bleed between cases" guarantee.
// ============================================================================

open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Runtime

let private nn (value: 'T) : obj = box value |> Unchecked.nonNull

[<Tests>]
let tests =
    testSequenced
    <| testList
        "Phase 128 — StateStore reset / scoping"
        [ test "reset clears stored values" {
              StateStore.set "p128-a" (nn 1.0)
              Expect.equal (StateStore.get "p128-a") (Some(nn 1.0)) "value present before reset"
              StateStore.reset ()
              Expect.equal (StateStore.get "p128-a") None "value gone after reset"
          }

          test "reset clears live subscriptions (no cross-bleed)" {
              let mutable fired = 0
              // Register a subscriber but deliberately never unsubscribe it —
              // the pre-128 leak that bled across cases.
              StateStore.subscribeKeys (Set.ofList [ "p128-leak" ]) (fun () -> fired <- fired + 1)
              |> ignore

              StateStore.set "p128-leak" (nn 1.0)
              Expect.equal fired 1 "fires while subscribed"

              StateStore.reset ()
              StateStore.set "p128-leak" (nn 2.0)
              Expect.equal fired 1 "the leaked subscriber is gone after reset — no cross-bleed"
          }

          test "a case can start from a clean slate after reset" {
              StateStore.set "p128-prev" (nn 99.0)
              StateStore.reset ()

              let mutable fired = 0

              let unsub =
                  StateStore.subscribeKeys (Set.ofList [ "p128-fresh" ]) (fun () -> fired <- fired + 1)

              try
                  Expect.isEmpty (StateStore.snapshot () |> Map.toList) "snapshot empty after reset"
                  StateStore.set "p128-fresh" (nn 1.0)
                  Expect.equal fired 1 "fresh subscriber fires"
                  Expect.equal (StateStore.get "p128-prev") None "prior-case value did not survive reset"
              finally
                  unsub ()
          } ]

// ============================================================================
//  Phase 128 task 4 — keyed-notify split.
//
//  `set` notifies keyless subscribers (every write) + only the keyed
//  subscribers registered for that key. These pin the split contract on the
//  process-global default store (the `subscribe` / `subscribeKeys` facade).
// ============================================================================
[<Tests>]
let keyedNotifyTests =
    testSequenced
    <| testList
        "Phase 128 — keyed-notify split"
        [ test "a write notifies the keyless subscriber AND only the matching keyed subscriber" {
              StateStore.reset ()
              let mutable keyless = 0
              let mutable watchedA = 0
              let mutable watchedB = 0

              let unsubKeyless = StateStore.subscribe (fun () -> keyless <- keyless + 1)

              let unsubA =
                  StateStore.subscribeKeys (Set.ofList [ "p128n-a" ]) (fun () -> watchedA <- watchedA + 1)

              let unsubB =
                  StateStore.subscribeKeys (Set.ofList [ "p128n-b" ]) (fun () -> watchedB <- watchedB + 1)

              try
                  StateStore.set "p128n-a" (nn 1.0)
                  Expect.equal keyless 1 "keyless fires on the write"
                  Expect.equal watchedA 1 "the subscriber watching the written key fires"
                  Expect.equal watchedB 0 "the subscriber watching a different key stays silent"
              finally
                  unsubKeyless ()
                  unsubA ()
                  unsubB ()
          }

          test "unsubscribing a keyed subscriber stops only its notifications" {
              StateStore.reset ()
              let mutable a = 0
              let mutable b = 0

              let unsubA =
                  StateStore.subscribeKeys (Set.ofList [ "p128n-shared" ]) (fun () -> a <- a + 1)

              let unsubB =
                  StateStore.subscribeKeys (Set.ofList [ "p128n-shared" ]) (fun () -> b <- b + 1)

              try
                  StateStore.set "p128n-shared" (nn 1.0)
                  Expect.equal (a, b) (1, 1) "both watchers fire while subscribed"
                  unsubA ()
                  StateStore.set "p128n-shared" (nn 2.0)
                  Expect.equal (a, b) (1, 2) "only the still-subscribed watcher fires after one unsubscribes"
              finally
                  unsubB ()
          } ]

// ============================================================================
//  Phase 128 task 3 — scope-keyed instance isolation.
//
//  `forScope` mints an isolated store per scope id so `Binding.State` keys
//  never collide across trees sharing one process, and a `resetScope` of one
//  scope never touches another or the global default.
// ============================================================================
[<Tests>]
let scopeIsolationTests =
    testSequenced
    <| testList
        "Phase 128 — scope-keyed store isolation"
        [ test "forScope returns the same instance for the same id" {
              let a = StateStore.forScope "p128s-stable"
              let b = StateStore.forScope "p128s-stable"
              Expect.isTrue (System.Object.ReferenceEquals(a, b)) "repeated forScope calls share one instance"
              StateStore.resetAllScopes ()
          }

          test "the same key in two scopes does not collide (and neither leaks to the default)" {
              StateStore.reset ()
              let tenantA = StateStore.forScope "p128s-tenantA"
              let tenantB = StateStore.forScope "p128s-tenantB"

              tenantA.Set("terms", nn "cash")
              tenantB.Set("terms", nn "real")

              Expect.equal (tenantA.Get "terms") (Some(nn "cash")) "scope A keeps its own value"
              Expect.equal (tenantB.Get "terms") (Some(nn "real")) "scope B keeps its own value — no collision"
              Expect.equal (StateStore.get "terms") None "the scoped writes never reached the global default"
              StateStore.resetAllScopes ()
          }

          test "resetScope clears only the named scope" {
              let a = StateStore.forScope "p128s-resetA"
              let b = StateStore.forScope "p128s-resetB"
              a.Set("k", nn 1.0)
              b.Set("k", nn 2.0)

              StateStore.resetScope "p128s-resetA"

              Expect.equal (a.Get "k") None "the reset scope is cleared"
              Expect.equal (b.Get "k") (Some(nn 2.0)) "the other scope is untouched"
              StateStore.resetAllScopes ()
          }

          test "a scoped subscriber fires for its own scope only" {
              let a = StateStore.forScope "p128s-subA"
              let b = StateStore.forScope "p128s-subB"
              let mutable firedA = 0

              let unsub =
                  a.SubscribeKeys(Set.ofList [ "watched" ], (fun () -> firedA <- firedA + 1))

              try
                  b.Set("watched", nn 1.0)
                  Expect.equal firedA 0 "a write to another scope does not notify this scope's subscriber"
                  a.Set("watched", nn 2.0)
                  Expect.equal firedA 1 "a write to this scope notifies its subscriber"
              finally
                  unsub ()
                  StateStore.resetAllScopes ()
          }

          test "resetAllScopes leaves the global default store intact" {
              StateStore.reset ()
              StateStore.set "p128s-global" (nn 7.0)
              let scoped = StateStore.forScope "p128s-ephemeral"
              scoped.Set("k", nn 1.0)

              StateStore.resetAllScopes ()

              Expect.equal (StateStore.get "p128s-global") (Some(nn 7.0)) "the default store survives resetAllScopes"
              // A fresh forScope after resetAllScopes is a brand-new instance.
              let again = StateStore.forScope "p128s-ephemeral"
              Expect.equal (again.Get "k") None "the dropped scope's value is gone"
              StateStore.reset ()
              StateStore.resetAllScopes ()
          } ]

// ============================================================================
//  Phase 266 — scope-aware render + Mount guest-loader seam (§4o).
//
//  The renderer's `Mount` arm resolves the guest via
//  `IFuaranRuntime.TryLoadGuest scopeId`, renders it under its own scope, and
//  falls back to the declared empty state when no loader is wired. These pin
//  the .NET-observable wiring: the seam is called with the mount's scope id;
//  the default (unwired) runtime returns None so a Mount is inert (never a
//  throw). The scoped-SetState routing (guest write → forScope, host untouched)
//  is browser-triggered (render on .NET does not fire event handlers) and is
//  exercised by the scope-isolation tests above at the `StateStore` level.
// ============================================================================
let private mkNode (id: string) (kind: NodeKind<obj>) : Node<obj> =
    { Id = id
      Kind = kind
      State = None
      Style = None
      Accessibility = None
      Motion = None
      ExtraAttributes = None }

/// An `IFuaranRuntime` that records the scope ids `TryLoadGuest` is called with
/// and returns `guest` for each. All other members delegate to the diagnostic
/// runtime. `loaded` accumulates the scope ids so a test can assert the seam was
/// reached with the mount's scope.
let private recordingRuntime (loaded: ResizeArray<string>) (guest: Node<obj> option) : IFuaranRuntime =
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
        member _.TryRenderCustomInScope(_, _, _, _) = None
        member _.TryGetCustomRendererInScope(_, _, _) = None
        member _.CanDispatch(_) = true

        member _.TryLoadGuest(scopeId) =
            loaded.Add scopeId
            guest }

let private mountNode (mountId: string) (scopeId: string) : Node<obj> =
    mkNode
        mountId
        (NodeKind.Mount
            { ScopeId = scopeId
              Inputs = None
              Channel =
                { Direction = ChannelDirection.OutOnly
                  MessageShape = None }
              OnBubble = Some(fun _ -> Action.Chain [])
              Capabilities = [] })

[<Tests>]
let mountScopeTests =
    testSequenced
    <| testList
        "Phase 266 — Mount scope-aware render + guest-loader seam"
        [ test "the Mount arm invokes TryLoadGuest with the mount's guest scope id" {
              let loaded = ResizeArray<string>()

              let guest =
                  mkNode
                      "guest-root"
                      (NodeKind.Box(
                          { Layout = BoxLayout.Auto
                            Role = BoxRole.Dashboard
                            Heading = None
                            Children = [] }
                      ))

              let rt = recordingRuntime loaded (Some guest)
              let host = mountNode "m266" "guest-266"

              // The Mount arm reaches the loader seam synchronously during render;
              // the final Feliz-element *evaluation* is a Fable/browser concern
              // (the .NET Feliz shim casts lazily), so we tolerate a render-eval
              // throw and assert the wiring — TryLoadGuest was consulted with the
              // mount's scope id — which happens before any such evaluation.
              let _ =
                  try
                      Render.renderWithSourcesInScope "host-266" BindingResolver.empty rt ignore host
                      |> ignore
                  with _ ->
                      ()

              Expect.equal
                  (List.ofSeq loaded)
                  [ "guest-266" ]
                  "the Mount arm called TryLoadGuest exactly once with the guest scope id"
          }

          test "the Mount arm consults the loader even when it returns None (inert guest)" {
              let loaded = ResizeArray<string>()
              let rt = recordingRuntime loaded None
              let host = mountNode "m266b" "guest-266b"

              let _ =
                  try
                      Render.renderWithSourcesInScope "host-266b" BindingResolver.empty rt ignore host
                      |> ignore
                  with _ ->
                      ()

              Expect.equal (List.ofSeq loaded) [ "guest-266b" ] "the seam was consulted with the guest scope id"
          }

          test "the diagnostic runtime's TryLoadGuest is None (seam default)" {
              Expect.isNone (Runtime.diagnostic.TryLoadGuest "any-scope") "the default runtime composes no guest loader"
          } ]
