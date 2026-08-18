module Fuaran.Samples.Catalog.Catalog

// ============================================================================
//  Catalog view.
//
//  Renders the side-nav + main pane + per-card matrix grid. Each card
//  carries the live render, the §4d JSON wire shape (collapsed by
//  default), and the F# author snippet (collapsed by default).
//
//  Catalog Msg is intentionally narrow — every interactive component
//  in the matrix wires its handlers to `Action.Chain []` so the gallery
//  never dispatches anything that mutates Model. The model only tracks
//  the active NodeKind + active theme + active locale + whether the
//  a11y audit view is on.
//
//  Catalog-axis tail (2026-05-26): the locale switcher
//  drives a `BindingResolver.makeI18nResolver` against the catalog's
//  test locale catalogs (en/fr/de). Cards whose specs use
//  `Binding.I18n("catalog.<key>", None)` re-render in the selected
//  locale; missing keys fall through to the `[i18n:<key>]` debug shape
//  the §4d convention specifies. The a11y audit toggle highlights cards
//  whose root Node has `Accessibility = None` (red outline + tooltip
//  listing the missing fields) and shows the resolved aria-* attributes
//  the renderer is emitting for cards that DO have metadata.
// ============================================================================

open Elmish
open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

// ─── Theme catalogue (selectable in the top-bar drop-down) ────────────────

type ThemeOption =
    { Id: string
      Label: string
      Theme: Theme }

let themeOptions: ThemeOption list =
    [ { Id = "default"
        Label = "Default"
        Theme = Fuaran.Samples.Themes.Default.theme }
      { Id = "dark"
        Label = "Dark"
        Theme = Fuaran.Samples.Themes.Dark.theme }
      { Id = "high-contrast"
        Label = "High Contrast"
        Theme = Fuaran.Samples.Themes.HighContrast.theme }
      { Id = "teal-brand"
        Label = "Teal (branded)"
        Theme = Fuaran.Samples.Catalog.TealBrandTheme.theme }
      { Id = "amber-brand"
        Label = "Amber (branded)"
        Theme = Fuaran.Samples.Catalog.AmberBrandTheme.theme } ]

// ─── Locale catalogue (catalog-axis tail) ──────────────────────
//
// Three test locales. English is a pass-through (the catalog map is empty,
// the resolver falls through to the `[i18n:<key>]` debug shape for every
// key). French + German carry partial translations so operators can see
// both resolved strings AND missing-key fallbacks on the same render. The
// `Catalog` map is the substrate the renderer's `BindingResolver`
// constructs into an `II18nResolver` via `BindingResolver.makeI18nResolver`.
//
// Keys are catalog-internal — `catalog.<spec-id>.<field>` — and live with
// the Matrix builders that reference them (Matrix.fs swaps `TextSource.Literal`
// for `TextSource.Bound (Binding.I18n key None)` on representative cards).

type LocaleOption =
    { Id: string
      Label: string
      Catalog: Map<string, string> }

let localeOptions: LocaleOption list =
    [ { Id = "en"
        Label = "English (pass-through)"
        Catalog = Map.empty }
      { Id = "fr"
        Label = "Français (test)"
        Catalog =
          Map.ofList
              [ "catalog.metric.label", "Recettes"
                "catalog.callout.heading.default", "Bandeau par défaut"
                "catalog.callout.heading.subdued", "Bandeau discret"
                "catalog.callout.heading.brand", "Bandeau marque"
                "catalog.callout.heading.success", "Bandeau succès"
                "catalog.callout.heading.warning", "Bandeau avertissement"
                "catalog.callout.heading.critical", "Bandeau critique"
                "catalog.callout.heading.info", "Bandeau info"
                "catalog.callout.body", "Bannière de niveau / mode / statut dimensionnée pour un bloc."
                "catalog.button.label", "Lancer l'analyse"
                "catalog.labelValueRow.label", "Chiffre d'affaires total" ] }
      { Id = "de"
        Label = "Deutsch (test, partial)"
        Catalog =
          Map.ofList
              [ "catalog.metric.label", "Umsatz"
                "catalog.callout.heading.default", "Standard-Hinweis"
                "catalog.callout.heading.warning", "Warn-Hinweis"
                "catalog.callout.heading.critical", "Kritischer Hinweis"
                "catalog.button.label", "Analyse starten" ] } ]

