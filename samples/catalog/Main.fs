module Fuaran.Samples.Catalog.Main

// ============================================================================
//  Catalog browser entry point.
//
//  Routes between two modes by query string:
//   - Default — the full catalog gallery (Catalog.view).
//   - ?ai-eval=1 — the AI-eval-input mode (a JSON textarea + render
//     pane + §4d-shaped error envelope).
//
//  Parity mode (?parity=1) is intentionally NOT exposed via the
//  Elmish boot — the Playwright harness loads the parity pairs by
//  pair id (?parity=<pair-id>) and renders ONLY the Fuaran node or
//  ONLY the Feliz function (?side=fuaran|feliz). Pixel diffs run
//  off those isolated renders.
// ============================================================================

open Fable.Core.JsInterop
open Elmish
open Elmish.React
open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Ops

importSideEffects "./index.css"

// ─── Coverage assertion (boot-time) ───────────────────────────────────────
//
// Surfaces missing NodeKind coverage as a console error rather than a
// silent gap. Every component the type contract declares is renderable
// from the catalog.

let private warnMissingKinds () : unit =
    let missing = Matrix.missingKinds ()

    if not (List.isEmpty missing) then
        Browser.Dom.console.error (
            "[catalog] Missing NodeKind entries: " + (String.concat ", " missing),
            "Add a matching KindEntry to samples/catalog/Matrix.fs."
        )

// ─── URL query parsing ───────────────────────────────────────────────────
//
// Fable maps `Browser.Dom.window.location.search` to the raw `?…` string.
// The catalog only needs a flat key/value scan — no nested params, no
// repeating keys.

let private queryParam (key: string) : string option =
    let search = Browser.Dom.window.location.search

    if System.String.IsNullOrEmpty search then
        None
    else
        let trimmed = search.TrimStart('?')

        trimmed.Split('&')
        |> Array.tryPick (fun pair ->
            let parts = pair.Split('=')

            if parts.Length >= 1 && parts[0] = key then
                if parts.Length >= 2 then
                    Some(System.Uri.UnescapeDataString parts[1])
                else
                    Some ""
            else
                None)

// ─── AI-eval-input mode ──────────────────────────────────────────────────

type AiEvalModel =
    { Input: string
      // Storage-shape Node<obj> — the canonical Ops.JsonDecode.decodeNode
      // erases 'Msg to obj on the wire. The error half is the §4d-shaped
      // recovery envelope rendered into the error pane.
      Rendered: Result<Node<obj>, string> option }

type AiEvalMsg =
    | UpdateInput of string
    | RenderInput

/// Format the canonical decoder's structured `DecodeError` into a compact
/// §4d-shaped AI-recovery JSON envelope for the error pane. Mirrors the
/// vocabulary Gate 1 pattern-matches on downstream (code / path / message).
let private decodeErrorEnvelope (e: JsonDecode.DecodeError) : string =
    let esc (s: string) =
        s.Replace("\\", "\\\\").Replace("\"", "\\\"")

    let fields =
        [ sprintf "  \"code\": \"%s\"" (esc e.Code)
          sprintf "  \"path\": \"%s\"" (esc e.Path)
          sprintf "  \"message\": \"%s\"" (esc e.Message) ]
        @ (match e.ExpectedShape with
           | Some s -> [ sprintf "  \"expectedShape\": \"%s\"" (esc s) ]
           | None -> [])

    "{\n" + String.concat ",\n" fields + "\n}"

// Canonical wire shape per WIRE_FORMAT.md — `kind` is a `$type`-hoisted
// flat object, default style/state omitted (algorithm rule 4). This is the
// exact shape `CanonicalJson.encodeNode` emits and `Ops.JsonDecode.decodeNode`
// consumes; it round-trips through the gallery inspector. (Pre-dedup the
// catalog accepted its own narrower `{id, kind, props}` illustrative shape.)
let private aiEvalSample =
    """{
  "id": "channel-analysis",
  "kind": {
    "$type": "Dashboard",
    "children": [
      {
        "id": "revenue-metric",
        "kind": {
          "$type": "Metric",
          "emphasis": "Normal",
          "format": { "$type": "None" },
          "label": { "$type": "Literal", "text": "Revenue" },
          "source": { "$type": "Static", "value": 142500 },
          "tone": "Brand",
          "weight": "Standard"
        }
      },
      {
        "id": "summary",
        "kind": {
          "$type": "Markdown",
          "text": { "$type": "Literal", "text": "Revenue trend across the quarter." }
        }
      }
    ]
  }
}"""

let private aiEvalInit () : AiEvalModel * Cmd<AiEvalMsg> =
    { Input = aiEvalSample
      Rendered = None },
    Cmd.none

