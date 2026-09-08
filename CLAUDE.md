# CLAUDE.md — fuaran (language tier)

This repo is the **Fuaran language tier**: the typed UI tree, smart constructors, renderer, apply engine, validator, op-stream persistence, layout observer, and op-apply telemetry. Ships as the `Fuaran.UI.*` NuGet package set.

> **The `Fern` → `Fuaran` rename has landed** (workspace roadmap Phase 51). Package ids, namespaces, module names, the CSS contract, wire attributes, and validator codes are all `Fuaran.*` / `fuaran-*` / `FUARAN*`. Historical `Fern@<hash>` commit references in docs are preserved as accurate records of what shipped under the old name.

Cross-repo development conventions (port allocation, launcher patterns, formatting mandate, language-baseline pinning) live at the maintainers' workspace level and are not shipped here; everything a contributor needs for this repo is below.

## Shipped packages

| Package | Role |
|---|---|
| `Fuaran.UI` | Typed tree, smart constructors, Defaults |
| `Fuaran.UI.Renderer` | Fable + React + Feliz renderer + reference CSS |
| `Fuaran.UI.Ops` | Tree-op apply engine |
| `Fuaran.UI.AiTools` | Runtime introspection (`fuaran.getNodeState` etc.) |
| `Fuaran.UI.Validator` | Build-time F# AST walker |
| `Fuaran.UI.OpStream.Abstractions` | Op-stream type contract + canonical-JSON + hash-chain |
| `Fuaran.UI.OpStream.InMemory` | In-memory sink |
| `Fuaran.UI.OpStream.Sqlite` | SQLite-backed sink |
| `Fuaran.UI.OpStream.Replay` | Replay engine |
| `Fuaran.UI.LayoutObserver.Abstractions` | Flag DU + observer interface |
| `Fuaran.UI.LayoutObserver` | InMemory + Browser (ResizeObserver) observers |
| `Fuaran.UI.Telemetry.Abstractions` | Telemetry record types + `IFuaranTelemetrySink` |
| `Fuaran.UI.Telemetry.Default` | NoOp / InMemory / Console sinks + `applyWithTelemetry` |
| `Fuaran.UI.Telemetry.Drift` | Aggregate metrics + window-over-window regression detector |
| `Fuaran.UI.Client` | Typed F#/.NET client over the generation endpoint — `generate` + session turn-loop (repair diffs) + the closed repair loop + decode glue |
| `Fuaran.UI.Cli` | The `fuaran` dotnet tool — `generate` / `validate` / `scaffold` over the public surfaces |
| `Fuaran.UI.Renderer.Web` | Embedded browser renderer for .NET hosts — the built `@fuaran-ui/renderer` bundle + reference CSS as embedded static web assets, `MapFuaranRenderer()`, and the mount snippet. No Node toolchain on the consumer side |

All packs land in `../local-nuget-feed/` for local downstream consumption.

## Layout

```
fuaran-dotnet/
├── src/                    # Language-tier projects
│   ├── Fuaran.UI/
│   ├── Fuaran.UI.Renderer/
│   ├── Fuaran.UI.Ops/, Fuaran.UI.Ops.Tests/
│   ├── Fuaran.UI.AiTools/, Fuaran.UI.AiTools.Tests/
│   ├── Fuaran.UI.JsonDecode.Tests/
│   ├── Fuaran.UI.Validator/, Fuaran.UI.Validator.Tests/
│   ├── Fuaran.UI.LayoutObserver{,.Abstractions,.Tests}/
│   ├── Fuaran.UI.OpStream.{Abstractions,InMemory,Sqlite,Replay,Tests}/
│   ├── Fuaran.UI.Telemetry.{Abstractions,Default,Drift,Tests}/
│   └── Fuaran.UI.Tests/      # Language-tier integration tests
├── samples/
│   ├── demo/               # Browser demo
│   ├── catalog/            # Visual component catalog
│   └── themes/             # Default/Dark/HighContrast sample themes
├── docs/                   # Language-tier docs (authoring guide, error codes, theme bridge, etc.)
├── Fuaran.sln                # Solution file
├── Build.fs, Build.fsproj  # FAKE pipeline — Format / Build / Test / Validate / Pack
└── run.ps1                 # Stage-0 entry point — verify-shape (Format/Build/Test/Pack)
```

