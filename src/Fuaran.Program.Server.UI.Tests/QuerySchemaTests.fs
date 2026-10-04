module Fuaran.Program.Server.Tests.QuerySchemaTests

// ─── The pre-execution query-schema check ────────────────────────────
//
// Four claims, each checked rather than asserted in prose:
//
//   the DERIVATION is faithful to the evaluator — a pipeline's output columns
//   are what the evaluator would produce, verb by verb, without evaluating
//   anything;
//
//   it is HONEST where it cannot decide — a derived column's type, a pivot's
//   value columns and an undeclared `Ref` all yield "unknown", which is
//   reported as data and never as a refusal;
//
//   an unsatisfiable handler is REFUSED BEFORE the tree runs, and the refusal
//   NAMES THE COLUMN;
//
//   the runtime posture is UNCHANGED — the same broken pipeline, run rather
//   than checked, still reports its error's discriminator and nothing more.
//
// The last two are one test read twice on purpose: they are the two halves of
// the trade the design note recorded, and a test that showed only the new half
// would not notice if the old half quietly gained a payload.

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.ServerDriven
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.Renderer.BindingResolver
open Fuaran.Program.Bounded
open Fuaran.Program.UI
open Fuaran.Program.Server
open Fuaran.Program.Server.UI

// ─── fixtures ────────────────────────────────────────────────────────

let private col (name: string) (ty: Fuaran.Core.ColumnType) (cells: Fuaran.Core.Cell list) : Fuaran.Core.Column =
    { Name = name
      Type = ty
      Cells = cells }

/// Three orders, three columns — enough for a pipeline to visibly narrow.
let private orders: Fuaran.Core.Table =
    { Schema =
        [ "id", Fuaran.Core.IntType
          "total", Fuaran.Core.FloatType
          "region", Fuaran.Core.StringType ]
      Columns =
        [ col "id" Fuaran.Core.IntType [ Fuaran.Core.Int 1; Fuaran.Core.Int 2; Fuaran.Core.Int 3 ]
          col "total" Fuaran.Core.FloatType [ Fuaran.Core.Float 10.0; Fuaran.Core.Float 20.0; Fuaran.Core.Float 30.0 ]
          col "region" Fuaran.Core.StringType [ Fuaran.Core.Str "n"; Fuaran.Core.Str "s"; Fuaran.Core.Str "n" ] ] }

let private ordersSchema: Fuaran.Core.Schema = orders.Schema

let private embedded = Fuaran.Core.DataSource.Embedded orders

let private querySlot (name: string) : Binding<Fuaran.Core.Row seq> =
    Binding.Query(name, (fun (value: obj) -> unbox<Fuaran.Core.Row seq> value), None)

let private gridColumn (field: string option) : ColumnErased<obj> =
    { Field = field
      Format = CellFormat.None
      Kind = CellKindErased.Text
      Label = defaultArg field "computed"
      Sortable = None
      Editable = None
      Value = None
      Width = ColumnWidth.Auto }

/// A grid bound to a query slot, naming the fields its columns read.
let private grid (id: string) (slot: string) (fields: string option list) : Node<obj> =
    let node = Fuaran.markdown id "placeholder"

    { node with
        Kind =
            NodeKind.DataGrid
                { Columns = fields |> List.map gridColumn
                  Editable = false
                  RowKey = None
                  RowKeyField = None
                  SortStateKey = None
                  PageSize = None
                  PageStateKey = None
                  DefaultSort = None
                  EditStateKey = None
                  Reorderable = false
                  // Every member the tier has added to this record since is at
                  // its absent / off value. These fixtures exist to ask what a
                  // grid READS FROM A QUERY SLOT, and none of the transfer,
                  // print-break or export members answers that question — so
                  // giving them anything else would put a second variable into
                  // a test with one subject.
                  TransferInKey = None
                  TransferOutKey = None
                  KeepRowsTogether = false
                  RepeatHeader = false
                  Exportable = false
                  WindowStateKey = None
                  RowTotal = None
                  Source = querySlot slot
                  StaticRows = None
                  OnRowClick = None } }

