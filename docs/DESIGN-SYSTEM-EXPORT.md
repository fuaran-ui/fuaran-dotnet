# Design-system export

`Fuaran.UI.Design` describes the Fuaran UI language as a design system: a directory of preview
cards, a DTCG token manifest and a content-hashed bundle manifest, generated from the language
itself. A design tool that ingests the bundle composes screens from Fuaran's own parts, so what
it produces is a composition a Fuaran UI tree can express, rather than a picture someone must
later translate.

```bash
fuaran design-system export --theme <manifest.json | default | dark | high-contrast> --out <dir> [--brand <css>] [--locale <bcp47>]
```

The verb builds the bundle, runs its checks, writes it only when they hold, and prints the card
count and the content hash. The library entry points are `Bundle.build`, `Bundle.check` and
`Bundle.write` (see the package README).

## What the bundle is

| File | What it holds |
|---|---|
| `manifest.json` | `kind: "fuaranDesignSystemBundle"`, `formatVersion`, the `contentHash`, the IDL `version` and kind count the cards came from, the cards by group, the colour roles, and every file with its SHA-256 |
| `theme.tokens.json` | The theme as a DTCG manifest (`Fuaran.UI.ThemeManifest`), including the role bindings |
| `fuaran-reference.css` | The canonical class stylesheet; every card links it |
| `README.md` | An authoring note per group |
| `cards/<group>/<name>.html` | One preview card per part |

There are five groups:

- **Components**: one card per kind the IDL declares (`src/Fuaran.UI.Idl/idl.json`). Each card
  shows the kind's representatives swept over the style axes one at a time: the baseline, every
  other tone, every other weight, every other emphasis. The representatives are the component
  catalog's own matrix (`samples/catalog/Matrix.fs`, compile-linked) plus a representative for
  each kind the catalog does not show. The note on each card says what the kind is for, which
  fields bind to data (`Binding` and `TextSource` fields), which hold children, and which are
  required, all read from the IDL. What the sweep shows is the subject of rule 4 of the card
  contract below.
- **Fragments**: one card per certified stdlib fragment (`Fuaran.UI.Fragments`), with its holes,
  their value spaces and whether each is required, and the fragment's example applied.
- **Type**, **Spacing**, **Colors**: one card each, from the chosen `Theme`.

The content hash is SHA-256 over one line per file, in ordinal path order, of
`<path> <sha256 of the file>`, with `manifest.json` excluded since it carries the hash. The
build is deterministic, so two exports of one theme and brand print the same hash, and any
change to a card, token or note moves it.

## The card contract

A card is a self-contained HTML document:

1. **The first line is the marker** `<!-- @dsCard group="<Group>" -->`, the group one of `Type`,
   `Colors`, `Spacing`, `Components`, `Fragments`. A consumer can classify a card from that line
   alone.
2. The head cascades in this order: a link to `fuaran-reference.css` at the bundle root; a
   `<style>` with the theme's `:root` custom-property block (`Theme.toCss`) and any manifest
   bridge; a `<style>` with the card's own layout scaffolding, which sets no `font-family`; and,
   when a brand is given, the brand stylesheet in a `<style>` of its own, last (Phase 2339).
3. The body is rendered by `Fuaran.UI.Renderer.Server`: the same class vocabulary and markup a
   Fuaran host serves.
4. **A Components card shows what its captions say** (Phase 2468):
   - **Each axis is set where the kind reads it.** A kind whose IDL spec declares a field of type
     `ToneVariant`, `StyleWeight` or `Emphasis` (Callout's `tone`, Metric's `tone`, `weight` and
     `emphasis`, and so on) takes that axis on its spec; any other axis goes on the node style.
     The list is read from the IDL, so a kind that gains a spec tone is swept through it with no
     edit to the bundle.
   - **A variant that would repeat its baseline is left out, and the card says so.** The node
     style reaches the markup only as the wrapper's hook classes (`fuaran-tone-*`,
     `fuaran-weight-*`, `fuaran-emphasis-*`), which the reference stylesheet deliberately leaves
     for a host to bind. So a variant is shown only when its markup, with its node ids masked and
     the hook classes no stylesheet in the card's cascade paints removed, differs from the
     baseline's. A brand that binds `.fuaran-tone-*` brings those variants back. The axes left out
     are named under the representative ("Not shown: Weight, Emphasis.").
   - **Every text slot has text.** The catalog binds a few labels through `Binding.I18n` so its
     locale switcher can retranslate them; the bundle resolves those keys in English.
   - **No part is shown as a hydration placeholder.** A client-library `DataGrid` is previewed as
     the static read-only table the server renderer emits for the same rows (cell text as the
     first-party grid formats it), and a `Map` as the table of its markers, each captioned as a
     preview. The interactive part renders client-side.
   - Each variant is a `<figure class="ds-variant" data-ds-specimen="<representative>">` whose
     rendered part sits between `<!--ds-render-->` and `<!--/ds-render-->`; the first figure of a
     representative is its baseline.

