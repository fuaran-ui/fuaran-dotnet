# Phase 2639 — the node decoder becomes a policy over the generated collecting decoders

**Packages:** `Fuaran.UI` (`Fuaran.UI.Generated`, new public members) and `Fuaran.UI.Ops`
(`Fuaran.UI.Ops.JsonDecode`, no surface change). **Rides the 0.103.1 draft slot** (additive).

## What a consumer sees

Nothing, by construction. Every verdict `JsonDecode` gives is unchanged: the accepted tree and its
canonical bytes, every refusal's code, path, sentence and expected shape, the whole §29 defect list,
the reliance events, the work meter and every admission verdict. The parity harness (below) holds
that over the whole node-decoder corpus, the op corpus, the malformed recovery sets and 9,255
mutations of the node fixtures, recorded from the hand decoder before anything moved.

What is new is in `Fuaran.UI.Generated`, emitted from the vocabulary now that it requests Core's
`SpecDecoders` derivation:

| Member | What it answers |
|---|---|
| `CollectingDecoders`, `collectingDecoders` | Core's record of overridable decoders (Core D175, D230): a field per node, union, record, kind spec, enumeration and JSON scalar, the `Binding` seam and `Absent`; the value overrides nothing |
| `decode<Tag>Spec` | one kind spec's object, the FIRST defect |
| `decode<Tag>SpecAll` | the same object, EVERY defect, in declaration order |
| `decode<Tag>SpecAllWith` | the same, read through a host's `CollectingDecoders`, so a decoder the host puts at a type applies at every depth |
| `decodeNodeJson`, `decodeNodeJsonAll(With)`, `decodeNodeAll(With)` | the whole node, the same three ways |

They answer Core's `DecodeError` with the structural layer's own sentences. A consumer that wants
this tier's policy — its leniencies and its sentences — keeps calling `JsonDecode`; one that wants
the vocabulary's structure alone, or to supply its own policy, calls these.

No migration is needed. No existing member changed.

## What moved inside `JsonDecode`

`JsonDecode` keeps a private `CollectingDecoders` built with `{ collectingDecoders with … }`: at each
field a moved spec reaches, the reader the hand structure called there (the scalar readers with the
§3.6 static unwrap, the enumeration readers with their aliases, `TextSource`), plus `Absent`, which
answers a required member's absence in the sentence the hand structure used and reads a member's
lenient alias, and the one relational check a moved spec carries (`Skeleton.rows`' §21.9 bound).
`Bridge` carries a reader's refusal through the generated walk and turns it back, at the call site's
path, into this decoder's `DecodeError`; a member `null` rides the walk as a sentinel so the reader at
its position answers it, as the hand structure did (Core D230.3).

Moved: `Heading`, `Math`, `Icon`, `CodeBlock`, `Badge`, `Callout`, `List`, `Skeleton`. Each one's hand
structure was deleted in the commit that moved it.

Hand-written decoder size: 10,841 lines before; see the phase outcome for the figure after.

## What is not moved yet, and why

- **Every spec with a `Binding` member** (`Metric`, `Sparkline`, `Progress`, `Toast`,
  `LabelValueRow`, every control). The record's `Binding` field is ONE seam generic in the payload
  type (Core D230.2), but this decoder's `Binding` reading needs two values per instantiation (the
  `Selection` arm's row-field projection and the slot's placeholder), and the Fable leg erases a
  generic method's type argument, so the seam cannot tell `Binding<float>` from `Binding<string>`
  there. Either Core's seam carries the instantiation's witness, or the policy names each `Binding`
  member at its holder.
- **A member whose policy differs from its type's** (`Fact.emphasis` and `LabelValueRow.emphasis`,
  read through the `Emphasis` aliases at a `bool` slot; `Markdown.citations`). The record overrides
  per TYPE; these need a per-member reader at their holder.
- **The node envelope, the kind dispatch, the containers and the unions** (`Box` and every kind
  holding nodes, `Action`, `Shape`, `FormFieldKind`, …): the depth bound, the kind admission policy,
  the near-miss member sets and the discriminator aliases are read at those positions, and each is
  its own landing.
