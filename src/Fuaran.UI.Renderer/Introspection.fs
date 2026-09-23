module Fuaran.UI.Renderer.Introspection

// ============================================================================
//  The WIRING section of the in-page introspection surface (Phase 1844).
//
//  The console REPL (`window.__fuaran`, `DebugGlobal.fs`) could already say
//  what one binding slot READS (`BindingSlotInfo.DependsOn`, Phase 1674). It
//  could not say what DRIVES that read: the chip that declares a filter, the
//  `Select` whose write-back commits to it, the `SetState` or `Call into:` that
//  fills a key, the grid whose selection a `Binding.Selection` names. That
//  relation is `Fuaran.UI.WiringGraph` (Phase 1736), and it is the relation
//  the validator's wiring rules decide on — so a developer asking "why did this
//  chip do nothing?" was debugging against a graph the surface never showed.
//
//  This module is that graph as a DTO, and it is a PROJECTION ONLY:
//
//    * `ofGraph` reads a `WiringGraph` and nothing else. There is no second
//      walk of the spec vocabulary here, and there must never be one — the
//      binding walk is the ONE enumeration of what reads and writes what, and a
//      second copy is exactly the drift it exists to prevent.
//      `Fuaran.UI.Tests/WiringIntrospectionTests.fs` asserts the two agree on
//      every corpus `nodes/` fixture.
//    * The per-edge `ControlKinds` is a JOIN over the graph's own `Controls`
//      (the kinds of the controls at the edge's driving end), not a fact read
//      from the tree. It is what makes an edge self-describing: a
//      `filter-write-back` edge is one whose driver is a write-back position
//      (Phase 1785 / 1801's closed list) rather than a declared chip.
//
//  ── THE TWO RENDERINGS, AND THE CROSS-HOST BYTE CONTRACT ────────────────────
//
//  [[toJson]] is the DTO's canonical JSON — fixed ordinal key order, every
//  section deduplicated and sorted, strings escaped exactly as ECMAScript
//  `JSON.stringify` escapes them. [[describe]] is the REPL's printable text,
//  edges first. The TypeScript mirror (`@fuaran-ui/renderer`
//  `wiringIntrospection.ts`) READS this DTO — it decodes it strictly and
//  re-encodes and describes it — and for the same DTO both hosts produce the
//  same bytes on both renderings. The mirror derives nothing: the graph has one
//  derivation, here, and the second host's job is to show it identically.
//
//  Sorted rather than walk-ordered for the reason `WiringGraph.render` gives:
//  walk order is a property of how a tree is SPELLED, and a byte comparison
//  across hosts must depend on the wiring and on nothing else.
//
//  ── FABLE ──────────────────────────────────────────────────────────────────
//
//  Compiles on both pipelines: FSharp.Core, `System.Text.StringBuilder` and
//  this tier's own types only. No `System.Text.Json` — the console surface
//  re-parses [[toJson]] with the page's own `JSON.parse`.
// ============================================================================

open Fuaran.UI
open Fuaran.UI.Types

/// The DTO's format token. Bumped when the JSON shape or the [[describe]] line
/// grammar changes, so a recorded byte comparison says which grammar made it.
[<Literal>]
let FormatVersion = "fuaran-wiring-introspection/1"

/// One control: a node that drives `Name` on `Channel`. Every string is a
/// `WiringGraph` token (`WiringChannel.name`, `ControlKind.name`).
type WiringControlEntry =
    { Channel: string
      Name: string
      NodeId: string
      Kind: string }

/// One consumer usage: a node that reads `Name` on `Channel`; `Kind` is a
/// `ConsumerKind.name` token (`value-read` / `declared-edge`).
type WiringConsumerEntry =
    { Channel: string
      Name: string
      NodeId: string
      Kind: string }

