module Fuaran.UI.StyleObserver.Tests.StyleObserverCorpus

// ============================================================================
//  The `style-observer/` conformance family — Phase 1752.
//
//  Before this, the byte-identical `StyleFlag` / `StyleObservation` encode was a
//  cross-host law only because each host had written the same literals down: the
//  Python cases were ported into Rust by hand, Go was measured by eye, and this
//  tier's own cases sat in `FlagDerivationTests.fs` beside them. Four test files
//  that agree are not an oracle — a fifth host has nothing to certify against
//  but the other four's tests, and a change to any one of them is only caught if
//  somebody remembers to change the rest.
//
//  So the cases become an ARTEFACT. This module is the authored case list plus
//  the writer that turns it into `<corpus>/style-observer/<id>.json`, and the
//  three sibling hosts read those files instead of their own literals.
//
//  ── Why the writer PROVES rather than records ──────────────────────────────
//
//  An emitter that simply serialised whatever the implementation returned would
//  publish a regression as an expectation, silently, and every host would then
//  be certified against it. Each case therefore declares what it EXISTS TO
//  EXHIBIT (`ExpectKinds`), and `write` refuses — raising, before a single file
//  is touched — when the reference derivation disagrees. The bytes are the
//  implementation's; the CLAIM is the case author's.
//
//  ── Why the manifest is carried as a STRING ────────────────────────────────
//
//  A manifest-aware vector needs a `ThemeManifest`, and every host reaches one
//  through its own `decode(json)`. Carrying the manifest as a nested JSON object
//  would make each host re-serialise it before decoding, and re-serialisation is
//  precisely where hosts differ (key order, number rendering, escape choices).
//  The string is fed to each host's decoder verbatim, so the vector also pins
//  that the manifest wire form is decodable by all of them.
//
//  ── Where the family lives, and why a regen cannot delete it ───────────────
//
//  `style-observer/` is NOT one of `Corpus.wholesaleDirectories`, so
//  `--emit-corpus` neither writes nor clears it — the `teleport/` and `laws/`
//  precedent, a family emitted by a different tool. Its `manifest.json` rows
//  carry `kind: "style-observer"`, a kind that emitter does not author, so
//  Phase 1602's `preservedRows` carries them across a wholesale regen for the
//  same reason the teleport rows survive.
//
//      dotnet run --project src/Fuaran.UI.StyleObserver.Tests -- --emit-style-observer [<dir>]
//
//  `<dir>` is the corpus root and is optional, resolving through the one
//  resolver every corpus-reading suite uses (Phase 1647).
// ============================================================================

open System
open System.IO
open System.Text
open Fuaran.UI.StyleObserver
open Fuaran.UI.ThemeManifest

/// The corpus subdirectory this family owns.
let familyDirectory = "style-observer"

/// The `manifest.json` fixture `kind` every row of this family carries. It is
/// deliberately a kind the wholesale corpus emitter does not author.
let manifestKind = "style-observer"

let private inv = Globalization.CultureInfo.InvariantCulture

// ─── The authored cases ─────────────────────────────────────────────────────

/// One case of the family. The three arms are the three questions a host can be
/// asked about resolved style, and they need different evidence: a manifest-free
/// derivation takes a `StyleInput`, a manifest-aware per-node check takes an
/// already-derived observation plus a manifest, and a usage-budget check takes a
/// manifest plus a whole tree's worth of area.
type Case =
    /// Manifest-free: `Flags.derive` + `Flags.toObservation` over a `StyleInput`.
    | Observation of
        id: string *
        description: string *
        nodeId: string *
        input: Flags.StyleInput *
        expectKinds: string list
    /// Manifest-aware per node: `ManifestFlags.perNodeFlags`.
    | PerNode of
        id: string *
        description: string *
        manifestJson: string *
        observation: StyleObservation *
        expectKinds: string list
    /// Tree-level: `ManifestFlags.verifyUsageBudgets`.
    | UsageBudget of
        id: string *
        description: string *
        manifestJson: string *
        nodes: (StyleObservation * float) list *
        expectKinds: string list

