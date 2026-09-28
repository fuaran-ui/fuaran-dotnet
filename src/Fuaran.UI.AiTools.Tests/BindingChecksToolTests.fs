module Fuaran.UI.AiTools.Tests.BindingChecksTool

// ============================================================================
//  Phase 1889 — a chart or grid bound to a column its pipeline cannot produce
//  reaches the closed loop through `getRuntimeErrors`, located and explained:
//  the FUARAN code, the reader's node id, the JSONPath of the slot, and the
//  schema the source DOES produce — enough for a model to repair the binding
//  by name. A reader graded unchecked is not an error and records nothing.
//
//  The documents are inline copies of the corpus's `binding-check-*` fixtures
//  (this suite does not read the corpus), trimmed to the reader.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.AiTools.Types
open Fuaran.UI.AiTools.Seams
open Fuaran.UI.AiTools

let private freshContext () : IntrospectionContext =
    { Sources = Seams.emptyContext.Sources
      Geometry = Seams.noGeometry
      CurrentState = Seams.noCurrentState
      Errors = Seams.createInMemorySink ()
      Clock = Seams.fixedClock }

let private table =
    """{"columns":{"amount":{"validity":[true,true,true],"values":[100,120,90]},"dept":{"validity":[true,true,true],"values":["eng","eng","sales"]}},"schema":[{"name":"dept","type":"string"},{"name":"amount","type":"int"}]}"""

let private groupBy =
    """[{"$type":"groupBy","aggs":[{"fn":"sum","name":"total","of":"amount"}],"keys":["dept"]}]"""

let private grid (fields: string) (rowKey: string) =
    sprintf
        """{"id":"doc","kind":{"$type":"Box","children":[{"id":"spend-grid","kind":{"$type":"DataGrid","columns":[%s],"rowKeyField":"%s","source":{"$type":"Transform","pipeline":%s,"source":%s}}}],"layout":{"$type":"Auto"},"role":"Dashboard"}}"""
        fields
        rowKey
        groupBy
        table

let private col (f: string) =
    sprintf """{"field":"%s","kind":{"$type":"Text"},"label":"%s"}""" f f

let private decode (json: string) : Node<obj> =
    match Fuaran.UI.Ops.JsonDecode.decodeNodeObj json with
    | Ok n -> n
    | Error e -> failtestf "fixture failed to decode: %s at %s" e.Code e.Path

[<Tests>]
let tests =
    testList
        "Phase 1889 — binding checks through getRuntimeErrors"
        [ test "a grid naming a column its pipeline renamed away surfaces one located entry per slot" {
              let ctx = freshContext ()
              let tree = decode (grid (col "dept" + "," + col "amount") "amount")

              let recorded = Tools.recordBindingChecks ctx tree (Some 4)
              Expect.equal recorded 2 "the column field and the rowKeyField"

              let drained = Tools.getRuntimeErrors ctx None

              Expect.equal
                  (drained |> List.map (fun e -> e.Code, e.NodeId, e.TurnId))
                  [ "FUARAN114", Some(NodeId "spend-grid"), Some 4
                    "FUARAN114", Some(NodeId "spend-grid"), Some 4 ]
                  "one entry per finding, against the reader and the turn"

              Expect.stringContains
                  drained[0].Message
                  "at $.kind.children[0].kind.columns[1].field"
                  "the slot, by JSONPath"

              Expect.stringContains drained[1].Message "at $.kind.children[0].kind.rowKeyField" "the second slot"

              Expect.stringContains
                  drained[0].Message
                  "the source produces: dept:string, total:int"
                  "the schema the model can repair against"
          }

          test "a correctly bound grid records nothing" {
              let ctx = freshContext ()
              let tree = decode (grid (col "dept" + "," + col "total") "dept")
              Expect.equal (Tools.recordBindingChecks ctx tree None) 0 "nothing to record"
              Expect.isEmpty (Tools.getRuntimeErrors ctx None) "and the sink stays empty"
          }

          test "an unchecked reader is not an error: a grid over a Query records nothing" {
              let ctx = freshContext ()

              let tree =
                  decode
                      """{"id":"q","kind":{"$type":"DataGrid","columns":[{"field":"anything","kind":{"$type":"Text"},"label":"x"}],"source":{"$type":"Query","name":"rows"}}}"""

              Expect.equal
                  (Tools.recordBindingChecks ctx tree None)
                  0
                  "unchecked is stated by the report, not recorded as an error"

              match Fuaran.UI.PreEmitValidate.bindingChecks tree with
              | [ c ] ->
                  Expect.equal
                      c.Grade
                      (Fuaran.UI.PreEmitValidate.BindingGrade.Unchecked(
                          Fuaran.UI.PreEmitValidate.UncheckedReason.NoStaticSchema "Query"
                      ))
                      "the grade says why"
              | other -> failtestf "expected one reader, got %A" other
          } ]
