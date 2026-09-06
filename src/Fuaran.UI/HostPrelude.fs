// The host prelude — the small set of HOST types the IDL's `THosted` slots name
// (Accessibility.role, StateBehaviour.onError's arg), compiled AHEAD
// of `Generated.fs` so the generated code can reference them and their wire codecs.
// `Fuaran.UI.Types` re-exposes each as an alias, so consumers are unaffected.
//
// A byte-identical stub lives in Fuaran-Core's test assembly
// (`tests/Fuaran.Core.Tests/UiHostPrelude.fs`) so the generated snapshot compiles
// there too — keep the two in sync; the corpus pins the wire bytes on both sides.
module Fuaran.UI.HostPrelude

open Fuaran.Core

/// Wire-adjacent error taxonomy for `StateBehaviour.onError` payloads.
type ErrorKind =
    | NotFound
    | Forbidden
    | Server
    | Network
    | Timeout
    /// Client-side: the renderer encountered a `Binding` it could not
    /// resolve (accessor threw, value did not unbox to expected type,
    /// computed binding threw). Distinct from `Server` — the failure
    /// did not cross the wire. Downstream observability filters keyed
    /// on `Server` should NOT fire for these.
    | BindingResolution

/// The payload handed to a `StateBehaviour.OnError` fallback closure.
type ErrorPayload =
    { Kind: ErrorKind
      Message: string
      CorrelationId: string }

/// Opaque handle to a selected file's blob (`Id` is the only wire-visible part;
/// `Handle` carries the boxed browser `File` on browser hosts — a sanctioned
/// host-blob boundary, unboxed by the renderer's `FileReader` arm).
type FileRef = { Id: string; Handle: obj option }

/// Browser file metadata handed to `FileUpload.onSelect` (closure arg — never
/// serialises, so no codec). `Ref` carries the opaque blob handle so `OnSelect`
/// can chain `Action.ReadFileBody` to ingest the body.
type FileSelection =
    { Name: string
      Size: int64
      MimeType: string
      Ref: FileRef }

/// What a single cell of grid data is, after a column's `Value` projection runs
/// (closure interior — never serialises, so no codec). Pre-formatted strings
/// break numeric sort; use `Numeric` + a `CellFormat` instead.
[<RequireQualifiedAccess>]
type CellValue =
    | Numeric of float
    | Text of string
    | Bool of bool
    | Date of System.DateTimeOffset
    | Empty

/// ARIA role — a closed convenience list plus `Custom` verbatim passthrough
/// (the wire position admits any string; canonical cases emit lower-case).
[<RequireQualifiedAccess>]
type AriaRole =
    | Button
    | Link
    | Dialog
    | Alert
    | Status
    | Banner
    | Navigation
    | Main
    | Form
    | Region
    | Heading
    | Progressbar
    | Tab
    | Tablist
    | Tabpanel
    | Custom of role: string

let encAriaRole (r: AriaRole) : JVal =
    JStr(
        match r with
        | AriaRole.Button -> "button"
        | AriaRole.Link -> "link"
        | AriaRole.Dialog -> "dialog"
        | AriaRole.Alert -> "alert"
        | AriaRole.Status -> "status"
        | AriaRole.Banner -> "banner"
        | AriaRole.Navigation -> "navigation"
        | AriaRole.Main -> "main"
        | AriaRole.Form -> "form"
        | AriaRole.Region -> "region"
        | AriaRole.Heading -> "heading"
        | AriaRole.Progressbar -> "progressbar"
        | AriaRole.Tab -> "tab"
        | AriaRole.Tablist -> "tablist"
        | AriaRole.Tabpanel -> "tabpanel"
        | AriaRole.Custom raw -> raw
    )

let decAriaRole (j: JVal) : Result<AriaRole, string> =
    match j with
    | JStr "button" -> Ok AriaRole.Button
    | JStr "link" -> Ok AriaRole.Link
    | JStr "dialog" -> Ok AriaRole.Dialog
    | JStr "alert" -> Ok AriaRole.Alert
    | JStr "status" -> Ok AriaRole.Status
    | JStr "banner" -> Ok AriaRole.Banner
    | JStr "navigation" -> Ok AriaRole.Navigation
    | JStr "main" -> Ok AriaRole.Main
    | JStr "form" -> Ok AriaRole.Form
    | JStr "region" -> Ok AriaRole.Region
    | JStr "heading" -> Ok AriaRole.Heading
    | JStr "progressbar" -> Ok AriaRole.Progressbar
    | JStr "tab" -> Ok AriaRole.Tab
    | JStr "tablist" -> Ok AriaRole.Tablist
    | JStr "tabpanel" -> Ok AriaRole.Tabpanel
    | JStr other -> Ok(AriaRole.Custom other)
    | _ -> Error "expected JSON string for aria role"

