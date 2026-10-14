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


// Phase 2252 — the probe trees for the two inputs the facts walk changed. The
// source groups `amount` by `dept` into `total`, so `amount` is ungrounded.
let private probeSource =
    """{"$type":"Transform","pipeline":[{"$type":"groupBy","aggs":[{"fn":"sum","name":"total","of":"amount"}],"keys":["dept"]}],"source":{"columns":{"amount":{"validity":[true,true,true],"values":[100,120,90]},"dept":{"validity":[true,true,true],"values":["eng","eng","sales"]}},"schema":[{"name":"dept","type":"string"},{"name":"amount","type":"int"}]}}"""

let private probeChart (id: string) (ys: string list) =
    sprintf
        """{"id":"%s","kind":{"$type":"Chart","kind":"Bar","source":%s,"xField":"dept","yFields":[%s]}}"""
        id
        probeSource
        (ys |> List.map (sprintf "\"%s\"") |> String.concat ",")

let private probeGrid (id: string) (cols: string list) =
    sprintf
        """{"id":"%s","kind":{"$type":"DataGrid","columns":[%s],"rowKeyField":"dept","source":%s}}"""
        id
        (cols
         |> List.map (fun c -> sprintf """{"field":"%s","kind":{"$type":"Text"},"label":"%s"}""" c c)
         |> String.concat ",")
        probeSource

let private probeCustom (id: string) (inner: string) =
    sprintf
        """{"id":"%s","kind":{"$type":"Custom","componentId":"trend-card","moduleId":"analytics","props":{"inner":%s}}}"""
        id
        inner

let private probeBox (children: string list) : Node<obj> =
    let json =
        sprintf
            """{"id":"root","kind":{"$type":"Box","children":[%s],"layout":{"$type":"Auto"},"role":"Dashboard"}}"""
            (String.concat "," children)

    match Fuaran.UI.Ops.JsonDecode.decodeNodeObj json with
    | Ok n -> n
    | Error e -> failtestf "the probe tree does not decode: %s at %s" e.Code e.Path

/// Whether a JSONPath of the shape the report writes (`$`, `.member`, `[i]`)
/// names a value in the tree's canonical document.
let private resolves (tree: Node<obj>) (path: string) : bool =
    let rec go (v: JVal) (rest: string) =
        if rest = "" then
            true
        elif rest.[0] = '.' then
            let stop = rest.IndexOfAny([| '.'; '[' |], 1)

            let key, tail =
                if stop < 0 then
                    rest.Substring 1, ""
                else
                    rest.Substring(1, stop - 1), rest.Substring stop

            match v with
            | JObj members ->
                match members |> List.tryFind (fun (k, _) -> k = key) with
                | Some(_, child) -> go child tail
                | None -> false
            | _ -> false
        elif rest.[0] = '[' then
            let close = rest.IndexOf ']'

            match v, System.Int32.TryParse(rest.Substring(1, close - 1)) with
            | JArr items, (true, i) when i < List.length items -> go (List.item i items) (rest.Substring(close + 1))
            | _ -> false
        else
            false

    path.StartsWith "$" && go (Generated.encodeNodeJson tree) (path.Substring 1)

/// The report as (reader id, reader kind, source path, [diagnostic code, path]).
let private reportOf (tree: Node<obj>) =
    bindingChecks tree
    |> List.map (fun c -> c.NodeId, c.Reader, c.Path, c.Diagnostics |> List.map (fun d -> d.Code, d.Path))

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
          }

          // Phase 2252 — a duplicated reader id. The rules judge every occurrence,
          // so a finding can be about the second; it is located at the slot of
          // the occurrence that raised it. Before 2054 it fell back to the first
          // occurrence's `kind`; 2054 put the second's slot under the first's
          // node, a path that did not resolve or named another slot.
          test "a finding on a repeated reader id is located at the occurrence that raised it" {
              let cases =
                  [ "a second chart's extra series",
                    [ probeChart "c" [ "total" ]; probeChart "c" [ "total"; "amount" ] ],
                    [ "FUARAN086", "$.kind.children[1].kind.yFields[1]" ]
                    "both charts naming the ungrounded column",
                    [ probeChart "c" [ "amount" ]; probeChart "c" [ "total"; "amount" ] ],
                    [ "FUARAN086", "$.kind.children[0].kind.yFields[0]"
                      "FUARAN086", "$.kind.children[1].kind.yFields[1]" ]
                    "a shorter second chart",
                    [ probeChart "c" [ "total"; "total" ]; probeChart "c" [ "amount" ] ],
                    [ "FUARAN086", "$.kind.children[1].kind.yFields[0]" ] ]

              for name, children, expected in cases do
                  let tree = probeBox children

                  for _, _, _, diagnostics in reportOf tree do
                      for _, path in diagnostics do
                          Expect.isTrue (resolves tree path) (sprintf "%s: %s names a slot in the tree" name path)

                  match reportOf tree with
                  | [ id, reader, path, diagnostics ] ->
                      Expect.equal
                          (id, reader, path)
                          ("c", "Chart", "$.kind.children[0].kind.source")
                          (name + ": one entry, at the first occurrence")

                      Expect.equal diagnostics expected (name + ": each finding at its own occurrence's slot")
                  | other -> failtestf "%s: expected one reader, got %A" name other

              let grids =
                  probeBox [ probeGrid "g" [ "dept" ]; probeGrid "g" [ "dept"; "amount" ] ]

              match reportOf grids with
              | [ _, _, _, diagnostics ] ->
                  for _, path in diagnostics do
                      Expect.isTrue (resolves grids path) (sprintf "%s names a slot in the tree" path)

                  Expect.equal
                      diagnostics
                      [ "FUARAN114", "$.kind.children[1].kind.columns[1].field" ]
                      "a grid's finding too"
              | other -> failtestf "expected one grid, got %A" other
          }

          // Phase 2252 — a `Custom` prop bag is opaque. A chart- or grid-shaped
          // object inside one is no reader: no rule judges it and no renderer
          // draws it as one. Before 2054 the report listed it, and where it shared
          // a real reader's id and came first it shadowed that reader.
          test "a chart- or grid-shaped object in a Custom prop bag is not a reader" {
              Expect.isEmpty
                  (reportOf (probeBox [ probeCustom "k" (probeChart "phantom" [ "amount" ]) ]))
                  "a chart-shaped bag value"

              Expect.isEmpty
                  (reportOf (probeBox [ probeCustom "k" (probeGrid "phantom" [ "amount" ]) ]))
                  "a grid-shaped bag value"

              Expect.equal
                  (reportOf (
                      probeBox
                          [ probeCustom "k" (probeChart "c" [ "total" ])
                            probeChart "c" [ "total"; "amount" ] ]
                  ))
                  [ "c",
                    "Chart",
                    "$.kind.children[1].kind.source",
                    [ "FUARAN086", "$.kind.children[1].kind.yFields[1]" ] ]
                  "a bag value sharing a real chart's id does not stand in for it"
          } ]
