module Fuaran.UI.Tests.Derivation

// ============================================================================
//  Phase 2749 — a derivation trace shows how a bound number was computed.
//
//  The claims, each stated against the corpus, the resolver or the rendered
//  bytes rather than against the implementation:
//
//   1. For every pipeline in the transform law corpus (`laws/transform-laws.json`
//      and `laws/dataframe-parity.json`), the trace's final table IS the
//      reference evaluation — byte for byte as the codec encodes it, or a
//      refusal where the reference refused — and its final schema is the
//      static walk's over the whole pipeline. Every step's table is the
//      reference over that step's prefix.
//   2. Through the resolver, the trace's last table is the binding's RESOLVED
//      value: the rows a grid slot shows, the cell a metric shows, with scalar
//      and list params bound and an unbound filter pruned (and reported so).
//   3. An open schema is reported with the walk's own reason.
//   4. A binding with no pipeline is refused with where its value comes from.
//   5. The introspection tool answers the trace, and refuses didactically.
//   6. With the policy off a tree renders byte-identically to today; on, the
//      affordance wraps the value's own markup unchanged.
// ============================================================================

open System
open System.IO
open System.Text.Json
open Expecto
open Fuaran.Core
open Fuaran.Compute
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Derivation
open Fuaran.UI.Renderer

// ─── The corpus ────────────────────────────────────────────────────────────

type private Vector =
    { Id: string
      Source: Table
      Pipeline: Fuaran.Compute.Transform list }

let private text (e: JsonElement) (name: string) : string =
    match e.GetProperty(name).GetString() with
    | null -> failwithf "corpus member '%s' is null" name
    | s -> s

let private vectorsOf (root: string) (file: string) : Vector list =
    use doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "laws", file)))

    [ for v in doc.RootElement.GetProperty("vectors").EnumerateArray() do
          if text v "case" = "evalPipeline" then
              let id = text v "id"
              let input = v.GetProperty "input"

              let source =
                  match ColumnCodec.decode (text input "source") with
                  | Ok(Embedded t) -> t
                  | other -> failwithf "%s: the source is not an embedded table: %A" id other

              let pipeline =
                  match DataFrameCodec.decodePipeline (text input "pipeline") with
                  | Ok p -> p
                  | Error e -> failwithf "%s: the pipeline did not decode: %A" id e

              { Id = file + ":" + id
                Source = source
                Pipeline = pipeline } ]

let private withVectors (f: Vector list -> unit) () =
    match Fuaran.Tests.CorpusRoot.tryFind () with
    | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
    | Some root ->
        match vectorsOf root "transform-laws.json" @ vectorsOf root "dataframe-parity.json" with
        | [] -> failtest "the law corpus holds no evalPipeline vector: the trace certified nothing"
        | vs -> f vs

let private encoded (table: Table) : string = ColumnCodec.encode (Embedded table)

