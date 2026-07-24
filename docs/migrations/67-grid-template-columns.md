# Phase 67 – `GridLayoutSpec.TemplateColumns` irregular-grid escape

Shipped 2026-05-30. Pre-1.0 minor add – additive optional field on an existing spec record. Pre-Phase-67 callers see no behavioural change.

## What changed

`GridLayoutSpec` was a 12-column-class shape with one knob: `Cols: int`. The renderer emitted `grid-template-columns: repeat({Cols}, 1fr)`, which covered the "N equal-width columns" case and forced every other shape – sidebars, master-detail panes, multi-column form layouts, the pilot app's heatmap row-label + N-data-columns grid – to escape into Feliz.

Phase 67 closes the gap with an additive optional field:

```fsharp
and GridLayoutSpec<'Msg> =
    { Cols: int
      Children: Node<'Msg> list
      TemplateColumns: string option }   // NEW — Phase 67
```

When `TemplateColumns` is `Some s`, the renderer emits `s` verbatim to `grid-template-columns` and ignores `Cols`. When `None` (the default), the existing `repeat({Cols}, 1fr)` emission applies – pre-Phase-67 fixtures encode byte-identical.

Two construction paths:

```fsharp
// Pre-Phase-67 shape — unchanged.
Fuaran.gridLayout "g1"
    { Defaults.gridLayout<Msg> with
        Cols = 12
        Children = [ ... ] }

// Phase 67 escape — verbatim grid-template-columns string.
Fuaran.gridLayoutTemplated "g1"
    "100px repeat(5, minmax(30px, 1fr))"
    { Defaults.gridLayout<Msg> with
        Children = [ ... ] }
```

## Why a string escape and not a typed CSS-grammar DU

Phase 67 deliberately picked the string escape over a typed `GridTrackList` DU shape (`Auto | Fr of float | Px of int | MinMax | Repeat | MinContent | MaxContent`). The trade-off:

| | Typed DU | String escape |
|---|---|---|
| AI emission space | Finite, typed | Unbounded CSS |
| Validator reasoning | Possible (over/under-constrained) | Structural only (FUARAN046) |
| Implementation cost | Substantial DU + encoder/decoder | One optional field |
| CSS-grammar coverage | Partial (drifts behind CSS) | Total |
| Migration cost | DU evolves with CSS spec | Free |

The pragmatic floor wins for the first pass: get the gap closed, validate against the pilot app's heatmap shape, then revisit with a typed DU in a Phase 67.B follow-up if the unbounded-string emission becomes a real eval-quality issue. The pattern mirrors `NodeKind.Custom` – a typed escape hatch for the rare cases that need it.

## When to escape – rule of thumb

Use `Fuaran.gridLayout` (the typed shape) when:

- N equal-width columns suffices.
- The grid is a stable shape across the page (12-column layouts, dashboard tiles, button grids).

Reach for `Fuaran.gridLayoutTemplated` when:

- Columns have **different widths** – `1fr 2fr` (sidebar + main), `auto 1fr auto` (icon + body + actions).
- Mixing **fixed + flex** sizes – `100px repeat(5, 1fr)` (row labels + N data columns), `200px 1fr` (sidebar + main).
- **Content-driven** sizing – `min-content max-content` (form-label + form-input pairs).
- **Auto-fit / auto-fill** – `repeat(auto-fit, minmax(150px, 1fr))` (responsive tile grid).

Don't reach for `gridLayoutTemplated` when the typed `Cols: int` shape works – the FUARAN046 advisory catches the canonical `repeat(N, 1fr)` regression (equivalent to the typed shape) at build time.

## Authoring patterns by shape

