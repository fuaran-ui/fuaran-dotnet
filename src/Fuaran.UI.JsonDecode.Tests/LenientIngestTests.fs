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
