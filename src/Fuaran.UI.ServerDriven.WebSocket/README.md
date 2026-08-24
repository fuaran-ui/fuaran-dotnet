# Fuaran.UI.ServerDriven.WebSocket

**Backend 2** for `Fuaran.UI.ServerDriven` — one **bidirectional WebSocket**
channel over ASP.NET. The lower-latency transport for *measured* high-frequency
interaction (per-keystroke, drag, pointer-move, live cursors).

This is a **first-class second backend**, not a placeholder. Its value is twofold:
the latency win above, and — structurally — it differs from the SSE+POST backend
*only* in its channel + endpoint glue (`WsChannel` mirrors `SseChannel`; the
driver / diff / lowering core is untouched). That structural identity is the
architectural-integrity check that `IFuaranLiveChannel` is genuinely
transport-neutral and not accidentally SSE-shaped.

That identity is now asserted rather than asserted-about. The two backends share
one connection-token implementation and one inbound budget, both in the
transport-agnostic core, and a transport-parity test pins their configs against
each other — because for a while this README's neighbouring config comment
claimed a security parity the code did not have, and a claim no test reads is
just a sentence. Neither package references the other; what they share, they
share through the core.

## Wiring

```fsharp
open Fuaran.UI.ServerDriven.Driver
open Fuaran.UI.ServerDriven.WebSocket.Endpoints

app.UseWebSockets() |> ignore   // required

let makeSession () =
    Driver.init (DriverServices.create renderFragment) update view initialModel

mapFuaranLiveWebSocket app (defaultWsConfig makeSession)
```

## Connecting: mint, then upgrade

`mapFuaranLiveWebSocket` maps **two** endpoints. `GET /live/ws-token` resolves the
request principal, mints a connection token bound to it, and sets it as a
hardened correlation cookie (`HttpOnly` + `Secure` + `SameSite=Strict`).
`GET /live/ws` then upgrades **only** if that cookie verifies against the
principal on the upgrade request; anything else is a `401` and no socket is
accepted. So a client fetches the token first, with credentials, and then
connects:

```js
await fetch("/live/ws-token", { credentials: "same-origin" })
const ws = new WebSocket(`wss://${location.host}/live/ws`)
```

The order matters and the split is not ceremony. A WebSocket upgrade is a single
request that both opens and authorises the session, so there is no second
request to gate the way the SSE backend gates its `POST` — the token has to
exist before the handshake or there is nothing to check. Two endpoints give the
two transports the same shape: one mints the principal-bound token, the other
refuses anything not carrying it.

`SameSite=Strict` is doing specific work here. A browser applies no same-origin
policy to a WebSocket handshake, so an attacker's page can open a socket to your
server with the victim's cookies attached — cross-site WebSocket hijacking. A
Strict cookie is not sent on that handshake, so the upgrade carries no token and
is refused.

**The host must still layer authentication in front of these paths.** The
principal resolver reflects whatever identity that authentication established;
with none wired, every visitor resolves to the empty principal and the token
closes forgeability but not authorisation.

Inbound messages are capped (1 MB by default, `MaxMessageBytes`), because the
fragment accumulator only flushes at `EndOfMessage` and an endless fragment
stream would otherwise grow server memory without bound. Exceeding the budget
closes the socket with `MessageTooBig` (1009). The budget itself lives in
`Fuaran.UI.ServerDriven.LiveLimits`, shared with the SSE backend.

`GET /live/ws` upgrades to a WebSocket; the same socket carries outbound frames
(`FrameWire.encodeJson` — raw JSON, no SSE `id:`/`event:` framing) and inbound
events (parsed → `LiveConnection.Handle` → G1 → driver → frames back). The
client uses the shim's WebSocket transport adapter (a Track-B drop-in — only the
`connect`/`send` adapter changes; the patch/effect/delegation core is identical).

## SSE+POST vs WebSocket

Default to **SSE+POST** (`Fuaran.UI.ServerDriven.AspNetCore`): it traverses infra
natively, gets per-event HTTP governance for free, and reconnects via
`Last-Event-ID`. Reach for **WebSocket** when a *measured* interaction pattern
needs the lower per-message latency. The `IFuaranLiveChannel` seam makes that a
swap, not a rewrite. WS owns its own reconnect + resequencing client-side (no
EventSource freebie); the server half is `Frame.Seq` + `LiveConnection.Resync`.
See `fuaran-dotnet/docs/SERVER_DRIVEN.md`.

**No platform-SDK dependency.** Apache-2.0 licensed — see the repo [LICENSE](../../LICENSE).
