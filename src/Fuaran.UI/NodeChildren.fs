module Fuaran.UI.NodeChildren

// ============================================================================
//  What a node's children are — the ONE enumeration of the parent/child
//  relation, with the reach a caller wants stated rather than re-derived.
//
//  The relation used to be written by hand in every walker that needed it, and
//  the copies disagreed: one listed eight kinds and fell through to `[]`, so an
//  island under a `Modal` never got its hydrate payload; one read only an error
//  boundary's `Child`, so a fragment declared in its `Fallback` never resolved
//  on the server; one probed an incoming subtree through the container lists
//  alone, so a `Switch` whose case reused an existing id was accepted. Each
//  copy was a reasonable answer to a slightly different question, and nothing
//  said which question it was answering.
//
//  So this module names the questions. Every position a node holds another
//  node in has a CLASS (`Position`), and a caller asks for the classes it means
//  (`Reach`). The match below is the only place a `NodeKind` is taken apart to
//  find its children; a census in the test suite fails when another one
//  appears.
//
//  FSharp.Core + the tier's own types only — this runs under Fable in the
//  browser renderer as well as on .NET.
// ============================================================================

open Fuaran.UI.Types

/// The kind of place one node holds another in.
[<RequireQualifiedAccess>]
type Position =
    /// A container's ordered child list (`Box`, `SplitPanel`, `Tabs`, `Stepper`,
    /// `SummaryList`, `Disclosure`, `Modal`, `ScrollArea`), and a `FragmentDecl`'s
    /// body. The surface the structural ops edit: an insert appends to it, a
    /// remove filters it, a reorder permutes it.
    | Ordered
    /// A node the KIND holds in a named arm: `ErrorBoundary.child` /
    /// `ErrorBoundary.fallback`, every `Switch` case child and its default.
    | Arm
    /// The node envelope's `state.onLoading` / `state.onEmpty` alternatives,
    /// which render INSTEAD of the node.
    | StateArm
    /// The node envelope's author-declared `fallback`, which renders instead of
    /// the node on a reader that cannot render it.
    | Fallback
    /// A subtree passed as an argument: a `FragmentRef`'s slot arguments and a
    /// `Mount`'s slot inputs.
    | Argument

/// The position classes a walk descends through.
type Reach = Set<Position>

[<RequireQualifiedAccess>]
module Reach =

    /// The ordered child lists alone — what the structural ops edit.
    let structural: Reach = set [ Position.Ordered ]

    /// Every node the node's own KIND holds: the ordered lists plus the arms.
    /// The tree as the vocabulary shapes it, and the relation the structural
    /// query, the island and resume walks, the fragment registries and the
    /// lints run on. It deliberately leaves out the envelope alternatives
    /// (which render INSTEAD of the node) and slot arguments (which belong to
    /// the scope they are passed into).
    let kindHeld: Reach = set [ Position.Ordered; Position.Arm ]

    /// What a fragment body's slot substitution and hygienic renaming descend
    /// through: the kind-held positions plus the two `state` alternatives.
    /// The envelope `fallback` and slot arguments are outside it today; that
    /// boundary is stated here so it is one decision rather than two copies.
    let fragmentScope: Reach = set [ Position.Ordered; Position.Arm; Position.StateArm ]

    /// Every position a node holds another node in — structural plus keyed. The
    /// surface id uniqueness, node lookup and the wire bounds quantify over.
    let keyed: Reach =
        set
            [ Position.Ordered
              Position.Arm
              Position.StateArm
              Position.Fallback
              Position.Argument ]

    /// The keyed positions alone — everything `structural` does not reach. The
    /// surface a `KeyedWitness` declares.
    let nonStructural: Reach = Set.difference keyed structural

/// One position a node holds another node in.
type Slot<'Msg> =
    {
        /// The position's name in the §3.3 spelling an author would recognise
        /// (`Box.children[0]`, `state.onLoading`, `Switch.cases[0].child`,
        /// `Mount.inputs[header]`).
        Label: string
        Position: Position
        Node: Node<'Msg>
    }

