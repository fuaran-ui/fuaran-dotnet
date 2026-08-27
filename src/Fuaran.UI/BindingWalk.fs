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
    /// Phase 865 — a `Binding.Transform` whose SOURCE slot
    /// (`TransformSource.Live`) is a `Binding.State`, carrying the key and
    /// whether THAT slot declares its own `defaultValue`. FUARAN105's subject.
    ///
    /// **Deliberately NOT a `State` read**, and the distinction is the whole
    /// point of recording it separately. `usesOfBinding` has never descended a
    /// Transform's source slot, so a key only a Transform reads is absent from
    /// `StateKeyFacts.Reads` today. Folding it in there would narrow FUARAN098
    /// (a shipped Warning) on trees that fire it now — a behaviour change well
    /// outside the remit of adding a Warning, and the same reasoning
    /// `StateKeyFacts` already records for holding `WriteKeys` beside `Writes`.
    /// The gap is real and is recorded as its own work; this case closes only
    /// what FUARAN105 needs.
    ///
    /// It is also filtered OUT of `TreeBindingFacts.Uses` for the mirror
    /// reason: the consumption-union rules (FUARAN070–076) are tuned to the
    /// surfaces `Uses` covers today.
    | TransformStateSource of key: string * hasDefault: bool

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
        /// Every state key the tree can be shown to WRITE, from EVERY write
        /// surface — not only `Action.SetState`. Held BESIDE `Writes` rather
        /// than replacing it: `Writes` is the (writer, key) list FUARAN098
        /// iterates, and widening it would newly fire a shipped Warning on
        /// trees that pass today. The surfaces counted here:
        ///
        ///  1. `Action.SetState`'s key, reachable from a wire-survivable slot.
        ///  2. `Action.Call`'s `into: State <key>` result target.
        ///  3. A control's WRITE-BACK slot bound to `Binding.State(k, _)` —
        ///     `Select.value` / `.values`, `Tabs.activeIndex` / `.activeTag`,
        ///     `Stepper.activeStep`, `Disclosure.open`, `Modal.open`,
        ///     `Toast.open`, and every `FormField` value slot. The renderer
        ///     writes these back when no handler is supplied.
        ///  4. A `FormField` whose value slot is `None` — the Phase 694
        ///     auto-bind writes back to `State(field.Id)`.
        ///  5. A `DataGrid`'s `sortStateKey` / `pageStateKey` / `editStateKey`
        ///     — plain STRINGS the renderer writes with no `Binding` to see,
        ///     and an editable grid whose `source` is a direct `Binding.State`
        ///     (the Phase 663 commit destination).
        WriteKeys: Set<string>
        /// True when the tree holds a writer whose DESTINATION cannot be seen —
        /// the write-side twin of `OpaqueReader`, and the reason FUARAN103 can
        /// afford to be a finding at all. A closure produces an arbitrary
        /// `Action` at dispatch time, so it may write any key: a control's
        /// `onChange` / `onToggle` / `onSelect` / `onDismiss` handler, a
        /// grid's closure-bearing cell kinds, a `Binding.Local`'s `onCommit`,
        /// an `Action.Call`'s `onResult`, and the host-crossing actions
        /// (`Dispatch` / `Invoke` / `AiTool` / `CommitLocal` / `ReadFileBody`).
        /// So are a `NodeKind.Custom` node's registered renderer and a `Mount`
        /// guest. Under any of them the absence of a write proves nothing.
        OpaqueWriter: bool
        /// Every `NodeKind.Switch` whose branch SELECTOR (`SwitchSpec.On`, a
        /// `Binding` since Phase 768) reads a state key, as (switch node id,
        /// key). Collected explicitly rather than recovered from `Uses`: a
        /// Switch node's accessibility slots are State-bindable too, so a
        /// reader-tagged `State` use on a Switch does not identify the
        /// selector, and the rule that reasons about it must not guess.
        SwitchSelectors: (string * string) list
        /// Phase 865 — every `Binding.Transform` in the tree whose source slot
        /// is a **default-less** `Binding.State`, as (reading node id, key).
        /// FUARAN105's subjects.
        ///
        /// A source that DOES carry a `defaultValue` is not recorded: the
        /// decoder derives the Transform's initial snapshot table from that
        /// carried default, so the pipeline runs over real rows rather than
        /// `TransformLive.emptySource`, and the silent zero cannot arise. A
        /// SIBLING reader's default is deliberately NOT a rescuer — under the
        /// shipped resolver semantics `Binding.State`'s `defaultValue` is a
        /// per-reader fallback, not a slot seed, so it never reaches this
        /// Transform. That reading is the one the deferral of the seeding rule
        /// requires; see `PreEmitDefect.TransformSourceInert`.
        TransformInertSources: (string * string) list
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
    | Binding.Transform(source, pipeline, parameters) ->
        // The pure `Transform.paramsOf` derivation (fuaran-core#77) names every
        // param the pipeline actually references — a declared `params` entry
        // outside it is dead weight (FUARAN076).
        let referenced = Fuaran.Core.Transform.paramsOf pipeline |> Set.ofList

        // Phase 865 — the SOURCE slot, recorded as its own case rather than as a
        // `State` read (see `BindingUse.TransformStateSource`). A `Data` source
        // is columnar/`ref` and names no state key; a `Live` source over any
        // other binding shape is not FUARAN105's subject.
        let sourceUse =
            match source with
            | TransformSource.Live(Binding.State(key, defaultValue), _) ->
                [ BindingUse.TransformStateSource(key, defaultValue.IsSome) ]
            | TransformSource.Live _
            | TransformSource.Data _ -> []

        sourceUse
        @ (defaultArg parameters []
           |> List.collect (fun (p: TransformParam) ->
               let sourceUses =
                   usesOfBinding p.From
                   |> List.map (function
                       // A param's Filter source is the DECLARED filter→consumer
                       // edge (the 424 construct) — distinct from a plain value
                       // read for the dangling / consumption checks.
                       | BindingUse.Filter filterName -> BindingUse.TransformParamFilter filterName
                       | other -> other)

               BindingUse.TransformParam(p.Name, Set.contains p.Name referenced) :: sourceUses))
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


