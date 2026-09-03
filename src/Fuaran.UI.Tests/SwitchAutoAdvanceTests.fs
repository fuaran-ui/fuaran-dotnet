module Fuaran.UI.Tests.SwitchAutoAdvance

// ============================================================================
//  The Switch stage's state machine (Phase 1122).
//
//  Every judgement `SwitchStage` makes lives in four pure functions, and this
//  file is why they are pure. The .NET test runner mounts no DOM, so a machine
//  buried inside a `useEffect` could only ever be ASSERTED about in prose —
//  and the phase's WCAG 2.2.2 obligations are exactly the kind of claim that is
//  worth nothing unasserted. Pulled out, each one is pinned by a test that can
//  fail:
//
//    * PAUSE on hover / focus / touch-hold      → `stageMode … paused = true`
//    * STOP PERMANENTLY on interaction          → `stopped` outranks everything
//    * INERT under prefers-reduced-motion       → no timer, ever
//
//  The third is the one most easily lost. A stylesheet can suppress the
//  TRANSITION and cannot suppress the ADVANCE, so if this rule regressed the
//  visible symptom would be a reduced-motion reader's content changing under
//  them with the animation correctly disabled — a failure that looks like
//  success in a screenshot.
// ============================================================================

open Expecto
open Fuaran.UI.Renderer.SwitchStage

let private threePanels = [ "a"; "b"; "c" ]

