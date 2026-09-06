// Fuaran.UI.Client — the typed contract for the Fuaran generation endpoint.
//
// These types mirror the generation endpoint's surface contract field-for-field:
// the turn request, the three-way turn result (produced / access-denied /
// turn-failed), the applied-op record, and the surface-version echo. They are
// the F# lockstep counterpart of the endpoint's published surface contract and
// of the TypeScript client that mirrors the same surface — a field added there
// is added here in the same change, and `Wire.fs` pins how each maps onto the
// HTTP envelope.
//
// The endpoint itself is the Fuaran generation endpoint: a paid, stateless,
// bring-your-own-key (BYOK) HTTPS surface that takes a prompt (+ an optional
// current tree) and returns a new canonical wire-format tree. The endpoint URL
// and the paid access token are the commercial gate; this client is a thin,
// OSS-safe types layer over it. `Client.fs` states the endpoint-scheme rule the
// client actually enforces (https, or loopback, or an explicit opt-in) — this
// comment used to say "HTTPS" flatly, which no code checked.

namespace Fuaran.UI.Client

/// The generation-surface contract shape this client is built against.
///
/// The client-facing request/response *shape* is the additive corpus-flag
/// contract — `DisableCorpusRead` / `ContributeCorpus` plus the surface-version
/// echo on a produced result. Later minor surface bumps have only added
/// server-side usage fields that never cross the client boundary, so this shape
/// is stable across them; `FuaranClient.Generate` echoes back whichever version
/// the live surface stamps (see `TurnResult.Produced`). Pinned to `1.2.0` to
/// match the TypeScript client's declared lockstep version; only the major is
/// compared for compatibility.
module SurfaceContract =

    /// The generation-surface contract version this client is built against.
    /// Kept in lockstep with the TypeScript client's `SURFACE_VERSION`.
    [<Literal>]
    let Version = "1.2.0"

    /// True when an echoed surface version shares this client's major version,
    /// i.e. the request/response shape is one this client understands. A
    /// differing major signals a breaking surface revision the client predates.
    let isVersionCompatible (echoed: string) : bool =
        let major (v: string) =
            match v.Split('.') with
            | [||] -> ""
            | parts -> parts.[0].Trim()

        let echoedMajor = major echoed
        echoedMajor <> "" && echoedMajor = major Version

/// One op the turn applied to reach the produced tree. Mirrors the surface's
/// applied-op record: a dedup `OpId` and the canonical wire JSON of the op.
/// Decode `OpJson` with `Fuaran.UI.Ops.JsonDecode.decodeOp` for a typed `TreeOp`.
type AppliedOp = { OpId: string; OpJson: string }

/// The loop stage at which a turn failed — distinguishes a rejected access
/// token (no provider call made) from a provider/transport failure from an
/// emission the endpoint refused to apply (the default-deny-by-shape gate).
/// Mirrors `GenerationSurface.TurnStage` on the endpoint side.
[<RequireQualifiedAccess>]
type TurnStage =
    | AccessToken
    | Provider
    | Parse
    | Apply

[<RequireQualifiedAccess>]
module TurnStage =

    /// The short stable wire label for a stage (no caller-supplied data).
    let label (stage: TurnStage) : string =
        match stage with
        | TurnStage.AccessToken -> "access-token"
        | TurnStage.Provider -> "provider"
        | TurnStage.Parse -> "parse"
        | TurnStage.Apply -> "apply"

    /// Parse a wire label back to a stage, defaulting to `Provider` for any
    /// unrecognised label (matching the TypeScript client's tolerance).
    let ofLabel (label: string) : TurnStage =
        match label with
        | "access-token" -> TurnStage.AccessToken
        | "parse" -> TurnStage.Parse
        | "apply" -> TurnStage.Apply
        | _ -> TurnStage.Provider

/// A recoverable failure surfaced by a turn. `Code` is a stable discriminant;
/// `Message` is model-facing — for the `Apply` stage it carries the apply-error
/// envelope so the caller's next prompt can re-emit against the hint. Never
/// carries the BYOK key. Mirrors the surface's recoverable-error envelope.
type RecoverableError =
    { Stage: TurnStage
      Code: string
      Message: string }

/// Well-known `RecoverableError.Code` values this CLIENT synthesises, as
/// opposed to the codes the endpoint sends. A caller branching on `Code` can
/// tell "the endpoint refused" from "the call never reached it", which is a
/// different remedy every time.
[<RequireQualifiedAccess>]
module ClientCode =

    /// The call did not complete: the transport threw, or the configured
    /// `Timeout` elapsed. The message is FIXED — an upstream exception string
    /// can quote a URL, a header, or a proxy's internal hostname, and this
    /// result is routinely rendered straight into a browser.
    [<Literal>]
    let Network = "NETWORK"

    /// The endpoint replied 200 with no usable tree. Not a success: a caller
    /// that accepted it would hold `""` as the current tree and silently
    /// repair nothing on every subsequent turn.
    [<Literal>]
    let MalformedResponse = "MALFORMED_RESPONSE"

    /// The endpoint is a plaintext `http://` URL that is not loopback, and
    /// `AllowInsecureEndpoint` was not set. Refused BEFORE the request is
    /// built, so the BYOK key and the access token are never put on a socket.
    [<Literal>]
    let InsecureEndpoint = "INSECURE_ENDPOINT"

