module Fuaran.UI.Tests.ClipboardActionTests

#nowarn "3261"

open Expecto
open Fuaran.UI
open Fuaran.UI.Types

// ============================================================================
//  Typed-surface tests for `Action.WriteToClipboard`.
//
//  Covers the .NET-side constructable / pattern-matchable surface:
//   - The DU case carries its literal-string payload.
//   - `Action.Chain` composes `WriteToClipboard` with a follow-on
//     `Action.Dispatch` (the canonical "copy + tell the model" shape
//     a share-link copy button migration adopts).
//   - The diagnostic runtime's `WriteToClipboard` no-ops without
//     throwing — proves the .NET-side fallback path stays loud (via
//     eprintfn) but does not surface as an exception consumers must
//     catch.
//
//  Renderer-side behaviour (navigator.clipboard.writeText dispatch +
//  execCommand fallback) is exercised in the catalog's Playwright spec
//  (`samples/catalog/snapshot/clipboard.spec.mts`) — those need a real
//  browser to grant the clipboard permission and read back through
//  `navigator.clipboard.readText`.
// ============================================================================

type private Msg = ClipboardCopied of string

[<Tests>]
let tests =
    testList
        "Fuaran.UI.Action.WriteToClipboard"
        [ test "Action.WriteToClipboard preserves its literal payload" {
              let action: Action<Msg> = Action.WriteToClipboard "https://example.com/share/abc123"

              match action with
              | Action.WriteToClipboard text ->
                  Expect.equal text "https://example.com/share/abc123" "Literal payload preserved"
              | other -> failtestf "Expected Action.WriteToClipboard, got %A" other
          }

          test "Action.Chain composes WriteToClipboard with a follow-on Dispatch" {
              // The canonical "copy + tell the model" shape — matches a
              // share-link copy-button migration target.
              let action: Action<Msg> =
                  Action.Chain
                      [ Action.WriteToClipboard "https://example.com/share/abc123"
                        Action.dispatch (ClipboardCopied "https://example.com/share/abc123") ]

              match action with
              | Action.Chain [ Action.WriteToClipboard text; Action.Dispatch(ClipboardCopied notified) ] ->
                  Expect.equal text "https://example.com/share/abc123" "Clipboard payload preserved in chain"
                  Expect.equal notified text "Follow-on Dispatch sees the same payload"
              | other -> failtestf "Expected 2-step chain, got %A" other
          }

          test "DiagnosticRuntime.WriteToClipboard does not throw" {
              // The .NET-side default substrate is `eprintfn` — proves
              // the renderer-tier dispatch path stays loud without
              // surfacing as an exception consumers must catch.
              let runtime = Fuaran.UI.Renderer.Runtime.diagnostic
              runtime.WriteToClipboard("Hello from Fuaran")
          } ]
