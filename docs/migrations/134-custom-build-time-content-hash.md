# Phase 134 – build-time SHA-256 `contentHash` for `NodeKind.Custom` bodies

Shipped 2026-06-03. Additive validator behaviour + a `HashStrictness` upgrade path (advisory → enforced). Pre-1.0 minor add – no type-contract break to the wire shape (a new enum *value*, not a new field).

## What changed

[Phase 70](70-custom-bounded-escape.md) shipped `NodeKind.Custom`'s `contentHash` + `HashStrictness` safety surfaces but left the hash itself for consumers to set **by hand** – a sentinel string like `"Individual.HeatmapTab.v1"` with `AdvisoryWarning` strictness. A hand-set hash is advisory by construction: nothing mechanical relates it to the body it claims to fingerprint, so drift is invisible. Phase 70's own "Computing content hashes" section flagged "FCS AST walk at build time" as the preferred follow-up but shipped no implementation.

Phase 134 ships that implementation. The build-time validator now computes a deterministic SHA-256 over the Custom body's **declared shape** and compares it to the hand-set `Hash`, surfacing drift as **FUARAN062**.

Two surfaces changed:

1. **`HashStrictness` gains an `Enforced` case** (`Fuaran.UI/Types.fs`):

   ```fsharp
   [<RequireQualifiedAccess>]
   type HashStrictness =
       | StrictReplay
       | AdvisoryWarning
       | Enforced          // NEW — Phase 134
   ```

   `Enforced` is the *build-time* enforcement posture: FUARAN062 fires as an **Error** (fails the build) when the hand-set hash has drifted. `StrictReplay` / `AdvisoryWarning` keep the **Warning** posture (surface the drift without breaking the build). At render / replay time `Enforced` behaves like `StrictReplay` – routes a mismatch through `OnError` – but the build gate is the primary enforcement.

2. **A new validator check** (`Fuaran.UI.Validator/CustomContentHashCheck.fs`) emitting **FUARAN062 CustomContentHashStale**.

## The body shape the hash covers

The load-bearing definition. The hash covers the Custom body's **declared shape** – never its runtime values:

- `moduleId`
- `componentId`
- **props schema** = the *keys* of the props map (not the values – values are runtime data, e.g. `JsonValue` payloads bound at dispatch time)
- `exposedNodeIds` = the declared interior-NodeId strings

The canonical pre-image is:

```
fuaran-custom-body-shape:v1\n
moduleId=<moduleId>\n
componentId=<componentId>\n
props=<sorted prop keys, comma-joined>\n
exposed=<sorted exposedNodeIds, comma-joined>
```

`SHA-256` of the UTF-8 bytes, rendered lower-case hex. **Prop keys and exposed-id lists are sorted**, so the hash is insensitive to declaration order – deterministic and replay-stable. This algorithm is reproduced verbatim here so any conformant host (including the TypeScript reference implementation) can compute the identical digest; the F# implementation is exposed as a pure helper:

```fsharp
Fuaran.UI.Validator.CustomContentHashCheck.computeBodyShapeHash
    : moduleId: string -> componentId: string
   -> propKeys: string list -> exposedNodeIds: string list -> string
```

### Why shape, not source

Phase 70's section sketched hashing the *registered renderer's F# source*. Phase 134 hashes the *construction-site declared shape* instead – that is what the build-time AST walker can see at the Custom node's call site, and it is what op-stream replay actually re-materialises. A renderer-source hash answers "did the implementation change?"; a body-shape hash answers "did the contract the tree declares change?" – the latter is the replay-safety question.

## FUARAN062 – CustomContentHashStale

| Strictness | Severity | Build outcome |
|---|---|---|
| `Enforced` | **Error** | fails `dotnet run -- Validate` |
| `StrictReplay` | Warning | surfaced, build passes |
| `AdvisoryWarning` | Warning | surfaced, build passes |

The finding carries the **computed hash** in its `Suggestion` so the author can paste it in – this is the migration mechanism. When the hand-set value is not a 64-char hex string the message additionally notes it "looks like a hand-set sentinel rather than a computed hash".

### Conservative firing

Like the sibling Custom-health checks (FUARAN053/054/055), the rule is best-effort and fires **only when the body shape is statically resolvable** from the construction site:

- literal `moduleId` / `componentId` strings;
- props as `Map.empty` or `Map.ofList [ "k", _; … ]` / `dict [ … ]` with literal **string keys**;
- a literal `[ NodeId "…"; … ]` exposed-id list (or `[]`);
- a literal `Some { Algorithm = "SHA256"; Hash = "…"; Strictness = … }`.

