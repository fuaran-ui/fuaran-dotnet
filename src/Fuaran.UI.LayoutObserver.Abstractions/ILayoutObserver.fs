namespace Fuaran.UI.LayoutObserver

// ─── ILayoutObserver — the observer contract ────────────────────
//
// The seam the renderer's mount hook + the
// `_platform.ui.inspect_layout` AI tool consume. Two concrete
// implementations ship in `Fuaran.UI.LayoutObserver`:
//   - `BrowserLayoutObserver` (Fable) — wires `ResizeObserver` to
//     each Register'd element, rAF-debounces, runs the shared
//     flag-derivation logic, emits to subscribers.
//   - `InMemoryLayoutObserver` (pure .NET) — accepts hand-authored
//     fixtures, drives Expecto tests + the future eval gate.
//
// **`Register` takes `element: obj`.** Browser-side, the renderer
// passes the `Browser.Types.Element`; the in-memory observer ignores
// it. Keeping the abstractions Fable-free (no `Browser.*` dependency)
// is load-bearing — the test project references this assembly and
// runs under pure .NET. Concrete observers cast at the boundary.
//
// **`Subscribe` returns IDisposable.** Hosts call `Subscribe` once
// per AIInspectTool invocation that wants a live stream, or hook
// the disposable lifecycle into a component-unmount path; calling
// `Dispose()` removes the handler without affecting other
// subscribers. Multiple subscribers are supported (the demo Layout
// Inspector panel subscribes alongside the AIInspectTool's
// per-invocation snapshot path).

/// The observer contract. Three reads (single-node, tree, raw
/// subscription) + two registration calls the renderer drives on
/// element mount/unmount. Implementations are expected to be
/// thread-safe for `Observe` / `ObserveTree` (read-side) — the
/// browser implementation's writes happen on the rAF tick and
/// snapshot under a small lock; the in-memory implementation
/// mutates only via test-driven `Register` calls.
type ILayoutObserver =
    /// Snapshot the observation for a single registered node.
    /// `None` if the node is not currently registered (e.g. it was
    /// unmounted, or never registered to begin with). Returns the
    /// most recent observation; the browser implementation drives
    /// updates on the rAF tick following any `ResizeObserver`
    /// notification.
    abstract Observe: nodeId: string -> LayoutObservation option

    /// Snapshot every observation reachable from `rootNodeId`,
    /// including the root itself. The "tree" walk relies on the
    /// renderer's NodeId-as-DOM-id convention — the browser
    /// implementation queries `document.getElementById(rootNodeId)`
    /// and walks descendants; the in-memory implementation walks
    /// the fixture's parent-NodeId graph. Empty list when the root
    /// is not registered.
    abstract ObserveTree: rootNodeId: string -> LayoutObservation list

    /// Subscribe to live observation deltas. The handler is invoked
    /// per the configured debounce policy with the (nodeId,
    /// observation) tuple for each node whose flag set changed
    /// since the previous emission (when `EmitOnFlagChangeOnly =
    /// true`, which is the v1 default) or for every observation
    /// (when false). Dispose the returned `IDisposable` to remove
    /// the handler.
    abstract Subscribe: handler: (string * LayoutObservation -> unit) -> System.IDisposable

    /// Register a node for observation. Browser-side the renderer
    /// passes the live `Browser.Types.Element`; in-memory observers
    /// ignore the `element` arg. Idempotent — re-registering an
    /// existing NodeId is a no-op (the existing observation stays
    /// current; the registry doesn't duplicate `ResizeObserver`
    /// entries).
    abstract Register: nodeId: string * element: obj -> unit

    /// Unregister a node. Browser-side this disconnects the
    /// `ResizeObserver` entry + drops the registry record;
    /// in-memory observers remove the fixture entry. Idempotent —
    /// unregistering an unknown NodeId is a no-op.
    abstract Unregister: nodeId: string -> unit
