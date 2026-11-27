# The chart lowering's text contract

> Phase 1143, 2026-09-02. What a chart's `TextSource`-typed fields do when a `Chart` node is
> lowered to a `Drawing`. Host-neutral: it states what **every** conformant lowering does, not
> what one implementation happens to do. Pairs with `CHARTS-DRAWING-PRIMITIVE-DESIGN.md` (the
> `Drawing` primitive's own design) and the `chart-lowering/*` fixture family, which pins it.

`ChartSpec` declares five `TextSource`-typed slots, and therefore five whose value may be a
`Literal`, a `Bound` binding, or an `I18n` key:

| Slot | What it names |
|---|---|
| `Title` | the chart's own name — drawn as the emphasised heading, and carried as the drawing's accessible `<title>` |
| `Subtitle` | the muted qualifier under it (Phase 878) |
| `XTitle` | the category-axis name (Phase 878) |
| `YTitle` | the value-axis name (Phase 878) |
| `Annotations[i]`'s `label` | the optional name on a data-addressed annotation (Phase 1490 — §4l) |

The fifth is the first that is not a FIELD of `ChartSpec`: it rides a member of the annotations
list, so a chart carries as many of them as it declares annotations. Nothing else about it is
special, which is the point — the clauses below are stated over slots rather than over fields
precisely so a new one inherits them rather than re-deciding them.

The lowering is a **pure, total function from a spec and rows to a canonical `Drawing`**, run on
every host, headless included. Text is the one part of a chart whose value may not be known at that
moment. This document says what happens to it.

## The contract

**1. Carry, do not resolve.** All five slots cross the lowering as `TextSource` and reach the
emitted `Shape.Label` (and the drawing's `Title`) as `TextSource`. The lowering never asks what a
`Bound` arm currently reads or what an `I18n` key expands to. Resolution is the renderer's, at
render time, where every arm resolves and where the host holds the binding sources and the
catalogue.

**2. Never drop.** A non-`Literal` arm is not discarded, and no host substitutes a fallback for
one. An authored meaning that reaches the wire reaches the picture.

**3. Layout reserves space by PRESENCE, never by resolved content.** The top margin reserves the
subtitle's line when a subtitle is *present*; the left margin reserves the rotated y-title's line
when a y-title is *present*; the display-unit slot is suppressed when a subtitle is *present*. No
margin, band, or suppression rule reads the text. This is what makes clause 1 affordable: a
drawing's geometry is a function of the spec's *shape*, so it is identical on every host and stable
under a binding that changes, and two hosts with different live state lower the same wire tree to
the same bytes.

**4. Measuring applies to the `Literal` arm alone**, and it has two forms — TRUNCATION and the FIT
GATE. The subtitle and both axis titles are bounded to the extent they run along
(`truncateToWidth` over the drawn glyphs); an annotation label is instead admitted or SUPPRESSED
whole by `fitsBox`, on Phase 881's rule — never clipped, never overlapped, never moved onto a mark.
Both forms stop at the same boundary: the text behind a `Bound` or `I18n` arm is not known at
lowering time, so it passes through untruncated and UNGATED, and may overrun. That is the honest
answer either way — a visible overrun is a fact the reader can see, where a measurement taken
against text that is not the text drawn is silently wrong. (The visible `Title` is not bounded on
any arm — it has the canvas width and its own line.)

**A suppressed label never suppresses what it labels.** The annotation still draws, and it still
reaches the accessible summary. That is what keeps suppression a LAYOUT decision rather than a
silent loss of authored content, and it is the reason clause 2 survives a gate that can refuse to
draw something: nothing was dropped, one rendering of it was declined.

**5. The axis-title fallback is a `Literal` the lowering mints, and it applies to ABSENCE only.**
An axis with no declared title falls back to its capitalised field name, so an axis is never
nameless. A *declared* title — of any arm — always wins; the fallback is never reached because a
title "could not be resolved". (Phase 882's rule stands unchanged: a self-evident date axis
suppresses its **default** x-title, never a declared one.)

**6. The generated accessible summary never contains the title.** The summary (Phase 921) is a
lowering rule like every other one here — canonical strings only, no host locale, no clock — so it
must be byte-identical on every host, and a title is not. The renderer composes the resolved title
in front of it at the root instead, so the announced string is `"<title>. <summary>"` for every arm.
The same reasoning is why a units statement is never concatenated into a title: a rule expressible
only for `Literal` is not a rule.

## The palette beside the text (Phase 2482)

The series palette is not a text slot, but it answers the same question this document answers
for text — what the lowering is handed and what it decides — so its rule sits here.

**7. The series palette is host-installed and validated, never a wire field.** A chart's series
colours are literal hex in the drawing, chosen by the chart style the HOST installs from its
theme, not by anything the `ChartSpec` carries. A palette is validated before it is installed:
the lightness band and chroma floor per slot, on both the light and the dark chart surface, and
the colour-vision and normal-vision separation floors over ADJACENT slots. A palette that fails
is refused with the failing slot or pair named, and the shipped default is installed in its
place. A failing colour is never drawn. With nothing installed, the lowering draws the shipped
default, which is what every `chart-lowering/*` golden pins. The `chart-lowering/themed/`
subfamily pins a non-default palette for a host that ships an install seam. Its input names the
palette as a harness-only `palette` selector, which is a style choice like `axisUnitMode` and
never part of the wire. The decision and its reasons are D23 in `DECISIONS.md`.

## The data table beside the drawing (Phase 2776)

A lowered chart is an image to assistive technology: `role="img"` with a title and a summary of at
most 320 characters (clause 6, Phases 921 and 1494). The summary is not the data. So the lowering
yields, beside the drawing, a **table model** of the values its marks were drawn from, and the
renderers offer it as a real table behind a "View as table" disclosure that the drawing names in
`aria-details`. It is a lowering rule like the clauses above: a pure function of the spec and the
rows, run on every host, and pinned by the `chart-lowering/table-model/` subfamily.

**8. The caption is a text slot, and clause 1 applies.** It is the chart's `Title`, carried
unresolved, so a `Bound` or `I18n` title resolves at render time exactly as the drawing's `<title>`
does. A chart with no title gets a `Literal` the lowering mints from its kind: "Bar chart data",
"Stacked area chart data", "Heatmap chart data". Clause 5's posture: the fallback answers
absence, never an unresolved title.

**9. The table holds what the marks read, and nothing else.**

- **Columns** are the fields the marks read, each under its declared name: the x field, then each
  series in order (the derived series of §3.6.29 when `yFields` is empty), then, when the kind
  reads them, the interval's low and high ends, the size and the path order (Phase 2654). A
  heatmap's columns are its x field, its row field and its value field. A field that orders the
  rows (`sortBy`) or withholds them (`withheldField`) is not plotted, so it is not a column: its
  effect is the row order and the "withheld" cells.
- **Rows** are the rows the chart plotted, one per row, in the order the marks were drawn, so after
  `sortBy`. A heatmap's rows are one per cell: the first row of each (x, row) pair, in data order,
  which is the row the cell draws.
- **Cells.** A value is its exact readout through the chart's declared `valueFormat`: unscaled by
  any display unit, the currency symbol kept, at the fewest decimals that read it exactly unless
  the format declares them. That is the hover tip's text (Phase 883), not the tick's: a tick
  answers "roughly where", a table cell "what exactly". The x cell is the category axis's own
  projection of the row (an ISO date on a temporal axis); a size or a path order is a plain
  number; an interval's ends are the pair as drawn, normalised low to high. A withheld value
  reads "withheld" and a missing one is an empty cell, never a zero. The path order is a
  position, so a withheld row keeps it.
- **The cost caps apply** through the same admission the drawing passes (Phase 790). A chart the
  caps refuse is drawn as the refusal and offers no table; a chart with no rows has no values to
  read and offers none. A table holds at most 1,000 rows, the bound grid's limit, so the two tables
  a reader meets are cut at the same place; `rowCount` says how many rows there were, and the
  renderer states "Rows 1–1000 of n" under a cut table.

**The golden's form.** Each case in `chart-lowering/table-model/` is a byte copy of an existing
chart-lowering input (`<name>.input.json`) beside a sidecar `<name>.table.json`: the table model in
canonical JSON — `{"caption":<TextSource>,"columns":[…],"rowCount":n,"rows":[[…],…]}`, the caption
in the `TextSource` canonical wire form (a bare string for a `Literal`), every cell a string — or
`null` where the caps refuse the chart. The existing top-level `*.expected.json` goldens are not
touched: the hosts that read the top-level family compare those bytes and do not read a
subdirectory, and Phase 2804 adds their readers.

**10. Whether the table is offered is host policy, never wire.** `RenderPolicy.ChartTable` is
`Offer` by default; a host that supplies its own alternative sets it `Off`, and every render is then
byte-identical to one before the table existed. The markup the F# renderers emit, the rules it must
pass (`a11y-contract.json`'s `chartTable` section) and the reasons for each choice — a native
`<details>`, `aria-details` on the drawing naming the disclosure rather than the table inside it —
are in `ElementPlan.ChartTablePlan`.

## Why this rather than the alternatives

**Resolving at the bridge** was the shape two hosts had reached for, and it fails on clause 3: what
the bridge resolves is baked into the geometry, so a chart's layout becomes a function of live
binding state and two hosts disagree about the same wire tree. `I18n` cannot be honest there at all
— the lowering carries no catalogue.

**Dropping the non-literal arms** — the behaviour that motivated this phase — is worse than either.
It loses content the author declared, silently: an `I18n` title vanishes from a localised chart and
a `Bound` axis name is replaced by a capitalised column name, with nothing anywhere saying so.

## Host obligations

A host whose lowering input type declares these fields as plain strings **does not conform** — the
type makes clause 1 unrepresentable, and clause 2 unachievable, at the bridge. The field type is
`TextSource`; a host whose wire vocabulary spells `TextSource.Literal` as a bare string (§16) may
accept that spelling, since it *is* the canonical literal form, but must accept the other arms too.

A host's own drawing renderer is where clauses 1 and 4 are honoured or lost: it resolves the label's
`TextSource` at render time, and it is what makes an untruncated bound title visible rather than
missing.

## What pins it

`chart-lowering/bar-bound-i18n-titles` in the shared fixture corpus: one chart carrying a `Bound`
title, an `I18n` subtitle, a `Bound` x-title and an `I18n` y-title, with the expected `Drawing`
carrying all four arms through unresolved and untruncated. Every host certifies against it. Until
that fixture existed, the divergence this document settles survived unseen on a lowering surface
that was otherwise at byte parity.

`chart-lowering/line-reference-labelled-bound-label` pins the fifth slot, and it is that fixture's
twin rather than a repetition of it: the annotation label is `Bound`, so the golden shows it carried
through unresolved AND admitted without being measured. The two halves are separable — a host could
carry the arm and still fit-gate it against text it cannot read — so the fixture that pins clause 1
here is also the one that pins clause 4's boundary.
