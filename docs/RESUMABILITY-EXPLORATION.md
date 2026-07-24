# Resumability exploration – zero-hydration load via `Action`-as-data

> **Status:** Design exploration (2026-06-13). Not shipped. This is the written-up
> rationale behind roadmap Phase 177; it contrasts a proposed *resumability* load
> strategy with the **shipped** isomorphic-hydration path
> ([`Fuaran.UI.Renderer.Hydration`](../src/Fuaran.UI.Renderer/Hydration.fs), Phase 143)
> and sketches a spike to decide whether to build it.

> **One line:** because a Fuaran `Action<'Msg>` is **serialisable typed data, not an
> opaque closure**, the server can ship a fully server-rendered page that boots
> **zero framework JavaScript on load** and resumes the Elmish update loop *only* on
> first interaction – the load-strategy a closure-based framework has to work hard to
> approximate, Fuaran gets almost for free from the shape of its own `Action` type.

---

## 1. What ships today, and the gap

The SSR family (Phases 138–143) renders a `Node<'Msg>` tree to crawlable, no-JS HTML
on plain .NET ([`Fuaran.UI.Renderer.Server`](../src/Fuaran.UI.Renderer.Server/)), then
**hydrates** it client-side: the browser reconstructs the *same* tree in F# and calls
React `hydrateRoot` over the server markup, after which "the Elmish update loop drives
interactivity" ([`Hydration.fs`](../src/Fuaran.UI.Renderer/Hydration.fs) lines 13–17).
Phase 163 islands narrow this to *selected* subtrees, but the hydration model is
unchanged within an island: **every event handler is wired at load**, by re-running the
view.

That is the React/Feliz contract, and it is correct. But it pays a load-time cost
proportional to the interactive surface: to know what an `onClick` *does*, hydration
must execute the view that produces it. The handler is, to the framework, an opaque
closure – its meaning is only recoverable by running code.

This is the seam where Fuaran differs from a closure-based framework, and the difference
is structural, not incidental.

## 2. Why Fuaran is unusually suited to resumability

A resumability strategy serialises *what each interactive node does* into the HTML, ships
**one** delegated listener, executes nothing at load, and resumes the specific handler on
first interaction. The hard part for any framework attempting this is serialising the
handler – a closure that has captured local state.

In Fuaran the handler is **already declarative typed data**. The event-bearing props on
the typed tree carry an [`Action<'Msg>`](../src/Fuaran.UI/Types.fs) (the
`and [<RequireQualifiedAccess>] Action<'Msg>` DU), whose data-shaped cases – 
`Dispatch`, `Navigate`, `Notify`, `SetState`, `Chain`, `CommitLocal`, `WriteToClipboard`,
`AiTool` – are values you can *read* rather than execute:

| | closure-based framework | Fuaran |
|---|---|---|
| what an `onClick` is | an opaque closure | `Action.Dispatch SaveForm` – a typed DU value |
| to know what it does | execute the view | **read the value** |
| load-time cost | hydrate (re-run the view) | **≈ 0 (read the value)** |
| serialisation effort | high (closure capture) | **trivial (`Action` is already data + wire-survivable)** |

The wire format already proves most of `Action` is serialisable: the
[`WIRE_FORMAT.md`](WIRE_FORMAT.md) `Action` encoding round-trips through the conformance
corpus. Resumability reuses that encoding as a per-node *resume envelope* rather than
inventing a new one.

## 3. The mechanism (sketch)

The server emits three things:

1. **Inert server-rendered HTML** (the shipped `Renderer.Server` path), each event-bearing
   node carrying its deterministic SSR id (Phase 138).
2. **The SSR-resolved `Model`**, in a `<script type="application/json">` envelope (the
   §-minimal-record + defaults discipline keeps it small – an absent field is its default).
3. **The `nodeId → Action<'Msg>` map** for every event-bearing node, in the same envelope,
   serialised with the existing wire `Action` codec.

The client ships **one small interpreter** (not the app):

```
load:        HTML + <script resume-envelope> + 1 document-root listener   ← 0 framework JS executed
1st event:   interpreter walks to nearest node id → looks up its Action → runs it
later:       the touched subtree is live; the rest of the page stays inert HTML
```

- **One delegated listener at the document root** (O(1), not per-node). On an event it
  walks to the nearest node id, looks up the `Action`, and runs it.
- **Data-shaped `Action`s need no view at all.** `Navigate` / `Notify` / `SetState` /
  `WriteToClipboard` are executed directly against the host runtime
  ([`IFuaranRuntime`](../src/Fuaran.UI.Renderer/)) – there is nothing to render to honour
  them.
- **`Dispatch msg` lazy-boots its module.** On the first `Dispatch` for a given module the
  interpreter lazy-loads that module's `update`/`view` chunk, runs `update`, and renders the
  affected subtree – handing it from server-HTML to Fable/React from that point. Static
  `Display`/`Layout` outside any touched subtree stays inert HTML forever.

The composition with islands is natural: an island *is* "the subtree first-interaction
touches", in its laziest form. Eager islands (see §5) boot at load or on visibility;
everything else resumes on demand. There is no hydration window to mind.

## 4. SSR pre-satisfies `init` (the synergy)

