// Fuaran.UI.Client — the typed repair loop.
//
// The endpoint already returns a recoverable envelope (stage + code + a
// model-facing message that, for the Apply stage, carries the §3.6 apply-error
// hint). The closed loop that distinguishes Fuaran from a raw LLM call is
// "emit -> apply -> recover -> re-emit": on a repairable failure, thread the
// hint back into the next turn so the emission self-corrects. This module makes
// that a first-class, one-call SDK affordance instead of manual plumbing, with a
// caller-set bound so it never loops forever.
//
// Parity: `fuaran-ts/packages/client/src/repair.ts` mirrors `isRepairable`, the
// `threadHint` prompt augmentation (byte-identical marker text), and the bounded
// `generateWithRepair` loop.

namespace Fuaran.UI.Client

[<RequireQualifiedAccess>]
module Repair =

    /// The marker prefixing a threaded repair hint in the next prompt — kept
    /// byte-identical to the TypeScript client so parity fixtures match.
    [<Literal>]
    let HintMarker = "[repair]"

    /// A failure is *repairable* when re-emitting against its hint can plausibly
    /// fix it: the `Apply` stage (the emission addressed a non-existent node /
    /// the §3.6 default-deny gate) and the `Parse` stage (a malformed emission).
    /// An `AccessToken` failure (bad credential) and a `Provider` failure
    /// (transport / provider error) are terminal — retrying the same call will
    /// not help, so the loop surfaces them immediately.
    let isRepairable (stage: TurnStage) : bool =
        match stage with
        | TurnStage.Apply
        | TurnStage.Parse -> true
        | TurnStage.AccessToken
        | TurnStage.Provider -> false

    /// Thread a recoverable error's hint into the next turn's prompt. The
    /// original prompt is preserved and the current tree carried, so the turn
    /// stays a repair; the model sees the stage, code, and the (§3.6) hint it
    /// must re-emit against.
    let threadHint (args: GenerateArgs) (error: RecoverableError) : GenerateArgs =
        let augmented =
            $"{args.Prompt}\n\n{HintMarker} the previous emission was rejected at the {TurnStage.label error.Stage} stage ({error.Code}). Re-emit against this: {error.Message}"

        { args with Prompt = augmented }

    /// Run a generation turn with the closed repair loop. On a repairable
    /// `TurnFailed`, re-issue the turn with the hint threaded in, up to
    /// `maxRetries` times; a `Produced` / `AccessDenied` / terminal failure ends
    /// the loop immediately, and the final envelope is surfaced when the retries
    /// are exhausted. `maxRetries = 0` is a single attempt (no repair).
    let generateWithRepair (client: FuaranClient) (args: GenerateArgs) (maxRetries: int) : Async<TurnResult> =
        let rec loop (attemptArgs: GenerateArgs) (remaining: int) =
            async {
                let! result = client.Generate attemptArgs

                match result with
                | TurnResult.TurnFailed err when remaining > 0 && isRepairable err.Stage ->
                    return! loop (threadHint attemptArgs err) (remaining - 1)
                | _ -> return result
            }

        loop args (max 0 maxRetries)
