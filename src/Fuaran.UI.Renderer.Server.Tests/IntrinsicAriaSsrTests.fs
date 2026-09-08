module Fuaran.UI.Renderer.Server.Tests.IntrinsicAriaSsrTests

// ============================================================================
//  The server floor's kind-intrinsic ARIA for `Form`'s two announced controls
//  (Phase 1605) — asserted in EMITTED BYTES, on the element that carries them.
//
//  Phase 1591 made kind-intrinsic ARIA a typed fact and declared three of
//  `Form`'s entries `ClientOnly`, naming the reason: the server floor rendered
//  a `Toggle` and a horizontal `SegmentedChoice` through its generic input arm,
//  so it emitted `type="toggle"` / `type="segmented-choice"` — types no user
//  agent knows — and no role at all. A screen reader on the static form heard a
//  text box where the `Filters` twin announced a switch and a radiogroup.
//
//  WHY THIS SUITE EXISTS RATHER THAN THE 1591 LOCK ALONE. That lock is a
//  source-reading census over the two `Render.fs` arms and is TOKEN-level by its
//  own admission: `switch`, `radiogroup` and `radio` already appeared in the
//  server arm — emitted by the FILTER arms — so its leg A would have gone green
//  for a `BothPipelines` re-declaration that changed no form arm at all. A
//  fidelity claim that passes because a DIFFERENT kind emits the token is the
//  defect, not the fix. These cases render the real document through the real
//  entry point and read the bytes.
//
//  Each positive assertion is paired with a NEGATIVE one over the same helper,
//  so a green run is evidence rather than silence: a `Checkbox` field — the same
//  boolean data, deliberately not a switch — must carry no role, and the
//  vertical segmented floor must emit no hand-written `radiogroup`, which is
//  exactly what the declaration's condition says.
// ============================================================================

open System
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server

/// An element's own open tag — everything from the first `<tag` at or after
/// `from` up to its first `>`. The whole point of splitting is that a substring
/// check over the whole markup cannot tell a role on the group container from
/// one on an option inside it, and assistive technology does not treat those as
/// the same statement.
let private openTagAt (tag: string) (from: int) (html: string) : string =
    let i = html.IndexOf("<" + tag, from, StringComparison.Ordinal)

    if i < 0 then
        failwithf "no <%s element in the emitted HTML" tag

    let rest = html.Substring i
    rest.Substring(0, rest.IndexOf('>') + 1)

let private openTagOf (tag: string) (html: string) : string = openTagAt tag 0 html

let private contains (needle: string) (haystack: string) =
    haystack.Contains(needle, StringComparison.Ordinal)

/// Every open tag of `tag` in document order.
let private allOpenTags (tag: string) (html: string) : string list =
    let rec walk (from: int) (acc: string list) =
        let i = html.IndexOf("<" + tag, from, StringComparison.Ordinal)

        if i < 0 then
            List.rev acc
        else
            let rest = html.Substring i
            let openTag = rest.Substring(0, rest.IndexOf('>') + 1)
            walk (i + 1) (openTag :: acc)

    walk 0 []

/// The one `<tag` open tag carrying `locator`.
///
/// The locator is deliberately NOT the attribute under test — a lookup keyed by
/// the thing being asserted proves only that the string exists somewhere. It is
/// the field's own buffer marker, so each case reads "the element this form
/// field addresses ALSO carries the role", which is the statement assistive
/// technology cares about: a role on an ancestor of the control is a different
/// claim from one on the control.
let private openTagCarrying (tag: string) (locator: string) (html: string) : string =
    match
        allOpenTags tag html
        |> List.filter (fun t -> t.Contains(locator, StringComparison.Ordinal))
    with
    | [ one ] -> one
    | [] -> failwithf "no <%s open tag carries %s" tag locator
    | many ->
        failwithf "%d <%s open tags carry %s; the locator does not name one element" (List.length many) tag locator

let private options: Binding<SelectOption list> =
    Binding.Static(Some [ { Label = "Daily"; Value = "daily" }; { Label = "Weekly"; Value = "weekly" } ])

let private field (id: string) (kind: FormFieldKind<obj>) : FormField<obj> =
    { Defaults.formField<obj> with
        Id = id
        Label = TextSource.Literal id
        Kind = kind }

