# Proposal — narrow `Link.target` and `Link.rel` on the wire (§4b)

**Status:** PROPOSED. Nothing in this document is implemented by any decoder.
**Scope:** `WIRE_FORMAT.md` §4b, the `Link` spec's `target` and `rel` members.
**Related:** the EMISSION rule for the same two slots is shipped and is described in
[`SANITIZATION.md`](../../SANITIZATION.md); its implementation is
[`src/Fuaran.UI/EmissionGrammar.fs`](../../src/Fuaran.UI/EmissionGrammar.fs).

## Why this is a proposal rather than a change

`Link.target` and `Link.rel` are free strings on the wire. Every conformant host accepts any string
in either slot today, and documents in the wild carry values this proposal would refuse. Narrowing a
decoder is therefore not a hardening step that can ride an ordinary phase: it makes documents that
decode today stop decoding, on every host at once, and there is no way to un-refuse a document
someone has already been told is invalid.

So the two halves are separated deliberately, and this document is the record of that separation:

- **The EMISSION rule shipped.** Every renderer — the F# client and server tiers, the TypeScript
  client and server tiers, and the Python, Go and Rust hosts — now emits only what the closed sets
  below admit, and forces `noopener noreferrer` on a `_blank` link. A document carrying `rel="opener"`
  still decodes, still validates, still persists and still introspects exactly as written; what
  changed is that no renderer puts it in a document.
- **The WIRE narrowing is proposed.** It needs an operator decision, a §4b amendment, schema changes,
  corpus reject vectors, and a coordinated adoption across seven hosts in one change-set — which is
  the forward-coupling rule the specification already states. This document is the input to that
  decision, not a substitute for it.

**A pre-emit advisory carries the gap in the meantime.** `PreEmitValidate` reports FUARAN146 for any
`target` or `rel` token outside the closed sets, at Warning severity, so an emitter is told before it
ships a document whose tokens will not survive rendering, and the demand loop counts the refusal.
That is what makes the interval between this proposal and its adoption observable rather than silent.

## The proposed narrowing

### `target` — a closed set of two

```
target := "_self" | "_blank"
```

Three values the HTML specification also defines are refused, each for its own reason:

- **`_parent` / `_top`** are meaningful only when the document is FRAMED. A framed Fuaran document
  navigating its embedder is precisely the frame-busting the embedding host did not consent to — and
  an embedding host has no way to inspect a decoded tree for the value before it renders.
- **A NAMED frame** (`target="victim"`) addresses a browsing context BY NAME. A decoded tree can
  therefore navigate a window it did not create and whose contents it cannot see, and the name is a
  free string with no way for a host to enumerate what it might hit.

An absent `target` stays absent and means the same thing it means today.

### `rel` — a closed token set

```
rel := token *( WSP token )
token := "alternate" | "author" | "bookmark" | "external" | "help" | "license"
       | "next" | "nofollow" | "noopener" | "noreferrer" | "prev"
       | "privacy-policy" | "search" | "tag" | "terms-of-service" | "ugc"
```

Every member describes THIS link's relationship to its destination and changes nothing about the
opener's capabilities in the wrong direction. Tokens are case-insensitive and whitespace-separated,
as in HTML.

The one deliberate absence is the finding. **`opener` re-enables `window.opener` on a `_blank`
link**, handing the opened document a live reference to the opening one — the capability `noopener`
exists to remove, and one no rendered tree has any reason to ask for. Modern browsers imply `noopener`
on `target="_blank"`, which is exactly why an explicit `opener` is dangerous rather than merely
untidy: the safe behaviour is a user-agent DEFAULT, an explicit `rel="opener"` overrides it, and the
version floor at which the default arrived is not something a document can know about its reader.

### The forced pair

A `_blank` link is emitted with `noopener noreferrer` whether or not the document asked, and whether
or not it declared a `rel` at all. This is an ADDITION the renderer makes, not a refusal of anything
the document wrote, and it is what makes the property a fact about the DOCUMENT rather than a fact
about the user agent.

The emitted token order is fixed — surviving declared tokens first, in their declared order, then the
forced pair if absent — so two hosts given one document emit one byte sequence. An unordered set
would make cross-host byte parity impossible to state, let alone to test.

## Proposed corpus reject vectors

These are written out here rather than added to `wire-format-fixtures/reject/`, because a reject
vector is a claim that every conformant host REFUSES the document — and today every conformant host
accepts all three. Adding them before the decoders narrow would fail every host in the estate on the
day they landed, which is the opposite of what a conformance corpus is for. They move into the corpus
in the same change-set as the decoder narrowing, per the §11 forward-coupling rule.

| Proposed fixture | Document | Expected refusal |
|---|---|---|
| `reject/link-rel-opener` | `{"id":"l","kind":{"$type":"Link","download":false,"href":{"$type":"Static","value":"/x"},"label":"L","rel":"opener","target":"_blank"}}` | `WRONG_TYPE` on `rel` — the token is outside the closed set |
| `reject/link-target-named-frame` | `{"id":"l","kind":{"$type":"Link","download":false,"href":{"$type":"Static","value":"/x"},"label":"L","target":"victim"}}` | `WRONG_TYPE` on `target` — a named browsing context |
| `reject/link-target-frame-keyword` | `{"id":"l","kind":{"$type":"Link","download":false,"href":{"$type":"Static","value":"/x"},"label":"L","target":"_top"}}` | `WRONG_TYPE` on `target` — a framing keyword |

The error code is proposed as `WRONG_TYPE` rather than a new one, on the ground that a closed token
set is a type: the same code a `FormFieldKind` outside the vocabulary already raises.

## What adoption would cost

Stated because the cost is the argument against doing it silently:

1. `WIRE_FORMAT.md` §4b gains the two grammars, and §11's forward-coupling list gains the slots.
2. `schemas/` narrows both members to enumerations.
3. The three reject vectors above land in the shared corpus, with `manifest.json` updated.
4. Seven host codecs narrow together: `Fuaran.UI.Ops` (F#), `@fuaran-ui/ops` (TS), `fuaran-py`,
   `fuaran-go`, `fuaran-rs`, and the two native surfaces' render-projection decoders.
5. Every authoring veneer that can express the slot gains the narrowed type — the C# fluent factory,
   the VB XML mapping and its analyzer vocabulary — per §11 step 6.
6. `STABILITY.md` records it as BREAKING to `Fuaran.UI.Ops`: documents that decoded stop decoding.

## The open question this proposal does not answer

Whether a host with a legitimate need for `_parent` (a deliberately framed embed that navigates its
own container by arrangement with the embedder) should have a declared opt-in, or whether such a host
should compose the navigation as an `Action` the host runtime interprets. The emission rule takes no
position: it refuses the token and the host's own runtime remains free to navigate however it likes.
A wire narrowing has to answer it, because after narrowing the document cannot express the intent at
all.
