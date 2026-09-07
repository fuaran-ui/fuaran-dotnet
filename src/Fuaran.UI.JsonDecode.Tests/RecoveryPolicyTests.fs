module Fuaran.UI.JsonDecode.Tests.RecoveryPolicy

// ============================================================================
//  Phase 1532 — decode-time recovery is a policy axis with a ceiling.
//
//  The two shipped recoveries (fuaran#850's implied node close, fuaran#855's
//  uniqueness-gated over-close deletion) are each careful — profile-gated,
//  bounded, failing closed, counted. What they were not is DECLINABLE. Every
//  `INVALID_JSON` node decode entered them, on every ingress, including one
//  carrying documents the host did not emit and does not want repaired.
//
//  And the 855 gate's bounds cap the number of candidate repairs, never the
//  size of one. Each candidate is a fresh copy of the whole document plus a
//  fresh parse of it, so the work is `candidates x length` and only one factor
//  was bounded: a multi-megabyte over-closed payload was gigabytes of parsing
//  on the FAILURE path, reachable by anyone who could post a malformed body.
//
//  Two things follow, and this suite pins both:
//
//    * `DecodePolicy.Recovery` — `Off` for an untrusted ingress, `Lenient`
//      (the default, so nothing shipped changes) otherwise.
//    * `DecodePolicy.MaxRecoverableLength` — past it, `Lenient` refuses to
//      enumerate and the document surfaces its original parser error.
//
//  Plus the per-decode `Recovered` record, because `Reliance` counts
//  process-wide and so cannot answer "was THIS tree repaired".
//
//  Counter-sensitive, so the list runs sequenced beside the other two recovery
//  suites.
// ============================================================================

open System.Diagnostics
open Expecto
open Fuaran.UI.KindPolicy
open Fuaran.UI.Ops

// ─── Fixtures ────────────────────────────────────────────────────────────────

/// One surplus `}` at a `children[]` sibling boundary, in a tree small enough
/// that the deletion is unique — the same synthetic instance the fuaran#855
/// suite uses, so the two agree on what "recoverable" means.
let private overClosedUnique =
    """{"id":"root","kind":{"$type":"Box","role":"Group","layout":{"$type":"Auto"},"children":[{"id":"m1","kind":{"$type":"Metric","label":"Revenue","value":{"$type":"Static","value":1420}}}},{"id":"m2","kind":{"$type":"Metric","label":"Cost","value":{"$type":"Static","value":7}}}]}}"""

/// A node wrapper missing its closing brace at a `children[]` boundary — the
/// fuaran#850 class, whose recovery is a single repair and a single re-parse.
let private impliedNodeClose =
    """{"id":"root","kind":{"$type":"Box","role":"Group","layout":{"$type":"Auto"},"children":[{"id":"m1","kind":{"$type":"Metric","label":"Revenue","value":{"$type":"Static","value":1420}},{"id":"m2","kind":{"$type":"Metric","label":"Cost","value":{"$type":"Static","value":7}}}]}}"""

let private valid =
    """{"id":"root","kind":{"$type":"Metric","label":"Revenue","value":{"$type":"Static","value":1420}}}"""

let private lenient = DecodePolicy.admitAll
let private strict = DecodePolicy.admitAll |> DecodePolicy.withRecovery Recovery.Off

/// `overClosedUnique` inflated past `MaxRecoverableLength` with a long label,
/// and NOTHING else changed: same surplus, same closer positions, same unique
/// repair. So the only difference between this document and the recoverable one
/// is its LENGTH — which is exactly the axis the ceiling bounds, and the reason
/// this fixture pads with a string rather than with siblings.
///
/// Padding with siblings would not have tested the ceiling at all: each sibling
/// adds closers, and past `MaxCloserPositions` the enumeration already refuses,
/// so a sibling-padded document would be declined by a bound that was already
/// there. A long STRING adds length while leaving the closer count alone —
/// which is precisely the shape the amplification takes, since every candidate
/// repair is a fresh copy and a fresh parse of the WHOLE document.
let private overClosedPastCeiling () : string =
    let padding = System.String('x', DecodePolicy.MaxRecoverableLength)
    overClosedUnique.Replace("\"Revenue\"", "\"" + padding + "\"")

