# Migration — the option and marker labels are `TextSource` (Phase 2811)

Part of the 0.105.0 announced breaking cut ([index](0.105.0-breaking-cut.md)).

## What changed

`SelectOption.Label` and `MapMarker.Label` are `TextSource` where they were `string`. The IDL row, the generated
record, the canonical encoder and the decoder now agree with WIRE_FORMAT §5's ruling (2026-10-19): an option or a
marker label can be `Literal`, `Bound` or `I18n`, like every other user-visible text.

On the wire nothing a document already holds moves. A literal label is still the bare JSON string, and a
`{"$type":"Literal","text":…}` envelope still decodes and re-encodes as that string. What is new is that a `Bound` or
an `I18n` label decodes, where the reference host used to refuse it with `WRONG_TYPE`, and that both renderers resolve
it like any other `TextSource`. A `Bound` label over a `State` binding with a default seeds its key (WIRE_FORMAT §24.4
rule 1).

## Who has to act

**F# code that builds a `SelectOption` or a `MapMarker` record.** Wrap a string label in `TextSource.Literal`:

```fsharp
// before
{ Value = "uk"; Label = "UK" }
{ Label = "London"; Latitude = 51.5; Longitude = -0.12 }

// after
{ Value = "uk"; Label = TextSource.Literal "UK" }
{ Label = TextSource.Literal "London"; Latitude = 51.5; Longitude = -0.12 }
```

**F# code that reads `.Label` as a string.** Resolve it, or match the literal arm:

```fsharp
// in a renderer, through the same resolver every other TextSource uses
BindingResolver.resolveTextSource sources option.Label

// where only literal labels can occur
match option.Label with
| TextSource.Literal text -> text
| other -> ...
```

**F# code that compares `SelectOption` or `MapMarker` values with `=`.** `TextSource` carries no structural equality
(its `Bound` arm holds a binding), so neither record supports `equality` any more. Compare the members you mean, for
example `option.Value` and the label's literal text.

**Renderer-component callers.** `TokensModel.tryAdd`, `TokensModel.visibleSuggestions` and `TokensModel.chipLabel` take
a first argument `textOf: TextSource -> string`, and the `TokensControl` and `ComboboxControl` props gain a `textOf`
member. Pass `BindingResolver.resolveTextSource sources`.

**C# and Visual Basic authors:** nothing. `Fuaran.UI.CSharp`'s option and marker tuples keep their `string` label and
write the literal form.

## Verifying

Decode
`{"id":"s","kind":{"$type":"Select","label":"Region","source":{"$type":"Static","value":[{"label":{"$type":"I18n","args":{},"key":"region.de"},"value":"de"}]},"value":{"$type":"Static","value":"de"}}}`
and re-encode it: the bytes are unchanged. Before this release the decode refused at `$.kind.source.value[0].label`.

## Rollback

Pin the previous release. A document that uses only literal labels reads there unchanged. A document with a `Bound` or
an `I18n` label is refused there, as it always was.
