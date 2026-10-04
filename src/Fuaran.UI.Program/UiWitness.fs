/// The UI witness — the UI tier's fourteen-arm action union, its node tree, its
/// binding store, its tree-ops and its client effects, seen through the generic
/// core's witness contract (DECISIONS.md D18, docs/generic-tier.md §3).
///
/// Part of the `Fuaran.UI.Program` adapter package: the core packages reference
/// no UI-tier package, and this is the instantiation the existing suite, the
/// scenario corpus and the sample run through. Phase 1896 wrote it (parked in a
/// non-packable project) and Phase 1897 moved it here unchanged in shape. Every member below is the code the core ran before the cut, moved
/// rather than rewritten, so the program wire's bytes do not move with it.
module Fuaran.UI.Program.UiWitness

open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops.Introspect
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.BindingResolver
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.ServerDriven
open Fuaran.Program.Bounded
open Fuaran.Program.Runtime

/// The core's witness at the UI tier's types: all three axes (Phase 1974) —
/// the UI tier is the one domain with events, so it fills the dispatch axis,
/// and its state is a tree it walks.
type UiProgramWitness = FullWitness<Node<obj>, Action<obj>, Binding<JVal>, BindingSources, TreeOp<obj>, ClientEffect>

// ─── the expression witness ─────────────────────────────────────────────────

let private unresolvedI18n (key: string) : string = sprintf "unresolved i18n key '%s'" key

/// `ExprWitness.Resolve`: the tier's four-case resolution narrowed to the core's
/// three. A missing catalogue key becomes `Errored` with the message the fold
/// has always reported for it, so the refusal text is byte-identical.
let resolveExpr (s: BindingSources) (binding: Binding<JVal>) : ExprResolution =
    match BindingResolver.resolveJVal s binding with
    | Resolved jv -> ExprResolution.Resolved jv
    | NotResolved -> ExprResolution.NotResolved
    | Errored m -> ExprResolution.Errored m
    | I18nUnresolved k -> ExprResolution.Errored(unresolvedI18n k)

/// `ExprWitness.Uses`: what a binding reads, for the demanded projection — a
/// query slot or a state key. Every other use is the tier's own and is not a
/// demand this projection states.
let usesOfExpr (binding: Binding<JVal>) : BindingUse list =
    Fuaran.UI.BindingWalk.usesOfBinding binding
    |> List.choose (fun u ->
        match u with
        | Fuaran.UI.BindingWalk.BindingUse.Query(name, _) -> Some(BindingUse.Query name)
        | Fuaran.UI.BindingWalk.BindingUse.State key -> Some(BindingUse.State key)
        | _ -> None)

// ─── the action witness ─────────────────────────────────────────────────────

/// The effect kind an arm demands, named THROUGH `ClientEffect.kind` on a
/// canonical sample rather than as a string literal, so a demanded name and a
/// registry key cannot drift apart — the registry is keyed on exactly this
/// discriminator.
let private kindOf (sample: ClientEffect) : LeafDeclaration =
    { EffectKinds = [ ClientEffect.kind sample ]
      HostCalls = [] }

let private hostCall (channel: string) (name: string) : LeafDeclaration =
    { EffectKinds = []
      HostCalls = [ { Channel = channel; Name = name } ] }

let private nothing: LeafDeclaration = { EffectKinds = []; HostCalls = [] }

// `Action.Dispatch` is marked in-process-only upstream, so naming it raises
// FS0044. `view` and `lower` are TOTAL analyses of the closed union: they must
// name every case that exists, and naming one is not authoring one.
#nowarn "44"

