# Decoder robustness: totality evidence

> **This file is generated.** It is rewritten in full by the long fuzz run:
>
> ```
> dotnet run --project src/Fuaran.UI.JsonDecode.Tests -c Release -- --fuzz-long <iterations> [seed]
> ```
>
> Do not hand-edit it. The numbers below are only worth citing because
> nothing can put them out of step with the run that produced them.

## The claim under test

Decoding is **total**: a malformed or hostile input yields a structured,
typed error, never an exception and never a hang. That claim is what makes
it safe to point the decoder at bytes from an untrusted producer — a
language model's emission, a third-party service, a stored document of
unknown provenance — and it is the claim this harness exists to falsify.

It was previously supported by a **curated** reject corpus: inputs an
author chose. That is evidence about the author's imagination. The harness
described here generates inputs nobody chose.

## Methodology

Each generated input is fed to **both** public decode entry points
(`JsonDecode.decodeNodeObj` and `JsonDecode.decodeOp`) and judged against
four invariants:

1. **Totality** — the call returns `Ok tree` or a typed `DecodeError`.
   Any escaping exception is a counterexample.
2. **Termination** — it returns inside a time budget. A soft breach is
   recorded as a counterexample; a hard breach is a genuine hang, and a
   watchdog terminates the process with a diagnosis rather than leaving a
   job to time out with nothing to show.
3. **Bounded work** — allocation stays inside a budget proportional to the
   input, which is what makes adversarial nesting and width visible as
   *super-linear* blow-up rather than as a slow test.
4. **Fixed point** — an accepted input's canonical form re-decodes, and
   re-encodes to itself. `encode(decode(encode x)) = encode x`, fuzzed
   over the reachable accept-space rather than pinned by fixtures.

Inputs come from five families, mixed per iteration:

- **Corpus-seeded mutation** — every payload in the conformance corpus's
  node, op, reject and lenient families, corrupted by one to four stacked
  mutators (character flips, span deletion and duplication, truncation,
  transposition, structural-character repetition, value retyping,
  key deletion and duplication, escape injection, junk prefixes and
  suffixes). Reject fixtures are the most productive seeds available:
  they already sit one edit from the refusal boundary.
- **Near-miss vocabulary** — discriminator values one edit away from a
  real kind name (case changes, pluralisation, a dropped or inserted
  character). This is the class a real emitter produces and the class a
  curated corpus covers worst, because a human writing fixtures reaches
  for obvious garbage.
- **Structure-aware generation** — random documents assembled from the
  real wire key vocabulary, so a generated near-miss reaches into the
  typed decoders instead of bouncing off the first missing field.
- **Crossover** — the prefix of one corpus payload spliced to the suffix
  of another, producing half-valid documents no single-seed mutation
  reaches.
- **Pathological payloads** — nesting, width and string length taken past
  the declared resource limits, assembled as text (building one as a
  value would overflow while constructing the input and prove nothing).

Generation is driven by a self-contained SplitMix64 generator rather than
the platform PRNG, whose sequence is documented as implementation-defined.
Given a seed and a configuration, iteration *N* is byte-identical on every
machine, so a find is replayable from the seed alone — which matters most
for the one failure mode that cannot persist anything on its way out.

Any counterexample is **minimised** by delta-debugging (span deletion
while the failure class holds) and persisted with its seed, iteration and
generating mutator chain.

### What this does not cover

- A `StackOverflowException` cannot be caught on this runtime: the process
  is torn down without unwinding. That is a stated boundary, not a gap —
  the process dying is itself the red signal, and the deterministic seed
  reproduces the input that did it. The resource limits exist so this path
  is not reachable from a bounded payload.
- Allocation is measured per thread, so the budget detects amplification,
  not steady-state footprint.
- This is a decode-side result for THIS host only. The wire format has
  several independent reference implementations, each with its own
  hand-written decoder, and none of them is covered by the figures below
  (see the follow-up section at the end).

### Go-red proof

A harness nobody has seen fail is decoration, and decoration passes. The
invariant machinery is therefore parameterised over the decoder under
test, and the suite runs it against five deliberately-broken stand-ins —
one throwing, one slow, one over-allocating, one whose canonical form is
not a fixed point, one whose canonical form it cannot re-read — asserting
each is caught and classified correctly. A sixth, inverse assertion checks
the unmutated decoder is clean over the same inputs, so the proof shows
discrimination rather than indiscriminate alarm.

## Latest long run

| | |
|---|---|
| Run at | 2026-08-18 10:52:32 UTC |
| Seed | `779` |
| Iterations | 250,000 |
| Inputs decoded | 500,000 (each iteration feeds both entry points) |
| Corpus payloads used as seeds | 259 |
| Max generated payload | 2,097,152 chars |
| Wall clock | 213.8 s |
| **Escapes, hangs, budget breaches** | **0** |

