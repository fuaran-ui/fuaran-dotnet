module Fuaran.UI.Tests.BindingCycleTests

// ============================================================================
//  Phase 1767 — the reference-cycle audit of the compute layer, and its pin.
//
//  THE QUESTION. Can a Fuaran-UI tree carry a circular data dependency — a
//  read whose re-evaluation writes, within the same propagation pass, a
//  channel it (transitively) reads — so that a host meets a hang or a stale
//  value? `Fuaran.Core.Propagation.sort` reports any reference cycle as data
//  (`TopoResult.Cycles`), and since Phase 1760 `BindingGraph.ofFacts` builds
//  the dependency map it would be asked of. The answer is NO, and this file is
//  the argument plus the assertion that goes red if the world changes back.
//
//  THE ARGUMENT — the reactive graph is LAYERED, so no loop can close in it.
//  `BindingGraph.Dependencies` has exactly two kinds of key and one kind of
//  kept edge (`Propagation.sort` drops an edge whose target is not a key):
//
//    * a SITE (`site:N`) reads inputs (`state:` / `filter:` / `selection:`),
//      queries (`query:`) and the opaque pseudo-inputs (`opaque:*`). No site
//      reads a site: a `Binding` names CHANNELS, never another binding site —
//      a nested binding (`Local.initialFrom`, `Format.source`, `I18n` args, a
//      live `Transform` source, a `TransformParam.From`) is a finite F# value
//      folded into its reader's own read set, not an edge to another node.
//    * a QUERY (`query:<name>`) reads only the filters its `dependsOn` names —
//      `Binding.Query`'s `dependsOn` is a filter-name list; nothing lets a
//      query depend on a query, a state key or a site.
//    * an INPUT is never a key: nothing re-evaluates an input from a read.
//
//  So the only kept edge is site -> query, and a query has no kept edge: the
//  longest path is one edge, and `sort` can find no cycle over ANY tree. The
//  layering is what this file asserts, on every corpus node fixture and on
//  the hand-built trees below, together with `Cycles = []` itself.
//
//  THE CANDIDATE EDGE KINDS, AND WHY NONE CLOSES A LOOP. A loop needs a WRITE
//  edge that fires as a consequence of a read's re-evaluation. Every writer in
//  the language, by kind:
//
//    1. `Action.Call … into: State/Query`, `Action.SetState` (incl. its
//       `valueFrom`, read ONCE at dispatch), `Action.CommitLocal`, `Invoke`,
//       `AiTool`, `ReadFileBody`: all are ACTIONS, and an action fires only
//       from a node's event slot (`onClick`, `onChange`, `onToggle`,
//       `onSelect`, `onEdit`, `onSubmit`, `OnDismiss`, `OnRowClick`, …) — a
//       reader's gesture. A Transform whose live source is a state key that a
//       `Call … into:` fills from a result derived from that Transform's own
//       output is a loop THROUGH THE READER: each turn needs a new gesture, so
//       it is a feedback path, not a reference cycle, and it cannot hang.
//    2. A controlled input's `value` binding beside its `onChange`: the host
//       contract is that the handler fires on the reader's input, never on a
//       programmatic re-render of `value`.
//    3. `Binding.Local`: `commitTo` writes the key `initialFrom` reads BY
//       DESIGN (the buffer commits to what it re-syncs from), so it is the one
//       read -> write pair on a single key. It does not close: the renderer's
//       re-seed runs only when the parsed buffer differs from the new external
//       value, and a commit fires only when the reader has touched the buffer
//       since the last seed (`LocalBindings.fs`, the `dirty` ref — without it
//       an `OnDebounce` Local committed on every re-seed, which was exactly
//       this loop, and was closed there). `initialFrom` itself is a nested
//       binding value, so a "chain" of Locals is a finite term, not a cycle.
//    4. `SwitchSpec.AutoAdvanceMs` over `Binding.State k`: writes the key its
//       selector reads, on a CLOCK. One write per tick, outside any
//       propagation pass — an intended rotation, not a divergence.
//    5. `Binding.Now`: furnished by the host once per render pass; it reads no
//       channel and writes none.
//    6. The compute layer's pipelines (`Binding.Transform`, `Binding.Expr`):
//       a `Transform list` is SEQUENTIAL — each step sees only the columns the
//       steps before it produced, so a derived column cannot reference itself
//       or a later one. There is no named-expression registry in the language
//       whose entries could reference each other; the premise that one exists
//       is false for this tree.
//    7. Host code — a `NodeKind.Custom` renderer, a `Mount` guest's
//       `OnBubble`, a host data loader refetching a query: may write anything
//       at any time. The tree cannot see it, so no validator over the tree
//       could name such a loop; the walk records each as an opaque reader /
//       writer and the graph dirties conservatively. It is outside what a
//       `PreEmitValidate` rule can falsify.
//
//  So no `PreEmitValidate` rule is added: a rule that can never fire is
//  vocabulary nobody can falsify. If a future edge kind adds a kept edge
//  (a site read as a channel, a query over a query or a state key, a reactive
//  writer), the layering assertion below goes red first, and the audit is
//  reopened there rather than silently outgrown.
// ============================================================================

