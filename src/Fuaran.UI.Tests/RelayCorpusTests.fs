module Fuaran.UI.Tests.RelayCorpus

#nowarn "3261" // DirectoryInfo.Parent + JsonElement.GetString() are legitimately nullable here.

// ============================================================================
//  Cross-host DevTools-relay conformance gate — F# leg (Phase 739).
//
//  Reads the shared corpus `../wire-format-fixtures/devtools-relay/` (its own
//  manifest — the relay family is deliberately NOT indexed by the wire-format
//  root manifest, whose runners execute codec round-trips and could not run a
//  relay exchange) and drives every fixture through the F# page peer.
//
//  What is asserted, per the contract's §12.3:
//
//   * the response `type` is the request's `type` + ".ok", or "refusal";
//   * the response `id` echoes the request's `id` VERBATIM;
//   * for a `relay-refusal`, `payload.class` equals the manifest's
//     `expectedClass`;
//   * every field the fixture's payload declares is present, with the stated
//     JSON TYPE — recursively through nested objects and arrays.
//
//  Shapes and ENUMERATED values, never bytes. A `treeRevision` token, a
//  geometry number, a resolved binding value and a `message` string are all
//  environment-specific and will legitimately differ from the fixture author's
//  choices; asserting byte-equality on them would test those choices rather
//  than this implementation. The closed-set fields — `class`, `status`,
//  `source`, `cause`, `event`, `profile`, `dir`, `requestType` — ARE compared by
//  value, because those are the ones a client branches on.
//
//  The relay-event fixtures are exercised by making the peer EMIT them (a
//  subscribe, then a committed tree change) rather than merely accepting them:
//  a fixture a peer only has to tolerate is no evidence that subscribe fires on
//  a commit, which is the whole point of the subscription leg.
//
//  Skips gracefully when the corpus is absent (a standalone fuaran-dotnet/
//  checkout without the specification sibling) — `RelayTests` still pins the
//  protocol in that case.
// ============================================================================

open System
open System.IO
open System.Text.Json
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Relay

// ─── Corpus location ────────────────────────────────────────────────────────

let private tryFindCorpus () : string option =
    let rec climb (dir: DirectoryInfo) =
        if isNull (box dir) then
            None
        else
            let candidate =
                Path.Combine(dir.FullName, "wire-format-fixtures", "devtools-relay", "manifest.json")

            if File.Exists candidate then
                Some(Path.GetDirectoryName candidate)
            else
                climb dir.Parent

    climb (DirectoryInfo(AppContext.BaseDirectory))

// ─── JsonElement → RelayValue ───────────────────────────────────────────────

let rec private ofJson (element: JsonElement) : RelayValue =
    match element.ValueKind with
    | JsonValueKind.Object ->
        RelayValue.Obj [ for property in element.EnumerateObject() -> property.Name, ofJson property.Value ]
    | JsonValueKind.Array -> RelayValue.Arr [ for item in element.EnumerateArray() -> ofJson item ]
    | JsonValueKind.String -> RelayValue.Str(element.GetString())
    | JsonValueKind.Number -> RelayValue.Num(element.GetDouble())
    | JsonValueKind.True -> RelayValue.Bool true
    | JsonValueKind.False -> RelayValue.Bool false
    | _ -> RelayValue.Null

// ─── The miniature host the fixtures are answered from ──────────────────────
//
// Node ids and kinds mirror the fixtures' illustrative tree so the responses
// are recognisably the same conversation: `root` (Box) with `metric-1` (Metric,
// Value bound to $state.revenue) and `grid-1` (DataGrid, Source bound to
// $queries.channels).

