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

// ─── Script direction (Phase 1114) ──────────────────────────────────────────
//
// ONE shared implementation for BOTH pipelines, defined above the `#if` for the
// same reason `formatDuration` is: a document's `dir` must be the same string
// under SSR and under the browser, or the server's markup and the client's
// hydration disagree about which way the page runs. Neither runtime can supply
// it — `Intl.Locale.prototype.getTextInfo` is not universally available and
// .NET's `CultureInfo.TextInfo.IsRightToLeft` is present but would give the two
// arms two different oracles, which is precisely the parity hazard — so the
// answer is derived from the BCP-47 tag itself, deterministically.
//
// Deliberately a TAG-DERIVED answer and not a locale-database one: the set of
// right-to-left scripts is small, closed in practice, and changes on the
// timescale of Unicode script additions rather than of CLDR releases.

/// The RTL SCRIPT subtags (BCP-47 second position, ISO 15924). Consulted first,
/// because an explicit script overrides the language's default — `az-Arab` is
/// right-to-left where bare `az` is not, and `ku-Latn` is left-to-right where
/// bare `ku` is not.
let private rtlScripts =
    set
        [ "adlm"
          "arab"
          "aran"
          "hebr"
          "mand"
          "nkoo"
          "rohg"
          "samr"
          "syrc"
          "thaa"
          "yezi" ]

/// The languages whose DEFAULT script is right-to-left, for a tag that names no
/// script: Arabic, Aramaic, Central Kurdish, Dhivehi, Persian, Hebrew,
/// Kashmiri, Mazanderani, N'Ko, Pashto, Sindhi, Syriac, Uyghur, Urdu, Yiddish.
///
/// DEFAULTS, not "languages ever written right-to-left". `pa` (Punjabi) and
/// `ku` (Kurdish) are absent deliberately: their default scripts are Gurmukhi
/// and Latin, and it is `pa-Arab` / `ckb` that run right-to-left — which the
/// script check above already resolves. Adding them here would mirror the
/// majority of their speakers' pages backwards.
let private rtlLanguages =
    set
        [ "ar"
          "arc"
          "ckb"
          "dv"
          "fa"
          "he"
          "iw"
          "ji"
          "ks"
          "mzn"
          "nqo"
          "ps"
          "sd"
          "syr"
          "ug"
          "ur"
          "yi" ]

/// The writing direction a BCP-47 tag implies — `"rtl"` or `"ltr"`. An empty,
/// malformed or unknown tag is `"ltr"`: the value is emitted as a document
/// attribute, so guessing wrong in the majority direction is the recoverable
/// error and refusing to answer is not an option the attribute has.
///
/// Two of the language entries are legacy ISO-639 codes (`iw` for Hebrew, `ji`
/// for Yiddish) that a host's own configuration may still carry; they resolve
/// the same as their modern forms rather than silently reading as left-to-right.
let textDirection (localeTag: string) : string =
    if System.String.IsNullOrWhiteSpace localeTag then
        "ltr"
    else
        let parts = localeTag.Replace('_', '-').ToLowerInvariant().Split('-')
        let language = parts[0]

        // A script subtag is the four-letter part; `ar-EG` has a region there,
        // `az-Arab-IR` a script. Scan rather than index, so a tag carrying an
        // extended-language subtag (`zh-cmn-Hans`) is still read correctly.
        let script = parts |> Array.tryFind (fun p -> p.Length = 4)

        match script with
        | Some s when rtlScripts.Contains s -> "rtl"
        | Some _ -> "ltr"
        | None -> if rtlLanguages.Contains language then "rtl" else "ltr"

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