let private formWith (fields: FormField<obj> list) : Node<obj> =
    Fuaran.form
        "f"
        { Defaults.form<obj> with
            Fields = fields }

let private renderForm (fields: FormField<obj> list) : string =
    Render.render BindingResolver.empty (formWith fields)

let private toggleField (value: bool) : FormField<obj> =
    field "notify" (FormFieldKind.Toggle(Some(Binding.Static(Some value)), None))

let private segmentedField (orientation: Orientation) : FormField<obj> =
    field "cadence" (FormFieldKind.SegmentedChoice(options, Some(Binding.Static(Some "weekly")), None, orientation))

// ─── Toggle ──────────────────────────────────────────────────────────────────

[<Tests>]
let toggleFloor =
    testList
        "Phase 1605 — the server floor's Toggle announces on/off"
        [ testCase "an unchecked toggle carries role=switch + aria-checked=false on the input itself" (fun () ->
              let html = renderForm [ toggleField false ]
              let tag = openTagOf "input" html

              Expect.isTrue
                  (contains "role=\"switch\"" tag)
                  (sprintf "the toggle's own input carries no switch role: %s" tag)

              Expect.isTrue
                  (contains "aria-checked=\"false\"" tag)
                  (sprintf "the toggle's own input carries no resolved aria-checked: %s" tag)

              // The type is what makes the role honest: a role on an element the
              // platform gives no keyboard operation to would have to reimplement
              // it.
              Expect.isTrue
                  (contains "type=\"checkbox\"" tag)
                  (sprintf "the toggle floors on something other than a native checkbox: %s" tag)

              Expect.isFalse
                  (contains "type=\"toggle\"" html)
                  "the pre-1605 generic-arm emission (a `type` no user agent knows) is still reaching the wire")

          testCase "a checked toggle announces aria-checked=true AND is checked" (fun () ->
              let html = renderForm [ toggleField true ]
              let tag = openTagOf "input" html

              Expect.isTrue (contains "aria-checked=\"true\"" tag) (sprintf "a checked toggle announces false: %s" tag)

              // The announced state and the native checked state must agree. A
              // static render is never corrected, so a disagreement is permanent.
              Expect.isTrue (contains "checked" tag) (sprintf "a checked toggle renders unchecked: %s" tag))

          testCase "the buffer marker survives, on an input the shim can read" (fun () ->
              // Policy (b): the live DOM value IS the client-side buffer. The
              // marker moved arm, not element — and it now sits on a checkbox,
              // whose checked state the shim reads, where it previously sat on a
              // text box that could hold no boolean.
              let html = renderForm [ toggleField true ]
              let tag = openTagOf "input" html

              Expect.isTrue (contains "data-fuaran-field=\"notify\"" tag) (sprintf "the buffer marker is gone: %s" tag))

          testCase "a Checkbox field is NOT a switch (the negative probe)" (fun () ->
              // Same boolean data, deliberately a different a11y contract —
              // which is the whole reason `Toggle` is its own kind. This is also
              // what proves the assertions above can fail: the identical helper
              // over the identical shape reports absence here.
              let html =
                  renderForm [ field "agree" (FormFieldKind.Checkbox(Some(Binding.Static(Some true)), None)) ]

              let tag = openTagOf "input" html

              Expect.isFalse
                  (contains "role=" tag)
                  (sprintf "a Checkbox field emits an intrinsic role no fidelity row declares: %s" tag)) ]

// ─── SegmentedChoice ─────────────────────────────────────────────────────────

