# Phase 12.Z – Op-stream persistence + replay

Ships [`Fuaran.UI.OpStream.Abstractions`](../../src/Fuaran.UI.OpStream.Abstractions/) + [`Fuaran.UI.OpStream.InMemory`](../../src/Fuaran.UI.OpStream.InMemory/) + [`Fuaran.UI.OpStream.Sqlite`](../../src/Fuaran.UI.OpStream.Sqlite/) + [`Fuaran.UI.OpStream.Replay`](../../src/Fuaran.UI.OpStream.Replay/) – a durable, hash-chained record of every op the apply engine has applied, plus a replay engine that reconstructs any tree state by walking the records.

§4f of `FUARAN-UI-LANGUAGE.md` frames "the conversation IS the source of truth". Phase 12 session 4+ shipped the apply engine but had nowhere to persist the apply trace. 12.Z makes the §4f wedge real and unblocks:
- The downstream AI-driving-the-UI consumer (a private host integration, shipped separately) – needs replay-from-known-state + diff to learn.
- AI-emission micro-eval (Phase 12.E) – needs deterministic failure reproduction.
- Op-apply telemetry (Phase 12.T) – telemetry aggregates atop a persistent trace, not a point-in-time one.

A second consumer rides on the canonical-JSON encoder this phase pins: the AI pre-emit self-check pattern surfaced in [`docs/AI_AUTHORING_GUIDE.md`](../AI_AUTHORING_GUIDE.md) "Self-checking before you emit" calls `CanonicalJson.encodeNode tree |> ArgsJsonContract.validate` to catch wire-shape violations cheaper than the apply-engine envelope. Same encoder, two consumers – drift here desynchronises them silently, so the algorithm is pinned below before any implementation code.

## The canonical-JSON algorithm (pinned)

The encoder produces a STABLE string for any value such that two inputs that are structurally equal (modulo closures) produce byte-for-byte identical output across .NET / Fable / process restarts / machines. This is load-bearing for hash-chain integrity AND for the AI pre-emit gate's wire-shape comparison.

### Output rules

1. **UTF-8 source, ASCII output for control chars.** The encoder emits ASCII for the JSON structural punctuation; non-ASCII characters in strings pass through as their UTF-8 sequence (no `\uXXXX` escaping for non-control characters – adding it would inflate output and confuse the wire-shape gate, which validates UTF-8 strings).

2. **Object keys are sorted alphabetically by Ordinal comparison.** `StringComparer.Ordinal` (NOT `OrdinalIgnoreCase`, NOT culture-aware). Deterministic across cultures; Fable-compatible. Empty objects render as `{}`.

3. **Lists / arrays preserve source order.** A list is an *ordered* structure; reordering would change tree semantics (sibling order matters for layout children). Empty arrays render as `[]`.

4. **`None` / null fields are EXCLUDED from object output.** `option None` does NOT render as `"key":null`; the key is omitted entirely. `Some x` renders the unwrapped `x` under the key. This keeps emissions minimal and matches the `Defaults.X` override discipline in [`AI_AUTHORING_GUIDE.md`](../AI_AUTHORING_GUIDE.md) section 5.

5. **Numbers.** Integers render as their decimal representation with no leading zeroes, no decimal point, no exponent (`42`, `-7`, `0`). Floats render via `System.Double.ToString("R", CultureInfo.InvariantCulture)` – the round-trip format that guarantees parse(toString(x)) = x for finite doubles. Special values: `NaN`, `+∞`, `-∞` render as the string `"NaN"`, `"Infinity"`, `"-Infinity"` (in quotes – RFC 8259 forbids these as bare numbers, and the wire shape must remain parseable). Negative-zero collapses to positive-zero (`0`, not `-0`).

