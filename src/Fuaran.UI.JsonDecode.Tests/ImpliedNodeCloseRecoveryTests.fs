module Fuaran.UI.JsonDecode.Tests.ImpliedNodeCloseRecovery

// ============================================================================
//  Phase 850 — implied-node-close decode recovery.
//
//  The class: a node wrapper's closing brace dropped at the end of a
//  `children[]` / `cases[]` element (or the root node left open at EOF),
//  after a run of ≥2 closing braces. Measured on stored model emissions
//  (2026-08-15); the 36 stored malformed emissions live under
//  `recovery-fixtures/` VERBATIM (code fences stripped — the exact bytes the
//  decode gate received) and are the acceptance suite: 36/36 must recover,
//  34/36 must decode clean, and the two residuals must be the named
//  MISSING_FIELD defects (independent of this class — they would have
//  surfaced on a well-formed emission anyway).
//
//  The suite also PINS the wrong fix: auto-close at EOF repairs NONE of the
//  mid-document cells (the owed brace sits mid-document, so the `]` after it
//  is already mis-parsed — appending closers at the end fixes nothing). If a
//  future change reroutes the recovery through an EOF-append, the pin fails.
//
//  Counter-sensitive tests share the process-wide `JsonDecode.Reliance`
//  counters, so the whole list runs sequenced.
// ============================================================================

open System
open System.IO
open Expecto
open Fuaran.UI.Ops

// ─── Fixture loading ──────────────────────────────────────────────────────

let private fixtureDir = Path.Combine(AppContext.BaseDirectory, "recovery-fixtures")

let private fixtureFiles () =
    Directory.GetFiles(fixtureDir, "emission-*.txt") |> Array.sort

/// The two residual cells: MISSING_FIELD defects unrelated to the nesting
/// class, named by the measurement record.
let private expectedResiduals =
    Map.ofList
        [ "emission-06.txt", ("$.kind.children[0].kind.children[5].kind.stateKey", "stateKey")
          "emission-22.txt", ("$.kind.children[1].kind.children[0].kind.variant", "variant") ]

/// The one EOF-class cell (the root node left open at document end) — the only
/// fixture the naive EOF-append could ever repair, and the one whose recovery
/// IS an EOF close under the shipped rule.
let private eofOnlyFixture = "emission-04.txt"

// ─── Synthetic class instances (didactic copies of the measured shape) ─────

/// The canonical mid-document instance: the `m1` node wrapper's own `}` is
/// dropped after the `…1200000}}` brace run, so the children-ending `]`
/// arrives while the wrapper is still open.
let private droppedNodeClose =
    """{"id":"root","kind":{"$type":"Box","role":"Group","layout":{"$type":"Auto"},"children":[{"id":"m1","kind":{"$type":"Metric","label":"Revenue","value":{"$type":"Static","value":1200000}}]}}"""

/// The separator signature: the wrapper owed its close before the `,` that
/// separates two `children[]` siblings (`…}},{` emitted where `…}}},{` was
/// owed).
let private droppedNodeCloseBeforeComma =
    """{"id":"root","kind":{"$type":"Box","role":"Group","layout":{"$type":"Auto"},"children":[{"id":"m1","kind":{"$type":"Metric","label":"Revenue","value":{"$type":"Static","value":1200000}},{"id":"m2","kind":{"$type":"Metric","label":"Cost","value":{"$type":"Static","value":7}}}]}}"""

let private expectRecovered (label: string) (json: string) =
    match JsonDecode.decodeNode json with
    | Ok _ -> ()
    | Error e -> failtestf "%s: expected the recovery to repair this document; got %s at %s" label e.Code e.Path

/// Fail-closed assertion: the decode fails as INVALID_JSON with the parser's
/// ORIGINAL message (offset intact), i.e. the recovery declined.
let private expectFailsClosed (label: string) (json: string) =
    match JsonDecode.decodeNode json with
    | Ok _ -> failtestf "%s: expected INVALID_JSON (fail closed); the document decoded" label
    | Error e ->
        Expect.equal e.Code "INVALID_JSON" (sprintf "%s: the original error code survives" label)

        Expect.stringContains
            e.Message
            "parse error at offset"
            (sprintf "%s: the ORIGINAL parser message (with its offset) survives" label)

