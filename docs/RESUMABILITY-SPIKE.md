# Resumability spike – measured verdict (Phase 177)

> **Status:** Spike shipped 2026-06-14 (throwaway-grade, decision-gating). This
> records what the spike built, what it measured against the Phase 143 hydrate
> baseline, and the verdict. The design rationale is
> [`RESUMABILITY-EXPLORATION.md`](RESUMABILITY-EXPLORATION.md); this is the §7
> "decide, don't build" follow-through.

## What shipped (spike artefacts)

| Artefact | Where | Role |
|---|---|---|
| Resume-envelope serialiser | [`Fuaran.UI.Renderer.Server/Resume.fs`](../src/Fuaran.UI.Renderer.Server/Resume.fs) | Emits inert HTML + a flat `nodeId → { action, disposition }` envelope + model + tree-hash + classified init effects. `renderResumable` is the resumable twin of `Hydration.renderHydratable`. |
| Client interpreter runtime | [`Fuaran.UI.Renderer/Resume.fs`](../src/Fuaran.UI.Renderer/Resume.fs) | One document-root delegated listener (`click` + `submit`), envelope lookup, direct interpretation of data-shaped `Action`s against `IFuaranRuntime`, `Dispatch`→lazy-boot handoff, `Call`/`ReadFileBody`→per-subtree hydration handoff, tree-hash mismatch→client-render fallback. Fable-portable; no Feliz/React dependency. |
| Init-effect classifier | `Resume.classifyInitEffect` | `skip` (SSR-resolved) / `eager` (pre-interaction subscription) / `deferred` (island-bound) / `lazy` (everything else). |
| Disposition classifier | `Resume.disposition` | Per-node: `interpret` / `boot` / `fallback`, strictest member wins in a `Chain`. |
| Tests | [`ResumeTests.fs`](../src/Fuaran.UI.Renderer.Server.Tests/ResumeTests.fs) | 9 tests: envelope shape + valid-JSON, action map + dispositions, both classifiers, tree-hash determinism, script-injection escaping, hard-case fallback, transfer-size win. |

## Measurement

Two complementary measurements: a unit-level transfer-size check (`ResumeTests.fs`)
and an **end-to-end browser harness** ([`samples/resume`](../samples/resume/),
added 2026-06-14) that renders the *same* page both ways and drives a real Vite +
Fable + React load with the preview tooling.

### A. Envelope vs full hydrate wire tree (unit)

On a dashboard of 12 `Navigate` buttons, the flat resume envelope is **1143 B vs
2545 B** for the full embedded wire tree – **0.45×**. The envelope ships only
event-bearing nodes; hydrate embeds the whole tree.

### B. End-to-end browser harness ([`samples/resume`](../samples/resume/))

Same tree (a Navigate-dominated page + one Dispatch "boot" node), rendered to
`resume.html` (`Resume.renderResumable`) and `hydrate.html`
(`Hydration.renderHydratable`). Measured on the Vite dev server over localhost
(see caveats):

| Metric | Resume | Hydrate (Phase 143) |
|---|---|---|
| **Framework JS requested at load** (react + react-dom + Feliz + `Render`) | **0 KB** – never requested | **~1.19 MB** (unminified dev) |
| Scripts loaded at load | 30 (Fable runtime + interpreter only) | 62 (adds the 4 react chunks + Feliz + `Render`) |
| Resume-entry self-eval | **0.4–0.7 ms** | n/a (full hydrate mount) |
| DOMContentLoaded | ~204 ms | ~263 ms |
| First-interaction latency – **interpret** path (`Navigate`) | **≈ 0 ms** (sub-ms; runs from the envelope, no chunk) | n/a (already hydrated) |
| First-interaction latency – **boot** path (`Dispatch`) | **26.3 ms** (lazy `Boot.js` + react + react-dom + Feliz + React mount, cold) | n/a |
| When the framework loads | **only after the first `Dispatch` click** (react/Feliz appear in the resource list *after* the click, never before) | at load |

