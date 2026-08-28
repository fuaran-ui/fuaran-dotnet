module Fuaran.UI.Tests.ChartProvenanceTests

#nowarn "3261" // `box` on a scalar cell is legitimately nullable here, as in ChartLoweringTests.

// ============================================================================
//  Phase 643 — chart provenance stamp + self-describing SVG.
//
//  Two legs, pinned separately because they fail separately:
//
//    * the STAMP is a pure derivation — same spec + same rows + same lowering
//      version ⇒ the same three strings, on any machine and either runtime. The
//      digests below are pinned as LITERALS rather than recomputed by the test,
//      because a test that recomputes what it asserts is a tautology: it would
//      pass unchanged if the derivation silently swapped its canonicalisation.
//      A literal is what makes a change to the bytes a failure rather than a
//      no-op. Each is paired with a recompute-from-first-principles assertion so
//      a legitimate change tells you what to paste.
//
//    * the ROUND TRIP is spec → SVG → spec, byte-identical. Every such lock
//      here is accompanied by a NON-VACUITY test that perturbs the artefact and
//      observes the lock go red — the assertion "these two strings are equal" is
//      worth nothing until you have seen the case where they are not.
// ============================================================================

open System
open System.IO
open System.Text.RegularExpressions
open Expecto
open Fuaran.UI.Types
open Fuaran.UI
open Fuaran.UI.Renderer
open Fuaran.UI.Charts

// ─── Fixtures ────────────────────────────────────────────────────────────────

/// A row feed with a hostile string in it — the chart lowering feeds series and
/// category strings straight into the emitted markup, and the provenance
/// document carries them a second time, so the escaping leg needs a fixture that
/// actually exercises it rather than a tidy one.
let private rows: Fuaran.Core.Row list =
    [ Map.ofList [ "quarter", box "Q1 <b>"; "revenue", box 120.0 ]
      Map.ofList [ "quarter", box "Q2 & \"co\""; "revenue", box 90.5 ] ]

let private baseSpec: ChartSpec<obj> =
    { Defaults.chart with
        Kind = ChartKind.Bar
        Source = Binding.Static(Some(rows :> Fuaran.Core.Row seq))
        XField = "quarter"
        YFields = [ "revenue" ]
        Title = Some(TextSource.Literal "Revenue & \"growth\" <q> ]]> '") }

/// A spec whose row source is NOT carried inline — the leg whose data cannot be
/// recovered from the spec alone, and which therefore needs the embedded feed.
let private boundSpec: ChartSpec<obj> =
    { baseSpec with
        Source = Binding.State("feed", None) }

let private textOf (t: TextSource) : string =
    match t with
    | TextSource.Literal s -> s
    | _ -> "?"

let private emit (provenance: ChartProvenance) (spec: ChartSpec<obj>) (feed: Fuaran.Core.Row list) : string =
    renderSvgWith provenance BindingResolver.empty textOf spec feed

/// The metadata element's body as it sits in the markup — ESCAPED, before any
/// recovery. Several assertions below are about these bytes specifically.
let private metadataRegion (svg: string) : string option =
    let openTag =
        "<metadata "
        + DrawingSvg.metadataMarkerAttribute
        + "=\""
        + DrawingSvg.metadataDocumentVersion
        + "\">"

    let i = svg.IndexOf(openTag, StringComparison.Ordinal)

    if i < 0 then
        None
    else
        let bodyStart = i + openTag.Length
        let j = svg.IndexOf("</metadata>", bodyStart, StringComparison.Ordinal)

        if j < 0 then
            None
        else
            Some(svg.Substring(bodyStart, j - bodyStart))

// ─── The pinned digests ──────────────────────────────────────────────────────
//
// Regenerating these is the whole point of the paired recompute assertions: a
// change to the canonical encoding, the hash tag lines, or the wire shape of
// `ChartSpec` fails here with the value to paste. It is NOT a licence to paste —
// a moved `specHash` means every previously-emitted artefact's stamp no longer
// re-derives, which is a compatibility event, not a test update.

