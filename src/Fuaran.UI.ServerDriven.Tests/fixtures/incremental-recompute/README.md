# `incremental-recompute` corpus vectors

The `incremental-recompute` conformance family of the estate's app-composition wire
specification (§12.7), vendored here so `../LiveTransformCorpusTests.fs` runs in any
clone of this repository rather than reporting itself skipped. **These bytes are the
specification's, unedited.** A conformance check that goes green without its oracle is
worse than no check, so the reader refuses a fixture directory it cannot find, and every
member it does not model, by name.

## What a vector is

Each file pairs a **pipeline**, a **source** table, an **edit stream** and the **result**
a correct evaluation over the changed source produces, with a recorded **footprint
triple**: what priming over the source cost, what a full evaluation over the *changed*
source cost, and what advancing the primed state against the edit stream cost.

## What is asserted here, and what deliberately is not

The two halves are not the same kind of claim, and this reader treats them differently
for the reason the family states.

- **The result is a pass criterion, with no allowance.** For every vector, priming over
  the source and then refreshing against the edit stream must produce **exactly** the
  table a full evaluation over the changed source produces, and that table must be the one
  the vector records. That is the whole of what this tier promises a caller, and it is
  asserted on every vector including the ones the seam declines — a decline that answered
  differently would be a defect the footprint would not reveal.
- **The counts are recorded evidence, and are NOT asserted equal.** Engines legitimately
  differ in how much work they can avoid, and this repository's substrate has widened its
  own restricted walk past the boundary these vectors record: two of them record a decline
  where it now restricts. Asserting the recorded class here would make an improvement
  upstream read as a regression, and would be measuring the evaluator the corpus was
  written beside rather than the property that binds this tier.

What *is* asserted about the work is one-directional and safe under any widening: a
refresh never evaluates more rows than the full evaluation beside it, on the vectors whose
own recorded refresh restricts. That is the claim the incremental path exists to make.

## What the reader models

Only what these vectors use — `filter`, `derive`, `groupBy`, `sort`, `window` and `join`
steps; `column`, `literal` and `binary` (`greaterThan`, `multiply`) expressions; `int`,
`string` and null cells; `setCell`, `appendRow` and `removeRow` edits — and it **refuses**
anything else by name. The refusal is per MEMBER, not per verb: it models `lag` and
refuses every other window function, models `semi` and refuses every other join kind,
because a vector whose `cumulSum` it silently read as a `lag` would certify a frame the
corpus did not write. That is a statement about what these bytes have been read against,
never about what the substrate's seam admits.

An `ordinal`-addressed stream is read as what it is and never as an identity one: that
distinction is the whole of §12.7's re-addressing pair, and a reader that collapsed it
would pass the control vector for the wrong reason.

## Keeping them current

They are a copy, so they can go stale. Re-copy the family's directory whenever the
specification re-records it; the reader's per-member refusal means a spelling that moved
arrives as a failing read rather than as a silently different meaning.
