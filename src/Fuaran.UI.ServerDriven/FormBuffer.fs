module Fuaran.UI.ServerDriven.FormBuffer

open Fuaran.UI.Types
open Fuaran.UI.Ops.Introspect
open Fuaran.UI.ServerDriven.Validation
open Fuaran.UI.ServerDriven.Driver

// ============================================================================
//  Form / input / local-state buffer flush (Phase 152, Track C — the form
//  policy). Pairs with Phase 156 (validation) + Phase 158 QW3 (debounce).
//
//  ── Policy (b): client-buffered, server sees only the flush ────────────────
//  A `Binding.Local` field is the Fable tier's per-NodeId `useState` buffer. In
//  the server-driven tier there is no client `useState`; the field's **live DOM
//  value IS the buffer** (the generic shim does not round-trip its
//  `change` / `input` per keystroke — `Driver.step` already classifies a
//  form-field change as legitimate-but-no-server-action, the policy-(b) floor).
//  The server sees the buffer only on a **flush**:
//
//    • `onSubmit` — the form submit. The shim harvests every `data-fuaran-field`
//      value into the submit payload (keyed by field id); this module turns each
//      `Binding.Local` field's buffered value into its `OnCommit` action, folds
//      them (atomically, before the form's `OnSubmit`), then re-renders + diffs
//      → only the changed nodes patch.
//    • `Action.CommitLocal fieldId` — an explicit "Apply" button (the
//      `LocalFlushTrigger.OnCommitAction` analogue). The shim marks the button
//      `data-fuaran-commit` and harvests just that field; this module flushes it.
//
//  ── What is and isn't part of the protocol ─────────────────────────────────
//  Only `Binding.Local` value bindings flush here — that is the per-field buffer
//  the policy is about. A non-`Local` field binding (`Static` / `State` / …) is
//  NOT buffered by this protocol: it never had a client buffer, so a form built
//  from non-`Local` fields behaves exactly as the pre-policy 152 path (only
//  `OnSubmit` fires — the existing Phase 156 tests rely on this). This keeps the
//  flush additive: enabling the buffer protocol cannot change a non-`Local`
//  form's behaviour.
//
//  Pure + Fable-clean where it can be: `fieldFlushAction` / `commitActions` are
//  functions of `(field, value)` → `Action`; the `step` wrapper reuses the
//  shipped `Driver.applyResolvedActions` + `FormValidation` (declared
//  enforcement + field-error lowering) so the trust floor (G1 + Phase 156) is
//  shared, not re-implemented.
// ============================================================================

// ─── wire-value coercion (the buffered DOM value off the wire) ────────────────

let private asStr (v: LiveValue) : string option =
    match v with
    | LiveValue.Str s -> Some s
    | LiveValue.Num n -> Some(string n)
    | LiveValue.Bool b -> Some(if b then "true" else "false")
    | LiveValue.Null -> None

// ─── per-field flush ──────────────────────────────────────────────────────────

