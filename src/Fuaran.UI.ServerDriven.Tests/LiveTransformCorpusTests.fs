module Fuaran.UI.ServerDriven.Tests.LiveTransformCorpusTests

open System
open System.IO
open Expecto
open Fuaran.Core
open Fuaran.UI.ServerDriven

// ============================================================================
//  Phase 1179 — the live-Transform incremental seam, measured against the
//  `incremental-recompute` conformance family's own edit streams.
//
//  The tier promises ONE thing: what it returns after a state edit is what a
//  full evaluation over the changed source produces. That equivalence is a
//  certified property of the substrate's seam rather than something asserted
//  here — which is exactly why it is measured here. A consumer that took a
//  certified property on trust would not notice the day it stopped holding, and
//  this tier is the estate's first consumer of that seam outside the
//  substrate's own tests.
//
//  ── WHAT IS ASSERTED, AND WHAT DELIBERATELY IS NOT ────────────────────────
//  The RESULT is asserted on every vector with no allowance, including the ones
//  whose recorded refresh declines: a decline that answered differently would be
//  a defect the footprint would not reveal.
//
//  The recorded COUNTS are NOT asserted equal. They are the corpus's reference
//  boundary, and the substrate has already widened its own restricted walk past
//  it in two places, so asserting them would make an improvement upstream read
//  as a regression here. What IS asserted about the work is one-directional and
//  survives any widening: where the vector's own recorded refresh restricts, the
//  refresh this tier performs must evaluate strictly fewer rows than the full
//  evaluation beside it. An evaluator that recomputed everything and reported it
//  as restricted would satisfy every other clause.
//
//  ── THE READER REFUSES WHAT IT DOES NOT MODEL, BY NAME ────────────────────
//  Per MEMBER, not per verb: `lag` is modelled and every other window function
//  refused, `semi` modelled and every other join kind refused. A vector whose
//  `cumulSum` were silently read as a `lag` would certify a frame the corpus did
//  not write, which is worse than a vector nobody ran. A missing fixture
//  directory is a failure and never a skip, for the same reason.
// ============================================================================

let private fixturesDir =
    Path.Combine(AppContext.BaseDirectory, "fixtures", "incremental-recompute")

let private refuse (what: string) (name: string) : 'a =
    failwithf "§12.7 reader: unregistered %s \"%s\" — the corpus uses a member this reader does not model" what name

// ─── the vector, read ─────────────────────────────────────────────────────────

let private field (name: string) (j: JVal) : JVal =
    match j with
    | JObj members ->
        match members |> List.tryFind (fun (k, _) -> k = name) with
        | Some(_, v) -> v
        | None -> failwithf "§12.7 reader: no member \"%s\"" name
    | _ -> failwithf "§12.7 reader: expected an object to read \"%s\" from" name

let private tryField (name: string) (j: JVal) : JVal option =
    match j with
    | JObj members -> members |> List.tryFind (fun (k, _) -> k = name) |> Option.map snd
    | _ -> None

let private asStr (j: JVal) : string =
    match j with
    | JStr s -> s
    | _ -> failwith "§12.7 reader: expected a string"

let private asInt (j: JVal) : int =
    match j with
    | JInt n -> n
    | _ -> failwith "§12.7 reader: expected an integer"

let private asArr (j: JVal) : JVal list =
    match j with
    | JArr items -> items
    | _ -> failwith "§12.7 reader: expected an array"

/// A cell is an integer, a string, or the null a bounded frame's partition-leading
/// row produces. The null's boolean rides as a STRING — §4.5 of the specification
/// admits no JSON `true` anywhere — so a raw JSON boolean here is a fixture that
/// was written against a different format, and is refused rather than coerced.
let private cellOf (j: JVal) : Cell =
    match tryField "int" j, tryField "string" j, tryField "null" j with
    | Some v, _, _ -> Int(asInt v)
    | _, Some v, _ -> Str(asStr v)
    | _, _, Some(JStr "true") -> Null
    | _, _, Some _ -> failwith "§12.7 reader: a null cell whose marker is not the string \"true\""
    | _ -> failwith "§12.7 reader: a cell that is neither an int, a string nor a null"

let private columnTypeOf (name: string) : ColumnType =
    match name with
    | "int" -> IntType
    | "string" -> StringType
    | other -> refuse "column type" other

/// Read a table as its schema and its rows, keeping the corpus's column order.
let private tableOf (j: JVal) : Schema * Cell list list =
    let schema =
        j
        |> field "columns"
        |> asArr
        |> List.map (fun c -> asStr (field "name" c), columnTypeOf (asStr (field "type" c)))

    let rows =
        j
        |> field "rows"
        |> asArr
        |> List.map (fun r -> r |> field "cells" |> asArr |> List.map cellOf)

    schema, rows

