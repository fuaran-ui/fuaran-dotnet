module Fuaran.UI.Program.Server.Tests.HarvestTests

// ─── The harvest ─────────────────────────────────────────────────────
//
// The pipeline this covers already existed and was already tested piecewise.
// What is new is that ONE call goes from a program and its registration to the
// bytes, and the properties worth pinning are the ones a consumer of those
// bytes relies on and could not check for itself:
//
//  1. DETERMINISM. The same program over the same registration produces
//     byte-identical output, on repeated calls and under permuted inputs. This
//     is what makes a content address an address rather than a timestamp: an
//     encoder that reordered anything would move the address for reasons that
//     are not changes, and an alarm that fires without a cause is one people
//     learn to silence.
//
//  2. THE DOCUMENT AND THE PROJECTION ARE ONE FACT. The bytes decode back to
//     the projection returned beside them. They are derived in one place, so
//     this cannot fail by construction — which is why it is worth asserting:
//     it is the invariant a later refactor breaks while everything still
//     compiles.
//
//  3. THE TWO ENTRY POINTS ANSWER DIFFERENT QUESTIONS. `ofRegistration` is not
//     `ofProgram` with an empty tree: it describes what a registration could be
//     asked for by any program that reaches all of it, which is a ceiling, and
//     `ofProgram` describes one program's reach into it. A test that treated
//     them as the same call would be pinning the wrong claim.
//
//  4. THE DETERMINISM CAN GO RED. A deliberately un-normalised projection —
//     the same demands in another order — is shown to encode to different
//     bytes, so property 1 is a measurement rather than a tautology about a
//     function that happens to be pure.

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.Program.Bounded
open Fuaran.UI.Program
open Fuaran.Program.Server
open Fuaran.UI.Program.Server

// ─── fixtures ────────────────────────────────────────────────────────

let private jstr (s: string) = Fuaran.Core.JStr s

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

/// A handler reaching two host functions and a channel — enough arms that an
/// ordering defect anywhere in the encoder would show.
let private busy (name: string) : Handler =
    { Name = name
      Stages =
        [ HandlerStage.Effect(ServerEffect.HostCall("sendMail", jstr "x", None))
          HandlerStage.Effect(ServerEffect.HostCall("charge", jstr "y", None))
          HandlerStage.Effect(ServerEffect.Notify("overdue", jstr "n"))
          HandlerStage.Effect(ServerEffect.ApplyOps []) ] }

let private registration = Map.ofList [ "/handlers/work", busy "/handlers/work" ]

// ─── the suite ───────────────────────────────────────────────────────