/// The flush `Action` for one `Binding.Local` field given its buffered wire
/// value. Recovers the author's typed flush handler from the field's `Local`
/// binding (`OnCommit`, obj-erased the same way `Action.Call`'s `onResult` is —
/// see `binding.local`) by parsing the buffered string through the binding's own
/// `Parse`. `None` when the field is not `Local` (not part of the buffer
/// protocol — see the module header) or the buffer fails to parse (a malformed
/// value flushes to nothing rather than mutating).
let fieldFlushAction (field: FormField<'Msg>) (value: LiveValue) : Action<'Msg> option =
    let commitOf (lb: LocalBinding<'T>) (s: string) : Action<'Msg> option =
        match lb.Parse s with
        | Ok t -> Some(unbox<Action<'Msg>> (lb.OnCommit t))
        | Error _ -> None

    match field.Kind with
    | FormFieldKind.Text(Binding.Local lb, _)
    | FormFieldKind.TextArea(Binding.Local lb, _, _) -> asStr value |> Option.bind (commitOf lb)
    | FormFieldKind.Number(Binding.Local lb, _)
    | FormFieldKind.RangedNumber(Binding.Local lb, _, _) -> asStr value |> Option.bind (commitOf lb)
    // Non-`Local` fields are not buffered by this protocol; other field shapes
    // (Checkbox / Choice / Segmented) carry no `Local` value binding.
    | _ -> None

/// The flush actions for every `Binding.Local` field of a form whose buffered
/// value rode in on the flush payload (`Map<fieldId, LiveValue>`). In field
/// declaration order, so `update` folds them deterministically.
let commitActions (form: FormSpec<'Msg>) (values: Map<string, LiveValue>) : Action<'Msg> list =
    form.Fields
    |> List.choose (fun field -> Map.tryFind field.Id values |> Option.bind (fieldFlushAction field))

/// Find a form field by id anywhere in the tree (an `Action.CommitLocal fieldId`
/// names a form field, not a `Node`, so `findNode` can't resolve it). Walks the
/// addressable node tree; a `Form` node's fields are checked directly.
let rec tryFindFormField (fieldId: string) (node: Node<'Msg>) : FormField<'Msg> option =
    let inChildren () =
        getChildren node.Kind
        |> Option.defaultValue []
        |> List.tryPick (tryFindFormField fieldId)

    match node.Kind with
    | NodeKind.Input(InputKind.Form form) ->
        match form.Fields |> List.tryFind (fun f -> f.Id = fieldId) with
        | Some _ as found -> found
        | None -> inChildren ()
    | _ -> inChildren ()

// ─── the form-aware step ──────────────────────────────────────────────────────

/// Step a session with the form buffer-flush protocol layered over `Driver.step`.
/// `validator` is the optional Phase 156 host business-rule validator (declared
/// constraints are always enforced when a validator is present — the trust
/// floor; `None` keeps the strictly-opt-in 152 behaviour).
///
///   • Form `submit` — G1 first (node / event / bounds / `OnSubmit` gate); then,
///     when a validator is present, declared + host validation BEFORE any
///     mutation (invalid → field-error patches, mutation suppressed). On a valid
///     submit, the buffered `Binding.Local` field values flush (each field's
///     `OnCommit`, gated) folded with `OnSubmit` in one atomic step, and the
///     stale field-error attributes clear.
///   • An explicit `Action.CommitLocal fieldId` (an "Apply" click) — flush just
///     that field's buffered value.
///   • Everything else delegates to `Driver.step` unchanged.
let step
    (validator: FormValidation.FormValidator<'Msg> option)
    (session: LiveSession<'Model, 'Msg>)
    (ev: LiveEvent)
    : LiveSession<'Model, 'Msg> * StepOutput =
    let rejected reason =
        session,
        { Patches = []
          Effects = []
          Rejected = Some reason }

    let noChange () =
        session,
        { Patches = []
          Effects = []
          Rejected = None }

    match findNode (NodeId ev.NodeId) session.Tree with
    | Some node ->
        match node.Kind with
        | NodeKind.Input(InputKind.Form form) when ev.Event = "submit" ->
            // G1 first — node exists, submit legitimate, payload in bounds, and
            // the resolved `OnSubmit` passes the dispatch policy gate.
            match validate session.Services.CanDispatch session.Tree ev with
            | Error reason -> rejected reason
            | Ok gated ->
                let errors =
                    match validator with
                    | Some host ->
                        let submission: FormValidation.FormSubmission<'Msg> =
                            { FormNodeId = ev.NodeId
                              Form = form
                              Values = ev.Payload }

                        FormValidation.enforceDeclared submission @ host submission
                    | None -> []

                match errors with
                | _ :: _ ->
                    // Invalid — surface the field errors, suppress the mutation.
                    // (`Rejected = None` — a business-validation failure is a
                    // legitimate gated submit, not a G1 boundary breach.)
                    session,
                    { Patches = FormValidation.lower form errors
                      Effects = []
                      Rejected = None }
                | [] ->
                    // Valid — flush the buffered Local fields (gated), then the
                    // form's OnSubmit, in one atomic diff/patch.
                    let fieldActions =
                        commitActions form ev.Payload |> List.filter session.Services.CanDispatch

                    let actions = fieldActions @ (gated.Action |> Option.toList)
                    let s2, out = applyResolvedActions session ev.NodeId actions
                    // Clear stale field-error attributes only when validation is
                    // active (so the non-validating 152 path's patch set is unchanged).
                    let clear =
                        match validator with
                        | Some _ -> FormValidation.lower form []
                        | None -> []

                    s2,
                    { out with
                        Patches = clear @ out.Patches }
        | _ ->
            // An explicit per-field flush? Peek the resolved action (G1-gated);
            // a CommitLocal names a form field whose buffered value the shim put
            // in the payload. Everything else is a normal Driver.step.
            match validate session.Services.CanDispatch session.Tree ev with
            | Ok { Action = Some(Action.CommitLocal fieldId) } ->
                match tryFindFormField fieldId session.Tree with
                | Some field ->
                    match Map.tryFind fieldId ev.Payload |> Option.bind (fieldFlushAction field) with
                    | Some action when session.Services.CanDispatch action ->
                        applyResolvedActions session ev.NodeId [ action ]
                    | _ -> noChange ()
                | None -> noChange ()
            | _ -> Driver.step session ev
    | None -> Driver.step session ev
