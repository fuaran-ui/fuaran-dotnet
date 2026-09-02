# Fuaran (language tier)

[![CI](https://github.com/fuaran-ui/fuaran-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/fuaran-ui/fuaran-dotnet/actions/workflows/ci.yml) [![NuGet](https://img.shields.io/nuget/v/Fuaran.UI.svg)](https://www.nuget.org/packages/Fuaran.UI) [![License: Apache-2.0](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)

The Fuaran language tier: a typed UI tree, smart constructors, renderer, apply engine, validator, op-stream persistence, layout observability, and op-apply telemetry — all shipped as the `Fuaran.UI.*` NuGet package set.

This is the OSS-target language tier, licensed Apache-2.0.

## Safety by construction

A model that emits React ships code, and a model that emits HTML ships markup. A `Fuaran.UI`
emission is neither: it is a value over a closed vocabulary, decoded and validated before anything
renders. The properties below are the ones this package set is prepared to be held to, each recorded
where a reader can check it rather than asserted here.

| Property | Recorded in |
|---|---|
| Every string the renderer puts into an attribute, a URL prop or a raw-HTML sink has a posture stated seam by seam | [`SANITIZATION.md`](SANITIZATION.md) |
| A wire-shape violation is a typed refusal carrying a stable code and a path, never a throw | wire format §6 ([specification](https://fuaran-ui.io/guide/wire-format)) |
| A structurally unbounded payload is refused as `LIMIT_EXCEEDED` on the way down rather than walked | wire format §21; `Fuaran.UI.WireLimits` |
| A URL-valued slot takes an allowlisted scheme or nothing, and the protocol-relative spellings are rejected | wire format §19; [`Sanitize.fs`](src/Fuaran.UI.Renderer.Core/Sanitize.fs) |
| Interactive dispatch fails closed: every shipped runtime denies until the host grants, and the permissive posture is reached by name | [`STABILITY.md`](STABILITY.md) 0.14.0; [migration](docs/migrations/782-default-deny-dispatch-gate.md) |
| A decoded `Mount` guest and a custom renderer are scoped by the host, never by the tree that names them | [`STABILITY.md`](STABILITY.md) 0.15.0; [migration](docs/migrations/783-mount-custom-renderer-isolation.md) |

Two qualifications, because a safety claim with no stated edge is not worth much.

The table describes this tier. The wire format is language-neutral and §19 and §21 are obligations on
every conformant host, but adoption is per host and the specification records where each one stands:
§21.5 currently names the F# host as the only one enforcing the resource limits. A tree vetted here
is not thereby vetted elsewhere.

Some surfaces are the host's by design, and are written down as such rather than quietly counted as
covered. A registered custom renderer's output is not policed (see "Custom-renderer trust boundary"
in `SANITIZATION.md`); Content Security Policy belongs to the application; and `sanitizeMarkdownHtml`
is a floor over the renderer's own escaped-by-construction output, not a general-purpose HTML
sanitiser.

Reporting a suspected vulnerability: [`SECURITY.md`](SECURITY.md). The reasoning behind the posture:
[default-deny by shape](https://fuaran-ui.io/discussion/default-deny-by-shape).

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

The test gate certifies against the shared wire-format corpus, which it reads from a directory named
`wire-format-fixtures` **next to** this repository — clone it there first:

```sh
git clone https://github.com/fuaran-ui/fuaran-ui-specification wire-format-fixtures
git clone https://github.com/fuaran-ui/fuaran-dotnet
cd fuaran-dotnet && pwsh ./run.ps1
```

Without it the corpus-parity suites fail by design ("`wire-format-fixtures/manifest.json` not found") rather
than passing over an absent oracle. The `local` NuGet source in `nuget.config` is an optional developer
shadow feed; it is not needed to build or test from a clean clone.

## Repository conventions

See [`CLAUDE.md`](CLAUDE.md) for repo conventions (build pipeline, formatting mandate, sample port allocation).

## Stability

See [`STABILITY.md`](STABILITY.md) for the package-by-package stability statement.

## License

See [`LICENSE`](LICENSE).
