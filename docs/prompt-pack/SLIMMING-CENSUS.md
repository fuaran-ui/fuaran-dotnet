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

# Minification census — Phase 839 (fuaran)

**Date:** 2026-08-15. **Charter:** Phase 839, task 1 — the `fuaran:example` blocks are
pretty-printed at 2-space indent, which buys a machine reader nothing (the decoder is
whitespace-indifferent) and is paid on every request. Emitted behind
`authoring-pack.fsx --minify-examples` so the pretty↔minified diff is inspectable and
the decision is reversible in one command.

**Method.** Same estimator as the 834 census above (`chars / 4`), same section split (at
every `##` / `###` heading), so the two ledgers compose. Reproduced as a check: this
pass's per-section figures match the 834 census row-for-row where no cut landed between
them (Preamble 198, Filters-must-be-WIRED 1,700, Rules 1,647, …), which is what licenses
comparing the totals.

## Baseline (pre-minification)

Measured at `a6782c8`. **This is not the 834 ledger's post-slim row** — that row predates
`a6782c8`, which restored three few-shot entries (+5,060 chars) under the charter's
regression rule. The pack the reader actually pays for is the one below.

| File | Chars | ~Tokens |
|---|---|---|
| `system-prompt.md` | 86,438 | 21,610 |
| `few-shot.jsonl` | 13,816 | 3,454 |
| **Pack total (taught prefix)** | **100,254** | **25,064** |

## What the flag touches

Only the 15 `fuaran:example` blocks in `system-prompt.md`. Two surfaces named in the
charter turn out to need nothing:

- **`few-shot.jsonl` is already minified — measured delta zero.** A JSONL record cannot
  carry a raw newline, so its embedded trees were always the corpus's compact bytes. The
  generator now minifies them unconditionally rather than assuming it, which is a
  robustness fix with no byte change today (verified: a `--write` with no flag reports
  `0 file(s) updated`).
- **`AI_AUTHORING_GUIDE.md` is deliberately excluded.** It is read by humans and is not
  part of the paid prefix, so minifying it would trade real legibility for no tokens.

## system-prompt.md — per-section delta

Fourteen sections carry an example block; eleven carry none and are unchanged (Preamble,
Submitting a form, The node kinds, Transform embedded-data, Grids without a column count,
Which tabular shape, Closure cells, Prompt-given data is `Static`, Dates and times, Closed
enum vocabularies, Style vocabularies).

| Section | ~Tok before | ~Tok after | Δ |
|---|---|---|---|
| The canonical wire shape | 558 | 520 | −38 |
| Containers nest under `children` | 1,148 | 722 | −426 |
| Actions on inputs | 157 | 133 | −24 |
| Editing an existing tree | 403 | 395 | −8 |
| Numbers vs text: `Metric` vs `Fact` | 934 | 926 | −8 |
| Selected / pre-selected / derived state | 2,981 | 1,754 | **−1,227** |
| Deriving ONE value — Transform in a scalar slot | 1,633 | 812 | **−821** |
| Distinguishing rows by value — the toned pill | 1,095 | 820 | −275 |
| Filters must be WIRED | 1,700 | 732 | **−968** |
| "Today" is a binding | 961 | 512 | −449 |
| Conditional rendering — `Switch` | 890 | 515 | −375 |
| Empty states — Card `Box`, not `Callout` | 545 | 473 | −72 |
| On/off controls — `Toggle` vs `Checkbox` | 422 | 369 | −53 |
| Rules | 1,647 | 1,573 | −74 |
| **Sum** | | | **−4,818** |

The saving concentrates exactly where the deep worked examples are — the three idioms,
the scalar-slot Transform, and the wired-filters section are 63% of it between them. No
sentence of teaching is removed anywhere.

## Ledger (2026-08-15)

| File | Pretty | Minified | Delta |
|---|---|---|---|
| `system-prompt.md` | 86,438 ch / ~21,610 tok | 67,168 ch / ~16,792 tok | −19,270 ch / ~−4,818 tok (−22.3%) |
| `few-shot.jsonl` | 13,816 ch / ~3,454 tok | 13,816 ch / ~3,454 tok | 0 |
| **Pack total** | **100,254 ch / ~25,064 tok** | **80,984 ch / ~20,246 tok** | **−19,270 ch / ~−4,818 tok (−19.2%)** |

**Cross-check, and it is the load-bearing one.** The 15 example blocks total 32,100 chars
pretty and 12,830 minified: −19,270, identical to the whole-file delta to the byte. The
transform therefore provably changed nothing outside the blocks — the census does not
have to take the scanner's word for it.

