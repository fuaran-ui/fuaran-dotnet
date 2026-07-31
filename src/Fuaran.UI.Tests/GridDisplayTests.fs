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
                BindingResolver.tonedPillOf row "status" statusToneMap statusDefault

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
                  BindingResolver.tonedPillOf (statusRow "On time") "status" statusToneMap ToneVariant.Critical

              Expect.equal tone ToneVariant.Success "the map wins over `default` for a mentioned value"
          }

          test "the tone map keys on the field's TEXT, so a numeric field maps by its canonical string" {
              let row: Row = Map.ofList [ "code", nn 3.0 ]

              let _, tone =
                  BindingResolver.tonedPillOf row "code" (Map [ "3", ToneVariant.Info ]) ToneVariant.Default

              Expect.equal tone ToneVariant.Info "numeric field keys by `projectRowFieldString`"
          } ]
