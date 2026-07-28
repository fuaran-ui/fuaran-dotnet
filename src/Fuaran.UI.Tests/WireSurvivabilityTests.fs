module Fuaran.UI.Tests.WireSurvivability

// ============================================================================
//  Phase 378 — the wire-survivability boundary coverage gate.
//
//  Enumerates every author-facing DU's union cases by reflection and asserts
//  `WireSurvivability.all` classifies each one (and names no phantom case) — so
//  a new NodeKind / Binding / Action / … case cannot ship unclassified. The
//  same forward-coupling discipline `SlotCapability`'s completeness test applies
//  to closure SLOTS, applied here to whole-vocabulary VERDICTS.
// ============================================================================

open Expecto
open FSharp.Reflection
open Fuaran.UI
open Fuaran.UI.Types

/// The DUs whose cases the survivability table must cover. Concrete `obj`
/// instantiations of the generic kinds (reflection needs a closed type).
let private classifiedDus: (string * System.Type) list =
    // Phase 692 — `NodeKind` is flat; the four category DUs are gone, and
    // their 33 cases enumerate under `NodeKind` itself.
    [ "NodeKind", typeof<NodeKind<obj>>
      "FormFieldKind", typeof<FormFieldKind<obj>>
      "CellKindErased", typeof<CellKindErased<obj>>
      "CellFormat", typeof<CellFormat>
      "Binding", typeof<Binding<obj>>
      "Action", typeof<Action<obj>>
      "TextSource", typeof<TextSource> ]

let private actualCases: string list =
    classifiedDus
    |> List.collect (fun (du, t) ->
        FSharpType.GetUnionCases t
        |> Array.toList
        |> List.map (fun c -> sprintf "%s.%s" du c.Name))

[<Tests>]
let tests =
    testList
        "WireSurvivability"
        [ test "every classified DU case carries a survivability verdict" {
              let missing =
                  actualCases
                  |> List.filter (fun c -> not (WireSurvivability.byCase.ContainsKey c))

              Expect.isEmpty
                  missing
                  (sprintf "unclassified DU case(s) — add a WireSurvivability.all row for each: %A" missing)
          }

          test "the survivability table names no phantom case" {
              let actual = Set.ofList actualCases

              let phantom =
                  WireSurvivability.all
                  |> List.map (fun c -> c.Case)
                  |> List.filter (fun c -> not (actual.Contains c))

              Expect.isEmpty phantom (sprintf "table row(s) with no matching DU case (stale name?): %A" phantom)
          }

          test "every steerable case is genuinely non-survivable" {
              for c in WireSurvivability.steerable do
                  Expect.notEqual c.Verdict WireSurvivability.Survivability.Survivable c.Case
          }

          test "Binding.Computed is host-only and names its recoverable alternative" {
              match Map.tryFind "Binding.Computed" WireSurvivability.byCase with
              | Some c ->
                  Expect.equal c.Verdict WireSurvivability.Survivability.HostOnly "Binding.Computed must be host-only"
                  Expect.isSome c.Alternative "Binding.Computed must name a recoverable alternative"
              | None -> failtest "Binding.Computed missing from the survivability table"
          } ]
