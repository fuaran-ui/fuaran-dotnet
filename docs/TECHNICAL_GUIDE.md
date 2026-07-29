# Fuaran technical guide

**Audience:** F# developers writing Fuaran UIs by hand, and AI agents emitting Fuaran fragments. AI agents should consult the §4b type contract first ([`src/Fuaran.UI/Types.fs`](../src/Fuaran.UI/Types.fs)) and the Fuaran design specification.

This guide documents the **language tier** – the typed UI tree, smart constructors, defaults, and the renderer. The AI-orchestration runtime that drives a Fuaran UI from natural language ships as a separate package family and is documented separately (see §7).

**Companion to:**
- [`README.md`](../README.md) – orientation + build invocations.
- [`CLAUDE.md`](../CLAUDE.md) – repo conventions, boundaries, licensing posture.
- [`HOST-STYLING-CHECKLIST.md`](HOST-STYLING-CHECKLIST.md) – variable + class-hook enumeration.
- [`AI_AUTHORING_GUIDE.md`](AI_AUTHORING_GUIDE.md) – the JSON wire shape AI authors emit.

---

## 1. What Fuaran is

Fuaran is an AI-emittable F# UI language. It is purpose-built as a conversational-AI generation target while remaining excellent for human F# authors and preserving Elmish MVU as the canonical state architecture.

The novel parts of Fuaran are in the **type system** – typed bindings to schema, `NodeId` stability across conversation turns, required state-behaviour slots, structural-edit op semantics – not in the surface syntax. Records + lists + pipes is the F# manifestation; flat JSON with `"kind"` tag is the AI manifestation; both project from the same record contracts.

### Two surfaces, one contract

| Surface | Shape | Authored by |
|---|---|---|
| `Fuaran.X "id" { Defaults.X with Label = ... }` | F# records + smart constructors + pipes | Human F# authors |
| `{ "kind": "metric", "id": "...", "label": "...", ... }` | Flat JSON discriminated by `"kind"` | AI generation, replay engines, structural-edit ops |

Both project from the same record contracts in `Fuaran.UI.Types`. AI emits only the fields that differ from `Defaults.X`; the renderer re-applies defaults on JSON ingest. F# authors do the same with `{ Defaults.X with ... }`.

The wedge is **value-density** – Fuaran's record-shaped, NodeId-stable, state-behaviour-required type tree gives the AI authoring surface things Feliz alone does not (typed bindings to schema, structural ops, observable state) – not switching cost. The §4l down-shift portability story keeps the Fuaran↔Feliz interface clean: a Fuaran app mechanically translates to Feliz so consumers are never locked in.

---

## 2. Repository + package layout

```
fuaran-dotnet/
├── Fuaran.sln
├── Build.fs / Build.fsproj                  — FAKE: Format / Build / Test / Pack / All
├── Directory.Build.props                    — TreatWarningsAsErrors=true, latest F#
├── Directory.Packages.props                 — central package management
├── global.json                              — .NET 10 SDK pin
├── nuget.config                             — references ../local-nuget-feed
└── src/
    ├── Fuaran.UI/                            — language: type contract, Defaults, smart ctors
    ├── Fuaran.UI.Renderer/                   — Fable + React + Feliz renderer + reference CSS
    ├── Fuaran.UI.Ops/                        — tree-op apply engine
    ├── Fuaran.UI.AiTools/                    — runtime introspection (fuaran.getNodeState …)
    ├── Fuaran.UI.Validator/                  — build-time F# AST walker
    ├── Fuaran.UI.OpStream.*/                 — op-stream type contract + sinks + replay
    ├── Fuaran.UI.LayoutObserver.*/           — layout-observer interface + observers
    ├── Fuaran.UI.Telemetry.*/                — op-apply telemetry sinks + drift detection
    └── Fuaran.UI.Tests/                      — Expecto test suite
```

### Package roles