/// `ActionWitness.View` — the total match over the closed fourteen-case union.
/// Four cases are control structure the core owns; the other ten are leaves,
/// each declaring what it may demand. No wildcard: a fifteenth arm fails to
/// compile here, which is the exhaustiveness check D18 moves into the adapter.
///
/// The other two effect arms (`PushState` / `Download`) are absent
/// deliberately: no `Action` produces them. They reach a host from the
/// navigation layer, which is not a program tree's to demand.
let view (action: Action<obj>) : ActionView<Action<obj>, Binding<JVal>> =
    match action with
    | Action.Chain actions -> ActionView.Sequence actions
    | Action.SetState(key, value, valueFrom) -> ActionView.Assign(key, value, valueFrom)
    | Action.Call(endpoint, _, into) -> ActionView.Call(endpoint, Option.isSome into)
    | Action.Navigate _ -> ActionView.Leaf(kindOf (ClientEffect.Navigate("", NavigateTarget.Self)))
    | Action.Focus _ -> ActionView.Leaf(kindOf (ClientEffect.Focus ""))
    | Action.WriteToClipboard _ -> ActionView.Leaf(kindOf (ClientEffect.WriteToClipboard ""))
    | Action.ReadFileBody _ -> ActionView.Leaf(kindOf (ClientEffect.ReadFileBody("", "")))
    | Action.Print -> ActionView.Leaf(kindOf ClientEffect.Print)
    | Action.Invoke(capabilityId, _) -> ActionView.Leaf(hostCall "Invoke" capabilityId)
    | Action.Notify(channel, _) -> ActionView.Leaf(hostCall "Notify" channel)
    | Action.AiTool(toolName, _) -> ActionView.Leaf(hostCall "AiTool" toolName)
    // `Confirm` demands nothing, including from its continuations — the bounded
    // interpreter answers it with a documented no-op, so neither continuation
    // can ever run here.
    | Action.Confirm _
    | Action.Dispatch _
    | Action.CommitLocal _ -> ActionView.Leaf nothing

/// `ActionWitness.Lower` — what each LEAF does at dispatch. The three control
/// cases never reach it through `view`; being total, it declines them.
let lower (nodeId: string) (action: Action<obj>) (s: BindingSources) : LeafOutcome<ClientEffect> =
    match action with
    // Inherently-browser arms → closure-free effects (no server form). The
    // route is sanitised before the effect is shipped; the host navigates with
    // its own router, so an unsafe scheme emitted here would land as a
    // client-side sink. A refusal emits no effect and one diagnostic, never a
    // silently-neutered `about:blank`.
    //
    // The PREDICATE is not this host's to choose: it is the tree wire
    // specification's renderer URL floor. §10.5 adds only the RESPONSE —
    // decline the action. `sanitizeUrl` is that floor and nothing stricter.
    //
    // The ROUTE is a `TextSource` resolved at DISPATCH TIME, and the floor
    // judges the RESOLVED string. AN UNRESOLVED ROUTE IS REFUSED: here
    // `sanitizeUrl ""` answers `Some ""`, and a `ClientEffect.Navigate("", …)` is
    // a real navigation to the current document, so every non-value the
    // resolver can answer with — unresolved, errored, a missing i18n key, and a
    // resolved null — is a refusal with a reason, and nothing is emitted.
    // `target` passes through untouched: it is not a destination.
    | Action.Navigate(route, target) ->
        let resolved: Result<string, string> =
            match route with
            | TextSource.Literal literal -> Ok literal
            | TextSource.Bound binding ->
                (match resolveScalarText s binding with
                 | Resolved value ->
                     if isNull (box value) then
                         Error "the route binding resolved to no value"
                     else
                         Ok value
                 | NotResolved -> Error "the route binding did not resolve to a value"
                 | Errored m -> Error m
                 | I18nUnresolved k -> Error(unresolvedI18n k))
            | TextSource.I18n(key, _) ->
                if Map.containsKey key s.I18n then
                    Ok(resolveTextSource s route)
                else
                    Error(unresolvedI18n key)

        match resolved with
        | Error reason -> LeafOutcome.Refuse(sprintf "%s — nothing was navigated to" reason)
        | Ok route ->
            match Sanitize.sanitizeUrl route with
            | Some safe -> LeafOutcome.Emit(ClientEffect.Navigate(safe, target))
            | None -> LeafOutcome.Refuse "route is not a safe URL"

    // The clipboard payload resolves at DISPATCH TIME through the same
    // resolver. A resolved-but-NULL value is copied as the empty string; a
    // binding that fails to resolve is REFUSED, because on a clipboard nobody
    // sees the gap. Which bindings resolve is the UI tier's answer, followed
    // rather than second-guessed: since its 0.86.0 a bare state binding at a
    // slot nothing has written is unresolved, and a declared default resolves.
    | Action.WriteToClipboard text ->
        let payload: Result<string, string> =
            match text with
            | TextSource.Literal literal -> Ok literal
            | TextSource.Bound binding ->
                (match resolveScalarText s binding with
                 | Resolved value -> Ok(if isNull (box value) then "" else value)
                 | NotResolved -> Error "the payload binding did not resolve to a value"
                 | Errored m -> Error m
                 | I18nUnresolved k -> Error(unresolvedI18n k))
            | TextSource.I18n(key, _) ->
                if Map.containsKey key s.I18n then
                    Ok(resolveTextSource s text)
                else
                    Error(unresolvedI18n key)

        match payload with
        | Ok value -> LeafOutcome.Emit(ClientEffect.WriteToClipboard value)
        | Error reason -> LeafOutcome.Refuse(sprintf "%s — nothing was written to the clipboard" reason)

    // Payload-free, and lowered rather than refused: printing is an act of the
    // machine the document is READ on.
    | Action.Print -> LeafOutcome.Emit ClientEffect.Print

    // The node id is a bare string the AUTHOR wrote, addressing a node in this
    // document: nothing to resolve and no floor to apply.
    | Action.Focus nodeIdToFocus -> LeafOutcome.Emit(ClientEffect.Focus nodeIdToFocus)

    // The `onRead` closure is the inert decode sentinel — NOT invoked here. The
    // emitted id is the node the EVENT came from (§5.2): the surface holds the
    // selected file against that node, and the returning event closes on it.
    | Action.ReadFileBody(_, _, encoding, _) ->
        let enc =
            match encoding with
            | FileReadEncoding.Text -> "Text"
            | FileReadEncoding.Base64 -> "Base64"
            | FileReadEncoding.DataUrl -> "DataUrl"

        LeafOutcome.Emit(ClientEffect.ReadFileBody(nodeId, enc))

    // Computational host arms with no store/DOM effect on the bounded path, and
    // the one the path has no return leg for: documented declines. A
    // confirmation is a ROUND TRIP the bounded placement has no channel for, so
    // NEITHER continuation runs — the fail-closed direction, and the only one
    // available. `Dispatch` has no `update` to fold a message through, and the
    // wire carries only the inert sentinel. `CommitLocal`'s flushed value is
    // applied as a state write by the loop before the commit is interpreted.
    | Action.Confirm _
    | Action.Notify _
    | Action.AiTool _
    | Action.Invoke _
    | Action.Dispatch _
    | Action.CommitLocal _ -> LeafOutcome.Decline

    // Control structure: never a leaf through `view`. Total, so it answers; it
    // answers the no-op.
    | Action.Chain _
    | Action.SetState _
    | Action.Call _ -> LeafOutcome.Decline

