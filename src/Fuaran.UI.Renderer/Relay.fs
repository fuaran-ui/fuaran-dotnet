module Fuaran.UI.Renderer.Relay

// ============================================================================
//  Fuaran — the page peer of the DevTools relay contract (Phase 739).
//
//  A `postMessage` envelope that carries this host's already-shipped in-page
//  introspection surface (`window.__fuaran`, see `DebugGlobal.fs`) across the
//  page/extension boundary, so a browser extension — or any other same-page
//  script — can inspect, and where the host permits edit, a live Fuaran UI.
//
//  The contract is specified language-neutrally in the wire-format
//  specification repository (`DEVTOOLS_RELAY.md`, profile `relay@1.0`) with an
//  executable fixture family beside it; this module is written to that document
//  and pinned against those fixtures by the .NET test runner. Section
//  references in the comments below are to that document. F# is the SECOND
//  relay-conformant host; the TypeScript renderer shipped first and the two are
//  parity-locked on message shapes and refusal classification, not on bytes
//  (§1.2: relay envelopes carry no byte-parity obligation).
//
//  Three properties are load-bearing and easy to lose in a refactor:
//
//  * OFF BY DEFAULT (§11.1). `createPeer` is not opted in unless told to be,
//    and no listener is installed unless a host asks for one. The preferred
//    production posture is ABSENCE — no listener, so a probe gets no answer
//    whatsoever; the opted-out peer, which answers NOT_OPTED_IN, is the
//    honest-client posture for a development build. `shouldInstall` adds the
//    DEBUG-build gate on top, mirroring `DebugGlobal.shouldRegister`.
//  * NO SIDE DOOR (§11.3). Every mutation crosses the host's own decode →
//    validate → policy path, in the page. This module contributes no apply
//    engine, no validator, and no policy of its own — the apply seam is the
//    host's `DebugGlobal.ApplyHandler`, unchanged, and all this module does is
//    map its outcome onto a refusal class.
//  * SILENCE FOR UNVERIFIED PEERS (§3.2, §11.4). A message failing the origin
//    checks gets no reply — not even a refusal, which would itself disclose
//    that a Fuaran host is here.
//
//  ── Why the protocol is pure F# over a surface RECORD ─────────────────────
//  Everything except the `postMessage` transport compiles on BOTH pipelines,
//  over a `RelaySurface` of plain functions. That is what lets the shared
//  conformance corpus run in the .NET Expecto suite with no browser and no
//  jsdom — the same Fable-vs-.NET pipeline-parity posture the rest of this
//  package takes, applied to a protocol rather than to a render.
// ============================================================================

open Fuaran.UI.Types

#if FABLE_COMPILER
open Fable.Core
open Fable.Core.JsInterop
#endif

/// The relay profile this peer speaks.
[<Literal>]
let Profile = "relay@1.0"

/// The envelope field whose presence marks a message as a relay message
/// (§3.2 check 4, §4). `$`-prefixed to mark it spec-reserved.
[<Literal>]
let RelayKey = "$relay"

/// The host implementation identifier reported in `hello.ok`. **Opaque to the
/// client** (§6.3) — a client must not branch on it; `capabilities` is the only
/// thing that determines what is available.
[<Literal>]
let HostName = "fuaran"

// ─── RelayValue — the transported JSON shape ────────────────────────────────
//
// Relay messages are JSON-compatible objects passed through the browser's
// structured-clone algorithm. Modelling them as a typed DU (rather than as
// `obj` throughout) is what makes the protocol logic pure and testable off the
// browser; the Fable transport converts to and from the live JS values at the
// two edges, and nowhere else.

[<RequireQualifiedAccess>]
type RelayValue =
    | Null
    | Bool of bool
    | Num of float
    | Str of string
    | Arr of RelayValue list
    | Obj of (string * RelayValue) list
    /// A host value carried through verbatim — a resolved binding value, which
    /// is application data of no fixed shape (§7.3's `value` is "any JSON
    /// value"). The transport hands the original value straight to the client;
    /// the .NET runner sees the boxed CLR value and can assert on it.
    | Opaque of obj

[<RequireQualifiedAccess>]
module RelayValue =

    /// A field of an object value, or `None` for anything else / an absent key.
    let field (name: string) (value: RelayValue) : RelayValue option =
        match value with
        | RelayValue.Obj fields -> fields |> List.tryPick (fun (k, v) -> if k = name then Some v else None)
        | _ -> None

    let asString (value: RelayValue) : string option =
        match value with
        | RelayValue.Str s -> Some s
        | _ -> None

    let asList (value: RelayValue) : RelayValue list option =
        match value with
        | RelayValue.Arr items -> Some items
        | _ -> None

    let isObject (value: RelayValue) : bool =
        match value with
        | RelayValue.Obj _ -> true
        | _ -> false

    /// A string field, or `None` when absent or not a string.
    let stringField (name: string) (value: RelayValue) : string option =
        field name value |> Option.bind asString

// ─── Serialising an `apply` op for the host's decoder (§8.2) ────────────────
//
// The client sends a structurally-cloned object; the page peer serialises it to
// canonical JSON before handing it to the host's decode path. That obligation
// sits HERE deliberately: canonical ordering is a property of the wire format
// the host already implements, and requiring every relay client to re-implement
// it would put a byte-sensitive obligation on the least-qualified peer.
//
// Ordinal key sorting — the canonical rule the wire encoder enforces — is
// applied here; the decoder is order-tolerant either way, so this matters for
// what a host JOURNALS, not for what it accepts. Number rendering is delegated
// to each pipeline's own primitive, since a relay envelope carries no
// byte-parity obligation (§1.2) and each rendering is correct on its own side.

