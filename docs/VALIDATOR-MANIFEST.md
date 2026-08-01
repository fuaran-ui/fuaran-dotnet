# Fuaran.UI.Validator manifest format – v1

The build-time validator gates a Fuaran tree against a typed contract: which query names may be bound, which `Msg` cases may be dispatched, and what row type each query returns. That contract is `fuaran-validator.manifest.json`, sibling of the `.fsproj`.

**The manifest is generated from your app's own F# source.** It used to be hand-written, which made the one artefact grounding everything else the least grounded artefact in the system — free to drift from the app it described, silently, with the validator reporting green the whole time. The emitter closes that gap: derive the contract, and it cannot disagree with the source. Hand-written entries survive as an explicit **override tier**, for the things no walker can see.

This document is the public contract for the manifest's wire shape and for the generate-first workflow. Renames or removed fields are **semver-major breaking changes** to the `Fuaran.UI.Validator` package.

## Quick start

```bash
# Generate (or regenerate) the manifest beside the .fsproj
Fuaran.UI.Validator emit-manifest src/MyModule/MyModule.fsproj

# Fail the build if the committed manifest no longer describes the source
Fuaran.UI.Validator emit-manifest src/MyModule/MyModule.fsproj --check
```

Commit the generated `fuaran-validator.manifest.json`. Run `--check` in CI. A developer who adds a query and forgets to regenerate gets a build failure naming the query, instead of a validator surprise somewhere downstream.

## File layout

```
src/MyModule/
├── MyModule.fsproj
├── fuaran-validator.manifest.json             ← generated; committed; the validator reads THIS
├── fuaran-validator.manifest.overrides.json   ← optional; hand-asserted entries only
└── ...
```

The validator reads exactly one file, exactly as it always has. It neither knows nor cares whether that file was generated — the override tier is merged at generation time, so there is no consumer-side change and no second file to keep in sync at validate time.

If the manifest is absent, the validator runs the structural checks (NodeId uniqueness) and emits a single `FUARAN900` Warning surfacing that the schema-coupled checks (`FUARAN010`, `FUARAN020`, `FUARAN030`, `FUARAN031`) were silenced for lack of a contract to gate against.

## What is derived, and from where

The load-bearing rule is **declaration site, never reference site**. A facet derived from the site the validator *checks* makes that check vacuous: derive `queries` from the tree's own `binding.query "n"` references and every reference resolves by construction — including the hallucinated one the check exists to catch.

| Facet | Derived from | Consequence |
|---|---|---|
| `queries` | the `BindingSources.QueryResults` construction site — the registration itself, including one hop through a `let`-bound map | `FUARAN010` stays fully live |
| `msgCases` | the `Msg` DU's own type declaration | `FUARAN020` stays fully live |
| `queryRowTypes` | the row-type annotation on each grid's `RowKey` / `OnRowClick` / `Columns` lambda | see below |
| `customNodeRatio` | *not derivable* — a policy threshold, not a fact about the source | override tier only |

### Query names

Recognised in any collection idiom (`Map.ofList`, `Map.ofSeq`, `dict`, `readOnlyDict`, a raw list, incremental `Map.add "name" …`), because the rule keys off the **shape** — the first element of a pair whose head is a string literal — rather than the constructor. The idiomatic two-step also resolves:

```fsharp
let queryMap =
    Map.ofList
        [ "totalRevenue", box { amount = 142_500.0 }
          "channelRows", box model.Channels ]

{ BindingResolver.empty with QueryResults = queryMap }   // ← followed one hop back to queryMap
```

The scope is narrow: only expressions reached from a `QueryResults` assignment are harvested, so a string-keyed pair elsewhere in the file is never mistaken for a query name.

### `Msg` case names

Collected from union type declarations named `Msg`, or ending in `Msg` (`AppMsg`, `GridMsg`). The rule is deliberately narrow rather than "every DU in the project": over-inclusion would admit unrelated case names into `msgCases` and weaken the very gate `FUARAN020` provides. Under-inclusion is the safe direction — it produces a loud, locatable `FUARAN020` that the override tier resolves.

Case **payload shapes** (`SelectRow of int` → `[ "int" ]`) are derived and printed in the emitter's summary, but deliberately **not** emitted: the v1 wire shape carries case names only, `MsgPayloadCheck` matches textually and reads nothing else, and adding an unconsumed key to a published wire shape costs every consumer a migration for no gate. When a check earns the data, the field is additive and minor-compatible.

### Row types — the honest exception

`queryRowTypes` is the one facet whose only in-source evidence *is* the annotation `FUARAN031` compares against. So under a generated manifest **`FUARAN031` cannot fire**. The defect it detects does not disappear; it moves **earlier**:

- Every annotated grid on a query agrees → an entry is emitted, and `FUARAN030` stops warning.
- Grids on one query annotate **different** row types → that is exactly the `FUARAN031` defect. The emitter reports it as a generation-time conflict and emits **no entry**, rather than picking a winner. `FUARAN030` then warns that the query is unverifiable, and it stays warning until the author fixes the source or asserts the row type in the override tier.
- No annotated grid reads the query → no entry, `FUARAN030` warns as before.

