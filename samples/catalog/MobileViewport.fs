module Fuaran.Samples.Catalog.MobileViewport

// ============================================================================
//  Mobile / responsive-collapse harness (Phase 58).
//
//  Page URL: `?viewport=mobile`.
//
//  Renders a curated stack of the layout primitives the Phase 58 responsive
//  reference-CSS rules target — a multi-column Grid, a horizontal Stack, a
//  SplitPanel, a Tabs group, a wide Table + DataGrid, and a Form with
//  buttons / inputs. At a phone-width viewport the `@media (max-width: 640px)`
//  rules collapse each to a single column (or scroll the wide tabular data /
//  tab bar), and interactive leaves reach the 44px touch-target floor.
//
//  The exhibit is the same set of hand-picked representatives the gallery
//  matrix uses (`Matrix.entries`), so it stays in lockstep with the catalog's
//  component coverage. The Playwright spec at
//  `snapshot/viewport-mobile.spec.mts` boots this page under a phone viewport
//  and asserts no horizontal overflow + no clipped touch targets at `sm`.
// ============================================================================

open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

// The layout primitives the responsive rules collapse / scroll, in the order
// they read best stacked vertically. Each id resolves against `Matrix.entries`;
// a missing id is skipped (kept honest by the boot-time coverage warning).
let private exhibitIds: string list =
    [ "Dashboard"
      "GridLayout"
      "GridLayoutIrregular"
      "Stack"
      "SplitPanel"
      "Tabs"
      "Table"
      "Grid"
      "Form"
      "Button" ]

let private renderCtx: Render.RenderContext<unit> =
    { Sources = BindingResolver.empty
      Runtime = Runtime.diagnostic
      VisAdapter = VisAdapter.noOp<unit>
      Dispatch = (fun () -> ())
      TelemetrySink = None
      InErrorBoundary = false
      Fragments = Map.empty
      ExpandingFragments = Set.empty
      Scope = None }

let private sample (entry: Matrix.KindEntry) : ReactElement =
    let node = entry.Build(ToneVariant.Brand, StyleWeight.Standard, Emphasis.Normal)

    Html.section
        [ prop.className "viewport-mobile-item"
          prop.custom ("data-kind", entry.Id)
          prop.children
              [ Html.h2 [ prop.className "viewport-mobile-label"; prop.text entry.Label ]
                Render.render renderCtx node ] ]

let view () : ReactElement =
    let items =
        exhibitIds
        |> List.choose (fun id -> Matrix.entries |> List.tryFind (fun e -> e.Id = id))
        |> List.map sample

    React.Fragment
        [ Render.themeStyleElement Defaults.theme
          Html.div
              [ prop.id "viewport-mobile-page"
                prop.className "viewport-mobile-page"
                prop.children
                    [ Html.h1 [ prop.text "Responsive layout collapse" ]
                      Html.p
                          [ prop.text
                                "Phase 58 harness. Below the sm breakpoint the grid / stack / split-panel collapse to one column, the tab bar and wide tables scroll horizontally within their box, and interactive controls reach a 44px touch-target floor." ]
                      yield! items ] ] ]
