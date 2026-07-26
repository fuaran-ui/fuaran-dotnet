# Fuaran UI — language decisions (newest first)

Decisions about the **language contract** — the wire model, its vocabulary, and the rules a
conformant host must obey. Narrower than the roadmap (which records *work*) and wider than
`VOCABULARY.md` (which governs only whether a `NodeKind` may be admitted).

A decision earns a place here when it constrains every host and would otherwise have to be
re-derived from the code. Each entry states what was decided, what was rejected, and the
consequence.

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
