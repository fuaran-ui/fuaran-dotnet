// The Elmish integration — the `Msg` + `update` arm that holds the tree.
//
// This module is the reusable half of the F# adapter, and it is deliberately
// PORTABLE: no Fable, no Elmish, no HTTP. `update` is a pure function that
// returns the next model plus, when a turn is wanted, a `TurnRequest` for the
// host to run. The host (App.fs under Fable, a test on .NET) supplies the
// effect. That keeps the turn-loop logic — the part worth getting right —
// unit-testable on the .NET side, while the browser half stays a thin edge.
//
// The load-bearing behaviour: `Submit` carries the CURRENT tree into the
// request, so the first prompt is a fresh generation and every prompt after it
// is a cheap repair diff; and a failed turn leaves the held tree untouched, so
// the user's last good UI keeps rendering and the same repair can be retried.

module Fuaran.Sample.SdkIntegration.Client.Turn

/// Where the loop is in the turn cycle.
[<RequireQualifiedAccess>]
type Status =
    | Idle
    | Generating
    | Ready
    | Error of message: string

type Model =
    {
        /// The prompt currently being typed.
        Prompt: string
        /// Canonical wire JSON of the tree being held — what the next turn repairs.
        TreeJson: string option
        Status: Status
    }

type Msg =
    | SetPrompt of string
    | Submit
    | Produced of treeJson: string
    | TurnFailed of message: string
    | Reset

/// What the host must POST to the proxy route when `update` asks for a turn.
type TurnRequest =
    { Prompt: string
      CurrentTreeJson: string option }

let init () : Model =
    { Prompt = ""
      TreeJson = None
      Status = Status.Idle }

/// The Elmish arm. Returns the next model and, when a turn should be issued,
/// the request the host runs (dispatching `Produced` / `TurnFailed` with the
/// outcome).
let update (msg: Msg) (model: Model) : Model * TurnRequest option =
    match msg with
    | SetPrompt prompt -> { model with Prompt = prompt }, None

    | Submit ->
        // Ignore an empty prompt, and never issue a second turn while one is in
        // flight (the user can still see the held tree meanwhile).
        if model.Prompt.Trim() = "" || model.Status = Status.Generating then
            model, None
        else
            { model with
                Status = Status.Generating },
            Some
                { Prompt = model.Prompt
                  // Carrying the held tree is what makes this a repair diff.
                  CurrentTreeJson = model.TreeJson }

    | Produced treeJson ->
        { model with
            TreeJson = Some treeJson
            Status = Status.Ready },
        None

    | TurnFailed message ->
        // The held tree is deliberately NOT cleared — the last good UI stays on
        // screen and the caller can retry the same repair.
        { model with
            Status = Status.Error message },
        None

    | Reset ->
        { model with
            TreeJson = None
            Status = Status.Idle },
        None
