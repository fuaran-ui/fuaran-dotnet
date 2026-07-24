# `Box` container unification – design note (Phase 390)

**Status:** design – pending operator checkpoint (objective *language-settled*, checkpoint review).
**Stability impact:** breaking (`NodeKind`/`LayoutKind` case removals + wire-format change → major bump).
**Window:** must land before public launch and before the full-breadth schema-driven codegen migration,
so the IDL migrates the *merged* vocabulary, not the pre-merge one.

This note is the Task-1 deliverable: the `BoxSpec` design + the §4b design-doc amendment draft (to be
landed in the canonical language design doc by the operator – a cross-estate edit) + the legacy
decode-upgrade mapping + the phased implementation plan across the three conformant hosts. It is the
decision the *language-settled* objective exists to settle before the corpus is regenerated.

## Why

The minimalist-vocabulary decision keeps the closed `NodeKind` DU as the language's sole vocabulary.
The cost of a closed set is that **valid-but-wrong-kind** selection becomes the error class that grows
with the set. `Stack` / `GridLayout` / `Dashboard` / `Card` are the biggest near-synonym confusion
cluster: four container kinds that differ only in arrangement and chrome, so an author (human or LLM)
can pick a defensible-but-non-canonical one and the eval's convergence detection frays. **Kind-merging**
collapses the cluster to one kind while keeping the vocabulary closed and grammar-constrainable – one
obvious emission per pattern (the canonicality property the eval assertions lean on).

## The four retired specs (union to absorb)

| Retired kind | Spec fields (today) | Distinguishing intent |
|---|---|---|
| `Stack` | `Orientation`, `Wrap`, `Children` | flex row/column, optional wrap |
| `GridLayout` | `Cols`, `TemplateColumns: string option`, `Children` | N-column grid or verbatim template |
| `Dashboard` | `Children` | responsive auto-tile region |
| `Card` | `Heading: TextSource option`, `Children` | bordered chrome + optional heading |

Two leaf display kinds sit adjacent to the cluster and are named parenthetically by the phase:

| Leaf kind | Spec fields | Note |
|---|---|---|
| `Spacer` (Display) | `Size: SpacerSize` | empty, no children |
| `Divider` (Display) | `Orientation`, `Label: TextSource option` | rule/separator, no children |

## The design

One container primitive whose **layout mode** names how children are arranged and whose **semantic
role** names what the container *means* (driving the HTML element, the ARIA landmark, and the
`fuaran-*` chrome classes). The role – not the kind – carries the semantics the retired kinds encoded.

```fsharp
/// §4b (Phase 390) — the unified container primitive. Absorbs the retired
/// Stack / GridLayout / Dashboard / Card near-synonyms. Layout mode = how
/// children arrange; Role = what the container means (HTML element + ARIA
/// landmark + fuaran-* chrome). One obvious emission per pattern.
and BoxSpec<'Msg> =
    { Layout: BoxLayout
      Role: BoxRole
      /// Optional container heading (the retired Card heading). Emitted as the
      /// card/section header when Some; None for a plain group.
      Heading: TextSource option
      Children: Node<'Msg> list }

/// How a Box arranges its children.
and [<RequireQualifiedAccess>] BoxLayout =
    /// Flex flow — the retired Stack. Direction = main axis; Wrap allows
    /// children to wrap at narrow widths (retired StackSpec.Wrap). Gap is the
    /// canonical inter-child spacing control (None ⇒ omitted on the wire ⇒
    /// byte-identical for existing trees); it is the mechanism that obsoletes
    /// the Spacer node (see open question 1).
    | Flex of FlexLayout
    /// Explicit grid — the retired GridLayout. Cols fixed count, or
    /// TemplateColumns verbatim grid-template-columns (Some ⇒ Cols ignored).
    | Grid of GridTemplate
    /// Responsive auto-tile — the retired Dashboard's defining behaviour.
    /// The renderer owns the tiling via the fuaran-dashboard class; no
    /// author-supplied column count.
    | Auto

and FlexLayout = { Direction: Orientation; Wrap: bool; Gap: int option }
and GridTemplate = { Cols: int; TemplateColumns: string option; Gap: int option }

/// What a Box means — drives the emitted element, ARIA landmark, and chrome.
and [<RequireQualifiedAccess>] BoxRole =
    /// Plain grouping container — a bare <div>, no landmark. Retired
    /// Stack / GridLayout default.
    | Group
    /// Card chrome — <section class="fuaran-card"> with optional heading.
    /// Retired Card.
    | Card
    /// Dashboard region — landmark <section>/role="region" with the
    /// auto-tiling fuaran-dashboard class. Retired Dashboard.
    | Dashboard
    /// Separator — <hr>/role="separator" (optional centred Heading = the
    /// retired DividerSpec.Label; Layout direction = orientation; Children = []).
    /// RESERVED this phase (no encoder emits it yet); it is the forward-compat
    /// slot for the Divider retirement (open question 1) so BoxRole does not
    /// need a second breaking wire change later.
    | Separator
```

