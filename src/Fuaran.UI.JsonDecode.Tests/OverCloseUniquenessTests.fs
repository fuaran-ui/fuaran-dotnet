module Fuaran.UI.JsonDecode.Tests.OverCloseUniqueness

// ============================================================================
//  Phase 855 — uniqueness-gated over-close recovery.
//
//  The mirror of the Phase 850 class with the sign reversed: the emission
//  carries a closer it does not owe (`…}}}` where `}}` was owed), closing one
//  level past the node. Phase 850's gate recovers what the grammar FORCES;
//  this one deletes what the grammar merely PERMITS, so its default is the
//  opposite: accept iff exactly one candidate repair decodes clean, refuse
//  otherwise.
//
//  THE LABELLED SET (`overclose-fixtures/`) is the acceptance oracle, and
//  building it is the phase's irreducible cost. It is every stored instance of
//  the class plus the live replications from the same measurement pass — 28
//  emissions, VERBATIM (code fences stripped: the exact bytes the decode gate
//  received). Each was labelled by exhaustive enumeration of the minimal
//  deletion repairs, decoded through the canonical decoder, and then INSPECTED:
//
//    - 14 admit exactly one clean repair. Their labelled intended tree is
//      committed beside them as `intended-NN.txt` — the repaired document,
//      hand-verified to be the tree the emission evidently meant. The gate must
//      recover each and reproduce that tree EXACTLY (canonical re-encode
//      equality, not merely "some tree decoded").
//    - 14 admit two to five repairs that EACH decode clean. Their correct
//      repair is unknowable by construction — the candidates share a node
//      skeleton and differ only in silent field ownership — so they are
//      labelled REFUSE. A gate that picks one of them is the defect, not the
//      feature, and zero wrong acceptances is the gate rather than the target.
//
//  THE WRONG FIX IS PINNED the way Phase 850 pinned EOF-close. On the
//  five-way-ambiguous cell `emission-14`, the leftmost legal deletion decodes
//  perfectly clean and is committed as `leftmost-legal-14.txt`; it buries five
//  trailing fields inside a `Static` binding and renders a bare unformatted
//  number. If a future change reaches for leftmost-first — the obvious
//  implementation — this suite fails.
//
//  Counter-sensitive tests share the process-wide `JsonDecode.Reliance`
//  counters, so the whole list runs sequenced.
// ============================================================================

open System
open System.IO
open Expecto
open Fuaran.UI.Ops

// ─── Fixture loading ──────────────────────────────────────────────────────

let private fixtureDir =
    Path.Combine(AppContext.BaseDirectory, "overclose-fixtures")

let private emissionFiles () =
    Directory.GetFiles(fixtureDir, "emission-*.txt") |> Array.sort

let private read name =
    File.ReadAllText(Path.Combine(fixtureDir, name))

/// The committed intended repair beside an emission, when the emission is
/// labelled unambiguous. Its ABSENCE is the REFUSE label.
let private intendedFor (emissionPath: string) =
    let candidate = emissionPath.Replace("emission-", "intended-")

    if File.Exists candidate then Some candidate else None

let private canonical (n: Fuaran.UI.Types.Node<obj>) =
    Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode n

// ─── Synthetic instances (didactic copies of the measured shapes) ──────────

/// One surplus `}` at a `children[]` sibling boundary, in a tree small enough
/// that the deletion is unique: the sparkline sibling has nowhere else to go.
let private overClosedUnique =
    """{"id":"root","kind":{"$type":"Box","role":"Group","layout":{"$type":"Auto"},"children":[{"id":"m1","kind":{"$type":"Metric","label":"Revenue","value":{"$type":"Static","value":1420}}}},{"id":"m2","kind":{"$type":"Metric","label":"Cost","value":{"$type":"Static","value":7}}}]}}"""

let private expectRefused (label: string) (json: string) =
    match JsonDecode.decodeNode json with
    | Ok _ -> failtestf "%s: expected the gate to refuse; the document decoded" label
    | Error e ->
        Expect.equal e.Code "INVALID_JSON" (sprintf "%s: the ORIGINAL error code survives" label)

        Expect.stringContains
            e.Message
            "parse error at offset"
            (sprintf "%s: the ORIGINAL parser message (with its offset) survives" label)

