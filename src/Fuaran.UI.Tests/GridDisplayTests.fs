module Fuaran.UI.Tests.GridDisplay

// ============================================================================
//  Phase 425 — the DataGrid row-field projection contract. A decoded grid
//  column names a row property (`Field`) instead of a host closure; the
//  projection reads it off a `Map<string,obj>` row (the `Transform` shape) and
//  coerces it to a `CellValue`. Missing field → `Empty` (never a throw).
// ============================================================================

#nowarn "3261"

open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Renderer

let private nn (v: 'T) : obj = box v |> Unchecked.nonNull

/// The renderer's `Literal` reading (`renderText` / `textOf` both return the string
/// verbatim) — enough for the parity comparison below, which only ever builds
/// literals.
let private textOfLiteral (text: TextSource) : string =
    match text with
    | TextSource.Literal s -> s
    | other -> failwithf "the parity fixture builds only literals, got %A" other

let private row: Row =
    Map.ofList [ "dept", nn "eng"; "amount", nn 100.0; "active", nn true ]

[<Tests>]
let tests =
    testList
        "Phase 425 — row-field projection"
        [ test "a string field projects to CellValue.Text" {
              Expect.equal (BindingResolver.projectRowFieldValue row "dept") (CellValue.Text "eng") "string cell"
          }

          test "a numeric field projects to CellValue.Numeric" {
              Expect.equal (BindingResolver.projectRowFieldValue row "amount") (CellValue.Numeric 100.0) "numeric cell"
          }

          test "a bool field projects to CellValue.Bool" {
              Expect.equal (BindingResolver.projectRowFieldValue row "active") (CellValue.Bool true) "bool cell"
          }

          test "a missing field projects to CellValue.Empty (never a throw)" {
              Expect.equal (BindingResolver.projectRowFieldValue row "nope") CellValue.Empty "missing → Empty"
          }

          test "row-key projection stringifies the field" {
              Expect.equal (BindingResolver.projectRowFieldString row "dept") "eng" "row key from field"
          }

          // fuaran#665 — the "non-Map row" case is gone BY TYPE: `projectRowFieldValue`
          // takes `Row`, so an opaque host row can no longer reach it at all.
          ]

// ============================================================================
//  Phase 750 — TonedPill / Pill render parity.
//
//  The declarative pill exists to make a value-conditional tone EXPRESSIBLE, not
//  to render differently, so the acceptance bar is that it renders identically to
//  the hosted `Pill` an author would hand-write to mean the same thing. The client
//  renderer is Fable/React and produces no .NET DOM, so parity is asserted at the
//  seam both renderer arms actually consume: the (class, text) pair each builds.
//
//  `pillCellShape` reproduces those two arms verbatim — both spell
//  `sprintf "fuaran-grid-cell-pill fuaran-pill-%s" (Theme.toneVar tone)` and the
//  cell's text, and the arms differ ONLY in where the pair comes from. What this
//  test therefore proves is that the two derivations agree on every row class,
//  including the ones a lookup gets wrong: a mapped value, an unmapped value
//  (falls back to `default`), a value absent from the row, and a non-Map row. What
//  it does NOT prove is that the arms keep spelling the element the same way — the
//  shape function is a mirror, and a renderer edit that changed one arm's markup
//  would have to change this file too. That residue is the reason both arms route
//  through one shared `tonedPillOf` rather than each doing its own lookup.
// ============================================================================

/// Exactly what both `CellKindErased.Pill` and `CellKindErased.TonedPill` renderer
/// arms build: the pill's class name and its text.
let private pillCellShape (label: string) (tone: ToneVariant) : string * string =
    sprintf "fuaran-grid-cell-pill fuaran-pill-%s" (Theme.toneVar tone), label

let private statusToneMap =
    Map
        [ "On time", ToneVariant.Success
          "Delayed", ToneVariant.Warning
          "Cancelled", ToneVariant.Critical ]

let private statusDefault = ToneVariant.Subdued

/// The hosted `Pill` an author writes to mean what `TonedPill("status", …)` means:
/// the row field's text as the label, the map lookup as the tone. Spelled as the
/// two closures the case actually carries, so the comparison is against the REAL
/// hosted shape rather than against a paraphrase of it.
let private hostedEquivalent: (Row -> TextSource) * (Row -> ToneVariant) =
    (fun (r: Row) -> TextSource.Literal(BindingResolver.projectRowFieldString r "status")),
    (fun (r: Row) ->
        statusToneMap
        |> Map.tryFind (BindingResolver.projectRowFieldString r "status")
        |> Option.defaultValue statusDefault)

let private statusRow (v: string) : Row =
    Map.ofList [ "id", nn "SHP-1"; "status", nn v ]

[<Tests>]
let tonedPillParityTests =
    let parityFor (description: string) (row: Row) =
        test description {
            let labelFn, toneFn = hostedEquivalent
            let hosted = pillCellShape (textOfLiteral (labelFn row)) (toneFn row)

            let declarativeLabel, declarativeTone =
                GridModel.tonedPillOf row "status" statusToneMap statusDefault

            let declarative = pillCellShape declarativeLabel declarativeTone
            Expect.equal declarative hosted "the declarative pill's (class, text) must equal the hosted pill's"
        }

    testList
        "Phase 750 — TonedPill renders identically to the equivalent hosted Pill"
        [ parityFor "a mapped value tones from the map" (statusRow "Delayed")
          parityFor "another mapped value tones from the map" (statusRow "On time")
          // The interesting one: nothing in the map matches, so both sides must reach
          // the SAME fallback. A lookup that defaulted to `ToneVariant.Default` on one
          // side would pass every mapped case and fail only here.
          parityFor "an unmapped value falls back to the declared default" (statusRow "Held at customs")
          parityFor "an empty value is a lookup miss, not a crash" (statusRow "")
          // A row with no `status` property at all: `projectRowFieldString` yields "",
          // so both sides take the fallback rather than throwing.
          parityFor "a row missing the driving field falls back" (Map.ofList [ "id", nn "SHP-1" ])
          // fuaran#665 — the "non-Map row" parity case is gone BY TYPE (see above).

          test "an explicit map entry beats the default even when the default is louder" {
              let _, tone =
                  GridModel.tonedPillOf (statusRow "On time") "status" statusToneMap ToneVariant.Critical

              Expect.equal tone ToneVariant.Success "the map wins over `default` for a mentioned value"
          }

          test "the tone map keys on the field's TEXT, so a numeric field maps by its canonical string" {
              let row: Row = Map.ofList [ "code", nn 3.0 ]

              let _, tone =
                  GridModel.tonedPillOf row "code" (Map [ "3", ToneVariant.Info ]) ToneVariant.Default

              Expect.equal tone ToneVariant.Info "numeric field keys by `projectRowFieldString`"
          } ]

// ============================================================================
//  Phase 2006 — the row value gains an exact decimal.
//
//  A decimal column reaches a `Row` as a `DecimalCell` (the typed path keeps the
//  type the Transform schema gave it), projects to `CellValue.Decimal`, sorts in
//  exact numeric order, formats from its digits, and plots at the one
//  screen-coordinate conversion. The fixtures below pin the sorted order and the
//  formatted text byte for byte; the TypeScript host's twin fixture pins the
//  same bytes.
// ============================================================================

/// The fixture column: canonical decimal text, chosen so lexical order, float
/// order and exact order all disagree somewhere. The last two differ only past
/// a double's precision (both are the same double).
let private decimalTexts =
    [ "9"
      "10"
      "-2.5"
      "100.25"
      "0.001"
      "12345678901234567.89"
      "12345678901234567.88" ]

let private decimalTable: Fuaran.Core.Table =
    { Schema = [ Fuaran.Core.Field.create "amount" Fuaran.Core.DecimalType ]
      Columns =
        [ Fuaran.Core.Column.ofCells
              "amount"
              Fuaran.Core.DecimalType
              (decimalTexts |> List.map Fuaran.Core.Cell.Decimal)
          |> Result.defaultWith (fun e -> failwithf "the fixture column must build: %A" e) ] }

/// The rows a `Binding.Transform` over the fixture table resolves to — the typed path.
let private decimalRows () : Row list =
    match
        BindingResolver.resolve<Row seq>
            BindingResolver.empty
            (Binding.Transform(TransformSource.Data(Fuaran.Core.Embedded decimalTable), [], None))
    with
    | BindingResolver.Resolved rows -> List.ofSeq rows
    | other -> failwithf "the fixture Transform must resolve, got %A" other

let private amountColumn: ColumnErased<obj> =
    { Fuaran.UI.Defaults.columnErased with
        Label = "Amount"
        Field = Some "amount" }

let private amounts (rows: Row list) : string list =
    rows |> List.map (fun r -> BindingResolver.projectRowFieldString r "amount")

let private decimalCell (text: string) : obj =
    nn ({ Text = text }: Fuaran.UI.HostPrelude.DecimalCell)

[<Tests>]
let decimalRowValueTests =
    testList
        "Phase 2006 — CellValue.Decimal"
        [ test "a Transform over a decimal column yields CellValue.Decimal carrying the canonical text" {
              let projected =
                  decimalRows ()
                  |> List.map (fun r -> BindingResolver.projectRowFieldValue r "amount")

              Expect.equal projected (decimalTexts |> List.map CellValue.Decimal) "every cell is an exact Decimal"
          }

          test "a bare string in a row stays Text: an untyped feed is never guessed at" {
              let row: Row = Map.ofList [ "amount", nn "12.50" ]
              Expect.equal (BindingResolver.projectRowFieldValue row "amount") (CellValue.Text "12.50") "Text"
          }

          test "the grid sorts a decimal column in exact numeric order, ascending, byte for byte" {
              let sorted =
                  GridModel.sortRowsByDescriptor [ amountColumn ] (Some(0, SortDirection.Asc)) (decimalRows ())

              Expect.equal
                  (amounts sorted)
                  [ "-2.5"
                    "0.001"
                    "9"
                    "10"
                    "100.25"
                    "12345678901234567.88"
                    "12345678901234567.89" ]
                  "never lexical ('10' before '9'), never via a float (the last two are one double)"
          }

          test "and descending is the exact reverse" {
              let sorted =
                  GridModel.sortRowsByDescriptor [ amountColumn ] (Some(0, SortDirection.Desc)) (decimalRows ())

              Expect.equal
                  (amounts sorted)
                  [ "12345678901234567.89"
                    "12345678901234567.88"
                    "100.25"
                    "10"
                    "9"
                    "0.001"
                    "-2.5" ]
                  "descending"
          }

          test "CellFormat renders a decimal from its digits, byte for byte" {
              let f format text =
                  Formatting.formatCellValue format (CellValue.Decimal text)

              Expect.equal (f CellFormat.None "100.25") "100.25" "None is the canonical text"
              Expect.equal (f (CellFormat.Number None) "10") "10" "Number None, integral"
              Expect.equal (f (CellFormat.Number None) "0.001") "0.001" "Number None, fractional"

              Expect.equal
                  (f (CellFormat.Number(Some 2)) "12345678901234567.895")
                  "12345678901234567.90"
                  "past a double's precision"

              Expect.equal (f (CellFormat.Number(Some 2)) "-0.001") "-0.00" "the sign survives, as toFixed's does"
              Expect.equal (f (CellFormat.Number(Some 0)) "2.5") "3" "half up"
              Expect.equal (f (CellFormat.Currency "GBP") "1234.5") "GBP 1234.50" "currency"

              Expect.equal
                  (f (CellFormat.Currency "GBP") "99999999999999999.995")
                  "GBP 100000000000000000.00"
                  "a carry through every digit"

              Expect.equal (f (CellFormat.Percent None) "0.0625") "6.3%" "percent, one place"
              Expect.equal (f (CellFormat.Percent(Some 2)) "0.123456") "12.35%" "percent, two places"
              Expect.equal (f (CellFormat.SignificantDigits 3) "123456") "123000" "significant digits"
              Expect.equal (f (CellFormat.SignificantDigits 2) "0.00012345") "0.00012" "small significant digits"
          }

          test "where the float is exact, a decimal formats exactly as the float does" {
              let formats =
                  [ CellFormat.None
                    CellFormat.Number None
                    CellFormat.Number(Some 0)
                    CellFormat.Number(Some 3)
                    CellFormat.Currency "USD"
                    CellFormat.Percent None
                    CellFormat.Percent(Some 2)
                    CellFormat.SignificantDigits 3 ]

              for text, value in [ "1234.5", 1234.5; "-0.25", -0.25; "10", 10.0; "0.125", 0.125 ] do
                  for format in formats do
                      Expect.equal
                          (Formatting.formatCellValue format (CellValue.Decimal text))
                          (Formatting.formatNumber format value)
                          (sprintf "%s under %A" text format)
          }

          test "a decimal series plots the exact values, at the one screen-coordinate conversion" {
              let floatRows: Row list =
                  [ "a", 12.5; "b", 3.25; "c", 100.0 ]
                  |> List.map (fun (k, v) -> Map.ofList [ "k", nn k; "v", nn v ])

              let decimalRows: Row list =
                  [ "a", "12.5"; "b", "3.25"; "c", "100" ]
                  |> List.map (fun (k, v) -> Map.ofList [ "k", nn k; "v", decimalCell v ])

              let spec: ChartSpec<obj> =
                  { Fuaran.UI.Defaults.chart with
                      Kind = ChartKind.Bar
                      XField = "k"
                      YFields = [ "v" ] }

              let drawn (rows: Row list) : string =
                  Fuaran.UI.Fuaran.drawingSpec "c" (Fuaran.UI.Charts.lower spec (Seq.ofList rows))
                  |> Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode

              let zeroRows: Row list =
                  [ "a"; "b"; "c" ] |> List.map (fun k -> Map.ofList [ "k", nn k; "v", nn 0.0 ])

              Expect.equal
                  (drawn decimalRows)
                  (drawn floatRows)
                  "the decimal bars are the float bars of the same values"

              Expect.notEqual (drawn decimalRows) (drawn zeroRows) "and not the flat zero bars main drew"
          }

          test "the row codec writes a decimal as its canonical text: the wire bytes are unchanged" {
              let typed: Row = Map.ofList [ "amount", decimalCell "12.5" ]
              let bare: Row = Map.ofList [ "amount", nn "12.5" ]

              Expect.equal
                  (Fuaran.UI.RowCodec.encodeRows [ typed ])
                  (Fuaran.UI.RowCodec.encodeRows [ bare ])
                  "same bytes"
          }

          test "a selected decimal field reads as its text in a text slot and its number in a numeric one" {
              let row = box (List.head (decimalRows ()))

              Expect.equal (Binding.projectSelectionField<string> "amount" row) "9" "text slot"
              Expect.equal (Binding.projectSelectionField<float> "amount" row) 9.0 "numeric slot"
          }

          test "a numeric sink accepts a decimal column" {
              Expect.isTrue
                  (Fuaran.UI.QueryBinding.compatible
                      Fuaran.Core.DecimalType
                      Fuaran.UI.QueryBinding.BindingSinkClass.Numeric)
                  "DecimalType -> Numeric"
          } ]
