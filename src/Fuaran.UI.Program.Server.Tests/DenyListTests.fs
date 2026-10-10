module Fuaran.UI.Program.Server.Tests.DenyListTests

// ─── A deny-list: "everything except these" without a universe ───────
//
// Phase 1975. The argument policy could say which values an argument MAY carry
// and could not say which it may NOT. A domain whose gate is a lock — a set of
// names nothing may touch, everything else writable — had one spelling for it:
// an allow-list over every name that existed when the policy was written. That
// universe is stale the moment a handler creates a name and then addresses it,
// which the third witness (a document pipeline under a server placement) found
// as an insert-then-edit the domain's own gate admitted and this policy refused.
//
// Four claims are load-bearing here, each checked rather than asserted:
//
//   a locked name is refused BEFORE anything performs — the op performer's own
//   record is the evidence, as the host performer's is for the allow-list;
//
//   a name created during the run is ADMITTED under a deny-list, where the
//   allow-list over the pre-run names refuses the very same sequence;
//
//   a deny-list and an allow-list on ONE argument compose as a lock over a
//   writable set — a value on both is refused — in either declaration order;
//
//   clauses of DIFFERENT kinds are checked in declaration order, so a value
//   failing a ceiling and a deny-list is reported under whichever came first.

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Renderer.BindingResolver
open Fuaran.Program.Bounded
open Fuaran.UI.Program
open Fuaran.Program.Server
open Fuaran.UI.Program.Server

// ─── fixtures ────────────────────────────────────────────────────────

let private Endpoint = "/handlers/edit"

let private tree: Node<obj> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<obj> with
            Children =
                [ Fuaran.button
                      "call"
                      { Defaults.button<obj> with
                          Label = TextSource.Literal "call"
                          OnClick = Action.Call(Endpoint, None, None) } ] }

let private store: ServerStore = { Tree = tree; Bindings = empty }

let private sources: string -> Result<Fuaran.Core.Table, Fuaran.Compute.EvalError> =
    Fuaran.Compute.DataFrame.noResolve

/// The names that exist BEFORE the run — the only universe an allow-list
/// standing in for a lock could be computed over.
let private universe = [ "root"; "call" ]

let private registryWith (clauses: ServerConstraintClause list) =
    ServerEffectRegistry.denyAll
    |> ServerEffectRegistry.permissive
    |> ServerEffectRegistry.constrain "ApplyOps" clauses

let private editing (ops: TreeOp<obj> list) : Handler =
    { Name = "edit"
      Stages = [ Effect(ServerEffect.ApplyOps ops) ] }

/// Run under a REGISTERED op performer that records every op it performs. The
/// record is the probe: a refusal that happened after the performer ran would
/// leave it non-empty.
let private runRecording (registry: ServerEffectRegistry) (handler: Handler) =
    let performed = ref []

    let outcome =
        Fuaran.Program.Server.Handler.runWith
            UiWitness.witness
            registry
            (OpPerformance.performedWithoutReceipt (fun _ op ->
                performed.Value <- performed.Value @ [ op ]
                Ok()))
            sources
            "call"
            handler
            store

    outcome, performed.Value

let private failedReason (outcome: HandlerOutcome) : (string * string) option =
    outcome.Diagnostics
    |> List.tryPick (fun diagnostic ->
        match diagnostic with
        | ServerDiagnostic.Failed(capability, reason) -> Some(capability, reason)
        | _ -> None)

/// A block the handler CREATES, and then edits: the name it addresses did not
/// exist when any policy was written.
let private fresh: Node<obj> =
    Fuaran.button
        "fresh"
        { Defaults.button<obj> with
            Label = TextSource.Literal "fresh" }

let private insertThenEdit: TreeOp<obj> list =
    [ TreeOp.InsertChild(NodeId "root", fresh)
      TreeOp.UpdateProp(NodeId "fresh", "Label", PropValue.Native(box (TextSource.Literal "edited"))) ]

let private removing (target: string) =
    ServerEffect.ApplyOps [ TreeOp.RemoveNode(NodeId target) ]

let private checkUnder (clauses: ServerConstraintClause list) effect =
    ServerArgumentPolicy.check UiWitness.state (registryWith clauses) effect

// ─── tests ───────────────────────────────────────────────────────────