Anything built from a let-bound variable, a function call, or a list comprehension is treated as unverifiable and **skipped** – no false positives. A non-`SHA256` algorithm is also skipped (left for a future phase). A Custom node with `contentHash = None` is not FUARAN062's concern (that absence is FUARAN055's advisory).

## Migration steps

### 1. Pattern-match arms on `HashStrictness`

Adding `Enforced` makes exhaustive matches on `HashStrictness` incomplete. The language-tier sites updated in this phase: `Render.fs` (`classifyCustomHash`), `JsonDecode.fs`, `CanonicalJson.fs`, `SchemaGen.fs`, and the property-test generator. Following the Phase 12.F / Phase 70 precedent, this is a pre-1.0 minor add – no major version bump.

Cross-sibling adoption (verified 2026-06-03):

- **Host/orchestrator tier** – **no action needed.** A source scan found zero `HashStrictness` references in the orchestrator tier; it consumes the wire value opaquely. (The matches in a demo consumer's vendored Fable output are of the prior `0.0.1-alpha` pack and refresh on that consumer's next rebuild against the new pack.)
- **`fuaran-ts` reference implementation** – **action needed to stay a conformant host.** The TS tier hard-codes the prior two values: the `HashStrictness` type union ([`packages/schema/src/types.ts:161`](../../../fuaran-ts/packages/schema/src/types.ts)) and the decoder guard ([`packages/ops/src/decode.ts:1810`](../../../fuaran-ts/packages/ops/src/decode.ts)) both omit `'Enforced'`, so the TS decoder would *reject* a canonical wire document carrying `"strictness": "Enforced"`. The encoder ([`packages/ops/src/encode.ts:905`](../../../fuaran-ts/packages/ops/src/encode.ts)) passes the string through unchanged and needs no edit. Tracked as a Tidy-Up bundle in `roadmap/TIDY-UP.md`. This is the canonical-wire-format forward-coupling obligation: `WIRE_FORMAT.md` §11 expects both hosts to track an enum addition; the F# host + `schema.json` shipped it this phase, the TS host follows.

### 2. Adopt a computed hash (advisory → enforced)

For an existing hand-set sentinel (e.g. a `"…HeatmapTab.v1"` with `AdvisoryWarning`):

1. Run the validator (`dotnet run -- Validate`, or your consumer build target). FUARAN062 fires as a Warning and reports the **computed** body-shape hash.
2. Paste the computed hash into the `contentHash.Hash` field.
3. Flip `Strictness` from `AdvisoryWarning` to `Enforced`. Future drift in the declared shape now fails the build mechanically.

```fsharp
// Before — advisory sentinel
Fuaran.custom "heatmap" "reporting" "HeatmapTab" props
    (Some { Algorithm = "SHA256"; Hash = "reporting.HeatmapTab.v1"; Strictness = HashStrictness.AdvisoryWarning })
    [ NodeId "cell-grid" ]

// After — mechanical, build-enforced
Fuaran.custom "heatmap" "reporting" "HeatmapTab" props
    (Some { Algorithm = "SHA256"; Hash = "<computed 64-char hex>"; Strictness = HashStrictness.Enforced })
    [ NodeId "cell-grid" ]
```

### 3. Wire shape

The canonical-JSON `contentHash.strictness` value can now be `"Enforced"` in addition to `"StrictReplay"` / `"AdvisoryWarning"`. The workspace `wire-format-fixtures/schema.json` enum was regenerated in the same change-set per the forward-coupling rule. Existing fixtures (which use the prior two values) are unaffected and continue to round-trip.

## Anti-patterns

- **Don't hash runtime values.** The shape is keys + ids + module/component identity. Hashing prop *values* would make the hash change on every data refresh – useless for replay safety.
- **Don't conflate `Enforced` with `StrictReplay`.** `Enforced` is the *build-time* gate; `StrictReplay` is the *replay-time* raise. A consumer can want one without the other.
- **Don't relax the conservative-firing posture into guessing.** A non-literal body shape is unverifiable, not "assume stale". The check skips it – silently widening to a guess would produce false positives on legitimate dynamic-props sites.

## See also

- [Phase 70 migration note](70-custom-bounded-escape.md) – the bounded-escape surfaces this phase hardens.
- `docs/AI_AUTHORING_GUIDE.md` § "Custom – the last-resort bounded escape".
- `Fuaran.UI.Validator/CustomContentHashCheck.fs` – the check + the `computeBodyShapeHash` helper.
