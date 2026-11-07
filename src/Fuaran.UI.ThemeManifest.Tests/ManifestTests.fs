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
              // The wire's token order is canonical path order, the example's is
              // authoring order: the round trip keeps the token SET and the rest.
              Expect.equal (Set.ofList m2.Tokens) (Set.ofList m1.Tokens) "the token set survives"
              Expect.equal { m2 with Tokens = m1.Tokens } m1 "meta, roles and invariants survive"
              Expect.equal s1 s2 "re-encode is byte-identical"
              Expect.equal (decodeOk s2) m2 "a canonical document round-trips exactly"
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

// ─── Phase 2133 — the byte law and the validation pass ──────────

let private decodeInvariants (invariants: string) =
    decodeOk ("""{"tokens":{},"invariants":[""" + invariants + "]}")

let private codes (m: ThemeManifest) =
    ThemeManifest.validate m |> List.map ManifestDefect.encode

[<Tests>]
let byteLawTests =
    testList
        "ThemeManifest — the cross-host byte law (Phase 2133)"
        [ test "the empty manifest is tokens and nothing else" {
              Expect.equal (Encode.encode ThemeManifest.empty) """{"tokens":{}}""" "only the shape selector"
          }

          test "a member at its default is omitted, and keys are Ordinal-sorted" {
              // The same pins the Rust oracle and the TypeScript and Go encoders
              // hold: a default-weight invariant carries no weight, a zero floor
              // no minRatio, an anonymous binding no role.
              let m =
                  { ThemeManifest.empty with
                      Roles =
                          [ { Role = ManifestRole.Named ""
                              TokenName = "t" } ]
                      Invariants =
                          [ Invariant.create (InvariantKind.MotionVoice { MaxDurationMs = 0; Easing = None })
                            Invariant.createWeighted 2.0 (InvariantKind.ContrastFloor("x", 0.0)) ] }

              Expect.equal
                  (Encode.encode m)
                  """{"invariants":[{"kind":"MotionVoice"},{"kind":"ContrastFloor","role":"x","weight":2}],"roles":[{"token":"t"}],"tokens":{}}"""
                  "the omitted-default canonical form"
          }

          test "a token leaf omits an empty $type and keeps an empty $value" {
              let m =
                  { ThemeManifest.empty with
                      Tokens =
                          [ { Name = "b.z"
                              Type = ""
                              Value = ""
                              Description = None
                              Role = None }
                            { Name = "a"
                              Type = "color"
                              Value = "#fff"
                              Description = None
                              Role = Some "r" } ] }

              Expect.equal
                  (Encode.encode m)
                  """{"tokens":{"a":{"$extensions":{"fuaran":{"role":"r"}},"$type":"color","$value":"#fff"},"b":{"z":{"$value":""}}}}"""
                  "sorted at every depth; $value is the leaf discriminator"
          }

          test "the omitted-default floor decodes tolerantly to a floor of 0" {
              // Phase 2077 pinned this tolerant decode; the MEANING is checked by
              // the validation pass (Phase 2133), not by the decoder.
              let m = decodeInvariants """{"kind":"ContrastFloor","role":"x"}"""

              Expect.equal m.Invariants [ Invariant.create (InvariantKind.ContrastFloor("x", 0.0)) ] "tolerant decode"

              Expect.equal
                  (Encode.encode m)
                  """{"invariants":[{"kind":"ContrastFloor","role":"x"}],"tokens":{}}"""
                  "and its canonical bytes"
          }

          test "an unknown invariant kind round-trips with its members and weight" {
              let json =
                  """{"invariants":[{"kind":"FocusRing","minWidthPx":2,"nested":{"b":1,"a":[true,"s"]},"weight":3}],"tokens":{}}"""

              let m = decodeOk json

              match m.Invariants with
              | [ { Kind = InvariantKind.Unknown("FocusRing", members)
                    Weight = 3.0 } ] ->
                  Expect.equal (List.map fst members) [ "minWidthPx"; "nested" ] "members kept, weight lifted out"
              | other -> failtestf "expected one unknown invariant, got %A" other

              Expect.equal
                  (Encode.encode m)
                  """{"invariants":[{"kind":"FocusRing","minWidthPx":2,"nested":{"a":[true,"s"],"b":1},"weight":3}],"tokens":{}}"""
                  "re-emitted canonically, never dropped"

              Expect.equal (Invariant.kind m.Invariants.Head) "FocusRing" "the discriminator is the wire kind"
          } ]

