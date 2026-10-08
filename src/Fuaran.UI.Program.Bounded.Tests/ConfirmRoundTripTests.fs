module Fuaran.UI.Program.Bounded.Tests.ConfirmRoundTripTests

// ─── Phase 2106: `Confirm` is a two-event round trip on the bounded path ────
//
// The gesture ASKS — the `Confirm` effect, its token the confirm's address in
// the node's action — and runs no continuation. The answer, the originating
// event re-delivered with `confirmToken` / `confirmAccepted`, runs one
// continuation as the core's `Choose` over the answer. Stale, duplicate and
// forged answers are refused as events; the continuation meets the dispatch
// gate on its own; and the demanded projection names both continuations.
//
// The driver-semantics corpus pins the four step traces on every host; this
// suite pins what a trace cannot see — the pending slot's invisibility to the
// tree, the second gate, an unanswerable prompt, a raw confirm outside a loop,
// and the demanded document.

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Introspect
open Fuaran.UI.Ops.Types
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation
open Fuaran.Program.Bounded
open Fuaran.Program.Runtime
open Fuaran.UI.Program
open Fuaran.UI.Program.BoundedDriver
open Fuaran.UI.Renderer.BindingResolver

let private jstr (s: string) = Fuaran.Core.JStr s

let private boundMarkdown (id: string) (key: string) (dflt: string) : Node<obj> =
    let n = Fuaran.markdown id "placeholder"

    { n with
        Kind = NodeKind.Markdown({ Text = TextSource.Bound(Binding.State(key, Some dflt)) }) }

let private button (id: string) (action: Action<obj>) : Node<obj> =
    Fuaran.button
        id
        { Defaults.button<obj> with
            Label = TextSource.Literal id
            OnClick = action }

let private tree (children: Node<obj> list) : WireTree =
    WireTree.ofDecoded (
        Fuaran.dashboard
            "root"
            { Defaults.dashboard<obj> with
                Children = children }
    )

let private confirm (prompt: TextSource) (onConfirm: Action<obj>) (onCancel: Action<obj> option) : Action<obj> =
    Action.Confirm(prompt, onConfirm, onCancel)

let private set (value: string) : Action<obj> =
    Action.SetState("msg", Some(jstr value), None)

let private go: Action<obj> =
    Action.Navigate(TextSource.Literal "/orders", NavigateTarget.Self)

/// The standard confirm: yes writes and navigates, no writes "kept".
let private deleteConfirm: Action<obj> =
    confirm (TextSource.Literal "Delete?") (Action.Chain [ set "deleted"; go ]) (Some(set "kept"))

let private click (nodeId: string) : LiveEvent =
    { ConnId = "c1"
      NodeId = nodeId
      Event = "click"
      Payload = Map.empty
      LastSeq = 0 }

let private answer (nodeId: string) (token: string) (accepted: bool) : LiveEvent =
    { click nodeId with
        Payload =
            Map.ofList
                [ ConfirmAnswer.TokenKey, LiveValue.Str token
                  ConfirmAnswer.AcceptedKey, LiveValue.Bool accepted ] }

let private session (children: Node<obj> list) : BoundedSession =
    BoundedDriver.init (BoundedServices.createPermissive (fun n -> n.Id)) BindingSources.empty (tree children)

let private readout (s: BoundedSession) : string option =
    match findNode (NodeId "readout") s.Resolved with
    | Some node ->
        match node.Kind with
        | NodeKind.Markdown spec ->
            match spec.Text with
            | TextSource.Literal text -> Some text
            | _ -> None
        | _ -> None
    | None -> None

/// Drive a session through events, returning every step's output.
let private run (s0: BoundedSession) (events: LiveEvent list) : BoundedSession * BoundedStepOutput list =
    events
    |> List.fold
        (fun (s, outs) ev ->
            let next, out = BoundedDriver.step s ev
            next, outs @ [ out ])
        (s0, [])

let private pendingOf (s: BoundedSession) : string list = fst (ConfirmRoundTrip.take s.Store)