`FUARAN031` remains live for **asserted** override entries, which is where a hand-declared row type can genuinely disagree with an annotation.

## The override tier

`fuaran-validator.manifest.overrides.json` carries only what the walker cannot see: a query registered dynamically, a `Msg` DU that does not follow the naming rule, a policy knob like `customNodeRatio`. Same wire shape, partial — declare only the facets you are asserting.

```json
{
  "queries": ["auditTrail"],
  "queryRowTypes": { "auditTrail": "AuditRow" },
  "customNodeRatio": 0.12
}
```

**Precedence**, applied per facet by `Manifest.mergeOverrides`:

| Facet | Rule |
|---|---|
| `queries`, `msgCases` | **union**. An override adds names the walker cannot see; it can never *remove* a name the source demonstrably registers, because removing it would silently re-open the hole `FUARAN010` / `FUARAN020` exist to close. |
| `queryRowTypes` | per-key **override**. The asserted row type wins, so an author can pin a query whose in-source evidence is absent or ambiguous. |
| `customNodeRatio` | override wins when set. In practice this is the override tier's own field. |

**Provenance.** Every entry the override tier contributes is listed under `$generated.asserted` in the emitted manifest, so a reviewer can tell asserted from derived without diffing anything:

```json
{
  "$generated": {
    "by": "Fuaran.UI.Validator manifest emitter",
    "doNotEdit": "Derived from source. Regenerate with: Fuaran.UI.Validator emit-manifest <project.fsproj>. Hand-assert entries in fuaran-validator.manifest.overrides.json instead.",
    "asserted": {
      "queries": ["auditTrail"],
      "queryRowTypes": ["auditTrail"],
      "customNodeRatio": true
    }
  },
  "queries": ["auditTrail", "channelRows", "totalRevenue"],
  ...
}
```

An override entry the derivation now produces **identically** is not asserted — it is derived, and the override is redundant. The emitter says so in its summary and leaves the file alone: pruning someone's asserted contract is their call, not the tool's.

## The drift gate (`--check`)

`--check` derives, merges, renders, and compares against the committed manifest **without writing**. Exit `1` on drift, `0` otherwise — so it drops straight into CI.

The comparison is **semantic, not byte-wise**. What a gate must fail on is a contract that no longer describes the app, and only a semantic diff can name the entry responsible:

```
error: manifest drift — src/MyModule/fuaran-validator.manifest.json no longer describes the source:
  queries: "salesRows" is registered in source but missing from the committed manifest
  msgCases: "Reset" is in the committed manifest but no longer registered in source
regenerate with: Fuaran.UI.Validator emit-manifest src/MyModule/MyModule.fsproj
```

A file that is semantically current but not in canonical form — an older header, hand-written key order, a project mid-migration — is reported as a **note** and passes. Blocking a build over whitespace would teach developers to bypass the gate, and the contract in that file is correct.

### Canonical form

Pinned so byte-comparison across machines is meaningful: 2-space indent, **LF** line endings, one trailing newline, UTF-8 without BOM, `$generated` first, then `queries` / `msgCases` / `queryRowTypes` / `customNodeRatio`, every name ordinal-sorted. (`JsonWriterOptions` defaults its newline to `Environment.NewLine`, which would make a Windows emit and a Linux emit of the same source differ; the emitter pins it.)

### CLI

```
Fuaran.UI.Validator emit-manifest <project.fsproj> [--out PATH] [--overrides PATH] [--check]
```

| Flag | Effect |
|---|---|
| `--out PATH` | write somewhere other than the conventional sibling path (also the file `--check` compares against) |
| `--overrides PATH` | explicit override-tier path instead of convention-based discovery |
| `--check` | compare only; write nothing; exit 1 on drift or on a missing manifest |

Exit codes: `0` current, `1` drift, `2` malformed arguments / project not found.

## Migrating an existing hand-written manifest

1. Run the emitter with `--check`. It compares your hand-written file against what the source implies.
2. **Every drift line is a finding about your app, not about the tool.** A name reported as *registered in source but missing from the committed manifest* was a hole in your contract — the validator was not gating it. A name reported as *in the committed manifest but no longer registered in source* was a phantom — the validator was accepting a binding that could never resolve at runtime. Read them before regenerating.
3. Move anything the walker genuinely cannot see into `fuaran-validator.manifest.overrides.json` — start from the drift lines that survive step 2, plus your `customNodeRatio` if you set one.
4. Regenerate without `--check` and commit both files.
5. Wire `--check` into CI.

Nothing about the validator's own behaviour changes: it reads the same single file, in the same wire shape, and every check gates exactly as before.

## Wire shape

```json
{
  "queries": ["totalRevenue", "salesRows"],
  "msgCases": ["LoadData", "SelectRow", "Reset"],
  "queryRowTypes": {
    "salesRows": "SaleRow"
  }
}
```

### Fields

