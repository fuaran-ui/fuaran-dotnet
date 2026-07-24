module Fuaran.Samples.Hydration.Tree

// The canonical chrome tree BOTH the server renderer and the client hydrate
// mount build (model-b isomorphic hydration, Phase 143). The same authoring
// code runs on both pipelines: the server (Fuaran.UI.Renderer.Server) renders
// it to HTML on plain .NET; the client (Fuaran.UI.Renderer, Fable) reconstructs
// the identical tree and attaches React with hydrateRoot.
//
// `activeTab` + `onTab` thread the one interactive surface (a Tabs node):
//  - the server passes `activeTab = 0` + an inert `onTab` (no host to dispatch
//    to) — so it renders tab 0 active;
//  - the client passes `activeTab` from its model + an `onTab` that dispatches —
//    after hydration, clicking a tab re-renders the hydrated root via
//    `Hydration.render` (the Elmish-over-hydration update path).
// Because the initial `activeTab` matches (0 on both sides), the first render is
// byte-equivalent and React hydrates without a mismatch.

open Fuaran.UI
open Fuaran.UI.Types

let build<'Msg> (activeTab: int) (onTab: int -> Action<'Msg>) : Node<'Msg> =
    Fuaran.dashboard
        "hroot"
        { Defaults.dashboard<'Msg> with
            Children =
                [ Fuaran.heading
                      "h"
                      { Defaults.heading with
                          Level = 1
                          Text = TextSource.Literal "Fuaran isomorphic hydration" }

                  Fuaran.card
                      "intro"
                      { Defaults.card<'Msg> with
                          Heading = Some(TextSource.Literal "Server-rendered, then hydrated")
                          Children =
                              [ Fuaran.markdown
                                    "blurb"
                                    "This page was **server-rendered** on plain .NET, then **hydrated** by React in the browser — one canonical tree, both pipelines."
                                Fuaran.link "more" "/about" "Learn more" ] }

                  Fuaran.summaryList
                      "sum"
                      { Defaults.summaryList<'Msg> with
                          Heading = Some(TextSource.Literal "At a glance")
                          Children =
                              [ Fuaran.labelValueRow
                                    "r1"
                                    { Defaults.labelValueRow with
                                        Label = TextSource.Literal "Pipelines"
                                        Source = Binding.Static 2.0
                                        Format = CellFormat.Number(Some 0) } ] }

                  Fuaran.tabs
                      "tabs"
                      { Defaults.tabs<'Msg> with
                          Orientation = Horizontal
                          ActiveIndex = Binding.Static activeTab
                          OnSelect = onTab
                          TabHeaders =
                              Some
                                  [ { Label = TextSource.Literal "Overview"
                                      Icon = Option.None
                                      Disabled = Option.None }
                                    { Label = TextSource.Literal "Details"
                                      Icon = Option.None
                                      Disabled = Option.None } ]
                          Children =
                              [ Fuaran.markdown
                                    "tab0"
                                    "**Overview** — click *Details* to switch tabs (live after hydration)."
                                Fuaran.markdown
                                    "tab1"
                                    "**Details** — this panel rendered because the hydrated React loop dispatched a tab change." ] }

                  Fuaran.disclosure
                      "dsc"
                      { Defaults.disclosure<'Msg> with
                          Heading = TextSource.Literal "How it works"
                          Open = Binding.Static true
                          Children =
                              [ Fuaran.markdown "how" "The server emits HTML; the client attaches with `hydrateRoot`." ] } ] }
