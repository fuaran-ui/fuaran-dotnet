module Fuaran.UI.Design.Bundle

// ============================================================================
//  Phase 2294 — the language describes itself as a design system.
//
//  `build` renders, under one `Theme`, a preview card per IDL kind (swept over
//  the tone / weight / emphasis axes), per certified stdlib fragment, and for the
//  Type, Spacing and Colors scales, beside the theme as a DTCG manifest; `write`
//  puts the bundle on disk with a `manifest.json` naming every file, its group and
//  the bundle's content hash. `check` is the bundle's own contract, run by the
//  CLI verb and by the test suite.
//
//  Phase 2468 adds that a card shows what it says it shows: each axis is set where
//  the kind reads it, a variant that would repeat its baseline is left out, every
//  text slot has text and no part is a hydration placeholder (`shownFindings`).
//
//  Three properties are the point, and each has a check behind it:
//
//  - COMPOSITION VOCABULARY, NOT ONLY TOKENS. A card per kind the IDL declares —
//    the kind set is read from the `idl.json` embedded in this assembly, so a kind
//    admitted there with no specimen is a failed check, never a stale bundle.
//  - ROLES, NEVER LITERAL COLOURS. The Colors group offers tone roles and the
//    manifest's named roles; a resolved value appears on the reference card only.
//    A Colors token whose NAME is a literal colour is refused.
//  - ONE CARD FORMAT. A self-contained HTML document whose first line is the
//    `<!-- @dsCard group="…" -->` marker, with the theme's `:root` block inlined
//    and the canonical class stylesheet linked from the bundle root. A consumer
//    wanting another shape adapts the bundle; the bundle names no consumer.
// ============================================================================

open System
open System.IO
open System.Net
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.ThemeManifest
open Fuaran.UI.Fragments
open Fuaran.UI.Renderer
open Fuaran.Samples.Catalog

// ─── Model ────────────────────────────────────────────────────────────────────

/// The five card groups. Closed: a card in any other group fails `check`.
[<RequireQualifiedAccess>]
type CardGroup =
    | Type
    | Colors
    | Spacing
    | Components
    | Fragments

module CardGroup =
    let all: CardGroup list =
        [ CardGroup.Type
          CardGroup.Colors
          CardGroup.Spacing
          CardGroup.Components
          CardGroup.Fragments ]

    let label (group: CardGroup) : string =
        match group with
        | CardGroup.Type -> "Type"
        | CardGroup.Colors -> "Colors"
        | CardGroup.Spacing -> "Spacing"
        | CardGroup.Components -> "Components"
        | CardGroup.Fragments -> "Fragments"

    let tryParse (s: string) : CardGroup option =
        all |> List.tryFind (fun g -> label g = s)

/// One IDL kind as the bundle reads it from `idl.json`.
type IdlField =
    { Name: string
      Type: string
      Required: bool }

type IdlKind =
    { Tag: string
      Category: string
      Fields: IdlField list }

/// One representative of a kind, swept over the style axes. `Kind` is the WIRE tag
/// of the kind the specimen demonstrates (the IDL's `tag`).
type Specimen =
    { Kind: string
      Label: string
      Hint: string
      Build: ToneVariant * StyleWeight * Emphasis -> Node<unit> }

/// A colour token the bundle offers to compose with: a ROLE bound to a manifest
/// token. `Reference` is the resolved value, shown on the reference card and never
/// offered as a token of its own.
type ColorRole =
    { Role: string
      Token: string
      Reference: string option }

type Card =
    {
        Group: CardGroup
        Name: string
        /// Bundle-relative, forward-slashed.
        Path: string
        Html: string
    }

type BundleFile = { Path: string; Content: string }

type BundleOptions =
    {
        /// A brand stylesheet (custom properties, typically `--fuaran-tone-*`),
        /// cascaded after the theme in every card and folded into the role values.
        Brand: string option
        /// The component sweep. `BundleOptions.defaults` is the catalog matrix plus
        /// the specimens for the kinds the catalog does not show.
        Specimens: Specimen list
        /// Phase 2470 — the reader's locale, a BCP-47 tag. With one, the Fact card
        /// adds a figure produced through `Binding.Format` under it, and the figure
        /// kinds' cards state what that figure reads. `None` is the locale-free bundle.
        Locale: string option
    }

type Bundle =
    {
        IdlVersion: int
        Kinds: IdlKind list
        Cards: Card list
        Colors: ColorRole list
        /// The DTCG manifest written beside the cards.
        Manifest: ThemeManifest
        /// Every file except `manifest.json`, sorted by path.
        Files: BundleFile list
        /// `sha256:` + hex over the sorted file set (see `contentHashOf`).
        ContentHash: string
    }

// ─── The IDL, read from the embedded idl.json ────────────────────────────────

let private resource (name: string) : string =
    let asm = typeof<CardGroup>.Assembly

    use stream = asm.GetManifestResourceStream name

    if isNull stream then
        failwithf "Fuaran.UI.Design: embedded resource %s is missing" name

    use reader = new StreamReader(stream, Encoding.UTF8)
    reader.ReadToEnd()

let rec private typeLabel (t: JsonElement) : string =
    let str (name: string) =
        match t.TryGetProperty name with
        | true, v -> v.GetString()
        | _ -> ""

    match str "$type" with
    | "union" ->
        let args =
            match t.TryGetProperty "args" with
            | true, a when a.GetArrayLength() > 0 ->
                "<" + (a.EnumerateArray() |> Seq.map typeLabel |> String.concat ", ") + ">"
            | _ -> ""

        str "name" + args
    | "enum"
    | "record" -> str "name"
    | "list" ->
        match t.TryGetProperty "of" with
        | true, o -> typeLabel o + " list"
        | _ -> "list"
    | "option" ->
        match t.TryGetProperty "of" with
        | true, o -> typeLabel o + " option"
        | _ -> "option"
    | other -> other

let private parseIdl (json: string) : int * IdlKind list =
    use doc = JsonDocument.Parse json
    let root = doc.RootElement

    let kinds =
        [ for k in root.GetProperty("kinds").EnumerateArray() ->
              { Tag = k.GetProperty("tag").GetString()
                Category = k.GetProperty("category").GetString()
                Fields =
                  [ for f in k.GetProperty("fields").EnumerateArray() ->
                        { Name = f.GetProperty("name").GetString()
                          Type = typeLabel (f.GetProperty "type")
                          Required = f.GetProperty("optionality").GetProperty("$type").GetString() = "required" } ] } ]

    root.GetProperty("version").GetInt32(), kinds

let private idl: Lazy<int * IdlKind list> =
    lazy (parseIdl (resource "Fuaran.UI.Design.idl.json"))

/// The IDL version and kinds the bundle is checked against.
let idlVersion () : int = fst idl.Value

let idlKinds () : IdlKind list = snd idl.Value

/// The canonical class stylesheet the cards link.
let referenceStylesheet: Lazy<string> =
    lazy (resource "Fuaran.UI.Design.fuaran-reference.css")

// ─── The style axes, set where each kind reads them ──────────────────────────

/// The IDL type names of the three swept axes.
let private axisTypes = [ "ToneVariant"; "StyleWeight"; "Emphasis" ]

/// Phase 2468 — per kind tag, the spec field carrying each axis, keyed by the axis's
/// IDL type name: a field typed `ToneVariant`, `StyleWeight` or `Emphasis`. Read from
/// the IDL, so a kind that gains a spec tone is swept through it with no edit here.
/// The type is the test, not the name: `Fact.emphasis` is a `bool` and
/// `SplitPanel.weight` a `float`, and neither is an axis.
let private specAxisFields: Lazy<Map<string, Map<string, string>>> =
    lazy
        (idlKinds ()
         |> List.map (fun k ->
             k.Tag,
             k.Fields
             |> List.filter (fun f -> List.contains f.Type axisTypes)
             |> List.map (fun f -> f.Type, f.Name)
             |> Map.ofList)
         |> Map.ofList)

/// Replace one field of a node's spec record, named as the IDL names it.
let private setSpecField (node: Node<unit>) (idlName: string) (value: obj) : Node<unit> =
    let case, fields =
        Reflection.FSharpValue.GetUnionFields(node.Kind, typeof<NodeKind<unit>>)

    let spec = fields |> Array.tryExactlyOne |> Option.filter (isNull >> not)

    let target =
        spec
        |> Option.bind (fun spec ->
            let props = Reflection.FSharpType.GetRecordFields(spec.GetType())

            props
            |> Array.tryFindIndex (fun p ->
                String.Equals(p.Name, idlName, StringComparison.OrdinalIgnoreCase)
                && p.PropertyType = value.GetType())
            |> Option.map (fun i -> spec, i))

    match target with
    | Some(spec, i) ->
        let values = Reflection.FSharpValue.GetRecordFields spec
        values[i] <- value

        { node with
            Kind =
                Reflection.FSharpValue.MakeUnion(case, [| Reflection.FSharpValue.MakeRecord(spec.GetType(), values) |])
                :?> NodeKind<unit> }
    | None ->
        failwithf "Fuaran.UI.Design: the IDL declares %s.%s but its spec record has no such field" case.Name idlName

// ─── Specimens ────────────────────────────────────────────────────────────────

