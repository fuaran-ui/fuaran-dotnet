namespace Fuaran.UI.Renderer

open Fuaran.UI
open Fuaran.UI.Types

// ============================================================================
//  FragmentApply — renderer-side application of a parameterised fragment
//  (Phase 180). Binds a `ParamFragment`'s holes to arguments and produces the
//  bound tree:
//
//   - TREE SLOTS: a slot hole is marked in the body by an unbound
//     `FragmentRef` whose name is the slot name. `apply` replaces that node
//     with the bound subtree, NAMESPACED by the ref site's id (`<refId>.<id>`)
//     so two refs binding the same fragment with different args cannot capture
//     each other's ids (HYGIENE, invariant 2).
//   - VALUE HOLES: bound values are returned as a `ValueBindings` map keyed by
//     the hole address `<refId>.<holeName>` — the host seeds them (the body's
//     `Binding.State` reads resolve against this), so two refs don't share a
//     value-hole's state. No deep typed-binding rewrite is required.
//
//  TOTALITY (invariant 1): a slot argument whose subtree references the
//  fragment's OWN name (directly or transitively) would produce unbounded
//  expansion — `apply` refuses it. (A `Repeat` count is bounded at the type
//  level by `HoleValueSpace.IntRange`; see `Fuaran.UI.Fragment`.)
//
//  FGP 2: `FSharp.Core` + `Fuaran.UI` only — Fable-clean.
// ============================================================================

/// The result of applying a parameterised fragment: the bound tree + the
/// value-hole bindings the host seeds (keyed by hole address).
type FragmentApplication<'Msg> =
    { Tree: Node<'Msg>
      ValueBindings: Map<string, obj> }

