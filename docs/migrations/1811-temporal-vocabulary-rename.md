# Phase 1811 — the temporal vocabulary says what it is

`Fuaran.UI` 0.86.0 (riding the draft slot; operator ruling `DECISIONS.md` D8). **Breaking** on the F#
surface and on the wire: four `$type` discriminators and two type names are renamed. Every host in
the wire format's §11.0 roster moved in the same change-set, and the old spellings survive only as
§16 lenient-ingest aliases — so a stored document still decodes, and re-encodes to the new names.

## What changes

| Was | Is | On the wire |
|---|---|---|
| `FormFieldKind.Date` | `FormFieldKind.DateTime` | `"$type":"Date"` → `"DateTime"` |
| `FormFieldKind.DateRange` | `FormFieldKind.DateTimeRange` | `"$type":"DateRange"` → `"DateTimeRange"` |
| `DateVariant` | `DateTimeVariant` (cases `Date` / `Time` / `DateTime` unchanged) | no — a type name |
| `DateRangePair` | `DateTimeRangePair` (`From` / `To` unchanged) | no — a type name |
| `Format.Date` | `Format.DateTime` | `"$type":"Date"` → `"DateTime"` inside a `Format` binding |
| `CellFormat.Date` | `CellFormat.DateTime` | `"$type":"Date"` → `"DateTime"` at a `format` slot |
| `DateStyle` | **unchanged** | — |

Not renamed, and deliberately: `CellKind.Date` (a data-cell kind) and `ChartAnnotationX.Date` (an
ISO address) are different families that happen to share a spelling.

## Diff per file

**F# authoring code** — a token rename; the associated values did not move:

```diff
-FormFieldKind.Date(value, onChange, DateVariant.Time, None, None, None)
+FormFieldKind.DateTime(value, onChange, DateTimeVariant.Time, None, None, None)
-FormFieldKind.DateRange(Some pair, None, DateVariant.Date, min, max, step)
+FormFieldKind.DateTimeRange(Some pair, None, DateTimeVariant.Date, min, max, step)
-let stay : Binding<DateRangePair> = …
+let stay : Binding<DateTimeRangePair> = …
-Format.Date(Some DateStyle.Medium, Some TimeStyle.Short)
+Format.DateTime(Some DateStyle.Medium, Some TimeStyle.Short)
-CellFormat.Date "yyyy-MM-dd"
+CellFormat.DateTime "yyyy-MM-dd"
```

Smart constructors follow the cases: `FormFieldKind.date` → `dateTime`, `dateRange` → `dateTimeRange`,
`dateDeclarative` → `dateTimeDeclarative`, `dateRangeDeclarative` → `dateTimeRangeDeclarative`, the
filter chip `dateRange` → `dateTimeRange`, `CellFormat.date` → `CellFormat.dateTime`, and
`ControlValueDefaults.date` / `dateRange` → `dateTime` / `dateTimeRange`. `Format.date`,
`Format.dateTime` and `Format.time` keep their names — they name the presentation, not the case.

**C#** — one factory: `CellFormat.Date(fmt)` → `CellFormat.DateTime(fmt)`. `LocaleFormat.Date` /
`DateTime` / `Time` are unchanged. **VB XML** — one attribute: `format-date="…"` → `format-date-time="…"`.

**JSON documents you emit** (canonical form; the old spellings still decode):

```diff
-{"$type":"Date","value":{"$type":"Static","value":"08:30"},"variant":"Time"}
+{"$type":"DateTime","value":{"$type":"Static","value":"08:30"},"variant":"Time"}
-{"$type":"Format","format":{"$type":"Date","timeStyle":"Short"},…}
+{"$type":"Format","format":{"$type":"DateTime","timeStyle":"Short"},…}
```

**Data that carries the names** — evaluation seeds, cookbook emission trees, stored fixtures — is
**re-emitted by its owner** through the encoder, not search-replaced: `"Date"` is also a
`CellKind`, an `enum` case and an annotation address, and a blind replace would rename those.

## Verification

1. `dotnet build` — every FS0764 / incomplete-match error names a site in the table above.
2. Decode a document that still spells `Date` / `DateRange`: it must decode and re-encode with the
   new names, byte-identical to `wire-format-fixtures/lenient/lenient-1811-*.expected.json`.
3. `{"$type":"Time"}` with no `variant` decodes to `DateTime` + `variant:"Time"`; with
   `"variant":"Date"` beside it the decoder refuses (`WRONG_TYPE` at `…variant`).
4. `pwsh ./run.ps1` — the corpus round-trip, lenient-accept and reject walks are green.

## Rollback

Revert the consuming commit and pin `Fuaran.UI` at 0.85.x (tagged). A document written with the
new names is read by a 0.85.x reader as a preserved `Unknown` (§15.3) — rendered as a placeholder or
its author-declared `fallback`, never refused and never destroyed.
