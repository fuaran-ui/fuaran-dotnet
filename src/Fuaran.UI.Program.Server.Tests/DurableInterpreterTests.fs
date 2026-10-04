module Fuaran.UI.Program.Server.Tests.DurableInterpreterTests

// ─── The second interpreter, and the facet it certifies ──────────────────────
//
// Four claims, and each of them is the kind that is easy to assert and hard to
// earn, so each is checked in the form that could go red.
//
//  1. ONE ALGEBRA, TWO INTERPRETERS. The same handler registration and the same
//     corpus scenarios produce the same results under both — asserted over the
//     whole driver-semantics corpus step by step, not over a hand-picked case,
//     and at the handler level over every arm of the closed vocabulary.
//
//  2. A CRASH MID-HANDLER COSTS NO DUPLICATE EFFECT. The fixtures kill the
//     interpreter inside a performer, replay from the journal, and count the
//     performer's own invocations across BOTH runs. A count is the only form of
//     this claim that cannot be satisfied by an implementation that merely looks
//     careful.
//
//  3. THE BOUNDARY IS DECLARED, NOT PAPERED OVER. A crash can land between the
//     effect and the record of it, and no engineering inside this repository
//     closes that window — so the fixtures drive the indeterminate step and pin
//     what each policy does with it, INCLUDING the one that duplicates.
//
//  4. THE CONJUNCTION NEVER INFLATES. The composition's facet is at least as
//     weak as every arm's, exhaustively over the lattice — and the negative test
//     at the foot of this file is an inflating declaration that must be
//     REFUSED. A check that has never been seen to refuse anything is a check
//     nobody has verified.
//
// ── The one rule this suite must never quietly relax ─────────────────────────
// The indeterminate step is REFUSED by default. Rounding it up to "it probably
// ran, serve the record" or down to "it probably did not, run it again" both
// read as tidier code and both publish a guarantee the substrate does not
// provide. A future edit that removes the refusal fails here, on purpose.

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation
open Fuaran.UI.Renderer.BindingResolver
open Fuaran.Program.Bounded
open Fuaran.UI.Program
open Fuaran.Program.Server
open Fuaran.UI.Program.Server
open Fuaran.UI.Program.Parity
open Fuaran.UI.Program.Parity.Runner

// ─── fixtures ────────────────────────────────────────────────────────────────

let private jstr (s: string) = Fuaran.Core.JStr s

let private handlerEndpoint = "/handlers/refresh"

[<Literal>]
let private handlerFixture = "server-handler-call"

/// A counting performer: the number of times the host actually ran it, which is
/// the whole subject of the crash-replay family.
type private Counter() =
    let mutable count = 0
    member _.Count = count
    member _.Bump() = count <- count + 1

/// A performer that succeeds, counting.
let private counting (counter: Counter) (answer: string) =
    fun (_: Fuaran.Core.JVal) ->
        counter.Bump()
        Ok(jstr answer)

/// A performer that COMMITS and then the process dies — the crash inside the
/// indeterminate window, where the effect happened and the record of it did not.
exception private ProcessDied of string

let private committingThenDying (counter: Counter) =
    fun (_: Fuaran.Core.JVal) ->
        counter.Bump()
        raise (ProcessDied "after the effect")

/// A performer that dies BEFORE it commits anything — the other side of the same
/// window, indistinguishable from the above in the journal, and deliberately so.
let private dyingBeforeCommitting (_: Counter) =
    fun (_: Fuaran.Core.JVal) -> raise (ProcessDied "before the effect")

/// Run something that is expected to die, and report whether it did. A crash the
/// fixture did not observe would leave every assertion after it meaningless.
let private crashing (f: unit -> 'T) : bool =
    try
        f () |> ignore
        false
    with ProcessDied _ ->
        true

let private rows: Fuaran.Core.Table =
    { Schema = [ "n", Fuaran.Core.IntType ]
      Columns =
        [ { Name = "n"
            Type = Fuaran.Core.IntType
            Cells = [ Fuaran.Core.Int 1; Fuaran.Core.Int 2; Fuaran.Core.Int 3 ] } ] }

/// One arm of every server capability, in the order a real handler uses them.
/// The same shape the tier-parity family's handler takes, so a difference
/// between the two interpreters would show against a registration the corpus
/// already exercises.
let private refreshHandler: Handler =
    { Name = "refresh"
      Stages =
        [ Effect(
              ServerEffect.RunQuery(
                  "rows",
                  Fuaran.Core.Embedded rows,
                  [ Fuaran.Compute.Limit(Fuaran.Compute.Slot.Lit 2, Fuaran.Compute.Slot.Lit 0) ]
              )
          )
          Compute(Action.SetState("rows", Some(jstr "2 rows"), None))
          Effect(ServerEffect.ApplyOps [ TreeOp.RemoveNode(NodeId "refresh") ])
          Effect(ServerEffect.HostCall("audit", jstr "refreshed", None))
          Effect(ServerEffect.EmitPatch [ TreeOp.RemoveNode(NodeId "readout") ])
          Effect(ServerEffect.Notify("audit", jstr "refreshed")) ] }

/// A handler with two host calls, so a crash can land at the SECOND and leave
/// the first recorded — the shape the certification actually needs.
let private twoCalls (a: string) (b: string) : Handler =
    { Name = "two"
      Stages =
        [ Effect(ServerEffect.HostCall(a, jstr "one", Some "first"))
          Effect(ServerEffect.HostCall(b, jstr "two", Some "second")) ] }

/// The domain tree the handler fixtures run against. It carries the two nodes
/// `refreshHandler` addresses, so an `ApplyOps` arm exercises the apply engine
/// rather than halting on a node that is not there — which would make every
/// assertion after it a test of the rollback path instead.
let private baseTree: Node<obj> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<obj> with
            Children =
                [ Fuaran.button
                      "refresh"
                      { Defaults.button<obj> with
                          Label = TextSource.Literal "refresh" }
                  Fuaran.markdown "readout" "idle" ] }

let private emptyStore: ServerStore = { Tree = baseTree; Bindings = empty }

let private registryOf (performers: (string * (Fuaran.Core.JVal -> Result<Fuaran.Core.JVal, string>)) list) =
    performers
    |> List.fold (fun r (fn, p) -> ServerEffectRegistry.register fn p r) ServerEffectRegistry.denyAll
    |> ServerEffectRegistry.permissive

/// The comparable projection of a handler outcome.
///
/// Not the record itself: a resolved tree's nodes carry handler slots, so a
/// structural comparison of `Node<obj>` is not defined. The canonical encoding
/// IS defined and is what every other parity leg in this repository compares, so
/// it is what this one compares too.
let private projectionOf (outcome: HandlerOutcome) =
    {| Tree = CanonicalJson.encodeNode outcome.Store.Tree
       State = outcome.Store.Bindings.State |> Map.map (fun _ v -> sprintf "%A" v)
       Queries = outcome.Store.Bindings.QueryResults |> Map.toList |> List.map fst
       Committed = outcome.Committed
       Performed = outcome.Performed
       Patches = outcome.Patches |> List.map (sprintf "%A")
       Notifications = outcome.Notifications
       Effects = outcome.ClientEffects |> List.map ClientEffect.encode
       Diagnostics = outcome.Diagnostics |> List.map (sprintf "%A") |}

// ─── the corpus, under both interpreters ─────────────────────────────────────

let private toLiveEvent (index: int) (ev: ScriptedEvent) : LiveEvent =
    { ConnId = "durable"
      NodeId = ev.NodeId
      Event = ev.Event
      Payload = ev.Payload |> Map.map (fun _ v -> LiveValue.Str v)
      LastSeq = index }

/// Drive a fixture through the server placement with a NAMED arm, producing the
/// tier-parity family's per-step observation. The arm is the only parameter, so
/// a divergence between two runs of this function is a divergence between two
/// interpreters and can be nothing else.
let private driveWith
    (armFor: ServerSession -> string -> HandlerArm<HandlerTally>)
    (services: ServerServices)
    (fixture: Fixture)
    : Result<StepObservation list, string> =
    match JsonDecode.decodeNode fixture.TreeJson, registryFor fixture.HostPolicy with
    | Error err, _ -> Error(sprintf "decode failed: %A" err)
    | _, Error e -> Error(sprintf "%s: %s" fixture.Name e)
    | Ok wire, Ok registry ->
        let session = ServerSession.init services empty wire

        let observations =
            fixture.Events
            |> List.mapi toLiveEvent
            |> List.mapi (fun i ev -> i, ev)
            |> List.scan
                (fun (session, _) (i, ev) ->
                    let next, out =
                        ServerSession.stepWith (armFor session (sprintf "%s#%d" fixture.Name i)) session ev

                    next,
                    Some
                        { ResolvedJson = CanonicalJson.encodeNode out.Resolved
                          Effects = out.ClientEffects |> List.map ClientEffect.encode
                          Refused = out.Rejected.IsSome
                          Denials = observeDenials fixture.HostPolicy registry out.ClientEffects })
                (session, None)
            |> List.choose snd

        Ok(
            { ResolvedJson = CanonicalJson.encodeNode session.Resolved
              Effects = []
              Refused = false
              Denials = observeDenials fixture.HostPolicy registry [] }
            :: observations
        )

