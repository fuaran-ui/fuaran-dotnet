module Fuaran.UI.Tests.FactsWalk

// ============================================================================
//  Phase 2054 — one facts walk per validation.
//
//  The binding facts (`BindingWalk.collectFacts`) are read by the validator,
//  the binding-check report, the wiring graph and the structural query. Each
//  now has a form taking facts a caller already holds, and each claim below is
//  that the form answers exactly what the tree-taking entry answers — over every
//  node fixture in the wire-format corpus, so the claim is about the whole
//  vocabulary rather than a hand-picked tree.
//
//  The binding-check report no longer encodes the tree to find its readers'
//  JSONPaths; it reads them off the typed tree. The last corpus claim is the
//  pin on that: every reader's path is the path the canonical document gives
//  it, computed here the old way, by encoding and walking the document.
// ============================================================================

open System.IO
open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.PreEmitValidate

let private corpus () : (string * Node<obj>) list option =
    Fuaran.Tests.CorpusRoot.tryFind ()
    |> Option.map (fun r -> Path.Combine(r, "nodes"))
    |> Option.filter Directory.Exists
    |> Option.map (fun dir ->
        Directory.GetFiles(dir, "*.json")
        |> Array.sort
        |> Array.toList
        |> List.choose (fun path ->
            match Fuaran.UI.Ops.JsonDecode.decodeNodeObj (File.ReadAllText path) with
            | Ok n -> Some(string (Path.GetFileName path), n)
            | Error _ -> None))

let private withCorpus (f: (string * Node<obj>) list -> unit) =
    match corpus () with
    | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
    | Some trees ->
        Expect.isGreaterThan (List.length trees) 100 "the corpus decoded — a near-empty set proves nothing"
        f trees

/// The old route to a reader's path: encode the tree and walk the document for
/// node objects whose kind is a Chart or a DataGrid, in document order.
let private encodedReaderPaths (tree: Node<obj>) : (string * string * string) list =
    let found = ResizeArray<string * string * string>()

    let field (key: string) (v: JVal) =
        match v with
        | JObj members -> members |> List.tryFind (fun (k, _) -> k = key) |> Option.map snd
        | _ -> None

    let isIdent (s: string) =
        s.Length > 0
        && (System.Char.IsAsciiLetter s.[0] || s.[0] = '_')
        && Seq.forall (fun c -> System.Char.IsAsciiLetterOrDigit c || c = '_') s

    let memberPath (k: string) =
        if isIdent k then
            "." + k
        else
            "['" + k.Replace("\\", "\\\\").Replace("'", "\\'") + "']"

    let rec go (path: string) (v: JVal) =
        match v with
        | JObj members ->
            match field "id" v, field "kind" v |> Option.bind (field "$type") with
            | Some(JStr id), Some(JStr "Chart") -> found.Add(id, "Chart", path)
            | Some(JStr id), Some(JStr "DataGrid") -> found.Add(id, "DataGrid", path)
            | _ -> ()

            for (k, child) in members do
                go (path + memberPath k) child
        | JArr items -> items |> List.iteri (fun i child -> go (path + "[" + string i + "]") child)
        | _ -> ()

    go "$" (Generated.encodeNodeJson tree)
    let seen = System.Collections.Generic.HashSet<string>()
    found |> Seq.filter (fun (id, _, _) -> seen.Add id) |> List.ofSeq

let private sourceSlot (c: BindingCheck) : string =
    let suffix =
        if c.Path.EndsWith ".staticRows" then
            ".staticRows"
        else
            ".source"

    c.Path.Substring(0, c.Path.Length - suffix.Length - ".kind".Length)

/// Three charts over tables A, B, A, C, B, A (by source value): FUARAN107's
/// pairs, which must come out in the all-pairs scan's (first, second) order.
let private repeatedTables () : Node<obj> =
    let chart (i: int) (seed: int) =
        sprintf
            """{"id":"c%d","kind":{"$type":"Chart","kind":"Bar","source":{"$type":"Transform","pipeline":[],"source":{"columns":{"dept":{"validity":[true],"values":["d%d"]}},"schema":[{"name":"dept","type":"string"}]}},"xField":"dept","yFields":["dept"]}}"""
            i
            seed

    let json =
        sprintf
            """{"id":"root","kind":{"$type":"Box","children":[%s],"layout":{"$type":"Auto"},"role":"Dashboard"}}"""
            (String.concat "," [ chart 0 1; chart 1 2; chart 2 1; chart 3 3; chart 4 2; chart 5 1 ])

    match Fuaran.UI.Ops.JsonDecode.decodeNodeObj json with
    | Ok n -> n
    | Error e -> failtestf "the probe tree does not decode: %s at %s" e.Code e.Path

