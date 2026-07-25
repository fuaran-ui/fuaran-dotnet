// Fuaran.UI.Client — the turn-loop helper.
//
// A session holds the current tree between turns so each subsequent prompt is a
// *repair* against it (a cheap diff), not a from-scratch regeneration — the
// token-saving ergonomic the whole model hinges on. The first `Next prompt` is
// a fresh generation; once a turn produces a tree the session remembers it, and
// the next `Next prompt` sends it as `CurrentTreeJson` automatically.
//
// Stateful by design — one session is one editing conversation. Mirrors the
// TypeScript `FuaranSession`: on `Produced` the session advances to the new
// tree; on `AccessDenied` / `TurnFailed` the held tree is left unchanged so the
// caller can retry the same repair.

namespace Fuaran.UI.Client

/// Per-call options for `FuaranSession.Next` — everything a `GenerateArgs`
/// carries except `Prompt` (the argument) and `CurrentTreeJson` (the session
/// supplies it). Every field defaults to unset.
type SessionTurnOptions =
    { ProviderKey: string option
      AccessToken: string option
      DisableCorpusRead: bool option
      ContributeCorpus: bool option }

[<RequireQualifiedAccess>]
module SessionTurnOptions =

    /// The all-unset default — a bare `Next prompt` uses this.
    let none: SessionTurnOptions =
        { ProviderKey = None
          AccessToken = None
          DisableCorpusRead = None
          ContributeCorpus = None }

/// A turn loop over a `FuaranClient` that carries the current tree forward.
/// Seed with an existing tree's canonical wire JSON so the first turn is already
/// a repair, or omit to start with a fresh generation.
type FuaranSession(client: FuaranClient, ?initialTreeJson: string) =
    let mutable currentTreeJson: string option = initialTreeJson

    /// The canonical wire JSON of the tree the session is holding, or `None`
    /// before the first produced turn.
    member _.CurrentTreeJson = currentTreeJson

    /// Run the next turn with per-call options. The held tree (if any) is sent
    /// as `CurrentTreeJson`, so this prompt repairs it rather than regenerating.
    /// On a `Produced` result the session advances to the new tree; on
    /// `AccessDenied` / `TurnFailed` the held tree is left unchanged.
    member _.NextWith(prompt: string, options: SessionTurnOptions) : Async<TurnResult> =
        async {
            let args: GenerateArgs =
                { Prompt = prompt
                  CurrentTreeJson = currentTreeJson
                  ProviderKey = options.ProviderKey
                  AccessToken = options.AccessToken
                  DisableCorpusRead = options.DisableCorpusRead
                  ContributeCorpus = options.ContributeCorpus }

            let! result = client.Generate args

            match result with
            | TurnResult.Produced(treeJson, _, _) -> currentTreeJson <- Some treeJson
            | _ -> ()

            return result
        }

    /// Run the next turn from just a prompt (the common case).
    member this.Next(prompt: string) : Async<TurnResult> =
        this.NextWith(prompt, SessionTurnOptions.none)

    /// Forget the held tree — the next turn is a fresh generation again.
    member _.Reset() : unit = currentTreeJson <- None