let private toTable (schema: Schema, rows: Cell list list) : Table =
    { Schema = schema
      Columns =
        schema
        |> List.mapi (fun i (name, ty) -> Column.create name ty (rows |> List.map (fun r -> List.item i r))) }

let private sortDirOf (s: string) : SortDir =
    match s with
    | "ascending" -> Asc
    | "descending" -> Desc
    | other -> refuse "sort direction" other

let rec private exprOf (j: JVal) : ColExpr =
    match asStr (field "expr" j) with
    | "column" -> Col(asStr (field "name" j))
    | "literal" -> Lit(Int(asInt (field "int" j)))
    | "binary" ->
        let op =
            match asStr (field "op" j) with
            | "greaterThan" -> Gt
            | "multiply" -> Mul
            | other -> refuse "operator" other

        Binary(op, exprOf (field "left" j), exprOf (field "right" j))
    | other -> refuse "expression" other

let private aggOf (j: JVal) : Agg =
    let fn =
        match asStr (field "fn" j) with
        | "count" -> Count
        | "sum" -> Sum
        | "first" -> First
        | other -> refuse "aggregate" other

    { Name = asStr (field "name" j)
      Fn = fn
      Of = asStr (field "of" j) }

let private stepOf (j: JVal) : Transform =
    match asStr (field "verb" j) with
    | "filter" -> Filter(exprOf (field "where" j))
    | "derive" -> Derive(asStr (field "column" j), exprOf (field "value" j))
    | "groupBy" ->
        GroupBy(j |> field "keys" |> asArr |> List.map asStr, j |> field "aggregates" |> asArr |> List.map aggOf)
    | "sort" ->
        Sort(
            j
            |> field "by"
            |> asArr
            |> List.map (fun b -> asStr (field "column" b), sortDirOf (asStr (field "direction" b)))
        )
    | "window" ->
        let fn =
            match asStr (field "fn" j) with
            | "lag" -> Lag
            | other -> refuse "window function" other

        Window
            { PartitionBy = j |> field "partitionBy" |> asArr |> List.map asStr
              OrderBy =
                j
                |> field "orderBy"
                |> asArr
                |> List.map (fun b -> asStr (field "column" b), sortDirOf (asStr (field "direction" b)))
              Fn = fn
              Of = asStr (field "of" j)
              As = asStr (field "as" j) }
    | "join" ->
        let how =
            match asStr (field "how" j) with
            | "semi" -> Semi
            | other -> refuse "join kind" other

        Join(
            Embedded(toTable (tableOf (field "source" j))),
            j
            |> field "on"
            |> asArr
            |> List.map (fun p -> asStr (field "left" p), asStr (field "right" p)),
            how
        )
    | other -> refuse "verb" other

// ─── the edit stream, applied ─────────────────────────────────────────────────

type private EditOp =
    | SetCell of row: string * column: string * value: Cell
    | AppendRow of Cell list
    | RemoveRow of row: string

type private Edits =
    { Scheme: string
      Key: string option
      Ops: EditOp list }

let private editsOf (j: JVal) : Edits =
    { Scheme = asStr (field "scheme" j)
      Key = tryField "key" j |> Option.map asStr
      Ops =
        j
        |> field "ops"
        |> asArr
        |> List.map (fun o ->
            match asStr (field "op" o) with
            | "setCell" -> SetCell(asStr (field "row" o), asStr (field "column" o), cellOf (field "value" o))
            | "appendRow" -> AppendRow(o |> field "cells" |> asArr |> List.map cellOf)
            | "removeRow" -> RemoveRow(asStr (field "row" o))
            | other -> refuse "edit op" other) }

/// Apply the stream to the source, in order, against the table as it stands.
/// An `ordinal` stream addresses a row by its POSITION and an `identity` one by
/// its key column's value; collapsing the two would lose the whole of §12.7's
/// re-addressing pair, so they are read as the two different things they are.
/// The stream's rendering of a key cell — the cell's own value, which is what an
/// `identity` op's `row` names. NOT the substrate's canonical row token: that is
/// type-tagged, so matching against it would find nothing and read as "the source
/// does not carry this row".
let private keyText (c: Cell) : string =
    match c with
    | Str s -> s
    | Int n -> string n
    | other -> failwithf "§12.7 reader: a key cell that is neither a string nor an integer (%A)" other

