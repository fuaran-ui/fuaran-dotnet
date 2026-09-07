module Fuaran.UI.Tests.TransformBudget

// ============================================================================
//  Phase 1532 — the client evaluator has a budget.
//
//  A decoded tree declares its own pipeline: `Join`, `Window`, `Pivot`, an
//  arbitrary chain of steps, over a source the tree also names. On the client
//  that pipeline runs in the reader's browser on every render AND on every
//  store notification touching its channel keys — so a tree that arrived over
//  the wire decided how much work the reader's machine does, per keystroke,
//  with nothing at all between the declaration and the evaluator.
//
//  The budget is measured in the two quantities a TREE declares — rows in and
//  steps in the pipeline — never in wall-clock time. Elapsed time is a property
//  of the machine, so a time-budgeted evaluator refuses different trees on a
//  phone and a workstation and no author can predict which; rows and steps are
//  the same numbers everywhere, so a refusal is reproducible and actionable.
//
//  The product is the third limit and not decoration: two limits alone admit
//  the whole row allowance through the whole step allowance, which is exactly
//  the case the budget exists for.
// ============================================================================

open Expecto
open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Renderer

module Budget = BindingResolver.TransformBudget

let private defaults = Budget.defaults

/// An `n`-row single-column table — the shape the evaluator counts rows over.
let private tableOf (n: int) : Table =
    { Schema = [ "v", IntType ]
      Columns = [ Column.create "v" IntType [ for i in 1..n -> Int i ] ] }

/// An `n`-step pipeline of no-op sorts. Every step is legal and cheap, so what
/// a refusal measures is the DECLARATION, not the evaluator's opinion of it.
let private pipelineOf (n: int) : Transform list = [ for _ in 1..n -> Sort [ "v", Asc ] ]

let private resolveRows (table: Table) (pipeline: Transform list) =
    BindingResolver.resolve<Row seq>
        BindingResolver.empty
        (Binding.Transform(TransformSource.Data(Embedded table), pipeline, None))

