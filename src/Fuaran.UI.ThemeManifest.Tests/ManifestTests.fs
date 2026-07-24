module Fuaran.UI.ThemeManifest.Tests.ManifestTests

// ─── ThemeManifest model + DTCG round-trip + invariants ─────────
//
// Acceptance criteria covered:
//   - A DTCG token file's $value/$type/$description round-trips
//     without loss; a vanilla DTCG file (no wrapper) decodes to a
//     manifest with empty Roles/Invariants.
//   - The example manifest validates against theme-manifest.schema.json
//     and round-trips encode→decode stably (byte-identical re-encode).
//   - resolveRole Tone.Brand returns the declared brand token; every
//     Invariant carries a Weight.

open System.IO
open Expecto
open Json.Schema
open System.Text.Json
open Fuaran.UI.Types
open Fuaran.UI.ThemeManifest

// Compile-time source dir → the sibling package's shipped artefacts.
let private pkgDir =
    Path.Combine(__SOURCE_DIRECTORY__, "..", "Fuaran.UI.ThemeManifest")

let private exampleText =
    File.ReadAllText(Path.Combine(pkgDir, "example-theme.manifest.json"))

let private schemaText =
    File.ReadAllText(Path.Combine(pkgDir, "theme-manifest.schema.json"))

let private decodeOk (json: string) : ThemeManifest =
    match Decode.decode json with
    | Ok m -> m
    | Error e -> failwithf "decode failed: %s" e

// A minimal vanilla DTCG file — token tree at top level, no Fuaran wrapper.
let private vanillaDtcg =
    """
    {
      "color": {
        "accent": { "$type": "color", "$value": "#112233", "$description": "An accent." }
      },
      "space": {
        "lg": { "$type": "dimension", "$value": "24px" }
      }
    }
    """

[<Tests>]
let tests =
    testList
        "ThemeManifest"
        [ test "example manifest validates against theme-manifest.schema.json" {
              let schema = JsonSchema.FromText(schemaText)
              use doc = JsonDocument.Parse(exampleText)
              let result = schema.Evaluate(doc.RootElement)
              Expect.isTrue result.IsValid "example must satisfy the published schema"
          }

          test "example manifest decodes with tokens, roles, and invariants" {
              let m = decodeOk exampleText
              Expect.equal m.Meta.Name "Harbour" "meta name"
              Expect.isGreaterThan (List.length m.Tokens) 0 "tokens present"
              Expect.isGreaterThan (List.length m.Roles) 0 "roles present"
              Expect.isGreaterThan (List.length m.Invariants) 0 "invariants present"
          }

          test "resolveRole Tone.Brand returns the declared brand token" {
              let m = decodeOk exampleText

              match ThemeManifest.resolveRole ToneVariant.Brand m with
              | Some tok ->
                  Expect.equal tok.Name "color.brand.base" "brand role resolves to the brand token"
                  Expect.equal tok.Value "#3b5bdb" "with its declared value"
                  Expect.equal tok.Type "color" "and DTCG $type"
              | None -> failtest "Tone.Brand must resolve"
          }

          test "resolveRole resolves every tone in the example" {
              let m = decodeOk exampleText

              for tone in
                  [ ToneVariant.Default
                    ToneVariant.Subdued
                    ToneVariant.Brand
                    ToneVariant.Success
                    ToneVariant.Warning
                    ToneVariant.Critical
                    ToneVariant.Info ] do
                  Expect.isSome (ThemeManifest.resolveRole tone m) $"tone {tone} resolves"
          }

          test "resolveNamedRole resolves a named (non-tone) role" {
              let m = decodeOk exampleText

              match ThemeManifest.resolveNamedRole "body-text" m with
              | Some tok -> Expect.equal tok.Value "#1a1a1a" "body-text resolves to the dark token"
              | None -> failtest "named role body-text must resolve"
          }

          test "every Invariant carries a Weight; explicit weight survives round-trip" {
              let m = decodeOk exampleText
              Expect.all m.Invariants (fun i -> i.Weight > 0.0) "every invariant has a positive weight"

              // The MotionVoice invariant declared weight 0.5 — must survive decode.
              let motion =
                  m.Invariants
                  |> List.tryPick (fun i ->
                      match i.Kind with
                      | InvariantKind.MotionVoice mb -> Some(i.Weight, mb)
                      | _ -> None)

              match motion with
              | Some(w, mb) ->
                  Expect.equal w 0.5 "declared weight preserved"
                  Expect.equal mb.MaxDurationMs 240 "motion duration preserved"
                  Expect.equal mb.Easing (Some "ease-out") "easing preserved"
              | None -> failtest "expected a MotionVoice invariant"
          }

          test "UsageBudget invariant decodes target + tolerance" {
              let m = decodeOk exampleText

              let brandBudget =
                  m.Invariants
                  |> List.tryPick (fun i ->
                      match i.Kind with
                      | InvariantKind.UsageBudget("color.brand.base", target, tol) -> Some(target, tol)
                      | _ -> None)

              Expect.equal brandBudget (Some(9.0, 3.0)) "brand budget 9% ± 3%"
          }

          test "encode→decode is stable (byte-identical re-encode)" {
              let m1 = decodeOk exampleText
              let s1 = Encode.encode m1
              let m2 = decodeOk s1
              let s2 = Encode.encode m2
              Expect.equal m1 m2 "decode is stable across a round-trip"
              Expect.equal s1 s2 "re-encode is byte-identical"
          }

          test "vanilla DTCG file decodes to tokens with empty Roles/Invariants" {
              let m = decodeOk vanillaDtcg
              Expect.isEmpty m.Roles "vanilla DTCG has no roles"
              Expect.isEmpty m.Invariants "vanilla DTCG has no invariants"
              Expect.equal (List.length m.Tokens) 2 "both leaves decoded"
          }

          test "DTCG $value/$type/$description round-trip without loss" {
              let m = decodeOk vanillaDtcg

              match ThemeManifest.tryGetToken "color.accent" m with
              | Some tok ->
                  Expect.equal tok.Value "#112233" "$value"
                  Expect.equal tok.Type "color" "$type"
                  Expect.equal tok.Description (Some "An accent.") "$description"
              | None -> failtest "color.accent must decode (dotted from the nested group)"
          }

          test "paletteColours returns the colour token values" {
              let m = decodeOk exampleText
              let palette = ThemeManifest.paletteColours m
              Expect.isTrue (Set.contains "#3b5bdb" palette) "brand colour in palette"
              Expect.isTrue (Set.contains "#ffffff" palette) "surface colour in palette"
              // The dimension/duration tokens are not colours.
              Expect.isFalse (Set.contains "16px" palette) "non-colour tokens excluded"
          }

          test "the SPEC role tag ($extensions.fuaran.role) round-trips" {
              let m = decodeOk exampleText

              match ThemeManifest.tryGetToken "color.brand.base" m with
              | Some tok -> Expect.equal tok.Role (Some "brand-accent") "role tag decoded from $extensions"
              | None -> failtest "brand token must decode"
          } ]
