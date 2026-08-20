module Fuaran.UI.StructuralQuery

// ============================================================================
//  Structural predicates over `Node` trees — the query surface for searching a
//  CORPUS of applications rather than a single one.
//
//  A Fuaran emission is data over a closed vocabulary, so a folder of
//  applications is a queryable set and "find every app with a data grid bound
//  to revenue" is an ordinary predicate rather than a text search. This module
//  is that predicate algebra: five term classes over the decoded tree —
//
//    kind       `Kind "DataGrid"`                 — the node vocabulary
//    binding    `BoundTo (Channel.Any, "revenue")` — what the node READS
//    shape      `Role "Dashboard"` + `ChildCount`  — containment structure
//    style      `Tone "Critical"`                  — the semantic tone surface
//    behaviour  `Dispatches (Act.SetState "page")` — what the node DOES
//
//  — closed under `And` / `Or` / `Not` and scoped by `HasDescendant` /
//  `HasAncestor`. Evaluation is a pure, total function producing a MATCH TRACE:
//  every hit carries the ids of the nodes whose own match was load-bearing, so a
//  caller can highlight the evidence and not merely the result.
//
//  FSharp.Core + the tier's own types only; no reflection, no server-only API —
//  the same evaluation runs in the browser under Fable and on .NET, and the two
//  agree because nothing here is environment-sensitive.
//
//  WHAT THIS DELIBERATELY DOES NOT DO. It is not a second signature-search
//  engine. Where a query is expressible as "which patterns produce kind K", the
//  shipped signature-searchable pattern bank already answers it against pattern
//  METADATA with no tree walk at all, and that surface stays the single
//  implementation: `Delegation` below classifies such a query and hands it out
//  through a caller-supplied search function rather than re-deriving the answer
//  here. This module owns only the tree-walk classes signatures cannot express.
//  (`Fuaran.UI` holds no reference to the signature-search package, so the
//  separation is structural rather than a matter of discipline.)
// ============================================================================

open Fuaran.UI.Types

// ── the query vocabulary ────────────────────────────────────────────────────

/// Numeric comparison for the shape class.
[<RequireQualifiedAccess>]
type Cmp =
    | Eq
    | Ne
    | Lt
    | Lte
    | Gt
    | Gte

/// Which reactive channel a `BoundTo` term reads. `Any` matches a name on any
/// channel — the chip form (`bound-to: revenue`), where the author knows the
/// name and not which channel carries it.
[<RequireQualifiedAccess>]
type Channel =
    | Any
    /// A `Binding.State` key.
    | State
    /// A `Binding.Filter` name — INCLUDING a `Transform` param fed from a
    /// filter, which the shared binding walk records separately (a declared
    /// edge) but which is the same read as far as a search is concerned.
    | Filter
    /// A `Binding.Query` name.
    | Query
    /// A `Binding.Selection` target node id.
    | Selection

/// Which action a `Dispatches` term looks for. Every discriminant accepts
/// `"*"` for "any value"; `Any` additionally accepts any action at all.
///
/// `Dispatch` is the one case whose discriminant a DECODED tree cannot supply:
/// the wire carries a message-slot sentinel, not a case name, so a decoded
/// `Action.Dispatch` has no label. `Dispatch "*"` therefore matches it and
/// `Dispatch "Submit"` does not — a query that cannot be answered returns no
/// match rather than a guess. Supply `Options.MessageLabel` to name the cases
/// of a typed, in-process `Node<'Msg>` and the specific form becomes answerable.
[<RequireQualifiedAccess>]
type Act =
    | Dispatch of case: string
    | Call of endpoint: string
    | Navigate of route: string
    | Invoke of capabilityId: string
    | Notify of channel: string
    | SetState of key: string
    | AiTool of toolName: string
    | WriteToClipboard
    | CommitLocal of nodeId: string
    | ReadFileBody of fileRef: string
    | Any