// ─── Model + Msg ──────────────────────────────────────────────────────────

type Msg =
    | SelectKind of string
    | SelectTheme of string
    | SelectLocale of string
    | ToggleA11yAudit

type Model =
    { ActiveKind: string
      ActiveTheme: ThemeOption
      ActiveLocale: LocaleOption
      A11yAuditOn: bool }

// ─── URL state — deep-linking (Phase 169) ─────────────────────────────────
//
// The gallery's kind / theme / locale / a11y selection is reflected into the
// query string so any view is deep-linkable AND the static site needs no
// server-side routing: the SPA reads `window.location.search` on load and
// resolves the rest client-side. Query params (not path segments) are
// deliberate — a static file host serves `index.html` for the directory and
// the client does the routing, so no 404-rewrite config is required on
// GitHub Pages / any plain file host. The reader tolerates absent / unknown
// values by falling back to the first option, so a hand-typed or stale link
// degrades gracefully rather than 404ing or rendering blank.

let private queryParam (key: string) : string option =
    let search = Browser.Dom.window.location.search

    if System.String.IsNullOrEmpty search then
        None
    else
        search.TrimStart('?').Split('&')
        |> Array.tryPick (fun pair ->
            let parts = pair.Split('=')

            if parts.Length >= 1 && parts[0] = key then
                if parts.Length >= 2 then
                    Some(System.Uri.UnescapeDataString parts[1])
                else
                    Some ""
            else
                None)

let private resolveKind (id: string option) : string =
    match id with
    | Some k when Matrix.entries |> List.exists (fun e -> e.Id = k) -> k
    | _ -> (Matrix.entries |> List.head).Id

let private resolveTheme (id: string option) : ThemeOption =
    id
    |> Option.bind (fun i -> themeOptions |> List.tryFind (fun t -> t.Id = i))
    |> Option.defaultValue (List.head themeOptions)

let private resolveLocale (id: string option) : LocaleOption =
    id
    |> Option.bind (fun i -> localeOptions |> List.tryFind (fun l -> l.Id = i))
    |> Option.defaultValue (List.head localeOptions)

let private resolveA11y (v: string option) : bool =
    match v with
    | Some s -> s = "1" || s = "true" || s = "on"
    | None -> false

/// Reflect the active selection back into the address bar without adding a
/// history entry (replaceState — the gallery is one logical page, not a
/// navigation stack). Run as an Elmish effect so `update` stays pure.
let private writeUrl (model: Model) : unit =
    let parts =
        [ "kind=" + System.Uri.EscapeDataString model.ActiveKind
          "theme=" + System.Uri.EscapeDataString model.ActiveTheme.Id
          "locale=" + System.Uri.EscapeDataString model.ActiveLocale.Id
          if model.A11yAuditOn then
              "a11y=1" ]

    Browser.Dom.window.history.replaceState (null, "", "?" + String.concat "&" parts)

let private syncUrl (model: Model) : Cmd<Msg> = Cmd.ofEffect (fun _ -> writeUrl model)

let init () : Model * Cmd<Msg> =
    let model =
        { ActiveKind = resolveKind (queryParam "kind")
          ActiveTheme = resolveTheme (queryParam "theme")
          ActiveLocale = resolveLocale (queryParam "locale")
          A11yAuditOn = resolveA11y (queryParam "a11y") }

    // Normalise the address bar on load so a partial / stale link rewrites to
    // the canonical, fully-qualified form the user can re-copy.
    model, syncUrl model

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    let next =
        match msg with
        | SelectKind id -> { model with ActiveKind = id }
        | SelectTheme id ->
            match themeOptions |> List.tryFind (fun t -> t.Id = id) with
            | Some t -> { model with ActiveTheme = t }
            | None -> model
        | SelectLocale id ->
            match localeOptions |> List.tryFind (fun l -> l.Id = id) with
            | Some l -> { model with ActiveLocale = l }
            | None -> model
        | ToggleA11yAudit ->
            { model with
                A11yAuditOn = not model.A11yAuditOn }

    next, syncUrl next

