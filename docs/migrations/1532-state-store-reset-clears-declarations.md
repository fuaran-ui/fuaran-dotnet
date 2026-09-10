# Migration — `StateStore.reset ()` now clears host DECLARATIONS too (Phase 1532)

_Written in Phase 1648 against a change Phase 1532 shipped. The behaviour was documented at the call
site and nowhere a host upgrading would look, which is the gap this page closes. Nothing about the
behaviour changes here._

## Symptom

You upgraded `Fuaran.UI.Renderer`, your host calls `StateStore.reset ()` at runtime, and **keys you
declared persistent stop persisting**. Values still round-trip within the session; they no longer
survive a reload. Nothing warns, and nothing is broken in a way a test that never reloads can see.

## What changed

Phase 1532 narrowed persistence: a `Binding.State` value reaches `localStorage` **only** for a key
the host declared persistent (`StateStore.declarePersistent`). An undeclared key lives in memory for
the session, which is the default and covers most of them.

`reset ()` was already the test-isolation seam — the default store is a process-global singleton, so
a .NET test runner sharing one process must clear it between cases that assert on store contents or
notification counts. With declarations in the store, "clear the store" now also clears **the
declaration itself**, and the persisted value of every declared key with it. That is right for the
seam's purpose: a test that reset and then found the previous case's declarations still standing
would be sharing exactly the state the reset exists to remove.

It is a trap for a browser host that calls `reset ()` at RUNTIME — to clear a session, say, or on
sign-out — because the declarations are usually made once at startup and never again.

## The fix

Re-declare after any runtime reset:

```fsharp
StateStore.reset ()
StateStore.declarePersistent [ "theme"; "sidebarCollapsed" ]   // whatever your startup declared
```

Or, better, hoist the declaration into a function your startup and your reset path both call, so the
two cannot drift:

```fsharp
let declareHostKeys () =
    StateStore.declarePersistent [ "theme"; "sidebarCollapsed" ]

// startup
declareHostKeys ()

// sign-out
StateStore.reset ()
declareHostKeys ()
```

## What is NOT affected

- **Scoped stores.** `reset ()` touches the default instance only. Use `resetScope` /
  `disposeScope` / `resetAllScopes` for instances created with `forScope`, and note that
  `disposeScope` additionally drops the scope from the registry.
- **Tests.** This is the seam's intended behaviour; a suite that resets between cases wants the
  declarations gone.
- **A host that never calls `reset ()`.** Which is most of them: it is not part of the steady-state
  browser path.