The stylesheet is linked rather than inlined in every card because it is the same bytes in each,
and a bundle of fifty copies of it would be most of the bundle. A consumer that ingests cards one
at a time resolves the link against the bundle root.

## Roles, not hex

The Colors group offers **roles**: the seven tones (`Default`, `Subdued`, `Brand`, `Success`,
`Warning`, `Critical`, `Info`) and any named roles the theme manifest binds (`page-surface`,
`body-text` and the like). The resolved value of each role appears on the Colors card as a
reference, and is never offered as a token.

The reason is what a design is for. A Fuaran UI node carries a tone, not a colour; the theme
resolves the tone. A design that picked `#1d4ed8` cannot be expressed by a tree without inventing
a colour the language does not have, and it would not follow a theme change, a dark mode or a
brand. A design that picked `Brand` lowers to a tree directly and follows all three.

So the bundle refuses the opposite: `Bundle.check` reports any Colors token whose **name** is a
literal colour (a `#` value, a colour function such as `rgb(…)` or `oklch(…)`, or a CSS colour
name). A theme manifest that names a role after its value fails the export.

Type and Spacing follow the same rule for the same reason. A design chooses a heading level or a
weight (Compact, Standard, Spacious), and the theme maps it onto the scale; the scale values are
shown for reference.

## Themes and brands

`--theme default | dark | high-contrast` selects `Defaults.theme` or one of the sample themes in
`samples/themes/`. `--theme <file>` reads a theme manifest (DTCG with Fuaran role extensions):
its role bindings become the Colors roles, and its tokens are bridged into every card with
literal values (`ThemeBridge.emitCss`), over the default theme. Tones the manifest does not bind
are resolved from the theme.

`--brand <css>` cascades a stylesheet after the theme and the card scaffolding in every card, and
folds its custom properties (typically `--fuaran-tone-*`) into the role values, so the Colors card
and `theme.tokens.json` show the branded resolution. Because it comes last, its rules win at equal
specificity, so a brand can set the typeface with `:root { font-family: … }`. Because it is alone
in its `<style>` element, it may begin with `@import` to load a web font. A brand stylesheet may
not contain `</`; it is inlined into a `<style>` element.

## Figures and locales

