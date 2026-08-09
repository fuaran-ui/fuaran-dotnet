# Cryptography posture

Fuaran ships **one** hash primitive: a dependency-free SHA-256 in
[`Fuaran.UI.Hashing`](src/Fuaran.UI/Hashing.fs). This document states what it is, what it is for, and —
for a reviewer who reasonably asks "why hand-roll SHA-256?" — why it is a single pure-F#
implementation rather than the .NET BCL.

## What it is

A straight FIPS 180-4 SHA-256 over the UTF-8 bytes of a string, returning lowercase hex. It is
verified two independent ways in CI:

- **Against the published NIST / FIPS 180-4 known-answer vectors** (`""`, `"abc"`, the two multi-block
  examples, and the one-million-`a` vector) — the gold-standard proof it *is* SHA-256.
- **Byte-for-byte against `System.Security.Cryptography.SHA256`** over a Unicode/emoji/multi-block
  corpus — the proof our bytes equal the platform's on .NET.

Both live in `Fuaran.UI.Tests` (`CustomContractTests.fs`).

## What it is for — and NOT for

- **For: content-addressing and corruption detection.** A `Custom` body-shape hash; the op-stream
  chain and op-DAG node hashes. The chain detects **accidental corruption, truncation and reordering**
  of a stored stream, and — given an anchor you already trust — detects substitution of one record for
  another.
- **NOT for: tamper evidence, authentication, or secrecy.** There is no HMAC, no keyed MAC, no
  signing, no encryption in this package. Do not use `sha256Hex` to authenticate a message or store a
  secret.

  **Being SHA-256 does not make the chain tamper-evident, and no stronger unkeyed hash would.** The
  chain is a content digest computed from data the store itself holds, so anyone who can write the
  store can recompute every hash after editing a record, and verification then passes. What
  collision resistance buys is that an attacker cannot forge a *different* record with the *same*
  hash — it does not stop one who is free to change the hash as well. Detecting an edit by someone
  with write access needs a secret the writer does not have: a keyed MAC, or a signature over the
  chain head. That is the `IAttestationSink` seam (Phase 320), whose key lives host-side (e.g.
  KMS/HSM) — the crypto that needs a key stays out of this portable package by design, and until a
  host wires it the property is corruption detection, not tamper evidence.

## Verification on the read paths

The chain was computed on write and, until Phase 793, never checked on read. `Replay` said so in a
comment; `DagVerify.records` had no production caller at all. A property nothing verifies is not a
property the code has, so every read path now verifies:

| Read path | Verifier | Anchor |
|---|---|---|
| `Replay.applyTo` | `Verify.segment` | the segment's own first record (genesis when it starts at sequence 1) |
| `SqliteSink.Replay` / `InMemorySink.Replay` | `Verify.segment` | as above |
| `SessionReplay.reconstruct` | `Verify.segmentFrom` | the checkpoint's `PreviousChainHead` — a real anchor, so strictly stronger |
| `SqliteDagSink` / `InMemoryDagSink` `.Records` | `DagVerify.recordsResolving` | parents resolved store-wide |
| both DAG sinks' `.TryGet` | `DagVerify.record` | content address only |

**On a break, `Replay.applyTo` refuses the whole stream** — it does not fold the clean prefix and
return it. A tree reconstructed from part of a corrupt store is a plausible-looking wrong answer that
the caller cannot distinguish from a correct one; and "the prefix is clean" is weaker than it sounds,
since the break is where verification *first* fails, not where corruption began. A caller that wants
the prefix slices to the reported sequence and calls `applyToUnverified` — one visible line, rather
than a default that silently shortens history. The reasoning is in `Replay.fs` beside the code.

The sinks have no error channel (their reads return bare lists), so they refuse by throwing an
`InvalidOperationException` naming the stream and the offending record — the same idiom they already
use for a duplicate sequence or an undecodable row.

### What it costs

Measured on the dev machine, Release build, `TestMsg` records carrying small structural ops.
Verification is a SHA-256 per record over its canonical pre-image, so cost is linear in records read
and independent of how they were stored.

| Measurement | Unverified | Verified | Delta |
|---|---|---|---|
| `Verify.segment`, 10,000 records | — | 38–47 ms | ~4–5 µs/record |
| `SqliteSink.Replay`, 10,000 records | 74 ms | 134 ms | +60 ms (~+80%) |
| `InMemorySink.Replay`, 10,000 records | 0.3 ms | 117 ms | verification is ~99% of the cost |
| Either sink, single-record read | ~0.1 ms | ~0.1 ms | below the noise floor |

Two things worth reading off that table. On the durable sink verification roughly doubles a large
read, because the read itself is real work. On the in-memory sink it *is* the whole cost, because the
read is a pointer walk — so a host that replays a long in-memory stream in a hot loop is the one case
where the default is genuinely expensive. And the ubiquitous single-record read (the append path's
`LatestSequence + 1` lookup) is unmeasurable, so the common case pays nothing.

### The fast path is named, never silent

`LoadVerification` is passed at sink construction and defaults to `Full` everywhere. The cheaper
modes exist, and each states what it stops proving:

- **`Full`** — recompute the whole loaded segment. The default.
- **`Tail n`** — recompute only the last `n` records, anchored on that window's own first
  `PreviousHash`. Detects corruption inside the window and **nothing before it**. Defensible for an
  append-heavy host re-reading a growing stream whose prefix it verified earlier; not defensible as a
  general default.
- **`Off`** — no verification on load. The caller owns integrity — it verifies once at start-up, or
  the store is a per-process structure it never re-reads from disk.

Naming one of these at a call site is the only way to get it. That asymmetry is deliberate: a silent
fast path is how "we verify" becomes "we sometimes verify" without anyone deciding to. There are
tests asserting that `Tail` misses corruption before its window, so widening it means editing a test
that says so out loud.

### And it is still only corruption detection

Everything above detects **accidental corruption, truncation and reordering**. None of it is tamper
evidence, for the reason in the section above: the chain is unkeyed, so a writer edits a record,
recomputes the chain from that point, and every check here passes. Verifying on read closes the gap
between what the chain claims and what the code does; it does not change what the chain claims.
These call sites are, however, exactly where a keyed MAC or a signature over the chain head would
plug in.

## Why one pure-F# implementation, not the BCL on .NET

`System.Security.Cryptography` does not exist under Fable (the browser/JS target), so the browser host
needs a pure implementation regardless. The load-bearing constraint is that the **op-stream chain hash
must be byte-identical across the .NET and Fable hosts** — a chain written by a server must verify in a
browser and vice-versa. Using the BCL on .NET and the pure implementation under Fable would create two
code paths that must agree bit-for-bit on the one primitive whose divergence silently breaks
cross-host verification. Keeping a **single** implementation — the pure one, proven `== BCL` and `==
NIST` — removes that divergence surface entirely. The cost (a hand-rolled hash) is bounded and pinned
by the vectors above; the benefit (one provably-correct primitive on every host) is exactly what a
cross-host integrity chain needs.

The pure implementation restricts itself to `uint32` arithmetic (no `uint64`, no `BigInt`) so it
transpiles identically under Fable; it is therefore correct for any input under 512 MB, which covers
every content-address and op-record this substrate produces.
