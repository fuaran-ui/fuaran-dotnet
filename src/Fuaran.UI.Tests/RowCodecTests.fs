module Fuaran.UI.Tests.RowCodecTests

// ============================================================================
//  Phase 1960 — the row-source codec this tier owns (`Fuaran.UI.RowCodec`),
//  which replaced the spine's retired one, and the two hazards it refuses.
//
//  Byte identity outside the hazards is held three ways:
//    1. every row-source payload in the shared corpus (every DataGrid / Chart
//       `source` `value` / `defaultValue` the tree carries, both spellings)
//       decodes and re-encodes exactly as the corpus states it;
//    2. a native-cell matrix — every rule-11 arm, both hazards' in-range
//       neighbours, the residual sentinel and a mixed feed — pinned to bytes
//       RECORDED from the retired codec (Fuaran.Core 0.36.0) before this tier
//       stopped referencing it. The live differential that recorded them ran
//       over the same corpus payloads (58 compared, 0 differ) and is described
//       in the phase's outcome; a pin here is what keeps it true;
//    3. the corpus's `row-encode` family, the cross-host statement of the two
//       refusals and their boundaries.
// ============================================================================

open System
open System.IO
open System.Text.Json
open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types

let private corpusRoot () : string option = Fuaran.Tests.CorpusRoot.tryFind ()

let private requireCorpus () =
    match corpusRoot () with
    | Some r -> r
    | None -> failtest "wire-format-fixtures not found — the corpus clone is missing"

/// A non-null value as a cell; `box` would type it nullable.
let inline private o (x: 'T when 'T: not null) : obj = x :> obj

let private canon (rows: Map<string, obj> seq) = Canon.render (RowCodec.encodeRows rows)

let private row (cells: (string * obj) list) : Map<string, obj> = Map.ofList cells

/// Every row-source payload under a DataGrid / Chart `source`, with the file it came from.
let private rowSourcePayloads (root: string) (dir: string) : (string * JVal) list =
    let found = ResizeArray()

    let rec inSource file (j: JVal) =
        match j with
        | JObj fs ->
            for (k, v) in fs do
                match k, v with
                | ("value" | "defaultValue"), (JArr _ as a) -> found.Add(file, a)
                | _ -> inSource file v
        | JArr xs -> xs |> List.iter (inSource file)
        | _ -> ()

    let rec walk file (j: JVal) =
        match j with
        | JObj fs ->
            let isRowKind =
                fs
                |> List.exists (fun (k, v) -> k = "$type" && (v = JStr "DataGrid" || v = JStr "Chart"))

            for (k, v) in fs do
                if isRowKind && k = "source" then
                    inSource file v
                else
                    walk file v
        | JArr xs -> xs |> List.iter (walk file)
        | _ -> ()

    for f in Directory.GetFiles(Path.Combine(root, dir), "*.json") |> Array.sort do
        match Json.parse (File.ReadAllText f) with
        | Ok j -> walk (Path.GetRelativePath(root, f)) j
        | Error _ -> ()

    List.ofSeq found

/// A JSON string member's text; the family's members are never null.
let private str (e: JsonElement) : string =
    match e.GetString() with
    | null -> failtest "row-encode: a string member is null"
    | s -> s

// ─── the `row-encode` family: descriptors onto .NET values ──────────────────

let private cellOf (d: JsonElement) : obj =
    match d.TryGetProperty "int64" with
    | true, v -> o (Int64.Parse(str v))
    | _ ->
        let text = str (d.GetProperty "dateTime")

        let kind =
            match str (d.GetProperty "kind") with
            | "utc" -> DateTimeKind.Utc
            | "unspecified" -> DateTimeKind.Unspecified
            | other -> failtestf "row-encode: unknown dateTime kind '%s'" other

        o (
            DateTime.SpecifyKind(
                DateTime.ParseExact(text, "yyyy-MM-dd'T'HH:mm:ss", Globalization.CultureInfo.InvariantCulture),
                kind
            )
        )

let private rowsOf (v: JsonElement) : Map<string, obj> list =
    [ for r in v.GetProperty("rows").EnumerateArray() ->
          r.EnumerateObject() |> Seq.map (fun p -> p.Name, cellOf p.Value) |> Map.ofSeq ]

