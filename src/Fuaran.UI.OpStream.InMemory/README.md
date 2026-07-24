# Fuaran.UI.OpStream.InMemory

In-memory `IOpStreamSink<'Msg>` implementation — per-process `Dictionary`-backed; useful for tests, ephemeral previews, and host-tier development environments.

See the [Phase 12.Z migration doc](../../docs/migrations/12-Z-op-stream.md) for the durability semantics, hash-chain rule, and acceptance set.
