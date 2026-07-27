module Fuaran.Samples.Catalog.Clipboard

// ============================================================================
//  Catalog page exercising `Action.WriteToClipboard`.
//
//  Shape: a single Fuaran.button whose `OnClick = Action.WriteToClipboard
//  "Hello from Fuaran"`. The renderer routes through `BrowserRuntime`'s
//  `IFuaranRuntime.WriteToClipboard` impl (navigator.clipboard.writeText
//  with an execCommand("copy") fallback). The mirror panel below the
//  button surfaces the last copied payload echoed back from a parallel
//  `Action.Dispatch` so the Playwright spec can assert the click fired
//  even when the clipboard-permission UX blocks the actual write
//  (headless Chromium grants the clipboard permission by default; this
//  fallback only matters on locked-down CI environments).
//
//  Mounted at `?clipboard=1`. The page intentionally uses the most
//  conservative shape — no form, no Local binding — so the Action.*
//  forward-coupling is the only thing being exercised.
// ============================================================================

open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

// ─── Model ─────────────────────────────────────────────────────────────────

type Model =
    { LastCopied: string option
      CopyCount: int }

type Msg = CopiedSentinel of string

let init () : Model = { LastCopied = None; CopyCount = 0 }

let update (msg: Msg) (model: Model) : Model =
    match msg with
    | CopiedSentinel text ->
        { model with
            LastCopied = Some text
            CopyCount = model.CopyCount + 1 }

// ─── View ──────────────────────────────────────────────────────────────────

let private clipboardPayload = "Hello from Fuaran"

let private copyButton: Node<Msg> =
    Fuaran.button
        "copy-button"
        { Defaults.button<Msg> with
            Label = TextSource.Literal "Copy 'Hello from Fuaran'"
            Variant = ButtonVariant.Primary
            OnClick =
                Action.Chain
                    [ Action.WriteToClipboard clipboardPayload
                      Action.dispatch (CopiedSentinel clipboardPayload) ] }

let view (model: Model) (dispatch: Msg -> unit) : ReactElement =
    let ctx: Render.RenderContext<Msg> =
        { Sources = BindingResolver.empty
          Runtime = BrowserRuntime.create ()
          VisAdapter = VisAdapter.noOp<Msg>
          Dispatch = dispatch
          TelemetrySink = None
          InErrorBoundary = false
          Fragments = Map.empty
          ExpandingFragments = Set.empty
          Scope = None
          SessionContext = Map.empty }

    React.Fragment
        [ Render.themeStyleElement Defaults.theme
          Html.div
              [ prop.id "clipboard-page"
                prop.className "catalog-clipboard"
                prop.style [ style.padding 24; style.maxWidth 720 ]
                prop.children
                    [ Html.h1 [ prop.text "Clipboard" ]
                      Html.p
                          [ prop.text
                                "A single Fuaran.button whose OnClick dispatches Action.WriteToClipboard. The browser runtime wires navigator.clipboard.writeText; the mirror below confirms the dispatch chain fired." ]
                      Html.section
                          [ prop.style [ style.marginTop 16 ]
                            prop.children [ Render.render ctx copyButton ] ]
                      Html.section
                          [ prop.style [ style.marginTop 24; style.padding 12 ]
                            prop.children
                                [ Html.h2 [ prop.text "Mirror panel (dispatch-chain echo)" ]
                                  Html.dl
                                      [ prop.style [ style.fontFamily "monospace" ]
                                        prop.children
                                            [ Html.dt [ prop.text "Last copied payload" ]
                                              Html.dd
                                                  [ prop.testId "model-last-copied"
                                                    prop.text (model.LastCopied |> Option.defaultValue "<none>") ]
                                              Html.dt [ prop.text "Click count" ]
                                              Html.dd
                                                  [ prop.testId "model-copy-count"; prop.text (string model.CopyCount) ] ] ] ] ] ] ] ]
