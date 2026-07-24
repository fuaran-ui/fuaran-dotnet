module Fuaran.UI.Tests.CustomRenderer

// ============================================================================
//  `NodeKind.Custom` runtime-registration contract.
//
//  Three tests:
//    (1) Registration path — `MutableRuntime.RegisterCustomRenderer` makes
//        `IFuaranRuntime.TryRenderCustom` return `Some _`.
//    (2) Fallback path — without registration, `TryRenderCustom` returns
//        `None` and the renderer falls back to the labelled placeholder
//        (asserted via the runtime contract; the placeholder body itself
//        is rendered inside the outer wrapper which carries the
//        `data-fuaran-node-id` attribute per the outer `render` function —
//        not directly testable without DOM).
//    (3) Outer-wrapper metadata — a Node<Msg> with `NodeKind.Custom`
//        carries the same Id field as any other Kind, so the renderer's
//        outer-wrapper emission of `data-fuaran-node-id={id}` applies
//        uniformly. We pin this through the typed Node contract rather
//        than the rendered DOM.
//
//  ExtraAttributes validator coverage lives here too — `Node.withExtraAttribute`
//  rejects non-data-* / non-aria-* keys; tests assert the rejection +
//  retention paths.
//
//  Feliz' .NET-side ReactElement is opaque, so DOM-shape assertions stay
//  out of scope (same constraint as AccessibilityTests / ToneClassEmissionTests).
// ============================================================================

open Expecto
open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.PreEmitValidate
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Runtime

type Msg = NoOp

// Feliz's `.NET-side build of `Html.div [ ... ]` invokes
// `HtmlHelper.createElement` which casts the property list to
// `FSharpList<IReactProperty>` — and `prop.className` returns a value
// whose cast to that target type fails outside the Fable pipeline
// (`InvalidCastException`). The tests below only need a sentinel
// `ReactElement` to pin the Some/None contract of `TryRenderCustom`, so
// `Unchecked.defaultof<ReactElement>` is the safe stand-in (a `null`
// reference on .NET; never inspected by the test assertions).
let private stubElement: ReactElement = Unchecked.defaultof<ReactElement>

