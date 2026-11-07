# Fuaran.UI.OpStream.Abstractions

Type contract for the Fuaran op-stream — the durable, replayable record of every `TreeOp` the [`Fuaran.UI.Ops`](https://www.nuget.org/packages/Fuaran.UI.Ops) apply engine has applied.

It also carries the attestation seams and their `ecdsa-p256-sha256-v1` provider, `EcdsaP256`: on .NET it signs and verifies through the BCL; under Fable it verifies through WebCrypto and never signs, over the same SPKI public keys and the same signatures, so a page or segment signed on a server verifies in the browser (Phase 2570).

See the [Phase 12.Z migration doc](../../docs/migrations/12-Z-op-stream.md) for the canonical-JSON algorithm, the hash-chain rule, the SQL schema, the `PromptId` correlation pattern, and the AI pre-emit self-check three-stage gate.

Apache-2.0 licensed — see the repo [LICENSE](../../LICENSE).
