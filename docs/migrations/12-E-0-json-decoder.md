# Phase 12.E.0 – JSON decoder (Node + TreeOp wire-form decoder)

Ships [`Fuaran.UI.Ops.JsonDecode`](../../src/Fuaran.UI.Ops/JsonDecode.fs) – a Fable-compatible structural decoder that reverses [`Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode`](../../src/Fuaran.UI.OpStream.Abstractions/CanonicalJson.fs) / `encodeOp`. Produces `Node<obj>` / `TreeOp<obj>` (storage-shape erasure per [`12-Z-op-stream.md`](12-Z-op-stream.md)) with `Result<_, DecodeError>` semantics – every wire-shape violation surfaces a structured, AI-recoverable `DecodeError` carrying location + expected-shape hint rather than throwing.

Three downstream consumers ride on this:
- **Phase 12.E (AI-emission micro-eval)** – Gate 1 entry point. `decodeNode` parses the AI's emitted JSON; failure flows to the eval's `parse-failed` outcome carrying the structured `DecodeError`.
- **Phase 12.S (visual catalog) AI-eval-input mode** – backing decoder for the `?ai-eval=1` JSON paste mode. The catalog's narrow stand-in (`samples/catalog/JsonDecode.fs`) retires once the catalog imports this module.
- **Downstream AI-driving-the-UI consumer (private sibling)** – needs decode + apply + replay round-trip against the persistent op stream.

## Public surface

```fsharp
module Fuaran.UI.Ops.JsonDecode

[<RequireQualifiedAccess>]
type DecodeErrorCode =
    | INVALID_JSON
    | MISSING_FIELD
    | WRONG_TYPE
    | UNKNOWN_DU_CASE
    | WRONG_NODE_KIND
    | EMPTY_NODE_ID

module DecodeErrorCode =
    val toString: DecodeErrorCode -> string

type DecodeError =
    { Code: string
      Path: string
      Message: string
      ExpectedShape: string option }

module DecodeError =
    val create:
        code: DecodeErrorCode -> path: string -> message: string -> expectedShape: string option
            -> DecodeError

val decodeNode: json: string -> Result<Node<obj>, DecodeError>
val decodeOp:   json: string -> Result<TreeOp<obj>, DecodeError>
```

The `DecodeError` shape mirrors the §4d AI-recovery JSON envelope vocabulary from [`Fuaran.UI.Ops.ErrorRender`](../../src/Fuaran.UI.Ops/ErrorRender.fs) so eval gate-1 failures stay in the same shape downstream consumers (gates 2/3/4 + AI consumer) read.

## Module placement (deviation from the phase opener)

The phase opener specified `Fuaran/src/Fuaran.UI/JsonDecode.fs`. The shipped form lives at [`Fuaran/src/Fuaran.UI.Ops/JsonDecode.fs`](../../src/Fuaran.UI.Ops/JsonDecode.fs) (`Fuaran.UI.Ops.JsonDecode` module). Reason: `decodeOp` consumes `TreeOp` which lives in `Fuaran.UI.Ops`, which already depends on `Fuaran.UI`. Co-locating the decoder in `Fuaran.UI` would close a project-reference cycle.

Discarded alternatives:
1. **Split decoder across two modules** (Node decode in `Fuaran.UI`, Op decode in `Fuaran.UI.Ops`). Awkward API split, two STABILITY entries, every consumer needs both packages.
2. **New `Fuaran.UI.JsonDecode` project referencing both.** One more package to discover; the phase opener explicitly said "no new project".
3. **Put both in `Fuaran.UI` + add Fuaran.UI → Fuaran.UI.Ops project ref.** Closes a project-reference cycle (Ops already references Fuaran.UI). Not a viable option.

The shipped placement mirrors where the encoder sits: [`Fuaran.UI.OpStream.Abstractions`](../../src/Fuaran.UI.OpStream.Abstractions/) sits above both `Fuaran.UI` and `Fuaran.UI.Ops` and owns `encodeNode` + `encodeOp`. The decoder is the inverse and needs the same access. `Fuaran.UI.Ops` is the lowest-tier package that depends on both surfaces, so it gets the decoder.

The §4l down-shift portability story (Fuaran.UI standalone-able like Feliz) is preserved: Fable-only consumers who only need typed-tree construction don't pull the decoder; consumers who already consume `Fuaran.UI.Ops` for `Apply.apply` get the decoder at the same tier they already pay for.

## Algorithm

