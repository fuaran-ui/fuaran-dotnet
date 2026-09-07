module Fuaran.UI.Renderer.Server.Tests.ConditionalVisibilitySsrTests

// ============================================================================
//  Conditional visibility, SERVER side (Fuaran-UI Phase 1535, WIRE_FORMAT.md
//  §3.1 "Conditional presence" obligations 2–5).
//
//  The pure rule is asserted in `Fuaran.UI.Tests/ConditionalVisibilityTests.fs`;
//  what CANNOT be asserted there is that the server renderer actually emits
//  nothing — the Feliz client `ReactElement` is opaque on .NET, so the emitted
//  HTML is only observable on this tier. That is the whole reason this file
//  exists rather than a few more cases beside the pure ones.
//
//  Obligation 5 (server and client agree) is not directly expressible on .NET
//  for the same opacity reason, and this suite gets as close as the platform
//  allows: BOTH renderers take the decision through the one shared
//  `BindingResolver.isNodeVisible`, which the pure suite pins, so what remains
//  to check here is that this tier calls it at all and honours every outcome.
//  A server that silently stopped calling it would pass every pure test.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server

let private nn (v: 'a) : obj = box v |> Unchecked.nonNull

let private sources (state: (string * obj) list) : BindingResolver.BindingSources =
    { BindingResolver.empty with
        State = Map.ofList state }

let private leaf (id: string) (text: string) : Node<obj> =
    { Id = id
      Kind = NodeKind.Markdown({ Text = TextSource.Literal text })
      State = None
      Style = None
      Accessibility = None
      Motion = None
      ExtraAttributes = None
      Tooltip = None
      Visible = None }

/// A parent with three children, the middle one carrying the predicate under
/// test. Siblings on both sides, deliberately: a renderer that dropped the
/// whole container, or that stopped after the hidden child, would pass a
/// single-node test.
let private wrap (middle: Node<obj>) : Node<obj> =
    { leaf "root" "" with
        Kind =
            NodeKind.Box
                { Layout = BoxLayout.Flex(Orientation.Vertical, false, None)
                  Role = BoxRole.Group
                  Heading = None
                  Children = [ leaf "before" "BEFORE"; middle; leaf "after" "AFTER" ]
                  KeepTogether = false
                  BreakBefore = false } }

let private withVisible (b: Binding<bool>) : Node<obj> =
    { leaf "subject" "SUBJECT" with
        Visible = Some b }

let private contains (needle: string) (haystack: string) =
    haystack.Contains(needle, System.StringComparison.Ordinal)

[<Tests>]
let tests =
    testList
        "Phase 1535 — SSR emits nothing for a hidden node"
        [ test "a resolved FALSE emits no element, no id, no placeholder and no aria-hidden" {
              let html =
                  Render.render (sources [ "shown", nn false ]) (wrap (withVisible (Binding.State("shown", None))))

              Expect.isFalse (contains "SUBJECT" html) "the text is gone"
              Expect.isFalse (contains "subject" html) "and so is the node id — no placeholder carries it"
              Expect.isFalse (contains "aria-hidden" html) "removal is not concealment"

              Expect.isTrue (contains "BEFORE" html && contains "AFTER" html) "and the siblings are untouched"
          }

          test "a resolved TRUE emits the node exactly as an unconditional one" {
              // Byte equality against the SAME node with no predicate at all:
              // obligation 3 says the slot changes presence and never
              // appearance, and this is that claim in its strongest form. A
              // renderer that emitted a marker class or a wrapper for a
              // conditionally-visible node would fail here rather than pass
              // quietly.
              let shown =
                  Render.render (sources [ "shown", nn true ]) (wrap (withVisible (Binding.State("shown", None))))

              let unconditional =
                  Render.render BindingResolver.empty (wrap (leaf "subject" "SUBJECT"))

              Expect.equal shown unconditional "presence, never appearance"
          }

          test "an UNRESOLVED predicate emits the node" {
              // A query result the host never furnished. `Binding.State` cannot
              // express this — its own rule resolves a default-less unwritten
              // key to `false` — which is why FUARAN143 exists and why this
              // fixture reaches for `Query`.
              let html =
                  Render.render
                      BindingResolver.empty
                      (wrap (withVisible (Binding.Query("flags.beta", (fun (o: obj) -> unbox<bool> o), None))))

              Expect.isTrue (contains "SUBJECT" html) "a missing source must not hide content"
          }

          test "a hidden node takes its WHOLE SUBTREE with it" {
              let child = leaf "buried" "BURIED"

              let parent =
                  { leaf "panel" "" with
                      Kind =
                          NodeKind.Box
                              { Layout = BoxLayout.Flex(Orientation.Vertical, false, None)
                                Role = BoxRole.Group
                                Heading = None
                                Children = [ child ]
                                KeepTogether = false
                                BreakBefore = false }
                      Visible = Some(Binding.State("shown", None)) }

              let html = Render.render (sources [ "shown", nn false ]) (wrap parent)

              Expect.isFalse (contains "BURIED" html) "the subtree goes too"
              Expect.isTrue (contains "BEFORE" html && contains "AFTER" html) "the siblings do not"
          }

          test "a computed predicate reaches the server through the scalar path" {
              // The wire-neutral half, on this tier: a `Transform` yielding one
              // bool cell. Both directions asserted from ONE pipeline shape, so
              // a server that ignored the predicate entirely fails the second
              // case rather than passing both.
              let overThreshold (threshold: int) : Binding<bool> =
                  let source =
                      Fuaran.Core.Embedded
                          { Schema = [ "id", Fuaran.Core.StringType ]
                            Columns =
                              [ Fuaran.Core.Column.create
                                    "id"
                                    Fuaran.Core.StringType
                                    [ Fuaran.Core.Str "a"; Fuaran.Core.Str "b"; Fuaran.Core.Str "c" ] ] }

                  Binding.Transform(
                      TransformSource.Data source,
                      [ Fuaran.Core.GroupBy(
                            [],
                            [ { Name = "n"
                                Fn = Fuaran.Core.AggFn.Count
                                Of = "id" } ]
                        )
                        Fuaran.Core.Derive(
                            "over",
                            Fuaran.Core.Binary(
                                Fuaran.Core.Gt,
                                Fuaran.Core.Col "n",
                                Fuaran.Core.Lit(Fuaran.Core.Int threshold)
                            )
                        )
                        Fuaran.Core.Project [ "over", "over" ] ],
                      None
                  )

              Expect.isTrue
                  (contains "SUBJECT" (Render.render BindingResolver.empty (wrap (withVisible (overThreshold 2)))))
                  "3 > 2 — rendered"

              Expect.isFalse
                  (contains "SUBJECT" (Render.render BindingResolver.empty (wrap (withVisible (overThreshold 9)))))
                  "3 is not > 9 — removed"
          }

          test "a computed Switch selector selects the matching case on the server" {
              // The other half of the wire-neutral fix, and the fixture the
              // phase's acceptance names: before 1535 this rendered `Default` on
              // both F# renderers. The `Default` is asserted absent as well as
              // the case present — a server that rendered both would satisfy a
              // contains-check on the case alone.
              let source =
                  Fuaran.Core.Embedded
                      { Schema = [ "id", Fuaran.Core.StringType ]
                        Columns =
                          [ Fuaran.Core.Column.create
                                "id"
                                Fuaran.Core.StringType
                                [ Fuaran.Core.Str "a"
                                  Fuaran.Core.Str "b"
                                  Fuaran.Core.Str "c"
                                  Fuaran.Core.Str "d" ] ] }

              let selector: Binding<string> =
                  Binding.Transform(
                      TransformSource.Data source,
                      [ Fuaran.Core.GroupBy(
                            [],
                            [ { Name = "n"
                                Fn = Fuaran.Core.AggFn.Count
                                Of = "id" } ]
                        )
                        Fuaran.Core.Derive(
                            "label",
                            Fuaran.Core.Case(
                                [ Fuaran.Core.Binary(
                                      Fuaran.Core.Gt,
                                      Fuaran.Core.Col "n",
                                      Fuaran.Core.Lit(Fuaran.Core.Int 3)
                                  ),
                                  Fuaran.Core.Lit(Fuaran.Core.Str "busy") ],
                                Fuaran.Core.Lit(Fuaran.Core.Str "quiet")
                            )
                        )
                        Fuaran.Core.Project [ "label", "label" ] ],
                      None
                  )

              let sw =
                  { leaf "sw" "" with
                      Kind =
                          NodeKind.Switch
                              { Defaults.switch with
                                  On = selector
                                  Cases =
                                      [ { Match = Some "busy"
                                          When = None
                                          Child = leaf "busy-case" "BUSY" } ]
                                  Default = leaf "sw-default" "FELL THROUGH" } }

              let html = Render.render BindingResolver.empty (wrap sw)

              Expect.isTrue (contains "BUSY" html) "the computed selector matched"
              Expect.isFalse (contains "FELL THROUGH" html) "and the default did not render"
          }

          test "a `when` case is selected on the server, and falls through when it declines" {
              let sw (flag: bool) =
                  { leaf "sw" "" with
                      Kind =
                          NodeKind.Switch
                              { Defaults.switch with
                                  On = Binding.State("", None)
                                  Cases =
                                      [ { Match = None
                                          When = Some(Binding.State("ready", None))
                                          Child = leaf "ready-case" "READY" } ]
                                  Default = leaf "sw-default" "NOT READY" } }
                  |> wrap
                  |> Render.render (sources [ "ready", nn flag ])

              Expect.isTrue (contains "READY" (sw true)) "a resolved true takes the predicate case"
              Expect.isTrue (contains "NOT READY" (sw false)) "a resolved false falls through to the default"
          } ]