/// A chart bound to a query slot, naming its axes.
let private chart (id: string) (slot: string) (x: string) (ys: string list) : Node<obj> =
    Fuaran.chart
        id
        { Defaults.chart<obj> with
            Source = querySlot slot
            XField = x
            YFields = ys }

let private page (children: Node<obj> list) : Node<obj> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<obj> with
            Children = children }

let private wireOf (node: Node<obj>) : WireTree = WireTree.ofDecoded node

/// Covers every bounded effect, so a strict construction's coverage half is
/// silent and only the schema half can speak. The two checks are independent by
/// design; a test of one that could be tripped by the other would be measuring
/// both.
let private coversEverything: HostCoverage =
    HostCoverage.nothing
    |> HostCoverage.withEffects [ "Navigate"; "WriteToClipboard"; "ReadFileBody" ]
    |> HostCoverage.permissive

let private handlerWith (name: string) (slot: string) (pipeline: Fuaran.Compute.Transform list) : Handler =
    { Name = name
      Stages = [ Effect(ServerEffect.RunQuery(slot, embedded, pipeline)) ] }

let private servicesWith (handler: Handler) : ServerServices =
    ServerServices.createPermissive |> ServerServices.withHandler "/orders" handler

let private closedNames (knowledge: SchemaKnowledge) =
    Expect.isTrue (Schema.isClosed knowledge) "expected a closed schema"
    Schema.names knowledge

// ─── the derivation ──────────────────────────────────────────────────

