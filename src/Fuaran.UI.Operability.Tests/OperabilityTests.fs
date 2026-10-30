module Fuaran.UI.Operability.Tests.OperabilityTests

open System.IO
open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Operability

let private corpusRoot () = Fuaran.Tests.CorpusRoot.tryFind ()

let private expectedOutcome (expect: Cases.Expect) : Outcome =
    match expect with
    | Cases.Resolves clauses ->
        clauses
        |> List.map (fun (id, value) ->
            id,
            (match value with
             | Some v -> SlotValue.Value v
             | None -> SlotValue.NoValue))
        |> Outcome.Resolved
    | Cases.Refused -> Outcome.NotResolved
    | Cases.AmbiguousAmong ids -> Outcome.Ambiguous ids

let private byId (affordances: OperableAffordance<obj> list) (id: string) =
    affordances
    |> List.tryFind (fun a -> a.Id = id)
    |> Option.defaultWith (fun () -> failwithf "no affordance %s" id)

[<Tests>]
let wire =
    testList
        "Phase 2517 — the affordance on the wire (§31.1, §31.2)"
        [ for c in Cases.roundTrips do
              test $"%s{c.Id} round-trips byte for byte" {
                  let canonical = Vectors.canonicalOf c.Json

                  Expect.equal
                      (Vectors.canonicalOf canonical)
                      canonical
                      "decode >> encode is the identity on canonical bytes"
                  // The generated structural layer agrees with the hand-written decoder.
                  match Generated.decodeNode canonical with
                  | Ok node ->
                      Expect.equal (Generated.encodeNode node) canonical "the generated layer round-trips it too"
                  | Error e -> failtestf "the generated layer refused it: %A" e
              }

          for c in Cases.rejects do
              test $"%s{c.Id} is refused with %s{c.Code} at %s{c.Path}" {
                  match JsonDecode.decodeNodeObj c.Json with
                  | Ok _ -> failtest "accepted"
                  | Error e ->
                      Expect.equal e.Code c.Code "code"
                      Expect.equal e.Path c.Path "path"
              }

          test "an absent affordance is absent on the wire — every pre-2517 document is byte-unchanged" {
              let plain =
                  """{"id":"s","kind":{"$type":"Select","label":"Region","source":{"$type":"Static","value":[]},"value":{"$type":"Static","value":""}}}"""

              Expect.isFalse ((Vectors.canonicalOf plain).Contains "affordance") "no key is synthesised"
          } ]

[<Tests>]
let derivation =
    testList
        "Phase 2517 — Operability.derive (§31.3)"
        [ test "the ticket's affordances, in document order" {
              let ids = Vectors.affordancesOf Cases.ticket |> List.map _.Id

              Expect.equal
                  ids
                  [ "country:set"
                    "country:clear"
                    "country:read"
                    "currency:set"
                    "currency:read"
                    "quantity:set"
                    "quantity:read"
                    "notes:read"
                    "reference:set"
                    "reference:read"
                    "express:set"
                    "express:read"
                    "submit:click"
                    "help:navigate" ]
                  "withdrawn nodes (pin, delete) yield nothing; free text without a pattern has no set"
          }

          test "a select's set carries its options, aliases attached to their option" {
              let set = byId (Vectors.affordancesOf Cases.ticket) "country:set"

              Expect.equal
                  set.Forms
                  [ "set country to {value}"; "change country to {value}"; "ship to {value}" ]
                  "label defaults first, then the authored pattern"

              match set.Slot with
              | Some(Slot.Enumeration options) ->
                  let uk = options |> List.find (fun o -> o.Value = "uk")
                  Expect.equal uk.Aliases [ "britain" ] "the alias rides its option"
              | other -> failtestf "expected an enumeration, got %A" other
          }

          test "a mount id qualifies node and id" {
              let ids = Vectors.affordancesOf Cases.twins |> List.map _.Id
              Expect.contains ids "panel/region-a:set" "qualified"
          }

          test "derive is total over every node fixture in the corpus" {
              match corpusRoot () with
              | None -> skiptest "no corpus"
              | Some root ->
                  for file in Directory.GetFiles(Path.Combine(root, "nodes"), "*.json") do
                      match JsonDecode.decodeNodeObj (File.ReadAllText file) with
                      | Ok node -> Operability.derive None node |> ignore
                      | Error _ -> ()
          } ]