// ─── Render context shared by every card ─────────────────────────────────

// Matrix nodes are `Node<unit>` (see Matrix.fs `KindEntry`); the
// catalog's own Msg flows through the Elmish dispatch outside the
// renderer (side-nav clicks, theme switcher). The renderer needs a
// unit-typed context for the matrix cards.
//
// Catalog-axis tail: the active locale's catalog feeds
// `BindingResolver.makeI18nResolver` into `BindingSources.I18nResolver`,
// so `Binding.I18n("catalog.<key>", None)` values in matrix specs resolve
// against the selected locale. Keys absent from the locale's map fall
// through to the `[i18n:<key>]` debug shape (the en pass-through locale's
// catalog is empty so every key shows the placeholder — the contract is
// visible at sight).
let private renderContext (locale: LocaleOption) : Render.RenderContext<unit> =
    let sources =
        { BindingResolver.empty with
            I18nResolver = BindingResolver.makeI18nResolver locale.Catalog }

    { Sources = sources
      Runtime = Runtime.diagnostic
      VisAdapter = VisAdapter.noOp<unit>
      Dispatch = (fun () -> ())
      TelemetrySink = None
      InErrorBoundary = false
      Fragments = Map.empty
      ExpandingFragments = Set.empty
      Scope = None
      SessionContext = Map.empty
      // Phase 889 — no user-action recording in the samples.
      ActionSink = None
      CurrentNodeId = None }

// ─── Side-nav ────────────────────────────────────────────────────────────

let private sideNav (entries: Matrix.KindEntry list) (activeId: string) (dispatch: Msg -> unit) : ReactElement =
    let groups = entries |> List.groupBy (fun e -> e.Group)

    let renderGroup (group: string, items: Matrix.KindEntry list) : ReactElement list =
        let header = Html.div [ prop.className "catalog-sidenav-group"; prop.text group ]

        let buttons =
            [ for item in items ->
                  Html.button
                      [ prop.className
                            [ "catalog-sidenav-item"
                              if item.Id = activeId then
                                  "is-active" ]
                        prop.onClick (fun _ -> dispatch (SelectKind item.Id))
                        prop.text item.Label ] ]

        header :: buttons

    let children = groups |> List.collect renderGroup

    Html.nav [ prop.className "catalog-sidenav"; prop.children children ]

// ─── Top bar (title + theme / locale switcher + a11y audit toggle) ──────

let private topbar (model: Model) (dispatch: Msg -> unit) : ReactElement =
    Html.header
        [ prop.className "catalog-topbar"
          prop.children
              [ Html.h1 [ prop.text (sprintf "Fuaran visual component catalog — %s" model.ActiveKind) ]
                Html.div
                    [ prop.className "catalog-topbar-controls"
                      prop.children
                          [ Html.div
                                [ prop.className "catalog-theme-switcher"
                                  prop.children
                                      [ Html.label [ prop.htmlFor "theme-switcher"; prop.text "Theme" ]
                                        Html.select
                                            [ prop.id "theme-switcher"
                                              prop.value model.ActiveTheme.Id
                                              prop.onChange (fun (v: string) -> dispatch (SelectTheme v))
                                              prop.children
                                                  [ for opt in themeOptions ->
                                                        Html.option [ prop.value opt.Id; prop.text opt.Label ] ] ] ] ]
                            Html.div
                                [ prop.className "catalog-locale-switcher"
                                  prop.children
                                      [ Html.label [ prop.htmlFor "locale-switcher"; prop.text "Locale" ]
                                        Html.select
                                            [ prop.id "locale-switcher"
                                              prop.value model.ActiveLocale.Id
                                              prop.onChange (fun (v: string) -> dispatch (SelectLocale v))
                                              prop.children
                                                  [ for opt in localeOptions ->
                                                        Html.option [ prop.value opt.Id; prop.text opt.Label ] ] ] ] ]
                            Html.label
                                [ prop.className "catalog-a11y-toggle"
                                  prop.children
                                      [ Html.input
                                            [ prop.type'.checkbox
                                              prop.id "a11y-audit-toggle"
                                              prop.isChecked model.A11yAuditOn
                                              prop.onChange (fun (_: bool) -> dispatch ToggleA11yAudit) ]
                                        Html.span [ prop.text "A11y audit" ] ] ] ] ] ] ]

