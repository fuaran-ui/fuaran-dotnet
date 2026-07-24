# Fuaran.UI.OpStream.Replay

Folds a sequence of `OpRecord<'Msg>` through the [`Fuaran.UI.Ops`](https://www.nuget.org/packages/Fuaran.UI.Ops) apply engine to reconstruct any tree state — given an `initialTree`, replay materialises the post-record tree deterministically.

See the [Phase 12.Z migration doc](../../docs/migrations/12-Z-op-stream.md) for the downstream AI-consumer + AI-emission micro-eval use cases that motivate replay.