| Shape | When to use | Example |
|---|---|---|
| `1fr 2fr` | Sidebar + main pane | `Fuaran.gridLayoutTemplated "g" "1fr 2fr" spec` |
| `auto 1fr auto` | Icon + body + action button | `Fuaran.gridLayoutTemplated "g" "auto 1fr auto" spec` |
| `100px repeat(N, 1fr)` | Row labels + N data columns | `Fuaran.gridLayoutTemplated "g" "100px repeat(12, minmax(30px, 1fr))" spec` |
| `min-content max-content` | Form label-value pairs (content-sized) | `Fuaran.gridLayoutTemplated "g" "min-content max-content" spec` |
| `repeat(auto-fit, minmax(150px, 1fr))` | Responsive tile grid | `Fuaran.gridLayoutTemplated "g" "repeat(auto-fit, minmax(150px, 1fr))" spec` |

## Anti-patterns

### Using `TemplateColumns` for what `Cols` covers

```fsharp
// ❌ FUARAN046 — `repeat(N, 1fr)` is the typed-shape default.
Fuaran.gridLayoutTemplated "g" "repeat(5, 1fr)" spec

// ✅ Use the typed shape.
Fuaran.gridLayout "g" { spec with Cols = 5 }
```

### Mixing `auto` columns with `1fr` columns

`auto` columns size to their content; `1fr` columns size to the remaining space. The first render before content loads (skeleton state, async data) can produce a visible reflow when the `auto` column's content settles. Phase 67's validator advisory doesn't catch this structurally (it's content-dependent). Prefer `min-content` / `max-content` when the intent is "size to content" – they're stable across load states.

### Hand-rolling responsive breakpoints inside the template string

`grid-template-columns` doesn't carry media-query semantics. If you need different shapes at different viewport widths, that's a `Binding<string>` of the template (delegating breakpoint resolution to the consumer's update loop) or a host-level CSS rule keyed on a layout-observer flag (Phase 12.G). Both are out of Phase 67 scope.

### Conflating with the inline-style cell-binding gap (Phase 62 gap-6)

Phase 67 types the **container's** `grid-template-columns`. The per-cell `style.backgroundColor` binding that the pilot app's heatmap also needs is a parallel concern (the "Phase 62 gap-6" deferred-decision). Closing one doesn't close the other – the heatmap's outer layout is now typed (Phase 67), but the per-cell colour gradient stays in the `NodeKind.Custom` escape until that gap closes separately.

## Wire format

The `templateColumns` key is **optional** in the JSON wire form. Pre-Phase-67 fixtures decode byte-identical against the Phase 67 decoder (the key is absent → `TemplateColumns = None`). Phase 67 fixtures encode the key only when `Some`, so existing canonical fixtures continue to encode unchanged.

Key order within the `Grid` spec object is `children < cols < templateColumns` (lexicographic), preserving the canonical-key-sorted-by-name invariant declared in `CanonicalJson.fs`.

## Op-stream apply surface

The op-stream's `UpdateProp` op carries `(NodeId, "TemplateColumns", value)` where `value` is either a `string option` or a raw `string` (the apply engine accepts both shapes – the latter is sugar that wraps in `Some`). Operators that want to switch a grid back to the typed shape send `UpdateProp(id, "TemplateColumns", (None : string option))`.

## Forward path – typed DU follow-up

If the string-escape shape produces a real eval-quality regression (AI emits malformed CSS, validator can't catch structurally invalid shapes the orchestrator should know about, the unbounded-string review tax exceeds the typed-DU implementation tax), Phase 67.B will add the typed `GridTrackList` DU as a sibling field. The string escape stays – it'd remain the escape hatch for shapes outside the typed grammar – but the typed shape becomes the recommended path. No decision yet; the rabbit-hole-avoidance verdict stands until that signal materialises.

## See also

- [`AI_AUTHORING_GUIDE.md`](../AI_AUTHORING_GUIDE.md) "Irregular grids" subsection.
- [`STABILITY.md`](../../STABILITY.md) "Versioning policy" – additive-field rule for spec records.
- Workspace roadmap `Phase 67` – phase body and acceptance criteria.
