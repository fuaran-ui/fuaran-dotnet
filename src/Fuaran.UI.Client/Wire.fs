// Fuaran.UI.Client — the HTTP envelope mapping.
//
// This is the SINGLE place that pins how the typed contract crosses the wire,
// so a future change to the endpoint's framing touches one file.
//
// IT SPEAKS THE DEPLOYED WIRE, and that was a correction. This file used to
// write `{Prompt, CurrentTreeJson, ByokKey, AccessToken, …}` and read
// `{TreeJson, Ops, Version}` across a 200/401/422 status map, because that is
// what the published specification described. The endpoint reads `prompt` /
// `currentTree`, takes secrets from HEADERS ONLY, replies
// `{version, tree, opsApplied, provider, servedModel?, snapshot}`, and refuses
// with `{error:{code,message,stage?}}` at 400 / 401 / 405 / 422 / 500 / 503. A
// client built from the old shape was answered `400 BAD_REQUEST: request body
// has no 'prompt' string`.
//
// So: the request body carries NO SECRET — `Client.fs` puts the access token and
// the BYOK key in headers, and the endpoint REFUSES a body that carries either,
// never reading the value. There is no way to express the old shape through this
// module, which is the point: a body-carried key is a key an intermediary may
// have logged wholesale, and the type system is where that should stop being
// reachable.
//
// Reads stay tolerant of the retired PascalCase spelling — a same-origin proxy
// or a mock in front of the endpoint may still speak it — but writes do not.

namespace Fuaran.UI.Client

open System
open System.Text
open System.Text.Json

/// Resolved per-call secrets, merged from the client config + call overrides.
/// They travel as HEADERS (see `Client.fs`); this type exists to carry them to
/// that point, never into a body.
type ResolvedSecrets =
    { ProviderKey: string option
      AccessToken: string option }