| Field | Type | Required | Purpose |
|---|---|---|---|
| `queries` | `string[]` | Recommended | Names accepted by `binding.query`. An unresolved `binding.query "foo"` reference where `"foo"` ∉ `queries` is **`FUARAN010` Error**. Absence silences `FUARAN010`. |
| `msgCases` | `string[]` | Recommended | `Msg` DU case names accepted by `Action.dispatch` / `Action.Dispatch`. A reference to a case not in this list is **`FUARAN020` Error**. Absence silences `FUARAN020`. |
| `queryRowTypes` | `{ string: string }` | Optional | Per-query short row-type name (e.g. `"SaleRow"`). Drives `FUARAN030` (missing entry → Warning) and `FUARAN031` (explicit lambda type annotation in `Fuaran.grid` differs from the declared row type → Error). |
| `customNodeRatio` | `number` | Optional | Project `NodeKind.Custom` ratio threshold (default `0.05`) above which `CustomHealthCheck` surfaces `FUARAN054`. Override-tier only — never derived. |
| `$generated` | `object` | Generated | Emitter provenance: `by`, `doNotEdit`, and (when the override tier contributed anything) `asserted`. Ignored by the parser — an additive key, so a generated manifest and a hand-written one are read identically. |

Trailing commas and `//` comments are tolerated; the parser is intentionally lenient on whitespace and case.

## What the validator does NOT infer

Unchanged, and worth restating precisely — the emitter derives the *contract*, it does not extend what the checks can reason about:

- The validator still does not type-check `msgCases` against the module's `Msg` DU at validate time. The check is textual – `Action.dispatch LoadData` matches against the string `"LoadData"`. The validator deliberately stops short of full FCS type-checker resolution per the Phase 12.V anti-pattern, and the **emitter honours the same line**: derivation is a set of narrow, documented syntactic rules over the untyped AST, never a type-checker pass. A rule that cannot see something says so — a conflict, or simply no entry — and the override tier absorbs it. The emitter never guesses.
- It does not infer row types from `binding.query` accessors. `queryRowTypes` is the only contract `RowTypeCheck` reasons against; a lambda parameter without a type annotation can't be cross-checked.

The contract surface is deliberately small so the manifest stays reviewable and the validator stays fast.

## Severity codes

| Code | Severity | Trigger |
|---|---|---|
| `FUARAN001` | Error | Duplicate `NodeId` within one `Fuaran.dashboard` subtree. |
| `FUARAN002` | Warning | Same `NodeId` appears across multiple trees. |
| `FUARAN010` | Error | `binding.query "name"` where `"name"` ∉ `queries`. Carries `available_fields` + best-guess `suggestion`. |
| `FUARAN020` | Error | `Action.dispatch (Case ...)` where `Case` ∉ `msgCases`. Carries `available_fields` + best-guess `suggestion`. |
| `FUARAN030` | Warning | `Fuaran.grid` with `Source = binding.query "name"` where `"name"` is not declared in `queryRowTypes`. Manifest gap, not a code defect. |
| `FUARAN031` | Error | `Fuaran.grid` lambda parameter (in `OnRowClick`, `RowKey`, or `Column.*`) annotated with a type that differs from `queryRowTypes["name"]`. |
| `FUARAN060` | Warning | `Node.withExtraAttribute "key" _` with a literal key that violates the data-* / aria-* allowlist OR matches a known dangerous attribute prefix (`on*` event handlers, `style`). Phase 56 render-time `Sanitize.sanitizeExtraAttributes` drops the entry; this is the build-time signal. |
| `FUARAN900` | Warning | No manifest file found beside the `.fsproj`. Schema checks silenced. |

## AI-recovery shape (§4d)

Findings with structural recovery information render their JSON form with two extra fields:

```json
{
  "severity": "error",
  "code": "FUARAN010",
  "file": "src/MyModule/Source.fs",
  "line": 14,
  "column": 32,
  "message": "Unresolved binding.query \"totalRevneu\" — name is not in the module's manifest queries list.",
  "available_fields": ["totalRevenue", "salesRows"],
  "suggestion": "totalRevenue"
}
```

Renderers select this shape via `--format json` on the CLI. The downstream AI consumer reads it directly to drive AI re-emission against the manifest as the typed-contract feedback channel. With a generated manifest the `available_fields` list is now the app's *actual* registration set rather than whatever was last hand-typed — the recovery hint is grounded for the same reason the gate is.

## Versioning

The format above is `v1`. Any future change that:

- renames a field,
- removes a field,
- changes a field's type (e.g. `queryRowTypes` becoming a richer structure),

ships a new major version of `Fuaran.UI.Validator` with a migration note in this document. Additive fields (a new optional top-level key, e.g. `$generated`) are minor-version-compatible – existing manifests keep working.

## See also

- Phase 12.V – Fuaran build-time validator (the AST walker the emitter reuses)
- [`Fuaran/CLAUDE.md`](../CLAUDE.md) – repo conventions (Fantomas mandate, Expecto-via-`dotnet run`)
- [`Fuaran/src/Fuaran.UI/Types.fs`](../src/Fuaran.UI/Types.fs) – the §4b record contract the validator gates against