// ─── The host instant: grain truncation + epoch conversion (Phase 1533) ─────
//
// ONE shared implementation for BOTH pipelines, above the `#if`, for the same
// reason `formatDuration` and the `dir` derivation are: the instant a server
// renders with and the instant its client hydrates with must project to the
// same string by the same route, or SSR and hydration disagree about what
// "now" was. Neither runtime may be consulted — `DateTimeOffset.Parse` and
// `Date.parse` are two oracles for one question, which is precisely the parity
// hazard — so both halves are arithmetic over the canonical form's own digits.
//
// NO CLOCK IS READ HERE OR ANYWHERE BELOW. Every function in this section is a
// pure projection of a string the HOST furnished; the tree names "now" and
// never reads one.

/// The canonical host instant is `YYYY-MM-DDTHH:MM:SS[.fff]Z` (`BindingSources.Now`).
/// Truncate it to `grain` by PREFIX, zero-filling the finer components so the
/// result stays a well-formed instant — except `Day`, which yields the bare
/// `YYYY-MM-DD` that `Fuaran.Core`'s `DateDiffDays` reads.
///
/// `Second` is the identity, deliberately: it is the default grain, so a
/// document that declares no grain resolves through exactly the bytes Phase 765
/// shipped, including any sub-second precision a host chooses to furnish.
///
/// An instant too short to slice is returned VERBATIM rather than padded or
/// refused: this is a host-furnished value, not wire data, and a renderer is the
/// wrong place to adjudicate a host's clock format. The corpus pins the
/// canonical form; a host that furnishes something else gets no truncation and
/// a visibly odd date rather than a silently plausible wrong one.
let truncateToGrain (grain: TimeGrain) (instant: string) : string =
    let sliceOr (n: int) (suffix: string) =
        if instant.Length >= n then
            instant.Substring(0, n) + suffix
        else
            instant

    match grain with
    | TimeGrain.Second -> instant
    | TimeGrain.Minute -> sliceOr 16 ":00Z"
    | TimeGrain.Hour -> sliceOr 13 ":00:00Z"
    | TimeGrain.Day -> sliceOr 10 ""

/// Days since 1970-01-01 for a proleptic-Gregorian civil date — Howard
/// Hinnant's `days_from_civil`, transcribed. Integer arithmetic only, so .NET
/// and Fable compute it identically (F#'s `/` on `int` truncates, and so does
/// the JS emission Fable produces for it).
let private daysFromCivil (y: int) (m: int) (d: int) : int =
    let y = if m <= 2 then y - 1 else y
    let era = (if y >= 0 then y else y - 399) / 400
    let yoe = y - era * 400
    let doy = (153 * (m + (if m > 2 then -3 else 9)) + 2) / 5 + d - 1
    let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy
    era * 146097 + doe - 719468

/// Parse a canonical instant (`YYYY-MM-DD`, optionally `THH:MM:SS…`) to whole
/// Unix-epoch seconds — the representation `Format.Date` and `Format.Since`
/// read their numeric source in. `None` when the leading date is not readable,
/// which the caller surfaces as unresolved rather than as an invented instant.
///
/// Deliberately tolerant of what follows the seconds (a fractional part, a `Z`,
/// an offset suffix) and deliberately INTOLERANT of a missing or non-numeric
/// date: truncating to a grain leaves `YYYY-MM-DDTHH:MM:00Z` and `YYYY-MM-DD`,
/// both of which must parse, and anything shorter is not an instant at all.
let epochSecondsOfInstant (instant: string) : float option =
    let digits (from: int) (len: int) : int option =
        if instant.Length < from + len then
            None
        else
            let mutable acc = 0
            let mutable ok = true

            for i in from .. from + len - 1 do
                let c = instant.[i]

                if c >= '0' && c <= '9' then
                    acc <- acc * 10 + (int c - int '0')
                else
                    ok <- false

            if ok then Some acc else None

    match digits 0 4, digits 5 2, digits 8 2 with
    | Some y, Some mo, Some d when y >= 1 && mo >= 1 && mo <= 12 && d >= 1 && d <= 31 ->
        let hh = digits 11 2 |> Option.defaultValue 0
        let mi = digits 14 2 |> Option.defaultValue 0
        let ss = digits 17 2 |> Option.defaultValue 0

        Some(float (daysFromCivil y mo d) * 86400.0 + float (hh * 3600 + mi * 60 + ss))
    | _ -> None

