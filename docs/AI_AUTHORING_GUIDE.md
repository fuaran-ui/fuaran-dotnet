# Authoring Fuaran from Claude / an AI agent

> This guide is written for AI authors (Claude, GPT, etc.) emitting Fuaran UI trees against the type contract. It collapses the canonical wire-format specification ([`WIRE_FORMAT.md`](WIRE_FORMAT.md)) + the conformance corpus into the concrete patterns an AI needs to author successfully on the first try and recover gracefully when it doesn't.
>
> **Every JSON wire example below is generated from the [`wire-format-fixtures/`](../../wire-format-fixtures/) corpus and drift-checked in the build** – the shapes you see here are exactly the bytes the decoder accepts. (The marker comments around each block wire them to the corpus; ignore them when reading.)
>
> Companion references: [`WIRE_FORMAT.md`](WIRE_FORMAT.md) – the language-neutral wire-format spec; [`ERROR_CODES.md`](ERROR_CODES.md) – a 0-latency error-code cheat sheet for pattern-matching in your retry loop; [`prompt-pack/`](prompt-pack/) – the copy-pasteable system-prompt + few-shot + schema + tool-defs pack.

## What Fuaran is

Fuaran is an **AI-emittable F# UI language**. The same Fuaran record-tree projects two ways:

- **F# manifestation** – records + lists + pipes; the smart-constructor surface humans author against.
- **JSON manifestation** – flat objects with a `"kind"` tag; the shape AI authors emit.

Both project from the same record contracts in [`Fuaran.UI.Types`](../src/Fuaran.UI/Types.fs). When you (the AI) emit JSON, the wire-side decoder reconstructs the typed `Node<'Msg>` value; when a human authors F#, the typed value serialises to the same JSON. **You can't have a wire-format question Fuaran doesn't answer at the type level.**

Fuaran's strategic position: AI generation target AND closed-loop self-debugging substrate. The substrate ships you four runtime-introspection tools (`fuaran.getNodeState` / `getBindingValue` / `getRenderedDom` / `getRuntimeErrors`) so you can observe what the renderer did with your emission and retry against authoritative state.

## The minimal valid tree

A Fuaran tree is a recursive `Node<'Msg>` record. Every node carries four fields:

| Field | Type | Required | Purpose |
|---|---|---|---|
| `Id` | `NodeId` (string-newtype) | ✅ | Stable identifier across turns. Reused on re-emit. |
| `Kind` | `NodeKind<'Msg>` (DU) | ✅ | What this node renders as (Box / Metric / Markdown / Button / DataGrid / ...). |
| `State` | `StateBehaviour<'Msg>` | ✅ | Required slots: `OnLoading`, `OnEmpty`, `OnError`. Always present (the type system enforces this). |
| `Style` | `SemanticStyle` | ✅ | Tone × Weight × Emphasis cube. Use `SemanticStyle.Default` if you don't have an opinion. |

### Minimal example – F# author view

```fsharp
Fuaran.metric "metric-1" {
    Defaults.metric with
        Label = TextSource.Literal "Revenue"
        Value = Binding.Static(Some 1234.5)
        Format = CellFormat.Currency "GBP"
        Tone = ToneVariant.Brand
        Icon = Some(IconSource.Named "trending-up")
        Subtext = Some(TextSource.Literal "vs last month")
        Trend = Some(Binding.Static(Some 0.07))
        TrendFormat = Some(CellFormat.Percent 1)
}
```

### Same node – flat JSON wire shape

This is the canonical wire form the decoder accepts – `id` + `kind`, with `kind` an
**object** whose `$type` names the primitive and whose spec fields sit **directly beside
`$type`** (no `spec` wrapper):

<!-- fuaran:example fixture=metric-1 -->
```json
{
  "id": "metric-1",
  "kind": {
    "$type": "Metric",
    "format": {
      "$type": "Currency",
      "code": "GBP"
    },
    "icon": "trending-up",
    "label": "Revenue",
    "subtext": "vs last month",
    "tone": "Brand",
    "trend": {
      "$type": "Static",
      "value": 0.07
    },
    "trendFormat": {
      "$type": "Percent",
      "decimals": 1
    },
    "value": {
      "$type": "Static",
      "value": 1234.5
    }
  }
}
```
<!-- /fuaran:example -->

**Key invariants** (the type system enforces; you must respect when emitting JSON – see [`WIRE_FORMAT.md`](WIRE_FORMAT.md) §3):
1. `id` is a non-empty string, unique tree-wide, reused on re-emit.
2. `kind` is an **object**: `{ "$type": "Metric", … }`. It is never `"kind": "Metric"` with the spec fields hoisted to the node's top level.
3. The spec's fields are hoisted **beside `$type`** – there is **no `spec` wrapper** and no Layout/Display/Input/Visualisation category envelope (the category is recovered host-side on decode).
4. Text slots are a `TextSource`. The canonical form is the object `{ "$type": "Literal", "text": "Revenue" }`, but you may emit a **bare string** `"Revenue"` as shorthand for a literal – the decoder accepts it and it canonicalises to the object form (WIRE_FORMAT.md §16). Prefer the bare string for plain labels; it saves the most tokens. (`Bound` / `I18n` text still needs its `$type` object.) Data slots are a `Binding` object keyed by `$type` (`{ "$type": "Static", "value": 4321.0 }`), never a `"binding"` key.
5. `state` / `style` / `accessibility` are **omitted** when empty / all-default; `None` fields are omitted, never emitted as `null`. Restore-on-absence (§16) also covers each kind's **stylistic** spec fields – `tone` / `weight` / `emphasis` / `format` / `width` restore their identity default when absent (WIRE_FORMAT.md §3.6, Phase 460), so a `Metric`, `Callout`, or grid column carrying only its semantic fields is complete. The **semantic** required fields still must be present (a `Button`'s `variant`, a `Metric`'s `label`/`source`, a `TextSource` label); the decoder raises `MISSING_FIELD` on those rather than inferring a default. The per-kind required/optional split is enumerated in the prompt pack's [`system-prompt.md`](prompt-pack/system-prompt.md) required-fields table (schema-derived, drift-checked) and pinned by [`schema.json`](prompt-pack/schema.json).

### Nesting – containers carry a `children` array

Layout primitives nest child nodes under `children`. A dashboard holding a card (metric +
total row) and a stack (metric + note):

<!-- fuaran:example fixture=composite-root -->
```json
{
  "id": "composite-root",
  "kind": {
    "$type": "Box",
    "children": [
      {
        "id": "composite-card",
        "kind": {
          "$type": "Box",
          "children": [
            {
              "id": "metric-2",
              "kind": {
                "$type": "Metric",
                "format": {
                  "$type": "Currency",
                  "code": "GBP"
                },
                "icon": "trending-up",
                "label": "Revenue",
                "subtext": "vs last month",
                "tone": "Brand",
                "trend": {
                  "$type": "Static",
                  "value": 0.07
                },
                "trendFormat": {
                  "$type": "Percent",
                  "decimals": 1
                },
                "value": {
                  "$type": "Static",
                  "value": 1234.5
                }
              }
            },
            {
              "id": "lvr-1",
              "kind": {
                "$type": "LabelValueRow",
                "emphasis": true,
                "format": {
                  "$type": "Number",
                  "decimals": 2
                },
                "help": "Last 30 days",
                "label": "Total",
                "value": {
                  "$type": "Static",
                  "value": 42
                }
              }
            }
          ],
          "heading": "Composite",
          "layout": {
            "$type": "Flex",
            "direction": "Vertical",
            "wrap": false
          },
          "role": "Card"
        }
      },
      {
        "id": "stack-1",
        "kind": {
          "$type": "Box",
          "children": [
            {
              "id": "metric-1",
              "kind": {
                "$type": "Metric",
                "format": {
                  "$type": "Currency",
                  "code": "GBP"
                },
                "icon": "trending-up",
                "label": "Revenue",
                "subtext": "vs last month",
                "tone": "Brand",
                "trend": {
                  "$type": "Static",
                  "value": 0.07
                },
                "trendFormat": {
                  "$type": "Percent",
                  "decimals": 1
                },
                "value": {
                  "$type": "Static",
                  "value": 1234.5
                }
              }
            },
            {
              "id": "markdown-1",
              "kind": {
                "$type": "Markdown",
                "text": "Updated hourly."
              }
            }
          ],
          "layout": {
            "$type": "Flex",
            "direction": "Vertical",
            "wrap": false
          },
          "role": "Group"
        }
      }
    ],
    "layout": {
      "$type": "Auto"
    },
    "role": "Dashboard"
  }
}
```
<!-- /fuaran:example -->

## Available smart constructors (F# side) / kinds (JSON side)

Fuaran ships these `NodeKind` variants – emit the matching `"kind"` tag. Source: [`src/Fuaran.UI/Fuaran.fs`](../src/Fuaran.UI/Fuaran.fs) + [`src/Fuaran.UI/Defaults.fs`](../src/Fuaran.UI/Defaults.fs).

### Layout kinds (`Layout.*`)

**The container cluster is one wire kind: `Box` (Phase 390).** `Stack` / `GridLayout` / `Dashboard`
/ `Card` were merged into a single `Box` primitive whose **`layout`** mode names how children arrange
(`Flex` direction+wrap | `Grid` cols/template | `Auto` responsive tile) and whose **`role`** names what
the container *means* (`Group` | `Card` | `Dashboard` | `Separator`, driving the HTML element + ARIA
landmark + `fuaran-*` chrome). The F# smart constructors below **survive unchanged** as `Box`-emitting
convenience surfaces – the authoring vocabulary is the same; only the wire `"kind"` consolidates to
`Box`. See [`BOX-CONTAINER-UNIFICATION.md`](BOX-CONTAINER-UNIFICATION.md).

| F# constructor | JSON `"kind"` | Children | Purpose |
|---|---|---|---|
| `Fuaran.dashboard` | `Box` (`layout: Auto`, `role: Dashboard`) | ✅ | Top-level responsive auto-tile region. One dashboard per portal page. |
| `Fuaran.stack` | `Box` (`layout: Flex`, `role: Group`) | ✅ | Vertical / horizontal flex flow. Inter-child spacing is the `Flex` `gap`. |
| `Fuaran.grid` | `Box` (`layout: Grid`, `role: Group`) | ✅ | N-col grid; per-child `Column.span`. |
| `Fuaran.gridLayout` | `Box` (`layout: Grid`, `role: Group`) | ✅ | N-col / verbatim-template grid (richer than `grid`). |
| `Fuaran.splitPanel` | `SplitPanel` | ✅ | Resizable splitter. |
| `Fuaran.tabs` | `Tabs` | ✅ | Tabbed container. Pre-Phase-69 shape with integer-indexed `ActiveIndex` / `OnSelect`. |
| `Fuaran.tabsTagged` | `Tabs` | ✅ | Phase 69 (2026-05-29) tab entry point with typed-tag overlay + explicit per-tab headers. See "Tabs with explicit headers" below. |
| `Fuaran.card` | `Box` (`layout: Flex`, `role: Card`) | ✅ | Bordered container with optional `heading`. |
| `Fuaran.stepper` | `Stepper` | ✅ | Multi-step wizard. |
| `Fuaran.summaryList` | `SummaryList` | ✅ | Single-card container of label/value rows (Phase 12.P). See "SummaryList vs Dashboard" below. |
| `Fuaran.disclosure` | `Disclosure` | ✅ | Accordion / collapsible section (Phase 65). Native `<details>` / `<summary>`. See "Disclosure vs Card" below. |

### Display kinds (`Display.*`)

Childless leaves.

