# Fuaran.UI.ThemeManifest

A machine-readable **theme token contract** the AI can *reason against* and the [`Fuaran.UI.StyleObserver`](https://www.nuget.org/packages/Fuaran.UI.StyleObserver) can *verify against*.

The W3C Design Tokens (DTCG) format carries token values + a free-form `$description` — and nothing else: no controlled role vocabulary, no constraints, no verification surface. `ThemeManifest` is **DTCG-compatible** (a vanilla DTCG file decodes cleanly — it is not NIH) **extended** with the two things DTCG lacks:

1. **Role → `ToneVariant` mapping** (`RoleBinding`) — so `Tone.Brand` is *known* to resolve to the manifest's brand token. The dual-field token shape (a quantified value + a semantic tag) borrows SpecifyUI's SPEC representation (`arXiv:2509.07334`).
2. **An invariant block** (`Invariant list`) — contrast floors, colour-usage budgets, motion voice — **soft-weighted** per Draco (`idl.uw.edu/draco`): each invariant carries a `Weight` so violations rank by importance rather than being equally fatal (weight *learning* is a later phase; the slot ships now).

The `UsageBudget` invariant formalises the established **60-30-10 rule** (`blog.logrocket.com/ux-design/60-30-10-rule`) as a per-token `targetPct ± tolerancePct`, rather than inventing a new metric — the check substrate the StyleObserver's area-weighted colour-distribution pass resolves against.

## Shape

```
ThemeManifest = { Meta; Tokens: ManifestToken list; Roles: RoleBinding list; Invariants: Invariant list }
```

- `ManifestToken` — DTCG `$type`/`$value`/`$description` round-trip + the SPEC `Role` tag (carried under `$extensions.fuaran.role`). `Name` is the dotted path flattened from the DTCG group tree (`"color.brand.base"`).
- `InvariantKind` — `ContrastFloor of role * minRatio` / `UsageBudget of token * targetPct * tolerancePct` / `MotionVoice of MotionBudget`. **Additive-only post-ship** (parallel to `StyleFlag` / `LayoutFlag`).
- `ThemeManifest.resolveRole : ToneVariant -> ThemeManifest -> ManifestToken option` — the lookup the StyleObserver consumes; `resolveNamedRole` is the named-role counterpart.

The manifest is a **host/theme artefact, not part of the `Node` tree** — it travels *alongside* the tree, never inside it, preserving the "semantic, not CSS" tree posture. `FSharp.Core`-only and Fable-portable (a small dependency-free JSON parser ships in the package, so `encode`/`decode` run byte-identically under Fable and .NET — no `System.Text.Json`, no external JSON dependency).

A JSON schema (`theme-manifest.schema.json`) and a neutral example theme (`example-theme.manifest.json`) ship in the package as the machine-readable contract + the canonical fixture.

## Adopting an existing theme (Phase 149)

You don't have to hand-author a manifest. The `Project` module ingests an app's *existing* token surface into a baseline manifest you then enrich with invariants:

- **`Project.projectFromFuaranToneVars css`** — the renderer's semantic `--fuaran-tone-{tone}-{bg,fg,border}` set. Role inference is **direct** (the contract is already semantic): each tone's `bg` slot is bound to its `ToneVariant`, so `resolveRole Tone.Brand` works with no hand-mapping.
- **`Project.projectFromCssCustomProperties css`** — a generic `:root` (plus an optional `:root[data-theme=dark]`) custom-property block. Bespoke var names carry no inferable role, so **roles are left unbound** for you to map. Light/dark pairs are preserved: the light value keeps the bare name, the dark counterpart is emitted as `{name}@dark`.
- **`Project.projectFromDtcg json`** — a DTCG / `tokens.json` file. Values (and any explicit `$extensions.fuaran.role` tags) round-trip losslessly; grouping is **not** mined for bindings (DTCG's "don't infer purpose from grouping" caveat), so nothing is over-claimed.
- **`Project.merge baseM over`** — layer an app override onto a shell token set with last-write-wins precedence matching the CSS cascade (e.g. a shell `--color-*` set + a rebound `--fuaran-tone-*` set).

**What you add by hand:** the projectors produce *tokens + inferable role bindings only*. A thin projected manifest (tokens, no invariants) already drives the contrast flags; **usage-budget enforcement and per-role contrast floors require you to add the `Invariant`s** (`ContrastFloor` / `UsageBudget` / `MotionVoice`) — opt-in by manifest richness.

Apache-2.0 licensed — see the repo [LICENSE](../../LICENSE).
