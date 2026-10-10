module Fuaran.UI.Program.Server.Tests.HandlerLoopTests

// ─── Handlers as data, and the loop that runs them ───────────────────
//
// Three claims are load-bearing here, and each is checked rather than asserted
// in prose:
//
//   the interpreter is IMPORTED — a `Compute` stage produces exactly what the
//   shared fold produces for the same action and the same store, field for
//   field, so a change to the fold changes this placement too and neither can
//   drift;
//
//   `ApplyOps` is the ONLY domain mutation — a handler doing everything else
//   leaves the tree reference-identical;
//
//   the HANDLER is the atomicity unit — a stage that fails after a stage that
//   succeeded leaves nothing behind but the diagnostics.

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops.Introspect
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.Renderer.BindingResolver
open Fuaran.Program.Bounded
open Fuaran.UI.Program
open Fuaran.Program.Server
open Fuaran.UI.Program.Server

// ─── fixtures ────────────────────────────────────────────────────────

let private jstr (s: string) = Fuaran.Core.JStr s

/// A three-row embedded table — enough for a pipeline to visibly narrow.
let private rows: Fuaran.Core.Table =
    { Schema = [ "n", Fuaran.Core.IntType ]
      Columns =
        [ { Name = "n"
            Type = Fuaran.Core.IntType
            Cells = [ Fuaran.Core.Int 1; Fuaran.Core.Int 2; Fuaran.Core.Int 3 ] } ] }

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

let private clickEv (nodeId: string) : LiveEvent =
    { ConnId = "server"
      NodeId = nodeId
      Event = "click"
      Payload = Map.empty
      LastSeq = 0 }

/// The resolved Markdown text at `id` — the observable "the store changed and
/// re-resolution made it visible" probe.
let private readout (tree: Node<obj>) : string option =
    match findNode (NodeId "readout") tree with
    | Some node ->
        match node.Kind with
        | NodeKind.Markdown({ Text = TextSource.Literal s }) -> Some s
        | _ -> None
    | None -> None

let private store: ServerStore =
    { Tree = treeNode (Action.Call("/handlers/x", None, None))
      Bindings = empty }

/// A registry with every capability permitted and one host function registered.
let private openRegistry (record: string list ref) =
    ServerEffectRegistry.denyAll
    |> ServerEffectRegistry.register "audit" (fun payload ->
        record.Value <- record.Value @ [ sprintf "%A" payload ]
        Ok(jstr "recorded"))
    |> ServerEffectRegistry.permissive

let private sources: string -> Result<Fuaran.Core.Table, Fuaran.Compute.EvalError> =
    Fuaran.Compute.DataFrame.noResolve

// ─── tests ───────────────────────────────────────────────────────────

