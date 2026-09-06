module Fuaran.UI.Tests.TrustedTypesTests

#nowarn "3261" // `DirectoryInfo.Parent` is legitimately nullable here (the climb's terminator).

// ============================================================================
//  Phase 1546 - the `fuaran-renderer` Trusted Types policy.
//
//  Three separable claims, pinned separately because they fail separately.
//
//   1. EVERY raw-HTML sink in the client renderer mints through the policy. The
//      oracle is the source itself rather than a list kept beside it: a sink
//      added tomorrow is a sink this suite reads, so the seam test goes red on
//      the commit that adds it rather than on whatever later change happens to
//      notice. `SANITIZATION.md`'s seam table is the prose form of the same
//      inventory, and the doc-parity test below binds the two.
//
//   2. The policy's floor is INVARIANT over the renderer's own payloads. The
//      floor is `Sanitize.sanitizeMarkdownHtml`, written for markdown and now
//      running over drawing SVG, chart SVG, MathML and the theme stylesheet as
//      well, on every host whether or not the browser enforces Trusted Types.
//      That uniformity is what keeps the server renderer's SSR bytes and the
//      client renderer's hydration bytes identical, and it only holds while the
//      floor finds nothing to remove in those payloads. Each case here builds
//      its payload from the REAL emitter, so a payload that grows a shape the
//      sweep would rewrite fails here rather than presenting later as a React
//      hydration mismatch or, in the chart provenance case, as a figure whose
//      embedded document no longer parses.
//
//   3. The floor still refuses what it always refused. Claim 2 is only worth
//      having if the sweep is still doing something, so every invariance
//      assertion is paired with a payload that must NOT survive intact.
//
//  One live defect was found by claim 2 and fixed in the same change: the sweep
//  matched a dangerous element name as a bare prefix, so `<metadata>` read as
//  `<meta>` and lost its opening tag, leaving the drawing builder's provenance
//  document loose in the figure. The boundary cases at the foot of this module
//  are that fix's go-red proof.
// ============================================================================

open System
open System.IO
open Expecto
open Fuaran.UI.Types
open Fuaran.UI
open Fuaran.UI.Renderer
open Fuaran.UI.Charts

// ─── Locating the sources ────────────────────────────────────────────────────

/// Climb to the repo root, identified by two files rather than one so a nested
/// checkout cannot be mistaken for it. Same shape as `SpecConstructionTests`.
let private repoRoot: string =
    let rec climb (dir: DirectoryInfo) =
        if isNull dir then
            failwith "TrustedTypesTests: could not locate the repo root from the test binary's directory."
        elif
            File.Exists(Path.Combine(dir.FullName, "Fuaran.sln"))
            && File.Exists(Path.Combine(dir.FullName, "SANITIZATION.md"))
        then
            dir.FullName
        else
            climb dir.Parent

    climb (DirectoryInfo(AppContext.BaseDirectory))

let private clientRendererDir = Path.Combine(repoRoot, "src", "Fuaran.UI.Renderer")

// ─── The seam scan ───────────────────────────────────────────────────────────

/// The two ways a string reaches a raw-HTML DOM sink from this renderer: React's
/// prop, and a direct `innerHTML` assignment. Both are Trusted Types sinks, and
/// both must therefore carry a value the policy minted.
let private sinkTokens = [ "prop.dangerouslySetInnerHTML"; "innerHTML <-" ]

/// The call every sink's argument must open with.
[<Literal>]
let private mintCall = "TrustedTypes.html"

type private Sink =
    {
        File: string
        Line: int
        Token: string
        /// The argument text as the scan sees it, for the failure message.
        Argument: string
    }

/// Is the occurrence at `index` inside a `//` comment on its own line? Comment
/// mentions of a sink are frequent in this renderer and are not sinks.
let private inComment (text: string) (index: int) =
    let lineStart =
        match text.LastIndexOf('\n', max 0 (index - 1)) with
        | -1 -> 0
        | i -> i + 1

    let before = text.Substring(lineStart, index - lineStart)
    before.Contains "//"

/// The text immediately after a sink token, with the whitespace and opening
/// parentheses that may legitimately separate the token from its argument
/// removed. What remains must begin with the mint call.
let private argumentAfter (text: string) (start: int) =
    let mutable i = start

    while i < text.Length
          && (text[i] = ' '
              || text[i] = '\t'
              || text[i] = '\r'
              || text[i] = '\n'
              || text[i] = '(') do
        i <- i + 1

    text.Substring(i, min 60 (text.Length - i))

let private scanFile (path: string) : Sink list =
    let text = File.ReadAllText path
    let name = Path.GetFileName path

    [ for token in sinkTokens do
          let mutable from = 0

          let mutable go = true

          while go do
              match text.IndexOf(token, from, StringComparison.Ordinal) with
              | -1 -> go <- false
              | i ->
                  from <- i + token.Length

                  if not (inComment text i) then
                      let line = 1 + (text.Substring(0, i) |> Seq.filter ((=) '\n') |> Seq.length)

                      yield
                          { File = name
                            Line = line
                            Token = token
                            Argument = argumentAfter text (i + token.Length) } ]

