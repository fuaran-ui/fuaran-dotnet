module Fuaran.UI.OpStream.Tests.ActionInvocationTests

open System
open System.IO
open Expecto
open Fuaran.Core
open Fuaran.UI.Generated
open Fuaran.UI.Ops.ActionInvocation
open Fuaran.UI.OpStream.Sqlite

// ============================================================================
//  Phase 889 — the user-action record, its redaction default, and its durable
//  sink.
//
//  The load-bearing test here is the POISON test. Everything else pins a shape;
//  that one pins the privacy posture, and it is the only assertion in the file
//  whose failure means user content reached a durable log.
// ============================================================================

type private Msg = Poke of string

/// The distinctive marker planted in EVERY payload position of EVERY case. If
/// this string appears anywhere in a default-mode record, a payload value
/// escaped redaction.
let private poison = "PoIsOn-uSeR-tYpEd-53cr3t"

/// All TWELVE `Action` cases — the count is checked below, because a record
/// designed against the "twelve cases" the phase ORIGINALLY claimed would then
/// have been one short, and the shortfall would have been invisible. (Phase 1124
/// made twelve the true count by adding `Print`; the guard below is why that
/// arrived as a failing test rather than as an untested case.)
///
/// Every argument that can carry a value carries the poison; the author-declared
/// NAMES (endpoint, channel, state key, tool, capability, node id) deliberately
/// do not, since those are what the redacted record is supposed to keep.
/// `Print` (Phase 1124) is the one case with NO argument at all — it cannot
/// carry the poison, and it is in the fixture precisely so that fact is asserted
/// rather than assumed: a payload-free case is the shape most likely to be left
/// out of a coverage list on the grounds that there is nothing to check.
let private allTwelveCases: (string * Action<Msg>) list =
    [ "Chain",
      Action.Chain
          [ Action.WriteToClipboard(TextSource.Literal poison)
            Action.Navigate("/a?q=" + poison) ]
      "WriteToClipboard", Action.WriteToClipboard(TextSource.Literal poison)
      "Dispatch", Action.Dispatch(Poke poison)
      // Fully qualified: `open System` puts `System.Action` in scope and its
      // instance `Invoke` wins the name resolution otherwise.
      "Invoke", Fuaran.UI.Generated.Action.Invoke("cap.publish", [])
      "ReadFileBody", Action.ReadFileBody(poison, None, FileReadEncoding.Text, None)
      "Call", Action.Call("/api/save", None, None)
      "Navigate", Action.Navigate("/orders?email=" + poison + "#" + poison)
      "CommitLocal", Action.CommitLocal "field-1"
      "Notify", Action.Notify("toast", JStr poison)
      "SetState", Action.SetState("draft.body", Some(JStr poison), None)
      "AiTool", Action.AiTool("summarise", JObj [ "text", JStr poison ])
      "Print", Action.Print ]

let private site =
    ActionInvocation.clientSite AffordanceProvenance.TreeDeclared (Some "node-1") (Some "interaction-7")

/// Everything in a record that is a string or a rendered JSON payload — the
/// full surface a poison string could hide in.
let private renderedSurface (r: ActionInvocation) : string =
    String.concat
        "|"
        [ r.Action
          defaultArg r.NodeId ""
          defaultArg r.Event ""
          sprintf "%A" r.Outcome
          sprintf "%A" r.Provenance
          sprintf "%A" r.Path
          defaultArg r.InteractionId ""
          match r.Payload with
          | Some jv -> Json.encode jv
          | None -> "" ]

