module Fuaran.UI.Tests.TransformListParam

// ============================================================================
//  Phase 610 — LIST-valued `Binding.Transform` params: the multi-select chip
//  end of the wiring.
//
//  Asserts (on the .NET pipeline; the browser path is the fuaran-ts renderer's
//  mirror suite): a `filter` step holding an `in`/`param` membership test over a
//  param whose source resolves to a LIST scopes its rows by that selection; the
//  binding resolves by SUBSTITUTION (Core's `Transform.substituteListParams`),
//  never through the scalar env; an EMPTY selection is UNBOUND and so PRUNES the
//  step ("nothing selected ⇒ no constraint", the multi-select twin of Phase 424's
//  unset-chip rule); and a kind mismatch in either direction reaches Core's strict
//  `UnboundParam` rather than silently mis-scoping the rows.
//
//  Reactivity keys off `Transform.paramsOf`, which names a list param exactly as
//  it names a scalar one — so the chip→grid edge is derived, never declared.
// ============================================================================

open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.Core

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

/// `dept IN $depts` — the multi-select chip's membership test.
let private pipeline: Fuaran.Core.Transform list =
    [ Fuaran.Core.Filter(Fuaran.Core.InParam(Fuaran.Core.Col "dept", "depts")) ]

let private bindingFrom (from: Binding<JVal>) : Binding<obj seq> =
    Binding.Transform(TransformSource.Data(table), pipeline, Some [ { From = from; Name = "depts" } ])

/// The declarative chip wiring (the Phase 424 shape, list-valued): the
/// multi-select's `values` binding IS `$filters.depts`, so its write-back
/// stores the selection there and the param reads the same name. No host code
/// between them, and no handler on either side.
let private chipSourced = bindingFrom (Binding.Filter("depts", None))

let private rowsOf (binding: Binding<obj seq>) (sources: BindingResolver.BindingSources) =
    BindingResolver.resolve sources binding

let private rowCount (binding: Binding<obj seq>) (sources: BindingResolver.BindingSources) : int =
    match rowsOf binding sources with
    | BindingResolver.Resolved rows -> Seq.length rows
    | other -> failtestf "expected Resolved, got %A" other

/// The wire/server shape of a selection: the `JValObj` array lowering the state
/// store holds after a decoded tree seeds it.
let private selection (values: string list) : obj = nn (values |> List.map box)

