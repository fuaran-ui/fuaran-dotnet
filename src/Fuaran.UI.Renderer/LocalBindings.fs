module Fuaran.UI.Renderer.LocalBindings

// ============================================================================
//  Fuaran — `Binding<'T>.Local` render-time realisation
//
//  The renderer maintains a per-NodeId `React.useState` buffer for any
//  `FormFieldKind.Text` / `FormFieldKind.Number` whose `Value` is a
//  `Binding.Local`. Keystrokes update the buffer only; the typed
//  `OnCommit` action dispatches on the configured `FlushOn` boundary:
//
//    - `OnBlur`     — the input's `blur` event fires `OnCommit`.
//    - `OnSubmit`   — a window-level `fuaran-form-commit` event drains
//                     every Local-bound input scoped to a submitting Form.
//    - `OnDebounce` — a setTimeout reset on each keystroke fires
//                     `OnCommit` after the configured milliseconds.
//    - `OnCommitAction` — a window-level `fuaran-commit-local-<nodeId>`
//                     custom event triggered by `Action.CommitLocal nodeId`.
//
//  Cursor-preservation invariant. The re-sync `useEffect` only re-seeds
//  the buffer when the external `InitialFrom`-side value changes AND the
//  buffer's `Parse` result does not already equal the new external value.
//  This keeps the user's typing position intact through any unrelated
//  model update that causes the form to re-render.
//
//  Function-component plumbing. The composition-based renderer uses
//  no hooks. Local-bound inputs need hooks (useState
//  for the buffer, useEffect for the re-sync), so they're wrapped as
//  React function components and invoked via `React.createElement`. The
//  `[<Import("createElement", "react")>]` declaration below is the
//  hand-rolled bridge; we don't depend on Feliz.CompilerPlugins'
//  `[<ReactComponent>]` attribute (not pinned in this repo's
//  `Directory.Packages.props`).
//
//  Cross-pipeline. The `React.useState` / `React.useEffect` overloads are
//  Feliz `jsNative` declarations that compile on both .NET and Fable; on
//  .NET they throw at runtime if invoked. The renderer's .NET tests don't
//  mount React, so the .NET-side path is never reached.
// ============================================================================

#nowarn "3261"

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Fuaran.UI.Types

// ─── React.createElement bridge ─────────────────────────────────────────────
//
// `Feliz.ReactInternal` ships the same helpers but is namespaced under the
// Feliz internal module; importing directly keeps this file's dependency
// surface explicit. Both helpers take a function as the React component
// `type` argument; the props object's fields become the component's props.

[<Import("createElement", "react")>]
let private reactCreateElement (componentFn: obj) (props: obj) : ReactElement = jsNative

// ─── Form-scope test for the submit broadcast ──────────────────────────
//
// `fuaran-form-commit` is dispatched ON THE SUBMITTING FORM and bubbles, so the
// window-level listener every Local input already holds still hears it — and the
// event's own `target` is the form that fired it. An input is in scope exactly
// when that form CONTAINS the input's element. `contains` is the DOM's own
// answer, so a nested fieldset, a wrapper or a re-parented control is handled by
// the browser rather than by a shape assumed here.
//
// Written as an `Emit` rather than through `Browser.Dom` because it must answer
// FALSE for three different absences — no such element, no target at all, and a
// target that is not an element (a window-level dispatch) — and a listener that
// threw on any of them would take the whole submit down.
[<Emit("(function(ev, id){ var el = document.getElementById(id); var t = ev && ev.target; return !!(el && t && typeof t.contains === 'function' && t.contains(el)); })($0, $1)")>]
let private eventTargetContains (ev: obj) (elementId: string) : bool = jsNative

// ─── Surfacing a parse refusal ─────────────────────────────────────
//
// A `Binding.Local` parse failure used to set a piece of component state that
// nothing rendered: no class, no attribute, no message. The reader typed
// something the field could not accept, the value was silently not committed,
// and the only observable difference was that the model did not change.
//
// The markers are the ones the form gate ALREADY uses — `data-fuaran-field-error`
// carrying the message and `aria-invalid="true"` (`FieldRules.markUnmet`, which
// takes them from the server-driven tier's field-error patches). Reusing them is
// the point: a host's existing CSS hook and a screen reader treat a client-side
// parse refusal and a validation failure identically, and NO new class enters
// the vocabulary that is parity-locked with the other renderers.
//
// The message itself is rendered in a `.fuaran-form-help` element — the field's
// own help slot, styled by the reference sheet already — with `role="alert"` so
// it is announced when it appears, and `aria-describedby` tying it to the input
// so a reader arriving at the field later is told why it is invalid.