/// What a node's kind holds, and how to put it back.
type private Held<'Msg> =
    /// An ordered child list. The rebuild accepts any length for a container
    /// and exactly one node for a `FragmentDecl` body (`None` otherwise).
    | Container of label: string * children: Node<'Msg> list * rebuild: (Node<'Msg> list -> NodeKind<'Msg> option)
    /// Arms or arguments, arity-preserving: the rebuild takes a list exactly as
    /// long as `slots`, position for position.
    | Positions of slots: (string * Position * Node<'Msg>) list * rebuild: (Node<'Msg> list -> NodeKind<'Msg>)
    | Nothing

/// The slot-argument entries of an argument bag, in key order.
let private slotArgs (bag: Map<string, FragmentArg<'Msg>> option) : (string * Node<'Msg>) list =
    bag
    |> Option.defaultValue Map.empty
    |> Map.toList
    |> List.choose (fun (k, v) ->
        match v with
        | FragmentArg.SlotArg n -> Some(k, n)
        | _ -> None)

/// Write `replacements` back over the slot-argument entries `slotArgs` read,
/// leaving every value argument alone. An absent or empty bag stays `None`
/// (omitted on the wire).
let private putSlotArgs
    (bag: Map<string, FragmentArg<'Msg>> option)
    (replacements: Node<'Msg> list)
    : Map<string, FragmentArg<'Msg>> option =
    let original = bag |> Option.defaultValue Map.empty

    let updated =
        List.zip (slotArgs bag) replacements
        |> List.fold (fun acc ((k, _), replaced) -> Map.add k (FragmentArg.SlotArg replaced) acc) original

    if Map.isEmpty updated then None else Some updated

/// THE match. Exhaustive on purpose: a new `NodeKind` case must declare what it
/// holds here in the same change that adds it, or the build fails rather than
/// every walk silently missing a subtree.
let private held (kind: NodeKind<'Msg>) : Held<'Msg> =
    let container (name: string) (children: Node<'Msg> list) (rebuild: Node<'Msg> list -> NodeKind<'Msg>) =
        Container(name + ".children", children, rebuild >> Some)

    match kind with
    | NodeKind.Box s -> container "Box" s.Children (fun cs -> NodeKind.Box { s with Children = cs })
    | NodeKind.SplitPanel s ->
        container "SplitPanel" s.Children (fun cs -> NodeKind.SplitPanel { s with Children = cs })
    | NodeKind.Tabs s -> container "Tabs" s.Children (fun cs -> NodeKind.Tabs { s with Children = cs })
    | NodeKind.Stepper s -> container "Stepper" s.Children (fun cs -> NodeKind.Stepper { s with Children = cs })
    | NodeKind.SummaryList s ->
        container "SummaryList" s.Children (fun cs -> NodeKind.SummaryList { s with Children = cs })
    | NodeKind.Disclosure s ->
        container "Disclosure" s.Children (fun cs -> NodeKind.Disclosure { s with Children = cs })
    | NodeKind.Modal s -> container "Modal" s.Children (fun cs -> NodeKind.Modal { s with Children = cs })
    | NodeKind.ScrollArea s ->
        container "ScrollArea" s.Children (fun cs -> NodeKind.ScrollArea { s with Children = cs })
    | NodeKind.FragmentDecl s ->
        Container(
            "FragmentDecl.body",
            [ s.Body ],
            fun cs ->
                match cs with
                | [ body ] -> Some(NodeKind.FragmentDecl { s with Body = body })
                | _ -> None
        )
    | NodeKind.ErrorBoundary s ->
        Positions(
            [ "ErrorBoundary.child", Position.Arm, s.Child
              "ErrorBoundary.fallback", Position.Arm, s.Fallback ],
            fun rs ->
                match rs with
                | [ child; fallback ] ->
                    NodeKind.ErrorBoundary
                        { s with
                            Child = child
                            Fallback = fallback }
                | _ -> kind
        )
    | NodeKind.Switch s ->
        // Cases in declaration order, then the default — so the rebuild splits
        // at the case count and cannot mis-pair a case with another's subtree.
        let caseCount = List.length s.Cases

        Positions(
            (s.Cases
             |> List.mapi (fun i c -> sprintf "Switch.cases[%d].child" i, Position.Arm, c.Child))
            @ [ "Switch.default", Position.Arm, s.Default ],
            fun rs ->
                if List.length rs = caseCount + 1 then
                    NodeKind.Switch
                        { s with
                            Cases =
                                List.map2
                                    (fun (c: SwitchCase<'Msg>) child -> { c with Child = child })
                                    s.Cases
                                    (List.truncate caseCount rs)
                            Default = List.item caseCount rs }
                else
                    kind
        )
    | NodeKind.FragmentRef s ->
        let args = slotArgs s.Args

        Positions(
            args
            |> List.map (fun (k, n) -> sprintf "FragmentRef.args[%s]" k, Position.Argument, n),
            fun rs ->
                if not (List.isEmpty args) && List.length rs = List.length args then
                    NodeKind.FragmentRef { s with Args = putSlotArgs s.Args rs }
                else
                    kind
        )
    | NodeKind.Mount s ->
        // The guest's own tree is a separate scope with its own ids; what the
        // host holds here is only the subtrees it passes IN.
        let inputs = slotArgs s.Inputs

        Positions(
            inputs
            |> List.map (fun (k, n) -> sprintf "Mount.inputs[%s]" k, Position.Argument, n),
            fun rs ->
                if not (List.isEmpty inputs) && List.length rs = List.length inputs then
                    NodeKind.Mount
                        { s with
                            Inputs = putSlotArgs s.Inputs rs }
                else
                    kind
        )
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
    | NodeKind.Media _
    | NodeKind.Embed _
    | NodeKind.List _
    | NodeKind.Tree _
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
    | NodeKind.Custom _ -> Nothing

// ── the structural surface ─────────────────────────────────────────────────

/// The ordered child list of a container kind, or `None` for a kind that holds
/// no ordered list (a leaf, or a kind holding only arms or arguments). `Some []`
/// is an EMPTY container, which the structural ops may still insert into.
let ordered (kind: NodeKind<'Msg>) : Node<'Msg> list option =
    match held kind with
    | Container(_, children, _) -> Some children
    | Positions _
    | Nothing -> None

/// The kind with exactly this ordered child list — any length for a container,
/// exactly one node for a `FragmentDecl` body — or `None` where the kind holds
/// no ordered list or the length is not one it can hold. The structural write.
let withOrdered (kind: NodeKind<'Msg>) (children: Node<'Msg> list) : NodeKind<'Msg> option =
    match held kind with
    | Container(_, _, rebuild) -> rebuild children
    | Positions _
    | Nothing -> None

// ── every position, in one canonical order ─────────────────────────────────

/// Every position `node` holds another node in, in ONE canonical order: the
/// ordered list, then `state.onLoading`, `state.onEmpty`, the envelope
/// `fallback`, then the kind's arms or arguments. (`StateBehaviour.OnError` is
/// a function from an error to a node, so there is no node to enumerate until
/// it is applied.)
let slots (node: Node<'Msg>) : Slot<'Msg> list =
    let envelope =
        [ match node.State |> Option.bind _.OnLoading with
          | Some n ->
              { Label = "state.onLoading"
                Position = Position.StateArm
                Node = n }
          | None -> ()
          match node.State |> Option.bind _.OnEmpty with
          | Some n ->
              { Label = "state.onEmpty"
                Position = Position.StateArm
                Node = n }
          | None -> ()
          match node.Fallback with
          | Some n ->
              { Label = "fallback"
                Position = Position.Fallback
                Node = n }
          | None -> () ]

    match held node.Kind with
    | Container(label, children, _) ->
        (children
         |> List.mapi (fun i c ->
             { Label = sprintf "%s[%d]" label i
               Position = Position.Ordered
               Node = c }))
        @ envelope
    | Positions(positions, _) ->
        envelope
        @ (positions
           |> List.map (fun (label, position, n) ->
               { Label = label
                 Position = position
                 Node = n }))
    | Nothing -> envelope

/// Rebuild `node` with `replacements` in exactly the positions `slots` names,
/// in its order. Arity-preserving: a list of any other length answers `node`
/// unchanged, since a rebuild is only ever handed the list it read.
let private replaceAll (node: Node<'Msg>) (replacements: Node<'Msg> list) : Node<'Msg> =
    let all = slots node

    if List.length replacements <> List.length all then
        node
    else
        let paired = List.zip all replacements

        let at (position: Position) =
            paired |> List.filter (fun (s, _) -> s.Position = position) |> List.map snd

        let pick (label: string) =
            paired |> List.tryFind (fun (s, _) -> s.Label = label) |> Option.map snd

        let kind =
            match held node.Kind with
            | Container(_, _, rebuild) -> rebuild (at Position.Ordered) |> Option.defaultValue node.Kind
            | Positions(positions, rebuild) ->
                let kindHeld =
                    paired
                    |> List.filter (fun (s, _) -> s.Position = Position.Arm || s.Position = Position.Argument)
                    |> List.map snd

                if List.length kindHeld = List.length positions then
                    rebuild kindHeld
                else
                    node.Kind
            | Nothing -> node.Kind

        { node with
            Kind = kind
            State =
                node.State
                |> Option.map (fun st ->
                    { st with
                        OnLoading = st.OnLoading |> Option.bind (fun _ -> pick "state.onLoading")
                        OnEmpty = st.OnEmpty |> Option.bind (fun _ -> pick "state.onEmpty") })
            Fallback = node.Fallback |> Option.bind (fun _ -> pick "fallback") }

// ── the reach-scoped reads and the lens ────────────────────────────────────

/// The positions of `node` inside `reach`, in the canonical order.
let slotsIn (reach: Reach) (node: Node<'Msg>) : Slot<'Msg> list =
    slots node |> List.filter (fun s -> Set.contains s.Position reach)

/// The children of `node` under `reach`, in the canonical order.
let children (reach: Reach) (node: Node<'Msg>) : Node<'Msg> list = slotsIn reach node |> List.map _.Node

/// The children of `node` under `reach` and the function that puts a list of
/// the same length back in exactly those positions, leaving every position
/// outside `reach` alone. Get and set are one function on purpose: as two
/// enumerations they could disagree, and the disagreement is silent — a walk
/// descends, rewrites, and the rewrite is dropped on the way out.
let lens (reach: Reach) (node: Node<'Msg>) : Node<'Msg> list * (Node<'Msg> list -> Node<'Msg>) =
    let all = slots node

    let picked =
        all
        |> List.indexed
        |> List.filter (fun (_, s) -> Set.contains s.Position reach)
        |> List.map fst

    let put (replacements: Node<'Msg> list) : Node<'Msg> =
        if List.length replacements <> List.length picked then
            node
        else
            let substitute = List.zip picked replacements |> Map.ofList

            all
            |> List.mapi (fun i s -> Map.tryFind i substitute |> Option.defaultValue s.Node)
            |> replaceAll node

    let nodes = all |> List.map _.Node |> Array.ofList
    picked |> List.map (fun i -> nodes[i]), put

/// Rebuild `node` with `replacements` in the positions `children reach node`
/// enumerates — the write half of `lens`.
let replace (reach: Reach) (node: Node<'Msg>) (replacements: Node<'Msg> list) : Node<'Msg> =
    (snd (lens reach node)) replacements
