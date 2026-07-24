# Fuaran.UI.Analyzers

Roslyn compile-time diagnostics for [`Fuaran.UI.CSharp`](../Fuaran.UI.CSharp) authoring
— the FUARAN* rules the F# `Fuaran.UI.Validator` can't reach across the C# boundary,
rebuilt as a `DiagnosticAnalyzer` over the language-neutral `IOperation` tree (so it
serves C# and, from Phase 315, VB with the same logic).

## Rules

| Code | Severity | Fires on |
|---|---|---|
| **FUARAN001** | Error | A duplicate `Id` literal across a code block — every NodeId in a Fuaran tree must be unique (§4g op-target stability). |
| **FUARAN010** | Warning | A `Binding.Query("name")` whose name is not in the manifest's `queries` list (silent when no manifest is wired). |

## Wiring

Add the analyzer package and point it at your validator manifest:

```xml
<ItemGroup>
  <PackageReference Include="Fuaran.UI.Analyzers" PrivateAssets="all" />
  <AdditionalFiles Include="fuaran-validator.manifest.json" />
</ItemGroup>
```

```ini
# .editorconfig
[*.cs]
fuaran_manifest_path = fuaran-validator.manifest.json
```

The manifest is the same `fuaran-validator.manifest.json` the F# validator reads
(`{ "queries": [...], "msgCases": [...] }`), so the query registry is shared across
hosts.
