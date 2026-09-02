module GiraffeSsr.Sample.App

// ============================================================================
//  Fuaran — Giraffe SSR host-integration worked sample (Phase 162).
//
//  A minimal Giraffe app that adopts Fuaran chrome page-by-page:
//   - two crawlable static-SSR routes (`/` and `/pricing`) via `fuaranPage`,
//   - one hydratable route (`/app`) via `fuaranHydratablePage`,
//   - one host Custom-renderer (an inline-SVG sparkline) registered on the
//     server registry (the consumer's domain content embedded in Fuaran chrome
//     via `NodeKind.Custom`),
//   - the render-cache seam wired to an in-memory store.
//
//  Neutral domain, generic "consumer app" framing (OSS boundary).
// ============================================================================

open Feliz.ViewEngine
open Giraffe
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Giraffe

// ─── A host Custom renderer as a typed CONTRACT (Phase 164) ──────────────────
//
// One `CustomContract<'Props>` defines the module/component ids, the typed
// encode/decode, and a content hash DERIVED from the declared shape — so the
// four things that must agree (the tree's prop bag, the server decode, the
// client decode, and the Phase-70 content hash) all flow from a SINGLE value
// instead of being hand-maintained at four sites. `Custom.node` stamps the
// derived hash on every node; `registerContract` records the same hash, so the
// strict bounded-escape verification "just works". The same `sparklineContract`
// value would drive the client `CustomRendererRegistry.RegisterContract` on the
// hydratable route; here we wire the server side.
//
// The registered render fn is still a host trust boundary — it escapes its own
// SVG output (SANITIZATION.md "Custom-renderer trust boundary"); the contract
// types the *input* seam, not the output.

type private Sparkline = { Points: string }

let private encodeSparkline (p: Sparkline) : Map<string, Fuaran.Core.JVal> =
    Map.ofList [ "points", Fuaran.Core.JStr p.Points ]

let private decodeSparkline (bag: Map<string, Fuaran.Core.JVal>) : Result<Sparkline, CustomDecodeError> =
    match Map.tryFind "points" bag with
    | Some(Fuaran.Core.JStr v) -> Ok { Points = v }
    | Some _ -> Error(CustomDecodeError.forKey "points" "expected a string polyline")
    | None -> Error(CustomDecodeError.forKey "points" "missing required 'points' polyline")

let private sparklineContract: CustomContract<Sparkline> =
    CustomContract.create
        "sample"
        "sparkline-svg"
        encodeSparkline
        decodeSparkline
        { Points = "" }
        []
        HashStrictness.StrictReplay

let private sparklineRenderer (p: Sparkline) : ReactElement =
    let svg =
        sprintf
            "<svg viewBox=\"0 0 100 30\" width=\"100\" height=\"30\" role=\"img\" aria-label=\"trend\"><polyline points=\"%s\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"/></svg>"
            p.Points

    Html.div [ prop.className "sample-sparkline"; prop.dangerouslySetInnerHTML svg ]

let private customs =
    Fuaran.UI.Renderer.Server.Registry.empty
    |> Fuaran.UI.Renderer.Server.Registry.registerContract sparklineContract sparklineRenderer

// ─── Trees (Node<obj> — the SSR tier is dispatch-less) ───────────────────────

let private sparkline (id: string) (pts: string) : Node<obj> =
    Custom.node id sparklineContract { Points = pts }

let homeTree: Node<obj> =
    Fuaran.card
        "home"
        { Defaults.card<obj> with
            Heading = Some(TextSource.Literal "Acme — server-rendered with Fuaran")
            Children =
                [ Fuaran.markdown
                      "home-body"
                      "This whole page is a **Fuaran tree** server-rendered as a crawlable document — no client runtime."
                  sparkline "home-spark" "0,30 25,18 50,22 75,8 100,12" ] }

let pricingTree: Node<obj> =
    Fuaran.card
        "pricing"
        { Defaults.card<obj> with
            Heading = Some(TextSource.Literal "Pricing")
            Children =
                [ Fuaran.markdown "pricing-body" "Simple, transparent pricing. One tree, one `fuaranPage` call."
                  sparkline "pricing-spark" "0,28 25,20 50,14 75,10 100,4" ] }

let appTree: Node<obj> =
    Fuaran.card
        "app"
        { Defaults.card<obj> with
            Heading = Some(TextSource.Literal "Interactive (hydratable)")
            Children =
                [ Fuaran.markdown
                      "app-body"
                      "This route ships the hydrate payload — server render for first paint, then `hydrateRoot` for interactivity."
                  sparkline "app-spark" "0,25 25,5 50,18 75,9 100,20" ] }

// An islands page: static SEO content with two small hydration islands. The
// page is inert static HTML except the two `Node.asIsland` subtrees, each of
// which the client mounts independently (Phase 163).
let islandsTree: Node<obj> =
    Fuaran.dashboard
        "islands-page"
        { Defaults.dashboard<obj> with
            Children =
                [ Fuaran.markdown
                      "islands-intro"
                      "# Static article\n\nMost of this page is inert SSR HTML. Two small regions are **islands** — they alone hydrate."
                  Fuaran.card
                      "playback"
                      { Defaults.card<obj> with
                          Heading = Some(TextSource.Literal "Playback (island)")
                          Children = [ Fuaran.markdown "playback-body" "play / pause control" ] }
                  |> Node.asIsland "playback"
                  Fuaran.card
                      "units"
                      { Defaults.card<obj> with
                          Heading = Some(TextSource.Literal "Units (island)")
                          Children = [ Fuaran.markdown "units-body" "metric / imperial toggle" ] }
                  |> Node.asIsland "units" ] }

// ─── Shells (host-authored SEO content) ──────────────────────────────────────

let private shell (title: string) (description: string) (canonical: string) : DocumentShell =
    // Phase 1114 — the document declares its language, and `lang` + `dir` on
    // `<html>` follow from it. The shell no longer hardcodes `lang="en"`, so a
    // host that says nothing gets nothing; this sample says English.
    { (DocumentShell.create title |> DocumentShell.withLocale "en") with
        MetaDescription = Some description
        Canonical = Some canonical
        OpenGraph = [ "og:title", title; "og:type", "website" ]
        Stylesheets = [ "/fuaran-reference.css" ] }

// ─── Options + the Giraffe web app ───────────────────────────────────────────

let options: FuaranGiraffeOptions =
    { FuaranGiraffeOptions.create with
        Customs = customs
        Theme = Some Defaults.theme
        Cache = RenderCache.inMemory () }

let webApp: HttpHandler =
    choose
        [ route "/"
          >=> fuaranPage options (shell "Acme" "Server-rendered with Fuaran." "https://acme.example/") homeTree
          route "/pricing"
          >=> fuaranPage
                  options
                  (shell "Pricing — Acme" "Simple, transparent pricing." "https://acme.example/pricing")
                  pricingTree
          route "/app"
          >=> fuaranHydratablePage
                  options
                  (shell "App — Acme" "The interactive surface." "https://acme.example/app")
                  appTree
          route "/islands"
          >=> fuaranIslandsPage
                  options
                  (shell "Islands — Acme" "Static SSR with two hydration islands." "https://acme.example/islands")
                  islandsTree
          // The body fragment alone, for an HTMX-style swap.
          route "/pricing/fragment" >=> fuaranFragment options pricingTree
          setStatusCode 404 >=> text "Not found" ]