| F# constructor | JSON `"kind"` | Purpose |
|---|---|---|
| `Fuaran.metric` | `Metric` | Headline metric with optional goal/tone/format. |
| `Fuaran.markdown` | `Markdown` | Rendered markdown text. |
| `Fuaran.callout` | `Callout` | Tier/mode/status banner. |
| `Fuaran.progress` | `Progress` | Determinate or indeterminate progress bar. |
| `Fuaran.badge` | `Badge` | Inline status pill. |
| `Fuaran.sparkline` | `Sparkline` | Inline trend microviz. |
| `Fuaran.skeleton` | `Skeleton` | Loading placeholder. |
| `Fuaran.heading` | `Heading` | Section title. Variant axis (`Standard` / `Eyebrow` / `Caption` / `Lead`) Phase 12.P – see "Heading variants" below. |
| `Fuaran.labelValueRow` | `LabelValueRow` | Single label-left / value-right row (Phase 12.P). Typically nested inside `Fuaran.summaryList`. |
| `Fuaran.link` | `Link` | Crawlable hyperlink – a real `<a href>` (Phase 139). See "Links and navigation" below. |

**No `Spacer`, no `Divider` (Phase 459).** Both retired into `Box`: inter-child spacing is a *container
property* – set `gap` on a `Box`'s `Flex` / `Grid` `layout` rather than inserting a spacer node – and a
horizontal rule / separator is a childless `Box` with `role: Separator` (`<hr>` / `role="separator"`).
The decoder **rejects** a bare `"$type": "Spacer"` / `"Divider"` (`UNKNOWN_DU_CASE`).

#### Links and navigation – `Link` vs `Button` + `Action.Navigate`

Two different intents, two different primitives:

- **`Fuaran.link` (`Display.Link`) – a real destination.** Renders a real
  `<a href="…">`: crawlable by search engines, followable with JavaScript
  disabled, and the right choice for content-to-content navigation (an article
  linking to another article, a breadcrumb, a footer sitemap). Reach for this
  whenever the target is a URL a user could bookmark or a crawler should index.
  Two-tier API: `Fuaran.link "id" "https://…" "Label"` for the static-href 80%
  case; `Fuaran.linkSpec "id" { Defaults.link with Href = …; Rel = Some
  "noopener"; Target = Some "_blank"; Download = true }` for bound hrefs and the
  `rel` / `target` / `download` attributes. The `href` is sanitised at render
  time (`javascript:` / `vbscript:` / raw `data:` collapse to `about:blank`).
- **`Fuaran.button` + `Action.Navigate "route"` – an in-app routing gesture.**
  Renders a `<button>` whose click runs client-side SPA routing through the
  runtime. There is no crawlable URL; it is invisible to search engines and
  does nothing with JavaScript disabled. Reach for this for stateful in-app
  transitions ("next step", "open settings panel") – not for content links.

**Rule of thumb:** if a crawler should follow it or a user should be able to
bookmark it, it is a `Link`. If it is a stateful gesture with no URL, it is a
`Button` + `Action.Navigate`. The validator's **FUARAN063** flags a `Link` with
a blank `Href` and steers you to one of the two shapes.

### Input kinds (`Input.*`)

| F# constructor | JSON `"kind"` | Purpose |
|---|---|---|
| `Fuaran.button` | `Button` | Clickable action emitter. |
| `Fuaran.form` | `Form` | Schema-driven form. |
| `Fuaran.filters` | `Filters` | Filter strip on a dataset. |
| `Fuaran.fileUpload` | `FileUpload` | Upload affordance. |
| `Fuaran.select` | `Select` | Filterable picker (single / multi). |

### Form-field kinds (`FormFieldKind.*`)

A `Fuaran.form` carries an ordered list of fields; each field's `Kind` chooses the input element + ARIA / keyboard model the renderer emits.

| F# case | Use for |
|---|---|
| `FormFieldKind.Text` | Free-text input. |
| `FormFieldKind.Number` | Numeric input. Add `RangedNumber` (below) when min/max/step bounds matter. |
| `FormFieldKind.RangedNumber` | Numeric input with optional `Min` / `Max` / `Step`. Pairs with FUARAN051 range advisory. |
| `FormFieldKind.Checkbox` | Boolean toggle. |
| `FormFieldKind.Choice` | Dropdown (`<select>`) – exclusive choice from a list that may be long or dynamically-sized. |
| `FormFieldKind.SegmentedChoice` (Phase 66) | Visible-options exclusive choice. `Horizontal` = segmented control pill row; `Vertical` = radio-button list. See "Segmented choice" below. |
| `FormFieldKind.TextArea` | Multi-line text. |
| `FormFieldKind.Range` | Dual-thumb numeric range. The value is a `(min, max)` pair; optional `Min` / `Max` / `Step` bound both ends. |
| `FormFieldKind.Date` | Date / time / datetime input. The value is an ISO-8601 string; `DateVariant` picks the native control. |
| `FormFieldKind.DateRange` | Start-and-end dates in **one** control — the value is an ordered `(from, to)` pair of ISO-8601 strings, with `DateVariant` and the optional ISO `Min` / `Max` + numeric `Step` bounding both ends. Reach for this rather than two `Date` fields whenever the two dates are one value: in a filter strip it binds **one** filter param, so everything downstream scopes off a single key. A literal pair must be ordered (`from <= to`) or the tree is refused at decode. |

#### Segmented choice (Phase 66)

`FormFieldKind.SegmentedChoice` is the visible-options counterpart to `Choice`. The full list of options is always on screen; the user picks one without opening a menu. Two orientations, picked at the call site:

- `Orientation.Horizontal` – emits `<div role="radiogroup">` containing per-option `<button role="radio">` elements styled as a segmented control via the `--fuaran-segmented-*` tokens. Arrow keys cycle (wrap-around); Home / End jump to the ends. Use for `≤5` short labels that read well side-by-side ("view mode", "tier", "metric").
- `Orientation.Vertical` – emits `<fieldset>` of `<input type="radio">` + `<label>` pairs grouped by shared `name`. The browser handles arrow-key navigation natively. Use for longer labels or settings-pane preference lists.

The parallel `FilterKind.SegmentedFilter` carries the same shape for filter strips (`FilterKind.ChoiceFilter` remains the dropdown shape).

**When to reach for `SegmentedChoice` vs `Choice`:**

| Situation | Reach for |
|---|---|
| ≤5 short options, user benefits from seeing them all at once | `SegmentedChoice` (Horizontal) |
| ≤7 options with longer labels (settings preference) | `SegmentedChoice` (Vertical) |
| Long or dynamically-sized list (categories, countries, customers) | `Choice` (dropdown) |
| Selecting between content panels (not value options) | `Tabs` – distinct ARIA role |

Rule of thumb: ≤5 options that fit visibly → `SegmentedChoice`; >5 or dynamic-sized → `Choice`. The validator's FUARAN045 advisory fires when a static `SegmentedChoice` exceeds 7 options.

**Don't conflate with `Tabs`.** Tabs select between content panels (`role="tablist"`); segmented controls select among value options (`role="radiogroup"`). The aria-role split reflects the distinct user model.

Authoring example (a canonical metric-selector shape):

```fsharp
{ Defaults.formField<Msg> with
    Id = "metric"
    Label = TextSource.Literal "Metric"
    Kind =
        FormFieldKind.segmentedChoice
            (binding.``static``
                [ { Value = "effective"; Label = TextSource.Literal "Effective rate" }
                  { Value = "marginal";  Label = TextSource.Literal "Marginal rate" }
                  { Value = "takeHome";  Label = TextSource.Literal "Take-home %" } ])
            (binding.state "metric" None)
            (SetMetric >> Action.dispatch)
            Orientation.Horizontal }
```

### Visualisation kinds (`Visualisation.*`)

| F# constructor | JSON `"kind"` | Purpose |
|---|---|---|
| `Fuaran.grid` (in `Visualisation`) | `DataGrid` | AG Grid-backed editable table. |
| `Fuaran.chart` | `Chart` | AG Charts-backed chart. |
| `Fuaran.table` | `DataGrid` (`staticRows` mode) | Simple read-only HTML table. Phase 393 retired the separate `Table` kind: `Fuaran.table` is an authoring shorthand that lowers into `NodeKind.DataGrid` with a `staticRows` payload, so **there is no `"$type": "Table"` on the wire** — emit `DataGrid`. |
| `Fuaran.map` | `Map` | Geographic map with markers. |

### Structural kinds

| F# constructor | JSON `"kind"` | Children | Purpose |
|---|---|---|---|
| `Fuaran.errorBoundary` | `ErrorBoundary` | `child` + `fallback` | Render `fallback` if `child` throws at render time. |
| `Fuaran.switch` | `Switch` | `cases[].child` + `default` | **State-bound conditional child (Phase 392)** – render one branch based on a reactive state key. See below. |
| `Fuaran.fragmentDecl` / `fragmentRef` | `FragmentDecl` / `FragmentRef` | – | Declare / expand a reusable subtree. |
| `Fuaran.custom` | `Custom` | opaque | The bounded escape hatch (see "Custom" below). |

### Conditional rendering – `Switch` (Phase 392)

`Switch` renders **one** child subtree chosen by a reactive state key: a `stateKey`
(a StateStore key), an ordered list of `(match, child)` **cases**, and a `default`.
The renderer resolves the value at `stateKey`, compares its **string form** against
each case's `match` in order (**first-match-wins**), and renders that case's child;
if none match, it renders `default` (also the SSR / first-paint surface before any
state is set). State changes ride the ordinary typed `Action.SetState` through the
existing policy gate – there is **no new dispatch path** – and the client re-renders
the matching case; SSR renders the initial match and hydrates without mismatch.

JSON wire shape:

```json
{ "id": "view-switch",
  "kind": { "$type": "Switch",
            "stateKey": "view",
            "cases": [ { "match": "details", "child": { "id": "d", "kind": { "$type": "Markdown", "text": "Details…" } } },
                       { "match": "summary", "child": { "id": "s", "kind": { "$type": "Markdown", "text": "Summary…" } } } ],
            "default": { "id": "empty", "kind": { "$type": "Callout", "tone": "Info", "body": "Pick a view", "dismissable": false } } } }
```

**Wizard panes over `Switch` + `Button`/`SetState` (the canonical pattern).** A
multi-pane wizard is a `Switch` keyed off a step-state, with `Button`s that
`SetState` the step – no bespoke kind, no host code:

```json
{ "id": "wizard", "kind": { "$type": "Box", "role": "Group", "layout": { "$type": "Flex", "direction": "Vertical" },
  "children": [
    { "id": "panes", "kind": { "$type": "Switch", "stateKey": "step",
        "cases": [ { "match": "plan",    "child": { "id": "p1", "kind": { "$type": "Markdown", "text": "Step 1 — choose a plan" } } },
                   { "match": "billing", "child": { "id": "p2", "kind": { "$type": "Markdown", "text": "Step 2 — billing details" } } },
                   { "match": "review",  "child": { "id": "p3", "kind": { "$type": "Markdown", "text": "Step 3 — review & confirm" } } } ],
        "default": { "id": "p0", "kind": { "$type": "Markdown", "text": "Step 1 — choose a plan" } } } },
    { "id": "next", "kind": { "$type": "Button", "label": "Next",
        "onClick": { "$type": "SetState", "key": "step", "value": "billing" } } } ] } }
```

Seed the initial step (`SetState "step" "plan"` at start, or host-seeded state);
each `Next`/`Back` button `SetState`s the step and the `Switch` re-selects the pane.

