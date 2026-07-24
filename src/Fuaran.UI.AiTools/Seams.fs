module Fuaran.UI.AiTools.Seams

// ============================================================================
//  Fuaran runtime-introspection AI tools — injectable read-only seams.
//
//  The §4i introspection tools observe state the renderer holds but the
//  typed-tree library doesn't (DOM-rooted geometry, runtime-error stream,
//  per-node currentState classification). Each is a narrow interface the
//  consumer wires from its own renderer / orchestrator side:
//
//   - `IGeometryProbe`   — browser-side bridge reads `getBoundingClientRect`
//                          + overflow-check on a per-`NodeId` ref registry.
//   - `IRuntimeErrorSink`— renderer writes binding-resolution failures,
//                          unwired-action warnings; orchestrator drains.
//   - `ICurrentStateProbe`— renderer classifies a node into
//                          Normal/Loading/Empty/Error based on the binding
//                          resolution + StateBehaviour slot the renderer
//                          actually chose to render.
//   - `IIntrospectionClock`— deterministic timestamp source for ErrorEntry
//                          `RecordedAt` and any other time-sensitive field.
//                          Default seam returns a fixed epoch so byte-stable
//                          snapshots are possible across runs.
//
//  GP 12 audit (six portability rules — applied to IGeometryProbe as the
//  representative new interface):
//   (1) Lives in its own file ✓ (this one).
//   (2) Pure interface, no implementation default mixed in ✓
//       (`NoGeometryProbe` is a separate concrete record below).
//   (3) Async-friendly — all members synchronous + side-effect free
//       (the consumer's implementation is free to cache eagerly).
//   (4) No 'Msg leak — the seam is 'Msg-agnostic, observed-state only.
//   (5) Default implementation supplied (`NoGeometryProbe`).
//   (6) No transport dependency — pure F# interfaces, Fable-compatible.
//
//  FGP 4 (Diagnostics under both pipelines): `Seams.fs` itself contains no
//  diagnostics writes — the `IRuntimeErrorSink` interface IS the
//  diagnostics seam this layer integrates with. The renderer's
//  centralised diagnostics module writes here on the renderer side; the
//  orchestrator drains here on the server side. Single seam, both
//  pipelines.
// ============================================================================

open System
open Fuaran.UI.Types
open Fuaran.UI.AiTools.Types

// ─── Geometry probe ────────────────────────────────────────────────────────

/// Returns the rendered geometry for a node, when the renderer has it
/// recorded. Implementations bridge to React refs / `getBoundingClientRect`
/// browser-side; the default returns `None` for every node.
type IGeometryProbe =
    abstract TryGetGeometry: nodeId: NodeId -> Geometry option

/// Probe that knows nothing about geometry — every call returns `None`.
/// Suitable for the .NET test runner and the orchestrator's pre-mount
/// phase (no renderer wired yet). `getRenderedDom` against this probe
/// returns a GeometryTree with every node's `Geometry = None` —
/// structurally complete but with no DOM-side data.
type NoGeometryProbe() =
    interface IGeometryProbe with
        member _.TryGetGeometry(_) = None

/// Shared no-op probe instance.
let noGeometry: IGeometryProbe = NoGeometryProbe() :> IGeometryProbe

// ─── Current-state probe ───────────────────────────────────────────────────

/// Returns the renderer's classification of a node's current state
/// (Normal/Loading/Empty/Error) plus an optional detail blob. The
/// renderer chooses one of OnLoading / OnEmpty / OnError / the main body
/// per data-bound component; the probe surfaces that choice. Default
/// returns `None` for every node — fields the orchestrator must observe
/// via getNodeState's `currentState` block will be omitted.
type ICurrentStateProbe =
    abstract TryGetCurrentState: nodeId: NodeId -> (CurrentState * StateDetail option) option

type NoCurrentStateProbe() =
    interface ICurrentStateProbe with
        member _.TryGetCurrentState(_) = None

let noCurrentState: ICurrentStateProbe = NoCurrentStateProbe() :> ICurrentStateProbe

// ─── Runtime-error sink ────────────────────────────────────────────────────

/// Bidirectional channel for runtime errors. Renderer (or anyone) calls
/// `Record` to append; introspection callers call `Drain` to read since
/// a watermark. The orchestrator may pass `sinceTurn = None` to drain
/// every entry (typical at session start), or `Some n` to filter to
/// strictly-newer entries (typical iteration-to-iteration).
type IRuntimeErrorSink =
    abstract Record: entry: ErrorEntry -> unit
    abstract Drain: sinceTurn: int option -> ErrorEntry list
    /// True when the sink was cleared by the consumer (typically on
    /// `beginDebugSession`). Implementations that don't differentiate
    /// session boundaries return `false` always.
    abstract Cleared: bool

