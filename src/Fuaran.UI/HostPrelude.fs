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
    let initialSource (data: JVal) : Result<DataSource, ColumnError> =
        ColumnCodec.decodeJson (normaliseData data)
