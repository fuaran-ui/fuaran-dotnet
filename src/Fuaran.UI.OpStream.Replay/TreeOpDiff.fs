namespace Fuaran.UI.OpStream.Replay

open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops.Introspect
open Fuaran.UI.OpStream.Abstractions

// ============================================================================
//  TreeOpDiff — the **TreeOp-emitting** tree diff (Phase 152, Track A).
//
//  The inverse of `Apply.apply`: given two `Node<'Msg>` snapshots, produce a
//  `TreeOp<'Msg> list` that, folded through the apply engine against the old
//  tree, reconstructs the new one. This is the genuinely-missing primitive
//  the server-driven tier (Phase 152) and the closed-loop orchestrator need —
//  distinct from `TreeDiff` (this file's neighbour), which emits *descriptive*
//  `NodeChange` records for an inspector, not *applyable* ops.
//
//  **Round-trip contract (the acceptance test):**
//    fold Apply.apply a (diff a b)  ≡  b   (canonical-JSON equality)
//  Closure-safe equality is canonical-JSON equality (closures render as the
//  `"<closure>"` sentinel), the same surface `TreeDiff` / `CanonicalJson`
//  already trust — NOT F# structural equality (which a closure-bearing kind
//  doesn't support).
//
//  **Coarse-but-correct floor (this file).** Per Phase 152 Track A, ship the
//  trivially-correct floor first; per-kind *field-level* minimisation
//  (`UpdateProp` / `ReplaceBinding` instead of a wholesale `EditNode`) is the
//  follow-on optimisation over this floor. The floor:
//    - same container kind + identical own-content (kind fields + state +
//      style, children excluded) → recurse into children only (minimal
//      structural ops: remove / insert / reorder + recurse common).
//    - otherwise (leaf content change, kind-discriminator change, or a
//      container whose own content changed) → wholesale node replace:
//      `EditNode(id, b.Kind)` (brings b's kind incl. its whole subtree) +
//      `UpdateState(id, b.State)` + `UpdateStyle(id, b.Style)` (EditNode
//      preserves Id/State/Style, so state/style are set explicitly). Coarse
//      (may resend an unchanged subtree / re-set unchanged state·style) but
//      unconditionally correct. The refinement trims to minimal patches.
//
//  **Closure-blindness (documented limitation, inherited).** A changed
//  `Action` / `Binding` closure that renders identically emits **no** op —
//  closures are the `"<closure>"` sentinel in the canonical shell, so two
//  nodes differing only in closure identity compare equal. Correct for the
//  DOM (identical markup); the orchestrator consumer that cares about closure
//  identity must know this boundary.
//
//  **Accessibility vocabulary gap (documented limitation, NEW finding).** The
//  canonical wire surface is id + kind + state + style + **accessibility**,
//  but the `TreeOp` vocabulary has **no op for `Accessibility`** (only
//  EditNode/UpdateState/UpdateStyle + structural ops). So an accessibility-only
//  change is **not expressible** and would not round-trip. This is acceptable
//  for the server-driven use case — accessibility is author-structural and
//  stable across a re-render (it is not `Model`-driven) — but it is a real gap
//  worth a follow-on `UpdateAccessibility` op if a consumer needs it.
//  (`Motion` / `ExtraAttributes` are omitted from the canonical surface
//  entirely, so they are invisible to the round-trip — no gap there.)
//
//  **Precondition.** `diff a b` assumes `a.Id = b.Id` (a re-render of the same
//  tree — no `TreeOp` changes a node's Id). Differing root ids cannot be
//  expressed; the function returns a best-effort wholesale replace at a's id
//  and the precondition is the caller's to honour.
//
//  FGP 2: FSharp.Core + Fuaran.UI + Fuaran.UI.Ops + OpStream.Abstractions
//  only (CanonicalJson for the closure-safe shell) — Fable-clean, no renderer
//  dep. Built adjacent to `TreeDiff` to share its indexing + shell-encode
//  posture (Phase 152 Key files).
// ============================================================================

