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

let rec private callFindings (node: string) (action: Action<'Msg>) : LintFinding list =
    match action with
    | Action.Call(_, Some _, into) ->
        [ { Node = node
            Slot = "Action.Call.onResult"
            Code = "FUARAN080"
            Remedy =
              match into with
              | Some _ -> "omit onResult — the into target already lands the response where readers look"
              | None ->
                  "omit onResult and add into: {\"$type\":\"State\",\"key\":…} or {\"$type\":\"Query\",\"name\":…} so the response lands where a reader binds" } ]
    | Action.Chain actions -> actions |> List.collect (callFindings node)
    | _ -> []

/// Lint a decoded tree for dead-on-decode slots. See the header for when to
/// run this (decoded / AI-ingested trees only — NOT F#-authored ones).
let lint<'Msg> (root: Node<'Msg>) : LintFinding list =
    let findings = ResizeArray<LintFinding>()

    let handler (node: string) (slot: string) (present: bool) (writable: bool) (hint: string) =
        if present then
            findings.Add(deadEvent node slot writable hint)

    let rec walk (n: Node<'Msg>) =
        let (NodeId nodeId) = n.Id

        match n.Kind with
        | NodeKind.Layout layout ->
            let children =
                match layout with
                | LayoutKind.Box s -> s.Children
                | LayoutKind.SplitPanel s -> s.Children
                | LayoutKind.Stepper s -> s.Children
                | LayoutKind.SummaryList s -> s.Children
                | LayoutKind.ScrollArea s -> s.Children
                | LayoutKind.Modal s ->
                    // `onDismiss` is a wire-survivable Action, never a
                    // sentinel — only its interior Calls are lintable.
                    s.OnDismiss |> Option.iter (fun a -> findings.AddRange(callFindings nodeId a))
                    s.Children
                | LayoutKind.Tabs s ->
                    handler
                        nodeId
                        "TabsSpec.onSelect"
                        s.OnSelect.IsSome
                        (isWritable s.ActiveIndex)
                        "$state (activeIndex)"

                    handler
                        nodeId
                        "TabsSpec.onSelectTag"
                        s.OnSelectTag.IsSome
                        (s.ActiveTag |> Option.map isWritable |> Option.defaultValue false)
                        "$state (activeTag)"

                    s.Children
                | LayoutKind.Disclosure s ->
                    handler nodeId "DisclosureSpec.onToggle" s.OnToggle.IsSome (isWritable s.Open) "$state (open)"
                    s.Children

            children |> List.iter walk
        | NodeKind.Input input ->
            match input with
            | InputKind.Button b -> findings.AddRange(callFindings nodeId b.OnClick)
            | InputKind.Form f ->
                findings.AddRange(callFindings nodeId f.OnSubmit)

                for field in f.Fields do
                    let slot kind = sprintf "FormFieldKind.%s" kind

                    match field.Kind with
                    | FormFieldKind.Text(v, oc) -> handler nodeId (slot "onChange") oc.IsSome (isWritable v) "$state"
                    | FormFieldKind.Number(v, oc) -> handler nodeId (slot "onChange") oc.IsSome (isWritable v) "$state"
                    | FormFieldKind.Range(v, oc, _) ->
                        handler nodeId (slot "onChange") oc.IsSome (isWritable v) "$state"
                    | FormFieldKind.Checkbox(v, ot) ->
                        handler nodeId (slot "onToggle") ot.IsSome (isWritable v) "$state"
                    | FormFieldKind.Choice(_, v, oc) ->
                        handler nodeId (slot "onChange") oc.IsSome (isWritable v) "$state"
                    | FormFieldKind.TextArea(v, oc, _) ->
                        handler nodeId (slot "onChange") oc.IsSome (isWritable v) "$state"
                    | FormFieldKind.RangedNumber(v, oc, _) ->
                        handler nodeId (slot "onChange") oc.IsSome (isWritable v) "$state"
                    | FormFieldKind.SegmentedChoice(_, v, oc, _) ->
                        handler nodeId (slot "onChange") oc.IsSome (isWritable v) "$state"
                    | FormFieldKind.Date(v, oc, _, _) ->
                        handler nodeId (slot "onChange") oc.IsSome (isWritable v) "$state"
            | InputKind.Select s ->
                handler nodeId "SelectSpec.onChange" s.OnChange.IsSome (isWritable s.Value) "$state (value)"

                handler
                    nodeId
                    "SelectSpec.onChangeMulti"
                    s.OnChangeMulti.IsSome
                    (s.Values |> Option.map isWritable |> Option.defaultValue false)
                    "$state (values)"
            | InputKind.Filters filters ->
                for fs in filters do
                    // A chip's write-back needs no writable value binding —
                    // it writes its own `$filters.<name>` (423); a present
                    // sentinel still suppresses that, so it is always dead.
                    // 0.2.0 filters-unification: the chip's control is a
                    // FormFieldKind; the same handler-presence probe applies.
                    let present =
                        match fs.Field with
                        | FormFieldKind.Text(_, oc) -> oc.IsSome
                        | FormFieldKind.Number(_, oc) -> oc.IsSome
                        | FormFieldKind.Checkbox(_, ot) -> ot.IsSome
                        | FormFieldKind.Choice(_, _, oc) -> oc.IsSome
                        | FormFieldKind.TextArea(_, oc, _) -> oc.IsSome
                        | FormFieldKind.RangedNumber(_, oc, _) -> oc.IsSome
                        | FormFieldKind.Range(_, oc, _) -> oc.IsSome
                        | FormFieldKind.SegmentedChoice(_, _, oc, _) -> oc.IsSome
                        | FormFieldKind.Date(_, oc, _, _) -> oc.IsSome

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
            | InputKind.FileUpload _ -> () // HostOnlyByDesign (see SlotCapability)
        | NodeKind.Visualisation vis ->
            match vis with
            | VisKind.DataGrid g ->
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
            | VisKind.Chart _
            | VisKind.Map _ -> () // click handlers are HostOnlyByDesign rows
        | NodeKind.ErrorBoundary spec ->
            walk spec.Child
            walk spec.Fallback
        | NodeKind.Switch spec ->
            // Switch has no closure-bearing slots (StateKey is a string; the
            // cases/default are Nodes) — nothing dead-on-decode of its own, so
            // just descend into the case children + default.
            spec.Cases |> List.iter (fun (_, child) -> walk child)
            walk spec.Default
        | NodeKind.FragmentDecl spec -> walk spec.Body
        | NodeKind.Display _
        | NodeKind.Custom _
        | NodeKind.FragmentRef _
        | NodeKind.Mount _ -> ()

    walk root
    List.ofSeq findings
