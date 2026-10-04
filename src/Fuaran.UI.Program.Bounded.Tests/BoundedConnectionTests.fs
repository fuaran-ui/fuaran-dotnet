module Fuaran.UI.Program.Bounded.Tests.BoundedConnectionTests

// ─── The bounded placement's channel glue ────────────────────────────────────
//
// `BoundedConnection` is what lets a bounded session be SERVED: it binds the
// driver to the UI tier's transport seam, sequences frames, buffers them for
// reconnect, and routes refusals. Same fixture as the driver tests — a dashboard
// with a button whose click writes state, and a Markdown bound to that state —
// driven here through an `InMemoryChannel` rather than by calling `step`.
//
// The second half covers out-of-band edits: a tree op submitted by a tool
// outside the interaction loop. The properties that matter are that it is
// refused by default, that a granted edit reaches the client on the ORDINARY
// frame stream, and that it is structural — a later interaction resolves
// against the edited base tree rather than reverting it. The last is what makes
// this loop, and only this loop, a coherent target for such edits.

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops.Introspect
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation
open Fuaran.Program.Bounded
open Fuaran.UI.Program
open Fuaran.UI.Program.BoundedDriver
open Fuaran.UI.Renderer.BindingResolver

let private o (v: 'T) : obj = box v |> Unchecked.nonNull

let private jv (v: obj) : Fuaran.Core.JVal =
    match v with
    | :? int as i -> Fuaran.Core.JInt i
    | :? string as s -> Fuaran.Core.JStr s
    | :? bool as b -> Fuaran.Core.JBool b
    | :? float as f -> Fuaran.Core.JFloat f
    | other -> failwith (sprintf "jv: unsupported test payload %A" other)

/// A Markdown node whose text is bound to a `State` key (the reactive node).
let private boundMarkdown (id: string) (key: string) (dflt: string) : Node<obj> =
    let n = Fuaran.markdown id "placeholder"

    { n with
        Kind = NodeKind.Markdown({ Text = TextSource.Bound(Binding.State(key, Some dflt)) }) }

let private mkTreeNode (onClick: Action<obj>) : Node<obj> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<obj> with
            Children =
                [ Fuaran.button
                      "set"
                      { Defaults.button<obj> with
                          OnClick = onClick }
                  boundMarkdown "count" "msg" "init" ] }

let private mkTree (onClick: Action<obj>) : WireTree = WireTree.ofDecoded (mkTreeNode onClick)

let private stubRender (n: Node<obj>) : string =
    let s = n.Id
    $"<f id='{s}'/>"

let private clickEv (nodeId: string) : LiveEvent =
    { ConnId = "c1"
      NodeId = nodeId
      Event = "click"
      Payload = Map.empty
      LastSeq = 0 }

let private markdownLiteral (tree: Node<obj>) (id: string) : string option =
    match findNode (NodeId id) tree with
    | Some node ->
        match node.Kind with
        | NodeKind.Markdown({ Text = TextSource.Literal s }) -> Some s
        | _ -> None
    | None -> None

/// The standard fixture: a session whose button click sets `msg` to `updated`.
let private session (services: BoundedServices) =
    BoundedDriver.init services empty (mkTree (Action.SetState("msg", Some(jv "updated"), None)))

let private permissive () =
    BoundedServices.createPermissive stubRender

/// The out-of-band edit used throughout: append a SECOND Markdown bound to the
/// same state key. Chosen deliberately — because it binds to `msg`, a later
/// interaction re-resolves it, which is exactly the structural claim.
let private insertSecondBound: TreeOp<obj> =
    TreeOp.InsertChild(NodeId "root", boundMarkdown "count2" "msg" "init")

let private request (op: TreeOp<obj>) : OutOfBandRequest =
    { ConnId = "c1"
      Actor = "tool:inspector"
      Op = op }

