module Fuaran.Samples.Catalog.LocalBindings

// ============================================================================
//  Catalog page exercising the four canonical Binding<'T>.Local
//  shapes:
//
//   1. Salary-style numeric-text with thousands formatting + OnBlur flush.
//   2. Live-validating email with OnDebounce 250 (parse-error path
//      surfaces under the Help slot).
//   3. Inline "Apply" OnCommitAction case with an explicit commit button.
//   4. Re-sync test: a preset-apply button mutates the InitialFrom-side
//      model state. The re-sync invariant says the buffer only re-syncs
//      when not mid-edit AND the buffer's Parse does not already equal
//      the new external value.
//
//  The page mounts at `?local-bindings=1`. Playwright spec at
//  `snapshot/local-bindings.spec.mts` drives the keyboard events and
//  observes the model-side dispatches via the visible mirror panel.
// ============================================================================

open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

// ─── Model ─────────────────────────────────────────────────────────────────

type Model =
    { Salary: decimal
      Email: string
      Note: string
      EmailParseError: string option }

type Msg =
    | SetSalary of decimal
    | SetEmail of string
    | SetEmailError of string option
    | SetNote of string
    | PresetSalary of decimal
    | ResetSalary

let init () : Model =
    { Salary = 50000m
      Email = ""
      Note = ""
      EmailParseError = None }

let update (msg: Msg) (model: Model) : Model =
    match msg with
    | SetSalary v -> { model with Salary = v }
    | SetEmail v ->
        { model with
            Email = v
            EmailParseError = None }
    | SetEmailError opt -> { model with EmailParseError = opt }
    | SetNote v -> { model with Note = v }
    | PresetSalary v -> { model with Salary = v }
    | ResetSalary -> { model with Salary = 50000m }

// ─── Format / parse helpers (consumer-side, per Local-binding contract) ─────

let formatThousands (v: decimal) : string =
    v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)

let parseDecimalLenient (raw: string) : Result<decimal, string> =
    let cleaned = raw.Replace(",", "").Trim()

    if cleaned = "" then
        Ok 0m
    else
        // Fable rejects the `Decimal.TryParse(string, NumberStyles, IFormatProvider)`
        // overload ("NumberStyle / provider argument is ignored") — use the single-arg
        // form. The thousands separators are already stripped above and the buffer is
        // `en-US`-shaped (dot-decimal), so the invariant-culture distinction is moot.
        // Mirrors the same fix in `JsonDecode.fs:159` and `Fuaran.UI.Renderer/Theme.fs`.
        match System.Decimal.TryParse cleaned with
        | true, v -> Ok v
        | false, _ -> Error(sprintf "could not parse '%s' as a decimal" raw)

let parseEmail (raw: string) : Result<string, string> =
    let trimmed = raw.Trim()

    if trimmed.Contains("@") && trimmed.Contains(".") && trimmed.Length >= 5 then
        Ok trimmed
    else
        Error "email needs an @ and a . and at least 5 characters"

// ─── View ──────────────────────────────────────────────────────────────────
//
// Each Local-bound input is wrapped in a single-field Fuaran.form so the
// renderer's FormFieldKind dispatch path fires. The Apply button (case 3)
// uses Action.CommitLocal — when clicked, the renderer dispatches the
// per-NodeId custom event the input's useEffect drains.

let private salaryForm (model: Model) : Node<Msg> =
    Fuaran.form
        "salary-form"
        { Defaults.form<Msg> with
            SubmitLabel = TextSource.Literal "Save"
            OnSubmit = Action.Chain []
            Fields =
                [ { Defaults.formField<Msg> with
                      Id = "salary-input"
                      Label = TextSource.Literal "Salary"
                      Kind =
                          FormFieldKind.Number(
                              Some(
                                  binding.local
                                      (binding.computed (fun _ -> float model.Salary))
                                      LocalFlushTrigger.OnBlur
                                      (fun v -> Action.dispatch (SetSalary(decimal v)))
                                      (Some(fun v -> formatThousands (decimal v)))
                                      (fun s ->
                                          match parseDecimalLenient s with
                                          | Ok d -> Ok(float d)
                                          | Error e -> Error e)
                              ),
                              Some(fun _ -> Action.Chain [])
                          ) } ] }

let private emailForm (model: Model) : Node<Msg> =
    Fuaran.form
        "email-form"
        { Defaults.form<Msg> with
            SubmitLabel = TextSource.Literal "Save"
            OnSubmit = Action.Chain []
            Fields =
                [ { Defaults.formField<Msg> with
                      Id = "email-input"
                      Label = TextSource.Literal "Email"
                      Help =
                          model.EmailParseError
                          |> Option.map TextSource.Literal
                          |> Option.defaultValue (TextSource.Literal "Will validate after 250ms idle")
                          |> Some
                      Kind =
                          FormFieldKind.Text(
                              Some(
                                  binding.local
                                      (binding.computed (fun _ -> model.Email))
                                      (LocalFlushTrigger.OnDebounce 250)
                                      (fun v -> Action.dispatch (SetEmail v))
                                      (Some id)
                                      parseEmail
                              ),
                              Some(fun _ -> Action.Chain [])
                          ) } ] }

