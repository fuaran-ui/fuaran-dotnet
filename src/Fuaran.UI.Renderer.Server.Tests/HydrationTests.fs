module Fuaran.UI.Renderer.Server.Tests.HydrationTests

// ============================================================================
//  Isomorphic hydration — server-side embedded-tree emission (Phase 143).
//
//  The server half of hydration: render the body HTML + embed the canonical
//  wire-format tree as a <script type="application/json"> payload the client
//  `hydrate` mount decodes (JsonDecode.decodeNode) and attaches via React
//  hydrateRoot. These tests pin (1) the embedded-script shape, (2) that the
//  embedded wire tree round-trips through JsonDecode to the same tree the
//  server encoded, and (3) the script-injection escaping.
//
//  The client `hydrateRoot` mount + the no-mismatch browser verification are
//  browser-only (see docs/SSR.md "Isomorphic hydration") and are not exercised
//  by this .NET suite.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.Ops

let private contains (needle: string) (haystack: string) =
    haystack.Contains(needle, System.StringComparison.Ordinal)

let private tree: Node<obj> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<obj> with
            Children =
                [ (Fuaran.heading
                      "h"
                      { Defaults.heading with
                          Level = 1
                          Text = TextSource.Literal "Hi" }
                  : Node<obj>)
                  Fuaran.link "lnk" "/about" "About" ] }

[<Tests>]
let hydrationTests =
    testList
        "Isomorphic hydration (server embed)"
        [ test "renderHydratable embeds the wire tree as a keyed <script> payload" {
              let html = Hydration.renderHydratable BindingResolver.empty tree
              // The body HTML is present...
              Expect.isTrue (contains "fuaran-layout-dashboard" html) "the rendered body"
              // ...alongside the embedded-tree script keyed to the root id.
              Expect.isTrue (contains "type=\"application/json\"" html) "json script type"
              Expect.isTrue (contains "id=\"fuaran-hydrate-root\"" html) "script keyed to the root id"
              Expect.isTrue (contains "data-fuaran-hydrate-root=\"root\"" html) "hydrate-root marker"
              Expect.isTrue (contains "\"$type\":\"Box\"" html) "the canonical wire tree is embedded"
          }

          test "the embedded wire tree round-trips through JsonDecode to the same tree" {
              let json = CanonicalJson.encodeNode tree

              match JsonDecode.decodeNodeObj json with
              | Error e -> failtestf "decode failed: %A" e
              | Ok decoded ->
                  let reEncoded = CanonicalJson.encodeNode decoded
                  Expect.equal reEncoded json "decode∘encode is the identity on the embedded tree"
          }

          test "script-injection escaping — a '<' in node data cannot break out of <script>" {
              let evil: Node<obj> = Fuaran.markdown "m" "</script><script>alert(1)</script>"

              let html = Hydration.renderHydratable BindingResolver.empty evil
              // The embedded JSON must not carry a literal closing-script sequence.
              Expect.isFalse (contains "</script><script>alert" html) "no literal break-out sequence"
              Expect.isTrue (contains "\\u003c" html) "the '<' is unicode-escaped in the payload"
          } ]
