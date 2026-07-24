module Fuaran.UI.Tests.BoundedTests

// ============================================================================
//  Parse-don't-validate bounded-type kit.
//
//  Asserts the construction-time invariant: a value of a bounded type can only
//  exist if it passed validation, and the round-trip value/tryCreate holds.
// ============================================================================

open Expecto
open Fuaran.UI.Bounded

[<Tests>]
let tests =
    testList
        "bounded-type kit"
        [ test "NonEmptyString accepts content, rejects empty / whitespace" {
              match NonEmptyString.tryCreate "Revenue" with
              | Ok v -> Expect.equal (NonEmptyString.value v) "Revenue" "round-trips the value"
              | Error e -> failtestf "expected Ok, got %s" e

              Expect.isError (NonEmptyString.tryCreate "") "empty rejected"
              Expect.isError (NonEmptyString.tryCreate "   ") "whitespace-only rejected"
          }

          test "BoundedInt enforces an inclusive range and a valid bound" {
              Expect.equal (BoundedInt.tryCreate 0 22 12 |> Result.map BoundedInt.value) (Ok 12) "in-range accepted"
              Expect.equal (BoundedInt.tryCreate 0 22 0 |> Result.map BoundedInt.value) (Ok 0) "lower bound inclusive"
              Expect.equal (BoundedInt.tryCreate 0 22 22 |> Result.map BoundedInt.value) (Ok 22) "upper bound inclusive"
              Expect.isError (BoundedInt.tryCreate 0 22 23) "over-max rejected"
              Expect.isError (BoundedInt.tryCreate 0 22 -1) "under-min rejected"
              Expect.isError (BoundedInt.tryCreate 22 0 5) "invalid bound rejected"
          }

          test "BoundedString enforces inclusive length bounds" {
              Expect.equal
                  (BoundedString.tryCreate 2 5 "abc" |> Result.map BoundedString.value)
                  (Ok "abc")
                  "in-bound length accepted"

              Expect.isError (BoundedString.tryCreate 2 5 "a") "under-length rejected"
              Expect.isError (BoundedString.tryCreate 2 5 "abcdef") "over-length rejected"
          }

          test "Fraction is bounded to [0, 1] and rejects NaN" {
              Expect.equal (Fraction.tryCreate 0.75 |> Result.map Fraction.value) (Ok 0.75) "in-range accepted"
              Expect.equal (Fraction.value Fraction.zero) 0.0 "zero constant"
              Expect.equal (Fraction.value Fraction.one) 1.0 "one constant"
              Expect.isError (Fraction.tryCreate 1.5) "over-one rejected"
              Expect.isError (Fraction.tryCreate -0.1) "negative rejected"
              Expect.isError (Fraction.tryCreate nan) "NaN rejected"
          } ]
