module Fuaran.UI.Program.Server.Tests.OperatorControlTests

// ─── The operator's controls, as recorded replayable ops ─────────────────────
//
// Four claims, and each is asserted in the form that could go red rather than in
// the form that reads well.
//
//  1. THE ACT IS A RECORD, AND THE STATE IS A FOLD OF IT. Not "there is a
//     suspend flag somewhere" — the state is `Controls.fold` of the stream and
//     of nothing else, so every prefix of a stream folds to the state that
//     prefix's reader gets. That is what makes a resume a replay rather than a
//     second implementation of the same decision, and it is checked over prefixes
//     rather than over one happy path.
//
//  2. REVOCATION IS MONOTONE. Asserted across every prefix of a stream that
//     deliberately contains a `Resume` after the revoke, because the only way
//     this claim fails is if some arm is quietly made to clear the set.
//
//  3. A CONTROL REFUSES IN THE REGISTRY'S OWN VOCABULARY. A revoked performer
//     reads as `Unregistered` — not as a new denial arm — which is what carries
//     it through to the coverage check with nothing new to teach. The coverage
//     assertion is here and not only in the demanded suite for that reason: it
//     is the end of the thread, and a thread is what breaks.
//
//  4. A SUSPENDED-THEN-RESUMED SESSION CONTINUES BYTE-IDENTICALLY. Compared as
//     canonical JSON against an uninterrupted run of the same events, plus a
//     count of the host performer's own invocations — a count being the only
//     form of "no duplicate effect" that an implementation which merely looks
//     careful cannot satisfy.
//
// ── The rule this suite must never quietly relax ─────────────────────────────
// A `Resume` lifts the SUSPEND and nothing else. Making it clear throttles or
// revocations would read as tidier and would turn the mildest word in the
// vocabulary into the most consequential one. A future edit that does it fails
// here, on purpose.

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops.Introspect
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.Renderer.BindingResolver
open Fuaran.Program.Bounded
open Fuaran.UI.Program
open Fuaran.Program.Server
open Fuaran.UI.Program.Server

// ─── fixtures ────────────────────────────────────────────────────────────────

let private jstr (s: string) = Fuaran.Core.JStr s

let private endpoint = "/handlers/work"

let private scope = "session-1"

let private ops = ControlActor.operator "ops"

let private detector = ControlActor.machine "denial-patterns"

/// A counting performer: what the host actually ran, which is the whole subject
/// of the revoke and throttle families.
type private Counter() =
    let mutable count = 0
    member _.Count = count

    member _.Performer: Fuaran.Core.JVal -> Result<Fuaran.Core.JVal, string> =
        fun _ ->
            count <- count + 1
            Ok(jstr "recorded")

/// A performer that commits and then the process dies — the crash inside the
/// indeterminate window, where the effect happened and the record of it did not.
exception private ProcessDied of string

let private boundMarkdown (id: string) (key: string) (dflt: string) : Node<obj> =
    let n = Fuaran.markdown id "placeholder"

    { n with
        Kind = NodeKind.Markdown({ Text = TextSource.Bound(Binding.State(key, Some dflt)) }) }

let private treeNode (onClick: Action<obj>) : Node<obj> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<obj> with
            Children =
                [ Fuaran.button
                      "call"
                      { Defaults.button<obj> with
                          Label = TextSource.Literal "call"
                          OnClick = onClick }
                  boundMarkdown "readout" "status" "init" ] }

let private wire (onClick: Action<obj>) : WireTree = WireTree.ofDecoded (treeNode onClick)

let private callWire = wire (Action.Call(endpoint, None, None))

let private clickEv (seq: int) : LiveEvent =
    { ConnId = "server"
      NodeId = "call"
      Event = "click"
      Payload = Map.empty
      LastSeq = seq }

let private readout (tree: Node<obj>) : string option =
    match findNode (NodeId "readout") tree with
    | Some node ->
        match node.Kind with
        | NodeKind.Markdown({ Text = TextSource.Literal s }) -> Some s
        | _ -> None
    | None -> None

/// One host call, then a compute whose write is visible through `readout`.
let private auditing (answer: string) : Handler =
    { Name = "work"
      Stages =
        [ Effect(ServerEffect.HostCall("audit", jstr "x", Some "note"))
          Compute(Action.SetState("status", Some(jstr answer), None)) ] }

/// Three host calls to the same performer — enough for a window of two to be
/// crossed exactly once.
let private thrice: Handler =
    { Name = "work"
      Stages =
        [ Effect(ServerEffect.HostCall("audit", jstr "1", None))
          Effect(ServerEffect.HostCall("audit", jstr "2", None))
          Effect(ServerEffect.HostCall("audit", jstr "3", None)) ] }

let private registryOf (performer: Fuaran.Core.JVal -> Result<Fuaran.Core.JVal, string>) =
    ServerEffectRegistry.denyAll
    |> ServerEffectRegistry.register "audit" performer
    |> ServerEffectRegistry.permissive

let private servicesOf (registry: ServerEffectRegistry) (handler: Handler) : ServerServices =
    { ServerServices.createPermissive with
        Handlers = Map.ofList [ endpoint, handler ]
        Effects = registry }

let private durableWith (journal: EffectJournal) =
    DurableServices.create |> DurableServices.withJournal journal

let private controlsOn (journal: ControlJournal) : ControlServices =
    ControlServices.create scope |> ControlServices.withJournal journal

let private store: ServerStore =
    { Tree = treeNode (Action.Call(endpoint, None, None))
      Bindings = empty }

let private entriesOf (journal: ControlJournal) = journal.Read scope