/// Set the axes on a node where its kind reads them: the spec's own field when the
/// IDL declares one, the node style otherwise. Callout reads its tone from its spec,
/// so a node-style tone on it would caption a variant the card does not show.
let private styled (tone: ToneVariant, weight: StyleWeight, emphasis: Emphasis) (node: Node<unit>) : Node<unit> =
    let fields =
        specAxisFields.Value
        |> Map.tryFind (Kind.wireTag node.Kind)
        |> Option.defaultValue Map.empty

    let onSpec (typeName: string) (value: obj) (n: Node<unit>) =
        match Map.tryFind typeName fields with
        | Some name -> setSpecField n name value, true
        | None -> n, false

    let n, toneOnSpec = onSpec "ToneVariant" (box tone) node
    let n, weightOnSpec = onSpec "StyleWeight" (box weight) n
    let n, emphasisOnSpec = onSpec "Emphasis" (box emphasis) n
    let style = n.Style |> Option.defaultValue Defaults.style

    { n with
        Style =
            Some
                { style with
                    Tone = (if toneOnSpec then style.Tone else tone)
                    Weight = (if weightOnSpec then style.Weight else weight)
                    Emphasis = (if emphasisOnSpec then style.Emphasis else emphasis) } }

let private literal (s: string) = TextSource.Literal s

let private sample (kind: string) (hint: string) (build: unit -> Node<unit>) : Specimen =
    { Kind = kind
      Label = kind
      Hint = hint
      Build = fun axes -> styled axes (build ()) }

let private exampleFragment = Stdlib.labelledMetricRow<unit>

let private factHint = "A labelled value pair for read-only detail views."

/// Representatives for the kinds the catalog matrix does not show. Each is the
/// smallest honest instance of its kind; `check` is what notices a kind with none.
let supplementSpecimens: Specimen list =
    [ sample "CodeBlock" "Monospaced source with optional line numbers and highlights." (fun () ->
          Fuaran.codeBlock "codeblock" "fsharp" "let total = items |> List.sumBy _.Price")
      sample "Drawing" "Vector shapes in a declared view box; charts lower to it." (fun () ->
          Fuaran.drawing
              "drawing"
              { MinX = 0.0
                MinY = 0.0
                Width = 120.0
                Height = 60.0 }
              [ Shape.Rectangle(4.0, 4.0, 52.0, 52.0, Some 6.0, Defaults.drawStyle)
                Shape.Circle(90.0, 30.0, 24.0, Defaults.drawStyle) ])
      sample "Embed" "Sandboxed third-party frame; grants no permission by default." (fun () ->
          Fuaran.embed "embed" "https://player.example.org/v/demo" "Walkthrough video")
      { Kind = "ErrorBoundary"
        Label = "ErrorBoundary"
        Hint = "Renders its child, or its fallback when the child fails to render."
        Build =
          fun axes ->
              Fuaran.errorBoundary
                  "errorboundary"
                  { Child =
                      styled
                          axes
                          (Fuaran.metric
                              "errorboundary-child"
                              { Defaults.metric with
                                  Label = literal "Active users"
                                  Value = Binding.Static(Some 1280.0) })
                    Fallback = Fuaran.markdown "errorboundary-fallback" "This panel is unavailable." } }
      sample "Fact" factHint (fun () -> Fuaran.fact "fact" "Account owner" "Northwind team")
      { Kind = "FragmentDecl"
        Label = "FragmentDecl"
        Hint = "A parameterised template with typed holes. Paints nothing itself; shown here applied by a FragmentRef."
        Build =
          fun axes ->
              Fuaran.box
                  "fragmentdecl"
                  { Defaults.box with
                      Children = [ exampleFragment.Decl; styled axes exampleFragment.Example ] } }
      { Kind = "FragmentRef"
        Label = "FragmentRef"
        Hint = "Applies a declared fragment, binding its holes."
        Build =
          fun axes ->
              Fuaran.box
                  "fragmentref"
                  { Defaults.box with
                      Children = [ exampleFragment.Decl; styled axes exampleFragment.Example ] } }
      sample "Icon" "A named glyph from the icon vocabulary, decorative unless labelled." (fun () ->
          Fuaran.iconSpec
              "icon"
              { Defaults.icon with
                  Icon = "info"
                  Label = Some "Information" })
      sample "Image" "A raster or vector image with alt text and a fit." (fun () ->
          Fuaran.image "image" "https://images.example.org/sample.png" "Sample illustration")
      sample "Link" "A navigational anchor." (fun () -> Fuaran.link "link" "https://example.org/docs" "Read the guide")
      sample "List" "An ordered or unordered list of text items." (fun () ->
          Fuaran.list "list" [ "Draft the brief"; "Review with the team"; "Publish" ])
      sample "Math" "Typeset mathematics (block or inline)." (fun () -> Fuaran.math "math" "a^2 + b^2 = c^2")
      sample "Media" "Audio or video with controls, tracks and a transcript." (fun () ->
          Fuaran.video "media" "https://media.example.org/intro.mp4" "Product introduction")
      sample "Modal" "A dialog over the page; open state is a binding." (fun () ->
          Fuaran.modal
              "modal"
              { Defaults.modal<unit> with
                  Open = Binding.Static(Some true)
                  Heading = Some(literal "Confirm export")
                  Children = [ Fuaran.markdown "modal-body" "The report will be emailed to the team." ] })
      { Kind = "Mount"
        Label = "Mount"
        Hint = "An isolation boundary a host-registered guest renders into."
        Build =
          fun axes ->
              styled
                  axes
                  (Fuaran.mount
                      "mount"
                      { Capabilities = []
                        Channel = Fuaran.guestOutChannel
                        Inputs = None
                        OnBubble = None
                        ScopeId = "design-guest" }) }
      sample "ScrollArea" "A bounded region that scrolls its children." (fun () ->
          Fuaran.scrollArea
              "scrollarea"
              { Defaults.scrollArea<unit> with
                  MaxHeight = Some 120
                  Children =
                      [ for i in 1..8 -> Fuaran.markdown $"scrollarea-row-{i}" $"Row {i} of the scrolled content." ] })
      { Kind = "Switch"
        Label = "Switch"
        Hint = "Selects one child by a bound value (state, selection or filter)."
        Build =
          fun axes ->
              Fuaran.switch
                  "switch"
                  { Defaults.switch<unit> with
                      On = Binding.State("design.view", Some "summary")
                      Cases =
                          [ { Match = Some "summary"
                              When = None
                              Child = styled axes (Fuaran.markdown "switch-summary" "Summary view (the selected case).") } ]
                      Default = Fuaran.markdown "switch-default" "Default view." } }
      sample "Toast" "A transient status message; open state is a binding." (fun () ->
          Fuaran.toast
              "toast"
              { Defaults.toast with
                  Message = literal "Settings saved"
                  Open = Binding.Static(Some true) })
      sample "Tree" "A hierarchy of labelled items with optional selection." (fun () ->
          Fuaran.tree
              "tree"
              [ Fuaran.treeItem "reports" "Reports" [ Fuaran.treeItem "q1" "Q1" []; Fuaran.treeItem "q2" "Q2" [] ]
                Fuaran.treeItem "archive" "Archive" [] ]) ]

let private baselineAxes =
    (ToneVariant.Default, StyleWeight.Standard, Emphasis.Normal)

/// The catalog matrix as specimens: each entry's kind is the wire tag of the node
/// it builds, so a catalog entry is filed under the kind it actually emits. The axes
/// are then set where the kind reads them (`styled`); several catalog builders take
/// the axes and set none.
let catalogSpecimens () : Specimen list =
    Matrix.entries
    |> List.map (fun e ->
        { Kind = Kind.wireTag (e.Build baselineAxes).Kind
          Label = e.Label
          Hint = e.Hint
          Build = fun axes -> styled axes (e.Build axes) })

module BundleOptions =
    /// The catalog sweep plus the supplement, no brand.
    let defaults () : BundleOptions =
        { Brand = None
          Specimens = catalogSpecimens () @ supplementSpecimens
          Locale = None }

/// The axis sweep a component card shows: the baseline, then every other tone, weight
/// and emphasis with the other two at their defaults. One axis moves at a time, so a
/// card stays readable; the full cartesian product is the catalog gallery's job.
let sweep: (ToneVariant * StyleWeight * Emphasis) list =
    let (t0, w0, e0) = baselineAxes

    [ yield baselineAxes
      for t in Matrix.allTones do
          if t <> t0 then
              yield (t, w0, e0)
      for w in Matrix.allWeights do
          if w <> w0 then
              yield (t0, w, e0)
      for e in Matrix.allEmphases do
          if e <> e0 then
              yield (t0, w0, e) ]

// ─── Colour roles ─────────────────────────────────────────────────────────────

let private namedColours: Set<string> =
    ("aliceblue antiquewhite aqua aquamarine azure beige bisque black blanchedalmond blue blueviolet brown "
     + "burlywood cadetblue chartreuse chocolate coral cornflowerblue cornsilk crimson cyan darkblue darkcyan "
     + "darkgoldenrod darkgray darkgreen darkgrey darkkhaki darkmagenta darkolivegreen darkorange darkorchid "
     + "darkred darksalmon darkseagreen darkslateblue darkslategray darkslategrey darkturquoise darkviolet "
     + "deeppink deepskyblue dimgray dimgrey dodgerblue firebrick floralwhite forestgreen fuchsia gainsboro "
     + "ghostwhite gold goldenrod gray green greenyellow grey honeydew hotpink indianred indigo ivory khaki "
     + "lavender lavenderblush lawngreen lemonchiffon lightblue lightcoral lightcyan lightgoldenrodyellow "
     + "lightgray lightgreen lightgrey lightpink lightsalmon lightseagreen lightskyblue lightslategray "
     + "lightslategrey lightsteelblue lightyellow lime limegreen linen magenta maroon mediumaquamarine "
     + "mediumblue mediumorchid mediumpurple mediumseagreen mediumslateblue mediumspringgreen mediumturquoise "
     + "mediumvioletred midnightblue mintcream mistyrose moccasin navajowhite navy oldlace olive olivedrab "
     + "orange orangered orchid palegoldenrod palegreen paleturquoise palevioletred papayawhip peachpuff peru "
     + "pink plum powderblue purple rebeccapurple red rosybrown royalblue saddlebrown salmon sandybrown "
     + "seagreen seashell sienna silver skyblue slateblue slategray slategrey snow springgreen steelblue tan "
     + "teal thistle tomato turquoise violet wheat white whitesmoke yellow yellowgreen transparent currentcolor")
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
    |> Set.ofArray