#if FABLE_COMPILER
[<Emit("String($0)")>]
let private floatToString (value: float) : string = jsNative
#else
let private floatToString (value: float) : string =
    value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
#endif

let private numberToJson (value: float) : string =
    if System.Double.IsNaN value || System.Double.IsInfinity value then
        // JSON has no representation for these; the wire format's own string
        // sentinels are the encoder's business, not this transport's.
        "null"
    elif value = floor value && abs value < 1e15 then
        string (int64 value)
    else
        floatToString value

let private escapeJsonString (raw: string) : string =
    let builder = System.Text.StringBuilder()
    builder.Append '"' |> ignore

    for ch in raw do
        match ch with
        | '"' -> builder.Append "\\\"" |> ignore
        | '\\' -> builder.Append "\\\\" |> ignore
        | '\n' -> builder.Append "\\n" |> ignore
        | '\r' -> builder.Append "\\r" |> ignore
        | '\t' -> builder.Append "\\t" |> ignore
        | '\b' -> builder.Append "\\b" |> ignore
        | '\f' -> builder.Append "\\f" |> ignore
        | c when c < ' ' -> builder.Append(sprintf "\\u%04x" (int c)) |> ignore
        | c -> builder.Append c |> ignore

    builder.Append '"' |> ignore
    builder.ToString()

/// Render a relay-carried value as JSON, with object keys ordinal-sorted.
let rec toJson (value: RelayValue) : string =
    match value with
    | RelayValue.Null -> "null"
    | RelayValue.Bool b -> if b then "true" else "false"
    | RelayValue.Num n -> numberToJson n
    | RelayValue.Str s -> escapeJsonString s
    | RelayValue.Arr items -> "[" + String.concat "," (items |> List.map toJson) + "]"
    | RelayValue.Obj fields ->
        let rendered =
            fields
            |> List.sortBy fst
            |> List.map (fun (k, v) -> escapeJsonString k + ":" + toJson v)

        "{" + String.concat "," rendered + "}"
    // An `Opaque` never reaches here in practice: it is produced only by the
    // host's own resolved binding values, which flow OUT to a client and never
    // into an op. Rendering it as null rather than throwing keeps a
    // mis-constructed value from taking down the peer.
    | RelayValue.Opaque _ -> "null"

// ─── The surface the peer relays ────────────────────────────────────────────

/// Looking up one binding slot: the node may be missing entirely, else the slot
/// resolution together with the node's kind — which the `SLOT_NOT_DECLARED`
/// refusal detail needs (§9.3).
[<RequireQualifiedAccess>]
type SlotLookup =
    | NodeMissing
    | Slot of resolution: DebugGlobal.SlotResolution * kind: string

/// The host's in-page introspection surface, as the peer consumes it. A record
/// of functions rather than the `window.__fuaran` POJO: the peer is pure over
/// this, so a test can hand it a miniature host.
type RelaySurface =
    {
        /// The version of the underlying in-page surface shape (§6.3).
        SurfaceVersion: string
        /// Whether the host wired a real apply path. `apply` is advertised only
        /// when this is true (§6.4) — advertising an entry point that does not
        /// exist is worse than not advertising it.
        CanApply: bool
        TreeRevision: unit -> string
        Subscribe: (ChangeHub.TreeChange -> unit) -> (unit -> unit)
        NodeState: string -> DebugGlobal.NodeIntrospection option
        InspectTree: unit -> DebugGlobal.TreeIntrospection
        ResolveSlot: string -> string -> SlotLookup
        Geometry: string -> DebugGlobal.NodeGeometry option
        FindNodes: string -> string list
        /// The host's gated apply, taking the canonical JSON this peer produced
        /// from the client's structured op (§8.2).
        Apply: string -> DebugGlobal.ApplyResult
    }

/// How a peer is configured. Every default is the conservative one.
type RelayOptions =
    {
        /// Whether the host has opted into relay exposure. **Defaults to
        /// `false`** — an opted-out peer answers every request, including
        /// `hello`, with `NOT_OPTED_IN`, and reaches the surface for nothing
        /// (§11.1).
        OptedIn: bool
        /// Advertise (and serve) `apply`. `None` defers to the surface's
        /// `CanApply`.
        OfferApply: bool option
        /// Advertise (and serve) `subscribe` / `unsubscribe`.
        OfferSubscribe: bool
        /// The `hostVersion` reported in `hello.ok`. Informational.
        HostVersion: string
        /// Where `changed` events go.
        Emit: RelayValue -> unit
    }

[<RequireQualifiedAccess>]
module RelayOptions =

    /// Opted OUT, offering subscribe if it is ever opted in, emitting nowhere.
    /// Opting in is a host-side act: there is no message in this contract that
    /// turns the relay on, because such a message would be reachable by exactly
    /// the peer it protects against (§11.1).
    let defaults: RelayOptions =
        { OptedIn = false
          OfferApply = None
          OfferSubscribe = true
          HostVersion = "0.0.0"
          Emit = ignore }

/// The page peer: a message handler plus the subscriptions it owns.
type RelayPeer =
    {
        /// Handle one inbound message. `None` means the message must be ignored
        /// **in silence** (§3.2) — not a relay message, not a request, or
        /// uncorrelatable.
        Handle: RelayValue -> RelayValue option
        /// The capabilities this peer currently advertises (§6.3).
        Capabilities: unit -> string list
        /// Release every subscription (§8.5).
        Dispose: unit -> unit
    }

// ─── Envelope helpers ───────────────────────────────────────────────────────

let private response (id: string) (messageType: string) (payload: (string * RelayValue) list) : RelayValue =
    RelayValue.Obj
        [ RelayKey, RelayValue.Str Profile
          "dir", RelayValue.Str "response"
          "id", RelayValue.Str id
          "type", RelayValue.Str messageType
          "payload", RelayValue.Obj payload ]

