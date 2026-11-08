module Fuaran.UI.Tests.CoreLoweringTests

open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.PreEmitValidate

// ============================================================================
//  Phase 2312 — the tier's one lowering into Core's finding.
//
//  A pre-emit defect enters the one finding shape through `findingOf`, and any
//  finding (pre-emit, query-binding, dead-on-decode, build-time) reaches Core
//  only through `Finding.toCore`. These pin that the lowering keeps the code,
//  severity and message `describe` mints, keeps the node, carries the §4d
//  recovery as Core's guidance, and leaves the cross-host code projection
//  (`Validator.canonicalCodes`) exactly the codes the walk reported.
// ============================================================================

type private Msg = NoOp

let private markdown id text : Node<Msg> = Fuaran.markdown id text

let private dashboard id children : Node<Msg> =
    Fuaran.dashboard
        id
        { Defaults.dashboard<Msg> with
            Children = children }

/// A tree with a duplicated id and an empty id: two whole-walk defects, one
/// located and one about the whole tree.
let private faulty: Node<Msg> =
    dashboard "root" [ markdown "dup" "a"; markdown "dup" "b"; markdown "" "c" ]

let private defectsOf (node: Node<Msg>) : PreEmitDefect list =
    match validate node with
    | Ok() -> []
    | Error defects -> defects

[<Tests>]
let tests =
    testList
        "Phase 2312 - the one lowering into Core"
        [ test "findingOf keeps describe's code, severity and message for every defect of a tree" {
              let defects = defectsOf faulty
              Expect.isNonEmpty defects "the fixture is faulty"

              for d in defects do
                  let code, severity, message = describe d
                  let f = findingOf d
                  Expect.equal (f.Code, f.Severity, f.Message) (code, severity, message) (describeVariant d)
          }

          test "a located defect keeps its node; a whole-tree defect has none" {
              let dup = findingOf (PreEmitDefect.DuplicateNodeId("dup", 2))
              Expect.equal dup.Location (Some "dup") "the duplicated id is the node"
              Expect.equal (findingOf PreEmitDefect.EmptyNodeId).Location None "an empty id names no node"

              let lowered = toDefect id (PreEmitDefect.DuplicateNodeId("dup", 2))
              Expect.equal lowered.Node (Some "dup") "the lowering keeps the node"
              Expect.equal lowered.Code dup.Code "and the code"
          }

          test "the recovery fields ride as Core's guidance; a finding stating neither is unguided" {
              let code = FindingCodes.queryColumnAbsent

              let bare = Finding.create code "n1" "column 'x' is absent"
              Expect.isNone (Finding.toCore Some bare).Guidance "no remedy, no alternatives: unguided"

              let guided =
                  bare |> Finding.withRecovery [ "revenue"; "region" ] (Some "bind 'revenue'")

              let lowered = Finding.toCore Some guided
              Expect.equal lowered.Defect.Node (Some "n1") "located"
              Expect.equal lowered.Defect.Code code.Code "the code"
              Expect.equal lowered.Defect.Severity code.Severity "the severity"

              Expect.equal
                  (lowered.Guidance |> Option.map (fun g -> g.Message, g.Alternatives))
                  (Some("bind 'revenue'", [ "revenue"; "region" ]))
                  "the remedy is the guidance's message, the available fields its alternatives"

              let remedyOnly = Finding.toCore Some (bare |> Finding.withRemedy "fix it")

              Expect.equal
                  (remedyOnly.Guidance |> Option.map (fun g -> g.Message, g.Alternatives))
                  (Some("fix it", []))
                  "a remedy alone is guided with no alternatives"
          }

          test "canonicalCodes over the lowered findings is the walk's own codes" {
              let defects = defectsOf faulty
              let lowered = defects |> List.map (toDefect id)

              let expected =
                  defects
                  |> List.map (fun d ->
                      let code, _, _ = describe d
                      code)
                  |> List.sort
                  |> Hash.canonicalFields

              Expect.equal (Validator.canonicalCodes lowered) expected "the code projection is unchanged"
              Expect.isTrue (Validator.hasErrors lowered) "the duplicate id is an error under Core's reading too"
          }

          test "the walk as one Core rule family reports what validate reports, node kept" {
              let w: NodeWitness<Node<Msg>, string> = Generated.nodeWitness

              let reg: Validator.RuleRegistry<Node<Msg>, string> =
                  { Families = [ ruleFamily<Msg, string> id ] }

              let viaCore = Validator.runAll w reg faulty
              let direct = defectsOf faulty |> List.map (toDefect id)

              Expect.equal
                  (viaCore |> List.map (fun d -> d.Code, d.Node, d.Message))
                  (direct |> List.map (fun d -> d.Code, d.Node, d.Message))
                  "same findings, same order"

              Expect.isTrue (viaCore |> List.forall (fun d -> d.Family = "Fuaran.UI.PreEmitValidate")) "stamped"
          } ]