/// The grounding-corpus snapshot the endpoint served a turn against.
/// `Version` / `ContentHash` are present only when a payload was actually
/// loaded — a turn generated ungrounded still succeeds.
type SnapshotState =
    { State: string
      Version: string option
      ContentHash: string option }

/// The deployment facts a produced turn carries beyond the tree itself.
///
/// They are here rather than on `TurnResult.Produced` deliberately. The three
/// things every caller needs — the tree, how much changed, the version echo —
/// stay in the case; the things a caller needs only when auditing a deployment
/// hang off `FuaranClient.GenerateDetailed`. Widening the case would break
/// every `match` in every consumer to carry information most of them ignore.
type ProducedDetail =
    {
        /// How many ops the turn applied to reach the produced tree — a COUNT.
        /// The endpoint does not return the op list, so `TurnResult.Produced`
        /// carries ops only when something in front of it (a proxy, a mock, an
        /// in-process host) supplies them.
        OpsApplied: int
        /// The allowlisted provider id the deployment chose.
        Provider: string option
        /// The model the provider's own reply named as having served. `None`
        /// means UNREPORTED, and is deliberately not the model the deployment
        /// asked for: a substituted value would look like a report and hide an
        /// alias re-point, which is the one thing this field exists to expose.
        ServedModel: string option
        /// The grounding snapshot's state for this turn.
        Snapshot: SnapshotState option
    }

/// The endpoint's reply, discriminated three ways. Mirrors the surface's
/// three-case turn result; the HTTP status selects the case (see `Wire.fs`).
[<RequireQualifiedAccess>]
type TurnResult =
    /// The turn produced a new tree (HTTP 200). Carries the canonical wire JSON
    /// of the new tree, the ops applied this turn, and the echoed surface version.
    | Produced of treeJson: string * ops: AppliedOp list * version: string
    /// The access token was missing / expired / invalid — rejected at the edge
    /// before any provider call, so the BYOK key was never used (HTTP 401).
    | AccessDenied of reason: string
    /// The provider / parse / apply stage failed; carries the recoverable
    /// envelope (HTTP 422, or a synthesised envelope for an unexpected status).
    | TurnFailed of RecoverableError

/// Arguments to one `FuaranClient.Generate` call. `Prompt` is required;
/// everything else falls back to the client's configuration.
///
/// Pass `CurrentTreeJson` (the canonical wire JSON of the tree the model is
/// editing) to make the turn a *repair* — the token-saving ergonomic the whole
/// model hinges on. Omit it for a fresh generation. The turn-loop helper
/// (`FuaranSession`) carries it forward for you.
type GenerateArgs =
    {
        /// The authoring prompt.
        Prompt: string
        /// Canonical wire JSON of the tree to edit; `None` for a fresh generation.
        CurrentTreeJson: string option
        /// BYOK provider key for this call (overrides the client config).
        /// Memory-only — never bundle a key into shipped code; see the README.
        ProviderKey: string option
        /// Paid access token for this call (overrides the client config).
        AccessToken: string option
        /// Opt OUT of corpus reads for this turn. `None` / `false` keeps reads on.
        DisableCorpusRead: bool option
        /// Opt IN to contributing this turn as a candidate for the next corpus
        /// version. `None` / `false` contributes nothing.
        ContributeCorpus: bool option
        /// Your OPAQUE correlation id, joining this turn's records at the
        /// endpoint with your own render / op-stream telemetry into one
        /// reconstructable change. The endpoint mints none — an id minted
        /// there could not reach your legs.
        InteractionId: string option
    }

[<RequireQualifiedAccess>]
module GenerateArgs =

    /// A fresh-generation request from just a prompt — every optional field
    /// unset. The 10-line-quickstart entry: `GenerateArgs.prompt "…" |> client.Generate`.
    let prompt (text: string) : GenerateArgs =
        { Prompt = text
          CurrentTreeJson = None
          ProviderKey = None
          AccessToken = None
          DisableCorpusRead = None
          ContributeCorpus = None
          InteractionId = None }

    /// A repair request: a prompt against an existing tree's canonical wire JSON.
    let repair (text: string) (currentTreeJson: string) : GenerateArgs =
        { prompt text with
            CurrentTreeJson = Some currentTreeJson }