[<AutoOpen>]
module private Cases =
    let white = Rgba.white
    let black = Rgba.black
    let transparent = Rgba.transparent
    let rgb r g b : Rgba = { R = r; G = g; B = b; A = 1.0 }

    let input fg layers font tone : Flags.StyleInput =
        { Foreground = fg
          BackgroundLayers = layers
          FontFamily = font
          EmittedTone = tone }

    /// An observation with no flags — the manifest-aware arms take the
    /// manifest-free result as their input, and only its tone, effective
    /// background and contrast ratio are consulted.
    let obs nodeId bg tone contrast : StyleObservation =
        { NodeId = nodeId
          Foreground = black
          EffectiveBackground = bg
          FontRole = FontRole.Unknown
          EmittedTone = tone
          ContrastRatio = contrast
          Flags = [] }

    /// A manifest binding nothing at all — every emitted tone falls through it.
    let unboundManifest =
        """{"meta":{"name":"corpus","version":"1"},"tokens":{},"roles":[],"invariants":[]}"""

    /// `Brand` bound to a palette colour that is NOT the surface the node
    /// rendered, so the tone resolves and the fill is off-palette.
    let offPaletteManifest =
        """{"meta":{"name":"corpus","version":"1"},"tokens":{"color":{"brand":{"$type":"color","$value":"#3b5bdb"}}},"roles":[{"role":{"tone":"Brand"},"token":"color.brand"}],"invariants":[]}"""

    /// `Brand` bound to the colour the node actually rendered (so it is
    /// on-palette), under a per-role contrast floor stricter than AA.
    let contrastFloorManifest =
        """{"meta":{"name":"corpus","version":"1"},"tokens":{"color":{"brand":{"$type":"color","$value":"#010203"}}},"roles":[{"role":{"tone":"Brand"},"token":"color.brand"}],"invariants":[{"kind":"ContrastFloor","role":"Brand","minRatio":7}]}"""

    /// A declared 10% ± 5% surface-area budget on one palette token.
    let budgetManifest =
        """{"meta":{"name":"corpus","version":"1"},"tokens":{"color":{"brand":{"$type":"color","$value":"#010203"}}},"roles":[],"invariants":[{"kind":"UsageBudget","token":"color.brand","targetPct":10,"tolerancePct":5}]}"""

