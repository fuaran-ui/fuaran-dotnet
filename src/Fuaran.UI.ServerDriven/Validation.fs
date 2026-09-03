module Fuaran.UI.ServerDriven.Validation

open Fuaran.UI.Types
open Fuaran.UI.Ops.Introspect

// ============================================================================
//  G1 — the inbound trust boundary (Phase 152, Track C). NON-NEGOTIABLE.
//
//  The browser shim sends *raw* `(nodeId, event, payload)`. The server-driven
//  runtime must NOT trust any of it: a forged / stale `nodeId`, an event that
//  doesn't belong to the node's `NodeKind`, an out-of-range payload, or an
//  action the host policy forbids are all attack surface — and the app classes
//  that most want a server-driven tier (business / regulated / multi-tenant)
//  are exactly the ones an authorization-bypass or injection breaks.
//
//  This module is the default-deny gate (FGP 3) applied to the inbound path —
//  the server-side mirror of the client `runAction` dispatch gate (the
//  `CanDispatch` consultation that today runs only in the renderer). It is a
//  **pure, Fable-clean** function of `(tree, event)` → `Result`, so it is fully
//  unit-testable headlessly and carries no transport / renderer dependency.
//
//  Four checks, in order (first failure wins, default-deny):
//    (a) the `nodeId` exists in the CURRENT server-side tree (`findNode`);
//    (b) the `event` is legitimate for the node's `NodeKind`
//        (`legitimateEvents`) — a Button accepts a click, a Select a change;
//    (c) the `payload` is in-bounds for the node (`boundsCheck`) — a Select's
//        value must be one of its statically-resolved options;
//    (d) the resolved `Action<'Msg>` passes the host **dispatch policy gate**
//        (`canDispatch`, injected — the host maps `Action` → its renderer
//        `ActionDescriptor` → `IFuaranRuntime.CanDispatch`, so this core stays
//        renderer-free).
//
//  Output on success: the resolved target `Node` + the resolved `Action<'Msg>`
//  (the author's typed handler applied to the validated payload), ready for the
//  driver to interpret. A legitimate event that resolves to no action (e.g. a
//  not-yet-server-resolvable file-select) succeeds with `Action = None` — the
//  driver no-ops it.
// ============================================================================

/// A single payload value off the wire — the closure-free, portable subset the
/// shim sends (`{ "value": "x" }` / `{ "checked": true }` / `{ "index": 2 }`).
/// The transport backend (Track D) parses inbound JSON into this; keeping it a
/// small DU (not an obj-erased carrier) makes the bounds checks precise + Fable-clean.
[<RequireQualifiedAccess>]
type LiveValue =
    | Str of string
    | Num of float
    | Bool of bool
    | Null

/// A raw inbound event from the browser shim. **UNTRUSTED** until `validate`
/// returns `Ok`.
type LiveEvent =
    { ConnId: string
      NodeId: string
      Event: string
      Payload: Map<string, LiveValue>
      LastSeq: int }

/// Why the trust boundary rejected an inbound event. Every reject path produces
/// one of these — the driver turns it into a structured error patch + an audit
/// event, never a state mutation.
[<RequireQualifiedAccess>]
type RejectReason =
    /// (a) No node with this id in the current server-side tree (stale or forged).
    | UnknownNode of nodeId: string
    /// (b) The event is not one the node's kind accepts.
    | IllegitimateEvent of nodeId: string * event: string * kind: string
    /// (c) The payload is out of the node's value space. `detail` says WHICH
    /// bound was missed, never the submitted value — see the redaction note on
    /// `describe`.
    | PayloadOutOfBounds of nodeId: string * detail: string
    /// (d) The resolved action was denied by the host dispatch policy gate.
    | DispatchDenied of nodeId: string * action: string

module RejectReason =
    /// A short, audit-log-shaped description (no payload values — those may be
    /// sensitive; the reason + node id + kind are the safe-to-log surface).
    ///
    /// **Phase 787 — that promise is now kept by CONSTRUCTION rather than by
    /// discipline.** It used to be false: `PayloadOutOfBounds` built its `detail`
    /// by interpolating the client's submitted value, and the SSE backend logs
    /// every rejection by default, so end-user form input reached an always-on
    /// host log through a function whose own doc comment said it could not. The
    /// three bounds checks below now name the bound that was missed and withhold
    /// the value that missed it, so there is no redaction step here to forget —
    /// the value never enters the reason.
    ///
    /// An author-declared NAME is deliberately still carried (a filter's name,
    /// a node id): that is grade B in `docs/ACTION-LOG-PRIVACY.md`'s vocabulary
    /// and is what makes the line diagnosable at all. What is withheld is grade
    /// C — anything the user typed or chose. A host that needs the offending
    /// value while developing logs the inbound event itself; that is a
    /// deliberate host opt-in at wiring time, which is the same party
    /// `ActionCaptureMode` puts the choice to.
    let describe (r: RejectReason) : string =
        match r with
        | RejectReason.UnknownNode id -> sprintf "unknown node '%s' (stale or forged id)" id
        | RejectReason.IllegitimateEvent(id, ev, kind) ->
            sprintf "event '%s' illegitimate for node '%s' (%s)" ev id kind
        | RejectReason.PayloadOutOfBounds(id, detail) -> sprintf "payload out of bounds for node '%s': %s" id detail
        | RejectReason.DispatchDenied(id, act) -> sprintf "dispatch denied by policy for node '%s': %s" id act

