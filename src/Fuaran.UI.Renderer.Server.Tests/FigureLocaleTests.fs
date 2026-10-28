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

let private gbp (locale: LocaleSource option) = CellFormat.Currency("GBP", locale)

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
                        CellFormat.Currency("EUR", None), Format.Currency "EUR"
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
