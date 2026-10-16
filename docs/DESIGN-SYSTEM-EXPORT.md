# Design-system export

`Fuaran.UI.Design` describes the Fuaran UI language as a design system: a directory of preview
cards, a DTCG token manifest and a content-hashed bundle manifest, generated from the language
itself. A design tool that ingests the bundle composes screens from Fuaran's own parts, so what
it produces is a composition a Fuaran UI tree can express, rather than a picture someone must
later translate.

```bash
fuaran design-system export --theme <manifest.json | default | dark | high-contrast> --out <dir> [--brand <css>]
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
  required, all read from the IDL.
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
2. The head links `fuaran-reference.css` from the bundle root and inlines the theme's `:root`
   custom-property block (`Theme.toCss`), then any manifest bridge and brand stylesheet, in that
   cascade order.
3. The body is rendered by `Fuaran.UI.Renderer.Server`: the same class vocabulary and markup a
   Fuaran host serves.

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

`--brand <css>` cascades a stylesheet of custom properties (typically `--fuaran-tone-*`) after the
theme in every card, and folds it into the role values, so the Colors card and
`theme.tokens.json` show the branded resolution. A brand stylesheet may not contain `</`; it is
inlined into a `<style>` element.

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
- every card's first line is the marker of its group.

The suite also proves each check can fail: removing a kind from the sweep, binding a role named
`#ff0000`, and rewriting a card's marker group each produce the named finding.
