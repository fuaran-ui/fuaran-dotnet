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
          }

          // Pinned FNV-1a vectors (Phase 960). These pin the VALUE, not just the
          // shape, so a reintroduced naive multiply is caught by a number rather
          // than a compile. The "a" pair is the measured divergence pair: the
          // canonical .NET value vs the value a naive `uint32 *` produces under
          // Fable's float-backed numerics (precision lost past 2^53 inside the
          // multiply). On .NET both the naive and split-half multiplies agree, so
          // these vectors also prove the mul32 transform changed nothing here —
          // the cross-pipeline half of the claim is measured by
          // tests/ids-parity-probe/, which this suite cannot reach.
          test "pinned vector: empty seed is the FNV-1a offset basis" {
              Expect.equal
                  (Ids.deterministicCorrelationId "")
                  "811c9dc5"
                  "FNV-1a(\"\") = offset basis 2166136261 = 0x811c9dc5"
          }

          test "pinned vector: \"a\" is the canonical FNV-1a value, not the naive-Fable one" {
              let id = Ids.deterministicCorrelationId "a"
              Expect.equal id "e40c292c" "canonical .NET FNV-1a(\"a\")"

              Expect.notEqual
                  id
                  "e40c2930"
                  "e40c2930 is the value a naive multiply produces under Fable — seeing it here means the split-half multiply was removed"
          } ]