[<Tests>]
let tests =
    testList
        "Phase 741 — bounded connection"
        [ test "an inbound event steps the session and pushes one sequenced frame" {
              let channel = InMemoryChannel()
              let conn = BoundedConnection("c1", session (permissive ()), channel)

              channel.Send(clickEv "set")

              Expect.equal (List.length channel.Pushed) 1 "one frame pushed"
              Expect.equal conn.Sequence 1 "sequence advanced"
              Expect.equal channel.Pushed.Head.Seq 1 "frame carries the sequence"
              Expect.isNonEmpty channel.Pushed.Head.Patches "the frame carries the re-resolved patch"
              Expect.equal (markdownLiteral conn.Session.Resolved "count") (Some "updated") "session advanced"
          }

          test "an event for another connection is ignored" {
              let channel = InMemoryChannel()
              let conn = BoundedConnection("c1", session (permissive ()), channel)

              channel.Send({ clickEv "set" with ConnId = "other" })

              Expect.isEmpty channel.Pushed "no frame pushed for another connection's event"
              Expect.equal conn.Sequence 0 "sequence not advanced"
          }

          test "a refused event pushes nothing and reaches the reject sink" {
              let channel = InMemoryChannel()
              let conn = BoundedConnection("c1", session (permissive ()), channel)
              let seen = ResizeArray<BoundedReject>()
              conn.EnableRejectSink seen.Add

              channel.Send(clickEv "ghost")

              Expect.isEmpty channel.Pushed "no frame on a G1 reject"
              Expect.equal conn.Sequence 0 "a refused step consumes no sequence number"

              match List.ofSeq seen with
              | [ Gate(RejectReason.UnknownNode "ghost") ] -> ()
              | other -> failtestf "expected one Gate(UnknownNode 'ghost'), got %A" other
          }

          test "Resync re-pushes only the frames newer than the client's last sequence" {
              let channel = InMemoryChannel()
              let conn = BoundedConnection("c1", session (permissive ()), channel)

              // Two distinct state writes, so each step really produces a patch.
              conn.Handle(clickEv "set")

              let replayed = conn.Resync 0

              Expect.equal replayed 1 "the one buffered frame replayed"
              Expect.equal (List.length channel.Pushed) 2 "the replay went out on the channel"
              Expect.equal (conn.Resync conn.Sequence) 0 "a fully-caught-up client is replayed nothing"
          }

          test "out-of-band apply is refused until a host installs a grant policy" {
              let channel = InMemoryChannel()
              let conn = BoundedConnection("c1", session (permissive ()), channel)

              let outcome = conn.ApplyOutOfBand(request insertSecondBound)

              match outcome.Refused with
              | Some(NotGranted _) -> ()
              | other -> failtestf "expected NotGranted with no policy installed, got %A" other

              Expect.isEmpty outcome.Patches "a refusal carries no patches"
              Expect.isEmpty channel.Pushed "a refusal pushes nothing"
              Expect.isNone (findNode (NodeId "count2") conn.Session.BaseTree) "the base tree is untouched"
          }

          test "an installed policy that denies refuses with the host's own reason" {
              let channel = InMemoryChannel()
              let conn = BoundedConnection("c1", session (permissive ()), channel)
              conn.EnableOutOfBandApply(fun _ -> Deny "read-only session")

              match (conn.ApplyOutOfBand(request insertSecondBound)).Refused with
              | Some(NotGranted "read-only session") -> ()
              | other -> failtestf "expected the host's reason to survive to the refusal, got %A" other

              Expect.isNone (findNode (NodeId "count2") conn.Session.BaseTree) "the base tree is untouched"
          }

          test "the submitter's attribution is handed to the policy, and is only a claim" {
              let channel = InMemoryChannel()
              let conn = BoundedConnection("c1", session (permissive ()), channel)
              let seen = ResizeArray<string>()

              conn.EnableOutOfBandApply(fun r ->
                  seen.Add r.Actor
                  Grant)

              conn.ApplyOutOfBand(request insertSecondBound) |> ignore

              Expect.equal (List.ofSeq seen) [ "tool:inspector" ] "the actor claim reached the policy verbatim"
          }

          test "a granted edit reaches the client on the ordinary frame stream" {
              let channel = InMemoryChannel()
              let conn = BoundedConnection("c1", session (permissive ()), channel)
              conn.EnableOutOfBandApply(fun _ -> Grant)

              let outcome = conn.ApplyOutOfBand(request insertSecondBound)

              Expect.isNone outcome.Refused "granted"
              Expect.isNonEmpty outcome.Patches "the edit lowered to patches"
              Expect.equal (List.length channel.Pushed) 1 "pushed as an ordinary frame"
              Expect.equal conn.Sequence 1 "and consumed one sequence number"
              Expect.equal channel.Pushed.Head.Patches outcome.Patches "the pushed frame carries exactly those patches"
          }

          test "a granted edit is structural — a later interaction resolves against the edited tree" {
              let channel = InMemoryChannel()
              let conn = BoundedConnection("c1", session (permissive ()), channel)
              conn.EnableOutOfBandApply(fun _ -> Grant)

              conn.ApplyOutOfBand(request insertSecondBound) |> ignore
              conn.Handle(clickEv "set")

              // Both the original and the inserted node are bound to "msg", so a
              // single interaction after the edit must re-resolve BOTH. This is
              // the property that fails on a model-backed loop, where the next
              // step's diff would revert the insertion instead.
              Expect.equal (markdownLiteral conn.Session.Resolved "count") (Some "updated") "the original re-resolved"
              Expect.equal (markdownLiteral conn.Session.Resolved "count2") (Some "updated") "the inserted node too"
              Expect.isSome (findNode (NodeId "count2") conn.Session.BaseTree) "the insertion survived the interaction"
          }

          test "an op that does not apply is refused, and the session is untouched" {
              let channel = InMemoryChannel()
              let conn = BoundedConnection("c1", session (permissive ()), channel)
              conn.EnableOutOfBandApply(fun _ -> Grant)

              let ghost = TreeOp.InsertChild(NodeId "ghost", boundMarkdown "count2" "msg" "init")

              match (conn.ApplyOutOfBand(request ghost)).Refused with
              | Some(Unapplicable _) -> ()
              | other -> failtestf "expected Unapplicable for an unknown parent, got %A" other

              Expect.isEmpty channel.Pushed "nothing pushed"
              Expect.isNone (findNode (NodeId "count2") conn.Session.BaseTree) "the base tree is untouched"
          }

          test "G2: an edit that pushes the tree past MaxNodes is refused" {
              // The fixture is exactly three nodes, so the budget admits it and
              // refuses the fourth. An out-of-band author must not be able to buy
              // work the interaction path is capped at.
              let services =
                  { permissive () with
                      Budget =
                          { InteractionBudget.defaults with
                              MaxNodes = 3 } }

              let channel = InMemoryChannel()
              let conn = BoundedConnection("c1", session services, channel)
              conn.EnableOutOfBandApply(fun _ -> Grant)

              match (conn.ApplyOutOfBand(request insertSecondBound)).Refused with
              | Some(OverBudget _) -> ()
              | other -> failtestf "expected OverBudget, got %A" other

              Expect.isEmpty channel.Pushed "nothing pushed"
              Expect.isNone (findNode (NodeId "count2") conn.Session.BaseTree) "the base tree is untouched"
              Expect.equal conn.Session.NodeCount 3 "the priced cost is unchanged"
          } ]