[<Tests>]
let tests =
    testList
        "confirm round trip (Phase 2106)"
        [ test "the gesture asks, carrying the confirm's address, and runs no continuation" {
              let s, outs =
                  run
                      (session [ button "delete" deleteConfirm; boundMarkdown "readout" "msg" "init" ])
                      [ click "delete" ]

              Expect.equal outs.[0].Effects [ ClientEffect.Confirm("Delete?", "delete#") ] "the question, addressed"
              Expect.equal (readout s) (Some "init") "no continuation ran on the ask"
              Expect.equal (pendingOf s) [ "delete#" ] "the question is pending"
          }

          test "a confirm inside a chain is addressed by its position, and the chain runs around it" {
              let s, outs =
                  run
                      (session
                          [ button "delete" (Action.Chain [ set "asked"; deleteConfirm ])
                            boundMarkdown "readout" "msg" "init" ])
                      [ click "delete" ]

              Expect.equal
                  outs.[0].Effects
                  [ ClientEffect.Confirm("Delete?", "delete#1") ]
                  "token is the chain position"

              Expect.equal (readout s) (Some "asked") "the member before the confirm ran on the ask"
          }

          test "yes runs onConfirm and no runs onCancel, each once, on the answer" {
              let start =
                  session [ button "delete" deleteConfirm; boundMarkdown "readout" "msg" "init" ]

              let yes, yesOuts = run start [ click "delete"; answer "delete" "delete#" true ]
              let no, noOuts = run start [ click "delete"; answer "delete" "delete#" false ]

              Expect.equal (readout yes) (Some "deleted") "yes ran onConfirm"

              Expect.equal
                  yesOuts.[1].Effects
                  [ ClientEffect.Navigate("/orders", NavigateTarget.Self) ]
                  "and its effect"

              Expect.equal (readout no) (Some "kept") "no ran onCancel"
              Expect.isEmpty noOuts.[1].Effects "and onCancel reached no effect"
              Expect.isEmpty (pendingOf yes) "the answer consumed the question"
          }

          test "no with no onCancel does nothing — the language spells nothing by having no action" {
              let s, outs =
                  run
                      (session
                          [ button "delete" (confirm (TextSource.Literal "Delete?") (set "deleted") None)
                            boundMarkdown "readout" "msg" "init" ])
                      [ click "delete"; answer "delete" "delete#" false ]

              Expect.isNone outs.[1].Rejected "a declined question is an admitted answer"
              Expect.equal (readout s) (Some "init") "nothing happened"
          }

          test "a duplicate, a withdrawn and a forged answer are each refused as an event" {
              let start =
                  session
                      [ button "delete" deleteConfirm
                        button "other" (set "other")
                        boundMarkdown "readout" "msg" "init" ]

              let _, dup =
                  run
                      start
                      [ click "delete"
                        answer "delete" "delete#" true
                        answer "delete" "delete#" true ]

              let late, withdrawn =
                  run start [ click "delete"; click "other"; answer "delete" "delete#" true ]

              let _, forged = run start [ answer "delete" "delete#" true ]
              let _, elsewhere = run start [ click "delete"; answer "other" "delete#" true ]

              Expect.isSome dup.[2].Rejected "a second answer to one question"
              Expect.isSome withdrawn.[2].Rejected "an answer after the reader moved on"
              Expect.equal (readout late) (Some "other") "and its continuation never ran"
              Expect.isSome forged.[0].Rejected "an answer to a question never asked"
              Expect.isSome elsewhere.[1].Rejected "a token minted for one node, answered on another"
          }

          test "a refused answer leaves the pending question standing" {
              let s, outs =
                  run
                      (session [ button "delete" deleteConfirm; boundMarkdown "readout" "msg" "init" ])
                      [ click "delete"
                        answer "delete" "delete#9" true
                        answer "delete" "delete#" true ]

              Expect.isSome outs.[1].Rejected "a token that addresses nothing is refused"
              Expect.isNone outs.[2].Rejected "the real answer still lands"
              Expect.equal (readout s) (Some "deleted") "and runs"
          }

          test "the continuation meets the dispatch gate on its own" {
              let services =
                  { BoundedServices.createPermissive (fun n -> n.Id) with
                      CanDispatch =
                          fun a ->
                              match a with
                              | Action.Chain _ -> false
                              | _ -> true }

              let s0 =
                  BoundedDriver.init
                      services
                      BindingSources.empty
                      (tree [ button "delete" deleteConfirm; boundMarkdown "readout" "msg" "init" ])

              let s, outs = run s0 [ click "delete"; answer "delete" "delete#" true ]

              Expect.isNone outs.[0].Rejected "the gate admits the dialogue"

              match outs.[1].Rejected with
              | Some(BoundedReject.Gate(RejectReason.DispatchDenied _)) -> ()
              | other -> failtestf "the gate refuses the continuation behind it, got %A" other

              Expect.equal (readout s) (Some "init") "and nothing ran"
          }

          test "a prompt that resolves to nothing asks nothing, so nothing is pending" {
              let s, outs =
                  run
                      (session
                          [ button
                                "delete"
                                (confirm (TextSource.Bound(Binding.State("absent", None))) (set "deleted") None)
                            boundMarkdown "readout" "msg" "init" ])
                      [ click "delete" ]

              Expect.isEmpty outs.[0].Effects "no question"
              Expect.isEmpty (pendingOf s) "nothing pending"
              Expect.isNonEmpty outs.[0].Diagnostics "and the refusal is observable"
          }

          test "a tree cannot read the pending slot: it is out of the store whenever anything resolves" {
              let peeking =
                  boundMarkdown "peek" (ConfirmRoundTrip.PendingPrefix + "delete#") "unseen"

              let s, _ =
                  run
                      (session [ button "delete" deleteConfirm; peeking; boundMarkdown "readout" "msg" "init" ])
                      [ click "delete" ]

              Expect.equal (pendingOf s) [ "delete#" ] "the question is pending"

              match findNode (NodeId "peek") s.Resolved with
              | Some { Kind = NodeKind.Markdown { Text = TextSource.Literal text } } ->
                  Expect.equal text "unseen" "the binding resolved its default, not the slot"
              | other -> failtestf "the peeking node did not resolve: %A" other
          }

          test "a tree cannot forge a pending question: the slot is host-reserved" {
              let forge =
                  Action.SetState(ConfirmRoundTrip.PendingPrefix + "delete#", Some(Fuaran.Core.JBool true), None)

              let _, outs =
                  run
                      (session
                          [ button "forge" forge
                            button "delete" deleteConfirm
                            boundMarkdown "readout" "msg" "init" ])
                      [ click "forge"; answer "delete" "delete#" true ]

              Expect.isSome outs.[1].Rejected "the forged slot was never written"
          }

          test "a raw confirm outside a loop is declined, exactly as before — it has no address" {
              let outcome =
                  BoundedActions.runBoundedAction "delete" deleteConfirm BindingSources.empty

              Expect.isEmpty outcome.Effects "no question without an address"
              Expect.equal outcome.Store.State BindingSources.empty.State "no continuation"
          }

          test "the client placement runs the same round trip" {
              let program =
                  Program.mkBounded
                      (ProgramServices.createPermissive ignore)
                      BindingSources.empty
                      (tree [ button "delete" deleteConfirm; boundMarkdown "readout" "msg" "init" ])

              let p1, ask = Program.handleEvent program (click "delete")
              let p2, yes = Program.handleEvent p1 (answer "delete" "delete#" true)
              let _, dup = Program.handleEvent p2 (answer "delete" "delete#" true)

              Expect.equal ask.Effects [ ClientEffect.Confirm("Delete?", "delete#") ] "asked"
              Expect.equal yes.Effects [ ClientEffect.Navigate("/orders", NavigateTarget.Self) ] "answered"
              Expect.isSome dup.Rejected "and once only"
          }

          test "the demanded projection names the question and BOTH continuations" {
              let root =
                  WireTree.reify (
                      tree
                          [ button
                                "delete"
                                (confirm
                                    (TextSource.Literal "Delete?")
                                    go
                                    (Some(Action.WriteToClipboard(TextSource.Literal "kept"))))
                            boundMarkdown "readout" "msg" "init" ]
                  )

              let projection = Demanded.ofTree root

              Expect.containsAll
                  projection.Effects
                  [ "Confirm"; "Navigate"; "WriteToClipboard" ]
                  "the question, the confirm branch and the cancel branch"
          } ]