let private rowsBinding (source: TransformSource) (pipeline: Fuaran.Compute.Transform list) params' : Binding<Row seq> =
    Binding.Transform(source, pipeline, params')

let private all = Some Int32.MaxValue

let private traceOk (traced: Result<DerivationTrace, DerivationRefusal>) : DerivationTrace =
    match traced with
    | Ok t -> t
    | Error refusal -> failtestf "expected a trace, got %A" refusal

let private finalTable (trace: DerivationTrace) : StageTable =
    match Derivation.result trace with
    | Some(Ok t) -> t
    | other -> failtestf "expected a final table, got %A" other

// ─── Fixtures ──────────────────────────────────────────────────────────────

/// A boxed store value: F# 10 types `box _` as `obj | null`, and a store holds
/// non-null values (the `UnreferencedSurfaceTests` helper).
let private bx (v: 'a) : obj = box v |> Unchecked.nonNull

let private table (wire: string) : Table =
    match ColumnCodec.decode wire with
    | Ok(Embedded t) -> t
    | other -> failwithf "fixture table did not decode: %A" other

/// Five rows: g (string), v (int), w (float).
let private sales: Table =
    table
        """{"columns":{"g":{"validity":[true,true,true,true,true],"values":["a","b","c","a","b"]},"v":{"validity":[true,true,true,true,true],"values":[1,2,3,4,5]},"w":{"validity":[true,true,true,true,true],"values":[0.5,1.5,2.5,3.5,4.5]}},"schema":[{"name":"g","type":"string"},{"name":"v","type":"int"},{"name":"w","type":"float"}]}"""

let private stateParam (name: string) : TransformParam =
    { Name = name
      From = Binding.State(name, None) }

/// v > $min, g in $groups, w > $unset (never bound, so pruned), then a sum per g.
let private parameterised: Fuaran.Compute.Transform list =
    [ Filter(Binary(Gt, Col "v", Param "min"))
      Filter(InParam(Col "g", "groups"))
      Filter(Binary(Gt, Col "w", Param "unset"))
      GroupBy([ "g" ], [ { Name = "total"; Fn = Sum; Of = "v" } ])
      Sort [ Slot.Lit "g", Asc ] ]

let private parameterisedSources: BindingSources =
    { BindingSources.empty with
        State = Map.ofList [ "min", bx 1; "groups", bx [ bx "a"; bx "b" ] ] }

let private resolvedRows (sources: BindingSources) (binding: Binding<Row seq>) : Row list =
    match BindingResolver.resolve<Row seq> sources binding with
    | BindingResolver.Resolved rows -> List.ofSeq rows
    | other -> failtestf "the binding did not resolve to rows: %A" other

// ─── Tests ─────────────────────────────────────────────────────────────────

[<Tests>]
let corpusTests =
    testList
        "Phase 2749 — the trace over the transform law corpus"
        [ testCase "every corpus pipeline: the trace's final table is the reference evaluation"
          <| withVectors (fun vs ->
              for v in vs do
                  let binding = rowsBinding (TransformSource.Data(Embedded v.Source)) v.Pipeline None

                  let trace =
                      traceOk (Derivation.trace all (DerivationSources.ofTable v.Source) binding)

                  Expect.equal (List.length trace.Steps) (List.length v.Pipeline) (v.Id + ": one entry per step")

                  match DataFrame.evalPipelineInEnv Map.empty v.Pipeline v.Source, Derivation.result trace with
                  | Ok reference, Some(Ok final) ->
                      Expect.equal (encoded final.Rows) (encoded reference) (v.Id + ": the final table")
                      Expect.equal final.RowCount (Table.rowCount reference) (v.Id + ": the row count")
                  | Error _, Some(Error _) -> ()
                  | reference, final -> failtestf "%s: the reference %A and the trace %A disagree" v.Id reference final

                  match List.tryLast trace.Steps with
                  | Some last ->
                      Expect.equal
                          last.Output.Schema
                          (SchemaWalk.ofPipeline v.Source.Schema v.Pipeline)
                          (v.Id + ": the final schema is the walk's over the whole pipeline")
                  | None -> ())

          testCase "every corpus pipeline: each step's table is the reference over its prefix"
          <| withVectors (fun vs ->
              for v in vs do
                  let binding = rowsBinding (TransformSource.Data(Embedded v.Source)) v.Pipeline None

                  let trace =
                      traceOk (Derivation.trace all (DerivationSources.ofTable v.Source) binding)

                  for step in trace.Steps do
                      let prefix = List.take (step.Index + 1) v.Pipeline

                      match DataFrame.evalPipelineInEnv Map.empty prefix v.Source, step.Output.Table with
                      | Ok reference, Some(Ok at) ->
                          Expect.equal (encoded at.Rows) (encoded reference) (sprintf "%s: step %d" v.Id step.Index)
                      | Error _, Some(Error _) -> ()
                      | reference, at ->
                          failtestf
                              "%s: step %d — the reference %A and the trace %A disagree"
                              v.Id
                              step.Index
                              reference
                              at) ]

[<Tests>]
let traceTests =
    testList
        "Phase 2749 — Derivation.trace"
        [ test "through the resolver, the last step's table is the rows the slot resolves to" {
              let binding =
                  rowsBinding
                      (TransformSource.Data(Embedded sales))
                      parameterised
                      (Some [ stateParam "min"; stateParam "groups"; stateParam "unset" ])

              let trace =
                  traceOk (BindingResolver.derivationTrace all parameterisedSources binding)

              let final = finalTable trace
              let slot = resolvedRows parameterisedSources binding

              let ofFinal =
                  resolvedRows BindingSources.empty (rowsBinding (TransformSource.Data(Embedded final.Rows)) [] None)

              Expect.equal ofFinal slot "the trace ends on the slot's own rows"
              Expect.equal (List.length slot) 2 "a and b, the rows above min, summed"

              Expect.equal
                  (trace.Steps |> List.map _.Status)
                  [ StepStatus.Applied
                    StepStatus.Applied
                    StepStatus.Pruned [ "unset" ]
                    StepStatus.Applied
                    StepStatus.Applied ]
                  "the filter reading the unbound param is reported pruned"

              Expect.equal
                  (trace.Steps |> List.map _.Verb)
                  [ "filter"; "filter"; "filter"; "groupBy"; "sort" ]
                  "each step names its wire verb"

              Expect.equal
                  trace.Steps[2].Output
                  trace.Steps[1].Output
                  "a pruned step changes nothing: what follows it is what preceded it"
          }

          test "a live source: the trace reads the store's current rows, as the slot does" {
              let live = Binding.State("rows", None)

              let rows =
                  [ Map.ofList [ "g", bx "x"; "v", bx 7 ]; Map.ofList [ "g", bx "y"; "v", bx 9 ] ]

              let sources =
                  { BindingSources.empty with
                      State = Map.ofList [ "rows", bx rows ] }

              let binding =
                  rowsBinding
                      (TransformSource.Live(live, Embedded Table.empty))
                      [ Filter(Binary(Gt, Col "v", Lit(Int 8))) ]
                      None

              let final =
                  finalTable (traceOk (BindingResolver.derivationTrace all sources binding))

              Expect.equal
                  (resolvedRows BindingSources.empty (rowsBinding (TransformSource.Data(Embedded final.Rows)) [] None))
                  (resolvedRows sources binding)
                  "the trace ends on the slot's own rows"
          }

          test "an Expr: the trace's 1x1 table is the cell the metric shows" {
              let binding: Binding<float> =
                  Binding.Expr(Binary(Mul, Param "rate", Lit(Float 2.0)), Some [ stateParam "rate" ])

              let sources =
                  { BindingSources.empty with
                      State = Map.ofList [ "rate", bx 1.25 ] }

              let trace = traceOk (BindingResolver.derivationTrace all sources binding)
              Expect.equal trace.Subject DerivationSubject.Expr "an Expr trace"

              let final = finalTable trace

              let cell =
                  Table.tryColumn "__value" final.Rows
                  |> Option.map (Column.cell 0)
                  |> Option.defaultWith (fun () -> failtest "no result column")

              match BindingResolver.resolveScalarWithField BindingResolver.cellToFloat sources binding with
              | BindingResolver.Resolved shown, _ ->
                  Expect.equal (BindingResolver.cellToFloat cell) (Ok shown) "the trace ends on the metric's value"
              | other, _ -> failtestf "the metric did not resolve: %A" other
          }

          test "an open schema is reported with the walk's own reason" {
              let pipeline = [ Join(Ref "targets", [ "g", "g" ], Inner); Project [ "g", "g" ] ]

              let binding = rowsBinding (TransformSource.Data(Embedded sales)) pipeline None
              let trace = traceOk (Derivation.trace all (DerivationSources.ofTable sales) binding)
              let join = trace.Steps[0].Output.Schema

              Expect.isFalse (SchemaWalk.isClosed join) "a Ref with no declared schema opens the walk"

              Expect.equal
                  (SchemaWalk.reason join)
                  (SchemaWalk.reason (SchemaWalk.ofPipeline sales.Schema [ List.head pipeline ]))
                  "the reason is the walk's, not a paraphrase"

              Expect.stringContains (SchemaWalk.reason join |> Option.defaultValue "") "targets" "it names the source"

              match trace.Steps[0].Output.Table with
              | Some(Error refusal) -> Expect.isNonEmpty refusal "the evaluator's refusal is carried"
              | other -> failtestf "an unresolved Ref is refused at evaluation: %A" other

              match trace.Steps[1].Output.Table with
              | Some(Error refusal) -> Expect.stringContains refusal "step 0" "a later step names the refused one"
              | other -> failtestf "a step after a refusal is not evaluated: %A" other
          }

          test "a resolver that declares the Ref closes the schema and evaluates the join" {
              let targets =
                  table
                      """{"columns":{"g":{"validity":[true],"values":["a"]},"goal":{"validity":[true],"values":[10]}},"schema":[{"name":"g","type":"string"},{"name":"goal","type":"int"}]}"""

              let sources =
                  { DerivationSources.ofTable sales with
                      Resolve =
                          fun name ->
                              if name = "targets" then
                                  Ok targets
                              else
                                  DataFrame.noResolve name }

              let pipeline = [ Join(Ref "targets", [ "g", "g" ], Inner) ]
              let binding = rowsBinding (TransformSource.Data(Embedded sales)) pipeline None
              let trace = traceOk (Derivation.trace all sources binding)

              Expect.isTrue (SchemaWalk.isClosed trace.Steps[0].Output.Schema) "the declared Ref closes the walk"
              Expect.equal (finalTable trace).RowCount 2 "the two 'a' rows join"
          }

          test "a definition later in the pipeline reaches a call before it" {
              let pipeline =
                  [ Derive("twice", Call("double", [ Col "v" ]))
                    Define
                        { Name = "double"
                          Params = [ { Name = "x"; Default = None } ]
                          Body = Binary(Mul, Arg "x", Lit(Int 2)) } ]

              let binding = rowsBinding (TransformSource.Data(Embedded sales)) pipeline None
              let trace = traceOk (Derivation.trace all (DerivationSources.ofTable sales) binding)

              match trace.Steps[0].Output.Table, DataFrame.evalPipeline pipeline sales with
              | Some(Ok first), Ok whole ->
                  Expect.equal (encoded first.Rows) (encoded whole) "the derive evaluates under the later definition"
              | first, whole -> failtestf "the call did not reach its definition: %A / %A" first whole
          }

          test "tables are bounded by the caller's row limit, and absent when not asked for" {
              let binding =
                  rowsBinding (TransformSource.Data(Embedded sales)) [ Sort [ Slot.Lit "v", Desc ] ] None

              let bounded =
                  traceOk (Derivation.trace (Some 2) (DerivationSources.ofTable sales) binding)

              let final = finalTable bounded
              Expect.equal final.RowCount 5 "the full count is reported"
              Expect.equal (Table.rowCount final.Rows) 2 "two rows are carried"

              let schemaOnly =
                  traceOk (Derivation.trace None (DerivationSources.ofTable sales) binding)

              Expect.isTrue
                  (schemaOnly.Input.Table.IsNone
                   && schemaOnly.Steps |> List.forall (fun s -> s.Output.Table.IsNone))
                  "the schema half evaluates nothing"

              Expect.equal
                  (schemaOnly.Steps |> List.map _.Output.Schema)
                  (bounded.Steps |> List.map _.Output.Schema)
                  "the schema half is the same with or without tables"
          }

          test "a binding with no pipeline is refused with where its value comes from" {
              let binding: Binding<float> = Binding.State("revenue", None)

              match Derivation.trace all (DerivationSources.ofTable sales) binding with
              | Error(DerivationRefusal.NoPipeline(case, source) as refusal) ->
                  Expect.equal case "State" "the refusal names the binding's case"
                  Expect.stringContains source "revenue" "and where its value comes from"
                  Expect.stringContains (DerivationRefusal.describe refusal) "getBindingValue" "and what to use instead"
              | other -> failtestf "a State binding has no derivation: %A" other
          }

          test "the effective pipeline is the resolver's: the pruned filter is gone" {
              Expect.equal
                  (Derivation.effectivePipeline Map.empty (Set.ofList [ "unset" ]) parameterised)
                  (parameterised |> List.filter (fun s -> s <> parameterised[2]))
                  "exactly the filter reading the unbound param is dropped"
          } ]

// ─── The introspection tool ────────────────────────────────────────────────

let private metricNode (binding: Binding<float>) : Node<obj> =
    Fuaran.metric
        "total"
        { Defaults.metric with
            Label = TextSource.Literal "Total"
            Value = binding }

/// A metric over the sales table: the sum of v.
let private totalBinding: Binding<float> =
    Binding.Transform(
        TransformSource.Data(Embedded sales),
        [ GroupBy([], [ { Name = "total"; Fn = Sum; Of = "v" } ]) ],
        None
    )

let private toolCtx: Fuaran.UI.AiTools.Seams.IntrospectionContext =
    Fuaran.UI.AiTools.Seams.emptyContext

let private args (members: (string * JVal) list) = JObj members

[<Tests>]
let toolTests =
    testList
        "Phase 2749 — fuaran.getDerivationTrace"
        [ test "the tool is named and its schema requires the node and the slot" {
              Expect.equal Fuaran.UI.AiTools.Tools.DerivationTraceToolName "fuaran.getDerivationTrace" "name"

              Expect.stringContains
                  (Json.render Fuaran.UI.AiTools.Tools.derivationTraceInputSchema)
                  "\"required\":[\"id\",\"slot\"]"
                  "id and slot are required"
          }

          test "a pipeline slot answers its trace, ending on the slot's value" {
              let root = metricNode totalBinding

              match Fuaran.UI.AiTools.Tools.getDerivationTrace toolCtx root (NodeId "total") "Value" all with
              | Ok(Ok trace) ->
                  let final = finalTable trace

                  Expect.equal
                      (Table.tryColumn "total" final.Rows
                       |> Option.map (Column.cell 0 >> BindingResolver.cellToFloat))
                      (Some(Ok 15.0))
                      "1 + 2 + 3 + 4 + 5"
              | other -> failtestf "expected a trace: %A" other

              match
                  Fuaran.UI.AiTools.Tools.derivationTrace
                      toolCtx
                      root
                      (args [ "id", JStr "total"; "slot", JStr "Value"; "rows", JInt 1 ])
              with
              | Ok json ->
                  Expect.stringContains json "\"verb\":\"groupBy\"" "the step's verb"
                  Expect.stringContains json "\"step\":{\"$type\":\"groupBy\"" "its canonical wire form"
                  Expect.stringContains json "\"closed\":true" "its schema"
                  Expect.stringContains json "\"rowCount\":5" "the input's table"
              | Error e -> failtestf "the tool refused: %s" e
          }

          test "a slot with no pipeline is refused didactically, by code" {
              let root = metricNode (Binding.Static(Some 3.0))

              match
                  Fuaran.UI.AiTools.Tools.derivationTrace
                      toolCtx
                      root
                      (args [ "id", JStr "total"; "slot", JStr "Value" ])
              with
              | Error e ->
                  Expect.stringContains e "\"code\":\"no-pipeline\"" "the code"
                  Expect.stringContains e "Binding.Static" "names the binding"
                  Expect.stringContains e "getBindingValue" "and what to use instead"
              | Ok json -> failtestf "a Static slot has no derivation: %s" json

              match
                  Fuaran.UI.AiTools.Tools.derivationTrace
                      toolCtx
                      root
                      (args [ "id", JStr "total"; "slot", JStr "Trend" ])
              with
              | Error e -> Expect.stringContains e "\"code\":\"absent\"" "an optional slot holding nothing"
              | Ok json -> failtestf "an absent Trend has no derivation: %s" json
          }

          test "addressing and argument errors are refused by code" {
              let root = metricNode totalBinding

              let call members =
                  Fuaran.UI.AiTools.Tools.derivationTrace toolCtx root (args members)

              let code (r: Result<string, string>) =
                  match r with
                  | Error e -> e
                  | Ok json -> failtestf "expected a refusal: %s" json

              Expect.stringContains (code (call [ "id", JStr "nope"; "slot", JStr "Value" ])) "node-not-found" "node"
              Expect.stringContains (code (call [ "id", JStr "total"; "slot", JStr "Columns" ])) "slot-not-found" "slot"
              Expect.stringContains (code (call [ "id", JStr "total" ])) "invalid-arguments" "slot required"

              Expect.stringContains
                  (code (call [ "id", JStr "total"; "slot", JStr "Value"; "rows", JInt -1 ]))
                  "invalid-arguments"
                  "rows non-negative"

              Expect.stringContains
                  (code (call [ "id", JStr "total"; "slot", JStr "Value"; "extra", JBool true ]))
                  "invalid-arguments"
                  "unknown argument"
          } ]

// ─── The renderer affordance ───────────────────────────────────────────────

let private render (policy: RenderPolicy) (node: Node<obj>) : string =
    Fuaran.UI.Renderer.Server.Render.renderWithPolicy
        policy
        Fuaran.UI.Renderer.Server.Registry.empty
        BindingResolver.empty
        node

let private showing =
    { RenderPolicy.defaults with
        DerivationTrace = DerivationTracePolicy.Show 3 }

/// `on` with the affordance cut out: the host element's open tag, the
/// disclosure, and the host's close tag.
let private withoutAffordance (on: string) : string =
    let hostOpen = "<div class=\"fuaran-derivation-host\">"
    let i = on.IndexOf(hostOpen, StringComparison.Ordinal)

    let j =
        on.IndexOf("<details class=\"fuaran-derivation\">", StringComparison.Ordinal)

    let k = on.IndexOf("</details></div>", StringComparison.Ordinal)

    if i < 0 || j < 0 || k < 0 then
        failtestf "the affordance is not in the markup: %s" on

    on.Substring(0, i)
    + on.Substring(i + hostOpen.Length, j - i - hostOpen.Length)
    + on.Substring(k + "</details></div>".Length)

[<Tests>]
let affordanceTests =
    testList
        "Phase 2749 — the derivation affordance"
        [ test "the policy is off by default, and off plans nothing" {
              Expect.equal RenderPolicy.defaults.DerivationTrace DerivationTracePolicy.Off "off by default"

              let offered: ElementPlan.Plan<unit> option =
                  ElementPlan.DerivationPlan.forBinding RenderPolicy.defaults BindingResolver.empty totalBinding

              Expect.isNone offered "a pipeline binding offers nothing under the default"

              let inner: ElementPlan.Plan<unit> = ElementPlan.Plan.classText "div" "x" "y"

              Expect.isTrue
                  (obj.ReferenceEquals(ElementPlan.DerivationPlan.around None inner, inner))
                  "no affordance returns the value's own plan, the very same value"
          }

          test "off, a tree renders byte-identically to today" {
              let tree = metricNode totalBinding

              Expect.equal
                  (render RenderPolicy.defaults tree)
                  (Fuaran.UI.Renderer.Server.Render.render BindingResolver.empty tree)
                  "the policy entry at its default is the ordinary render"

              Expect.isFalse
                  ((render RenderPolicy.defaults tree).Contains("fuaran-derivation", StringComparison.Ordinal))
                  "no affordance class is emitted"
          }

          test "on, the affordance wraps the metric's own markup, unchanged" {
              let tree = metricNode totalBinding
              let off = render RenderPolicy.defaults tree
              let on = render showing tree

              Expect.notEqual on off "the control: the probe sees the affordance"
              Expect.equal (withoutAffordance on) off "cut out, what remains is the off render byte for byte"

              Expect.stringContains
                  on
                  "<summary class=\"fuaran-derivation-toggle\">How this was computed</summary>"
                  "labelled toggle"

              Expect.stringContains on "role=\"group\" aria-label=\"Derivation\"" "a labelled panel"
              Expect.stringContains on "<span class=\"fuaran-derivation-verb\">groupBy</span>" "the step's verb"
              Expect.stringContains on "fuaran-derivation-wire" "its canonical wire form"
              Expect.stringContains on "5 rows; the first 3 shown" "the input table, bounded by the policy"
          }

          test "on, a binding with no pipeline renders exactly as off" {
              let tree = metricNode (Binding.Static(Some 4.0))
              Expect.equal (render showing tree) (render RenderPolicy.defaults tree) "nothing to trace, nothing offered"
          }

          test "on, a pipeline whose sources do not resolve offers the reason" {
              let unresolved: Binding<float> =
                  Binding.Transform(TransformSource.Data(Ref "missing"), [ Distinct ], None)

              let on = render showing (metricNode unresolved)
              Expect.stringContains on "fuaran-derivation-refusal" "the refusal is shown"
              Expect.stringContains on "The derivation could not be traced" "in a sentence"
          } ]

// ─── Phase 2767 — the derivation trace as a law family ─────────────────────
//
// `laws/derivation-trace-laws.json` (WIRE_FORMAT §35): one vector per
// `evalPipeline` vector of the transform law corpus (`transform-laws.json` and
// `dataframe-parity.json`), traced over its own source with the tables bounded
// at `drawnRows`, then the authored cases a drawn pipeline cannot reach: bound
// scalar and list params beside a pruned filter, an `Expr`, an open schema
// whose evaluation is refused, and a schema-only trace. Each vector's
// `expected.trace` is `Derivation.encode` of the reference trace.
//
// Three claims below. The COMMITTED file is byte-identical to a fresh emission.
// Every committed vector, read back from the FILE, traces to its expected text
// (the file, never the in-memory list, because the file is what another host
// reads). And the codec round-trips every expected text byte for byte, and
// refuses what is not a trace.

/// Where the family lives in the corpus, and the command that writes it.
module DerivationLaws =

    let directory = "laws"
    let fileName = "derivation-trace-laws.json"
    let familyId = "derivationTraceLaws"

    let emitCommand =
        "dotnet run --project src/Fuaran.UI.Tests -- --emit-derivation-trace <corpus dir>"

    /// The row limit every drawn vector traces at: small enough that most
    /// tables are bounded, so the bound is certified, not only the evaluation.
    let drawnRows = 3

    type Input =
        {
            Subject: DerivationSubject
            /// The pipeline's canonical wire text, for a `transform` subject.
            Pipeline: string option
            /// The expression's canonical wire text, for an `expr` subject.
            Expr: string option
            /// The input table's canonical wire text, for a `transform` subject.
            Source: string option
            Env: Map<string, Cell>
            Lists: Map<string, Cell list>
            Unbound: string list
            Rows: int option
        }

    type Case = { Id: string; Input: Input }

    let private canonical (v: JVal) : string = Canonical.write Rendering.CrossHost v

    /// A cell as the dataframe codec writes a literal: a `lit` expression.
    let private cellJson (c: Cell) : JVal = DataFrameCodec.encodeExpr (Lit c)

    let private cellOf (path: string) (v: JVal) : Cell =
        match DataFrameCodec.decodeExpr v with
        | Ok(Lit c) -> c
        | other -> failwithf "%s: expected a lit expression, got %A" path other

    let private transformInput (pipeline: Fuaran.Compute.Transform list) (source: Table) rows env lists unbound =
        { Subject = DerivationSubject.Transform
          Pipeline = Some(DataFrameCodec.encodePipeline pipeline)
          Expr = None
          Source = Some(ColumnCodec.encode (Embedded source))
          Env = env
          Lists = lists
          Unbound = unbound
          Rows = rows }

    /// The authored cases: what no drawn pipeline exercises.
    let private authored: Case list =
        let paramsCase rows =
            transformInput
                [ Filter(Binary(Gt, Col "v", Param "min"))
                  Filter(InParam(Col "g", "groups"))
                  Filter(Binary(Gt, Col "w", Param "unset"))
                  GroupBy([ "g" ], [ { Name = "total"; Fn = Sum; Of = "v" } ])
                  Sort [ Slot.Lit "g", Asc ] ]
                sales
                rows
                (Map.ofList [ "min", Int 1 ])
                (Map.ofList [ "groups", [ Str "a"; Str "b" ] ])
                [ "unset" ]

        [ { Id = "authored-params-bound-and-pruned"
            Input = paramsCase (Some 5) }
          { Id = "authored-schema-only"
            Input = paramsCase None }
          { Id = "authored-expr"
            Input =
              { Subject = DerivationSubject.Expr
                Pipeline = None
                Expr = Some(canonical (DataFrameCodec.encodeExpr (Binary(Mul, Param "rate", Lit(Float 2.0)))))
                Source = None
                Env = Map.ofList [ "rate", Float 1.25 ]
                Lists = Map.empty
                Unbound = []
                Rows = Some 1 } }
          { Id = "authored-open-schema-refused"
            Input =
              transformInput
                  [ Join(Ref "targets", [ "g", "g" ], Inner); Project [ "g", "g" ] ]
                  sales
                  (Some 5)
                  Map.empty
                  Map.empty
                  [] } ]

    /// The drawn cases: every `evalPipeline` vector of the transform law corpus
    /// at `root`, its source and pipeline texts carried verbatim.
    let private drawn (root: string) : Case list =
        [ for family, file in
              [ "transformLaws", "transform-laws.json"
                "dataframeParity", "dataframe-parity.json" ] do
              use doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, directory, file)))

              for v in doc.RootElement.GetProperty("vectors").EnumerateArray() do
                  if text v "case" = "evalPipeline" then
                      let input = v.GetProperty "input"

                      yield
                          { Id = family + "/" + text v "id"
                            Input =
                              { Subject = DerivationSubject.Transform
                                Pipeline = Some(text input "pipeline")
                                Expr = None
                                Source = Some(text input "source")
                                Env = Map.empty
                                Lists = Map.empty
                                Unbound = []
                                Rows = Some drawnRows } } ]

    let cases (root: string) : Case list = drawn root @ authored

    let private inputJson (i: Input) : JVal =
        let opt name (v: string option) =
            v |> Option.map (fun s -> name, JStr s) |> Option.toList

        JObj(
            [ "subject",
              JStr(
                  match i.Subject with
                  | DerivationSubject.Transform -> "transform"
                  | DerivationSubject.Expr -> "expr"
              ) ]
            @ opt "pipeline" i.Pipeline
            @ opt "expr" i.Expr
            @ opt "source" i.Source
            @ (if Map.isEmpty i.Env then
                   []
               else
                   [ "env", JObj [ for KeyValue(k, c) in i.Env -> k, cellJson c ] ])
            @ (if Map.isEmpty i.Lists then
                   []
               else
                   [ "lists", JObj [ for KeyValue(k, cs) in i.Lists -> k, JArr(List.map cellJson cs) ] ])
            @ (if List.isEmpty i.Unbound then
                   []
               else
                   [ "unbound", JArr(List.map JStr i.Unbound) ])
            @ (i.Rows |> Option.map (fun r -> "rows", JInt r) |> Option.toList)
        )

    /// An input read back from the file's JSON.
    let inputOf (id: string) (e: JsonElement) : Input =
        let tryText (name: string) =
            match e.TryGetProperty name with
            | true, v -> Some(string (v.GetString()))
            | _ -> None

        let jval (el: JsonElement) : JVal =
            match Json.parse (el.GetRawText()) with
            | Ok v -> v
            | Error m -> failwithf "%s: %s" id m

        let tryObj (name: string) =
            match e.TryGetProperty name with
            | true, v -> [ for p in v.EnumerateObject() -> p.Name, p.Value ]
            | _ -> []

        { Subject =
            match text e "subject" with
            | "transform" -> DerivationSubject.Transform
            | "expr" -> DerivationSubject.Expr
            | other -> failwithf "%s: unknown subject '%s'" id other
          Pipeline = tryText "pipeline"
          Expr = tryText "expr"
          Source = tryText "source"
          Env =
            tryObj "env"
            |> List.map (fun (k, v) -> k, cellOf (id + ".env." + k) (jval v))
            |> Map.ofList
          Lists =
            tryObj "lists"
            |> List.map (fun (k, v) -> k, [ for c in v.EnumerateArray() -> cellOf (id + ".lists." + k) (jval c) ])
            |> Map.ofList
          Unbound =
            match e.TryGetProperty "unbound" with
            | true, v -> [ for n in v.EnumerateArray() -> string (n.GetString()) ]
            | _ -> []
          Rows =
            match e.TryGetProperty "rows" with
            | true, v -> Some(v.GetInt32())
            | _ -> None }

    /// The reference trace's canonical text for an input.
    let traceText (id: string) (i: Input) : string =
        let sources (input: Table) : DerivationSources =
            { DerivationSources.ofTable input with
                Env = i.Env
                Lists = i.Lists
                Unbound = Set.ofList i.Unbound }

        let traced =
            match i.Subject, i.Pipeline, i.Expr, i.Source with
            | DerivationSubject.Transform, Some pipeline, None, Some source ->
                let input =
                    match ColumnCodec.decode source with
                    | Ok(Embedded t) -> t
                    | other -> failwithf "%s: the source is not an embedded table: %A" id other

                match DataFrameCodec.decodePipeline pipeline with
                | Ok p ->
                    Derivation.trace i.Rows (sources input) (rowsBinding (TransformSource.Data(Embedded input)) p None)
                | Error e -> failwithf "%s: the pipeline did not decode: %A" id e
            | DerivationSubject.Expr, None, Some expr, None ->
                match
                    Json.parse expr
                    |> Result.mapError string
                    |> Result.bind (DataFrameCodec.decodeExpr >> Result.mapError string)
                with
                | Ok e -> Derivation.trace i.Rows (sources exprFrame) (Binding.Expr(e, None): Binding<JVal>)
                | Error e -> failwithf "%s: the expression did not decode: %s" id e
            | _ -> failwithf "%s: an input carries the members its subject names and no other" id

        match traced with
        | Ok t -> Derivation.encode t
        | Error refusal -> failwithf "%s: the reference refused to trace: %A" id refusal

    let private description =
        "The derivation trace (WIRE_FORMAT 35): for a pipeline binding and its RESOLVED sources, one entry per written step with its verb, its canonical wire form, whether it ran (a filter reading an unbound param is pruned), the output schema the static walk derives, and, where `rows` is given, the table after it bounded to that many rows, or the evaluator's refusal. `input.subject` is `transform` (the pipeline `input.pipeline` over the embedded table `input.source`) or `expr` (the expression `input.expr` over the one-row frame of 35.2); `input.env` binds scalar params and `input.lists` list params, each value a `lit` expression in the dataframe codec's ColExpr form; `input.unbound` names the params no source bound; a `ref` source resolves to nothing. `expected.trace` is the reference trace's canonical text (35.1), which a host compares byte for byte. The drawn vectors are every `evalPipeline` vector of transformLaws and dataframeParity, their `source` and `pipeline` texts carried verbatim and traced at `rows` 3, so each one's last table is that vector's expected table bounded to three rows; the authored vectors (ids `authored-*`) cover bound and pruned params, an `expr`, an open schema whose evaluation is refused, and a trace taken without tables."

    /// The family file's text for the corpus at `root`, LF endings.
    let render (root: string) : string =
        let lines =
            cases root
            |> List.map (fun c ->
                "    "
                + canonical (
                    JObj
                        [ "id", JStr c.Id
                          "case", JStr "trace"
                          "input", inputJson c.Input
                          "expected", JObj [ "trace", JStr(traceText c.Id c.Input) ] ]
                ))

        "{\n"
        + "  \"family\": \""
        + familyId
        + "\",\n"
        + "  \"description\": "
        + canonical (JStr description)
        + ",\n"
        + "  \"vectors\": [\n"
        + String.Join(",\n", lines)
        + "\n  ]\n}\n"

    /// Write the family into the corpus at `root`; the path written.
    let write (root: string) : string =
        let path = Path.Combine(root, directory, fileName)
        File.WriteAllText(path, render root)
        path