6. **Strings.** Quoted; the following escapes apply:
   - `"` → `\"`
   - `\` → `\\`
   - control chars (`U+0000`..`U+001F`) → `\uXXXX` (lower-case hex, four digits)
   - everything else passes through literally – including `/`, which is NOT escaped (RFC 8259 permits `\/` but does not require it; we choose the un-escaped form for canonicalisation).

7. **Booleans.** `true` / `false`.

8. **DU cases.** Render as an object with a `"$type"` discriminator first (alphabetically before any data field), then the case's payload fields sorted Ordinal. The discriminator value is the case's short name (`"Static"`, `"Query"`, `"EditNode"`, etc. – NOT fully qualified). Example: `Binding.Static 42.0` → `{"$type":"Static","value":42}`.

9. **Tuples.** Render as positional arrays (`(1, "x")` → `[1,"x"]`). Avoids inventing keys.

10. **Closures, function values, and other unobservable runtime payloads.** Render as the sentinel string `"<closure>"`. This is the v1 limitation called out below.

11. **`obj`-typed values.** Best-effort: if the runtime type matches one of the recognised JSON primitives (string, bool, integer, float, list, tuple, F# record, `option`, F# DU), encode that. Otherwise render as `"<opaque>"`. The encoder does NOT use reflection for arbitrary CLR objects.

### V1 limitation – closure-bearing payloads hash equivalently

The typed `TreeOp<'Msg>` carries closures in several places: `Action.Dispatch 'Msg` (the `'Msg` value itself is opaque from the encoder's perspective when boxed), `Action.Call`'s `onResult: obj -> 'Msg`, `Binding.Query`'s `accessor: obj -> 'T`, every `FormFieldKind.*` `onChange` callback, every `CellKindErased.*` action handler, `Column.Value: obj -> CellValue`, etc. All of these render as `"<closure>"`.

The consequence: two ops that differ ONLY in their closure / `'Msg` payload (e.g. `Action.Dispatch (SelectRow 1)` vs `Action.Dispatch (SelectRow 2)`) hash IDENTICALLY in v1. The hash chain still detects structural tamper – changing op kind, NodeId, slot name, fixed-value fields, the tree's structural shape – but does not detect tamper purely inside an opaque `'Msg` payload.

This is the same defect §4g flags around the typed-vs-storage shape distinction:

> The *apply* shape this engine ships is the typed in-memory one – a downstream AI consumer hands the engine a typed `Node<'Msg>` and gets back a typed `Node<'Msg>` or a structured error, with no decoder round-trip in the hot path. The storage layer (`Node<obj>` + `moduleMsgDecoder: JsonValue -> 'Msg`) lands with the per-turn JSONL persistence substrate – explicitly out of v1 scope.

Phase 12.Z ships against the typed shape and accepts the closure-sentinel canonicalisation as v1 behaviour. The follow-on storage-shape phase (post-Phase 12 session 4+) replaces the sentinels with structured payloads emitted from the `moduleMsgDecoder`, which strengthens hash-chain coverage to the leaves. The OpRecord schema and hash-chain rule do NOT change at that follow-on – only the encoder's treatment of the now-structured payloads.

## OpRecord shape

```fsharp
type OpResultEnvelope =
    /// The apply engine returned Ok and the sink recorded the post-apply state.
    | Success
    /// Reserved for the future apply-failure-also-recorded variant. Phase 12.Z
    /// v1 only records successful applies (the apply-engine integration in
    /// Apply.fs fires sink.Append AFTER the Result.Ok branch); the case is
    /// kept on the type so callers don't need to grow it in a later breaking
    /// change.
    | Failure of code: string * message: string

type OpRecord<'Msg> =
    { StreamId: string
      Sequence: int
      PreviousHash: string
      Hash: string
      Op: TreeOp<'Msg>
      PromptId: string option
      UserId: string
      Timestamp: System.DateTimeOffset
      ResultEnvelope: OpResultEnvelope }
```

