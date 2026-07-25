// Fuaran.UI.Cli — the `fuaran` dotnet tool.
//
// The F# front-end of the Fuaran CLI: `fuaran generate | validate | scaffold`
// over the public F# surfaces, zero MCP config. A thin wrapper over the shared
// `Commands.dispatch` core (which the tests also drive). Secret hygiene: the
// access token + BYOK key are read from the environment only, never a flag,
// never printed; `--mock` needs no secret.

module Fuaran.UI.Cli.Program

open System

[<EntryPoint>]
let main argv =
    let code, out = Commands.dispatch (List.ofArray argv)
    Console.Out.Write out
    code
