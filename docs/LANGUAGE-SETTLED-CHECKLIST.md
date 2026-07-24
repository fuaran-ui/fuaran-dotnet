# Language-settled checklist – the objective trigger to start LLM testing

This checklist encodes the **objective bar** for declaring the Fuaran language
tier *settled* – the point at which it is worth committing the (expensive,
multi-day) LLM evaluation budget to measure the language on a **stable** target
rather than a moving one.

It exists because the settle→test transition was previously a gut call. The
strategy is "harden the language until it stops changing under exercise, **then**
test the best version." This document makes that a checkable decision: each item
below is a yes/no with a **named producing signal** (the command or artefact
that answers it). The release-gate testing wave's entry criterion references this
checklist as its promotion gate.

The gate is **all-must-pass**. A single `[ ]` blocks promotion; re-run the
producing signal after the gap closes.

---

## The bar

### (a) No Critical/High language or wire-format gap

- [ ] A fresh **structured gap scan** over the language tier returns **no
  Critical and no High** gap classified as a *language-surface* or
  *wire-format* defect. (Medium/Low gaps and gaps scoped to the runtime/consumer
  tiers do not block – the bar is about the *language contract* being settled.)

**Producing signal:** run the maintainers' structured gap scan;
read the Critical/High groups; confirm none is language-surface or wire-format.

### (b) A real-app translation completes without growing the contract

- [ ] A real consumer-view translation through the typed surface – the standing
  language-exerciser loop – completes **without adding a new `NodeKind` /
  `Spec` / `Binding` / `Action` case**. The exercise harvests *author-surface*
  conveniences and *portability seams* (those are additive and expected); it must
  **not** force a new core wire-format case. If it does, the language is not yet
  settled – close the gap, then re-run.

**Producing signal:** the second-real-app exerciser walkthrough (the case-study
gap-harvest doc); confirm its surfaced-gap list contains no new core
`NodeKind`/`Spec`/`Binding`/`Action` case (only additive author-surface /
portability-seam items, each either shipped or recorded).

### (c) The authoring corpus emits without surfacing a new contract need

- [ ] An AI consumer emitting the canonical authoring corpus (the prompt set the
  release-gate eval scores against) produces valid trees **without** any prompt
  surfacing a missing contract case. A prompt that can only be satisfied by a
  new `NodeKind`/`Spec` is a settle blocker, not an eval result.

**Producing signal:** the release-gate emission micro-eval over the canonical
prompt set; confirm no run records a "no expressible tree – missing contract"
outcome (distinct from a low *quality* score, which is a legitimate eval result
and does **not** block).

### (d) Cross-host conformance is green and covers the divergence zones

- [ ] The F# ↔ TS cross-host wire conformance gate is **green** over the full
  `wire-format-fixtures/` corpus, **and** the corpus covers the known
  divergence zones – in particular the canonical float-formatting zone
  (Phase 117) where F# and TS must encode
  byte-identically outside the int53 range.

**Producing signal:** the F# `JsonDecode` corpus suite + the TS `@fuaran-ui/ops`
corpus/idempotence suite both pass; the corpus `manifest.json` includes the
float-zone fixtures.

---

## How to run the gate (runbook)

Run each signal in order; tick the matching item above. The whole gate is a
~30-minute pass (no LLM spend – the expensive eval is what this gate *authorises*,
not what it runs).

| Item | Command / artefact | Pass condition |
|---|---|---|
| (a) | The maintainers' structured gap scan | No Critical/High gap tagged language-surface or wire-format |
| (b) | The second-app exerciser case-study walkthrough's surfaced-gap list | No new core `NodeKind`/`Spec`/`Binding`/`Action`; additive-only |
| (c) | Release-gate emission micro-eval over the canonical prompt set | No "missing contract case" outcome on any prompt |
| (d) | `cd fuaran; dotnet run --project Build.fsproj -- Test` (JsonDecode corpus) **and** the TS `ops` corpus suite | Both green; float-zone fixtures present in `manifest.json` |

When all four are `[x]`, the language is **settled**: promote the testing wave
and spend the eval budget against this version. Record the pass date + the
commit each signal was run against, so the eval results are attributable to a
known language snapshot.

### Settle decision log

| Date | (a) | (b) | (c) | (d) | Decision | Language snapshot (commit) |
|---|---|---|---|---|---|---|
| _pending first run_ | – | – | – | – | – | – |