let private clientSinks: Sink list =
    Directory.EnumerateFiles(clientRendererDir, "*.fs", SearchOption.AllDirectories)
    |> Seq.sort
    |> Seq.collect scanFile
    |> List.ofSeq

// ─── Payload fixtures, built from the real emitters ──────────────────────────

let private textOf (t: TextSource) : string =
    match t with
    | TextSource.Literal s -> s
    | _ -> "?"

/// Hostile strings in every author-supplied slot: the escaping the invariance
/// claim rests on is only exercised by a fixture that actually needs it.
let private rows: Fuaran.Core.Row list =
    [ Map.ofList [ "quarter", box "Q1 <script>alert(1)</script>"; "revenue", box 120.0 ]
      Map.ofList [ "quarter", box "Q2 & \"co\" <meta http-equiv=refresh>"; "revenue", box 90.5 ] ]

let private chartSpec: ChartSpec<obj> =
    { Defaults.chart with
        Kind = ChartKind.Bar
        Source = Binding.Static(Some(rows :> Fuaran.Core.Row seq))
        XField = "quarter"
        YFields = [ "revenue" ]
        Title = Some(TextSource.Literal "Revenue & \"growth\" <link rel=stylesheet> '") }

/// A chart SVG carrying the Phase 643 provenance document, which is the payload
/// the pre-1546 sweep corrupted: it is emitted inside an SVG `<metadata>`
/// element, and the sweep read that element's name as `<meta>`.
let private chartSvgWithProvenance =
    renderSvgWith ChartProvenance.SpecAndData BindingResolver.empty textOf chartSpec rows

let private chartSvgPlain =
    renderSvgWith ChartProvenance.Off BindingResolver.empty textOf chartSpec rows

let private markdownHtml =
    Markdown.toHtml
        "# Heading <script>alert(1)</script>\n\nA [link](https://example.com) and `code` and one & another.\n\n- one\n- two\n"

let private mathMl =
    match MathMl.translate "x^2 + y_1" MathDisplay.Block with
    | Some markup -> markup
    | None -> failwith "TrustedTypesTests: the MathML fixture must translate; the invariance claim needs real output."

let private themeCss = Theme.toCss Defaults.theme

// ─── Tests ───────────────────────────────────────────────────────────────────