let private refusalWith
    (id: string)
    (requestType: string)
    (refusalClass: string)
    (message: string)
    (detail: (string * RelayValue) list option)
    : RelayValue =
    response
        id
        "refusal"
        ([ "class", RelayValue.Str refusalClass
           "requestType", RelayValue.Str requestType
           "message", RelayValue.Str message ]
         @ (match detail with
            | None -> []
            | Some fields -> [ "detail", RelayValue.Obj fields ]))

let private refusal (id: string) (requestType: string) (refusalClass: string) (message: string) : RelayValue =
    refusalWith id requestType refusalClass message None

// ─── Profile negotiation (§5) ───────────────────────────────────────────────

/// Parse `<name>@<major>.<minor>` (the grammar `WIRE_FORMAT.md` §15.1 defines);
/// `None` when the token is not that grammar.
let parseProfile (token: string) : (string * int * int) option =
    match token.Split('@') with
    | [| name; version |] when name <> "" ->
        match version.Split('.') with
        | [| major; minor |] ->
            match System.Int32.TryParse major, System.Int32.TryParse minor with
            | (true, majorValue), (true, minorValue) -> Some(name, majorValue, minorValue)
            | _ -> None
        | _ -> None
    | _ -> None

/// `Current` / `Behind` proceed; `Foreign` — a different name or major —
/// refuses. A newer minor is safe to proceed on because within a major,
/// everything added is something an older peer may ignore (§5.2, §5.3, §10.2).
let isForeignProfile (token: string) : bool =
    match parseProfile token, parseProfile Profile with
    | Some(name, major, _), Some(ownName, ownMajor, _) -> name <> ownName || major <> ownMajor
    | _ -> true

// ─── The closed request-type set (§4.2) ─────────────────────────────────────

let private readTypes =
    [ "read.nodeState"
      "read.bindingValue"
      "read.renderedDom"
      "read.tree"
      "read.findNodes" ]

let private requestTypes =
    "hello" :: "apply" :: "subscribe" :: "unsubscribe" :: readTypes

/// The capability gating a request type. Every gated type is NAMED for its
/// capability, so authorisation is a set-membership test rather than a lookup
/// table that can drift from its entry points — the one exception being
/// `unsubscribe`, which is governed by `subscribe` (releasing what you
/// established needs no separate permission).
let private capabilityFor (requestType: string) : string =
    if requestType = "unsubscribe" then
        "subscribe"
    else
        requestType

/// The event names `relay@1.0` defines (§8.5). Unknown names are ignored, not
/// refused, provided at least one is recognised.
let private knownEvents = [ "tree" ]

// ─── The wire kind discriminator (§7.1, §1.4) ───────────────────────────────
//
// §7.1 pins `kind` to "the node's wire kind discriminator — the same token used
// as `kind.$type` in the wire format". This host's own console surface reports
// `Kind.name` instead: the kind-constraint vocabulary `HoleDecl.Slot` matches
// against, which coincides with the wire token for every kind BUT ONE —
// `NodeKind.DataGrid` is `"Grid"` there and `"DataGrid"` on the wire.
//
// A relay client filters and displays on the wire token it read from a tree, so
// `read.findNodes "DataGrid"` has to find those nodes. §1.4's rule is exactly
// this case: "A host whose local surface differs adapts at the relay boundary.
// That adaptation is the page peer's responsibility and is invisible to the
// client." So the peer adapts, rather than `Kind.name` changing under a
// vocabulary that is not the relay's to move — and a test pins the mapping
// against the canonical encoder, so a SECOND divergence is a failing build
// rather than a silently mis-reported kind.

