module Fuaran.UI.Design.Tests.BundleTests

// Phase 2294 — the design-system bundle's contract, over the default theme and the
// Dark and High-Contrast sample themes, plus the go-red probes that show each check
// can fail: a kind removed from the sweep, a Colors token named by a literal colour,
// and a card whose first line is not its marker. Lane `pure`: nothing here touches
// the filesystem (`Bundle.write` is exercised through the CLI verb).

open System.Text.Json
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.ThemeManifest
open Fuaran.UI.Fragments
open Fuaran.UI.Design

let private themes: (string * Theme) list =
    [ "default", Defaults.theme
      "dark", Fuaran.Samples.Themes.Dark.theme
      "high-contrast", Fuaran.Samples.Themes.HighContrast.theme ]

let private defaults () = Bundle.BundleOptions.defaults ()

let private expectedCardCount () =
    (Bundle.idlKinds ()).Length + Stdlib.all<unit>.Length + 3

let private themeTests (name: string, theme: Theme) =
    testList
        name
        [ test "the bundle checks hold" {
              let bundle = Bundle.build theme None (defaults ())
              Expect.isEmpty (Bundle.check bundle) "a bundle built from the defaults satisfies its own contract"
          }

          test "card count is IDL kinds + stdlib fragments + Type, Spacing and Colors" {
              let bundle = Bundle.build theme None (defaults ())
              Expect.equal bundle.Cards.Length (expectedCardCount ()) "one card per kind, per fragment, per scale"
          }

          test "the content hash is stable across two builds" {
              let a = Bundle.build theme None (defaults ())
              let b = Bundle.build theme None (defaults ())
              Expect.equal a.ContentHash b.ContentHash "same theme, same options, same bytes"
              Expect.stringStarts a.ContentHash "sha256:" "the hash names its algorithm"
          }

          test "the Colors group names the seven tone roles, none of them a literal colour" {
              let bundle = Bundle.build theme None (defaults ())
              let roles = bundle.Colors |> List.map _.Role |> Set.ofList

              for tone in [ "Default"; "Subdued"; "Brand"; "Success"; "Warning"; "Critical"; "Info" ] do
                  Expect.contains roles tone $"the {tone} role is offered"

              for r in bundle.Colors do
                  Expect.isFalse (Bundle.isLiteralColour r.Role) $"{r.Role} is a role, not a value"
          }

          test "the DTCG manifest beside the cards decodes back" {
              let bundle = Bundle.build theme None (defaults ())

              let file = bundle.Files |> List.find (fun f -> f.Path = "theme.tokens.json")

              match Decode.decode file.Content with
              | Ok m -> Expect.equal m.Tokens.Length bundle.Manifest.Tokens.Length "every token round-trips"
              | Error e -> failtest $"theme.tokens.json does not decode: {e}"
          } ]

[<Tests>]
let perTheme =
    testList "Design-system bundle — per theme" (themes |> List.map themeTests)