/// F# 10 `box _` types as `obj | null`; the source maps require non-null obj.
let private nn (value: 'T) : obj = box value |> Unchecked.nonNull

let private metricNode: Node<obj> =
    Fuaran.metric
        "metric-1"
        { Defaults.metric with
            Label = TextSource.Literal "Revenue"
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

/// A node that IS in the tree but has NO rendered element — the world the
/// `refusal-node-not-found` fixture describes. §7.4 asks a host to distinguish
/// "no such node" from "not currently on screen" via `detail.reason`, and a
/// host whose tree lacks the node entirely cannot exercise that distinction at
/// all; the fixture would then pass against an implementation that never
/// implemented it.
let private unrenderedNode: Node<obj> =
    Fuaran.metric
        "metric-9"
        { Defaults.metric with
            Label = TextSource.Literal "Offscreen"
            Value = binding.state "revenue" 0.0
            Trend = None }

let private hostTree: Node<obj> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard with
            Children = [ metricNode; gridNode; unrenderedNode ] }

let private hostSources: BindingResolver.BindingSources =
    { BindingResolver.empty with
        State = Map.ofList [ "revenue", nn 42.0 ]
        QueryResults = Map.ofList [ "channels", nn ([]: obj list) ] }

/// Geometry the fixtures' `read.renderedDom` expects. There is no DOM on this
/// pipeline, so the surface's geometry leg is supplied directly — which is also
/// what lets the "in the tree but not rendered" distinction be tested at all.
let private stubGeometry (nodeId: string) : DebugGlobal.NodeGeometry option =
    if nodeId = "metric-1" then
        Some
            { X = 24.0
              Y = 180.5
              Width = 320.0
              Height = 96.0
              Overflowing = false
              Hidden = false }
    else
        None

/// A runtime whose dispatch gate is caller-controlled — the POLICY_DENIED leg.
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
        member _.CanDispatch(_) = canDispatch
        member _.TryLoadGuest(_) = None

/// Build a surface + peer for one fixture's world. Each parameter is the axis
/// some fixture needs to move: the opt-in, the apply capability, the gate, and
/// what the host's apply handler decides.
let private peerFor
    (optedIn: bool)
    (applyHandler: DebugGlobal.ApplyHandler option)
    (canDispatch: bool)
    (offerSubscribe: bool)
    (hub: ChangeHub.ChangeHub)
    : RelayPeer * RelaySurface =
    let options =
        { DebugGlobal.DebugOptions.defaults with
            Hub = hub
            ApplyHandler = applyHandler }

    let surface =
        { Relay.surfaceOf hostTree hostSources (StubRuntime(canDispatch)) options with
            Geometry = stubGeometry }

    let peer =
        Relay.createPeer
            (fun () -> Some surface)
            { RelayOptions.defaults with
                OptedIn = optedIn
                OfferSubscribe = offerSubscribe
                HostVersion = "0.6.0" }

    peer, surface

/// The apply handler the accepting fixtures use: it "applies" by handing back a
/// fresh tree object, which is what makes the hub advance and `apply.ok` carry
/// the same revision the `changed` event will.
let private acceptingHandler: DebugGlobal.ApplyHandler =
    fun _ -> DebugGlobal.ApplyOutcome.AppliedWithTree(nn (Fuaran.markdown "applied" "ok": Node<obj>))

let private decodeFailingHandler: DebugGlobal.ApplyHandler =
    fun _ ->
        DebugGlobal.ApplyOutcome.DecodeFailedWith
            { Code = "UNKNOWN_DU_CASE"
              Path = "$.$type"
              Message = "Unknown TreeOp case 'UpdatePropertyValue'."
              ExpectedShape = Some "UpdateProp | EditNode | InsertChild | RemoveNode | MoveNode | ReorderChildren" }

let private rejectingHandler: DebugGlobal.ApplyHandler =
    fun _ ->
        DebugGlobal.ApplyOutcome.RejectedWith(
            "The op decoded but the apply engine rejected it.",
            "FUARAN-APPLY-ROOT-REMOVAL"
        )

/// The peer each fixture is answered by. Everything not named here gets the
/// fully-capable, opted-in peer.
let private peerForFixture (fixtureId: string) (hub: ChangeHub.ChangeHub) : RelayPeer =
    let peer, _ =
        match fixtureId with
        | "hello-read-only" -> peerFor true None true false hub
        | "refusal-not-opted-in" -> peerFor false (Some acceptingHandler) true true hub
        | "refusal-capability-absent" -> peerFor true None true true hub
        | "refusal-policy-denied" -> peerFor true (Some acceptingHandler) false true hub
        | "refusal-decode-failed" -> peerFor true (Some decodeFailingHandler) true true hub
        | "refusal-validator-reject" -> peerFor true (Some rejectingHandler) true true hub
        | _ -> peerFor true (Some acceptingHandler) true true hub

    peer

// ─── Shape assertions (§12.3) ───────────────────────────────────────────────

/// The payload fields whose values come from a CLOSED SET in the contract, and
/// are therefore compared by value rather than only by type. Everything else —
/// ids, revisions, geometry, resolved values, prose messages — is
/// environment-specific.
let private enumeratedFields =
    set [ "class"; "status"; "source"; "cause"; "event"; "profile"; "dir" ]

let private typeName (value: RelayValue) : string =
    match value with
    | RelayValue.Null -> "null"
    | RelayValue.Bool _ -> "boolean"
    | RelayValue.Num _ -> "number"
    | RelayValue.Str _ -> "string"
    | RelayValue.Arr _ -> "array"
    | RelayValue.Obj _ -> "object"
    | RelayValue.Opaque _ -> "opaque"

/// `Opaque` is a host value passed through verbatim (§7.3's "any JSON value"),
/// so it satisfies whatever type the fixture declared for that slot.
let private typeCompatible (expected: RelayValue) (actual: RelayValue) : bool =
    match actual with
    | RelayValue.Opaque _ -> true
    | _ -> typeName expected = typeName actual

/// Assert `actual` carries every field `expected` declares, with the same JSON
/// type — recursively. Extra fields on `actual` are fine: the contract is
/// additively extensible and a peer may carry more than a fixture pins (§10.2).
///
/// `strictEnums` controls whether closed-set values are also compared BY VALUE.
/// It holds while fixture and response are known to line up, and is dropped once
/// they demonstrably do not (see the array rule): comparing the `source` of the
/// fixture's first child against the response's second child would be asserting
/// an alignment that is not there, and would fail a correct implementation.
let rec private assertShape (strictEnums: bool) (path: string) (expected: RelayValue) (actual: RelayValue) : unit =
    match expected, actual with
    | RelayValue.Obj expectedFields, RelayValue.Obj _ ->
        for (name, expectedValue) in expectedFields do
            match RelayValue.field name actual with
            | None -> failtestf "%s.%s — the fixture declares this field and the response omits it" path name
            | Some actualValue ->
                if not (typeCompatible expectedValue actualValue) then
                    failtestf
                        "%s.%s — expected JSON type %s, got %s"
                        path
                        name
                        (typeName expectedValue)
                        (typeName actualValue)

                if strictEnums && enumeratedFields.Contains name then
                    Expect.equal
                        actualValue
                        expectedValue
                        (sprintf "%s.%s is a closed-set value and must match the fixture exactly" path name)

                assertShape strictEnums (path + "." + name) expectedValue actualValue
    | RelayValue.Arr expectedItems, RelayValue.Arr actualItems ->
        // Array LENGTH is environment-specific — how many Metrics a tree happens
        // to hold is the fixture author's choice, not the contract's. So:
        //
        //  * same length ⇒ position means the same thing on both sides, and the
        //    elements are compared pairwise at full strictness;
        //  * different length ⇒ nothing is known about which element corresponds
        //    to which, so every element is checked against the fixture's first as
        //    a SHAPE template, with enumerated-value equality dropped.
        if List.length expectedItems = List.length actualItems then
            List.zip expectedItems actualItems
            |> List.iteri (fun index (expectedItem, actualItem) ->
                assertShape strictEnums (sprintf "%s[%d]" path index) expectedItem actualItem)
        else
            match expectedItems with
            | [] -> ()
            | template :: _ ->
                actualItems
                |> List.iteri (fun index item ->
                    if typeCompatible template item then
                        assertShape false (sprintf "%s[%d]" path index) template item)
    | _ -> ()

let private expectString (path: string) (value: RelayValue option) : string =
    match value with
    | Some(RelayValue.Str s) -> s
    | _ -> failtestf "%s must be a string" path

// ─── The suite ──────────────────────────────────────────────────────────────

type private Fixture =
    { Id: string
      Kind: string
      Request: RelayValue option
      Response: RelayValue option
      Event: RelayValue option
      ExpectedClass: string option }

let private loadFixtures (dir: string) : Fixture list =
    let manifest =
        JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "manifest.json")))

    let read (element: JsonElement) (key: string) : RelayValue option =
        match element.TryGetProperty key with
        | true, value ->
            Some(ofJson (JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, value.GetString()))).RootElement))
        | _ -> None

    [ for element in manifest.RootElement.GetProperty("fixtures").EnumerateArray() ->
          { Id = element.GetProperty("id").GetString()
            Kind = element.GetProperty("kind").GetString()
            Request = read element "requestFile"
            Response = read element "responseFile"
            Event = read element "eventFile"
            ExpectedClass =
              match element.TryGetProperty "expectedClass" with
              | true, value -> Some(value.GetString())
              | _ -> None } ]

