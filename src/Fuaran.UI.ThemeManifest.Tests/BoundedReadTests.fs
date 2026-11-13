module Fuaran.UI.ThemeManifest.Tests.BoundedReadTests

// Phase 2310 — the manifest read is Core's bounded reader under `Decode.limits`.
// The package's own recursive-descent parser had no depth bound, so a document
// nested a few tens of thousands deep overflowed the stack, an uncatchable
// process kill on .NET. These tests pin the refusal by NAME, the boundary of
// each bound, and the bytes Core's cross-host writer puts on the wire.
//
// Phase 2653 — the bounds are WIRE_FORMAT §21.1's syntactic limits, not this
// host's: the same manifest string is decoded on every host, so each edge below
// is stated as the format's literal value, at it and one past it.

open Expecto
open Fuaran.UI.ThemeManifest

let private refusal (json: string) : string =
    match Decode.decode json with
    | Ok _ -> failtestf "expected a refusal for a %d-char document" json.Length
    | Error e -> e

/// A vanilla DTCG tree `levels` objects deep with one leaf at the bottom: the
/// leaf object is the deepest container, so the document's depth is `levels + 1`.
let private nestedGroups (levels: int) : string =
    String.replicate levels """{"g":"""
    + """{"$value":"#000"}"""
    + String.replicate levels "}"

/// A wrapper manifest whose `invariants` array holds `n` items.
let private invariantItems (n: int) : string =
    """{"tokens":{},"invariants":["""
    + String.concat "," (List.replicate n "0")
    + "]}"

/// A wrapper manifest whose `tokens` object holds `n` members.
let private tokenMembers (n: int) : string =
    """{"tokens":{"""
    + String.concat "," (List.init n (fun i -> $"\"k{i}\":0"))
    + "}}"

/// A wrapper manifest with one token whose `$value` is `value`.
let private tokenValue (value: string) : string =
    """{"tokens":{"a":{"$value":""" + "\"" + value + "\"}}}"

let private decodes (what: string) (json: string) =
    match Decode.decode json with
    | Ok _ -> ()
    | Error e -> failtestf "%s must decode: %s" what e

[<Tests>]
let tests =
    testList
        "ThemeManifest — the bounded read (Phase 2310)"
        [ test "a manifest nested far past the depth bound is refused by name, not a stack overflow" {
              // 100 000 nested arrays: the old parser recursed once per level.
              let n = 100000

              let json =
                  """{"tokens":{"a":{"$value":"""
                  + String.replicate n "["
                  + String.replicate n "]"
                  + "}}}"

              let e = refusal json
              Expect.stringContains e "depth" "the refusal names the depth bound"
              Expect.stringContains e (string Decode.limits.MaxDepth) "and its declared value"
          }

          test "the bounds are WIRE_FORMAT §21.1's syntactic limits (Phase 2653)" {
              Expect.equal Decode.limits.MaxDepth 256 "JSON depth"
              Expect.equal Decode.limits.MaxStringLength 1048576 "string length, in code points"
              Expect.equal Decode.limits.MaxArrayLength 100000 "array length"
              Expect.equal Decode.limits.MaxObjectMembers 100000 "object members"
              Expect.equal Decode.limits.MaxDocumentBytes 33554432 "document bytes"
          }

          test "a manifest 256 containers deep decodes and 257 is refused by name (Phase 2653)" {
              // `nestedGroups l` is l + 1 containers deep; the scalar leaf counts as
              // none, because §21.1 counts every `{` and `[`.
              decodes "a manifest at depth 256" (nestedGroups 255)
              let e = refusal (nestedGroups 256)
              Expect.stringContains e "depth" "the refusal names the depth bound"
              Expect.stringContains e "256" "and its value"
          }

          test "the string and member bounds hold at their §21.1 edges (Phase 2653)" {
              decodes "a 1,048,576-code-point string" (tokenValue (String.replicate 1048576 "a"))
              Expect.stringContains (refusal (tokenValue (String.replicate 1048577 "a"))) "stringLength" "one more"

              // Code points, not UTF-16 units: an astral character costs one.
              decodes "1,048,576 astral code points" (tokenValue (String.replicate 1048576 "\U0001D11E"))

              decodes "a 100,000-item array" (invariantItems 100000)
              Expect.stringContains (refusal (invariantItems 100001)) "arrayLength" "one more item"

              decodes "a 100,000-member object" (tokenMembers 100000)
              Expect.stringContains (refusal (tokenMembers 100001)) "objectMembers" "one more member"
          }

          test "a document past the byte bound is refused before it is read" {
              let pad = String.replicate (Decode.limits.MaxDocumentBytes + 1) " "
              Expect.stringContains (refusal ("{\"tokens\":{}}" + pad)) "documentBytes" "named"
          }

          test "malformed text is refused as malformed, never as a breach" {
              let e = refusal """{"tokens":{"""
              Expect.isFalse (e.Contains "bound") $"not a limit breach: {e}"
          }

          test "a member null reads as the member's absence" {
              match Decode.decode """{"meta":{"name":null},"tokens":{"a":{"$value":"1","$type":null}}}""" with
              | Ok m ->
                  Expect.equal m.Meta.Name "" "an absent name"
                  Expect.equal (m.Tokens |> List.map (fun t -> t.Type)) [ "" ] "an absent $type"
              | Error e -> failtestf "a member null must decode: %s" e
          }

          test "a repeated member name is refused, as Core's grammar refuses it" {
              refusal """{"tokens":{},"tokens":{}}""" |> ignore
          }

          test "encode spells a control character the cross-host way" {
              let m =
                  { ThemeManifest.empty with
                      Meta =
                          { ManifestMeta.anonymous with
                              Name = "a\nb" } }

              Expect.equal (Encode.encode m) """{"meta":{"name":"a\u000ab"},"tokens":{}}""" "\\u000a, never \\n"

              match Decode.decode (Encode.encode m) with
              | Ok back -> Expect.equal back m "and it reads back"
              | Error e -> failtestf "re-decode: %s" e
          } ]
