module Fuaran.UI.Tests.GridExport

// ============================================================================
//  The grid export serialiser (Phase 1125).
//
//  Every judgement the export makes lives in pure functions, and this file is
//  why they are pure. The .NET test runner mounts no DOM, so a serialiser
//  written inline in a click handler could only ever be ASSERTED about in
//  prose — and every claim here is a claim about BYTES, where prose is worth
//  nothing. A one-line mistake in any of these rules produces a file that
//  opens, looks plausible, and is wrong from the first offending row onward:
//
//    * a value containing the DELIMITER          → the row gains a field
//    * a value containing the QUOTE character    → the row ends early
//    * a value containing a LINE BREAK           → the file gains a row
//    * no byte order mark                        → every non-ASCII cell mangles
//
//  The last one is the reason the BOM is a decision rather than an oversight,
//  and it is pinned here so that removing it is a failing test rather than a
//  quiet regression in one spreadsheet on one platform.
// ============================================================================

open Expecto
open Fuaran.UI.Renderer.GridExport

[<Tests>]
let tests =
    testList
        "grid CSV export"
        [
          // ── escapeField — RFC 4180 §2.5–2.7 ──────────────────────────────
          testList
              "escapeField — quote only what must be quoted"
              [ test "an ordinary value is emitted bare" {
                    Expect.equal (escapeField "Acme") "Acme" "nothing to escape, no quotes added"
                    Expect.equal (escapeField "") "" "an empty field stays empty rather than becoming a pair of quotes"
                }

                test "a value containing the delimiter is quoted" {
                    // Unquoted, this is the failure that matters most: the row
                    // silently gains a field and every column after it shifts.
                    Expect.equal (escapeField "1,234.50") "\"1,234.50\"" "a comma inside a value must not end the field"
                }

                test "a value containing a quote is quoted AND the quote doubled" {
                    Expect.equal (escapeField "say \"hi\"") "\"say \"\"hi\"\"\"" "an embedded quote doubles"

                    Expect.equal
                        (escapeField "\"")
                        "\"\"\"\""
                        "a lone quote is a quoted field containing a doubled quote"
                }

                test "a value containing a line break is quoted and KEEPS the break" {
                    // Stripping it would be a silent edit of the reader's data;
                    // quoting is what makes the record still parse with it there.
                    Expect.equal (escapeField "one\ntwo") "\"one\ntwo\"" "LF is preserved inside quotes"
                    Expect.equal (escapeField "one\r\ntwo") "\"one\r\ntwo\"" "CRLF is preserved inside quotes"
                }

                test "a null value is the empty field, not a crash" {
                    Expect.equal
                        (escapeField (Unchecked.defaultof<string>))
                        ""
                        "a projection that yielded nothing exports as nothing"
                }

                test "the three triggers compose" {
                    Expect.equal
                        (escapeField "a,\"b\"\nc")
                        "\"a,\"\"b\"\"\nc\""
                        "one pair of quotes, every embedded quote doubled, the break kept"
                } ]

          // ── serialise — the document grammar ─────────────────────────────
          testList
              "serialise — header record, CRLF, no trailing break, no BOM"
              [ test "the header record comes first and the rows follow in order" {
                    let csv = serialise [ "Name"; "Team" ] [ [ "Ada"; "1" ]; [ "Grace"; "2" ] ]

                    Expect.equal csv "Name,Team\r\nAda,1\r\nGrace,2" "header then rows, source order preserved"
                }

                test "records are separated by CRLF on every platform" {
                    // Deliberately not the host newline: a grid exported on one
                    // platform and the same grid exported on another must produce
                    // the same bytes.
                    let csv = serialise [ "H" ] [ [ "a" ] ]
                    Expect.stringContains csv "\r\n" "CRLF is the record separator RFC 4180 fixes"
                }

                test "there is no trailing record separator" {
                    let csv = serialise [ "H" ] [ [ "a" ] ]

                    Expect.isFalse
                        (csv.EndsWith "\r\n")
                        "a reader that treats a trailing CRLF as an empty final row must not be given one"
                }

                test "an empty grid still exports its header record" {
                    // An empty file says nothing at all and several readers refuse
                    // it outright; the column names with nothing under them say
                    // *this grid is empty*, which is the true statement.
                    Expect.equal (serialise [ "Name"; "Team" ] []) "Name,Team" "the columns survive an empty grid"
                }

                test "serialise emits NO byte order mark" {
                    // The grammar and the signature are separable, and each is
                    // tested on its own.
                    //
                    // ORDINAL, and that is load-bearing rather than fussy: the
                    // byte order mark is a zero-width IGNORABLE character, so a
                    // culture-sensitive `StartsWith` reports that EVERY string
                    // starts with it — this assertion passed against a serialiser
                    // that emitted no mark and would have passed against one that
                    // emitted five.
                    let csv = serialise [ "H" ] [ [ "a" ] ]

                    Expect.isFalse
                        (csv.StartsWith(byteOrderMark, System.StringComparison.Ordinal))
                        "the mark belongs to `document`, not to the grammar"
                }

                test "quoting applies to header cells too" {
                    Expect.equal
                        (serialise [ "Amount, net" ] [ [ "1" ] ])
                        "\"Amount, net\"\r\n1"
                        "a column label carrying the delimiter is as dangerous as a value carrying it"
                } ]

          // ── document — the bytes handed over ─────────────────────────────
          testList
              "document — the byte order mark"
              [ test "document is serialise behind U+FEFF" {
                    let headers = [ "Name" ]
                    let rows = [ [ "Ada" ] ]

                    Expect.equal
                        (document headers rows)
                        (byteOrderMark + serialise headers rows)
                        "one mark, then the CSV"
                }

                test "the mark is exactly one character" {
                    // A file that opens correctly in a spreadsheet is the
                    // acceptance this phase is held to, and on the most widely
                    // used desktop spreadsheet a BOM-less UTF-8 CSV decodes in
                    // the ambient code page. Removing this must be a failing test
                    // rather than a quiet regression in one reader on one
                    // platform.
                    Expect.equal byteOrderMark "﻿" "the UTF-8 byte order mark, as the single character it is"
                    Expect.equal (document [] []).Length 1 "an empty header set behind the mark is one character"

                    // The positive half, ordinal for the reason the negative half
                    // records.
                    Expect.isTrue
                        ((document [ "H" ] [ [ "a" ] ]).StartsWith(byteOrderMark, System.StringComparison.Ordinal))
                        "the delivered document leads with the mark"
                } ]

          // ── scope — the declared-total posture ───────────────────────────
          testList
              "scope — what the control may honestly claim"
              [ test "a client-resolved grid exports the whole grid" {
                    Expect.equal (scope false 42) (Scope.WholeGrid 42) "the client holds every row it resolved"
                }

                test "a HOST-PAGED grid exports one page and says so" {
                    // The tree cannot substantiate data it does not hold. A
                    // control that promised a whole dataset and delivered a page
                    // would be a fake affordance by understatement.
                    Expect.equal (scope true 20) (Scope.ClientPage 20) "the client holds exactly one page"
                }

                test "the accessible name states the scope and the count" {
                    Expect.equal (scopeLabel (Scope.WholeGrid 42)) "Export 42 rows as CSV" "the whole grid, counted"

                    Expect.equal
                        (scopeLabel (Scope.ClientPage 20))
                        "Export this page (20 rows) as CSV"
                        "one page, said out loud rather than left to be inferred"
                }

                test "the singular is not \"1 rows\"" {
                    Expect.equal (scopeLabel (Scope.WholeGrid 1)) "Export 1 row as CSV" "one row reads as one row"

                    Expect.equal
                        (scopeLabel (Scope.ClientPage 1))
                        "Export this page (1 row) as CSV"
                        "and on the paged branch too"
                } ]

          // ── filename — the suggested name ────────────────────────────────
          testList
              "filename — derived from the grid's own identity"
              [ test "an ordinary identity keeps its shape and gains the suffix" {
                    Expect.equal (filename "team-roster") "team-roster.csv" "the identity is the name"
                }

                test "characters a filesystem refuses are collapsed" {
                    Expect.equal
                        (filename "reports/2026 Q1")
                        "reports-2026-Q1.csv"
                        "slash and space both fold to a hyphen"

                    Expect.equal (filename "a:b*c?d") "a-b-c-d.csv" "the reserved set folds together"
                }

                test "runs of hyphens fold to one" {
                    Expect.equal (filename "a   b") "a-b.csv" "three spaces are one separator, not three"
                }

                test "an identity that survives nothing falls back rather than producing a bare suffix" {
                    // `.csv` alone is a hidden file on several platforms and a
                    // name on none of them.
                    Expect.equal (filename "") "export.csv" "an empty identity"
                    Expect.equal (filename "///") "export.csv" "an identity of nothing but separators"
                    Expect.equal (filename (Unchecked.defaultof<string>)) "export.csv" "no identity at all"
                }

                test "a long identity is capped on the STEM so the suffix always survives" {
                    let name = filename (String.replicate 200 "x")
                    Expect.isTrue (name.EndsWith ".csv") "the extension is never the part that is trimmed"
                    Expect.equal name.Length 84 "80 characters of stem plus the four-character suffix"
                } ]

          // ── dataUrl — the delivery payload ───────────────────────────────
          testList
              "dataUrl — the media type and the encoding"
              [ test "the media type names CSV and the charset" {
                    // The charset and the byte order mark say the same thing to
                    // two different readers, and neither one alone reaches both.
                    Expect.stringStarts
                        (dataUrl "a,b")
                        "data:text/csv;charset=utf-8,"
                        "the type is declared, not guessed"
                }

                test "the delimiter and the record separator are percent-encoded" {
                    // Unencoded, a comma in the payload would be read as part of
                    // the URL grammar rather than the document.
                    let url = dataUrl "a,b\r\nc,d"
                    Expect.stringContains url "%2C" "the comma is encoded"
                    Expect.stringContains url "%0D%0A" "the record separator is encoded"
                } ]

          ]
