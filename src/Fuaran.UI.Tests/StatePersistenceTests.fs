module Fuaran.UI.Tests.StatePersistence

// ============================================================================
//  Phase 1532 — declared persistent state keys.
//
//  What the store does with a value AFTER the write is what these pin. The
//  in-memory half is covered by the Phase 128 scoping suite beside this one;
//  everything here is about the durable half:
//
//   1. Nothing persists until the HOST declares the key. That is the whole
//      posture, and it is the one assertion that fails if the allow-list is
//      removed.
//   2. Hydration is gated by the same declaration, so retiring a key retires
//      the value it left behind.
//   3. `Remove` and `Reset` clear what was persisted — a cleared value that
//      comes back on reload reads as the clear not having worked.
//   4. A backing store that REFUSES a write (the browser at its quota) is a
//      diagnostic, not an exception: the in-memory write and the notification
//      both stand.
//   5. `disposeScope` retires the instance and sweeps its prefix, which is the
//      per-request leak `forScope` had no exit from.
//
//  The store's persistence is a PORT (`StateStore.StatePersistence`), so all of
//  this is reachable from .NET. It has to be: the platform default off the
//  browser stores nothing, and the quota arm has no .NET twin at all — without
//  the port these are paths no test on this leg can enter.
// ============================================================================

open System
open Expecto
open Fuaran.UI.Renderer

/// A fresh in-memory backing store, and a store instance over it. The
/// persistence value doubles as the test's view of what was really written —
/// asserting through `Read` asks the backing store, not the store's own
/// memory, which is the distinction every case here turns on.
let private freshStore (prefix: string) =
    let persistence = StateStore.inMemoryPersistence ()
    StateStore.StateStoreInstance(prefix, persistence), persistence

