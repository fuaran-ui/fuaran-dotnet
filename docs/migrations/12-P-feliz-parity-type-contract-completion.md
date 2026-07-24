# Phase 12.P – Fuaran Feliz-parity type-contract completion

**Phase:** `Fuaran/roadmap/phases/12-P-fern-feliz-parity-type-contract-completion.md`
**Date:** 2026-05-26
**Stability impact:** Additive – pre-1.0 minor add per [`STABILITY.md`](../../STABILITY.md). Every new field defaults to current behaviour; existing call sites compile unchanged. No major-version bump required.

## What changes

Phase 12.P closes the four pilot-app-revealed Feliz-parity gaps (High-E, High-F, High-G, Medium-I) by adding four additive shapes to the §4b canonical type contract:

1. **`StackSpec.Wrap : bool`** – defaults to `false`. When `true`, the renderer emits the `fuaran-stack-wrap` class so horizontal stacks wrap at narrow viewport widths instead of overflowing. Closes the "chip strip / button row / badge cluster" gap (High-E).
2. **`HeadingSpec.Variant : HeadingVariant`** – `Standard` / `Eyebrow` / `Caption` / `Lead`; defaults to `Standard`. The renderer emits the same `<h{Level}>` tag for every variant but appends a `fuaran-heading-{variant}` class so consumers can style uppercase mini-labels, italic small-print footnotes, and hero-text leads without overriding the heading's screen-reader semantics. Closes the "uppercase eyebrow / italic caption" gap (High-G + Medium-I).
3. **`DisplayKind.LabelValueRow of LabelValueRowSpec`** – single label-left / value-right row, baseline-aligned. Honours `StateBehaviour.OnLoading` / `OnError` slots the same way `KPI` does (the resolver runs against `Source`). The row primitive for `StatList`; can stand alone too. Closes the "row of stats inside a single card" primitive gap (High-F).
4. **`LayoutKind.StatList of StatListSpec`** – single-card container of label/value rows with divider rules between children and an optional section heading. Distinct visual shape from `Card` (no per-child padding, dividers between rows). Composes with `DisplayKind.LabelValueRow` to produce the "list of stats in one card" shape Feliz expresses as a hand-rolled `<div class="flex justify-between">` per row.

The four additions give every Feliz "list-of-stats-in-one-card" / "wrap-on-narrow-viewport" / "uppercase mini-label" / "italic footnote" pattern a typed Fuaran equivalent, so the Feliz→Fuaran translation produces visually-equivalent output (the §4l down-shift portability promise). Pre-12.P, attempting that translation forced a shape change – a row of KPI cards instead of a card of rows, an `<h6 class="...">` instead of a typed variant.

Six artefacts land:

1. **`StackSpec.Wrap`, `LayoutKind.StatList` + `StatListSpec`, `DisplayKind.LabelValueRow` + `LabelValueRowSpec`, `HeadingSpec.Variant` + `HeadingVariant` DU** – appended to [`src/Fuaran.UI/Types.fs`](../../src/Fuaran.UI/Types.fs).
2. **`Defaults.statList<'Msg>`, `Defaults.labelValueRow`, `Defaults.heading.Variant = HeadingVariant.Standard`, `Defaults.stack<'Msg>.Wrap = false`** – appended to [`src/Fuaran.UI/Defaults.fs`](../../src/Fuaran.UI/Defaults.fs).
3. **`Fuaran.statList`, `Fuaran.labelValueRow`, `Fuaran.heading`** smart constructors – appended to [`src/Fuaran.UI/Fuaran.fs`](../../src/Fuaran.UI/Fuaran.fs).
4. **Renderer arms** – `LayoutKind.StatList` + `DisplayKind.LabelValueRow` cases, `Stack.Wrap` class-suffix emission, `Heading.Variant` class-suffix emission. [`src/Fuaran.UI.Renderer/Render.fs`](../../src/Fuaran.UI.Renderer/Render.fs) + [`src/Fuaran.UI.Renderer/Theme.fs`](../../src/Fuaran.UI.Renderer/Theme.fs).
5. **Reference CSS** – `.fuaran-layout-stat-list`, `.fuaran-stat-list-heading`, `.fuaran-stat-list-body`, `.fuaran-label-value-row`, `.fuaran-label-value-row-label-block`, `.fuaran-label-value-row-label`, `.fuaran-label-value-row-help`, `.fuaran-label-value-row-value`, `.fuaran-label-value-row-emphasis`, `.fuaran-stack-wrap`, `.fuaran-heading-eyebrow`, `.fuaran-heading-caption`, `.fuaran-heading-lead`. [`src/Fuaran.UI.Renderer/content/fuaran-reference.css`](../../src/Fuaran.UI.Renderer/content/fuaran-reference.css). Rules sit outside the `:root` block so Phase 12.K's byte-for-byte regression test remains unaffected.
6. **Op-apply support** – `Fuaran.UI.Ops.Introspect.kindName` / `availableFields` / `availableBindingSlots` / `getChildren` / `withChildren` updated for the new kinds + the new fields; `Fuaran.UI.Ops.Apply.dispatchUpdateField` + `dispatchReplaceBinding` route the new shapes. The op-stream canonical-JSON encoder ([`src/Fuaran.UI.OpStream.Abstractions/CanonicalJson.fs`](../../src/Fuaran.UI.OpStream.Abstractions/CanonicalJson.fs)) and AI-tools introspection ([`src/Fuaran.UI.AiTools/Tools.fs`](../../src/Fuaran.UI.AiTools/Tools.fs)) gain the matching entries. Test cases land in [`src/Fuaran.UI.Ops.Tests/OpsApplyTests.fs`](../../src/Fuaran.UI.Ops.Tests/OpsApplyTests.fs) + [`src/Fuaran.UI.Tests/Tests.fs`](../../src/Fuaran.UI.Tests/Tests.fs).

## Diff highlights

### New types – `Fuaran.UI/Types.fs`

```fsharp
and StackSpec<'Msg> =
    { Orientation: Orientation
      Children: Node<'Msg> list
      Wrap: bool }                              // additive — default false

and [<RequireQualifiedAccess>] LayoutKind<'Msg> =
    | Dashboard of DashboardSpec<'Msg>
    | Stack of StackSpec<'Msg>
    | ...
    | Stepper of StepperSpec<'Msg>
    | StatList of StatListSpec<'Msg>            // additive

and StatListSpec<'Msg> =
    { Heading: TextSource option
      Children: Node<'Msg> list }

and [<RequireQualifiedAccess>] DisplayKind<'Msg> =
    | Heading of HeadingSpec
    | ...
    | Skeleton of SkeletonSpec
    | LabelValueRow of LabelValueRowSpec        // additive

and LabelValueRowSpec =
    { Label: TextSource
      Source: Binding<float>
      Format: CellFormat
      Emphasis: bool
      Help: TextSource option }

and [<RequireQualifiedAccess>] HeadingVariant =
    | Standard
    | Eyebrow
    | Caption
    | Lead

and HeadingSpec =
    { Level: int
      Text: TextSource
      Variant: HeadingVariant }                 // additive — default Standard
```

### New smart constructors – `Fuaran.UI/Fuaran.fs`

```fsharp
let statList (id: string) (spec: StatListSpec<'Msg>) : Node<'Msg> = ...
let heading (id: string) (spec: HeadingSpec) : Node<'Msg> = ...
let labelValueRow (id: string) (spec: LabelValueRowSpec) : Node<'Msg> = ...
```

## Four worked translation examples (Feliz → Fuaran, drawn from the pilot app)

These mirror the patterns the pilot-app walkthrough flagged as forcing a shape change pre-12.P. Each before/after pair preserves the data flow + the visual shape.

### Example 1 – uppercase tax-year banner (High-G)

**Feliz (pre-Fuaran):**
```fsharp
Html.div [
    prop.className "text-xs font-medium uppercase tracking-wide text-blue-700"
    prop.text $"Tax year {year}/{year + 1}"
]
```

**Fuaran (post-12.P):**
```fsharp
Fuaran.heading "year-banner" {
    Defaults.heading with
        Level = 6
        Text = TextSource.Literal $"Tax year {year}/{year + 1}"
        Variant = HeadingVariant.Eyebrow
}
```

### Example 2 – per-row income breakdown (High-F)

