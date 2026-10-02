module Fuaran.UI.Tests.BindingGraphTests

// ============================================================================
//  Phase 1760 — the binding dependency graph, as a law over the production
//  module.
//
//  Phase 1479 stated the dirty cone as an ORACLE over `BindingWalk.collect`
//  (`Fuaran.UI.ServerDriven.Tests/CoreIncrementalLawsTests.fs`, section (c)): a
//  test-side projection of the walk's uses, checked against the generator's own
//  record of which readers it built to read the edited key. Nothing at run time
//  asked the question. `BindingGraph` now does, through Core's
//  `Propagation.dirtyFromChangedIds`, so the law is stated over IT:
//
//    * SOUND AND MINIMAL on generated binding sets drawn across all three
//      surfaces that reach the State channel — a plain `Binding.State` read, a
//      `Transform`'s live SOURCE slot, a `Transform` PARAM bound to state — the
//      oracle being the generator's record, not a second walk.
//    * GO-RED. The law is a function of the graph, so it is run again over a
//      graph with one reader's edge to the edited key DROPPED (soundness must
//      fail) and with a spurious edge ADDED (minimality must fail). A law that
//      stays green under both says nothing.
//    * THE CORPUS. For every node fixture and every state key it reads, the
//      graph's cone equals the 1479 oracle's projection of the walk — equal
//      under an `Exact` verdict, contained under a `Conservative` one.
//
//  The opaque-read rule is asserted by name: a `Binding.Computed`, an unnamed
//  live source and an unlocated opaque reader each produce a `Conservative`
//  verdict that SAYS which, never a silent full-dirty.
//
//  Phase 1761 sharpened two of those verdicts, and the generator draws both
//  shapes so the law states the sharpening rather than a unit test beside it:
//
//    * a `Binding.Computed` reader is LOCATED — the walk records its node — so
//      a tree whose only opaque reader is a Computed answers `Conservative`
//      naming exactly the Computed readers, with only them added to the cone.
//      Before 1760's coarse fallback was narrowed, the same tree answered
//      `Unlocated` with every node dirty, and the law goes red on that.
//    * a live Transform over a `Filter` source records the channel it reads, so
//      it is CLEAN for a state write. Before, it answered `LiveSourceUnnamed`
//      on every write, and the law goes red on that too.
// ============================================================================

open System.IO
open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.BindingGraph

// ---------------------------------------------------------------------------
//  generated binding sets
// ---------------------------------------------------------------------------

/// A deterministic 64-bit LCG — a counterexample replays from its seed.
type private Rng = { mutable State: uint64 }

let private rngOf (seed: int) : Rng =
    { State = uint64 seed * 6364136223846793005UL + 1442695040888963407UL }

let private intBelow (bound: int) (r: Rng) : int =
    r.State <- r.State * 6364136223846793005UL + 1442695040888963407UL
    int ((r.State >>> 33) % uint64 bound)

/// The key a case's dirty readers read. Clean readers read `otherKey` or nothing, so "no more" is a
/// claim with something to be wrong about.
let private editedKey = "orders"

let private otherKey = "filters"

let private countPipeline: Fuaran.Compute.Transform list =
    [ Fuaran.Compute.GroupBy(
          [ "b" ],
          [ { Name = "n"
              Fn = Fuaran.Core.Count
              Of = "a" } ]
      ) ]

let private paramPipeline: Fuaran.Compute.Transform list =
    [ Fuaran.Compute.Filter(
          Fuaran.Compute.Binary(Fuaran.Compute.Gt, Fuaran.Compute.Col "a", Fuaran.Compute.ColExpr.Param "threshold")
      ) ]

let private badgeOf (id: string) (label: TextSource) : Node<obj> =
    Fuaran.badge id { Defaults.badge with Label = label }

/// A plain `Binding.State` read.
let private stateReader (id: string) (key: string) : Node<obj> =
    badgeOf id (TextSource.Bound(Binding.State(key, None)))

