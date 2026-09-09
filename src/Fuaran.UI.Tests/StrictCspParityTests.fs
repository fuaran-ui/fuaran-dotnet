module Fuaran.UI.Tests.StrictCspParityTests

open System
open System.IO
open System.Text.RegularExpressions
open Expecto
open Fuaran.UI.Renderer

// ============================================================================
//  Phase 1545 — the cross-tier lock on the generated class name.
//
//  Under `Csp.Strict` a continuous value stops being an inline `style`
//  attribute and becomes a generated CLASS. The server writes the rule; the
//  client, hydrating the same document, has to put the SAME class on the SAME
//  element or the value it names is simply not applied — an unstyled grid on a
//  page whose markup, classes and tests all look right.
//
//  Nothing about the two tiers' emission is shared: the server writes kebab-case
//  keys straight into a style attribute, the client hands React a camelCase
//  object, and their progress fills have always formatted the same percentage
//  differently. So the class cannot be derived from what either tier emits. It
//  is derived instead from `Csp.Declarations`, ONE set of builders both tiers
//  call — and this module is the lock that keeps that true.
//
//  It is a SOURCE scan, because the alternative is not available: the F# Feliz
//  client renderer cannot render to a string on .NET (the `ScalarSsrParityTests`
//  finding), so no test can compare the two tiers' output here. What CAN be
//  compared is which builder each tier calls at each slot, and that is exactly
//  the fact the hydration claim rests on.
//
//  The sources are the ones this build compiled — copied into the test output by
//  the fsproj rather than resolved by climbing to a repo root, so the scan
//  cannot read a different checkout's files (the `CssCoverageTests` precedent).
// ============================================================================

let private rendererSourceDir: string =
    Path.Combine(AppContext.BaseDirectory, "renderer-sources")

let private sourceText (tier: string) (file: string) : string =
    let path = Path.Combine(rendererSourceDir, tier, file)

    if not (File.Exists path) then
        failwithf
            "renderer source not found at %s — the Fuaran.UI.Tests project copies the renderer sources into its output. A shape scan with no source to scan reports every call site as clean."
            path

    File.ReadAllText path

/// Every `Csp.Declarations.<builder>` call in a source, as the builder name.
let private declarationBuilders (source: string) : string list =
    Regex.Matches(source, @"Csp\.Declarations\.([A-Za-z]+)")
    |> Seq.cast<Match>
    |> Seq.map (fun m -> m.Groups[1].Value)
    |> Seq.distinct
    |> Seq.sort
    |> List.ofSeq

/// Every slot discriminator passed to a `cspStyle` call, as the literal. The
/// pattern spans newlines because both tiers write the call multi-line where the
/// argument list is long.
let private cspSlots (source: string) : string list =
    Regex.Matches(source, "cspStyle\\s+ctx\\s+parentNodeId\\s+\"([^\"]+)\"", RegexOptions.Singleline)
    |> Seq.cast<Match>
    |> Seq.map (fun m -> m.Groups[1].Value)
    |> Seq.sort
    |> List.ofSeq

[<Tests>]
let tests =
    testList
        "StrictCsp cross-tier derivation"
        [ test "both renderers derive their classes from the SAME shared builders" {
              let client = declarationBuilders (sourceText "client" "Render.fs")
              let server = declarationBuilders (sourceText "server" "Render.fs")

              Expect.isNonEmpty
                  server
                  "the server renderer calls the shared declaration builders — an empty scan would make the comparison below vacuous"

              Expect.equal
                  client
                  server
                  "the two tiers call the same set of Csp.Declarations builders. A tier that stopped calling one — or started building its own pairs inline — would derive a class name the other never generates, and the symptom is an unstyled element in a hydrated document, not a failing test."
          }

          test "both renderers name the SAME slot discriminators" {
              let client = cspSlots (sourceText "client" "Render.fs")
              let server = cspSlots (sourceText "server" "Render.fs")

              Expect.isNonEmpty server "the server renderer has cspStyle call sites"

              Expect.equal
                  client
                  server
                  "the slot discriminator is part of the hashed seed, so a tier spelling `split-left` where the other spells `left` derives a different class for the same element"
          }

          test "every slot the tiers name is covered by the corpus proof" {
              // The seven slots are enumerated here so that adding an eighth
              // fails HERE, beside the statement that the strict-mode proof
              // covers them all — rather than silently shipping a site the
              // corpus test in Fuaran.UI.Renderer.Server.Tests never renders.
              let expected =
                  [ "flex"
                    "grid"
                    "masonry"
                    "progress-fill"
                    "scroll"
                    "split-left"
                    "split-right" ]

              Expect.equal
                  (cspSlots (sourceText "server" "Render.fs"))
                  expected
                  "the strict-mode slot inventory. A new slot belongs in SANITIZATION.md's inventory table and in StrictCspTests' style-bearing corpus, and this assertion is what says so."
          }

          test "the declaration builders' canonical text is what the class hashes" {
              // The derivation, exercised rather than described: same inputs,
              // same name; one character different, different name.
              let a = Csp.generatedClass "n" "grid" (Csp.Declarations.grid "1fr 1fr" (Some 8))
              let b = Csp.generatedClass "n" "grid" (Csp.Declarations.grid "1fr 1fr" (Some 8))
              let c = Csp.generatedClass "n" "grid" (Csp.Declarations.grid "1fr 1fr" (Some 9))
              let d = Csp.generatedClass "n" "masonry" (Csp.Declarations.grid "1fr 1fr" (Some 8))
              let e = Csp.generatedClass "m" "grid" (Csp.Declarations.grid "1fr 1fr" (Some 8))

              Expect.equal b a "the same node, slot and declarations derive the same class"
              Expect.notEqual c a "a different declaration derives a different class"
              Expect.notEqual d a "a different slot derives a different class"
              Expect.notEqual e a "a different node derives a different class"

              Expect.stringStarts a Csp.classRoot "the class carries the reserved root"
          } ]
