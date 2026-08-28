# Vocabulary-growth governance charter

This document is the **contract for admitting, merging, or retiring a `NodeKind`** – the closed
discriminated-union vocabulary in [`src/Fuaran.UI/Types.fs`](../src/Fuaran.UI/Types.fs) that is the
Fuaran UI language's **sole** structural vocabulary. It exists so the curation discipline that keeps
that set small is written down as a checkable rule rather than carried as habit.

> **Why a closed vocabulary needs a charter.** Constrained decoding makes an *invalid* kind
> structurally impossible to emit – the grammar only permits kinds that exist. The error class that
> remains, and the one that grows as the set approaches its natural plateau, is **valid-but-wrong-kind**
> selection: `Table` vs `DataGrid`, `Callout` vs `Toast` vs `Badge`. A small, canonical, near-synonym-
> free vocabulary is the strongest form of the language's error-scope promise – one obvious emission per
> pattern. Every kind added trades a sliver of that promise for expressive reach. This charter is the
> rule that keeps the trade deliberate.

The static closed kind set is a **feature**, not a limitation: it is what makes emission statically
grammar-constrainable, one-canonical-form-per-pattern, and cheap for an AI consumer to hold in working
memory. Growth is expected – toward a natural plateau of roughly **60 kinds** – but every step is
evidenced, costed, and measured.

