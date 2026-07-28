module ServerDriven.Sample.Form

open Fuaran.UI
open Fuaran.UI.Types

// ─── The form sample (Phase 152 Track E — the form-policy worked example) ──────
//
// A neutral, language-tier-only worked example of the server-driven tier's form
// policy (b): each field is **client-buffered** — its live DOM value is the
// buffer; the generic shim does NOT round-trip keystrokes — and the server sees
// the values only on a **flush**:
//
//   • Submit — the shim harvests every buffered field into the submit payload;
//     the server flushes each `Binding.Local` field's `OnCommit` (here `SetName`
//     / `SetAge`) folded with the form's `OnSubmit` (`Greet`), all in ONE diff,
//     so only the changed "saved" card patches — the form keeps its typed
//     values, focus, and scroll (no full re-render, no flash).
//   • Apply name — an explicit `Action.CommitLocal "name"` flush (the
//     `OnCommitAction` analogue): the shim harvests just the name field; the
//     server commits it without a full submit.
//
// The model lives on the SERVER; `update` runs on the server; the browser ships
// the generic shim and nothing else. The tree is `Node<obj>` (Msg boxed at the
// dispatch site) so it renders directly through `Renderer.Server.Render.render`.

type Model =
    { Name: string
      Age: float
      Greeted: bool }

let initial =
    { Name = ""
      Age = 30.0
      Greeted = false }

type Msg =
    | SetName of string
    | SetAge of float
    | Greet

let update (msgObj: obj) (model: Model) : Model =
    match unbox<Msg> msgObj with
    | SetName n -> { model with Name = n }
    | SetAge a -> { model with Age = a }
    | Greet -> { model with Greeted = true }

let private dispatch (msg: Msg) : Action<obj> =
    Action.Dispatch(Unchecked.nonNull (box msg))

// A name field whose value is a client-buffered `Binding.Local`. `InitialFrom`
// is `Static ""` (the buffer is authoritative — the field never re-renders from
// the model, so a commit patches the saved card, not the field). `OnCommit` is
// the flush handler the server runs on submit / Apply; `Parse` reverses the
// display string. The field's `onChange` is unused under policy (b).
let private nameField: FormField<obj> =
    { Defaults.formField<obj> with
        Id = "name"
        Label = TextSource.Literal "Display name"
        Required = true
        Kind =
            FormFieldKind.Text(
                binding.local
                    (Binding.Static(Some ""))
                    LocalFlushTrigger.OnCommitAction
                    (fun n -> dispatch (SetName n))
                    None
                    (fun s -> Ok s),
                Some(fun _ -> Action.Chain [])
            ) }

// An age field (0..120), client-buffered + range-checked server-side (Phase 156
// declared enforcement runs on submit; the buffered value flushes through SetAge).
let private ageField: FormField<obj> =
    { Defaults.formField<obj> with
        Id = "age"
        Label = TextSource.Literal "Age"
        Kind =
            FormFieldKind.RangedNumber(
                binding.local
                    (Binding.Static(Some 30.0))
                    LocalFlushTrigger.OnSubmit
                    (fun a -> dispatch (SetAge a))
                    None
                    (fun s ->
                        match System.Double.TryParse s with
                        | true, v -> Ok v
                        | _ -> Error "Enter a number"),
                Some(fun _ -> Action.Chain []),
                { Defaults.numberFieldConstraints with
                    Min = Some 0.0
                    Max = Some 120.0 }
            ) }

let private profileForm: Node<obj> =
    Fuaran.form
        "profile"
        { Defaults.form<obj> with
            Fields = [ nameField; ageField ]
            SubmitLabel = TextSource.Literal "Save profile"
            OnSubmit = dispatch Greet }

/// The "saved" card — the only region that patches when a flush lands. It reads
/// the SERVER model, so an `Apply name` (commits name only) or a `Save profile`
/// (commits name + age + greets) shows up here as a targeted `ReplaceFragment`.
let private savedCard (model: Model) : Node<obj> =
    let body =
        if model.Greeted then
            sprintf "Hello **%s** — age **%g**. _(committed on submit)_" model.Name model.Age
        elif model.Name <> "" then
            sprintf "Saved name: **%s**. _(committed via Apply; submit to greet)_" model.Name
        else
            "Fill in the form and submit — the saved values appear here."

    Fuaran.card
        "saved"
        { Defaults.card<obj> with
            Heading = Some(TextSource.Literal "Saved profile")
            Children = [ Fuaran.markdown "saved-body" body ] }

let view (model: Model) : Node<obj> =
    Fuaran.dashboard
        "app"
        { Defaults.dashboard<obj> with
            Children =
                [ Fuaran.markdown
                      "title"
                      "# Fuaran server-driven form\nFields buffer client-side; only the flush reaches the server."
                  profileForm
                  // The explicit per-field flush: commits just the name buffer.
                  Fuaran.button
                      "apply-name"
                      { Defaults.button<obj> with
                          Label = TextSource.Literal "Apply name"
                          OnClick = Action.CommitLocal "name" }
                  savedCard model ] }