let private derivationTests =
    testList
        "Schema.ofTransform"
        [ test "an embedded source is closed at its own schema" {
              let knowledge = Schema.ofSource SourceSchemas.none embedded
              Expect.equal (closedNames knowledge) [ "id"; "total"; "region" ] "source schema"
          }

          test "Project closes the set to exactly the listed columns, in order" {
              let knowledge =
                  Schema.ofPipeline
                      SourceSchemas.none
                      embedded
                      [ Fuaran.Compute.Transform.Project [ "total", "amount"; "id", "id" ] ]

              Expect.equal (closedNames knowledge) [ "amount"; "id" ] "projected names"
              Expect.equal (Schema.typeOf "amount" knowledge) (Some Fuaran.Core.FloatType) "renamed type carries over"
          }

          test "Derive names its column and declines to type it" {
              let knowledge =
                  Schema.ofPipeline
                      SourceSchemas.none
                      embedded
                      [ Fuaran.Compute.Transform.Derive("doubled", Fuaran.Compute.ColExpr.Col "total") ]

              Expect.equal (closedNames knowledge) [ "id"; "total"; "region"; "doubled" ] "appended"
              // The evaluator infers the type from the cells the expression
              // produced, so a static answer here would be a guess that could
              // disagree with the run.
              Expect.isNone (Schema.typeOf "doubled" knowledge) "a derived column's type is data-dependent"
          }

          test "GroupBy closes to keys plus aggregates, typed by the aggregate" {
              let knowledge =
                  Schema.ofPipeline
                      SourceSchemas.none
                      embedded
                      [ Fuaran.Compute.Transform.GroupBy(
                            [ "region" ],
                            [ { Name = "n"
                                Fn = Fuaran.Core.Count
                                Of = "id" }
                              { Name = "avg"
                                Fn = Fuaran.Core.Mean
                                Of = "total" } ]
                        ) ]

              Expect.equal (closedNames knowledge) [ "region"; "n"; "avg" ] "grouped names"
              Expect.equal (Schema.typeOf "n" knowledge) (Some Fuaran.Core.IntType) "Count is an int"
              Expect.equal (Schema.typeOf "avg" knowledge) (Some Fuaran.Core.FloatType) "Mean is a float"
          }

          test "CountDistinct stays an Int where the source column's type is not known" {
              // `Derive` leaves its column's type data-dependent, so the
              // aggregate here is typed by the ARM that ignores the source
              // type — which is a fact about the aggregate rather than a
              // fallback, and is exactly the arm this case was missing.
              let knowledge =
                  Schema.ofPipeline
                      SourceSchemas.none
                      embedded
                      [ Fuaran.Compute.Transform.Derive("tag", Fuaran.Compute.ColExpr.Col "region")
                        Fuaran.Compute.Transform.GroupBy(
                            [ "region" ],
                            [ { Name = "tags"
                                Fn = Fuaran.Core.CountDistinct
                                Of = "tag" } ]
                        ) ]

              Expect.isNone
                  (Schema.typeOf
                      "tag"
                      (Schema.ofPipeline
                          SourceSchemas.none
                          embedded
                          [ Fuaran.Compute.Transform.Derive("tag", Fuaran.Compute.ColExpr.Col "region") ]))
                  "the source column really is untyped, so the aggregate arm under test is the one that runs"

              Expect.equal (closedNames knowledge) [ "region"; "tags" ] "grouped names"
              Expect.equal (Schema.typeOf "tags" knowledge) (Some Fuaran.Core.IntType) "a distinct count is a count"
          }

          test "Window appends its output column, typed by the function" {
              let knowledge =
                  Schema.ofPipeline
                      SourceSchemas.none
                      embedded
                      [ Fuaran.Compute.Transform.Window
                            { PartitionBy = [ "region" ]
                              OrderBy = [ "total", Fuaran.Compute.Desc ]
                              Fn = Fuaran.Compute.RowNumber
                              Of = "total"
                              As = "rank" } ]

              Expect.equal (closedNames knowledge) [ "id"; "total"; "region"; "rank" ] "windowed names"
              Expect.equal (Schema.typeOf "rank" knowledge) (Some Fuaran.Core.IntType) "RowNumber is an int"
          }

          test "CompetitionRank is an Int, and its `of` column is never required" {
              // `of` names nothing at all here, deliberately. The evaluator's
              // own guard exempts the whole positional/ranking family from
              // resolving it, so a check that required it would refuse a
              // handler that runs perfectly well — which is what a wildcard
              // arm would have done the moment this case arrived.
              let spec: Fuaran.Compute.WindowSpec =
                  { PartitionBy = [ "region" ]
                    OrderBy = [ "total", Fuaran.Compute.Desc ]
                    Fn = Fuaran.Compute.CompetitionRank
                    Of = "no-such-column"
                    As = "place" }

              let knowledge =
                  Schema.ofPipeline SourceSchemas.none embedded [ Fuaran.Compute.Transform.Window spec ]

              Expect.equal (closedNames knowledge) [ "id"; "total"; "region"; "place" ] "windowed names"

              Expect.equal
                  (Schema.typeOf "place" knowledge)
                  (Some Fuaran.Core.IntType)
                  "SQL RANK() is an int, like the ranking family it joined"

              Expect.isEmpty
                  (Schema.readsOfTransform (Fuaran.Compute.Transform.Window spec))
                  "a ranking window is computed from the ORDER key alone"
          }

          test "an Except takes the left schema through, and a finding can name it" {
              let step = Fuaran.Compute.Transform.Except(Fuaran.Core.DataSource.Embedded orders)

              let knowledge = Schema.ofPipeline SourceSchemas.none embedded [ step ]

              Expect.equal
                  (closedNames knowledge)
                  [ "id"; "total"; "region" ]
                  "a multiset set-op drops ROWS; it touches no column"

              Expect.isEmpty (Schema.readsOfTransform step) "it keys on the whole row, not on a named column"
              Expect.equal (Schema.verbOf step) "Except" "and the verb has a name, so a finding can locate the step"
          }

          test "Unpivot closes to the id columns plus the melted pair" {
              let knowledge =
                  Schema.ofPipeline
                      SourceSchemas.none
                      embedded
                      [ Fuaran.Compute.Transform.Unpivot([ "id" ], [ "total" ]) ]

              Expect.equal (closedNames knowledge) [ "id"; "variable"; "value" ] "melted names"
              Expect.equal (Schema.typeOf "variable" knowledge) (Some Fuaran.Core.StringType) "the label is a string"
              Expect.equal (Schema.typeOf "value" knowledge) (Some Fuaran.Core.FloatType) "the value keeps its type"
          }

          test "a Join renames a colliding right column exactly as the evaluator does" {
              let right: Fuaran.Core.Table =
                  { Schema = [ "id", Fuaran.Core.IntType; "name", Fuaran.Core.StringType ]
                    Columns =
                      [ col "id" Fuaran.Core.IntType [ Fuaran.Core.Int 1 ]
                        col "name" Fuaran.Core.StringType [ Fuaran.Core.Str "a" ] ] }

              let knowledge =
                  Schema.ofPipeline
                      SourceSchemas.none
                      embedded
                      [ Fuaran.Compute.Transform.Join(
                            Fuaran.Core.DataSource.Embedded right,
                            [ "id", "id" ],
                            Fuaran.Compute.Inner
                        ) ]

              Expect.equal
                  (closedNames knowledge)
                  [ "id"; "total"; "region"; "id_right"; "name" ]
                  "the collision is suffixed, the rest is not"
          }

          test "a Pivot opens the set, and says why" {
              let knowledge =
                  Schema.ofPipeline
                      SourceSchemas.none
                      embedded
                      [ Fuaran.Compute.Transform.Pivot
                            { Index = [ "id" ]
                              On = "region"
                              Values = "total"
                              Agg = Fuaran.Core.Sum } ]

              Expect.isFalse (Schema.isClosed knowledge) "a pivot's value columns are data"
              Expect.equal (Schema.names knowledge) [ "id" ] "the index columns are still known"

              match knowledge with
              | SchemaKnowledge.AtLeast(_, reason) -> Expect.stringContains reason "pivot" "the reason names the verb"
              | SchemaKnowledge.Closed _ -> failtest "expected an open schema"
          }

          test "an undeclared Ref knows nothing; a declared one knows its schema" {
              let source = Fuaran.Core.DataSource.Ref "orders"

              let blind = Schema.ofSource SourceSchemas.none source
              Expect.isFalse (Schema.isClosed blind) "no declaration, no knowledge"
              Expect.equal (Schema.names blind) [] "and nothing invented"

              let declared =
                  Schema.ofSource (SourceSchemas.none |> SourceSchemas.declare "orders" ordersSchema) source

              Expect.equal (closedNames declared) [ "id"; "total"; "region" ] "declared schema"
          } ]