[<Tests>]
let contract =
    testList
        "Design-system bundle — contract"
        [ test "every IDL kind is covered by the default sweep" {
              let covered = (defaults ()).Specimens |> List.map _.Kind |> Set.ofList

              let missing =
                  Bundle.idlKinds ()
                  |> List.map _.Tag
                  |> List.filter (fun k -> not (Set.contains k covered))

              Expect.isEmpty missing "a kind with no specimen would have no card"
          }

          test "go-red: a kind removed from the sweep fails the check, naming it" {
              let options =
                  { defaults () with
                      Specimens = (defaults ()).Specimens |> List.filter (fun s -> s.Kind <> "Metric") }

              let findings = Bundle.check (Bundle.build Defaults.theme None options)

              Expect.contains
                  findings
                  "IDL kind Metric has no Components card (no specimen in the sweep builds it)"
                  "the removed kind is named"

              Expect.equal findings.Length 1 "and nothing else is reported"
          }

          test "go-red: a Colors token named by a literal colour is refused" {
              let manifest =
                  { ThemeManifest.empty with
                      Tokens =
                          [ { Name = "color.accent"
                              Type = "color"
                              Value = "#ff0000"
                              Description = None
                              Role = None } ]
                      Roles =
                          [ { Role = ManifestRole.Named "#ff0000"
                              TokenName = "color.accent" } ] }

              let findings =
                  Bundle.check (Bundle.build Defaults.theme (Some manifest) (defaults ()))

              Expect.contains
                  findings
                  "Colors token \"#ff0000\" is a literal colour; the Colors group offers roles only"
                  "the hex-named token is refused"
          }

          test "a manifest's named roles are offered beside the tone roles" {
              let manifest =
                  { ThemeManifest.empty with
                      Tokens =
                          [ { Name = "color.neutral.0"
                              Type = "color"
                              Value = "#ffffff"
                              Description = None
                              Role = None } ]
                      Roles =
                          [ { Role = ManifestRole.Named "page-surface"
                              TokenName = "color.neutral.0" } ] }

              let bundle = Bundle.build Defaults.theme (Some manifest) (defaults ())
              Expect.isEmpty (Bundle.check bundle) "a role-named manifest satisfies the contract"

              let surface = bundle.Colors |> List.find (fun r -> r.Role = "page-surface")
              Expect.equal surface.Reference (Some "#ffffff") "the resolved value is kept as a reference"
              Expect.equal bundle.Colors.Length 8 "seven tone roles plus the named one"
          }

          test "literal-colour detection" {
              for name in [ "#fff"; "#3b5bdb"; "rgb(0 0 0)"; "oklch(0.6 0.1 250)"; "Red"; "transparent" ] do
                  Expect.isTrue (Bundle.isLiteralColour name) $"{name} is a literal colour"

              for name in [ "Brand"; "Critical"; "page-surface"; "body-text"; "redline" ] do
                  Expect.isFalse (Bundle.isLiteralColour name) $"{name} is a role"
          }

          test "go-red: a card whose first line is not its marker fails the check" {
              let bundle = Bundle.build Defaults.theme None (defaults ())

              let tampered =
                  { bundle with
                      Cards =
                          bundle.Cards
                          |> List.map (fun c ->
                              if c.Name = "Badge" then
                                  { c with
                                      Html = c.Html.Replace("group=\"Components\"", "group=\"Widgets\"") }
                              else
                                  c) }

              Expect.contains
                  (Bundle.check tampered)
                  "card cards/components/Badge.html: marker group \"Widgets\" is not one of the five"
                  "a marker outside the five groups is refused"
          }

          test "every card's first line is the marker for its group" {
              let bundle = Bundle.build Defaults.theme None (defaults ())

              for c in bundle.Cards do
                  Expect.stringStarts c.Html (Bundle.marker c.Group + "\n") $"{c.Path} opens with its marker"
          }

          test "a brand stylesheet re-resolves the roles and moves the hash" {
              let plain = Bundle.build Defaults.theme None (defaults ())

              let branded =
                  Bundle.build
                      Defaults.theme
                      None
                      { defaults () with
                          Brand = Some ":root { --fuaran-tone-brand-bg: #123456; }" }

              Expect.isEmpty (Bundle.check branded) "a branded bundle satisfies the contract"
              Expect.notEqual branded.ContentHash plain.ContentHash "the brand reaches the bytes"

              let brand = branded.Colors |> List.find (fun r -> r.Role = "Brand")
              Expect.equal brand.Reference (Some "#123456") "the brand value is the role's reference"
          }

          // Phase 2339 — the brand cascades last, in a <style> of its own.
          test "the brand is the head's last style element, its @import first in it" {
              let brandCss =
                  "@import url(\"https://fonts.example/brand.css\");\n:root { font-family: \"Brand Sans\", serif; }"

              let branded =
                  Bundle.build
                      Defaults.theme
                      None
                      { defaults () with
                          Brand = Some brandCss }

              Expect.isEmpty (Bundle.check branded) "a branded bundle satisfies the contract"

              for c in branded.Cards do
                  let head = c.Html.Substring(0, c.Html.IndexOf "</head>")
                  let lastStyle = head.LastIndexOf "<style>"

                  Expect.equal
                      (head.Substring lastStyle)
                      $"<style>\n{brandCss}\n</style>\n"
                      $"{c.Path}: the brand is the last style element, verbatim, @import first"

                  Expect.isFalse
                      (head.Substring(0, lastStyle).Contains "font-family")
                      $"{c.Path}: nothing before the brand sets a font-family"
          }

          test "an unbranded card carries no empty brand style element" {
              let plain = Bundle.build Defaults.theme None (defaults ())

              for c in plain.Cards do
                  let head = c.Html.Substring(0, c.Html.IndexOf "</head>")
                  let styles = head.Split("<style>").Length - 1
                  Expect.equal styles 2 $"{c.Path}: theme and scaffolding only"
          }

          test "a brand stylesheet that could close the style element is refused" {
              Expect.throws
                  (fun () ->
                      Bundle.build
                          Defaults.theme
                          None
                          { defaults () with
                              Brand = Some ":root {} </style><script>" }
                      |> ignore)
                  "the brand is inlined into a <style> element"
          }

          test "manifest.json lists every file, the groups, the hash and the IDL version" {
              let bundle = Bundle.build Defaults.theme None (defaults ())
              use doc = JsonDocument.Parse(Bundle.manifestJson bundle)
              let root = doc.RootElement

              Expect.equal (root.GetProperty("contentHash").GetString()) bundle.ContentHash "the hash"

              Expect.equal
                  (root.GetProperty("idl").GetProperty("version").GetInt32())
                  (Bundle.idlVersion ())
                  "the IDL version"

              Expect.equal (root.GetProperty("files").GetArrayLength()) bundle.Files.Length "every file"

              let grouped =
                  [ for g in root.GetProperty("groups").EnumerateObject() -> g.Value.GetArrayLength() ]
                  |> List.sum

              Expect.equal grouped bundle.Cards.Length "every card is filed under a group"
          }

          test "the content hash moves when one file moves" {
              let bundle = Bundle.build Defaults.theme None (defaults ())

              let edited =
                  bundle.Files
                  |> List.map (fun f ->
                      if f.Path = "README.md" then
                          { f with Content = f.Content + "x" }
                      else
                          f)

              Expect.notEqual (Bundle.contentHashOf edited) bundle.ContentHash "a one-byte edit moves the hash"
          } ]

