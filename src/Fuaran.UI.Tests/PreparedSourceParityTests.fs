module Fuaran.UI.Tests.PreparedSourceParity

// ============================================================================
//  Phase 1890 — one prepared source per render.
//
//  The resolver now prepares each Transform source ONCE per render
//  (`PreparedSources`) and evaluates every site bound to it through
//  `DataFrame.evalPrepared`, where it used to hand each site's table to
//  `evalPipelineInEnv`. Three claims, each stated against the corpus or the
//  scope rather than against the implementation:
//
//   1. Every Transform vector in the corpus (`laws/transform-laws.json`)
//      answers through the resolver's route exactly as the reference
//      evaluator answers it: the same table, byte for byte as the corpus pins
//      it, the same rows at a rendered site, and the same refusal.
//   2. Sharing changes no answer: every corpus pipeline evaluated as a site
//      over every corpus source, all in ONE render — embedded, and live through
//      a state key each — renders the rows it renders alone in a render of its
//      own.
//   3. The scope is the render: one build per source object per render, a
//      fresh one for another render or another object, and a refusal kept as
//      a refusal.
// ============================================================================

open System.IO
open System.Text.Json
open Expecto
open Fuaran.Core
open Fuaran.Compute
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

type private Vector =
    {
        Id: string
        Source: Table
        Pipeline: Transform list
        /// The corpus's canonical table text for an `ok` vector; `None` for an
        /// `error` vector, whose refusal is deliberately unnamed.
        Expected: string option
    }

/// A string member of a corpus vector; the corpus never writes one as null.
let private text (e: JsonElement) (name: string) : string =
    match e.GetProperty(name).GetString() with
    | null -> failwithf "corpus member '%s' is null" name
    | s -> s

let private vectors () : Vector list option =
    Fuaran.Tests.CorpusRoot.tryFind ()
    |> Option.map (fun root ->
        use doc =
            JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "laws", "transform-laws.json")))

        [ for v in doc.RootElement.GetProperty("vectors").EnumerateArray() do
              if text v "case" = "evalPipeline" then
                  let id = text v "id"
                  let input = v.GetProperty "input"
                  let expected = v.GetProperty "expected"

                  let source =
                      match ColumnCodec.decode (text input "source") with
                      | Ok(Embedded t) -> t
                      | other -> failwithf "%s: the source is not an embedded table: %A" id other

                  let pipeline =
                      match DataFrameCodec.decodePipeline (text input "pipeline") with
                      | Ok p -> p
                      | Error e -> failwithf "%s: the pipeline did not decode: %A" id e

                  { Id = id
                    Source = source
                    Pipeline = pipeline
                    Expected =
                      match text expected "verdict" with
                      | "ok" -> Some(text expected "table")
                      | _ -> None } ])

let private withVectors (f: Vector list -> unit) () =
    match vectors () with
    | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
    | Some [] -> failtest "transform-laws.json holds no evalPipeline vector: the parity certified nothing"
    | Some vs -> f vs

/// A render: one `BindingSources` value, as a context builder makes one.
let private render (state: Map<string, obj>) : BindingSources =
    { BindingSources.empty with
        State = state }

/// The rows a site renders, or its refusal, as one comparable value.
let private site (sources: BindingSources) (source: TransformSource) (pipeline: Transform list) =
    match BindingResolver.resolve<Row seq> sources (Binding.Transform(source, pipeline, None)) with
    | BindingResolver.Resolved rows -> Ok(List.ofSeq rows)
    | BindingResolver.Errored m -> Error m
    | other -> failwithf "a Transform site resolved to neither rows nor a refusal: %A" other

let private emptyInitial = Embedded { Schema = []; Columns = [] }