/// Drive one request/response fixture and assert the four §12.3 properties.
let private runExchange (fixture: Fixture) : unit =
    let request =
        match fixture.Request with
        | Some r -> r
        | None -> failtestf "%s declares kind %s but carries no requestFile" fixture.Id fixture.Kind

    let expected =
        match fixture.Response with
        | Some r -> r
        | None -> failtestf "%s carries no responseFile" fixture.Id

    let hub = ChangeHub.createWith (fun run -> run ())
    let peer = peerForFixture fixture.Id hub

    match peer.Handle request with
    | None -> failtestf "%s — the peer answered with silence; a verified peer always gets an answer" fixture.Id
    | Some actual ->
        let requestId = expectString "request.id" (RelayValue.field "id" request)
        let requestType = expectString "request.type" (RelayValue.field "type" request)
        let actualType = expectString "response.type" (RelayValue.field "type" actual)

        Expect.equal
            (RelayValue.stringField "id" actual)
            (Some requestId)
            (sprintf "%s — the response must echo the request id verbatim (§4.1)" fixture.Id)

        Expect.equal
            (RelayValue.stringField Relay.RelayKey actual)
            (Some Relay.Profile)
            (sprintf "%s — every message carries the sender's profile id (§4)" fixture.Id)

        Expect.equal
            (RelayValue.stringField "dir" actual)
            (Some "response")
            (sprintf "%s — a reply to a request is a response (§4)" fixture.Id)

        // "A successful response's type is the request's type with '.ok'
        // appended; a refused response's type is always 'refusal'. There is no
        // third outcome." (§4.2)
        Expect.isTrue
            (actualType = requestType + ".ok" || actualType = "refusal")
            (sprintf "%s — response type %s is neither %s.ok nor refusal (§4.2)" fixture.Id actualType requestType)

        match fixture.ExpectedClass with
        | Some expectedClass ->
            Expect.equal actualType "refusal" (sprintf "%s — a relay-refusal fixture must be refused" fixture.Id)

            Expect.equal
                (RelayValue.field "payload" actual
                 |> Option.bind (RelayValue.stringField "class"))
                (Some expectedClass)
                (sprintf "%s — the refusal class is the machine-readable field (§9.1)" fixture.Id)

            Expect.equal
                (RelayValue.field "payload" actual
                 |> Option.bind (RelayValue.stringField "requestType"))
                (Some requestType)
                (sprintf "%s — a refusal echoes the refused request's type (§9.1)" fixture.Id)
        | None ->
            Expect.equal
                actualType
                (requestType + ".ok")
                (sprintf "%s — a relay-exchange fixture must succeed" fixture.Id)

        assertShape
            true
            "payload"
            (defaultArg (RelayValue.field "payload" expected) (RelayValue.Obj []))
            (defaultArg (RelayValue.field "payload" actual) (RelayValue.Obj []))

