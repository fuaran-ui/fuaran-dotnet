# Fuaran starter — F# (Fable + Elmish + Vite)

The 30-second on-ramp: author a typed Fuaran tree in F#, render it through the
language-tier renderer, run the Elmish dispatch loop.

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
| [`Main.fs`](Main.fs) | The whole app — `Model`/`Msg`/`update` (MVU core), the authored `tree`, `buildSources`, and the Elmish boot via `Render.renderWithSources` |
| [`Starter.fsproj`](Starter.fsproj) | `Fuaran.UI.*` package refs, `Nullable disable`, `FABLE_COMPILER` define |
| [`index.html`](index.html) | The mount root (`#fuaran-app-root`) + the `output/Main.js` script |
| [`vite.config.mts`](vite.config.mts) | Bundles the Fable-transpiled `output/*.js`; ignores raw `.fs` (the Fable watcher handles those) |

## The loop

`Model` → `tree` projects a typed `Node` tree → `Render.renderWithSources`
renders it → a click emits a `Msg` via `dispatch` → `update` folds it into the
next `Model` → Elmish re-renders. `Render.renderWithSources` supplies a default
diagnostic runtime + the no-op visualisation adapter, so a starter needs no
host-runtime wiring — reach for the full `Render.render ctx tree` once you wire a
real `IFuaranRuntime`, a visualisation adapter (Grid/Chart), or a telemetry sink.

## Project facts (Fable)

- **`<Nullable>disable</Nullable>`** — Fable.Elmish + Feliz pre-date F# 10
  nullness; the checker is off here (the `Fuaran.UI` typed-tree library keeps it
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

## Using this outside the Fuaran workspace

The template restores the `Fuaran.UI.*` packs from the workspace-local feed
(`../../../local-nuget-feed` in [`nuget.config`](nuget.config)). When you copy it
out (degit / `create-fuaran-app`), replace that `local` source with the published
GitHub Packages feed, `https://nuget.pkg.github.com/fuaran-ui/index.json` (a PAT
with `read:packages` is required), once the packages are published.
