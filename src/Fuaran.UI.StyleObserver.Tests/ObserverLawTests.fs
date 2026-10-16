module Fuaran.UI.StyleObserver.Tests.ObserverLawTests

// ─── The in-memory style observer runs on Core's ObserverWitness ────
//
// Phase 2307. `InMemoryStyleObserver` no longer carries an engine of its own:
// it threads an `ObserverState` through Core's `ObserverWitness` functions,
// and the witness it is built on — `Flags.derive`, plus the per-node
// manifest flags when a manifest is wired — is the subject of Core's
// observer law family, `Conformance.observerLawsAt`.

open Expecto
open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.StyleObserver
open Fuaran.UI.ThemeManifest

let private brand = Rgba.rgb 59.0 91.0 219.0

/// Tone.Brand bound to a brand token with a contrast floor stricter than AA,
/// so the manifest-wired witness reaches its own flags.
let private manifest: ThemeManifest =
    { Meta = ManifestMeta.anonymous
      Tokens =
        [ { Name = "color.brand.base"
            Type = "color"
            Value = "#3b5bdb"
            Description = None
            Role = None } ]
      Roles =
        [ { Role = ManifestRole.Tone ToneVariant.Brand
            TokenName = "color.brand.base" } ]
      Invariants = [ Invariant.create (InvariantKind.ContrastFloor("Brand", 7.0)) ] }

/// A drawn `StyleInput` across legible, invisible, low-contrast and
/// translucent pairings, toned or not, so both flag sets are reached.
let private genInput (r: ConfRng.T) : Flags.StyleInput * ConfRng.T =
    let fg, r =
        ConfRng.choose [ Rgba.black; Rgba.white; brand; Rgba.rgba 0.0 0.0 0.0 0.2 ] r

    let bg, r =
        ConfRng.choose [ []; [ Rgba.white ]; [ brand ]; [ Rgba.rgba 0.0 0.0 0.0 0.5; Rgba.white ] ] r

    let tone, r = ConfRng.choose [ None; Some "Brand"; Some "Unbound" ] r

    { Flags.StyleInput.baseline with
        Foreground = fg
        BackgroundLayers = bg
        EmittedTone = tone },
    r

let private assertAllPassed (what: string) (results: LawResult list) =
    Expect.isNonEmpty results (sprintf "%s reported no laws" what)

    for r in results do
        Expect.isTrue r.Passed (sprintf "%s: %s — %A" what r.Law r.Counterexample)

[<Tests>]
let tests =
    testList
        "InMemoryStyleObserver on Core's ObserverWitness"
        [ test
              "the in-memory style observer's witness certifies under Core's observerLawsAt, manifest-free and manifest-wired" {
              for wired in [ None; Some manifest ] do
                  for options in
                      [ StyleObserverOptions.defaults
                        { StyleObserverOptions.defaults with
                            EmitOnFlagChangeOnly = false } ] do
                      Conformance.observerLawsAt (InMemoryStyleObserver.Witness(options, wired)) genInput 2307 200
                      |> assertAllPassed (
                          sprintf
                              "observerLawsAt over the in-memory style observer's witness (manifest %b)"
                              wired.IsSome
                      )
          }

          test "the witness's flags are the observer's flags, manifest flags included" {
              let input =
                  { Flags.StyleInput.baseline with
                      Foreground = Rgba.white
                      BackgroundLayers = [ brand ]
                      EmittedTone = Some "Brand" }

              let observer =
                  InMemoryStyleObserver.createWithManifest StyleObserverOptions.defaults manifest

              observer.RegisterFixture("n", { Input = input; Parent = None })

              let flags = (observer :> IStyleObserver).Observe "n" |> Option.map _.Flags

              let expected =
                  (InMemoryStyleObserver.Witness(StyleObserverOptions.defaults, Some manifest)).Derive input

              Expect.equal flags (Some expected) "the observer reports exactly the witness's derivation"

              Expect.exists
                  expected
                  (function
                  | StyleFlag.ContrastBelowDeclaredFloor _ -> true
                  | _ -> false)
                  "the manifest-aware floor is part of the derivation"
          }

          test "a cyclic parent declaration is walked once per node and ends" {
              // The engine this observer used to carry walked a ring of parents forever.
              let observer = InMemoryStyleObserver.create ()
              let input = Flags.StyleInput.baseline
              observer.RegisterFixture("a", { Input = input; Parent = Some "c" })
              observer.RegisterFixture("b", { Input = input; Parent = Some "a" })
              observer.RegisterFixture("c", { Input = input; Parent = Some "b" })

              let walked = (observer :> IStyleObserver).ObserveTree "a" |> List.map _.NodeId

              Expect.equal walked [ "a"; "b"; "c" ] "each ring member once, by level"
          } ]
