module Fuaran.Program.Server.Tests.EffectConstraintTests

// ─── The gate decides on ARGUMENTS, not only on the effect name ───────
//
// A capability name is the coarse decision and it is not the whole one. "May
// this session call out" and "may it call out THERE" are different questions,
// and the second is the one the confused deputy turns on: a permitted host call
// carrying an endpoint that came off the wire reaches wherever the wire said.
//
// Four claims are load-bearing here, and each is checked rather than asserted:
//
//   the check runs BEFORE the performer — an off-list call leaves the
//   performer's own record untouched, which is the only evidence that "refused
//   before it ran" is a fact about execution and not about a return value;
//
//   the gate's two halves compose in ONE direction — a capability the gate
//   refuses by name never has its arguments examined at all;
//
//   an UNDECLARED constraint is unconstrained — a host that declares nothing
//   behaves exactly as it did before any of this existed, and the document says
//   so by silence rather than by an empty bound;
//
//   the declared policy travels in the DOCUMENT and is RECOMPUTED — a host that
//   relaxed a bound since a record was signed produces a different document from
//   the one it signed.

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Renderer.BindingResolver
open Fuaran.Program.Bounded
open Fuaran.Program.UI
open Fuaran.Program.Server
open Fuaran.Program.Server.UI

// ─── fixtures ────────────────────────────────────────────────────────

let private jstr (s: string) = Fuaran.Core.JStr s

let private Endpoint = "/handlers/fetch"

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

/// A host call to `fetch` carrying `url` — the shape the whole phase is about:
/// an argument a generated tree can put a value into, under a capability a host
/// was willing to permit.
let private fetching (url: string) : Handler =
    { Name = "fetch"
      Stages = [ Effect(ServerEffect.HostCall("fetch", Fuaran.Core.JObj [ "url", jstr url ], None)) ] }

/// A registry whose gate permits everything and whose one performer RECORDS
/// that it ran. The record is the probe: a refusal that happened after the
/// performer would leave it non-empty.
let private permitting (ran: string list ref) =
    ServerEffectRegistry.denyAll
    |> ServerEffectRegistry.register "fetch" (fun args ->
        ran.Value <- ran.Value @ [ ProgramWire.render args ]
        Ok(jstr "fetched"))
    |> ServerEffectRegistry.register "send" (fun _ -> Ok(jstr "sent"))
    |> ServerEffectRegistry.permissive

let private allowing (values: string list) =
    ServerConstraintClause.AllowList("url", values)

/// The first `Failed` diagnostic's reason, or `None` when the outcome carries
/// no such diagnostic.
let private failedReason (outcome: HandlerOutcome) : (string * string) option =
    outcome.Diagnostics
    |> List.tryPick (fun diagnostic ->
        match diagnostic with
        | ServerDiagnostic.Failed(capability, reason) -> Some(capability, reason)
        | _ -> None)

let private denials (outcome: HandlerOutcome) : ServerEffectDenial list =
    outcome.Diagnostics
    |> List.choose (fun diagnostic ->
        match diagnostic with
        | ServerDiagnostic.Denied denial -> Some denial
        | _ -> None)

// ─── tests ───────────────────────────────────────────────────────────