**Scope.** This charter governs the **`NodeKind` vocabulary** (the structural wire cases). It does not
govern *author-surface* conveniences (smart constructors, `Defaults`) or *portability seams* (runtime
interfaces) – those are additive and expected and grow freely. Application-space **composition**
(reusable fragments, content-packs, a consumer app's repeated patterns) is a *separate* growth axis with
its own home; it is never the language's kind-vocabulary axis. This charter supersedes the
fragment-first kind-routing authoring gate that an earlier plan (Phase 380) once carried – Phase 380 was
re-scoped to application-space fragments and now points here for kind governance.

---

## 1. The admission checklist

A proposal to add a `NodeKind` case **must** clear every gate below. A proposal that fails any gate is
not admitted; it is redirected (to a variant – §2; to a composition – §2; or to application-space
fragments – out of scope here).

### 1.1 Demand evidence (mandatory – no evidence, no admission)

The proposal cites **at least one** of these observed, recorded signals – not a hypothetical:

- **An expression gap in a real translation.** A real consumer-view translation through the typed
  surface reached a pattern the existing kinds could not express *without an escape hatch*, and the
  gap is recorded in the vocabulary-gap ledger (the running inventory of "patterns the vocabulary
  couldn't express"; ledger code FUARAN054).
- **`Custom`-node fallout.** A shipped case study or demo resorted to `NodeKind.Custom` (the principled
  escape hatch) for a pattern that recurs across consumers – recurrence, not a one-off, is the signal.
  Since Phase 1103 the evaluation harness reports that recurrence as a **standing rate** over stored
  emissions rather than as an anecdote from whichever run somebody had open, so "recurs" is a number a
  proposal can cite and a reader can re-derive.
- **An emission-eval miss.** An AI consumer emitting the canonical prompt corpus produced a
  *"no expressible tree – missing contract case"* outcome for a prompt (distinct from a low *quality*
  score, which is a legitimate eval result and is **not** admission evidence).

**Where signal (c) comes from – the shadow evaluation leg (Phase 1103).** The third bullet has a
structural problem the first two do not: an evaluation that teaches the closed kind list *suppresses*
the very outcome it asks for. A model handed the vocabulary does not invent a `$type`, and a prompt set
written against the vocabulary does not pose a task the vocabulary already cannot express – so the
channel can read clean for months while the gap it exists to detect goes unrecorded, which is how the
media vocabulary came to be admitted by other means. The evaluation harness therefore also runs a
small, periodic **unconstrained shadow leg**: the same prompts with the kind-list teaching removed,
solely to harvest what a model reaches for when nothing stops it – invented `$type` names,
discriminators the wire format does not use at all, and emissions the vocabulary has no shape to
receive.

Sightings from that leg **are** admissible §1.1(c) evidence, under one condition: **a sighting carries
its shadow provenance wherever it is cited.** A shadow emission crosses no gate, scores no cell, seeds
no pattern bank and enters no corpus – it is quarantined by construction, which is what makes the leg
safe to run at all – so a proposal citing one as though it were a scored emission would be claiming a
reliability that emission was never measured for. Cite the leg, its teaching stamp and its run. A
sighting whose provenance has been dropped is not evidence, and an *absence* of sightings is only a
result when a leg actually ran: a leg that was not executed has measured nothing, which is a different
fact from a leg that found nothing. Recurrence governs here exactly as it does for signal (b) – one
sighting is a sighting; a repeated or cross-model one is demand.

A kind whose only justification is "a real product might want it" or "it rounds out the set" has **no
demand evidence** and is not admitted. It may be *reserved* in the plateau taxonomy (Appendix A) so its
name and disposition are pre-decided, but reservation is not admission – implementation stays
demand-gated.

### 1.2 Irreducibility – why it cannot be a composition or a variant

The proposal states, explicitly, **why the pattern cannot be expressed** as:

- **A composition** of existing kinds – in particular a composition over `Switch` (the state-bound
  conditional-child primitive; Phase 392), which absorbs conditional regions, wizard panes, empty-state
  alternatives, and mode toggles without new vocabulary. Many "I need a container that shows X or Y"
  requests are `Switch` compositions.
- **A spec-record variant** of an existing kind – a new case on an existing spec DU
  (`FormFieldKind`, `ChartKind`, a format enum) rather than a new `NodeKind`. See §2.
- **A role** on an existing kind – the `Box` container carries a semantic *role* that reproduces a
  retired kind's HTML/a11y semantics (card chrome, region landmark, separator) without a distinct kind.
  A "new container" is almost always a `Box` role.
- **An application-space fragment** – a repeated composite that belongs in a consumer app's fragment
  library, not in the language. This is the Phase 380 axis and is out of scope for kind admission.

If the pattern reduces to any of the above, it is redirected there. A `NodeKind` is admitted only for an
**irreducible structural primitive** – a shape that no combination of existing kinds, roles, variants,
or compositions can express with correct semantics.

### 1.3 Cost acknowledgment (the full carrying cost, named)

The proposal acknowledges, in writing, that a new kind imposes **every** cost below – because a kind is
the most expensive thing this language can grow:

| Cost centre | What must change |
|---|---|
| **Renderer × 3 host tiers** | Each conformant host (F#, and the sibling reference implementations) renders the kind identically. |
| **Reference CSS + class vocabulary** | New `fuaran-*` class hooks, parity-locked across every renderer + every CSS copy. |
| **Accessibility curation** | The kind's semantic HTML, ARIA roles, and landmark semantics, hand-audited – not defaulted. |
| **Wire corpus (§11)** | Encoder + decoder + JSON Schema + `wire-format-fixtures/` corpus + every sibling host, **in one commit** (the forward-coupling rule – [`WIRE_FORMAT.md` §11](WIRE_FORMAT.md#11-forward-coupling-rule-load-bearing)). |
| **Validator** | Any kind-specific structural checks, in every host. |
| **Eval + recipe coverage** | Canonical-prompt coverage, a confusion-metric baseline (§3), and pattern-bank / cookbook seeds so the AI learns *when* to reach for it. |
| **Working-memory tax** | Every AI consumer, every prompt, forever, carries one more near-synonym to disambiguate against. |

The IDL codegen family (Phase 317) cheapens the *mechanical* legs of this table but not the
*conceptual* cost – the a11y curation, the confusion tax, and the working-memory tax are irreducible
and are the ones this charter guards.

---

## 2. Variant-vs-kind guidance (the quiet-churn rule)

Not every unit of new expressiveness is a `NodeKind`. Much of it lands, correctly, as a **variant** – a
new case on an existing spec-record DU. The distinction matters because it changes what the AI consumer
must disambiguate:

- A **new kind** adds a top-level choice: the consumer now picks between *N+1* kinds at the point where
  it previously picked between *N*. This is the expensive axis – it widens the confusion surface.
- A **new variant** adds a choice *within* a kind the consumer has already selected: having chosen
  `Form` → `FormFieldKind`, or `Chart` → `ChartKind`, the consumer picks the variant locally, inside a
  much smaller decision. The disambiguation is scoped.

**The rule: prefer the variant when the pattern is a specialisation of a kind the consumer would already
have chosen.** Precedents already in the tree:

- **`FormFieldKind`** absorbed date/time (`Date` + `DateVariant`), ranged number (`RangedNumber`),
  segmented choice (`SegmentedChoice`) – all as *variants*, never as new top-level kinds. A future
  rating, colour-picker, combobox, or autocomplete field is the same: a `FormFieldKind` variant.
- **`ChartKind`** is the home for new chart types (gauge, funnel, heatmap, treemap, sankey) – variants,
  not new `NodeKind`s.
- Format/enum cases (`Format`, `DateStyle`) grow inside their DUs.

### 2.1 Below the variant line – spec-record FIELD additions

There is a **third** tier under the two above, and it was left implicit until the form-validation
charter (Phase 864) had to choose between it and a variant. A new **field on an existing spec
record** – `StaticRows.sortable`, `GridSpec.pageStateKey`, `FormField.rule` – adds *no* choice at
all: the consumer has already selected the kind and already selected the case, and the field is an
optional refinement it may ignore entirely. On the confusion axis this tier is **free**, which is
strictly cheaper than a variant's *scoped* disambiguation.

On the **wire-coupling** axis it is *not* free, and it is not cheaper than a variant either: §11's
forward-coupling rule is stated over the whole wire contract, so a field addition still costs
encoder + decoder + schema + corpus + every host in the roster, in one change-set. What it skips is
the §11.2 **vocabulary attestation** – `manifest.formFieldKinds` and `manifest.kinds` enumerate
*cases*, so a host that has never met a new field is not named by any manifest, and only the fixture
catches it. That is the tier's one genuine disadvantage and a proposal choosing it must say so.

**The rule for choosing between the three:**

> A **kind** names a structural primitive. A **variant** names a different CONTROL or SHAPE within a
> kind the consumer already chose. A **field** names a REFINEMENT of a control that is otherwise
> unchanged. Where a pattern would read equally well as a variant or a field, prefer the **field** –
> it is the only one of the three that adds nothing to the working-memory tax.

The trap this rule exists to catch is a real one and it is counter-intuitive in the other direction:
*widening an existing case* is more expensive than adding a new one. Adding a field to a DU **case**
changes that case's arity, so every existing pattern match on it stops compiling; adding a new case
leaves them all alone and raises an `FS0025` only where a match is exhaustive. So "just put it on
`Text`" is the most expensive of the three spellings, not the cheapest.

**The quiet-churn caveat.** A variant is *not* free. Per [`WIRE_FORMAT.md` §11](WIRE_FORMAT.md#11-forward-coupling-rule-load-bearing),
adding a case to *any* wire DU – `NodeKind` **or** a spec-record variant like `FormFieldKind` /
`ChartKind` – carries the identical forward-coupling cost: encoder + decoder + schema + corpus + every
host, in one commit. A variant is cheaper on the *confusion* axis (scoped disambiguation) but **equal**
on the *wire-coupling* axis. So variant additions are still governed: they cite demand evidence (§1.1)
and acknowledge the §11 cost, even though they skip the kind-level irreducibility test (§1.2) – a
variant *is* the redirect. The thing to avoid is treating variant growth as invisible: a spec DU that
accretes a case every few weeks is churning the wire contract just as surely as new kinds would, and the
confusion metric (§3) should watch intra-DU variants as well as top-level kinds.

---

## 3. Plateau budget + confusion guard

### 3.1 The plateau as an advisory budget

The natural plateau is roughly **60 kinds**. This is an **advisory budget**, not a hard cap: it is the
number at which a semantic UI vocabulary is expected to have covered the irreducible structural
primitives a general consumer surface needs, with the long tail handled by variants, roles, and
composition. Crossing it is not forbidden – but a proposal that would push the set past the budget
carries a heightened burden on §1.2 (irreducibility) and should ask whether the *budget* is wrong or the
*proposal* is a variant in disguise.

### 3.2 Every change cites a confusion delta

Every vocabulary change – an addition, a merge, or a retirement – **cites a pre/post delta on the
kind-confusion metric** (the valid-but-wrong-kind rate captured per eval run, aggregated into per-pair
confusion counts). The metric is the instrument that makes the minimalist strategy *safe*: it turns
"the set feels confusing" into a measured number per release.

- An **addition** must show it does not raise the confusion rate materially – a new kind that the AI
  routinely confuses with an existing one is a failed admission even if it passed §1, and should be
  reconsidered or merged.
- A **merge** (near-synonym collapse) is expected to *lower* the rate for the merged cluster – that
  drop is the merge's acceptance evidence.

### 3.3 A sustained confusion rise triggers a merge review

If the confusion metric shows a **sustained rise** in a cluster's wrong-kind rate across releases, that
is the trigger for a **merge review** – the discipline that produced the container merge (`Box`
absorbing Stack / GridLayout / Dashboard / Card, Phase 390) and the tabular merge (`Table` folding into
`DataGrid`, Phase 393). Those two merges are the standing precedent: when two kinds are chronically
confused, the right move is often to collapse them into one kind with a role or mode, not to write more
documentation telling the AI how to tell them apart. The vocabulary shrinks under measured confusion
pressure exactly as it grows under measured demand pressure.

---

## 4. Post-publication versioning policy

The kind vocabulary is serialised through the canonical wire format, whose schema `$id` pins a **major**
wire-profile segment (`.../wire-format/v1/schema.json`). This section governs how kind changes map to
wire-profile versions **after** the language tier publishes `1.0.0`. Until then, the pre-1.0 rules in
[`STABILITY.md`](../STABILITY.md) apply: a kind addition is a **minor** bump (existing consumer matches
gain an `FS0025` exhaustiveness warning – the correct signal, not a break), under the §11 forward-
coupling discipline.

### 4.1 Addition – an additive `core@1.x` profile minor

After `1.0.0`, **adding** a kind is an **additive minor** within the `v1` wire profile – call it a
`core@1.(x+1)` profile bump. It adds a new `$type` branch to the schema's top-level `oneOf`; every
previously-valid document stays valid; the `/v1/` major segment does **not** move. This mirrors the
pre-1.0 additive posture – the difference post-1.0 is that the profile minor is *recorded* so hosts can
negotiate on it (§4.3).

### 4.2 Removal / rename – a `v2` major (avoid; do it pre-launch)

**Removing or renaming** a kind – changing a `$type` discriminator string – is a **breaking wire-format
change**: a `/v2/` major event, not a silent encoder bump. This is why the near-synonym **merges**
(`Box`, `Table`→`DataGrid`) are deliberately scheduled **before** publication: a merge retires kinds, so
doing it post-1.0 would force a major. The decode-upgrade seam (decoders accept a retired tag and upgrade
it to the survivor on read) softens replay compatibility but does not make a removal non-breaking for
*emitters*. **Rule: retire kinds before the public launch; after it, the set grows additively or not at all.**

### 4.3 Unknown-discriminator behaviour + the host-lag commitment

A decoder that predates a newly-added kind encounters an **unknown `$type`** and rejects it – 
`UNKNOWN_DU_CASE` at the decoder, no matching schema branch for external validators. A new kind is
therefore **forward-incompatible**: a new-version emitter that emits it produces a document an
old-version host cannot render. Two commitments follow, and they are the reason the growth *rate*
matters:

1. **Emitters gate on a negotiated profile floor.** An emitter must not emit a kind newer than the
   `core@1.x` profile the receiving host advertises support for. The host declares its supported
   profile; the emitter stays within it. A slowly-growing vocabulary makes this floor easy to hold – 
   hosts rarely lag by more than one or two profile minors.
2. **The host-lag window is bounded by the growth rate.** Because kinds are admitted deliberately (a
   small evidenced batch pre-launch, then demand-paced – §Appendix B), the gap between "newest emitter"
   and "oldest conformant host" stays narrow. A vocabulary that grew a kind a week would make the
   host-lag commitment untenable; the deliberate rate is what makes it tractable. This is a concrete
   downstream reason the admission checklist's demand gate (§1.1) is strict.

---

## 5. How this charter binds phase authoring

Any development plan that **changes the wire vocabulary** – a kind addition, a variant addition, a
**spec-record field addition** (§2.1), a merge, or a retirement – cites this charter and satisfies its
gates in the plan body: the demand evidence (§1.1), the
irreducibility statement (§1.2), the cost acknowledgment (§1.3), and the pre/post confusion delta (§3.2).
A field addition's §1.2 argument is the one that most often goes unwritten, because the §2/§2.1 redirect
looks like it *is* the argument – it is not. The redirect says why the pattern is not a kind; §1.2 still
has to say why the pattern needs the wire at all, and a field whose only justification is that it was
cheap to add is a field with no evidence behind it.
[`STABILITY.md`](../STABILITY.md) links this charter from its stability policy so the two documents stay
in agreement: STABILITY.md governs the *version-bump* classification of a kind change; this charter
governs *whether the change is admitted at all*. A plan that adds a kind without clearing this charter is
a discipline defect, the same class as a plan that introduces a breaking change without the
`**Stability impact:**` annotation.

---

## Appendix A – Plateau taxonomy (reserve names now, implement on evidence)

This appendix **reserves names and clusters** for the roughly-twenty candidates that a general UI
vocabulary tends to accrete on the way to its plateau, and **pre-rules each one's disposition**
(variant / composition / role / genuine kind). Deciding the naming and clustering *early* prevents the
near-synonym drift that the `Box` and `Table`→`DataGrid` merges had to clean up retroactively – the
namespace is filled now; the implementations stay **demand-gated** (each still needs §1 evidence before
it is built). A reservation is a pre-decision, not a commitment to ship.

**Legend – disposition:** **Variant** (a case on an existing spec DU – §2) · **Composition** (built from
existing kinds – §1.2) · **Role** (a `Box`/existing-kind semantic role) · **Kind** (a genuine
irreducible primitive, reserved pending demand) · **Covered** (already expressible today) ·
**Host chrome** (the capability belongs to the host application, and the language names it nowhere –
added 2026-08-18 by the affordance→op charter, Phase 866, which needed a way to record a *decline*
that is a positive design position rather than a deferral) · **Field** (a slot on an existing spec
RECORD – the tier §2.1 defines, below the variant line; listed here 2026-08-27 by Phase 873 because
three rows already carried the disposition while the legend did not define it, and a legend that
omits a disposition in live use is how a reader concludes the row is a typo).

### Navigation cluster

| Reserved name | Disposition | Ruling |
|---|---|---|
| `NavBar` / `Menu` | **Kind** (reserved) — **assessed negative 2026-08-20** | Reserved as the strongest genuine-kind candidate in this cluster (distinct `<nav>` landmark + menu ARIA semantics). **Assessed 2026-08-20 (Phase 829) and NOT ADMITTED** — the reservation stands; admission does not. §1.1: **no recorded emission demand at all** — no sighting of an invented `NavBar` / `Menu` / `NavItem` / `Sidebar` emission is on record, where every admitted neighbour cites a count (`DateRange` ×9, `TonedPill` ×26, `DragReorder` ×20). §1.2: the headline irreducibility claim fails on its own terms — the landmark is **already carried on the wire** by `AriaRole.Navigation`, a first-class `Accessibility.Role` case and ARIA-landmark-equivalent to `<nav>`; an ordered item-set is a host-side projection concern; and responsive collapse is **Host chrome** on exactly the `Virtualisation` reasoning above, a tree that collapses and one that does not being identical in every respect a consumer can observe. Confirmed from the demand side rather than argued: the **first production navigation composed from `Group` + `Link`** (Phase 828's projection, adopted 2026-08-20) sufficed with **no missing vocabulary** — structure, ordering, the landmark and real crawlable anchors all fell out of the composition, and the frictions it did record were two stylesheet re-binds and one chrome-vs-navigation boundary, none of them a kind. Recorded here for the `Virtualisation` reason: the charter is not a surface a reader consults. **Reopen condition:** a §1.1 sighting count of the order the admitted rows cite, or composed nav proving semantically wrong in recorded production use. (The one genuine expression gap the assessment found — `aria-current` reaches the tree only through wire-omitted `ExtraAttributes` — is a §2 trait-field question, not this row. **Disposition of that redirect, updated 2026-08-20 (Phase 951):** the follow-up note reasoned a real `Accessibility.Current` field "would land on the anchor via the a11y projection". It would not have — the projection was emitted on the node's wrapper `<div>` in every renderer, the same wrong element `ExtraAttributes` already reached by a different route, so the §11 cost of a wire field would have bought no observable improvement. Phase 951 fixed the placement, which makes the redirect **coherent** rather than actionable: a `Link`'s a11y projection now lands on the `<a>`, so a `Current` field would too — and so does the `aria-current` extra-attribute the nav projection already uses, which is what removed the production host's descendant CSS selector. The field itself stays **gated on its own §1.1 evidence** — a round-tripping consumer hitting the wire gap, which no pure-SSR adoption can supply, and which the working extra-attribute route makes less likely to arrive, not more.) |
| `Breadcrumb` | **Composition / Role** | A `List` of `Link`s with an ordered-trail role; reach for a kind only if the a11y trail semantics prove irreducible under demand. |
| `Pagination` | **Composition / renderer-owned affordance** | Not a kind. Page state is a `pageStateKey` + `pageSize` on the grid that pages, and that grid's own renderer draws the pager. A `Button` + `SetState` composition over the same key remains legal as an *additional* writer, but is not the primary spelling: a free-standing pager has no structural relation to the grid it means to drive, and eleven cross-family emissions of exactly that shape wrote page state nothing read. **Amended 2026-08-16** — the disposition (not a kind) is upheld; the original ruling prescribed the composition alone, which the demand evidence showed to be the fake-affordance generator. See the grid-behaviour charter (Phase 860). |
| `Virtualisation` / `VirtualList` | **Host chrome** | Not a kind, not a field, and deliberately not the other half of `Pagination`. Windowing rows is a **render-tier** concern with nothing for the wire to say: no tree changes shape because rows are windowed, so a decoded tree that declared it and one that did not would be identical in every respect a consumer can observe. Paging is different precisely because it changes *which rows exist* in the projection, which is why it earned a spelling and this did not. Ruled OUT by the grid-behaviour charter (Phase 860) as a named satellite; recorded here in 2026-08-18 because the charter is not a surface a reader consults, and the ruling was re-proposed from scratch once. **Reopen condition:** a wire-observable consequence — a windowed grid that must tell a host its viewport, or a declared row-height contract a host cannot infer. |
| `CommandPalette` | **Composition** | `Modal` + `Filters` + `List` – an application-space fragment, not a kind. |
| `TableOfContents` | **Composition** | `List` + `Link` (+ anchor bindings). |

### Temporal-input cluster (variant-dominated – the `FormFieldKind` precedent)

| Reserved name | Disposition | Ruling |
|---|---|---|
| `DatePicker` / `TimePicker` / `DateTime` | **Variant** (shipped) | Already `FormFieldKind.Date` + `DateVariant`. The exemplar: temporal input is a field variant, never a kind. |
| `DateRange` | **Variant** (shipped) | Shipped as `FormFieldKind.DateRange` at 0.7.0 (Phase 725): `Range`'s pair mechanics with `Date`'s ISO/variant conventions, `Min`/`Max`/`Step` bounding both ends. Admitted on the operator mandate plus 9 full-pack emissions of an invented `$type:"DateRange"`; irreducible because the two-`Date`-field workaround splits one semantic value across two uncoordinated bindings (one filter param, not two). |
| `Slider` / `Range` | **Variant** (shipped surface) | `FormFieldKind.RangedNumber` with a slider render variant. |
| `Calendar` (month-grid display) | **Kind** (reserved) or **Variant** | If it is *display* of a month grid, a possible `DataGrid` mode or a genuine kind; if it is *input*, a `FormFieldKind` variant. Disposition decided when demand names which. |

### Media cluster

| Reserved name | Disposition | Ruling |
|---|---|---|
| `Media` (→ `Video` / `Audio` variants) | **Kind** (**ADMITTED 2026-08-27**, Phase 1076) | `<video>`/`<audio>` carry distinct controls + captioning a11y no existing kind expresses. Admitted in exactly the shape this row pre-ruled — **one** `Media` kind with a `MediaKind` variant DU (Video / Audio), per-case payloads for the video-only slots — so the variant ruling stands unweakened. The §1.1 gate was overridden by an **explicit operator mandate, recorded standing alone** (Phase 1076's Charter admission section carries the walk and the mandate): media display is a ubiquitous platform capability whose absence the reactive demand channels structurally cannot evidence — constrained decoding suppresses the invented-`$type` sighting class, and the census never poses tasks the vocabulary already cannot express. The mandate is an attributable override of the evidence gate for THIS row, not a softening of it: future kinds still clear §1.1 or carry their own recorded mandate. |
| `Avatar` | **Variant / Role** | An `Image` variant (shape + fallback-initial role), not a kind. |
| `Icon` | **Variant / Role** | An `Image` variant or a `Badge` role; never its own kind. |
| `Carousel` / `Gallery` | **Composition** | `Box` + `Switch` (Phase 392) over an index state key – the canonical "compose, don't add a kind" example the authoring guide teaches. |
| `Embed` (iframe-like) | **Covered** | `Mount` (Phase 265, §4o) already carries the isolation/embedding boundary. |

### Communication / feedback cluster

| Reserved name | Disposition | Ruling |
|---|---|---|
| `Banner` / `Alert` | **Covered** | `Callout` (inline) + `Toast` (transient) already span this. A *third* feedback kind here is a confusion-metric risk, not a gap – watch the Callout/Toast/Badge cluster (§3.3) rather than adding to it. |
| `Tooltip` | **Role / Composition** | An annotation role or a `Disclosure` variant; a hover hint is not a structural primitive. |
| `Popover` | **Variant** | A `Modal` variant (a non-modal modality flag), not a distinct kind. |
| `NotificationCenter` | **Composition** | `List` + `Toast`. |

### Data-display / structured cluster

| Reserved name | Disposition | Ruling |
|---|---|---|
| `Tree` / `TreeView` | **Kind** (reserved) or **Composition** | Recursive disclosure + selection *may* be irreducible; first attempt is a `List` + `Disclosure` composition. Reserve the name; admit only if composition proves semantically wrong under demand. |
| `Timeline` | **Role / Variant** | A `List` role (ordered temporal-trail rendering), not a kind. |
| `Rating` / `ColorPicker` / `Combobox` | **Variant** | All `FormFieldKind` variants – data entry is the field DU's territory. |
| `KanbanBoard` | **Composition** | `Box` + `DataGrid` – application-space fragment territory (Phase 380), never a language kind. |
| New chart types (`Gauge` / `Funnel` / `Heatmap` / `Treemap` / `Sankey`) | **Variant** | `ChartKind` variants, one and all. |
| `StatusPill` / conditional row emphasis | **Variant** (shipped) | Shipped as `CellKindErased.TonedPill` at 0.11.0 (Phase 750): `field` + a value→`ToneVariant` `map` + an omit-when-default `default`. Admitted on 26 usage-evaluation occurrences of one intent across three prompt families and three providers, every one a partial. **Irreducible in the strongest available sense** — not "no composition expresses it" but "no wire spelling exists at all": the pre-existing `Pill` case's tone is a `'row -> ToneVariant` closure, which erases to `"<closure>"`, so the rule could not be *said* rather than being awkward to say. Cost: one variant + a `Map` field, no new kind, no new class vocabulary (it renders through the existing `fuaran-grid-cell-pill` / `fuaran-pill-<tone>` hooks). Confusion-delta: measured as the three canaries' criteria moving off PARTIAL in the next cohort — the pack change supersedes the current baseline by design. |

### Grid-behaviour cluster (added 2026-08-27 by Phase 873 — the Phase 860 charter's own rows)

**Filed retroactively, and the reason it was missing is worth more than the rows.** Phase 860's charter
was approved and its three implementation phases shipped, but only ONE of its rulings reached this file
— `Pagination`, and only because a reserved NAME already sat in the Navigation cluster for it to amend.
The rest had no reserved name to attach to, so nothing pulled them here, and this charter's own §5 says
the charter is not a surface a reader consults. A ruling that lives only in a charter document and a
phase Outcome is a ruling the next session re-derives.

Its governing ruling is one sentence and it decides every row below — *a grid behaviour the user drives
is declared as a named State KEY that the grid both writes and reads, carrying a descriptor whose shape
the specification fixes; the affordance belongs to the renderer.* The corollary is the part that keeps
getting re-proposed: **there is no grid-level `sortable` or `pageable` boolean**, because the key IS the
affordance and a flag with no key behind it is a decorative control writing state nothing reads.

| Reserved name | Disposition | Ruling |
|---|---|---|
| `sortStateKey` + bound `defaultSort` (Phase 861) | **Field** (shipped 2026-08-16, 0.26.0) | Not a kind, not a variant. `sortStateKey` names the key carrying `{"column", "direction"}`; declaring it IS the header affordance, so a "sortable grid" that names no key is prose. `defaultSort` reuses the record and the field NAME `staticRows` already carried from Phase 801 — same behaviour, same spelling, deliberately not a second vocabulary — and applies only while the key carries nothing; once the user has sorted, the state wins. A grid may declare `defaultSort` with no `sortStateKey`, which is an opening order without interactive re-sorting. §1.1: census #26 at HIGH urgency, `stress-007/c2` ×cross-family. |
| per-column `sortable` (Phase 861) | **Field** (shipped) | A column flag NARROWS and never widens — the charter's rule, and the reason `true` under a grid declaring no `sortStateKey` is `FUARAN094` rather than a silent no-op: a column asking to turn a behaviour ON is asking for something the rule does not grant. Absent inherits; `false` opts out. |
| `pageSize` + `pageStateKey` (Phase 862) | **Field** (shipped) | The `Pagination` row in the Navigation cluster carries the ruling; recorded here so the grid-behaviour family reads whole. The pager is renderer-owned, which is what makes a decorative pager unauthorable rather than merely discouraged. |
| `editStateKey` (Phase 863) | **Field** (shipped 2026-08-16, 0.26.0) | Not a kind, not a variant, and not a convenience. Before it the only spelling for an edit destination was a closure, which erases to `"<closure>"` — so a DECODED editable grid could not say where its edits land, which is census #27's whole complaint. Absent keeps Phase 663's shipped behaviour exactly (write back to the grid's own `source` when that source is a direct `State` binding, display-only otherwise), so nothing already authored changes meaning. It is also the destination Phase 866's admitted row-reorder reuses rather than minting a second write path. |
| per-column `editable` (Phase 863) | **Field** (shipped) | The write-side twin of `sortable`, and the same narrowing rule: absent inherits the grid-level `editable`, `false` makes a column read-only under a grid-level `true` — the declaration that read-only-by-omission could not make — and `true` under a non-editable grid is `FUARAN095`. Its own demand row is distinct from #27's: #27 asks WHERE edits go, this asks WHICH columns may be edited, and the demand log carries them separately for that reason. |
| a grid-level `sortable` / `pageable` boolean | **Refused by name** | Not reserved, not deferred — refused, and refused at DECODE by the near-miss table rather than ignored, so an emission reaching for it is told what to write. The `staticRows` path keeps its own `sortable`, which is not an exception: a static table holds its rows in the tree, so there is no state key for a reader to name. |

### Interaction / affordance cluster (added 2026-08-18 — the affordance→op charter, Phase 866)

The cluster the taxonomy lacked: how a **user gesture** reaches an effect. Its governing ruling is one
sentence and it decides every row below — *a user gesture is not named on the wire; the wire names a
capability on the node that both hosts the gesture and consumes its effect, and the renderer owns the
affordance, dispatching through the existing dispatch gate.* Two rows reduce under it; two are **Host
chrome**, and those two are the point of recording the cluster at all — a decline whose reasoning is
written down is what stops the next proposal re-deriving it.

| Reserved name | Disposition | Ruling |
|---|---|---|
| `DragReorder` / `RowReorder` | **Composition / renderer-owned affordance** | Not a kind and not a new `Action` verb. A reorder is a write of the collection the node already reads, so it is a capability flag on the grid that reorders (`reorderable`), whose destination is the grid's existing edit destination (`editStateKey`, else the source's own state key) — the Phase 863 field reused, never a second write path. The gesture itself has no wire name: the renderer draws the drag handle **and its keyboard equivalent**, which is part of the affordance rather than a follow-up. Admitted on ×20 cross-family task-qualified sightings (2026-08-15), ranked last of its cohort by the Phase 856 baseline read. See the affordance→op charter (Phase 866). |
| `ChartSelection` / `Crossfilter` | **Covered — on a renderer default** | Not a kind, not a variant, **no wire change at all**. Chart-driven selection is the Phase 818 read rule (any read slot may take a `Binding`, and `Binding.Selection` reads what a node publishes) plus the Phase 427 write default (a node whose click handler is absent publishes under its own `NodeId`) extended from `DataGrid` to `Chart`. Crossfilter falls out of the same mechanism with no coordination vocabulary. The existing `ChartSpec.onPointClick` closure sits beside it, untouched, and continues not to survive decode. **Zoom and brush are NOT covered by this ruling** and are separately out: zoom is view state with no cross-node consumer, brush is a range whose value is not a row and which has no demand evidence. |
| `Undo` / `Redo` | **Host chrome** | Not an `Action` case. The op-stream's inverse ops are real and certified, and they invert **tree** ops — the AI's authoring channel. Every user gesture the language admits writes *state*, and a state write has no op representation at all, so an `Action.Undo` would either do nothing or undo an authoring op the user never performed: a fake affordance minted at the vocabulary level, in the one place a mistake cannot be withdrawn cheaply (§4.2). A host that owns a history owns its control. **Reopen condition:** a durable *user*-action record exists (Phase 889); only then is "invert a recorded user action" a question with an answer. |
| `KeyboardShortcut` / `Hotkey` | **Host chrome** | Not a kind, not a field, not an `Action`. The language's keyboard posture is already complete and deliberate: **widget-local WAI-ARIA interaction, renderer-owned, named nowhere on the wire** (roving tablist focus, radiogroup arrow cycling, grid key handling). A wire vocabulary would buy a per-host binding table, platform normalisation (⌘ vs Ctrl, `key` vs `code`, IME state), a conflict-resolution policy against the host's and the browser's own chords, a discoverability obligation (an undiscoverable shortcut is another fake affordance), and a trust question — a decoded tree from an untrusted emitter capturing document-level keystrokes is a keylogger-shaped capability. Against that, §1.1 evidence is **nil**: the demand row's own text records that its probe was never authored, so the intent has never been observed firing. Application-global chords belong to the host, which already owns the seams to dispatch into the tree. **Reopen condition:** a stress-authored cross-family sighting — and even then the first question is whether the demand is widget-local a11y the renderer already owes. |

### Form-constraint cluster (added 2026-08-21 — the form-validation charter, Phase 864)

The cluster the taxonomy lacked on the *input* side: how a form says what it will ACCEPT. Its
governing ruling is one sentence — *a `FormFieldKind` case names a CONTROL, a rule names an ACCEPTED
SET, and the renderer's choice of `<input type="email">` is that set's HTML projection rather than a
second place the wire says the same thing.* One row is **admitted** as a §2.1 field addition, three
are **redirected** to it, and one is **out**. The point of recording the redirects is that each was
independently proposed as a variant, and the reasoning that declined them is not otherwise written
down anywhere a reader would find it.

| Reserved name | Disposition | Ruling |
|---|---|---|
| `FieldRule` / per-field constraint | **Field** (shipped 2026-08-21) | Admitted as `FormField.rule`, an optional non-discriminated record carrying `format` / `pattern` / `minLength` / `maxLength` / `compare` / `message` — a §2.1 field addition, no new case in any discriminator family. **§1.1 demand evidence:** one stress-authored task's constraint criteria, ×20 task-qualified sightings across two of them plus ten on a third, cross-family and cross-provider. Verbatim: *"email format rule only appears in help text … no format/pattern constraint"* (×10); *"no cross-field constraint referencing hireStartDate is declared anywhere in the form"* — **ten straight NOs**, the strongest single-criterion evidence in the set (×10); *"email uses generic `$type:Text` with no email-specific kind or format attribute"* (×10, on emissions that DID reach `RangedNumber` for a numeric bound and `Date` for the dates, so the residue is narrower than "no constraint vocabulary"). Every one of the twenty restated the rule as **help text**, which is the failure mode the admission has to beat rather than merely improve on. **§1.2 irreducibility:** `required` was the entire constraint vocabulary, so a format, a pattern, a length bound and a cross-field predicate had exactly one expressible home — prose a host cannot act on. Not a composition (`Switch` conditions a *subtree*; it does not constrain a value); not a role; not a fragment. Reuse was checked first and bounded the scope: `RangedNumber` already carries `min`/`max` and `Date` already carries `min`/`max`, so **the rule slot mints no numeric or temporal bound of its own** — the residue was format, pattern, length and the cross-field operand. **Why a field and not a variant** (the charter's headline departure): a `FormattedText` case beside `Text` manufactures one more instance of the exact error class §1's preamble names — valid-but-wrong-kind selection — and the worst-behaved instance, because `Text` for an email field is *not wrong*, only less specific, so nothing can call it. A field adds no choice to get wrong. It also matches how the demand actually arrives: the models already emit `{"$type":"Text"}`, so a key added beside `help` is the gesture they already perform, where substituting a discriminator requires knowing the alternative exists before the emission begins. **§1.3 carrying cost, in full:** renderer × every host tier (the constraint attributes and the submit refusal); no new `fuaran-*` class vocabulary (invalid-field marking rides the shipped field-error hooks); accessibility curation *is* required and is not defaulted (`aria-invalid` / `aria-describedby` wiring to the rule's own message); the §11 wire tax in full — IDL, generated codec, policy decoder, schema, corpus, every codec host in the roster; a three-code validator family (FUARAN099/100/101) in every host that carries one; eval + pack coverage so a model learns to reach for it; and the working-memory tax, which is the one line this row can honestly claim to have *minimised* rather than paid. It also gives up §11.2 **vocabulary attestation** — a manifest enumerates cases, not fields, so a host that never adopts `rule` is named by no manifest and only the fixture catches it. That loss is the strongest argument for the variant spelling and it was accepted deliberately: attestation catches a host lagging on adoption, a coordination problem with an owner, whereas the confusion tax is paid by every emission forever. **§3 confusion delta:** structurally **zero** against the existing nine `FormFieldKind` cases — the change adds no case, so there is no new pair for the metric to score and the pre/post baseline is unchanged by construction. That is a stronger claim than a small measured delta, and it is the reason the spelling was chosen. The residual risk is on a different axis and is measured elsewhere: whether an author reaches for `rule` or falls back to `help` prose, read as the flip measure on the three criteria above at the pack-teaching sweep. |
| `EmailField` / `UrlField` / `TelField` | **Field** (redirected) | Not three cases, and not one case with a nested enum either. The `Media` row above already pre-ruled the three-siblings shape ("reserve **one** … with a variant DU … not two kinds"); the single-case spelling then failed the confusion test in the row above. Both land as `rule.format`, a bare-string enum of `email` / `url` / `tel`. `password`, `search`, `number` and `color` are HTML input types with **no §1.1 evidence** and are *reserved, not admitted* — `number` in particular would collide with `RangedNumber` and re-open the reuse rule. |
| `PatternField` / `RegexField` | **Field** (redirected) | `rule.pattern`, an ECMA-262 source implicitly anchored to the whole value — the HTML `pattern` semantics exactly, so the browser, the static projection and every host agree without a second definition. Deliberately not carried on a format case: an email field with an *additional* corporate-domain pattern is real, so a pattern has to reach any control, which means the record. |
| `CrossFieldRule` / `FormPredicate` | **Field** (redirected — and mostly a reuse) | Not a new construct. A rule's comparison operand is a read slot, and the reactive-derivation charter's one rule (any read slot may take a `Binding`) already says what a read slot may hold; the auto-bind rule already puts every form field's value in State under the field's own id. So a cross-field predicate is an ordinary per-field rule whose operand is `{"$type":"State","key":"<sibling field id>"}`, with no coordination vocabulary at all. Six operators, one operand, **no boolean combinators, no arithmetic, no expression language** — the standing rejection is unchanged by the slot being a predicate rather than a value. Rejected sibling spelling: letting `Date.min` / `RangedNumber.max` accept a `Binding`, which reaches only controls that already have bounds (so "confirm password equals password" stays inexpressible), conflates a *selectable range* with an *accepted set*, and re-types five slots where one suffices. |
| `FieldError` / a validation-state slot on the wire | **Host chrome** | Not a field. An error is a **state**, not a declaration: an emitter would be authoring the outcome of a validation it has not performed over values it has not seen, and a decoded tree carrying an error message would replay a stale failure on every mount. Surfacing an unmet rule belongs to the host's own form feedback, which already exists on the server-driven path. **Reopen condition:** none foreseeable — the shape is wrong rather than unevidenced. |

### Data-sharing cluster (added 2026-08-26 — the shared-data-source charter, Phase 865)

The cluster the taxonomy lacked on the *provenance* side: how two sibling nodes read ONE table. Its
governing ruling is one sentence — *the tree already has one name for host data and one name for
tree-scoped data, and sharing is a question about what may DECLARE a value under the second, never
about minting a third.*

**Nothing was admitted at first, and one thing shipped.** The only shape that works is a **semantics
change to an already-shipped slot** rather than an additive case, and on ×2 evidence from one
criterion that was not a call to make; it was deferred, evidence-gated, pending corroboration at the
pack-teaching sweep. What shipped alongside is the *defect flag* that stands independent of it:
**FUARAN105 (Warning)**, which names the silent zero the old semantics produced — a `Transform` over a
default-less `State` source resolved to the empty table and rendered a plausible wrong answer with
nothing red anywhere. A finding about shipped behaviour needs no vocabulary decision, and shipping it
did not anticipate one.

**The gate then closed the other way, and that is the record worth keeping.** Phase 872's sweep
(epoch `b77343e`) reproduced the unlinked-copies complaint at `stress-001/c2` from a THIRD model
family, post-teaching, on a criterion teaching structurally cannot move — three families across two
windows. The operator ruled the charter's own pre-registered reopening condition met on 2026-08-27
and **admitted the seeding rule**; it shipped as [Phase
1075](../../roadmap/phases/1075-binding-state-seeding-semantics-o1.md). An evidence gate that only
ever refuses is not a gate, and this row is the first time one in this charter has been reopened by
the evidence it named in advance.

| Reserved name | Disposition | Ruling |
|---|---|---|
| `SharedSource` / named embedded table | **Semantics (ADMITTED 2026-08-27 — shipped, fuaran#1075)** | Still not a kind, not a variant, not a field, and that refutation is unchanged: the two sighted nodes share no slot type but `Binding`, so `DataSource` could never have carried the answer. What was admitted is the **seeding rule on `Binding.State.defaultValue`** — a declared default fills its slot for every reader in the tree rather than falling back for its own binding alone — which is a semantics change to a shipped slot and was deferred at ×2 on one criterion. The gate reopened on its own terms: 872's sweep (epoch `b77343e`) reproduced `stress-001/c2` from a third model family post-teaching, on a pack-independent criterion, and the operator ruled the charter's condition met. Shipped with `FUARAN106` (conflicting seeds, **Error**) and `FUARAN107` (two inline copies of one table, **Warning**); **FUARAN105 widened** to the wording the charter always gave it, since a sibling's declaration now IS a rescuer. Normative in `WIRE_FORMAT.md` §24.4–§24.6. See the charter §10. |
| `DataScope` / `Provide` | **Composition / rejected** | A container that renders nothing, inventing a third tree-scoped namespace beside `$state` and `queryResults`. Scoping is not a structural primitive. |
| `DataSource.Named` / tree-first `Ref` | **Rejected — structurally cannot work** | `DataSource` reaches the UI wire only inside `Binding.Transform`. It cannot name a grid's or a chart's source, so it could not make the sighted pair share anything. The phase file's own anticipated shape, refuted. |
| declared total / "a feed larger than you inline" (`stress-006/c1`) | **Host chrome** | A number the tree cannot substantiate. A host `Query` knows its own count; an inline source of twenty rows captioning two hundred is a false claim, and a slot carrying it would make the language complicit rather than fix it. Routed to pack teaching (872). **Reopen:** a paged grid over a host feed needing a total the host cannot supply. |

### Metric-semantics cluster (added 2026-08-26 — the trend-polarity charter, Phase 867)

The cluster the taxonomy lacked on the *interpretation* side: how a tree says what a number MEANS
as opposed to what it IS. Its governing ruling is one sentence — *`tone` states how a reading
stands and is a fact about the value; polarity states which way the quantity improves and is a fact
about the metric; a host derives neither from the other.*

| Reserved name | Disposition | Ruling |
|---|---|---|
| `trendPolarity` / `inverse` / "down is good" | **Field (admitted 2026-08-26)** | Not a kind, not a variant. An optional bare-string enum on `MetricSpec` beside `trend`/`trendFormat`, defaulting to `HigherIsBetter` and omitted at default. `tone` cannot carry it: the slot already colours the tile, the two statements have different subjects, and — decisively — `trend` is a `Binding` resolved by the host, so a static enum cannot be a function of a value the emitter never sees. Admitted on ×7 cross-family SHOULD-level sightings. See the charter. |
| trend sentiment rendering | **Renderer-owned — was a live defect, fixed in the same phase** | Not vocabulary at all. `.fuaran-metric-trend` was painted `--fuaran-tone-success-fg` unconditionally in the reference CSS and its byte-copy, so **every** trend rendered as an improvement in every host, in both directions. Sign→sentiment is a renderer + CSS change needing no wire slot, and it fixed the default-polarity majority of the sighted demand on its own. A polarity slot without it would have changed nothing observable — which is why the phase shipped it first, as Part A. |
| sign inversion / "emit the trend already flipped" | **Rejected** | A −7.34% error rate printed as +7.34% is a false statement about the world. Polarity changes how a number reads, never what it says. |
| value→tone map on a trend (the `TonedPill` shape) | **Rejected** | `TonedPill` maps a discrete field value through a `Map`; a continuous trend would need a range predicate, which is an expression language in a slot — refused here on the same grounds 864 refused boolean combinators. It also re-encodes tone where the missing fact is direction. |
| `Neutral` polarity (a quantity with no better direction) | **Reserved, not admitted** | No §1.1 evidence. Reserved as a third case of the enum so a later admission is a bare-string addition rather than a boolean's replacement — which is the whole reason the slot is an enum and not `inverted: bool`. |

**Host adoption is PARTIAL, and this is the surface the next host sweep reads.** Recorded here by
Phase 873; Phase 867's Outcome undertook to leave the note and left only the CSS-defect row above,
which is the row describing a defect that was FIXED — so the standing gap was recorded nowhere. The F#,
TypeScript, Swift and Kotlin surfaces read `trendPolarity` and project the sentiment with a non-colour
channel. **Go, Python and Rust still paint the trend unconditionally and do not read the field**, which
means the constant-green defect Part A fixed in the reference tiers is still live in those three: a
falling error rate and a falling revenue both read as improvements. `nodes/metric-inverted-polarity.json`
exists to gate the port, so each is a mechanical follow-up against a fixture rather than a design
question. `Email.fs`'s email-safe projection is deliberately NOT in that list — it has no class
vocabulary and tones the trend with the TILE's tone, so it never carried the defect.

**Reading of the taxonomy:** of ~20 reserved candidates, the overwhelming majority resolve to
**variant / composition / role / covered** – only `NavBar`/`Menu`, a single consolidated `Media`, and a
provisional `Tree` and `Calendar` are even *reserved* as genuine kinds, and each is admission-gated on
§1 demand evidence. That distribution is the charter's thesis made concrete: **most vocabulary demand is
not a new kind.** The interaction cluster added in 2026-08 sharpens it a second way: of four intents
that each arrived as a filed gap, two resolved to an existing mechanism and two resolved to *nothing at
all* — **most vocabulary demand is not vocabulary.** The form-constraint cluster added in 2026-08
sharpens it a third time, and this one cuts *inside* the redirect: of five intents that each arrived
proposed as a `FormFieldKind` variant, four resolved to a single optional **field** on a record and
one resolved to host chrome. Not one earned a case. So — **most vocabulary demand that survives the
kind test is not a case either**, which is the sentence §2.1 exists to make checkable rather than
lucky.

---

## Appendix B – Demand-evidence sweep (recorded 2026-07-07)

This is the recorded output of the demand-evidence sweep the charter mandates before publication: a mine
of the instruments the language already has, to determine whether a **deliberate pre-publication
kind-admission batch** exists. The verdict feeds two decisions – the batch list below (a follow-up
feature-proposal input) and, because a kind-forcing gap would mean the contract is *not* settled, the
language-settled gate ([`LANGUAGE-SETTLED-CHECKLIST.md`](LANGUAGE-SETTLED-CHECKLIST.md) item (b)/(c)).

### Sources swept + verdicts

| Source | What it records | Sweep verdict |
|---|---|---|
| **`Custom`-node usage in shipped case studies / demos** (the FUARAN054 "couldn't express" ledger) | Patterns a real translation had to drop to `NodeKind.Custom` to render | **No kind-forcing entry.** The consolidated multi-app harvest concludes every surfaced gap closed *additively* – an author-surface helper or a portability seam – and explicitly records **no `NodeKind.Custom` escape was needed** across the three structurally-distinct views exercised. |
| **Real-app translation exercises** (three structurally-distinct consumer views: form-heavy, data-grid/upload/drill-down, selection-driven analysis) | Whether translating a real view forced a new core `NodeKind` / `Spec` / `Binding` / `Action` case | **No kind-forcing gap.** All harvested gaps were additive author-surface / portability-seam items (each shipped or recorded); **none forced a new core wire case.** This is precisely the settle-checklist item (b) signal. |
| **Emission-eval failures** (canonical-prompt runs classified as *"no expressible tree – missing contract"* vs low quality) | Prompts the vocabulary cannot satisfy at all | **No recorded miss.** The kind-confusion metric and the release-gate emission micro-eval are the producing instruments. The confusion metric's first baseline landed 2026-08-16 (below); the release-gate eval's first canonical-set pass has not. No *"missing contract case"* outcome is on record from either. |

### The evidenced pre-launch batch

**Result: the evidenced pre-publication kind-admission batch is currently EMPTY.** No candidate presently
clears the admission checklist's §1.1 demand gate on recorded evidence. Every gap the language's own
instruments have surfaced to date closed as a variant, an author-surface convenience, or a portability
seam – not as a new kind.

This is a **settle-positive** result, not a gap: an empty batch means the contract did not have to grow a
structural case to express the real views exercised (settle-checklist items (b)/(c) point the same way).

**Instrument status (updated 2026-08-16 – the batch verdict is unchanged).** One of the two
still-landing instruments has now landed: the **kind-confusion metric captured its first live baseline**
on 2026-08-16 – **12.5% valid-but-wrong-kind** (1 substitution over 8 scored prompts against
`Fuaran.UI.Ops` 0.26.0, one cohort, one model), with a single substitution pair, `Toast → Callout`. The
baseline carries a decoder + prompt-set + cohort provenance stamp, so a pre/post delta against it is
attributable rather than merely comparable.

**That result is not §1.1 demand evidence, and the distinction is the point.** A confusion is a
*selection* error among kinds that all exist – the emission was valid, renderable, and reached for the
wrong one of two kinds the language already has. It is evidence about **learnability**, which is what
§3's confusion guard weighs, and it says nothing about expressibility. Only a *"no expressible tree –
missing contract"* outcome feeds the §1.1 gate, and the first baseline produced none. So the batch stays
**EMPTY**, and stays **open** on the remaining instrument – the release-gate emission eval's first
canonical-set pass. If either surfaces a valid-but-unexpressible
pattern that clears §1, that candidate feeds a follow-up feature-proposal pass, designed against the
named real consumer, shipped with recipe + eval seeds, confusion-checked, and landed **before** the OSS
flip (so it needs no post-1.0 profile bump) and before the IDL codegen's full-breadth migration (so the
IDL carries it). The unevidenced tail – the Appendix A reservations without demand – stays post-launch
and demand-paced.

### Re-run trigger

Re-run this sweep (and update this appendix) when: ~~the confusion metric's first baseline run lands~~
(**fired 2026-08-16** – swept, verdict unchanged, see the instrument-status note above); the
release-gate emission eval completes its first canonical-set pass; or a fourth real-app translation
exercise surfaces a gap. A non-empty batch result promotes to a feature-proposal pass under this
charter's gates.

**A confusion-metric re-run is not a sweep trigger on its own.** The trigger above is the metric's
*first* baseline – the moment it stopped being an absent instrument. Subsequent runs measure a rate, and
a rate cannot promote a candidate into the batch, because §1.1 asks whether a pattern is *expressible*
and the metric only ever reports on patterns that were. A rising rate fires §3.3's merge review instead,
which is a different gate with a different remedy (merge two near-synonyms; do not admit a third).

---

## See also

- [`STABILITY.md`](../STABILITY.md) – version-bump classification of a kind change (this charter governs
  *whether* the change is admitted; STABILITY.md governs *what version bump* it is).
- [`WIRE_FORMAT.md` §11](WIRE_FORMAT.md#11-forward-coupling-rule-load-bearing) – the forward-coupling
  rule every kind/variant addition obeys.
- [`LANGUAGE-SETTLED-CHECKLIST.md`](LANGUAGE-SETTLED-CHECKLIST.md) – the settle gate whose items (b)/(c)
  consume the demand-evidence sweep's "no new core case" result.