open System.IO
open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.BindingGraph

// ---------------------------------------------------------------------------
//  the layering invariant — the reason no cycle is possible
// ---------------------------------------------------------------------------

let private isSite (id: string) = id.StartsWith "site:"
let private isQuery (id: string) = id.StartsWith "query:"
let private isFilter (id: string) = id.StartsWith "filter:"

/// Every violation of the layering the argument above rests on, as prose.
/// Empty = the graph is layered (sites -> inputs/queries, queries -> filters).
let private layeringViolations (g: Graph) : string list =
    [ for KeyValue(key, reads) in g.Dependencies do
          if isSite key then
              for r in reads do
                  if isSite r then
                      yield sprintf "site %s reads site %s" key r
          elif isQuery key then
              for r in reads do
                  if not (isFilter r) then
                      yield sprintf "query %s reads %s (a query reads only filters)" key r
          else
              yield sprintf "key %s is neither a site nor a query (an input re-evaluated from a read)" key ]

/// The kept edges `Propagation.sort` walks: those whose target is a key.
let private keptEdges (g: Graph) : (string * string) list =
    [ for KeyValue(key, reads) in g.Dependencies do
          for r in reads do
              if Map.containsKey r g.Dependencies then
                  yield key, r ]

let private assertAcyclic (label: string) (g: Graph) =
    Expect.equal (layeringViolations g) [] (sprintf "%s: the binding graph is not layered" label)
    let topo = Propagation.sort g.Dependencies
    Expect.equal topo.Cycles [] (sprintf "%s: Propagation.sort found a reference cycle" label)

    Expect.equal
        (Set.ofList topo.Order)
        (g.Dependencies |> Map.toSeq |> Seq.map fst |> Set.ofSeq)
        (sprintf "%s: the evaluation order does not cover every key" label)

// ---------------------------------------------------------------------------
//  hand-built trees reaching every edge kind the graph has
// ---------------------------------------------------------------------------

let private badgeOf (id: string) (b: Binding<string>) : Node<obj> =
    Fuaran.badge
        id
        { Defaults.badge with
            Label = TextSource.Bound b }

let private countPipeline: Fuaran.Compute.Transform list =
    [ Fuaran.Compute.GroupBy(
          [ "b" ],
          [ { Name = "n"
              Fn = Fuaran.Core.Count
              Of = "a" } ]
      ) ]

let private liveOver (source: Binding<JVal>) : Binding<string> =
    Binding.Transform(TransformSource.Live(source, HostPrelude.TransformLive.emptySource), countPipeline, None)

/// Every read surface the graph turns into an edge, including the one kept
/// edge kind (a site reading a query that depends on a filter), a `Local`
/// whose `initialFrom` and `commitTo` name the same key, and a Transform whose
/// live source is the state key a `Call … into:` would fill.
let private everyEdgeKind: Node<obj> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<obj> with
            Children =
                [ badgeOf "state" (Binding.State("orders", None))
                  badgeOf "filter" (Binding.Filter("region", None))
                  badgeOf "selection" (Binding.Selection("grid", (fun o -> unbox o), None, None))
                  badgeOf "query" (Binding.Query("totals", (fun o -> unbox o), Some [ "region"; "year" ]))
                  badgeOf "live-state" (liveOver (Binding.State("orders", None)))
                  badgeOf "live-query" (liveOver (Binding.Query("totals", (fun o -> unbox o), Some [ "region" ])))
                  badgeOf
                      "live-nested"
                      (liveOver (
                          Binding.Transform(
                              TransformSource.Live(Binding.State("orders", None), HostPrelude.TransformLive.emptySource),
                              countPipeline,
                              None
                          )
                      ))
                  badgeOf
                      "format"
                      (Binding.Format(Binding.State("amount", None), Format.Number(Some 2), LocaleSource.Ambient))
                  badgeOf
                      "local"
                      (Binding.Local(
                          LocalFlushTrigger.OnDebounce 200,
                          id,
                          Binding.State("orders", None),
                          None,
                          Ok,
                          None,
                          Some "orders"
                      ))
                  badgeOf "computed" (Binding.Computed(fun _ -> "x")) ] }

