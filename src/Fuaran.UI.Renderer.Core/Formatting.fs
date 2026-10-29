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
//     code's symbol resolved from a curated ISO-4217 table (the code itself
//     past it) — but does NOT match `Intl`
//     byte-for-byte (currency fraction digits follow the locale not the
//     currency, so JPY keeps 2 digits; relative-time is English-only;
//     date-style mapping is coarse). The fallback exists so
//     diagnostics + SSR produce a sensible string under the pure-.NET runner;
//     the browser pipeline is the visual source of truth.
//
//  `localeTag` is the resolved BCP-47 tag (e.g. "en-GB"). An empty string
//  means "use the runtime default locale" — `Intl` with `undefined`, .NET with
//  `CultureInfo.InvariantCulture` (the identity-default per the 12.I locale
//  source). The numeric source is interpreted per the `Format` case: `DateTime`
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

// ─── Instants a `Format.DateTime` cannot represent ─────────────────────────────
//
// The wire admits every IEEE-754 double a JSON number can spell, including
// `NaN`, `±Infinity` and values far outside any calendar (WIRE_FORMAT §7's
// float sentinels; the decoder's own sentinel set at `JsonDecode`). A
// `Format.DateTime` slot then receives one, and the two hosts failed differently:
// `DateTimeOffset.FromUnixTimeSeconds` THROWS, out of an SSR render pass, so the
// whole page 500s on one bad cell; `Intl.DateTimeFormat` raises a RangeError on
// an invalid time value, so the browser loses the render for that node.
//
// Neither of those is a rendering. The guard sits ABOVE both legs and answers
// the same string on each, so a page rendered on the server and the same page
// hydrated in the browser agree about what an unrepresentable instant looks
// like — which is the property the whole two-pipeline formatter exists to keep.
//
// THE BOUNDS ARE .NET'S, deliberately, and they are the NARROWER pair.
// `DateTimeOffset` spans year 1 to year 9999 (±~2.5×10¹¹ seconds); JavaScript's
// `DateTime` spans ±~8.6×10¹². Taking the wider range would let a value render on
// one host and refuse on the other, which is the divergence being closed — so
// the narrower range is the shared one, and a value between the two bounds is
// refused on both even though one host could have drawn it.

/// The lower bound of `DateTimeOffset` as Unix seconds (0001-01-01T00:00:00Z).
[<Literal>]
let minInstantSeconds = -62135596800.0

/// The upper bound of `DateTimeOffset` as Unix seconds (9999-12-31T23:59:59Z).
[<Literal>]
let maxInstantSeconds = 253402300799.0

/// What a `Format.DateTime` slot renders for a value that is not an instant.
///
/// The `(error: …)` shape is the renderer's own error rendition — the same one
/// `BindingResolver.Errored` reaches the DOM as — so an unrepresentable instant
/// reads to a reader exactly like every other resolution failure rather than
/// like a date. Deliberately NOT an empty string: a blank cell is what an ABSENT
/// value renders as, and a wrong value is not an absent one.
[<Literal>]
let unrepresentableInstant = "(error: not a representable instant)"

/// Can this float be read as an instant at all? `NaN` fails every comparison,
/// so the range test rejects it without a special case; the infinities are
/// outside the bounds by construction.
let isRepresentableInstant (unixSeconds: float) : bool =
    unixSeconds >= minInstantSeconds && unixSeconds <= maxInstantSeconds

let private dateStyleStr (s: DateStyle) : string =
    match s with
    | DateStyle.Short -> "short"
    | DateStyle.Medium -> "medium"
    | DateStyle.Long -> "long"
    | DateStyle.Full -> "full"

/// Phase 1810 — the time-of-day half of the style pair, `Intl.DateTimeFormat`'s
/// own `timeStyle` spellings.
let private timeStyleStr (s: TimeStyle) : string =
    match s with
    | TimeStyle.Short -> "short"
    | TimeStyle.Medium -> "medium"
    | TimeStyle.Long -> "long"
    | TimeStyle.Full -> "full"

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
/// Unix-epoch seconds — the representation `Format.DateTime` and `Format.Since`
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
                let c = instant[i]

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

