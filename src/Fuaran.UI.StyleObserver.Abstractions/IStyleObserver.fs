namespace Fuaran.UI.StyleObserver

// ─── IStyleObserver — the observer contract ─────────────────────
//
// The seam the renderer's mount hook + the
// `_platform.ui.inspect_style` AI tool consume — the resolved-style
// twin of `ILayoutObserver`. Two concrete implementations ship in
// `Fuaran.UI.StyleObserver`:
//   - `BrowserStyleObserver` (Fable) — reads `getComputedStyle` per
//     Register'd element, climbs ancestors to composite the effective
//     background, rAF-debounces, runs the shared flag-derivation
//     logic, emits to subscribers.
//   - `InMemoryStyleObserver` (pure .NET) — accepts hand-authored
//     fixtures, drives Expecto tests + the future eval gate.
//
// **`Register` takes `element: obj`.** Browser-side, the renderer
// passes the `Browser.Types.Element`; the in-memory observer ignores
// it. Keeping the abstractions Fable-free (no `Browser.*` dependency)
// is load-bearing — the test project references this assembly and
// runs under pure .NET. Concrete observers cast at the boundary
// (identical posture to `ILayoutObserver`).
//
// **`Subscribe` returns IDisposable.** Hosts call `Subscribe` once
// per AI-tool invocation that wants a live stream, or hook the
// disposable lifecycle into a component-unmount path; calling
// `Dispose()` removes the handler without affecting other
// subscribers. Multiple subscribers are supported.

/// The observer contract. Three reads (single-node, tree, raw
/// subscription) + two registration calls the renderer drives on
/// element mount/unmount. The five-method shape is identical to
/// `ILayoutObserver` so a host wires both observers the same way.
/// Implementations are expected to be thread-safe for `Observe` /
/// `ObserveTree` (read-side).
type IStyleObserver =
    /// Snapshot the observation for a single registered node. `None`
    /// if the node is not currently registered (e.g. it was
    /// unmounted, or never registered to begin with). Returns the
    /// most recent observation; the browser implementation drives
    /// updates on the rAF tick following any style mutation.
    abstract Observe: nodeId: string -> StyleObservation option

    /// Snapshot every observation reachable from `rootNodeId`,
    /// including the root itself. The browser implementation queries
    /// `document.getElementById(rootNodeId)` and walks descendants
    /// carrying `data-fuaran-node-id`; the in-memory implementation
    /// walks the fixture's parent-NodeId graph. Empty list when the
    /// root is not registered.
    abstract ObserveTree: rootNodeId: string -> StyleObservation list

    /// Subscribe to live observation deltas. The handler is invoked
    /// per the configured debounce policy with the (nodeId,
    /// observation) tuple for each node whose flag set changed since
    /// the previous emission (when `EmitOnFlagChangeOnly = true`, the
    /// v1 default) or for every observation (when false). Dispose the
    /// returned `IDisposable` to remove the handler.
    abstract Subscribe: handler: (string * StyleObservation -> unit) -> System.IDisposable

    /// Register a node for observation. Browser-side the renderer
    /// passes the live `Browser.Types.Element`; in-memory observers
    /// ignore the `element` arg. Idempotent — re-registering an
    /// existing NodeId is a no-op.
    abstract Register: nodeId: string * element: obj -> unit

    /// Unregister a node. Browser-side this drops the registry record
    /// (and any per-element observation entry); in-memory observers
    /// remove the fixture entry. Idempotent — unregistering an unknown
    /// NodeId is a no-op.
    abstract Unregister: nodeId: string -> unit