In Elmish, `init : unit -> Model * Cmd<Msg>`, and the `init` `Cmd` typically (a) loads
initial data and (b) subscribes to live channels. When `Query`/data bindings are resolved
**server-side** during SSR, (a) is already done – the resolved values are in the serialised
`Model`, so resume **skips the data-loading init `Cmd`s entirely**. SSR doesn't only
pre-render the view; it pre-satisfies `init`. What remains is a small classifier over the
residual effects:

| init effect | resume handling |
|---|---|
| data load (`Cmd.OfAsync` query) | **skip** – SSR already resolved it into `Model` |
| live subscription needed pre-interaction | **eager** – run at load (usually one channel attach; the only non-zero load cost) |
| subscription tied to a specific island | **deferred** to that island's boot |
| everything else | **lazy** – run on first interaction |

## 5. The hard cases (and where they fall back)

The cases that *don't* serialise are bounded and already named in the type system, so each
has a clean fallback to hydration for that subtree only – never a broken page:

| case | why | resolution |
|---|---|---|
| `Action.Call(endpoint, onResult: obj -> 'Msg)` / `Action.ReadFileBody(_, _, onRead)` | the continuation is an obj-erased closure (`"<closure>"` sentinel on the wire, per `Types.fs`) | serialise as a **named `Msg`-constructor reference** – the schema (§4m / Phase 96) already enumerates `Msg` cases, so the interpreter maps the result to the named `Msg`; closures over locals become explicit `Msg` args |
| `Binding.Computed (BindingContext -> 'T)` | a closure that "doesn't serialise" by construction (`Types.fs` line 1257) | classify the containing subtree as an **eager island** – it can't be server-final, so it hydrates |
| controlled inputs | the value isn't tracked before boot | **uncontrolled-until-boot**: the browser handles typing natively; the interpreter reads the live DOM value at dispatch (the `Action` already carries the typed value shape); the subtree becomes React-controlled after first render – forms work before any JS, which is a feature |
| `Custom(moduleId, componentId, props)` | opaque Feliz / JS-only component | `ClientOnly` → eager island, never resume-rendered; the server emits the loading-state skeleton as inert HTML |
| non-click events | listener coverage | the single root listener registers exactly the event types the envelope enumerates |

## 6. How it composes with shipped contracts

- **⊕ SSR parity corpus (Phase 142).** Resume requires the server and client to agree on the
  tree – exactly the property the parity corpus already locks. Same contract, second beneficiary.
- **⊕ Determinism.** A mismatch between SSR and resumed tree must degrade to a client render,
  never to a broken UI – so a deterministic server render (injectable clock / seeded RNG, the
  same precondition the closed-loop verify path needs) is load-bearing here.
- **⊕ FGP 5 (op-stream is the source of truth).** The interpreter dispatches the same `Action`s
  the hydrated path would, so op-stream + telemetry emission is unchanged – resumability is a
  *load* strategy, not a new dispatch path; it must emit to both sinks identically.
- **⊕ FGP 2 (`Fuaran.UI` standalone / Fable-clean).** The interpreter is a small client runtime
  that must stay Fable-portable; it consumes only the public typed surface + the wire `Action`
  codec.

## 7. Spike plan (the decision, not the build)

**Goal:** prove a real page loads with ≈ 0 executed framework JS and resumes correctly on first
interaction.

**Build (throwaway-grade):**
1. resume-envelope serialiser (server) over a sample module's `Model` + tree, reusing the wire
   `Action` codec;
2. the interpreter runtime (client): root delegated listener + envelope lookup + lazy module-chunk
   loader + first-render handoff to Fable/React;
3. the init-effect classifier (skip / eager / deferred / lazy);
4. resume-mismatch detection (reuse the Phase 142 parity marker contract).

**Measure (vs the Phase 143 hydrate baseline, same page):** framework JS *executed* at load
(target ≈ 0); TTI / INP; first-interaction latency (lazy-chunk load + `update` + subtree render);
total JS *transferred* at load (resume envelope vs full hydrate bundle).

**Decide:** default-on resumability vs islands-hydrate fallback *per surface class*; the
eager-island threshold; whether the first-interaction latency stays imperceptible (pre-warm on
pointer-over is the lever).

## 8. Open questions / risks

- **First-interaction latency** – the lazy chunk load is the variable; speculative pre-warm on
  `visible` / `pointerover` is the mitigation. Must stay imperceptible or the strategy loses to
  hydration.
- **Envelope size** for wide trees / large `Model`s – the minimal-record discipline + per-island
  lazy envelope sections bound it.
- **Secrets / PII in the serialised `Model`** – a `Model`-field serialisation allowlist is a hard
  prerequisite, not optional. The envelope ships to the client in clear.
- **Accessibility of uncontrolled-until-boot inputs** – verify focus / ARIA survive the
  hydration handoff on first interaction.
- **Interaction with the Phase 163 islands boot path** – confirm the resume-envelope shape and the
  islands hydration boundary agree rather than conflict; resumability should be the *laziest* island
  boot, not a parallel mechanism.

## 9. Relationship to hydration

Resumability does not replace isomorphic hydration – it is a strictly-lazier load strategy that
*falls back* to hydration for the subtrees that can't be server-final (§5). The shipped Phase 143
path stays the correct default until a spike (§7) shows resumability wins on a real page; the two
share the server render, the parity contract, and the dispatch/op-stream path, differing only in
*when* and *how much* client code executes at load.
