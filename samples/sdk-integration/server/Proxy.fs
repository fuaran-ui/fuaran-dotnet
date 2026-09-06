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
// AND IT IS NOT AN OPEN SPEND ENDPOINT. A route that holds your BYOK key and
// answers anyone who can reach it spends your money at whatever rate the
// internet feels like. Three things stand between the two, and all three are
// here rather than in the README, because a sample is copied before it is read:
//
//   * an AUTHORISATION hook that is FAIL-CLOSED — there is no "no policy"
//     value, so a host must choose, and choosing to serve everyone is spelled
//     `Policy.anonymous` in the host's own source where a reader can grep it;
//   * a PROMPT-LENGTH cap — prompt length is the input-token bill, and it is
//     the one cost dimension a caller controls directly;
//   * a per-caller RATE LIMIT — a fixed window, in memory, keyed on whatever
//     the host calls a caller.
//
// None of the three is a substitute for a real gateway. What they are is a
// floor: a copy of this file cannot become an unauthenticated, unbounded spend
// endpoint by omission, only by deliberate opt-out.
//
// `handle` is a function of (policy, caller, client, requestBody) so it can be
// driven in a test against the local mock endpoint with no live credentials.

module Fuaran.Sample.SdkIntegration.Server.Proxy

open System
open System.Collections.Concurrent
open System.Text
open System.Text.Json

open Fuaran.UI.Client

/// Whatever the host uses to tell one caller from another — a session id, an
/// API-key hash, a remote address. The sample uses the remote address, which is
/// the weakest useful choice and is called out as such in the README.
type CallerId = CallerId of string

/// What the browser is allowed to ask for: a prompt, and optionally the tree it
/// is editing. Everything else on the wire is the server's business.
type ClientRequest =
    { Prompt: string
      CurrentTreeJson: string option }

/// A refusal this route makes on its own account, before any turn runs.
type ProxyRefusal =
    { Status: int
      Code: string
      Message: string }

/// The fixed-window rate limit. Deliberately the simplest thing that bounds
/// spend: one window per caller, reset when it expires.
type RateLimit = { MaxTurns: int; Window: TimeSpan }

type private CallerWindow = { Started: DateTimeOffset; Turns: int }

/// A per-process fixed-window limiter. A real deployment wants a shared store —
/// this one is per-instance, so two instances allow twice the traffic. That is
/// a fact worth stating rather than a defect worth hiding, and the clock is a
/// parameter so a test can prove the window actually resets.
type RateLimiter(limit: RateLimit, now: unit -> DateTimeOffset) =
    let windows = ConcurrentDictionary<string, CallerWindow>()

    /// `true` when this turn is within the caller's window.
    member _.Admit(CallerId caller) : bool =
        let current = now ()

        let updated =
            windows.AddOrUpdate(
                caller,
                (fun _ -> { Started = current; Turns = 1 }),
                (fun _ existing ->
                    if current - existing.Started >= limit.Window then
                        { Started = current; Turns = 1 }
                    else
                        { existing with
                            Turns = existing.Turns + 1 })
            )

        updated.Turns <= limit.MaxTurns

/// Everything this route refuses on, in one record so a host cannot wire the
/// client and forget the guards.
type ProxyPolicy =
    {
        /// FAIL-CLOSED. `Ok ()` admits the caller; `Error reason` refuses with
        /// 401. There is no default — `Policy.create` makes a host choose.
        Authorize: CallerId -> Result<unit, string>
        /// The longest prompt this route will forward.
        MaxPromptLength: int
        /// `None` serves unlimited turns per caller — legitimate behind a
        /// gateway that already limits, and a deliberate choice either way.
        RateLimiter: RateLimiter option
    }

[<RequireQualifiedAccess>]
module Policy =

    /// The sample's default cap. Long enough for any real authoring prompt,
    /// short enough that a scripted caller cannot make each request expensive.
    [<Literal>]
    let DefaultMaxPromptLength = 4000

    /// The sample's default window: enough for interactive authoring, far
    /// short of what a script can do in a minute.
    let DefaultRateLimit =
        { MaxTurns = 30
          Window = TimeSpan.FromMinutes 1.0 }

    /// A policy with an authorisation hook you supply, and the sample's default
    /// cap and window.
    let create (authorize: CallerId -> Result<unit, string>) : ProxyPolicy =
        { Authorize = authorize
          MaxPromptLength = DefaultMaxPromptLength
          RateLimiter = Some(RateLimiter(DefaultRateLimit, fun () -> DateTimeOffset.UtcNow)) }

    /// A shared-secret hook: the host reads a secret off the request and hands
    /// it here. Not an authentication scheme — a placeholder with the right
    /// SHAPE, so replacing it is a one-line change and forgetting to replace it
    /// is not silent.
    let sharedSecret (expected: string) (presented: CallerId -> string option) : CallerId -> Result<unit, string> =
        fun caller ->
            match presented caller with
            | Some value when
                // Length-independent comparison: the sample is copied, and a
                // short-circuiting compare over a secret is copied with it.
                value.Length = expected.Length && String.CompareOrdinal(value, expected) = 0
                ->
                Ok()
            | _ -> Error "this route requires the shared secret configured for it"

    /// Serve every caller. Named so a route with no real authorisation says so
    /// in its own source, and a reader can grep for it.
    let anonymous: CallerId -> Result<unit, string> = fun _ -> Ok()

