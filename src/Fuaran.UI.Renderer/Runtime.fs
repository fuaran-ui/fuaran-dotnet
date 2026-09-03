module Fuaran.UI.Renderer.Runtime

// ============================================================================
//  Fuaran — Action runtime substrate (§4b Action<'Msg>, session 3b)
//
//  Session 3a wired `Action.Dispatch` + `Action.Chain` through to the caller's
//  Elmish `dispatch`. The other five `Action` kinds (Call / Notify / Navigate
//  / SetState / AiTool) need runtime infrastructure that lives outside the
//  typed-tree library — HTTP client, notification channel, router, state
//  store, AI-tool registry. Session 3b encapsulates those behind a single
//  `IFuaranRuntime` interface the caller supplies.
//
//  Why an interface vs a record of callbacks?  Interfaces give consumers a
//  clean unit of override (override one method, inherit the rest from a
//  base class); callback-records force the consumer to construct every
//  field even when defaulting four of five to no-ops.  Same pattern a host
//  platform uses for its query-bus / peer-client interfaces.
//
//  FGP 2 (standalone posture): the renderer must not pull in the
//  downstream orchestration tier's diagnostics surface (forbidden by §4l
//  down-shift portability — `Fuaran.UI.Renderer` must stay platform-agnostic).
//  The `Warn` member is the seam the platform adapter hooks to route
//  warnings through the orchestration tier's diagnostics per FGP 4 — but
//  the renderer itself stays decoupled.
//
//  `LayoutObserver` is an additional opt-in seam carrying an
//  `ILayoutObserver` instance when the host has wired one (e.g. via a
//  platform adapter's layout-observation wiring). Default `None`; the
//  renderer's recursive `render` honours it by emitting the
//  `data-fuaran-node-id` attribute that the observer's MutationObserver
//  self-discovers — no per-element ref callback is required at the
//  renderer layer.
// ============================================================================

open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.LayoutObserver
open Feliz

/// JS ↔ `JVal` structural bridges (Fable). Every seam where the renderer
/// meets a dynamic JS value — resumed action payloads, AG Grid row objects,
/// custom-renderer React props, the browser state store — crosses through
/// these two functions, so the structured payload contract holds right up to
/// the JS boundary.
module JsonBridge =
    open Fable.Core
    open Fable.Core.JsInterop

    /// Structural JS value → `JVal`. The wire model has no null, so `null` /
    /// `undefined` fields are DROPPED inside objects (absence is the
    /// encoding) and map to `JStr ""` at the root (a display-safe degenerate
    /// value). Integral JS numbers bridge to `JInt` (mirroring the decoder's
    /// number policy); everything else stays `JFloat`.
    let rec jsToJVal (o: obj) : JVal =
        if isNull o then
            JStr ""
        else
            match o with
            | :? string as s -> JStr s
            | :? bool as b -> JBool b
            | :? float as n ->
                if
                    not (System.Double.IsNaN n)
                    && not (System.Double.IsInfinity n)
                    && System.Math.Floor n = n
                    && abs n <= 2147483647.0
                then
                    JInt(int n)
                else
                    JFloat n
            | _ ->
                if JS.Constructors.Array.isArray o then
                    JArr(unbox<obj[]> o |> Array.toList |> List.map jsToJVal)
                else
                    JObj(
                        JS.Constructors.Object.keys o
                        |> Seq.toList
                        |> List.choose (fun k ->
                            let v: obj = o?(k)
                            if isNull v then None else Some(k, jsToJVal v))
                    )

    /// `JVal` → plain JS value (numbers as JS numbers, arrays as JS arrays,
    /// objects as plain JS objects) — the shape React props, the state store,
    /// and host runtime callbacks consume.
    let rec jvalToJs (j: JVal) : obj =
        match j with
        | JStr s -> box s
        | JInt i -> box i
        | JBool b -> box b
        | JFloat f -> box f
        | JArr xs -> box (xs |> List.map jvalToJs |> List.toArray)
        | JObj fields ->
            let target = obj ()

            for (k, v) in fields do
                target?(k) <- jvalToJs v

            target

