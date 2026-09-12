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

open System
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

          // ── Phase 1674 — the PUBLIC SPEC's projection of this table ───────
          //
          //  WIRE_FORMAT.md 5.1 says outright that its table "is a projection of
          //  Fuaran.UI.WireSurvivability (the authoritative, code-side
          //  classification)". Nothing checked that sentence, and it had stopped
          //  being true: the NodeKind block still listed `NodeKind.Layout`,
          //  `.Display`, `.Input` and `.Visualisation` -- the four category
          //  wrappers Phase 692 DELETED, whose 33 kinds have been flat rows in
          //  the code ever since -- and carried three further tables headed
          //  `LayoutKind`, `DisplayKind` and `InputKind`, DUs that do not exist.
          //  The spec is PUBLIC, and the documentation site republishes that
          //  table to /guide/wire-format and llms-full.txt, so a reader and an
          //  LLM were both being taught a vocabulary with four kinds nothing can
          //  emit and none of the kinds that replaced them.
          //
          //  The gate is here rather than in the corpus repo for the reason the
          //  reference-CSS sync and the authoring-pack drift check are both here:
          //  the AUTHORING side is where a change that invalidates a projection
          //  is made, so it is where the failure belongs. A gate in the corpus
          //  would have reported this to whoever next touched the corpus.
          //
          //  It asserts MEMBERSHIP and VERDICT rather than regenerating the
          //  block, and that is a deliberate choice between two shapes. A
          //  generator would pin the bytes and would also own the prose
          //  ("Recoverable alternative" is authored per row, often more
          //  specifically than the classification's one-line hint), so it would
          //  have to either flatten that prose or carry it back into the code.
          //  Membership plus verdict makes the two impossible to disagree about
          //  the things a reader acts on, and leaves the writing to the writer.
          test "WIRE_FORMAT.md 5.1's table names exactly the classified cases, with the same verdicts" {
              let specPath =
                  Fuaran.Tests.CorpusRoot.tryFind ()
                  |> Option.map (fun root -> IO.Path.Combine(root, "WIRE_FORMAT.md"))
                  |> Option.filter IO.File.Exists

              match specPath with
              | None ->
                  skiptest
                      "wire-format-fixtures/WIRE_FORMAT.md not found walking up from the test assembly - this projection gate needs the workspace checkout (skipped in a bare single-repo clone)"
              | Some path ->
                  let text = IO.File.ReadAllText path

                  // The section runs from its own heading to the next `## `.
                  let sectionStart =
                      text.IndexOf("## 5.1 Wire-survivability boundary", StringComparison.Ordinal)

                  Expect.isGreaterThan sectionStart -1 "WIRE_FORMAT.md carries a 5.1 wire-survivability section"

                  let rest = text.Substring(sectionStart + 4)

                  let sectionEnd =
                      match
                          rest.IndexOf(
                              "
## ",
                              StringComparison.Ordinal
                          )
                      with
                      | -1 -> text.Length
                      | i -> sectionStart + 4 + i

                  let section = text.Substring(sectionStart, sectionEnd - sectionStart)

                  // A row is a pipe, a backticked `Du.Case`, then the verdict. The
                  // backticks are what separate a row from the surrounding
                  // prose, which names cases too.
                  let rowShape =
                      Text.RegularExpressions.Regex(
                          @"^\|\s*`([A-Za-z]+\.[A-Za-z0-9]+)`\s*\|\s*([a-z-]+)\s*\|",
                          Text.RegularExpressions.RegexOptions.Multiline
                      )

                  let specRows =
                      rowShape.Matches section
                      |> Seq.cast<Text.RegularExpressions.Match>
                      |> Seq.map (fun m -> m.Groups[1].Value, m.Groups[2].Value)
                      |> Seq.toList

                  Expect.isNonEmpty specRows "the 5.1 section carries table rows this gate can read"

                  let verdictWord (v: WireSurvivability.Survivability) =
                      match v with
                      | WireSurvivability.Survivability.Survivable -> "survivable"
                      | WireSurvivability.Survivability.HostOnly -> "host-only"
                      | WireSurvivability.Survivability.Partial -> "partial"

                  let classified =
                      WireSurvivability.all |> List.map (fun c -> c.Case, verdictWord c.Verdict)

                  let specCases = specRows |> List.map fst |> Set.ofList
                  let codeCases = classified |> List.map fst |> Set.ofList

                  let missing = Set.difference codeCases specCases |> Set.toList
                  let phantom = Set.difference specCases codeCases |> Set.toList

                  Expect.isEmpty
                      missing
                      (sprintf
                          "case(s) classified in Fuaran.UI.WireSurvivability that WIRE_FORMAT.md 5.1 does not list: %s. The spec is PUBLIC and the documentation site republishes this table, so a missing row teaches a vocabulary that is not the one the hosts implement. Add the row(s) to the section in the corpus repo, in the same change-set."
                          (String.Join(", ", missing)))

                  Expect.isEmpty
                      phantom
                      (sprintf
                          "case(s) WIRE_FORMAT.md 5.1 lists that no longer exist in Fuaran.UI.WireSurvivability: %s. This is how the four Phase-692 category wrappers survived in the public spec for a year after they were deleted from the code."
                          (String.Join(", ", phantom)))

                  let specVerdict = Map.ofList specRows

                  let disagreeing =
                      classified
                      |> List.choose (fun (case, code) ->
                          match Map.tryFind case specVerdict with
                          | Some spec when spec <> code ->
                              Some(sprintf "%s (spec says %s, code says %s)" case spec code)
                          | _ -> None)

                  Expect.isEmpty
                      disagreeing
                      (sprintf
                          "verdict disagreement(s) between WIRE_FORMAT.md 5.1 and the classification: %s"
                          (String.Join("; ", disagreeing)))
          }

          test "Binding.Computed is host-only and names its recoverable alternative" {
              match Map.tryFind "Binding.Computed" WireSurvivability.byCase with
              | Some c ->
                  Expect.equal c.Verdict WireSurvivability.Survivability.HostOnly "Binding.Computed must be host-only"
                  Expect.isSome c.Alternative "Binding.Computed must name a recoverable alternative"
              | None -> failtest "Binding.Computed missing from the survivability table"
          } ]
