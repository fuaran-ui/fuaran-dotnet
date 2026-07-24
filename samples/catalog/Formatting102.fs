module Fuaran.Samples.Catalog.Formatting102

// ============================================================================
//  Catalog page for Phase 102 — locale-aware number / date / currency / percent
//  / relative-time formatting bindings (`Binding.Format`).
//
//  Shape: one row per `Format` case, each rendering the SAME numeric source
//  through `binding.format` against several locales side-by-side so the
//  locale-correctness is visible (e.g. "1,234.57" en-US vs "1.234,57" de-DE vs
//  "1 234,57" fr-FR; "£1,234.50" vs "¥1,235" vs "1.234,50 €"). A final row
//  exercises `LocaleSource.Ambient`, resolved against the surface's
//  `BindingResolver` `Locale` field (the 12.I locale source) — set to "en-GB"
//  here. Under Fable these render via the native browser `Intl` APIs.
//
//  Mounted at `?formatting-102=1`. The browser counterpart to the .NET
//  `BindingResolver` Format-resolution tests + the corpus round-trip fixture.
// ============================================================================

open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

type Model = unit
type Msg = NoOp

let init () : Model = ()
let update (_: Msg) (model: Model) : Model = model

// One labelled value: a heading caption + the live formatted string. The
// formatted value is a `Binding.Format` dropped into a `Binding<string>` slot
// (`TextSource.Bound`).
let private fmtCell (id: string) (caption: string) (source: float) (fmt: Format) (locale: LocaleSource) : Node<Msg> =
    Fuaran.stack
        id
        { Defaults.stack with
            Children =
                [ Fuaran.heading
                      (id + "-cap")
                      { Defaults.heading with
                          Level = 4
                          Variant = HeadingVariant.Eyebrow
                          Text = TextSource.Literal caption }
                  Fuaran.heading
                      (id + "-val")
                      { Defaults.heading with
                          Level = 3
                          Variant = HeadingVariant.Lead
                          Text = TextSource.Bound(binding.format (binding.``static`` source) fmt locale) } ] }

// A labelled axis: a heading + a horizontal strip of cells (one per locale).
let private axis (id: string) (title: string) (cells: Node<Msg> list) : Node<Msg> =
    Fuaran.stack
        id
        { Defaults.stack with
            Children =
                [ Fuaran.heading
                      (id + "-title")
                      { Defaults.heading with
                          Level = 2
                          Text = TextSource.Literal title }
                  Fuaran.stack
                      (id + "-strip")
                      { Defaults.stack with
                          Orientation = Orientation.Horizontal
                          Wrap = true
                          Children = cells } ] }

let private tree: Node<Msg> =
    Fuaran.stack
        "formatting-102"
        { Defaults.stack with
            Children =
                [ axis
                      "axis-number"
                      "Number (1234.567, 2 decimals)"
                      [ fmtCell "num-us" "en-US" 1234.567 (localeFormat.number (Some 2)) (locale.explicit "en-US")
                        fmtCell "num-de" "de-DE" 1234.567 (localeFormat.number (Some 2)) (locale.explicit "de-DE")
                        fmtCell "num-fr" "fr-FR" 1234.567 (localeFormat.number (Some 2)) (locale.explicit "fr-FR") ]
                  axis
                      "axis-currency"
                      "Currency (1234.5)"
                      [ fmtCell "cur-gb" "GBP / en-GB" 1234.5 (localeFormat.currency "GBP") (locale.explicit "en-GB")
                        fmtCell "cur-us" "USD / en-US" 1234.5 (localeFormat.currency "USD") (locale.explicit "en-US")
                        fmtCell "cur-jp" "JPY / ja-JP" 1234.5 (localeFormat.currency "JPY") (locale.explicit "ja-JP")
                        fmtCell "cur-de" "EUR / de-DE" 1234.5 (localeFormat.currency "EUR") (locale.explicit "de-DE") ]
                  axis
                      "axis-percent"
                      "Percent (ratio 0.4267)"
                      [ fmtCell "pct-us" "en-US" 0.4267 (localeFormat.percent (Some 1)) (locale.explicit "en-US")
                        fmtCell "pct-fr" "fr-FR" 0.4267 (localeFormat.percent (Some 1)) (locale.explicit "fr-FR") ]
                  axis
                      "axis-date"
                      "Date (Unix 1700000000s)"
                      [ fmtCell
                            "dt-gb"
                            "Medium / en-GB"
                            1700000000.0
                            (localeFormat.date DateStyle.Medium)
                            (locale.explicit "en-GB")
                        fmtCell
                            "dt-fr"
                            "Full / fr-FR"
                            1700000000.0
                            (localeFormat.date DateStyle.Full)
                            (locale.explicit "fr-FR") ]
                  axis
                      "axis-relative"
                      "RelativeTime"
                      [ fmtCell
                            "rel-past"
                            "-3 days / en-US"
                            -3.0
                            (localeFormat.relativeTime RelativeTimeUnit.Day)
                            (locale.explicit "en-US")
                        fmtCell
                            "rel-future"
                            "+2 hours / es-ES"
                            2.0
                            (localeFormat.relativeTime RelativeTimeUnit.Hour)
                            (locale.explicit "es-ES") ]
                  axis
                      "axis-ambient"
                      "Ambient locale (resolver Locale = en-GB)"
                      [ fmtCell "amb-cur" "Currency GBP" 9999.9 (localeFormat.currency "GBP") locale.ambient
                        fmtCell "amb-pct" "Percent" 0.075 (localeFormat.percent (Some 2)) locale.ambient ] ] }

let view (_model: Model) (dispatch: Msg -> unit) : ReactElement =
    // The Ambient row resolves against this `Locale`; Explicit rows pin their
    // own tag and ignore it.
    let ctx: Render.RenderContext<Msg> =
        { Sources =
            { BindingResolver.empty with
                Locale = "en-GB" }
          Runtime = BrowserRuntime.create ()
          VisAdapter = VisAdapter.noOp<Msg>
          Dispatch = dispatch
          TelemetrySink = None
          InErrorBoundary = false
          Fragments = Map.empty
          ExpandingFragments = Set.empty
          Scope = None }

    React.Fragment
        [ Render.themeStyleElement Defaults.theme
          Html.div
              [ prop.id "formatting-102-page"
                prop.className "catalog-formatting"
                prop.style [ style.padding 24; style.maxWidth 960 ]
                prop.children
                    [ Html.h1 [ prop.text "Locale-aware formatting bindings (Phase 102)" ]
                      Html.p
                          [ prop.text
                                "Each row renders one numeric source through binding.format against several locales. Number grouping, currency symbols, percent shape, date style and relative-time wording all derive from the bounded Format intent × the locale — no raw Intl format strings. The Ambient row defers to the surface's resolver Locale (the 12.I locale source)." ]
                      Render.render ctx tree ] ] ]