/// A `Transform` over a LIVE `Binding.State` source.
let private transformSourceReader (id: string) (key: string) : Node<obj> =
    badgeOf
        id
        (TextSource.Bound(
            Binding.Transform(
                TransformSource.Live(Binding.State(key, None), HostPrelude.TransformLive.emptySource),
                countPipeline,
                None
            )
        ))

/// A `Transform` over an EMBEDDED table whose filter threshold is a PARAM bound to state.
let private transformParamReader (id: string) (key: string) : Node<obj> =
    badgeOf
        id
        (TextSource.Bound(
            Binding.Transform(
                TransformSource.Data(Fuaran.Core.Embedded { Schema = []; Columns = [] }),
                paramPipeline,
                Some
                    [ { Name = "threshold"
                        From = Binding.State(key, None) } ]
            )
        ))

let private inertReader (id: string) : Node<obj> =
    badgeOf id (TextSource.Literal "static")

/// Phase 1761 — a `Binding.Computed` reader: opaque, and located.
let private computedReader (id: string) : Node<obj> =
    badgeOf id (TextSource.Bound(Binding.Computed(fun _ -> "x")))

/// The filter a filter-source reader reads — never written by a case's edit.
let private readFilter = "region"

/// Phase 1761 — a `Transform` over a LIVE `Binding.Filter` source: its channel is
/// named, so a state write leaves it clean.
let private transformFilterSourceReader (id: string) : Node<obj> =
    badgeOf
        id
        (TextSource.Bound(
            Binding.Transform(
                TransformSource.Live(Binding.Filter(readFilter, None), HostPrelude.TransformLive.emptySource),
                countPipeline,
                None
            )
        ))

let private dashboardOf (children: Node<obj> list) : Node<obj> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<obj> with
            Children = children }

/// How a generated reader stands towards a write to `editedKey`.
type private Role =
    /// It reads the key: it must be in the cone.
    | Reads
    /// It does not: it must stay out of the cone.
    | Clean
    /// A located opaque reader (Phase 1761): in the cone, and named by the verdict.
    | Opaque

/// One generated binding set, with the generator's OWN record of which readers read `editedKey`.
type private Case =
    {
        Tree: Node<obj>
        Dirty: Set<string>
        Clean: Set<string>
        /// Phase 1761 — the `Binding.Computed` readers drawn: dirty on every state write, and the
        /// only readers a `Conservative` verdict may name.
        Opaque: Set<string>
    }

let private caseOf (seed: int) (iteration: int) : Case =
    let r = rngOf (seed + iteration * 7919)
    let n = 3 + intBelow 6 r

    let drawn =
        [ for i in 0 .. n - 1 do
              let id = sprintf "n%d" i

              match intBelow 8 r with
              | 0 -> id, Reads, stateReader id editedKey
              | 1 -> id, Reads, transformSourceReader id editedKey
              | 2 -> id, Reads, transformParamReader id editedKey
              | 3 -> id, Role.Clean, stateReader id otherKey
              | 4 -> id, Role.Clean, transformSourceReader id otherKey
              // Phase 1761 — the two shapes whose verdicts it sharpened.
              | 5 -> id, Opaque, computedReader id
              | 6 -> id, Role.Clean, transformFilterSourceReader id
              | _ -> id, Role.Clean, inertReader id ]

    let idsOf (role: Role) =
        drawn
        |> List.filter (fun (_, r, _) -> r = role)
        |> List.map (fun (id, _, _) -> id)
        |> Set.ofList

    { Tree = dashboardOf (drawn |> List.map (fun (_, _, node) -> node))
      Dirty = idsOf Reads
      Clean = idsOf Role.Clean
      Opaque = idsOf Opaque }

let private seed = 20260927

let private iterations = 50