| Package | Role | Dependencies |
|---|---|---|
| `Fuaran.UI` | The language. §4b typed record tree, Defaults, smart constructors. | `FSharp.Core` only. |
| `Fuaran.UI.Renderer` | Per-`NodeKind` dispatch to Feliz `ReactElement`. Binding resolution. Reference CSS. | `Fable.Core`, `Feliz`, `Fuaran.UI`. |
| `Fuaran.UI.Ops` | Tree-op apply engine (§4g) – the 10-op vocabulary + `apply` engine. | `Fuaran.UI`. |
| `Fuaran.UI.AiTools` | Runtime introspection surface (`fuaran.getNodeState`, §4i). | `Fuaran.UI`. |
| `Fuaran.UI.Validator` | Build-time F# AST walker (lint rules over author code). | `FSharp.Compiler.Service`. |
| `Fuaran.UI.OpStream.*` | Op-stream type contract + canonical-JSON + hash-chain; in-memory / SQLite sinks; replay. | `Fuaran.UI` (+ sink backends). |
| `Fuaran.UI.LayoutObserver.*` | Flag DU + observer interface; in-memory + browser (ResizeObserver) observers. | `Fuaran.UI`. |
| `Fuaran.UI.Telemetry.*` | Telemetry record types + `IFuaranTelemetrySink`; NoOp / InMemory / Console sinks; drift detection. | `Fuaran.UI`. |

### Standalone posture (mandate)

`Fuaran.UI` itself must remain free of any non-`FSharp.Core` dependency. The §4l down-shift portability story (Fuaran apps mechanically translate to Feliz) requires `Fuaran.UI` be usable standalone. `.Renderer` pulls in Feliz; the AI-orchestration runtime that drives a Fuaran UI is a **separate package family**, not part of the language tier (see §7).

---

## 3. The §4b type contract

The canonical type contract lives at [`src/Fuaran.UI/Types.fs`](../src/Fuaran.UI/Types.fs). It is the **source of truth** for the language. Smart constructors, the renderer, the tree-op apply engine, the AI tool surface, and any future C# / VB.NET authoring shapes all project from these record contracts.

### Shape overview

```fsharp
type Node<'Msg> =
    { Id: NodeId
      Kind: NodeKind<'Msg>
      State: StateBehaviour<'Msg>
      Style: SemanticStyle }
```

Every node carries:
- **`Id : NodeId`** – stable across conversation turns. AI tree-ops address nodes by `NodeId`; the renderer maps `NodeId` to React's `key` so reconciliation survives op-driven mutation.
- **`Kind : NodeKind<'Msg>`** – five-way DU: `Layout` / `Display` / `Input` / `Visualisation` / `Custom`. The `'Msg` parameter flows through `Input` and `Visualisation` so typed Elmish dispatch is preserved.
- **`State : StateBehaviour<'Msg>`** – required slots for loading / empty / error. AI cannot forget them because they're a record field, not a downstream concern.
- **`Style : SemanticStyle`** – semantic intent (`Tone` / `Weight` / `Emphasis`), not pixel-pushing. The renderer maps semantic style to CSS variables; consumer apps wire the palette.

### `NodeKind` cases

```fsharp
and [<RequireQualifiedAccess>] NodeKind<'Msg> =
    | Layout of LayoutKind<'Msg>
    | Display of DisplayKind<'Msg>
    | Input of InputKind<'Msg>
    | Visualisation of VisKind<'Msg>
    | Custom of moduleId: string * componentId: string * props: Map<string, JsonValue>
```

| Branch | Purpose | Cases |
|---|---|---|
| `Layout` | Semantic containers; no Msg surface | `Dashboard` / `Stack` / `GridLayout` / `SplitPanel` / `Tabs` / `Card` / `Stepper` |
| `Display` | Pure presentation, no Msg | `Heading` / `Markdown` / `Metric` / `Badge` / `Sparkline` / `Spacer` / `Callout` / `Progress` / `Skeleton` |
| `Input` | Interactive, dispatches `Action<'Msg>` | `Form` / `Filters` / `Button` / `FileUpload` |
| `Visualisation` | Data-bound, complex | `DataGrid` / `Chart` / `Table` / `Map` |
| `Custom` | Last-resort escape – host-resident component identified by `moduleId` + `componentId` | – |

### Children-as-records (Defect 3 resolution)

Each `LayoutKind` case carries its own spec record with a typed `Children: Node<'Msg> list` field:

```fsharp
and DashboardSpec<'Msg> = { Children: Node<'Msg> list }
and StackSpec<'Msg>     = { Orientation: Orientation; Children: Node<'Msg> list }
and GridLayoutSpec<'Msg> = { Cols: int; Children: Node<'Msg> list }
and SplitPanelSpec<'Msg> = { Weight: float; Children: Node<'Msg> list }
and TabsSpec<'Msg>       = { Orientation: Orientation; Children: Node<'Msg> list }
and CardSpec<'Msg>       = { Heading: TextSource option; Children: Node<'Msg> list }
and StepperSpec<'Msg>    = { ActiveStep: Binding<int>; Children: Node<'Msg> list }
```

