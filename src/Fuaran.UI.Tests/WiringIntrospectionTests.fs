module Fuaran.UI.Tests.WiringIntrospection

// ============================================================================
//  Phase 1844 — the wiring section of the introspection surface
//  (`Fuaran.UI.Renderer.Introspection`), served by `window.__fuaran` as
//  `getWiring()` / `describeWiring()`.
//
//  Four things are under test:
//
//   1. NO SECOND WALK. The DTO is a projection of `WiringGraph` and nothing
//      else, so over EVERY corpus `nodes/` fixture it must say exactly what the
//      graph says — every control, consumer, edge and unresolved end, the
//      untagged reads and both opacity flags. A DTO that learned something the
//      graph does not know (or lost something it does) fails here by name.
//
//   2. THE EDGES THE SHARD NAMES, over the corpus. Every `Query.dependsOn` name
//      in the `filters-dependson-*` and `Query`-carrying fixtures surfaces as a
//      `declared-edge` consumer on the filter channel, and resolves to either
//      an edge or an `ungrounded` entry — never to silence.
//
//   3. THE CROSS-HOST BYTE CONTRACT. The canonical JSON and the REPL text of a
//      named set of documents are pinned as vectors under
//      `wiring-introspection/`; the TypeScript mirror carries the same bytes
//      and decodes + re-encodes + describes them to the same bytes. One vector
//      is synthetic and exists for the string escaping — a name with a quote,
//      a backslash, a control character, U+2028, a surrogate pair and a lone
//      surrogate — because that is where "canonical JSON" quietly means two
//      different things on two hosts.
//
//   4. ORDER, with its falsifier run both ways: the same wiring spelled in
//      opposite sibling order encodes to identical bytes, AND the graph's own
//      walk-order lists differ — the second half is what stops the first from
//      passing vacuously if the sort were ever removed.
//
//  Regenerating the vectors after an intended change:
//  `FUARAN_WIRING_INTROSPECTION_REGEN=1` rewrites them from this host; copy
//  the directory to the TypeScript mirror's `test/fixtures/wiring-introspection/`
//  in the same change, or the two hosts stop comparing the same bytes.
// ============================================================================

open System.IO
open System.Text.Json
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Renderer

type private Msg = NoOp

// ── the corpus ──────────────────────────────────────────────────────────────

let private nodesDir () : string option =
    Fuaran.Tests.CorpusRoot.tryFind ()
    |> Option.map (fun r -> Path.Combine(r, "nodes"))
    |> Option.filter Directory.Exists

let private fileName (p: string) : string =
    Path.GetFileName p |> Option.ofObj |> Option.defaultValue p

/// Every `nodes/*.json` fixture as (file name, contents), in a stable order.
let private nodeFixtures () : (string * string) list =
    match nodesDir () with
    | None -> []
    | Some d ->
        Directory.GetFiles(d, "*.json")
        |> Array.toList
        |> List.sortBy fileName
        |> List.map (fun p -> fileName p, File.ReadAllText p)

let private graphOf (name: string) (json: string) : WiringGraph.WiringGraph =
    match JsonDecode.decodeNodeObj json with
    | Ok node -> WiringGraph.project node
    | Error e -> failwithf "%s did not decode: %A" name e

// ── the vectors ─────────────────────────────────────────────────────────────

/// The documents whose DTO bytes are pinned for the cross-host comparison: the
/// declared and undeclared `dependsOn` pair, the declared and undeclared
/// transform-param pair, a `Query.dependsOn` on its own, the write-back shape
/// (a `Select` committing to a filter a grid's transform reads), a transform
/// param on a grid, an `Expr` reading state and selection, and a `Call into:`.
let private vectorDocuments =
    [ "filters-dependson-declared"
      "filters-dependson-undeclared"
      "filters-param-source-declared"
      "filters-param-source-undeclared"
      "query-dependson"
      "multiselect-chip-list-param"
      "grid-transform-param"
      "expr-params-state-selection"
      "call-into" ]

let private vectorsDir () : string option =
    Fuaran.Tests.CorpusRoot.tryRepoRoot ()
    |> Option.map (fun r -> Path.Combine(r, "src", "Fuaran.UI.Tests", "wiring-introspection"))