/// THE LAW, as a function of the graph so it can be run over a perturbed one: an edit to
/// `editedKey` yields a verdict whose readers are exactly the case's dirty readers plus its located
/// opaque readers — `Exact` when it drew none, and otherwise `Conservative` naming exactly those
/// Computed readers and nothing else (Phase 1761: not `Unlocated`, not a live source it can name).
let private law (graph: Graph) (c: Case) : Result<unit, string> =
    let expected = Set.union c.Dirty c.Opaque

    let check (sites: Set<Site>) =
        let readers = readersOf sites

        if not (Set.isSubset expected readers) then
            Error(sprintf "unsound: %A read '%s' and are not dirty" (Set.difference expected readers) editedKey)
        elif not (Set.isEmpty (Set.intersect readers c.Clean)) then
            Error(sprintf "not minimal: %A are dirty and do not read '%s'" (Set.intersect readers c.Clean) editedKey)
        elif readers <> expected then
            Error(sprintf "the cone %A is not the generated set %A" readers expected)
        else
            Ok()

    match dirty graph [ Input.State editedKey ], Set.isEmpty c.Opaque with
    | Dirty.Exact sites, true -> check sites
    | Dirty.Exact _, false -> Error(sprintf "the verdict claimed exactness over the opaque readers %A" c.Opaque)
    | Dirty.Conservative(_, because), true -> Error(sprintf "the verdict declined exactness: %A" because)
    | Dirty.Conservative(sites, because), false ->
        let named = c.Opaque |> Set.map OpaqueRead.Computed

        if Set.ofList because <> named || List.length because <> Set.count named then
            Error(sprintf "the verdict named %A, not exactly the located Computed readers %A" because named)
        else
            check sites

let private editedId = inputId (Input.State editedKey)

/// Drop the edited key from every site of `reader` — the edge the graph would lose if a surface were
/// forgotten. Every site, because a Transform reader carries a reader site AND a Transform site, and
/// dropping one of the two would leave the reader dirty through the other.
let private dropEdge (reader: string) (g: Graph) : Graph =
    let ofReader =
        g.Sites
        |> Map.filter (fun _ s ->
            match s with
            | Site.Reader r
            | Site.Transform(r, _, _, _) -> r = reader)

    { g with
        Dependencies =
            g.Dependencies
            |> Map.map (fun id reads ->
                if ofReader.ContainsKey id then
                    Set.remove editedId reads
                else
                    reads) }

/// Make every site of `reader` read the edited key — a spurious edge.
let private addEdge (reader: string) (g: Graph) : Graph =
    let ids =
        g.Sites
        |> Map.filter (fun _ s ->
            match s with
            | Site.Reader r -> r = reader
            | Site.Transform _ -> false)
        |> Map.toList
        |> List.map fst

    let ids = if List.isEmpty ids then [ "site:spurious" ] else ids

    { g with
        Dependencies =
            ids
            |> List.fold
                (fun acc id -> Map.add id (Set.add editedId (Map.tryFind id acc |> Option.defaultValue Set.empty)) acc)
                g.Dependencies
        Sites =
            ids
            |> List.fold
                (fun acc id ->
                    if Map.containsKey id acc then
                        acc
                    else
                        Map.add id (Site.Reader reader) acc)
                g.Sites }

/// The Phase 1479 oracle's projection of the walk — the readers whose uses reach `key` on the State
/// channel, stated exactly as `CoreIncrementalLawsTests.fs` states it.
let private walkCone (facts: BindingWalk.TreeBindingFacts) (key: string) : Set<string> =
    facts.Uses
    |> List.choose (fun u ->
        match u.Use with
        | BindingWalk.BindingUse.State k when k = key -> Some u.Reader
        | BindingWalk.BindingUse.TransformStateSource(k, _) when k = key -> Some u.Reader
        | _ -> None)
    |> Set.ofList

let private nodesDir () : string option =
    Fuaran.Tests.CorpusRoot.tryFind ()
    |> Option.map (fun r -> Path.Combine(r, "nodes"))
    |> Option.filter Directory.Exists

