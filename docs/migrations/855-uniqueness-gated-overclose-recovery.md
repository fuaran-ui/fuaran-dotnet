# Phase 855 — uniqueness-gated over-close recovery

`Fuaran.UI` 0.26.0. Additive decode behaviour, additive public surface. **No consumer action is
required**; read the counter section if you measure decoder reliance.

## The class

The mirror of the Phase 850 class, at the same emission boundary with the sign reversed. Phase 850's
emission owes a closing brace and drops it; this one emits a closer it does not owe:

```
… "trendFormat":{"$type":"Percent","decimals":0},"tone":"Success","icon":"users"}}}
                                                                                ↑ surplus
,{"id":"customers-sparkline","kind":{ …
```

`kind`'s last field is a plain string, so the child owes exactly two closers — one for `kind`, one
for the node wrapper. It emits three, closing one level past the node. Measured across the stored
emission corpus: a steady low-rate mode over four model families, twenty-six days and five decoder
versions — never a stray bracket in open ground, and in 14 of 16 sitting at the end of a contiguous
closing run of ≥ 2, with a `children[]` sibling, the array's own `]`, or a property on the array's
parent following it.

It is not truncation. Every instance ends with a complete token, none is cut inside a string, and
every one fails strictly before the last character — in the worst case 63 % of the document follows
the failure point, intact.

## Why this gate is not Phase 850 with a minus sign

**An owed closer has exactly one legal home.** When `]` arrives with objects still open inside the
array, the grammar admits precisely one repair: close them, there and then. That is why Phase 850
could measure ancestor-legal auto-close as determined and recover its whole set.

**A surplus closer has as many candidate homes as there are enclosing levels**, and every choice
re-assigns the fields that follow it to a different owner. Both documents parse. Both decode. The
decoder has no ground on which to prefer one, and neither does the wire spec.

Measured rather than argued. Every deletion set of the surplus size was drawn from the document's
structural closer positions, kept if it parsed, de-duplicated by parsed value, and decoded through
this decoder:

| | Instances |
|---|---:|
| Stored instances characterised | 16 |
| **Grammar alone** — exactly one distinct repair | 6 |
| **Grammar alone** — two to five distinct repairs | **10** |
| **After decoding every candidate** — exactly one decodes clean | 10 |
| **After decoding every candidate** — two to five decode clean | **6** |
| At least one canonical repair exists | 16 |

**What the residual ambiguity costs is field ownership, not node placement**, which is exactly what
makes it dangerous — it is invisible in the tree outline. On the worst cell, a single character
deleted at five different positions produces the identical node skeleton and five different owners
for `format`, `trend`, `trendFormat`, `tone` and `icon`:

| Deletion at | `format` | `trend` | `trendFormat` | `tone` | `icon` | Decodes |
|---|---|---|---|---|---|---|
| **872 (leftmost)** | `value` | `value` | `value` | `value` | `value` | **clean** |
| 913 | `kind` | `format` | `format` | `format` | `format` | clean |
| 953 | `kind` | `kind` | `trend` | `trend` | `trend` | clean |
| 1000 | `kind` | `kind` | `kind` | `trendFormat` | `trendFormat` | clean |
| **1031** | `kind` | `kind` | `kind` | `kind` | `kind` | clean |

Only the last row is the tree the emission evidently intended. The first buries all five fields
inside the `Static` binding, so the metric renders as a bare unformatted number with no trend arrow
and no icon — **a materially different UI that passes every gate**. A recovery that picked the
leftmost legal deletion, which is the obvious implementation, picks exactly that row.

That is why the reliance counter cannot rescue this class the way it rescues Phase 850's. It can
report *that* a coercion happened; it cannot report that the coercion chose the right owner, and
nothing downstream can either.

**A directly-verified sub-finding, and it is why schema guidance does not disambiguate.** The
leftmost row decodes clean even though `Static` is declared as `{ "$type", "value" }`: the decoder
accepts undeclared fields on a `$type`-tagged binding. Whether to tighten that is a real question
with its own blast radius; it is recorded here and deliberately not folded in.

## The contract

- **Reached only** after `tryParse` fails with `INVALID_JSON` **and** the Phase 850 recovery
  declines. The two never contend: 850 fails closed on an over-closed document, which is precisely
  the profile this gate requires. A document that parses never enters this code.
- **Profile-gated.** A string-aware structural scan must show the document net over-closed by one or
  two closers, with the surplus uncompensated (running minimum depth equals final depth), and not
  cut inside a string. A document outside the profile is **not counted** — it was never in the
  class, so counting it as a refusal would dilute the signal the counter exists to carry.