// ─── A11y audit projection (catalog-axis tail) ───────────────
//
// Two views of a Node's `Accessibility` trait:
//
//   - **Missing-metadata report.** When `node.Accessibility = None`, list
//     the fields a typical interactive / notification shape would expect
//     (Label / Role / LiveRegion). Surfaced as a tooltip + red outline.
//   - **Resolved-attribute inspector.** When `node.Accessibility = Some _`,
//     run the same `accessibilityAttributes` projection the renderer uses
//     and show the resulting `aria-* / role / aria-live` pairs verbatim.
//
// Inspector only walks the root Node — child nodes inherit defaults from
// their own smart constructors and would clutter the per-card pane if
// enumerated recursively. A future deeper walker can land if the audit
// graduates from "spotlight" to "tree-wide report".

let private missingAccessibilityFields () : string list =
    // The fields the renderer reads. Catalog reports all six so operators
    // see the full surface they'd populate if they opted into the trait.
    [ "Label"; "LabelledBy"; "DescribedBy"; "Role"; "LiveRegion"; "Hidden" ]

let private a11yInspectorPane (ctx: Render.RenderContext<unit>) (node: Node<unit>) : ReactElement =
    match node.Accessibility with
    | None ->
        let fields = missingAccessibilityFields ()

        Html.div
            [ prop.className "catalog-a11y-inspector catalog-a11y-inspector-missing"
              prop.children
                  [ Html.div
                        [ prop.className "catalog-a11y-inspector-heading"
                          prop.text "Accessibility = None" ]
                    Html.div
                        [ prop.className "catalog-a11y-inspector-hint"
                          prop.text "Renderer emits no aria-* / role attributes. Fields available to set:" ]
                    Html.ul
                        [ prop.className "catalog-a11y-missing-fields"
                          prop.children [ for field in fields -> Html.li [ prop.text field ] ] ] ] ]
    | Some _ ->
        let attrs = Render.accessibilityAttributes ctx.Sources node.Accessibility

        let body =
            if List.isEmpty attrs then
                [ Html.div
                      [ prop.className "catalog-a11y-inspector-hint"
                        prop.text
                            "Accessibility = Some, but every field resolves to no emission (renderer skips this Node)." ] ]
            else
                [ Html.div
                      [ prop.className "catalog-a11y-inspector-heading"
                        prop.text "Emitted aria-* attributes" ]
                  Html.dl
                      [ prop.className "catalog-a11y-attrs"
                        prop.children
                            [ for (k, v) in attrs do
                                  Html.dt [ prop.text k ]
                                  Html.dd [ prop.text v ] ] ] ]

        Html.div [ prop.className "catalog-a11y-inspector"; prop.children body ]

// ─── Per-card render: live + JSON wire shape + F# snippet ───────────────

/// Copy the card's canonical wire JSON to the clipboard via the browser
/// Clipboard API. The `Action.WriteToClipboard` fixture proves the
/// renderer-routed path; this gallery card is plain Feliz chrome *outside*
/// the renderer's dispatch, so it calls the API directly. Guarded against a
/// non-secure-context host (where `navigator.clipboard` is undefined) so the
/// button no-ops rather than throwing.
[<Emit("navigator.clipboard && navigator.clipboard.writeText($0)")>]
let private copyToClipboard (text: string) : unit = jsNative