/// Drive one event fixture by making the peer EMIT it: subscribe, then commit a
/// tree change, and assert the emitted envelope against the fixture's shape.
let private runEvent (fixture: Fixture) : unit =
    let expected =
        match fixture.Event with
        | Some e -> e
        | None -> failtestf "%s carries no eventFile" fixture.Id

    let expectedCause =
        RelayValue.field "payload" expected
        |> Option.bind (RelayValue.stringField "cause")
        |> Option.defaultValue "host"

    let cause =
        if expectedCause = "apply" then
            ChangeHub.ChangeCause.Apply
        else
            ChangeHub.ChangeCause.Host

    let hub = ChangeHub.createWith (fun run -> run ())
    let emitted = ResizeArray<RelayValue>()

    let options =
        { DebugGlobal.DebugOptions.defaults with
            Hub = hub
            ApplyHandler = Some acceptingHandler }

    let surface =
        { Relay.surfaceOf hostTree hostSources (StubRuntime(true)) options with
            Geometry = stubGeometry }

    let peer =
        Relay.createPeer
            (fun () -> Some surface)
            { RelayOptions.defaults with
                OptedIn = true
                HostVersion = "0.6.0"
                Emit = emitted.Add }

    let subscribeRequest =
        RelayValue.Obj
            [ Relay.RelayKey, RelayValue.Str Relay.Profile
              "dir", RelayValue.Str "request"
              "id", RelayValue.Str "c-10"
              "type", RelayValue.Str "subscribe"
              "payload", RelayValue.Obj [ "events", RelayValue.Arr [ RelayValue.Str "tree" ] ] ]

    peer.Handle subscribeRequest
    |> Option.iter (fun reply ->
        Expect.equal
            (RelayValue.stringField "type" reply)
            (Some "subscribe.ok")
            (sprintf "%s — the subscription must be established before an event can fire" fixture.Id))

    hub.Commit (nn (obj ())) cause |> ignore

    Expect.equal
        (List.ofSeq emitted |> List.length)
        1
        (sprintf "%s — one committed change must produce exactly one coalesced event (§8.5)" fixture.Id)

    let actual = emitted[0]

    Expect.equal
        (RelayValue.stringField "dir" actual)
        (Some "event")
        (sprintf "%s — a change notification is an event, not a response (§4)" fixture.Id)

    Expect.equal
        (RelayValue.stringField "id" actual)
        (Some "c-10")
        (sprintf "%s — an event carries the id of the subscribe that established it (§4.1)" fixture.Id)

    Expect.equal
        (RelayValue.stringField "type" actual)
        (Some "changed")
        (sprintf "%s — the one event type in relay@1.0 is `changed` (§8.5)" fixture.Id)

    assertShape
        true
        "payload"
        (defaultArg (RelayValue.field "payload" expected) (RelayValue.Obj []))
        (defaultArg (RelayValue.field "payload" actual) (RelayValue.Obj []))

