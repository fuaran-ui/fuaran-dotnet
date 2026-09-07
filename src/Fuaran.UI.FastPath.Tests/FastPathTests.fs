namespace Fuaran.UI.FastPath.Tests

open Expecto
open Fuaran.UI
open Fuaran.UI.Types

module FastPathTests =

    let private banked = SeedCatalogue.defaultBank

    /// Collect every metric `Source` binding in a tree (for the ComputeLayer check).
    let rec private metricSources (n: Node<unit>) : Binding<float> list =
        match n.Kind with
        | NodeKind.Metric(spec) -> [ spec.Value ]
        | NodeKind.Box(s) -> s.Children |> List.collect metricSources
        | _ -> []

    let private isTransform (b: Binding<float>) : bool =
        match b with
        | Binding.Transform _ -> true
        | _ -> false

    let private nodeId (n: Node<unit>) : string = n.Id

    [<Tests>]
    let tests =
        testList
            "Fuaran.UI.FastPath"
            [ test "every seed pattern registers (no duplicate ids dropped)" {
                  let count = List.length SeedCatalogue.all
                  Expect.equal banked.Patterns.Count count "id→pattern map holds every pattern"
                  Expect.equal banked.Registry.Entries.Count count "registry holds one entry per pattern"

                  let distinct =
                      SeedCatalogue.all |> List.map (fun p -> p.Id) |> List.distinct |> List.length

                  Expect.equal distinct count "pattern ids are unique"
              }

              test "every pattern declares well-formed value holes" {
                  for p in SeedCatalogue.all do
                      Expect.isNonEmpty p.Holes (sprintf "%s declares at least one hole" p.Id)

                      for h in p.Holes do
                          match h.Kind with
                          | Fuaran.Core.ValueHole _ ->
                              Expect.isNotEmpty h.Addr (sprintf "%s: hole has an address" p.Id)
                              Expect.isNotEmpty h.Name (sprintf "%s: hole has a name" p.Id)
                          | _ -> ()
              }

              test "every pattern instantiates into a tree (empty binding falls back)" {
                  for p in SeedCatalogue.all do
                      let tree = FastPath.instantiate p Map.empty
                      Expect.isNotEmpty (nodeId tree) (sprintf "%s produces a tree with an id" p.Id)
              }

              test "Subsumes finds a pattern whose required holes the context supplies" {
                  let q =
                      FastPath.query
                          [ FastPath.textHole "m0.label" "first"
                            FastPath.numberHole "m0.value" "first" 0 1000000
                            FastPath.textHole "m1.label" "second"
                            FastPath.numberHole "m1.value" "second" 0 1000000
                            FastPath.textHole "m2.label" "third"
                            FastPath.numberHole "m2.value" "third" 0 1000000 ]
                          (Some "Box")

                  let ids = FastPath.findRunnable q banked |> List.map (fun p -> p.Id)
                  Expect.contains ids "metric-strip" "the 3-metric strip is runnable from a 3-metric context"
                  // single-metric needs metric.label/value (absent here) → not matched.
                  Expect.isFalse (List.contains "single-metric" ids) "a pattern whose holes are absent is not matched"
                  // every match honours the requested result kind.
                  for p in FastPath.findRunnable q banked do
                      Expect.equal p.ResultType "Box" "Produce narrows every match to the requested kind"
              }

              test "Produce narrows to the requested node kind" {
                  let q =
                      FastPath.query
                          [ FastPath.textHole "heading" "h"
                            FastPath.textHole "body" "b"
                            FastPath.textHole "message" "m" ]
                          (Some "Callout")

                  let ids = FastPath.findRunnable q banked |> List.map (fun p -> p.Id) |> List.sort
                  Expect.equal ids [ "callout-info"; "empty-state"; "error-state" ] "only the callout patterns match"
              }

              test "Exact matches only the pattern with precisely these holes" {
                  let q =
                      FastPath.query
                          [ FastPath.textHole "metric.label" "label"
                            FastPath.numberHole "metric.value" "value" 0 1000000 ]
                          (Some "Metric")

                  let ids = FastPath.findExact q banked |> List.map (fun p -> p.Id)

                  Expect.equal
                      ids
                      [ "single-metric" ]
                      "exact match is the single-metric (compute-metric has a different hole set)"
              }

              test "every seed pattern passes the egress pre-emit gate (valid Fuaran out of the bank)" {
                  // FGP 7 egress (Phase 562): tryInstantiate re-validates before the
                  // tree leaves the bank. Every shipped catalogue pattern must pass —
                  // the bank ships only valid Fuaran.
                  for p in SeedCatalogue.all do
                      match FastPath.tryInstantiate p Map.empty with
                      | Ok _ -> ()
                      | Error defects -> failtestf "%s: seed pattern emits an invalid tree: %A" p.Id defects
              }

              test "tryInstantiate rejects a pattern whose Build emits an invalid tree (egress gate)" {
                  // A pattern whose Build produces a semantically-invalid tree (here an
                  // empty node id) is rejected with its defects rather than served —
                  // the defence-in-depth the unchecked `instantiate` does not provide.
                  let sample = List.head SeedCatalogue.all

                  let broken =
                      { sample with
                          Build =
                              fun values ->
                                  { FastPath.instantiate sample values with
                                      Id = "" } }

                  match FastPath.tryInstantiate broken Map.empty with
                  | Error defects -> Expect.isNonEmpty defects "the empty-node-id tree is rejected with defects"
                  | Ok _ -> failtest "expected the egress gate to reject an empty-node-id tree"
              }

              test "ComputeLayer patterns carry a real transform binding" {
                  match FastPath.tryPattern "compute-metric" banked with
                  | Some p ->
                      let sources = metricSources (FastPath.instantiate p Map.empty)

                      Expect.exists
                          sources
                          isTransform
                          "the computed metric's source is a Binding.Transform (serverless compute)"
                  | None -> failtest "compute-metric pattern is missing from the seed bank"

                  match FastPath.tryPattern "compute-dashboard" banked with
                  | Some p ->
                      let sources = metricSources (FastPath.instantiate p Map.empty)
                      Expect.exists sources isTransform "the computed dashboard's metric is compute-bound"
                  | None -> failtest "compute-dashboard pattern is missing from the seed bank"
              } ]