let private renderCard
    (ctx: Render.RenderContext<unit>)
    (a11yAuditOn: bool)
    (entry: Matrix.KindEntry)
    (tone: ToneVariant)
    (weight: StyleWeight)
    (emphasis: Emphasis)
    : ReactElement =
    let node = entry.Build(tone, weight, emphasis)
    let jsonWire = JsonShape.toCanonical node
    let prettyJson = JsonShape.prettyPrint 2 jsonWire

    let snippet =
        sprintf
            "// Tone = %s, Weight = %s, Emphasis = %s\n// Fuaran smart constructor: Fuaran.%s (id) (spec record)"
            (Matrix.toneLabel tone)
            (Matrix.weightLabel weight)
            (Matrix.emphasisLabel emphasis)
            (entry.Id.ToLower())

    let missingAccessibility = Option.isNone node.Accessibility

    let cardClassName =
        if a11yAuditOn && missingAccessibility then
            "catalog-card catalog-a11y-missing"
        else
            "catalog-card"

    let cardTitle =
        if a11yAuditOn && missingAccessibility then
            "Accessibility = None — renderer emits no aria-* attributes for this Node."
        else
            ""

    Html.article
        [ prop.key (
              sprintf
                  "%s-%s-%s-%s"
                  entry.Id
                  (Matrix.toneLabel tone)
                  (Matrix.weightLabel weight)
                  (Matrix.emphasisLabel emphasis)
          )
          prop.className cardClassName
          prop.title cardTitle
          prop.children
              [ Html.div
                    [ prop.className "catalog-card-header"
                      prop.children
                          [ Html.span [ prop.text entry.Label ]
                            Html.div
                                [ prop.className "catalog-card-tags"
                                  prop.children
                                      [ Html.span
                                            [ prop.className "catalog-card-tag"; prop.text (Matrix.toneLabel tone) ]
                                        Html.span
                                            [ prop.className "catalog-card-tag"; prop.text (Matrix.weightLabel weight) ]
                                        Html.span
                                            [ prop.className "catalog-card-tag"
                                              prop.text (Matrix.emphasisLabel emphasis) ] ] ] ] ]
                Html.div
                    [ prop.className "catalog-card-render"
                      prop.children [ Render.render ctx node ] ]
                (if a11yAuditOn then
                     a11yInspectorPane ctx node
                 else
                     Html.none)
                Html.details
                    [ prop.className "catalog-disclosure"
                      prop.children
                          [ Html.summary [ prop.text "JSON wire shape" ]
                            Html.button
                                [ prop.className "catalog-copy-json"
                                  prop.type'.button
                                  prop.onClick (fun _ -> copyToClipboard prettyJson)
                                  prop.text "Copy JSON" ]
                            Html.pre [ prop.text prettyJson ] ] ]
                Html.details
                    [ prop.className "catalog-disclosure"
                      prop.children [ Html.summary [ prop.text "F# author code" ]; Html.pre [ prop.text snippet ] ] ] ] ]

// ─── Main pane: matrix grid for the active NodeKind ──────────────────────

let private mainPane (model: Model) (_dispatch: Msg -> unit) : ReactElement =
    let entries = Matrix.entries

    let active =
        entries
        |> List.tryFind (fun e -> e.Id = model.ActiveKind)
        |> Option.defaultValue (List.head entries)

    let ctx = renderContext model.ActiveLocale

    let cards =
        [ for tone in Matrix.allTones do
              for weight in Matrix.allWeights do
                  for emphasis in Matrix.allEmphases do
                      renderCard ctx model.A11yAuditOn active tone weight emphasis ]

    Html.main
        [ prop.className "catalog-main"
          prop.children
              [ Html.h2 [ prop.text active.Label ]
                Html.p [ prop.className "catalog-kind-hint"; prop.text active.Hint ]
                Html.div [ prop.className "catalog-matrix"; prop.children cards ] ] ]

// ─── Root view ───────────────────────────────────────────────────────────

let view (model: Model) (dispatch: Msg -> unit) : ReactElement =
    React.Fragment
        [ Render.themeStyleElement model.ActiveTheme.Theme
          Html.div
              [ prop.className "catalog-shell"
                prop.children
                    [ topbar model dispatch
                      sideNav (Matrix.entries) model.ActiveKind dispatch
                      mainPane model dispatch ] ] ]
