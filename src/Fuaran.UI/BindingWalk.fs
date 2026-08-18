module Fuaran.UI.BindingWalk

// ============================================================================
//  Cross-tree binding collection for validation (Phase 427; the shared walk
//  Phases 421 / 424 / 425 deferred).
//
//  `PreEmitValidate` needs tree-wide facts the per-node checks can't see: which
//  filters / state keys / selections / queries the tree READS (wherever a
//  `Binding<'T>` or `TextSource` appears, in any spec slot), and which filters
//  the tree DECLARES (Filters-node chips). This module is that single walk —
//  a validation-oriented projection of every binding usage, tagged with the
//  reading node's id.
//
//  FORWARD-COUPLING (the same discipline as the renderer's reactive
//  key-collection walk in `Fuaran.UI.Renderer/Render.fs`, which serves
//  render-time subscriptions and mirrors this slot coverage): a new
//  binding-bearing field on any spec — or a new `NodeKind` / `Binding` /
//  `TextSource` case — must extend BOTH walks, else its readers silently
//  escape validation here and reactive subscription there.
//
//  Fable-compatible — no reflection, no server-only API; pattern-matching
//  only (the walk reads key/name strings off each binding, staying generic
//  over `'T` with no type-erasure hazard).
// ============================================================================

open Fuaran.UI.Types
open Fuaran.Core

/// One observed binding usage — the validation-oriented projection of a
/// `Binding<'T>` read. `Query` carries its `dependsOn` filter names so the
/// consumption-union checks (decorative filter / dangling dependency) can be
/// derived without a second walk. A `Transform` param whose source is a
/// `Binding.Filter` surfaces as `TransformParamFilter` (a declared edge the
/// dangling check must verify against the declared chips), distinct from a
/// plain `Filter` value read (a display read a host may legitimately feed
/// without a chip). `TransformParam` records each declared param name with
/// whether the pipeline's `paramsOf` derivation actually references it.
[<RequireQualifiedAccess>]
type BindingUse =
    | State of key: string
    | Filter of name: string
    | Selection of targetNodeId: string
    | Query of name: string * dependsOn: string list
    | TransformParamFilter of name: string
    | TransformParam of name: string * referenced: bool
    /// A `Binding.Computed` read (Phase 932). The closure is handed the WHOLE
    /// `BindingContext.State` bag, so WHICH keys it reads is unknowable
    /// statically. Recorded as a usage rather than dropped, so a rule reasoning
    /// from the ABSENCE of a read can tell "nothing reads this key" apart from
    /// "this tree cannot be analysed" — the difference between a finding and a
    /// false accusation.
    | Computed

/// One observed usage tagged with the id of the node whose spec reads it.
type NodeBindingUse = { Reader: string; Use: BindingUse }

/// One observed wire-survivable `Action.Call` (Phase 428), collected from the
/// wire-survivable Action slots (`Button.OnClick` / `Form.OnSubmit` /
/// `Modal.OnDismiss`, recursing `Chain`). Closure-held actions are invisible
/// by construction — the walk sees what the wire sees.
type CallUse =
    { Reader: string
      Endpoint: string
      HasOnResult: bool
      Into: CallResultTarget option }