// ─── Live Transform-source helpers (Phase 818 — the reactive-derivation
//     first cut). Compiled ahead of `Generated.fs` so the generated decoder's
//     Transform arm (which now preserves a binding-shaped source as
//     `TransformSource.Live`) can derive the SSR/diagnostic initial snapshot
//     from the binding's carried data. Mirrors the host bridge's Phase-815
//     Json-level row-major transpose at the JVal level — same first-row key
//     set, same canonical columnar target — so the decode-time snapshot and a
//     runtime re-evaluation of the same data produce the same table. ─────────

[<RequireQualifiedAccess>]
module TransformLive =

    /// Normalise a live source's carried JVal data toward the canonical
    /// columnar shape `{"columns": {...}}`: ROW-MAJOR data (an array of row
    /// objects) transposes by the first row's key set; canonical columnar (and
    /// anything else) passes through untouched. A ragged row set is NOT
    /// silently patched — the missing cell surfaces as Core's schema-inference
    /// didactic downstream, matching the Phase-815 Json-level behaviour
    /// (`JVal` has no null to fill with, and a quiet wrong column is worse
    /// than a loud teachable error).
    let normaliseData (j: JVal) : JVal =
        match j with
        | JArr(JObj first :: _ as rows) ->
            let keys = first |> List.map fst

            let cols =
                keys
                |> List.map (fun k ->
                    let cells =
                        rows
                        |> List.collect (function
                            | JObj rf ->
                                (match rf |> List.tryFind (fun (rk, _) -> rk = k) with
                                 | Some(_, v) -> [ v ]
                                 | None -> [])
                            | _ -> [])

                    k, JArr cells)

            JObj [ "columns", JObj cols ]
        | _ -> j

    /// The empty embedded table — the initial snapshot of a live source that
    /// carries no data yet (a `Selection` with no default, a `Query`): the
    /// pipeline evaluates over zero rows and the node renders its empty state.
    let emptySource: DataSource = Embedded { Schema = []; Columns = [] }

    /// Decode a live source's carried JVal data to the initial snapshot
    /// `DataSource` (normalise, then Core's columnar codec). This is the
    /// decode-time half of the Phase-815 snapshot semantics: SSR / diagnostic
    /// evaluation reads this table, byte-identical to what the 0.20.0 snapshot
    /// unwrap produced for the same input.
    ///
    /// 0.23.1 — an EMPTY array default (`"defaultValue": []`) is the empty
    /// table, exactly as a Query/Selection live source starts: an
    /// initially-empty live collection ("count the requests in an empty log")
    /// is a correct, complete intent with zero rows and no columns to infer —
    /// observed organically (terra, the Tier-D cohort r0 count badge), where
    /// the columnar codec's "expected object, got array" didactic was refusing
    /// a shape with nothing wrong in it.
    let initialSource (data: JVal) : Result<DataSource, ColumnError> =
        match data with
        | JArr [] -> Ok emptySource
        | _ -> ColumnCodec.decodeJson (normaliseData data)

// ─── Hex-colour recognition (Phase 1130 — the `FormFieldKind.Color` control).
//     Compiled ahead of `Generated.fs` so the generated decoder's Color arm can
//     refuse a `Static` literal that `<input type="color">` could never hold,
//     and public so the renderers, the pre-emit validator and the server-driven
//     submission floor read the SAME predicate. Four copies of "is this a hex
//     colour" is exactly how one of them ends up admitting `#FFF`. ───────────

[<RequireQualifiedAccess>]
module HexColor =

    /// `true` for the canonical `#rrggbb` form and nothing else — six hex
    /// digits after a `#`, either case.
    ///
    /// It is deliberately STRICT. `<input type="color">` holds exactly this
    /// form: it cannot carry `#fff`, `rebeccapurple`, `rgb(0 0 0)` or an alpha
    /// channel, and a control that silently narrowed one of those to something
    /// else would be answering a question the author did not ask. Widening is
    /// additive and reversible; a lenient normalisation that guessed wrong is
    /// not.
    ///
    /// Case is accepted and never rewritten — `#FFFFFF` is a hex colour, and
    /// the codec preserves the author's bytes. The browser lower-cases on its
    /// own, at the DOM, where that is its business.
    let isValid (text: string) : bool =
        text.Length = 7
        && text[0] = '#'
        && (let mutable ok = true

            for i in 1..6 do
                let c = text[i]

                if not ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')) then
                    ok <- false

            ok)

    /// The canonical spelling of "no colour chosen" — black, the value
    /// `<input type="color">` itself reports when nothing has been picked. It
    /// is the auto-bind placeholder for a `Color` field (`Defaults`), so decode,
    /// encode and the resolver all name the same literal once.
    let unset: string = "#000000"

// ─── Canonical ISO-8601 DATE recognition (Phase 1491 — the event marker's date
//     address). Promoted here, ahead of `Generated.fs`, so the wire decoder, the
//     pre-emit validator and the chart lowering's temporal calendar read ONE
//     definition of "is this string a date". Before this, only the lowering
//     could answer — and the lowering is TOTAL by design (an unparseable cell
//     reads as the epoch), so a decoder or a validator that wanted to REFUSE one
//     had nowhere to ask. ────────────────────────────────────────────────────

