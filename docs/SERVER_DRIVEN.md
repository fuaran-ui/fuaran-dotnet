# Server-driven interactivity (`Fuaran.UI.ServerDriven`)

The **third client tier** for a Fuaran tree – an HTMX / Phoenix-LiveView /
Blazor-Server-shaped runtime. Keep the Elmish `update` loop on the **server**;
ship one tiny generic JS shim to the browser; patch the DOM in place. The other
two tiers are the Fable client renderer (`@fuaran-ui/renderer` / the F#
`Fuaran.UI.Renderer`) and the `hydrateRoot` hydration mount; this tier owns the
model server-side and ships ~nothing to the browser.

This document is the architecture + the transport-choice analysis + the two
per-arm decision tables. The package README (`src/Fuaran.UI.ServerDriven/`) is
the API surface; this is the "why + how it fits".

---

## The loop

```
 browser event on a [data-fuaran-node-id] element
   │  (the generic shim forwards (nodeId, event, payload) as a LiveEvent)
   ▼
 ① Validation.validate   — G1 inbound trust boundary (default-deny)
   ▼
 ② Driver.interpret      — resolve the typed Action; fold update → new Model
   ▼
 ③ view newModel         — re-render the tree (server-side)
   ▼
 ④ TreeOpDiff.diff       — old tree → new tree → minimal TreeOp list   (Track A)
   ▼
 ⑤ Lowering.lower        — TreeOp list → DomPatch list (+ ClientEffect list)  (Track B)
   ▼
 ⑥ IFuaranLiveChannel.Push  — Frame { Seq; Patches; Effects } down the wire  (Track D)
   ▼
 the shim applies the patches + performs the effects — targeted, no full
 re-render, no flash.
```

Every step is a separate, individually-tested module; the driver (`Driver.step`)
is the one function that threads them.

---

## The three client tiers

| | **Fable client** | **Hydration** (`hydrateRoot`) | **Server-driven** (this) |
|---|---|---|---|
| Where the model lives | Browser | Browser (after mount) | **Server** |
| Initial paint | Client render | Server HTML, client attaches | Server HTML |
| Interactivity | Full client MVU | Full client MVU after hydrate | Server MVU, DOM-patched |
| JS shipped | The whole app (Fable→JS) | The whole app + SSR HTML | **One generic shim (~a few KB)** |
| Offline | Works | Works after hydrate | Needs the connection |
| Latency per interaction | Local | Local | One server round-trip |
| The "closure space" | Compiled to JS | Compiled to JS | **Invoked directly on the server** |

Reach for the server-driven tier when the closure space is rich / sensitive /
server-coupled (the model never leaves the server), when you want one shim
instead of a compiled bundle, and when a per-interaction round-trip is
acceptable (forms, tabs, buttons, navigation – all low-frequency).

---

## The server-closure win – "free here, hard there"

Because the closures stay on the server, this tier **invokes** them; the Fable
tier must **compile** every closure to JS, and a future TS-interpreter tier must
**interpret** a bounded action set. What that buys, per `Action` case:

| `Action` case | Server-driven | Fable client | (Hypothetical) TS interpreter |
|---|---|---|---|
| `Dispatch 'Msg` | Invoke → `update` | Compiled | Bounded-set interpret |
| `Call(endpoint, onResult)` | **Run server-side directly** | Compiled fetch + closure | Hard – interpret the continuation |
| `Notify` / `SetState` / `AiTool` | **Run server-side directly** | Compiled | Hard |
| `Binding.Computed f` | **Invoke `f` server-side** | Compiled | Hard – arbitrary closure |
| arbitrary `NodeKind.Custom` server logic | **Just runs** | Must compile the component | Out of reach |
| `CommitLocal` | Server-side flush | Client useState flush | Bounded interpret |

The computational arms run via the driver's injected `InterpretHostEffect` (the
host wires the real services); the core stays free of any host-service
dependency.

---

## The server-executable / client-only split (`ClientEffect`)

Not every `Action` arm has a server form. The *inherently browser* arms have no
DOM-mutation shape, so the driver lowers them to a `ClientEffect` the shim
performs. The result-bearing one (`ReadFileBody`) round-trips its result back as
a `file-read` `LiveEvent`.

