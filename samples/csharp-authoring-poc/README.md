# C# fluent-builder authoring-shape PoC (Phase 172)

A bounded proof-of-concept that **idiomatic C# can author Fuaran trees that are
wire-identical to F#-authored equivalents** — the third authoring surface the
canonical design doc's §4e sketches (sealed records + a fluent builder
converging on the same `Node<'Msg>` tree), validated with running code now that
the language contract is settled.

> **PoC posture — kept honest.** This is *evidence for the §4e design*, not a
> supported package. It lives under `samples/`, references only the public
> typed-tree / encoder / decoder surface by `ProjectReference`, and is deletable
> without touching any shipped suite. Nothing in the `Fuaran.UI.*` package set
> changes. FGP 2 holds one-way: this C# project references `Fuaran.UI`; the F#
> tier never references back.

## What it does

`Program.cs` is a console verification harness. For each representative tree
authored in C# (`Trees.cs`) it:

1. **Encodes** through the canonical F# encoder
   (`Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode`) and asserts the
   bytes are **identical** to the language-neutral corpus fixture of the same
   name (`wire-format-fixtures/nodes/<name>.json`).
2. **Round-trips** — decodes the encoding back to a `Node<obj>` (the canonical
   decoder `Fuaran.UI.Ops.JsonDecode.decodeNode`) and re-encodes it, asserting
   byte-stability.

A **negative control** mutates one field and asserts it does *not* match its
fixture — so a green run is provably meaningful.

The tree set exercises layout nesting (Card / Stack / Grid / Dashboard), display
(Heading / Metric-KPI / Badge / Markdown), a Form field family (Text / Number /
Checkbox / Choice / TextArea), Button + chained actions, and one Chart.

## Run it

```powershell
dotnet run --project samples/csharp-authoring-poc/Fuaran.UI.CSharp.Poc.csproj -c Release
```

Exit code `0` == every tree is wire-identical and round-trip-stable; non-zero on
any divergence. The same invocation runs as a wire-identity gate in `Build.fs`'s
`Test` target.

## Layout

| File | Role |
|---|---|
| `Interop.cs` | The F#-interop seam (`FSharpOption` / `FSharpList` / `FSharpFunc` / `FSharpMap`) the builders sit on. The "what fights C#" surface. |
| `Builders.cs` | Fluent builders + value-helper statics (`Txt` / `Bind` / `Fmt` / `Act`) — the §4e authoring shape in C#. |
| `Trees.cs` | The representative tree set, each authored to match a corpus fixture. |
| `Program.cs` | The encode / round-trip / negative-control harness. |

## Findings

The idiom audit, the §4e confirmations/amendments, and the PoC-to-package gap
list live in [`../../docs/csharp-authoring-poc-findings.md`](../../docs/csharp-authoring-poc-findings.md).
