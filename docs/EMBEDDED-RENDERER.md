# The embedded browser renderer — `Fuaran.UI.Renderer.Web`

A .NET app that wants a **live client-side render** of a Fuaran tree has had exactly one route:
compile the client from F# with Fable, through a Node toolchain. That excludes C# and VB
outright and asks every other host to adopt a build pipeline it may want nothing else from.

`Fuaran.UI.Renderer.Web` closes the gap. It embeds the built `@fuaran-ui/renderer` browser bundle
and the canonical reference stylesheet **inside the assembly**, serves them from one
`MapFuaranRenderer()` call, and emits the HTML snippet that hydrates a serialized tree. Author in
any .NET language; the browser renders it; no Node anywhere on the consumer side.

The package README carries the API. This page carries the two things that are easy to get wrong:
**which tier to choose**, and **how interaction actually reaches the host**.

---

## 1. Choosing a tier

Three ways to put a tree in front of a user. They are not a ladder — each answers a different
question, and the middle one is not a compromise between the other two.

| | **Server-driven** | **Embedded renderer** | **Full Fable** |
|---|---|---|---|
| Package | `Fuaran.UI.ServerDriven.*` | `Fuaran.UI.Renderer.Web` | `Fuaran.UI.Renderer` |
| Where the tree lives | server | server, hydrated in the browser | browser |
| Client toolchain | none | none | Node + Fable |
| What crosses the wire | rendered HTML, then patches | the tree as wire JSON, once | nothing — the tree never serialises |
| Interaction cost | a round trip each | wire action → host → optional re-render | in-process, immediate |
| Offline / no-server | no | the rendered tree stays live | yes |
| `Action.Dispatch` | not available | not available | **available** |
| Authoring language | any .NET | any .NET | F# only |
| First paint | server HTML | server HTML, or client-rendered | client-rendered (or SSR + hydrate) |

**Take server-driven** when the page is mostly static, or the interaction is inherently a server
round trip anyway, and you want no client-side state to reason about at all. The client holds
nothing; the server holds everything.

**Take the embedded renderer** when you want a live client-side render — local state, instant
control response, a tree that keeps working while the network is slow — from a host that is not
going to run a JavaScript build. This is the C# and VB story, and it is the F# story for a team
that does not want Fable.

**Take full Fable** when you want typed in-process message dispatch: an `Action.Dispatch of 'Msg`
delivering your own message type to your own `update`, with no serialisation between them. That
is a real capability the other two tiers cannot offer, and it costs an F#-only client and a Node
build.

### They compose

The tiers are not exclusive. An app can server-render its first paint and hand the same tree to
the embedded renderer to hydrate; an app can serve most pages server-driven and one dashboard
through the embedded renderer. The wire format is the same in every case, which is what makes the
choice reversible.

---

## 2. The interaction model

**The browser raises a wire action. The host binds the behaviour.**

This is the part that is silent when you get it wrong, so it is worth stating exactly.

### `Action.Dispatch` does not cross the wire

`Action.Dispatch of 'Msg` carries a **host closure**. The canonical encoder emits the case's
discriminator and **drops the payload** — a `Dispatch` encodes as `{"$type":"Dispatch"}` — and the
decoder rebuilds it as the `"<closure>"` sentinel. This is documented wire behaviour, not a
defect.

The consequence is precise: a button whose `onClick` is a `Dispatch`, serialised and rendered in
the browser, **still renders and still fires**. It just does nothing. And the emitted bytes carry
no trace of the loss — nothing downstream can distinguish a `Dispatch` that lost a message from
one that never had a payload.

It is three cases, not one: `Dispatch`'s `msg`, `Call`'s `onResult`, and `ReadFileBody`'s
`onRead` all carry closures and all erase.

**Full Fable is the one tier where `Dispatch` survives, because there the tree is never
serialised.**

### What does cross

- **`Action.Notify(channel, payload)`** — a channel name and a JSON payload. The standalone
  bundle surfaces it through its `onNotify` mount option; set `MountOptions.NotifyEndpoint` and
  the emitted snippet POSTs `{"channel": …, "payload": …}` there.
- **`Action.Call(endpoint, into: …)`** — the wire-native round trip. The response is written into
  a `$state` slot or a named query result, and every reader of that binding re-renders. Note
  `into:` and **not** `onResult`: `onResult` is a closure and does not survive either.
- **State-bound controls** — `open` / `activeIndex` / `value` bound to `$state`. A `Disclosure`
  bound to state renders as a native `<details>` honouring it, with no host involvement at all.