/// Registry for consumer-side custom renderers. Holds a
/// dictionary keyed on `(moduleId, componentId)` whose values produce
/// `ReactElement` from the typed-tree prop bag. The renderer's `Custom`
/// arm consults `IFuaranRuntime.TryRenderCustom` first; this class is the
/// canonical implementation hosts use to populate the runtime side.
///
/// Originally the `NodeKind.Custom` arm rendered only a labelled
/// placeholder; consumers had no API to register a real renderer. Now
/// any host (platform adapter, standalone browser-runtime consumer,
/// the test runner) can construct a registry, register renderers
/// keyed on `(moduleId, componentId)`, and pass it into the runtime
/// constructor (`MutableRuntime` for tests, `BrowserRuntime` for the
/// browser).
type CustomRendererRegistry() =
    // Phase 783 — the key gains a RENDER SCOPE. It was `(moduleId, componentId)`
    // process-wide, and lookup was a raw `TryGetValue` on ids taken straight off
    // the wire, so any decoded tree could invoke any renderer registered anywhere
    // in the process, with attacker-chosen props. That is a confused deputy: a
    // renderer registered for a privileged admin surface was reachable from a
    // tree rendered on a public one, and nothing in the language could express
    // "not from there".
    //
    // `None` is the ROOT scope — what a plain `render` / `renderWithSources` runs
    // under, and where the unscoped `Register` overloads land, so existing hosts
    // are unaffected. A host separates surfaces by rendering them under distinct
    // scopes (`renderWithSourcesInScope`) and registering each surface's
    // renderers with `RegisterInScope`; a mounted guest already renders under its
    // own scope, so a guest reaches only what was registered for it.
    let renderers =
        System.Collections.Generic.Dictionary<
            string option * string * string,
            (Map<string, JVal> -> ReactElement) * ContentHash option
         >()

    /// Register (or replace) a renderer keyed on the module + component id, in
    /// the ROOT render scope (`None`) — the scope a plain `render` runs under.
    /// Subsequent renders of `NodeKind.Custom(moduleId, componentId, props,
    /// ...)` invoke `renderFn props` and use the returned element.
    /// Optional `contentHash` carries the registered
    /// renderer's source hash so the renderer's pre-dispatch verification
    /// can compare it against any hash the tree declares. Callers that
    /// omit the hash argument continue working — hash verification is
    /// opt-in on both sides.
    member _.Register
        (moduleId: string, componentId: string, renderFn: Map<string, JVal> -> ReactElement, ?contentHash: ContentHash)
        : unit =
        renderers[(None, moduleId, componentId)] <- (renderFn, contentHash)

    /// Register (or replace) a renderer reachable ONLY from trees rendered under
    /// `scope` (Phase 783). A tree rendered under a different scope — or under the
    /// root scope — cannot address it, and gets the unregistered placeholder.
    member _.RegisterInScope
        (
            scope: string,
            moduleId: string,
            componentId: string,
            renderFn: Map<string, JVal> -> ReactElement,
            ?contentHash: ContentHash
        ) : unit =
        renderers[(Some scope, moduleId, componentId)] <- (renderFn, contentHash)

    /// Register a typed Custom **contract** (Phase 164) — one call wires the
    /// decode + render + the contract's derived content hash. A decode failure
    /// renders a labelled placeholder naming the failing key + emits a
    /// diagnostic (12.D, both pipelines via `eprintfn`), never a blank box. The
    /// four-way agreement (encode / client decode / server decode / hash)
    /// becomes structural — one contract value drives all of them.
    member this.RegisterContract(contract: Fuaran.UI.CustomContract<'Props>, render: 'Props -> ReactElement) : unit =
        let wrapped (props: Map<string, JVal>) : ReactElement =
            match contract.Decode props with
            | Ok p -> render p
            | Error e ->
                eprintfn
                    "[Fuaran] Custom decode failed for %s.%s — key '%s': %s"
                    contract.ModuleId
                    contract.ComponentId
                    e.Key
                    e.Message

                Html.div
                    [ prop.className (
                          sprintf
                              "fuaran-kind-custom-placeholder fuaran-custom-decode-error fuaran-custom-%s-%s"
                              contract.ModuleId
                              contract.ComponentId
                      )
                      prop.custom ("data-fuaran-custom-decode-error", e.Key)
                      prop.text (
                          sprintf
                              "[fuaran:custom %s.%s — decode error (key '%s'): %s]"
                              contract.ModuleId
                              contract.ComponentId
                              e.Key
                              e.Message
                      ) ]

        this.Register(contract.ModuleId, contract.ComponentId, wrapped, contract.Hash)

    /// Look up a ROOT-scope renderer; returns `Some element` when registered,
    /// `None` when the renderer is not registered (the renderer's Custom arm then
    /// falls back to the labelled placeholder).
    member this.TryRender(moduleId: string, componentId: string, props: Map<string, JVal>) : ReactElement option =
        this.TryRenderInScope(None, moduleId, componentId, props)

    /// Scope-constrained lookup (Phase 783). `scope` is the render scope the
    /// current tree is running under (`RenderContext.Scope`); `None` is the root
    /// scope. There is deliberately NO fallback to another scope — falling back
    /// would make the scoping advisory, which is the same as not having it.
    member _.TryRenderInScope
        (scope: string option, moduleId: string, componentId: string, props: Map<string, JVal>)
        : ReactElement option =
        match renderers.TryGetValue((scope, moduleId, componentId)) with
        | true, (fn, _) -> Some(fn props)
        | false, _ -> None

    /// Parallel accessor exposing the registered
    /// renderer's optional `ContentHash` alongside the render function.
    /// The renderer's Custom arm uses this to perform hash verification
    /// before dispatch; `TryRender` is kept stable so hosts
    /// implementing `IFuaranRuntime` directly don't need updating.
    member this.TryGet
        (moduleId: string, componentId: string)
        : ((Map<string, JVal> -> ReactElement) * ContentHash option) option =
        this.TryGetInScope(None, moduleId, componentId)

    /// Scope-constrained twin of `TryGet` (Phase 783). No cross-scope fallback.
    member _.TryGetInScope
        (scope: string option, moduleId: string, componentId: string)
        : ((Map<string, JVal> -> ReactElement) * ContentHash option) option =
        match renderers.TryGetValue((scope, moduleId, componentId)) with
        | true, pair -> Some pair
        | false, _ -> None

    /// True when at least one renderer is registered.
    member _.Count: int = renderers.Count

