namespace Fuaran.UI.OpStream.Replay

open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops.Introspect
open Fuaran.UI.OpStream.Abstractions

// ============================================================================
//  TreeOpDiff — the **TreeOp-emitting** tree diff (Phase 152; on Fuaran.Core's
//  `Diff` since Phase 2304).
//
//  The inverse of `Apply.apply`: given two `Node<'Msg>` snapshots, produce a
//  `TreeOp<'Msg> list` that, folded through the apply engine against the old
//  tree, reconstructs the new one. The server-driven step, the Program driver,
//  the DAG merge's replay delta and the undo witness all read it.
//
//  **Round-trip contract (the acceptance test):**
//    fold Apply.apply a (diff a b)  ≡  b   (canonical-JSON equality)
//  Closure-safe equality is canonical-JSON equality (closures render as the
//  `"<closure>"` sentinel) — NOT F# structural equality, which a
//  closure-bearing kind does not support.
//
//  **The diff is Core's (Phase 2304).** This file holds no diff algorithm of
//  its own. The SCRIPT is `Fuaran.Core.Diff.toOpsContainedWith` over the
//  tier's witnesses (`Introspect.nodeWitness` / `idWitness`), the canonical
//  node encoder over each node's childless shell, and the apply engine's own
//  container predicate (a kind holds children iff `getChildren` answers). So
//  Core's `Diff` theorems are the UI script's: a survivor that changed parent
//  is relocated by `MoveNode` — never removed and re-sent — removals come
//  last, and the order is applyable by construction (D103's placement rule:
//  a node that becomes or stays a container is rewritten first, a node that
//  becomes a leaf last, once its children have left).
//
//  What is genuinely the tier's is the LOWERING of that script onto `TreeOp`:
//    - `InsertChild` / `MoveNode` / `RemoveNode` / `ReorderChildren` map one
//      to one. Core inserts an added region top-down as childless shells; a
//      region with no survivor below it is emitted as ONE `InsertChild`
//      carrying the whole new subtree, which is what those shell inserts
//      build (each appends under its after-parent in after-order, and none of
//      those parents needs a reorder), in one op.
//    - `UpdateNode` (a survivor whose own content changed) has no `TreeOp`
//      spelling, so it lowers to the vertical ops: field-level `UpdateProp` /
//      `ReplaceBinding` where they reproduce the kind (propose-then-verify,
//      below), else the `EditNode` floor — carrying the children the node
//      HOLDS at that point of the script, which is what Core's `UpdateNode`
//      keeps — then `UpdateState` when the state moved, and the style edit.
//    - a style change is ALWAYS field-level (Phase 2291): `PatchStyle` naming
//      the fields that moved, and the whole-record `UpdateStyle` only when
//      every one of them did (`styleEdit` below).
//
//  Core's refusals are the tier's by name (`tryDiff`): a tree that carries an
//  id twice is `DuplicateNodeId`, the apply engine's name for the same defect.
//  Two roots with different ids are NOT a refusal here: `ReplaceRoot` is the
//  one op that may swap the root id, and it is what every conformant host's
//  diff emits for that pair (the corpus `diff/goldens.json`).
//
//  **Closure-blindness (documented limitation, inherited).** A changed
//  `Action` / `Binding` closure that renders identically emits **no** op —
//  closures are the `"<closure>"` sentinel in the canonical shell, so two
//  nodes differing only in closure identity compare equal. Correct for the
//  DOM (identical markup); a consumer that cares about closure identity must
//  know this boundary.
//
//  **Node-trait vocabulary gap (documented limitation).** The canonical wire
//  surface is id + kind + state + style + **accessibility** + **tooltip**, but
//  the `TreeOp` vocabulary has **no op for either trait** (only
//  EditNode/UpdateState/UpdateStyle/PatchStyle + structural ops, and `EditNode`
//  replaces `Kind` alone). So a change to either is **not expressible** and does
//  not round-trip: an accessibility-only edit diffs to NO ops at all, and a
//  tooltip-only edit falls to the `EditNode` floor, which reinstates the kind
//  and leaves the trait as it was. Both traits are author-structural and stable
//  across a re-render, so the server-driven use case is unaffected — but it is a
//  real gap, and `ReplaceRoot` is the only op that closes it, wholesale.
//
//  **What is NOT acceptable is a caller that cannot tell.** Phase 1526: the DAG
//  merge takes this diff as the replay delta of a merge node that commits, by
//  hash, to the merged tree, so the mint REPLAYS its own delta and refuses the
//  node on a mismatch (`Fuaran.UI.OpStream.Dag.Merge.DagMerge.buildMergeRecord`),
//  and `DagReplay` reports the same mismatch on a node an older engine already
//  wrote. Closing the gap outright means new `TreeOp` cases, which is a wire
//  event under WIRE_FORMAT §11.
//
//  (`Motion` / `ExtraAttributes` are omitted from the canonical surface
//  entirely, so they are invisible to the round-trip — no gap there.)
//
//  FGP 2: FSharp.Core + Fuaran.Core + Fuaran.UI + Fuaran.UI.Ops +
//  OpStream.Abstractions only (CanonicalJson for the closure-safe shell) —
//  Fable-clean, no renderer dep.
// ============================================================================

