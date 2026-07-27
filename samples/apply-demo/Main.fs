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
                          Value = Binding.Static 42.0
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
        | Error e -> DebugGlobal.ApplyOutcome.DecodeFailed e.Message
        | Ok op ->
            match Apply.apply op model.Tree with
            | Ok newTree ->
                dispatch (ReplaceTree newTree)
                DebugGlobal.ApplyOutcome.Applied
            | Error err -> DebugGlobal.ApplyOutcome.Rejected err.Message

    // Phase 90 — register the in-page REPL over the live Node<obj> tree WITH a
    // real apply handler (the samples/demo host passes None). `debug = true` is
    // the opt-in; `shouldRegister` still requires a DEBUG build, so a release
    // Fable build dead-code-eliminates the registration.
    DebugGlobal.register true model.Tree sources runtime (Some applyHandler)

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

Program.mkProgram init update view
|> Program.withReactSynchronous "fuaran-apply-root"
|> Program.run
