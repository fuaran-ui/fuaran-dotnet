<!--
  GENERATED FILE (Phase 840) — the leniency-surface classification behind the
  lenient emission dialect. Produced by docs/tools/authoring-pack.fsx from the
  wire-format-fixtures manifest's lenient-accept family + the classification
  table in the generator; drift-checked in the build. Do not hand-edit — edit
  the generator's `leniencyFamilies` table and rerun `authoring-pack.fsx --write`.
-->

# The lenient-dialect appendix — Phase 840

Classification of the ENTIRE decoder-leniency surface, as pinned by the corpus's
`lenient-accept` fixture family (60 fixtures — `manifest.json` authoritative). Every
fixture id is claimed by exactly one family below (asserted at generation, so a
new leniency cannot land unclassified). Classes:

- **taught-primary** — TOTAL and LOSS-FREE and token-positive: the decoder
  provably normalises the shorthand to exactly the canonical semantics for every
  legal input, and the shorthand is cheaper. Taught as the primary emission
  dialect in `prompt-pack-lenient/`; the pack transform applies exactly these,
  each block decoder-proved (`dialect-verify.fsx`).
- **safe-not-taught** — total and loss-free, but token-neutral/negative (synonym
  acceptance for model priors) or catalogue-contradicting. Stays a safety net.
- **already-canonical** — the TERSE side of the pair is the canonical form; the
  leniency accepts the verbose spelling. Nothing to teach beyond the canonical
  rule the pack already teaches.
- **never-taught** — partial, heuristic, or contextual: normalisation cannot be
  proved loss-free for every legal input (unproved ⇒ unsafe by default).

The Δ column is mechanical: minified EXPECTED (canonical) bytes − minified
INPUT (shorthand) bytes, summed per family (positive = the shorthand side is
cheaper; 0 = identity or equal size; negative = the fixture's input is the
VERBOSE side — the accepted-not-preferred direction). It measures the corpus
pins, not the pack — the pack-level ledger lives in the census.

