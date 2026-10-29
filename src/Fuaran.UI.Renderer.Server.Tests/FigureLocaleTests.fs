module Fuaran.UI.Renderer.Server.Tests.FigureLocaleTests

// ============================================================================
//  Phase 2490 — a numeric figure can carry a locale.
//
//  `CellFormat`'s figure cases (`Number`, `Currency`, `Percent`) take an
//  optional `locale`. Absent, a figure renders the locale-free projection it
//  always did; present, it renders through `Formatting.format`, the function a
//  `Binding.Format` slot renders through for the matching `Format` case. These
//  pins hold both halves on the server renderer, the email and speech
//  projections that share its formatter, and the ambient reading of `Ambient`.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server

let private contains (needle: string) (haystack: string) =
    haystack.Contains(needle, System.StringComparison.Ordinal)

let private revenue (format: CellFormat) : Node<obj> =
    Fuaran.metric
        "revenue"
        { Defaults.metric with
            Label = TextSource.Literal "Revenue"
            Value = Binding.Static(Some 412380.0)
            Format = format }

let private gbp (locale: LocaleSource option) =
    CellFormat.Currency("GBP", None, locale)

[<Tests>]
let tests =
    testList
        "Phase 2490 — a figure carrying a locale"
        [ test "a GBP figure in en-GB renders grouped and symbol-led" {
              let out =
                  Render.render BindingResolver.empty (revenue (gbp (Some(LocaleSource.Explicit "en-GB"))))

              Expect.isTrue (contains ">£412,380.00<" out) $"grouped, symbol-led figure in: {out}"
          }

          test "the same figure without a locale renders exactly as before" {
              let out = Render.render BindingResolver.empty (revenue (gbp None))
              Expect.isTrue (contains ">GBP 412380.00<" out) $"the locale-free projection in: {out}"
          }

          test "a present locale is the Binding.Format route for the matching Format case" {
              for tag in [ "en-GB"; "de-DE"; "fr-FR"; "" ] do
                  for cell, fmt in
                      [ CellFormat.Number(Some 2, None), Format.Number(Some 2)
                        CellFormat.Currency("EUR", None, None), Format.Currency("EUR", None)
                        CellFormat.Percent(Some 1, None), Format.Percent(Some 1) ] do
                      let located = format.inLocale (LocaleSource.Explicit tag) cell

                      Expect.equal
                          (Formatting.formatNumber located 1234.5678)
                          (Formatting.format tag fmt 1234.5678)
                          $"{tag} {cell}"
          }

          test "Ambient reads the render's ambient locale" {
              let located = format.inLocale LocaleSource.Ambient (format.number (Some 1))
              Expect.equal (Formatting.formatNumberIn "de-DE" located 1234.5) "1.234,5" "the ambient tag"

              let sources =
                  { BindingResolver.empty with
                      Locale = "de-DE" }

              let out = Render.render sources (revenue (gbp (Some LocaleSource.Ambient)))
              Expect.isTrue (contains "412.380,00" out) $"the ambient de-DE figure in: {out}"
          }

          test "a figure case that carries no locale is unchanged by inLocale" {
              let duration = CellFormat.Duration(DurationUnit.Minutes, DurationStyle.Compact)

              match format.inLocale LocaleSource.Ambient duration with
              | CellFormat.Duration(DurationUnit.Minutes, DurationStyle.Compact) -> ()
              | other -> failtestf "no locale member, unchanged: %A" other
          }

          test "an exact decimal with a locale converts once, at the formatter boundary" {
              let located = gbp (Some(LocaleSource.Explicit "en-GB"))
              Expect.equal (Formatting.formatDecimal located "412380.005") "£412,380.01" "rounded once by the formatter"

              Expect.equal
                  (Formatting.formatDecimal (gbp None) "412380.005")
                  "GBP 412380.01"
                  "locale-free: from the digits"
          }

          test "the email and speech projections read the same figure" {
              let node = revenue (gbp (Some(LocaleSource.Explicit "en-GB")))
              let email = Email.render BindingResolver.empty node
              Expect.isTrue (contains "£412,380.00" email) "the email digest's figure"
              let script, _ = Speech.projectStatic node
              Expect.isTrue (contains "£412,380.00" (Speech.toPlainText script)) "the spoken figure"
          } ]

