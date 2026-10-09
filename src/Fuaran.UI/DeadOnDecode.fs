module Fuaran.UI.DeadOnDecode

// ============================================================================
//  The dead-on-decode lint (Phase 430) — the un-regression guarantee for the
//  declarative-floor family (423–428).
//
//  Run this over a DECODED (AI-authored / wire-ingested) tree, where every
//  closure slot is an inert placeholder by construction: a present handler is
//  a `"<closure>"` sentinel that never dispatches AND suppresses the
//  write-back default; a present display accessor renders blank AND shadows
//  the field-name form. `PreEmitValidate.validate` deliberately does NOT
//  include these findings — an F#-authored tree's closures are real and
//  legitimate; only on the decoded path is a closure slot dead by
//  construction. (The complementary omitted-handler checks — FUARAN069
//  inert control, FUARAN073 dropped result — live in `validate`, because
//  they hold for both authoring paths.)
//
//  Findings drive off the `SlotCapability` table's postures: `WriteBack` /
//  `FieldName` slots produce findings when present on a decoded tree, each
//  naming the slot, the node, and the declarative remedy (the GP5
//  enumerate-the-alternatives discipline applied to authorability);
//  `HostOnlyByDesign` slots stay silent. `ResultTarget` (Call.onResult) is
//  folded into the event-slot family here; the dropped-result case is
//  `validate`'s FUARAN073, referenced not duplicated.
// ============================================================================

open Fuaran.UI.Types

/// One lint finding: the owning node, the slot (the `SlotCapability`
/// vocabulary), the umbrella code, and the declarative remedy.
///   FUARAN080 — dead event slot on decode (a sentinel handler; inert AND
///               suppresses the write-back default).
///   FUARAN081 — blank display slot on decode (a sentinel accessor; renders
///               blank AND shadows the field-name form).
type LintFinding =
    { Node: string
      Slot: string
      Code: string
      Remedy: string }

let private deadEvent (node: string) (slot: string) (valueWritable: bool) (bindHint: string) : LintFinding =
    { Node = node
      Slot = slot
      Code = "FUARAN080"
      Remedy =
        if valueWritable then
            sprintf
                "omit %s — the value binding is already writable, so the write-back default takes over on the decoded path"
                slot
        else
            sprintf "omit %s and bind the control's value to %s so the write-back default can fire" slot bindHint }

let private blankDisplay (node: string) (slot: string) (remedy: string) : LintFinding =
    { Node = node
      Slot = slot
      Code = "FUARAN081"
      Remedy = remedy }

