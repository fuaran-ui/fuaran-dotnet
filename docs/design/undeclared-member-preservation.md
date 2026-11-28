# Keeping an undeclared member — the mechanics

**Status:** design for Phase 2879, **ruled 2026-11-28**: all four recommendations accepted as written
(section 8, and `docs/DECISIONS.md` D45). The implementation, on every host, is Phase 2880. This
document changes no host and no wire member.

**The ruling this document builds on (2026-11-28).** A member that a kind does not declare is **kept** on
re-encode, on every host and every kind. Nothing below reopens that ruling. This document answers how a
member is kept, in four questions:

1. Carriage: where a host holds the member.
2. Canonical form: what bytes it re-encodes to.
3. Identity: whether it takes part in equality, hashing and diffing.
4. Gates: what validators, policies and ops do with it.

## 1. What the hosts and the specification do today

Phase 2536 wrote the vector `lenient-2536-text-field-shaped-as-node`: a `Text` form field carrying the
node's `text` member. Its kind does not declare `text`. The vector was withheld, and `WIRE_FORMAT.md`
3.6.38 records the question as open.

To prepare this design, every host was probed over the same documents. Each document carries an
undeclared member `zzz` at a different position. The table records whether the member is kept (K) or
dropped (D) on re-encode:

| Position | reference (F#) | TypeScript | Go | Rust | Python |
|---|---|---|---|---|---|
| a node kind (`Heading`) | D | D | **K** | D | K |
| the node envelope (beside `id` and `kind`) | D | D | D | D | D |
| a form-field kind (the 2536 vector) | D | D | D | D | K |
| a form-field record (`FormField`) | D | D | D | D | K |
| an action case (`Chain`) | D | D | D | D | K |
| a binding case (`State`) | D | D | D | D | D |
| the accessibility record | D | D | D | D | D |
| a kept value that is `null` | D | D | writes `null` | D | writes `null` |

What the table shows:

- **No host is uniform.** Go keeps on node kinds and on the records its IDL table decodes (`wire/decode.go`
  `build`). It drops on the case objects it decodes by hand, the ones that call `buildStrict`. Python keeps
  on kinds, form fields, grid columns, filter items and tree items. It drops on the node envelope, on
  accessibility and on bindings.
- **The reference, TypeScript and Rust hosts drop at every position.** Their typed records and cases have
  no slot that could hold the member. The reference decoder looks each declared member up by name
  (`Generated.fs` `dReq` / `dOpt`) and never reads the rest. `Node`'s two host-only slots,
  `extraAttributes` and `motion`, are never written to the wire.
- **The two hosts that keep a member write a kept `null` back.** Rule 4 says a canonical emission never
  contains null, so this is a defect.

The split already costs more than a changed hash. The attested envelope recomputes its digest by
decoding a document and re-encoding it (§26.2). So, by that construction, a document that carries an
undeclared member at a position where the hosts disagree, signed on one side of the split, fails
verification on the other. This follows from §26.2's definition; it was not probed separately.

The specification speaks of unknown members in these places:

- **§2.1, forward tolerance.** An unknown key is "ignored on decode". Rule 2 itself only says that a
  decoder accepts any key order and looks a field up by name.
- **The schema note (§13, `schema.json`).** The schema sets no `additionalProperties:false`, to match this tolerance.
- **§3.1, the tooltip trait.** A `tooltip` inside a kind is "an unknown key, tolerated and ignored".
- **§3.1, the near-miss names.** The accessibility trait refuses an enumerated set of near-miss names
  (Phase 959). So do the grid (Phase 863) and the form field (Phase 864).
- **The `Print` arm (§3.6.14).** It refuses any member beside `$type`. Its text explains the asymmetry: "everywhere
  else in this format an unknown member is one the reading host has not learned yet, and dropping it is
  the forward-compatible answer".
- **§3.6.26, the field laws.** "An unknown member read past".
- **3.6.38.** Records the question as open.
- **§15 introduction.** Tolerance lives "only on the decode boundary of a consumer that is behind".
- **§15.3.** Must-ignore-but-preserve, for an unknown **kind**. It does not mention a member.
- **§15.4, the optional-field row, and the Phase 1670 paragraph.** An added optional field takes no
  profile step, because "§15.3's must-ignore-but-preserve keeps its bytes". Four hosts falsify that today.
- **§18.3, §26.1, the page document (§32.1) and the demanded document (§32.4).** These closed artefacts refuse an
  undeclared member.
- **§31.7.** A codec that predates a member "drops it on re-encode".

Phase 2880 reconciles this text to one voice.

## 2. Scope: which members this is about

A member is **undeclared** on an object when the object's shape, as the IDL and this specification
define it, names neither the member nor a lenient alias of it. Everything below is about undeclared
members, and only them. Four kinds of member look similar but are not undeclared. They keep their
present rules:

- **A declared alias or shorthand** (§16, §3.6) is read and normalised to its canonical spelling. For
  example, `title` becomes `heading` and `name` becomes `id`.
- **A declared normalisation** is applied as declared. Phase 1962's drop of a multi-select's `value` is
  one: `value` is declared on `Select`, and the rule that drops it is declared too. It is not an
  exception to keeping.
- **A retired member** is a hard error where the specification says so. For example, `source` on
  `Metric` and `LabelValueRow`, and the retired position members of ops. A retired member is declared,
  as a refusal.
- **An enumerated refusal** stays a refusal: a near-miss set, the `Print` arm, and every member of a
  closed artefact.

A **rule-12 payload** (a `Custom` prop, the payload of `Notify`, `SetState` or `AiTool`, an `I18n`
argument, an `UpdateProp` value) is already carried faithfully, whatever members it holds. Keeping adds
nothing to it.

The positions in scope are every object a host decodes against a closed table: the node envelope, every
kind, every record and every union case. This includes the members of a `TreeOp` object.

## 3. Question one — carriage

**The rule every host implements, whatever its carriage.** A kept member belongs to the JSON object it was
read from:

- It travels wherever that object travels.
- It is replaced when that object, or any object enclosing it, is replaced.
- It is never moved onto another object.

This is the behaviour the corpus pins. How a host stores the member is its own choice, provided the
vectors pass.

### The options

**(a) A bag on every object.** Every record and every union case holds the members it did not read, in
an opaque type that only the decoder can fill.

- Python already works this way. Its model is `Obj(tag, fields)`, and a kept member is one more entry
  in `fields`.
- So does Go at its `build` sites, through `Obj.Fields`.
- On a typed host, every record and every case payload gains a field.

**(b) One bag per node, keyed by the member's path inside the node.**

- A node holds every member kept anywhere in its own structure. The path stops at child nodes, because
  each child node has its own bag. Examples: `kind.zzz`, `kind.fields[2].zzz`, `accessibility.zzz`.
- An op object holds a bag of its own, because an op is never edited after decode. `EditNode`'s new
  kind is a kind with no node around it, so its members ride in its op's bag.
- The IDL can already declare a host-only slot on `Node` with a type per host. This is how
  `extraAttributes` and `motion` are declared, so the slot needs no generator change. It differs from
  those two in one way: its contents are written to the wire, at their paths, by the encoder.

**(c) The parsed document kept beside the typed tree.** A side table, keyed by path, holds the original
objects, and the encoder merges them back in.

### What each option costs

**(a) Per-object bag:**

- **Reference (F#):** a field on every record and every union-case payload. It breaks every record
  literal and every positional pattern match in every consumer: `FormFieldKind`, `Binding`, `Action` and
  `TextSource` cases are positional. `Generated.fs` is regenerated, but the hand decoder `JsonDecode.fs`,
  the C# and VB facades and `Defaults.fs` are each edited at every construction. Every author pays a
  permanent tax, writing an empty bag in every record literal.
- **TypeScript:** an optional symbol-keyed property on each decoded object. Types do not break, because
  the property is optional. Object spread copies it, but a hand-built fresh object in the decoder or in
  apply drops it silently, so every construction site must be audited.
- **Go:** exists at the 38 `build` sites (37 kind arms and the IDL-table record decoder). The 41
  `buildStrict` sites, the envelope and accessibility move
  to keeping, except where the specification enumerates a refusal.
- **Rust:** a field on every struct and every enum payload. It breaks every struct literal and every
  pattern match that does not end in `..`.
- **Python:** exists. The node envelope, accessibility and binding cases move to keeping, and the null
  case is fixed (question two).

**(b) Per-node bag:**

- **Reference (F#):** one host-only slot on `Node`, declared in the IDL, plus a bag returned with a
  decoded op. Neither `TreeOp` nor any record or case changes shape. The cost is inside the codec and
  the apply engine:
  - The decoder collects unread members by path.
  - The encoder writes them back at their paths.
  - Apply drops the entries under a member an op writes, and installs the entries an op's value brings.
  - Apply must re-key the entries when it rebuilds a list that holds no nodes, such as a nested
    `UpdateProp` into `TabHeaders[i]` (Phase 364).
- **TypeScript:** the same as F#. TypeScript is the host new hosts port their codec from, so one design
  across the typed hosts keeps that port honest.
- **Go and Python:** not needed. Their model is already (a).
- **Rust:** the same as F#. `Node` gains one field, and only `Node` literals break.

**(c) Side table:**

- **All hosts:** the table goes stale after the first op, so it needs everything (b) needs, over the
  whole document rather than per node. It also holds the document twice, with no gain over (b).

### The authoring surface stays closed

On every typed host, under (a) or (b), the bag's type has no public constructor and no public way to
add an entry. An author can write only the empty bag, and smart constructors write that for them. No
constructor can produce an undeclared member. Phase 2880 pins this with a test on each typed host.

Python and Go expose their structural model types publicly today. An author holding `Obj` can write any
member there already. Their typed authoring layers do not, and keeping changes neither.

### Behaviour meanwhile

A host that has not moved keeps dropping, as the table in section 1 shows. Its encoding of a tree that
carries an undeclared member differs from a moved host's. That changes the hash, forks a hash chain at
the first such re-encode, and fails cross-host attestation. The corpus treats the vectors as a family
of their own, demanded only of the hosts that declare it covered (§27.7), as §31.7 does for affordances.
Each host then adopts in its own change-set, and the family moves into the default set when all five
hosts declare it.

### Recommendation

Make the object-local rule above normative.

- **Python and Go:** keep using (a), through their existing structural map.
- **Reference, TypeScript and Rust:** use (b).

Option (a) on the typed hosts buys nothing the vectors can observe. It charges every author and every
consumer a breaking change on every record and case. Option (b) confines the whole cost to the codec
and the apply engine, where the tests live.

The one standing hazard of (b) is an apply rule that rebuilds a list holding no nodes without re-keying
its entries. It is closed by a property test per op family: apply an op, then encode. Every kept entry
not under a written member is still present, at its object's new path.

## 4. Question two — canonical form

### Where a kept member sorts

A kept member sorts among the declared keys of its object, by §2 rule 2's UTF-16 code-unit order. There
is no separate region for kept members at the end of an object.

This answer is forced. A host that declares the member emits it at its ordinal place, and a host that
keeps the member must produce the same bytes. A kept `$`-key sorts first, as every `$`-key does (§2.1).

### What a kept value re-encodes to

A kept member's value is re-encoded **canonically**, under rule 12, as a structured JSON payload:

- keys sorted at every depth;
- numbers under rule 5, where the token decides integer identity;
- strings under rule 6.

The value is not copied verbatim.

**Why canonical, not verbatim.** On canonical input, canonical re-encoding is a fixed point, so every
host produces the producer's bytes:

- `1E+21` stays `1E+21`.
- `2` stays `2`.
- A sorted object stays sorted.

This holds on canonical input because a conforming newer producer writes the member under rules 1 to 12
whatever type it declares. Verbatim copying would put non-canonical bytes inside a canonical encoding,
and two decoded values that are equal would then encode differently.

On non-canonical input, a host that keeps the member emits rule-12 canonical bytes. These can differ
from what a host that declares the member emits, where the declaring host applies a lenient
normalisation the keeping host does not know. That is the position every lenient input is already in,
and it is not a new class.

### A value holding null

A member whose value is `null`, or contains `null` at any depth, is **not kept**. It is dropped, which is
what three hosts do with it today. It is neither refused nor written back.

- Writing it back breaks rule 4.
- Refusing it would narrow what a host accepts: every host accepts such a document today. That is a
  §15.4-class change.
- No conforming encoder writes `null`, so dropping it loses nothing a producer authored. A host that
  declares the member and reads a null at one of rule 4's two lenient positions also normalises it to
  absent.

### Bounds

A kept value is held to the same §21 bounds as a rule-12 payload. The document-bytes and depth limits
already apply at parse.

### The bytes, for two hosts to agree on

Input:

```json
{"id":"a","kind":{"$type":"Heading","level":2,"text":"Hi","zzz":{"b":1.0,"a":[1,2]}}}
```

The only correct re-encoding:

```json
{"id":"a","kind":{"$type":"Heading","level":2,"text":"Hi","zzz":{"a":[1,2],"b":1}}}
```

The input carries `1.0`, a token with a `.`, so the payload reads it as a double, and rule 5 lays it out
as `1`. Python and Go produce exactly these bytes today.

### Behaviour meanwhile

A host that drops the member emits the document without `zzz`. Python and Go write a kept null back,
until Phase 2880 fixes it.

### Recommendation

As stated above:

- sort by the ordinal rule;
- re-encode canonically under rule 12;
- drop a member whose value holds a null.

## 5. Question three — identity

Every identity in this format is defined over the canonical encoding:

- the content hash;
- the op-stream hash chain, whose op bytes include an `EditNode` kind, an `InsertChild` node and a
  `ReplaceRoot` tree;
- the teleport digests;
- the attested-document digest (§26.2);
- the tree diff's equality, which is canonical-JSON equality (`TreeOpDiff`).

A kept member is in the encoding, so it takes part in all of them.

### The two options

- **In identity.** This follows from the encoding.
- **Excluded from identity.** This would need a second encoding without kept members, and a host that
  declares the member would hash it while a host that keeps it would not. Every chain and attestation
  would split across versions, which is what the ruling exists to stop.

The answer is forced: a kept member is in identity.

### Structural equality

On a typed host, structural equality includes the bag. Where structural equality is not available
(closure-bearing kinds), equality is canonical equality, which includes the bag already.

### The diff

No op names an undeclared member. An `UpdateProp` naming one answers `UnknownField`. So a change that is
confined to kept members has no field-level op:

- **Inside the kind:** the diff takes its existing `EditNode` floor, and the op's bag carries the new
  kind's members.
- **On the node envelope:** no op rewrites the envelope's members. The floor is `RemoveNode` then
  `InsertChild` at the same index, or `ReplaceRoot` at the root.

These floors cost more, but they are total, and they arise only between two trees that both came from a
producer that declares the member.

### The two gates that read the encoding

- **The op-stream chain** sees the kept member in each op's bytes. A host that decodes and re-encodes a
  replicated op reproduces those bytes only if it keeps op members too. This is why ops are in scope.
- **A pre-emit wire-shape check over the encoded bytes** sees an unrecognised key. It passes, as the
  schema does, which sets no `additionalProperties:false`.
- **A validator over the typed tree** does not see the kept member, except through the advisory finding
  of question four.

### Behaviour meanwhile

Today a dropping host and a keeping host give one document two hashes. A chain that passes through both
forks, and attestation between them fails (section 1).

### Recommendation

A kept member is in every identity, and the diff's floors are as above.

## 6. Question four — gates

The principle: **a gate binds the host that acts on content.** A host that keeps a member it does not
declare never acts on it:

- it never renders it, resolves it, fetches it, binds it, executes it or follows it;
- the AI introspection surface (`availableFields`) never lists it.

A host that declares the member decodes it through its own decoder, policy and validators, as it does any
member. So a kept member carries no content past a gate on the host where the content would take
effect. One case needs more: a deployment that relies on a host that keeps the member as the gate for a
consumer that declares it.

### Strict decode

- **The strict posture (§28.1) is unchanged.** It governs parse repair, not members.
- **The default decoder never refuses an undeclared member.** Refusing would narrow the wire, which §23.1
  forbids of a default decoder.
- **The enumerated refusals stay refusals:** the near-miss sets, the `Print` arm, retired members and the
  closed artefacts. A member on one of those lists is not undeclared.

### Kind admission policy (§23)

A kept member can hold a discriminated value: a node, an action or a binding, shaped like one the host
would decode if it declared the member. A host that keeps the member never decodes that value, so its
policy never sees what is inside. Downstream, a consumer that declares the member decodes it, and if
that consumer has no policy of its own, the content goes through.

The options:

- **(i) The policy ignores kept members.** This leaves the gap open.
- **(ii) Refuse, under a narrower policy.** Under any policy other than admit-all, a kept member whose
  value contains a `$type`-discriminated object at any depth is refused, with `KIND_NOT_ADMITTED` at the
  member's path, and the message says the policy cannot inspect what the member holds. Scalar members,
  enum tokens, flags and plain objects without a discriminator are kept.
- **(iii) Drop every kept member under a policy.** This is silent data loss, and it is the narrower rule
  in another form.

**Recommendation: (ii).**

- A policy is a declared promise of closure, and a discriminated value the policy never decoded is
  exactly what it cannot vouch for.
- The refusal is logged and attributable, as §23.4 requires of a refusal.
- The commonest optional additions are a string, a number, an enum token or a flag, and all of them
  still pass.
- A decoder given no policy (admit-all) keeps everything, because it promised no closure.

### Validators

A validator judges declared content. A pass says nothing about kept members.

**Recommendation:** an authoring-facing validator also reports each kept member as an **advisory**
finding, never an error, naming the member and its object. The member is then not silent. The 2536 vector
is an author's mistake, and under keeping it would otherwise round-trip unremarked forever. This is the
near-miss argument of Phase 959 applied to every name not on a near-miss list. It needs one new finding
code, which is an additive change.

### What an op does to a kept member

- **No op can write or name one.** `UpdateProp` names a declared member; an undeclared name answers
  `UnknownField`.
- **An op that writes a declared member replaces the member's whole value,** including any kept members
  inside it. The op's own value brings the kept members that its bytes carry.
- **`EditNode`** replaces the kept members of the kind with those of the new kind it carries.
- **`RemoveNode`** removes the node's kept members.
- **Child-structure ops** (`InsertChild`, `MoveNode`, `ReorderChildren`) touch no kept member of the
  parent.
- **Every other object** keeps its kept members through every op.

### Behaviour meanwhile

A dropping host has nothing to gate. Python and Go keep members today and apply no policy to them, so
the gap in the policy section is open on those two hosts now. Phase 2880 closes it, with a test on each
host that a kept member does not pass a gate that would refuse it if it were declared.

## 7. Does the plain rule hold?

**Yes.** None of the four answers forces the narrower rule ("keep only when the reader is behind the
artifact's declared version").

The narrower rule is not workable as the format stands:

- An added optional member takes no profile step (§15.4, Phase 1670). The declared profile never moves
  for one, and negotiation answers `Current`. So a reader is never behind for the commonest change, and
  the narrower rule reduces to dropping.
- To make it work, the minor version would step for every optional member, which reverses Phase 1670.

Keeping does the opposite. It makes §15.4's optional-field row true, where today four hosts falsify it,
so Phase 1670's exemption rests on a fact instead of a claim.

Two places look like the narrower rule and are not:

- **The refusal under a policy (question four)** is a host narrowing by declaration (§23), scoped to
  discriminated values. It does not depend on version.
- **The null drop (question two)** concerns a value no conforming producer writes.

**What keeping costs.** An author's mistaken member is kept and re-encoded, where today it disappears.
Three things contain that cost:

- the advisory finding of question four;
- the near-miss sets, which keep refusing the costly mistakes;
- the closed authoring surfaces, which cannot produce one.

## 8. The ruling

**Ruled 2026-11-28: each of the four recommendations is accepted as written.** The ruling is recorded
here and as `docs/DECISIONS.md` D45. Phase 2880 is built from these answers and needs no further
decision.

| Question | Ruling | Date |
|---|---|---|
| 1. Carriage | Accepted as recommended. The object-local rule is normative. Python and Go keep the member in their structural map. The reference, TypeScript and Rust hosts keep one host-only slot per node, and the same per decoded op. No record or case changes shape. | 2026-11-28 |
| 2. Canonical form | Accepted as recommended. A kept member takes its ordinal place among the declared keys. Its value is re-encoded canonically under rule 12. A member whose value holds a null is dropped. | 2026-11-28 |
| 3. Identity | Accepted as recommended. A kept member counts in every hash, chain, digest, diff and equality. No op names one. A change confined to kept members lowers to `EditNode`, or to remove-then-insert for a member on the node envelope. | 2026-11-28 |
| 4. Gates | Accepted as recommended. The default decoder keeps and never refuses an undeclared member, and the enumerated refusals stay. Under any policy narrower than admit-all, a kept member whose value contains a `$type` object is refused with `KIND_NOT_ADMITTED` at the member, and the refusal is logged; plain values are kept. Validators report each kept member as a warning, never an error, under one new warning code. No edit writes or names a kept member. | 2026-11-28 |

The ruling also confirms the fact in section 7: an added optional member takes no profile step (§15.4),
so the narrower rule is not available.
