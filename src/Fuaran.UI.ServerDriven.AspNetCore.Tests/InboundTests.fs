module Fuaran.UI.ServerDriven.AspNetCore.Tests.InboundTests

// ─── Phase 152 Track D: AspNetCore SSE+POST backend ──────────────
//
// The testable halves of the backend: Inbound.parseLiveEvent (POST body JSON
// → LiveEvent, with the connId stamped from the cookie not the body) and the
// ConnectionRegistry (thread-safe connId → connection routing). The SSE
// streaming handler + POST endpoint are thin ASP.NET glue, browser-verified.

open Expecto
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation
open Fuaran.UI.ServerDriven.AspNetCore

[<Tests>]
let inboundTests =
    testList
        "Inbound.parseLiveEvent"
        [ test "parses nodeId / event / lastSeq + a typed payload, connId from the cookie" {
              let json =
                  """{"nodeId":"sel","event":"change","payload":{"value":"a","count":3,"on":true},"lastSeq":7}"""

              let ev = Inbound.parseLiveEvent "conn-42" json

              Expect.equal ev.ConnId "conn-42" "connId stamped from the cookie, not the body"
              Expect.equal ev.NodeId "sel" "nodeId"
              Expect.equal ev.Event "change" "event"
              Expect.equal ev.LastSeq 7 "lastSeq"
              Expect.equal (Map.find "value" ev.Payload) (LiveValue.Str "a") "string payload value"
              Expect.equal (Map.find "count" ev.Payload) (LiveValue.Num 3.0) "number payload value"
              Expect.equal (Map.find "on" ev.Payload) (LiveValue.Bool true) "bool payload value"
          }

          test "a missing payload degrades to an empty map (G1 will reject downstream)" {
              let ev = Inbound.parseLiveEvent "c" """{"nodeId":"btn","event":"click"}"""
              Expect.isEmpty ev.Payload "no payload → empty map"
              Expect.equal ev.LastSeq 0 "missing lastSeq → 0"
          }

          test "a null payload value maps to LiveValue.Null" {
              let ev =
                  Inbound.parseLiveEvent "c" """{"nodeId":"sel","event":"change","payload":{"value":null}}"""

              Expect.equal (Map.find "value" ev.Payload) LiveValue.Null "null → LiveValue.Null"
          }

          test "junk fields degrade to safe empties (never throws past JSON parse)" {
              let ev = Inbound.parseLiveEvent "c" """{"unexpected":123}"""
              Expect.equal ev.NodeId "" "no nodeId → empty (G1 rejects)"
              Expect.equal ev.Event "" "no event → empty"
          }

          test "an out-of-Int32-range lastSeq degrades to 0, never overflows (Phase 211 M1)" {
              let ev =
                  Inbound.parseLiveEvent "c" """{"nodeId":"btn","event":"click","lastSeq":9999999999999}"""

              Expect.equal ev.LastSeq 0 "out-of-range lastSeq → 0 (no OverflowException)"
          } ]

// Phase 211 (C2/M1): the guarded parse both transports share — a malformed body
// is None (→ a clean 400 / WS skip), never an unhandled exception.
[<Tests>]
let guardedParseTests =
    testList
        "Inbound.tryParseLiveEvent (guarded, Phase 211)"
        [ test "a well-formed body parses to Some, connId stamped" {
              match Inbound.tryParseLiveEvent "conn-9" """{"nodeId":"btn","event":"click"}""" with
              | Some ev ->
                  Expect.equal ev.ConnId "conn-9" "connId stamped"
                  Expect.equal ev.NodeId "btn" "nodeId parsed"
              | None -> failtest "expected Some for a well-formed body"
          }

          test "a non-JSON body is None (default-deny → 400), never throws" {
              Expect.isNone (Inbound.tryParseLiveEvent "c" "not json at all {") "malformed → None"
          }

          test "an empty body is None, never throws" { Expect.isNone (Inbound.tryParseLiveEvent "c" "") "empty → None" }

          test "a structurally-valid-but-junk body is Some safe-empties (G1 rejects, not a parse failure)" {
              match Inbound.tryParseLiveEvent "c" """{"unexpected":123}""" with
              | Some ev ->
                  Expect.equal ev.NodeId "" "no nodeId → empty"
                  Expect.equal ev.Event "" "no event → empty"
              | None -> failtest "junk-but-valid JSON is not malformed — expected Some"
          }

          test "an out-of-range lastSeq still parses to Some with lastSeq 0 (M1)" {
              match Inbound.tryParseLiveEvent "c" """{"nodeId":"n","event":"click","lastSeq":9999999999999}""" with
              | Some ev -> Expect.equal ev.LastSeq 0 "out-of-range → 0, still Some"
              | None -> failtest "out-of-range lastSeq is not a parse failure — expected Some"
          } ]

// A trivial ILiveConnection stub recording the events routed to it.
type private StubConn(sink: ResizeArray<LiveEvent>) =
    interface ILiveConnection with
        member _.Handle ev = sink.Add ev
        member _.Resync _ = ()
        member _.Channel = SseChannel()

[<Tests>]
let registryTests =
    testList
        "ConnectionRegistry"
        [ test "routes an event to the registered connection by id" {
              let sink = ResizeArray<LiveEvent>()
              let registry = ConnectionRegistry()
              registry.Add("c1", StubConn(sink))

              let ev =
                  { ConnId = "c1"
                    NodeId = "n"
                    Event = "click"
                    Payload = Map.empty
                    LastSeq = 0 }

              match registry.TryGet "c1" with
              | Some conn -> conn.Handle ev
              | None -> failtest "expected a registered connection"

              Expect.equal (List.ofSeq sink) [ ev ] "the event reached the right connection"
          }

          test "an unknown connId resolves to None (event dropped, default-deny)" {
              let registry = ConnectionRegistry()
              Expect.isNone (registry.TryGet "ghost") "no connection for an unknown id"
          }

          test "Remove deregisters and decrements the count" {
              let registry = ConnectionRegistry()
              registry.Add("c1", StubConn(ResizeArray()))
              registry.Add("c2", StubConn(ResizeArray()))
              Expect.equal registry.Count 2 "two live connections"
              registry.Remove "c1"
              Expect.equal registry.Count 1 "one after removal"
              Expect.isNone (registry.TryGet "c1") "removed connection is gone"
          } ]
