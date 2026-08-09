module Fuaran.UI.JsonDecode.Tests.ProgressFieldCellTests

// ============================================================================
//  Phase 425 / cat:Fuaran.UI.ProgressFieldCell — the field-driven Progress
//  grid cell (2026-08-10).
//
//  Before this, EVERY wire-decoded `Progress` cell rendered a zero fill
//  regardless of the data (the fraction slot is closure-typed and decoded to
//  an inert placeholder) — silently WRONG rather than failing, and
//  repair-proof from the model side (the eval suite's
//  627-repair-escalation-20260809.md carries the rubric-demanded evidence).
//  Now the column-level `field` (the Phase 425 core) drives a synthesized
//  per-row projection: clamp 0..1, missing / non-numeric → 0, never a throw.
//  No new wire key; closure-authored grids never pass through decode.
// ============================================================================

open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Ops.JsonDecode

/// The 005-emission shape: an embedded columnar source + a field-carrying
/// Progress column (exactly the intent the escalation found inexpressible).
let private gridJson (progressColumn: string) =
    """{"id":"g","kind":{"$type":"DataGrid","rowKeyField":"name","columns":["""
    + progressColumn
    + """],"source":{"$type":"Transform","pipeline":[],"source":{"columns":{"name":{"values":["Alpha","Beta"],"validity":[true,true]},"capacity":{"values":[0.9,0.5],"validity":[true,true]}}}}},"state":{},"style":{"emphasis":"Normal","tone":"Default","weight":"Standard"}}"""

let private decodeProgressFraction (progressColumn: string) : (Row -> float) =
    match decodeNodeObj (gridJson progressColumn) with
    | Error e -> failtestf "expected the grid to decode: %s at %s: %s" e.Code e.Path e.Message
    | Ok node ->
        match node.Kind with
        | NodeKind.DataGrid spec ->
            match spec.Columns with
            | [ col ] ->
                match col.Kind with
                | CellKindErased.Progress(fraction, _) -> fraction
                | other -> failtestf "expected a Progress cell kind, got %A" other
            | cols -> failtestf "expected exactly one column, got %d" cols.Length
        | other -> failtestf "expected a DataGrid, got %A" other

let private row (pairs: (string * obj) list) : Row = Map.ofList pairs

let private cell (v: 'a) : obj = box v |> Unchecked.nonNull

[<Tests>]
let progressFieldCellTests =
    testList
        "Phase 425 — field-driven Progress grid cell"
        [ testCase "a field-carrying Progress column projects the row's fraction"
          <| fun () ->
              let fraction =
                  decodeProgressFraction """{"field":"capacity","label":"Capacity","kind":{"$type":"Progress"}}"""

              Expect.floatClose Accuracy.high (fraction (row [ "capacity", cell 0.9 ])) 0.9 "0.9 projects as 0.9"
              Expect.floatClose Accuracy.high (fraction (row [ "capacity", cell 3 ])) 1.0 "an int > 1 clamps to 1.0"
              Expect.floatClose Accuracy.high (fraction (row [ "capacity", cell -0.2 ])) 0.0 "negative clamps to 0.0"

          testCase "missing or non-numeric row values project 0 — never a throw"
          <| fun () ->
              let fraction =
                  decodeProgressFraction """{"field":"capacity","label":"Capacity","kind":{"$type":"Progress"}}"""

              Expect.floatClose Accuracy.high (fraction (row [ "other", cell 0.5 ])) 0.0 "missing field is 0"
              Expect.floatClose Accuracy.high (fraction (row [ "capacity", cell "high" ])) 0.0 "text value is 0"

          testCase "a fieldless Progress column keeps the inert placeholder (unchanged behaviour)"
          <| fun () ->
              let fraction =
                  decodeProgressFraction """{"label":"Capacity","kind":{"$type":"Progress"}}"""

              Expect.floatClose Accuracy.high (fraction (row [ "capacity", cell 0.9 ])) 0.0 "no field ⇒ inert"

          testCase "junk *Fn payloads inside the kind are still ignored — the field drives regardless"
          <| fun () ->
              // The exact Stage-2 emission shape: an invented fractionFn binding
              // beside the column field. The junk is discarded (closure slots do
              // not decode); the field projection wins.
              let fraction =
                  decodeProgressFraction
                      """{"field":"capacity","label":"Capacity","kind":{"$type":"Progress","fractionFn":{"$type":"col","name":"capacity"}}}"""

              Expect.floatClose Accuracy.high (fraction (row [ "capacity", cell 0.75 ])) 0.75 "field projection wins" ]
