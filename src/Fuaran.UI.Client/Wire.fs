// Fuaran.UI.Client — the HTTP envelope mapping.
//
// This is the SINGLE place that pins how the typed contract crosses the wire,
// so a future change to the endpoint's framing touches one file. The request
// body mirrors the surface's request record field-for-field (the PascalCase
// names below are the canonical on-the-wire keys); the response is discriminated
// by HTTP status — 200 -> produced, 401 -> access denied, 422 -> turn failed —
// per the endpoint's documented status map. Any other status is surfaced as a
// `Provider`-stage failure so a caller never has to special-case transport.
//
// Mirrors the TypeScript client's `wire.ts` behaviour byte-for-byte: fields are
// omitted (not sent as null) when absent, and reads tolerate the canonical
// PascalCase key OR a camelCase alias so a deployment that lower-cases its JSON
// still parses.

namespace Fuaran.UI.Client

open System
open System.Text
open System.Text.Json

/// Resolved per-call secrets, merged from the client config + call overrides.
type ResolvedSecrets =
    { ProviderKey: string option
      AccessToken: string option }

[<RequireQualifiedAccess>]
module Wire =

    /// Build the JSON request body from the typed args + resolved secrets.
    /// Fields are omitted (not written as `null`) when absent, matching the
    /// surface defaults (a missing corpus flag is privacy-preserving; a missing
    /// current tree is a fresh generation).
    let toWireBody (args: GenerateArgs) (secrets: ResolvedSecrets) : string =
        use stream = new System.IO.MemoryStream()

        (use writer = new Utf8JsonWriter(stream)
         writer.WriteStartObject()
         writer.WriteString("Prompt", args.Prompt)

         args.CurrentTreeJson
         |> Option.iter (fun v -> writer.WriteString("CurrentTreeJson", v))

         secrets.ProviderKey |> Option.iter (fun v -> writer.WriteString("ByokKey", v))

         secrets.AccessToken
         |> Option.iter (fun v -> writer.WriteString("AccessToken", v))

         args.DisableCorpusRead
         |> Option.iter (fun v -> writer.WriteBoolean("DisableCorpusRead", v))

         args.ContributeCorpus
         |> Option.iter (fun v -> writer.WriteBoolean("ContributeCorpus", v))

         writer.WriteEndObject()
         writer.Flush())

        Encoding.UTF8.GetString(stream.ToArray())

    /// Read a string value tolerant of the canonical PascalCase wire key or a
    /// camelCase alias, so a deployment that lower-cases its JSON still parses.
    let private pickString (el: JsonElement) (pascal: string) (camel: string) : string option =
        let tryGet (name: string) =
            match el.TryGetProperty name with
            | true, v when v.ValueKind = JsonValueKind.String ->
                match v.GetString() with
                | null -> None
                | s -> Some s
            | _ -> None

        match tryGet pascal with
        | Some s -> Some s
        | None -> tryGet camel

    let private pickElement (el: JsonElement) (pascal: string) (camel: string) : JsonElement option =
        match el.TryGetProperty pascal with
        | true, v -> Some v
        | _ ->
            match el.TryGetProperty camel with
            | true, v -> Some v
            | _ -> None

    let private parseAppliedOps (el: JsonElement option) : AppliedOp list =
        match el with
        | Some arr when arr.ValueKind = JsonValueKind.Array ->
            [ for entry in arr.EnumerateArray() do
                  if entry.ValueKind = JsonValueKind.Object then
                      { OpId = pickString entry "OpId" "opId" |> Option.defaultValue ""
                        OpJson = pickString entry "OpJson" "opJson" |> Option.defaultValue "" } ]
        | _ -> []

    /// Parse a JSON body into its root element; an empty / malformed / non-object
    /// body yields an empty object so every read below falls back cleanly.
    let private parseRoot (bodyText: string) : JsonElement =
        let empty () = JsonDocument.Parse("{}").RootElement

        if System.String.IsNullOrWhiteSpace bodyText then
            empty ()
        else
            try
                let doc = JsonDocument.Parse bodyText

                if doc.RootElement.ValueKind = JsonValueKind.Object then
                    doc.RootElement
                else
                    empty ()
            with _ ->
                empty ()

    let private failed (stage: TurnStage) (code: string) (message: string) : TurnResult =
        TurnResult.TurnFailed
            { Stage = stage
              Code = code
              Message = message }

    /// Map an HTTP (status, body) pair onto the typed `TurnResult`. The status
    /// selects the case; the body supplies the payload. The error envelope may be
    /// flat (`{ Stage, Code, Message }`) or nested under `Error` — both parse.
    let parseTurnResponse (status: int) (bodyText: string) : TurnResult =
        let body = parseRoot bodyText

        match status with
        | 200 ->
            TurnResult.Produced(
                treeJson = (pickString body "TreeJson" "treeJson" |> Option.defaultValue ""),
                ops = parseAppliedOps (pickElement body "Ops" "ops"),
                version = (pickString body "Version" "version" |> Option.defaultValue "")
            )
        | 401 -> TurnResult.AccessDenied(pickString body "Reason" "reason" |> Option.defaultValue "access denied")
        | 422 ->
            let envelope = pickElement body "Error" "error" |> Option.defaultValue body

            failed
                (pickString envelope "Stage" "stage"
                 |> Option.map TurnStage.ofLabel
                 |> Option.defaultValue TurnStage.Provider)
                (pickString envelope "Code" "code" |> Option.defaultValue "TURN_FAILED")
                (pickString envelope "Message" "message" |> Option.defaultValue "the turn failed")
        | _ ->
            // Any other status is a transport-level failure — surfaced as a
            // provider-stage envelope so the caller handles it through the same
            // `TurnFailed` path.
            let detail =
                match pickString body "Message" "message" with
                | Some m when m <> "" -> m
                | _ ->
                    if String.IsNullOrEmpty bodyText then
                        $"unexpected status {status}"
                    else
                        let snippet = bodyText.Substring(0, min 200 bodyText.Length)

                        if snippet = "" then
                            $"unexpected status {status}"
                        else
                            snippet

            failed TurnStage.Provider $"HTTP_{status}" detail
