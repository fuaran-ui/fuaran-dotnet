# Migration — the two fail-open defaults flip (Phase 1550)

## Symptom

You upgraded `Fuaran.UI.Renderer` / `Fuaran.UI.Renderer.Core` and **some `Custom` nodes stopped
rendering**. Where the component used to appear you now get the labelled mismatch placeholder — or
your `OnError` route, if you supplied one — and your console (or whatever `IFuaranRuntime.Warn` is
wired to) carries:

```
{ "kind": "FuaranCustomHashMismatch", "moduleId": "analytics", "componentId": "trend-card", "expected": "SHA256:9a3f…", "actual": "SHA256:11c0…" }
```

That is the change working. The `Custom` content-hash **host floor** used to default to
`AdvisoryWarning`, so a tree whose declared hash disagreed with the registered renderer's warned and
rendered anyway; it now defaults to `Enforced` and refuses. The published posture was always that
enforcement is available and that a tree cannot talk its way underneath the host's choice — and until
this release the shipped default said the disagreement did not matter.

**A `Custom` node that declares no hash at all is unaffected and still renders.** If yours stopped,
it declared a hash that does not match what the registry recorded, which is the case the floor exists
for.

## The fix — pick one

### 1. Fix the hash (preferred)

A mismatch means the tree and the registered renderer disagree about the component's shape. Two
honest causes:

- **The renderer moved and the tree is stale.** Re-emit the tree, or re-derive its `ContentHash`.
  With `CustomContract` the hash is derived from the declared shape rather than hand-typed, so
  `CustomContract.hash contract` is the value the tree should carry — hand-set hashes are what
  `FUARAN062` reports statically.
- **The tree is addressing a different component than you think.** The registry is scope-keyed with
  no cross-scope fallback (Phase 783), so check the scope as well as the module/component ids.

### 2. Name the permissive posture (the ramp, not the destination)

```fsharp
Fuaran.UI.Renderer.CustomHash.installCustomHashFloor HashStrictness.AdvisoryWarning
```

That is the pre-flip behaviour exactly: a mismatch warns and renders, with the warning intact.
Nothing else changes.

**Why a call and not an unchanged default.** `grep -r installCustomHashFloor` over your codebase now
enumerates every place the permissive posture is in force. An unchanged default would have left that
question unanswerable — which is how the estate arrived at a documented content-hash floor that
warned and rendered in every host that had not configured one.

The install is raise-only **among declarations**, so this works from a fresh process and cannot undo
an enforcement another part of the process has already declared. That is deliberate: on a tier
serving several tenants from one process, a permissive declaration that could lower an installed
floor would hand every tenant the ability to switch off the host's verification.

### 3. If you were already enforcing, re-read which floor you want

| You installed | What changed |
|---|---|
| `HashStrictness.StrictReplay` | Nothing. Every arm behaves exactly as before. |
| `HashStrictness.Enforced` | A mismatch is still refused. A node that declares **no** hash now renders where it used to be refused. |

`Enforced` governs the declared hash; `StrictReplay` additionally refuses a node whose hash cannot be
verified because the tree declared none. If you installed `Enforced` in order to close the
declare-nothing route, **install `StrictReplay`** — one word, same call, and it is the floor whose
name was always the stronger claim.

The two are ordered now (`StrictReplay` above `Enforced`) where they used to rank equal, so a
`RenderContext.CustomHashFloor` of `StrictReplay` raises above the shipped default rather than being
discarded as no stricter.

## The other default in the same release: reserved state keys

### Symptom

A state write your tree used to make stops landing, and the `Warn` channel carries:

```
[Fuaran] State write refused — key 'sessionToken' was declared host-reserved by name and is not addressable from a rendered tree.
```

That happens only if **your host declared that key**, so unlike the floor above there is nothing to
fix on upgrade: nothing changes for a host that declares nothing.

### What is new

Reservation used to be the `host.` prefix and nothing else, so a host-owned key named before that
convention existed could be written by any tree that rendered — and the only hardening was to rename
the slot through every reader, every `BindingSources.State` seed and every persisted value. That is
why, in practice, it was not done.

```fsharp
// At startup, or as the surfaces that own each slot are composed.
Fuaran.UI.Renderer.StateStore.declareReserved [ "sessionToken"; "tenantId" ]
```

Every tree-originated write to a declared key is now refused and recorded — `Action.SetState`, a
covered control's write-back default, a declarative `Call … into State` target, an edit buffer's
`commitTo` — on the same path, through the same diagnostic, that has refused `host.*` since 0.14.0.
It holds **even when your gate allows everything**: this is a namespace, not gate policy.

Three properties worth knowing:

- **The prefix rule seeds the list.** `host.*` is still reserved with no declaration at all, so
  nothing you rely on today stops being reserved and you never declare what the prefix covers.
- **Host code is unrestricted, as it always was.** `StateStore.set`, your own `BindingSources.State`
  seed and a scoped store's own writes reach a reserved key normally. The restriction is on the tree
  side only.
- **There is no withdrawal.** `declareReserved` is additive and idempotent and has no paired
  un-declare, deliberately: a function that un-reserved a key would be reachable from any assembly
  loaded into the process, which is the fail-open shape reservation exists to remove. Declare what
  you own.

### The new validator finding

`FUARAN149` (Warning), from the pre-emit validator, in two shapes:

- **A tree writes a reserved key.** Refused at dispatch, so the gesture runs and nothing happens —
  reported at build time rather than discovered at render time. Covers prefixed keys too, which have
  been refused since 0.14.0 and reported by nothing.
- **A tree writes an unread, unreserved key** — while your host has declared at least one
  *pre-convention* slot (one the `host.` prefix does not cover). That is the shape a host-owned
  legacy slot has when a rendered tree can reach it, and the message names the remedy. It is silent
  unless you have made such a declaration, because the validator cannot see your host and must not
  guess which unprefixed keys are yours.

`FUARAN098` and the other host-writes-it exemptions now read the whole reserved rule rather than the
prefix alone, so a key you declare is exempted from them exactly as a prefixed one always was — one
finding on a write, never two.

## Rollback

Pin the previous version. Nothing here changes the wire format, so a tree emitted against either
version decodes on the other, and no corpus fixture moved.

## Verification

1. Build. Nothing should fail to compile — both breaks are behavioural, not signature-level.
2. Run your app and read the `Warn` channel. Every `FuaranCustomHashMismatch` line names a component
   whose registered renderer and declared hash disagree. That list is real drift, whatever you decide
   to do about it.
3. Confirm the enforcement is real rather than assumed: point a tree at a deliberately wrong hash and
   check the node is refused. A gate that agrees with what you expected is the least-examined kind of
   evidence.
4. If you declared reserved keys, confirm one is refused from a tree AND still writable by your own
   host code — the two halves are the whole of the contract, and a reservation that blocked the host
   too would be a different bug.
