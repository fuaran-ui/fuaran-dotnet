module Fuaran.UI.Renderer.ComboboxControl

// ============================================================================
//  Fuaran — the WAI-ARIA combobox pattern (Phase 1113)
//
//  `FormFieldKind.Combobox` is the one control in the vocabulary whose
//  affordance cannot be reached by composing markup: a listbox popup that
//  filters as the reader types, with the ARIA relationships and the keyboard
//  interaction the pattern requires. Under the affordance→op charter NOTHING on
//  the wire names a keystroke — the document says what the options are, whether
//  free text is admitted, and where the value goes. Arrow, Enter, Escape, Home,
//  End, the popup, the highlight and `aria-activedescendant` are all this file's.
//
//  ── Why this is a function component and not a composition ─────────────────
//  The pattern is stateful in a way the tree is not: whether the popup is open,
//  which option is ACTIVE (highlighted, and not the same thing as selected), and
//  the reader's in-progress query all live for the duration of one interaction
//  and are never part of the document. The composition-based renderer holds no
//  hooks, so — exactly as `LocalBindings` does for the Local-buffer inputs — the
//  control is wrapped as a React function component and invoked through
//  `React.createElement`.
//
//  ── The pattern, as implemented (APG "combobox with listbox popup") ────────
//  * The `<input>` carries `role="combobox"`, `aria-expanded`,
//    `aria-controls` (the listbox id), `aria-autocomplete="list"` and
//    `aria-activedescendant` (the ACTIVE option's id, absent when none is).
//  * Focus never leaves the input. The listbox and its options are not focus
//    stops; the active option is named by `aria-activedescendant`, which is
//    what lets a screen reader announce the highlighted option while the
//    reader keeps typing.
//  * ArrowDown / ArrowUp move the active option, opening the popup if it is
//    closed and wrapping at both ends. Home / End jump to the first / last.
//    Enter commits the active option (and, when free text is admitted and no
//    option is active, commits what was typed). Escape closes the popup and
//    restores the committed value — a reader who opened it by accident is
//    returned to where they were, not left with a half-typed entry.
//  * A pointer commits by `mousedown`, NOT `click`: the input's `blur` would
//    otherwise fire first, close the popup, and unmount the option before the
//    click landed on it.
//
//  ── Free text, and the one thing this control will not do ─────────────────
//  With `allowFreeText = false` the reader may type anything (typing IS how the
//  list is searched), but only an option commits: blurring with an unmatched
//  entry restores the committed value rather than keeping it. That restore is an
//  AFFORDANCE and never a gate — the trust boundary is the server-side re-check
//  in `Fuaran.UI.ServerDriven.FormValidation`, on the standing rule that client
//  validation is not a trust boundary.
//
//  ── Cross-pipeline ─────────────────────────────────────────────────────────
//  `React.useState` / `React.useRef` are Feliz `jsNative` declarations that
//  compile on both .NET and Fable and throw on .NET if invoked. The renderer's
//  .NET tests never mount React; the SSR floor for this control is the server
//  renderer's `<input list>` + `<datalist>`, which needs no script at all.
// ============================================================================

#nowarn "3261"

open Fable.Core
open Feliz
open Fuaran.UI.Types

[<Import("createElement", "react")>]
let private reactCreateElement (componentFn: obj) (props: obj) : ReactElement = jsNative

/// Everything the control needs, already resolved: the renderer keeps its
/// `BindingSources` plumbing at the call site and this component holds only the
/// interaction.
type ComboboxProps =
    {| fieldId: string
       className: string
       listClassName: string
       required: bool
       allowFreeText: bool
       placeholder: string
       options: SelectOption list
       committed: string option
       commit: string option -> unit |}

/// The id an option element takes. Derived from the field id and the option's
/// INDEX rather than its value: `aria-activedescendant` must name an element
/// that exists in the DOM, and an option value can carry characters that are
/// not valid in an id.
let private optionId (fieldId: string) (index: int) = fieldId + "-option-" + string index