type private Committed =
    { Id: string
      Input: DerivationLaws.Input
      Expected: string }

let private committed (root: string) : Committed list =
    let path = Path.Combine(root, DerivationLaws.directory, DerivationLaws.fileName)

    if not (File.Exists path) then
        failtestf "%s is missing from the corpus — regenerate with `%s`" path DerivationLaws.emitCommand

    use doc = JsonDocument.Parse(File.ReadAllText path)

    [ for v in doc.RootElement.GetProperty("vectors").EnumerateArray() ->
          let id = text v "id"

          { Id = id
            Input = DerivationLaws.inputOf id (v.GetProperty "input")
            Expected = text (v.GetProperty "expected") "trace" } ]

let private withCorpus (f: string -> unit) () =
    match Fuaran.Tests.CorpusRoot.tryFind () with
    | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
    | Some root -> f root

let private decoded (id: string) (wire: string) : DerivationTrace =
    match Derivation.decode wire with
    | Ok t -> t
    | Error e -> failtestf "%s: the trace did not decode: %s" id e

let private refusedAs (fragment: string) (wire: string) =
    match Derivation.decode wire with
    | Ok t -> failtestf "decoded what is not a trace: %A" t
    | Error e -> Expect.stringContains e fragment (sprintf "the refusal names the defect: %s" e)