/// The family, in emission order. Every case here is one that previously lived
/// as a literal in this tier's own suite or in a sibling host's.
let cases: Case list =
    [
      // ─── Manifest-free derivation + observation bytes ───────────────────
      Observation(
          "legible-black-on-white",
          "Opaque black text on the implicit white canvas — the baseline: no flags, contrast 21.",
          "node-1",
          input black [ white ] None None,
          []
      )
      Observation(
          "invisible-white-on-white",
          "Text the same colour as the surface behind it — the severe subset of the legibility axis.",
          "n",
          input white [ white ] None None,
          [ "InvisibleText" ]
      )
      Observation(
          "contrast-below-aa-grey-on-white",
          "Mid-grey on white: visible, but under the WCAG AA normal-text floor.",
          "n",
          input (rgb 150.0 150.0 150.0) [ white ] None None,
          [ "ContrastBelowAA" ]
      )
      Observation(
          "accent-indistinct-toned-tint",
          "A toned element whose own tint is all but the colour of its container.",
          "n",
          input black [ rgb 240.0 240.0 240.0; white ] None (Some "brand"),
          [ "AccentIndistinct" ]
      )
      Observation(
          "untoned-tint-is-exempt",
          "The same near-identical tint, untoned: the accent check fires only for toned elements.",
          "n",
          input black [ rgb 240.0 240.0 240.0; white ] None None,
          []
      )
      Observation(
          "invisible-and-accent-indistinct",
          "The two axes are independent: an invisible-text failure and an indistinct accent at once.",
          "n",
          input white [ white; white ] None (Some "brand"),
          [ "InvisibleText"; "AccentIndistinct" ]
      )
      Observation(
          "toned-observation-carries-its-tone",
          "`emittedTone` is a string on the wire when the element declared one, and null otherwise. A toned \
           element whose only background layer is the canvas itself has no ancestor surface to stand out \
           against, so the accent check fires here too — a fact no host's own tests asserted.",
          "n2",
          input black [ white ] None (Some "brand"),
          [ "AccentIndistinct" ]
      )
      Observation(
          "font-role-monospace",
          "Font-family classification — the monospace arm.",
          "n",
          input black [ white ] (Some "ui-monospace, Menlo") None,
          []
      )
      Observation(
          "font-role-sans-serif",
          "Font-family classification — the sans-serif arm.",
          "n",
          input black [ white ] (Some "Inter, sans-serif") None,
          []
      )
      Observation(
          "font-role-serif",
          "Font-family classification — the serif arm.",
          "n",
          input black [ white ] (Some "Georgia, serif") None,
          []
      )
      Observation(
          "font-role-unknown",
          "An unclassifiable family reports Unknown rather than guessing.",
          "n",
          input black [ white ] (Some "Wingdings") None,
          []
      )
      Observation(
          "transparent-layers-fall-through-to-white",
          "A fully transparent own layer composites to the opaque layer behind it.",
          "n",
          input black [ transparent; white ] None None,
          []
      )
      Observation(
          "first-opaque-layer-wins",
          "The composite walk stops at the first opaque layer; deeper ancestors cannot show through.",
          "n",
          input white [ rgb 10.0 20.0 30.0; rgb 99.0 99.0 99.0 ] None None,
          []
      )
      Observation(
          "no-layers-means-the-implicit-white-canvas",
          "An element and every ancestor transparent: the effective background is the page canvas.",
          "n",
          input black [] None None,
          []
      )

      // ─── Manifest-aware, per node ───────────────────────────────────────
      PerNode(
          "manifest-token-resolution-failed",
          "The element declared a tone the manifest binds to nothing — the emission fell through the host CSS.",
          unboundManifest,
          obs "n" (rgb 1.0 2.0 3.0) (Some "Brand") 21.0,
          [ "TokenResolutionFailed" ]
      )
      PerNode(
          "manifest-token-resolution-failed-escaped-slot",
          "The same failure with a slot name carrying a quote and a backslash — the encoder's escape path.",
          unboundManifest,
          obs "n" (rgb 1.0 2.0 3.0) (Some "he said \"hi\"\\here") 21.0,
          [ "TokenResolutionFailed" ]
      )
      PerNode(
          "manifest-off-palette-colour",
          "The tone resolved, but the surface it rendered is not a colour the palette declares.",
          offPaletteManifest,
          obs "n" (rgb 1.0 2.0 3.0) (Some "Brand") 21.0,
          [ "OffPaletteColour" ]
      )
      PerNode(
          "manifest-contrast-below-declared-floor",
          "An on-palette fill under a per-role floor stricter than the manifest-free AA default.",
          contrastFloorManifest,
          obs "n" (rgb 1.0 2.0 3.0) (Some "Brand") 5.0,
          [ "ContrastBelowDeclaredFloor" ]
      )
      PerNode(
          "manifest-untoned-node-is-exempt",
          "An untoned node is exempt from every manifest check — the Custom / domain-SVG exemption.",
          unboundManifest,
          obs "n" (rgb 1.0 2.0 3.0) None 1.0,
          []
      )

      // ─── Manifest-aware, tree level ─────────────────────────────────────
      UsageBudget(
          "budget-breached",
          "60px² of a 10%-budgeted token out of 100px² total — six times its declared share.",
          budgetManifest,
          [ obs "a" (rgb 1.0 2.0 3.0) None 21.0, 60.0
            obs "b" (rgb 9.0 9.0 9.0) None 21.0, 40.0 ],
          [ "UsageBudgetExceeded" ]
      )
      UsageBudget(
          "budget-within-tolerance",
          "12% observed against 10% ± 5% — inside the band, so no flag.",
          budgetManifest,
          [ obs "a" (rgb 1.0 2.0 3.0) None 21.0, 12.0
            obs "b" (rgb 9.0 9.0 9.0) None 21.0, 88.0 ],
          []
      )
      UsageBudget(
          "budget-at-the-tolerance-edge",
          "15% observed against 10% ± 5% — exactly on the boundary, which the comparison admits.",
          budgetManifest,
          [ obs "a" (rgb 1.0 2.0 3.0) None 21.0, 15.0
            obs "b" (rgb 9.0 9.0 9.0) None 21.0, 85.0 ],
          []
      ) ]