/// Describes a host-effecting action presented to the dispatch policy gate
/// (`IFuaranRuntime.CanDispatch`, Phase 119) BEFORE the renderer invokes the
/// corresponding runtime effect. Closed + structural so a gate can switch on
/// it without a `'Msg`-aware codec.
///
/// Phase 782 closed the set: it now covers **every wire-survivable action that
/// reaches a host substrate**. Until then it covered only `Call` / `Navigate` /
/// `AiTool` / `ReadFileBody` / `ApplyTreeOp`, and `Notify` / `SetState` /
/// `WriteToClipboard` / `CommitLocal` called their substrates with no gate
/// consultation at all — so a host with a perfect deny-all policy still could
/// not refuse a decoded tree's `SetState`, which writes the process-global
/// `StateStore` (persisted, on the browser path, in a key namespace shared with
/// host-owned keys). "Route through their own substrates" was never a reason
/// they were unreachable; it was only a reason nobody had wired them up.
[<RequireQualifiedAccess>]
type ActionDescriptor =
    | Call of endpoint: string
    | Navigate of route: string
    | AiTool of toolName: string
    /// A `ReadFileBody` of the selected file identified by `fileId`
    /// (Phase 136). Reading a user-selected file's bytes is a host-effecting
    /// action, so it joins the gated set — a default-deny host (BYOK
    /// playground, etc.) can refuse file reads through the same seam it
    /// refuses `Call` / `Navigate` / `AiTool`.
    | ReadFileBody of fileId: string
    /// An in-page `apply(treeOpJson)` from the debug-introspection REPL
    /// (`window.__fuaran`, Phase 90). A live console mutation is a
    /// tree-mutating effect, so it joins the gated set — the same default-deny
    /// contract an AI-tool dispatch obeys (FGP 3). A host that denies mutation
    /// (BYOK playground, read-only embed) refuses in-page applies through the
    /// same `CanDispatch` seam it refuses `Call` / `Navigate` / `AiTool`. The
    /// `summary` is the raw op JSON (or a truncation) — the renderer does not
    /// decode the op (the apply engine lives outside the renderer's Fable
    /// graph), so the gate decides on the opaque payload, and the host's
    /// apply handler decodes only after the gate allows.
    | ApplyTreeOp of summary: string
    /// A `Notify` publication on a named channel (Phase 782). The channel name
    /// is host-addressable — a decoded tree naming a channel the host listens on
    /// is injecting into the host's own message plane, so it is gated like any
    /// other outbound effect.
    | Notify of channel: string
    /// A `SetState` write of `key` (Phase 782). The gate sees the key, so a host
    /// policy can be per-key rather than all-or-nothing. Note the key namespace
    /// is ALSO closed structurally — `StateStore.HostReservedPrefix` keys are
    /// unaddressable from a tree-originated write regardless of gate policy.
    | SetState of key: string
    /// A clipboard write (Phase 782). The text is deliberately NOT carried: a
    /// clipboard payload is user data, and a gate that logs its descriptor would
    /// then log it. The decision is "may this tree write the clipboard at all".
    | WriteToClipboard
    /// A `CommitLocal` flush for `nodeId` (Phase 782). It dispatches a DOM
    /// CustomEvent, which is a host-observable effect even though no
    /// `IFuaranRuntime` member backs it.
    | CommitLocal of nodeId: string
    /// A print-dialogue request (Phase 1124). Payload-free, like
    /// `WriteToClipboard` and for a stronger reason — the clipboard descriptor
    /// withholds its text deliberately, where this one has no text to withhold.
    /// The decision is the whole of it: "may this tree open the reader's print
    /// dialogue at all". It is in the gated set because an unbidden modal that
    /// steals focus, and on some platforms starts a physical act, is
    /// host-observable however little it discloses — the `CommitLocal`
    /// reasoning, which is likewise backed by no `IFuaranRuntime` member.
    | Print