let private colourFunctions =
    [ "rgb("
      "rgba("
      "hsl("
      "hsla("
      "hwb("
      "lab("
      "lch("
      "oklab("
      "oklch("
      "color("
      "color-mix(" ]

/// Whether a token NAME is a literal colour — a hex value, a colour function or a
/// CSS named colour. The Colors group offers roles; a name like this is a value
/// wearing a role's clothes, and composing with it would pin the design to one
/// palette.
let isLiteralColour (name: string) : bool =
    let s = name.Trim().ToLowerInvariant()

    s.StartsWith "#"
    || colourFunctions |> List.exists s.StartsWith
    || Set.contains s namedColours

let private roleName (role: ManifestRole) : string =
    match role with
    | ManifestRole.Tone t -> ManifestRole.toneToString t
    | ManifestRole.Named n -> n

let private rolesOf (manifest: ThemeManifest) : ColorRole list =
    manifest.Roles
    |> List.map (fun b ->
        { Role = roleName b.Role
          Token = b.TokenName
          Reference = ThemeManifest.tryGetToken b.TokenName manifest |> Option.map _.Value })

// ─── HTML ─────────────────────────────────────────────────────────────────────

let private enc (s: string) : string = WebUtility.HtmlEncode s

/// The marker every card's FIRST line carries.
let marker (group: CardGroup) : string =
    $"<!-- @dsCard group=\"{CardGroup.label group}\" -->"

/// What a card's head carries beside the reference stylesheet: the theme's `:root`
/// block with any manifest bridge, and the brand stylesheet (empty when none).
type private CardStyles =
    {
        Theme: string
        Brand: string
        /// The wrapper hook families (`tone`, `weight`, `emphasis`) some stylesheet
        /// in the card's cascade paints (see `paintedHooks`).
        Painted: Set<string>
    }

/// The head cascades in the order the card contract states: reference stylesheet,
/// theme and bridge, the card's own scaffolding, then the brand in a `<style>` of its
/// own. Last, so its rules win at equal specificity; alone, so an `@import` at its
/// start is the first statement of its stylesheet, which is the only place CSS
/// honours one. The scaffolding sets no `font-family`, leaving the reference
/// stylesheet's (or the brand's) typeface in charge.
let private cardDocument (group: CardGroup) (title: string) (depth: int) (styles: CardStyles) (body: string) : string =
    let up = String.replicate depth "../"

    String.concat
        "\n"
        [ marker group
          "<!doctype html>"
          "<html lang=\"en\">"
          "<head>"
          "<meta charset=\"utf-8\">"
          $"<title>{enc title}</title>"
          $"<link rel=\"stylesheet\" href=\"{up}fuaran-reference.css\">"
          $"<style>\n{styles.Theme}\n</style>"
          "<style>"
          "body { margin: 0; padding: var(--fuaran-space-lg, 24px);"
          "  background: var(--fuaran-tone-default-bg); color: var(--fuaran-tone-default-fg); }"
          ".ds-note { max-width: 72ch; margin-bottom: var(--fuaran-space-lg, 24px); }"
          ".ds-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));"
          "  gap: var(--fuaran-space-md, 16px); }"
          ".ds-variant { margin: 0; padding: var(--fuaran-space-sm, 8px);"
          "  border: var(--fuaran-border-width, 1px) dashed var(--fuaran-tone-subdued-border); }"
          ".ds-variant figcaption { font-size: var(--fuaran-text-xs, 12px); opacity: 0.75;"
          "  margin-bottom: var(--fuaran-space-xs, 4px); }"
          ".ds-table { border-collapse: collapse; } .ds-table td, .ds-table th { padding: 4px 12px;"
          "  text-align: left; border-bottom: 1px solid var(--fuaran-tone-subdued-border); }"
          ".ds-swatch { display: inline-block; width: 48px; height: 24px; vertical-align: middle;"
          "  border: 1px solid var(--fuaran-tone-subdued-border); }"
          "</style>"
          if styles.Brand.Trim() <> "" then
              $"<style>\n{styles.Brand}\n</style>"
          "</head>"
          "<body>"
          body
          "</body>"
          "</html>"
          "" ]

/// English text for the catalog's I18n keys. The catalog binds a few labels through
/// `Binding.I18n` so its locale switcher retranslates them, and its English locale is
/// a pass-through; rendered with no catalog those slots come out blank. The bundle
/// resolves them here. A key with no entry renders `[i18n:<key>]`, which `check`
/// refuses, so a new catalog key cannot quietly blank a card.
let private exampleText: Map<string, string> =
    Map.ofList
        [ "catalog.metric.label", "Revenue"
          "catalog.callout.heading.default", "Default notice"
          "catalog.callout.heading.subdued", "Subdued notice"
          "catalog.callout.heading.brand", "Brand notice"
          "catalog.callout.heading.success", "Success notice"
          "catalog.callout.heading.warning", "Warning notice"
          "catalog.callout.heading.critical", "Critical notice"
          "catalog.callout.heading.info", "Info notice"
          "catalog.callout.body", "A banner for a level, mode or status, sized for one block."
          "catalog.button.label", "Run analysis"
          "catalog.labelValueRow.label", "Total revenue" ]

let private exampleSources: BindingResolver.BindingSources =
    { BindingResolver.empty with
        I18n = exampleText
        I18nResolver = BindingResolver.makeI18nResolver exampleText }

let private render (node: Node<unit>) : string =
    Server.Render.render exampleSources (Node.mapMsg box node)

// ─── Figures (Phase 2470, 2490) ───────────────────────────────────────────────
//
// A figure has two routes, and the cards name both. `CellFormat` (a Metric's or a
// LabelValueRow's `Format`, a DataGrid column's) is a NUMBER's format: without a
// `locale` it is locale-free and every host renders the same text; with one (Phase
// 2490) it reads grouped and symbol-led in that locale, through the formatter
// `Binding.Format` uses, and stays a number for trends and sorting. `Binding.Format`
// with a `LocaleSource` yields TEXT, so it fits a text slot (a Fact's value, a
// subtext). With a locale, the bundle shows each route on the kinds that carry it.
//
// Phase 2499 — the money figure is whole pounds, as a designed screen shows it
// (`£142,500`): both routes carry `decimals: 0`, the fraction digits that override
// the currency's own. Without it the currency's digits apply (`£142,500.00`).

/// The fraction digits the located money figure carries on both routes (Phase 2499).
let private figureDecimals = 0

/// The money figure the catalog's Metric and LabelValueRow examples carry.
let private figureValue = 142_500.0
let private figureCurrency = "GBP"

/// The example figure through `Binding.Format` under `tag`.
let private localeFigure (tag: string) : Binding<string> =
    Binding.Format(
        Binding.Static(Some figureValue),
        Format.Currency(figureCurrency, Some figureDecimals),
        LocaleSource.Explicit tag
    )

/// Refuses a tag the platform does not know: the formatter would otherwise fall back
/// to the invariant culture, and the card would show a figure no reader reads.
let private requireLocale (tag: string) : unit =
    let known =
        not (String.IsNullOrWhiteSpace tag)
        && (try
                Globalization.CultureInfo.GetCultureInfo(tag, true) |> ignore
                true
            with :? Globalization.CultureNotFoundException ->
                false)

    if not known then
        invalidArg "options" $"the locale \"{tag}\" is not a known BCP-47 tag"

/// The specimen a locale adds to the Fact card: the example figure as a Fact's value,
/// produced through `Binding.Format` under the locale.
let private figureSpecimen (tag: string) : Specimen =
    { Kind = "Fact"
      Label = $"Fact, a reader-facing figure ({tag})"
      Hint = factHint
      Build =
        fun axes ->
            styled
                axes
                (Fuaran.factSpec
                    "fact-figure"
                    { Defaults.fact with
                        Label = literal "Revenue, year to date"
                        Value = TextSource.Bound(localeFigure tag) }) }

/// The example figure's `CellFormat` under `tag` — a figure kind's own route (Phase 2490).
let private localeCellFormat (tag: string) : CellFormat =
    CellFormat.Currency(figureCurrency, Some figureDecimals, Some(LocaleSource.Explicit tag))

/// The specimens a locale adds to the Metric and LabelValueRow cards: the example
/// figure in the kind's own value, its `CellFormat` carrying the locale.
let private figureKindSpecimens (tag: string) : Specimen list =
    [ { Kind = "Metric"
        Label = $"Metric, a figure in {tag}"
        Hint = "A headline figure with its label."
        Build =
          fun axes ->
              styled
                  axes
                  (Fuaran.metric
                      "metric-figure"
                      { Defaults.metric with
                          Label = literal "Revenue, year to date"
                          Value = Binding.Static(Some figureValue)
                          Format = localeCellFormat tag }) }
      { Kind = "LabelValueRow"
        Label = $"LabelValueRow, a figure in {tag}"
        Hint = "A label and its figure on one row."
        Build =
          fun axes ->
              styled
                  axes
                  (Fuaran.labelValueRow
                      "labelvaluerow-figure"
                      { Defaults.labelValueRow with
                          Label = literal "Revenue, year to date"
                          Value = Binding.Static(Some figureValue)
                          Format = localeCellFormat tag }) } ]

