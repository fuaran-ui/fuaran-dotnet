module Fuaran.UI.Tests.Affordances

// ============================================================================
//  The declared-affordance provider seam + the relay's `read.affordances`
//  entry point (`relay@1.1`), pinned on .NET.
//
//  Two halves, and they answer different questions:
//
//   * THE SEAM (`Affordances.fs`) — registration, removal, provider ordering,
//     the module filter, and the three properties `enumerate` owns on every
//     provider's behalf: total, filtered, isolated. None of this needs a
//     browser, a tree, or a host.
//
//   * THE VERB (`Relay.fs` §7.6) — that a page with nothing registered answers
//     an EMPTY, WELL-FORMED enumeration rather than an error; that a declared
//     enumeration projects onto the wire with every part the contract names;
//     that an unknown module id is answered with `[]` rather than a refusal
//     (deny-by-absence: a refusal would tell a client that a withheld module
//     exists); and that the minor bump is negotiated rather than imposed.
//
//  The negotiation tests are the ones worth reading twice. Advancing a peer's
//  profile is only additive if a client that predates the advance is still
//  SERVED — so `hello` with `accepts: ["relay@1.0"]` must answer `relay@1.0`
//  and must NOT advertise the 1.1 entry point, and a 1.0 envelope asking for
//  that entry point must be refused exactly as a genuine 1.0 peer would refuse
//  it. Without those, a minor bump silently breaks every existing client, which
//  is the failure the version rules exist to prevent.
//
//  Sequenced: the provider registry is page-global by design (a host registers
//  once at install, the surface is rebuilt every render), so these tests must
//  not interleave with each other.
// ============================================================================

open Expecto
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Relay

// ─── A miniature declared enumeration ───────────────────────────────────────
//
// One module, three fields chosen to cover the three `ValueHint` shapes plus
// the read-only posture — the axes a projection can get wrong independently.

let private countryField: Affordances.FieldAffordance =
    { Id = "country"
      Shape = Affordances.FieldShape.Choice
      Controllable = true
      Commands =
        [ { Phrase = "set country to {value}"
            Effect = Affordances.CommandEffect.Write }
          { Phrase = "what is the country"
            Effect = Affordances.CommandEffect.Read } ]
      Aliases = [ "uk", "United Kingdom" ]
      Values = Some(Affordances.ValueHint.OneOf [ "United Kingdom"; "France" ])
      Description = Some "one of: United Kingdom, France" }

let private weeksField: Affordances.FieldAffordance =
    { Id = "weeks"
      Shape = Affordances.FieldShape.Number
      Controllable = true
      Commands =
        [ { Phrase = "set weeks to {value}"
            Effect = Affordances.CommandEffect.Write } ]
      Aliases = []
      // A half-open bound: `max` is declared, `min` is not. The projection must
      // OMIT the open end rather than emit a null for it.
      Values = Some(Affordances.ValueHint.NumberRange(None, Some 52.0, Some 1.0))
      Description = None }

let private noteField: Affordances.FieldAffordance =
    { Id = "note"
      Shape = Affordances.FieldShape.Text
      // Published but not settable: an agent may ASK about it and may not drive
      // it. Distinct from a field that is withheld entirely, which does not
      // appear at all.
      Controllable = false
      Commands =
        [ { Phrase = "what is the note"
            Effect = Affordances.CommandEffect.Read } ]
      Aliases = []
      Values = Some(Affordances.ValueHint.TextLength(Some 1, Some 240))
      Description = None }

let private salesModule: Affordances.ModuleAffordance =
    { Id = "sales"
      Active = true
      Fields = [ countryField; weeksField; noteField ]
      Commands =
        [ { Phrase = "go to sales"
            Effect = Affordances.CommandEffect.Navigate } ] }

let private inventoryModule: Affordances.ModuleAffordance =
    { Id = "inventory"
      Active = false
      Fields = []
      Commands = [] }

let private declaring (modules: Affordances.ModuleAffordance list) : Affordances.AffordanceProvider =
    fun _ -> { Modules = modules }

// ─── A surface with no tree ─────────────────────────────────────────────────
//
// `read.affordances` reads the provider registry, never the tree, so the test
// surface supplies the registry leg honestly and stubs the rest. Building it
// directly (rather than through `surfaceOf`) keeps the test about the verb.

let private emptyTree: DebugGlobal.TreeIntrospection =
    { Id = "root"
      Kind = "Box"
      Bindings = []
      ChildIds = []
      Children = [] }

let private testSurface: RelaySurface =
    { SurfaceVersion = DebugGlobal.Version
      CanApply = false
      TreeRevision = fun () -> "r-1"
      Subscribe = fun _ -> (fun () -> ())
      NodeState = fun _ -> None
      InspectTree = fun () -> emptyTree
      ResolveSlot = fun _ _ -> SlotLookup.NodeMissing
      Geometry = fun _ -> None
      FindNodes = fun _ -> []
      Affordances = Affordances.enumerate
      Apply = fun _ -> DebugGlobal.ApplyResult.Unwired "read-only" }

