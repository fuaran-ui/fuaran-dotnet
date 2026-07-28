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
                  "Empty Label does NOT emit aria-label (renderer falls back to structural label)"
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