[<Tests>]
let validationTests =
    testList
        "ThemeManifest — validation pass (Phase 2133)"
        [ test "a canonical omitted-default manifest that binds everything validates clean" {
              let m =
                  decodeOk
                      """{"invariants":[{"kind":"ContrastFloor","minRatio":4.5,"role":"Brand"},{"kind":"UsageBudget","token":"color.brand"},{"kind":"MotionVoice"}],"roles":[{"role":{"tone":"Brand"},"token":"color.brand"}],"tokens":{"color":{"brand":{"$type":"color","$value":"#010203"}}}}"""

              Expect.isEmpty (ThemeManifest.validate m) "no defect"
          }

          test "a zero floor (the member omitted) is out of range" {
              Expect.equal
                  (codes (decodeInvariants """{"kind":"ContrastFloor","role":"x"}"""))
                  [ """{"code":"CONTRAST_FLOOR_OUT_OF_RANGE","index":0,"subject":"x"}"""
                    """{"code":"CONTRAST_FLOOR_ROLE_UNBOUND","index":0,"subject":"x"}""" ]
                  "the floor of 0 and the unbound role"
          }

          test "an explicit sub-1 floor is out of range; 1 and 21 are the inclusive bounds" {
              let floorOf (ratio: string) =
                  { decodeInvariants ("""{"kind":"ContrastFloor","role":"Brand","minRatio":""" + ratio + "}") with
                      Roles =
                          [ { Role = ManifestRole.Tone ToneVariant.Brand
                              TokenName = "t" } ] }

              Expect.equal
                  (codes (floorOf "0.5"))
                  [ """{"code":"CONTRAST_FLOOR_OUT_OF_RANGE","index":0,"subject":"Brand"}""" ]
                  "0.5 passes every pair"

              Expect.equal
                  (codes (floorOf "21.5"))
                  [ """{"code":"CONTRAST_FLOOR_OUT_OF_RANGE","index":0,"subject":"Brand"}""" ]
                  "above 21 no pair can pass"

              Expect.isEmpty (codes (floorOf "1")) "1 is in range"
              Expect.isEmpty (codes (floorOf "21")) "21 is in range"
          }

          test "an empty role is reported as empty, not as unbound" {
              Expect.equal
                  (codes (decodeInvariants """{"kind":"ContrastFloor","minRatio":7}"""))
                  [ """{"code":"CONTRAST_FLOOR_ROLE_EMPTY","index":0,"subject":""}""" ]
                  "the floor checks nothing"
          }

          test "a role nothing binds is unbound; a named binding binds its name" {
              let m =
                  decodeInvariants """{"kind":"ContrastFloor","minRatio":7,"role":"body-text"}"""

              Expect.equal
                  (codes m)
                  [ """{"code":"CONTRAST_FLOOR_ROLE_UNBOUND","index":0,"subject":"body-text"}""" ]
                  "unbound"

              let bound =
                  { m with
                      Roles =
                          [ { Role = ManifestRole.Named "body-text"
                              TokenName = "t" } ] }

              Expect.isEmpty (codes bound) "bound by name"
          }

          test "a usage budget on an undeclared token is reported" {
              Expect.equal
                  (codes (decodeInvariants """{"kind":"UsageBudget","targetPct":10,"token":"color.ghost"}"""))
                  [ """{"code":"USAGE_BUDGET_TOKEN_UNDECLARED","index":0,"subject":"color.ghost"}""" ]
                  "names the missing token"
          }

          test "an unknown kind is reported unenforced, by index, after the known ones" {
              Expect.equal
                  (codes (decodeInvariants """{"kind":"MotionVoice"},{"kind":"FocusRing","minWidthPx":2}"""))
                  [ """{"code":"INVARIANT_KIND_UNKNOWN","index":1,"subject":"FocusRing"}""" ]
                  "kept and reported, never dropped"

              let verified = ThemeBridge.verify (decodeInvariants """{"kind":"FocusRing"}""")

              Expect.exists
                  verified.Deferred
                  (fun d -> d.Description.StartsWith "FocusRing")
                  "the bridge's verification lists it as unenforced too"
          }

          test "the example manifest validates clean" {
              Expect.isEmpty (ThemeManifest.validate (decodeOk exampleText)) "the shipped example means what it says"
          } ]

[<Tests>]
let schemaByteLawTests =
    let valid (json: string) =
        use doc = JsonDocument.Parse(json)
        manifestSchema.Value.Evaluate(doc.RootElement).IsValid

    testList
        "ThemeManifest — the schema agrees with the byte law (Phase 2133)"
        [ test "every canonical omitted-default form the encoder writes satisfies the schema" {
              for json in
                  [ Encode.encode ThemeManifest.empty
                    """{"invariants":[{"kind":"ContrastFloor","role":"x"}],"tokens":{}}"""
                    """{"invariants":[{"kind":"UsageBudget"},{"kind":"MotionVoice"}],"roles":[{"token":"t"}],"tokens":{"t":{"$value":""}}}"""
                    Encode.encode (decodeOk exampleText) ] do
                  Expect.isTrue (valid json) $"the schema admits {json}"
          }

          test "the schema documents the floor's range and stays closed for authoring" {
              Expect.isFalse
                  (valid """{"invariants":[{"kind":"ContrastFloor","minRatio":0.5,"role":"x"}],"tokens":{}}""")
                  "an explicit sub-1 floor is refused at authoring time"

              Expect.isFalse
                  (valid """{"invariants":[{"kind":"FocusRing"}],"tokens":{}}""")
                  "an unknown kind is outside the authoring enum, though decoders keep it"
          } ]

[<Tests>]
let kindlessTests =
    test "an object with no non-empty string kind is still not an invariant (Phase 2133)" {
        for src in
            [ """{"role":"x","minRatio":7}"""
              """{"kind":"","role":"x"}"""
              """{"kind":3}""" ] do
            Expect.isEmpty (decodeInvariants src).Invariants $"{src} is not an invariant of any kind"
    }
