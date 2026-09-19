module Fuaran.UI.WiringGraph

// ============================================================================
//  The WIRING GRAPH of a decoded tree (Phase 1736) — which controls drive which
//  consumers, as plain data.
//
//  "Controls drive their named consumers" is a property of the RESOLVED BINDING
//  GRAPH — a chip declaration and the readers that name it, a `SetState` and the
//  readers of that key, a fetch's landing slot and the reader that binds it, a
//  selection producer and the readers that target it. It is not a property of
//  the tree's SHAPE, so no structural predicate over kinds, containment or style
//  can express it: two trees with identical shape differ on this question by one
//  string.
//
//  This module is that graph, and it is a PROJECTION ONLY. It decides nothing,
//  reports no defect, and is read by no render path and by no validator: every
//  rule that today reads a SLICE of this graph (see the table below) keeps its
//  own derivation unchanged. What this adds is the WHOLE relation, with the node
//  ids on both ends, so a caller that wants to decide something about wiring can
//  do it over enumerated edges instead of re-walking the spec vocabulary.
//
//  ── WHAT THE FOUR CHANNELS ARE ─────────────────────────────────────────────
//
//  Each channel already has a shipped pre-emit rule that reads a slice of it.
//  The channels are those, not a new vocabulary:
//
//    channel    control (the driver)              consumer (the reader)
//    ───────    ──────────────────────            ──────────────────────
//    Filter     a `Filters` chip declaration      a `Binding.Filter` read, a
//                                                 `Query.dependsOn` name, or a
//                                                 param whose source is a filter
//    State      an `Action.SetState`, or an       a `Binding.State` read
//               `Action.Call` landing `into:
//               State <key>`
//    Query      an `Action.Call` landing `into:   a `Binding.Query` read
//               Query <name>`
//    Selection  a selection-PRODUCING node        a `Binding.Selection` naming it
//
//  ── WHERE THIS LIVES, AND WHY NOT BESIDE THE DECODER ───────────────────────
//
//  Every input is `BindingWalk.collect`'s `TreeBindingFacts`, which is in this
//  package, and the four rules above are `PreEmitValidate`'s, also in this
//  package. The projection is derived from the walk rather than performing one:
//  a second walk of the spec vocabulary is precisely the drift the walk exists
//  to prevent, and the walk's own forward-coupling note is the reason a new
//  binding-bearing slot reaches this graph with nothing here edited.
//
//  ── FABLE ──────────────────────────────────────────────────────────────────
//
//  FSharp.Core and this tier's own types only — no reflection, no `System.IO`,
//  no text encoding, no `StringBuilder`. [[render]] returns a `string`; a caller
//  wanting BYTES encodes it itself, which keeps the one encoding decision at the
//  host rather than in a package four hosts compile.
// ============================================================================

open Fuaran.UI.Types

// ── the vocabulary ──────────────────────────────────────────────────────────

/// The reactive channel an edge runs over.
[<RequireQualifiedAccess>]
type WiringChannel =
    /// The `$state` store.
    | State
    /// The `$filters` store.
    | Filter
    /// A named query slot.
    | Query
    /// A selection produced by one node and read by another; the NAME on this
    /// channel is the producing node's own id.
    | Selection

/// How a control drives its channel.
[<RequireQualifiedAccess>]
type ControlKind =
    /// A `FilterSpec.Name` on a `Filters` node — the language's one express
    /// filter declaration. FUARAN074's subject.
    | DeclaredFilter
    /// An `Action.SetState` reachable from a wire-survivable action slot.
    /// FUARAN098's subject.
    | StateWrite
    /// An `Action.Call` carrying `into: State <key>`.
    | FetchIntoState
    /// An `Action.Call` carrying `into: Query <name>`. FUARAN072's subject.
    | FetchIntoQuery
    /// A node whose KIND produces a selection (a `Visualisation` — the grid's
    /// row-click write; charts / tables / maps through host closures).
    /// FUARAN071's subject.
    | SelectionProducer