[<RequireQualifiedAccess>]
module ActionDescriptor =
    /// Human-readable label for a denied-dispatch diagnostic.
    let describe (descriptor: ActionDescriptor) : string =
        match descriptor with
        | ActionDescriptor.Call endpoint -> sprintf "Call(%s)" endpoint
        // PATH only. This label reaches a `Warn` line AND — through the
        // devtools apply gate — `DenyTelemetry.Reason`, which a host may
        // persist. A route's query string is user data, so the same scrubber
        // the durable action record uses applies here rather than a second
        // spelling of the same intent.
        | ActionDescriptor.Navigate route ->
            sprintf "Navigate(%s)" (Fuaran.UI.Ops.ActionInvocation.ActionInvocation.routePath route)
        | ActionDescriptor.AiTool toolName -> sprintf "AiTool(%s)" toolName
        | ActionDescriptor.ReadFileBody fileId -> sprintf "ReadFileBody(%s)" fileId
        | ActionDescriptor.ApplyTreeOp summary -> sprintf "ApplyTreeOp(%s)" summary
        | ActionDescriptor.Notify channel -> sprintf "Notify(%s)" channel
        | ActionDescriptor.SetState key -> sprintf "SetState(%s)" key
        | ActionDescriptor.WriteToClipboard -> "WriteToClipboard"
        | ActionDescriptor.CommitLocal nodeId -> sprintf "CommitLocal(%s)" nodeId
        | ActionDescriptor.Print -> "Print"