/// The "Figures" lines of a figure kind's authoring note, by wire tag.
let private figureNote (locale: string option) (kind: string) : string list =
    let cellFigure =
        Formatting.formatCellValue
            (CellFormat.Currency(figureCurrency, None, None))
            (HostPrelude.CellValue.Numeric figureValue)

    let localeFree what =
        $"{what} a CellFormat. Without a locale it is locale-free: every host renders the same text (Currency \"{figureCurrency}\" reads {cellFigure})."

    let located =
        "A reader-facing figure, grouped and symbol-led in the reader's locale, is the same CellFormat with a locale (Currency with locale Explicit \"en-GB\"); it stays a number, so trends, comparisons and sorting still read it. Its decimals sets the fraction digits on either route: whole pounds are Currency with decimals 0; without it the currency's own digits apply. Binding.Format with a locale is the route for a text slot (a Fact's value, a subtext)."

    let routes =
        match kind with
        | "Metric"
        | "LabelValueRow" -> Some(localeFree "The value is shown through Format," + " " + located)
        | "DataGrid" -> Some(localeFree "A column's Format is" + " " + located)
        | "Fact" ->
            Some
                "The value is text. A reader-facing figure in it is Binding.Format with a locale. A figure kind (Metric, LabelValueRow, a DataGrid column) carries its locale in its CellFormat instead."
        | _ -> None

    match routes with
    | None -> []
    | Some r ->
        [ $"<p><strong>Figures:</strong> {enc r}</p>"
          match locale with
          | None -> ()
          | Some tag ->
              let reads =
                  BindingResolver.tryResolve exampleSources (localeFigure tag)
                  |> Option.defaultValue ""

              match kind with
              | "Fact" ->
                  $"<p><strong>Under {enc tag}:</strong> the figure reads {enc reads}, shown below through Binding.Format.</p>"
              | "DataGrid" ->
                  $"<p><strong>Under {enc tag}:</strong> a column whose CellFormat carries the locale reads {enc reads} in each cell of the hydrated grid. The preview below is a static table, whose cells are authored text.</p>"
              | _ ->
                  $"<p><strong>Under {enc tag}:</strong> a reader-facing figure reads {enc reads}, shown below through the CellFormat's locale; the other examples keep the locale-free figure.</p>" ]

/// Phase 2472 — the Metric card says where a SECOND comparison goes, because the
/// design-seam pilot pushed one into the subtext as text and lost its glyph, its
/// sentiment and its polarity.
let private comparisonsNote (kind: string) : string list =
    if kind = "Metric" then
        [ "<p><strong>Comparisons:</strong> trend is the figure's primary comparison. A figure compared against more than one period (yesterday against last week and last year) carries each further one in comparisons: a label, a value, a format and a polarity, each rendered as its own trend line with its own glyph and reading. Not the subtext, which is text with no direction, and not a second Metric, which repeats the figure.</p>" ]
    else
        []

// ─── Common shapes (Phase 2475) ───────────────────────────────────────────────
//
// A shape a designer reaches for that is a COMBINATION of kinds, not a kind of its
// own. The design-seam pilot lost three of them: the designer composed each from
// the kind that looked closest, and the lowering followed. The README teaches the
// combination and names the tempting kind; the cards a shape touches point at it;
// `check` keeps every kind, field and fragment a shape names in the IDL, so a
// rename goes red here instead of leaving the teaching to name a part that is gone.
//
// Each combination was rendered through the server renderer before it was taught.
// A Card Box's node tone is not painted (the Box card shows Tone as not shown), so
// a status message's tone rides a toned Badge inside the Card.

/// A shape taught as a combination of kinds.
type private CommonShape =
    {
        /// The heading the README and the cards use.
        Name: string
        /// The kinds the combination uses, each with the wire fields it sets.
        Uses: (string * string list) list
        /// The stdlib fragments the combination applies.
        Fragments: string list
        /// The tempting kinds, each with what of it a designer reaches for.
        Tempting: (string * string) list
        /// The README prose: the combination, then why the tempting kind loses.
        Text: string list
    }

/// The README heading the section sits under; `check` looks for it.
[<Literal>]
let private commonShapesHeading = "## Common shapes"

let private commonShapes: CommonShape list =
    [ { Name = "Columns that share the width"
        Uses = [ "SplitPanel", [ "weight"; "children" ]; "Box", [ "layout"; "children" ] ]
        Fragments = []
        Tempting = [ "Box", "a Flex layout" ]
        Text =
          [ "Two columns sharing the row at a ratio are a `SplitPanel`: its first child takes `weight` of the width"
            "(0.5 for halves, 0.6 for three fifths) and the second the rest, each in its own pane. `weight` is that"
            "share, not the Weight density axis. Three or more columns are a `Box` whose `layout` is `Grid` with"
            "`cols`, which shares the width equally; `templateColumns` sets unequal shares."
            ""
            "Not a `Box` with a `Flex` layout: a flex row sizes each child to its content and has no grow or basis,"
            "so the columns render narrow." ] }
      { Name = "A status message holding rows"
        Uses = [ "Box", [ "role"; "heading"; "children" ]; "Badge", [ "label"; "variant" ] ]
        Fragments = []
        Tempting = [ "Callout", "its body" ]
        Text =
          [ "One bordered region: a `Box` with `role` Card, its `heading` naming the subject, and its `children` a"
            "`Badge` carrying the status (its `variant` is the tone) followed by the rows (LabelValueRow, Fact,"
            "Markdown). The tone goes on the Badge: the Box card shows Tone as not painted, so a tone set on the"
            "Card itself would not show."
            ""
            "Not a `Callout`: its `body` is text and it holds no children, so the rows end up in a second region"
            "beside it." ] }
      { Name = "A progress bar showing its value"
        Uses =
          [ "Box", [ "layout"; "children" ]
            "Progress", [ "fraction" ]
            "Markdown", [ "text" ] ]
        Fragments = []
        Tempting = [ "Progress", "its label" ]
        Text =
          [ "Three cells in a `Box` whose `layout` is `Grid` with `templateColumns` (for example"
            "`minmax(0, 1fr) minmax(0, 2fr) auto`): the label as `Markdown`, the `Progress` with its `fraction`"
            "and no `label`, and the value as `Markdown` whose `text` is the fraction through Binding.Format with"
            "a Percent format. Several bars are one such Box with three cells per bar, so labels, bars and values"
            "line up in columns. Point each bar at its label through `accessibility.labelledBy`."
            ""
            "Not the label and the value merged into the Progress `label`: that is one line of text above the bar,"
            "and the value can no longer line up or follow the fraction." ] }
      { Name = "A row of key figures"
        Uses = [ "Metric", [ "trend"; "comparisons" ]; "Box", [ "layout"; "children" ] ]
        Fragments = [ "kpi-strip" ]
        Tempting = [ "Fact", "a figure as text" ]
        Text =
          [ "The `kpi-strip` fragment: a heading and one to six `Metric`s, each figure's format, trend and"
            "polarity a hole, and its label, value and trend read from host state. A row whose figures share the"
            "width, or compare against more than one period, is a `Box` whose `layout` is `Grid` with `cols` set"
            "to the figure count, holding `Metric`s; a figure's further comparison is an entry in its"
            "`comparisons`."
            ""
            "Not `Fact`s or Markdown numbers, which carry no trend and no polarity, and not a second comparison"
            "written into a Metric's subtext." ] }
      { Name = "A table whose cells carry a status"
        Uses = [ "DataGrid", [ "staticRows"; "columns" ] ]
        Fragments = []
        Tempting = [ "Badge", "a pill per cell" ]
        Text =
          [ "A static `DataGrid`: `staticRows` holds the headers and the cells, and `columns`, read by position,"
            "says how each column's cells draw. A column whose cell kind is TonedPill draws each cell as a pill"
            "toned by its value through the column's map (a rise Success, a fall Critical); declare a Text column"
            "ahead of it to keep the positions aligned."
            ""
            "Not a `Badge` per cell: Badge is a node, never a cell, and a column of nodes is no longer a table." ] }
      // Phase 2474 — the drill, taught once a static table's rows could be selected.
      { Name = "A row that opens its detail"
        Uses =
          [ "SplitPanel", [ "weight"; "children" ]
            "DataGrid", [ "staticRows"; "columns"; "rowKeyField" ]
            "Fact", [ "label"; "value" ] ]
        Fragments = []
        Tempting = [ "Markdown", "the detail written out" ]
        Text =
          [ "A `SplitPanel` holding a static `DataGrid` and the detail pane. The grid's `rowKeyField` names the"
            "column whose cell identifies a row, so clicking a row selects it; each detail `Fact` binds its `value`"
            "through a Selection on the grid's id with its own `field`, and reads that cell of the selected row. A"
            "row selected in the design is the Selection on the key field with a `defaultValue` naming that row:"
            "the server render marks the row and every Fact shows its cells."
            ""
            "Not the detail written out as `Markdown` or literal Facts: a pane that does not bind to the selection"
            "shows one row forever, and a click changes nothing beside it." ] } ]

