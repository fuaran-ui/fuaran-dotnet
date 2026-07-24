# Fuaran.UI.OpStream.Sqlite

SQLite-backed `IOpStreamSink<'Msg>` implementation. Single-table schema documented in the [Phase 12.Z migration doc](../../docs/migrations/12-Z-op-stream.md).

Takes a host-provided `IOpJsonCodec<'Msg>` for op JSON serialisation — `TreeOp<'Msg>` carries closures that cannot round-trip generically, so the host supplies the codec it owns the `'Msg` shape for. Hosts that need only integrity verification (not read-back) can use `OpJsonCodec.encodeOnly` from `Fuaran.UI.OpStream.Abstractions`.