module TreeOpDiff =

    let private rawId (NodeId s) : string = s

    let private childrenOf<'Msg> (node: Node<'Msg>) : Node<'Msg> list =
        getChildren node.Kind |> Option.defaultValue []

    let private childlessKind<'Msg> (node: Node<'Msg>) : NodeKind<'Msg> =
        match withChildren node.Kind [] with
        | Some k -> k
        | None -> node.Kind

    /// Index a tree as `rawNodeId → parent-raw-id` (`None` for the root) — the
    /// global view cross-parent move detection needs (the per-parent
    /// recursive diff can't see a node reappear under a different parent).
    let private indexParents<'Msg> (root: Node<'Msg>) : Map<string, string option> =
        let rec walk (acc: Map<string, string option>) (parent: string option) (node: Node<'Msg>) =
            let acc = Map.add (rawId node.Id) parent acc

            match getChildren node.Kind with
            | None -> acc
            | Some kids -> kids |> List.fold (fun a c -> walk a (Some(rawId node.Id)) c) acc

        walk Map.empty None root

    /// Safe cross-parent moves: ids present in **both** trees whose parent
    /// changed, where the new parent exists in `a` and the move introduces no
    /// cycle (`id` is not an ancestor of its new parent in `a`). Returns
    /// `(movedId, newParentId)`. Reparentings to a newly-inserted parent or to
    /// the root fall through to the remove+insert path (still correct, just not
    /// identity-preserving). Emitting `MoveNode` for the safe set preserves the
    /// moved node's DOM identity (focus / scroll / element state) — the
    /// server-driven tier's "unchanged nodes keep identity" goal.
    let private detectMoves<'Msg> (a: Node<'Msg>) (b: Node<'Msg>) : (NodeId * NodeId) list =
        let idxA = indexParents a
        let idxB = indexParents b

        [ for KeyValue(id, parentB) in idxB do
              match Map.tryFind id idxA, parentB with
              | Some parentA, Some newParentRaw when
                  parentA <> parentB
                  && Map.containsKey newParentRaw idxA
                  && not (isAncestorOf (NodeId id) (NodeId newParentRaw) a)
                  ->
                  yield (NodeId id, NodeId newParentRaw)
              | _ -> () ]

    /// `true` when the node's **kind own-fields** changed (the kind
    /// discriminator, or a scalar/binding field of the spec — children,
    /// state, style and accessibility excluded). Closure-safe: compares the
    /// childless kinds via canonical JSON with state·style·accessibility
    /// normalised to `b`'s, so only kind drift moves the needle. When this is
    /// `false`, the kind shape is identical and a wholesale `EditNode` (which
    /// would re-send the whole subtree) is unnecessary.
    let private kindChanged<'Msg> (a: Node<'Msg>) (b: Node<'Msg>) : bool =
        let aNorm =
            { a with
                Kind = childlessKind a
                State = b.State
                Style = b.Style
                Accessibility = b.Accessibility }

        let bNorm = { b with Kind = childlessKind b }
        CanonicalJson.encodeNode aNorm <> CanonicalJson.encodeNode bNorm

    /// `true` when the node's `State` block changed. Only meaningful (and only
    /// called) when `kindChanged` is `false` — then both nodes' childless
    /// kinds encode identically, so substituting `b`'s childless kind into
    /// both isolates the `State` field (style·accessibility normalised to
    /// `b`'s). Closure-safe via canonical JSON.
    let private stateChanged<'Msg> (a: Node<'Msg>) (b: Node<'Msg>) : bool =
        let k = childlessKind b

        let aNorm =
            { a with
                Kind = k
                Style = b.Style
                Accessibility = b.Accessibility }

        let bNorm = { b with Kind = k }
        CanonicalJson.encodeNode aNorm <> CanonicalJson.encodeNode bNorm

    /// Launder `box` to a non-null `obj` (F# 10 nullness: `box` yields
    /// `objnull`; `UpdateProp`'s `PropValue.Native` wants non-null).
    let private boxNN (v: 'T) : obj = box v |> Unchecked.nonNull

    /// Erase a typed `Binding<'T>` to the wire `Binding<obj>` that
    /// `ReplaceBinding` carries — the inverse of `Apply.castBinding`'s
    /// `unbox`-compose (here we `box`-compose). `castBinding` reshapes it back
    /// to `Binding<'T>` at apply time, so `box`→`unbox` round-trips the value;
    /// closures round-trip via the `"<closure>"` canonical sentinel.
    let rec private toObjBinding<'T> (b: Binding<'T>) : Binding<obj> =
        match b with
        | Binding.Static v -> Binding.Static(boxNN v)
        | Binding.Query(name, accessor, dependsOn) -> Binding.Query(name, accessor >> boxNN, dependsOn)
        | Binding.Filter(name, dv) -> Binding.Filter(name, dv |> Option.map boxNN)
        | Binding.Selection(id, accessor, dv, fld) ->
            Binding.Selection(id, accessor >> boxNN, dv |> Option.map boxNN, fld)
        | Binding.State(key, defaultValue) -> Binding.State(key, boxNN defaultValue)
        | Binding.Computed f -> Binding.Computed(f >> boxNN)
        | Binding.I18n(key, args) -> Binding.I18n(key, args)
        | Binding.Local local ->
            Binding.Local
                { InitialFrom = toObjBinding local.InitialFrom
                  FlushOn = local.FlushOn
                  OnCommit = (fun (o: obj) -> local.OnCommit(unbox<'T> o))
                  Format = local.Format |> Option.map (fun f -> (fun (o: obj) -> f (unbox<'T> o)))
                  Parse = (fun s -> local.Parse s |> Result.map boxNN) }
        | Binding.Format(source, format, locale) -> Binding.Format(source, format, locale)
        | Binding.Transform(source, pipeline, parameters) -> Binding.Transform(source, pipeline, parameters)
        | Binding.Invoke(capabilityId, args) -> Binding.Invoke(capabilityId, args)

    // ── Per-field `UpdateProp` payloads (task 17) ───────────────────────────
    //
    // An `UpdateProp`'s `PropValue` chooses between two populations:
    //   • `PropValue.Wire jval` — the canonical JVal, **faithful in the op log**
    //     (survives serialise → replay), decoded back by `JsonDecode.Coerce.*`.
    //   • `PropValue.Native (box v)` — the in-process typed value; apply-exact,
    //     but a non-scalar Native persists as `"<opaque>"` in the log, so it is
    //     NOT log-replayable.
    // Each field emits `Wire` when it is losslessly wire-representable (scalars,
    // variant DUs, a `Literal` TextSource, a `Some` string/icon), keeps `Native`
    // for the in-process-only residue (a closure-bearing `Bound`/`I18n`
    // TextSource, a structured `CellFormat`), and **refuses to emit at all** for
    // a `Some → None` option transition (the `pv*Opt` encoders return `None`):
    // a JVal has no null form and the op decoder's value position rejects JSON
    // null, so "unset this optional field" is NOT expressible as a wire
    // `UpdateProp` — and boxing `None` produces a CLR null that canonical-encodes
    // as JSON `null`, an op the log could never replay. A skipped field means the
    // candidates cannot reproduce `b`, so `tryFieldLevel`'s propose-then-verify
    // (apply the candidates, accept only if the kind reproduces `b`) drops the
    // node to the always-correct `EditNode` floor. The same verify catches a
    // mis-encoded Wire value — coverage can regress, correctness cannot. The
    // variant-DU case strings **mirror `CanonicalJson.encode*`**; a case added
    // there but missed here degrades to the floor, never to a wrong op.
    let private pvInt (n: int) : PropValue = PropValue.Wire(JInt n)
    let private pvFloat (f: float) : PropValue = PropValue.Wire(JFloat f)
    let private pvBool (b: bool) : PropValue = PropValue.Wire(JBool b)

    let private pvText (t: TextSource) : PropValue =
        match t with
        | TextSource.Literal s -> PropValue.Wire(JStr s)
        | _ -> PropValue.Native(boxNN t)

    // Option-typed fields: `Some` encodes; `None` is inexpressible as an
    // UpdateProp (see the block comment) and skips → the EditNode floor.
    let private pvTextOpt (t: TextSource option) : PropValue option =
        match t with
        | Some(TextSource.Literal s) -> Some(PropValue.Wire(JStr s))
        // Box the whole option (non-null — it's a `Some`): Apply's fast path
        // unboxes the field's exact type, `TextSource option`.
        | Some _ -> Some(PropValue.Native(boxNN t))
        | None -> None

    let private pvStrOpt (s: string option) : PropValue option =
        match s with
        | Some v -> Some(PropValue.Wire(JStr v))
        | None -> None

    let private pvIconOpt (i: IconSource option) : PropValue option =
        match i with
        | Some(IconSource raw) -> Some(PropValue.Wire(JStr raw))
        | None -> None

    let private pvOrientation (o: Orientation) : PropValue =
        PropValue.Wire(
            JStr(
                match o with
                | Vertical -> "Vertical"
                | Horizontal -> "Horizontal"
            )
        )

    let private pvHeadingVariant (v: HeadingVariant) : PropValue =
        PropValue.Wire(
            JStr(
                match v with
                | HeadingVariant.Standard -> "Standard"
                | HeadingVariant.Eyebrow -> "Eyebrow"
                | HeadingVariant.Caption -> "Caption"
                | HeadingVariant.Lead -> "Lead"
            )
        )

    let private pvBadgeVariant (v: BadgeVariant) : PropValue =
        PropValue.Wire(
            JStr(
                match v with
                | BadgeVariant.Neutral -> "Neutral"
                | BadgeVariant.Brand -> "Brand"
                | BadgeVariant.Success -> "Success"
                | BadgeVariant.Warning -> "Warning"
                | BadgeVariant.Critical -> "Critical"
                | BadgeVariant.Info -> "Info"
            )
        )

    let private pvTone (t: ToneVariant) : PropValue =
        PropValue.Wire(
            JStr(
                match t with
                | Default -> "Default"
                | Subdued -> "Subdued"
                | Brand -> "Brand"
                | Success -> "Success"
                | Warning -> "Warning"
                | Critical -> "Critical"
                | Info -> "Info"
            )
        )

    let private pvWeight (w: StyleWeight) : PropValue =
        PropValue.Wire(
            JStr(
                match w with
                | Compact -> "Compact"
                | Standard -> "Standard"
                | Spacious -> "Spacious"
            )
        )

    let private pvEmphasis (e: Emphasis) : PropValue =
        PropValue.Wire(
            JStr(
                match e with
                | Quiet -> "Quiet"
                | Normal -> "Normal"
                | Loud -> "Loud"
            )
        )

    /// Structured `CellFormat` — not yet given a per-field JVal encoder; kept
    /// on the `Native` population (log-opaque) until one lands. Isolated here so
    /// the call sites read uniformly with the wire-encoded fields.
    let private pvCellFormat (f: CellFormat) : PropValue = PropValue.Native(boxNN f)

    /// Best-effort per-kind **scalar/text field** extraction → granular
    /// `UpdateProp`s. For the covered kinds, emit `UpdateProp(id, field, bValue)`
    /// for each scalar field whose value differs (detected closure-safely by
    /// canonical-isolation: setting a's field to b's value changes a's canonical
    /// iff they differed). Binding-typed slots + uncovered kinds yield nothing
    /// (and so fall through to the `EditNode` floor via the verify in
    /// `tryFieldLevel`). The field names match `Apply`'s `UpdateProp` dispatch
    /// exactly; `tryUnbox`'s fast path resolves a `box`ed typed value directly.
    let private extractFieldUpdates<'Msg> (a: Node<'Msg>) (b: Node<'Msg>) : TreeOp<'Msg> list =
        // Phase 692 — the category wrappers are gone; the kinds are flat, so the
        // rebuild helpers are the identity and are kept only to avoid touching
        // the fifty swap sites below.
        let disp (d: NodeKind<'Msg>) = d
        let lay (l: NodeKind<'Msg>) = l

        // Emit UpdateProp(field, b's `PropValue`) only when swapping a's field to
        // b's value actually changes a's canonical shell. The caller builds the
        // `PropValue` via the `pv*` encoders above — `Wire` for the fields that are
        // losslessly wire-representable (task 17: log-replayable), `Native` for the
        // structured / closure / None residue. `tryFieldLevel`'s verify guarantees
        // the round-trip regardless of the population chosen.
        let prop (field: string) (pv: PropValue) (aSwapped: Node<'Msg>) : TreeOp<'Msg> list =
            if CanonicalJson.encodeNode a <> CanonicalJson.encodeNode aSwapped then
                [ TreeOp.UpdateProp(a.Id, field, pv) ]
            else
                []

        // Option-typed flavour: a `None` payload (an inexpressible `Some → None`
        // transition — see the `pv*Opt` encoders) emits nothing, so the verify in
        // `tryFieldLevel` fails to reproduce `b` and the node takes the `EditNode`
        // floor instead of logging an unreplayable null.
        let propOpt (field: string) (pv: PropValue option) (aSwapped: Node<'Msg>) : TreeOp<'Msg> list =
            match pv with
            | Some v -> prop field v aSwapped
            | None -> []

        // Emit ReplaceBinding(slot, b's erased binding) only when swapping a's
        // slot to b's binding actually changes a's canonical shell.
        let bind (slot: string) (bObj: Binding<obj>) (aSwapped: Node<'Msg>) : TreeOp<'Msg> list =
            if CanonicalJson.encodeNode a <> CanonicalJson.encodeNode aSwapped then
                [ TreeOp.ReplaceBinding(a.Id, slot, bObj) ]
            else
                []

        match a.Kind, b.Kind with
        | NodeKind.Heading( sa), NodeKind.Heading( sb) ->
            prop
                "Level"
                (pvInt sb.Level)
                { a with
                    Kind = disp (NodeKind.Heading { sa with Level = sb.Level }) }
            @ prop
                "Text"
                (pvText sb.Text)
                { a with
                    Kind = disp (NodeKind.Heading { sa with Text = sb.Text }) }
            @ prop
                "Variant"
                (pvHeadingVariant sb.Variant)
                { a with
                    Kind = disp (NodeKind.Heading { sa with Variant = sb.Variant }) }
        | NodeKind.Markdown( sa), NodeKind.Markdown( sb) ->
            prop
                "Text"
                (pvText sb.Text)
                { a with
                    Kind = disp (NodeKind.Markdown { sa with Text = sb.Text }) }
        | NodeKind.Badge( sa), NodeKind.Badge( sb) ->
            prop
                "Label"
                (pvText sb.Label)
                { a with
                    Kind = disp (NodeKind.Badge { sa with Label = sb.Label }) }
            @ prop
                "Variant"
                (pvBadgeVariant sb.Variant)
                { a with
                    Kind = disp (NodeKind.Badge { sa with Variant = sb.Variant }) }
        | NodeKind.Metric( sa), NodeKind.Metric( sb) ->
            // Scalar fields + the Source binding slot. The optional Trend slot
            // (Binding option) falls to the EditNode floor (ReplaceBinding only
            // sets Some).
            prop
                "Label"
                (pvText sb.Label)
                { a with
                    Kind = disp (NodeKind.Metric { sa with Label = sb.Label }) }
            @ prop
                "Format"
                (pvCellFormat sb.Format)
                { a with
                    Kind = disp (NodeKind.Metric { sa with Format = sb.Format }) }
            @ prop
                "Tone"
                (pvTone sb.Tone)
                { a with
                    Kind = disp (NodeKind.Metric { sa with Tone = sb.Tone }) }
            @ prop
                "Weight"
                (pvWeight sb.Weight)
                { a with
                    Kind = disp (NodeKind.Metric { sa with Weight = sb.Weight }) }
            @ prop
                "Emphasis"
                (pvEmphasis sb.Emphasis)
                { a with
                    Kind = disp (NodeKind.Metric { sa with Emphasis = sb.Emphasis }) }
            @ bind
                "Value"
                (toObjBinding sb.Value)
                { a with
                    Kind = disp (NodeKind.Metric { sa with Value = sb.Value }) }
        | NodeKind.Callout( sa), NodeKind.Callout( sb) ->
            prop
                "Tone"
                (pvTone sb.Tone)
                { a with
                    Kind = disp (NodeKind.Callout { sa with Tone = sb.Tone }) }
            @ propOpt
                "Heading"
                (pvTextOpt sb.Heading)
                { a with
                    Kind = disp (NodeKind.Callout { sa with Heading = sb.Heading }) }
            @ prop
                "Body"
                (pvText sb.Body)
                { a with
                    Kind = disp (NodeKind.Callout { sa with Body = sb.Body }) }
            @ propOpt
                "Icon"
                (pvIconOpt sb.Icon)
                { a with
                    Kind = disp (NodeKind.Callout { sa with Icon = sb.Icon }) }
            @ prop
                "Dismissable"
                (pvBool sb.Dismissable)
                { a with
                    Kind = disp (NodeKind.Callout { sa with Dismissable = sb.Dismissable }) }
        | NodeKind.Progress( sa), NodeKind.Progress( sb) ->
            propOpt
                "Label"
                (pvTextOpt sb.Label)
                { a with
                    Kind = disp (NodeKind.Progress { sa with Label = sb.Label }) }
            @ propOpt
                "Caveat"
                (pvTextOpt sb.Caveat)
                { a with
                    Kind = disp (NodeKind.Progress { sa with Caveat = sb.Caveat }) }
            @ prop
                "Indeterminate"
                (pvBool sb.Indeterminate)
                { a with
                    Kind =
                        disp (
                            NodeKind.Progress
                                { sa with
                                    Indeterminate = sb.Indeterminate }
                        ) }
            @ prop
                "Tone"
                (pvTone sb.Tone)
                { a with
                    Kind = disp (NodeKind.Progress { sa with Tone = sb.Tone }) }
            @ bind
                "Fraction"
                (toObjBinding sb.Fraction)
                { a with
                    Kind = disp (NodeKind.Progress { sa with Fraction = sb.Fraction }) }
        | NodeKind.LabelValueRow( sa), NodeKind.LabelValueRow( sb) ->
            prop
                "Label"
                (pvText sb.Label)
                { a with
                    Kind = disp (NodeKind.LabelValueRow { sa with Label = sb.Label }) }
            @ prop
                "Format"
                (pvCellFormat sb.Format)
                { a with
                    Kind = disp (NodeKind.LabelValueRow { sa with Format = sb.Format }) }
            @ prop
                "Emphasis"
                (pvBool sb.Emphasis)
                { a with
                    Kind = disp (NodeKind.LabelValueRow { sa with Emphasis = sb.Emphasis }) }
            @ propOpt
                "Help"
                (pvTextOpt sb.Help)
                { a with
                    Kind = disp (NodeKind.LabelValueRow { sa with Help = sb.Help }) }
            @ bind
                "Value"
                (toObjBinding sb.Value)
                { a with
                    Kind = disp (NodeKind.LabelValueRow { sa with Value = sb.Value }) }
        | NodeKind.Link( sa), NodeKind.Link( sb) ->
            prop
                "Label"
                (pvText sb.Label)
                { a with
                    Kind = disp (NodeKind.Link { sa with Label = sb.Label }) }
            @ propOpt
                "Rel"
                (pvStrOpt sb.Rel)
                { a with
                    Kind = disp (NodeKind.Link { sa with Rel = sb.Rel }) }
            @ propOpt
                "Target"
                (pvStrOpt sb.Target)
                { a with
                    Kind = disp (NodeKind.Link { sa with Target = sb.Target }) }
            @ prop
                "Download"
                (pvBool sb.Download)
                { a with
                    Kind = disp (NodeKind.Link { sa with Download = sb.Download }) }
            @ bind
                "Href"
                (toObjBinding sb.Href)
                { a with
                    Kind = disp (NodeKind.Link { sa with Href = sb.Href }) }
        | NodeKind.Sparkline( sa), NodeKind.Sparkline( sb) ->
            bind
                "Source"
                (toObjBinding sb.Source)
                { a with
                    Kind = disp (NodeKind.Sparkline { sa with Source = sb.Source }) }
        | NodeKind.Skeleton( sa), NodeKind.Skeleton( sb) ->
            prop
                "Rows"
                (pvInt sb.Rows)
                { a with
                    Kind = disp (NodeKind.Skeleton { sa with Rows = sb.Rows }) }
        | NodeKind.Box( sa), NodeKind.Box( sb) ->
            // Phase 390 — diff the layout-mode fields (mirroring the retired
            // Stack/GridLayout diffs) + Heading. Propose-then-verify (below)
            // makes this safe: a mode or role change these granular ops can't
            // reproduce falls back to the EditNode floor, so replay stays exact.
            let layoutOps =
                match sa.Layout, sb.Layout with
                | BoxLayout.Flex fa, BoxLayout.Flex fb ->
                    prop
                        "Orientation"
                        (pvOrientation fb.Direction)
                        { a with
                            Kind =
                                lay (
                                    NodeKind.Box
                                        { sa with
                                            Layout = BoxLayout.Flex { fa with Direction = fb.Direction } }
                                ) }
                    @ prop
                        "Wrap"
                        (pvBool fb.Wrap)
                        { a with
                            Kind =
                                lay (
                                    NodeKind.Box
                                        { sa with
                                            Layout = BoxLayout.Flex { fa with Wrap = fb.Wrap } }
                                ) }
                | BoxLayout.Grid ga, BoxLayout.Grid gb ->
                    prop
                        "Cols"
                        (pvInt gb.Cols)
                        { a with
                            Kind =
                                lay (
                                    NodeKind.Box
                                        { sa with
                                            Layout = BoxLayout.Grid { ga with Cols = gb.Cols } }
                                ) }
                    @ propOpt
                        "TemplateColumns"
                        (pvStrOpt gb.TemplateColumns)
                        { a with
                            Kind =
                                lay (
                                    NodeKind.Box
                                        { sa with
                                            Layout =
                                                BoxLayout.Grid
                                                    { ga with
                                                        TemplateColumns = gb.TemplateColumns } }
                                ) }
                | _ -> []

            layoutOps
            @ propOpt
                "Heading"
                (pvTextOpt sb.Heading)
                { a with
                    Kind = lay (NodeKind.Box { sa with Heading = sb.Heading }) }
        | NodeKind.SplitPanel( sa), NodeKind.SplitPanel( sb) ->
            prop
                "Weight"
                (pvFloat sb.Weight)
                { a with
                    Kind = lay (NodeKind.SplitPanel { sa with Weight = sb.Weight }) }
        | NodeKind.Tabs( sa), NodeKind.Tabs( sb) ->
            prop
                "Orientation"
                (pvOrientation sb.Orientation)
                { a with
                    Kind = lay (NodeKind.Tabs { sa with Orientation = sb.Orientation }) }
        | NodeKind.SummaryList( sa), NodeKind.SummaryList( sb) ->
            propOpt
                "Heading"
                (pvTextOpt sb.Heading)
                { a with
                    Kind = lay (NodeKind.SummaryList { sa with Heading = sb.Heading }) }
        | NodeKind.Disclosure( sa), NodeKind.Disclosure( sb) ->
            prop
                "Heading"
                (pvText sb.Heading)
                { a with
                    Kind = lay (NodeKind.Disclosure { sa with Heading = sb.Heading }) }
            @ prop
                "DefaultOpen"
                (pvBool sb.DefaultOpen)
                { a with
                    Kind = lay (NodeKind.Disclosure { sa with DefaultOpen = sb.DefaultOpen }) }
            @ bind
                "Open"
                (toObjBinding sb.Open)
                { a with
                    Kind = lay (NodeKind.Disclosure { sa with Open = sb.Open }) }
        | NodeKind.Stepper( sa), NodeKind.Stepper( sb) ->
            bind
                "ActiveStep"
                (toObjBinding sb.ActiveStep)
                { a with
                    Kind = lay (NodeKind.Stepper { sa with ActiveStep = sb.ActiveStep }) }
        // Visualisation (Grid/Chart/Map), Input (Button/Select/Form/FileUpload),
        // Custom / ErrorBoundary / Fragment*, and Dashboard fall through to the
        // EditNode floor (still correct via the verify in `tryFieldLevel`).
        | _ -> []

    /// Try to express a kind own-field change as granular `UpdateProp`s instead
    /// of a wholesale `EditNode`. **Propose-then-verify:** extract candidate
    /// field ops, apply them to `a`, and accept only if the result's kind
    /// own-fields equal `b`'s (`not (kindChanged …)`). If the candidates don't
    /// fully reproduce the kind (a binding/uncovered field also drifted, an
    /// unknown field, etc.), return `None` so the caller uses the always-correct
    /// `EditNode` floor — round-trip can never break.
    let private tryFieldLevel<'Msg> (a: Node<'Msg>) (b: Node<'Msg>) : TreeOp<'Msg> list option =
        match extractFieldUpdates a b with
        | [] -> None
        | candidates ->
            let applied =
                candidates
                |> List.fold
                    (fun (n: Node<'Msg>) op ->
                        match Fuaran.UI.Ops.Apply.apply op n with
                        | Ok n' -> n'
                        | Error _ -> n)
                    a

            if not (kindChanged applied b) then
                Some candidates
            else
                None

    /// Diff two nodes known to share a NodeId (the root, or a child matched by
    /// id during recursion).
    let rec private diffNode<'Msg> (a: Node<'Msg>) (b: Node<'Msg>) : TreeOp<'Msg> list =
        let id = a.Id

        if kindChanged a b then
            match tryFieldLevel a b with
            | Some fieldOps ->
                // Granular field-level patch reproduced the kind own-fields — no
                // wholesale EditNode. State / style + children are handled
                // separately (UpdateProp brings neither).
                [ yield! fieldOps
                  if a.Style <> b.Style then
                      TreeOp.UpdateStyle(id, b.Style)
                  if stateChanged a b then
                      TreeOp.UpdateState(id, b.State)
                  yield! childrenDiff id (childrenOf a) (childrenOf b) ]
            | None ->
                // Floor: wholesale node replace — EditNode brings b's kind incl.
                // its whole subtree; UpdateState / UpdateStyle bring b's
                // state·style (EditNode preserves the old ones).
                [ TreeOp.EditNode(id, b.Kind)
                  TreeOp.UpdateState(id, b.State)
                  TreeOp.UpdateStyle(id, b.Style) ]
        else
            // Kind shape identical → no wholesale swap. Emit granular state /
            // style ops only when they actually drifted, and recurse into
            // children (a no-op for a leaf or identical children). This keeps a
            // style-toggle / loading-state change / deep child edit to a
            // minimal patch instead of re-sending the kind + subtree.
            [ if a.Style <> b.Style then
                  TreeOp.UpdateStyle(id, b.Style)
              if stateChanged a b then
                  TreeOp.UpdateState(id, b.State)
              yield! childrenDiff id (childrenOf a) (childrenOf b) ]

    /// Diff a parent's children lists (the parent already matched by id, own
    /// content equal). Sequence: remove a-only ids → append b-only subtrees →
    /// reorder to b's order → recurse into common children. Each step is
    /// id-addressed or append-at-tail, so the composition reconstructs b's
    /// children exactly (in b's order, each child equal to b's).
    and private childrenDiff<'Msg>
        (parentId: NodeId)
        (aKids: Node<'Msg> list)
        (bKids: Node<'Msg> list)
        : TreeOp<'Msg> list =
        let aIdSet = aKids |> List.map (fun n -> rawId n.Id) |> Set.ofList
        let bIdSet = bKids |> List.map (fun n -> rawId n.Id) |> Set.ofList

        // 1. Remove children whose id is gone in b.
        let removes =
            aKids
            |> List.filter (fun n -> not (bIdSet.Contains(rawId n.Id)))
            |> List.map (fun n -> TreeOp.RemoveNode n.Id)

        // After removals, the surviving children are the common ids in a-order.
        let commonAOrder =
            aKids
            |> List.filter (fun n -> bIdSet.Contains(rawId n.Id))
            |> List.map (fun n -> rawId n.Id)

        // 2. Insert b-only subtrees, appended at the growing tail.
        let bOnly = bKids |> List.filter (fun n -> not (aIdSet.Contains(rawId n.Id)))

        let inserts = bOnly |> List.map (fun child -> TreeOp.InsertChild(parentId, child))

        // 3. Reorder to b's exact order, if the post-insert order differs.
        let resultingOrder = commonAOrder @ (bOnly |> List.map (fun n -> rawId n.Id))
        let targetOrder = bKids |> List.map (fun n -> rawId n.Id)

        let reorders =
            if resultingOrder <> targetOrder then
                [ TreeOp.ReorderChildren(parentId, bKids |> List.map _.Id) ]
            else
                []

        // 4. Recurse into children present in both (content drift). Id-addressed,
        //    so order-independent of the reorder above.
        let recurse =
            bKids
            |> List.collect (fun bChild ->
                match aKids |> List.tryFind (fun aChild -> rawId aChild.Id = rawId bChild.Id) with
                | Some aChild -> diffNode aChild bChild
                | None -> [])

        removes @ inserts @ reorders @ recurse

    /// Diff `a` → `b` into an applyable `TreeOp` list. Folding the result
    /// through `Apply.apply` against `a` reconstructs `b` (canonical equality).
    /// Precondition: `a.Id = b.Id`.
    ///
    /// Cross-parent moves are handled by a **correct-by-construction pre-pass**:
    /// safe moves are emitted as `MoveNode`s (which append; the move-free diff's
    /// `ReorderChildren` fixes the final position — this diff already worked that
    /// way, using position 0 as a placeholder, so losing the ordinal changed
    /// nothing about its strategy) against an intermediate tree, then the
    /// existing move-free diff
    /// runs `intermediate → b`. Both segments round-trip, so the whole does.
    let diff<'Msg> (a: Node<'Msg>) (b: Node<'Msg>) : TreeOp<'Msg> list =
        if rawId a.Id <> rawId b.Id then
            // No TreeOp changes a node's Id — different roots are not
            // expressible. Best-effort wholesale replace at a's id (the
            // precondition is the caller's to honour).
            [ TreeOp.EditNode(a.Id, b.Kind)
              TreeOp.UpdateState(a.Id, b.State)
              TreeOp.UpdateStyle(a.Id, b.Style) ]
        else
            match detectMoves a b with
            | [] -> diffNode a b
            | moves ->
                let moveOps =
                    moves |> List.map (fun (id, newParent) -> TreeOp.MoveNode(id, newParent))

                // Fold the moves through the apply engine to get the post-move
                // intermediate, then diff the (now move-free) remainder. A move
                // that somehow fails to apply is simply left for the move-free
                // diff to express as remove+insert — no infinite loop, still
                // correct.
                let intermediate =
                    moveOps
                    |> List.fold
                        (fun (t: Node<'Msg>) op ->
                            match Fuaran.UI.Ops.Apply.apply op t with
                            | Ok t' -> t'
                            | Error _ -> t)
                        a

                moveOps @ diffNode intermediate b

    /// Diff `a` → `b` as a single atomic unit: wraps the op list in `Batch`
    /// when there are ≥2 ops (one interaction = one atomic patch, per Phase
    /// 152), passes a single op through unwrapped, and yields `[]` unchanged.
    let diffBatched<'Msg> (a: Node<'Msg>) (b: Node<'Msg>) : TreeOp<'Msg> list =
        match diff a b with
        | []
        | [ _ ] as ops -> ops
        | many -> [ TreeOp.Batch many ]
