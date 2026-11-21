# Migration: `AgAdapterOptions` drops its type parameter (Phase 2835)

**Class:** breaking (rides the 0.105.0 slot). Package `Fuaran.UI.Renderer`, module `AgAdapter`.

## What changes

- `AgAdapterOptions<'Msg>` becomes `AgAdapterOptions`. The parameter was never used by any field.
- `defaultOptions<'Msg>` becomes the value `defaultOptions`.
- `AgVisualisationAdapter<'Msg>` and `adapterWith<'Msg>` keep their own `'Msg` and take the non-generic record.

## Edit at each site

```fsharp
// before
let opts: AgAdapter.AgAdapterOptions<Msg> = { AgAdapter.defaultOptions<Msg> with OnGridReady = Some f }
let adapter = AgAdapter.adapterWith<Msg> opts

// after
let opts: AgAdapter.AgAdapterOptions = { AgAdapter.defaultOptions with OnGridReady = Some f }
let adapter = AgAdapter.adapterWith<Msg> opts
```

Drop the type argument from every annotation and from every `defaultOptions<…>` call site. Code that wrote
`{ AgAdapter.defaultOptions with … }` without an argument needs no change.

## Verify

Rebuild: a leftover `AgAdapterOptions<Msg>` fails with "type is not generic" and `defaultOptions<Msg>` with
"value is not a type function". Nothing else changes: the hooks, their order and the isolation property
(`Fuaran.UI.Tests.AdapterOptionsIsolation`) are as before.

## Rollback

Restore the type parameter on the record and `defaultOptions`; no data or wire byte is involved.
