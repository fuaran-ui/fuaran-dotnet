# Core API asks

Changes this language tier would make to the `Fuaran.Core.*` substrate if it owned it, recorded here
because it does not: the substrate is a separately released package family, and this repo pins a
published version of it. Each entry states the ask, the shape in this repo that works around its
absence, and what the workaround costs — so that when the substrate does move, the reader can find
the code that should be deleted rather than re-deriving it.

An entry stays until the ask lands **and** the workaround here is removed. An entry is not a to-do
for this repo; it is a note to whoever next reads the workaround and wonders why it exists.

---

## `ChainBreak.Reason` should be a closed DU, not a `string`

**Ask.** `Fuaran.Core.ChainBreak` reports why a chain walk failed as
`Reason: string`, with the walkers emitting four literal values
(`"sequence-number mismatch"`, `"prev-hash link broken"`,
`"hash mismatch (tampered op/actor/seq)"`, `"hash mismatch (tampered capture)"`). It should be a
closed discriminated union, so a consumer's classification is a total match the compiler checks.

**Workaround here.** `Verify.classify` in
[`src/Fuaran.UI.OpStream.Abstractions/HashChain.fs`](../src/Fuaran.UI.OpStream.Abstractions/HashChain.fs)
maps those four strings onto the domain's own `ChainBreakReason` DU, with an explicit
`Unrecognised of reason` arm for anything else.

**What the workaround costs.** The mapping is a string comparison against literals this repo copied
out of the substrate's source, so a substrate release that *rewords* a reason silently reclassifies
it as `Unrecognised` — the failure is visible rather than wrong, which is the point of the arm, but
it is still a break that a typed reason would have made a compile error instead. And because
`VerificationError` is a shipped closed DU in this tier, `Unrecognised` cannot be surfaced through
it without a major bump, so `Verify.segment` / `Verify.chain` project an unrecognised reason onto
`HashMismatch`. A caller that needs the distinction must call `classify` directly.

**When it lands.** Delete `ChainBreakReason` and `Verify.classify`, match Core's DU directly in
`ofChainBreak`, and delete this entry.

_Recorded by Phase 1525 (finding L-A8)._