[<Literal>]
let private pinnedSpecHash =
    "sha256:ef42e203d7e34705f0fe50ed6e1c7321d1779e037a10b9f28920434b1dd48839"

[<Literal>]
let private pinnedDataFingerprint =
    "sha256:225e2a7ce30c35a5883408941114523c6a67fa378f5efeaac51448ce81bd81df"

// ─── Renderer-source parity (the "threads identically" leg) ──────────────────

let private rendererSourceDir =
    Path.Combine(AppContext.BaseDirectory, "renderer-sources")

let private rendererSource (tier: string) : string =
    let path = Path.Combine(rendererSourceDir, tier, "Render.fs")

    if not (File.Exists path) then
        failwithf
            "renderer source not found at %s — the Fuaran.UI.Tests project copies the renderer sources into its output; check the Content items."
            path

    File.ReadAllText path

let private occurrences (needle: string) (haystack: string) : int =
    Regex.Matches(haystack, Regex.Escape needle).Count

[<Tests>]
let tests =
    testList
        "chart provenance (Phase 643)"
        [

          // ══ Stamp derivation ═══════════════════════════════════════════════

          test "specWireJson is the canonical wire encoding of the ChartSpec, not a restatement of it" {
              // The acceptance clause's "an independent re-encoding": this is
              // the encode path the WIRE takes, reached here without going
              // through the provenance module at all.
              let independent =
                  Fuaran.Core.Canon.render (Fuaran.UI.Generated.encodeNodeKindJson (NodeKind.Chart baseSpec))

              Expect.equal (specWireJson baseSpec) independent "the stamped spec bytes are the wire's own bytes"
          }

          test "specHash is sha256 over those bytes, and matches its pin" {
              let recomputed = "sha256:" + Fuaran.UI.Hashing.sha256Hex (specWireJson baseSpec)

              Expect.equal (specHashOf baseSpec) recomputed "the hash is taken over the canonical spec JSON"
              Expect.equal (specHashOf baseSpec) pinnedSpecHash "the spec hash has not moved"
          }

          test "the stamp is deterministic" {
              Expect.equal (stampOf baseSpec (Some rows)) (stampOf baseSpec (Some rows)) "same inputs, same stamp"
          }

          test "a changed wire field moves the spec hash" {
              let altered =
                  { baseSpec with
                      YFields = [ "revenue"; "cost" ] }

              Expect.notEqual
                  (specHashOf altered)
                  (specHashOf baseSpec)
                  "a spec that differs on the wire must not stamp alike"
          }

          test "a no-typed-feed source stamps `opaque`, honestly" {
              Expect.equal (dataFingerprintOf None) "opaque" "the absent-feed answer is a value, not an omission"

              Expect.equal
                  (stampOf boundSpec None).DataFingerprint
                  opaqueDataFingerprint
                  "the stamp carries it through unchanged"
          }

          test "the data fingerprint matches its pin, and covers the encoded feed" {
              let recomputed =
                  [ "fuaran-chart-data:v1"
                    "schema=" + String.Join(",", schemaColumns rows)
                    "rows=" + dataWireJson rows ]
                  |> String.concat "\n"
                  |> Fuaran.UI.Hashing.sha256Hex
                  |> fun h -> "sha256:" + h

              Expect.equal (dataFingerprintOf (Some rows)) recomputed "the digest is over the tagged schema+content"

              Expect.equal (dataFingerprintOf (Some rows)) pinnedDataFingerprint "the data fingerprint has not moved"
          }

          // The two-leg justification, made falsifiable. A content-only hash
          // would pass the second of these and FAIL the first — which is
          // precisely why the schema line exists.
          test "a renamed column moves the fingerprint even when every value is unchanged" {
              let renamed =
                  rows
                  |> List.map (fun r ->
                      r
                      |> Map.toList
                      |> List.map (fun (k, v) -> (if k = "quarter" then "period" else k), v)
                      |> Map.ofList)

              Expect.notEqual
                  (dataFingerprintOf (Some renamed))
                  (dataFingerprintOf (Some rows))
                  "the schema leg is load-bearing"
          }

          test "a changed value moves the fingerprint" {
              let edited =
                  rows
                  |> List.mapi (fun i r -> if i = 0 then Map.add "revenue" (box 121.0) r else r)

              Expect.notEqual
                  (dataFingerprintOf (Some edited))
                  (dataFingerprintOf (Some rows))
                  "the content leg is load-bearing"
          }

          test "row ORDER is data — reordering the feed moves the fingerprint" {
              Expect.notEqual
                  (dataFingerprintOf (Some(List.rev rows)))
                  (dataFingerprintOf (Some rows))
                  "a bar chart's category order is part of the picture, so it is part of the fingerprint"
          }

          test "the lowering version rides the stamp" {
              Expect.equal (stampOf baseSpec (Some rows)).LoweringVersion loweringVersion "verbatim"
          }

          // ══ Emission ═══════════════════════════════════════════════════════

          test "Off emits the pre-643 bytes exactly" {
              let withOption = emit ChartProvenance.Off baseSpec rows
              let pre643 = DrawingSvg.render BindingResolver.empty textOf (lower baseSpec rows)

              Expect.equal withOption pre643 "an un-opted-in render is byte-identical to the drawing emitter's output"
              Expect.isFalse (withOption.Contains "<metadata") "and carries no metadata element at all"
          }

          test "SpecOnly embeds the spec and the stamp, and no data" {
              let svg = emit ChartProvenance.SpecOnly baseSpec rows

              Expect.isSome (metadataRegion svg) "the metadata element is present"

              match tryRecover svg with
              | Error e -> failtestf "recovery failed: %s" e
              | Ok recovered ->
                  Expect.equal recovered.SpecJson (specWireJson baseSpec) "the spec is recoverable"
                  Expect.isNone recovered.DataJson "SpecOnly carries no row feed"
                  Expect.equal recovered.Stamp (stampOf baseSpec (Some rows)) "the stamp is the derived one"
          }

          test "SpecAndData additionally embeds the resolved feed" {
              let svg = emit ChartProvenance.SpecAndData boundSpec rows

              match tryRecover svg with
              | Error e -> failtestf "recovery failed: %s" e
              | Ok recovered ->
                  Expect.equal recovered.SpecJson (specWireJson boundSpec) "the spec is recoverable"
                  Expect.equal recovered.DataJson (Some(dataWireJson rows)) "the row feed is recoverable"
          }

          // ══ Escaping ═══════════════════════════════════════════════════════

          test "the embedded document is XML-escaped — and the fixture proves the escape did work" {
              let svg = emit ChartProvenance.SpecAndData baseSpec rows
              let raw = provenanceDocument ChartProvenance.SpecAndData baseSpec rows |> Option.get

              // NON-VACUITY: the unescaped document really does contain the
              // characters that would break the markup. Without this the
              // assertion below is satisfied by any harmless input.
              Expect.isTrue (raw.Contains "<") "the fixture's document contains a raw '<'"
              Expect.isTrue (raw.Contains "&") "the fixture's document contains a raw '&'"
              Expect.isTrue (raw.Contains "]]>") "the fixture's document contains the CDATA terminator"

              match metadataRegion svg with
              | None -> failtest "no metadata element"
              | Some region ->
                  Expect.isFalse (region.Contains "<") "no raw '<' survives into the markup"
                  Expect.isFalse (region.Contains ">") "no raw '>' survives into the markup"
                  Expect.isTrue (region.Contains "&lt;") "it is entity-escaped rather than dropped"
                  Expect.equal (DrawingSvg.unescape region) raw "and unescapes back to the original document"
          }

          test "unescape is the exact inverse of escape, including the &amp;-first case" {
              // The case a replace-per-entity implementation gets wrong: a
              // LITERAL `&lt;` in the source must not decode to `<`.
              for original in [ "&lt;"; "a & b"; "<tag>"; "\"q\" 'q'"; "&amp;gt;"; "]]>"; "" ] do
                  Expect.equal (DrawingSvg.unescape (DrawingSvg.escape original)) original ("round-trips: " + original)
          }

          // ══ Round trip ═════════════════════════════════════════════════════

          test "spec → SVG → spec is byte-identical" {
              let svg = emit ChartProvenance.SpecOnly baseSpec rows

              match tryRecover svg with
              | Error e -> failtestf "recovery failed: %s" e
              | Ok recovered ->
                  Expect.equal
                      recovered.SpecJson
                      (specWireJson baseSpec)
                      "the recovered spec JSON is byte-for-byte the encoding that produced the artefact"
          }

          // The probe. Without this, "equal" above is a claim no one has seen
          // fail — and a recovery helper that returned its own re-encode of the
          // fixture would satisfy it forever.
          test "the round-trip lock is NOT vacuous — a perturbed artefact fails it" {
              let svg = emit ChartProvenance.SpecOnly baseSpec rows

              // Perturb the EMBEDDED document, leaving the markup well-formed:
              // rename the plotted x field inside the metadata only.
              let perturbed =
                  svg.Replace("&quot;xField&quot;:&quot;quarter&quot;", "&quot;xField&quot;:&quot;period&quot;")

              Expect.notEqual perturbed svg "the perturbation actually changed the artefact"

              match tryRecover perturbed with
              | Error e -> failtestf "the perturbed artefact should still parse, but: %s" e
              | Ok recovered ->
                  Expect.notEqual
                      recovered.SpecJson
                      (specWireJson baseSpec)
                      "a changed embedded spec is observed as changed"

                  Expect.notEqual
                      ("sha256:" + Fuaran.UI.Hashing.sha256Hex recovered.SpecJson)
                      recovered.Stamp.SpecHash
                      "and the carried stamp no longer re-derives from the carried spec"
          }

          test "recovery names its failures rather than throwing" {
              let plain = DrawingSvg.render BindingResolver.empty textOf (lower baseSpec rows)

              match tryRecover plain with
              | Ok _ -> failtest "a plain drawing must not recover"
              | Error e -> Expect.stringContains e "no provenance metadata" "the absence is named"

              let svg = emit ChartProvenance.SpecOnly baseSpec rows

              let truncated =
                  svg.Substring(0, svg.IndexOf("</metadata>", StringComparison.Ordinal))

              match tryRecover truncated with
              | Ok _ -> failtest "a truncated metadata element must not yield a partial document"
              | Error e -> Expect.stringContains e "no provenance metadata" "a prefix is not a document"

              let foreign =
                  svg.Replace(DrawingSvg.metadataMarkerAttribute, "data-someone-elses-metadata")

              match tryRecover foreign with
              | Ok _ -> failtest "a foreign metadata element must not be read as ours"
              | Error _ -> ()
          }

          // ══ Size guard ═════════════════════════════════════════════════════

          test "an embedded feed rides the output budget — and spec-only is the documented retry" {
              // `boundSpec`'s source is a State binding, so the SPEC carries no
              // rows and the two scopes differ by the whole feed — which is the
              // case the flag exists for.
              let bigFeed =
                  [ for i in 1..400 ->
                        Map.ofList
                            [ "quarter", box ("category-with-a-long-name-" + string i)
                              "revenue", box (float i) ] ]

              let drawing = lower boundSpec bigFeed
              let specOnlyDoc = provenanceDocument ChartProvenance.SpecOnly boundSpec bigFeed
              let bothDoc = provenanceDocument ChartProvenance.SpecAndData boundSpec bigFeed

              let render doc =
                  DrawingSvg.renderWithMetadata doc BindingResolver.empty textOf drawing

              // The ceiling is derived from the two EMITTED lengths rather than
              // from the documents' own: the payload is entity-escaped on the
              // way in (a JSON document is mostly quotes, and `"` costs six
              // characters), so reasoning about the raw document lengths
              // understates the emission by a multiple. That mis-estimate is
              // exactly what this test caught when it was written the other way.
              let specOnlyLen = (render specOnlyDoc).Length
              let bothLen = (render bothDoc).Length

              Expect.isGreaterThan bothLen specOnlyLen "the fixture's feed genuinely enlarges the emission"

              let limit = (specOnlyLen + bothLen) / 2

              Expect.isGreaterThan limit specOnlyLen "the ceiling sits strictly above spec-only"
              Expect.isLessThan limit bothLen "and strictly below spec+data"

              match DrawingSvg.tryRenderWithMetadataAndLimit limit bothDoc BindingResolver.empty textOf drawing with
              | Ok _ -> failtest "spec+data should have exceeded the ceiling for this fixture"
              | Error(DrawingSvg.OutputTooLarge l) -> Expect.equal l limit "the breached ceiling is reported"

              match DrawingSvg.tryRenderWithMetadataAndLimit limit specOnlyDoc BindingResolver.empty textOf drawing with
              | Error _ -> failtest "spec-only must fit where spec+data did not — that IS the guard"
              | Ok svg -> Expect.isTrue (svg.Contains "<metadata") "and it still carries the stamp"
          }

          test "a refused lowering carries no stamp" {
              // The stamp describes the picture the lowering drew. A refusal
              // drawing is not that picture.
              let tooManySeries =
                  { baseSpec with
                      YFields = [ for i in 1..64 -> "y" + string i ] }

              let svg = emit ChartProvenance.SpecAndData tooManySeries rows

              Expect.isFalse (svg.Contains "<metadata") "a refusal is unstamped"
              Expect.isTrue (svg.Contains "Chart not rendered") "and still says why it is empty"
          }

          // ══ SSR / CSR parity ═══════════════════════════════════════════════

          test "both renderer tiers reach a chart's SVG through the SAME entry point" {
              // The strongest parity statement expressible on .NET: the Feliz
              // client renderer produces an opaque `ReactElement` with no string
              // projection, so a byte diff cannot be written here. What CAN be
              // pinned is that neither tier has its own emission path — and
              // that is what makes the provenance option thread identically by
              // construction rather than by two call sites kept in step.
              for tier in [ "client"; "server" ] do
                  let source = rendererSource tier

                  Expect.equal
                      (occurrences "Fuaran.UI.Charts.renderSvg ctx.Sources (renderText ctx) spec" source)
                      1
                      (tier + " renders a chart through Charts.renderSvg, exactly once")

                  Expect.equal
                      (occurrences "Fuaran.UI.Charts.lower spec" source)
                      0
                      (tier + " no longer lowers-then-emits on its own, which would bypass the option")
          }

          test "the installed scope is what the renderer entry point reads" {
              try
                  clearChartProvenance ()

                  Expect.equal (currentChartProvenance ()) ChartProvenance.Off "it ships Off"

                  Expect.equal
                      (renderSvg BindingResolver.empty textOf baseSpec rows)
                      (emit ChartProvenance.Off baseSpec rows)
                      "Off by default"

                  installChartProvenance ChartProvenance.SpecAndData

                  Expect.equal
                      (renderSvg BindingResolver.empty textOf baseSpec rows)
                      (emit ChartProvenance.SpecAndData baseSpec rows)
                      "an installed scope is honoured"
              finally
                  // Process-global, like the Custom hash floor — a test that
                  // leaves it installed changes what every later test renders.
                  clearChartProvenance ()
          } ]
