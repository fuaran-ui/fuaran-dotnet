# Fuaran.UI.Validator

Build-time validator for Fuaran UI trees — an F# AST walker that gates `Fuaran.X "id" { ... }` author code against the schema contract in `fuaran-validator.manifest.json` before runtime. Surfaces structural defects (duplicate NodeIds, malformed Bindings, Action.Dispatch payloads against unknown Msg cases, RowType mismatches) as CLI findings with file:line locations, with `FUARAN9xx` warning + error codes mirroring the §4d AI-recovery error format.

## Usage

```
Fuaran.UI.Validator <project.fsproj> [--module-pattern SUBSTR]
                                   [--manifest PATH]
                                   [--format plain|json]
```

Exit codes: `0` on no Error-severity findings, `1` on at least one Error, `2` on malformed CLI arguments or missing project file. Warnings do not affect the exit code; an absent manifest emits `FUARAN900` (warning) and silences the schema-coupled checks. See `Fuaran/docs/VALIDATOR-MANIFEST.md` for the manifest schema; see `Fuaran/docs/TECHNICAL_GUIDE.md` for how the validator integrates with the wider Phase 12 pipeline.

## Suppressing a finding

Some source deliberately holds a shape the validator is right to reject in application code — most often a **negative test**, whose whole purpose is to construct the defect and assert the runtime reports it. There the finding is correct about the code and wrong about the intent, and no edit fixes it without destroying the test. Two comment pragmas opt a source out:

```fsharp
// fuaran-validator: disable FUARAN047, FUARAN048 — negative-test fixtures
```

File-scoped: suppresses the listed codes anywhere in the file, wherever the comment sits (convention: beside the module's doc comment, so a reader meets it before the fixtures).

```fsharp
// fuaran-validator: disable-next-line FUARAN044
```

Line-scoped: suppresses the listed codes on the **following** source line only — the precise form, for a single exceptional call site in a file that should otherwise stay gated.

Any text after the codes is free prose, so a pragma carries its justification on the same line. A pragma naming a code that never fires is inert, and a pragma naming no code at all suppresses nothing (a bare `disable` is a typo, not a blanket). Suppression applies to warnings as well as errors, and filters only the reporting layer — every check still runs. Suppressed findings are counted in the run summary (`… 0 error(s), 3 warning(s), 2 suppressed, …`) so a silenced rule stays visible in the build log.

Apache-2.0 licensed — see the repo [LICENSE](../../LICENSE).