/// One resolved edge — the entry this phase exists to surface.
type WiringEdgeEntry =
    {
        /// The channel the edge runs over (`filter` / `state` / `query` /
        /// `selection`).
        Channel: string
        /// The filter / key / query / producing-node name both ends meet on.
        Name: string
        /// The driving node's id — the edge's SOURCE.
        Control: string
        /// The reading node's id.
        Consumer: string
        /// What the reading end's evidence is worth: `declared-edge` for a
        /// `Query.dependsOn` name or a `Transform` / `Expr` param sourced from a
        /// filter, `value-read` for a plain read.
        Consumption: string
        /// The kinds of the controls at the driving end, sorted and distinct —
        /// `declared-filter` for a chip, `filter-write-back` for a write-back
        /// position, `state-write`, `fetch-into-state`, `fetch-into-query`,
        /// `selection-producer`. A join over `WiringGraph.Controls`.
        ControlKinds: string list
    }

/// An end that met nothing. `Reason` is `undriven` (an express control that
/// drives nothing) or `ungrounded` (a consumer naming something no control in
/// the tree produces); `Kind` is the control's or the consumer's kind token.
type WiringUnresolvedEntry =
    { Reason: string
      Channel: string
      Name: string
      NodeId: string
      Kind: string }

/// The wiring section of the introspection surface — the whole
/// `WiringGraph`, sorted, as plain data.
type WiringIntrospection =
    { Controls: WiringControlEntry list
      Consumers: WiringConsumerEntry list
      Edges: WiringEdgeEntry list
      Unresolved: WiringUnresolvedEntry list
      UntaggedStateReads: string list
      OpaqueReader: bool
      OpaqueWriter: bool }

// ── the projection ──────────────────────────────────────────────────────────

/// Deduplicate and sort by a key of strings. F# structural comparison of
/// strings is ORDINAL on both pipelines, which is the order the TypeScript
/// mirror's `<` gives — the property the byte contract stands on.
let private sortedBy (key: 'T -> string list) (items: 'T list) : 'T list =
    items |> List.distinct |> List.sortBy key

/// The projection. Reads the graph and nothing else.
let ofGraph (graph: WiringGraph.WiringGraph) : WiringIntrospection =
    let controls =
        graph.Controls
        |> List.map (fun c ->
            { WiringControlEntry.Channel = WiringGraph.WiringChannel.name c.Channel
              Name = c.Name
              NodeId = c.NodeId
              Kind = WiringGraph.ControlKind.name c.Kind })
        |> sortedBy (fun c -> [ c.Channel; c.Name; c.NodeId; c.Kind ])

    let consumers =
        graph.Consumers
        |> List.map (fun r ->
            { WiringConsumerEntry.Channel = WiringGraph.WiringChannel.name r.Channel
              Name = r.Name
              NodeId = r.NodeId
              Kind = WiringGraph.ConsumerKind.name r.Kind })
        |> sortedBy (fun r -> [ r.Channel; r.Name; r.NodeId; r.Kind ])

    let kindsAt (channel: string) (name: string) (nodeId: string) : string list =
        controls
        |> List.filter (fun c -> c.Channel = channel && c.Name = name && c.NodeId = nodeId)
        |> List.map (fun c -> c.Kind)
        |> List.distinct
        |> List.sort

    let edges =
        graph.Edges
        |> List.map (fun e ->
            let channel = WiringGraph.WiringChannel.name e.Channel

            { WiringEdgeEntry.Channel = channel
              Name = e.Name
              Control = e.Control
              Consumer = e.Consumer
              Consumption = WiringGraph.ConsumerKind.name e.Consumption
              ControlKinds = kindsAt channel e.Name e.Control })
        |> sortedBy (fun e -> [ e.Channel; e.Name; e.Control; e.Consumer; e.Consumption ])

    let unresolved =
        graph.Unresolved
        |> List.map (fun u ->
            match u with
            | WiringGraph.UnresolvedWiring.UndrivenControl c ->
                { WiringUnresolvedEntry.Reason = "undriven"
                  Channel = WiringGraph.WiringChannel.name c.Channel
                  Name = c.Name
                  NodeId = c.NodeId
                  Kind = WiringGraph.ControlKind.name c.Kind }
            | WiringGraph.UnresolvedWiring.UngroundedConsumer r ->
                { WiringUnresolvedEntry.Reason = "ungrounded"
                  Channel = WiringGraph.WiringChannel.name r.Channel
                  Name = r.Name
                  NodeId = r.NodeId
                  Kind = WiringGraph.ConsumerKind.name r.Kind })
        |> sortedBy (fun u -> [ u.Reason; u.Channel; u.Name; u.NodeId; u.Kind ])

    { Controls = controls
      Consumers = consumers
      Edges = edges
      Unresolved = unresolved
      UntaggedStateReads = graph.UntaggedStateReads |> Set.toList |> List.sort
      OpaqueReader = graph.OpaqueReader
      OpaqueWriter = graph.OpaqueWriter }

