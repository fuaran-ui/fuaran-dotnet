module Fuaran.UI.Derivation

// ============================================================================
//  Phase 2749 — a derivation trace: how a bound value was computed.
//
//  A `Binding.Transform` or a `Binding.Expr` carries its pipeline as data, so
//  the language tier can say how a value was computed without asking anything
//  else in the tree. `trace` answers that: one record per pipeline step, with
//  the step's verb, its canonical wire form, whether it ran, the output schema
//  the static schema walk (`Fuaran.Compute.SchemaWalk`) derives for it, and,
//  when the caller asks, the table after that step, bounded by a row limit the
//  caller states.
//
//  Two halves, kept apart so Phase 2539's evaluator split can move one without
//  the other: the SCHEMA half (verb, wire form, status, schema) evaluates
//  nothing; the TABLE half is the reference evaluator
//  (`DataFrame.evalPipelineWithInEnv`) over each step's prefix and adds no
//  evaluator of its own. Evaluating the prefix, rather than feeding one step's
//  table to the next, is what makes the last step's table the binding's value
//  by construction: the last prefix IS the pipeline the binding runs.
//
//  What the binding runs is the EFFECTIVE pipeline, and that rule is defined
//  here once (`effective`) for the resolver and the trace alike: list params
//  substituted (`Transform.substituteListParams`), then every `filter` that
//  reads an unbound param pruned (an unset filter is no constraint). A pruned
//  step is reported, not hidden: it is the most common answer to "why is this
//  number the whole total".
//
//  A `define` step is pipeline-wide (DECISIONS D48 of the compute tier): a call
//  may precede its definition. So each step's prefix carries every definition
//  the pipeline holds, including later ones, which changes no frame.
//
//  The trace reads RESOLVED sources (`DerivationSources`): the input table and
//  the param environment a resolver produced. It resolves nothing itself, so it
//  depends on no renderer and stays Fable-clean.
// ============================================================================

open Fuaran.Core
open Fuaran.Compute
open Fuaran.UI.Types

/// What a trace reads: a pipeline binding's sources, already resolved. The
/// resolver builds one from a render's `BindingSources`
/// (`BindingResolver.derivationSources`); a caller holding a table builds one
/// with `DerivationSources.ofTable`.
type DerivationSources =
    {
        /// The table the pipeline's first step reads. Not read for a
        /// `Binding.Expr`, which always evaluates over `exprFrame`.
        Input: Table
        /// The bound scalar params.
        Env: Map<string, Cell>
        /// The bound list params, substituted into the pipeline before it runs.
        Lists: Map<string, Cell list>
        /// The params whose source did not resolve; a `filter` reading one is
        /// pruned.
        Unbound: Set<string>
        /// The resolver for a `Ref` source a `join` or a set operation names.
        Resolve: string -> Result<Table, EvalError>
    }

/// Builders over `DerivationSources`.
[<RequireQualifiedAccess>]
module DerivationSources =

    /// A table and nothing else: no params bound, no `Ref` source resolved.
    let ofTable (input: Table) : DerivationSources =
        { Input = input
          Env = Map.empty
          Lists = Map.empty
          Unbound = Set.empty
          Resolve = DataFrame.noResolve }

/// Whether a written step runs.
[<RequireQualifiedAccess>]
type StepStatus =
    /// The step runs.
    | Applied
    /// A `filter` that reads a param no source bound: it is removed before the
    /// pipeline runs, so it constrains nothing. Carries the unbound names.
    | Pruned of unbound: string list

/// A table at one point of the derivation, bounded by the caller's row limit.
type StageTable =
    {
        /// The table's full row count.
        RowCount: int
        /// The first rows of the table, at most the caller's limit.
        Rows: Table
    }

/// What is known at one point of the derivation.
type DerivationStage =
    {
        /// The output schema the static walk derives. `AtLeast` carries the
        /// walk's own reason for being open.
        Schema: SchemaKnowledge
        /// The table, when the caller asked for tables: `Error` names the
        /// evaluator's refusal at this point.
        Table: Result<StageTable, string> option
    }

/// One written step of a pipeline.
type DerivationStep =
    {
        /// The step's position in the pipeline as written, from 0.
        Index: int
        /// The step's wire `$type`, such as `filter` or `groupBy`.
        Verb: string
        /// The step's canonical wire form, as written.
        Wire: JVal
        /// Whether it runs.
        Status: StepStatus
        /// What is known after it.
        Output: DerivationStage
    }