[<Tests>]
let harvestTests =
    testList
        "the harvest"
        [ test "the same program over the same registration harvests byte-identical bytes" {
              let a = Harvest.ofProgram registration (treeCalling "/handlers/work")
              let b = Harvest.ofProgram registration (treeCalling "/handlers/work")

              Expect.equal
                  b.Document
                  a.Document
                  "two harvests of one program are the same document — the property an address rests on"

              Expect.isGreaterThan a.Document.Length 0 "and it is a document rather than nothing"
          }

          test "and permuting the registration's construction order changes nothing" {
              // The registration is a map, so its ITERATION order is not the
              // order it was built in — but a handler's own stage list is
              // ordered, and the demands derived from several handlers are
              // collected before they are sorted. This is the case where a
              // fold that forgot to normalise would still pass a
              // call-it-twice test.
              let two =
                  Map.ofList [ "/handlers/a", busy "/handlers/a"; "/handlers/b", busy "/handlers/b" ]

              let twoReversed =
                  Map.ofList [ "/handlers/b", busy "/handlers/b"; "/handlers/a", busy "/handlers/a" ]

              Expect.equal
                  (Harvest.ofRegistration (Map.values twoReversed)).Document
                  (Harvest.ofRegistration (Map.values two)).Document
                  "the same registration built in either order publishes one document"
          }

          test "the document decodes back to the projection returned beside it" {
              let harvested = Harvest.ofProgram registration (treeCalling "/handlers/work")

              Expect.equal
                  (Demanded.decode harvested.Document)
                  (Ok harvested.Projection)
                  "the bytes and the value are one fact in two forms — this is what a later refactor breaks while still compiling"
          }

          test "a harvested document is the pinned reader's, not merely well-formed-looking" {
              // `Demanded.decode` refuses an undeclared member, a version it
              // does not read, and a document whose lists are not canonical. A
              // harvest that produced any of those would be publishing bytes
              // no conformant consumer can read, and the failure would land on
              // the consumer.
              for harvested in
                  [ Harvest.ofProgram registration (treeCalling "/handlers/work")
                    Harvest.ofProgram registration (treeCalling "/handlers/absent")
                    Harvest.ofRegistration [] ] do
                  Expect.isOk (Demanded.decode harvested.Document) "every harvest is readable by the pinned reader"
          }

          test "the two entry points answer DIFFERENT questions" {
              let program = Harvest.ofProgram registration (treeCalling "/handlers/work")
              let surface = Harvest.ofRegistration (Map.values registration)

              Expect.notEqual
                  surface.Document
                  program.Document
                  "a registration's ceiling is not a program's reach into it — collapsing them would publish one document for two claims"

              // The program's document carries the tree's own tier as well; the
              // registration's does not, because there is no tree.
              Expect.isSome program.Projection.Server "the program harvest walked a registration"
              Expect.isSome surface.Projection.Server "and so did the surface harvest"
          }

          test "a program reaching NO registered handler still publishes an honest document" {
              // Not an empty document and not a refusal: a tree that names an
              // endpoint nothing is registered under reaches no handler, so the
              // server tier is present (a walk ran) and demands nothing. That
              // is a different fact from "not asked", and the encoding keeps
              // them apart.
              let harvested = Harvest.ofProgram registration (treeCalling "/handlers/nobody")

              match harvested.Projection.Server with
              | None -> failtest "a walk ran, so the tier must be present"
              | Some tier ->
                  Expect.isEmpty tier.Functions "no reachable handler names a host function"
                  Expect.isEmpty tier.Replay "and none contributes a posture"
          }

          test "…and the determinism CAN GO RED — the probe that proves it is not a tautology" {
              // The mistake this guards: publishing a projection that was
              // assembled rather than normalised. Purity alone would not save
              // it — the function is pure either way — so the probe builds the
              // same demands in two orders and encodes them WITHOUT going
              // through the normalising constructor.
              let tier (fns: ServerFunctionDemand list) : DemandedProjection =
                  { Effects = []
                    HostCalls = []
                    StateNamespaces = []
                    OpaqueHandlers = []
                    Server =
                      Some
                          { Effects = []
                            Capabilities = []
                            Functions = fns
                            Channels = []
                            Reach = []
                            Replay = []
                            Undo = []
                            Constraints = [] } }

              let a =
                  { Function = "a"
                    Capability = "host:a" }

              let b =
                  { Function = "b"
                    Capability = "host:b" }

              Expect.notEqual
                  (Demanded.encode (tier [ b; a ]))
                  (Demanded.encode (tier [ a; b ]))
                  "un-normalised, the same demands in two orders encode differently — so the harvest's sameness above is bought by the normalisation and not by the encoder being a function"

              Expect.equal
                  (Harvest.publish (tier [ b; a ])).Document
                  (Harvest.publish (tier [ a; b ])).Document
                  "and publishing normalises, which is what makes the two orders one document"
          } ]

// ─── The content address on a cross-layer reference ──────────────────
//
// The other half of the arc, and it lives beside the harvest deliberately: the
// harvest publishes bytes worth addressing and the reference is what addresses
// them, so a session changing one and not the other should be reading both.
//
// The corpus covers what a document can exhibit — the pinned round trip, and a
// malformed address refused. What it structurally cannot cover is here: that an
// ABSENT address survives a round trip as an absence rather than as an empty
// string, and that the digest's case is part of the value. Both are properties
// of the codec across a pair of runs rather than of any single document.

