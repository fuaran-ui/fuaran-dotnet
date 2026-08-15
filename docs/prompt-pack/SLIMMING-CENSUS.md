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

# Exemplar-coverage census — Phase 841 (fuaran)

**Date:** 2026-08-15. **Charter:** Phase 841 — exemplar selection had never been asked
whether a smaller set covers the same teaching, because it accreted one-per-lesson
through the demand loop. Plus the **reinvestment doctrine** (operator direction
2026-08-15, post-842): freed tokens are a QUALITY budget, so run the minimisation, then
spend what it frees on the weakest-covered rules with observed failures. Ceiling: the
pre-841 pack size; net growth needs operator approval.

**Method.** Both currencies again (bytes + the **o200k_base** reference tokenizer), per
the 838 finding that `chars/4` mis-signs a re-encoding. All figures at LF-normalised file
bytes. The new instrument is a **generated rule→fixture coverage matrix**
(`docs/tools/coverage-matrix.json`, 21.4 kB, produced and drift-checked by
`authoring-pack.fsx`); the miner runs from it via `authoring-pack.fsx --mine`, which is
read-only and writes nothing.

## The rule inventory — 436 taught rules

| Family | Count | Where it comes from |
|---|---:|---|
| `case:<Union>.<Case>` | 160 | `schema.json` — every `$type`-discriminated alternative |
| `field:<Scope>.<name>` | 145 | `schema.json` — every OPTIONAL property |
| `enum:<Enum>=<value>` | 102 | `schema.json` — every closed-vocabulary value |
| `idiom:<name>` | 11 | authored predicates — teaching that is a composition, not a symbol |
| `pipestep:` / `pipefn:` / `pipeop:` / `pipeagg:` | 18 | the corpus — `schema.json` models `pipeline` as `any[]` |

Four modelling decisions carry the census, and each was made because the naive
alternative measures the wrong thing:

- **Required fields get no rule.** A fixture cannot carry a case without them, so
  counting them would inflate every coverage figure with rules nothing can miss.
- **Optional CLOSURE fields get no rule**, matching the 838 catalogue's own suppression
  ("fields whose only correct emission is absence"). A model that rewarded an exemplar
  for demonstrating `onChange` would be scoring the pack against the shape rules 6–9
  forbid.
- **A §16 alias is its own rule** (`alias:ToneVariant=Danger`), not the canonical value.
  The exemplar's bytes are what the reader sees; crediting `Success` for a fixture that
  spells `Danger` would count teaching that is not on the page.
- **The pipeline algebra is enumerated from the corpus**, because the schema does not
  constrain it — the same reason 838 could not put it in the catalogue.

**The probe fired, which is why the numbers are trustworthy.** The walk runs in two modes
off ONE traversal (enumerate vs observe), so an inventory rule and an observed rule are
spelled by the same code; and any observed rule outside the inventory is a hard failure
rather than a coverage finding. That check earned its place immediately: the first
implementation did not descend from `NodeKind` into its four sub-unions, so no `Box` node
ever matched a case and the walk returned a plausible-looking 30 rules where the answer
was 137. It reported a tidy number and a wrong one — the class the estate's "verify the
probe, not just the verdict" rule exists for.

## Minimisation — the accreted set was already near-minimal, and that is the finding

Two independent procedures were run and they agree at every stage: greedy set-cover from
scratch (cost-weighted — new rules per minified byte, since bytes are what a prefix
spends) with the 15 system-prompt blocks forced in, and a redundancy prune that drops any
current exemplar whose removal leaves coverage unchanged.

| Pass | Dropped | Why the 834 census had kept it |
|---|---|---|
| 1 | `markdown-1`, `lenient-grid-transform-param-compact` | both marked "unique" — true of the question that census could ask (unique among few-shot FIXTURES), not of whether the RULES are unique |
| 2 | `op-insertchild`, `op-reorderchildren` | invisible until the coverage model learned to read the pack's hand-authored JSON blocks |

