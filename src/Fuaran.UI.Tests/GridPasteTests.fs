module Fuaran.UI.Tests.GridPaste

// ============================================================================
//  Structured paste into an editable grid (Phase 1126).
//
//  `GridExportTests`' reason, inverted. The .NET test runner mounts no DOM, so
//  a parser written inline in a paste handler could only be asserted ABOUT —
//  and every claim here is a claim about a delimiter, a quote or an embedded
//  line break, where prose is worth nothing. Each of these rules is a one-line
//  mistake away from a block that applies, looks plausible, and is misaligned
//  from the first offending field onward:
//
//    * a TAB in the block          → the format is TSV, and CSV quoting is OFF
//    * a QUOTE in a CSV field      → the field ends early, the row gains one
//    * a trailing record separator → the row below the block is blanked
//    * a non-writable column       → every later value lands one column left
//
//  The last is the failure mode this file exists for: a paste that silently
//  writes the right values into the wrong columns is worse than one that
//  refuses, because nothing about it looks wrong.
// ============================================================================

open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Renderer.GridPaste

let private text (f: string) = Some { Field = f; Numeric = false }
let private num (f: string) = Some { Field = f; Numeric = true }

/// A plan's writes as `(row, field, rendered value)` triples, for comparison.
let private applied (p: PastePlan) =
    p.Writes
    |> List.map (fun w ->
        let v =
            match w.Value with
            | CellValue.Text s -> "T:" + s
            | CellValue.Numeric n -> "N:" + string n
            | other -> sprintf "%A" other

        w.RowIndex, w.Field, v)

[<Tests>]
let tests =
    testList
        "Fuaran.UI.Renderer.GridPaste"
        [ // ── Decision 1: TSV first, CSV as the fallback ───────────────────
          test "a block containing a TAB is read as TSV, with no CSV quoting" {
              // The quotes here are DATA. Every desktop spreadsheet writes
              // tab-separated text for a copied range and applies no quoting to
              // it, so reading this as CSV would strip characters the reader
              // actually copied.
              let block = parse "a\t\"b,c\"\nd\te"

              Expect.equal block [ [ "a"; "\"b,c\"" ]; [ "d"; "e" ] ] "Tabs split; quotes are literal"
          }

          test "a block with no tab is read as RFC 4180 CSV" {
              let block = parse "a,\"b,c\",\"say \"\"hi\"\"\""

              Expect.equal block [ [ "a"; "b,c"; "say \"hi\"" ] ] "Quoted separators and doubled quotes"
          }

          test "records split on CRLF, LF and CR alike" {
              Expect.equal (parse "a\tb\r\nc\td") [ [ "a"; "b" ]; [ "c"; "d" ] ] "CRLF"
              Expect.equal (parse "a\tb\nc\td") [ [ "a"; "b" ]; [ "c"; "d" ] ] "LF"
              Expect.equal (parse "a\tb\rc\td") [ [ "a"; "b" ]; [ "c"; "d" ] ] "CR"
          }

          test "ONE trailing record separator is dropped, a second is not" {
              // A copied range habitually ends with a separator; reading that as
              // an extra record would blank the row below the block. A block
              // ending in TWO is genuinely claiming a final empty row.
              Expect.equal (parse "a\tb\n") [ [ "a"; "b" ] ] "One trailing separator dropped"
              Expect.equal (parse "a\tb\n\n") [ [ "a"; "b" ]; [ "" ] ] "Two: the empty record survives"
          }

          test "a single value is not a block" {
              Expect.isFalse (isBlock (parse "hello")) "One field belongs to the browser"
              Expect.isTrue (isBlock (parse "a\tb")) "One row, two columns IS a block"
              Expect.isTrue (isBlock (parse "a\nb")) "Two rows, one column IS a block"
              Expect.isFalse (isBlock (parse "")) "Nothing is not a block"
          }

          // ── Decision 2: overflow is dropped, never grown ──────────────────
          test "rows past the last row are DROPPED and counted" {
              let p = plan 2 [ text "a" ] 1 0 (parse "x\ny\nz")

              Expect.equal (applied p) [ 1, "a", "T:x" ] "Only the row that exists is written"
              Expect.equal p.DroppedRows 2 "The two rows past the end are reported"
          }

          test "columns past the last column are DROPPED and counted" {
              let p = plan 1 [ text "a"; text "b" ] 0 1 (parse "x\ty\tz")

              Expect.equal (applied p) [ 0, "b", "T:x" ] "Only the column that exists is written"
              Expect.equal p.DroppedColumns 2 "The two columns past the end are reported"
          }

          // ── Decision 3: a non-writable column consumes its position ───────
          test "a non-writable column swallows its field and does NOT shift the rest" {
              // The middle column has no destination. `z` must still land in the
              // THIRD column — shifting it left would write the right values
              // into the wrong columns, which is the failure that looks like it
              // worked.
              let p = plan 1 [ text "a"; None; text "c" ] 0 0 (parse "x\ty\tz")

              Expect.equal (applied p) [ 0, "a", "T:x"; 0, "c", "T:z" ] "Position preserved"
              Expect.equal p.SkippedCells 1 "The swallowed field is reported"
          }

          // ── Decision 4: a value that does not parse is dropped, not coerced ─
          test "text pasted into a numeric column is dropped for that cell alone" {
              let p = plan 1 [ num "n"; text "t" ] 0 0 (parse "not-a-number\tkept")

              Expect.equal (applied p) [ 0, "t", "T:kept" ] "The rest of the block still lands"
              Expect.equal p.SkippedCells 1 "The unparseable cell is reported"
          }

          test "a numeric column takes a parsed number, not the raw text" {
              let p = plan 1 [ num "n" ] 0 0 (parse "12.5\t")

              Expect.equal (applied p) [ 0, "n", "N:12.5" ] "Parsed as a number"
          }

          // ── The ordinary case ────────────────────────────────────────────
          test "a rectangular block lands cell for cell from the anchor" {
              let p = plan 4 [ text "a"; num "b"; text "c" ] 1 1 (parse "10\tx\n20\ty")

              Expect.equal
                  (applied p)
                  [ 1, "b", "N:10"; 1, "c", "T:x"; 2, "b", "N:20"; 2, "c", "T:y" ]
                  "Anchored at (1,1), spreading down and right"

              Expect.equal (p.DroppedRows, p.DroppedColumns, p.SkippedCells) (0, 0, 0) "Nothing lost"
          }

          test "a ragged block writes what each record carries, padding nothing" {
              // Padding would invent empty values, and an empty value is a real
              // edit: it blanks a cell the reader did not touch.
              let p = plan 2 [ text "a"; text "b" ] 0 0 (parse "x\ty\np")

              Expect.equal (applied p) [ 0, "a", "T:x"; 0, "b", "T:y"; 1, "a", "T:p" ] "Short record stays short"
          }

          test "an empty pasted field IS an edit — it blanks the cell" {
              let p = plan 1 [ text "a"; text "b" ] 0 0 (parse "\tkept")

              Expect.equal (applied p) [ 0, "a", "T:"; 0, "b", "T:kept" ] "The empty string is written"
          } ]
