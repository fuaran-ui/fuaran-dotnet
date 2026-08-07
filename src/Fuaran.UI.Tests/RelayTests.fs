module Fuaran.UI.Tests.Relay

// ============================================================================
//  Phase 739 — the change hub + the DevTools relay page peer, pinned on .NET.
//
//  The shared conformance corpus (`RelayCorpusTests`) proves this host answers
//  the contract's fixtures. These tests pin the parts a corpus of exchanges
//  cannot reach:
//
//   * the change hub's coalescing, identity-idempotence, and cause precedence —
//     the properties that make ONE committed change produce ONE event;
//   * the two postures of "off" (§11.1) and the DEBUG-build gate;
//   * the CAPABILITY_ABSENT / UNKNOWN_MESSAGE distinction, which a client
//     branches on and which the corpus pins one instance of each of;
//   * silence for messages a peer must not answer (§3.2, §4, §10.4);
//   * that the wire kind discriminator the relay reports IS the canonical
//     encoder's `kind.$type` — a drift guard, not an example.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Relay
open Fuaran.UI.OpStream.Abstractions

// ─── A manual scheduler, so coalescing is deterministic on both pipelines ───

/// A hub whose notifications queue until `flush ()` — the .NET stand-in for the
/// browser's microtask turn. Coalescing is a property of the WINDOW, so a test
/// that cannot control the window cannot pin it.
let private manualHub () =
    let pending = ResizeArray<unit -> unit>()
    let hub = ChangeHub.createWith pending.Add

    let flush () =
        let queued = List.ofSeq pending
        pending.Clear()

        for run in queued do
            run ()

    hub, flush

// ─── The miniature host ─────────────────────────────────────────────────────

let private nn (value: 'T) : obj = box value |> Unchecked.nonNull

/// A distinct object standing in for "a new tree", for the hub's identity check.
/// The hub keys on IDENTITY, never on shape, so any fresh reference is a
/// faithful stand-in and the tests stay about the hub rather than about trees.
let private freshTree () : obj = nn (obj ())

let private metricNode: Node<obj> =
    Fuaran.metric
        "metric-1"
        { Defaults.metric with
            Value = binding.state "revenue" 0.0
            Trend = None }

let private gridNode: Node<obj> =
    // fuaran#665 — the required `toRow`; this fixture never renders rows, so an
    // explicit empty projection is the honest minimal choice (explicit here,
    // never a facade default).
    Fuaran.grid
        "grid-1"
        (fun (_: obj) -> (Map.empty: Row))
        { Defaults.grid<obj, obj> with
            Source = binding.query "channels" (fun (rows: obj list) -> rows) }

let private hostTree: Node<obj> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard with
            Children = [ metricNode; gridNode ] }

let private hostSources: BindingResolver.BindingSources =
    { BindingResolver.empty with
        State = Map.ofList [ "revenue", nn 42.0 ]
        QueryResults = Map.ofList [ "channels", nn ([]: obj list) ] }

type private StubRuntime(canDispatch: bool) =
    interface Runtime.IFuaranRuntime with
        member _.Call(endpoint, onResult) =
            Runtime.diagnostic.Call(endpoint, onResult)

        member _.Notify(channel, payload) =
            Runtime.diagnostic.Notify(channel, payload)

        member _.Navigate(route) = Runtime.diagnostic.Navigate(route)
        member _.SetState(key, value) = Runtime.diagnostic.SetState(key, value)

        member _.InvokeAiTool(toolName, args) =
            Runtime.diagnostic.InvokeAiTool(toolName, args)

        member _.WriteToClipboard(text) =
            Runtime.diagnostic.WriteToClipboard(text)

        member _.ReadFileBody(file, encoding, onRead) =
            Runtime.diagnostic.ReadFileBody(file, encoding, onRead)

        member _.Warn(_) = ()
        member _.LayoutObserver = None
        member _.TryRenderCustom(_, _, _) = None
        member _.TryGetCustomRenderer(_, _) = None
        member _.TryRenderCustomInScope(_, _, _, _) = None
        member _.TryGetCustomRendererInScope(_, _, _) = None
        member _.CanDispatch(_) = canDispatch
        member _.TryLoadGuest(_) = None