[<RequireQualifiedAccess>]
module IsoDate =

    /// Gregorian leap year (proleptic — the rule applies to every year admitted
    /// below, with no historical exception).
    let isLeapYear (y: int) : bool =
        (y % 4 = 0 && y % 100 <> 0) || y % 400 = 0

    /// Days in a month — the one place the calendar's irregularity is written
    /// down, so `2026-02-30` is refused for the calendar reason rather than
    /// admitted on shape alone.
    let daysInMonth (y: int) (m: int) : int =
        if m = 2 then (if isLeapYear y then 29 else 28)
        elif m = 4 || m = 6 || m = 9 || m = 11 then 30
        else 31

    /// `(year, month, day)` for a canonical ISO-8601 date — `YYYY-MM-DD`,
    /// optionally followed by `T…`, whose time-of-day is DISCARDED. `None` for
    /// everything else.
    ///
    /// STRICT by shape AND by calendar: four digits, two, two, both hyphens, a
    /// month in 1–12 and a day the month actually has. A locale spelling
    /// (`15/01/2026`) and a bare year are both refused — admitting either would
    /// be the string-sniffing the temporal axis exists to avoid, and a date
    /// address that quietly read as something else would move a whole axis.
    let tryParts (text: string) : (int * int * int) option =
        let digits (start: int) (len: int) : int option =
            let mutable ok = start + len <= text.Length
            let mutable acc = 0

            if ok then
                for k in start .. start + len - 1 do
                    let c = text[k]

                    if c >= '0' && c <= '9' then
                        acc <- acc * 10 + (int c - int '0')
                    else
                        ok <- false

            if ok then Some acc else None

        if text.Length < 10 then
            None
        elif text[4] <> '-' || text[7] <> '-' then
            None
        elif text.Length > 10 && text[10] <> 'T' then
            None
        else
            match digits 0 4, digits 5 2, digits 8 2 with
            | Some y, Some m, Some d when m >= 1 && m <= 12 && d >= 1 && d <= daysInMonth y m -> Some(y, m, d)
            | _ -> None

    /// `true` when `text` is a canonical ISO-8601 date the temporal axis can
    /// place. The predicate half of `tryParts`, for the callers that only refuse.
    let isValid (text: string) : bool = (tryParts text).IsSome

// ─── Row-field projection (promoted by Phase 1491). The row-key / band-label
//     floor: what STRING a named field of a `Row` presents as. It lived in the
//     renderer's binding resolver, which is downstream of the pre-emit
//     validator — so a validator rule that has to ground an authored key against
//     the labels the lowering will draw could not reach it. Promoted rather than
//     re-derived, on `MediaCapture`'s reasoning: two definitions of "what does
//     this cell read as" is exactly how a grounding rule ends up refusing a key
//     the picture goes on to draw. The resolver now delegates here. ───────────

[<RequireQualifiedAccess>]
module RowProjection =

    /// Classify a boxed row cell. An unrecognised type is `Empty` rather than a
    /// failure: the projection is TOTAL, and the language's grounding rules —
    /// not this function — are what make a wrong column loud.
    ///
    /// `int` / `int64` / `float` all collapse to `Numeric`
    /// (cross-host-deterministic — JS erases the int/float distinction).
    /// A null cell is `Empty`, and it is spelled with `ReferenceEquals` rather
    /// than a `| null ->` arm: this file compiles under F# 10 nullness, where
    /// `obj` does not admit `null` (FS3261), and the alternative is a file-wide
    /// `#nowarn` on a SHIPPED source file to state something one call can say.
    let ofObj (v: obj) : CellValue =
        if System.Object.ReferenceEquals(v, null) then
            CellValue.Empty
        else

            match v with
            | :? string as s -> CellValue.Text s
            | :? bool as b -> CellValue.Bool b
            | :? float as f -> CellValue.Numeric f
            | :? int as i -> CellValue.Numeric(float i)
            | :? int64 as i -> CellValue.Numeric(float i)
            | :? System.DateTimeOffset as d -> CellValue.Date d
            | _ -> CellValue.Empty

    /// Project a named field off a `Row` to a `CellValue`; a missing key is
    /// `CellValue.Empty`.
    let value (row: Row) (field: string) : CellValue =
        match Map.tryFind field row with
        | Some v -> ofObj v
        | None -> CellValue.Empty

    /// Project a named field off a `Row` to a string. Empty string when the
    /// field is missing (the caller may fall back to the row index).
    let string_ (row: Row) (field: string) : string =
        match value row field with
        | CellValue.Text s -> s
        | CellValue.Numeric f -> string f
        | CellValue.Bool b -> (if b then "true" else "false")
        | CellValue.Date d -> d.ToString("o")
        | CellValue.Empty -> ""