/// Phase 2499 — every figure case carries `decimals`: a currency figure can set its
/// fraction digits, on the locale-free projection and the locale formatter alike.
let private gbpDigits (decimals: int option) (locale: LocaleSource option) =
    CellFormat.Currency("GBP", decimals, locale)

[<Tests>]
let decimalsTests =
    testList
        "Phase 2499 — a currency figure can set its fraction digits"
        [ test "a GBP figure at 0 decimals in en-GB renders whole pounds" {
              let out =
                  Render.render
                      BindingResolver.empty
                      (revenue (gbpDigits (Some 0) (Some(LocaleSource.Explicit "en-GB"))))

              Expect.isTrue (contains ">£412,380<" out) $"whole pounds in: {out}"
          }

          test "without decimals the located figure keeps the currency's digits, and without either it is unchanged" {
              let located =
                  Render.render BindingResolver.empty (revenue (gbpDigits None (Some(LocaleSource.Explicit "en-GB"))))

              Expect.isTrue (contains ">£412,380.00<" located) $"the currency's digits in: {located}"
              let plain = Render.render BindingResolver.empty (revenue (gbpDigits None None))
              Expect.isTrue (contains ">GBP 412380.00<" plain) $"the locale-free projection in: {plain}"
          }

          test "a present decimals sets the locale-free currency's digits" {
              Expect.equal (Formatting.formatNumber (gbpDigits (Some 0) None) 412380.4) "GBP 412380" "0 digits"
              Expect.equal (Formatting.formatNumber (gbpDigits (Some 3) None) 1.5) "GBP 1.500" "3 digits"
              Expect.equal (Formatting.formatDecimal (gbpDigits (Some 0) None) "412380.5") "GBP 412381" "exact, half up"

              Expect.equal
                  (Formatting.formatDecimal (gbpDigits (Some 3) None) "0.0005")
                  "GBP 0.001"
                  "exact: from the digits, never through a float"
          }

          test "a located Percent and Number render at their decimals" {
              let percent =
                  format.inLocale (LocaleSource.Explicit "en-GB") (format.percent (Some 1))

              Expect.equal (Formatting.formatNumber percent 0.0425) "4.3%" "Percent at 1 place"

              let number =
                  format.inLocale (LocaleSource.Explicit "en-GB") (format.number (Some 0))

              Expect.equal (Formatting.formatNumber number 1234567.6) "1,234,568" "Number at 0 places"
          }

          test "a located currency at decimals is the Binding.Format route with the same digits" {
              for tag in [ "en-GB"; "de-DE"; "" ] do
                  for d in [ 0; 1; 3 ] do
                      let located =
                          format.currency "EUR"
                          |> format.withDecimals d
                          |> format.inLocale (LocaleSource.Explicit tag)

                      Expect.equal
                          (Formatting.formatNumber located 1234.5678)
                          (Formatting.format tag (localeFormat.currencyDigits "EUR" d) 1234.5678)
                          $"{tag} {d}"
          }

          test "withDecimals sets the digits on the three figure cases and leaves the rest" {
              match format.withDecimals 0 (format.currency "GBP") with
              | CellFormat.Currency("GBP", Some 0, None) -> ()
              | other -> failtestf "currency: %A" other

              match format.withDecimals 2 (format.number None) with
              | CellFormat.Number(Some 2, None) -> ()
              | other -> failtestf "number: %A" other

              match format.withDecimals 1 (format.percent None) with
              | CellFormat.Percent(Some 1, None) -> ()
              | other -> failtestf "percent: %A" other

              match format.withDecimals 1 (CellFormat.SignificantDigits 3) with
              | CellFormat.SignificantDigits 3 -> ()
              | other -> failtestf "no decimals member, unchanged: %A" other
          } ]