let private renderCombobox (props: ComboboxProps) : ReactElement =
    let committedLabel =
        props.committed
        |> Option.bind (fun v -> props.options |> List.tryFind (fun o -> o.Value = v))
        |> Option.map (fun o -> o.Label)
        // Free text that matches no option is its own label; a constrained
        // control cannot reach this branch with a value it did not commit.
        |> Option.orElse props.committed
        |> Option.defaultValue ""

    let query, setQuery = React.useState committedLabel
    let isOpen, setOpen = React.useState false
    let activeIndex, setActiveIndex = React.useState (-1)

    // Re-seed the entry text when the committed value changes underneath us
    // (another control writing the same state key, a server-driven patch). The
    // guard is the cursor-preservation invariant `LocalBindings` documents: only
    // re-seed when the popup is CLOSED, so an unrelated model update cannot
    // rewrite what the reader is halfway through typing.
    React.useEffect (
        (fun () ->
            if not isOpen then
                setQuery committedLabel),
        [| box committedLabel |]
    )

    /// The options the current query admits. An empty query shows everything —
    /// opening the popup with no text typed is how a reader browses the set.
    let visible =
        if query = "" then
            props.options
        else
            let needle = query.ToLowerInvariant()

            props.options
            |> List.filter (fun o ->
                o.Label.ToLowerInvariant().Contains needle
                || o.Value.ToLowerInvariant().Contains needle)

    let listId = props.fieldId + "-listbox"

    let close () =
        setOpen false
        setActiveIndex -1

    let commitOption (o: SelectOption) =
        setQuery o.Label
        close ()
        props.commit (Some o.Value)

    /// Leaving the control. A matched entry commits; an unmatched one commits
    /// only where free text is admitted, and otherwise the committed value is
    /// restored. Clearing the box clears the selection in both modes — an empty
    /// entry is "no value", which is the one reading both modes share.
    let settle () =
        close ()

        if query.Trim() = "" then
            setQuery ""
            props.commit None
        else
            match props.options |> List.tryFind (fun o -> o.Label = query || o.Value = query) with
            | Some o ->
                setQuery o.Label
                props.commit (Some o.Value)
            | None ->
                if props.allowFreeText then
                    props.commit (Some query)
                else
                    setQuery committedLabel

    let move (delta: int) =
        let count = List.length visible

        if count = 0 then
            setOpen true
        else
            setOpen true
            let next = activeIndex + delta

            let wrapped =
                if next < 0 then count - 1
                elif next >= count then 0
                else next

            setActiveIndex wrapped

    let onKeyDown (e: Browser.Types.KeyboardEvent) =
        match e.key with
        | "ArrowDown" ->
            e.preventDefault ()
            move 1
        | "ArrowUp" ->
            e.preventDefault ()
            move -1
        | "Home" when isOpen ->
            e.preventDefault ()

            if not (List.isEmpty visible) then
                setActiveIndex 0
        | "End" when isOpen ->
            e.preventDefault ()

            if not (List.isEmpty visible) then
                setActiveIndex (List.length visible - 1)
        | "Enter" ->
            if isOpen && activeIndex >= 0 && activeIndex < List.length visible then
                // The popup owns Enter while an option is highlighted; without
                // this the keystroke would submit the enclosing form and lose
                // the selection the reader was making.
                e.preventDefault ()
                commitOption (List.item activeIndex visible)
            elif isOpen then
                e.preventDefault ()
                settle ()
        | "Escape" ->
            if isOpen then
                e.preventDefault ()
                close ()
                setQuery committedLabel
        | "Tab" ->
            // Tab moves on and settles; it must NOT be swallowed, or the
            // control becomes a keyboard trap.
            settle ()
        | _ -> ()

    let activeDescendant =
        if isOpen && activeIndex >= 0 && activeIndex < List.length visible then
            Some(optionId props.fieldId activeIndex)
        else
            None

    let inputElement =
        Html.input
            [ prop.className props.className
              prop.type'.text
              prop.id props.fieldId
              prop.required props.required
              prop.placeholder props.placeholder
              prop.role "combobox"
              prop.ariaExpanded isOpen
              prop.custom ("aria-autocomplete", "list")
              prop.custom ("aria-controls", listId)
              prop.custom ("autocomplete", "off")
              match activeDescendant with
              | Some id -> prop.custom ("aria-activedescendant", id)
              | None -> ()
              prop.value query
              prop.onChange (fun (v: string) ->
                  setQuery v
                  setOpen true
                  setActiveIndex -1)
              prop.onKeyDown onKeyDown
              prop.onBlur (fun _ -> settle ()) ]

    let listElement =
        Html.ul
            [ prop.className props.listClassName
              prop.id listId
              prop.role "listbox"
              prop.hidden (not isOpen)
              prop.children
                  [ for i, o in List.indexed visible ->
                        Html.li
                            [ prop.key (string i + ":" + o.Value)
                              prop.id (optionId props.fieldId i)
                              prop.role "option"
                              prop.custom ("aria-selected", (if i = activeIndex then "true" else "false"))
                              prop.className (
                                  if i = activeIndex then
                                      "fuaran-combobox-option fuaran-combobox-option-active"
                                  else
                                      "fuaran-combobox-option"
                              )
                              // `mousedown`, not `click` — see the header. The
                              // input's blur would close the popup first and the
                              // click would land on nothing.
                              prop.onMouseDown (fun e ->
                                  e.preventDefault ()
                                  commitOption o)
                              prop.text o.Label ] ] ]

    Html.span
        [ prop.className "fuaran-combobox"
          prop.children [ inputElement; listElement ] ]

/// The public surface — `Render.fs` invokes this.
let combobox (props: ComboboxProps) : ReactElement =
    reactCreateElement (box renderCombobox) (box props)