`OpRecord` is parameterised on `'Msg` to match the typed apply engine. The generic parameter flows through `IOpStreamSink<'Msg>` – a sink instance is bound to one `'Msg` type per app, the way Elmish dispatch is. Hosts that pin to one app's `'Msg` get full type safety; hosts that bridge multiple apps use one sink per app or erase to `obj` at the registration seam.

## Hash chain rule

```
Hash[0]  = SHA-256("00...0" (64 chars) ++ canonical-json(Op[0]) ++ "0"        ++ unix-seconds(Timestamp[0]))
Hash[n]  = SHA-256(Hash[n-1]           ++ canonical-json(Op[n]) ++ string(n)  ++ unix-seconds(Timestamp[n]))
```

Where:
- `++` is byte concatenation of the UTF-8 encoding of each string.
- `00...0 (64 chars)` is the genesis `PreviousHash` – sixty-four `'0'` characters. Equal to `String.replicate 64 "0"`.
- `Sequence` renders as its decimal `string` form, no padding.
- `unix-seconds(t)` is `t.ToUnixTimeSeconds() |> string` – second-precision (NOT millisecond) so the rule remains stable across the .NET wire and any future Postgres / Kafka companion sink.
- The output of SHA-256 is rendered as 64 lower-case hex characters.

Verification re-derives `Hash[n]` from the stored `PreviousHash` / `Op` / `Sequence` / `Timestamp` and compares; AND asserts `record[n].PreviousHash = record[n-1].Hash`. Either mismatch surfaces `VerificationError`.

## SQL schema (SqliteSink)

```sql
CREATE TABLE IF NOT EXISTS op_stream (
    stream_id            TEXT    NOT NULL,
    sequence             INTEGER NOT NULL,
    previous_hash        TEXT    NOT NULL,
    hash                 TEXT    NOT NULL,
    op_json              TEXT    NOT NULL,
    prompt_id            TEXT    NULL,
    user_id              TEXT    NOT NULL,
    timestamp            INTEGER NOT NULL,
    result_envelope_json TEXT    NOT NULL,
    PRIMARY KEY (stream_id, sequence)
);
```

The composite primary key `(stream_id, sequence)` is the index. `Append` is a single `INSERT` (rolls back atomically on key collision – duplicate sequence is a structural defect, not a recoverable condition); `Replay(streamId, from, to)` is a single `SELECT ... WHERE stream_id = @s AND sequence BETWEEN @f AND @t ORDER BY sequence` that walks the index. `LatestSequence` is `SELECT MAX(sequence) ...`. `Streams` is `SELECT DISTINCT stream_id ...`.

`op_json` stores the host-provided JSON encoding of `Op` – see "Sink codec contract" below for why the sink takes an `IOpJsonCodec<'Msg>` parameter rather than using `CanonicalJson.encodeOp` directly. `result_envelope_json` is encoded by the abstractions package (`OpResultEnvelope` is closed and structural, no codec needed).

## Sink codec contract

The Sqlite sink persists `Op` as JSON text and must read it back as a typed `TreeOp<'Msg>` for `Replay`. Per the v1 limitation above, closure-bearing payloads cannot round-trip – there's no general decoder from `"<closure>"` back to a function value. The sink therefore takes an `IOpJsonCodec<'Msg>` from the host:

```fsharp
type IOpJsonCodec<'Msg> =
    abstract member EncodeOp: TreeOp<'Msg> -> string
    abstract member DecodeOp: string -> Result<TreeOp<'Msg>, string>
```

The host supplies the codec it owns the `'Msg` shape for. Hosts that only need integrity verification (no read-back) can supply `EncodeOp = CanonicalJson.encodeOp` and a stub `DecodeOp` that always errors; the `Append` + hash-chain verification paths still work, only `Replay` is gated on the codec.

The follow-on storage-shape phase replaces this with the auto-derived `Node<obj>` + `moduleMsgDecoder` pair from §4g; v1 ships with the host-provided codec for tractability.

## PromptId correlation

`OpRecord.PromptId` is opaque to Fuaran – it's a free-form `string option` the host writes when calling `sink.Append`. The convention: the host's AI integration (the side that dispatches the AI-emitted op) writes the conversation's current prompt id. Downstream, `Replay(streamId, ...)` callers filter on `PromptId` to reconstruct "everything the AI did in this conversation" without seeing the operator's manual interleaved ops.

The field is `string option` (not `string`) because non-AI-emitted ops legitimately have no prompt id – operator-initiated ops, the orchestrator's own structural ops, replay-induced ops, etc. The hash-chain canonicalisation excludes `None` per output rule 4, so adding a `PromptId` to a previously-`None` op would change its hash – desired tamper detection.

## Three-stage pre-emit gate (the second consumer)

Once 12.Z ships, AI authors emitting Fuaran trees client-side or server-side can self-check before submission:

```fsharp
open Fuaran.UI
open Fuaran.UI.OpStream.Abstractions
// + the orchestrator tier's abstractions namespace, for ArgsJsonContract

let preEmitCheck (tree: Node<'Msg>) : Result<unit, string> =
    match PreEmitValidate.validate tree with
    | Error defects -> Error (sprintf "tree-shape: %A" defects)
    | Ok () ->
        let canonical = CanonicalJson.encodeNode tree
        match ArgsJsonContract.validate canonical with
        | Error msg -> Error (sprintf "wire-shape: %s" msg)
        | Ok () -> Ok ()
```

