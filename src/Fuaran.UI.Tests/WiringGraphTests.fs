module Fuaran.UI.Tests.WiringGraph

// ============================================================================
//  Phase 1736 — the wiring-graph projection (`Fuaran.UI.WiringGraph`).
//
//  Three things are under test, and the third is the one worth reading:
//
//   1. WHAT THE GRAPH SAYS, over the shared corpus's wiring fixtures. The
//      corpus carries exactly one fully-wired document, seven whose chips
//      nothing reads (the decorative-wiring trap, already in the corpus) and
//      six whose declared edges name a chip no `Filters` node declares. Those
//      three populations are the projection's three outputs, so the corpus is
//      a real oracle here rather than a smoke test.
//
//   2. BYTE STABILITY over the whole `nodes/` family — the phase's acceptance
//      criterion. Measured, not asserted: every fixture is decoded twice,
//      independently, and the two renderings are compared byte for byte.
//
//   3. THE FALSIFIER FOR (2), run in BOTH directions. A determinism check that
//      only ever re-renders the same list proves nothing about ORDER, and walk
//      order is a property of how a tree is SPELLED. So the same wiring is
//      built twice with the declaring nodes in opposite sibling order, and the
//      test asserts BOTH that the renderings are identical AND that the
//      underlying walk-order lists are NOT — the second half is what stops the
//      first from passing vacuously if the sort were ever removed.
// ============================================================================

open System.IO
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops

type private Msg = NoOp

// ── the corpus ──────────────────────────────────────────────────────────────

let private nodesDir () : string option =
    Fuaran.Tests.CorpusRoot.tryFind ()
    |> Option.map (fun r -> Path.Combine(r, "nodes"))
    |> Option.filter Directory.Exists

/// Every `nodes/*.json` fixture as (file name, contents), in a stable order.
let private nodeFixtures () : (string * string) list =
    match nodesDir () with
    | None -> []
    | Some d ->
        let fileName (p: string) =
            Path.GetFileName p |> Option.ofObj |> Option.defaultValue p

        Directory.GetFiles(d, "*.json")
        |> Array.toList
        |> List.sortBy fileName
        |> List.map (fun p -> fileName p, File.ReadAllText p)

/// Decode a fixture and project it, failing the test rather than skipping when
/// the decode itself is broken — a projection test that silently drops the
/// fixtures it cannot read certifies against nothing.
let private graphOf (name: string) (json: string) : WiringGraph.WiringGraph =
    match JsonDecode.decodeNodeObj json with
    | Ok node -> WiringGraph.project node
    | Error e -> failtestf "%s failed to decode: %s at %s" name e.Code e.Path

let private filterControlNames (g: WiringGraph.WiringGraph) =
    g.Controls
    |> List.filter (fun c -> c.Kind = WiringGraph.ControlKind.DeclaredFilter)
    |> List.map (fun c -> c.Name)
    |> List.sort

let private undrivenFilterNames (g: WiringGraph.WiringGraph) =
    g.Unresolved
    |> List.choose (fun u ->
        match u with
        | WiringGraph.UnresolvedWiring.UndrivenControl c when c.Channel = WiringGraph.WiringChannel.Filter ->
            Some c.Name
        | _ -> None)
    |> List.sort

let private ungroundedFilterEdges (g: WiringGraph.WiringGraph) =
    g.Unresolved
    |> List.choose (fun u ->
        match u with
        | WiringGraph.UnresolvedWiring.UngroundedConsumer r when
            r.Channel = WiringGraph.WiringChannel.Filter
            && r.Kind = WiringGraph.ConsumerKind.DeclaredEdge
            ->
            Some r.Name
        | _ -> None)
    |> List.distinct
    |> List.sort

// ── the hand-built trees for the falsifier ──────────────────────────────────

/// A `Filters` node declaring `declares` and whose chip's value slot reads
/// `reads` — a CROSS-read, so the declaration and the consumption sit on
/// different nodes and every edge in the pair is a genuine resolved edge.
let private crossChip (nodeId: string) (declares: string) (reads: string) : Node<Msg> =
    Fuaran.filters
        nodeId
        [ { Name = declares
            Label = TextSource.Literal declares
            Kind = FormFieldKind.Text(Some(Binding.Filter(reads, None)), None) } ]

