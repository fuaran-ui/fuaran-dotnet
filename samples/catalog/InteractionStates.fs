module Fuaran.Samples.Catalog.InteractionStates

// ============================================================================
//  Interaction-state token-surface snapshot harness target.
//
//  Renders a fixed grid of (button-variant × interaction-state) cells the
//  Playwright spec at snapshot/interaction-state.spec.mts probes for
//  consumer-bridge override propagation. Each cell carries a stable
//  data-testid so the spec selects deterministically, independent of
//  surrounding DOM structure.
//
//  Two render modes:
//   - bridge = false (default) — baseline. The reference CSS + Theme
//     `<style>` populate `:root` with the declared values;
//     `getComputedStyle` returns the fallback for every probe.
//   - bridge = true — appends `<style>{bridgeStylesheet}</style>` AFTER
//     `Render.themeStyleElement` (handled by Main.fs) so the consumer-
//     bridge `:root` block wins the cascade. Every overridden variable
//     resolves to the bridge value.
//
//  Coverage. The reference CSS actively consumes ~16 tone-state
//  variables across .fuaran-button-{primary|secondary|tertiary|destructive}
//  hover/focus/active/disabled plus the four `--fuaran-focus-ring-*`
//  globals. The remaining ~68 tone-state-slot variables form the
//  authoring surface for consumer-side scoped rules (e.g. a consumer
//  who writes `.fuaran-tone-success .fuaran-button-tertiary:hover`) — they
//  are declared by Theme.toCss + the reference CSS :root block. The
//  spec's "declares-every-variable" assertion checks the full 88-var
//  surface; the per-button override-propagation assertions exercise the
//  subset the reference CSS reads.
// ============================================================================

open Feliz

/// Consumer-bridge stylesheet. Each value is a deliberately-distinct
/// `rgb()` so the spec's per-property assertions can diff trivially against
/// the reference fallback. Mirrors the "Raw CSS bridge" worked example in
/// `Fuaran/docs/migrations/12-N-interaction-state-tokens.md` (Example 3) but
/// covers the full reference-consumed surface. The Playwright spec
/// (`snapshot/interaction-state.spec.mts`) duplicates these values — keep
/// them in sync; the bridge `<style>` element carries the
/// `data-testid="bridge-stylesheet"` so the spec can also assert the bridge
/// was injected before probing.
let bridgeStylesheet =
    """:root {
  --fuaran-tone-brand-hover-fg: rgb(11, 22, 33);
  --fuaran-tone-brand-hover-bg: rgb(44, 55, 66);
  --fuaran-tone-default-hover-bg: rgb(77, 88, 99);
  --fuaran-tone-default-hover-border: rgb(108, 119, 130);
  --fuaran-tone-critical-hover-fg: rgb(141, 152, 163);
  --fuaran-tone-brand-active-fg: rgb(174, 185, 196);
  --fuaran-tone-brand-active-bg: rgb(207, 218, 229);
  --fuaran-tone-default-active-bg: rgb(240, 251, 6);
  --fuaran-tone-default-active-border: rgb(17, 28, 39);
  --fuaran-tone-critical-active-fg: rgb(50, 61, 72);
  --fuaran-tone-brand-disabled-fg: rgb(83, 94, 105);
  --fuaran-tone-default-disabled-bg: rgb(116, 127, 138);
  --fuaran-tone-default-disabled-fg: rgb(149, 160, 171);
  --fuaran-tone-default-disabled-border: rgb(182, 193, 204);
  --fuaran-tone-critical-disabled-fg: rgb(215, 226, 237);
  --fuaran-focus-ring-color: rgb(12, 24, 36);
}"""

let private buttonCell (testId: string) (variant: string) (label: string) (isDisabled: bool) : ReactElement =
    Html.button
        [ prop.custom ("data-testid", testId)
          prop.className ("fuaran-button fuaran-button-" + variant)
          prop.disabled isDisabled
          prop.text label ]

/// Render the fixture. `bridge=true` appends a consumer-bridge `<style>` so
/// the spec can probe both modes from the same page module.
let view (bridge: bool) : ReactElement =
    let bridgeNode =
        if bridge then
            Html.style [ prop.custom ("data-testid", "bridge-stylesheet"); prop.text bridgeStylesheet ]
        else
            Html.none

    React.Fragment
        [ bridgeNode
          Html.div
              [ prop.id "interaction-state-page"
                prop.style [ style.padding 24 ]
                prop.children
                    [ Html.h1 [ prop.text "Interaction-state token surface" ]
                      Html.p
                          [ prop.text
                                "Snapshot harness fixture. Each button carries a data-testid; the Playwright spec hovers / focuses / activates / disables and reads getComputedStyle to verify --fuaran-tone-{tone}-{state}-{slot} overrides propagate." ]
                      // Interactive buttons — one per variant. The same node serves the
                      // hover, focus, and active probes since pseudo-states change at
                      // interaction time.
                      Html.div
                          [ prop.className "interaction-row"
                            prop.style [ style.display.flex; style.padding 12 ]
                            prop.children
                                [ buttonCell "btn-primary" "primary" "Primary" false
                                  buttonCell "btn-secondary" "secondary" "Secondary" false
                                  buttonCell "btn-tertiary" "tertiary" "Tertiary" false
                                  buttonCell "btn-destructive" "destructive" "Destructive" false ] ]
                      // Disabled mirrors — same variant set with `disabled` so :disabled
                      // applies without interaction.
                      Html.div
                          [ prop.className "interaction-row"
                            prop.style [ style.display.flex; style.padding 12 ]
                            prop.children
                                [ buttonCell "btn-primary-disabled" "primary" "Primary" true
                                  buttonCell "btn-secondary-disabled" "secondary" "Secondary" true
                                  buttonCell "btn-tertiary-disabled" "tertiary" "Tertiary" true
                                  buttonCell "btn-destructive-disabled" "destructive" "Destructive" true ] ]
                      // Text input for the focus-ring outline-color assertion (the
                      // input surface consumes `--fuaran-focus-ring-color` via
                      // `outline: var(--fuaran-focus-ring-width) var(--fuaran-focus-ring-style)
                      //   var(--fuaran-focus-ring-color)`).
                      Html.input
                          [ prop.custom ("data-testid", "ring-input")
                            prop.className "fuaran-form-input"
                            prop.type' "text"
                            prop.placeholder "Focus to verify ring colour" ] ] ] ]
