module Fuaran.UI.Tests.StructuralQuery

// Phase 1152 — `Action.Dispatch` carries the IDL's `inProcessOnly` marking, which
// the generator renders as `[<Obsolete(…, false)>]`: FS0044 at every mention, and
// an error under this repo's `TreatWarningsAsErrors`. File-scoped rather than
// per-declaration because the mentions sit INSIDE `testList` expressions, where a
// lexical directive cannot be placed — this is the tightest form the file can
// express. A suite is not an authoring surface: these uses exist to PIN the marked
// case's behaviour, which is the one use the marking is not addressed to.
#nowarn "44"

#nowarn "3261" // DirectoryInfo.Parent + AssemblyName.Name are legitimately nullable here.

// ============================================================================
//  Phase 443 — the structural predicate library over `Node` trees.
//
//  Three things are pinned here, in increasing order of how easily they rot:
//
//   1. THE FIVE QUERY CLASSES answer correctly over the shared wire-format
//      corpus — real decoded trees, not hand-built ones, so the fixtures are the
//      oracle rather than the author's memory of the vocabulary. Each class is
//      asserted with its match trace, since a hit with no evidence is not
//      usable for highlighting and would pass a naive assertion.
//
//   2. THE ALGEBRA LAWS, checked by exhaustive enumeration over a predicate
//      pool crossed with every decoded corpus tree. Deliberately NOT a random
//      generator: the corpus is a fixed, committed set and the pool is small, so
//      enumeration covers every pair and triple outright and the result is
//      reproducible without a shrinking story. The scoping terms are checked
//      against an INDEPENDENT oracle built in this file from the public
//      `children` relation, not against the evaluator's own idea of ancestry.
//
//      THE LAWS THEMSELVES ARE NOT WRITTEN HERE. They live in
//      `tests/pure-tier-laws/Laws.fs`, linked into this project, because a
//      second executor consumes the same definitions: that directory's probe
//      runs them on .NET and on the same sources transpiled to JavaScript and
//      byte-compares the two. This suite is the executor that ASSERTS — one
//      Expecto case per law, each claim an `Expect.equal` naming its fixture and
//      instance. The laws had been written out twice, once on each side, and the
//      copies had already drifted; one definition site is what makes "the laws
//      hold on both pipelines" a statement about the same laws.
//
//   3. THAT NO SIGNATURE-SEARCH CAPABILITY IS REIMPLEMENTED. The delegation
//      seam routes a signature-expressible query out to the shipped bank and
//      the two answers agree; and `Fuaran.UI` is shown to hold no reference to
//      the artifact-function registry at all, so a copy could not compile here
//      even if someone wrote one.
// ============================================================================

open System.IO
open Expecto

open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.StructuralQuery

// The shared law definitions, linked in from tests/pure-tier-laws/Laws.fs.
open PureTierLaws

// ── the corpus ──────────────────────────────────────────────────────────────

/// The shared `wire-format-fixtures/nodes` family, located by climbing from the
/// test binary — the same idiom the generated-layer and markdown corpus suites
/// use. `None` when the corpus clone is absent (a bare single-repo checkout).
let private corpusDir () : string option =
    let rec climb (dir: DirectoryInfo option) =
        match dir with
        | None -> None
        | Some d ->
            let candidate = Path.Combine(d.FullName, "wire-format-fixtures", "nodes")

            if Directory.Exists candidate then
                Some candidate
            else
                climb (Option.ofObj d.Parent)

    climb (Some(DirectoryInfo(System.AppContext.BaseDirectory)))

let private fileName (p: string) =
    Path.GetFileName p |> Option.ofObj |> Option.defaultValue p

/// One named fixture, decoded.
let private tree (name: string) : Node<obj> option =
    corpusDir ()
    |> Option.map (fun d -> Path.Combine(d, name + ".json"))
    |> Option.filter File.Exists
    |> Option.bind (fun p ->
        match Generated.decodeNode ((File.ReadAllText p).Trim()) with
        | Ok node -> Some node
        | Error e -> failtestf "corpus fixture '%s' did not decode: %s" name e)