let private directly =
    fun (session: ServerSession) _ -> ServerSession.directArm session.Services

let private durably (services: DurableServices) =
    fun (session: ServerSession) (invocation: string) -> Durable.arm services invocation session.Services

let private openServices =
    { ServerServices.createPermissive with
        Effects = registryOf [ "audit", (fun _ -> Ok(jstr "recorded")) ] }

// ─── the facet lattice, for the law tests ────────────────────────────────────

let private allHazards =
    [ for lose in [ false; true ] do
          for duplicate in [ false; true ] ->
              { MayLose = lose
                MayDuplicate = duplicate } ]

let private allDerived =
    [ for delivery in allHazards do
          for idempotency in IdempotencyFacet.all do
              for restart in RestartVisibility.all ->
                  { Delivery = delivery
                    Idempotency = idempotency
                    Restart = restart } ]

let private allEffects: ServerEffect<TreeOp<obj>> list =
    [ ServerEffect.RunQuery("slot", Fuaran.Core.Embedded rows, [])
      ServerEffect.ApplyOps [ TreeOp.RemoveNode(NodeId "x") ]
      ServerEffect.HostCall("audit", jstr "a", None)
      ServerEffect.EmitPatch [ TreeOp.RemoveNode(NodeId "x") ]
      ServerEffect.Notify("ops", jstr "n") ]

let private allDisciplines =
    PlacementDiscipline.Direct
    :: [ for survives in [ false; true ] do
             for reinvoke in [ false; true ] ->
                 PlacementDiscipline.DeterministicReplay
                     { JournalSurvivesRestart = survives
                       ReinvokeIndeterminate = reinvoke } ]

let private durableDiscipline (reinvoke: bool) =
    PlacementDiscipline.DeterministicReplay
        { JournalSurvivesRestart = true
          ReinvokeIndeterminate = reinvoke }

let private logicTree: LogicTreeRef = { Ref = "orders/refresh"; Hash = None }

/// The UI tier's op performance, typed for this suite's witness: ops are
/// performed by being applied, and nothing new is journaled.
let private inMemory: OpPerformance<Node<obj>, TreeOp<obj>> = OpPerformance.InMemory

// ─── a performed op, journaled like a host call (Phase 1980) ─────────────────

let private removeRefresh: TreeOp<obj> = TreeOp.RemoveNode(NodeId "refresh")
let private removeReadout: TreeOp<obj> = TreeOp.RemoveNode(NodeId "readout")

/// An op, as this suite compares it: its canonical form off the state axis.
let private enc (op: TreeOp<obj>) =
    UiWitness.witness.State.Stream.Encode op

/// The journal subject of an op — what production records for its stage.
let private subjectOf (op: TreeOp<obj>) =
    Durable.opSubject UiWitness.witness.State op

/// Two ops in one stage and a host call after them, so under a registered op
/// performer the staged list is op, op, host call — three ordinals in ONE
/// sequence, which is the shape the certification needs.
let private editsThenAudit: Handler =
    { Name = "edits"
      Stages =
        [ Effect(ServerEffect.ApplyOps [ removeRefresh; removeReadout ])
          Effect(ServerEffect.HostCall("audit", jstr "edited", None)) ] }

/// The same ops in the other order: the same capabilities at every ordinal
/// and different subjects, which only the subject can tell apart.
let private editsSwapped: Handler =
    { Name = "edits-swapped"
      Stages =
        [ Effect(ServerEffect.ApplyOps [ removeReadout; removeRefresh ])
          Effect(ServerEffect.HostCall("audit", jstr "edited", None)) ] }

/// What an op performer was handed, in order — the op-side twin of `Counter`.
type private OpLog() =
    let performed = ResizeArray<string>()
    member _.Performed = List.ofSeq performed
    member _.Record(op: TreeOp<obj>) = performed.Add(enc op)

/// An op performer that records and succeeds.
let private performing (log: OpLog) : OpPerformance<Node<obj>, TreeOp<obj>> =
    OpPerformance.performedWithoutReceipt (fun _ op ->
        log.Record op
        Ok())

/// An op performer that performs the op named and then the process dies — the
/// crash inside the indeterminate window, where the effect happened and the
/// record of it did not.
let private performingThenDyingAt (victim: string) (log: OpLog) : OpPerformance<Node<obj>, TreeOp<obj>> =
    OpPerformance.performedWithoutReceipt (fun _ op ->
        log.Record op

        match op with
        | TreeOp.RemoveNode(NodeId id) when id = victim -> raise (ProcessDied "after the op")
        | _ -> Ok())

/// A journal the process dies in front of: the ATTEMPT record for `step` kills
/// the process before it lands. The interruption BETWEEN two steps — the one
/// before completed and recorded, the next never attempted — which is the
/// clean resume case, and the one the acceptance names.
let private dyingBeforeAttempting (step: int) (inner: EffectJournal) : EffectJournal =
    { inner with
        Append =
            fun entry ->
                match entry.Phase with
                | JournalPhase.Attempted when entry.Step = step -> raise (ProcessDied "between steps")
                | _ -> inner.Append entry }

let private auditRegistry (audit: Counter) =
    registryOf [ "audit", counting audit "recorded" ]

let private runEdits
    (services: DurableServices)
    (registry: ServerEffectRegistry)
    (performance: OpPerformance<Node<obj>, TreeOp<obj>>)
    (handler: Handler)
    =
    Durable.runWith
        UiWitness.witness
        services
        "inv"
        registry
        performance
        Fuaran.Compute.DataFrame.noResolve
        "node"
        handler
        emptyStore

// ─── the proved durable replay as oracle (Phase 1980) ────────────────────────
//
// `proofs/Staging.fst` models `Durable.runWith`'s perform phase — `decide` and
// `replay` over a journal SNAPSHOT — and the extraction is what runs here,
// beside production, over every journal shape the cases above leave behind.
// The staged list is built by hand in the shape the plan phase stages for
// `editsThenAudit`: two op stages carrying their subjects, one host call.

/// The model's performer token for this host: production's closure, with the
/// subject and the declaration the model's arrows read off it.
type private Token =
    { Subject: string option
      Idempotent: bool
      Invoke: Fuaran.Core.JVal -> Result<Fuaran.Core.JVal, string> }

