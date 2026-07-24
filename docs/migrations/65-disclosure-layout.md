# Phase 65 migration – `LayoutKind.Disclosure` accordion / collapsible primitive

**Shipped:** 2026-05-28
**Scope:** `Fuaran.UI` typed contract + `Fuaran.UI` smart-ctor + `Fuaran.UI.Renderer` runtime + reference CSS + `Fuaran.UI.Ops.JsonDecode` decoder + `Fuaran.UI.OpStream.Abstractions.CanonicalJson` encoder + `Fuaran.UI.PreEmitValidate` walker + `samples/catalog/` axis + Playwright snapshot.
**Stability impact:** Additive across every surface. No reordering, renaming, or signature changes to existing `LayoutKind` cases, smart-ctor entry points, decoder branches, validator codes, or encoder arms. Pre-Phase-65 consumers see no behavioural change.

## What changes

### 1. `LayoutKind.Disclosure` + `DisclosureSpec<'Msg>`

`Fuaran.UI/Types.fs` adds (additive DU case + additive spec record):

```fsharp
and [<RequireQualifiedAccess>] LayoutKind<'Msg> =
    | ...
    | StatList of StatListSpec<'Msg>
    | Disclosure of DisclosureSpec<'Msg>          // NEW

and DisclosureSpec<'Msg> =
    { Heading: TextSource
      Open: Binding<bool>
      OnToggle: bool -> Action<'Msg>
      Children: Node<'Msg> list
      DefaultOpen: bool }
```

The renderer emits HTML-native `<details>` / `<summary>` so the open/closed toggle works without React state. `Open` overlays controlled-mode semantics (when the binding resolves, its value drives the element's `open` attribute via React's `prop.isOpen`); `DefaultOpen` seeds the initial mount before the binding resolves.

### 2. `Fuaran.disclosure` smart-ctor

```fsharp
let disclosure (id: string) (spec: DisclosureSpec<'Msg>) : Node<'Msg>
```

Mirrors `Fuaran.card` / `Fuaran.statList`. `Defaults.disclosure<'Msg>` ships an `emptyLiteral` Heading, a `Binding.Static false` Open, a no-op `Action.Chain []` OnToggle, an empty Children list, and `DefaultOpen = false`. `Defaults.Accessibility.disclosure` is `Some { Role = Some AriaRole.Region }` – same shape as Card / StatList.

### 3. Renderer arm

`Fuaran.UI.Renderer.Render.renderLayout` grows a `LayoutKind.Disclosure` arm:

- Resolves `spec.Open` via `BindingResolver.tryResolve`; falls back to `spec.DefaultOpen` when unresolved.
- Sets `prop.isOpen` (React's controlled `open` prop) to the resolved value.
- Sets `defaultOpen` via `prop.custom ("defaultOpen", ...)` for the uncontrolled-mode initial render.
- Attaches `prop.custom ("onToggle", ...)` – the renderer reads the new `open` attribute off the target HTMLElement and dispatches `spec.OnToggle isOpen`.
- Renders the `Heading` inside `<summary class="fuaran-disclosure-summary">` and the children inside `<div class="fuaran-disclosure-body">`.

Native HTML5 `<details>` already exposes `aria-expanded` through the accessibility tree – no explicit `aria-expanded` emission needed. The Node-level Region role from `Defaults.Accessibility.disclosure` still applies via the outer `render` wrapper.

### 4. JsonDecode forward-coupling

`decodeLayoutKind` learns the `"Disclosure"` discriminator. The wire shape:

```json
{
  "kind": "Layout",
  "id": "additional-entitlements",
  "spec": {
    "$type": "Disclosure",
    "spec": {
      "children": [ ... ],
      "defaultOpen": true,
      "heading": { "$type": "Literal", "value": "Additional entitlements" },
      "open": { "$type": "Static", "value": false }
    }
  }
}
```

Fields are alphabetically ordered to match the canonical-JSON encoder. `OnToggle` is a closure (renderer-side dispatch) and is not encoded – mirrors the `Tabs.OnSelect` precedent; decoded `OnToggle` falls to the no-op `Action.Chain []`. `defaultOpen` defaults to `false` when omitted; `open` defaults to `Binding.Static false`.

### 5. CanonicalJson encoder arm

`Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode` grows a `Disclosure` arm. Field order (alphabetical, canonical): `children`, `defaultOpen`, `heading`, `open`.

### 6. PreEmitValidate walker

`Fuaran.UI.PreEmitValidate.validate` enumerates `Disclosure.Children` when walking the tree. No new defect codes – disclosure inherits the standard `NodeId` uniqueness + non-emptiness checks.

### 7. Reference CSS

`Fuaran.UI.Renderer/content/fuaran-reference.css` adds a `.fuaran-layout-disclosure` block plus `.fuaran-disclosure-summary` / `.fuaran-disclosure-body` selectors. Surface tone / border / radius mirror `.fuaran-layout-card` so the disclosure shape integrates with the existing visual contract. New theme token: `--fuaran-disclosure-summary-padding` (defaults to the same `md lg` shape `--fuaran-stat-list-heading` uses – hosts override to tighten the click target without touching the body padding).

The native WebKit/Blink disclosure marker is hidden; the summary draws its own chevron via `::after` that rotates 90° on `[open]`.

## When to use `Disclosure` vs `Card`

- **Choose `Disclosure`** when the section's open/closed state is part of the user's interaction model. Examples: "Show advanced settings" / "Additional individual entitlements" / FAQ entries / sub-form sections that most users don't need to fill in.
- **Choose `Card`** when the content is always visible. Cards group related content; disclosures hide it behind a click.

A `Disclosure` whose `DefaultOpen = true` AND whose `Open = Binding.Static true` is observationally identical to a `Card` (other than the chevron + click affordance). Prefer `Card` for that shape – using `Disclosure` for always-open content signals to AI authors that the section is collapsible, which is misleading.

## Authoring patterns

### Uncontrolled (renderer-side state)

The simplest form – let the HTML-native `<details>` element own the open/closed state. `Open` stays `Binding.Static false`; toggle the section's default via `DefaultOpen`.

```fsharp
Fuaran.disclosure
    "advanced"
    { Defaults.disclosure with
        Heading = TextSource.Literal "Advanced options"
        DefaultOpen = false
        Children = [ ... ] }
```

### Controlled (model-driven state)

When the host's model needs to know whether the disclosure is open (URL deep-linking, server-persisted preferences, sibling components reading the open state), drive `Open` from a `binding.state` and pair with a typed `OnToggle`:

```fsharp
Fuaran.disclosure
    "additional-entitlements"
    { Defaults.disclosure with
        Heading = TextSource.Literal "Additional individual entitlements"
        Open = binding.state "additionalEntitlementsOpen" false
        OnToggle = (fun isOpen -> Action.dispatch (ToggleAdditionalEntitlements isOpen))
        Children = [ ... ] }
```

### Nested disclosures

Multiple disclosures can be open simultaneously – the semantic is distinct from `Tabs` (at most one active panel). Nested-disclosure cases work without aria-attribute collision because `<details>` carries no `aria-controls` link by default.

## Anti-patterns

- **Don't roll a custom React-state toggle.** HTML's native `<details>` / `<summary>` handles the toggle without React state – the Open binding overlays controlled-mode semantics on top, but the uncontrolled fallback is what most authors will reach for.
- **Don't conflate Disclosure with Tabs.** A tabs surface has at most one active panel at a time; multiple disclosures can be open simultaneously. The semantic and aria-attribute shapes are distinct.
- **Don't bypass `aria-expanded`.** Modern browsers derive `aria-expanded` for `<details>` automatically from the `open` attribute. Renderer must not emit a redundant `aria-expanded` – that creates a contract surface authors might come to depend on, and breaks if `<details>` semantics ever require a wrapper element.

## Migration path for existing Feliz code

Existing Feliz code that hand-rolls an accordion with React `useState` + a `<button>` toggle + a `<div>` body:

```fsharp
// Before — hand-rolled Feliz accordion (does NOT round-trip through the AI orchestrator):
let isOpen, setIsOpen = React.useState false
Html.div [
    Html.button [
        prop.onClick (fun _ -> setIsOpen (not isOpen))
        prop.text "Additional details"
    ]
    if isOpen then
        Html.div [ ... ]
    else
        Html.none
]
```

translates to:

```fsharp
// After — Fuaran.disclosure (round-trips through the AI orchestrator):
Fuaran.disclosure
    "additional-details"
    { Defaults.disclosure with
        Heading = TextSource.Literal "Additional details"
        Children = [ ... ] }
```

For controlled-state cases (the model needs to know whether the section is open), pair with `binding.state` + `OnToggle` as in the "Controlled" example above. The host's `update` writes the new open state to the model; the renderer reads it back via the binding on the next frame.

## Walkthrough closure

The pilot app's "Additional individual entitlements" Feliz card translates to `Fuaran.disclosure` as the worked-example re-validation for this phase – tracked under Phase 68's cross-repo work.