module TreeOpDiff =

    // Stage 4b of the 692-694 swap: `Node.State` / `Node.Style` are OPTIONS
    // (`None` ≡ the old empty-state / default-style records — the canonical
    // encoder omits both spellings identically). The diff must not see the two
    // spellings as different, and the `UpdateState` / `UpdateStyle` ops still
    // carry the MATERIALISED records — so normalise at the boundary and the
    // emitted op sequence for any (a, b) pair is unchanged from pre-swap.
    let private styleOf<'Msg> (n: Node<'Msg>) : SemanticStyle =
        n.Style |> Option.defaultValue Fuaran.UI.Defaults.style

    /// The op that takes a node's style from `before` to `after` (Phase 2291):
    /// nothing when the two are equal; `PatchStyle` naming exactly the fields
    /// that moved; and the whole-record `UpdateStyle` only when EVERY field
    /// moved, the one case in which the record is the edit. So a lifted stream
    /// records a restyle at the granularity it happened, and two restyles of
    /// different fields on one node carry disjoint field sets.
    let styleEdit<'Msg> (id: NodeId) (before: SemanticStyle) (after: SemanticStyle) : TreeOp<'Msg> list =
        let delta = StyleDelta.between before after

        if StyleDelta.isEmpty delta then
            []
        elif StyleDelta.isTotal delta then
            [ TreeOp.UpdateStyle(id, after) ]
        else
            [ TreeOp.PatchStyle(id, delta) ]

    let private stateOf<'Msg> (n: Node<'Msg>) : StateBehaviour<'Msg> =
        n.State |> Option.defaultValue Fuaran.UI.Defaults.stateBehaviour


    // ── Own-content comparison (Phase 2063) ─────────────────────────────────
    //
    // Every comparison below is canonical-JSON equality over CHILDLESS shells
    // (`NodeIndex.encodeShell`), and `lowerUpdate` encodes each node's shell ONCE,
    // passing `b`'s shell down to the predicates that compare against it. Two
    // facts make that equivalent to the whole-node encodings it replaced:
    //   • a node's canonical JSON is an object whose members are its fields, so
    //     swapping one non-children field changes the whole-node bytes iff it
    //     changes the childless bytes — the children member is common to both;
    //   • equal canonical JSON parses to equal members, so two nodes with equal
    //     shells have equal kind own-fields, state, style, accessibility and
    //     tooltip — neither predicate below can then be `true`.

    /// `true` when the node's **kind own-fields** changed (the kind
    /// discriminator, or a scalar/binding field of the spec — children,
    /// state, style and accessibility excluded). Closure-safe: compares the
    /// childless kinds via canonical JSON with state·style·accessibility
    /// normalised to `b`'s, so only kind drift (or a tooltip, which no op can
    /// set) moves the needle. When this is `false`, the kind shape is identical
    /// and a wholesale `EditNode` (which would re-send the whole subtree) is
    /// unnecessary. `shellB` is `NodeIndex.encodeShell b`.
    let private kindChanged<'Msg> (a: Node<'Msg>) (b: Node<'Msg>) (shellB: string) : bool =
        NodeIndex.encodeShell
            { a with
                State = b.State
                Style = b.Style
                Accessibility = b.Accessibility }
        <> shellB

    /// `true` when the node's `State` block changed. Substituting `b`'s
    /// childless kind, style and accessibility into `a` leaves state, id and
    /// tooltip as the only members that can differ from `b`'s shell; both call
    /// sites in `lowerUpdate` have already established the tooltip is equal — one
    /// because `kindChanged` is `false`, the other because the field-level
    /// patch passed `tryFieldLevel`'s verify, which is `kindChanged` over the
    /// patched node and no field op sets a tooltip — and `lowerUpdate` is only
    /// reached for equal ids. So the comparison isolates `State`, whether or
    /// not the kind changed. Closure-safe via canonical JSON; `shellB` is
    /// `NodeIndex.encodeShell b`.
    let private stateChanged<'Msg> (a: Node<'Msg>) (b: Node<'Msg>) (shellB: string) : bool =
        NodeIndex.encodeShell
            { a with
                Kind = b.Kind
                Style = b.Style
                Accessibility = b.Accessibility }
        <> shellB

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
        | Binding.Static v -> Binding.Static(v |> Option.map boxNN)
        | Binding.Query(name, accessor, dependsOn) -> Binding.Query(name, accessor >> boxNN, dependsOn)
        | Binding.Filter(name, dv) -> Binding.Filter(name, dv |> Option.map boxNN)
        | Binding.Selection(id, accessor, dv, fld) ->
            Binding.Selection(id, accessor >> boxNN, dv |> Option.map boxNN, fld)
        | Binding.State(key, defaultValue) -> Binding.State(key, defaultValue |> Option.map boxNN)
        | Binding.Computed f -> Binding.Computed(f >> boxNN)
        | Binding.Now(accessor, grain) -> Binding.Now(accessor >> boxNN, grain)
        | Binding.I18n(key, args) -> Binding.I18n(key, args)
        // Fuaran-UI Phase 1538 — the two declared slots are wire data and cross
        // the erasure unchanged (the closures are what the erasure is about).
        | Binding.Local(flushOn, format, initialFrom, onCommit, parse, codec, commitTo) ->
            Binding.Local(
                flushOn,
                (fun (o: obj) -> format (unbox<'T> o)),
                toObjBinding initialFrom,
                onCommit |> Option.map (fun oc -> fun (o: obj) -> oc (unbox<'T> o)),
                (fun s -> parse s |> Result.map boxNN),
                codec,
                commitTo
            )
        | Binding.Format(source, format, locale) -> Binding.Format(source, format, locale)
        | Binding.Transform(source, pipeline, parameters) -> Binding.Transform(source, pipeline, parameters)
        // Fuaran-UI Phase 1534 — no 'T payload of its own (the evaluator produces the cell), so the
        // case erases unchanged, as `Transform` does above.
        | Binding.Expr(expr, parameters) -> Binding.Expr(expr, parameters)
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

    let private pvIconOpt (i: string option) : PropValue option =
        match i with
        | Some raw -> Some(PropValue.Wire(JStr raw))
        | None -> None

    let private pvOrientation (o: Orientation) : PropValue =
        PropValue.Wire(
            JStr(
                match o with
                | Orientation.Vertical -> "Vertical"
                | Orientation.Horizontal -> "Horizontal"
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
                | ToneVariant.Default -> "Default"
                | ToneVariant.Subdued -> "Subdued"
                | ToneVariant.Brand -> "Brand"
                | ToneVariant.Success -> "Success"
                | ToneVariant.Warning -> "Warning"
                | ToneVariant.Critical -> "Critical"
                | ToneVariant.Info -> "Info"
            )
        )

    let private pvWeight (w: StyleWeight) : PropValue =
        PropValue.Wire(
            JStr(
                match w with
                | StyleWeight.Compact -> "Compact"
                | StyleWeight.Standard -> "Standard"
                | StyleWeight.Spacious -> "Spacious"
            )
        )

    let private pvEmphasis (e: Emphasis) : PropValue =
        PropValue.Wire(
            JStr(
                match e with
                | Emphasis.Quiet -> "Quiet"
                | Emphasis.Normal -> "Normal"
                | Emphasis.Loud -> "Loud"
            )
        )

    /// Structured `CellFormat` — not yet given a per-field JVal encoder; kept
    /// on the `Native` population (log-opaque) until one lands. Isolated here so
    /// the call sites read uniformly with the wire-encoded fields.
    let private pvCellFormat (f: CellFormat) : PropValue = PropValue.Native(boxNN f)

    /// Best-effort per-kind **scalar/text field** extraction → granular
    /// `UpdateProp`s. For the covered kinds, emit `UpdateProp(id, field, bValue)`
    /// for each scalar field whose value differs (detected closure-safely by
    /// canonical-isolation: setting a's field to b's value changes a's childless
    /// shell, `shellA`, iff they differed — or, for a closure-free field with an
    /// injective encoding, by comparing the two values directly), and
    /// `ReplaceBinding(id, slot, binding)` for a covered binding slot that
    /// differs. Uncovered fields and kinds yield nothing (and so fall through to
    /// the `EditNode` floor via the verify in `tryFieldLevel`). The field names
    /// match `Apply`'s `UpdateProp` dispatch exactly; `tryUnbox`'s fast path
    /// resolves a `box`ed typed value directly.
    let private extractFieldUpdates<'Msg> (a: Node<'Msg>) (shellA: string) (b: Node<'Msg>) : TreeOp<'Msg> list =
        // Emit UpdateProp(field, b's `PropValue`) only when swapping a's field to
        // b's value actually changes a's canonical shell. The caller builds the
        // `PropValue` via the `pv*` encoders above — `Wire` for the fields that are
        // losslessly wire-representable (task 17: log-replayable), `Native` for the
        // structured / closure / None residue. `tryFieldLevel`'s verify guarantees
        // the round-trip regardless of the population chosen.
        let prop (field: string) (pv: PropValue) (aSwapped: Node<'Msg>) : TreeOp<'Msg> list =
            if NodeIndex.encodeShell aSwapped <> shellA then
                [ TreeOp.UpdateProp(NodeId a.Id, field, pv) ]
            else
                []

        // Phase 2063 — the structural flavour, for a field whose value carries no
        // closure and whose canonical encoding is injective: an `int`, a `bool`,
        // or a payload-free variant DU (each is emitted by the generated encoder,
        // omit-when-default included, so distinct values never share bytes).
        // There `<>` on the values IS the shell comparison, without an encode.
        // Strings and floats are deliberately NOT here: the canonical render
        // aliases a lone surrogate with U+FFFD and `-0` with `0`, so structural
        // inequality could emit an op the shell comparison would not.
        let scalar (field: string) (pv: PropValue) (differs: bool) : TreeOp<'Msg> list =
            if differs then
                [ TreeOp.UpdateProp(NodeId a.Id, field, pv) ]
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
            if NodeIndex.encodeShell aSwapped <> shellA then
                [ TreeOp.ReplaceBinding(NodeId a.Id, slot, bObj) ]
            else
                []

        match a.Kind, b.Kind with
        | NodeKind.Heading(sa), NodeKind.Heading(sb) ->
            scalar "Level" (pvInt sb.Level) (sa.Level <> sb.Level)
            @ prop
                "Text"
                (pvText sb.Text)
                { a with
                    Kind = NodeKind.Heading { sa with Text = sb.Text } }
            @ scalar "Variant" (pvHeadingVariant sb.Variant) (sa.Variant <> sb.Variant)
        | NodeKind.Markdown(sa), NodeKind.Markdown(sb) ->
            prop
                "Text"
                (pvText sb.Text)
                { a with
                    Kind = NodeKind.Markdown { sa with Text = sb.Text } }
        | NodeKind.Badge(sa), NodeKind.Badge(sb) ->
            prop
                "Label"
                (pvText sb.Label)
                { a with
                    Kind = NodeKind.Badge { sa with Label = sb.Label } }
            @ scalar "Variant" (pvBadgeVariant sb.Variant) (sa.Variant <> sb.Variant)
        | NodeKind.Metric(sa), NodeKind.Metric(sb) ->
            // Scalar fields + the Source binding slot. The optional Trend slot
            // (Binding option) falls to the EditNode floor (ReplaceBinding only
            // sets Some).
            prop
                "Label"
                (pvText sb.Label)
                { a with
                    Kind = NodeKind.Metric { sa with Label = sb.Label } }
            @ prop
                "Format"
                (pvCellFormat sb.Format)
                { a with
                    Kind = NodeKind.Metric { sa with Format = sb.Format } }
            @ scalar "Tone" (pvTone sb.Tone) (sa.Tone <> sb.Tone)
            @ scalar "Weight" (pvWeight sb.Weight) (sa.Weight <> sb.Weight)
            @ scalar "Emphasis" (pvEmphasis sb.Emphasis) (sa.Emphasis <> sb.Emphasis)
            @ bind
                "Value"
                (toObjBinding sb.Value)
                { a with
                    Kind = NodeKind.Metric { sa with Value = sb.Value } }
        | NodeKind.Callout(sa), NodeKind.Callout(sb) ->
            scalar "Tone" (pvTone sb.Tone) (sa.Tone <> sb.Tone)
            @ propOpt
                "Heading"
                (pvTextOpt sb.Heading)
                { a with
                    Kind = NodeKind.Callout { sa with Heading = sb.Heading } }
            @ prop
                "Body"
                (pvText sb.Body)
                { a with
                    Kind = NodeKind.Callout { sa with Body = sb.Body } }
            @ propOpt
                "Icon"
                (pvIconOpt sb.Icon)
                { a with
                    Kind = NodeKind.Callout { sa with Icon = sb.Icon } }
            @ scalar "Dismissable" (pvBool sb.Dismissable) (sa.Dismissable <> sb.Dismissable)
        | NodeKind.Progress(sa), NodeKind.Progress(sb) ->
            propOpt
                "Label"
                (pvTextOpt sb.Label)
                { a with
                    Kind = NodeKind.Progress { sa with Label = sb.Label } }
            @ propOpt
                "Caveat"
                (pvTextOpt sb.Caveat)
                { a with
                    Kind = NodeKind.Progress { sa with Caveat = sb.Caveat } }
            @ prop
                "Indeterminate"
                (pvBool sb.Indeterminate)
                { a with
                    Kind =

                        NodeKind.Progress
                            { sa with
                                Indeterminate = sb.Indeterminate } }
            @ scalar "Tone" (pvTone sb.Tone) (sa.Tone <> sb.Tone)
            @ bind
                "Fraction"
                (toObjBinding sb.Fraction)
                { a with
                    Kind = NodeKind.Progress { sa with Fraction = sb.Fraction } }
        | NodeKind.LabelValueRow(sa), NodeKind.LabelValueRow(sb) ->
            prop
                "Label"
                (pvText sb.Label)
                { a with
                    Kind = NodeKind.LabelValueRow { sa with Label = sb.Label } }
            @ prop
                "Format"
                (pvCellFormat sb.Format)
                { a with
                    Kind = NodeKind.LabelValueRow { sa with Format = sb.Format } }
            @ scalar "Emphasis" (pvBool sb.Emphasis) (sa.Emphasis <> sb.Emphasis)
            @ propOpt
                "Help"
                (pvTextOpt sb.Help)
                { a with
                    Kind = NodeKind.LabelValueRow { sa with Help = sb.Help } }
            @ bind
                "Value"
                (toObjBinding sb.Value)
                { a with
                    Kind = NodeKind.LabelValueRow { sa with Value = sb.Value } }
        | NodeKind.Link(sa), NodeKind.Link(sb) ->
            prop
                "Label"
                (pvText sb.Label)
                { a with
                    Kind = NodeKind.Link { sa with Label = sb.Label } }
            @ propOpt
                "Rel"
                (pvStrOpt sb.Rel)
                { a with
                    Kind = NodeKind.Link { sa with Rel = sb.Rel } }
            @ propOpt
                "Target"
                (pvStrOpt sb.Target)
                { a with
                    Kind = NodeKind.Link { sa with Target = sb.Target } }
            @ scalar "Download" (pvBool sb.Download) (sa.Download <> sb.Download)
            @ bind
                "Href"
                (toObjBinding sb.Href)
                { a with
                    Kind = NodeKind.Link { sa with Href = sb.Href } }
        | NodeKind.Sparkline(sa), NodeKind.Sparkline(sb) ->
            bind
                "Source"
                (toObjBinding sb.Source)
                { a with
                    Kind = NodeKind.Sparkline { sa with Source = sb.Source } }
        | NodeKind.Skeleton(sa), NodeKind.Skeleton(sb) -> scalar "Rows" (pvInt sb.Rows) (sa.Rows <> sb.Rows)
        | NodeKind.Box(sa), NodeKind.Box(sb) ->
            // Phase 390 — diff the layout-mode fields (mirroring the retired
            // Stack/GridLayout diffs) + Heading. Propose-then-verify (below)
            // makes this safe: a mode or role change these granular ops can't
            // reproduce falls back to the EditNode floor, so replay stays exact.
            // `BoxLayout` cases are POSITIONAL since the swap —
            // `Flex(direction, wrap, gap)` / `Grid(cols, templateColumns, gap)`.
            // Field coverage is unchanged (Direction→"Orientation", Wrap, Cols,
            // TemplateColumns); a `gap` drift is uncovered here, so it fails the
            // propose-then-verify in `tryFieldLevel` and takes the EditNode
            // floor — exactly the pre-swap behaviour for an uncovered field.
            let layoutOps =
                match sa.Layout, sb.Layout with
                | BoxLayout.Flex(dirA, wrapA, _), BoxLayout.Flex(dirB, wrapB, _) ->
                    scalar "Orientation" (pvOrientation dirB) (dirA <> dirB)
                    @ scalar "Wrap" (pvBool wrapB) (wrapA <> wrapB)
                | BoxLayout.Grid(colsA, tmplA, gapA), BoxLayout.Grid(colsB, tmplB, _) ->
                    scalar "Cols" (pvInt colsB) (colsA <> colsB)
                    @ propOpt
                        "TemplateColumns"
                        (pvStrOpt tmplB)
                        { a with
                            Kind =

                                NodeKind.Box
                                    { sa with
                                        Layout = BoxLayout.Grid(colsA, tmplB, gapA) } }
                // Masonry → Masonry diffs its one settable field. Without this
                // arm a column-count change between two masonry boxes falls to
                // `[]` and the diff silently reports no op, so a replay would
                // not reproduce it. Cross-MODE changes (Grid ↔ Masonry) stay in
                // the `_` arm deliberately: they are a layout REPLACEMENT, not a
                // field edit, and `UpdateProp` has no spelling for one.
                | BoxLayout.Masonry(colsA, _), BoxLayout.Masonry(colsB, _) ->
                    scalar "Cols" (pvInt colsB) (colsA <> colsB)
                | _ -> []

            layoutOps
            @ propOpt
                "Heading"
                (pvTextOpt sb.Heading)
                { a with
                    Kind = NodeKind.Box { sa with Heading = sb.Heading } }
        | NodeKind.SplitPanel(sa), NodeKind.SplitPanel(sb) ->
            prop
                "Weight"
                (pvFloat sb.Weight)
                { a with
                    Kind = NodeKind.SplitPanel { sa with Weight = sb.Weight } }
        | NodeKind.Tabs(sa), NodeKind.Tabs(sb) ->
            scalar "Orientation" (pvOrientation sb.Orientation) (sa.Orientation <> sb.Orientation)
        | NodeKind.SummaryList(sa), NodeKind.SummaryList(sb) ->
            propOpt
                "Heading"
                (pvTextOpt sb.Heading)
                { a with
                    Kind = NodeKind.SummaryList { sa with Heading = sb.Heading } }
        | NodeKind.Disclosure(sa), NodeKind.Disclosure(sb) ->
            prop
                "Heading"
                (pvText sb.Heading)
                { a with
                    Kind = NodeKind.Disclosure { sa with Heading = sb.Heading } }
            @ scalar "DefaultOpen" (pvBool sb.DefaultOpen) (sa.DefaultOpen <> sb.DefaultOpen)
            @ bind
                "Open"
                (toObjBinding sb.Open)
                { a with
                    Kind = NodeKind.Disclosure { sa with Open = sb.Open } }
        | NodeKind.Stepper(sa), NodeKind.Stepper(sb) ->
            bind
                "ActiveStep"
                (toObjBinding sb.ActiveStep)
                { a with
                    Kind = NodeKind.Stepper { sa with ActiveStep = sb.ActiveStep } }
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
    let private tryFieldLevel<'Msg>
        (a: Node<'Msg>)
        (shellA: string)
        (b: Node<'Msg>)
        (shellB: string)
        : TreeOp<'Msg> list option =
        match extractFieldUpdates a shellA b with
        | [] -> None
        | candidates ->
            let applied =
                candidates
                |> List.fold
                    (fun (n: Node<'Msg>) op ->
                        match Fuaran.UI.Ops.Apply.apply op n with
                        | Ok n' -> n'
                        // Swallowed on purpose: a candidate is emitted only for a field
                        // whose value differs from `b`'s, so one that fails to apply
                        // leaves that difference in place and the verify below rejects
                        // the whole set — the node takes the `EditNode` floor, and the
                        // error carries nothing the verify does not already decide.
                        | Error _ -> n)
                    a

            if not (kindChanged applied b shellB) then
                Some candidates
            else
                None


    // ── The lowering of Core's script (Phase 2304) ─────────────────────────

    /// A kind holds children in the script iff it holds a free list
    /// (`NodeIndex.holdsList`): every container the structural ops can grow and
    /// shrink, so the script is placed for the engine that will run it.
    let private canHold<'Msg> (n: Node<'Msg>) : bool = NodeIndex.holdsList n

    /// The ops a content change of one survivor lowers to — Core's `UpdateNode`
    /// spelt in the tier's vertical ops. `before` is the survivor in the old tree;
    /// `after` its counterpart in the new. Core places a node that is a container
    /// in `after` BEFORE every structural op, where it still holds `before`'s
    /// children, and a node that is a leaf in `after` AFTER them, where it holds
    /// none — so the `EditNode` floor carries `before`'s children in the first
    /// case and the leaf kind alone in the second, and keeps, as `UpdateNode`
    /// does, exactly the children the tree holds when it runs.
    let private lowerUpdate<'Msg> (before: Node<'Msg>) (after: Node<'Msg>) : TreeOp<'Msg> list =
        let id = NodeId after.Id
        let shellA = NodeIndex.encodeShell before
        let shellB = NodeIndex.encodeShell after

        if kindChanged before after shellB then
            match tryFieldLevel before shellA after shellB with
            | Some fieldOps ->
                // Granular field-level patch reproduced the kind own-fields — no
                // wholesale EditNode. Style compares via the materialised records
                // so `None` ≡ the default record (no spurious op).
                [ yield! fieldOps
                  yield! styleEdit id (styleOf before) (styleOf after)
                  if stateChanged before after shellB then
                      TreeOp.UpdateState(id, stateOf after) ]
            | None ->
                // Floor: the new kind, holding the children the node holds here.
                // EditNode preserves the old state·style, so the state is set
                // explicitly and the style by the fields that moved.
                let held =
                    if canHold after then
                        withChildren after.Kind (NodeIndex.children before)
                        |> Option.defaultValue after.Kind
                    else
                        after.Kind

                [ TreeOp.EditNode(id, held)
                  TreeOp.UpdateState(id, stateOf after)
                  yield! styleEdit id (styleOf before) (styleOf after) ]
        else
            // Kind shape identical: only the state and/or style moved (or a trait
            // no op expresses — see the preamble), each emitted only if it drifted.
            [ yield! styleEdit id (styleOf before) (styleOf after)
              if stateChanged before after shellB then
                  TreeOp.UpdateState(id, stateOf after) ]

    /// The ids of a subtree as the diff reads it (the node and every free-list
    /// descendant, `NodeIndex.children`) — the ids Core's index keys, so the ids
    /// a survivor test reads.
    let rec private structuralIds<'Msg> (acc: Set<string>) (n: Node<'Msg>) : Set<string> =
        NodeIndex.children n |> List.fold structuralIds (Set.add n.Id acc)

    /// Lower Core's script onto `TreeOp`, in Core's order. `beforeById` and
    /// `afterById` index the two trees (the diff witness's positions — Core's
    /// index), built only when an op needs one;
    /// an added region with no survivor below it is one `InsertChild` of the
    /// whole new subtree, and the shell inserts of its descendants are dropped.
    let private lowerScript<'Msg>
        (beforeById: Lazy<Map<string, Node<'Msg>>>)
        (afterById: Lazy<Map<string, Node<'Msg>>>)
        (script: SkeletonOp<Node<'Msg>, NodeId> list)
        : TreeOp<'Msg> list =
        let survives (id: string) = Map.containsKey id beforeById.Value
        let mutable covered = Set.empty<string>

        [ for op in script do
              match op with
              | SkeletonOp.InsertChild(parent, shell) ->
                  if not (covered.Contains shell.Id) then
                      match Map.tryFind shell.Id afterById.Value with
                      | Some whole ->
                          let region = structuralIds Set.empty whole

                          if region |> Set.exists survives then
                              TreeOp.InsertChild(parent, shell)
                          else
                              covered <- Set.union covered region
                              TreeOp.InsertChild(parent, whole)
                      | None -> TreeOp.InsertChild(parent, shell)
              | SkeletonOp.RemoveNode target -> TreeOp.RemoveNode target
              | SkeletonOp.MoveNode(target, newParent) -> TreeOp.MoveNode(target, newParent)
              | SkeletonOp.ReorderChildren(parent, order) -> TreeOp.ReorderChildren(parent, order)
              | SkeletonOp.UpdateNode after ->
                  match Map.tryFind after.Id beforeById.Value with
                  | Some before -> yield! lowerUpdate before after
                  // Unreachable: Core updates survivors only.
                  | None -> ()
              // Unreachable: the diff emits a flat script.
              | SkeletonOp.Batch _ -> () ]

    /// Index a tree by id over its structural positions (Core's index).
    let private byId<'Msg> (root: Node<'Msg>) : Map<string, Node<'Msg>> =
        Tree.preorder NodeIndex.witness<'Msg> root
        |> List.fold (fun (m: Map<string, Node<'Msg>>) n -> Map.add n.Id n m) Map.empty

    /// Core's refusal, under the tier's own name for the same defect.
    let private refusal (e: Rejection<NodeId>) : ApplyError =
        let raw (NodeId s) = s

        match e with
        | DuplicateId id ->
            { Code = ApplyErrorCode.DuplicateNodeId
              Message =
                $"The tree carries node id '{raw id}' more than once, so no id-addressed script reaches it (Fuaran.Core Rejection.DuplicateId)."
              Hint = ApplyHint.empty }
        | NotAContainer(target, kindTag) ->
            { Code = ApplyErrorCode.ChildlessKind
              Message =
                $"Node '{raw target}' ({kindTag}) holds children in the new tree but its kind cannot (Fuaran.Core Rejection.NotAContainer)."
              Hint =
                { ApplyHint.empty with
                    NodeKind = Some kindTag } }
        | IllegalChild(child, childKind, parent, parentKind, _) ->
            { Code = ApplyErrorCode.KindMismatch
              Message =
                $"Node '{raw child}' ({childKind}) sits under '{raw parent}' ({parentKind}), which may not hold it (Fuaran.Core Rejection.IllegalChild)."
              Hint = ApplyHint.empty }
        | RootIdMismatch(before, after) ->
            // `tryDiff` answers this pair with `ReplaceRoot` before Core is asked.
            { Code = ApplyErrorCode.KindMismatch
              Message =
                $"The roots carry different ids ('{raw before}', '{raw after}') (Fuaran.Core Rejection.RootIdMismatch)."
              Hint = ApplyHint.empty }
        // Since Fuaran.Core 0.39 (Phase 519) a diff refuses with the engine's `Rejection`; under the
        // contained scope it answers only the four cases above, so any other is a Core defect.
        | other ->
            invalidOp (sprintf "Fuaran.Core Diff.toOpsWith refused with %A, which a contained diff never answers" other)

    /// Diff `a` → `b` into an applyable `TreeOp` list, or Core's refusal under
    /// the tier's name for it. Folding an `Ok` script through `Apply.apply`
    /// against `a` reconstructs `b` (canonical equality, up to the traits no op
    /// expresses — see the preamble). Two roots with different ids diff to
    /// `[ ReplaceRoot b ]`, the one op that may change the root id.
    let tryDiff<'Msg> (a: Node<'Msg>) (b: Node<'Msg>) : Result<TreeOp<'Msg> list, ApplyError> =
        if a.Id <> b.Id then
            Ok [ TreeOp.ReplaceRoot b ]
        else
            match
                Diff.toOpsWith
                    CanonicalJson.encodeNode
                    Policy.none
                    (OpScope.contained canHold NodeIndex.witness<'Msg> idWitness)
                    a
                    b
            with
            | Ok [] -> Ok []
            | Ok script -> Ok(lowerScript (lazy (byId a)) (lazy (byId b)) script)
            | Error e -> Error(refusal e)

    /// Diff `a` → `b` into an applyable `TreeOp` list (`tryDiff`). A pair Core
    /// refuses — a tree carrying an id twice, which no id-addressed script can
    /// reach — diffs to the wholesale `[ ReplaceRoot b ]`, which reaches any
    /// `b`; a caller that must know the difference calls `tryDiff`.
    let diff<'Msg> (a: Node<'Msg>) (b: Node<'Msg>) : TreeOp<'Msg> list =
        match tryDiff a b with
        | Ok ops -> ops
        | Error _ -> [ TreeOp.ReplaceRoot b ]

    /// Diff `a` → `b` as a single atomic unit: wraps the op list in `Batch`
    /// when there are ≥2 ops (one interaction = one atomic patch, per Phase
    /// 152), passes a single op through unwrapped, and yields `[]` unchanged.
    let diffBatched<'Msg> (a: Node<'Msg>) (b: Node<'Msg>) : TreeOp<'Msg> list =
        match diff a b with
        | []
        | [ _ ] as ops -> ops
        | many -> [ TreeOp.Batch many ]

    // ── Undo (Phase 2304) ──────────────────────────────────────────────────

    /// The op as a Core skeleton op, when it is one of the structural five
    /// (`InsertChild` / `RemoveNode` / `MoveNode` / `ReorderChildren`, and a
    /// `Batch` of them); `None` for a vertical op or a batch holding one.
    let rec private toSkeleton<'Msg> (op: TreeOp<'Msg>) : SkeletonOp<Node<'Msg>, NodeId> option =
        match op with
        | TreeOp.InsertChild(parent, child) -> Some(SkeletonOp.InsertChild(parent, child))
        | TreeOp.RemoveNode target -> Some(SkeletonOp.RemoveNode target)
        | TreeOp.MoveNode(target, newParent) -> Some(SkeletonOp.MoveNode(target, newParent))
        | TreeOp.ReorderChildren(parent, order) -> Some(SkeletonOp.ReorderChildren(parent, order))
        | TreeOp.Batch inner ->
            let mapped = inner |> List.choose toSkeleton

            if List.length mapped = List.length inner then
                Some(SkeletonOp.Batch mapped)
            else
                None
        | _ -> None

    /// A structural inverse back on `TreeOp`. Core's inverse of a structural op
    /// is structural; an `UpdateNode` cannot arise, and answers `None`.
    let rec private ofSkeleton<'Msg> (op: SkeletonOp<Node<'Msg>, NodeId>) : TreeOp<'Msg> option =
        match op with
        | SkeletonOp.InsertChild(parent, child) -> Some(TreeOp.InsertChild(parent, child))
        | SkeletonOp.RemoveNode target -> Some(TreeOp.RemoveNode target)
        | SkeletonOp.MoveNode(target, newParent) -> Some(TreeOp.MoveNode(target, newParent))
        | SkeletonOp.ReorderChildren(parent, order) -> Some(TreeOp.ReorderChildren(parent, order))
        | SkeletonOp.Batch inner ->
            let mapped = inner |> List.choose ofSkeleton

            if List.length mapped = List.length inner then
                Some(TreeOp.Batch mapped)
            else
                None
        | SkeletonOp.UpdateNode _ -> None

    /// The ops that undo `op` against the tree it was applied to (`pre`): for
    /// the structural five, Core's `Ops.invert` over the tier's witnesses, whose
    /// law is `apply (invert op pre) (apply op pre) = pre`; for a vertical op —
    /// and a structural one Core cannot address, such as a node held at a
    /// non-structural position, which the tier's engine reaches and Core's
    /// structural witness does not — the diff from the post-state back to
    /// `pre`. An op the apply engine refuses has no post-state and answers `[]`.
    let inverse<'Msg> (op: TreeOp<'Msg>) (pre: Node<'Msg>) : TreeOp<'Msg> list =
        match Fuaran.UI.Ops.Apply.apply op pre with
        | Error _ -> []
        | Ok post ->
            let viaCore =
                toSkeleton op
                |> Option.bind (fun skeleton ->
                    match Ops.invert nodeWitness<_> idWitness skeleton pre with
                    | Ok inv -> ofSkeleton inv
                    | Error _ -> None)

            match viaCore with
            | Some inv -> [ inv ]
            | None -> diff post pre
