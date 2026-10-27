# Fuaran.UI.Design

Fuaran UI described as a design system. One call renders the language's own parts into a
directory of preview cards a design-system tool can ingest, beside the theme as a DTCG
manifest:

- a **Components** card per kind the IDL declares, swept over the tone, weight and emphasis
  axes one axis at a time, each with a note on what the kind is for, which fields bind to
  data and which hold children. Each axis is set where the kind reads it (its spec's own
  field when the IDL declares one, the node style otherwise), and a variant that would only
  repeat the baseline is left out and named;
- a **Fragments** card per certified stdlib fragment (`Fuaran.UI.Fragments`), with its holes
  named and an applied example;
- one **Type**, one **Spacing** and one **Colors** card from the chosen `Theme`.

Colours are offered as **roles** (the seven tones and the theme manifest's named roles),
never as literal values. A design composed from the bundle is a composition of parts a
Fuaran UI tree can express.

```fsharp
open Fuaran.UI
open Fuaran.UI.Design

let bundle = Bundle.build Defaults.theme None (Bundle.BundleOptions.defaults ())

match Bundle.check bundle with
| [] ->
    Bundle.write bundle "./design-system"
    printfn "%s" bundle.ContentHash
| findings -> findings |> List.iter (printfn "%s")
```

The same from the command line, through the `fuaran` dotnet tool (`Fuaran.UI.Cli`):

```bash
fuaran design-system export --theme default --out ./design-system
fuaran design-system export --theme theme.manifest.json --brand brand.css --out ./design-system
```

The brand stylesheet is placed last in every card, in a `<style>` element of its own, so it can set the
typeface (`:root { font-family: … }`) and begin with an `@import` that loads a web font.

## The bundle

```
manifest.json          file list with hashes, cards by group, colour roles, content hash, IDL version
theme.tokens.json      the theme as a DTCG manifest (Fuaran.UI.ThemeManifest)
fuaran-reference.css   the canonical class stylesheet every card links
README.md              an authoring note per group
cards/type/type.html
cards/colors/colors.html
cards/spacing/spacing.html
cards/components/<Kind>.html
cards/fragments/<fragment>.html
```

Every card's first line is `<!-- @dsCard group="…" -->`, the group one of `Type`, `Colors`,
`Spacing`, `Components` or `Fragments`. The rest is a self-contained HTML document rendered by
`Fuaran.UI.Renderer.Server`, with the theme's `:root` variable block inlined.

Figures have two routes, and the figure kinds' cards name both: a `CellFormat` (a Metric's,
LabelValueRow's or DataGrid column's `Format`) is locale-free, so every host renders the same text;
`Binding.Format` with a locale is the reader-facing figure, and yields text for a text slot.
`BundleOptions.Locale = Some "<bcp47>"` (the CLI's `--locale`) adds a Fact specimen whose value is a
figure formatted under that locale; `None` adds nothing. A tag the platform does not know is refused.

The content hash is SHA-256 over the sorted file set (every file except `manifest.json`), so two
exports of one theme with one brand print the same hash.

## The checks

`Bundle.check` returns the bundle's contract violations, empty when it holds:

- every kind in the IDL has a Components card (the kind set is read from the IDL, so a kind
  admitted there with no specimen is a finding, not a stale bundle);
- every stdlib fragment has a Fragments card;
- there is exactly one Type, Colors and Spacing card;
- no Colors token is a literal colour (a hex value, a colour function or a CSS colour name);
- every card's first line is the marker of its group;
- every swept variant differs from its baseline in what the card's stylesheets paint;
- no example renders an empty text slot, an unresolved `[i18n:<key>]` or a server hydration
  placeholder (a client-library grid is previewed as the static table of the same rows).

See [`docs/DESIGN-SYSTEM-EXPORT.md`](https://github.com/fuaran-ui/fuaran-dotnet/blob/main/docs/DESIGN-SYSTEM-EXPORT.md)
for the card contract and how a consumer reads the bundle.

## The design-provider seam

A design tool sits behind the seam in `Fuaran.UI.Design.Seam`, which this package references:
the records a tool and the language exchange (`DesignSystemRef`, `DesignBrief`, `DesignRequest`,
`DesignResult`), their codec and the `IDesignProvider` interface. A reference names a bundle by
its `ContentHash` and `IdlVersion`; a result's component inventory names cards of that bundle.
They live in their own package because they are Fable-portable and this one is not.

.NET only: it renders through the server renderer. Apache-2.0 licensed.
