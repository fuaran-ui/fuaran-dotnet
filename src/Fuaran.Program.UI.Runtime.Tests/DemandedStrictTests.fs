module Fuaran.Program.Runtime.Tests.DemandedStrictTests

// ─── The client placement's strict construction path ────────────────
//
// The projection and the validator are the shared package's; what belongs here
// is the CLIENT wiring of them — that this placement derives its coverage from
// the effect registry it was constructed with rather than being told, and that
// a refusal happens before the initial render, so a refused program has not
// painted anything.

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.ServerDriven
open Fuaran.UI.Renderer.BindingResolver
open Fuaran.Program.Bounded
open Fuaran.Program.Runtime
open Fuaran.Program.UI

let private btn (id: string) (action: Action<obj>) : Node<obj> =
    Fuaran.button
        id
        { Defaults.button<obj> with
            OnClick = action }

let private wireOf (action: Action<obj>) : WireTree =
    WireTree.ofDecoded (
        Fuaran.dashboard
            "root"
            { Defaults.dashboard<obj> with
                Children = [ btn "b" action ] }
    )

/// Services whose registry offers exactly `names`, all permitted, rendering
/// into a counter. The registry IS the host's declaration of what it covers,
/// which is the whole point of deriving the coverage from it.
let private servicesOffering (names: string list) (renders: int ref) : ProgramServices =
    let registry =
        names
        |> List.fold (fun reg name -> EffectRegistry.register name ignore reg) EffectRegistry.denyAll
        |> EffectRegistry.permissive

    { ProgramServices.createPermissive (fun _ -> renders.Value <- renders.Value + 1) with
        Effects = registry }

[<Tests>]
let tests =
    testList
        "Phase 892 — client placement, strict construction"
        [ test "coverageOf reads the registry rather than being told" {
              let renders = ref 0
              let none = Program.coverageOf (servicesOffering [] renders)
              Expect.isEmpty none.Effects "denyAll covers nothing"

              let some = Program.coverageOf (servicesOffering [ "Navigate" ] renders)
              Expect.equal some.Effects (Set.ofList [ "Navigate" ]) "the registered performer is the covered vocabulary"
              Expect.isTrue (some.Gate "Navigate") "and the registry's own gate is the policy"
          }

          test "mkBoundedStrict refuses BEFORE the initial render" {
              // A refused program must not have painted: the check is a
              // precondition of construction, not a report on it.
              let renders = ref 0
              let services = servicesOffering [] renders

              match
                  Program.mkBoundedStrict
                      services
                      empty
                      (wireOf (Action.Navigate(TextSource.Literal "/x", NavigateTarget.Self)))
              with
              | Ok _ -> failtest "expected a refusal"
              | Error findings ->
                  Expect.equal findings [ CoverageFinding.UnregisteredEffect "Navigate" ] "named the absent effect"

              Expect.equal renders.Value 0 "nothing was rendered"
          }

          test "mkBoundedStrict admits a covered tree and builds it normally" {
              let renders = ref 0
              let services = servicesOffering [ "Navigate" ] renders

              match
                  Program.mkBoundedStrict
                      services
                      empty
                      (wireOf (Action.Navigate(TextSource.Literal "/x", NavigateTarget.Self)))
              with
              | Ok program ->
                  Expect.equal program.BaseTree.Id "root" "the ordinary program"
                  Expect.equal renders.Value 1 "rendered once, exactly as mkBounded would"
              | Error f -> failtestf "expected admission, got %A" f
          }

          test "mkBounded remains the default posture — it admits what strict mode refuses" {
              // The standing behaviour is unchanged: construction succeeds and
              // an uncoverable effect is refused where it is dispatched, with
              // the denial recorded. Strict mode is an opt-in on top.
              let renders = ref 0
              let services = servicesOffering [] renders

              let program =
                  Program.mkBounded
                      services
                      empty
                      (wireOf (Action.Navigate(TextSource.Literal "/x", NavigateTarget.Self)))

              Expect.equal program.BaseTree.Id "root" "built anyway"
              Expect.equal renders.Value 1 "and rendered"
          }

          test "PROBE — widening the tree's demand flips admission to refusal" {
              // One host, one registry; only the tree changes. Without this the
              // three expectations above would all pass against a strict mode
              // that admitted everything.
              let renders = ref 0
              let services = servicesOffering [ "WriteToClipboard" ] renders

              Expect.isTrue
                  (Program.mkBoundedStrict services empty (wireOf (Action.WriteToClipboard(TextSource.Literal "x")))
                   |> Result.isOk)
                  "within coverage: admitted"

              let widened =
                  Action.Chain
                      [ Action.WriteToClipboard(TextSource.Literal "x")
                        Action.Navigate(TextSource.Literal "/escape", NavigateTarget.Self) ]

              Expect.isFalse
                  (Program.mkBoundedStrict services empty (wireOf widened) |> Result.isOk)
                  "widened past coverage: refused"
          } ]