/// ISO-4217 code → symbol for the common currencies, the ONE table both the
/// locale-aware .NET `format` fallback below and the chart value axis read
/// (Phase 1941; each held a copy, and their fallbacks disagreed). Pinned rather
/// than read from `RegionInfo`, which is platform- and enumeration-order-
/// dependent: `RegionInfo.CurrencySymbol` is the glyph under Windows NLS and the
/// bare ISO code for some cultures under Linux/ICU, so EUR rendered "€" on one
/// server and "EUR" on another.
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

/// The symbol of an ISO-4217 code: the table's glyph, else the code itself —
/// deterministic on every platform, never a wrong symbol, and what `Intl` itself
/// writes for a code it has no symbol for.
let currencySymbol (isoCode: string) : string =
    match Map.tryFind isoCode knownCurrencySymbols with
    | Some sym -> sym
    | None -> isoCode

// ─── JavaScript number text (Phase 1941) ────────────────────────────────────
//
// The `CellFormat` projection is the text the HYDRATING bundle produces, so the
// three JavaScript number spellings it is written in are the contract, and both
// pipelines produce them exactly:
//
//   - `jsNumberText`  — `String(x)`: the shortest round-trip digits, positional
//     from 1e-6 up to (not including) 1e21 and exponential (`1e+21`, `1.5e-7`)
//     outside that, `-0` spelled `0`, `NaN` / `Infinity` / `-Infinity`;
//   - `jsToFixed`     — `x.toFixed(d)`: the exact binary value rounded to `d`
//     places with a tie going to the larger magnitude, `String(x)` from 1e21 up;
//   - `jsSignificant` — `Number(x.toPrecision(d)).toString()`: `d` significant
//     digits by the same rounding, then spelled by `String(x)` — so `1234` at 3
//     is `1230` and never `1.23E+03`.
//
// Fable emits the JavaScript itself. The .NET pipeline cannot lean on the BCL:
// every `ToString` overload that takes no provider reads the CURRENT culture (a
// de-DE host wrote `1,50` where the bundle hydrated `1.50`), `"G<n>"` and `%g`
// switch to exponent form far below 1e21, and `"F<n>"` / `"E<n>"` round an exact
// tie to EVEN where JavaScript rounds it away from zero (`(0.125).toFixed(2)` is
// `0.13`; .NET's `"F2"` gives `0.12`). So it rounds the double's exact value as
// a ratio of integers, and takes only the shortest round-trip digits (`"R"`,
// invariant) from the BCL, which agree with `String(x)` digit for digit.
//
// Precision is clamped to JavaScript's own domain (`toFixed` 0–100,
// `toPrecision` 1–100), where JavaScript throws a RangeError instead; no wire
// value outside it is pinned by the corpus.

let private fixedDigits (digits: int) : int = max 0 (min 100 digits)

let private precisionDigits (digits: int) : int = max 1 (min 100 digits)

#if FABLE_COMPILER

[<Fable.Core.Emit("String($0)")>]
let private jsNumberText (value: float) : string = Fable.Core.Util.jsNative

[<Fable.Core.Emit("$1.toFixed($0)")>]
let private jsToFixed (digits: int) (value: float) : string = Fable.Core.Util.jsNative

[<Fable.Core.Emit("Number($1.toPrecision($0)).toString()")>]
let private jsSignificant (digits: int) (value: float) : string = Fable.Core.Util.jsNative

#else

let private invariant = System.Globalization.CultureInfo.InvariantCulture