/// The State-channel projection **FUARAN098** runs on (Phase 932) — which keys
/// the tree WRITES with `Action.SetState`, and which it can be shown to READ.
///
/// **What counts as a READ, enumerated rather than assumed.** The rule reasons
/// from the ABSENCE of a read, so an under-broad definition does not merely miss
/// findings — it manufactures false ones, which is how a Warning-severity rule
/// gets suppressed and stops protecting anything. The eight surfaces:
///
///  1. `Binding.State(k, _)` in ANY binding-bearing slot, recursing exactly as
///     `usesOfBinding` does — through `Local.initialFrom`, `Format.source`,
///     `I18n` args, and a `Transform`'s `params[].From`. This is the obvious
///     case and by volume the overwhelming majority.
///  2. A `Switch`'s branch SELECTOR, `SwitchSpec.On` — a `Binding` since
///     Phase 768, not the `StateKey: string` several stale comments still
///     describe. A button writing the key a `Switch` selects on is the canonical
///     honest affordance, so missing this surface alone would false-warn on the
///     single most idiomatic shape in the language.
///  3. A `DataGrid`'s `SortStateKey` and `PageStateKey` — plain STRINGS, not
///     bindings, and genuinely read (`BindingResolver.readSortSlot` /
///     `readPageDescriptor`). `EditStateKey` is deliberately NOT here: it is a
///     write DESTINATION with no reader anywhere in the renderer, so counting it
///     would mask real defects.
///  4. A `FormField` whose `FormFieldKind` value slot is `None` — the Phase 694
///     auto-bind reads `Binding.State(field.Id, _)` with nothing in the tree to
///     see. Already covered by `usesOfFormFieldKind`'s implicit-use argument.
///  5. An `Action.SetState`'s own `valueFrom` binding: a write whose value
///     derives from another key READS that key.
///  6. A `StateBehaviour` subtree — `OnEmpty` / `OnLoading` are wire-encoded
///     child nodes that render in place of the body and may read anything.
///  7. A `FragmentArg.SlotArg` subtree passed through `FragmentRef.Args` or
///     `Mount.Inputs`.
///  8. `Accessibility.Label` / `Hidden`, `Drawing`'s `DrawStyle` colour
///     bindings and `Shape.Label` text — ordinary binding slots, listed because
///     the two tree walks in this estate have each historically missed one of
///     them while covering the other.
///
/// Where a surface could not be decided, it is counted AS A READ: over-counting
/// costs a missed finding, under-counting costs a false accusation, and only one
/// of those kills the rule. `Mount.Inputs` is the live instance — a guest renders
/// in an isolated `StateStore.forScope`, so its INTERIOR reads no host key, but a
/// `SlotArg` handed to it is host-authored and its scope at render time is not
/// settled by the type. It is counted.
type StateKeyFacts =
    {
        /// Every `Action.SetState` reachable from a wire-survivable action slot
        /// (`Button.OnClick` / `Form.OnSubmit` / `Modal.OnDismiss`, recursing
        /// `Chain`), as (writing node id, key). Closure-held handlers are
        /// invisible by construction — the walk sees what the wire sees.
        Writes: (string * string) list
        /// Every state key the tree can be SHOWN to read, per the eight surfaces
        /// above.
        Reads: Set<string>
        /// True when the tree holds a reader whose state access cannot be seen —
        /// a `Binding.Computed` closure (handed the whole state bag) or a
        /// `NodeKind.Custom` node (whose registered host renderer may read
        /// anything). Under either, the absence of a read PROVES nothing, so
        /// FUARAN098 stands down for the whole tree rather than guessing.
        OpaqueReader: bool
    }

/// The tree-wide facts `PreEmitValidate`'s cross-tree checks run on.
type TreeBindingFacts =
    {
        /// Every binding usage in the tree, reader-tagged.
        Uses: NodeBindingUse list
        /// Every declared filter chip: (owning Filters-node id, `FilterSpec.Name`).
        DeclaredFilters: (string * string) list
        /// Every wire-survivable `Action.Call` in the tree, reader-tagged (Phase 428).
        Calls: CallUse list
        /// Every node id in the tree, mapped to whether the node is a
        /// selection PRODUCER — a `Visualisation` kind (the grid's Phase 427
        /// default row-click write; charts/tables/maps via host closures).
        Nodes: Map<string, bool>
        /// The State-channel read/write projection FUARAN098 runs on (Phase 932).
        /// Held BESIDE `Uses` rather than folded into it: the consumption-union
        /// rules (FUARAN070–076) are tuned to the surfaces `Uses` covers today,
        /// and widening that set would newly fire five shipped Error-severity
        /// rules on trees that pass now — a behaviour change well outside an
        /// additive Warning rule's remit. The gaps in `Uses` are real and are
        /// filed as their own work; see `TIDY-UP.md`.
        StateKeys: StateKeyFacts
    }

