# Fuaran.UI.Ops.Abstractions

The **pure type contract** of the Fuaran tree-op apply engine — the `TreeOp<'Msg>`
DU, the `ErrorPayload` shape, and the `ApplyHint` helpers (§4g). FSharp.Core-only
and **Fable-portable**.

This package exists so consumers that only need the *types* (`Fuaran.UI.Ops.Type`
module — op vocabulary, error envelope) don't have to reference the full
`Fuaran.UI.Ops` package, whose apply engine (`Apply.fs`) and JSON decoder
(`JsonDecode.fs`) use .NET-only patterns (primitive type-tests, `typeof`,
`GetType()`) that do **not** compile under Fable. Pulling the whole `Ops` package
into a browser (Fable) consumer's graph breaks its `dotnet fable` build; pulling
only `Ops.Abstractions` does not.

In particular, `Fuaran.UI.Telemetry.Abstractions` (which the Fable client
renderer references for `IFuaranTelemetrySink`) now references this package
instead of `Fuaran.UI.Ops`, so the renderer's Fable graph stays clean.

The module name is preserved (`Fuaran.UI.Ops.Types`), so `open Fuaran.UI.Ops.Types`
is unchanged for every consumer — only the assembly the type lives in moved.
`Fuaran.UI.Ops` takes a package reference on this assembly and re-exports the
types to its apply engine + decoder.