let private exactSites (verdict: Dirty) : Set<Site> =
    match verdict with
    | Dirty.Exact sites -> sites
    | Dirty.Conservative(_, because) -> failtestf "expected an Exact verdict, got Conservative because %A" because

// ---------------------------------------------------------------------------
//  the tests
// ---------------------------------------------------------------------------

[<Tests>]
let tests =
    testList
        "Phase 1760 — BindingGraph: which binding sites a write makes stale"
        [ testList
              "the law over the module"
              [ testCase "BindingGraph.dirty is sound and minimal on generated binding sets"
                <| fun _ ->
                    for i in 0 .. iterations - 1 do
                        let c = caseOf seed i

                        match law (ofTree c.Tree) c with
                        | Ok() -> ()
                        | Error why -> failtestf "iter=%d: %s" i why

                testCase "the generated binding sets reach both halves of the cone and all three surfaces"
                <| fun _ ->
                    let xs = [ for i in 0 .. iterations - 1 -> caseOf seed i ]

                    Expect.isNonEmpty
                        (xs |> List.filter (fun c -> not (Set.isEmpty c.Dirty)))
                        "no generated binding set held a reader of the edited key"

                    Expect.isNonEmpty
                        (xs |> List.filter (fun c -> not (Set.isEmpty c.Clean)))
                        "no generated binding set held a reader outside the edited key's cone"

                    // Each surface, named rather than inferred from the mix: the Transform PARAM is the
                    // one an implementation forgets, and a sample that never drew it certifies nothing
                    // about it.
                    let sitesOfTree (c: Case) =
                        (ofTree c.Tree).Sites |> Map.toList |> List.map snd

                    let drewParam =
                        xs
                        |> List.exists (fun c ->
                            sitesOfTree c
                            |> List.exists (function
                                | Site.Transform(r, _, None, _) -> Set.contains r c.Dirty
                                | _ -> false))

                    let drewLiveSource =
                        xs
                        |> List.exists (fun c ->
                            sitesOfTree c
                            |> List.exists (function
                                | Site.Transform(r, _, Some _, _) -> Set.contains r c.Dirty
                                | _ -> false))

                    Expect.isTrue drewParam "no generated set held a Transform PARAM reader of the edited key"

                    Expect.isTrue
                        drewLiveSource
                        "no generated set held a live Transform SOURCE reader of the edited key"

                    // Phase 1761 — both sharpened shapes, drawn and drawn beside ordinary readers, so
                    // the law's `Conservative` arm and the filter source's clean verdict are exercised.
                    Expect.isNonEmpty
                        (xs
                         |> List.filter (fun c -> not (Set.isEmpty c.Opaque) && not (Set.isEmpty c.Dirty)))
                        "no generated set held a located Computed reader beside a reader of the edited key"

                    let drewFilterSource =
                        xs
                        |> List.exists (fun c ->
                            (BindingWalk.collect c.Tree).TransformSites
                            |> List.exists (fun d -> d.Site.IsLive && d.Site.StateKey.IsNone))

                    Expect.isTrue drewFilterSource "no generated set held a live Transform over a Filter source"

                testCase "dropping an edge turns the law red (soundness is load-bearing)"
                <| fun _ ->
                    let mutable perturbed = 0

                    for i in 0 .. iterations - 1 do
                        let c = caseOf seed i
                        let g = ofTree c.Tree

                        for reader in c.Dirty do
                            perturbed <- perturbed + 1

                            match law (dropEdge reader g) c with
                            | Error _ -> ()
                            | Ok() ->
                                failtestf "iter=%d: dropping %s's edge to '%s' left the law green" i reader editedKey

                    Expect.isGreaterThan perturbed 0 "no edge was dropped — the go-red proof ran over nothing"

                testCase "adding an edge turns the law red (minimality is load-bearing)"
                <| fun _ ->
                    let mutable perturbed = 0

                    for i in 0 .. iterations - 1 do
                        let c = caseOf seed i
                        let g = ofTree c.Tree

                        for reader in c.Clean do
                            perturbed <- perturbed + 1

                            match law (addEdge reader g) c with
                            | Error _ -> ()
                            | Ok() -> failtestf "iter=%d: an edge from %s to '%s' left the law green" i reader editedKey

                    Expect.isGreaterThan perturbed 0 "no edge was added — the go-red proof ran over nothing"

                testCase "each of the three State-channel surfaces is in the cone on its own"
                <| fun _ ->
                    let coneOf (node: Node<obj>) =
                        dirty (ofTree (dashboardOf [ node ])) [ Input.State editedKey ]
                        |> exactSites
                        |> readersOf

                    Expect.equal (coneOf (stateReader "s" editedKey)) (Set.singleton "s") "a plain Binding.State read"

                    Expect.equal
                        (coneOf (transformSourceReader "t" editedKey))
                        (Set.singleton "t")
                        "a Transform's live State SOURCE slot"

                    Expect.equal
                        (coneOf (transformParamReader "p" editedKey))
                        (Set.singleton "p")
                        "a Transform PARAM bound to state"

                    Expect.isEmpty (coneOf (inertReader "i") |> Set.toList) "a reader that binds no state" ]

          testList
              "site identity"
              [ testCase "a live Transform site carries the walk's live-store key"
                <| fun _ ->
                    let sites =
                        dirty (ofTree (dashboardOf [ transformSourceReader "t" editedKey ])) [ Input.State editedKey ]
                        |> exactSites

                    let expectedKey =
                        BindingWalk.liveSiteKey (Binding.State(editedKey, None)) countPipeline

                    Expect.equal
                        sites
                        (Set.ofList [ Site.Reader "t"; Site.Transform("t", None, Some expectedKey, 0) ])
                        "the reader site and its Transform site, keyed as the store keys it"

                testCase "a parameterised Transform site declines a key, as the walk does"
                <| fun _ ->
                    let sites =
                        dirty (ofTree (dashboardOf [ transformParamReader "p" editedKey ])) [ Input.State editedKey ]
                        |> exactSites

                    Expect.equal
                        sites
                        (Set.ofList [ Site.Reader "p"; Site.Transform("p", None, None, 0) ])
                        "no key is guessed for an effective pipeline decided at render time"

                testCase "an empty write dirties nothing"
                <| fun _ ->
                    let g = ofTree (caseOf seed 0).Tree
                    Expect.equal (dirty g []) (Dirty.Exact Set.empty) "no input, no stale site" ]

          testList
              "the other channels"
              [ testCase "a filter write reaches a query's readers through the query's dependsOn"
                <| fun _ ->
                    let queryReader =
                        badgeOf "q" (TextSource.Bound(Binding.Query("sales", (fun o -> unbox o), Some [ "region" ])))

                    let filterReader = badgeOf "f" (TextSource.Bound(Binding.Filter("region", None)))

                    let g =
                        ofTree (dashboardOf [ queryReader; filterReader; stateReader "s" editedKey ])

                    Expect.equal
                        (dirty g [ Input.Filter "region" ] |> exactSites |> readersOf)
                        (Set.ofList [ "q"; "f" ])
                        "the filter's own reader and, transitively, the query's"

                    Expect.equal
                        (dirty g [ Input.Query "sales" ] |> exactSites |> readersOf)
                        (Set.singleton "q")
                        "a query refresh reaches only its readers"

                testCase "a selection write reaches exactly its readers"
                <| fun _ ->
                    let selReader =
                        badgeOf "sel" (TextSource.Bound(Binding.Selection("grid", (fun o -> unbox o), None, None)))

                    let g = ofTree (dashboardOf [ selReader; stateReader "s" editedKey ])

                    Expect.equal
                        (dirty g [ Input.Selection "grid" ] |> exactSites |> readersOf)
                        (Set.singleton "sel")
                        "only the selection's reader" ]

          testList
              "the opaque-read rule — declined, and named"
              [ testCase "a Binding.Computed reader is dirty on every state write, and the verdict says why"
                <| fun _ ->
                    let computed = badgeOf "c" (TextSource.Bound(Binding.Computed(fun _ -> "x")))

                    let g = ofTree (dashboardOf [ computed; stateReader "s" otherKey ])

                    match dirty g [ Input.State editedKey ] with
                    | Dirty.Conservative(sites, because) ->
                        Expect.contains because (OpaqueRead.Computed "c") "the Computed reader is named"
                        Expect.isTrue (Set.contains "c" (readersOf sites)) "the Computed reader is dirty"
                    | Dirty.Exact _ -> failtest "a Computed reader was answered as if its reads were known"

                    // A filter write never reaches a Computed closure (it is handed the STATE bag), so the
                    // verdict there is exact — the rule is not a blanket.
                    Expect.equal (dirty g [ Input.Filter "region" ]) (Dirty.Exact Set.empty) "a filter write is exact"

                testCase "a live Transform over an unnamed channel is dirty on every write, and named"
                <| fun _ ->
                    // A `Static` source: no use names a channel for it (Phase 1761 names a `Query`,
                    // `Filter` or `Selection` source, so those are no longer this case).
                    let unnamed =
                        badgeOf
                            "u"
                            (TextSource.Bound(
                                Binding.Transform(
                                    TransformSource.Live(Binding.Static None, HostPrelude.TransformLive.emptySource),
                                    countPipeline,
                                    None
                                )
                            ))

                    let g = ofTree (dashboardOf [ unnamed ])

                    match dirty g [ Input.Filter "anything" ] with
                    | Dirty.Conservative(sites, because) ->
                        Expect.equal because [ OpaqueRead.LiveSourceUnnamed "u" ] "the unnamed live reader is named"
                        Expect.equal (readersOf sites) (Set.singleton "u") "and it is dirty"
                    | Dirty.Exact _ -> failtest "an unnamed live source was answered as if its channel were known"

                testCase "a live Transform over a query names its channel: exact, through the query's dependsOn (1761)"
                <| fun _ ->
                    let overQuery =
                        badgeOf
                            "q"
                            (TextSource.Bound(
                                Binding.Transform(
                                    TransformSource.Live(
                                        Binding.Query("feed", (fun o -> unbox o), Some [ "region" ]),
                                        HostPrelude.TransformLive.emptySource
                                    ),
                                    countPipeline,
                                    None
                                )
                            ))

                    let g = ofTree (dashboardOf [ overQuery; stateReader "s" editedKey ])

                    Expect.equal
                        (dirty g [ Input.Query "feed" ] |> exactSites |> readersOf)
                        (Set.singleton "q")
                        "the query's refresh reaches its live Transform"

                    Expect.equal
                        (dirty g [ Input.Filter "region" ] |> exactSites |> readersOf)
                        (Set.singleton "q")
                        "a filter the query depends on reaches it through the query"

                    Expect.equal
                        (dirty g [ Input.State editedKey ] |> exactSites |> readersOf)
                        (Set.singleton "s")
                        "a state write it does not read leaves it clean — no longer LiveSourceUnnamed"

                testCase "a Custom node is located: named, dirty on a state write, and nothing else on its account"
                <| fun _ ->
                    let custom: Node<obj> = Fuaran.custom "k" "mod" "widget" Map.empty None []

                    let g =
                        ofTree (dashboardOf [ custom; stateReader "s" editedKey; stateReader "o" otherKey ])

                    match dirty g [ Input.State editedKey ] with
                    | Dirty.Conservative(sites, because) ->
                        Expect.equal because [ OpaqueRead.Custom "k" ] "the Custom node is named, and located"
                        Expect.equal (readersOf sites) (Set.ofList [ "k"; "s" ]) "the cone plus the Custom node only"
                    | Dirty.Exact _ -> failtest "a Custom node was answered as if its reads were known"

                    Expect.equal (dirty g [ Input.Filter "region" ]) (Dirty.Exact Set.empty) "a filter write is exact"

                testCase "a tree whose only opaque reader is a Computed gets a located answer (1761)"
                <| fun _ ->
                    let g =
                        ofTree (dashboardOf [ computedReader "c"; stateReader "s" editedKey; stateReader "o" otherKey ])

                    Expect.isTrue g.OpaqueReader "the walk still raises the flag"

                    match dirty g [ Input.State editedKey ] with
                    | Dirty.Conservative(sites, because) ->
                        Expect.equal because [ OpaqueRead.Computed "c" ] "only the Computed reader — not Unlocated"
                        Expect.equal (readersOf sites) (Set.ofList [ "c"; "s" ]) "not every node"
                    | Dirty.Exact _ -> failtest "a Computed reader was answered as if its reads were known"

                testCase "an opaque reader the facts cannot locate dirties every node, and says so"
                <| fun _ ->
                    // `StateKeys.OpaqueReader` set with no located reader behind it — facts the walk
                    // never builds (it records the reader beside the flag) and hand-assembled facts
                    // may. Set on an otherwise-analysable tree here, to isolate the rule.
                    let c = caseOf seed 1
                    let facts = BindingWalk.collect c.Tree

                    let g =
                        ofFacts
                            { facts with
                                StateKeys =
                                    { facts.StateKeys with
                                        OpaqueReader = true
                                        OpaqueReaders = [] } }

                    match dirty g [ Input.State editedKey ] with
                    | Dirty.Conservative(sites, because) ->
                        Expect.contains because OpaqueRead.Unlocated "the unlocated reader is named"

                        Expect.equal
                            (readersOf sites)
                            (facts.Nodes |> Map.toSeq |> Seq.map fst |> Set.ofSeq)
                            "every node is dirty"
                    | Dirty.Exact _ -> failtest "an unlocated opaque reader was answered as exact" ]

          testList
              "the corpus"
              [ testCase "on every node fixture, the graph's cone is the 1479 oracle's for every key it reads"
                <| fun _ ->
                    match nodesDir () with
                    | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
                    | Some dir ->
                        let mutable exact = 0
                        let mutable conservative = 0

                        for path in Directory.GetFiles(dir, "*.json") |> Array.sort do
                            let name = Path.GetFileName path

                            let tree =
                                match Fuaran.UI.Ops.JsonDecode.decodeNodeObj (File.ReadAllText path) with
                                | Ok node -> node
                                | Error e -> failtestf "%s failed to decode: %s at %s" name e.Code e.Path

                            let facts = BindingWalk.collect tree
                            let g = ofFacts facts

                            let keys =
                                facts.Uses
                                |> List.choose (fun u ->
                                    match u.Use with
                                    | BindingWalk.BindingUse.State k
                                    | BindingWalk.BindingUse.TransformStateSource(k, _) -> Some k
                                    | _ -> None)
                                |> List.distinct

                            for key in keys do
                                let oracle = walkCone facts key

                                match dirty g [ Input.State key ] with
                                | Dirty.Exact sites ->
                                    exact <- exact + 1

                                    Expect.equal
                                        (readersOf sites)
                                        oracle
                                        (sprintf "%s, key '%s': the graph's cone is not the walk's" name key)
                                | Dirty.Conservative(sites, _) ->
                                    conservative <- conservative + 1

                                    Expect.isTrue
                                        (Set.isSubset oracle (readersOf sites))
                                        (sprintf "%s, key '%s': a conservative cone dropped a reader" name key)

                        // Adequacy: a corpus in which no key was read, or every verdict declined, certified
                        // nothing about exactness.
                        Expect.isGreaterThan exact 0 "no corpus fixture yielded an exact cone to compare"
                        printfn "BindingGraph corpus: %d exact, %d conservative (fixture, key) pairs" exact conservative ] ]
