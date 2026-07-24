# Fuaran zero-hydration resumability harness (Phase 177)

The measurement harness behind the resumability spike. It renders the **same**
Fuaran tree two ways and lets you compare load strategies directly:

- `resume.html` — `Resume.renderResumable`: inert HTML + a flat `nodeId → Action`
  envelope. The client (`output/Main.js`) installs **one** document-root listener
  and executes **zero framework JS at load**. `Navigate` buttons run directly
  from the envelope on first click (no chunk); the one `Dispatch` "boot" button
  lazily `import()`s an interactive React chunk.
- `hydrate.html` — `Hydration.renderHydratable` (the Phase 143 baseline): inert
  HTML + the full embedded wire tree. The client (`output/HydrateMain.js`)
  reconstructs the tree and attaches React with `hydrateRoot` at load.

## Layout

- `Tree.fs` — the page both pages build (shared by gen + client; generic over
  `'Msg`, the one Dispatch handler threaded via `onBoot`).
- `gen/` — a .NET console (`Gen.fs`) that server-renders the tree both ways and
  writes `resume.html` + `hydrate.html` from the `*.template.html` shells.
- `client/` — the Fable client: `Main.fs` (resume entry — interpreter + a minimal
  framework-free `IFuaranRuntime`), `HydrateMain.fs` (baseline entry), `Boot.fs`
  (the lazy React chunk, dynamically imported on first `Dispatch`).

## Run it

```powershell
npm install
dotnet run --project gen/Gen.fsproj             # server-render → resume.html + hydrate.html
dotnet fable client/Client.fsproj -o output     # F# → output/*.js
npm run dev                                      # vite on http://localhost:24051
```

Open `http://localhost:24051/resume.html` and `.../hydrate.html`. The resume page
is fully visible with zero framework JS executed; click *Boot the interactive
island* and react/Feliz load **only then** (visible in the Network panel). The
measured numbers are recorded in [`docs/RESUMABILITY-SPIKE.md`](../../docs/RESUMABILITY-SPIKE.md).

`resume.html` / `hydrate.html` are generated (git-ignored); edit the
`*.template.html` shells instead.