/// Every node fixture, decoded, as (fixture name, tree) — the corpus a query
/// runs over. A fixture that fails to decode fails the suite loudly: a silently
/// shrinking corpus would turn every law below into a vacuous green.
let private corpus () : (string * Node<obj>) list =
    match corpusDir () with
    | None -> []
    | Some d ->
        Directory.GetFiles(d, "*.json")
        |> Array.toList
        |> List.sortBy fileName
        |> List.map (fun p ->
            let name = fileName p

            match Generated.decodeNode ((File.ReadAllText p).Trim()) with
            | Ok node -> name, node
            | Error e -> failtestf "corpus fixture '%s' did not decode: %s" name e)

// ── an independent tree oracle (NOT the evaluator's own relation) ────────────

/// Every node of a tree in document order, via the module's public `children`.
let rec private allNodes (node: Node<obj>) : Node<obj> list =
    node :: (children node |> List.collect allNodes)

/// (node id, its strict descendants' ids) for every node.
let rec private descendantPairs (node: Node<obj>) : (string * string list) list =
    let kids = children node
    let mine = kids |> List.collect allNodes |> List.map _.Id
    (node.Id, mine) :: (kids |> List.collect descendantPairs)

/// (node id, its strict ancestors' ids) for every node.
let private ancestorPairs (root: Node<obj>) : (string * string list) list =
    let rec go (trail: string list) (node: Node<obj>) =
        (node.Id, trail) :: (children node |> List.collect (go (node.Id :: trail)))

    go [] root

/// A tree whose ids are unique — the oracle keys on id, and the pre-emit
/// validator forbids duplicates, but a search must survive one, so the oracle
/// simply declines to reason about such a tree rather than asserting nonsense.
let private idsAreUnique (root: Node<obj>) : bool =
    let ids = allNodes root |> List.map _.Id
    List.length ids = List.length (List.distinct ids)

// ── the shared law definitions ───────────────────────────────────────────────
//
// `pool`, `matched`, `everyNode` and every algebra law come from the linked
// `PureTierLaws.Laws` module — the one definition site the transpiled probe
// also compiles. These are aliases onto it, not second definitions: the point
// of the seam is that there is nothing here to drift.

let private pool = Laws.pool

let private matched = Laws.matched

/// The identity of `And` — it holds of every node, so this is "every id".
let private everyNode = Laws.everyNode

/// Run `check` for every (name, tree) in the corpus, reporting the fixture that
/// broke rather than a bare set inequality.
let private forEachTree (label: string) (check: string -> Node<obj> -> unit) =
    match corpus () with
    | [] -> skiptest "wire-format-fixtures/nodes not found — the corpus clone is missing"
    | trees ->
        Expect.isGreaterThan
            (List.length trees)
            20
            (sprintf "%s: the corpus is large enough to be worth enumerating" label)

        for name, t in trees do
            check name t

/// One Expecto case per law in the shared list. Every claim asserted here is the
/// same `Laws.Claim` the transpiled probe folds into its violation count, so a
/// single edit to a law body reddens this suite and that probe together — the
/// property two hand-maintained copies could not offer.
let private lawCase (label: string, law: Node<obj> -> Laws.Claim seq) =
    test ("law: " + label) {
        forEachTree label (fun name t ->
            for c in law t do
                Expect.equal c.Left c.Right (sprintf "%s: %s [%s]" name c.Law c.Instance))
    }

