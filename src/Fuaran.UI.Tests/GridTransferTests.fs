module Fuaran.UI.Tests.GridTransfer

// ============================================================================
//  The cross-container transfer state machine (Phase 1123).
//
//  Every judgement `GridTransfer` makes lives in a pure function, and this file
//  is why they are pure. The .NET test runner mounts no DOM, so a decision made
//  inline in a `dragover` handler could only ever be ASSERTED about in prose —
//  and the claims this phase makes are exactly the kind that are worth nothing
//  unasserted:
//
//    * a grid never transfers to ITSELF (that is Phase 934's reorder)     → `canReceive`
//    * an undeclared end pairs with nothing                              → `accepts`
//    * a drop past the last row APPENDS rather than being refused         → `clampInsert`
//    * a removal the caller cannot perform writes nothing new             → `removeAt`
//    * the record is the four members the specification fixes, always     → `record`
//
//  The `SwitchStage` shape (Phase 1122), for the same reason and with the same
//  discipline: each obligation is pinned by a test that can fail.
// ============================================================================

open Expecto
open Fuaran.Core
open Fuaran.UI.Renderer.GridTransfer

let private lifted (source: string) (index: int) (itemId: string) (outKey: string option) : Lifted<string> =
    { SourceNodeId = source
      SourceIndex = index
      ItemId = itemId
      OutKey = outKey
      Row = itemId
      Release = None }