- **Declarative op chains** — `SetState`, `WriteToClipboard`, tree ops.

### Typed dispatch, without inventing anything

An untyped `(channel, payload)` pair is not where the story ends. The typed answer is
**hole-binding**: the artifact is a pure tree with **declared action holes**, and typed dispatch
is a host-supplied **handler table bound to those holes**, validated against the artifact's
signature — every key addresses a declared hole, every handler's effect is within its hole's
ceiling, every hole is bound.

That mechanism is uniform across hosts and across languages, which is exactly why this package
does not invent a second one. A `MessageContract` that lowered typed messages onto `Notify` was
prototyped for this package and **withdrawn**: it duplicated hole-binding with a weaker
guarantee — an unchecked channel string across a language boundary and a hand-written codec.

So the division of labour is:

1. The browser raises a wire action (`Notify`, or `Call` with `into:`).
2. Your host receives it — a POST for `Notify`, an endpoint hit for `Call`.
3. Your host binds typed behaviour to the artifact's declared action holes.
4. If the tree changed, send the new tree (or ops) to `handle.update` / `handle.applyOps`.

---

## 3. Encoding a tree for the browser

Encode with **`CanonicalJson.encodeNodeForTransport`**, not `encodeNode`:

```fsharp
match CanonicalJson.encodeNodeForTransport tree with
| Ok json -> Snippet.mount options Theme.vocabularyFingerprint json
| Error paths ->
    // paths names every node id and slot whose interaction would be lost
    failwithf "tree carries closures that will not survive: %A" paths
```

`encodeNode` feeds the op-stream hash chain, where two ops differing only in an opaque `'Msg`
hash identically **by design**; making that path refuse would break the property it exists to
have. So intent is taken from which encoder you call. `encodeNodeForTransport` produces the same
canonical bytes and refuses a tree whose interaction would be lost.

Two more checks sit beside it, and neither replaces the other:

- **`PreEmitValidate.validateForTransport`** reports **FUARAN112** (Warning) for the same three
  slots. The backstop for an author who reaches past the encoder.
- **`Action.dispatch`'s doc comment** names the constraint at the authoring site.

`PreEmitValidate.validate` says nothing about a closure, deliberately: an in-process Fable host
renders `Dispatch` correctly and forever, so relevance is the caller's to declare.

### What is *not* claimed

Only the `Action` DU's three closure slots are refused. Other slots erase too — a
`FormFieldKind.onChange`, a `TabsSpec.onSelect`, a `DisclosureSpec.onToggle` — and are
deliberately out of scope, because the renderers' write-back default reconstructs their behaviour
from the control's own writable binding: the closure is lost and the interaction is not.
`Binding.Computed` is FUARAN084's subject already.

A tree `encodeNodeForTransport` accepts carries no **unrecoverable** interaction. That is
narrower than "nothing about it erased", and the narrower claim is the true one.

---

## 4. Keeping the embedded copy honest

The bundle is a built artefact from another repository, byte-copied and committed. A copy across a
repo boundary goes stale silently, so it carries a fingerprint and three things read it:

- `scripts/sync-renderer-web.ps1 -Sync` writes it, from the sources it copied. Idempotent: run it
  twice and the second run writes identical bytes.
- `dotnet run --project Build.fsproj -- RendererWebCheck` fails when it disagrees with this
  checkout, and runs inside `Check`.
- `Snippet.mount` warns at **development** time when it disagrees with the authoring constants a
  consumer actually restored — a pair no gate in this repo ever sees.

The gate makes two different statements depending on what is present, and says which: a **version
and vocabulary** match is answerable from committed text always; a **byte** match needs the
bundle built in the `fuaran-ts` sibling, and is reported as `NOT CHECKED` when it is not. "Nothing
to check here" and "everything checked" must not read alike.

The maintainers' rule: **a renderer or stylesheet change re-syncs the embedded copy in the same
change-set.**

---

## See also

- [`../src/Fuaran.UI.Renderer.Web/README.md`](../src/Fuaran.UI.Renderer.Web/README.md) — the package API.
- [`SERVER_DRIVEN.md`](SERVER_DRIVEN.md) — the server-driven tier.
- [`SSR.md`](SSR.md) — server-side rendering, the first paint this tier can hydrate onto.
- [`ERROR_CODES.md`](ERROR_CODES.md) — the `FUARAN###` band, FUARAN112 included.
