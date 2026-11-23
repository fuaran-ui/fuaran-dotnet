# Fuaran starter — F# (Fable + Fuaran.MVU + Vite)

The 30-second on-ramp: author a typed Fuaran tree in F#, render it through the
language-tier renderer, run it on the `Fuaran.MVU` runtime.

```powershell
npm install
dotnet tool restore        # restores the Fable compiler
dotnet fable -o output     # F# -> JS (re-run, or add --watch, on every .fs edit)
npm run dev                # http://localhost:24001
```

Or `npm run build` (transpile + Vite bundle) for a production build. You should
see a "Hello, Fuaran" heading, a Counter metric, and two buttons that move it —
the typed `Node` tree in [`Main.fs`](Main.fs), no hand-written Feliz.

## How it's wired

| File | Role |
|---|---|
| [`Main.fs`](Main.fs) | The whole app — `Model`/`Msg`/`update` (MVU core), the authored `tree`, `buildSources`, the view via `Render.renderWithSources`, and the mount of the component |
| [`Starter.fsproj`](Starter.fsproj) | `Fuaran.UI.*` and `Fuaran.MVU.*` package refs, `Nullable disable`, `FABLE_COMPILER` define |
| [`index.html`](index.html) | The mount root (`#fuaran-app-root`) + the `output/Main.js` script |
| [`vite.config.mts`](vite.config.mts) | Bundles the Fable-transpiled `output/*.js`; ignores raw `.fs` (the Fable watcher handles those) |

## The loop

`Model` → `tree` projects a typed `Node` tree → `Render.renderWithSources`
renders it → a click emits a `Msg` via `dispatch` → `update` folds it into the
next `Model` and the steps it asks for → the runtime renders again.
`Render.renderWithSources` supplies a default diagnostic runtime + the no-op
visualisation adapter, so a starter needs no host-runtime wiring — reach for the
full `Render.render ctx tree` once you wire a real `IFuaranRuntime`, a
visualisation adapter (Grid/Chart), or a telemetry sink.

## The runtime: Fuaran.MVU

The program is a component (`Component.plain init update resources view`): four
pure functions. `update` returns the next model and a list of steps, each an
effect to perform (`Do`) or a follow-up message (`Then`); this starter asks for
none, so its effect type is `NoEffect` and its steps are `[]`. The view returns
a React element, which `ElementView.ofView` runs on the runtime, and
`Live.elements` from `Fuaran.MVU.React` mounts the component at
`#fuaran-app-root` with the browser host from `Fuaran.MVU.Browser`.

When the app needs the browser (clipboard, navigation, history, focus, download,
file reads, print, confirm, storage, timers), make
`Fuaran.MVU.Browser.StandardEffect` a case of your effect type and build the
interpreter with `StandardInterpreter.embed`: each of those is a standard effect
with its classes registered, so a gate can decide it.

The template takes `Fuaran.MVU` **0.1.0** (with `Fuaran.MVU.React` and
`Fuaran.MVU.Browser` 0.1.0) on `Fuaran.UI` **0.106.0**, the first `Fuaran.UI`
release built on the `Fuaran.Core` line that `Fuaran.MVU` needs; it is the
runtime's first public user. The runtime's earlier caveat, that the public
starter stay on Elmish until the runtime had a release history, was revised:
the starter moves now (Phase 721).

## Project facts (Fable)

- **`<Nullable>disable</Nullable>`** — Feliz, which the renderer draws with,
  pre-dates F# 10 nullness; the checker is off here (the `Fuaran.UI` typed-tree library keeps it
  on separately).
- **`FABLE_COMPILER` define** — gates Fable-only code (Browser/DOM APIs) so it
  compiles under Fable but a plain `dotnet build` doesn't try to.
- **`NumberStyles` / `IFormatProvider` caveat** — Fable's culture support is
  partial. Prefer the renderer's `format.*` helpers (`format.currency`,
  `format.percent`, `format.number`) for numeric display rather than calling
  `Double.Parse` / `.ToString(provider)` directly in transpiled code; a
  `NumberStyles`/`IFormatProvider` overload that works on .NET can no-op or throw
  under Fable.

## Next steps

1. **Add a node** — author another `Fuaran.*` node in `tree` (a `Fuaran.button`,
   `Fuaran.grid`, `Fuaran.form`, …). If it reads state, add the key to
   `buildSources`; if it emits, add the case to `Msg`.
2. **Register a query** — back `binding.query key accessor` with live data by
   adding a `QueryResults` entry in `buildSources`.
3. **Register a custom renderer** — wire an `IFuaranRuntime` via the full
   `Render.render ctx tree` path and `RegisterCustomRenderer` for the bounded
   `NodeKind.Custom` escape hatch.

## Packages

The template restores from nuget.org only ([`nuget.config`](nuget.config) clears
every other source), so a copy anywhere builds with no extra feed. Two
properties in [`Starter.fsproj`](Starter.fsproj) pin the packages:
`FuaranUIVersion` every `Fuaran.UI.*` package and `FuaranMVUVersion` every
`Fuaran.MVU.*` one; raise them to move onto a newer release.
