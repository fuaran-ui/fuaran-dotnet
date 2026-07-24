# Giraffe SSR sample

A minimal Giraffe / ASP.NET app that adopts Fuaran chrome **page-by-page** via
[`Fuaran.UI.Giraffe`](../../src/Fuaran.UI.Giraffe/) (Phase 162).

```powershell
dotnet run --project GiraffeSsr.Sample.fsproj
# → http://localhost:14020
```

| Route | Handler | What it shows |
|---|---|---|
| `/` | `fuaranPage` | A crawlable static-SSR document — no client runtime. |
| `/pricing` | `fuaranPage` | A second crawlable page; shares the in-memory render cache. |
| `/pricing/fragment` | `fuaranFragment` | The body fragment only (HTMX-style swap target). |
| `/app` | `fuaranHydratablePage` | The document + the Phase 143 hydrate payload. |
| `/islands` | `fuaranIslandsPage` | Static SSR with two hydration **islands** (Phase 163) — only the marked subtrees carry a hydrate `<script>`. |

Each page is a `Node<obj>` tree composed of Fuaran chrome plus one **host Custom
renderer** — an inline-SVG sparkline registered on the server Custom registry
(the consumer's domain content embedded via `NodeKind.Custom`). The document
shell (title / meta / canonical / Open Graph / stylesheet) is host-authored; the
wrapper plumbing is the library's.

## Verifying the ETag / cache

Every response carries a strong `ETag`. Re-request with `If-None-Match` to get a
`304`:

```powershell
$r = Invoke-WebRequest http://localhost:14020/pricing
Invoke-WebRequest http://localhost:14020/pricing -Headers @{ "If-None-Match" = $r.Headers.ETag }
# → 304 Not Modified, no body, no re-render
```

The render cache is wired to `RenderCache.inMemory ()`; a second request for the
same `(tree, theme, shell)` is served from the cache without re-rendering.
