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
is a thin, OSS-safe types layer over it. The wire payload is the same canonical
JSON both the F# and TypeScript renderers consume — one host-neutral contract, a
second host client.

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

Both credentials travel as **headers** — the access token as
`Authorization: Bearer`, the provider key as `X-Fuaran-Provider-Key` — and never
in the request body. A body is the thing most likely to be logged wholesale by
an intermediary; a header is the thing most likely to be redacted by one. The
endpoint enforces it: a body carrying `ByokKey` or `AccessToken` is refused
`400 SECRETS_IN_BODY` and the value is not read, so if you see that code, treat
the key you just sent as exposed and rotate it. `Wire.toWireBody` has no
credential parameter, so this client cannot produce such a body.

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
- `SendBearerHeader` (default `true`) sends the `Authorization` header. That is
  the endpoint's ONLY auth channel — a body-carried token is refused — so set it
  `false` only when pointing at a proxy that authenticates some other way, and
  supply that header via `Headers`.

## The endpoint must be https, or loopback, or opted out of

Because both credentials ride headers on every call, a plaintext hop hands them
to anyone on the path. `http://127.0.0.1` and `http://localhost` are admitted
(that is where the offline mock and a local proxy live, and no packet leaves the
machine); a relative path like `/api/fuaran` is admitted (its security is the
page's own origin); anything else plaintext is refused as `INSECURE_ENDPOINT`
before the request is built, unless you set `AllowInsecureEndpoint = true`.
`EndpointPolicy.isSecure` applies the same rule to a URL you are about to
configure, so a host can check at startup rather than on the first turn.

## Failures the CLIENT reports, as distinct from the endpoint's

`RecoverableError.Code` carries the endpoint's own code whenever there is one
(`ACCESS_DENIED`, `APPLY_REJECTED`, `SECRETS_IN_BODY`, `MISSING_PROVIDER_KEY`,
…). Three codes are this client's own, on `ClientCode`:

| Code | Means |
|---|---|
| `NETWORK` | the call did not complete — the transport threw, the `Timeout` elapsed, or a `CancellationToken` fired. The message is FIXED: an exception string can quote a URL, a header, or a proxy's internal hostname, and this result is routinely rendered straight into a browser. The detail belongs in your host's log. |
| `MALFORMED_RESPONSE` | a 200 with no usable tree. Not a success — accepting it would leave the session holding `""` and silently repairing nothing on every later turn. |
| `INSECURE_ENDPOINT` | the endpoint is plaintext and not loopback; see above. |

`Generate(args)` never raises for an endpoint-level outcome. `Timeout` bounds one
turn; `Generate(args, cancellationToken)` reports a cancelled call the same way,
because from the caller's side it is a call that did not complete.

## More than the tree

`GenerateDetailed(args)` returns the result plus what the deployment reported:
`OpsApplied` (a count — the endpoint returns how much changed, not the op list),
`Provider` (which allowlisted provider it chose), `ServedModel` (what the
provider's own reply said actually answered; `None` means **unreported**,
deliberately not the model the deployment asked for) and the grounding
`Snapshot` state. `Provider` on the config selects an allowlisted provider via
`X-Fuaran-Provider`.

## Testing without a live endpoint

The transport is a seam. Implement `IFuaranTransport` to return a scripted
`HttpResult` (or throw to exercise the transport-failure path) and pass it as
`Transport = Some myMock` in the config — no network required.

## Surface version

The client speaks the deployed endpoint's wire: a camelCase body
(`prompt` / `currentTree` / the corpus flags / `interactionId`), secrets in
headers, and a reply of `{version, tree, opsApplied, provider, servedModel?,
snapshot}` at 200 with one `{error:{code,message,stage?}}` envelope at every
refusal. Reads stay tolerant of the retired PascalCase spelling — a proxy or the
offline mock in front of the endpoint may not have moved — but nothing writes it.

`SurfaceContract.Version` is `1.2.0`, in lockstep with the TypeScript client, and
names the request/response SHAPE this client understands rather than the newest
surface. Later minor surface bumps have only added optional reply fields, so the
shape is stable across them; `TurnResult.Produced` echoes whichever version the
live surface stamps, and `SurfaceContract.isVersionCompatible` compares only the
major.