| Family | Class | Δ bytes | Fixtures | Evidence / judgement |
|---|---|---:|---|---|
| Static-envelope elision (bare scalar / array in a Binding slot) | taught-primary | -49 | `lenient-shape-binding-scalar-fraction` | JUDGEMENT: total + loss-free — §3.6: every Binding case is $type-discriminated, so a bare array/scalar can only mean Static; bare objects and null stay refused (ambiguity preserved). Token-positive: ~24 chars per slot. Decoder-proof per emitted block. |
| Option as bare string (label = value) | taught-primary | +233 | `lenient-shape-options-bare-strings`<br>`lenient-shape-segmented-orientation-omitted` | JUDGEMENT: total + loss-free on its domain — a bare string option denotes exactly {label:s, value:s} (§3.6 SelectOption rule, the HTML <select> prior). Applied only where label equals value; distinct labels keep the object form. (The segmented fixture also pins orientation-omitted-⇒-Horizontal — the omitted-default family below.) |
| Embedded source column as bare array (validity elided) | taught-primary | +142 | `lenient-transform-bare-columns` | JUDGEMENT: total + loss-free when the mask is all-true — the wire has no JSON null, so a bare array can only denote all-present (§3.6, §16.1 explicitly PREFERS this form). Guarded: a column with any false validity keeps its envelope. |
| Schema omission (inferable column types) | taught-primary | +74 | `lenient-transform-schemaless` | JUDGEMENT: loss-free ONLY on the guarded domain — types infer deterministically for string/int/float/bool (fuaran-core columnar codec authority); date/timestamp NEVER infer and empty/mixed refuse. The transform drops a schema only when inference reproduces every declared type exactly (checked per column), so e.g. a float column of integral literals keeps its schema. §16.1 PREFERS the omitted form on this domain. |
| Transform.params as a name→binding map | taught-primary | +15 | `lenient-shape-params-map` | JUDGEMENT: total + loss-free — params are a name-keyed SET (ColExpr.Param lookup), so object key order carries no meaning (§3.6, contrast the refused options-map form where order IS meaningful). Applied only when every element is exactly {name, from} with distinct names. |
| Flat filter step (column/op/param) | taught-primary | +301 | `lenient-transform-flat-filter`<br>`lenient-transform-flat-contains` | JUDGEMENT: total + loss-free on its pinned domain — one binary comparison of a column against a param with op eq (flat-filter) or contains (flat-contains). The flat spelling for literal right-hands or composed predicates is NOT corpus-pinned, so those keep the full pred form (teaching an unpinned shorthand would be a private dialect, §16). Largest per-occurrence saving (~90 chars per wired filter step). |
| DateRange value as the bare [from,to] pair | taught-primary | +12 | `lenient-daterange-bare-array` | JUDGEMENT: total + loss-free — a two-element array at a DateRange value position maps uniquely onto {from, to} (pinned cross-host by the fixture). |
| Compact composites (multi-family exemplars) | composite | +1811 | `lenient-filterable-static-dashboard-compact`<br>`lenient-grid-field-named-compact`<br>`lenient-grid-transform-param-compact`<br>`lenient-master-detail-preselected-compact`<br>`lenient-scalar-transform-composition-compact` | Whole-tree fixtures composing several taught families (bare columns, schema omission, wired filters). Several are pack exemplars already — the shipped dialect the 841 miner was constrained to preserve. |
| values-only column envelope | safe-not-taught | +46 | `lenient-transform-values-only-columns` | Safe (validity restores all-true) but strictly dominated by the bare-array form — an intermediate spelling with no reason to teach it. |
| Literal-envelope acceptance (bare string IS canonical) | already-canonical | -20 | `lenient-bare-text-button-label`<br>`lenient-bare-text-callout`<br>`lenient-bare-text-heading`<br>`lenient-bare-text-markdown` | 0.2.0 direction-flip (§16 rule 1): the bare string is the canonical TextSource form; the leniency accepts the VERBOSE {"$type":"Literal"} envelope. The terse side is already taught as canonical. |
| Bound-wrapper unwrap in Binding value positions | already-canonical | -56 | `lenient-binding-bound-wrapper` | fuaran#633: {"$type":"Bound","binding":B} in a Binding slot unwraps to B. The wrapper costs MORE tokens than canonical — a safety net for a TextSource-convention carry-over, nothing to teach. |
| Explicit defaults accepted (canonical omits them) | already-canonical | -433 | `lenient-460-explicit-default-column`<br>`lenient-460-explicit-default-metric`<br>`lenient-460-explicit-default-style`<br>`lenient-596-form-explicit-auto-state`<br>`lenient-fact-explicit-defaults` | Omitted-when-default is the canonical posture on both boundaries (§3.6): the leniency accepts the VERBOSE explicit spelling and re-encode drops it. The terse side (omission) is already the taught rule — restated in the dialect passage, no transform needed (canonical corpus bytes already omit). |
| Static-envelope unwrap at plain-value fields | already-canonical | -29 | `lenient-shape-static-envelope-plain-scalars` | The INVERSE confusion (§3.6): models wrap plain fields in Static envelopes; the decoder unwraps. Canonical at a plain field is the bare value — already the terse form. |
| DateRange value in a Static envelope | already-canonical | -27 | `lenient-daterange-static-envelope` | Same inverse-wrap acceptance at the DateRange position; canonical is the bare {from,to}. |
| Enum-value aliases | safe-not-taught | -172 | `lenient-460-alias-emphasis-muted`<br>`lenient-460-alias-emphasis-strong`<br>`lenient-460-alias-tone-danger`<br>`lenient-460-alias-tone-positive`<br>`lenient-tonedpill-tone-aliases` | Total (each alias maps to exactly one canonical case, §3.6 tables) but token-NEUTRAL — synonym acceptance for model priors, not compression. Teaching them would displace the catalogue's canonical spellings for zero gain. |
| Enum-spelling coercion at bool emphasis slots | safe-not-taught | -26 | `lenient-022-lvr-emphasis-loud`<br>`lenient-022-lvr-emphasis-normal`<br>`lenient-emphasis-cross-vocab` | Cross-vocabulary re-typing ("Loud"→true at a bool slot; true→"Loud" at the enum slot): total for the accepted spellings but the canonical bool is already the terse form. |
| Field-name aliases | safe-not-taught | -324 | `lenient-alias-call-url`<br>`lenient-alias-card-title-metric-value`<br>`lenient-alias-datagrid-data-column-type`<br>`lenient-alias-form-field-name`<br>`lenient-alias-grid-columns-row`<br>`lenient-alias-navigate-href`<br>`lenient-alias-select-options-query-deps`<br>`lenient-tonedpill-tonemap-alias` | Total (same concept, same semantics, §3.6 table; canonical wins when both present) but token-neutral or negative — the canonical names are as short or shorter (route vs href, cols vs columns, map vs toneMap). Synonym safety net, not compression. |
| Pill tag carrying a tone map (→ TonedPill) | safe-not-taught | +5 | `lenient-tonedpill-pill-tag` | Total + unambiguous (a closure Pill can never carry a map — Phase 750, prevents silent data loss) but saves ~1 token and contradicts the catalogue's case name. Not taught. |
| Pipeline step / aggregation aliases | safe-not-taught | +132 | `lenient-transform-step-aliases` | Alias spellings (by→keys, aggregations→aggs, avg→mean, descending→dir, count→n): the canonical names are mostly SHORTER. Synonym acceptance, not compression. |
| Alternate predicate/expression spellings | safe-not-taught | +375 | `lenient-transform-expr-spellings`<br>`lenient-transform-flat-or`<br>`lenient-transform-flat-scalar-fn` | Alternate spellings of the expression algebra ($type-as-op eq, or/exprs n-ary form, call/fn/predicate spellings). Marginally terser in places, but the flat step above covers the dominant case and teaching a second predicate dialect would split model attention for single-digit tokens. Accepted, not taught. |
| Row-major source transposition | never-taught | +138 | `lenient-transform-source-rowmajor` | JUDGEMENT: heuristic + not order-preserving — transposed with the FIRST row's key set (sorted), absent cells null, ragged rows refuse downstream (fuaran#815). Column order is not preserved, so normalisation is not loss-free in general; also token-NEGATIVE beyond ~2 rows (keys repeat per row). Safety net only. |
| State/Static/Bound envelope at a Transform source | never-taught | 0 | `lenient-transform-source-state-rows` | Semantics-bearing (live/snapshot distinction, fuaran#815/#818) — the fixture is now an identity (the State envelope round-trips as a live source), so this is not an emission shorthand at all; teaching it would teach a different meaning. |
| Epoch-integer timestamps | never-taught | +67 | `lenient-transform-epoch-timestamps` | JUDGEMENT: heuristic — seconds-vs-milliseconds is resolved by magnitude (the fixture's own 1752000000 and 1752000000000 normalise to the SAME instant), and the coercion is contextual on a schema declaring timestamp. Not provably loss-free; safety net only. |
| Legacy window-function spelling | never-taught | +144 | `lenient-window-cumsum-legacy` | cumSum→cumulSum is a superseded-spelling seam — §16's own admission law says backward compatibility is NOT an admission ground; teaching it would resurrect a retired spelling. |
| Opaque-sentinel recovery | never-taught | -85 | `lenient-665-rows-opaque-sentinel`<br>`lenient-opaque-static-markers`<br>`lenient-opaque-static-options`<br>`lenient-opaque-static-series`<br>`lenient-opaque-static-values` | "<opaque>" is the §5.1 erasure residue of a survivability boundary, not an authoring form — an author emitting it would be emitting data loss on purpose. |
| null accepted for absence (two positions) | never-taught | -42 | `lenient-null-static-options` | null in a Binding slot is refused in general (ambiguous with absence, §3.6); the two accepted positions normalise to empty. Omission is the taught form; emitting null teaches the refused shape everywhere else. |
| Bare Grid (no cols) → Auto | safe-not-taught | 0 | `lenient-shape-grid-no-cols` | Total (accept-and-canonicalise across kinds, the CSS auto-grid prior) but token-neutral: emitting {"$type":"Auto"} costs the same and matches the catalogue. |
| Grid templateColumns without cols (cols synthesised) | never-taught | +9 | `lenient-shape-grid-template-no-cols` | Contextual synthesis — the decoder inserts cols:1 beside a templateColumns; loss-free only when the intended cols was 1, which the input cannot state. Safety net only. |

## What the dialect variant does with this

`docs/prompt-pack-lenient/` re-emits every example block and few-shot tree in
the taught-primary shorthand via a mechanical transform run to a fixpoint (so
the variant is ONE dialect — no canonical spelling a taught family covers can
survive in an emitted block), and every transformed block is proved loss-free
through the real decoder: `encode(decode(dialect)) == encode(decode(canonical))`,
byte-equal. TreeOp examples stay canonical in both variants: the lenient-accept
family pins NODE decode only, so no op-position shorthand is corpus-pinned
cross-host, and §16 forbids teaching an unpinned one. Regenerate with
`dotnet fsi docs/tools/authoring-pack.fsx --write --dialect lenient` (requires
the Release build of src/Fuaran.UI.JsonDecode.Tests for the decoder proof).

