module Fuaran.UI.ServerDriven.Tests.ConfirmAction

// ============================================================================
//  Phase 1537 — `Action.Confirm` and `Action.Focus`: ONE dispatch path, gated
//  TWICE.
//
//  The claim this file exists to make falsifiable is the security one, and it
//  is not "a confirm is gated". It is that the CONTINUATION meets its own gate:
//  a runtime that permits a dialogue and refuses navigation must accept the
//  confirm and still refuse the navigate behind it. A host that dispatched the
//  branch directly from the confirm arm would pass every test that only checks
//  the dialogue, which is why the deny-Navigate pair below is the load-bearing
//  one rather than the deny-everything one.
//
//  The server-driven tier and not the client renderer, on the Phase 1536
//  reasoning: `Render.runActionCore` is `private` and Fable-only, so the client
//  emission point is not reachable from a .NET runner. `Driver.step` IS
//  reachable through the entry a real connection uses, and it performs the
//  identical two-gate sequence.
//
//  FALSIFICATION, run rather than asserted. The tests below were executed
//  against a deliberately broken `Driver.step` — one that dispatched the
//  resolved branch WITHOUT re-consulting `CanDispatch` — and each records what
//  it did under that break.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Driver
open Fuaran.UI.ServerDriven.Validation

type private Msg = Noop

let private viewOf (action: Action<Msg>) (_: int) : Node<Msg> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<Msg> with
            Children =
                [ Fuaran.button
                      "ask"
                      { Defaults.button<Msg> with
                          OnClick = action } ] }

/// A permissive host except for the actions `deny` answers true for. The gate
/// is expressed over the ACTION rather than a descriptor because that is the
/// seam this tier owns (`DriverServices.CanDispatch`); a real host maps the
/// action to a renderer `ActionDescriptor` and asks its runtime.
let private servicesDenying (deny: Action<Msg> -> bool) =
    { DriverServices.createPermissive (fun (n: Node<Msg>) -> "<f id='" + n.Id + "'/>") with
        CanDispatch = fun a -> not (deny a) }

let private ask: LiveEvent =
    { ConnId = "c1"
      NodeId = "ask"
      Event = "click"
      Payload = Map.empty
      LastSeq = 0 }

let private answer (token: string) (accepted: bool) : LiveEvent =
    { ask with
        Payload =
            Map.ofList
                [ ConfirmAnswer.TokenKey, LiveValue.Str token
                  ConfirmAnswer.AcceptedKey, LiveValue.Bool accepted ] }

let private run (deny: Action<Msg> -> bool) (action: Action<Msg>) (ev: LiveEvent) =
    let session =
        init (servicesDenying deny) (fun (_: Msg) (m: int) -> m) (viewOf action) 0

    let _, out = step session ev
    out

let private navigateAway: Action<Msg> =
    Action.Navigate(TextSource.Literal "/orders/42", NavigateTarget.Self)

let private confirmThenNavigate: Action<Msg> =
    Action.Confirm(TextSource.Literal "Delete this order?", navigateAway, None)

let private isNavigate (a: Action<Msg>) =
    match a with
    | Action.Navigate _ -> true
    | _ -> false

let private isConfirm (a: Action<Msg>) =
    match a with
    | Action.Confirm _ -> true
    | _ -> false

let private isFocus (a: Action<Msg>) =
    match a with
    | Action.Focus _ -> true
    | _ -> false

