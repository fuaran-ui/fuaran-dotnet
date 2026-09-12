# Fuaran.UI.AiWire

The **minimal portable substrate for talking to an AI provider** — the part a Fuaran host needs in
order to build a provider request body, read the response, and hand the bytes to whatever HTTP
machinery the host already has.

It is deliberately small, and deliberately *not* an SDK. There is no client, no retry loop, no
streaming parser, no credential store and no provider registry. What ships is the four things a
provider mapping cannot be written without:

| | |
|---|---|
| `JsonValue` | A JSON value model whose objects preserve **insertion order**, with total `option`-returning accessors that never throw. |
| `JsonHost` | `serialize` — one canonical byte-stable writer, the same F# on every host. `parse` — bridged to the host engine (`System.Text.Json` on .NET, `JSON.parse` under Fable). |
| `Contract` | The provider value types: messages (text and multimodal), tool calls and results, tool definitions, token usage, the response record, capability flags, and a closed `AIProviderError` vocabulary. |
| `IHttpTransport` | A one-method egress seam over host-agnostic `HttpRequest` / `HttpResponse` records. |

`FSharp.Core` is the only dependency (plus `Fable.Core`, reached solely from the `#if FABLE_COMPILER`
arm of the parser). No `System.Net.Http`, no host APIs — so the same source compiles to a .NET server
host and to a Fable browser host, which is the whole point: one provider mapping, two hosts.

## Using it

```fsharp
open Fuaran.UI.AiWire

// Build a request body. Members serialize in the order written.
let body =
    jobj
        [ "model", jstr "some-model"
          "max_tokens", jint 1024
          "messages", jarr [ jobj [ "role", jstr "user"; "content", jstr "Hello" ] ] ]

let request = HttpRequest.post "/v1/messages" [ "x-api-key", key ] (JsonHost.serialize body)

async {
    let! response = transport.Send request

    if HttpResponse.isSuccess response then
        // Total navigation: every step returns an option, nothing throws.
        let text =
            JsonHost.parse response.Body
            |> Option.bind (JsonValue.tryField "content")
            |> Option.bind (JsonValue.tryItem 0)
            |> Option.bind (JsonValue.tryField "text")
            |> Option.bind JsonValue.asString

        return Ok text
    else
        return Error(PermanentClient(response.StatusCode, response.Body))
}
```

The host supplies `transport`. On .NET that is a wrapper over a shared `HttpClient`; in a browser it
is a wrapper over `fetch`. The mapping above does not change between them.

## Two properties worth knowing

**Object member order is load-bearing.** `JObject` carries an ordered `(string * JsonValue) list`, not
a `Map`, and `JsonHost.serialize` emits members in that order. A request body therefore serializes to
the same bytes on .NET and under Fable — which is what makes a cross-host byte-parity claim provable
rather than a matter of two JSON engines happening to agree. The writer is a single portable
implementation for exactly this reason; only the *parser* is host-bridged.

**Nothing here retries, and that is a decision.** `AIProviderError.isRetryable` classifies a failure;
it does not act on one. Whether a rate limit should be retried is the host's call and cannot be made
in a library — in a bring-your-own-key deployment an automatic retry spends the reader's own key, and
the reader is the only party who can weigh that.

## Relationship to the rest of Fuaran

This package has **no dependency on any other `Fuaran.UI.*` package**, and nothing in the Fuaran tree,
renderer or op-stream depends on it. It is a self-contained substrate a host may adopt on its own.

It deliberately does not reuse `Fuaran.Core.Wire.JVal`. That model has no null case — provider bodies
use one — and splits integers from floats where JSON has a single numeric type. The two are not
interchangeable, and substituting one for the other would change round-tripping at the provider
boundary.

## Licence

Apache-2.0. This is a modified copy of the author's own Apache-2.0 wire layer, republished under this
name (Apache-2.0 §4(b): modified copy, 2026-09-12). A fix made here is a fix made to this copy.