let private jsNumberText (value: float) : string =
    if System.Double.IsNaN value then
        "NaN"
    elif System.Double.IsPositiveInfinity value then
        "Infinity"
    elif System.Double.IsNegativeInfinity value then
        "-Infinity"
    elif value = 0.0 then
        "0"
    else
        // The shortest round-trip digits and their decimal exponent, read off
        // the invariant `"R"` spelling (`1234567.5`, `1E+21`, `1.234E-06`).
        let r = value.ToString("R", invariant)
        let negative = r.StartsWith "-"
        let body = if negative then r.Substring 1 else r

        let mantissa, exponent =
            match body.IndexOf 'E' with
            | -1 -> body, 0
            | i -> body.Substring(0, i), System.Int32.Parse(body.Substring(i + 1), invariant)

        let intPart, fracPart =
            match mantissa.IndexOf '.' with
            | -1 -> mantissa, ""
            | i -> mantissa.Substring(0, i), mantissa.Substring(i + 1)

        let all = intPart + fracPart
        let leadingZeros = all.Length - all.TrimStart('0').Length
        let digits = all.TrimStart('0').TrimEnd('0')
        // ECMA-262 Number::toString: the value is 0.<digits> × 10^n.
        let k = digits.Length
        let n = intPart.Length + exponent - leadingZeros

        let text =
            if k <= n && n <= 21 then
                digits + System.String('0', n - k)
            elif 0 < n && n <= 21 then
                digits.Substring(0, n) + "." + digits.Substring n
            elif -6 < n && n <= 0 then
                "0." + System.String('0', -n) + digits
            else
                let e = n - 1

                let lead =
                    if k = 1 then
                        digits
                    else
                        digits.Substring(0, 1) + "." + digits.Substring 1

                lead + "e" + (if e < 0 then "-" else "+") + string (abs e)

        if negative then "-" + text else text

/// |value| as an exact ratio of integers (finite `value`).
let private exactRatio (value: float) : System.Numerics.BigInteger * System.Numerics.BigInteger =
    let bits = System.BitConverter.DoubleToInt64Bits(abs value)
    let biased = int ((bits >>> 52) &&& 0x7FFL)
    let fraction = bits &&& 0xFFFFFFFFFFFFFL

    let mantissa, exp2 =
        if biased = 0 then
            fraction, -1074
        else
            fraction ||| 0x10000000000000L, biased - 1075

    let two = System.Numerics.BigInteger 2

    if exp2 >= 0 then
        System.Numerics.BigInteger mantissa * System.Numerics.BigInteger.Pow(two, exp2), System.Numerics.BigInteger.One
    else
        System.Numerics.BigInteger mantissa, System.Numerics.BigInteger.Pow(two, -exp2)

/// `num / den` rounded to an integer, a tie going up (both non-negative).
let private roundHalfUp (num: System.Numerics.BigInteger) (den: System.Numerics.BigInteger) =
    let q = System.Numerics.BigInteger.Divide(num, den)

    if (num - q * den) * System.Numerics.BigInteger 2 >= den then
        q + System.Numerics.BigInteger.One
    else
        q

let private pow10 (n: int) =
    System.Numerics.BigInteger.Pow(System.Numerics.BigInteger 10, n)

let private jsToFixed (digits: int) (value: float) : string =
    if System.Double.IsNaN value || System.Double.IsInfinity value || abs value >= 1e21 then
        jsNumberText value
    else
        let num, den = exactRatio value
        let scaled = (roundHalfUp (num * pow10 digits) den).ToString(invariant)

        let body =
            if digits = 0 then
                scaled
            else
                let padded = scaled.PadLeft(digits + 1, '0')

                padded.Substring(0, padded.Length - digits)
                + "."
                + padded.Substring(padded.Length - digits)

        // `toFixed` signs a negative operand whatever it rounds to (-0.001 →
        // "-0.00"), and never signs -0, which is not less than zero.
        if value < 0.0 then "-" + body else body

let private jsSignificant (digits: int) (value: float) : string =
    if System.Double.IsNaN value || System.Double.IsInfinity value || value = 0.0 then
        jsNumberText value
    else
        let num, den = exactRatio value

        // e = floor(log10 |value|), exactly: the float estimate, corrected
        // against the exact ratio.
        let atLeast (k: int) =
            if k >= 0 then
                num >= den * pow10 k
            else
                num * pow10 -k >= den

        let mutable e = int (floor (log10 (abs value)))

        while not (atLeast e) do
            e <- e - 1

        while atLeast (e + 1) do
            e <- e + 1

        let shift = e - digits + 1

        let n =
            if shift >= 0 then
                roundHalfUp num (den * pow10 shift)
            else
                roundHalfUp (num * pow10 -shift) den

        let rounded =
            System.Double.Parse(
                n.ToString(invariant) + "E" + string shift,
                System.Globalization.NumberStyles.Float,
                invariant
            )

        jsNumberText (if value < 0.0 then -rounded else rounded)

#endif