// ─── the reader walk ─────────────────────────────────────────────────

let private readerTests =
    testList
        "QuerySchema.readersOfTree"
        [ test "a grid's fields and a chart's axes are both read off the wire" {
              let readers =
                  QuerySchema.readersOfTree (
                      page
                          [ grid "grid" "orders" [ Some "id"; Some "total" ]
                            chart "chart" "summary" "region" [ "total" ] ]
                  )

              let bySlot slot =
                  readers |> List.find (fun r -> r.Slot = slot)

              Expect.equal (bySlot "orders").Fields [ "id"; "total" ] "grid columns"
              Expect.equal (bySlot "orders").NodeId "grid" "the reading node is named"
              Expect.equal (bySlot "summary").Fields [ "region"; "total" ] "chart axes"
              Expect.isFalse ((bySlot "orders").ClosureHeld) "every column names a field"
          }

          test "a field-less grid column marks the reader's expectation a lower bound" {
              let readers =
                  QuerySchema.readersOfTree (page [ grid "grid" "orders" [ Some "id"; None ] ])

              let reader = List.exactlyOne readers
              Expect.equal reader.Fields [ "id" ] "only the declared field is visible"
              Expect.isTrue reader.ClosureHeld "the closure column is reported, not guessed at"
          }

          test "a node bound to something other than a query slot is not a reader" {
              let node = Fuaran.markdown "grid" "placeholder"

              let unbound =
                  { node with
                      Kind =
                          NodeKind.DataGrid
                              { Columns = [ gridColumn (Some "id") ]
                                Editable = false
                                RowKey = None
                                RowKeyField = None
                                SortStateKey = None
                                PageSize = None
                                PageStateKey = None
                                DefaultSort = None
                                EditStateKey = None
                                Reorderable = false
                                TransferInKey = None
                                TransferOutKey = None
                                KeepRowsTogether = false
                                RepeatHeader = false
                                Exportable = false
                                WindowStateKey = None
                                RowTotal = None
                                Source = Binding.Static None
                                StaticRows = None
                                OnRowClick = None } }

              Expect.isEmpty (QuerySchema.readersOfTree (page [ unbound ])) "a static grid demands nothing of a query"
          } ]

