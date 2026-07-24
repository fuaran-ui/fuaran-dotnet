# Phase 70 – `NodeKind.Custom` bounded escape (contentHash + exposedNodeIds + project-health metric)

Shipped 2026-05-29. Pre-1.0 minor add – additive extension to an existing stable surface.

## What changed

`NodeKind.Custom` was Fuaran's acknowledged escape hatch for components the typed surface doesn't reach (third-party React widgets, canvas visualisations, drag-drop interactions, platform-specific affordances). Pre-Phase-70 it worked but was an *unbounded* hole in the contract: opaque to op-stream replay, opaque to the layout observer, opaque to the validator, opaque to the AI introspection tools.

Phase 70 turns the unbounded hole into a **bounded** one by extending the existing `Custom` case with two additive optional fields:

```fsharp
and [<RequireQualifiedAccess>] NodeKind<'Msg> =
    | ...
    | Custom of
        moduleId: string *
        componentId: string *
        props: Map<string, JsonValue> *
        contentHash: ContentHash option *           // NEW — Phase 70
        exposedNodeIds: NodeId list                  // NEW — Phase 70
```

Plus two new top-level types:

```fsharp
type ContentHash =
    { Algorithm: string
      Hash: string
      Strictness: HashStrictness }

[<RequireQualifiedAccess>]
type HashStrictness =
    | StrictReplay
    | AdvisoryWarning
```

The body's identity becomes hashable; its declared interior NodeIds become observable; its module-scope discipline becomes part of the contract. The escape stays an escape – but with declared boundaries the rest of the system can still reason about.

## Cultural-posture invariant

This phase enhances Custom's safety guarantees. It MUST NOT enhance Custom's invitation-to-use. Custom remains the language's **last-resort escape**, not the path of least resistance for typed-surface friction. The AI authoring guide section ("Custom – the last-resort bounded escape") + the validator's FUARAN054 advisory together preserve the cultural default.

If a project finds itself reaching for Custom repeatedly, treat that as a signal: there's a typed-contract gap the language should close upstream rather than absorbing the creep silently. FUARAN054 fires when the project's Custom-node count exceeds the manifest-declared threshold (default `0.05`).

The "always one exception" empirical claim: every real Fuaran consumer will have a small finite set of components that don't fit the typed surface. Pretending otherwise leads to either typed-surface contortion (worse design) or silent reliance on opaque Custom (worse safety). Neither is what the language wants. Phase 70 makes those legitimate exceptions **honestly bounded**.

## Migration steps

### 1. Pattern-match arms

Every `NodeKind.Custom(_, _, _)` pattern grows to `NodeKind.Custom(_, _, _, _, _)`. F# exhaustiveness check guides the migration – every site fails to compile until updated. Sites in the language tier that already shipped: `PreEmitValidate.fs`, `Apply.fs`, `Introspect.fs`, `AiTools.Tools.fs`, `Theme.fs`, `Render.fs`, `CanonicalJson.fs`, `JsonDecode.fs`.

Wildcard `NodeKind.Custom _` patterns (e.g. `Apply.fs` line 487) work unchanged – F# wildcards aren't arity-sensitive.

### 2. Construction-site call sites

Authors who constructed `NodeKind.Custom(moduleId, componentId, props)` directly need to pass `None` + `[]` for the two new fields:

```fsharp
// Before
NodeKind.Custom("my-mod", "MyComp", Map.empty)

// After
NodeKind.Custom("my-mod", "MyComp", Map.empty, None, [])
```

Or – preferred – use the new `Fuaran.custom` smart-ctor:

```fsharp
Fuaran.custom "my-id" "my-mod" "MyComp" Map.empty None []
```

The smart-ctor routes through the same `buildNode` defaults every other kind uses (style / accessibility / motion / state-behaviour) and gives the validator a stable lexical pattern to walk.

### 3. Registering renderers with hashes