let private modelOpt (value: 'T option) : Staging.opt<'T> =
    match value with
    | Some v -> Staging.OSome v
    | None -> Staging.ONone

let private modelRes (value: Result<'T, string>) : Staging.res<'T> =
    match value with
    | Ok v -> Staging.ROk v
    | Error e -> Staging.RErr e

/// Only `w_assign` is reached — no staged call here lands a slot, so the
/// identity is never even asked — and the rest refuse loudly: the replay
/// differential is about the perform phase and stages no plan.
let private modelWitness: Staging.witness<Node<obj>, BindingSources, Fuaran.Core.JVal, TreeOp<obj>, obj, obj, obj, obj> =
    { w_compute = fun _ _ _ -> failwith "the replay differential plans nothing"
      w_undo_compute = fun _ _ _ -> failwith "the replay differential plans nothing"
      w_query = fun _ _ _ -> failwith "the replay differential plans nothing"
      w_apply = fun _ _ -> failwith "the replay differential plans nothing"
      w_op_view = fun _ -> failwith "the replay differential plans nothing"
      w_assign = fun _ _ bindings -> bindings
      w_slot_refused = fun _ -> failwith "the replay differential plans nothing" }

let private modelRegistry: Staging.registry<Node<obj>, Fuaran.Core.JVal, TreeOp<obj>, obj, Token> =
    { r_gate = fun _ -> true
      r_policy = fun _ -> Staging.ONone
      r_lookup = fun _ -> Staging.ONone
      r_perf = fun token args -> token.Invoke args |> modelRes
      r_op_perform = Staging.ONone }

/// The journal snapshot as the model reads it, from the production journal's
/// own entries: `stepOf` for the three-state reading, `capabilityOf` and
/// `subjectOf` for the recorded identity.
let private modelJournal (entries: JournalEntry list) : Staging.journal<Fuaran.Core.JVal> =
    { j_step =
        fun k ->
            match Journal.stepOf entries (int k) with
            | JournaledStep.Unrun -> Staging.JUnrun
            | JournaledStep.Value v -> Staging.JValue v
            | JournaledStep.Refusal r -> Staging.JRefusal r
            | JournaledStep.Indeterminate _ -> Staging.JIndeterminate
      j_recorded =
        fun k ->
            match Journal.capabilityOf entries (int k), Journal.subjectOf entries (int k) with
            | Some capability, Some subject -> Staging.OSome(capability, modelOpt subject)
            | _ -> Staging.ONone }

/// The staged list the plan phase stages for `editsThenAudit` under a
/// registered op performer, with the resume's performers as tokens.
let private modelStaged
    (opDeclared: bool)
    (auditDeclared: bool)
    (audit: Fuaran.Core.JVal -> Result<Fuaran.Core.JVal, string>)
    : Staging.staged_call<Fuaran.Core.JVal, Token> list =
    let opStage (op: TreeOp<obj>) : Staging.staged_call<Fuaran.Core.JVal, Token> =
        { Staging.sc_capability = "ApplyOps"
          Staging.sc_performer =
            { Subject = Some(subjectOf op)
              Idempotent = opDeclared
              Invoke = fun _ -> Ok(Fuaran.Core.JObj []) }
          Staging.sc_args = Fuaran.Core.JObj []
          Staging.sc_into = Staging.ONone }

    let hostCall: Staging.staged_call<Fuaran.Core.JVal, Token> =
        { Staging.sc_capability = "host:audit"
          Staging.sc_performer =
            { Subject = None
              Idempotent = auditDeclared
              Invoke = audit }
          Staging.sc_args = jstr "edited"
          Staging.sc_into = Staging.ONone }

    [ opStage removeRefresh; opStage removeReadout; hostCall ]

let private modelDiagnostic (diagnostic: Staging.diagnostic<obj>) : ServerDiagnostic =
    match diagnostic with
    | Staging.PerformFailed(capability, reason) -> ServerDiagnostic.PerformFailed(capability, reason)
    | Staging.Failed(capability, reason) -> ServerDiagnostic.Failed(capability, reason)
    | Staging.Denied(Staging.Unregistered capability) ->
        ServerDiagnostic.Denied(ServerEffectDenial.Unregistered capability)
    | Staging.Denied(Staging.GateRefused capability) ->
        ServerDiagnostic.Denied(ServerEffectDenial.GateRefused capability)
    | Staging.Bounded _ -> failwith "the replay differential plans nothing"

/// One differential: production's resume over `journal` under `services`,
/// against the model's `replay` over the same snapshot, the same staged
/// shape and the same performer verdicts. Six things are compared: the four
/// ordinal lists, the verdict, the audit trail — and the diagnostics.
let private replayDifferential
    (services: DurableServices)
    (journal: EffectJournal)
    (auditAnswer: Fuaran.Core.JVal -> Result<Fuaran.Core.JVal, string>)
    =
    let snapshot = journal.Read "inv"
    let resumeLog = OpLog()

    let production =
        runEdits
            (services |> DurableServices.withJournal journal)
            (registryOf [ "audit", auditAnswer ])
            (performing resumeLog)
            editsThenAudit

    let dur: Staging.durable<Fuaran.Core.JVal, Token> =
        { d_journal = modelJournal snapshot
          d_subject = fun call -> modelOpt call.sc_performer.Subject
          d_idempotent = fun call -> call.sc_performer.Idempotent
          d_reinvoke = services.ReinvokeIndeterminate }

    let staged =
        modelStaged
            (PerformerFacets.opPerformerFacet services.Performers = IdempotencyFacet.Idempotent)
            (PerformerFacets.facetOf "audit" services.Performers = IdempotencyFacet.Idempotent)
            auditAnswer

    let model =
        Staging.replay
            modelWitness
            modelRegistry
            dur
            0I
            staged
            (Staging.start
                { st_tree = baseTree
                  st_bindings = empty })

    let ordinals (xs: bigint list) = xs |> List.map int

    Expect.equal production.Replayed (ordinals model.rp_replayed) "replayed: the ordinals served from the journal"
    Expect.equal production.Invoked (ordinals model.rp_invoked) "invoked: the ordinals whose performer ran"

    Expect.equal
        production.Indeterminate
        (ordinals model.rp_indeterminate)
        "indeterminate: the ordinals refused undecided"

    Expect.equal
        (production.Overrides |> List.map _.Step)
        (ordinals model.rp_overrides)
        "overrides: the ordinals re-invoked under the opt-in"

    Expect.equal production.Outcome.Committed (not model.rp_acc.ac_halted) "the verdict"

    Expect.equal
        production.Outcome.Performed
        (Staging.rev model.rp_acc.ac_externally)
        "the audit trail — served stages included, because the outcome is recomputed"

    Expect.equal
        production.Outcome.Diagnostics
        (Staging.rev model.rp_acc.ac_diagnostics |> List.map modelDiagnostic)
        "the diagnostics, verbatim"

    production, resumeLog

[<Tests>]
let tests =
    let fixtures = FixtureIo.load FixtureIo.fixturesRoot

    testList
        "durable execution — the second interpreter"
        [ testList
              "one algebra, two interpreters"
              [ test "the corpus is present" {
                    Expect.isNonEmpty fixtures "the corpus enumerates no driver-semantics scenario"
                }

                testList
                    "every corpus scenario agrees step by step under both interpreters"
                    [ for fixture in fixtures ->
                          test fixture.Name {
                              let services =
                                  openServices |> ServerServices.withHandler handlerEndpoint refreshHandler

                              let durable =
                                  DurableServices.create
                                  |> DurableServices.withJournal (Journal.declaringDurable (Journal.inMemory ()))
                                  |> DurableServices.declaringPerformer "audit" IdempotencyFacet.Idempotent

                              match
                                  driveWith directly services fixture, driveWith (durably durable) services fixture
                              with
                              | Error e, _
                              | _, Error e -> failtestf "%s: %s" fixture.Name e
                              | Ok direct, Ok replayed ->
                                  match compare fixture.Name "Handler.run" direct "Durable.run" replayed with
                                  | None -> ()
                                  | Some divergence -> failtestf "%s" (Divergence.describe divergence)
                          } ]

                test "at the handler level, every arm of the vocabulary agrees" {
                    let counter = Counter()
                    let registry = registryOf [ "audit", counting counter "recorded" ]

                    let direct =
                        Handler.run registry Fuaran.Compute.DataFrame.noResolve "node" refreshHandler emptyStore

                    let durable =
                        Durable.run
                            (DurableServices.create |> DurableServices.withJournal (Journal.inMemory ()))
                            "inv"
                            registry
                            Fuaran.Compute.DataFrame.noResolve
                            "node"
                            refreshHandler
                            emptyStore

                    Expect.isTrue direct.Committed "the direct interpreter committed"
                    Expect.isTrue durable.Outcome.Committed "and so did the durable one"

                    Expect.equal
                        (projectionOf durable.Outcome)
                        (projectionOf direct)
                        "the same handler, the same store, the same result — the outcome is the direct \
                         interpreter's own type and compares as one"

                    Expect.equal counter.Count 2 "each interpreter reached the performer exactly once"
                }

                test "a halted handler agrees too — including what it rolled back" {
                    // The interesting half of parity: agreement on the SUCCESS
                    // path is what any two implementations achieve first, and
                    // agreement on a rollback is what tells you the second one
                    // did not quietly reimplement the fold.
                    let registry =
                        registryOf [ "ok", (fun _ -> Ok(jstr "y")); "no", (fun _ -> Error "refused by host") ]

                    let handler = twoCalls "ok" "no"

                    let direct =
                        Handler.run registry Fuaran.Compute.DataFrame.noResolve "node" handler emptyStore

                    let durable =
                        Durable.run
                            (DurableServices.create |> DurableServices.withJournal (Journal.inMemory ()))
                            "inv"
                            registry
                            Fuaran.Compute.DataFrame.noResolve
                            "node"
                            handler
                            emptyStore

                    Expect.isFalse direct.Committed "the direct interpreter rolled back"

                    Expect.equal
                        (projectionOf durable.Outcome)
                        (projectionOf direct)
                        "and the durable one rolled back identically — same Performed prefix, same \
                         PerformFailed diagnostic"
                } ]

          testList
              "crash mid-handler, replayed from the journal"
              [ test "replaying a completed invocation reaches no performer at all" {
                    // The cleanest reading of exactly-once-effective: the process
                    // died after the handler finished and before the caller
                    // recorded the outcome, so the whole invocation runs again.
                    // Every step is served, nothing outside is touched, and the
                    // outcome is identical because it was recomputed rather than
                    // stored.
                    let counter = Counter()
                    let registry = registryOf [ "audit", counting counter "recorded" ]
                    let journal = Journal.declaringDurable (Journal.inMemory ())
                    let services = DurableServices.create |> DurableServices.withJournal journal

                    let first =
                        Durable.run
                            services
                            "inv"
                            registry
                            Fuaran.Compute.DataFrame.noResolve
                            "node"
                            refreshHandler
                            emptyStore

                    let replay =
                        Durable.run
                            services
                            "inv"
                            registry
                            Fuaran.Compute.DataFrame.noResolve
                            "node"
                            refreshHandler
                            emptyStore

                    Expect.equal first.Invoked [ 0 ] "the first run performed the one host call"
                    Expect.equal replay.Invoked [] "the replay performed none"
                    Expect.equal replay.Replayed [ 0 ] "it served that step from the journal"
                    Expect.equal counter.Count 1 "so the performer ran ONCE across both runs — no duplicate effect"

                    Expect.equal
                        (projectionOf replay.Outcome)
                        (projectionOf first.Outcome)
                        "and the replayed outcome is the recorded one, recomputed"

                    Expect.isTrue
                        (Journal.isComplete (journal.Read "inv"))
                        "the invocation carries its completion marker"
                }

                test "a crash at the second call leaves the first served and never re-run" {
                    let first = Counter()
                    let boom = Counter()
                    let journal = Journal.declaringDurable (Journal.inMemory ())
                    let services = DurableServices.create |> DurableServices.withJournal journal
                    let handler = twoCalls "first" "boom"

                    let crashed =
                        crashing (fun () ->
                            Durable.run
                                services
                                "inv"
                                (registryOf [ "first", counting first "a"; "boom", committingThenDying boom ])
                                Fuaran.Compute.DataFrame.noResolve
                                "node"
                                handler
                                emptyStore)

                    Expect.isTrue crashed "the interpreter was killed inside the second performer"
                    Expect.equal first.Count 1 "the first call had already run"
                    Expect.equal boom.Count 1 "and the second had committed before the process died"

                    Expect.equal
                        (Journal.describe (journal.Read "inv"))
                        [ "0 host:first attempted"; "0 host:first completed"; "1 host:boom attempted" ]
                        "the journal records exactly that: one step decided, one step attempted and no more"

                    // The replay. The second performer would now succeed, which
                    // is the point — a resume must not be tempted by it.
                    let replay =
                        Durable.run
                            services
                            "inv"
                            (registryOf [ "first", counting first "a"; "boom", counting boom "b" ])
                            Fuaran.Compute.DataFrame.noResolve
                            "node"
                            handler
                            emptyStore

                    Expect.equal replay.Replayed [ 0 ] "the decided step was served from the journal"
                    Expect.equal first.Count 1 "so the first call was NOT performed a second time"
                    Expect.equal replay.Indeterminate [ 1 ] "and the undecided step was refused"
                    Expect.equal boom.Count 1 "so nothing ran twice — zero duplicate effects across the crash"

                    Expect.isFalse replay.Outcome.Committed "the handler rolled back around the refusal"

                    Expect.equal
                        replay.Outcome.Diagnostics
                        [ ServerDiagnostic.PerformFailed("host:boom", DurableCode.IndeterminateStep) ]
                        "…naming the step it could not decide, in the closed vocabulary and with no payload"
                }

                test "the indeterminate step is REFUSED by default — the rule that must not relax" {
                    let boom = Counter()
                    let journal = Journal.declaringDurable (Journal.inMemory ())
                    let services = DurableServices.create |> DurableServices.withJournal journal
                    let handler = twoCalls "boom" "boom"

                    crashing (fun () ->
                        Durable.run
                            services
                            "inv"
                            (registryOf [ "boom", committingThenDying boom ])
                            Fuaran.Compute.DataFrame.noResolve
                            "node"
                            handler
                            emptyStore)
                    |> fun died -> Expect.isTrue died "killed inside the first performer"

                    Expect.equal boom.Count 1 "the effect happened once"

                    let replay =
                        Durable.run
                            services
                            "inv"
                            (registryOf [ "boom", counting boom "b" ])
                            Fuaran.Compute.DataFrame.noResolve
                            "node"
                            handler
                            emptyStore

                    Expect.equal boom.Count 1 "the strict policy refused rather than repeating it"
                    Expect.equal replay.Overrides [] "and recorded no override, because none was used"
                }

                test "the accepting policy re-invokes, records the override, and DUPLICATES" {
                    // The honest boundary, driven rather than described. This is
                    // the fixture a reader should look at before believing any
                    // exactly-once claim on this page: the placement CAN be
                    // configured to duplicate, and when it is, the facet
                    // derivation below says at-least-once.
                    let boom = Counter()
                    let journal = Journal.declaringDurable (Journal.inMemory ())

                    let services =
                        DurableServices.create
                        |> DurableServices.withJournal journal
                        |> DurableServices.acceptingIndeterminateReplay

                    let handler = twoCalls "boom" "boom"

                    crashing (fun () ->
                        Durable.run
                            services
                            "inv"
                            (registryOf [ "boom", committingThenDying boom ])
                            Fuaran.Compute.DataFrame.noResolve
                            "node"
                            handler
                            emptyStore)
                    |> ignore

                    let replay =
                        Durable.run
                            services
                            "inv"
                            (registryOf [ "boom", counting boom "b" ])
                            Fuaran.Compute.DataFrame.noResolve
                            "node"
                            handler
                            emptyStore

                    Expect.equal boom.Count 3 "the effect ran again — once before the crash, twice on the replay"

                    Expect.equal
                        (replay.Overrides |> List.map (fun o -> o.Step, o.Capability))
                        [ 0, "host:boom" ]
                        "and the override was RECORDED, so a caller receives the fact rather than being \
                         trusted to remember it"
                }

                test "a performer the host declares idempotent closes the window with no override" {
                    let boom = Counter()
                    let journal = Journal.declaringDurable (Journal.inMemory ())

                    let services =
                        DurableServices.create
                        |> DurableServices.withJournal journal
                        |> DurableServices.declaringPerformer "boom" IdempotencyFacet.Idempotent

                    let handler = twoCalls "boom" "boom"

                    crashing (fun () ->
                        Durable.run
                            services
                            "inv"
                            (registryOf [ "boom", dyingBeforeCommitting boom ])
                            Fuaran.Compute.DataFrame.noResolve
                            "node"
                            handler
                            emptyStore)
                    |> ignore

                    Expect.equal boom.Count 0 "the process died before the effect — the other side of the same window"

                    let replay =
                        Durable.run
                            services
                            "inv"
                            (registryOf [ "boom", counting boom "b" ])
                            Fuaran.Compute.DataFrame.noResolve
                            "node"
                            handler
                            emptyStore

                    Expect.isTrue replay.Outcome.Committed "the resume completed"
                    Expect.equal replay.Overrides [] "no override was needed: the performer's own shape closes it"
                    Expect.equal boom.Count 2 "both calls ran, each exactly once"
                }

                test "a replay that diverges is refused rather than served the wrong answer" {
                    // The premise of ordinal addressing is that the plan phase
                    // recomputes the same call list. A `RunQuery` reads data that
                    // may have moved, so the premise can fail — and when it does,
                    // serving one call's recorded answer to another is the worst
                    // available outcome. The journal records the capability so
                    // the divergence is detectable at all.
                    let journal = Journal.declaringDurable (Journal.inMemory ())
                    let services = DurableServices.create |> DurableServices.withJournal journal
                    let counter = Counter()

                    Durable.run
                        services
                        "inv"
                        (registryOf [ "alpha", counting counter "a"; "beta", counting counter "b" ])
                        Fuaran.Compute.DataFrame.noResolve
                        "node"
                        (twoCalls "alpha" "beta")
                        emptyStore
                    |> ignore

                    // The same invocation id, a differently-shaped recomputation.
                    let replay =
                        Durable.run
                            services
                            "inv"
                            (registryOf [ "alpha", counting counter "a"; "beta", counting counter "b" ])
                            Fuaran.Compute.DataFrame.noResolve
                            "node"
                            (twoCalls "beta" "alpha")
                            emptyStore

                    Expect.isFalse replay.Outcome.Committed "the divergent replay did not commit"

                    Expect.equal
                        replay.Outcome.Diagnostics
                        [ ServerDiagnostic.PerformFailed("host:beta", DurableCode.ReplayDivergence) ]
                        "it named the ordinal's capability mismatch and stopped"

                    Expect.equal counter.Count 2 "and reached no performer on the replay"
                }

                test "with no journal the interpreter degrades honestly to the direct one" {
                    let counter = Counter()
                    let registry = registryOf [ "audit", counting counter "recorded" ]

                    let first =
                        Durable.run
                            DurableServices.create
                            "inv"
                            registry
                            Fuaran.Compute.DataFrame.noResolve
                            "node"
                            refreshHandler
                            emptyStore

                    let again =
                        Durable.run
                            DurableServices.create
                            "inv"
                            registry
                            Fuaran.Compute.DataFrame.noResolve
                            "node"
                            refreshHandler
                            emptyStore

                    Expect.equal first.Invoked [ 0 ] "the first run performed the call"
                    Expect.equal again.Replayed [] "the second served nothing — there was nothing to serve"
                    Expect.equal counter.Count 2 "so it ran twice, exactly as the direct interpreter would"
                } ]

          testList
              "a performed op is journaled like a host call (Phase 1980)"
              [ test "an op stage is journaled under ApplyOps at its ordinal, in the sequence the host calls share" {
                    let log = OpLog()
                    let audit = Counter()
                    let journal = Journal.declaringDurable (Journal.inMemory ())
                    let services = DurableServices.create |> DurableServices.withJournal journal

                    let first = runEdits services (auditRegistry audit) (performing log) editsThenAudit

                    Expect.isTrue first.Outcome.Committed "the plan committed and every staged call ran"

                    Expect.equal
                        first.Invoked
                        [ 0; 1; 2 ]
                        "two op stages and one host call: three ordinals in ONE sequence"

                    Expect.equal
                        log.Performed
                        [ enc removeRefresh; enc removeReadout ]
                        "the performer was handed each op once, in plan order"

                    Expect.equal
                        first.Outcome.Performed
                        [ "ApplyOps"; "ApplyOps"; "host:audit" ]
                        "and the audit trail names each op stage as performed, in execution order"

                    let s0 = subjectOf removeRefresh
                    let s1 = subjectOf removeReadout

                    Expect.equal
                        (Journal.describe (journal.Read "inv"))
                        [ sprintf "0 ApplyOps %s attempted" s0
                          sprintf "0 ApplyOps %s completed" s0
                          sprintf "1 ApplyOps %s attempted" s1
                          sprintf "1 ApplyOps %s completed" s1
                          "2 host:audit attempted"
                          "2 host:audit completed"
                          "-1 Invocation completed" ]
                        "each op stage is journaled under ApplyOps with the op's content address as its \
                         subject — attempted before the performer, decided after — exactly as a host call is"

                    Expect.equal
                        s0
                        ("sha256:" + Fuaran.Core.Hash.sha256Hex (enc removeRefresh))
                        "the subject is the content address of the op's canonical form, read off the state axis"

                    Expect.notEqual s0 s1 "two ops, two subjects"

                    Expect.equal
                        s0.Length
                        ("sha256:".Length + 64)
                        "and a subject is fixed-size: a hash, never the op's payload"
                }

                test
                    "interrupted after the first op and before the second, the resume performs only the ops after the recorded prefix" {
                    // The acceptance's clause, driven: the process died between
                    // the first op stage's record and the second's attempt.
                    let journal = Journal.declaringDurable (Journal.inMemory ())
                    let firstLog = OpLog()
                    let audit = Counter()
                    let services = DurableServices.create |> DurableServices.withJournal journal

                    let crashed =
                        crashing (fun () ->
                            runEdits
                                (services |> DurableServices.withJournal (dyingBeforeAttempting 1 journal))
                                (auditRegistry audit)
                                (performing firstLog)
                                editsThenAudit)

                    Expect.isTrue crashed "the process died between the first op's record and the second's attempt"
                    Expect.equal firstLog.Performed [ enc removeRefresh ] "the first op had been performed"

                    let s0 = subjectOf removeRefresh

                    Expect.equal
                        (Journal.describe (journal.Read "inv"))
                        [ sprintf "0 ApplyOps %s attempted" s0; sprintf "0 ApplyOps %s completed" s0 ]
                        "and the journal holds exactly that: one op stage decided, nothing after it"

                    let resumeLog = OpLog()

                    let resumed =
                        runEdits services (auditRegistry audit) (performing resumeLog) editsThenAudit

                    Expect.equal resumed.Replayed [ 0 ] "the recorded op stage was served from the journal"

                    Expect.equal
                        resumeLog.Performed
                        [ enc removeReadout ]
                        "so the performer was handed ONLY the op after the recorded prefix"

                    Expect.equal resumed.Invoked [ 1; 2 ] "— the second op stage and the host call"
                    Expect.equal audit.Count 1 "each exactly once across both runs"
                    Expect.isTrue resumed.Outcome.Committed "and the resumed run committed"

                    Expect.equal
                        resumed.Outcome.Performed
                        [ "ApplyOps"; "ApplyOps"; "host:audit" ]
                        "with the audit trail of the whole plan, served stage included: recomputed, not stored"

                    Expect.equal resumed.Indeterminate [] "nothing was refused"
                    Expect.equal resumed.Overrides [] "and nothing was overridden"
                }

                test "a crash inside the second op performer leaves it indeterminate, and the default replay refuses it" {
                    let journal = Journal.declaringDurable (Journal.inMemory ())
                    let firstLog = OpLog()
                    let audit = Counter()
                    let services = DurableServices.create |> DurableServices.withJournal journal

                    crashing (fun () ->
                        runEdits
                            services
                            (auditRegistry audit)
                            (performingThenDyingAt "readout" firstLog)
                            editsThenAudit)
                    |> fun died -> Expect.isTrue died "killed inside the second op performer, after its effect"

                    Expect.equal
                        firstLog.Performed
                        [ enc removeRefresh; enc removeReadout ]
                        "both ops happened — and the second's record did not"

                    Expect.equal
                        (Journal.describe (journal.Read "inv"))
                        [ sprintf "0 ApplyOps %s attempted" (subjectOf removeRefresh)
                          sprintf "0 ApplyOps %s completed" (subjectOf removeRefresh)
                          sprintf "1 ApplyOps %s attempted" (subjectOf removeReadout) ]
                        "one op stage decided, one attempted, and no more"

                    let resumeLog = OpLog()

                    let replay =
                        runEdits services (auditRegistry audit) (performing resumeLog) editsThenAudit

                    Expect.equal replay.Replayed [ 0 ] "the decided op stage was served"
                    Expect.equal replay.Indeterminate [ 1 ] "the undecided one was REFUSED"
                    Expect.equal replay.Invoked [] "and no performer ran — not the op, and not the host call after it"
                    Expect.equal resumeLog.Performed [] "so the op was not performed a second time"
                    Expect.equal audit.Count 0 "and the host call was never reached"
                    Expect.isFalse replay.Outcome.Committed "the handler rolled back around the refusal"

                    Expect.equal
                        replay.Outcome.Diagnostics
                        [ ServerDiagnostic.PerformFailed("ApplyOps", DurableCode.IndeterminateStep) ]
                        "naming the op stage's capability and the code, with no payload"

                    Expect.equal
                        replay.Outcome.Performed
                        [ "ApplyOps" ]
                        "and the residual names the stage that did run — reported, never absorbed"

                    Expect.equal replay.Overrides [] "no override was recorded, because none was used"
                }

                test "the opt-in re-invokes the undecided op stage, records the override under ApplyOps, and DUPLICATES" {
                    let journal = Journal.declaringDurable (Journal.inMemory ())
                    let audit = Counter()

                    let services =
                        DurableServices.create
                        |> DurableServices.withJournal journal
                        |> DurableServices.acceptingIndeterminateReplay

                    crashing (fun () ->
                        runEdits
                            services
                            (auditRegistry audit)
                            (performingThenDyingAt "readout" (OpLog()))
                            editsThenAudit)
                    |> ignore

                    let resumeLog = OpLog()

                    let replay =
                        runEdits services (auditRegistry audit) (performing resumeLog) editsThenAudit

                    Expect.equal
                        resumeLog.Performed
                        [ enc removeReadout ]
                        "the second op ran AGAIN — the duplicate the opt-in accepts, and the first was not touched"

                    Expect.equal replay.Invoked [ 1; 2 ] "then the host call"
                    Expect.isTrue replay.Outcome.Committed "and the resume completed"

                    Expect.equal
                        (replay.Overrides |> List.map (fun o -> o.Step, o.Capability))
                        [ 1, "ApplyOps" ]
                        "and the override was RECORDED under the op stage's capability"
                }

                test "an op performer the host declares idempotent closes the window with no override" {
                    let journal = Journal.declaringDurable (Journal.inMemory ())
                    let audit = Counter()

                    let services =
                        DurableServices.create
                        |> DurableServices.withJournal journal
                        |> DurableServices.declaringOpPerformer IdempotencyFacet.Idempotent

                    crashing (fun () ->
                        runEdits
                            services
                            (auditRegistry audit)
                            (performingThenDyingAt "readout" (OpLog()))
                            editsThenAudit)
                    |> ignore

                    let resumeLog = OpLog()

                    let replay =
                        runEdits services (auditRegistry audit) (performing resumeLog) editsThenAudit

                    Expect.equal
                        resumeLog.Performed
                        [ enc removeReadout ]
                        "re-invoked, because repeating it costs nothing"

                    Expect.equal replay.Invoked [ 1; 2 ] "then the host call"
                    Expect.isTrue replay.Outcome.Committed "the resume completed"
                    Expect.equal replay.Overrides [] "with no override: the performer's own declared shape closes it"
                }

                test
                    "a replay holding a different op at a recorded ordinal is refused — the subject is what tells them apart" {
                    let journal = Journal.declaringDurable (Journal.inMemory ())
                    let audit = Counter()
                    let services = DurableServices.create |> DurableServices.withJournal journal

                    runEdits services (auditRegistry audit) (performing (OpLog())) editsThenAudit
                    |> ignore

                    // The same invocation id, the same capability at every
                    // ordinal — ApplyOps, ApplyOps, host:audit — and a different
                    // op at the first. Without the subject this would be served.
                    let resumeLog = OpLog()

                    let replay =
                        runEdits services (auditRegistry audit) (performing resumeLog) editsSwapped

                    Expect.isFalse replay.Outcome.Committed "the divergent replay did not commit"

                    Expect.equal
                        replay.Outcome.Diagnostics
                        [ ServerDiagnostic.PerformFailed("ApplyOps", DurableCode.ReplayDivergence) ]
                        "it named the ordinal's identity mismatch, under the op stage's capability, and stopped"

                    Expect.equal replay.Replayed [] "nothing was served"
                    Expect.equal resumeLog.Performed [] "and no op was performed"
                    Expect.equal audit.Count 1 "nor the host call, a second time"
                }

                test
                    "an op stage's RECEIPT is what the journal records as its completed value, and a replay serves it (Phase 1981)" {
                    let journal = Journal.declaringDurable (Journal.inMemory ())
                    let audit = Counter()
                    let services = DurableServices.create |> DurableServices.withJournal journal

                    let receipted: OpPerformance<Node<obj>, TreeOp<obj>> =
                        OpPerformance.performedBy (fun _ op -> Ok(jstr ("did:" + enc op)))

                    let first = runEdits services (auditRegistry audit) receipted editsThenAudit
                    Expect.isTrue first.Outcome.Committed "committed"

                    let completed =
                        journal.Read "inv"
                        |> List.choose (fun entry ->
                            match entry.Phase with
                            | JournalPhase.Completed value when entry.Capability = Durable.OpStageCapability ->
                                Some value
                            | _ -> None)

                    Expect.equal
                        completed
                        [ jstr ("did:" + enc removeRefresh); jstr ("did:" + enc removeReadout) ]
                        "the receipt the performer answered is the step's completed value — not the inert object"

                    let resumed =
                        runEdits
                            services
                            (auditRegistry audit)
                            (OpPerformance.performedBy (fun _ _ -> failwith "a replay reaches no performer"))
                            editsThenAudit

                    Expect.equal resumed.Replayed [ 0; 1; 2 ] "every step served from the journal"
                    Expect.isTrue resumed.Outcome.Committed "and the resumed run committed on the served receipts"
                }

                test
                    "a receipt the contract rejects is journaled as REFUSED, never as a completed performance, and the op is not reported as performed (Phase 1981)" {
                    let journal = Journal.declaringDurable (Journal.inMemory ())
                    let audit = Counter()
                    let services = DurableServices.create |> DurableServices.withJournal journal
                    let log = OpLog()

                    // The contract admits the first op's receipt and rejects the
                    // second's — a performer that did the first thing it said and
                    // then claimed more than its op reaches.
                    let withinReach: OpContract<Node<obj>, TreeOp<obj>> =
                        { Name = "within-reach"
                          Holds = fun _ op _ -> enc op = enc removeRefresh }

                    let overreaching =
                        OpPerformance.performedChecked withinReach (fun _ op ->
                            log.Record op
                            Ok(jstr ("did:" + enc op)))

                    let outcome = runEdits services (auditRegistry audit) overreaching editsThenAudit

                    Expect.isFalse outcome.Outcome.Committed "rolled back"

                    Expect.equal
                        outcome.Outcome.Performed
                        [ "ApplyOps" ]
                        "the op whose receipt held is reported; the one the contract refused is NOT"

                    Expect.equal
                        (outcome.Outcome.Diagnostics |> List.last)
                        (ServerDiagnostic.PerformFailed("ApplyOps", "return-contract:within-reach"))
                        "a PerformFailed naming the contract"

                    Expect.equal log.Performed [ enc removeRefresh; enc removeReadout ] "the performer DID run for both"
                    Expect.equal audit.Count 0 "and the host call after the refused op never ran"

                    let s0 = subjectOf removeRefresh
                    let s1 = subjectOf removeReadout

                    Expect.equal
                        (Journal.describe (journal.Read "inv"))
                        [ sprintf "0 ApplyOps %s attempted" s0
                          sprintf "0 ApplyOps %s completed" s0
                          sprintf "1 ApplyOps %s attempted" s1
                          sprintf "1 ApplyOps %s refused" s1
                          "-1 Invocation completed" ]
                        "the rejected receipt is journaled as a REFUSAL at its ordinal — a contract refusal is a \
                         decided step, not a completed performance and not an indeterminate one"

                    let replayLog = OpLog()

                    let replayed =
                        runEdits
                            services
                            (auditRegistry audit)
                            (OpPerformance.performedChecked withinReach (fun _ op ->
                                replayLog.Record op
                                Ok(jstr "never")))
                            editsThenAudit

                    Expect.equal replayLog.Performed [] "a replay reaches no performer: the refusal is served"
                    Expect.equal replayed.Replayed [ 0; 1 ] "both decided steps served, the refusal included"
                    Expect.isFalse replayed.Outcome.Committed "and the served refusal halts where it halted"

                    Expect.equal
                        (replayed.Outcome.Diagnostics |> List.last)
                        (ServerDiagnostic.PerformFailed("ApplyOps", "return-contract:within-reach"))
                        "naming the same contract"
                }

                test "in memory, nothing new is journaled — the UI tier's journal is what it was" {
                    let journal = Journal.declaringDurable (Journal.inMemory ())
                    let audit = Counter()
                    let services = DurableServices.create |> DurableServices.withJournal journal

                    let outcome =
                        Durable.run
                            services
                            "inv"
                            (auditRegistry audit)
                            Fuaran.Compute.DataFrame.noResolve
                            "node"
                            editsThenAudit
                            emptyStore

                    Expect.equal
                        (Journal.describe (journal.Read "inv"))
                        [ "0 host:audit attempted"
                          "0 host:audit completed"
                          "-1 Invocation completed" ]
                        "the host call is the only step journaled: the apply IS the effect, recomputed on replay"

                    Expect.equal outcome.Invoked [ 0 ] "one ordinal"

                    Expect.equal
                        outcome.Outcome.Performed
                        [ "ApplyOps"; "host:audit" ]
                        "and the audit trail is the in-memory placement's, as it always was"
                }

                test
                    "a performed op is derived on a host call's terms, and reaches exactly-once only where it is earned" {
                    let edits = ServerEffect.ApplyOps [ removeRefresh ]
                    let performed = performing (OpLog())

                    let delivery discipline performers performance =
                        (Facets.ofEffect discipline performers performance edits).Delivery
                        |> DeliveryFacet.ofHazards

                    Expect.equal
                        (delivery (durableDiscipline false) PerformerFacets.none inMemory)
                        (Some DeliveryFacet.ExactlyOnceEffective)
                        "in memory the arm is recomputed: exactly-once, as before this phase"

                    Expect.equal
                        (delivery (durableDiscipline false) PerformerFacets.none performed)
                        (Some DeliveryFacet.AtMostOnce)
                        "performed and undeclared, strict: the placement may LOSE the op"

                    Expect.equal
                        (delivery (durableDiscipline true) PerformerFacets.none performed)
                        (Some DeliveryFacet.AtLeastOnce)
                        "…accepting: it may DUPLICATE it"

                    Expect.equal
                        (delivery
                            (durableDiscipline false)
                            (PerformerFacets.none
                             |> PerformerFacets.declareOpPerformer IdempotencyFacet.Idempotent)
                            performed)
                        (Some DeliveryFacet.ExactlyOnceEffective)
                        "a content-addressed write the host declares idempotent earns exactly-once"

                    Expect.equal
                        (delivery
                            (durableDiscipline true)
                            (PerformerFacets.none
                             |> PerformerFacets.declareOpPerformer IdempotencyFacet.NonIdempotent)
                            performed)
                        (Some DeliveryFacet.AtLeastOnce)
                        "a push the host says is not idempotent cannot claim it, under either policy"

                    Expect.isFalse
                        (allDisciplines
                         |> List.exists (fun d ->
                             delivery d PerformerFacets.none performed = Some DeliveryFacet.ExactlyOnceEffective))
                        "under NO configuration does an undeclared op performer reach exactly-once"

                    let services =
                        DurableServices.create
                        |> DurableServices.withJournal (Journal.declaringDurable (Journal.inMemory ()))
                        |> DurableServices.declaringPerformer "audit" IdempotencyFacet.Idempotent

                    let inflated =
                        { Placement = PlacementId.durable
                          LogicTree = logicTree
                          Guarantees =
                            { Delivery = DeliveryFacet.ExactlyOnceEffective
                              Idempotency = IdempotencyFacet.Idempotent
                              Restart = RestartVisibility.SurvivesRestart } }

                    let findings =
                        Durable.checkDeclaration services performed [ editsThenAudit ] inflated

                    Expect.contains
                        (findings |> List.map _.Code)
                        FacetCode.DeliveryInflated
                        "an undeclared op performer cannot be exactly-once, and saying so is refused"

                    Expect.isTrue
                        (findings
                         |> List.exists (fun f ->
                             f.Code = FacetCode.UndeclaredPerformer && f.Capability = Some "ApplyOps"))
                        "and the report says WHY, under the capability the op stages are journaled under"

                    Expect.equal
                        (Durable.checkDeclaration
                            (services |> DurableServices.declaringOpPerformer IdempotencyFacet.Idempotent)
                            performed
                            [ editsThenAudit ]
                            inflated)
                        []
                        "declared idempotent, the same registration checks clean"

                    Expect.isFalse
                        (Durable.checkDeclaration services inMemory [ editsThenAudit ] inflated
                         |> List.exists (fun f -> f.Code = FacetCode.DeliveryInflated))
                        "and in memory the delivery was never inflated: nothing reaches outside"
                } ]

          testList
              "Phase 1980 - the proved durable replay as oracle"
              [ test "a first run: nothing recorded, everything invoked" {
                    let journal = Journal.declaringDurable (Journal.inMemory ())

                    let production, log =
                        replayDifferential DurableServices.create journal (fun _ -> Ok(jstr "recorded"))

                    Expect.equal production.Invoked [ 0; 1; 2 ] "every ordinal invoked"
                    Expect.equal log.Performed [ enc removeRefresh; enc removeReadout ] "both ops performed"
                }

                test "interrupted between two op stages: the prefix served, the rest performed" {
                    let journal = Journal.declaringDurable (Journal.inMemory ())

                    crashing (fun () ->
                        runEdits
                            (DurableServices.create
                             |> DurableServices.withJournal (dyingBeforeAttempting 1 journal))
                            (auditRegistry (Counter()))
                            (performing (OpLog()))
                            editsThenAudit)
                    |> ignore

                    let production, _ =
                        replayDifferential DurableServices.create journal (fun _ -> Ok(jstr "recorded"))

                    Expect.equal production.Replayed [ 0 ] "the differential ran the resume case"
                }

                test "interrupted, and the host call refuses on the resume" {
                    let journal = Journal.declaringDurable (Journal.inMemory ())

                    crashing (fun () ->
                        runEdits
                            (DurableServices.create
                             |> DurableServices.withJournal (dyingBeforeAttempting 1 journal))
                            (auditRegistry (Counter()))
                            (performing (OpLog()))
                            editsThenAudit)
                    |> ignore

                    let production, _ =
                        replayDifferential DurableServices.create journal (fun _ -> Error "refused")

                    Expect.isFalse production.Outcome.Committed "the differential ran the refusal case"
                    Expect.equal production.Invoked [ 1; 2 ] "and the refused call counts as invoked"
                }

                test "a recorded refusal is served, and halts where it halted" {
                    let journal = Journal.declaringDurable (Journal.inMemory ())

                    runEdits
                        (DurableServices.create |> DurableServices.withJournal journal)
                        (registryOf [ "audit", (fun _ -> Error "refused") ])
                        (performing (OpLog()))
                        editsThenAudit
                    |> ignore

                    let production, log =
                        replayDifferential DurableServices.create journal (fun _ -> Ok(jstr "recorded"))

                    Expect.equal production.Replayed [ 0; 1; 2 ] "every step served, the refusal included"
                    Expect.equal log.Performed [] "and no op performed"
                }

                testList
                    "a crash inside the second op stage, under each policy"
                    [ for name, services in
                          [ "strict", DurableServices.create
                            "accepting", DurableServices.create |> DurableServices.acceptingIndeterminateReplay
                            "declared idempotent",
                            DurableServices.create
                            |> DurableServices.declaringOpPerformer IdempotencyFacet.Idempotent ] ->
                          test name {
                              let journal = Journal.declaringDurable (Journal.inMemory ())

                              crashing (fun () ->
                                  runEdits
                                      (services |> DurableServices.withJournal journal)
                                      (auditRegistry (Counter()))
                                      (performingThenDyingAt "readout" (OpLog()))
                                      editsThenAudit)
                              |> ignore

                              let production, _ =
                                  replayDifferential services journal (fun _ -> Ok(jstr "recorded"))

                              Expect.equal production.Replayed [ 0 ] "the differential ran the indeterminate case"
                          } ]

                test "a recorded ordinal holding another op: refused as divergence" {
                    let journal = Journal.declaringDurable (Journal.inMemory ())

                    runEdits
                        (DurableServices.create |> DurableServices.withJournal journal)
                        (auditRegistry (Counter()))
                        (performing (OpLog()))
                        editsSwapped
                    |> ignore

                    let production, _ =
                        replayDifferential DurableServices.create journal (fun _ -> Ok(jstr "recorded"))

                    Expect.equal
                        production.Outcome.Diagnostics
                        [ ServerDiagnostic.PerformFailed("ApplyOps", DurableCode.ReplayDivergence) ]
                        "the differential ran the divergence case"
                } ]

          testList
              "the conjunction rule, and what it refuses to claim"
              [ test "combination is associative, commutative, idempotent, with a two-sided identity" {
                    // Pinned over the whole 36-value lattice rather than
                    // sampled: a combination whose laws nobody checked is a
                    // combination nobody can reason with.
                    for a in allDerived do
                        Expect.equal (Facets.combine a Facets.neutral) a "neutral is a right identity"
                        Expect.equal (Facets.combine Facets.neutral a) a "and a left identity"
                        Expect.equal (Facets.combine a a) a "idempotent"

                        for b in allDerived do
                            Expect.equal (Facets.combine a b) (Facets.combine b a) "commutative"

                            for c in allDerived do
                                Expect.equal
                                    (Facets.combine (Facets.combine a b) c)
                                    (Facets.combine a (Facets.combine b c))
                                    "associative"
                }

                test "combination NEVER strengthens: every part's hazards survive into the whole" {
                    for a in allDerived do
                        for b in allDerived do
                            let combined = Facets.combine a b

                            for part in [ a; b ] do
                                Expect.isTrue
                                    (not part.Delivery.MayLose || combined.Delivery.MayLose)
                                    "a part that may lose makes the whole one that may lose"

                                Expect.isTrue
                                    (not part.Delivery.MayDuplicate || combined.Delivery.MayDuplicate)
                                    "and a part that may duplicate makes the whole one that may duplicate"

                                Expect.isTrue
                                    (IdempotencyFacet.hazardRank combined.Idempotency
                                     >= IdempotencyFacet.hazardRank part.Idempotency)
                                    "the whole is no more idempotent than its least idempotent part"

                                Expect.isTrue
                                    (RestartVisibility.hazardRank combined.Restart
                                     >= RestartVisibility.hazardRank part.Restart)
                                    "and no more restart-durable than its least durable part"
                }

                test "a derived handler facet is never stronger than any arm it holds" {
                    // The same law one level up, over the real derivation rather
                    // than over synthetic triples — so a future arm added to the
                    // per-arm table cannot escape it.
                    for discipline in allDisciplines do
                        for performers in
                            [ PerformerFacets.none
                              PerformerFacets.none
                              |> PerformerFacets.declare "audit" IdempotencyFacet.Idempotent ] do
                            let handler =
                                { Name = "all"
                                  Stages = allEffects |> List.map Effect }

                            let whole = Facets.ofHandler discipline performers inMemory handler

                            for effect in allEffects do
                                let arm = Facets.ofEffect discipline performers inMemory effect

                                Expect.isTrue
                                    (not arm.Delivery.MayLose || whole.Delivery.MayLose)
                                    $"{ServerEffect.kind effect}: a losing arm makes a losing handler"

                                Expect.isTrue
                                    (not arm.Delivery.MayDuplicate || whole.Delivery.MayDuplicate)
                                    $"{ServerEffect.kind effect}: a duplicating arm makes a duplicating handler"
                }

                test "the strongest facet is reachable — and only where it is earned" {
                    let engineOwned =
                        { Name = "engine"
                          Stages =
                            [ Effect(ServerEffect.RunQuery("slot", Fuaran.Core.Embedded rows, []))
                              Effect(ServerEffect.ApplyOps [ TreeOp.RemoveNode(NodeId "x") ])
                              Effect(ServerEffect.Notify("ops", jstr "n")) ] }

                    Expect.equal
                        (Facets.ofHandler (durableDiscipline false) PerformerFacets.none inMemory engineOwned
                         |> Facets.narrowest
                         |> Option.map _.Delivery)
                        (Some DeliveryFacet.ExactlyOnceEffective)
                        "a handler that reaches nothing outside is exactly-once-effective under replay"

                    Expect.equal
                        (Facets.ofHandler PlacementDiscipline.Direct PerformerFacets.none inMemory engineOwned
                         |> Facets.narrowest
                         |> Option.map _.Delivery)
                        (Some DeliveryFacet.AtMostOnce)
                        "…and is NOT under the direct interpreter, which journals nothing"

                    let declared =
                        PerformerFacets.none
                        |> PerformerFacets.declare "audit" IdempotencyFacet.Idempotent

                    Expect.equal
                        (Facets.ofHandler (durableDiscipline false) declared inMemory refreshHandler
                         |> Facets.narrowest
                         |> Option.map _.Delivery)
                        (Some DeliveryFacet.ExactlyOnceEffective)
                        "a host call the host declares idempotent earns the same facet"
                }

                test "an undeclared performer is never flattered — the honest boundary, both ways" {
                    Expect.equal
                        (Facets.ofHandler (durableDiscipline false) PerformerFacets.none inMemory refreshHandler
                         |> Facets.narrowest
                         |> Option.map _.Delivery)
                        (Some DeliveryFacet.AtMostOnce)
                        "strict: the placement may LOSE the call rather than repeat it"

                    Expect.equal
                        (Facets.ofHandler (durableDiscipline true) PerformerFacets.none inMemory refreshHandler
                         |> Facets.narrowest
                         |> Option.map _.Delivery)
                        (Some DeliveryFacet.AtLeastOnce)
                        "accepting: it may DUPLICATE it — and neither policy may claim exactly-once"

                    Expect.isFalse
                        (allDisciplines
                         |> List.exists (fun d ->
                             Facets.ofHandler d PerformerFacets.none inMemory refreshHandler
                             |> Facets.narrowest
                             |> Option.map _.Delivery = Some DeliveryFacet.ExactlyOnceEffective))
                        "under NO configuration does an undeclared host call reach exactly-once"
                }

                test "a journal that dies with the process claims nothing" {
                    let services =
                        DurableServices.create
                        |> DurableServices.withJournal (Journal.inMemory ())
                        |> DurableServices.declaringPerformer "audit" IdempotencyFacet.Idempotent

                    Expect.equal
                        (Durable.guarantees services inMemory [ refreshHandler ] |> Facets.narrowest)
                        (Facets.ofHandler PlacementDiscipline.Direct services.Performers inMemory refreshHandler
                         |> Facets.narrowest)
                        "an in-memory journal derives the DIRECT interpreter's posture, exactly"
                } ]

          testList
              "the placement declaration, end to end"
              [ test "a derived declaration names the placement and the logic tree, and checks clean" {
                    let services =
                        DurableServices.create
                        |> DurableServices.withJournal (Journal.declaringDurable (Journal.inMemory ()))
                        |> DurableServices.declaringPerformer "audit" IdempotencyFacet.Idempotent

                    match Durable.declaration services inMemory logicTree [ refreshHandler ] with
                    | None -> failtest "the registration has an honest declaration and should have produced one"
                    | Some declaration ->
                        Expect.equal declaration.Placement PlacementId.durable "it names this placement"
                        Expect.equal declaration.LogicTree logicTree "and the logic tree it is about"

                        Expect.equal
                            declaration.Guarantees.Delivery
                            DeliveryFacet.ExactlyOnceEffective
                            "with the facet this phase exists to certify"

                        Expect.equal
                            (Durable.checkDeclaration services inMemory [ refreshHandler ] declaration)
                            []
                            "and a derived declaration is consistent with the registration it came from"

                        Expect.equal PlacementId.slot ProgramWire.logicTreeSlot "the slot id is the specification's own"
                }

                test "AN INFLATING DECLARATION GOES RED" {
                    // The negative test the acceptance names. Nothing else in
                    // this file would fail if `checkDeclaration` returned the
                    // empty list unconditionally.
                    let services =
                        DurableServices.create
                        |> DurableServices.withJournal (Journal.declaringDurable (Journal.inMemory ()))

                    let inflated =
                        { Placement = PlacementId.durable
                          LogicTree = logicTree
                          Guarantees =
                            { Delivery = DeliveryFacet.ExactlyOnceEffective
                              Idempotency = IdempotencyFacet.Idempotent
                              Restart = RestartVisibility.SurvivesRestart } }

                    let findings =
                        Durable.checkDeclaration services inMemory [ refreshHandler ] inflated

                    Expect.contains
                        (findings |> List.map _.Code)
                        FacetCode.DeliveryInflated
                        "an undeclared host call cannot be exactly-once, and saying so is refused"

                    Expect.contains
                        (findings |> List.map _.Code)
                        FacetCode.IdempotencyInflated
                        "…nor intrinsically idempotent"

                    Expect.contains
                        (findings |> List.map _.Code)
                        FacetCode.RestartInflated
                        "…nor durable across a restart"

                    Expect.contains
                        (findings |> List.map _.Code)
                        FacetCode.UndeclaredPerformer
                        "and the report says WHY: the host declared nothing about the performer"

                    Expect.isTrue
                        (findings
                         |> List.filter Facets.isInflation
                         |> List.forall (fun f -> not (f.Detail.Contains "refreshed")))
                        "no finding echoes a handler's payload"
                }

                test "a declaration that promises LESS raises nothing" {
                    let services =
                        DurableServices.create
                        |> DurableServices.withJournal (Journal.declaringDurable (Journal.inMemory ()))
                        |> DurableServices.declaringPerformer "audit" IdempotencyFacet.Idempotent

                    let conservative =
                        { Placement = PlacementId.durable
                          LogicTree = logicTree
                          Guarantees =
                            { Delivery = DeliveryFacet.AtMostOnce
                              Idempotency = IdempotencyFacet.NonIdempotent
                              Restart = RestartVisibility.LostOnRestart } }

                    Expect.equal
                        (Durable.checkDeclaration services inMemory [ refreshHandler ] conservative
                         |> List.filter Facets.isInflation)
                        []
                        "promising less than you can keep costs only the promise"
                }

                test "a placement id this package does not serve is reported, not guessed" {
                    let services = DurableServices.create

                    let foreign =
                        { Placement = "somebody.else/engine"
                          LogicTree = logicTree
                          Guarantees =
                            { Delivery = DeliveryFacet.AtMostOnce
                              Idempotency = IdempotencyFacet.NonIdempotent
                              Restart = RestartVisibility.LostOnRestart } }

                    Expect.contains
                        (Durable.checkDeclaration services inMemory [ refreshHandler ] foreign
                         |> List.map _.Code)
                        FacetCode.UnknownPlacement
                        "an id nobody here serves is a finding"

                    Expect.isNone (PlacementId.disciplineOf "somebody.else/engine") "and resolves to no discipline"
                }

                test "the mirrored vocabulary round-trips through its tags" {
                    // The tags ARE the cross-boundary agreement, so they are what
                    // a suite has to pin: a mirror nobody checks is a copy.
                    for facet in DeliveryFacet.all do
                        Expect.equal (DeliveryFacet.ofTag (DeliveryFacet.tag facet)) (Ok facet) "delivery"

                    for facet in IdempotencyFacet.all do
                        Expect.equal (IdempotencyFacet.ofTag (IdempotencyFacet.tag facet)) (Ok facet) "idempotency"

                    for facet in RestartVisibility.all do
                        Expect.equal (RestartVisibility.ofTag (RestartVisibility.tag facet)) (Ok facet) "restart"

                    Expect.equal
                        (DeliveryFacet.all |> List.map DeliveryFacet.tag)
                        [ "atMostOnce"; "atLeastOnce"; "exactlyOnceEffective" ]
                        "and the spellings are the agreed ones, pinned literally"
                } ] ]