**For scale:** this one whitespace change is **1.3× the entire Phase 834 slim**
(−4,818 tok against −3,650) and costs no teaching whatsoever, where 834 spent an audit
of every accreted paragraph to reach its figure. The cheap win was in the formatting the
whole time.

### Why the emission is a scanner, not `WriteIndented = false`

The obvious implementation is wrong in a way that would not have shown up in this ledger.
Re-serialising a fixture through `JsonSerializer` rewrites content the conformance corpus
pins: it re-escapes string payloads that themselves contain JSON
(`nodes/btn-json-payloads.json` grows 757 → 787 chars) and re-formats some number
literals (`nodes/code-1.json`, 156 → 152). Both would have ridden into the pack as byte
changes against the corpus, dressed as a whitespace cut, and the drift check would have
passed them — it compares canonicalised structure, and structure is exactly what those
edits preserve. The generator therefore strips insignificant whitespace with a scanner
that copies string-literal text verbatim, so "the diff is whitespace" is a property of
the mechanism rather than a claim about it.

### Reversibility

`--minify-examples` is a pure toggle: re-running `authoring-pack.fsx --write` without it
restores `system-prompt.md` byte-for-byte to `a6782c8` (verified by hash, not by
inspection). A regression finding therefore costs one command, not a revert.

## Sweep gate (n=1, two families) — 2026-08-15

**Verdict: no flip regression. Adopted.**

Stress track, all 12 tasks × two families (`claude-opus-4-8@low`, `gpt-5.6-terra@low`) ×
the `fuaran` condition at n=1 — the same 24-cell shape as the 817 and 834 sweeps, so the
arms are comparable. The pre-minification arm is the 834 post-slim sweep run earlier the
same day (window `20260815T0014Z`–`0021Z`); `system-prompt.md` is byte-identical between
that run and this pack's pretty form, so the two arms differ in whitespace and nothing
else.

| Arm | claude success | gpt success | Total | claude parse | gpt parse |
|---|---|---|---|---|---|
| Pretty (`20260815T0014Z`) | 7/12 | 4/12 | 11/24 | 12/12 | 9/12 |
| Minified (`20260815T0640Z`) | 6/12 | 6/12 | **12/24** | 12/12 | 10/12 |

Five cells changed verdict: three up (stress-003 claude, stress-006 gpt, stress-007 gpt),
two down (stress-007 claude, stress-008 claude). Comprehension did not degrade on either
family's decode gate — parse held at 12/12 on claude and rose 9 → 10 on gpt.

**Why the two down-flips are not read as a minification effect, stated carefully because
this is the whole judgement.** The corpus carries its own noise measurement: the 834
restore re-probed stress-001/002/003 twenty minutes after the baseline against a
**byte-identical** system prompt, and 2 of those 6 cells changed verdict (stress-002 gpt
fail → pass, stress-003 claude fail → pass). A same-prompt repeat therefore flips ~33% of
cells at n=1, against the 5/24 ≈ 21% observed across the pretty→minified change. The A/B
difference is smaller than the instrument's own repeat error, so this sweep can show the
absence of a large regression and nothing finer. It is not evidence that minification
helps, despite the +1.

What would falsify the adoption is a *reproducible* per-family degradation, which is a
question for n ≥ 3 or the Tier-A/B mini-window, not for this gate. Reversal, if it comes
to that, is `authoring-pack.fsx --write` with no flag.

**Cost of the gate:** ≈ $1.25 of metered emission across 24 cells (prompt caching carries
almost the whole prefix — 256k cache-read tokens against 85k cache-create on the claude
arm), plus an unmetered judge leg.

**Not measured here:** the pack's size in each family's *own* tokenizer. One incidental
datum — the first uncached gpt cell billed 18,703 input tokens for a prefix this census
estimates at ~16,792 — shows the `chars / 4` estimator is close but not neutral across
families. Sizing every future cut in the currency it actually spends is a separate piece
of work in the harness, deliberately not done here.

# Signature-catalogue census — Phase 838 (fuaran)

**Date:** 2026-08-15. **Charter:** Phase 838 — the sanctioned revival of Phase 614's
retire note ("restructure, not slice"). The pack's type-surface teaching re-encodes as
a `.d.ts`-flavoured signature catalogue generated from the same `schema.json` the
decoder enforces; prose that RESTATED type structure collapses to catalogue
references; prose that teaches SEMANTICS stays verbatim (the 613 ablation proved the
vocabulary teaching is distributed through it — nothing didactic was cut here).
Human readability of the pack is waived (operator release 2026-08-15).

