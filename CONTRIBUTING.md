# Contributing to Fuaran (language tier)

Fuaran's language tier is licensed **Apache-2.0** (see [`LICENSE`](LICENSE)). Contributions are
welcome under the same licence. The conventions below keep the tree green and the wire format stable.

## Contribution licensing — Developer Certificate of Origin

Every commit must be signed off under the [Developer Certificate of Origin 1.1](https://developercertificate.org/)
to certify you have the right to contribute the code under Apache-2.0. Add a `Signed-off-by:` trailer
to each commit:

```
git commit -s -m "feat: your change"
```

A pull request without DCO sign-off on every commit will not be merged.

## Code conventions

- **F# style, MVU discipline, and the renderer's portability rules** apply.
- **Repo-specific rules** — see [`CLAUDE.md`](CLAUDE.md).

Per-commit hard requirements:

1. **Fantomas formatting** — run `dotnet fantomas .` from the repo root before every commit.
   Unformatted F# is not mergeable (`Directory.Build.props` treats it as a hard gate).
2. **`dotnet build Fuaran.sln` is clean** — zero warnings, zero errors
   (`Directory.Build.props` sets `TreatWarningsAsErrors=true`).
3. **Tests pass** — `dotnet run --project Build.fsproj -- Test`. **Do not** use `dotnet test` —
   the Expecto console runner silently no-ops under `dotnet test`.
4. **Wire-format forward-coupling** — adding or changing a `NodeKind` / `Spec` / `TreeOp` /
   `Binding` / `Action` case updates the encoder, the decoder, and the
   [`wire-format-fixtures/`](../wire-format-fixtures) corpus in the **same** change
   (see [`WIRE_FORMAT.md`](../wire-format-fixtures/WIRE_FORMAT.md) §11). The corpus is the cross-host parity gate.
5. **API-stability impact declared** — if your change touches a surface listed as stable in
   [`STABILITY.md`](STABILITY.md), declare the impact in the PR description.

## Pull request flow

1. Branch from `main` with a descriptive name (`feat/<short-name>`, `fix/<short-name>`,
   `docs/<short-name>`).
2. Make focused, DCO-signed commits. Group related changes; do not bundle unrelated cleanups.
3. Run the per-commit hard requirements above.
4. Open a PR describing the change and its wire-format / API-stability impact.
5. A maintainer reviews and merges.