let private isWritable (binding: Binding<'T>) : bool =
    match binding with
    | Binding.State _
    | Binding.Filter(_, _) -> true
    | _ -> false

/// The optional-value-slot form (Phase 596): an OMITTED slot (`None`) is
/// writable by construction — the symmetric auto-bind gives the write-back
/// default `$state.<field id>` (or the chip's `$filters.<name>`) to write to.
let private isWritableOpt (binding: Binding<'T> option) : bool =
    match binding with
    | None -> true
    | Some b -> isWritable b

/// Every `Action.Call` with an `onResult` closure in `action` and every action
/// nested in it. Phase 2039 — the recursion is the generated `Action.fold`, so a
/// `Call` inside a `Confirm`'s continuation is reported exactly as one inside a
/// `Chain` always was; before, the confirmation hid it.
let private callFindings (node: string) (action: Action<'Msg>) : LintFinding list =
    Generated.Action.fold
        (fun found a ->
            found
            @ (match a with
               | Action.Call(_, Some _, into) ->
                   [ { Node = node
                       Slot = "Action.Call.onResult"
                       Code = "FUARAN080"
                       Remedy =
                         match into with
                         | Some _ -> "omit onResult — the into target already lands the response where readers look"
                         | None ->
                             "omit onResult and add into: {\"$type\":\"State\",\"key\":…} or {\"$type\":\"Query\",\"name\":…} so the response lands where a reader binds" } ]
               | _ -> []))
        []
        action

/// Lint a decoded tree for dead-on-decode slots. See the header for when to
/// run this (decoded / AI-ingested trees only — NOT F#-authored ones).
let lint<'Msg> (root: Node<'Msg>) : LintFinding list =
    let findings = ResizeArray<LintFinding>()

    let handler (node: string) (slot: string) (present: bool) (writable: bool) (hint: string) =
        if present then
            findings.Add(deadEvent node slot writable hint)

    let rec walk (n: Node<'Msg>) =
        // `Node.Id` is a bare string since the swap.
        let nodeId = n.Id

        // Phase 692 — one flat match, where this was three nested under the
        // category envelope. Every arm lints the node's OWN slots; the descent
        // reads the tier's one enumeration (`NodeChildren`) rather than a copy of it.
        match n.Kind with
        // -- Layout --
        | NodeKind.Box _ -> ()
        | NodeKind.SplitPanel _ -> ()
        | NodeKind.Stepper _ -> ()
        | NodeKind.SummaryList _ -> ()
        | NodeKind.ScrollArea _ -> ()
        | NodeKind.Modal s ->
            // `onDismiss` is a wire-survivable Action, never a
            // sentinel — only its interior Calls are lintable.
            s.OnDismiss |> Option.iter (fun a -> findings.AddRange(callFindings nodeId a))
        | NodeKind.Tabs s ->
            handler nodeId "TabsSpec.onSelect" s.OnSelect.IsSome (isWritable s.ActiveIndex) "$state (activeIndex)"

            handler
                nodeId
                "TabsSpec.onSelectTag"
                s.OnSelectTag.IsSome
                (s.ActiveTag |> Option.map isWritable |> Option.defaultValue false)
                "$state (activeTag)"

        | NodeKind.Disclosure s ->
            handler nodeId "DisclosureSpec.onToggle" s.OnToggle.IsSome (isWritable s.Open) "$state (open)"
        // -- Input --
        | NodeKind.Button b -> findings.AddRange(callFindings nodeId b.OnClick)
        | NodeKind.Form f ->
            findings.AddRange(callFindings nodeId f.OnSubmit)

            // Phase 2177 — every field kind carries ONE handler, `onChange`, and
            // one value slot; both are the generated projections, so a new
            // field kind is linted here with no arm to add.
            for field in f.Fields do
                handler
                    nodeId
                    "FormFieldKind.onChange"
                    (Generated.FormFieldKind.onChange field.Kind).IsSome
                    (isWritableOpt (Generated.FormFieldKind.value field.Kind))
                    "$state"

        | NodeKind.Select s ->
            handler nodeId "SelectSpec.onChange" s.OnChange.IsSome (isWritable s.Value) "$state (value)"

            handler
                nodeId
                "SelectSpec.onChangeMulti"
                s.OnChangeMulti.IsSome
                (s.Values |> Option.map isWritable |> Option.defaultValue false)
                "$state (values)"

        | NodeKind.Filters spec ->
            for fs in spec.Items do
                // A chip's write-back needs no writable value binding —
                // it writes its own `$filters.<name>` (423); a present
                // sentinel still suppresses that, so it is always dead.
                // 0.2.0 filters-unification: the chip's control is a
                // FormFieldKind; the same handler-presence probe applies.
                let present = (Generated.FormFieldKind.onChange fs.Kind).IsSome

                if present then
                    findings.Add(
                        { Node = nodeId
                          Slot = "FilterKind.onChange"
                          Code = "FUARAN080"
                          Remedy =
                            sprintf
                                "omit onChange on chip '%s' — a handler-free chip writes $filters.%s itself"
                                fs.Name
                                fs.Name }
                    )

        | NodeKind.FileUpload _ -> () // HostOnlyByDesign (see SlotCapability)
        // -- Visualisation --
        | NodeKind.DataGrid g ->
            handler nodeId "GridSpec.onRowClick" g.OnRowClick.IsSome true "$selection (its own NodeId)"

            if g.RowKey.IsSome then
                findings.Add(
                    blankDisplay
                        nodeId
                        "GridSpec.rowKey"
                        "replace the rowKey closure with rowKeyField: \"<row property>\" — the decoded closure yields a constant key (no stable identity)"
                )

            for col in g.Columns do
                if col.Value.IsSome then
                    findings.Add(
                        blankDisplay
                            nodeId
                            "ColumnErased.value"
                            (sprintf
                                "replace column '%s''s value closure with field: \"<row property>\" — the decoded closure renders blank and shadows the field form"
                                col.Label)
                    )

        | NodeKind.Chart c ->
            // Phase 933 gave `onPointClick` a write-back default (publish
            // the clicked datum under the chart's own NodeId), so the slot
            // became an override rather than a host escape — see the
            // capability row. A present sentinel is therefore dead AND
            // suppresses that default, which is precisely FUARAN080's
            // subject. The write-back needs no writable value binding, for
            // the same reason `GridSpec.onRowClick` does not: the node
            // writes its OWN id.
            handler nodeId "ChartSpec.onPointClick" c.OnPointClick.IsSome true "$selection (its own NodeId)"
        | NodeKind.Map _ -> () // marker clicks are still a HostOnlyByDesign row
        // -- Structural --
        | NodeKind.ErrorBoundary _ -> ()
        // Switch has no closure-bearing slots (StateKey is a string; the
        // cases/default are Nodes) — nothing dead-on-decode of its own; the
        // descent below reaches its cases and default.
        | NodeKind.Switch _
        | NodeKind.FragmentDecl _ -> ()
        // -- Display --
        // Leaves, with no closure-bearing slot this check can call dead.
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
        | NodeKind.Custom _
        | NodeKind.FragmentRef _
        | NodeKind.Mount _ -> ()

        NodeChildren.children NodeChildren.Reach.kindHeld n |> List.iter walk

    walk root
    List.ofSeq findings