let private regenerating () =
    System.Environment.GetEnvironmentVariable "FUARAN_WIRING_INTROSPECTION_REGEN" = "1"

/// The synthetic escaping vector's DTO. Hand-built, because no corpus document
/// carries these names and none should.
let private escapingDto: Introspection.WiringIntrospection =
    // A LONE surrogate cannot be written in a literal: the compiler replaces
    // one with U+FFFD, which is exactly the substitution under test. Built.
    let lone = string (char 0xD800)
    let awkward = "q\"b\\c\u0001n\nt\tl\u2028pair\U0001F600lone" + lone + "end"

    { Controls =
        [ { Introspection.WiringControlEntry.Channel = "filter"
            Name = awkward
            NodeId = "chips"
            Kind = "declared-filter" } ]
      Consumers =
        [ { Introspection.WiringConsumerEntry.Channel = "filter"
            Name = awkward
            NodeId = "grid"
            Kind = "declared-edge" } ]
      Edges =
        [ { Introspection.WiringEdgeEntry.Channel = "filter"
            Name = awkward
            Control = "chips"
            Consumer = "grid"
            Consumption = "declared-edge"
            ControlKinds = [ "declared-filter" ] } ]
      Unresolved = []
      UntaggedStateReads = [ "sort\u001fkey" ]
      OpaqueReader = false
      OpaqueWriter = true }

/// The UTF-8 bytes a vector file must hold, written without a BOM.
let private utf8 (text: string) : byte array =
    System.Text.UTF8Encoding(false).GetBytes text

let private checkVector (dir: string) (file: string) (expected: string) : string option =
    let path = Path.Combine(dir, file)

    if regenerating () then
        Directory.CreateDirectory dir |> ignore
        File.WriteAllBytes(path, utf8 expected)
        None
    elif not (File.Exists path) then
        Some(sprintf "%s is missing (FUARAN_WIRING_INTROSPECTION_REGEN=1 writes it)" file)
    elif File.ReadAllBytes path <> utf8 expected then
        Some(sprintf "%s differs from this host's emission:\n%s" file expected)
    else
        None

// ── the hand-built trees for the order falsifier ────────────────────────────

let private crossChip (nodeId: string) (declares: string) (reads: string) : Node<Msg> =
    Fuaran.filters
        nodeId
        [ { Name = declares
            Label = TextSource.Literal declares
            Kind = FormFieldKind.Text(Some(Binding.Filter(reads, None)), None) } ]

let private dashboard (id: string) (children: Node<Msg> list) : Node<Msg> =
    Fuaran.dashboard
        id
        { Defaults.dashboard<Msg> with
            Children = children }

// ── the projection's own view of a graph, for the agreement check ───────────

let private graphTokens (g: WiringGraph.WiringGraph) =
    let ch = WiringGraph.WiringChannel.name
    let ck = WiringGraph.ControlKind.name
    let rk = WiringGraph.ConsumerKind.name

    let controls =
        g.Controls
        |> List.map (fun c -> ch c.Channel, c.Name, c.NodeId, ck c.Kind)
        |> Set.ofList

    let consumers =
        g.Consumers
        |> List.map (fun r -> ch r.Channel, r.Name, r.NodeId, rk r.Kind)
        |> Set.ofList

    let edges =
        g.Edges
        |> List.map (fun e -> ch e.Channel, e.Name, e.Control, e.Consumer, rk e.Consumption)
        |> Set.ofList

    let unresolved =
        g.Unresolved
        |> List.map (fun u ->
            match u with
            | WiringGraph.UnresolvedWiring.UndrivenControl c -> "undriven", ch c.Channel, c.Name, c.NodeId, ck c.Kind
            | WiringGraph.UnresolvedWiring.UngroundedConsumer r ->
                "ungrounded", ch r.Channel, r.Name, r.NodeId, rk r.Kind)
        |> Set.ofList

    controls, consumers, edges, unresolved

let private dtoTokens (w: Introspection.WiringIntrospection) =
    let controls =
        w.Controls
        |> List.map (fun c -> c.Channel, c.Name, c.NodeId, c.Kind)
        |> Set.ofList

    let consumers =
        w.Consumers
        |> List.map (fun r -> r.Channel, r.Name, r.NodeId, r.Kind)
        |> Set.ofList

    let edges =
        w.Edges
        |> List.map (fun e -> e.Channel, e.Name, e.Control, e.Consumer, e.Consumption)
        |> Set.ofList

    let unresolved =
        w.Unresolved
        |> List.map (fun u -> u.Reason, u.Channel, u.Name, u.NodeId, u.Kind)
        |> Set.ofList

    controls, consumers, edges, unresolved