/// The "Common shapes" lines of a kind's authoring note: the shapes it is part of,
/// and the shapes it is the tempting wrong part for.
let private shapesNote (kind: string) : string list =
    let quoted (s: CommonShape) = $"“{s.Name}”"

    let partOf =
        commonShapes
        |> List.filter (fun s -> s.Uses |> List.exists (fun (k, _) -> k = kind))
        |> List.map quoted

    let notFor =
        commonShapes
        |> List.choose (fun s ->
            s.Tempting
            |> List.tryFind (fun (k, _) -> k = kind)
            |> Option.map (fun (_, what) -> $"{quoted s} (not {what})"))

    if List.isEmpty partOf && List.isEmpty notFor then
        []
    else
        let parts =
            [ if not (List.isEmpty partOf) then
                  "part of " + String.concat ", " partOf
              if not (List.isEmpty notFor) then
                  "composed otherwise for " + String.concat ", " notFor ]

        [ $"""<p><strong>Common shapes:</strong> {enc (String.concat "; " parts)}. The README's Common shapes section gives each combination.</p>""" ]

// ─── Server-renderable previews ──────────────────────────────────────────────

/// A static read-only table in the node's place, keeping its id and style. This is
/// the table the server renderer emits for a `DataGrid` carrying `staticRows`.
///
/// Phase 2473 — `columns` ride beside the rows, read positionally, so a previewed
/// grid's `TonedPill` columns keep their tones on the server render.
let private staticTable
    (node: Node<unit>)
    (columns: ColumnErased<unit> list)
    (headers: string list)
    (rows: string list list)
    : Node<unit> =
    let table =
        Fuaran.tableWithColumns
            node.Id
            columns
            { Defaults.table<unit> with
                Headers = headers |> List.map literal
                Rows = rows |> List.map (List.map literal) }

    { table with Style = node.Style }

/// Phase 2468 — a client-drawn kind renders on the server as a hydration placeholder
/// (`[Grid: 3 rows — hydrates client-side]`), which shows a designer nothing. Over
/// rows the example resolves, a client-library `DataGrid` is previewed as the static
/// table of the same rows (cell text as the first-party grid formats it) and a `Map`
/// as the table of its markers. Returns the rewritten tree and a caption per preview.
let rec private serverPreview (node: Node<unit>) : Node<unit> * string list =
    match node.Kind with
    | NodeKind.DataGrid spec when spec.StaticRows.IsNone ->
        (match BindingResolver.tryResolve exampleSources spec.Source with
         | Some rows ->
             let cell (col: ColumnErased<unit>) (row: Map<string, obj>) =
                 let value =
                     match col.Value, col.Field with
                     | Some accessor, _ -> accessor row
                     | None, Some field -> BindingResolver.projectRowFieldValue row field
                     | None, None -> HostPrelude.CellValue.Empty

                 Formatting.formatCellValue col.Format value

             staticTable
                 node
                 spec.Columns
                 (spec.Columns |> List.map _.Label)
                 [ for r in rows -> [ for c in spec.Columns -> cell c r ] ],
             [ "Shown as the static read-only table the server renderer emits for the same rows, toned pills included. The interactive grid (sorting, selection) renders client-side." ]
         | None -> node, [])
    | NodeKind.Map spec ->
        (match BindingResolver.tryResolve exampleSources spec.Source with
         | Some markers ->
             staticTable
                 node
                 []
                 [ "Marker"; "Latitude"; "Longitude" ]
                 [ for m in markers -> [ m.Label; sprintf "%.4f" m.Latitude; sprintf "%.4f" m.Longitude ] ],
             [ "Shown as a static table of the map's markers, which is what a server can show of it. The map itself draws client-side." ]
         | None -> node, [])
    | _ ->
        let children, rebuild = NodeChildren.lens NodeChildren.Reach.kindHeld node

        match children with
        | [] -> node, []
        | _ ->
            let previews = children |> List.map serverPreview

            if previews |> List.forall (snd >> List.isEmpty) then
                node, []
            else
                rebuild (List.map fst previews), previews |> List.collect snd

// ─── Variant comparison ──────────────────────────────────────────────────────

/// The three wrapper hook families every rendered node carries as classes.
let private hookFamilies = [ "tone"; "weight"; "emphasis" ]

let private cssComment =
    RegularExpressions.Regex(@"/\*.*?\*/", RegularExpressions.RegexOptions.Singleline)

/// The hook families a stylesheet paints: those with a `.fuaran-<family>-` selector
/// outside a comment. The reference stylesheet paints none (tone reaches pixels
/// through each kind's own suffix class, and weight and emphasis are left to hosts),
/// so a node-style axis is visible only where a theme or brand binds the hook.
let private paintedHooks (css: string) : Set<string> =
    let live = cssComment.Replace(css, "")

    hookFamilies
    |> List.filter (fun f -> live.Contains $".fuaran-{f}-")
    |> Set.ofList

let private rootId = RegularExpressions.Regex("data-fuaran-node-id=\"([^\"]+)\"")

/// What a variant shows, for comparing it with its baseline: the rendered markup with
/// its root node id (and every id derived from it) masked, and the hook classes no
/// stylesheet in the cascade paints removed. Two variants with one signature look the
/// same on the card.
let private variantSignature (painted: Set<string>) (markup: string) : string =
    let m = rootId.Match markup

    let masked =
        if m.Success then
            RegularExpressions.Regex.Replace(
                markup,
                "(?<=[\"\\s#])" + RegularExpressions.Regex.Escape m.Groups[1].Value,
                "\u0000"
            )
        else
            markup

    hookFamilies
    |> List.filter (fun f -> not (Set.contains f painted))
    |> List.fold
        (fun acc f -> RegularExpressions.Regex.Replace(acc, $"(?<=[\\s\"])fuaran-{f}-[a-z]+(?=[\\s\"])", ""))
        masked

let private axesLabel (tone, weight, emphasis) =
    $"{Matrix.toneLabel tone} · {Matrix.weightLabel weight} · {Matrix.emphasisLabel emphasis}"

let private bindsOf (kind: IdlKind) : string list =
    kind.Fields
    |> List.filter (fun f -> f.Type.StartsWith "Binding" || f.Type.StartsWith "TextSource")
    |> List.map (fun f -> $"{f.Name} ({f.Type})")

let private slotsOf (kind: IdlKind) : string list =
    kind.Fields
    |> List.filter (fun f -> f.Type = "node" || f.Type = "node list")
    |> List.map _.Name

let private listOrNone (items: string list) =
    if List.isEmpty items then
        "none"
    else
        String.concat ", " items

let private componentCard
    (styles: CardStyles)
    (locale: string option)
    (kind: IdlKind)
    (specimens: Specimen list)
    : Card =
    let purpose = specimens |> List.map _.Hint |> List.distinct |> String.concat " "
    let required = kind.Fields |> List.filter _.Required |> List.map _.Name

    let note =
        String.concat
            "\n"
            [ $"<h1>{enc kind.Tag}</h1>"
              "<div class=\"ds-note\">"
              $"<p><strong>Category:</strong> {enc kind.Category}.</p>"
              $"<p><strong>For:</strong> {enc purpose}</p>"
              $"<p><strong>Binds to:</strong> {enc (listOrNone (bindsOf kind))}.</p>"
              $"<p><strong>Children:</strong> {enc (listOrNone (slotsOf kind))}.</p>"
              $"<p><strong>Required fields:</strong> {enc (listOrNone required)}.</p>"
              yield! figureNote locale kind.Tag
              yield! comparisonsNote kind.Tag
              yield! shapesNote kind.Tag
              "</div>" ]

    let sections =
        [ for s in specimens do
              let variant axes =
                  let tree, captions = serverPreview (s.Build axes)
                  axes, render tree, captions

              // The sweep's head is the baseline. A variant whose signature equals the
              // baseline's would show a caption the card does not show, so it is left
              // out and its axis is named below as not shown.
              let baseline = variant (List.head sweep)
              let (_, baselineHtml, captions) = baseline
              let baselineSignature = variantSignature styles.Painted baselineHtml

              let shown =
                  List.tail sweep
                  |> List.map variant
                  |> List.filter (fun (_, html, _) -> variantSignature styles.Painted html <> baselineSignature)

              let (t0, w0, _) = baselineAxes

              let axisOf (t, w, _) =
                  if t <> t0 then "Tone"
                  elif w <> w0 then "Weight"
                  else "Emphasis"

              let shownAxes = shown |> List.map (fun (axes, _, _) -> axisOf axes) |> Set.ofList

              let unshown =
                  [ "Tone"; "Weight"; "Emphasis" ]
                  |> List.filter (fun a -> not (Set.contains a shownAxes))

              yield $"<h2>{enc s.Label}</h2>"

              for c in captions |> List.distinct do
                  yield $"<p class=\"ds-note\">{enc c}</p>"

              if not (List.isEmpty unshown) then
                  let names = String.concat ", " unshown

                  yield
                      $"<p class=\"ds-note\">Not shown: {enc names}. Setting them does not change what this part renders under this theme and brand, so a variant would only repeat the first.</p>"

              yield "<div class=\"ds-grid\">"

              for (axes, html, _) in baseline :: shown do
                  yield
                      $"<figure class=\"ds-variant\" data-ds-specimen=\"{enc s.Label}\"><figcaption>{enc (axesLabel axes)}</figcaption><!--ds-render-->{html}<!--/ds-render--></figure>"

              yield "</div>" ]

    { Group = CardGroup.Components
      Name = kind.Tag
      Path = $"cards/components/{kind.Tag}.html"
      Html = cardDocument CardGroup.Components kind.Tag 2 styles (String.concat "\n" (note :: sections)) }