// ============================================================================
//  A bank that would answer wrongly refuses to be built, and a value outside
//  its hole's declared space refuses to be instantiated.
//
//  The two halves of a bank used to skip a duplicate id DIFFERENTLY: the Core
//  registry declines a re-registration and so kept the FIRST pattern's
//  signature, while `Map.ofList` takes the last binding and so kept the LAST
//  pattern's builder. A search matched one pattern's holes; `instantiate` ran
//  the other pattern's `Build`. Nothing was reported.
//
//  And `HoleDecl` declares a `ValueSpace` that nothing checked, so
//  `IntRange(1, 12)` accepted "purple".
// ============================================================================

module FastPathRefusalTests =

    open Fuaran.Core

    let private pattern (id: string) (title: string) (holes: HoleDecl list) : FastPath.Pattern =
        { Id = id
          Title = title
          Summary = "test pattern"
          ResultType = "Metric"
          Holes = holes
          Build =
            fun _ ->
                { Id = id
                  Kind =
                    NodeKind.Heading
                        { Defaults.heading with
                            Text = TextSource.Literal title
                            Level = 1 }
                  State = None
                  Style = None
                  Accessibility = None
                  Motion = None
                  ExtraAttributes = None
                  Tooltip = None
                  Visible = None } }

    [<Tests>]
    let tests =
        testList
            "the FastPath bank refuses what it cannot serve"
            [ test "a duplicate id refuses the WHOLE list and names the collision" {
                  let a = pattern "kpi" "Revenue tile" []
                  let b = pattern "kpi" "Headcount tile" []

                  match FastPath.tryBank [ a; b ] with
                  | Ok _ -> failtest "a list with a duplicate id must not build a bank"
                  | Error [ dup ] ->
                      Expect.equal dup.Id "kpi" "the colliding id is named"

                      Expect.equal
                          (List.sort dup.Titles)
                          [ "Headcount tile"; "Revenue tile" ]
                          "and both titles, because a duplicate id is nearly always two different patterns"
                  | Error other -> failtestf "expected exactly one collision, got %A" other
              }

              test "GO-RED TWIN: the two halves of a bank USED to disagree, and now cannot" {
                  // The defect, stated as the thing that is no longer
                  // constructible. Before the refusal, this list built a bank
                  // whose registry answered for "Revenue tile" and whose
                  // pattern map built "Headcount tile".
                  let a = pattern "kpi" "Revenue tile" []
                  let b = pattern "kpi" "Headcount tile" []

                  Expect.throws
                      (fun () -> FastPath.bank [ a; b ] |> ignore)
                      "the ergonomic form raises rather than building a bank that answers wrongly"

                  // And the honest list still builds, so the refusal is not blanket.
                  match FastPath.tryBank [ a; pattern "headcount" "Headcount tile" [] ] with
                  | Ok built -> Expect.equal built.Patterns.Count 2 "two distinct ids, two patterns"
                  | Error e -> failtestf "distinct ids must build: %A" e
              }

              test "a value outside its hole's declared space refuses" {
                  let p = pattern "months" "Months" [ FastPath.numberHole "n" "count" 1 12 ]

                  // Every one of these was accepted before: a word, a float
                  // against an integer range, and an integer outside it.
                  for bad in [ "purple"; "3.7"; "0"; "13"; "" ] do
                      Expect.throws
                          (fun () -> FastPath.instantiate p (Map.ofList [ "n", bad ]) |> ignore)
                          (sprintf "'%s' is not in IntRange(1, 12)" bad)
              }

              test "and a value INSIDE it instantiates, at both ends of the range" {
                  let p = pattern "months" "Months" [ FastPath.numberHole "n" "count" 1 12 ]

                  for good in [ "1"; "6"; "12" ] do
                      let tree = FastPath.instantiate p (Map.ofList [ "n", good ])
                      Expect.equal tree.Id "months" (sprintf "'%s' is admitted" good)
              }

              test "an UNBOUND hole is not a violation — a partial binding still renders" {
                  // `Build` is contracted to fall back to a default, which is
                  // what makes a partial binding useful at all. Refusing an
                  // absent value would break every existing caller that passes
                  // `Map.empty`.
                  let p = pattern "months" "Months" [ FastPath.numberHole "n" "count" 1 12 ]

                  let tree = FastPath.instantiate p Map.empty
                  Expect.equal tree.Id "months" "no value supplied, nothing to violate"
              }

              test "the violation NAMES the hole, its address and the value" {
                  let p = pattern "months" "Months" [ FastPath.numberHole "n" "count" 1 12 ]

                  match FastPath.valueViolations p (Map.ofList [ "n", "purple" ]) with
                  | [ v ] ->
                      Expect.equal v.Addr "n" "the address binding goes by"
                      Expect.equal v.Name "count" "the human name"
                      Expect.equal v.Value "purple" "and the value that was refused"
                  | other -> failtestf "expected one violation, got %A" other
              }

              test "AnyString admits anything — the check follows the DECLARED space" {
                  // The rule is not "validate everything", it is "hold each
                  // value to the space its own hole declared". A free-text hole
                  // declares no constraint and must not acquire one here.
                  let p = pattern "note" "Note" [ FastPath.textHole "t" "text" ]

                  for anything in [ "purple"; ""; "3.7"; "=cmd|'/c calc'!A0" ] do
                      Expect.isEmpty
                          (FastPath.valueViolations p (Map.ofList [ "t", anything ]))
                          (sprintf "AnyString admits '%s'" anything)
              } ]