/// The State key a WRITE-BACK slot commits to, and whether committing also runs
/// host code that may write elsewhere. A slot holding any other binding shape
/// gives the renderer's write-back default nowhere to write — the FUARAN069
/// inert-control condition — so it contributes no write. A `Local` buffers and
/// commits to whatever it re-syncs FROM, so its destination is `initialFrom`'s;
/// its `onCommit` hook is host code layered on top of that.
let rec writeBackTargetOf<'T> (binding: Binding<'T>) : string option * bool =
    match binding with
    | Binding.State(key, _) -> Some key, false
    | Binding.Local(_, _, initialFrom, onCommit, _) ->
        let key, opaque = writeBackTargetOf initialFrom
        key, opaque || onCommit.IsSome
    | _ -> None, false

/// The write-side facts of one `FormFieldKind`'s value slot.
type FormFieldWrite =
    {
        /// The State key an explicit value binding commits to.
        Target: string option
        /// True when the value slot is ABSENT, so the Phase 694 auto-bind
        /// decides the destination from the field's own id (in a form) or the
        /// FilterStore (on a chip) — a distinction only the caller can make.
        SlotAbsent: bool
        /// True when a change handler, or a `Local`'s `onCommit`, may write
        /// somewhere this walk cannot see.
        Opaque: bool
    }