let private holeLine (h: HoleDecl) : string =
    match h with
    | HoleDecl.Value(name, space, dflt) ->
        let spaceText =
            match space with
            | HoleValueSpace.IntRange(lo, hi) -> $"integer {lo}..{hi}"
            | HoleValueSpace.FloatRange(lo, hi) -> $"number {lo}..{hi}"
            | HoleValueSpace.StringLen(lo, hi) -> $"text of {lo}..{hi} characters"
            | HoleValueSpace.Enum choices -> "one of " + String.concat " | " choices
            | HoleValueSpace.AnyString -> "any text"

        let req = if dflt.IsSome then "optional" else "required"
        $"{name}: value, {spaceText} ({req})"
    | HoleDecl.Slot(name, constraintKind) ->
        let c =
            constraintKind
            |> Option.map (fun k -> $", a {k} node")
            |> Option.defaultValue ""

        $"{name}: slot{c} (required)"
    | HoleDecl.Repeat(name, _) -> $"{name}: repeat count (required)"

let private fragmentCard (styles: CardStyles) (fragment: StdlibFragment<unit>) : Card =
    let holes =
        match fragment.Decl.Kind with
        | NodeKind.FragmentDecl spec -> spec.Holes |> List.map holeLine
        | _ -> []

    // The example's own arguments, applied the way the certification harness applies
    // them: `Materialize` over the value and repeat bindings, then each slot marker
    // replaced by its bound subtree. A renderer expands a `FragmentRef` against its
    // declaration but binds no arguments (the host applies them), so rendering the
    // bare reference would show the holes' fallbacks rather than the example.
    let args =
        match fragment.Example.Kind with
        | NodeKind.FragmentRef spec -> spec.Args |> Option.map Map.toList |> Option.defaultValue []
        | _ -> []

    let values =
        args
        |> List.choose (fun (name, arg) ->
            match arg with
            | FragmentArg.Int v -> Some(name, box v)
            | FragmentArg.Float v -> Some(name, box v)
            | FragmentArg.Bool v -> Some(name, box v)
            | FragmentArg.Str v -> Some(name, box v)
            | FragmentArg.SlotArg _ -> None)
        |> Map.ofList

    let slots =
        args
        |> List.choose (fun (name, arg) ->
            match arg with
            | FragmentArg.SlotArg tree -> Some(name, tree)
            | _ -> None)
        |> Map.ofList

    let rec fillSlots (node: Node<unit>) : Node<unit> =
        match node.Kind with
        | NodeKind.FragmentRef spec when spec.Args.IsNone && Map.containsKey spec.Name slots -> Map.find spec.Name slots
        | _ ->
            let children, rebuild = NodeChildren.lens NodeChildren.Reach.fragmentScope node
            rebuild (children |> List.map fillSlots)

    let tree = fillSlots (fragment.Materialize values)

    let body =
        [ $"<h1>{enc fragment.Name}</h1>"
          "<div class=\"ds-note\">"
          $"<p>{enc fragment.Summary}</p>"
          "<p><strong>Holes:</strong></p><ul>" ]
        @ (holes |> List.map (fun h -> $"<li>{enc h}</li>"))
        @ [ "</ul>"
            $"<p>Apply it with a <code>FragmentRef</code> named <code>{enc fragment.Name}</code>, binding the required holes; the example below binds every one.</p>"
            "</div>"
            render tree ]
        |> String.concat "\n"

    { Group = CardGroup.Fragments
      Name = fragment.Name
      Path = $"cards/fragments/{fragment.Name}.html"
      Html = cardDocument CardGroup.Fragments fragment.Name 2 styles body }

let private tokenRows (vars: (string * string) list) (prefixes: string list) =
    vars |> List.filter (fun (name, _) -> prefixes |> List.exists name.StartsWith)

let private typeCard (styles: CardStyles) (theme: Theme) : Card =
    let vars = Theme.toCssVariables theme

    let rows =
        tokenRows vars [ "--fuaran-text-"; "--fuaran-font-weight-"; "--fuaran-line-height-" ]
        |> List.map (fun (name, value) ->
            let sampleStyle =
                if name.StartsWith "--fuaran-text-" then
                    $"font-size: var({name})"
                elif name.StartsWith "--fuaran-font-weight-" then
                    $"font-weight: var({name})"
                else
                    $"line-height: var({name}); max-width: 24ch; display: inline-block"

            $"<tr><td><code>{enc name}</code></td><td>{enc value}</td><td><span style=\"{enc sampleStyle}\">The quick brown fox jumps over the lazy dog</span></td></tr>")

    let headings =
        Fuaran.box
            "type-headings"
            { Defaults.box with
                Children =
                    [ for level in 1..4 ->
                          Fuaran.heading
                              $"type-h{level}"
                              { Defaults.heading with
                                  Level = level
                                  Text = literal $"Heading level {level}" } ]
                    @ [ Fuaran.markdown
                            "type-body"
                            "Body text is **Markdown**: a paragraph, *emphasis*, `code` and [a link](https://example.org)." ] }

    let body =
        [ "<h1>Type</h1>"
          "<div class=\"ds-note\"><p>The type scale, weights and line heights of the theme, as the custom properties the renderer reads. Compose with heading levels and Markdown; the renderer maps them onto this scale, so a design names a level, never a size.</p></div>"
          render headings
          "<table class=\"ds-table\"><tr><th>Token</th><th>Value</th><th>Sample</th></tr>" ]
        @ rows
        @ [ "</table>" ]
        |> String.concat "\n"

    { Group = CardGroup.Type
      Name = "Type"
      Path = "cards/type/type.html"
      Html = cardDocument CardGroup.Type "Type" 2 styles body }

let private spacingCard (styles: CardStyles) (theme: Theme) : Card =
    let vars = Theme.toCssVariables theme

    let rows =
        tokenRows
            vars
            [ "--fuaran-space-"
              "--fuaran-radius-"
              "--fuaran-border-width"
              "--fuaran-button-pad-" ]
        |> List.map (fun (name, value) ->
            let sampleStyle =
                if name.StartsWith "--fuaran-radius-" then
                    $"display: inline-block; width: 48px; height: 32px; border-radius: var({name}); background: var(--fuaran-tone-subdued-bg); border: 1px solid var(--fuaran-tone-subdued-border)"
                elif name = "--fuaran-border-width" then
                    $"display: inline-block; width: 48px; height: 32px; border: var({name}) solid var(--fuaran-tone-default-border)"
                else
                    $"display: inline-block; height: 16px; width: var({name}); background: var(--fuaran-tone-brand-bg)"

            $"<tr><td><code>{enc name}</code></td><td>{enc value}</td><td><span style=\"{enc sampleStyle}\"></span></td></tr>")

    let weights =
        Fuaran.box
            "spacing-weights"
            { Defaults.box with
                Children =
                    [ for w in Matrix.allWeights ->
                          styled
                              (ToneVariant.Default, w, Emphasis.Normal)
                              (Fuaran.box
                                  $"spacing-{Matrix.weightLabel w}"
                                  { Defaults.box with
                                      Role = BoxRole.Card
                                      Heading = Some(literal $"Weight: {Matrix.weightLabel w}")
                                      Children =
                                          [ Fuaran.markdown
                                                $"spacing-{Matrix.weightLabel w}-body"
                                                "Density follows the weight axis." ] }) ] }

    let body =
        [ "<h1>Spacing</h1>"
          "<div class=\"ds-note\"><p>The spacing, radius and border scales. A design chooses DENSITY through the weight axis (Compact, Standard, Spacious) on a node; it never sets a gap or padding directly.</p></div>"
          render weights
          "<table class=\"ds-table\"><tr><th>Token</th><th>Value</th><th>Sample</th></tr>" ]
        @ rows
        @ [ "</table>" ]
        |> String.concat "\n"

    { Group = CardGroup.Spacing
      Name = "Spacing"
      Path = "cards/spacing/spacing.html"
      Html = cardDocument CardGroup.Spacing "Spacing" 2 styles body }

let private toneCss (role: string) : string option =
    Matrix.allTones
    |> List.tryFind (fun t -> ManifestRole.toneToString t = role)
    |> Option.map (fun t -> (Matrix.toneLabel t).ToLowerInvariant())

let private colorsCard (styles: CardStyles) (roles: ColorRole list) : Card =
    let rows =
        roles
        |> List.map (fun r ->
            let swatch =
                match toneCss r.Role, r.Reference with
                | Some t, _ -> $"<span class=\"ds-swatch\" style=\"background: var(--fuaran-tone-{t}-bg)\"></span>"
                | None, Some v -> $"<span class=\"ds-swatch\" style=\"background: {enc v}\"></span>"
                | None, None -> ""

            let reference = r.Reference |> Option.defaultValue "(unresolved)"

            $"<tr><td><strong>{enc r.Role}</strong></td><td><code>{enc r.Token}</code></td><td>{swatch} <small>{enc reference}</small></td></tr>")

    let toneStrip =
        Fuaran.box
            "colors-tones"
            { Defaults.box with
                Children =
                    [ for t in Matrix.allTones ->
                          Fuaran.callout
                              $"colors-{Matrix.toneLabel t}"
                              { Defaults.callout with
                                  Tone = t
                                  Heading = Some(literal $"Tone.{Matrix.toneLabel t}")
                                  Body = literal "A role, resolved by the theme." } ] }

    let body =
        [ "<h1>Colors</h1>"
          "<div class=\"ds-note\"><p>Colours are ROLES. Compose with the role names in the first column, through a node's tone; the resolved values are shown for reference only and are not tokens. A theme or brand changes the values; a design built on roles follows it.</p></div>"
          render toneStrip
          "<table class=\"ds-table\"><tr><th>Role</th><th>Bound token</th><th>Reference value</th></tr>" ]
        @ rows
        @ [ "</table>" ]
        |> String.concat "\n"

    { Group = CardGroup.Colors
      Name = "Colors"
      Path = "cards/colors/colors.html"
      Html = cardDocument CardGroup.Colors "Colors" 2 styles body }

