# Migration — 0.14.0: the dispatch gate fails closed (Phase 782)

## Symptom

You upgraded to `Fuaran.UI.Renderer` 0.14.0 and **your actions stopped happening**. Buttons render,
clicks fire, nothing occurs. Your console (or whatever `IFuaranRuntime.Warn` is wired to) carries:

```
[Fuaran] dispatch denied by policy gate: Call(/api/reports)
[Fuaran] dispatch denied by policy gate: SetState(theme)
```

That is the change working. `IFuaranRuntime.CanDispatch` used to return `true` in every shipped
runtime; it now returns `false`. The published posture was always "denied by default, permitted only
through an explicit allow-list", and until 0.14.0 the shipped default said the opposite.

## The fix — pick one

### 1. Write the allow-list you always meant to have (preferred)

```fsharp
type AppRuntime() =
    inherit /* your existing base, or implement IFuaranRuntime directly */

    // Whatever your app actually needs, and nothing else.
    member _.CanDispatch(action: ActionDescriptor) : bool =
        match action with
        | ActionDescriptor.Call endpoint -> endpoint.StartsWith "/api/"
        | ActionDescriptor.Navigate route -> route.StartsWith "/"
        | ActionDescriptor.SetState key -> key.StartsWith "ui."
        | ActionDescriptor.AiTool _ -> false
        | _ -> false
```

The descriptor set is **closed** and every case carries what you need to decide on:

| Descriptor | Carries | Gates |
|---|---|---|
| `Call of endpoint` | the endpoint string | outbound HTTP / Remoting |
| `Navigate of route` | the **sanitised** route | host router navigation |
| `AiTool of toolName` | the tool name | AI-tool + `Action.Invoke` dispatch |
| `ReadFileBody of fileId` | the file id | reading a selected file's bytes |
| `ApplyTreeOp of summary` | the raw op JSON | in-page `window.__fuaran` mutation |
| `Notify of channel` | the channel name | publication onto a host channel |
| `SetState of key` | the state key | a State-channel write |
| `WriteToClipboard` | *(nothing — the text is user data)* | clipboard writes |
| `CommitLocal of nodeId` | the node id | the `Binding.Local` commit event |

The last four are new in 0.14.0. They previously reached their substrates with **no gate
consultation at all**, so a host with a perfect deny-all policy still could not refuse a decoded
tree's `SetState`.

### 2. Name the permissive opt-in (the ramp, not the destination)

| You had | You now write |
|---|---|
| `Runtime.diagnostic` | `Runtime.permissive` |
| `MutableRuntime()` | `MutableRuntime.Permissive()` |
| `BrowserRuntime.create ()` | `BrowserRuntime.createPermissive ()` |
| `BrowserRuntime.createWithLayoutObserver obs` | `BrowserRuntime.createPermissiveWithLayoutObserver obs` |
| `DriverServices.create render` | `DriverServices.createPermissive render` |
| `BoundedServices.create render` | `BoundedServices.createPermissive render` |

Nothing else changes. These are the pre-0.14.0 runtimes exactly.

**Why a name and not a flag.** `grep -r permissive` over your codebase now enumerates every place
the old behaviour is in force. A boolean argument, or an unchanged default, would have left that
question unanswerable — which is how the estate arrived at a documented default-deny gate that
allowed everything in every shipped runtime.

## Two other behaviour changes in the same version

### `Action.Navigate` is sanitised before it reaches your router

The route passes `Sanitize.sanitizeUrl` on the action path — client renderer and both server-driven
interpreters — before the gate sees it and before `IFuaranRuntime.Navigate` (or
`ClientEffect.Navigate`) is reached. `javascript:` / `vbscript:` / `file:` / unknown schemes and
protocol-relative URLs (`//host`, `\\host`, `/\host`, `\/host`) are refused: **no navigation happens
and no effect is emitted**, and a diagnostic is recorded.

The check is on the canonical decoded field, so the wire's `route` / `href` / `url` / `to` aliases
are all covered — there is no spelling that reaches the router around it.

If you were relying on a custom scheme (`myapp:`, `tel:`, …) reaching your router from a tree, it
now will not. Add it to `Sanitize`'s allowed-scheme set, or handle it through a host capability
rather than a tree-declared route.

### State keys under `host.` are unaddressable from a tree

```fsharp
Fuaran.UI.Renderer.StateKeys.HostReservedPrefix // = "host."
Fuaran.UI.Renderer.StateKeys.isHostReserved "host.session" // true
```

Every tree-originated State write refuses a key under that prefix and records the refusal:
`Action.SetState`, a covered control's write-back default, a declarative `Call … into State` target,
and the bounded server-driven interpreter. It holds **even when your gate allows everything** — it is
a namespace, not a policy.

**What to do:** rename any State slot you own and do not want a rendered tree to reach so that it
starts with `host.`. Host code writing the store directly (`StateStore.set`, your own
`BindingSources.State` seed) is unrestricted, as it always was — the restriction is on the tree side
only.

**What this does not do:** it does not sandbox tree writes into their own namespace. A slot named
`theme` is still writable by any tree that renders, because the declarative write-back loop depends
on tree writes and tree reads naming the same key. If a slot matters, give it the prefix.

## Rollback

Pin `0.13.0`. Nothing in 0.14.0 changes the wire format, so a tree emitted against either version
decodes on the other, and no corpus fixture moved.

## Verification

1. Build. Nothing should fail to compile — this break is behavioural, not signature-level, unless you
   `match` exhaustively over `ActionDescriptor` (four new cases → `FS0025`, an error under
   `TreatWarningsAsErrors`).
2. Run your app and read the `Warn` channel. Every `dispatch denied by policy gate: …` line names a
   capability your app uses and your policy has not granted. That list *is* your allow-list.
3. Confirm the deny is real rather than assumed: point your policy at `fun _ -> false` and check the
   same actions stop. A gate that agrees with what you expected is the least-examined kind of
   evidence.
