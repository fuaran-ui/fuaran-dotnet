# Fuaran (language tier)

The Fuaran language tier: a typed UI tree, smart constructors, renderer, apply engine, validator, op-stream persistence, layout observability, and op-apply telemetry — all shipped as the `Fuaran.UI.*` NuGet package set.

This is the OSS-target language tier, licensed Apache-2.0.

## Versions a consumer pins against

| Contract | Version | Where |
|---|---|---|
| **Wire-format profile** | `core@1.0` | the canonical JSON tree/op format — [`WIRE_FORMAT.md`](../wire-format-fixtures/WIRE_FORMAT.md) §15 (negotiation) + §16 (lenient ingest). The F# constant is `Fuaran.Core.Wire.Versioning.Profile.coreV1`; a conformant TS/Python host speaks the same profile. |
| **Op-stream chain format** | `v2` | the hash-chain pre-image envelope — `Fuaran.UI.OpStream.Abstractions.StreamEntry.chainFormatVersion`; self-describing (`{"v":2,…}`) and cross-host, see [`STABILITY.md`](STABILITY.md) → "Chain format version". |
| **Package semver** | pre-1.0 | every minor may break until `1.0.0`; pin the exact patch. See [`STABILITY.md`](STABILITY.md). |

Both wire versions are **cross-host contracts** — the F#, TypeScript, and Python hosts encode/decode
byte-identically against the shared [`wire-format-fixtures/`](../wire-format-fixtures) corpus, which is
the parity gate; neither bumps without all hosts + the corpus moving together.

## What ships here

| Package | Role |
|---|---|
| `Fuaran.UI` | Typed tree + smart constructors + Defaults |
| `Fuaran.UI.Renderer` | Fable + React + Feliz renderer + reference CSS |
| `Fuaran.UI.Ops` | Tree-op apply engine |
| `Fuaran.UI.AiTools` | Runtime introspection |
| `Fuaran.UI.Validator` | Build-time F# AST walker |
| `Fuaran.UI.OpStream.*` | Op-stream persistence + replay |
| `Fuaran.UI.LayoutObserver.*` | Layout observability |
| `Fuaran.UI.Telemetry.*` | Op-apply telemetry + drift detection |

> The package set was renamed from `Fern.UI.*` to `Fuaran.UI.*` (workspace roadmap Phase 51) — a breaking, major-version-bump change. Historical `Fern@<hash>` references in docs are preserved as accurate records of what shipped under the old name.

## Build

```powershell
dotnet tool restore
dotnet run --project Build.fsproj                 # Format -> Build -> Test
dotnet run --project Build.fsproj -- Pack         # pack to ../local-nuget-feed/
dotnet run --project Build.fsproj -- Check        # full pre-commit gate
```

The `run.ps1` script at the repo root is the universal "drop in and verify" entry point.

## Repository conventions

See [`CLAUDE.md`](CLAUDE.md) for repo conventions (build pipeline, formatting mandate, sample port allocation).

## Stability

See [`STABILITY.md`](STABILITY.md) for the package-by-package stability statement.

## License

See [`LICENSE`](LICENSE).