/// What a consumer's read is worth as evidence of an edge.
[<RequireQualifiedAccess>]
type ConsumerKind =
    /// A plain value read. It says the reader WANTS the slot; it does not
    /// assert that the tree fills it, because a host may legitimately furnish
    /// `BindingSources.Filters` / `.State` / `.Queries` with nothing in the tree
    /// to see. This is exactly why FUARAN075 exempts a plain `Binding.Filter`.
    | ValueRead
    /// An edge the TREE asserts — a `Query.dependsOn` name, or a `Transform` /
    /// `Expr` param whose source is a `Binding.Filter`. FUARAN075 fires on one
    /// of these naming an undeclared filter, and does not fire on a value read.
    | DeclaredEdge

/// One control: a node that drives `Name` on `Channel`.
type WiringControl =
    { NodeId: string
      Channel: WiringChannel
      Name: string
      Kind: ControlKind }

/// One consumer: a node that reads `Name` on `Channel`. Recorded once per
/// USAGE, so a node reading one filter in two slots appears twice — the
/// consumption relation is recovered from [[WiringGraph.Edges]], which is
/// deduplicated.
type WiringConsumer =
    { NodeId: string
      Channel: WiringChannel
      Name: string
      Kind: ConsumerKind }

/// One resolved edge — a control and a consumer that meet on a name. Both ends
/// carry their node id, which is the whole point of the projection.
type WiringEdge =
    {
        Channel: WiringChannel
        Name: string
        /// The driving node's id.
        Control: string
        /// The reading node's id.
        Consumer: string
        /// What the reading end's evidence is worth (see [[ConsumerKind]]).
        Consumption: ConsumerKind
    }

/// An end that met nothing.
[<RequireQualifiedAccess>]
type UnresolvedWiring =
    /// An EXPRESS control that drives nothing in this tree — the
    /// decorative-wiring trap: a control that looks wired and drives nothing.
    ///
    /// Raised for express controls ONLY. A `SelectionProducer` is a capability
    /// of a node kind rather than a declaration to drive, and no shipped rule
    /// fires on an unread grid selection — so reporting every grid in every
    /// tree here would bury the finding this case exists to surface. See
    /// [[ControlKind.isExpress]].
    | UndrivenControl of control: WiringControl
    /// A consumer naming something no control in this tree produces. On the
    /// Filter channel with `ConsumerKind.DeclaredEdge` this is FUARAN075's
    /// subject; with `ConsumerKind.ValueRead` it is the legitimate host-fed
    /// shape and is NOT a defect. Deciding between them is the caller's.
    | UngroundedConsumer of consumer: WiringConsumer

/// The whole projection.
type WiringGraph =
    {
        /// Every control, in walk order (the order every list on
        /// `TreeBindingFacts` is in).
        Controls: WiringControl list
        /// Every consumer usage, in walk order.
        Consumers: WiringConsumer list
        /// Every resolved control→consumer edge, deduplicated, in walk order of
        /// the control end.
        Edges: WiringEdge list
        /// Every end that met nothing, deduplicated.
        Unresolved: UnresolvedWiring list
        /// State keys the tree can be shown to READ through a surface the walk
        /// cannot tag with a reading node — a `DataGrid`'s `sortStateKey` and
        /// `pageStateKey` are plain STRINGS the renderer reads, with no
        /// `Binding` to attribute.
        ///
        /// **Carried because absence from [[Consumers]] is NOT absence of a
        /// reader.** [[Unresolved]] already accounts for it, so an
        /// `UndrivenControl` is never raised for a key one of these reads; a
        /// caller re-deriving undriven-ness from `Consumers` alone would be
        /// wrong, and this field is what makes that recoverable rather than
        /// silent.
        UntaggedStateReads: Set<string>
        /// The tree holds a reader whose state access cannot be seen — a
        /// `Binding.Computed` closure (handed the whole state bag) or a
        /// registered `NodeKind.Custom` renderer (host code that may read
        /// anything). Under this, an `UndrivenControl` proves NOTHING: the
        /// absence of a read is not evidence.
        OpaqueReader: bool
        /// The tree holds a writer whose destination cannot be seen. Under
        /// this, an `UngroundedConsumer` on the State channel proves nothing.
        OpaqueWriter: bool
    }

// ── names, for the canonical rendering and for callers that report ──────────

