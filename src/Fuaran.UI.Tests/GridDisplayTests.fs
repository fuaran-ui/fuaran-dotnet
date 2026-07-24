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

let private row: obj =
    Map.ofList [ "dept", nn "eng"; "amount", nn 100.0; "active", nn true ] |> box

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

          test "a non-Map row projects to Empty (no host data shape)" {
              Expect.equal (BindingResolver.projectRowFieldValue (nn "scalar") "dept") CellValue.Empty "non-map → Empty"
          } ]