/// The claims the shared laws deliberately do NOT carry, and the trace and
/// determinism pins that sit beside them. Every one of these is about something
/// other than set equality between two predicate expressions — which is all a
/// `Laws.Claim` says: an independent walk of the tree, a hit's evidence rather
/// than its match set, the containment relation read from both ends, the shape
/// of a trace, evaluation order. Keeping them here is what stops the shared
/// claim type growing a shape only one of the two executors could interpret.
let private oracleCases: Test list =
    [ test "And [] is exactly the tree's node set — against an independent walk" {
          forEachTree "identity-oracle" (fun name t ->
              Expect.equal
                  (List.length (allNodes t))
                  (Set.count (everyNode t))
                  (sprintf "%s: And [] is every node" name))
      }

      test "a negated match carries no positive evidence" {
          forEachTree "negation-evidence" (fun name t ->
              for a in pool do
                  Expect.isEmpty
                      ((evaluate (Predicate.Not a) t).Hits |> List.collect _.Witnesses)
                      (sprintf "%s: a negated match carries no positive evidence" name))
      }

      test "scoping agrees with an independently-built containment relation" {
          forEachTree "scoping" (fun name t ->
              if idsAreUnique t then
                  let descendants = descendantPairs t |> Map.ofList
                  let ancestors = ancestorPairs t |> Map.ofList

                  for a in pool do
                      let inner = matched a t

                      let expectedDescendantScope =
                          descendants
                          |> Map.toList
                          |> List.filter (fun (_, ds) -> ds |> List.exists (fun d -> Set.contains d inner))
                          |> List.map fst
                          |> Set.ofList

                      let expectedAncestorScope =
                          ancestors
                          |> Map.toList
                          |> List.filter (fun (_, asc) -> asc |> List.exists (fun x -> Set.contains x inner))
                          |> List.map fst
                          |> Set.ofList

                      Expect.equal
                          (matched (Predicate.HasDescendant a) t)
                          expectedDescendantScope
                          (sprintf "%s: HasDescendant matches the oracle" name)

                      Expect.equal
                          (matched (Predicate.HasAncestor a) t)
                          expectedAncestorScope
                          (sprintf "%s: HasAncestor matches the oracle" name))
      }

      test "descendant and ancestor scoping are exact duals" {
          forEachTree "duality" (fun name t ->
              if idsAreUnique t then
                  // n has d as a strict descendant iff d has n as a strict
                  // ancestor — the property that makes the two scoping terms one
                  // relation read from two ends.
                  let asDescendantEdges =
                      descendantPairs t
                      |> List.collect (fun (n, ds) -> ds |> List.map (fun d -> n, d))
                      |> Set.ofList

                  let asAncestorEdges =
                      ancestorPairs t
                      |> List.collect (fun (d, ancs) -> ancs |> List.map (fun n -> n, d))
                      |> Set.ofList

                  Expect.equal asDescendantEdges asAncestorEdges (sprintf "%s: the relation is one relation" name)

                  // The root has no ancestor; a leaf has no descendant.
                  Expect.isFalse
                      (Set.contains t.Id (matched (Predicate.HasAncestor(Predicate.And [])) t))
                      (sprintf "%s: the root has no strict ancestor" name))
      }

      test "every hit's trace names real nodes, and highlight is hits plus trace" {
          forEachTree "traces" (fun name t ->
              let ids = allNodes t |> List.map _.Id |> Set.ofList

              let scoping =
                  [ for a in pool do
                        yield Predicate.HasDescendant a
                        yield Predicate.HasAncestor a
                        yield Predicate.And [ a; Predicate.ChildCount(Cmp.Gte, 1) ] ]

              for p in pool @ scoping do
                  let r = evaluate p t

                  Expect.isTrue (Set.isSubset r.Matched r.Highlight) (sprintf "%s: matched is part of highlight" name)

                  Expect.equal
                      r.Highlight
                      (r.Hits |> List.collect (fun h -> h.NodeId :: h.Witnesses) |> Set.ofList)
                      (sprintf "%s: highlight is exactly hits plus witnesses" name)

                  for h in r.Hits do
                      Expect.isTrue (Set.contains h.NodeId ids) (sprintf "%s: a hit names a real node" name)

                      Expect.equal h.Witnesses (List.distinct h.Witnesses) (sprintf "%s: witnesses are distinct" name)

                      for w in h.Witnesses do
                          Expect.isTrue (Set.contains w ids) (sprintf "%s: a witness names a real node" name))
      }

      test "evaluation is deterministic and order-stable" {
          forEachTree "determinism" (fun name t ->
              for a in pool do
                  let first = evaluate a t
                  let second = evaluate a t

                  Expect.equal first.Hits second.Hits (sprintf "%s: same input, same hits in the same order" name))
      } ]