- **Delete-only, bounded.** Candidates are the document with one or two structural closers removed.
  Nothing is inserted; no key, value or bracket is invented. Bounds, stated as literals in the
  source: surplus ≤ 2, ≤ 512 closer positions, ≤ 8192 deletion sets, ≤ 32 distinct parseable
  candidates. Every measured instance fits well inside them (the largest needs 2,628 deletion sets
  and yields five distinct candidates).
- **Accept iff exactly one candidate decodes clean.** Uniqueness is evaluated over the complete
  enumeration, **de-duplicated by parsed value** — deleting either of two closers separated only by
  whitespace yields two different strings and one document, and counting that as two repairs would
  refuse a genuinely unique cell. (It would also have mis-scored one of the sixteen.)
- **Refuses by default.** Zero clean candidates, two or more, or an enumeration past the bounds all
  return the **original** `INVALID_JSON` with its offset intact. An error the demand loop can feed
  on is worth more than a wrong tree no counter can flag.
- **The failure-offset rule orders, never decides.** "The repair lies in the contiguous closer run
  ending at the first mismatch" selects a schema-clean repair in 11 of 16 and decides 5 of the 6
  ambiguous cells — but "schema-clean" is not "what the model meant", and 5 of 16 have their true
  repair outside that run. It is used for candidate ordering only, and ordering cannot change a
  verdict that is a count over the whole enumeration.
- **`decodeOp` is untouched** — the strict parse stands; the class is node emissions.

## Counters

`JsonDecode.Reliance` gains two ids, and their being **two** is the point:

| Id | Fires when |
|---|---|
| `Reliance.OverCloseUnique` = `"over-close-unique"` | the gate accepted a unique repair |
| `Reliance.OverCloseRefused` = `"over-close-refused"` | the profile matched and the gate declined |

A class that is silently recovered stops generating demand signal; a class that is silently refused
stops generating it too. Both strings are stable identifiers, on the same footing as a `DecodeError`
code. Read them through the existing `count` / `snapshot` / `reset`.

## The labelled set

`src/Fuaran.UI.JsonDecode.Tests/overclose-fixtures/` holds every stored instance of the class plus
the live replications from the same measurement pass — **28 emissions, verbatim** (code fences
stripped: the exact bytes the decode gate received). Building it is the phase's irreducible cost,
because the class has no other oracle.

- **14 admit exactly one clean repair.** Their labelled intended tree is committed beside them as
  `intended-NN.txt`, verified by inspection: in every case the deletion sits at the boundary where a
  sibling begins or a parent closes, and the surviving structure is the one the emission evidently
  meant. The suite asserts the gate reproduces that tree **exactly** (canonical re-encode equality),
  not merely that some tree decoded. Zero wrong acceptances is the gate, not the target.
- **14 admit two to five clean repairs and are labelled REFUSE.** Their correct repair is unknowable
  by construction. A gate that picks one of them is the defect.
- **`leftmost-legal-14.txt`** pins the wrong fix the way Phase 850 pinned EOF-close: it decodes
  perfectly clean, so a leftmost-first gate would accept it, and the suite asserts both that it is
  available and that the gate refuses the emission anyway.

One divergence from the characterisation pass is worth recording rather than smoothing over: one
34 KB pretty-printed instance was counted there as admitting three clean repairs and is unique here.
The three differ only in which of three whitespace-separated closers is deleted and parse to the
identical value — so de-duplicating by parsed value, which this gate does, is what reconciles them.
The reproduction otherwise matches the published counts cell for cell.

## Follow-on staging (recorded here, deliberately not executed in this change)

On the Phase 850 pattern:

- **Shared-corpus fixture family** — pending. The over-closed class must **not** be added to the
  `lenient-accept` family, which is for loss-free spelling normalisation only. This is error
  recovery, and a recovery whose default is refusal is doubly not a normalisation.
- **`WIRE_FORMAT.md` optional-host-behaviour note** — pending, alongside 850's.
- **Sibling-host ports** — pending. A host that does not implement the gate is not non-conformant:
  refusing every over-closed document is the *conservative* end of the range this gate occupies.
- **Schema tightening on `$type`-tagged bindings** — filed as a separate question, not folded in.

## See also

- [`850-implied-node-close-recovery.md`](850-implied-node-close-recovery.md) — the sibling class,
  whose posture this extends rather than weakens.
- [`../../STABILITY.md`](../../STABILITY.md) — the 0.26.0 recorded change.