let private applyEdits (schema: Schema) (rows: Cell list list) (edits: Edits) : Cell list list =
    let columnIndex name =
        match schema |> List.tryFindIndex (fun (n, _) -> n = name) with
        | Some i -> i
        | None -> failwithf "§12.7 reader: no column \"%s\"" name

    let locate (current: Cell list list) (ref: string) =
        match edits.Scheme, edits.Key with
        | "ordinal", _ -> int ref
        | "identity", Some key ->
            let k = columnIndex key

            match current |> List.tryFindIndex (fun r -> keyText (List.item k r) = ref) with
            | Some i -> i
            | None -> failwithf "§12.7 reader: the stream names row \"%s\", which the source does not carry" ref
        | scheme, _ -> refuse "addressing scheme" scheme

    edits.Ops
    |> List.fold
        (fun current op ->
            match op with
            | SetCell(row, column, value) ->
                let i = locate current row
                let j = columnIndex column

                current
                |> List.mapi (fun ri r ->
                    if ri = i then
                        r |> List.mapi (fun ci c -> if ci = j then value else c)
                    else
                        r)
            | AppendRow cells ->
                if List.length cells <> List.length schema then
                    failwith
                        "§12.7 reader: an appended row carries a different number of cells from the source's columns"

                current @ [ cells ]
            | RemoveRow row ->
                let i = locate current row

                current
                |> List.mapi (fun ri r -> ri, r)
                |> List.filter (fun (ri, _) -> ri <> i)
                |> List.map snd)
        rows

// ─── the vectors ──────────────────────────────────────────────────────────────

type private Vector =
    {
        Name: string
        Pipeline: Transform list
        Source: Table
        Changed: Table
        Edits: Edits
        Recorded: Table
        /// The refresh class the corpus recorded, and what a full evaluation cost
        /// on its instrument. Read as evidence, never asserted equal — see the
        /// header.
        RecordedRefreshKind: string
        RecordedFullRows: int
    }

/// The vector's own name, which is its file's. Read through a total lift rather
/// than trusted: the framework's answer is nullable and a test whose subject is
/// silently the empty string reports on nothing.
let private vectorName (path: string) : string =
    match Path.GetFileNameWithoutExtension path with
    | null -> failwithf "§12.7 reader: %s has no readable file name" path
    | name -> name

let private readVector (path: string) : Vector =
    let doc =
        match Json.parse (File.ReadAllText path) with
        | Ok j -> j
        | Error e -> failwithf "§12.7 reader: %s did not parse — %s" (Path.GetFileName path) e

    let schema, rows = tableOf (field "source" doc)
    let edits = editsOf (field "edits" doc)
    let expect = field "expect" doc

    { Name = vectorName path
      Pipeline = doc |> field "pipeline" |> asArr |> List.map stepOf
      Source = toTable (schema, rows)
      Changed = toTable (schema, applyEdits schema rows edits)
      Edits = edits
      Recorded = toTable (tableOf (field "result" expect))
      RecordedRefreshKind = asStr (field "kind" (field "recompute" (field "refresh" expect)))
      RecordedFullRows =
        expect
        |> field "full"
        |> field "recompute"
        |> tryField "rowsEvaluated"
        |> Option.map asInt
        |> Option.defaultValue 0 }

let private vectors () : Vector list =
    if not (Directory.Exists fixturesDir) then
        failwithf
            "the incremental-recompute fixtures are not at %s — a conformance check that goes green without its oracle is worse than no check"
            fixturesDir

    Directory.GetFiles(fixturesDir, "*.json")
    |> Array.toList
    |> List.sort
    |> List.map readVector

/// Phase 1479 — the recorded vectors as fixed cases for the footprint laws beside this file,
/// flattened to plain tuples rather than the `Vector` record. The reader's own shapes stay private
/// deliberately: they model §12.7's document and nothing else, and a second file depending on them
/// would make every future change to the reader a change to two files. What a law list needs from a
/// vector is only what it drives the seam with.
let corpusCases () : (string * Transform list * Table * Table * string) list =
    vectors ()
    |> List.map (fun v -> v.Name, v.Pipeline, v.Source, v.Changed, v.Edits.Key |> Option.defaultValue "id")

// ─── the assertions ───────────────────────────────────────────────────────────

/// The site key a live grid would use. One key throughout: each vector gets a
/// fresh store, so the first evaluation primes and the second advances, which is
/// exactly the render-then-edit sequence the tier is built for.
let private site = "grid:orders"