/// The five Action substrates the renderer dispatches to. Consumers
/// implement once at the app shell; the renderer treats it as a black box.
/// `Call`'s callback is `obj -> unit` (not `obj -> 'Msg`) because the
/// renderer pre-wraps the typed `Action.Call(_, onResult: obj -> 'Msg)`
/// closure as `fun raw -> dispatch (onResult raw)` before handing it
/// over — the runtime stays generic over 'Msg.
type IFuaranRuntime =
    /// HTTP / Fable.Remoting call to `endpoint`. Implementation fetches,
    /// decodes, then invokes `onResult` with the decoded payload (as obj —
    /// the captured renderer closure unboxes back to typed `'a` per
    /// `Defect (2)` resolution).
    abstract Call: endpoint: ApiEndpoint * onResult: (obj -> unit) -> unit

    /// Publish to a named notification channel. Consumer apps wire this
    /// to `INotificationChannel.Publish`; standalone hosts route to
    /// `window.postMessage` or similar.
    abstract Notify: channel: string * payload: JVal -> unit

    /// Route navigation. Browser hosts wire to `window.location.hash` or
    /// the consumer's SPA router.
    abstract Navigate: route: string -> unit

    /// Write a Binding.State cell. Consumers wire this to whatever store
    /// the resolver's `BindingSources.State` reads from.
    abstract SetState: key: string * value: JVal -> unit

    /// Invoke a registered AI tool by name. Consumer apps wire this to
    /// the downstream orchestration tier's tool registry; standalone hosts log.
    abstract InvokeAiTool: toolName: string * args: JVal -> unit

    /// Write a literal string to the clipboard. The default browser
    /// impl calls `navigator.clipboard.writeText`; the diagnostic runtime
    /// emits a warning. Hosts that need to override (electron `clipboard` IPC,
    /// server-side log capture, test mocks) implement this member without
    /// touching call sites.
    abstract WriteToClipboard: text: string -> unit

    /// Read the body of a user-selected file (Phase 136). `file` is the
    /// opaque `FileRef` the renderer assigned when the `<input type=file>`
    /// change event fired (its `Handle` carries the boxed browser `File` on
    /// browser hosts); `encoding` picks the byte→string projection; `onRead`
    /// receives the read body. The default browser impl reads via
    /// `FileReader` and invokes `onRead` from the load callback (the read is
    /// async at the host level, but the typed dispatch surface stays
    /// callback-shaped — same posture as `Call`). The diagnostic runtime
    /// emits a warning and never calls back. Hosts that need to override
    /// (electron file IPC, server-side capture, test mocks) implement this
    /// member without touching call sites.
    ///
    /// Per established precedent, `IFuaranRuntime` gaining a new abstract
    /// member is a pre-1.0 minor add — direct implementers add the member
    /// alongside their existing members. See `STABILITY.md`.
    abstract ReadFileBody: file: FileRef * encoding: FileReadEncoding * onRead: (string -> unit) -> unit

    /// Diagnostic channel for renderer-emitted warnings (e.g. unwired-action
    /// fallbacks before a runtime is supplied, binding-resolution failures
    /// the caller's `OnError` slot did not catch). A platform adapter
    /// hooks this to the orchestration tier's diagnostics surface; standalone
    /// hosts route to `console.warn`.
    abstract Warn: message: string -> unit

    /// Opt-in layout observer. `None` by default; hosts wire
    /// one via a platform adapter's layout-observation wiring (or the
    /// equivalent direct install for standalone hosts). When present, the
    /// renderer's emitted `data-fuaran-node-id` attribute lets the observer
    /// (via its own MutationObserver) self-discover addressable elements
    /// and bind ResizeObserver to each — no per-element ref callback is
    /// required at the renderer layer.
    abstract LayoutObserver: ILayoutObserver option

    /// Optional render hook for `NodeKind.Custom`. The renderer
    /// calls this first before falling back to a labelled placeholder. The
    /// diagnostic runtime returns `None`; hosts that need custom renderers
    /// (consumer apps, the catalog demo, the test suite) consult their
    /// own registry. Implementations are responsible for whatever
    /// safety / sandboxing they want — the renderer trusts the returned
    /// `ReactElement` verbatim.
    abstract TryRenderCustom: moduleId: string * componentId: string * props: Map<string, JVal> -> ReactElement option

    /// Parallel accessor exposing the registered
    /// renderer fn AND its optional `ContentHash` so the renderer can
    /// perform hash verification BEFORE dispatch. Returns `None` when no
    /// renderer is registered for `(moduleId, componentId)`; returns
    /// `Some (fn, None)` for renderers registered without a hash
    /// (no-hash shape); returns `Some (fn, Some hash)` for hash-aware
    /// registrations.
    ///
    /// Hosts that don't participate in bounded-escape verification can
    /// return `None` here and continue to serve renderers via
    /// `TryRenderCustom`; the renderer falls through transparently.
    /// Per established precedent, IFuaranRuntime gaining a new
    /// abstract member is a pre-1.0 minor add — direct implementers
    /// add the member alongside their `TryRenderCustom`.
    abstract TryGetCustomRenderer:
        moduleId: string * componentId: string -> ((Map<string, JVal> -> ReactElement) * ContentHash option) option

    /// **Scope-constrained custom-renderer lookup (Phase 783) — the members the
    /// renderer's `Custom` arm actually calls.**
    ///
    /// `scope` is the render scope the current tree is running under
    /// (`RenderContext.Scope`): `None` for a plain `render`, `Some scopeId`
    /// inside a mounted guest or under `renderWithSourcesInScope`. An
    /// implementation MUST NOT fall back to another scope — a fallback makes the
    /// scoping advisory, which is indistinguishable from not having it.
    ///
    /// **Why these exist rather than a parameter change.** The registry was one
    /// process-wide dictionary keyed on `(moduleId, componentId)` taken straight
    /// off the wire, so any decoded tree could invoke any renderer registered
    /// anywhere in the process: a renderer registered for a privileged admin
    /// surface was reachable from a tree rendered on a public one. Adding members
    /// rather than retyping the existing ones follows this interface's own
    /// pre-1.0 minor-add precedent — but note the FAIL-CLOSED consequence, which
    /// is deliberate: a host that implements `IFuaranRuntime` by hand and returns
    /// `None` here loses custom rendering until it implements them. Unregistered
    /// must mean unreachable, and "unimplemented" is a kind of unregistered.
    abstract TryRenderCustomInScope:
        scope: string option * moduleId: string * componentId: string * props: Map<string, JVal> -> ReactElement option

    /// Scope-constrained twin of `TryGetCustomRenderer` (Phase 783). Same
    /// no-fallback contract as `TryRenderCustomInScope`.
    abstract TryGetCustomRendererInScope:
        scope: string option * moduleId: string * componentId: string ->
            ((Map<string, JVal> -> ReactElement) * ContentHash option) option

    /// Deny-by-default seam at the renderer dispatch boundary (Phase 119,
    /// inverted by Phase 782). `runAction` consults this BEFORE invoking the
    /// host effect for EVERY wire-survivable action. Return `false` to deny —
    /// the renderer emits a diagnostic via `Warn` and skips the host effect.
    ///
    /// **The shipped runtimes DENY by default** (Phase 782). Before that they
    /// all returned `true`, which made the gate an opt-in seam a host had to
    /// remember to override — the inverse of the posture the language claims,
    /// and a claim is not worth much when the shipped default contradicts it.
    /// A host that genuinely wants the permissive posture asks for it BY NAME:
    /// `Runtime.permissive` / `PermissiveRuntime` / `MutableRuntime.Permissive()`
    /// / `BrowserRuntime.createPermissive()` / `DriverServices.createPermissive`.
    /// One grep for `permissive` finds every place the old behaviour is back.
    ///
    /// Per established precedent, `IFuaranRuntime` gaining a new abstract
    /// member is a pre-1.0 minor add — direct implementers add the member
    /// alongside their existing members. A direct implementer that returns
    /// `true` unconditionally has, since Phase 782, written a permissive host;
    /// that is allowed, and it is now visible in its own source rather than
    /// inherited silently. See `STABILITY.md`.
    abstract CanDispatch: action: ActionDescriptor -> bool

    /// Guest-loader seam for `NodeKind.Mount` (Phase 266, §4o). The renderer's
    /// `Mount` arm calls this with the mount's guest scope id; a host that wants
    /// live guest composition returns the guest's `Node<obj>` tree (the
    /// orchestration tier plugs its `IFuaranGuestLoader` here). The diagnostic /
    /// standalone runtimes return `None`, so the `Mount` arm falls back to the
    /// declared empty state (the Phase 265 placeholder) — a `Mount` in a tree
    /// rendered by an unwired host is inert, never a throw. When `Some guest`,
    /// the renderer renders it under the guest scope (`StateStore.forScope`) with
    /// dispatch bridged through the mount's `OnBubble` channel.
    ///
    /// Per established precedent, `IFuaranRuntime` gaining a new abstract member
    /// is a pre-1.0 minor add — direct implementers add `member _.TryLoadGuest _
    /// = None` alongside their existing members. See `STABILITY.md`.
    abstract TryLoadGuest: scopeId: string -> Node<obj> option


