module Fuaran.ApplyDemo.Main

// ============================================================================
//  Fuaran apply-demo — the Phase 90 `window.__fuaran.apply(op)` browser host.
//
//  This is the minimal host that proves the Phase 191 outcome: a Fable browser
//  app references the (now Fable-clean) `Fuaran.UI.Ops` decode + apply engine,
//  owns a `Node<obj>` tree, and runs the full
//      wire JSON → decodeOp → policy gate → Apply.apply → setState → re-render
//  loop entirely client-side, with no server.
//
//  Why `Node<obj>` (not the domain-shaped `Node<'Msg>` of samples/demo): the
//  wire decoder yields `TreeOp<obj>` and `Apply.apply : TreeOp<'Msg> ->
//  Node<'Msg> -> …`, so the host model must hold a `Node<obj>` for the op to
//  apply without a type clash. (The samples/demo model is domain-shaped,
//  so it wires `apply = None` — the `unwired` envelope.) Mirrors the TS demo's
//  `applied`-state + `onApply` pattern.
//
//  Verify in DevTools (or via the Phase 191 browser session):
//    > __fuaran.getNodeState("headline-metric")
//    > __fuaran.apply('{"$type":"UpdateProp","path":"Label","target":"headline-metric","value":{"$type":"Literal","text":"Mutated!"}}')
//  The metric's label re-renders from "Original Label" to "Mutated!".
// ============================================================================

open Fable.Core.JsInterop
open Fuaran.MVU
open Fuaran.MVU.React
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Ops

importSideEffects "./index.css"

// ─── Debug-global opt-in (Phase 1532) ──────────────────────────────────────
//
// The console global / relay opt-in this host passes at `register`, taken from
// this project's own build rather than hardcoded: `FUARAN_DEBUG_GLOBAL` is
// defined by the `.fsproj` outside a Release configuration. The renderer gates
// on the SAME symbol (or `DebugGlobal.enableDebugGlobal ()`) in ITS OWN
// compilation — symbols do not cross project boundaries, so a Fable build that
// wants the surface passes `--define FUARAN_DEBUG_GLOBAL` as well.
let private debugOptIn =
#if FUARAN_DEBUG_GLOBAL
    true
#else
    false
#endif

// ─── Authored Node<obj> base tree ───────────────────────────────────────────
//
// A dashboard with one Metric: a `TextSource.Literal` label (the UpdateProp
// target the acceptance test mutates) + a `Binding.Static` float source (the
// Binding-typed field). Both exercise the coercion path Phase 191 made
// Fable-correct.

let private baseTree: Node<obj> =
    Fuaran.dashboard
        "apply-root"
        { Defaults.dashboard with
            Children =
                [ Fuaran.metric
                      "headline-metric"
                      { Defaults.metric with
                          Label = TextSource.Literal "Original Label"
                          Value = Binding.Static(Some 42.0)
                          Format = format.number (Some 0)
                          Tone = ToneVariant.Brand } ] }

// ─── Model + Msg ─────────────────────────────────────────────────────────────

type Model = { Tree: Node<obj> }

type Msg = ReplaceTree of Node<obj>

let init () : Model * Step<Msg, NoEffect> list = { Tree = baseTree }, []

let update (msg: Msg) (model: Model) : Model * Step<Msg, NoEffect> list =
    match msg with
    | ReplaceTree tree -> { Tree = tree }, []

// ─── Binding sources + runtime ───────────────────────────────────────────────
//
// The Static-bound metric source needs no live query/state sources; an empty
// `BindingSources` suffices.

let private sources: BindingResolver.BindingSources = BindingResolver.empty

/// This sample's dispatch policy. The browser runtime is DENY-by-default
/// (Phase 782) and stays so; this permits exactly `ApplyTreeOp`, so the in-page
/// `__fuaran.apply` loop this sample exists to show passes the gate and nothing
/// else gains a permit. A host applying ops from an untrusted source writes its
/// own, narrower allow-list the same way.
type private ApplyPermittingRuntime(inner: Runtime.IFuaranRuntime) =
    interface Runtime.IFuaranRuntime with
        member _.Call(endpoint, onResult) = inner.Call(endpoint, onResult)
        member _.Notify(channel, payload) = inner.Notify(channel, payload)
        member _.Navigate(route) = inner.Navigate(route)
        member _.SetState(key, value) = inner.SetState(key, value)
        member _.InvokeAiTool(toolName, args) = inner.InvokeAiTool(toolName, args)
        member _.WriteToClipboard(text) = inner.WriteToClipboard(text)

        member _.ReadFileBody(file, encoding, onRead) =
            inner.ReadFileBody(file, encoding, onRead)

        member _.Warn(message) = inner.Warn(message)
        member _.LayoutObserver = inner.LayoutObserver
        member _.TryRenderCustom(m, c, p) = inner.TryRenderCustom(m, c, p)
        member _.TryGetCustomRenderer(m, c) = inner.TryGetCustomRenderer(m, c)

        member _.TryRenderCustomInScope(s, m, c, p) =
            inner.TryRenderCustomInScope(s, m, c, p)

        member _.TryGetCustomRendererInScope(s, m, c) =
            inner.TryGetCustomRendererInScope(s, m, c)

        member _.TryLoadGuest(guest) = inner.TryLoadGuest(guest)

        member _.CanDispatch(descriptor) =
            match descriptor with
            | Runtime.ActionDescriptor.ApplyTreeOp _ -> true
            | other -> inner.CanDispatch(other)