let private noteForm (model: Model) : Node<Msg> =
    Fuaran.form
        "note-form"
        { Defaults.form<Msg> with
            SubmitLabel = TextSource.Literal "Save"
            OnSubmit = Action.Chain []
            Fields =
                [ { Defaults.formField<Msg> with
                      Id = "note-input"
                      Label = TextSource.Literal "Note (Apply to commit)"
                      Kind =
                          FormFieldKind.Text(
                              Some(
                                  binding.local
                                      (binding.computed (fun _ -> model.Note))
                                      LocalFlushTrigger.OnCommitAction
                                      (fun v -> Action.dispatch (SetNote v))
                                      (Some id)
                                      (fun s -> Ok s)
                              ),
                              Some(fun _ -> Action.Chain [])
                          ) } ] }

let view (model: Model) (dispatch: Msg -> unit) : ReactElement =
    let ctx: Render.RenderContext<Msg> =
        { Sources = BindingResolver.empty
          Runtime = Runtime.diagnostic
          VisAdapter = VisAdapter.noOp<Msg>
          Dispatch = dispatch
          TelemetrySink = None
          InErrorBoundary = false
          Fragments = Map.empty
          ExpandingFragments = Set.empty
          Scope = None
          SessionContext = Map.empty }

    React.Fragment
        [ Render.themeStyleElement Defaults.theme
          Html.div
              [ prop.id "local-bindings-page"
                prop.className "catalog-local-bindings"
                prop.style [ style.padding 24; style.maxWidth 720 ]
                prop.children
                    [ Html.h1 [ prop.text "Local bindings" ]
                      Html.p
                          [ prop.text
                                "Four canonical shapes for Binding<'T>.Local. The model panel below mirrors the committed values." ]
                      Html.section
                          [ prop.style [ style.marginTop 16 ]
                            prop.children
                                [ Html.h2 [ prop.text "1. Salary — OnBlur + thousands formatter" ]
                                  Render.render ctx (salaryForm model)
                                  Html.div
                                      [ prop.style [ style.marginTop 8 ]
                                        prop.children
                                            [ Html.button
                                                  [ prop.testId "preset-100k"
                                                    prop.onClick (fun _ -> dispatch (PresetSalary 100000m))
                                                    prop.text "Preset £100,000" ]
                                              Html.button
                                                  [ prop.testId "reset-salary"
                                                    prop.onClick (fun _ -> dispatch ResetSalary)
                                                    prop.style [ style.marginLeft 8 ]
                                                    prop.text "Reset to £50,000" ] ] ] ] ]
                      Html.section
                          [ prop.style [ style.marginTop 24 ]
                            prop.children
                                [ Html.h2 [ prop.text "2. Email — OnDebounce 250" ]
                                  Render.render ctx (emailForm model) ] ]
                      Html.section
                          [ prop.style [ style.marginTop 24 ]
                            prop.children
                                [ Html.h2 [ prop.text "3. Note — OnCommitAction + explicit Apply" ]
                                  Render.render ctx (noteForm model)
                                  Html.button
                                      [ prop.testId "apply-note"
                                        prop.style [ style.marginTop 8 ]
                                        prop.onClick (fun _ ->
                                            // Dispatching Action.CommitLocal "note-input"
                                            // fires the per-NodeId custom event the input's
                                            // useEffect drains. The renderer's runAction handles
                                            // CommitLocal natively (no IFuaranRuntime substrate).
                                            let evt = Browser.Dom.window.document.createEvent "CustomEvent"
                                            evt.initEvent ("fuaran-commit-local-note-input", false, true)
                                            Browser.Dom.window.dispatchEvent evt |> ignore)
                                        prop.text "Apply" ] ] ]
                      Html.section
                          [ prop.style [ style.marginTop 24; style.padding 12 ]
                            prop.children
                                [ Html.h2 [ prop.text "Model panel (committed values)" ]
                                  Html.dl
                                      [ prop.style [ style.fontFamily "monospace" ]
                                        prop.children
                                            [ Html.dt [ prop.text "Salary (decimal)" ]
                                              Html.dd
                                                  [ prop.testId "model-salary"
                                                    prop.text (formatThousands model.Salary) ]
                                              Html.dt [ prop.text "Email (string)" ]
                                              Html.dd [ prop.testId "model-email"; prop.text model.Email ]
                                              Html.dt [ prop.text "Note (string)" ]
                                              Html.dd [ prop.testId "model-note"; prop.text model.Note ] ] ] ] ] ] ] ]
