module Fuaran.UI.Renderer.BindingGraph

// ============================================================================
//  BindingGraph — which binding sites a write makes stale (Phase 1760).
//
//  The question "when `$state.orders` is written, which readers re-evaluate?"
//  used to be answered only by a TEST oracle over `BindingWalk.collect` (the
//  Phase 1479 cone law). This module answers it in production code, and it
//  answers it with `Fuaran.Core.Propagation`: the facts the walk already
//  gathers become a dependency map, and the dirty set of a write is Core's
//  reverse-reachability closure over it. Core owns no evaluator; neither does
//  this module — it says WHAT is stale, the host re-renders it.
//
//  NODES OF THE GRAPH. Two kinds, keyed as strings for `Propagation`:
//
//    * INPUTS — the channels something writes: a state key, a filter name, a
//      selection target, a query name. An input reads nothing, except that a
//      query reads the filters its `dependsOn` names, so a filter write
//      reaches the query's readers THROUGH the query (the closure is transitive
//      and this is where that matters).
//    * SITES — the readers that re-evaluate. The walk records reads per READER
//      (the node whose spec holds the binding), not per slot, so the finest
//      honest identity for an ordinary read is the reader's node id
//      (`Site.Reader`). A `Binding.Transform` is the exception: the walk gathers
//      it as a `TransformSiteFacts`, with its slot when the reader's own arm
//      names one and its live-store key where it has one, so it is its own
//      site (`Site.Transform`) — the identity a host refreshing an
//      `ILiveTransformStore` needs.
//
//  WHAT THE GRAPH DECLINES, AND SAYS SO. Three reads cannot be attributed from
//  the facts, and each is a named `OpaqueRead` on a `Conservative` verdict,
//  never a silent full-dirty:
//
//    * `Binding.Computed` — its closure is handed the whole state bag, so its
//      reader is dirty on EVERY state write.
//    * a live Transform source over a channel other than `Binding.State` — the
//      walk records no key for it (`TransformSiteFacts.StateKey` is `None`), so
//      its reader is dirty on EVERY write.
//    * an opaque reader the facts do not LOCATE — `StateKeys.OpaqueReader` is
//      also set by a `NodeKind.Custom` node, whose id the facts do not carry,
//      and the flag cannot tell "a Computed we located" apart from "a Computed
//      and a Custom node". So whenever the flag is set the graph cannot prove
//      it has located every opaque reader, and a state write dirties every
//      site and every node of the tree.
//
//  The over-approximation is always safe: a site re-evaluated needlessly costs
//  time, a site left stale costs a wrong screen. `Exact` is a MINIMALITY claim
//  and is made only when no opaque read took part.
//
//  Fable-clean: pattern matching over the walk's records and Core's
//  FSharp.Core-only `Propagation` — no reflection, no server-only API.
// ============================================================================

open Fuaran.UI.BindingWalk

/// A channel a write lands on — the inputs of the graph.
[<RequireQualifiedAccess>]
type Input =
    /// `$state.<key>` — an `Action.SetState`, a two-way binding's write-back,
    /// a `Call`'s `into: State` result.
    | State of key: string
    /// A filter chip's value.
    | Filter of name: string
    /// The selection a producer node publishes.
    | Selection of targetNodeId: string
    /// A named query's result (it also moves when a filter it depends on does;
    /// the graph derives that, so a caller names only what was written).
    | Query of name: string

/// A binding site: a reader that re-evaluates when something it reads is
/// written.
[<RequireQualifiedAccess>]
type Site =
    /// Every binding the reader's spec holds. The walk records reads per
    /// reader, not per slot, so this is the finest identity an ordinary read
    /// has.
    | Reader of nodeId: string
    /// One `Binding.Transform` site on a reader: its slot when the reader's own
    /// arm names it (`Some "source"` on a grid / chart / map row feed), its
    /// live-store key when the walk has one (`TransformSiteFacts.SiteKey`), and
    /// its ordinal among the reader's Transform sites in walk order — so two
    /// unslotted, unkeyed sites on one reader stay two sites.
    | Transform of reader: string * slot: string option * siteKey: string option * ordinal: int

/// A read the graph cannot attribute from the facts — the reason a verdict is
/// `Conservative`. See the header for each.
[<RequireQualifiedAccess>]
type OpaqueRead =
    /// A `Binding.Computed` on this reader: its closure is handed the whole
    /// state bag, so every state write dirties it.
    | Computed of reader: string
    /// A live `Binding.Transform` on this reader whose source is not a
    /// `Binding.State`; the facts name no channel for it, so every write
    /// dirties it.
    | LiveSourceUnnamed of reader: string
    /// `StateKeys.OpaqueReader` is set and the facts cannot locate every opaque
    /// reader it stands for (a `NodeKind.Custom` renderer's id is not carried),
    /// so a state write dirties every site and every node.
    | Unlocated