[<Tests>]
let tests =
    match tryFindCorpus () with
    | None ->
        testList
            "DevTools relay corpus (cross-host gate)"
            [ test "corpus absent — skipped (standalone checkout)" {
                  Expect.isTrue true "wire-format-fixtures/devtools-relay/ not found; RelayTests still pin the contract"
              } ]
    | Some dir ->
        let fixtures = loadFixtures dir

        testList
            "DevTools relay corpus (cross-host gate)"
            [ test "the corpus is non-empty" {
                  Expect.isGreaterThan (List.length fixtures) 0 "devtools-relay/manifest.json must enumerate fixtures"
              }

              test "every message type in the closed set has a fixture" {
                  // The manifest claims this (§12.3); a runner that never checks
                  // it would pass a corpus that quietly stopped covering a type.
                  let covered =
                      fixtures
                      |> List.choose (fun f -> f.Request |> Option.bind (RelayValue.stringField "type"))
                      |> Set.ofList

                  let required =
                      set
                          [ "hello"
                            "read.nodeState"
                            "read.bindingValue"
                            "read.renderedDom"
                            "read.tree"
                            "read.findNodes"
                            "apply"
                            "subscribe"
                            "unsubscribe" ]

                  Expect.isEmpty (Set.difference required covered) "every §4.2 request type needs a fixture"
              }

              for fixture in fixtures do
                  test (sprintf "%s — %s" fixture.Id fixture.Kind) {
                      match fixture.Kind with
                      | "relay-event" -> runEvent fixture
                      | _ -> runExchange fixture
                  } ]