**Feliz (pre-Fuaran, one row of many):**
```fsharp
Html.div [
    prop.className "flex justify-between py-2"
    prop.children [
        Html.span [ prop.text "Income tax" ]
        Html.span [ prop.text "£7,486" ]
    ]
]
```

**Fuaran (post-12.P):**
```fsharp
Fuaran.labelValueRow "row-income-tax" {
    Defaults.labelValueRow with
        Label = TextSource.Literal "Income tax"
        Source = binding.query "incomeTax" (fun r -> r.IncomeTax)
        Format = format.currency "GBP"
}
```

### Example 3 – list of stats inside one card (High-F + composition)

**Feliz (pre-Fuaran):**
```fsharp
Html.section [
    prop.className "rounded-lg border bg-white"
    prop.children [
        Html.header [ prop.className "p-3 border-b"; prop.text "Tax breakdown" ]
        Html.div [ prop.className "px-4"
                   prop.children [ row "Income tax" incomeTax
                                   row "Employee NI" employeeNi
                                   row "Take-home" takeHome ] ]
    ]
]
```

**Fuaran (post-12.P):**
```fsharp
Fuaran.statList "tax-breakdown" {
    Defaults.statList with
        Heading = Some (TextSource.Literal "Tax breakdown")
        Children = [
            Fuaran.labelValueRow "row-income-tax" { ... }
            Fuaran.labelValueRow "row-employee-ni" { ... }
            Fuaran.labelValueRow "row-take-home" {
                Defaults.labelValueRow with
                    Label = TextSource.Literal "Take-home"
                    Source = binding.query "takeHome" id
                    Format = format.currency "GBP"
                    Emphasis = true   // bolds the total row
            }
        ]
}
```

### Example 4 – wrapping scenario-chip strip (High-E)

**Feliz (pre-Fuaran):**
```fsharp
Html.div [
    prop.className "flex flex-wrap gap-2"
    prop.children [ for scenario in scenarios -> chipFor scenario ]
]
```

**Fuaran (post-12.P):**
```fsharp
Fuaran.stack "scenario-strip" {
    Defaults.stack with
        Orientation = Horizontal
        Wrap = true
        Children = [ for scenario in scenarios -> chipFor scenario ]
}
```

## `Fuaran.UI.Ops` op-apply additions

The four additive shapes plug into the apply engine via the standard surface:

- **`UpdateProp` paths:**
  - `Stack.Wrap` – boolean field, additive.
  - `Heading.Variant` – `HeadingVariant` DU.
  - `LabelValueRow.{Label, Source, Format, Emphasis, Help}` – five top-level fields.
  - `StatList.Heading` – `TextSource option`. `StatList.Children` returns `PathNotSupportedYet` per the v1 convention (use a structural op instead).
- **`ReplaceBinding` slots:**
  - `LabelValueRow.Source : Binding<float>` – joins the existing per-Kind binding-slot surface alongside `KPI.Source`, `Sparkline.Source`, `Progress.Fraction`.
- **`InsertChild` / `RemoveNode` / `ReorderChildren` / `MoveNode`:**
  - `LayoutKind.StatList` joins `getChildren` / `withChildren` so structural ops over its `Children` work the same way as every other layout.

The §4d AI-recovery hint payloads (`hint.available_fields`, `hint.nodes_with_<field>_field`) update automatically – the dispatch tables are the single source.

## Stability impact

Additive – no breaking change. Every new field defaults to current behaviour:

| Field / case | Default | Pre-12.P behaviour preserved |
|---|---|---|
| `StackSpec.Wrap` | `false` | Pre-12.P stacks did not wrap; default false means no class fragment is emitted. |
| `HeadingSpec.Variant` | `HeadingVariant.Standard` | Pre-12.P headings emitted bare `fuaran-heading`; Standard emits no class suffix. |
| `LayoutKind.StatList` | n/a (new case) | New layout – no pre-12.P call site can pattern-match against it. |
| `DisplayKind.LabelValueRow` | n/a (new case) | Same – additive case. |
| `Defaults.statList<'Msg>` | empty heading + children | Mirrors `Defaults.card`'s shape. |
| `Defaults.labelValueRow` | empty label + sentinel source | Same `NotProvidedSentinel` shape as `Defaults.kpi.Source` so the renderer's `OnLoading` slot fires when a consumer forgets the override. |

