module Fuaran.UI.Renderer.Formatting

// ============================================================================
//  Locale-aware value formatting (Phase 102).
//
//  Projects the numeric source of a `Binding.Format` to a localised display
//  string. Two pipelines, one contract (FGP 4):
//
//   - Fable (browser): delegates to the native `Intl.NumberFormat` /
//     `Intl.DateTimeFormat` / `Intl.RelativeTimeFormat` APIs — full CLDR
//     locale data, the canonical rendering surface.
//   - .NET (Expecto / SSR / non-browser): a documented `System.Globalization`
//     fallback. It approximates the browser output — `CultureInfo`-driven
//     grouping + fraction digits for numbers/percent, a culture date format
//     for dates, and the locale's currency pattern with the requested ISO
//     code's symbol resolved from a curated ISO-4217 table (with a `RegionInfo`
//     scan for the long tail) — but does NOT match `Intl`
//     byte-for-byte (currency fraction digits follow the locale not the
//     currency, so JPY keeps 2 digits; relative-time is English-only;
//     date-style mapping is coarse). The fallback exists so
//     diagnostics + SSR produce a sensible string under the pure-.NET runner;
//     the browser pipeline is the visual source of truth.
//
//  `localeTag` is the resolved BCP-47 tag (e.g. "en-GB"). An empty string
//  means "use the runtime default locale" — `Intl` with `undefined`, .NET with
//  `CultureInfo.InvariantCulture` (the identity-default per the 12.I locale
//  source). The numeric source is interpreted per the `Format` case: `Date`
//  reads whole Unix-epoch seconds; `RelativeTime` reads a signed count of its
//  unit; the rest read a plain number (`Percent` a ratio).
// ============================================================================

open Fuaran.UI.Types

let private dateStyleStr (s: DateStyle) : string =
    match s with
    | DateStyle.Short -> "short"
    | DateStyle.Medium -> "medium"
    | DateStyle.Long -> "long"
    | DateStyle.Full -> "full"

let private relativeUnitStr (u: RelativeTimeUnit) : string =
    match u with
    | RelativeTimeUnit.Second -> "second"
    | RelativeTimeUnit.Minute -> "minute"
    | RelativeTimeUnit.Hour -> "hour"
    | RelativeTimeUnit.Day -> "day"
    | RelativeTimeUnit.Week -> "week"
    | RelativeTimeUnit.Month -> "month"
    | RelativeTimeUnit.Year -> "year"

#if FABLE_COMPILER

open Fable.Core
open Fable.Core.JsInterop

// An empty tag → `undefined`, so `Intl` falls back to the runtime default
// locale (identity-default per LocaleSource.Ambient).
[<Emit("($0 === '' ? undefined : $0)")>]
let private localeArg (tag: string) : obj = jsNative

[<Emit("new Intl.NumberFormat($0, $1).format($2)")>]
let private intlNumber (locale: obj) (options: obj) (value: float) : string = jsNative

[<Emit("new Intl.DateTimeFormat($0, $1).format(new Date($2 * 1000))")>]
let private intlDate (locale: obj) (options: obj) (unixSeconds: float) : string = jsNative

[<Emit("new Intl.RelativeTimeFormat($0, { numeric: 'auto' }).format($1, $2)")>]
let private intlRelative (locale: obj) (value: float) (unit: string) : string = jsNative

let private numberOptions (fmt: Format) : obj =
    match fmt with
    | Format.Number(Some d) -> createObj [ "minimumFractionDigits" ==> d; "maximumFractionDigits" ==> d ]
    | Format.Number None -> createObj []
    | Format.Currency isoCode -> createObj [ "style" ==> "currency"; "currency" ==> isoCode ]
    | Format.Percent(Some d) ->
        createObj
            [ "style" ==> "percent"
              "minimumFractionDigits" ==> d
              "maximumFractionDigits" ==> d ]
    | Format.Percent None -> createObj [ "style" ==> "percent" ]
    | Format.Date _
    | Format.RelativeTime _ -> createObj []

/// Format `value` per the bounded `Format` intent + resolved `localeTag`.
let format (localeTag: string) (fmt: Format) (value: float) : string =
    match fmt with
    | Format.Number _
    | Format.Currency _
    | Format.Percent _ -> intlNumber (localeArg localeTag) (numberOptions fmt) value
    | Format.Date dateStyle ->
        intlDate (localeArg localeTag) (createObj [ "dateStyle" ==> dateStyleStr dateStyle ]) value
    | Format.RelativeTime unit -> intlRelative (localeArg localeTag) value (relativeUnitStr unit)

#else

open System
open System.Globalization

let private culture (tag: string) : CultureInfo =
    if String.IsNullOrEmpty tag then
        CultureInfo.InvariantCulture
    else
        try
            CultureInfo(tag)
        with _ ->
            CultureInfo.InvariantCulture

