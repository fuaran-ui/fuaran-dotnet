module Fuaran.UI.Renderer.Server.Tests.IslandsTests

// ============================================================================
//  Islands (partial hydration, Phase 163) — server-emission + parity tests.
//
//  The mismatch-freedom guarantee is checked here without a browser: a server
//  island wrapper's *inner* HTML is byte-identical to the standalone static
//  render of the island node (marker stripped), which is exactly what the
//  client `hydrateRoot`s into the wrapper — so React hydration cannot mismatch.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server

let private contains (needle: string) (haystack: string) =
    haystack.Contains(needle, System.StringComparison.Ordinal)

let private sources = BindingResolver.empty

// A page: a static heading + two islands (a card + a markdown).
let private island1: Node<obj> =
    Fuaran.card
        "i1"
        { Defaults.card<obj> with
            Heading = Some(TextSource.Literal "Player")
            Children = [ Fuaran.markdown "p1" "play/pause" ] }
    |> Node.asIsland "player"

let private island2: Node<obj> =
    Fuaran.markdown "i2" "metric/imperial" |> Node.asIsland "units"

let private page: Node<obj> =
    Fuaran.dashboard
        "page"
        { Defaults.dashboard<obj> with
            Children = [ Fuaran.markdown "static" "# Static heading"; island1; island2 ] }

let private noIslandPage: Node<obj> =
    Fuaran.dashboard
        "page2"
        { Defaults.dashboard<obj> with
            Children = [ Fuaran.markdown "a" "alpha"; Fuaran.markdown "b" "beta" ] }

[<Tests>]
let tests =
    testList
        "Islands (Phase 163)"
        [ test "asIsland / islandId / stripIsland round-trip (render-time-only marker)" {
              let n = Fuaran.markdown "x" "y" |> Node.asIsland "iso"
              Expect.equal (Node.islandId n) (Some "iso") "islandId reads the marker"
              Expect.equal (Node.islandId (Node.stripIsland n)) None "stripIsland removes it"
              Expect.equal (Node.islandId (Fuaran.markdown "x" "y")) None "an unmarked node has no island id"
          }

          test "a page with zero islands renders byte-identically to plain render (no wrappers, no scripts)" {
              let withIslands = Hydration.renderWithIslands sources noIslandPage
              let plain = Render.render sources noIslandPage
              Expect.equal withIslands plain "zero islands ⇒ renderWithIslands ≡ render"
              Expect.isFalse (contains "data-fuaran-island" withIslands) "no island boundary marker"
              Expect.isFalse (contains "fuaran-hydrate-island" withIslands) "no hydrate script"
          }

          test "each island gets a boundary wrapper + exactly one embedded hydrate script" {
              let html = Hydration.renderWithIslands sources page
              Expect.stringContains html "data-fuaran-island=\"player\"" "island 1 boundary marker"
              Expect.stringContains html "data-fuaran-island=\"units\"" "island 2 boundary marker"
              Expect.stringContains html "id=\"fuaran-hydrate-island-player\"" "island 1 hydrate script"
              Expect.stringContains html "id=\"fuaran-hydrate-island-units\"" "island 2 hydrate script"

              let scriptCount =
                  (html.Split([| "application/json" |], System.StringSplitOptions.None).Length)
                  - 1

              Expect.equal scriptCount 2 "exactly two embedded island payloads"
          }

          test "the island marker is on the boundary wrapper, NOT the node's own element" {
              let html = Hydration.renderWithIslands sources page
              Expect.stringContains html "data-fuaran-node-id=\"i1\"" "the island node element is present"
              // The marker rides only on the `fuaran-island` boundary wrapper.
              Expect.stringContains html "class=\"fuaran-island\" data-fuaran-island=\"player\"" "marker on the wrapper"
              // The node's own static render (what's inside the wrapper) is marker-free.
              let inner = Render.render sources (Node.stripIsland island1)
              Expect.isFalse (contains "data-fuaran-island" inner) "the inner node element carries no marker"
          }

          test
              "mismatch-freedom: the boundary wrapper's inner HTML is the island node's plain static render (marker stripped)" {
              let html = Hydration.renderWithIslands sources page
              // What the client hydrateRoots into each wrapper:
              let island1Inner = Render.render sources (Node.stripIsland island1)
              let island2Inner = Render.render sources (Node.stripIsland island2)
              // Each appears verbatim inside the page — so the hydrated container's
              // children are byte-identical to the client render ⇒ no React mismatch.
              Expect.stringContains html island1Inner "island 1 inner render appears verbatim"
              Expect.stringContains html island2Inner "island 2 inner render appears verbatim"
          }

          test "the static (non-island) remainder matches the islands-free render of that region" {
              // The static heading renders identically whether or not the page has islands.
              let html = Hydration.renderWithIslands sources page

              let staticHeading =
                  Render.render sources (Fuaran.markdown "static" "# Static heading")

              Expect.stringContains html staticHeading "the non-island remainder is untouched"
          } ]