// ─── the refusal, and the runtime posture it does not change ─────────

/// A pipeline that keeps only `id` — so a reader wanting `total` cannot be
/// satisfied, and nothing in the pipeline itself is wrong.
let private dropsTotal = [ Fuaran.Compute.Transform.Project [ "id", "id" ] ]

/// A pipeline that names a column its source does not carry — the runtime's
/// `UnknownColumn`, waiting to happen.
let private readsMissing = [ Fuaran.Compute.Transform.Project [ "missing", "x" ] ]

let private refusalTests =
    testList
        "ServerSession.initStrict"
        [ test "a query that drops a column its reader needs is refused, with the column named" {
              let tree = page [ grid "grid" "orders" [ Some "id"; Some "total" ] ]
              let services = servicesWith (handlerWith "orders" "orders" dropsTotal)

              match ServerSession.initStrict coversEverything services empty (wireOf tree) with
              | Ok _ -> failtest "expected the unsatisfiable handler to be refused"
              | Error findings ->
                  let missing =
                      findings
                      |> List.choose (fun f ->
                          match f with
                          | ServerStrictFinding.QuerySchema(QuerySchemaFinding.ReaderColumnMissing(origin,
                                                                                                   reader,
                                                                                                   column,
                                                                                                   available)) ->
                              Some(origin, reader, column, available)
                          | _ -> None)

                  let origin, reader, column, available = List.exactlyOne missing
                  Expect.equal origin.Slot "orders" "the query is named"
                  Expect.equal origin.Handler "orders" "the handler is named"
                  Expect.equal reader "grid" "the reader is named"
                  Expect.equal column "total" "the missing column is named"
                  Expect.equal available [ "id" ] "and so is what the query does provide"

                  let described = ServerStrictFinding.describe (List.exactlyOne findings)
                  Expect.stringContains described "total" "the description carries the column name"
                  Expect.stringContains described "grid" "and the reader"
          }

          test "the same tree over a query that provides the column constructs" {
              let tree = page [ grid "grid" "orders" [ Some "id"; Some "total" ] ]

              let services =
                  servicesWith (
                      handlerWith
                          "orders"
                          "orders"
                          [ Fuaran.Compute.Transform.Project [ "id", "id"; "total", "total" ] ]
                  )

              match ServerSession.initStrict coversEverything services empty (wireOf tree) with
              | Ok _ -> ()
              | Error findings ->
                  failtestf
                      "expected construction to succeed, got %A"
                      (findings |> List.map ServerStrictFinding.describe)
          }

          test "a chart's axes are checked the same way a grid's columns are" {
              let tree = page [ chart "chart" "orders" "region" [ "total" ] ]
              let services = servicesWith (handlerWith "orders" "orders" dropsTotal)

              match ServerSession.initStrict coversEverything services empty (wireOf tree) with
              | Ok _ -> failtest "expected the chart's axes to be checked"
              | Error findings ->
                  let columns =
                      findings
                      |> List.choose (fun f ->
                          match f with
                          | ServerStrictFinding.QuerySchema(QuerySchemaFinding.ReaderColumnMissing(_, _, column, _)) ->
                              Some column
                          | _ -> None)

                  Expect.equal (List.sort columns) [ "region"; "total" ] "both axes are reported, not just the first"
          }

          test "a pipeline that cannot run against its own source is refused with the column named" {
              // No reader at all: the defect is in the host's registration, and
              // a tree that never calls the handler does not make it correct.
              let tree = page [ Fuaran.markdown "text" "nothing reads a query here" ]
              let services = servicesWith (handlerWith "orders" "orders" readsMissing)

              match ServerSession.initStrict coversEverything services empty (wireOf tree) with
              | Ok _ -> failtest "expected the unrunnable pipeline to be refused"
              | Error findings ->
                  match findings with
                  | [ ServerStrictFinding.QuerySchema(QuerySchemaFinding.UnknownColumn(origin,
                                                                                       step,
                                                                                       verb,
                                                                                       column,
                                                                                       available)) ] ->
                      Expect.equal origin.Slot "orders" "the query is named"
                      Expect.equal step 0 "the step is located"
                      Expect.equal verb "Project" "the verb is named"
                      Expect.equal column "missing" "the column is named — the whole point of this side"
                      Expect.equal available [ "id"; "total"; "region" ] "and what was available"
                  | other -> failtestf "expected exactly one UnknownColumn finding, got %A" other
          }

          test "a Union whose sides disagree is refused before it runs" {
              let reordered: Fuaran.Core.Table =
                  { Schema =
                      [ "total", Fuaran.Core.FloatType
                        "id", Fuaran.Core.IntType
                        "region", Fuaran.Core.StringType ]
                    Columns =
                      [ col "total" Fuaran.Core.FloatType [ Fuaran.Core.Float 1.0 ]
                        col "id" Fuaran.Core.IntType [ Fuaran.Core.Int 9 ]
                        col "region" Fuaran.Core.StringType [ Fuaran.Core.Str "n" ] ] }

              let tree = page [ Fuaran.markdown "text" "no reader" ]

              let services =
                  servicesWith (
                      handlerWith
                          "orders"
                          "orders"
                          [ Fuaran.Compute.Transform.Union(Fuaran.Core.DataSource.Embedded reordered) ]
                  )

              match ServerSession.initStrict coversEverything services empty (wireOf tree) with
              | Ok _ -> failtest "the evaluator compares ORDERED name lists, so this is a refusal"
              | Error findings ->
                  match findings with
                  | [ ServerStrictFinding.QuerySchema(QuerySchemaFinding.UnionColumnMismatch(_, _, left, right)) ] ->
                      Expect.equal left [ "id"; "total"; "region" ] "left names"
                      Expect.equal right [ "total"; "id"; "region" ] "right names, in the order that breaks it"
                  | other -> failtestf "expected exactly one UnionColumnMismatch, got %A" other
          } ]