// ─── Phase 2468 — the cards show what they say they show ─────────────────────

let private specimen (kind: string) =
    (defaults ()).Specimens |> List.find (fun s -> s.Kind = kind)

let private brandAxes = (ToneVariant.Brand, StyleWeight.Standard, Emphasis.Normal)

let private card (bundle: Bundle.Bundle) (name: string) =
    bundle.Cards
    |> List.find (fun c -> c.Group = Bundle.CardGroup.Components && c.Name = name)

let private figureCount (html: string) =
    html.Split("<figure class=\"ds-variant\"").Length - 1

/// A specimen of one kind, the sweep's single representative of it.
let private probe (kind: string) (hint: string) (build: unit -> Node<unit>) : Bundle.Specimen =
    { Kind = kind
      Label = "Probe"
      Hint = hint
      Build = fun _ -> build () }

/// The default sweep with every specimen of `kind` replaced by `replacement`.
let private withOnly (kind: string) (replacement: Bundle.Specimen) =
    { defaults () with
        Specimens =
            ((defaults ()).Specimens |> List.filter (fun s -> s.Kind <> kind))
            @ [ replacement ] }

[<Tests>]
let shown =
    testList
        "Design-system bundle — the cards show what they say"
        [ test "a spec-toned kind is swept through its spec, the rest through the node style" {
              let callout = (specimen "Callout").Build brandAxes

              match callout.Kind with
              | NodeKind.Callout spec -> Expect.equal spec.Tone ToneVariant.Brand "Callout reads its tone from its spec"
              | other -> failtest $"the Callout specimen built {other}"

              Expect.equal
                  (callout.Style |> Option.map _.Tone)
                  (Some ToneVariant.Default)
                  "and its node style is left at the default"

              match ((specimen "Metric").Build(ToneVariant.Default, StyleWeight.Compact, Emphasis.Loud)).Kind with
              | NodeKind.Metric spec ->
                  Expect.equal spec.Weight StyleWeight.Compact "Metric's own weight field carries the weight axis"
                  Expect.equal spec.Emphasis Emphasis.Loud "and its emphasis field the emphasis axis"
              | other -> failtest $"the Metric specimen built {other}"

              let chart = (specimen "Chart").Build brandAxes

              Expect.equal
                  (chart.Style |> Option.map _.Tone)
                  (Some ToneVariant.Brand)
                  "Chart declares no spec tone, so the node style carries it"
          }

          test "every kind whose spec carries a tone shows all seven tones" {
              let toned =
                  Bundle.idlKinds ()
                  |> List.filter (fun k -> k.Fields |> List.exists (fun f -> f.Type = "ToneVariant"))
                  |> List.map _.Tag

              Expect.containsAll toned [ "Callout"; "Metric" ] "the IDL declares Callout and Metric spec tones"

              let bundle = Bundle.build Defaults.theme None (defaults ())

              for kind in toned do
                  let html = (card bundle kind).Html

                  for tone in Fuaran.Samples.Catalog.Matrix.allTones do
                      let caption =
                          $"<figcaption>{Fuaran.Samples.Catalog.Matrix.toneLabel tone} &#183; Standard &#183; Normal</figcaption>"

                      Expect.stringContains html caption $"{kind} shows the {tone} variant"
          }

          test "a variant that would only repeat the baseline is not shown, and its axis is named" {
              let bundle = Bundle.build Defaults.theme None (defaults ())
              let chart = (card bundle "Chart").Html

              Expect.equal (figureCount chart) 1 "nothing Chart renders moves with tone, weight or emphasis"
              Expect.stringContains chart "Not shown: Tone, Weight, Emphasis." "the card says which axes it leaves out"
          }

          test "a brand that paints the wrapper tone hook brings node-style tone variants back" {
              let painted =
                  Bundle.build
                      Defaults.theme
                      None
                      { defaults () with
                          Brand = Some ".fuaran-tone-brand { outline: 2px solid; }" }

              Expect.isEmpty (Bundle.check painted) "the branded bundle satisfies the contract"

              Expect.stringContains
                  (card painted "Chart").Html
                  "<figcaption>Brand &#183; Standard &#183; Normal</figcaption>"
                  "the Brand variant is visible under that brand, so it is shown"
          }

          test "the DataGrid card previews the client grid as the static table of the same rows" {
              let bundle = Bundle.build Defaults.theme None (defaults ())
              let html = (card bundle "DataGrid").Html

              Expect.isFalse (html.Contains "data-fuaran-ssr-placeholder") "no hydration placeholder"

              Expect.stringContains
                  html
                  "<th class=\"fuaran-table-header\">Channel</th>"
                  "the grid's columns head the table"

              Expect.stringContains
                  html
                  "<td class=\"fuaran-table-cell\">35.6</td>"
                  "its cells read as the grid formats them"

              Expect.stringContains
                  html
                  "static read-only table the server renderer emits"
                  "and the preview says what it is"
          }

          test "no card carries an empty Metric label, Callout heading or body, or a placeholder" {
              let bundle = Bundle.build Defaults.theme None (defaults ())

              for c in bundle.Cards do
                  for blank in
                      [ "class=\"fuaran-metric-label\"></div>"
                        "class=\"fuaran-callout-heading\"></div>"
                        "class=\"fuaran-callout-body\"></div>"
                        "data-fuaran-ssr-placeholder" ] do
                      Expect.isFalse (c.Html.Contains blank) $"{c.Path}: no {blank}"
          }

          test "go-red: a variant that renders the same as its baseline is named" {
              let bundle = Bundle.build Defaults.theme None (defaults ())
              let callout = card bundle "Callout"
              let start = callout.Html.IndexOf "<figure class=\"ds-variant\""
              let finish = callout.Html.IndexOf("</figure>", start) + "</figure>".Length
              let baseline = callout.Html.Substring(start, finish - start)

              // A copy of the baseline captioned as a variant, its wrapper tone hook
              // moved: the reference stylesheet paints no hook, so it shows nothing new.
              let repeat =
                  baseline
                      .Replace("Default &#183; Standard &#183; Normal", "Probe &#183; Standard &#183; Normal")
                      .Replace("fuaran-tone-default", "fuaran-tone-brand")

              let tampered =
                  { bundle with
                      Cards =
                          bundle.Cards
                          |> List.map (fun c ->
                              if c.Path = callout.Path then
                                  { c with
                                      Html = c.Html.Insert(finish, repeat) }
                              else
                                  c) }

              Expect.equal
                  (Bundle.check tampered)
                  [ "card cards/components/Callout.html: specimen Callout, variant Probe · Standard · Normal renders the same as its baseline" ]
                  "the repeated variant is the one finding"
          }

          test "go-red: an example with an empty text slot is named" {
              let options =
                  withOnly
                      "Callout"
                      (probe "Callout" "A callout with a blank heading." (fun () ->
                          Fuaran.callout
                              "callout-probe"
                              { Defaults.callout with
                                  Heading = Some(TextSource.Literal "")
                                  Body = TextSource.Literal "Body text." }))

              Expect.equal
                  (Bundle.check (Bundle.build Defaults.theme None options))
                  [ "card cards/components/Callout.html: an example renders an empty fuaran-callout-heading" ]
                  "the blank heading is the one finding"
          }

          test "go-red: an example whose text key has no text renders an empty slot, and it is named" {
              let options =
                  withOnly
                      "Metric"
                      (probe "Metric" "A metric whose label key has no text." (fun () ->
                          Fuaran.metric
                              "metric-probe"
                              { Defaults.metric with
                                  Label = TextSource.Bound(Binding.I18n("probe.unknown", None))
                                  Value = Binding.Static(Some 1.0) }))

              Expect.equal
                  (Bundle.check (Bundle.build Defaults.theme None options))
                  [ "card cards/components/Metric.html: an example renders an empty fuaran-metric-label" ]
                  "the blank label is the one finding"
          }

          test "go-red: an example showing an unresolved i18n key is named" {
              let options =
                  withOnly
                      "Markdown"
                      (probe "Markdown" "Markdown whose text is a key with no entry." (fun () ->
                          { Fuaran.markdown "markdown-probe" "" with
                              Kind = NodeKind.Markdown { Text = TextSource.I18n("probe.unknown", Map.empty) } }))

              Expect.equal
                  (Bundle.check (Bundle.build Defaults.theme None options))
                  [ "card cards/components/Markdown.html: an example renders the unresolved key [i18n:probe.unknown]" ]
                  "the unresolved key is the one finding"
          }

          test "go-red: a client grid whose rows do not resolve leaves a placeholder, and it is named" {
              let options =
                  withOnly
                      "DataGrid"
                      (probe "DataGrid" "A grid bound to rows the example does not hold." (fun () ->
                          Generated.mkDataGrid "grid-probe" [] (Binding.State("probe.rows", None))))

              Expect.equal
                  (Bundle.check (Bundle.build Defaults.theme None options))
                  [ "card cards/components/DataGrid.html: an example renders a DataGrid hydration placeholder" ]
                  "the placeholder is the one finding"
          } ]

