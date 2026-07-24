namespace Fuaran.UI.LayoutObserver

// ─── LayoutObservation — one geometric snapshot per NodeId ──────
//
// The per-node payload surfaced by `ILayoutObserver` —
// keyed by NodeId (the same identity the renderer uses for DOM-id
// resolution), carrying a small fixed set of raw metrics plus the
// derived flag list. Stays small on the wire because every AI tool
// result that includes a `Layout` block ships one of these per
// observed node; an `AIStateSnapshot` covering 50 nodes serialises
// in well under 5 KB.
//
// **Raw metrics are exactly four.** `Width`, `Height`, `ViewportX`,
// `ViewportY` cover the AI's primary geometric reasoning surface;
// `ScrollWidth` / `ScrollHeight` are deliberately not separately
// surfaced (overflow is already covered by the flag derivation,
// and exposing raw scroll geometry doubles the wire payload without
// adding interpretation value). If a future phase needs them, the
// record extends additively.
//
// **JSON shape:** camelCase per the existing `_platform.ui.inspect_active_module`
// envelope convention. `flags` is a JSON array of tagged-object flags
// per `LayoutFlag.encode`.

/// One geometric snapshot for a single addressable node, derived
/// from the browser's `getBoundingClientRect` + `getComputedStyle`
/// (browser path) or from a hand-authored fixture (in-memory path).
/// The flag list is the AI-facing interpretation; the raw metrics
/// are the supporting evidence.
type LayoutObservation =
    { NodeId: string
      Width: float
      Height: float
      ViewportX: float
      ViewportY: float
      Flags: LayoutFlag list }

module LayoutObservation =
    /// Construct an observation with an empty flag set — the
    /// observer's flag-derivation logic populates `Flags` post-hoc.
    let withoutFlags
        (nodeId: string)
        (width: float)
        (height: float)
        (viewportX: float)
        (viewportY: float)
        : LayoutObservation =
        { NodeId = nodeId
          Width = width
          Height = height
          ViewportX = viewportX
          ViewportY = viewportY
          Flags = [] }

    /// Encode as JSON. Invariant-culture floats; tagged-object
    /// flags. Field order matches the record declaration so the
    /// shape stays stable across F# minor versions.
    let encode (obs: LayoutObservation) : string =
        let inv = System.Globalization.CultureInfo.InvariantCulture

        let escape (s: string) =
            s.Replace("\\", "\\\\").Replace("\"", "\\\"")

        let fmt (n: float) = n.ToString("F2", inv)

        let flagsJson = obs.Flags |> List.map LayoutFlag.encode |> String.concat ","

        let nodeId = escape obs.NodeId
        let w = fmt obs.Width
        let h = fmt obs.Height
        let vx = fmt obs.ViewportX
        let vy = fmt obs.ViewportY

        $"""{{"nodeId":"{nodeId}","width":{w},"height":{h},"viewportX":{vx},"viewportY":{vy},"flags":[{flagsJson}]}}"""
