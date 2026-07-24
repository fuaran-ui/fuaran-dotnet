module Fuaran.UI.Validator.Tests.ManifestTests

open Expecto
open Fuaran.UI.Validator.Manifest

[<Tests>]
let tests =
    testList
        "Manifest parser"
        [ test "Empty JSON object yields the empty manifest" {
              let parsed = parse "{}"
              Expect.equal parsed empty "empty"
          }

          test "Queries + msgCases parse from arrays" {
              let json =
                  """{ "queries": ["totalRevenue", "salesRows"], "msgCases": ["LoadData", "SelectRow"] }"""

              let parsed = parse json

              Expect.equal parsed.Queries (Set.ofList [ "totalRevenue"; "salesRows" ]) "queries parsed into a set"

              Expect.equal parsed.MsgCases (Set.ofList [ "LoadData"; "SelectRow" ]) "msgCases parsed into a set"
          }

          test "queryRowTypes parses as a name → typeName map" {
              let json =
                  """{ "queryRowTypes": { "salesRows": "SaleRow", "channelRows": "ChannelRow" } }"""

              let parsed = parse json

              Expect.equal parsed.QueryRowTypes["salesRows"] "SaleRow" "salesRows → SaleRow"
              Expect.equal parsed.QueryRowTypes["channelRows"] "ChannelRow" "channelRows → ChannelRow"
          }

          test "Trailing commas and // comments are tolerated" {
              let json =
                  """{
                       // tiny module
                       "queries": ["x",],
                     }"""

              let parsed = parse json
              Expect.equal parsed.Queries (Set.singleton "x") "x is parsed"
          }

          // ─── customNodeRatio field ──────────────────────────
          test "customNodeRatio defaults to None when absent" {
              let parsed = parse "{}"
              Expect.isNone parsed.CustomNodeRatio "absent = None"
          }

          test "customNodeRatio parses a declared threshold" {
              let json = """{ "customNodeRatio": 0.12 }"""
              let parsed = parse json
              Expect.equal parsed.CustomNodeRatio (Some 0.12) "0.12 round-trips through the parser"
          } ]
