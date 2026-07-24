# Fuaran.UI.OpStream.Abstractions

Type contract for the Fuaran op-stream — the durable, replayable record of every `TreeOp` the [`Fuaran.UI.Ops`](https://www.nuget.org/packages/Fuaran.UI.Ops) apply engine has applied.

See the [Phase 12.Z migration doc](../../docs/migrations/12-Z-op-stream.md) for the canonical-JSON algorithm, the hash-chain rule, the SQL schema, the `PromptId` correlation pattern, and the AI pre-emit self-check three-stage gate.

Apache-2.0 licensed — see the repo [LICENSE](../../LICENSE).