let private ok (r: Result<'a, string>) : 'a =
    match r with
    | Ok v -> v
    | Error e -> failwithf "the seam refused: %s" e

let private expectSameTable (context: string) (expected: Table) (actual: Table) =
    Expect.equal (List.map fst actual.Schema) (List.map fst expected.Schema) (context + ": column names")
    Expect.equal actual.Schema expected.Schema (context + ": schema")
    Expect.equal (actual.Columns |> List.map _.Cells) (expected.Columns |> List.map _.Cells) (context + ": cells")

[<Tests>]
let tests =
    testList
        "LiveTransform — the incremental seam over the incremental-recompute corpus"
        [ test "the corpus is present and every vector is readable" {
              let vs = vectors ()
              Expect.isGreaterThan (List.length vs) 0 "the fixtures directory carries vectors"
          }

          testList
              "prime then refresh answers what a full evaluation answers"
              (vectors ()
               |> List.map (fun v ->
                   test v.Name {
                       // The identity column is the caller's declaration. An
                       // ordinal-addressed stream has none, and the seam must
                       // decline a positional delta rather than treat it as an
                       // identity one — so the ordinal vector deliberately hands
                       // the reserved scheme's own witness column and relies on
                       // the DIFF, not on the vector's scheme, to be honest about
                       // what moved.
                       let key = v.Edits.Key |> Option.defaultValue "id"
                       let store = LiveTransformStore()

                       let primed = ok (store.Evaluate(site, key, v.Pipeline, v.Source))
                       Expect.isTrue primed.Primed "the first evaluation of a site primes"

                       let refreshed = ok (store.Evaluate(site, key, v.Pipeline, v.Changed))
                       Expect.isFalse refreshed.Primed "the second evaluation of a site advances the primed state"

                       // The pass criterion, with no allowance: the refreshed
                       // table is the table a full evaluation over the changed
                       // source produces, and it is the table the corpus records.
                       let reference = ok (LiveTransform.reference v.Pipeline v.Changed)
                       expectSameTable "refresh vs full evaluation" reference refreshed.Result
                       expectSameTable "full evaluation vs the recorded result" v.Recorded reference
                   }))

          testList
              "a restricted refresh evaluates strictly fewer rows than the full evaluation"
              (vectors ()
               |> List.filter (fun v -> v.RecordedRefreshKind <> "fullRecompute")
               |> List.map (fun v ->
                   test v.Name {
                       let key = v.Edits.Key |> Option.defaultValue "id"
                       let store = LiveTransformStore()
                       store.Evaluate(site, key, v.Pipeline, v.Source) |> ok |> ignore
                       let refreshed = ok (store.Evaluate(site, key, v.Pipeline, v.Changed))

                       // One-directional, and so it survives any widening of the
                       // restricted walk upstream: an evaluator that recomputed
                       // everything and reported it as restricted would satisfy
                       // every other clause in this file.
                       Expect.isLessThan
                           (Incremental.rowsEvaluated refreshed.Footprint)
                           v.RecordedFullRows
                           "the refresh does less work than the full evaluation it is measured against"
                   }))

          test "a site primed under one pipeline and refreshed under another still answers correctly" {
              // The seam notices and re-primes rather than advancing caches that
              // answer a different question. It is asserted here because the store
              // keys by SITE, so a grid whose pipeline changes between renders
              // reaches exactly this path.
              let vs = vectors ()

              let rowLocal = vs |> List.find (fun v -> v.Name = "point-edit-row-local")
              let grouping = vs |> List.find (fun v -> v.Name = "chain-edit-group-local")

              let store = LiveTransformStore()
              store.Evaluate(site, "id", rowLocal.Pipeline, rowLocal.Source) |> ok |> ignore

              let crossed = ok (store.Evaluate(site, "id", grouping.Pipeline, grouping.Changed))
              let reference = ok (LiveTransform.reference grouping.Pipeline grouping.Changed)
              expectSameTable "a pipeline swap under one site" reference crossed.Result
          }

          test "an unkeyable identity column answers in full rather than reusing a cache" {
              // A witness that cannot key the source cannot describe what moved.
              // The honest delta for that is the top element, and the table must
              // still be right — which is the property that makes the fall-back
              // safe rather than merely quiet.
              let v = vectors () |> List.find (fun x -> x.Name = "point-edit-row-local")
              let store = LiveTransformStore()
              store.Evaluate(site, "no-such-column", v.Pipeline, v.Source) |> ok |> ignore
              let refreshed = ok (store.Evaluate(site, "no-such-column", v.Pipeline, v.Changed))
              let reference = ok (LiveTransform.reference v.Pipeline v.Changed)
              expectSameTable "an unkeyable source" reference refreshed.Result
          }

          test "the reader refuses a member it does not model rather than reading it as a neighbour" {
              // The go-red proof for the refusal itself. Without it, "every vector
              // was read" and "every vector was read AS WRITTEN" are the same
              // sentence, and only one of them is worth anything.
              let unmodelled =
                  JObj
                      [ "verb", JStr "window"
                        "partitionBy", JArr [ JStr "b" ]
                        "orderBy", JArr [ JObj [ "column", JStr "a"; "direction", JStr "ascending" ] ]
                        "fn", JStr "cumulSum"
                        "of", JStr "a"
                        "as", JStr "running" ]

              Expect.throws (fun () -> stepOf unmodelled |> ignore) "an unmodelled window function is refused by name"
          } ]