[<Tests>]
let redactionTests =
    testList
        "Phase 889 — the redaction default"
        [ test "the Action vocabulary has TWELVE cases and the fixture covers each exactly once" {
              // Guards the poison test below against the failure mode that
              // makes it useless: a case the fixture forgot is a case whose
              // redaction nobody checked. The phase itself corrected "twelve"
              // to eleven; a fixture out of step with the DU would restore the
              // gap silently. It did its job at Phase 1124, which added
              // `Print` — the count is reflected off the DU, so the new case
              // could not be shipped without being covered.
              let names = allTwelveCases |> List.map fst
              Expect.equal (List.length names) 12 "twelve cases"
              Expect.equal (List.length (List.distinct names)) 12 "each named once"

              let unionCases =
                  Reflection.FSharpType.GetUnionCases(typeof<Action<Msg>>)
                  |> Array.map _.Name
                  |> Array.sort

              Expect.equal (List.sort names |> Array.ofList) unionCases "the fixture matches the DU exactly"
          }

          test "POISON: no payload value survives the default capture mode, in any of the twelve cases" {
              for name, action in allTwelveCases do
                  let record =
                      ActionInvocation.record ActionCaptureMode.Redacted site ActionOutcome.Dispatched action

                  Expect.isNone record.Payload (sprintf "%s: the redacted mode carries no payload at all" name)

                  Expect.isFalse
                      ((renderedSurface record).Contains poison)
                      (sprintf
                          "%s: a payload value reached the record — this is the assertion whose failure means user content is in a durable log. Record: %s"
                          name
                          (renderedSurface record))
          }

          test "Navigate keeps its PATH and drops the query string and fragment" {
              // A route is the one `describe` argument that is not author-fixed
              // vocabulary, and a query string is where user data rides.
              Expect.equal
                  (ActionInvocation.describe (Action.Navigate "/orders?email=a@b.c#tok": Action<Msg>))
                  "Navigate(/orders)"
                  "query and fragment gone, path kept"

              Expect.equal (ActionInvocation.routePath "/plain") "/plain" "a route with neither is unchanged"
              Expect.equal (ActionInvocation.routePath "/p#f?q=1") "/p" "a fragment before a query still cuts"
          }

          test "SetState keeps the KEY and never the value" {
              Expect.equal
                  (ActionInvocation.describe (Action.SetState("draft.body", Some(JStr poison), None): Action<Msg>))
                  "SetState(draft.body)"
                  "the free-text a text control writes back must not appear"
          }

          test "a Chain is ONE invocation and names no constituent" {
              let chain: Action<Msg> =
                  Action.Chain
                      [ Action.Navigate("/x?s=" + poison)
                        Action.WriteToClipboard(TextSource.Literal poison) ]

              Expect.equal (ActionInvocation.describe chain) "Chain" "no contents"

              Expect.isNone
                  (ActionInvocation.payloadFor ActionCaptureMode.PayloadBearing chain)
                  "not even under the opt-in — a chain's constituents are a deliberate omission, not an oversight"
          } ]

[<Tests>]
let optInTests =
    testList
        "Phase 889 — payload capture is an opt-in a host has to type"
        [ test "the opt-in mode carries the payload where the wire has one" {
              let payloadOf (a: Action<Msg>) =
                  ActionInvocation.payloadFor ActionCaptureMode.PayloadBearing a

              Expect.equal (payloadOf (Action.Notify("toast", JStr "hi"))) (Some(JStr "hi")) "Notify"
              Expect.equal (payloadOf (Action.SetState("k", Some(JInt 3), None))) (Some(JInt 3)) "SetState"
              Expect.equal (payloadOf (Action.AiTool("t", JStr "a"))) (Some(JStr "a")) "AiTool"

              Expect.equal
                  (payloadOf (Action.Navigate "/o?email=a@b.c"))
                  (Some(JStr "/o?email=a@b.c"))
                  "Navigate under the opt-in keeps the WHOLE route — that IS the opt-in"
          }

          test "three cases stay payload-free even under the opt-in, each structurally" {
              let payloadOf (a: Action<Msg>) =
                  ActionInvocation.payloadFor ActionCaptureMode.PayloadBearing a

              Expect.isNone (payloadOf (Action.Dispatch(Poke "x"))) "Dispatch is a closure — no wire payload exists"
              Expect.isNone (payloadOf (Action.Call("/api", None, None))) "Call has no payload slot on the wire"
              Expect.isNone (payloadOf (Action.Chain [])) "a Chain is one gesture"
          }

          test "the shipped default sinks are redacted" {
              Expect.equal
                  (ActionInvocationSink.noop.CaptureMode)
                  ActionCaptureMode.Redacted
                  "the no-op sink must not declare an opt-in it would hand on"

              Expect.equal
                  ((ActionInvocationSink.Collector() :> IActionInvocationSink).CaptureMode)
                  ActionCaptureMode.Redacted
                  "the collector's parameterless ctor is redacted"
          }

          test "emit routes through the SINK's mode, not the call site's" {
              // The opt-in lives on the sink precisely so an emission point
              // cannot widen it. Two sinks, one call site, two answers.
              let redacted = ActionInvocationSink.Collector()
              let bearing = ActionInvocationSink.Collector(ActionCaptureMode.PayloadBearing)
              let action: Action<Msg> = Action.Notify("toast", JStr poison)

              ActionInvocation.emit (Some(redacted :> IActionInvocationSink)) site ActionOutcome.Dispatched action
              ActionInvocation.emit (Some(bearing :> IActionInvocationSink)) site ActionOutcome.Dispatched action

              Expect.isNone redacted.Recorded.Head.Payload "redacted sink"
              Expect.equal bearing.Recorded.Head.Payload (Some(JStr poison)) "opted-in sink"
          }

          test "an unwired sink records nothing and a throwing sink does not break dispatch" {
              ActionInvocation.emit None site ActionOutcome.Dispatched (Action.Chain []: Action<Msg>)

              let throwing =
                  { new IActionInvocationSink with
                      member _.CaptureMode = ActionCaptureMode.Redacted
                      member _.RecordActionInvocation _ = failwith "sink is down" }

              ActionInvocation.emit (Some throwing) site ActionOutcome.Dispatched (Action.Chain []: Action<Msg>)
          } ]

