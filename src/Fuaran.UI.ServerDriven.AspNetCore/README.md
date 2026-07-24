# Fuaran.UI.ServerDriven.AspNetCore

**Backend 1 (the v1 default)** for `Fuaran.UI.ServerDriven` — **SSE-push +
POST-receive** over ASP.NET. The thin transport glue around the tested
server-driven core: an SSE `IFuaranLiveChannel`, the POST inbound parser, the
connection registry, and the endpoint wiring.

## Wiring

```fsharp
open Fuaran.UI.ServerDriven.Driver
open Fuaran.UI.ServerDriven.AspNetCore.Endpoints

// In your ASP.NET app, after building `app`:
let makeSession () =
    Driver.init
        (DriverServices.create renderFragment)   // renderFragment = Renderer.Server.Render.render sources
        update
        view
        initialModel

mapFuaranLive app (defaultConfig makeSession) |> ignore
```

This maps:

- `GET /live/stream` — opens the SSE stream. Assigns a `connId`, sets the
  correlation cookie, builds a fresh `LiveSession` + `SseChannel` +
  `LiveConnection`, registers it, and drains the channel's frame queue to the
  long-lived response (`FrameWire.encodeSse` + flush) until the client
  disconnects. `Last-Event-ID` drives `LiveConnection.Resync` (reconnect replay).
- `POST /live/event` — verifies the signed `connId` token against the request
  principal (`ConnToken.verify`; **401** on a forged / unbound cookie),
  guard-parses the body (`Inbound.tryParseLiveEvent`; **400** on a malformed
  body), and routes it to the registered connection (→ G1 trust boundary →
  driver → frames pushed back down the GET stream).

> **Auth floor (Phase 211).** The host **must layer authentication in front of
> `/live/*`**. The `connId` is bound, not bearer: the cookie carries a signed,
> principal-bound token (`ConnToken`), so a forged / cross-principal cookie is
> rejected 401 rather than routed. `ResolvePrincipal` (default
> `ctx.User.Identity.Name`) feeds both the binding and the durability attribution
> (`ConfigureConnection` receives the resolved principal — thread it into
> `conn.EnableDurability(store, userId = principal)`). See `docs/SERVER_DRIVEN.md`.

Serve the generic shim (`Fuaran.UI.ServerDriven`'s `content/fuaran-live-patch.js`)
as a static asset and point it at the two endpoints:

```html
<script src="/fuaran-live-patch.js"
        data-fuaran-live-stream="/live/stream"
        data-fuaran-live-send="/live/event"></script>
```

## Why SSE+POST is the default

Both backends are freshly written against the same `IFuaranLiveChannel` seam, so
the default is **not** maturity. SSE+POST wins on four durable axes: infra
traversal (no `Upgrade`/101 handshake to be blocked by enterprise proxies),
per-event HTTP governance for free (every client event is an ordinary request
through the whole auth/rate-limit/audit pipeline), browser-native reconnect that
maps 1:1 onto the journal via `Last-Event-ID`, and a smaller debuggable surface.
The WebSocket backend (`Fuaran.UI.ServerDriven.WebSocket`) is the drop-in for
*measured* high-frequency interaction. See `fuaran/docs/SERVER_DRIVEN.md`.

## Testability

The shared `Inbound.tryParseLiveEvent` (JSON → `LiveEvent option`), `ConnToken`
(the connId authz binding), and `ConnectionRegistry` are pure / deterministic and
unit-tested headlessly. The SSE streaming handler + the POST endpoint are thin
ASP.NET glue, browser-verified via `samples/server-driven`.

**No platform-SDK dependency** — the SSE framing is self-contained (`FrameWire`), not
referenced. Apache-2.0 licensed — see the repo [LICENSE](../../LICENSE).
