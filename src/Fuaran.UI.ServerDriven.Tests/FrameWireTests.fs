module Fuaran.UI.ServerDriven.Tests.FrameWireTests

// ─── Phase 152 Track D: SSE frame wire encoding ──────────────
//
// The pure framing half of the SSE+POST backend. Asserts the JSON body shape
// + the full SSE wire frame (id / event / data / blank-line terminator) the
// generic shim's sseAdapter consumes.

open Expecto
open Fuaran.UI.ServerDriven

let private frame =
    { Seq = 7
      Patches = [ DomPatch.SetText("count", "42"); DomPatch.RemoveNode "stale" ]
      Effects = [ ClientEffect.Navigate "/done" ] }

[<Tests>]
let tests =
    testList
        "FrameWire (SSE encoding)"
        [ test "encodeJson carries seq + the patch/effect arrays" {
              let json = FrameWire.encodeJson frame
              Expect.stringContains json "\"seq\":7" "sequence present"
              Expect.stringContains json "\"kind\":\"SetText\"" "patch encoded"
              Expect.stringContains json "\"kind\":\"Navigate\"" "effect encoded"
              // Shape: a single object with the three keys, in order.
              Expect.isTrue (json.StartsWith "{\"seq\":7,\"patches\":[") "starts with seq then patches"
              Expect.stringContains json ",\"effects\":[" "effects array follows"
          }

          test "encodeSse frames id + event + data + blank-line terminator" {
              let sse = FrameWire.encodeSse frame
              Expect.stringContains sse "id: 7\n" "id line = the op sequence (Last-Event-ID key)"
              Expect.stringContains sse "event: patch\n" "patch event type"
              Expect.stringContains sse "data: {" "data line carries the JSON body"
              Expect.isTrue (sse.EndsWith "\n\n") "terminated by a blank line"
              // The data line's payload is exactly encodeJson.
              Expect.stringContains sse ("data: " + FrameWire.encodeJson frame) "data = encodeJson"
          }

          test "an empty frame still encodes valid empty arrays" {
              let empty = { Seq = 0; Patches = []; Effects = [] }

              Expect.equal (FrameWire.encodeJson empty) "{\"seq\":0,\"patches\":[],\"effects\":[]}" "empty arrays"
          } ]
