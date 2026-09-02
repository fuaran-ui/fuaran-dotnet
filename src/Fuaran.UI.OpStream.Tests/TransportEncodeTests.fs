module Fuaran.UI.OpStream.Tests.TransportEncodeTests

open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.OpStream.Abstractions

// ============================================================================
//  Phase 577 — `CanonicalJson.encodeNodeForTransport`.
//
//  The load-bearing pair here is the last two tests. One asserts the transport
//  encoder REFUSES a tree whose interaction would be lost; the other asserts
//  `encodeNode` still ACCEPTS the same tree and emits the sentinel. If the
//  second ever goes red, the hash chain's deliberate closure-blindness has been
//  broken in the act of adding a check beside it — which is the failure this
//  phase was most able to cause and least able to notice.
// ============================================================================

type private Msg =
    | NoOp
    | Other of string

let private dashboard id children : Node<Msg> =
    Fuaran.dashboard
        id
        { Defaults.dashboard<Msg> with
            Children = children }

let private actionButton (id: string) (action: Action<Msg>) : Node<Msg> =
    Fuaran.button
        id
        { Defaults.button<Msg> with
            Label = TextSource.Literal "Go"
            OnClick = action }

[<Tests>]
let tests =
    testList
        "Fuaran.UI.OpStream.TransportEncode"
        [ test "a wire-representable tree encodes, and to the canonical bytes" {
              let tree =
                  dashboard
                      "root"
                      [ actionButton "notify" (Action.notify "app.saved" (JVal.JStr "ok"))
                        actionButton "fetch" (Action.callIntoState (ApiEndpoint "/rows") "rows") ]

              match CanonicalJson.encodeNodeForTransport tree with
              | Ok json ->
                  // Not merely "it succeeded": the SAME bytes the canonical
                  // encoder produces. A transport encoder that quietly emitted
                  // a different serialisation would be a second wire format.
                  Expect.equal json (CanonicalJson.encodeNode tree) "the canonical bytes, unchanged"
              | Error paths -> failtestf "Expected Ok, got lossy paths: %A" paths
          }

          test "a Dispatch is refused, naming the node and the slot" {
              let tree = dashboard "root" [ actionButton "go" (Action.dispatch NoOp) ]

              match CanonicalJson.encodeNodeForTransport tree with
              | Ok _ -> failtest "Expected a refusal — the closure cannot cross the wire"
              | Error [ path ] ->
                  Expect.equal path.NodeId "go" "the node the author must repair"
                  Expect.equal path.Slot "Action.Dispatch.msg" "the SlotCapability spelling"
              | Error other -> failtestf "Expected exactly one lossy path, got %A" other
          }

          test "a Call's onResult is refused; the same Call with into: is not" {
              let lossy =
                  dashboard "root" [ actionButton "go" (Action.call (ApiEndpoint "/save") (fun (_: obj) -> NoOp)) ]

              match CanonicalJson.encodeNodeForTransport lossy with
              | Error [ path ] -> Expect.equal path.Slot "Action.Call.onResult" "the continuation, not the endpoint"
              | other -> failtestf "Expected one Action.Call.onResult refusal, got %A" other

              let survivable =
                  dashboard "root" [ actionButton "go" (Action.callIntoState (ApiEndpoint "/save") "saved") ]

              Expect.isTrue
                  (match CanonicalJson.encodeNodeForTransport survivable with
                   | Ok _ -> true
                   | Error _ -> false)
                  "into: is the wire-native round trip"
          }

          test "every lossy path is reported, not the first" {
              let tree =
                  dashboard
                      "root"
                      [ actionButton "one" (Action.dispatch NoOp)
                        actionButton "two" (Action.call (ApiEndpoint "/save") (fun (_: obj) -> NoOp)) ]

              match CanonicalJson.encodeNodeForTransport tree with
              | Error paths ->
                  Expect.equal (List.length paths) 2 "an author repairs a tree in one pass"
                  Expect.contains (paths |> List.map (fun p -> p.NodeId)) "one" "the Dispatch"
                  Expect.contains (paths |> List.map (fun p -> p.NodeId)) "two" "the Call"
              | Ok _ -> failtest "Expected a refusal"
          }

          test "the op-stream encoder keeps its deliberate closure-blindness" {
              // `encodeNode` feeds the hash chain, where two ops differing only
              // in an opaque 'Msg payload hash identically BY DESIGN. Adding a
              // refusal beside it must not have narrowed it — so this asserts
              // the OLD behaviour survives, unchanged, on the exact tree the
              // transport encoder refuses.
              //
              // And it pins the shape precisely, because the shape is worse than
              // the phase text assumed: the encoder does not write a sentinel,
              // it writes the discriminator and DROPS the payload. `"<closure>"`
              // is the DECODER's reconstruction (WIRE_FORMAT §4), which is why
              // the emitted bytes carry no trace of the loss at all — nothing
              // downstream of the encoder can tell a Dispatch that lost a
              // message from one that never had a payload to lose.
              let tree = dashboard "root" [ actionButton "go" (Action.dispatch NoOp) ]

              let json = CanonicalJson.encodeNode tree

              Expect.stringContains
                  json
                  "\"onClick\":{\"$type\":\"Dispatch\"}"
                  "the discriminator survives; the payload does not"

              Expect.isFalse (json.Contains "msg") "no payload field is emitted at all"
          }

          test "the two Dispatch trees are indistinguishable to the chain" {
              // The property the hash chain relies on, stated as a test rather
              // than as prose: two trees whose ONLY difference is the 'Msg
              // encode to identical bytes. This is the thing the transport
              // encoder must not have broken, and the thing that makes the
              // transport encoder necessary.
              let one = dashboard "root" [ actionButton "go" (Action.dispatch NoOp) ]

              let two =
                  dashboard "root" [ actionButton "go" (Action.Dispatch(Other "a different message")) ]

              Expect.equal
                  (CanonicalJson.encodeNode one)
                  (CanonicalJson.encodeNode two)
                  "hash-blind by design — which is exactly why an author needs a second encoder"
          } ]
