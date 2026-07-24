# Fuaran theme-bridge guide

Most hosts that consume `Fuaran.UI.Renderer` already own a design system – Tailwind utilities with CSS-variable extensions, shadcn's `--primary` / `--muted` set, MUI's `--mui-palette-*` palette, raw CSS tokens authored in-house. This guide shows how to bridge those tokens through to Fuaran's `--fuaran-tone-*` variable surface so the renderer picks up the host's brand without forking the reference stylesheet.

See [`HOST-STYLING-CHECKLIST.md`](../HOST-STYLING-CHECKLIST.md) for the full enumeration of variables and class hooks the renderer emits, and [`src/Fuaran.UI.Renderer/content/fuaran-reference.css`](../src/Fuaran.UI.Renderer/content/fuaran-reference.css) for the reference values the bridge re-binds.

## Mechanical adoption (recommended): project → emit → verify

You no longer have to hand-author the bridge. The `Fuaran.UI.ThemeManifest` package closes the loop: **project** your existing token surface into a theme manifest, **emit** the bridge stylesheet from it, and **verify** the emitted values against declared contrast floors – no hand-written mapping file, and a coverage report that tells you exactly what the bridge did.

The hand-authored worked examples further down (A–D) remain valid and are the right reference for understanding the output and for one-off tweaks, but the three-step mechanical path is the recommended way to adopt Fuaran chrome page-by-page inside an existing product.

### Step 1 – Project your tokens into a manifest

Pick the projector that matches your existing token surface (see the [`Fuaran.UI.ThemeManifest` README](../src/Fuaran.UI.ThemeManifest/README.md) for all four):

```fsharp
open Fuaran.UI.Types
open Fuaran.UI.ThemeManifest

// A raw `:root { --color-*: … }` block — the in-house-design-system shape.
let projected =
    Project.projectFromCssCustomProperties (System.IO.File.ReadAllText "tokens.css")
```

`projectFromCssCustomProperties` ingests the values but leaves roles **unbound** (bespoke var names carry no inferable semantic role). Add the role bindings – the one place an operator's judgement is required – mapping each Fuaran tone / named role to the host token that should drive it:

```fsharp
let manifest =
    { projected with
        Roles =
            [ { Role = ManifestRole.Tone ToneVariant.Brand;    TokenName = "color-brand" }
              { Role = ManifestRole.Tone ToneVariant.Success;  TokenName = "color-success" }
              { Role = ManifestRole.Tone ToneVariant.Critical; TokenName = "color-danger" }
              { Role = ManifestRole.Named "page-surface";      TokenName = "color-surface" }
              { Role = ManifestRole.Named "body-text";         TokenName = "color-text" } ] }
```

(If your tokens are already Fuaran-shaped – a `--fuaran-tone-*` set – `Project.projectFromFuaranToneVars` infers the role bindings directly and you can skip the hand-binding step entirely. A DTCG `tokens.json` projects via `Project.projectFromDtcg`.)

The recognised named-role names the emitter bridges are `page-surface` / `surface` → the Default tone background, `body-text` / `text` → the Default tone foreground, `border` / `divider` → the Default tone border, and `muted` / `muted-surface` / `muted-text` → the Subdued tone slots. A tone role binds its host token to the tone's **foreground** slot (the accent colour); the bg / border slots fall back to the reference defaults unless you bind them too (the coverage report names every fallback).

### Step 2 – Emit the bridge stylesheet

```fsharp
let result = ThemeBridge.emitCss manifest ThemeBridge.BridgeOptions.defaults
System.IO.File.WriteAllText ("fuaran-bridge.css", result.Css)
```

`result.Css` is the bridge – each bound contract variable written as a `var()` reference to your token, so your stylesheet stays the single source of truth and your dark-mode / theming variants flow through automatically:

```css
/* Fuaran host-styling bridge — generated from a ThemeManifest (Phase 165).
   Load AFTER fuaran-reference.css so this :root block overrides the defaults.
   Unmapped contract variables inherit their reference defaults via the cascade. */
:root {
  --fuaran-tone-brand-fg: var(--color-brand);
  --fuaran-tone-success-fg: var(--color-success);
  --fuaran-tone-critical-fg: var(--color-danger);
  --fuaran-tone-default-bg: var(--color-surface);
  --fuaran-tone-default-fg: var(--color-text);
}
```

`BridgeOptions` controls the output:

- **`Scope`** – the selector the block is written under: `":root"` (default) or a container selector to scope the re-brand to one region of the page.
- **`Mode`** – `EmitMode.Reference` (default; `var()` references, host stays canonical) or `EmitMode.Literal` (copy the resolved value in – useful for snapshotting).
- **`Families`** – which 12.H contract families to emit (`Tones` / `Fonts` / `Spacing` / `Motion`). `BridgeOptions.tonesOnly` is the common brownfield case (you own brand colours but not Fuaran's spacing / type scale).

The **coverage report** (`result.Coverage`) tells you exactly what happened – which contract variables mapped to a host token, which fell back to a reference default, and which host tokens were ingested but never bound to anything:

```fsharp
printfn "%s" (ThemeBridge.CoverageReport.toConsole result.Coverage)
// Or, for a CI artefact:
System.IO.File.WriteAllText ("bridge-coverage.md", ThemeBridge.CoverageReport.toMarkdown result.Coverage)
```

A fallback line names the reference default the un-bridged surface will render as, so a partial adoption (brand colours bridged, status colours not yet) is a deliberate, visible choice rather than a silent gap.

### Step 3 – Verify the emitted values against your contrast floors

Add the contrast floors (and any usage budgets) you care about to the manifest's `Invariants`, then verify – deterministically, no browser needed:

```fsharp
let toVerify =
    { manifest with
        Invariants = [ Invariant.create (InvariantKind.ContrastFloor ("body-text", 7.0)) ] }

let bridge, verification = ThemeBridge.emitAndVerify toVerify ThemeBridge.BridgeOptions.defaults

match ThemeBridge.VerificationResult.violations verification with
| [] -> printfn "Bridge clears every declared contrast floor."
| breaches ->
    for b in breaches do
        printfn "FLOOR BREACH: %s — %A on %A is %.2f:1, floor %.1f:1"
            b.Role b.Foreground b.Background (Option.defaultValue 0.0 b.Ratio) b.Floor
    exit 1
```

A breached floor is a build-time finding – the violating role and the exact foreground / background values are in the result – not a production surprise. Contrast floors resolve deterministically from the manifest's token values; **usage-budget and motion invariants are reported as observer-assisted** (`verification.Deferred`) because they need rendered area / a runtime to settle – wire them through the resolved-style observer for the full check.

That's the whole loop. Re-run it whenever your tokens or role bindings change; the bridge stays a generated artefact, never a hand-maintained one.

## How the bridge works

Fuaran's renderer emits one CSS class per tone-bearing component (`fuaran-callout-brand`, `fuaran-progress-success`, `fuaran-metric-critical`, etc.). The reference stylesheet styles each against a `var(--fuaran-tone-{tone}-{slot}, fallback)` lookup. A *bridge* file re-binds those `--fuaran-tone-*` variables at the `:root` layer to the host's own tokens – the cascade picks the bridge's binding over the reference's, and Fuaran renders against the host's colours with zero changes to the reference CSS.

```css
:root {
  /* Re-bind Fuaran's tone surface to your existing tokens. */
  --fuaran-tone-brand-fg: var(--color-brand);
  --fuaran-tone-brand-bg: color-mix(in srgb, var(--color-brand) 12%, white);
  /* ... */
}
```

The load order matters: the bridge file must load AFTER `fuaran-reference.css` so its `:root` block overrides. The standard pattern is:

```html
<link rel="stylesheet" href="/fuaran-reference.css" />
<link rel="stylesheet" href="/fuaran-bridge.css" />
```

`src/Fuaran.UI.Renderer/content/fuaran-bridge.css.template` is a copy-and-customise starting point with every tone × slot stub present and commented; consumers rename the template to `fuaran-bridge.css` and fill in the right-hand-side expressions.

The four worked examples below cover the common host shapes. They are the **manual** alternative to the mechanical project → emit → verify path above – read them to understand the output shape, to hand-tweak a generated bridge, or when you'd rather author the `:root` block directly than project a manifest.

## Example A – Tailwind with CSS-variable extension

Tailwind v3+ supports CSS variables in the theme config. A host with the standard "brand colour as a CSS variable, Tailwind utilities reference it" setup:

```js
// tailwind.config.js
export default {
  theme: {
    extend: {
      colors: {
        brand: 'rgb(var(--color-brand-rgb) / <alpha-value>)',
        // ...
      },
    },
  },
};
```

```css
/* index.css */
:root {
  --color-brand-rgb: 29 78 216;        /* #1d4ed8 */
  --color-success-rgb: 4 120 87;       /* #047857 */
  --color-critical-rgb: 185 28 28;     /* #b91c1c */
  --color-warning-rgb: 180 83 9;       /* #b45309 */
}
```

Bridge to Fuaran:

```css
/* fuaran-bridge.css — loaded AFTER fuaran-reference.css */
:root {
  /* Brand */
  --fuaran-tone-brand-fg: rgb(var(--color-brand-rgb));
  --fuaran-tone-brand-bg: rgb(var(--color-brand-rgb) / 0.08);
  --fuaran-tone-brand-border: rgb(var(--color-brand-rgb) / 0.4);

  /* Success */
  --fuaran-tone-success-fg: rgb(var(--color-success-rgb));
  --fuaran-tone-success-bg: rgb(var(--color-success-rgb) / 0.08);
  --fuaran-tone-success-border: rgb(var(--color-success-rgb) / 0.4);

  /* Critical */
  --fuaran-tone-critical-fg: rgb(var(--color-critical-rgb));
  --fuaran-tone-critical-bg: rgb(var(--color-critical-rgb) / 0.08);
  --fuaran-tone-critical-border: rgb(var(--color-critical-rgb) / 0.4);

  /* Warning */
  --fuaran-tone-warning-fg: rgb(var(--color-warning-rgb));
  --fuaran-tone-warning-bg: rgb(var(--color-warning-rgb) / 0.12);
  --fuaran-tone-warning-border: rgb(var(--color-warning-rgb) / 0.4);

  /* Info, Default, Subdued — pin to neutrals; or copy Brand pattern. */
  --fuaran-tone-info-fg: rgb(var(--color-brand-rgb));
  --fuaran-tone-info-bg: rgb(var(--color-brand-rgb) / 0.06);
  --fuaran-tone-info-border: rgb(var(--color-brand-rgb) / 0.3);
}
```

Pick whatever bg/border opacities match your existing component styling. The `rgb(...)` + `<alpha-value>` shape is the same one Tailwind uses, so the resulting Fuaran surfaces visually match the host's components rendered with `bg-brand/8 text-brand`-style utilities.

## Example B – shadcn / Radix tokens

shadcn's stock theme exposes `--primary`, `--secondary`, `--accent`, `--destructive`, `--muted`, etc., usually as `hsl(...)`-shaped values:

```css
/* shadcn's stock globals.css */
:root {
  --primary: 222.2 47.4% 11.2%;
  --primary-foreground: 210 40% 98%;
  --secondary: 210 40% 96.1%;
  --secondary-foreground: 222.2 47.4% 11.2%;
  --destructive: 0 84.2% 60.2%;
  --destructive-foreground: 210 40% 98%;
  --muted: 210 40% 96.1%;
  --muted-foreground: 215.4 16.3% 46.9%;
  --border: 214.3 31.8% 91.4%;
  /* ... */
}
```

Bridge to Fuaran:

```css
/* fuaran-bridge.css — loaded AFTER fuaran-reference.css */
:root {
  /* Brand maps to shadcn's primary */
  --fuaran-tone-brand-fg: hsl(var(--primary));
  --fuaran-tone-brand-bg: hsl(var(--primary) / 0.08);
  --fuaran-tone-brand-border: hsl(var(--primary) / 0.4);

  /* Critical maps to shadcn's destructive */
  --fuaran-tone-critical-fg: hsl(var(--destructive));
  --fuaran-tone-critical-bg: hsl(var(--destructive) / 0.1);
  --fuaran-tone-critical-border: hsl(var(--destructive) / 0.4);

  /* Subdued maps to shadcn's muted */
  --fuaran-tone-subdued-fg: hsl(var(--muted-foreground));
  --fuaran-tone-subdued-bg: hsl(var(--muted));
  --fuaran-tone-subdued-border: hsl(var(--border));

  /* Default maps to shadcn's foreground / background pair */
  --fuaran-tone-default-fg: hsl(var(--foreground));
  --fuaran-tone-default-bg: hsl(var(--background));
  --fuaran-tone-default-border: hsl(var(--border));

  /* shadcn has no first-class success / warning / info — pick a sensible
     near-match or reach into Radix' `radix-ui/colors` package, e.g.
     `var(--green-11)` / `var(--amber-11)` / `var(--blue-11)`. */
}
```

shadcn ships no built-in `--success` / `--warning` / `--info` tokens (their stock theme is greyscale + destructive only); the most common path is to add them as siblings of `--destructive` in your own globals.css and bridge straight through, or pull from `@radix-ui/colors`.

## Example C – Raw CSS tokens, no framework

A host with hand-authored design tokens – common for in-house design systems or marketing-site stacks without Tailwind/shadcn:

```css
/* tokens.css */
:root {
  --color-brand: #2563eb;
  --color-brand-soft: #dbeafe;
  --color-brand-border: #93c5fd;

  --color-success: #16a34a;
  --color-success-soft: #dcfce7;
  --color-success-border: #86efac;

  --color-danger: #dc2626;
  --color-danger-soft: #fee2e2;
  --color-danger-border: #fca5a5;

  --color-warning: #ca8a04;
  --color-warning-soft: #fef9c3;
  --color-warning-border: #fde047;

  --color-text: #0f172a;
  --color-text-muted: #64748b;
  --color-surface: #ffffff;
  --color-border: #e2e8f0;
}
```

Bridge to Fuaran:

```css
/* fuaran-bridge.css — loaded AFTER fuaran-reference.css */
:root {
  /* Brand */
  --fuaran-tone-brand-fg: var(--color-brand);
  --fuaran-tone-brand-bg: var(--color-brand-soft);
  --fuaran-tone-brand-border: var(--color-brand-border);

  /* Success */
  --fuaran-tone-success-fg: var(--color-success);
  --fuaran-tone-success-bg: var(--color-success-soft);
  --fuaran-tone-success-border: var(--color-success-border);

  /* Critical */
  --fuaran-tone-critical-fg: var(--color-danger);
  --fuaran-tone-critical-bg: var(--color-danger-soft);
  --fuaran-tone-critical-border: var(--color-danger-border);

  /* Warning */
  --fuaran-tone-warning-fg: var(--color-warning);
  --fuaran-tone-warning-bg: var(--color-warning-soft);
  --fuaran-tone-warning-border: var(--color-warning-border);

  /* Default, Info, Subdued */
  --fuaran-tone-default-fg: var(--color-text);
  --fuaran-tone-default-bg: var(--color-surface);
  --fuaran-tone-default-border: var(--color-border);

  --fuaran-tone-subdued-fg: var(--color-text-muted);
  --fuaran-tone-subdued-bg: color-mix(in srgb, var(--color-text-muted) 8%, var(--color-surface));
  --fuaran-tone-subdued-border: var(--color-border);

  --fuaran-tone-info-fg: var(--color-brand);
  --fuaran-tone-info-bg: var(--color-brand-soft);
  --fuaran-tone-info-border: var(--color-brand-border);
}
```

This is the most direct shape – one `var()` per Fuaran slot. If a host doesn't have a token for one of the seven tones, either pick the closest existing token (Brand → Info is common) or invent a new pair and add it to the host's `tokens.css` alongside the existing ones.

## Example D – Dark mode via `prefers-color-scheme`

Fuaran itself has no dark mode opinion; it renders against whatever the variables resolve to at runtime. The bridge pattern composes naturally with the host's existing dark-mode mechanism. Two common shapes:

### D.1 Media-query-driven (no JS toggle)

```css
/* fuaran-bridge.css */
:root {
  --fuaran-tone-default-bg: #ffffff;
  --fuaran-tone-default-fg: #1f2937;
  --fuaran-tone-brand-fg: #1d4ed8;
  --fuaran-tone-brand-bg: #eff6ff;
  /* ... */
}

@media (prefers-color-scheme: dark) {
  :root {
    --fuaran-tone-default-bg: #0f172a;
    --fuaran-tone-default-fg: #f1f5f9;
    --fuaran-tone-brand-fg: #60a5fa;
    --fuaran-tone-brand-bg: rgb(96 165 250 / 0.12);
    /* ... */
  }
}
```

The renderer re-paints automatically whenever `prefers-color-scheme` changes – no `dispatch` round-trip needed.

### D.2 Class-toggled (Tailwind / shadcn dark class)

If the host uses a `.dark` class on `<html>` to switch themes (Tailwind's `darkMode: 'class'`, shadcn's default), the bridge follows the same shape:

```css
/* fuaran-bridge.css */
:root {
  --fuaran-tone-default-bg: #ffffff;
  --fuaran-tone-default-fg: #1f2937;
  /* ... */
}

.dark {
  --fuaran-tone-default-bg: #0f172a;
  --fuaran-tone-default-fg: #f1f5f9;
  /* ... */
}
```

Or – if the host already maintains its own `--color-*` set that auto-flips with the dark class – just bridge through to the host's tokens and let dark mode propagate from there (Examples A / B / C, with no explicit dark-mode block in the bridge file).

## I have my own hover colour – interaction-state tokens (Phase 12.N)

The bridges above cover the **idle** tone palette. The Phase 12.N
interaction-state matrix extends the same re-binding pattern to
`:hover` / `:focus-visible` / `:active` / `:disabled` via 88 additional
`--fuaran-tone-{tone}-{state}-{slot}` + `--fuaran-focus-ring-*` tokens. A
consumer who already owns hover / focus colours for their brand re-binds
those tokens at the same `:root` layer:

```css
/* fuaran-bridge.css — loaded AFTER fuaran-reference.css */
:root {
  /* Static palette (per the examples above) */
  --fuaran-tone-brand-fg: var(--color-brand);
  --fuaran-tone-brand-bg: var(--color-brand-soft);
  --fuaran-tone-brand-border: var(--color-brand-border);

  /* Hover / focus / active — host's pre-existing per-state palette */
  --fuaran-tone-brand-hover-fg: var(--color-brand-dark);
  --fuaran-tone-brand-hover-bg: var(--color-brand-soft-strong);
  --fuaran-tone-brand-active-fg: var(--color-brand-darker);
  --fuaran-focus-ring-color: var(--color-brand);
}
```

Four worked examples (Tailwind utility bridge, shadcn `data-state`,
raw-CSS-override, dark-mode hover-state inversion) extend the bridges
above into the interaction layer – see the Phase 12.N migration doc at
[`migrations/12-N-interaction-state-tokens.md`](migrations/12-N-interaction-state-tokens.md)
"Consumer bridge pattern" section. The token surface inventory + per-component
state matrix lives at [`HOST-STYLING-CHECKLIST.md`](../HOST-STYLING-CHECKLIST.md)
§1.6.

## Common pitfalls

- **Bridge loads BEFORE reference CSS.** The bridge's `:root` re-binding has to override the reference's, which means it loads second. Reversed order means the reference values win and the bridge is silently ignored.
- **Only bridging some tones.** Forgetting to bridge `--fuaran-tone-warning-*` means warning surfaces render against the reference's amber palette while everything else renders against your brand. Either bridge all seven tones or accept the visual mismatch deliberately.
- **Bridging into per-component variables (e.g. `--fuaran-button-brand-fill`).** No such variable exists – see `HOST-STYLING-CHECKLIST.md` anti-patterns. Bridge at the tone layer (`--fuaran-tone-brand-fg`); the button styling picks it up automatically.
- **Forgetting the spacing / typography scales.** The bridge above only covers the tone palette. Hosts that want a denser layout should also bridge `--fuaran-space-*` and `--fuaran-text-*` (see `HOST-STYLING-CHECKLIST.md` §1.2–1.4). The bridge template carries placeholders for both.
- **Using HSL/RGB without checking the syntax.** Tailwind v3 expects `rgb(var(--token-rgb) / <alpha>)`; shadcn expects `hsl(var(--token))`; raw CSS expects `var(--token)`. Mixing shapes (e.g. wrapping an already-`rgb(...)` token in `rgb(...)` again) silently produces an invalid value and the variable resolves to its fallback – looks like the bridge didn't take effect.

## See also

- [`HOST-STYLING-CHECKLIST.md`](../HOST-STYLING-CHECKLIST.md) – full variable + class-hook enumeration.
- [`HOST-INTEGRATION-CHECKLIST.md`](../HOST-INTEGRATION-CHECKLIST.md) – the six host-interface wires (host-integration tier).
- [`src/Fuaran.UI.Renderer/content/fuaran-reference.css`](../src/Fuaran.UI.Renderer/content/fuaran-reference.css) – the reference stylesheet the bridge re-binds.
- [`src/Fuaran.UI.Renderer/content/fuaran-bridge.css.template`](../src/Fuaran.UI.Renderer/content/fuaran-bridge.css.template) – copy-and-customise template (rename + fill RHS).
- [`docs/migrations/12-H-host-styling-contract.md`](migrations/12-H-host-styling-contract.md) – Phase 12.H migration notes.
- [`docs/migrations/12-N-interaction-state-tokens.md`](migrations/12-N-interaction-state-tokens.md) – Phase 12.N migration notes; four worked examples extending the bridges above into `:hover` / `:focus-visible` / `:active` / `:disabled`.