[<RequireQualifiedAccess>]
module ClientRequest =

    /// Read the two client-controlled fields, tolerant of camelCase or the
    /// retired PascalCase. Credential fields present in the body are
    /// deliberately not read — see the module header.
    let parse (bodyJson: string) : ClientRequest =
        let pick (root: JsonElement) (canonical: string) (alias: string) =
            let tryGet (name: string) =
                match root.TryGetProperty name with
                | true, v when v.ValueKind = JsonValueKind.String ->
                    match v.GetString() with
                    | null -> None
                    | s -> Some s
                | _ -> None

            match tryGet canonical with
            | Some s -> Some s
            | None -> tryGet alias

        if String.IsNullOrWhiteSpace bodyJson then
            { Prompt = ""; CurrentTreeJson = None }
        else
            try
                let root = JsonDocument.Parse(bodyJson).RootElement

                if root.ValueKind <> JsonValueKind.Object then
                    { Prompt = ""; CurrentTreeJson = None }
                else
                    { Prompt = pick root "prompt" "Prompt" |> Option.defaultValue ""
                      CurrentTreeJson = pick root "currentTree" "CurrentTreeJson" }
            with _ ->
                { Prompt = ""; CurrentTreeJson = None }

let private writeJson (write: Utf8JsonWriter -> unit) =
    use stream = new IO.MemoryStream()

    (use writer = new Utf8JsonWriter(stream)
     write writer
     writer.Flush())

    Encoding.UTF8.GetString(stream.ToArray())

/// The endpoint's own refusal shape, so a browser client decodes ONE contract
/// whether it is talking to this proxy or the endpoint directly.
let private errorPayload (code: string) (message: string) (stage: string option) =
    writeJson (fun w ->
        w.WriteStartObject()
        w.WriteStartObject "error"
        w.WriteString("code", code)
        w.WriteString("message", message)
        stage |> Option.iter (fun s -> w.WriteString("stage", s))
        w.WriteEndObject()
        w.WriteEndObject())

/// Serialise a `TurnResult` (+ the deployment facts the reply carried) back to
/// the browser in the endpoint's own wire shape. Never echoes a secret.
let private toResponse (result: TurnResult) (detail: ProducedDetail option) : int * string =
    match result with
    | TurnResult.Produced(treeJson, ops, version) ->
        200,
        writeJson (fun w ->
            w.WriteStartObject()
            w.WriteString("version", version)
            w.WritePropertyName "tree"
            w.WriteRawValue treeJson

            w.WriteNumber(
                "opsApplied",
                match detail with
                | Some d -> d.OpsApplied
                | None -> List.length ops
            )

            detail
            |> Option.bind _.Provider
            |> Option.iter (fun p -> w.WriteString("provider", p))

            // Absence is written as absence, exactly as the endpoint does: a
            // substituted value would look like a report.
            detail
            |> Option.bind _.ServedModel
            |> Option.iter (fun m -> w.WriteString("servedModel", m))

            detail
            |> Option.bind _.Snapshot
            |> Option.iter (fun s ->
                w.WriteStartObject "snapshot"
                w.WriteString("state", s.State)
                s.Version |> Option.iter (fun v -> w.WriteString("version", v))
                s.ContentHash |> Option.iter (fun h -> w.WriteString("contentHash", h))
                w.WriteEndObject())

            w.WriteEndObject())
    | TurnResult.AccessDenied reason -> 401, errorPayload "ACCESS_DENIED" reason None
    | TurnResult.TurnFailed err -> 422, errorPayload err.Code err.Message (Some(TurnStage.label err.Stage))

/// The guards, in the order that spends least: authorise, then bound the input,
/// then bound the rate. Each refusal happens before the endpoint is called, so
/// none of them costs a token.
let private guard (policy: ProxyPolicy) (caller: CallerId) (request: ClientRequest) : ProxyRefusal option =
    match policy.Authorize caller with
    | Error reason ->
        Some
            { Status = 401
              Code = "ACCESS_DENIED"
              Message = reason }
    | Ok() ->
        if request.Prompt.Trim() = "" then
            Some
                { Status = 400
                  Code = "BAD_REQUEST"
                  Message = "a prompt is required" }
        elif request.Prompt.Length > policy.MaxPromptLength then
            Some
                { Status = 400
                  Code = "PROMPT_TOO_LONG"
                  Message = $"a prompt may be at most {policy.MaxPromptLength} characters" }
        else
            match policy.RateLimiter with
            | Some limiter when not (limiter.Admit caller) ->
                Some
                    { Status = 429
                      Code = "RATE_LIMITED"
                      Message = "too many turns from this caller; try again shortly" }
            | _ -> None

/// Handle one proxied turn: authorise and bound the caller, read the client's
/// prompt (+ tree), run it through the server-held client, and return the
/// (status, body) to write back.
let handle (policy: ProxyPolicy) (caller: CallerId) (client: FuaranClient) (bodyJson: string) : Async<int * string> =
    async {
        let request = ClientRequest.parse bodyJson

        match guard policy caller request with
        | Some refusal -> return refusal.Status, errorPayload refusal.Code refusal.Message None
        | None ->
            // The client config carries the credentials; the request contributes
            // only the prompt and the tree under repair.
            let args =
                { GenerateArgs.prompt request.Prompt with
                    CurrentTreeJson = request.CurrentTreeJson }

            let! result, detail = client.GenerateDetailed args
            return toResponse result detail
    }
