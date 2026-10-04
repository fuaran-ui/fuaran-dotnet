module Fuaran.Program.Server.Tests.ServerDemandedTests

// ─── The demanded projection's SERVER tier ───────────────────────────
//
// The client tier's suite pins three things and says the third keeps the other
// two honest. The same three apply here, with one addition that is specific to
// this placement and is the reason the tier exists at all:
//
//  1. WHAT IS DEMANDED — every server-effect arm contributes its discriminator,
//     the capability the gate will be asked about, the names it reaches, and the
//     namespace a landing slot writes; and a `Compute` stage contributes to the
//     CLIENT tier, unchanged, because it is the same fold running the same
//     action.
//  2. WHAT IS REFUSED — an unregistered host function is named; a registered one
//     the gate refuses is named DIFFERENTLY, mirroring the runtime denial DU.
//  3. THAT THE CHECK CAN GO RED — the probe widens a HANDLER's demand past a
//     fixed host's coverage and asserts the checker reports it, at the document
//     and at the strict construction path.
//  4. THAT THE NAMES CANNOT DRIFT — every demanded name is compared against the
//     value the interpreter and the gate actually use, on the same effect. A
//     projection whose names were a second spelling of the vocabulary would pass
//     every other test here and report nothing in production.

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.ServerDriven
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.Renderer.BindingResolver
open Fuaran.Program.Bounded
open Fuaran.Program.UI
open Fuaran.Program.Server
open Fuaran.Program.Server.UI

// ─── fixtures ────────────────────────────────────────────────────────

let private jstr (s: string) = Fuaran.Core.JStr s

let private endpoint = "/handlers/work"

/// A tree whose one button calls `target`.
let private treeCalling (target: string) : Node<obj> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<obj> with
            Children =
                [ Fuaran.button
                      "call"
                      { Defaults.button<obj> with
                          Label = TextSource.Literal "call"
                          OnClick = Action.Call(target, None, None) } ] }

let private handlerOf (stages: HandlerStage list) : Handler = { Name = "work"; Stages = stages }

/// The five arms, each with a recognisable name, for the drift check.
let private samples: ServerEffect<Fuaran.UI.Ops.Types.TreeOp<obj>> list =
    [ ServerEffect.RunQuery("slot", Fuaran.Core.Embedded({ Schema = []; Columns = [] }: Fuaran.Core.Table), [])
      ServerEffect.ApplyOps []
      ServerEffect.HostCall("sendMail", jstr "x", None)
      ServerEffect.EmitPatch []
      ServerEffect.Notify("ops", jstr "n") ]

/// The server tier of a projection, or a failure that says the walk never ran.
let private tierOf (projection: DemandedProjection) : ServerDemand =
    match projection.Server with
    | Some tier -> tier
    | None -> failtest "expected a server tier: the walk ran, so the document must say so"

/// A registry offering the named host functions under a permissive gate.
let private registryOffering (names: string list) : ServerEffectRegistry =
    names
    |> List.fold
        (fun r name -> ServerEffectRegistry.register name (fun _ -> Ok(jstr "ok")) r)
        ServerEffectRegistry.denyAll
    |> ServerEffectRegistry.permissive

/// A host covering the named server functions, permissive, with the client tier
/// left covering nothing — which constrains nothing here, since these handlers
/// demand no client effect unless a test gives them one.
let private hostOffering (names: string list) : HostCoverage =
    HostCoverage.nothing
    |> HostCoverage.withServer (ServerDemanded.coverageOfRegistry (registryOffering names))

// ─── tests ───────────────────────────────────────────────────────────

