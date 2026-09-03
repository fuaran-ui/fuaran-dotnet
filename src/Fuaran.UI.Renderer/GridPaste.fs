module Fuaran.UI.Renderer.GridPaste

// ============================================================================
//  Fuaran — structured paste into an editable grid (Phase 1126)
//
//  The inverse of `GridExport`, and governed by the same sentence. NOTHING here
//  reaches the wire: a grid that already declares `editable` and a destination
//  (`editStateKey`, or the Phase-663 State-source floor) has said everything the
//  vocabulary needs to hear — THIS GRID'S CELLS ARE THE READER'S TO CHANGE.
//  Whether a reader changes one by typing into it or twenty by pasting a block
//  is a property of the affordance, not a second capability, so it mints no
//  member, no `Action` case and no `NodeKind` (`docs/VOCABULARY.md`, Appendix A,
//  Interaction / affordance cluster).
//
//  That is a claim worth stating plainly because the alternative is tempting: a
//  `pasteable` flag beside `exportable` reads symmetric. It is not. `exportable`
//  had to be declared because a grid is NOT otherwise the reader's to take —
//  reading rows off the screen and handing them over as a file is a capability
//  no other declaration implies. Writing a cell IS already declared, by
//  `editable` plus a destination, and a flag saying "and also by pasting" would
//  be a second spelling of a permission already granted. The charter's cost is
//  paid at admission; here there is nothing to admit.
//
//  -- Why these are PURE functions ----------------------------------------
//  `GridExport`'s reason, unchanged: the .NET test runner mounts no DOM, so a
//  parser written inline in a paste handler could only be asserted ABOUT. And
//  every claim here is a claim about a delimiter, a quote, or an embedded line
//  break — one-line mistakes that produce a block which looks plausible and is
//  misaligned from the first offending field onward.
//
//  -- The four decisions this phase had to make and record -----------------
//
//  1. TSV FIRST, CSV AS THE FALLBACK, and the discriminator is the TAB.
//     Every desktop spreadsheet puts tab-separated text on the clipboard for a
//     copied range, so a tab anywhere in the block means TSV and the fields are
//     split on it with no quoting rules at all — which is correct, because that
//     is the format those applications write. Only when there is no tab is the
//     block read as RFC 4180 CSV, quotes and doubled-quotes and embedded
//     separators included. Sniffing rather than declaring, because the reader
//     is pasting from an application this page has never heard of and cannot
//     ask.
//
//  2. OVERFLOW IS DROPPED, NEVER GROWN. A block taller or wider than the space
//     below and right of the anchor loses its surplus. A paste is an EDIT of
//     rows that exist: the language has no row-insert affordance and no
//     column-add one, so growing the grid here would mint both silently, and a
//     grid whose row set is a `Query` result cannot be grown at all — its rows
//     are the host's. Dropping is the only behaviour that means the same thing
//     on every source a grid can have. `plan` reports what was dropped so a
//     host can say so.
//
//  3. A NON-WRITABLE COLUMN CONSUMES ITS POSITION. Where a pasted column lands
//     on a column with no edit destination — a closure-projected cell, a
//     `Kind` that is not Text or Numeric, an explicit `editable: false` — that
//     field is discarded and the NEXT pasted field still goes to the NEXT
//     column. Skipping the position instead would shift the remainder left and
//     write every subsequent value into the wrong column, which is the failure
//     mode that looks like it worked.
//
//  4. A VALUE THAT DOES NOT PARSE FOR ITS COLUMN IS DROPPED, not coerced. Text
//     pasted into a numeric column is discarded for that cell alone, exactly as
//     typing it into that cell's own input commits nothing (the Phase-663 rule:
//     a NaN cell would silently flatten every chart reading the same key). The
//     rest of the block still lands.
// ============================================================================

open Fuaran.UI.Types

// ─── Parsing ────────────────────────────────────────────────────────────────

/// Split a pasted block into records on CR / LF / CRLF, dropping a single
/// trailing empty record.
///
/// The trailing drop is deliberate and narrow: a copied spreadsheet range
/// habitually ends with a record separator, and reading that as an extra row of
/// empty cells would blank the row below the block. It drops ONE — a block
/// ending in two separators is genuinely claiming a final empty row, and the
/// overflow rule already bounds what that can reach.
let private splitRecords (text: string) : string list =
    let normalised = text.Replace("\r\n", "\n").Replace("\r", "\n")
    let parts = normalised.Split('\n') |> List.ofArray

    match List.rev parts with
    | "" :: rest when not (List.isEmpty rest) -> List.rev rest
    | _ -> parts