// ─── JSON writing (no dependency; the corpus is read by five languages) ─────

let private jstr (s: string) =
    let b = StringBuilder()
    b.Append('"') |> ignore

    for ch in s do
        match ch with
        | '"' -> b.Append("\\\"") |> ignore
        | '\\' -> b.Append("\\\\") |> ignore
        | '\n' -> b.Append("\\n") |> ignore
        | '\r' -> b.Append("\\r") |> ignore
        | '\t' -> b.Append("\\t") |> ignore
        | c when c < ' ' -> b.AppendFormat(inv, "\\u{0:x4}", int c) |> ignore
        | c -> b.Append(c) |> ignore

    b.Append('"').ToString()

/// Numbers are written in their shortest round-trippable invariant form. The
/// EXPECTATION fields carry the encoder's own `F2` rendering verbatim, so this
/// only ever renders inputs.
let private jnum (v: float) =
    if Double.IsInteger v then
        v.ToString("0", inv)
    else
        v.ToString("R", inv)

let private jrgba (c: Rgba) =
    sprintf "{\"r\":%s,\"g\":%s,\"b\":%s,\"a\":%s}" (jnum c.R) (jnum c.G) (jnum c.B) (jnum c.A)

let private jopt (v: string option) =
    match v with
    | Some s -> jstr s
    | None -> "null"

let private jarr (items: string list) =
    if List.isEmpty items then
        "[]"
    else
        "[\n    " + String.Join(",\n    ", items) + "\n  ]"

let private jobservation (o: StyleObservation) =
    String.concat
        ""
        [ "{"
          "\"nodeId\":" + jstr o.NodeId
          ",\"foreground\":" + jrgba o.Foreground
          ",\"effectiveBackground\":" + jrgba o.EffectiveBackground
          ",\"fontRole\":" + jstr (FontRole.kind o.FontRole)
          ",\"emittedTone\":" + jopt o.EmittedTone
          ",\"contrastRatio\":" + jnum o.ContrastRatio
          "}" ]

// ─── Deriving the expectations from the reference implementation ────────────

/// The id a case carries in `manifest.json` and in its filename.
let caseId (case: Case) =
    match case with
    | Observation(id, _, _, _, _) -> id
    | PerNode(id, _, _, _, _) -> id
    | UsageBudget(id, _, _, _, _) -> id

let private describe (case: Case) =
    match case with
    | Observation(_, d, _, _, _) -> d
    | PerNode(_, d, _, _, _) -> d
    | UsageBudget(_, d, _, _, _) -> d

let private decodeManifest (id: string) (json: string) =
    match Decode.decode json with
    | Ok m -> m
    | Error e -> failwithf "style-observer case '%s': its manifest does not decode in the reference host — %s" id e

/// The declared claim, checked against what the reference implementation
/// actually produced. Raises rather than writing: a family that records a
/// regression as its expectation certifies every host against the regression.
let private proveKinds (id: string) (expected: string list) (flags: StyleFlag list) =
    let actual = flags |> List.map StyleFlag.kind

    if actual <> expected then
        failwithf
            "style-observer case '%s' claims flags %A but the reference derivation produced %A. Either the case is \
             mis-authored or the implementation has regressed — the emitter will not publish the disagreement."
            id
            expected
            actual

    flags

