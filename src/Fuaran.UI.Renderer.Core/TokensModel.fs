module Fuaran.UI.Renderer.TokensModel

// ============================================================================
//  Fuaran — the token control's judgements, as pure functions (Phase 1121)
//
//  `FormFieldKind.Tokens` puts three things on the wire and nothing else:
//  whether tokens off the suggestion list are admitted (`allowFreeText`), where
//  the suggestions come from (`suggestions`, optional), and where the token
//  list goes (`value`). Every keystroke — Enter to commit, Backspace to take
//  the last chip back, Delete on a focused chip — the chip row itself and the
//  suggestion popup are the renderers', under the affordance→op charter. Every
//  DECISION they make is here, in one place both renderers read.
//
//  ── Why here, and why pure ────────────────────────────────────────────────
//  `RatingModel`'s two reasons, unchanged and, if anything, sharper:
//
//  * The .NET test runner mounts no DOM (Feliz's .NET `ReactElement` is opaque,
//    so a rendered control's handler cannot be extracted and fired). A keyboard
//    model buried inside the React component would be unreachable by any test
//    in this repo; held out here, it is pinned directly. This control has more
//    keyboard model than any other in the vocabulary — Enter, Backspace, Delete
//    and the arrow walk over the suggestions all mean different things
//    depending on what is in the entry box and which chip has focus — so the
//    difference between testable and untestable is larger here than anywhere.
//  * The hydrated control and the SSR floor must agree about what the same
//    token list IS, and they are different projects with no common renderer.
//    `Fuaran.UI.Renderer.Core` is what both reference.
//
//  ── THE ARIA DECISION, recorded (the phase's a11y audit) ──────────────────
//  The chip row is `role="list"` of `role="listitem"`, and each chip's remove
//  control is a real `<button>` inside its item. NOT a `role="listbox"` of
//  `role="option"`, which is the reading a writer of this markup reaches for
//  first because the chips look like a set of selected options:
//
//    1. A listbox is a control for CHOOSING from a set of candidates. These
//       are not candidates; they are the value, already chosen. The candidates
//       live in the suggestion popup, which IS a listbox, and having two
//       listboxes in one control — one of things you might pick and one of
//       things you did — is precisely the confusion to avoid.
//    2. `aria-selected` has no honest value on a chip. Every chip is
//       "selected"; a listbox in which every option is selected and none can be
//       deselected has no state a reader can act on, and screen readers
//       announce the position and count as though a choice were pending.
//    3. The gesture a chip actually offers is REMOVAL, and removal is a button.
//       A real `<button>` gets the platform's own name, role, focus ring and
//       Enter/Space activation for free — none of which a `role="option"`
//       carries — and it is what makes the row navigable by Tab as well as by
//       the arrow walk.
//
//  The entry `<input>` keeps `role="combobox"` when a suggestion source is
//  present, with `aria-controls` naming the popup and `aria-activedescendant`
//  naming the active suggestion: that half IS the combobox pattern and is
//  deliberately identical to `ComboboxControl`'s, because it is the same
//  affordance. With no suggestion source it is a plain text input and no
//  combobox ARIA is emitted — a `role="combobox"` with nothing to expand is the
//  same overclaim the SSR floor is forbidden to make.
//
//  A token count belongs in the field's accessible description, not in a chip's
//  label: "3 tokens" said once is a fact, said on every chip it is noise.
//
//  ── And the SSR floor is ONE TEXT INPUT, which is not a contradiction ─────
//  Zero-JS there is no gesture that adds a chip: a chip row is built by a
//  keystroke handler, and inert markup has none. So the static floor is a
//  single `<input type="text">` carrying the tokens comma-separated, which a
//  reader can edit and which submits with the form. `docs/SSR.md` states it
//  normatively, along with the two limits it cannot honour (a token containing
//  a comma, and `allowFreeText = false`).
// ============================================================================

open Fuaran.UI.Types

// ─── the pure model ─────────────────────────────────────────────────────────

/// The token a piece of entry text would become — trimmed, and nothing else.
///
/// Deliberately NOT case-folded, NOT lower-cased and NOT otherwise normalised.
/// A token is a value the author's document may carry as a literal and the
/// server may match exactly; a control that quietly rewrote `React` to `react`
/// would be answering a question the document did not ask, and the two would
/// then disagree about whether a token is a duplicate. Trimming is the one
/// rewrite that is safe, because leading and trailing space is invisible to the
/// reader who typed it — a token that differs from another only by space is not
/// a second token, it is the same one entered clumsily.
let normalise (text: string) : string =
    if isNull (box text) then "" else text.Trim()