[<Tests>]
let tests =
    testList
        "a deny-list is the locked set's own spelling (Phase 1975)"
        [ testList
              "the clause, at the placement"
              [ test "PROBE TARGET — a locked name refuses the op that names it before anything performs" {
                    let registry =
                        registryWith [ ServerConstraintClause.DenyList("target", [ "call" ]) ]

                    let refused, performed =
                        runRecording registry (editing [ TreeOp.RemoveNode(NodeId "call") ])

                    Expect.isFalse refused.Committed "a locked target is refused"
                    Expect.isEmpty performed "and the op performer never ran"
                    Expect.isEmpty refused.Performed "and nothing is reported performed"

                    Expect.equal
                        (failedReason refused)
                        (Some("ApplyOps", "argument-not-allowed:target"))
                        "under the same closed token the allow-list uses, naming the argument and never the value"

                    Expect.equal
                        (Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode refused.Store.Tree)
                        (Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode store.Tree)
                        "the tree is the entry tree"

                    // The other direction: the same run with nothing
                    // locked performs the op, so the empty record above is a
                    // refusal and not a performer that never records.
                    let admitted, performed =
                        runRecording (registryWith []) (editing [ TreeOp.RemoveNode(NodeId "call") ])

                    Expect.isTrue admitted.Committed "unlocked, the same op is admitted"
                    Expect.hasLength performed 1 "and performed"
                }

                test "PROBE TARGET — an insert-then-edit of a NEW block is admitted under a deny-list" {
                    // The case an allow-list over the pre-run names gets wrong:
                    // `fresh` is in no universe read before the run, so the
                    // allow-list refuses the edit of a block the handler itself
                    // just created. The deny-list needs no universe, and admits it.
                    let underDeny, performed =
                        runRecording
                            (registryWith [ ServerConstraintClause.DenyList("target", [ "call" ]) ])
                            (editing insertThenEdit)

                    Expect.isTrue underDeny.Committed "admitted: nothing it names is locked"
                    Expect.equal (failedReason underDeny) None "with no refusal"
                    Expect.hasLength performed 2 "and both ops are performed"

                    let underAllow, performed =
                        runRecording
                            (registryWith [ ServerConstraintClause.AllowList("target", universe) ])
                            (editing insertThenEdit)

                    Expect.isFalse underAllow.Committed "the allow-list over the pre-run names refuses it"
                    Expect.isEmpty performed "before anything performs"

                    Expect.equal
                        (failedReason underAllow)
                        (Some("ApplyOps", "argument-not-allowed:target"))
                        "which is the disagreement the deny-list exists to remove"
                }

                test "a deny-list the effect names nothing under is vacuously satisfied" {
                    // The same reading the allow-list takes: a deny-list bounds
                    // what the effect reaches under that name, and an effect
                    // naming nothing there reaches nothing there.
                    let locked = [ ServerConstraintClause.DenyList("target", [ "call" ]) ]
                    Expect.equal (checkUnder locked (ServerEffect.ApplyOps [])) (Ok()) "an empty sequence"

                    Expect.equal
                        (checkUnder [ ServerConstraintClause.DenyList("url", [ "call" ]) ] (removing "call"))
                        (Ok())
                        "an argument the ops never name"

                    Expect.equal
                        (checkUnder [ ServerConstraintClause.DenyList("target", []) ] (removing "call"))
                        (Ok())
                        "and an EMPTY refused list refuses nothing"
                }

                test "a deny-list binds every value the effect carries under the argument" {
                    // A sequence that reaches one locked name has reached it,
                    // whatever else it reaches beside it.
                    Expect.equal
                        (checkUnder
                            [ ServerConstraintClause.DenyList("target", [ "call" ]) ]
                            (ServerEffect.ApplyOps
                                [ TreeOp.InsertChild(NodeId "root", fresh)
                                  TreeOp.RemoveNode(NodeId "fresh")
                                  TreeOp.RemoveNode(NodeId "call") ]))
                        (Error(ServerConstraintDefect.OffList "target"))
                        "the third op is locked"
                }

                test "deny and allow on one argument compose as Locked over Writable, in either order" {
                    // Writable = root, call; Locked = call. The lock wins, so a
                    // name on both is refused; a name on neither is refused by
                    // the allow-list; only writable-and-unlocked is admitted.
                    let writable = ServerConstraintClause.AllowList("target", universe)
                    let locked = ServerConstraintClause.DenyList("target", [ "call" ])

                    for clauses in [ [ locked; writable ]; [ writable; locked ] ] do
                        Expect.equal (checkUnder clauses (removing "root")) (Ok()) "writable and unlocked"

                        Expect.equal
                            (checkUnder clauses (removing "call"))
                            (Error(ServerConstraintDefect.OffList "target"))
                            "on both lists: refused"

                        Expect.equal
                            (checkUnder clauses (removing "elsewhere"))
                            (Error(ServerConstraintDefect.OffList "target"))
                            "off the writable set: refused"
                }

                test "a Ceiling beside a DenyList is checked in declaration order" {
                    // One effect failing BOTH: over a one-byte ceiling and naming a
                    // locked target. Which defect is reported is the order the host
                    // declared — the policy is one statement, read as written.
                    let ceiling = ServerConstraintClause.Ceiling 1
                    let locked = ServerConstraintClause.DenyList("target", [ "call" ])

                    Expect.equal
                        (checkUnder [ ceiling; locked ] (removing "call"))
                        (Error(ServerConstraintDefect.OverCeiling 1))
                        "ceiling first"

                    Expect.equal
                        (checkUnder [ locked; ceiling ] (removing "call"))
                        (Error(ServerConstraintDefect.OffList "target"))
                        "deny-list first"

                    Expect.equal
                        (checkUnder [ locked; ceiling ] (removing "root"))
                        (Error(ServerConstraintDefect.OverCeiling 1))
                        "and an unlocked name still meets the ceiling after it"
                }

                test "the refusal's description is the allow-list's, unchanged" {
                    Expect.equal
                        (ServerArgumentPolicy.describe (ServerConstraintDefect.OffList "target"))
                        "argument-not-allowed:target"
                        "one closed token for both lists"
                } ]

          testList
              "the clause, in the demanded document"
              [ test "the document carries the deny-list beside the capability, and round-trips" {
                    let handlers = Map.ofList [ Endpoint, editing [ TreeOp.RemoveNode(NodeId "call") ] ]

                    let registry =
                        registryWith
                            [ ServerConstraintClause.DenyList("target", [ "call"; "root"; "call" ])
                              ServerConstraintClause.AllowList("target", universe) ]

                    let document = ServerDemanded.ofTreeHandlersAndRegistry registry handlers tree

                    match document.Server with
                    | Some tier ->
                        Expect.equal
                            tier.Constraints
                            [ { Capability = "ApplyOps"
                                Clauses =
                                  [ ServerConstraintClause.AllowList("target", [ "call"; "root" ])
                                    ServerConstraintClause.DenyList("target", [ "call"; "root" ]) ] } ]
                            "both lists, each a sorted set, in the canonical clause order"
                    | None -> failtest "the walk produced no server tier"

                    let json = Demanded.encode document

                    Expect.stringContains
                        json
                        """{"clause":"denyList","argument":"target","refused":["call","root"]}"""
                        "the wire spelling"

                    Expect.stringContains json (sprintf "\"version\":%d" Demanded.Version) "at the current version"
                    Expect.equal (Demanded.decode json) (Ok document) "and the document's own reader reads it back"
                }

                test "PROBE TARGET — a host that SHORTENED its deny-list recomputes to a different document" {
                    let handlers = Map.ofList [ Endpoint, editing [ TreeOp.RemoveNode(NodeId "call") ] ]

                    let documentOf clauses =
                        Demanded.encode (ServerDemanded.ofTreeHandlersAndRegistry (registryWith clauses) handlers tree)

                    Expect.notEqual
                        (documentOf [ ServerConstraintClause.DenyList("target", [ "call"; "root" ]) ])
                        (documentOf [ ServerConstraintClause.DenyList("target", [ "call" ]) ])
                        "an unlocked name is visible"

                    Expect.notEqual
                        (documentOf [ ServerConstraintClause.DenyList("target", [ "call" ]) ])
                        (documentOf [ ServerConstraintClause.AllowList("target", [ "call" ]) ])
                        "and a deny-list is never the allow-list over the same names"

                    Expect.equal
                        (documentOf
                            [ ServerConstraintClause.Label "locked"
                              ServerConstraintClause.DenyList("target", [ "root"; "call" ]) ])
                        (documentOf
                            [ ServerConstraintClause.DenyList("target", [ "call"; "root" ])
                              ServerConstraintClause.Label "locked" ])
                        "the same policy written in a different order encodes alike"
                }

                test "a denyList clause carrying an undeclared member is refused, not read" {
                    // The discriminator selects the members: a deny-list with a
                    // `permitted` array is not an allow-list with a stray bound.
                    let document =
                        """{"kind":"demanded","version":%d,"effects":[],"hostCalls":[],"stateNamespaces":[],"opaqueHandlers":[],"iterations":[],"opaqueLeaves":[],"values":[],"server":{"effects":[],"capabilities":[],"functions":[],"channels":[],"reach":[],"replay":[],"undo":[],"constraints":[{"capability":"ApplyOps","clauses":[{"clause":"denyList","argument":"target","refused":["call"],"permitted":["root"]}]}]}}"""

                    match Demanded.decode (document.Replace("%d", string Demanded.Version)) with
                    | Error failure ->
                        Expect.equal failure.Defect DemandedDefect.UndeclaredMember "refused for the stray member"
                    | Ok _ -> failtest "a deny-list carrying an allow-list's member was read"
                } ] ]