Tree-ops apply uniformly – `UpdateProp(id, "Children", ...)` is the same op shape as any other field update. AI never has to learn a special "children are positional" rule.

### `StateBehaviour` – required slots

```fsharp
and StateBehaviour<'Msg> =
    { OnLoading: Node<'Msg> option
      OnEmpty: Node<'Msg> option
      OnError: (ErrorPayload -> Node<'Msg>) option }
```

A node declares its loading / empty / error placeholders inline. The renderer substitutes them based on binding-resolution outcome:
- `NotResolved` → `OnLoading` (the binding hasn't returned data yet).
- The data is present but empty (zero-length seq / empty string / `None`) → `OnEmpty`.
- The binding resolution threw → `OnError errorPayload`.

`ErrorPayload.Kind` includes `BindingResolution` – explicitly distinct from `Server` so observability filters keyed on `Server` don't fire for client-side resolution failures.

The slots are `option` so authors can omit any subset; the renderer's fall-throughs are conservative (placeholder skeleton + `console.warn` rather than a blank node).

Postfix-pipe helpers (`Node.onLoading` / `Node.onEmpty` / `Node.onError`) populate the slots without copying the rest of the node:

```fsharp
Fuaran.metric "totalRevenue" { Defaults.metric with Label = TextSource.Literal "Revenue" ; Source = binding.query "totalRevenue" id }
|> Node.onLoading (Fuaran.skeleton "loading" 1)
|> Node.onEmpty (Fuaran.markdown "empty" "No revenue data yet.")
```

### Bindings – typed at author, stringly-typed at wire

```fsharp
and [<RequireQualifiedAccess>] Binding<'T> =
    | Static of 'T
    | Query of name: string * accessor: (obj -> 'T)
    | Filter of name: string
    | Selection of nodeId: NodeId * accessor: (obj -> 'T)
    | State of key: string * defaultValue: 'T
    | Computed of (BindingContext -> 'T)
```

| Case | Purpose | AI emit |
|---|---|---|
| `Static` | Compile-time constant. | Literal value in JSON. |
| `Query` | Read from a query result registered by name. **Module-scoped** by default (`"totalRevenue"` resolves against the current module's typed API); cross-module reads use the fully-qualified `"ModuleId.name"` form. | Name + path-shaped accessor expression. |
| `Filter` | Read from a currently-active filter value. | Filter name. |
| `Selection` | Read from another node's current selection state (e.g. selected row in a Grid). | NodeId + accessor expression. |
| `State` | Module-level state cell with a fallback default. | Key + default value. |
| `Computed` | F#-only escape hatch; not serialisable. AI never emits these. | – |

The `Query` / `Selection` cases carry obj-erased accessors at the tree level – typed entry points (`binding.query` / `binding.selection`) wrap typed `'result -> 'T` / `'row -> 'T` accessors in obj-erasing closures. Authors never see the obj boundary. See §4 below.

### Actions – effect-typed

```fsharp
and [<RequireQualifiedAccess>] Action<'Msg> =
    | Dispatch of 'Msg
    | Call of ApiEndpoint * onResult: (obj -> 'Msg)
    | Notify of channel: string * payload: JsonValue
    | Navigate of route: string
    | SetState of key: string * value: JsonValue
    | AiTool of toolName: string * args: JsonValue
    | Chain of Action<'Msg> list
```

`Dispatch` is the canonical Elmish entry point. `Chain` is the sequencer (two-step interactions like "set field then fetch"). The other five are typed effects the host's runtime executes – `Call` does HTTP via a registered `ApiEndpoint`, `Notify` publishes through the host's notification channel, `Navigate` routes, `SetState` writes a `binding.state` cell, `AiTool` invokes a named AI tool.

The renderer's session-3a foundation wires `Dispatch` + `Chain` directly; the other five (`Call` / `Notify` / `Navigate` / `SetState` / `AiTool`) route through the `IFuaranRuntime` seam shipped in session 3b. Unwired action kinds are marked `fuaran-button-unwired` with a `title` tooltip and emit a `console.error` on click – a host that hasn't installed a runtime sees those affordances disabled rather than silently no-op.

### Visualisation – typed grid with obj-erasure boundary (Defect 1 resolution)

`GridSpec<'Msg>` at the tree level is row-obj-erased (`Source : Binding<obj seq>`, `RowKey : obj -> string`, `Columns : ColumnErased<'Msg> list`). The author surface is the typed facade `GridSpecOf<'row, 'Msg>` – smart-ctor `Fuaran.grid` boxes the row accessors internally. The renderer trusts the per-Kind type-tag invariant.

Cell formatters (`CellFormat`) are typed enums (`Number` / `Currency` / `Percent` / `SignificantDigits` / `Date` / `Custom`) so a column displaying numbers keeps numeric sort intact – pre-formatted strings break AG Grid's numeric sort, which §4c idiom 3 explicitly forbids.

Cell kinds (`CellKind`) are typed too – `Text` / `Numeric` / `Date` are non-interactive; `Editable` / `Checkbox` / `Button` / `ButtonGroup` / `Link` / `Pill` / `Progress` / `Custom` carry typed action constructors.

### Text – bindable + i18n-aware

```fsharp
and [<RequireQualifiedAccess>] TextSource =
    | Literal of string
    | Bound of Binding<string>
    | I18n of key: string * args: Map<string, JsonValue>
```

`Literal` for compile-time strings. `Bound` for dynamic strings reading from any `Binding`. `I18n` for translation keys; the session-3b renderer ships a catalog resolver (`IFuaranRuntime`-backed). Missing-translation cases stay loud – the renderer emits `[i18n:key]` as a debug-visible placeholder.

---

## 4. Smart constructors

The smart-ctor surface lives at [`src/Fuaran.UI/Fuaran.fs`](../src/Fuaran.UI/Fuaran.fs). It's the **author entry point** – F# authors write through it; AI emits through the JSON projection (session 4+).

### `Fuaran.X` – components

Every `NodeKind` case has a smart constructor named `Fuaran.<kind>`:

```fsharp
Fuaran.dashboard "id" spec    // LayoutKind.Dashboard
Fuaran.stack "id" spec        // LayoutKind.Stack
Fuaran.gridLayout "id" spec   // LayoutKind.GridLayout
Fuaran.splitPanel "id" spec   // LayoutKind.SplitPanel
Fuaran.tabs "id" spec         // LayoutKind.Tabs
Fuaran.card "id" spec         // LayoutKind.Card
Fuaran.stepper "id" spec      // LayoutKind.Stepper

Fuaran.metric "id" spec       // DisplayKind.Metric
Fuaran.markdown "id" body     // DisplayKind.Markdown (positional 80% case)
Fuaran.markdownSpec "id" spec // DisplayKind.Markdown (full record form)
Fuaran.callout "id" spec      // DisplayKind.Callout
Fuaran.progress "id" spec     // DisplayKind.Progress
Fuaran.skeleton "id" rows     // DisplayKind.Skeleton

Fuaran.button "id" spec       // InputKind.Button
Fuaran.grid "id" spec         // VisKind.DataGrid (typed GridSpecOf<'row,'Msg>)
```

Two-tier API where authoring ergonomics call for it: `Fuaran.markdown "id" "body"` for the 80% case; `Fuaran.markdownSpec "id" { Defaults.markdown with Text = ... }` for full record control. Authors choose; AI emits the record form.

### `binding.X` – typed binding constructors

```fsharp
binding.none                                   // Binding.Static Unchecked.defaultof<'T>
binding.``static`` value                       // Binding.Static
binding.query "name" (accessor: 'result -> 'T) // Binding.Query (obj-erased internally)
binding.filter "name"                          // Binding.Filter
binding.selection "nodeId" (accessor: 'row -> 'T)
binding.state "key" defaultValue               // Binding.State
binding.computed (fun ctx -> ...)              // Binding.Computed
```

`binding.query` and `binding.selection` are the typed entry points for the obj-erased Query / Selection cases. The author writes `binding.query "totalRevenue" _.amount` against a known result schema; the smart ctor boxes the `'result -> 'T` accessor.

### `Action.X` – typed action constructors

```fsharp
Action.dispatch msg                           // Action.Dispatch
Action.call endpoint (onResult: 'a -> 'Msg)   // Action.Call (obj-erased internally)
Action.notify "channel" payload
Action.navigate "/route"
Action.setState "key" value
Action.aiTool "tool-name" args
Action.chain [ a; b; c ]
```

### `format.X` – typed CellFormat constructors

```fsharp
format.currency "GBP"
format.percent (Some 1)         // 1 decimal place
format.number (Some 2)          // 2 decimals
format.significantDigits 3
format.date "yyyy-MM-dd"
```

### `Column.X` – typed Column builders

```fsharp
Column.text "Country" (fun r -> r.Country)
Column.numeric "Revenue" (fun r -> r.Revenue) |> Column.withFormat (format.currency "GBP")
Column.date "Created" (fun r -> r.Created)
Column.bool "Active" (fun r -> r.Active)

// Convert a non-interactive column to editable
Column.numeric "Price" (fun r -> r.Price)
|> Column.editable (fun r v -> Action.dispatch (SetPrice (r.Id, v)))
|> Column.withWidth (ColumnWidth.Fixed 120)
```

`Column.erase` boxes a typed `Column<'row, 'Msg>` into the tree-level row-erased `ColumnErased<'Msg>`. `Fuaran.grid` runs `List.map Column.erase` internally so the author never sees erasure.

### `Node.X` – postfix-pipe modifiers

```fsharp
node
|> Node.onLoading (Fuaran.skeleton "loading" 1)
|> Node.onEmpty (Fuaran.markdown "empty" "Nothing yet.")
|> Node.onError (fun err -> Fuaran.markdown "error" $"Failed: {err.Message}")
|> Node.withTone ToneVariant.Brand
|> Node.withWeight StyleWeight.Compact
|> Node.withEmphasis Emphasis.Loud
```

State + style modifiers always go on the **outer** Node, not inside a spec record. This keeps the spec records narrowly focused on the component's intrinsic configuration.

---

## 5. Defaults

[`src/Fuaran.UI/Defaults.fs`](../src/Fuaran.UI/Defaults.fs) ships a typed `Defaults.X` record for every spec shape. Authors use record-with-syntax to override only what differs:

```fsharp
let revenueMetric : Node<Msg> =
    Fuaran.metric "totalRevenue"
        { Defaults.metric with
            Label = TextSource.Literal "Revenue"
            Source = binding.query "totalRevenue" _.amount
            Format = format.currency "GBP" }
```

AI emits the JSON projection following the same convention – only the differing fields go on the wire; the renderer re-applies defaults on ingest. This is a **major** AI-emit-shape stability property: the JSON shape doesn't grow as the spec record grows, because absent fields are not absent values, they're "use the default."

### The `NotProvidedSentinel`

`Defaults.metric.Source = noBinding<float>` resolves to a `Binding.Query` against the magic sentinel name `__fuaran_not_provided__`. The `BindingResolver` short-circuits on this name to `NotResolved`, which triggers the `OnLoading` slot. This encodes "`Source =` is mandatory at runtime but the author hasn't overridden the default yet" in the existing §4b DU shape – the renderer reads `NotResolved` and substitutes the `OnLoading` placeholder rather than silently formatting `Unchecked.defaultof<'T>` (which would render a missing-source Metric as `0`).

Consumers MUST NOT register a query under `__fuaran_not_provided__` in `BindingSources.QueryResults`; the double-underscore + repo-namespaced shape makes a real collision effectively impossible.

---

## 6. The renderer (`Fuaran.UI.Renderer`)

The renderer's job is to walk a `Node<'Msg>` tree and emit Feliz `ReactElement`s. It runs in both `dotnet build` and `dotnet fable` pipelines – Feliz's nuget surface looks identical from both sides; Fable rewrites the calls to `React.createElement` on transpile.

### Render dispatch

`Render.fs` is a `let rec view (sources: BindingSources) (dispatch: 'Msg -> unit) (node: Node<'Msg>) : ReactElement` over the `NodeKind` DU. Per-kind dispatch with state-behaviour interpretation:

1. Resolve every `Binding<'T>` in the spec record against `BindingSources`.
2. If any binding returned `NotResolved` and the node declares `OnLoading`, render the loading placeholder.
3. If the data is present but empty and `OnEmpty` is declared, render the empty placeholder.
4. If any binding `Errored` and `OnError` is declared, render the error placeholder with the typed `ErrorPayload`.
5. Otherwise render the component body.

### Choosing a render entry

`render` takes a `RenderContext` you build; the `renderWithSources*` family builds one for you and differs only in which axes it lets you supply — **runtime**, **telemetry sink**, **runtime scope**, **correlation context**. Which axes an entry pins is the contract, so pick by that rather than by name similarity. The full grid, plus the two seams that matter to hosts registering custom renderers or mounting guests (`TryRenderCustom`, `GuestSeam`), is [`RENDER-ENTRIES.md`](RENDER-ENTRIES.md).

### `BindingSources` – the resolver inputs

```fsharp
type BindingSources =
    { QueryResults: Map<string, obj>
      State: Map<string, obj>
      Filters: Map<string, obj>
      Selections: Map<NodeId, obj>
      ComputedContext: BindingContext }
```

The consumer (host application, downstream AI consumer, test harness) provides the implementation. `BindingResolver.empty` is the test default. Session 3a shipped in-memory variants; session 3b shipped the `IFuaranRuntime` seam + browser implementation backing query / filter / selection / state resolution.

### Action interpretation

`runAction dispatch action` recursively executes `Action.Dispatch` + `Action.Chain` into the caller's `dispatch`. The other five action kinds (`Call` / `Notify` / `Navigate` / `SetState` / `AiTool`) need runtime infrastructure (HTTP client, notification channel, router, state store, AI-tool registry) that lives consumer-side; the session-3b `IFuaranRuntime` seam provides the host-side substrate. When a host hasn't installed a runtime, the renderer detects unwired actions via `containsUnwiredAction` and:

1. Marks the affording control (e.g. `Fuaran.button`) with `className "fuaran-button-unwired"` so the developer sees the gap visually.
2. Adds a `title` tooltip explaining the action requires runtime substrate.
3. Emits a `console.error` when the action fires.

### Correlation IDs for resolution failures

Each `ErrorPayload.Kind = BindingResolution` emission gets a fresh 8-char correlation id from `System.Guid.NewGuid().ToString("N").Substring(0, 8)` (Fable-compatible). Devtools / log filters can disambiguate individual failures rather than clustering them into the empty-string bucket.

### Semantic theme – CSS variables, not Tailwind tokens

[`Theme.fs`](../src/Fuaran.UI.Renderer/Theme.fs) maps semantic style (`Tone` / `Weight` / `Emphasis`) to CSS variable bindings on the rendered element. The renderer never emits Tailwind classes or pixel-CSS strings.

Why CSS variables (not className tokens)? **AI-emit shape stability.** A variable name is a stable contract regardless of which palette / dark-mode / vertical-pack theme is active in the consuming app. Consumer apps override the variables at the app shell layer; Fuaran emits the same shape every time.

Session 3a shipped the mapping table; session 3b shipped the Vite + Fable browser demo at `samples/demo/` (the reference styling target). Wiring the CSS variables into a production stylesheet remains consumer-side work – consumer app shells provide the palette. Phase 12.K promotes `Theme` to a typed contract.

**Phase 12.H** packages the reference stylesheet as `content/fuaran-reference.css` inside the `Fuaran.UI.Renderer` NuGet and turns the previously-implicit class-hook contract into a documented one. See [`HOST-STYLING-CHECKLIST.md`](HOST-STYLING-CHECKLIST.md) for the full variable + class-hook enumeration (including the Tailwind-safelist list and the `BadgeVariant` ↔ `ToneVariant` vocabulary fork) and [`THEME-BRIDGE-GUIDE.md`](THEME-BRIDGE-GUIDE.md) for the four worked examples bridging consumer design tokens (Tailwind / shadcn / raw CSS / dark mode) through to Fuaran's tone surface.

---

## 7. AI orchestration (ships separately)

The language tier in this repo is what an AI **emits** and a renderer **draws**. The runtime that lets an AI agent *drive* a live Fuaran UI from natural language – observable state snapshots, typed field decoders, natural-language fast-path resolution, pause/resume, and a default-deny server authorisation policy – is a **separate package family that does not ship in this repo** and is documented with its own guide.

What the language tier exposes for that runtime to build on is a small set of public seams:

- **`IFuaranRuntime`** ([`src/Fuaran.UI.Renderer/Runtime.fs`](../src/Fuaran.UI.Renderer/Runtime.fs)) – the host-supplied substrate the renderer calls for query / filter / selection / state resolution, clipboard, navigation, notifications, and the i18n catalog. The default browser implementation backs these against the live DOM / `navigator` APIs.
- **The introspection surface** (`Fuaran.UI.AiTools`, §4i) – read-only `fuaran.getNodeState`-style tools that project the live tree into the shapes an AI agent reasons over.
- **The op-stream** (`Fuaran.UI.OpStream.*`, §4f/§4g) – the conversation-as-source-of-truth substrate: the 10-op vocabulary, canonical-JSON, hash-chained journal, and replay engine.

A Fuaran UI is fully usable by hand or as a pure render target without any orchestration runtime installed – that is the standalone-posture mandate (§2).

---

## 8. AI emission shape

AI agents emit Fuaran fragments as flat JSON discriminated by `"kind"`. Every node has the same four top-level fields the F# record has:

```json
{
  "kind": "metric",
  "id": "totalRevenue",
  "label": "Revenue",
  "source": { "kind": "Query", "name": "totalRevenue", "accessor": ".amount" },
  "format": { "kind": "Currency", "code": "GBP" },
  "state": {
    "onLoading": { "kind": "skeleton", "id": "totalRevenue-loading", "rows": 1 },
    "onEmpty": { "kind": "markdown", "id": "totalRevenue-empty", "text": { "kind": "Literal", "value": "No revenue data yet." } }
  }
}
```

A node whose `Style` is fully default (`Default` tone, `Standard` weight, `Normal` emphasis) and whose `State` slots are all absent omits both entirely on the wire – a minimal node is just its `id` + `kind` discriminator plus the non-default spec fields. The example above carries `state` because it declares non-default loading / empty placeholders, and omits `style` because the tone is the default.

Three properties make this AI-friendly:

1. **Defaults convention.** AI emits only the fields that differ from `Defaults.X`. A Metric with no trend and no subtext omits those fields entirely; the renderer re-applies `Defaults.metric` on ingest.
2. **`NodeId` stability.** The `id` is the addressing key for structural-edit ops. AI editing a previous fragment via `UpdateProp(id, "Source", ...)` keeps the id stable across the edit.
3. **Required state slots.** `state.onLoading` / `state.onEmpty` / `state.onError` are part of the same record shape – AI can't accidentally forget them in a separate downstream-concern section.

The JSON schema for each `Kind` is derivable from the F# record contract. See [`AI_AUTHORING_GUIDE.md`](AI_AUTHORING_GUIDE.md) for the per-kind wire shapes.

---

## 9. Authoring workflow

### F# author flow

1. Open `Fuaran.UI` + (typically) `Fuaran.UI.Types`.
2. Declare the tree as a top-level `let` or inside a module's `view`:
   ```fsharp
   let view (model: Model) (dispatch: Msg -> unit) : ReactElement =
       Fuaran.dashboard "sales-analysis"
           { Defaults.dashboard with
               Children =
                 [ Fuaran.metric "totalRevenue"
                       { Defaults.metric with
                           Label = TextSource.Literal "Revenue"
                           Source = binding.query "totalRevenue" _.amount
                           Format = format.currency "GBP" }
                   |> Node.onLoading (Fuaran.skeleton "totalRevenue-loading" 1)
                   |> Node.onEmpty (Fuaran.markdown "totalRevenue-empty" "No data yet.") ] }
       |> Render.view sources dispatch
   ```
3. Wire `BindingSources` from the consuming app's Elmish model into `sources`.

### AI generation flow

1. Conversation produces a JSON fragment.
2. The host's runtime ingests via `Json.deserialize<Node<Msg>>` (the ingest path re-applies defaults).
3. The fragment renders into the existing tree via the structural-edit op engine (`Fuaran.UI.Ops`) – `Replace` / `UpdateProp` / `Insert` / `Remove` – preserving `NodeId` stability so React reconciles cleanly.

### Mixing AI-emitted and hand-authored

A common pattern: a Fuaran fragment is AI-authored, but its action handlers are wired by the human-authored code for that module. Author the typed `'Msg` cases and the `update` function by hand; let the AI emit the `Fuaran.X` tree. This is the **value-density** wedge: AI generates the structural tree but lands inside a typed, audited, MVU-disciplined application – none of the unsafe `dangerouslySetInnerHTML` properties of bare-LLM HTML generation.

---

## 10. Conventions + anti-patterns

### Conventions

- **F# style, Fantomas, MVU discipline; workspace coordination, port allocations, parallel-sessions discipline** – see the repo `CLAUDE.md` and the workspace conventions it points to.

### Fuaran-specific rules

1. **§4b is canonical for the type contract.** Do not modify `Types.fs` to "improve" the shape. If the implementation surfaces a real design defect, surface it explicitly to the operator with the proposed amendment + section reference. Silent rewrites are forbidden.
2. **Fantomas before every commit.** `dotnet fantomas .` from the repo root, then build, then commit. No exceptions.
3. **Expecto runner via `dotnet run --project`, NOT `dotnet test`.** `dotnet test` silently no-ops on Expecto console runners.
   ```powershell
   dotnet run --project src/Fuaran.UI.Tests
   ```
4. **`TreatWarningsAsErrors=true`.** Zero warnings before commit.
5. **`git push` requires explicit operator approval.** Standing approval for commits; pushing is operator-only.
6. **`Fuaran.UI` stays standalone.** No dependency beyond `FSharp.Core`. The §4l down-shift portability story requires this.

### Anti-patterns

- **Don't bypass smart constructors.** Direct construction of `Node<'Msg> { Id = ...; Kind = NodeKind.Layout (LayoutKind.Dashboard ...) }` works but loses the obj-erasure boundary smart constructors enforce. Use `Fuaran.dashboard` / `Fuaran.grid` / etc.
- **Don't author CSS-in-author-code.** The renderer maps `SemanticStyle` to CSS variables; the consuming app's stylesheet owns the values. Author-side `style.css` overrides defeat the AI-emit-shape stability story.
- **Don't register the same NodeId twice within a tree.** The renderer maps `NodeId` to React's `key`; duplicates cause reconciliation drift. Smart constructors do not enforce this – it's an author / AI-generator discipline.

---

## 11. Debugging + observability

### Renderer-emitted errors

`ErrorPayload.Kind = BindingResolution` carries an 8-char correlation id. Filter on it in devtools / log aggregators to disambiguate individual binding failures.

A Metric / Progress / Grid whose binding resolves to `NotResolved` renders the `OnLoading` placeholder by default. If the author hasn't declared one, the renderer falls through to a skeleton + `console.warn`. A Metric showing `0` when the data isn't loaded yet is a hint that `OnLoading` is missing – `Defaults.metric.Source` uses the `NotProvidedSentinel` mechanism explicitly to avoid this failure mode.

### Op-apply telemetry + drift

`Fuaran.UI.Telemetry.*` records each tree-op apply through an `IFuaranTelemetrySink`; `Fuaran.UI.Telemetry.Drift` aggregates window-over-window metrics to flag regression in deny rates / apply latency. Wire a sink (NoOp / InMemory / Console, or a host sink) at the apply boundary.

---

## 12. References

- **Fuaran design specification** – the canonical language design (§4b type contract; §4c author surface + idioms; §4f introspection / conversation-as-source-of-truth; §4g tree-op apply semantics; §4i AI tool surface; §4l down-shift portability). Maintained separately.
- **Wire format** – [`WIRE_FORMAT.md`](WIRE_FORMAT.md) + the shared conformance fixtures corpus.
- **Repo conventions** – [`../CLAUDE.md`](../CLAUDE.md).
- **AI authoring guide** – [`AI_AUTHORING_GUIDE.md`](AI_AUTHORING_GUIDE.md).
- **Host styling checklist** – [`HOST-STYLING-CHECKLIST.md`](HOST-STYLING-CHECKLIST.md).
- **Render entries** – [`RENDER-ENTRIES.md`](RENDER-ENTRIES.md) (the entry × runtime × scope × sink hosting matrix + the `GuestSeam` mount policy).

### Phase tracking

The Fuaran roadmap lives at `../roadmap/` – start with `../roadmap/DEVELOPMENT_ROADMAP.md` for the Current State + Development Waves + Phase Index.

---

## 13. Licensing posture

The Fuaran language tier – `Fuaran.UI` + `Fuaran.UI.Renderer` + `Fuaran.UI.Ops` + `Fuaran.UI.AiTools` + `Fuaran.UI.Validator` + `Fuaran.UI.OpStream.*` + `Fuaran.UI.LayoutObserver.*` + `Fuaran.UI.Telemetry.*` – ships under **Apache-2.0**. See [`../LICENSE`](../LICENSE) for the full grant.

The AI-orchestration runtime that drives a Fuaran UI (§7) is a separate package family with its own licensing, published separately from this repo.

---

_This guide is hand-curated and drift-prone; cross-check claims against `../roadmap/DEVELOPMENT_ROADMAP.md` when authority matters._
