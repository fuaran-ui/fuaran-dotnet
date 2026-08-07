module Fuaran.UI.JsonDecode.Tests.Limits

// Phase 781 — the decode-side resource limits (`Fuaran.UI.WireLimits`,
// WIRE_FORMAT §21) that make the totality claim true on SHAPE as well as on
// semantics: "a malformed or hostile input yields a structured, typed error,
// never an exception or a hang".
//
// Before these bounds, every walk in the decode stack was plainly recursive with
// no counter. About 200 KB of `[[[[[…` — two bytes per level — produced a
// `StackOverflowException`, which .NET can neither catch nor recover from: the
// process dies and no `DecodeError` is ever returned. So the claim did not fail
// gracefully, it failed by being unobservable.
//
// EVERY hostile payload below is built as a STRING. Constructing one as a nested
// F# value would overflow the stack while BUILDING the input, which proves
// nothing about the decoder and produces a test that cannot distinguish a
// working guard from a broken one.
//
// The pairing matters as much as the rejections: each limit is tested both just
// PAST the bound (typed error) and AT the bound (still decodes). A guard that
// only ever refuses is indistinguishable from a decoder that refuses everything.

open Expecto
open Fuaran.UI
open Fuaran.UI.Ops

/// A canonical-wire `Box` chain `depth` nodes deep with a `Heading` leaf,
/// assembled as text. Matches `CanonicalJson.encodeNode` byte-for-byte on the
/// shape (Ordinal-sorted keys, omit-when-default), so a within-limit chain is a
/// genuinely decodable tree and not merely something the decoder tolerates.
let private boxChain (depth: int) : string =
    let leaf =
        """{"id":"leaf","kind":{"$type":"Heading","level":1,"text":"x","variant":"Standard"}}"""

    let mutable acc = leaf

    for i in 1..depth do
        acc <-
            "{\"id\":\"n"
            + string i
            + "\",\"kind\":{\"$type\":\"Box\",\"children\":["
            + acc
            + "],\"layout\":{\"$type\":\"Auto\"},\"role\":\"Group\"}}"

    acc

/// `depth` nested `TreeOp.Batch` ops around an empty innermost `Batch`, so the
/// document holds `depth + 1` nested op objects and no nodes at all — the op
/// nesting axis on its own.
let private batchChain (depth: int) : string =
    let mutable acc = """{"$type":"Batch","ops":[]}"""

    for _ in 1..depth do
        acc <- "{\"$type\":\"Batch\",\"ops\":[" + acc + "]}"

    acc

/// A single `Box` with `childCount` `Heading` children — a tree that is WIDE and
/// only two levels deep, so it isolates the node-count bound from the depth one.
/// Assembled with a `StringBuilder`: at the scale `MaxNodes` sits at, naive
/// string concatenation is quadratic and the test would appear to hang.
let private flatBox (childCount: int) : string =
    let sb = System.Text.StringBuilder()

    sb.Append("{\"id\":\"root\",\"kind\":{\"$type\":\"Box\",\"children\":[")
    |> ignore

    for i in 1..childCount do
        if i > 1 then
            sb.Append(',') |> ignore

        sb
            .Append("{\"id\":\"c")
            .Append(i)
            .Append("\",\"kind\":{\"$type\":\"Heading\",\"level\":1,\"text\":\"x\",\"variant\":\"Standard\"}}")
        |> ignore

    sb.Append("],\"layout\":{\"$type\":\"Auto\"},\"role\":\"Group\"}}") |> ignore
    sb.ToString()