/// Which binding a trace is of.
[<RequireQualifiedAccess>]
type DerivationSubject =
    /// A `Binding.Transform`: a table derived from a source.
    | Transform
    /// A `Binding.Expr`: one value, derived over a one-row frame.
    | Expr

/// How a bound value was computed.
type DerivationTrace =
    {
        Subject: DerivationSubject
        /// What the first step reads.
        Input: DerivationStage
        /// One entry per written step, in order.
        Steps: DerivationStep list
    }

/// Why a binding has no trace.
[<RequireQualifiedAccess>]
type DerivationRefusal =
    /// The binding carries no pipeline. `binding` names its case; `source` says
    /// where its value comes from instead.
    | NoPipeline of binding: string * source: string
    /// An optional slot that holds no binding at all.
    | Absent
    /// The binding carries a pipeline, but its sources did not resolve.
    | Unresolved of reason: string

/// Readers over `DerivationRefusal`.
[<RequireQualifiedAccess>]
module DerivationRefusal =

    /// The refusal as one sentence a reader can act on.
    let describe (refusal: DerivationRefusal) : string =
        match refusal with
        | DerivationRefusal.NoPipeline(binding, source) ->
            "This slot holds a Binding."
            + binding
            + ", which carries no pipeline: its value is "
            + source
            + ". Only a Binding.Transform or a Binding.Expr carries a derivation to trace; read this value with fuaran.getBindingValue instead."
        | DerivationRefusal.Absent ->
            "This optional slot holds no binding, so nothing computes its value and there is no derivation to trace."
        | DerivationRefusal.Unresolved reason -> "The derivation could not be traced: " + reason

/// The one-row frame a `Binding.Expr` evaluates over: one column of one row,
/// so a `derive` has a row to produce. One value for the process, so a render
/// prepares it once however many `Expr` slots it holds (Phase 1890).
let exprFrame: Table =
    { Schema = [ Field.create "__unit" BoolType ]
      Columns = [ Column.ofBools "__unit" (Vector.ofList [ true ]) AllValid ] }

/// The pipeline a `Binding.Expr` runs over `exprFrame` (Phase 1534): the
/// expression derived into `__value`, then projected alone, so the result is
/// 1x1 by construction.
let exprPipeline (expr: ColExpr) : Fuaran.Compute.Transform list =
    [ Derive("__value", expr); Project [ "__value", "__value" ] ]

/// A step's wire `$type`, read off the step's own encoder, so a verb the
/// compute tier adds arrives here without an edit. Empty for a step that does
/// not encode with a string `$type`, which is a codec defect to report where
/// codecs are certified rather than a name to invent here.
let verb (step: Fuaran.Compute.Transform) : string =
    match DataFrameCodec.encodeTransform step with
    | JObj fields ->
        fields
        |> List.tryPick (fun (k, v) ->
            match k, v with
            | "$type", JStr tag -> Some tag
            | _ -> None)
        |> Option.defaultValue ""
    | _ -> ""

/// Each written step paired with the step that runs and its status: list
/// params substituted, then every `filter` reading an unbound param pruned.
let effective
    (lists: Map<string, Cell list>)
    (unbound: Set<string>)
    (pipeline: Fuaran.Compute.Transform list)
    : (Fuaran.Compute.Transform * StepStatus) list =
    let substituted =
        if Map.isEmpty lists then
            pipeline
        else
            Fuaran.Compute.Transform.substituteListParams lists pipeline

    substituted
    |> List.map (fun step ->
        match step with
        | Filter pred ->
            match ColExpr.paramsOf pred |> List.filter (fun p -> Set.contains p unbound) with
            | [] -> step, StepStatus.Applied
            | missing -> step, StepStatus.Pruned missing
        | _ -> step, StepStatus.Applied)

/// The pipeline a binding actually runs (see `effective`).
let effectivePipeline
    (lists: Map<string, Cell list>)
    (unbound: Set<string>)
    (pipeline: Fuaran.Compute.Transform list)
    : Fuaran.Compute.Transform list =
    effective lists unbound pipeline
    |> List.choose (fun (step, status) ->
        match status with
        | StepStatus.Applied -> Some step
        | StepStatus.Pruned _ -> None)

