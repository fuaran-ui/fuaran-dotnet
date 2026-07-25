// Fuaran.UI.Cli tests — the command core over the public F# surfaces. Mirrors
// the npm @fuaran-ui/cli test surface (validate / scaffold / dispatch), so the
// two front-ends are checked to the same behaviour.

module Fuaran.UI.Cli.Tests.CliTests

open System.IO
open Expecto

open Fuaran.UI.Cli

let private writeTemp (name: string) (contents: string) : string =
    let path = Path.Combine(Path.GetTempPath(), name)
    File.WriteAllText(path, contents)
    path

let private validTree =
    """{"id":"badge-1","kind":{"$type":"Badge","label":"Beta","variant":"Info"}}"""

[<Tests>]
let cliTests =
    testList
        "Fuaran.UI.Cli.Commands"
        [ test "validate passes a canonical tree" {
              let file = writeTemp "fuaran-cli-good.json" validTree
              let code, out = Commands.validate [ file ]
              Expect.equal code 0 "exit 0"
              Expect.stringContains out "valid" "reports valid"
          }

          test "validate fails a malformed tree with a diagnostic" {
              let file = writeTemp "fuaran-cli-bad.json" """{"id":"x"}"""
              let code, out = Commands.validate [ file ]
              Expect.equal code 1 "exit 1"
              Expect.stringContains out "invalid" "reports invalid"
          }

          test "validate requires a file" {
              let code, _ = Commands.validate []
              Expect.equal code 2 "usage error"
          }

          test "scaffold emits the fsharp-fable target" {
              let code, out = Commands.scaffold [ "--target"; "fsharp" ]
              Expect.equal code 0 "exit 0"
              Expect.stringContains out "fsharp-fable" "names the target"
              Expect.stringContains out "FuaranPanel" "emits the panel"
          }

          test "scaffold points ts to the npm CLI (single-sourced)" {
              let code, out = Commands.scaffold [ "--target"; "ts" ]
              Expect.equal code 0 "exit 0"
              Expect.stringContains out "@fuaran-ui/cli" "delegates to the npm CLI"
          }

          test "scaffold requires a target" {
              let code, _ = Commands.scaffold []
              Expect.equal code 2 "usage error"
          }

          test "generate without config or --mock is not-configured" {
              // No FUARAN_ENDPOINT in the test env and no --mock ⇒ a clean usage error,
              // never a crash and never a leaked secret.
              let code, out = Commands.generate [ "make a form" ]
              Expect.equal code 2 "not-configured exit"
              Expect.stringContains out "FUARAN_ENDPOINT" "names the missing config"
          }

          test "dispatch routes commands and help" {
              Expect.equal (fst (Commands.dispatch [])) 0 "no args → help"
              Expect.stringContains (snd (Commands.dispatch [ "help" ])) "Usage:" "help text"
              Expect.equal (fst (Commands.dispatch [ "bogus" ])) 2 "unknown command"
          }

          test "positionals skip flags and their values" {
              let got =
                  Commands.positionals [ "--tree"; "--mock" ] [ "a"; "prompt"; "--mock"; "url"; "--tree"; "f.json" ]

              Expect.equal got [ "a"; "prompt" ] "only the prompt words remain"
          } ]