/// The LOCALE-FREE `CellFormat` projection of a number: what every arm renders
/// when the figure carries no `locale` (Phase 2490 — `formatNumberIn` below routes
/// a present one). Culture-invariant, and byte-identical to the hydrating
/// bundle's on every arm. The corpus's `formatting` family pins it on every host.
let private formatFigure (format: CellFormat) (value: float) : string =
    match format with
    | CellFormat.None -> jsNumberText value
    | CellFormat.Number(Some decimals, _) -> jsToFixed (fixedDigits decimals) value
    | CellFormat.Number(None, _) ->
        // `Number.isInteger(x) ? x.toFixed(0) : String(x)`.
        if not (System.Double.IsInfinity value) && value = floor value then
            jsToFixed 0 value
        else
            jsNumberText value
    // Phase 2499 — a present `decimals` is the fixed digit count; absent, two.
    | CellFormat.Currency(code, decimals, _) ->
        code
        + " "
        + jsToFixed (decimals |> Option.map fixedDigits |> Option.defaultValue 2) value
    | CellFormat.Percent(Some decimals, _) -> jsToFixed (fixedDigits decimals) (value * 100.0) + "%"
    | CellFormat.Percent(None, _) -> jsToFixed 1 (value * 100.0) + "%"
    | CellFormat.SignificantDigits digits -> jsSignificant (precisionDigits digits) value
    | CellFormat.DateTime _ -> jsNumberText value
    | CellFormat.Duration(unit, style) -> formatDuration unit style value
    | CellFormat.RelativeTime unit -> formatRelativeEnglish unit value
    | CellFormat.Custom f -> f (CellValue.Numeric value)

// ─── The exact decimal (Phase 2006) ──────────────────────────────────────────
//
// A `CellValue.Decimal` formats from its DIGITS. Every arm below is the decimal
// twin of `formatFigure`'s arm of the same name, spelled so that a decimal and a
// float holding the same value render the same bytes wherever the float is exact,
// and so that no arm rounds through a float: `12345678901234567.895` to two places
// is `12345678901234567.90`, where a double would have said `...68.00`.

/// A decimal as `(negative, digits, scale)`: the number `±digits × 10^-scale`.
/// Read through Core's canonical form, so the grammar is Core's and only Core's.
let private decimalScaled (text: string) : (bool * string * int) option =
    Fuaran.Core.DecimalText.tryCanonical text
    |> Option.map (fun canonical ->
        let negative = canonical.Length > 0 && canonical[0] = '-'
        let body = if negative then canonical.Substring 1 else canonical

        match body.IndexOf '.' with
        | -1 -> negative, body, 0
        | dot -> negative, body.Substring(0, dot) + body.Substring(dot + 1), body.Length - dot - 1)

/// Add one to a non-empty run of ASCII digits (`"0999"` → `"1000"`, `"99"` → `"100"`).
let private incrementDigits (digits: string) : string =
    let chars = digits.ToCharArray()
    let mutable i = chars.Length - 1
    let mutable carry = true

    while carry && i >= 0 do
        if chars[i] = '9' then
            chars[i] <- '0'
            i <- i - 1
        else
            chars[i] <- char (int chars[i] + 1)
            carry <- false

    let body = System.String(chars)
    if carry then "1" + body else body

/// Drop the last `cut` digits, rounding half up on the magnitude (the rule
/// `jsToFixed` applies to a float's exact value).
let private roundOff (digits: string) (cut: int) : string =
    if cut <= 0 then
        digits
    else
        let padded = digits.PadLeft(cut + 1, '0')
        let keep = padded.Substring(0, padded.Length - cut)

        if padded[padded.Length - cut] >= '5' then
            incrementDigits keep
        else
            keep

/// `toFixed(places)` over a scaled decimal, sign included whatever it rounds to
/// (`jsToFixed`'s posture: `-0.001` → `-0.00`).
let private decimalToFixed (places: int) (negative: bool, digits: string, scale: int) : string =
    let kept =
        if scale <= places then
            digits + System.String('0', places - scale)
        else
            roundOff digits (scale - places)

    let padded = kept.PadLeft(places + 1, '0')
    let whole = padded.Substring(0, padded.Length - places).TrimStart('0')
    let whole = if whole = "" then "0" else whole

    let body =
        if places = 0 then
            whole
        else
            whole + "." + padded.Substring(padded.Length - places)

    if negative then "-" + body else body