[<Tests>]
let tests =
    testList
        "Phase 1532 — the Transform evaluation budget"
        [ test "check: within every limit is Ok" {
              Expect.isOk (Budget.check defaults 1 1) "a one-row, one-step pipeline"

              Expect.isOk
                  (Budget.check defaults defaults.MaxRows 1)
                  "the row limit itself is admitted — the limit is a maximum, not a strict bound"

              Expect.isOk (Budget.check defaults 1 defaults.MaxSteps) "and so is the step limit"
          }

          test "check: each limit refuses by name and states what it refused" {
              match Budget.check defaults (defaults.MaxRows + 1) 1 with
              | Ok() -> failtest "one row past the row limit must refuse"
              | Error m ->
                  Expect.stringStarts m Budget.RefusalMarker "the stable marker leads"
                  Expect.stringContains m "input rows" "and names the quantity"
                  Expect.stringContains m (string defaults.MaxRows) "and the limit it overran"

              match Budget.check defaults 1 (defaults.MaxSteps + 1) with
              | Ok() -> failtest "one step past the step limit must refuse"
              | Error m ->
                  Expect.stringStarts m Budget.RefusalMarker "the stable marker leads"
                  Expect.stringContains m "pipeline steps" "and names the quantity"
          }

          test "check: the PRODUCT refuses a pipeline both other limits admit" {
              // The case two limits cannot see, and the reason the third exists.
              let rows = defaults.MaxRowSteps / defaults.MaxSteps + 1
              let steps = defaults.MaxSteps

              Expect.isTrue (rows <= defaults.MaxRows) "rows are inside the row limit"
              Expect.isTrue (steps <= defaults.MaxSteps) "steps are inside the step limit"

              match Budget.check defaults rows steps with
              | Ok() -> failtest "the product must refuse what the two limits admit"
              | Error m ->
                  Expect.stringStarts m Budget.RefusalMarker "the stable marker leads"
                  Expect.stringContains m "row-steps" "and names the product"
                  Expect.stringContains m (string defaults.MaxRowSteps) "and the limit"
          }

          test "check: the product is computed without overflowing" {
              // `50_000 * 50_000` overflows `int` to a NEGATIVE number, and a
              // negative product passes every `>` comparison — a bound that
              // overflows is an absent bound wearing the shape of a present one.
              // Both operands come from a tree, so this is reachable.
              let huge =
                  { Budget.unbounded with
                      MaxRowSteps = 1_000_000 }

              match Budget.check huge 50_000 50_000 with
              | Ok() -> failtest "2.5 billion row-steps must refuse, not wrap to a negative"
              | Error m -> Expect.stringContains m "row-steps" "and it is the product that refused"
          }

          test "unbounded admits what defaults refuse — and is reached by name" {
              Expect.isError (Budget.check defaults (defaults.MaxRows * 2) 1) "the default refuses"

              Expect.isOk
                  (Budget.check Budget.unbounded (defaults.MaxRows * 2) defaults.MaxSteps)
                  "the named opt-out admits it"
          }

          test "a Transform inside the budget still resolves its rows" {
              // The property that makes the budget shippable: every pipeline the
              // estate actually renders is orders of magnitude inside it.
              match resolveRows (tableOf 3) (pipelineOf 2) with
              | BindingResolver.Resolved rows -> Expect.equal (Seq.length rows) 3 "all three rows come back"
              | other -> failtestf "expected the rows, got %A" other
          }

          test "a Transform over the step limit resolves Errored, naming the budget" {
              // End to end, through the public resolver: a tree declaring too
              // long a pipeline gets a refusal a host can read, not a hang.
              match resolveRows (tableOf 3) (pipelineOf (defaults.MaxSteps + 1)) with
              | BindingResolver.Errored m ->
                  Expect.stringStarts m Budget.RefusalMarker "the resolver surfaces the budget refusal verbatim"
                  Expect.stringContains m "pipeline steps" "naming what overran"
              | other -> failtestf "expected Errored, got %A" other
          }

          test "a Transform over the row-step product resolves Errored" {
              let rows = defaults.MaxRowSteps / defaults.MaxSteps + 1

              match resolveRows (tableOf rows) (pipelineOf defaults.MaxSteps) with
              | BindingResolver.Errored m -> Expect.stringContains m "row-steps" "the product is what refused"
              | other -> failtestf "expected Errored, got %A" other
          }

          test "the budget counts the PRUNED pipeline, not the declared one" {
              // A filter whose declared param resolves to nothing is dropped
              // before evaluation ("unset filter ⇒ no constraint"), so the steps
              // that will actually run are fewer than the tree wrote. Counting
              // the declaration would refuse work that never happens — which is
              // why the check sits after the prune and not before it.
              let unboundFilters =
                  [ for _ in 1 .. defaults.MaxSteps + 4 -> Filter(Binary(Gt, Col "v", Param "unset")) ]

              // An unregistered query resolves `NotResolved`, which is what
              // makes the param unbound and the filters prunable.
              let unsetParam: TransformParam =
                  { Name = "unset"
                    From = Binding.Query("no-such-query", (fun (_: obj) -> JInt 0), None) }

              let source =
                  Binding.Transform(TransformSource.Data(Embedded(tableOf 3)), unboundFilters, Some [ unsetParam ])

              match BindingResolver.resolve<Row seq> BindingResolver.empty source with
              | BindingResolver.Resolved rows ->
                  Expect.equal (Seq.length rows) 3 "every step was pruned, so nothing was over budget"
              | other -> failtestf "expected the pruned pipeline to resolve, got %A" other
          }

          test "an undeclared param name is NOT a prune, and the budget sees those steps" {
              // The twin of the test above, and the reason it has to declare the
              // param: only a DECLARED-but-unresolved param prunes. A pipeline
              // naming a param the tree never declared reaches the evaluator
              // whole — so those steps are real work and the budget counts them.
              let filters =
                  [ for _ in 1 .. defaults.MaxSteps + 4 -> Filter(Binary(Gt, Col "v", Param "never-declared")) ]

              match resolveRows (tableOf 3) filters with
              | BindingResolver.Errored m -> Expect.stringContains m "pipeline steps" "counted, not pruned"
              | other -> failtestf "expected the budget to refuse, got %A" other
          } ]