let private aiEvalUpdate (msg: AiEvalMsg) (model: AiEvalModel) : AiEvalModel * Cmd<AiEvalMsg> =
    match msg with
    | UpdateInput s -> { model with Input = s }, Cmd.none
    | RenderInput ->
        // Previewing a decoded wire tree: its closures are inert, so we
        // `WireTree.reify` before rendering it through the (no-op diagnostic)
        // runtime — the deliberate escape hatch. A real interactive host would
        // drive the WireTree server-side instead (BoundedDriver.init) or
        // re-attach handlers; here it is a read-only structural preview.
        let decoded =
            JsonDecode.decodeNode model.Input
            |> Result.map WireTree.reify
            |> Result.mapError decodeErrorEnvelope

        { model with Rendered = Some decoded }, Cmd.none

let private aiEvalRenderCtx () : Render.RenderContext<obj> =
    { Sources = BindingResolver.empty
      Runtime = Runtime.diagnostic
      VisAdapter = VisAdapter.noOp<obj>
      Dispatch = ignore
      TelemetrySink = None
      InErrorBoundary = false
      Fragments = Map.empty
      ExpandingFragments = Set.empty
      Scope = None
      SessionContext = Map.empty }

let private aiEvalView (model: AiEvalModel) (dispatch: AiEvalMsg -> unit) : ReactElement =
    React.Fragment
        [ Render.themeStyleElement Defaults.theme
          Html.div
              [ prop.className "catalog-ai-eval"
                prop.children
                    [ Html.h1 [ prop.text "Fuaran AI-eval-input mode" ]
                      Html.p
                          [ prop.text
                                "Paste a Fuaran §4d JSON tree. The decoder accepts the canonical wire shape; on success the tree renders below, on failure the §4d AI-recovery error envelope renders in the error pane." ]
                      Html.textarea
                          [ prop.value model.Input
                            prop.onChange (fun (v: string) -> dispatch (UpdateInput v)) ]
                      Html.div
                          [ prop.className "catalog-ai-eval-actions"
                            prop.children
                                [ Html.button [ prop.onClick (fun _ -> dispatch RenderInput); prop.text "Render" ] ] ]
                      match model.Rendered with
                      | None -> Html.none
                      | Some(Ok node) ->
                          Html.div
                              [ prop.className "catalog-ai-eval-render"
                                prop.children [ Render.render (aiEvalRenderCtx ()) node ] ]
                      | Some(Error envelope) -> Html.pre [ prop.className "catalog-ai-eval-error"; prop.text envelope ] ] ] ]

// ─── Parity-mode entry (Playwright harness target) ───────────────────────
//
// `?parity=<pair-id>&side=fuaran|feliz` renders one half of a parity pair
// in isolation so the Playwright harness can pixel-diff the two
// renders. Renders against the default theme — parity-mode tests
// don't sweep themes; that's the regression-snapshot harness's job.

let private parityView () : ReactElement =
    let pairId = queryParam "parity" |> Option.defaultValue ""
    let side = queryParam "side" |> Option.defaultValue "fuaran"

    let pair = Parity.pairs |> List.tryFind (fun p -> p.Id = pairId)

    React.Fragment
        [ Render.themeStyleElement Defaults.theme
          match pair with
          | None ->
              Html.div
                  [ prop.style [ style.padding 24 ]
                    prop.text (sprintf "No parity pair with id '%s'" pairId) ]
          | Some p when side = "feliz" ->
              Html.div
                  [ prop.id ("parity-feliz-" + p.Id)
                    prop.style [ style.padding 24 ]
                    prop.children [ p.Feliz ] ]
          | Some p ->
              let ctx: Render.RenderContext<unit> =
                  { Sources = BindingResolver.empty
                    Runtime = Runtime.diagnostic
                    VisAdapter = VisAdapter.noOp<unit>
                    Dispatch = (fun () -> ())
                    TelemetrySink = None
                    InErrorBoundary = false
                    Fragments = Map.empty
                    ExpandingFragments = Set.empty
                    Scope = None
                    SessionContext = Map.empty }

              Html.div
                  [ prop.id ("parity-fuaran-" + p.Id)
                    prop.style [ style.padding 24 ]
                    prop.children [ Render.render ctx p.Fuaran ] ] ]

// ─── Boot ────────────────────────────────────────────────────────────────

let private mountId = "fuaran-catalog-root"

let private bootGallery () : unit =
    // mkProgram (not mkSimple): the gallery's init/update reflect the active
    // kind/theme/locale/a11y selection into the query string via an Elmish
    // effect (Phase 169 deep-linking), so they return Cmd<Msg>.
    Program.mkProgram Catalog.init Catalog.update Catalog.view
    |> Program.withReactSynchronous mountId
    |> Program.run

let private bootAiEval () : unit =
    Program.mkProgram aiEvalInit aiEvalUpdate aiEvalView
    |> Program.withReactSynchronous mountId
    |> Program.run