// ─── Authoring notes ──────────────────────────────────────────────────────────

let private readme (idlVersion: int) (kindCount: int) (fragmentCount: int) (locale: string option) : string =
    String.concat
        "\n"
        [ "# Fuaran UI design system"
          ""
          "Generated from the Fuaran UI language. Every card is a preview of a part a Fuaran UI tree can"
          "express; a screen composed from these parts lowers to a tree without invention."
          ""
          $"IDL version {idlVersion}: {kindCount} kinds, {fragmentCount} stdlib fragments."
          ""
          "## Type"
          ""
          "The type scale. Choose a heading level or Markdown emphasis; never a font size."
          ""
          "## Colors"
          ""
          "Tone roles (Default, Subdued, Brand, Success, Warning, Critical, Info) and the theme's named"
          "roles. Compose with ROLE NAMES only. The values on the card are a reference for this theme;"
          "a different theme or brand re-resolves every role, and a design that used a value would not"
          "follow it."
          ""
          "## Spacing"
          ""
          "Density is the weight axis (Compact, Standard, Spacious) on a node. Gaps, padding and radii are"
          "the theme's; a design chooses a weight, not a dimension."
          ""
          "## Components"
          ""
          "One card per IDL kind, swept over the tone, weight and emphasis axes one axis at a time. Each"
          "card says what the kind is for, which fields bind to data, and which hold children. Each axis is"
          "set where the kind reads it; a variant that would look the same as the first is left out, and"
          "the card names the axes it leaves out. Choose only a variant the card shows. A client-drawn"
          "grid or map is previewed as a static table, captioned as a preview."
          ""
          "Figures have two routes. A figure kind's Format (Metric, LabelValueRow, a DataGrid column) is a"
          "CellFormat. Without a locale it is locale-free, so every host renders the same text, and a currency"
          "reads as its code and two decimals, ungrouped. With a locale (Currency \"GBP\" with locale Explicit"
          "\"en-GB\") the figure reads grouped and symbol-led there, as £142,500.00, and stays a number for"
          "trends, comparisons and sorting. Either route takes decimals, the fraction digits, overriding the"
          "currency's own: Currency \"GBP\" with decimals 0 and locale Explicit \"en-GB\" reads £142,500. A"
          "figure in a text slot, such as a Fact's value, is Binding.Format with a locale, which yields text."
          "Design a reader-facing figure in a figure kind as its CellFormat with the reader's locale, and a"
          "figure inside text as Binding.Format; give either decimals when the design shows whole units."
          ""
          "A Metric compared against more than one period keeps its primary comparison in trend and each"
          "further one in comparisons, each its own trend line with a label, a glyph and a polarity. Do not"
          "fold a second comparison into the subtext or split the figure into two Metrics."
          match locale with
          | Some tag ->
              $"This bundle was exported for {tag}: the Fact, Metric and LabelValueRow cards show a figure as a reader there reads it."
          | None -> ()
          ""
          "## Fragments"
          ""
          "One card per certified stdlib fragment: a reusable composite with typed holes. Apply one by"
          "name and bind its holes; its example binds every hole."
          ""
          commonShapesHeading
          ""
          "A shape below is a combination of the parts above, not a part of its own. Compose it as written: the"
          "kind it is tempting to reach for instead loses something on the way to a tree."
          for s in commonShapes do
              ""
              $"### {s.Name}"
              ""
              yield! s.Text
              ""

              let uses =
                  s.Uses |> List.map (fun (k, fields) -> $"""{k} ({String.concat ", " fields})""")

              let fragments = s.Fragments |> List.map (fun f -> $"the {f} fragment")
              $"""Uses: {String.concat "; " (fragments @ uses)}."""
          "" ]

// ─── Content hash ─────────────────────────────────────────────────────────────

let private sha256Hex (bytes: byte array) : string =
    Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()

let private utf8 (s: string) : byte array = Encoding.UTF8.GetBytes s

/// The bundle's content hash: SHA-256 over one line per file, in ordinal path order,
/// of `<path> <sha256 of the file's UTF-8 bytes>\n`. `manifest.json` is excluded (it
/// carries the hash). Two builds of one theme with one option set are byte-identical,
/// so the hash is stable across runs and moves with any card, token or note.
let contentHashOf (files: BundleFile list) : string =
    let lines =
        files
        |> List.sortWith (fun a b -> String.CompareOrdinal(a.Path, b.Path))
        |> List.map (fun f -> $"{f.Path} {sha256Hex (utf8 f.Content)}\n")
        |> String.concat ""

    "sha256:" + sha256Hex (utf8 lines)

// ─── Build ────────────────────────────────────────────────────────────────────

/// Build the bundle for `theme`. `manifest`, when given, supplies the role bindings
/// and is bridged (literal values) into every card after the theme; without one, the
/// roles are projected from the theme's own tone variables. A brand stylesheet in
/// `options.Brand` cascades after both and the card scaffolding, in its own `<style>`
/// (so it may begin with `@import`), and is folded into the role values.
let build (theme: Theme) (manifest: ThemeManifest option) (options: BundleOptions) : Bundle =
    let brand = options.Brand |> Option.defaultValue ""

    if brand.IndexOf("</", StringComparison.Ordinal) >= 0 then
        invalidArg "options" "the brand stylesheet may not contain '</' (it is inlined into a <style> element)"

    let bridgeCss =
        match manifest with
        | Some m ->
            (ThemeBridge.emitCss
                m
                { ThemeBridge.BridgeOptions.defaults with
                    Mode = ThemeBridge.EmitMode.Literal })
                .Css
        | None -> ""

    let themeCss =
        [ Theme.toCss theme; bridgeCss ]
        |> List.filter (fun s -> s.Trim() <> "")
        |> String.concat "\n"

    let styles =
        { Theme = themeCss
          Brand = brand
          Painted = paintedHooks (referenceStylesheet.Value + "\n" + themeCss + "\n" + brand) }

    // The role values resolve through the whole cascade, brand last.
    let projected = Project.projectFromFuaranToneVars (themeCss + "\n" + brand)

    let dtcg = manifest |> Option.defaultValue projected

    let colors =
        let declared = manifest |> Option.map rolesOf |> Option.defaultValue []
        let declaredNames = declared |> List.map _.Role |> Set.ofList

        // The seven tone roles are always offered: a manifest that binds only some of
        // them leaves the rest resolved from the theme projection.
        let tones =
            rolesOf projected
            |> List.filter (fun r -> not (Set.contains r.Role declaredNames))

        declared @ tones

    let version, kinds = idl.Value

    let specimens =
        match options.Locale with
        | Some tag ->
            requireLocale tag
            options.Specimens @ (figureSpecimen tag :: figureKindSpecimens tag)
        | None -> options.Specimens

    let bySpecimenKind = specimens |> List.groupBy _.Kind |> Map.ofList

    let components =
        kinds
        |> List.choose (fun k ->
            Map.tryFind k.Tag bySpecimenKind
            |> Option.map (componentCard styles options.Locale k))

    let fragments = Stdlib.all<unit> |> List.map (fragmentCard styles)

    let cards =
        [ typeCard styles theme; colorsCard styles colors; spacingCard styles theme ]
        @ components
        @ fragments

    let files =
        [ yield!
              cards
              |> List.map (fun c ->
                  { BundleFile.Path = c.Path
                    Content = c.Html })
          { Path = "theme.tokens.json"
            Content = Encode.encode dtcg + "\n" }
          { Path = "fuaran-reference.css"
            Content = referenceStylesheet.Value }
          { Path = "README.md"
            Content = readme version kinds.Length (List.length Stdlib.all<unit>) options.Locale } ]
        |> List.sortWith (fun a b -> String.CompareOrdinal(a.Path, b.Path))

    { IdlVersion = version
      Kinds = kinds
      Cards = cards
      Colors = colors
      Manifest = dtcg
      Files = files
      ContentHash = contentHashOf files }

// ─── Check ────────────────────────────────────────────────────────────────────

let private markerPattern =
    RegularExpressions.Regex(
        "^<!-- @dsCard group=\"([A-Za-z]+)\" -->$",
        RegularExpressions.RegexOptions.CultureInvariant
    )

let private figurePattern =
    RegularExpressions.Regex(
        "<figure class=\"ds-variant\" data-ds-specimen=\"([^\"]*)\"><figcaption>(.*?)</figcaption><!--ds-render-->(.*?)<!--/ds-render--></figure>",
        RegularExpressions.RegexOptions.Singleline
    )

/// A text slot rendered empty: an element whose class names a `-label`, `-heading`,
/// `-body` or `-title` part, or a button, with nothing inside it.
let private emptySlotPattern =
    RegularExpressions.Regex(
        "<(\\w+)\\b[^>]*\\bclass=\"(?:[^\"]*\\s)?(fuaran-[a-z0-9-]+-(?:label|heading|body|title))(?:\\s[^\"]*)?\"[^>]*>\\s*</\\1>|<button\\b[^>]*>\\s*</button>"
    )

let private unresolvedText = RegularExpressions.Regex("\\[i18n:([^\\]]*)\\]")

