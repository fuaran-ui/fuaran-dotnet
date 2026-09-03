module Fuaran.UI.Renderer.GridExport

// ============================================================================
//  Fuaran - the grid export affordance: taking a grid away as a file
//  (Phase 1125)
//
//  `DataGridSpec.Exportable` is the ONLY thing on the wire here, and it says
//  one thing: THIS GRID'S ROWS ARE THE READER'S TO TAKE. Which control offers
//  it, what the control is called, what the file is named and how a value is
//  written into a cell - none of that reaches the vocabulary, under the
//  affordance-to-op charter's governing sentence (`docs/VOCABULARY.md`,
//  Appendix A, Interaction / affordance cluster), which decides this case
//  UNAMENDED: the grid both hosts the gesture and consumes its effect, because
//  only the grid holds its resolved rows, its columns, its declared formats
//  and the order the reader has sorted them into.
//
//  -- Why the decisions are PURE functions --------------------------------
//  The same reason `SwitchStage` and `GridTransfer` are. The .NET test runner
//  mounts no DOM, so a serialiser written inline in a click handler could only
//  ever be ASSERTED about in prose - and every claim worth making here is a
//  claim about BYTES. A value containing the delimiter, a value containing the
//  quote character, a value containing a line break, an encoding a spreadsheet
//  will guess wrong: each is a one-line mistake that produces a file which
//  opens, looks plausible, and is wrong from the first offending row onward.
//  Pulled out, each is pinned by a test that can fail.
//
//  -- The two decisions this phase had to make and record ------------------
//
//  FORMATTED, NOT RAW. A cell is exported as the text the reader is looking
//  at - the column's own value projection, rendered through the column's own
//  declared `CellFormat` - and not as the underlying number or timestamp. The
//  export is the reader's copy of the grid IN FRONT OF THEM: it carries that
//  grid's column set, its column order, its sort order and its formats,
//  because those are what the grid uniquely holds and what nothing outside it
//  can reconstruct. A raw dump is a different artefact with a different owner -
//  it is the SOURCE's, reachable by a host capability over the same data - and
//  producing it here would hand back a file matching neither what the reader
//  sees nor what the host would serve.
//
//  The cost is real and is not hidden: a currency-formatted column exports as
//  text a spreadsheet will not sum, because a grouped, symbol-bearing amount is
//  not a number to any CSV reader. That is the honest consequence of exporting
//  a rendering, and it is why the choice is recorded rather than assumed. The
//  reopen route is a declared export projection on the column - which would
//  need its own demand and its own charter walk, and gets neither here.
//
//  THE BYTE ORDER MARK IS EMITTED. `document` prefixes U+FEFF; `serialise`
//  does not, so the grammar and the signature stay separable and each is
//  tested on its own. RFC 4180 says nothing about a BOM, and a
//  standards-lawyer reading would leave it off - but the acceptance this phase
//  is held to is that the file OPENS CORRECTLY IN A SPREADSHEET, and the most
//  widely used spreadsheet on the most widely used desktop decodes a BOM-less
//  UTF-8 CSV in the ambient code page, mangling every non-ASCII character in
//  it. Every conformant reader tolerates the mark; one prominent reader
//  requires it. One character is the cheapest possible price for a file that is
//  readable rather than merely correct.
//
//  Nothing in this module is browser-specific: it is `FSharp.Core` and strings,
//  so it compiles under Fable and under .NET identically and the test runner
//  exercises exactly the code the renderer ships.
// ============================================================================

/// The record separator RFC 4180 section 2.1 fixes: CRLF, on every platform. It
/// is a property of the FORMAT and not of the machine that wrote the file, so
/// this is deliberately not the host's newline - a grid exported on one
/// platform and the same grid exported on another must produce the same bytes.
[<Literal>]
let recordSeparator = "\r\n"

/// The field separator. A comma, and not a host-locale list separator: the
/// format is named for it.
[<Literal>]
let fieldSeparator = ","

/// The UTF-8 byte order mark, as the single character it is. See the header:
/// `document` prefixes it, `serialise` does not.
[<Literal>]
let byteOrderMark = "\uFEFF"

/// One field, quoted per RFC 4180 sections 2.5-2.7.
///
/// A field is quoted when it contains the delimiter, a quote, CR or LF; inside
/// the quotes every embedded quote is doubled. Nothing else is escaped and
/// nothing is stripped - a value containing a newline keeps its newline, and
/// the quoting is what makes the record still parse.
///
/// A field that needs no quoting is emitted bare, which is what keeps an
/// ordinary export free of quotes a reader would then have to look past.
let escapeField (value: string) : string =
    let v = if isNull value then "" else value

    let needsQuoting =
        v.Contains "," || v.Contains "\"" || v.Contains "\n" || v.Contains "\r"

    if needsQuoting then
        "\"" + v.Replace("\"", "\"\"") + "\""
    else
        v