## Build pipeline (FAKE)

```powershell
dotnet run --project Build.fsproj                  # default 'All' = Format -> Build -> Test
dotnet run --project Build.fsproj -- Pack          # pack the shipping packages to ../local-nuget-feed/
dotnet run --project Build.fsproj -- Validate      # run Fuaran.UI.Validator across src/
dotnet run --project Build.fsproj -- Check         # Format -> Build -> Test -> Validate (the pre-commit gate)
dotnet run --project Build.fsproj -- Css           # regenerate the reference stylesheet's tier copies
dotnet run --project Build.fsproj -- CssCheck      # fail on any tier copy that has drifted (in `Check`)
dotnet run --project Build.fsproj -- RendererWeb      # re-sync the embedded browser-renderer assets
dotnet run --project Build.fsproj -- RendererWebCheck # fail when the embedded copy is stale (in `Check`)
dotnet run --project Build.fsproj -- FableCheck       # the Fable stage: client-tier portability + the law harness (in `Check`)
```

## Gate lanes

`run.ps1 -Lane pure|fast|full` selects TESTS rather than dropping a stage, so a change can be
gated in seconds before a merge and the full gate spent once, on the merged tree. Default `full`
is byte-identical to the pre-lane gate and is the only lane a shipping claim may cite.

```powershell
pwsh ./run.ps1                             # full — the ship lane (unchanged)
pwsh ./run.ps1 -Lane fast -SkipFable       # the pre-merge lane
pwsh ./run.ps1 -Lane pure -SkipFable       # the per-commit lane, seconds
```

A lane is decided at two granularities, because the two costs live at different granularities:

- **Suite level** — `"lane": "pure" | "slow"` in [`test-suites.json`](test-suites.json), read by
  this script AND by `Build.fs`'s `Test` target, so the two entry points cannot select different
  sets. `pure` lives here: every suite costs a ~1.0s `dotnet run` floor whatever it contains, so a
  seconds-fast lane is one that runs fewer PROCESSES.
- **Test level** — `Lanes.slow` markers from [`tests/lanes/Lanes.fs`](tests/lanes/Lanes.fs), read
  at a suite's own Expecto entry point via `FUARAN_TEST_LANE`. `slow` lives here: fourteen
  generative/fuzz tests in one suite carry ~15s of the test stage, and no suite-level switch drops
  them without also dropping the ~1,420 cheap corpus assertions beside them.

A test runs in lane L iff its suite admits L and it is not slow-marked. Both readers REFUSE an
unrecognised `lane` value, and a narrow lane that would admit no test raises rather than running
nothing and exiting 0.

**`-SkipFable` is not implied by any lane** — the Fable leg is a stage, not a lane. It is named
explicitly in the fast-lane invocations above because on measurement it is the single largest
share of this gate's wall-clock, so a pre-merge lane that leaves it in is not fast.

## The Fable stage

`Check` and `run.ps1` both run [`tests/fable-laws/fable-check.ps1`](tests/fable-laws/fable-check.ps1)
— declared once, called by both, the same posture `test-suites.json` takes for the test roster. It
does two things, and they answer different questions:

1. **Portability.** The client-tier projects that ship their `.fs` sources in the package are
   Fable-compiled **under their own MSBuild properties**, with `--noCache`. Under Fable it is the
   ENTRY project's properties that govern the whole transpiled source graph, so a compile entered
   through a `<Nullable>disable</Nullable>` sample proves nothing about a nullable-enabled
   consumer. A server-only API leaking into a Fable-consumed file fails here, naming the file.

   **Which projects is DERIVED, not listed** (Phase 1606). The set is every `src/**/*.fsproj`
   packing `.fs` under `PackagePath="fable\"` — so a new package is gated on its first commit with
   no edit anywhere — minus any project declaring `<FablePortabilityExemption>reason</...>` in its
   OWN fsproj, which the stage echoes by name on every run. Of those, the ones compiled *directly*
   are the roots of the reference graph (which makes coverage total, since project references are
   acyclic) plus every project holding a `#if FABLE_COMPILER` arm (which compiles it under its own
   properties rather than an entry's). `pwsh ./tests/fable-laws/fable-check.ps1 -List` prints the
   answer with the reason beside each entry and compiles nothing; CI's `fable-portability` job runs
   the same script rather than mirroring its result. Before 1606 the list was hand-kept in two
   places, and 0.78.0 shipped a Renderer whose Fable arm neither of them named.

   The derivation's own go-red proof is `tests/fable-laws/fable-check.tests.ps1` — it builds a
   scratch package tree, puts the 0.78.0 defect shape into it, and asserts the gate fails with no
   list edited. It is not part of the gate; run it when the derivation changes.
2. **The laws.** [`tests/fable-laws/`](tests/fable-laws/) is a Fable-compilable law project. It runs
   on both pipelines and its output is compared byte for byte, so two pipelines that are each
   internally lawful and disagree about a result still fail. Its `.NET` leg is also a rostered suite
   in `test-suites.json`. It currently states `TreeMerge.merge3Way`'s order-independence over
   generated three-way edits, runs `FoldConfluence.laneFoldLaws` over this tier's reducer, op
   codec and footprint projection, and inflates a committed foreign **dynamic-Huffman** DEFLATE
   stream — the block type `Deflate.compress` never emits and every standard deflate library
   always does, whose .NET conformance check goes through `System.IO.Compression` and therefore
   could never run on the pipeline that actually receives foreign bundles.

Add a law by adding it to `tests/fable-laws/Laws.fs`; nothing there may use a construct Fable cannot
lower (no Expecto, no `System.IO`, no reflection).

### Fable method traps

Four, each of which cost real time and none of which announces itself:

- **A transpile is not a run.** `dotnet fable` can finish green on JavaScript that dies at its first
  `import`. With nullness ON at the entry project, the canonical wire encoder emits
  `StringBuilder__ToString(sb)`, which the bundled `fable-library-js` 5.0.0 does not export — so
  every module importing the wire types throws a `SyntaxError` before a line of it runs. That is why
  `tests/fable-laws/FableLaws.fsproj` sets `<Nullable>disable</Nullable>` (as `samples/apply-demo`
  does, the other Fable entry project that is actually executed) and why the nullness-ON compile is
  a separate stage that only has to COMPILE.
- **Never pipe `dotnet fable`.** A pipeline reports the LAST command's status, so
  `dotnet fable … | tail` reports `tail`'s success and a failed compile reads as a pass. Read
  `$LASTEXITCODE` from the unpiped call. The same trap catches `node …  | tail` — and note that
  Fable does not wire `[<EntryPoint>] main`'s return value to the process exit status at all, so a
  Fable console must set `process.exitCode` itself (`tests/fable-laws/Program.fs` does).
- **Never Fable-output into `obj/`.** Fable writes beside the project's own build intermediates
  there, re-parses part of the project, and reports errors against files the change never touched.
  Use a fresh directory — `output/` (gitignored) or a temp path.
- **A `let` used exactly ONCE is inlined at its use site — including into a `for` loop's bound,
  which JavaScript re-evaluates every iteration.** So `let n = reader.Read()` followed by
  `for i in 0 .. n - 1` calls `reader.Read()` once per iteration under Fable and once here. Any
  `let` bound to a SIDE-EFFECTING expression and consumed once by a loop bound is this bug; it is
  silent, and the arithmetic downstream of it looks like a data defect rather than a control-flow
  one. `Deflate.readDynamicTables` hit it on HCLEN (`Compression.fs`) and every foreign
  dynamic-Huffman bundle was undecodable in a browser while the .NET suite stayed green. The fix
  is a `mutable` the loop mutates — a mutated binding cannot be inlined. To check: read the
  emitted JavaScript for a `for (...; i <= (...(...));` whose bound contains a call.

`Fuaran.UI.Renderer.Web` embeds a **built artefact from `fuaran-ts`** — the standalone
`@fuaran-ui/renderer` browser bundle — so a .NET consumer needs no Node toolchain. The copy is
generated by [`scripts/sync-renderer-web.ps1`](scripts/sync-renderer-web.ps1) and committed;
`RendererWebCheck` is the drift gate and runs as part of `Check`. **A renderer or reference-CSS
change re-syncs the embedded copy in the same change-set.** The check makes two different
statements depending on what is present and says which: a version-and-vocabulary match is
answerable from committed text always, a byte match needs the bundle built in the sibling and is
reported as *not checked* when it is not. See [`docs/EMBEDDED-RENDERER.md`](docs/EMBEDDED-RENDERER.md).

`content/fuaran-reference.css` is the **canonical** stylesheet and every other host tier ships a byte-copy of it. The copies are generated by `-- Css`, never hand-copied; `-- CssCheck` (also `-- Css --check`) is the drift gate and runs as part of `Check`. A tier whose repo is not in the checkout is reported as *not checked* rather than passing quietly. See [`docs/HOST-STYLING-CHECKLIST.md`](docs/HOST-STYLING-CHECKLIST.md) §1.5c.

The maintainers' workspace-level `pack-all.ps1` packs `fuaran` ahead of its downstream consumers, so callers don't have to chain themselves.

## Formatting mandate

Per the workspace mandate, every commit is preceded by a Fantomas pass over changed F# files. `dotnet fantomas` is available via [`.config/dotnet-tools.json`](.config/dotnet-tools.json) (`dotnet tool restore` first if it's missing).

## Cross-repo dependencies

This repo has **no upstream dependencies on any private repo**. It packs into `../local-nuget-feed/` for consumption by downstream apps and runtime tiers, which consume these packs as `PackageReference`s, not `ProjectReference`s.

Downstream tiers' tests reference behaviour that lives partly in this repo (e.g. the canonical-JSON encoder validation against `ArgsJsonContract`). Those tests live with the consuming tier, not here.

## Samples

`samples/demo/` and `samples/catalog/` are browser-driven F#/Fable apps. Ports are allocated from the workspace's 14000-band server + 24000-band Vite range:

- `samples/demo` — server `14000–14009`, Vite `24000–24009`
- `samples/catalog` — server `14010–14019`, Vite `24010–24019`

If `vite.config.mts` / `launchSettings.json` in either sample still references the pre-migration ports, update them to the band above.

## Test partition note

The pre-migration `Fuaran.UI.Tests` project mixed language-tier tests with tests belonging to a downstream runtime tier; those moved out with the split. If a test in this repo grows a dependency on a downstream tier, it belongs over there.

## Render-time sanitization contract

[`SANITIZATION.md`](SANITIZATION.md) declares the injection-safety posture at every string→DOM seam the renderer exposes (Phase 56). New renderer code that touches `prop.custom`, `prop.href`, `prop.src`, or `prop.dangerouslySetInnerHTML` must route through `Fuaran.UI.Renderer.Sanitize.*` or document why the seam is already safe. Custom renderers registered via `IFuaranRuntime.RegisterCustomRenderer` are a host trust boundary — see the "Custom-renderer trust boundary" section of SANITIZATION.md for the contract hosts must follow.

## Public vocabulary discipline

Anything under this repo is visible to OSS consumers. **Do not reference private, unpublished projects or package names in shipped artefacts** — code comments, READMEs, and sample files that ship to NuGet or get rendered into public docs name only the public `Fuaran.UI.*` package set and generic "downstream consumer" framing. Cross-references stay one-way: private consumers may reference this repo; this repo never references them.
