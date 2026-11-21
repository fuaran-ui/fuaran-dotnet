/// Phase 2107 — `Action.Choose` and `Action.Require` on the bounded path. They lower
/// to the core's own `Choose` / `Require` (WIRE_FORMAT §30.1), so these tests pin
/// that the UI witness hands the core the right arms and that the core's semantics
/// reach a UI gesture unchanged: selection by the boolean `true`, a halting guard,
/// a condition read at DISPATCH time, and the exit assertion.
module Fuaran.UI.Program.Bounded.Tests.ControlFlowTests

open Expecto
open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Renderer.BindingResolver
open Fuaran.Program.Bounded
open Fuaran.UI.Program

let private o (v: 'T) : obj = box v |> Unchecked.nonNull

let private set (key: string) (value: JVal) : Action<obj> = Action.SetState(key, Some value, None)

let private state (key: string) : Binding<JVal> = Binding.State(key, None)

let private withState (pairs: (string * obj) list) : BoundedStore = { empty with State = Map.ofList pairs }

[<Tests>]
let tests =
    testList
        "Phase 2107 — Choose and Require on the bounded path"
        [ test "Choose runs whenTrue when the condition is the boolean true" {
              let action =
                  Action.Choose(state "flag", set "out" (JStr "yes"), Some(set "out" (JStr "no")), None)

              let out = BoundedActions.runBoundedAction "n" action (withState [ "flag", o true ])
              Expect.equal (Map.tryFind "out" out.Store.State) (Some(o "yes")) "the true arm ran"
              Expect.isFalse out.Halted "a selection that resolved does not halt"
          }

          test "Choose runs whenFalse for any other resolved value, and an absent whenFalse does nothing" {
              let both =
                  Action.Choose(state "flag", set "out" (JStr "yes"), Some(set "out" (JStr "no")), None)

              let out = BoundedActions.runBoundedAction "n" both (withState [ "flag", o "true" ])
              Expect.equal (Map.tryFind "out" out.Store.State) (Some(o "no")) "a STRING 'true' is not the boolean"

              let noElse = Action.Choose(state "flag", set "out" (JStr "yes"), None, None)

              let out2 =
                  BoundedActions.runBoundedAction "n" noElse (withState [ "flag", o false ])

              Expect.isNone (Map.tryFind "out" out2.Store.State) "nothing happens"
              Expect.isFalse out2.Halted "the empty arm is not a halt"
          }

          test "Choose reads its condition at DISPATCH time, so it sees an earlier member's write" {
              let action =
                  Action.Chain
                      [ set "flag" (JBool true)
                        Action.Choose(state "flag", set "out" (JStr "yes"), Some(set "out" (JStr "no")), None) ]

              let out = BoundedActions.runBoundedAction "n" action (withState [ "flag", o false ])
              Expect.equal (Map.tryFind "out" out.Store.State) (Some(o "yes")) "the write before it was read"
          }

          test "an unresolved condition halts before either arm runs" {
              let action =
                  Action.Choose(state "absent", set "out" (JStr "yes"), Some(set "out" (JStr "no")), None)

              let out = BoundedActions.runBoundedAction "n" action empty
              Expect.isTrue out.Halted "an unresolved entry halts"
              Expect.isNone (Map.tryFind "out" out.Store.State) "neither arm ran"
          }

          test "Require that holds changes nothing and the chain continues" {
              let action = Action.Chain [ Action.Require(state "ok"); set "after" (JInt 1) ]
              let out = BoundedActions.runBoundedAction "n" action (withState [ "ok", o true ])
              Expect.isFalse out.Halted "the guard held"
              Expect.isSome (Map.tryFind "after" out.Store.State) "the member after the guard ran"
          }

          test "Require that does not hold halts: nothing after it runs, what ran before it stays" {
              let action =
                  Action.Chain
                      [ set "before" (JInt 1)
                        Action.Require(state "ok")
                        set "after" (JInt 2)
                        Action.Navigate(TextSource.Literal "/next", NavigateTarget.Self) ]

              let out = BoundedActions.runBoundedAction "n" action (withState [ "ok", o false ])
              Expect.isTrue out.Halted "the guard halted the gesture"
              Expect.isSome (Map.tryFind "before" out.Store.State) "the write before the guard is kept"
              Expect.isNone (Map.tryFind "after" out.Store.State) "the write after the guard never ran"
              Expect.isEmpty out.Effects "the effect after the guard never ran"
          }

          test "an unresolved guard halts — a guard is never assumed to hold" {
              let action = Action.Chain [ Action.Require(state "absent"); set "after" (JInt 2) ]
              let out = BoundedActions.runBoundedAction "n" action empty
              Expect.isTrue out.Halted "unresolved halts"
              Expect.isNone (Map.tryFind "after" out.Store.State) "nothing after it ran"
          }

          test "the exit assertion is checked after the arm, and a violation halts keeping the arm's write" {
              // A toggle whose exit assertion is the flag itself: after the true arm
              // (which sets it false) the assertion must hold, and it does not.
              let toggle =
                  Action.Choose(
                      state "open",
                      set "open" (JBool false),
                      Some(set "open" (JBool true)),
                      Some(state "open")
                  )

              let out = BoundedActions.runBoundedAction "n" toggle (withState [ "open", o true ])
              Expect.isTrue out.Halted "the exit assertion did not hold after the true arm"
              Expect.equal (Map.tryFind "open" out.Store.State) (Some(o false)) "the arm's write is kept"

              // The reversible spelling: the exit is the NEGATION of the entry's
              // post-state, which a false arm leaves false and a true arm true.
              let reversible =
                  Action.Choose(
                      state "open",
                      set "open" (JBool true),
                      Some(set "open" (JBool false)),
                      Some(state "open")
                  )

              let out2 =
                  BoundedActions.runBoundedAction "n" reversible (withState [ "open", o true ])

              Expect.isFalse out2.Halted "the exit held after the true arm"
          }

          test "the lowering is the core's own arm, and a selection demands both arms' effects" {
              let action =
                  Action.Choose(
                      state "flag",
                      Action.Navigate(TextSource.Literal "/a", NavigateTarget.Self),
                      Some Action.Print,
                      None
                  )

              match UiWitness.view action with
              | ActionView.Choose(_, Action.Navigate _, Action.Print, None) -> ()
              | other -> failtestf "Choose did not view as the core's Choose: %A" other

              match UiWitness.view (Action.Choose(state "flag", Action.Print, None, None)) with
              | ActionView.Choose(_, _, Action.Chain [], _) -> ()
              | other -> failtestf "an absent whenFalse is the empty sequence: %A" other

              match UiWitness.view (Action.Require(state "ok")) with
              | ActionView.Require _ -> ()
              | other -> failtestf "Require did not view as the core's Require: %A" other
          }

          test "a confirm inside a Choose arm is addressed under the arm's name" {
              let confirm = Action.Confirm(TextSource.Literal "Sure?", Action.Print, None)
              let action = Action.Choose(state "flag", confirm, None, None)

              match UiWitness.addressedConfirm "n" "n#whenTrue" action with
              | Some(path, Action.Confirm _) -> Expect.equal path "whenTrue" "the path names the arm"
              | other -> failtestf "the confirm under whenTrue was not addressable: %A" other

              Expect.isNone (UiWitness.addressedConfirm "n" "n#whenFalse" action) "an absent arm addresses nothing"
          } ]
