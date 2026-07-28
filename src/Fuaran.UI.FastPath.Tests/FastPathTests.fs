namespace Fuaran.UI.FastPath.Tests

open Expecto
open Fuaran.UI
open Fuaran.UI.Types

module FastPathTests =

    let private banked = SeedCatalogue.defaultBank

    /// Collect every metric `Source` binding in a tree (for the ComputeLayer check).
    let rec private metricSources (n: Node<unit>) : Binding<float> list =
        match n.Kind with
        | NodeKind.Metric( spec) -> [ spec.Value ]
        | NodeKind.Box( s) -> s.Children |> List.collect metricSources
        | _ -> []

    let private isTransform (b: Binding<float>) : bool =
        match b with
        | Binding.Transform _ -> true
        | _ -> false

    let private nodeId (n: Node<unit>) : string =
        match n.Id with
        | NodeId s -> s

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
                                      Id = NodeId "" } }

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
