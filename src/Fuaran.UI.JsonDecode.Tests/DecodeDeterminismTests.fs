module Fuaran.UI.JsonDecode.Tests.DecodeDeterminism

// ============================================================================
//  WIRE_FORMAT §20 — decode determinism, the reference host's leg.
//
//  §1's byte-stable round-trip is silent about a narrower question: given the
//  SAME input bytes, do two conformant hosts produce the same tree, or the same
//  rejection? For a small set of inputs they did not, and because every host is
//  individually self-consistent the corpus could not see it — every divergence
//  ratified in §20 was found by reading source rather than by a failing gate.
//
//  Most of §20 is pinned by `reject/reject-json-*` fixtures, which every host
//  runs. What CANNOT be pinned by a fixture is the culture defect below, and it
//  is the worst of the set: it is a divergence between one host and ITSELF,
//  under a process setting no document can carry. A corpus fixture is a
//  document; there is no document that means "decode me on a de-DE machine".
// ============================================================================

open System.Globalization
open Expecto
open Fuaran.UI.Ops

/// Run a body under a named culture, restoring whatever was there.
///
/// Restoring in a `finally` is load-bearing rather than tidy: Expecto runs
/// tests in parallel on a thread pool, so a leaked `CurrentCulture` would land
/// on an unrelated test as an intermittent failure somewhere else entirely —
/// which is the same class of defect as the one under test.
let private underCulture (name: string) (body: unit -> unit) : unit =
    let previous = CultureInfo.CurrentCulture

    try
        CultureInfo.CurrentCulture <- CultureInfo(name)
        body ()
    finally
        CultureInfo.CurrentCulture <- previous

let private metricValue (json: string) : float =
    match JsonDecode.decodeNodeObj json with
    | Error e -> failtestf "the payload did not decode: %s at %s — %s" e.Code e.Path e.Message
    | Ok node ->
        match node.Kind with
        | Fuaran.UI.Types.NodeKind.Metric spec ->
            match spec.Value with
            | Fuaran.UI.Types.Binding.Static(Some v) -> v
            | other -> failtestf "expected a Static value binding, got %A" other
        | other -> failtestf "expected a Metric, got %A" other

/// The corpus's own 17-significant-digit float fixture, inline. Reading it from
/// disk would make this test skip when the corpus is absent, and the point of
/// this one is that it must run everywhere.
let private metricFloat17Sig =
    """{"id":"metric-float-17sig","kind":{"$type":"Metric","format":{"$type":"Currency","code":"GBP"},"icon":"trending-up","label":"Revenue","subtext":"vs last month","tone":"Brand","value":{"$type":"Static","value":0.30000000000000004}}}"""

let private metricSimple =
    """{"id":"m","kind":{"$type":"Metric","label":"Revenue","value":{"$type":"Static","value":1.5}}}"""

let private expectRefused (label: string) (json: string) (code: string) =
    match JsonDecode.decodeNodeObj json with
    | Ok _ -> failtestf "%s: the payload decoded; §20 requires a refusal" label
    | Error e -> Expect.equal e.Code code (sprintf "%s: wrong refusal class (%s)" label e.Message)

[<Tests>]
let cultureTests =
    testList
        "WIRE_FORMAT §20 — number parsing is culture-INVARIANT (Phase 1521)"
        [
          // The defect: `Double.TryParse`'s single-argument overload honours the
          // AMBIENT culture and permits group separators, so on a de-DE or fr-FR
          // host `1.5` parsed as `15` — silently, with a green decode. No project
          // in the tier sets `InvariantGlobalization`, and the Fable build read
          // `1.5` correctly, so the same bytes gave two trees on two pipelines
          // and two trees on one pipeline in two locales.
          //
          // These tests FAIL against the pre-phase `parseNumberRaw` and pass
          // after, which is the only property that makes them worth having.
          test "a decimal point is a decimal point under a comma-decimal culture" {
              underCulture "de-DE" (fun () ->
                  Expect.equal (metricValue metricSimple) 1.5 "de-DE must not read `1.5` as fifteen")
          }

          test "the 17-significant-digit fixture decodes identically under de-DE" {
              let invariant = metricValue metricFloat17Sig

              underCulture "de-DE" (fun () ->
                  Expect.equal
                      (metricValue metricFloat17Sig)
                      invariant
                      "the corpus float fixture must decode to the same double in every locale")
          }

          test "a comma-grouped number is refused in every culture" {
              // `1,234.5` is not a JSON number in any locale. The ambient-culture
              // overload ACCEPTED it under en-US (AllowThousands), which is the
              // same defect wearing its other face: a document one host refuses
              // and another silently reinterprets.
              let grouped =
                  """{"id":"m","kind":{"$type":"Metric","label":"Revenue","value":{"$type":"Static","value":1,234.5}}}"""

              expectRefused "en-US" grouped "INVALID_JSON"
              underCulture "fr-FR" (fun () -> expectRefused "fr-FR" grouped "INVALID_JSON")
          } ]

