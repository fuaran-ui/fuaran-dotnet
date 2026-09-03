# Fuaran UI — language decisions (newest first)

Decisions about the **language contract** — the wire model, its vocabulary, and the rules a
conformant host must obey. Narrower than the roadmap (which records *work*) and wider than
`VOCABULARY.md` (which governs only whether a `NodeKind` may be admitted).

A decision earns a place here when it constrains every host and would otherwise have to be
re-derived from the code. Each entry states what was decided, what was rejected, and the
consequence.

---

## 2026-09-02 — D6: writing direction is a DOCUMENT fact and a BOUND-TEXT fact; it is not on the wire

**Decided.** Direction enters a Fuaran page at exactly two places, and neither is a wire slot.

1. **The document declares one direction, derived from its locale.** A host says which language the
   page is in — `LocaleSource.Explicit "ar-EG"`, or its ambient locale — and `lang` + `dir` on
   `<html>` follow from that one declaration. The direction is never passed separately, so the two
   cannot disagree. `Formatting.textDirection` is the derivation, shared by every arm.

2. **A display leaf whose text is RUNTIME-BOUND carries `dir="auto"`.** The renderer emits it on the
   node wrapper; the browser then resolves that element from its own first strong directional
   character. The policy is `Accessibility.isBidiIsolated`, kind-level and total, so a new kind
   forces an answer rather than defaulting to silence.

Everything between those two — the layout — is handled by CSS logical properties in the reference
stylesheet, which mirrors from the document's `dir` with no override block and no second sheet.

**The slot set, and why it is that set.** `TextSource.Bound` only. `Literal` is the author writing in
the document's language and `I18n` is a host-resolved translation of the document's own locale; both
are already correct under the document's `dir`, and `auto` on them would let one stray character
re-lay-out a line the author controls. `Bound` is the only case whose direction genuinely cannot be
known at author time. And only on a **display leaf**: `dir` inherits, so `auto` on a layout container
would resolve ONE direction from the first strong character anywhere beneath it and impose it on
every child — the opposite of what a mixed-direction page needs. Data surfaces that own per-cell
emission (`DataGrid` / `Chart` / `Map`) are excluded for the same reason, and `Custom` because this
library cannot know what a host-rendered body puts on the page.

**The wire question — ASSESSED, and the answer is NO.** A per-node direction override was considered
and is **not** admitted to the wire. The vocabulary charter's gate asks for a demonstrated
inexpressible case, and none was found:

- *A page in one language.* Expressible: the document's locale.
- *A page whose data is in another language than its chrome.* Expressible: `dir="auto"` on the bound
  leaves, which is strictly better than a wire slot because it needs no emitter to know the
  direction of a value it is passing through.
- *A region deliberately fixed against its content* — a code block, a table of Latin identifiers on
  an Arabic page. Expressible: `ExtraAttributes` does not carry `dir` (its allowlist is `data-*` /
  `aria-*`), but a **host** composes such a region in its own shell or its own `Custom` renderer,
  which is where a deliberate override belongs; a tree emitted by a model has no business asserting
  one.