[<RequireQualifiedAccess>]
module Wire =

    /// Build the JSON request body from the typed args. Fields are omitted (not
    /// written as `null`) when absent, matching the endpoint's defaults (a
    /// missing corpus flag is privacy-preserving; a missing current tree is a
    /// fresh generation).
    ///
    /// `CurrentTreeJson` is written as a JSON STRING rather than inlined as an
    /// object. The endpoint accepts both, and the string form is the one that
    /// cannot corrupt the payload: inlining would mean re-emitting the caller's
    /// canonical bytes, and the whole repair ergonomic depends on the tree
    /// crossing back and forth unchanged.
    let toWireBody (args: GenerateArgs) : string =
        use stream = new System.IO.MemoryStream()

        (use writer = new Utf8JsonWriter(stream)
         writer.WriteStartObject()
         writer.WriteString("prompt", args.Prompt)

         args.CurrentTreeJson
         |> Option.iter (fun v -> writer.WriteString("currentTree", v))

         args.DisableCorpusRead
         |> Option.iter (fun v -> writer.WriteBoolean("disableCorpusRead", v))

         args.ContributeCorpus
         |> Option.iter (fun v -> writer.WriteBoolean("contributeCorpus", v))

         args.InteractionId
         |> Option.iter (fun v -> writer.WriteString("interactionId", v))

         writer.WriteEndObject()
         writer.Flush())

        Encoding.UTF8.GetString(stream.ToArray())

    /// Read a string value tolerant of the canonical wire key or an alternate
    /// spelling, so a proxy or mock that has not yet moved still parses.
    let private pickString (el: JsonElement) (canonical: string) (alias: string) : string option =
        let tryGet (name: string) =
            match el.TryGetProperty name with
            | true, v when v.ValueKind = JsonValueKind.String ->
                match v.GetString() with
                | null -> None
                | s -> Some s
            | _ -> None

        match tryGet canonical with
        | Some s -> Some s
        | None -> tryGet alias

    let private pickElement (el: JsonElement) (canonical: string) (alias: string) : JsonElement option =
        match el.TryGetProperty canonical with
        | true, v -> Some v
        | _ ->
            match el.TryGetProperty alias with
            | true, v -> Some v
            | _ -> None

    let private pickInt (el: JsonElement) (canonical: string) (alias: string) : int option =
        match pickElement el canonical alias with
        | Some v when v.ValueKind = JsonValueKind.Number ->
            match v.TryGetInt32() with
            | true, n -> Some n
            | _ -> None
        | _ -> None

    let private parseAppliedOps (el: JsonElement option) : AppliedOp list =
        match el with
        | Some arr when arr.ValueKind = JsonValueKind.Array ->
            [ for entry in arr.EnumerateArray() do
                  if entry.ValueKind = JsonValueKind.Object then
                      { OpId = pickString entry "opId" "OpId" |> Option.defaultValue ""
                        OpJson = pickString entry "opJson" "OpJson" |> Option.defaultValue "" } ]
        | _ -> []

    /// Parse a JSON body into its root element; an empty / malformed / non-object
    /// body yields `None`, so a 200 can be told from a 200-shaped nothing.
    let private parseRoot (bodyText: string) : JsonElement option =
        if String.IsNullOrWhiteSpace bodyText then
            None
        else
            try
                let doc = JsonDocument.Parse bodyText

                if doc.RootElement.ValueKind = JsonValueKind.Object then
                    Some doc.RootElement
                else
                    None
            with _ ->
                None

    let private emptyObject = JsonDocument.Parse("{}").RootElement

    let private failed (stage: TurnStage) (code: string) (message: string) : TurnResult =
        TurnResult.TurnFailed
            { Stage = stage
              Code = code
              Message = message }

    /// The endpoint replied 200 with nothing usable in it. A `TurnFailed`, not a
    /// `Produced ""`: the session HOLDS the produced tree, so an empty one
    /// poisons every later repair rather than failing the turn that caused it.
    let internal malformed (detail: string) : TurnResult =
        failed TurnStage.Provider ClientCode.MalformedResponse detail

    /// The produced tree's canonical wire JSON, however the reply carried it.
    /// The deployed endpoint writes `tree` as an OBJECT; a proxy or mock may
    /// write it as a JSON string, and the retired shape called it `TreeJson`.
    let private readTree (body: JsonElement) : string option =
        match pickElement body "tree" "TreeJson" with
        | Some v when v.ValueKind = JsonValueKind.Object -> Some(v.GetRawText())
        | Some v when v.ValueKind = JsonValueKind.String ->
            match v.GetString() with
            | null -> None
            | s when String.IsNullOrWhiteSpace s -> None
            | s -> Some s
        | _ -> None

    let private readSnapshot (body: JsonElement) : SnapshotState option =
        match body.TryGetProperty "snapshot" with
        | true, v when v.ValueKind = JsonValueKind.Object ->
            match pickString v "state" "State" with
            | None -> None
            | Some state ->
                Some
                    { State = state
                      Version = pickString v "version" "Version"
                      ContentHash = pickString v "contentHash" "ContentHash" }
        | _ -> None

    /// The deployment facts a 200 carries beyond the tree. `None` when the
    /// status was not 200, or the body carried no usable tree — the same
    /// condition that makes the turn `MALFORMED_RESPONSE`, so a caller can
    /// never read a detail off a reply the turn itself rejected.
    let parseProducedDetail (status: int) (bodyText: string) : ProducedDetail option =
        match status, parseRoot bodyText with
        | 200, Some body when (readTree body).IsSome ->
            Some
                { OpsApplied =
                    match pickInt body "opsApplied" "OpsApplied" with
                    | Some n -> n
                    // No count on the wire: fall back to the length of an op
                    // list, which is what a proxy or an in-process host sends
                    // instead.
                    | None -> List.length (parseAppliedOps (pickElement body "ops" "Ops"))
                  Provider = pickString body "provider" "Provider"
                  ServedModel = pickString body "servedModel" "ServedModel"
                  Snapshot = readSnapshot body }
        | _ -> None

    /// Map an HTTP (status, body) pair onto the typed `TurnResult`. The status
    /// selects the case; the body supplies the payload.
    ///
    /// The endpoint sends ONE refusal shape — `{error:{code,message,stage?}}` —
    /// at every non-200 status, so every refusal is read the same way and a
    /// caller never has to special-case transport. The retired flat and
    /// `Error`-nested PascalCase forms still parse, for a proxy or mock in front
    /// of the endpoint that has not moved.
    let parseTurnResponse (status: int) (bodyText: string) : TurnResult =
        let body = parseRoot bodyText |> Option.defaultValue emptyObject

        let envelope = pickElement body "error" "Error" |> Option.defaultValue body

        let code fallback =
            pickString envelope "code" "Code" |> Option.defaultValue fallback

        let message fallback =
            pickString envelope "message" "Message" |> Option.defaultValue fallback

        let stage fallback =
            pickString envelope "stage" "Stage"
            |> Option.map TurnStage.ofLabel
            |> Option.defaultValue fallback

        match status with
        | 200 ->
            match readTree body with
            | Some treeJson ->
                TurnResult.Produced(
                    treeJson = treeJson,
                    ops = parseAppliedOps (pickElement body "ops" "Ops"),
                    version = (pickString body "version" "Version" |> Option.defaultValue "")
                )
            | None -> malformed "the endpoint replied 200 with no tree"
        | 401 ->
            // The retired shape put the reason in a bare `Reason` member.
            TurnResult.AccessDenied(
                pickString envelope "message" "Message"
                |> Option.orElse (pickString body "Reason" "reason")
                |> Option.defaultValue "access denied"
            )
        | 422 -> failed (stage TurnStage.Provider) (code "TURN_FAILED") (message "the turn failed")
        | _ ->
            // Every other refusal — 400 / 405 / 500 / 503, and anything a proxy
            // invents — is surfaced as a provider-stage envelope so the caller
            // handles it through the same `TurnFailed` path. The endpoint's OWN
            // code is preferred over a synthesised one: a body-carried secret
            // and a missing key header are different mistakes with different
            // remedies, and `HTTP_400` names neither.
            failed (stage TurnStage.Provider) (code $"HTTP_{status}") (message $"unexpected status {status}")