/// Default runtime — every method logs an `eprintfn` and otherwise no-ops.
/// Suitable for the test runner (.NET-side, no browser substrate) and for
/// the bootstrap pre-runtime-wiring phase of any consumer. Same diagnostic
/// shape session 3a's `runAction` emitted, just promoted into the runtime
/// seam.
type DiagnosticRuntime() =
    interface IFuaranRuntime with
        member _.Call(ApiEndpoint endpoint, _) =
            eprintfn
                "[Fuaran] Action.Call(%s) reached the runtime with no HTTP substrate wired (consumer must supply an IFuaranRuntime)."
                endpoint

        member _.Notify(channel, _) =
            eprintfn "[Fuaran] Action.Notify(%s) reached the runtime with no notification substrate wired." channel

        member _.Navigate(route) =
            // Path only: this is the DEFAULT runtime an unconfigured host gets,
            // so its stderr is the likeliest place a query string is captured
            // by something that outlives the session.
            eprintfn
                "[Fuaran] Action.Navigate(%s) reached the runtime with no router substrate wired."
                (Fuaran.UI.Ops.ActionInvocation.ActionInvocation.routePath route)

        member _.SetState(key, _) =
            eprintfn "[Fuaran] Action.SetState(%s) reached the runtime with no state-store substrate wired." key

        member _.InvokeAiTool(toolName, _) =
            eprintfn "[Fuaran] Action.AiTool(%s) reached the runtime with no AI-tool registry wired." toolName

        member _.WriteToClipboard(_) =
            eprintfn
                "[Fuaran] Action.WriteToClipboard reached the runtime with no clipboard substrate wired (browser hosts use BrowserRuntime; non-browser hosts override this member)."

        member _.ReadFileBody(file, _, _) =
            eprintfn
                "[Fuaran] Action.ReadFileBody(%s) reached the runtime with no file-read substrate wired (browser hosts use BrowserRuntime; non-browser hosts override this member). onRead will not fire."
                file.Id

        member _.Warn(message) = eprintfn "[Fuaran] %s" message

        member _.LayoutObserver = None

        member _.TryRenderCustom(_, _, _) = None

        // The diagnostic runtime has no registry, so it never
        // has a hash to expose. Hosts that want bounded-escape participation
        // use `MutableRuntime` / `BrowserRuntime` (both override this).
        member _.TryGetCustomRenderer(_, _) = None

        // No registry, so no scope has anything registered either.
        member _.TryRenderCustomInScope(_, _, _, _) = None
        member _.TryGetCustomRendererInScope(_, _, _) = None

        // DENY-by-default (Phase 782). The previous "the renderer is not the
        // trust boundary — the host gate is" rationale was self-defeating: this
        // IS the runtime an unconfigured host gets, so "the host gate" was, for
        // an unconfigured host, exactly this line returning `true`. A security
        // default has to fail closed; the opt-out is `Runtime.permissive`.
        member _.CanDispatch(_) = false

        // No guest loader in the diagnostic runtime — a Mount renders its
        // declared empty state (Phase 266). Hosts that compose guests plug an
        // orchestration-tier IFuaranGuestLoader through their own runtime.
        member _.TryLoadGuest(_) = None