- *A single mixed run inside one text value* — Arabic and English in the same sentence, each needing
  its own embedding level. **Genuinely not expressible**, and it is not expressible by a per-node
  slot either: it needs per-SPAN markup inside a text value, which is a markup question
  (`NodeKind.Markdown`'s territory) rather than a direction question. Recorded as the one open case;
  it does not motivate the slot that was under consideration.

So the wire is unchanged by this phase, which is the outcome the assessment was there to establish
rather than to assume.

**Rejected.**

- **A `dir` slot on every node.** Would put a presentational attribute in a semantic tree, and every
  emitter would then have to decide it for every node — with no better information than the browser
  has, and usually worse, since the emitter often does not know the value being bound.
- **Emitting `dir="ltr"` / `dir="rtl"` rather than `auto` on bound leaves.** Requires the renderer to
  guess a direction from a string, which is exactly the analysis the browser already performs
  correctly and to spec.
- **An `[dir="rtl"]` override block in the stylesheet.** Mirrors the page but cannot un-mirror a
  nested LTR island, which is the case a real mixed-direction document is made of. Logical properties
  scope correctly by construction; the catalog's `?dir=rtl` harness exists to show the difference.

**Consequences.** No wire change and no vocabulary change. The reference stylesheet's inline-axis
rules are now logical, so **every host tier's byte-copy inherits the mirroring** — but a tier's own
document SHELL does not, and each must derive `lang`/`dir` from its own locale seam
([Phase 1128](../../roadmap/phases/1128-platform-baseline-host-adoption.md)). The Giraffe shell's
hardcoded `lang="en"` is gone: a shell that declares no locale now emits no `lang` and no `dir`,
because asserting English about a document nobody made a statement about is the defect this phase
exists to remove, not a safe default.

Roadmap: [Phase 1114](../../roadmap/phases/1114-rtl-bidi-document-language.md).

---

## 2026-08-20 — D5: this tier owns its vocabulary; Fuaran.Core ships only the engine

**Decided.** The IDL **engine** is a package — `Fuaran.Core.Idl`, distributed from Fuaran.Core 0.4.0
alongside the rest of the spine. A **vocabulary** — the `Idl` value describing this language's kinds,
unions, enums and records — is not something the engine's repo distributes. It is data belonging to
the domain whose contract it describes, and for the UI wire model that domain is this repo.
Fuaran.Core's DECISIONS.md D14 states the same rule from the other side.

**Rejected: a shared vocabularies package.** The UI vocabulary sitting in a `Fuaran.Core.*` package
would make every kind this language admits a release of the substrate repo, put one release cadence
across every domain's wire, and bake a single domain's name into the substrate's identity
permanently. `VOCABULARY.md`'s admission gates are written and enforced here; the artefact they
govern should not live where the gate does not run.

**The consequence for a second domain, which is the point.** Before 0.4.0 the engine was
`IsPackable=false`, so no workspace could consume it and this tier was necessarily the only domain
using it — via a byte-copy of the generated artefact out of the substrate repo's own test project.
That is not a distribution mechanism; it is the absence of one. Any domain can now
`PackageReference` the engine, declare an `Idl`, and generate its structural layer without either
the declaration or the output passing through anyone else's repo.

**What has NOT moved yet, stated plainly rather than implied.** `uiIdl` and its declared-support
record are still in Fuaran.Core's test project, because they are also that repo's only full-scale
engine-certification fixture — seven suites read them to certify corpus byte-parity, the
compiled-codegen drift guard, the schema leg, the op leg, the diff classifier and the cross-host
fuzz. Neither route out is sound today: a package dependency from the substrate onto this tier
closes a cycle in the feed (this tier already depends on `Fuaran.Core.Idl`), and a compile-link
across a sibling checkout would make that repo's build depend on a checkout it does not control.
Nor can `idl.json` stand in for the declaration — `Artifact` renders, it does not parse, and the
generator needs the declared-support record and the host prelude besides.

So the vocabulary's arrival here is staged, and `scripts/sync-generated-layer.ps1` is **demoted
rather than retired**: its default is now the drift check, and the byte-copy is behind an explicit
`-Adopt`. The copy stops being the mechanism and becomes an opt-in adoption step — which also
removes a live hazard, since a bare run of the old script once erased 292 lines of a tier that was
legitimately ahead. The check is enforced on every push by `apply-parity-fable.yml`, so the two
copies stay pinned rather than trusted.

**When it retires.** Once the vocabulary lands here, the check becomes an in-process
regenerate-and-compare against the packaged engine, with no sibling checkout involved at all. The
`.fantomasignore` entry for `src/Fuaran.UI/Generated.fs` survives that change unaltered — the file
is generated output either way.

**CLOSED 2026-09-03 (fuaran#1181).** The staging above is finished, and the two paragraphs before
this one are kept as the record of why it took two steps rather than as a description of the tree.
What changed: `fuaran-core#114` made `Artifact.parse` a total inverse of `Artifact.render` and gave
the declared-support record and the host-prelude reference a canonical document of their own
(`support.json`), which removed the blocker this decision named — "`Artifact` renders, it does not
parse, and the generator needs the declared-support record and the host prelude besides". So the
vocabulary is here, in `src/Fuaran.UI.Idl/`: `Vocabulary.fs` and `Support.fs` (the declaration), and
`idl.json` + `support.json` rendered from them and committed beside them. The third member of the
triple, the host prelude, was already here — `src/Fuaran.UI/HostPrelude.fs`, which the support
document NAMES rather than inlines.

`scripts/sync-generated-layer.ps1` is deleted, and `apply-parity-fable.yml`'s job is the in-process
check: `src/Fuaran.UI.Idl.Tests` parses the two committed artifacts **from bytes**, emits the
structural layer through the packaged `Fuaran.Core.Idl` / `.Codegen`, and byte-compares
`src/Fuaran.UI/Generated.fs`. One checkout, no sibling repo, no token, and — the property the old
guard lacked — it FAILS rather than exits 0 when it cannot find what it certifies against.

**No cycle was closed in the feed**, which is what this decision was most careful about. The
dependency direction is unchanged: this repo consumes `Fuaran.Core.Idl` and `.Codegen`, the
substrate consumes nothing of ours. `src/Fuaran.UI.Idl` is `IsPackable=false`, so the engine
dependency stops at the vocabulary and never reaches a shipped `Fuaran.UI.*` package.

**What is left on the substrate side** is the fixture's deletion, which is `fuaran-core#123`'s work
and depends on this landing first — deliberately, so no interval exists in which the domain has no
vocabulary. Until it runs, `tests/Fuaran.Core.Tests/UiIdl.fs` and `UiIdlSupport.fs` still hold the
first ~2,492 and ~423 lines this repo now owns; the remainder of each file, and `UiGenerated.fs` and
`UiHostPrelude.fs`, are that repo's own certification fixture and are its call, not ours.

## 2026-08-20 — D4: the accessibility projection targets the node's semantic element, not its wrapper

**Decided.** A node's `Accessibility` projection — and the `aria-*` half of `ExtraAttributes` — is
emitted on the node's **semantic element**: the single element the kind body renders, when that
element rather than the wrapper carries the node's semantics. The set is closed and currently three
kinds: `Link` (`<a>`), `Button` (`<button>`), `Image` (`<img>`). Every other kind keeps the
projection on the wrapper `<div>`, unchanged.

Membership is a property of the body, tested by three conditions that must all hold:

1. the body is a **single root element** — not a container of siblings, not a label-wrapped control;
2. that element carries **native semantics of its own** (an interactive role, or a graphic), so a
   `role` / `aria-*` on an ancestor `<div>` is announced against the wrong node;
3. the element **is** the node — nothing else in the body competes for the accessible name.

Before this, every renderer had exactly one a11y emission site and it was the wrapper, with no seam
by which a kind could opt in: `renderKind` was never passed `node.Accessibility` in any tier. So the
placement was not a per-kind decision that happened to be wrong for `Link` — it was a uniform
architecture that had never been asked the question. The observable cost: a nav projection marking
the current page with an `aria-current` extra-attribute put it on the wrapper `<div>`, and the host
had to reach the anchor with a descendant CSS selector; and `AriaRole.Link` / `AriaRole.Button` on a
node whose body already renders an `<a>` / `<button>` announced two interactive elements where the
author declared one.

**Rejected — a per-kind opt-in list.** It has no falsifier: a kind joins when someone remembers, and
nothing tells a reviewer whether a new kind belongs. Stating the rule as a property of the body means
the vocabulary charter (`VOCABULARY.md`) can answer it once, at admission, for every kind that
follows.

**Rejected — including the form-field kinds** (a tempting reading of "the body is the semantic
element"). `Select` renders `<label><span>…</span><select></label>`: the control is not the body root
(condition 1) and the wrapping `<label>` already supplies an accessible name (condition 3), so a
forwarded `aria-label` would compete with the one the author wrote. Field-level targeting is a
different feature — it needs a per-kind target selector, not this predicate — and is deliberately not
in this rule.

**`ExtraAttributes` splits by prefix, deliberately.** `data-*` stays on the wrapper because it is
addressing: it sits beside `data-fuaran-node-id`, which layout observers, DOM-snapshot hooks and the
in-page introspection surface scan for, and moving it would move the node's address. `aria-*` follows
the projection, because an `aria-*` hatch is an accessibility attribute and belongs where the
accessibility attributes are — that half is the whole of the `aria-current` defect above. On a
non-forwarding kind the split is unobservable: both halves land on the wrapper, in the same
key-sorted order as before.

**Two limits, stated rather than assumed away.** The protected-email `Link` variant renders an
entity-encoded opaque anchor string on the server tier, so the projection lands on its wrap `<span>`
— the only element that arm owns in *both* tiers, and client/server parity outranks reaching one
tier's anchor; writing attribute names into a `dangerouslySetInnerHTML` payload would open a new
injection seam and was declined. And the predicate is **kind-level** by construction, because the
wrapper must decide before the body is rendered and the only thing it has then is the `NodeKind`;
where an arm has a runtime branch, the arm owns placement within its own body.

**Consequence.** Breaking for consumers pinning rendered DOM — host CSS or DOM-snapshot tests
selecting `role` / `aria-*` on a `Link` / `Button` / `Image` wrapper now find them one level in. The
wire is untouched: no field, no encoding and no fixture moves, and a decoded tree renders the same
information at a different address. Every conformant host that emits DOM owes the same placement, or
the emitted DOM forks by host.

---

## 2026-07-28 — D3: the IDL reaches full `Binding` parity by hosting foreign codecs, not by re-modelling them

**Decided.** The generated `Binding<'T>` now carries every case the hand-written encoder can emit
(`Transform`, `Selection`, `I18n`, `Filter.defaultValue`, and host-only `accessor` slots on `Query` /
`Selection` included), and the full 85-fixture node corpus round-trips through the generated layer
alone. Four modelling decisions carry it, each a shape the swap (692–694) inherits:

1. **A hosted slot (`THosted`) delegates to a host codec instead of re-modelling its vocabulary.**
   `Binding.Transform`'s `source` / `pipeline` are real `Fuaran.Core.DataSource` / `Fuaran.Core.Transform`
   values encoded by `ColumnCodec` / `DataFrameCodec` — the codecs the evaluator already trusts, under
   the same `Canon` discipline, so the composite splices in canonical. Re-modelling the DataFrame
   vocabulary as IDL unions was rejected: it would mint a second set of types beside the ones
   `DataFrame.evalPipelineInEnv` consumes, and the two would drift. Everywhere except the generated F#
   (schema, TS backend, interpreter, sampler) a hosted slot is verbatim JSON, exactly like `TJson` —
   its content is the host codec's business.
2. **Obj-erased binding positions instantiate at `JVal`.** `Transform.params[].from` and `I18n.args`
   are `Binding<obj>` in the hand-written tier, encoded best-effort; the generated layer uses
   `Binding<JVal>` — typed, verbatim, byte-faithful. `TOpaque` was rejected for these positions: it
   erases a real `defaultValue` to a sentinel, which is silent data loss (the D1 argument, again).
3. **Absence is authorable.** Every control `value` slot (and `Modal.onDismiss`, and the grid's
   `value`/`field`, `rowKey`/`rowKeyField` siblings) is `option` in the generated types because the
   wire says absence is legal (Phase 596 auto-bind, Phase 425/426). The context-dependent synthesis —
   absent value ⇒ `Filter(name)` / `State(field id, placeholder)` — stays policy in the hand-written
   decoder ABOVE the structural layer; structure carries absence as absence. Consequence for the swap:
   the generated types can express "auto-bind me" (`None`) where the hand-written types must replicate
   the synthesis by hand — strictly more expressive, and a shape change at every construction site.
4. **The `Range` value's transparent Static is a SLOT convention, not a union one.** `Binding.Static`
   of a range pair encodes as the bare `{"max":…,"min":…}` object; other cases stay `$type`-tagged.
   The slot carries its own hosted codec over the generated `encBinding`/`decBinding` + a `RangePair`
   record (the IDL has no tuple type; the record IS the wire object, so the hand-written
   `float * float` becomes `RangePair` at the swap).

**Not modelled — `Deferred<'T>` (`Pending`/`Ready`/`Error`).** It is not a `Binding` case and not wire
vocabulary at all: it is the resolver's runtime envelope for an `Invoke` in flight ("a runtime value;
not wire-serialised"), and the corpus carries no occurrence. It stays a hand-written runtime type,
untouched by the generated layer and by the swap.

**Also corrected in passing.** The IDL's separate `FilterKind` union was pre-0.2.0 drift — the
hand-written `FilterSpec` holds a `FormFieldKind` (the filters-unification), so the IDL now does too,
and the drifted union is deleted.

---

## 2026-07-27 — D2: a closure's HOST type is free, so the generated layer can be the authoring type

**Decided.** A function-typed slot declares its host signature in the IDL (`TFn`), and any type
transitively holding one is generated generic in `'Msg`. The IDL-generated structural layer therefore
*is* the authoring type — `Node<'Msg>` — rather than a `'Msg`-erased projection of it. There is no
handler table, no closure address, and no author facade.

**Why.** The encoder never reads a closure. It emits the fixed sentinel `"<closure>"` and moves on;
the decoder reads presence only. Nothing downstream of the declaration depends on what the slot's
host type is — so the host type was always free, and erasing it to `unit` was a convenience of the
first generator (Phase 317), not a property the wire requires. Once that is seen, the `'Msg` can stay
in the tree exactly where the hand-written tier already puts it.

**Rejected — the handler table.** The programme was planned around addressing each closure by
`(nodeId, slot)` or a positional path, holding the closures in a side table keyed by that address,
and reuniting them at render time. Four things were wrong with it, in ascending order of severity:

1. It needs an addressing scheme, and both candidates have real costs — a path is invalidated by any
   `TreeOp` that moves a node; a minted id must ride the wire or a side channel.
2. It needs `TreeOp` laws for what happens to a binding when its node is replaced, moved or deleted —
   a whole phase of work, and a binding that silently survives onto a *different* node is worse than
   one that errors.
3. It puts `'Msg` on a *pair* while the tree stays monomorphic, so every signature in the renderer and
   apply engine threads a type parameter that no longer matches the thing it describes.
4. **It solves a problem that does not exist.** All of the above is machinery for reattaching
   something that never had to be detached.

**Rejected — the projection** (`toGenerated : Node<'Msg> -> Generated.Node`, feeding the generated
encoder). A case per kind and a line per field is comparable in size to the 2,282-line encoder it
replaces, `Types.fs` survives, and the mirror count does not fall. It buys a rename of where the
mirror lives at the price of a second surface to keep in step.

**Corrected — node ids ARE unique.** The programme recorded "node ids are not unique" as settled
evidence, on the strength of 4 of 85 corpus fixtures repeating one. Measured rather than read: the
tier's own `PreEmitValidate` refuses **exactly those 4** with `DuplicateNodeId`, the validator's
`FUARAN001` is an *Error* citing "§4g op-target stability", the session-bundle path refuses such a
tree outright, and every `TreeOp` addresses by `NodeId` alone — so a duplicate is already ambiguous
for `UpdateProp` and `RemoveNode`, quite apart from handlers. Uniqueness is an invariant the corpus
violates, not a property the language lacks. This does not revive the handler table (it is rejected
on its own merits above), but the record should not stand on a false premise. The four fixtures are a
corpus defect, tracked separately.

**Consequences.**

- **The wire is untouched.** `UiGenerated.fs` regenerates byte-identical, and no corpus fixture moves.
  `'Msg` is a host-side type parameter; it has no wire projection.
- **The decoder is the storage shape.** A closure cannot be rebuilt from `"<closure>"`, so
  `decodeNode` returns `Node<obj>` with declared placeholders in the closure slots — the same
  boundary the hand-written `decodeNodeObj` / `WireTree` already draws, reached independently.
- **`'Msg` does not leak.** The fixpoint puts it only on types that genuinely dispatch: on the real
  vocabulary `Binding<'T>` stays msg-free, because the tier already obj-erases exactly where a `'Msg`
  parameter would be inconvenient (`LocalBinding.OnCommit: 'T -> obj`, `Action.Call`'s
  `onResult: obj -> 'Msg`). The spike proves the fixpoint respects that.
- **Fable is unaffected**, checked rather than assumed: the generated shape is now compiled by the
  `fable-smoke` gate on every verify run.

**Open questions this leaves for adoption.**

1. **Host types in a signature.** `TFn.FSharp` is free text, so a signature naming a type the
   generated module does not have in scope (`BindingContext`, `FileRef`) will not compile. Either
   those types move into the IDL or the module needs a configurable `open` prelude.
2. **`Action.Dispatch of 'Msg` is not a closure.** Its `'Msg` is a wire-*omitted* value, not a
   sentinel — a distinct case from `TFn`, needing a "host-only field" optionality that `Node.Motion`
   and `Node.ExtraAttributes` (`WIRE_FORMAT.md` §9) would share.
3. **A per-type-parameter placeholder.** `Binding<'T>.Computed.fn` decodes to a placeholder that must
   produce a `'T`, which cannot be conjured. The hand-written decoder threads a real placeholder value
   per parameter; the generated one should take one alongside each `decT` codec.

**Where it is enforced.** `Fuaran.Core.Idl.Gen.msgCarrying` (the fixpoint); the `Idl.Spike` mini-IDL,
whose `Tabs` kind and `Binding.Computed` case exercise all three legs and are compiled, snapshot-
gated and Fable-gated on every run.

Roadmap: [Phase 689](../../roadmap/phases/689-handler-binding-design-and-spike.md), and the programme
at [generated-authoring-programme.md](../../docs/domain-explorations/generated-authoring-programme.md).

---

## 2026-07-26 — D1: the wire model has no `null`; absence is structural

**Decided.** `null` is not a value in the Fuaran wire model. It appears in no canonical emission,
in any position, on any host. Absence is expressed **structurally**, by omitting the key.

**Why.** F#'s rejection of null is one of its strongest features, and null is the standing regret of
most languages that carry it. A value that inhabits every type defeats the point of having types.
Fuaran is a typed tree with a canonical serialisation; admitting null would put a hole in every slot.

**What this changed.** The format already said so — rule 4: *"never synthesise `null`"* — but two
positions carved themselves out and emitted it: the obj-erased `Binding.Static` seam, and
`Binding.State.defaultValue`. Both now omit:

| Before | After |
|---|---|
| `{"$type":"Static","value":null}` | `{"$type":"Static"}` |
| `{"$type":"State","defaultValue":null,"key":"k"}` | `{"$type":"State","key":"k"}` |

**Rejected.**

- **A dedicated `Unset` binding case.** Does not compose: `State` still needs its `key`, so you would
  carry `Unset` *and* the key — two concepts where one suffices — and it costs a vocabulary-charter
  admission for a case that adds no information.
- **Typed empty** (`""` / `[]` / `0`). Conflates "no selection" with "selected nothing". That is
  null's trap wearing different clothes, and it is worse than null because it is undetectable.
- **Adding `JNull` to `Fuaran.Core.Wire.JVal`** so the substrate could represent what the wire
  contained. This was the obvious fix and it is the wrong direction: it would have propagated null
  *into* the shared substrate, breaking a public DU across three estates to preserve a mistake.

**Consequences.**

- **Decoders still accept `null`** at those two positions as a §16 lenient shorthand for absence,
  because models emit null naturally and the intent is unambiguous. Accepting is not emitting:
  `encode(decode(x))` never reproduces a null. Everywhere else (rule 12 structured-payload
  positions) null remains a hard decode error, pinned by six `reject/reject-null-*` fixtures.
- **`Fuaran.Core.Wire.JVal` stays null-free and is now correct.** Before this decision its parser
  refused null while the format emitted it, so the substrate could not parse two of the corpus's own
  fixtures. The substrate was right; the format was wrong.
- **A silent-corruption path was deleted rather than fixed.** The tier bridged its own JSON AST to
  Core's with `JNull -> JStr ""`, justified by an unreachability assumption. With null gone from the
  model there is nothing to bridge.
- Breaking for **emitters** only: six `nodes/` fixtures and two `lenient/*.expected` changed bytes.

**Where it is enforced.** `WIRE_FORMAT.md` §2 rule 4 (normative, no exception); the encoder in each
of the five hosts; the corpus, which contains no `null` outside `reject/` and the two lenient
*inputs* that prove the shorthand still decodes.

Roadmap: [Phase 677](../../roadmap/phases/677-reject-null-absence-is-structural.md).