/// `writeBackTargetOf` over a `FormFieldKind`'s value slot, plus its handler.
/// One arm per case so a new field kind is a compile error here rather than a
/// silently-uncounted writer — the same forward-coupling posture the read walk
/// takes in `usesOfFormFieldKind`.
let formFieldWriteFacts<'Msg> (kind: FormFieldKind<'Msg>) : FormFieldWrite =
    let slot v hasHandler =
        match v with
        | Some b ->
            let target, opaque = writeBackTargetOf b

            { Target = target
              SlotAbsent = false
              Opaque = opaque || hasHandler }
        | None ->
            { Target = None
              SlotAbsent = true
              Opaque = hasHandler }

    match kind with
    | FormFieldKind.Text(v, h) -> slot v h.IsSome
    | FormFieldKind.Number(v, h) -> slot v h.IsSome
    | FormFieldKind.Checkbox(v, h) -> slot v h.IsSome
    | FormFieldKind.Toggle(v, h) -> slot v h.IsSome
    | FormFieldKind.TextArea(v, h, _) -> slot v h.IsSome
    | FormFieldKind.RangedNumber(v, h, _, _, _) -> slot v h.IsSome
    | FormFieldKind.Range(v, h, _, _, _) -> slot v h.IsSome
    | FormFieldKind.Choice(_, v, h) -> slot v h.IsSome
    | FormFieldKind.SegmentedChoice(_, v, h, _) -> slot v h.IsSome
    | FormFieldKind.Date(v, h, _, _, _, _) -> slot v h.IsSome
    | FormFieldKind.DateRange(v, h, _, _, _, _) -> slot v h.IsSome

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

    // ── The write-side projection FUARAN103 runs on ──
    let stateWriteKeys = System.Collections.Generic.HashSet<string>()
    let switchSelectors = ResizeArray<string * string>()
    let mutable opaqueWriter = false

    // ── The Phase 865 read-side projection FUARAN105 runs on ──
    let transformInertSources = ResizeArray<string * string>()

    /// A closure produces an arbitrary `Action` at dispatch time, so it may
    /// write any key. Seeing one stands the write-side rule down for the whole
    /// tree — over-counting an opaque writer costs a missed finding, and the
    /// alternative costs a false accusation of a tree that is perfectly correct.
    let noteOpaqueIf (present: bool) =
        if present then
            opaqueWriter <- true

    /// Fold one `writeBackTargetOf` result into the write projection.
    let noteWriteBack (target: string option, opaque: bool) =
        target |> Option.iter (fun k -> stateWriteKeys.Add k |> ignore)
        noteOpaqueIf opaque

    /// A `FormFieldKind`'s value slot is a write-back DESTINATION.
    /// `implicitKey` is what the Phase 694 auto-bind writes when the slot is
    /// ABSENT: the field's own id inside a form, and NOTHING on a filter chip,
    /// whose channel is the FilterStore rather than the State store.
    let recordFormFieldWrites (implicitKey: string option) (kind: FormFieldKind<'Msg>) =
        let facts = formFieldWriteFacts kind
        noteWriteBack (facts.Target, facts.Opaque)

        if facts.SlotAbsent then
            implicitKey |> Option.iter (fun k -> stateWriteKeys.Add k |> ignore)

    /// Fold usages into the STATE projection only. Every read surface reaches
    /// this, including the ones deliberately kept out of `Uses`. `readerId` is
    /// the node whose spec holds the binding — carried because FUARAN105 names
    /// the reading node, and this is the one fold every read surface reaches.
    let recordStateOf (readerId: string) (found: BindingUse list) =
        for u in found do
            match u with
            | BindingUse.State k -> stateReads.Add k |> ignore
            | BindingUse.Computed -> opaqueReader <- true
            // Phase 865 — only the DEFAULT-LESS source is a candidate; a source
            // carrying its own default is what makes the initial snapshot real.
            | BindingUse.TransformStateSource(key, false) -> transformInertSources.Add(readerId, key)
            | BindingUse.TransformStateSource(_, true) -> ()
            | _ -> ()

    // `inUses` is false while walking a subtree that contributes STATE facts but
    // must not widen `Uses` / `Calls` / `Nodes` — a `StateBehaviour` branch or a
    // `SlotArg` argument tree. Those subtrees were never walked here before, so
    // feeding them into the consumption-union checks would change five shipped
    // rules' verdicts as a side-effect of adding a Warning.
    let record (inUses: bool) (readerId: string) (found: BindingUse list) =
        recordStateOf readerId found

        if inUses then
            for u in found do
                match u with
                // Phase 865 — the STATE projection only, never `Uses`. See
                // `BindingUse.TransformStateSource`: the consumption-union
                // rules are tuned to the surfaces `Uses` covers today, and a
                // Transform's source slot is not one of them.
                | BindingUse.TransformStateSource _ -> ()
                | _ -> uses.Add { Reader = readerId; Use = u }

    /// Every `SetState` reachable from a wire-survivable action slot: the key is
    /// a WRITE, and a `valueFrom` deriving the written value is itself a READ.
    let rec recordStateAction (readerId: string) (action: Action<'Msg>) =
        match action with
        | Action.SetState(key, _, valueFrom) ->
            stateWrites.Add(readerId, key)
            stateWriteKeys.Add key |> ignore

            match valueFrom with
            | Some b -> recordStateOf readerId (usesOfBinding b)
            | None -> ()
        | Action.Chain actions -> actions |> List.iter (recordStateAction readerId)
        // A declared result target names its destination; an `onResult` closure
        // does not, and may write anything at all.
        | Action.Call(_, onResult, into) ->
            match into with
            | Some(CallResultTarget.State key) -> stateWriteKeys.Add key |> ignore
            | _ -> ()

            noteOpaqueIf onResult.IsSome
        // The host-crossing arms. Each hands control to code the tree cannot
        // see, which may write the store directly.
        | Action.Dispatch _
        | Action.Invoke _
        | Action.AiTool _
        | Action.CommitLocal _
        | Action.ReadFileBody _ -> opaqueWriter <- true
        | Action.Navigate _
        | Action.Notify _
        | Action.WriteToClipboard _ -> ()

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
            sb.OnEmpty |> Option.iter (walk inUses)
            sb.OnLoading |> Option.iter (walk inUses)
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
            | NodeKind.Stepper s ->
                noteWriteBack (writeBackTargetOf s.ActiveStep)
                noteOpaqueIf s.OnSelect.IsSome
                usesOfBinding s.ActiveStep, s.Children
            | NodeKind.Disclosure s ->
                noteWriteBack (writeBackTargetOf s.Open)
                noteOpaqueIf s.OnToggle.IsSome
                (usesOfText s.Heading @ usesOfBinding s.Open), s.Children
            | NodeKind.Tabs s ->
                noteWriteBack (writeBackTargetOf s.ActiveIndex)
                s.ActiveTag |> Option.iter (writeBackTargetOf >> noteWriteBack)
                noteOpaqueIf (s.OnSelect.IsSome || s.OnSelectTag.IsSome)

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
                // A dismissable modal's close gesture writes its own `open` slot.
                noteWriteBack (writeBackTargetOf s.Open)
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
            | NodeKind.Toast t ->
                noteWriteBack (writeBackTargetOf t.Open)
                usesOfText t.Message @ usesOfBinding t.Open, []
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
            | NodeKind.FileUpload fu ->
                noteOpaqueIf fu.OnSelect.IsSome
                usesOfText fu.Label @ usesOfBindingOpt fu.Disabled, []
            | NodeKind.Select s ->
                noteWriteBack (writeBackTargetOf s.Value)
                s.Values |> Option.iter (writeBackTargetOf >> noteWriteBack)
                noteOpaqueIf (s.OnChange.IsSome || s.OnChangeMulti.IsSome)

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
                            recordFormFieldWrites (Some field.Id) field.Kind

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
                        // No implicit key: an absent value slot on a chip
                        // auto-binds to the FILTER store, not the State store.
                        recordFormFieldWrites None fs.Kind

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

                // The write side of the same three slots: a header click writes
                // the sort descriptor, the pager writes the page descriptor, and
                // an edited cell commits to `editStateKey` — or, absent one, back
                // to a directly-State-bound `source` (Phase 663).
                g.SortStateKey |> Option.iter (fun k -> stateWriteKeys.Add k |> ignore)
                g.PageStateKey |> Option.iter (fun k -> stateWriteKeys.Add k |> ignore)
                g.EditStateKey |> Option.iter (fun k -> stateWriteKeys.Add k |> ignore)

                if g.Editable && g.EditStateKey.IsNone then
                    noteWriteBack (writeBackTargetOf g.Source)

                // A row-click handler is a closure over the row: an arbitrary
                // action per row, so an arbitrary write.
                noteOpaqueIf g.OnRowClick.IsSome

                // A closure-bearing cell produces an arbitrary `Action` per row,
                // so it may write any key. The value-only cell kinds cannot.
                for col in g.Columns do
                    match col.Kind with
                    | CellKindErased.Editable onEdit -> noteOpaqueIf onEdit.IsSome
                    | CellKindErased.Checkbox(_, onToggle) -> noteOpaqueIf onToggle.IsSome
                    | CellKindErased.Button(_, onClick) -> noteOpaqueIf onClick.IsSome
                    | CellKindErased.ButtonGroup _
                    | CellKindErased.Custom _ -> opaqueWriter <- true
                    | CellKindErased.Text
                    | CellKindErased.Numeric
                    | CellKindErased.Date
                    | CellKindErased.Link _
                    | CellKindErased.Pill _
                    | CellKindErased.TonedPill _
                    | CellKindErased.Progress _ -> ()

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
            // alone would false-warn on the most idiomatic shape in the language).
            //
            // Tidy-Up follow-on — 932 routed this to `recordStateOf`, i.e. into the
            // state projection but deliberately NOT into `Uses`, because widening `Uses`
            // is a behaviour change outside an additive Warning rule's remit. It now
            // goes through `record` into BOTH: 768 made `On` any `Binding`, so a
            // `Binding.Selection` selector is a Selection READ and a dangling one is
            // FUARAN070's own case. Measured before landing — the full suite stays
            // green, and reverting this line reddens exactly the Switch-selector probe
            // in `PreEmitValidateTests`. The case children + default are walked so
            // their own bindings are captured.
            | NodeKind.Switch spec ->
                record inUses readerId (usesOfBinding spec.On)

                // The selector, recorded EXPLICITLY for FUARAN103: a Switch's
                // accessibility slots are State-bindable too, so a reader-tagged
                // State use on this node does not identify the branch selector.
                match spec.On with
                | Binding.State(key, _) -> switchSelectors.Add(readerId, key)
                | _ -> ()

                [], (spec.Cases |> List.map _.Child) @ [ spec.Default ]
            | NodeKind.FragmentDecl spec -> [], [ spec.Body ]
            // Custom props are JVal literals, not bindings; a FragmentRef carries
            // no body; a Mount guest owns its own scoped stores.
            | NodeKind.Custom _ ->
                // Phase 932 — a REGISTERED custom renderer is host code that may read
                // any state key, so the tree can no longer be shown to read nothing.
                // It may equally WRITE any key, which is the same argument on the
                // other channel.
                opaqueReader <- true
                opaqueWriter <- true
                [], []
            | NodeKind.FragmentRef spec ->
                spec.Args |> Option.iter walkSlotArgs
                [], []
            | NodeKind.Mount spec ->
                // A guest's INTERIOR reads no host key (it renders under an isolated
                // `StateStore.forScope`), but a `SlotArg` handed to it is host-authored
                // and its render-time scope is not settled by the type. Counted as a
                // read: over-counting costs a missed finding, under-counting costs a
                // false accusation. The same holds on the write channel — a guest
                // is another tree entirely, and this walk never sees its body.
                opaqueWriter <- true
                spec.Inputs |> Option.iter walkSlotArgs
                [], []

        record inUses readerId directUses
        children |> List.iter (walk inUses)

    and walkSlotArgs (args: Map<string, FragmentArg<'Msg>>) =
        for KeyValue(_, arg) in args do
            match arg with
            | FragmentArg.SlotArg tree -> walk true tree
            | _ -> ()

    walk true root

    { Uses = List.ofSeq uses
      DeclaredFilters = List.ofSeq declaredFilters
      Calls = List.ofSeq calls
      Nodes = nodes |> Seq.fold (fun acc (KeyValue(k, v)) -> Map.add k v acc) Map.empty
      StateKeys =
        { Writes = List.ofSeq stateWrites
          Reads = Set.ofSeq stateReads
          OpaqueReader = opaqueReader
          WriteKeys = Set.ofSeq stateWriteKeys
          OpaqueWriter = opaqueWriter
          SwitchSelectors = List.ofSeq switchSelectors
          TransformInertSources = List.ofSeq transformInertSources } }