[<Tests>]
let tests =
    testSequenced
    <| testList
        "Fuaran.UI.Ops.JsonDecode — implied-node-close recovery (fuaran#850)"
        [ test
              "the stored-emission re-gate — 36/36 recover, 34/36 decode clean, residuals are the two named MISSING_FIELDs" {
              let files = fixtureFiles ()
              Expect.equal files.Length 36 "the stored-emission fixture set is complete"

              let before = JsonDecode.Reliance.count JsonDecode.Reliance.ImpliedNodeClose
              let mutable clean = 0

              for file in files do
                  let name = Path.GetFileName file
                  let text = File.ReadAllText file

                  match JsonDecode.decodeNode text, Map.tryFind name expectedResiduals with
                  | Ok _, None -> clean <- clean + 1
                  | Ok _, Some(path, _) ->
                      failtestf "%s: decoded clean but the record names it a MISSING_FIELD residual at %s" name path
                  | Error e, Some(path, field) ->
                      // A residual still RECOVERS (the parse succeeds); it
                      // fails one layer later, on a field the emission never
                      // carried — the defect a well-formed emission would have
                      // surfaced anyway.
                      Expect.equal e.Code "MISSING_FIELD" (sprintf "%s: the residual is a MISSING_FIELD" name)
                      Expect.equal e.Path path (sprintf "%s: the residual path is the recorded one" name)

                      Expect.stringContains
                          e.Message
                          (sprintf "'%s'" field)
                          (sprintf "%s: the residual names the missing field" name)
                  | Error e, None ->
                      failtestf "%s: expected clean decode; got %s at %s: %s" name e.Code e.Path e.Message

              Expect.equal clean 34 "34 of 36 decode clean through the canonical decoder"

              let after = JsonDecode.Reliance.count JsonDecode.Reliance.ImpliedNodeClose
              Expect.equal (after - before) 36 "every one of the 36 recoveries is counted — 36/36 recover"
          }

          test "the EOF-close counter-example — appending owed braces at EOF repairs NONE of the mid-document class" {
              // The wrong fix, pinned. For every mid-document fixture the
              // parse failure sits before the document end, so no amount of
              // appended closers can repair it. Only the one EOF-class cell
              // (the root left open at EOF) is reachable by appending — and
              // that is the EOF leg of the shipped rule, not this strategy.
              let files =
                  fixtureFiles () |> Array.filter (fun f -> Path.GetFileName f <> eofOnlyFixture)

              Expect.equal files.Length 35 "35 of the 36 fixtures are the mid-document class"

              for file in files do
                  let name = Path.GetFileName file
                  let text = File.ReadAllText file

                  for k in 1..12 do
                      // Bypass the shipped recovery deliberately: the naive
                      // repair candidate must fail the RAW parse, proving the
                      // appended braces never reach the mis-parsed `]`.
                      match JsonDecode.decodeOp (text + String.replicate k "}") with
                      | Ok _ ->
                          failtestf "%s: EOF-appending %d brace(s) unexpectedly produced a parseable payload" name k
                      | Error e ->
                          Expect.equal
                              e.Code
                              "INVALID_JSON"
                              (sprintf "%s (+%d braces): still the raw INVALID_JSON — EOF-append fixes nothing" name k)
          }

          test "the synthetic class instances — `]` and `,` boundaries both recover, and re-encode canonically" {
              expectRecovered "dropped close at ]" droppedNodeClose
              expectRecovered "dropped close before ," droppedNodeCloseBeforeComma

              // The recovered tree is the canonical tree, not merely
              // well-formed JSON: re-encode equals the correctly-braced form.
              let corrected = droppedNodeClose.Replace("""1200000}}]}}""", """1200000}}}]}}""")

              match JsonDecode.decodeNodeObj droppedNodeClose, JsonDecode.decodeNodeObj corrected with
              | Ok recovered, Ok intended ->
                  Expect.equal
                      (Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode recovered)
                      (Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode intended)
                      "the recovery reconstructs exactly the tree the correctly-braced emission decodes to"
              | r, c -> failtestf "expected both forms to decode; got %A / %A" r c
          }

          test "the reliance counter fires once per recovery, under its own id" {
              let before = JsonDecode.Reliance.count JsonDecode.Reliance.ImpliedNodeClose
              expectRecovered "counter probe" droppedNodeClose
              let after = JsonDecode.Reliance.count JsonDecode.Reliance.ImpliedNodeClose
              Expect.equal (after - before) 1 "one recovery, one count"

              Expect.equal
                  JsonDecode.Reliance.ImpliedNodeClose
                  "implied-node-close"
                  "the counter id is the documented one"

              Expect.isTrue
                  (JsonDecode.Reliance.snapshot ()
                   |> Map.containsKey JsonDecode.Reliance.ImpliedNodeClose)
                  "the snapshot surfaces the counter id"
          }

          test "profile mismatch — the same defect outside children[]/cases[] fails closed to the original error" {
              // A dropped `}` at the end of an element of an array keyed
              // `items`: same syntactic defect, not the measured class. The
              // recovery must decline so the defect stays a visible error
              // (the demand signal is not eaten).
              expectFailsClosed "non-children array" """{"id":"a","items":[{"x":1]}"""
          }

          test "genuinely-ambiguous nesting still rejects" {
              // `,` then a string inside an open wrapper: the string is either
              // an owed array element (close the wrapper first) or a key
              // missing its value — two readings, so no recovery.
              expectFailsClosed "ambiguous string after comma" """{"id":"a","children":[{"x":1, "s"]}"""
              // A separator with nothing after it at the array level.
              expectFailsClosed "trailing comma before ]" """{"id":"a","children":[{"x":1},]}"""
          }

          test "truncation fingerprints never recover" {
              expectFailsClosed "EOF inside a string" """{"id":"a","children":[{"id":"b"""
              expectFailsClosed "EOF awaiting a value" """{"id":"a","kind":"""
              expectFailsClosed "EOF after an array separator" """{"id":"a","children":[{"id":"x"},"""
              expectFailsClosed "EOF after a key" """{"id":"a","children":[{"id" """
          }

          test "an over-closed document fails closed" {
              expectFailsClosed "extra ]" """{"id":"a","children":[{"id":"b"}]]}"""
          }

          test "LIMIT_EXCEEDED is never re-classified by the recovery" {
              // An under-closed document whose failure is the depth limit, not
              // syntax: the recovery is gated on INVALID_JSON and must leave
              // the limit error untouched.
              let hostile = String.replicate (Fuaran.UI.WireLimits.MaxJsonDepth + 10) """{"a":"""

              match JsonDecode.decodeNode hostile with
              | Ok _ -> failtest "expected LIMIT_EXCEEDED"
              | Error e -> Expect.equal e.Code "LIMIT_EXCEEDED" "the limit classification survives"
          }

          test "happy path — the recovery never fires on a valid document, and corpus decode is unchanged" {
              let corpusRoot, entries = Corpus.load ()
              let before = JsonDecode.Reliance.count JsonDecode.Reliance.ImpliedNodeClose

              let nodeEntries = entries |> List.filter (fun e -> e.Kind = "node-round-trip")

              Expect.isGreaterThan nodeEntries.Length 0 "the corpus has node fixtures"

              for e in nodeEntries do
                  let wire = Corpus.readPayload corpusRoot e.InputFile

                  match JsonDecode.decodeNodeObj wire with
                  | Ok decoded ->
                      // Byte-identity: the decode result is the same tree the
                      // pre-850 decoder produced — its canonical re-encode is
                      // the corpus payload itself (the round-trip law).
                      Expect.equal
                          (Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode decoded)
                          wire
                          (sprintf "%s: decode result unchanged (round-trips byte-identically)" e.Id)
                  | Error err -> failtestf "%s: corpus fixture failed to decode: %A" e.Id err

              let after = JsonDecode.Reliance.count JsonDecode.Reliance.ImpliedNodeClose
              Expect.equal after before "no recovery fired on any valid corpus document"
          } ]
