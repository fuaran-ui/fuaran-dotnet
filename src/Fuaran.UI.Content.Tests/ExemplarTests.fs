module Fuaran.UI.Content.Tests.ExemplarTests

open Expecto
open Fuaran.UI.Content

module F = Fuaran.UI.Fuaran

let private canonicalOf node =
    Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode node

[<Tests>]
let tests =
    testList
        "Fuaran.UI.Content.Exemplar"
        [ test "a valid canonical exemplar passes all three gates and is a fixed point" {
              // A node → its canonical bytes is, by contract, an admissible exemplar.
              let node = F.markdown "m" "hello **world**"
              let json = canonicalOf node

              match Exemplar.decodeExemplar json with
              | Ok(_, canonical) ->
                  Expect.equal canonical json "the returned canonical is the decode/encode fixed point"
              | Error f -> failtestf "expected Ok, got: %s" (Exemplar.describeFailure f)
          }

          test "the returned node re-encodes to the same canonical bytes" {
              let node = F.markdown "m" "round-trip me"
              let json = canonicalOf node

              match Exemplar.decodeExemplar json with
              | Ok(decoded, canonical) ->
                  Expect.equal (canonicalOf decoded) canonical "decoded node re-encodes byte-identically"
              | Error f -> failtestf "expected Ok, got: %s" (Exemplar.describeFailure f)
          }

          test "malformed JSON is rejected at the decode gate" {
              match Exemplar.decodeExemplar "{ this is not wire json" with
              | Error(Exemplar.DecodeFailed _) -> ()
              | Error other -> failtestf "expected DecodeFailed, got %A" other
              | Ok _ -> failtest "expected a decode failure for malformed JSON"
          }

          test "describeFailure surfaces a non-empty typed hint" {
              match Exemplar.decodeExemplar "{}" with
              | Error f -> Expect.isTrue ((Exemplar.describeFailure f).Length > 0) "a non-empty failure message"
              | Ok _ -> failtest "expected a failure for an empty object"
          } ]