[<Tests>]
let tests =
    testList
        "RowCodec (Phase 1960)"
        [ testCase "every row-source payload in the corpus round-trips to its own bytes"
          <| fun () ->
              let root = requireCorpus ()
              let payloads = rowSourcePayloads root "nodes"
              Expect.isGreaterThan payloads.Length 10 "the walk found the corpus's row feeds"

              for (file, p) in payloads do
                  match RowCodec.decodeRows p with
                  | Ok rows -> Expect.equal (canon rows) (Canon.render p) file
                  | Error e -> failtestf "%s: %s" file e

          testCase "the legacy sentinel and the lenient row feeds decode as the retired codec read them"
          <| fun () ->
              let root = requireCorpus ()

              Expect.equal
                  (RowCodec.decodeRows (JStr RowCodec.opaqueSentinel) |> Result.map List.ofSeq)
                  (Ok [])
                  "sentinel"

              let lenient = rowSourcePayloads root "lenient"
              Expect.isNonEmpty lenient "the lenient row feeds are found"

              for (file, p) in lenient do
                  Expect.isOk (RowCodec.decodeRows p) file

              Expect.equal
                  (RowCodec.decodeRows (JFloat 1.0))
                  (Error "rows: expected an array of row objects or \"<opaque>\", got float")
                  "a non-array"

              Expect.equal
                  (RowCodec.decodeRows (JArr [ JStr "x" ]))
                  (Error "rows: expected a row object, got string")
                  "a non-object row"

          testCase "outside the hazards every rule-11 cell keeps the retired codec's bytes"
          <| fun () ->
              let utc = DateTime(2026, 10, 18, 12, 30, 45, DateTimeKind.Utc)

              // Recorded from Fuaran.Core 0.36.0's RowCodec.encodeRows, one cell per row.
              let pins: (string * obj * string) list =
                  [ "string", o "héllo \"q\"", """[{"string":"héllo \"q\""}]"""
                    "empty", o "", """[{"empty":""}]"""
                    "true", o true, """[{"true":true}]"""
                    "false", o false, """[{"false":false}]"""
                    "float", o 3.25, """[{"float":3.25}]"""
                    "floatInt", o 42.0, """[{"floatInt":42}]"""
                    "negZero", o -0.0, """[{"negZero":0}]"""
                    "nan", o nan, """[{"nan":"NaN"}]"""
                    "inf", o infinity, """[{"inf":"Infinity"}]"""
                    "big", o 1e16, """[{"big":10000000000000000}]"""
                    "tiny", o 1e-7, """[{"tiny":1E-07}]"""
                    "int", o 42, """[{"int":42}]"""
                    "intMin", o Int32.MinValue, """[{"intMin":-2147483648}]"""
                    "int64", o 1234567890123L, """[{"int64":1234567890123}]"""
                    "int64Max", o 9007199254740992L, """[{"int64Max":9007199254740992}]"""
                    "int64Min", o -9007199254740992L, """[{"int64Min":-9007199254740992}]"""
                    "float32", o 1.5f, """[{"float32":1.5}]"""
                    "dto",
                    o (DateTimeOffset(2026, 10, 18, 12, 30, 45, TimeSpan.FromHours 5.0)),
                    """[{"dto":1792308645}]"""
                    "dtUtc", o utc, """[{"dtUtc":1792326645}]"""
                    "dtLocal", o (utc.ToLocalTime()), """[{"dtLocal":1792326645}]"""
                    "list", o [ 1; 2 ], """[{"list":"<opaque>"}]"""
                    "decimal", o 1.5m, """[{"decimal":"<opaque>"}]"""
                    "guid", o Guid.Empty, """[{"guid":"<opaque>"}]"""
                    "uint64", o 5UL, """[{"uint64":"<opaque>"}]"""
                    "nested", o (Map.ofList [ "a", o 1 ]), """[{"nested":"<opaque>"}]""" ]

              for (name, v, expected) in pins do
                  Expect.equal (canon [ row [ name, v ] ]) expected name

              let mixed =
                  [ pins |> List.map (fun (n, v, _) -> n, v) |> Map.ofList
                    Map.ofList [ "n", Unchecked.defaultof<obj>; "s", o "x" ]
                    Map.empty ]

              Expect.equal
                  (canon mixed)
                  """[{"big":10000000000000000,"decimal":"<opaque>","dtLocal":1792326645,"dtUtc":1792326645,"dto":1792308645,"empty":"","false":false,"float":3.25,"float32":1.5,"floatInt":42,"guid":"<opaque>","inf":"Infinity","int":42,"int64":1234567890123,"int64Max":9007199254740992,"int64Min":-9007199254740992,"intMin":-2147483648,"list":"<opaque>","nan":"NaN","negZero":0,"nested":"<opaque>","string":"héllo \"q\"","tiny":1E-07,"true":true,"uint64":"<opaque>"},{"s":"x"},{}]"""
                  "a mixed feed, a null cell omitted, an empty row"

              Expect.equal (canon []) "[]" "an empty feed is [], never null"

          testCase "the two hazards are refused, naming the cell, and never written"
          <| fun () ->
              let unspecified = DateTime(2026, 10, 18, 12, 30, 45, DateTimeKind.Unspecified)

              let cases =
                  [ unspecified :> obj, RowCodec.RowCellHazard.UnspecifiedDateTime, "ROW_CELL_UNSPECIFIED_DATETIME"
                    9007199254740993L :> obj,
                    RowCodec.RowCellHazard.InexactInteger 9007199254740993L,
                    "ROW_CELL_INEXACT_INTEGER"
                    -9007199254740993L :> obj,
                    RowCodec.RowCellHazard.InexactInteger -9007199254740993L,
                    "ROW_CELL_INEXACT_INTEGER"
                    Int64.MaxValue :> obj,
                    RowCodec.RowCellHazard.InexactInteger Int64.MaxValue,
                    "ROW_CELL_INEXACT_INTEGER" ]

              for (v, hazard, code) in cases do
                  let feed = [ row [ "a", o 1 ]; row [ "a", o 2; "z", v ] ]

                  let expected =
                      { RowCodec.Row = 1
                        RowCodec.Key = "z"
                        RowCodec.Hazard = hazard }

                  Expect.equal (RowCodec.tryEncodeRows feed) (Error expected) code
                  Expect.equal (RowCodec.RowCellRefusal.code expected) code "the stable code"
                  Expect.stringContains (RowCodec.RowCellRefusal.describe expected) "row 1, cell 'z'" "names the cell"

                  Expect.throwsC (fun () -> RowCodec.encodeRows feed |> ignore) (fun e ->
                      match e with
                      | RowCodec.RowCellRefused r -> Expect.equal r expected "the raised refusal"
                      | other -> failtestf "expected RowCellRefused, got %O" other)

          testCase "the generated structural codec refuses through the row codec"
          <| fun () ->
              let feed: Map<string, obj> seq =
                  [ row [ "at", o (DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)) ] ]

              let chart: ChartSpec<unit> =
                  { Defaults.chart with
                      Source = Binding.Static(Some feed) }

              Expect.throwsC (fun () -> Generated.encodeNodeKindJson (NodeKind.Chart chart) |> ignore) (fun e ->
                  match e with
                  | RowCodec.RowCellRefused r -> Expect.equal r.Key "at" "the chart's cell"
                  | other -> failtestf "expected RowCellRefused, got %O" other)

              let grid: DataGridSpec<unit> =
                  { Defaults.dataGrid with
                      Source = Binding.Static(Some [ row [ "n", o 9007199254740992L ] ]) }

              let bytes = Canon.render (Generated.encodeNodeKindJson (NodeKind.DataGrid grid))
              Expect.stringContains bytes "\"value\":[{\"n\":9007199254740992}]" "the in-range bound encodes"

          testCase "the corpus's row-encode vectors"
          <| fun () ->
              let root = requireCorpus ()

              use doc =
                  JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "row-encode", "manifest.json")))

              let vectors = doc.RootElement.GetProperty("vectors").EnumerateArray() |> List.ofSeq
              Expect.isGreaterThanOrEqual vectors.Length 6 "the family's vectors"

              for v in vectors do
                  let id = str (v.GetProperty "id")
                  let outcome = RowCodec.tryEncodeRows (rowsOf v)

                  match v.TryGetProperty "refused", outcome with
                  | (true, r), Error got ->
                      Expect.equal (RowCodec.RowCellRefusal.code got) (str (r.GetProperty "code")) id
                      Expect.equal got.Row (r.GetProperty("row").GetInt32()) id
                      Expect.equal got.Key (str (r.GetProperty "key")) id
                  | (true, _), Ok j -> failtestf "%s: expected a refusal, encoded %s" id (Canon.render j)
                  | _, Ok j -> Expect.equal (Canon.render j) (str (v.GetProperty "expected")) id
                  | _, Error got -> failtestf "%s: expected bytes, refused %s" id (RowCodec.RowCellRefusal.describe got) ]