[<Tests>]
let tests =
    testSequenced
    <| testList
        "Fuaran.UI.Ops.JsonDecode — uniqueness-gated over-close recovery (fuaran#855)"
        [ test "the labelled set — 14 unambiguous recover to their labelled tree, 14 ambiguous refuse" {
              let files = emissionFiles ()
              Expect.equal files.Length 28 "the labelled set is complete"

              let beforeUnique = JsonDecode.Reliance.count JsonDecode.Reliance.OverCloseUnique
              let beforeRefused = JsonDecode.Reliance.count JsonDecode.Reliance.OverCloseRefused

              let mutable recovered = 0
              let mutable refused = 0

              for file in files do
                  let name = Path.GetFileName file
                  let text = File.ReadAllText file

                  match intendedFor file with
                  | Some intended ->
                      // Labelled UNAMBIGUOUS: the gate must recover, and the
                      // tree must be the labelled one — not merely a tree.
                      match JsonDecode.decodeNodeObj text, JsonDecode.decodeNodeObj (File.ReadAllText intended) with
                      | Ok got, Ok want ->
                          Expect.equal
                              (canonical got)
                              (canonical want)
                              (sprintf "%s: the recovery reproduces the labelled intended tree exactly" name)

                          recovered <- recovered + 1
                      | Error e, _ ->
                          failtestf "%s: labelled unambiguous but the gate refused (%s at %s)" name e.Code e.Path
                      | _, Error e -> failtestf "%s: the labelled intended repair does not decode: %A" intended e
                  | None ->
                      // Labelled AMBIGUOUS: two to five repairs each decode
                      // clean, so the correct one is unknowable. Refuse.
                      expectRefused (sprintf "%s (labelled ambiguous)" name) text
                      refused <- refused + 1

              Expect.equal recovered 14 "14 of the 28 admit exactly one clean repair and are recovered"
              Expect.equal refused 14 "14 of the 28 admit two or more and are refused"

              // Both directions are counted, under distinct ids. A refused cell
              // that vanished from the accounting would be the demand signal
              // this class exists to generate, silently dropped.
              Expect.equal
                  (JsonDecode.Reliance.count JsonDecode.Reliance.OverCloseUnique - beforeUnique)
                  14
                  "every recovery is counted"

              Expect.equal
                  (JsonDecode.Reliance.count JsonDecode.Reliance.OverCloseRefused - beforeRefused)
                  14
                  "every refusal is counted"
          }

          test "the leftmost-legal deletion is PINNED as the wrong fix" {
              // The five clean repairs of this cell share a node skeleton and
              // differ only in which object owns the five trailing fields. The
              // leftmost buries all five in the `Static` binding: a materially
              // different UI that passes every gate in the suite.
              let emission = read "emission-14.txt"
              let leftmost = read "leftmost-legal-14.txt"

              // (a) The wrong fix is genuinely AVAILABLE — it decodes clean, so
              //     a leftmost-first gate would have accepted it. The pin is
              //     meaningless unless this holds.
              match JsonDecode.decodeNodeObj leftmost with
              | Ok _ -> ()
              | Error e -> failtestf "the leftmost-legal repair should decode clean; got %s at %s" e.Code e.Path

              // (b) And the gate does not take it — it refuses the emission
              //     outright, returning the original error.
              expectRefused "the five-way-ambiguous cell" emission

              // (c) The ambiguity is MATERIAL, not cosmetic: the leftmost tree
              //     differs from the repair the failure-offset rule selects.
              let failureOffsetRepair = emission.Remove(1031, 1)

              match JsonDecode.decodeNodeObj leftmost, JsonDecode.decodeNodeObj failureOffsetRepair with
              | Ok wrong, Ok other ->
                  Expect.notEqual
                      (canonical wrong)
                      (canonical other)
                      "the two clean repairs are different trees — this is the ambiguity, not a formatting difference"
              | l, r -> failtestf "expected both repairs to decode; got %A / %A" l r
          }

          test "the synthetic instance — a genuinely unique repair recovers and re-encodes canonically" {
              let corrected = overClosedUnique.Replace("""1420}}}},{""", """1420}}},{""")

              match JsonDecode.decodeNodeObj overClosedUnique, JsonDecode.decodeNodeObj corrected with
              | Ok recovered, Ok intended ->
                  Expect.equal
                      (canonical recovered)
                      (canonical intended)
                      "the recovery reconstructs exactly the correctly-braced tree"
              | r, c -> failtestf "expected both forms to decode; got %A / %A" r c
          }

          test "the counter ids are the documented strings and both surface in the snapshot" {
              Expect.equal JsonDecode.Reliance.OverCloseUnique "over-close-unique" "the recovery counter id"
              Expect.equal JsonDecode.Reliance.OverCloseRefused "over-close-refused" "the refusal counter id"

              Expect.notEqual
                  JsonDecode.Reliance.OverCloseUnique
                  JsonDecode.Reliance.OverCloseRefused
                  "recovery and refusal are distinct ids, never two readings of one"

              let snapshot = JsonDecode.Reliance.snapshot ()

              Expect.isTrue
                  (snapshot |> Map.containsKey JsonDecode.Reliance.OverCloseUnique)
                  "the snapshot surfaces the recovery counter"

              Expect.isTrue
                  (snapshot |> Map.containsKey JsonDecode.Reliance.OverCloseRefused)
                  "the snapshot surfaces the refusal counter"
          }

          test "a document that was never in the class is not counted as a refusal" {
              // The refusal counter measures THIS class declining. A malformed
              // document that is not over-closed at all must leave it alone, or
              // the demand signal is diluted into noise.
              let before = JsonDecode.Reliance.count JsonDecode.Reliance.OverCloseRefused

              expectRefused "under-closed, not the 850 profile either" """{"id":"a","items":[{"x":1]}"""
              expectRefused "balanced but invalid" """{"id":"a","kind":,}"""
              expectRefused "cut inside a string" """{"id":"a","children":[{"id":"b"""

              Expect.equal
                  (JsonDecode.Reliance.count JsonDecode.Reliance.OverCloseRefused)
                  before
                  "none of these is over-closed, so none is a refusal of this class"
          }

          test "an over-closed document with no clean repair refuses, and IS counted" {
              let before = JsonDecode.Reliance.count JsonDecode.Reliance.OverCloseRefused

              // Over-closed, and no deletion yields a decodable node (there is
              // no `kind`): profile matched, gate declined.
              expectRefused "over-closed with nothing to recover" """{"id":"a","children":[{"id":"b"}]]}"""

              Expect.equal
                  (JsonDecode.Reliance.count JsonDecode.Reliance.OverCloseRefused - before)
                  1
                  "a profile-matching document the gate declines is a counted refusal"
          }

          test "over-closure past the surplus bound is out of scope, not merely refused" {
              let before = JsonDecode.Reliance.count JsonDecode.Reliance.OverCloseRefused

              // Three surplus closers at the same sibling boundary. Deliberately
              // MID-document: a purely trailing surplus never reaches this gate,
              // because the parser stops at the first complete value and ignores
              // what follows (long-standing behaviour, untouched here).
              expectRefused
                  "surplus of three"
                  """{"id":"root","kind":{"$type":"Box","role":"Group","layout":{"$type":"Auto"},"children":[{"id":"m1","kind":{"$type":"Metric","label":"Revenue","value":{"$type":"Static","value":1420}}}}}},{"id":"m2","kind":{"$type":"Metric","label":"Cost","value":{"$type":"Static","value":7}}}]}}"""

              Expect.equal
                  (JsonDecode.Reliance.count JsonDecode.Reliance.OverCloseRefused)
                  before
                  "a surplus past the bound is a differently-shaped defect, not this class"
          }

          test "LIMIT_EXCEEDED is never re-classified by the gate" {
              let hostile =
                  String.replicate (Fuaran.UI.WireLimits.MaxJsonDepth + 10) """{"a":"""
                  + String.replicate 4 "}"

              match JsonDecode.decodeNode hostile with
              | Ok _ -> failtest "expected LIMIT_EXCEEDED"
              | Error e -> Expect.equal e.Code "LIMIT_EXCEEDED" "the limit classification survives"
          }

          test "decodeOp keeps the strict parse — the gate is node payloads only" {
              let before = JsonDecode.Reliance.count JsonDecode.Reliance.OverCloseUnique

              match JsonDecode.decodeOp overClosedUnique with
              | Ok _ -> failtest "decodeOp must not recover"
              | Error e -> Expect.equal e.Code "INVALID_JSON" "the strict op parse is untouched"

              Expect.equal
                  (JsonDecode.Reliance.count JsonDecode.Reliance.OverCloseUnique)
                  before
                  "no recovery is attributed to the op path"
          }

          test "happy path — the gate never fires on a valid document, and corpus decode is unchanged" {
              let corpusRoot, entries = Corpus.load ()
              let beforeUnique = JsonDecode.Reliance.count JsonDecode.Reliance.OverCloseUnique
              let beforeRefused = JsonDecode.Reliance.count JsonDecode.Reliance.OverCloseRefused
              let beforeImplied = JsonDecode.Reliance.count JsonDecode.Reliance.ImpliedNodeClose

              let nodeEntries = entries |> List.filter (fun e -> e.Kind = "node-round-trip")

              Expect.isGreaterThan nodeEntries.Length 0 "the corpus has node fixtures"

              for e in nodeEntries do
                  let wire = Corpus.readPayload corpusRoot e.InputFile

                  match JsonDecode.decodeNodeObj wire with
                  | Ok decoded ->
                      Expect.equal
                          (canonical decoded)
                          wire
                          (sprintf "%s: decode result unchanged (round-trips byte-identically)" e.Id)
                  | Error err -> failtestf "%s: corpus fixture failed to decode: %A" e.Id err

              Expect.equal
                  (JsonDecode.Reliance.count JsonDecode.Reliance.OverCloseUnique)
                  beforeUnique
                  "no over-close recovery fired on any valid corpus document"

              Expect.equal
                  (JsonDecode.Reliance.count JsonDecode.Reliance.OverCloseRefused)
                  beforeRefused
                  "no over-close refusal fired on any valid corpus document"

              Expect.equal
                  (JsonDecode.Reliance.count JsonDecode.Reliance.ImpliedNodeClose)
                  beforeImplied
                  "the Phase 850 counter is untouched by a valid corpus"
          } ]
