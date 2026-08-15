# Phase 850 — implied-node-close decode recovery

**Package:** `Fuaran.UI.Ops` (`Fuaran.UI.Ops.JsonDecode`). **Ships in 0.24.0** (additive decode
behaviour — pre-1.0 minor, the same axis as the 0.20.0 Transform-source leniencies).

## The class

A malformed-emission class measured on stored model emissions (2026-08-15): a node wrapper's
closing brace dropped at the end of a `children[]` / `cases[]` element — or, in one shape, the root
node left open at end of input — typically after a run of ≥ 2 closing braces. The envelope is two
levels deep (`{"id": …, "kind": { …spec… }}`), so a child whose last spec field is itself an object
ends in a brace run; the model closes the nested value and `kind` and stops one brace short of the
node's own `}`.

Three properties make it a **decode-boundary** problem rather than a prompt-teaching one:

- The emission is canonical in intent and correct in vocabulary — re-running the recovered
  documents through the canonical decoder, 34 of the 36 stored cells decode clean, and the two
  residuals fail on unrelated `MISSING_FIELD` defects that a well-formed emission would have
  surfaced anyway. The class discards near-perfect intent over a brace count.
- Teaching it has zero measured effect: a worked-example prompt section quoting the exact failing
  fragment as its own wrong half changed nothing — failing emissions re-emit that fragment
  verbatim. This is a token-level generation slip inside a correct plan, not a knowledge gap, so
  the instruction ladder tops out below it.
- It is not truncation: bracket depth never goes negative, no document ends inside a string, and
  the failure offset tracks emission length, not an output cap.

## The remedy — measured, not assumed

Two candidate rules were measured against the 36 stored malformed emissions:

- **Auto-close at EOF — REJECTED.** The obvious leniency ("the document is under-closed; append
  the owed braces") repairs **none** of the mid-document cells: the missing brace is owed
  mid-document, so the `]` that follows it is already mis-parsed and appending closers at the end
  fixes nothing. It would have looked right in a design note and failed on first contact. Pinned
  as a test (`ImpliedNodeCloseRecoveryTests.fs`) so the wrong fix cannot return.
- **Auto-close on an ancestor-legal token — recovers 36/36.** When `]` arrives while node wrappers
  opened inside that array are still open — or the array-level `,` that separates two element
  objects arrives while a wrapper still owes its close — close the owed wrappers implicitly; then
  close whatever a prefix-valid document leaves open at EOF (the root-node shape). 30 of 36 need
  exactly one implied closer; the worst needs 10.

## The contract

`decodeNode` / `decodeNodeObj` attempt the recovery **only** when the raw parse fails with
`INVALID_JSON`. The recovery is:

- **Bounded — insert-only owed closers.** It inserts `}` for wrappers that are demonstrably open
  at a boundary where their close is the only insert-only reading, plus the matching closers at a
  clean EOF. It never invents content, keys, values, or anything that opens structure.
- **Profile-gated.** Mid-document closes fire only into an array keyed `children` / `cases` — the
  node-list positions of the measured class. The same syntactic defect inside any other array
  (data rows, options, props payloads) does **not** recover and keeps its original error.
- **Fail-closed.** Anything outside the profile surfaces the **original** parser error unchanged:
  genuinely-ambiguous nesting (a wrapper mid-key, awaiting a value, or after a separator — e.g.
  `,` followed by a string inside an open wrapper, which reads equally as an owed array element or
  as a key missing its value), over-closed documents, unterminated strings, truncated tails
  (EOF mid-token, after `:` or after `,`), `LIMIT_EXCEEDED` failures (never re-classified), and
  any repair whose re-parse still fails.
- **Scoped to node payloads.** `decodeOp` is untouched: the measured class is node emissions, and
  op payloads keep the strict parse.

A document that parses takes the identical code path it always did — the recovery machinery is
reached only after the ordinary parse has already failed, so valid-document decode behaviour is
byte-identical (asserted over the conformance corpus in the recovery test suite).

## Charter posture — error recovery, not §16 normalisation

This is **not** the wire specification's §16 lenient-accept family. §16 normalises an
alternative-but-valid *spelling*, proved loss-free against the canonical form; recovering a
malformed document is **error recovery**, and the honest objection to error recovery is demand
starvation: a silently-recovered class stops generating the failure signal that drives language
and teaching improvements.

The answer is the **`reliance` counter**: every recovery is counted and surfaced exactly the way
§16 leniencies are measured, so the class stays measurable while ceasing to be a loss.

- Counter id: **`implied-node-close`** (`JsonDecode.Reliance.ImpliedNodeClose`).
- Read side: `JsonDecode.Reliance.count`, `.snapshot`, `.reset` — process-wide, monotonic between
  resets. Consumers measuring how much a cohort of emissions leans on the lenient decoder read
  these counters beside the structural-divergence measurement they already run.
- The write side is internal to the decode entry points; a recovery that fired is always counted.

## Follow-on staging (recorded here, deliberately not executed in this change)

Per the staging pattern established by the 0.20.0 Transform-source leniencies (F# first with
in-repo tests; shared artefacts move deliberately so sibling hosts are never broken from here):

1. **Corpus fixtures** — register the class in the shared `wire-format-fixtures/` corpus as
   `lenient-accept`-adjacent recovery fixtures (a new family or a flagged extension; the corpus
   manifest is the authoritative enumeration), so every conformant host gains the same measured
   acceptance suite.
2. **Wire-specification note** — a §16-adjacent section in `WIRE_FORMAT.md` documenting the
   recovery contract (profile, boundedness, fail-closed, the counter posture) as OPTIONAL host
   behaviour: a host that recovers must count; a host that does not recover is still conformant.
3. **Host parity** — port the recovery to the sibling reference implementations once the corpus
   fixtures exist to certify against.

None of these are in this change: the in-repo fixture suite under
`src/Fuaran.UI.JsonDecode.Tests/recovery-fixtures/` (the 36 stored emissions, code fences
stripped) is the acceptance record until the corpus step lands.

## See also

- `src/Fuaran.UI.Ops/JsonDecode.fs` — the `Reliance` module + `ImpliedNodeClose` scanner +
  `tryParseNodeWithRecovery` (all beside `tryParse`).
- `src/Fuaran.UI.JsonDecode.Tests/ImpliedNodeCloseRecoveryTests.fs` — the 36-emission re-gate,
  the EOF-close counter-example pin, the profile-mismatch / ambiguity / truncation rejects, the
  counter test, and the valid-document identity sweep.
- `STABILITY.md` — "Recorded change — 0.24.0".
- `docs/migrations/12-E-0-json-decoder.md` — the decoder's original algorithm + placement.