The decoder is symmetric with the encoder pinned in [`12-Z-op-stream.md`](12-Z-op-stream.md). The output rules below describe the decoder side; the encoder pin describes the inverse mapping.

### 1. JSON syntax parser

Hand-rolled inside the module. Not `Fable.SimpleJson` (Fable-only – its `Fable.Parsimmon` dependency throws `System.Exception: JS only` under .NET), not `Newtonsoft.Json` (server-only, would break Fable consumers), not `System.Text.Json` (same). The catalog sample's narrow-decoder parser proves the hand-roll works under both runtimes; this module ports the same pattern.

Produces a local-private `Json` DU with the same shape `Fable.SimpleJson` exposes (`JNull | JBool | JNumber | JString | JArray | JObject of Map<string, Json>`) so the per-NodeKind dispatch downstream stays unchanged.

### 2. Object-key order tolerance

The encoder Ordinal-sorts keys; this decoder accepts any source order – the structural shape is what matters, not the bytes. Symmetric: encoder enforces order, decoder accepts any order. Field lookup is via `Map.tryFind`.

### 3. DU-discriminator dispatch

Every DU position on the wire is a JSON object with a `"$type"` discriminator string + the case's payload fields. The decoder reads `$type`, dispatches to the per-case parser, and surfaces an `UNKNOWN_DU_CASE` `DecodeError` for unrecognised discriminators with the expected-shape hint enumerating the valid cases.

The top-level `NodeKind` discriminator gets its own dedicated error code, `WRONG_NODE_KIND`, since the eval gate-1 surface specifically pattern-matches on "AI emitted something other than Layout / Display / Input / Visualisation / Custom".

### 4. Closure-bearing slots → placeholder

Every slot the encoder rendered as `"<closure>"` (per the `12-Z-op-stream.md` "Closures, function values, and other unobservable runtime payloads" rule) decodes to a placeholder closure:
- `Action.Dispatch _` → `Action.Dispatch (box "<closure>")`.
- `Action.Call(endpoint, _)` → `Action.Call(ApiEndpoint endpoint, fun _ -> box "<closure>")`.
- `FormFieldKind.*` `onChange` / `onToggle` → `fun _ -> Action.Chain []`.
- `SelectSpec.OnChange`, `FileUploadSpec.OnSelect`, `TabsSpec.OnSelect` → `fun _ -> Action.Chain []`.
- `CellKindErased.*` `onEdit` / `onToggle` / `onClick` → `fun _ -> Action.Chain []`.
- `GridSpec.OnRowClick`, `ChartSpec.OnPointClick`, `TableSpec.OnRowClick`, `MapSpec.OnMarkerClick` → `Some (fun _ -> Action.Chain [])` (when present).
- `Binding.Query` / `Binding.Selection` accessors → `fun _ -> placeholder` where `placeholder` is the slot-typed zero (0 / 0.0 / "" / Seq.empty / etc.).
- `Binding.Computed` → `fun _ -> placeholder`.
- `Column.Value` → `fun _ -> CellValue.NoValue`.
- `GridSpec.RowKey` → `fun _ -> "<closure>"`.
- `StateBehaviour.OnError` (encoder writes `"<closure>"` sentinel for the whole `ErrorPayload -> Node` callback) → `Some (fun _ -> placeholderClosureNode)` where `placeholderClosureNode` is a minimal markdown carrying the `"<closure>"` sentinel.