// ─── The durable sink ───────────────────────────────────────────────────────

let private freshDbPath () : string =
    Path.Combine(Path.GetTempPath(), sprintf "fuaran-actionlog-%s.db" (Guid.NewGuid().ToString("N")))

let private connStringFor (path: string) : string = sprintf "Data Source=%s" path

let private fixedAt = DateTimeOffset(2026, 8, 18, 9, 30, 0, TimeSpan.Zero)

[<Tests>]
let durabilityTests =
    testList
        "Phase 889 — the durable user-action log"
        [ test "every field of every outcome round-trips through SQLite" {
              let path = freshDbPath ()

              let sink =
                  ActionInvocationSqliteSink(connStringFor path, ActionCaptureMode.PayloadBearing, fun () -> fixedAt)

              let asSink = sink :> IActionInvocationSink

              let written: ActionInvocation list =
                  [ { Action = "SetState(page)"
                      NodeId = Some "grid-1"
                      Event = Some "click"
                      Outcome = ActionOutcome.Dispatched
                      Provenance = AffordanceProvenance.RendererSynthesised
                      Path = DispatchPath.ClientRenderer
                      InteractionId = Some "interaction-7"
                      Payload = Some(JObj [ "page", JInt 3 ]) }
                    { Action = "Call(/api/pay)"
                      NodeId = Some "btn-pay"
                      Event = Some "click"
                      Outcome = ActionOutcome.Denied "dispatch denied by host policy: Call(/api/pay)"
                      Provenance = AffordanceProvenance.TreeDeclared
                      Path = DispatchPath.ServerDriven
                      InteractionId = None
                      Payload = None }
                    { Action = "Chain"
                      NodeId = None
                      Event = None
                      Outcome = ActionOutcome.Failed "host closure threw"
                      Provenance = AffordanceProvenance.TreeDeclared
                      Path = DispatchPath.ClientRenderer
                      InteractionId = Some "interaction-8"
                      Payload = None } ]

              for w in written do
                  asSink.RecordActionInvocation w

              let read = sink.Read()

              Expect.equal (List.length read) 3 "three entries, append-only"
              Expect.equal (read |> List.map _.Invocation) written "every field survives, in insertion order"

              Expect.equal
                  (read |> List.map _.At)
                  [ fixedAt; fixedAt; fixedAt ]
                  "the HOST stamped the instant — the record carries none, so the driver stays deterministic"

              try
                  File.Delete path
              with _ ->
                  ()
          }

          test "the log outlives the sink instance — that is what durable means here" {
              let path = freshDbPath ()
              let conn = connStringFor path

              let first =
                  ActionInvocationSqliteSink(conn, ActionCaptureMode.Redacted, fun () -> fixedAt)
                  :> IActionInvocationSink

              first.RecordActionInvocation(
                  ActionInvocation.record
                      ActionCaptureMode.Redacted
                      site
                      ActionOutcome.Dispatched
                      (Action.Navigate "/home?t=1": Action<Msg>)
              )

              // A second sink over the same file, as a later process would open it.
              let second = ActionInvocationSqliteSink(conn)
              let read = second.Read()

              Expect.equal (List.length read) 1 "the row is on disk, not in the first instance"
              Expect.equal read.Head.Invocation.Action "Navigate(/home)" "and it was written redacted"

              try
                  File.Delete path
              with _ ->
                  ()
          }

          test "the sink's own capture mode is what a host wired" {
              let path = freshDbPath ()

              Expect.equal
                  ((ActionInvocationSqliteSink(connStringFor path) :> IActionInvocationSink).CaptureMode)
                  ActionCaptureMode.Redacted
                  "the one-argument ctor — the shape a host reaches for by default — is redacted"

              try
                  File.Delete path
              with _ ->
                  ()
          } ]
