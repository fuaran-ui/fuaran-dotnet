# Fuaran UI — language decisions (newest first)

Decisions about the **language contract** — the wire model, its vocabulary, and the rules a
conformant host must obey. Narrower than the roadmap (which records *work*) and wider than
`VOCABULARY.md` (which governs only whether a `NodeKind` may be admitted).

A decision earns a place here when it constrains every host and would otherwise have to be
re-derived from the code. Each entry states what was decided, what was rejected, and the
consequence.

---

## 2026-10-27 — D23: a chart's series palette stays literal in the lowering and is themed by installation, after a gate

**Decided (Phase 2482).** A theme carries a chart series palette (`Theme.Charts`: up to eight series slots in
assignment order, plus the reserved positive / negative / neutral triple; default `Defaults.charts`, the Phase 875
set). It reaches a chart by INSTALLATION, not by inherited CSS: a host derives a chart style from its theme once, at
boot (`Charts.installChartStyleFromTheme`), and `Charts.renderSvg` — the one entry point both renderers' chart arms
call — lowers under the installed style. `lowerWithStyle` is unchanged, and with nothing installed the style is
`ChartStyle.defaults`, so every default chart and every default golden is byte-identical. Before a palette is installed
it is VALIDATED (`Fuaran.UI.ChartPalette`): the lightness band and chroma floor per slot, on both chart surfaces, and
the colour-vision (Machado 2009, protanopia and deuteranopia, ΔE ≥ 6) and normal-vision (ΔE ≥ 15) floors over adjacent
pairs. A failing palette is refused with each failing slot or pair named, and the shipped default is installed in its
place. A failing colour is never drawn.

**Why the palette stays literal in the lowering.** A series colour must stay distinct for every reader on a light or a
dark surface. That is a property of the SET, measured over adjacent pairs, so a palette cannot be assembled from
per-slot CSS variables the way the chrome inks from `currentColor` (Phase 536). Indirection through CSS would make each
slot a cascade function that no gate ever sees as a set, and would make the drawing depend on the page it lands in, so
two hosts could draw the same tree differently. Keeping the colours literal keeps the lowering a pure function of its
inputs. The theme still decides WHICH literals: the style is a lowering parameter, never a `ChartSpec` field, so the
wire does not move.

**Why the palette is checked by one shared gate.** The install path and the theme manifest's new
`ChartSeriesDistinct` invariant, which `ThemeBridge.verify` reports beside the contrast floors, both call the same
`ChartPalette.validateSeries`. A designer therefore sees the same refusal a host would hit. The gate lives in
`Fuaran.UI` because that is the only package both `Fuaran.UI.Charts` and `Fuaran.UI.ThemeManifest` already depend on.
Its thresholds are the ones the Phase 875 default was validated against, and they are calibrated to the Machado
simulation, so changing the simulation would mean recalibrating them. The relief-eligible gates are not refused: CVD in
the 6–8 band, and contrast below 3:1 against the surface. Every lowered series and pie category carries a visible
legend label, which is the relief those gates require.

**Decided with it.**
- **The `--fuaran-chart-*` variables are a read-only projection.** `Theme.toCss` emits them so a host page can match a
  chart (an HTML legend, a table swatch). The reference stylesheet does not declare them, because no rule there reads
  them and overriding one in CSS recolours no chart: declaring them would advertise an override that does nothing.
  `ThemeTests` pins the difference as exactly those eleven names.
- **The browser renderer's `renderWithTheme` installs; the server's does not.** A page has one theme. A server process
  may render several, concurrently, and a per-request install would race on a process-wide style. An SSR host
  installs at boot, or lowers per call with `lowerWithStyle` and `chartStyleOfTheme`.
- **The themed golden is a subfamily, `chart-lowering/themed/`.** It pins the install path on this host. Every other
  host's harness enumerates the family directory non-recursively, so the subfamily adds a contract without failing a
  host that has no install seam to certify. A host that gains an install seam certifies the subfamily too. The input
  carries the palette as a harness-only `palette` selector, following the `axisUnitMode` precedent.

**Rejected.**
- **A per-theme palette chosen by the lowering** (light and dark sets). D8's theme-invariance argument stands: one hex
  set clears both surfaces, and a lowering that picks per surface would need to know the surface.
- **Validating at draw time.** That would repeat the measurement on every render, and the only possible failure
  response there is to draw something else silently. Measuring once at install puts the refusal where it can be
  reported.
- **Threading a style through the renderers' contexts.** That widens every emitter's public record for a value set
  once per process. The provenance scope (Phase 643) uses the same process-global seam for the same reason.

Roadmap: [Phase 2482](../../roadmap/phases/2482-a-host-installed-chart-style-from-the-theme-validated.md).

---

## 2026-10-22 — D22: the reference host parses through Core, and the three answers that moved are every host's

**Resolved (Phase 2286, 2026-10-22).** D21's blocker is gone: `Fuaran.Compute` 0.41.0 and `Fuaran.Program` 0.11.0 are
released on Fuaran.Core 0.40.2. The Core pins move to 0.40.2, `FuaranCoreComputeVersion` to 0.41.0 and
`FuaranProgramVersion` to 0.11.0 in one change-set, and the 89 `TypeLoadException: Fuaran.Core.Canon` failures D21
recorded are gone. `JsonDecode.tryParse` reads through `Reader.read { Null = RejectNull; Limits = wire limits }` and the
hand parser is deleted, so this host's two decode entry points are one parser and the WIRE_FORMAT §20.1 declaration is
retired (the test that carried it asserts agreement instead).

**Decided — the three corpus answers the parse move changed are amended, not declared.** Each is a coordinated corpus
change landed with every codec host in one change-set (the WIRE_FORMAT §11 procedure), never a divergence this host
carries alone:

1. **§20.2 row 7, an overflowing exponent (`1e999`), is `INVALID_JSON`.** D19 recorded the operator's ruling
   (2026-10-16): Core's answer is the row's answer. The row met the admission test the section states for a refusal —
   no conformant encoder can write the token, since the wire has no non-finite float and §7's quoted sentinels are its
   only spelling — so the amendment takes no version step (§20.5's reasoning). The corpus pins it as
   `reject-json-number-overflow-exponent`.
2. **§2 rule 5, an integer token past 2^53, is `INVALID_JSON` unless it is the canonical float layout of the double it
   reads as.** Confirmed with row 7 (2026-10-16). The rule is Core's exactly, token-judged: `10000000000000000` (the
   fixed-point layout of `1E+16`) is read because a conformant encoder writes those bytes; `9007199254740993` is
   refused. The lenient fixture that pinned the rounding is replaced by `reject-json-integer-beyond-int53`.
3. **A depth breach names the breach's path.** §21.2 rule 2 already required it; the `repair-limit-exceeded` case
   pinned `$`, and now pins the path of the first array past `MaxJsonDepth`. Only the hosts that implement repair (this
   one and TypeScript) certify that case, and TypeScript's parser now tracks the path.

Every codec host moved its own parser by the smallest change (TypeScript, Python, Go and Rust each refuse both tokens
in their own number reading; no code is shared). The two projection hosts decode through the Rust core and inherit its
answers.

**Found — the parse move is slower on text.** `decode-rate 100` (STABILITY.md, the 0.98.0 entry for this phase): the
routes that parse text are about 14–23% slower and allocate 8–11% less, and the `JVal` routes are unchanged. Recorded
as a finding, per the phase's rule that a regression is not a reason to keep a second parser. The cost sits in the
reading over Core's token surface and Core's scanner, so the place to buy it back is the substrate, where every entry
point and every host that reads through Core gains it.

**Rejected.**
- **Declaring rows 7 and the integer past 2^53 as this host's divergence (§20.1 rule 2).** That re-opens the
  declaration the phase exists to close, as a dialect of one host.
- **Pre-scanning the text to restore the old answers.** That is a parser of this host's own, in front of Core's.
- **Moving `reject-limit-json-depth`'s `expectedPath` too.** Reject fixtures match the path as a prefix, so `$` already
  admits the breach's path; the repair family matches exactly, which is why its case moved.

Roadmap: [Phase 2286](../../roadmap/phases/2286-the-reference-host-parses-through-core-with-its-own-limits-and-stops-carrying-a-parser-of-its-own.md).

---

## 2026-10-22 — D21: the Core 0.40.2 raise waits on a compute release built on Core 0.39 or later; the parse through Core's reader is ready behind it

