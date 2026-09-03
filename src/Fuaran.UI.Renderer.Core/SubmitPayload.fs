module Fuaran.UI.Renderer.SubmitPayload

open Fuaran.Core
open Fuaran.UI.Types

// ============================================================================
//  Phase 820 — form-submission submit-payload semantics.
//
//  The emission harvest settled the design: the dominant constructive prior for
//  a form's `onSubmit` is the HTML one — *submitting posts the fields*. So a
//  `Call` (and a `Notify`) appearing in a Form's `onSubmit` action receives the
//  form's field values as its payload, keyed by field id. **No new wire
//  shape**: `Action.Call` / `Action.Notify` encode exactly as before; this is
//  runtime semantics layered on the existing actions.
//
//  Two delivery shapes, one per action, dictated by the action's own slots:
//
//    • `Notify(channel, payload)` HAS a payload slot — the field values merge
//      into it under a `"fields"` key (`mergeIntoNotifyPayload`). An authored
//      `"fields"` key wins: the merge never clobbers what the author wrote.
//    • `Call(endpoint, onResult, into)` has NO payload slot (`into` unchanged),
//      so the invocation body is defined out-of-band as
//      `{"fields": {<id>: <value>, …}}` (`callBody`) and rides the executing
//      tier's host seam — the server-driven driver's `InterpretSubmitCall`
//      (see `Driver.DriverServices`).
//
//  This module is the pure, Fable-clean shared core: the field-value object,
//  the Notify merge, the `onSubmit` action-tree rewrite (Chain-recursive), and
//  the client-side harvest (each declared field's current value, read exactly
//  as the renderer's own controls read it). The `Local` / `CommitLocal`
//  ceremony remains the precision path for cross-field choreography.
// ============================================================================

/// The `Call` invocation body envelope: `{"fields": {<id>: <value>, …}}`.
/// `Call` carries no payload slot on the wire, so the executing tier hands
/// this to its host seam alongside the action (server-driven:
/// `DriverServices.InterpretSubmitCall`).
let callBody (fields: (string * JVal) list) : JVal = JObj [ "fields", JObj fields ]

/// Merge the submitted field values into a `Notify` payload under a `"fields"`
/// key. Never clobbers authored content: a payload that already carries a
/// `"fields"` key is returned untouched (the author's spelling wins), and a
/// non-object payload (an authored scalar / array) has no slot to merge into,
/// so it too is returned untouched.
let mergeIntoNotifyPayload (fields: (string * JVal) list) (payload: JVal) : JVal =
    match payload with
    | JObj entries when entries |> List.exists (fun (k, _) -> k = "fields") -> payload
    | JObj entries -> JObj(entries @ [ "fields", JObj fields ])
    | _ -> payload

