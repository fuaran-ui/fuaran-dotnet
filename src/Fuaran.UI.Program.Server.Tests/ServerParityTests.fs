module Fuaran.UI.Program.Server.Tests.ServerParityTests

// ─── The server placement joins the tier-parity family ───────────────
//
// The family's premise is that one tree and one event script behave identically
// at every placement. A third placement that could not join it would be a third
// implementation of the algebra wearing the same name, so it joins from birth
// rather than after the fact.
//
// The leg reads the SAME fixture files, through the SAME loader, as the two
// legs in the .NET parity suite and the one under node. A separate corpus would
// prove nothing.
//
// It lives here rather than in the shared runner for one reason: the shared
// runner is Fable-clean, because leg (c) compiles it to JavaScript. A SERVER
// placement has no browser leg by definition, so putting it there would compile
// a server loop into a bundle to assert something about a server.
//
// ── The two readings of one fixture ──────────────────────────────────────────
// Driven with NO handlers registered, this placement must agree with the client
// placement on every fixture — including the one whose call action names a
// handler, because an endpoint no host registered reaches nothing. That is the
// claim "the third placement inherits the shared algebra", and it is asserted
// over the whole corpus rather than over a hand-picked case.
//
// Driven with the handler registered, that same fixture exercises the
// server-effect arms. Same tree, same event script, different host — which is
// exactly the axis the family was built to vary.

open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops.Introspect
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

/// The corpus's fixture root — the driver-semantics family is read from the
/// same sibling clone the codec suite certifies against, through the SAME
/// loader the other legs use. A separate corpus would prove nothing, and a
/// second copy of one would be the drift class the graduation removed.
let private fixturesRoot = FixtureIo.fixturesRoot

/// The endpoint the corpus's handler scenario names.
[<Literal>]
let private handlerEndpoint = "/handlers/refresh"

[<Literal>]
let private handlerFixture = "server-handler-call"

/// The same endpoint, named from INSIDE a chain — the nested reading (D7).
[<Literal>]
let private nestedFixture = "nested-handler-call"

let private toLiveEvent (index: int) (ev: ScriptedEvent) : LiveEvent =
    { ConnId = "parity"
      NodeId = ev.NodeId
      Event = ev.Event
      Payload = ev.Payload
      LastSeq = index }

