module Fuaran.UI.Tests.Ids

// ============================================================================
//  Deterministic renderer ids (Phase 138).
//
//  The renderer's error-correlation ids moved from `Guid.NewGuid()` (fresh
//  every render) to `Ids.deterministicCorrelationId` (derived from a seed —
//  the failing node's id). This is the unit-level pin for acceptance
//  criterion 3: rendering the same tree twice produces byte-identical output,
//  because the same seed always yields the same id. Same-seed-stable +
//  distinct-seed-distinct + shape (8 lowercase hex chars).
// ============================================================================

open Expecto
open Fuaran.UI.Renderer

[<Tests>]
let idsTests =
    testList
        "Renderer.Ids deterministic correlation id"
        [ test "same seed produces the same id (no Guid nondeterminism)" {
              let a = Ids.deterministicCorrelationId "node-42|metric"
              let b = Ids.deterministicCorrelationId "node-42|metric"
              Expect.equal a b "identical seeds must produce identical ids"
          }

          test "distinct seeds produce distinct ids" {
              let a = Ids.deterministicCorrelationId "node-1"
              let b = Ids.deterministicCorrelationId "node-2"
              Expect.notEqual a b "different node ids should disambiguate"
          }

          test "id is 8 lowercase hex chars (Guid-prefix width)" {
              let id = Ids.deterministicCorrelationId "anything"
              Expect.equal id.Length 8 "width matches the old Guid.ToString(\"N\").Substring(0,8)"

              Expect.isTrue
                  (id |> Seq.forall (fun c -> (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                  "lowercase hex only"
          }

          test "empty seed is total (FNV offset basis, no throw)" {
              let id = Ids.deterministicCorrelationId ""
              Expect.equal id.Length 8 "empty seed still yields a well-formed id"
          } ]