/// The id of the element carrying a field's refusal message.
let errorSlotId (fieldId: string) : string = fieldId + "-error"

/// The attribute PAIRS a refusal puts on the input, as data — the same split
/// `Accessibility.accessibilityAttributes` / `Render.toProps` already make, and
/// for the same reason: the pairs are assertable off a browser where an
/// `IReactProperty` is not, so what the DOM ends up carrying is a fact a test
/// can read rather than one only a rendered page can.
///
/// Empty when nothing is wrong, so a valid field emits byte-identical DOM to
/// before this change.
let invalidFieldAttributes (fieldId: string) (parseError: string option) : (string * string) list =
    match parseError with
    | None -> []
    | Some message ->
        [ "aria-invalid", "true"
          "data-fuaran-field-error", message
          "aria-describedby", errorSlotId fieldId ]

/// The same pairs as Feliz props.
let private invalidInputProps (fieldId: string) (parseError: string option) : IReactProperty list =
    invalidFieldAttributes fieldId parseError
    |> List.map (fun (name, value) -> prop.custom (name, value))

/// The message element itself, beside the input.
let private errorSlot (fieldId: string) (message: string) : ReactElement =
    Html.div
        [ prop.id (errorSlotId fieldId)
          prop.className "fuaran-form-help"
          prop.role "alert"
          prop.text message ]

// ─── Resolved-value sniff for the InitialFrom side ──────────────────────────
//
// The component takes the *already resolved* `InitialFrom` value as a prop
// so the renderer's `BindingSources` plumbing stays at the call site. The
// resolution happens in Render.fs before invoking the component; the
// component's job is only the local-buffer mechanic.

// ─── String-side Local input ────────────────────────────────────────────────

type LocalTextProps =
    {| nodeId: string
       fieldId: string
       className: string
       required: bool
       externalValue: string
       flushOn: LocalFlushTrigger
       formatter: string -> string
       parser: string -> Result<string, string>
       commit: string -> unit |}