module WiringChannel =
    /// The channel's stable token. Part of [[render]]'s byte contract.
    let name (channel: WiringChannel) : string =
        match channel with
        | WiringChannel.State -> "state"
        | WiringChannel.Filter -> "filter"
        | WiringChannel.Query -> "query"
        | WiringChannel.Selection -> "selection"

module ControlKind =
    /// The kind's stable token. Part of [[render]]'s byte contract.
    let name (kind: ControlKind) : string =
        match kind with
        | ControlKind.DeclaredFilter -> "declared-filter"
        | ControlKind.StateWrite -> "state-write"
        | ControlKind.FetchIntoState -> "fetch-into-state"
        | ControlKind.FetchIntoQuery -> "fetch-into-query"
        | ControlKind.SelectionProducer -> "selection-producer"

    /// True when the control is an EXPRESS declaration to drive — something an
    /// author wrote in order to make a consumer move — rather than a capability
    /// the node's kind happens to carry.
    ///
    /// The split is read off the shipped rules rather than invented: a chip
    /// (FUARAN074), a `SetState` (FUARAN098) and a `Call into:` (FUARAN072)
    /// each have a rule that fires when they drive nothing, and an unread
    /// selection producer has none. It is the ONE implementation of the split —
    /// `UndrivenControl` is raised through it, and a caller filtering controls
    /// reads it rather than re-matching the kinds.
    let isExpress (kind: ControlKind) : bool =
        match kind with
        | ControlKind.DeclaredFilter
        | ControlKind.StateWrite
        | ControlKind.FetchIntoState
        | ControlKind.FetchIntoQuery -> true
        | ControlKind.SelectionProducer -> false

module ConsumerKind =
    /// The kind's stable token. Part of [[render]]'s byte contract.
    let name (kind: ConsumerKind) : string =
        match kind with
        | ConsumerKind.ValueRead -> "value-read"
        | ConsumerKind.DeclaredEdge -> "declared-edge"

// ── the projection ──────────────────────────────────────────────────────────

/// A chip's read of its OWN filter is a declaration, not consumption — the
/// declarative-chip shape, where the chip both declares the name and binds its
/// own value slot to it. FUARAN074 excludes exactly this, and so must the edge
/// set, or every declared chip would resolve against itself and the decorative
/// trap would be unreachable.
let private isSelfRead (control: WiringControl) (consumer: WiringConsumer) : bool =
    control.Kind = ControlKind.DeclaredFilter
    && consumer.Kind = ConsumerKind.ValueRead
    && consumer.NodeId = control.NodeId