// ─── honesty: what it declines to decide, it does not refuse ─────────

let private honestyTests =
    testList
        "QuerySchema honesty"
        [ test "an open schema refuses nothing, and reports itself underivable" {
              let tree = page [ grid "grid" "orders" [ Some "north" ] ]

              let services =
                  servicesWith (
                      handlerWith
                          "orders"
                          "orders"
                          [ Fuaran.Compute.Transform.Pivot
                                { Index = [ "id" ]
                                  On = "region"
                                  Values = "total"
                                  Agg = Fuaran.Core.Sum } ]
                  )

              // `north` is a pivoted column name: it exists only if the data
              // carries that region. Refusing it would refuse a correct handler.
              let report = ServerSession.querySchemaReport services (wireOf tree)
              Expect.isEmpty report.Findings "an unrefutable expectation is not a finding"

              Expect.equal
                  (report.Underivable |> List.map fst |> List.map _.Slot)
                  [ "orders" ]
                  "reported as data instead"

              match ServerSession.initStrict coversEverything services empty (wireOf tree) with
              | Ok _ -> ()
              | Error findings -> failtestf "strict construction must not refuse on ignorance: %A" findings
          }

          test "an undeclared Ref source is underivable; declaring it restores the check" {
              let tree = page [ grid "grid" "orders" [ Some "total" ] ]

              let handler =
                  { Name = "orders"
                    Stages =
                      [ Effect(
                            ServerEffect.RunQuery(
                                "orders",
                                Fuaran.Core.DataSource.Ref "orders",
                                [ Fuaran.Compute.Transform.Project [ "id", "id" ] ]
                            )
                        ) ] }

              let blind =
                  ServerServices.createPermissive |> ServerServices.withHandler "/orders" handler

              // A `Project` closes the set whatever its input knew, so the
              // reader check still bites — the Ref's ignorance costs the STEP
              // check, not the reader one.
              let blindReport = ServerSession.querySchemaReport blind (wireOf tree)

              Expect.equal (blindReport.Findings |> List.length) 1 "the reader is still checkable through a projection"

              let declared = blind |> ServerServices.withSourceSchema "orders" ordersSchema
              let declaredReport = ServerSession.querySchemaReport declared (wireOf tree)
              Expect.isEmpty declaredReport.Underivable "a declared source closes the walk"
          }

          test "a closure-projecting reader is reported, never refused" {
              let tree = page [ grid "grid" "orders" [ Some "id"; None ] ]

              let services =
                  servicesWith (handlerWith "orders" "orders" [ Fuaran.Compute.Transform.Project [ "id", "id" ] ])

              let report = ServerSession.querySchemaReport services (wireOf tree)
              Expect.isEmpty report.Findings "the declared field is satisfied"
              Expect.equal (report.OpaqueReaders |> List.map snd) [ "grid" ] "the closure column is reported as data"
          } ]

