// Fuaran.UI.Cli — the command core.
//
// Each command returns (exitCode, output) so the same core drives the `fuaran`
// bin AND the tests (no process spawning). `generate` / `validate` run over the
// public F# surfaces (Fuaran.UI.Client / Fuaran.UI.Ops), sharing the wire
// substrate with the npm @fuaran-ui/cli so both front-ends behave identically.

module Fuaran.UI.Cli.Commands

open System
open System.IO
open Fuaran.UI.Client
open Fuaran.UI.Ops

let usage =
    """fuaran — the Fuaran generative-UI CLI (dotnet tool)

Usage:
  fuaran generate <prompt> [--tree <file>] [--mock [url]]   Prompt -> a canonical tree
  fuaran validate <file>                                    Wire JSON -> pass/fail + diagnostics
  fuaran scaffold --target ts|fsharp                        Integration boilerplate
  fuaran recipe <query>                                     (served by @fuaran-ui/cli / the MCP)

Secrets: FUARAN_ENDPOINT / FUARAN_ACCESS_TOKEN / FUARAN_PROVIDER_KEY are read from
the environment (never a flag, never printed). --mock needs no secret."""

/// Value of `--name value`, or None.
let flagValue (name: string) (args: string list) : string option =
    let rec loop =
        function
        | a :: v :: _ when a = name -> Some v
        | _ :: rest -> loop rest
        | [] -> None

    loop args

let hasFlag (name: string) (args: string list) = List.contains name args

/// Positionals: args that are not a flag or a flag's value.
let positionals (flagsWithValue: string list) (args: string list) : string list =
    let rec loop acc (items: string list) =
        match items with
        | (a: string) :: rest when a.StartsWith "--" ->
            if List.contains a flagsWithValue then
                match rest with
                | _ :: more -> loop acc more // skip the flag's value
                | [] -> List.rev acc
            else
                loop acc rest
        | a :: rest -> loop (a :: acc) rest
        | [] -> List.rev acc

    loop [] args

let private envOpt (name: string) =
    match Environment.GetEnvironmentVariable name with
    | null -> None
    | "" -> None
    | v -> Some v

let generate (args: string list) : int * string =
    let prompt =
        positionals [ "--tree"; "--mock" ] args
        |> String.concat " "
        |> (fun s -> s.Trim())

    if prompt = "" then
        2, "generate: a prompt is required.\n"
    else
        let currentTreeJson = flagValue "--tree" args |> Option.map File.ReadAllText

        let args' =
            { GenerateArgs.prompt prompt with
                CurrentTreeJson = currentTreeJson }

        let client =
            if hasFlag "--mock" args then
                let url = flagValue "--mock" args |> Option.defaultValue "http://127.0.0.1:8123"
                Ok(FuaranClient(FuaranClientConfig.create url))
            else
                match envOpt "FUARAN_ENDPOINT" with
                | None -> Error "not configured — set FUARAN_ENDPOINT (or use --mock for offline)\n"
                | Some endpoint ->
                    Ok(
                        FuaranClient(
                            { FuaranClientConfig.create endpoint with
                                AccessToken = envOpt "FUARAN_ACCESS_TOKEN"
                                ProviderKey = envOpt "FUARAN_PROVIDER_KEY" }
                        )
                    )

        match client with
        | Error msg -> 2, msg
        | Ok c ->
            match c.Generate args' |> Async.RunSynchronously with
            | TurnResult.Produced(treeJson, _, _) -> 0, treeJson + "\n"
            | TurnResult.AccessDenied reason -> 1, $"access denied: {reason}\n"
            | TurnResult.TurnFailed err -> 1, $"turn failed [{TurnStage.label err.Stage}/{err.Code}]: {err.Message}\n"

let validate (args: string list) : int * string =
    match positionals [] args with
    | [] -> 2, "validate: a file is required.\n"
    | file :: _ ->
        let json = File.ReadAllText file

        match JsonDecode.decodeNode json with
        | Ok _ -> 0, "valid (node)\n"
        | Error e -> 1, $"invalid (node):\n  {e.Code} at {e.Path}: {e.Message}\n"

let scaffold (args: string list) : int * string =
    match flagValue "--target" args with
    | Some "fsharp"
    | Some "fsharp-fable" ->
        0,
        $"# scaffold: fsharp-fable (server-proxied)\n\n// ==== src/FuaranPanel.fs ====\n{Scaffold.fsharpFablePanel}\nNotes:\n  - {Scaffold.proxyNote}\n"
    | Some "ts"
    | Some "ts-react" ->
        0,
        "The ts-react scaffold is served by the npm CLI (single-sourced with the MCP):\n  npx @fuaran-ui/cli scaffold --target ts\n"
    | _ -> 2, "scaffold: --target ts|fsharp is required.\n"

let recipe (_args: string list) : int * string =
    0,
    "The recipe bank is single-sourced in the TS tier (shared with the MCP recipe tool):\n  npx @fuaran-ui/cli recipe <query>\n"

/// Dispatch an argv list to (exitCode, output).
let dispatch (argv: string list) : int * string =
    match argv with
    | "generate" :: rest -> generate rest
    | "validate" :: rest -> validate rest
    | "scaffold" :: rest -> scaffold rest
    | "recipe" :: rest -> recipe rest
    | []
    | "help" :: _
    | "--help" :: _
    | "-h" :: _ -> 0, usage + "\n"
    | other :: _ -> 2, $"unknown command \"{other}\".\n\n{usage}\n"