[<Tests>]
let tests =
    testList
        "switch auto-advance state machine"
        [
          // ── nextMatch — where a tick goes ────────────────────────────────
          testList
              "nextMatch — declaration order, wrapping"
              [ test "advances to the next case in declaration order" {
                    Expect.equal (nextMatch threePanels (Some "a")) (Some "b") "a → b"
                    Expect.equal (nextMatch threePanels (Some "b")) (Some "c") "b → c"
                }

                test "wraps from the last case to the first" {
                    Expect.equal (nextMatch threePanels (Some "c")) (Some "a") "c wraps to a"
                }

                test "a current value matching no case advances to the FIRST case" {
                    // The switch is showing its `Default`. Staying there would
                    // make an auto-advancing carousel whose key happens to be
                    // unset silently do nothing at all.
                    Expect.equal (nextMatch threePanels (Some "unset")) (Some "a") "defaulted → first case"
                    Expect.equal (nextMatch threePanels None) (Some "a") "unresolved → first case"
                }

                test "a single case does not advance" {
                    // Wrapping from the only case to itself would rewrite the
                    // key on every tick with the value it already holds.
                    Expect.equal (nextMatch [ "only" ] (Some "only")) None "one case has nowhere to go"
                    Expect.equal (nextMatch [] None) None "no cases at all has nowhere to go"
                }

                test "duplicate matches resolve on the FIRST occurrence" {
                    // Matching the renderer's own first-match-wins selection.
                    // Disagreeing here would make the advance skip a case for a
                    // reason nothing explains. FUARAN082 reports the duplicate.
                    Expect.equal
                        (nextMatch [ "a"; "b"; "a"; "c" ] (Some "a"))
                        (Some "b")
                        "the first 'a' is the one showing"
                } ]

          // ── stepMatch — where an arrow key or a swipe goes ───────────────
          testList
              "stepMatch — both directions, wrapping"
              [ test "steps forward exactly as nextMatch does" {
                    for current in [ Some "a"; Some "b"; Some "c"; Some "unset"; None ] do
                        Expect.equal
                            (stepMatch threePanels current 1)
                            (nextMatch threePanels current)
                            "a forward step and a tick are the same move"
                }

                test "steps backward, wrapping at the front" {
                    Expect.equal (stepMatch threePanels (Some "b") -1) (Some "a") "b → a"
                    Expect.equal (stepMatch threePanels (Some "a") -1) (Some "c") "a wraps to c"
                }

                test "a defaulted switch steps to the LAST case going backward" {
                    // "The one before this" from a switch showing nothing is the
                    // end of the list, which is where a reader pressing Left
                    // expects to land.
                    Expect.equal (stepMatch threePanels None -1) (Some "c") "unresolved, backward → last case"
                }

                test "a single case steps nowhere in either direction" {
                    Expect.equal (stepMatch [ "only" ] (Some "only") 1) None "forward"
                    Expect.equal (stepMatch [ "only" ] (Some "only") -1) None "backward"
                } ]

          // ── stageMode — the three WCAG obligations ───────────────────────
          testList
              "stageMode — WCAG 2.2.2"
              [ test "runs at the declared interval when nothing suppresses it" {
                    Expect.equal
                        (stageMode (Some 5000) true false false false)
                        (StageMode.Running 5000)
                        "an interval, somewhere to go, no preference, not stopped, not paused"
                }

                test "PAUSE — hover, focus or a held touch suspends the timer" {
                    Expect.equal (stageMode (Some 5000) true false false true) StageMode.Paused "paused, not stopped"
                }

                test "PAUSE is reversible — releasing returns to running" {
                    // The discriminator between pause and stop, and the reason
                    // they are separate states rather than one flag: a pause is
                    // a courtesy the reader did not ask for, so nothing is
                    // decided and it must come back.
                    Expect.equal
                        (stageMode (Some 5000) true false false false)
                        (StageMode.Running 5000)
                        "the same inputs with `paused = false` run again"
                }

                test "STOP is permanent — it outranks every other input" {
                    // The one-way latch. Nothing restores it: not the pause
                    // ending, not a preference change, not a re-render.
                    for reduced in [ true; false ] do
                        for paused in [ true; false ] do
                            for hasNext in [ true; false ] do
                                Expect.equal
                                    (stageMode (Some 5000) hasNext reduced true paused)
                                    StageMode.Stopped
                                    "stopped wins over every combination"
                }

                test "REDUCED MOTION — the timer never starts" {
                    // The obligation a stylesheet structurally cannot meet: the
                    // reduce rule makes the transition inert and the content
                    // would still change under the reader.
                    Expect.equal (stageMode (Some 5000) true true false false) StageMode.Inert "reduce means no timer"
                }

                test "no declared interval is inert" {
                    Expect.equal
                        (stageMode None true false false false)
                        StageMode.Inert
                        "absence is the spelling of off"
                }

                test "a non-positive interval is inert" {
                    // The decoder refuses these, so no decoded tree carries one
                    // — but an in-process author can construct one, and a
                    // zero-millisecond interval would be a tight re-render loop.
                    Expect.equal (stageMode (Some 0) true false false false) StageMode.Inert "zero"
                    Expect.equal (stageMode (Some -1) true false false false) StageMode.Inert "negative"
                }

                test "nothing to advance to is inert" {
                    // `hasNext` folds together "fewer than two cases" and "no
                    // writable state key" — both mean the tick has nowhere to go.
                    Expect.equal (stageMode (Some 5000) false false false false) StageMode.Inert "no next"
                }

                test "Inert and Stopped stay DISTINCT" {
                    // Collapsing them would make `data-fuaran-switch-state`
                    // unable to say whether a stationary carousel was never
                    // running or was stopped by the reader, which is exactly the
                    // distinction an accessibility audit needs to see.
                    Expect.notEqual StageMode.Inert StageMode.Stopped "two different facts"
                    Expect.equal (StageMode.token StageMode.Inert) "inert" "inert token"
                    Expect.equal (StageMode.token StageMode.Stopped) "stopped" "stopped token"
                    Expect.equal (StageMode.token StageMode.Paused) "paused" "paused token"
                    Expect.equal (StageMode.token (StageMode.Running 1)) "running" "running token"
                } ]

          // ── swipeIntent — the gesture threshold ──────────────────────────
          testList
              "swipeIntent — the 40px threshold"
              [ test "a finger moving LEFT advances" {
                    Expect.equal (swipeIntent -40.0) (Some 1) "exactly at the threshold counts"
                    Expect.equal (swipeIntent -120.0) (Some 1) "well past it counts"
                }

                test "a finger moving RIGHT goes back" {
                    Expect.equal (swipeIntent 40.0) (Some -1) "exactly at the threshold counts"
                    Expect.equal (swipeIntent 120.0) (Some -1) "well past it counts"
                }

                test "a tap is not a swipe" {
                    // Above the ~10px a browser already absorbs as a tap, so an
                    // ordinary press on a control inside the stage does not
                    // register as a gesture.
                    Expect.equal (swipeIntent 0.0) None "no movement"
                    Expect.equal (swipeIntent 8.0) None "a thumb settling"
                    Expect.equal (swipeIntent -39.9) None "just under, left"
                    Expect.equal (swipeIntent 39.9) None "just under, right"
                } ] ]
