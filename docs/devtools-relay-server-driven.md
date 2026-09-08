# DevTools relay over the server-driven tier — design note

_Phase 741 re-scope. Written against the substrate as shipped 2026-07-29._

> **Update 2026-09-02 — B1 is closed, and it did not land here.** The bounded tier now has a channel
> connection, so the "there is no page to run against" verdict below no longer holds. Read the
> [B1 closed](#b1-closed--where-it-landed-and-why-not-here) section at the foot before acting on the
> blocker table: the row's *destination repo* was wrong, for a reason that also constrains B2.
> Everything else in this note stands as written — the two architectural findings (a server-driven
> page holds no tree; relay `apply` is coherent on the bounded driver only) were findings about the
> tier, not about missing work.
>
> **Update 2026-09-08 — B3 is CLOSED, and B3's own framing below is superseded.** The relay contract
> answers the treeless peer normatively at `relay@1.4`: `hello.ok` carries an optional `treeSource`
> (`"page"` | `"upstream"`, absent meaning `"page"`), §6.5 states what `"upstream"` obliges, §8.1
> says where the three gates run for such a peer, and §9.3 adds `UPSTREAM_UNAVAILABLE`. **No request
> type, no capability token and no payload shape changed**, which is the part the B3 row below did
> not anticipate and which decides several of its questions by dissolving them — see
> [B3 closed](#b3-closed--what-the-contract-actually-says) at the foot.
>
> **B2 remains open**, and the page side no longer waits on it. A treeless peer declares itself,
> advertises `read.renderedDom` — the one read that asks the DOM rather than the tree — and is
> rendered correctly by a client; the proxied reads arrive later as a capability set that GROWS,
> which is exactly what §5.3 and §6.3 are built to absorb.

The [DevTools relay contract](../../wire-format-fixtures/DEVTOOLS_RELAY.md) (`relay@1.0`) lets a
browser extension read and edit a Fuaran page in place. It was specified (Phase 734) and implemented
(Phase 735, TS renderer; Phase 739, the Fable leg) against the **client tiers**, where the typed tree
lives in the page. This note answers the three questions Phase 741 asks about extending it to the
**server-driven tier** (`Fuaran.UI.ServerDriven`, Phases 152/153), and records what blocks each.

**Summary verdict: not implementable today, and the binding constraint is not the relay.** Relay
`apply` maps cleanly onto exactly one of the two server-driven driver modes — the bounded driver —
and that mode has no channel connection, so a bounded page cannot be served over a live transport at
all. Two of the four blockers below are architectural findings rather than missing work.

---

## The shape of the problem

A server-driven page is not a thin variant of a client page. It is a page with **no tree in it**.

The browser half is `src/Fuaran.UI.ServerDriven/content/fuaran-live-patch.js`, whose entire public
surface is:

```js
global.FuaranLive = { start, sseAdapter, applyPatches, applyPatch, performEffects };
```

It receives `DomPatch`es addressed by `data-fuaran-node-id` and applies them to the DOM. There is no
`window.__fuaran`, no typed tree, no binding resolver, no node introspection — by design ("**app-agnostic
and Fuaran-agnostic**", the file header). The relay's page peer wraps a host's in-page surface
(`DEVTOOLS_RELAY.md` §2: "the host's **in-page** introspection object the page peer relays"); on a
server-driven page there is nothing to wrap.

That marker attribute is nonetheless present, so the extension's `hasFuaranMarkup` heuristic
(`fuaran-devtools/src/inspect/detect.ts`) fires on a server-driven page — correctly, since §6.1 makes
the marker a hint about *where to look* and explicitly not a detection signal. `hello` then goes
unanswered, which is the right behaviour for a page with no peer (§11.1, silence when not opted in),
but it means a server-driven page today reads to the panel as "not a Fuaran page".

---

## Q1 — How does relay `apply` map onto the channel's op path?

**There is no op path.** The channel's inbound envelope is a closed *interaction* type
(`Validation.fs`):

```fsharp
type LiveValue = Str of string | Num of float | Bool of bool | Null

type LiveEvent =
    { ConnId: string; NodeId: string; Event: string
      Payload: Map<string, LiveValue>; LastSeq: int }
```

`LiveValue` is deliberately "the closure-free, portable subset the shim sends". A `TreeOp` cannot ride
it, and should not be made to: the envelope's meaning is *"the user interacted with node X"*, which the
driver resolves to an `Action` and runs through the update loop. An extension-authored `TreeOp` is a
categorically different inbound — it mutates the tree directly, bypassing that loop. Carrying it needs
a **second inbound message kind**, parsed alongside `tryParseLiveEvent` (`Inbound.fs`) and routed by
the connection.

### The mapping is coherent on one driver mode and incoherent on the other

This is the load-bearing finding, and it is a property of the tier, not a gap in it.

**Model-backed driver (Phase 152) — incoherent.** `LiveSession` holds `{ Model; Tree; Update; View }`,
and every step recomputes the tree from the model (`Driver.fs`, `applyResolvedActions`):

```fsharp
let newModel = msgs |> List.fold (fun m msg -> session.Update msg m) session.Model
let newTree = session.View newModel
let ops = TreeOpDiff.diff session.Tree newTree
```

The tree is a pure projection of the model. A `TreeOp` applied directly to `session.Tree` is not merely
lost on the next event — it is **actively reverted**: the next step diffs the edited tree against
`View newModel`, and the difference between them emits patches that undo the extension's edit in the
browser. An extension edit here would apply, appear to work, then visibly snap back on the user's next
click. There is no correct way to route relay `apply` into this mode, and none should be invented; an
edit that must survive has to be expressed as a model change, which a `TreeOp` cannot express.

**Bounded driver — coherent, and the round-trip falls out for free.** _(From 0.25.0 the bounded
driver ships in `Fuaran.Program.Bounded`, not this repo; the type shapes cited below are unchanged.)_
The bounded mode has
no `Model`/`View`/`Update`. Its state is the tree itself plus a store, and the tree is the fixed
structural input (`BoundedDriver.fs`):

```fsharp
type BoundedSession = { BaseTree: Node<obj>; Store: BoundedStore; Resolved: Node<obj>; ... }

let newResolved = resolveTree outcome.Store session.BaseTree
let ops = TreeOpDiff.diff session.Resolved newResolved
let patches = Lowering.lower session.Services.RenderFragment newResolved ops
```

Apply the extension's `TreeOp` to `BaseTree`, re-resolve, and the existing diff → lower → push path
carries the change to the browser with no new machinery. **The reflection half of Phase 741's acceptance
criterion is satisfied by construction on this mode** — which is a strong signal that the bounded driver
is the right and only target.

That is also the mode the phase's own framing wants: the bounded driver exists to drive *AI-emitted*
trees, which is precisely the "UIs produced by headless hosts" case Phase 741 cites as its motivation.

### Blocker B1 — the bounded tier has no channel connection

The mode where relay apply works cannot be served to a browser. `LiveConnection<'Model,'Msg>`
(`Channel.fs`) takes a `LiveSession<'Model,'Msg>`, and both transports bind that type —
`mapFuaranLive` takes `LiveAppConfig<'Model,'Msg>`, `mapFuaranLiveWebSocket` takes
`LiveWsConfig<'Model,'Msg>`. The bounded analogue does not exist in code: `BoundedConnection` appears
exactly once in the repository, as a sentence in `SERVER_DRIVEN.md` describing it as a follow-on
("`step` is transport-shaped already, so it is glue, not new design").

So the end-to-end demo Phase 741 asks for has no page to run against. **This blocks the phase outright**,
and it is prior to every relay concern: a bounded server-driven page cannot currently be served over SSE
or WebSocket at all, with or without an extension.

---

## Q2 — Where does authorization sit?

**Server-side, at the driver, before the op reaches `BaseTree`** — and this is a stronger posture than
the client tier's, not a weaker one.

On the client tiers the gate must run *in the page*, because the page is where apply happens; §11.3
("the relay has no side door") permits it nowhere else, and Phase 740's migration note wires
`RelayApplyGate` into the `ActionDescriptor.ApplyTreeOp` branch of the host's `IFuaranRuntime`. On the
server-driven tier the page holds nothing and is fully untrusted, so the gate moves to the server, where
the tree actually lives. `RelayApplyGate` is already the right shape for that move:

- It is typed over `opJson: string` and takes **no `Fuaran.UI.Renderer` dependency**, so it composes at
  a driver just as well as at a renderer.
- Its default is `RelayApplyGrant.NotGranted` — extension apply refused unless a host explicitly grants
  it, which is the correct default for a server-held session serving many connections.
- It records `DenyTelemetry` under the reserved principal `fuaran.relay.apply`, so refusals are
  attributable without trusting the attribution.

**What does not compose is the driver's existing gate.** `BoundedServices.CanDispatch: Action<obj> -> bool`
is typed over `Action` — the bounded interaction vocabulary — and cannot express "an extension-attributed
`TreeOp`". Phase 741's task 3 assumes the driver can refuse such an op "through its existing gate"; it
cannot, because no existing gate has a shape that admits one. This needs:

- a gate entry typed over the op (i.e. `RelayApplyGate.Decide` called by the connection, not by
  `CanDispatch`), and
- a new reject case, since `BoundedReject` is `Gate of RejectReason | BudgetExceeded of string` and
  `RejectReason`'s four cases are all interaction-shaped (`UnknownNode` / `IllegitimateEvent` /
  `PayloadOutOfBounds` / `DispatchDenied of nodeId * action`). A relay refusal is none of these.

**Blocker B2 — typed refusals have no ride back to the panel.** The channel is push-frames outbound
(`Frame = { Seq; Patches; Effects }`) and fire-and-forget inbound; the browser shim posts an event and
receives frames, with no correlation id and no response envelope. `Frame` carries patches and effects
only, so there is no slot for a refusal, and nothing downstream to route one to. Phase 741's second
acceptance criterion — "a refusal at the driver reaches the panel as the typed deny envelope" — therefore
needs a correlated response leg on a channel that has never had one. Note the relay's own refusal
(§9) is a *response* correlated by request `id` (§4.1), so satisfying this honestly means adding
request/response semantics, not smuggling a refusal into a patch frame.

Attribution stays advisory and untrusted throughout (§8.2). Its correct destination is the Phase 211
attributed-durability journal, which already records a real principal per connection — so an
extension-originated op would be journalled as the connection's authenticated user with the relay's
`actor` recorded as untrusted text beside it, never in place of it.

---

## Q3 — What does the `hello` advertise for a server-driven page?

Today: nothing, because there is no peer to answer it (above).

What one *should* advertise is genuinely undecided, and it is a **specification question rather than a
coding one**. Of the five read capabilities, exactly one is servable from a server-driven client:

| Capability | Servable in-page? | Why |
|---|---|---|
| `read.renderedDom` | **Yes** | The DOM is right there; this is the one capability the tier can answer honestly. |
| `read.tree` | No | The tree is on the server. |
| `read.nodeState` | No | Needs the typed node (kind + binding slots + child ids). |
| `read.bindingValue` | No | Needs the bindings and their live sources. |
| `read.findNodes` | No | Needs kinds, which only the tree carries. |

Two shapes are available and neither is free:

**(a) A minimal honest peer** advertising `read.renderedDom` alone (plus `apply`/`subscribe` once B1
lands). §6.4 makes this *fully conformant* — "a read-only host is fully conformant", and a peer must
refuse unadvertised capabilities with `CAPABILITY_ABSENT`. But an inspector that can only read DOM is
close to useless: the panel's whole value is showing the typed tree behind the markup, and this shape
shows none of it.

**(b) A proxy peer** that forwards reads over the channel to the server-held tree. This is what the
panel actually wants, and it is what "the session tree lives server-side" implies. It needs the same
request/response leg B2 needs, plus a read-op vocabulary on the channel — and it changes what a page
peer *is*: `relay@1.0` §2 defines the surface as the host's **in-page** introspection object, and a
proxy peer is not that. A conformant client would also need to reason about read latency and staleness
that the spec currently assumes away (reads are synchronous against a local surface).

**Blocker B3 — `relay@1.0` does not describe a server-held-tree peer.** Whichever shape is chosen, the
spec has to say so: capability semantics for a treeless page, whether reads may be asynchronous, and
what `treeRevision` (§5.4) means when the revision is authoritative on the server and the page learns it
only via frames. Under §5.3 that is a minor version bump — `relay@1.1` — and it belongs in the
specification repo (`fuaran-ui/fuaran-ui-specification`), where the shared conformance corpus five hosts
certify against lives. It is not a change a code phase should make unilaterally.

Note one consequence for `subscribe`, which is the cheapest part of the whole picture: the frame stream
**is** the change signal, and `Frame.Seq` is a natural `treeRevision`. Once a peer exists, `subscribe`
and the `changed` event are close to free, and `cause` would honestly be `"host"` for a server push and
`"apply"` for the extension's own op — exactly the distinction §8.5 asks for.

---

## What this means for Phase 741

The phase was accepted as a deliberately thin placeholder, to be re-scoped once the extension existed.
Re-scoped, it is **blocked on substrate that is prior to it**, and the two owner repos it was filed
against are both wrong:

- **Not `fuaran-ts`.** A server-driven page never loads `@fuaran-ui/renderer`; there is no
  server-driven code anywhere in that repo. The relay work for this tier is in the shim, which is
  `fuaran-dotnet`.
- **Not the private runtime tier, except for composition.** The driver, channel, shim and transports
  are all `fuaran-dotnet/src/Fuaran.UI.ServerDriven*`. The runtime tier holds `RelayApplyGate` (already
  shipped, Phase 740) and a 53-line host channel adapter; the host wires the gate, it does not
  implement this.

The blocking order is B1 → (B2, B3) → the phase:

| | Blocker | Where it lands |
|---|---|---|
| **B1** | `BoundedConnection` — the bounded tier has no channel connection, so a bounded page cannot be served. Prior to everything else. | `fuaran-dotnet` |
| **B2** | No correlated response leg on the channel — typed refusals (and proxied reads) have nowhere to ride. | `fuaran-dotnet` |
| **B3** | ~~`relay@1.0` does not describe a treeless / server-held-tree peer; needs a minor bump and corpus cases.~~ **CLOSED at `relay@1.4`** — see [B3 closed](#b3-closed--what-the-contract-actually-says). | specification repo |

Only once those land does Phase 741's own work — a page peer in the shim, an inbound op kind, the
`RelayApplyGate` call at the bounded connection, and the demo — become a phase-sized piece of work.
Attempting it before B1 would mean building a relay leg for a page that cannot be served.

---

## B1 closed — where it landed, and why not here

_2026-09-02._

`BoundedConnection` exists. It is **`Fuaran.Program.Bounded.BoundedConnection`**, not a type in this
repository, and the reason is worth recording because the same constraint governs what is left.

**The blocker table above says B1 lands in `fuaran-dotnet`. That was true when the table was written
and cannot be made true now.** Phase 756 moved the bounded driver into the `Fuaran.Program.Bounded`
package, and that package consumes `Fuaran.UI.ServerDriven` as a *published dependency*, one way, by
design. A connection needs the bounded session type; putting one in `Channel.fs` would mean this
repository referencing back into a package that references it, which is the one direction that tier
does not have. So the connection sits beside the driver it sequences, and this repository's
contribution to B1 is the seam it already shipped: `IFuaranLiveChannel`, `Frame`, `InMemoryChannel`
and the replay-buffer default were consumed **unchanged**, at the version that consumer already
pinned. B1 needed no change here at all.

What that closes, against the note's own predictions:

- **The bounded mode can now be served.** Any backend implementing the transport seam serves a
  bounded page, with no bounded code of its own — which is what makes the rest of this note about a
  page that exists rather than one that does not.
- **The reflection half is demonstrated, not merely predicted.** The note argued that on the bounded
  driver the round trip "falls out for free" from `resolveTree` → `TreeOpDiff.diff` →
  `Lowering.lower`. It does, and the connection's tests pin the sharper claim underneath it: an edit
  applied to `BaseTree` is *structural*, so a later interaction re-resolves against the edited tree
  rather than reverting it — the exact property the model-backed driver cannot have.

Two design predictions in Q2 turned out to want amending, and the amendments are recorded in that
package's `DECISIONS.md` **D13** rather than restated here:

- The note expected "**a new reject case**" on the bounded reject DU. That DU documents itself as the
  event-level refusal and nothing else, and it is closed and matched exhaustively by hosts — so
  widening it would be a breaking change made to model something the type says it does not model. The
  out-of-band refusal is an additive type of its own.
- The note expected the gate to be "**`RelayApplyGate.Decide` called by the connection**". The
  *shape* is right and the reasoning held — the driver's dispatch gate is typed over the interaction
  action and cannot express an attributed op, exactly as predicted. What could not follow is the
  composition: the runtime tier holding that gate is not on the bounded package's restore graph. So
  the connection exposes a grant-policy seam, default-closed, and a host that has such a gate
  installs it there. One gate, composed at the host, rather than a dependency the tier cannot take.

### What this changes about B2 — a second cost the note did not have

B2 is unchanged as a *finding*: the channel is push-frames outbound and fire-and-forget inbound, so
a refusal has nowhere to ride, and the connection therefore **returns** its refusal to the caller
rather than pushing it. For an in-process host that is complete. For a remote submitter it is not,
and B2 is still what closes the gap.

The new cost is ordering. The response leg is a change to `Frame` and the shim — this repository —
but the bounded connection consumes this repository as a **published package on a public restore
path**, with a pin check that refuses a version only a local feed can serve. So B2 is not one change
followed by a consuming change: it is a change here, a version cut, a publish, and only then a
consumer able to see it. Worth knowing before scheduling it, and it is why B1 was worth doing first
in the one form that needed nothing from this side.

B3 is untouched and remains the specification repository's, on the reasoning already given.

---

## B3 closed — what the contract actually says

_Added 2026-09-08 (Phase 1598), against `DEVTOOLS_RELAY.md` at `relay@1.4`._

The B3 row above asked for "capability semantics for a treeless page, whether reads may be
asynchronous, and what `treeRevision` means when the revision is authoritative on the server". The
contract answered the first and **dissolved the other two**, which is worth reading rather than
skimming, because both were framed as design questions and neither turned out to be one.

**The peer declares where its tree is, and nothing else changes.** `hello.ok` gains one optional
field, `treeSource`, with values `"page"` and `"upstream"`; absent means `"page"`, on the §8.2.1
`actorClass` precedent, so a peer whose tree IS in the page omits it and its handshake stays
byte-identical to one a pre-1.4 peer would have sent. §6.5 states what `"upstream"` obliges: the
declaration gates nothing (`capabilities` is still the whole authorisation surface), every advertised
tree read is **proxied and never reconstructed** from the patches the page applied (§7.7 rule 1's
"no second projection" — this is the rule that makes §6.5 normative rather than advisory), the fact
is stable for the session, and it is emitted whatever profile the session settled on.

**Capability semantics for a treeless page: unchanged, and that is the answer.** §6.4 already made a
narrow peer fully conformant. An upstream-tree peer advertises what it can serve; today that is
`read.renderedDom` (§7.4) alone, because it is the one read that asks the DOM a geometry question
rather than asking the tree. Everything else is `CAPABILITY_ABSENT` — the entry point exists, this
peer does not offer it — which is §10.1's distinction doing an unchanged job.

**"May reads be asynchronous" was never a contract question.** Every request already has a
correlation id and a response; the contract never promised a latency. What was missing was a way to
say the request COULD NOT BE SENT, and §9.3's `UPSTREAM_UNAVAILABLE` is that — restricted to the case
the peer can assert, namely that the request never left. A request that WAS dispatched and went
unanswered gets no response at all and the client's own timeout governs, because §8.3's "a refused op
MUST leave the tree unchanged" is a promise a peer in that position cannot make.

**`treeRevision` needed no new meaning.** §5.4 already makes it opaque — compared, never parsed — so
the upstream tree's revision is a revision like any other, and a `changed` event from a server push
carries `cause: "host"` (a push is the host changing its own tree) with no new `cause` value.

### What this repository owes, and what it does not

The page side does **not** need a peer in this shim to work: a client that injects its own page peer
reads `window.FuaranLive` — this tier's existing public surface — and speaks the contract over it. So
what this tier owes is the two FACTS such a peer needs, published deliberately rather than inferred,
and `content/fuaran-live-patch.js` now publishes both on `FuaranLive`:

- `treeSource: "upstream"` — the declaration itself, so a peer does not have to key off this global
  merely existing;
- `isConnected()` — whether the stream is up right now. This is what makes `UPSTREAM_UNAVAILABLE`
  raisable here with **no correlated response leg at all**, and it is the one part of §6.5 that does
  not wait on B2: "no channel is established" is a fact held locally. Before it, a peer had to read
  the QW2 `data-fuaran-disconnected` attribute on `<html>` — a reconnecting-banner styling hook, and
  a poor thing to make load-bearing for a protocol decision.

Deliberately NOT exposed: anything tree-shaped. §6.5 rule 2 forbids answering a tree read from a
reconstruction of the patches this shim has applied, and there is nothing else this shim could offer
that would not be that.

**What remains for a host-registered peer of this tier's own** — one that exists without a client
injecting one, and that advertises the proxied reads — is B2-shaped and stays 741's: it needs the
correlated response leg, and per the B1 section above that is a change here, a version cut, a
publish, and only then a consumer able to see it.

---

## See also

- [`SERVER_DRIVEN.md`](SERVER_DRIVEN.md) — the tier itself: the loop, the three client tiers, G1, the
  bounded driver.
- [`../../wire-format-fixtures/DEVTOOLS_RELAY.md`](../../wire-format-fixtures/DEVTOOLS_RELAY.md) —
  the relay contract (§6.4 capabilities, §8 apply, §9 refusals, §11 security).
- `RelayApplyGate` — the grant model, and where it hooks on the client tiers — is documented in the
  Phase 740 migration note shipped with the runtime tier that owns it.
- [`in-page-introspection-repl.md`](in-page-introspection-repl.md) — the `window.__fuaran` surface the
  relay wraps on the client tiers, and which the server-driven tier does not have.
