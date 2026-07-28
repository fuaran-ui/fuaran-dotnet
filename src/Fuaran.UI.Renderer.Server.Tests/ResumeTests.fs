module Fuaran.UI.Renderer.Server.Tests.ResumeTests

// ============================================================================
//  Zero-hydration resumability — server resume-envelope emission (Phase 177).
//
//  The server half of the resumability spike: render inert HTML + embed a flat
//  `nodeId → { action, disposition }` envelope so the client interpreter can
//  resume the Elmish loop on first interaction with ≈ 0 framework JS at load.
//  These tests pin (1) the envelope's shape + valid-JSON-ness, (2) the
//  per-node disposition classifier, (3) the init-effect classifier, (4) the
//  tree-hash determinism (resume-mismatch detection), and (5) the
//  script-injection escaping. The client `install` mount is browser-only and
//  not exercised by this .NET suite.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server
open Fuaran.UI.Renderer.Server.Resume
open Fuaran.UI.OpStream.Abstractions

let private contains (needle: string) (haystack: string) =
    haystack.Contains(needle, System.StringComparison.Ordinal)

/// A dashboard with one Navigate button (interpret) + one Dispatch form (boot).
let private tree: Node<obj> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<obj> with
            Children =
                [ (Fuaran.button
                      "btn"
                      { Defaults.button<obj> with
                          Label = TextSource.Literal "Go"
                          OnClick = Action.Navigate "/about" }
                  : Node<obj>)
                  Fuaran.form
                      "frm"
                      { Defaults.form<obj> with
                          OnSubmit = Action.Dispatch(box "save") } ] }