[<Tests>]
let grammarTests =
    testList
        "WIRE_FORMAT §20.2 — the grammar rows, at the entry point (Phase 1521)"
        [
          // The corpus pins each of these with a `reject/` fixture, so this list
          // is not the primary assertion. It is here because the corpus fixtures
          // are ONE vector per row and the rows are classes — and because these
          // run when the corpus is absent, which is the condition under which a
          // host's conformance leg silently asserts nothing.
          test "row 3 — the RFC 8259 number grammar" {
              for token in [ "+1"; "01"; ".5"; "1."; "1e"; "1e+"; "-"; "0x1F"; "1..2" ] do
                  let json =
                      sprintf
                          """{"id":"m","kind":{"$type":"Metric","label":"R","value":{"$type":"Static","value":%s}}}"""
                          token

                  expectRefused (sprintf "number token '%s'" token) json "INVALID_JSON"
          }

          test "row 3 — the grammar admits every shape RFC 8259 permits" {
              for token in [ "0"; "-0"; "1"; "-1"; "1.5"; "1e3"; "1E3"; "1e+3"; "1e-3"; "0.5"; "1.5e10" ] do
                  let json =
                      sprintf
                          """{"id":"m","kind":{"$type":"Metric","label":"R","value":{"$type":"Static","value":%s}}}"""
                          token

                  match JsonDecode.decodeNodeObj json with
                  | Ok _ -> ()
                  | Error e -> failtestf "the grammar refused the legal token '%s': %s" token e.Message
          }

          test "row 7 — an overflowing exponent is an infinity, not a refusal" {
              // Ratified as-is: all five hosts agreed, so it was unspecified
              // rather than divergent. It sits deliberately beside row 4, which
              // refuses the same value written as a bare `Infinity` literal.
              let json =
                  """{"id":"m","kind":{"$type":"Metric","label":"R","value":{"$type":"Static","value":1e999}}}"""

              Expect.isTrue (System.Double.IsPositiveInfinity(metricValue json)) "1e999 decodes to +infinity"
          }

          test "row 1 — a repeated member is refused wherever it appears" {
              expectRefused
                  "repeated at the root"
                  """{"id":"a","id":"b","kind":{"$type":"Markdown","text":"x"}}"""
                  "INVALID_JSON"

              expectRefused
                  "repeated in a nested object"
                  """{"id":"a","kind":{"$type":"Markdown","text":"x","text":"y"}}"""
                  "INVALID_JSON"

              expectRefused
                  "repeated in a rule-12 payload"
                  """{"id":"a","kind":{"$type":"Custom","componentId":"c","moduleId":"m","props":{"k":1,"k":2}}}"""
                  "INVALID_JSON"
          }

          test "row 2 — content after the root value" {
              expectRefused
                  "a second document"
                  """{"id":"a","kind":{"$type":"Markdown","text":"x"}} {"id":"b"}"""
                  "INVALID_JSON"

              // A SURPLUS CLOSING BRACE is where row 2 meets this host's repair
              // layer, and the interaction is worth pinning rather than
              // asserting away. Before ratification the parser stopped at the
              // root value and silently ignored the remainder; it refuses now,
              // and the fuaran#855 over-close uniqueness gate reconstructs the
              // document and admits it. Same acceptance, different act — an
              // attributed, counted, single-candidate repair instead of a silent
              // truncation nothing recorded. §20.2's repair-layer note is the
              // spec side of this.
              match JsonDecode.decodeNodeObj """{"id":"a","kind":{"$type":"Markdown","text":"x"}}}""" with
              | Ok _ -> ()
              | Error e ->
                  failtestf "a surplus closing brace should reach the over-close repair, not the caller: %s" e.Message

              // Trailing WHITESPACE is not trailing content — the check must not
              // refuse a document a conformant encoder could have produced with a
              // newline on the end.
              match
                  JsonDecode.decodeNodeObj "{\"id\":\"a\",\"kind\":{\"$type\":\"Markdown\",\"text\":\"x\"}}  \n\t "
              with
              | Ok _ -> ()
              | Error e -> failtestf "trailing whitespace must not be a refusal: %s" e.Message
          }

          test "row 5 — a raw C0 control character inside a string" {
              for code in [ 0x00; 0x09; 0x0A; 0x1F ] do
                  let json =
                      sprintf """{"id":"a","kind":{"$type":"Markdown","text":"x%cy"}}""" (char code)

                  expectRefused (sprintf "raw U+%04X" code) json "INVALID_JSON"

              // The ESCAPED spelling is the specified one and stays legal.
              match JsonDecode.decodeNodeObj """{"id":"a","kind":{"$type":"Markdown","text":"x\ty"}}""" with
              | Ok _ -> ()
              | Error e -> failtestf "an escaped control character must decode: %s" e.Message
          }

          test "row 6 — unpaired surrogates, in both directions and split" {
              expectRefused "lone high" """{"id":"a","kind":{"$type":"Markdown","text":"\ud83d"}}""" "INVALID_JSON"

              expectRefused "lone low" """{"id":"a","kind":{"$type":"Markdown","text":"\ude00"}}""" "INVALID_JSON"

              expectRefused
                  "high followed by a non-surrogate"
                  """{"id":"a","kind":{"$type":"Markdown","text":"\ud83dx"}}"""
                  "INVALID_JSON"

              expectRefused
                  "high at the end of the string"
                  """{"id":"a","kind":{"$type":"Markdown","text":"x\ud83d"}}"""
                  "INVALID_JSON"

              expectRefused
                  "two highs"
                  """{"id":"a","kind":{"$type":"Markdown","text":"\ud83d\ud83d"}}"""
                  "INVALID_JSON"

              // A well-formed PAIR is an ordinary character and must decode.
              match JsonDecode.decodeNodeObj """{"id":"a","kind":{"$type":"Markdown","text":"😀"}}""" with
              | Ok _ -> ()
              | Error e -> failtestf "a well-formed surrogate pair must decode: %s" e.Message
          } ]

