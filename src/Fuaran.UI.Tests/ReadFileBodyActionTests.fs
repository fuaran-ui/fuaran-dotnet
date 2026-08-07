module Fuaran.UI.Tests.ReadFileBodyActionTests

#nowarn "3261"

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Runtime

// ============================================================================
//  Typed-surface tests for `Action.ReadFileBody` (Phase 136).
//
//  Covers the .NET-side constructable / pattern-matchable surface + the
//  dispatch-policy-gate interaction (the same `Render.applyDispatchGate`
//  path `runAction` drives):
//   - The DU case carries its `FileRef` + `FileReadEncoding` + continuation.
//   - The `Action.readFileBody` smart ctor builds the same case.
//   - `Action.Chain` composes it with a follow-on dispatch.
//   - The diagnostic runtime's `ReadFileBody` no-ops without throwing (and
//     never fires `onRead`, since there is no browser blob).
//   - The gate descriptor `ActionDescriptor.ReadFileBody` is allowed by the
//     default runtimes and refusable by a deny-by-shape host.
//
//  Renderer-side behaviour (the FileReader read + base64/data-URL/text
//  projection) needs a real browser and is exercised by the catalog's
//  Playwright surface, not here.
// ============================================================================

type private Msg = WorkbookLoaded of string

let private aRef: FileRef =
    { Id = "workbook-upload:0"
      Handle = None }

[<Tests>]
let tests =
    testList
        "Fuaran.UI.Action.ReadFileBody"
        [ test "Action.ReadFileBody carries its FileRef + encoding" {
              let action: Action<Msg> =
                  Action.ReadFileBody(aRef.Id, aRef.Handle, FileReadEncoding.Base64, Some WorkbookLoaded)

              match action with
              | Action.ReadFileBody(fileRef, fileHandle, encoding, Some onRead) ->
                  Expect.equal fileRef "workbook-upload:0" "FileRef id preserved"
                  Expect.equal fileHandle None "Handle is None on a hand-built ref"
                  Expect.equal encoding FileReadEncoding.Base64 "Encoding preserved"
                  Expect.equal (onRead "abc==") (WorkbookLoaded "abc==") "Continuation maps the body into a msg"
              | other -> failtestf "Expected Action.ReadFileBody, got %A" other
          }

          test "Action.readFileBody smart ctor builds the same case" {
              let action: Action<Msg> =
                  Action.readFileBody aRef FileReadEncoding.Text WorkbookLoaded

              match action with
              | Action.ReadFileBody(fileRef, _, FileReadEncoding.Text, _) ->
                  Expect.equal fileRef "workbook-upload:0" "Smart ctor preserves the ref id"
              | other -> failtestf "Expected Action.ReadFileBody (Text), got %A" other
          }

          test "Action.Chain composes ReadFileBody with a follow-on Dispatch" {
              let action: Action<Msg> =
                  Action.Chain
                      [ Action.ReadFileBody(aRef.Id, aRef.Handle, FileReadEncoding.DataUrl, Some WorkbookLoaded)
                        Action.dispatch (WorkbookLoaded "sentinel") ]

              match action with
              | Action.Chain [ Action.ReadFileBody(_, _, FileReadEncoding.DataUrl, _); Action.Dispatch(WorkbookLoaded _) ] ->
                  ()
              | other -> failtestf "Expected 2-step chain, got %A" other
          }

          test "DiagnosticRuntime.ReadFileBody does not throw and does not fire onRead" {
              // The .NET-side default substrate is `eprintfn` — proves the
              // renderer-tier dispatch path stays loud without surfacing as an
              // exception, and that onRead is never invoked when no browser
              // blob is present.
              let runtime = Fuaran.UI.Renderer.Runtime.diagnostic
              let mutable fired = 0
              runtime.ReadFileBody(aRef, FileReadEncoding.Base64, (fun _ -> fired <- fired + 1))
              Expect.equal fired 0 "onRead not fired by the diagnostic runtime"
          }

          // Phase 782 inverted this: the default is DENY. Reading a
          // user-selected file's bytes on the say-so of a decoded tree is
          // exactly the capability an unconfigured host should not grant.
          test "default runtimes REFUSE the ReadFileBody gate descriptor" {
              Expect.isFalse
                  (Runtime.diagnostic.CanDispatch(ActionDescriptor.ReadFileBody "workbook-upload:0"))
                  "diagnostic refuses ReadFileBody"

              Expect.isFalse
                  ((MutableRuntime() :> IFuaranRuntime).CanDispatch(ActionDescriptor.ReadFileBody "f"))
                  "mutable refuses ReadFileBody"

              // …and the named opt-in restores the old posture.
              Expect.isTrue
                  (Runtime.permissive.CanDispatch(ActionDescriptor.ReadFileBody "workbook-upload:0"))
                  "the named permissive runtime allows ReadFileBody"

              Expect.isTrue
                  ((MutableRuntime.Permissive() :> IFuaranRuntime).CanDispatch(ActionDescriptor.ReadFileBody "f"))
                  "MutableRuntime.Permissive allows ReadFileBody"
          }

          test "the gate descriptor labels the file id for a denied read" {
              Expect.equal
                  (ActionDescriptor.describe (ActionDescriptor.ReadFileBody "workbook-upload:0"))
                  "ReadFileBody(workbook-upload:0)"
                  "describe names the descriptor + file id"
          } ]