The static import graph confirms the runtime numbers: the resume entry's reachable
module set is `Main → Resume → Types → fable-library` – it contains **no** react,
Feliz, or `Render` import. The hydrate entry pulls `Render → react`. So the
**executed-framework-JS-at-load ≈ 0** property is *structural* (the interpreter is
data-driven, not view-driven), not a tuning result, and the framework-bytes = 0
finding is independent of caching/minification – the resources are simply never
requested until first interaction.

**Caveats (honest):** the harness ran on the Vite **dev** server (unminified
modules, Vite-pre-bundled deps) over **localhost** (no network latency), single
run. Absolute KB and the boot-path 26.3 ms would shift on a production build over
a real network – the boot path's lazy chunk is the variable. But the *contrast*
(0 vs ~1.19 MB framework; ≈ 0 vs 26 ms first interaction) is the load-strategy
difference, and it holds by construction.

## Hard cases (§5) – confirmed bounded

Each non-serialisable case is named in the type system and degrades cleanly to
hydration **for that subtree only**, never a broken page:

- `Action.Dispatch` – opaque `'Msg` on the wire → `boot` disposition; first
  interaction lazy-loads the module chunk (host-supplied `BootSubtree`).
- `Action.Call` / `Action.ReadFileBody` – obj-erased continuation → `fallback`
  disposition; host hydrates that subtree (`HydrateSubtree`).
- Resume-mismatch (DOM mutated after render) → tree-hash disagreement →
  `OnMismatch` full client render.
- `Binding.Computed` / `Custom` subtrees, controlled inputs – out of the spike's
  click/submit scope; the exploration doc's eager-island / uncontrolled-until-boot
  resolutions stand as the production path.

## Verdict

**Resumability is worth shipping as a per-surface-class load strategy, not as a
blanket default.** The evidence:

1. The `Action`-as-data shape makes the envelope cheap and the interpreter tiny
   and Fable-clean (it consumes only the public typed surface + `IFuaranRuntime`,
   no Feliz) – the FGP-2 / FGP-5 contracts hold, and op-stream/telemetry emission
   is identical because data-shaped actions route through the same runtime the
   hydrated `runAction` uses.
2. The win is largest on **content-dense, low-interaction surfaces** (marketing /
   docs / SEO landing pages, on-prem read-mostly dashboards): mostly `Navigate`
   and static content, where ≈ 0 executed JS at load is pure upside and almost
   nothing ever boots.
3. The win is **marginal-to-negative on interaction-dense app shells** (forms,
   live-updating dashboards): most nodes are `Dispatch` and boot on first touch
   anyway, so resume mostly adds a first-interaction-latency tax over hydrate.

**Recommended disposition (registered per the Phase 161 gated-backlog discipline):**

- **Default-on resume** for the `Display`/`Navigate`-dominated surface class
  (the "page", not the "app").
- **Islands-hydrate fallback** as the default for `Dispatch`-dominated app
  shells, with resume available opt-in per island.
- **Eager-island threshold:** a subtree whose event-bearing nodes are >50%
  `boot`/`fallback` disposition is better hydrated than resumed – emit it as an
  eager island rather than paying the per-node boot tax.
- **First-interaction latency** – the harness now gives a real number: the
  **interpret** path (the static surface class the verdict defaults-on) is **≈ 0 ms**
  (no chunk), and the **boot** path is **26.3 ms** cold on localhost dev. That is
  already near-imperceptible, but the production-network + warm-CDN number for the
  boot path is still future work; pre-warm-on-`pointerover` is the lever if it
  regresses. Default-on therefore ships for the static surface class now (where
  the measured latency is ≈ 0), with the boot-dominated surface staying on
  islands-hydrate pending a production-network measurement.

This is a "where", not a "whether" – the on-prem/SEO value is real for the static
surface class regardless of how the latency question resolves.