/// Drive a fixture through the server placement, producing the family's
/// per-step observation. The comparable projection is the resolved tree's
/// canonical JSON plus the closure-free client effects — the shared semantics,
/// not this placement's transport.
let private runServerLogicPlacement
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
            |> List.scan
                (fun (session, _) ev ->
                    let next, out = ServerSession.step session ev

                    next,
                    Some
                        { ResolvedJson = CanonicalJson.encodeNode out.Resolved
                          Effects = out.ClientEffects |> List.map ClientEffect.encode
                          Refused = out.Rejected.IsSome
                          // Like the bounded driver, this placement does not
                          // PERFORM a client effect — it ships one to the
                          // surface that will — so the seam is applied here, to
                          // what the loop emitted, through the same decision
                          // procedure every other placement uses.
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

/// Every built-in arm permitted and one host function registered — the same
/// posture the client leg takes with its effect registry, so a difference in
/// observations is a difference in the FOLD rather than in what the host allowed.
let private openServices =
    let registry =
        ServerEffectRegistry.denyAll
        |> ServerEffectRegistry.register "audit" (fun _ -> Ok(Fuaran.Core.JStr "recorded"))
        |> ServerEffectRegistry.permissive

    { ServerServices.createPermissive with
        Effects = registry }

/// The handler the corpus fixture's endpoint names when it is registered: one
/// arm of each server capability, in the order a real handler would use them —
/// read, compute, mutate, call out, push, announce.
///
/// The host call sits in the MIDDLE of that list deliberately. Two-phase staging
/// (DECISIONS.md D8) defers it to the perform phase, so the fixture's audit trail
/// reports it LAST — the decision made visible in the one family every placement
/// reads, rather than only in a unit test of the handler.
let private refreshHandler: Handler =
    let rows: Fuaran.Core.Table =
        { Schema = [ "n", Fuaran.Core.IntType ]
          Columns =
            [ { Name = "n"
                Type = Fuaran.Core.IntType
                Cells = [ Fuaran.Core.Int 1; Fuaran.Core.Int 2; Fuaran.Core.Int 3 ] } ] }

    { Name = "refresh"
      Stages =
        [ Effect(
              ServerEffect.RunQuery(
                  "rows",
                  Fuaran.Core.Embedded rows,
                  [ Fuaran.Compute.Limit(Fuaran.Compute.Slot.Lit 2, Fuaran.Compute.Slot.Lit 0) ]
              )
          )
          Compute(Action.SetState("rows", Some(Fuaran.Core.JStr "2 rows"), None))
          Effect(ServerEffect.ApplyOps [ TreeOp.RemoveNode(NodeId "refresh") ])
          Effect(ServerEffect.HostCall("audit", Fuaran.Core.JStr "refreshed", None))
          Effect(ServerEffect.EmitPatch [ TreeOp.RemoveNode(NodeId "readout") ])
          Effect(ServerEffect.Notify("audit", Fuaran.Core.JStr "refreshed")) ] }

/// The resolved Markdown text at `id` — the observable "the store changed and
/// re-resolution made it visible" probe.
let private readAt (id: string) (tree: Node<obj>) : string option =
    match findNode (NodeId id) tree with
    | Some node ->
        match node.Kind with
        | NodeKind.Markdown({ Text = TextSource.Literal s }) -> Some s
        | _ -> None
    | None -> None

[<Tests>]
let tests =
    let fixtures = FixtureIo.load fixturesRoot

    testList
        "tier parity — the server-logic placement"
        [ test "the fixture corpus is present" {
              // A parity leg that silently found no fixtures would report green
              // while asserting nothing.
              Expect.isNonEmpty fixtures $"the corpus enumerates no driver-semantics scenario under {fixturesRoot}"

              Expect.isTrue
                  (fixtures |> List.exists (fun f -> f.Name = handlerFixture))
                  $"the corpus carries the {handlerFixture} fixture this placement is here for"
          }

          testList
              "with no handler registered, it agrees with the client placement step by step"
              [ for fixture in fixtures ->
                    test fixture.Name {
                        match runServerLogicPlacement openServices fixture, runClientPlacement fixture with
                        | Error e, _
                        | _, Error e -> failtestf "%s: %s" fixture.Name e
                        | Ok server, Ok client ->
                            match compare fixture.Name "ServerSession" server "Runtime" client with
                            | None -> ()
                            | Some divergence -> failtestf "%s" (Divergence.describe divergence)
                    } ]

          test "with its handler registered, the fixture exercises the server-effect arms" {
              match fixtures |> List.tryFind (fun f -> f.Name = handlerFixture) with
              | None -> failtestf "%s is missing from the corpus" handlerFixture
              | Some fixture ->
                  match JsonDecode.decodeNode fixture.TreeJson with
                  | Error err -> failtestf "decode failed: %A" err
                  | Ok wire ->
                      let services =
                          openServices |> ServerServices.withHandler handlerEndpoint refreshHandler

                      let session = ServerSession.init services empty wire

                      let ev = fixture.Events |> List.mapi toLiveEvent |> List.head
                      let next, out = ServerSession.step session ev

                      Expect.isNone out.Rejected "the scripted event passed both gates"
                      Expect.isTrue out.Committed "and the handler ran to completion"

                      Expect.equal
                          out.Performed
                          [ "RunQuery"; "ApplyOps"; "EmitPatch"; "Notify"; "host:audit" ]
                          "every arm the handler declared was performed — in EXECUTION order, so the \
                           staged host call reports after the four it was declared among (D8)"

                      Expect.isTrue
                          (Map.containsKey "rows" next.Store.QueryResults)
                          "the query landed its result in the session's query slot"

                      Expect.equal
                          (readAt "readout" next.Resolved)
                          (Some "2 rows")
                          "the Compute stage's write re-resolved into the tree — the shared fold, at a third placement"

                      Expect.isNone (findNode (NodeId "refresh") next.Resolved) "the applied ops changed domain state"

                      Expect.isSome
                          (findNode (NodeId "readout") next.Resolved)
                          "and the EMITTED patch did not — it is shipped, never applied"

                      Expect.equal
                          (out.Notifications |> List.map fst)
                          [ "audit" ]
                          "the notification is reported for the host to deliver"

                      Expect.isNonEmpty out.Ops "and the response carries the re-resolution diff"
          }

          test "the nested fixture splices the handler into the chain, in place" {
              // The claim D7 makes, driven through the corpus: a call action
              // buried between two writes runs where it sits. The write BEFORE
              // it must be visible to the handler and then overwritten by the
              // handler's own; the write AFTER it must survive. A top-level-only
              // arm produces neither — it produces "before" and "after" with no
              // handler at all, which is what this fixture looked like until the
              // arm moved into the fold.
              match fixtures |> List.tryFind (fun f -> f.Name = nestedFixture) with
              | None -> failtestf "%s is missing from the corpus" nestedFixture
              | Some fixture ->
                  match JsonDecode.decodeNode fixture.TreeJson with
                  | Error err -> failtestf "decode failed: %A" err
                  | Ok wire ->
                      let services =
                          openServices |> ServerServices.withHandler handlerEndpoint refreshHandler

                      let session = ServerSession.init services empty wire
                      let ev = fixture.Events |> List.mapi toLiveEvent |> List.head
                      let next, out = ServerSession.step session ev

                      Expect.isNone out.Rejected "the scripted event passed both gates"
                      Expect.isTrue out.Committed "and the nested handler ran to completion"

                      Expect.equal
                          out.Performed
                          [ "RunQuery"; "ApplyOps"; "EmitPatch"; "Notify"; "host:audit" ]
                          "a nested call reaches every arm a top-level one reaches — the SAME handler run"

                      Expect.equal
                          (readAt "readout" next.Resolved)
                          (Some "2 rows")
                          "the handler ran AFTER the chain's first write, overwriting it"

                      Expect.equal
                          (readAt "tail" next.Resolved)
                          (Some "after")
                          "and BEFORE the chain's last write, which still landed"
          }

          test "the nested fixture is inert in the chain when the endpoint names nothing" {
              match fixtures |> List.tryFind (fun f -> f.Name = nestedFixture) with
              | None -> failtestf "%s is missing from the corpus" nestedFixture
              | Some fixture ->
                  match JsonDecode.decodeNode fixture.TreeJson with
                  | Error err -> failtestf "decode failed: %A" err
                  | Ok wire ->
                      let session = ServerSession.init openServices empty wire
                      let ev = fixture.Events |> List.mapi toLiveEvent |> List.head
                      let next, out = ServerSession.step session ev

                      Expect.isEmpty out.Performed "no capability was reached"

                      Expect.equal
                          out.Diagnostics
                          [ ServerDiagnostic.HandlerUnregistered ]
                          "the dead end is diagnosed from inside the chain, exactly as from the top"

                      Expect.equal
                          (readAt "readout" next.Resolved, readAt "tail" next.Resolved)
                          (Some "before", Some "after")
                          "and the chain around it folded untouched — the other legs' reading of this fixture"
          }

          test "the same fixture is inert when the endpoint names nothing" {
              // The negative half, and the reason the fixture can sit in a
              // family the other placements also run: an unregistered endpoint
              // reaches no capability, however permissive the gates.
              match fixtures |> List.tryFind (fun f -> f.Name = handlerFixture) with
              | None -> failtestf "%s is missing from the corpus" handlerFixture
              | Some fixture ->
                  match JsonDecode.decodeNode fixture.TreeJson with
                  | Error err -> failtestf "decode failed: %A" err
                  | Ok wire ->
                      let session = ServerSession.init openServices empty wire
                      let ev = fixture.Events |> List.mapi toLiveEvent |> List.head
                      let next, out = ServerSession.step session ev

                      Expect.isEmpty out.Performed "no capability was reached"
                      Expect.isEmpty out.Ops "and nothing changed"

                      Expect.equal
                          out.Diagnostics
                          [ ServerDiagnostic.HandlerUnregistered ]
                          "but the dead end is diagnosed rather than silent"

                      Expect.isSome (findNode (NodeId "refresh") next.Resolved) "the tree is untouched"
          } ]