#warnon "44"

/// The action codec (K6): the tree codec's own encoder, spliced verbatim.
let encodeAction (action: Action<obj>) : JVal =
    Fuaran.UI.Generated.encodeActionJson action

/// The action decoder: through the one public entry point that reaches the tree
/// codec's action reader — a one-button carrier node — rather than a second
/// action reader here. The core applies the program's own refusals first.
let decodeAction (value: JVal) : Result<Action<obj>, WireRefusal> =
    let carrier =
        JObj
            [ "id", JStr "carrier"
              "kind",
              JObj
                  [ "$type", JStr "Button"
                    "label", JStr "carrier"
                    "onClick", value
                    "variant", JStr "Primary" ] ]

    match Fuaran.UI.Ops.JsonDecode.decodeNodeObj (Canon.render carrier) with
    | Error err ->
        ProgramWire.refuse RefusalClass.MalformedReferencedValue ("the action does not decode: " + string err.Code)
    | Ok node ->
        match node.Kind with
        | NodeKind.Button spec -> Ok spec.OnClick
        | _ -> ProgramWire.refuse RefusalClass.MalformedReferencedValue "the action carrier did not decode as expected"

// ─── the tree: walk and dispatch members ───────────────────────────────────

/// Substitute a binding with `Binding.Static (resolved value)` when it resolves;
/// leave it untouched otherwise (NotResolved / Errored — the renderer's
/// loading / error states still apply). Generic over the binding's `'T`.
let private substB (sources: BindingSources) (b: Binding<'T>) : Binding<'T> =
    match BindingResolver.resolve sources b with
    | Resolved v -> Binding.Static(Some v)
    | NotResolved
    | Errored _
    | I18nUnresolved _ -> b

let private substBOpt (sources: BindingSources) (bo: Binding<'T> option) : Binding<'T> option =
    bo |> Option.map (substB sources)

/// Resolve a bound `TextSource` to its `Literal`; pass `Literal` / `I18n`
/// through (stable under `SetState`).
let private resolveText (sources: BindingSources) (t: TextSource) : TextSource =
    match t with
    | TextSource.Bound b ->
        match BindingResolver.resolve sources b with
        | Resolved s -> TextSource.Literal s
        | NotResolved
        | Errored _
        | I18nUnresolved _ -> t
    | TextSource.Literal _
    | TextSource.I18n _ -> t

let private resolveTextOpt (sources: BindingSources) (t: TextSource option) : TextSource option =
    t |> Option.map (resolveText sources)

/// Resolve this node's OWN state-reactive leaf fields (text + bindings) against
/// the store. Children are untouched here (the generic recursion in
/// `resolveTree` handles them). Uncovered kinds pass through (the floor).
let resolveOwnFields (sources: BindingSources) (node: Node<obj>) : Node<obj> =
    let kind =
        // One flat match with one catch-all. Uncovered kinds pass through (the
        // floor).
        match node.Kind with
        | NodeKind.Heading s ->
            NodeKind.Heading
                { s with
                    Text = resolveText sources s.Text }
        | NodeKind.Markdown s ->
            NodeKind.Markdown
                { s with
                    Text = resolveText sources s.Text }
        | NodeKind.Badge s ->
            NodeKind.Badge
                { s with
                    Label = resolveText sources s.Label }
        | NodeKind.Metric s ->
            NodeKind.Metric
                { s with
                    Label = resolveText sources s.Label
                    Value = substB sources s.Value
                    Trend = substBOpt sources s.Trend
                    Subtext = resolveTextOpt sources s.Subtext }
        | NodeKind.Callout s ->
            NodeKind.Callout
                { s with
                    Heading = resolveTextOpt sources s.Heading
                    Body = resolveText sources s.Body }
        | NodeKind.Progress s ->
            NodeKind.Progress
                { s with
                    Fraction = substB sources s.Fraction
                    Label = resolveTextOpt sources s.Label
                    Caveat = resolveTextOpt sources s.Caveat }
        | NodeKind.LabelValueRow s ->
            NodeKind.LabelValueRow
                { s with
                    Label = resolveText sources s.Label
                    Value = substB sources s.Value
                    Help = resolveTextOpt sources s.Help }
        | NodeKind.Link s ->
            NodeKind.Link
                { s with
                    Href = substB sources s.Href
                    Label = resolveText sources s.Label }
        | NodeKind.Sparkline s ->
            NodeKind.Sparkline
                { s with
                    Source = substB sources s.Source }

        | NodeKind.Button s ->
            NodeKind.Button
                { s with
                    Label = resolveText sources s.Label
                    Tooltip = resolveTextOpt sources s.Tooltip
                    Disabled = substBOpt sources s.Disabled }
        | NodeKind.Select s ->
            NodeKind.Select
                { s with
                    Label = resolveText sources s.Label
                    Source = substB sources s.Source
                    Value = substB sources s.Value
                    Placeholder = resolveTextOpt sources s.Placeholder }

        | NodeKind.Tabs s ->
            NodeKind.Tabs
                { s with
                    ActiveIndex = substB sources s.ActiveIndex
                    ActiveTag = substBOpt sources s.ActiveTag }
        | NodeKind.Stepper s ->
            NodeKind.Stepper
                { s with
                    ActiveStep = substB sources s.ActiveStep }
        | NodeKind.Disclosure s ->
            NodeKind.Disclosure
                { s with
                    Open = substB sources s.Open
                    Heading = resolveText sources s.Heading }
        | NodeKind.Box s ->
            NodeKind.Box
                { s with
                    Heading = resolveTextOpt sources s.Heading }
        | NodeKind.SummaryList s ->
            NodeKind.SummaryList
                { s with
                    Heading = resolveTextOpt sources s.Heading }

        | other -> other

    { node with Kind = kind }

/// Re-resolve a whole tree's state-reactive bindings against the store,
/// producing a tree whose changed values a (binding-blind) structural diff can
/// see. Structure is preserved (no node added / removed / re-id'd); only leaf

// Budget weights. Only a `Binding.Static` payload is counted: a `Query` /
// `State` / `Transform` binding resolves at render time from the host's own
// store, so its size is not a property of the untrusted tree.
let private staticSeqCount (binding: Binding<'t seq>) : int =
    match binding with
    | Binding.Static(Some items) -> items |> Seq.truncate Budget.maxCountedRows |> Seq.length
    | _ -> 0

let private staticListCount (binding: Binding<'t list>) : int =
    match binding with
    | Binding.Static(Some items) -> min Budget.maxCountedRows (List.length items)
    | _ -> 0

/// `WalkWitness.Cost` — one node's own data cost, excluding the node itself
/// (the core adds it). A `Chart` costs one per (point × series), a `DataGrid`
/// one per (row × column); every other kind carries no data of its own.
let nodeDataCost (node: Node<obj>) : int =
    match node.Kind with
    | NodeKind.Chart spec -> Budget.satMul (staticSeqCount spec.Source) (max 1 (List.length spec.YFields))
    | NodeKind.DataGrid spec -> Budget.satMul (staticSeqCount spec.Source) (max 1 (List.length spec.Columns))
    | NodeKind.Map spec -> staticListCount spec.Source
    | NodeKind.Sparkline spec -> staticListCount spec.Source
    | _ -> 0

/// The query slot a row source is bound to, when it is bound to one at all.
/// Only a direct `Binding.Query` is a query read; a `Binding.Transform` over a
/// query is a second question, deliberately not attempted.
let private slotOf (source: Binding<Row seq>) : string option =
    match source with
    | Binding.Query(name, _, _) -> Some name
    | _ -> None

/// `WalkWitness.QueryReaders` — the columns a node needs of the query slot it
/// reads, where it reads one. A grid's columns name their fields, and a chart
/// names its axes. `ClosureHeld` covers the projections that decide WHICH
/// COLUMNS ARE READ: a grid column with no `field` and a closure row key.
let queryReaders (node: Node<obj>) : QueryReader list =
    let reader slot fields closureHeld =
        [ { NodeId = node.Id
            Slot = slot
            Fields = fields |> List.distinct
            ClosureHeld = closureHeld } ]

    match node.Kind with
    | NodeKind.DataGrid spec ->
        match slotOf spec.Source with
        | None -> []
        | Some slot ->
            let declared = spec.Columns |> List.choose _.Field
            let closureHeld = spec.Columns |> List.exists (fun c -> Option.isNone c.Field)

            reader slot (declared @ Option.toList spec.RowKeyField) (closureHeld || Option.isSome spec.RowKey)

    | NodeKind.Chart spec ->
        match slotOf spec.Source with
        | None -> []
        | Some slot -> reader slot (spec.XField :: spec.YFields) false

    | _ -> []

/// `DispatchWitness.Handlers` — the action slots the WIRE preserves, named by the
/// event that dispatches each. Every other handler slot is a closure the
/// decoder replaces with an inert placeholder, so it can demand nothing on a
/// decoded tree.
let handlers (node: Node<obj>) : (string * Action<obj>) list =
    match node.Kind with
    | NodeKind.Button spec -> [ "click", spec.OnClick ]
    | NodeKind.Form spec -> [ "submit", spec.OnSubmit ]
    | NodeKind.Modal spec -> spec.OnDismiss |> Option.map (fun a -> "dismiss", a) |> Option.toList
    | _ -> []

/// `WalkWitness.Nodes` — Core's node witness over the STRUCTURAL surface, the
/// one `getChildren` / `withChildren` describe and the budget and
/// re-resolution walk.
let nodes: NodeWitness<Node<obj>, string> =
    { Id = fun n -> n.Id
      KindTag = Fuaran.UI.Generated.nodeWitness.KindTag
      Children = fun n -> getChildren n.Kind |> Option.defaultValue []
      ReplaceChildren =
        fun n kids ->
            match withChildren n.Kind kids with
            | Some k -> { n with Kind = k }
            | None -> n }

// ─── the store witness ──────────────────────────────────────────────────────

/// `StoreWitness`: the `State` map is the channel an assignment writes (the
/// `JVal` lowered to the structural obj shapes the store has always held, so
/// `Binding.State` re-resolution reads it back), a server read lands in
/// `QueryResults`, and the host-reserved namespace is the tier's `host.`.
let store: StoreWitness<BindingSources> =
    { Assign =
        fun key jv s ->
            { s with
                State = Map.add key (JValObj.toObj jv) s.State }
      // The read a REVERSIBLE run records an overwritten value through (Phase
      // 1976): the `State` key, resolved back through the binding resolver
      // exactly as a `Binding.State` re-resolution reads it, so what the trace
      // records is what the tier itself would have read. Never called by the
      // forward fold, and the UI tier views nothing as a branch or a repeat.
      Read =
        fun key s ->
            match BindingResolver.resolveJVal s (Binding.State(key, None)) with
            | Resolved jv -> Some jv
            | NotResolved
            | Errored _
            | I18nUnresolved _ -> None
      LandQuery =
        fun slot table s ->
            { s with
                QueryResults = Map.add slot (Unchecked.nonNull (box table)) s.QueryResults }
      IsReserved = StateKeys.isHostReserved
      ReservedPrefix = StateKeys.HostReservedPrefix }

// ─── the state axis ─────────────────────────────────────────────────────────

/// `StateWitness`: the tier's apply engine, canonical op codec, structural
/// diff and canonical tree encoding. A refusal from the apply engine is
/// reported by its code, exactly as the handler's halt always named it. The UI
/// tier has no op-channel guard: every op is an edit (Phase 1974), so the
/// handler's `ApplyOps` arm is byte-for-byte what it was.
let state: StateWitness<Node<obj>, TreeOp<obj>> =
    { Stream =
        { Apply =
            fun op tree ->
                Fuaran.UI.Ops.Apply.apply op tree
                |> Result.mapError (fun err -> sprintf "%A" err.Code)
          Encode = Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeOp
          Decode =
            fun text ->
                Fuaran.UI.Ops.JsonDecode.decodeOp text
                |> Result.mapError (fun err -> string err.Code) }
      Diff = TreeOpDiff.diff
      // An op naming its `target` is re-runnable on replay. Read off the op's
      // own canonical encoding, which is the declared form the specification
      // derives the classification from.
      AbsoluteTarget =
        fun op ->
            match Json.parse (Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeOp op) with
            | Ok encoded -> ProgramWire.tryString "target" encoded
            | Error _ -> None
      // What a tree op REACHES (Phase 1967): the nodes it addresses, under
      // the op's own member names — `target`, and the parent an insert,
      // move or reorder names. Read off the canonical encoding, the declared
      // form, exactly as `AbsoluteTarget` is. Deliberately NOT every string
      // member: a prop path, a binding slot and a prop value are what the op
      // WRITES, not what it reaches, and a reach the document carries must
      // never be a payload. No destination: a tree op reaches the tree the
      // host holds and nothing beyond it.
      Reach =
        fun op ->
            let addressing = [ "target"; "parentId"; "newParentId" ]

            { Arguments =
                match Json.parse (Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeOp op) with
                // In the canonical encoding's own member order, so the reach
                // reads as the declared form does.
                | Ok(JObj members) ->
                    members
                    |> List.choose (fun (name, value) ->
                        match value with
                        | JStr id when List.contains name addressing -> Some(name, id)
                        | _ -> None)
                | _ -> []
              Destination = EffectDestination.Absent }
      Canonical = Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode
      View = OpView.edits
      // No UI op is viewed as an `Each` (Phase 1990), so no UI op is ever
      // substituted into and none reads a placeholder: the identity, and
      // nothing — the honest answers for a tier whose iteration is data
      // binding in the tree.
      Substitute = fun _ _ op -> op
      Placeholders = fun _ -> []
      // Every tree op has an EXACT inverse through the tier's own diff
      // (Phase 1977): apply the op to the pre-state and diff the result back
      // to the pre-state, which answers the ops that restore it — the same
      // `TreeOpDiff.diff` the driver emits its patches with. A list, which is
      // why the member answers one. An op the apply engine refuses has no
      // post-state to diff from and answers nothing; the plan never records
      // such an op, because the refusal halted it.
      Undo =
        fun op ->
            UndoClass.Inverse(fun pre ->
                match Fuaran.UI.Ops.Apply.apply op pre with
                | Ok post -> TreeOpDiff.diff post pre
                | Error _ -> []) }

// ─── the effect witness ─────────────────────────────────────────────────────

/// The reader the shipped emitter never had. A rendering surface decodes
/// these in its own runtime; nothing on this side did, which is why a
/// round-trip of this family was uncertifiable before the wire cut.
let decodeClientEffect (value: JVal) : Result<ClientEffect, WireRefusal> =
    let kind =
        match ProgramWire.tryString "kind" value with
        | Some k -> Ok k
        | None -> ProgramWire.refuse RefusalClass.MissingMember "required member 'kind' is absent"

    kind
    |> Result.bind (fun kind ->
        let one name ctor =
            ProgramWire.declaredOnly [ "kind"; name ] value
            |> Result.bind (fun () -> ProgramWire.requireString name value)
            |> Result.map ctor

        match kind with
        // Phase 1601 — the specification declares `target`, so this arm
        // reads it. Until it did, the tier could emit a `Blank` document
        // that this decoder refused: `ProgramWire.declaredOnly` correctly rejected a
        // member the specification did not name, and the host was therefore
        // producing bytes its own conformance codec would not take back.
        // Closing that was a specification act performed in one change-set
        // across both repositories — normative text (§5.2), schema, the two
        // fixtures, the manifest, and then this arm — which is the forward
        // coupling this family always carried.
        //
        // `Self` is the identity and §5.2 says it is NOT written, so
        // absence restores it. The explicit spelling is still ACCEPTED — it
        // is a declared member holding a declared value — but nothing emits
        // it, which is why it is not a round-trip vector: it would not
        // re-encode to its own bytes. A third value is refused as
        // `undeclared-member`, on the same footing as `ReadFileBody`'s
        // `encoding` below: the member is declared, the value it carries is
        // not, and passing one through would hand a rendering surface a
        // browsing context it has no rule for.
        | "Navigate" ->
            ProgramWire.declaredOnly [ "kind"; "route"; "target" ] value
            |> Result.bind (fun () -> ProgramWire.requireString "route" value)
            |> Result.bind (fun route ->
                // `ProgramWire.tryMember`, not `ProgramWire.tryString`: the latter reads a present
                // non-string member as absence, which would silently
                // decode `{"target":7}` to `Self` rather than refusing it.
                match ProgramWire.tryMember "target" value with
                | None
                | Some(JStr "Self") -> Ok(ClientEffect.Navigate(route, NavigateTarget.Self))
                | Some(JStr "Blank") -> Ok(ClientEffect.Navigate(route, NavigateTarget.Blank))
                | Some(JStr other) ->
                    ProgramWire.refuse
                        RefusalClass.UndeclaredMember
                        ("target '" + other + "' is neither 'Self' nor 'Blank'")
                | Some _ -> ProgramWire.refuse RefusalClass.UndeclaredMember "member 'target' is not a string")
        | "PushState" -> one "route" ClientEffect.PushState
        | "WriteToClipboard" -> one "text" ClientEffect.WriteToClipboard
        | "Focus" -> one "nodeId" ClientEffect.Focus
        | "Download" ->
            ProgramWire.declaredOnly [ "kind"; "url"; "name" ] value
            |> Result.bind (fun () -> ProgramWire.requireString "url" value)
            |> Result.bind (fun url -> ProgramWire.requireString "name" value |> Result.map (fun name -> url, name))
            |> Result.map ClientEffect.Download
        | "ReadFileBody" ->
            ProgramWire.declaredOnly [ "kind"; "nodeId"; "encoding" ] value
            |> Result.bind (fun () -> ProgramWire.requireString "nodeId" value)
            |> Result.bind (fun nodeId ->
                ProgramWire.requireString "encoding" value
                |> Result.bind (fun encoding ->
                    if List.contains encoding [ "Text"; "Base64"; "DataUrl" ] then
                        Ok(nodeId, encoding)
                    else
                        ProgramWire.refuse
                            RefusalClass.UndeclaredMember
                            ("encoding '" + encoding + "' is not one of the three")))
            |> Result.map ClientEffect.ReadFileBody
        // Phase 1689 — arms seven and eight, at specification format
        // version 2. The vocabulary was closed at six while a conformant
        // emitter already shipped these two, so a rendering surface was
        // receiving documents the text declared ill-formed; §11.1's rule
        // makes widening a closed vocabulary breaking, which is why they
        // arrive together with a version rather than one at a time.
        //
        // `Print` carries NO members, and `ProgramWire.declaredOnly [ "kind" ]` is
        // therefore the WHOLE decoder — there is nothing to read, and a
        // member that is present is refused rather than ignored. That
        // refusal is the arm's only real rule: every parameter of a
        // printing belongs to the reader's own dialogue, so a document
        // constraining one would leave its emitter believing it had
        // constrained something it had not.
        | "Print" ->
            ProgramWire.declaredOnly [ "kind" ] value
            |> Result.map (fun () -> ClientEffect.Print)
        // `Confirm` carries both members required. What a yes will DO is
        // deliberately absent: the continuations stay with whoever holds
        // the tree, the gate and the egress policy, and a surface handed
        // them is a surface that can perform them without ever asking.
        //
        // `token` says WHICH confirmation in the originating gesture is
        // being answered, so a chain raising two of them is unambiguous —
        // and it is untrusted payload like every other value here. It
        // addresses a question; it never authorises an answer, which is
        // the reader's own continuation meeting the gate on its own.
        | "Confirm" ->
            ProgramWire.declaredOnly [ "kind"; "prompt"; "token" ] value
            |> Result.bind (fun () -> ProgramWire.requireString "prompt" value)
            |> Result.bind (fun prompt ->
                ProgramWire.requireString "token" value
                |> Result.map (fun token -> prompt, token))
            |> Result.map ClientEffect.Confirm
        | other ->
            ProgramWire.refuse RefusalClass.UnknownEffectArm ("'" + other + "' is not an arm of the closed vocabulary"))

/// `EffectWitness`.
let effects: EffectWitness<ClientEffect> =
    { Kind = ClientEffect.kind
      Destination = ClientEffectDestination.destinationOf
      Encode = ClientEffect.encode
      Decode = decodeClientEffect }

// ─── the claim verifier ─────────────────────────────────────────────────────

/// The core's `ClaimVerifier`, built out of the tier's attestation seam. The
/// envelope verifier reads nothing from a key but its id.
let claimVerifier
    (crypto: Fuaran.UI.OpStream.Abstractions.IClaimSignatureVerifier)
    : ClaimVerifier<Fuaran.UI.OpStream.Abstractions.KeyDirectoryEntry> =
    { KeyId = fun key -> key.KeyId
      Verify = fun payload signature key -> crypto.VerifyClaim payload signature key }

// ─── the witness, assembled ─────────────────────────────────────────────────

/// The walk axis: the structural and traversal surfaces, the per-node data
/// cost and the query readers.
let walk: WalkWitness<Node<obj>> =
    { Nodes = nodes
      Traverse = descendantNodes
      Cost = nodeDataCost
      QueryReaders = queryReaders }

/// The dispatch axis: handlers and events on nodes, per-node re-resolution,
/// the fourteen-arm action view, expressions, the binding store and the
/// client effects.
let dispatch: DispatchWitness<Node<obj>, Action<obj>, Binding<JVal>, BindingSources, ClientEffect> =
    { Handlers = handlers
      Events = fun node -> Validation.legitimateEvents node |> Set.toList
      Resolve = resolveOwnFields
      Action =
        { View = view
          Lower = lower
          Describe = Validation.describeAction
          Encode = encodeAction
          Decode = decodeAction
          // No UI arm views as an `Each` (Phase 1990; `ui_view_no_flow`), so
          // the fold never substitutes into a UI action and none reads a
          // placeholder — the UI tier repeats through data binding, in the
          // tree. The identity, and nothing.
          Substitute = fun _ _ action -> action
          Placeholders = fun _ -> [] }
      Expr =
        { Resolve = resolveExpr
          Uses = usesOfExpr }
      Store = store
      Effect = effects }

/// The UI witness: `ProgramWitness` at the UI tier's types, all three axes
/// filled (D18, D20).
let witness: UiProgramWitness =
    { State = state
      Walk = walk
      Dispatch = dispatch }