[<Tests>]
let tests =
    testSequenced
    <| testList
        "Fuaran.UI.Ops.JsonDecode — recovery as a declared policy (Phase 1532)"
        [ test "the default is unchanged — a shipped policy recovers" {
              Expect.isTrue (DecodePolicy.recovers DecodePolicy.admitAll) "admitAll"
              Expect.isTrue (DecodePolicy.recovers (DecodePolicy.admitting "p" [ "Metric" ])) "admitting"

              Expect.isTrue
                  (DecodePolicy.recovers (DecodePolicy.excludingFrom "p" [ "Metric"; "Custom" ] [ "Custom" ]))
                  "excludingFrom"

              Expect.isFalse (DecodePolicy.recovers strict) "and only an explicit Off declines"
          }

          test "under Lenient both recoveries still fire (the pre-phase behaviour)" {
              match JsonDecode.decodeNodeObjWithPolicy lenient overClosedUnique with
              | Ok _ -> ()
              | Error e -> failtestf "the over-close gate must still recover under Lenient: %A" e

              match JsonDecode.decodeNodeObjWithPolicy lenient impliedNodeClose with
              | Ok _ -> ()
              | Error e -> failtestf "the implied-node-close recovery must still fire under Lenient: %A" e
          }

          test "under Off the over-close gate does not run — the parser's error survives" {
              match JsonDecode.decodeNodeObjWithPolicy strict overClosedUnique with
              | Ok _ -> failtest "Recovery.Off must not repair an over-closed document"
              | Error e ->
                  Expect.equal e.Code "INVALID_JSON" "the ORIGINAL error code"
                  Expect.stringContains e.Message "parse error at offset" "and the ORIGINAL parser message"
          }

          test "under Off the implied-node-close recovery does not run either" {
              // The axis is "does this host want a malformed document repaired
              // at all". A host that declines the enumerating gate has not
              // consented to the cheap one.
              match JsonDecode.decodeNodeObjWithPolicy strict impliedNodeClose with
              | Ok _ -> failtest "Recovery.Off must not repair an implied node close"
              | Error e -> Expect.equal e.Code "INVALID_JSON" "the ORIGINAL error code"
          }

          test "Off counts nothing — it is the parser refusing, not the gate declining" {
              let beforeRefused = JsonDecode.Reliance.count JsonDecode.Reliance.OverCloseRefused
              let beforeUnique = JsonDecode.Reliance.count JsonDecode.Reliance.OverCloseUnique
              let beforeImplied = JsonDecode.Reliance.count JsonDecode.Reliance.ImpliedNodeClose

              JsonDecode.decodeNodeObjWithPolicy strict overClosedUnique |> ignore
              JsonDecode.decodeNodeObjWithPolicy strict impliedNodeClose |> ignore

              Expect.equal
                  (JsonDecode.Reliance.count JsonDecode.Reliance.OverCloseRefused)
                  beforeRefused
                  "a gate that never ran refused nothing"

              Expect.equal
                  (JsonDecode.Reliance.count JsonDecode.Reliance.OverCloseUnique)
                  beforeUnique
                  "and recovered nothing"

              Expect.equal
                  (JsonDecode.Reliance.count JsonDecode.Reliance.ImpliedNodeClose)
                  beforeImplied
                  "nor did the other one"
          }

          test "Off changes nothing about a document that parses" {
              // The whole safety argument for the axis: `Off` is a refusal to
              // REPAIR, never a different decode.
              match
                  JsonDecode.decodeNodeObjWithPolicy lenient valid, JsonDecode.decodeNodeObjWithPolicy strict valid
              with
              | Ok a, Ok b ->
                  let canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode
                  Expect.equal (canon a) (canon b) "the same document decodes to the same tree under both postures"
              | a, b -> failtestf "both postures must decode a valid document; got %A / %A" a b
          }

          test "a document past the ceiling is refused even though its repair is unique" {
              // THE assertion. This document differs from the recoverable one
              // ONLY in length, so nothing but the ceiling can decide it — and
              // the ceiling decides it against recovery, because each candidate
              // repair costs a full copy and a full re-parse of whatever the
              // caller sent.
              let hostile = overClosedPastCeiling ()

              Expect.isGreaterThan
                  hostile.Length
                  DecodePolicy.MaxRecoverableLength
                  "the fixture is genuinely past the ceiling"

              let before = JsonDecode.Reliance.count JsonDecode.Reliance.OverCloseRefused
              let sw = Stopwatch.StartNew()
              let result = JsonDecode.decodeNodeObjWithPolicy lenient hostile
              sw.Stop()

              match result with
              | Ok _ -> failtest "a document past the recovery ceiling must not be repaired"
              | Error e -> Expect.equal e.Code "INVALID_JSON" "the ORIGINAL error code survives"

              Expect.equal
                  (JsonDecode.Reliance.count JsonDecode.Reliance.OverCloseRefused - before)
                  1
                  "it matched the profile and the gate declined, so it is a counted refusal — the class stays visible"

              // Secondary, and generous: the bounded path is one linear profile
              // scan, so it is milliseconds. This measures that the enumeration
              // did not run, not the speed of the machine.
              Expect.isLessThan sw.ElapsedMilliseconds 2000L "and it declined without enumerating"
          }

          test "the SAME document under the ceiling still recovers" {
              // The twin of the test above, and what makes it an assertion about
              // the ceiling rather than about over-closed documents in general:
              // shorten the padding and the identical shape recovers.
              let shortened =
                  overClosedUnique.Replace("\"Revenue\"", "\"" + System.String('x', 1024) + "\"")

              Expect.isLessThan shortened.Length DecodePolicy.MaxRecoverableLength "inside the ceiling"

              match JsonDecode.decodeNodeObjWithPolicy lenient shortened with
              | Ok _ -> ()
              | Error e -> failtestf "length is the only difference, and this one is inside it: %A" e
          }

          test "the ceiling does not move the verdict for a document inside it" {
              Expect.isLessThan
                  overClosedUnique.Length
                  DecodePolicy.MaxRecoverableLength
                  "the measured class sits well inside the ceiling"

              match JsonDecode.decodeNodeObjWithPolicy lenient overClosedUnique with
              | Ok _ -> ()
              | Error e -> failtestf "the ceiling must not refuse the class it was sized for: %A" e
          }

          test "Recovered names the recovery this document needed" {
              let overClose = JsonDecode.decodeNodeObjWithOutcome lenient overClosedUnique
              Expect.isTrue (Result.isOk overClose.Result) "it decoded"

              Expect.equal
                  overClose.Recovered
                  [ JsonDecode.Reliance.OverCloseUnique ]
                  "and says which gate repaired it, in the vocabulary Reliance counts"

              let implied = JsonDecode.decodeNodeObjWithOutcome lenient impliedNodeClose
              Expect.isTrue (Result.isOk implied.Result) "it decoded"
              Expect.equal implied.Recovered [ JsonDecode.Reliance.ImpliedNodeClose ] "and names the other recovery"
          }

          test "Recovered is empty for a clean decode and for a failure" {
              let clean = JsonDecode.decodeNodeWithOutcome lenient valid
              Expect.isTrue (Result.isOk clean.Result) "a valid document decodes"
              Expect.isEmpty clean.Recovered "nothing was repaired"

              let refusedByPolicy = JsonDecode.decodeNodeWithOutcome strict overClosedUnique
              Expect.isTrue (Result.isError refusedByPolicy.Result) "Off refuses it"
              Expect.isEmpty refusedByPolicy.Recovered "a document that did not decode was not repaired"

              let garbage = JsonDecode.decodeNodeWithOutcome lenient """{"id":"a","kind":,}"""
              Expect.isTrue (Result.isError garbage.Result) "and neither did this"
              Expect.isEmpty garbage.Recovered "nothing to report"
          }

          test "the outcome entry point observes the decode, it does not change it" {
              let canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode

              for policy in [ lenient; strict ] do
                  for json in [ valid; overClosedUnique; impliedNodeClose; """{"id":"a","kind":,}""" ] do
                      match
                          JsonDecode.decodeNodeObjWithPolicy policy json,
                          (JsonDecode.decodeNodeObjWithOutcome policy json).Result
                      with
                      | Ok a, Ok b -> Expect.equal (canon a) (canon b) "same tree"
                      | Error a, Error b -> Expect.equal a.Code b.Code "same refusal"
                      | a, b -> failtestf "the two entry points disagreed: %A / %A" a b
          }

          test "withRecovery leaves the admission half alone" {
              let closed = DecodePolicy.admitting "closed" [ "Metric" ]
              let narrowed = closed |> DecodePolicy.withRecovery Recovery.Off

              Expect.equal narrowed.Identity closed.Identity "the identity is the host's, and does not move"
              Expect.equal narrowed.Admission closed.Admission "nor does the admitted vocabulary"
              Expect.isFalse (DecodePolicy.recovers narrowed) "only the recovery posture changed"
          } ]