[<Tests>]
let segmentedFloor =
    testList
        "Phase 1605 — the server floor's horizontal SegmentedChoice is a radiogroup"
        [ testCase "the group container carries role=radiogroup and the field marker" (fun () ->
              let html = renderForm [ segmentedField Orientation.Horizontal ]
              let tag = openTagCarrying "div" "data-fuaran-field=\"cadence\"" html

              Expect.isTrue
                  (contains "role=\"radiogroup\"" tag)
                  (sprintf "the segmented field's container carries no group role: %s" tag)

              Expect.isTrue
                  (contains "aria-orientation=\"horizontal\"" tag)
                  (sprintf "the horizontal group does not say so: %s" tag)

              Expect.isTrue
                  (contains "data-fuaran-field=\"cadence\"" tag)
                  (sprintf "the container carries no per-field buffer marker: %s" tag)

              Expect.isFalse
                  (contains "type=\"segmented-choice\"" html)
                  "the pre-1605 generic-arm emission (a `type` no user agent knows) is still reaching the wire")

          testCase "each option is a role=radio button and exactly the bound one is checked" (fun () ->
              let html = renderForm [ segmentedField Orientation.Horizontal ]

              // The form's own submit button is a `<button>` too, and it is not
              // an option — filtering by the option class is what keeps the
              // count below a statement about the choice rather than the chrome.
              let buttons =
                  allOpenTags "button" html |> List.filter (contains "fuaran-segmented-option")

              Expect.equal (List.length buttons) 2 "one option button per declared option"

              Expect.isFalse
                  (allOpenTags "button" html
                   |> List.filter (fun b -> not (contains "fuaran-segmented-option" b))
                   |> List.exists (contains "role=\"radio\""))
                  "a non-option button in the form carries the radio role"

              for b in buttons do
                  Expect.isTrue (contains "role=\"radio\"" b) (sprintf "an option button carries no radio role: %s" b)

              Expect.equal
                  (buttons |> List.filter (contains "aria-checked=\"true\"") |> List.length)
                  1
                  "exactly one option is announced as chosen — the bound value's"

              // The FILTER-side shim marker must NOT appear on a form field: it
              // is what bridges a click to `payload.value`, and this floor is
              // inert (`RichTier.Behavioural`).
              Expect.isFalse
                  (contains "data-filter-value" html)
                  "a form field emitted the filter shim's per-option marker — that would claim an interaction the floor does not wire")

          testCase "the vertical form floors on native radios and emits NO hand-written role" (fun () ->
              // The declaration's condition says the roles are emitted for the
              // HORIZONTAL orientation only, because the vertical shape takes its
              // group semantics from the user agent. This is the second negative
              // probe: it fails if a later change over-emits.
              let html = renderForm [ segmentedField Orientation.Vertical ]

              Expect.isFalse
                  (contains "role=\"radiogroup\"" html)
                  "the vertical floor emitted a hand-written radiogroup role, which the fidelity condition says it does not"

              Expect.isTrue (contains "<fieldset" html) "the vertical floor does not use a fieldset"

              Expect.isTrue (contains "type=\"radio\"" html) "the vertical floor does not use native radio inputs"

              let fieldset = openTagCarrying "fieldset" "data-fuaran-field=\"cadence\"" html

              Expect.isTrue
                  (contains "aria-orientation=\"vertical\"" fieldset)
                  (sprintf "the vertical group does not say so: %s" fieldset)) ]

// ─── The declaration and the floor, held to each other ───────────────────────

[<Tests>]
let declarationAgrees =
    testList
        "Phase 1605 — the fidelity declaration matches the floor these tests read"
        [ testCase "every Form intrinsic entry is declared on BOTH pipelines" (fun () ->
              // The bytes above ARE the server pipeline, so this is the pairing
              // that makes the tier claim falsifiable here rather than only in
              // the token-level census: re-declaring an entry `ClientOnly`
              // without removing the emission fails here, and removing the
              // emission without re-declaring fails the cases above.
              let entries =
                  RenderFidelity.tryFind "Form"
                  |> Option.map (fun r -> r.Intrinsic)
                  |> Option.defaultWith (fun () -> failwith "Form carries no fidelity row")

              Expect.isNonEmpty entries "the Form row declares no intrinsic emission at all"

              for e in entries do
                  match e.Tier with
                  | RenderFidelity.IntrinsicTier.BothPipelines -> ()
                  | RenderFidelity.IntrinsicTier.ClientOnly why ->
                      failtestf
                          "Form/%s is still declared client-only (%s) — Phase 1605 gave the server floor its own arms, so either the declaration is stale or the floor regressed"
                          e.Element
                          why

              Expect.equal
                  (entries |> List.choose (fun e -> e.Role) |> List.sort)
                  [ "radio"; "radiogroup"; "switch" ]
                  "the Form row's declared roles are the three these tests read out of the emitted bytes") ]
