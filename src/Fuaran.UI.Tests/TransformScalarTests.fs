module Fuaran.UI.Tests.TransformScalar

// ============================================================================
//  Phase 632 — `Binding.Transform` in SCALAR slots + `Selection.field`.
//
//  Pins the scalar resolution rule (the Filter-law style pinning): a Transform
//  in a scalar slot resolves to the lone cell of an exactly-1×1 result via the
//  two canonical terminals (`project [col] + limit 1`; `groupBy(keys: [],
//  aggs: [one agg])`); >1 row/col is a LOUD didactic (never a silent first
//  cell); an empty result renders absence — except a trailing global
//  single-`count`, which resolves 0. And the designed-together adjacency: a
//  `Selection.field` projection keeps the binding (and a Transform param fed
//  by it) scalar after a real row click — the r42 composition's post-click
//  path, which errored loudly pre-632.
// ============================================================================

open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Renderer

let private nn (v: 'T) : obj = box v |> Unchecked.nonNull

// The r42-shaped table: id / alert / severity.
let private table =
    Fuaran.Core.Embedded
        { Schema =
            [ "id", Fuaran.Core.StringType
              "alert", Fuaran.Core.StringType
              "severity", Fuaran.Core.StringType ]
          Columns =
            [ Fuaran.Core.Column.create
                  "id"
                  Fuaran.Core.StringType
                  [ Fuaran.Core.Str "TCK-2041"
                    Fuaran.Core.Str "TCK-2042"
                    Fuaran.Core.Str "TCK-2043" ]
              Fuaran.Core.Column.create
                  "alert"
                  Fuaran.Core.StringType
                  [ Fuaran.Core.Str "alert-2041"
                    Fuaran.Core.Str "alert-2042"
                    Fuaran.Core.Str "alert-2043" ]
              Fuaran.Core.Column.create
                  "severity"
                  Fuaran.Core.StringType
                  [ Fuaran.Core.Str "critical"
                    Fuaran.Core.Str "high"
                    Fuaran.Core.Str "critical" ] ] }

/// The r42 composition, canonicalised: Selection-fed param → filter → project
/// one column → limit 1. The Selection carries the Phase-632 `field`
/// projection, so the param stays scalar post-click.
let private r42Binding: Binding<string> =
    Binding.Transform(
        TransformSource.Data(table),
        [ Fuaran.Core.Filter(Fuaran.Core.Binary(Fuaran.Core.Eq, Fuaran.Core.Col "id", Fuaran.Core.Param "ticketId"))
          Fuaran.Core.Project [ "alert", "alert" ]
          Fuaran.Core.Limit(1, 0) ],
        Some
            [ { From =
                  Binding.Selection(
                      "ticket-grid",
                      (fun raw -> Fuaran.Core.JStr(Binding.projectSelectionField<string> "id" raw)),
                      Some(Fuaran.Core.JStr "TCK-2041"),
                      Some "id"
                  )
                Name = "ticketId" } ]
    )

let private countBinding<'T> (severity: string) : Binding<'T> =
    Binding.Transform(
        TransformSource.Data(table),
        [ Fuaran.Core.Filter(
              Fuaran.Core.Binary(Fuaran.Core.Eq, Fuaran.Core.Col "severity", Fuaran.Core.Lit(Fuaran.Core.Str severity))
          )
          Fuaran.Core.GroupBy(
              [],
              [ { Name = "n"
                  Fn = Fuaran.Core.AggFn.Count
                  Of = "id" } ]
          ) ],
        None
    )

/// A clicked row in the grid's write shape (a `Map<string, obj>`, cells boxed).
let private clickedRow (id: string) : obj =
    Map.ofList [ "id", nn id; "alert", nn ("alert-" + id) ] |> nn

[<Tests>]
let tests =
    testList
        "Phase 632 — Transform in scalar slots"
        [ test "the r42 composition resolves the defaulted row's cell (pre-click)" {
              match BindingResolver.resolveScalarText BindingResolver.empty r42Binding with
              | BindingResolver.Resolved v -> Expect.equal v "alert-2041" "the defaultValue keys the lookup"
              | other -> failtestf "expected Resolved, got %A" other
          }

          test "the r42 composition resolves the CLICKED row's cell (Selection.field keeps the param scalar)" {
              let sources =
                  { BindingResolver.empty with
                      Selections = Map.ofList [ NodeId "ticket-grid", clickedRow "TCK-2042" ] }

              match BindingResolver.resolveScalarText sources r42Binding with
              | BindingResolver.Resolved v -> Expect.equal v "alert-2042" "the projected row key re-keys the lookup"
              | other -> failtestf "expected Resolved post-click, got %A" other
          }

          test "a global single-agg groupBy resolves the count" {
              match BindingResolver.resolveScalarText BindingResolver.empty (countBinding "critical") with
              | BindingResolver.Resolved v -> Expect.equal v "2" "two critical rows"
              | other -> failtestf "expected Resolved, got %A" other
          }

          test "the count of nothing is 0 (empty input + trailing global count)" {
              match BindingResolver.resolveScalarText BindingResolver.empty (countBinding "no-such-severity") with
              | BindingResolver.Resolved v -> Expect.equal v "0" "the host completes the SQL global-aggregate semantic"
              | other -> failtestf "expected Resolved 0, got %A" other
          }

          test "a numeric slot coerces the count cell to float" {
              let asFloat: Binding<float> = countBinding<float> "critical"

              match BindingResolver.resolveScalarFloat BindingResolver.empty asFloat with
              | BindingResolver.Resolved v -> Expect.equal v 2.0 "Int 2 → 2.0"
              | other -> failtestf "expected Resolved, got %A" other
          }

          test "an N×M result is a LOUD didactic, never a silent first cell" {
              let bare: Binding<string> = Binding.Transform(TransformSource.Data(table), [], None)

              match BindingResolver.resolveScalarText BindingResolver.empty bare with
              | BindingResolver.Errored m ->
                  Expect.stringContains m "one row × one column" "names the rule"
                  Expect.stringContains m "groupBy" "teaches the aggregate terminal"
              | other -> failtestf "expected Errored, got %A" other
          }

          test "an empty non-aggregate result renders absence (NotResolved)" {
              let emptyLookup: Binding<string> =
                  Binding.Transform(
                      TransformSource.Data(table),
                      [ Fuaran.Core.Filter(
                            Fuaran.Core.Binary(
                                Fuaran.Core.Eq,
                                Fuaran.Core.Col "id",
                                Fuaran.Core.Lit(Fuaran.Core.Str "TCK-9999")
                            )
                        )
                        Fuaran.Core.Project [ "alert", "alert" ]
                        Fuaran.Core.Limit(1, 0) ],
                      None
                  )

              match BindingResolver.resolveScalarText BindingResolver.empty emptyLookup with
              | BindingResolver.NotResolved -> ()
              | other -> failtestf "expected NotResolved, got %A" other
          }

          test "non-Transform bindings pass through the scalar path unchanged" {
              match BindingResolver.resolveScalarText BindingResolver.empty (Binding.Static(Some "plain")) with
              | BindingResolver.Resolved v -> Expect.equal v "plain" "Static delegates to resolve"
              | other -> failtestf "expected Resolved, got %A" other
          }

          test "a direct Selection.field read projects the clicked row's field" {
              let direct: Binding<string> =
                  Binding.Selection(
                      "ticket-grid",
                      Binding.projectSelectionField<string> "id",
                      Some "TCK-2041",
                      Some "id"
                  )

              let sources =
                  { BindingResolver.empty with
                      Selections = Map.ofList [ NodeId "ticket-grid", clickedRow "TCK-2043" ] }

              match BindingResolver.resolveScalarText BindingResolver.empty direct with
              | BindingResolver.Resolved v -> Expect.equal v "TCK-2041" "pre-click: the declared default"
              | other -> failtestf "expected Resolved default, got %A" other

              match BindingResolver.resolveScalarText sources direct with
              | BindingResolver.Resolved v -> Expect.equal v "TCK-2043" "post-click: the projected field"
              | other -> failtestf "expected Resolved projection, got %A" other
          }

          test "a missing field on the clicked row is a loud accessor error" {
              let direct: Binding<string> =
                  Binding.Selection(
                      "ticket-grid",
                      Binding.projectSelectionField<string> "no-such-field",
                      None,
                      Some "no-such-field"
                  )

              let sources =
                  { BindingResolver.empty with
                      Selections = Map.ofList [ NodeId "ticket-grid", clickedRow "TCK-2041" ] }

              match BindingResolver.resolveScalarText sources direct with
              | BindingResolver.Errored m -> Expect.stringContains m "no-such-field" "names the missing field"
              | other -> failtestf "expected Errored, got %A" other
          } ]