/// Back to canonical decimal text, from a scale that may be negative.
let private decimalCanonicalOf (negative: bool, digits: string, scale: int) : string =
    let digits, scale =
        if scale < 0 then
            digits + System.String('0', -scale), 0
        else
            digits, scale

    let padded = digits.PadLeft(scale + 1, '0')

    let text =
        (if negative then "-" else "")
        + (if scale = 0 then
               padded
           else
               padded.Substring(0, padded.Length - scale)
               + "."
               + padded.Substring(padded.Length - scale))

    Fuaran.Core.DecimalText.tryCanonical text |> Option.defaultValue text

/// `toPrecision(digits)` rendered plainly: round half up at `digits` significant
/// digits and give the canonical text, which never takes an exponent.
let private decimalSignificant (precision: int) (negative: bool, digits: string, scale: int) : string =
    let significant = digits.TrimStart('0')

    if significant = "" then
        "0"
    else
        let cut = significant.Length - precision

        if cut <= 0 then
            decimalCanonicalOf (negative, digits, scale)
        else
            decimalCanonicalOf (negative, roundOff significant cut, scale - cut)

/// The LOCALE-FREE `CellFormat` projection of an exact decimal (Phase 2006) —
/// `formatFigure`'s twin, from the canonical text and never through a float. `Number`, `Currency`,
/// `Percent` and `SignificantDigits` round half up at the declared digit; `None`,
/// `Number None` and `DateTime` are the canonical text itself. `Duration` and
/// `RelativeTime` render the canonical text too: each reads its value as a count of
/// time units through float arithmetic, and a decimal does not become a float here.
let private formatDecimalFigure (format: CellFormat) (text: string) : string =
    match decimalScaled text with
    | None -> text
    | Some scaled ->
        let negative, digits, scale = scaled

        match format with
        | CellFormat.Number(Some decimals, _) -> decimalToFixed (fixedDigits decimals) scaled
        | CellFormat.Currency(code, decimals, _) ->
            code
            + " "
            + decimalToFixed (decimals |> Option.map fixedDigits |> Option.defaultValue 2) scaled
        | CellFormat.Percent(Some decimals, _) ->
            decimalToFixed (fixedDigits decimals) (negative, digits, scale - 2) + "%"
        | CellFormat.Percent(None, _) -> decimalToFixed 1 (negative, digits, scale - 2) + "%"
        | CellFormat.SignificantDigits d -> decimalSignificant (precisionDigits d) scaled
        | CellFormat.Custom f -> f (CellValue.Decimal text)
        | CellFormat.None
        | CellFormat.Number(None, _)
        | CellFormat.DateTime _
        | CellFormat.Duration _
        | CellFormat.RelativeTime _ -> decimalCanonicalOf scaled

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
    | Format.Currency(isoCode, None) -> createObj [ "style" ==> "currency"; "currency" ==> isoCode ]
    // Phase 2499 — a present `decimals` overrides the currency's own digits.
    | Format.Currency(isoCode, Some d) ->
        createObj
            [ "style" ==> "currency"
              "currency" ==> isoCode
              "minimumFractionDigits" ==> d
              "maximumFractionDigits" ==> d ]
    | Format.Percent(Some d) ->
        createObj
            [ "style" ==> "percent"
              "minimumFractionDigits" ==> d
              "maximumFractionDigits" ==> d ]
    | Format.Percent None -> createObj [ "style" ==> "percent" ]
    | Format.DateTime _
    | Format.RelativeTime _
    | Format.Since _
    | Format.Duration _ -> createObj []

