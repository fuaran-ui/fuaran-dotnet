# Fuaran.UI.Validator manifest format – v1

Module authors hand-write a small JSON file declaring the typed contract the build-time validator gates against. The file is the source of truth for everything the validator can't infer from untyped F# AST alone – query names, `Msg` DU case names, optional per-query row types.

This document is the public contract for the manifest's wire shape. Renames or removed fields are **semver-major breaking changes** to the `Fuaran.UI.Validator` package.

## File location

The validator looks for `fuaran-validator.manifest.json` next to the `.fsproj` it is given:

```
src/MyModule/
├── MyModule.fsproj
├── fuaran-validator.manifest.json   ← here
└── ...
```

If the file is absent, the validator runs the structural checks (NodeId uniqueness) and emits a single `FUARAN900` Warning surfacing that schema-coupled checks (`FUARAN010`, `FUARAN020`, `FUARAN030`, `FUARAN031`) were silenced for lack of a contract to gate against.

A future variant may let the `.fsproj` declare an explicit `<FuaranManifest>` item; v1 is convention-only.

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

Trailing commas and `//` comments are tolerated; the parser is intentionally lenient on whitespace and case.

## What the validator does NOT infer

- It does not parse the F# source to derive `queries` from the module's typed API surface. Module authors keep the manifest in sync by hand. (Inference is an optimisation; correctness depends on a stable, hand-written contract.)
- It does not type-check `msgCases` against the module's `Msg` DU. The check is textual – `Action.dispatch LoadData` matches against the string `"LoadData"`, regardless of what the DU actually declares. The validator deliberately stops short of full FCS type-checker resolution per the Phase 12.V anti-pattern.
- It does not infer row types from `binding.query` accessors. `queryRowTypes` is the only contract `RowTypeCheck` reasons against; a lambda parameter without a type annotation can't be cross-checked.

The contract surface is deliberately small so the manifest stays maintainable and the validator stays fast.

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

Renderers select this shape via `--format json` on the CLI. The downstream AI consumer reads it directly to drive AI re-emission against the manifest as the typed-contract feedback channel.

## Versioning

The format above is `v1`. Any future change that:

- renames a field,
- removes a field,
- changes a field's type (e.g. `queryRowTypes` becoming a richer structure),

ships a new major version of `Fuaran.UI.Validator` with a migration note in this document. Additive fields (a new optional top-level key) are minor-version-compatible – existing manifests keep working.

## See also

- Phase 12.V – Fuaran build-time validator (after shipping; while in flight: `roadmap/phases/12-V-fern-build-time-validator.md`)
- [`Fuaran/CLAUDE.md`](../CLAUDE.md) – repo conventions (Fantomas mandate, Expecto-via-`dotnet run`, no-`git push`)
- [`Fuaran/src/Fuaran.UI/Types.fs`](../src/Fuaran.UI/Types.fs) – the §4b record contract the validator gates against
