module Fuaran.UI.JsonDecode.Tests.LenientIngest

// The lenient AI-ingest profile (WIRE_FORMAT.md §16): decode accepts author-
// friendly shorthands the canonical encoder does not itself emit, so a model
// spends fewer tokens. These tests pin the shorthand AND the invariant that it
// canonicalises to exactly the verbose form — same decoded value, same
// re-encoded bytes — so the shorthand never becomes a second wire dialect.

open Expecto
open Fuaran.UI.Ops
open Fuaran.UI.OpStream.Abstractions

let private decodeOk (label: string) (json: string) =
    match JsonDecode.decodeNodeObj json with
    | Ok n -> n
    | Error e -> failtestf "%s: decode failed: %A" label e

[<Tests>]
let tests =
    testList
        "Fuaran.UI.Ops.JsonDecode — lenient AI-ingest (§16)"
        [ test "the bare string IS the canonical Literal form (0.2.0); the envelope decodes identically" {
              // 0.2.0 inverts the 0.1.x direction: the envelope
              // {"$type":"Literal","text":…} remains decode-accepted forever,
              // but the CANONICAL encoding is the bare string.
              let shorthand =
                  """{"id":"heading-1","kind":{"$type":"Heading","level":2,"text":"Channel performance","variant":"Standard"}}"""

              let verbose =
                  """{"id":"heading-1","kind":{"$type":"Heading","level":2,"text":{"$type":"Literal","text":"Channel performance"},"variant":"Standard"}}"""

              let fromShorthand = decodeOk "shorthand" shorthand
              let fromVerbose = decodeOk "verbose" verbose

              Expect.equal
                  (CanonicalJson.encodeNode fromShorthand)
                  (CanonicalJson.encodeNode fromVerbose)
                  "both forms decode to the SAME node"

              Expect.equal
                  (CanonicalJson.encodeNode fromVerbose)
                  shorthand
                  "re-encoding the ENVELOPE yields the canonical bare-string bytes (one wire dialect, not two)"
          }

          test "bare-string canonical form holds for a nested text position (a Button label)" {
              let shorthand =
                  """{"id":"btn-1","kind":{"$type":"Button","disabled":{"$type":"State","defaultValue":false,"key":"loading"},"icon":"refresh","label":"Refresh","onClick":{"$type":"Chain","ops":[]},"variant":"Primary"}}"""

              let n = decodeOk "button-shorthand" shorthand

              Expect.stringContains
                  (CanonicalJson.encodeNode n)
                  "\"label\":\"Refresh\""
                  "the button label stays a bare string on the canonical wire"
          } ]


[<Tests>]
let retiredPositionTests =
    // Phase 687 — the CLOSE of the migration window Phase 681 opened.
    //
    // `InsertChild` / `MoveNode` lost their integer position (they append;
    // `ReorderChildren` states order by naming ids). Through 681–686 the decoder
    // ACCEPTED AND IGNORED the legacy field so five hosts could adopt
    // independently. Every host is now positionless and no emitter in reach
    // produces the field, so the tolerance is withdrawn: it is a decode error.
    //
    // These tests are the going-red half of that change. The window's own tests
    // asserted the field was silently dropped; keeping them alongside would have
    // meant asserting both readings at once, so they were REWRITTEN rather than
    // added to — the accept case is not a thing that still holds.
    testList
        "retired positional ops (687 — the window is closed)"
        [ test "InsertChild with a retired `position` is refused by name" {
              let legacy =
                  """{"$type":"InsertChild","child":{"id":"n","kind":{"$type":"Markdown","text":"x"}},"parentId":"p","position":3}"""

              match JsonDecode.decodeOp legacy with
              | Ok _ -> failtest "the retired `position` was accepted — the migration window is closed"
              | Error e ->
                  Expect.equal e.Code "WRONG_TYPE" "refused as WRONG_TYPE, the near-miss family's code"
                  Expect.equal e.Path "$.position" "the error names the retired field, not some downstream defect"
                  Expect.stringContains e.Message "ReorderChildren" "the didactic names what to use instead"
          }

          test "MoveNode with a retired `newPosition` is refused by name" {
              let legacy =
                  """{"$type":"MoveNode","newParentId":"q","newPosition":2,"target":"n"}"""

              match JsonDecode.decodeOp legacy with
              | Ok _ -> failtest "the retired `newPosition` was accepted — the migration window is closed"
              | Error e ->
                  Expect.equal e.Code "WRONG_TYPE" "refused as WRONG_TYPE"
                  Expect.equal e.Path "$.newPosition" "the error names the retired field"
          }

          test "the retired field is named ahead of any other defect in the same op" {
              // The ordering is fixed across all five hosts (see
              // `retiredPositionalField`): an author who also omitted a required
              // field must still learn that the ordinal is gone, rather than
              // fixing the other defect and meeting this one on the next run.
              let both = """{"$type":"InsertChild","position":0}"""

              match JsonDecode.decodeOp both with
              | Ok _ -> failtest "an op missing `parentId` AND carrying `position` decoded"
              | Error e -> Expect.equal e.Path "$.position" "the retired field wins over the missing required field"
          }

          test "the positionless form is what the encoder emits" {
              let current = """{"$type":"MoveNode","newParentId":"q","target":"n"}"""

              match JsonDecode.decodeOp current with
              | Error e -> failtestf "canonical MoveNode refused: %A" e
              | Ok op -> Expect.equal (CanonicalJson.encodeOp op) current "decode → re-encode is the identity"
          } ]