/// Rewrite a form's `onSubmit` action tree with the submitted field values:
/// each `Notify` (at any `Chain` depth) gains the `"fields"` merge. Every
/// other action — including `Call`, whose body has no in-action slot and rides
/// the host seam instead — passes through structurally unchanged, so a form
/// whose `onSubmit` carries no `Notify` folds byte-identical actions.
let rec attachToAction (fields: (string * JVal) list) (action: Action<'Msg>) : Action<'Msg> =
    match action with
    | Action.Notify(channel, payload) -> Action.Notify(channel, mergeIntoNotifyPayload fields payload)
    | Action.Chain actions -> Action.Chain(actions |> List.map (attachToAction fields))
    | other -> other

// ─── the client-side harvest ─────────────────────────────────────────────────

/// One declared field's current value as a wire field, read EXACTLY as the
/// client renderer's own controls read it: `BindingResolver.tryResolve` over
/// the field's value binding, with the same Phase-596 auto-bind default
/// (`Binding.State(field.Id, <typed placeholder>)`) substituted for an absent
/// slot. Consequences worth stating:
///
///   • A `Binding.Local` field contributes its RESOLVED value (the
///     `initialFrom` re-sync source — i.e. the last-committed/external value),
///     not the uncommitted keystroke buffer, which is component-local.
///   • Pair-valued fields (`Range` / `DateRange`) contribute their FIRST
///     (min / from) input's value — mirroring the server-driven shim harvest,
///     which reads the one input carrying the `data-fuaran-field` marker.
///   • A `Choice` / `SegmentedChoice` with no selection contributes nothing.
///   • An unresolvable binding contributes nothing (no fabricated defaults
///     beyond the control's own placeholder default).
let private harvestField (sources: BindingResolver.BindingSources) (field: FormField<'Msg>) : (string * JVal) option =
    let resolveWith (placeholder: 'T option) (b: Binding<'T> option) : 'T option =
        b
        |> Option.defaultValue (Binding.State(field.Id, placeholder))
        |> BindingResolver.tryResolve sources

    match field.Kind with
    | FormFieldKind.Text(v, _) ->
        resolveWith (Some Fuaran.UI.Defaults.ControlValueDefaults.text) v
        |> Option.map (fun s -> field.Id, JStr s)
    | FormFieldKind.TextArea(v, _, _) ->
        resolveWith (Some Fuaran.UI.Defaults.ControlValueDefaults.text) v
        |> Option.map (fun s -> field.Id, JStr s)
    | FormFieldKind.Date(v, _, _, _, _, _) ->
        resolveWith (Some Fuaran.UI.Defaults.ControlValueDefaults.date) v
        |> Option.map (fun s -> field.Id, JStr s)
    | FormFieldKind.Number(v, _) ->
        resolveWith (Some Fuaran.UI.Defaults.ControlValueDefaults.number) v
        |> Option.map (fun n -> field.Id, JFloat n)
    | FormFieldKind.RangedNumber(v, _, _, _, _) ->
        resolveWith (Some Fuaran.UI.Defaults.ControlValueDefaults.number) v
        |> Option.map (fun n -> field.Id, JFloat n)
    | FormFieldKind.Checkbox(v, _) ->
        resolveWith (Some Fuaran.UI.Defaults.ControlValueDefaults.checkbox) v
        |> Option.map (fun b -> field.Id, JBool b)
    | FormFieldKind.Toggle(v, _) ->
        resolveWith (Some Fuaran.UI.Defaults.ControlValueDefaults.checkbox) v
        |> Option.map (fun b -> field.Id, JBool b)
    | FormFieldKind.Choice(_, v, _)
    | FormFieldKind.SegmentedChoice(_, v, _, _)
    // Phase 1113 — the combobox harvests as a choice: same value slot, same
    // no-selection contract. Free text is still just the string in that slot.
    | FormFieldKind.Combobox(_, _, _, v) ->
        // The choice value is `Binding<string>`; a null resolution (the
        // default-less auto-bind State resolving `Unchecked.defaultof<string>`)
        // is no-selection — contribute nothing, like an unselected `<select>`.
        resolveWith Fuaran.UI.Defaults.ControlValueDefaults.choice v
        |> Option.bind (fun s -> if isNull (box s) then None else Some(field.Id, JStr s))
    | FormFieldKind.Range(v, _, _, _, _) ->
        resolveWith (Some Fuaran.UI.Defaults.ControlValueDefaults.range) v
        |> Option.map (fun (p: RangePair) -> field.Id, JFloat p.Min)
    | FormFieldKind.DateRange(v, _, _, _, _, _) ->
        resolveWith (Some Fuaran.UI.Defaults.ControlValueDefaults.dateRange) v
        |> Option.map (fun (p: DateRangePair) -> field.Id, JStr p.From)

/// The client-side submit harvest: every declared field's current value (see
/// `harvestField` for exactly what "current" means per field shape), keyed by
/// field id, in field-declaration order — the same order the server-driven
/// tier's flush fold uses.
let harvestFields (sources: BindingResolver.BindingSources) (fields: FormField<'Msg> list) : (string * JVal) list =
    fields |> List.choose (harvestField sources)
