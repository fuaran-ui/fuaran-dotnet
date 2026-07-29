# Fuaran.UI.Ops.Abstractions

The **pure type contract** of the Fuaran tree-op apply engine — the `TreeOp<'Msg>`
DU, the `ErrorPayload` shape, and the `ApplyHint` helpers (§4g). FSharp.Core-only
and **Fable-portable**.

This package exists so consumers that only need the *types* (`Fuaran.UI.Ops.Types`
module — op vocabulary, error envelope) don't have to reference the full
`Fuaran.UI.Ops` package, whose apply engine (`Apply.fs`) and JSON decoder
(`JsonDecode.fs`) are a substantial dependency a type-only consumer has no use
for. The split is a **layering** boundary, not a portability one: `Fuaran.UI.Ops`
is itself Fable-portable, so a browser (Fable) consumer *may* reference the whole
package when it genuinely needs decode + apply — it simply need not, and the
decode/apply seam belongs at the host rather than in every consumer's graph.

In particular, `Fuaran.UI.Telemetry.Abstractions` (which the Fable client
renderer references for `IFuaranTelemetrySink`) references this package instead
of `Fuaran.UI.Ops`, so the renderer's Fable graph carries the type contract only.

The module name is preserved (`Fuaran.UI.Ops.Types`), so `open Fuaran.UI.Ops.Types`
is unchanged for every consumer — only the assembly the type lives in moved.
`Fuaran.UI.Ops` takes a package reference on this assembly and re-exports the
types to its apply engine + decoder.