**Found (Phase 2286, 2026-10-22).** The decoder's parse through Core's token reader (`Reader.peek` /
`Reader.path` / `Reader.readNull`, fuaran-core#467, Core DECISIONS D213) needs Fuaran.Core 0.40.2, and this
repository cannot take 0.40.2 yet. The blocker is not in this repository. `Fuaran.Compute` 0.40.0, the
newest compute release on nuget.org, was built on Fuaran.Core 0.37. Its `DataFrameCodec.encodeTransform`
calls `Fuaran.Core.Canon`, which Core 0.39 removed (Core Phase 520: every byte rule is a named `Rendering`).
Against the raised pins the solution COMPILES, because the call sits inside the compute assembly, and then
throws at run time:

    System.TypeLoadException: Could not load type 'Fuaran.Core.Canon' from assembly
    'Fuaran.Core.Wire, Version=0.40.2.0'
       at Fuaran.Compute.DataFrameCodec.encodeTransform(Transform t)

In `Fuaran.UI.JsonDecode.Tests` this is 89 errored tests: every corpus fixture carrying a `Binding.Transform`,
the live-Transform suite, the §21.8 expression-bound tests, and the robustness fuzz, which reports the
exception as escaping the refusal contract. The bounded program core is in the same position.
`Fuaran.Program` 0.10.0 and 0.10.2 are built on Core 0.38.1, and their assemblies name `Fuaran.Core.Canon`
too. That half was read from the assemblies, not run.

**Decided.** The pins stay on Fuaran.Core 0.38.1 and Compute 0.40.0. A mixed spine (Core 0.40.2 beside a
compute layer that cannot load on it) is not shipped, and no compute version is pinned that has not been
released. The raise, the parse move and the rest of Phase 2286 resume once the compute repository and the
program core each release a version built on Fuaran.Core 0.39 or later. At that point `FuaranCoreComputeVersion`
and `FuaranProgramVersion` move in the same change-set as the Core pins.

**What the attempt established, for the change-set that lands it.**

1. **Compile breaks at 0.40.2, beyond `JsonDecode.fs`.** These are mechanical, one Core migration line each.
   - `Canon.render` / `renderOrdered` / `tryRender` / `canonicalFloat` / `typed` become `Canonical.write` /
     `tryWrite` under `Rendering.CrossHost` / `Rendering.Declared`, `FloatLayout.canonical` and
     `Canonical.typed` (about thirty files, including the vocabulary support splice).
   - The op engine takes a `Policy` and an `OpScope` (`Ops.apply` / `canApply` / `footprint`,
     `TreePlacement.place` / `move`, `Diff.toOpsWith` / `changes`). `KeyedPosition` gains its slot.
   - The op stream is one `Stream` value: `StreamConfig.canonical`, `OpStream.ofConfig`,
     `firstChainBreak`, and `Jsonl.read` / `write`.
   - `Diff.DiffError` is `Rejection`. `ObserverState` is opaque (`ObserverWitness.isRegistered`).
   - The IDL records widen: `IdlField.Aliases` / `Presence`, `IdlUnion.Annotations` / `TagRule`,
     `IdlUnionCase.Hosted`, `HostedCodec.Refusal`, `IdlRecord.Annotations`. The generator's types move to
     `Fuaran.Core.Idl.Codegen.Types`.
   - `Json.parseTolerantOfNull` becomes `Decoder.parse ReadOptions.tolerantOfNull`, and
     `Versioning.Profile` becomes `Versioning.Capability`.
   - A `Fuaran.Core.Bool` / `Fuaran.Core.Null` cell pattern resolves to the new `JsonToken` case, so it
     takes `Fuaran.Core.Cell.`.
   - The generator no longer emits `encodeStateBehaviourJson` / `encodeSemanticStyleJson`, so the support
     splice declares them.
   - `Artifact.render` no longer writes a default `description`, so `idl.json` keeps its bytes only if the
     vocabulary declares its identity and renders with `Artifact.renderWith`.
2. **No corpus byte moved from the raise itself.** `idl.json` re-rendered byte-identical once the identity
   was declared, and `Generated.fs` and the F* vocabulary model regenerate with only their headers and stamps
   moving.
3. **The parse move changes three answers a corpus vector pins.** The other hosts still give the old answer,
   so each change is a coordinated corpus change and not this host's alone:
   - row 7 (`1e999`) is `INVALID_JSON` (D19, operator 2026-10-16), against the §20.2 test's accept;
   - the `lenient/` fixture whose `beyond` member is 9007199254740993, a non-canonical integer past 2^53, is
     `INVALID_JSON` under Core's reader, where the corpus pins an accept;
   - a depth breach is `LIMIT_EXCEEDED` at the breach's PATH (`$[0][0]…`), which Core reports and Phase
     2286 asks for, where the `repair-limit-exceeded` fixture pins `$`.

   The nine null fixtures, every other §20.2 row and every other `limit` fixture answered as pinned.
4. **Baseline for the benchmark the move is held to.** This is `decode-rate 100` (Release) at Core 0.40.2
   with the hand parser still in place:

   | Route | mean µs | allocated B |
   |---|---|---|
   | node, string | 4195.2 | 6,974,746 |
   | node, render-reparse | 4540.6 | 10,386,760 |
   | node, jval | 2458.5 | 5,221,816 |
   | ops, string | 633.5 | 3,049,136 |
   | ops, render-reparse | 1037.5 | 4,758,136 |
   | ops, jval | 501.1 | 2,127,624 |

   Inputs: a 2,001-node tree of 149,616 characters, and 200 ops of 79,770 characters. No "after" figure is
   recorded: with the transform codec unloadable, the parse move cannot be timed on the same inputs.

**Rejected.**
- **Pinning Core back below 0.39 for the compute packages alone.** That is a diamond on `Fuaran.Core.Wire`,
  and the reader the move needs is 0.40.2's.
- **Shimming `Fuaran.Core.Canon` in this repository.** A type defined here cannot satisfy a reference the
  compute assembly binds to `Fuaran.Core.Wire`.
- **Landing the parse move against a hand-copied token reader.** That would be a parser of this host's own,
  which is what the phase deletes.

Roadmap: [Phase 2286](../../roadmap/phases/2286-the-reference-host-parses-through-core-with-its-own-limits-and-stops-carrying-a-parser-of-its-own.md).

---

## 2026-10-18 — D20: the row feed's codec is this tier's, and it refuses its two hazards at encode

**Decided (operator, 2026-10-18).** The grid / chart row feed is written and read by `Fuaran.UI.RowCodec`, which this
tier owns, and no longer by Fuaran.Core's row codec, which Core 0.37.0 removed. The move keeps every byte except two, and
those two are refused rather than carried:

1. **A `DateTime` of `Unspecified` kind is refused at encode.** Its Unix seconds are not a property of the value. The
   retired codec chose them through `ToUniversalTime()`, which reads the ENCODING machine's time zone, so one value
   encoded to different seconds on two servers.
2. **An `int64` outside ±2^53 is refused at encode.** Every integer in [−2^53, 2^53] is exactly a double, so its bytes
   are exact. Past either bound the retired codec widened it to the nearest double, which is a different number, and no
   reader can tell. This follows D19's ruling that a JSON integer past 2^53 is not a number a conformant reader admits
   (Core's `INVALID_JSON`): this is its encode-side twin, and the reader side still moves with the parse, as D19 says.

The refusal is typed, `RowCellRefusal { Row; Key; Hazard }`, and names the cell: the row's position in the feed and the
cell's key. `tryEncodeRows` returns it as a value. `encodeRows` raises it as `RowCellRefused`, because its callers are the
generated layer's encoders, which are total over every other value. **No new cell kind is minted.** The class is BREAKING
for exactly those two inputs (`STABILITY.md`, 0.97.0), and the wire rule is `WIRE_FORMAT.md` §5 "The two encode refusals",
with the hand-authored `row-encode/` corpus family carrying the vectors: each refusal beside an admitted boundary twin
(±2^53 itself, a UTC date-time), so a host that refuses too much fails as surely as one that refuses too little.

**Every codec host adopts it in the same change-set**, each in the encoder its node encode actually takes, and each
says per vector what its authoring surface cannot hold:

- TypeScript: the 64-bit integer is a `bigint` cell, admitted in range as an integer and refused past it. It used to fall
  to the `"<opaque>"` sentinel, against rule 11. A `Date` always names an instant, so the unspecified date-time is
  unrepresentable there;
- Python: an `int` cell past ±2^53 and a naive `datetime` are refused. An aware `datetime` encodes as Unix seconds, where
  it used to raise a plain `TypeError` (rule 11 lists it);
- Go: an `Int` cell past ±2^53 is refused. The wire model holds no date value, so the date-time vectors are
  unrepresentable there;
- Rust: a row cell's only number is a double and there is no date value, so neither hazard can reach the encoder. The
  family is declared not applicable, with that reason.

**Measured, not assumed.** Before the tier stopped referencing the retired codec, a differential ran both over every
row-source payload in the corpus (58 payloads, none differing) and over a native-cell matrix covering every rule-11 arm,
both hazards' in-range neighbours and the residual sentinel. The matrix's bytes, recorded from the retired codec, are
pinned in `RowCodecTests.fs`, so the identity outlives the codec it was measured against.

**Rejected.**
- *Read an `Unspecified` value as UTC.* That is a stated rule rather than a machine's zone, but it is still a guess. A
  wall-clock value an author meant as local time would encode silently wrong on every machine instead of on some, and
  the encoder cannot tell which the author meant.
- *Carry a large `int64` losslessly* as a canonical integer string with a declared cell kind. That mints a cell kind
  every host would have to read, to serve a value a string cell already carries without one.
- *Refuse at decode as well.* A wire integer past 2^53 is the parser's business. D19 already ruled it and deferred it to
  the parse move, because no host's parser answers it today. Doing it at the row cell alone would need the literal's
  text, which only the parser has.

**Consequence.** No file in this repository references `Fuaran.Core.RowCodec`, and the two `#nowarn "44"` suppressions
added for it in `BindingWalk.fs` and `Charts.fs` are gone. The raise to Core 0.37.0 or later no longer meets the row codec.
It still meets the `Fuaran.Core.Row` alias, which the same Core release removed, and the other classes D19 lists. Those
belong to the cohort raise, Phase 2226.

Roadmap: [Phase 1960](../../roadmap/phases/1960-the-ui-tier-hosts-its-own-row-codec.md).

---

## 2026-10-16 — D19: the decoder does not yet parse through Core's limited parse; two conditions remain, and row 7 is Core's answer

**Decided.** Phase 2286 set out to route `JsonDecode.tryParse` through `Json.parseWithLimits` (Fuaran.Core.Wire
0.38.1, fuaran-core#465) with `WireLimits`' values and delete the private parser. It does not, and D16 stands until
both conditions below are met. What 0.38.1 does close is D16's limits half: Core now takes a limits record and
enforces it on the way down, reporting a breach as a typed `LimitExceeded`, never as malformed JSON. With rows 1 and
5 answered since fuaran-core#462, two of D16's three facts no longer hold. One still does, and the cohort raise is a
prerequisite in its own right.

1. **`JVal` still has no null, and no `NullPolicy` reproduces this host's null answers.** Measured against 0.38.1's
   `parseWithLimits`, over the corpus's null fixtures:
   - under `RejectNull`, every one of the seven `reject/*null*` inputs is `NullNotRepresentable`. That maps to
     `INVALID_JSON` at `$`, where each fixture requires `WRONG_TYPE` at the null's own path (for example
     `$.kind.srcSet`, `$.kind.onClick.payload`). Both `lenient/*null*` inputs, which must be accepted by read-compat,
     are refused;
   - under `EraseMemberNull`, all nine parse, with the null erased to absence. `reject-image-srcset-null` then
     decodes, because an absent `srcSet` is the empty list. The fixture refuses it on purpose: a present null is a
     second spelling of an absence that already has one.

   Neither policy keeps the §6 envelope unchanged for every caller, which was the phase's acceptance. The parse can
   move only when Core carries a null whose answer the decoder chooses by position (D16's rejected option, now the
   only one left), or when the specification moves its null fixtures. The specification move was rejected in D16
   and stays rejected here.
2. **The raise to Fuaran.Core 0.38.1 is not a pin edit.** At 0.38.1, `Fuaran.sln` stops in its first layer with 72
   errors in `Fuaran.UI` and `Fuaran.UI.Idl`. They fall into five classes: `Fuaran.Core.Row` / `RowCodec` removed;
   schema entries become `Field`; the typed column vector; `Validator.Registry` becomes `RuleRegistry` in the
   generated layer; and `KindProjection.MapMsg`. A whole-solution census of the same change set, taken at 0.37.0 on
   2026-10-15, counted about 500 errors in 88 files. Two runtime prerequisites sit outside this repository:
   - `Fuaran.Program` 0.9.0, the newest on nuget.org, is built on Fuaran.Core 0.34 and fails at run time on the
     0.37 column;
   - `Fuaran.Compute` 0.38.1, the version pinned here, is built on Fuaran.Core 0.36. A raised Core needs Compute
     0.39 or later, and 0.40.0 is built on 0.37.0.

   The raise therefore waits on Phase 1960, the tier-owned row codec, whose wire ruling is still open. It also
   waits on a re-scoped Phase 2226 (column, `Field`, timestamp unit), on a Program release built on Core 0.37 or
   later, and on the Compute pin moving with it. Because every project shares one pin, it lands as one change.

**Row 7, confirmed (operator, 2026-10-16).** `WIRE_FORMAT.md` §20.2 row 7 (an overflowing exponent such as `1e999`)
currently requires an accept. It is amended to Core's answer: `INVALID_JSON`, because Core's value model has no
non-finite float. This is the conforming direction, since a
non-finite literal reaches no conformant encoder. The amendment is NOT made to the specification by this entry. All
five hosts accept today, and nothing in this phase changes their parsers, so the text would make every host
non-conformant at once. It lands with the parse move, in the same change-set as each host's leg. Core also refuses
an integer past 2^53 written in a layout that is not a double's canonical one (for example `9007199254740993`,
`MalformedNumber`), where this host reads it. That divergence is outside §20.2's rows; the same ruling covers it,
and it is amended in the same change-set as row 7.

**Measured before the move**, as the baseline the move is held to. Command:
`dotnet run -c Release --project benchmarks/Fuaran.UI.Ops.Benchmarks -- decode-rate 50`, one run at `fe90c4f`.
The inputs are a 2001-node tree (177,216 chars) and 200 ops (79,770 chars):

| Measure | Time (µs) | Allocated (bytes) |
|---|---|---|
| `node.string` | 7686.9 | 7,871,215 |
| `node.jval` | 4204.1 | 5,698,257 |
| `ops.string` | 825.0 | 3,045,937 |
| `ops.jval` | 1611.1 | 2,162,825 |

The figures come from a single run on a developer machine, so they are indicative only.

**Rejected.**
- Routing through `RejectNull` or `EraseMemberNull` and re-pinning the null fixtures to whatever answer results.
  That is a specification change made to suit one host's parser, and in the `EraseMemberNull` case it admits a
  document the format refuses on purpose.
- Pre-scanning the text for nulls or overflowing exponents so that a Core parse returns the old answers. That
  would be a second tokenizer in front of the one being adopted.
- Raising only `Fuaran.Core.Wire`. `Row` lives in that package, so the same first-layer breaks occur, and the
  cohort moves as one.

**Consequence.** `JsonDecode.fs` keeps its parser, and the §20.1 rule-2 declaration in `GeneratedLayerTests.fs`
stays as the executable record of the two accept sets. `WIRE_FORMAT.md` §20.1 and §21.5 are unchanged. The move
becomes possible when Core carries a positional null and the 0.38-or-later raise has landed. When it does, it
deletes the parser, retires the declaration, and makes the row-7 amendment.

Roadmap: [Phase 2286](../../roadmap/phases/2286-the-reference-host-parses-through-core-with-its-own-limits-and-stops-carrying-a-parser-of-its-own.md),
[Phase 2233](../../roadmap/phases/2233-the-codec-seams-private-parser-stays-until-core-carries-a-positional-null-and-the-limits.md),
[Phase 1960](../../roadmap/phases/1960-the-ui-tier-hosts-its-own-row-codec.md), [Phase 2226](../../roadmap/phases/2226-fuaran-dotnet-moves-onto-core-s-typed-column.md).

---

## 2026-10-15 — D18: the C# `*Options` records, the C# factory members and the VB mapping builders stay hand-written

**Decided.** Phase 2241 generated the VB analyzer vocabulary and the C# enum maps from the IDL. The remaining two
authoring mirrors — the C# factory's kind-keyed members over the public `*Options` records, and the VB XML-literal
mapping builders — stay hand-written. Their drift is guarded rather than generated away:

- the positional spec construction in `src/Fuaran.UI.CSharp/Factories/` and `Fuaran.cs` (the
  SPEC-CONSTRUCTION-TRIPWIRE, held by `SpecConstructionTests`): a spec slot added to the vocabulary is a CS7036 at
  the one site that decides whether C# exposes it or passes its default;
- the re-pointed authoring-surface pin (Phase 2241): every attribute the generated analyzer vocabulary admits must
  be a settable property on the C# `<Kind>Options` record — the only route by which VB can read it — or be declared
  a GAP.

**Rejected.** Generating the `*Options` records, the factories and the VB builders from the IDL plus a declared
table of per-kind defaults (Stack vertical, Tabs horizontal, Grid 12 columns, SplitPanel 0.5, Media controls on,
Button Secondary) and wrapper types (`Text` / `OptText` over `TextSource`, `IntBinding?` over `Binding<int>`, the
Video/Audio split of Media, the row-generic `DataGridOptions`, the conveniences that lower onto Box, DataGrid and
Select). It would make the whole public C# and VB authoring surface generator output, need a defaults table the
size of that surface, and remove the tripwire, so a new field would be exposed to C# and VB by default instead of by
decision.

**Consequence.** Adding a field to a spec the authoring surfaces map is a hand edit in the C# factory (forced by
CS7036) and, where VB should read it, a C# `*Options` property (forced by the pin) — plus, as the Phase 2241 probe
showed, the F# record literals that construct that spec in `src/Fuaran.UI/Defaults.fs` and
`src/Fuaran.UI.Ops/JsonDecode.fs`. The analyzer vocabulary and the C# enum maps move on their own. Phase 2241's
deferred tasks (generating the factory members and VB builders; the probe moving all three mirrors with no hand
edit) are closed by this decision rather than built.

