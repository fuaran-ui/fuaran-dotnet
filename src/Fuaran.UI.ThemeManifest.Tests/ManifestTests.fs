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

// Built ONCE: the schema library registers a schema by its `$id`, process-wide,
// and refuses a second registration under the same id.
let private manifestSchema = lazy (JsonSchema.FromText schemaText)

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
              let schema = manifestSchema.Value
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

// ─── Phase 2482 — the ChartSeriesDistinct invariant ─────────────

/// A manifest declaring a chart series palette, slot by slot (declared in
/// REVERSE so the slot-order reading is exercised, not the declaration order).
let private withChartSeries (series: string list) : ThemeManifest =
    { ThemeManifest.empty with
        Tokens =
            series
            |> List.mapi (fun i hex ->
                { Name = $"chart.series.{i + 1}"
                  Type = "color"
                  Value = hex
                  Description = None
                  Role = None })
            |> List.rev
        Invariants = [ Invariant.create InvariantKind.ChartSeriesDistinct ] }

let private phase875 = Fuaran.UI.Defaults.chartSeriesHex

/// The palette Phase 875 replaced (the 2008 Google Charts default, first eight).
let private google2008 =
    [ "#3366cc"
      "#dc3912"
      "#ff9900"
      "#109618"
      "#990099"
      "#0099c6"
      "#dd4477"
      "#66aa00" ]

let private rgb hex =
    Fuaran.UI.ChartPalette.tryParseHex hex |> Option.get

/// `actual` rounds to `expected` at `places` decimals — the precision the
/// reference validator printed.
let private near (expected: float) (places: int) (actual: float) (what: string) =
    let half = 0.5 * (10.0 ** float -places)

    Expect.isTrue
        (abs (actual - expected) <= half)
        (sprintf "%s: %f is not %f to %d places" what actual expected places)