let private ssrPlaceholder =
    RegularExpressions.Regex("data-fuaran-ssr-placeholder=\"([^\"]*)\"")

/// Phase 2468 — what a card shows, read from its HTML so a hand-built bundle is
/// checked the same way. (a) Every swept variant on a Components card differs from
/// its specimen's baseline in what the card's cascade paints. (b) No example renders
/// an empty text slot or an unresolved `[i18n:…]` key, and none renders a server
/// hydration placeholder in place of the part.
let private shownFindings (bundle: Bundle) : string list =
    let reference =
        bundle.Files
        |> List.tryFind (fun f -> f.Path = "fuaran-reference.css")
        |> Option.map _.Content
        |> Option.defaultValue ""

    [ for c in bundle.Cards do
          let headEnd = c.Html.IndexOf "</head>"

          let head, body =
              if headEnd < 0 then
                  "", c.Html
              else
                  c.Html.Substring(0, headEnd), c.Html.Substring headEnd

          if c.Group = CardGroup.Components then
              let painted = paintedHooks (reference + "\n" + head)

              let figures =
                  [ for m in figurePattern.Matches body -> m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value ]

              for (specimen, group) in figures |> List.groupBy (fun (s, _, _) -> s) do
                  match group with
                  | (_, _, baseline) :: variants ->
                      let baselineSignature = variantSignature painted baseline

                      for (_, caption, html) in variants do
                          if variantSignature painted html = baselineSignature then
                              yield
                                  $"card {c.Path}: specimen {WebUtility.HtmlDecode specimen}, variant {WebUtility.HtmlDecode caption} renders the same as its baseline"
                  | [] -> ()

          for m in emptySlotPattern.Matches body do
              let slot = if m.Groups[2].Success then m.Groups[2].Value else "button"
              yield $"card {c.Path}: an example renders an empty {slot}"

          for m in unresolvedText.Matches body do
              yield $"card {c.Path}: an example renders the unresolved key [i18n:{m.Groups[1].Value}]"

          for m in ssrPlaceholder.Matches body do
              yield $"card {c.Path}: an example renders a {m.Groups[1].Value} hydration placeholder" ]
    |> List.distinct

/// The bundle's contract. Empty means the bundle holds: every IDL kind has a
/// Components card, every stdlib fragment has a Fragments card, there is exactly one
/// Type, Colors and Spacing card, no Colors token is a literal colour, every card's
/// first line is a marker naming one of the five groups, and every card shows what
/// it says it shows (`shownFindings`).
/// Phase 2475 — the README's Common shapes section exists, names each shape, and
/// every kind, field and fragment a shape names is one the IDL and the stdlib
/// declare. A renamed or removed kind is a finding here, not a stale lesson.
let private commonShapeFindings (bundle: Bundle) : string list =
    let kinds = bundle.Kinds |> List.map (fun k -> k.Tag, k) |> Map.ofList
    let fragments = Stdlib.all<unit> |> List.map _.Name |> Set.ofList

    let readme =
        bundle.Files
        |> List.tryFind (fun f -> f.Path = "README.md")
        |> Option.map _.Content

    [ match readme with
      | None -> yield "README.md is missing, so the Common shapes section is too"
      | Some text ->
          if not (text.Contains(commonShapesHeading + "\n", StringComparison.Ordinal)) then
              yield "README.md has no Common shapes section"

          for s in commonShapes do
              if not (text.Contains($"### {s.Name}\n", StringComparison.Ordinal)) then
                  yield $"README.md Common shapes does not teach \"{s.Name}\""
      for s in commonShapes do
          for (tag, fields) in s.Uses do
              match Map.tryFind tag kinds with
              | None -> yield $"Common shape \"{s.Name}\" names kind {tag}, which the IDL does not declare"
              | Some k ->
                  let declared = k.Fields |> List.map _.Name |> Set.ofList

                  for f in fields do
                      if not (Set.contains f declared) then
                          yield $"Common shape \"{s.Name}\" names {tag}.{f}, a field the IDL does not declare"

          for (tag, _) in s.Tempting do
              if not (Map.containsKey tag kinds) then
                  yield $"Common shape \"{s.Name}\" names kind {tag}, which the IDL does not declare"

          for f in s.Fragments do
              if not (Set.contains f fragments) then
                  yield $"Common shape \"{s.Name}\" names fragment {f}, which the stdlib does not declare" ]

let check (bundle: Bundle) : string list =
    let cardsIn group =
        bundle.Cards |> List.filter (fun c -> c.Group = group)

    let named group =
        cardsIn group |> List.map _.Name |> Set.ofList

    let components = named CardGroup.Components
    let fragments = named CardGroup.Fragments

    [ for k in bundle.Kinds do
          if not (Set.contains k.Tag components) then
              yield $"IDL kind {k.Tag} has no Components card (no specimen in the sweep builds it)"
      for f in Stdlib.all<unit> do
          if not (Set.contains f.Name fragments) then
              yield $"stdlib fragment {f.Name} has no Fragments card"
      for g in [ CardGroup.Type; CardGroup.Colors; CardGroup.Spacing ] do
          let n = (cardsIn g).Length

          if n <> 1 then
              yield $"expected exactly one {CardGroup.label g} card, found {n}"
      for r in bundle.Colors do
          if isLiteralColour r.Role then
              yield $"Colors token \"{r.Role}\" is a literal colour; the Colors group offers roles only"
      for c in bundle.Cards do
          let firstLine =
              match c.Html.IndexOf '\n' with
              | -1 -> c.Html
              | i -> c.Html.Substring(0, i)

          let m = markerPattern.Match firstLine

          if not m.Success then
              yield $"card {c.Path}: first line is not a @dsCard marker"
          else
              match CardGroup.tryParse m.Groups[1].Value with
              | Some g when g = c.Group -> ()
              | Some g ->
                  yield
                      $"card {c.Path}: marker group {CardGroup.label g} disagrees with its group {CardGroup.label c.Group}"
              | None -> yield $"card {c.Path}: marker group \"{m.Groups[1].Value}\" is not one of the five"
      yield! commonShapeFindings bundle
      yield! shownFindings bundle ]

// ─── Write ────────────────────────────────────────────────────────────────────

let private manifestKind = "fuaranDesignSystemBundle"

/// The bundle's own `manifest.json`: the file list with each file's hash, the cards
/// by group, the content hash, and the IDL version the kinds came from.
let manifestJson (bundle: Bundle) : string =
    let options = JsonWriterOptions(Indented = true)
    use stream = new MemoryStream()

    (use w = new Utf8JsonWriter(stream, options)
     w.WriteStartObject()
     w.WriteString("kind", manifestKind)
     w.WriteNumber("formatVersion", 1)
     w.WriteString("contentHash", bundle.ContentHash)
     w.WriteStartObject("idl")
     w.WriteNumber("version", bundle.IdlVersion)
     w.WriteNumber("kinds", bundle.Kinds.Length)
     w.WriteEndObject()
     w.WriteStartObject("groups")

     for g in CardGroup.all do
         w.WriteStartArray(CardGroup.label g)

         for c in bundle.Cards |> List.filter (fun c -> c.Group = g) do
             w.WriteStringValue c.Path

         w.WriteEndArray()

     w.WriteEndObject()
     w.WriteStartArray("colorRoles")

     for r in bundle.Colors do
         w.WriteStartObject()
         w.WriteString("role", r.Role)
         w.WriteString("token", r.Token)
         w.WriteEndObject()

     w.WriteEndArray()
     w.WriteStartArray("files")

     for f in bundle.Files do
         w.WriteStartObject()
         w.WriteString("path", f.Path)
         w.WriteString("sha256", sha256Hex (utf8 f.Content))
         w.WriteEndObject()

     w.WriteEndArray()
     w.WriteEndObject())

    Encoding.UTF8.GetString(stream.ToArray()) + "\n"

let private isBundleDirectory (dir: string) : bool =
    let path = Path.Combine(dir, "manifest.json")

    File.Exists path
    && (try
            use doc = JsonDocument.Parse(File.ReadAllText path)

            match doc.RootElement.TryGetProperty "kind" with
            | true, k -> k.GetString() = manifestKind
            | _ -> false
        with _ ->
            false)

/// Write the bundle into `dir`. A directory holding a previous bundle is replaced
/// (its `cards/` tree and top-level bundle files go first, so a kind that left the
/// IDL leaves no stale card); a non-empty directory that is NOT a bundle is refused
/// rather than written into.
let write (bundle: Bundle) (dir: string) : unit =
    if
        Directory.Exists dir
        && Directory.EnumerateFileSystemEntries dir |> Seq.isEmpty |> not
    then
        if not (isBundleDirectory dir) then
            invalidOp $"{dir} is not empty and holds no design-system bundle; refusing to write into it"

        let cards = Path.Combine(dir, "cards")

        if Directory.Exists cards then
            Directory.Delete(cards, true)

        for name in [ "manifest.json"; "theme.tokens.json"; "fuaran-reference.css"; "README.md" ] do
            let p = Path.Combine(dir, name)

            if File.Exists p then
                File.Delete p

    let utf8NoBom = UTF8Encoding(false)

    for f in bundle.Files do
        let target = Path.Combine(dir, f.Path.Replace('/', Path.DirectorySeparatorChar))
        Directory.CreateDirectory(Path.GetDirectoryName target) |> ignore
        File.WriteAllText(target, f.Content, utf8NoBom)

    File.WriteAllText(Path.Combine(dir, "manifest.json"), manifestJson bundle, utf8NoBom)
