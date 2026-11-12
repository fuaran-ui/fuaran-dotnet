module Fuaran.UI.ThemeManifest.Tests.BoundedReadTests

// Phase 2310 — the manifest read is Core's bounded reader under `Decode.limits`.
// The package's own recursive-descent parser had no depth bound, so a document
// nested a few tens of thousands deep overflowed the stack, an uncatchable
// process kill on .NET. These tests pin the refusal by NAME, the boundary of
// each bound, and the bytes Core's cross-host writer puts on the wire.

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

          test "the depth bound admits exactly MaxDepth containers" {
              let atBound = nestedGroups (Decode.limits.MaxDepth - 1)

              match Decode.decode atBound with
              | Ok m -> Expect.equal (List.length m.Tokens) 1 "a tree at the bound decodes"
              | Error e -> failtestf "a tree at the bound must decode: %s" e

              Expect.stringContains
                  (refusal (nestedGroups Decode.limits.MaxDepth))
                  "depth"
                  "one container more is refused"
          }

          test "a document past the byte bound is refused before it is read" {
              let pad = String.replicate (Decode.limits.MaxDocumentBytes + 1) " "
              Expect.stringContains (refusal ("{\"tokens\":{}}" + pad)) "documentBytes" "named"
          }

          test "a string past the string bound is refused" {
              let long = String.replicate (Decode.limits.MaxStringLength + 1) "a"

              Expect.stringContains
                  (refusal ("""{"tokens":{"a":{"$value":""" + "\"" + long + "\"}}}"))
                  "stringLength"
                  "named"
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