/// Binding usages read by a single binding, recursing into a `Local` binding's
/// re-sync source, `I18n` `{arg}` sub-bindings, a `Format` binding's numeric
/// source, and a parameterised `Transform`'s param sources — the same
/// recursion contract as the renderer's reactive walk.
let rec usesOfBinding<'T> (binding: Binding<'T>) : BindingUse list =
    match binding with
    | Binding.State(key, _) -> [ BindingUse.State key ]
    | Binding.Filter(name, _) -> [ BindingUse.Filter name ]
    | Binding.Selection(nodeId, _, _, _) -> [ BindingUse.Selection nodeId ]
    | Binding.Query(name, _, dependsOn) -> [ BindingUse.Query(name, defaultArg dependsOn []) ]
    // Phase 765 — `Now` reads no node, state key, filter or query: the host
    // furnishes it once per render pass. It participates in no reactive edge,
    // so it contributes no usage (the `Computed` posture below).
    | Binding.Now _ -> []
    | Binding.Local(_, _, initialFrom, _, _) -> usesOfBinding initialFrom
    | Binding.I18n(_, Some args) -> args |> Map.toList |> List.collect (fun (_, ab) -> usesOfBinding<JVal> ab)
    | Binding.I18n(_, None) -> []
    | Binding.Format(source, _, _) -> usesOfBinding source
    | Binding.Transform(_, pipeline, parameters) ->
        // The pure `Transform.paramsOf` derivation (fuaran-core#77) names every
        // param the pipeline actually references — a declared `params` entry
        // outside it is dead weight (FUARAN076).
        let referenced = Fuaran.Core.Transform.paramsOf pipeline |> Set.ofList

        defaultArg parameters []
        |> List.collect (fun (p: TransformParam) ->
            let sourceUses =
                usesOfBinding p.From
                |> List.map (function
                    // A param's Filter source is the DECLARED filter→consumer
                    // edge (the 424 construct) — distinct from a plain value
                    // read for the dangling / consumption checks.
                    | BindingUse.Filter filterName -> BindingUse.TransformParamFilter filterName
                    | other -> other)

            BindingUse.TransformParam(p.Name, Set.contains p.Name referenced) :: sourceUses)
    // Phase 932 — `Computed` reads the whole state bag through a closure, so it
    // is an OPAQUE read, not an absent one. See `BindingUse.Computed`.
    | Binding.Computed _ -> [ BindingUse.Computed ]
    | Binding.Invoke _
    | Binding.Static _ -> []

/// Binding usages read by a `TextSource`. `Literal` carries none; `Bound`
/// defers to its binding; `TextSource.I18n` args are `JVal` literals.
let usesOfText (text: TextSource) : BindingUse list =
    match text with
    | TextSource.Bound b -> usesOfBinding b
    | TextSource.Literal _
    | TextSource.I18n _ -> []

let private usesOfTextOpt (text: TextSource option) : BindingUse list =
    match text with
    | Some t -> usesOfText t
    | None -> []

