# Fuaran.UI.Renderer.Web

**Render a Fuaran tree in the browser from any .NET app, with no Node toolchain.**

Author the tree in F#, C# or VB, serve its canonical wire JSON, and this package's embedded
browser renderer draws it live. One `PackageReference`, one `MapFuaranRenderer()` call, one
snippet in your page.

```fsharp
open Fuaran.UI.Renderer.Web

let app = WebApplication.CreateBuilder(args).Build()
app.MapFuaranRenderer() |> ignore          // serves /_fuaran/...
```

```fsharp
let html =
    Snippet.assetTags "/_fuaran"
    + Snippet.mount Snippet.defaults Fuaran.UI.Renderer.Theme.vocabularyFingerprint wireJson
```

## The no-Node claim, stated precisely

**Maintainers of this package run Node to produce the browser bundle. Consumers never do.**

The bundle is built once from the `@fuaran-ui/renderer` TypeScript sources, byte-copied into
this package, and **committed**. It is then embedded in the assembly. Restoring this package
brings the renderer with it — there is no `npm install`, no bundler, no `wwwroot` to populate,
and no build step that reaches the network. This is the same arrangement a Swagger-UI package
uses, for the same reason.

## What is served

`MapFuaranRenderer(prefix)` maps three `GET` routes under `prefix` (default `/_fuaran`):

| Route | What | Cache |
|---|---|---|
| `/fuaran-renderer.js` | the standalone renderer — React, the renderer and the canonical decoder in one file, exposing the `FuaranRenderer` global | `immutable`, 1 year, strong content ETag |
| `/fuaran-reference.css` | the canonical reference stylesheet | `immutable`, 1 year, strong content ETag |
| `/fingerprint.json` | what the embedded bundle **is** | `no-cache` |

The fingerprint is deliberately uncacheable: it is the drift oracle, and an oracle a proxy may
answer from last year answers a question about last year. It publishes package and version
identifiers and a content digest — no host configuration, no request data, nothing about the
trees your app renders.

`MapFuaranRenderer` returns the group's convention builder, so `.RequireAuthorization()`, a CORS
policy or an output-cache policy applies to all three at once.

## Choosing a tier

Three ways to put a Fuaran tree in front of a user. They are not ranked; they answer different
questions.

| | **Server-driven** | **Embedded renderer** (this package) | **Full Fable** |
|---|---|---|---|
| Where the tree lives | server | server, hydrated in the browser | browser |
| Client toolchain | none | none | Node + Fable |
| What crosses | rendered HTML, then patches | the tree as wire JSON, once | nothing — the tree never serialises |
| Interaction | a round trip per interaction | wire actions, host-bound | in-process, immediate |
| `Action.Dispatch` | **not available** | **not available** | **available** |
| Authoring language | any .NET | any .NET | F# only |

Take **server-driven** when the page is mostly static or the interaction is inherently a server
round trip, and you want no client-side state at all. Take **this package** when you want a live
client-side render from a C#, VB or F# host with no client toolchain. Take **full Fable** when
you want typed in-process message dispatch and are willing to author in F# and run a Node build.

## Interaction: what reaches the host, and how

**The browser raises a wire action. The host binds the behaviour.**

`Action.Dispatch of 'Msg` carries a host closure. The canonical encoder emits the case's
discriminator and drops the payload; the decoder rebuilds it as the `"<closure>"` sentinel. So a
`Dispatch` that crosses the wire arrives as an affordance that renders, fires, and does nothing.
The renderer's `dispatch` callback receives that sentinel and is a **diagnostic signal, never a
message** — this package's snippet does not wire it.

What does cross:

- **`Action.Notify(channel, payload)`** — a channel name and a JSON payload. Set
  `MountOptions.NotifyEndpoint` and the snippet POSTs `{"channel": …, "payload": …}` there.
- **`Action.Call(endpoint, into: …)`** — the wire-native round trip, writing the response into a
  state slot or a query result. Note `into:` and not `onResult`: `onResult` is a closure and does
  not survive either.
- **State-bound controls** — `open` / `activeIndex` / `value` bound to `$state`, and declarative
  op chains (`SetState`, `WriteToClipboard`, tree ops).

**Typed dispatch is obtained host-side**, by binding a handler table to the artifact's declared
action holes — checked against the artifact's signature, uniform across hosts, and needing no
per-language mechanism. This package deliberately invents nothing of its own for it.

**Encode with `CanonicalJson.encodeNodeForTransport`**, which returns `Error` naming every node
and slot whose interaction would be lost, rather than `encodeNode`, whose closure-blindness is
deliberate for the hash chain and silent here.

## The fingerprint, and the drift it catches

The embedded assets carry a sidecar recording which renderer package and version the bundle came
from, the bundle's own stamps, the reference stylesheet's class-vocabulary fingerprint at sync
time, and a digest of the copied bytes.

`Snippet.mount` compares it against this build's authoring surface and, **when
`MountOptions.Development` is set**, emits an HTML comment and a `console.warn` naming what
disagrees and the command that repairs it. Off by default: a warning that leaked internal version
state to a visitor would be the wrong way round.

Two axes are compared, separately, because they have different consequences:

- **wire profile** — the bundle decodes a different major wire version than the host emits. A
  tree it cannot decode renders as nothing at all.
- **class vocabulary** — the assets were synced against a stylesheet the renderer no longer
  matches. The page renders, and some nodes are unstyled, which looks like a design bug rather
  than a version one.

## Dependencies

`FSharp.Core` and the ASP.NET Core shared framework. Nothing else — this package serves assets and
emits HTML, and takes no reference on the tree library, the renderer or the op-stream. Your app
references those directly, at whatever versions it wants.

## Security notes

- The mount snippet inlines the tree as `<script type="application/json">`, escaping `<`, `>` and
  `&`, so a payload containing `</script` cannot close the element. The escape is
  value-preserving: the browser decodes exactly the bytes you encoded.
- The snippet emits **inline** scripts. Under a `script-src` policy, pass
  `MountOptions.Nonce`; there is no safe default, so the host says.
- Assets are served with `X-Content-Type-Options: nosniff`.

Apache-2.0.