let private runtime: Runtime.IFuaranRuntime =
    ApplyPermittingRuntime(BrowserRuntime.create ()) :> Runtime.IFuaranRuntime

// ─── View ─────────────────────────────────────────────────────────────────────

let view (model: Model) (dispatch: Msg -> unit) =
    let ctx: Render.RenderContext<obj> =
        let baseCtx = Render.RenderContext.create sources ignore model.Tree

        { baseCtx with
            Runtime = runtime
            // A HAND-AUTHORED tree, where the author is the trust boundary, so the
            // permissive egress posture is correct and is reached BY NAME. A host
            // rendering a DECODED tree must not copy this line.
            Policy =
                { baseCtx.Policy with
                    Egress = Sanitize.permissiveEgress } }

    // The host-supplied apply handler: decode the wire TreeOp, apply it to the
    // live Node<obj> tree, and dispatch ReplaceTree to re-render. Phase 90's
    // `applyEnvelope` consults `runtime.CanDispatch(ApplyTreeOp …)` BEFORE this
    // handler ever runs (default-deny by shape, FGP 3; this sample's runtime
    // admits `ApplyTreeOp` by name, above). Built per-render so it
    // closes over the current tree (mirrors the TS demo's `onApply`).
    let applyHandler (opJson: string) : DebugGlobal.ApplyOutcome =
        match JsonDecode.decodeOp opJson with
        // Phase 739 — hand the decoder's STRUCTURED diagnostic back, not just its
        // prose. The relay's DECODE_FAILED refusal carries the wire format's own
        // `DecodeError` verbatim, so a client reports the failure typed.
        | Error e ->
            DebugGlobal.ApplyOutcome.DecodeFailedWith
                { Code = e.Code
                  Path = e.Path
                  Message = e.Message
                  ExpectedShape = e.ExpectedShape }
        | Ok op ->
            match Apply.apply op model.Tree with
            | Ok newTree ->
                dispatch (ReplaceTree newTree)
                // Hand the POST-APPLY tree back so the renderer commits it to the
                // change hub itself. That is what makes `apply.ok`'s revision and
                // the `changed` event's revision the same token, which the relay
                // contract says they are by construction.
                DebugGlobal.ApplyOutcome.AppliedWithTree(box newTree)
            | Error err -> DebugGlobal.ApplyOutcome.RejectedWith(err.Message, string err.Code)

    // Phase 90 — register the in-page REPL over the live Node<obj> tree WITH a
    // real apply handler (the samples/demo host passes None). `debugOptIn` is
    // this project's own `FUARAN_DEBUG_GLOBAL` symbol (Phase 1532); the renderer
    // requires the same symbol in ITS compilation — for a Fable build, an
    // explicit `--define FUARAN_DEBUG_GLOBAL` — or `enableDebugGlobal ()`.
    // With neither, nothing is registered and no relay listener is installed.
    //
    // Phase 739 — the same call also PUBLISHES the relay surface for the peer
    // installed at boot, so a browser extension speaking `relay@1.0` reads (and,
    // here, edits) this app through the very same gated path the console uses.
    // The second `true` is the relay opt-in; leaving it `false` publishes
    // nothing, and never calling `Relay.install` at all means no listener exists
    // to probe — the contract's preferred production posture.
    Relay.registerAndPublish
        debugOptIn
        debugOptIn
        model.Tree
        sources
        runtime
        { DebugGlobal.DebugOptions.defaults with
            ApplyHandler = Some applyHandler }

    Render.render ctx model.Tree

// ─── Phase 192 — cross-pipeline apply-parity probe ─────────────────────────────
//
// Expose the shared `ApplyParity.evalOp` on `window.__fuaranParity` so the
// parity session can run the canonical corpus ops through the Fable pipeline
// and assert byte-identical results against the .NET-generated golden. Pure
// over (opJson → result string); no dependence on the rendered tree.

let private exposeParityProbe () : unit =
    Fable.Core.JsInterop.emitJsStatement
        (System.Func<string, string>(fun opJson -> ApplyParity.evalOp opJson))
        "globalThis.__fuaranParity = $0"

// ─── Boot ─────────────────────────────────────────────────────────────────────

exposeParityProbe ()

// Phase 739 — install the DevTools relay page peer ONCE, not per render: it
// holds client subscriptions and reads the live surface each request, so it
// needs no rebuild when the tree moves. `shouldInstall` still requires a DEBUG
// build, so a release Fable build installs no listener at all.
Relay.install true "0.6.0" |> ignore

// The component performs nothing and holds nothing, so its interpreter has no
// kinds and neither of its functions is ever called.
let private app: Component<unit, Model, Msg, NoEffect, NoResource, ElementView<Msg>> =
    Component.plain init update (fun _ -> Map.empty) (ElementView.ofView view)

let private interpreter: Interpreter<Msg, NoEffect, NoResource> =
    Interpreter.create [] (fun NoEffect -> failwith "no effects") (fun _ NoResource -> failwith "no resources")

Live.elements "fuaran-apply-root" Attach.Fresh (Fuaran.MVU.Browser.Live.host ignore) app interpreter ()
|> ignore