let private usesOfBindingOpt (binding: Binding<'T> option) : BindingUse list =
    match binding with
    | Some b -> usesOfBinding b
    | None -> []

let private usesOfFormFieldKind<'Msg> (implicitUse: BindingUse option) (kind: FormFieldKind<'Msg>) : BindingUse list =
    // Value slots are `option` since the swap (Phase 596 auto-bind — absence
    // is legal wire); constraints ride flat (min/max/step) rather than as the
    // retired constraint records.
    //
    // Phase 694 — a `None` value slot IS a read: the renderer substitutes the
    // context's auto-binding at render time (decode no longer synthesises it
    // into the tree), so the walker contributes `implicitUse` for absence —
    // `State(field id)` in a form, `Filter(name)` on a chip — keeping the
    // wiring lint and resume analysis semantically identical to the old
    // decode-synthesised shape.
    let usesOfValueSlot (v: Binding<'x> option) : BindingUse list =
        match v with
        | Some b -> usesOfBinding b
        | None -> Option.toList implicitUse

    match kind with
    | FormFieldKind.Text(v, _) -> usesOfValueSlot v
    | FormFieldKind.Number(v, _) -> usesOfValueSlot v
    | FormFieldKind.Checkbox(v, _) -> usesOfValueSlot v
    | FormFieldKind.Toggle(v, _) -> usesOfValueSlot v
    | FormFieldKind.TextArea(v, _, _) -> usesOfValueSlot v
    | FormFieldKind.RangedNumber(v, _, _, _, _) -> usesOfValueSlot v
    | FormFieldKind.Range(v, _, _, _, _) -> usesOfValueSlot v
    | FormFieldKind.Choice(opts, value, _) -> usesOfBinding opts @ usesOfValueSlot value
    | FormFieldKind.SegmentedChoice(opts, value, _, _) -> usesOfBinding opts @ usesOfValueSlot value
    | FormFieldKind.Date(v, _, _, _, _, _) -> usesOfValueSlot v
    | FormFieldKind.DateRange(v, _, _, _, _, _) -> usesOfValueSlot v

/// The `Action.Call`s reachable from a wire-survivable action value,
/// recursing `Chain` (Phase 428). Non-Call arms carry no fetch.
let rec callsOfAction<'Msg> (readerId: string) (action: Action<'Msg>) : CallUse list =
    match action with
    | Action.Call(endpoint, onResult, into) ->
        [ { Reader = readerId
            Endpoint = endpoint
            HasOnResult = onResult.IsSome
            Into = into } ]
    | Action.Chain actions -> actions |> List.collect (callsOfAction readerId)
    | Action.Dispatch _
    | Action.Notify _
    | Action.Navigate _
    | Action.SetState _
    | Action.AiTool _
    | Action.CommitLocal _
    | Action.WriteToClipboard _
    | Action.ReadFileBody _
    | Action.Invoke _ -> []