/// The projection over a live tree — one `WiringGraph.project`, then
/// [[ofGraph]]. Computed on demand by the console surface, never on a render.
let ofTree (root: Node<'Msg>) : WiringIntrospection = ofGraph (WiringGraph.project root)

// ── the canonical JSON ──────────────────────────────────────────────────────

/// Four lowercase hex digits, the body of the escape `JSON.stringify` writes.
let private hex4 (code: int) : string = sprintf "%04x" code

/// A JSON string literal, escaped EXACTLY as ECMAScript `JSON.stringify`
/// escapes one (ES2019 well-formed form): the two-character escapes for `"`,
/// `\`, backspace, form feed, newline, carriage return and tab; `\u00xx`
/// (lowercase) for every other control character; `\udxxx` for a LONE
/// surrogate; every other code unit as itself. The TypeScript mirror encodes
/// strings with `JSON.stringify`, so this is the half of the byte contract a
/// name carrying an unusual character would otherwise break.
let private jsonString (raw: string) : string =
    let builder = System.Text.StringBuilder()
    builder.Append '"' |> ignore
    let n = raw.Length
    let isHigh (c: char) = int c >= 0xD800 && int c <= 0xDBFF
    let isLow (c: char) = int c >= 0xDC00 && int c <= 0xDFFF
    let mutable i = 0

    while i < n do
        let c = raw.[i]

        match c with
        | '"' -> builder.Append "\\\"" |> ignore
        | '\\' -> builder.Append "\\\\" |> ignore
        | '\b' -> builder.Append "\\b" |> ignore
        | '\f' -> builder.Append "\\f" |> ignore
        | '\n' -> builder.Append "\\n" |> ignore
        | '\r' -> builder.Append "\\r" |> ignore
        | '\t' -> builder.Append "\\t" |> ignore
        | c when int c < 0x20 -> builder.Append("\\u" + hex4 (int c)) |> ignore
        | c when isHigh c && i + 1 < n && isLow raw.[i + 1] ->
            builder.Append(c).Append(raw.[i + 1]) |> ignore
            i <- i + 1
        | c when isHigh c || isLow c -> builder.Append("\\u" + hex4 (int c)) |> ignore
        | c -> builder.Append c |> ignore

        i <- i + 1

    builder.Append '"' |> ignore
    builder.ToString()

let private jsonObject (fields: (string * string) list) : string =
    "{"
    + (fields |> List.map (fun (k, v) -> jsonString k + ":" + v) |> String.concat ",")
    + "}"

let private jsonArray (items: string list) : string = "[" + String.concat "," items + "]"

let private jsonBool (b: bool) : string = if b then "true" else "false"

/// The DTO's canonical JSON text: no whitespace, object keys in ordinal order,
/// sections in [[ofGraph]]'s sorted order. Byte-identical to the TypeScript
/// mirror's `encodeWiringIntrospection` over the same DTO.
let toJson (w: WiringIntrospection) : string =
    let control (c: WiringControlEntry) =
        jsonObject
            [ "channel", jsonString c.Channel
              "kind", jsonString c.Kind
              "name", jsonString c.Name
              "nodeId", jsonString c.NodeId ]

    let consumer (r: WiringConsumerEntry) =
        jsonObject
            [ "channel", jsonString r.Channel
              "kind", jsonString r.Kind
              "name", jsonString r.Name
              "nodeId", jsonString r.NodeId ]

    let edge (e: WiringEdgeEntry) =
        jsonObject
            [ "channel", jsonString e.Channel
              "consumer", jsonString e.Consumer
              "consumption", jsonString e.Consumption
              "control", jsonString e.Control
              "controlKinds", jsonArray (e.ControlKinds |> List.map jsonString)
              "name", jsonString e.Name ]

    let unresolved (u: WiringUnresolvedEntry) =
        jsonObject
            [ "channel", jsonString u.Channel
              "kind", jsonString u.Kind
              "name", jsonString u.Name
              "nodeId", jsonString u.NodeId
              "reason", jsonString u.Reason ]

    jsonObject
        [ "consumers", jsonArray (w.Consumers |> List.map consumer)
          "controls", jsonArray (w.Controls |> List.map control)
          "edges", jsonArray (w.Edges |> List.map edge)
          "format", jsonString FormatVersion
          "opaqueReader", jsonBool w.OpaqueReader
          "opaqueWriter", jsonBool w.OpaqueWriter
          "unresolved", jsonArray (w.Unresolved |> List.map unresolved)
          "untaggedStateReads", jsonArray (w.UntaggedStateReads |> List.map jsonString) ]

// ── the REPL rendering ──────────────────────────────────────────────────────

/// The printable form `__fuaran.describeWiring()` returns — edges first,
/// because an edge is what a developer debugging a dead control is looking
/// for, then the controls (a `filter-write-back` control IS a write-back
/// position), then every end that met nothing. Consumers are counted, not
/// listed: every consumer that meets a control is already an edge, and every
/// one that does not is an `ungrounded` line. Byte-identical to the TypeScript
/// mirror's `describeWiringIntrospection` over the same DTO.
let describe (w: WiringIntrospection) : string =
    let section (title: string) (rows: string list) : string list =
        title
        :: (if List.isEmpty rows then
                [ "  (none)" ]
            else
                rows |> List.map (fun r -> "  " + r))

    let header =
        sprintf
            "wiring (%s): %d edges, %d controls, %d consumers, %d unresolved"
            FormatVersion
            (List.length w.Edges)
            (List.length w.Controls)
            (List.length w.Consumers)
            (List.length w.Unresolved)

    let edges =
        w.Edges
        |> List.map (fun e ->
            e.Channel
            + " "
            + e.Name
            + ": "
            + e.Control
            + " -> "
            + e.Consumer
            + " ["
            + e.Consumption
            + "] via "
            + String.concat "+" e.ControlKinds)

    let controls =
        w.Controls
        |> List.map (fun c -> c.Channel + " " + c.Name + " @ " + c.NodeId + " (" + c.Kind + ")")

    let unresolved =
        w.Unresolved
        |> List.map (fun u ->
            u.Reason
            + " "
            + u.Channel
            + " "
            + u.Name
            + " @ "
            + u.NodeId
            + " ("
            + u.Kind
            + ")")

    let untagged =
        "untagged state reads: "
        + (if List.isEmpty w.UntaggedStateReads then
               "(none)"
           else
               String.concat ", " w.UntaggedStateReads)

    let flags =
        "opaque reader: "
        + (if w.OpaqueReader then "yes" else "no")
        + "; opaque writer: "
        + (if w.OpaqueWriter then "yes" else "no")

    let lines =
        List.concat
            [ [ header ]
              section "edges:" edges
              section "controls:" controls
              section "unresolved:" unresolved
              [ untagged; flags ] ]

    String.concat "\n" lines + "\n"