/// Split one RFC 4180 record into fields. A field opened with `"` runs to the
/// matching close, `""` inside it is one literal quote, and a separator inside
/// quotes is data. Anything outside quotes is taken literally, so an unbalanced
/// quote degrades to "the rest of the record is one field" rather than throwing
/// away everything after it.
let private splitCsvRecord (record: string) : string list =
    let fields = ResizeArray<string>()
    let current = System.Text.StringBuilder()
    let mutable inQuotes = false
    let mutable i = 0

    while i < record.Length do
        let ch = record[i]

        if inQuotes then
            if ch = '"' then
                if i + 1 < record.Length && record[i + 1] = '"' then
                    current.Append('"') |> ignore
                    i <- i + 1
                else
                    inQuotes <- false
            else
                current.Append(ch) |> ignore
        elif ch = '"' && current.Length = 0 then
            inQuotes <- true
        elif ch = ',' then
            fields.Add(current.ToString())
            current.Clear() |> ignore
        else
            current.Append(ch) |> ignore

        i <- i + 1

    fields.Add(current.ToString())
    List.ofSeq fields

/// Parse a pasted clipboard block into a rectangle of raw field strings, one
/// list per record. Decision 1 above: a tab anywhere selects TSV.
///
/// Records are NOT padded to a common width — a ragged block stays ragged, and
/// `plan` simply writes what each record carries. Padding would invent empty
/// values, and an empty value is a real edit (it blanks a cell).
let parse (text: string) : string list list =
    if System.String.IsNullOrEmpty text then
        []
    elif text.Contains "\t" then
        splitRecords text |> List.map (fun r -> r.Split('\t') |> List.ofArray)
    else
        splitRecords text |> List.map splitCsvRecord

/// Is this block worth intercepting? A single field is an ordinary paste into
/// the focused input and belongs to the browser — intercepting it would replace
/// native behaviour (the reader's undo stack, their selection, a partial
/// replacement inside the field) with a whole-cell overwrite, for no gain.
let isBlock (block: string list list) : bool =
    match block with
    | [] -> false
    | [ [ _ ] ] -> false
    | [ _ ] -> true
    | _ -> true

// ─── Planning the write ─────────────────────────────────────────────────────

/// A column's edit destination, as the grid's own render pass computes it:
/// `None` where the column has no destination (decision 3), otherwise the row
/// field to write and whether that column is numeric.
type ColumnTarget = { Field: string; Numeric: bool }

/// One resolved cell write: an absolute row index, the row field, and the value
/// to put there.
type CellWrite =
    { RowIndex: int
      Field: string
      Value: CellValue }

/// What a paste would do, and what it could not do.
///
/// `DroppedRows` / `DroppedColumns` count the block's overflow past the grid's
/// edges (decision 2); `SkippedCells` counts fields discarded because their
/// column has no destination (decision 3) or the value did not parse for it
/// (decision 4). A host that wants to tell the reader "12 of 20 cells applied"
/// has the numbers; the renderer itself says nothing, because a grid that
/// announced every partial paste would be noisier than one that does not.
type PastePlan =
    { Writes: CellWrite list
      DroppedRows: int
      DroppedColumns: int
      SkippedCells: int }

/// Resolve a pasted block against the grid, anchored at (`anchorRow`,
/// `anchorColumn`) — both absolute indices into the full sorted row set and the
/// declared column list.
///
/// `rowCount` is the grid's resolved row count and `columns` its per-column
/// targets, so this function needs no view of the rows themselves: it decides
/// WHERE each value goes, and the caller applies the writes in one pass (which
/// is not a stylistic split — the caller's write-back replaces the whole rows
/// value, so applying the block cell by cell would compute each write from the
/// pre-paste rows and keep only the last).
let plan
    (rowCount: int)
    (columns: ColumnTarget option list)
    (anchorRow: int)
    (anchorColumn: int)
    (block: string list list)
    : PastePlan =
    let columnArray = Array.ofList columns
    let columnCount = columnArray.Length

    let droppedRows =
        block |> List.length |> (fun n -> max 0 (anchorRow + n - rowCount))

    let droppedColumns =
        block
        |> List.fold (fun acc record -> max acc (List.length record)) 0
        |> fun w -> max 0 (anchorColumn + w - columnCount)

    let mutable skipped = 0
    let writes = ResizeArray<CellWrite>()

    block
    |> List.iteri (fun r record ->
        let rowIndex = anchorRow + r

        if rowIndex >= 0 && rowIndex < rowCount then
            record
            |> List.iteri (fun c field ->
                let colIndex = anchorColumn + c

                if colIndex >= 0 && colIndex < columnCount then
                    match columnArray[colIndex] with
                    | None -> skipped <- skipped + 1
                    | Some target ->
                        if target.Numeric then
                            match System.Double.TryParse field with
                            | true, f ->
                                writes.Add
                                    { RowIndex = rowIndex
                                      Field = target.Field
                                      Value = CellValue.Numeric f }
                            | false, _ -> skipped <- skipped + 1
                        else
                            writes.Add
                                { RowIndex = rowIndex
                                  Field = target.Field
                                  Value = CellValue.Text field }))

    { Writes = List.ofSeq writes
      DroppedRows = droppedRows
      DroppedColumns = droppedColumns
      SkippedCells = skipped }