[<Tests>]
let lawFamilyTests =
    testList
        "Phase 2767 — the derivation trace law family"
        [ testCase "the committed family is byte-identical to a fresh emission"
          <| withCorpus (fun root ->
              let path = Path.Combine(root, DerivationLaws.directory, DerivationLaws.fileName)

              if not (File.Exists path) then
                  failtestf "%s is missing — regenerate with `%s`" path DerivationLaws.emitCommand

              Expect.isTrue
                  (File.ReadAllText path = DerivationLaws.render root)
                  (sprintf "%s is stale — regenerate with `%s`" path DerivationLaws.emitCommand))

          testCase "laws/manifest.json indexes the family with its vector count and producer"
          <| withCorpus (fun root ->
              use doc =
                  JsonDocument.Parse(File.ReadAllText(Path.Combine(root, DerivationLaws.directory, "manifest.json")))

              let row =
                  doc.RootElement.GetProperty("families").EnumerateArray()
                  |> Seq.tryFind (fun r -> text r "id" = DerivationLaws.familyId)
                  |> Option.defaultWith (fun () -> failtest "laws/manifest.json has no derivationTraceLaws row")

              Expect.equal (text row "file") DerivationLaws.fileName "the row names the file"
              Expect.equal (row.GetProperty("vectors").GetInt32()) (List.length (committed root)) "the vector count"

              Expect.equal
                  (text (row.GetProperty "producer") "emit")
                  DerivationLaws.emitCommand
                  "the row names this emitter")

          testCase "every committed vector traces to its expected text"
          <| withCorpus (fun root ->
              let vectors = committed root
              Expect.isNonEmpty vectors "the family certifies something"

              for c in vectors do
                  Expect.equal (DerivationLaws.traceText c.Id c.Input) c.Expected (c.Id + ": the trace"))

          testCase "every expected text decodes and re-encodes byte for byte"
          <| withCorpus (fun root ->
              for c in committed root do
                  Expect.equal (Derivation.encode (decoded c.Id c.Expected)) c.Expected (c.Id + ": the round trip"))

          testCase "the family reaches what it says it does"
          <| withCorpus (fun root ->
              let traces = committed root |> List.map (fun c -> c.Id, decoded c.Id c.Expected)

              let steps =
                  traces |> List.collect (fun (_, t) -> t.Input :: (t.Steps |> List.map _.Output))

              let reached what (p: bool) =
                  Expect.isTrue p ("a vector reaches " + what)

              reached "an open schema" (steps |> List.exists (fun s -> not (SchemaWalk.isClosed s.Schema)))
              reached "a refused evaluation" (steps |> List.exists (fun s -> s.Table |> Option.exists Result.isError))

              reached
                  "a bounded table"
                  (steps
                   |> List.exists (fun s ->
                       match s.Table with
                       | Some(Ok t) -> Table.rowCount t.Rows < t.RowCount
                       | _ -> false))

              reached "a trace without tables" (steps |> List.exists (fun s -> Option.isNone s.Table))
              reached "an expr" (traces |> List.exists (fun (_, t) -> t.Subject = DerivationSubject.Expr))

              reached
                  "a pruned step"
                  (traces
                   |> List.exists (fun (_, t) ->
                       t.Steps
                       |> List.exists (fun s ->
                           match s.Status with
                           | StepStatus.Pruned _ -> true
                           | StepStatus.Applied -> false))))

          test "a trace round-trips through its canonical text" {
              let binding =
                  rowsBinding
                      (TransformSource.Data(Embedded sales))
                      parameterised
                      (Some [ stateParam "min"; stateParam "groups"; stateParam "unset" ])

              let trace =
                  traceOk (BindingResolver.derivationTrace (Some 2) parameterisedSources binding)

              let wire = Derivation.encode trace
              Expect.equal (Derivation.encode (decoded "fixture" wire)) wire "byte-stable"

              Expect.equal
                  (Derivation.encode (decoded "fixture" wire))
                  (Fuaran.Core.Canonical.write Fuaran.Core.Rendering.CrossHost (Derivation.toJson trace))
                  "the canonical text is toJson under the cross-host rendering"

              let back = decoded "fixture" wire
              Expect.equal (back.Steps |> List.map _.Status) (trace.Steps |> List.map _.Status) "the statuses"

              let wires (t: DerivationTrace) =
                  t.Steps
                  |> List.map (fun s -> Fuaran.Core.Canonical.write Fuaran.Core.Rendering.CrossHost s.Wire)

              Expect.equal (wires back) (wires trace) "the steps' wire forms, canonically"

              Expect.equal
                  (back.Steps
                   |> List.map (fun s ->
                       s.Output.Table |> Option.map (Result.map (fun t -> t.RowCount, encoded t.Rows))))
                  (trace.Steps
                   |> List.map (fun s ->
                       s.Output.Table |> Option.map (Result.map (fun t -> t.RowCount, encoded t.Rows))))
                  "the tables"
          }

          test "the decoder refuses what is not a trace, naming the member" {
              let wire =
                  Derivation.encode (traceOk (Derivation.trace (Some 2) (DerivationSources.ofTable sales) totalBinding))

              let edited (f: string -> string) = f wire

              refusedAs
                  "'extra' is not a member"
                  (edited (fun w -> w.Replace("{\"input\":", "{\"extra\":1,\"input\":")))

              refusedAs "$.subject" (edited (fun w -> w.Replace("\"subject\":\"transform\"", "\"subject\":\"table\"")))
              refusedAs ".verb" (edited (fun w -> w.Replace("\"verb\":\"groupBy\"", "\"verb\":\"filter\"")))
              refusedAs ".index" (edited (fun w -> w.Replace("\"index\":0", "\"index\":1")))

              refusedAs
                  "an applied step names no unbound param"
                  (edited (fun w -> w.Replace("\"status\":\"applied\"", "\"status\":\"applied\",\"unbound\":[\"x\"]")))

              refusedAs
                  "a closed schema states no reason"
                  (edited (fun w -> w.Replace("\"closed\":true,", "\"closed\":true,\"reason\":\"r\",")))

              refusedAs
                  "never both"
                  (edited (fun w -> w.Replace("\"schema\":{\"closed\"", "\"refusal\":\"r\",\"schema\":{\"closed\"")))

              refusedAs "is not a column type" (edited (fun w -> w.Replace("\"type\":\"int\"", "\"type\":\"integer\"")))
          } ]