let private nodesDir () : string option =
    Fuaran.Tests.CorpusRoot.tryFind ()
    |> Option.map (fun r -> Path.Combine(r, "nodes"))
    |> Option.filter Directory.Exists

// ---------------------------------------------------------------------------
//  the tests
// ---------------------------------------------------------------------------

[<Tests>]
let tests =
    testList
        "Phase 1767 — BindingCycle: the binding graph is layered, so no reference cycle is possible"
        [ testCase "a tree reaching every edge kind is layered and acyclic, and holds a kept site -> query edge"
          <| fun _ ->
              let g = ofTree everyEdgeKind
              assertAcyclic "everyEdgeKind" g
              let kept = keptEdges g
              Expect.isNonEmpty kept "no kept edge: the acyclicity claim was vacuous here"

              for (from, target) in kept do
                  Expect.isTrue
                      (isSite from && isQuery target)
                      (sprintf "a kept edge %s -> %s is not site -> query" from target)

          testCase "go-red: the probe sees a cycle when a query is made to read a site that reads it"
          <| fun _ ->
              // The acyclicity assertions are only evidence if they CAN fail. Close the one loop the
              // layering forbids — a query reading back a site that reads it — and both the layering
              // check and `Propagation.sort` must see it, naming the path.
              let g = ofTree everyEdgeKind

              let from, query =
                  keptEdges g
                  |> List.tryHead
                  |> Option.defaultWith (fun () -> failtest "no kept edge to close a loop over")

              let looped =
                  { g with
                      Dependencies = g.Dependencies |> Map.add query (Set.add from (Map.find query g.Dependencies)) }

              Expect.isNonEmpty (layeringViolations looped) "the layering check missed a query reading a site"

              match Propagation.cycleThrough query looped.Dependencies with
              | Some path -> Expect.containsAll path [ from; query ] "the reported cycle names both ends of the loop"
              | None -> failtest "Propagation.sort missed a closed loop"

          testCase "go-red: a query reading a query is a layering violation"
          <| fun _ ->
              let g = ofTree everyEdgeKind

              let chained =
                  { g with
                      Dependencies = g.Dependencies |> Map.add "query:totals" (Set.ofList [ "query:totals" ]) }

              Expect.isNonEmpty (layeringViolations chained) "a query-over-query edge went unnoticed"

              Expect.isNonEmpty
                  (Propagation.sort chained.Dependencies).Cycles
                  "a self-referential query is a cycle Propagation.sort must report"

          testCase "on every corpus node fixture, Propagation.sort finds no cycle and the graph is layered"
          <| fun _ ->
              match nodesDir () with
              | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
              | Some dir ->
                  let mutable fixtures = 0
                  let mutable withEdges = 0

                  for path in Directory.GetFiles(dir, "*.json") |> Array.sort do
                      let name =
                          match Path.GetFileName path with
                          | null -> path
                          | n -> n

                      let tree =
                          match Fuaran.UI.Ops.JsonDecode.decodeNodeObj (File.ReadAllText path) with
                          | Ok node -> node
                          | Error e -> failtestf "%s failed to decode: %s at %s" name e.Code e.Path

                      let g = ofTree tree
                      fixtures <- fixtures + 1

                      if not (Map.isEmpty g.Dependencies) then
                          withEdges <- withEdges + 1

                      assertAcyclic name g

                  // Adequacy: a corpus whose every graph is empty certified nothing.
                  Expect.isGreaterThan fixtures 0 "the corpus holds no node fixture"
                  Expect.isGreaterThan withEdges 0 "no corpus fixture produced a non-empty binding graph"
                  printfn "BindingCycle corpus: %d fixtures, %d with a non-empty graph, 0 cycles" fixtures withEdges ]