| `Action` arm | Disposition |
|---|---|
| `Dispatch` | **Server-executed** → `update` |
| `Call`, `Notify`, `SetState`, `AiTool`, `CommitLocal` | **Server-executed** (host `InterpretHostEffect`; a form-submit's `Call` routes through `InterpretSubmitCall` with the submit body when wired – Phase 820, §"Submit payload") |
| `Navigate route` | **`ClientEffect.Navigate`** |
| `WriteToClipboard text` | **`ClientEffect.WriteToClipboard`** |
| `ReadFileBody(file, encoding, onRead)` | **`ClientEffect.ReadFileBody`** (body round-trips as a `file-read` `LiveEvent`; the blob is browser-held) |
| `Chain` | Folds – each inner arm dispatched by the same table |

This is why the wire needs a second instruction channel (`ClientEffect`)
alongside `DomPatch`: the server-driven runtime must never try to execute a
browser-only effect server-side.

---

## G1 – the inbound trust boundary (non-negotiable)

The browser sends *raw* `(nodeId, event, payload)`. The runtime trusts none of
it. `Validation.validate` is the default-deny gate – the server-side mirror of
the client `runAction` dispatch gate. Four checks, first-failure-wins:

1. **Node exists** – `findNode` against the *current* server tree (rejects stale
   / forged ids).
2. **Event legitimate for the kind** – a `Button` accepts `click`, a `Select`
   `change`; non-interactive kinds accept nothing. The per-kind vocabulary
   (`Validation.legitimateEvents`):

   | Kind | Legitimate events | Resolution |
   |---|---|---|
   | `Button` | `click` | `OnClick` |
   | `Select` | `change` | `OnChange (payload.value)` |
   | `Form` | `submit`, `change`, `input` | `OnSubmit` on submit; field-level change/input are the form policy's (§ forms) |
   | `Filters` | `change`, `input`, `click` | name-addressed: `payload.name` picks the filter, its `onChange` gets `payload.value` (`""` → `None` for choice-shaped filters); segmented horizontal options arrive as clicks |
   | `FileUpload` | `change`, `file-read` | `ReadFileBody` continuation |
   | `Tabs` | `click`, `change` | `OnSelectTag (payload.value)` else `OnSelect (payload.index)` |
   | `Stepper` | `click`, `change` | `OnSelect (payload.index)` – the shim bridges `data-step-index` |
   | `Disclosure` | `click`, `change`, `toggle` | `OnToggle (payload.open, default true)` |
   | everything else | – (default-deny) | – |
3. **Payload in bounds** – a `Select` value must be one of its statically-
   resolved options (dynamic bindings + per-field form bounds are enforced
   downstream at interpret time).
4. **Dispatch policy gate** – the resolved `Action` passes the injected
   `canDispatch` (the host maps `Action` → its renderer `ActionDescriptor` →
   `IFuaranRuntime.CanDispatch`).

A rejected event mutates **no** state and pushes **no** frame; the reject is
emitted through the **always-on `DriverServices.OnReject` sink** (Phase 212 – 
see *Observability + resource bounds* below), independent of the telemetry
opt-in, so a denial leaves an audit trail by default. Without G1 a
server-driven app is an authorization-bypass + injection surface – and the app
classes that most want this tier (business / regulated / multi-tenant) are
exactly the ones that breaks.

---

## Inbound trust floor – connId binding, guarded parse, attributed audit (Phase 211)

G1 gates *what an event may do* to the tree. Phase 211 hardens the layer beneath
it – *who the event is from, that its body is well-formed, and who the audit
trail attributes it to* – so the SSE+POST inbound path is not a trusted session
driver by default.

### The auth floor – the host layers auth in front of `/live/*`

**The host must place its own authentication in front of the `/live/stream` and
`/live/event` endpoints** (the standard ASP.NET auth pipeline – the SSE+POST
default exists partly *because* every event is an ordinary HTTP request that
flows through that pipeline for free; see "Why SSE+POST is the default"). The
server-driven package does not ship an identity system; it *binds to* whatever
principal the host's auth established, via `LiveAppConfig.ResolvePrincipal :
HttpContext -> string` (default: `ctx.User.Identity.Name`, `""` when
unauthenticated).

### connId is bound, not bearer

Before Phase 211 the routing `connId` was a bare `Guid.NewGuid()` in the
`fuaran-conn` cookie; the registry lookup was the entire gate, so a leaked or
guessed id was effectively a bearer token. Now the cookie carries a **signed,
principal-bound token** – `connId "." HMAC-SHA256(secret, connId "|" principal)`
(`ConnToken.sign` at stream-open, `ConnToken.verify` on every POST):

- A forged / guessed `connId` carries no valid signature → `verify` returns
  `None` → the POST handler answers **401**, never routing it.
- The principal is folded into the signed pre-image, so a cookie lifted from one
  user does not verify under another's principal – the binding is cryptographic,
  not a lookup.
- With no auth wired the principal is `""` for everyone, and the HMAC *still*
  closes the forgeability gap (the strict improvement over a bare GUID). Layering
  auth then additionally binds the connId to the real user, at no code change.

`LiveAppConfig.Secret` is the HMAC key; it defaults to a fresh per-process key
(`ConnToken.freshSecret ()`) – secure by default but not restart-stable /
multi-node. A host that needs tokens to survive a restart or verify across nodes
supplies its own stable secret (the same posture as the durable session store).

### Guarded parse – a malformed body is a clean 400, on both transports

The inbound JSON parse now lives **once** in `Fuaran.UI.ServerDriven.Inbound`
(the core), shared by both backends, so they cannot diverge:

- `Inbound.tryParseLiveEvent : connId:string -> json:string -> LiveEvent option`
  returns `None` for a non-JSON body. The SSE+POST handler maps `None` to a clean
  **400 Bad Request** (previously a non-JSON POST threw an *unhandled* 500); the
  WebSocket recv loop skips the message and keeps the socket alive. Both fail
  identically – default-deny by shape, not by discipline.
- A JSON number outside the `Int32` range for `lastSeq` degrades to `0` (via
  `TryGetInt32`) rather than throwing. A structurally-valid-but-junk body
  (missing / mistyped fields) is **not** a parse failure – it parses to a
  safe-empty `LiveEvent` the G1 boundary then rejects, exactly as before.

### Attributed durability – the journal names the real user

`LiveConnection.EnableDurability(store, ?userId, …)` already stamps every
journaled `OpRecord.UserId`; the gap was that the ASP.NET wiring had no principal
to pass, so the hash-chained audit trail recorded the placeholder
`"server-driven"` for everyone. `LiveAppConfig.ConfigureConnection` now receives
the **resolved principal** as its first argument, so the host threads it in:

```fsharp
let makeSession () = Driver.init (DriverServices.create renderFragment) update view initialModel

let config =
    { defaultConfig makeSession with
        // The host's ResolvePrincipal (or the default ctx.User.Identity.Name)
        // feeds the connId binding AND this durability attribution.
        ConfigureConnection =
            fun principal conn ->
                conn.EnableDurability(sessionStore, userId = principal) }

mapFuaranLive app config |> ignore
```

The audit journal is now per-user attributable (load-bearing for the regulated /
audited class), sharing the exact principal the connId binding verifies against.

> **Stability note.** `LiveAppConfig` gains `Secret` + `ResolvePrincipal`, and
> `ConfigureConnection` gains the `principal` parameter – additive to the tier's
> capability but a **semantically-tightening** change to the inbound contract: a
> malformed POST now returns 400 (was 500) and a POST without a valid signed
> cookie returns 401 (was routed on the bare id). A host relying on the prior
> unauthenticated-connId behaviour must layer auth in front of `/live/*`.

---

## Observability + resource bounds (Phase 212)

G1 gates *what an event may do*; G2 (`InteractionBudget`) bounds *compute per
interaction*. Phase 212 closes the two remaining blind spots around them: a
denial with telemetry off used to vanish silently, and the per-connection
queues could grow memory without limit. All additive; the G1/G2 gate semantics
and the wire format are unchanged.

### The always-on reject sink – `DriverServices.OnReject`

`DriverServices` carries an `OnReject : RejectReason -> unit` seam.
`LiveConnection.Handle` emits **every** rejected step through it – independent
of the `EnableTelemetry` opt-in – so a forged-node / dispatch-denied event is
recorded by default, not only when interaction telemetry happens to be on.
`DriverServices.create` defaults it to no-op (the transport-free core has
nowhere to log); **the AspNetCore backend composes a default logging sink onto
it at stream-open** – a G1 reject reaches the host's `ILogger` (category
`Fuaran.UI.ServerDriven`, warning level, via the log-safe
`RejectReason.describe` – never payload values), falling back to stderr when no
logger factory is registered. A host sink wired in `MakeSession` still fires;
the default composes, it does not replace.

### Bounded per-connection queues – documented caps

A slow-loris reader that keeps POSTing events used to give one connection
unbounded memory-growth vectors. All three are now capped:

| Queue | Cap (default) | On overflow |
|---|---|---|
| SSE frame queue (`SseChannel`) | `SseDefaults.FrameQueueCapacity` = **256 frames** | the channel **closes** – the drain loop ends the stream; `EventSource` auto-reconnects with `Last-Event-ID` and `Resync` replays the missed frames (a clean reconnect-replay, not silent frame loss) |
| WS frame queue (`WsChannel`) | `WsDefaults.FrameQueueCapacity` = **256 frames** | the channel **closes** – the send pump ends and the socket closes; the client's WS adapter reconnects and `Frame.Seq` + `Resync` replay the missed frames (the same stalled-reader guard as `SseChannel`, mirrored onto the WebSocket backend) |
| Reconnect-replay buffer (`LiveConnection`) | `LiveConnectionDefaults.ReplayBufferCapacity` = **512 frames** | the **oldest** frame is evicted; a client reconnecting from behind the retained window gets a partial replay (the Phase 155 durable journal is the complete recovery) |

All three caps are per-channel/per-connection overridable (`SseChannel(capacity =
…)`, `WsChannel(capacity = …)`, `LiveConnection(…, replayBufferCapacity = …)`). **Semantically-tightening
note:** a queue this deep means the reader is stalled, not slow – a stalled SSE
connection is now *closed* (forcing a clean reconnect) where it previously grew
server memory unbounded. The G2 budget bounds compute per interaction; these
caps bound memory across queued frames – the two halves of "no arbitrary cost".

### Bounded-action no-op diagnostics

The bounded interpreter's documented no-op arms (`Notify` / `AiTool` / `Invoke`
/ `Dispatch` / `Call` / `CommitLocal`) each emit a readable
`BoundedDiagnostic.UnsupportedOnBoundedPath (nodeId, action)` through
`BoundedOutcome.Diagnostics`, threaded into `BoundedStepOutput.Diagnostics` – 
so a generated tree that *intended* a `Call` is an observable "this action is
inert on the generated-app path" the introspection tools can surface, not a
silent dead end for AI-emission debugging. Observability only: the no-op
behaviour and the no-arbitrary-code invariant are unchanged
(`BoundedDiagnostic.describe` gives the log-safe text – action constructor
names, never payload values).

---

## Transport – `IFuaranLiveChannel` + the two backends

The transport sits behind a per-connection seam (`IFuaranLiveChannel`:
`Push` / `Receive` / `Close`) so the core (driver + `DomPatch` + lowering +
shim) is transport-blind. The driver pushes `Frame { Seq; Patches; Effects }`
and receives `LiveEvent`s. This is what makes a WebSocket backend a *drop-in
swap* rather than a fork – both backends implement the one interface.

### Why SSE+POST is the default (and not just "it's proven")

Both backends are freshly written against the same seam, so the choice is **not**
maturity. SSE+POST is the default for four *durable* reasons:

1. **Infra traversal.** SSE is a long-lived HTTP response; POST is POST – both
   ride natively through proxies / CDNs / WAFs / L7 load balancers. WebSocket
   needs an `Upgrade`/101 handshake a meaningful fraction of enterprise proxies
   and older LBs block or mishandle.
2. **Per-message governance for free.** Every client→server event is an ordinary
   HTTP request, so it flows through the *whole* existing pipeline (auth
   fail-closed, rate-limit, audit, CSRF, correlation-id) **per event** – aligned
   with Fuaran's default-deny-by-shape posture. WS authenticates once at the
   handshake; you then rebuild per-frame authorization / rate-limit / audit
   inside the socket loop.
3. **Browser-native reconnect that maps 1:1 onto the journal.** `EventSource`
   auto-reconnects and replays `Last-Event-ID`; set the SSE event `id:` to the
   `Frame.Seq` and reconnect-replay against `LiveConnection.Resync` is nearly
   free. WS has no built-in reconnect – hand-rolled backoff + resequencing.
4. **Smaller, debuggable surface** – plain HTTP (devtools-visible, curl-able, one
   access-log row per event); no ping/pong / close-frame / framing machinery.

**The honest counter (WS's intrinsic win):** latency + per-message overhead. WS
is one full-duplex channel with tiny frame overhead; SSE+POST pays a full HTTP
request per client→server event (and the SSE stream eats one of HTTP/1.1's
6-per-origin slots). For **high-frequency input** (per-keystroke, drag,
pointer-move, live cursors) WS is materially faster. → Default SSE+POST; reach
for WS when a *measured* interaction pattern needs the latency. The seam makes
that a swap, not a rewrite – not a one-way door.

> **Status:** shipped – the seam, the in-memory reference channel, the
> `LiveConnection` glue, the transport-agnostic reconnect replay (`Resync`), and
> **both backends**: `Fuaran.UI.ServerDriven.AspNetCore` (SSE+POST, verified
> end-to-end via `samples/server-driven`) and `Fuaran.UI.ServerDriven.WebSocket`.

---

## Reconnection + replay

`LiveConnection` buffers each pushed `Frame` (bounded – Phase 212: at
`LiveConnectionDefaults.ReplayBufferCapacity` the oldest frame evicts) and
exposes `Resync(lastSeq)`, which re-pushes the retained frames newer than the
client's last-applied `Seq`. The session model already survives a transport
drop in the `LiveConnection`; `Resync` recovers the unacknowledged frames.
Implemented **once in the core**, against the seam – both backends share it.

The durable form promotes the `OpStream` journal (hash-chained `OpRecord`
sequence) from audit log to **load-bearing transport-recovery layer**: replay
`OpRecord`s since the client's `Sequence` from the last checkpoint instead of an
in-memory frame buffer. **Shipped in Phase 155** – see
[Session durability](#session-durability-phase-155--checkpoint--journal-replay)
below. The in-memory `Resync` stays the zero-config single-node default; the
durable store survives a restart and crosses nodes.

---

## Per-connection lifecycle + the single-instance boundary

The session model lives in **server memory keyed by connection id** – one
`LiveConnection` per live connection, holding one evolving `Model`. This is a
**single-instance assumption**: a multi-node host needs sticky sessions (route a
connection's events to the node holding its session) or a shared session store
(rehydrate the model on any node). Don't silently assume single-node – flag it at
deployment. The reconnect-replay layer is what makes a *brief* drop transparent;
a *node failover* needs the shared-store form, **shipped in Phase 155** (next
section): a shared `IFuaranSessionStore` lifts this boundary.

---

## Session durability (Phase 155) – checkpoint + journal-replay

Phase 152 holds the session in **server memory**, so a restart / deploy /
scale-out loses every in-flight session. Phase 155 closes that with the
`IFuaranSessionStore` seam, reusing primitives already shipped in
`Fuaran.UI.OpStream`: the hash-chain-linked `Checkpoint<'Msg>` (a rendered-tree
snapshot) + the `OpRecord<'Msg>` journal + `CheckpointedReplay`. **Tree-level
durability**: every applied `TreeOp` is appended to a hash-chained journal, and
the rendered tree is periodically checkpointed; reconstruction = latest
checkpoint ≤ head + replay the journal tail through the apply engine.

### The seam (six-portability-rules posture)

```fsharp
type IFuaranSessionStore<'Msg> =
    abstract member Checkpoint : sessionId:string * checkpoint:Checkpoint<'Msg> -> Async<unit>
    abstract member LoadLatest : sessionId:string -> Async<Checkpoint<'Msg> option>
    abstract member AppendOp   : sessionId:string * record:OpRecord<'Msg> -> Async<unit>
    abstract member OpsSince   : sessionId:string * sequence:int -> Async<OpRecord<'Msg> list>
```

A pure storage port – stateless between calls, identity-by-value (`sessionId` ==
the persisted `StreamId`). Two factories:

- `SessionStore.inMemory ()` – zero-config, single-node, lost on restart (the
  current 152 behaviour preserved).
- `SessionStore.overSink sink` – adapts any shipped `IOpStreamCheckpointSink`
  (the **Sqlite** durable / shared backend, or a future Redis) without the core
  knowing which.

### Wiring a connection

`LiveConnection.EnableDurability(store, ?sessionId, ?checkpointEvery, ?userId,
?clock)` turns it on **non-breakingly** – a connection constructed the 152 way
behaves exactly as before until you call it. When on, every applied step is
journaled (the durable journal advances in lock-step with the host's own
`OnApply` sink, which still fires – FGP 5), and the tree is checkpointed every
`checkpointEvery` ops. `CheckpointNow()` flushes on graceful disconnect /
explicit session-end.

### Reconstruction + integrity

```fsharp
SessionReplay.reconstruct store sessionId genesisTree
  : Async<Result<Node<'Msg> * int * string, SessionReconstructError>>
```

Loads the latest checkpoint, **verifies its snapshot hash**, **verifies the
journal tail's hash chain** (contiguous sequence + `PreviousHash` links +
recomputed `Hash`) – both *before* the apply engine runs – then folds the tail.
A tampered snapshot or a forked / corrupt segment surfaces an explicit
`SessionReconstructError` (`SnapshotHashMismatch` / `JournalIntegrity` /
`ReplayFailed`), never a silent bad resume. The returned `(tree, sequence,
headHash)` feeds `LiveConnection.ResumeFrom`, which re-baselines the connection
and pushes a single full-document resync `Frame` so the reconnecting client
adopts the exact current DOM.

> **Tree vs model.** Reconstruction restores the **rendered tree** (the client
> DOM). For the **bounded driver** (the generated-app path, where state is data)
> the store is fully reconstructable. For the hand-authored `'Model` driver the
> model is host-owned and not generically serialisable, so the host re-binds it
> on resume; the client DOM is correct either way.

### Deployment shapes

| Shape | Store | Survives restart? | Multi-node? |
|---|---|---|---|
| Single-node, in-memory | `SessionStore.inMemory ()` | No | No |
| Single-node, durable | `SessionStore.overSink (sqlite)` | Yes | No |
| Multi-node, shared | `SessionStore.overSink (shared backend)` + sticky-or-shared routing | Yes | Yes |

With a shared store, a reconnect on a **different node** reconstructs from the
shared backend – the single-instance boundary above is lifted. `SessionRegistry`
provides the in-process lifecycle bookkeeping (`Touch` / `End` /
`GarbageCollect(idleFor, now)`) to GC abandoned sessions; the store retains each
session's journal per its own retention / compaction policy (load-bearing for the
regulated / audited class).

---

## Form / input / local-state policy

`Binding.Local` (per-field buffer) is a client concern in the Fable tier (a
per-NodeId `React.useState` slot). The server-driven tier adopts policy **(b)**:
the field is **client-buffered**, and **the field's live DOM value IS the
buffer** – there is no separate shim-side state slot to seed. The server sees a
field's value only on a **flush**.

### The dividing line – round-trip vs buffer

| Interaction | Round-trips per event? | Why |
|---|---|---|
| `Button` click, `Select` change, tab / step / disclosure | **Yes** – one event → one server step | discrete, low-frequency; the model reacts immediately |
| **`Binding.Local` form field** `input` / `change` | **No – buffered** | typing is high-frequency; a per-keystroke round-trip would add input latency. The DOM value is the buffer; the server sees it on flush only |
| `Filters` text/choice input | **Yes** (debounceable via QW3) | a filter *is* the query – the server must re-resolve to reflect it |
| live-search input (`data-fuaran-debounce`) | **Yes, debounced** | wants server feedback, but batched |

So a field round-trips when its change *is* the interaction the server must react
to (a filter, a live search); it stays buffered when its change is just *typing
toward a later commit* (a form field). The line is **"does the model need this
value before the flush?"** – for a `Binding.Local` field the answer is no by
construction (that's what `Local` means), so it never round-trips per keystroke.

### The flush protocol (the shipped contract)

1. **Marker.** The server renderer marks each form-field control
   `data-fuaran-field="<fieldId>"` (the same additive shim-bridge pattern as
   `data-tab-index` / `data-filter-name`). The generic shim **suppresses** that
   field's `change` / `input` (policy (b) – nothing goes to the server per
   keystroke); the live DOM value is the buffer.
2. **`onSubmit` flush.** On a form `submit` the shim harvests every
   `data-fuaran-field` value into the submit payload (keyed by field id).
   `FormBuffer.step` then, after G1 + any Phase 156 validation, turns each
   `Binding.Local` field's buffered value into its `OnCommit` action (parsed
   through the binding's own `Parse`), folds them – in field-declaration order – 
   together with the form's `OnSubmit`, and runs the whole flush as **one**
   `update` → re-render → diff. Only the changed nodes patch; the form keeps its
   DOM identity, focus, and scroll (no full re-render, no flash).
3. **`Action.CommitLocal` flush.** An explicit "Apply" button
   (`OnClick = Action.CommitLocal fieldId`, the `LocalFlushTrigger.OnCommitAction`
   analogue) renders with `data-fuaran-commit="<fieldId>"`; the shim harvests
   just that field, and `FormBuffer.step` commits its buffered value without a
   full submit.
4. **Scope.** Only `Binding.Local` value bindings flush here – that is the
   per-field buffer the policy is about. A non-`Local` field is **not** buffered
   by this protocol (it had no client buffer), so enabling the flush is purely
   additive: a form built from non-`Local` fields behaves exactly as the
   pre-policy 152 path (only `OnSubmit` fires).
5. **Submit payload (Phase 820).** A `Call` (or `Notify`) in the form's
   `OnSubmit` – directly or `Chain`-nested – receives the harvested field
   values as its payload, keyed by field id: the HTML prior ("submitting posts
   the fields"), made real on the harvest that already exists. Included is
   every value the step-2 harvest delivered that names a **declared** field of
   the form – `Local` and non-`Local` alike, since every rendered control
   carries the `data-fuaran-field` marker – coerced as the shim read it
   (checkbox → bool, number → number, everything else its string value; a
   `null` – e.g. an empty number input – is omitted, the wire has no null; a
   pair control contributes its marker-carrying first input). A `Notify` gains
   the object merged into its payload under a `"fields"` key (an authored
   `"fields"` key wins – the merge never clobbers); a `Call`, which has no
   payload slot on the wire (`into` unchanged), receives the body
   `{"fields": {<id>: <value>, …}}` through the driver's
   `DriverServices.InterpretSubmitCall` seam (`None` default falls back to
   `InterpretHostEffect` with no body – the pre-820 behaviour). No wire
   change; an `OnSubmit` with no `Call`/`Notify` folds byte-identical actions.
   The `Local`/`CommitLocal` ceremony remains the precision path for
   cross-field choreography.

The buffer-flush composes with the Phase 156 validation half above
(`LiveConnection.EnableFormValidation`): on a valid submit the buffers commit and
the stale `data-fuaran-field-error` attributes clear; on an invalid submit the
field-error patches surface and **no** buffer is committed (the mutation is
suppressed). Both address fields by the `data-fuaran-field` marker. The worked
example is [`samples/server-driven/`](../samples/server-driven/) (`Form.fs` – a
buffered name + ranged age that commit on submit, plus an "Apply name" button
that commits one field via `CommitLocal`).

---

## Runtime form validation (Phase 156)

Fuaran's `Fuaran.UI.Validator` is **build-time**. The forms / wizard /
configurator class – a server-driven sweet spot – also needs a **runtime**
per-field feedback loop: submit values, the server validates them, field-level
errors come back. `Fuaran.UI.ServerDriven.FormValidation` is that round-trip.

On a form `submit` the driver runs a validator over the submitted values
(`ev.Payload`, keyed by field id) **before any mutation**:

- **invalid** → the session is unchanged, the output carries only the field-error
  patches (the mutation is **suppressed**); the rest of the form keeps its values
  + focus (no full re-render).
- **valid** → the field-error attributes are cleared and the normal step runs.

```fsharp
type FieldError = { FieldId: string; Message: string }
type FormSubmission<'Msg> = { FormNodeId: string; Form: FormSpec<'Msg>; Values: Map<string, LiveValue> }
type FormValidator<'Msg> = FormSubmission<'Msg> -> FieldError list   // host business rules, server-side
```

### Two layers – declared enforcement is non-bypassable

`FormValidation.enforceDeclared` re-checks the build-time-declared constraints
(`Required`, `RangedNumber` Min/Max) **server-side at runtime** – the build-time
validator cannot catch a client bypassing HTML5 validation (e.g. posting a number
as a string to dodge the spinner bounds). It runs **first, always**, composing
with the 152 G1 inbound gate, so "client validation is not a trust boundary" is
closed. The host `FormValidator` adds business rules on top
(`FormValidation.combine`); `FormValidation.declaredOnly` is declared enforcement
with no extra rules.

### Lowering – closure-free, idempotent

`FormValidation.lower form errors` emits the **full** field-error state: an
erroring field gets `data-fuaran-field-error="<message>"`, every other field has
the attribute removed. So it self-corrects – a field that just became valid drops
its stale error without a separate clear pass. The host CSS keys off the
attribute to surface the message inline (the styling is a host / Phase 158
concern; the patch + message is the data layer Phase 156 ships).

### Per-field vs whole-form

`enforceDeclared` / a host validator operate on a `Map<fieldId, LiveValue>`: one
entry for a **validate-on-commit** (per field, as the user leaves it), every
field for a **validate-on-submit** (whole form). Both lower to the same
field-error patch shape, so the shim handles them identically. (The per-field
*buffer* protocol – seeding the shim's `Binding.Local` state, the debounce vs
commit boundary – remains the 152 form-policy follow-on; Phase 156 specifies the
*validation* half of the round-trip.)

### Wiring

`LiveConnection.EnableFormValidation(validator)` is non-breaking – off until
called, so a connection without it submits exactly as the 152 path. The pure
`FormValidation.stepWithValidation validator session ev` is the directly-testable
core both the connection and a custom host use.

---

## Navigation + routing (Phase 157)

The content-site + wizard app classes need navigation. The server-driven tier
supports two modes, selected by the **host route table** (`Navigation.RouteResolver
= route -> Node tree option`):

- **Full-SSR route** (SEO-correct, the default for content): the resolver returns
  `None` → a real URL → a fresh `Renderer.Server` render served as a normal HTTP
  navigation. `Display.Link`'s `<a href>` is the crawlable, no-JS form. Lowered as
  `ClientEffect.Navigate route` (the shim sets `location.href`).
- **In-place tree swap** (app-like, for wizard steps / dashboard views): the
  resolver returns `Some routeTree` → the server diffs `currentTree → routeTree`,
  patches, and emits `ClientEffect.PushState route` so the URL updates **without a
  reload**.

**The resolver IS the mode switch** – if the host can render the route in-session
it's an in-place route, otherwise it's a full SSR route. Routing stays host-owned:
the language tier ships the mechanism, never an app's route table.

### URL + history

An in-place nav emits `ClientEffect.PushState route`; the shim calls
`history.pushState`. The shim also listens for `popstate` and round-trips a
`popstate` `LiveEvent` carrying the popped route – the routing layer swaps the
tree to match and does **not** push state again (the browser already moved).
`popstate` is not node-addressed, so it is handled *outside* the per-node G1 gate.

### Deep-link + reload correctness

A direct hit on an in-place route URL is a normal HTTP request the host SSRs with
`Renderer.Server` for full first paint – then the session drives in-place from
there. `Navigation.firstPaintTree resolver route` is the seam the host renders
that first paint through (`None` → the host's 404), so there are no
"only-reachable-by-clicking-from-home" dead routes.

### Wiring

`LiveConnection.EnableRouting(resolver)` layers routing over the (optionally
form-validating) step – non-breaking, off until called. The pure
`Navigation.resolveNav` / `Navigation.stepWithRouting` are the directly-testable
core. (In-place swap re-baselines the session's `Tree`; on the hand-authored
`'Model` path the route should be the tree the resolver returns for that route – 
intra-page reactive state belongs *inside* a route's tree via the normal loop, or
use the bounded path.)

---

## UX + latency-masking quick wins (Phase 158)

A bundle of small, additive features – the difference between "technically
interactive" and "feels good", plus the main mitigation for the tier's one
intrinsic cost (the per-interaction round-trip). Mostly the generic shim + the
reference CSS; no new packages.

**Shim + reference CSS (QW1–QW5)** – opt-in via reserved attributes the
`fuaran-live-patch.js` shim toggles and the reference CSS styles (browser-verified):

- **QW1 – in-flight state.** The interacting node gets `data-fuaran-pending` until
  its patch frame lands; the CSS dims it + shows a progress cursor + suppresses
  re-clicks. Large perceived-responsiveness payoff for the round-trip cost.
- **QW2 – connection-state affordance.** `<html data-fuaran-disconnected>` while
  the live stream is down → a non-scary "Reconnecting…" banner (pairs with
  152 reconnect-replay + 155 durability). Pure shim + CSS.
- **QW3 – declarative debounce.** `data-fuaran-debounce="<ms>"` on an input → the
  shim batches its `input`/`change` before sending – forms-heavy apps batch
  high-frequency input with no per-app code.
- **QW4 – optimistic local echo.** `data-fuaran-optimistic` → the shim echoes
  `data-fuaran-optimistic-active` immediately on click (a bounded, safe active
  state); the server patch reconciles. Closes most of the perceived-latency gap.
- **QW5 – focus / caret / scroll preservation.** The shim captures + restores
  focus, text caret, and scroll around a `ReplaceFragment`, so a fragment swap
  doesn't drop the user's place – important for the forms / wizard class.

> Reference-CSS edits follow the workspace sync discipline: the QW1/QW2/QW4 rules
> are added to BOTH `Fuaran.UI.Renderer/content/fuaran-reference.css` and the
> `@fuaran-ui/renderer` byte-copy in the same change-set.

**F# (QW6–QW7)** – headlessly tested:

- **QW6 – `DomPatch` conformance corpus.** A named golden `TreeOp → DomPatch`
  corpus (`DomPatchCorpusTests`) locks the lowering; the `DomPatchCorpus`
  `Build.fs` target runs it as a standalone CI gate (the DomPatch analogue of
  Phase 142's `SsrParity`). Cheap guard against silent patch-lowering drift.
- **QW7 – per-interaction telemetry.** `LiveConnection.EnableTelemetry(sink)`
  records `{ node, event, op/patch/effect counts, patch bytes, rejected }` per
  interaction to an `IInteractionTelemetrySink` (a ServerDriven-local seam – the
  shipped `IFuaranTelemetrySink` records the apply-engine's vocabulary, not a
  per-interaction shape). It is the signal for *which* surfaces want WebSocket vs
  SSE. Round-trip latency is host-measured (the driver is deterministic – no
  wall-clock), so a host stamps it by wrapping the sink.

---

## The bounded tree driver – driving *generated* apps (no `'Msg`)

> **Moved in 0.25.0 — this path now ships in `Fuaran.Program.Bounded`, not
> `Fuaran.UI.ServerDriven`.** `BoundedActions`, `BoundedDriver` and `resolveTree`
> (now `Resolve.resolveTree`) live in the program domain's package, because the
> same interpreter drives *two* placements of this loop — the server session
> described below, and a browser client — and "one algebra, two placements" only
> holds if there is exactly one interpreter. Behaviour is unchanged; the section
> below describes it as it still works, at its new address. Add a
> `Fuaran.Program.Bounded` package reference and `open Fuaran.Program.Bounded`
> (plus `.BoundedDriver` for the loop). The transport core this section builds on
> — `Validation`, `Lowering`, `DomPatch`, `ClientEffect` — is untouched and stays
> in `Fuaran.UI.ServerDriven`. See `STABILITY.md`, "Recorded breaking change —
> 0.25.0".

The driver above (`Driver` / `LiveConnection`) runs a **hand-authored** Elmish
`(Model, update, view)` loop on the server. But an AI-generated app has **no
hand-authored `update` / `'Msg` / `view`** – it is an **AI-emitted, wire-decoded
`Node<obj>` tree**. `BoundedDriver` is the second driver mode for that case: the
"model" is the tree's **state store** (a
`BindingResolver.BindingSources` value), and the "update" is **applying the
bounded `Action` set against that store** – no app-specific F# code, no Fable
compile, no `'Msg` type. The bounded language *is* the update loop.

```
inbound LiveEvent
  → G1 validate (Validation.validate — reused verbatim)
  → BoundedActions.runBoundedAction  (SetState mutates the store; Navigate /
      WriteToClipboard / ReadFileBody → closure-free ClientEffects; Chain folds;
      Notify / AiTool / Dispatch / Call / CommitLocal are no-ops — each emitting
      a readable BoundedDiagnostic, Phase 212)
  → resolveTree  (re-resolve the FIXED tree's bindings against the new store)
  → TreeOpDiff.diff  →  Lowering.lower  →  DomPatches + ClientEffects
```

### Why re-resolve into the tree (the binding-blind-diff problem)

The Track-A diff compares `Node` trees via canonical JSON, and a
`Binding.State(key, default)` canonical-encodes **identically regardless of the
store value** (it encodes the key + default, not the resolved value). So diffing
the raw decoded tree before/after a `SetState` yields **no ops** – the state
change is invisible. `resolveTree` fixes this: it substitutes every resolvable
binding with `Binding.Static (resolved value)` (and every bound `TextSource` with
its resolved `Literal`) **before** the diff, so a state change shows up as a real
`Binding.Static old → new` drift the diff can see and patch. (The hand-authored
path sidesteps this by baking state into the tree in its `view`; the bounded path
has no `view`.) Coverage is the documented coarse floor (the state-reactive
Display / Input / Layout kinds generators actually use; containers recurse
generically so structure is never lost; uncovered kinds pass through and extend
in a follow-on).

### The safety boundary – bounded language ⇒ no arbitrary code server-side

**This is the invariant the whole "run generated apps server-side" direction
rests on, and it is stated *and tested*, not assumed.** AI-emitted trees are
**bounded**: the wire format cannot carry arbitrary closures. The JSON decoder
neutralises every closure slot to an inert sentinel – `Action.Call`'s `onResult`
decodes to `fun _ -> box "<closure>"`, `ReadFileBody`'s `onRead` likewise,
`Binding.Computed` to a placeholder. `BoundedActions.runBoundedAction` enforces
the other half: it **never invokes** any closure carried by an `Action` (its only
mutation is the `State` write; its only outward effects are the closure-free
`ClientEffect`s). So a server driving a *generated* tree has **no
arbitrary-code-execution surface** – proven end-to-end (author a side-effecting
`Call`, send it over the wire, drive it: the authored closure is erased at the
wire boundary and never runs).

### Resource bounds (G2) – bounded language ⇒ no arbitrary code; bounded cost ⇒ no arbitrary cost

The no-closures invariant prevents arbitrary *code*; it does not prevent
arbitrary *cost* – a generated tree can still drive an enormous `Chain` or be
pathologically large. `InteractionBudget` caps both **per interaction**:
`MaxActions` (the bounded-action cascade size – a `Chain` flattens) and
`MaxNodes` (the re-resolve + diff tree cost, the work/memory proxy). A breach
surfaces a structured `BudgetExceeded` (the session is unchanged – no hang, no
mutation; default-deny by shape). The budget is **step/size-based, not
wall-clock** (no `Stopwatch` / `Date.now`), so it is Fable-clean and
deterministic – the same tree + event sequence bounds identically and is
unit-testable headlessly. `InteractionBudget.unlimited` is the trusted
single-tenant default; `InteractionBudget.defaults` is the conservative
multi-tenant cap. **Bounded code + bounded cost = safe to run untrusted generated
apps on shared / multi-tenant infrastructure** (the property Phase 154's
Fable-free vibe-coding emission cites).

**`MaxNodes` is a COST, not a node count (Phase 790).** A `Chart` or a
`DataGrid` is a *single node* carrying its own data, so a bare node count priced
a chart of ten thousand points the same as an empty one — a bounded-looking tree
with unbounded render work behind it. The tree cost therefore weights a
data-bearing node by the payload it carries (a chart: one per point × series; a
grid: one per row × column; a map / sparkline: one per point), counting only
`Binding.Static` payloads, since a `Query` / `State` binding's size is a
property of the host's store rather than of the untrusted tree. For a tree with
no data-bearing node the cost is exactly the node count it always was. A new
data-bearing kind joins the cost function, and the existing budget then sees it
with no host-side change — which is what stops the one-node-unbounded-cost shape
recurring.

> **Status:** shipped – `BoundedActions.runBoundedAction`, `BoundedDriver`
> (`init` / `step` / `resolveTree`), `InteractionBudget`, and the safety-boundary
> + G2 property tests. The form / `Binding.Local` per-field buffer protocol on
> the bounded path inherits the floor in *Form / input / local-state policy*
> above (the flushed value arrives as the input event → `SetState`); the explicit
> buffer-seeding protocol lands with the form sample. The `BoundedConnection`
> channel glue (the bounded analogue of `LiveConnection`) is the documented
> follow-on – `step` is transport-shaped already, so it is glue, not new design.

---

## See also

- `src/Fuaran.UI.ServerDriven/README.md` – the package API + Track status.
- `src/Fuaran.UI.ServerDriven/content/fuaran-live-patch.js` – the generic shim.
- `docs/SSR.md` – server-side rendering (the first paint this tier patches onto).
- `docs/devtools-relay-server-driven.md` – why the DevTools relay does not reach
  this tier yet: the page holds no tree, relay `apply` is coherent only on the
  bounded driver, and that driver has no channel connection (`BoundedConnection`).
- `SANITIZATION.md` – the string→DOM injection-safety contract every rendered
  fragment (including the lowering's re-rendered nodes) routes through.