Roadmap: [Phase 2257](../../roadmap/phases/2257-whether-the-published-csharp-options-records-and-vb-builders-become-generated.md),
[Phase 2241](../../roadmap/phases/2241-the-csharp-vb-and-analyzer-vocabulary-is-generated-from-the-idl.md).

---

## 2026-10-13 — D17: the hole value-space mapping gets its own small package rather than widening `Fuaran.UI` or the validator

**Decided.** `toCoreSpace` and `toCoreEffect` — the projection of a hole's `ValueSpace` and `EffectClass`
onto the Core function model — will have ONE definition in a new, deliberately small package that both
`Fuaran.UI.AiTools` and `Fuaran.UI.Validator` reference. Neither existing package's dependency closure moves.

**Rejected.** (A) Giving `Fuaran.UI` a reference to `Fuaran.Core.Function`: that drags `Fuaran.Core.OpStream`
and `Fuaran.Core.Ops` into every `Fuaran.UI` consumer, Fable builds included, for a mapping a few dozen lines
long. (B) Giving `Fuaran.UI.Validator` a reference to `Fuaran.UI.AiTools`: a validator that depends on the AI
tools, the ops and the renderer core is a layering inversion a consumer restoring the validator alone would pay
for.

**Consequence.** One more package id to publish and pin, carried as its own `STABILITY.md` slot; the two copies
(Phase 2042 found them on opposite sides of the boundary) are deleted when it lands. The work is Phase 2234.

**Landed (Phase 2234).** The package is `Fuaran.UI.Core.Function`, module `Projection`: named on the
`Fuaran.UI.*` pattern for the Core package it projects onto (`Fuaran.Core.Function`), so a reader looking for
the UI side of that model finds it by name. It references `Fuaran.UI` and `Fuaran.Core.Function` only, ships
Fable sources, and is first published at 0.94.0. Both copies are deleted; `STABILITY.md`'s 0.94.0 entry records
the closure change as breaking, riding the already-breaking draft.

Roadmap: [Phase 2234](../../roadmap/phases/2234-one-home-for-the-hole-value-space-mapping-is-a-packaging-decision.md),
[Phase 2042](../../roadmap/phases/2042-core-predicates-have-one-definition.md).

---

## 2026-10-13 — D16: the decoder keeps its private JSON parser; Core's `JVal` is a value the host lowers, not a parser it adopts

**Decided.** `JsonDecode` keeps its own JSON AST and parser. Core's parser is not adopted at the codec seam, and
the AST is not deleted. The Core-facing seam is the additive entry points Phase 2059 shipped (`decodeNodeJVal`,
`decodeNodeObjJVal`, `decodeOpJVal`), which lower a `JVal` into the host's AST under the same §20 and §21
checks the text route applies, and refuse through the text route so codes, paths and messages are identical.