Outcome distribution:

| Outcome | Count |
|---|---|
| accepted (canonical form is a fixed point) | 12,099 |
| `INVALID_JSON` | 291,242 |
| `MISSING_FIELD` | 96,275 |
| `WRONG_TYPE` | 72,916 |
| `LIMIT_EXCEEDED` | 12,047 |
| `WRONG_NODE_KIND` | 8,428 |
| `UNKNOWN_DU_CASE` | 6,677 |
| `EMPTY_NODE_ID` | 316 |

Observed maxima, and the budgets they are measured against:

| | Observed | Budget |
|---|---|---|
| Slowest single decode | 397 ms | 3000 ms soft, 60000 ms hard |
| Largest allocation, ordinary input (411,296 inputs) | 55.0 MiB; peak amplification 2,296 x | 16.0 MiB floor, then 512 bytes per input char |
| Largest allocation, over-closed input (88,704 inputs) | 289.0 MiB; peak amplification 86,307 x | 512.0 MiB |

In each allocation row the byte maximum and the amplification maximum are
**independent** maxima over the run, not two readings of one input: the
largest absolute allocation comes from a large payload, the largest
amplification from a small one. Dividing one by the other would be
meaningless.

No input escaped the refusal contract: every one of the inputs above
returned either a decoded tree whose canonical form is a fixed point, or
a typed error, inside both budgets.

## Open finding: the over-closed recovery path is super-linear

The two allocation rows above are reported separately because they measure
genuinely different things, and blending them would hide the second.

A document with a surplus structural closer engages a recovery gate that
enumerates candidate repairs and re-parses the document for each. The cost
is quadratic in document length. Measured on a node list with one surplus
closer placed mid-document, every row **decoding successfully** — this is
the cost of the accept path, not of a refusal:

| input chars | allocated | ms |
|---|---|---|
| 899 | 0.5 MiB | 0.5 |
| 2,129 | 2.3 MiB | 2.8 |
| 4,179 | 8.1 MiB | 8.6 |
| 8,280 | 30.1 MiB | 22.3 |
| 16,580 | 115.9 MiB | 81.9 |

Doubling the input roughly quadruples both figures. Growth stops beyond
that only because the gate refuses outright past its closer-count bound — a
cliff, not a taper. So the work is **bounded** and the totality claim above
is unaffected: no escape, no hang, a typed outcome either way. What a
producer of untrusted input controls is the amplification: about
seven thousand fold at the bottom of the ladder above, and the over-closed
peak measured by the run itself is in the table. A well-formed document of
the same size costs 40 to 60 bytes per character.

**No fixture in the curated corpus reaches this**, which is the point: the
heaviest payload across every corpus family decodes at 63 bytes per
character. The property was reachable, unrecorded, and invisible to the
evidence that existed before this harness.

It is deliberately **not fixed here**. The remedy is either a lower
enumeration bound, which changes *which* malformed documents recover and is
measured against a labelled acceptance oracle, or a restructured
enumeration that shares parse work across candidates. Both are decisions
about how far the recovery feature should reach, and neither is something a
fuzz harness should settle as a side effect of finding it. The cost is
pinned by a regression test at 8 KB so it cannot silently grow, and
reported in every run so it cannot silently return to being unrecorded.

## Follow-up: the other host decode paths

The result above is evidence about one decoder. The wire format is
implemented independently in several languages, and a totality claim made
for the format rather than for a host is only as good as its weakest
decoder — so each of these is a distinct piece of work, not a port:

- **TypeScript** — the same invariants, with the language's own failure
  modes: an exception is the default control flow, so "returns a typed
  error" needs asserting rather than assuming, and prototype-pollution
  keys (`__proto__`, `constructor`) are a live class this host does not
  have. The harness already generates them.
- **Python** — recursion limits make the depth family behave differently:
  the interpreter raises rather than terminating the process, so a deep
  payload is catchable there and the guard has a different shape.
- **Go** — no exceptions, so the totality invariant is about panics and
  about error returns being complete; the time and allocation budgets
  transfer directly.
- **Rust** — the sum-typed decoders make several of the classes above
  unrepresentable, which is worth demonstrating rather than asserting;
  the interesting residue is the allocation budget and the recovery paths.

A cross-host leg would also gain something none of the single-host runs
can: the same generated input judged by every decoder, so a DIVERGENCE in
what is accepted becomes visible. That is a conformance property, not a
robustness one, and it is the natural next step once the harness shape
here is settled.

## Reproducing this

```
dotnet run --project src/Fuaran.UI.JsonDecode.Tests -c Release -- --fuzz-long 250000 779
```

A bounded run of the same harness is part of the repository test gate, so a
decoder change that reintroduces an escape is caught by the next test run
rather than by the next long run somebody remembers to launch.
