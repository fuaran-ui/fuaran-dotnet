module Fuaran.UI.Tests.GridReorder

// ============================================================================
//  Phase 934 — declarative row reorder: the two pure halves the renderer's
//  affordance is built from.
//
//  `reorderDestination` is the DECISION — where a reorder commits, by the same
//  precedence the edit path uses (declared `editStateKey` wins; else the
//  Phase-663 State-source floor; else NONE, and the renderer draws no handle
//  at all — a gesture with no destination is the fake-affordance class Phase
//  866 charters against). Both verbs now resolve through ONE function
//  (`gridWriteDestination`), and the `editDestination` list at the foot of this
//  file is Phase 863's half of the same decision.
//
//  `moveRow` is the MECHANICS — the whole-list move whose result is written
//  back wholesale. An invalid move returns the SAME list instance, which the
//  caller uses to write nothing at all: "refused" and "no new bytes" are one
//  behaviour, with no partial state between them.
//
//  The DOM half (the handle button, drag wiring, arrow keys) is Fable-only and
//  not headlessly testable — the constraint DispatchGateTests already records.
//  What IS pinned here is the property the DOM half is driven by: a `None`
//  destination produces empty affordance lists, so a grid without
//  `reorderable` renders through exactly the pre-934 construction.
// ============================================================================

open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Renderer

let private rows (xs: string list) : Row list =
    xs |> List.map (fun t -> Map.ofList [ "task", (box t |> Unchecked.nonNull) ])

let private taskNames (rs: Row list) : string list =
    rs
    |> List.map (fun r ->
        match Map.tryFind "task" r with
        | Some v -> string v
        | None -> "?")

let private stateSource: Binding<Row seq> = Binding.State("sprint-order", None)

let private querySource: Binding<Row seq> =
    Binding.Query("stock", (fun (_: obj) -> Seq.empty), None)

