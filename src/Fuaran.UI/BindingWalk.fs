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
    | Binding.Invoke _
    | Binding.Static _
    | Binding.Computed _ -> []

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

let private usesOfFormFieldKind<'Msg> (kind: FormFieldKind<'Msg>) : BindingUse list =
    // Value slots are `option` since the swap (Phase 596 auto-bind — absence
    // is legal wire); constraints ride flat (min/max/step) rather than as the
    // retired constraint records.
    match kind with
    | FormFieldKind.Text(v, _) -> usesOfBindingOpt v
    | FormFieldKind.Number(v, _) -> usesOfBindingOpt v
    | FormFieldKind.Checkbox(v, _) -> usesOfBindingOpt v
    | FormFieldKind.TextArea(v, _, _) -> usesOfBindingOpt v
    | FormFieldKind.RangedNumber(v, _, _, _, _) -> usesOfBindingOpt v
    | FormFieldKind.Range(v, _, _, _, _) -> usesOfBindingOpt v
    | FormFieldKind.Choice(opts, value, _) -> usesOfBinding opts @ usesOfBindingOpt value
    | FormFieldKind.SegmentedChoice(opts, value, _, _) -> usesOfBinding opts @ usesOfBindingOpt value
    | FormFieldKind.Date(v, _, _, _, _, _) -> usesOfBindingOpt v

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

    let record (readerId: string) (found: BindingUse list) =
        for u in found do
            uses.Add { Reader = readerId; Use = u }

    let recordCalls (readerId: string) (action: Action<'Msg>) =
        for c in callsOfAction readerId action do
            calls.Add c

    let rec walk (n: Node<'Msg>) =
        let (NodeId readerId) = n.Id

        let isProducer =
            match Kind.category n.Kind with
            | NodeCategory.Visualisation -> true
            | _ -> false

        nodes[readerId] <- isProducer

        match n.Accessibility with
        | Some a -> record readerId (usesOfBindingOpt a.Label @ usesOfBindingOpt a.Hidden)
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
                s.OnDismiss |> Option.iter (recordCalls readerId)
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
                    recordCalls readerId b.OnClick
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
                    recordCalls readerId f.OnSubmit

                    let fieldUses =
                        f.Fields
                        |> List.collect (fun field ->
                            usesOfText field.Label
                            @ usesOfTextOpt field.Help
                            @ usesOfFormFieldKind field.Kind)

                    usesOfText f.SubmitLabel @ usesOfBindingOpt f.Disabled @ fieldUses

                uses, []
            | NodeKind.Filters filters ->
                let uses =
                    for fs in filters do
                        declaredFilters.Add(readerId, fs.Name)

                    filters
                    |> List.collect (fun (fs: FilterSpec<_>) -> usesOfText fs.Label @ usesOfFormFieldKind fs.Kind)

                uses, []
            // ── Visualisation ──
            | NodeKind.DataGrid g ->
                let uses =
                    // Phase 393 — a static read-only grid carries its cells as `TextSource`
                    // in `StaticRows`; a data-bound grid carries a `Source` binding.
                    usesOfBinding g.Source
                    @ (match g.StaticRows with
                       | Some(headers, rows) ->
                           (headers |> List.collect usesOfText)
                           @ (rows |> List.collect (List.collect usesOfText))
                       | None -> [])

                uses, []
            | NodeKind.Chart c -> usesOfBinding c.Source @ usesOfTextOpt c.Title, []
            | NodeKind.Map m -> usesOfBinding m.Source, []
            // ── Structural ──
            | NodeKind.ErrorBoundary spec -> [], [ spec.Child; spec.Fallback ]
            // The StateKey is a literal string (not a Binding), so it records no
            // filter/query use here; the case children + default are walked so their
            // own bindings are captured.
            | NodeKind.Switch spec -> [], (spec.Cases |> List.map snd) @ [ spec.Default ]
            | NodeKind.FragmentDecl spec -> [], [ spec.Body ]
            // Custom props are JVal literals, not bindings; a FragmentRef carries
            // no body; a Mount guest owns its own scoped stores.
            | NodeKind.Custom _
            | NodeKind.FragmentRef _
            | NodeKind.Mount _ -> [], []

        record readerId directUses
        children |> List.iter walk

    walk root

    { Uses = List.ofSeq uses
      DeclaredFilters = List.ofSeq declaredFilters
      Calls = List.ofSeq calls
      Nodes = nodes |> Seq.fold (fun acc (KeyValue(k, v)) -> Map.add k v acc) Map.empty }
