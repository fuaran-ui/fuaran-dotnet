module Fuaran.UI.Tests.PropagationLawVectorTests

// Phase 2328 — the F# host reads back `laws/propagation-laws.json`.
//
// Fuaran-Core emits the propagation law vectors (fuaran-core Phase 538,
// `Fuaran.Core.Conformance.propagationEvalLaws`) and the corpus carries a
// byte-identical copy (WIRE_FORMAT §27.8.2). This tier emits none of them, so the
// emitter is never its own only reader: every vector is recomputed here through
// the `Fuaran.Core.Propagation` this repository PINS — the same
// `dirtyFromChangedIds` `BindingGraph.dirty` closes a write over, the same `sort`
// `BindingCycleTests` reads cycles from — and never trusted.
//
// The evaluator is the one the file's description states: value(n) = base(n) +
// the sum of value(r) over n's declared reads r, a read that resolves to nothing
// counting 0.

open System.IO
open System.Text.Json
open Expecto
open Fuaran.Core

let private fileName = "propagation-laws.json"

type private Vector =
    { Id: string
      Case: string
      Input: JsonElement
      Expected: JsonElement }

let private strings (e: JsonElement) : string list =
    [ for x in e.EnumerateArray() -> x.GetString() |> nonNull ]

let private deps (input: JsonElement) : Map<string, Set<string>> =
    [ for p in input.GetProperty("deps").EnumerateObject() -> p.Name, Set.ofList (strings p.Value) ]
    |> Map.ofList

let private ints (e: JsonElement) : Map<string, int> =
    [ for p in e.EnumerateObject() -> p.Name, p.Value.GetInt32() ] |> Map.ofList

/// The vectors' evaluator over `bases`, recording every node it is invoked on.
/// `extra` adds one read a node never declared (`undeclaredRead`).
let private evaluator
    (deps: Map<string, Set<string>>)
    (bases: Map<string, int>)
    (invoked: System.Collections.Generic.List<string>)
    (extra: (string * string) option)
    : (string -> int option) -> string -> Result<int, string> =
    fun resolve id ->
        invoked.Add id

        let reads =
            (Map.tryFind id deps |> Option.defaultValue Set.empty |> Set.toList)
            @ (match extra with
               | Some(node, read) when node = id -> [ read ]
               | _ -> [])

        let sum = reads |> List.sumBy (fun r -> resolve r |> Option.defaultValue 0)
        Ok((Map.tryFind id bases |> Option.defaultValue 0) + sum)

let private refusalOf (e: Propagation.PropagationError) : string * string list =
    match e with
    | Propagation.PropagationError.EvalUnknownChange ids -> "evalUnknownChange", ids
    | Propagation.PropagationError.EvalNodeFailed(node, message) -> "evalNodeFailed", [ node; message ]
    | Propagation.PropagationError.EvalUndeclaredRead(node, read) -> "evalUndeclaredRead", [ node; read ]

let private expectedRefusal (expected: JsonElement) : string * string list =
    let error = expected.GetProperty("error").GetString() |> nonNull

    match error with
    | "evalUnknownChange" -> error, strings (expected.GetProperty "ids")
    | _ ->
        error,
        [ expected.GetProperty("node").GetString() |> nonNull
          expected.GetProperty("read").GetString() |> nonNull ]