let private peer () =
    Relay.createPeer
        (fun () -> Some testSurface)
        { RelayOptions.defaults with
            OptedIn = true
            HostVersion = "0.32.0" }

let private requestAt (profile: string) (id: string) (requestType: string) (payload: (string * RelayValue) list) =
    RelayValue.Obj
        [ Relay.RelayKey, RelayValue.Str profile
          "dir", RelayValue.Str "request"
          "id", RelayValue.Str id
          "type", RelayValue.Str requestType
          "payload", RelayValue.Obj payload ]

let private request id requestType payload =
    requestAt Relay.Profile id requestType payload

let private answer (message: RelayValue) : RelayValue =
    match (peer ()).Handle message with
    | Some reply -> reply
    | None -> failtest "the peer answered a verified request with silence"

let private payloadOf (reply: RelayValue) : RelayValue =
    match RelayValue.field "payload" reply with
    | Some p -> p
    | None -> failtest "a response always carries a payload object (§4)"

let private modulesOf (reply: RelayValue) : RelayValue list =
    match RelayValue.field "modules" (payloadOf reply) |> Option.bind RelayValue.asList with
    | Some items -> items
    | None -> failtest "read.affordances.ok carries a modules array"

let private refusalClassOf (reply: RelayValue) : string option =
    RelayValue.stringField "class" (payloadOf reply)

let private stringsOf (value: RelayValue option) : string list =
    match value |> Option.bind RelayValue.asList with
    | Some items -> items |> List.choose RelayValue.asString
    | None -> []

// ─── The suite ──────────────────────────────────────────────────────────────