/// The 423 declarative chip: it declares a name and reads ITS OWN slot, and
/// nothing else in the tree reads it. FUARAN074's shape, and the projection's
/// `UndrivenControl`.
let private selfChip (nodeId: string) (name: string) : Node<Msg> =
    Fuaran.filters
        nodeId
        [ { Name = name
            Label = TextSource.Literal name
            Kind = FormFieldKind.Text(Some(Binding.Filter(name, None)), None) } ]

let private dashboard (id: string) (children: Node<Msg> list) : Node<Msg> =
    Fuaran.dashboard
        id
        { Defaults.dashboard<Msg> with
            Children = children }

[<Tests>]
let tests =
    testList
        "wiring graph (Phase 1736)"
        [

          // ── the falsifier, both directions ────────────────────────────────

          test "the rendering is invariant under sibling order, and the walk-order lists are not" {
              // Same wiring, opposite declaration order. `fa` declares alpha and
              // reads beta; `fb` declares beta and reads alpha — so both edges
              // resolve and neither read is a self-read.
              let fa = crossChip "fa" "alpha" "beta"
              let fb = crossChip "fb" "beta" "alpha"

              let forward = WiringGraph.project (dashboard "root" [ fa; fb ])
              let reversed = WiringGraph.project (dashboard "root" [ fb; fa ])

              // THE VACUITY CHECK. If this ever becomes false the sibling
              // permutation has stopped perturbing anything and the assertion
              // below stops being evidence about ordering.
              Expect.notEqual
                  forward.Controls
                  reversed.Controls
                  "the permutation must actually change walk order, or the byte assertion below is vacuous"

              Expect.equal
                  (WiringGraph.render forward)
                  (WiringGraph.render reversed)
                  "the canonical rendering depends on the wiring and not on how the tree is spelled"

              // And the wiring itself is what was claimed: two resolved edges,
              // nothing unresolved.
              Expect.equal (List.length forward.Edges) 2 "both cross-reads resolve"
              Expect.isEmpty forward.Unresolved "a fully-wired pair leaves no unresolved end"
          }

          test "a chip that reads only its own slot drives nothing — the decorative-wiring trap" {
              let g = WiringGraph.project (dashboard "root" [ selfChip "chips" "solo" ])

              Expect.equal (filterControlNames g) [ "solo" ] "the chip is a declared-filter control"
              Expect.isEmpty g.Edges "a chip's read of its OWN slot is a declaration, not consumption"
              Expect.equal (undrivenFilterNames g) [ "solo" ] "so the chip is an undriven control"
          }

          test "a selection producer that nothing reads is NOT reported undriven, across the whole corpus" {
              // The express-vs-incidental split, measured rather than asserted:
              // a grid produces a selection by KIND, and no shipped rule fires
              // on an unread one. Over 200-odd fixtures this would be the
              // noisiest possible finding if the split were wrong.
              let offenders =
                  nodeFixtures ()
                  |> List.collect (fun (name, json) ->
                      (graphOf name json).Unresolved
                      |> List.choose (fun u ->
                          match u with
                          | WiringGraph.UnresolvedWiring.UndrivenControl c when
                              c.Kind = WiringGraph.ControlKind.SelectionProducer
                              ->
                              Some(name, c.NodeId)
                          | _ -> None))

              Expect.isEmpty offenders "an incidental control is never an undriven control"
          }

          // ── what the graph says about the corpus ──────────────────────────

          test "the corpus's one fully-wired document resolves both of its chips" {
              match nodesDir () with
              | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
              | Some d ->
                  let name = "filterable-static-dashboard.json"
                  let g = graphOf name (File.ReadAllText(Path.Combine(d, name)))

                  Expect.equal (filterControlNames g) [ "genre"; "region" ] "both chips are declared"

                  let resolved =
                      g.Edges
                      |> List.filter (fun e -> e.Channel = WiringGraph.WiringChannel.Filter)
                      |> List.map (fun e -> e.Name)
                      |> List.distinct
                      |> List.sort

                  Expect.equal resolved [ "genre"; "region" ] "and both are consumed"

                  Expect.isEmpty (undrivenFilterNames g) "the one fully-wired fixture carries no decorative filter"
          }

          test "the corpus's declaration-only fixtures report their chips undriven" {
              match nodesDir () with
              | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
              | Some d ->
                  let expected =
                      [ "filters-1.json", [ "q"; "tier" ]
                        "filters-date-range.json", [ "stay" ]
                        "filters-declarative.json", [ "age"; "q"; "tier" ]
                        "filters-rating-colour.json", [ "stars"; "swatch" ]
                        "filters-segmented.json", [ "view" ]
                        "filters-tokens.json", [ "labels" ] ]

                  for (name, names) in expected do
                      let g = graphOf name (File.ReadAllText(Path.Combine(d, name)))
                      Expect.equal (undrivenFilterNames g) names (name + ": every declared chip is undriven")
          }

          test "the corpus's declared edges over undeclared chips are reported ungrounded" {
              match nodesDir () with
              | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
              | Some d ->
                  // A `Query.dependsOn` name and a param's `Filter` source are
                  // edges the TREE asserts — FUARAN075's subjects. A plain
                  // `Binding.Filter` value read is host-feedable and is
                  // deliberately NOT in this projection's declared-edge set.
                  //
                  // Phase 1785 — `multiselect-chip-list-param` left this table,
                  // and its departure is that phase's whole point rather than an
                  // expectation relaxed to fit. Its grid's param reads `depts`,
                  // which the `Select` beside it WRITES through the renderer's
                  // write-back default; the walk recorded that slot as a read
                  // alone, so the graph reported an edge grounded by nothing. The
                  // fixture is unchanged, the reader is unchanged, and the tree
                  // was never defective — the projection now says so. It is
                  // asserted from the other side in `FilterWriteWalkTests`, over
                  // this same fixture, so the row is covered rather than dropped.
                  let expected =
                      [ "query-dependson.json", [ "region"; "status" ]
                        "form-combobox-query.json", [ "country" ]
                        "form-tokens-query.json", [ "role" ]
                        "grid-transform-param.json", [ "dept" ]
                        "expr-params-state-selection.json", [ "statuses" ]
                        // Phase 1784 — the one shape the others could not
                        // express: the `Filters` node is PRESENT and declares
                        // `region`, and the same consumer's other edge names a
                        // chip it does not declare. Every other entry here
                        // dangles only because its fixture has no `Filters`
                        // sibling at all. Unaffected by 1785: this fixture's
                        // chips declare, and nothing in it writes a filter back.
                        "filters-param-source-undeclared.json", [ "genre" ]
                        // Phase 1800 — the same shape on the OTHER arm. The
                        // param-source entry above and this one are the corpus's
                        // only two documents where a `Filters` node is present
                        // and a declared edge still names a chip it does not
                        // declare; every other entry here dangles only for want
                        // of a `Filters` sibling. Both arms now have one, so a
                        // host that grounded `dependsOn` against the wrong set —
                        // or against nothing — is visible in this projection.
                        "filters-dependson-undeclared.json", [ "genre" ] ]

                  for (name, names) in expected do
                      let g = graphOf name (File.ReadAllText(Path.Combine(d, name)))

                      Expect.equal
                          (ungroundedFilterEdges g)
                          names
                          (name + ": the declared edge names a filter no chip declares")
          }

          test "exactly the corpus's Filters-bearing fixtures carry a declared-filter control" {
              match nodesDir () with
              | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
              | Some _ ->
                  let declaring =
                      nodeFixtures ()
                      |> List.filter (fun (name, json) -> not (List.isEmpty (filterControlNames (graphOf name json))))
                      |> List.map fst
                      |> List.sort

                  Expect.equal
                      declaring
                      [ "filterable-static-dashboard.json"
                        "filters-1.json"
                        "filters-date-range.json"
                        "filters-declarative.json"
                        // Phase 1800's pair — the `dependsOn` arm's twins, added
                        // for exactly the reason the 1784 note below gives.
                        "filters-dependson-declared.json"
                        "filters-dependson-undeclared.json"
                        // Phase 1784's pair — both carry a `Filters` node, and
                        // they are the corpus's second and third documents to
                        // carry one BESIDE a declared filter edge. Note this list
                        // is the one assertion in the suite that a fixture ADDED
                        // to a repo this one does not own can redden, which is
                        // the hazard the byte-total test below declines to take
                        // on; adding these two is all that was owed.
                        "filters-param-source-declared.json"
                        "filters-param-source-undeclared.json"
                        "filters-rating-colour.json"
                        "filters-segmented.json"
                        "filters-tokens.json"
                        "frag-stdlib-filter-bar.json" ]
                      "the declared-filter control set is exactly the corpus's Filters-bearing fixtures"
          }

          // ── byte stability over the corpus ────────────────────────────────

          test "the projection is byte-stable over every nodes/ fixture" {
              match nodesDir () with
              | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
              | Some _ ->
                  let corpus = nodeFixtures ()
                  Expect.isNonEmpty corpus "the nodes/ family is not empty"

                  let unstable =
                      corpus
                      |> List.choose (fun (name, json) ->
                          // TWO independent decodes, so nothing about a shared
                          // object graph can make the comparison trivially true.
                          let first =
                              System.Text.Encoding.UTF8.GetBytes(WiringGraph.render (graphOf name json))

                          let second =
                              System.Text.Encoding.UTF8.GetBytes(WiringGraph.render (graphOf name json))

                          if first = second then None else Some name)

                  Expect.isEmpty unstable (sprintf "these fixtures rendered differently on two runs: %A" unstable)
          }

          test "every nodes/ fixture projects and renders without refusing" {
              match nodesDir () with
              | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
              | Some _ ->
                  // The measurement the phase reports: the corpus's total
                  // rendered size. It is asserted only to be non-trivial — the
                  // number itself belongs in the outcome, not pinned here,
                  // because a fixture added to the corpus would then redden a
                  // suite in a repo that does not own it.
                  let total =
                      nodeFixtures ()
                      |> List.sumBy (fun (name, json) ->
                          System.Text.Encoding.UTF8.GetByteCount(WiringGraph.render (graphOf name json)))

                  printfn
                      "[wiring-graph] %d nodes/ fixtures render to %d bytes of canonical projection (%s)"
                      (List.length (nodeFixtures ()))
                      total
                      WiringGraph.FormatVersion

                  Expect.isGreaterThan total 0 "the corpus renders to a non-empty projection"
          }

          // ── the acceptance criterion the projection must keep ─────────────

          test "no render path references the wiring graph" {
              match Fuaran.Tests.CorpusRoot.tryRepoRoot () with
              | None -> skiptest "this assembly is not inside the repo, so its sources cannot be read"
              | Some root ->
                  // "Used by no render path" is an acceptance criterion, so it
                  // is a test rather than a comment. A projection that a
                  // renderer came to depend on would be on the hot path, and
                  // its cost and its stability would then be render concerns.
                  let rendererDirs =
                      [ "Fuaran.UI.Renderer"
                        "Fuaran.UI.Renderer.Core"
                        "Fuaran.UI.Renderer.Server"
                        "Fuaran.UI.Renderer.Web" ]
                      |> List.map (fun p -> Path.Combine(root, "src", p))
                      |> List.filter Directory.Exists

                  Expect.isNonEmpty rendererDirs "the renderer projects are present to be swept"

                  // Phase 1844 — ONE file is exempt, by name: the renderer's
                  // `Introspection.fs`, which projects the graph for the
                  // on-demand console surface (`__fuaran.getWiring()`) and is
                  // reached from no render. `WiringIntrospectionTests` pins the
                  // other half — that the console surface is its only caller —
                  // so the exemption cannot widen into a render path unseen.
                  let offenders =
                      rendererDirs
                      |> List.collect (fun d ->
                          Directory.GetFiles(d, "*.fs", SearchOption.AllDirectories) |> Array.toList)
                      |> List.filter (fun f ->
                          Path.GetFileName f <> "Introspection.fs"
                          && (File.ReadAllText f).Contains "WiringGraph")

                  Expect.isEmpty offenders (sprintf "the wiring graph is on a render path in: %A" offenders)
          }

          test "the express/incidental split has one implementation" {
              Expect.isTrue
                  (WiringGraph.ControlKind.isExpress WiringGraph.ControlKind.DeclaredFilter)
                  "a chip is an express declaration to drive"

              Expect.isTrue
                  (WiringGraph.ControlKind.isExpress WiringGraph.ControlKind.StateWrite)
                  "a SetState is an express write"

              Expect.isTrue
                  (WiringGraph.ControlKind.isExpress WiringGraph.ControlKind.FetchIntoState)
                  "a fetch landing into state is an express write"

              Expect.isTrue
                  (WiringGraph.ControlKind.isExpress WiringGraph.ControlKind.FetchIntoQuery)
                  "a fetch landing into a query slot is an express fill"

              Expect.isFalse
                  (WiringGraph.ControlKind.isExpress WiringGraph.ControlKind.SelectionProducer)
                  "a selection producer is a capability of a kind, not a declaration to drive"
          } ]