/// Why the DTO disagrees with the graph it was projected from, or `None`.
let private disagreement (g: WiringGraph.WiringGraph) (w: Introspection.WiringIntrospection) : string option =
    let gc, gr, ge, gu = graphTokens g
    let wc, wr, we, wu = dtoTokens w

    let controlKindsOk =
        w.Edges
        |> List.forall (fun e ->
            not (List.isEmpty e.ControlKinds)
            && e.ControlKinds
               |> List.forall (fun k -> Set.contains (e.Channel, e.Name, e.Control, k) wc))

    let lengthsOk =
        // Sets hide duplicates; the DTO's sections are deduplicated by
        // contract, so each section's LENGTH must equal the distinct count too.
        List.length w.Controls = Set.count wc
        && List.length w.Consumers = Set.count wr
        && List.length w.Edges = Set.count we
        && List.length w.Unresolved = Set.count wu

    if gc <> wc then
        Some "controls"
    elif gr <> wr then
        Some "consumers"
    elif ge <> we then
        Some "edges"
    elif gu <> wu then
        Some "unresolved"
    elif Set.ofList w.UntaggedStateReads <> g.UntaggedStateReads then
        Some "untagged state reads"
    elif w.OpaqueReader <> g.OpaqueReader || w.OpaqueWriter <> g.OpaqueWriter then
        Some "opacity flags"
    elif not controlKindsOk then
        Some "edge control kinds"
    elif not lengthsOk then
        Some "duplicate entries"
    else
        None

/// Every `dependsOn` name anywhere in a fixture's JSON, read with a JSON
/// reader rather than the decoder — the oracle must not share the code under
/// test.
let private dependsOnNames (json: string) : string list =
    let rec collect (e: JsonElement) : string list =
        match e.ValueKind with
        | JsonValueKind.Object ->
            e.EnumerateObject()
            |> Seq.toList
            |> List.collect (fun p ->
                if p.Name = "dependsOn" && p.Value.ValueKind = JsonValueKind.Array then
                    p.Value.EnumerateArray()
                    |> Seq.choose (fun v ->
                        if v.ValueKind = JsonValueKind.String then
                            v.GetString() |> Option.ofObj
                        else
                            None)
                    |> Seq.toList
                else
                    collect p.Value)
        | JsonValueKind.Array -> e.EnumerateArray() |> Seq.toList |> List.collect collect
        | _ -> []

    use doc = JsonDocument.Parse json
    collect doc.RootElement |> List.distinct

