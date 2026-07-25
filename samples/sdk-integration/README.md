# SDK integration sample — Elmish/Fable + ASP.NET (server-proxied BYOK)

The F# half of the framework adapters: a worked **server-proxied BYOK**
integration. The browser holds no secret; an ASP.NET route does, and runs the
turn with `Fuaran.UI.Client`.

```
sdk-integration/
├── server/   ASP.NET host — POST /api/fuaran injects the credentials (Fuaran.UI.Client)
├── client/   Fable/Elmish app — Turn.fs (portable core) + App.fs (browser edge)
└── tests/    Expecto — the turn-loop and the proxy route
```

## Run it

Offline, with no endpoint and no token:

```bash
npx @fuaran-ui/mock                                              # one shell
dotnet run --project samples/sdk-integration/server              # another
```

Then `http://localhost:14040`, and:

```bash
curl -X POST http://localhost:14040/api/fuaran \
  -H 'content-type: application/json' \
  -d '{"Prompt":"a metric strip showing revenue"}'
```

To go live, set the three environment variables and restart — nothing in the
code changes:

```
FUARAN_ENDPOINT       the generation endpoint URL
FUARAN_ACCESS_TOKEN   the paid access token
FUARAN_PROVIDER_KEY   the BYOK provider key
```

## The server-proxied pattern (why the split is what it is)

`Fuaran.UI.Client` is a **plain .NET** tier — it uses `System.Net.Http` and is
deliberately not source-packed for Fable. That is not a limitation to work
around; it is the shape of the secure pattern:

- **Server** (`server/Proxy.fs`) holds the credentials and runs the turn with
  `Fuaran.UI.Client`. The request body's credential fields are **ignored, never
  merged** — a caller cannot supply, override, or probe the server's credentials.
  The only things it controls are the prompt and the tree being repaired.
- **Browser** (`client/App.fs`) speaks the wire contract directly: `fetch` to the
  proxy, `JsonDecode.decodeNodeObj`, `Render.renderWithSources`. It references no
  client SDK and carries no secret.

## The Elmish adapter

`client/Turn.fs` is the reusable piece and is **portable** — no Fable, no Elmish,
no HTTP. `update` is pure, returning the next model plus an optional
`TurnRequest` the host runs:

```fsharp
let update (msg: Msg) (model: Model) : Model * TurnRequest option
```

Two behaviours carry the whole ergonomic:

- **`Submit` carries the held tree**, so the first prompt is a fresh generation
  and every prompt after it is a cheap **repair diff**.
- **A failed turn leaves the held tree untouched**, so the last good UI keeps
  rendering and the same repair can be retried.

`client/App.fs` is the thin browser edge that supplies the effect and maps the
request onto an Elmish `Cmd`:

```fsharp
let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    let next, request = Turn.update msg model
    match request with
    | None -> next, Cmd.none
    | Some turn -> next, Cmd.OfPromise.either runTurn turn id (fun ex -> TurnFailed ex.Message)
```

That split is what makes the turn-loop unit-testable on .NET while the browser
half stays a few lines.

## Tests

```bash
dotnet run --project samples/sdk-integration/tests
```

- **`TurnTests`** drives the portable core: fresh-then-repair, tree advance, the
  held tree surviving a failure, empty-prompt and in-flight guards, reset.
- **`ProxyTests`** drives the route over the client's transport seam: the prompt
  and repair tree reaching the endpoint, **client-supplied credentials being
  ignored**, no credential echoed back, and the 200/401/422 mapping.

The browser half is verified by transpilation (`dotnet fable
samples/sdk-integration/client`) — the check that catches server-only code
leaking into a client bundle.

## React equivalent

The TypeScript/React adapter of the same loop is `@fuaran-ui/react`
(`useFuaranGenerate`), which owns the tree as React state.
