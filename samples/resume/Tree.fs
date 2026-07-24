module Fuaran.Samples.Resume.Tree

// The canonical page BOTH load strategies render server-side, so the harness
// measures resume vs hydrate on the *same* tree (Phase 177 §7).
//
// The page is deliberately **Navigate-dominated** — the surface class the spike
// verdict recommends default-on resume for (`docs/RESUMABILITY-SPIKE.md`): four
// `Navigate` buttons (all `interpret` disposition — run directly against
// `IFuaranRuntime`, no module boot, no view) over a body of static content, plus
// exactly one `Dispatch` "boot" button (the `boot` disposition) so the harness
// can also time the lazy-chunk first-interaction path the app-shell surface
// class would pay.
//
// `onBoot` threads the one Dispatch handler:
//  - the resume gen passes `Action.Dispatch (box "boot")` — opaque on the wire,
//    so the envelope marks the node `boot`; the client lazy-imports its chunk on
//    first click;
//  - the hydrate baseline passes a real `Dispatch BootMsg` and the whole tree
//    hydrates at load.

open Fuaran.UI
open Fuaran.UI.Types

let private navButton<'Msg> (id: string) (label: string) (route: string) : Node<'Msg> =
    Fuaran.button
        id
        { Defaults.button<'Msg> with
            Label = TextSource.Literal label
            Variant = ButtonVariant.Secondary
            OnClick = Action.Navigate route }

let build<'Msg> (count: int) (onBoot: Action<'Msg>) : Node<'Msg> =
    Fuaran.dashboard
        "rroot"
        { Defaults.dashboard<'Msg> with
            Children =
                [ Fuaran.heading
                      "h"
                      { Defaults.heading with
                          Level = 1
                          Text = TextSource.Literal "Fuaran zero-hydration resumability" }

                  Fuaran.card
                      "intro"
                      { Defaults.card<'Msg> with
                          Heading = Some(TextSource.Literal "Server-rendered, resumed on demand")
                          Children =
                              [ Fuaran.markdown
                                    "blurb"
                                    "This page **executes zero framework JS at load**. The links below run directly from a per-node `Action` envelope on first click — no module boot, no React. Only the *Boot* button lazy-loads an interactive chunk."
                                Fuaran.markdown
                                    "blurb2"
                                    "Because a Fuaran `Action` is *serialisable typed data, not an opaque closure*, the server ships what each node does and the client reads it." ] }

                  Fuaran.summaryList
                      "sum"
                      { Defaults.summaryList<'Msg> with
                          Heading = Some(TextSource.Literal "At a glance")
                          Children =
                              [ Fuaran.labelValueRow
                                    "r1"
                                    { Defaults.labelValueRow with
                                        Label = TextSource.Literal "Framework JS executed at load"
                                        Source = Binding.Static 0.0
                                        Format = CellFormat.Number(Some 0) }
                                Fuaran.labelValueRow
                                    "r2"
                                    { Defaults.labelValueRow with
                                        Label = TextSource.Literal "Boot count"
                                        Source = Binding.Static(float count)
                                        Format = CellFormat.Number(Some 0) } ] }

                  // The Navigate row — every button `interpret`s directly.
                  Fuaran.card
                      "nav"
                      { Defaults.card<'Msg> with
                          Heading = Some(TextSource.Literal "Navigate (interpret — zero boot)")
                          Children =
                              [ navButton "navHome" "Home" "/home"
                                navButton "navDocs" "Docs" "/docs"
                                navButton "navPricing" "Pricing" "/pricing"
                                navButton "navAbout" "About" "/about" ] }

                  // The one Dispatch node — `boot` disposition, lazy chunk.
                  Fuaran.card
                      "bootcard"
                      { Defaults.card<'Msg> with
                          Heading = Some(TextSource.Literal "Dispatch (boot — lazy chunk on first click)")
                          Children =
                              [ Fuaran.button
                                    "bootBtn"
                                    { Defaults.button<'Msg> with
                                        Label = TextSource.Literal "Boot the interactive island"
                                        Variant = ButtonVariant.Primary
                                        OnClick = onBoot } ] }

                  Fuaran.disclosure
                      "dsc"
                      { Defaults.disclosure<'Msg> with
                          Heading = TextSource.Literal "How it works"
                          Open = Binding.Static true
                          Children =
                              [ Fuaran.markdown
                                    "how"
                                    "The server emits inert HTML + a flat `nodeId → Action` envelope. One document-root listener resumes the touched node on first interaction." ] } ] }
