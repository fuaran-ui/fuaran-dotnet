module Fuaran.UI.OpStream.Dag.Tests.MergeConformanceTests

open System.IO
open Expecto
open Fuaran.UI.OpStream.Dag.Merge
open Fuaran.UI.OpStream.Dag.Tests.TestSupport

// ============================================================================
//  Merge-conformance — Leg A (F# merge == corpus).
//
//  For each committed merge fixture: the F# 3-way merge of (base, A, B) equals
//  the committed `expected` tree byte-for-byte, and sha256(expected) equals the
//  manifest's `outcomeHash`. Gated on the workspace corpus being present. Leg B
//  (TS merge == corpus) runs in the fuaran-ts repo against the same payloads.
// ============================================================================

let private corpusDir =
    Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "..", "wire-format-fixtures", "merge-conformance")

[<Tests>]
let tests =
    testList
        "Dag.MergeConformance"
        [ test "committed merge corpus matches the F# 3-way merge + outcome hash" {
              if not (Directory.Exists corpusDir) then
                  skiptest "wire-format-fixtures/merge-conformance corpus absent (single-repo checkout)"
              else
                  for (id, _description, baseT, a, b) in MergeCorpus.fixtures do
                      let merged = MergeCorpus.mergedOf baseT a b
                      let expectedPath = Path.Combine(corpusDir, id + ".expected.json")
                      Expect.isTrue (File.Exists expectedPath) (sprintf "%s expected fixture present" id)
                      let committed = File.ReadAllText(expectedPath).TrimEnd('\n', '\r')

                      Expect.equal (canonical merged) committed (sprintf "%s: F# merge == committed expected tree" id)

                      // The base/a/b payloads also match the encoder (no drift).
                      Expect.equal
                          (File.ReadAllText(Path.Combine(corpusDir, id + ".base.json")).TrimEnd('\n', '\r'))
                          (canonical baseT)
                          (sprintf "%s: base payload == encoder" id)
          }

          // Leg A for the Phase 184 validator-gated verdicts: the F# gated merge
          // reproduces the committed verdict bytes + hash. Leg B (TS) runs in the
          // fuaran-ts repo against the same payloads + a port of the sample validator.
          test "committed validator-gated verdicts match the F# gated merge + verdict hash" {
              if not (Directory.Exists corpusDir) then
                  skiptest "wire-format-fixtures/merge-conformance corpus absent (single-repo checkout)"
              else
                  for (id, _description, baseT, a, b) in MergeCorpus.gatedFixtures do
                      let verdict = MergeCorpus.verdictOf baseT a b
                      Expect.isNonEmpty verdict (sprintf "%s: the fixture introduces a defect" id)
                      let verdictJson = ValidatorGate.encodeVerdict verdict
                      let verdictPath = Path.Combine(corpusDir, id + ".verdict.json")
                      Expect.isTrue (File.Exists verdictPath) (sprintf "%s verdict fixture present" id)
                      let committed = File.ReadAllText(verdictPath).TrimEnd('\n', '\r')

                      Expect.equal verdictJson committed (sprintf "%s: F# verdict == committed verdict" id)
          }

          // Leg A for the Phase 1497 refusal envelopes: the F# merge refuses each
          // triad and reproduces the committed envelope bytes. The swap is asserted
          // here rather than committed twice — two files that were transpositions of
          // each other would pin the same fact in a form a host could satisfy by
          // emitting both from one side.
          test "committed refusal envelopes match the F# two-sided merge envelope" {
              if not (Directory.Exists corpusDir) then
                  skiptest "wire-format-fixtures/merge-conformance corpus absent (single-repo checkout)"
              else
                  for (id, _description, baseT, a, b) in MergeCorpus.refusalFixtures do
                      let envelope = MergeCorpus.envelopeOf baseT a b
                      Expect.isNonEmpty envelope (sprintf "%s: the fixture refuses" id)
                      let envelopeJson = MergeConflict.encodeEnvelope envelope
                      let envelopePath = Path.Combine(corpusDir, id + ".envelope.json")
                      Expect.isTrue (File.Exists envelopePath) (sprintf "%s envelope fixture present" id)
                      let committed = File.ReadAllText(envelopePath).TrimEnd('\n', '\r')

                      Expect.equal envelopeJson committed (sprintf "%s: F# envelope == committed envelope" id)

                      // The swapped merge transposes the envelope and changes nothing else.
                      let swapped = MergeCorpus.envelopeOf baseT b a |> MergeConflict.sortCanonical

                      for (fc, rc) in List.zip (MergeConflict.sortCanonical envelope) swapped do
                          Expect.equal
                              (fc.NodeId, fc.Facet, fc.Class)
                              (rc.NodeId, rc.Facet, rc.Class)
                              (sprintf "%s: same cell" id)

                          Expect.equal fc.A rc.B (sprintf "%s: forward A == swapped B" id)
                          Expect.equal fc.B rc.A (sprintf "%s: forward B == swapped A" id)
          } ]