**Method.** This census records BOTH currencies: bytes (the 834/839 ledgers' basis —
their `chars/4` estimate is bytes-derived) and the **o200k_base reference tokenizer**
(the 839 census's "not measured here" follow-up). The two disagree in SIGN on this
restructure, which is the headline: declaration-style text tokenizes materially better
than pipe-table markdown, so a byte-neutral swap is a real token cut. All measurements
at LF-normalised file bytes.

## Baseline (pre-restructure, `d69e3ff` — the 839 post-minification pack)

| File | Bytes | o200k |
|---|---|---|
| `system-prompt.md` | 67,168 | 18,559 |
| `few-shot.jsonl` | 13,816 | 3,730 |
| **Pack total (taught prefix)** | **80,984** | **22,289** |

## What the catalogue replaces

| Artefact | Bytes | o200k | Notes |
|---|---|---|---|
| required-fields table (old, generated) | 3,052 | 970 | per-kind required/optional names + † marks; NO field types |
| enum-vocab block (old, generated) | 6,599 | 2,029 | enums + discriminator case names + required-only payload fields + nested-collection table |
| **Old generated total** | **9,651** | **2,999** | |
| **Signature catalogue (new, generated)** | **10,284** | **2,886** | the COMPLETE typed surface: every field typed, optional payload-DU fields included, record shapes (`Accessibility`, `DrawStyle`, `SemanticStyle`, …) the tables never carried, spelling-complete on every closed vocabulary |

The catalogue is **2,886 o200k against `schema.json`'s 20,683 (14.0%)** — inside the
charter's 10–15% estimate — and **beats the two partial tables by 113 tokens while
carrying strictly more constraint**. Spelling-completeness design (the measured
mid-session catalogue-stub arm's one failure was an invented enum spelling): single-use
enums inline their quoted values AT the use site; multi-use enums are declared once by
name; only name-referenced defs emit, so dead vocabulary cannot ride. Optional
`closure` fields are suppressed (host handler slots rules 6–9 forbid authoring);
required closure slots stay. The `Transform` pipeline-step algebra is NOT in the
catalogue — `schema.json` models `pipeline` as `any[]`, so the algebra's teaching
stays where it already lives, in the kept Transform prose sections (hand-authoring it
into the catalogue would break the zero-hand-maintained-content acceptance).

## Per-slice ledger (one commit per slice — bisection/restore-ready)

| Commit | Slice | What moved | file bytes | file o200k |
|---|---|---|---|---|
| — | baseline `d69e3ff` | | 67,168 | 18,559 |
| `2dfe8c4` | catalogue swap | generator + notation paragraphs in; required-fields + enum-vocab tables + their framing prose out; `-Filter` retirement paragraph moved beside the catalogue verbatim; the three didactic subsections under the retired enum section promoted to `##` (text unchanged); minified emission made the generator DEFAULT | 69,013 | 18,790 |
| `f4f1e73` | prose slice 1 | Badge-variant list (§Metric-vs-Fact), ToneVariant list (§toned pill), Icon size list (§Empty states) → catalogue references; every semantic clause kept | 68,958 | 18,760 |
| `cc4e556` | prose slice 2 | §Style-vocabularies value enumerations → catalogue references; density-not-font / prominence-not-bold / bool-vs-enum trap / identity defaults / omit-when-unsure kept | 68,785 | 18,705 |
| `57ba8d9` | prose slice 3 | Rules 3–5 restatements (Required/Optional-column vocabulary, literal-text restatement, schema-artefact authority pointer) → catalogue terms; `dateStyle` list; the two stray † marks | 68,244 | 18,547 |
| `1d29278` | prose slice 4 | wire-shape optional-node-key bullets (restated `Node`/`StateBehaviour`/`SemanticStyle` + style identity defaults) and the Editing section's TreeOp name list → references | 67,796 | 18,406 |
| `6af2460` | colon-dense emission | no space after the field colon: −135 o200k measured across the catalogue (the `; ` separator saves ~10 and was kept) — the 839 minification argument applied to the generated declarations | 67,356 | 18,270 |

## Ledger (2026-08-15)

| File | Baseline | Catalogue pack | Delta |
|---|---|---|---|
| `system-prompt.md` | 67,168 B / 18,559 o200k | 67,356 B / 18,270 o200k | +188 B / **−289 o200k (−1.6%)** |
| `few-shot.jsonl` | 13,816 B / 3,730 o200k | 13,816 B / 3,730 o200k | 0 (untouched — exemplars are Phase 841's territory) |
| **Pack total** | **80,984 B / 22,289 o200k** | **81,172 B / 22,000 o200k** | **+188 B / −289 o200k** |

**The sign disagreement is the finding.** The bytes ledger says the restructure cost
+188 B; the reference tokenizer says it saved 289 tokens. Markdown pipe tables spend
tokens on `|`, backticks and padding that declaration syntax does not, so the 834/839
`chars/4` convention — adequate for measuring cuts WITHIN one encoding — mis-signs a
re-encoding. Future pack-economics entries should quote o200k alongside bytes, as here.

What the delta does NOT say: the catalogue did not merely re-encode the old tables —
it completed them (full field typing, payload-DU optionals, previously untabled record
shapes). The honest statement is: **strictly more type-surface teaching, spelling-
complete, at −1.6% of the prefix**, with the model's authority pointer (rule 5) moved
off the 20,683-token `schema.json` artefact onto the in-prompt catalogue — a host that
was attaching the schema for reference can now drop it.

### Considered and held

- The Containers section's layout-primitive name list and the whole Transform op/fn
  vocabulary prose — the former is a verified-flip section (direction-as-field), the
  latter is not schema-derivable (see above); both kept whole.
- The Duration unit/style value lists in §Dates — annotated with rendered forms
  ("1h 22m" / "1:22:00"), which is semantics the catalogue cannot carry; kept.
- Full-density brace/equals stripping in the catalogue (`Name{f:T}`) — a further
  −21 o200k, rejected: it breaks the notation paragraph's declared reading form for
  a fifth of the colon-dense win.
- The pack's header comment still describes only the example-block discipline; the
  catalogue's own markers + the generator header carry the do-not-hand-edit rule.
  Left unchanged so the sweep-gate pack SHA is the shipped pack SHA.

## Sweep gate (n=1, two families) — 2026-08-15

**Verdict: no flip regression attributable to the restructure. Adopted.**

Stress track, all 12 tasks × two families (`claude-opus-4-8@low`, `gpt-5.6-terra@low`)
× the `fuaran` condition at n=1 — the same 24-cell shape as the 817/834/839 gates.
The comparator is the Phase 839 minified arm run earlier the same day (window
`20260815T0640Z`, pack `d69e3ff`); the catalogue arm is window `20260815T0902Z`
against the pack at `6af2460` (verified in-flight: every cell's sent prompt carries
the `fuaran:signature-catalogue` block and no retired table).

| Arm | claude success | gpt success | Total | claude parse | gpt parse |
|---|---|---|---|---|---|
| Minified baseline (`0640Z`) | 6/12 | 6/12 | 12/24 | 12/12 | 10/12 |
| Catalogue (`0902Z`) | 7/12 | 5/12 | **12/24** | 12/12 | 10/12 |

Four cells changed verdict: two up (stress-001 claude judge, stress-002 gpt
parse+judge), two down (stress-006 gpt, stress-007 gpt). Parse held exactly
per family — 12/12 claude, 10/12 gpt, both arms.

**The two down-flips do not bisect to a displaced prose section, stated carefully
because that is the restore trigger.** stress-006 gpt failed `INVALID_JSON` (a raw
bracket error at offset 799) while the judge scored the same emission 1.000 — no
collapsed section taught JSON syntax; the strict-JSON rule is untouched. stress-007
gpt parsed clean and lost on judge PARTIALs over `sortable`/`defaultSort` signalling —
teaching that was KEPT whole (§Which tabular shape) and that the catalogue also
carries; the 839 census records this same cell flipping in BOTH directions between
same-day arms against byte-identical or whitespace-only-different prompts. Per the
839 honesty rule: the instrument's same-prompt repeat error is ~33% of cells at n=1,
against 4/24 ≈ 17% observed here — the A/B difference is smaller than the repeat
error, so this gate demonstrates the absence of a LARGE regression and nothing finer.
It is not evidence the catalogue helps, despite claude's +1. What would falsify
adoption is a reproducible per-family degradation — a question for n ≥ 3 or the
Tier-A/B mini-window, not this gate. Reversal of any single prose slice is one
commit revert (the per-slice ledger above); reversal of the whole emission is
`--pretty-examples`.

**Cost of the gate:** one arm only — the baseline arm is reused from the same-day 839
gate at zero marginal cost. Catalogue arm across 24 cells: 21,474 metered input +
17,171 output tokens, 515,757 cache-read + 28,571 cache-create (prompt caching
carries almost the whole prefix), ≈ $1–2 by the 839 gate's ≈$1.25 same-shape
yardstick — well inside the $15 cap.

**Epoch input (Phase 838):** pack SHA `6af2460`; `system-prompt.md` 67,356 B /
18,270 o200k; `few-shot.jsonl` 13,816 B / 3,730 o200k; pack total 81,172 B /
**22,000 o200k** (baseline `d69e3ff`: 80,984 B / 22,289 o200k). The catalogue block:
10,284 B / 2,886 o200k = 14.0% of `schema.json`'s 20,683 o200k.