[<Tests>]
let tests =
    testList
        "cross-container transfer state machine"
        [
          // ── accepts — the pairing rule ───────────────────────────────────
          testList
              "accepts — two declarations name one key"
              [ test "the same key on both sides pairs" {
                    Expect.isTrue (accepts (Some "board") (Some "board")) "one key, two sides"
                }

                test "different keys do not pair" {
                    Expect.isFalse (accepts (Some "board") (Some "other")) "two boards are two boards"
                }

                test "an absent declaration on either side pairs with nothing" {
                    // Every grid written before this release declares neither,
                    // and this is the assertion that keeps them all inert.
                    Expect.isFalse (accepts None (Some "board")) "nothing releases"
                    Expect.isFalse (accepts (Some "board") None) "nothing accepts"
                    Expect.isFalse (accepts None None) "the pre-1123 grid"
                }

                test "the EMPTY key pairs with nothing, even with itself" {
                    // A key of "" names no state slot, so two grids carrying it
                    // are not a board — they are two grids with an unfilled
                    // declaration, and pairing them would invent a channel.
                    Expect.isFalse (accepts (Some "") (Some "")) "an empty key is not a key"
                } ]

          // ── canReceive — and the self-transfer that must not be one ──────
          testList
              "canReceive — a grid never transfers to itself"
              [ test "a different grid on a paired key receives" {
                    Expect.isTrue
                        (canReceive (lifted "todo" 0 "t-1" (Some "board")) "done" (Some "board"))
                        "todo releases, done accepts"
                }

                test "THE SAME grid does not, however the keys read" {
                    // This is the load-bearing one. A two-way column declares
                    // both ends of one key, so `accepts` alone would say yes to
                    // a drop on the grid the drag began in — and that gesture is
                    // a REORDER, which Phase 934 already owns. Routing it here
                    // would give one gesture two implementations.
                    Expect.isFalse
                        (canReceive (lifted "todo" 0 "t-1" (Some "board")) "todo" (Some "board"))
                        "a drop on the source grid is a reorder, not a transfer"
                }

                test "a lift with no out-key is a reorder drag and reaches no target" {
                    // What a drag begun on a reorder-only grid carries, and what
                    // a lift on a grid with no usable row identity is demoted to.
                    Expect.isFalse (canReceive (lifted "todo" 0 "" None) "done" (Some "board")) "nothing to pair with"
                } ]

          // ── clampInsert — where an incoming row lands ────────────────────
          testList
              "clampInsert — a drop is positioned, never refused"
              [ test "an in-range index is itself" { Expect.equal (clampInsert 2 5) 2 "unchanged" }

                test "past the end APPENDS rather than being refused" {
                    // A drop below the last row means the end of the list, and
                    // refusing it would make the most natural gesture on a board
                    // do nothing.
                    Expect.equal (clampInsert 9 3) 3 "clamped to the append position"
                }

                test "a negative index is the top" { Expect.equal (clampInsert -4 3) 0 "clamped to the head" }

                test "the append position of an EMPTY list is 0" {
                    // The empty target column, which is an ordinary state for a
                    // board to be in and the one a naive bound would get wrong.
                    Expect.equal (clampInsert 0 0) 0 "an empty list accepts at 0"
                    Expect.equal (clampInsert 7 0) 0 "and only at 0"
                } ]

          // ── insertAt / removeAt — the two halves of a move ───────────────
          testList
              "insertAt / removeAt — the collection edits"
              [ test "insertAt puts the item BEFORE the named position" {
                    Expect.equal (insertAt 1 "x" [ "a"; "b"; "c" ]) [ "a"; "x"; "b"; "c" ] "inserted before b"
                }

                test "insertAt at the length appends" {
                    Expect.equal (insertAt 3 "x" [ "a"; "b"; "c" ]) [ "a"; "b"; "c"; "x" ] "appended"
                }

                test "insertAt clamps on its own, so it is total" {
                    Expect.equal (insertAt 99 "x" [ "a" ]) [ "a"; "x" ] "past the end appends"
                    Expect.equal (insertAt -1 "x" [ "a" ]) [ "x"; "a" ] "before the start heads"
                }

                test "removeAt takes out exactly the named index" {
                    Expect.equal (removeAt 1 [ "a"; "b"; "c" ]) [ "a"; "c" ] "b is gone"
                }

                test "removeAt out of range returns the list UNCHANGED" {
                    // The caller writes the result back wholesale, so "invalid
                    // removal writes nothing new" and "invalid removal is
                    // refused" have to be the same behaviour — there is no
                    // partial state in between for a reader to see.
                    let rows = [ "a"; "b" ]
                    Expect.equal (removeAt 5 rows) rows "past the end"
                    Expect.equal (removeAt -1 rows) rows "before the start"
                }

                test "a remove then an insert is a MOVE that preserves the other rows" {
                    let source = [ "a"; "b"; "c" ]
                    let target = [ "x"; "y" ]
                    let moved = List.item 1 source

                    Expect.equal (removeAt 1 source) [ "a"; "c" ] "the source loses exactly one row"
                    Expect.equal (insertAt 0 moved target) [ "b"; "x"; "y" ] "the target gains exactly that row"
                } ]

          // ── record — the shape the specification fixes ───────────────────
          testList
              "record — the four members, always"
              [ test "the record carries itemId / from / to / index and nothing else" {
                    let r = record "t-7" "todo" "done" 2

                    Expect.equal
                        r
                        (JObj
                            [ "itemId", JStr "t-7"
                              "from", JStr "todo"
                              "to", JStr "done"
                              "index", JInt 2 ])
                        "the specified shape, member for member"
                }

                test "index 0 is present rather than omitted" {
                    // The one member with a plausible identity value. Omitting it
                    // would make the top of a list indistinguishable from a
                    // record that failed to state a position, and the shape is
                    // fixed as ALL FOUR precisely so a reader never has to guess.
                    match record "t-7" "todo" "done" 0 with
                    | JObj members ->
                        Expect.isTrue (members |> List.exists (fun (k, _) -> k = "index")) "index is written at 0"
                    | other -> failtestf "expected an object, got %A" other
                }

                test "an empty itemId still produces a well-formed record" {
                    // The FUARAN130 shape reaching the renderer anyway: a grid
                    // whose only identity is a closure. The record stays
                    // well-formed and says plainly that nothing was named, which
                    // is what makes the defect visible in the state rather than
                    // in a crash.
                    match record "" "todo" "done" 1 with
                    | JObj members ->
                        Expect.equal
                            (members |> List.tryFind (fun (k, _) -> k = "itemId") |> Option.map snd)
                            (Some(JStr ""))
                            "the empty identity is stated, not dropped"
                    | other -> failtestf "expected an object, got %A" other
                } ]

          // ── keyIntent — the keyboard route ───────────────────────────────
          testList
              "keyIntent — the chords, and everything that is not one"
              [ test "Control+X lifts" {
                    Expect.equal (keyIntent "x" true) TransferIntent.Lift "the cut chord"
                    Expect.equal (keyIntent "X" true) TransferIntent.Lift "shift-cased, same chord"
                }

                test "a BARE x does not lift" {
                    // A bare letter would be captured from anything focusable and
                    // would collide with type-ahead in the surrounding page.
                    Expect.equal (keyIntent "x" false) TransferIntent.Ignore "no modifier, no lift"
                }

                test "Escape cancels with no modifier" {
                    // A reader who wants out of a state must not have to remember
                    // a chord to get there.
                    Expect.equal (keyIntent "Escape" false) TransferIntent.Cancel "plain Escape"
                    Expect.equal (keyIntent "Escape" true) TransferIntent.Cancel "and with one, harmlessly"
                }

                test "the arrow keys are NOT this module's" {
                    // They belong to Phase 934's reorder, and the two routes
                    // compose on one handle: lift moves a row between lists, the
                    // arrows move it within one. If this returned anything but
                    // Ignore, the reorder would be shadowed.
                    Expect.equal (keyIntent "ArrowUp" false) TransferIntent.Ignore "reorder's"
                    Expect.equal (keyIntent "ArrowDown" true) TransferIntent.Ignore "reorder's, modifier or not"
                }

                test "an ordinary key is ignored" {
                    Expect.equal (keyIntent "a" false) TransferIntent.Ignore "nothing to do"
                    Expect.equal (keyIntent "Enter" false) TransferIntent.Ignore "activation is the button's own"
                } ]

          // ── the announcements ────────────────────────────────────────────
          testList
              "announcements — a mode change a listener can detect"
              [ test "a lift names the row and the route out" {
                    let m = liftAnnouncement "Ship the release"

                    Expect.stringContains m "Ship the release" "the row is named"
                    Expect.stringContains m "Escape" "and the way out of the state is stated"
                }

                test "a placement names the position as well as the fact" {
                    // "Moved" alone leaves the reader to go and look.
                    Expect.equal (placeAnnouncement "t-7" 0 3) "Moved t-7 to position 1 of 3." "1-based for a reader"
                }

                test "the refusal says how to succeed" {
                    // The place control is a real affordance whose precondition
                    // was not met; silence would make it read as broken.
                    Expect.stringContains nothingLiftedAnnouncement "Control+X" "the chord that fixes it"
                } ] ]