/// The answer to "which sites does this write touch".
[<RequireQualifiedAccess>]
type Dirty =
    /// Sound AND minimal: exactly the sites that read, directly or through a
    /// query's filter dependency, something the write changed.
    | Exact of sites: Set<Site>
    /// Sound, not minimal: the sites above plus every site an opaque read may
    /// reach, with each opaque read that took part named.
    | Conservative of sites: Set<Site> * because: OpaqueRead list

/// The binding dependency graph of one tree.
type Graph =
    {
        /// `Propagation`'s dependency map: node id → the ids it reads. Sites
        /// and queries are keys; inputs appear as read targets.
        Dependencies: Map<string, Set<string>>
        /// Every site, by its node id in `Dependencies`.
        Sites: Map<string, Site>
        /// The `Binding.Computed` readers, in walk order.
        ComputedReaders: string list
        /// The readers carrying a live Transform source over an unnamed
        /// channel, in walk order.
        UnnamedLiveReaders: string list
        /// `StateKeys.OpaqueReader` — see `OpaqueRead.Unlocated`.
        OpaqueReader: bool
        /// Every node id of the tree (`TreeBindingFacts.Nodes`), for the
        /// `Unlocated` fallback.
        Nodes: Set<string>
    }

/// The graph id of an input. The tag is a fixed prefix and the name is the
/// suffix, so two inputs share an id only when they are the same input; no
/// input id begins `site:` or `opaque:`.
let inputId (input: Input) : string =
    match input with
    | Input.State key -> "state:" + key
    | Input.Filter name -> "filter:" + name
    | Input.Selection target -> "selection:" + target
    | Input.Query name -> "query:" + name

/// The pseudo-input a `Binding.Computed` reader reads: every state write
/// touches it.
let private anyStateId = "opaque:state"

/// The pseudo-input an unnamed live Transform source reads: every write
/// touches it.
let private anyWriteId = "opaque:any"

let private siteId (ordinal: int) = "site:" + string ordinal

/// The input ids one usage reads. `TransformParam` names a param, not a
/// channel — its SOURCE binding's own uses carry the read.
let private readsOfUse (u: BindingUse) : string list =
    match u with
    | BindingUse.State key
    | BindingUse.TransformStateSource(key, _) -> [ inputId (Input.State key) ]
    | BindingUse.Filter name
    | BindingUse.TransformParamFilter name -> [ inputId (Input.Filter name) ]
    | BindingUse.Selection target -> [ inputId (Input.Selection target) ]
    | BindingUse.Query(name, _) -> [ inputId (Input.Query name) ]
    | BindingUse.Computed -> [ anyStateId ]
    | BindingUse.TransformParam _
    | BindingUse.StateSeed _
    | BindingUse.InlineTable _
    | BindingUse.TransformSite _
    | BindingUse.UnstyledDateFormat -> []

