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
6. **Spec records are constructed with `{ Defaults.X with … }`** — see below.

## Constructing spec records

Build a spec record by record-updating its default, naming only the fields that
differ from it:

```fsharp
{ Defaults.image with
    Src = Binding.Static(Some "/harbour.jpg")
    Alt = TextSource.Literal "Fishing boats moored at first light" }
```

**Why this is a rule and not a preference.** Spec records grow additively: a new
slot ships with an identity value in
[`Defaults.fs`](src/Fuaran.UI/Defaults.fs), and every document that omits it
decodes exactly as before. That is the design, and on the wire it holds. In F#
source it holds only for the `with` form — a *full* record literal must name
every field, so it fails to compile (FS0764) the moment the record gains one.
When a repo is full of such literals, an additive wire slot becomes a
source-breaking change and the churn lands on whoever added the field. The
`with` form mentions no absent slot and so needs no edit at all.

**Full literals stay correct where a record is RECONSTRUCTED rather than
authored** — a wire decoder, or generated codec code. There FS0764 is the safety
net that makes a new slot impossible to forget, so library sources are
deliberately outside the rule.

`Fuaran.UI.Tests`'s `SpecConstruction` suite enforces this at the **authoring
sites** — every `*.Tests` project, `samples/` and `benchmarks/` — and fails
naming the file, the line and the `Defaults` value to start from. The governed
spec set is derived from `Defaults.fs`, so a record is governed as soon as it
has a default.

If a literal's full-ness genuinely is the assertion — a test that exists to
catch field additions — declare it on a comment line just above the literal:

```fsharp
// FULL-LITERAL(ImageSpec): this literal exists to break when the record grows.
{ Src = …
  Alt = … }
```

The marker names the spec so it cannot drift onto a neighbouring literal and go
on silencing something else.

## Pull request flow

1. Branch from `main` with a descriptive name (`feat/<short-name>`, `fix/<short-name>`,
   `docs/<short-name>`).
2. Make focused, DCO-signed commits. Group related changes; do not bundle unrelated cleanups.
3. Run the per-commit hard requirements above.
4. Open a PR describing the change and its wire-format / API-stability impact.
5. A maintainer reviews and merges.
