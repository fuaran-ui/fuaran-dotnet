module Fuaran.UI.Renderer.TokensControl

// ============================================================================
//  Fuaran — the multi-token control (Phase 1121)
//
//  `FormFieldKind.Tokens` is the second control in the vocabulary whose
//  affordance cannot be reached by composing markup: a row of removable chips
//  beside an entry box, with an optional suggestion popup, and a keyboard model
//  in which Enter, Backspace and Delete all mean different things depending on
//  where focus is and what is typed. Under the affordance→op charter NOTHING on
//  the wire names a keystroke — the document says what the tokens are, whether
//  free text is admitted, where the suggestions come from and where the value
//  goes.
//
//  ── What is HERE and what is in `TokensModel` ─────────────────────────────
//  Every JUDGEMENT is in `Fuaran.UI.Renderer.TokensModel`: whether an entry may
//  become a token, what the list becomes, which suggestions to show, what a
//  chip reads as. This file holds only the React state (popup open, active
//  suggestion, focused chip, the in-progress entry) and the markup. The split
//  is `RatingControl`'s, for `RatingModel`'s reason — the .NET runner mounts no
//  DOM, so a decision inside this component would be unreachable by any test in
//  this repo.
//
//  ── The pattern, as implemented ───────────────────────────────────────────
//  * The CHIP ROW is `role="list"` of `role="listitem"`, each carrying a real
//    `<button>` that removes it. Not a listbox of options — see the ARIA
//    decision recorded in `TokensModel`'s header.
//  * The ENTRY INPUT is a `role="combobox"` when a suggestion source is present
//    (`aria-expanded`, `aria-controls`, `aria-autocomplete="list"`,
//    `aria-activedescendant`), and a plain text input when there is none: a
//    combobox role with nothing to expand is an overclaim, and the same one the
//    SSR floor is forbidden to make.
//  * ENTER commits — the active suggestion when one is highlighted, otherwise
//    what was typed. It is `preventDefault`ed whenever it commits, or the
//    keystroke submits the enclosing form and loses the token the reader was
//    adding. On an empty box with no popup open it is NOT swallowed, so a form
//    whose only field is a token box can still be submitted by keyboard.
//  * BACKSPACE on an EMPTY entry takes the last chip back. This is the one
//    gesture readers expect from every token control on the web and the reason
//    the model has `removeLast`. With text in the box it is an ordinary
//    backspace and is not intercepted at all.
//  * DELETE (and Backspace) on a FOCUSED CHIP removes that chip, and focus
//    moves to the chip that took its place — or to the entry box when the
//    removed chip was the last one. A control that dropped focus to `<body>`
//    after a removal would make removing three chips a three-time re-navigation.
//  * ARROW LEFT / RIGHT walk the chip row from the entry box; ARROW DOWN / UP
//    walk the suggestion popup. Two axes, two gestures — the chips are a
//    horizontal row and the popup is a vertical list, which is what a reader
//    sees and therefore what the keys should follow.
//  * A pointer commits a suggestion by `mousedown`, NOT `click`:
//    `ComboboxControl`'s reason exactly — the input's `blur` would otherwise
//    fire first, close the popup, and unmount the option before the click
//    landed on it.
//
//  ── Free text, and the one thing this control will not do ─────────────────
//  With `allowFreeText = false` an entry that matches no suggestion is REFUSED
//  rather than added, and the refusal is announced in a live region rather than
//  swallowed silently — a control that ignores a keystroke without saying why
//  reads as broken. That refusal is an AFFORDANCE and never a gate: per §22's
//  standing posture, client validation is not a trust boundary, and the real
//  check is the server-side re-check in `Fuaran.UI.ServerDriven.FormValidation`.
//
//  ── Cross-pipeline ────────────────────────────────────────────────────────
//  `React.useState` / `React.useRef` are Feliz `jsNative` declarations that
//  compile on both .NET and Fable and throw on .NET if invoked. The renderer's
//  .NET tests never mount React; the SSR floor for this control is the server
//  renderer's single comma-separated `<input>`, which needs no script at all.
// ============================================================================

#nowarn "3261"

open Fable.Core
open Feliz
open Fuaran.UI.Types

[<Import("createElement", "react")>]
let private reactCreateElement (componentFn: obj) (props: obj) : ReactElement = jsNative