[<Tests>]
let chartPaletteTests =
    testList
        "ThemeManifest — chart series palette (Phase 2482)"
        [ test "the colour maths reproduces the reference validator's figures" {
              // The figures the data-visualisation palette validator printed for
              // the Phase 875 palette and its predecessor (Phase 875's outcome).
              let deutan = Some Fuaran.UI.ChartPalette.CvdKind.Deutan
              let protan = Some Fuaran.UI.ChartPalette.CvdKind.Protan
              let dE = Fuaran.UI.ChartPalette.deltaE

              let lightness hex =
                  fst (Fuaran.UI.ChartPalette.lightnessAndChroma (rgb hex))

              near 17.1 1 (dE deutan (rgb "#21a766") (rgb "#a51574")) "worst adjacent CVD pair (deutan)"
              near 24.3 1 (dE None (rgb "#bf831c") (rgb "#1a86ac")) "worst adjacent normal-vision pair"
              near 9.4 1 (dE protan (rgb "#109618") (rgb "#ff9900")) "2008 worst adjacent CVD pair (protan)"
              near 0.772 3 (lightness "#ff9900") "#ff9900 lightness"
              near 0.479 3 (lightness "#990099") "#990099 lightness"
          }

          test "the Phase 875 default passes on both surfaces" {
              Expect.isEmpty (ThemeManifest.verifyChartPalette (withChartSeries phase875)) "no finding"
          }

          test "the 2008 palette fails, naming #ff9900 on light" {
              let findings = ThemeManifest.verifyChartPalette (withChartSeries google2008)

              let described =
                  findings
                  |> List.map (fun f ->
                      match f with
                      | Fuaran.UI.ChartPalette.PaletteFinding.OutOfLightnessBand(slot, hex, surface, _, _, _) ->
                          sprintf "%d %s %s" slot hex surface
                      | other -> sprintf "%A" other)

              Expect.equal
                  described
                  [ "3 #ff9900 light"; "3 #ff9900 dark"; "5 #990099 dark" ]
                  "exactly the lightness-band failures the reference validator reports"

              Expect.stringContains
                  (Fuaran.UI.ChartPalette.describe findings.Head)
                  "series-3 #ff9900 on light"
                  "the refusal names the slot, the colour and the surface"
          }

          test "an adjacent pair that cannot be told apart is refused by naming the pair" {
              // Slot 2 replaced by a near-twin of slot 1 (adjacent dE 2.7 both
              // with full colour vision and under deuteranopia).
              let palette = "#1a86ac" :: "#1f8fb0" :: List.skip 2 phase875
              let findings = ThemeManifest.verifyChartPalette (withChartSeries palette)

              let pairs =
                  findings
                  |> List.choose (fun f ->
                      match f with
                      | Fuaran.UI.ChartPalette.PaletteFinding.CvdTooClose(a, _, b, _, _, _) -> Some("cvd", a, b)
                      | Fuaran.UI.ChartPalette.PaletteFinding.NormalVisionTooClose(a, _, b, _, _) ->
                          Some("normal", a, b)
                      | _ -> None)

              Expect.contains pairs ("cvd", 1, 2) "the CVD gate names slots 1 and 2"
              Expect.contains pairs ("normal", 1, 2) "the normal-vision gate names slots 1 and 2"

              Expect.exists
                  (findings |> List.map Fuaran.UI.ChartPalette.describe)
                  (fun s -> s.Contains "series-1 #1a86ac and series-2 #1f8fb0")
                  "the sentence names both colours of the pair"
          }

          test "a non-literal slot is refused by name rather than measured" {
              let m =
                  { withChartSeries phase875 with
                      Tokens =
                          (withChartSeries phase875).Tokens
                          |> List.map (fun t ->
                              if t.Name = "chart.series.4" then
                                  { t with Value = "var(--house-green)" }
                              else
                                  t) }

              Expect.equal
                  (ThemeManifest.verifyChartPalette m)
                  [ Fuaran.UI.ChartPalette.PaletteFinding.NotLiteral("series-4", "var(--house-green)") ]
                  "the one unmeasurable slot, named"
          }

          test "a manifest with no chart series has nothing to verify" {
              Expect.isEmpty (ThemeManifest.verifyChartPalette (decodeOk exampleText)) "no chart tokens, no finding"
              Expect.isNone (ThemeManifest.chartTokens (decodeOk exampleText)) "and no chart tokens to install"
          }

          test "chartTokens reads the slots in slot order and defaults an absent status triple" {
              match ThemeManifest.chartTokens (withChartSeries google2008) with
              | None -> failtest "a manifest with series tokens yields chart tokens"
              | Some tokens ->
                  Expect.equal tokens.Series (google2008 |> List.map ColorVar.Hex) "slot order, not declaration order"
                  Expect.equal tokens.Positive Fuaran.UI.Defaults.charts.Positive "absent positive is the default"
                  Expect.equal tokens.Neutral Fuaran.UI.Defaults.charts.Neutral "absent neutral is the default"
          }

          test "ChartSeriesDistinct round-trips with its weight and satisfies the schema" {
              let m =
                  { withChartSeries phase875 with
                      Invariants = [ Invariant.createWeighted 2.5 InvariantKind.ChartSeriesDistinct ] }

              let json = Encode.encode m
              let back = decodeOk json

              Expect.equal back.Invariants m.Invariants "the invariant and its weight survive"
              Expect.equal (Encode.encode back) json "re-encode is byte-identical"
              Expect.equal (Invariant.kind back.Invariants.Head) "ChartSeriesDistinct" "the stable discriminator"

              let schema = manifestSchema.Value
              use doc = JsonDocument.Parse(json)
              Expect.isTrue (schema.Evaluate(doc.RootElement).IsValid) "the published schema admits the kind"
          }

          test "ThemeBridge.verify reports a failing palette where the other invariants report" {
              let failing = ThemeBridge.verify (withChartSeries google2008)

              Expect.isNonEmpty failing.ChartPalette "the chart findings are on the verification result"
              Expect.isFalse (ThemeBridge.VerificationResult.passed failing) "and they fail the gate"

              let passing = ThemeBridge.verify (withChartSeries phase875)
              Expect.isEmpty passing.ChartPalette "the default palette reports nothing"
              Expect.isTrue (ThemeBridge.VerificationResult.passed passing) "and passes"
          } ]