/// Format `value` per the bounded `Format` intent + resolved `localeTag`.
let format (localeTag: string) (fmt: Format) (value: float) : string =
    match fmt with
    | Format.Number _
    | Format.Currency _
    | Format.Percent _ -> intlNumber (localeArg localeTag) (numberOptions fmt) value
    | Format.DateTime(dateStyle, timeStyle) ->
        if not (isRepresentableInstant value) then
            unrepresentableInstant
        else
            // Phase 1810 — the platform's own `dateStyle` / `timeStyle` pair:
            // each option rides only when declared, so `timeStyle` alone is a
            // time of day and both together a date-time.
            let options =
                createObj
                    [ match dateStyle with
                      | Some d -> yield "dateStyle" ==> dateStyleStr d
                      | None -> ()
                      match timeStyle with
                      | Some t -> yield "timeStyle" ==> timeStyleStr t
                      | None -> () ]

            intlDate (localeArg localeTag) options value
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

/// Format `value` per the bounded `Format` intent + resolved `localeTag`.
/// Documented .NET fallback — see the module header for the parity caveats.
let format (localeTag: string) (fmt: Format) (value: float) : string =
    let c = culture localeTag

    match fmt with
    | Format.Number decimals -> value.ToString(numberPattern decimals, c)
    | Format.Currency(isoCode, decimals) ->
        // Format with the locale's currency pattern (symbol placement,
        // grouping, decimal digits) but substitute the requested currency's
        // symbol — resolved from the curated ISO-4217 table, the code itself
        // past it (see `currencySymbol`). The fraction-
        // digit count still follows the locale, not the currency (so e.g. JPY
        // keeps the locale's 2 digits rather than CLDR's 0) — a documented
        // approximation; the browser Intl path is exact.
        let nfi = c.NumberFormat.Clone() :?> NumberFormatInfo
        nfi.CurrencySymbol <- currencySymbol isoCode
        // Phase 2499 — a present `decimals` overrides the locale's digits.
        let precision =
            match decimals with
            | Some d -> string (max 0 d)
            | None -> ""

        value.ToString("C" + precision, nfi)
    | Format.Percent decimals ->
        let suffix =
            match decimals with
            | Some d -> string d
            | None -> ""

        value.ToString("P" + suffix, c)
    | Format.DateTime(dateStyle, timeStyle) ->
        if not (isRepresentableInstant value) then
            unrepresentableInstant
        else
            // `int64 value` on a float outside Int64's range is itself
            // undefined behaviour before `FromUnixTimeSeconds` is ever reached,
            // so the guard has to precede the conversion rather than wrap it.
            let dt = DateTimeOffset.FromUnixTimeSeconds(int64 value).UtcDateTime

            // Phase 1810 — the .NET standard patterns stand in for the
            // platform's `dateStyle` / `timeStyle` pair. Date-only keeps its
            // pre-1810 renditions byte-for-byte; a time of day is the culture's
            // short (`t`) or long (`T`) time pattern; a date-time is the two
            // joined by a space, with the date half at its long form rather
            // than `F` (which already carries a long time of its own).
            let datePattern (withTime: bool) =
                match dateStyle with
                | Some DateStyle.Short -> Some "d"
                | Some DateStyle.Medium
                | Some DateStyle.Long -> Some "D"
                | Some DateStyle.Full -> Some(if withTime then "D" else "F")
                | None -> None

            let timePattern =
                match timeStyle with
                | Some TimeStyle.Short -> Some "t"
                | Some TimeStyle.Medium
                | Some TimeStyle.Long
                | Some TimeStyle.Full -> Some "T"
                | None -> None

            match datePattern timePattern.IsSome, timePattern with
            | Some d, Some t -> dt.ToString(d, c) + " " + dt.ToString(t, c)
            | Some d, None -> dt.ToString(d, c)
            | None, Some t -> dt.ToString(t, c)
            // Neither declared — FUARAN155 refuses this shape upstream; the
            // fallback here is the culture's general pattern rather than a
            // throw, on the same never-throw law the instant guard serves.
            | None, None -> dt.ToString("g", c)
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

// ─── A figure's locale (Phase 2490) ─────────────────────────────────────────
//
// `CellFormat`'s three figure cases (`Number`, `Currency`, `Percent`) carry an
// optional `locale`. Absent, the figure is `formatFigure` / `formatDecimalFigure`
// above, byte-for-byte what it was before the member existed. Present, it renders
// through `format` — the SAME function `Binding.Format` renders through, for the
// matching `Format` case — so it takes that route's cross-pipeline caveat (the
// header above) and no new one: `Intl` in the browser, the documented
// `System.Globalization` fallback on .NET.
//
// `Ambient` is the render's ambient locale, which the caller supplies as it does
// for `Binding.Format` (`BindingSources.Locale`); the argument-free entry points
// pass "" — the runtime default, `format`'s own reading of an empty tag.