/// The successfully-validated, gated event — the boundary's output. `Action` is
/// `None` for a legitimate event with no server-resolvable action yet (the
/// driver no-ops). The driver interprets `Action`.
type ValidatedEvent<'Msg> =
    { Node: Node<'Msg>
      Action: Action<'Msg> option }

// ─── (b) event legitimacy ───────────────────────────────────────────────────

/// The DOM event names the shim may legitimately deliver for a node's kind.
/// Everything not interactive returns the empty set (default-deny: a `Heading`
/// or `Markdown` node accepts no events).
let legitimateEvents (node: Node<'Msg>) : Set<string> =
    match node.Kind with
    | NodeKind.Button _ -> set [ "click" ]
    | NodeKind.Select _ -> set [ "change" ]
    // A Form node receives its submit, plus field-level change/input that
    // bubble to the form (per-field addressing is the driver's form policy).
    //
    // Phase 1113 — a `Combobox` field needs no row of its own: it delivers
    // `input` as the reader types and `change` on commit, and BOTH already
    // bubble to the enclosing Form here (and to Filters below). The allow-list
    // is keyed by the node that RECEIVES the event, never by the control that
    // produced it, so a new field control widens no boundary — which is the
    // property worth stating rather than re-deriving at the next control.
    | NodeKind.Form _ -> set [ "submit"; "change"; "input" ]
    // Field-level change/input bubble to the Filters node; a segmented
    // filter's horizontal shape is per-option buttons, so its selection
    // arrives as a bubbled click (mirrors the tab-header path).
    | NodeKind.Filters _ -> set [ "change"; "input"; "click" ]
    // Phase 1115 — `dropTarget` / `acceptPaste` need no row of their own, and
    // the reason is stronger than the combobox's above: they are not new events
    // at all. A conformant client tier writes a dropped or pasted file into the
    // control's own `<input type="file">` and lets the input's ordinary `change`
    // fire, so an ingested file is INDISTINGUISHABLE here from a picked one —
    // same event name, same element, same `input.files` for `ReadFileBody` to
    // read. Widening this row would have implied a per-GESTURE keying the file
    // does not have, and admitting a `drop` or `paste` event name would have
    // opened a boundary nothing on the wire asks for.
    | NodeKind.FileUpload _ -> set [ "change"; "file-read" ]
    // Tab headers + step headers + disclosure summaries arrive as
    // bubbled clicks.
    | NodeKind.Tabs _ -> set [ "click"; "change" ]
    | NodeKind.Stepper _ -> set [ "click"; "change" ]
    | NodeKind.Disclosure _ -> set [ "click"; "change"; "toggle" ]
    | _ -> Set.empty

// ─── payload accessors ──────────────────────────────────────────────────────

let private tryStr key (p: Map<string, LiveValue>) =
    match Map.tryFind key p with
    | Some(LiveValue.Str s) -> Some s
    | _ -> None

let private tryBool key (p: Map<string, LiveValue>) =
    match Map.tryFind key p with
    | Some(LiveValue.Bool b) -> Some b
    | _ -> None

let private tryNum key (p: Map<string, LiveValue>) =
    match Map.tryFind key p with
    | Some(LiveValue.Num n) -> Some n
    | _ -> None

/// The `string option` a Select / Choice change carries: `Str` → `Some`,
/// explicit `Null` (or absent) → `None`.
let private selectValue (p: Map<string, LiveValue>) : string option = tryStr "value" p

// ─── (c) payload bounds ─────────────────────────────────────────────────────

/// Check the payload is within the node's value space. Only the bounds we can
/// resolve *server-side without binding context* are enforced here (a Select
/// whose options are `Binding.Static`); dynamic bindings + per-field form bounds
/// are enforced downstream at interpret time. Returns `Ok ()` when in-bounds.
let private boundsCheck (node: Node<'Msg>) (ev: LiveEvent) : Result<unit, RejectReason> =
    match node.Kind with
    | NodeKind.Select(spec) when ev.Event = "change" ->
        match spec.Source, selectValue ev.Payload with
        | Binding.Static(Some options), Some chosen ->
            if options |> List.exists (fun o -> o.Value = chosen) then
                Ok()
            else
                // Phase 787 — the chosen value is the user's; name the bound, not it.
                Error(RejectReason.PayloadOutOfBounds(ev.NodeId, "submitted value is not among the select's options"))
        // Dynamic option source, or a clear-to-none change: accept (bounds
        // enforced at interpret time against live binding sources).
        | _ -> Ok()
    | NodeKind.Filters(spec) ->
        // A name-addressed filter event must name a filter the node declares
        // (a forged / stale name is attack surface, same as a forged nodeId),
        // and a choice-shaped filter with statically-resolved options must
        // receive one of them. Events without a `name` resolve to no action
        // downstream (the driver no-ops) — nothing to bound here.
        match tryStr "name" ev.Payload with
        | None -> Ok()
        | Some name ->
            match spec.Items |> List.tryFind (fun f -> f.Name = name) with
            | None ->
                // Phase 787 — an UNMATCHED name is client-supplied text, not an
                // author-declared one, so it is withheld like any other value.
                Error(RejectReason.PayloadOutOfBounds(ev.NodeId, "named filter is not declared on this node"))
            | Some f ->
                let checkOptions (options: Binding<SelectOption list>) =
                    match options, selectValue ev.Payload with
                    | Binding.Static(Some opts), Some chosen when chosen <> "" ->
                        if opts |> List.exists (fun o -> o.Value = chosen) then
                            Ok()
                        else
                            // Phase 787 — `name` matched a declared filter, so it is
                            // author-declared (grade B) and stays; `chosen` is the
                            // user's and goes.
                            Error(
                                RejectReason.PayloadOutOfBounds(
                                    ev.NodeId,
                                    sprintf "submitted value is not among filter '%s' options" name
                                )
                            )
                    // Dynamic option source, or a clear-to-none change: accept.
                    | _ -> Ok()

                match f.Kind with
                | FormFieldKind.Choice(options, _, _) -> checkOptions options
                | FormFieldKind.SegmentedChoice(options, _, _, _) -> checkOptions options
                // Phase 1113 — a combobox is bounds-checked exactly when it
                // DECLARED itself bounded. `allowFreeText = true` is the author
                // saying an off-list value is legitimate, so refusing one would
                // refuse the shape the document asked for; `false` is the
                // searchable select, and gets the select's check.
                | FormFieldKind.Combobox(false, _, options, _) -> checkOptions options
                | FormFieldKind.Combobox(true, _, _, _) -> Ok()
                | _ -> Ok()
    | _ -> Ok()

// ─── action resolution ──────────────────────────────────────────────────────

/// Resolve the typed `Action<'Msg>` for a legitimate `(node, event, payload)`
/// by applying the author's handler to the validated payload. `None` when the
/// kind/event pair has no server-resolvable action yet (FileUpload's
/// browser-held file select; bubbled form-field change without per-field
/// addressing) — the driver no-ops those.
let resolveAction (node: Node<'Msg>) (ev: LiveEvent) : Action<'Msg> option =
    match node.Kind with
    | NodeKind.Button(spec) when ev.Event = "click" -> Some spec.OnClick
    // `OnChange` is optional (Phase 426) — a `None` (declarative / write-back)
    // select has no server-side action to dispatch; the driver no-ops it.
    | NodeKind.Select(spec) when ev.Event = "change" ->
        spec.OnChange |> Option.map (fun oc -> oc (selectValue ev.Payload))
    | NodeKind.Form(spec) when ev.Event = "submit" -> Some spec.OnSubmit
    | NodeKind.Filters(spec) ->
        // Name-addressed: the shim bridges the changed control's filter name
        // across as `payload.name` (`data-filter-name`); boundsCheck already
        // verified it names a declared filter and the value is in-bounds.
        // Choice-shaped filters mirror the renderer's clear-to-none contract:
        // the empty string (the placeholder option) resolves to `None`.
        match tryStr "name" ev.Payload with
        | None -> None
        | Some name ->
            spec.Items
            |> List.tryFind (fun f -> f.Name = name)
            |> Option.bind (fun f ->
                let chosen =
                    match selectValue ev.Payload with
                    | Some "" -> None
                    | v -> v

                // `onChange` is optional (Phase 423) — a `None` (declarative) chip has no server-side
                // action to dispatch (its write is the client-side FilterStore path), so the driver
                // no-ops it just as it does the range filter.
                match f.Kind with
                | FormFieldKind.Text(_, onChange)
                | FormFieldKind.TextArea(_, onChange, _) ->
                    onChange |> Option.map (fun oc -> oc (chosen |> Option.defaultValue ""))
                | FormFieldKind.Choice(_, _, onChange) -> onChange |> Option.map (fun oc -> oc chosen)
                | FormFieldKind.SegmentedChoice(_, _, onChange, _) -> onChange |> Option.map (fun oc -> oc chosen)
                // Phase 1113 — the combobox's handler is the choice handler
                // (`string option`), so the clear-to-none contract is the same.
                | FormFieldKind.Combobox(_, onChange, _, _) -> onChange |> Option.map (fun oc -> oc chosen)
                // Numeric / bool / range / date chip payloads are not yet
                // server-resolvable — the driver no-ops them (client store path).
                | FormFieldKind.Number _
                | FormFieldKind.RangedNumber _
                | FormFieldKind.Range _
                | FormFieldKind.Checkbox _
                | FormFieldKind.Toggle _
                | FormFieldKind.Date _
                // Phase 725 — a DateRange handler takes the (from, to) pair,
                // not the single chosen string this resolution carries.
                | FormFieldKind.DateRange _ -> None)
    | NodeKind.Disclosure(spec) ->
        // Default to "open" when no explicit bool rides along (a summary click).
        // `OnToggle` is optional (Phase 426) — a `None` disclosure has no
        // server-side action; the driver no-ops it (the write-back default is
        // a client-side store path).
        let isOpen = tryBool "open" ev.Payload |> Option.defaultValue true
        spec.OnToggle |> Option.map (fun ot -> ot isOpen)
    | NodeKind.Tabs(spec) ->
        match tryStr "value" ev.Payload, spec.OnSelectTag with
        | Some tag, Some onTag -> Some(onTag tag)
        | _ ->
            // `OnSelect` is optional (Phase 426) — same no-op posture.
            match tryNum "index" ev.Payload, spec.OnSelect with
            | Some i, Some onSelect -> Some(onSelect (int i))
            | _ -> None
    | NodeKind.Stepper(spec) ->
        // A step-header click: the shim bridges the clicked step's index
        // across as `payload.index` (`data-step-index`), mirroring tabs.
        // `OnSelect` is optional since the swap — same no-op posture as Tabs.
        match tryNum "index" ev.Payload, spec.OnSelect with
        | Some i, Some onSelect -> Some(onSelect (int i))
        | _ -> None
    | _ -> None

// ─── the gate ───────────────────────────────────────────────────────────────

/// A short, log-safe description of an `Action` for the dispatch-denied reason
/// (the action *constructor*, never payload values).
///
/// Phase 889 moved the implementation to `Fuaran.UI.Ops.ActionInvocation.describe`
/// and this delegates, so the reject sink and the user-action record cannot
/// drift into two different notions of "log-safe". The move carries ONE
/// behaviour change, deliberate: `Navigate` now prints its PATH, not the whole
/// route. A route's query string carries user data, and this text lands in a
/// host's logger through `RejectReason.describe` — so the pre-889 spelling was
/// leaking exactly what a log-safe describer exists to withhold.
let describeAction (a: Action<'Msg>) : string =
    Fuaran.UI.Ops.ActionInvocation.ActionInvocation.describe a

/// Validate one untrusted inbound event against the current tree + the host
/// dispatch policy. `canDispatch` is the injected gate — the host maps the
/// resolved `Action` to its renderer `ActionDescriptor` and consults
/// `IFuaranRuntime.CanDispatch` (so this core takes no renderer dependency).
/// Default-deny: the first failed check wins; success yields the resolved node
/// + gated action.
let validate
    (canDispatch: Action<'Msg> -> bool)
    (tree: Node<'Msg>)
    (ev: LiveEvent)
    : Result<ValidatedEvent<'Msg>, RejectReason> =
    // (a) node exists.
    match findNode (NodeId ev.NodeId) tree with
    | None -> Error(RejectReason.UnknownNode ev.NodeId)
    | Some node ->
        // (b) event legitimate for the kind.
        if not ((legitimateEvents node).Contains ev.Event) then
            Error(RejectReason.IllegitimateEvent(ev.NodeId, ev.Event, kindName node.Kind))
        else
            // (c) payload in bounds.
            match boundsCheck node ev with
            | Error r -> Error r
            | Ok() ->
                // (d) resolved action passes the host policy gate.
                match resolveAction node ev with
                | None -> Ok { Node = node; Action = None }
                | Some action ->
                    if canDispatch action then
                        Ok { Node = node; Action = Some action }
                    else
                        Error(RejectReason.DispatchDenied(ev.NodeId, describeAction action))
