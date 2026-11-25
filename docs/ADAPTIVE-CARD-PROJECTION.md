# Adaptive Card projection

`Fuaran.UI.Renderer.Server.AdaptiveCard` (Phase 1814) lowers a Fuaran tree to one
[Adaptive Card](https://adaptivecards.io). Adaptive Cards has the one thing this language cannot
build: renderers already shipped inside hosts we do not control (Teams, Outlook, Windows, Webex).
The projection reaches those hosts rather than competing for them. It is **lossy and declared**: the
tree is the larger language on almost every axis, so the content of this page is the degradation
rules, and every degradation is returned to the caller by node id.

It sits in the `Renderer.Server` family beside the [email projection](SSR.md) (Phase 441) and the
speech projection (Phase 1813), and follows their rule: what the target cannot express becomes a
labelled **open-live** link to the live surface, never a half-working control.

```fsharp
open Fuaran.UI.Renderer.Server

let ctx = Render.mkContextWithEgress policy Registry.empty sources tree

match AdaptiveCard.projectWith { AdaptiveCard.defaults with LiveUrl = Some "https://app.example/report" } ctx tree with
| Ok(card, degradations) -> send (AdaptiveCard.toJson card)     // a System.Text.Json.Nodes.JsonObject
| Error refusal -> log (AdaptiveCard.describeRefusal refusal)
```

`project ctx tree` is `projectWith AdaptiveCard.defaults ctx tree` (no live surface,
`HostLimits.card`); `projectStatic tree` also builds the default context.

## The target: schema 1.4, what both Teams and Outlook render

| Host | What it renders | Source |
|---|---|---|
| Teams | 1.5 (cards up to 1.6 accepted) | the Adaptive Cards designer's Teams host targets 1.5; Teams' card reference |
| Outlook (Actionable Messages) | 1.4, **without** `Action.Submit` and `Input.Time` | the designer's Outlook host targets 1.4; Outlook's Adaptive Card reference lists both members as "not displayed" |

So every projected card declares `"version": "1.4"`, and the projection never writes the two members
Outlook drops:

- **Actions run through `Action.Execute`** (Universal Actions, which both hosts route to the same
  bot), never `Action.Submit`.
- **A time-of-day field is an `Input.Text`** with the pattern `^([01][0-9]|2[0-3]):[0-5][0-9]$` and
  the placeholder `HH:MM`, declared as a substitution, never `Input.Time`.
- **A table is a `Container` of `ColumnSet` rows**, because `Table` is a 1.5 element.

Host-specific extensions (Teams charts, badge, rating, carousel) are out of this projection: a
projection only one host can read is a Teams projection. A later move to 1.5 is one constant
(`AdaptiveCard.SchemaVersion`), the schema it is validated against, and the rows above.

## Rules that hold for every kind

- **The card is inert.** Every binding resolves **once**, at projection time, through the shared
  `ServerRenderContext`, the same resolution the SSR document, the email digest and the speech script
  use, so a card cannot disagree with the page it links to. Interaction is a round trip:
  `Action.Execute` posts its verb and `data: { "fuaranNodeId": "<id>" }` to the backend that owns the
  tree, which answers with a new card.
- **A node whose `visible` resolves false is omitted with its subtree**, and reported.
- **Strings are literal.** Text is written as `TextRun`s inside `RichTextBlock`s, which hosts never
  read as markdown. A `TextBlock` is never written, because hosts read its text as markdown and a
  bound string could then author a link. A `Fact`'s title and value are the one place hosts apply
  markdown, so they are escaped (`AdaptiveCard.escapeFactText`): every character that can open a
  link, an autolink or emphasis is backslash-escaped, on one normalised line.
- **URLs pass the destination policy.** Every tree-authored URL goes through the context's
  `Sanitize.EgressPolicy` under its egress class (Phases 1026 / 1032): `Hyperlink` for links and
  navigation, `Media` for images, media and posters. A refused destination is never written; the
  node degrades and the degradation carries the log-safe reason (host and class, never the URL). A
  card host resolves nothing relative, so an allowed same-origin URL degrades too. The default
  context denies every non-local destination, so a decoded tree's images and links degrade until
  the host declares, with `Render.mkContextWithEgress`, where its card may point.
- **The live surface is the host's own declaration.** `CardOptions.LiveUrl` is supplied by the host
  in the same call, so, as in the email projection, it is not checked against the host's own policy.
  An open-live link is `<LiveUrl>#<node id>`. With no live surface declared, open-live is a labelled
  note ("available in the live view"), never a link that goes nowhere.
- **Ids.** A form's inputs are `<form id>.<field id>`, so two forms on one card cannot collide. A
  form's `Action.Execute` gathers inputs (`associatedInputs: "auto"`); a standalone button's does not
  (`"none"`).

## Limits: refused before any card JSON exists

`CardOptions.Limits` is a `HostLimits` (Phase 1817), `HostLimits.card` by default. A tree over it is
refused through `PreEmitValidate.validateWithLimits` before any card is built:
`ProjectionRefusal.HostLimitsExceeded` carries the limits' identity and each `FUARAN158`-`162`
finding exactly as the validator describes it. Then the projected card's **own** UTF-8 size is held
to the same `MaxSerializedBytes` (`ProjectionRefusal.CardPayloadExceeded`, code `FUARAN162`): the
card and the tree are not the same bytes, and the card is usually the larger. No new ceiling was
needed; `DataGrid`'s row ceiling is the limits' `MaxGridRows`.

## The mapping table

One row per canonical wire kind. The machine-readable form is `RenderFidelity.cardRulings`,
published as the `adaptiveCard` column of `render-fidelity.json`; a test holds this table to it row
for row, and the projection's match on `NodeKind` is exhaustive, so a new kind cannot ship without a
ruling.

| Kind | Class | Card elements | What happens |
|---|---|---|---|
| `Badge` | `flatten-to-text` | RichTextBlock | the label as a bold TextRun coloured by the variant; the pill shape has no card form |
| `Box` | `structural` | Container, ColumnSet | a horizontal flex row is a ColumnSet; a grid or masonry layout is a Container of ColumnSet rows; a Card gains the emphasis style and its heading |
| `Button` | `mapped` | ActionSet, RichTextBlock | one action: Navigate to an allowed absolute URL is Action.OpenUrl, a backend action is Action.Execute, a client-only action is open-live; a disabled button is a note |
| `Callout` | `mapped` | Container | styled by tone, holding the heading (bold) and the body |
| `Chart` | `open-live` | ActionSet, RichTextBlock | drawn as SVG, which neither host renders in a card image; the title is the link text |
| `CodeBlock` | `flatten-to-text` | RichTextBlock | the source as one monospace TextRun |
| `Custom` | `open-live` | ActionSet, RichTextBlock | a host renderer's output is outside this projection's audit |
| `DataGrid` | `mapped` | Container, ActionSet, RichTextBlock | the staticRows form is a Container of ColumnSet rows under a bold header row; the client-library form is open-live |
| `Disclosure` | `structural` | Container | always expanded: the heading, then the children |
| `Drawing` | `open-live` | ActionSet, RichTextBlock | inline SVG, as Chart |
| `Embed` | `open-live` | ActionSet, RichTextBlock | a third-party browsing context has no card form |
| `ErrorBoundary` | `structural` | (its child's) | the protected child projects; the fallback is reported as not taken |
| `Fact` | `mapped` | FactSet, RichTextBlock | a FactSet row; adjacent Facts and LabelValueRows share one FactSet; help follows as a subtle line |
| `FileUpload` | `open-live` | ActionSet, RichTextBlock | a card cannot carry a file |
| `Filters` | `open-live` | ActionSet, RichTextBlock | filters re-query a live view |
| `Form` | `mapped` | Container, ActionSet, RichTextBlock | Input elements then an ActionSet whose Action.Execute submits them (fields below) |
| `FragmentDecl` | `omitted` | none | a template declaration paints nothing |
| `FragmentRef` | `structural` | RichTextBlock | expanded against the fragment registry; an unresolved reference is a labelled note |
| `Heading` | `mapped` | RichTextBlock | a bold TextRun sized by level (1 extraLarge, 2 large, 3 medium, 4-6 default) |
| `Icon` | `omitted` | none | a card has no icon font; reported, not drawn |
| `Image` | `mapped` | Image, RichTextBlock | an Image with the alt as altText, then the caption; a refused or relative source degrades to the alt text |
| `LabelValueRow` | `mapped` | FactSet, RichTextBlock | a FactSet row, the value formatted through the shared projection |
| `Link` | `mapped` | RichTextBlock | a TextRun whose selectAction is Action.OpenUrl; a refused or relative destination is plain text |
| `List` | `flatten-to-text` | RichTextBlock | one literal line per item, numbered or bulleted |
| `Map` | `open-live` | ActionSet, RichTextBlock | a client map library draws it |
| `Markdown` | `mapped` | RichTextBlock | the one deterministic GFM render, one RichTextBlock per block; bold, italic, strikethrough and code become TextRun styles; an allowed absolute link keeps an Action.OpenUrl; an image becomes its alt text |
| `Math` | `flatten-to-text` | RichTextBlock | the LaTeX source in a monospace TextRun |
| `Media` | `mapped` | Media, ActionSet, RichTextBlock | a Media element when the source is an allowed absolute URL whose extension names a MIME type; otherwise open-live |
| `Metric` | `mapped` | Container | the label (subtle), the value (large, bold), the trend and comparisons, the subtext |
| `Modal` | `open-live` | ActionSet, RichTextBlock | an overlay has no meaning in a card |
| `Mount` | `open-live` | ActionSet, RichTextBlock | the guest tree attaches client-side |
| `Progress` | `flatten-to-text` | RichTextBlock | `<label>: <n>%`, or `in progress` |
| `ScrollArea` | `structural` | Container | a card has no clipping |
| `Select` | `open-live` | ActionSet, RichTextBlock | outside a Form there is no submit to carry it |
| `Skeleton` | `omitted` | none | a loading state a delivered card is never in |
| `Sparkline` | `open-live` | ActionSet, RichTextBlock | drawn client-side |
| `SplitPanel` | `structural` | ColumnSet | two columns at the declared weights |
| `Stepper` | `open-live` | ActionSet, RichTextBlock | step navigation needs the runtime |
| `SummaryList` | `structural` | Container | the heading, then the children, whose adjacent fact rows share one FactSet |
| `Switch` | `structural` | (its case's) | the selected case; the others are reported as not taken |
| `Tabs` | `open-live` | ActionSet, RichTextBlock | projecting only the active panel would misstate how much the section holds |
| `Text` | `mapped` | RichTextBlock | one literal TextRun; a TextRun is never parsed, so the words need no escape |
| `Toast` | `flatten-to-text` | Container | an open toast is a toned Container holding the message; a closed one is omitted and reported |
| `Tree` | `open-live` | ActionSet, RichTextBlock | a card can toggle nothing |

The shard's third ruling, **refuse**, is held for whole trees rather than kinds: no kind needs it
today, because every kind the card cannot carry has an honest open-live or text form. A tree is
refused only over its host limits (above).

### Form fields

| Field kind | Card input | Declared? |
|---|---|---|
| Text | `Input.Text` | |
| TextArea | `Input.Text` (`isMultiline`) | |
| Number, RangedNumber | `Input.Number` (`min`, `max`) | |
| Range | `Input.Number` (`min`, `max`) | substituted: a slider becomes a number box |
| Checkbox, Toggle | `Input.Toggle` (`valueOn` `true`, `valueOff` `false`) | |
| Choice | `Input.ChoiceSet` (compact) | |
| SegmentedChoice | `Input.ChoiceSet` (expanded) | |
| Combobox | `Input.ChoiceSet` (compact) | substituted when it allows free text |
| Tokens | `Input.ChoiceSet` (multi-select) over the suggestions, else `Input.Text` | substituted |
| Rating | `Input.ChoiceSet` (expanded scale, half steps when allowed) | substituted |
| Color | `Input.Text` (`#rrggbb` pattern) | substituted |
| DateTime, `Date` variant | `Input.Date` | |
| DateTime, `Time` variant | `Input.Text` (`HH:MM` pattern) | substituted (Outlook does not display `Input.Time`) |
| DateTime, `DateTime` variant | `Input.Date` + `Input.Text` (`HH:MM`), ids `.date` / `.time` | substituted |
| DateTimeRange | the variant's inputs twice, ids `.start` / `.end` | substituted |

A field's validation `Rule` has no 1.4 counterpart and is declared (`field-rule-not-carried`); the
backend that receives the `Action.Execute` re-validates, as it must for any input. `required` maps to
`isRequired`, and help text follows the input as a subtle line.

### Actions

| Tree action | Card action |
|---|---|
| `Navigate` to an allowed absolute URL | `Action.OpenUrl` |
| `Invoke` | `Action.Execute`, verb = the capability id |
| `Call`, `SetState`, `CommitLocal`, `Notify`, `AiTool` | `Action.Execute`, verb `fuaran.<case>` (`fuaran.setState`, ...) |
| `Chain` of backend actions | `Action.Execute`, verb `fuaran.chain` |
| `WriteToClipboard`, `Print`, `Focus`, `ReadFileBody`, `Confirm`, `Dispatch`, a refused or relative `Navigate` | not projectable: the control is open-live |

Every `Action.Execute` is declared (`action-as-execute`, naming the case and the verb): the action
no longer runs in the client, it runs where the backend runs it.

## Degradations

`Degradation = { NodeId; Kind; Rule; DecidedAt }`. `DecidedAt` is the node whose state or ruling
caused it (the node itself, or the ancestor that decided for its subtree). Rules, by their stable
token (`AdaptiveCard.ruleId`): `open-live`, `inside-open-live`, `flattened-to-text`, `omitted`,
`not-visible`, `not-taken`, `closed`, `always-expanded`, `layout-approximated`, `input-substituted`,
`field-rule-not-carried`, `action-as-execute`, `client-only-action`, `destination-refused`,
`relative-destination`, `media-type-unknown`, `markdown-image-as-text`, `disabled`,
`unresolved-fragment`, `depth-exceeded`.

## How it is held

`Fuaran.UI.Renderer.Server.Tests/AdaptiveCardProjectionTests.fs`:

- Every card is validated against the **published** Adaptive Cards 1.4 JSON Schema, vendored byte for
  byte under `adaptive-card-schema/` with its MIT licence and source commit, by an off-the-shelf
  validator that is first shown to refuse a 1.5 `Table` and a misspelled member.
- Every corpus node fixture is projected, and its card validated; each root emits only the elements
  its ruling declares, and an open-live, omitted or flattened root appears in the degradation list.
- The ruling covers every fidelity row, this page's table is the ruling row for row, and the corpus
  exercises every ruled kind.
- Refusals carry the 1817 codes; strings cannot author links; refused and relative destinations are
  never written.

The scripted half stops at the schema. Whether a card **renders** in the Adaptive Cards Designer's
Teams and Outlook hosts is a browser check an operator performs.