/// A structural predicate over one node of a tree. Evaluation is per-node
/// across the whole tree, so a bare term is already the "anywhere" form —
/// `Tone "Critical"` finds every critically-toned node wherever it sits.
/// `HasDescendant` / `HasAncestor` narrow that to a containment relation.
[<RequireQualifiedAccess>]
type Predicate =
    // ── kind ──
    /// The node's kind-tag (`Types.Kind.name`). `"DataGrid"` is accepted as a
    /// synonym of the tag `"Grid"` — see `canonicalKind`.
    | Kind of name: string
    /// The node's behavioural category (`Types.Kind.category`).
    | Category of category: NodeCategory
    // ── binding ──
    /// The node READS `name` on `channel`.
    | BoundTo of channel: Channel * name: string
    // ── shape ──
    /// The node is a container whose role is `name` (`Dashboard` / `Card` /
    /// `Group` / `Separator`).
    | Role of name: string
    /// The node's child count compares as stated.
    | ChildCount of comparison: Cmp * count: int
    // ── style ──
    /// The node carries `name` as a tone, on its semantic style or on the
    /// tone slot of its own spec.
    | Tone of name: string
    // ── behaviour ──
    /// The node carries a matching action in a wire-survivable action slot.
    | Dispatches of action: Act
    // ── combinators ──
    /// Conjunction. `And []` is the identity — it holds of every node.
    | And of Predicate list
    /// Disjunction. `Or []` is the identity — it holds of no node.
    | Or of Predicate list
    | Not of Predicate
    // ── scoping ──
    /// Some STRICT descendant matches.
    | HasDescendant of Predicate
    /// Some STRICT ancestor matches.
    | HasAncestor of Predicate

/// Evaluation options. `MessageLabel` names the case of a `Action.Dispatch`
/// payload for a typed in-process tree; a decoded tree has no case to name, so
/// the default returns `None` and only the `"*"` form of `Act.Dispatch` matches.
type Options<'Msg> = { MessageLabel: 'Msg -> string option }

[<RequireQualifiedAccess>]
module Options =

    /// The default: message payloads are opaque (the decoded-tree posture).
    let opaque<'Msg> () : Options<'Msg> = { MessageLabel = fun _ -> None }

    /// Name `Action.Dispatch` payloads with `label`.
    let labelled<'Msg> (label: 'Msg -> string option) : Options<'Msg> = { MessageLabel = label }

// ── results ─────────────────────────────────────────────────────────────────

/// One matched node plus its match trace: every OTHER node whose own match was
/// load-bearing for this one — the descendant that satisfied a `HasDescendant`,
/// the ancestor that satisfied a `HasAncestor`, the children a `ChildCount`
/// counted. Ids are in document order and distinct. A term that matches on the
/// node's own content (kind, tone, binding, action) contributes no witness:
/// the hit IS the evidence.
type Hit =
    { NodeId: string
      Witnesses: string list }

/// The outcome of evaluating one predicate over one tree.
type Result =
    {
        /// The matched nodes with their traces, in document order.
        Hits: Hit list
        /// The matched node ids.
        Matched: Set<string>
        /// Every id a caller should light up — the hits and their witnesses.
        Highlight: Set<string>
    }

[<RequireQualifiedAccess>]
module Result =

    /// The empty result — no node matched.
    let empty: Result =
        { Hits = []
          Matched = Set.empty
          Highlight = Set.empty }

    /// Did anything match?
    let any (r: Result) : bool = not (List.isEmpty r.Hits)

// ── the tree relation ───────────────────────────────────────────────────────