[<Tests>]
let tests =
    testList
        "server handler"
        [ testList
              "the interpreter is imported, not forked"
              [ for name, action in
                    [ "SetState", Action.SetState("status", Some(jstr "written"), None)
                      "Chain",
                      Action.Chain
                          [ Action.SetState("status", Some(jstr "first"), None)
                            Action.SetState("status", Some(jstr "second"), None) ]
                      "Navigate", Action.Navigate(TextSource.Literal "/next", NavigateTarget.Self)
                      "refused Navigate", Action.Navigate(TextSource.Literal "javascript:alert(1)", NavigateTarget.Self)
                      "documented no-op", Action.Notify("channel", jstr "payload")
                      "host-reserved write",
                      Action.SetState(Fuaran.UI.Renderer.StateKeys.HostReservedPrefix + "x", Some(jstr "no"), None) ] ->
                    test name {
                        // The claim is not "it behaves similarly" but "it is the
                        // same fold": a single `Compute` stage must agree with a
                        // direct call on the store, the effects AND the
                        // diagnostics.
                        let direct = BoundedActions.runBoundedAction "call" action empty

                        let staged =
                            Handler.run
                                (openRegistry (ref []))
                                sources
                                "call"
                                { Name = "one-stage"
                                  Stages = [ Compute action ] }
                                store

                        Expect.isTrue staged.Committed "a Compute stage always commits — the fold is total"

                        Expect.equal
                            staged.Store.Bindings.State
                            direct.Store.State
                            "the store the stage produced is the store the shared fold produced"

                        Expect.equal
                            staged.ClientEffects
                            direct.Effects
                            "and the client effects are the fold's, unaltered"

                        Expect.equal
                            (staged.Diagnostics
                             |> List.map (fun d ->
                                 match d with
                                 | ServerDiagnostic.Bounded b -> BoundedDiagnostic.describe b
                                 | other -> sprintf "%A" other))
                            (direct.Diagnostics |> List.map BoundedDiagnostic.describe)
                            "and the diagnostics pass through unchanged"
                    } ]

          test "ApplyOps is the only thing that touches the domain tree" {
              let record = ref []

              let readsAndWritesEverythingElse =
                  { Name = "no-mutation"
                    Stages =
                      [ Effect(
                            ServerEffect.RunQuery(
                                "rows",
                                Fuaran.Core.Embedded rows,
                                [ Fuaran.Compute.Limit(Fuaran.Compute.Slot.Lit 2, Fuaran.Compute.Slot.Lit 0) ]
                            )
                        )
                        Compute(Action.SetState("status", Some(jstr "written"), None))
                        Effect(ServerEffect.HostCall("audit", jstr "note", Some "audited"))
                        Effect(ServerEffect.EmitPatch [ TreeOp.RemoveNode(NodeId "call") ])
                        Effect(ServerEffect.Notify("channel", jstr "note")) ] }

              let outcome =
                  Handler.run (openRegistry record) sources "call" readsAndWritesEverythingElse store

              Expect.isTrue outcome.Committed "the handler committed"

              Expect.isTrue
                  (LanguagePrimitives.PhysicalEquality outcome.Store.Tree store.Tree)
                  "a query, a state write, a host call, a patch and a notification leave the domain tree untouched"

              // …and the patch it emitted is emphatically NOT applied, which is
              // the distinction EmitPatch exists to carry.
              Expect.isSome (findNode (NodeId "call") outcome.Store.Tree) "an emitted patch is shipped, never applied"

              let mutating =
                  { Name = "mutation"
                    Stages = [ Effect(ServerEffect.ApplyOps [ TreeOp.RemoveNode(NodeId "call") ]) ] }

              let mutated = Handler.run (openRegistry record) sources "call" mutating store

              Expect.isTrue mutated.Committed "the mutating handler committed"
              Expect.isNone (findNode (NodeId "call") mutated.Store.Tree) "and ApplyOps did change the tree"
          }

          test "a committed handler reports the capabilities it performed, in order" {
              let record = ref []

              let handler =
                  { Name = "ordered"
                    Stages =
                      [ Effect(ServerEffect.RunQuery("rows", Fuaran.Core.Embedded rows, []))
                        Effect(ServerEffect.HostCall("audit", jstr "note", None))
                        Effect(ServerEffect.ApplyOps [])
                        Effect(ServerEffect.EmitPatch [])
                        Effect(ServerEffect.Notify("channel", jstr "note")) ] }

              let outcome = Handler.run (openRegistry record) sources "call" handler store

              Expect.equal
                  outcome.Performed
                  [ "RunQuery"; "ApplyOps"; "EmitPatch"; "Notify"; "host:audit" ]
                  "the audit trail is EXECUTION order, not stage order: two-phase staging (D8) defers the \
                   host call to the perform phase, so it reports last however early it was declared"
          }

          test "a UI program's findings land on its outcome in program order, kept when the handler rolls back" {
              // Program 0.9.0 (D45): `Report` is the sixth arm. A finding moves no
              // state at this witness either; it is carried on the outcome, in
              // program order, and on the audit trail beside every other arm.
              let finding code : Finding =
                  { Code = code
                    Severity = "info"
                    Message = "reported " + code }

              let reporting =
                  { Name = "reporting"
                    Stages =
                      [ Effect(ServerEffect.Report(finding "started"))
                        Effect(ServerEffect.ApplyOps [])
                        Effect(ServerEffect.Report(finding "done")) ] }

              let committed = Handler.run (openRegistry (ref [])) sources "call" reporting store

              Expect.isTrue committed.Committed "the handler committed"
              Expect.equal committed.Findings [ finding "started"; finding "done" ] "both findings, in program order"
              Expect.equal committed.Performed [ "Report"; "ApplyOps"; "Report" ] "each report is on the audit trail"

              // A finding reported before a host call the registry cannot serve
              // survives the rollback, as the diagnostics do.
              let refused =
                  { Name = "refused"
                    Stages =
                      [ Effect(ServerEffect.Report(finding "started"))
                        Effect(ServerEffect.HostCall("absent", jstr "note", None)) ] }

              let rolledBack = Handler.run (openRegistry (ref [])) sources "call" refused store

              Expect.isFalse rolledBack.Committed "the unserved host call rolls the handler back"
              Expect.equal rolledBack.Findings [ finding "started" ] "and the finding reported before it is kept"
          }

          test "a query lands its result in the session's query slot" {
              let handler =
                  { Name = "query"
                    Stages =
                      [ Effect(
                            ServerEffect.RunQuery(
                                "rows",
                                Fuaran.Core.Embedded rows,
                                [ Fuaran.Compute.Limit(Fuaran.Compute.Slot.Lit 2, Fuaran.Compute.Slot.Lit 0) ]
                            )
                        ) ] }

              let outcome = Handler.run (openRegistry (ref [])) sources "call" handler store

              match Map.tryFind "rows" outcome.Store.Bindings.QueryResults with
              | Some value ->
                  match value with
                  | :? Fuaran.Core.Table as table ->
                      Expect.equal
                          (table.Columns |> List.map (fun c -> List.length c.Cells))
                          [ 2 ]
                          "the pipeline ran — three rows in, the declared limit out"
                  | other -> failtestf "the query slot holds %A rather than a table" other
              | None -> failtest "the query slot was never written"
          }

          test "a host call's result lands in its declared slot, and never in the host's namespace" {
              let handler into =
                  { Name = "landing"
                    Stages = [ Effect(ServerEffect.HostCall("audit", jstr "note", Some into)) ] }

              let landed =
                  Handler.run (openRegistry (ref [])) sources "call" (handler "audited") store

              Expect.isTrue landed.Committed "a call with an ordinary landing slot commits"

              match Map.tryFind "audited" landed.Store.Bindings.State with
              | Some value -> Expect.equal (string value) "recorded" "and the performer's result is in the slot"
              | None -> failtest "the landing slot was never written"

              let record = ref []

              let reserved =
                  Handler.run
                      (openRegistry record)
                      sources
                      "call"
                      (handler (Fuaran.UI.Renderer.StateKeys.HostReservedPrefix + "x"))
                      store

              Expect.isFalse reserved.Committed "a landing slot in the host-reserved namespace is refused"

              Expect.isEmpty
                  reserved.Store.Bindings.State
                  "and the refusal leaves the store as it was — the same posture the shared fold takes"

              // Staging moved this refusal from after the call to before it. The
              // slot is DECLARED, so nothing about checking it ever needed a
              // result — and now a handler with a bad slot never reaches the
              // outside world at all (D8).
              Expect.isEmpty record.Value "and the performer never ran: the slot is checked while planning"
          }

          testList
              "the handler is the atomicity unit"
              [ test "a denied stage rolls back everything before it" {
                    let record = ref []

                    // The gate permits the built-in arms and refuses the host
                    // capability, so stage 1 succeeds and stage 2 is denied.
                    let partialGate =
                        openRegistry record
                        |> ServerEffectRegistry.withGate (fun capability -> not (capability.StartsWith "host:"))

                    let denials = ResizeArray<ServerEffectDenial>()
                    let registry = partialGate |> ServerEffectRegistry.onDenied denials.Add

                    let handler =
                        { Name = "halts"
                          Stages =
                            [ Effect(ServerEffect.ApplyOps [ TreeOp.RemoveNode(NodeId "call") ])
                              Compute(Action.SetState("status", Some(jstr "written"), None))
                              Effect(ServerEffect.HostCall("audit", jstr "note", None))
                              Effect(ServerEffect.Notify("channel", jstr "never")) ] }

                    let outcome = Handler.run registry sources "call" handler store

                    Expect.isFalse outcome.Committed "the handler did not commit"

                    Expect.isTrue
                        (LanguagePrimitives.PhysicalEquality outcome.Store.Tree store.Tree)
                        "the op applied by stage 1 was rolled back"

                    Expect.isEmpty outcome.Store.Bindings.State "and so was the state written by stage 2"
                    Expect.isEmpty outcome.Performed "nothing is reported as performed"
                    Expect.isEmpty outcome.Notifications "and the stage after the denial never ran"

                    Expect.isNonEmpty
                        outcome.Diagnostics
                        "but the diagnostics survive — they are the whole record of why nothing happened"

                    Expect.sequenceEqual
                        (List.ofSeq denials)
                        [ ServerEffectDenial.GateRefused "host:audit" ]
                        "and the denial reached the sink exactly once"
                }

                test "a failing effect rolls back, and reports a discriminator rather than a message" {
                    let handler =
                        { Name = "fails"
                          Stages =
                            [ Compute(Action.SetState("status", Some(jstr "written"), None))
                              // Addressing a node that is not there: the apply
                              // engine refuses, and the handler unwinds.
                              Effect(ServerEffect.ApplyOps [ TreeOp.RemoveNode(NodeId "absent") ]) ] }

                    let outcome = Handler.run (openRegistry (ref [])) sources "call" handler store

                    Expect.isFalse outcome.Committed "the handler did not commit"
                    Expect.isEmpty outcome.Store.Bindings.State "the earlier state write was rolled back"

                    match
                        outcome.Diagnostics
                        |> List.filter (fun d ->
                            match d with
                            | ServerDiagnostic.Failed _ -> true
                            | _ -> false)
                    with
                    | [ ServerDiagnostic.Failed(capability, reason) ] ->
                        Expect.equal capability "ApplyOps" "the failure names the capability"
                        Expect.equal reason "NodeNotFound" "and the error's discriminator, not its message"
                    | other -> failtestf "expected exactly one failure diagnostic, got %A" other
                }

                test "a stage that fails AFTER a host call still leaves the host call unrun" {
                    // The decision, at its sharpest (D8). Before staging, the
                    // performer had run by the time the later stage failed and
                    // no rollback reached it. Now the whole plan phase completes
                    // or nothing external happens, so the recorder is the probe:
                    // it stays empty.
                    let record = ref []

                    let handler =
                        { Name = "fails-after-calling-out"
                          Stages =
                            [ Effect(ServerEffect.HostCall("audit", jstr "note", Some "audited"))
                              Compute(Action.SetState("status", Some(jstr "written"), None))
                              // Addressing a node that is not there: the apply
                              // engine refuses while PLANNING.
                              Effect(ServerEffect.ApplyOps [ TreeOp.RemoveNode(NodeId "absent") ]) ] }

                    let outcome = Handler.run (openRegistry record) sources "call" handler store

                    Expect.isFalse outcome.Committed "the handler did not commit"
                    Expect.isEmpty record.Value "and the host performer never ran — the plan failed first"
                    Expect.isEmpty outcome.Performed "so nothing at all is reported as performed"
                    Expect.isEmpty outcome.Store.Bindings.State "and the landing slot was never written"
                }

                test "a performer that fails in the perform phase reports what already ran" {
                    // The residual staging does NOT abolish, reported rather
                    // than absorbed: the first call ran, the second refused, and
                    // an uncommitted outcome names the one that happened. This
                    // is the only case where `Performed` is non-empty on a
                    // rollback, which is what makes it readable as a signal.
                    let record = ref []

                    let registry =
                        ServerEffectRegistry.denyAll
                        |> ServerEffectRegistry.register "first" (fun _ ->
                            record.Value <- record.Value @ [ "first" ]
                            Ok(jstr "ok"))
                        |> ServerEffectRegistry.register "second" (fun _ ->
                            record.Value <- record.Value @ [ "second" ]
                            Error "the downstream refused it")
                        |> ServerEffectRegistry.register "third" (fun _ ->
                            record.Value <- record.Value @ [ "third" ]
                            Ok(jstr "ok"))
                        |> ServerEffectRegistry.permissive

                    let handler =
                        { Name = "half-performs"
                          Stages =
                            [ Effect(ServerEffect.HostCall("first", jstr "a", Some "landed"))
                              Effect(ServerEffect.HostCall("second", jstr "b", None))
                              Effect(ServerEffect.HostCall("third", jstr "c", None)) ] }

                    let outcome = Handler.run registry sources "call" handler store

                    Expect.isFalse outcome.Committed "the handler did not commit"

                    Expect.equal
                        record.Value
                        [ "first"; "second" ]
                        "the perform phase stopped at the first failure — the third was never reached"

                    Expect.equal
                        outcome.Performed
                        [ "host:first" ]
                        "and the outcome names exactly the call that ran and cannot be taken back"

                    Expect.isEmpty outcome.Store.Bindings.State "the landing slot rolled back with everything else"

                    match
                        outcome.Diagnostics
                        |> List.filter (fun d ->
                            match d with
                            | ServerDiagnostic.PerformFailed _ -> true
                            | _ -> false)
                    with
                    | [ ServerDiagnostic.PerformFailed(capability, reason) ] ->
                        Expect.equal capability "host:second" "the failure names the capability"

                        Expect.equal
                            reason
                            "the downstream refused it"
                            "and the performer's own text, which is the host's and so safe verbatim"
                    | other -> failtestf "expected exactly one perform-phase failure, got %A" other
                }

                test "a later stage cannot read an earlier host call's result — the stated cost" {
                    // Recorded as a test rather than only as prose, because it is
                    // the price D8 paid and a future change that silently bought
                    // it back should have to delete an assertion saying so.
                    let handler =
                        { Name = "reads-too-early"
                          Stages =
                            [ Effect(ServerEffect.HostCall("audit", jstr "note", Some "audited"))
                              Compute(
                                  Action.SetState(
                                      "echo",
                                      None,
                                      Some(Binding.State("audited", Some(jstr "unresolved-at-plan-time")))
                                  )
                              ) ] }

                    let outcome = Handler.run (openRegistry (ref [])) sources "call" handler store

                    Expect.isTrue outcome.Committed "the handler commits — this is a cost, not a failure"

                    Expect.equal
                        (Map.tryFind "echo" outcome.Store.Bindings.State |> Option.map string)
                        (Some "unresolved-at-plan-time")
                        "the compute stage saw the slot's default, because at plan time there is no result yet"

                    Expect.equal
                        (Map.tryFind "audited" outcome.Store.Bindings.State |> Option.map string)
                        (Some "recorded")
                        "and the result did land — just after every stage had already run"
                }

                test "an unresolvable source fails the query without a partial write" {
                    let handler =
                        { Name = "unresolvable"
                          Stages =
                            [ Effect(ServerEffect.RunQuery("rows", Fuaran.Core.Ref "elsewhere", []))
                              Effect(ServerEffect.Notify("channel", jstr "never")) ] }

                    let outcome = Handler.run (openRegistry (ref [])) sources "call" handler store

                    Expect.isFalse outcome.Committed "an unresolved source halts the handler"
                    Expect.isEmpty outcome.Store.Bindings.QueryResults "and writes no query slot"
                } ]

          test "the gate is consulted before the performer is looked up" {
              // The ordering claim, made observable: a performer that records
              // being called, behind a gate that refuses. If the lookup ran
              // first the recorder would still be untouched — so the stronger
              // check is that NO host function ran at all, which a refused
              // registered performer demonstrates.
              let record = ref []

              let registry = openRegistry record |> ServerEffectRegistry.withGate (fun _ -> false)

              let outcome =
                  Handler.run
                      registry
                      sources
                      "call"
                      { Name = "gated"
                        Stages = [ Effect(ServerEffect.HostCall("audit", jstr "note", None)) ] }
                      store

              Expect.isFalse outcome.Committed "the refused call halted the handler"
              Expect.isEmpty record.Value "and the registered performer never ran"
          }

          testList
              "the loop"
              [ test "a call naming a registered handler runs it" {
                    let handler =
                        { Name = "refresh"
                          Stages =
                            [ Effect(ServerEffect.RunQuery("rows", Fuaran.Core.Embedded rows, []))
                              Compute(Action.SetState("status", Some(jstr "refreshed"), None)) ] }

                    let services =
                        ServerServices.createPermissive
                        |> ServerServices.withHandler "/handlers/refresh" handler

                    let session =
                        ServerSession.init services empty (wire (Action.Call("/handlers/refresh", None, None)))

                    let next, out = ServerSession.step session (clickEv "call")

                    Expect.isNone out.Rejected "the event passed both gates"
                    Expect.isTrue out.Committed "and the handler committed"
                    Expect.equal (readout next.Resolved) (Some "refreshed") "re-resolution made the state write visible"
                    Expect.isNonEmpty out.Ops "and the diff produced the response ops"
                }

                test "a call naming no handler is inert, diagnosed, and does not echo the endpoint" {
                    let session =
                        ServerSession.init
                            ServerServices.createPermissive
                            empty
                            (wire (Action.Call("/handlers/absent-and-secret", None, None)))

                    let next, out = ServerSession.step session (clickEv "call")

                    Expect.isNone out.Rejected "an absent handler is not a rejection"
                    Expect.isEmpty out.Ops "and changes nothing"

                    Expect.equal
                        out.Diagnostics
                        [ ServerDiagnostic.HandlerUnregistered ]
                        "but it is diagnosed rather than silent"

                    Expect.isFalse
                        ((sprintf "%A" out.Diagnostics).Contains "absent-and-secret")
                        "and the wire-supplied endpoint is never repeated back"

                    Expect.equal (readout next.Resolved) (Some "init") "the session is unchanged"
                }

                test "a call NESTED in a chain runs the handler in its place" {
                    // D7. The spike recognised a call only at the top level, so
                    // this shape reached nothing at all. Now the fold recognises
                    // it where it sits: the write before it is overwritten by
                    // the handler's own, and the write after it survives.
                    let handler =
                        { Name = "refresh"
                          Stages = [ Compute(Action.SetState("status", Some(jstr "from the handler"), None)) ] }

                    let services =
                        ServerServices.createPermissive
                        |> ServerServices.withHandler "/handlers/refresh" handler

                    let nested =
                        Action.Chain
                            [ Action.SetState("status", Some(jstr "before"), None)
                              Action.Call("/handlers/refresh", None, None)
                              Action.SetState("trailing", Some(jstr "after"), None) ]

                    let session = ServerSession.init services empty (wire nested)
                    let next, out = ServerSession.step session (clickEv "call")

                    Expect.isTrue out.Committed "the nested handler committed"

                    Expect.equal
                        (readout next.Resolved)
                        (Some "from the handler")
                        "the handler ran after the chain's first write and overwrote it"

                    Expect.equal
                        (Map.tryFind "trailing" next.Store.State |> Option.map string)
                        (Some "after")
                        "and before the chain's last write, which still landed"
                }

                test "a nested call naming no handler is diagnosed without disturbing the chain" {
                    let nested =
                        Action.Chain
                            [ Action.SetState("status", Some(jstr "before"), None)
                              Action.Call("/handlers/absent", None, None)
                              Action.SetState("trailing", Some(jstr "after"), None) ]

                    let session = ServerSession.init ServerServices.createPermissive empty (wire nested)
                    let next, out = ServerSession.step session (clickEv "call")

                    Expect.equal
                        out.Diagnostics
                        [ ServerDiagnostic.HandlerUnregistered ]
                        "the arm answers from inside a chain exactly as it does from the top"

                    Expect.equal (readout next.Resolved) (Some "before") "and folds the chain around it untouched"
                }

                test "a call that declares its own result target is refused, and reaches no handler" {
                    // D9. The handler declares where its results land; a tree
                    // that declares one too is refused rather than quietly
                    // ignored, so the retired mechanism says so out loud.
                    let record = ref []

                    let handler =
                        { Name = "never-runs"
                          Stages =
                            [ Effect(ServerEffect.HostCall("audit", jstr "note", None))
                              Compute(Action.SetState("status", Some(jstr "ran"), None)) ] }

                    let services =
                        { ServerServices.createPermissive with
                            Effects = openRegistry record }
                        |> ServerServices.withHandler "/handlers/refresh" handler

                    let declaring =
                        Action.Call("/handlers/refresh", None, Some(CallResultTarget.State "somewhere"))

                    let session = ServerSession.init services empty (wire declaring)
                    let next, out = ServerSession.step session (clickEv "call")

                    Expect.isEmpty out.Performed "the handler was never reached"
                    Expect.isEmpty record.Value "and no host performer ran"
                    Expect.equal (readout next.Resolved) (Some "init") "the session is unchanged"

                    match out.Diagnostics with
                    | [ ServerDiagnostic.Bounded(BoundedDiagnostic.Refused(_, _, reason)) ] ->
                        Expect.isFalse
                            (reason.Contains "somewhere")
                            "the refusal does not echo the wire-supplied target"

                        Expect.isFalse
                            (reason.Contains "/handlers/refresh")
                            "nor the wire-supplied endpoint — both come off the wire"
                    | other -> failtestf "expected one Refused diagnostic from the shared fold, got %A" other
                }

                test "an action that is not a call takes the shared fold, exactly as elsewhere" {
                    let action = Action.SetState("status", Some(jstr "direct"), None)
                    let session = ServerSession.init ServerServices.createPermissive empty (wire action)
                    let next, out = ServerSession.step session (clickEv "call")

                    Expect.isTrue out.Committed "the shared-fold path always commits"
                    Expect.isEmpty out.Performed "no server capability was involved"
                    Expect.equal (readout next.Resolved) (Some "direct") "and the fold's write is visible"
                }

                test "the dispatch gate defaults to deny" {
                    let session =
                        ServerSession.init
                            ServerServices.create
                            empty
                            (wire (Action.SetState("status", Some(jstr "nope"), None)))

                    let next, out = ServerSession.step session (clickEv "call")

                    match out.Rejected with
                    | Some(Gate _) -> ()
                    | other -> failtestf "expected a gate rejection from the default services, got %A" other

                    Expect.equal (readout next.Resolved) (Some "init") "and nothing changed"
                }

                test "the action budget refuses an oversized cascade" {
                    let cascade =
                        Action.Chain [ for i in 1..8 -> Action.SetState("status", Some(jstr (string i)), None) ]

                    let services =
                        { ServerServices.createPermissive with
                            Budget =
                                { InteractionBudget.defaults with
                                    MaxActions = 4 } }

                    let session = ServerSession.init services empty (wire cascade)
                    let next, out = ServerSession.step session (clickEv "call")

                    match out.Rejected with
                    | Some(BudgetExceeded _) -> ()
                    | other -> failtestf "expected a budget rejection, got %A" other

                    Expect.equal (readout next.Resolved) (Some "init") "and the store is untouched"
                }

                test "a handler that edits the tree is re-priced for the next interaction" {
                    // The tree is no longer fixed at this placement, so the
                    // cached cost has to move with it. A handler that shrinks
                    // the tree must leave a smaller cached cost behind.
                    let handler =
                        { Name = "shrink"
                          Stages = [ Effect(ServerEffect.ApplyOps [ TreeOp.RemoveNode(NodeId "readout") ]) ] }

                    let services =
                        ServerServices.createPermissive
                        |> ServerServices.withHandler "/handlers/shrink" handler

                    let session =
                        ServerSession.init services empty (wire (Action.Call("/handlers/shrink", None, None)))

                    let next, out = ServerSession.step session (clickEv "call")

                    Expect.isTrue out.Committed "the handler committed"
                    Expect.isLessThan next.NodeCount session.NodeCount "and the cached cost followed the tree"
                } ]

          test "the op sink sees the handler's response once, not once per stage" {
              // Journaling per stage would make a rolled-back handler
              // indistinguishable from a committed one in the stream, which is
              // the property replay depends on.
              let applied = ResizeArray<TreeOp<obj> list>()

              let handler =
                  { Name = "several"
                    Stages =
                      [ Compute(Action.SetState("status", Some(jstr "one"), None))
                        Compute(Action.SetState("status", Some(jstr "two"), None))
                        Effect(ServerEffect.Notify("channel", jstr "note")) ] }

              let services =
                  { ServerServices.createPermissive with
                      OnApply = applied.Add }
                  |> ServerServices.withHandler "/handlers/several" handler

              let session =
                  ServerSession.init services empty (wire (Action.Call("/handlers/several", None, None)))

              let _, out = ServerSession.step session (clickEv "call")

              Expect.isTrue out.Committed "the handler committed"
              Expect.equal applied.Count 1 "the sink was called once for the whole handler"

              // Compared by identity: a resolved tree's ops carry handler slots,
              // so a `TreeOp` list has no structural equality to compare with.
              Expect.isTrue
                  (LanguagePrimitives.PhysicalEquality applied[0] out.Ops)
                  "and it saw the response ops themselves"
          } ]