/// The resolved tag and `Format` case a figure's present `locale` routes to;
/// `None` for a figure without one, and for every other case.
let private figureLocale (ambientLocale: string) (cellFormat: CellFormat) : (string * Format) option =
    let tagOf =
        function
        | LocaleSource.Explicit tag -> tag
        | LocaleSource.Ambient -> ambientLocale

    match cellFormat with
    | CellFormat.Number(decimals, Some locale) -> Some(tagOf locale, Format.Number decimals)
    | CellFormat.Currency(code, decimals, Some locale) -> Some(tagOf locale, Format.Currency(code, decimals))
    | CellFormat.Percent(decimals, Some locale) -> Some(tagOf locale, Format.Percent decimals)
    | _ -> None

/// The `CellFormat` projection of a number — a Metric / LabelValueRow value, a
/// Metric trend, a grid cell. ONE definition (Phase 2108 collapsed the client
/// and server copies onto it; Phase 1941 the AG Grid adapter's). Without a
/// `locale` it is culture-invariant and byte-identical to the hydrating bundle's
/// on every arm; with one it is `format` under the resolved tag (Phase 2490).
let formatNumberIn (ambientLocale: string) (cellFormat: CellFormat) (value: float) : string =
    match figureLocale ambientLocale cellFormat with
    | Some(tag, fmt) -> format tag fmt value
    | None -> formatFigure cellFormat value

/// `formatNumberIn` where no ambient locale is in scope (an `Ambient` figure
/// takes the runtime default).
let formatNumber (cellFormat: CellFormat) (value: float) : string = formatNumberIn "" cellFormat value

/// The `CellFormat` projection of an exact decimal (Phase 2006). Without a
/// `locale` it is computed from the digits and never through a float. With one,
/// the locale formatter takes a float (`format`'s one numeric input on both
/// pipelines), so the canonical text converts ONCE, here, at that boundary — the
/// only point a decimal figure is rounded through a double (Phase 2490).
let formatDecimalIn (ambientLocale: string) (cellFormat: CellFormat) (text: string) : string =
    match figureLocale ambientLocale cellFormat, Fuaran.Core.DecimalText.tryCanonical text with
    | Some(tag, fmt), Some canonical ->
#if FABLE_COMPILER
        format tag fmt (float canonical)
#else
        format tag fmt (System.Double.Parse(canonical, System.Globalization.CultureInfo.InvariantCulture))
#endif
    | _ -> formatDecimalFigure cellFormat text

/// `formatDecimalIn` where no ambient locale is in scope.
let formatDecimal (cellFormat: CellFormat) (text: string) : string = formatDecimalIn "" cellFormat text

/// A grid cell's text — `formatNumberIn` for a number (`formatDecimalIn` for an
/// exact decimal), the cell's own spelling otherwise. One definition for the first-party table and the AG Grid
/// adapter (Phase 1941; each held a copy). A date reads its `DateTime` pattern,
/// else ISO `yyyy-MM-dd`, invariantly on the .NET pipeline.
let formatCellValueIn (ambientLocale: string) (format: CellFormat) (value: CellValue) : string =
    match format, value with
    | CellFormat.Custom f, _ -> f value
    | _, CellValue.Numeric n -> formatNumberIn ambientLocale format n
    | _, CellValue.Decimal s -> formatDecimalIn ambientLocale format s
    | _, CellValue.Text s -> s
    | _, CellValue.Bool b -> if b then "true" else "false"
    | _, CellValue.Date d ->
        let pattern =
            match format with
            | CellFormat.DateTime fmt -> fmt
            | _ -> "yyyy-MM-dd"
#if FABLE_COMPILER
        d.ToString pattern
#else
        d.ToString(pattern, System.Globalization.CultureInfo.InvariantCulture)
#endif
    | _, CellValue.Empty -> ""

/// `formatCellValueIn` where no ambient locale is in scope.
let formatCellValue (format: CellFormat) (value: CellValue) : string = formatCellValueIn "" format value