[<Tests>]
let resumeTests =
    testList
        "Zero-hydration resumability (server envelope)"
        [ test "renderResumable emits inert body + a keyed resume envelope <script>" {
              let html = Resume.renderResumable BindingResolver.empty "mod.sample" "" [] tree
              // The inert server body is present (crawlable, no-JS HTML)...
              Expect.isTrue (contains "fuaran-layout-dashboard" html) "the rendered body"
              // ...alongside the resume envelope keyed to the root id.
              Expect.isTrue (contains "type=\"application/json\"" html) "json script type"
              Expect.isTrue (contains "id=\"fuaran-resume-root\"" html) "envelope keyed to the root id"
              Expect.isTrue (contains "data-fuaran-resume-root=\"root\"" html) "resume-root marker"
              Expect.isTrue (contains "data-fuaran-resume-hash=" html) "tree-hash marker for mismatch detection"
          }

          test "the envelope is valid JSON carrying the flat action map + dispositions" {
              let json = Resume.encodeEnvelope "mod.sample" "" [] tree
              // Parses as JSON (no malformed escaping / trailing commas).
              use doc = System.Text.Json.JsonDocument.Parse json
              let root = doc.RootElement
              Expect.equal (root.GetProperty("moduleId").GetString()) "mod.sample" "module id"
              let actions = root.GetProperty "actions"
              // The Navigate button interprets; the Dispatch form boots.
              Expect.equal
                  (actions.GetProperty("btn").GetProperty("disposition").GetString())
                  "interpret"
                  "Navigate button → interpret"

              Expect.equal
                  (actions.GetProperty("frm").GetProperty("disposition").GetString())
                  "boot"
                  "Dispatch form → boot"

              Expect.equal
                  (actions.GetProperty("btn").GetProperty("action").GetProperty("$type").GetString())
                  "Navigate"
                  "the button's action is the wire-shaped Navigate"

              Expect.equal
                  (actions.GetProperty("btn").GetProperty("action").GetProperty("route").GetString())
                  "/about"
                  "the navigate route round-trips"
          }

          test "disposition classifier — strictest member wins in a Chain" {
              Expect.equal (disposition (Action.Navigate "/x")) ResumeDisposition.Interpret "Navigate"
              Expect.equal (disposition (Action.Dispatch(box "m"))) ResumeDisposition.Boot "Dispatch"

              Expect.equal (disposition (Action.Call("/api", Some id, None))) ResumeDisposition.Fallback "Call"

              Expect.equal
                  (disposition (Action.Chain [ Action.Navigate "/x"; Action.Notify("c", JStr "p") ]))
                  ResumeDisposition.Interpret
                  "all-data Chain interprets"

              Expect.equal
                  (disposition (Action.Chain [ Action.Navigate "/x"; Action.Dispatch(box "m") ]))
                  ResumeDisposition.Boot
                  "a Dispatch in the Chain forces boot"

              Expect.equal
                  (disposition (Action.Chain [ Action.Navigate "/x"; Action.Call("/api", Some id, None) ]))
                  ResumeDisposition.Fallback
                  "a Call in the Chain forces fallback"
          }

          test "init-effect classifier maps each residual effect to its handling (§4)" {
              Expect.equal (classifyInitEffect InitEffectInput.DataLoad) InitEffectClass.Skip "SSR-resolved data → skip"

              Expect.equal
                  (classifyInitEffect InitEffectInput.LiveSubscription)
                  InitEffectClass.Eager
                  "pre-interaction subscription → eager"

              Expect.equal
                  (classifyInitEffect (InitEffectInput.IslandSubscription "isl"))
                  InitEffectClass.Deferred
                  "island-bound subscription → deferred"

              Expect.equal (classifyInitEffect InitEffectInput.Other) InitEffectClass.Lazy "everything else → lazy"
          }

          test "treeHash is deterministic and tree-sensitive (resume-mismatch basis)" {
              Expect.equal (treeHash tree) (treeHash tree) "same tree → same hash (cache-stable)"

              let altered: Node<obj> =
                  Fuaran.dashboard
                      "root"
                      { Defaults.dashboard<obj> with
                          Children =
                              [ Fuaran.button
                                    "btn"
                                    { Defaults.button<obj> with
                                        OnClick = Action.Navigate "/CHANGED" } ] }

              Expect.notEqual (treeHash tree) (treeHash altered) "a changed tree → a changed hash"
          }

          test "init-effect classifications surface in the envelope" {
              let json =
                  Resume.encodeEnvelope
                      "mod.sample"
                      ""
                      [ "orders", InitEffectInput.DataLoad
                        "presence", InitEffectInput.LiveSubscription ]
                      tree

              use doc = System.Text.Json.JsonDocument.Parse json
              let effects = doc.RootElement.GetProperty "initEffects"
              Expect.equal (effects.GetProperty("orders").GetString()) "skip" "data load is skipped"
              Expect.equal (effects.GetProperty("presence").GetString()) "eager" "live subscription is eager"
          }

          test "script-injection escaping — a '</script>' in action data cannot break out" {
              let evil: Node<obj> =
                  Fuaran.button
                      "btn"
                      { Defaults.button<obj> with
                          OnClick = Action.Navigate "/x</script><script>alert(1)</script>" }

              let html = Resume.renderResumable BindingResolver.empty "m" "" [] evil
              Expect.isFalse (contains "</script><script>alert" html) "no literal break-out sequence"
              Expect.isTrue (contains "\\u003c" html) "the '<' is unicode-escaped in the payload"
          }

          test "envelope is smaller than the full hydrate wire tree (load-transfer win, §7)" {
              // The flat action envelope ships only event-bearing nodes; the
              // hydrate path embeds the whole wire tree. (The spike's measured
              // ratio on a representative page — ~0.45× — is recorded in
              // docs/RESUMABILITY-SPIKE.md, before counting the deferred JS bundle.)
              let page: Node<obj> =
                  Fuaran.dashboard
                      "root"
                      { Defaults.dashboard<obj> with
                          Children =
                              [ for i in 1..12 do
                                    Fuaran.button
                                        (sprintf "btn%d" i)
                                        { Defaults.button<obj> with
                                            Label = TextSource.Literal(sprintf "Action %d" i)
                                            OnClick = Action.Navigate(sprintf "/go/%d" i) } ] }

              let envBytes =
                  System.Text.Encoding.UTF8.GetByteCount(Resume.encodeEnvelope "m" "{}" [] page)

              let hydBytes = System.Text.Encoding.UTF8.GetByteCount(CanonicalJson.encodeNode page)
              Expect.isLessThan envBytes hydBytes "the flat action envelope is smaller than the full wire tree"
          }

          test "hard case — a Call handler is marked fallback, never silently dropped" {
              let withCall: Node<obj> =
                  Fuaran.button
                      "btn"
                      { Defaults.button<obj> with
                          OnClick = Action.Call("/api/save", Some id, None) }

              let json = Resume.encodeEnvelope "m" "" [] withCall
              use doc = System.Text.Json.JsonDocument.Parse json

              Expect.equal
                  (doc.RootElement.GetProperty("actions").GetProperty("btn").GetProperty("disposition").GetString())
                  "fallback"
                  "Call degrades to hydration for that subtree only"
          } ]