Pass 2 is the more interesting one, and it is a correction to this instrument rather than
to the 834 census. The matrix originally read only corpus-derived marker blocks, so
§Editing's hand-authored `Batch` example — which inserts a child and then states the
resulting order — did not exist as far as coverage was concerned. It cannot be a corpus
fixture (it addresses ids belonging to the `composite-root` tree, and no fixture knows
another fixture's ids), but it is teaching on the surface every posture reads. Counting
it did two things at once: it proved two few-shot op entries redundant, and it withdrew
the apparent gap in layout-reorganisation teaching that would otherwise have consumed a
reinvestment admission. **A coverage model blind to a third of the pack's worked examples
does not merely under-report; it misdirects the spend.**

**Why the minimisation yield is small (4 entries, no dense-tree substitution).** The
phase's premise was that one maximally-dense tree could replace ten sparse ones. Run
against this pack, greedy-from-scratch reproduces the pruned current set byte-for-byte
and never finds a denser substitution worth making. The reason is that the redundancy was
already spent: Phase 834's dedup cut ten duplicate few-shot entries, and what remained was
very nearly non-redundant. Set-cover confirms that independently, which is the useful
result even though it is not the exciting one.

**Two constraints on the miner, both deliberate.** It may not displace a system-prompt
block (every one is caution-listed with its own flip record in the 834 census, and the
default posture reads only that surface), and it may not introduce a lenient-accept
fixture the pack does not already carry — admitting one would swap a canonical example
for a §16 shorthand, which changes the taught DIALECT: a separate decision with its own
evidence, not a side-effect of minimising a set.

## Reinvestment — what the matrix confirmed, and what it did not

| Target class | Matrix verdict | Disposition |
|---|---|---|
| Structural / wrap vocabulary | `idiom:container-in-wrapper` covered by **zero fixtures in the whole corpus**; `case:LayoutKind.Tabs` few-shot-only | **ADMITTED** — new composite fixture + system-prompt block |
| Pre-filled default values | `idiom:prefilled-default` few-shot-only — never on the surface the default posture reads | **ADMITTED** — same tree, plus two clauses naming the lesson |
| Layout reorganisation intent | apparently uncovered; that was the model's blind spot. §Editing already teaches `Batch` + `InsertChild` + `ReorderChildren` by worked example | **NOT ADMITTED** — the hole was already filled |
| Unified search grouping | — | **NOT TAUGHT** — watch class; its pack-vs-rubric classification is still open |

The zero-fixture finding is the one worth restating: every `Tabs` / `SplitPanel` /
`Stepper` fixture in the corpus held bare leaves, so the commonest screen shape there is —
a tabbed view whose panels are cards — had no conformance witness and nothing an author
could copy, while the pack listed the wrapper primitives in prose and never once showed
one holding a container. A measured mid-session arm failed on exactly that.

**The composite fixture** (`composite-tabs-panels`, corpus `7d81eb7`) is deliberately
dense so one tree absorbs several sparse singles: two `Box` cards inside a `Tabs` wrapper,
one on an explicit `Grid` layout and one on `Flex`, holding a form whose text and choice
fields both arrive PRE-FILLED with a `Static` value, every optional handler slot omitted.
It was authored a second time to carry `Sparkline` rather than `Metric`, specifically so
it would be a strict SUPERSET of `tabs-explicit-1` and could retire it — the phase's own
thesis applied to the phase's own admission.

## Ledger (2026-08-15)

| Commit | Class | What moved | pack bytes | pack o200k |
|---|---|---|---:|---:|
| — | baseline `ae265f8` | the 838 catalogue pack | 81,172 | 22,000 |
| `aa5d801` | tooling | coverage matrix + miner; no pack change | 81,172 | 22,000 |
| `9bd2329` | minimisation 1 | `markdown-1`, `lenient-grid-transform-param-compact` | 80,296 | 21,777 |
| `765dcb5` | minimisation 2 | miner reads hand-authored blocks; `op-insertchild`, `op-reorderchildren` | 79,561 | 21,579 |
| `ed89b27` | reinvestment | `composite-tabs-panels` block + prose; `tabs-explicit-1` retired | 80,891 | 21,924 |

| File | Baseline | Phase 841 | Delta |
|---|---|---|---|
| `system-prompt.md` | 67,356 B / 18,270 o200k | 69,340 B / 18,796 o200k | +1,984 B / +526 o200k |
| `few-shot.jsonl` | 13,816 B / 3,730 o200k | 11,551 B / 3,128 o200k | −2,265 B / −602 o200k |
| **Pack total** | **81,172 B / 22,000 o200k** | **80,891 B / 21,924 o200k** | **−281 B / −76 o200k** |

**Budget arithmetic.** Freed by minimisation: 1,611 B / 421 o200k across four entries.
Freed by the retirement the admission paid for itself (`tabs-explicit-1`): 654 B /
181 o200k. Spent on the admission: 1,984 B / 526 o200k (the block's tree is 1,300 B /
355 o200k; the remainder is the two teaching clauses). Net **−281 B / −76 o200k against
the ceiling, on both currencies** — so the reinvestment needs no growth approval. The
pack is smaller than it was and teaches more.

| Coverage | Baseline | Phase 841 |
|---|---:|---:|
| exemplars (deduped fixtures) | 28 | 24 |
| rules covered / 436 taught | 131 | **136** |
| rules on the OPERATIVE surface (system prompt) | 99 | **116** |
| rules exemplified only in few-shot | 32 | **20** |
| `idiom:` rules left uncovered | 1 | **0** |

Coverage preservation across the cuts is by construction, not by re-measurement: every cut
was a prune output, and the prune only drops an entry whose removal leaves the covered set
identical.

## Sweep gate (n=1, two families) — 2026-08-15

**Verdict: no flip regression attributable to Phase 841. Adopted.**

Stress track, all 12 tasks × two families (`claude-opus-4-8@low`, `gpt-5.6-terra@low`) ×
the `fuaran` condition at n=1 — the same 24-cell shape as the 817/834/839/838 gates. The
comparator is the Phase 838 catalogue arm run earlier the same day (window
`20260815T0902Z`, pack `6af2460`); this arm is window `20260815T0952Z` against the pack at
`ed89b27`, verified in-flight: all 24 cells' sent prompts carry the
`composite-tabs-panels` block and none carries the retired `tabs-explicit-1` few-shot
entry.

| Arm | claude success | gpt success | Total |
|---|---|---|---|
| Catalogue baseline (`0902Z`) | 7/12 | 5/12 | 12/24 |
| Mined + reinvested (`0952Z`) | 5/12 | 6/12 | **11/24** |

Three cells changed verdict: one up (stress-001 gpt), two down (stress-001 claude,
stress-002 claude).

**Neither down-flip bisects to a displaced exemplar, which is the restore trigger, and
this was checked mechanism-first rather than by counting.** stress-002 claude failed at
the PARSE gate — `INVALID_JSON at offset 742: expected ',' or '}' but found ']'` — while
the judge returned YES on all four criteria: a semantically perfect emission that was
syntactically malformed. Nothing cut in this phase taught JSON syntax, and the 838 census
records the identical shape (stress-006 gpt, judge 1.000, raw bracket error). stress-001
claude failed on judge c2 PARTIAL, over a derived count computed against a second inline
copy of the data rather than a shared reference — teaching carried by
`lenient-scalar-transform-composition-compact`, which is PINNED, untouched, and whose own
tree uses two independent inline copies in exactly the way the rubric marked down. That
is a standing pack-vs-rubric tension, not a Phase 841 effect. Neither cell involves Tabs,
containers, markdown leaves, tree-edit ops, or filter-param grids — the five things this
phase moved.

Per the 839 honesty rule: the instrument's same-prompt repeat error is ~33% of cells at
n=1 (the 834 restore re-probed three tasks against a byte-identical prompt and 2 of 6
cells changed verdict), against 3/24 ≈ 12.5% observed here. **The A/B difference is well
inside the instrument's own repeat error, so this gate demonstrates the absence of a LARGE
regression and nothing finer.** It is not evidence that the mined pack is worse, despite
the −1, any more than the 839 gate's +1 was evidence its change helped. What would falsify
adoption is a reproducible per-family degradation — a question for n ≥ 3 or the Tier-A/B
mini-window, not this gate.

**Cost of the gate:** 22,000 metered input + 14,425 output tokens, 442,926 cache-read +
117,832 cache-create across 24 cells, plus an unmetered judge leg (sonnet). ≈ $3–5 by the
839 gate's same-shape yardstick — the cache-create figure is four times that arm's because
this run started cold, which is where the difference sits. Well inside the $15 cap.

**Epoch input (Phase 841):** pack SHA `ed89b27`; `system-prompt.md` 69,340 B / 18,796
o200k; `few-shot.jsonl` 11,551 B / 3,128 o200k; pack total 80,891 B / **21,924 o200k**
(baseline `ae265f8`: 81,172 B / 22,000 o200k). Corpus: `fuaran-ui-specification@7d81eb7`.
Coverage 136 of 436 taught rules, 116 of them on the operative surface.

### Considered and held

- The five caution-listed few-shot entries that duplicate a system-prompt block
  (`composite-root`, `metric-1`, `btn-1`, `op-replacebinding`, `badge-1`) cost 756 o200k
  for zero additional rule coverage — measured here, deliberately not cut. Few-shot's job
  includes the natural-language request PAIRING, which the coverage model does not score,
  and the 834 census held them under the caution rule; cutting them on this instrument
  alone would be scoring one dimension and paying in another.
- Displacing a system-prompt block. The miner is forbidden from it by construction, and
  the reason is the flip record rather than conservatism.
- A second composite fixture. One idiom had zero coverage and now has one exemplar; the
  rest of the unexemplified queue is vocabulary BREADTH (112 `case:` rules, mostly Drawing
  shapes, Action variants and the Hole/Fragment surfaces), which is a different argument
  about what a prompt pack is for and not something to spend a token budget on silently.
- The `alias:` family as a teaching target. It is recorded so the model stays honest about
  what an exemplar's bytes say, not because the pack should teach §16 shorthands — the
  taught dialect is canonical, and that is Phase 840's territory.

# Lenient-dialect census — Phase 840 (fuaran)

**Date:** 2026-08-15. **Charter:** Phase 840 — the §16 lenient-accept shorthands exist
decoder-side as a silent safety net, taught nowhere; this phase inverts the posture for
ONE pack variant: teach the tersest decodable form as the primary emission dialect and
let the decoder normalise to canonical. The only pack strategy that cuts OUTPUT tokens
(the expensive ones — the Tier-D analysis has output at parity with the jsx baseline)
as well as the prefix. The canonical wire form remains the sole contract; the dialect
is an emission-side economy, invisible to hosts.

**Method.** Both currencies (bytes + o200k_base), per the 838 finding. The new
instruments: a classification of the ENTIRE leniency surface (the corpus's 60
`lenient-accept` fixtures, every id claimed by exactly one family — generated as
[`DIALECT-APPENDIX.md`](DIALECT-APPENDIX.md), so a new leniency fails the drift gate
until classified); a mechanical canonical→dialect transform run to a FIXPOINT (the
one-dialect-per-variant purity property, structural rather than reviewed); and a
per-block decoder PROOF (`docs/tools/dialect-verify.fsx`): every emitted dialect block
must satisfy `encode(decode(dialect)) == encode(decode(canonical))` byte-equal through
the real decoder, or the emission refuses to write. 32 of 33 proposed blocks proved;
the one advisory failure is the deliberately-WRONG Metric teaching example, which does
not decode by design and correctly fell back to its canonical text.

## The classification — 28 families over 60 fixtures

Full table (generated, with per-family evidence): [`DIALECT-APPENDIX.md`](DIALECT-APPENDIX.md).

| Class | Families | Disposition |
|---|---:|---|
| **taught-primary** (total + loss-free + token-positive) | 7 | Static-envelope elision (scalar/array), bare-string options, bare source columns, guarded schema omission, params map, flat filter step (eq/contains vs param), DateRange pair — the taught table |
| **composite** | 1 | the five `-compact` whole-tree exemplars |
| **safe-not-taught** (loss-free, no token gain) | 8 | enum/field/step/expression aliases, values-only columns, Pill-tag, bare Grid to Auto, bool-emphasis coercion |
| **already-canonical** (the terse side IS canonical) | 5 | Literal envelope, Bound wrapper, explicit defaults, plain-field Static unwrap, DateRange Static envelope |
| **never-taught** (partial / heuristic / contextual) | 7 | row-major transposition (order-lossy heuristic), source State/Static envelope (semantics-bearing), epoch timestamps (magnitude heuristic), legacy cumSum, opaque sentinels, null-for-absence, synthesised Grid cols |

**The decoder proof earned its place on day one — two order-semantics findings no
document carried.** (1) Schema ENTRY ORDER is semantic on the canonical wire (re-encode
preserves it) while inference derives its order from the columns object's sorted key
order — so dropping a schema whose declared order differs from the column order changes
canonical bytes. Caught LOSSY on four pack blocks; the transform now drops a schema only
when the declared order matches the column order exactly. (2) The params-map coercion
re-encodes the params array NAME-SORTED, so the byte-exact transform applies only to
already-sorted params (emission-side the map stays order-free — params are a name-keyed
set). Both are now guards, and both are the class of thing the appendix exists to record:
"loss-free" claims decided by the decoder, not by reading the spec.

**Enumeration findings (leniencies with no dedicated fixture — recorded, not taught,
no fixtures authored per the cross-host CI cost).** The flat filter step with a LITERAL
right-hand (`"value":…`) decodes in the F# tier but is not corpus-pinned — teaching it
would be a private dialect, so composed/literal predicates keep the full `pred` form.
A `limit` step's `offset:0` is explicit in canonical bytes yet restorable on omission
(visible inside the step-aliases fixture) — an omit-default the canonical encoder does
not omit. The segmented-orientation fixture's expected bytes still carry
`"orientation":"Horizontal"` although §3.6 records the 0.2.0 encoder as omitting it —
a minor spec/corpus tension worth a look next corpus regen. Pre-existing and held: the
CANONICAL pack already carries four `lenient-*-compact` example blocks (bare columns, no
schema), i.e. the default pack was already mildly mixed-dialect before this phase; the
one-dialect purity rule is enforced on the dialect variant, and re-emitting the
canonical variant's lenient blocks in strict canonical form would change the shipped
teaching — a separate decision, not taken here.

## Ledger (2026-08-15)

| File | Canonical (HEAD) | Dialect variant | Delta |
|---|---|---|---|
| `system-prompt.md` | 69,340 B / 18,796 o200k | 70,544 B / 19,053 o200k | +1,204 B / +257 o200k |
| `few-shot.jsonl` | 11,551 B / 3,128 o200k | 10,827 B / 2,920 o200k | −724 B / −208 o200k |
| **Pack total** | **80,891 B / 21,924 o200k** | **81,371 B / 21,973 o200k** | **+480 B / +49 o200k** |

The decomposition: the generated dialect passage costs 2,379 B / 624 o200k; the example
re-emissions pay back −1,342 B / −383 o200k across the 10 transformed system-prompt
blocks and −724 B / −208 o200k across few-shot. So the PREFIX is net +49 o200k — the
dialect variant is not a prefix cut, it is a bet that teaching the shorthand moves the
OUTPUT side, which the prefix ledger cannot show and the sweep below does.

## Sweep — dialect pack vs canonical pack (n=1, two families)

Stress track, all 12 tasks × two families (`claude-opus-4-8@low`, `gpt-5.6-terra@low`) ×
the `fuaran` condition at n=1 — the same 24-cell shape as the 817/834/839/838/841 gates.
The canonical arm is the Phase 841 arm (window `20260815T0952Z`, pack `ed89b27` — whose
pack files are byte-identical at HEAD), reused at zero marginal cost. The dialect arm is
window `20260815T1032Z`–`1035Z` against the dialect variant (composed-prompt
`sha256:6c0d6296…`), pinned via the harness's pack-directory override; verified
in-flight: every cell's sent prompt carries the emission-dialect passage.

| Arm | claude parse | claude success | gpt parse | gpt success | Total success |
|---|---|---|---|---|---|
| Canonical (`0952Z`) | 11/12 | 5/12 | 10/12 | 6/12 | 11/24 |
| Dialect (`1032Z`) | **12/12** | **7/12** | **11/12** | 6/12 | **13/24** |

**Output tokens (the phase's claim), excluding reasoning tokens — the emission bytes:**

| Family | Canonical | Dialect | Delta |
|---|---:|---:|---|
| claude-opus | 5,959 | 5,604 | **−6.0%** |
| gpt | 3,422 | 3,208 | **−6.3%** |

Direction is consistent, not just aggregate: 17 of 24 cells emitted fewer output tokens,
6 more, 1 unchanged. **And the mechanism is verified, not inferred** — across the arm's
24 emissions, elidable `Static` envelopes fell 21 → 1, `validity` masks 3 → 0, flat
filter steps rose 1 → 3, and one params map appeared: the models adopted the taught
shorthand, and every one of those emissions decoded through the released decoder the
harness gates with. (Under the canonical pack the models already emitted bare columns
uninstructed — the leniency was silently absorbing dialect before this phase taught it;
what the teaching adds is the envelope elision, which nothing emitted spontaneously.)

**Flips, honesty-framed per the 839 rule.** Three cells changed verdict — stress-001
claude (judge, up), stress-002 claude (parse fail to clean, up), stress-006 gpt (parse
fail to clean, judge still down) — 3/24 ≈ 12.5%, inside the instrument's ~33%
same-prompt repeat error at n=1, and all three moved UP. So this sweep demonstrates the
absence of a decode-rate regression (the per-family decline trigger — decode rate
dropping on shorthand — did not fire for either family; parse in fact rose on both) and
cannot certify the success-rate gain. The output-token delta is the more robust reading:
it is an aggregate over 24 cells with a verified mechanism, not a verdict flip — but it
is still n=1 per cell, and the per-cell spread (−136 to +84) is wide.

**Cost of the sweep:** one arm only (the canonical arm reused): 22,166 metered input +
13,929 output tokens, 505,928 cache-read + 59,368 cache-create across 24 cells, plus the
unmetered judge leg — ≈ $1–2 by the 839 gate's same-shape yardstick. Well inside the
$15 cap.

## Per-family verdicts (the Phase 843 compiler input)

| Family | Decode on dialect | Output delta | Verdict |
|---|---|---|---|
| `claude-opus-4-8@low` | 12/12 (was 11/12) | −6.0% | **ADOPT** — dialect eligible as this family's pack dimension |
| `gpt-5.6-terra@low` | 11/12 (was 10/12) | −6.3% | **ADOPT** — same |

Neither family tripped the phase's decline rule ("a family whose decode rate drops on
shorthand keeps canonical") — both rose. The verdicts are adoption of the dialect as a
**per-family pack dimension for the Phase 843 compiler**, NOT a flip of the default
artifact: the canonical pack remains the default; the dialect variant is a sibling
emission selected per family. What would reverse a verdict: a reproducible per-family
decode drop at n ≥ 3 or in the Tier-A/B mini-window — the same escalation the
839/838/841 gates name.

**Epoch input (Phase 840):** dialect variant at `docs/prompt-pack-lenient/`
(`system-prompt.md` 70,544 B / 19,053 o200k; `few-shot.jsonl` 10,827 B / 2,920 o200k;
pack total 81,371 B / 21,973 o200k; composed-prompt `sha256:6c0d6296…`); canonical
baseline `ed89b27` content (80,891 B / 21,924 o200k). Output-token delta −6.0% (claude)
/ −6.3% (gpt) at decode 12/12 + 11/12. Appendix: 60 fixtures, 28 families, 7
taught-primary. Corpus: unchanged (no fixtures authored).

# Per-family compiled-pack census — Phase 843 (fuaran)

**Date:** 2026-08-15. **Charter:** Phase 843 — the pack as a COMPILATION TARGET
over the flip history. One pack serves every family, so it carries the union of
every family's needs, while the record above says which family each teaching
flipped for and the dialect verdicts are already per-family. `authoring-pack.fsx
--family <id>` emits a variant carrying the always-core plus every section that
family's record does not show to be unnecessary, in that family's own adopted
dialect. Each variant is a versioned artefact with an inclusion manifest under
the Phase 383 hash discipline.

**Amended framing (operator direction 2026-08-15, post-842), and it is what this
entry is shaped by.** Cache posture is a MEASURED input per deployment, never a
family label — the 842 shakedown found a family taking deep prefix-cache hits
against its own reputation, and a deep-cache family whose token fade saved
nothing. So the compiler selects TEACHING CONTENT per family and says nothing
about which posture should be served which variant: a warm posture can afford
the RICHER pack and a genuinely cold one cannot, and the same family can be
either. Sizing is a measurement, not a compilation dimension.

## What shipped

| Artefact | Path | Generated by |
|---|---|---|
| The section-demand index | `docs/tools/section-demand-index.json` | `authoring-pack.fsx --write` (the default run — its inputs both live in this repo) |
| The demand-attribution ledger | `docs/prompt-pack/demand-attribution.jsonl` | the evaluation harness's extractor; extraction only — every classification is made here |
| The compiled variants | `docs/prompt-pack-variants/<family>/` | `authoring-pack.fsx --write --family <id or all>` |
| The build gate | FAKE target `AuthoringPackFamilies` (`--check --family all`), wired into `Check` | — |

Three invariants, all asserted rather than intended:

- **A bare `--write` reproduces the canonical adopted emission unchanged.** The
  variants are their own artefact set behind their own flag, so neither emission
  can silently revert the other — the same separation the dialect run already has.
- **An empty exclusion set reproduces its source pack BYTE FOR BYTE**, verified
  by comparison, not by inspection: a canonical-dialect variant is
  byte-identical to `prompt-pack/`, a lenient-dialect one to
  `prompt-pack-lenient/`. That is what makes "the variant differs in exactly its
  excluded sections" a fact.
- **The per-host dimension cannot reach the committed variants.** It is off
  unless a host registry manifest is passed, and a run carrying one must name
  `--out`, which is the only place it writes.

## The section-demand index — and the finding is the sparsity

Two recorded-flip sources, ONE classifier. The census above is the first; the
demand-attribution ledger is the second, and the division of labour across the
repo boundary is deliberate — the harness EXTRACTS (date, cluster, families,
text) and this repo CLASSIFIES, so the public artefact is reproducible from its
own committed inputs.

Three mechanical steps, each recorded per row: resolve a record line to at most
ONE section by longest matching phrase (a table row's first section-naming cell
outranks the rest of the row — the verdict column routinely cites a section it
is not about); attribute to families by word-boundary alias match; classify by
marker. `needed` wins a conflict with `never-needed`, which is the conservative
direction: a section with any live flip evidence is teaching a family has been
measured to want, and the cost of keeping it is tokens where the cost of cutting
it is a regression.

| Family | needed | never-needed | unknown → included | cuttable |
|---|---:|---:|---:|---:|
| claude-opus | 3 | 0 | 21 | 0 |
| claude-sonnet | 0 | 0 | 24 | 0 |
| gemini | 2 | 0 | 22 | 0 |
| gpt | 3 | 0 | 21 | 0 |
| grok | 3 | 0 | 21 | 0 |
| kimi | 0 | 0 | 24 | 0 |

**Zero sections are never-needed for any family, so no variant excludes
anything, and that is the honest headline rather than a shortfall in the
instrument.** The record's family attribution is SPARSE by construction: it
verdicts sections, and it names families only where a flip was measured
per-arm. Eleven attributions fired across five sections, and every one of them reads
correctly against its source — the toned-pill section attributed to all four
arms its 35/35 flip ran on, the scalar-slot derivation to the three families its
cluster was censused on, the conditional-rendering section to the two the
selection-driven demand was raised by. What the record does not carry is the
other polarity: a finding that a teaching's target behaviour is ABSENT on a
named family's current generation. It carries exactly one such shape (the
generation-scoped arithmetic reading), and that teaching was already cut
wholesale, so it excludes nothing further.

The consequence is worth stating rather than burying: **a per-family gate is the
only instrument that produces never-needed evidence, so the compiler had to
exist before the cuts could.** This phase ships the mechanism and leaves the
cuts to the pass that buys cells.

### Always-core — enumerated in every manifest, never implicit

Four sections by ROLE, plus every section two or more families' records both
name. A compiled pack must be able to show what was structurally out of the
compiler's reach.

| Section | Because |
|---|---|
| _(preamble)_ | the framing every posture reads |
| The canonical wire shape | wire-shape — the emission contract itself |
| The node kinds you can emit | type-surface — the spelling-complete signature catalogue (613: a slimmed vocabulary craters) |
| Rules | contract — the self-wiring / editable / Call-into rules |
| Deriving ONE value from data | cross-family-flip — needed by three families |
| Distinguishing rows by value | cross-family-flip — needed by four families |
| Conditional rendering | cross-family-flip — needed by two families |

Each manifest also lists the PINNED EXEMPLARS its always-core sections carry, so
"always-core" names the examples it protects and not only the headings — the
Phase 841 pin (the miner may never displace a system-prompt block) applied to
the compiler.

## The variants — sizes in each family's OWN currency

The dialect is the only dimension that varies on this epoch: the two families
whose Phase 840 verdict was ADOPT compile from the lenient sibling pack, the
other four from the canonical one. Sizes below are measured in each family's own
counter — the reference column is exact and every other is a calibrated
estimate, so a marginal figure here is not a finding.

| Family | Dialect | Variant `sha256:` | Bytes | Prefix (own tokens) | Few-shot (own tokens) | Total |
|---|---|---|---:|---:|---:|---:|
| claude-opus | lenient | `9e7a8049…` | 81,371 | 28,050 | 4,332 | 32,382 |
| claude-sonnet | canonical | `330961fb…` | 80,891 | 21,348 | 3,564 | 24,912 |
| gemini | canonical | `330961fb…` | 80,891 | 15,090 | 2,520 | 17,610 |
| gpt | lenient | `9e7a8049…` | 81,371 | 18,931 | 2,919 | 21,850 |
| grok | canonical | `330961fb…` | 80,891 | 19,680 | 3,287 | 22,967 |
| kimi | canonical | `330961fb…` | 80,891 | 18,829 | 3,146 | 21,975 |

**A reconciliation, because the prefix column will otherwise look like it
disagrees with the Phase 841 ledger.** That ledger prices the FILE (18,796 in
the reference counter); this column prices what is SENT, which is the file minus
its generated-file banner comment — 18,765 for the same bytes. A 31-token
difference, in the honest direction, and the sent figure is the one a cut is
paid in.

**What the dialect dimension is worth on the prefix: almost nothing, as
predicted.** Measured in their own counters, the two ADOPT families' variants
cost 62 and 42 tokens LESS than the same families would pay on the canonical
pack, against prefixes of ~32,400 and ~21,900. The Phase 840 ledger already said
this — the dialect passage costs what the re-emitted examples pay back — and its
claim was never the prefix but the −6.0% / −6.3% OUTPUT-token delta with a
verified adoption mechanism. A per-family compiled pack does not become
worthwhile by shrinking; it becomes worthwhile when the record can say what a
family does not need.

## The optional per-host dimension — default OFF, and off is the guard

`Custom` is emittable only for a `(moduleId, componentId)` pair the host has
registered, and the pack registers none, so the escape hatch is unreachable in
the shipped artefact BY DESIGN. The compiler can compile a host's registry into
a variant as a generated section: the closed-enumeration framing plus one block
per component carrying its props contract and its own exemplar tree, minified
through the same scanner the pack's example blocks use. **No hand-authored
content anywhere** — the section is generated from a supplied
`fuaran.pack.host-registry/v1` manifest, and an exemplar that is not parseable
JSON fails the compile rather than riding into a paid prefix as teaching.

The evidence is the 2026-08-15 sentinel probe recorded by the evaluation
harness: taught as a closed enumeration ("this is the registry in full; any
other pair does not exist here") with ONE exemplar, both families read it as an
enumeration rather than a hatch — zero foreign pairs, zero diversion under gap
pressure, zero substitution where a typed kind sufficed, exact contract fidelity
from that single exemplar. It does not license a many-component registry, a
component overlapping a typed kind, or the families the probe did not reach,
which is precisely why the dimension is off by default and why SCALE is a
measured question this phase does not answer.

Worked example: `docs/tools/host-registry.example/` (one component). Compiled
with

```
dotnet fsi docs/tools/authoring-pack.fsx --write --family <id> \
    --host-manifest docs/tools/host-registry.example/registry.json --out <dir>
```

it appends **1,586 bytes** to the prefix. Two guards, both structural rather
than remembered: `--host-manifest` without `--out` is refused, and `--out`
without `--host-manifest` is refused — so a host-specific variant can neither
overwrite the committed host-free set nor be produced by accident.

## Not run here (the paid legs)

The per-family gate and the economics re-measure are cells and belong to the
imminent measurement pass. They are wired, not executed: the harness binding is
an explicit flag whose default remains the shared pack (so the control arm stays
the control arm), every cell records the variant it ran under, and the run plan
for both legs is written up on the harness side rather than re-derived mid-pass.
Nothing in this entry was measured by spending on a provider except where it
quotes a figure the record already carried.

**Epoch input (Phase 843):** canonical pack unchanged at `ed89b27` content
(80,891 B / 21,924 in the reference counter); compiled variants at
`sha256:330961fb…` (canonical dialect, 4 families) and `sha256:9e7a8049…`
(lenient dialect, 2 families). Index: 24 sections × 6 families, 11 attributions,
0 never-needed, 7 always-core. Corpus: unchanged (no fixtures authored).