/// The pipeline a binding carries, or why it carries none.
let pipelineOf (binding: Binding<'T>) : Result<DerivationSubject * Fuaran.Compute.Transform list, DerivationRefusal> =
    let none name source =
        Error(DerivationRefusal.NoPipeline(name, source))

    match binding with
    | Binding.Transform(_, pipeline, _) -> Ok(DerivationSubject.Transform, pipeline)
    | Binding.Expr(expr, _) -> Ok(DerivationSubject.Expr, exprPipeline expr)
    | Binding.Static _ -> none "Static" "a literal written into the document"
    | Binding.Query(name, _, _) -> none "Query" ("the host's query result '" + name + "', computed outside the tree")
    | Binding.Filter(name, _) -> none "Filter" ("the current value of the filter '" + name + "'")
    | Binding.Selection(nodeId, _, _, _) -> none "Selection" ("the current selection of node '" + nodeId + "'")
    | Binding.State(key, _) -> none "State" ("read from the state key '" + key + "'")
    | Binding.Now _ -> none "Now" "the current instant, which the host supplies"
    | Binding.Computed _ -> none "Computed" "a host function with no wire form"
    | Binding.Local _ -> none "Local" "a control's edit buffer"
    | Binding.Format _ -> none "Format" "another binding's number rendered as text; trace that binding"
    | Binding.I18n(key, _) -> none "I18n" ("the localised message '" + key + "'")
    | Binding.Invoke(capabilityId, _) -> none "Invoke" ("the answer of the host capability '" + capabilityId + "'")

/// The first `limit` rows of `table`, through the reference evaluator's own
/// `limit` rather than a slicing of this module's.
let private bounded (limit: int) (table: Table) : Result<StageTable, string> =
    let count = Table.rowCount table

    if count <= limit then
        Ok { RowCount = count; Rows = table }
    else
        DataFrame.evalPipeline [ Limit(Slot.Lit(max 0 limit), Slot.Lit 0) ] table
        |> Result.map (fun rows -> { RowCount = count; Rows = rows })
        |> Result.mapError DataFrame.errorString

/// How `binding` was computed over `sources`. `rows` asks for the table at each
/// point, at most that many rows of it; `None` traces the schema half alone and
/// evaluates nothing. A binding with no pipeline is refused with the reason.
let trace
    (rows: int option)
    (sources: DerivationSources)
    (binding: Binding<'T>)
    : Result<DerivationTrace, DerivationRefusal> =
    match pipelineOf binding with
    | Error refusal -> Error refusal
    | Ok(subject, written) ->
        let input =
            match subject with
            | DerivationSubject.Expr -> exprFrame
            | DerivationSubject.Transform -> sources.Input

        let declared (name: string) : Schema option =
            match sources.Resolve name with
            | Ok table -> Some table.Schema
            | Error _ -> None

        let inputSchema = SchemaWalk.ofSource SchemaWalk.noSources (Embedded input)
        let steps = effective sources.Lists sources.Unbound written |> List.toArray

        let definitions (after: int) : Fuaran.Compute.Transform list =
            steps
            |> Array.skip (after + 1)
            |> Array.choose (fun (step, _) ->
                match step with
                | Define _ -> Some step
                | _ -> None)
            |> List.ofArray

        // The pipeline that has run once step `i` has: the applied steps up to
        // it, and every later definition (a definition changes no frame).
        let prefix (i: int) : Fuaran.Compute.Transform list =
            (steps
             |> Array.take (i + 1)
             |> Array.choose (fun (step, status) ->
                 match status with
                 | StepStatus.Applied -> Some step
                 | StepStatus.Pruned _ -> None)
             |> List.ofArray)
            @ definitions i

        let tableAt (pipeline: Fuaran.Compute.Transform list) : Result<StageTable, string> option =
            rows
            |> Option.map (fun limit ->
                DataFrame.evalPipelineWithInEnv sources.Resolve sources.Env pipeline input
                |> Result.mapError DataFrame.errorString
                |> Result.bind (bounded limit))

        let start =
            { Schema = inputSchema
              Table = tableAt [] }

        let traced =
            written
            |> List.mapi (fun i step -> i, step)
            |> List.fold
                (fun (before: DerivationStage, refusedAt: int option, acc) (i, writtenStep) ->
                    let _, status = steps[i]

                    let output =
                        match status, refusedAt with
                        // A pruned filter does not run: what follows it is what preceded it.
                        | StepStatus.Pruned _, _ -> before
                        | StepStatus.Applied, Some k ->
                            { Schema = SchemaWalk.ofPipelineFrom declared inputSchema (prefix i)
                              Table =
                                rows
                                |> Option.map (fun _ -> Error(sprintf "not evaluated: step %d was refused" k)) }
                        | StepStatus.Applied, None ->
                            let pipeline = prefix i

                            { Schema = SchemaWalk.ofPipelineFrom declared inputSchema pipeline
                              Table = tableAt pipeline }

                    let refusedAt =
                        match refusedAt, output.Table with
                        | None, Some(Error _) when status = StepStatus.Applied -> Some i
                        | _ -> refusedAt

                    let entry =
                        { Index = i
                          Verb = verb writtenStep
                          Wire = DataFrameCodec.encodeTransform writtenStep
                          Status = status
                          Output = output }

                    output, refusedAt, entry :: acc)
                (start, None, [])
            |> fun (_, _, acc) -> List.rev acc

        Ok
            { Subject = subject
              Input = start
              Steps = traced }

/// The table the trace ends on: the last step's, or the input's for an empty
/// pipeline. `None` when the trace was taken without tables.
let result (trace: DerivationTrace) : Result<StageTable, string> option =
    match List.tryLast trace.Steps with
    | Some step -> step.Output.Table
    | None -> trace.Input.Table

// ─── The trace as data (the introspection tool's answer) ──────────────────

let private schemaJson (schema: SchemaKnowledge) : JVal =
    let columns =
        SchemaWalk.columns schema
        // A column whose type the walk cannot decide states no `type`.
        |> List.map (fun c ->
            JObj(
                ("name", JStr c.Name)
                :: (c.Type |> Option.map (fun t -> "type", JStr(ColumnType.tag t)) |> Option.toList)
            ))

    JObj(
        [ "closed", JBool(SchemaWalk.isClosed schema); "columns", JArr columns ]
        @ (SchemaWalk.reason schema
           |> Option.map (fun r -> "reason", JStr r)
           |> Option.toList)
    )

let private stageJson (stage: DerivationStage) : (string * JVal) list =
    [ "schema", schemaJson stage.Schema ]
    @ (match stage.Table with
       | None -> []
       | Some(Ok t) ->
           [ "table",
             JObj
                 [ "rowCount", JInt t.RowCount
                   "rows", ColumnCodec.encodeJson (Embedded t.Rows) ] ]
       | Some(Error refusal) -> [ "refusal", JStr refusal ])

/// The trace as an ordered JSON value: `subject`, `input` and one entry per
/// step (`index`, `verb`, `step` — the canonical wire form — `status`, the
/// unbound params of a pruned step, the `schema`, and the `table` or the
/// evaluator's `refusal` where tables were asked for).
let toJson (trace: DerivationTrace) : JVal =
    JObj
        [ "subject",
          JStr(
              match trace.Subject with
              | DerivationSubject.Transform -> "transform"
              | DerivationSubject.Expr -> "expr"
          )
          "input", JObj(stageJson trace.Input)
          "steps",
          JArr(
              trace.Steps
              |> List.map (fun s ->
                  JObj(
                      [ "index", JInt s.Index
                        "verb", JStr s.Verb
                        "step", s.Wire
                        "status",
                        JStr(
                            match s.Status with
                            | StepStatus.Applied -> "applied"
                            | StepStatus.Pruned _ -> "pruned"
                        ) ]
                      @ (match s.Status with
                         | StepStatus.Pruned names -> [ "unbound", JArr(names |> List.map JStr) ]
                         | StepStatus.Applied -> [])
                      @ stageJson s.Output
                  ))
          ) ]

// ─── The canonical wire form and its decoder (Phase 2767, WIRE_FORMAT §35) ─
//
// The trace's canonical text is `toJson` under the cross-host rendering (§2:
// members sorted Ordinal, no null, an absent member is structural). It is what
// the `derivationTraceLaws` vectors carry as `expected.trace`, so a host that
// implements the trace compares its own text with them byte for byte.
//
// A column of a trace's schema states its name and, where the walk decides it,
// its type: the wire form carries no unit, label or exactness, so a decoded
// trace re-encodes to the same text (the round trip is byte-stable) without
// being the value it was encoded from in those members.

/// The trace's canonical text (WIRE_FORMAT §35).
let encode (trace: DerivationTrace) : string =
    Canonical.write Rendering.CrossHost (toJson trace)

/// Readers for `ofJson`: each refusal names the member's path.
module private Read =

    /// The members of an object, refused when it holds one the form does not.
    let fields (path: string) (allowed: string list) (v: JVal) : Result<(string * JVal) list, string> =
        match v with
        | JObj fs ->
            match fs |> List.tryFind (fun (k, _) -> not (List.contains k allowed)) with
            | Some(k, _) -> Error(sprintf "%s: '%s' is not a member of this form" path k)
            | None -> Ok fs
        | _ -> Error(path + ": expected an object")

    let tryMember (name: string) (fs: (string * JVal) list) : JVal option =
        fs |> List.tryPick (fun (k, v) -> if k = name then Some v else None)

    let required (path: string) (name: string) (fs: (string * JVal) list) : Result<JVal, string> =
        match tryMember name fs with
        | Some v -> Ok v
        | None -> Error(sprintf "%s: the member '%s' is required" path name)

    let str (path: string) (v: JVal) : Result<string, string> =
        match v with
        | JStr s -> Ok s
        | _ -> Error(path + ": expected a string")

    let int (path: string) (v: JVal) : Result<int, string> =
        match v with
        | JInt i -> Ok i
        | _ -> Error(path + ": expected an integer")

    /// Every item of an array read, or the first refusal.
    let items (path: string) (read: int -> string -> JVal -> Result<'a, string>) (v: JVal) : Result<'a list, string> =
        match v with
        | JArr items ->
            let rec go i acc rest =
                match rest with
                | [] -> Ok(List.rev acc)
                | item :: rest ->
                    match read i (sprintf "%s[%d]" path i) item with
                    | Ok x -> go (i + 1) (x :: acc) rest
                    | Error e -> Error e

            go 0 [] items
        | _ -> Error(path + ": expected an array")

let private columnOfJson (path: string) (v: JVal) : Result<ColumnKnowledge, string> =
    Read.fields path [ "name"; "type" ] v
    |> Result.bind (fun fs ->
        Read.required path "name" fs
        |> Result.bind (Read.str (path + ".name"))
        |> Result.bind (fun name ->
            match Read.tryMember "type" fs with
            | None -> Ok(ColumnKnowledge.named name None)
            | Some t ->
                Read.str (path + ".type") t
                |> Result.bind (fun tag ->
                    match ColumnType.ofTag tag with
                    | Some ty -> Ok(ColumnKnowledge.named name (Some ty))
                    | None -> Error(sprintf "%s.type: '%s' is not a column type" path tag))))

let private schemaOfJson (path: string) (v: JVal) : Result<SchemaKnowledge, string> =
    Read.fields path [ "closed"; "columns"; "reason" ] v
    |> Result.bind (fun fs ->
        Read.required path "closed" fs
        |> Result.bind (fun closed ->
            Read.required path "columns" fs
            |> Result.bind (Read.items (path + ".columns") (fun _ -> columnOfJson))
            |> Result.bind (fun columns ->
                match closed, Read.tryMember "reason" fs with
                | JBool true, None -> Ok(SchemaKnowledge.Closed columns)
                | JBool false, Some r ->
                    Read.str (path + ".reason") r
                    |> Result.map (fun reason -> SchemaKnowledge.AtLeast(columns, reason))
                | JBool true, Some _ -> Error(path + ": a closed schema states no reason")
                | JBool false, None -> Error(path + ": an open schema states the reason it is open")
                | _ -> Error(path + ".closed: expected a boolean"))))

let private tableOfJson (path: string) (v: JVal) : Result<StageTable, string> =
    Read.fields path [ "rowCount"; "rows" ] v
    |> Result.bind (fun fs ->
        Read.required path "rowCount" fs
        |> Result.bind (Read.int (path + ".rowCount"))
        |> Result.bind (fun count ->
            Read.required path "rows" fs
            |> Result.bind (fun rows ->
                match ColumnCodec.decodeJson rows with
                | Ok(Embedded t) -> Ok { RowCount = count; Rows = t }
                | Ok _ -> Error(path + ".rows: expected an embedded table")
                | Error e -> Error(sprintf "%s.rows: %A" path e))))

/// The members a stage carries: the input's, and every step's beside its own.
let private stageMembers = [ "schema"; "table"; "refusal" ]

let private stageOfJson (path: string) (fs: (string * JVal) list) : Result<DerivationStage, string> =
    let table =
        match Read.tryMember "table" fs, Read.tryMember "refusal" fs with
        | None, None -> Ok None
        | Some _, Some _ -> Error(path + ": a stage carries a table or a refusal, never both")
        | None, Some r -> Read.str (path + ".refusal") r |> Result.map (Error >> Some)
        | Some t, None -> tableOfJson (path + ".table") t |> Result.map (Ok >> Some)

    Read.required path "schema" fs
    |> Result.bind (schemaOfJson (path + ".schema"))
    |> Result.bind (fun schema -> table |> Result.map (fun t -> { Schema = schema; Table = t }))

let private statusOfJson (path: string) (fs: (string * JVal) list) : Result<StepStatus, string> =
    match Read.tryMember "status" fs, Read.tryMember "unbound" fs with
    | Some(JStr "applied"), None -> Ok StepStatus.Applied
    | Some(JStr "pruned"), Some names ->
        Read.items (path + ".unbound") (fun _ -> Read.str) names
        |> Result.map StepStatus.Pruned
    | Some(JStr "applied"), Some _ -> Error(path + ": an applied step names no unbound param")
    | Some(JStr "pruned"), None -> Error(path + ": a pruned step names its unbound params")
    | Some _, _ -> Error(path + ".status: expected \"applied\" or \"pruned\"")
    | None, _ -> Error(path + ": the member 'status' is required")

let private stepOfJson (position: int) (path: string) (v: JVal) : Result<DerivationStep, string> =
    Read.fields path ([ "index"; "verb"; "step"; "status"; "unbound" ] @ stageMembers) v
    |> Result.bind (fun fs ->
        Read.required path "index" fs
        |> Result.bind (Read.int (path + ".index"))
        |> Result.bind (fun index ->
            if index <> position then
                Error(sprintf "%s.index: %d is not the step's position %d" path index position)
            else
                Ok index)
        |> Result.bind (fun index ->
            Read.required path "step" fs
            |> Result.bind (fun wire ->
                match DataFrameCodec.decodeTransform wire with
                | Error e -> Error(sprintf "%s.step: %A" path e)
                | Ok written -> Ok(wire, verb written))
            |> Result.bind (fun (wire, own) ->
                Read.required path "verb" fs
                |> Result.bind (Read.str (path + ".verb"))
                |> Result.bind (fun stated ->
                    if stated <> own then
                        Error(sprintf "%s.verb: '%s' is not the step's own $type '%s'" path stated own)
                    else
                        statusOfJson path fs
                        |> Result.bind (fun status ->
                            stageOfJson path fs
                            |> Result.map (fun output ->
                                { Index = index
                                  Verb = stated
                                  Wire = wire
                                  Status = status
                                  Output = output }))))))

/// A trace read from its JSON value: the inverse of `toJson` up to the members
/// the wire form does not carry. A refusal names the member's path.
let ofJson (v: JVal) : Result<DerivationTrace, string> =
    Read.fields "$" [ "subject"; "input"; "steps" ] v
    |> Result.bind (fun fs ->
        let subject =
            match Read.required "$" "subject" fs with
            | Ok(JStr "transform") -> Ok DerivationSubject.Transform
            | Ok(JStr "expr") -> Ok DerivationSubject.Expr
            | Ok _ -> Error "$.subject: expected \"transform\" or \"expr\""
            | Error e -> Error e

        subject
        |> Result.bind (fun subject ->
            Read.required "$" "input" fs
            |> Result.bind (Read.fields "$.input" stageMembers)
            |> Result.bind (stageOfJson "$.input")
            |> Result.bind (fun input ->
                Read.required "$" "steps" fs
                |> Result.bind (Read.items "$.steps" stepOfJson)
                |> Result.map (fun steps ->
                    { Subject = subject
                      Input = input
                      Steps = steps }))))

/// A trace read from its text (WIRE_FORMAT §35). Any member order is read; the
/// canonical text is what `encode` writes.
let decode (text: string) : Result<DerivationTrace, string> = Json.parse text |> Result.bind ofJson
