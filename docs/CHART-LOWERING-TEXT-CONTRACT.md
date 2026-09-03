# The chart lowering's text contract

> Phase 1143, 2026-09-02. What a chart's `TextSource`-typed fields do when a `Chart` node is
> lowered to a `Drawing`. Host-neutral: it states what **every** conformant lowering does, not
> what one implementation happens to do. Pairs with `CHARTS-DRAWING-PRIMITIVE-DESIGN.md` (the
> `Drawing` primitive's own design) and the `chart-lowering/*` fixture family, which pins it.

`ChartSpec` declares four fields whose type is `TextSource`, and therefore whose value may be a
`Literal`, a `Bound` binding, or an `I18n` key:

| Field | What it names |
|---|---|
| `Title` | the chart's own name — drawn as the emphasised heading, and carried as the drawing's accessible `<title>` |
| `Subtitle` | the muted qualifier under it (Phase 878) |
| `XTitle` | the category-axis name (Phase 878) |
| `YTitle` | the value-axis name (Phase 878) |

The lowering is a **pure, total function from a spec and rows to a canonical `Drawing`**, run on
every host, headless included. Text is the one part of a chart whose value may not be known at that
moment. This document says what happens to it.

## The contract

**1. Carry, do not resolve.** All four fields cross the lowering as `TextSource` and reach the
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

**4. Truncation applies to the `Literal` arm alone.** The subtitle and both axis titles are bounded
to the extent they run along — `truncateToWidth` over the drawn glyphs. The text behind a `Bound`
or `I18n` arm is not known at lowering time, so it passes through untruncated and may overrun. That
is the honest boundary: a visible overrun is a fact the reader can see, where a measurement taken
against text that is not the text drawn is silently wrong. (The visible `Title` is not bounded on
any arm — it has the canvas width and its own line.)

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
