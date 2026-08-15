# Pack slimming census — Phase 834 (fuaran)

**Date:** 2026-08-15. **Charter:** `roadmap/phases/834-pack-slimming-no-legacy.md` —
the pack prefix is the dominant depth-cost driver (Tier-D cohort2 r0, 2026-08-14);
operator release 2026-08-14: no installed base needs teaching for extinct behaviours.

**Method.** Tokens are estimated as `chars / 4` (a consistent estimator beats a precise
one — the same divisor prices the baseline and the post-slim total, so the DELTA is
honest). `system-prompt.md` is split at every `##` / `###` heading; "gen" is the
content inside the three generated marker families (`fuaran:example`,
`fuaran:required-fields`, `fuaran:enum-vocab` — corpus/schema-derived, drift-checked by
`docs/tools/authoring-pack.fsx --check`, and NOT hand-cuttable except by cutting a whole
section); "prose" is the authored remainder. `few-shot.jsonl` is counted per entry
(line length). Evidence citations are rows of
the evaluation harness's demand log (`CAPABILITY-DEMAND-LOG.md`, cited by
date/cluster) and its expressibility census (`EXPRESSIBILITY-CENSUS.md`).

## Baseline (pre-slim)

Measured AFTER the Phase-834 prep regen (`f8434ff` — the fixtures corpus had moved at
fuaran#818 without a pack regen; the drift gate must be clean before any cut is sized).

| File | Chars | ~Tokens |
|---|---|---|
| `system-prompt.md` | 87,873 | 21,968 |
| `few-shot.jsonl` | 21,920 | 5,480 |
| **Pack total (taught prefix)** | **109,793** | **27,448** |

Of `system-prompt.md`: generated blocks ≈ 42,964 chars (~10,741 tok); authored prose
≈ 44,137 chars (~11,040 tok, incl. newline accounting).

## system-prompt.md — per-section census and verdicts

| Lines | Section | ~Tok (gen / prose) | Verdict |
|---|---|---|---|
| 1–16 | Preamble | 198 (0/198) | **KEEP** — the emission contract (one JSON document, no prose). |
| 17–79 | The canonical wire shape | 558 (130/427) | **KEEP** — caution list (the opening). |
| 80–225 | Containers nest under `children` | 1,148 (757/391) | **KEEP** — the one layout example; the direction-as-field line is a verified flip (2026-08-10 005 row: pack `ade366b`, 2/2 clean). |
| 226–254 | Actions on inputs | 157 (92/64) | **KEEP**. |
| 255–266 | Submitting a form — `Call` posts the fields | 183 (0/183) | **KEEP** — caution list (the submit-`Call` line, new, Phase 820). The empty-`Chain` warning still steers canonical (an empty chain still goes nowhere). |
| 267–306 | Editing an existing tree | 403 (55/348) | **KEEP** — caution list (Editing/TreeOp); already slimmed once (`d70fd33` retired positional teaching). |
| 307–369 | The node kinds — required fields | 1,031 (757/273) | **KEEP** — generated table (caution list). |
| 370–467 | Numbers vs text: `Metric` vs `Fact` | 1,086 (45/1,041) | **CUT (a), partial** — the arithmetic wrong-then-right exact-example pair (lines 395–413, ~800 chars). Evidence: demand-log 2026-08-09 008-arithmetic row — teaching-proof at BOTH treatments (prose `a89e84c` AND the exact pair `602ada7`: 7/9 base cells re-emitted `178 / 180` verbatim with the pair in-prompt), and **generation-scoped 2026-08-11**: gpt-5.6-sol@low 4/4 clean, terra@low 4/4 clean — a gpt-4o legacy behaviour no current family emits. The one-line "Compute every number you emit" hard rule (lines 389–393) is KEPT (didactic-adjacent, near-free). The formatted-quantity ("1h 20m") pair is KEPT — it is the `e4c3a2a` mechanism that extinguished the WRONG_TYPE cluster (2026-08-09 row: 0/4 → 2/2), never generation-scope-verified extinct. The Badge-vs-Fact prose + Badge example are KEPT — caution list (0/6 → 6/6 flip, 2026-08-02). **CUT (c), one sentence** — "There is no duration format — a pre-formatted duration is always a `Fact`" (line 431–432): falsified by shipped vocabulary (`Format.Duration` / `CellFormat.Duration`, Phase 819 family, taught at lines 1662–1669 per pack `7ce7a9c`; verified flipping — 817 sweep, stress-009 pass ×2). |
| 468–879 | Selected / pre-selected / derived state — the three idioms | 3,186 (2,052/1,133) | **CUT (b), partial** — the Callout-body prose paragraph ("That includes prose slots…", lines 502–508, ~450 chars, `61a9c01`): teaching attempted and FAILED twice — flip-4 prose-only 1/6 → 1/6 zero movement; flip-5 the worked example 1/6 → 1/6 (demand-log 2026-08-02 rows). It earns nothing; the next lever named by the flip record is a validator/repair turn, not this prose. **CUT (c), partial** — idiom 3's `stateKey`-Switch inline example (lines 706–718, ~480 chars): superseded by Phase 768 `Switch.on` + Phase 818; it wires a `Switch` to a `stateKey` nothing writes — the exact shape §Conditional-rendering below now warns against (2026-08-02 032/c6 row: confirmed dead end). Replaced by a two-line pointer to that section. EVERYTHING ELSE KEPT: the multi-field prose + `master-detail-multi-field` example are the 8ccbaee/1fbeecc flip mechanism (grok 1 → 5 projected fields); `master-detail-preselected` pins the Selection-as-param composition (2026-07-20 rows). Both master-detail examples overlap but each carries a unique verified mechanism — considered, held. |
| 880–922 | Transform — the embedded-data canonical shape | 782 (0/782) | **KEEP** — caution list (Transform sections); the minimal-authoring-form steer is economy teaching, not a legality warning. |
| 923–1,143 | Deriving ONE value — Transform in a scalar slot | 1,633 (1,333/300) | **KEEP** — caution list (Transform sections); fuaran#632 mechanism. |
| 1,144–1,149 | Grids without a column count | 58 (0/58) | **KEEP**. |
| 1,150–1,182 | Which tabular shape | 512 (0/512) | **KEEP** — caution list (sortable-table rule, Phase 801 `129a8a8`). |
| 1,183–1,325 | Distinguishing rows by value — the toned pill | 1,095 (508/586) | **KEEP** — Phase 750 mechanism, VERIFIED FLIPPED 35/35 (2026-07-30 shakedown row); the cell-Badge anti-pattern line extinguished the gpt decode cluster (2026-08-09 row, pack `a89e84c`). |
| 1,326–1,373 | Closure cells — no `*Fn` payloads | 524 (0/524) | **KEEP** — verified mechanism (2026-08-10 005 rows: `ade366b` flip 2/2, zero `*Fn`/`lit`/`col` post; `4fcf841` field-driven Progress flip 2/2, judge 1.000). Not generation-scope-verified extinct — held under the caution rule. |
| 1,374–1,386 | Prompt-given data is `Static` | 212 (0/212) | **KEEP** — accurate; renders-empty warning still true. |
| 1,387–1,642 | Filters must be WIRED | 1,700 (1,502/198) | **KEEP** — the 2026-07-20 612-616 campaign fix (×34+×12+×9+×7 cluster); demand-log names this teaching as the lever. |
| 1,643–1,675 | Dates and times | 583 (0/583) | **KEEP** — caution list (dates/durations, new, load-bearing; 817 sweep verified Duration cross-family). |
| 1,676–1,817 | "Today" is a binding | 961 (713/247) | **KEEP** — Phase 765 mechanism (the pilot-5 "strongest new capability demand", 023/c1 ×14). |
| 1,818–1,903 | Closed enum vocabularies | 1,823 (1,616/206) | **KEEP** — generated table (caution list). The `-Filter` retirement paragraph is KEPT: it is the `e34c449` mechanism that took incidence 397 → 0/89 (2026-07-30 row) — cutting it would re-open a closed demand. |
| 1,904–2,030 | Conditional rendering — `Switch` branches on any binding | 890 (646/243) | **KEEP** — Phase 768/818; the stateKey-nothing-writes warning steers canonical. |
| 2,031–2,093 | Empty states — Card `Box`, not `Callout` | 545 (206/338) | **KEEP** — Phase 767 disposition (020/c4); Icon flip verified (817 sweep, 010 ×2). |
| 2,094–2,147 | On/off controls — `Toggle` vs `Checkbox` | 422 (148/274) | **KEEP** — Phase 766 (017/c2 + 043/c3 ×6 cross-family language demand). |
| 2,148–2,175 | Style vocabularies | 431 (0/431) | **KEEP / DEFER (d)** — the value enumerations duplicate the generated enum-vocab table; the semantic halves (density-not-font, `emphasis` bool-vs-enum) are unique. Defer the duplication measurement to 836; cut nothing now. |
| 2,176–2,297 | Rules | 1,647 (175/1,472) | **KEEP** — rules 6–9 are the self-wiring/editable/Call-into contract. DEFER (d): rule 3 partially restates the node-kinds preamble; rule 4 partially restates the canonical-wire-shape bullets. Rule 8's editable-grid `FUARAN090` note may need a Phase-818 re-read (live Transform sources) — uncertain, needs a targeted probe; KEEP. |

### Class (d) deferrals — schema prose duplicated by the generated tables (cut NONE now; measure in 836)

- §Metric-vs-Fact Badge `variant` enumeration (lines 459–461) vs the `BadgeVariant` enum row.
- §TonedPill `map`-values enumeration (lines 1,313–1,316) vs the `ToneVariant` enum row.
- §Empty-states `Icon` size list (line 2,047–2,048) vs the `IconSize` enum row.
- §Style-vocabularies value enumerations vs the enum table.
- Rule 3 vs the node-kinds preamble (MISSING_FIELD / never-infers-default, stated twice).
- Rule 4 vs the canonical-wire-shape literal-text bullet.

## few-shot.jsonl — per-entry census and verdicts

Structural evidence for the dedup class (b): the flip record repeatedly shows the
SYSTEM PROMPT is the operative teaching surface — flip-3 (2026-08-02): "the few-shot
addition was redundant; no change at n=6"; flip-4 (2026-08-02): "`master-detail-multi-field`
is a few-shot entry, **which the default posture never reads**"; the Badge flip's
mechanism was promoting `badge-1` to a system-prompt example block (0/6 → 6/6). A
few-shot entry whose fixture already renders as a system-prompt example block therefore
adds no example any posture would otherwise lack — the system-prompt copy remains in
every posture — and is exemplification beyond the one-example rule.

| # | Fixture | Chars | ~Tok | Verdict |
|---|---|---|---|---|
| 1 | composite-root | 1,457 | 364 | KEEP — dup of a system-prompt block, but the archetypal composition request-pairing and the seed-corpus head entry; uncertain — held under the caution rule. |
| 2 | heading-1 | 225 | 56 | KEEP — unique fixture. |
| 3 | metric-1 | 458 | 114 | KEEP — dup, but the canonical-wire-shape opening's fixture (caution list). |
| 4 | markdown-1 | 190 | 47 | KEEP — unique. |
| 5 | callout-1 | 275 | 68 | KEEP — unique. |
| 6 | btn-1 | 350 | 87 | KEEP — dup, small; the Action request-pairing. |
| 7 | form-1 | 1,167 | 291 | KEEP — unique (the only multi-field form request-pairing). |
| 8 | filters-declarative | 575 | 143 | KEEP — unique. |
| 9 | filters-date-range | 353 | 88 | KEEP — unique (Phase 725 mechanism). |
| 10 | lenient-grid-transform-param-compact | 684 | 171 | KEEP — unique (not a system-prompt block). |
| 11 | grid-toned-pill | 1,134 | 283 | **CUT (b)** — dup of the system-prompt block; the 750 flip (35/35) was verified under the default posture, which reads only the system prompt. |
| 12 | query-dependson | 475 | 118 | KEEP — unique. |
| 13 | discl-1 | 375 | 93 | KEEP — unique. |
| 14 | tabs-explicit-1 | 653 | 163 | KEEP — unique. |
| 15 | custom-1 | 246 | 61 | KEEP — unique. |
| 16 | op-replacebinding | 272 | 68 | KEEP — dup, small; op-decoder diversity (one of only three op entries). |
| 17 | op-insertchild | 484 | 121 | KEEP — unique. |
| 18 | op-reorderchildren | 249 | 62 | KEEP — unique. |
| 19 | lenient-filterable-static-dashboard-compact | 2,258 | 564 | **CUT (b)** — dup of the system-prompt block (§Filters must be WIRED). |
| 20 | lenient-master-detail-preselected-compact | 1,606 | 401 | **CUT (b)** — dup of the system-prompt block (§three idioms). |
| 21 | master-detail-multi-field | 2,045 | 511 | **CUT (b)** — dup; flip-4 names this exact entry as never read by the default posture — its remedy (the system-prompt block, `1fbeecc`) exists and stays. |
| 22 | switch-on-selection | 1,237 | 309 | **CUT (b)** — dup of the system-prompt block (§Conditional rendering). |
| 23 | empty-state-card | 710 | 177 | **CUT (b)** — dup of the system-prompt block (§Empty states). |
| 24 | form-toggle | 496 | 124 | **CUT (b)** — dup of the system-prompt block (§On/off controls). |
| 25 | now-environment-binding | 1,157 | 289 | **CUT (b)** — dup of the system-prompt block (§"Today" is a binding). |
| 26 | progress-1 | 297 | 74 | **CUT (b)** — flip-3 verdict verbatim: "the few-shot addition was redundant; no change at n=6"; `Progress` stays taught in prose (§Metric-vs-Fact decision rule + the field-driven Progress cell). |
| 27 | badge-1 | 250 | 62 | KEEP — caution list (the Badge example; both copies held). |
| 28 | lenient-scalar-transform-composition-compact | 2,214 | 553 | **CUT (b)** — dup of the system-prompt block (§Deriving ONE value). |

Few-shot cut total: **13,154 chars ≈ 3,288 tok** (10 of 28 entries; 18 remain, every
one either unique to few-shot, an op-decoder entry, or caution-listed).

## Cut budget (planned)

| Class | What | ~Chars | ~Tok |
|---|---|---|---|
| (a) extinct-behaviour | arithmetic wrong-then-right pair (gpt-4o legacy, teaching-proof on its own target) | ~800 | ~200 |
| (b) failed/duplicate exemplification | Callout-body prose (failed twice) + 10 few-shot dups | ~13,600 | ~3,400 |
| (c) superseded by shipped vocabulary | "no duration format" sentence; idiom-3 stateKey example → pointer | ~550 | ~140 |
| (d) deferred to 836 | schema-prose duplication (listed above) | 0 cut | 0 |
| **Total planned** | | **~14,950** | **~3,740 (≈13.6% of the pack)** |

**Why the prose cut is small (~4% of authored prose, not 15–30%):** the audit found the
accreted prose is overwhelmingly VERIFIED flip mechanisms (Badge 6/6, TonedPill 35/35,
filters-wired, `-Filter` retirement 397→0, closure-cells 2/2, Now/Duration verified in
the 817 sweep) — all caution-listed. The genuinely extinct class is one wrong-then-right
pair; the redundancy lives in the few-shot duplication, which is where the bulk of this
slim lands. Cutting deeper into prose would mean cutting live teaching, which the
charter forbids ("measured, not maximal").

## Post-slim ledger (2026-08-15)

| File | Baseline | Post-slim | Delta |
|---|---|---|---|
| `system-prompt.md` | 87,873 ch / ~21,968 tok | 86,438 ch / ~21,610 tok | −1,435 ch / ~−359 tok |
| `few-shot.jsonl` (28 → 18 entries) | 21,920 ch / ~5,480 tok | 8,756 ch / ~2,189 tok | −13,164 ch / ~−3,291 tok |
| **Pack total** | **109,793 ch / ~27,448 tok** | **95,194 ch / ~23,798 tok** | **−14,599 ch / ~−3,650 tok (−13.3%)** |

### Per-cut ledger (one commit per class — bisection-ready)

| Commit | Class | Cut | Evidence |
|---|---|---|---|
| `f8434ff` | prep | regen of the drift-checked surfaces (corpus had moved at fuaran#818) + `authoring-pack.fsx` walker fix so `SetState(key)` is not silently dropped from the generated `Action.$type` list | drift gate must be clean before any cut is sized |
| `1db06df` | (a) | the gpt-4o arithmetic wrong-then-right pair (§Metric-vs-Fact, ~780 ch) | teaching-proof at both treatments (`a89e84c`, `602ada7`); generation-scoped 2026-08-11: sol@low 4/4 + terra@low 4/4 clean |
| `c74eee9` | (b) | Callout-body prose paragraph (`61a9c01`, ~450 ch) — failed twice (flip-4 1/6→1/6, flip-5 1/6→1/6); few-shot dedup ×10 (~13.2k ch): grid-toned-pill, lenient-filterable-static-dashboard-compact, lenient-master-detail-preselected-compact, master-detail-multi-field, switch-on-selection, empty-state-card, form-toggle, now-environment-binding, lenient-scalar-transform-composition-compact (all dups of system-prompt blocks), progress-1 (flip-3: "redundant; no change at n=6") | flip record: the system prompt is the operative surface; no posture loses an example |
| `1ff2305` | (c) | "There is no duration format" sentence (falsified by shipped `Duration` format, Phase 819, 817-sweep-verified); idiom-3 stateKey-Switch inline example (superseded by Phase 768 `Switch.on`; wired a stateKey nothing writes — the pack's own §Conditional-rendering warns against exactly that) → both replaced by pointers to the canonical sections | ~530 ch net |

### Deferred to Phase 836 (class d — cut nothing)

The schema-prose-duplicated-by-generated-tables list in the census above (Badge
variants, TonedPill tone values, Icon sizes, Style-vocabulary enumerations, Rules
3/4 restatements) — measure, then cut.

### Considered and held (cut wanted, evidence said no)

- The formatted-quantity ("1h 20m") wrong-then-right pair — same census window as the
  extinct arithmetic pair, but it is the `e4c3a2a` mechanism that extinguished a live
  WRONG_TYPE cluster and was never generation-scope-verified extinct.
- The closure-cells / `*Fn` subsection — verified flips (`ade366b` 2/2, `4fcf841` 2/2),
  not generation-scoped; needs a targeted probe before any cut.
- The second master-detail example (`lenient-master-detail-preselected-compact` block)
  — overlaps `master-detail-multi-field` but uniquely pins the Selection-as-param
  composition (2026-07-20 pinned rows).
- The `-Filter` retirement paragraph — it IS the mechanism that took incidence
  397 → 0/89; cutting it would re-open a closed demand.
- `composite-root` in few-shot — a dup, but the archetypal composition
  request-pairing; uncertain, held under the caution rule.

**Next (the coordinator's wet half):** stress-track sweep (n=1, two families) +
Tier-A/B mini-window against this pack SHA; any flip regression restores the class by
reverting its single commit. Then the evaluation-harness pin + the depth-cost re-measure
(grok's cumulative-USD ratio is the sentinel).