The orchestrator's typed re-attachment happens downstream via `moduleMsgDecoder: JsonValue -> 'Msg`. The decoder is structural; type-recovery is the orchestrator's responsibility.

### 5. `"<opaque>"` sentinels → `box "<opaque>"`

Symmetric: `Binding.Static` slots whose typed value the encoder couldn't decompose render as `"<opaque>"`; the decoder passes the sentinel through as `box "<opaque>"`. The decoder MUST NOT attempt to reconstruct the original CLR type – it doesn't have the schema knowledge.

**F# 10 representation gotcha (load-bearing).** The encoder's `appendObj` only recognises a fixed set of primitives (string / bool / int / int64 / float / float32 / DateTimeOffset / DateTime); lists / seqs / options / records collapse to `"<opaque>"`. The decoder's typed binding parsers accept the sentinel and substitute a non-null placeholder collection so the round-trip stays clean. Naïve placeholders (`[]` for SelectOption list, `None` for `string option`) box to **`null`**, which the encoder writes as JSON `null` – NOT `"<opaque>"`. Use:
- `Binding<SelectOption list>` opaque path → `[ { Value = "<opaque>"; Label = TextSource.Literal "<opaque>" } ]`.
- `Binding<string option>` opaque path → `Some "<opaque>"`.
- `Binding<float seq>` / `Binding<obj seq>` / `Binding<MapMarker seq>` opaque path → `Seq.empty` (which IS a real reference, not null – F# `Seq.empty` is a generator object).

These re-encode through `appendObj` as the catch-all `"<opaque>"` sentinel, preserving `encode(decode(encode(x))) = encode(x)`.

### 6. Number-edge handling

The encoder writes `NaN` / `+∞` / `-∞` as the string sentinels `"NaN"` / `"Infinity"` / `"-Infinity"` (RFC 8259 forbids these as bare numbers). The decoder accepts both forms at float slots:
- `JNumber n` → `Ok n`.
- `JString "NaN"` → `Ok Double.NaN`.
- `JString "Infinity"` → `Ok Double.PositiveInfinity`.
- `JString "-Infinity"` → `Ok Double.NegativeInfinity`.

Integers vs floats: Fable.SimpleJson and the hand-rolled parser both surface every number as `JNumber float`. Integer slots truncate via `int`; round-trip is exact for the int53 range (any 32-bit int).

### 7. NodeId / DecodeError invariants

- `"id"` field present but empty string → `EMPTY_NODE_ID` `DecodeError`. Same defect [`PreEmitValidate.EmptyNodeId`](../../src/Fuaran.UI/PreEmitValidate.fs) catches downstream after apply; surfacing it at decode time saves the round-trip.
- `"id"` field absent → `MISSING_FIELD`.
- `"id"` field wrong JSON kind (e.g. number) → `WRONG_TYPE`.

### 8. Storage-shape erasure

The return type is `Node<obj>` / `TreeOp<obj>`. The wire form has no typed-`'Msg` information to recover – the encoder rendered every `'Msg` payload as `"<closure>"`. Typed callers (the downstream AI consumer, per-module dispatch in `12.E`) re-attach a real `'Msg` downstream via their `moduleMsgDecoder`.

## Encoder gaps (tracked follow-ups)

The encoder doesn't currently emit every type-contract field. The decoder uses defaults for these and the round-trip suite stays clean; fixing them is an encoder-side TIDY-UP candidate:
- `TabsSpec.ActiveIndex` (the typed `Binding<int>` driving the active tab – added as a Phase 12 pilot-app follow-on).
- `TabsSpec.OnSelect` (the `int -> Action<'Msg>` dispatch – same phase).
- `ButtonSpec.Tooltip` (the optional native-title `TextSource` – same phase).
- `ChartSpec.Stacked` (the `bool` enabling AG Charts stacked-series – same phase).

These exist on the type contract today but the encoder predates them and was not updated in the same commit as the type-contract addition. The decoder substitutes the defaults from `Defaults.X` for each. When the encoder catches up, the decoder updates in the same commit per the forward-coupling rule below.

## Node.Motion / Node.ExtraAttributes are wire-omitted by design

Per [`Types.fs`](../../src/Fuaran.UI/Types.fs) lines 213-218, `Node.ExtraAttributes` is "AI-opaque consumer-side hatch … the §4d JSON wire shape omits it on emit". `Node.Motion` follows the same convention – motion is consumer-authored, not AI-authored. The encoder doesn't emit either field; the decoder always sets them to `None`.

## Forward-coupling rule (load-bearing)