**Compose over `Switch` instead of proposing a new kind.** Many "I need a container
that shows X *or* Y" requests are `Switch` compositions, not new vocabulary – 
conditional regions, wizard panes, empty-state alternatives, mode toggles, and
carousels/galleries (`Box` + `Switch` over an index state) all fall out of
`Switch` + `SetState`. Before reaching for a new `NodeKind`, ask whether the pattern
is "render a different subtree when this state changes" – if so, it is a `Switch`.
This is the standing example the vocabulary-growth charter
([`VOCABULARY.md`](VOCABULARY.md) §1.2) points to: the minimalist vocabulary stays
small precisely because `Switch` absorbs the long tail of conditional-container
requests.

**Two gotchas the validator catches:** every case needs a **distinct** `match`
(duplicates make the later case dead – **FUARAN082**, error), and the `stateKey`
must be **non-empty** (an empty key never resolves – the switch is stuck on
`default` – **FUARAN083**, warning).

### Defaults

Every spec record has a matching `Defaults.X` record with sensible defaults. Always start from `Defaults.X` and override only what differs – this is the canonical authoring idiom and minimises emission size.

## Motion (Phase 12.F)

Fuaran's typed surface doesn't include arbitrary animation primitives. Instead, every `Node<'Msg>` has an optional `Motion` field carrying one of 8 canonical tokens:

| Token | JSON | When to use |
|---|---|---|
| `Motion.None` | `"motion": "None"` | No motion. Equivalent to omitting the field. |
| `Motion.PulseDuringLoad` | `"motion": "PulseDuringLoad"` | Long-running async indicator. Apply to a Metric / Card whose `Source` is still resolving. |
| `Motion.FadeInOnMount` | `"motion": "FadeInOnMount"` | New content arriving – Metric value transitions from loading to resolved; section appears for the first time. |
| `Motion.SlideInFromBelow` | `"motion": "SlideInFromBelow"` | Disclosure expansions, drawer-shaped content arrival. (No-op hook in the reference CSS – host theme expected.) |
| `Motion.ShakeOnError` | `"motion": "ShakeOnError"` | Error callouts. Use sparingly – a shake on every error is fatigue-inducing; reserve for cases where the user must notice. |
| `Motion.RotateOnRefresh` | `"motion": "RotateOnRefresh"` | Refresh affordance – icon or chip spinning while data refetches. (No-op hook.) |
| `Motion.SlideInFromRight` | `"motion": "SlideInFromRight"` | Sidebar / panel arriving from the right edge. |
| `Motion.ExpandCollapse` | `"motion": "ExpandCollapse"` | Accordion-shaped content reveal. (No-op hook.) |

**Emit motion sparingly.** Animation has a real attention cost; default to no motion unless the absence would actively confuse (e.g. async indicators where stillness reads as "frozen"). Heavy animation is the `Custom` escape hatch's job, not Fuaran's typed surface.

The reference CSS implements the four most common tokens via `@keyframes` and respects `@media (prefers-reduced-motion: reduce)`. The remaining four are class hooks consumers extend at the design-system layer.

**Finer-grained animations go to `Custom`** – Fuaran's typed vocabulary is deliberately small.

## Semantic roles + font voice (Phase 147)

`SemanticStyle` carries two optional, bounded fields beyond the `Tone × Weight × Emphasis` cube – a named **content role** and a **font voice**. The AI emits them as *intent*; the renderer projects a stable `fuaran-role-{role}` / `fuaran-voice-{voice}` class the host CSS owns (class-name-only, no raw style). Both default to "unset" and are **omitted from the wire at their default**, so omitting them costs nothing.

| `StyleRole` | JSON | When to use |
|---|---|---|
| `StyleRole.None` | _(omitted)_ | No content role. The default. |
| `StyleRole.Eyebrow` | `"role": "Eyebrow"` | A small kicker / overline label above a heading or section – **on a `Heading` node, prefer `HeadingVariant.Eyebrow`**; use `StyleRole.Eyebrow` to tag a non-heading node. |
| `StyleRole.Data` | `"role": "Data"` | Tabular / numeric data voice – figures, metric values, monospaced data (tabular-nums). |
| `StyleRole.Lede` | `"role": "Lede"` | A lead paragraph / standfirst – the intro voice above body copy. |
| `StyleRole.Caption` | `"role": "Caption"` | A small supporting caption / footnote on a non-heading node – on a `Heading`, prefer `HeadingVariant.Caption`. |

| `FontVoice` | JSON | When to use |
|---|---|---|
| `FontVoice.Default` | _(omitted)_ | No declared voice. The default. |
| `FontVoice.Display` | `"voice": "Display"` | Large, expressive headline / cover / hero type – the "display" voice. |
| `FontVoice.Structural` | `"voice": "Structural"` | Body copy + UI chrome – the workhorse "structural" voice. |

**Role vs `HeadingVariant`.** `HeadingVariant.{Eyebrow,Caption,Lead}` owns heading-text variants (it emits `fuaran-heading-*` and keeps the `<h{Level}>` tag). `StyleRole` tags the content role of *any* node (it emits `fuaran-role-*`). On a `Heading`, reach for `HeadingVariant`; elsewhere, reach for `StyleRole`. **Don't set both for the same intent.**

These are bounded, additive DU cases – there is no free-text role. A design system that declares a `ThemeManifest` (Phase 145) can bind a role to a token, and the `StyleObserver` (Phase 146) verifies the emitted role resolved to the bound token. Finer-grained typography goes to the host CSS / `Custom`, not the typed surface.

## Style vocabularies – density & prominence, not font styling (Phase 460)

The `Tone × Weight × Emphasis` cube and the `format` / `width` slots each carry a **small closed
vocabulary**, and every one is **omitted-when-default** (leave it out and the identity default applies
 – WIRE_FORMAT.md §3.6). The cases mean **density and prominence**, not font styling – the common
misread that made models emit `weight: "Bold"` / `emphasis: "Strong"`:

| Field | Vocabulary | Identity default | Means |
|---|---|---|---|
| `tone` (`ToneVariant`) | `Default` · `Subdued` · `Brand` · `Success` · `Warning` · `Critical` · `Info` | `Default` | semantic **colour role** |
| `emphasis` (`Emphasis`) | `Quiet` · `Normal` · `Loud` | `Normal` | visual **prominence** – `Loud` ≠ bold text |
| `weight` (`StyleWeight`) | `Compact` · `Standard` · `Spacious` | `Standard` | layout **density** – spacing, *not* font-weight |
| `format` (`CellFormat`) | `None` / `Number` / `Currency` / `Percent` / `Date` / `SignificantDigits` | `None` | number formatting on a metric/column |

**Omit when unsure.** Each has an identity default the decoder restores on absence – if you have no
specific colour / prominence / density intent, **leave the field out**. That is the correct minimal
emission, and it never raises `MISSING_FIELD`. Emit a case only when you mean it, and only from the
list above (an unknown case fails with `UNKNOWN_DU_CASE` and the expected-case list).

**Lenient synonyms accepted on input** (decode-only – a re-emit normalises to the canonical case):
`tone: Positive`→`Success`, `Danger`/`Negative`→`Critical`, `Neutral`→`Default`; `emphasis:
Strong`/`Bold`→`Loud`, `Subtle`/`Muted`→`Quiet`. `StyleWeight` has **no** synonyms – `Bold`/`Heavy`
is font-weight intent and would misread the density vocabulary, so it fails loudly; prefer omitting it.

## Custom (Phase 12.F)

When Fuaran's typed shape can't express what you need – complex animations beyond the 8 tokens, niche third-party components (Framer Motion, AG Pivot Grid Enterprise, MapLibre vector tiles), hand-tuned visualisations – emit a `Custom` node. The `kind.$type` is `Custom`, with `moduleId` + `componentId` + a host-defined `props` object beside it:

<!-- fuaran:example fixture=custom-1 -->
```json
{
  "id": "custom-1",
  "kind": {
    "$type": "Custom",
    "componentId": "trend-card",
    "moduleId": "analytics",
    "props": {}
  }
}
```
<!-- /fuaran:example -->

`props` is an arbitrary JSON object the host's renderer interprets – its shape is defined by the registered component, not by the Fuaran type contract (the example above has none). The runtime dispatches against `(moduleId, componentId)`; the host registers the actual rendering function (consumer-side concern, not in your scope as an author). If no renderer is registered, the renderer falls back to a labelled placeholder showing the prop keys – the host knows the slot exists but the rendering surface is missing.

**Hard rules for `Custom` emission:**

1. **You emit the slot; the host owns the rendering.** Don't author what the rendered shape looks like – that's the developer's `RegisterCustomRenderer` call.
2. **`moduleId` + `componentId` are stable identifiers.** Treat them like database column names: don't rename without coordinating with the host.
3. **Props are AI-opaque from the host's perspective too.** Pass the data the renderer needs; the host's renderer function decides how to interpret it.
4. **DO NOT use `Custom` as a generic catch-all.** If the same idea could be expressed with `Metric` / `Box` (a `Card` or `Group` role) / `DataGrid`, use those. `Custom` exists for genuine vocabulary gaps, not for bypassing the typed-tree contract.

### `ExtraAttributes` is NOT for you

`Node.ExtraAttributes : Map<string, string> option` exists on every Node, but the AI authoring contract **forbids you from emitting it**. The §4d JSON wire shape omits it on emit; the field is a consumer-only escape for test hooks (`data-cy`, `data-testid`) and analytics tags (`data-analytics`) that the developer adds at the host layer. If you emit `extraAttributes` in your JSON, the decoder will reject the field.

## Custom – the last-resort bounded escape (Phase 70)

Custom is the language's principled escape hatch for components that genuinely don't fit the typed surface – third-party React components, canvas-based visualisations, drag-drop interactions, platform-specific affordances. Reach for it when you have to, and when you do, declare its boundaries: a `contentHash` so op-stream replay can verify the body hasn't drifted; an `exposedNodeIds` list so the layout observer and AI introspection can still reason about declared interior elements. These fields make Custom usage observable, not opaque.

Custom is NOT the path of least resistance for typed-surface friction. If you find yourself reaching for Custom repeatedly within the same project, treat that as a signal: there's a typed-contract gap the language should close – surface it as a feature request, not absorb it into normal Custom usage. The validator's FUARAN054 advisory exists to make this signal visible – when a project's Custom-node count exceeds the healthy threshold, the advisory fires so the maintainer sees the creep.

AI emission target: AI-authored trees should reach for typed nodes by default and only emit Custom when the consumer has explicitly registered a `(moduleId, componentId)` pair that the AI is being asked to invoke. The AI does not invent new Custom registrations.

### First-class extensions – emit a registered widget's props by schema