module FragmentApply =

    let private rawId (NodeId s) : string = s

    // ── generic children lens (the only place the Layout arms are enumerated) ─

    let private getChildren<'Msg> (node: Node<'Msg>) : Node<'Msg> list option =
        match node.Kind with
        | NodeKind.Layout layout ->
            match layout with
            | NodeKind.Box s -> Some s.Children
            | NodeKind.SplitPanel s -> Some s.Children
            | NodeKind.Tabs s -> Some s.Children
            | NodeKind.Stepper s -> Some s.Children
            | NodeKind.SummaryList s -> Some s.Children
            | NodeKind.Disclosure s -> Some s.Children
            | NodeKind.Modal s -> Some s.Children
            | NodeKind.ScrollArea s -> Some s.Children
        | _ -> None

    let private setChildren<'Msg> (children: Node<'Msg> list) (node: Node<'Msg>) : Node<'Msg> =
        let kind =
            match node.Kind with
            | NodeKind.Layout layout ->
                let l =
                    match layout with
                    | NodeKind.Box s -> NodeKind.Box { s with Children = children }
                    | NodeKind.SplitPanel s -> NodeKind.SplitPanel { s with Children = children }
                    | NodeKind.Tabs s -> NodeKind.Tabs { s with Children = children }
                    | NodeKind.Stepper s -> NodeKind.Stepper { s with Children = children }
                    | NodeKind.SummaryList s -> NodeKind.SummaryList { s with Children = children }
                    | NodeKind.Disclosure s -> NodeKind.Disclosure { s with Children = children }
                    | NodeKind.Modal s -> NodeKind.Modal { s with Children = children }
                    | NodeKind.ScrollArea s -> NodeKind.ScrollArea { s with Children = children }

                NodeKind.Layout l
            | other -> other

        { node with Kind = kind }

    /// The slot name a node is an unbound marker for (a bare `FragmentRef`),
    /// when it is one.
    let private slotMarker<'Msg> (node: Node<'Msg>) : string option =
        match node.Kind with
        | NodeKind.FragmentRef spec ->
            let (FragmentId n) = spec.Name
            Some n
        | _ -> None

    /// `true` when `node`'s subtree references `fragmentName` (a totality
    /// hazard if used as a slot argument).
    let rec private referencesFragment<'Msg> (fragmentName: string) (node: Node<'Msg>) : bool =
        match slotMarker node with
        | Some n when n = fragmentName -> true
        | _ ->
            match getChildren node with
            | Some kids -> kids |> List.exists (referencesFragment fragmentName)
            | None ->
                match node.Kind with
                | NodeKind.ErrorBoundary spec ->
                    referencesFragment fragmentName spec.Child
                    || referencesFragment fragmentName spec.Fallback
                | _ -> false

    /// Rewrite every interior NodeId by `prefix` (hygienic namespacing of an
    /// inserted slot subtree).
    let rec private namespaceIds<'Msg> (prefix: string) (node: Node<'Msg>) : Node<'Msg> =
        let renamed =
            { node with
                Id = NodeId(prefix + rawId node.Id) }

        match getChildren renamed with
        | Some kids -> setChildren (kids |> List.map (namespaceIds prefix)) renamed
        | None ->
            match renamed.Kind with
            | NodeKind.ErrorBoundary spec ->
                { renamed with
                    Kind =
                        NodeKind.ErrorBoundary
                            { Child = namespaceIds prefix spec.Child
                              Fallback = namespaceIds prefix spec.Fallback } }
            | _ -> renamed

    /// Substitute bound slots in `body`, replacing each unbound `FragmentRef`
    /// slot-marker with its (namespaced) argument subtree. Recurses into
    /// containers + error boundaries.
    let rec private substituteSlots<'Msg>
        (refPrefix: string)
        (boundSlots: Map<string, Node<'Msg>>)
        (body: Node<'Msg>)
        : Node<'Msg> =
        match slotMarker body with
        | Some slot when Map.containsKey slot boundSlots ->
            // Replace the marker with the namespaced argument subtree.
            namespaceIds (refPrefix + ".") (Map.find slot boundSlots)
        | _ ->
            match getChildren body with
            | Some kids -> setChildren (kids |> List.map (substituteSlots refPrefix boundSlots)) body
            | None ->
                match body.Kind with
                | NodeKind.ErrorBoundary spec ->
                    { body with
                        Kind =
                            NodeKind.ErrorBoundary
                                { Child = substituteSlots refPrefix boundSlots spec.Child
                                  Fallback = substituteSlots refPrefix boundSlots spec.Fallback } }
                | _ -> body

    /// Apply `pf` at ref site `refId`, binding `valueArgs` (validated against
    /// each hole's value-space) and `slotArgs` (bound subtrees). All REQUIRED
    /// holes must be bound. Slot arguments that reference the fragment's own
    /// name are refused (totality). Returns the bound tree + the value-hole
    /// bindings (keyed `<refId>.<holeName>`) the host seeds.
    let apply<'Msg>
        (pf: ParamFragment<'Msg>)
        (refId: string)
        (valueArgs: Map<string, obj>)
        (slotArgs: Map<string, Node<'Msg>>)
        : Result<FragmentApplication<'Msg>, string> =
        let (FragmentId fragName) = pf.Name

        // 1. Validate value args against their hole spaces.
        let valueErr =
            valueArgs
            |> Map.toList
            |> List.tryPick (fun (n, v) ->
                match Fragment.validateValueArg pf n v with
                | Ok _ -> None
                | Error e -> Some(sprintf "value hole '%s': %s" n e))

        // 2. Every required hole must be bound (value or slot).
        let boundNames =
            Set.union (Set.ofSeq (Map.toSeq valueArgs |> Seq.map fst)) (Set.ofSeq (Map.toSeq slotArgs |> Seq.map fst))

        let missing =
            Fragment.requiredHoles pf |> List.filter (fun h -> not (boundNames.Contains h))

        // 3. Totality: no slot argument references the fragment itself.
        let totalityErr =
            slotArgs
            |> Map.toList
            |> List.tryPick (fun (n, sub) ->
                if referencesFragment fragName sub then
                    Some(
                        sprintf "slot '%s' argument references fragment '%s' — unbounded expansion refused" n fragName
                    )
                else
                    None)

        // 3b. Slot kind-constraint: a `HoleDecl.Slot(_, Some c)` requires its
        // bound subtree's kind-tag to equal `c` (the wire vocabulary — `Kind.name`).
        // An unconstrained slot (`None`) accepts any kind. Closes the
        // declared-but-unenforced gap: the constraint round-trips on the wire and
        // is now checked at bind time, so a mistyped slot fails here instead of
        // silently rendering the wrong shape.
        let slotKindErr =
            slotArgs
            |> Map.toList
            |> List.tryPick (fun (n, sub) ->
                match pf.Holes |> List.tryFind (fun h -> HoleDecl.name h = n) with
                | Some(HoleDecl.Slot(_, Some c)) ->
                    let actual = Kind.name sub.Kind

                    if actual = c then
                        None
                    else
                        Some(sprintf "slot '%s' requires a %s node but got %s" n c actual)
                | _ -> None)

        match valueErr, missing, totalityErr, slotKindErr with
        | Some e, _, _, _ -> Error e
        | _, (h :: _), _, _ -> Error(sprintf "required hole '%s' is unbound" h)
        | _, _, Some e, _ -> Error e
        | _, _, _, Some e -> Error e
        | None, [], None, None ->
            let tree = substituteSlots refId slotArgs pf.Body

            let valueBindings =
                valueArgs
                |> Map.toList
                |> List.map (fun (n, v) -> refId + "." + n, v)
                |> Map.ofList

            Ok
                { Tree = tree
                  ValueBindings = valueBindings }