/// Collect the tree-wide binding facts for `node` (see `TreeBindingFacts`),
/// descending through layout children, error-boundary subtrees, and
/// fragment-decl bodies (`FragmentRef` carries no body; a `Mount` guest is an
/// opaque isolation boundary — both contribute their own node id only).
let collect<'Msg> (root: Node<'Msg>) : TreeBindingFacts =
    let uses = ResizeArray<NodeBindingUse>()
    let declaredFilters = ResizeArray<string * string>()
    let calls = ResizeArray<CallUse>()
    let nodes = System.Collections.Generic.Dictionary<string, bool>()

    // ── The Phase 932 State-channel projection (see `StateKeyFacts`) ──
    let stateReads = System.Collections.Generic.HashSet<string>()
    let stateWrites = ResizeArray<string * string>()
    let mutable opaqueReader = false

    /// Fold usages into the STATE projection only. Every read surface reaches
    /// this, including the ones deliberately kept out of `Uses`.
    let recordStateOf (found: BindingUse list) =
        for u in found do
            match u with
            | BindingUse.State k -> stateReads.Add k |> ignore
            | BindingUse.Computed -> opaqueReader <- true
            | _ -> ()

    // `inUses` is false while walking a subtree that contributes STATE facts but
    // must not widen `Uses` / `Calls` / `Nodes` — a `StateBehaviour` branch or a
    // `SlotArg` argument tree. Those subtrees were never walked here before, so
    // feeding them into the consumption-union checks would change five shipped
    // rules' verdicts as a side-effect of adding a Warning.
    let record (inUses: bool) (readerId: string) (found: BindingUse list) =
        recordStateOf found

        if inUses then
            for u in found do
                uses.Add { Reader = readerId; Use = u }

    /// Every `SetState` reachable from a wire-survivable action slot: the key is
    /// a WRITE, and a `valueFrom` deriving the written value is itself a READ.
    let rec recordStateAction (readerId: string) (action: Action<'Msg>) =
        match action with
        | Action.SetState(key, _, valueFrom) ->
            stateWrites.Add(readerId, key)

            match valueFrom with
            | Some b -> recordStateOf (usesOfBinding b)
            | None -> ()
        | Action.Chain actions -> actions |> List.iter (recordStateAction readerId)
        | _ -> ()

    let recordCalls (inUses: bool) (readerId: string) (action: Action<'Msg>) =
        recordStateAction readerId action

        if inUses then
            for c in callsOfAction readerId action do
                calls.Add c

    let rec walk (inUses: bool) (n: Node<'Msg>) =
        let readerId = n.Id

        let isProducer =
            match Kind.category n.Kind with
            | NodeCategory.Visualisation -> true
            | _ -> false

        if inUses then
            nodes[readerId] <- isProducer

        match n.Accessibility with
        | Some a -> record inUses readerId (usesOfBindingOpt a.Label @ usesOfBindingOpt a.Hidden)
        | None -> ()

        // A `StateBehaviour` branch is a wire-encoded child node rendered in
        // place of the body — a real reader the walk never descended into.
        match n.State with
        | Some sb ->
            sb.OnEmpty |> Option.iter (walk false)
            sb.OnLoading |> Option.iter (walk false)
        | None -> ()

        // Phase 692 — one exhaustive match over the flat vocabulary, where this
        // was four nested ones under the category envelope. Every arm yields
        // `(the bindings it reads, the children to walk)`; only the container
        // kinds have children, so the rest yield `[]`.
        let directUses, children =
            match n.Kind with
            // ── Layout ──
            | NodeKind.Box s -> usesOfTextOpt s.Heading, s.Children
            | NodeKind.SplitPanel s -> [], s.Children
            | NodeKind.SummaryList s -> usesOfTextOpt s.Heading, s.Children
            | NodeKind.Stepper s -> usesOfBinding s.ActiveStep, s.Children
            | NodeKind.Disclosure s -> (usesOfText s.Heading @ usesOfBinding s.Open), s.Children
            | NodeKind.Tabs s ->
                let headerUses =
                    match s.TabHeaders with
                    | Some headers ->
                        headers
                        |> List.collect (fun h -> usesOfText h.Label @ usesOfBindingOpt h.Disabled)
                    | None -> []

                (usesOfBinding s.ActiveIndex @ usesOfBindingOpt s.ActiveTag @ headerUses), s.Children
            | NodeKind.Modal s ->
                // Modal's OnDismiss is the wire-survivable Action slot (Phase 428).
                s.OnDismiss |> Option.iter (recordCalls inUses readerId)
                (usesOfTextOpt s.Heading @ usesOfBinding s.Open), s.Children
            | NodeKind.ScrollArea s -> [], s.Children
            // ── Display ──
            | NodeKind.Heading h -> usesOfText h.Text, []
            | NodeKind.Markdown m -> usesOfText m.Text, []
            | NodeKind.Metric k ->
                let uses =
                    usesOfText k.Label
                    @ usesOfBinding k.Value
                    @ usesOfBindingOpt k.Trend
                    @ usesOfTextOpt k.Subtext

                uses, []
            | NodeKind.Badge b -> usesOfText b.Label, []
            | NodeKind.Sparkline s -> usesOfBinding s.Source, []
            | NodeKind.Callout c -> usesOfTextOpt c.Heading @ usesOfText c.Body, []
            | NodeKind.Progress p -> usesOfBinding p.Fraction @ usesOfTextOpt p.Label @ usesOfTextOpt p.Caveat, []
            | NodeKind.Skeleton _ -> [], []
            | NodeKind.Icon _ -> [], []
            | NodeKind.LabelValueRow r -> usesOfText r.Label @ usesOfBinding r.Value @ usesOfTextOpt r.Help, []
            | NodeKind.Fact fa -> usesOfText fa.Label @ usesOfText fa.Value @ usesOfTextOpt fa.Help, []
            | NodeKind.Link l -> usesOfBinding l.Href @ usesOfText l.Label, []
            | NodeKind.Image i -> usesOfBinding i.Src @ usesOfText i.Alt, []
            | NodeKind.List l -> l.Items |> List.collect usesOfText, []
            | NodeKind.Toast t -> usesOfText t.Message @ usesOfBinding t.Open, []
            | NodeKind.CodeBlock _ -> [], []
            | NodeKind.Math _ -> [], []
            | NodeKind.Drawing d ->
                let uses =
                    // Phase 524 — geometry is static; the reactive slots are the
                    // DrawStyle colour bindings + Label text, walked recursively
                    // through Group nesting.
                    let usesOfDrawStyle (st: DrawStyle) =
                        usesOfBindingOpt st.Fill
                        @ usesOfBindingOpt st.Stroke
                        @ usesOfBindingOpt st.StrokeWidth
                        @ usesOfBindingOpt st.Opacity

                    let rec usesOfShape (sh: Shape) =
                        match sh with
                        | Shape.Group(children, st) -> (children |> List.collect usesOfShape) @ usesOfDrawStyle st
                        | Shape.Rectangle(_, _, _, _, _, st) -> usesOfDrawStyle st
                        | Shape.Line(_, _, _, _, st) -> usesOfDrawStyle st
                        | Shape.Polyline(_, st) -> usesOfDrawStyle st
                        | Shape.Polygon(_, st) -> usesOfDrawStyle st
                        | Shape.Curve(_, st) -> usesOfDrawStyle st
                        | Shape.Circle(_, _, _, st) -> usesOfDrawStyle st
                        | Shape.Ellipse(_, _, _, _, st) -> usesOfDrawStyle st
                        | Shape.Label(_, _, text, st) -> usesOfText text @ usesOfDrawStyle st

                    usesOfDrawStyle d.Style
                    @ (d.Shapes |> List.collect usesOfShape)
                    @ usesOfTextOpt d.Title
                    @ usesOfTextOpt d.Description

                uses, []
            // ── Input ──
            | NodeKind.Button b ->
                let uses =
                    // OnClick is a wire-survivable Action slot (Phase 428).
                    recordCalls inUses readerId b.OnClick
                    usesOfText b.Label @ usesOfTextOpt b.Tooltip @ usesOfBindingOpt b.Disabled

                uses, []
            | NodeKind.FileUpload fu -> usesOfText fu.Label @ usesOfBindingOpt fu.Disabled, []
            | NodeKind.Select s ->
                let uses =
                    usesOfText s.Label
                    @ usesOfBinding s.Source
                    @ usesOfBinding s.Value
                    @ usesOfBindingOpt s.Values
                    @ usesOfTextOpt s.Placeholder
                    @ usesOfBindingOpt s.Disabled

                uses, []
            | NodeKind.Form f ->
                let uses =
                    // OnSubmit is a wire-survivable Action slot (Phase 428).
                    recordCalls inUses readerId f.OnSubmit

                    let fieldUses =
                        f.Fields
                        |> List.collect (fun field ->
                            usesOfText field.Label
                            @ usesOfTextOpt field.Help
                            @ usesOfFormFieldKind (Some(BindingUse.State field.Id)) field.Kind)

                    usesOfText f.SubmitLabel @ usesOfBindingOpt f.Disabled @ fieldUses

                uses, []
            | NodeKind.Filters spec ->
                let uses =
                    if inUses then
                        for fs in spec.Items do
                            declaredFilters.Add(readerId, fs.Name)

                    spec.Items
                    |> List.collect (fun (fs: FilterSpec<_>) ->
                        usesOfText fs.Label
                        @ usesOfFormFieldKind (Some(BindingUse.Filter fs.Name)) fs.Kind)

                uses, []
            // ── Visualisation ──
            | NodeKind.DataGrid g ->
                // Phase 932 — `sortStateKey` / `pageStateKey` are plain STRINGS the
                // renderer READS (`readSortSlot` / `readPageDescriptor`), so they are
                // state reads with no `Binding` for the walk to see. `editStateKey` is
                // a write DESTINATION with no reader anywhere in the renderer, and
                // counting it would mask the very defect this rule looks for.
                g.SortStateKey |> Option.iter (fun k -> stateReads.Add k |> ignore)
                g.PageStateKey |> Option.iter (fun k -> stateReads.Add k |> ignore)

                let uses =
                    // Phase 393 — a static read-only grid carries its cells as `TextSource`
                    // in `StaticRows`; a data-bound grid carries a `Source` binding.
                    usesOfBinding g.Source
                    @ (match g.StaticRows with
                       | Some sr ->
                           (sr.Headers |> List.collect usesOfText)
                           @ (sr.Rows |> List.collect (List.collect usesOfText))
                       | None -> [])

                uses, []
            | NodeKind.Chart c -> usesOfBinding c.Source @ usesOfTextOpt c.Title, []
            | NodeKind.Map m -> usesOfBinding m.Source, []
            // ── Structural ──
            | NodeKind.ErrorBoundary spec -> [], [ spec.Child; spec.Fallback ]
            // Phase 932 — the branch SELECTOR is a BINDING since Phase 768; the comment
            // that stood here still described the `StateKey: string` field that change
            // retired. Its state reads are collected (a button writing the key a Switch
            // selects on is the canonical HONEST affordance, so missing this surface
            // alone would false-warn on the most idiomatic shape in the language), but
            // deliberately NOT into `Uses` — see `StateKeyFacts`. The case children +
            // default are walked so their own bindings are captured.
            | NodeKind.Switch spec ->
                recordStateOf (usesOfBinding spec.On)
                [], (spec.Cases |> List.map _.Child) @ [ spec.Default ]
            | NodeKind.FragmentDecl spec -> [], [ spec.Body ]
            // Custom props are JVal literals, not bindings; a FragmentRef carries
            // no body; a Mount guest owns its own scoped stores.
            | NodeKind.Custom _ ->
                // Phase 932 — a REGISTERED custom renderer is host code that may read
                // any state key, so the tree can no longer be shown to read nothing.
                opaqueReader <- true
                [], []
            | NodeKind.FragmentRef spec ->
                spec.Args |> Option.iter walkSlotArgs
                [], []
            | NodeKind.Mount spec ->
                // A guest's INTERIOR reads no host key (it renders under an isolated
                // `StateStore.forScope`), but a `SlotArg` handed to it is host-authored
                // and its render-time scope is not settled by the type. Counted as a
                // read: over-counting costs a missed finding, under-counting costs a
                // false accusation.
                spec.Inputs |> Option.iter walkSlotArgs
                [], []

        record inUses readerId directUses
        children |> List.iter (walk inUses)

    and walkSlotArgs (args: Map<string, FragmentArg<'Msg>>) =
        for KeyValue(_, arg) in args do
            match arg with
            | FragmentArg.SlotArg tree -> walk false tree
            | _ -> ()

    walk true root

    { Uses = List.ofSeq uses
      DeclaredFilters = List.ofSeq declaredFilters
      Calls = List.ofSeq calls
      Nodes = nodes |> Seq.fold (fun acc (KeyValue(k, v)) -> Map.add k v acc) Map.empty
      StateKeys =
        { Writes = List.ofSeq stateWrites
          Reads = Set.ofSeq stateReads
          OpaqueReader = opaqueReader } }
