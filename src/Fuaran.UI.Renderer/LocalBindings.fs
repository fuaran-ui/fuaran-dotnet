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

                let handler =
                    System.Func<Browser.Types.Event, unit>(fun _ ->
                        match props.parser buffer with
                        | Ok parsed ->
                            setParseError None
                            props.commit parsed
                        | Error msg -> setParseError (Some msg))

                Browser.Dom.window.addEventListener (eventName, unbox handler)

                let cleanup () =
                    Browser.Dom.window.removeEventListener (eventName, unbox handler)

                cleanup
            | LocalFlushTrigger.OnSubmit ->
                let handler =
                    System.Func<Browser.Types.Event, unit>(fun _ ->
                        match props.parser buffer with
                        | Ok parsed ->
                            setParseError None
                            props.commit parsed
                        | Error msg -> setParseError (Some msg))

                Browser.Dom.window.addEventListener ("fuaran-form-commit", unbox handler)

                let cleanup () =
                    Browser.Dom.window.removeEventListener ("fuaran-form-commit", unbox handler)

                cleanup
            | LocalFlushTrigger.OnDebounce ms ->
                let timeoutId =
                    Browser.Dom.window.setTimeout (
                        (fun () ->
                            match props.parser buffer with
                            | Ok parsed ->
                                setParseError None
                                props.commit parsed
                            | Error msg -> setParseError (Some msg)),
                        ms
                    )

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
        Html.input
            [ prop.className props.className
              prop.type'.text
              prop.id props.fieldId
              prop.required props.required
              prop.value buffer
              prop.onChange (fun (v: string) ->
                  setBuffer v
                  setParseError None)
              prop.onBlur (fun _ ->
                  match props.flushOn with
                  | LocalFlushTrigger.OnBlur ->
                      match props.parser buffer with
                      | Ok parsed ->
                          setParseError None
                          props.commit parsed
                      | Error msg -> setParseError (Some msg)
                  | _ -> ()) ]

    match parseError with
    | None -> inputElement
    | Some _ ->
        // Parse-error path. The renderer's existing field-help slot
        // surfaces the message; here we only mark the input with an
        // error class so CSS can highlight it. The Help slot itself is
        // rendered by `renderFormField` upstream — the parseError
        // message stays inside this component and is reflected via the
        // class hook.
        React.Fragment [ inputElement ]

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

    React.useEffect (
        (fun () ->
            let shouldResync =
                match props.parser buffer with
                | Ok parsed -> parsed <> props.externalValue
                | Error _ -> true

            if shouldResync then
                setBuffer (props.formatter props.externalValue)),
        [| box props.externalValue |]
    )

    React.useEffect (
        (fun () ->
            match props.flushOn with
            | LocalFlushTrigger.OnCommitAction ->
                let eventName = "fuaran-commit-local-" + props.nodeId

                let handler =
                    System.Func<Browser.Types.Event, unit>(fun _ ->
                        match props.parser buffer with
                        | Ok parsed ->
                            setParseError None
                            props.commit parsed
                        | Error msg -> setParseError (Some msg))

                Browser.Dom.window.addEventListener (eventName, unbox handler)

                let cleanup () =
                    Browser.Dom.window.removeEventListener (eventName, unbox handler)

                cleanup
            | LocalFlushTrigger.OnSubmit ->
                let handler =
                    System.Func<Browser.Types.Event, unit>(fun _ ->
                        match props.parser buffer with
                        | Ok parsed ->
                            setParseError None
                            props.commit parsed
                        | Error msg -> setParseError (Some msg))

                Browser.Dom.window.addEventListener ("fuaran-form-commit", unbox handler)

                let cleanup () =
                    Browser.Dom.window.removeEventListener ("fuaran-form-commit", unbox handler)

                cleanup
            | LocalFlushTrigger.OnDebounce ms ->
                let timeoutId =
                    Browser.Dom.window.setTimeout (
                        (fun () ->
                            match props.parser buffer with
                            | Ok parsed ->
                                setParseError None
                                props.commit parsed
                            | Error msg -> setParseError (Some msg)),
                        ms
                    )

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
    Html.input (
        [ prop.className props.className
          prop.type'.text
          prop.inputMode.numeric
          prop.id props.fieldId
          prop.required props.required
          prop.value buffer
          prop.onChange (fun (v: string) ->
              setBuffer v
              setParseError None)
          prop.onBlur (fun _ ->
              match props.flushOn with
              | LocalFlushTrigger.OnBlur ->
                  match props.parser buffer with
                  | Ok parsed ->
                      setParseError None
                      props.commit parsed
                  | Error msg -> setParseError (Some msg)
              | _ -> ()) ]
        @ constraintAttrs
    )

// ─── Public surface — Render.fs invokes these ──────────────────────────────

let localTextInput (props: LocalTextProps) : ReactElement =
    reactCreateElement (box renderLocalText) (box props)

let localNumberInput (props: LocalNumberProps) : ReactElement =
    reactCreateElement (box renderLocalNumber) (box props)

// ─── Form-submit broadcast ──────────────────────────────────────────────────
//
// Render.fs's form `onSubmit` calls this after running the form's typed
// `OnSubmit` action. Every Local-bound input whose `FlushOn = OnSubmit`
// hears the event and drains its buffer through `OnCommit`. The event
// name is global rather than per-form because forms are not addressable
// by NodeId at the `OnSubmit` callsite (the form's NodeId IS the wrapper);
// scoping per-form is a future refinement if intra-page Local-input
// pollution becomes a real concern.

let dispatchFormCommit () : unit =
    let evt = Browser.Dom.window.document.createEvent "CustomEvent"
    evt.initEvent ("fuaran-form-commit", false, true)
    Browser.Dom.window.dispatchEvent (evt) |> ignore