let private numberPattern (decimals: int option) : string =
    match decimals with
    | Some d when d <= 0 -> "#,##0"
    | Some d -> "#,##0." + String('0', d)
    | None -> "#,##0.###"

/// Authoritative ISO-4217 → symbol for the common currencies. Consulted
/// before the `RegionInfo` scan below, because that scan is both
/// platform-dependent and enumeration-order-dependent: `RegionInfo.CurrencySymbol`
/// returns the glyph under Windows NLS but the bare ISO code for some cultures
/// under Linux/ICU, and the fold keeps whichever culture is enumerated last for
/// each code — so e.g. EUR resolved to "€" on Windows but "EUR" on Linux (where
/// the server renderer runs). Pinning the common codes makes server-side SSR
/// render the same glyph the browser `Intl` path emits. Codes outside this table
/// fall back to the scan, then to the code itself.
let private knownCurrencySymbols: Map<string, string> =
    Map
        [ "EUR", "€"
          "USD", "$"
          "GBP", "£"
          "JPY", "¥"
          "CNY", "¥"
          "CHF", "CHF"
          "AUD", "$"
          "CAD", "$"
          "NZD", "$"
          "HKD", "$"
          "SGD", "$"
          "INR", "₹"
          "KRW", "₩"
          "BRL", "R$"
          "RUB", "₽"
          "ZAR", "R"
          "SEK", "kr"
          "NOK", "kr"
          "DKK", "kr"
          "PLN", "zł"
          "CZK", "Kč"
          "HUF", "Ft"
          "TRY", "₺"
          "MXN", "$"
          "THB", "฿"
          "ILS", "₪" ]

/// ISO-4217 code → currency symbol for codes outside `knownCurrencySymbols`,
/// derived once by scanning every specific culture's `RegionInfo`
/// (`ISOCurrencySymbol` → `CurrencySymbol`). Cached lazily — the scan touches
/// ~hundreds of cultures, so it runs at most once per process. An unknown code
/// falls back to the code itself.
let private currencySymbols: Lazy<Map<string, string>> =
    lazy
        (CultureInfo.GetCultures CultureTypes.SpecificCultures
         |> Array.fold
             (fun acc culture ->
                 try
                     let region = RegionInfo culture.Name
                     Map.add region.ISOCurrencySymbol region.CurrencySymbol acc
                 with _ ->
                     acc)
             Map.empty)

let private currencySymbol (isoCode: string) : string =
    match Map.tryFind isoCode knownCurrencySymbols with
    | Some sym -> sym
    | None ->
        match Map.tryFind isoCode currencySymbols.Value with
        | Some sym -> sym
        | None -> isoCode

/// Format `value` per the bounded `Format` intent + resolved `localeTag`.
/// Documented .NET fallback — see the module header for the parity caveats.
let format (localeTag: string) (fmt: Format) (value: float) : string =
    let c = culture localeTag

    match fmt with
    | Format.Number decimals -> value.ToString(numberPattern decimals, c)
    | Format.Currency isoCode ->
        // Format with the locale's currency pattern (symbol placement,
        // grouping, decimal digits) but substitute the requested currency's
        // symbol — resolved from the curated ISO-4217 table (RegionInfo scan
        // for the long tail; see `currencySymbol`). The fraction-
        // digit count still follows the locale, not the currency (so e.g. JPY
        // keeps the locale's 2 digits rather than CLDR's 0) — a documented
        // approximation; the browser Intl path is exact.
        let nfi = c.NumberFormat.Clone() :?> NumberFormatInfo
        nfi.CurrencySymbol <- currencySymbol isoCode
        value.ToString("C", nfi)
    | Format.Percent decimals ->
        let suffix =
            match decimals with
            | Some d -> string d
            | None -> ""

        value.ToString("P" + suffix, c)
    | Format.Date dateStyle ->
        let dt = DateTimeOffset.FromUnixTimeSeconds(int64 value).UtcDateTime

        let pattern =
            match dateStyle with
            | DateStyle.Short -> "d"
            | DateStyle.Medium
            | DateStyle.Long -> "D"
            | DateStyle.Full -> "F"

        dt.ToString(pattern, c)
    | Format.RelativeTime unit ->
        // English-only fallback (no CLDR relative-time data on .NET).
        let n = int (Math.Round value)
        let unitWord = relativeUnitStr unit

        if n = 0 then
            sprintf "this %s" unitWord
        else
            let magnitude = abs n
            let plural = if magnitude = 1 then unitWord else unitWord + "s"

            if n < 0 then
                sprintf "%d %s ago" magnitude plural
            else
                sprintf "in %d %s" magnitude plural

#endif