/// Shared instance of the diagnostic runtime — the renderer falls back to
/// this when the caller does not supply one.
let diagnostic: IFuaranRuntime = DiagnosticRuntime() :> IFuaranRuntime

/// **The named opt-in back to the pre-0.14.0 allow-everything dispatch posture**
/// (Phase 782). Identical to [[DiagnosticRuntime]] in every respect except that
/// `CanDispatch` allows every descriptor.
///
/// This type exists so that re-enabling the permissive posture is a deliberate,
/// greppable act. A host that upgrades and finds its actions refused has exactly
/// two honest choices: implement a real allow-list, or name this type. What it
/// cannot do any more is inherit permissiveness without saying so — which is
/// what every host did before Phase 782, mostly without knowing.
///
/// Prefer a real policy. This is the migration ramp, not the destination.
type PermissiveRuntime() =
    interface IFuaranRuntime with
        member _.Call(endpoint, onResult) = diagnostic.Call(endpoint, onResult)
        member _.Notify(channel, payload) = diagnostic.Notify(channel, payload)
        member _.Navigate(route) = diagnostic.Navigate(route)
        member _.SetState(key, value) = diagnostic.SetState(key, value)
        member _.InvokeAiTool(toolName, args) = diagnostic.InvokeAiTool(toolName, args)
        member _.WriteToClipboard(text) = diagnostic.WriteToClipboard(text)

        member _.ReadFileBody(file, encoding, onRead) =
            diagnostic.ReadFileBody(file, encoding, onRead)

        member _.Warn(message) = diagnostic.Warn(message)
        member _.LayoutObserver = diagnostic.LayoutObserver
        member _.TryRenderCustom(_, _, _) = None
        member _.TryGetCustomRenderer(_, _) = None
        member _.TryRenderCustomInScope(_, _, _, _) = None
        member _.TryGetCustomRendererInScope(_, _, _) = None
        member _.CanDispatch(_) = true
        member _.TryLoadGuest(_) = None

/// Shared instance of the permissive runtime — the one-line migration for a
/// host that relied on the pre-0.14.0 allow-everything default.
let permissive: IFuaranRuntime = PermissiveRuntime() :> IFuaranRuntime

/// The runtime a `NodeKind.Mount` guest receives when the host has installed NO
/// `GuestSeam` (Phase 783). Refuses every capability and records each refusal
/// through the host's own `Warn` channel, so an unwired mount is observable
/// rather than mysteriously inert.
///
/// **Why this type exists.** Until Phase 783 the `Mount` arm's no-seam branch
/// handed the guest `ctx.Runtime` — the HOST's runtime — unwrapped. A guest is by
/// definition foreign content, and `MountSpec.Capabilities` is documented as "a
/// request, not a grant", so the default grant was the complete opposite of the
/// declared posture: a host that had installed nothing gave a mounted guest
/// everything the host itself could do. Unregistered must mean unprivileged.
///
/// A host that wants a mounted guest to reach anything installs a `GuestSeam`
/// and returns a runtime of its choosing from `WrapRuntime` — which is exactly
/// the deliberate, visible act the seam was built for.
type UnprivilegedGuestRuntime(host: IFuaranRuntime, scopeId: string) =
    let refuse (what: string) =
        host.Warn(
            sprintf
                "[Fuaran] mount guest '%s' attempted %s with no GuestSeam installed — refused. Install a GuestSeam (Render.installGuestSeam) to grant a mounted guest any capability."
                scopeId
                what
        )

    interface IFuaranRuntime with
        member _.Call(ApiEndpoint endpoint, _) = refuse (sprintf "Call(%s)" endpoint)
        member _.Notify(channel, _) = refuse (sprintf "Notify(%s)" channel)

        member _.Navigate(route) =
            refuse (sprintf "Navigate(%s)" (Fuaran.UI.Ops.ActionInvocation.ActionInvocation.routePath route))

        member _.SetState(key, _) = refuse (sprintf "SetState(%s)" key)
        member _.InvokeAiTool(toolName, _) = refuse (sprintf "AiTool(%s)" toolName)
        member _.WriteToClipboard(_) = refuse "WriteToClipboard"

        member _.ReadFileBody(file, _, _) =
            refuse (sprintf "ReadFileBody(%s)" file.Id)

        // Diagnostics still reach the host — refusing to say anything would make
        // an unwired mount silently inert, which is how this defect survived.
        member _.Warn(message) = host.Warn message

        // Observing the host's layout is itself a capability.
        member _.LayoutObserver = None

        // No custom renderers, in any scope: reaching one is exactly the
        // confused-deputy shape the scoping closes.
        member _.TryRenderCustom(_, _, _) = None
        member _.TryGetCustomRenderer(_, _) = None
        member _.TryRenderCustomInScope(_, _, _, _) = None
        member _.TryGetCustomRendererInScope(_, _, _) = None

        member _.CanDispatch(_) = false

        // No nested guest loading — a guest cannot mount its own guests to climb
        // back out through a differently-wired branch.
        member _.TryLoadGuest(_) = None