/// Every prefix of a list, shortest first — what "any reader of any prefix"
/// means as a value.
let private prefixes (items: 'a list) : 'a list list =
    [ for n in 0 .. List.length items -> List.truncate n items ]

// ─── tests ───────────────────────────────────────────────────────────────────

[<Tests>]
let tests =
    testList
        "Phase 1746 — suspend, throttle, revoke and resume as recorded ops"
        [

          // ── the record ───────────────────────────────────────────────

          test "every op carries its reason and its actor, and its ordinal is the stream's" {
              let journal = Controls.inMemory ()
              let controls = controlsOn journal

              DurableControls.record controls (Controls.suspend ops "spend spike") |> ignore

              DurableControls.record
                  controls
                  (Controls.throttle
                      ops
                      "slow the mailer"
                      { Capability = "host:audit"
                        MaxPerInvocation = 2 })
              |> ignore

              DurableControls.record controls (Controls.revoke detector "repeated denials" "audit")
              |> ignore

              DurableControls.record controls (Controls.resume ops "reviewed") |> ignore

              let entries = entriesOf journal

              Expect.equal
                  (entries |> List.map _.Sequence)
                  [ 0; 1; 2; 3 ]
                  "ordinals come off the stream, not the raiser"

              Expect.equal
                  (entries |> List.map _.Reason)
                  [ "spend spike"; "slow the mailer"; "repeated denials"; "reviewed" ]
                  "every op carries its reason — the resume included"

              Expect.equal
                  (entries |> List.map (fun e -> ControlActor.tag e.Actor, e.Actor.Id))
                  [ "operator", "ops"
                    "operator", "ops"
                    "machine", "denial-patterns"
                    "operator", "ops" ]
                  "and its actor, with the machine raiser recorded as one"

              Expect.equal
                  (entries |> List.map (fun e -> ControlOp.tag e.Op))
                  ControlOp.tags
                  "the four acts, in the order the closed vocabulary names them"
          }

          test "a machine-raised suspend and an operator-raised one are the SAME op" {
              let machineRaised = Controls.suspend detector "denial pattern"
              let handRaised = Controls.suspend ops "denial pattern"

              Expect.equal machineRaised.Op handRaised.Op "one op, not two mechanisms"

              // And the fold cannot tell them apart either — which is the point:
              // a detector's suspension is exactly as binding as a person's, and
              // only the record says which raised it.
              let foldOf request =
                  Controls.fold
                      [ { Sequence = 0
                          Op = request.Op
                          Actor = request.Actor
                          Reason = request.Reason
                          MidStage = [] } ]

              Expect.isTrue (Controls.isSuspended (foldOf machineRaised)) "the machine's suspend binds"
              Expect.isTrue (Controls.isSuspended (foldOf handRaised)) "so does the operator's"

              Expect.equal
                  ((foldOf machineRaised).Suspended |> Option.map (fst >> ControlActor.tag))
                  (Some "machine")
                  "and the state carries the attribution through"
          }

          // ── the fold ─────────────────────────────────────────────────

          test "a resume lifts the SUSPEND and nothing else" {
              let journal = Controls.inMemory ()
              let controls = controlsOn journal

              DurableControls.record controls (Controls.suspend ops "halt") |> ignore

              DurableControls.record
                  controls
                  (Controls.throttle
                      ops
                      "slow it"
                      { Capability = "host:audit"
                        MaxPerInvocation = 1 })
              |> ignore

              DurableControls.record controls (Controls.revoke ops "withdraw" "audit")
              |> ignore

              DurableControls.record controls (Controls.resume ops "reviewed") |> ignore

              let state = DurableControls.stateOf controls

              Expect.isFalse (Controls.isSuspended state) "the suspend is lifted"

              Expect.equal
                  (state.Throttles |> Map.toList |> List.map fst)
                  [ "host:audit" ]
                  "the throttle stands — a standing limit is not a suspension"

              Expect.equal
                  (state.Revoked |> Map.toList |> List.map fst)
                  [ "audit" ]
                  "and the revocation stands, because it is monotone"
          }

          test "revocation is monotone across EVERY prefix of a stream" {
              let raised =
                  [ Controls.revoke detector "denials" "audit"
                    Controls.suspend ops "halt"
                    Controls.throttle
                        ops
                        "slow"
                        { Capability = "ApplyOps"
                          MaxPerInvocation = 1 }
                    Controls.resume ops "reviewed"
                    Controls.revoke ops "withdraw the second" "mailer"
                    Controls.resume ops "reviewed again" ]

              let entries =
                  raised
                  |> List.mapi (fun i r ->
                      { Sequence = i
                        Op = r.Op
                        Actor = r.Actor
                        Reason = r.Reason
                        MidStage = [] })

              let revokedAt =
                  prefixes entries
                  |> List.map (fun prefix -> (Controls.fold prefix).Revoked |> Map.toList |> List.map fst |> Set.ofList)

              revokedAt
              |> List.pairwise
              |> List.iteri (fun i (before, after) ->
                  Expect.isTrue
                      (Set.isSubset before after)
                      (sprintf "prefix %d shrank the revoked set — revocation is not monotone" (i + 1)))

              Expect.equal (List.last revokedAt) (Set.ofList [ "audit"; "mailer" ]) "and both withdrawals survive"
          }

          test "a later throttle of the same capability REPLACES the earlier one" {
              let entryOf i (request: ControlRequest) =
                  { Sequence = i
                    Op = request.Op
                    Actor = request.Actor
                    Reason = request.Reason
                    MidStage = [] }

              let state =
                  Controls.fold
                      [ entryOf
                            0
                            (Controls.throttle
                                ops
                                "first"
                                { Capability = "host:audit"
                                  MaxPerInvocation = 5 })
                        entryOf
                            1
                            (Controls.throttle
                                ops
                                "tighter"
                                { Capability = "host:audit"
                                  MaxPerInvocation = 1 }) ]

              Expect.equal
                  (state.Throttles |> Map.tryFind "host:audit" |> Option.map _.MaxPerInvocation)
                  (Some 1)
                  "one standing window per capability, and the newest wins"
          }

          test "folding a PREFIX gives that prefix's reader the same state, every time" {
              // The replayability claim in the only form that can go red: the
              // state is a function of the entries, so folding a prefix in one
              // step and reaching it incrementally must agree at every position.
              let entries =
                  [ Controls.suspend ops "halt"
                    Controls.revoke ops "withdraw" "audit"
                    Controls.resume ops "reviewed"
                    Controls.throttle
                        ops
                        "slow"
                        { Capability = "Notify"
                          MaxPerInvocation = 0 } ]
                  |> List.mapi (fun i r ->
                      { Sequence = i
                        Op = r.Op
                        Actor = r.Actor
                        Reason = r.Reason
                        MidStage = [] })

              let incremental = entries |> List.scan Controls.step Controls.initial

              let replayed = prefixes entries |> List.map Controls.fold

              Expect.equal replayed incremental "a replay of any prefix reaches the state that prefix produced"

              Expect.equal (replayed |> List.map _.Folded) [ 0..4 ] "and the state says which prefix it is about"
          }

          // ── the effect at the gate ───────────────────────────────────

          test "a suspend refuses every dispatch, and a resume lifts it" {
              let counter = Counter()
              let services = servicesOf (registryOf counter.Performer) (auditing "done")
              let journal = Controls.inMemory ()
              let controls = controlsOn journal
              let durable = durableWith (Journal.inMemory ())
              let session = ServerSession.init services empty callWire

              DurableControls.record controls (Controls.suspend detector "denial pattern")
              |> ignore

              let suspended = DurableControls.step durable controls "inv-0" session (clickEv 0)

              match suspended.Output.Rejected with
              | Some(Gate _) -> ()
              | other -> failtestf "expected a gate rejection from a suspended session, got %A" other

              Expect.equal (readout suspended.Session.Resolved) (Some "init") "and nothing ran"
              Expect.equal counter.Count 0 "the performer was never reached"

              Expect.equal
                  (suspended.Refusals |> List.map Controls.describeRefusal |> List.length)
                  1
                  "the refusal is recorded, not merely enacted"

              match suspended.Refusals with
              | [ ControlRefusal.Suspended(capability, actor, reason) ] ->
                  Expect.equal capability ControlCode.DispatchCapability "refused at the dispatch, not at an effect"
                  Expect.equal (ControlActor.tag actor) "machine" "by the detector"
                  Expect.equal reason "denial pattern" "with its reason on the record"
              | other -> failtestf "expected one suspension refusal, got %A" other

              DurableControls.record controls (Controls.resume ops "reviewed") |> ignore

              let resumed =
                  DurableControls.step durable controls "inv-1" suspended.Session (clickEv 1)

              Expect.isNone resumed.Output.Rejected "the resume lifts the refusal"
              Expect.equal (readout resumed.Session.Resolved) (Some "done") "and the event now does its work"
              Expect.equal counter.Count 1 "the performer ran exactly once"
              Expect.isEmpty resumed.Refusals "and no control refused anything"
          }

          test "a throttled effect refuses with the window NAMED, past the window and not before" {
              let counter = Counter()
              let services = servicesOf (registryOf counter.Performer) thrice
              let journal = Controls.inMemory ()
              let controls = controlsOn journal

              DurableControls.record
                  controls
                  (Controls.throttle
                      ops
                      "the mailer is over budget"
                      { Capability = "host:audit"
                        MaxPerInvocation = 2 })
              |> ignore

              let outcome =
                  DurableControls.run
                      (durableWith (Journal.inMemory ()))
                      controls
                      "inv-0"
                      services.Effects
                      services.Sources
                      "call"
                      thrice
                      store

              match outcome.Refusals with
              | [ ControlRefusal.Throttled(capability, window, attempt) ] ->
                  Expect.equal capability "host:audit" "the capability the window is about"
                  Expect.equal window.MaxPerInvocation 2 "the window itself, on the record"
                  Expect.equal attempt 3 "and where it was crossed"

                  Expect.stringContains
                      (Controls.describeRefusal outcome.Refusals.Head)
                      ControlCode.CapabilityThrottled
                      "the description carries the stable code"
              | other -> failtestf "expected exactly one throttle refusal, got %A" other

              // **And no partial mutation.** A throttle breach is the ordinary
              // structured denial, so it HALTS the handler in the plan phase —
              // and every host call is staged to the perform phase (D8), so a
              // breach at the third leaves none of the three performed. That is
              // the honest reading of "refuses, never a partial mutation", and it
              // is stronger than "the first two ran": a throttled handler commits
              // nothing at all.
              Expect.isFalse outcome.Durable.Outcome.Committed "the handler rolled back"
              Expect.equal counter.Count 0 "and nothing reached the performer"
              Expect.isEmpty outcome.Durable.Outcome.Performed "so there is no residue to take back"

              // Inside the window, the same handler performs every call — the
              // throttle is a limit, not a blanket refusal, and a test that never
              // saw it permit anything would not distinguish the two.
              let inside = Counter()

              let permitted =
                  DurableControls.run
                      (durableWith (Journal.inMemory ()))
                      (controlsOn (Controls.inMemory ()))
                      "inv-1"
                      (registryOf inside.Performer)
                      services.Sources
                      "call"
                      thrice
                      store

              Expect.isEmpty permitted.Refusals "no window, no refusal"
              Expect.equal inside.Count 3 "and all three calls performed"

              // A fresh invocation is a fresh window: the throttle is per
              // invocation by construction, not a running total nobody resets.
              let again = Counter()

              let second =
                  DurableControls.run
                      (durableWith (Journal.inMemory ()))
                      controls
                      "inv-2"
                      (registryOf again.Performer)
                      services.Sources
                      "call"
                      thrice
                      store

              Expect.equal
                  (List.length second.Refusals)
                  1
                  "the next invocation crosses its own window in the same place"

              match second.Refusals with
              | [ ControlRefusal.Throttled(_, _, attempt) ] ->
                  Expect.equal attempt 3 "the count restarted, it did not carry"
              | other -> failtestf "expected one throttle refusal, got %A" other
          }

          test "a revoked performer reads as UNREGISTERED, with the withdrawal on the record" {
              let counter = Counter()
              let denials = ResizeArray<ServerEffectDenial>()

              let registry =
                  registryOf counter.Performer |> ServerEffectRegistry.onDenied denials.Add

              let journal = Controls.inMemory ()
              let controls = controlsOn journal

              DurableControls.record controls (Controls.revoke detector "three refusals in a row" "audit")
              |> ignore

              let outcome =
                  DurableControls.run
                      (durableWith (Journal.inMemory ()))
                      controls
                      "inv-0"
                      registry
                      Fuaran.Compute.DataFrame.noResolve
                      "call"
                      (auditing "done")
                      store

              Expect.equal counter.Count 0 "the withdrawn performer was never reached"

              Expect.equal
                  (List.ofSeq denials)
                  [ ServerEffectDenial.Unregistered "host:audit" ]
                  "and the effect reads as ABSENT, not as gate-refused — the registry's own distinction, unchanged"

              match outcome.Refusals with
              | [ ControlRefusal.Revoked(capability, actor, reason) ] ->
                  Expect.equal capability "host:audit" "the capability behind the withdrawn performer"
                  Expect.equal (ControlActor.tag actor) "machine" "withdrawn by the detector"
                  Expect.equal reason "three refusals in a row" "with its reason on the record"
              | other -> failtestf "expected one revocation refusal, got %A" other
          }

          test "the COVERAGE check reports a revoked performer, by name" {
              let counter = Counter()
              let services = servicesOf (registryOf counter.Performer) (auditing "done")
              let journal = Controls.inMemory ()
              let controls = controlsOn journal

              let projection =
                  ServerDemanded.ofTreeAndHandlers services.Handlers (treeNode (Action.Call(endpoint, None, None)))

              let hostWith (coverage: ServerCoverage) =
                  HostCoverage.nothing |> HostCoverage.withServer coverage

              Expect.isEmpty
                  (Demanded.checkProjection (hostWith (DurableControls.coverage controls services)) projection)
                  "before the withdrawal the host covers the handler"

              DurableControls.record controls (Controls.revoke ops "withdrawn" "audit")
              |> ignore

              Expect.equal
                  (Demanded.checkProjection (hostWith (DurableControls.coverage controls services)) projection)
                  [ CoverageFinding.UnregisteredServerFunction "audit" ]
                  "afterwards it does not, and the finding NAMES the performer — this is the end of the thread"

              // A suspension is a different finding for a different reason, and
              // collapsing the two would lose the more useful fact: only one of
              // them is resolved by changing policy. It is asserted on its OWN
              // stream, because absence outranks policy — a withdrawn performer
              // reads as unregistered whatever the gate would have said, which
              // is exactly right and is why the two cannot share a fixture.
              let suspendedOnly = controlsOn (Controls.inMemory ())

              DurableControls.record suspendedOnly (Controls.suspend ops "halt") |> ignore

              Expect.equal
                  (Demanded.checkProjection (hostWith (DurableControls.coverage suspendedOnly services)) projection)
                  [ CoverageFinding.ServerGateRefusesCapability "host:audit" ]
                  "a suspended session has the capability and refuses it — a gate finding, not an absence"
          }

          test "a session that receives NO control costs nothing and runs identically" {
              let uncontrolled = Counter()
              let controlled = Counter()

              let project (outcome: HandlerOutcome) =
                  {| Tree = CanonicalJson.encodeNode outcome.Store.Tree
                     Committed = outcome.Committed
                     Performed = outcome.Performed
                     Diagnostics = outcome.Diagnostics |> List.map (sprintf "%A") |}

              let direct =
                  Durable.run
                      (durableWith (Journal.inMemory ()))
                      "inv-0"
                      (registryOf uncontrolled.Performer)
                      Fuaran.Compute.DataFrame.noResolve
                      "call"
                      (auditing "done")
                      store

              let withControls =
                  DurableControls.run
                      (durableWith (Journal.inMemory ()))
                      (ControlServices.create scope)
                      "inv-0"
                      (registryOf controlled.Performer)
                      Fuaran.Compute.DataFrame.noResolve
                      "call"
                      (auditing "done")
                      store

              Expect.equal
                  (project withControls.Durable.Outcome)
                  (project direct.Outcome)
                  "the controlled run IS the durable run when no act was recorded"

              Expect.isEmpty withControls.Refusals "nothing refused"
              Expect.equal withControls.Controls Controls.initial "and the fold of an empty stream is the initial state"
              Expect.equal controlled.Count uncontrolled.Count "the performer ran the same number of times"
          }

          // ── suspend, resume, and the replay between them ─────────────

          test "a resumed session's continuation is BYTE-IDENTICAL to an uninterrupted one" {
              let run (interrupt: bool) =
                  let counter = Counter()
                  let services = servicesOf (registryOf counter.Performer) (auditing "done")
                  let controls = controlsOn (Controls.inMemory ())
                  let durable = durableWith (Journal.inMemory ())
                  let session = ServerSession.init services empty callWire

                  let first = DurableControls.step durable controls "inv-0" session (clickEv 0)

                  let resumedFrom =
                      if interrupt then
                          DurableControls.record controls (Controls.suspend detector "denial pattern")
                          |> ignore

                          // The event a suspended session refuses. It must leave
                          // no trace at all — that is what makes the resume a
                          // continuation rather than a reconstruction.
                          let refused =
                              DurableControls.step durable controls "inv-1" first.Session (clickEv 1)

                          Expect.isTrue refused.Output.Rejected.IsSome "the suspended step was refused"

                          DurableControls.record controls (Controls.resume ops "reviewed") |> ignore
                          refused.Session
                      else
                          first.Session

                  let second = DurableControls.step durable controls "inv-2" resumedFrom (clickEv 2)

                  {| Tree = CanonicalJson.encodeNode second.Session.Resolved
                     Readout = readout second.Session.Resolved
                     Performed = second.Output.Performed
                     Ops = second.Output.Ops |> List.map (sprintf "%A")
                     Calls = counter.Count |}

              let uninterrupted = run false
              let interrupted = run true

              Expect.equal
                  interrupted.Tree
                  uninterrupted.Tree
                  "the resumed continuation is the canonical bytes of the uninterrupted one"

              Expect.equal interrupted.Performed uninterrupted.Performed "the same capabilities, in the same order"
              Expect.equal interrupted.Ops uninterrupted.Ops "and the same diff shipped"
              Expect.equal interrupted.Readout (Some "done") "the work happened"

              Expect.equal
                  interrupted.Calls
                  uninterrupted.Calls
                  "and the suspension cost the host performer nothing — no duplicate, no loss"
          }

          test "a MID-STAGE suspend records the indeterminate window, and the resume refuses to guess" {
              let journal = Journal.inMemory ()
              let services = durableWith journal
              let controls = controlsOn (Controls.inMemory ())
              let mutable calls = 0

              let dying =
                  ServerEffectRegistry.denyAll
                  |> ServerEffectRegistry.register "audit" (fun _ ->
                      calls <- calls + 1
                      raise (ProcessDied "after the effect"))
                  |> ServerEffectRegistry.permissive

              let died =
                  try
                      DurableControls.run
                          services
                          controls
                          "inv-0"
                          dying
                          Fuaran.Compute.DataFrame.noResolve
                          "call"
                          (auditing "done")
                          store
                      |> ignore

                      false
                  with ProcessDied _ ->
                      true

              Expect.isTrue died "the fixture's crash must actually have happened"
              Expect.equal calls 1 "the effect committed, and the record of it did not"

              // The operator suspends across the live invocation. The window is
              // READ from the effect journal rather than asserted by the raiser,
              // which is the only way it can be right.
              let entry =
                  DurableControls.suspendMidStage services controls [ "inv-0" ] ops "crashed mid-handler"

              match entry.MidStage with
              | [ window ] ->
                  Expect.equal window.Invocation "inv-0" "the invocation that was live"
                  Expect.equal window.Step 0 "the step ordinal it stopped at"
                  Expect.equal window.Capability "host:audit" "and the capability nobody can decide"
              | other -> failtestf "expected exactly one indeterminate window on the record, got %A" other

              Expect.isTrue (Controls.isSuspended (DurableControls.stateOf controls)) "and the session is suspended"

              // A suspend raised with nothing open records NO window, and that is
              // a statement rather than an omission.
              let quiet =
                  DurableControls.suspendMidStage services controls [ "inv-never-ran" ] ops "quiet"

              Expect.isEmpty quiet.MidStage "no window was open, and the record says so"

              // Resuming re-runs the invocation. The recorded step is
              // indeterminate, so the default REFUSES rather than re-invoking —
              // rounding it either way publishes a guarantee the substrate does
              // not provide.
              DurableControls.record controls (Controls.resume ops "reviewed") |> ignore

              let counter = Counter()

              let resumed =
                  DurableControls.run
                      services
                      controls
                      "inv-0"
                      (registryOf counter.Performer)
                      Fuaran.Compute.DataFrame.noResolve
                      "call"
                      (auditing "done")
                      store

              Expect.equal resumed.Durable.Indeterminate [ 0 ] "the resume names the step it cannot decide"
              Expect.equal counter.Count 0 "and does not re-invoke it"

              Expect.isEmpty
                  resumed.Refusals
                  "no CONTROL refused this — the indeterminate window is D12's, not a control's"
          }

          // ── the wire ─────────────────────────────────────────────────

          test "every op round-trips through its canonical form" {
              let entries =
                  [ { Sequence = 0
                      Op = ControlOp.Suspend
                      Actor = detector
                      Reason = "denial pattern"
                      MidStage =
                        [ { Invocation = "inv-0"
                            Step = 2
                            Capability = "host:audit" } ] }
                    { Sequence = 1
                      Op =
                        ControlOp.Throttle
                            { Capability = "host:audit"
                              MaxPerInvocation = 2 }
                      Actor = ops
                      Reason = "over budget"
                      MidStage = [] }
                    { Sequence = 2
                      Op = ControlOp.Revoke "audit"
                      Actor = ops
                      Reason = "withdrawn"
                      MidStage = [] }
                    { Sequence = 3
                      Op = ControlOp.Resume
                      Actor = ops
                      Reason = "reviewed"
                      MidStage = [] } ]

              for entry in entries do
                  match Controls.decode (Controls.encode entry) with
                  | Ok decoded -> Expect.equal decoded entry (sprintf "%s does not round-trip" (ControlOp.tag entry.Op))
                  | Error refusal -> failtestf "%s was refused: %A" (ControlOp.tag entry.Op) refusal
          }

          test "the canonical rendering puts $type first and orders its members" {
              let rendered =
                  Controls.render
                      { Sequence = 7
                        Op =
                          ControlOp.Throttle
                              { Capability = "host:audit"
                                MaxPerInvocation = 2 }
                        Actor = ops
                        Reason = "over budget"
                        MidStage = [] }

              Expect.equal
                  rendered
                  "{\"$type\":\"Throttle\",\"actor\":{\"id\":\"ops\",\"kind\":\"operator\"},\"reason\":\"over budget\",\"sequence\":7,\"window\":{\"capability\":\"host:audit\",\"maxPerInvocation\":2}}"
                  "the bytes are the document"
          }

          test "an absent optional member is OMITTED, never rendered as a null" {
              let rendered =
                  Controls.render
                      { Sequence = 0
                        Op = ControlOp.Resume
                        Actor = ops
                        Reason = "reviewed"
                        MidStage = [] }

              Expect.isFalse (rendered.Contains "midStage") "an empty window list is absent, not empty-and-present"
              Expect.isFalse (rendered.Contains "null") "and there is no null on this wire"
          }

          test "the decoder REFUSES what the vocabulary does not admit" {
              // A check that has never been seen to refuse anything is a check
              // nobody has verified.
              let refusalOf (json: string) =
                  match ProgramWire.parseDocument json |> Result.bind Controls.decode with
                  | Ok entry -> failtestf "expected a refusal, decoded %A" entry
                  | Error refusal -> refusal.Class

              Expect.equal
                  (refusalOf
                      "{\"$type\":\"Detonate\",\"actor\":{\"id\":\"ops\",\"kind\":\"operator\"},\"reason\":\"x\",\"sequence\":0}")
                  RefusalClass.UnknownEffectArm
                  "a fifth control is a decision, not a document"

              Expect.equal
                  (refusalOf
                      "{\"$type\":\"Resume\",\"actor\":{\"id\":\"ops\",\"kind\":\"operator\"},\"reason\":\"x\",\"sequence\":0,\"extra\":1}")
                  RefusalClass.UndeclaredMember
                  "an undeclared member is refused"

              Expect.equal
                  (refusalOf "{\"$type\":\"Resume\",\"actor\":{\"id\":\"ops\",\"kind\":\"operator\"},\"sequence\":0}")
                  RefusalClass.MissingMember
                  "a control with no reason is not a control"

              Expect.equal
                  (refusalOf
                      "{\"$type\":\"Revoke\",\"actor\":{\"id\":\"ops\",\"kind\":\"operator\"},\"performer\":\"\",\"reason\":\"x\",\"sequence\":0}")
                  RefusalClass.EmptyName
                  "a revocation must name a performer"

              Expect.equal
                  (refusalOf
                      "{\"$type\":\"Suspend\",\"actor\":{\"id\":\"ops\",\"kind\":\"robot\"},\"reason\":\"x\",\"sequence\":0}")
                  RefusalClass.UnknownEffectArm
                  "and an actor is a person or a machine, with no third reading"
          } ]

// ─── the op performer is revocable (Phase 1983, D26) ─────────────────────────
//
// D23 recorded the finding these close: a revoke named a performer by its
// registry key, the op performer had none, and so an operator who withdrew
// every host performer still had ops performed. Each claim is asserted with a
// COUNT of the op performer's own invocations, because "no op was performed"
// is the property, and an outcome that merely reports a refusal could still
// have performed one first.

/// A counting op performer — the op-side twin of `Counter`.
type private OpCounter() =
    let mutable count = 0
    member _.Count = count

    member _.Performance: OpPerformance<Node<obj>, TreeOp<obj>> =
        OpPerformance.performedWithoutReceipt (fun _ _ ->
            count <- count + 1
            Ok())

let private removeReadout: TreeOp<obj> = TreeOp.RemoveNode(NodeId "readout")
let private removeCall: TreeOp<obj> = TreeOp.RemoveNode(NodeId "call")

/// Two edits, then a host call: under a registered op performer the staged
/// list is op, op, host call — ordinals 0, 1, 2.
let private editsThenAudit: Handler =
    { Name = "work"
      Stages =
        [ Effect(ServerEffect.ApplyOps [ removeReadout; removeCall ])
          Effect(ServerEffect.HostCall("audit", jstr "x", None)) ] }

/// A host call, then an edit: the host call takes ordinal 0, the op stage 1.
let private auditThenEdit: Handler =
    { Name = "work"
      Stages =
        [ Effect(ServerEffect.HostCall("audit", jstr "x", None))
          Effect(ServerEffect.ApplyOps [ removeReadout ]) ] }

/// Edits only — no host call to be denied while planning.
let private editsOnly: Handler =
    { Name = "work"
      Stages = [ Effect(ServerEffect.ApplyOps [ removeReadout ]) ] }

let private revokeOps (controls: ControlServices) (reason: string) =
    DurableControls.record controls (Controls.revoke ops reason OpPerformance.RegistrationKey)
    |> ignore

let private runControlled
    (journal: EffectJournal)
    (controls: ControlServices)
    (registry: ServerEffectRegistry)
    (performance: OpPerformance<Node<obj>, TreeOp<obj>>)
    (handler: Handler)
    =
    Fuaran.Program.Server.DurableControls.runWith
        UiWitness.witness
        (durableWith journal)
        controls
        "inv-0"
        registry
        performance
        Fuaran.Compute.DataFrame.noResolve
        "call"
        handler
        store

/// The DIRECT interpreter with the controls in force: the registry through
/// `Controls.apply`, the op performance through `Controls.performance`.
let private runDirect
    (controls: ControlServices)
    (registry: ServerEffectRegistry)
    (performance: OpPerformance<Node<obj>, TreeOp<obj>>)
    (handler: Handler)
    =
    let state = DurableControls.stateOf controls
    let refusals = ResizeArray<ControlRefusal>()

    let outcome =
        Fuaran.Program.Server.Handler.runWith
            UiWitness.witness
            (Controls.apply refusals.Add state registry)
            (Controls.performance refusals.Add state performance)
            Fuaran.Compute.DataFrame.noResolve
            "call"
            handler
            store

    outcome, List.ofSeq refusals

let private revokedOpStage =
    ServerDiagnostic.PerformFailed("ApplyOps", ControlCode.PerformerRevoked)

/// The non-invocation steps of one invocation's journal.
let private stepsOf (journal: EffectJournal) =
    journal.Read "inv-0" |> List.filter (fun e -> e.Step <> Journal.InvocationStep)

/// A journal the process dies in front of: the ATTEMPT record for `step`
/// kills the process before it lands — the clean between-steps interruption.
let private dyingBefore (step: int) (inner: EffectJournal) : EffectJournal =
    { inner with
        Append =
            fun entry ->
                match entry.Phase with
                | JournalPhase.Attempted when entry.Step = step -> raise (ProcessDied "between steps")
                | _ -> inner.Append entry }

/// The demanded document for the one endpoint a test session exposes.
let private coverageProjection (services: ServerServices) =
    ServerDemanded.ofTreeAndHandlers services.Handlers (treeNode (Action.Call(endpoint, None, None)))

/// A host whose only declaration is its server tier.
let private serverHost (coverage: ServerCoverage) =
    HostCoverage.nothing |> HostCoverage.withServer coverage

[<Tests>]
let opPerformerRevocation =
    testList
        "Phase 1983 — the op performer is revocable"
        [

          test "the registration key is the capability the op stage is gated and journaled under" {
              Expect.equal
                  OpPerformance.RegistrationKey
                  (ServerEffect.capability (ServerEffect.ApplyOps([]: TreeOp<obj> list)))
                  "one name for the arm: what a revoke names is what a throttle names"

              Expect.equal
                  OpPerformance.RegistrationKey
                  Durable.OpStageCapability
                  "and what a performed op stage is journaled under"
          }

          test "revoking the op performer, every host performer live, refuses the FIRST op stage and performs no op" {
              let audit = Counter()
              let opsRun = OpCounter()
              let effects = Journal.inMemory ()
              let controls = controlsOn (Controls.inMemory ())

              revokeOps controls "ops reach the world; withdrawn"

              let outcome =
                  runControlled effects controls (registryOf audit.Performer) opsRun.Performance editsThenAudit

              Expect.equal opsRun.Count 0 "the op performer was never invoked"
              Expect.equal audit.Count 0 "and the host call staged after the refused stage was never reached"
              Expect.isFalse outcome.Durable.Outcome.Committed "the handler did not commit"

              Expect.equal
                  outcome.Durable.Outcome.Diagnostics
                  [ revokedOpStage ]
                  "refused at the op stage, under ApplyOps, naming the control's code"

              Expect.isEmpty outcome.Durable.Outcome.Performed "nothing is reported performed"

              Expect.isEmpty
                  (stepsOf effects)
                  "and the effect journal records no step: a refused stage was never attempted"

              match outcome.Refusals with
              | [ ControlRefusal.Revoked(capability, actor, reason) ] ->
                  Expect.equal capability "ApplyOps" "the capability the op stage is gated under"
                  Expect.equal (ControlActor.tag actor) "operator" "withdrawn by the operator"
                  Expect.equal reason "ops reach the world; withdrawn" "with the raiser's reason"
              | other -> failtestf "expected one revocation refusal, got %A" other
          }

          test "the same, in the DIRECT interpreter" {
              let audit = Counter()
              let opsRun = OpCounter()
              let controls = controlsOn (Controls.inMemory ())

              revokeOps controls "withdrawn"

              let outcome, refusals =
                  runDirect controls (registryOf audit.Performer) opsRun.Performance editsThenAudit

              Expect.equal opsRun.Count 0 "the op performer was never invoked"
              Expect.equal audit.Count 0 "nor the host call after it"
              Expect.isFalse outcome.Committed "the handler did not commit"
              Expect.equal outcome.Diagnostics [ revokedOpStage ] "refused at the op stage"

              match refusals with
              | [ ControlRefusal.Revoked("ApplyOps", _, "withdrawn") ] -> ()
              | other -> failtestf "expected one revocation refusal under ApplyOps, got %A" other
          }

          test "the refusal lands at the op stage's ORDINAL: a host call staged before it performs, and is reported" {
              let audit = Counter()
              let opsRun = OpCounter()
              let effects = Journal.inMemory ()
              let controls = controlsOn (Controls.inMemory ())

              revokeOps controls "withdrawn"

              let outcome =
                  runControlled effects controls (registryOf audit.Performer) opsRun.Performance auditThenEdit

              Expect.equal audit.Count 1 "the live host performer at ordinal 0 ran"
              Expect.equal opsRun.Count 0 "the withdrawn op performer at ordinal 1 did not"

              Expect.equal
                  outcome.Durable.Outcome.Performed
                  [ "host:audit" ]
                  "the prefix that ran is reported — D8's residual, unchanged in shape"

              Expect.equal outcome.Durable.Outcome.Diagnostics [ revokedOpStage ] "and the op stage refused"
              Expect.equal outcome.Durable.Invoked [ 0 ] "only ordinal 0 was invoked"

              Expect.equal
                  (stepsOf effects |> List.map _.Step |> List.distinct)
                  [ 0 ]
                  "the journal holds the host call's step and nothing at the refused ordinal"
          }

          test "revoking a HOST performer leaves op stages unaffected" {
              let audit = Counter()
              let opsRun = OpCounter()
              let controls = controlsOn (Controls.inMemory ())

              DurableControls.record controls (Controls.revoke ops "withdrawn" "audit")
              |> ignore

              let outcome =
                  runControlled (Journal.inMemory ()) controls (registryOf audit.Performer) opsRun.Performance editsOnly

              Expect.equal opsRun.Count 1 "the op performer ran"
              Expect.isTrue outcome.Durable.Outcome.Committed "and the handler committed"
              Expect.isEmpty outcome.Refusals "no control refused anything"

              let direct, refusals =
                  runDirect controls (registryOf audit.Performer) opsRun.Performance editsOnly

              Expect.equal opsRun.Count 2 "the direct interpreter performs it too"
              Expect.isTrue direct.Committed "and commits"
              Expect.isEmpty refusals "with nothing refused"
          }

          test "the refusal is IDENTICAL in shape to a revoked host call's" {
              let hostRevoked =
                  let controls = controlsOn (Controls.inMemory ())

                  DurableControls.record controls (Controls.revoke ops "same reason" "audit")
                  |> ignore

                  let outcome =
                      runControlled
                          (Journal.inMemory ())
                          controls
                          (registryOf (Counter()).Performer)
                          (OpCounter()).Performance
                          (auditing "done")

                  outcome.Refusals

              let opRevoked =
                  let controls = controlsOn (Controls.inMemory ())
                  revokeOps controls "same reason"

                  let outcome =
                      runControlled
                          (Journal.inMemory ())
                          controls
                          (registryOf (Counter()).Performer)
                          (OpCounter()).Performance
                          editsOnly

                  outcome.Refusals

              let erase (refusal: ControlRefusal) =
                  match refusal with
                  | ControlRefusal.Revoked(_, actor, reason) -> Some(actor, reason)
                  | _ -> None

              Expect.equal (List.length hostRevoked) 1 "one refusal for the host call"
              Expect.equal (List.length opRevoked) 1 "one for the op stage"

              Expect.equal
                  (opRevoked |> List.map erase)
                  (hostRevoked |> List.map erase)
                  "the same arm, the same actor, the same reason — only the capability differs"

              Expect.equal
                  (opRevoked |> List.map Controls.describeRefusal)
                  [ ControlCode.PerformerRevoked
                    + ": 'ApplyOps' refused — its performer was withdrawn by operator 'ops' (same reason)" ]
                  "rendered in the one vocabulary"

              Expect.equal
                  (hostRevoked |> List.map Controls.describeRefusal)
                  [ ControlCode.PerformerRevoked
                    + ": 'host:audit' refused — its performer was withdrawn by operator 'ops' (same reason)" ]
                  "as a host call's is"
          }

          test "a resumed run SERVES the journalled prefix and refuses the first unperformed op stage" {
              let audit = Counter()
              let opsRun = OpCounter()
              let effects = Journal.inMemory ()
              let controls = controlsOn (Controls.inMemory ())
              let registry = registryOf audit.Performer

              // The process dies in front of ordinal 1: the first op stage
              // performed and recorded, the second never attempted.
              Expect.throws
                  (fun () ->
                      runControlled (dyingBefore 1 effects) controls registry opsRun.Performance editsThenAudit
                      |> ignore)
                  "the first run dies between steps"

              Expect.equal opsRun.Count 1 "one op reached the world before the crash"

              revokeOps controls "withdrawn while the session was down"

              let resumed =
                  runControlled effects controls registry opsRun.Performance editsThenAudit

              Expect.equal resumed.Durable.Replayed [ 0 ] "ordinal 0 was SERVED from the journal"
              Expect.isEmpty resumed.Durable.Invoked "nothing was invoked"
              Expect.equal opsRun.Count 1 "the op performer was not reached again"
              Expect.equal audit.Count 0 "nor the host call after the refused stage"

              Expect.equal
                  resumed.Durable.Outcome.Performed
                  [ "ApplyOps" ]
                  "the served op is reported as performed — it was"

              Expect.equal resumed.Durable.Outcome.Diagnostics [ revokedOpStage ] "and ordinal 1 refused"

              match resumed.Refusals with
              | [ ControlRefusal.Revoked("ApplyOps", _, "withdrawn while the session was down") ] -> ()
              | other -> failtestf "expected one revocation refusal under ApplyOps, got %A" other
          }

          test "revocation survives a resume, and a RESUME does not lift it" {
              let opsRun = OpCounter()
              let controls = controlsOn (Controls.inMemory ())
              let registry = registryOf (Counter()).Performer

              revokeOps controls "withdrawn"
              DurableControls.record controls (Controls.suspend ops "halt") |> ignore
              DurableControls.record controls (Controls.resume ops "reviewed") |> ignore

              let state = DurableControls.stateOf controls

              Expect.isFalse (Controls.isSuspended state) "the suspend is lifted"

              Expect.equal
                  (Controls.opPerformerRevocation state |> Option.map snd)
                  (Some "withdrawn")
                  "the op performer's withdrawal stands"

              // Two runs of the same session: each re-decides from the control
              // stream, rather than serving a refusal frozen into a journal.
              for journal in [ Journal.inMemory (); Journal.inMemory () ] do
                  let outcome = runControlled journal controls registry opsRun.Performance editsOnly

                  Expect.equal outcome.Durable.Outcome.Diagnostics [ revokedOpStage ] "still refused"
                  Expect.equal (List.length outcome.Refusals) 1 "and recorded on every run"

              Expect.equal opsRun.Count 0 "the op performer was never invoked"
          }

          test "under IN-MEMORY performance a revoke of the key withdraws nothing — nothing reaches outside" {
              let controls = controlsOn (Controls.inMemory ())
              revokeOps controls "withdrawn"

              let inMemory: OpPerformance<Node<obj>, TreeOp<obj>> = OpPerformance.InMemory

              let outcome =
                  runControlled (Journal.inMemory ()) controls (registryOf (Counter()).Performer) inMemory editsOnly

              Expect.isTrue outcome.Durable.Outcome.Committed "the apply is the effect, and it ran"
              Expect.isEmpty outcome.Refusals "nothing refused"
              Expect.equal outcome.Durable.Outcome.Performed [ "ApplyOps" ] "performed in the plan phase, as always"
          }

          test "a session stepped with the op performer revoked refuses the op stage (the arm path)" {
              let opsRun = OpCounter()

              let services =
                  { servicesOf (registryOf (Counter()).Performer) editsOnly with
                      OpPerformance = opsRun.Performance }

              let controls = controlsOn (Controls.inMemory ())
              revokeOps controls "withdrawn"

              let stepped =
                  DurableControls.step
                      (durableWith (Journal.inMemory ()))
                      controls
                      "inv-0"
                      (ServerSession.init services empty callWire)
                      (clickEv 0)

              Expect.equal opsRun.Count 0 "the op performer was never invoked"

              match stepped.Refusals with
              | [ ControlRefusal.Revoked("ApplyOps", _, "withdrawn") ] -> ()
              | other -> failtestf "expected one revocation refusal under ApplyOps, got %A" other
          }

          test "a deployment that revokes NOTHING runs a performing handler byte-identically" {
              let project (outcome: HandlerOutcome) =
                  {| Tree = CanonicalJson.encodeNode outcome.Store.Tree
                     Committed = outcome.Committed
                     Performed = outcome.Performed
                     Diagnostics = outcome.Diagnostics |> List.map (sprintf "%A") |}

              let uncontrolledOps = OpCounter()
              let controlledOps = OpCounter()
              let uncontrolledJournal = Journal.inMemory ()
              let controlledJournal = Journal.inMemory ()

              let uncontrolled =
                  Fuaran.Program.Server.Durable.runWith
                      UiWitness.witness
                      (durableWith uncontrolledJournal)
                      "inv-0"
                      (registryOf (Counter()).Performer)
                      uncontrolledOps.Performance
                      Fuaran.Compute.DataFrame.noResolve
                      "call"
                      editsThenAudit
                      store

              let controlled =
                  runControlled
                      controlledJournal
                      (ControlServices.create scope)
                      (registryOf (Counter()).Performer)
                      controlledOps.Performance
                      editsThenAudit

              Expect.isTrue uncontrolled.Outcome.Committed "the probe performs: two ops and a host call"
              Expect.equal (project controlled.Durable.Outcome) (project uncontrolled.Outcome) "the same outcome"
              Expect.equal controlled.Durable.Invoked uncontrolled.Invoked "the same steps invoked"

              Expect.equal
                  (controlledJournal.Read "inv-0")
                  (uncontrolledJournal.Read "inv-0")
                  "the same journal, entry for entry"

              Expect.equal controlledOps.Count uncontrolledOps.Count "the op performer ran as often"
              Expect.isEmpty controlled.Refusals "nothing refused"

              let direct =
                  Fuaran.Program.Server.Handler.runWith
                      UiWitness.witness
                      (registryOf (Counter()).Performer)
                      (OpCounter()).Performance
                      Fuaran.Compute.DataFrame.noResolve
                      "call"
                      editsThenAudit
                      store

              let directControlled, refusals =
                  runDirect
                      (ControlServices.create scope)
                      (registryOf (Counter()).Performer)
                      (OpCounter()).Performance
                      editsThenAudit

              Expect.equal (project directControlled) (project direct) "and the direct interpreter likewise"
              Expect.isEmpty refusals "with nothing refused"
          } ]

// ─── Phase 1986: coverage reads the op performer ─────────────────────────────
//
// The revoke above WORKS; until 1986 the report an operator reads to see what a
// placement still covers said it had not, because coverage was read off the
// registry and the op performer is not a member of it.

[<Tests>]
let opPerformerCoverage =
    testList
        "Phase 1986 — control coverage reads the op performer"
        [

          test "COVERAGE reports a revoked op performer: ApplyOps is withdrawn, and the controls name who and why" {
              let opsRun = OpCounter()
              let controls = controlsOn (Controls.inMemory ())

              let services =
                  { servicesOf (registryOf (Counter()).Performer) editsOnly with
                      OpPerformance = opsRun.Performance }

              let projection = coverageProjection services

              Expect.isEmpty
                  (Demanded.checkProjection (serverHost (DurableControls.coverage controls services)) projection)
                  "before the withdrawal the placement covers the edit"

              revokeOps controls "ops reach the world; withdrawn"

              let coverage = DurableControls.coverage controls services

              Expect.equal
                  (Demanded.checkProjection (serverHost coverage) projection)
                  [ CoverageFinding.ServerGateRefusesCapability OpPerformance.RegistrationKey ]
                  "afterwards ApplyOps is withdrawn, under the key the revoke named"

              Expect.isFalse (coverage.Gate "ApplyOps") "the arm's one coverage fact is closed"
              Expect.isTrue (coverage.Gate "host:audit") "and nothing else is"

              Expect.equal
                  coverage.HostFunctions
                  (Set.ofList [ "audit" ])
                  "the host performers are untouched: the op performer was never one of them"

              // Coverage carries no actor; the control state the coverage was
              // read from does, and it is the same fold.
              match Controls.opPerformerRevocation (DurableControls.stateOf controls) with
              | Some(actor, reason) ->
                  Expect.equal (ControlActor.tag actor) "operator" "withdrawn by the operator"
                  Expect.equal reason "ops reach the world; withdrawn" "with the raiser's reason"
              | None -> failtest "the revocation behind the finding is named by the controls"

              Expect.equal opsRun.Count 0 "asking the question performs nothing"
          }

          test "COVERAGE: revoking a HOST performer leaves ApplyOps covered" {
              let controls = controlsOn (Controls.inMemory ())

              let services =
                  { servicesOf (registryOf (Counter()).Performer) editsThenAudit with
                      OpPerformance = (OpCounter()).Performance }

              DurableControls.record controls (Controls.revoke ops "withdrawn" "audit")
              |> ignore

              let coverage = DurableControls.coverage controls services

              Expect.equal
                  (Demanded.checkProjection (serverHost coverage) (coverageProjection services))
                  [ CoverageFinding.UnregisteredServerFunction "audit" ]
                  "the host function is absent, and ApplyOps is not named"

              Expect.isTrue (coverage.Gate "ApplyOps") "the op performer still stands"
          }

          test "COVERAGE with no op performer revoked is the registry's coverage, unchanged, whatever else is in force" {
              let capabilities =
                  [ "ApplyOps"
                    "EmitPatch"
                    "host:audit"
                    "host:ApplyOps"
                    "Notify"
                    "RunQuery"
                    "SetState" ]

              let registry = registryOf (Counter()).Performer

              // The pre-1986 formula, spelled out: the registry with the
              // controls applied, and nothing else.
              let registryOnly (state: ControlState) =
                  ServerDemanded.coverageOfRegistry (Controls.apply ignore state registry)

              let sameAs (expected: ServerCoverage) (actual: ServerCoverage) (label: string) =
                  Expect.equal actual.HostFunctions expected.HostFunctions $"{label}: the same host functions"
                  Expect.equal actual.Channels expected.Channels $"{label}: the same channel surface"

                  Expect.equal
                      (capabilities |> List.map actual.Gate)
                      (capabilities |> List.map expected.Gate)
                      $"{label}: the same gate, capability by capability"

              let stateAfter (requests: ControlRequest list) =
                  let controls = controlsOn (Controls.inMemory ())

                  for request in requests do
                      DurableControls.record controls request |> ignore

                  DurableControls.stateOf controls

              let states =
                  [ "nothing recorded", stateAfter []
                    "a host performer revoked", stateAfter [ Controls.revoke ops "withdrawn" "audit" ]
                    "suspended", stateAfter [ Controls.suspend ops "halt" ]
                    "throttled",
                    stateAfter
                        [ Controls.throttle
                              ops
                              "slow"
                              { Capability = "ApplyOps"
                                MaxPerInvocation = 2 } ] ]

              let inMemory: OpPerformance<Node<obj>, TreeOp<obj>> = OpPerformance.InMemory

              for label, state in states do
                  sameAs
                      (registryOnly state)
                      (Controls.coverage state registry (OpCounter()).Performance)
                      $"{label}, ops performed"

                  sameAs (registryOnly state) (Controls.coverage state registry inMemory) $"{label}, ops in memory"
          }

          test "COVERAGE in memory: a revoke of the key withdraws nothing, so ApplyOps stays covered" {
              let controls = controlsOn (Controls.inMemory ())

              let services =
                  { servicesOf (registryOf (Counter()).Performer) editsOnly with
                      OpPerformance = OpPerformance.InMemory }

              revokeOps controls "withdrawn"

              let coverage = DurableControls.coverage controls services

              Expect.isEmpty
                  (Demanded.checkProjection (serverHost coverage) (coverageProjection services))
                  "the apply IS the effect in memory, and nothing reaches outside to withdraw (D26 item 5)"

              Expect.isTrue (coverage.Gate "ApplyOps") "the arm is covered"
          }

          test "COVERAGE: a Resume does not restore a withdrawn op performer, on any prefix of the stream" {
              let services =
                  { servicesOf (registryOf (Counter()).Performer) editsOnly with
                      OpPerformance = (OpCounter()).Performance }

              let journal = Controls.inMemory ()
              let controls = controlsOn journal

              for request in
                  [ Controls.revoke ops "withdrawn" OpPerformance.RegistrationKey
                    Controls.suspend ops "halt"
                    Controls.resume ops "reviewed" ] do
                  DurableControls.record controls request |> ignore

              let entries = entriesOf journal
              Expect.equal (List.length entries) 3 "the stream holds the three acts"

              for prefix in prefixes entries |> List.filter (List.isEmpty >> not) do
                  let coverage =
                      Controls.coverage (Controls.fold prefix) services.Effects services.OpPerformance

                  Expect.isFalse
                      (coverage.Gate "ApplyOps")
                      $"ApplyOps stays withdrawn after {List.length prefix} control(s)"

              Expect.isTrue
                  ((DurableControls.coverage controls services).Gate "host:audit")
                  "the resume lifted the suspend, and only the suspend"
          } ]
