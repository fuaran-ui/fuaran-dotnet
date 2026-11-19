# The client render suite (Phase 2550)

The client renderer (`Fuaran.UI.Renderer`) emits Feliz elements, and Feliz builds its properties with an erased
`unbox`: free under Fable, a real cast on .NET, where a `(string * obj)` tuple is not an `IReactProperty`. So the
renderer compiles on .NET but every path that emits an element throws at its first property, and the .NET suites can
pin only the value-level pieces around an element. This suite is where a claim about the ELEMENTS, or about a mounted
DOM, is proved.

It is an Expecto-shaped suite compiled by Fable and run under Node with a DOM, as the **client render suite** leg of
the gate's Fable stage ([`../fable-laws/fable-check.ps1`](../fable-laws/fable-check.ps1)), beside the law harness.

| File | What it holds |
|---|---|
| `Expecto.fs` | Expecto's call shape (`testList`, `testCase`, `Expect.*`) and a small runner. Expecto itself does not transpile. |
| `SubtreeMemoTests.fs` | Phase 2547's subtree memo on real elements: a kept node is the identical element; a store write, a new context and a new node each rebuild; an action sink defeats it. |
| `RawSinkPropTests.fs` | Phase 2069's raw sink: one `dangerouslySetInnerHTML` object per markup, and no `childList` mutation when a mounted markdown sink re-renders with unchanged markup. |
| `Program.fs` | The entry point: runs the suite, publishes the verdict, sets `process.exitCode`. |
| `drive.mjs` | Sets up the DOM, imports the compiled suite, and exits non-zero on any failure or on a run of zero tests. |

Every test file is `#if FABLE_COMPILER` throughout; on .NET the project only says it runs under Fable and exits 1.

## Which DOM, and why

**jsdom**, in the Node process the Fable stage already requires for the law harness. The alternatives were headless
Edge or Chromium through Playwright, as the render-cost benchmark uses. jsdom won on the three things a gate leg needs:

- **Nothing to download at run time and no browser to install.** jsdom, React and React DOM are three pinned npm
  packages (`package-lock.json`), installed by `npm ci` when `node_modules` is absent or older than the lock file and
  read from the npm cache after that. A browser leg needs a browser binary per machine, and on the reference machine
  headless Edge prints nothing at all.
- **Deterministic and in-process.** The suite commits synchronously through `flushSync` and reads mutations with
  `MutationObserver.takeRecords()`, so there is no page, server, port or timeout between the test and its verdict.
- **It implements what the claims need**: element creation through React, a mounted React root, and
  `MutationObserver` with `childList`, `characterData` and `attributes` records.

What it does not do is lay out or paint, so a claim about geometry or timing belongs in the benchmark
(`benchmarks/Fuaran.UI.Renderer.Benchmarks`), not here.

## Running it by hand

```powershell
dotnet fable tests/fable-render/FableRender.fsproj -o tests/fable-render/output --noCache
cd tests/fable-render; npm ci; node drive.mjs output
```

`-o output` is load-bearing: Node resolves `react` and `jsdom` from the compiled files' location, so the output must
sit under this directory, beside `node_modules`.

## The go-red proof

`drive.mjs --plant memo|prop|sink` edits the COMPILED renderer in place to re-create one regression each claim rules
out: a memo whose decision never hits, a `dangerouslySetInnerHTML` object built per emit, and a markdown sink whose
markup differs per render (so it is reparsed). [`../fable-laws/fable-check.tests.ps1`](../fable-laws/fable-check.tests.ps1)
runs the stage with each plant through `FUARAN_FABLE_RENDER_PLANT` and requires it to go red, naming the case that
failed, and once without one and requires it green. A plant that finds nothing to edit is a harness fault (exit 2),
never a pass. A planted run never records a green.

## Adding a case

Add it to the file whose claim it extends, or a new file before `Program.fs` in `FableRender.fsproj` and in
`Program.fs`'s list. Keep the control beside the claim: a case asserting "nothing changed" needs a sibling that shows
the probe sees a change, or its zero measures nothing.
