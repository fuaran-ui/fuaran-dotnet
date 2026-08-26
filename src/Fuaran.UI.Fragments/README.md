# Fuaran.UI.Fragments

The certified fragment library: a curated set of parameterised `FragmentDecl`s for the composite
shapes consumer apps keep re-deriving.

Each fragment declares **typed holes** over bounded value-spaces and a **two-axis effect class**, and
each is **certified valid-for-all-bindings** before it ships — not sampled once and eyeballed. The
library depends on `FSharp.Core` and `Fuaran.UI` and nothing else, so it is Fable-clean and carries
no renderer, ops or host dependency.

## The charter: fragments are application-space composition

**This library is not a second language vocabulary, and it never gates, substitutes for, or
pre-empts `NodeKind` admission.** That is the whole of its scope, and it is worth stating plainly
because the two things look adjacent from outside.

The closed `NodeKind` set is the language's **sole** vocabulary. A closed, statically known kind set
is the strongest form of the error-scope promise — emission can be grammar-constrained, and there is
one canonical form per pattern. Growing it is a governed act with its own admission charter, in
[`../../docs/VOCABULARY.md`](../../docs/VOCABULARY.md): demand evidence, irreducibility, cost, and a
pre/post confusion delta. Nothing in this package participates in that decision.

What fragments are instead is **composition in application space**: a consumer app's repeated
pattern, a content pack's shipped shapes, a pattern bank's currency. A fragment is a saved tree that
behaves as a *function* of its declared holes — lambda abstraction over the artefact substrate — so a
consumer that keeps hand-rolling the same subtree can name it once and apply it. That is a
consumer-side convenience with a verification floor under it. It is not a way to add a kind, and a
fragment that looks like it wants to be a kind is a signal to open the admission charter, not to
extend this library.

Two consequences follow, and both are enforced rather than hoped for:

- **A fragment composes existing kinds only.** Nothing here introduces a wire discriminator. Every
  fragment's declaration and its representative application round-trip through the canonical wire
  format as ordinary `FragmentDecl` / `FragmentRef` nodes, and both halves are pinned as fixtures in
  the shared conformance corpus — so every conformant host carries the library identically, with no
  host-side knowledge of any individual fragment.
- **The dependency runs one way.** The certification harness lives in `Fuaran.UI.Validator` and
  reaches *in* to this library from the test tier. This package never reaches out. If it did, the
  library would stop being Fable-clean and would start being infrastructure.

## What is in the set

| Fragment | Holes | Effect | What it stands for |
|---|---|---|---|
| `labelled-metric-row` | `label`, `value` | pure | A label and its figure on one line — the smallest read-only composite in the set. |
| `kpi-strip` | `heading`, `count` (repeat, 1–6) | pure | A headed horizontal strip of figures — the dashboard's top band. |
| `filter-bar` | `searchLabel`, `statusLabel` | reads host | A free-text chip and a status chip, each bound to its own host filter key. |
| `confirm-action-pair` | `confirmLabel`, `cancelLabel` | writes host | A primary confirm and a secondary cancel, both writing one declared state key. |
| `empty-state-panel` | `title`, `body`, `action` (slot, `Button`) | pure | A titled nothing-to-show card whose call to action is the caller's. |
| `section-header` | `eyebrow`, `title`, `level` (1–4) | pure | An eyebrow above a title, at a caller-chosen heading level. |
| `metric-card` | `title`, `value`, `caption` | pure | A card carrying one headline figure and the caption that qualifies it. |
| `loading-placeholder` | `rows` (repeat, 1–8) | pure | Skeleton rows — the shape a list takes while its query is in flight. |

Fragment names are **permanent**. Renaming one breaks every consumer's refs and every stored tree
that carries them, so a rename is a new fragment plus a deprecation, never an edit.

## The three parts of an entry

```fsharp
open Fuaran.UI.Fragments

let card = Stdlib.metricCard<unit>

card.Decl        // the declaration node — the TEMPLATE, and what travels on the wire
card.Materialize // the emitted tree for a full binding — what certification proves
card.Example     // one representative applied FragmentRef, binding every required hole
```