[<Tests>]
let tests =
    testList
        "Phase 984 — the demanded projection's server tier"
        [

          // ── what is demanded ──────────────────────────────────────

          test "every demanded name is the string the gate and the interpreter use" {
              // The 892 discipline, applied to the second vocabulary. Not "the
              // projection produces plausible names" but "the projection
              // produces THESE names", compared against the functions the
              // running code calls on the very same effect value. Two spellings
              // of one intention is precisely how a coverage check silently
              // starts reporting nothing.
              for effect in samples do
                  let tier = tierOf (ServerDemanded.ofHandler (handlerOf [ Effect effect ]))

                  Expect.equal
                      tier.Effects
                      [ ServerEffect.kind effect ]
                      $"the discriminator for %A{effect} is the one the vocabulary declares"

                  let gateFacing =
                      (tier.Capabilities @ (tier.Functions |> List.map _.Capability)) |> List.sort

                  Expect.equal
                      gateFacing
                      [ ServerEffect.capability effect ]
                      $"and the gate-facing capability for %A{effect} is the one the gate is asked about"
          }

          test "a host function demands a REGISTRATION key and a capability, together" {
              let tier =
                  tierOf (
                      ServerDemanded.ofHandler (handlerOf [ Effect(ServerEffect.HostCall("sendMail", jstr "x", None)) ])
                  )

              Expect.equal
                  tier.Functions
                  [ { Function = "sendMail"
                      Capability = ServerEffect.capability (ServerEffect.HostCall("sendMail", jstr "x", None)) } ]
                  "the two facts a coverage check needs, and neither derivable from the other by hand"

              Expect.isEmpty
                  tier.Capabilities
                  "and it is NOT in the flat capability list: its capability is namespaced per function, so gating the bare discriminator would permit every host function at once"
          }

          test "a landing slot is a state-namespace write, in the tier that owns that store" {
              let projection =
                  ServerDemanded.ofHandler (
                      handlerOf [ Effect(ServerEffect.HostCall("fn", jstr "x", Some "outbox.last")) ]
                  )

              Expect.equal
                  projection.StateNamespaces
                  [ { Namespace = "outbox"
                      Written = true
                      Read = false } ]
                  "the landing slot writes the SHARED binding store, so it lands beside the fold's own writes rather than in a server-only list"
          }

          test "a query demands the sources it reads, at every depth of the pipeline" {
              let projection =
                  ServerDemanded.ofHandler (
                      handlerOf
                          [ Effect(
                                ServerEffect.RunQuery(
                                    "rows",
                                    Fuaran.Core.Ref "orders",
                                    [ Fuaran.Compute.Union(Fuaran.Core.Ref "archive"); Fuaran.Compute.Distinct ]
                                )
                            ) ]
                  )

              let tier = tierOf projection

              Expect.equal
                  (tier.Channels |> List.map _.Name)
                  [ "archive"; "orders" ]
                  "the source AND the one a pipeline stage joins in — both are names the host's resolver will be handed"

              Expect.isEmpty
                  (projection.StateNamespaces)
                  "and the slot the table lands in is NOT a demand: it is this placement's own query store, not something asked of anyone"
          }

          test "an Except stage demands the source it subtracts" {
              // The set-ops carry a second source exactly as `Join` and `Union`
              // do. Before this arm existed the walk did not merely under-report
              // that source — the match declared itself total and threw on the
              // first handler that used the verb.
              let projection =
                  ServerDemanded.ofHandler (
                      handlerOf
                          [ Effect(
                                ServerEffect.RunQuery(
                                    "rows",
                                    Fuaran.Core.Ref "orders",
                                    [ Fuaran.Compute.Except(Fuaran.Core.Ref "cancelled") ]
                                )
                            ) ]
                  )

              Expect.equal
                  ((tierOf projection).Channels |> List.map _.Name)
                  [ "cancelled"; "orders" ]
                  "the right-hand side of a set-op reaches the host's resolver, exactly as a Union's does"
          }

          test "the op-carrying arms demand what their ops REACH, and the document carries it (Phase 1967)" {
              // Before the op witness could say, these two arms demanded the
              // capability and nothing else. At the UI witness an op's reach is
              // the nodes it addresses, under the op's own member names — and
              // the projection moved DELIBERATELY for a handler whose ops name
              // one: the reach rides a new document member, at version 5 (the undo
              // posture rides version 6, Phase 1977, and moves no byte here).
              let projection =
                  ServerDemanded.ofHandler (
                      handlerOf
                          [ Effect(ServerEffect.ApplyOps [ TreeOp.RemoveNode(NodeId "spinner") ])
                            Effect(ServerEffect.EmitPatch [ TreeOp.MoveNode(NodeId "row", NodeId "list") ]) ]
                  )

              Expect.equal
                  ((tierOf projection).Reach
                   |> List.map (fun r -> r.Capability, r.Argument, r.Name))
                  [ "ApplyOps", "target", "spinner"
                    "EmitPatch", "newParentId", "list"
                    "EmitPatch", "target", "row" ]
                  "every node every op addresses, under the capability it rides"

              Expect.stringContains
                  (Demanded.encode projection)
                  "\"reach\":[{\"capability\":\"ApplyOps\",\"argument\":\"target\",\"name\":\"spinner\"},{\"capability\":\"EmitPatch\",\"argument\":\"newParentId\",\"name\":\"list\"},{\"capability\":\"EmitPatch\",\"argument\":\"target\",\"name\":\"row\"}]"
                  "the document carries the reach beside the channels"

              // The empty sequence reaches nothing, and the key is still
              // present: "walked, and the ops name nothing" is a different
              // fact from a producer that predates the member.
              let none = ServerDemanded.ofHandler (handlerOf [ Effect(ServerEffect.ApplyOps []) ])
              Expect.isEmpty (tierOf none).Reach "nothing reached"
              Expect.stringContains (Demanded.encode none) "\"reach\":[]" "present and empty"
          }

          test "a Compute stage demands what the same action demands anywhere else" {
              // One algebra, read at the projection rather than at the
              // interpreter: a stage's action is run by the shared fold, so its
              // client-tier demands are the fold's, not a server variant.
              let action =
                  Action.Chain
                      [ Action.Navigate(TextSource.Literal "/next", NavigateTarget.Self)
                        Action.SetState("cart.total", Some(jstr "1"), None) ]

              let viaStage = ServerDemanded.ofHandler (handlerOf [ Compute action ])
              let viaFold = Demanded.ofAction action

              Expect.equal viaStage.Effects viaFold.Effects "the same client effects"
              Expect.equal viaStage.StateNamespaces viaFold.StateNamespaces "the same namespaces"

              Expect.isEmpty
                  (tierOf viaStage).Effects
                  "and nothing in the server tier: a Compute stage emits no server effect"
          }

          test "a call action INSIDE a stage demands nothing — it is the documented no-op" {
              // D7's one deliberate boundary. A stage's call runs against the
              // inert arm, so projecting its endpoint would report a demand for
              // a capability no host will ever be asked to cover — the same
              // reasoning D9 gives for dropping the tree-declared result target.
              let projection =
                  ServerDemanded.ofHandler (handlerOf [ Compute(Action.Call("/handlers/nested", None, None)) ])

              Expect.isEmpty projection.HostCalls "the inert endpoint is not a host call here"

              Expect.isNonEmpty
                  (Demanded.ofAction (Action.Call("/handlers/nested", None, None))).HostCalls
                  "…and the difference is the placement, not the walk: the same action IN A TREE does demand it"
          }

          // ── reachability ──────────────────────────────────────────

          test "only handlers the tree can NAME contribute" {
              let handlers =
                  Map.ofList
                      [ endpoint, handlerOf [ Effect(ServerEffect.HostCall("reached", jstr "x", None)) ]
                        "/handlers/elsewhere", handlerOf [ Effect(ServerEffect.HostCall("unreached", jstr "x", None)) ] ]

              let tier = tierOf (ServerDemanded.ofTreeAndHandlers handlers (treeCalling endpoint))

              Expect.equal
                  (tier.Functions |> List.map _.Function)
                  [ "reached" ]
                  "a registered handler no tree reaches is a capability the HOST holds, not one this program can exercise"
          }

          test "an endpoint with no handler contributes nothing, and is not named" {
              // The wire-sourced string, kept out of the document for the same
              // reason `ServerDiagnostic.HandlerUnregistered` refuses to repeat
              // it back. The silence is the rule, not a gap.
              let projection = ServerDemanded.ofTreeAndHandlers Map.empty (treeCalling endpoint)
              let tier = tierOf projection

              Expect.isEmpty tier.Effects "nothing demanded"
              Expect.isEmpty tier.Functions "no functions"
              Expect.isEmpty (Demanded.checkProjection (hostOffering []) projection) "and no finding to leak it through"
          }

          test "a walk that ran and found nothing is a DIFFERENT document from no walk at all" {
              let tree = treeCalling endpoint

              Expect.isNone (Demanded.ofTree tree).Server "the tree-only walk makes no claim about a server placement"

              Expect.isSome
                  (ServerDemanded.ofTreeAndHandlers Map.empty tree).Server
                  "the two-tier walk did make one, and the answer was 'nothing'"
          }

          // ── the projection as a document ──────────────────────────

          test "the two-tier document is deterministic and carries its server tier" {
              let handlers =
                  Map.ofList
                      [ endpoint,
                        handlerOf
                            [ Effect(ServerEffect.ApplyOps [])
                              Effect(ServerEffect.HostCall("sendMail", jstr "x", Some "outbox.last"))
                              Effect(ServerEffect.Notify("ops", jstr "n")) ] ]

              let tree = treeCalling endpoint
              let a = ServerDemanded.ofTreeAndHandlers handlers tree
              let b = ServerDemanded.ofTreeAndHandlers handlers tree

              Expect.equal a b "the same tree and registration project identically"
              Expect.equal (Demanded.encode a) (Demanded.encode b) "and encode to the same bytes"

              let json = Demanded.encode a
              Expect.stringContains json (sprintf "\"version\":%d" Demanded.Version) "the version the encoder emits"

              Expect.stringContains
                  json
                  "\"constraints\":[]"
                  "this walk declares no argument policy — the key is present and EMPTY rather than absent, so 'predates the member' and 'read and empty' stay distinguishable"

              // This walk contributes no posture — the join is a later act, and
              // the key is present and EMPTY rather than absent so the two facts
              // stay distinguishable. See `ReplayTests` for the joined document.
              Expect.stringContains json "\"replay\":[]" "the walk ran and joined no posture"

              Expect.stringContains
                  json
                  "\"effects\":[\"ApplyOps\",\"HostCall\",\"Notify\"]"
                  "the server discriminators, sorted"

              Expect.stringContains json "\"capabilities\":[\"ApplyOps\",\"Notify\"]" "the gate-facing capabilities"
              Expect.stringContains json "\"function\":\"sendMail\"" "the registration key"
              Expect.stringContains json "\"channel\":\"Notify\",\"name\":\"ops\"" "the channel it reaches"
          }

          // ── what is refused ───────────────────────────────────────

          test "a handler naming an UNREGISTERED host function is refused BY NAME" {
              let handlers =
                  Map.ofList [ endpoint, handlerOf [ Effect(ServerEffect.HostCall("sendMail", jstr "x", None)) ] ]

              let findings =
                  Demanded.checkProjection
                      (hostOffering [])
                      (ServerDemanded.ofTreeAndHandlers handlers (treeCalling endpoint))

              Expect.equal
                  findings
                  [ CoverageFinding.UnregisteredServerFunction "sendMail" ]
                  "one finding, naming the function"

              Expect.stringContains
                  (Demanded.describe findings.Head)
                  "sendMail"
                  "and the description names it too — a finding a host cannot act on is not a finding"
          }

          test "registered-but-gate-refused is a DIFFERENT finding from unregistered" {
              // The same distinction the runtime denial DU draws, at the same
              // two names: only the second is resolved by a policy change.
              let handlers =
                  Map.ofList [ endpoint, handlerOf [ Effect(ServerEffect.HostCall("sendMail", jstr "x", None)) ] ]

              let gated =
                  HostCoverage.nothing
                  |> HostCoverage.withServer (
                      ServerCoverage.nothing
                      |> ServerCoverage.withFunctions [ "sendMail" ]
                      |> ServerCoverage.withGate (fun _ -> false)
                  )

              Expect.equal
                  (Demanded.checkProjection gated (ServerDemanded.ofTreeAndHandlers handlers (treeCalling endpoint)))
                  [ CoverageFinding.ServerGateRefusesCapability "host:sendMail" ]
                  "gate refusal, not absence — and named by the capability the gate refused"
          }

          test "a permissive gate cannot reach an UNREGISTERED host function" {
              let handlers =
                  Map.ofList [ endpoint, handlerOf [ Effect(ServerEffect.HostCall("sendMail", jstr "x", None)) ] ]

              Expect.equal
                  (Demanded.checkProjection
                      (hostOffering [ "audit" ])
                      (ServerDemanded.ofTreeAndHandlers handlers (treeCalling endpoint)))
                  [ CoverageFinding.UnregisteredServerFunction "sendMail" ]
                  "still absent: the vocabulary is closed by registration, not by policy"
          }

          test "the four always-available arms meet the gate and nothing else" {
              let handlers =
                  Map.ofList [ endpoint, handlerOf [ Effect(ServerEffect.ApplyOps []) ] ]

              let projection = ServerDemanded.ofTreeAndHandlers handlers (treeCalling endpoint)

              Expect.isEmpty
                  (Demanded.checkProjection (hostOffering []) projection)
                  "a permissive gate covers it with no performer to register"

              let denying = HostCoverage.nothing |> HostCoverage.withServer ServerCoverage.nothing

              Expect.equal
                  (Demanded.checkProjection denying projection)
                  [ CoverageFinding.ServerGateRefusesCapability "ApplyOps" ]
                  "and a default-deny gate refuses it"
          }

          test "an UNDECLARED server channel surface produces no channel finding; a declared one is checked" {
              let handlers =
                  Map.ofList [ endpoint, handlerOf [ Effect(ServerEffect.Notify("ops", jstr "n")) ] ]

              let projection = ServerDemanded.ofTreeAndHandlers handlers (treeCalling endpoint)

              Expect.isEmpty
                  (Demanded.checkProjection (hostOffering []) projection)
                  "silent until the host declares a surface it can actually enumerate"

              let declared =
                  HostCoverage.nothing
                  |> HostCoverage.withServer (
                      ServerCoverage.nothing
                      |> ServerCoverage.permissive
                      |> ServerCoverage.withChannels [ "audit" ]
                  )

              Expect.equal
                  (Demanded.checkProjection declared projection)
                  [ CoverageFinding.UncoveredServerChannel("Notify", "ops") ]
                  "declared ⇒ checked"
          }

          test "a host that declared NO server tier is never told it failed to serve one" {
              // Unconstrained rather than default-deny, and the distinction is
              // the point: most hosts have no server placement at all, and a
              // client host meeting a two-tier document should not be reported
              // as failing to offer capabilities nobody asked it for.
              let handlers =
                  Map.ofList [ endpoint, handlerOf [ Effect(ServerEffect.HostCall("sendMail", jstr "x", None)) ] ]

              Expect.isEmpty
                  (Demanded.checkProjection
                      HostCoverage.nothing
                      (ServerDemanded.ofTreeAndHandlers handlers (treeCalling endpoint)))
                  "no declaration, no verdict"
          }

          // ── the strict construction path ──────────────────────────

          test "initStrict refuses a session whose handler the host cannot serve; init still builds one" {
              let handler =
                  handlerOf [ Effect(ServerEffect.HostCall("sendMail", jstr "x", None)) ]

              let services =
                  ServerServices.createPermissive |> ServerServices.withHandler endpoint handler

              let wire = WireTree.ofDecoded (treeCalling endpoint)

              match ServerSession.initStrict HostCoverage.nothing services empty wire with
              | Ok _ ->
                  failtest "strict mode must refuse a program whose reachable handler names an unregistered function"
              | Error findings ->
                  Expect.equal
                      findings
                      [ ServerStrictFinding.Coverage(CoverageFinding.UnregisteredServerFunction "sendMail") ]
                      "refused, naming the function"

              // The DEFAULT posture is unchanged: construction succeeds and the
              // refusal happens where the effect is dispatched.
              let session = ServerSession.init services empty wire
              Expect.equal session.BaseTree.Id "root" "the default path still builds the session"
          }

          test "initStrict admits a session the host CAN serve, reading its own registry" {
              let handler =
                  handlerOf [ Effect(ServerEffect.HostCall("sendMail", jstr "x", None)) ]

              let services =
                  { ServerServices.create with
                      Effects = registryOffering [ "sendMail" ] }
                  |> ServerServices.withHandler endpoint handler

              match
                  ServerSession.initStrict
                      HostCoverage.nothing
                      services
                      empty
                      (WireTree.ofDecoded (treeCalling endpoint))
              with
              | Ok session -> Expect.equal session.BaseTree.Id "root" "admitted — and nothing had to be declared twice"
              | Error f -> failtestf "expected admission, got %A" f
          }

          // ── the probe: this check can go RED ──────────────────────

          test "PROBE — widening a HANDLER's demand past the host's coverage turns a passing check red" {
              // The falsifier for every green assertion above. One host, one
              // fixed registration; the only thing that changes is what the
              // handler declares. If the server-tier check were vacuous —
              // returning the empty list whatever it was handed — the first
              // expectation would still pass and only the later ones would catch
              // it.
              let services (stages: HandlerStage list) =
                  { ServerServices.create with
                      Effects = registryOffering [ "sendMail" ] }
                  |> ServerServices.withHandler endpoint (handlerOf stages)

              let within = [ Effect(ServerEffect.HostCall("sendMail", jstr "x", None)) ]

              let widened =
                  within @ [ Effect(ServerEffect.HostCall("exfiltrate", jstr "x", None)) ]

              let projectionOf stages =
                  ServerDemanded.ofTreeAndHandlers (services stages).Handlers (treeCalling endpoint)

              let coverageFor stages =
                  ServerSession.coverageOf HostCoverage.nothing (services stages)

              Expect.isEmpty
                  (Demanded.checkProjection (coverageFor within) (projectionOf within))
                  "the unwidened handler passes"

              Expect.equal
                  (Demanded.checkProjection (coverageFor widened) (projectionOf widened))
                  [ CoverageFinding.UnregisteredServerFunction "exfiltrate" ]
                  "the widened demand is caught, and named"

              // And the same widening flips the strict construction path from
              // admitting to refusing — the check is wired, not merely present.
              let wire = WireTree.ofDecoded (treeCalling endpoint)

              Expect.isTrue
                  (ServerSession.initStrict HostCoverage.nothing (services within) empty wire
                   |> Result.isOk)
                  "unwidened: admitted"

              Expect.isFalse
                  (ServerSession.initStrict HostCoverage.nothing (services widened) empty wire
                   |> Result.isOk)
                  "widened: refused"
          } ]