let private expectLimit (label: string) (result: Result<'a, JsonDecode.DecodeError>) =
    match result with
    | Ok _ -> failtestf "%s: expected LIMIT_EXCEEDED but the payload decoded" label
    | Error e ->
        Expect.equal e.Code "LIMIT_EXCEEDED" (sprintf "%s: expected LIMIT_EXCEEDED, got %s (%s)" label e.Code e.Message)

[<Tests>]
let tests =
    testList
        "Fuaran.UI.Ops.JsonDecode — WireLimits (Phase 781)"
        [
          // ─── The original defect ────────────────────────────────────────
          test "a 100k-deep array payload returns a typed error and does not kill the process" {
              // The exact shape from the phase report: ~200 KB of `[[[[[…`.
              // Reaching the assertion at all is half the proof — before the
              // guard this line terminated the test host.
              let hostile = String.replicate 100_000 "[" + String.replicate 100_000 "]"
              expectLimit "100k-deep array" (JsonDecode.decodeNodeObj hostile)
          }

          test "a 100k-deep array payload is refused by decodeOp too" {
              let hostile = String.replicate 100_000 "[" + String.replicate 100_000 "]"
              expectLimit "100k-deep array via decodeOp" (JsonDecode.decodeOp hostile)
          }

          test "a 100k-deep object payload returns a typed error" {
              // Objects nest through a different parser arm than arrays, so the
              // guard has to be on both. `{"a":{"a":{"a": … }}}` unterminated is
              // still refused for DEPTH before it is refused for truncation,
              // because the depth check fires on the way DOWN.
              let hostile =
                  String.replicate 100_000 "{\"a\":" + "1" + String.replicate 100_000 "}"

              expectLimit "100k-deep object" (JsonDecode.decodeNodeObj hostile)
          }

          // ─── Node nesting: MaxDepth ─────────────────────────────────────
          test "a node chain one level past MaxDepth is refused with a typed error" {
              expectLimit "MaxDepth + 1 box chain" (JsonDecode.decodeNodeObj (boxChain (WireLimits.MaxDepth + 1)))
          }

          test "a node chain far past MaxDepth is refused rather than overflowing" {
              // Deep enough to overflow the structural decoder outright (it dies
              // at 186 in Release, 31 in Debug) but shallow enough that the
              // parser's own MaxJsonDepth does not catch it first — so this
              // isolates the decodeNodeAst guard from the parser guard.
              expectLimit "60-deep box chain" (JsonDecode.decodeNodeObj (boxChain 60))
          }

          test "a node chain exactly at MaxDepth still decodes" {
              // The other half: the limit must admit what it claims to admit.
              // `boxChain n` has n Box nodes plus the Heading leaf, so
              // `MaxDepth - 1` boxes is a tree exactly MaxDepth nodes deep.
              match JsonDecode.decodeNodeObj (boxChain (WireLimits.MaxDepth - 1)) with
              | Ok _ -> ()
              | Error e -> failtestf "a tree exactly at MaxDepth was refused: %s at %s — %s" e.Code e.Path e.Message
          }

          // ─── String + array size limits ─────────────────────────────────
          test "a string past MaxStringLength is refused with a typed error" {
              let huge =
                  "{\"id\":\"a\",\"kind\":{\"$type\":\"Heading\",\"level\":1,\"text\":\""
                  + String.replicate (WireLimits.MaxStringLength + 1) "x"
                  + "\",\"variant\":\"Standard\"}}"

              expectLimit "oversize string" (JsonDecode.decodeNodeObj huge)
          }

          test "an array past MaxArrayLength is refused with a typed error" {
              let elements =
                  String.concat "," (List.replicate (WireLimits.MaxArrayLength + 1) "1")

              let huge = "{\"id\":\"a\",\"kind\":[" + elements + "]}"
              expectLimit "oversize array" (JsonDecode.decodeNodeObj huge)
          }

          // ─── Op nesting: TreeOp.Batch ───────────────────────────────────
          //
          // `decodeOp` is the second public decode entry point and `TreeOp.Batch`
          // makes its decoder self-recursive. The parser's MaxJsonDepth looked
          // like cover — a Batch level costs two JSON levels, so 256 admits only
          // about 127 — and measurably was not: with every other guard in place,
          // 2.6 KB of 100 nested Batches still killed the process outright.
          // These payloads are far inside MaxJsonDepth, so they isolate the op
          // guard from the parser guard.

          test "a Batch nested one level past MaxDepth is refused with a typed error" {
              expectLimit "MaxDepth + 1 batch chain" (JsonDecode.decodeOp (batchChain WireLimits.MaxDepth))
          }

          test "a Batch nested far past MaxDepth is refused rather than overflowing" {
              // 100 levels: the payload measured to kill the process before the
              // op walk was bounded. Reaching the assertion at all is the proof.
              expectLimit "100-deep batch chain" (JsonDecode.decodeOp (batchChain 100))
          }

          test "a Batch nested exactly at MaxDepth still decodes" {
              match JsonDecode.decodeOp (batchChain (WireLimits.MaxDepth - 1)) with
              | Ok _ -> ()
              | Error e -> failtestf "a batch exactly at MaxDepth was refused: %s at %s — %s" e.Code e.Path e.Message
          }

          // ─── Total node count: MaxNodes ─────────────────────────────────
          //
          // A tree can be hostile by being WIDE rather than deep, and depth /
          // string / array limits do not touch that: `MaxArrayLength` bounds one
          // array, not the document. The pair below straddles the ceiling with a
          // single Box whose children array is at exactly `MaxArrayLength`, so
          // only the node total distinguishes them.

          test "a document past MaxNodes is refused with a typed error" {
              expectLimit "MaxNodes + 1" (JsonDecode.decodeNodeObj (flatBox WireLimits.MaxNodes))
          }

          test "a document of exactly MaxNodes still decodes" {
              match JsonDecode.decodeNodeObj (flatBox (WireLimits.MaxNodes - 1)) with
              | Ok _ -> ()
              | Error e -> failtestf "a document of exactly MaxNodes was refused: %s at %s — %s" e.Code e.Path e.Message
          }

          // ─── The limit error is a REFUSAL, not a misdiagnosis ───────────
          test "a limit breach is not reported as invalid JSON" {
              // The distinction is the whole point of the separate code: the
              // input below is perfectly well-formed JSON. Telling an author it
              // is malformed would send them to fix the wrong thing.
              match JsonDecode.decodeNodeObj (boxChain (WireLimits.MaxDepth + 1)) with
              | Ok _ -> failtest "expected a refusal"
              | Error e ->
                  Expect.notEqual e.Code "INVALID_JSON" "a well-formed but over-deep payload is not a syntax error"

                  Expect.isTrue
                      (e.Message.Contains "MaxDepth")
                      (sprintf "the message should name the limit breached; got: %s" e.Message)
          }

          // ─── The limits are internally consistent ───────────────────────
          test "MaxJsonDepth admits a MaxDepth-deep tree of any shape" {
              // A tree level costs several JSON levels (a Box costs 3; the
              // worst-shaped kinds about 5). If MaxJsonDepth were set below
              // 5 * MaxDepth, the syntactic bound would refuse legitimate trees
              // BEFORE the node bound could give the accurate diagnosis — a
              // silent mis-attribution rather than a visible failure.
              Expect.isGreaterThanOrEqual
                  WireLimits.MaxJsonDepth
                  (5 * WireLimits.MaxDepth)
                  "MaxJsonDepth must leave room for a MaxDepth-deep tree"
          } ]
