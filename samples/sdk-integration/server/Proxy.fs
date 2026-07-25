// The server-proxied BYOK route.
//
// This is the pattern any browser app should use: the client posts a bare
// prompt to YOUR same-origin route; the server holds the access token + BYOK
// key (from environment, never the repo) and runs the turn with
// `Fuaran.UI.Client`. No secret ever reaches the browser bundle.
//
// Load-bearing rule: the request body's credential fields are IGNORED, not
// merged. A client cannot supply, override, or probe the server's credentials —
// the only thing it controls is the prompt and the tree being repaired.
//
// `handle` is a pure-ish function of (client, requestBody) so it can be driven
// in a test against the local mock endpoint with no live credentials.

module Fuaran.Sample.SdkIntegration.Server.Proxy

open System.Text
open System.Text.Json

open Fuaran.UI.Client

/// What the browser is allowed to ask for: a prompt, and optionally the tree it
/// is editing. Everything else on the wire is the server's business.
type ClientRequest =
    { Prompt: string
      CurrentTreeJson: string option }

[<RequireQualifiedAccess>]
module ClientRequest =

    /// Read the two client-controlled fields, tolerant of PascalCase or
    /// camelCase. Credential fields present in the body are deliberately not
    /// read — see the module header.
    let parse (bodyJson: string) : ClientRequest =
        let pick (root: JsonElement) (pascal: string) (camel: string) =
            let tryGet (name: string) =
                match root.TryGetProperty name with
                | true, v when v.ValueKind = JsonValueKind.String ->
                    match v.GetString() with
                    | null -> None
                    | s -> Some s
                | _ -> None

            match tryGet pascal with
            | Some s -> Some s
            | None -> tryGet camel

        if System.String.IsNullOrWhiteSpace bodyJson then
            { Prompt = ""; CurrentTreeJson = None }
        else
            try
                let root = JsonDocument.Parse(bodyJson).RootElement

                if root.ValueKind <> JsonValueKind.Object then
                    { Prompt = ""; CurrentTreeJson = None }
                else
                    { Prompt = pick root "Prompt" "prompt" |> Option.defaultValue ""
                      CurrentTreeJson = pick root "CurrentTreeJson" "currentTreeJson" }
            with _ ->
                { Prompt = ""; CurrentTreeJson = None }

/// Serialise a `TurnResult` back to the browser in the endpoint's own wire
/// shape, so the client decodes one contract whether it is talking to the
/// proxy or (in a desktop host) the endpoint directly. Never echoes a secret.
let private toResponse (result: TurnResult) : int * string =
    use stream = new System.IO.MemoryStream()

    let status =
        (use writer = new Utf8JsonWriter(stream)

         let status =
             match result with
             | TurnResult.Produced(treeJson, ops, version) ->
                 writer.WriteStartObject()
                 writer.WriteString("TreeJson", treeJson)
                 writer.WriteStartArray("Ops")

                 for op in ops do
                     writer.WriteStartObject()
                     writer.WriteString("OpId", op.OpId)
                     writer.WriteString("OpJson", op.OpJson)
                     writer.WriteEndObject()

                 writer.WriteEndArray()
                 writer.WriteString("Version", version)
                 writer.WriteEndObject()
                 200
             | TurnResult.AccessDenied reason ->
                 writer.WriteStartObject()
                 writer.WriteString("Reason", reason)
                 writer.WriteEndObject()
                 401
             | TurnResult.TurnFailed err ->
                 writer.WriteStartObject()
                 writer.WriteString("Stage", TurnStage.label err.Stage)
                 writer.WriteString("Code", err.Code)
                 writer.WriteString("Message", err.Message)
                 writer.WriteEndObject()
                 422

         writer.Flush()
         status)

    status, Encoding.UTF8.GetString(stream.ToArray())

/// Handle one proxied turn: read the client's prompt (+ tree), run it through
/// the server-held client, and return the (status, body) to write back.
let handle (client: FuaranClient) (bodyJson: string) : Async<int * string> =
    async {
        let request = ClientRequest.parse bodyJson

        if request.Prompt.Trim() = "" then
            return 422, """{"Stage":"parse","Code":"EMPTY_PROMPT","Message":"a prompt is required"}"""
        else
            // The client config carries the credentials; the request contributes
            // only the prompt and the tree under repair.
            let args =
                { GenerateArgs.prompt request.Prompt with
                    CurrentTreeJson = request.CurrentTreeJson }

            let! result = client.Generate args
            return toResponse result
    }