The `Gap` field and the `Separator` role are **reserved forward-compatibility slots**: `Gap` defaults
to `None` (omitted on the wire, byte-identical), and no encoder emits `Separator` in this phase. They
exist so the later Spacer/Divider retirement (open question 1) is a pure follow-up that never has to
re-cut `BoxLayout`/`BoxRole` – a second breaking wire change the pre-launch window would not forgive
(charter §4.2: retiring a kind post-1.0 is a `/v2/` major).

`Layout` and `Role` are orthogonal by construction, but the retired kinds only ever occupied four of
the cells – those four are the canonical corners the smart constructors and the legacy-upgrade seam
target. The rest of the product space is reachable but is not what any retired kind emitted, so the
validator advises (not rejects) on the off-corner combinations.

### Authoring surface is preserved (call sites compile unchanged)

The author-facing spec records **survive** as smart-constructor inputs; only the `LayoutKind` DU cases
retire. Each smart constructor becomes a thin `Box`-emitter:

```fsharp
let stack id (s: StackSpec<'Msg>) =
    box id { Layout = BoxLayout.Flex { Direction = s.Orientation; Wrap = s.Wrap }
             Role = BoxRole.Group; Heading = None; Children = s.Children }

let gridLayout id (s: GridLayoutSpec<'Msg>) =
    box id { Layout = BoxLayout.Grid { Cols = s.Cols; TemplateColumns = s.TemplateColumns }
             Role = BoxRole.Group; Heading = None; Children = s.Children }

let dashboard id (s: DashboardSpec<'Msg>) =
    box id { Layout = BoxLayout.Auto; Role = BoxRole.Dashboard; Heading = None; Children = s.Children }

let card id (s: CardSpec<'Msg>) =
    box id { Layout = BoxLayout.Flex { Direction = Vertical; Wrap = false }
             Role = BoxRole.Card; Heading = s.Heading; Children = s.Children }
```

So `Fuaran.stack` / `Fuaran.card` / `Fuaran.dashboard` / `Fuaran.grid` call sites, `Defaults.*`, and
in-tree samples move **not at all** – the consolidation is entirely below the authoring surface, on
the wire. (The wire `"kind"` changes from four tags to `"Box"`; that is the breaking part.)

### Legacy decode-upgrade seam (Phase 255 precedent)

Decoders keep accepting the four retired tags and upgrade each to the equivalent `Box` on read, so
existing op-streams and permalinks replay to the same rendered output. One corpus fixture pins each
upgrade (decode-only – a legacy tag never re-encodes to its old form; it round-trips as `Box`).

| Legacy `"kind"` | Upgrades to `Box` with |
|---|---|
| `Stack` | `Layout=Flex{Direction=Orientation, Wrap}`, `Role=Group`, `Heading=None` |
| `GridLayout` | `Layout=Grid{Cols, TemplateColumns}`, `Role=Group`, `Heading=None` |
| `Dashboard` | `Layout=Auto`, `Role=Dashboard`, `Heading=None` |
| `Card` | `Layout=Flex{Vertical, false}`, `Role=Card`, `Heading` |

### Canonical wire shape (byte-exact – the three-host contract)

The discriminator key is `$type` (spliced first; all other keys Ordinal-sorted). `Box` encodes as:

```json
{"$type":"Box","children":[…],"heading":<TextSource?>,"layout":{…},"role":"Group|Card|Dashboard|Separator"}
```

`heading` is emitted only when `Some` (Card). `layout` is a discriminated object:

- `{"$type":"Flex","direction":"Vertical|Horizontal","gap":<int?>,"wrap":<bool>}` – `gap` omitted when `None`.
- `{"$type":"Grid","cols":<int>,"gap":<int?>,"templateColumns":<string?>}` – `gap`/`templateColumns` omitted when `None`.
- `{"$type":"Auto"}`.

Byte-exact examples (the four canonical corners):

| Retired author | Canonical `Box` wire |
|---|---|
| `stack{Vertical,wrap=false}` | `{"$type":"Box","children":[…],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}` |
| `gridLayout{cols=2}` | `{"$type":"Box","children":[…],"layout":{"$type":"Grid","cols":2},"role":"Group"}` |
| `dashboard{}` | `{"$type":"Box","children":[…],"layout":{"$type":"Auto"},"role":"Dashboard"}` |
| `card{heading}` | `{"$type":"Box","children":[…],"heading":<TextSource>,"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Card"}` |

