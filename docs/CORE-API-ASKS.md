# Core API asks

Changes this language tier would make to the `Fuaran.Core.*` substrate if it owned it, recorded here
because it does not: the substrate is a separately released package family, and this repo pins a
published version of it. Each entry states the ask, the shape in this repo that works around its
absence, and what the workaround costs — so that when the substrate does move, the reader can find
the code that should be deleted rather than re-deriving it.

An entry stays until the ask lands **and** the workaround here is removed. An entry is not a to-do
for this repo; it is a note to whoever next reads the workaround and wonders why it exists.

---

_No open asks. The last one — `ChainBreak.Reason` as a closed DU (Phase 1525, finding L-A8) — landed
in Fuaran.Core 0.23.0 and its workaround (`ChainBreakReason` / `Verify.classify` in
`Fuaran.UI.OpStream.Abstractions`) was removed when this repo took Core 0.24.0 on 2026-09-15._
