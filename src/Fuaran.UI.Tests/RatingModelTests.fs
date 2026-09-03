module Fuaran.UI.Tests.RatingModel

// ============================================================================
//  The rating control's judgements (Phase 1130).
//
//  `GridPasteTests`' reason. The .NET test runner mounts no DOM and Feliz's
//  .NET `ReactElement` is opaque, so a keyboard model written inline in the
//  React component could only be asserted ABOUT — and every claim here is a
//  claim about an arithmetic edge where prose is worth nothing:
//
//    * a keystroke at either end       → the value must stop, not wrap
//    * a half-step control             → 0.5 per press, and 0.5 positions
//    * a bound average (4.3 of 5)      → drawn as a PARTIAL star, not rounded
//    * `Home`                          → 0, which is "no rating", not one star
//    * a key that is not ours          → `None`, so Tab still works
//
//  The last is the one that is invisible until someone cannot tab out of a
//  form: a handler that returned a value for every key would swallow Tab and
//  trap focus on the control, and nothing about the markup would look wrong.
//
//  The model is also what the SSR floor computes its star row and its
//  announcement from, so a failure here is a failure on both tiers at once —
//  which is the whole reason it is one module rather than two copies.
// ============================================================================

open Expecto
open Fuaran.UI.Renderer.RatingModel

[<Tests>]
let tests =
    testList
        "RatingModel"
        [ test "the granularity is one unit, or a half when halves are admitted" {
              Expect.equal (step false) 1.0 "Whole stars by default"
              Expect.equal (step true) 0.5 "Halves when asked for"
          }

          test "a value is folded into the scale rather than refused" {
              // Clamping and not refusing is the render path's share of the
              // rule: a form that vanished because a bound average was
              // momentarily 5.2 would take every other field with it.
              Expect.equal (clamp 5 7.0) 5.0 "Above the ceiling"
              Expect.equal (clamp 5 -2.0) 0.0 "Below the floor"
              Expect.equal (clamp 5 4.3) 4.3 "Inside, untouched"
              Expect.equal (clamp 5 nan) 0.0 "NaN is no rating, not a crash"
          }

          test "entry snaps to a position; a bound value does not" {
              // `snap` is applied to what a keystroke or a click PRODUCES.
              // Nothing applies it to what a binding resolves — which is why
              // the fills test below can still see 4.3.
              Expect.equal (snap false 5 3.4) 3.0 "Whole-star entry rounds"
              Expect.equal (snap true 5 3.4) 3.5 "Half-star entry rounds to the half"
              Expect.equal (snap true 5 3.24) 3.0 "…and away from it when nearer the whole"
              Expect.equal (snap false 5 9.0) 5.0 "Snapping still cannot leave the scale"
          }

          test "arrow keys move by one step and stop at both ends" {
              Expect.equal (keyIntent false 5 3.0 "ArrowRight") (Some 4.0) "Right increases"
              Expect.equal (keyIntent false 5 3.0 "ArrowUp") (Some 4.0) "Up is the same gesture"
              Expect.equal (keyIntent false 5 3.0 "ArrowLeft") (Some 2.0) "Left decreases"
              Expect.equal (keyIntent false 5 3.0 "ArrowDown") (Some 2.0) "Down is the same gesture"
              // STOPS, never wraps: a slider's ends are ends. Wrapping here
              // would turn "one more star" into "no stars at all".
              Expect.equal (keyIntent false 5 5.0 "ArrowRight") (Some 5.0) "At the ceiling it stays"
              Expect.equal (keyIntent false 5 0.0 "ArrowLeft") (Some 0.0) "At the floor it stays"
          }

          test "a half-step control moves in halves" {
              Expect.equal (keyIntent true 5 3.0 "ArrowRight") (Some 3.5) "Half up"
              Expect.equal (keyIntent true 5 3.5 "ArrowLeft") (Some 3.0) "Half down"
          }

          test "Home clears the rating and End fills it" {
              // Home going to ZERO rather than to one star is the deliberate
              // reading: `aria-valuemin` is 0, an empty row of stars is what
              // "no rating" looks like, and a reader who wants to take a rating
              // back has no other gesture for it.
              Expect.equal (keyIntent false 5 4.0 "Home") (Some 0.0) "Home is no rating"
              Expect.equal (keyIntent false 5 1.0 "End") (Some 5.0) "End is the ceiling"
          }

          test "a key the control does not own is left alone" {
              // The handler only calls `preventDefault` when this returns
              // `Some`, so a model that answered every key would swallow Tab
              // and trap focus on the control.
              Expect.isNone (keyIntent false 5 3.0 "Tab") "Tab is the host's"
              Expect.isNone (keyIntent false 5 3.0 "Enter") "Enter is the form's"
              Expect.isNone (keyIntent false 5 3.0 "a") "An ordinary character is nobody's"
          }

          test "the star row fills left to right, and a fraction stays a fraction" {
              Expect.equal (fills 5 3.0) [ 1.0; 1.0; 1.0; 0.0; 0.0 ] "Three whole stars"
              Expect.equal (fills 5 0.0) [ 0.0; 0.0; 0.0; 0.0; 0.0 ] "No rating is an empty row"
              Expect.equal (fills 5 5.0) [ 1.0; 1.0; 1.0; 1.0; 1.0 ] "A full row"
              // The reason the value slot is a float at all: a bound average is
              // drawn as it is, not rounded to the nearest star.
              let partial = fills 5 4.3
              Expect.equal (List.length partial) 5 "One entry per position"
              Expect.equal (List.take 4 partial) [ 1.0; 1.0; 1.0; 1.0 ] "Four whole"
              Expect.isTrue (abs (List.item 4 partial - 0.3) < 1e-9) "…and three tenths of the fifth"
          }

          test "the fill class is the three-state vocabulary, not the fraction" {
              Expect.equal (fillClass 0.0) "fuaran-rating-star-empty" "Empty"
              Expect.equal (fillClass 1.0) "fuaran-rating-star-full" "Full"
              Expect.equal (fillClass 0.3) "fuaran-rating-star-partial" "Anything between"
          }

          test "the announcement is a sentence, and a whole figure has no decimal point" {
              // "3.5" alone is not a rating: the scale is half the fact, which
              // is why `aria-valuetext` carries this rather than the bare number
              // `aria-valuenow` already holds.
              Expect.equal (valueText 5 4.0) "4 out of 5" "A whole figure reads as a whole"
              Expect.equal (valueText 5 3.5) "3.5 out of 5" "A half keeps its half"
              Expect.equal (valueText 5 4.3) "4.3 out of 5" "A bound average reads exactly"
              Expect.equal (valueText 10 0.0) "0 out of 10" "The scale is whatever the document said"
          } ]