// Phase 2470 — figures. A figure kind's value takes a locale-free `CellFormat`; a
// reader-facing figure is `Binding.Format` with a locale, which yields text. The cards
// name both routes, and an export with a locale shows the second where a kind can
// carry it (a Fact's value) and says what it reads where a kind cannot.

let private cardHtml (bundle: Bundle.Bundle) (kind: string) =
    (bundle.Cards |> List.find (fun c -> c.Path = $"cards/components/{kind}.html")).Html

let private withLocale (tag: string) = { defaults () with Locale = Some tag }

let private figureKinds = [ "Metric"; "LabelValueRow"; "Fact"; "DataGrid" ]

[<Tests>]
let figures =
    testList
        "Design-system bundle — figures"
        [ test "the figure kinds' notes name both routes" {
              let bundle = Bundle.build Defaults.theme None (defaults ())

              for kind in figureKinds do
                  let html = cardHtml bundle kind
                  Expect.stringContains html "<strong>Figures:</strong>" $"{kind} carries the figures note"
                  Expect.stringContains html "CellFormat" $"{kind} names the locale-free route"
                  Expect.stringContains html "Binding.Format with a locale" $"{kind} names the reader-facing route"

              Expect.isFalse
                  ((cardHtml bundle "Markdown").Contains "<strong>Figures:</strong>")
                  "a kind with no figure carries no figures note"

              let readme = (bundle.Files |> List.find (fun f -> f.Path = "README.md")).Content
              Expect.stringContains readme "Figures have two routes." "the bundle README carries the rule"
          }

          test "an en-GB export shows the figure grouped and symbol-led, through Binding.Format" {
              let bundle = Bundle.build Defaults.theme None (withLocale "en-GB")
              Expect.isEmpty (Bundle.check bundle) "the localised bundle satisfies its own contract"

              let fact = cardHtml bundle "Fact"

              Expect.stringContains
                  fact
                  "data-ds-specimen=\"Fact, a reader-facing figure (en-GB)\""
                  "the Fact card adds the reader-facing specimen"

              let rendered =
                  fact.Substring(fact.IndexOf "data-ds-specimen=\"Fact, a reader-facing figure (en-GB)\"")

              Expect.stringContains rendered "£142,500" "the figure renders grouped, symbol-led"

              for kind in [ "Metric"; "LabelValueRow"; "DataGrid" ] do
                  Expect.stringContains
                      (System.Net.WebUtility.HtmlDecode(cardHtml bundle kind))
                      "<strong>Under en-GB:</strong> a reader-facing figure reads £142,500"
                      $"{kind} says what the figure reads under the locale"

              Expect.stringContains
                  (cardHtml bundle "Metric")
                  "GBP 142500.00"
                  "the Metric's own value keeps the locale-free figure the screen renders"
          }

          test "the locale reaches only the figure cards and the README" {
              let plain = Bundle.build Defaults.theme None (defaults ())
              let localised = Bundle.build Defaults.theme None (withLocale "en-GB")

              let touched =
                  Set.ofList [ for k in figureKinds -> $"cards/components/{k}.html" ]
                  |> Set.add "README.md"

              Expect.equal (localised.Files |> List.map _.Path) (plain.Files |> List.map _.Path) "the same files"

              for (a, b) in List.zip plain.Files localised.Files do
                  if Set.contains a.Path touched then
                      Expect.notEqual b.Content a.Content $"{a.Path} carries the locale"
                  else
                      Expect.equal b.Content a.Content $"{a.Path} is byte-identical without the locale"

              Expect.notEqual localised.ContentHash plain.ContentHash "the locale reaches the hash"

              for f in plain.Files do
                  Expect.isFalse
                      (f.Content.Contains "reader-facing figure (")
                      $"{f.Path}: no locale specimen without a locale"

                  Expect.isFalse (f.Content.Contains "<strong>Under ") $"{f.Path}: no locale line without a locale"
          }

          test "go-red: a locale the platform does not know is refused" {
              for tag in [ "xx-NOTREAL"; ""; " " ] do
                  Expect.throwsT<System.ArgumentException>
                      (fun () -> Bundle.build Defaults.theme None (withLocale tag) |> ignore)
                      $"\"{tag}\" is refused rather than formatted under the invariant culture"
          } ]