let private renderLocalText (props: LocalTextProps) : ReactElement =
    let buffer, setBuffer = React.useState (props.formatter props.externalValue)
    let parseError, setParseError = React.useState (None: string option)

    // Has the READER touched this input since it was last seeded or committed?
    //
    // A ref rather than state, deliberately: the answer must not itself cause a
    // render, and it must be readable from inside an effect scheduled before it
    // changed. `OnDebounce` is the arm that needs it — that effect runs on mount
    // and on every external re-seed, so without this flag a debounced input
    // commits its own opening value the moment it appears and commits again
    // every time an unrelated model update re-seeds it. Each of those is a
    // dispatch, an op-stream entry and possibly a network call for a value
    // nobody typed.
    let dirty = React.useRef false

    /// Parse the buffer and either commit it or surface the refusal. One
    /// definition because five call sites need it to behave identically — the
    /// four flush triggers and the blur handler — and a parse failure must set
    /// the error state on every one of them rather than on whichever arms were
    /// remembered.
    let commitBuffer () =
        match props.parser buffer with
        | Ok parsed ->
            setParseError None
            dirty.current <- false
            props.commit parsed
        | Error msg -> setParseError (Some msg)

    // Re-sync invariant — runs when externalValue changes; re-seeds the
    // buffer only when the buffer's `Parse` result does not already equal
    // the new external value. Mid-edit typing survives unrelated re-renders.
    React.useEffect (
        (fun () ->
            let shouldResync =
                match props.parser buffer with
                | Ok parsed -> parsed <> props.externalValue
                | Error _ -> true

            if shouldResync then
                // An external re-seed is not the reader's edit, so it clears
                // both the dirty flag and any refusal the discarded buffer was
                // carrying — that message described text no longer in the field.
                dirty.current <- false
                setParseError None
                setBuffer (props.formatter props.externalValue)),
        [| box props.externalValue |]
    )

    // OnCommitAction listener — addEventListener returns the disposal
    // closure; React calls it on cleanup so the listener doesn't leak
    // across re-renders.
    React.useEffect (
        (fun () ->
            match props.flushOn with
            | LocalFlushTrigger.OnCommitAction ->
                let eventName = "fuaran-commit-local-" + props.nodeId

                let handler = System.Func<Browser.Types.Event, unit>(fun _ -> commitBuffer ())

                Browser.Dom.window.addEventListener (eventName, unbox handler)

                let cleanup () =
                    Browser.Dom.window.removeEventListener (eventName, unbox handler)

                cleanup
            | LocalFlushTrigger.OnSubmit ->
                // Scoped to the SUBMITTING form. The broadcast used to drain
                // EVERY `OnSubmit` Local input on the page, so a search box in a
                // header or another form's fields committed — dispatching an
                // action and writing the model — on a gesture the reader made
                // somewhere else entirely.
                let handler =
                    System.Func<Browser.Types.Event, unit>(fun ev ->
                        if eventTargetContains (box ev) props.fieldId then
                            commitBuffer ())

                Browser.Dom.window.addEventListener ("fuaran-form-commit", unbox handler)

                let cleanup () =
                    Browser.Dom.window.removeEventListener ("fuaran-form-commit", unbox handler)

                cleanup
            | LocalFlushTrigger.OnDebounce ms ->
                if not dirty.current then
                    // Mount, or an external re-seed. Neither is an edit, so
                    // there is nothing to debounce and nothing to commit.
                    ignore
                else
                    let timeoutId = Browser.Dom.window.setTimeout ((fun () -> commitBuffer ()), ms)

                    let cleanup () =
                        Browser.Dom.window.clearTimeout timeoutId

                    cleanup
            | LocalFlushTrigger.OnBlur ->
                // Wired inline on the input's `prop.onBlur` — nothing to
                // listen for at the window level.
                ignore),
        [| box buffer; box props.flushOn |]
    )

    let inputElement =
        Html.input (
            [ prop.className props.className
              prop.type'.text
              prop.id props.fieldId
              prop.required props.required
              prop.value buffer
              prop.onChange (fun (v: string) ->
                  dirty.current <- true
                  setBuffer v
                  setParseError None)
              prop.onBlur (fun _ ->
                  match props.flushOn with
                  | LocalFlushTrigger.OnBlur -> commitBuffer ()
                  | _ -> ()) ]
            @ invalidInputProps props.fieldId parseError
        )

    match parseError with
    | None -> inputElement
    | Some message -> React.Fragment [ inputElement; errorSlot props.fieldId message ]

// ─── Number-side Local input ────────────────────────────────────────────────

type LocalNumberProps =
    {| nodeId: string
       fieldId: string
       className: string
       required: bool
       externalValue: float
       flushOn: LocalFlushTrigger
       formatter: float -> string
       parser: string -> Result<float, string>
       commit: float -> unit
       constraints: NumberFieldConstraints |}

