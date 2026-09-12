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

    // ── the child lens (the ONE place a subtree slot is enumerated) ────────
    //
    // Every recursion in this module runs on this lens, so a slot it cannot see
    // is a slot that is not namespaced, not substituted, and not checked for
    // totality — three different symptoms of one omission. It used to see only
    // the eight straightforward containers plus the two error-boundary arms, so
    // a `Fragment.slot` marker inside a `Switch` case, inside a nested
    // `FragmentDecl` body, or inside a node's `OnLoading` / `OnEmpty` alternative
    // rendered as an unbound `FragmentRef` (the marker itself, drawn), and a
    // self-reference hidden in any of them defeated the totality refusal that
    // exists to stop unbounded expansion.
    //
    // GET AND SET ARE ONE FUNCTION on purpose. As two enumerations they can
    // disagree — one reads a slot the other cannot write back — and that
    // disagreement is silent: the traversal descends, rewrites, and the rewrite
    // is dropped on the way out. A lens returns the children and the rebuilder
    // that consumes exactly those children, in exactly that order.
    //
    // THE MATCH IS EXHAUSTIVE, matching `StructuralQuery.children`. A new
    // `NodeKind` case must declare its subtrees here in the same change that
    // adds the case, or this file stops compiling — which is the whole reason
    // the omission above was possible under the old wildcard.
    //
    // Two subtrees are deliberately NOT children, and both are stated rather
    // than forgotten:
    //   * a `Mount` guest's interior — a separate scope with its own ids, and
    //     hygienic namespacing across it would rewrite ids the guest owns;
    //   * `StateBehaviour.OnError`, which is a FUNCTION `exn -> Node` rather
    //     than a node. There is no subtree to visit until it is applied, so a
    //     slot marker inside one cannot be substituted by any traversal. The
    //     two alternative arms that ARE nodes (`OnLoading` / `OnEmpty`) are
    //     visited.

    let private childLens<'Msg> (node: Node<'Msg>) : Node<'Msg> list * (Node<'Msg> list -> Node<'Msg>) =
        let kindChildren, rebuildKind: Node<'Msg> list * (Node<'Msg> list -> NodeKind<'Msg>) =
            match node.Kind with
            | NodeKind.Box s -> s.Children, (fun cs -> NodeKind.Box { s with Children = cs })
            | NodeKind.SplitPanel s -> s.Children, (fun cs -> NodeKind.SplitPanel { s with Children = cs })
            | NodeKind.Tabs s -> s.Children, (fun cs -> NodeKind.Tabs { s with Children = cs })
            | NodeKind.Stepper s -> s.Children, (fun cs -> NodeKind.Stepper { s with Children = cs })
            | NodeKind.SummaryList s -> s.Children, (fun cs -> NodeKind.SummaryList { s with Children = cs })
            | NodeKind.Disclosure s -> s.Children, (fun cs -> NodeKind.Disclosure { s with Children = cs })
            | NodeKind.Modal s -> s.Children, (fun cs -> NodeKind.Modal { s with Children = cs })
            | NodeKind.ScrollArea s -> s.Children, (fun cs -> NodeKind.ScrollArea { s with Children = cs })
            | NodeKind.ErrorBoundary s ->
                [ s.Child; s.Fallback ],
                (fun cs ->
                    match cs with
                    | [ child; fallback ] -> NodeKind.ErrorBoundary { Child = child; Fallback = fallback }
                    | _ -> node.Kind)
            | NodeKind.Switch s ->
                // Cases in declaration order, then the default — so the
                // rebuilder can split at the case count and cannot mis-pair a
                // case with another case's subtree.
                (s.Cases |> List.map _.Child) @ [ s.Default ],
                (fun cs ->
                    let caseCount = List.length s.Cases

                    if List.length cs = caseCount + 1 then
                        NodeKind.Switch
                            { s with
                                Cases =
                                    List.map2
                                        (fun (c: SwitchCase<'Msg>) child -> { c with Child = child })
                                        s.Cases
                                        (List.truncate caseCount cs)
                                Default = List.item caseCount cs }
                    else
                        node.Kind)
            | NodeKind.FragmentDecl s ->
                [ s.Body ],
                (fun cs ->
                    match cs with
                    | [ body ] -> NodeKind.FragmentDecl { s with Body = body }
                    | _ -> node.Kind)
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
            | NodeKind.Custom _
            | NodeKind.FragmentRef _
            | NodeKind.Mount _ -> [], (fun _ -> node.Kind)

        // The alternative arms that are NODES. Present-only, so the rebuilder
        // puts back exactly the arms that were there — a `None` arm must not
        // become `Some` because the list happened to be long enough.
        let onLoading = node.State |> Option.bind _.OnLoading
        let onEmpty = node.State |> Option.bind _.OnEmpty
        let stateArms = [ onLoading; onEmpty ] |> List.choose id

        let all = kindChildren @ stateArms

        let rebuild (replacements: Node<'Msg> list) : Node<'Msg> =
            if List.length replacements <> List.length all then
                // A rebuilder is only ever called with the list this lens
                // returned. Answering the unchanged node rather than throwing
                // keeps a future misuse a no-op instead of a crash inside a
                // render.
                node
            else
                let kindCount = List.length kindChildren
                let kindPart = replacements |> List.truncate kindCount
                let statePart = replacements |> List.skip kindCount

                let newOnLoading, afterLoading =
                    match onLoading, statePart with
                    | Some _, head :: tail -> Some head, tail
                    | _ -> None, statePart

                let newOnEmpty =
                    match onEmpty, afterLoading with
                    | Some _, head :: _ -> Some head
                    | _ -> None

                { node with
                    Kind = rebuildKind kindPart
                    State =
                        node.State
                        |> Option.map (fun st ->
                            { st with
                                OnLoading = newOnLoading
                                OnEmpty = newOnEmpty }) }

        all, rebuild

    /// The slot name a node is an unbound marker for (a bare `FragmentRef`),
    /// when it is one.
    let private slotMarker<'Msg> (node: Node<'Msg>) : string option =
        match node.Kind with
        | NodeKind.FragmentRef spec -> Some spec.Name
        | _ -> None

    /// `true` when `node`'s subtree references `fragmentName` (a totality
    /// hazard if used as a slot argument).
    let rec private referencesFragment<'Msg> (fragmentName: string) (node: Node<'Msg>) : bool =
        match slotMarker node with
        | Some n when n = fragmentName -> true
        | _ -> fst (childLens node) |> List.exists (referencesFragment fragmentName)

    /// Rewrite every interior NodeId by `prefix` (hygienic namespacing of an
    /// inserted slot subtree).
    let rec private namespaceIds<'Msg> (prefix: string) (node: Node<'Msg>) : Node<'Msg> =
        let renamed = { node with Id = prefix + node.Id }
        let children, rebuild = childLens renamed
        rebuild (children |> List.map (namespaceIds prefix))

    /// Substitute bound slots in `body`, replacing each unbound `FragmentRef`
    /// slot-marker with its (namespaced) argument subtree.
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
            let children, rebuild = childLens body
            rebuild (children |> List.map (substituteSlots refPrefix boundSlots))

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
        let fragName = pf.Name

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
