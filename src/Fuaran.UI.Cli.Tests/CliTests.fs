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

/// The emitted fsharp-fable scaffold is a STRING, so the compiler never checks
/// it — these invariants are the only thing standing between a template typo and
/// a broken-on-arrival scaffold. (Both were live defects once: the template used
/// a `BindingResolver.BindingSources.empty` that does not exist, and wired the
/// .NET-only `Fuaran.UI.Client` into a Fable panel.)
[<Tests>]
let scaffoldTemplateTests =
    testList
        "Fuaran.UI.Cli.Scaffold — emitted-template invariants"
        [ test "uses the real empty BindingSources value" {
              Expect.stringContains Scaffold.fsharpFablePanel "BindingResolver.empty" "the module-level `empty` let"

              Expect.isFalse
                  (Scaffold.fsharpFablePanel.Contains "BindingResolver.BindingSources.empty")
                  "there is no `BindingSources` module — that spelling does not compile"
          }

          test "the Fable panel never references the .NET-only client package" {
              // Fuaran.UI.Client opens System.Net.Http and is not source-packed for
              // Fable; a browser panel must speak the wire contract directly.
              for forbidden in [ "Fuaran.UI.Client"; "FuaranClient"; "FuaranSession" ] do
                  Expect.isFalse
                      (Scaffold.fsharpFablePanel.Contains forbidden)
                      $"the Fable panel must not reference {forbidden}"
          }

          test "wires the canonical decode + render path" {
              for required in
                  [ "open Fuaran.UI.Ops"
                    "open Fuaran.UI.Renderer"
                    "JsonDecode.decodeNodeObj"
                    "Render.renderWithSources" ] do
                  Expect.stringContains Scaffold.fsharpFablePanel required "canonical decode/render wiring"
          } ]

/// Phase 1816 — `fuaran scaffold form --schema <file>`. It prints
/// `SchemaForm.deriveWireFromText` for the file's text and nothing else; the
/// AI-tool side of the same byte pin lives in the AiTools tests.
[<Tests>]
let scaffoldFormTests =
    let schema =
        """{"type":"object","required":["name"],"properties":{"name":{"type":"string"},"size":{"enum":["s","m"]}}}"""

    testList
        "scaffold form (Phase 1816)"
        [ test "prints the derivation's canonical bytes, exit 0" {
              let path = writeTemp $"fuaran-schema-{System.Guid.NewGuid():N}.json" schema
              let code, out = Commands.scaffoldForm [ "--schema"; path ]
              File.Delete path
              Expect.equal code 0 "exit"

              match
                  Fuaran.UI.SchemaForm.deriveWireFromText Fuaran.UI.SchemaForm.SchemaFormOptions.defaults<obj> schema
              with
              | Ok json -> Expect.equal out (json + "\n") "same bytes as the derivation (and so as the AI tool)"
              | Error e -> failtestf "%s" e
          }
          test "a refused schema prints the refusal envelope, exit 1" {
              let path =
                  writeTemp
                      $"fuaran-schema-{System.Guid.NewGuid():N}.json"
                      """{"type":"object","properties":{"a":{"anyOf":[]}}}"""

              let code, out = Commands.scaffoldForm [ "--schema"; path ]
              File.Delete path
              Expect.equal code 1 "exit"
              Expect.stringContains out "\"path\":\"/properties/a/anyOf\"" "names the path"
          }
          test "--schema is required, and a missing file is named" {
              Expect.equal (fst (Commands.scaffoldForm [])) 2 "no --schema"
              Expect.equal (fst (Commands.scaffoldForm [ "--schema"; "no-such-file.json" ])) 2 "missing file"
          }
          test "dispatch routes scaffold form" {
              let path = writeTemp $"fuaran-schema-{System.Guid.NewGuid():N}.json" schema

              let code, out =
                  Commands.dispatch [ "scaffold"; "form"; "--schema"; path; "--form-id"; "f1" ]

              File.Delete path
              Expect.equal code 0 "exit"
              Expect.stringContains out "\"id\":\"f1\"" "form id flag"
          } ]
