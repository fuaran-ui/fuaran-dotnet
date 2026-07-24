# Fuaran.UI.Memo

Incremental re-derivation engine — effect-aware memoisation over the Fuaran
artifact-function `apply` (`FragmentApply.apply`).

- **Whole-application memo.** Substituted fragment trees are content-keyed
  (canonical-JSON + SHA-256, the same digest the op-stream hash-chain uses) and
  held in a bounded LRU. An identical application is a cache hit.
- **Incremental re-derivation.** A value-parameter change reuses the structural
  tree (reference-identical) and recomputes only the affected hole-addressed
  (`<refId>.<holeName>`) bindings; disjoint changes don't invalidate each other.
- **Effect gate.** Only pure-deterministic fragments are cached; effecting /
  clock / random / network fragments bypass the cache and re-derive every call.
- **Replay-as-reapplication.** An op-stream replay resolves through the cache to
  a byte-identical tree.
- **Observability.** Every call emits a `CacheStatTelemetry` (hit / miss /
  incremental / bypass + cache size against its bound) to the configured
  `IFuaranTelemetrySink`.

The cache is transparent: no wire change, no change to existing apply semantics.

```fsharp
open Fuaran.UI.Memo

let engine = Engine<unit>(capacity = 256, sink = telemetrySink)

// First derivation: a cache miss; the substituted tree is stored.
let d0 = engine.Apply(fragment, "ref1", valueArgs, slotArgs) |> Result.toOption |> Option.get

// Edit a value parameter → the tree is reused; only that binding recomputes.
let d1 = engine.Reapply(d0, fragment, "ref1", changedValueArgs, slotArgs)
```

Apache-2.0 licensed — see the repo [LICENSE](../../LICENSE).
