module Fuaran.UI.Tests.Messaging

// Typed cross-host messaging — the prototype's authoring surface, exercised the
// way a real host would use it, plus the contract law property-tested.
//
// The point being demonstrated: a message is written typed, crosses the wire as
// a plain `Notify` with a data payload (so it genuinely survives the canonical
// codec), and is received typed again. An untyped host sees the same
// `(channel, payload)` and routes it identically.

open Expecto
open FsCheck
open FsCheck.FSharp

open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI
open Fuaran.UI.Ops
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.Messaging

// ─── A host's message vocabulary — an ordinary data DU, no closures ─────────

type AppMsg =
    | SetYear of int
    | Search of term: string
    | ToggleAdvanced of on: bool
    | Refresh

/// The contract a host writes once. Hand-written on purpose: this is the piece
/// the design admits is fallible, and `roundTrips` below is what closes it.
let appContract: MessageContract<AppMsg> =
    MessageContract.create
        [ "app.setYear"; "app.search"; "app.toggleAdvanced"; "app.refresh" ]
        (fun msg ->
            match msg with
            | SetYear year -> "app.setYear", JObj [ "year", JInt year ]
            | Search term -> "app.search", JObj [ "term", JStr term ]
            | ToggleAdvanced on -> "app.toggleAdvanced", JObj [ "on", JBool on ]
            | Refresh -> "app.refresh", JObj [])
        (fun channel payload ->
            let field name =
                match payload with
                | JObj fields -> fields |> List.tryPick (fun (k, v) -> if k = name then Some v else None)
                | _ -> None

            match channel, field "year", field "term", field "on" with
            | "app.setYear", Some(JInt year), _, _ -> Some(SetYear year)
            | "app.search", _, Some(JStr term), _ -> Some(Search term)
            | "app.toggleAdvanced", _, _, Some(JBool on) -> Some(ToggleAdvanced on)
            | "app.refresh", _, _, _ -> Some Refresh
            | _ -> None)

[<Tests>]
let messagingTests =
    testList
        "Fuaran.UI.Messaging — typed messages over an untyped wire"
        [ test "a typed message lowers to a Notify carrying real data" {
              // The authoring surface: hand it a typed message, exactly as
              // `Action.Dispatch` would take one.
              match dispatchTyped appContract (SetYear 2024) with
              | Action.Notify(channel, payload) ->
                  Expect.equal channel "app.setYear" "lowers onto its declared channel"
                  Expect.equal payload (JObj [ "year", JInt 2024 ]) "the payload is DATA, not a closure"
              | other -> failtestf "expected a Notify, got %A" other
          }

          test "the receiving end gets the typed message back" {
              // What a host's IFuaranRuntime.Notify implementation does.
              match dispatchTyped appContract (Search "revenue") with
              | Action.Notify(channel, payload) ->
                  Expect.equal (route appContract channel payload) (Some(Search "revenue")) "typed at both ends"
              | other -> failtestf "expected a Notify, got %A" other
          }

          test "an unowned channel is a miss, not a crash" {
              Expect.isNone (route appContract "someone.elses.channel" (JObj [])) "None for a foreign channel"
          }

          test "routeAny composes several module vocabularies" {
              let other: MessageContract<AppMsg> =
                  MessageContract.create [ "legacy.refresh" ] (fun _ -> "legacy.refresh", JObj []) (fun c _ ->
                      if c = "legacy.refresh" then Some Refresh else None)

              Expect.equal
                  (routeAny [ appContract; other ] "legacy.refresh" (JObj []))
                  (Some Refresh)
                  "second contract claims it"

              Expect.equal
                  (routeAny [ appContract; other ] "app.refresh" (JObj []))
                  (Some Refresh)
                  "first contract claims it"

              Expect.isNone (routeAny [ appContract; other ] "nobody" (JObj [])) "neither claims it"
          }

          test "the manifest declares a channel for every message" {
              // Proves `Channels` is complete — what a cross-language host
              // generates its constants from, so the two sides cannot drift.
              for msg in [ SetYear 1; Search "x"; ToggleAdvanced true; Refresh ] do
                  Expect.isTrue (MessageContract.declaresChannelFor appContract msg) $"declared for {msg}"
          }

          test "THE LAW: lower then lift is identity, over generated messages" {
              // The contract is hand-written, so this is the guard that it is not
              // silently dropping or corrupting interactions. Same Check.One
              // idiom the wire-format fuzzer uses.
              let msgArb =
                  ArbMap.defaults
                  |> ArbMap.generate<int * string * bool>
                  |> Gen.map (fun (year, term, on) -> [ SetYear year; Search term; ToggleAdvanced on; Refresh ])
                  |> Arb.fromGen

              Check.One(
                  Config.QuickThrowOnFailure.WithMaxTest(500),
                  Prop.forAll msgArb (List.forall (MessageContract.roundTrips appContract))
              )
          }

          test "END TO END: a typed message survives the canonical wire and comes back typed" {
              // The claim the whole design rests on. A tree authored with a typed
              // message is encoded by the SAME canonical encoder the op-stream
              // hashes with, decoded by the canonical decoder, and the message is
              // recovered typed — no closure sentinel anywhere in between.
              let tree: Node<AppMsg> =
                  Fuaran.button
                      "set-year"
                      { Defaults.button with
                          Label = TextSource.Literal "Set year"
                          OnClick = dispatchTyped appContract (SetYear 2024) }

              let json = CanonicalJson.encodeNode tree

              // The payload is on the wire as data. Contrast with Action.Dispatch,
              // which would render "<closure>" here and lose the 2024 entirely.
              Expect.stringContains json "app.setYear" "the channel crossed the wire"
              Expect.stringContains json "2024" "the PAYLOAD crossed the wire"
              Expect.isFalse (json.Contains "<closure>") "no closure sentinel — nothing was lost"

              // The EXACT bytes, pinned. The TypeScript standalone-bundle test
              // feeds this same literal to an untyped host, so the two tiers are
              // locked to one string and cannot drift apart silently.
              Expect.equal
                  json
                  """{"accessibility":{"role":"button"},"id":"set-year","kind":{"$type":"Button","label":"Set year","onClick":{"$type":"Notify","channel":"app.setYear","payload":{"year":2024}},"variant":"Secondary"}}"""
                  "canonical encoding is byte-stable across tiers"

              // Decode as any conformant host would, then lift back to typed.
              match JsonDecode.decodeNodeObj json with
              | Error e -> failtestf "decode failed at %s: %s" e.Path e.Message
              | Ok decoded ->
                  let onClick =
                      match decoded.Kind with
                      | NodeKind.Input(InputKind.Button spec) -> spec.OnClick
                      | other -> failtestf "expected a Button, got %A" other

                  match onClick with
                  | Action.Notify(channel, payload) ->
                      Expect.equal
                          (route appContract channel payload)
                          (Some(SetYear 2024))
                          "the host recovers the ORIGINAL typed message after a full wire round trip"
                  | other -> failtestf "expected a Notify on the wire, got %A" other
          }

          test "a contract that loses data fails its own law" {
              // The law has teeth: a lossy contract is caught, not accepted.
              let lossy: MessageContract<AppMsg> =
                  MessageContract.create [ "app.setYear" ] (fun _ -> "app.setYear", JObj []) (fun c _ ->
                      if c = "app.setYear" then Some(SetYear 0) else None)

              Expect.isFalse (MessageContract.roundTrips lossy (SetYear 2024)) "the dropped year is caught"
          } ]