let private surfaceWith (hub: ChangeHub.ChangeHub) (applyHandler: DebugGlobal.ApplyHandler option) (canDispatch: bool) =
    Relay.surfaceOf
        hostTree
        hostSources
        (StubRuntime(canDispatch))
        { DebugGlobal.DebugOptions.defaults with
            Hub = hub
            ApplyHandler = applyHandler }

let private request (id: string) (requestType: string) (payload: (string * RelayValue) list) =
    RelayValue.Obj
        [ Relay.RelayKey, RelayValue.Str Relay.Profile
          "dir", RelayValue.Str "request"
          "id", RelayValue.Str id
          "type", RelayValue.Str requestType
          "payload", RelayValue.Obj payload ]

let private refusalClass (envelope: RelayValue option) : string option =
    envelope
    |> Option.bind (RelayValue.field "payload")
    |> Option.bind (RelayValue.stringField "class")

[<Tests>]
let tests =
    testList
        "Phase 739 — change hub + DevTools relay peer"
        [ testList
              "ChangeHub"
              [ test "a fresh hub has a revision and no listeners to notify" {
                    let hub = ChangeHub.create ()
                    Expect.equal (hub.Revision()) "r-0" "the baseline revision"
                }

                test "committing a new tree advances the revision" {
                    let hub, _ = manualHub ()
                    let first = hub.Commit (freshTree ()) ChangeHub.ChangeCause.Host
                    let second = hub.Commit (freshTree ()) ChangeHub.ChangeCause.Host
                    Expect.notEqual first second "a distinct tree is a distinct revision"
                }

                test "committing the SAME tree object twice is a no-op" {
                    // The idempotence that stops a re-registration caused only by
                    // a `sources` / `runtime` change from reading as a tree change.
                    let hub, flush = manualHub ()
                    let received = ResizeArray<ChangeHub.TreeChange>()
                    hub.Subscribe received.Add |> ignore
                    let tree = freshTree ()
                    let first = hub.Commit tree ChangeHub.ChangeCause.Host
                    let second = hub.Commit tree ChangeHub.ChangeCause.Host
                    flush ()
                    Expect.equal second first "the revision did not move"
                    Expect.equal received.Count 1 "and only the first commit notified"
                }

                test "commits in one window coalesce into one notification at the latest revision" {
                    let hub, flush = manualHub ()
                    let received = ResizeArray<ChangeHub.TreeChange>()
                    hub.Subscribe received.Add |> ignore
                    hub.Commit (freshTree ()) ChangeHub.ChangeCause.Host |> ignore
                    hub.Commit (freshTree ()) ChangeHub.ChangeCause.Host |> ignore
                    let latest = hub.Commit (freshTree ()) ChangeHub.ChangeCause.Host
                    flush ()
                    Expect.equal received.Count 1 "a change is a staleness signal, not a change log"
                    Expect.equal received[0].TreeRevision latest "carrying the LATEST revision"
                }

                test "the Apply cause wins the coalesced window over Host" {
                    // The natural double-fire of an in-page apply: the apply path
                    // commits, then the host's re-render re-registers. `apply` is
                    // the more specific answer, so it must survive the collapse.
                    let hub, flush = manualHub ()
                    let received = ResizeArray<ChangeHub.TreeChange>()
                    hub.Subscribe received.Add |> ignore
                    hub.Commit (freshTree ()) ChangeHub.ChangeCause.Apply |> ignore
                    hub.Commit (freshTree ()) ChangeHub.ChangeCause.Host |> ignore
                    flush ()
                    Expect.equal received[0].Cause ChangeHub.ChangeCause.Apply "apply wins"
                }

                test "an unsubscribed listener stops receiving, and re-release is harmless" {
                    let hub, flush = manualHub ()
                    let received = ResizeArray<ChangeHub.TreeChange>()
                    let release = hub.Subscribe received.Add
                    hub.Commit (freshTree ()) ChangeHub.ChangeCause.Host |> ignore
                    flush ()
                    release ()
                    release ()
                    hub.Commit (freshTree ()) ChangeHub.ChangeCause.Host |> ignore
                    flush ()
                    Expect.equal received.Count 1 "nothing after the release"
                }

                test "a throwing subscriber does not stop the others" {
                    let hub, flush = manualHub ()
                    let received = ResizeArray<ChangeHub.TreeChange>()
                    hub.Subscribe(fun _ -> failwith "boom") |> ignore
                    hub.Subscribe received.Add |> ignore
                    hub.Commit (freshTree ()) ChangeHub.ChangeCause.Host |> ignore
                    flush ()
                    Expect.equal received.Count 1 "the hub is a signal, not a dispatch chain"
                }

                test "the wire cause tokens are the contract's" {
                    Expect.equal (ChangeHub.ChangeCause.toWire ChangeHub.ChangeCause.Apply) "apply" "apply"
                    Expect.equal (ChangeHub.ChangeCause.toWire ChangeHub.ChangeCause.Host) "host" "host"
                } ]

          testList
              "Opt-in — the two postures of off (§11.1)"
              [ test "an opted-out peer answers NOT_OPTED_IN to hello itself" {
                    let hub, _ = manualHub ()
                    let surface = surfaceWith hub None true

                    let peer = Relay.createPeer (fun () -> Some surface) RelayOptions.defaults

                    let reply =
                        peer.Handle(
                            request "c-1" "hello" [ "accepts", RelayValue.Arr [ RelayValue.Str Relay.Profile ] ]
                        )

                    Expect.equal (refusalClass reply) (Some "NOT_OPTED_IN") "opt-out short-circuits everything"
                    Expect.isEmpty (peer.Capabilities()) "and advertises nothing"
                }

                test "RelayOptions.defaults is opted OUT" {
                    // Opting in is a host-side act; a default-on record would make
                    // the opt-in reachable by forgetting to set it.
                    Expect.isFalse RelayOptions.defaults.OptedIn "the default posture is off"
                }

                test "a peer with no published surface answers NOT_OPTED_IN" {
                    let peer =
                        Relay.createPeer
                            (fun () -> None)
                            { RelayOptions.defaults with
                                OptedIn = true }

                    let reply =
                        peer.Handle(
                            request "c-1" "hello" [ "accepts", RelayValue.Arr [ RelayValue.Str Relay.Profile ] ]
                        )

                    Expect.equal (refusalClass reply) (Some "NOT_OPTED_IN") "there is no live host behind it"
                }

                test "shouldInstall requires the host opt-in" {
                    Expect.isFalse (Relay.shouldInstall false) "no opt-in, no listener"

                    Expect.equal
                        (Relay.shouldInstall true)
                        DebugGlobal.compiledInDebug
                        "and a DEBUG build on top, mirroring DebugGlobal.shouldRegister"
                } ]

          testList
              "Capabilities (§6.4)"
              [ test "apply is advertised only when the host wired one" {
                    let hub, _ = manualHub ()

                    let peerFor handler =
                        Relay.createPeer
                            (fun () -> Some(surfaceWith hub handler true))
                            { RelayOptions.defaults with
                                OptedIn = true }

                    let readOnly = peerFor None

                    let writable = peerFor (Some(fun _ -> DebugGlobal.ApplyOutcome.Applied))

                    Expect.isFalse (List.contains "apply" (readOnly.Capabilities())) "read-only host: no apply"
                    Expect.isTrue (List.contains "apply" (writable.Capabilities())) "wired host: apply"

                    Expect.isTrue
                        (List.contains "read.nodeState" (readOnly.Capabilities()))
                        "a read-only host is fully conformant (§6.4)"
                }

                test "an unadvertised capability refuses CAPABILITY_ABSENT, never UNKNOWN_MESSAGE" {
                    // Reporting the wrong one tells the client something false:
                    // either that a real entry point does not exist, or that a
                    // non-existent one is merely switched off (§10.1).
                    let hub, _ = manualHub ()

                    let peer =
                        Relay.createPeer
                            (fun () -> Some(surfaceWith hub None true))
                            { RelayOptions.defaults with
                                OptedIn = true }

                    Expect.equal
                        (refusalClass (peer.Handle(request "c-1" "apply" [ "op", RelayValue.Obj [] ])))
                        (Some "CAPABILITY_ABSENT")
                        "apply exists but is not offered"

                    Expect.equal
                        (refusalClass (peer.Handle(request "c-2" "read.runtimeErrors" [])))
                        (Some "UNKNOWN_MESSAGE")
                        "read.runtimeErrors is not in the closed set at all"
                }

                test "unsubscribe is governed by the subscribe capability" {
                    let hub, _ = manualHub ()

                    let peer =
                        Relay.createPeer
                            (fun () -> Some(surfaceWith hub None true))
                            { RelayOptions.defaults with
                                OptedIn = true
                                OfferSubscribe = false }

                    let reply =
                        peer.Handle(request "c-1" "unsubscribe" [ "subscriptionId", RelayValue.Str "s-1" ])

                    Expect.equal (refusalClass reply) (Some "CAPABILITY_ABSENT") "no subscribe, no unsubscribe"
                } ]

          testList
              "Transport discipline"
              [ test "silence for anything that is not an answerable request" {
                    let hub, _ = manualHub ()

                    let peer =
                        Relay.createPeer
                            (fun () -> Some(surfaceWith hub None true))
                            { RelayOptions.defaults with
                                OptedIn = true }

                    Expect.isNone (peer.Handle(RelayValue.Str "not an envelope")) "not an object"
                    Expect.isNone (peer.Handle(RelayValue.Obj [ "dir", RelayValue.Str "request" ])) "no $relay"

                    Expect.isNone
                        (peer.Handle(
                            RelayValue.Obj
                                [ Relay.RelayKey, RelayValue.Str Relay.Profile
                                  "dir", RelayValue.Str "response"
                                  "id", RelayValue.Str "c-1"
                                  "type", RelayValue.Str "hello.ok"
                                  "payload", RelayValue.Obj [] ]
                        ))
                        "a page peer ignores responses (§10.4)"

                    Expect.isNone
                        (peer.Handle(
                            RelayValue.Obj
                                [ Relay.RelayKey, RelayValue.Str Relay.Profile
                                  "dir", RelayValue.Str "request"
                                  "type", RelayValue.Str "hello"
                                  "payload", RelayValue.Obj [] ]
                        ))
                        "no correlatable id — a reply could not be routed"
                }

                test "profile negotiation runs on EVERY request, not only hello" {
                    let hub, _ = manualHub ()

                    let peer =
                        Relay.createPeer
                            (fun () -> Some(surfaceWith hub None true))
                            { RelayOptions.defaults with
                                OptedIn = true }

                    let foreign =
                        RelayValue.Obj
                            [ Relay.RelayKey, RelayValue.Str "relay@2.0"
                              "dir", RelayValue.Str "request"
                              "id", RelayValue.Str "c-1"
                              "type", RelayValue.Str "read.tree"
                              "payload", RelayValue.Obj [] ]

                    Expect.equal
                        (refusalClass (peer.Handle foreign))
                        (Some "FOREIGN_PROFILE")
                        "a client cannot be assumed to keep its profile constant (§5.2)"
                }

                test "a newer MINOR is Behind, not Foreign" {
                    Expect.isFalse (Relay.isForeignProfile "relay@1.9") "within a major, additions are ignorable"
                    Expect.isTrue (Relay.isForeignProfile "relay@2.0") "a different major may have REMOVED a shape"
                    Expect.isTrue (Relay.isForeignProfile "core@1.0") "a different namespace entirely"
                    Expect.isTrue (Relay.isForeignProfile "nonsense") "not the grammar at all"
                } ]

          testList
              "Subscription (§8.5)"
              [ test "unknown event names are ignored while one is recognised" {
                    let hub, _ = manualHub ()

                    let peer =
                        Relay.createPeer
                            (fun () -> Some(surfaceWith hub None true))
                            { RelayOptions.defaults with
                                OptedIn = true }

                    let reply =
                        peer.Handle(
                            request
                                "c-1"
                                "subscribe"
                                [ "events", RelayValue.Arr [ RelayValue.Str "tree"; RelayValue.Str "quantum" ] ]
                        )

                    Expect.equal
                        (reply |> Option.bind (RelayValue.stringField "type"))
                        (Some "subscribe.ok")
                        "additive-is-minor stays safe only if unknown names are ignored (§10.2)"

                    Expect.equal
                        (reply
                         |> Option.bind (RelayValue.field "payload")
                         |> Option.bind (RelayValue.field "events"))
                        (Some(RelayValue.Arr [ RelayValue.Str "tree" ]))
                        "the response echoes the subset actually established"
                }

                test "no recognised event name at all is MALFORMED_MESSAGE" {
                    let hub, _ = manualHub ()

                    let peer =
                        Relay.createPeer
                            (fun () -> Some(surfaceWith hub None true))
                            { RelayOptions.defaults with
                                OptedIn = true }

                    Expect.equal
                        (refusalClass (
                            peer.Handle(
                                request "c-1" "subscribe" [ "events", RelayValue.Arr [ RelayValue.Str "quantum" ] ]
                            )
                        ))
                        (Some "MALFORMED_MESSAGE")
                        "nothing could be established"
                }

                test "unsubscribing an unknown id is ok, not a refusal" {
                    let hub, _ = manualHub ()

                    let peer =
                        Relay.createPeer
                            (fun () -> Some(surfaceWith hub None true))
                            { RelayOptions.defaults with
                                OptedIn = true }

                    Expect.equal
                        (peer.Handle(request "c-1" "unsubscribe" [ "subscriptionId", RelayValue.Str "s-99" ])
                         |> Option.bind (RelayValue.stringField "type"))
                        (Some "unsubscribe.ok")
                        "the caller's desired end state is reached either way"
                }

                test "dispose releases every subscription, so no further event is emitted" {
                    let hub, flush = manualHub ()
                    let emitted = ResizeArray<RelayValue>()

                    let peer =
                        Relay.createPeer
                            (fun () -> Some(surfaceWith hub None true))
                            { RelayOptions.defaults with
                                OptedIn = true
                                Emit = emitted.Add }

                    peer.Handle(request "c-1" "subscribe" [ "events", RelayValue.Arr [ RelayValue.Str "tree" ] ])
                    |> ignore

                    hub.Commit (freshTree ()) ChangeHub.ChangeCause.Host |> ignore
                    flush ()
                    Expect.equal emitted.Count 1 "one event while subscribed"

                    peer.Dispose()
                    hub.Commit (freshTree ()) ChangeHub.ChangeCause.Host |> ignore
                    flush ()
                    Expect.equal emitted.Count 1 "and none after (§8.5 — release on unload)"
                } ]

          testList
              "Gated apply (§8)"
              [ test "a structured op reaches the host's decoder as canonical JSON" {
                    // §8.2: the client sends a structurally-cloned object and the
                    // PAGE PEER serialises it, because canonical ordering is a
                    // property of the wire format the host already implements.
                    let hub, _ = manualHub ()
                    let seen = ResizeArray<string>()

                    let handler: DebugGlobal.ApplyHandler =
                        fun json ->
                            seen.Add json
                            DebugGlobal.ApplyOutcome.Applied

                    let peer =
                        Relay.createPeer
                            (fun () -> Some(surfaceWith hub (Some handler) true))
                            { RelayOptions.defaults with
                                OptedIn = true }

                    peer.Handle(
                        request
                            "c-1"
                            "apply"
                            [ "op",
                              RelayValue.Obj
                                  [ "target", RelayValue.Str "metric-1"; "$type", RelayValue.Str "RemoveNode" ]
                              "attribution", RelayValue.Obj [ "actor", RelayValue.Str "fuaran-devtools" ] ]
                    )
                    |> ignore

                    Expect.equal
                        (List.ofSeq seen)
                        [ """{"$type":"RemoveNode","target":"metric-1"}""" ]
                        "keys ordinal-sorted, and the advisory attribution left out of the op"
                }

                test "the three mandated refusal classes stay distinct (§8.4)" {
                    // Collapsing these sends the user to fix a tree that was never
                    // the problem — which is why they are separate wire values.
                    let hub, _ = manualHub ()

                    let peerWith handler canDispatch =
                        Relay.createPeer
                            (fun () -> Some(surfaceWith hub handler canDispatch))
                            { RelayOptions.defaults with
                                OptedIn = true }

                    let op = [ "op", RelayValue.Obj [ "$type", RelayValue.Str "RemoveNode" ] ]

                    let denied =
                        (peerWith (Some(fun _ -> DebugGlobal.ApplyOutcome.Applied)) false)
                            .Handle(request "c-1" "apply" op)

                    let rejected =
                        (peerWith
                            (Some(fun _ -> DebugGlobal.ApplyOutcome.RejectedWith("nope", "FUARAN-APPLY-ROOT-REMOVAL")))
                            true)
                            .Handle(request "c-2" "apply" op)

                    Expect.equal (refusalClass denied) (Some "POLICY_DENIED") "the gate refused"
                    Expect.equal (refusalClass rejected) (Some "VALIDATOR_REJECT") "the edit was illegal"

                    Expect.isNone
                        (denied
                         |> Option.bind (RelayValue.field "payload")
                         |> Option.bind (RelayValue.field "detail"))
                        "POLICY_DENIED detail stays empty — a reason hands out a map of the policy (§11.5)"

                    Expect.equal
                        (rejected
                         |> Option.bind (RelayValue.field "payload")
                         |> Option.bind (RelayValue.field "detail")
                         |> Option.bind (RelayValue.stringField "code"))
                        (Some "FUARAN-APPLY-ROOT-REMOVAL")
                        "VALIDATOR_REJECT carries the host's diagnostic code"
                }

                test "a decode failure carries the wire DecodeError verbatim" {
                    let hub, _ = manualHub ()

                    let handler: DebugGlobal.ApplyHandler =
                        fun _ ->
                            DebugGlobal.ApplyOutcome.DecodeFailedWith
                                { Code = "UNKNOWN_DU_CASE"
                                  Path = "$.$type"
                                  Message = "Unknown TreeOp case."
                                  ExpectedShape = Some "UpdateProp | RemoveNode" }

                    let peer =
                        Relay.createPeer
                            (fun () -> Some(surfaceWith hub (Some handler) true))
                            { RelayOptions.defaults with
                                OptedIn = true }

                    let detail =
                        peer.Handle(request "c-1" "apply" [ "op", RelayValue.Obj [ "$type", RelayValue.Str "Nope" ] ])
                        |> Option.bind (RelayValue.field "payload")
                        |> Option.bind (RelayValue.field "detail")

                    // PascalCase: §9.3 carries the wire format's own envelope, it
                    // does not re-spell it.
                    Expect.equal (detail |> Option.bind (RelayValue.stringField "Code")) (Some "UNKNOWN_DU_CASE") "Code"
                    Expect.equal (detail |> Option.bind (RelayValue.stringField "Path")) (Some "$.$type") "Path"

                    Expect.equal
                        (detail |> Option.bind (RelayValue.stringField "ExpectedShape"))
                        (Some "UpdateProp | RemoveNode")
                        "ExpectedShape"
                }

                test "an applied op advances the revision and the event carries the same token" {
                    // §8.3: `apply.ok`'s treeRevision and the `changed` event's are
                    // consistent by construction.
                    let hub, flush = manualHub ()
                    let emitted = ResizeArray<RelayValue>()

                    let handler: DebugGlobal.ApplyHandler =
                        fun _ -> DebugGlobal.ApplyOutcome.AppliedWithTree(freshTree ())

                    let peer =
                        Relay.createPeer
                            (fun () -> Some(surfaceWith hub (Some handler) true))
                            { RelayOptions.defaults with
                                OptedIn = true
                                Emit = emitted.Add }

                    peer.Handle(request "c-1" "subscribe" [ "events", RelayValue.Arr [ RelayValue.Str "tree" ] ])
                    |> ignore

                    let applied =
                        peer.Handle(
                            request "c-2" "apply" [ "op", RelayValue.Obj [ "$type", RelayValue.Str "RemoveNode" ] ]
                        )

                    flush ()

                    let appliedRevision =
                        applied
                        |> Option.bind (RelayValue.field "payload")
                        |> Option.bind (RelayValue.stringField "treeRevision")

                    let eventRevision =
                        emitted
                        |> Seq.tryHead
                        |> Option.bind (RelayValue.field "payload")
                        |> Option.bind (RelayValue.stringField "treeRevision")

                    Expect.isSome appliedRevision "apply.ok carries a revision"
                    Expect.equal eventRevision appliedRevision "and the change event carries the same one"

                    Expect.equal
                        (emitted
                         |> Seq.tryHead
                         |> Option.bind (RelayValue.field "payload")
                         |> Option.bind (RelayValue.stringField "cause"))
                        (Some "apply")
                        "attributed to the apply that caused it"
                }

                test "a read-only host's apply refuses without ever reaching a handler" {
                    let hub, _ = manualHub ()

                    let peer =
                        Relay.createPeer
                            (fun () -> Some(surfaceWith hub None true))
                            { RelayOptions.defaults with
                                OptedIn = true
                                OfferApply = Some true }

                    // Capability forced on while the surface has no apply path:
                    // the mis-declaration still refuses honestly rather than
                    // claiming the op was illegal.
                    Expect.equal
                        (refusalClass (
                            peer.Handle(request "c-1" "apply" [ "op", RelayValue.Obj [ "$type", RelayValue.Str "X" ] ])
                        ))
                        (Some "CAPABILITY_ABSENT")
                        "an unwired apply is an absent capability, not a rejected edit"
                } ]

          testList
              "Wire kind discriminator (§7.1)"
              [ test "the relay reports the canonical encoder's kind.$type" {
                    // A DRIFT GUARD, not an example: this host's console surface
                    // reports `Kind.name`, which differs from the wire token for
                    // `DataGrid`. The relay adapts at the boundary (§1.4) — and a
                    // SECOND divergence must fail this build rather than silently
                    // mis-report a kind to every extension client.
                    let wireTypeOf (node: Node<obj>) : string =
                        let json = CanonicalJson.encodeNode node
                        // The encoder hoists the kind discriminator as the node's
                        // `$type`; read the first one, which is the node's own.
                        let marker = "\"$type\":\""
                        let start = json.IndexOf(marker) + marker.Length
                        json.Substring(start, json.IndexOf('"', start) - start)

                    let cases: (string * Node<obj>) list =
                        [ "Box", hostTree
                          "Metric", metricNode
                          "DataGrid", gridNode
                          "Markdown", Fuaran.markdown "md" "hi"
                          "Button",
                          Fuaran.button
                              "b"
                              { Defaults.button<obj> with
                                  Label = TextSource.Literal "go" }
                          "Select",
                          Fuaran.select
                              "s"
                              { Defaults.select<obj> with
                                  // Since the swap: `Static` payloads are
                                  // option-wrapped, and `SelectSpec.Value` is a
                                  // `Binding<string>` whose no-selection form is
                                  // the default-less State read (it was a
                                  // `Binding<string option>` with a `None` default).
                                  Source = Binding.Static(Some [])
                                  Value = binding.stateNoDefault "pick" } ]

                    for (expected, node) in cases do
                        Expect.equal (wireTypeOf node) expected (sprintf "the encoder's $type for %s" expected)

                        Expect.equal
                            (Relay.wireKindName node.Kind)
                            expected
                            (sprintf "and the relay must report the same token for %s" expected)
                }

                test "findNodes matches on the wire token, not the console one" {
                    let hub, _ = manualHub ()

                    let peer =
                        Relay.createPeer
                            (fun () -> Some(surfaceWith hub None true))
                            { RelayOptions.defaults with
                                OptedIn = true }

                    let idsFor kind =
                        peer.Handle(request "c-1" "read.findNodes" [ "kind", RelayValue.Str kind ])
                        |> Option.bind (RelayValue.field "payload")
                        |> Option.bind (RelayValue.field "nodeIds")

                    Expect.equal
                        (idsFor "DataGrid")
                        (Some(RelayValue.Arr [ RelayValue.Str "grid-1" ]))
                        "the token a client read from a wire tree finds the node"

                    Expect.equal
                        (idsFor "Nonexistent")
                        (Some(RelayValue.Arr []))
                        "an unrecognised kind is [], never a refusal (§7.5)"
                } ]

          testList
              "JSON serialisation (§8.2)"
              [ test "object keys are ordinal-sorted" {
                    Expect.equal
                        (Relay.toJson (
                            RelayValue.Obj
                                [ "zulu", RelayValue.Num 1.0
                                  "Alpha", RelayValue.Bool true
                                  "mike", RelayValue.Null ]
                        ))
                        """{"Alpha":true,"mike":null,"zulu":1}"""
                        "the canonical ordering rule the wire encoder enforces"
                }

                test "strings are escaped and integral numbers render without a decimal point" {
                    Expect.equal (Relay.toJson (RelayValue.Str "a\"b\\c\nd")) "\"a\\\"b\\\\c\\nd\"" "escapes"
                    Expect.equal (Relay.toJson (RelayValue.Num 42.0)) "42" "integral"
                    Expect.equal (Relay.toJson (RelayValue.Num 0.5)) "0.5" "fractional"
                } ] ]