[<Tests>]
let tests =
    testList
        "Phase 1546 - Trusted Types policy"
        [ testList
              "every raw-HTML sink mints through the policy"
              [ test "the scan finds the sinks it is meant to govern" {
                    // Non-vacuity. A scan that matched nothing would pass every
                    // assertion below while proving nothing at all, and this
                    // renderer's sinks are spread over two files.
                    Expect.isGreaterThanOrEqual
                        clientSinks.Length
                        7
                        "the client renderer's raw-HTML sink count; a sudden drop means the scan stopped matching, not that the sinks went away"

                    let files = clientSinks |> List.map _.File |> List.distinct |> List.sort
                    Expect.contains files "Render.fs" "Render.fs holds raw-HTML sinks"
                    Expect.contains files "MathEnhance.fs" "MathEnhance.fs holds the direct innerHTML sink"
                }

                test "each sink's argument opens with the mint call" {
                    let unrouted =
                        clientSinks
                        |> List.filter (fun s -> not (s.Argument.StartsWith(mintCall, StringComparison.Ordinal)))

                    let describe (s: Sink) =
                        sprintf "%s:%d  %s <- %s" s.File s.Line s.Token s.Argument

                    Expect.equal
                        (unrouted |> List.map describe)
                        []
                        (sprintf
                            "every raw-HTML sink in the client renderer must take its value from `%s`, so a host requiring Trusted Types is served a value its policy minted. Add the sink's payload to SANITIZATION.md's seam table in the same change."
                            mintCall)
                }

                test "the contract names the policy and the directive a host pins it in" {
                    let contract = File.ReadAllText(Path.Combine(repoRoot, "SANITIZATION.md"))

                    Expect.stringContains
                        contract
                        TrustedTypes.policyName
                        "SANITIZATION.md must name the policy, since that string is what a host writes into its CSP"

                    Expect.stringContains
                        contract
                        "require-trusted-types-for"
                        "SANITIZATION.md must name the directive the policy is there to satisfy"
                } ]

          testList
              "the floor is invariant over the renderer's own payloads"
              [ let invariant name payload =
                    test name {
                        Expect.equal
                            (TrustedTypes.createHtml payload)
                            payload
                            "the policy's floor must not rewrite a payload the renderer emitted: the client applies it and the SSR path does not, so any rewrite here is a hydration mismatch there"
                    }

                yield invariant "rendered markdown" markdownHtml
                yield invariant "chart SVG" chartSvgPlain
                yield invariant "chart SVG carrying the provenance document" chartSvgWithProvenance
                yield invariant "MathML" mathMl
                yield invariant "the theme stylesheet" themeCss

                yield
                    test "the provenance fixture really does carry a metadata element" {
                        // Non-vacuity for the case above: without this, a change
                        // that stopped emitting provenance would make the
                        // invariance assertion pass for the wrong reason.
                        Expect.stringContains
                            chartSvgWithProvenance
                            ("<metadata " + DrawingSvg.metadataMarkerAttribute)
                            "the fixture must exercise the element the pre-1546 sweep corrupted"
                    }

                yield
                    test "the markdown fixture really does exercise the sweep upstream" {
                        Expect.isFalse
                            (markdownHtml.Contains "<script")
                            "the GFM renderer escapes raw HTML by construction, which is the precondition the floor rests on"
                    } ]

          testList
              "the floor still refuses what it always refused"
              [ test "a script element does not survive" {
                    let clean = TrustedTypes.createHtml "<p>ok</p><script>alert(1)</script>"
                    Expect.isFalse (clean.Contains "<script") "no live open script tag survives"
                    Expect.stringContains clean "<p>ok</p>" "the surrounding markup is left alone"
                }

                test "a meta refresh does not survive" {
                    let clean =
                        TrustedTypes.createHtml "<meta http-equiv=\"refresh\" content=\"0;url=//evil\"><p>ok</p>"

                    Expect.isFalse (clean.Contains "<meta ") "the meta element is stripped"
                    Expect.stringContains clean "<p>ok</p>" "the surrounding markup is left alone"
                }

                test "an inline event handler does not survive" {
                    let clean = TrustedTypes.createHtml "<a href=\"#\" onclick=\"alert(1)\">x</a>"
                    Expect.isFalse (clean.Contains "onclick") "the handler attribute is stripped"
                }

                test "a javascript: URL does not survive" {
                    let clean = TrustedTypes.createHtml "<a href=\"JaVaScRiPt:alert(1)\">x</a>"
                    Expect.isFalse (clean.ToLowerInvariant().Contains "javascript:") "the scheme is neutralised"
                    Expect.stringContains clean "about:blank" "and replaced with an inert one"
                } ]

          testList
              "a dangerous element name matches only at a tag-name boundary"
              [ test "an SVG metadata element survives intact" {
                    let svg =
                        "<svg><metadata data-fuaran-provenance=\"v1\">{\"a\":1}</metadata><rect /></svg>"

                    Expect.equal
                        (TrustedTypes.createHtml svg)
                        svg
                        "`<metadata>` is not `<meta>`: before Phase 1546 the sweep stripped its opening tag and left the document's text loose in the figure"
                }

                test "an SVG linearGradient element survives intact" {
                    let svg =
                        "<svg><defs><linearGradient id=\"g\"><stop /></linearGradient></defs></svg>"

                    Expect.equal (TrustedTypes.createHtml svg) svg "`<linearGradient>` is not `<link>`"
                }

                test "the boundary rule admits no real dangerous element" {
                    // The go-red half: each spelling below IS the element, so
                    // each must still be refused. A boundary rule that let any
                    // of these through would be a weakening rather than a
                    // narrowing.
                    for payload in
                        [ "<meta http-equiv=refresh>"
                          "<meta/>"
                          "<link rel=stylesheet href=evil>"
                          "<script>x</script>"
                          "<script\n>x</script>"
                          "<iframe src=evil></iframe>" ] do
                        let clean = TrustedTypes.createHtml payload

                        Expect.isFalse
                            (clean.Contains "<meta"
                             || clean.Contains "<link"
                             || clean.Contains "<script"
                             || clean.Contains "<iframe")
                            (sprintf "a real dangerous element must not survive the boundary rule: %s" payload)
                }

                test "a truncated open tag at end of input is still stripped" {
                    let clean = TrustedTypes.createHtml "<p>ok</p><script"
                    Expect.isFalse (clean.Contains "<script") "end of input counts as a tag-name boundary"
                } ]

          testList
              "the .NET build takes the floor alone"
              [ test "html is createHtml where there is no Trusted Types API" {
                    // The uniformity claim, stated where it can be observed: this
                    // pipeline has no DOM, so `html` must be the sanitisation
                    // floor and nothing else. The Fable pipeline adds the trusted
                    // wrapper around exactly these bytes.
                    for payload in [ markdownHtml; chartSvgWithProvenance; mathMl; themeCss ] do
                        Expect.equal
                            (TrustedTypes.html payload)
                            (TrustedTypes.createHtml payload)
                            "the .NET path is the floor with no wrapper"
                } ]

          test "the policy name is the one the contract and the CSP directive share" {
              Expect.equal
                  TrustedTypes.policyName
                  "fuaran-renderer"
                  "the policy name is a public surface: a host pins this exact string in its `trusted-types` directive, so changing it is a breaking change for every such host"
          } ]