[<Tests>]
let contentAddressTests =
    let address = "sha256:" + String.replicate 64 "a"

    let roundTrip (reference: LogicTreeRef) =
        ProgramWire.encodeLogicTreeRef reference
        |> ProgramWire.render
        |> ProgramWire.parseDocument
        |> Result.bind ProgramWire.decodeLogicTreeRef

    testList
        "the cross-layer reference's content address"
        [ test "an unpinned reference round-trips as UNPINNED, and omits the member" {
              let unpinned = { Ref = "orders-logic"; Hash = None }

              Expect.equal (roundTrip unpinned) (Ok unpinned) "absence survives as absence"

              Expect.isFalse
                  ((ProgramWire.render (ProgramWire.encodeLogicTreeRef unpinned)).Contains "hash")
                  "and the member is omitted rather than emitted empty — an always-emitted member would change the bytes, and therefore the address, of every reference ever written without one"
          }

          test "a pinned reference round-trips, and the member sorts between the discriminator and the reference" {
              let pinned =
                  { Ref = "orders-logic"
                    Hash = Some address }

              Expect.equal (roundTrip pinned) (Ok pinned) "the address survives"

              Expect.stringStarts
                  (ProgramWire.render (ProgramWire.encodeLogicTreeRef pinned))
                  (sprintf "{\"$type\":\"LogicTreeRef\",\"hash\":\"%s\"" address)
                  "Ordinal order puts it there, and the corpus pins the same bytes"
          }

          test "a malformed address is refused for its own class, and absence is NOT malformed" {
              let doc (hash: string) =
                  sprintf
                      "{\"$type\":\"LogicTreeRef\",\"hash\":\"%s\",\"ref\":\"orders-logic\",\"slot\":\"fuaran.program/logic-tree\"}"
                      hash

              let refusalOf hash =
                  ProgramWire.parseDocument (doc hash)
                  |> Result.bind ProgramWire.decodeLogicTreeRef

              for bad, why in
                  [ "sha256:" + String.replicate 64 "A",
                    "UPPER-CASE hex — two spellings of one digest compare unequal, so admitting both would make a pin fail against the very document it addresses"
                    "sha256:" + String.replicate 63 "a",
                    "one digit short — a truncated claim is not a weaker claim, it is a different one"
                    "sha256:" + String.replicate 65 "a", "one digit long"
                    String.replicate 64 "a",
                    "no algorithm prefix — the algorithm rides IN the value so a later digest is a visible change"
                    "md5:" + String.replicate 64 "a", "an algorithm this version does not name"
                    "", "present and empty" ] do
                  match refusalOf bad with
                  | Error refusal ->
                      Expect.equal refusal.Class RefusalClass.MalformedContentAddress (sprintf "%s: %s" bad why)
                  | Ok _ -> failtestf "a malformed address must be refused (%s): %s" bad why

              Expect.isOk
                  (ProgramWire.parseDocument
                      """{"$type":"LogicTreeRef","ref":"orders-logic","slot":"fuaran.program/logic-tree"}"""
                   |> Result.bind ProgramWire.decodeLogicTreeRef)
                  "and OMITTING it is a posture, not a defect — collapsing the two would let a malformed value buy the unpinned treatment"
          }

          test "the widening admitted ONE member and nothing else" {
              // The mechanism §9.2 rests on, re-asserted after the declaration
              // moved. What retired is "the slot declares exactly one member";
              // what stayed is that the declaration decides, so every shape
              // nobody has thought of is still refused as undeclared.
              for claim in [ "body"; "semantics"; "digest"; "sha256"; "checksum" ] do
                  let doc =
                      sprintf
                          """{"$type":"LogicTreeRef","%s":"x","ref":"orders-logic","slot":"fuaran.program/logic-tree"}"""
                          claim

                  match ProgramWire.parseDocument doc |> Result.bind ProgramWire.decodeLogicTreeRef with
                  | Error refusal ->
                      Expect.equal
                          refusal.Class
                          RefusalClass.UndeclaredMember
                          (sprintf "'%s' is still undeclared" claim)
                  | Ok _ -> failtestf "'%s' must be refused as undeclared" claim
          } ]
