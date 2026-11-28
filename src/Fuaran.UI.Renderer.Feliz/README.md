# Fuaran.UI.Renderer.Feliz

Feliz interop for the Fuaran client renderer.

`Fuaran.UI.Renderer` emits React elements but carries no authoring DSL: every entry point returns the
opaque `RenderedElement`, and nothing in its restore closure is Feliz. A host that authors its own UI in
Feliz takes this package beside the renderer and converts at the edge.

```fsharp
open Feliz
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Feliz

// A rendered tree as a Feliz child.
Html.main [ prop.children [ Render.renderWithSources sources dispatch node |> Rendered.toReact ] ]

// A Custom renderer written in Feliz.
runtime.CustomRendererRegistry.RegisterReact(
    "acme", "Badge", fun props -> Html.span [ prop.className "acme-badge"; prop.text "new" ])
```

| Member | What it does |
|---|---|
| `Rendered.toReact` | A `RenderedElement` as a Feliz `ReactElement` |
| `Rendered.ofReact` | A Feliz `ReactElement` as a `RenderedElement`, to hand back to the renderer |
| `CustomRendererRegistry.RegisterReact` / `RegisterReactInScope` | Register a Custom renderer that returns a Feliz element |
| `CustomRendererRegistry.RegisterReactContract` | Register a typed Custom contract whose render returns a Feliz element |
| `BrowserRuntime.RegisterReactRenderer` / `MutableRuntime.RegisterReactRenderer` | The runtime-level twin of `RegisterReact` |

The conversions are identity at runtime: a `RenderedElement` is a React element. A Custom renderer
written against the plain DOM needs neither Feliz nor this package; register it with
`RegisterDomMount`.

Apache-2.0 licensed.
