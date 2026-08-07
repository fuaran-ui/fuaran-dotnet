# Migration — 0.15.0: Mount + custom-renderer isolation (Phase 783)

Four changes. Two break at **compile time** (you will see them), two break at **runtime** (you might
not) — the runtime ones are listed first for that reason.

## 1. Your `Custom` nodes render a placeholder now

**Symptom.** `NodeKind.Custom` nodes that used to render your component now show the labelled
`fuaran-custom-placeholder`.

**Cause.** The registry is keyed on `(scope, moduleId, componentId)` and the renderer calls the new
scoped lookup members. Either:

- you implement `IFuaranRuntime` **by hand** and have not implemented
  `TryRenderCustomInScope` / `TryGetCustomRendererInScope` (they were added, so yours return
  whatever you wrote — most likely `None`); or
- you register renderers and render the tree under **different scopes**.

**Fix (hand-written runtime).** Delegate to your registry, exactly as the scoped members' unscoped
twins do:

```fsharp
member _.TryRenderCustomInScope(scope, moduleId, componentId, props) =
    registry.TryRenderInScope(scope, moduleId, componentId, props)

member _.TryGetCustomRendererInScope(scope, moduleId, componentId) =
    registry.TryGetInScope(scope, moduleId, componentId)
```

If you have no registry, `None` for both is correct — and now means what it says.

**Fix (scope mismatch).** A plain `render` / `renderWithSources` runs under the **root** scope
(`None`), which is where the unscoped `Register` puts renderers, so the ordinary single-surface host
needs no change. If you render under a scope (`renderWithSourcesInScope`, or inside a `Mount` guest),
register for that scope:

```fsharp
registry.RegisterInScope("admin-surface", "reports", "chart", renderChart)
Render.renderWithSourcesInScope "admin-surface" sources dispatch node
```

**There is no cross-scope fallback**, and that is the point: a fallback makes the scoping advisory,
which is the same as not having it. Before 0.15.0 the registry was one process-wide map keyed on ids
taken straight off the wire, so a tree rendered on a public surface could invoke a renderer registered
for a privileged one.

Server side is identical: `Registry.registerInScope` / `registerInScopeWithHash`, and
`ServerRenderContext.Scope` (or the new `Render.renderWithInScope`).

## 2. Your mounted guests can no longer do anything

**Symptom.** A `NodeKind.Mount` guest renders but its actions are refused, with

```
[Fuaran] mount guest 'my-scope' attempted Call(/api/x) with no GuestSeam installed — refused.
```

**Cause.** With no `GuestSeam` installed the guest used to receive the **host's own runtime**,
unwrapped. It now receives `Runtime.UnprivilegedGuestRuntime`. A guest is foreign content and
`MountSpec.Capabilities` is documented as "a request, not a grant", so a host that installed no
policy granting a mounted guest everything the host could do was the inverse of the declared posture.

**Fix.** Install a seam and return whatever runtime you intend the guest to have. This is the
deliberate, visible act the seam exists for:

```fsharp
Render.installGuestSeam
    { WrapRuntime = fun ctx hostRuntime -> myScopedRuntimeFor ctx.ScopeId hostRuntime
      GateBubble = fun _ raw -> raw
      GrantTwoWay = fun _ -> false }
```

Returning `hostRuntime` unchanged restores the old behaviour for every mount — do that only if you
control every tree you render.

## 3. `GuestSeam` and `GuestSeamContext` gained fields (compile break)

```fsharp
// GuestSeam
GrantTwoWay: GuestSeamContext -> bool     // NEW — write `fun _ -> false` unless you need TwoWay

// GuestSeamContext
Channel: GuestChannel                     // now the EFFECTIVE channel (clamped, or granted)
DeclaredDirection: ChannelDirection       // NEW — what the tree asked for
```

A decoded mount is **clamped to `OutOnly`** regardless of what it declared, and the downgrade is
recorded through `Warn`. `ChannelDirection` is a *required* wire field, so a hostile tree simply wrote
`TwoWay`; `OutOnly` was only the default of the authoring smart constructor. `TwoWay` is now a host
grant.

If a specific mount genuinely needs host→guest push, grant it by identity, not by trusting the
declaration:

```fsharp
GrantTwoWay = fun ctx -> ctx.ScopeId = "my-trusted-embed"
```

Note the clamp is at the **renderer**, not the decoder: the wire still round-trips `TwoWay`
byte-identically, so no conformance fixture and no other host implementation is affected.

## 4. `ServerRenderContext` gained a required field (compile break)

```fsharp
{ Sources = …; Fragments = …; Customs = …; Scope = None }   // Scope is new
```

`None` is the root scope and reproduces the previous behaviour. Prefer the constructors:
`Render.mkContext`, `Render.mkContextWith`, or the new `Render.mkContextInScope`.

## 5. Optional: turn on hash enforcement

`ContentHash` is **drift detection between a registered renderer and a replayed tree. It is not
authentication of the tree** — the tree supplies its own hash record, so a match proves only that
whoever wrote the tree knew the registered renderer's hash. Read it that way and two bypasses follow,
both of which existed:

- omitting the hash classified as `NoTreeHash`, which shared a render branch with `Match` and rendered
  **silently**;
- strictness was read from the *tree's own* record, so a tree that declared `AdvisoryWarning` got
  warn-then-render on a mismatch.

Strictness is now a host floor a tree may only **tighten**:

```fsharp
open Fuaran.UI.Renderer
CustomHash.installCustomHashFloor HashStrictness.StrictReplay
```

Under an enforcing floor, a `Custom` node whose hash cannot be verified — the tree declared none, or
the registry recorded none — is **refused** rather than rendered.

**The default is unchanged (`AdvisoryWarning`)**, so this costs you nothing until you opt in. That is
deliberate: a tree with no hash is the common legitimate case, and an enforcing default would refuse
most existing `Custom` nodes on upgrade. Before enabling it, register your renderers with hashes
(`Register(…, contentHash)` / `RegisterContract`, or `registerWithHash` server-side) — otherwise every
`Custom` node becomes `Unverifiable`.

## Rollback

Pin `0.14.0`. No wire-format change: no fixture moved, and a tree emitted against either version
decodes on the other.

## Verification

1. Build. Fix the two compile breaks (`GuestSeam` / `GuestSeamContext`, `ServerRenderContext.Scope`)
   and any hand-written `IFuaranRuntime`.
2. Render a page with `Custom` nodes and a page with a `Mount`. Watch the `Warn` channel: a
   placeholder where you expected a component means a scope mismatch; a refusal line from a mount
   means you have not installed a seam.
3. Prove the boundary rather than assuming it: register a renderer under one scope, render a tree
   naming it under another, and confirm the placeholder. A guard you have not seen refuse is a guard
   you have not tested.
