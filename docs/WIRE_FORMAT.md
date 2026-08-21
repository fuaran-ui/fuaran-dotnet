# Fuaran wire format – moved

The canonical, language-neutral wire-format specification lives in the
**`fuaran-specification`** repository, alongside the JSON Schema, the generated
render-fidelity manifest (`render-fidelity.json` — spec §13), and the executable
conformance corpus it is certified by:

- In a side-by-side workspace checkout: [`../../wire-format-fixtures/WIRE_FORMAT.md`](../../wire-format-fixtures/WIRE_FORMAT.md)
- Public home: `https://github.com/fuaran-ui/fuaran-specification`

This repo (the F# tier, published as `fuaran-dotnet`) is one **conformant host** of that contract – the
reference implementation. Its encoder/decoder and the corpus-regeneration flow are unchanged:

```
dotnet run --project src/Fuaran.UI.JsonDecode.Tests -- --emit-corpus ..\wire-format-fixtures
```
