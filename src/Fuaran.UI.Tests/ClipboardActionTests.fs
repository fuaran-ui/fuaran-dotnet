module Fuaran.UI.Tests.ClipboardActionTests

// Phase 1152 — `Action.Dispatch` carries the IDL's `inProcessOnly` marking, which
// the generator renders as `[<Obsolete(…, false)>]`: FS0044 at every mention, and
// an error under this repo's `TreatWarningsAsErrors`. File-scoped rather than
// per-declaration because the mentions sit INSIDE `testList` expressions, where a
// lexical directive cannot be placed — this is the tightest form the file can
// express. A suite is not an authoring surface: these uses exist to PIN the marked
// case's behaviour, which is the one use the marking is not addressed to.
#nowarn "44"

#nowarn "3261"

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.Core

// ============================================================================
//  Typed-surface tests for `Action.WriteToClipboard`.
//
//  Covers the .NET-side constructable / pattern-matchable surface:
//   - The DU case carries its `TextSource` payload — literal AND bound
//     (Phase 1126 widened it from a bare string, so that a reader can copy
//     a value the tree computed rather than only one the author typed).
//   - `Action.Chain` composes `WriteToClipboard` with a follow-on
//     `Action.Dispatch` (the canonical "copy + tell the model" shape
//     a share-link copy button migration adopts).
//   - The wire is UNMOVED for a literal payload, and the pre-1126 bare-string
//     spelling still decodes — the two halves of the Phase 1126 compatibility
//     claim, asserted rather than asserted-about.
//   - The diagnostic runtime's `WriteToClipboard` no-ops without
//     throwing — proves the .NET-side fallback path stays loud (via
//     eprintfn) but does not surface as an exception consumers must
//     catch.
//
//  `TextSource` carries no structural equality (its `I18n` arm holds a `JVal`
//  bag), so payload assertions here destructure rather than compare.
//
//  Renderer-side behaviour (navigator.clipboard.writeText dispatch +
//  execCommand fallback) is exercised in the catalog's Playwright spec
//  (`samples/catalog/snapshot/clipboard.spec.mts`) — those need a real
//  browser to grant the clipboard permission and read back through
//  `navigator.clipboard.readText`.
// ============================================================================

type private Msg = ClipboardCopied of string

/// The literal a `TextSource` stands for, or `None` when it is not a literal.
let private literalOf (t: TextSource) : string option =
    match t with
    | TextSource.Literal s -> Some s
    | _ -> None

[<Tests>]
let tests =
    testList
        "Fuaran.UI.Action.WriteToClipboard"
        [ test "Action.WriteToClipboard preserves its literal payload" {
              let action: Action<Msg> =
                  Action.WriteToClipboard(TextSource.Literal "https://example.com/share/abc123")

              match action with
              | Action.WriteToClipboard text ->
                  Expect.equal (literalOf text) (Some "https://example.com/share/abc123") "Literal payload preserved"
              | other -> failtestf "Expected Action.WriteToClipboard, got %A" other
          }

          // Phase 1126 — the whole point of the widening: the payload may be a
          // binding, which the renderer resolves at dispatch time rather than at
          // decode time.
          test "Action.WriteToClipboard carries a BOUND payload" {
              let action: Action<Msg> =
                  Action.WriteToClipboard(TextSource.Bound(Binding.State("shareUrl", None)))

              match action with
              | Action.WriteToClipboard(TextSource.Bound(Binding.State(key, None))) ->
                  Expect.equal key "shareUrl" "Bound payload preserves its state key"
              | other -> failtestf "Expected a bound WriteToClipboard payload, got %A" other
          }

          test "Action.Chain composes WriteToClipboard with a follow-on Dispatch" {
              // The canonical "copy + tell the model" shape — matches a
              // share-link copy-button migration target.
              let action: Action<Msg> =
                  Action.Chain
                      [ Action.WriteToClipboard(TextSource.Literal "https://example.com/share/abc123")
                        Action.dispatch (ClipboardCopied "https://example.com/share/abc123") ]

              match action with
              | Action.Chain [ Action.WriteToClipboard text; Action.Dispatch(ClipboardCopied notified) ] ->
                  Expect.equal
                      (literalOf text)
                      (Some "https://example.com/share/abc123")
                      "Clipboard payload preserved in chain"

                  Expect.equal (Some notified) (literalOf text) "Follow-on Dispatch sees the same payload"
              | other -> failtestf "Expected 2-step chain, got %A" other
          }

          // ── The Phase 1126 compatibility claim, in two assertions ─────────
          //
          // STABILITY.md says the change is source-breaking and wire-neutral.
          // Both halves are asserted here rather than left to the corpus, so a
          // future edit to `encTextSource`'s transparent-literal rule fails in
          // the tier that made the claim.
          test "a literal payload encodes to the pre-1126 bytes" {
              let node: Node<obj> =
                  Fuaran.button
                      "b"
                      { Defaults.button<obj> with
                          Label = TextSource.Literal "Copy"
                          OnClick = Action.WriteToClipboard(TextSource.Literal "hello") }

              let json = Generated.encodeNode node

              Expect.stringContains
                  json
                  "{\"$type\":\"WriteToClipboard\",\"text\":\"hello\"}"
                  "A literal clipboard payload still encodes as the BARE string — the wire did not move"
          }

          test "the pre-1126 bare-string wire spelling still decodes" {
              let json =
                  "{\"id\":\"b\",\"kind\":{\"$type\":\"Button\",\"label\":\"Copy\","
                  + "\"onClick\":{\"$type\":\"WriteToClipboard\",\"text\":\"hello\"},\"variant\":\"Primary\"}}"

              match Generated.decodeNode json with
              | Ok node ->
                  match node.Kind with
                  | NodeKind.Button spec ->
                      match spec.OnClick with
                      | Action.WriteToClipboard text ->
                          Expect.equal
                              (literalOf text)
                              (Some "hello")
                              "A bare string upgrades to TextSource.Literal — no shipped document breaks"
                      | other -> failtestf "Expected WriteToClipboard, got %A" other
                  | other -> failtestf "Expected a Button, got %A" other
              | Error e -> failtestf "Expected the legacy spelling to decode, got: %s" e
          }

          test "a wrong-typed clipboard payload is REFUSED" {
              // Not merely "not a string" — `TextSource` accepts an object with a
              // known `$type` too, so the refusal has to name a value that is
              // neither. A number is the shape an emitter reaches for when it
              // believes the slot carries a count.
              let json =
                  "{\"id\":\"b\",\"kind\":{\"$type\":\"Button\",\"label\":\"Copy\","
                  + "\"onClick\":{\"$type\":\"WriteToClipboard\",\"text\":42},\"variant\":\"Primary\"}}"

              match Generated.decodeNode json with
              | Ok _ -> failtest "A numeric clipboard payload must not decode"
              | Error _ -> ()
          }

          test "DiagnosticRuntime.WriteToClipboard does not throw" {
              // The .NET-side default substrate is `eprintfn` — proves
              // the renderer-tier dispatch path stays loud without
              // surfacing as an exception consumers must catch.
              let runtime = Fuaran.UI.Renderer.Runtime.diagnostic
              runtime.WriteToClipboard("Hello from Fuaran")
          } ]