The new DU cases will trigger non-exhaustive match warnings in hand-rolled introspection code that pattern-matches over `LayoutKind` / `DisplayKind` without a `_ -> ...` arm – that's a discoverable signal (a code-reader sees they need to think about the new kind), not a defect. The `Fuaran.UI`-internal pattern-match sites are all updated in this phase.

Pre-1.0 minor add per [`STABILITY.md`](../../STABILITY.md); no major-version bump required.

## Rollback

The four shapes are independent – each can be rolled back in isolation by reverting:

1. **`StackSpec.Wrap`** – delete the field from `Types.fs` + `Defaults.fs`, delete the `Wrap = ...` line from `Defaults.stack`, delete the `wrap` conditional from `Render.fs`'s `Stack` arm + the matching `.fuaran-stack-wrap` rule + the `availableFields` entry + the `updateStack "Wrap"` branch.
2. **`HeadingSpec.Variant`** – delete the field + the `HeadingVariant` DU from `Types.fs`, delete the default from `Defaults.fs`, delete the variant-suffix logic from `Render.fs`'s `Heading` arm + the matching `.fuaran-heading-{eyebrow,caption,lead}` rules + the `availableFields` entry + the `updateHeading "Variant"` branch.
3. **`DisplayKind.LabelValueRow`** – delete the DU case + spec from `Types.fs`, delete `Defaults.labelValueRow`, delete `Fuaran.labelValueRow`, delete the `renderLabelValueRow` function + the `renderDisplay` arm + the matching CSS rules + every Introspect / Apply / Tools.fs / CanonicalJson.fs entry.
4. **`LayoutKind.StatList`** – same shape as `LabelValueRow`; delete the DU case + spec from `Types.fs`, delete `Defaults.statList`, delete `Fuaran.statList`, delete the `renderLayout` arm + the matching CSS rules + every Introspect / Apply / Tools.fs / CanonicalJson.fs entry.

The op-stream canonical JSON encoding changes (Stack.wrap field, Heading.variant field, two new cases) shift the hash for trees containing those shapes. Existing hash-chained streams compiled pre-12.P contain no Stack with wrap=true, no Heading with Variant, no StatList, and no LabelValueRow – their encoded forms are unaffected if the encoder reads pre-12.P trees (Wrap=false / Variant=Standard / no new cases). Phase 12.P-emitting trees that round-trip through the encoder are stable across encoder versions ≥ 12.P.

## Consumer adoption

The four additive shapes are pure SDK additions. Consumer impact:

- **Platform-SDK / OSS hosts** – N-A. This phase pre-dates the language tier's public availability.
- **Regulated-data host app** – no behaviour change at upgrade time; new shapes available for any pages that want to express label/value row patterns.
- **Pilot app** – the worked-example re-validation (the High-E/F/G / Medium-I gaps that motivated this phase) lands as a commit in that app's own repo, not part of this phase's commit. Specifically: `breakdownBodyFuaran` swaps its `Fuaran.kpi`-as-row pattern for `Fuaran.statList` of `Fuaran.labelValueRow`; the tax-year banner becomes `Variant = Eyebrow`; the employer-NI footnote becomes `Variant = Caption`; the scenario-chip strip becomes `Stack` with `Wrap = true`. Take-home keeps the `Fuaran.kpi` shape (it's a hero, not a row).
- **Document-conversion host portal** – no behaviour change at upgrade time; the new shapes match common patterns its tile-and-row layouts produce.
- **Portal** – N-A until the downstream AI consumer ships.

## See also

- [`HOST-STYLING-CHECKLIST.md`](../../HOST-STYLING-CHECKLIST.md) – the canonical class-fragment + variable-surface contract (updated in this phase).
- [`docs/AI_AUTHORING_GUIDE.md`](../AI_AUTHORING_GUIDE.md) – author-facing patterns + when-to-use guidance (updated in this phase).
- [`docs/migrations/12-H-host-styling-contract.md`](12-H-host-styling-contract.md) – the variable surface the new kinds consume.
- [`docs/migrations/12-K-theme-as-api.md`](12-K-theme-as-api.md) – the typed `Theme` record + the design-deviation framing this phase mirrors.