/// Why an entry could not become a token. `Accepted` carries the NEW LIST
/// rather than the token, because that is what the caller writes back: the
/// whole slot is rewritten on every add, which is what keeps the reader's own
/// order on a decoded tree with no host code at all.
[<RequireQualifiedAccess>]
type AddOutcome =
    /// The token was added; this is the new list, in reader order.
    | Accepted of tokens: string list
    /// The entry was empty (or only whitespace). Not an error — it is what
    /// Enter on an empty box means, which is "nothing", not "add nothing".
    | Empty
    /// The list already carries this token. Refused rather than appended: two
    /// identical chips are one fact drawn twice, with two remove buttons that
    /// do different things.
    | Duplicate of token: string
    /// `allowFreeText` is false and the token is not in the suggestion set.
    | NotSuggested of token: string

/// Whether an entry may become a token, and what the list becomes if it does.
///
/// `suggestions` is the RESOLVED option list — `None` when the case declares no
/// suggestion source at all, which is a different fact from an empty list: with
/// no source the control is open by construction (the decoder refuses a closed
/// field with no source), while an empty resolved list on a closed field is the
/// unusable shape FUARAN135 names.
///
/// Membership is tested against both the option VALUE and its LABEL, because a
/// reader types what they can see. The token committed is always the option's
/// `Value` — the label is what the chip shows, the value is what submits.
let tryAdd
    (allowFreeText: bool)
    (suggestions: SelectOption list option)
    (current: string list)
    (entry: string)
    : AddOutcome =
    let token = normalise entry

    if token = "" then
        AddOutcome.Empty
    else
        let matched =
            suggestions
            |> Option.bind (List.tryFind (fun (o: SelectOption) -> o.Value = token || o.Label = token))

        let resolved =
            match matched with
            | Some o -> o.Value
            | None -> token

        if current |> List.contains resolved then
            AddOutcome.Duplicate resolved
        elif not allowFreeText && Option.isNone matched then
            AddOutcome.NotSuggested token
        else
            AddOutcome.Accepted(current @ [ resolved ])

/// Remove the token at `index`, if there is one there. Out-of-range is a no-op
/// rather than a throw: the index comes from a DOM event on a row the model
/// does not own, and a stale one is a race, not a defect.
let removeAt (index: int) (tokens: string list) : string list =
    if index < 0 || index >= List.length tokens then
        tokens
    else
        tokens
        |> List.mapi (fun i t -> i, t)
        |> List.filter (fun (i, _) -> i <> index)
        |> List.map snd

/// Take the last token back — what Backspace on an EMPTY entry box means. An
/// empty list is a no-op, so the key falls through to the browser rather than
/// being swallowed by a control with nothing to undo.
let removeLast (tokens: string list) : string list =
    match List.rev tokens with
    | [] -> []
    | _ :: rest -> List.rev rest

/// The suggestions an entry admits, for the popup. An empty query shows
/// everything — opening the popup with nothing typed is how a reader browses
/// the set — and tokens ALREADY CHOSEN are filtered out, which is the one place
/// this differs from `ComboboxControl`'s filter and the reason it is here
/// rather than inlined: a single-value combobox may legitimately re-offer the
/// committed value, and a multi-token control offering a chip the reader
/// already has is offering a gesture that would be refused.
let visibleSuggestions (options: SelectOption list) (chosen: string list) (query: string) : SelectOption list =
    let needle = (normalise query).ToLowerInvariant()

    options
    |> List.filter (fun o ->
        not (chosen |> List.contains o.Value)
        && (needle = ""
            || o.Label.ToLowerInvariant().Contains needle
            || o.Value.ToLowerInvariant().Contains needle))

/// The label a chip shows for a token: the matching option's label where the
/// suggestion set knows the token, and the token itself otherwise (which is
/// every free-text token, and every token whose asynchronous suggestion source
/// has not resolved yet). Never blank — a chip with no text is a chip a reader
/// cannot identify or decide to remove.
let chipLabel (suggestions: SelectOption list option) (token: string) : string =
    suggestions
    |> Option.bind (List.tryFind (fun (o: SelectOption) -> o.Value = token))
    |> Option.map (fun o -> o.Label)
    |> Option.defaultValue token

/// The SSR floor's projection of a token list: comma-and-space separated, in
/// reader order. Stated here rather than in the server renderer so the
/// round-trip below reads it too — one separator, defined once.
///
/// **RECORDED LIMIT.** A token containing a comma cannot survive this
/// projection: it re-parses as two. The floor is a degraded medium and this is
/// what it degrades to; the client tier never uses it, the hydrated control
/// carries the real list, and `docs/SSR.md` states the limit rather than
/// letting a host discover it.
let toCommaSeparated (tokens: string list) : string = String.concat ", " tokens

/// The inverse, for a floor submission: split on commas, trim, drop the empties
/// and the repeats. Order is preserved — the reader's own — and de-duplication
/// keeps the FIRST occurrence, so an edited box behaves the way the chip row
/// would have.
let fromCommaSeparated (text: string) : string list =
    if isNull (box text) then
        []
    else
        let seen = System.Collections.Generic.HashSet<string>()

        text.Split(',')
        |> Array.toList
        |> List.map normalise
        |> List.filter (fun t -> t <> "" && seen.Add t)