/// Seconds in one `RelativeTimeUnit`. `Month` and `Year` are the mean Gregorian
/// lengths (365.2425 days / 12 and 365.2425 days) — FIXED constants rather than
/// calendar arithmetic, because "2 months ago" is a rounded human phrase and a
/// calendar-exact answer would make the same delta read differently depending on
/// which months it spanned, on five hosts that must agree to the byte.
let private relativeUnitSeconds (u: RelativeTimeUnit) : float =
    match u with
    | RelativeTimeUnit.Second -> 1.0
    | RelativeTimeUnit.Minute -> 60.0
    | RelativeTimeUnit.Hour -> 3600.0
    | RelativeTimeUnit.Day -> 86400.0
    | RelativeTimeUnit.Week -> 604800.0
    | RelativeTimeUnit.Month -> 2629746.0
    | RelativeTimeUnit.Year -> 31556952.0

/// The `Format.Since` reduction: a signed delta in seconds becomes a
/// `(unit, count)` pair the relative-time renderers already know how to say.
///
/// `declared = None` is the AUTO-SELECTION request (not a default): the unit is
/// the largest whose length does not exceed the magnitude, from the fixed
/// threshold ladder in WIRE_FORMAT 4b. The count TRUNCATES toward zero rather
/// than rounding, so 3599 seconds is "59 minutes" and never "1 hour" — the
/// ladder and the count then agree at every boundary, which rounding would break
/// exactly at the point a reader is most likely to check.
let sinceUnitAndCount (declared: RelativeTimeUnit option) (deltaSeconds: float) : RelativeTimeUnit * float =
    let unit =
        match declared with
        | Some u -> u
        | None ->
            let m = abs deltaSeconds

            if m < 60.0 then RelativeTimeUnit.Second
            elif m < 3600.0 then RelativeTimeUnit.Minute
            elif m < 86400.0 then RelativeTimeUnit.Hour
            elif m < 604800.0 then RelativeTimeUnit.Day
            elif m < 2629746.0 then RelativeTimeUnit.Week
            elif m < 31556952.0 then RelativeTimeUnit.Month
            else RelativeTimeUnit.Year

    unit, float (int (deltaSeconds / relativeUnitSeconds unit))

// ─── Duration decomposition (Phase 819) ─────────────────────────────────────
//
// ONE shared hand-rolled implementation for BOTH pipelines, defined above the
// `#if` so Fable and .NET render byte-identically. A duration is deliberately
// LOCALE-INDEPENDENT: "1h 20m" / "1:20:00" / "1 hour 20 minutes" are unit
// glyphs and English words, not CLDR-driven forms — `Intl` has no duration
// formatter at this API surface — so no locale is consulted and SSR ↔ CSR
// parity is exact by construction. The numeric source counts `unit`s
// (Seconds = 1, Minutes = 60, Hours = 3600); negatives render with a leading
// "-" once the rounded magnitude is nonzero.

let private durationUnitSeconds (u: DurationUnit) : float =
    match u with
    | DurationUnit.Seconds -> 1.0
    | DurationUnit.Minutes -> 60.0
    | DurationUnit.Hours -> 3600.0

