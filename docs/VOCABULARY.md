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
- **An emission-eval miss.** An AI consumer emitting the canonical prompt corpus produced a
  *"no expressible tree – missing contract case"* outcome for a prompt (distinct from a low *quality*
  score, which is a legitimate eval result and is **not** admission evidence).

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

Any development plan that **changes the kind set** – an addition, a variant addition, a merge, or a
retirement – cites this charter and satisfies its gates in the plan body: the demand evidence (§1.1), the
irreducibility statement (§1.2), the cost acknowledgment (§1.3), and the pre/post confusion delta (§3.2).
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
that is a positive design position rather than a deferral).

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
| `Media` (→ `Video` / `Audio` variants) | **Kind** (reserved) | `<video>`/`<audio>` carry distinct controls + captioning a11y no existing kind expresses. Reserve **one** `Media` kind with a `MediaKind` variant DU (Video / Audio), not two kinds – the variant rule applied pre-emptively. |
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

**Reading of the taxonomy:** of ~20 reserved candidates, the overwhelming majority resolve to
**variant / composition / role / covered** – only `NavBar`/`Menu`, a single consolidated `Media`, and a
provisional `Tree` and `Calendar` are even *reserved* as genuine kinds, and each is admission-gated on
§1 demand evidence. That distribution is the charter's thesis made concrete: **most vocabulary demand is
not a new kind.** The interaction cluster added in 2026-08 sharpens it a second way: of four intents
that each arrived as a filed gap, two resolved to an existing mechanism and two resolved to *nothing at
all* — **most vocabulary demand is not vocabulary.**

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