`CustomRendererRegistry.Register` and `MutableRuntime.RegisterCustomRenderer` / `BrowserRuntime.RegisterCustomRenderer` gain an optional `?contentHash` parameter:

```fsharp
runtime.RegisterCustomRenderer("my-mod", "MyComp", renderFn)               // pre-Phase-70 shape — still works
runtime.RegisterCustomRenderer("my-mod", "MyComp", renderFn, myHash)      // Phase 70 — hash-aware
```

### 4. `IFuaranRuntime` direct implementers

`IFuaranRuntime` gains one new abstract member:

```fsharp
abstract TryGetCustomRenderer:
    moduleId: string * componentId: string ->
        ((Map<string, JsonValue> -> ReactElement) * ContentHash option) option
```

Hosts implementing `IFuaranRuntime` directly (not via `MutableRuntime` / `BrowserRuntime`) must add this member. Returning `None` is the back-compat-safe default – the renderer falls through to the existing `TryRenderCustom` for dispatch, skipping Phase 70 verification.

Following the Phase 12.F precedent, this is a pre-1.0 minor add – no major version bump.

## Hash verification flow

The renderer's Custom arm consults `runtime.TryGetCustomRenderer(moduleId, componentId)` to retrieve `(renderFn, registryHash)`. It then classifies the (tree-hash, registry-hash) pair:

| Tree hash | Registry hash | Outcome | Behaviour |
|---|---|---|---|
| `None` | `_` | `NoTreeHash` | Render normally (pre-Phase-70 opt-out). |
| `Some` | `None` | `RegistryNoHash` | Warn + render. Tree expected verification; registry doesn't participate. |
| `Some t` | `Some r`, match | `Match` | Render normally. |
| `Some t` | `Some r`, mismatch, `AdvisoryWarning` | `MismatchAdvisory` | Warn + render. |
| `Some t` | `Some r`, mismatch, `StrictReplay` | `MismatchStrict` | Warn + route through `OnError`. |

The structured warn-channel payload is a single-line JSON shape so log-tail tooling stays readable:

```json
{ "kind": "FuaranCustomHashMismatch", "moduleId": "my-mod", "componentId": "MyComp", "expected": "SHA256:abc123", "actual": "SHA256:def456" }
```

## Exposed-NodeIds DOM walk