[<Tests>]
let tests =
    testList
        "Custom renderer registration + ExtraAttributes validator"
        [ // ── (1) Registration path ─────────────────────────────────────────
          test "MutableRuntime.RegisterCustomRenderer makes TryRenderCustom return Some _" {
              let runtime = MutableRuntime()
              runtime.RegisterCustomRenderer("mymod", "mycomp", (fun _props -> stubElement))

              let r = runtime :> IFuaranRuntime
              let result = r.TryRenderCustom("mymod", "mycomp", Map.empty)
              Expect.isSome result "Registered (moduleId, componentId) should round-trip via TryRenderCustom"
          }

          // ── (2) Fallback path — no registration → None ─────────────────────
          test "Unregistered Custom returns None from TryRenderCustom (renderer falls back to placeholder)" {
              let runtime = MutableRuntime() :> IFuaranRuntime

              let result = runtime.TryRenderCustom("missing", "comp", Map.empty)
              Expect.isNone result "Unregistered Custom must return None so the renderer hits the placeholder"
          }

          // ── (3) DiagnosticRuntime always returns None ──────────────────────
          test "DiagnosticRuntime.TryRenderCustom is always None (no registration surface)" {
              let result = Runtime.diagnostic.TryRenderCustom("any", "any", Map.empty)
              Expect.isNone result "diagnostic runtime is read-only — no Custom dispatch surface"
          }

          // ── (4) Registry isolation — registering on one runtime doesn't
          //     leak to another instance ───────────────────────────────────
          test "MutableRuntime instances have isolated CustomRendererRegistry state" {
              let r1 = MutableRuntime()
              let r2 = MutableRuntime()
              r1.RegisterCustomRenderer("m", "c", (fun _ -> stubElement))

              let asI1 = r1 :> IFuaranRuntime
              let asI2 = r2 :> IFuaranRuntime

              Expect.isSome (asI1.TryRenderCustom("m", "c", Map.empty)) "r1 has the registration"
              Expect.isNone (asI2.TryRenderCustom("m", "c", Map.empty)) "r2 must NOT see r1's registration"
          }

          // ── (5) Placeholder-metadata — Custom Node carries the same Id as
          //     any other Node, so the outer-wrapper data-fuaran-node-id
          //     attribute applies uniformly. Pin via the typed Node ────────
          test "NodeKind.Custom node carries Id field — outer-wrapper data-fuaran-node-id applies" {
              let node: Node<Msg> =
                  { Id = NodeId "custom-host"
                    Kind = NodeKind.Custom("hostmod", "tooltipcard", Map.empty, None, [])
                    State = Defaults.stateBehaviour<Msg>
                    Style = Defaults.style
                    Accessibility = Defaults.Accessibility.none
                    Motion = Defaults.Motion.none
                    ExtraAttributes = None }

              match node.Id with
              | NodeId s ->
                  Expect.equal s "custom-host" "Custom Node Id is preserved (outer wrapper emits data-fuaran-node-id)"
          }

          // ── (6) ExtraAttributes validator — data-* / aria-* accepted ─────
          test "Node.withExtraAttribute accepts data-* keys" {
              let node: Node<Msg> =
                  Fuaran.button
                      "btn"
                      { Defaults.button<Msg> with
                          Label = TextSource.Literal "Go" }
                  |> Node.withExtraAttribute "data-cy" "apply-preset"

              match node.ExtraAttributes with
              | Some attrs -> Expect.equal (Map.tryFind "data-cy" attrs) (Some "apply-preset") "data-cy retained"
              | None -> failtest "Expected ExtraAttributes populated"
          }

          test "Node.withExtraAttribute accepts aria-* keys" {
              let node: Node<Msg> =
                  Fuaran.button
                      "btn"
                      { Defaults.button<Msg> with
                          Label = TextSource.Literal "Go" }
                  |> Node.withExtraAttribute "aria-describedby" "help-text"

              match node.ExtraAttributes with
              | Some attrs ->
                  Expect.equal (Map.tryFind "aria-describedby" attrs) (Some "help-text") "aria-describedby retained"
              | None -> failtest "Expected ExtraAttributes populated"
          }

          // ── (7) ExtraAttributes validator — non-conforming keys rejected ──
          test "Node.withExtraAttribute rejects keys outside data-* / aria-*" {
              let node: Node<Msg> =
                  Fuaran.button
                      "btn"
                      { Defaults.button<Msg> with
                          Label = TextSource.Literal "Go" }
                  |> Node.withExtraAttribute "onclick" "alert(1)"

              // Rejected key → ExtraAttributes stays None (validator emits a
              // warning via eprintfn but doesn't add the entry).
              Expect.isNone node.ExtraAttributes "Non-conforming key must NOT populate ExtraAttributes"
          }

          // ── (8) Defaults.Motion.none is None (no motion class by default) ─
          test "Defaults.Motion.none = None (smart constructors emit no motion class)" {
              Expect.isNone Defaults.Motion.none "Motion defaults to None — no fuaran-motion-* emission"
          }

          test "Node.withMotion populates Node.Motion = Some token" {
              let node: Node<Msg> =
                  Fuaran.metric
                      "rev"
                      { Defaults.metric with
                          Label = TextSource.Literal "Revenue" }
                  |> Node.withMotion Motion.FadeInOnMount

              Expect.equal node.Motion (Some Motion.FadeInOnMount) "Node.Motion populated by withMotion"
          }

          // ─── Bounded-escape registration + hash retrieval ────
          test "Register(...,?contentHash) round-trips through TryGet" {
              let runtime = MutableRuntime()

              let hash =
                  { Algorithm = "SHA256"
                    Hash = "abc123"
                    Strictness = HashStrictness.StrictReplay }

              runtime.RegisterCustomRenderer("mod", "comp", (fun _ -> stubElement), hash)

              match runtime.Registry.TryGet("mod", "comp") with
              | Some(_, Some retrievedHash) ->
                  Expect.equal retrievedHash hash "Registered ContentHash round-trips through TryGet"
              | Some(_, None) -> failtest "Expected Some hash; got None"
              | None -> failtest "Expected Some renderer; got None"
          }

          test "Register without contentHash leaves TryGet's hash slot as None" {
              let runtime = MutableRuntime()
              runtime.RegisterCustomRenderer("mod", "comp", (fun _ -> stubElement))

              match runtime.Registry.TryGet("mod", "comp") with
              | Some(_, hashOpt) -> Expect.isNone hashOpt "No registered hash => TryGet returns (_, None)"
              | None -> failtest "Expected Some renderer; got None"
          }

          // ─── Fuaran.custom smart-ctor populates the additive
          //     fields verbatim ────────────────────────────────────────────
          test "Fuaran.custom populates Custom case with declared contentHash + exposedNodeIds" {
              let hash =
                  Some
                      { Algorithm = "SHA256"
                        Hash = "abc"
                        Strictness = HashStrictness.AdvisoryWarning }

              let exposed = [ NodeId "interior-a"; NodeId "interior-b" ]

              let node: Node<Msg> =
                  Fuaran.custom "be70-id" "my-mod" "MyComp" Map.empty hash exposed

              match node.Kind with
              | NodeKind.Custom(moduleId, componentId, _, h, ids) ->
                  Expect.equal moduleId "my-mod" "moduleId carried"
                  Expect.equal componentId "MyComp" "componentId carried"
                  Expect.equal h hash "contentHash carried"
                  Expect.equal ids exposed "exposedNodeIds carried"
              | _ -> failtest "Expected NodeKind.Custom"
          }

          // ─── PreEmitValidate still enforces non-empty
          //     module / component identifiers on the new shape ────────────
          test "PreEmitValidate.EmptyCustomKindIdentifier still fires on empty moduleId" {
              let bad: Node<Msg> = Fuaran.custom "be70-empty" "" "MyComp" Map.empty None []

              match validate bad with
              | Error defects ->
                  Expect.contains
                      defects
                      (PreEmitDefect.EmptyCustomKindIdentifier("", "MyComp"))
                      "EmptyCustomKindIdentifier still fires on empty moduleId"
              | Ok() -> failtest "Expected validation to fail"
          } ]
