# Fuaran.UI.Content

Host-neutral **validated-exemplar** seam for Fuaran content pipelines.

`decodeExemplar` admits a canonical wire-format JSON string into a `Node` tree through three gates:

1. **decode** — `Fuaran.UI.Ops.JsonDecode.decodeNodeObj`
2. **pre-emit-validate** — `Fuaran.UI.PreEmitValidate.validate`
3. **round-trip fixed point** — the canonical encoding re-decodes and re-encodes byte-identically
   (`Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode`)

It returns the decoded `Node<obj>` plus its canonical bytes, or an `ExemplarFailure` carrying the
decoder's typed AI-recovery hints (stable code, JSON path, expected shape).

```fsharp
open Fuaran.UI.Content

match Exemplar.decodeExemplar json with
| Ok (node, canonical) -> // splice `node` into a larger tree; show/permalink `canonical`
| Error failure        -> // Exemplar.describeFailure failure — make it a build failure
```

## Why

A documentation site, an evaluation-suite seed corpus, and a `fuaran-wire` fence pipeline each need the
same thing: "take this authored exemplar and either give me a first-class `Node` to compose, or tell
me exactly why it is not admissible." Making a rejected exemplar an `Error` lets a consumer turn a
stale or invalid example into a **build failure** rather than a published invalid tree.

The canonical string — not the authored input — is what a consumer should display or embed in a
permalink, so byte-determinism is the encoder's responsibility, not the author's.

## Scope

Decode + validate only. The package carries **no** Giraffe / ASP.NET / routing / markdown
dependency — rendering (`Fuaran.UI.Renderer.Server`), fence location, and page assembly stay with
the host. It depends only on `Fuaran.UI`, `Fuaran.UI.Ops`, and `Fuaran.UI.OpStream.Abstractions`.

Apache-2.0.