[<Tests>]
let tests =
    testList
        "Phase 1890 — one prepared source per render"
        [ testCase "every corpus Transform vector answers through the shared-prepared route as the reference does"
          <| withVectors (fun vs ->
              for v in vs do
                  let reference = DataFrame.evalPipelineInEnv Map.empty v.Pipeline v.Source
                  let prepared = DataFrame.prepare v.Source

                  let viaPrepared =
                      DataFrame.evalPrepared DataFrame.noResolve Map.empty v.Pipeline prepared

                  match v.Expected, reference, viaPrepared with
                  | Some pinned, Ok r, Ok p ->
                      Expect.equal (ColumnCodec.encode (Embedded r)) pinned (v.Id + ": the reference is the corpus's")
                      Expect.equal (ColumnCodec.encode (Embedded p)) pinned (v.Id + ": evalPrepared is the corpus's")
                  | None, Error r, Error p ->
                      Expect.equal (DataFrame.errorString p) (DataFrame.errorString r) (v.Id + ": the same refusal")
                  | _ -> failtestf "%s: the verdicts disagree (reference %A, prepared %A)" v.Id reference viaPrepared

                  // At a rendered site: the rows the reference table renders as, or
                  // the refusal the resolver has always worded.
                  let expectedSite =
                      match reference with
                      | Ok r -> site (render Map.empty) (TransformSource.Data(Embedded r)) []
                      | Error e -> Error("Transform evaluation failed: " + DataFrame.errorString e)

                  Expect.equal
                      (site (render Map.empty) (TransformSource.Data(Embedded v.Source)) v.Pipeline)
                      expectedSite
                      (v.Id + ": the rendered site is the reference's"))

          testCase "sharing sources across every corpus pipeline in one render changes no site's answer"
          <| withVectors (fun vs ->
              let pipelines = vs |> List.map _.Pipeline
              let key (i: int) = sprintf "rows-%d" i

              // ONE render holding EVERY corpus source — embedded, and live under a
              // state key each — so a cache that confused two sources, or a site's
              // source with its neighbour's, answers some site with the wrong rows.
              let shared =
                  render (
                      vs
                      |> List.mapi (fun i v -> key i, (ColumnCodec.encodeJson (Embedded v.Source) :> obj))
                      |> Map.ofList
                  )

              let mutable sites = 0

              for i, v in List.indexed vs do
                  for p in pipelines do
                      // The site alone, in a render of its own: nothing shares its source.
                      let alone = site (render Map.empty) (TransformSource.Data(Embedded v.Source)) p
                      let embedded = site shared (TransformSource.Data(Embedded v.Source)) p

                      let live =
                          site shared (TransformSource.Live(Binding.State(key i, None), emptyInitial)) p

                      Expect.equal embedded alone (v.Id + ": the shared embedded source answers as the site alone")
                      Expect.equal live alone (v.Id + ": the shared live source answers as the site alone")
                      sites <- sites + 2

              Expect.equal sites (2 * List.length vs * List.length pipelines) "every shared site was compared")

          testList
              "the scope is the render"
              [ let table =
                    { Schema = [ Field.create "n" IntType ]
                      Columns = [ Column.ofInts "n" (Vector.ofList [ 1; 2; 3 ]) AllValid ] }

                let counting () =
                    let mutable builds = 0

                    (fun () ->
                        builds <- builds + 1
                        Ok table),
                    (fun () -> builds)

                test "two sites over one source object in one render build it once and share it" {
                    let build, builds = counting ()
                    let r = render Map.empty
                    let first = PreparedSources.prepare r (table :> obj) build
                    let second = PreparedSources.prepare r (table :> obj) build
                    Expect.equal (builds ()) 1 "the second site reuses the first site's preparation"

                    match first, second with
                    | Ok a, Ok b -> Expect.isTrue (obj.ReferenceEquals(a, b)) "both sites hold the same prepared source"
                    | _ -> failtest "a table source refused"
                }

                test "another render prepares the same source afresh" {
                    let build, builds = counting ()
                    PreparedSources.prepare (render Map.empty) (table :> obj) build |> ignore
                    PreparedSources.prepare (render Map.empty) (table :> obj) build |> ignore
                    Expect.equal (builds ()) 2 "nothing a render prepared is handed to the next one"
                }

                test "another source object is another source, whatever its content" {
                    let build, builds = counting ()
                    let r = render Map.empty
                    let twin = { table with Columns = table.Columns }
                    PreparedSources.prepare r (table :> obj) build |> ignore
                    PreparedSources.prepare r (twin :> obj) build |> ignore
                    Expect.equal (builds ()) 2 "the key is the object, never its content"
                }

                test "a refusal is kept as the refusal, not retried per site" {
                    let mutable builds = 0
                    let r = render Map.empty
                    let source = [ "not"; "a"; "table" ] :> obj

                    let refuse () =
                        builds <- builds + 1
                        Error "not a table"

                    let first = PreparedSources.prepare r source refuse
                    let second = PreparedSources.prepare r source refuse
                    Expect.equal builds 1 "the failed conversion ran once"

                    match first, second with
                    | Error a, Error b -> Expect.equal b a "with the same words"
                    | _ -> failtest "a refusal became an answer"
                } ] ]