[<Tests>]
let tests =
    testList
        "server effect-argument policy"
        [ testList
              "an allow-list decides, and it decides before the performer"
              [ test "PROBE TARGET — an off-list endpoint is refused BEFORE the performer runs" {
                    // The probe is `ran`, not the outcome: a check placed after
                    // the performer would produce this same uncommitted outcome
                    // with the call already made, which is the failure the
                    // ordering exists to prevent. Deleting the allow-list arm
                    // from `checkClause` turns this red on `ran` first.
                    let ran = ref []

                    let registry =
                        permitting ran
                        |> ServerEffectRegistry.constrain "host:fetch" [ allowing [ "api.example.com" ] ]

                    let outcome =
                        Handler.run registry sources "call" (fetching "evil.example.net") store

                    Expect.isEmpty ran.Value "the performer never ran"
                    Expect.isFalse outcome.Committed "and nothing committed"
                    Expect.isEmpty outcome.Performed "nothing is recorded as performed"

                    Expect.equal
                        (failedReason outcome)
                        (Some("host:fetch", "argument-not-allowed:url"))
                        "the refusal names the capability and the CONSTRAINT that refused it"

                    Expect.isEmpty
                        (denials outcome)
                        "and it is not reported as a denial: the gate admitted the capability"
                }

                test "the refusal names the host's own declaration and never the value it refused" {
                    let registry =
                        permitting (ref [])
                        |> ServerEffectRegistry.constrain "host:fetch" [ allowing [ "api.example.com" ] ]

                    let outcome =
                        Handler.run registry sources "call" (fetching "secrets.example.net") store

                    match failedReason outcome with
                    | Some(_, reason) ->
                        Expect.isFalse
                            (reason.Contains "secrets.example.net")
                            "the off-list value came off the wire and is never echoed"
                    | None -> failtest "expected a refusal"
                }

                test "an in-list endpoint passes, and the performer runs" {
                    let ran = ref []

                    let registry =
                        permitting ran
                        |> ServerEffectRegistry.constrain "host:fetch" [ allowing [ "api.example.com" ] ]

                    let outcome = Handler.run registry sources "call" (fetching "api.example.com") store

                    Expect.isTrue outcome.Committed "the bound admitted it"
                    Expect.equal (List.length ran.Value) 1 "and the performer ran exactly once"
                    Expect.equal outcome.Performed [ "host:fetch" ] "recorded as performed"
                }

                test "an allow-list the effect names nothing under is vacuously satisfied" {
                    // A bound on `url` says what may be reached UNDER that name.
                    // A call naming nothing there reaches nothing there — so it
                    // is admitted, and a host wanting the argument to be
                    // mandatory is asking for a clause it did not declare.
                    let ran = ref []

                    let registry =
                        permitting ran
                        |> ServerEffectRegistry.constrain "host:fetch" [ allowing [ "api.example.com" ] ]

                    let handler =
                        { Name = "fetch"
                          Stages =
                            [ Effect(ServerEffect.HostCall("fetch", Fuaran.Core.JObj [ "note", jstr "hello" ], None)) ] }

                    let outcome = Handler.run registry sources "call" handler store

                    Expect.isTrue outcome.Committed "no value under the constrained argument, no refusal"
                    Expect.equal (List.length ran.Value) 1 "the performer ran"
                } ]

          testList
              "the same clause covers the other two argument-bearing arms"
              [ test "a notification's channel is allow-listed under its own argument name" {
                    let registry =
                        permitting (ref [])
                        |> ServerEffectRegistry.constrain
                            "Notify"
                            [ ServerConstraintClause.AllowList(ServerArgumentPolicy.ChannelArgument, [ "ops" ]) ]

                    let notifying channel =
                        { Name = "notify"
                          Stages = [ Effect(ServerEffect.Notify(channel, jstr "body")) ] }

                    let refused = Handler.run registry sources "call" (notifying "pager") store
                    let admitted = Handler.run registry sources "call" (notifying "ops") store

                    Expect.equal
                        (failedReason refused)
                        (Some("Notify", "argument-not-allowed:channel"))
                        "the off-list channel is refused, naming the constraint"

                    Expect.isTrue admitted.Committed "the declared one is admitted"
                }

                test "a query's by-reference sources are allow-listed, every one of them" {
                    let registry =
                        permitting (ref [])
                        |> ServerEffectRegistry.constrain
                            "RunQuery"
                            [ ServerConstraintClause.AllowList(ServerArgumentPolicy.SourceArgument, [ "orders" ]) ]

                    let querying source =
                        { Name = "query"
                          Stages = [ Effect(ServerEffect.RunQuery("slot", Fuaran.Core.Ref source, [])) ] }

                    let refused = Handler.run registry sources "call" (querying "payroll") store

                    Expect.equal
                        (failedReason refused)
                        (Some("RunQuery", "argument-not-allowed:source"))
                        "a source outside the declared set is refused before the pipeline is evaluated"

                    // A stage's SECOND source is reached too: a pipeline that
                    // joins an off-list table has reached it, whatever its
                    // primary source was.
                    let joining =
                        { Name = "query"
                          Stages =
                            [ Effect(
                                  ServerEffect.RunQuery(
                                      "slot",
                                      Fuaran.Core.Ref "orders",
                                      [ Fuaran.Compute.Union(Fuaran.Core.Ref "payroll") ]
                                  )
                              ) ] }

                    Expect.equal
                        (failedReason (Handler.run registry sources "call" joining store))
                        (Some("RunQuery", "argument-not-allowed:source"))
                        "the pipeline's own source is checked, not only the query's"
                } ]

          testList
              "a ceiling bounds the declarative payload"
              [ test "an over-ceiling payload is refused, naming the LIMIT and not the size" {
                    let ran = ref []

                    let registry =
                        permitting ran
                        |> ServerEffectRegistry.constrain "host:fetch" [ ServerConstraintClause.Ceiling 16 ]

                    let big = String.replicate 100 "x"

                    let outcome =
                        Handler.run
                            registry
                            sources
                            "call"
                            { Name = "fetch"
                              Stages = [ Effect(ServerEffect.HostCall("fetch", jstr big, None)) ] }
                            store

                    Expect.isEmpty ran.Value "the performer never ran"

                    Expect.equal
                        (failedReason outcome)
                        (Some("host:fetch", "payload-over-ceiling:16"))
                        "the host's own declared limit, and nothing measured from the payload"
                }

                test "a payload at or under the ceiling passes" {
                    let ran = ref []

                    let registry =
                        permitting ran
                        |> ServerEffectRegistry.constrain "host:fetch" [ ServerConstraintClause.Ceiling 4096 ]

                    let outcome = Handler.run registry sources "call" (fetching "api.example.com") store

                    Expect.isTrue outcome.Committed "under the ceiling"
                    Expect.equal (List.length ran.Value) 1 "the performer ran"
                }

                test "the size measured is the canonical encoding's bytes" {
                    let payload = Fuaran.Core.JObj [ "url", jstr "api.example.com" ]

                    Expect.equal
                        (ServerArgumentPolicy.payloadBytes
                            UiWitness.state
                            (ServerEffect.HostCall("fetch", payload, None)))
                        (System.Text.Encoding.UTF8.GetByteCount(ProgramWire.render payload))
                        "the bytes the wire carries, not an in-memory estimate"

                    // Since Phase 1967 an op sequence IS measured: the sum of its
                    // ops' canonical bytes, through the op witness's own encoder —
                    // the same bytes the handler's wire form splices in.
                    let op = TreeOp.RemoveNode(NodeId "call")

                    Expect.equal
                        (ServerArgumentPolicy.payloadBytes UiWitness.state (ServerEffect.ApplyOps [ op; op ]))
                        (2 * System.Text.Encoding.UTF8.GetByteCount(UiWitness.state.Stream.Encode op))
                        "an op sequence is measured as its ops' canonical bytes, summed"
                } ]

          testList
              "an undeclared constraint is unconstrained, and a refused capability is never examined"
              [ test "PROBE TARGET — a capability nobody constrained admits any argument" {
                    // The whole compatibility claim: a host that declares no
                    // policy behaves exactly as it did before this existed.
                    // Making `constraintsFor` default-deny instead of returning
                    // an empty list turns this red.
                    let ran = ref []

                    let outcome =
                        Handler.run (permitting ran) sources "call" (fetching "anywhere.example.net") store

                    Expect.isTrue outcome.Committed "no declaration, no bound"
                    Expect.equal (List.length ran.Value) 1 "the performer ran"
                    Expect.isNone (failedReason outcome) "and nothing was refused"
                }

                test "a capability the gate refuses by NAME never has its arguments examined" {
                    // The composition runs one way. A refusal that reported the
                    // argument bound here would tell a caller something about a
                    // call the host was never going to make.
                    let registry =
                        permitting (ref [])
                        |> ServerEffectRegistry.withGate (fun _ -> false)
                        |> ServerEffectRegistry.constrain "host:fetch" [ allowing [ "api.example.com" ] ]

                    let outcome =
                        Handler.run registry sources "call" (fetching "evil.example.net") store

                    Expect.equal
                        (denials outcome)
                        [ ServerEffectDenial.GateRefused "host:fetch" ]
                        "refused by name, as a denial"

                    Expect.isNone (failedReason outcome) "and never as an argument refusal"
                }

                test "a label decides nothing" {
                    let ran = ref []

                    let registry =
                        permitting ran
                        |> ServerEffectRegistry.constrain "host:fetch" [ ServerConstraintClause.Label "pii" ]

                    Expect.isTrue
                        (Handler.run registry sources "call" (fetching "anywhere.example.net") store).Committed
                        "a label is a word for a deployer, not a policy this gate enforces"

                    Expect.equal (List.length ran.Value) 1 "the performer ran"
                } ]

          testList
              "the declared policy travels in the document, and is recomputed"
              [ test "the envelope carries the bound beside the capability that is bounded" {
                    let handlers = Map.ofList [ Endpoint, fetching "api.example.com" ]

                    let registry =
                        permitting (ref [])
                        |> ServerEffectRegistry.constrain
                            "host:fetch"
                            [ allowing [ "api.example.com" ]
                              ServerConstraintClause.Ceiling 65536
                              ServerConstraintClause.Label "pii" ]

                    let document = ServerDemanded.ofTreeHandlersAndRegistry registry handlers tree

                    match document.Server with
                    | Some tier ->
                        Expect.equal
                            tier.Constraints
                            [ { Capability = "host:fetch"
                                Clauses =
                                  [ ServerConstraintClause.AllowList("url", [ "api.example.com" ])
                                    ServerConstraintClause.Ceiling 65536
                                    ServerConstraintClause.Label "pii" ] } ]
                            "'HTTP to api.example.com, <= 64 KB' rather than 'HTTP'"
                    | None -> failtest "the walk produced no server tier"

                    Expect.equal
                        (Demanded.decode (Demanded.encode document))
                        (Ok document)
                        "and it round-trips through the document's own reader"
                }

                test "a capability the handlers cannot reach contributes no clause" {
                    // The document is about THIS program on this host. A bound on
                    // something these handlers never name would be the host's
                    // whole registry leaking into it.
                    let handlers = Map.ofList [ Endpoint, fetching "api.example.com" ]

                    let registry =
                        permitting (ref [])
                        |> ServerEffectRegistry.constrain "host:send" [ allowing [ "api.example.com" ] ]

                    match (ServerDemanded.ofTreeHandlersAndRegistry registry handlers tree).Server with
                    | Some tier ->
                        Expect.isEmpty tier.Constraints "no clause for a capability this program cannot exercise"
                    | None -> failtest "the walk produced no server tier"
                }

                test "PROBE TARGET — a host that RELAXED a bound recomputes to a different document" {
                    // This is what signing the policy buys. Without it both
                    // registries produce the same bytes and a verifier reading a
                    // signed record could not tell whether the bound still held.
                    let handlers = Map.ofList [ Endpoint, fetching "api.example.com" ]
                    let ran = ref []

                    let tight =
                        permitting ran
                        |> ServerEffectRegistry.constrain "host:fetch" [ allowing [ "api.example.com" ] ]

                    let relaxed =
                        permitting ran
                        |> ServerEffectRegistry.constrain
                            "host:fetch"
                            [ allowing [ "api.example.com"; "evil.example.net" ] ]

                    let dropped = permitting ran

                    let documentOf registry =
                        Demanded.encode (ServerDemanded.ofTreeHandlersAndRegistry registry handlers tree)

                    Expect.notEqual (documentOf tight) (documentOf relaxed) "a widened allow-list is visible"
                    Expect.notEqual (documentOf tight) (documentOf dropped) "a dropped clause is visible"

                    Expect.notEqual
                        (documentOf tight)
                        (Demanded.encode (ServerDemanded.ofTreeAndHandlers handlers tree))
                        "and the policy-free walk is a different document again, never silently the same one"
                }

                test "two hosts with the same policy written in a different order encode alike" {
                    let handlers = Map.ofList [ Endpoint, fetching "api.example.com" ]

                    let documentOf clauses =
                        Demanded.encode (
                            ServerDemanded.ofTreeHandlersAndRegistry
                                (permitting (ref []) |> ServerEffectRegistry.constrain "host:fetch" clauses)
                                handlers
                                tree
                        )

                    Expect.equal
                        (documentOf
                            [ ServerConstraintClause.Label "pii"
                              allowing [ "b.example.com"; "a.example.com" ] ])
                        (documentOf
                            [ allowing [ "a.example.com"; "b.example.com" ]
                              ServerConstraintClause.Label "pii" ])
                        "the document's determinism reaches into the clause list and into a permitted set"
                } ]

          testList
              "the argument surface is derived from the effect, never described"
              [ test "the op-carrying arms carry what their ops REACH, read through the op witness" {
                    // Phase 1967: before the op witness could say, these two
                    // arms carried no arguments at all, and a handler that wrote
                    // files and pushed a branch was bounded by nothing but the
                    // word `ApplyOps`. At the UI witness an op's reach is the
                    // nodes it addresses, under the op's own member names.
                    for effect in
                        [ ServerEffect.ApplyOps [ TreeOp.RemoveNode(NodeId "call") ]
                          ServerEffect.EmitPatch [ TreeOp.RemoveNode(NodeId "call") ] ] do
                        Expect.equal
                            (ServerArgumentPolicy.arguments UiWitness.state effect)
                            [ "target", "call" ]
                            (ServerEffect.kind effect)

                    Expect.equal
                        (ServerArgumentPolicy.arguments
                            UiWitness.state
                            (ServerEffect.ApplyOps
                                [ TreeOp.MoveNode(NodeId "call", NodeId "root")
                                  TreeOp.RemoveNode(NodeId "call") ]))
                        [ "newParentId", "root"; "target", "call"; "target", "call" ]
                        "every op's reach, in op order, every argument it names"

                    Expect.isEmpty
                        (ServerArgumentPolicy.arguments UiWitness.state (ServerEffect.ApplyOps []))
                        "and an empty sequence reaches nothing"
                }

                test
                    "an allow-list on ApplyOps binds the ops' reach, and an off-list op is refused before anything performs" {
                    // W3, closed: the gate admits `ApplyOps`, the policy binds
                    // which nodes its ops may address, and an op addressing one
                    // off the list halts the handler while PLANNING — the tree
                    // is the entry tree, nothing is performed.
                    let registry =
                        permitting (ref [])
                        |> ServerEffectRegistry.constrain
                            "ApplyOps"
                            [ ServerConstraintClause.AllowList("target", [ "call" ]) ]

                    let run (target: string) =
                        Handler.run
                            registry
                            sources
                            "call"
                            { Name = "edit"
                              Stages = [ Effect(ServerEffect.ApplyOps [ TreeOp.RemoveNode(NodeId target) ]) ] }
                            store

                    let admitted = run "call"
                    Expect.isTrue admitted.Committed "an in-list target is admitted"

                    let refused = run "root"
                    Expect.isFalse refused.Committed "an off-list target is refused"
                    Expect.isEmpty refused.Performed "and nothing was performed"

                    Expect.equal
                        (failedReason refused)
                        (Some("ApplyOps", "argument-not-allowed:target"))
                        "naming the argument"

                    Expect.equal
                        (Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode refused.Store.Tree)
                        (Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode store.Tree)
                        "the tree is the entry tree"
                }

                test "a host call's nested members are not named arguments; its integer members are (Phase 1982)" {
                    // Read ONE LEVEL DEEP, string and integer values only — the
                    // module header states the limit, and this is it as a fact
                    // rather than as prose. The integer joined in Phase 1982, so
                    // an `AtMost` on a number sees the number.
                    let args =
                        Fuaran.Core.JObj
                            [ "url", jstr "api.example.com"
                              "retries", Fuaran.Core.JInt 3
                              "request", Fuaran.Core.JObj [ "url", jstr "nested.example.net" ] ]

                    Expect.equal
                        (ServerArgumentPolicy.arguments UiWitness.state (ServerEffect.HostCall("fetch", args, None)))
                        [ "url", "api.example.com"; "retries", "3" ]
                        "a nested value is NOT reachable by an allow-list; the bound belongs on the top-level argument"
                } ] ]