A figure has two routes, and the Metric, LabelValueRow, Fact and DataGrid cards name both. A figure
kind's `Format` (a Metric's, a LabelValueRow's, a DataGrid column's) is a `CellFormat`, which is
locale-free so every host renders the same text: `CellFormat.Currency "GBP"` reads `GBP 142500.00`
and `Number (Some 0)` reads `3921`, ungrouped. A figure a reader reads, grouped and symbol-led in
their locale, is `Binding.Format` with a `LocaleSource`. It yields text, so it fits a text slot (a
Fact's value, a subtext, a heading) and never a figure kind's numeric value.

`--locale <bcp47>` exports the bundle for a reader's locale. The Fact card adds a specimen whose
value is the example figure through `Binding.Format` under that locale (`£142,500.00` under
`en-GB`), the other three cards say what the figure reads there, and the README names the locale.
Nothing else changes: the figure kinds' own examples keep the locale-free text the screen renders,
because their value cannot carry a localised figure. Without `--locale` there is no locale
specimen and no locale line. A tag the platform does not know is refused (exit 2) rather than
formatted under the invariant culture.

## How a consumer reads it

1. Read `manifest.json`. Check `kind` and `formatVersion`, and note `idl.version`: a bundle names
   the vocabulary its cards were rendered from.
2. Classify each card by its marker line, or by `groups` in the manifest.
3. Offer the Components and Fragments cards as the parts to compose with, the Colors roles as
   the only colour tokens, and Type and Spacing as the scale a level or weight maps onto.
4. Use `contentHash` to tell whether a bundle changed since the last ingest.

A consumer that needs another card shape adapts the bundle; the bundle does not change shape for
a consumer.

## The checks

`Bundle.check` is run by the verb before anything is written, and by
`src/Fuaran.UI.Design.Tests` over the default, Dark and High-Contrast themes:

- every IDL kind has a Components card;
- every stdlib fragment has a Fragments card;
- exactly one Type, Colors and Spacing card;
- no Colors token is a literal colour;
- every card's first line is the marker of its group;
- every swept variant on a Components card differs from its representative's baseline in what
  the card's cascade paints (rule 4);
- no example renders an empty label, heading, body or title slot, an empty button, an unresolved
  `[i18n:<key>]`, or a server hydration placeholder.

The suite also proves each check can fail: removing a kind from the sweep, binding a role named
`#ff0000`, rewriting a card's marker group, repeating a baseline as a variant, a Callout with a
blank heading, a Metric label bound to a key with no text, Markdown showing an unresolved key, and
a grid bound to rows the example does not hold each produce the named finding.

## The design-provider seam

The bundle is what a design tool composes FROM. The seam is how the tool is driven and what it
hands back, and it is specified as data so that every tool is held to the same contract.
`Fuaran.UI.Design.Seam` carries it: FSharp.Core and `Fuaran.Core.Wire` only, Fable-portable, and
referenced by this package. The normative text is `DESIGN_SEAM.md` in the wire-format corpus, with
the `design/` fixture family beside it.

Two boundaries define it. **The provider never sees a Fuaran UI tree**: it receives a bundle and
a brief and returns screens. **The lowering never sees the provider**: it reads a result, whoever
produced it.

### The records

| Record | What it carries |
|---|---|
| `DesignSystemRef` | The bundle a provider holds: `providerId`, optional `projectId`, the bundle's `bundleHash` (its `ContentHash`) and `idlVersion` (its `IdlVersion`) |
| `DesignBrief` | The module's name and optional purpose; its data shapes (name, type, cardinality `One` / `Optional` / `Many`); the user tasks; the interactions; the viewports (name, width) and optional density (`Compact` / `Standard` / `Spacious`); the fragments it may use; the `DesignSystemRef` |
| `DesignRequest` | The brief's hash, the reference, a state (`Manual`, `Pending`, `Ready`) and an optional provider handle |
| `DesignResult` | The screens as named files (path, media type, hash), optional per-screen component inventories naming the bundle's cards, the reference, and the result's content hash |

The interactions are a closed vocabulary, because each must lower to a construct the language has:
`Filter` to a filter binding written by a `Filters` node, `Select` to a selection binding, `Drill`
to a navigation carrying the selected key, `Compare` to a selection over several items read by a
second view, `Export` to the invocation of a host capability, `Navigate` to a navigation. A brief
asking for anything else is refused when it is read.

Every hash is `sha256:` and 64 lowercase hex digits. A brief's hash is over its canonical bytes
(`SeamCodec.encodeBrief`); a result's content hash is over one `<path> <hash>\n` line per file in
ordinal path order, the rule the bundle's own content hash uses.

### The provider contract

```fsharp
type IDesignProvider =
    abstract ProviderId: string
    abstract PublishDesignSystem: bundleDirectory: string * bundleHash: string -> Async<Result<DesignSystemRef, string>>
    abstract RequestDesign: brief: DesignBrief -> Async<Result<DesignRequest, string>>
    abstract FetchDesign: request: DesignRequest -> Async<Result<DesignResult option, string>>
```

A provider receives a written bundle and answers with the reference designs are made against;
receives a brief and answers with the request it holds; and, asked for a request's design, answers
with the result, or `None` while the design is not ready. A `Manual` request is one the person
designs interactively, and the result is retrieved from the tool's declared export. This repository
declares the interface and implements it nowhere.

A result answers its request only when both name the same `bundleHash` (`SeamCodec.checkExchange`).
`DesignResult.unknownComponents` names any annotated component that is not a card of a given
bundle.

### How a provider is certified

`SeamCodec` writes canonical bytes and reads strictly on the records (a missing or unknown member,
a value outside a vocabulary, a malformed hash are each refused with a code and a path) and
leniently on annotations (an unknown member of an annotation is evidence, and is ignored).

A provider is certified by the corpus, not by resembling another provider:

1. every document it produces decodes, and re-encodes byte-identically, through `SeamCodec`; and
2. a reader of its output passes the `design/` family: each `design-seam-round-trip` vector
   re-encodes to its expected bytes and each `design-seam-reject` vector refuses with exactly its
   code at exactly its path (`SeamCodec.reencode decoder text` is the entry point the corpus names).

`src/Fuaran.UI.Design.Tests` runs both legs on this host, and the family is written by
`--emit-corpus`, which proves every vector before writing it.