/// Render `value` (a signed count of `unit`s) per the bounded `DurationStyle`.
let formatDuration (unit: DurationUnit) (style: DurationStyle) (value: float) : string =
    let totalSeconds = value * durationUnitSeconds unit
    let total = int (round (abs totalSeconds))
    let sign = if totalSeconds < 0.0 && total > 0 then "-" else ""
    let hours = total / 3600
    let minutes = (total % 3600) / 60
    let seconds = total % 60

    let body =
        match style with
        | DurationStyle.Compact ->
            // Largest two grains, zero tails omitted: "1h 20m" / "2h" /
            // "5m 30s" / "42s"; zero → "0s".
            if hours >= 1 then
                if minutes > 0 then
                    sprintf "%dh %dm" hours minutes
                else
                    sprintf "%dh" hours
            elif minutes >= 1 then
                if seconds > 0 then
                    sprintf "%dm %ds" minutes seconds
                else
                    sprintf "%dm" minutes
            else
                sprintf "%ds" seconds
        | DurationStyle.Clock ->
            // "h:mm:ss" from one hour up, "m:ss" below it.
            if hours >= 1 then
                sprintf "%d:%02d:%02d" hours minutes seconds
            else
                sprintf "%d:%02d" minutes seconds
        | DurationStyle.Long ->
            // English words, singular/plural, zero components omitted;
            // zero → "0 minutes".
            let part (n: int) (word: string) : string option =
                if n = 0 then None
                elif n = 1 then Some(sprintf "1 %s" word)
                else Some(sprintf "%d %ss" n word)

            let parts =
                [ part hours "hour"; part minutes "minute"; part seconds "second" ]
                |> List.choose id

            match parts with
            | [] -> "0 minutes"
            | ps -> String.concat " " ps

    sign + body

/// English relative-time rendering over a signed count of `unit` — "in 2
/// hours" / "3 minutes ago" / "this minute" (Phase 819). Shared above the
/// `#if`: the `CellFormat.RelativeTime` projection in BOTH renderers uses it
/// (the cell vocabulary has no locale dimension, so the English form IS the
/// canonical cell rendering), and the .NET `Format.RelativeTime` fallback
/// below delegates to it (the browser `Format` path stays
/// `Intl.RelativeTimeFormat`).
let formatRelativeEnglish (unit: RelativeTimeUnit) (value: float) : string =
    let n = int (round value)
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
    | Format.RelativeTime _
    | Format.Since _
    | Format.Duration _ -> createObj []

/// Format `value` per the bounded `Format` intent + resolved `localeTag`.
let format (localeTag: string) (fmt: Format) (value: float) : string =
    match fmt with
    | Format.Number _
    | Format.Currency _
    | Format.Percent _ -> intlNumber (localeArg localeTag) (numberOptions fmt) value
    | Format.Date dateStyle ->
        intlDate (localeArg localeTag) (createObj [ "dateStyle" ==> dateStyleStr dateStyle ]) value
    | Format.RelativeTime unit -> intlRelative (localeArg localeTag) value (relativeUnitStr unit)
    // Phase 1533 — for `Since`, `value` is the signed delta in SECONDS that the
    // binding resolver has ALREADY taken against the host instant. This function
    // stays a pure projection of its arguments in both pipelines; the one place
    // that reads `sources.Now` is the resolver.
    | Format.Since declared ->
        let unit, count = sinceUnitAndCount declared value
        intlRelative (localeArg localeTag) count (relativeUnitStr unit)
    | Format.Duration(unit, style) -> formatDuration unit style value

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
        // English-only fallback (no CLDR relative-time data on .NET) — the
        // shared helper above the #if (Phase 819 hoisted it so the
        // CellFormat.RelativeTime projection shares the exact rendering).
        formatRelativeEnglish unit value
    // Phase 1533 — `value` is the signed delta in SECONDS the binding resolver
    // already took against the host instant; the unit/count reduction is shared
    // above the `#if`, so only the final phrasing differs between the pipelines
    // (English here, `Intl.RelativeTimeFormat` in the browser) — exactly the
    // split `RelativeTime` already has.
    | Format.Since declared ->
        let unit, count = sinceUnitAndCount declared value
        formatRelativeEnglish unit count
    | Format.Duration(unit, style) ->
        // Locale-independent by design (see the shared helper above) — the
        // one Format case with exact .NET ↔ browser parity.
        formatDuration unit style value

#endif