let private renderLocalNumber (props: LocalNumberProps) : ReactElement =
    let buffer, setBuffer = React.useState (props.formatter props.externalValue)
    let parseError, setParseError = React.useState (None: string option)

    // See `renderLocalText` for why this is a ref, and for what `OnDebounce`
    // does without it.
    let dirty = React.useRef false

    let commitBuffer () =
        match props.parser buffer with
        | Ok parsed ->
            setParseError None
            dirty.current <- false
            props.commit parsed
        | Error msg -> setParseError (Some msg)

    React.useEffect (
        (fun () ->
            let shouldResync =
                match props.parser buffer with
                | Ok parsed -> parsed <> props.externalValue
                | Error _ -> true

            if shouldResync then
                dirty.current <- false
                setParseError None
                setBuffer (props.formatter props.externalValue)),
        [| box props.externalValue |]
    )

    React.useEffect (
        (fun () ->
            match props.flushOn with
            | LocalFlushTrigger.OnCommitAction ->
                let eventName = "fuaran-commit-local-" + props.nodeId

                let handler = System.Func<Browser.Types.Event, unit>(fun _ -> commitBuffer ())

                Browser.Dom.window.addEventListener (eventName, unbox handler)

                let cleanup () =
                    Browser.Dom.window.removeEventListener (eventName, unbox handler)

                cleanup
            | LocalFlushTrigger.OnSubmit ->
                // Scoped to the SUBMITTING form. The broadcast used to drain
                // EVERY `OnSubmit` Local input on the page, so a search box in a
                // header or another form's fields committed — dispatching an
                // action and writing the model — on a gesture the reader made
                // somewhere else entirely.
                let handler =
                    System.Func<Browser.Types.Event, unit>(fun ev ->
                        if eventTargetContains (box ev) props.fieldId then
                            commitBuffer ())

                Browser.Dom.window.addEventListener ("fuaran-form-commit", unbox handler)

                let cleanup () =
                    Browser.Dom.window.removeEventListener ("fuaran-form-commit", unbox handler)

                cleanup
            | LocalFlushTrigger.OnDebounce ms ->
                if not dirty.current then
                    // Mount, or an external re-seed. Neither is an edit, so
                    // there is nothing to debounce and nothing to commit.
                    ignore
                else
                    let timeoutId = Browser.Dom.window.setTimeout ((fun () -> commitBuffer ()), ms)

                    let cleanup () =
                        Browser.Dom.window.clearTimeout timeoutId

                    cleanup
            | LocalFlushTrigger.OnBlur -> ignore),
        [| box buffer; box props.flushOn |]
    )

    // Emit Min / Max / Step when present. Local-bound numbers
    // render as `type=text` + `inputMode=numeric` (not `type=number`) so
    // formatted entry survives; the browser will not enforce min/max on
    // a text input, but the attributes still surface to screen readers
    // and let assistive tech announce the constraint. Same surface as
    // the plain `Number` arm — the FUARAN051 validator catches static
    // out-of-range literals at build time regardless.
    let constraintAttrs =
        [ match props.constraints.Min with
          | Some m -> prop.min m
          | None -> ()
          match props.constraints.Max with
          | Some m -> prop.max m
          | None -> ()
          match props.constraints.Step with
          | Some s -> prop.step s
          | None -> () ]

    // Numeric-formatted inputs render as `type=text` (not `type=number`)
    // so the consumer's `formatter` thousands-separator survives — a
    // `type=number` input rejects non-numeric characters like commas.
    let inputElement =
        Html.input (
            [ prop.className props.className
              prop.type'.text
              prop.inputMode.numeric
              prop.id props.fieldId
              prop.required props.required
              prop.value buffer
              prop.onChange (fun (v: string) ->
                  dirty.current <- true
                  setBuffer v
                  setParseError None)
              prop.onBlur (fun _ ->
                  match props.flushOn with
                  | LocalFlushTrigger.OnBlur -> commitBuffer ()
                  | _ -> ()) ]
            @ constraintAttrs
            @ invalidInputProps props.fieldId parseError
        )

    match parseError with
    | None -> inputElement
    | Some message -> React.Fragment [ inputElement; errorSlot props.fieldId message ]

// ─── Public surface — Render.fs invokes these ──────────────────────────────

let localTextInput (props: LocalTextProps) : ReactElement =
    reactCreateElement (box renderLocalText) (box props)

let localNumberInput (props: LocalNumberProps) : ReactElement =
    reactCreateElement (box renderLocalNumber) (box props)

// ─── Form-submit broadcast ──────────────────────────────────────────────────
//
// Render.fs's form `onSubmit` calls this before running the form's typed
// `OnSubmit` action, so every Local-bound input whose `FlushOn = OnSubmit`
// drains its buffer through `OnCommit` first — the order matters, because the
// typed handler may dispatch a network call that reads the just-committed
// values.
//
// SCOPED TO THE SUBMITTING FORM. It used to be a window-level dispatch that
// every `OnSubmit` input on the page answered, so a submit anywhere drained a
// search box in the header and any other form's fields — dispatching their
// `OnCommit` actions and writing their values into the model on a gesture the
// reader made somewhere else. The event is now dispatched ON the form and
// BUBBLES, so the window-level listeners still hear it and each one asks
// whether the form that fired it contains its own input.
//
// The form ELEMENT rather than a node id: a form's NodeId is its wrapper's, so
// there is nothing addressable to match on, and `contains` is a question the
// DOM already answers correctly for every nesting an author can build.

let dispatchFormCommit (formElement: Browser.Types.Element) : unit =
    let evt = Browser.Dom.window.document.createEvent "CustomEvent"
    // bubbles = true: the listeners sit on `window` and this is dispatched on
    // the form, so without bubbling nothing would hear it at all.
    evt.initEvent ("fuaran-form-commit", true, true)
    formElement.dispatchEvent (evt) |> ignore