[<Tests>]
let tests =
    testSequenced
    <| testList
        "Affordances"
        [

          // ── the seam ──────────────────────────────────────────────────────

          test "a page with no registered provider enumerates empty, not absent" {
              Affordances.clearProviders ()

              Expect.equal (Affordances.providerCount ()) 0 "nothing is registered"

              Expect.equal
                  (Affordances.enumerate None)
                  Affordances.AffordanceEnumeration.empty
                  "the empty enumeration is a well-formed answer, not an error condition"
          }

          test "a registered provider is enumerated, and its handle removes it" {
              Affordances.clearProviders ()
              let remove = Affordances.registerProvider (declaring [ salesModule ])

              Expect.equal (Affordances.providerCount ()) 1 "one provider"

              Expect.equal
                  ((Affordances.enumerate None).Modules |> List.map _.Id)
                  [ "sales" ]
                  "the provider's modules are what the seam reports"

              remove ()

              Expect.equal (Affordances.enumerate None) Affordances.AffordanceEnumeration.empty "removal is complete"

              // Removing twice must not disturb anything — a teardown that runs
              // on both an effect cleanup and an unload is ordinary.
              remove ()
              Expect.equal (Affordances.providerCount ()) 0 "a second removal is harmless"
          }

          test "providers compose, and the FIRST declaration of a module id wins" {
              Affordances.clearProviders ()

              let overriding = { inventoryModule with Active = true }

              Affordances.registerProvider (declaring [ salesModule; inventoryModule ])
              |> ignore

              Affordances.registerProvider (declaring [ overriding ]) |> ignore

              let modules = (Affordances.enumerate None).Modules

              Expect.equal (modules |> List.map _.Id) [ "sales"; "inventory" ] "declared order is preserved"

              Expect.equal
                  (modules
                   |> List.tryPick (fun m -> if m.Id = "inventory" then Some m.Active else None))
                  (Some false)
                  "the earlier registration owns the id; the later one does not shadow it"
          }

          test "the module filter is applied to the union, not left to the provider" {
              Affordances.clearProviders ()

              // A provider that IGNORES the hint entirely — the narrowing must
              // still hold, so a provider cannot answer wider than it was asked.
              Affordances.registerProvider (fun _ -> { Modules = [ salesModule; inventoryModule ] })
              |> ignore

              Expect.equal
                  ((Affordances.enumerate (Some "inventory")).Modules |> List.map _.Id)
                  [ "inventory" ]
                  "only the requested module survives"

              Expect.equal
                  (Affordances.enumerate (Some "no-such-module"))
                  Affordances.AffordanceEnumeration.empty
                  "an unknown id is empty, which is also what a WITHHELD id looks like — deliberately"
          }

          test "a throwing provider is skipped and the others still answer" {
              Affordances.clearProviders ()

              Affordances.registerProvider (fun _ -> failwith "this provider is broken")
              |> ignore

              Affordances.registerProvider (declaring [ salesModule ]) |> ignore

              Expect.equal
                  ((Affordances.enumerate None).Modules |> List.map _.Id)
                  [ "sales" ]
                  "one badly-behaved registration must not take down the read"
          }

          // ── the verb ──────────────────────────────────────────────────────

          test "read.affordances on a page with no provider is an empty ok, never a refusal" {
              Affordances.clearProviders ()

              let reply = answer (request "c-1" "read.affordances" [])

              Expect.equal
                  (RelayValue.stringField "type" reply)
                  (Some "read.affordances.ok")
                  "a non-declaring page answers successfully"

              Expect.equal (modulesOf reply) [] "with an empty modules array"
          }

          test "read.affordances projects every part of a declaration" {
              Affordances.clearProviders ()
              Affordances.registerProvider (declaring [ salesModule ]) |> ignore

              let modules = modulesOf (answer (request "c-2" "read.affordances" []))
              Expect.equal (List.length modules) 1 "one module"
              let sales = modules[0]

              Expect.equal (RelayValue.stringField "id" sales) (Some "sales") "module id"
              Expect.equal (RelayValue.field "active" sales) (Some(RelayValue.Bool true)) "active flag"

              let moduleCommands = stringsOf (RelayValue.field "commands" sales) // shape check below
              Expect.equal moduleCommands [] "module commands are objects, not bare strings"

              match RelayValue.field "commands" sales |> Option.bind RelayValue.asList with
              | Some [ command ] ->
                  Expect.equal (RelayValue.stringField "phrase" command) (Some "go to sales") "module-level phrase"

                  Expect.equal (RelayValue.stringField "effect" command) (Some "navigate") "module-level effect token"
              | other -> failtestf "expected one module-level command, got %A" other

              let fields =
                  match RelayValue.field "fields" sales |> Option.bind RelayValue.asList with
                  | Some f -> f
                  | None -> failtest "a module carries a fields array"

              Expect.equal (List.length fields) 3 "three declared fields"

              // country — the closed set, the alias map, the slot syntax
              let country = fields[0]
              Expect.equal (RelayValue.stringField "id" country) (Some "country") "field id"
              Expect.equal (RelayValue.stringField "shape" country) (Some "choice") "field shape token"

              Expect.equal
                  (RelayValue.field "controllable" country)
                  (Some(RelayValue.Bool true))
                  "controllability is reported explicitly"

              match RelayValue.field "commands" country |> Option.bind RelayValue.asList with
              | Some [ write; read ] ->
                  Expect.equal
                      (RelayValue.stringField "phrase" write)
                      (Some "set country to {value}")
                      "the slot syntax is carried VERBATIM — it is what tells an agent where its words go"

                  Expect.equal (RelayValue.stringField "effect" write) (Some "write") "write effect"
                  Expect.equal (RelayValue.stringField "effect" read) (Some "read") "read effect"
              | other -> failtestf "expected two declared commands, got %A" other

              match RelayValue.field "aliases" country |> Option.bind RelayValue.asList with
              | Some [ alias ] ->
                  Expect.equal (RelayValue.stringField "alias" alias) (Some "uk") "alias key"
                  Expect.equal (RelayValue.stringField "value" alias) (Some "United Kingdom") "canonical value"
              | other -> failtestf "expected one alias pair, got %A" other

              match RelayValue.field "values" country with
              | Some values ->
                  Expect.equal (RelayValue.stringField "kind" values) (Some "oneOf") "value-hint discriminator"

                  Expect.equal
                      (stringsOf (RelayValue.field "values" values))
                      [ "United Kingdom"; "France" ]
                      "the closed set, in declared order"
              | None -> failtest "a bounded field carries a values hint"

              Expect.equal
                  (RelayValue.stringField "description" country)
                  (Some "one of: United Kingdom, France")
                  "the human-readable note rides along"

              // weeks — the half-open numeric bound
              let weeks = fields[1]

              match RelayValue.field "values" weeks with
              | Some values ->
                  Expect.equal (RelayValue.stringField "kind" values) (Some "numberRange") "numeric hint"
                  Expect.equal (RelayValue.field "max" values) (Some(RelayValue.Num 52.0)) "declared upper bound"
                  Expect.equal (RelayValue.field "step" values) (Some(RelayValue.Num 1.0)) "declared step"

                  Expect.isNone
                      (RelayValue.field "min" values)
                      "the OPEN end is omitted, not nulled — a null reads as a bound that must be handled"
              | None -> failtest "a bounded field carries a values hint"

              Expect.isNone
                  (RelayValue.field "description" weeks)
                  "an undeclared description is absent rather than null"

              // note — published, read-only
              let note = fields[2]

              Expect.equal
                  (RelayValue.field "controllable" note)
                  (Some(RelayValue.Bool false))
                  "a published-but-not-settable field says so; a field that must not be touched at all is ABSENT"

              match RelayValue.field "values" note with
              | Some values ->
                  Expect.equal (RelayValue.stringField "kind" values) (Some "textLength") "length hint"
                  Expect.equal (RelayValue.field "minLength" values) (Some(RelayValue.Num 1.0)) "declared min length"
                  Expect.equal (RelayValue.field "maxLength" values) (Some(RelayValue.Num 240.0)) "declared max length"
              | None -> failtest "a length-bounded field carries a values hint"
          }

          test "read.affordances narrows to one module, and an unknown id is empty rather than refused" {
              Affordances.clearProviders ()

              Affordances.registerProvider (declaring [ salesModule; inventoryModule ])
              |> ignore

              let scoped =
                  answer (request "c-3" "read.affordances" [ "moduleId", RelayValue.Str "inventory" ])

              Expect.equal
                  (modulesOf scoped |> List.choose (RelayValue.stringField "id"))
                  [ "inventory" ]
                  "module-scoped read"

              let unknown =
                  answer (request "c-4" "read.affordances" [ "moduleId", RelayValue.Str "_withheld" ])

              Expect.equal
                  (RelayValue.stringField "type" unknown)
                  (Some "read.affordances.ok")
                  "an unknown module is a legitimate question with the answer 'none'"

              Expect.equal (modulesOf unknown) [] "and a refusal here would disclose that a withheld module exists"
          }

          test "a non-string moduleId is a client defect and IS refused" {
              Affordances.clearProviders ()

              let reply =
                  answer (request "c-5" "read.affordances" [ "moduleId", RelayValue.Num 7.0 ])

              Expect.equal (RelayValue.stringField "type" reply) (Some "refusal") "refused"
              Expect.equal (refusalClassOf reply) (Some "MALFORMED_MESSAGE") "a wrong JSON type is malformed (§9.3)"
          }

          // ── the minor bump is negotiated, not imposed ─────────────────────

          test "a relay@1.0 client is served at relay@1.0 and is not offered the 1.1 entry point" {
              Affordances.clearProviders ()
              Affordances.registerProvider (declaring [ salesModule ]) |> ignore

              let reply =
                  answer (
                      requestAt
                          "relay@1.0"
                          "c-6"
                          "hello"
                          [ "client", RelayValue.Str "test"
                            "accepts", RelayValue.Arr [ RelayValue.Str "relay@1.0" ] ]
                  )

              Expect.equal
                  (RelayValue.stringField "type" reply)
                  (Some "hello.ok")
                  "an older client is SERVED, not refused"

              Expect.equal
                  (RelayValue.stringField "profile" (payloadOf reply))
                  (Some "relay@1.0")
                  "the session profile is the one the client accepted"

              Expect.isFalse
                  (stringsOf (RelayValue.field "capabilities" (payloadOf reply))
                   |> List.contains "read.affordances")
                  "naming an entry point the client's own profile does not define tells it something it cannot use"
          }

          test "a relay@1.0 envelope asking for the 1.1 entry point is CAPABILITY_ABSENT" {
              Affordances.clearProviders ()
              Affordances.registerProvider (declaring [ salesModule ]) |> ignore

              let reply = answer (requestAt "relay@1.0" "c-7" "read.affordances" [])

              Expect.equal (RelayValue.stringField "type" reply) (Some "refusal") "refused"

              Expect.equal
                  (refusalClassOf reply)
                  (Some "CAPABILITY_ABSENT")
                  "the same answer a genuine 1.0 peer gives — the outcome must not depend on which peer received it"
          }

          test "a relay@1.1 client is served at relay@1.1 with the entry point advertised" {
              Affordances.clearProviders ()

              let reply =
                  answer (
                      requestAt
                          "relay@1.1"
                          "c-8"
                          "hello"
                          [ "client", RelayValue.Str "test"
                            "accepts", RelayValue.Arr [ RelayValue.Str "relay@1.0"; RelayValue.Str "relay@1.1" ] ]
                  )

              Expect.equal
                  (RelayValue.stringField "profile" (payloadOf reply))
                  (Some "relay@1.1")
                  "the HIGHEST mutually-speakable profile is chosen, not the first listed"

              Expect.isTrue
                  (stringsOf (RelayValue.field "capabilities" (payloadOf reply))
                   |> List.contains "read.affordances")
                  "and the 1.1 entry point is advertised at 1.1"
          }

          test "a foreign major is still refused" {
              Affordances.clearProviders ()

              let reply =
                  answer (
                      requestAt
                          "relay@2.0"
                          "c-9"
                          "hello"
                          [ "client", RelayValue.Str "test"
                            "accepts", RelayValue.Arr [ RelayValue.Str "relay@2.0" ] ]
                  )

              Expect.equal
                  (refusalClassOf reply)
                  (Some "FOREIGN_PROFILE")
                  "widening the minor must not widen the MAJOR check"
          } ]