/// The children of `node`, in rendered position: container children, both
/// error-boundary arms, every switch case plus its default, and a fragment
/// declaration's body. This is the ONE containment relation the whole module
/// runs on — `ChildCount`, `HasDescendant` and `HasAncestor` all read it — so
/// the ancestor and descendant relations are exact duals by construction.
///
/// Two subtrees are deliberately NOT children, matching the traversal the rest
/// of the tier already uses: a node's `State` alternative arms (`OnEmpty` /
/// `OnLoading`, which render INSTEAD of the node) and a `Mount` guest's
/// interior (a separate scope, addressable in the guest's own tree). A node
/// held only in an alternative arm is therefore not reachable from the root by
/// this walk.
///
/// FORWARD-COUPLING: the match is exhaustive on purpose. A new `NodeKind` case
/// must declare its children here in the same change that adds the case, or the
/// build fails rather than the search silently missing a subtree.
let children (node: Node<'Msg>) : Node<'Msg> list =
    match node.Kind with
    | NodeKind.Box s -> s.Children
    | NodeKind.SplitPanel s -> s.Children
    | NodeKind.Tabs s -> s.Children
    | NodeKind.Stepper s -> s.Children
    | NodeKind.SummaryList s -> s.Children
    | NodeKind.Disclosure s -> s.Children
    | NodeKind.Modal s -> s.Children
    | NodeKind.ScrollArea s -> s.Children
    | NodeKind.ErrorBoundary s -> [ s.Child; s.Fallback ]
    | NodeKind.Switch s -> (s.Cases |> List.map _.Child) @ [ s.Default ]
    | NodeKind.FragmentDecl s -> [ s.Body ]
    | NodeKind.Heading _
    | NodeKind.Markdown _
    | NodeKind.Metric _
    | NodeKind.Badge _
    | NodeKind.Sparkline _
    | NodeKind.Callout _
    | NodeKind.Progress _
    | NodeKind.Skeleton _
    | NodeKind.Icon _
    | NodeKind.LabelValueRow _
    | NodeKind.Fact _
    | NodeKind.Link _
    | NodeKind.Image _
    | NodeKind.List _
    | NodeKind.Toast _
    | NodeKind.CodeBlock _
    | NodeKind.Math _
    | NodeKind.Drawing _
    | NodeKind.Form _
    | NodeKind.Filters _
    | NodeKind.Button _
    | NodeKind.FileUpload _
    | NodeKind.Select _
    | NodeKind.DataGrid _
    | NodeKind.Chart _
    | NodeKind.Map _
    | NodeKind.Custom _
    | NodeKind.FragmentRef _
    | NodeKind.Mount _ -> []

// ── per-node fact extraction ────────────────────────────────────────────────

/// The kind-tag vocabulary carries one documented divergence from the wire
/// discriminator: `DataGrid` tags as `"Grid"`. A query may name either, because
/// a reader writing `has: DataGrid` is naming the wire form they can see in the
/// JSON and should not have to know the tag vocabulary's history.
let canonicalKind (token: string) : string =
    if token = "DataGrid" then "Grid" else token

/// The container role of `node`, where it has one. Only `Box` carries a role.
let private roleOf (node: Node<'Msg>) : string option =
    match node.Kind with
    | NodeKind.Box s ->
        Some(
            match s.Role with
            | BoxRole.Dashboard -> "Dashboard"
            | BoxRole.Card -> "Card"
            | BoxRole.Group -> "Group"
            | BoxRole.Separator -> "Separator"
        )
    | _ -> None

let private toneName (tone: ToneVariant) : string =
    match tone with
    | ToneVariant.Default -> "Default"
    | ToneVariant.Subdued -> "Subdued"
    | ToneVariant.Brand -> "Brand"
    | ToneVariant.Success -> "Success"
    | ToneVariant.Warning -> "Warning"
    | ToneVariant.Critical -> "Critical"
    | ToneVariant.Info -> "Info"

/// Every tone `node` declares. Tone lives in two places and a search that read
/// only one would miss most of the corpus: the per-node `SemanticStyle`, and the
/// `Tone` slot six specs carry in their own right.
///
/// FORWARD-COUPLING: exhaustive on purpose — a new tone-bearing spec declares
/// itself here or the build fails. A `CellKind.Pill` tone is host-computed
/// (a closure over the row) and is invisible to a static walk by construction.
let private tonesOf (node: Node<'Msg>) : ToneVariant list =
    let fromStyle =
        match node.Style with
        | Some style -> [ style.Tone ]
        | None -> []

    let fromSpec =
        match node.Kind with
        | NodeKind.Icon s -> [ s.Tone ]
        | NodeKind.Callout s -> [ s.Tone ]
        | NodeKind.Progress s -> [ s.Tone ]
        | NodeKind.Metric s -> [ s.Tone ]
        | NodeKind.Fact s -> [ s.Tone ]
        | NodeKind.Toast s -> [ s.Tone ]
        | NodeKind.Box _
        | NodeKind.SplitPanel _
        | NodeKind.Tabs _
        | NodeKind.Stepper _
        | NodeKind.SummaryList _
        | NodeKind.Disclosure _
        | NodeKind.Modal _
        | NodeKind.ScrollArea _
        | NodeKind.Heading _
        | NodeKind.Markdown _
        | NodeKind.Badge _
        | NodeKind.Sparkline _
        | NodeKind.Skeleton _
        | NodeKind.LabelValueRow _
        | NodeKind.Link _
        | NodeKind.Image _
        | NodeKind.List _
        | NodeKind.CodeBlock _
        | NodeKind.Math _
        | NodeKind.Drawing _
        | NodeKind.Form _
        | NodeKind.Filters _
        | NodeKind.Button _
        | NodeKind.FileUpload _
        | NodeKind.Select _
        | NodeKind.DataGrid _
        | NodeKind.Chart _
        | NodeKind.Map _
        | NodeKind.Custom _
        | NodeKind.ErrorBoundary _
        | NodeKind.Switch _
        | NodeKind.FragmentDecl _
        | NodeKind.FragmentRef _
        | NodeKind.Mount _ -> []

    fromStyle @ fromSpec

/// One action a node carries, reduced to a tag plus the discriminant a query
/// can name. `Disc = None` means the action carries no nameable discriminant —
/// a decoded `Dispatch`, or a clipboard write.
type private Carried = { Tag: string; Disc: string option }

/// The actions reachable from one action value, recursing `Chain`.
///
/// FORWARD-COUPLING: exhaustive on purpose — a new `Action` case declares its
/// query surface here.
let rec private carriedOf (label: 'Msg -> string option) (action: Action<'Msg>) : Carried list =
    match action with
    | Action.Chain ops -> ops |> List.collect (carriedOf label)
    | Action.Dispatch msg -> [ { Tag = "Dispatch"; Disc = label msg } ]
    | Action.Call(endpoint, _, _) -> [ { Tag = "Call"; Disc = Some endpoint } ]
    | Action.Navigate route -> [ { Tag = "Navigate"; Disc = Some route } ]
    | Action.Invoke(capabilityId, _) ->
        [ { Tag = "Invoke"
            Disc = Some capabilityId } ]
    | Action.Notify(channel, _) -> [ { Tag = "Notify"; Disc = Some channel } ]
    | Action.SetState(key, _, _) -> [ { Tag = "SetState"; Disc = Some key } ]
    | Action.AiTool(toolName, _) -> [ { Tag = "AiTool"; Disc = Some toolName } ]
    | Action.WriteToClipboard _ ->
        [ { Tag = "WriteToClipboard"
            Disc = None } ]
    | Action.CommitLocal nodeId ->
        [ { Tag = "CommitLocal"
            Disc = Some nodeId } ]
    | Action.ReadFileBody(fileRef, _, _, _) ->
        [ { Tag = "ReadFileBody"
            Disc = Some fileRef } ]

/// The actions `node` carries in a WIRE-SURVIVABLE action slot — `Button.OnClick`,
/// `Form.OnSubmit`, `Modal.OnDismiss`, the same three the tier's shared binding
/// walk scans. Every other handler slot in the vocabulary is a closure
/// (`'a -> Action<'Msg>`), so it carries no action until it is invoked and a
/// static walk sees exactly what the wire sees.
let private actionsOf (label: 'Msg -> string option) (node: Node<'Msg>) : Carried list =
    match node.Kind with
    | NodeKind.Button s -> carriedOf label s.OnClick
    | NodeKind.Form s -> carriedOf label s.OnSubmit
    | NodeKind.Modal s ->
        match s.OnDismiss with
        | Some action -> carriedOf label action
        | None -> []
    | _ -> []

let private actMatches (query: Act) (carried: Carried) : bool =
    let tagMatches (tag: string) = carried.Tag = tag

    let discMatches (wanted: string) =
        wanted = "*"
        || (match carried.Disc with
            | Some actual -> actual = wanted
            | None -> false)

    match query with
    | Act.Any -> true
    | Act.Dispatch case -> tagMatches "Dispatch" && discMatches case
    | Act.Call endpoint -> tagMatches "Call" && discMatches endpoint
    | Act.Navigate route -> tagMatches "Navigate" && discMatches route
    | Act.Invoke capabilityId -> tagMatches "Invoke" && discMatches capabilityId
    | Act.Notify channel -> tagMatches "Notify" && discMatches channel
    | Act.SetState key -> tagMatches "SetState" && discMatches key
    | Act.AiTool toolName -> tagMatches "AiTool" && discMatches toolName
    | Act.WriteToClipboard -> tagMatches "WriteToClipboard"
    | Act.CommitLocal nodeId -> tagMatches "CommitLocal" && discMatches nodeId
    | Act.ReadFileBody fileRef -> tagMatches "ReadFileBody" && discMatches fileRef

/// Does one recorded binding usage answer a `BoundTo (channel, name)` term?
///
/// `Channel.Filter` accepts a `TransformParamFilter` as well as a plain
/// `Filter`: the shared walk keeps them apart because one is a declared edge
/// the validator must check and the other a display read, but both ARE the node
/// reading that filter, which is the only distinction a search cares about.
/// A `Computed` usage names nothing (its closure is handed the whole state bag)
/// and so answers no name.
let private useMatches (channel: Channel) (name: string) (usage: BindingWalk.BindingUse) : bool =
    let wants (c: Channel) = channel = Channel.Any || channel = c

    match usage with
    | BindingWalk.BindingUse.State key -> wants Channel.State && key = name
    | BindingWalk.BindingUse.Filter filterName -> wants Channel.Filter && filterName = name
    | BindingWalk.BindingUse.TransformParamFilter filterName -> wants Channel.Filter && filterName = name
    | BindingWalk.BindingUse.Query(queryName, _) -> wants Channel.Query && queryName = name
    | BindingWalk.BindingUse.Selection targetNodeId -> wants Channel.Selection && targetNodeId = name
    | BindingWalk.BindingUse.TransformParam _
    | BindingWalk.BindingUse.Computed -> false

// ── the tree index ──────────────────────────────────────────────────────────

/// The tree flattened once, in document (pre-order) order, with every fact the
/// term classes read precomputed. Nodes are addressed by POSITION rather than
/// id: ids are what a caller is told, positions are what the algebra computes
/// over, so a tree that carries a duplicate id (which the pre-emit validator
/// rejects, but a search must survive) still has an exact ancestor relation.
type private Ix<'Msg> =
    { Nodes: Node<'Msg>[]
      Kids: int list[]
      Descendants: int list[]
      Ancestors: int list[]
      Uses: BindingWalk.BindingUse list[]
      Actions: Carried list[] }

let private index (options: Options<'Msg>) (root: Node<'Msg>) : Ix<'Msg> =
    let nodes = ResizeArray<Node<'Msg>>()
    let kids = ResizeArray<int list>()
    let parent = ResizeArray<int>()

    let rec walk (parentIx: int) (node: Node<'Msg>) : int =
        let self = nodes.Count
        nodes.Add node
        kids.Add []
        parent.Add parentIx
        kids[self] <- children node |> List.map (walk self)
        self

    walk -1 root |> ignore

    let count = nodes.Count

    // Descendants: pre-order, strict. Computed by folding children's answers,
    // which are already complete because a child's index always exceeds its
    // parent's.
    let descendants = Array.create count []

    for i in count - 1 .. -1 .. 0 do
        descendants[i] <- kids[i] |> List.collect (fun c -> c :: descendants[c])

    let ancestors = Array.create count []

    for i in 0 .. count - 1 do
        let p = parent[i]
        ancestors[i] <- if p < 0 then [] else p :: ancestors[p]

    // Binding usages come from the tier's own cross-tree binding walk rather
    // than a second traversal of the spec vocabulary: one walk, already
    // forward-coupled to every binding-bearing slot. It is reader-ID-keyed, so
    // a duplicate id attributes to both holders — the only place in this module
    // where positions cannot rescue the ambiguity.
    let facts = BindingWalk.collect root

    let usesById =
        facts.Uses
        |> List.groupBy _.Reader
        |> List.map (fun (reader, group) -> reader, group |> List.map _.Use)
        |> Map.ofList

    { Nodes = nodes.ToArray()
      Kids = kids.ToArray()
      Descendants = descendants
      Ancestors = ancestors
      Uses =
        Array.init count (fun i ->
            match Map.tryFind nodes[i].Id usesById with
            | Some uses -> uses
            | None -> [])
      Actions = Array.init count (fun i -> actionsOf options.MessageLabel nodes[i]) }

// ── evaluation ──────────────────────────────────────────────────────────────

/// One node's verdict for one predicate: did it match, and which OTHER nodes
/// carried the match.
type private Verdict = { Hit: bool; Witness: int list }

let private missed = { Hit = false; Witness = [] }

let private compare (comparison: Cmp) (actual: int) (expected: int) : bool =
    match comparison with
    | Cmp.Eq -> actual = expected
    | Cmp.Ne -> actual <> expected
    | Cmp.Lt -> actual < expected
    | Cmp.Lte -> actual <= expected
    | Cmp.Gt -> actual > expected
    | Cmp.Gte -> actual >= expected

/// Evaluate one predicate against EVERY node at once, bottom-up over
/// sub-predicates. Each sub-predicate is evaluated exactly once for the whole
/// tree, so a scoping term is a lookup against an already-computed vector
/// rather than a nested re-walk.
let rec private vector (ix: Ix<'Msg>) (predicate: Predicate) : Verdict[] =
    let count = ix.Nodes.Length

    let per (holds: int -> bool) =
        Array.init count (fun i -> if holds i then { Hit = true; Witness = [] } else missed)

    match predicate with
    | Predicate.Kind name ->
        let wanted = canonicalKind name
        per (fun i -> Kind.name ix.Nodes[i].Kind = wanted)
    | Predicate.Category category -> per (fun i -> Kind.category ix.Nodes[i].Kind = category)
    | Predicate.Role name -> per (fun i -> roleOf ix.Nodes[i] = Some name)
    | Predicate.Tone name -> per (fun i -> tonesOf ix.Nodes[i] |> List.exists (fun tone -> toneName tone = name))
    | Predicate.BoundTo(channel, name) -> per (fun i -> ix.Uses[i] |> List.exists (useMatches channel name))
    | Predicate.Dispatches action -> per (fun i -> ix.Actions[i] |> List.exists (actMatches action))
    | Predicate.ChildCount(comparison, count') ->
        Array.init count (fun i ->
            let kids = ix.Kids[i]

            if compare comparison (List.length kids) count' then
                { Hit = true; Witness = kids }
            else
                missed)
    | Predicate.And parts ->
        let vectors = parts |> List.map (vector ix)

        Array.init count (fun i ->
            if vectors |> List.forall (fun v -> v[i].Hit) then
                { Hit = true
                  Witness = vectors |> List.collect (fun v -> v[i].Witness) }
            else
                missed)
    | Predicate.Or parts ->
        let vectors = parts |> List.map (vector ix)

        Array.init count (fun i ->
            let holding = vectors |> List.filter (fun v -> v[i].Hit)

            if List.isEmpty holding then
                missed
            else
                { Hit = true
                  Witness = holding |> List.collect (fun v -> v[i].Witness) })
    | Predicate.Not inner ->
        let v = vector ix inner
        // A negated match has no positive evidence to point at, so it carries
        // no witness — the absence is the finding.
        Array.init count (fun i -> if v[i].Hit then missed else { Hit = true; Witness = [] })
    | Predicate.HasDescendant inner ->
        let v = vector ix inner

        Array.init count (fun i ->
            let found = ix.Descendants[i] |> List.filter (fun d -> v[d].Hit)

            if List.isEmpty found then
                missed
            else
                { Hit = true
                  Witness = found @ (found |> List.collect (fun d -> v[d].Witness)) })
    | Predicate.HasAncestor inner ->
        let v = vector ix inner

        Array.init count (fun i ->
            let found = ix.Ancestors[i] |> List.filter (fun a -> v[a].Hit)

            if List.isEmpty found then
                missed
            else
                { Hit = true
                  Witness = found @ (found |> List.collect (fun a -> v[a].Witness)) })

/// Evaluate `predicate` over every node of `tree`, with match traces.
let evaluateWith (options: Options<'Msg>) (predicate: Predicate) (tree: Node<'Msg>) : Result =
    let ix = index options tree
    let verdicts = vector ix predicate

    let hits =
        [ for i in 0 .. ix.Nodes.Length - 1 do
              if verdicts[i].Hit then
                  { NodeId = ix.Nodes[i].Id
                    Witnesses =
                      verdicts[i].Witness
                      |> List.distinct
                      |> List.sort
                      |> List.map (fun w -> ix.Nodes[w].Id)
                      |> List.distinct } ]

    { Hits = hits
      Matched = hits |> List.map _.NodeId |> Set.ofList
      Highlight = hits |> List.collect (fun h -> h.NodeId :: h.Witnesses) |> Set.ofList }

/// Evaluate `predicate` over `tree`, treating message payloads as opaque — the
/// decoded-tree posture.
let evaluate (predicate: Predicate) (tree: Node<'Msg>) : Result =
    evaluateWith (Options.opaque ()) predicate tree

/// Evaluate across a corpus of trees, keeping only the entries that matched —
/// "which of these applications answer this query, and where".
let evaluateCorpusWith
    (options: Options<'Msg>)
    (predicate: Predicate)
    (corpus: (string * Node<'Msg>) list)
    : (string * Result) list =
    corpus
    |> List.map (fun (key, tree) -> key, evaluateWith options predicate tree)
    |> List.filter (snd >> Result.any)

/// `evaluateCorpusWith` with opaque message payloads.
let evaluateCorpus (predicate: Predicate) (corpus: (string * Node<'Msg>) list) : (string * Result) list =
    evaluateCorpusWith (Options.opaque ()) predicate corpus

// ── the chip vocabulary ─────────────────────────────────────────────────────

/// The five composable query chips, in the words a query bar uses. Each is a
/// thin naming of the algebra above — the algebra is the contract, these are
/// the labels.
[<RequireQualifiedAccess>]
module Chip =

    /// `has: DataGrid` — the tree contains a node of this kind.
    let has (kind: string) : Predicate = Predicate.Kind kind

    /// `bound-to: revenue` — the node reads this name on any reactive channel.
    let boundTo (name: string) : Predicate = Predicate.BoundTo(Channel.Any, name)

    /// `children-of: Dashboard >= 3` — a container of this role holding at
    /// least this many children.
    let childrenOf (role: string) (atLeast: int) : Predicate =
        Predicate.And [ Predicate.Role role; Predicate.ChildCount(Cmp.Gte, atLeast) ]

    /// `tone: Critical anywhere` — any node carrying this tone. Evaluation is
    /// already per-node across the whole tree, so "anywhere" is the plain form.
    let tone (name: string) : Predicate = Predicate.Tone name

    /// `dispatches: Submit` — the node dispatches this message case. See
    /// `Act.Dispatch` on why a decoded tree answers only the `"*"` form.
    let dispatches (case: string) : Predicate = Predicate.Dispatches(Act.Dispatch case)

// ── composition with the signature-searchable pattern bank ──────────────────

/// Routing a query to the SHIPPED signature-search surface instead of walking
/// trees.
///
/// The pattern bank already answers "which known patterns produce kind K" as a
/// deterministic, total lookup over pattern signatures — no tree, no corpus, no
/// walk. Re-deriving that here would be a second implementation of a shipped
/// capability, and the two would drift. So this module classifies rather than
/// competes: `tryRoute` decides whether a predicate is expressible in the
/// bank's own vocabulary, and `tryVia` hands it out to a caller-supplied
/// binding of that surface. Everything the bank cannot express — the binding,
/// shape, style, behaviour and scoping classes, all of which are facts about a
/// tree's interior rather than about a pattern's signature — falls to
/// `evaluate` above.
///
/// The seam is caller-supplied on purpose, and the reason is structural rather
/// than stylistic: the signature-search package sits ABOVE this one (it
/// references the tier, not the reverse), so this module cannot call it
/// directly and therefore cannot quietly grow a copy of it. `Fuaran.UI` holds
/// no reference to the artifact-function registry at all.
[<RequireQualifiedAccess>]
module Delegation =

    /// A query the signature bank can answer: the node kind to produce, in the
    /// bank's own kind-tag vocabulary.
    type Route = { Produce: string }

    /// How a predicate should be answered.
    [<RequireQualifiedAccess>]
    type Plan =
        /// Signature-expressible — ask the pattern bank.
        | Signature of Route
        /// Not signature-expressible — walk the trees with `evaluate`.
        | TreeWalk

    /// Every kind named by a purely kind-class predicate, or `None` if the
    /// predicate says anything a signature cannot.
    let rec private kindsOf (predicate: Predicate) : string list option =
        match predicate with
        | Predicate.Kind name -> Some [ canonicalKind name ]
        | Predicate.And parts ->
            (Some [], parts)
            ||> List.fold (fun acc part ->
                match acc, kindsOf part with
                | Some found, Some more -> Some(found @ more)
                | _ -> None)
        | Predicate.Category _
        | Predicate.BoundTo _
        | Predicate.Role _
        | Predicate.ChildCount _
        | Predicate.Tone _
        | Predicate.Dispatches _
        | Predicate.Or _
        | Predicate.Not _
        | Predicate.HasDescendant _
        | Predicate.HasAncestor _ -> None

    /// The route for a signature-expressible predicate, or `None`.
    ///
    /// A predicate qualifies when it says exactly one thing and that thing is a
    /// produced kind: `Kind "DataGrid"`, or a conjunction naming that same kind
    /// more than once. A conjunction naming two DIFFERENT kinds is not a
    /// signature query — no single pattern produces both — and is deliberately
    /// NOT reported as unsatisfiable here: that is a judgement about the
    /// corpus, and the tree walk is where corpus judgements are made.
    let tryRoute (predicate: Predicate) : Route option =
        match kindsOf predicate with
        | Some kinds ->
            match kinds |> List.distinct with
            | [ single ] -> Some { Produce = single }
            | _ -> None
        | None -> None

    /// Classify `predicate` — the decision, without acting on it.
    let plan (predicate: Predicate) : Plan =
        match tryRoute predicate with
        | Some route -> Plan.Signature route
        | None -> Plan.TreeWalk

    /// Answer `predicate` through `search` when it is signature-expressible;
    /// `None` when it is not, which is the caller's cue to `evaluate` instead.
    /// `search` is the caller's binding of the shipped signature-search surface
    /// — this module never implements one.
    let tryVia (search: Route -> 'result list) (predicate: Predicate) : 'result list option =
        tryRoute predicate |> Option.map search