Adding a new `NodeKind` / `Spec` / `TreeOp` case in any future phase MUST update the decoder in the **same commit** + the `wire-format-fixtures/` corpus update + (once Wave 9 ships) TS encoder/decoder bump. A missing case becomes an `UNKNOWN_DU_CASE` defect at runtime rather than a compile-time error (the decoder consumes JSON, not the F# DU). This is the same forward-coupling [`CanonicalJson.fs`](../../src/Fuaran.UI.OpStream.Abstractions/CanonicalJson.fs) already carries; both move together for hash-chain integrity.

The acceptance test `Fuaran.UI.JsonDecode.Tests.Fixtures.allNodes` + `allOps` is the canonical enumeration – every shipped case has a fixture, and the round-trip suite tests each fixture exactly once. A new case lands a new fixture in the same commit.

> **Phase 73 amendment (the one allowable edit to this frozen ship record).** The wire format is now specified language-neutrally in [`WIRE_FORMAT.md`](../WIRE_FORMAT.md), and the fixtures are relocated to the workspace-root [`wire-format-fixtures/`](../../../wire-format-fixtures/) corpus. The forward-coupling rule above is therefore extended: a new case lands the encoder + decoder edit **and** a new fixture in [`Fixtures.fs`](../../src/Fuaran.UI.JsonDecode.Tests/Fixtures.fs) (or `RejectFixtures.fs`) **and** a corpus regeneration (`dotnet run --project src/Fuaran.UI.JsonDecode.Tests -- --emit-corpus <workspace-root>/wire-format-fixtures`) **and** – once Wave 9 ships – a matching TS encoder/decoder bump, all in one commit. The live source of this rule is [`WIRE_FORMAT.md`](../WIRE_FORMAT.md) §11; this note records the extension at the ship-record site so the two stay in lockstep.

## V1 limitations (recap)

- Decoded closures are placeholders (per (4) above). Typed re-attachment is `moduleMsgDecoder`'s job.
- `Binding.Static` values whose typed value the encoder couldn't decompose (lists / options / seqs / records) lose their original typed content (per (5) above). The orchestrator's per-app schema re-hydrates.
- Encoder gaps from "Encoder gaps" above leave the corresponding fields at their `Defaults.X` value on round-trip.
- `AriaRole.Custom` and the named `AriaRole.*` cases all encode as the raw string form (e.g. `"button"`); the decoder can't tell `AriaRole.Custom "button"` from `AriaRole.Button` and prefers the named case. v1 limitation; encoder-side discriminator tagging would close it.

## Verification steps

After landing 12.E.0:

1. **`dotnet build src/Fuaran.UI.Ops/Fuaran.UI.Ops.fsproj -c Release`** clean – `TreatWarningsAsErrors=true` catches any nullness leak past the obj-erasure seam.
2. **`dotnet fantomas src/Fuaran.UI.Ops/JsonDecode.fs`** clean – no formatting drift.
3. **`dotnet run --project src/Fuaran.UI.JsonDecode.Tests/Fuaran.UI.JsonDecode.Tests.fsproj -c Release`** – the 69-test acceptance set passes (30 Node round-trips + 10 TreeOp round-trips + 29 reject-fixture assertions across all six `DecodeError` codes).
4. **`dotnet run --project Build.fsproj -- All`** – the new project is wired into the `Test` target; the full pipeline passes alongside existing suites.

## Sibling-tool sanity checks (NOT needed)

- **`Fuaran.UI.Validator` manifest update.** The phase opener listed this as a task, but no manifest change is required: the validator's per-module manifest (per [`docs/VALIDATOR-MANIFEST.md`](../VALIDATOR-MANIFEST.md)) tracks per-tree `queries` / `msgCases` / `queryRowTypes`. JsonDecode adds no new query name or `Msg` case – it's a read-side surface, not a tree-construction one. The validator's AST walker pattern-matches on `Fuaran.X` / `binding.query` / `Action.dispatch` call shapes; it doesn't track API surfaces by symbol.

## Rollback

`Fuaran.UI.Ops.JsonDecode` is purely additive – no existing source file changes shape. Removing the `<Compile Include="JsonDecode.fs" />` from `Fuaran.UI.Ops.fsproj`, deleting `JsonDecode.fs`, and removing the test project (`src/Fuaran.UI.JsonDecode.Tests/`) + its `Build.fs` + `Fuaran.sln` entries backs out 12.E.0 cleanly. Consumer code that imports `Fuaran.UI.Ops.JsonDecode` would need to be edited or stubbed.

## See also

- [`12-Z-op-stream.md`](12-Z-op-stream.md) – the encoder pin this decoder mirrors. Algorithm changes are joint (encoder + decoder bump together; symmetry is load-bearing for hash-chain integrity).
- [`Fuaran.UI.Ops.ErrorRender`](../../src/Fuaran.UI.Ops/ErrorRender.fs) – the §4d AI-recovery JSON envelope vocabulary `DecodeError` mirrors.
- [`STABILITY.md`](../../STABILITY.md) – `Fuaran.UI.Ops` stable surfaces (post-12.E.0 includes the JsonDecode entry).
- [`samples/catalog/JsonDecode.fs`](../../samples/catalog/JsonDecode.fs) – the catalog's narrow stand-in. Retires once the catalog imports `Fuaran.UI.Ops.JsonDecode`.
- [`AI_AUTHORING_GUIDE.md`](../AI_AUTHORING_GUIDE.md) "Self-checking before you emit" – the encoder-side pre-emit gate. The decoder is the wire-side inverse.