A host can register a custom component as **first-class**: `CustomContract.createWithSchema` declares a
typed **prop schema** (each prop's name / type / required), and a `CustomRegistry` then makes the
component behave like a built-in kind in the two ways that matter to you as an author:

- **Discovery.** `registry.DescribeForAi()` projects each registered component's prop schema into a
  card – `{ moduleId, componentId, props: [{ name, type, required }] }` – which the orchestrator folds
  into your available-kinds prompt context. So when you're asked to emit a registered widget, you see
  its exact prop contract (e.g. `points: string (required)`, `width: int`) and emit `props` against it,
  instead of guessing an opaque bag.
- **Validation.** `registry.ValidateProps(moduleId, componentId, props)` checks your emitted `props`
  against that schema – a missing required prop or a wrong-typed value is a `FUARAN068` defect, exactly
  as a mistyped built-in field would be. A registered custom kind is no longer a validation blind spot.

So: prefer typed built-ins; for a registered custom kind, emit its `props` per the schema in your
context; you still never invent a `(moduleId, componentId)` the host hasn't registered.

### The two additive safety fields

When the host's registered renderer ships with a known source hash and declared interior NodeIds, populate them:

<!-- fuaran:example fixture=custom-bounded-1 -->
```json
{
  "id": "custom-bounded-1",
  "kind": {
    "$type": "Custom",
    "componentId": "QualityRing",
    "contentHash": {
      "algorithm": "SHA256",
      "hash": "abc123def456",
      "strictness": "StrictReplay"
    },
    "exposedNodeIds": [
      "quality-ring-segment-1",
      "quality-ring-segment-2"
    ],
    "moduleId": "deal-flow",
    "props": {}
  }
}
```
<!-- /fuaran:example -->

- **`contentHash`** is the identity envelope. `algorithm` is `"SHA256"` for v1. `strictness` is `"StrictReplay"` (mismatches route through the `OnError` slot) or `"AdvisoryWarning"` (mismatches render normally but log). Pre-Phase-70 trees omit the field – opting out of replay safety is a valid choice but the validator's FUARAN055 advisory surfaces it.
- **`exposedNodeIds`** declares which interior NodeIds the registered renderer emits as `data-fuaran-node-id` attributes. The renderer post-mount-verifies them; the layout observer / structural ops / AI introspection address them by id. Declaring an id that the registered renderer doesn't actually emit fires FUARAN053.

Both fields are omittable. They make Custom safer to USE; they do not make Custom more INVITING.

### When NOT to add bounded-escape fields

- You're emitting a Custom slot for a third-party widget whose source is opaque (Stripe Elements, Clerk widgets) – there's nothing meaningful to hash. Leave `contentHash` out; FUARAN055 surfaces the omission, and the validator's manifest can declare it as expected.
- The Custom body has no addressable interior structure (a pure canvas paint, a single `<iframe>`). Leave `exposedNodeIds` as `[]`.

## Tabs with explicit headers + the Custom escape for non-Fuaran tab bodies (Phase 69)

Pre-Phase-69, `Fuaran.tabs` inferred per-tab labels from the child's `Card.Heading` and required every tab body to be expressible in typed Fuaran. Both constraints relaxed 2026-05-29:

1. **Declare per-tab labels explicitly** via `TabsSpec.TabHeaders`. The renderer no longer infers from `Card.Heading`; what you write is what shows. Aligns 1:1 with `Children` by index – FUARAN047 catches length mismatches.
2. **Mix typed Fuaran and `NodeKind.Custom`-wrapped Feliz tab bodies.** A tab body that cannot reasonably translate to typed Fuaran (e.g. a heatmap with per-cell colour gradients) wraps as `Custom`; the host registers the renderer via `IFuaranRuntime.TryRenderCustom` exactly as described in the "Custom (Phase 12.F)" section above.
3. **Bind active-tab state to a typed DU** via the `TabTags` / `ActiveTag` / `OnSelectTag` overlay. The model carries `ActiveResultTab : ResultTab` (a typed DU); the overlay maps it to / from the wire `string` tag without an integer-indirection step on the consumer side. The integer-indexed `ActiveIndex` / `OnSelect` shape stays the canonical wire form – the tag overlay is consumer ergonomics on top.

Use the **`Fuaran.tabsTagged`** smart-ctor as the entry point when you adopt the typed overlay – it requires both `TabHeaders` and `TabTags` to be populated, so the typed-tag contract is obvious at the call site.

```fsharp
Fuaran.tabsTagged
    "results-tabs"
    { Defaults.tabs<Msg> with
        Children = [ breakdownTab; heatmapTab; incomeTab ]
        TabHeaders =
            Some
                [ { Defaults.tabHeader with Label = TextSource.Literal "Breakdown" }
                  { Defaults.tabHeader with Label = TextSource.Literal "Heatmap" }
                  { Defaults.tabHeader with Label = TextSource.Literal "Income" } ]
        TabTags = Some [ "breakdown"; "heatmap"; "income" ]
        ActiveTag = Some(binding.computed (fun _ -> tagOf model.ActiveResultTab))
        OnSelectTag = Some(fun tag -> Action.dispatch (SetActiveTab(tabOf tag))) }
```

JSON wire shape (a two-tab corpus fixture; additive – pre-Phase-69 payloads remain valid). Note `tabHeaders` aligns 1:1 with `children` by index, `tabTags` carries the typed-tag overlay, and the `onSelect` closure is the `"<closure>"` sentinel:

<!-- fuaran:example fixture=tabs-explicit-1 -->
```json
{
  "id": "tabs-explicit-1",
  "kind": {
    "$type": "Tabs",
    "activeIndex": {
      "$type": "Static",
      "value": 1
    },
    "activeTag": {
      "$type": "Static",
      "value": "overview"
    },
    "children": [
      {
        "id": "markdown-1",
        "kind": {
          "$type": "Markdown",
          "text": "Updated hourly."
        }
      },
      {
        "id": "spark-1",
        "kind": {
          "$type": "Sparkline",
          "source": {
            "$type": "Static",
            "value": [
              1,
              2,
              3,
              2,
              4
            ]
          }
        }
      }
    ],
    "onSelect": "<closure>",
    "tabHeaders": [
      {
        "icon": "overview-glyph",
        "label": "Overview"
      },
      {
        "disabled": {
          "$type": "Static",
          "value": false
        },
        "label": "Detail"
      }
    ],
    "tabTags": [
      "overview",
      "detail"
    ]
  }
}
```
<!-- /fuaran:example -->

(The spec fields sit directly under `$type` – no `spec` wrapper – and `state`/`style` are omitted because they're empty/default. See [`WIRE_FORMAT.md`](WIRE_FORMAT.md) §3.1–§3.2.)

(`kind` carries the primitive `$type` directly – `Tabs` – with no `Layout` category envelope; the category is recovered host-side on decode. See [`WIRE_FORMAT.md`](WIRE_FORMAT.md) §3.2.)

Use this shape when the model-side active-tab state is a DU rather than an int. For URL-deep-linkable integer-indexed tabs, keep using `Fuaran.tabs`.

### Validator codes

| Code | Severity | Trigger |
|---|---|---|
| **FUARAN047** | Error | `TabHeaders.Length ≠ Children.Length` |
| **FUARAN048** | Error | `TabTags.Length ≠ Children.Length` |
| **FUARAN049** | Warning | `ActiveTag` is `Some` but `TabTags` is `None` (binding has nothing to resolve against) |
| **FUARAN063** | Warning | `Fuaran.link` / `Fuaran.linkSpec` has a blank `Href` (statically-knowable empty / whitespace string). Provide a real destination URL, or use `Fuaran.button` + `Action.Navigate` for a stateful in-app gesture. |
| **FUARAN064** | Warning | `Fuaran.button` has `Disabled = Some (Binding.Static false)` – a constant-false disabled binding never disables the button (equivalent to omitting `Disabled`). Almost always an unfinished binding: point `Disabled` at the live state, e.g. `Disabled = Some (binding.state "loading" false)`. `Binding.Static true` (a permanent-disable placeholder) is legitimate and not flagged. |
| **FUARAN069** | Warning | An interactive control (form field / Select / Tabs / dismissable Modal / Disclosure) has **no event handler and no writable value binding** – the Phase 426 write-back default has nothing to write to, so the control is inert. Bind the control's value slot directly to `{"$type":"State","key":…}` (or `{"$type":"Filter","name":…}`), or supply the handler. |
| **FUARAN070** | Error | A `Binding.Selection` names a `nodeId` **absent from the tree** – the reader can never resolve (nothing exists to produce a selection under that id). Point the binding at the selection-producing node's id (Phase 427). |
| **FUARAN071** | Warning | A `Binding.Selection` targets a node that exists but is **not a selection-producing kind** (a Visualisation – grid/chart/table/map). Nothing in the tree will ever write that selection; usually a mis-targeted id. |
| **FUARAN072** | Warning | An `Action.Call` carries `into: Query <name>` but **no `Binding.Query <name>` in the tree reads that slot** – an orphan fetch (Phase 428). Usually a name typo; target the query name a reader binds. |
| **FUARAN073** | Warning | An `Action.Call` has **neither `onResult` nor `into`** – the response is dropped (Phase 428). Legitimate for command-style endpoints; add `into` when the response carries data a reader needs. |
| **FUARAN074** | Warning | A declared filter chip is **consumed by nothing** – no `Binding.Filter` read outside the chip itself, no `Query.dependsOn`, no `Transform` param source. A decorative filter: setting it changes nothing. Wire a consumer (Phases 421/424) or drop the chip. |
| **FUARAN075** | Error | A **declared filter edge** (`Query.dependsOn`, or a `Transform` param whose source is `{"$type":"Filter"}`) names a filter **no `Filters` chip declares** – the edge can never fire from the tree. Usually a name typo. (A plain `Binding.Filter` value read is exempt – hosts may feed filters without chips.) |
| **FUARAN076** | Warning | A `Transform` `params` entry names a param the **pipeline never references** (`paramsOf`) – dead weight; rename the param or the pipeline reference. |
| **FUARAN077** | Warning | A grid column has **neither `value` nor `field`** – it renders blank in every host (Phase 425). Always give a decoded column a `field`. |
| **FUARAN078** | Warning | A `DataGrid` has **neither `rowKey` nor `rowKeyField`** – no stable row identity, so selection highlighting (Phase 427) and keyed diffing degrade. Give the grid a `rowKeyField`. |
| **FUARAN091** | Error | The tree nests nodes deeper than the wire limit **max node depth = 24** (`WIRE_FORMAT.md` §21). Reported once, at the first node past the limit, carrying that node's id; the walk stops descending there, so a single over-deep subtree does not bury the rest of the report. A decoder refuses such a tree outright (`LIMIT_EXCEEDED`), so this fires on trees built in-process. Flatten the nesting — 24 levels is far beyond any realistic layout (a deliberately deep dashboard reaches about 16). |
| **FUARAN090** | Warning | A `DataGrid` sets `editable: true` but its `source` is **not directly a `$state` binding** – edits have nowhere to go (a `Transform` pipeline is not invertible; `Static` rows are host data), so every cell renders read-only (Phase 663). Source the grid – and every reader that should track edits – from a shared `{"$type":"State","key":…,"defaultValue":[rows]}` binding. |

`PreEmitValidate.validate` reports all of these; fix every reported defect before submitting the tree.

### Runtime query-binding codes (Phase 323)

These are **runtime** codes, not AST-validator codes. `Fuaran.UI.QueryBinding.check schema tree` resolves a dynamic query's result `Schema` (`(name, ColumnType)` pairs) against the tree's query-bound sinks and rejects a binding whose column type is incompatible with its sink class – the static F# type system cannot see a column type that is only known once the query resolves at runtime. The defect carries `AvailableFields` (the §4d recovery hint: the columns that *would* type-check) + a `Suggestion`.

| Code | Severity | Trigger |
|---|---|---|
| **FUARAN066** | Error | A query column is bound into an incompatible sink class – e.g. a `string` column into a numeric sink (Metric value, chart Y-series, numeric form field). `AvailableFields` lists the schema columns compatible with that sink. |
| **FUARAN067** | Error | A query-bound slot references a column **absent** from the resolved schema (default-deny by shape, FGP 3). `AvailableFields` lists the columns the schema actually has. |
| **FUARAN068** | Error | A registered custom component's `props` violate its declared `PropSchema` – a required prop is missing, or a present prop's JSON shape doesn't match its declared `PropType` (surfaced by `CustomRegistry.ValidateProps` / `PreEmitValidate.validateWithRegistry`). Emit the props the component's kind card declares, typed as declared. |

## What survives the wire (host-only vs wire-survivable)

Everything you emit as an AI author is JSON – so it must be **wire-survivable**. Some F# authoring
constructs erase to a sentinel when serialised (`"<closure>"` for a function value, `"<opaque>"` for a
non-enumerated `Binding.Static` payload) and become invisible to op-stream replay, structural diffing,
introspection, and the TypeScript / Python hosts – a decoded tree shows *nothing* for them. These are
**host-only** and are **not for you**: they are F#-only authoring escapes. The full per-case
classification is [`WIRE_FORMAT.md`](WIRE_FORMAT.md) §5.1 (a projection of `Fuaran.UI.WireSurvivability`);
the build-time validator flags the primary one, `Binding.Computed`, as **FUARAN084** (an Error in an
orchestrated run).

**The escape ladder – always prefer the wire-survivable form:**

| Instead of (host-only) | Emit (wire-survivable) |
|---|---|
| `Binding.Computed` (a compute closure) | `Binding.Transform` (declarative data derivation), `Binding.Format` (locale/number formatting), or `Binding.State` / `Binding.Filter` (reactive values) |
| a closure grid column (`Column.value (fun row -> …)`) | `Column.field "propertyName"` + a typed `CellFormat` – the renderer projects the named row property with zero host code |
| a value-changed handler (`onChange` / `onToggle` / `onSelect` closure) | **omit it** – the renderer's write-back default writes the change to the control's own writable `Binding.State` / `Binding.Filter` value slot |
| an `onResult` continuation on `Action.Call` | `Action.Call … into: State/Query` – the declarative result target |
| `RowKey` (a row→string closure) | `RowKeyField "propertyName"` |
| `CellFormat.Custom (fun v -> …)` | one of the six typed `CellFormat` cases (`Number` / `Currency` / `Percent` / `SignificantDigits` / `Date` / `None`) |

If a task genuinely needs host-only behaviour (an arbitrary compute, an interactive cell mutation), that
is a **host wiring** job for the F# integrator, not something you emit – leave the control declarative and
let the host attach behaviour.

## The wire-format contract

When you emit JSON it MUST be RFC 8259 strict and conform to the canonical wire format ([`WIRE_FORMAT.md`](WIRE_FORMAT.md) §2 lists the eleven encoder rules in full). The lexical essentials:

**Required (per RFC 8259 strict):**
- UTF-8 encoding.
- Double-quoted keys + string values; single quotes are rejected.
- No trailing commas after the last array element or object property.
- No comments (`//` or `/* */`).
- No JavaScript-style hex literals (`0xFF`) – use decimal.
- No bare `NaN` / `Infinity` / `-Infinity` – these are not valid JSON (the wire encodes them as the quoted strings `"NaN"` / `"Infinity"` / `"-Infinity"`; see [`WIRE_FORMAT.md`](WIRE_FORMAT.md) §5).
- Numeric values within IEEE 754 double range.

**Forbidden but commonly emitted by mistake:**
- Unquoted property names (JavaScript-style) – rejected.
- Multi-line strings without `\n` escapes – rejected.
- A `"kind"` whose value is a bare string instead of a `{ "$type": … }` object – the single most common shape error.

If your emission violates the wire shape, the decoder returns a structured, recoverable `DecodeError` envelope – `{ Code, Path, Message, ExpectedShape? }` – never a throw. The six codes and their JSONPath locations are in [`WIRE_FORMAT.md`](WIRE_FORMAT.md) §6 and [`ERROR_CODES.md`](ERROR_CODES.md); pattern-match on `Code` in your retry loop.

**Self-validate against the public surface before you submit.** The canonical-JSON encoder – `Fuaran.UI.OpStream.Abstractions.CanonicalJson` (the same deterministic algorithm the op-stream uses for hash chaining) – round-trips a typed `Node` to the exact wire bytes, so a host that holds your tree can re-encode and confirm it matches. The companion [`schema.json`](../../wire-format-fixtures/schema.json) (Draft 2020-12) is a drop-in for any off-the-shelf validator or provider-native constrained-output mode.

For **tree-shape pre-emit checks** that don't require a JSON encoder (duplicate `NodeId` detection, empty identifier strings, empty `Custom` kind ids), call [`Fuaran.UI.PreEmitValidate.validate`](../src/Fuaran.UI/PreEmitValidate.fs). Fable-compatible (no reflection), returns `Result<unit, PreEmitDefect list>` – every defect surfaced in one pass, no short-circuiting, so the AI can repair the tree in a single turn.

## Structural editing: the 10 TreeOp variants

After the initial emission, subsequent turns can edit the tree via `Fuaran.UI.Ops.TreeOp` rather than re-emitting the full tree. Source: [`src/Fuaran.UI.Ops/Types.fs:45-96`](../src/Fuaran.UI.Ops/Types.fs).

| Op | Purpose | When to use |
|---|---|---|
| **`EditNode`** | Replace one node's `Kind` (preserves `Id` / `State` / `Style`). | "Swap Metric for a Sparkline at the same position." |
| **`UpdateProp`** | Patch a field on a spec record – top-level (`"Label"`) or nested through the per-kind typed surface (`"Columns[0].Label"`, `"YFields[1]"`, `"TabHeaders[1].Disabled"`, `"Fields[0].Required"`). | "Change Metric.Label from 'Revenue' to 'Net revenue'"; "rename the second grid column". |
| **`ReplaceBinding`** | Replace a Binding-typed slot's binding source. | "Switch Metric.Value from Query 'revenue' to Query 'revenue-net'." |
| **`UpdateStyle`** | Replace `SemanticStyle` wholesale. | "Change a callout from Tone.Info to Tone.Warning." |
| **`UpdateState`** | Replace `StateBehaviour` wholesale. | "Update the Skeleton onLoading slot to use a different shape." |
| **`InsertChild`** | **Append** a new child to a parent. | "Add a Markdown summary to the dashboard." |
| **`RemoveNode`** | Remove a node. | "Drop the deprecated progress bar." |
| **`MoveNode`** | Move a subtree to a different parent, **appending** it there. | "Move the Metric from the header into the sidebar." |
| **`ReorderChildren`** | State a parent's child order, by naming every child id. | "Reorder the dashboard tabs: General, Detail, Settings." |
| **`Batch`** | All-or-nothing group of inner ops. | "Insert a row + two Metrics in one atomic change." |

**Membership and order are separate ops.** `InsertChild` and `MoveNode` change *which* children a
parent has, and both **append**. `ReorderChildren` states *what order* they are in, by naming every
child id. Placing a node anywhere but last is the two together, in one `Batch`:

```json
{"$type":"Batch","ops":[
  {"$type":"InsertChild","child":{"id":"summary","kind":{"$type":"Markdown","text":"Totals for Q3."}},"parentId":"composite-root"},
  {"$type":"ReorderChildren","newOrder":["summary","composite-card","stack-1"],"parentId":"composite-root"}
]}
```

`newOrder` must name **exactly** the parent's children as they stand after the insert — a partial or
stale list is `OrderingMismatch`, and the error lists the ids it expected.

**None of these ops takes an index**, because a collection whose members have identity is addressed by
identity. That is a different question from the bracket indices in `UpdateProp` paths below
(`Columns[0].Label`): those address *contained data* inside a single node, whose items have no id of
their own, and they are unaffected.

**Emission model** (§4g): emit the **full tree** on turn 1 or on large restructures (>50% nodes changed); emit **ops** on subsequent turns when the change is small. The orchestrator picks the heuristic; you emit whichever the orchestrator's prompt asks for.

**`UpdateProp` path grammar** (`WIRE_FORMAT.md` §3.4): dot-separated field segments with optional
0-based bracket indices for list positions – `"Label"`, `"Columns[0].Label"`, `"YFields[1]"` (an
indexed scalar leaf). Indices are bracket-form only (`"Columns.2.Label"` is `PathInvalid` – the
error names the bracket form). The nested surface is per-kind and typed: grid
`Columns[i].{Label,Format,Width}`, chart `YFields[i]`, tabs `TabHeaders[i].{Label,Icon,Disabled}`,
form `Fields[i].{Label,Required,Help}`. Closure-bearing sub-fields (`Columns[i].Value`,
`Columns[i].Kind`, `Fields[i].Kind`) and paths outside this surface surface `PathNotSupportedYet`
with the kind's supported paths in the hint – use a structural op (`EditNode` / `ReplaceBinding`)
there instead.

**v1 limitations**:
- Storage-shape op layer (JSONL `ops/turn-NNN.jsonl` persistence) is deferred to Phase 12.Z; current shipping ops are in-memory only.

## Binding resolution rules

`Binding<'T>` is the typed wire between AI emission and host-supplied data. Six cases; emit the case that matches the binding intent.

Every binding is a `$type`-keyed object (the same discriminator dispatch as `kind`), and closure-bearing slots inside it (a `Query` accessor, a `Computed` fn) render as the `"<closure>"` sentinel:

| F# case | JSON wire shape | Expression at runtime |
|---|---|---|
| `Binding.Static value` | `{ "$type": "Static", "value": <value> }` | `$static` |
| `Binding.Query("name", accessor)` | `{ "$type": "Query", "accessor": "<closure>", "name": "name" }` | `$queries.<name>` |
| `Binding.Filter "name"` | `{ "$type": "Filter", "name": "name" }` | `$filters.<name>` |
| `Binding.Selection nodeId` | `{ "$type": "Selection", "accessor": "<closure>", "nodeId": "..." }` | `$selection.<nodeId>` |
| `Binding.State("key", default)` | `{ "$type": "State", "defaultValue": <default>, "key": "..." }` | `$state.<key>` |
| `Binding.Computed (fun ctx -> ...)` | `{ "$type": "Computed", "fn": "<closure>" }` (closure not serialisable) | `$computed` |
| `Binding.Local local` (Phase 62) | `{ "$type": "Local", "flushOn": {...}, "format": "<closure>", "initialFrom": {...}, "onCommit": "<closure>", "parse": "<closure>" }` | renderer-side useState slot |

**Resolution rules:**
1. **Module-scoped names** by default – `Binding.Query "revenue"` resolves against the current module's query namespace. Cross-module references require explicit qualification (`Binding.Query "billing.revenue"`).
2. **`Binding.Static`** is preferred over `Binding.Query` for compile-time-known values – keeps the wire shape narrow.
3. **`Binding.Computed`** carries a closure – when emitting JSON, the closure isn't serialisable; rely on `Binding.Query` with an accessor instead, or escalate to the orchestrator if you need computation.

### Declarative filter wiring (Phase 423)

A `Filters` node's chips are self-wiring – you do **not** author an `onChange` handler (that would be a closure you cannot emit). Instead, **omit `onChange`** on each `FilterKind` and point its `value` at its own filter via `Binding.Filter` with the **same name** as the chip. The renderer writes `$filters.<name>` on every change and re-renders every reader of that filter, so the loop closes with zero host code:

```json
{ "$type": "TextFilter", "value": { "$type": "Filter", "name": "q" } }          // chip named "q"
{ "$type": "ChoiceFilter", "options": <Binding>, "value": { "$type": "Filter", "name": "tier" } }
{ "$type": "RangeFilter", "value": { "min": 0, "max": 100 } }                    // authorable bounds
```

Then any consumer scoped by the filter reads the **same** `Binding.Filter "q"` (e.g. a Metric's `source`, a Select's filtered options). The chip writes the filter; the consumers read it – that is the whole wiring, all in the tree. (An F#-authored app may still supply a real `onChange` closure for custom dispatch; a present handler wins and does not write the store.) `RangeFilter` bounds ride as a typed `{ "min": …, "max": … }` object – emit real numbers, not a placeholder.

**Filter-scoped tables/charts over embedded data (Phase 424).** When a grid/chart's rows come from **embedded data you author** (not a host query), scope them with a `Binding.Transform` whose pipeline has a `filter` step comparing a column to a `param`, and bind that param to the chip via `params`:

```json
{ "$type": "Transform",
  "params": [ { "from": { "$type": "Filter", "name": "tier" }, "name": "tier" } ],
  "pipeline": [ { "$type": "filter", "pred": { "$type": "binary", "op": "eq",
    "left": { "$type": "col", "name": "tier" }, "right": { "$type": "param", "name": "tier" } } } ],
  "source": { "schema": [ … ], "columns": { … } } }
```

A chip write re-evaluates the pipeline; an **unset** choice filter drops the constraint (shows all rows). The filter→consumer edge is derived from the pipeline – nothing to declare or keep in sync. **Division of labour:** embedded/declarative data → `Binding.Transform` + `params`; host-computed data (an opaque `Query`) → `Query` + `dependsOn`.

**Filter-scoped tables/charts over host-computed data (Phase 421).** When a consumer's rows come from an opaque host `Query` (the host owns the filtering), declare *which* filters scope it with `dependsOn` – you name the edge, the host still owns the predicate:

```json
{ "$type": "Query", "name": "orders", "dependsOn": ["status", "date-range"] }
```

The host re-runs the query when any named filter changes. This is the host-computed twin of `Transform` + `params`: reach for `dependsOn` when the data is an opaque query; reach for `Transform` + `params` when you author the data inline. Never invent a `filter`/predicate field on a component – that shape does not exist.

**A `DataGrid` displays data with field names, not closures (Phase 425).** Author each column with a `field` naming the row property to show, and give the grid a `rowKeyField` for stable row identity – no `value`/`rowKey` closures (those are F#-author overrides you cannot emit):

```json
{ "$type": "DataGrid", "rowKeyField": "id",
  "columns": [ { "$type"... "label": "Dept", "field": "dept", "kind": { "$type": "Text" } },
               { "label": "Amount", "field": "amount", "kind": { "$type": "Text" } } ],
  "source": { "$type": "Transform", ... } }
```

The renderer projects each `field` off the row (a `Transform` produces rows keyed by column name). A column with **neither** `value` nor `field` renders blank – always give a decoded column a `field`.

**Master-detail is self-wiring too (Phase 427).** A data-bearing grid with an omitted `onRowClick` writes the clicked row to the selection store under **its own node id**; any reader binds `{"$type":"Selection","nodeId":"<the grid's id>"}` and re-renders with the full row on every click – zero host code:

```json
{ "id": "orders-grid", "kind": { "$type": "DataGrid", "rowKeyField": "id",
  "columns": [ …field-named columns… ], "source": { "$type": "Transform", … } } }
{ "id": "detail", "kind": { "$type": "Metric", "label": …,
  "source": { "$type": "Selection", "nodeId": "orders-grid" } } }
```

Give the grid a `rowKeyField` (stable identity – it also drives the selected-row highlight). The `nodeId` must name a real, selection-producing node: a `Selection` over an id absent from the tree is FUARAN070 (error); over a non-Visualisation node, FUARAN071 (warn). A present `onRowClick` closure wins and suppresses the default write.

**An editable grid is the write-back default applied to rows (Phase 663).** `"editable": true` has semantics only when the grid's `source` is **directly** a `$state` binding carrying the rows as its default:

```json
{ "id": "perf-grid", "kind": { "$type": "DataGrid", "editable": true, "rowKeyField": "quarter",
  "columns": [ …field-named Text/Numeric columns… ],
  "source": { "$type": "State", "key": "perf-rows",
              "defaultValue": [ { "quarter": "Q1", "revenue": 120 }, { "quarter": "Q2", "revenue": 150 } ] } } }
{ "id": "perf-chart", "kind": { "$type": "Chart", "kind": "Bar", "xField": "quarter", "yFields": ["revenue"],
  "source": { "$type": "State", "key": "perf-rows",
              "defaultValue": [ { "quarter": "Q1", "revenue": 120 }, { "quarter": "Q2", "revenue": 150 } ] } } }
```

The renderer turns each field-named Text/Numeric cell into an input and commits the updated rows to the key on every edit; the chart on the same key re-renders live. `"editable": true` over a `Transform`/`Static` source is inert (the data is not writable – every cell renders read-only) and draws **FUARAN090** (warn). A `Transform` pipeline cannot read the edited rows back, so keep every edit-tracking reader on the plain `$state` source. Date columns and row add/remove are not part of the floor.

### The control write-back default – every interactive control is self-wiring (Phase 426)

The Phase 423 chip rule generalises to **every value-carrying control**: form fields (all eight kinds), `Select` (single and multi), `Tabs`, `Modal`, and `Disclosure`. **Omit the event handler** (`onChange` / `onToggle` / `onSelect` / `onDismiss` – closures you cannot emit) and bind the control's **value slot directly to a `$state` key** (`{"$type":"State","key":…,"defaultValue":…}`) or a `$filters` name. The renderer then writes every change back to that slot and re-renders every reader of it – a fully interactive control with zero host code:

```json
{ "$type": "Text",     "value": { "$type": "State", "key": "profileName", "defaultValue": "" } }
{ "$type": "Checkbox", "value": { "$type": "State", "key": "agree", "defaultValue": false } }
{ "$type": "Tabs",     "activeIndex": { "$type": "State", "key": "activePane", "defaultValue": 0 }, … }
{ "$type": "Modal",    "open": { "$type": "State", "key": "confirmOpen", "defaultValue": false }, "dismissable": true, … }
{ "$type": "Disclosure", "open": { "$type": "State", "key": "advancedOpen", "defaultValue": false }, … }
```

What gets written: text/textarea/date → the string; number → the number; checkbox → the bool; choice/segmented/select → the chosen option value (clearing the choice clears the slot); multi-select → the selected list (against `values`); tabs → the clicked index (against `activeIndex`) or, with a `tabTags` overlay, the clicked tag (against `activeTag`); a dismissable modal → `false` on dismiss; disclosure → the new open bool. Anything else that reads the same `$state` key (a `Metric`'s bound label, a `Transform` param with a `State` source, a modal-opening button's `Action.SetState`) sees the change immediately.

**The one-paragraph interactive-dashboard recipe:** fields, tabs, modals, disclosures → bind values to `$state` keys and omit handlers; filter chips → name them and let them write `$filters.<name>` (Phase 423); data consumers → scope embedded data with `Transform` + `params`, host data with `Query` + `dependsOn`; buttons → wire-survivable `Action`s (`SetState` to open a modal or switch a tab, `Call` for host work). **A control with no handler and a value bound to anything other than `State`/`Filter` is inert** – the FUARAN069 pre-emit check warns; fix it by binding the value to a `$state` key.

### The declarative fetch – `Call` + `into` (Phase 428)

A plain data fetch – the refresh button, "load details on click" – is one action: `Call` the host-registered endpoint and say **where the response lands**:

```json
{ "$type": "Call", "endpoint": "/api/total",  "into": { "$type": "State", "key": "total" } }
{ "$type": "Call", "endpoint": "/api/orders", "into": { "$type": "Query", "name": "orders" } }
```

An `into: State` result is read by `{"$type":"State","key":"total"}` bindings; an `into: Query` result by `{"$type":"Query","name":"orders"}` bindings (the whole response flows through – the decoded accessor is an identity projection). Readers re-render on completion; on failure the slot stays unwritten and readers keep their `onLoading` surface. **Division of labour:** `Call` + `into` = an explicit user-triggered fetch; `Query` + `dependsOn` = host-recomputed data scoped by filters; `AiTool` = the orchestrated round-trip. Endpoints are host-registered `ApiEndpoint`s behind the default-deny dispatch gate – `into` adds no new capability, only a destination. The `queryResults` population contract: a host may pre-populate slots, a `Call … into Query` writes them live, and both feed the same `Binding.Query` readers. Target a query name **some reader actually binds** (FUARAN072 warns on an orphan fetch); a `Call` with neither `onResult` nor `into` drops its response (FUARAN073 warns – fine for command endpoints).

### The declarative floor – never emit a closure sentinel (Phase 430)

One rule generalises everything above: **if you find yourself emitting `"<closure>"`, there is a declarative form – use it.** Handlers → omit them and bind the value slot (`$state` / `$filters`; Phases 423/426/427). Display accessors → `field` / `rowKeyField` (Phase 425). Call results → `into` (Phase 428). On the decoded path a closure sentinel is inert by construction *and* suppresses the declarative default, so it is strictly worse than omitting it. The dead-on-decode lint (FUARAN080 sentinel event slot / FUARAN081 sentinel display slot) flags every such slot and names the declarative remedy; the sanctioned closure-only escapes (`Binding.Computed`, `Binding.Local`'s pipeline, cell-mutation handlers, …) are registered `HostOnly-by-design` in `Fuaran.UI.SlotCapability` and stay quiet.

### `Binding.Computed` reading state vs `Binding.State` directly (Phase 137)

A `Binding.Computed (fun ctx -> …)` closure receives a `BindingContext` with **typed read access to live module state** – `ctx.TryGetState<'T> "key"` (or the pipeline form `ctx |> BindingContext.tryGetState<'T> "key"`) returns `'T option`, `None` for a missing key or a type mismatch (never throws). So a computed value can derive from a `Binding.State` slot:

```fsharp
// label text that depends on whether a "busy" flag is set
binding.computed (fun ctx ->
    if ctx.TryGetState<bool> "busy" = Some true then "Working…" else "Ready")
```

**Prefer `binding.state` for the common case.** Two reasons:

1. **Reactivity.** A direct `Binding.State` reader auto-re-renders when its key changes (the renderer's static `stateKeysOfBinding` analysis subscribes it). A `Computed` closure's state reads are *opaque* to that analysis, so a `Computed`-over-state binding does **not** auto-re-render on state change. Only reach for `Computed` when the value is genuinely *derived* (a branch, a format, a combination of slots) and a re-render is driven by something else, or the value is read once at mount.
2. **Wire shape.** `Computed` doesn't serialise (the closure encodes to the `$computed` sentinel), so an orchestrator emitting JSON can't express the derivation – it must emit a `Binding.State` / `Binding.Query` the host resolves. `Computed` is a host-side F#/TS authoring convenience, not an emit target.

If you find yourself writing `binding.computed (fun ctx -> ctx.TryGetState<bool> "x" = Some true)` – a bare slot read with no derivation – use `binding.state "x" false` instead.

### Local bindings (Phase 62)

`Binding<'T>.Local` is a component-scoped buffer for `FormFieldKind.Text` / `FormFieldKind.Number` whose value should NOT dispatch on every keystroke. Use it when the model only cares about the final committed value:

- Salary inputs (intermediate `"5."` would be un-parseable for the model)
- Formatted numeric fields with thousands separators
- Free-text drafts that need an explicit Apply

```fsharp
binding.local
    (binding.state "salary" 50000m)            // InitialFrom — re-sync source
    LocalFlushTrigger.OnBlur                     // commit boundary
    (fun v -> Action.dispatch (SetSalary v))     // OnCommit
    (Some formatThousands)                       // Format (display side)
    parseDecimalLenient                          // Parse (commit side)
```

**Don't reach for `Local` when `State` is right.** Live-search and inline filtering need per-keystroke dispatch – those stay `binding.state`.

**Pair `OnCommitAction` with `Action.CommitLocal "<field-id>"`.** If you emit a Local binding with `flushOn = OnCommitAction`, you must also emit a button or action that dispatches `Action.CommitLocal` keyed on the field's id. The validator's `FUARAN043` catches an OnCommitAction binding with no commit-action partner anywhere in the project.

**The flush triggers:**

| Trigger | When the buffer commits |
|---|---|
| `OnBlur` | Input loses focus (canonical free-text shape). |
| `OnSubmit` | Surrounding form's submit event fires. |
| `OnDebounce of ms` | `ms` milliseconds elapse without a keystroke. |
| `OnCommitAction` | An `Action.CommitLocal "<field-id>"` is dispatched somewhere. |

**Parse errors stay on the buffer side.** A failed parse keeps the raw text visible; the buffer does NOT dispatch and the field's `Help` slot can surface the error message. The renderer wires this through the existing field-help slot – no new error-display affordance.

### File upload, then read the body (Phase 136)

`Fuaran.fileUpload`'s `OnSelect` hands you a `FileSelection list` carrying metadata (`Name` / `Size` / `MimeType`) **plus an opaque `Ref`** – the blob itself stays browser-held. To ingest a file's body, **do not reach for `Browser.Dom.FileReader`**; chain `Action.ReadFileBody` off the `Ref`:

```fsharp
Fuaran.fileUpload
    "workbook-upload"
    { Defaults.fileUpload<Msg> with
        Label = TextSource.Literal "Upload workbook"
        Accept = [ ".xlsx"; ".csv" ]
        OnSelect =
            fun selections ->
                match selections with
                | sel :: _ ->
                    // Read the first selected file's bytes as base64, then
                    // dispatch them to the model — no FileReader interop.
                    Action.readFileBody sel.Ref FileReadEncoding.Base64 WorkbookLoaded
                | [] -> Action.Chain [] }
```

The renderer reads the blob through `IFuaranRuntime.ReadFileBody` (default browser impl: `FileReader`) and dispatches `onRead body` when the read completes – the read is async at the host level, but you express it as a normal `Action`, exactly like `Action.call`'s return channel.

**Pick the encoding by what the body is for:**

| `FileReadEncoding` | Reader projection | Use for |
|---|---|---|
| `Text` | `readAsText` | CSV / JSON / plain-text ingestion. |
| `Base64` | bytes, base64 with no data-URL prefix | uploading bytes to an API. |
| `DataUrl` | full `data:<mime>;base64,…` string | inline preview (`<img src=…>`). |

Only `Ref.Id` + `encoding` cross the wire – the blob and the `onRead` continuation never serialise (the continuation decodes to a no-op, like every closure slot). The file-read path consults the host's dispatch policy gate before reaching the substrate, so a deny-by-shape host can refuse file reads.

## The runtime-introspection AI tools (§4i)

You can call these four tools to inspect what the renderer did with your tree. Source: [`src/Fuaran.UI.AiTools/Tools.fs:265-381`](../src/Fuaran.UI.AiTools/Tools.fs).

### `fuaran.getNodeState`

```json
{
  "tool": "fuaran.getNodeState",
  "arguments": {
    "id": "metric-revenue",
    "include": ["Props", "Bindings", "CurrentState"]
  }
}
```

Returns the node's `Id` + `Kind` + (optionally) `Props` / `Bindings` / `CurrentState` / `StateDetail` / `Geometry` blocks. Use `include` to filter for cheap polling; an empty `include` list returns all five blocks. The `CurrentState` is one of `Normal` / `Loading` / `Empty` / `Error` – your loop should branch on this.

### `fuaran.getBindingValue`

```json
{
  "tool": "fuaran.getBindingValue",
  "arguments": { "id": "metric-revenue", "slot": "Value" }
}
```

Returns one Binding slot's resolved value (or a `BindingError` if resolution failed). Use this when `getNodeState` returns `CurrentState = Loading` and you want to confirm whether the query has arrived.

### `fuaran.getRenderedDom`

```json
{
  "tool": "fuaran.getRenderedDom",
  "arguments": { "id": "dashboard-root" }
}
```

Returns a recursive `GeometryTree` rooted at the addressed node – `(X, Y, Width, Height, Overflowing)` per descendant. Use this to detect layout problems (children overflowing parents, zero-height grids, etc.). Returns `ProbeUnwired` if the host hasn't bound an `IGeometryProbe`.

### `fuaran.getRuntimeErrors`

```json
{
  "tool": "fuaran.getRuntimeErrors",
  "arguments": { "since_turn": 3 }
}
```

Returns the runtime-error stream filtered to entries recorded after turn 3. Use this to diagnose "I emitted a tree but nothing rendered" failures – the error sink captures decoder failures, unwired actions, binding resolution exceptions, etc.

### Tools deferred to the downstream AI consumer

Six §4i tools are designed but not yet shipped: `fuaran.getDispatchLog`, `fuaran.simulateInteraction`, `fuaran.runEvalAssertions`, `fuaran.beginDebugSession`, `fuaran.endDebugSession`, `fuaran.revertToTurn`, `fuaran.escalate`. These are provided by a downstream AI-consumer/orchestration layer built on top of the language tier, not by the `Fuaran.UI.*` packages themselves.

## The closed-loop authoring shape (worked example)

The closed-loop authoring pattern: emit → observe → repair. Suppose the operator asks: **"Show the channel revenue with its month-on-month trend."**

### Turn 1 – you emit the initial tree

(A metric's number is its `source` binding; its movement is the optional `trend` binding – there is no `goal` field.)

<!-- fuaran:example fixture=metric-1 -->
```json
{
  "id": "metric-1",
  "kind": {
    "$type": "Metric",
    "format": {
      "$type": "Currency",
      "code": "GBP"
    },
    "icon": "trending-up",
    "label": "Revenue",
    "subtext": "vs last month",
    "tone": "Brand",
    "trend": {
      "$type": "Static",
      "value": 0.07
    },
    "trendFormat": {
      "$type": "Percent",
      "decimals": 1
    },
    "value": {
      "$type": "Static",
      "value": 1234.5
    }
  }
}
```
<!-- /fuaran:example -->

### Turn 2 – you observe the renderer

```
fuaran.getNodeState { id: "metric-1", include: ["CurrentState", "Bindings"] }

→ {
    "id": "metric-1",
    "kind": "Metric",
    "current_state": "Loading",
    "bindings": {
      "Source": { "resolution": "Failed", "code": "NotResolvedYet", "message": "source pending" },
      "Trend":  { "resolution": "Resolved", "value": 0.07, "type_hint": "Double" }
    }
  }
```

The `Source` binding hasn't resolved yet. **Normal**: wait one polling cycle and re-check.

### Turn 3 – the value arrives

```
fuaran.getNodeState { id: "metric-1", include: ["CurrentState", "Bindings"] }

→ {
    "current_state": "Normal",
    "bindings": {
      "Source": { "resolution": "Resolved", "value": 87432.50, "type_hint": "Double" },
      "Trend":  { "resolution": "Resolved", "value": 0.07, "type_hint": "Double" }
    }
  }
```

Converged. You report to the operator: "Revenue is £87,432.50, up 7% on last month."

### Editing – operator says "just pin it to 99.5 for now"

Rather than re-emit the whole tree, you emit a `ReplaceBinding` op targeting the metric's `Source` slot. The op is a JSON document whose own `$type` is the op kind:

<!-- fuaran:example fixture=op-replacebinding -->
```json
{
  "$type": "ReplaceBinding",
  "binding": {
    "$type": "Static",
    "value": 99.5
  },
  "slot": "Value",
  "target": "metric-1"
}
```
<!-- /fuaran:example -->

### Failure-mode example – the operator instead asks for an unregistered query

Suppose they ask for ARR and you emit a `ReplaceBinding` whose `binding` is `{ "$type": "Query", "accessor": "<closure>", "name": "arr" }`. If the host hasn't registered an `"arr"` query, you get back a structured error:

```json
{
  "op": { "$type": "ReplaceBinding", "target": "metric-1", "slot": "Source" },
  "error": {
    "code": "SourceUnregistered",
    "message": "Query 'arr' was not registered on the host's BindingSources",
    "hint": {
      "node_kind": "Metric",
      "suggestion": "Either ask the host to register the 'arr' query, or use Binding.Static if you have the value inline.",
      "available_alternatives": ["revenue (Query)", "99.5 (Static)"]
    }
  }
}
```

**Recovery**: per [`ERROR_CODES.md`](ERROR_CODES.md) decision tree, `SourceUnregistered` → escalate or emit a `Binding.Static`. You escalate to the operator: "The host doesn't have an `arr` query. Want me to compute it from `revenue` ÷ months in the period, or should we ask the operator to wire it?"

## Phase-scoped capabilities (shipped vs deferred)

What you can rely on today (as of 2026-06-08):

| Capability | Status | Notes |
|---|---|---|
| §4b type contract | ✅ | Full record-tree shipped in `Fuaran.UI` |
| §4g 10-op TreeOp vocabulary | ✅ | All 10 ops + Batch atomicity in `Fuaran.UI.Ops` |
| §4d error format + rich hints | ✅ | `ApplyError` + `ApplyHint` + `IntrospectError` |
| Canonical-JSON encoder + schema gate | ✅ | `CanonicalJson` round-trip + `schema.json` (Draft 2020-12) |
| 4 of 10 §4i introspection tools | ✅ | `getNodeState` / `getBindingValue` / `getRenderedDom` / `getRuntimeErrors` |
| Default-deny safety hardening | ✅ | Phase 12.Y / 12.Y.2 |
| Build-time validator | ✅ | `dotnet run --project src/Fuaran.UI.Validator` |
| Multi-scope runtime | ✅ | `FuaranRuntimeScope` opaque token (Phase 12.M) |
| Op-stream persistence + replay | ❌ Phase 12.Z (Wave 3) | "What state is the UI in?" answerable from op stream once shipped |
| Op telemetry + drift detector | ❌ Phase 12.T (Wave 3) | Your denial-rate visibility |
| Emission micro-eval | ❌ Phase 12.E (Wave 3) | Release-gate against canonical prompt set |
| 6 of 10 §4i tools (simulate / runEvalAssertions / debug-session / dispatchLog / revertToTurn / escalate) | ❌ Downstream AI consumer (separate sibling) | The AI consumer's introspection lens |
| Downstream AI consumer | ❌ Separate sibling / package family | Drives the loop you're substrate for |
| C# / VB authoring shapes | ❌ Phase 25 (Polyglot) | F# is production-ready; C#/VB deferred per design |

## Troubleshooting decision tree

```
"My emission failed validation"
  → RFC 8259 / wire-shape violation. Read the DecodeError message; common:
    trailing comma, unquoted key, single quotes, bare-string "kind".
    See "Wire-format contract" above.

"My tree was rejected"
  → ApplyError envelope. See ERROR_CODES.md decision tree.
    Pattern-match on error.code.

"The tree rendered but nothing's there"
  → Probably a Binding stuck in NotResolvedYet. Call fuaran.getNodeState
    with include=["CurrentState", "Bindings"] to confirm. If pending,
    re-poll; if SourceUnregistered, escalate.

"The tree rendered but it's the wrong shape"
  → Call fuaran.getRenderedDom to see actual geometry. Check whether your
    Children list ordering matches what you intended. Emit ReorderChildren
    to fix.

"I get NodeNotFound on a node I just inserted"
  → Race condition: the InsertChild op succeeded but the geometry probe
    hadn't observed it yet. Wait one cycle and re-query.

"I get DuplicateNodeId on InsertChild"
  → Two concurrent ops both used the same id. Generate a UUID-suffixed id
    instead of a stable prefix.

"PathNotSupportedYet"
  → the path is grammatical but outside the target kind's typed surface
    (the hint enumerates its top-level fields + nested patterns, e.g.
    Columns[i].Label). Re-emit against a listed path, or use a structural
    op (EditNode / ReplaceBinding / InsertChild) for what isn't listed.

"My closed-loop hit a budget cap"
  → The downstream AI consumer enforces per-iteration / token / wall-clock
    budgets. The escalate envelope tells you whether to retry with a
    smaller scope, ask the operator, or stop.
```

## Choosing between similar shapes (Phase 12.P additions)

The Phase 12.P additive shapes – `SummaryList`, `LabelValueRow`, `Heading.Variant`, and `Stack.Wrap` – close four small gaps where a pre-12.P author had no typed Fuaran equivalent for a common Feliz pattern. The shapes are easy to confuse with their nearest neighbours; the guidance below names the discriminator for each.

### SummaryList vs Dashboard vs Card

Since Phase 390, "Dashboard" and "Card" are **`Box` roles**, not distinct kinds – the guidance below
is a choice of `Box` `role` (+ `layout`) vs the still-distinct `SummaryList` kind.

| Use… | When… | Shape |
|---|---|---|
| **`SummaryList`** | The data is N rows × (label, value); the operator should read them as a list in one card. Divider rules between rows. Example: "Tax breakdown" – Income tax £7,486; Employee NI £4,128; Take-home £32,500. | One card, N rows. |
| **`Box` (`role: Dashboard`, `layout: Auto`)** | The data is N separate Metrics the operator should compare; each Metric is a hero. Cards auto-tiled in a responsive grid. Example: a sales dashboard showing Revenue / Pipeline / Win rate side-by-side. | N cards. |
| **`Box` (`role: Card`)** | The container is arbitrary content (not all rows of the same shape). Example: a card holding a paragraph + a button. | Mixed-content container. |

Rule of thumb: if the data shape is "N × (string label, numeric value)", use `SummaryList` of `LabelValueRow` children. If each value deserves a hero treatment with its own tone / trend / format / icon, use a `Box` (`role: Dashboard`) of `Metric` children. A `Metric` is one big number; a `LabelValueRow` is a row inside a list.

The "Pattern shift" framing (`flex justify-between` per row vs Metric cards) flags this distinction: pre-12.P, the Feliz→Fuaran translation forced a shape change because Fuaran had no row primitive. Post-12.P, the typed shape exists.

### Disclosure vs Card (Phase 65)

| Use… | When… | Shape |
|---|---|---|
| **`Disclosure`** | The section's open/closed state is part of the user's interaction model. Examples: "Show advanced", "Additional individual entitlements", FAQ entries, optional sub-form sections most users won't fill in. | One bordered container with a click-to-toggle summary; body hidden until opened. |
| **`Box` (`role: Card`)** | The content is always visible. Cards group related content; disclosures hide it behind a click. | One bordered container, always-visible body. |

Pair with `binding.state` + `OnToggle` when the host's model needs to know whether the section is open (URL deep-linking, server-persisted preferences). Otherwise leave `Open = Binding.Static(Some false)` and use `DefaultOpen` to seed the initial state – HTML's native `<details>` element handles the toggle without React state.

Multiple disclosures can be open simultaneously – distinct from `Tabs` (at most one active panel). Don't pick `Disclosure` to hack tab-like behaviour; pick `Tabs`.

### Heading variants

`HeadingSpec.Variant` is an axis on top of `Level`; the underlying `<h{Level}>` tag is preserved across variants so screen-reader semantics stay correct.

| Variant | Use for… | Visual |
|---|---|---|
| **`Standard`** (default) | Section titles inside a panel / card heading equivalent. | Bold, sized to `Level`. |
| **`Eyebrow`** | Above-title mini-labels in uppercase tracking-wide style. Tax-year banners, category eyebrows, breadcrumb-style labels. | Uppercase 12px, brand-toned, letter-spacing 0.04em, medium weight. |
| **`Caption`** | Small-print footnotes under a heading or Metric. "Employer NI excluded from take-home". | Italic 12px, subdued-toned. |
| **`Lead`** | Hero text – larger than `Standard` for visual hierarchy at the top of a page or section. | 20px semibold. |

Standard wins when in doubt – the variants are deliberately narrow shapes the AI should reach for only when the human-author intent is one of the four listed cases. Do NOT pick `Caption` for body paragraphs (use `Fuaran.markdown`); do NOT pick `Lead` to "make headings bigger" – bump `Level` instead.

### Stack wrap

Set `StackSpec.Wrap = true` on any horizontal stack whose children might exceed the viewport width when laid out side-by-side (on the wire this is a `Box` with `layout: { "$type": "Flex", "wrap": true }`). Three patterns this catches:

- **Chip strips** – a row of filter chips / scenario chips / tag chips that grows past the container.
- **Button rows** – multi-button toolbars where overflow should wrap onto a second line instead of clipping or producing a horizontal scrollbar.
- **Badge clusters** – multiple `Fuaran.badge` values for a record (tags, statuses) that should flow into multiple lines on narrow viewports.

The default `Wrap = false` matches the pre-12.P behaviour (single-line flex). Vertical stacks ignore `Wrap` – wrap is only meaningful for horizontal orientation.

### Irregular grids

`Fuaran.gridLayout` with `Cols: int` covers the "N equal-width columns" case (Tailwind-style 12-col layouts, dashboard tiles, button grids). For irregular column sizing – sidebars, master-detail panes, multi-column form layouts, mixed fixed-plus-flex columns – reach for `Fuaran.gridLayoutTemplated` and pass a verbatim `grid-template-columns` CSS string:

```fsharp
// Sidebar + main pane (1:2 ratio)
Fuaran.gridLayoutTemplated "g1" "1fr 2fr"
    { Defaults.gridLayout<Msg> with Children = [ sidebar; mainPane ] }

// Row labels + N data columns (heatmap shape)
Fuaran.gridLayoutTemplated "heatmap"
    "100px repeat(12, minmax(30px, 1fr))"
    { Defaults.gridLayout<Msg> with Children = cells }

// Responsive tile grid
Fuaran.gridLayoutTemplated "tiles"
    "repeat(auto-fit, minmax(150px, 1fr))"
    { Defaults.gridLayout<Msg> with Children = tiles }
```

The smart-ctor pre-populates `GridLayoutSpec.TemplateColumns`. Both `Fuaran.gridLayout` and `Fuaran.gridLayoutTemplated` emit a `Box` with `layout: { "$type": "Grid", "cols": N, "templateColumns"?: "…" }` and `role: Group` (Phase 390); `templateColumns` is omitted on the wire when `None`, and when it is `Some` the renderer short-circuits the `cols`-based `repeat(N, 1fr)` emission – `cols` is ignored in that case.

**Rule of thumb:** use `Fuaran.gridLayout` (typed `Cols`) when N equal-width columns suffices; reach for `Fuaran.gridLayoutTemplated` only when the sizing function (`1fr 2fr`, `100px repeat(...)`, `min-content max-content`, `auto-fit minmax`) can't be expressed by `Cols`. The unbounded-string escape pays a review tax – don't pay it for shapes the typed surface already covers.

**FUARAN046 advisory** catches the canonical regression – a `gridLayoutTemplated` with `templateColumns = "repeat(N, 1fr)"` (equivalent to the typed `Cols = N` shape). Warning only, doesn't fail builds, but signals the typed shape should be used instead.

See [`docs/migrations/67-grid-template-columns.md`](migrations/67-grid-template-columns.md) for the full authoring-pattern matrix.

### Grid cell pills

To render a `DataGrid` column's value as a tone-bearing status pill, author it with the postfix-pipe `Column.withPill` – **do not** hand-construct a `CellKindErased.Pill` (the row-erased layer is an internal boxing detail; `Fuaran.grid` reaches it for you via `Column.erase`). `withPill` reuses the column's existing value accessor for the pill label and takes a `'row -> ToneVariant` mapping the row to a tone:

```fsharp
Column.text "Status" (fun r -> r.Status)
|> Column.withPill (fun r -> if r.Healthy then ToneVariant.Success else ToneVariant.Critical)
```

The cell renders as `fuaran-grid-cell-pill fuaran-pill-<tone>` – the existing Pill rendering, no renderer change.

**Which of the two pills you want.** `withPill` takes a closure, and a closure does not
survive the wire: the tone rule erases to `"<closure>"`, so a decoded tree renders every row in
the same tone. Since Phase 750 there is a declarative twin, and it is the one to reach for
whenever the rule is *"this value gets this tone"*:

```fsharp
Column.text "Status" (fun r -> r.Status)
|> Column.withTonedPill
    "status"
    (Map [ "On time", ToneVariant.Success; "Delayed", ToneVariant.Warning ])
    ToneVariant.Subdued
```

Both render identically (a parity test pins that). The difference is what survives serialisation:

| | `withPill` | `withTonedPill` |
|---|---|---|
| Tone rule | any F# expression over the row | a declared value→tone map |
| On the wire | `{"$type":"Pill","labelFn":"<closure>","toneFn":"<closure>"}` – the rule is GONE | `{"$type":"TonedPill","field":"status","map":{…},"default":"Subdued"}` – the rule rides |
| A decoded / replayed / AI-emitted tree | every row the same tone | correct |

So: **`withTonedPill` unless the tone genuinely needs host computation** (a threshold against
live data, a cross-field predicate). `withPill` remains the override for exactly those cases —
the same closures-are-overrides-never-the-floor rule the rest of the vocabulary follows.

The row-erased advice is unchanged for both: author through `Column.*`, not by hand-constructing
a `CellKindErased` case.

## Self-checking before you emit (recommended pattern)

1. **Construct the typed tree** in F#-thinking (or your internal representation).
2. **Serialise to JSON** – emit the flat `kind.$type` object shape (see [`WIRE_FORMAT.md`](WIRE_FORMAT.md) §3.2), with spec fields beside `$type` and no `spec` wrapper.
3. **Validate against the schema** – run your candidate JSON through a Draft 2020-12 validator with [`schema.json`](../../wire-format-fixtures/schema.json), or re-encode via the canonical `CanonicalJson` encoder, to catch shape + lexical violations.
4. **Sanity-check NodeId uniqueness** – every `id` in your tree must be unique. Pre-emit linting via `PreEmitValidate.validate` catches this cheaper than the wire-side decode.
5. **Use `Defaults.X` overrides** – emit only fields that differ from the spec record's defaults. Smaller emissions = fewer ambiguities.
6. **Match the type for each field** – e.g. `Metric.Source` is `Binding<float>` and `Metric.Trend` is `Binding<float> option`; emit a number binding, not a string. The wire decoder rejects type mismatches.

## See also

- [`ERROR_CODES.md`](ERROR_CODES.md) – 0-latency error-code cheat sheet.
- [`TECHNICAL_GUIDE.md`](TECHNICAL_GUIDE.md) – human-author reference.
- [`WIRE_FORMAT.md`](WIRE_FORMAT.md) – the canonical, language-neutral wire-format specification.
- [`prompt-pack/`](prompt-pack/) – the copy-pasteable system-prompt + few-shot + schema + tool-defs pack (generated from the same corpus as this guide).
- [`HOST-INTEGRATION-CHECKLIST.md`](../HOST-INTEGRATION-CHECKLIST.md) – what a host must wire to drive the runtime tier.
- Source files in [`../src/Fuaran.UI/`](../src/Fuaran.UI/), [`../src/Fuaran.UI.Ops/`](../src/Fuaran.UI.Ops/), [`../src/Fuaran.UI.AiTools/`](../src/Fuaran.UI.AiTools/).
