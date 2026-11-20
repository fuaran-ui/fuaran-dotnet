module Fuaran.UI.AiTools.Tests.AiToolsSurfaceTests

// ============================================================================
//  Phase 2100 — public AI-tools surface that had no caller and no test, kept
//  and tested: the three `ResponseRender` renderers the suite never reached
//  (`renderResolvedBindingResult`, `renderGeometryTree`, `renderErrorEntries`)
//  and `Fidelity.ofNode`. Each assertion reads the emitted JSON back, so a key
//  renamed or dropped fails here rather than at an orchestrator.
// ============================================================================

open System
open System.Text.Json
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.AiTools.Types
open Fuaran.UI.AiTools

let private parse (json: string) = JsonDocument.Parse(json).RootElement

let private str (e: JsonElement) (key: string) = e.GetProperty(key).GetString()

[<Tests>]
let tests =
    testList
        "AiTools - kept and tested surface (Phase 2100)"
        [ test "renderResolvedBindingResult renders a resolved value with its source" {
              let result =
                  ResolvedBindingResult.Resolved
                      { Expression = "$state.region"
                        ResolvedValue = Some(box "EMEA" |> Unchecked.nonNull)
                        TypeHint = Some "String"
                        Source = BindingSource.State "region" }

              let root = parse (ResponseRender.renderResolvedBindingResult result)
              Expect.equal (str root "expression") "$state.region" "expression"
              Expect.equal (str root "resolvedValue") "EMEA" "resolvedValue"
              Expect.equal (str root "type") "String" "type hint"
              Expect.equal (str root "source") "State" "source token"
          }

          test "renderResolvedBindingResult renders an absent value as null, not as a missing key" {
              let result =
                  ResolvedBindingResult.Resolved
                      { Expression = "$query.sales"
                        ResolvedValue = None
                        TypeHint = None
                        Source = BindingSource.Query "sales" }

              let root = parse (ResponseRender.renderResolvedBindingResult result)
              Expect.equal (root.GetProperty("resolvedValue").ValueKind) JsonValueKind.Null "null value"
              let mutable ignored = Unchecked.defaultof<JsonElement>
              Expect.isFalse (root.TryGetProperty("type", &ignored)) "no type hint, no key"
          }

          test "renderResolvedBindingResult renders a failure in the §4d shape" {
              let result =
                  ResolvedBindingResult.Failed
                      { Expression = "$query.sales"
                        Source = BindingSource.Query "sales"
                        Code = BindingErrorCode.NotResolvedYet
                        Message = "pending"
                        Hint =
                          { NodeKind = Some "Metric"
                            Suggestion = Some "wait"
                            AvailableAlternatives = [ "static" ] } }

              let root = parse (ResponseRender.renderResolvedBindingResult result)
              Expect.equal (str root "source") "Query" "source token"
              let error = root.GetProperty "error"
              Expect.equal (str error "code") "NotResolvedYet" "code token"
              Expect.equal (str error "message") "pending" "message"
              let hint = error.GetProperty "hint"
              Expect.equal (str hint "node_kind") "Metric" "snake_case hint key"
              Expect.equal (str hint "suggestion") "wait" "suggestion"

              Expect.equal
                  [ for a in hint.GetProperty("available_alternatives").EnumerateArray() -> a.GetString() ]
                  [ "static" ]
                  "alternatives"
          }

          test "renderGeometryTree renders the tree recursively, a missing geometry as null" {
              let tree: GeometryTree =
                  { Id = NodeId "root"
                    Kind = "Box"
                    Geometry =
                      Some
                          { X = 0.0
                            Y = 0.0
                            Width = 100.0
                            Height = 50.0
                            Overflowing = false }
                    Children =
                      [ { Id = NodeId "child"
                          Kind = "Markdown"
                          Geometry = None
                          Children = [] } ] }

              let root = parse (ResponseRender.renderGeometryTree tree)
              Expect.equal (str root "id") "root" "id"
              Expect.equal (str root "kind") "Box" "kind"
              Expect.equal (root.GetProperty("geometry").GetProperty("width").GetDouble()) 100.0 "width"
              Expect.isFalse (root.GetProperty("geometry").GetProperty("overflowing").GetBoolean()) "overflowing"
              let children = [ for c in root.GetProperty("children").EnumerateArray() -> c ]
              Expect.equal children.Length 1 "one child"
              Expect.equal (str children.Head "id") "child" "child id"
              Expect.equal (children.Head.GetProperty("geometry").ValueKind) JsonValueKind.Null "null geometry"
          }

          test "renderErrorEntries renders an array, absent turn and node as null" {
              let at = DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)

              let entries: ErrorEntry list =
                  [ { Id = "err-1"
                      TurnId = Some 3
                      Code = "BindingResolution"
                      Message = "no source"
                      NodeId = Some(NodeId "m1")
                      RecordedAt = at }
                    { Id = "err-2"
                      TurnId = None
                      Code = "UnwiredAction"
                      Message = "no handler"
                      NodeId = None
                      RecordedAt = at } ]

              let root = parse (ResponseRender.renderErrorEntries entries)
              let rows = [ for r in root.EnumerateArray() -> r ]
              Expect.equal rows.Length 2 "two entries, in order"
              Expect.equal (str rows[0] "id") "err-1" "first id"
              Expect.equal (rows[0].GetProperty("turnId").GetInt32()) 3 "turn"
              Expect.equal (str rows[0] "nodeId") "m1" "node"
              Expect.equal (str rows[0] "recordedAt") (at.ToString "o") "round-trip timestamp"
              Expect.equal (rows[1].GetProperty("turnId").ValueKind) JsonValueKind.Null "no turn"
              Expect.equal (rows[1].GetProperty("nodeId").ValueKind) JsonValueKind.Null "no node"
              Expect.equal (ResponseRender.renderErrorEntries []) "[]" "an empty drain is an empty array"
          }

          test "Fidelity.ofNode is forKind over the node's own wire kind" {
              let node: Node<unit> = Fuaran.markdown "m" "text"

              Expect.equal
                  (Fidelity.ofNode node)
                  (Fidelity.forKind (RenderFidelity.wireNameOf node.Kind))
                  "projection of the declaration"
          } ]
