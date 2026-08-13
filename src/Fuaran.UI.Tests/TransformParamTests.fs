module Fuaran.UI.Tests.TransformParam

// ============================================================================
//  Phase 424 — parameterised `Binding.Transform` resolution semantics.
//
//  Asserts (on the .NET pipeline; the browser path is the fuaran-ts renderer's
//  corpus test): a `Transform` whose `filter` step compares a `col` against a
//  `param` sourced from a `Binding.Filter` scopes its rows by the live filter
//  value; an UNSET filter (its source `NotResolved`) prunes the filter step
//  ("unset filter ⇒ no constraint" — the one lenient UI rule); the edge is
//  derived from the pipeline's params, so no host code wires it.
// ============================================================================

open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Renderer

let private nn (v: 'T) : obj = box v |> Unchecked.nonNull

let private table =
    Fuaran.Core.Embedded
        { Schema = [ "dept", Fuaran.Core.StringType; "amount", Fuaran.Core.IntType ]
          Columns =
            [ Fuaran.Core.Column.create
                  "dept"
                  Fuaran.Core.StringType
                  [ Fuaran.Core.Str "eng"; Fuaran.Core.Str "eng"; Fuaran.Core.Str "sales" ]
              Fuaran.Core.Column.create
                  "amount"
                  Fuaran.Core.IntType
                  [ Fuaran.Core.Int 100; Fuaran.Core.Int 120; Fuaran.Core.Int 90 ] ] }

let private pipeline: Fuaran.Core.Transform list =
    [ Fuaran.Core.Filter(Fuaran.Core.Binary(Fuaran.Core.Eq, Fuaran.Core.Col "dept", Fuaran.Core.Param "dept")) ]

let private transformBinding: Binding<obj seq> =
    Binding.Transform(
        TransformSource.Data(table),
        pipeline,
        Some
            [ { From = Binding.Filter("dept", None)
                Name = "dept" } ]
    )

let private rowCount (sources: BindingResolver.BindingSources) : int =
    match BindingResolver.resolve sources transformBinding with
    | BindingResolver.Resolved rows -> Seq.length rows
    | other -> failtestf "expected Resolved, got %A" other

[<Tests>]
let tests =
    testList
        "Phase 424 — parameterised Binding.Transform"
        [ test "a bound filter param scopes the rows (dept = $filters.dept)" {
              let sources =
                  { BindingResolver.empty with
                      Filters = Map.ofList [ "dept", nn "eng" ] }

              Expect.equal (rowCount sources) 2 "only the two eng rows survive the param filter"
          }

          test "an unset filter param prunes the filter step (unset ⇒ no constraint)" {
              // no "dept" filter present → its source is NotResolved → the filter step is pruned.
              Expect.equal (rowCount BindingResolver.empty) 3 "all rows shown when the filter is unset"
          }

          test "a different bound value scopes differently" {
              let sources =
                  { BindingResolver.empty with
                      Filters = Map.ofList [ "dept", nn "sales" ] }

              Expect.equal (rowCount sources) 1 "only the single sales row"
          }

          test "Transform.paramsOf derives the pipeline's param edge (no stored edge)" {
              Expect.equal (Fuaran.Core.Transform.paramsOf pipeline) [ "dept" ] "the derived filter→consumer edge"
          } ]