When a Custom node declares `exposedNodeIds = [...]`, the renderer schedules a post-paint DOM walk via `setTimeout(0)` (Fable-side only – .NET-side is a no-op since `Browser.Dom` isn't meaningfully available in test contexts). The walk scans the Custom wrapper's subtree for `[data-fuaran-node-id]` attributes and verifies each declared id appears. Missing ids log through `IFuaranRuntime.Warn`. Non-blocking – the render completes regardless.

Custom renderers participating in `exposedNodeIds` emit the matching `data-fuaran-node-id` attribute on each interior element they want to expose:

```fsharp
let private myRenderer (props: Map<string, JsonValue>) : ReactElement =
    Html.div [
        prop.children [
            Html.div [
                prop.custom ("data-fuaran-node-id", "my-segment-a")
                prop.text "Segment A"
            ]
            Html.div [
                prop.custom ("data-fuaran-node-id", "my-segment-b")
                prop.text "Segment B"
            ]
        ]
    ]
```

The layout observer's `MutationObserver` then self-discovers these declared interior elements and addresses them like any other Fuaran-addressable node.

## Validator rules

Four new defect codes:

- **FUARAN052** (Error) – op-stream replay surfaces a Custom hash mismatch with `Strictness = StrictReplay`. Detected at the apply engine's error-reporting surface (not at build time – the validator has no op-stream view). The constant lives in `Fuaran.UI.Validator.CustomHealthCheck.codeFUARAN052` for cross-reference; runtime surface lives in the apply engine.

- **FUARAN053** (Warning) – a Custom node declares `exposedNodeIds = [...]` but no `RegisterCustomRenderer` for the same `(moduleId, componentId)` appears in the project's source. Conservative shape: best-effort detection of the registration cross-reference. Walking the registered renderer's body for emit-sites (the "perfect" check) is intentionally not shipped in v1 – Feliz emits attributes through many patterns and the walker can't recognise all of them. Future phase candidate.

- **FUARAN054** (Advisory Warning) – the project's Custom-node count exceeds the manifest-declared `customNodeRatio` (default `0.05`). Fires only when `customCount >= 3` AND `totalTypedNodes >= 20` – small-project floor prevents structural noise. The advisory's structured payload lists the contributing call sites so the operator can audit. **This is the project-health metric** – the cultural-posture mitigation against Custom-creep.

- **FUARAN055** (Advisory Warning) – a Custom node lacks `contentHash` (the safety feature wasn't adopted for this escape). Advisory only – opting out of replay safety is a valid choice.

### Manifest extension

`fuaran-validator.manifest.json` gains a `customNodeRatio` field:

```json
{
  "queries": [ "..." ],
  "msgCases": [ "..." ],
  "customNodeRatio": 0.10
}
```

Consumers with legitimately higher Custom usage (e.g. a UI shell wrapping many third-party React components) can raise the threshold. The manifest declaration is the audit point – "we know we have many Customs and here's why".

## Wire shape

The canonical-JSON wire form gains two optional fields on the `Custom` discriminator:

```json
{
  "$type": "Custom",
  "moduleId": "my-mod",
  "componentId": "MyComp",
  "props": { ... },
  "contentHash": {
    "algorithm": "SHA256",
    "hash": "abc123...",
    "strictness": "StrictReplay"
  },
  "exposedNodeIds": ["interior-id-1", "interior-id-2"]
}
```

Both fields decode optionally – absent maps to `None` / `[]`. The wire shape is the stable structural-decoder surface: JsonDecode breakage on either field is a major-version bump (same shape as Phase 62's `Local` wire-lock declaration).

## Computing content hashes

Several recommended shapes (none baked into the language tier):

1. **FCS AST walk at build time** (preferred). A consumer-side Build.fsproj target reads the registered renderer's F# source via `FSharpChecker`, walks the typed expression tree, normalises to a canonical form, and SHA-256s. Stable across whitespace + formatting changes; sensitive to actual code changes. The Fuaran toolchain ships no specific implementation; consumers pick their normalisation.

2. **`sha256(File.ReadAllText("MyModule.fs"))`** – simplest. Whitespace-sensitive but acceptable for projects under stable formatter conventions (e.g. always Fantomas-formatted before commit). Document the constraint in the consumer's authoring guide.

3. **Runtime `f.ToString()`** – explicitly NOT recommended. F# closure `ToString` is implementation-dependent and brittle across runtime versions. The hash should be content-derived, not runtime-typed.

## Anti-patterns

- **Don't ship hash verification as renderer-only.** Op-stream replay is the load-bearing scenario; the orchestrator's apply engine must perform the same verification. FUARAN052's home is the apply engine's error path.
- **Don't conflate `ContentHash` with `Strictness`.** The hash is just bytes; `Strictness` governs what happens on mismatch.
- **Don't enforce a global Custom-node ratio threshold.** Some app shapes legitimately have many Custom escapes (UI shells). The manifest declaration is the audit point; the rule's default is a starting suggestion.
- **Don't relax the "last-resort escape" framing.** Safer Custom must not become more-inviting Custom. The AI authoring guide language + FUARAN054 are the cultural-posture mitigation.

## Reopen criteria

If FUARAN054 starts firing across multiple consumers AND the underlying gaps are NOT being addressed via new typed-contract phases, the safety features have over-shot – the language is being asked to absorb more Custom than is healthy. The right response is to surface the gaps as language-roadmap candidates, not to relax FUARAN054's threshold.