[<Tests>]
let tests =
    testList
        "Fuaran.UI.GridReorder"
        [ testList
              "reorderDestination"
              [ test "not reorderable resolves to no destination whatever else is declared" {
                    Expect.isNone
                        (BindingResolver.reorderDestination false (Some "sprint-order") stateSource)
                        "reorderable=false must yield None — this is what keeps a plain grid byte-identical"
                }

                test "a declared editStateKey wins, and is returned as a State binding on that key" {
                    match BindingResolver.reorderDestination true (Some "sprint-order") querySource with
                    | Some(Binding.State(key, None)) -> Expect.equal key "sprint-order" "the declared destination key"
                    | other -> failtestf "expected State(sprint-order, None), got %A" other
                }

                test "no declared key falls to the Phase-663 floor: the grid's own State source" {
                    match BindingResolver.reorderDestination true None stateSource with
                    | Some(Binding.State(key, _)) -> Expect.equal key "sprint-order" "the source's own key"
                    | other -> failtestf "expected the State source itself, got %A" other
                }

                test "no declared key over a non-State source refuses — no destination, no affordance" {
                    Expect.isNone
                        (BindingResolver.reorderDestination true None querySource)
                        "Query rows are host data: a handle over them would be the Phase-866 fake-affordance class"
                } ]

          testList
              "moveRow"
              [ test "a forward move re-seats the row at the target index" {
                    let moved = BindingResolver.moveRow 0 2 (rows [ "a"; "b"; "c"; "d" ])
                    Expect.equal (taskNames moved) [ "b"; "c"; "a"; "d" ] "a moved to index 2"
                }

                test "a backward move re-seats the row at the target index" {
                    let moved = BindingResolver.moveRow 3 1 (rows [ "a"; "b"; "c"; "d" ])
                    Expect.equal (taskNames moved) [ "a"; "d"; "b"; "c" ] "d moved to index 1"
                }

                test "first-to-last and last-to-first are exact end moves, not clamps" {
                    let toEnd = BindingResolver.moveRow 0 3 (rows [ "a"; "b"; "c"; "d" ])
                    Expect.equal (taskNames toEnd) [ "b"; "c"; "d"; "a" ] "a to the end"
                    let toStart = BindingResolver.moveRow 3 0 (rows [ "a"; "b"; "c"; "d" ])
                    Expect.equal (taskNames toStart) [ "d"; "a"; "b"; "c" ] "d to the start"
                }

                test "a no-op move returns the SAME list instance — the write-nothing contract" {
                    let original = rows [ "a"; "b"; "c" ]
                    let moved = BindingResolver.moveRow 1 1 original

                    Expect.isTrue
                        (obj.ReferenceEquals(moved, original))
                        "same instance, so the caller's reference-equality check writes nothing"
                }

                test "an out-of-range move (either side, either bound) returns the SAME list instance" {
                    let original = rows [ "a"; "b"; "c" ]

                    for fromIdx, toIdx in [ -1, 1; 3, 1; 1, -1; 1, 3 ] do
                        let moved = BindingResolver.moveRow fromIdx toIdx original

                        Expect.isTrue
                            (obj.ReferenceEquals(moved, original))
                            (sprintf "move %d->%d out of range must be identity" fromIdx toIdx)
                } ]

          // ── Phase 863's half of the SAME decision ────────────────────────
          // The reorder destination above and the edit destination below are
          // one rule with two gates, because both write the whole updated rows
          // value of one collection. Until this pass the renderer resolved the
          // edit destination inline as the Phase-663 floor alone, so a grid
          // DECLARING `editStateKey` over a `Query` source — the corner
          // `nodes/grid-declared-edit.json` pins, and one FUARAN090's 863
          // widening already reports as live — rendered with no inputs at all.
          // These cases pin the two ends of the fix: the declared key acts, and
          // an undeclared grid is unchanged.
          testList
              "editDestination"
              [ test "not editable resolves to no destination whatever else is declared" {
                    Expect.isNone
                        (BindingResolver.editDestination false (Some "stock-adjustments") stateSource)
                        "editable=false must yield None — a non-editable grid renders spans, as before"
                }

                test "a declared editStateKey makes a Query-sourced grid writable — 863's whole point" {
                    match BindingResolver.editDestination true (Some "stock-adjustments") querySource with
                    | Some(Binding.State(key, None)) ->
                        Expect.equal key "stock-adjustments" "the declared destination key"
                    | other -> failtestf "expected State(stock-adjustments, None), got %A" other
                }

                test "a declared editStateKey WINS over the source, so one collection has one destination" {
                    match BindingResolver.editDestination true (Some "stock-adjustments") stateSource with
                    | Some(Binding.State(key, None)) ->
                        Expect.equal key "stock-adjustments" "the declared key, not the source's own"
                    | other -> failtestf "expected the declared key to win, got %A" other
                }

                test "no declared key falls to the Phase-663 floor — the shipped behaviour, unchanged" {
                    match BindingResolver.editDestination true None stateSource with
                    | Some(Binding.State(key, _)) -> Expect.equal key "sprint-order" "the source's own key"
                    | other -> failtestf "expected the State source itself, got %A" other
                }

                test "no declared key over a non-State source refuses — display-only, as FUARAN090 warns" {
                    Expect.isNone
                        (BindingResolver.editDestination true None querySource)
                        "no writable slot: the grid stays display-only rather than committing nowhere"
                }

                test "edit and reorder resolve the SAME destination for the same declaration" {
                    // The single-destination property stated as a test rather
                    // than as a comment: if the two ever diverge, one grid gains
                    // two destinations for one collection and this goes red.
                    // Compared through `describe` because `Binding` carries
                    // functions and so supports no equality constraint.
                    let describe (d: Binding<Row seq> option) =
                        match d with
                        | None -> "none"
                        | Some(Binding.State(key, _)) -> "state:" + key
                        | Some other -> sprintf "other:%A" other

                    for key in [ None; Some "sprint-order" ] do
                        for label, source in [ "State", stateSource; "Query", querySource ] do
                            Expect.equal
                                (describe (BindingResolver.editDestination true key source))
                                (describe (BindingResolver.reorderDestination true key source))
                                (sprintf "edit and reorder must agree for key=%A over a %s source" key label)
                } ] ]
