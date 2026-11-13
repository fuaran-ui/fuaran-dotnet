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

Hand-written decoder size: `JsonDecode.fs` was 10,841 lines before and is 10,870 after (252 lines
of hand structure deleted, 281 added). The addition is the bridge and the policy record (about 230
lines with their documentation), which land once, with the first family; every later family only
deletes structure and adds the policy entries it needs.

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

## Phase 2659 — the Binding-free display kinds

**Package:** `Fuaran.UI.Ops` (private members of `JsonDecode` only). **Rides the 0.104.1 draft slot** with no
surface change. Every verdict is unchanged; the parity harness is identical.

Moved: `Fact`, `Markdown` (and `MarkdownCitation`), `Tree` (and `TreeItem`), `Custom` (and `ContentHash`). Each one's
hand structure is deleted in the same commit. The rules they carry beyond their types are HOLDER-level overrides on
the policy record (2639 finding 4, ruled A):

| Holder | Rule | How the record carries it |
|---|---|---|
| `FactSpec` | `emphasis` reads the `Emphasis` aliases at a `bool` slot | the holder hands its walk `decodeEmphasisFlag` at `Bool` (its only `bool`) |
| `MarkdownCitation` | `name` is a `cite:` token | a check on the decoded citation |
| `MarkdownSpec` | `citations`, when present, is non-empty with unique names, and is answered before `text` | an empty list is refused before the walk; a citation defect drops the later members' defects; uniqueness is checked on the decoded list |
| `TreeItem` | row nesting is bounded by `MaxDepth` on its own axis | the holder threads the depth through the record it hands each row's children |
| `TreeSpec` | `onSelect` is the closure sentinel, read by presence | the holder reads the member's presence and hands the walk the rest |
| `CustomSpec` | `props` is verbatim JSON, which no record field reaches | the holder reads it with `decodeJValMap` and hands the walk an empty object; `exposedNodeIds: []` reads as absent |
| `ContentHash`, `HashStrictness` | the hand sentences | `Absent`'s table and the enumeration reader |

The bridge gains one helper, `Bridge.record`: a member `null` rides the walk as an object sentinel, so a record
position refuses it before the generated walk reads it as one.

Hand-written decoder size: `JsonDecode.fs` was 10,885 lines before and is 10,758 after (333 lines deleted, 206
added). The 2639 bridge's 29-line net growth is now accounted: the decoder is 83 lines below the
10,841 lines it held before the bridge landed, though phases between the two landings added 15.

### What is still not moved, and why

Every remaining kind spec, and the node envelope itself, reaches a `Binding`: the node envelope through
`Accessibility`, every container through `Node`, `Action`, `FormFieldKind` and every control directly, `Drawing`
through `DrawStyle` (and so `Shape`), `FragmentRef` and `Mount` through `FragmentArg`'s `Node`. They move once the
`Binding` seam carries a per-instantiation witness (fuaran-core#635, ruled A on 2026-11-11). That change is in the
untagged `Fuaran.Core` 0.43.0 draft, and this repository restores Core from nuget.org, so it waits on that release
and the pin raise.

