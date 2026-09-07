module Fuaran.UI.StyleObserver.Tests.ElementRegistrationTests

// ============================================================================
//  What a repeated registration of one node id means — and, in particular,
//  that a SAME-ID REMOUNT is not the same thing as an element already
//  registered.
//
//  The browser observer re-walks `[data-fuaran-node-id]` on every DOM mutation
//  and re-registers everything it finds, so it asks this question constantly.
//  Its old guard was `registry.ContainsKey nodeId`, which cannot tell "this is
//  the element I hold" from "this id now names a different element": a React
//  remount under a stable id therefore left the registry pointing at the
//  DETACHED element for as long as the id lived, and a detached node measures
//  as all zeroes forever.
//
//  These are unit tests of the pure classifier the observer routes through.
//  What they do NOT claim is that the browser observer then behaves — that is a
//  DOM property and this package's real observer is `#if FABLE_COMPILER`. The
//  source pin below is what keeps the observer wired to this rule.
// ============================================================================

open System
open System.IO
open Expecto
open Fuaran.UI.StyleObserver

/// Two distinct objects that are structurally identical. Element identity is
/// the whole question here, so the fixtures must not accidentally answer it by
/// being equal or by being the same instance.
let private element () : obj =
    box (ResizeArray<int>()) |> Unchecked.nonNull

[<Tests>]
let tests =
    testList
        "style-observer element registration"
        [ test "an id the registry does not hold is FRESH" {
              Expect.equal (ElementRegistration.classify None (element ())) ElementRegistration.Fresh "nothing held"
          }

          test "the SAME element offered again is UNCHANGED" {
              // The common case: a mutation elsewhere on the page triggers a
              // full rescan, and every already-registered element is offered
              // back. Re-observing each one every time would be wasteful and
              // would re-schedule a read for a node that has not moved.
              let el = element ()
              Expect.equal (ElementRegistration.classify (Some el) el) ElementRegistration.Unchanged "same object"
          }

          test "a DIFFERENT element under the same id is a REMOUNT" {
              // The finding. Structurally identical and still a different node:
              // the old one is detached, and every reading of it from here on
              // is zero.
              let before = element ()
              let after = element ()

              Expect.equal
                  (ElementRegistration.classify (Some before) after)
                  ElementRegistration.Remounted
                  "a same-id remount is detected"
          }

          test "GO-RED TWIN: the guard this replaced cannot tell those two apart" {
              // `ContainsKey` is the predicate the observer used. It answers
              // the same thing for an unchanged element and for a remount,
              // which is exactly why the detached node survived.
              let before = element ()
              let after = element ()
              let containsKey (existing: obj option) = Option.isSome existing

              Expect.equal
                  (containsKey (Some before))
                  (containsKey (Some after))
                  "the old guard collapses the two cases"

              Expect.notEqual
                  (ElementRegistration.classify (Some before) before)
                  (ElementRegistration.classify (Some before) after)
                  "and the new one does not"
          }

          test "structural equality is NOT what decides it" {
              // Two empty lists are `=`. If the classifier compared by equality
              // rather than by identity it would call a genuine remount
              // `Unchanged` and reintroduce the defect silently.
              let before: obj = box (ResizeArray<int>()) |> Unchecked.nonNull
              let after: obj = box (ResizeArray<int>()) |> Unchecked.nonNull

              Expect.equal
                  (ElementRegistration.classify (Some before) after)
                  ElementRegistration.Remounted
                  "identity, not equality"
          }

          // ── The observer is still wired to the rule ───────────────────────

          test "the browser observer routes registration through the classifier and clears the caches on a remount" {
              // The real observer is `#if FABLE_COMPILER`, so the .NET build
              // never type-checks that branch and no test in this project can
              // run it. What CAN be asserted is that the source still says it —
              // the same posture the renderer's CSS-coverage scan takes, and
              // stated as the regression pin it is rather than as proof the
              // browser behaves.
              let source =
                  Path.Combine(__SOURCE_DIRECTORY__, "..", "Fuaran.UI.StyleObserver", "BrowserStyleObserver.fs")

              Expect.isTrue (File.Exists source) (sprintf "observer source at %s" source)
              let text = File.ReadAllText source

              Expect.stringContains
                  text
                  "ElementRegistration.classify"
                  "registerElement asks the classifier rather than ContainsKey"

              Expect.stringContains text "ElementRegistration.Remounted" "and handles the remount arm"

              // The caches describe the node that LEFT. Keeping them lets the
              // change filter suppress the first emission from the element that
              // replaced it — the one a reader most needs.
              for cache in [ "lastFlagSet.Remove"; "lastEmitAt.Remove"; "lastObservation.Remove" ] do
                  let occurrences = text.Split([| cache |], StringSplitOptions.None).Length - 1

                  Expect.isGreaterThanOrEqual
                      occurrences
                      2
                      (sprintf "%s is cleared on unregister AND on remount (found %d)" cache occurrences)
          }

          test "the subscriber-throw isolation REPORTS rather than swallowing" {
              // The comment that used to sit in that `with` clause said the
              // browser console already surfaced the trace. The `with` clause
              // is what made that untrue.
              let source =
                  Path.Combine(__SOURCE_DIRECTORY__, "..", "Fuaran.UI.StyleObserver", "BrowserStyleObserver.fs")

              let text = File.ReadAllText source

              Expect.stringContains text "reportSubscriberFailure" "a failing subscriber is reported"

              // The exception is BOUND rather than discarded. `with _ -> ()` is
              // the shape that made the old claim false, and binding it is what
              // lets the report carry the stack the reader needs.
              Expect.stringContains text "with ex ->" "the exception is bound, not discarded"
          } ]
