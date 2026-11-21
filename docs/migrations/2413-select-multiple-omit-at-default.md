# Migration — `Select.multiple` is omitted at its default (Phase 2413)

Part of the 0.105.0 announced breaking cut ([index](0.105.0-breaking-cut.md)).

## What changed

`Select.multiple` joins the canonical form's one rule (WIRE_FORMAT §3.6 rule 1): a field with a declared default is
absent when it holds that default. Its IDL row now declares `omitDefault false`, exactly as `FileUpload.multiple`'s
does, so:

- the canonical encoding of a `Select` never writes `"multiple":false`;
- a document that carries `"multiple":false` still decodes, as a single-select, and re-encodes **without** the member
  (a §16 lenient accept, pinned by the corpus vector `lenient/lenient-2413-select-multiple-false.json`);
- the generated record field `Fuaran.UI.Generated.SelectSpec.Multiple` is a `bool` (default `false`) where it was a
  `bool option`.

Phase 1962 had made the field "an ordinary optional field, emitted as authored", which left it the one boolean
outside the canonical rule. A document that never wrote `"multiple":false` has the same bytes as before.

## Who has to act

**F# code that builds or reads a `SelectSpec` record directly.** The field is a plain `bool` now:

```fsharp
// before
{ Defaults.select with Multiple = Some true; Values = Some values; Value = None }
if spec.Multiple = Some true then ...

// after
{ Defaults.select with Multiple = true; Values = Some values; Value = None }
if spec.Multiple then ...
```

`Multiple = None` and `Multiple = Some false` both become `Multiple = false` (or drop the line: `Defaults.select`
already holds `false`). Code that builds through `Fuaran.select` / `Fuaran.multiSelect` needs no edit.

**C# and Visual Basic authors:** nothing. `Fuaran.UI.CSharp.Select` / `MultiSelect` take no `Multiple` option.

**Anything that stores or compares canonical bytes** of a `Select` that carried an explicit `false`: re-encode the
stored document once. Its decoded value is unchanged; only the spelling is.

## Verifying

Decode `{"id":"s","kind":{"$type":"Select","label":"Size","multiple":false,"source":{"$type":"Static","value":[]},"value":{"$type":"Static"}}}`
and re-encode it: the result carries no `multiple` member.

## Rollback

Pin the previous release. Documents written by this release decode there unchanged, because an absent `multiple`
has always meant a single-select.