Legacy decoders additionally accept `$type` ∈ {`Stack`,`GridLayout`,`Dashboard`,`Card`} and upgrade each
to the corresponding `Box` on read (re-encoding as `Box` – a legacy tag never round-trips to its old form).

### Role-driven semantic emission (byte-comparable HTML/a11y)

The renderer emits each retired kind's HTML element + `fuaran-*` class hooks from `Role`, so theming
and a11y snapshots are byte-comparable pre/post:

- `Group` → `<div class="fuaran-box …">` (+ `fuaran-stack`/`fuaran-grid` layout class from `Layout`).
- `Card` → `<section class="fuaran-card">` + heading `<h*>` when `Heading = Some`.
- `Dashboard` → landmark `<section class="fuaran-dashboard">` (`role="region"`), auto-tile via class.

The `Layout` mode supplies the arrangement class (`fuaran-stack` flex / `fuaran-grid` +
`grid-template-columns` / `fuaran-dashboard` auto-tile); the `Role` supplies the chrome + landmark.
Reference CSS keeps the retired class names so no stylesheet churn escapes this change-set.

## Open questions for the checkpoint

1. **`Spacer` / `Divider` – the best long-term solution (charter-grounded).** The operator asked for
   the best *long-term* answer, not a scope pick. The vocabulary charter (`VOCABULARY.md`) settles the
   direction: both should ultimately **leave the top-level kind set**, because both are expressible
   without a distinct kind – the charter's canonicality thesis (one obvious emission per pattern) plus:
   - **`Divider` → a `Box` `Separator` role.** Charter §1.2 *names "separator" as a canonical `Box`
     role* – a separator is not a new/independent kind, it is a `Box` role. `DividerSpec.Orientation`
     → the box's `Layout` direction; `DividerSpec.Label` → the box's `Heading`; `Children = []`. This
     is the charter's own ruling applied to an existing leaf, and it shrinks the set by one.
   - **`Spacer` → deprecate in favour of container `Gap`.** A `Spacer` node is the classic
     empty-node-for-spacing anti-primitive: inter-child space is a *container property*, not a node.
     `BoxLayout.Flex`/`Grid` now carry `Gap`, which is the canonical spacing mechanism. Keeping a
     `Spacer` node alongside `Gap` manufactures exactly the valid-but-wrong-**mechanism** confusion the
     charter guards against ("do I add a Spacer or set the gap?"). `Spacer` is the weaker of the two – 
     it has no irreducible a11y semantics, unlike `Separator`.

   **But sequence it as a follow-up, not in this commit.** Phase 390's *acceptance evidence* is the
   **container-cluster** confusion drop (Phase 391, charter §3.2). Folding `Spacer`/`Divider` into the
   same change-set widens the blast radius and muddies that delta's attribution – a merge's acceptance
   is the specific cluster's measured drop. So: **this phase merges the four containers; `BoxSpec` is
   cut *forward-compatible* with the retirement** (the reserved `Separator` role + the `Gap` field
   above), and a **separate pre-launch phase** retires `Spacer` (→ `Gap`) and `Divider` (→
   `Box.Separator`), each citing its own confusion delta + the charter's retirement gate, landing
   before the public launch so neither forces a post-1.0 `/v2/` major (charter §4.2). Net: fold both – the
   charter's logic demands it – but on a cleanly-attributed follow-up, with 390 leaving the door open
   at zero extra wire cost. *This is my recommendation; the operator confirms the sequencing.*
2. **`Dashboard` layout representation (recommend: `BoxLayout.Auto`).** Dashboard's auto-tile is a
   renderer-owned responsive behaviour, not an author column count. A dedicated `Auto` case keeps the
   wire honest (no fake `Cols`) and preserves the `fuaran-dashboard` class byte-for-byte. Alternative:
   model it as `Grid` with a canonical `TemplateColumns` auto-fit string – rejected (leaks a CSS string
   into the wire for what is a role behaviour).
3. **`Heading` on `BoxSpec` vs. a heading child (recommend: field).** A `Heading: TextSource option`
   field keeps `Card` byte-identical and cheap for the LLM (emit only when present). Modelling the
   heading as a first child would change every card's child indexing and break structural eval
   assertions. Recommendation: **keep the field**.
4. **`SummaryList` (out of scope).** `SummaryListSpec` (heading + rows) is card-adjacent but is a
   distinct Feliz-parity primitive; not in the four. Leave it. Note for a future pass.

## Charter compliance (`VOCABULARY.md`)

This is a **merge** (a kind-set change), so per the vocabulary charter's §5 and the roadmap
conventions it must satisfy and cite the charter's gates. A merge is the charter's *shrink* path, so
the gates read inversely to an addition:

- **Demand evidence (§1.1 / §3.3).** The merge trigger is the charter's own §3.3 *sustained-confusion
  merge review*: `Stack`/`GridLayout`/`Dashboard`/`Card` are the named container near-synonym cluster,
  and the charter cites this very phase (§3.3) as the standing precedent. The Phase 391 kind-confusion
  metric supplies the pre-merge cluster baseline (soft dependency – capture before landing).
- **Irreducibility (§1.2) – inverted.** An addition must prove irreducibility; a merge proves the
  opposite: the four kinds are *reducible* to one kind + a `Layout` mode + a `Role`. The charter §1.2
  explicitly frames "a new container is almost always a `Box` role," which is the principle this merge
  realises for the whole cluster (and, later, for `Divider`).
- **Cost (§1.3).** Acknowledged in full: the merge pays the §1.3 table once – renderer × 3 hosts,
  reference CSS + class parity, a11y curation (role-driven, snapshot-parity), the §11 wire corpus
  (encoder + decoder + schema + fixtures + every host in one commit), validator re-point, and eval +
  recipe sweep. The migration plan below is exactly this table.
- **Confusion delta (§3.2).** The acceptance instrument: the Phase 391 container-cluster wrong-kind
  rate must not regress and is expected to **drop** (the cluster collapses to one kind). That drop is
  the merge's acceptance evidence.
- **Versioning (§4.2).** A kind removal is a breaking `/v2/` major *post-1.0*; scheduling the merge
  **pre-launch** is deliberate (charter §4.2). The decode-upgrade seam softens replay but does not make
  removal non-breaking for emitters – hence the window-gate before the public launch and before the IDL
  codegen's full-breadth migration.

## §4b amendment draft (operator to land in the canonical design doc)

> Replace the `LayoutKind` container-cluster cases (`Dashboard` / `Stack` / `GridLayout` / `Card`) with
> a single `Box of BoxSpec<'Msg>` case. `BoxSpec` carries a `Layout` (`Flex` direction+wrap | `Grid`
> cols+template | `Auto` responsive tile), a `Role` (`Group` | `Card` | `Dashboard`) that drives the
> emitted element + ARIA landmark + `fuaran-*` chrome, an optional `Heading`, and `Children`. The
> author-facing `StackSpec` / `GridLayoutSpec` / `DashboardSpec` / `CardSpec` records and their smart
> constructors (`Fuaran.stack` / `.gridLayout` / `.dashboard` / `.card`) are retained as `Box`-emitting
> convenience surfaces – the authoring vocabulary is unchanged; the *wire* vocabulary consolidates.
> Decoders upgrade the four legacy tags to the equivalent `Box` on read (permalink/op-stream
> compatibility). Rationale: with a closed `NodeKind` DU, near-synonym containers are the dominant
> valid-but-wrong-kind error class; merging them enforces canonicality (one emission per pattern)
> without opening the vocabulary. `SplitPanel` / `Tabs` / `Stepper` / `SummaryList` / `Disclosure` /
> `Modal` / `ScrollArea` are unaffected (distinct arrangements, not near-synonyms).

## Phased implementation plan (post-checkpoint)

The corpus regeneration is the atomic pivot – F#, TS, and Python encoders/decoders/renderers must land
in one change-set (WIRE_FORMAT.md §11), so the plan front-loads all design/decode work *before* the
regen and treats the regen + three-host green as a single landing.

1. **F# types + authoring** – add `BoxSpec`/`BoxLayout`/`BoxRole` to `Types.fs`; retire the four
   `LayoutKind` cases; re-target smart ctors + `Defaults`; `KindName`/introspection strings. *(Builds
   green against the OLD corpus only after step 3's decoder upgrade – sequence 1→2→3 before any test
   run.)*
2. **F# renderer** – role-driven emission in `Render.fs` (client) + `Render.fs` (server); reference CSS
   retains the retired class names; a11y/landmark parity.
3. **F# encoder + decoder + legacy-upgrade** – encode `Box`; decode `Box`; upgrade the four legacy tags
   (`JsonDecode.fs`); validator re-points kind-specific checks (`GridTemplateColumnsCheck` etc.).
4. **Corpus regen (pivot)** – `cd fuaran; dotnet run --project src/Fuaran.UI.JsonDecode.Tests --
   --emit-corpus ..\wire-format-fixtures`; add one legacy-upgrade fixture per retired tag.
5. **TS leg** – `packages/{schema,ops,renderer}` encode/decode/upgrade/render; re-copy reference CSS;
   class-name vocabulary parity lock.
6. **Python leg** – `fuaran_py/{schema,ops,validator,renderer}` encode/decode/upgrade/render.
7. **Sweep** – samples/demos/courses; cookbook recipes + FastPathResolver anchors + eval assertions
   naming the retired kinds; capture the post-merge container-cluster confusion run.

Each host must round-trip the regenerated corpus byte-identically; a pre-merge op-stream must replay to
identical rendered output (legacy-upgrade fixtures).