let private bootParity () : unit =
    // Reuse Elmish + react-synchronous for parity mode so the boot path
    // matches the gallery + AI-eval modes (one less code shape to keep
    // working). The "Model" is unit; the view is constant.
    Program.mkSimple (fun () -> ()) (fun () () -> ()) (fun () _ -> parityView ())
    |> Program.withReactSynchronous mountId
    |> Program.run

// ─── Interaction-state harness (?interaction-state=1[&bridge=on]) ────────
//
// Snapshot-test target. Renders the InteractionStates fixture
// with the optional consumer-bridge stylesheet appended AFTER
// `themeStyleElement` so the bridge wins the cascade. The Playwright
// spec at `snapshot/interaction-state.spec.mts` probes both modes from
// the same page and asserts every reference-CSS-consumed tone-state
// variable propagates the override.

let private bootInteractionStates () : unit =
    let bridge =
        match queryParam "bridge" with
        | Some value -> value = "on" || value = "1" || value = "true"
        | None -> false

    let view () _ : ReactElement =
        React.Fragment [ Render.themeStyleElement Defaults.theme; InteractionStates.view bridge ]

    Program.mkSimple (fun () -> ()) (fun () () -> ()) view
    |> Program.withReactSynchronous mountId
    |> Program.run

// ─── Local-bindings harness (?local-bindings=1) ──────────────────────────
//
// Mounts the LocalBindings catalog page so the Playwright spec
// at `snapshot/local-bindings.spec.mts` can exercise the four canonical
// shapes (Salary / Email / Note / Re-sync). Uses Elmish so the model-side
// dispatches surface in the visible mirror panel.

let private bootLocalBindings () : unit =
    Program.mkSimple LocalBindings.init LocalBindings.update LocalBindings.view
    |> Program.withReactSynchronous mountId
    |> Program.run

// ─── Tabs harness (?tabs-69=1) ───────────────────────────────────────────
//
// Explicit headers + ARIA / keyboard navigation + typed-tag
// overlay snapshot target. Renders the Tabs69 fixture (Overview / Detail
// Custom-wrapped / Audit disabled). Playwright spec at
// `snapshot/tabs-69.spec.mts` probes the ARIA emission, arrow-key
// behaviour, disabled-tab skipping, and tag-overlay round-trip.

let private bootTabs69 () : unit =
    Program.mkProgram Tabs69.init Tabs69.update Tabs69.view
    |> Program.withReactSynchronous mountId
    |> Program.run

// ─── Clipboard harness (?clipboard=1) ────────────────────────────────────
//
// A single Fuaran.button whose OnClick dispatches
// Action.WriteToClipboard. The browser runtime wires
// navigator.clipboard.writeText; the mirror panel below echoes the
// dispatch chain so the Playwright spec can assert the click fired.

let private bootClipboard () : unit =
    Program.mkSimple Clipboard.init Clipboard.update Clipboard.view
    |> Program.withReactSynchronous mountId
    |> Program.run

// ─── Bounded-escape harness (?bounded-escape-70=1) ───────────────────────
//
// Custom bounded-escape verification fixture. Renders four
// Custom variants exercising the contentHash + exposedNodeIds matrix
// (green / strict-mismatch / advisory-mismatch / missing-exposed-ids).
// The captured-warnings panel surfaces the renderer's Warn-channel
// output so the Playwright spec can assert the verification fired.

let private bootBoundedEscape70 () : unit =
    Program.mkProgram BoundedEscape70.init BoundedEscape70.update BoundedEscape70.view
    |> Program.withReactSynchronous mountId
    |> Program.run

// ─── Error-boundary harness (?error-boundary-60=1) ───────────────────────
//
// Render-time error boundaries + per-node fallback fixture.
// Renders three cards: per-node guard isolating a throwing leaf among
// healthy siblings; ErrorBoundary catching a failing subtree and
// rendering the typed Fallback; ErrorBoundary on a clean path
// contributing nothing visible. Operator inspects the per-node
// fallback placeholder + the boundary's Fallback callout + the
// captured-warnings panel.

let private bootErrorBoundary60 () : unit =
    Program.mkProgram ErrorBoundary60.init ErrorBoundary60.update ErrorBoundary60.view
    |> Program.withReactSynchronous mountId
    |> Program.run

// ─── Op-stream inspector harness (?inspector=1) ──────────────────────────
//
// Snapshot-diff + op-stream visual inspector fixture. Hand-
// curated three-op segment with timeline scrubber, tree-after render,
// and side panel showing the OpRecord + telemetry + per-id diff
// entries. Append `?inspector-fork=1` to flip the chain-verified flag
// false and exercise the `StepDiffsError.IntegrityFailed` banner state.

