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

## Wiring

```fsharp
open Fuaran.UI.ServerDriven.Driver
open Fuaran.UI.ServerDriven.WebSocket.Endpoints

app.UseWebSockets() |> ignore   // required

let makeSession () =
    Driver.init (DriverServices.create renderFragment) update view initialModel

mapFuaranLiveWebSocket app (defaultWsConfig makeSession)
```

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
