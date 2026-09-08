# Fuaran.UI.ServerDriven

The transport-agnostic core for **server-driven interactivity** over a Fuaran tree — the third client tier (alongside the Fable client renderer and the Phase 143 `hydrateRoot` hydration), HTMX / Phoenix-LiveView / Blazor-Server-shaped: keep the Elmish `update` loop on the **server**, ship one tiny generic JS shim to the browser, and patch the DOM in place.

The loop: browser event on a `[data-fuaran-node-id]` element → server runs `update` → re-renders the tree → **diffs old→new into a `TreeOp` list** (`Fuaran.UI.OpStream.Replay.TreeOpDiff`) → **lowers each op to a `DomPatch`** (rendering HTML fragments via `Fuaran.UI.Renderer.Server` for structure-adding ops) → sends the patch → the shim applies it (targeted, no full re-render, no flash).

## What's here

- **`DomPatch`** — the lowered, closure-free, browser-applyable patch vocabulary (8 primitives: `SetAttr` / `RemoveAttr` / `SetText` / `ReplaceFragment` / `InsertFragment` / `RemoveNode` / `ReorderChildren` / `MoveNode`). The shim's entire instruction set. Tagged-object camelCase JSON.
- **`ClientEffect`** — the parallel channel for client-only effects the server decides but the shim performs (`WriteToClipboard` / `Navigate` / `Focus` / `Download` / `ReadFileBody`), because they are inherently browser-side and have no DOM-mutation form.
- **`Lowering`** — `TreeOp → DomPatch` (`Lowering.lower : renderFragment -> newTree -> TreeOp list -> DomPatch list`). Structural ops lower directly (`RemoveNode` / `ReorderChildren` / `MoveNode` / `InsertChild`→`InsertFragment`); content ops re-render the changed node (looked up in the post-apply tree) into a targeted `ReplaceFragment`; `Batch` flattens. **The HTML renderer is injected** (`renderFragment : Node<'Msg> -> string`) rather than a hard `Renderer.Server` dependency — keeps the core Fable-clean + dependency-light and dodges the `Node<obj>` cast; the host wires `Render.render`.
- **`content/fuaran-live-patch.js`** — the **generic browser shim** (shipped as package content under `content/`). App-agnostic, framework-free vanilla JS (~a few KB): event delegation on `[data-fuaran-node-id]`, a transport adapter (`connect`/`send` — the client mirror of `IFuaranLiveChannel`, default SSE-push + POST-receive), the `DomPatch` applier (addresses by node id; `MoveNode` / `ReorderChildren` relocate the *live* element, identity-preserving), and the `ClientEffect` performer. Auto-starts from `<script src="fuaran-live-patch.js" data-fuaran-live-stream="/live/stream" data-fuaran-live-send="/live/event">`, or call `FuaranLive.start(config, adapterFactory?)` explicitly (a WebSocket adapter is a drop-in — only the adapter object changes; the patch/effect/delegation core is transport-identical). `start()` is **restartable** — hosts that swap the live tree per page state call it repeatedly: each call closes the previous stream and re-points the once-wired document-level delegation at the new transport (no leaked `EventSource`, no duplicate sends). Click payloads bridge layout interactivity server-side: a tab-header click carries `payload.index` (from the server renderer's `data-tab-index`), a disclosure summary click carries `payload.open` (from the `<details>` state). Browser-verified via `samples/server-driven` (tabs + disclosure + repeated `start()`); no headless unit tests. It also publishes two read-only members for an inspecting DevTools relay peer — `treeSource` (the string `"upstream"`: this page holds no tree) and `isConnected()` (whether the stream is up right now, never a promise that a dispatched request will be answered). Both are FACTS such a peer previously had to infer, from this global merely existing and from the reconnecting-banner styling attribute; publishing them turns an inference into a contract. Nothing tree-shaped is exposed, deliberately: a tree reconstructed from the patches this shim has applied would carry the shim's idea of the tree rather than the host's, and no client could tell that it had received one.

### Live `Transform` sources — the per-session store (opt-in)

A `TransformSource.Live` binding runs a pipeline over a state-bound table and is read again on every
write. `LiveTransformStore` evaluates it incrementally — prime once, then advance the primed state
against what the edit changed — and the renderer consults one through `BindingSources.LiveTransforms`.
Somebody has to construct it and hold it for the life of a session; `LiveTransform.initSession` is
that composition:

```fsharp
let session, store =
    LiveTransform.initSession
        (LiveTransformOptions.identifiedBy "id")   // the host's options
        (fun () -> mySources)                      // read once per render
        Render.render                              // the host renderer, as a function of its sources
        DriverServices.createPermissive            // how the host builds its services
        update view initialModel
```

| Option | What it declares | Default |
|---|---|---|
| `Capacity` | How many live-`Transform` **sites** one session keeps primed at a time. At the bound the least recently used site is evicted, which is correctness-neutral — the next evaluation of an evicted site re-primes and answers the same table, having paid for it. | `64` (`LiveTransformDefaults.Capacity`) |
| `IdentityColumn` | The column whose value identifies a row — the key an edit stream addresses rows by. Nothing in a rendered tree declares one, so the host declares it here, once, for every site the session serves. | `""` — *none declared*. Correct on every site and restricting on none: a store with no row identity evaluates through the seam's reference path and answers exactly what a full evaluation answers, losing only the saving. |

`LiveTransformOptions.defaults` is both of the above; `LiveTransformOptions.identifiedBy "<column>"` is
the one-line opt-in for a host that knows its key column.

**The store is the session's.** `initSession` mints one per call, reachable only from that session's own
render closure and the handle it returns — there is no registry and no shared default, so two sessions
cannot see each other's primed results. Drop the session and the store goes with it. It holds no
unmanaged resource, so it is not `IDisposable`; an explicit session end that wants the primed tables
released early calls `store.Clear()`, which is correctness-neutral. Not opting in at all is today's path
exactly: every render evaluates the pipeline in full.


## Roadmap (Phase 152)

- ✅ Track A — the `TreeOp`-emitting diff (`Fuaran.UI.OpStream.Replay.TreeOpDiff`).
- **Track B (in progress)** — ✅ the `DomPatch` / `ClientEffect` wire vocabularies (this package), ✅ the `TreeOp → DomPatch` lowering (renderer injected), ✅ the generic JS shim. **Remaining:** the granular `SetText` / `SetAttr` lowering follow-on (currently content ops re-render the node via `ReplaceFragment` — correct + targeted, just not field-level).
- **Track C (in progress)** — ✅ the **G1 inbound trust boundary** (`Validation.fs` — the non-negotiable default-deny gate: node-exists / event-legitimate-for-kind / payload-in-bounds / dispatch-policy-gated, mirroring the client `runAction` gate server-side), ✅ the **per-connection driver** (`Driver.fs` — `LiveSession` + `step`: validate → interpret → `update` → re-view → diff → lower → `DomPatch`/`ClientEffect`, with the server-closure win + the server-executable/client-only split). **Remaining:** the explicit per-field `Binding.Local` / `CommitLocal` form-buffer protocol (the floor — client-buffered, server-sees-the-flush — already holds).
- **Track D ✅** — the `IFuaranLiveChannel` transport seam + `Frame` + `InMemoryChannel` + `LiveConnection` (`Channel.fs`), transport-agnostic reconnect replay (`LiveConnection.Resync`), the SSE frame wire encoding (`FrameWire.fs`), and **both backends**: `Fuaran.UI.ServerDriven.AspNetCore` (SSE+POST, the v1 default — verified end-to-end via the sample) and `Fuaran.UI.ServerDriven.WebSocket` (the lower-latency drop-in; its structural identity to the SSE backend proves the seam is transport-neutral).
- **Track E ✅** — `fuaran-dotnet/docs/SERVER_DRIVEN.md` (architecture + transport analysis + the per-arm tables) and `samples/server-driven` (an SSR counter made live via the shim + the SSE backend, no client bundle).

**No platform-SDK dependency** appears anywhere here — the SSE framing is implemented here, not depended on.

Apache-2.0 licensed — see the repo [LICENSE](../../LICENSE).