/// Everything the control needs, already resolved: the renderer keeps its
/// `BindingSources` plumbing at the call site and this component holds only the
/// interaction. `suggestions` is `None` when the case declares no source at all
/// — a fact distinct from an empty resolved list, and the one that decides
/// whether combobox ARIA is emitted.
type TokensProps =
    {| fieldId: string
       className: string
       listClassName: string
       required: bool
       allowFreeText: bool
       placeholder: string
       suggestions: SelectOption list option
       tokens: string list
       commit: string list -> unit |}

/// The id a suggestion element takes. Derived from the field id and the
/// option's INDEX rather than its value, on `ComboboxControl`'s reason:
/// `aria-activedescendant` must name an element that exists, and an option
/// value can carry characters that are not valid in an id.
let private optionId (fieldId: string) (index: int) = fieldId + "-suggestion-" + string index

/// The id a chip's remove button takes — used to restore focus after a removal.
let private chipId (fieldId: string) (index: int) = fieldId + "-chip-" + string index

let private renderTokens (props: TokensProps) : ReactElement =
    let query, setQuery = React.useState ""
    let isOpen, setOpen = React.useState false
    let activeIndex, setActiveIndex = React.useState (-1)
    // -1 means "focus is not on a chip". A chip index means the chip row owns
    // the arrow keys and Delete.
    let focusedChip, setFocusedChip = React.useState (-1)
    // What the last refused gesture was, announced in the live region. Cleared
    // by the next successful commit, so a stale refusal is never read out.
    let refusal, setRefusal = React.useState ""

    let listId = props.fieldId + "-suggestions"
    let statusId = props.fieldId + "-status"
    let hasSuggestions = Option.isSome props.suggestions

    let visible =
        props.suggestions
        |> Option.map (fun opts -> TokensModel.visibleSuggestions opts props.tokens query)
        |> Option.defaultValue []

    let close () =
        setOpen false
        setActiveIndex -1

    /// Commit an entry. Every add in this control routes here — the keyboard,
    /// the suggestion pointer and the suggestion Enter — so the refusal
    /// announcement and the entry-box clear cannot be got right in one place
    /// and wrong in another.
    let commitEntry (entry: string) =
        match TokensModel.tryAdd props.allowFreeText props.suggestions props.tokens entry with
        | TokensModel.AddOutcome.Accepted next ->
            setQuery ""
            setRefusal ""
            close ()
            props.commit next
        | TokensModel.AddOutcome.Empty -> ()
        | TokensModel.AddOutcome.Duplicate token -> setRefusal (token + " is already in the list")
        | TokensModel.AddOutcome.NotSuggested token -> setRefusal (token + " is not one of the suggestions")

    let removeAt (index: int) =
        let next = TokensModel.removeAt index props.tokens
        setRefusal ""
        // Focus follows the removal: the chip that took this one's place, or
        // the entry box when the row is now shorter than the index.
        if index < List.length next then
            setFocusedChip index
        else
            setFocusedChip -1

        props.commit next

    let moveSuggestion (delta: int) =
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

    let onEntryKeyDown (e: Browser.Types.KeyboardEvent) =
        match e.key with
        | "Enter" ->
            if isOpen && activeIndex >= 0 && activeIndex < List.length visible then
                e.preventDefault ()
                commitEntry (List.item activeIndex visible).Value
            elif TokensModel.normalise query <> "" then
                // The entry is what commits. `preventDefault` because the
                // enclosing form would otherwise submit and take the token with
                // it — and an empty box deliberately falls through, so a form
                // whose only field is a token box is still submittable.
                e.preventDefault ()
                commitEntry query
        | "Backspace" when query = "" && not (List.isEmpty props.tokens) ->
            e.preventDefault ()
            setRefusal ""
            props.commit (TokensModel.removeLast props.tokens)
        | "ArrowDown" when hasSuggestions ->
            e.preventDefault ()
            moveSuggestion 1
        | "ArrowUp" when hasSuggestions ->
            e.preventDefault ()
            moveSuggestion -1
        | "ArrowLeft" when query = "" && not (List.isEmpty props.tokens) ->
            // Step out of the entry box onto the last chip. Only from an EMPTY
            // box: with text in it, Left is a caret move and belongs to the
            // input.
            e.preventDefault ()
            setFocusedChip (List.length props.tokens - 1)
        | "Escape" when isOpen ->
            e.preventDefault ()
            close ()
        | _ -> ()

    let onChipKeyDown (index: int) (e: Browser.Types.KeyboardEvent) =
        match e.key with
        | "Delete"
        | "Backspace" ->
            e.preventDefault ()
            removeAt index
        | "ArrowLeft" when index > 0 ->
            e.preventDefault ()
            setFocusedChip (index - 1)
        | "ArrowRight" ->
            e.preventDefault ()

            if index < List.length props.tokens - 1 then
                setFocusedChip (index + 1)
            else
                // Past the last chip is the entry box, which is where a reader
                // walking right expects to end up.
                setFocusedChip -1
        | _ -> ()

    let activeDescendant =
        if isOpen && activeIndex >= 0 && activeIndex < List.length visible then
            Some(optionId props.fieldId activeIndex)
        else
            None

    let chipRow =
        Html.ul
            [ prop.className "fuaran-tokens-list"
              prop.role "list"
              prop.children
                  [ for i, token in List.indexed props.tokens ->
                        Html.li
                            [ prop.key (string i + ":" + token)
                              prop.className "fuaran-tokens-chip"
                              prop.role "listitem"
                              prop.children
                                  [ Html.span
                                        [ prop.className "fuaran-tokens-chip-label"
                                          prop.text (TokensModel.chipLabel props.suggestions token) ]
                                    Html.button
                                        [ prop.id (chipId props.fieldId i)
                                          prop.className "fuaran-tokens-chip-remove"
                                          prop.type'.button
                                          // The accessible name says WHICH token
                                          // this button removes. A row of eight
                                          // buttons all named "Remove" is a row
                                          // of eight buttons a screen-reader
                                          // user cannot tell apart.
                                          prop.custom (
                                              "aria-label",
                                              "Remove " + TokensModel.chipLabel props.suggestions token
                                          )
                                          prop.tabIndex (if focusedChip = i then 0 else -1)
                                          prop.onKeyDown (onChipKeyDown i)
                                          prop.onFocus (fun _ -> setFocusedChip i)
                                          prop.onClick (fun _ -> removeAt i)
                                          prop.text "×" ] ] ] ] ]

    let entryElement =
        Html.input
            [ prop.className props.className
              prop.type'.text
              prop.id props.fieldId
              // `required` on the ENTRY box would demand text in a box that is
              // emptied on every commit — a form with three chips in it would
              // refuse to submit. The requirement is about the token LIST, and
              // the server-driven floor is what holds it.
              prop.placeholder props.placeholder
              prop.custom ("autocomplete", "off")
              prop.custom ("aria-describedby", statusId)
              if hasSuggestions then
                  prop.role "combobox"
                  prop.ariaExpanded isOpen
                  prop.custom ("aria-autocomplete", "list")
                  prop.custom ("aria-controls", listId)

                  match activeDescendant with
                  | Some id -> prop.custom ("aria-activedescendant", id)
                  | None -> ()
              prop.value query
              prop.tabIndex (if focusedChip < 0 then 0 else -1)
              prop.onChange (fun (v: string) ->
                  setQuery v
                  setRefusal ""

                  if hasSuggestions then
                      setOpen true
                      setActiveIndex -1)
              prop.onKeyDown onEntryKeyDown
              prop.onFocus (fun _ -> setFocusedChip -1)
              prop.onBlur (fun _ -> close ()) ]

    let suggestionList =
        if hasSuggestions then
            [ Html.ul
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
                                            "fuaran-tokens-option fuaran-tokens-option-active"
                                        else
                                            "fuaran-tokens-option"
                                    )
                                    prop.onMouseDown (fun e ->
                                        e.preventDefault ()
                                        commitEntry o.Value)
                                    prop.text o.Label ] ] ] ]
        else
            []

    // The live region carries the refusal and nothing else. `polite`, because a
    // refused keystroke is information the reader asked for by pressing a key,
    // not an interruption; and always PRESENT rather than conditionally
    // rendered, because a region added to the DOM at the moment it gains text
    // is a region many screen readers never announce.
    let status =
        Html.span
            [ prop.id statusId
              prop.className "fuaran-tokens-status"
              prop.role "status"
              prop.custom ("aria-live", "polite")
              prop.text refusal ]

    Html.span
        [ prop.className "fuaran-tokens"
          prop.children ([ chipRow; entryElement ] @ suggestionList @ [ status ]) ]

/// The public surface — `Render.fs` invokes this.
let tokens (props: TokensProps) : ReactElement =
    reactCreateElement (box renderTokens) (box props)
