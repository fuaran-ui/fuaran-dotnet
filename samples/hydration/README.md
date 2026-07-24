# Fuaran isomorphic-hydration sample (Phase 143)

A minimal, neutral-domain demonstration of **isomorphic hydration**: a Fuaran
tree is **server-rendered** to HTML on plain .NET (`Fuaran.UI.Renderer.Server`),
then **hydrated** in the browser by React (`Fuaran.UI.Renderer` via
`hydrateRoot`) — one canonical tree, both pipelines, no flash and no
re-render.

Model **b**: the client reconstructs the *same* tree in F# (`Tree.build`, shared
by both sides) and attaches React with `hydrateRoot` over the server DOM — **no
in-browser wire-decode**. A tiny Elmish loop then drives interactivity: clicking
the *Details* tab dispatches through `Hydration.render` (React reuses the
hydrated root).

## Layout

- `Tree.fs` — the chrome tree both pipelines build (generic over `'Msg`; the one
  interactive surface, a Tabs node, is threaded via `activeTab` + `onTab`).
- `gen/` — a .NET console (`Gen.fs`) that server-renders the tree and writes
  `index.html` from `index.template.html` (replacing the `<!--SSR-->` marker).
- `client/` — the Fable client (`Main.fs`): reconstructs the tree, hydrates, and
  runs the Elmish-over-hydration loop.

## Run it

```powershell
npm install
dotnet run --project gen/Gen.fsproj        # server-render -> index.html
dotnet fable client/Client.fsproj -o output # F# -> output/Main.js
npm run dev                                 # vite dev server on http://localhost:24050
```

Open `http://localhost:24050`. The page is fully visible before any JS runs
(server-rendered); React then hydrates it with **zero hydration-mismatch
warnings** (verified — the server and client renderers are class+ARIA
parity-locked, Phase 142). Click *Details* to switch tabs — interactivity is
live after hydration.

`index.html` is generated (git-ignored); edit `index.template.html` instead.
