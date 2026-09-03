module Fuaran.UI.Tests.Accessibility

// ============================================================================
//  `Accessibility` trait + per-component defaults +
//  Render.accessibilityAttributes projection.
//
//  Feliz' .NET-side ReactElement is opaque (rendering happens in the browser),
//  so HTML-shape assertions live in the Catalog visual-inspection layer.
//  These tests pin the typed contract instead — the projection from
//  `Accessibility option` to `(attr-name, attr-value) list` that the
//  renderer feeds into `prop.custom`. Same mapping the browser sees,
//  asserted as pure F#.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

type Msg = NoOp

[<Tests>]
let tests =
    testList
        "Accessibility trait + renderer attribute projection"
        [ test "Defaults.Accessibility.button supplies Role = Button" {
              match Defaults.Accessibility.button with
              | Some a -> Expect.equal a.Role (Some AriaRole.Button) "Role defaults to Button"
              | None -> failtest "Expected Some Accessibility for buttons"
          }

          test "Defaults.Accessibility.callout supplies Role = Alert + LiveRegion = Assertive" {
              match Defaults.Accessibility.callout with
              | Some a ->
                  Expect.equal a.Role (Some AriaRole.Alert) "Role defaults to Alert"
                  Expect.equal a.LiveRegion (Some LiveRegionKind.Assertive) "LiveRegion defaults to Assertive"
              | None -> failtest "Expected Some Accessibility for callouts"
          }

          test "Defaults.Accessibility.none is None — decorative defaults skip ARIA" {
              // Accessibility carries `Binding<string> option` / `Binding<bool> option`
              // fields with function-typed payloads — no structural equality.
              // Pattern-match instead of Expect.equal.
              match Defaults.Accessibility.none with
              | None -> ()
              | Some _ -> failtest "Decorative shapes default to None"
          }

          test "Fuaran.button populates Node.Accessibility with the button default" {
              let node: Node<Msg> =
                  Fuaran.button
                      "submit"
                      { Defaults.button<Msg> with
                          Label = TextSource.Literal "Submit" }

              match node.Accessibility with
              | Some a -> Expect.equal a.Role (Some AriaRole.Button) "Button role wired by smart-ctor"
              | None -> failtest "Expected Accessibility populated"
          }

          test "Fuaran.stack populates Node.Accessibility with None (decorative layout)" {
              let node: Node<Msg> =
                  Fuaran.stack
                      "row"
                      { Defaults.stack<Msg> with
                          Children = [] }

              match node.Accessibility with
              | None -> ()
              | Some _ -> failtest "Stack is decorative — no ARIA default"
          }

          test "Node.withAccessibility overrides the smart-ctor's default" {
              let custom =
                  { Defaults.Accessibility.empty with
                      Role = Some(AriaRole.Custom "tooltip")
                      LiveRegion = Some LiveRegionKind.Polite }

              let node: Node<Msg> =
                  Fuaran.button
                      "tip"
                      { Defaults.button<Msg> with
                          Label = TextSource.Literal "?" }
                  |> Node.withAccessibility (Some custom)

              match node.Accessibility with
              | Some a ->
                  Expect.equal a.Role (Some(AriaRole.Custom "tooltip")) "Custom role applied"
                  Expect.equal a.LiveRegion (Some LiveRegionKind.Polite) "Custom LiveRegion applied"
              | None -> failtest "Expected Some Accessibility after override"
          }

          test "accessibilityAttributes projects None to []" {
              let attrs = Render.accessibilityAttributes BindingResolver.empty Option.None
              Expect.isEmpty attrs "No Accessibility ⇒ no aria-* attributes"
          }

          test "accessibilityAttributes emits role=\"button\" for AriaRole.Button" {
              let a11y =
                  Some
                      { Defaults.Accessibility.empty with
                          Role = Some AriaRole.Button }

              let attrs = Render.accessibilityAttributes BindingResolver.empty a11y

              Expect.contains attrs ("role", "button") "role=\"button\" attribute emitted"
          }

          test "accessibilityAttributes emits aria-live for LiveRegionKind" {
              let a11y =
                  Some
                      { Defaults.Accessibility.empty with
                          LiveRegion = Some LiveRegionKind.Assertive }

              let attrs = Render.accessibilityAttributes BindingResolver.empty a11y

              Expect.contains attrs ("aria-live", "assertive") "aria-live=\"assertive\" emitted"
          }

          test "accessibilityAttributes resolves Label binding into aria-label" {
              let a11y =
                  Some
                      { Defaults.Accessibility.empty with
                          Label = Some(Binding.Static(Some "Close dialog")) }

              let attrs = Render.accessibilityAttributes BindingResolver.empty a11y

              Expect.contains attrs ("aria-label", "Close dialog") "Static Label resolves to aria-label"
          }

          test "accessibilityAttributes resolves Label via the I18n resolver" {
              let catalog = Map.ofList [ "close", "Close" ]

              let sources =
                  { BindingResolver.empty with
                      I18nResolver = BindingResolver.makeI18nResolver catalog }

              let a11y =
                  Some
                      { Defaults.Accessibility.empty with
                          Label = Some(Binding.I18n("close", Option.None)) }

              let attrs = Render.accessibilityAttributes sources a11y

              Expect.contains attrs ("aria-label", "Close") "I18n-bound Label resolves to aria-label"
          }

          test "accessibilityAttributes omits aria-label when Label resolves to empty string" {
              let a11y =
                  Some
                      { Defaults.Accessibility.empty with
                          Label = Some(Binding.Static(Some "")) }

              let attrs = Render.accessibilityAttributes BindingResolver.empty a11y

              Expect.isFalse
                  (attrs |> List.exists (fun (k, _) -> k = "aria-label"))
                  "Empty Label does NOT emit aria-label (the element's text content supplies its accessible name instead)"
          }

          test "accessibilityAttributes resolves LabelledBy/DescribedBy NodeIds to their string id" {
              let a11y =
                  Some
                      { Defaults.Accessibility.empty with
                          LabelledBy = Some "heading-1"
                          DescribedBy = Some "help-text" }

              let attrs = Render.accessibilityAttributes BindingResolver.empty a11y

              Expect.contains attrs ("aria-labelledby", "heading-1") "aria-labelledby = NodeId text"
              Expect.contains attrs ("aria-describedby", "help-text") "aria-describedby = NodeId text"
          }

          test "accessibilityAttributes emits aria-hidden=\"true\" only when Hidden resolves to true" {
              let a11yTrue =
                  Some
                      { Defaults.Accessibility.empty with
                          Hidden = Some(Binding.Static(Some true)) }

              let attrsTrue = Render.accessibilityAttributes BindingResolver.empty a11yTrue

              Expect.contains attrsTrue ("aria-hidden", "true") "Hidden=true emits aria-hidden"

              let a11yFalse =
                  Some
                      { Defaults.Accessibility.empty with
                          Hidden = Some(Binding.Static(Some false)) }

              let attrsFalse = Render.accessibilityAttributes BindingResolver.empty a11yFalse

              Expect.isFalse
                  (attrsFalse |> List.exists (fun (k, _) -> k = "aria-hidden"))
                  "Hidden=false does not emit aria-hidden (avoids spurious skip)"
          }

          test "accessibilityAttributes handles AriaRole.Custom via the supplied role string" {
              let a11y =
                  Some
                      { Defaults.Accessibility.empty with
                          Role = Some(AriaRole.Custom "combobox") }

              let attrs = Render.accessibilityAttributes BindingResolver.empty a11y

              Expect.contains attrs ("role", "combobox") "Custom role string emitted verbatim"
          }

          // ── Phase 951 — WHERE the projection lands (docs/DECISIONS.md D4) ──
          //
          // The predicate is kind-level and shared by every F# renderer, so it
          // is the one place the placement rule can be asserted as pure F#.
          test "forwardsToSemanticElement admits exactly Link / Button / Image" {
              let forwards (n: Node<Msg>) =
                  Accessibility.forwardsToSemanticElement n.Kind

              Expect.isTrue (forwards (Fuaran.link "lk" "/x" "X")) "Link's body IS the <a>"

              Expect.isTrue
                  (forwards (
                      Fuaran.button
                          "btn"
                          { Defaults.button<Msg> with
                              Label = TextSource.Literal "Go" }
                  ))
                  "Button's body IS the <button>"

              Expect.isTrue
                  (forwards (
                      Fuaran.imageSpec
                          "img"
                          { Defaults.image with
                              Src = Binding.Static(Some "/a.png")
                              Alt = TextSource.Literal "A" }
                  ))
                  "Image's body IS the <img>"

              // A container body — the wrapper keeps the projection.
              Expect.isFalse
                  (forwards (
                      Fuaran.stack
                          "stk"
                          { Defaults.stack<Msg> with
                              Children = [] }
                  ))
                  "a container kind does not forward"

              // The deliberate non-member: a form field's control is not the
              // body root, and its <label> already names it (D4 condition 3).
              Expect.isFalse
                  (forwards (
                      Fuaran.select
                          "sel"
                          { Defaults.select<Msg> with
                              Label = TextSource.Literal "Pick" }
                  ))
                  "a form-field kind does not forward — the <label> already names the control"
          }

          // ── Phase 1114 — the `dir="auto"` policy ──────────────────────────
          //
          // Kind-level and shared by both renderer arms, so this is the one
          // place the slot set can be asserted as pure F#. Read the policy note
          // above `Accessibility.isBidiIsolated` for what decides each answer.

          test "dir=auto is emitted for a display leaf whose text is RUNTIME-bound" {
              let isolated (n: Node<Msg>) = Accessibility.isBidiIsolated n.Kind

              let boundHeading =
                  Fuaran.heading
                      "h"
                      { Defaults.heading with
                          Text = TextSource.Bound(Binding.State("customerName", Some "")) }

              Expect.isTrue (isolated boundHeading) "a bound heading carries data of unknown direction"

              Expect.equal
                  (Accessibility.bidiAttributes boundHeading.Kind Defaults.style)
                  [ "dir", "auto" ]
                  "the attribute pair is exactly dir=auto"
          }

          // ── Phase 1472 — the DECLARED direction ───────────────────────────

          test "a declared direction is emitted, and beats the Phase 1114 heuristic" {
              // `auto` infers from the value's own first strong character, and
              // the declaration exists for exactly the values that inference
              // gets wrong — so the declaration wins, not the other way round.
              let bound =
                  Fuaran.heading
                      "h"
                      { Defaults.heading with
                          Text = TextSource.Bound(Binding.State("reference", Some "")) }

              Expect.equal
                  (Accessibility.bidiAttributes bound.Kind Defaults.style)
                  [ "dir", "auto" ]
                  "undeclared, the heuristic still answers"

              Expect.equal
                  (Accessibility.bidiAttributes
                      bound.Kind
                      { Defaults.style with
                          Direction = TextDirection.Ltr })
                  [ "dir", "ltr" ]
                  "the declaration replaces auto rather than sitting beside it"

              Expect.equal
                  (Accessibility.bidiAttributes
                      bound.Kind
                      { Defaults.style with
                          Direction = TextDirection.Rtl })
                  [ "dir", "rtl" ]
                  "and in the other direction"
          }

          test "a declared direction reaches a kind the heuristic never touches" {
              // A literal is authored in the document's own language, so Phase
              // 1114 says nothing about it. An opaque identifier written as a
              // literal is precisely the case the declaration is for.
              let literal =
                  Fuaran.heading
                      "h"
                      { Defaults.heading with
                          Text = TextSource.Literal "RR123456789IL" }

              Expect.equal (Accessibility.bidiAttributes literal.Kind Defaults.style) [] "silent, undeclared"

              Expect.equal
                  (Accessibility.bidiAttributes
                      literal.Kind
                      { Defaults.style with
                          Direction = TextDirection.Ltr })
                  [ "dir", "ltr" ]
                  "the document says so, and is believed"
          }

          test "the isolation rides a class, and an undeclared node's class is byte-identical" {
              // `dir` states the direction; `.fuaran-dir-*` carries the
              // `unicode-bidi: isolate` that stops the surrounding context
              // reordering the run. Both are markup and CSS only — no script.
              let plain = Theme.className Defaults.style

              Expect.isFalse
                  (plain.Contains "fuaran-dir-")
                  "a tree that declares nothing yields the pre-1472 class string"

              let ltr =
                  Theme.className
                      { Defaults.style with
                          Direction = TextDirection.Ltr }

              Expect.equal ltr (plain + " fuaran-dir-ltr") "appended last, so every earlier fragment is unmoved"

              let rtl =
                  Theme.className
                      { Defaults.style with
                          Direction = TextDirection.Rtl }

              Expect.equal rtl (plain + " fuaran-dir-rtl") "and in the other direction"
          }

          test "dir=auto is NOT emitted for authored or host-translated text" {
              let isolated (n: Node<Msg>) = Accessibility.isBidiIsolated n.Kind

              let literal =
                  Fuaran.heading
                      "h"
                      { Defaults.heading with
                          Text = TextSource.Literal "Revenue" }

              let i18n =
                  Fuaran.heading
                      "h"
                      { Defaults.heading with
                          Text = TextSource.I18n("dashboard.revenue", Map.empty) }

              Expect.isFalse (isolated literal) "a literal is the author writing in the document's language"
              Expect.isFalse (isolated i18n) "an i18n key resolves in the document's own locale"

              Expect.equal
                  (Accessibility.bidiAttributes literal.Kind Defaults.style)
                  []
                  "no attribute at all, not dir=ltr"
          }

          test "dir=auto is NOT emitted on a layout container, even one holding bound text" {
              // `dir` inherits, so `auto` here would resolve ONE direction from
              // the first strong character anywhere beneath and impose it on
              // every child — the opposite of what a mixed-direction page needs.
              let boundHeading =
                  Fuaran.heading
                      "h"
                      { Defaults.heading with
                          Text = TextSource.Bound(Binding.State("name", Some "")) }

              let container =
                  Fuaran.stack
                      "stk"
                      { Defaults.stack<Msg> with
                          Children = [ boundHeading ] }

              Expect.isFalse (Accessibility.isBidiIsolated container.Kind) "a container never isolates for its children"
          }

          test "partitionExtraAttributes splits data-* (wrapper) from aria-* (projection)" {
              let dataHalf, ariaHalf =
                  Accessibility.partitionExtraAttributes [ "aria-current", "page"; "data-hook", "nav"; "data-x", "1" ]

              Expect.equal dataHalf [ "data-hook", "nav"; "data-x", "1" ] "data-* stays with the node address"
              Expect.equal ariaHalf [ "aria-current", "page" ] "aria-* follows the a11y projection"
          }

          test "Existing seed-component tests still pass — Accessibility default is None on stack but Some on button" {
              // Smoke check that the additive change preserved
              // session-2 + session-3a assertions: Fuaran.dashboard still
              // produces a Layout/Dashboard node, etc. Just construct
              // and inspect Kind; Accessibility addition is orthogonal.
              let dashboardNode: Node<Msg> =
                  Fuaran.dashboard
                      "main"
                      { Defaults.dashboard<Msg> with
                          Children = [] }

              match dashboardNode.Kind with
              | NodeKind.Box({ Role = BoxRole.Dashboard }) -> ()
              | other -> failtestf "Expected Layout.Box with Dashboard role, got %A" other

              // Dashboard's default is Main role
              match dashboardNode.Accessibility with
              | Some a -> Expect.equal a.Role (Some AriaRole.Main) "Dashboard defaults to Role=Main"
              | None -> failtest "Expected Accessibility on dashboard"
          } ]