/// In-memory FIFO sink — thread-safe append + snapshot drain. Default
/// implementation for tests + the orchestrator's pre-renderer-mount
/// phase. Production consumers (browser renderer with telemetry,
/// orchestrator with per-turn ring buffer) replace it with their own
/// implementation.
type InMemoryRuntimeErrorSink() =
    let entries = System.Collections.Generic.List<ErrorEntry>()
    let mutable cleared = false
    let sync = obj ()

    interface IRuntimeErrorSink with
        member _.Record(entry) = lock sync (fun () -> entries.Add entry)

        member _.Drain(sinceTurn) =
            lock sync (fun () ->
                entries
                |> Seq.filter (fun e ->
                    match sinceTurn with
                    | None -> true
                    | Some t ->
                        match e.TurnId with
                        | Some entryTurn -> entryTurn > t
                        | None -> false)
                |> List.ofSeq)

        member _.Cleared = cleared

    /// Drop every entry. The orchestrator calls this on
    /// `beginDebugSession` to start each session with a clean slate.
    member _.Clear() =
        lock sync (fun () ->
            entries.Clear()
            cleared <- true)

let createInMemorySink () : IRuntimeErrorSink =
    InMemoryRuntimeErrorSink() :> IRuntimeErrorSink

// ─── Clock ─────────────────────────────────────────────────────────────────

/// Time source for ErrorEntry `RecordedAt` and other time-sensitive
/// fields. The default returns a fixed epoch so snapshot tests are
/// byte-stable across runs; production wires `SystemClock`; debug
/// sessions wire a `SeededClock` per §4i determinism contract.
type IIntrospectionClock =
    abstract Now: unit -> DateTimeOffset

type FixedClock(epoch: DateTimeOffset) =
    interface IIntrospectionClock with
        member _.Now() = epoch

/// The fixed-clock epoch used by default — 2026-01-01T00:00:00Z, a stable
/// reference point. The orchestrator overrides this in production.
let defaultEpoch = DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)

let fixedClock: IIntrospectionClock =
    FixedClock(defaultEpoch) :> IIntrospectionClock

type SystemClock() =
    interface IIntrospectionClock with
        member _.Now() = DateTimeOffset.UtcNow

let systemClock: IIntrospectionClock = SystemClock() :> IIntrospectionClock

// ─── Bundled introspection context ─────────────────────────────────────────

/// Bundle the four seams + the binding-source data the tools need. The
/// orchestrator constructs this once per debug-session iteration; the
/// tool functions take this as their first argument so the substrate is
/// explicit at the call site (no global mutable state in this package,
/// FGP 4 + portability rule 5).
///
/// `Sources` mirrors the renderer's `BindingResolver.BindingSources`
/// shape — duplicated rather than referenced because `Fuaran.UI.AiTools`
/// stays free of `Fuaran.UI.Renderer` (the renderer depends on Feliz +
/// Fable; AiTools targets .NET-side orchestrator consumption). The
/// duplication is flagged for TIDY-UP — consolidating `BindingSources`
/// + the resolver into `Fuaran.UI` proper breaks the cycle without
/// pulling in Renderer.
type IntrospectionContext =
    { Sources: BindingProbeSources
      Geometry: IGeometryProbe
      CurrentState: ICurrentStateProbe
      Errors: IRuntimeErrorSink
      Clock: IIntrospectionClock }

/// Forward-declared placeholder; the real shape lives in `BindingProbe.fs`
/// (open recursion isn't available across .fs files in F#). The
/// `IntrospectionContext` record references this name; `BindingProbe.fs`
/// defines the body.
and BindingProbeSources =
    { QueryResults: Map<string, obj>
      State: Map<string, obj>
      Filters: Map<string, obj>
      Selections: Map<NodeId, obj>
      I18n: Map<string, string> }

/// Empty / default context — useful for tests that exercise the
/// tree-shape introspection without wiring any external state.
let emptyContext: IntrospectionContext =
    { Sources =
        { QueryResults = Map.empty
          State = Map.empty
          Filters = Map.empty
          Selections = Map.empty
          I18n = Map.empty }
      Geometry = noGeometry
      CurrentState = noCurrentState
      Errors = createInMemorySink ()
      Clock = fixedClock }