Stage 1 (`PreEmitValidate.validate`) catches tree-level invariants (NodeId uniqueness, non-empty Ids). Stage 2 (`CanonicalJson.encodeNode`) produces the canonical JSON. Stage 3 (`ArgsJsonContract.validate`) catches lexical contract violations against the dispatch-path JSON gate. Three cheap stages catch what the wire-side `Apply` envelope would catch more expensively after a round-trip.

The encoder is Fable-compatible (no reflection, no `System.Security.Cryptography`, no closures captured), so Fable-side AI authors call the same surface as server-side ones.

## Apply-engine integration

**Shipped 2026-05-26.** `Fuaran.UI.OpStream.Replay.ApplyPersist.applyAndPersist` is the host-callable entry point; `Fuaran.UI.Ops.Apply.apply` does NOT change shape.

```fsharp
// Public API — Fuaran.UI.OpStream.Replay/ApplyPersist.fs:
namespace Fuaran.UI.OpStream.Replay

type PersistContext =
    { StreamId: string
      UserId: string
      PromptId: string option
      OnSinkError: (exn -> unit) option }

module PersistContext =
    val create        : streamId: string -> userId: string -> PersistContext
    val withPromptId  : promptId: string -> PersistContext -> PersistContext
    val withSinkErrorHook : (exn -> unit) -> PersistContext -> PersistContext

module ApplyPersist =
    val applyAndPersist<'Msg>
        : IOpStreamSink<'Msg> -> PersistContext -> TreeOp<'Msg> -> Node<'Msg>
            -> Async<Result<Node<'Msg>, ApplyError>>
```