The separation between `Decl.Body` and `Materialize` is the load-bearing part, and it is not
redundancy.

`Decl.Body` is the template the renderer-side apply binds: value-hole sites read
`Binding.State(<holeName>, <default>)`, and slot-hole sites are unbound `FragmentRef` markers named
for the slot. `Fuaran.UI.Renderer.FragmentApply.apply` substitutes the bound subtrees, namespaces
their ids by the ref site so two refs cannot capture each other's, and returns the value bindings the
host seeds.

`Materialize` is the separate step the certification harness drives: given a full binding of the
value and repeat holes, the tree the fragment *yields*. It is a function rather than a derivation of
`Body` for one concrete reason — a `Repeat` hole has no in-tree expansion marker, because nothing in
the tier materialises a count. How a binding **shapes** the emission is knowledge only the fragment's
author has, so the author supplies it. Slot markers are left standing in the materialised tree:
certification varies the value and repeat holes only, and a slot's subtree belongs to the caller.

## What certification does and does not claim

Certification runs the shipped tree-shape validator over the emitted tree for **every** covered
binding of the fragment's value and repeat holes — exhaustively where the space is finite and small,
sampled deterministically where it is not, with the coverage carried on the verdict so a reader never
sees "certified" without knowing which. A failing fragment reports a readable `(binding, defect)`
counterexample; the same seed reproduces it.

Three invariants are stamped into the type surface rather than checked by convention:

1. **Totality** — a repeat count ranges over a bounded `IntRange`, so no binding can produce
   unbounded expansion.
2. **Hygiene** — application is capture-avoiding by lexical hole addressing, so two refs binding the
   same fragment with different arguments cannot collide.
3. **Effect signature** — the declared two-axis class (host effect × determinism source) is total and
   joins componentwise through composition.

And one honesty boundary, which is the reason the effect axis is declared at all: **a fragment whose
declared effect is not pure-deterministic is certified for STRUCTURE only.** `filter-bar` reads host
filter state and `confirm-action-pair` writes host state; their verdicts carry `StructureOnly = true`
and assert that the emitted tree is valid for every binding — never that the emission is a pure
function of its holes, and never anything about output quality. A consumer that reads a
structure-only verdict as a determinism guarantee has been told otherwise here.

## Adding a fragment

Four things, and the last is not optional:

1. Add the entry to `Stdlib.fs` with typed holes over **bounded** value-spaces and an honestly
   declared effect class. A hole whose space you cannot bound is a design problem, not a hole.
2. Add it to `Stdlib.all`.
3. Certify it: the library's suite drives every entry in `all`, so a new entry is certified by
   construction — but read the verdict's coverage line, because a sampled verdict over a large
   product of string spaces is a weaker claim than an exhaustive one.
4. Land its wire fixtures (declaration + representative application) in the shared conformance
   corpus in the same change-set. A fragment that is in the library but not in the corpus is one the
   other hosts do not carry, and the library's promise is that they all carry it identically.

One trap on step 4, worth knowing before you hit it. The wire format specifies a **canonical** form
for the declaration's optional fields: a zero-hole decl omits `holes`, and a **pure-deterministic
decl omits `effect`**. The F# type carries `Effect` as an option and encodes `Some x` verbatim, so
the redundant explicit default is expressible, decodes to the same meaning, and round-trips through
this host without a murmur. It is still wrong — a host that normalises to the specified form
re-encodes the default away and its byte-comparison against the corpus fails. The first cut of these
fixtures did exactly that: six of them broke a sibling host's conformance leg while every suite here
stayed green, because this host is the encoder that produced the bytes it was checking against.
`decl` builds the canonical shape and the suite pins it, so this particular shape cannot come back —
but the lesson generalises past the one field, and a green local gate is not evidence about the
other hosts.

Before any of that, ask whether the shape you are naming is genuinely a *composite of existing
kinds*. If the answer is that it wants to be a kind, this library is the wrong place and
[`../../docs/VOCABULARY.md`](../../docs/VOCABULARY.md) is the right one.
