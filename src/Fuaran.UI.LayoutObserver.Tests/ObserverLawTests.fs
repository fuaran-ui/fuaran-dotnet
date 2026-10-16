module Fuaran.UI.LayoutObserver.Tests.ObserverLawTests

// ─── The in-memory layout observer runs on Core's ObserverWitness ───
//
// Phase 2307. `InMemoryLayoutObserver` threads an `ObserverState` through
// Core's `ObserverWitness` functions, so the witness it is built on is the
// subject of Core's observer law family: `Conformance.observerLawsAt` drives
// a drawn registration script (parents, updates, an unregistration) through
// it and holds every emission and snapshot to the derivation of its input,
// and walks a ring of parents to its end.

open Expecto
open Fuaran.Core
open Fuaran.UI.LayoutObserver

/// A drawn `LayoutInput` that reaches the flag vocabulary: a size (zero
/// included), and optionally a horizontal overflow and a declared aspect ratio.
let private genInput (r: ConfRng.T) : Flags.LayoutInput * ConfRng.T =
    let w, r = ConfRng.intBelow 400 r
    let h, r = ConfRng.intBelow 400 r
    let overflow, r = ConfRng.intBelow 3 r
    let aspect, r = ConfRng.intBelow 3 r
    let baseInput = Flags.LayoutInput.empty (float w) (float h)

    let withOverflow =
        if overflow = 0 then
            { baseInput with
                ScrollWidth = Some(float w + 50.0)
                ClientWidth = Some(float w)
                OverflowX = Some "hidden" }
        else
            baseInput

    let withAspect =
        if aspect = 0 then
            { withOverflow with
                ExpectedAspectRatio = Some 16.0 }
        else
            withOverflow

    withAspect, r

let private assertAllPassed (what: string) (results: LawResult list) =
    Expect.isNonEmpty results (sprintf "%s reported no laws" what)

    for r in results do
        Expect.isTrue r.Passed (sprintf "%s: %s — %A" what r.Law r.Counterexample)

[<Tests>]
let tests =
    testList
        "InMemoryLayoutObserver on Core's ObserverWitness"
        [ test "the in-memory layout observer's witness certifies under Core's observerLawsAt" {
              for options in
                  [ LayoutObserverOptions.defaults
                    { LayoutObserverOptions.defaults with
                        EmitOnFlagChangeOnly = false } ] do
                  Conformance.observerLawsAt (InMemoryLayoutObserver.Witness options) genInput 2307 200
                  |> assertAllPassed "observerLawsAt over the in-memory layout observer's witness"
          }

          test "a cyclic parent declaration is walked once per node and ends" {
              let observer = InMemoryLayoutObserver.create ()
              let input = Flags.LayoutInput.empty 10.0 10.0
              observer.RegisterFixture("a", { Input = input; Parent = Some "b" })
              observer.RegisterFixture("b", { Input = input; Parent = Some "a" })

              let walked = (observer :> ILayoutObserver).ObserveTree "a" |> List.map _.NodeId

              Expect.equal walked [ "a"; "b" ] "each ring member once, root first"
          } ]