// ─── Common shapes (Phase 2475) ───────────────────────────────────────────────
//
// The README teaches five shapes as combinations of kinds. These tests hold the
// teaching to the tree: the section exists and its check can go red, the cards a
// shape touches point at it, and each combination renders as the README says.

let private serverRender (node: Node<unit>) : string =
    Fuaran.UI.Renderer.Server.Render.render
        Fuaran.UI.Renderer.BindingResolver.empty
        (Node.mapMsg (fun () -> obj ()) node)

let private md (id: string) (text: string) : Node<unit> = Fuaran.markdown id text

let private readmeOf (bundle: Bundle.Bundle) =
    (bundle.Files |> List.find (fun f -> f.Path = "README.md")).Content

let private shapeNames =
    [ "Columns that share the width"
      "A status message holding rows"
      "A progress bar showing its value"
      "A row of key figures"
      "A table whose cells carry a status"
      "A row that opens its detail" ]

[<Tests>]
let commonShapes =
    testList
        "Design-system bundle — common shapes"
        [ test "the README teaches each shape, and the check holds" {
              let bundle = Bundle.build Defaults.theme None (defaults ())
              let readme = readmeOf bundle
              Expect.stringContains readme "\n## Common shapes\n" "the section exists"

              for name in shapeNames do
                  Expect.stringContains readme $"### {name}\n" $"the README teaches \"{name}\""

              Expect.stringContains readme "Uses: SplitPanel (weight, children)" "a shape names its kinds and fields"
              Expect.stringContains readme "the kpi-strip fragment" "the key-figure row names its fragment"

              Expect.stringContains
                  readme
                  "Uses: SplitPanel (weight, children); DataGrid (staticRows, columns, rowKeyField)"
                  "the drill names the table's row key"

              Expect.isFalse (readme.Contains "not taught here yet") "the drill is taught, not deferred"
              Expect.isEmpty (Bundle.check bundle) "the bundle checks hold"
          }

          test "go-red: a kind a shape names, removed from the IDL list, is named" {
              let bundle = Bundle.build Defaults.theme None (defaults ())

              let without =
                  { bundle with
                      Kinds = bundle.Kinds |> List.filter (fun k -> k.Tag <> "SplitPanel") }

              Expect.contains
                  (Bundle.check without)
                  "Common shape \"Columns that share the width\" names kind SplitPanel, which the IDL does not declare"
                  "the removed kind is named against the shape that teaches it"
          }

          test "go-red: a field a shape names, renamed in the IDL, is named" {
              let bundle = Bundle.build Defaults.theme None (defaults ())

              let rename (f: Bundle.IdlField) =
                  if f.Name = "comparisons" then
                      { f with Name = "compare" }
                  else
                      f

              let renamed =
                  { bundle with
                      Kinds =
                          bundle.Kinds
                          |> List.map (fun k ->
                              if k.Tag = "Metric" then
                                  { k with
                                      Fields = List.map rename k.Fields }
                              else
                                  k) }

              Expect.contains
                  (Bundle.check renamed)
                  "Common shape \"A row of key figures\" names Metric.comparisons, a field the IDL does not declare"
                  "the renamed field is named"
          }

          test "go-red: a README without the section is named" {
              let bundle = Bundle.build Defaults.theme None (defaults ())

              let stripped =
                  { bundle with
                      Files =
                          bundle.Files
                          |> List.map (fun f ->
                              if f.Path = "README.md" then
                                  { f with
                                      Content = f.Content.Replace("## Common shapes", "## Shapes") }
                              else
                                  f) }

              Expect.contains (Bundle.check stripped) "README.md has no Common shapes section" "the section is checked"
          }

          test "the cards a shape touches point at the section" {
              let bundle = Bundle.build Defaults.theme None (defaults ())

              for kind in [ "SplitPanel"; "Box"; "Callout"; "Progress"; "Metric"; "DataGrid" ] do
                  Expect.stringContains
                      (cardHtml bundle kind)
                      "<strong>Common shapes:</strong>"
                      $"the {kind} card points at Common shapes"

              Expect.stringContains
                  (cardHtml bundle "Callout")
                  "A status message holding rows"
                  "the Callout card names the shape it is the tempting wrong kind for"

              Expect.isFalse
                  ((cardHtml bundle "Toast").Contains "<strong>Common shapes:</strong>")
                  "a kind no shape names carries no pointer"
          }

          test "columns: a SplitPanel shares the row by weight, a Grid Box equally" {
              let split =
                  serverRender (
                      Fuaran.splitPanel
                          "cols"
                          { Defaults.splitPanel with
                              Weight = 0.6
                              Children = [ md "l" "Left"; md "r" "Right" ] }
                  )

              Expect.stringContains split "style=\"flex:0.600000 1 0\"" "the first pane takes the weight"
              Expect.stringContains split "style=\"flex:0.400000 1 0\"" "the second pane takes the rest"

              let grid =
                  serverRender (
                      Fuaran.box
                          "g"
                          { Defaults.box with
                              Layout = BoxLayout.Grid(3, None, None)
                              Children = [ md "a" "A"; md "b" "B"; md "c" "C" ] }
                  )

              Expect.stringContains grid "grid-template-columns:repeat(3, 1fr)" "a Grid Box shares the width equally"
          }

          test "status with rows: one Card region, the Badge carries the tone" {
              let html =
                  serverRender (
                      Fuaran.box
                          "q"
                          { Defaults.box with
                              Role = BoxRole.Card
                              Layout = BoxLayout.Flex(Orientation.Vertical, false, None)
                              Heading = Some(TextSource.Literal "Data quality")
                              Children =
                                  [ Fuaran.badge
                                        "q-status"
                                        { Defaults.badge with
                                            Label = TextSource.Literal "Needs attention"
                                            Variant = BadgeVariant.Warning }
                                    Fuaran.labelValueRow
                                        "q-rows"
                                        { Defaults.labelValueRow with
                                            Label = TextSource.Literal "Rows checked"
                                            Value = Binding.Static(Some 1200.0) } ] }
                  )

              let body = html.IndexOf "fuaran-card-body"
              Expect.isGreaterThan body 0 "the Card has a body"
              Expect.isGreaterThan (html.IndexOf "fuaran-badge-warning") body "the toned Badge sits inside the Card"
              Expect.isGreaterThan (html.IndexOf "Rows checked") body "the rows sit inside the Card"
          }

          test "progress with its value: label, bar and value in one grid row" {
              let bar =
                  Fuaran.progress
                      "band-bar"
                      { Defaults.progress with
                          Fraction = Binding.Static(Some 0.62) }

              let bar =
                  { bar with
                      Accessibility =
                          bar.Accessibility
                          |> Option.map (fun a ->
                              { a with
                                  LabelledBy = Some "band-label" }) }

              let value =
                  TextSource.Bound(
                      Binding.Format(Binding.Static(Some 0.62), Format.Percent(Some 0), LocaleSource.Explicit "en-GB")
                  )

              let html =
                  serverRender (
                      Fuaran.box
                          "band"
                          { Defaults.box with
                              Layout = BoxLayout.Grid(3, Some "minmax(0, 1fr) minmax(0, 2fr) auto", None)
                              Children =
                                  [ md "band-label" "Search"
                                    bar
                                    Fuaran.markdownSpec "band-value" { Defaults.markdown with Text = value } ] }
                  )

              Expect.stringContains html "grid-template-columns:minmax(0, 1fr) minmax(0, 2fr) auto" "three cells"
              Expect.stringContains html "aria-labelledby=\"band-label\"" "the bar is named by its label"
              Expect.stringContains html "width:62.000000%" "the bar follows the fraction"
              Expect.stringContains html "<p>62%</p>" "the value is the formatted fraction"
              Expect.isFalse (html.Contains "fuaran-progress-label") "the bar carries no merged label"
          }

          test "key figures: Metrics with comparisons share a Grid row" {
              let figure (i: int) : Node<unit> =
                  Fuaran.metric
                      $"kpi-{i}"
                      { Defaults.metric with
                          Label = TextSource.Literal $"Figure {i}"
                          Value = Binding.Static(Some 10.0)
                          Trend = Some(Binding.Static(Some 0.05))
                          TrendFormat = Some(CellFormat.Percent(Some 0))
                          Comparisons =
                              [ { Label = TextSource.Literal "vs last year"
                                  Value = Binding.Static(Some -0.02)
                                  Format = CellFormat.Percent(Some 0)
                                  Polarity = TrendPolarity.HigherIsBetter } ] }

              let html =
                  serverRender (
                      Fuaran.box
                          "kpis"
                          { Defaults.box with
                              Layout = BoxLayout.Grid(3, None, None)
                              Children = [ figure 1; figure 2; figure 3 ] }
                  )

              Expect.stringContains html "grid-template-columns:repeat(3, 1fr)" "the figures share the width"
              Expect.stringContains html "vs last year" "the further comparison is its own trend line"

              let strip =
                  serverRender (
                      Stdlib.kpiStrip<unit>
                          .Materialize(Map.ofList [ "heading", ("This week" :> obj); "count", (3 :> obj) ])
                  )

              Expect.stringContains strip "kpi-strip-metric-3" "the kpi-strip fragment lays out its figures"
          }

          test "a table whose cells carry a status: a static DataGrid with a TonedPill column" {
              let html =
                  serverRender (
                      Fuaran.tableWithColumns
                          "t"
                          [ TableColumn.text "name"
                            TableColumn.tonedPill
                                "status"
                                (Map.ofList [ "Late", ToneVariant.Critical; "On time", ToneVariant.Success ])
                                ToneVariant.Default ]
                          { Defaults.table<unit> with
                              Headers = [ TextSource.Literal "Name"; TextSource.Literal "Status" ]
                              Rows =
                                  [ [ TextSource.Literal "A"; TextSource.Literal "Late" ]
                                    [ TextSource.Literal "B"; TextSource.Literal "On time" ] ] }
                  )

              Expect.stringContains html "<table class=\"fuaran-table\">" "a static table"
              Expect.stringContains html "fuaran-pill-critical\">Late" "a late row is a Critical pill"
              Expect.stringContains html "fuaran-pill-success\">On time" "an on-time row is a Success pill"
          } ]
