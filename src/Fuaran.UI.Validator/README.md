# Fuaran.UI.Validator

Build-time validator for Fuaran UI trees — an F# AST walker that gates `Fuaran.X "id" { ... }` author code against the schema contract in `fuaran-validator.manifest.json` before runtime. Surfaces structural defects (duplicate NodeIds, malformed Bindings, Action.Dispatch payloads against unknown Msg cases, RowType mismatches) as CLI findings with file:line locations, with `FUARAN9xx` warning + error codes mirroring the §4d AI-recovery error format.

## Usage

```
Fuaran.UI.Validator <project.fsproj> [--module-pattern SUBSTR]
                                   [--manifest PATH]
                                   [--format plain|json]
```

Exit codes: `0` on no Error-severity findings, `1` on at least one Error, `2` on malformed CLI arguments or missing project file. Warnings do not affect the exit code; an absent manifest emits `FUARAN900` (warning) and silences the schema-coupled checks. See `Fuaran/docs/VALIDATOR-MANIFEST.md` for the manifest schema; see `Fuaran/docs/TECHNICAL_GUIDE.md` for how the validator integrates with the wider Phase 12 pipeline.

Apache-2.0 licensed — see the repo [LICENSE](../../LICENSE).
