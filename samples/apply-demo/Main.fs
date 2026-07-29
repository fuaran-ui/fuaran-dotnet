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
//  apply without a type clash. (The samples/demo Elmish model is domain-shaped,
//  so it wires `apply = None` — the `unwired` envelope.) Mirrors the TS demo's
//  `applied`-state + `onApply` pattern.
//
//  Verify in DevTools (or via the Phase 191 browser session):
//    > __fuaran.getNodeState("headline-metric")
//    > __fuaran.apply('{"$type":"UpdateProp","path":"Label","target":"headline-metric","value":{"$type":"Literal","text":"Mutated!"}}')
//  The metric's label re-renders from "Original Label" to "Mutated!".
// ============================================================================

open Fable.Core.JsInterop
open Elmish
open Elmish.React
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Ops

importSideEffects "./index.css"

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

let init () : Model * Cmd<Msg> = { Tree = baseTree }, Cmd.none

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | ReplaceTree tree -> { Tree = tree }, Cmd.none

// ─── Binding sources + runtime ───────────────────────────────────────────────
//
// The Static-bound metric source needs no live query/state sources; an empty
// `BindingSources` suffices. The browser runtime's `CanDispatch` is
// allow-by-default (Phase 119), so the apply gate permits the in-page mutation.

let private sources: BindingResolver.BindingSources = BindingResolver.empty

let private runtime: Runtime.IFuaranRuntime = BrowserRuntime.create ()

// ─── View ─────────────────────────────────────────────────────────────────────

let view (model: Model) (dispatch: Msg -> unit) =
    let ctx: Render.RenderContext<obj> =
        { Sources = sources
          Runtime = runtime
          VisAdapter = VisAdapter.noOp<obj>
          Dispatch = ignore
          TelemetrySink = None
          InErrorBoundary = false
          Fragments = Map.empty
          ExpandingFragments = Set.empty
          Scope = None
          SessionContext = Map.empty }

    // The host-supplied apply handler: decode the wire TreeOp, apply it to the
    // live Node<obj> tree, and dispatch ReplaceTree to re-render. Phase 90's
    // `applyEnvelope` consults `runtime.CanDispatch(ApplyTreeOp …)` BEFORE this
    // handler ever runs (default-deny by shape, FGP 3). Built per-render so it
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
    // real apply handler (the samples/demo host passes None). `debug = true` is
    // the opt-in; `shouldRegister` still requires a DEBUG build, so a release
    // Fable build dead-code-eliminates the registration.
    //
    // Phase 739 — the same call also PUBLISHES the relay surface for the peer
    // installed at boot, so a browser extension speaking `relay@1.0` reads (and,
    // here, edits) this app through the very same gated path the console uses.
    // The second `true` is the relay opt-in; leaving it `false` publishes
    // nothing, and never calling `Relay.install` at all means no listener exists
    // to probe — the contract's preferred production posture.
    Relay.registerAndPublish
        true
        true
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

Program.mkProgram init update view
|> Program.withReactSynchronous "fuaran-apply-root"
|> Program.run