/// Diagnostic-shaped runtime that ALSO holds a
/// `CustomRendererRegistry` so callers can register `NodeKind.Custom`
/// renderers without standing up the full `BrowserRuntime`. Intended
/// surface: the test suite (.NET-side, no `Browser.*` types) and any
/// .NET-side host that wants Custom dispatch without the browser
/// substrate. Browser hosts use [[BrowserRuntime]] directly — same
/// registration surface, browser-shaped action substrates.
///
/// All non-Custom members delegate to [[diagnostic]] verbatim so the
/// substrate-warning shape is unchanged.
type MutableRuntime(allowAll: bool) =
    let registry = CustomRendererRegistry()

    /// Default construction is DENY-by-default (Phase 782), matching every
    /// other shipped runtime.
    new() = MutableRuntime(false)

    /// The named permissive opt-in (Phase 782) — a `MutableRuntime` whose
    /// `CanDispatch` allows every descriptor. Same rationale as
    /// [[PermissiveRuntime]]: the old behaviour stays available, but only to a
    /// caller who asks for it by name.
    static member Permissive() = MutableRuntime(true)

    /// Register a renderer for `NodeKind.Custom(moduleId, componentId, props,
    /// ...)`. The renderer's Custom arm calls the registered function with
    /// the node's prop bag and uses the returned element verbatim.
    /// the optional `contentHash` argument is the registered renderer's
    /// source hash; the renderer's pre-dispatch verification compares it
    /// against any hash the tree declares.
    member _.RegisterCustomRenderer
        (moduleId: string, componentId: string, renderFn: Map<string, JVal> -> ReactElement, ?contentHash: ContentHash)
        : unit =
        match contentHash with
        | Some h -> registry.Register(moduleId, componentId, renderFn, h)
        | None -> registry.Register(moduleId, componentId, renderFn)

    /// Direct registry access — exposed so tests can assert registration
    /// state without going through `TryRenderCustom`.
    member _.Registry: CustomRendererRegistry = registry

    interface IFuaranRuntime with
        member _.Call(endpoint, onResult) = diagnostic.Call(endpoint, onResult)
        member _.Notify(channel, payload) = diagnostic.Notify(channel, payload)
        member _.Navigate(route) = diagnostic.Navigate(route)
        member _.SetState(key, value) = diagnostic.SetState(key, value)
        member _.InvokeAiTool(toolName, args) = diagnostic.InvokeAiTool(toolName, args)
        member _.WriteToClipboard(text) = diagnostic.WriteToClipboard(text)

        member _.ReadFileBody(file, encoding, onRead) =
            diagnostic.ReadFileBody(file, encoding, onRead)

        member _.Warn(message) = diagnostic.Warn(message)
        member _.LayoutObserver = diagnostic.LayoutObserver

        member _.TryRenderCustom(moduleId, componentId, props) =
            registry.TryRender(moduleId, componentId, props)

        member _.TryGetCustomRenderer(moduleId, componentId) = registry.TryGet(moduleId, componentId)

        member _.TryRenderCustomInScope(scope, moduleId, componentId, props) =
            registry.TryRenderInScope(scope, moduleId, componentId, props)

        member _.TryGetCustomRendererInScope(scope, moduleId, componentId) =
            registry.TryGetInScope(scope, moduleId, componentId)

        member _.CanDispatch(action) =
            if allowAll then
                permissive.CanDispatch(action)
            else
                diagnostic.CanDispatch(action)

        member _.TryLoadGuest(scopeId) = diagnostic.TryLoadGuest(scopeId)
