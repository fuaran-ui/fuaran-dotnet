module Fuaran.UI.AiTools.Tests.FidelityTests

// ============================================================================
//  Phase 1591 — the kind-intrinsic ARIA query surface.
//
//  Every assertion here is a PROJECTION check: the answers must agree with
//  `Fuaran.UI.RenderFidelity`, never with a literal transcribed beside them. A
//  literal would be the second source of truth the declaration exists to
//  remove, and it would go stale in exactly the way the hand-kept renderer
//  mirrors did. What the declaration SAYS is locked against the renderer arms
//  in the JsonDecode suite; what this suite says is that the query does not
//  distort it.
// ============================================================================

open Expecto

open Fuaran.UI.RenderFidelity
open Fuaran.UI.AiTools

[<Tests>]
let fidelityQueries =
    testList
        "Fuaran.UI.AiTools.Fidelity — the kind-intrinsic ARIA query"
        [ testCase "forKind answers exactly what the row declares, for every row" (fun () ->
              for r in all do
                  Expect.equal
                      (Fidelity.forKind r.Kind)
                      r.Intrinsic
                      (sprintf "the query disagrees with the render-fidelity table for %s" r.Kind))

          testCase "isAnnounced follows the declaration" (fun () ->
              for r in all do
                  Expect.equal
                      (Fidelity.isAnnounced r.Kind)
                      (not (List.isEmpty r.Intrinsic))
                      (sprintf "`isAnnounced` disagrees with the table for %s" r.Kind))

          testCase "an unknown kind is answered, not thrown at" (fun () ->
              // The honest answer for a kind the table does not carry is "nothing
              // is KNOWN to be pinned" — which is what an emitter asking about a
              // §15.3-tolerated unknown kind needs, and is not the same claim as
              // "this kind is silent". `tryFind` is where that difference lives.
              Expect.isEmpty (Fidelity.forKind "NoSuchKind") "an unknown kind answers with no emissions"
              Expect.isFalse (Fidelity.isAnnounced "NoSuchKind") "an unknown kind is not claimed to be announced"
              Expect.isEmpty (Fidelity.describe "NoSuchKind") "an unknown kind has nothing to describe")

          testCase "the announced shortlist is the rows that declare something" (fun () ->
              Expect.equal
                  Fidelity.kinds
                  (all
                   |> List.filter (fun r -> not (List.isEmpty r.Intrinsic))
                   |> List.map (fun r -> r.Kind))
                  "the shortlist must be the declaring rows, in table order"

              Expect.isNonEmpty
                  Fidelity.kinds
                  "no kind declares an intrinsic emission at all — the query would be answering a question nobody can act on"

              for kind in Fidelity.kinds do
                  Expect.isTrue
                      (Fidelity.isAnnounced kind)
                      (sprintf "%s is on the shortlist but reads as unannounced" kind))

          testCase "the flat enumeration is the table's, in table order" (fun () ->
              Expect.equal Fidelity.all allIntrinsics "the enumeration must be the declaration's own")

          testCase "rolesEmitted and liveRegionsEmitted partition the entries" (fun () ->
              // Per ELEMENT, not per kind: a kind pinning three roles answers with
              // three, because a trait role competes with the one on ITS element
              // and with nothing on the others.
              for r in all do
                  Expect.equal
                      (Fidelity.rolesEmitted r.Kind)
                      (r.Intrinsic
                       |> List.choose (fun a -> a.Role |> Option.map (fun role -> a.Element, role)))
                      (sprintf "rolesEmitted disagrees with the table for %s" r.Kind)

                  Expect.equal
                      (Fidelity.liveRegionsEmitted r.Kind)
                      (r.Intrinsic
                       |> List.choose (fun a -> a.Live |> Option.map (fun l -> a.Element, l)))
                      (sprintf "liveRegionsEmitted disagrees with the table for %s" r.Kind))

          testCase "the motivating question is answerable: Toast is announced, Heading is not" (fun () ->
              // The phase exists because "does this node need an `Accessibility`
              // trait?" had no queryable answer. These two are the ends of it: a
              // kind the renderer announces of its own accord, and one whose
              // announcement is entirely the trait's.
              Expect.isTrue
                  (Fidelity.isAnnounced "Toast")
                  "Toast is emitted with role=\"status\" and aria-live=\"polite\" whatever the trait says"

              Expect.contains
                  (Fidelity.rolesEmitted "Toast" |> List.map snd)
                  "status"
                  "the Toast answer must name the role, not merely that there is one"

              Expect.equal
                  (Fidelity.liveRegionsEmitted "Toast" |> List.map snd)
                  [ Fuaran.UI.Types.LiveRegionKind.Polite ]
                  "the Toast answer must name the politeness"

              Expect.isFalse
                  (Fidelity.isAnnounced "Heading")
                  "a Heading announces nothing of itself — what it announces is exactly what its trait declares")

          testCase "describe renders one line per entry, naming the tier" (fun () ->
              for kind in Fidelity.kinds do
                  let lines = Fidelity.describe kind

                  Expect.equal
                      (List.length lines)
                      (List.length (Fidelity.forKind kind))
                      (sprintf "%s: one line per declared emission" kind)

                  for line in lines do
                      Expect.stringStarts
                          line
                          (kind + "/")
                          (sprintf "%s: a description that does not name its kind" kind)

                      Expect.isTrue
                          (line.Contains "both pipelines" || line.Contains "CLIENT ONLY")
                          (sprintf "%s: a description that does not say which pipelines emit it" kind)) ]
