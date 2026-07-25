# Fuaran.UI.Client

A thin, typed **F#/.NET client** over the Fuaran generation endpoint — the F#
mirror of the TypeScript `@fuaran-ui/client`. It gives .NET apps (server-side,
desktop, or any host) the same *call → render → remember-the-tree* ergonomics:

- a typed `Generate` over the generation endpoint returning a three-way result
  (`Produced` / `AccessDenied` / `TurnFailed`),
- a **session / turn-loop** helper so each subsequent prompt is a cheap **repair
  diff** instead of a from-scratch regeneration (the token-saving ergonomic),
- **decode glue** to a typed `Node<obj>` you hand to `Fuaran.UI.Renderer`.

The endpoint URL and the paid access token are the commercial gate; this client
is a thin, OSS-safe HTTPS + types layer over it. The wire payload is the same
canonical JSON both the F# and TypeScript renderers consume — one host-neutral
contract, a second host client.

## Quickstart (~10 lines)

```fsharp
open Fuaran.UI.Client

// 1. Construct a client against the endpoint (or your server-proxy path).
let client =
    FuaranClient(
        { FuaranClientConfig.create "https://your-endpoint.example/api/fuaran" with
            AccessToken = Some accessToken
            ProviderKey = Some byokKey } // memory-only; never bundle a key into shipped code
    )

// 2. Generate. The result is typed — branch on the case.
async {
    match! client.Generate(GenerateArgs.prompt "a sign-up form with email + password") with
    | TurnResult.Produced (treeJson, _ops, _version) ->
        match Render.decodeTreeJson treeJson with
        | Ok tree -> () // hand `tree` to Fuaran.UI.Renderer in your Fable host — see below
        | Error err -> eprintfn "decode failed: %s" err.Message
    | TurnResult.AccessDenied reason -> eprintfn "access denied: %s" reason
    | TurnResult.TurnFailed err -> eprintfn "turn failed at %s: %s" (TurnStage.label err.Stage) err.Message
}
```

## The turn loop (cheap repair diffs)

`FuaranSession` carries the current tree forward, so every prompt after the first
is a repair against the tree the last turn produced — not a full regeneration:

```fsharp
let session = FuaranSession(client)

async {
    let! _ = session.Next "a dashboard with a revenue chart"      // fresh generation
    let! _ = session.Next "make the chart a bar chart"            // repair diff — sends the held tree
    let! _ = session.Next "add a date-range filter"               // repair diff
    // session.CurrentTreeJson holds the latest produced tree
}
```

On `Produced` the session advances to the new tree; on `AccessDenied` /
`TurnFailed` the held tree is left unchanged, so you can retry the same repair.
`session.Reset()` forgets the tree — the next turn is a fresh generation again.

## Rendering the returned tree

`Render.decodeTreeJson` (this package, plain .NET) decodes a produced tree's
canonical wire JSON into a typed `Node<obj>`. The **render** call itself lives in
`Fuaran.UI.Renderer`, which is a browser/Fable renderer — so in a Fable host it
is one further line:

```fsharp
// In your Fable/Elmish host, after decoding:
open Fuaran.UI.Renderer
Render.renderWithSources bindingSources dispatch tree
```

This mirrors how the TypeScript client splits its Node-safe core from its
`./render` subpath: decode is host-neutral; the render step belongs to whichever
renderer your host runs. For a **browser BYOK** integration, the TypeScript
`@fuaran-ui/client` is the first-class in-browser client.

## Key / token guidance

- **Never bundle a BYOK provider key into shipped client code.** It is
  memory-only — supply it per session from a secure input, and prefer the
  **server-proxied** pattern for anything user-facing.
- **Server-side / desktop (.NET host):** hold the access token + BYOK key in the
  process and construct the client with them. This is the natural fit for this
  package.
- **Server-proxied (recommended for browsers):** point `Endpoint` at your own
  same-origin proxy path (e.g. `/api/fuaran`) and leave `AccessToken` /
  `ProviderKey` unset — your proxy injects them server-side, so no secret ever
  reaches the browser. Add a proxy auth header via `Headers` if needed.
- `SendBearerHeader` (default `true`) also sends the access token as
  `Authorization: Bearer <token>`; set it `false` for a deployment that reads the
  token from the body only.

## Testing without a live endpoint

The transport is a seam. Implement `IFuaranTransport` to return a scripted
`HttpResult` (or throw to exercise the transport-failure path) and pass it as
`Transport = Some myMock` in the config — no network required.

## Surface version

The client is built against the additive corpus-flag request/response shape
(surface `1.2.0`, in lockstep with the TypeScript client). Later minor surface
bumps only add server-side usage fields that never cross the client boundary, so
the shape is stable across them; `TurnResult.Produced` echoes whichever version
the live surface stamps, and `SurfaceContract.isVersionCompatible` compares only
the major.