let private nn (value: 'T) : obj = box value |> Unchecked.nonNull

[<Tests>]
let tests =
    testList
        "Phase 1532 — declared persistent state keys"
        [ test "an undeclared write survives the session and nothing else" {
              let store, persistence = freshStore "t1532.a."
              store.Set("mode", nn "cash")

              Expect.equal (store.Get "mode") (Some(nn "cash")) "the value is live for the session"

              Expect.isNone
                  (persistence.Read "t1532.a.mode")
                  "an undeclared key reaches no backing store — the default is session-lived, and a tree cannot opt itself in"
          }

          test "a declared write reaches the backing store" {
              let store, persistence = freshStore "t1532.b."
              store.DeclarePersistent [ "mode" ]
              store.Set("mode", nn "real")

              Expect.equal
                  (persistence.Read "t1532.b.mode")
                  (Some "real")
                  "the declared key is stored under the instance's prefix"
          }

          test "a declared value is hydrated by a fresh store on the same prefix" {
              // The reload, modelled: a second instance over the same backing
              // store, with no in-memory value of its own.
              let persistence = StateStore.inMemoryPersistence ()
              let first = StateStore.StateStoreInstance("t1532.c.", persistence)
              first.DeclarePersistent [ "theme" ]
              first.Set("theme", nn "dark")

              let second = StateStore.StateStoreInstance("t1532.c.", persistence)
              second.DeclarePersistent [ "theme" ]

              Expect.equal (second.Get "theme") (Some(nn "dark")) "the second instance hydrates the stored value"
          }

          test "hydration is gated by the declaration, not only the write" {
              // A value already in storage — written by a previous release that
              // did declare the key, or by hand.
              let persistence = StateStore.inMemoryPersistence ()
              persistence.Write "t1532.d.legacy" "stale"
              let store = StateStore.StateStoreInstance("t1532.d.", persistence)

              Expect.isNone
                  (store.Get "legacy")
                  "an undeclared key is not read back — retiring a key retires the value it left behind, rather than serving it indefinitely"

              store.DeclarePersistent [ "legacy" ]
              Expect.equal (store.Get "legacy") (Some(nn "stale")) "declaring it makes the same stored value readable"
          }

          test "a non-string value never persists, declared or not" {
              let store, persistence = freshStore "t1532.e."
              store.DeclarePersistent [ "count" ]
              store.Set("count", nn 3.0)

              Expect.equal (store.Get "count") (Some(nn 3.0)) "the value is live"

              Expect.isNone
                  (persistence.Read "t1532.e.count")
                  "non-string state is in-memory only, and the declaration does not change that"
          }

          test "Remove clears the persisted value" {
              let store, persistence = freshStore "t1532.f."
              store.DeclarePersistent [ "choice" ]
              store.Set("choice", nn "north")
              Expect.equal (persistence.Read "t1532.f.choice") (Some "north") "stored before the clear"

              store.Remove "choice"

              Expect.isNone (store.Get "choice") "gone from memory"

              Expect.isNone
                  (persistence.Read "t1532.f.choice")
                  "gone from storage too — a cleared choice that returns on the next reload is indistinguishable from a clear that never happened"
          }

          test "Remove notifies watchers even when the value was only persisted" {
              let persistence = StateStore.inMemoryPersistence ()
              persistence.Write "t1532.g.choice" "north"
              let store = StateStore.StateStoreInstance("t1532.g.", persistence)
              store.DeclarePersistent [ "choice" ]

              let mutable fired = 0

              store.SubscribeKeys(Set.ofList [ "choice" ], (fun () -> fired <- fired + 1))
              |> ignore

              // Never read, so never hydrated: the value exists only in storage.
              store.Remove "choice"

              Expect.equal fired 1 "the watchers are told, because the value a reader would have hydrated is gone"
              Expect.isNone (persistence.Read "t1532.g.choice") "and it really is gone"
          }

          test "Reset clears the persisted value and the declaration" {
              let store, persistence = freshStore "t1532.h."
              store.DeclarePersistent [ "mode" ]
              store.Set("mode", nn "cash")

              store.Reset()

              Expect.isNone
                  (persistence.Read "t1532.h.mode")
                  "reset clears storage too — otherwise the next case's first read hydrates this case's value"

              Expect.isFalse (store.IsPersistent "mode") "and the declaration goes with it"
          }

          test "Reset removes only declared keys, never a prefix sweep" {
              // The default store's prefix is a prefix of every scope's, so a
              // sweep from a `Reset` would take other scopes' values with it.
              let persistence = StateStore.inMemoryPersistence ()
              let store = StateStore.StateStoreInstance("fuaran.state.", persistence)
              store.DeclarePersistent [ "mode" ]
              store.Set("mode", nn "cash")
              persistence.Write "fuaran.state.scope-7.mode" "someone else's"

              store.Reset()

              Expect.isNone (persistence.Read "fuaran.state.mode") "its own declared key is cleared"

              Expect.equal
                  (persistence.Read "fuaran.state.scope-7.mode")
                  (Some "someone else's")
                  "a scoped store's value beneath the same prefix is untouched"
          }

          test "EvictPersisted empties this instance's prefix and no other" {
              let persistence = StateStore.inMemoryPersistence ()
              let store = StateStore.StateStoreInstance("t1532.scope-a.", persistence)
              store.DeclarePersistent [ "one"; "two" ]
              store.Set("one", nn "1")
              store.Set("two", nn "2")
              persistence.Write "t1532.scope-ab.one" "neighbour"

              store.EvictPersisted()

              Expect.isNone (persistence.Read "t1532.scope-a.one") "swept"
              Expect.isNone (persistence.Read "t1532.scope-a.two") "swept, including keys never removed one by one"

              Expect.equal
                  (persistence.Read "t1532.scope-ab.one")
                  (Some "neighbour")
                  "a prefix that merely starts the same is not swept — the separator is what keeps scopes disjoint"
          }

          test "a backing store that refuses the write neither throws nor loses the value" {
              let refusing =
                  { StateStore.inMemoryPersistence () with
                      Write = fun _ _ -> raise (InvalidOperationException "QuotaExceededError") }

              let store = StateStore.StateStoreInstance("t1532.i.", refusing)
              store.DeclarePersistent [ "mode" ]

              let mutable fired = 0

              store.SubscribeKeys(Set.ofList [ "mode" ], (fun () -> fired <- fired + 1))
              |> ignore

              // The assertion is the absence of an exception: the write reaches
              // this call site from inside the action interpreter, and a throw
              // there abandons the rest of the chain for a reason no part of
              // the tree can see or influence.
              store.Set("mode", nn "cash")

              Expect.equal
                  (store.Get "mode")
                  (Some(nn "cash"))
                  "the in-memory write — the one that was asked for — stands"

              Expect.equal fired 1 "and the subscribers are still told"
          }

          // Sequenced: redirects the *global* `Console.Error`, which is where
          // the renderer's last-resort diagnostic lands on the .NET leg. Same
          // shape as the telemetry suite's stdout cases.
          testSequenced (
              test "a refused write is reported on the diagnostic line" {
                  let refusing =
                      { StateStore.inMemoryPersistence () with
                          Write = fun _ _ -> raise (InvalidOperationException "QuotaExceededError") }

                  let store = StateStore.StateStoreInstance("t1532.j.", refusing)
                  store.DeclarePersistent [ "mode" ]

                  let originalError = Console.Error
                  use writer = new IO.StringWriter()
                  Console.SetError writer

                  try
                      store.Set("mode", nn "cash")
                  finally
                      Console.SetError originalError

                  let output = writer.ToString()

                  Expect.stringContains output "mode" "the diagnostic names the key that could not be persisted"

                  Expect.stringContains
                      output
                      "will not survive a reload"
                      "and says what was actually lost — the durability, not the write"
              }
          )

          // ── The scope registry ────────────────────────────────────────────

          testSequenced (
              test "disposeScope retires the instance rather than emptying it" {
                  StateStore.resetAllScopes ()
                  let first = StateStore.forScope "t1532-req-1"

                  Expect.isTrue
                      (Object.ReferenceEquals(first, StateStore.forScope "t1532-req-1"))
                      "the same id resolves the same instance while the scope is live"

                  StateStore.disposeScope "t1532-req-1"

                  Expect.isFalse
                      (Object.ReferenceEquals(first, StateStore.forScope "t1532-req-1"))
                      "after disposal the id mints a FRESH instance — the registry entry is gone, which is what an SSR host serving one scope per request needs and `resetScope` cannot give it"

                  StateStore.resetAllScopes ()
              }
          )

          testSequenced (
              test "disposeScope of an unknown scope is a no-op" {
                  StateStore.resetAllScopes ()
                  StateStore.disposeScope "t1532-never-created"
                  Expect.isTrue true "did not throw"
              }
          ) ]