let wireKindName (kind: NodeKind<'Msg>) : string =
    match kind with
    | NodeKind.DataGrid _ -> "DataGrid"
    | other -> DebugGlobal.kindName other

/// `DebugGlobal.introspectNode` with the wire discriminator. The traversal is
/// DebugGlobal's — only the kind projection differs.
let private introspectForRelay (node: Node<'Msg>) : DebugGlobal.NodeIntrospection =
    { DebugGlobal.introspectNode node with
        Kind = wireKindName node.Kind }

/// `DebugGlobal.inspectTree` with the wire discriminator, recursively.
let rec private inspectTreeForRelay (node: Node<'Msg>) : DebugGlobal.TreeIntrospection =
    let self = introspectForRelay node

    { Id = self.Id
      Kind = self.Kind
      Bindings = self.Bindings
      ChildIds = self.ChildIds
      Children = DebugGlobal.childNodes node |> List.map inspectTreeForRelay }

/// `DebugGlobal.findNodesByKind` matched against the wire discriminator.
let private findNodesByWireKind (kind: string) (tree: Node<'Msg>) : string list =
    DebugGlobal.walkNodes tree
    |> List.filter (fun node -> wireKindName node.Kind = kind)
    |> List.map (fun node ->
        let (NodeId raw) = node.Id
        raw)

// ─── Payload projections ────────────────────────────────────────────────────

let private slotInfoValue (slot: DebugGlobal.BindingSlotInfo) : RelayValue =
    RelayValue.Obj
        [ "slot", RelayValue.Str slot.Slot
          "expression", RelayValue.Str slot.Expression
          "source", RelayValue.Str slot.Source ]

let private nodeStatePayload (node: DebugGlobal.NodeIntrospection) : (string * RelayValue) list =
    [ "id", RelayValue.Str node.Id
      "kind", RelayValue.Str node.Kind
      "bindings", RelayValue.Arr(node.Bindings |> List.map slotInfoValue)
      "childIds", RelayValue.Arr(node.ChildIds |> List.map RelayValue.Str) ]

/// §7.2 — the §7.1 shape plus `children`, recursive, with `children` mirroring
/// `childIds` in order and a leaf carrying an empty array.
let rec private treePayload (node: DebugGlobal.TreeIntrospection) : (string * RelayValue) list =
    [ "id", RelayValue.Str node.Id
      "kind", RelayValue.Str node.Kind
      "bindings", RelayValue.Arr(node.Bindings |> List.map slotInfoValue)
      "childIds", RelayValue.Arr(node.ChildIds |> List.map RelayValue.Str)
      "children", RelayValue.Arr(node.Children |> List.map (treePayload >> RelayValue.Obj)) ]

/// §7.3 — the tagged resolution envelope. `expression` and `source` are present
/// on EVERY status, so a client can always render what the binding IS even when
/// it has no value; the bare resolution cannot be recovered into this shape,
/// which is why the tagged form is the canonical one the relay carries (§1.4).
let private bindingValuePayload (resolution: DebugGlobal.SlotResolution) : (string * RelayValue) list =
    match resolution with
    | DebugGlobal.SlotResolution.Resolved(value, expression, source) ->
        [ "status", RelayValue.Str "resolved"
          "value", RelayValue.Opaque value
          "expression", RelayValue.Str expression
          "source", RelayValue.Str source ]
    | DebugGlobal.SlotResolution.NotResolved(expression, source) ->
        [ "status", RelayValue.Str "notResolved"
          "expression", RelayValue.Str expression
          "source", RelayValue.Str source ]
    | DebugGlobal.SlotResolution.Errored(message, expression, source) ->
        [ "status", RelayValue.Str "errored"
          "message", RelayValue.Str message
          "expression", RelayValue.Str expression
          "source", RelayValue.Str source ]
    | DebugGlobal.SlotResolution.I18nUnresolved(key, expression, source) ->
        [ "status", RelayValue.Str "i18nUnresolved"
          "key", RelayValue.Str key
          "expression", RelayValue.Str expression
          "source", RelayValue.Str source ]
    | DebugGlobal.SlotResolution.NoOverride ->
        [ "status", RelayValue.Str "noOverride"
          "expression", RelayValue.Str "$none"
          "source", RelayValue.Str "Static" ]
    // Unreachable: the caller refuses SLOT_NOT_DECLARED before projecting. Kept
    // total rather than throwing — a peer that cannot answer must still answer.
    | DebugGlobal.SlotResolution.NotDeclared ->
        [ "status", RelayValue.Str "noOverride"
          "expression", RelayValue.Str "$none"
          "source", RelayValue.Str "Static" ]

let private geometryPayload (geometry: DebugGlobal.NodeGeometry) : (string * RelayValue) list =
    [ "x", RelayValue.Num geometry.X
      "y", RelayValue.Num geometry.Y
      "width", RelayValue.Num geometry.Width
      "height", RelayValue.Num geometry.Height
      "overflowing", RelayValue.Bool geometry.Overflowing
      "hidden", RelayValue.Bool geometry.Hidden ]

// ─── The peer ───────────────────────────────────────────────────────────────

/// Build a relay page peer over a live-surface lookup.
///
/// The surface is looked up PER REQUEST, never captured: the host rebuilds it on
/// every committed tree change, and a peer holding one instance would answer
/// from a stale tree. The peer itself outlives those rebuilds, which is exactly
/// why it — and not the surface — owns the client's subscriptions.
let createPeer (surfaceSource: unit -> RelaySurface option) (options: RelayOptions) : RelayPeer =
    // The subscription registry is per-peer mutable state by nature: a
    // subscription is a live relationship with a client, established by one
    // message and released by another.
    let subscriptions = ResizeArray<string * (unit -> unit)>()
    let mutable subscriptionCounter = 0

    let capabilities () : string list =
        if not options.OptedIn then
            []
        else
            match surfaceSource () with
            | None -> []
            | Some surface ->
                let offerApply = defaultArg options.OfferApply surface.CanApply

                readTypes
                @ (if offerApply then [ "apply" ] else [])
                @ (if options.OfferSubscribe then [ "subscribe" ] else [])

    // ── read entry points (§7) ──────────────────────────────────────────────

    let malformed (id: string) (requestType: string) (message: string) (path: string) =
        refusalWith id requestType "MALFORMED_MESSAGE" message (Some [ "path", RelayValue.Str path ])

    let readNodeState (surface: RelaySurface) id requestType payload =
        match RelayValue.stringField "nodeId" payload with
        | None -> malformed id requestType "nodeId must be a string." "payload.nodeId"
        | Some nodeId ->
            match surface.NodeState nodeId with
            | None ->
                refusalWith
                    id
                    requestType
                    "NODE_NOT_FOUND"
                    (sprintf "Node '%s' not found in tree." nodeId)
                    (Some [ "nodeId", RelayValue.Str nodeId ])
            | Some node -> response id (requestType + ".ok") (nodeStatePayload node)

    let readBindingValue (surface: RelaySurface) id requestType payload =
        match RelayValue.stringField "nodeId" payload, RelayValue.stringField "slot" payload with
        | None, _ -> malformed id requestType "nodeId must be a string." "payload.nodeId"
        | _, None -> malformed id requestType "slot must be a string." "payload.slot"
        | Some nodeId, Some slot ->
            match surface.ResolveSlot nodeId slot with
            | SlotLookup.NodeMissing ->
                refusalWith
                    id
                    requestType
                    "NODE_NOT_FOUND"
                    (sprintf "Node '%s' not found in tree." nodeId)
                    (Some [ "nodeId", RelayValue.Str nodeId ])
            // `noOverride` (a declared slot holding nothing) is a STATE and gets
            // an `.ok`; `SLOT_NOT_DECLARED` (not a binding slot on this kind at
            // all) is a client error and gets a refusal. Conflating them tells
            // the client its question was wrong when it was not (§7.3).
            | SlotLookup.Slot(DebugGlobal.SlotResolution.NotDeclared, kind) ->
                refusalWith
                    id
                    requestType
                    "SLOT_NOT_DECLARED"
                    (sprintf "Slot '%s' is not a binding slot on node '%s' (kind=%s)." slot nodeId kind)
                    (Some
                        [ "nodeId", RelayValue.Str nodeId
                          "slot", RelayValue.Str slot
                          "kind", RelayValue.Str kind ])
            | SlotLookup.Slot(resolution, _) -> response id (requestType + ".ok") (bindingValuePayload resolution)

    let readRenderedDom (surface: RelaySurface) id requestType payload =
        match RelayValue.stringField "nodeId" payload with
        | None -> malformed id requestType "nodeId must be a string." "payload.nodeId"
        | Some nodeId ->
            match surface.Geometry nodeId with
            | Some geometry -> response id (requestType + ".ok") (geometryPayload geometry)
            | None ->
                // "In the tree but not on screen" and "no such node" are both
                // NODE_NOT_FOUND, distinguished by `reason` so a client can say
                // which (§7.4). The TREE is the arbiter, not the DOM.
                let inTree = (surface.NodeState nodeId).IsSome

                refusalWith
                    id
                    requestType
                    "NODE_NOT_FOUND"
                    (sprintf "No rendered element for node '%s'." nodeId)
                    (Some(
                        [ "nodeId", RelayValue.Str nodeId ]
                        @ (if inTree then
                               [ "reason", RelayValue.Str "not-rendered" ]
                           else
                               [])
                    ))

    let readFindNodes (surface: RelaySurface) id requestType payload =
        match RelayValue.stringField "kind" payload with
        | None -> malformed id requestType "kind must be a string." "payload.kind"
        // An unmatched — or unrecognised — kind is `[]`, never a refusal: "which
        // nodes have this kind" has the honest answer "none" (§7.5).
        | Some kind ->
            response
                id
                (requestType + ".ok")
                [ "nodeIds", RelayValue.Arr(surface.FindNodes kind |> List.map RelayValue.Str) ]

    // ── apply (§8) ──────────────────────────────────────────────────────────

    let applyOp (surface: RelaySurface) id requestType payload =
        match RelayValue.field "op" payload with
        | Some op when RelayValue.isObject op ->
            // `attribution` is advisory and UNTRUSTED (§8.2): it is read for
            // nothing here, grants nothing, and influences no decision below. A
            // host must not grant privilege on an unprivileged peer's label.
            match surface.Apply(toJson op) with
            | DebugGlobal.ApplyResult.Applied treeRevision ->
                response
                    id
                    (requestType + ".ok")
                    [ "applied", RelayValue.Bool true; "treeRevision", RelayValue.Str treeRevision ]
            | DebugGlobal.ApplyResult.Denied _ ->
                // `detail` stays empty by design — explaining WHY policy refused
                // hands out a map of the policy (§11.5).
                refusal id requestType "POLICY_DENIED" "The host's policy layer refused this mutation."
            | DebugGlobal.ApplyResult.DecodeFailed(message, detail) ->
                match detail with
                // §9.3 carries the wire format's own `DecodeError` VERBATIM,
                // PascalCase field names included — it is that envelope, not a
                // re-spelling of it.
                | Some e ->
                    refusalWith
                        id
                        requestType
                        "DECODE_FAILED"
                        "The op is not a recognised TreeOp."
                        (Some(
                            [ "Code", RelayValue.Str e.Code
                              "Path", RelayValue.Str e.Path
                              "Message", RelayValue.Str e.Message ]
                            @ (match e.ExpectedShape with
                               | Some shape -> [ "ExpectedShape", RelayValue.Str shape ]
                               | None -> [])
                        ))
                // A host that gave no structured diagnostic still owes the
                // client the prose one — `detail` is optional and a client MUST
                // tolerate its absence (§9.1).
                | None -> refusal id requestType "DECODE_FAILED" message
            | DebugGlobal.ApplyResult.Rejected(message, code) ->
                refusalWith
                    id
                    requestType
                    "VALIDATOR_REJECT"
                    message
                    (match code with
                     | None -> None
                     | Some c -> Some [ "code", RelayValue.Str c ])
            | DebugGlobal.ApplyResult.Unwired message ->
                // Unreachable while `apply` is advertised only when it is wired;
                // kept so a mis-declared capability still refuses honestly rather
                // than claiming the op was illegal.
                refusalWith id requestType "CAPABILITY_ABSENT" message (Some [ "capability", RelayValue.Str "apply" ])
        | _ -> malformed id requestType "op must be an embedded JSON object." "payload.op"

    // ── subscribe / unsubscribe (§8.5) ──────────────────────────────────────

    let subscribe (surface: RelaySurface) id requestType payload =
        match RelayValue.field "events" payload |> Option.bind RelayValue.asList with
        | None
        | Some [] -> malformed id requestType "events must be a non-empty array." "payload.events"
        | Some events ->
            // Unknown event names are IGNORED, not refused, provided at least
            // one is recognised — that is what makes "additive is a minor bump"
            // safe (§10.2).
            let accepted =
                events
                |> List.choose RelayValue.asString
                |> List.filter (fun name -> List.contains name knownEvents)

            if List.isEmpty accepted then
                malformed id requestType "No recognised event name in events." "payload.events"
            else
                subscriptionCounter <- subscriptionCounter + 1
                let subscriptionId = "s-" + string subscriptionCounter

                let release =
                    surface.Subscribe(fun change ->
                        options.Emit(
                            RelayValue.Obj
                                [ RelayKey, RelayValue.Str Profile
                                  "dir", RelayValue.Str "event"
                                  // The event carries the id of the `subscribe`
                                  // request that established it, so a client
                                  // routes it without holding extra state (§4.1).
                                  "id", RelayValue.Str id
                                  "type", RelayValue.Str "changed"
                                  "payload",
                                  RelayValue.Obj
                                      [ "subscriptionId", RelayValue.Str subscriptionId
                                        "event", RelayValue.Str "tree"
                                        "treeRevision", RelayValue.Str change.TreeRevision
                                        "cause", RelayValue.Str(ChangeHub.ChangeCause.toWire change.Cause) ] ]
                        ))

                subscriptions.Add(subscriptionId, release)

                response
                    id
                    (requestType + ".ok")
                    [ "subscriptionId", RelayValue.Str subscriptionId
                      "events", RelayValue.Arr(accepted |> List.map RelayValue.Str)
                      "treeRevision", RelayValue.Str(surface.TreeRevision()) ]

    let unsubscribe id requestType payload =
        match RelayValue.stringField "subscriptionId" payload with
        | None -> malformed id requestType "subscriptionId must be a string." "payload.subscriptionId"
        | Some subscriptionId ->
            // Releasing an unknown or already-released subscription is `ok`, not
            // a refusal — the caller's desired end state is reached either way
            // (§8.5).
            match subscriptions |> Seq.tryFindIndex (fun (sid, _) -> sid = subscriptionId) with
            | Some index ->
                let (_, release) = subscriptions[index]
                release ()
                subscriptions.RemoveAt index
            | None -> ()

            response id (requestType + ".ok") [ "subscriptionId", RelayValue.Str subscriptionId ]

    // ── dispatch ────────────────────────────────────────────────────────────

    let handle (message: RelayValue) : RelayValue option =
        match RelayValue.stringField RelayKey message with
        | None -> None
        | Some clientProfile ->
            // A peer ignores a direction it does not handle: a page peer serves
            // requests and ignores responses and events (§4, §10.4).
            if RelayValue.stringField "dir" message <> Some "request" then
                None
            else
                match RelayValue.stringField "id" message, RelayValue.stringField "type" message with
                // Without a correlatable id or a type token there is nothing to
                // answer TO; a reply could not be routed, so silence is the only
                // honest outcome.
                | None, _
                | _, None -> None
                | Some id, Some requestType ->
                    // Negotiation runs on EVERY request, not only on `hello` — a
                    // client cannot be assumed to keep its profile constant, and
                    // the check is a string comparison (§5.2).
                    if isForeignProfile clientProfile then
                        Some(
                            refusalWith
                                id
                                requestType
                                "FOREIGN_PROFILE"
                                "Incompatible relay profile."
                                (Some
                                    [ "received", RelayValue.Str clientProfile
                                      "supported", RelayValue.Arr [ RelayValue.Str Profile ] ])
                        )
                    // Opt-out short-circuits everything, including `hello`, and
                    // reaches the surface for nothing (§9.3). Nothing about the
                    // request is examined.
                    elif not options.OptedIn then
                        Some(refusal id requestType "NOT_OPTED_IN" "The relay is not enabled on this host.")
                    else
                        match RelayValue.field "payload" message with
                        | None -> Some(malformed id requestType "payload must be an object." "payload")
                        | Some payload when not (RelayValue.isObject payload) ->
                            Some(malformed id requestType "payload must be an object." "payload")
                        | Some payload ->
                            match surfaceSource () with
                            | None ->
                                Some(refusal id requestType "NOT_OPTED_IN" "The relay is not enabled on this host.")
                            | Some surface ->
                                if requestType = "hello" then
                                    match RelayValue.field "accepts" payload |> Option.bind RelayValue.asList with
                                    | None
                                    | Some [] ->
                                        Some(
                                            malformed
                                                id
                                                requestType
                                                "accepts must be a non-empty array."
                                                "payload.accepts"
                                        )
                                    | Some accepts ->
                                        // The profile named in `hello.ok` MUST be
                                        // one the client listed (§6.3).
                                        if
                                            not (accepts |> List.exists (fun a -> RelayValue.asString a = Some Profile))
                                        then
                                            Some(
                                                refusalWith
                                                    id
                                                    requestType
                                                    "FOREIGN_PROFILE"
                                                    "Incompatible relay profile."
                                                    (Some
                                                        [ "received", RelayValue.Str clientProfile
                                                          "supported", RelayValue.Arr [ RelayValue.Str Profile ] ])
                                            )
                                        else
                                            Some(
                                                response
                                                    id
                                                    "hello.ok"
                                                    [ "host", RelayValue.Str HostName
                                                      "hostVersion", RelayValue.Str options.HostVersion
                                                      "surfaceVersion", RelayValue.Str surface.SurfaceVersion
                                                      "profile", RelayValue.Str Profile
                                                      "capabilities",
                                                      RelayValue.Arr(capabilities () |> List.map RelayValue.Str)
                                                      "treeRevision", RelayValue.Str(surface.TreeRevision()) ]
                                            )
                                // A type outside the closed set is
                                // UNKNOWN_MESSAGE; a type INSIDE it whose
                                // capability was not advertised is
                                // CAPABILITY_ABSENT. Reporting the wrong one tells
                                // the client something false — either that a real
                                // entry point does not exist, or that a
                                // non-existent one is merely switched off (§10.1).
                                elif not (List.contains requestType requestTypes) then
                                    Some(
                                        refusalWith
                                            id
                                            requestType
                                            "UNKNOWN_MESSAGE"
                                            "Unrecognised message type."
                                            (Some [ "received", RelayValue.Str requestType ])
                                    )
                                else
                                    let capability = capabilityFor requestType
                                    // Re-checked per request: capability
                                    // advertisement is discovery, not a security
                                    // boundary, and a client is not a trusted
                                    // component (§11.3).
                                    if not (List.contains capability (capabilities ())) then
                                        Some(
                                            refusalWith
                                                id
                                                requestType
                                                "CAPABILITY_ABSENT"
                                                (sprintf "This host does not offer the %s capability." capability)
                                                (Some [ "capability", RelayValue.Str capability ])
                                        )
                                    else
                                        match requestType with
                                        | "read.nodeState" -> Some(readNodeState surface id requestType payload)
                                        | "read.tree" ->
                                            Some(
                                                response id (requestType + ".ok") (treePayload (surface.InspectTree()))
                                            )
                                        | "read.bindingValue" -> Some(readBindingValue surface id requestType payload)
                                        | "read.renderedDom" -> Some(readRenderedDom surface id requestType payload)
                                        | "read.findNodes" -> Some(readFindNodes surface id requestType payload)
                                        | "apply" -> Some(applyOp surface id requestType payload)
                                        | "subscribe" -> Some(subscribe surface id requestType payload)
                                        | "unsubscribe" -> Some(unsubscribe id requestType payload)
                                        | _ ->
                                            Some(
                                                refusalWith
                                                    id
                                                    requestType
                                                    "UNKNOWN_MESSAGE"
                                                    "Unrecognised message type."
                                                    (Some [ "received", RelayValue.Str requestType ])
                                            )

    { Handle = handle
      Capabilities = capabilities
      Dispose =
        fun () ->
            for (_, release) in List.ofSeq subscriptions do
                release ()

            subscriptions.Clear() }

// ─── Building a surface over the live tree ──────────────────────────────────

/// Project the host's live tree + sources into the surface the peer consumes.
/// Compiles on both pipelines: geometry is the only browser-dependent leg, and
/// `DebugGlobal.readGeometry` already answers `None` where there is no DOM.
let surfaceOf
    (tree: Node<'Msg>)
    (sources: BindingResolver.BindingSources)
    (runtime: Runtime.IFuaranRuntime)
    (options: DebugGlobal.DebugOptions)
    : RelaySurface =
    { SurfaceVersion = DebugGlobal.Version
      CanApply = options.ApplyHandler.IsSome
      TreeRevision = options.Hub.Revision
      Subscribe = options.Hub.Subscribe
      NodeState = fun id -> DebugGlobal.findNode id tree |> Option.map introspectForRelay
      InspectTree = fun () -> inspectTreeForRelay tree
      ResolveSlot =
        fun id slot ->
            match DebugGlobal.findNode id tree with
            | None -> SlotLookup.NodeMissing
            | Some node -> SlotLookup.Slot(DebugGlobal.resolveSlot sources node.Kind slot, wireKindName node.Kind)
      Geometry = DebugGlobal.readGeometry
      FindNodes = fun kind -> findNodesByWireKind kind tree
      Apply = DebugGlobal.applyResult runtime options }

// ─── The published live surface ─────────────────────────────────────────────
//
// A peer installed once must reach the surface built by the LATEST render. That
// is page-global state by nature — it is the typed twin of `window.__fuaran`,
// which is page-global for the same reason — so the cell lives here rather than
// being threaded through a host's model. `publish` is called from the render
// path; `withdraw` from the teardown that unregisters the console global.

let mutable private publishedSurface: RelaySurface option = None

/// Publish the surface built by this render, for an installed peer to read.
let publish (surface: RelaySurface) : unit = publishedSurface <- Some surface

/// Withdraw the published surface. After this a peer answers `NOT_OPTED_IN`,
/// which is the honest answer: there is no live host behind it.
let withdraw () : unit = publishedSurface <- None

/// The live-surface lookup a peer installed with no explicit source uses.
let published () : RelaySurface option = publishedSurface

/// Should a relay listener be installed at all? The host's `relay` opt-in AND a
/// DEBUG build, mirroring `DebugGlobal.shouldRegister` — a Release build makes
/// this constant-false, so the installation is dead-code-eliminated and a
/// production bundle cannot expose the relay even if the flag is set wrongly
/// (§11.1's "gate it behind a development build in addition to the runtime
/// opt-in").
let shouldInstall (relay: bool) : bool = relay && DebugGlobal.compiledInDebug

/// Register the console global AND publish the relay surface, in one call from
/// the host's render path. `relay` is the relay opt-in; leaving it `false` — the
/// default posture — publishes nothing, so an installed peer has no host to
/// answer for and a never-installed peer is simply absent.
let registerAndPublish
    (debug: bool)
    (relay: bool)
    (tree: Node<'Msg>)
    (sources: BindingResolver.BindingSources)
    (runtime: Runtime.IFuaranRuntime)
    (options: DebugGlobal.DebugOptions)
    : unit =
    DebugGlobal.registerWith debug tree sources runtime options

    if DebugGlobal.shouldRegister debug && shouldInstall relay then
        publish (surfaceOf tree sources runtime options)

#if FABLE_COMPILER

// ─── Transport (§3) — the only browser-dependent part of this module ────────

[<Emit("$0 !== null && typeof $0 === 'object' && !Array.isArray($0)")>]
let private isJsObject (value: obj) : bool = jsNative

[<Emit("typeof $0 === 'string'")>]
let private isJsString (value: obj) : bool = jsNative

[<Emit("typeof $0 === 'number'")>]
let private isJsNumber (value: obj) : bool = jsNative

[<Emit("typeof $0 === 'boolean'")>]
let private isJsBoolean (value: obj) : bool = jsNative

[<Emit("Array.isArray($0)")>]
let private isJsArray (value: obj) : bool = jsNative

[<Emit("Object.keys($0)")>]
let private jsKeys (value: obj) : string[] = jsNative

[<Emit("$0[$1]")>]
let private jsGet (value: obj) (key: string) : obj = jsNative

[<Emit("$0[$1] = $2")>]
let private jsSet (target: obj) (key: string) (value: obj) : unit = jsNative

[<Emit("({})")>]
let private jsNewObject () : obj = jsNative

/// Read a live JS value into the typed relay shape. Unknown/exotic values (a
/// function, a symbol, `undefined`) read as `Null` — a peer must degrade rather
/// than throw on anything a client can post (§10).
let rec ofJs (value: obj) : RelayValue =
    if isNull value then
        RelayValue.Null
    elif isJsString value then
        RelayValue.Str(unbox<string> value)
    elif isJsNumber value then
        RelayValue.Num(unbox<float> value)
    elif isJsBoolean value then
        RelayValue.Bool(unbox<bool> value)
    elif isJsArray value then
        RelayValue.Arr(unbox<obj[]> value |> Array.toList |> List.map ofJs)
    elif isJsObject value then
        RelayValue.Obj(jsKeys value |> Array.toList |> List.map (fun k -> k, ofJs (jsGet value k)))
    else
        RelayValue.Null

/// Materialise a typed relay value as a live JS value for `postMessage`.
let rec toJs (value: RelayValue) : obj =
    match value with
    | RelayValue.Null -> null
    | RelayValue.Bool b -> box b
    | RelayValue.Num n -> box n
    | RelayValue.Str s -> box s
    | RelayValue.Opaque raw -> raw
    | RelayValue.Arr items -> box (items |> List.map toJs |> Array.ofList)
    | RelayValue.Obj fields ->
        let target = jsNewObject ()

        for (key, fieldValue) in fields do
            jsSet target key (toJs fieldValue)

        target

/// The mandatory receive-side checks (§3.2), in order. **All four must pass**;
/// a failing message is ignored in silence.
///
/// The `source === window` check is not redundant with the origin check: a
/// SAME-ORIGIN frame passes the origin check and is still a different document
/// with a different trust story (§11.2).
[<Emit("""(function(ev){
  if (typeof window === 'undefined') return false;
  if (ev.source !== window) return false;
  if (ev.origin !== window.origin) return false;
  var d = ev.data;
  if (d === null || typeof d !== 'object' || Array.isArray(d)) return false;
  return typeof d['$relay'] === 'string';
})($0)""")>]
let acceptsRelayMessage (event: obj) : bool = jsNative

[<Emit("window.postMessage($0, window.origin)")>]
let private postToOwnWindow (message: obj) : unit = jsNative

[<Emit("window.addEventListener($0, $1)")>]
let private addWindowListener (name: string) (handler: obj -> unit) : unit = jsNative

[<Emit("window.removeEventListener($0, $1)")>]
let private removeWindowListener (name: string) (handler: obj -> unit) : unit = jsNative

[<Emit("$0.data")>]
let private eventData (event: obj) : obj = jsNative

[<Emit("typeof window !== 'undefined'")>]
let private hasWindow () : bool = jsNative

/// Install a relay page peer on the page's own window: a `message` listener
/// applying §3.2's checks, with the reply posted at `window.origin` — never
/// `"*"`, which is readable by any document that can obtain a reference to this
/// window, a real disclosure of application state on a page with embedded
/// frames (§3.3, §11.2).
///
/// Returns a teardown handle that removes the listener and releases every
/// subscription; subscriptions are also released on page unload (§8.5). A no-op
/// (returning an inert handle) where there is no `window`.
let installPeer (surfaceSource: unit -> RelaySurface option) (options: RelayOptions) : unit -> unit =
    if not (hasWindow ()) then
        (fun () -> ())
    else
        let peer =
            createPeer
                surfaceSource
                { options with
                    Emit = fun event -> postToOwnWindow (toJs event) }

        let listener (event: obj) =
            if acceptsRelayMessage event then
                match peer.Handle(ofJs (eventData event)) with
                | Some reply -> postToOwnWindow (toJs reply)
                | None -> ()

        let onUnload (_: obj) = peer.Dispose()

        addWindowListener "message" listener
        addWindowListener "pagehide" onUnload

        fun () ->
            removeWindowListener "message" listener
            removeWindowListener "pagehide" onUnload
            peer.Dispose()

/// Install the peer over the published surface, opted in — the one-line host
/// call. **Only under `shouldInstall relay`**: without the opt-in NO listener is
/// installed, so a probe gets no answer whatsoever, which is §11.1's preferred
/// production posture (absent, not merely inert). A host that wants the honest
/// development posture instead — a peer that answers `NOT_OPTED_IN` — installs
/// `installPeer published { RelayOptions.defaults with OptedIn = false }`
/// directly.
let install (relay: bool) (hostVersion: string) : unit -> unit =
    if not (shouldInstall relay) then
        (fun () -> ())
    else
        installPeer
            published
            { RelayOptions.defaults with
                OptedIn = true
                HostVersion = hostVersion }

#else

/// Non-Fable hosts (the .NET test runner / SSR) have no `window`, so there is no
/// transport to install. The whole protocol above is still exercised by the .NET
/// runner against the shared conformance corpus — the peer is pure, and only
/// this transport is not.
let installPeer (_surfaceSource: unit -> RelaySurface option) (_options: RelayOptions) : unit -> unit = (fun () -> ())

let install (_relay: bool) (_hostVersion: string) : unit -> unit = (fun () -> ())

#endif