/// The projection over facts a caller already has. Prefer this wherever
/// `BindingWalk.collect` has already run — `PreEmitValidate` performs ONE walk
/// per validation and every rule reads those facts, and this projection is
/// cheap over them and expensive beside them.
let ofFacts (facts: BindingWalk.TreeBindingFacts) : WiringGraph =
    let controls =
        // Filter — the chips. `DeclaredFilters` is (declaring node id, name).
        // NOTE `WiringControl.` on the first field of every construction below:
        // `WiringControl` and `WiringConsumer` carry the same four field names,
        // so bare inference resolves to the LAST-declared record. The
        // qualification is load-bearing, not decoration.
        (facts.DeclaredFilters
         |> List.map (fun (owner, name) ->
             { WiringControl.NodeId = owner
               Channel = WiringChannel.Filter
               Name = name
               Kind = ControlKind.DeclaredFilter }))
        // State — the `SetState` writes, reader-tagged. `StateKeys.WriteKeys` is
        // the wider set (a grid's `editStateKey`, a control's write-back slot),
        // but it is a `Set<string>` with no writing node to put on the edge, so
        // it cannot serve a projection whose contract is ids on both ends. The
        // tagged list is what is claimed; the wider set is not silently implied.
        @ (facts.StateKeys.Writes
           |> List.map (fun (writer, key) ->
               { WiringControl.NodeId = writer
                 Channel = WiringChannel.State
                 Name = key
                 Kind = ControlKind.StateWrite }))
        // Both landing slots of a wire-survivable `Action.Call`.
        @ (facts.Calls
           |> List.choose (fun c ->
               match c.Into with
               | Some(CallResultTarget.State key) ->
                   Some
                       { WiringControl.NodeId = c.Reader
                         Channel = WiringChannel.State
                         Name = key
                         Kind = ControlKind.FetchIntoState }
               | Some(CallResultTarget.Query name) ->
                   Some
                       { WiringControl.NodeId = c.Reader
                         Channel = WiringChannel.Query
                         Name = name
                         Kind = ControlKind.FetchIntoQuery }
               | None -> None))
        // Selection — the producing nodes. The NAME is the producer's own id,
        // which is what a `Binding.Selection` names.
        @ (facts.Nodes
           |> Map.toList
           |> List.choose (fun (nodeId, producesSelection) ->
               if producesSelection then
                   Some
                       { WiringControl.NodeId = nodeId
                         Channel = WiringChannel.Selection
                         Name = nodeId
                         Kind = ControlKind.SelectionProducer }
               else
                   None))

    let consumers =
        facts.Uses
        |> List.collect (fun u ->
            let at (channel: WiringChannel) (name: string) (kind: ConsumerKind) : WiringConsumer =
                { NodeId = u.Reader
                  Channel = channel
                  Name = name
                  Kind = kind }

            match u.Use with
            | BindingWalk.BindingUse.State key -> [ at WiringChannel.State key ConsumerKind.ValueRead ]
            // A `Binding.Transform`'s live SOURCE slot is a State read that does
            // NOT also emit `BindingUse.State` (it is FUARAN105's subject and
            // keeps its own case). It reaches `StateKeys.Reads`, so omitting it
            // here would make the tagged consumer set disagree with the untagged
            // one on the same tree.
            | BindingWalk.BindingUse.TransformStateSource(key, _) ->
                [ at WiringChannel.State key ConsumerKind.ValueRead ]
            | BindingWalk.BindingUse.Filter name -> [ at WiringChannel.Filter name ConsumerKind.ValueRead ]
            | BindingWalk.BindingUse.TransformParamFilter name ->
                [ at WiringChannel.Filter name ConsumerKind.DeclaredEdge ]
            | BindingWalk.BindingUse.Query(name, dependsOn) ->
                at WiringChannel.Query name ConsumerKind.ValueRead
                :: (dependsOn
                    |> List.map (fun f -> at WiringChannel.Filter f ConsumerKind.DeclaredEdge))
            | BindingWalk.BindingUse.Selection target -> [ at WiringChannel.Selection target ConsumerKind.ValueRead ]
            // The cases that are not consumption edges, matched EXHAUSTIVELY so
            // a new `BindingUse` case has to be classified here rather than
            // silently escaping the graph. `Computed` is an opaque read carrying
            // no name (its opacity reaches the graph through `OpaqueReader`);
            // `TransformParam` names a PIPELINE param, not a store slot, and its
            // filter-sourced form already arrives as `TransformParamFilter`;
            // `StateSeed`, `InlineTable` and `TransformSite` are facts about a
            // slot rather than reads of one, and are filtered out of `Uses`
            // upstream in any case.
            | BindingWalk.BindingUse.Computed
            | BindingWalk.BindingUse.TransformParam _
            | BindingWalk.BindingUse.StateSeed _
            | BindingWalk.BindingUse.InlineTable _
            | BindingWalk.BindingUse.TransformSite _ -> [])

    let edges =
        controls
        |> List.collect (fun c ->
            consumers
            |> List.filter (fun r -> r.Channel = c.Channel && r.Name = c.Name && not (isSelfRead c r))
            |> List.map (fun r ->
                { Channel = c.Channel
                  Name = c.Name
                  Control = c.NodeId
                  Consumer = r.NodeId
                  Consumption = r.Kind }))
        |> List.distinct

    let untaggedStateReads = facts.StateKeys.Reads

    let driven = edges |> List.map (fun e -> e.Control, e.Channel, e.Name) |> Set.ofList

    let undriven =
        controls
        |> List.filter (fun c ->
            ControlKind.isExpress c.Kind
            && not (Set.contains (c.NodeId, c.Channel, c.Name) driven)
            // The untagged State reads (see `WiringGraph.UntaggedStateReads`) —
            // a key a grid's `sortStateKey` reads HAS a reader, just not one the
            // walk can name, so a write to it is not undriven.
            && not (c.Channel = WiringChannel.State && Set.contains c.Name untaggedStateReads))
        |> List.distinct
        |> List.map UnresolvedWiring.UndrivenControl

    let controlNames = controls |> List.map (fun c -> c.Channel, c.Name) |> Set.ofList

    let ungrounded =
        consumers
        |> List.filter (fun r -> not (Set.contains (r.Channel, r.Name) controlNames))
        |> List.distinct
        |> List.map UnresolvedWiring.UngroundedConsumer

    { Controls = controls
      Consumers = consumers
      Edges = edges
      Unresolved = undriven @ ungrounded
      UntaggedStateReads = untaggedStateReads
      OpaqueReader = facts.StateKeys.OpaqueReader
      OpaqueWriter = facts.StateKeys.OpaqueWriter }