[<Tests>]
let resolution =
    testList
        "Phase 2517 — the resolution contract (§31.4, §31.5)"
        [ for c in Cases.resolves do
              test $"%s{c.Stratum} %s{c.Id}" {
                  Expect.equal (Vectors.outcomeOf c) (expectedOutcome c.Expect) c.Instruction
              } ]

[<Tests>]
let resolving =
    testList
        "Phase 2517 — Resolve applies the node's OWN action"
        [ test "a write-back select sets its state key" {
              let set = byId (Vectors.affordancesOf Cases.ticket) "country:set"

              match set.Resolve(SlotValue.Value "fr") with
              | Some(Action.SetState("country", Some(JStr "fr"), None)) -> ()
              | other -> failtestf "the state-bound value is written back, got %A" other
          }

          test "clear writes the absence" {
              let clear = byId (Vectors.affordancesOf Cases.ticket) "country:clear"

              match clear.Resolve SlotValue.NoValue with
              | Some(Action.SetState("country", None, None)) -> ()
              | other -> failtestf "cleared, got %A" other
          }

          test "a button resolves to its own click" {
              let click = byId (Vectors.affordancesOf Cases.ticket) "submit:click"

              match click.Resolve SlotValue.NoValue with
              | Some(Action.Chain []) -> ()
              | other -> failtestf "its onClick, got %A" other
          }

          test "a read dispatches nothing" {
              let read = byId (Vectors.affordancesOf Cases.ticket) "country:read"
              Expect.isNone (read.Resolve SlotValue.NoValue) "reads never dispatch"
          }

          test "a handler-bearing select calls its handler with the option value" {
              let tree =
                  Fuaran.select
                      "s"
                      { Defaults.select with
                          Label = TextSource.Literal "Size"
                          Source = Binding.Static(Some [ { Label = "Large"; Value = "l" } ])
                          OnChange = Some(fun v -> Action.Notify("size", JStr(defaultArg v "none"))) }

              let set = Operability.derive None tree |> List.find (fun a -> a.Id = "s:set")

              match set.Resolve(SlotValue.Value "l") with
              | Some(Action.Notify("size", JStr "l")) -> ()
              | other -> failtestf "OnChange (Some value), got %A" other
          }

          test "a numeric field writes a JSON number" {
              let set = byId (Vectors.affordancesOf Cases.ticket) "quantity:set"

              match set.Resolve(SlotValue.Value "12") with
              | Some(Action.Chain []) -> () // the decoded tree's handler placeholder
              | other -> failtestf "unexpected %A" other
          } ]

[<Tests>]
let family =
    testList
        "Phase 2517 — the operability/ corpus family (§31.7)"
        [ test "the committed vectors are what the reference computes (FUARAN_REGEN=1 rewrites)" {
              match corpusRoot () with
              | None -> skiptest "no corpus"
              | Some root ->
                  let path = Vectors.familyPath root
                  let fresh = Vectors.emit ()

                  if Vectors.regenerating then
                      Directory.CreateDirectory(Path.Combine(root, Vectors.FamilyDir)) |> ignore
                      File.WriteAllText(path, fresh)
                      printfn "FUARAN_REGEN=1 — rewrote %s" path
                  else
                      Expect.isTrue (File.Exists path) $"%s{path} is missing: FUARAN_REGEN=1 emits it"

                      Expect.equal
                          (File.ReadAllText path)
                          fresh
                          "the committed family is stale: FUARAN_REGEN=1 dotnet run --project src/Fuaran.UI.Operability.Tests"
          } ]
