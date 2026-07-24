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

- **For: integrity / tamper-evidence.** Content-addressing (a `Custom` body-shape hash; the op-stream
  chain and op-DAG node hashes). With the **default** hash a chain is tamper-evident against accidental
  corruption and reordering, **not** against a motivated adversary who recomputes the chain — see
  `STABILITY.md` "Hash-chain integrity posture" (inherited from `Fuaran.Core`).
- **NOT for: authentication or secrecy.** There is no HMAC, no keyed MAC, no signing, no encryption in
  this package. Do not use `sha256Hex` to authenticate a message or store a secret. Adversarial
  tamper-evidence is obtained by supplying a cryptographic `HashFn` at the host boundary and, for
  attestation, wiring a signing sink (the `IAttestationSink` seam) whose key lives host-side (e.g.
  KMS/HSM) — the crypto that needs a key stays out of this portable package by design.

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