/// The projection over a decoded tree. Pure and total: it reads the tree and
/// nothing else, performs no IO, resolves no host store, and returns the same
/// graph for the same tree on every host and every run.
let project<'Msg> (root: Node<'Msg>) : WiringGraph = ofFacts (BindingWalk.collect root)

// ── the canonical rendering ─────────────────────────────────────────────────

/// The rendering's format token. Bumped when the line grammar below changes, so
/// a recorded byte measurement says which grammar produced it.
[<Literal>]
let FormatVersion = "fuaran-wiring-graph/1"

/// Tabs and newlines are the record and field separators, so a name carrying
/// one must not be able to forge a row. Backslash first, or the escapes escape
/// each other.
let private esc (s: string) : string =
    s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r")

/// A CANONICAL, byte-stable rendering of the graph: one tab-separated record
/// per line, every section deduplicated and sorted, terminated by a newline.
///
/// **Sorted here rather than in the graph, deliberately.** The graph's lists are
/// in WALK order, which is the order every list on `TreeBindingFacts` is in and
/// which a caller reporting findings in document order needs. Walk order is a
/// property of how the tree is SPELLED — two trees whose sibling nodes are
/// ordered differently, and whose wiring is identical, walk differently — so it
/// is exactly the wrong order for a byte comparison. Sorting at the rendering
/// boundary gives both: the projection stays in document order, and the bytes
/// depend on the wiring and on nothing else.
let render (graph: WiringGraph) : string =
    let row (parts: string list) : string =
        parts |> List.map esc |> String.concat "\t"

    let section (rows: string list) : string list = rows |> List.distinct |> List.sort

    let controls =
        graph.Controls
        |> List.map (fun c ->
            row
                [ "control"
                  WiringChannel.name c.Channel
                  c.Name
                  c.NodeId
                  ControlKind.name c.Kind ])
        |> section

    let consumers =
        graph.Consumers
        |> List.map (fun r ->
            row
                [ "consumer"
                  WiringChannel.name r.Channel
                  r.Name
                  r.NodeId
                  ConsumerKind.name r.Kind ])
        |> section

    let edges =
        graph.Edges
        |> List.map (fun e ->
            row
                [ "edge"
                  WiringChannel.name e.Channel
                  e.Name
                  e.Control
                  e.Consumer
                  ConsumerKind.name e.Consumption ])
        |> section

    let unresolved =
        graph.Unresolved
        |> List.map (fun u ->
            match u with
            | UnresolvedWiring.UndrivenControl c ->
                row
                    [ "undriven"
                      WiringChannel.name c.Channel
                      c.Name
                      c.NodeId
                      ControlKind.name c.Kind ]
            | UnresolvedWiring.UngroundedConsumer r ->
                row
                    [ "ungrounded"
                      WiringChannel.name r.Channel
                      r.Name
                      r.NodeId
                      ConsumerKind.name r.Kind ])
        |> section

    let untagged =
        graph.UntaggedStateReads
        |> Set.toList
        |> List.map (fun k -> row [ "untagged-state-read"; k ])
        |> section

    let flags =
        [ row [ "opaque-reader"; (if graph.OpaqueReader then "true" else "false") ]
          row [ "opaque-writer"; (if graph.OpaqueWriter then "true" else "false") ] ]

    let lines =
        List.concat [ [ FormatVersion ]; controls; consumers; edges; unresolved; untagged; flags ]

    String.concat "\n" lines + "\n"