let private bootInspector57 () : unit =
    let verifiedChain = queryParam "inspector-fork" |> Option.isNone

    Program.mkSimple (fun () -> Inspector57.init verifiedChain) Inspector57.update Inspector57.view
    |> Program.withReactSynchronous mountId
    |> Program.run

// ─── Fragments harness (?fragments-61=1) ─────────────────────────────────
//
// Reusable subtree fragments + named partials fixture. Three
// cards: a decl with no matching ref (zero paint); a decl + two refs
// demonstrating interior-id namespacing; a cyclic decl pair exercising
// the runtime cycle-guard placeholder.

let private bootFragments61 () : unit =
    Program.mkProgram Fragments61.init Fragments61.update Fragments61.view
    |> Program.withReactSynchronous mountId
    |> Program.run

// ─── State-reactive harness (?state-reactive-106=1) ───────────────────────
//
// Phase 106 — two independent panes reading the same global Binding.State
// key via renderStateReactive; a toggle writes the key through
// Action.SetState and BOTH panes re-render at once.

let private bootStateReactive106 () : unit =
    Program.mkSimple StateReactive106.init StateReactive106.update StateReactive106.view
    |> Program.withReactSynchronous mountId
    |> Program.run

// ─── Formatting harness (?formatting-102=1) ──────────────────────────────
//
// Phase 102 — locale-aware Number / Currency / Percent / Date / RelativeTime
// formatting bindings (`binding.format`), one axis per Format case rendering
// the same source across multiple locales via the browser Intl APIs.

let private bootFormatting102 () : unit =
    Program.mkSimple Formatting102.init Formatting102.update Formatting102.view
    |> Program.withReactSynchronous mountId
    |> Program.run

// ─── Interactive-disabled harness (?interactive-disabled-130=1) ──────────────
//
// Phase 130 — bound disabled-state across interactive kinds. Button + Select +
// Form + FileUpload each bind their `Disabled` slot to one global
// `Binding.State "busy"` key; a toggle disables / enables the whole class at
// once through `Action.setState`, re-rendering via the StateStore subscription.

let private bootInteractiveDisabled130 () : unit =
    Program.mkSimple InteractiveDisabled130.init InteractiveDisabled130.update InteractiveDisabled130.view
    |> Program.withReactSynchronous mountId
    |> Program.run

// Phase 58 responsive-collapse harness. Constant view; the responsive
// behaviour is driven by the browser viewport width + the reference-CSS
// `@media` rules, so no model state is needed.
let private bootViewport () : unit =
    Program.mkSimple (fun () -> ()) (fun () () -> ()) (fun () _ -> MobileViewport.view ())
    |> Program.withReactSynchronous mountId
    |> Program.run

warnMissingKinds ()

// `?viewport=mobile` is handled ahead of the gallery/harness router so the
// existing 13-arm dispatch stays untouched (guard-clause idiom).
if queryParam "viewport" = Some "mobile" then
    bootViewport ()
else

    match
        queryParam "parity",
        queryParam "ai-eval",
        queryParam "interaction-state",
        queryParam "local-bindings",
        queryParam "tabs-69",
        queryParam "clipboard",
        queryParam "bounded-escape-70",
        queryParam "error-boundary-60",
        queryParam "inspector",
        queryParam "fragments-61",
        queryParam "state-reactive-106",
        queryParam "formatting-102",
        queryParam "interactive-disabled-130"
    with
    | Some _, _, _, _, _, _, _, _, _, _, _, _, _ -> bootParity ()
    | _, Some _, _, _, _, _, _, _, _, _, _, _, _ -> bootAiEval ()
    | _, _, Some _, _, _, _, _, _, _, _, _, _, _ -> bootInteractionStates ()
    | _, _, _, Some _, _, _, _, _, _, _, _, _, _ -> bootLocalBindings ()
    | _, _, _, _, Some _, _, _, _, _, _, _, _, _ -> bootTabs69 ()
    | _, _, _, _, _, Some _, _, _, _, _, _, _, _ -> bootClipboard ()
    | _, _, _, _, _, _, Some _, _, _, _, _, _, _ -> bootBoundedEscape70 ()
    | _, _, _, _, _, _, _, Some _, _, _, _, _, _ -> bootErrorBoundary60 ()
    | _, _, _, _, _, _, _, _, Some _, _, _, _, _ -> bootInspector57 ()
    | _, _, _, _, _, _, _, _, _, Some _, _, _, _ -> bootFragments61 ()
    | _, _, _, _, _, _, _, _, _, _, Some _, _, _ -> bootStateReactive106 ()
    | _, _, _, _, _, _, _, _, _, _, _, Some _, _ -> bootFormatting102 ()
    | _, _, _, _, _, _, _, _, _, _, _, _, Some _ -> bootInteractiveDisabled130 ()
    | None, None, None, None, None, None, None, None, None, None, None, None, None -> bootGallery ()