// ─── the runtime posture, unchanged ──────────────────────────────────

let private runtimePostureTests =
    testList
        "the runtime stays discriminator-only"
        [ test "the same broken pipeline, RUN rather than checked, still names no column" {
              let node = Fuaran.markdown "text" "placeholder"

              let tree =
                  page
                      [ Fuaran.button
                            "call"
                            { Defaults.button<obj> with
                                Label = TextSource.Literal "call"
                                OnClick = Action.Call("/orders", None, None) }
                        node ]

              let services = servicesWith (handlerWith "orders" "orders" readsMissing)
              let session = ServerSession.init services empty (wireOf tree)

              let _, output =
                  ServerSession.step
                      session
                      { ConnId = "server"
                        NodeId = "call"
                        Event = "click"
                        Payload = Map.empty
                        LastSeq = 0 }

              Expect.isFalse output.Committed "the handler halted"

              let failures =
                  output.Diagnostics
                  |> List.choose (fun d ->
                      match d with
                      | ServerDiagnostic.Failed(capability, reason) -> Some(capability, reason)
                      | _ -> None)

              let capability, reason = List.exactlyOne failures
              Expect.equal capability "RunQuery" "the capability is named — it is host-declared"
              Expect.equal reason "UnknownColumn" "the DISCRIMINATOR, and nothing else"
              Expect.isFalse (reason.Contains "missing") "the column name must not reach a runtime diagnostic"
          } ]

[<Tests>]
let tests =
    testList
        "query schema"
        [ derivationTests
          readerTests
          refusalTests
          honestyTests
          runtimePostureTests ]