[<Tests>]
let structuralQueryTests =
    testList
        "Phase 443 — structural predicates over Node trees"
        [

          // ── 1. the five demo query classes, over corpus fixtures ──────────

          testList
              "the five query classes evaluate over the wire-format corpus"
              [ test "kind — has: DataGrid finds the grid, naming either vocabulary" {
                    match tree "filterable-static-dashboard" with
                    | None -> skiptest "corpus fixture absent"
                    | Some t ->
                        let byWireName = evaluate (Chip.has "DataGrid") t
                        let byTag = evaluate (Chip.has "Grid") t

                        Expect.equal (Set.toList byWireName.Matched) [ "episode-grid" ] "has: DataGrid matches the grid"

                        Expect.equal
                            byTag.Matched
                            byWireName.Matched
                            "the wire discriminator and the kind tag name the same node"

                        // A hit on the node's own content is its own evidence.
                        Expect.equal
                            (byWireName.Hits |> List.map _.Witnesses)
                            [ [] ]
                            "a kind hit carries no witness — the hit IS the evidence"

                        Expect.equal byWireName.Highlight byWireName.Matched "nothing extra to highlight"
                }

                test "binding — bound-to: <name> finds every reader of that channel" {
                    match tree "filterable-static-dashboard" with
                    | None -> skiptest "corpus fixture absent"
                    | Some t ->
                        let r = evaluate (Chip.boundTo "region") t

                        Expect.isTrue
                            (Set.contains "retention-chart" r.Matched)
                            "the chart reads the region filter (through a transform param)"

                        // The Filters node matches too, and that is right rather
                        // than incidental: a chip renders its filter's CURRENT
                        // value, so it reads the channel it declares. The query
                        // asks who reads `region`, and the chip is one of them.
                        Expect.isTrue
                            (Set.contains "content-filters" r.Matched)
                            "the chip that offers the filter also reads it"

                        Expect.isFalse
                            (Set.contains "filterable-static-dashboard" r.Matched)
                            "the container reads nothing itself — a read is not inherited"

                        // Channel-scoped asks the same question more narrowly.
                        let onFilter = evaluate (Predicate.BoundTo(Channel.Filter, "region")) t
                        Expect.equal onFilter.Matched r.Matched "region lives on the filter channel"

                        let onState = evaluate (Predicate.BoundTo(Channel.State, "region")) t
                        Expect.isEmpty onState.Matched "region is not a state key"

                        Expect.isEmpty
                            (evaluate (Chip.boundTo "no-such-name") t).Matched
                            "an unknown name matches nothing"
                }

                test "binding — bound-to on the State channel reaches a Transform's live SOURCE" {
                    // The seeding charter's pair fixture: a grid reading
                    // `$state.members` through a plain `Binding.State`, and a
                    // badge deriving a count from the SAME key through a
                    // `Transform`'s live source. Both read `members`; only the
                    // grid was findable while the shared walk withheld
                    // `BindingUse.TransformStateSource` from
                    // `TreeBindingFacts.Uses`, which this index reads. A search
                    // that named one reader of a key two nodes read was not
                    // giving a narrower answer, it was giving a wrong one.
                    match tree "shared-source-seeded-pair" with
                    | None -> skiptest "corpus fixture absent"
                    | Some t ->
                        let r = evaluate (Predicate.BoundTo(Channel.State, "members")) t

                        Expect.isTrue (Set.contains "member-grid" r.Matched) "the grid's source reads members"

                        Expect.isTrue
                            (Set.contains "member-count" r.Matched)
                            "the badge's Transform source reads members too"

                        Expect.isFalse
                            (Set.contains "shared-source-seeded-pair" r.Matched)
                            "the container reads nothing itself — a read is not inherited"
                }

                test "shape — children-of: Dashboard >= N, with the counted children as the trace" {
                    match tree "filterable-static-dashboard" with
                    | None -> skiptest "corpus fixture absent"
                    | Some t ->
                        let r = evaluate (Chip.childrenOf "Dashboard" 3) t

                        Expect.equal
                            (r.Hits |> List.map _.NodeId)
                            [ "filterable-static-dashboard" ]
                            "the dashboard root holds three children"

                        Expect.equal
                            (r.Hits |> List.head |> _.Witnesses)
                            [ "content-filters"; "retention-chart"; "episode-grid" ]
                            "the trace names the counted children, in document order"

                        Expect.equal
                            r.Highlight
                            (Set.ofList
                                [ "filterable-static-dashboard"
                                  "content-filters"
                                  "retention-chart"
                                  "episode-grid" ])
                            "highlight = the hit plus its witnesses"

                        Expect.isEmpty (evaluate (Chip.childrenOf "Dashboard" 4) t).Matched "four is one too many"
                        Expect.isEmpty (evaluate (Chip.childrenOf "Card" 3) t).Matched "the role is part of the term"
                }

                test "style — tone: Critical anywhere, including inside a switch case" {
                    match tree "switch-on-selection" with
                    | None -> skiptest "corpus fixture absent"
                    | Some t ->
                        let r = evaluate (Chip.tone "Critical") t

                        Expect.equal
                            (Set.toList r.Matched)
                            [ "ward-critical" ]
                            "the critically-toned callout matches wherever it sits"

                        // Scoped: the same tone, but only under the switch.
                        let scoped =
                            evaluate
                                (Predicate.And [ Chip.tone "Critical"; Predicate.HasAncestor(Chip.has "Switch") ])
                                t

                        Expect.equal scoped.Matched r.Matched "it does sit under a Switch"

                        Expect.equal
                            (scoped.Hits |> List.head |> _.Witnesses)
                            [ "ward-status-panel" ]
                            "the ancestor that satisfied the scope is the witness"

                        Expect.isEmpty
                            (evaluate
                                (Predicate.And [ Chip.tone "Critical"; Predicate.HasAncestor(Chip.has "Modal") ])
                                t)
                                .Matched
                            "no modal ancestor, no match"
                }

                test "behaviour — dispatches, and the honest limit of a decoded tree" {
                    match tree "btn-copy-link" with
                    | None -> skiptest "corpus fixture absent"
                    | Some t ->
                        Expect.equal
                            (Set.toList (evaluate (Chip.dispatches "*") t).Matched)
                            [ "btn-copy-link" ]
                            "the button dispatches, inside a chain"

                        Expect.isEmpty
                            (evaluate (Chip.dispatches "Submit") t).Matched
                            "a decoded Dispatch carries no case name, so the specific form declines rather than guesses"

                        Expect.equal
                            (Set.toList (evaluate (Predicate.Dispatches Act.WriteToClipboard) t).Matched)
                            [ "btn-copy-link" ]
                            "the chain's other arm is reachable too"

                        Expect.isEmpty
                            (evaluate (Predicate.Dispatches(Act.Navigate "*")) t).Matched
                            "it navigates nowhere"
                }

                test "behaviour — a labelled in-process tree answers the specific case" {
                    // The same query the decoded tree above could not answer,
                    // asked of a typed tree whose message cases have names.
                    let typed: Node<string> =
                        Fuaran.button
                            "submit-button"
                            { Defaults.button with
                                Label = TextSource.Literal "Submit"
                                OnClick = Action.Dispatch "SubmitPressed" }

                    let options = Options.labelled (fun (m: string) -> Some m)

                    Expect.equal
                        (Set.toList (evaluateWith options (Chip.dispatches "SubmitPressed") typed).Matched)
                        [ "submit-button" ]
                        "the labeller names the case and the query lands"

                    Expect.isEmpty
                        (evaluateWith options (Chip.dispatches "CancelPressed") typed).Matched
                        "a different case does not"
                }

                test "a corpus search returns only the applications that answer" {
                    match corpus () with
                    | [] -> skiptest "corpus clone absent"
                    | trees ->
                        let hits = evaluateCorpus (Chip.has "DataGrid") trees

                        Expect.isGreaterThan (List.length hits) 0 "some applications hold a grid"

                        Expect.isLessThan
                            (List.length hits)
                            (List.length trees)
                            "and some do not — the query discriminates"

                        for name, r in hits do
                            Expect.isTrue (Result.any r) (sprintf "%s: a returned entry has hits" name)
                } ]

          // ── 2. the algebra laws ───────────────────────────────────────────

          testList
              "the predicate algebra, enumerated over the corpus"
              ([ test "every predicate in the pool discriminates — the laws are not vacuous" {
                     // A law quantified over predicates that match nothing is a
                     // law about the empty set. Each pool member must therefore
                     // match somewhere in the corpus AND miss somewhere, or the
                     // enumeration below proves less than it appears to.
                     match corpus () with
                     | [] -> skiptest "wire-format-fixtures/nodes not found — the corpus clone is missing"
                     | trees ->
                         for p in pool do
                             let hitting = trees |> List.filter (fun (_, t) -> not (Set.isEmpty (matched p t)))

                             Expect.isGreaterThan
                                 (List.length hitting)
                                 0
                                 (sprintf "%A matches somewhere in the corpus" p)

                             Expect.isLessThan
                                 (List.length hitting)
                                 (List.length trees)
                                 (sprintf "%A misses somewhere in the corpus" p)
                 } ]

               // The eight algebra laws, enumerated from the shared definition
               // list rather than restated here. A law added to `Laws.laws`
               // becomes an Expecto case with no edit in this file — and a case
               // in the transpiled probe in the same edit.
               @ (Laws.laws |> List.map lawCase)

               @ oracleCases)

          // ── 3. composition with the shipped signature-search surface ───────

          testList
              "composition with the signature-searchable pattern bank"
              [ test "a kind-only query is classified as signature-expressible" {
                    Expect.equal
                        (Delegation.tryRoute (Chip.has "Callout"))
                        (Some { Delegation.Route.Produce = "Callout" })
                        "a bare kind term routes"

                    Expect.equal
                        (Delegation.tryRoute (Predicate.And [ Chip.has "DataGrid"; Chip.has "DataGrid" ]))
                        (Some { Delegation.Route.Produce = "Grid" })
                        "a conjunction naming one kind routes, in the bank's own tag vocabulary"

                    Expect.equal
                        (Delegation.plan (Chip.has "Callout"))
                        (Delegation.Plan.Signature { Produce = "Callout" })
                        "plan agrees"
                }

                test "anything a signature cannot express falls to the tree walk" {
                    let notRoutable =
                        [ Chip.boundTo "revenue"
                          Chip.childrenOf "Dashboard" 3
                          Chip.tone "Critical"
                          Chip.dispatches "*"
                          Predicate.HasDescendant(Chip.has "Callout")
                          Predicate.HasAncestor(Chip.has "Box")
                          Predicate.Not(Chip.has "Callout")
                          Predicate.Or [ Chip.has "Callout"; Chip.has "Metric" ]
                          Predicate.And [ Chip.has "Callout"; Chip.has "Metric" ]
                          Predicate.And [ Chip.has "Callout"; Chip.tone "Critical" ] ]

                    for p in notRoutable do
                        Expect.equal (Delegation.tryRoute p) None (sprintf "%A is not a signature query" p)
                        Expect.equal (Delegation.plan p) Delegation.Plan.TreeWalk "and plans as a tree walk"

                        Expect.equal
                            (Delegation.tryVia (fun _ -> [ "should-not-run" ]) p)
                            None
                            "and never reaches the bank"
                }

                test "a routed query is answered BY the shipped bank, not by a copy of it" {
                    // `FastPath.find` delegates verbatim to the shipped
                    // signature-search engine. The predicate library never
                    // calls it — the caller supplies the binding — so this test
                    // is the wiring a consumer writes, and the assertion is that
                    // routing through it is identical to asking the bank directly.
                    let provide =
                        [ FastPath.textHole "heading" "heading"
                          FastPath.textHole "body" "body"
                          FastPath.textHole "message" "message" ]

                    let search (r: Delegation.Route) =
                        FastPath.find Subsumes (FastPath.query provide (Some r.Produce)) SeedCatalogue.defaultBank
                        |> List.map _.Id
                        |> List.sort

                    let viaPredicate = Delegation.tryVia search (Chip.has "Callout")
                    let direct = search { Produce = "Callout" }

                    Expect.equal viaPredicate (Some direct) "the routed answer IS the bank's answer"

                    Expect.isGreaterThan
                        (List.length direct)
                        0
                        "and the bank actually answered — not a vacuous agreement"

                    Expect.contains direct "callout-info" "the seed catalogue's info callout is among them"
                }

                test "Fuaran.UI cannot reimplement the signature search — it cannot see it" {
                    // The structural pin behind the discipline: the predicate
                    // library lives in a package that holds no reference to the
                    // artifact-function registry, so a second `findBySignature`
                    // could not compile here. The bank sits ABOVE this tier and
                    // the dependency runs one way.
                    let referenced =
                        typeof<Predicate>.Assembly.GetReferencedAssemblies()
                        |> Array.choose (fun a -> Option.ofObj a.Name)

                    Expect.isFalse
                        (referenced |> Array.contains "Fuaran.Core.Function")
                        "Fuaran.UI does not reference the artifact-function registry"

                    // The probe is only worth its verdict if it can go red:
                    // the package that DOES hold the search must show up.
                    let bankReferenced =
                        typeof<FastPath.Bank>.Assembly.GetReferencedAssemblies()
                        |> Array.choose (fun a -> Option.ofObj a.Name)

                    Expect.isTrue
                        (bankReferenced |> Array.contains "Fuaran.Core.Function")
                        "the pattern-bank package does reference it — so the check above measures something"
                } ] ]
