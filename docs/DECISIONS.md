# Fuaran UI — language decisions (newest first)

Decisions about the **language contract** — the wire model, its vocabulary, and the rules a
conformant host must obey. Narrower than the roadmap (which records *work*) and wider than
`VOCABULARY.md` (which governs only whether a `NodeKind` may be admitted).

A decision earns a place here when it constrains every host and would otherwise have to be
re-derived from the code. Each entry states what was decided, what was rejected, and the
consequence.

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
