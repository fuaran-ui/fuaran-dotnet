module Fuaran.UI.Tests.FormatBinding

// ============================================================================
//  `Binding.Format` + `Formatting.format` resolution semantics (Phase 102).
//
//  Asserts (on the .NET pipeline — the documented `System.Globalization`
//  fallback; the browser `Intl` path is exercised by the Fable catalog page):
//  - `Formatting.format` produces a sensible localised string for each `Format`
//    case under an explicit locale tag.
//  - `BindingResolver.resolve` resolves a `Binding.Format` wrapping a numeric
//    source to the formatted string, and `LocaleSource.Ambient` defers to
//    `BindingSources.Locale`.
//  - An unresolved numeric source propagates as `NotResolved` (the formatter
//    is not invoked against a missing value).
//
//  These pin the .NET fallback's *shape* (grouping, percent suffix, ISO-code
//  currency prefix, relative-time wording), not byte-parity with `Intl` — see
//  Formatting.fs header for the parity caveats.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

let private resolvesTo (sources: BindingResolver.BindingSources) (binding: Binding<string>) : string =
    match BindingResolver.resolve sources binding with
    | BindingResolver.Resolved s -> s
    | other -> failtestf "Expected Resolved, got %A" other

[<Tests>]
let tests =
    testList
        "Binding.Format + Formatting.format (.NET fallback)"
        [ test "Number formats with grouping + fixed fraction digits (en-US)" {
              let s = Formatting.format "en-US" (Format.Number(Some 2)) 1234.5
              Expect.equal s "1,234.50" "two-decimal grouped number"
          }

          test "Number with None decimals keeps up to three fraction digits" {
              let s = Formatting.format "en-US" (Format.Number None) 1234.567
              Expect.equal s "1,234.567" "default fraction handling"
          }

          test "Currency uses the resolved currency symbol in the .NET fallback" {
              let s = Formatting.format "en-GB" (Format.Currency "GBP") 1234.5
              Expect.equal s "£1,234.50" "GBP symbol resolved via RegionInfo + locale pattern"
          }

          test "Currency symbol + placement follow the locale pattern (de-DE/EUR)" {
              let s = Formatting.format "de-DE" (Format.Currency "EUR") 1234.5
              // de-DE puts the symbol after the amount with comma decimals.
              Expect.stringContains s "€" "euro symbol resolved"
              Expect.stringContains s "1.234,50" "de-DE grouping + decimal"
          }

          test "Common currency symbols are platform-deterministic (USD glyph)" {
              // The symbol resolves from the curated ISO-4217 table, not the
              // RegionInfo scan, so the glyph is identical on Windows NLS and
              // Linux/ICU (where the server renderer runs). Guards the EUR→"EUR"
              // server-side regression the RegionInfo-only path exhibited.
              let s = Formatting.format "en-US" (Format.Currency "USD") 1234.5
              Expect.equal s "$1,234.50" "USD symbol + en-US pattern"
          }

          test "Percent reads the source as a ratio and appends a percent sign" {
              let s = Formatting.format "en-US" (Format.Percent(Some 0)) 0.42
              Expect.stringContains s "42" "0.42 ratio → 42 percent"
              Expect.stringContains s "%" "percent sign present"
          }

          test "Date reads whole Unix-epoch seconds" {
              let s = Formatting.format "en-US" (Format.Date DateStyle.Short) 1700000000.0
              // 1700000000s = 2023-11-14 (UTC). Short date includes the year.
              Expect.stringContains s "2023" "year present in short date"
          }

          test "RelativeTime past reads a negative count as 'ago' (English fallback)" {
              let s = Formatting.format "en-US" (Format.RelativeTime RelativeTimeUnit.Day) -3.0
              Expect.equal s "3 days ago" "negative day count"
          }

          test "RelativeTime future reads a positive count as 'in N' (English fallback)" {
              let s = Formatting.format "en-US" (Format.RelativeTime RelativeTimeUnit.Hour) 2.0
              Expect.equal s "in 2 hours" "positive hour count"
          }

          test "RelativeTime singular drops the plural 's'" {
              let s = Formatting.format "en-US" (Format.RelativeTime RelativeTimeUnit.Day) -1.0
              Expect.equal s "1 day ago" "singular day"
          }

          test "Binding.Format resolves a Static numeric source to the formatted string" {
              let b: Binding<string> =
                  Binding.Format(Binding.Static(Some 1234.5), Format.Currency "GBP", LocaleSource.Explicit "en-GB")

              Expect.equal (resolvesTo BindingResolver.empty b) "£1,234.50" "explicit-locale currency"
          }

          test "LocaleSource.Ambient defers to BindingSources.Locale" {
              let b: Binding<string> =
                  Binding.Format(Binding.Static(Some 1234.5), Format.Number(Some 1), LocaleSource.Ambient)

              let sources =
                  { BindingResolver.empty with
                      Locale = "en-US" }

              Expect.equal (resolvesTo sources b) "1,234.5" "ambient locale → en-US grouping"
          }

          // ── Phase 1114 — RTL locales ─────────────────────────────────────
          //
          // The claim under test is that the formatting layer needed NO
          // direction work: `CultureInfo` (ICU on every platform .NET runs on)
          // already carries the direction-aware shapes, so a right-to-left
          // locale formats correctly through the same code path as any other.
          // Verified rather than assumed, and the two shapes that actually
          // DIFFER from the left-to-right baseline are fixtured below.
          //
          // Asserted as PROPERTIES (which characters are present, which side the
          // symbol lands on) rather than as exact strings: these values come
          // from ICU's locale data, which moves with the runtime, and a
          // byte-pinned fixture would fail on a CLDR bump that changed nothing
          // this phase cares about.

          test "RTL difference 1 — the percent shape carries Arabic-Indic marks" {
              let arabic = Formatting.format "ar-EG" (Format.Percent(Some 1)) 0.42
              let latin = Formatting.format "en-US" (Format.Percent(Some 1)) 0.42

              Expect.stringContains arabic "٪" "ARABIC PERCENT SIGN, not U+0025"
              Expect.stringContains arabic "٫" "ARABIC DECIMAL SEPARATOR"

              // The invisible half, and the reason this is a DIRECTION-aware
              // shape rather than merely a different glyph: ICU appends U+061C
              // ARABIC LETTER MARK so the percent sign resolves on the correct
              // side of the number in a bidi run. Nothing in this repo produces
              // it — it arrives from the locale data, which is the finding.
              Expect.stringContains arabic "؜" "ARABIC LETTER MARK — the bidi control ICU appends"

              Expect.equal latin "42.0%" "the LTR baseline is unchanged"
              Expect.isFalse (latin.Contains "؜") "and carries no bidi control"
          }

          test "RTL difference 2 — the currency symbol changes SIDE, not just glyph" {
              // `Format.Currency` substitutes the requested ISO code's symbol
              // into the LOCALE's currency pattern (see `Formatting.fs`), and
              // that pattern is where direction shows up: Arabic and Hebrew put
              // the symbol after the amount, English before it. So the phase's
              // question — does the formatter need to know about direction —
              // is answered no: the pattern already did.
              let arabic = Formatting.format "ar-EG" (Format.Currency "USD") 1234.5
              let hebrew = Formatting.format "he-IL" (Format.Currency "USD") 1234.5
              let english = Formatting.format "en-US" (Format.Currency "USD") 1234.5

              Expect.stringEnds arabic "$" "ar-EG places the symbol after the amount"
              Expect.stringEnds hebrew "$" "he-IL places the symbol after the amount"
              Expect.stringStarts english "$" "en-US places it before"
          }

          test "an RTL locale that uses Latin digits still formats through the same path" {
              // Not every right-to-left locale uses Arabic-Indic digits — `he`
              // and `ur` do not — which is exactly why the direction of a
              // DOCUMENT and the digit shapes of a NUMBER are two independent
              // questions, and why `textDirection` is derived from the tag
              // rather than inferred from the formatted output.
              Expect.equal (Formatting.format "he-IL" (Format.Number(Some 2)) 1234.5) "1,234.50" "Hebrew: Latin digits"
              Expect.equal (Formatting.format "ur-PK" (Format.Number(Some 2)) 1234.5) "1,234.50" "Urdu: Latin digits"

              Expect.equal (Formatting.textDirection "he-IL") "rtl" "…and both are still right-to-left documents"
              Expect.equal (Formatting.textDirection "ur-PK") "rtl" ""
          }

          test "Formatting.textDirection is total over malformed and unknown tags" {
              // The value is emitted as a document attribute, so there is no
              // "refuse to answer" branch — an unreadable tag must resolve to
              // the recoverable direction rather than throw at render time.
              for tag in [ ""; "  "; "-"; "!!!"; "zz"; "x-private"; "en-US-u-ca-gregory" ] do
                  Expect.equal (Formatting.textDirection tag) "ltr" ("malformed/unknown tag: '" + tag + "'")

              // Case and separator normalisation, since a host's configured tag
              // is whatever the host's configuration file says.
              Expect.equal (Formatting.textDirection "AR-eg") "rtl" "case-insensitive"
              Expect.equal (Formatting.textDirection "ar_EG") "rtl" "underscore separator (a .NET/POSIX habit)"
          }

          test "Binding.Format over an unresolved Filter source propagates NotResolved" {
              // A Filter source with no value in the sources resolves to
              // NotResolved; the formatter must not be invoked.
              let b: Binding<string> =
                  Binding.Format(Binding.Filter("missing", None), Format.Number None, LocaleSource.Ambient)

              match BindingResolver.resolve BindingResolver.empty b with
              | BindingResolver.NotResolved -> ()
              | other -> failtestf "Expected NotResolved, got %A" other
          } ]