[<Tests>]
let tests =
    testList
        "Phase 2054 — one facts walk per validation"
        [ test "validateWithFacts answers what validate answers, over the corpus" {
              withCorpus (fun trees ->
                  for name, tree in trees do
                      Expect.equal
                          (validateWithFacts ValidateOptions.defaults (BindingWalk.collectFacts tree) tree)
                          (validate tree)
                          name)
          }

          test "bindingChecksWithFacts answers what bindingChecks answers, over the corpus" {
              withCorpus (fun trees ->
                  for name, tree in trees do
                      Expect.equal
                          (bindingChecksWithFacts (BindingWalk.collectFacts tree) tree)
                          (bindingChecks tree)
                          name)
          }

          test "evaluateWithFacts answers what evaluateWith answers, over the corpus" {
              let options = StructuralQuery.Options.opaque ()

              // Every name the tree reads, on any channel, so the query consults
              // the facts on every tree that has any.
              let readsAnything (facts: BindingWalk.TreeFacts) =
                  facts.Bindings.Uses
                  |> List.choose (fun u ->
                      match u.Use with
                      | BindingWalk.BindingUse.State n
                      | BindingWalk.BindingUse.Filter n
                      | BindingWalk.BindingUse.Selection n
                      | BindingWalk.BindingUse.Query(n, _) ->
                          Some(StructuralQuery.Predicate.BoundTo(StructuralQuery.Channel.Any, n))
                      | _ -> None)
                  |> StructuralQuery.Predicate.Or

              withCorpus (fun trees ->
                  let mutable hits = 0

                  for name, tree in trees do
                      let facts = BindingWalk.collectFacts tree
                      let predicate = readsAnything facts
                      let result = StructuralQuery.evaluateWithFacts options facts predicate tree
                      hits <- hits + List.length result.Hits
                      Expect.equal result (StructuralQuery.evaluateWith options predicate tree) name

                  Expect.isGreaterThan hits 10 "the query matched readers, so the facts were consulted")
          }

          test "every reader the report lists sits at the path the encoded document gives it" {
              withCorpus (fun trees ->
                  let mutable readers = 0

                  for name, tree in trees do
                      let report =
                          bindingChecks tree |> List.map (fun c -> c.NodeId, c.Reader, sourceSlot c)

                      readers <- readers + List.length report
                      Expect.equal report (encodedReaderPaths tree) name

                  Expect.isGreaterThan readers 10 "the corpus holds readers, so the comparison compared something")
          }

          test "a reader under a fallback, a state arm, a switch and a slot argument is located there" {
              let grid (id: string) =
                  sprintf
                      """{"id":"%s","kind":{"$type":"DataGrid","columns":[{"field":"a","kind":{"$type":"Text"},"label":"a"}],"rowKeyField":"a","source":{"$type":"Query","name":"q"}}}"""
                      id

              let json =
                  sprintf
                      """{"id":"root","kind":{"$type":"Switch","cases":[{"child":%s,"match":"x"}],"default":{"id":"ref","kind":{"$type":"FragmentRef","name":"frag","args":{"a b":{"$type":"SlotArg","tree":%s}}}},"stateKey":"k"},"fallback":%s,"state":{"onEmpty":%s,"onLoading":%s}}"""
                      (grid "g-case")
                      (grid "g-arg")
                      (grid "g-fallback")
                      (grid "g-empty")
                      (grid "g-loading")

              match Fuaran.UI.Ops.JsonDecode.decodeNodeObj json with
              | Error e -> failtestf "the probe tree does not decode: %s at %s" e.Code e.Path
              | Ok tree ->
                  let report =
                      bindingChecks tree |> List.map (fun c -> c.NodeId, c.Reader, sourceSlot c)

                  Expect.equal report (encodedReaderPaths tree) "the typed walk and the encoded document agree"

                  Expect.equal
                      (report |> List.map (fun (id, _, p) -> id, p))
                      [ "g-case", "$.kind.cases[0].child"
                        "g-arg", "$.kind.default.kind.args['a b'].tree"
                        "g-fallback", "$.fallback"
                        "g-empty", "$.state.onEmpty"
                        "g-loading", "$.state.onLoading" ]
                      "each held position, spelled as the wire spells it, in document order"
          }

          test "FUARAN107 pairs equal tables only, in first-then-second order" {
              let pairs =
                  match validate (repeatedTables ()) with
                  | Ok() -> []
                  | Error defects ->
                      defects
                      |> List.choose (function
                          | PreEmitDefect.DuplicateInlineTable(a, b, _) -> Some(a, b)
                          | _ -> None)

              Expect.equal
                  pairs
                  [ "c0", "c2"; "c0", "c5"; "c1", "c4"; "c2", "c5" ]
                  "every pair of equal tables, none of unequal ones, ordered by first then second copy"
          } ]