/// Render one case as its fixture document. Every expectation in it is computed
/// here, by the reference implementation, and checked against the case's claim.
let render (case: Case) : string =
    let opts = StyleObserverOptions.defaults

    let body =
        match case with
        | Observation(id, _, nodeId, input, expect) ->
            let flags = Flags.derive opts input |> proveKinds id expect
            let observation = Flags.toObservation opts nodeId input

            [ "  \"tier\": \"observation\","
              "  \"options\": {\"contrastAaThreshold\": "
              + jnum opts.ContrastAAThreshold
              + ", \"invisibleTextThreshold\": "
              + jnum opts.InvisibleTextThreshold
              + ", \"accentIndistinctThreshold\": "
              + jnum opts.AccentIndistinctThreshold
              + "},"
              "  \"nodeId\": " + jstr nodeId + ","
              "  \"input\": {"
              "    \"foreground\": " + jrgba input.Foreground + ","
              "    \"backgroundLayers\": ["
              + String.Join(", ", input.BackgroundLayers |> List.map jrgba)
              + "],"
              "    \"fontFamily\": " + jopt input.FontFamily + ","
              "    \"emittedTone\": " + jopt input.EmittedTone
              "  },"
              "  \"expectedFlags\": "
              + jarr (flags |> List.map (StyleFlag.encode >> jstr))
              + ","
              "  \"expectedObservation\": " + jstr (StyleObservation.encode observation) ]

        | PerNode(id, _, manifestJson, observation, expect) ->
            let manifest = decodeManifest id manifestJson
            let flags = ManifestFlags.perNodeFlags manifest observation |> proveKinds id expect

            [ "  \"tier\": \"per-node-manifest\","
              "  \"manifest\": " + jstr manifestJson + ","
              "  \"observation\": " + jobservation observation + ","
              "  \"expectedManifestFlags\": "
              + jarr (flags |> List.map (StyleFlag.encode >> jstr)) ]

        | UsageBudget(id, _, manifestJson, nodes, expect) ->
            let manifest = decodeManifest id manifestJson
            let flags = ManifestFlags.verifyUsageBudgets manifest nodes |> proveKinds id expect

            let areas =
                nodes
                |> List.map (fun (o, area) ->
                    "    {\"observation\": " + jobservation o + ", \"area\": " + jnum area + "}")

            [ "  \"tier\": \"usage-budget\","
              "  \"manifest\": " + jstr manifestJson + ","
              "  \"nodeAreas\": [\n" + String.Join(",\n", areas) + "\n  ],"
              "  \"expectedBudgetFlags\": "
              + jarr (flags |> List.map (StyleFlag.encode >> jstr)) ]

    let header =
        [ "{"
          "  \"id\": " + jstr (caseId case) + ","
          "  \"description\": " + jstr (describe case) + "," ]

    String.Join("\n", header @ body) + "\n}\n"

// ─── Emission ───────────────────────────────────────────────────────────────

/// The `decoder` every row of this family names. The corpus loader requires the
/// property on EVERY row, so it is not optional even for a family whose vectors
/// are not decoded documents; what it names here is the entry point a host
/// reaches for, which is the same thing it names for `teleport` or
/// `contract-card`.
let manifestDecoder = "style-observer"

/// The `manifest.json` row for one case, as the corpus spells its rows.
let manifestRow (case: Case) =
    sprintf
        "{ \"id\": \"style-observer-%s\", \"kind\": \"%s\", \"decoder\": \"%s\", \"inputFile\": \"%s/%s.json\" }"
        (caseId case)
        manifestKind
        manifestDecoder
        familyDirectory
        (caseId case)

/// Write the family into `corpusRoot/style-observer/`. LF endings, one file per
/// case, and every stale file in the directory removed — the family is the
/// authored list, not whatever happens to be on disk.
let write (corpusRoot: string) : string list =
    let dir = Path.Combine(corpusRoot, familyDirectory)
    // Render EVERY case first: `render` proves each vector, and a proof that
    // fails must not leave a half-written family behind.
    let rendered = cases |> List.map (fun c -> caseId c, render c)

    Directory.CreateDirectory dir |> ignore

    let keep = rendered |> List.map (fun (id, _) -> id + ".json") |> Set.ofList

    for existing in Directory.GetFiles(dir, "*.json") do
        let name = Path.GetFileName existing |> Option.ofObj |> Option.defaultValue ""

        if not (keep.Contains name) then
            File.Delete existing

    for id, text in rendered do
        File.WriteAllText(Path.Combine(dir, id + ".json"), text.Replace("\r\n", "\n"), UTF8Encoding false)

    rendered |> List.map fst