[<Tests>]
let integerSlotTests =
    testList
        "WIRE_FORMAT §7.1 — integer slots (Phase 1521)"
        [ test "an integer-valued number decodes however it is spelled" {
              for token in [ "3"; "3.0"; "3e0"; "0.3e1" ] do
                  let json = sprintf """{"id":"s","kind":{"$type":"Skeleton","rows":%s}}""" token

                  match JsonDecode.decodeNodeObj json with
                  | Ok _ -> ()
                  | Error e -> failtestf "an integer-valued `%s` was refused at an integer slot: %s" token e.Message
          }

          test "a fractional value is a WRONG_TYPE, not a truncation" {
              expectRefused "2.5" """{"id":"s","kind":{"$type":"Skeleton","rows":2.5}}""" "WRONG_TYPE"
              expectRefused "-2.5" """{"id":"s","kind":{"$type":"Skeleton","rows":-2.5}}""" "WRONG_TYPE"
          }

          test "a value outside the 32-bit range is a WRONG_TYPE" {
              // The cast was implementation-defined: the same bytes became
              // Int32.MinValue on .NET and 1410065408 under Fable. Two trees, one
              // document, one host.
              expectRefused "1e10" """{"id":"s","kind":{"$type":"Skeleton","rows":1e10}}""" "WRONG_TYPE"
              expectRefused "-1e10" """{"id":"s","kind":{"$type":"Skeleton","rows":-1e10}}""" "WRONG_TYPE"
              expectRefused "2^53" """{"id":"s","kind":{"$type":"Skeleton","rows":9007199254740992}}""" "WRONG_TYPE"
          }

          test "the 32-bit boundary itself decodes on both sides" {
              for token in [ "2147483647"; "-2147483648" ] do
                  let json = sprintf """{"id":"s","kind":{"$type":"Skeleton","rows":%s}}""" token

                  match JsonDecode.decodeNodeObj json with
                  | Ok _ -> ()
                  | Error e -> failtestf "the 32-bit boundary value `%s` was refused: %s" token e.Message
          }

          test "the §7 float sentinels do not leak into an integer slot" {
              // §20.2 row 8 widens a FLOAT slot by exactly three strings. An
              // integer slot does not widen, and a host where one function serves
              // both would not notice.
              for sentinel in [ "\"NaN\""; "\"Infinity\""; "\"-Infinity\"" ] do
                  let json = sprintf """{"id":"s","kind":{"$type":"Skeleton","rows":%s}}""" sentinel
                  expectRefused (sprintf "sentinel %s at an integer slot" sentinel) json "WRONG_TYPE"
          } ]
