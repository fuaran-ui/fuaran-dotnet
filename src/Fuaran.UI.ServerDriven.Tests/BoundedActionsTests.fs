module Fuaran.UI.ServerDriven.Tests.BoundedActionsTests

// ─── Phase 153 (Wave 20): the bounded-Action interpreter ───────────
//
// `runBoundedAction` drives an AI-emitted, wire-decoded tree's state store with
// NO hand-authored `update` and NO `'Msg`. These tests pin the bounded subset
// (SetState is the only mutation; Navigate / clipboard / file-read are
// closure-free ClientEffects; Notify / AiTool / Dispatch / Call / CommitLocal
// are no-ops) and — load-bearing — the SAFETY PROPERTY: the interpreter never
// invokes a closure carried by an Action, so a server driving a *generated*
// tree has no arbitrary-code-execution surface.

open Expecto
open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.ServerDriven
open Fuaran.UI.Renderer.BindingResolver

/// Non-null box (F# 10 nullness: `box` yields `objnull`;
/// `Map<string, obj>` wants non-null — same `boxNN` posture as TreeOpDiff).
let private o (v: 'T) : obj = box v |> Unchecked.nonNull

/// The empty per-connection store (an empty `BindingSources`).
let private store0: BoundedStore = empty

/// Scalar → `JVal` for test payloads. Numbers land in the bounded store as
/// floats (JSON-number semantics — the store holds the lowered wire population).
let private jv (v: obj) : JVal =
    match v with
    | :? int as i -> JInt i
    | :? string as s -> JStr s
    | :? bool as b -> JBool b
    | :? float as f -> JFloat f
    | other -> failwith (sprintf "jv: unsupported test payload %A" other)

[<Tests>]
let tests =
    testList
        "Phase 153 — bounded-action interpreter"
        [ test "SetState writes the State channel; no client effect" {
              let out =
                  BoundedActions.runBoundedAction "n" (Action.SetState("count", jv 3)) store0

              Expect.equal
                  out.Store.State
                  (Map.ofList [ "count", o 3.0 ])
                  "State['count'] = 3 (a JSON number lowers to float)"

              Expect.isEmpty out.Effects "SetState emits no client effect"
          }

          test "SetState overwrites an existing key" {
              let s1 =
                  { store0 with
                      State = Map.ofList [ "mode", o "cash" ] }

              let out =
                  BoundedActions.runBoundedAction "n" (Action.SetState("mode", jv "real")) s1

              Expect.equal out.Store.State (Map.ofList [ "mode", o "real" ]) "mode overwritten to 'real'"
          }

          test "Navigate → closure-free ClientEffect; store unchanged" {
              let out = BoundedActions.runBoundedAction "n" (Action.Navigate "/next") store0
              Expect.equal out.Effects [ ClientEffect.Navigate "/next" ] "one Navigate effect"
              Expect.equal out.Store.State store0.State "store unchanged"
          }

          test "WriteToClipboard → closure-free ClientEffect" {
              let out =
                  BoundedActions.runBoundedAction "n" (Action.WriteToClipboard "copied") store0

              Expect.equal out.Effects [ ClientEffect.WriteToClipboard "copied" ] "one clipboard effect"
          }

          test "ReadFileBody → node-addressed ClientEffect; onRead never invoked" {
              let mutable invoked = false

              let onRead (_: string) : obj =
                  invoked <- true
                  o "<closure>"

              let action = Action.ReadFileBody("f1", None, FileReadEncoding.Text, Some onRead)

              let out = BoundedActions.runBoundedAction "upload" action store0
              Expect.equal out.Effects [ ClientEffect.ReadFileBody("upload", "Text") ] "node-addressed read effect"
              Expect.isFalse invoked "the onRead closure must NOT be invoked server-side"
          }

          test "Chain threads the store and concatenates effects in order" {
              let action =
                  Action.Chain
                      [ Action.SetState("a", jv 1)
                        Action.Navigate "/go"
                        Action.SetState("b", jv 2) ]

              let out = BoundedActions.runBoundedAction "n" action store0

              Expect.equal
                  out.Store.State
                  (Map.ofList [ "a", o 1.0; "b", o 2.0 ])
                  "both SetStateS applied (JSON numbers lower to float)"

              Expect.equal out.Effects [ ClientEffect.Navigate "/go" ] "Navigate effect preserved in order"
          }

          test "Notify / AiTool / Dispatch / CommitLocal are no-ops with a readable diagnostic" {
              let s1 =
                  { store0 with
                      State = Map.ofList [ "k", o 1 ] }

              for action in
                  [ Action.Notify("ch", jv "p")
                    Action.AiTool("tool", jv "args")
                    Action.Dispatch(o "msg")
                    Action.CommitLocal "field" ] do
                  let out = BoundedActions.runBoundedAction "n" action s1
                  Expect.equal out.Store.State s1.State "store unchanged"
                  Expect.isEmpty out.Effects "no client effect"

                  // Phase 212 — the no-op is observable: one diagnostic naming
                  // the inert action, with a non-empty human-readable describe.
                  match out.Diagnostics with
                  | [ BoundedDiagnostic.UnsupportedOnBoundedPath(nodeId, _) as d ] ->
                      Expect.equal nodeId "n" "diagnostic names the originating node"
                      Expect.isNotEmpty (BoundedDiagnostic.describe d) "describe is readable"
                  | other -> failtestf "expected one UnsupportedOnBoundedPath diagnostic, got %A" other
          }

          test "SetState / Navigate emit no diagnostic (only the no-op arms do)" {
              let setOut = BoundedActions.runBoundedAction "n" (Action.SetState("k", jv 1)) store0
              Expect.isEmpty setOut.Diagnostics "SetState is a real mutation — no diagnostic"
              let navOut = BoundedActions.runBoundedAction "n" (Action.Navigate "/x") store0
              Expect.isEmpty navOut.Diagnostics "Navigate has a client effect — no diagnostic"
          }

          // ── The load-bearing safety property (Phase 153) ──────────────────
          test "SAFETY: a Call's onResult closure is never invoked (no ACE surface)" {
              let mutable invoked = false

              let onResult (_: obj) : obj =
                  invoked <- true
                  o "<closure>"

              let out =
                  BoundedActions.runBoundedAction "n" (Action.Call("https://evil", Some onResult, None)) store0

              Expect.isFalse invoked "the Call onResult closure must NEVER execute on the bounded path"
              Expect.equal out.Store.State store0.State "Call is a store-level no-op"
              Expect.isEmpty out.Effects "Call emits no client effect on the bounded path"
          }

          test "SAFETY: closures buried inside a Chain are never invoked" {
              let mutable calls = 0

              let throwing (_: obj) : obj =
                  calls <- calls + 1
                  failwith "closure executed!"

              let throwingRead (_: string) : obj =
                  calls <- calls + 1
                  failwith "closure executed!"

              let action =
                  Action.Chain
                      [ Action.SetState("ok", jv 1)
                        Action.Call("e", Some throwing, None)
                        Action.ReadFileBody("f", None, FileReadEncoding.Base64, Some throwingRead) ]

              // The whole interpretation must complete without invoking any closure.
              let out = BoundedActions.runBoundedAction "up" action store0
              Expect.equal calls 0 "NO closure carried by any chained action was invoked"
              Expect.equal out.Store.State (Map.ofList [ "ok", o 1.0 ]) "the SetState still applied"
              Expect.equal out.Effects [ ClientEffect.ReadFileBody("up", "Base64") ] "only the closure-free read effect"
          }

          // ─── Phase 782 — the server EFFECT path is a URL sink too ───────────
          //
          // A `ClientEffect.Navigate` is performed by the shim with whatever
          // router the host wired, so an unsafe route emitted here lands on the
          // client exactly as if the client had produced it. Sanitising only the
          // client action path would have left this half open.
          test "a javascript: route is neutralised on the server effect path" {
              let unsafeRoutes =
                  [ "javascript:alert(1)"
                    "JaVaScRiPt:alert(1)"
                    "vbscript:msgbox(1)"
                    "//evil.example/x" ]

              for route in unsafeRoutes do
                  let out = BoundedActions.runBoundedAction "n" (Action.Navigate route) store0

                  Expect.isEmpty out.Effects (sprintf "'%s' emits NO client effect" route)

                  Expect.equal out.Diagnostics.Length 1 (sprintf "'%s' refusal is recorded, not silent" route)

                  match out.Diagnostics with
                  | [ BoundedDiagnostic.Refused(nodeId, _, reason) ] ->
                      Expect.equal nodeId "n" "the diagnostic names the originating node"
                      Expect.stringContains reason "safe URL" "the diagnostic says why"
                  | other -> failtestf "expected a Refused diagnostic, got %A" other

              // A legitimate route still ships, sanitised.
              let ok = BoundedActions.runBoundedAction "n" (Action.Navigate "  /next  ") store0
              Expect.equal ok.Effects [ ClientEffect.Navigate "/next" ] "a safe route ships trimmed"
              Expect.isEmpty ok.Diagnostics "no diagnostic for a safe route"
          }

          test "a host-reserved State key is refused on the bounded path" {
              let out =
                  BoundedActions.runBoundedAction "n" (Action.SetState("host.session-token", jv "stolen")) store0

              Expect.equal out.Store.State store0.State "the host-reserved slot is NOT written"

              match out.Diagnostics with
              | [ BoundedDiagnostic.Refused(_, _, reason) ] ->
                  Expect.stringContains reason "host-reserved" "the diagnostic names the namespace"
              | other -> failtestf "expected a Refused diagnostic, got %A" other

              // Ordinary keys are unaffected — this is a namespace, not a ban.
              let ok =
                  BoundedActions.runBoundedAction "n" (Action.SetState("theme", jv "dark")) store0

              Expect.equal ok.Store.State (Map.ofList [ "theme", o "dark" ]) "an ordinary key writes normally"
              Expect.isEmpty ok.Diagnostics "no diagnostic for an ordinary key"
          } ]
