module Fuaran.UI.Tests.GridReorder

// ============================================================================
//  Phase 934 — declarative row reorder: the two pure halves the renderer's
//  affordance is built from.
//
//  `reorderDestination` is the DECISION — where a reorder commits, by the same
//  precedence the edit path uses (declared `editStateKey` wins; else the
//  Phase-663 State-source floor; else NONE, and the renderer draws no handle
//  at all — a gesture with no destination is the fake-affordance class Phase
//  866 charters against).
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
                } ] ]