[<Tests>]
let tests =
    testList
        "Phase 1537 — a Confirm is gated, and so is what it confirms"
        [ test "a default-deny runtime refuses the dialogue and ships NOTHING" {
              // The first gate. `Validation.validate` puts the resolved action
              // — the one CONTAINING the confirm — to `CanDispatch` before
              // anything is lowered, so a host that renders untrusted trees can
              // refuse an unbidden dialogue outright.
              let out = run (fun _ -> true) confirmThenNavigate ask
              Expect.isEmpty out.Effects "no dialogue instruction crosses to the shim"
              Expect.isSome out.Rejected "and the denial is recorded rather than silent"
          }

          test "an allowing runtime lowers the question and NOT the branches" {
              // The instruction carries what to ask and a token, and nothing
              // about what a yes will do. This is the property that keeps the
              // decision on the server: a shim told the continuation is a shim
              // that can perform it.
              let out = run (fun _ -> false) confirmThenNavigate ask

              match out.Effects with
              | [ ClientEffect.Confirm(prompt, token) ] ->
                  Expect.equal prompt "Delete this order?" "the resolved question crosses"
                  Expect.equal token "ask#" "the token addresses this node's whole action"

                  Expect.isFalse
                      ((ClientEffect.encode (ClientEffect.Confirm(prompt, token))).Contains "orders/42")
                      "the continuation's destination is nowhere in the instruction"
              | other -> failtestf "expected one Confirm effect, got %A" other
          }

          test "GATED TWICE: Confirm allowed, Navigate denied — the confirm is accepted and the navigate refused" {
              // THE test. A host that dispatched the branch straight out of the
              // confirm arm would ship the navigation here, because the confirm
              // itself was permitted. The branch has to be put to the gate on
              // its own for this to hold.
              //
              // FALSIFICATION: red under a `step` that skipped the second
              // `CanDispatch` — the effect list then carried
              // `Navigate("/orders/42", Self)` and `Rejected` was `None`.
              let deny = isNavigate

              let asked = run deny confirmThenNavigate ask
              Expect.isNonEmpty asked.Effects "the dialogue itself is permitted"

              let answered = run deny confirmThenNavigate (answer "ask#" true)
              Expect.isEmpty answered.Effects "the confirmed navigation is still refused"

              match answered.Rejected with
              | Some(RejectReason.DispatchDenied(node, description)) ->
                  Expect.equal node "ask" "the refusal names the node"
                  Expect.stringContains description "Navigate" "and names the action that was refused"
              | other -> failtestf "expected a DispatchDenied on the continuation, got %A" other
          }

          test "the accepted branch reaches the shim when its OWN gate allows it" {
              // The mirror, and the half that stops the refusal above being
              // achieved by refusing everything.
              let out = run (fun _ -> false) confirmThenNavigate (answer "ask#" true)

              Expect.equal
                  out.Effects
                  [ ClientEffect.Navigate("/orders/42", NavigateTarget.Self) ]
                  "the confirmed navigation is what crosses, and the question is not re-asked"
          }

          test "a DECLINED confirm runs the cancel branch, and nothing when there is none" {
              let withCancel: Action<Msg> =
                  Action.Confirm(TextSource.Literal "Discard?", navigateAway, Some(Action.Focus "draft-field"))

              let declined = run (fun _ -> false) withCancel (answer "ask#" false)
              Expect.equal declined.Effects [ ClientEffect.Focus "draft-field" ] "the cancel branch runs"

              let noCancel = run (fun _ -> false) confirmThenNavigate (answer "ask#" false)
              Expect.isEmpty noCancel.Effects "an absent cancel branch means nothing happens"
              Expect.isNone noCancel.Rejected "and that is not a rejection — the author declared it"
          }

          test "a token addressing no Confirm is REFUSED, not ignored" {
              // The tree may have moved under the reader, or the token may be
              // forged. Running nothing silently would leave a gesture that
              // reports success and did nothing.
              let out = run (fun _ -> false) confirmThenNavigate (answer "ask#7.3" true)

              match out.Rejected with
              | Some(RejectReason.PayloadOutOfBounds(node, detail)) ->
                  Expect.equal node "ask" "the refusal names the node"
                  Expect.stringContains detail "Confirm" "and says what the token failed to address"
              | other -> failtestf "expected PayloadOutOfBounds, got %A" other
          }

          test "a token minted for another node addresses nothing here" {
              // The shim chooses what it sends back, so a token is untrusted
              // payload like any other: one minted for a delete button must not
              // reach a save button's action.
              let out = run (fun _ -> false) confirmThenNavigate (answer "other-node#" true)
              Expect.isEmpty out.Effects "nothing is dispatched"
              Expect.isSome out.Rejected "and the mismatch is reported"
          }

          test "a chained confirm mints a token naming its POSITION" {
              // Two dialogues in one gesture have to be distinguishable, which
              // is the whole reason the token is a path rather than a node id.
              let chained: Action<Msg> =
                  Action.Chain
                      [ Action.Focus "first"
                        Action.Confirm(TextSource.Literal "Second?", Action.Focus "second", None) ]

              let asked = run (fun _ -> false) chained ask

              Expect.equal
                  asked.Effects
                  [ ClientEffect.Focus "first"; ClientEffect.Confirm("Second?", "ask#1") ]
                  "the confirm's token carries its index in the chain"

              let answered = run (fun _ -> false) chained (answer "ask#1" true)
              Expect.equal answered.Effects [ ClientEffect.Focus "second" ] "and the answer resolves to that branch"
          }

          test "an UNRESOLVED prompt lowers no dialogue at all" {
              // A yes/no with no subject is not a question. The `Navigate` arm
              // refuses an empty resolution for the same reason one layer over.
              let bound: Action<Msg> =
                  Action.Confirm(TextSource.Bound(Binding.State("question", None)), navigateAway, None)

              let out = run (fun _ -> false) bound ask
              Expect.isEmpty out.Effects "no dialogue crosses"
          }

          test "Action.Focus lowers to the effect this channel has carried since Phase 152" {
              let out = run (fun _ -> false) (Action.Focus "search-field") ask
              Expect.equal out.Effects [ ClientEffect.Focus "search-field" ] "the addressed node crosses"

              let deniedConfirms = run isConfirm (Action.Focus "search-field") ask
              Expect.isNonEmpty deniedConfirms.Effects "a policy that denies dialogues does not thereby deny focus"

              let deniedFocus = run isFocus (Action.Focus "search-field") ask
              Expect.isEmpty deniedFocus.Effects "and a policy that denies focus refuses it"
          } ]