/// One vector's verdict: `None` when the pinned implementation reproduces it,
/// else the disagreement.
let private check (v: Vector) : string option =
    let d = deps v.Input

    let mismatch what (want: obj) (got: obj) =
        Some(sprintf "%s: %s — the vector expects %A, the pinned Propagation gives %A" v.Id what want got)

    match v.Case with
    | "dirtySet" ->
        let changed = Set.ofList (strings (v.Input.GetProperty "changed"))
        let want = strings (v.Expected.GetProperty "dirty")
        let got = Propagation.dirtyFromChangedIds d changed |> Set.toList

        if got = want then None else mismatch "dirty set" want got
    | "evaluationOrder" ->
        let topo = Propagation.sort d
        let wantOrder = strings (v.Expected.GetProperty "order")

        let wantCycles =
            [ for c in v.Expected.GetProperty("cycles").EnumerateArray() -> strings c ]

        let gotCycles = topo.Cycles |> List.map List.sort

        if topo.Order <> wantOrder then
            mismatch "evaluation order" wantOrder topo.Order
        elif gotCycles <> wantCycles then
            mismatch "cyclic groups" wantCycles gotCycles
        else
            None
    | "evalFrom" ->
        let bases = ints (v.Input.GetProperty "base")
        let changedBase = ints (v.Input.GetProperty "changedBase")
        let changed = Set.ofList (strings (v.Input.GetProperty "changed"))
        let primed = System.Collections.Generic.List<string>()

        match Propagation.eval (evaluator d bases primed None) d with
        | Error e -> mismatch "the priming full evaluation" "success" e
        | Ok prior ->
            let invoked = System.Collections.Generic.List<string>()

            let result =
                Propagation.evalFrom (evaluator d changedBase invoked None) prior.Values changed d

            match v.Expected.GetProperty("verdict").GetString(), result with
            | "accept", Ok outcome ->
                let wantValues = ints (v.Expected.GetProperty "values")
                let wantRecomputed = strings (v.Expected.GetProperty "recomputed")
                let gotRecomputed = invoked |> Seq.distinct |> Seq.sort |> List.ofSeq

                let wantCycles =
                    [ for c in v.Expected.GetProperty("cycles").EnumerateArray() -> strings c ]

                if outcome.Values <> wantValues then
                    mismatch "incremental values" wantValues outcome.Values
                elif gotRecomputed <> wantRecomputed then
                    mismatch "recomputed nodes" wantRecomputed gotRecomputed
                elif (outcome.Cyclic |> List.map List.sort) <> wantCycles then
                    mismatch "cyclic groups" wantCycles outcome.Cyclic
                else
                    None
            | "reject", Error e ->
                let want = expectedRefusal v.Expected
                let got = refusalOf e
                if got = want then None else mismatch "refusal" want got
            | verdict, got -> mismatch "verdict" (string verdict) got
    | "undeclaredRead" ->
        let bases = ints (v.Input.GetProperty "base")
        let node = v.Input.GetProperty("node").GetString() |> nonNull
        let read = v.Input.GetProperty("read").GetString() |> nonNull
        let want = expectedRefusal v.Expected
        let scratch = System.Collections.Generic.List<string>()
        let lying = evaluator d bases scratch (Some(node, read))

        match Propagation.eval lying d with
        | Ok _ -> mismatch "eval verdict" want "accept"
        | Error e when refusalOf e <> want -> mismatch "eval refusal" want (refusalOf e)
        | Error _ ->
            match Propagation.eval (evaluator d bases scratch None) d with
            | Error e -> mismatch "the priming full evaluation" "success" e
            | Ok prior ->
                match Propagation.evalFrom lying prior.Values (Set.singleton node) d with
                | Ok _ -> mismatch "evalFrom verdict" want "accept"
                | Error e when refusalOf e <> want -> mismatch "evalFrom refusal" want (refusalOf e)
                | Error _ -> None
    | other -> Some(sprintf "%s: the case `%s` is not one this reader knows — extend it with the family" v.Id other)

let private load (path: string) : Vector list =
    use doc = JsonDocument.Parse(File.ReadAllText path)

    [ for e in doc.RootElement.GetProperty("vectors").EnumerateArray() ->
          { Id = e.GetProperty("id").GetString() |> nonNull
            Case = e.GetProperty("case").GetString() |> nonNull
            Input = e.GetProperty("input").Clone()
            Expected = e.GetProperty("expected").Clone() } ]

[<Tests>]
let tests =
    testList
        "Phase 2328 — laws/propagation-laws.json read back through the pinned Propagation"
        [ testCase "every propagation vector is reproduced by the pinned Fuaran.Core.Propagation"
          <| fun _ ->
              match Fuaran.Tests.CorpusRoot.tryFind () with
              | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
              | Some root ->
                  let path = Path.Combine(root, "laws", fileName)

                  if not (File.Exists path) then
                      failtestf
                          "laws/%s is missing from the corpus — it is Fuaran-Core's (`--emit-laws`), copied byte for byte"
                          fileName

                  let vectors = load path

                  let cases = vectors |> List.map _.Case |> Set.ofList

                  Expect.equal
                      cases
                      (set [ "dirtySet"; "evaluationOrder"; "evalFrom"; "undeclaredRead" ])
                      "the file carries the four cases this reader certifies"

                  let failures = vectors |> List.choose check

                  Expect.isEmpty
                      failures
                      (sprintf
                          "%d of %d propagation vectors disagree with the pinned implementation:\n%s"
                          failures.Length
                          vectors.Length
                          (String.concat "\n" failures)) ]