**Why.** Three facts, none of which a host phase may change: Core's parser is not a §21 host by Core D84
(operator ruling 2026-10-01, option b) — it caps depth at 512 against this tier's 256 and bounds no string, array
or document; Core answers `WIRE_FORMAT.md` §20.2 rows 1, 5 and 7 differently from what the format requires
(it refuses `1e999`, which must accept to infinity, and integers past 2^53 in non-canonical layout); and `JVal`
has no null, while the corpus pins null outcomes BY POSITION (`lenient/*null*` accepts by read-compat;
`reject/reject-null-*` is `WRONG_TYPE` at the null's own path), which a position-free value cannot reproduce.

**Rejected.** Growing Core with a positional null and a limits-taking parse (a closed DU gaining a case —
breaking across the cohort, and a reversal of D84); and moving the corpus's null fixtures to a position-free
form (a specification change made to suit one host's parser).

**Consequence.** The design note in `JsonDecode.fs` justifies the private parser by §20/§21 and D84, not by the
historical Fable.SimpleJson comparison. Phase 2059's deferred task closes as decided-no; Phase 2233 records the
ruling and is shipped on it.

Roadmap: [Phase 2233](../../roadmap/phases/2233-the-codec-seams-private-parser-stays-until-core-carries-a-positional-null-and-the-limits.md),
[Phase 2059](../../roadmap/phases/2059-one-json-value-at-the-codec-seam.md).

---


## 2026-10-10 — D15: a commit is resolved against the tree at lowering and flushed from the event before the fold; it views as an `Assign`, and every flushed write meets the gate on its own

**Decided (Phase 2198).** On the bounded path `CommitLocal` WRITES. The key is the `commitTo` of the `Local`
binding on the form field the commit names, the first in document order, found with the same lookup the
server-driven flush uses (`FormBuffer.tryFindFormField`). The value is the admitted event's payload member
under the field's id, which is the same protocol. Both are resolved where the tree and the event are in
view: the key at LOWERING, the value in the LOOP after the trust boundary and the confirm round trip. The
commit is rewritten into a sealed carrier (`UiWitness.CommitCarrier`, riding `Action.Dispatch`'s in-process
slot as the confirm carriers do), and the carrier views as the core's `Assign` with a literal. So the core
reads nothing new and Program needs no release (Program D46). WIRE_FORMAT §30.1 states the rule, and the
`lowers-to/` vectors pin it with the tree a commit's key is found in.

**Premises checked against the tree first.** (1) *Phase 2130's refutation holds for the per-action view.*
The action carries only a field id, and `UiWitness.view` has no tree. (2) *The server-driven channel
already flushes.* `FormBuffer.step` reads the payload member under the field's id and folds
`SetState(commitTo, value)`, gated. The bounded flush adopts its protocol rather than inventing one.
(3) *A decoded tree's `Local` has no host closure*, so the flush can honour only `commitTo`. The decoder
refuses `commitTo` beside `onCommit`, and a decoded `onCommit` is the inert sentinel. (4) *The bounded
loops fold against a FIXED base tree.* The key the demanded document names is therefore the key the fold
writes. All four held.

**The value is written as the surface sends it, with one narrowing.** The shim harvests a number input as
a number and a text input as a string, and each is written as it arrives. A buffer declaring the `Number`
codec writes only a number. A string is read under the codec's own grammar (`LocalCodec.tryNumberText`)
and anything else is refused. This is narrower than `FormBuffer.fieldFlushAction`, which stringifies the
value and re-parses it through the binding's `parse`. That reading is per slot type and so per host, and
`fuaran-rs` has no typed parse to share. A rule two hosts can state identically was preferred to one only
this host can run.

**Each flushed write meets `CanDispatch` on its own, as the `SetState` it is**, after the commit has passed
the trust boundary. A denied write refuses the event, as a denied confirm continuation does. Without this
rule, a host whose policy admits `CommitLocal` but not a write to a given key would be bypassed by a
`commitTo` the untrusted tree declares. The server-driven flush gates the same write.

**Rejected: lowering a commit to an `Assign` with no key** (2130's refused reading), and **a new core arm
for a flush** (Program D46's three alternatives). Also **rejected: rewriting the commit into a plain
`SetState`.** It would fold the same, but diagnostics would name an action the author never wrote. And a
commit whose event carried no value would read as a malformed `SetState`, where it should read as a
refused flush with its reason.

**The demanded projection of a TREE reads `UiWitness.demandWitnessIn tree`**, which resolves each handler's
commits with `lowerCommits`. The tree-blind `demandWitness` remains what a lone action reads
(`Demanded.ofAction`, a handler stage), because it has no tree to resolve in. In that position a commit is
a leaf that declares nothing, which is true of it: no loop flushes it there.

**Assumptions kept, with the evidence that would falsify each.**

- *Form field ids are unique enough that "first in document order" is the field the surface harvested.*
  Falsified by a tree with two forms sharing a field id where the second is the one rendered under the
  button. The validator's id rules do not cover form fields, so the host and the surface could then
  disagree about which buffer committed.
- *An answer to a confirm re-delivers the originating payload.* The answer's commits flush from the
  answer event's own payload. Falsified by a surface that sends the answer bare, in which case a commit in
  a continuation would find no value and refuse.

**Tidied in the same landing.** Reviewed: `UiWitness.fs` and `BoundedDriver.fs`. In `UiWitness.lower`, the
comment claiming "`CommitLocal`'s flushed value is applied as a state write by the loop before the commit
is interpreted" was false before this phase and is now made true by the flush. It is rewritten to say
which commits still reach the decline. The decline arms are KEPT, not removed. A raw action folded with no
tree (`runBoundedAction`, a server handler's stage) still reaches them, and both the view and the lowering
are total over the closed union. The other paths that 2130 described as "the decline" are the same arms,
and nothing in either file was made dead or duplicate.

---

## 2026-10-10 — D14: the program adapters move onto Fuaran.Program 0.9.0, and the server aliases take the host's query posture rather than defaulting it

**Decided (Phase 2203).** `FuaranProgramVersion` moves 0.8.0 → 0.9.0 and the two adapters and their suites
take every consumer obligation it carries in the same change (Program D42–D45): the value channel's
`OpView.Let` arm, the typed `OpReceipt`, the staged-query defect, the `Report` arm with its `Findings`, and
the demanded document at **version 11**. The proof oracles' translation views a `Let` as a value the UI
state never resolves and refuses a `Report` effect, since the staging model has no report arm (Program
D45), so no differential case draws one. The K7 envelope is re-signed over the same tree, under a new key
whose private half was never kept, because a version-8 envelope is refused by version.

**The server aliases take `QueryPosture` as their first argument, exactly as the core does.** Program D44
puts the host's declaration at the head of every posture derivation so that a staged read is named, not
guessed. An alias that passed `QueryPosture.PureRead` on the host's behalf would publish a pure-read
posture for a host whose registered evaluator is `Reaching`. Its replay posture would then read `safe`
where the runtime stages the read and refuses to undo it. **Rejected: defaulting it in the alias.** It
would keep every call site unchanged, but on a claim this tier cannot make. The conformance suite reads
each handler vector's `queryEvaluator` from the manifest and recomputes under it.

**The demanded corpus is still generated here, and the program repository's hand-moved copy has drifted
in a second vector.** Program 2186 moved its copy to version 9 by inserting `values` after `opaqueLeaves`.
In `missing-opaque-leaves`, which omits `opaqueLeaves`, that insertion had nowhere to land, which is the
mechanism D12 records for `missing-root-member`. That vector now omits four members where its description
names one. In both vectors the recorded read is the same either way. D12's consequence holds: the copy
is re-copied from this one.

---

## 2026-10-09 — D13: `Dispatch` is an opaque leaf of class `in-process`, and no host this tier ships accepts that class by default

**Decided (Phase 2194).** The UI witness views `Action.Dispatch` as an OPAQUE leaf, through the
declaration Program 0.8.0 added (`LeafDeclaration.opaque`, Program D40): reason class `in-process`,
name `Dispatch`. Its message is folded by the host's own `update`, which no walk of the tree can see
into, and a leaf that declares nothing read, in the demanded document, exactly like an action that
does nothing. Now the demanded projection names it in `opaqueLeaves`, a signed envelope over that
projection names it too, and the tree wire specification's §30.1 row and `lowers-to/` vector say so,
certified on this host and on the Rust host. A wire `Dispatch` carries only the inert sentinel and is
marked all the same: the declaration belongs to the arm, and the projection is computed from the
action rather than from where it was decoded. The confirm carriers, which ride the same slot
in-process, are not the escape and still view as the confirm they carry.

**No host coverage this tier builds accepts `in-process` by default.** Program's `HostCoverage.Opaque`
is empty unless a host calls `HostCoverage.acceptingOpaque`, and nothing here calls it:

- `Program.coverageOf` (behind `mkBoundedStrict`) reads its coverage off the effect registry, and a
  bounded client has no `update` to fold a message through, so its fold DECLINES the arm. Accepting
  the class there would claim a capability the placement does not have.
- `BoundedDriver.initStrict` and the server placement's `initStrict` take the caller's coverage, and
  declare nothing on the caller's behalf, for the reason `coverageOf` gives about host-call surfaces:
  inventing a declaration the host never made is this tier asserting something it does not know.

**Consequence:** a strict construction over a tree that contains a `Dispatch` is refused with
`CoverageFinding.UnacceptedOpaqueLeaf("in-process", "Dispatch")` until the host accepts the class.
That is a change from before this phase, when the same tree passed because the leaf declared nothing,
and it is the intended one: the strict paths promise that the host covers everything the tree can ask
for, and no bounded placement covers a message for an `update` it does not have. The DEFAULT
constructions (`mkBounded`, `BoundedDriver.init`) check no coverage and are unchanged, and the fold
still declines the arm exactly as before. A host that genuinely folds `Dispatch` through an in-process
`update`, and wants the strict check, accepts the class explicitly. The decision to accept it is the
host's, and it is visible in the host's own code.

**Rejected: accepting `in-process` in `coverageOf`.** It would keep every strict construction that
passed before passing, but on a false claim, and the class would then be accepted on a placement that
cannot perform it. That is the fail-open default Program D40 refuses.

**Rejected: a public constant for the class name here.** The class is the program core's vocabulary.
A second spelling in this tier's public surface would be one more place for the two to drift, so the
witness names it once, privately.

---

## 2026-10-09 — D12: the program adapters move onto Fuaran.Program 0.8.0, and the demanded corpus stays generated here even where the canonical copy was moved by hand

**Decided (Phase 2191).** `FuaranProgramVersion` moves 0.7.1 → 0.8.0, the first Program release cut
after the adapters moved here, and the two adapters and their suites take every consumer obligation it
carries in the same change: the widened records (`LeafDeclaration.Opaque`, `DemandedProjection`'s
`Iterations` and `OpaqueLeaves`, `HandlerArm.ReadExtent`), the prefix-first op performer and contract
(`OpPrefix`, `OpContract.at`, a LIST at `performedChecked`), the store-bound `Each` shape in the proof
oracles' translation (the UI tier views nothing as one, so the stored arm reaches the model as an
extent it never answers), and the demanded document at **version 8**.

**A withdrawn op performer is ABSENT in every coverage this tier reports, not refused by policy.**
Program D37 amended D28: with ops performed and `ApplyOps` revoked, coverage carries the key in
`Withdrawn`, the gate stays the registry's, and the demanded-effect check reports
`ServerCapabilityWithdrawn "ApplyOps"` before it asks the gate, so a session both suspended and revoked
still reads as withdrawn. The two fuaran#1986 coverage tests assert that reading, and a standing probe
rebuilds the D28 reading from the same coverage (the withdrawal moved onto the gate) and shows both
tests' assertions fail on it.

**The demanded corpus is regenerated through `DemandedCorpus.emit`, never moved by hand, and where it
and the program repository's hand-moved copy disagree the generator is right.** Program's 1991 and
2130 moved their copy to versions 7 and 8 by inserting the new members after `opaqueHandlers`; in the
`missing-root-member` vector, which omits `opaqueHandlers`, that insertion had nowhere to land, so the
copy omits three members where the vector says one. The generator omits exactly `opaqueHandlers`, and
the recorded read is the same either way (`MissingMember` naming `opaqueHandlers`). The five opaque-leaf
vectors Program authored are now generated here, byte for byte. Rejected: making the generator
reproduce the hand-move, which would publish a vector that does not test what its description says.
**Consequence:** the program repository's copy is re-copied from this one (the declared direction in
`copies.json`), not the other way.

**Rejected: declaring `Dispatch` opaque here.** Program 2130 made the mark available; adopting it on
the UI witness changes what every host's coverage reports and is fuaran#2194's change, with its own
migration. This raise declares no opaque leaf, so no host's coverage moves.

## 2026-10-08 — D11: `Confirm` is a two-event round trip on the bounded path; the pending question is host state, carried in reserved store slots

**Decided (Phase 2106).** On the bounded path a `Confirm` is two events. The GESTURE asks: it emits the
`Confirm` client effect with a token naming the confirm's address — the node id, `#`, and the
confirm's structural path in the node's action (dot-joined chain positions and continuation names, the
empty path being the action itself; the same `ConfirmPath` the server-driven host mints) — and runs no
continuation; the rest of an enclosing chain runs around it. The ANSWER is the originating event
re-delivered with `confirmToken` (a string) and `confirmAccepted` (a boolean), re-validated in full; it
folds the addressed confirm as the bounded core's `Choose` with the answer as its entry, `onConfirm`
the true arm and `onCancel` — or the empty sequence — the false. The tree wire specification's §30.1
row and the program specification's driver-semantics rule (§10.5) state it; four scenarios
(`confirm-answer-yes`, `-no`, `confirm-withdrawn`, `confirm-duplicate-answer`) pin it, and this
repository's three bounded placements and a second host reproduce them step for step.

**Where the pending question lives: host-reserved store slots, one per token, under `host.confirm.`.**
The store is the one thing all three placements already thread — the server placement's session is a
core type this repository cannot widen, and a record field on the other two would have been a second
mechanism and a breaking field addition. A tree cannot write a slot (the host-reserved prefix refuses
every tree-originated write, on every path), and it cannot READ one either: the slots are taken out of
the store before a step folds or re-resolves anything and put back after, so they are the loop's state
carried in the store, never a value a binding resolves. The precedent is the upload slot
(`host.upload.<nodeId>`): a fact only the host may state lives under the host prefix.

**The correlation rule.** An answer is admitted only when its token is PENDING and still addresses a
confirm in the node's current action; admitting it consumes the token. Any other admitted event
WITHDRAWS every pending question — a surface holds a confirmation modal, so an event that is not its
answer means the reader moved on, and a late answer must not act on a store they have since changed.
A stale, duplicate or forged answer is refused as an EVENT (the store, pending slots included, is
untouched), and the selected continuation meets the dispatch gate on its own before it runs. A
confirmation remains a courtesy and never an authorisation: a hostile surface answers yes without
asking anyone, and the gate the continuation meets is the only control.

**How a fold tells an ask from an answer: the loop addresses the action it folds.** The core's
`ActionView` has no channel for "this confirm is at path p" or "this event IS the answer", and the UI
union must not grow one — a continuation on the wire is a surface that can perform it without asking
(WIRE_FORMAT §3.6.22). So after the trust boundary admits the event the loop rewrites the action it is
about to fold, wrapping each addressed confirm in a `UiWitness.ConfirmCarrier` riding
`Action.Dispatch`'s in-process payload: an ASK carrier views as the question's leaf and lowers to the
effect; an ANSWER carrier views as `Choose`. The carrier type is this package's own and sealed, so no
decoder produces one — a wire `Dispatch` carries only the inert sentinel. A RAW `Confirm` (a server
handler's stage, `BoundedActions.runBoundedAction` on an unaddressed action) is declined exactly as
before: it has no token, so its question could never be answered.

**The demanded projection reads a separate view.** `UiWitness.demandView` makes a confirm demand its
question and the union of both continuations; every `Demanded.*` / `ServerDemanded.*` entry point,
`sign` / `verify` and the server placement's `initStrict` read `demandWitness`. It is never folded: it
would ask and run an arm in one event.

**Rejected.**
- *Stateless correlation, the server-driven host's design* (the token alone, resolved against the
  tree): it cannot refuse a duplicate answer, so a replayed yes would run the continuation twice. That
  host keeps its design; its §3.6.22 obligation is unchanged by this decision.
- *A token-to-continuation map holding the BRANCHES* rather than the tokens: the continuation is
  re-read from the node's current action, as §5.2 of the program specification already required, so
  only the question needs to be remembered.
- *A new arm in the bounded core's view, or a core change to carry the address*: the core is another
  repository's contract, and the carrier needs none.
- *The ordinal of a confirm among a step's emitted effects as its token*: an unresolvable prompt asks
  nothing, which shifts every later ordinal; a path does not move.

**What the proof oracle covers, stated rather than left to be assumed.** `proofs/program/BoundedFold.fst`
models the fold over RAW actions under `UiWitness.witness`, and its differential host feeds it raw
actions — the oracle's view of a confirm is unchanged (a leaf its lowering declines). The ask carrier's
emitted question and the answer carrier's `Choose` are outside that corpus: the `Choose` arm is the
core fold's own, modelled generically, and the ask's emit is pinned by the driver-semantics scenarios
and `ConfirmRoundTripTests`, not by a theorem.

---

## 2026-10-08 — D10: UI actions keep their compact spelling and lower to the program core by a table; a generic action wire is deferred

**Decided (Phase 2103).** The fourteen `Action` arms keep their UI spelling on the wire. Their
meaning on the bounded path is a **lowering** onto the bounded program core's action view
(`Fuaran.Program.Bounded`'s `Sequence` / `Assign` / `Call` / `Leaf`), stated as a normative table in
the tree wire specification (WIRE_FORMAT.md §30) and pinned by its `lowers-to/` vectors, which this
repository certifies through `UiWitness.view` and a second host certifies through its own bounded
loop. Before this the mapping existed only in those two hosts' code.

**Rejected: re-spelling every action in a generic `Sequence` / `Assign` / `Leaf` form.** Measured, not
estimated, by a throwaway script (not shipped) over the conformance corpus — every parseable `.json`
outside `reject/`, 893 documents, 101 actions at the five action positions it holds (`onClick`,
`onConfirm`, `onCancel`, `onDismiss`, `onSubmit`); 30 deliberately malformed inputs could not be
parsed and were skipped — and over the prompt pack's `few-shot.jsonl` (18 records). Two generic
spellings were measured, both canonical (Ordinal keys, compact): **minimal** — `Chain` becomes
`{"$type":"Sequence","members":[…]}`, `SetState` becomes `Assign` with `valueFrom` renamed `from`,
`Call` is unchanged, and every other arm becomes `{"$type":"Leaf","leaf":"<arm>","args":{<its other
members>}}`; and **self-declaring** — the same, with each leaf also carrying the §30.1 declaration
(`"declares":{"effectKinds":[…]}` or `{"hostCalls":[…]}`), which is what a host that does not know this
vocabulary would need in order to read anything off a leaf at all.

| Measure | Minimal | Self-declaring |
|---|---|---|
| Corpus: bytes added | +1,154 | +2,662 |
| … as a share of the 4,825 bytes of action sub-documents | +23.9% | +55.2% |
| … as a share of the corpus's 2,177,426 bytes | +0.05% | +0.12% |
| Prompt pack: bytes added (2 actions, both `Chain`, 52 bytes) | +14 | +14 |

Per arm over the corpus (occurrences, then bytes added minimal / self-declaring): `Chain` 41, +287 /
+287 (+7 each); `Dispatch` 13, +182 / +182; `Notify` 12, +276 / +1,058; `Call` 9, 0 / 0; `Navigate` 7,
+161 / +441; `SetState` 7, −19 / −19; `WriteToClipboard` 4, +92 / +284; `Confirm` 2, +46 / +46;
`Invoke` 1, +23 / +92; `AiTool` 1, +23 / +90; `ReadFileBody` 1, +23 / +67; `Focus` 1, +23 / +60;
`CommitLocal` 1, +23 / +23; `Print` 1, +14 / +51. Each arm's figure is its own spelling's change,
its children's excluded, so the column sums to the total.

**Why the compact spelling is kept.** The byte cost is real for what it buys and small in absolute
terms, so it does not decide this alone. What decides it is that a generic spelling buys a host that
does not know this vocabulary nothing it can act on: a leaf it cannot perform is still a leaf it
cannot perform, whichever way it is spelled, and the only thing a generic reader gains is the
declaration — which the table already states, once, where every host reads it, instead of in every
document. Against that, the migration would touch every emitter, every decoder, the corpus, the
prompt pack and every stored emission, for a spelling that is a quarter to a half longer where
actions are, in exchange for nothing a current host needs. The two hosts that run actions on the
bounded path agree on the table under its vectors today.

**Revisit when any of these holds:**

1. a second domain's client-side actions must run on a host that is not .NET — at which point a
   domain-neutral action spelling has a consumer the table cannot serve;
2. the program specification's own second-domain condition (its §11.3 deferral of a generic
   instantiation over a second domain) is met; or
3. keeping the per-host lowering implementations in agreement costs more than the migration would —
   measured as the `lowers-to/` family's maintenance across hosts, not asserted.

**Consequence.** A host that runs actions on the bounded path certifies `lowers-to/`. An arm added to
`Action` needs a §30.1 row and a vector in the same change-set: the family's manifest lists the arm
set, and both certifying hosts fail when their closed union and that list disagree.

---

## 2026-10-04 — D9: the program UI adapters take UI-family ids before their first release from here

**Operator decision, 2026-10-04 (Phase 2022).** The two adapters that instantiate the bounded program
core over this tier's types arrived here in Phase 2012 under the ids they had carried in the program
repository, `Fuaran.Program.UI` and `Fuaran.Program.Server.UI`. They are renamed **`Fuaran.UI.Program`**
and **`Fuaran.UI.Program.Server`** — package id, assembly, project directory and root namespace — and
their test, parity and benchmark projects follow as `Fuaran.UI.Program.*`.

**Why.** Since Phase 2012 the adapters live in this tier, depend on it, and version in its lockstep
line; the program core references nothing here. A `Fuaran.Program.*` id on a package this repository
releases says the opposite of that dependency — it reads as a member of the core's family, released
on the core's line, which is the confusion that the move existed to remove. Under the UI prefix the
id says where the package is released, what it depends on, and whose version line it follows.

**Why now.** This is the cheapest point the rename will ever have. Nothing has been published from
this repository under the old ids — the 0.91.0 slot that carries them is an untagged draft — and the
registry's publishing trust for them has not been set up here. After a first release a rename costs
every consumer of that release a second migration; before it, the only consumers are those of the old
ids' last version, 0.7.1, published from the program repository, and they take one move (new ids, the
version jump, new `open` lines) instead of two.

**Consequences.** `STABILITY.md`'s 0.91.0 slot records the rename as breaking for anyone who took the
old ids, and names 0.7.1 as their last version. The old ids are deprecated on the registry in favour
of the new ones once the new ids are published; that is an act on the registry, outside this
repository. The publish workflow's id filter needs no adapter-specific arm any more: every package
this repository packs again carries a `Fuaran.UI.*` id. The program proof models and extractions under
`proofs/program/` are byte copies of the program repository's files and keep their wording; only the
host and project names that point at them moved.

**Rejected.** Keeping the old ids — it leaves a package family prefix that no longer matches the
repository, the version line or the dependency direction, and the rename only grows more expensive.
A forwarding package under each old id — a second package to version and release for consumers who
can change one reference, when the registry's deprecation pointer already tells them where to go.

---

## 2026-09-23 — D8: the temporal vocabulary is renamed on the 0.2.0 clean-break precedent, riding the untagged 0.86.0 draft — not a profile major

**Operator ruling, driver session, 2026-09-23 — recorded before any renamed byte (Phase 1811, task
1).** Phase 1811 renames the temporal family so the names say what the controls already do: the
form-field kinds `Date` → `DateTime` and `DateRange` → `DateTimeRange`, the enum `DateVariant` →
`DateTimeVariant` (its cases `Date` / `Time` / `DateTime` unchanged), the pair record `DateRangePair`
→ `DateTimeRangePair`, and the formatters `Format.Date` → `Format.DateTime` and `CellFormat.Date` →
`CellFormat.DateTime`. `DateStyle` does not move — it still styles only the date part, beside
`TimeStyle`. Four `$type` discriminators move on the wire; two type names move in the IDL, the schema,
`enum-tokens.json` and every host's types. The vocabulary-growth charter (§4.2) classes a rename as a
major-version act, and the phase's first task was to rule on the **versioning vehicle** between

- **(a) a profile major** — `core@1.x` → `core@2.0`, a new `/vN/` + `$id`, `Foreign` hard-refusal
  for every old consumer and a generated migration shim per host (WIRE_FORMAT §15.4's
  removal/rename row), and
- **(b) a coordinated clean-break revision on the 0.2.0 precedent** (WIRE_FORMAT §1.1) — one
  change-set across every host in the §11.0 roster, the corpus regenerated on the new bytes in the
  same change-set, sanctioned by §15.4's pre-1.0 posture.

**Ruled: (b).** The operator's words, against the driver's recommendation of (b) with the ground
below: *"on 1811, do as recommended."*

**The ground.** The fuaran-dotnet `0.86.0` slot is a **draft**: untagged, and pinned by no
public-path consumer. Phase 1810 minted it as BREAKING (its `timeStyle` entry in `STABILITY.md`), and
Phase 1812 rode it. Under the draft-slot rule a change of the same class as the draft already carries
RIDES the slot, so 1811 **rides 0.86.0** — it appends to that `STABILITY.md` entry and moves no
number. A profile major would have been the vehicle for a rename that reaches a *released* consumer;
none does, and spending the profile's major counter on a pre-1.0 rename that no shipped reader has
to negotiate would make the counter say something no consumer can act on — the same reasoning §15.4
records for the optional-field exemption (Phase 1670), applied one row down.

**What the ruling obliges, and where the old names survive.** Every host moves in ONE change-set:
the F# reference, the TypeScript, Python, Go and Rust codecs, and the Swift and Kotlin render
projections, with both bundled corpus snapshots re-synced and re-declared in the same landing window.
The old `$type`s survive **only as §16 lenient-ingest aliases** — `Date`, `DateRange`, `Format`'s and
`CellFormat`'s `Date` each decode to the new name and re-encode canonical — and the canonical encoder
never emits them. That is a departure from the pure 0.2.0 form, where retired names were a hard
decode error, and it is recorded rather than smoothed over: §16 admits a shorthand on evidence that
models emit it and rules out backward compatibility as a ground, so these four aliases are listed in
the §16 table on the same footing as the 0.28.0 column-member aliases (Phase 1821) — kept by this
ruling, never taught, pinned by the corpus. The invented spellings the rename exists to close —
`Time` and `TimeRange` — ARE admitted on §16's own ground, normalising to `DateTime` /
`DateTimeRange` with `variant: "Time"`.

**Rejected.** (a), for the reason above. Also rejected: leaving `Format.Date` / `CellFormat.Date`
behind while the form fields move — `Format.DateTime` is an honest name only once the formatter
renders a time, which Phase 1810 made true, and a family that says `DateTime` on the input side and
`Date` on the presentation side would re-open exactly the discoverability gap the rename closes.

**Consequence a behind reader carries (§15.3).** A reader that predates this change-set meets
`DateTime` / `DateTimeRange` as an unknown discriminator, materialises a transport-only `Unknown`,
preserves the bytes verbatim, and renders the placeholder (or the author-declared `fallback`, Phase
1812). That is the cost the chosen vehicle carries instead of a `Foreign` refusal, and it is the
correct one: the old reader cannot destroy what the new producer authored.

---

## 2026-09-10 — D7: three claims about the language tier that turned out to be false, and the boundaries they mark

**Why they are here rather than in a work log.** Each was written down as a defect, survived long
enough to be planned against, and is wrong for a reason that will produce the same wrong belief
again. What follows is the corrected model in each case, so the next reader meets it before the
mistaken one. (Phase 1646.)

**1. A "required record member" cannot be missing at pre-emit, and a validator family over them
would fire on nothing.** The claim was that every required member of every record reachable from a
kind is unguarded at the pre-emit layer, generalising FUARAN108's shape, with `FormField.required`
as the instance. It is false at this layer by construction: pre-emit validation walks a
`Node<'Msg>` the host has already built, and an F# record's required member always has a value —
there is nothing absent for a rule to find. FUARAN108 is not the same shape either; it reports a
member that is PRESENT and resolves to nothing, which is a question a validator can ask.

The observation behind the claim was real but belongs one boundary away: an emitting model omitted
`required` and the DECODER refused the document (`MISSING_FIELD`). That is the decoder doing its
job. The gap it exposes is in the TEACHING — the material a model is given enumerates the required
fields of every KIND, and `FormField`, `TreeItem`, `SwitchCase` and `TabHeader` are records, so
their required members are taught by nobody and guarded by nothing that measures teaching. That
guard lives in the evaluation harness, which is not this repository, and it is a piece of work in
its own right rather than a tidy-up.

**2. A no-op `Action` is a NAME, not a wire case.** `ButtonSpec.OnClick` and `FormSpec.OnSubmit` are
required, and an enhancement-driven control — one whose behaviour comes from a host enhancement over
the rendered element rather than from the document — genuinely raises nothing. `Action.Chain []` is
already exactly that, on every host and in the corpus; what it lacked was a name, which is why it
read as an oversight at every call site. `Action.none` is that name and encodes to the bytes an
empty chain has always encoded to.

A new wire case was considered and refused on the [vocabulary charter](VOCABULARY.md)'s own ground:
it would be a near-synonym of a shipped case, indistinguishable in meaning, and it would cost a
codec arm, a schema alternative and a corpus fixture on every conformant host to express something
the format already expresses. Making `OnClick` optional was refused separately — a control whose
gesture is unstated is a control nobody can reason about, and "raises nothing" said explicitly is
worth more than said by omission.

**3. A dialect pair whose CANONICAL side does not decode is not a lossy normalisation.** The pack's
lenient-dialect proof reported `1 of 40 pair(s) failed the loss-free proof`, which was read twice as
a shorthand that loses information. It is not one. The pair is a teaching COUNTER-EXAMPLE — a
`Metric` whose value is the formatted string `"1h 20m"`, introduced in the pack by the words *this
exact emission fails to decode* — so the canonical side has no decoded value for the dialect side to
differ from, and the verifier had recorded that correctly as `canonical-fail` rather than `lossy`
all along. Only the summary line added the two together.

The lesson is about the report, not the pack: a check that collapses two verdict classes into one
sentence will have that sentence believed. `dialect-verify.fsx` now counts and words them apart, and
the advisory tier's fallback for a non-decoding hand block is what it always was — the mechanism
working, not a defect to chase.

---

## 2026-09-02 — D6: writing direction is a DOCUMENT fact and a BOUND-TEXT fact; it is not on the wire

**Decided.** Direction enters a Fuaran page at exactly two places, and neither is a wire slot.

1. **The document declares one direction, derived from its locale.** A host says which language the
   page is in — `LocaleSource.Explicit "ar-EG"`, or its ambient locale — and `lang` + `dir` on
   `<html>` follow from that one declaration. The direction is never passed separately, so the two
   cannot disagree. `Formatting.textDirection` is the derivation, shared by every arm.

2. **A display leaf whose text is RUNTIME-BOUND carries `dir="auto"`.** The renderer emits it on the
   node wrapper; the browser then resolves that element from its own first strong directional
   character. The policy is `Accessibility.isBidiIsolated`, kind-level and total, so a new kind
   forces an answer rather than defaulting to silence.

Everything between those two — the layout — is handled by CSS logical properties in the reference
stylesheet, which mirrors from the document's `dir` with no override block and no second sheet.

**The slot set, and why it is that set.** `TextSource.Bound` only. `Literal` is the author writing in
the document's language and `I18n` is a host-resolved translation of the document's own locale; both
are already correct under the document's `dir`, and `auto` on them would let one stray character
re-lay-out a line the author controls. `Bound` is the only case whose direction genuinely cannot be
known at author time. And only on a **display leaf**: `dir` inherits, so `auto` on a layout container
would resolve ONE direction from the first strong character anywhere beneath it and impose it on
every child — the opposite of what a mixed-direction page needs. Data surfaces that own per-cell
emission (`DataGrid` / `Chart` / `Map`) are excluded for the same reason, and `Custom` because this
library cannot know what a host-rendered body puts on the page.

**The wire question — ASSESSED, and the answer is NO.** A per-node direction override was considered
and is **not** admitted to the wire. The vocabulary charter's gate asks for a demonstrated
inexpressible case, and none was found:

- *A page in one language.* Expressible: the document's locale.
- *A page whose data is in another language than its chrome.* Expressible: `dir="auto"` on the bound
  leaves, which is strictly better than a wire slot because it needs no emitter to know the
  direction of a value it is passing through.
- *A region deliberately fixed against its content* — a code block, a table of Latin identifiers on
  an Arabic page. Expressible: `ExtraAttributes` does not carry `dir` (its allowlist is `data-*` /
  `aria-*`), but a **host** composes such a region in its own shell or its own `Custom` renderer,
  which is where a deliberate override belongs; a tree emitted by a model has no business asserting
  one.
- *A single mixed run inside one text value* — Arabic and English in the same sentence, each needing
  its own embedding level. **Genuinely not expressible**, and it is not expressible by a per-node
  slot either: it needs per-SPAN markup inside a text value, which is a markup question
  (`NodeKind.Markdown`'s territory) rather than a direction question. Recorded as the one open case;
  it does not motivate the slot that was under consideration.

So the wire is unchanged by this phase, which is the outcome the assessment was there to establish
rather than to assume.

**Rejected.**

- **A `dir` slot on every node.** Would put a presentational attribute in a semantic tree, and every
  emitter would then have to decide it for every node — with no better information than the browser
  has, and usually worse, since the emitter often does not know the value being bound.
- **Emitting `dir="ltr"` / `dir="rtl"` rather than `auto` on bound leaves.** Requires the renderer to
  guess a direction from a string, which is exactly the analysis the browser already performs
  correctly and to spec.
- **An `[dir="rtl"]` override block in the stylesheet.** Mirrors the page but cannot un-mirror a
  nested LTR island, which is the case a real mixed-direction document is made of. Logical properties
  scope correctly by construction; the catalog's `?dir=rtl` harness exists to show the difference.

**Consequences.** No wire change and no vocabulary change. The reference stylesheet's inline-axis
rules are now logical, so **every host tier's byte-copy inherits the mirroring** — but a tier's own
document SHELL does not, and each must derive `lang`/`dir` from its own locale seam
([Phase 1128](../../roadmap/phases/1128-platform-baseline-host-adoption.md)). The Giraffe shell's
hardcoded `lang="en"` is gone: a shell that declares no locale now emits no `lang` and no `dir`,
because asserting English about a document nobody made a statement about is the defect this phase
exists to remove, not a safe default.

Roadmap: [Phase 1114](../../roadmap/phases/1114-rtl-bidi-document-language.md).

---

## 2026-08-20 — D5: this tier owns its vocabulary; Fuaran.Core ships only the engine

**Decided.** The IDL **engine** is a package — `Fuaran.Core.Idl`, distributed from Fuaran.Core 0.4.0
alongside the rest of the spine. A **vocabulary** — the `Idl` value describing this language's kinds,
unions, enums and records — is not something the engine's repo distributes. It is data belonging to
the domain whose contract it describes, and for the UI wire model that domain is this repo.
Fuaran.Core's DECISIONS.md D14 states the same rule from the other side.

**Rejected: a shared vocabularies package.** The UI vocabulary sitting in a `Fuaran.Core.*` package
would make every kind this language admits a release of the substrate repo, put one release cadence
across every domain's wire, and bake a single domain's name into the substrate's identity
permanently. `VOCABULARY.md`'s admission gates are written and enforced here; the artefact they
govern should not live where the gate does not run.

**The consequence for a second domain, which is the point.** Before 0.4.0 the engine was
`IsPackable=false`, so no workspace could consume it and this tier was necessarily the only domain
using it — via a byte-copy of the generated artefact out of the substrate repo's own test project.
That is not a distribution mechanism; it is the absence of one. Any domain can now
`PackageReference` the engine, declare an `Idl`, and generate its structural layer without either
the declaration or the output passing through anyone else's repo.

**What has NOT moved yet, stated plainly rather than implied.** `uiIdl` and its declared-support
record are still in Fuaran.Core's test project, because they are also that repo's only full-scale
engine-certification fixture — seven suites read them to certify corpus byte-parity, the
compiled-codegen drift guard, the schema leg, the op leg, the diff classifier and the cross-host
fuzz. Neither route out is sound today: a package dependency from the substrate onto this tier
closes a cycle in the feed (this tier already depends on `Fuaran.Core.Idl`), and a compile-link
across a sibling checkout would make that repo's build depend on a checkout it does not control.
Nor can `idl.json` stand in for the declaration — `Artifact` renders, it does not parse, and the
generator needs the declared-support record and the host prelude besides.

So the vocabulary's arrival here is staged, and `scripts/sync-generated-layer.ps1` is **demoted
rather than retired**: its default is now the drift check, and the byte-copy is behind an explicit
`-Adopt`. The copy stops being the mechanism and becomes an opt-in adoption step — which also
removes a live hazard, since a bare run of the old script once erased 292 lines of a tier that was
legitimately ahead. The check is enforced on every push by `apply-parity-fable.yml`, so the two
copies stay pinned rather than trusted.

**When it retires.** Once the vocabulary lands here, the check becomes an in-process
regenerate-and-compare against the packaged engine, with no sibling checkout involved at all. The
`.fantomasignore` entry for `src/Fuaran.UI/Generated.fs` survives that change unaltered — the file
is generated output either way.

**CLOSED 2026-09-03 (fuaran#1181).** The staging above is finished, and the two paragraphs before
this one are kept as the record of why it took two steps rather than as a description of the tree.
What changed: `fuaran-core#114` made `Artifact.parse` a total inverse of `Artifact.render` and gave
the declared-support record and the host-prelude reference a canonical document of their own
(`support.json`), which removed the blocker this decision named — "`Artifact` renders, it does not
parse, and the generator needs the declared-support record and the host prelude besides". So the
vocabulary is here, in `src/Fuaran.UI.Idl/`: `Vocabulary.fs` and `Support.fs` (the declaration), and
`idl.json` + `support.json` rendered from them and committed beside them. The third member of the
triple, the host prelude, was already here — `src/Fuaran.UI/HostPrelude.fs`, which the support
document NAMES rather than inlines.

`scripts/sync-generated-layer.ps1` is deleted, and `apply-parity-fable.yml`'s job is the in-process
check: `src/Fuaran.UI.Idl.Tests` parses the two committed artifacts **from bytes**, emits the
structural layer through the packaged `Fuaran.Core.Idl` / `.Codegen`, and byte-compares
`src/Fuaran.UI/Generated.fs`. One checkout, no sibling repo, no token, and — the property the old
guard lacked — it FAILS rather than exits 0 when it cannot find what it certifies against.

**No cycle was closed in the feed**, which is what this decision was most careful about. The
dependency direction is unchanged: this repo consumes `Fuaran.Core.Idl` and `.Codegen`, the
substrate consumes nothing of ours. `src/Fuaran.UI.Idl` is `IsPackable=false`, so the engine
dependency stops at the vocabulary and never reaches a shipped `Fuaran.UI.*` package.

**What is left on the substrate side** is the fixture's deletion, which is `fuaran-core#123`'s work
and depends on this landing first — deliberately, so no interval exists in which the domain has no
vocabulary. Until it runs, `tests/Fuaran.Core.Tests/UiIdl.fs` and `UiIdlSupport.fs` still hold the
first ~2,492 and ~423 lines this repo now owns; the remainder of each file, and `UiGenerated.fs` and
`UiHostPrelude.fs`, are that repo's own certification fixture and are its call, not ours.

## 2026-08-20 — D4: the accessibility projection targets the node's semantic element, not its wrapper

**Decided.** A node's `Accessibility` projection — and the `aria-*` half of `ExtraAttributes` — is
emitted on the node's **semantic element**: the single element the kind body renders, when that
element rather than the wrapper carries the node's semantics. The set is closed and currently three
kinds: `Link` (`<a>`), `Button` (`<button>`), `Image` (`<img>`). Every other kind keeps the
projection on the wrapper `<div>`, unchanged.

Membership is a property of the body, tested by three conditions that must all hold:

1. the body is a **single root element** — not a container of siblings, not a label-wrapped control;
2. that element carries **native semantics of its own** (an interactive role, or a graphic), so a
   `role` / `aria-*` on an ancestor `<div>` is announced against the wrong node;
3. the element **is** the node — nothing else in the body competes for the accessible name.

Before this, every renderer had exactly one a11y emission site and it was the wrapper, with no seam
by which a kind could opt in: `renderKind` was never passed `node.Accessibility` in any tier. So the
placement was not a per-kind decision that happened to be wrong for `Link` — it was a uniform
architecture that had never been asked the question. The observable cost: a nav projection marking
the current page with an `aria-current` extra-attribute put it on the wrapper `<div>`, and the host
had to reach the anchor with a descendant CSS selector; and `AriaRole.Link` / `AriaRole.Button` on a
node whose body already renders an `<a>` / `<button>` announced two interactive elements where the
author declared one.

**Rejected — a per-kind opt-in list.** It has no falsifier: a kind joins when someone remembers, and
nothing tells a reviewer whether a new kind belongs. Stating the rule as a property of the body means
the vocabulary charter (`VOCABULARY.md`) can answer it once, at admission, for every kind that
follows.

**Rejected — including the form-field kinds** (a tempting reading of "the body is the semantic
element"). `Select` renders `<label><span>…</span><select></label>`: the control is not the body root
(condition 1) and the wrapping `<label>` already supplies an accessible name (condition 3), so a
forwarded `aria-label` would compete with the one the author wrote. Field-level targeting is a
different feature — it needs a per-kind target selector, not this predicate — and is deliberately not
in this rule.

**`ExtraAttributes` splits by prefix, deliberately.** `data-*` stays on the wrapper because it is
addressing: it sits beside `data-fuaran-node-id`, which layout observers, DOM-snapshot hooks and the
in-page introspection surface scan for, and moving it would move the node's address. `aria-*` follows
the projection, because an `aria-*` hatch is an accessibility attribute and belongs where the
accessibility attributes are — that half is the whole of the `aria-current` defect above. On a
non-forwarding kind the split is unobservable: both halves land on the wrapper, in the same
key-sorted order as before.

**Two limits, stated rather than assumed away.** The protected-email `Link` variant renders an
entity-encoded opaque anchor string on the server tier, so the projection lands on its wrap `<span>`
— the only element that arm owns in *both* tiers, and client/server parity outranks reaching one
tier's anchor; writing attribute names into a `dangerouslySetInnerHTML` payload would open a new
injection seam and was declined. And the predicate is **kind-level** by construction, because the
wrapper must decide before the body is rendered and the only thing it has then is the `NodeKind`;
where an arm has a runtime branch, the arm owns placement within its own body.

**Consequence.** Breaking for consumers pinning rendered DOM — host CSS or DOM-snapshot tests
selecting `role` / `aria-*` on a `Link` / `Button` / `Image` wrapper now find them one level in. The
wire is untouched: no field, no encoding and no fixture moves, and a decoded tree renders the same
information at a different address. Every conformant host that emits DOM owes the same placement, or
the emitted DOM forks by host.

---

## 2026-07-28 — D3: the IDL reaches full `Binding` parity by hosting foreign codecs, not by re-modelling them

**Decided.** The generated `Binding<'T>` now carries every case the hand-written encoder can emit
(`Transform`, `Selection`, `I18n`, `Filter.defaultValue`, and host-only `accessor` slots on `Query` /
`Selection` included), and the full 85-fixture node corpus round-trips through the generated layer
alone. Four modelling decisions carry it, each a shape the swap (692–694) inherits:

1. **A hosted slot (`THosted`) delegates to a host codec instead of re-modelling its vocabulary.**
   `Binding.Transform`'s `source` / `pipeline` are real `Fuaran.Core.DataSource` / `Fuaran.Core.Transform`
   values encoded by `ColumnCodec` / `DataFrameCodec` — the codecs the evaluator already trusts, under
   the same `Canon` discipline, so the composite splices in canonical. Re-modelling the DataFrame
   vocabulary as IDL unions was rejected: it would mint a second set of types beside the ones
   `DataFrame.evalPipelineInEnv` consumes, and the two would drift. Everywhere except the generated F#
   (schema, TS backend, interpreter, sampler) a hosted slot is verbatim JSON, exactly like `TJson` —
   its content is the host codec's business.
2. **Obj-erased binding positions instantiate at `JVal`.** `Transform.params[].from` and `I18n.args`
   are `Binding<obj>` in the hand-written tier, encoded best-effort; the generated layer uses
   `Binding<JVal>` — typed, verbatim, byte-faithful. `TOpaque` was rejected for these positions: it
   erases a real `defaultValue` to a sentinel, which is silent data loss (the D1 argument, again).
3. **Absence is authorable.** Every control `value` slot (and `Modal.onDismiss`, and the grid's
   `value`/`field`, `rowKey`/`rowKeyField` siblings) is `option` in the generated types because the
   wire says absence is legal (Phase 596 auto-bind, Phase 425/426). The context-dependent synthesis —
   absent value ⇒ `Filter(name)` / `State(field id, placeholder)` — stays policy in the hand-written
   decoder ABOVE the structural layer; structure carries absence as absence. Consequence for the swap:
   the generated types can express "auto-bind me" (`None`) where the hand-written types must replicate
   the synthesis by hand — strictly more expressive, and a shape change at every construction site.
4. **The `Range` value's transparent Static is a SLOT convention, not a union one.** `Binding.Static`
   of a range pair encodes as the bare `{"max":…,"min":…}` object; other cases stay `$type`-tagged.
   The slot carries its own hosted codec over the generated `encBinding`/`decBinding` + a `RangePair`
   record (the IDL has no tuple type; the record IS the wire object, so the hand-written
   `float * float` becomes `RangePair` at the swap).

**Not modelled — `Deferred<'T>` (`Pending`/`Ready`/`Error`).** It is not a `Binding` case and not wire
vocabulary at all: it is the resolver's runtime envelope for an `Invoke` in flight ("a runtime value;
not wire-serialised"), and the corpus carries no occurrence. It stays a hand-written runtime type,
untouched by the generated layer and by the swap.

**Also corrected in passing.** The IDL's separate `FilterKind` union was pre-0.2.0 drift — the
hand-written `FilterSpec` holds a `FormFieldKind` (the filters-unification), so the IDL now does too,
and the drifted union is deleted.

---

## 2026-07-27 — D2: a closure's HOST type is free, so the generated layer can be the authoring type

**Decided.** A function-typed slot declares its host signature in the IDL (`TFn`), and any type
transitively holding one is generated generic in `'Msg`. The IDL-generated structural layer therefore
*is* the authoring type — `Node<'Msg>` — rather than a `'Msg`-erased projection of it. There is no
handler table, no closure address, and no author facade.

**Why.** The encoder never reads a closure. It emits the fixed sentinel `"<closure>"` and moves on;
the decoder reads presence only. Nothing downstream of the declaration depends on what the slot's
host type is — so the host type was always free, and erasing it to `unit` was a convenience of the
first generator (Phase 317), not a property the wire requires. Once that is seen, the `'Msg` can stay
in the tree exactly where the hand-written tier already puts it.

**Rejected — the handler table.** The programme was planned around addressing each closure by
`(nodeId, slot)` or a positional path, holding the closures in a side table keyed by that address,
and reuniting them at render time. Four things were wrong with it, in ascending order of severity:

1. It needs an addressing scheme, and both candidates have real costs — a path is invalidated by any
   `TreeOp` that moves a node; a minted id must ride the wire or a side channel.
2. It needs `TreeOp` laws for what happens to a binding when its node is replaced, moved or deleted —
   a whole phase of work, and a binding that silently survives onto a *different* node is worse than
   one that errors.
3. It puts `'Msg` on a *pair* while the tree stays monomorphic, so every signature in the renderer and
   apply engine threads a type parameter that no longer matches the thing it describes.
4. **It solves a problem that does not exist.** All of the above is machinery for reattaching
   something that never had to be detached.

**Rejected — the projection** (`toGenerated : Node<'Msg> -> Generated.Node`, feeding the generated
encoder). A case per kind and a line per field is comparable in size to the 2,282-line encoder it
replaces, `Types.fs` survives, and the mirror count does not fall. It buys a rename of where the
mirror lives at the price of a second surface to keep in step.

**Corrected — node ids ARE unique.** The programme recorded "node ids are not unique" as settled
evidence, on the strength of 4 of 85 corpus fixtures repeating one. Measured rather than read: the
tier's own `PreEmitValidate` refuses **exactly those 4** with `DuplicateNodeId`, the validator's
`FUARAN001` is an *Error* citing "§4g op-target stability", the session-bundle path refuses such a
tree outright, and every `TreeOp` addresses by `NodeId` alone — so a duplicate is already ambiguous
for `UpdateProp` and `RemoveNode`, quite apart from handlers. Uniqueness is an invariant the corpus
violates, not a property the language lacks. This does not revive the handler table (it is rejected
on its own merits above), but the record should not stand on a false premise. The four fixtures are a
corpus defect, tracked separately.

**Consequences.**

- **The wire is untouched.** `UiGenerated.fs` regenerates byte-identical, and no corpus fixture moves.
  `'Msg` is a host-side type parameter; it has no wire projection.
- **The decoder is the storage shape.** A closure cannot be rebuilt from `"<closure>"`, so
  `decodeNode` returns `Node<obj>` with declared placeholders in the closure slots — the same
  boundary the hand-written `decodeNodeObj` / `WireTree` already draws, reached independently.
- **`'Msg` does not leak.** The fixpoint puts it only on types that genuinely dispatch: on the real
  vocabulary `Binding<'T>` stays msg-free, because the tier already obj-erases exactly where a `'Msg`
  parameter would be inconvenient (`LocalBinding.OnCommit: 'T -> obj`, `Action.Call`'s
  `onResult: obj -> 'Msg`). The spike proves the fixpoint respects that.
- **Fable is unaffected**, checked rather than assumed: the generated shape is now compiled by the
  `fable-smoke` gate on every verify run.

**Open questions this leaves for adoption.**

1. **Host types in a signature.** `TFn.FSharp` is free text, so a signature naming a type the
   generated module does not have in scope (`BindingContext`, `FileRef`) will not compile. Either
   those types move into the IDL or the module needs a configurable `open` prelude.
2. **`Action.Dispatch of 'Msg` is not a closure.** Its `'Msg` is a wire-*omitted* value, not a
   sentinel — a distinct case from `TFn`, needing a "host-only field" optionality that `Node.Motion`
   and `Node.ExtraAttributes` (`WIRE_FORMAT.md` §9) would share.
3. **A per-type-parameter placeholder.** `Binding<'T>.Computed.fn` decodes to a placeholder that must
   produce a `'T`, which cannot be conjured. The hand-written decoder threads a real placeholder value
   per parameter; the generated one should take one alongside each `decT` codec.

**Where it is enforced.** `Fuaran.Core.Idl.Gen.msgCarrying` (the fixpoint); the `Idl.Spike` mini-IDL,
whose `Tabs` kind and `Binding.Computed` case exercise all three legs and are compiled, snapshot-
gated and Fable-gated on every run.

Roadmap: [Phase 689](../../roadmap/phases/689-handler-binding-design-and-spike.md), and the programme
at [generated-authoring-programme.md](../../docs/domain-explorations/generated-authoring-programme.md).

---

## 2026-07-26 — D1: the wire model has no `null`; absence is structural

**Decided.** `null` is not a value in the Fuaran wire model. It appears in no canonical emission,
in any position, on any host. Absence is expressed **structurally**, by omitting the key.

**Why.** F#'s rejection of null is one of its strongest features, and null is the standing regret of
most languages that carry it. A value that inhabits every type defeats the point of having types.
Fuaran is a typed tree with a canonical serialisation; admitting null would put a hole in every slot.

**What this changed.** The format already said so — rule 4: *"never synthesise `null`"* — but two
positions carved themselves out and emitted it: the obj-erased `Binding.Static` seam, and
`Binding.State.defaultValue`. Both now omit:

| Before | After |
|---|---|
| `{"$type":"Static","value":null}` | `{"$type":"Static"}` |
| `{"$type":"State","defaultValue":null,"key":"k"}` | `{"$type":"State","key":"k"}` |

**Rejected.**

- **A dedicated `Unset` binding case.** Does not compose: `State` still needs its `key`, so you would
  carry `Unset` *and* the key — two concepts where one suffices — and it costs a vocabulary-charter
  admission for a case that adds no information.
- **Typed empty** (`""` / `[]` / `0`). Conflates "no selection" with "selected nothing". That is
  null's trap wearing different clothes, and it is worse than null because it is undetectable.
- **Adding `JNull` to `Fuaran.Core.Wire.JVal`** so the substrate could represent what the wire
  contained. This was the obvious fix and it is the wrong direction: it would have propagated null
  *into* the shared substrate, breaking a public DU across three estates to preserve a mistake.

**Consequences.**

- **Decoders still accept `null`** at those two positions as a §16 lenient shorthand for absence,
  because models emit null naturally and the intent is unambiguous. Accepting is not emitting:
  `encode(decode(x))` never reproduces a null. Everywhere else (rule 12 structured-payload
  positions) null remains a hard decode error, pinned by six `reject/reject-null-*` fixtures.
- **`Fuaran.Core.Wire.JVal` stays null-free and is now correct.** Before this decision its parser
  refused null while the format emitted it, so the substrate could not parse two of the corpus's own
  fixtures. The substrate was right; the format was wrong.
- **A silent-corruption path was deleted rather than fixed.** The tier bridged its own JSON AST to
  Core's with `JNull -> JStr ""`, justified by an unreachability assumption. With null gone from the
  model there is nothing to bridge.
- Breaking for **emitters** only: six `nodes/` fixtures and two `lenient/*.expected` changed bytes.

**Where it is enforced.** `WIRE_FORMAT.md` §2 rule 4 (normative, no exception); the encoder in each
of the five hosts; the corpus, which contains no `null` outside `reject/` and the two lenient
*inputs* that prove the shorthand still decodes.

Roadmap: [Phase 677](../../roadmap/phases/677-reject-null-absence-is-structural.md).
