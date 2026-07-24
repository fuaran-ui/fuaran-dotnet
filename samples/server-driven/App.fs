module ServerDriven.Sample.App

open Fuaran.UI
open Fuaran.UI.Types

// ─── The app: a tabbed counter (Phase 152 Track E sample) ──────────────
//
// A neutral, language-tier-only worked example of the server-driven tier. The
// model holds the count, the active tab, and the disclosure state; `update`
// runs on the SERVER; clicking a button / tab header / disclosure summary
// round-trips through the server `update` and the changed region patches in
// place with no full re-render and no flash.
//
// Tabs + Disclosure are here deliberately: they exercise the shim's click
// payload bridges (a tab-header click carries `payload.index` from
// `data-tab-index`; a summary click carries `payload.open` from the
// `<details>` state) — the layout-interactivity half of the loop, alongside
// the buttons' plain-click half.
//
// The tree is `Node<obj>` (Msg boxed at the dispatch site) so it renders
// directly through `Renderer.Server.Render.render`, whose injected form is the
// driver's `renderFragment`. The driver invokes `update` (the server-closure
// win) — no Fable compile, no client bundle.

type Model =
    { Count: int; Tab: int; Advanced: bool }

let initial = { Count = 0; Tab = 0; Advanced = false }

type Msg =
    | Inc
    | Dec
    | Reset
    | SelectTab of int
    | ToggleAdvanced of bool

let update (msgObj: obj) (model: Model) : Model =
    match unbox<Msg> msgObj with
    | Inc -> { model with Count = model.Count + 1 }
    | Dec -> { model with Count = model.Count - 1 }
    | Reset -> { model with Count = 0 }
    | SelectTab i -> { model with Tab = i }
    | ToggleAdvanced isOpen -> { model with Advanced = isOpen }

let private dispatch (msg: Msg) : Action<obj> =
    Action.Dispatch(Unchecked.nonNull (box msg))

let private btn (id: string) (label: string) (msg: Msg) : Node<obj> =
    Fuaran.button
        id
        { Defaults.button<obj> with
            Label = TextSource.Literal label
            OnClick = dispatch msg }

/// Tab 0 — the counter. The card heading doubles as the tab label
/// (Card-heading inference; no explicit TabHeaders needed).
let private counterTab (model: Model) : Node<obj> =
    Fuaran.card
        "counter-tab"
        { Defaults.card<obj> with
            Heading = Some(TextSource.Literal "Counter")
            Children =
                [ Fuaran.markdown "count" (sprintf "Count: **%d**" model.Count)
                  btn "inc" "+1" Inc
                  btn "dec" "-1" Dec
                  btn "reset" "Reset" Reset ] }

/// Tab 1 — static content, so switching tabs visibly swaps the panel.
let private aboutTab: Node<obj> =
    Fuaran.card
        "about-tab"
        { Defaults.card<obj> with
            Heading = Some(TextSource.Literal "About")
            Children =
                [ Fuaran.markdown
                      "about-text"
                      "Tab clicks ship `payload.index` to the server; the active panel patches in place." ] }

let view (model: Model) : Node<obj> =
    Fuaran.dashboard
        "app"
        { Defaults.dashboard<obj> with
            Children =
                [ Fuaran.markdown "title" "# Fuaran server-driven counter"
                  Fuaran.tabs
                      "tabs"
                      { Defaults.tabs<obj> with
                          ActiveIndex = Binding.Static model.Tab
                          OnSelect = Some(fun i -> dispatch (SelectTab i))
                          Children = [ counterTab model; aboutTab ] }
                  Fuaran.disclosure
                      "advanced"
                      { Defaults.disclosure<obj> with
                          Heading = TextSource.Literal "Advanced"
                          Open = Binding.Static model.Advanced
                          DefaultOpen = model.Advanced
                          OnToggle = Some(fun isOpen -> dispatch (ToggleAdvanced isOpen))
                          Children =
                              [ Fuaran.markdown
                                    "advanced-state"
                                    (sprintf "The server sees open = **%b**." model.Advanced) ] } ] }