Semantics: `applyAndPersist` calls `Fuaran.UI.Ops.Apply.apply op tree`; on `Error`, returns the apply error unchanged and the sink is not touched. On `Ok updated`, queries `sink.LatestSequence` for the next sequence, derives the previous hash (genesis on `sequence = 1`; the immediately-prior record's `Hash` otherwise via `sink.Replay`), computes the new `Hash` via `HashChain.computeHash`, builds an `OpRecord<'Msg>`, and calls `sink.Append`. Sink failures are surfaced via `ctx.OnSinkError` (when set) but do NOT propagate – the apply path returns `Ok updated` regardless. Callers that want strict durability wrap their sink in a synchronous variant that propagates throws.

### Design deviation from the original sketch

The original sketch (kept below for reference) modelled `applyWithSink` as a wrapper that would eventually land inside `Fuaran.UI.Ops.Apply.fs`. The shipped form lives in `Fuaran.UI.OpStream.Replay` instead. The dispatch-point form would have forced `Fuaran.UI.Ops` to depend on `Fuaran.UI.OpStream.Abstractions` (for `IOpStreamSink<'Msg>` + `HashChain.computeHash`), which violates the §4l standalone posture mandate recorded in `Fuaran/CLAUDE.md` (*"`Fuaran.UI.Ops` ... Standalone – depends on `Fuaran.UI` + `FSharp.Core` only"*). Sitting the wrapper in `Fuaran.UI.OpStream.Replay` – which already references both `Fuaran.UI.Ops` and `Fuaran.UI.OpStream.Abstractions` for the read-back replay engine – keeps the dependency direction clean.

Consequence: the Phase 12.T `applyWithTelemetry` wrapper does NOT retire alongside this work. Both wrappers ship as parallel sink-fan-outs; hosts that want both compose them at the call site (or write a small two-sink wrapper inside their own composition root). Future consolidation into a single `applyWithSinks` form is tracked separately if a host asks for it; absent that demand, parallel wrappers are the canonical shape.

Two other small deviations:

- **Sink-error logging is host-supplied**, not routed through the orchestrator tier's `Diagnostics.error` helper. `PersistContext.OnSinkError: (exn -> unit) option` is the seam – hosts wire their own diagnostics. This keeps the `Fuaran.UI.OpStream.Replay` package free of an orchestrator-tier client dependency (the package would have crossed three tiers otherwise).
- **`PersistContext.OnSinkError`'s own exceptions are swallowed** rather than propagated. A buggy logger cannot poison the apply path. The 7-test acceptance set in `Fuaran.UI.OpStream.Tests.ApplyPersistTests` includes a `failwith "buggy hook"` case to pin this behaviour.

### Original sketch (reference; the shipped form above is canonical)

```fsharp
// Host-side wrapper (original sketch — superseded by the shipped form above):
let applyWithSink (sink: IOpStreamSink<'Msg> option) (streamId: string) (userId: string)
                  (promptId: string option) (op: TreeOp<'Msg>) (tree: Node<'Msg>)
                  : Async<Result<Node<'Msg>, ApplyError>> =
    async {
        match Apply.apply op tree with
        | Error e -> return Error e
        | Ok updated ->
            match sink with
            | None -> ()
            | Some s ->
                let! next = s.LatestSequence streamId
                let sequence = next + 1
                let timestamp = DateTimeOffset.UtcNow
                let previousHash =
                    match sequence with
                    | 1 -> HashChain.genesisPreviousHash
                    | _ ->
                        let! prev = s.Replay(streamId, sequence - 1, sequence - 1)
                        prev |> List.head |> fun r -> r.Hash
                let hash = HashChain.computeHash previousHash op sequence timestamp
                let record =
                    { StreamId = streamId
                      Sequence = sequence
                      PreviousHash = previousHash
                      Hash = hash
                      Op = op
                      PromptId = promptId
                      UserId = userId
                      Timestamp = timestamp
                      ResultEnvelope = OpResultEnvelope.Success }
                try
                    do! s.Append record
                with ex ->
                    Diagnostics.error
                        "fuaran.opstream.append"
                        (sprintf "sink.Append failed on stream %s seq %d: %s" streamId sequence ex.Message)
            return Ok updated
    }
```

The shipped form differs structurally – `sink` is not `option` (callers that don't want persistence simply call `Fuaran.UI.Ops.Apply.apply` directly), and sink-error logging is opt-in via `PersistContext.OnSinkError` rather than a hard dependency on `Diagnostics.error` – but the hash-chain derivation, sequence-handling, and best-effort durability semantics are preserved.

## Verification steps

After landing 12.Z:

1. **`dotnet build Fuaran.sln`** clean – `TreatWarningsAsErrors=true` catches any leftover from the wire-in.
2. **`dotnet fantomas .`** clean – no formatting drift in the new files.
3. **`dotnet run --project src/Fuaran.UI.OpStream.Tests -c Release`** – the 25-test acceptance set passes.
4. **`dotnet run --project src/Fuaran.UI.Ops.Tests -c Release`** – no regressions in the apply-engine suite (the apply path itself does not change in this phase).
5. **`dotnet run --project src/Fuaran.UI.Tests -c Release`** + **`dotnet run --project src/Fuaran.UI.AiTools.Tests -c Release`** + **`dotnet run --project src/Fuaran.UI.Validator.Tests -c Release`** – no regressions in any sibling suite.
6. **Round-trip self-test** – the canonical encoder pinned by this document passes `CanonicalJson.encodeNode tree |> ArgsJsonContract.validate` for the §4c canonical seed example (the AI pre-emit consumer's load-bearing assertion).

## Rollback

`Fuaran.UI.OpStream.*` is purely additive – no existing source file changes shape. The apply-engine integration ships as `Fuaran.UI.OpStream.Replay.ApplyPersist.applyAndPersist` (a host-callable wrapper in `Fuaran.UI.OpStream.Replay`); `Fuaran.UI.Ops.Apply.fs` is untouched, so the §4l standalone posture is preserved. Removing the new projects + reverting the integration block backs out 12.Z cleanly. Consumer code that imports `Fuaran.UI.OpStream.*` would need to be edited or stubbed.

## See also

- `FUARAN-UI-LANGUAGE.md` §4f (conversation-as-source-of-truth), §4g (op vocabulary), §4i (wire-expression strings for Binding.Query accessors).
- [`AI_AUTHORING_GUIDE.md`](../AI_AUTHORING_GUIDE.md) "Self-checking before you emit" – the second canonical-encoder consumer.
- [`12-X-posture-reversal.md`](12-X-posture-reversal.md) – abstractions tier licensing posture (`Fuaran.UI.OpStream.Abstractions` inherits the same internal-modularity-seam framing).
- [`12-J-args-json-contract.md`](12-J-args-json-contract.md) – `ArgsJsonContract.validate`, the wire-shape gate that pairs with the encoder.