[<Tests>]
let tests =
    testList
        "wiring introspection (Phase 1844)"
        [

          // ── 1. no second walk ─────────────────────────────────────────────

          test "the DTO says exactly what the wiring graph says, on every nodes/ fixture" {
              match nodesDir () with
              | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
              | Some _ ->
                  let corpus = nodeFixtures ()
                  Expect.isNonEmpty corpus "the nodes/ family is not empty"

                  let disagreeing =
                      corpus
                      |> List.choose (fun (name, json) ->
                          let g = graphOf name json

                          disagreement g (Introspection.ofGraph g)
                          |> Option.map (fun what -> sprintf "%s (%s)" name what))

                  Expect.isEmpty disagreeing (sprintf "the DTO and the graph disagree on: %A" disagreeing)

                  let edges =
                      corpus
                      |> List.sumBy (fun (name, json) -> (Introspection.ofGraph (graphOf name json)).Edges.Length)

                  printfn
                      "[wiring-introspection] %d nodes/ fixtures project to %d edges (%s)"
                      corpus.Length
                      edges
                      Introspection.FormatVersion

                  Expect.isGreaterThan edges 0 "the corpus carries resolved edges for the DTO to surface"
          }

          test "the canonical JSON is well-formed and carries every section, on every nodes/ fixture" {
              match nodesDir () with
              | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
              | Some _ ->
                  let broken =
                      nodeFixtures ()
                      |> List.choose (fun (name, json) ->
                          let w = Introspection.ofGraph (graphOf name json)
                          use doc = JsonDocument.Parse(Introspection.toJson w)
                          let root = doc.RootElement
                          let len (key: string) = root.GetProperty(key).GetArrayLength()

                          let ok =
                              root.GetProperty("format").GetString() = Introspection.FormatVersion
                              && len "controls" = w.Controls.Length
                              && len "consumers" = w.Consumers.Length
                              && len "edges" = w.Edges.Length
                              && len "unresolved" = w.Unresolved.Length
                              && len "untaggedStateReads" = w.UntaggedStateReads.Length

                          if ok then None else Some name)

                  Expect.isEmpty broken (sprintf "these fixtures encoded a DTO that does not read back: %A" broken)
          }

          test "no render path reads the projection — only the console surface serves it" {
              match Fuaran.Tests.CorpusRoot.tryRepoRoot () with
              | None -> skiptest "this assembly is not inside the repo, so its sources cannot be read"
              | Some root ->
                  // Phase 1736's acceptance ("no render path references the
                  // wiring graph") admits exactly one renderer file, this
                  // module, and this is the other half of that exemption: the
                  // projection is reached from the on-demand console surface
                  // and from nowhere a render runs.
                  let dir = Path.Combine(root, "src", "Fuaran.UI.Renderer")

                  let callers =
                      Directory.GetFiles(dir, "*.fs", SearchOption.AllDirectories)
                      |> Array.toList
                      |> List.filter (fun f ->
                          fileName f <> "Introspection.fs"
                          && System.Text.RegularExpressions.Regex.IsMatch(
                              File.ReadAllText f,
                              @"\bIntrospection\.(ofTree|ofGraph|toJson|describe)\b"
                          ))
                      |> List.map fileName

                  Expect.equal callers [ "DebugGlobal.fs" ] "the console surface is the projection's one caller"
          }

          // ── 2. the edges the shard names ──────────────────────────────────

          test "every dependsOn name in the filters-dependson-* and Query-carrying fixtures surfaces as a declared edge" {
              match nodesDir () with
              | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
              | Some _ ->
                  let population =
                      nodeFixtures ()
                      |> List.filter (fun (name, json) ->
                          name.StartsWith "filters-dependson-" || json.Contains "\"$type\":\"Query\"")

                  let names = population |> List.map fst

                  for expected in
                      [ "filters-dependson-declared.json"
                        "filters-dependson-undeclared.json"
                        "query-dependson.json" ] do
                      Expect.contains names expected (sprintf "%s is in the population" expected)

                  let silent =
                      population
                      |> List.collect (fun (name, json) ->
                          let w = Introspection.ofGraph (graphOf name json)

                          dependsOnNames json
                          |> List.filter (fun dep ->
                              let consumed =
                                  w.Consumers
                                  |> List.exists (fun r ->
                                      r.Channel = "filter" && r.Name = dep && r.Kind = "declared-edge")

                              let resolved =
                                  w.Edges
                                  |> List.exists (fun e ->
                                      e.Channel = "filter" && e.Name = dep && e.Consumption = "declared-edge")

                              let ungrounded =
                                  w.Unresolved
                                  |> List.exists (fun u ->
                                      u.Reason = "ungrounded"
                                      && u.Channel = "filter"
                                      && u.Name = dep
                                      && u.Kind = "declared-edge")

                              not (consumed && (resolved || ungrounded)))
                          |> List.map (fun dep -> sprintf "%s: %s" name dep))

                  Expect.isEmpty silent (sprintf "these dependsOn names reached no edge and no finding: %A" silent)
          }

          test "a declared chip drives its dependsOn consumer; an undeclared name is ungrounded, not an edge" {
              match nodesDir () with
              | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
              | Some _ ->
                  let byName = nodeFixtures () |> Map.ofList

                  let dtoOf (n: string) =
                      Introspection.ofGraph (graphOf n byName.[n])

                  let declared = dtoOf "filters-dependson-declared.json"

                  Expect.isTrue
                      (declared.Edges
                       |> List.exists (fun e ->
                           e.Channel = "filter"
                           && e.Consumption = "declared-edge"
                           && e.ControlKinds = [ "declared-filter" ]))
                      "the declared chip's dependsOn edge is an edge, driven by the chip"

                  let undeclared = dtoOf "filters-dependson-undeclared.json"

                  // The fixture's `dependsOn` names two filters and its chip
                  // declares ONE of them (`region`), so the pair is the whole
                  // question in one document: the declared name is an edge,
                  // the undeclared one (`genre`) is not, and is a finding.
                  Expect.isTrue
                      (undeclared.Edges
                       |> List.exists (fun e -> e.Name = "region" && e.Consumption = "declared-edge"))
                      "the declared name is an edge"

                  Expect.isFalse
                      (undeclared.Edges |> List.exists (fun e -> e.Name = "genre"))
                      "an undeclared dependsOn name is not an edge"

                  Expect.isTrue
                      (undeclared.Unresolved
                       |> List.exists (fun u -> u.Reason = "ungrounded" && u.Name = "genre" && u.Kind = "declared-edge"))
                      "it is reported ungrounded instead"
          }

          test "a write-back position is visible as the driver of the edge it closes" {
              match nodesDir () with
              | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
              | Some _ ->
                  let byName = nodeFixtures () |> Map.ofList
                  let n = "multiselect-chip-list-param.json"
                  let w = Introspection.ofGraph (graphOf n byName.[n])

                  Expect.isTrue
                      (w.Edges
                       |> List.exists (fun e ->
                           e.Channel = "filter"
                           && e.Consumption = "declared-edge"
                           && List.contains "filter-write-back" e.ControlKinds))
                      "the Select's write-back drives the grid's transform param, and the edge says so"
          }

          // ── 3. the cross-host byte contract ───────────────────────────────

          test "the pinned vectors are this host's emission, byte for byte" {
              match nodesDir (), vectorsDir () with
              | None, _ -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
              | _, None -> skiptest "this assembly is not inside the repo, so its vectors cannot be read"
              | Some nodes, Some dir ->
                  let fromCorpus =
                      vectorDocuments
                      |> List.collect (fun doc ->
                          let path = Path.Combine(nodes, doc + ".json")
                          Expect.isTrue (File.Exists path) (sprintf "the corpus still carries %s" doc)
                          let w = Introspection.ofGraph (graphOf doc (File.ReadAllText path))

                          [ checkVector dir (doc + ".json") (Introspection.toJson w)
                            checkVector dir (doc + ".txt") (Introspection.describe w) ])

                  let synthetic =
                      [ checkVector dir "synthetic-escaping.json" (Introspection.toJson escapingDto)
                        checkVector dir "synthetic-escaping.txt" (Introspection.describe escapingDto) ]

                  let failures = fromCorpus @ synthetic |> List.choose id
                  Expect.isEmpty failures (String.concat "\n" failures)
          }

          test "strings are escaped exactly as JSON.stringify escapes them" {
              // The expected text is written out by hand from the ECMAScript
              // algorithm, not produced by any encoder in this process.
              let json = Introspection.toJson escapingDto

              let expected = "\"q\\\"b\\\\c\\u0001n\\nt\\tl\u2028pair\U0001F600lone\\ud800end\""

              Expect.stringContains json expected "the awkward name encodes as JSON.stringify would"
              Expect.stringContains json "\"sort\\u001fkey\"" "a unit separator is a \\u escape"
          }

          // ── 4. order, both directions ─────────────────────────────────────

          test "the DTO bytes are invariant under sibling order, and the walk-order lists are not" {
              let fa = crossChip "fa" "alpha" "beta"
              let fb = crossChip "fb" "beta" "alpha"
              let forward = WiringGraph.project (dashboard "root" [ fa; fb ])
              let reversed = WiringGraph.project (dashboard "root" [ fb; fa ])

              Expect.notEqual
                  forward.Controls
                  reversed.Controls
                  "the graph's walk-order lists DO differ — otherwise the byte check below proves nothing"

              Expect.equal
                  (Introspection.toJson (Introspection.ofGraph forward))
                  (Introspection.toJson (Introspection.ofGraph reversed))
                  "the canonical JSON depends on the wiring, not the spelling"

              Expect.equal
                  (Introspection.describe (Introspection.ofGraph forward))
                  (Introspection.describe (Introspection.ofGraph reversed))
                  "and so does the REPL text"
          } ]