/// One record: the fields joined by the delimiter, each escaped.
let serialiseRecord (fields: string list) : string =
    fields |> List.map escapeField |> String.concat fieldSeparator

/// The CSV text: the header record, then one record per row, CRLF-separated.
///
/// **No trailing record separator.** RFC 4180 section 2.2 permits either, and
/// the absent one is chosen so that the number of separators is exactly one
/// less than the number of records - a reader that treats a trailing CRLF as an
/// empty final row then cannot invent one.
///
/// **No BOM** - see `document`, which adds it.
///
/// The header record is emitted even when there are no rows: a file with the
/// column names and nothing under them says *this grid is empty*, where an
/// empty file says nothing at all and several readers refuse it outright.
let serialise (headers: string list) (rows: string list list) : string =
    (serialiseRecord headers) :: (rows |> List.map serialiseRecord)
    |> String.concat recordSeparator

/// The text handed to the reader: `serialise` behind a UTF-8 byte order mark.
/// The header comment records why the mark is there and why the two are
/// separate functions.
let document (headers: string list) (rows: string list list) : string = byteOrderMark + serialise headers rows

/// What an export takes, and it is a fact the control must be able to state.
///
/// `WholeGrid` - the client holds every row the grid resolved, so the export is
/// the grid entire, whatever page is on screen. `ClientPage` - the source is
/// host-paged (a `Query` whose `dependsOn` names the page key), so the client
/// holds exactly one page and there is no honest way for it to export more.
///
/// The distinction is not cosmetic. The declared-total ruling's reasoning is
/// that the tree cannot substantiate data it does not hold; a control that said
/// *export* without qualification on a host-paged grid would promise a whole
/// dataset and deliver a page, which is the fake-affordance failure arriving by
/// understatement rather than by decoration. A full-dataset export over a paged
/// query is host chrome and stays out of the language.
[<RequireQualifiedAccess>]
type Scope =
    | WholeGrid of rowCount: int
    | ClientPage of rowCount: int

/// Which of the two a grid is in, from the source-shape rule the pager already
/// resolves and the resolved row count.
let scope (hostPages: bool) (resolvedRowCount: int) : Scope =
    if hostPages then
        Scope.ClientPage resolvedRowCount
    else
        Scope.WholeGrid resolvedRowCount

/// The control's accessible name. It states the scope rather than leaving the
/// reader to infer it, and it states the count it can actually deliver rather
/// than a total the tree cannot substantiate.
let scopeLabel (s: Scope) : string =
    match s with
    | Scope.WholeGrid 1 -> "Export 1 row as CSV"
    | Scope.WholeGrid n -> "Export " + string n + " rows as CSV"
    | Scope.ClientPage 1 -> "Export this page (1 row) as CSV"
    | Scope.ClientPage n -> "Export this page (" + string n + " rows) as CSV"

/// The suggested filename, derived from the grid's own identity.
///
/// The characters a file name may not carry on the platforms this file lands on
/// are collapsed to a hyphen, runs of hyphens are folded, and the result is
/// trimmed; an identity that survives none of that falls back to `export`. The
/// `.csv` suffix is added here rather than by the caller so no call site can
/// forget it - a suggested name with no extension is one the operating system
/// opens with the wrong program.
///
/// Length is capped so a long identity cannot produce a name a filesystem
/// refuses; the cap is on the STEM, so the suffix always survives.
let filename (identity: string) : string =
    let unsafe =
        set [ '<'; '>'; ':'; '"'; '/'; '\\'; '|'; '?'; '*'; ','; ' '; '\t'; '\n'; '\r' ]

    let mapped =
        (if isNull identity then "" else identity)
        |> Seq.map (fun c -> if unsafe.Contains c || c < ' ' then '-' else c)
        |> Seq.toArray
        |> System.String

    let folded =
        mapped.Split('-') |> Array.filter (fun part -> part <> "") |> String.concat "-"

    let stem =
        let trimmed = folded.Trim('.')

        if trimmed = "" then "export"
        elif trimmed.Length > 80 then trimmed.Substring(0, 80)
        else trimmed

    stem + ".csv"

/// The `data:` URL the delivery hands to the browser.
///
/// The delivery itself is deliberately NOT a new mechanism: it is the same
/// url-plus-suggested-name pair the platform's existing download instruction
/// already carries, performed the way that instruction is performed - an anchor
/// carrying a `download` attribute, activated and discarded. Nothing new is
/// minted for this phase on either side.
///
/// The content is percent-encoded rather than base64-encoded so that the URL a
/// consumer inspects is the CSV a consumer wrote, and the media type carries
/// `charset=utf-8` alongside the byte order mark: the two say the same thing to
/// two different readers, and neither one alone reaches both.
let dataUrl (csv: string) : string =
    "data:text/csv;charset=utf-8," + System.Uri.EscapeDataString csv
