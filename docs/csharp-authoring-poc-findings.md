# C# fluent-builder authoring shape – PoC findings (Phase 172)

**Status:** advisory findings note for the §4e design (the canonical design doc's
C#/VB polyglot-authoring sketch). Design-doc edits are operator-coordinated
cross-workspace; nothing here amends a shipped contract.

**Artefact:** [`samples/csharp-authoring-poc/`](../samples/csharp-authoring-poc/)
 – a runnable console PoC. `dotnet run` over it encodes a representative set of
C#-authored trees and byte-compares each against the language-neutral corpus.

## 1. Result

Twelve C#-authored trees – spanning layout nesting (Card / Stack / Grid /
Dashboard), display (Heading / Metric-KPI / Badge / Markdown), a Form field
family (Text / Number / Checkbox / Choice / TextArea), Button + chained actions,
and one Chart – each produce canonical-JSON **byte-identical** to their corpus
fixture under `wire-format-fixtures/nodes/`, and each is **round-trip-stable**
(`encode → decodeNode → encode` is byte-equal). A negative control (one mutated
field) is asserted *not* to match, so a green run is meaningful rather than
vacuous.

The core finding is structural and strong: **the C# surface never re-implements
the encoder.** It constructs the same F# `Node<obj>` values the F# tier
constructs and runs them through the *same* canonical encoder
(`CanonicalJson.encodeNode`). Wire-identity therefore reduces to *value*
identity of the constructed tree – which a fluent builder over the public typed
contract achieves directly. This is the cleanest possible evidence for §4e: a
second authoring surface that is wire-faithful **by construction**, not by a
parallel encoder kept in sync.

## 2. The F#-shape → C# mapping (what §4e asked the PoC to document)

| F# shape | C# rendering in the PoC | Notes |
|---|---|---|
| `[<RequireQualifiedAccess>]` DU with fields | static factory `NewCase(...)` (`NodeKind<object>.NewLayout`, `Binding<T>.NewStatic`) | The compiler-generated CLR surface; wrapped behind value-helper statics (`Txt`/`Bind`/`Fmt`/`Act`) and per-kind builders so call sites read as idiomatic C#. |
| fieldless DU case | static singleton property (`ToneVariant.Default`, `CellFormat.None`, `ChartKind.Line`) | Directly usable from C#. |
| `'a option` | `FSharpOption<T>` via `Fs.Some` / `Fs.None` | Surfaced on builders as "set it / don't" fluent methods; optional spec fields default to `None`. |
| `'a list` | `FSharpList<T>` via `Fs.List(...)` (`ListModule.OfArray`) | Builders accept C# `params` arrays / `List<T>` and convert at `Build()`. |
| record | generated all-args constructor, fields in **declaration order** | F# emits the ctor in declaration order – confirmed against `HeadingSpec` / `BadgeSpec` / `SemanticStyle`. |
| `'Msg` payloads (`Action.Dispatch`, field `onChange`) | opaque – bridged C# lambdas via `FuncConvert`, never invoked | Per the wire posture (§4/§5 of `WIRE_FORMAT.md`): every `'Msg`/closure encodes as `"<closure>"`, every non-primitive `Static` as `"<opaque>"`. **A C# author never names a message type to author a wire-faithful tree.** This is the practical meaning of the `Node<obj>` storage-shape erasure (§4g of the design doc). |

## 3. What §4e got right (confirmations)

- **Sealed records + fluent builder converging on the same tree is the right
  shape.** The PoC's builders are a thin C# veneer; the payload is the exact
  `Node<obj>` the F# tier builds. No divergent type model was needed.
- **The `Node<obj>` wire-level posture (§4g) makes `'Msg` a non-problem for a
  non-F# author.** Because message payloads and event handlers are unobservable
  on the wire, the C# surface elides the entire `'Msg` type-parameter story: a
  builder fixes `Node<object>` and supplies placeholder closures. The design's
  bet – that the typed-`'Msg` ergonomics are an *F#-host* concern, not a
  *wire/authoring* concern – holds.
- **The encoder's determinism rules (`WIRE_FORMAT.md` §2) are language-neutral
  in practice.** A C#-boxed `double`, `bool`, `string`, and empty sequence all
  encode identically to their F# counterparts with zero special handling – the
  "best-effort `obj`" path (rule 11) keys off CLR runtime type, which C# and F#
  share.
- **Optional-field omit-on-`None` discipline travels cleanly.** Builders that
  leave an optional field `None` produce wire output with the key absent,
  matching the corpus exactly (e.g. `btn-copy-link` omits `disabled`/`icon`).

## 4. Idiom audit (friction points, C#-developer eyes)

Target consumer: a .NET shop evaluating Fuaran with no F# familiarity. The
builder *call sites* (`Trees.cs`) read as clean, conventional C#. The friction
is entirely in the **builder/interop layer**, concentrated in `Interop.cs`:

1. **FSharp.Core types leak at the seam.** `FSharpOption<T>` (`Some` is a
   wrapper, `None` is `null`), `FSharpList<T>` (no collection-initialiser),
   `FSharpFunc<,>` (a C# lambda must be bridged through `FuncConvert`), and
   `FSharpMap<,>` have no C# literal forms. A C# developer authoring *builders*
   would hit these immediately; a developer using the *builders* never sees
   them. A supported package must hide them entirely.
2. **`Action<'Msg>` name-clashes with `System.Action<T>`.** Required a closed-
   generic `using` alias (`FsAction = …Action<object>`). A shipped package would
   want a renamed or namespaced facade type to avoid forcing the alias on every
   consumer file.
3. **Nullable-reference analysis is noise at the interop seam.** FSharp.Core
   ships null-oblivious metadata, so `<Nullable>enable</Nullable>` produces
   neither useful warnings nor errors over the F# types; the PoC sets
   `annotations` to keep the `?` surface without the noise. A package facade
   would be hand-null-annotated and could restore full `enable`.
4. **Record construction is positional, order-significant, and unlabelled.**
   Building a `MetricSpec` is a 10-arg positional constructor call – safe here
   because the builder owns it, but it is exactly the ergonomic hazard a fluent
   builder exists to remove. Confirms the §4e instinct to *not* expose raw
   record construction to consumers.
5. **No friction on the decode/round-trip leg.** `decodeNode` returns
   `FSharpResult<Node<obj>, DecodeError>`; reading `IsOk` / `ResultValue` from
   C# is unremarkable.

Net: the C# *authoring experience* is idiomatic; the *cost* is a hand-written
interop/builder layer that today is the PoC and tomorrow would be the package.

## 5. Proposed §4e amendments (advisory only)

1. **State the `Node<obj>` authoring posture explicitly as the C#/VB story.**
   §4e should record that a non-F# host authors against `Node<obj>` and supplies
   placeholder closures for unobservable slots – i.e. the polyglot surface
   deliberately drops the `'Msg` type parameter, and this is *sound* because the
   wire carries no `'Msg`. The PoC is the evidence.
2. **Note the default-fragments decision a package must make.** The corpus
   fixtures (and this PoC) build nodes with `Accessibility = None`, whereas the
   F# `Fuaran.*` smart constructors layer per-component ARIA defaults
   (`Defaults.Accessibility.card`, etc.). A supportable `Fuaran.UI.CSharp` must
   choose, per builder, whether to replicate those defaults – and that choice is
   *wire-visible* (a defaulted `accessibility` object would appear in the JSON).
   §4e should flag this as a required design decision, not an incidental.
3. **Recommend a generated-or-hand-written facade, not raw FSharp.Core interop.**
   The friction in §4 is all at the FSharp.Core boundary. §4e should commit a
   supported package to a facade that never surfaces `FSharpOption` /
   `FSharpList` / `FSharpFunc` to consumers (see §6).

## 6. PoC → supportable `Fuaran.UI.CSharp` package: the gap list

What this PoC deliberately does *not* do, and a real package would need:

- **Full spec coverage.** The PoC covers ~12 of the ~40 NodeKind/spec shapes
  (enough to exercise layout nesting, bindings, actions, a field family, and a
  chart). A package needs every shipped kind, plus the visualisation grid
  (`GridSpecOf<'row,'Msg>` with its row-erasure boxing), `Tabs`/`Stepper`
  overlays, `Disclosure`, `Custom` bounded-escape, `ErrorBoundary`, fragments,
  and the locale-aware `Binding.Format` family.
- **A null-annotated facade over FSharp.Core.** Hide `FSharpOption` /
  `FSharpList` / `FSharpFunc` / `FSharpMap` behind C#-native shapes (nullable
  refs, `params`/`IEnumerable`, C# delegates, dictionaries). This is the bulk of
  the package's value and the bulk of the work.
- **A real `'Msg` story for hosts that want one.** The PoC's `Node<object>` +
  placeholder-closure posture is correct for *authoring*, but a C# *host* that
  dispatches typed messages needs a generic builder surface (`NodeBuilder<TMsg>`)
  and typed action/binding constructors – out of scope here, non-trivial given
  the obj-erasure boundary the F# smart constructors manage internally.
- **Forward-coupling discipline.** Per `WIRE_FORMAT.md` §11, a new
  `NodeKind`/`Spec`/`Binding`/`Action` case must update encoder + decoder +
  corpus (+ the TS host). A C# package becomes a *fourth* mirror to keep in
  lockstep – a real maintenance cost the §4e go/no-go should weigh. (The PoC
  avoids this by consuming the F# encoder/decoder directly rather than
  re-implementing them – a package could make the same choice and stay a thin
  authoring veneer, which materially shrinks the coupling surface.)
- **Validator parity.** The PoC does no build-time validation (FUARAN* codes are
  an F#-AST walker). A C# authoring package has no equivalent static gate; it
  would rely on the runtime pre-emit self-check (`encodeNode |>
  ArgsJsonContract.validate`) instead.
- **Idiom polish.** Resolve the `Action` name clash with a facade type;
  decide the accessibility-defaults posture (§5.2); restore `<Nullable>enable`.

## 7. Bottom line

Idiomatic C# can author Fuaran trees that are **wire-identical** to F#-authored
equivalents, today, over the settled contract – proven by 12 byte-identical,
round-trip-stable trees plus a negative control. The §4e shape is sound; the
remaining work to make it a *supported* surface is a facade-and-coverage
exercise, not a contract question. This PoC is a citable artefact for the
papers' polyglot-reach claim.
