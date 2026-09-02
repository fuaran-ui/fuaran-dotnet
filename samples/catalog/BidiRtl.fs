module Fuaran.Samples.Catalog.BidiRtl

// ============================================================================
//  Right-to-left / bidi harness (Phase 1114).
//
//  Page URL: `?dir=rtl`.
//
//  The visual regression surface for the reference stylesheet's logical-property
//  sweep. Three regions, deliberately of MIXED direction, because a page that is
//  uniformly right-to-left proves only that a mirror exists — it cannot show
//  whether the mirror is scoped:
//
//   1. An RTL region (`dir="rtl" lang="ar"`) holding the layout and form
//      primitives whose inline-axis geometry the sweep moved: the callout's
//      accent border, the disclosure chevron, the vertical tab bar's divider,
//      the table header's text alignment, the button's icon gap, the list's
//      marker indent. Every one of these must appear on the opposite edge from
//      the LTR region below, with no horizontal overflow.
//
//   2. An LTR island NESTED INSIDE that RTL region (`dir="ltr"`), holding a data
//      grid. Numeric tabular data is conventionally read left-to-right even on a
//      right-to-left page, and a host says so by setting `dir` on the subtree.
//      If the sweep had reached for an `[dir="rtl"]` override block instead of
//      logical properties, this island would still be mirrored — so it is the
//      region that distinguishes a correct sweep from a plausible one.
//
//   3. A bidi text run: three headings whose text is RUNTIME-BOUND, carrying
//      Arabic, English, and a mixed string. Under the Phase 1114 policy the
//      renderer emits `dir="auto"` on each, so the browser resolves each line
//      from its own first strong character rather than from the region's
//      direction. The Arabic line inside the LTR region and the English line
//      inside the RTL region are the two that show it working; a literal
//      heading beside them is the control, and stays with the region.
// ============================================================================

open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

let private renderCtx: Render.RenderContext<unit> =
    { Sources = BindingResolver.empty
      Runtime = Runtime.diagnostic
      VisAdapter = VisAdapter.noOp<unit>
      Dispatch = (fun () -> ())
      TelemetrySink = None
      InErrorBoundary = false
      Fragments = Map.empty
      ExpandingFragments = Set.empty
      Scope = None
      SessionContext = Map.empty
      ActionSink = None
      CurrentNodeId = None
      // Phase 1026 — a HAND-AUTHORED tree, where the author is the trust
      // boundary, so the permissive posture is correct and is reached BY NAME.
      // A host rendering a DECODED tree must not copy this line.
      EgressPolicy = Sanitize.permissiveEgress }

/// The mirrored surfaces, in the order they read best stacked. Each id resolves
/// against `Matrix.entries`, so the harness stays in lockstep with the catalog's
/// component coverage rather than growing its own private exhibits; a missing id
/// is skipped (kept honest by the boot-time coverage warning).
let private mirroredIds: string list =
    [ "Callout"
      "Disclosure"
      "Tabs"
      "Form"
      "Button"
      "Metric"
      "LabelValueRow"
      "Badge" ]

/// The LTR island's content: tabular data, read left-to-right even here.
let private ltrIslandIds: string list = [ "Grid"; "Table" ]

let private sample (entry: Matrix.KindEntry) : ReactElement =
    let node = entry.Build(ToneVariant.Brand, StyleWeight.Standard, Emphasis.Normal)

    Html.section
        [ prop.className "bidi-rtl-item"
          prop.custom ("data-kind", entry.Id)
          prop.children
              [ Html.h2 [ prop.className "bidi-rtl-label"; prop.text entry.Label ]
                Render.render renderCtx node ] ]

let private samples (ids: string list) : ReactElement list =
    ids
    |> List.choose (fun id -> Matrix.entries |> List.tryFind (fun e -> e.Id = id))
    |> List.map sample

// ─── The bidi text run ──────────────────────────────────────────────────────

/// A heading whose text is runtime-BOUND — the slot the `dir="auto"` policy
/// admits. `Binding.Static` is a bound source that needs no store, so the page
/// stays a constant view while still exercising the policy's real branch.
let private boundHeading (id: string) (text: string) : Node<unit> =
    Fuaran.heading
        id
        { Defaults.heading with
            Level = 3
            Text = TextSource.Bound(Binding.Static(Some text)) }

/// The same heading with AUTHORED text — the control. It carries no `dir`, so it
/// lays out with its region, which is the correct answer for author-written
/// copy and the thing `dir="auto"` must not do to it.
let private literalHeading (id: string) (text: string) : Node<unit> =
    Fuaran.heading
        id
        { Defaults.heading with
            Level = 3
            Text = TextSource.Literal text }

let private textRun (regionLabel: string) : ReactElement =
    Html.section
        [ prop.className "bidi-rtl-textrun"
          prop.custom ("data-region", regionLabel)
          prop.children
              [ Html.h2
                    [ prop.className "bidi-rtl-label"
                      prop.text ("Bound text run — " + regionLabel) ]
                Render.render renderCtx (boundHeading ("bound-ar-" + regionLabel) "مرحبا بالعالم")
                Render.render renderCtx (boundHeading ("bound-en-" + regionLabel) "Hello, world")
                Render.render renderCtx (boundHeading ("bound-mixed-" + regionLabel) "الإيرادات: 1,240 USD")
                Render.render renderCtx (literalHeading ("literal-" + regionLabel) "Authored control line") ] ]

let view () : ReactElement =
    React.Fragment
        [ Render.themeStyleElement Defaults.theme
          Html.div
              [ prop.id "bidi-rtl-page"
                prop.className "bidi-rtl-page"
                prop.children
                    [ Html.h1 [ prop.text "Right-to-left and bidi" ]
                      Html.p
                          [ prop.text
                                "Phase 1114 harness. The reference stylesheet's inline-axis rules are logical, so the RTL region below mirrors with no override block; the LTR island inside it stays left-to-right, which an override block could not have achieved; and the runtime-bound headings resolve their own direction through dir=\"auto\"." ]

                      // ── 1. the RTL region ──
                      Html.div
                          [ prop.id "bidi-rtl-region"
                            prop.className "bidi-rtl-region"
                            prop.custom ("dir", "rtl")
                            prop.custom ("lang", "ar")
                            prop.children
                                [ Html.h2 [ prop.text "منطقة من اليمين إلى اليسار" ]
                                  yield! samples mirroredIds
                                  textRun "rtl"

                                  // ── 2. the LTR island, nested ──
                                  Html.div
                                      [ prop.id "bidi-ltr-island"
                                        prop.className "bidi-ltr-island"
                                        prop.custom ("dir", "ltr")
                                        prop.custom ("lang", "en")
                                        prop.children
                                            [ Html.h2 [ prop.text "LTR island — tabular data" ]
                                              yield! samples ltrIslandIds ] ] ] ]

                      // ── 3. the same surfaces left-to-right, for comparison ──
                      Html.div
                          [ prop.id "bidi-ltr-region"
                            prop.className "bidi-ltr-region"
                            prop.custom ("dir", "ltr")
                            prop.custom ("lang", "en")
                            prop.children
                                [ Html.h2 [ prop.text "Left-to-right region" ]
                                  yield! samples mirroredIds
                                  textRun "ltr" ] ] ] ] ]