/// Build the dependency graph from the facts `BindingWalk.collect` gathers.
/// Pure, total, deterministic (sites are numbered in walk order).
let ofFacts (facts: TreeBindingFacts) : Graph =
    // Each reader's read set, in first-seen reader order.
    let readerOrder = facts.Uses |> List.map (fun u -> u.Reader) |> List.distinct

    let readerReads: Map<string, Set<string>> =
        facts.Uses
        |> List.fold
            (fun acc u ->
                let prior = Map.tryFind u.Reader acc |> Option.defaultValue Set.empty
                Map.add u.Reader (Set.union prior (Set.ofList (readsOfUse u.Use))) acc)
            Map.empty

    // A query reads the filters its `dependsOn` names, unioned over every
    // reader that declared it.
    let queryReads: Map<string, Set<string>> =
        facts.Uses
        |> List.fold
            (fun acc u ->
                match u.Use with
                | BindingUse.Query(name, dependsOn) ->
                    let key = inputId (Input.Query name)
                    let prior = Map.tryFind key acc |> Option.defaultValue Set.empty

                    let deps = dependsOn |> List.map (Input.Filter >> inputId) |> Set.ofList

                    Map.add key (Set.union prior deps) acc
                | _ -> acc)
            Map.empty

    let unnamedLive =
        facts.TransformSites
        |> List.filter (fun d -> d.Site.IsLive && d.Site.StateKey.IsNone)
        |> List.map (fun d -> d.Reader)
        |> List.distinct

    let readsOfReader (reader: string) =
        let own = Map.tryFind reader readerReads |> Option.defaultValue Set.empty

        if List.contains reader unnamedLive then
            Set.add anyWriteId own
        else
            own

    let readerSites =
        readerOrder
        |> List.map (fun r -> Site.Reader r, readsOfReader r)
        |> List.filter (fun (_, reads) -> not (Set.isEmpty reads))

    // A Transform site reads its own SOURCE channel, plus — when its pipeline
    // references a param — its reader's whole read set, because the walk
    // records a param's source on the reader rather than on the site. That is
    // an over-approximation only across the reader's other slots.
    let transformSites =
        facts.TransformSites
        |> List.fold
            (fun (ordinals: Map<string, int>, acc) d ->
                let ordinal = Map.tryFind d.Reader ordinals |> Option.defaultValue 0
                let s = d.Site

                let sourceReads =
                    match s.IsLive, s.StateKey with
                    | true, Some key -> Set.singleton (inputId (Input.State key))
                    | true, None -> Set.singleton anyWriteId
                    | false, _ -> Set.empty

                let paramReads =
                    if List.isEmpty (Fuaran.Core.Transform.paramsOf s.Pipeline) then
                        Set.empty
                    else
                        readsOfReader d.Reader

                let site = Site.Transform(d.Reader, s.Slot, s.SiteKey, ordinal)
                Map.add d.Reader (ordinal + 1) ordinals, (site, Set.union sourceReads paramReads) :: acc)
            (Map.empty, [])
        |> snd
        |> List.rev

    let numbered =
        (readerSites @ transformSites) |> List.mapi (fun i sr -> siteId i, sr)

    let dependencies =
        numbered
        |> List.fold (fun acc (id, (_, reads)) -> Map.add id reads acc) queryReads

    { Dependencies = dependencies
      Sites = numbered |> List.map (fun (id, (site, _)) -> id, site) |> Map.ofList
      ComputedReaders =
        facts.Uses
        |> List.choose (fun u ->
            match u.Use with
            | BindingUse.Computed -> Some u.Reader
            | _ -> None)
        |> List.distinct
      UnnamedLiveReaders = unnamedLive
      OpaqueReader = facts.StateKeys.OpaqueReader
      Nodes = facts.Nodes |> Map.toSeq |> Seq.map fst |> Set.ofSeq }

/// Build the graph of a tree — `ofFacts` over `BindingWalk.collect`.
let ofTree<'Msg> (root: Fuaran.UI.Types.Node<'Msg>) : Graph =
    ofFacts (Fuaran.UI.BindingWalk.collect root)

/// The sites a write to `changed` makes stale — `Propagation.dirtyFromChangedIds`
/// over the graph, projected to sites. An empty `changed` dirties nothing.
let dirty (graph: Graph) (changed: Input seq) : Dirty =
    let changed = List.ofSeq changed

    let anyState =
        changed
        |> List.exists (function
            | Input.State _ -> true
            | _ -> false)

    let anyWrite = not (List.isEmpty changed)

    let seeds =
        [ yield! changed |> List.map inputId
          if anyState then
              yield anyStateId
          if anyWrite then
              yield anyWriteId ]
        |> Set.ofList

    let closed =
        Fuaran.Core.Propagation.dirtyFromChangedIds graph.Dependencies seeds
        |> Seq.choose (fun id -> Map.tryFind id graph.Sites)
        |> Set.ofSeq

    let unlocated = anyState && graph.OpaqueReader

    let because =
        [ if anyState then
              yield! graph.ComputedReaders |> List.map OpaqueRead.Computed
          if anyWrite then
              yield! graph.UnnamedLiveReaders |> List.map OpaqueRead.LiveSourceUnnamed
          if unlocated then
              yield OpaqueRead.Unlocated ]

    let sites =
        if unlocated then
            Set.unionMany
                [ closed
                  graph.Sites |> Map.toSeq |> Seq.map snd |> Set.ofSeq
                  graph.Nodes |> Set.map Site.Reader ]
        else
            closed

    match because with
    | [] -> Dirty.Exact sites
    | _ -> Dirty.Conservative(sites, because)

/// The sites of a verdict, whichever it is.
let sitesOf (verdict: Dirty) : Set<Site> =
    match verdict with
    | Dirty.Exact sites
    | Dirty.Conservative(sites, _) -> sites

/// The reader node ids of a site set — a `Site.Transform` projects to its
/// reader. The shape the Phase 1479 cone is stated in.
let readersOf (sites: Set<Site>) : Set<string> =
    sites
    |> Set.map (function
        | Site.Reader r -> r
        | Site.Transform(r, _, _, _) -> r)