[<Tests>]
let tests =
    testList
        "Phase 610 — list-valued Binding.Transform params"
        [ test "a selection scopes the rows (dept IN $filters.depts)" {
              let sources =
                  { BindingResolver.empty with
                      Filters = Map.ofList [ "depts", selection [ "eng" ] ] }

              Expect.equal (rowCount chipSourced sources) 2 "only the two eng rows survive the membership test"
          }

          test "a wider selection widens the scope" {
              let sources =
                  { BindingResolver.empty with
                      Filters = Map.ofList [ "depts", selection [ "eng"; "sales" ] ] }

              Expect.equal (rowCount chipSourced sources) 3 "both departments selected"
          }

          test "an EMPTY selection prunes the step (nothing selected ⇒ no constraint)" {
              // The acceptance criterion: deselecting everything shows the UNFILTERED
              // table, not the empty one an `in []` membership test would produce.
              let sources =
                  { BindingResolver.empty with
                      Filters = Map.ofList [ "depts", selection [] ] }

              Expect.equal (rowCount chipSourced sources) 3 "the unfiltered table, not an empty one"
          }

          test "an unset chip prunes the step, exactly as an unset scalar chip does" {
              // The FIRST render, before the user has touched the control: the filter
              // store holds nothing, the source is `NotResolved`, the step prunes.
              Expect.equal (rowCount chipSourced BindingResolver.empty) 3 "all rows shown before any selection"
          }

          test "a State-sourced list param on an UNWRITTEN slot is loud, not lenient" {
              // Pinned deliberately, because the two chip sources differ here and the
              // difference is Phase 424's, not this phase's. An unset `Filter` source is
              // `NotResolved` — absence, so the step prunes. An unwritten `State` slot
              // resolves to a present `null`, which the scalar coercion has read as the
              // `Null` CELL since 424; a `Null` cell is not a selection, so the membership
              // test reaches Core's strict `UnboundParam`. Deselecting to `[]` through a
              // State-sourced chip still prunes (the write stores an empty array); it is
              // only the never-written slot that differs. The declarative chip idiom the
              // pack teaches therefore sources the param from `Filter`, where "never
              // touched" and "cleared" are one answer.
              let stateSourced = bindingFrom (Binding.State("depts", None))

              match rowsOf stateSourced BindingResolver.empty with
              | BindingResolver.Errored m ->
                  Expect.stringContains m "depts" "the error names the param, rather than silently showing every row"
              | other -> failtestf "expected a loud Errored, got %A" other
          }

          test "a State-sourced list param prunes once the slot holds an empty selection" {
              let stateSourced = bindingFrom (Binding.State("depts", None))

              let sources =
                  { BindingResolver.empty with
                      State = Map.ofList [ "depts", selection [] ] }

              Expect.equal (rowCount stateSourced sources) 3 "cleared through a State-sourced chip prunes too"
          }

          test "a genuine JArr source resolves identically to the store lowering" {
              // `Binding.Static` carries a verbatim `JVal`; the store carries the host's
              // own array. One coercion serves both, so the two spellings cannot diverge.
              let staticSel = bindingFrom (Binding.Static(Some(JArr [ JStr "sales" ])))
              Expect.equal (rowCount staticSel BindingResolver.empty) 1 "only the single sales row"
          }

          test "a selection of numbers scopes a numeric column" {
              let numeric: Fuaran.Core.Transform list =
                  [ Fuaran.Core.Filter(Fuaran.Core.InParam(Fuaran.Core.Col "amount", "amounts")) ]

              let binding: Binding<obj seq> =
                  Binding.Transform(
                      TransformSource.Data(table),
                      numeric,
                      Some
                          [ { From = Binding.Static(Some(JArr [ JInt 100; JInt 90 ]))
                              Name = "amounts" } ]
                  )

              Expect.equal (rowCount binding BindingResolver.empty) 2 "the 100 and 90 rows"
          }

          test "a LIST bound to a name the pipeline reads as a SCALAR param is loud, not silent" {
              // Substitution binds `in`/`param` occurrences only, so the scalar `param`
              // reaches Core unbound. Core stays strict; the host does not guess.
              let scalarPipeline: Fuaran.Core.Transform list =
                  [ Fuaran.Core.Filter(
                        Fuaran.Core.Binary(Fuaran.Core.Eq, Fuaran.Core.Col "dept", Fuaran.Core.Param "depts")
                    ) ]

              let binding: Binding<obj seq> =
                  Binding.Transform(
                      TransformSource.Data(table),
                      scalarPipeline,
                      Some
                          [ { From = Binding.Static(Some(JArr [ JStr "eng" ]))
                              Name = "depts" } ]
                  )

              match rowsOf binding BindingResolver.empty with
              | BindingResolver.Errored m ->
                  Expect.stringContains m "depts" "the error names the param that could not be bound"
              | other -> failtestf "expected a loud Errored, got %A" other
          }

          test "a param source resolving to a nested array is still the loud non-scalar error" {
              let binding = bindingFrom (Binding.Static(Some(JArr [ JArr [ JStr "eng" ] ])))

              match rowsOf binding BindingResolver.empty with
              | BindingResolver.Errored m ->
                  Expect.stringContains m "non-scalar" "an array of arrays is not a selection"
              | other -> failtestf "expected a loud Errored, got %A" other
          }

          test "Transform.paramsOf names a list param, so reactivity is derived not declared" {
              Expect.equal (Fuaran.Core.Transform.paramsOf pipeline) [ "depts" ] "the derived chip→grid edge"
          }

          test "substituteListParams rewrites the membership test to a literal list" {
              let substituted =
                  Fuaran.Core.Transform.substituteListParams
                      (Map.ofList [ "depts", [ Fuaran.Core.Str "eng" ] ])
                      pipeline

              Expect.equal
                  substituted
                  [ Fuaran.Core.Filter(
                        Fuaran.Core.InList(Fuaran.Core.Col "dept", [ Fuaran.Core.Lit(Fuaran.Core.Str "eng") ])
                    ) ]
                  "InParam resolves to InList by substitution, never through the scalar env"

              Expect.equal (Fuaran.Core.Transform.paramsOf substituted) [] "a substituted step names no param"
          } ]
