module Fuaran.Program.Bounded.Tests.DemandedDecodeTests

// ─── The demanded projection, read back ───
//
// `Demanded.encode` had no inverse, so every consumer that wanted one wrote its
// own reader against an envelope this repository is the only authority on. That
// is a drift risk with no detector: a reader that gets the envelope subtly wrong
// produces a projection that LOOKS like a projection, and the coverage verdict
// computed from it is wrong in a way nothing downstream can see.
//
// These tests pin four things, and the last two are what keep the first two
// honest:
//
//  1. THE ROUND TRIP, BOTH DIRECTIONS. `decode (encode p)` is `p` on the value,
//     and `encode` of a decoded document is the document's own bytes — for
//     documents with and without a server tier, with and without replay
//     postures, and across the host-call / namespace / opaque-handler variants.
//  2. WHAT IS REFUSED. An unrecognised version, an unrecognised kind, a missing
//     or undeclared or mistyped member, a null with no absence to erase to, and
//     a document whose lists are not canonical — each a TYPED failure naming the
//     member and the version rather than an exception.
//  3. THAT A WEAKER READER GOES RED. Two probes are named in the tests below and
//     were run destructively against a perturbed decoder: one tolerating an
//     unrecognised version, one dropping an effect discriminator it does not
//     know. Both turn tests here red. A round-trip suite that only ever meets a
//     correct reader cannot tell a decoder from a decoder-shaped hole.
//  4. THAT IT IS TOTAL. No input throws — the whole point of a typed failure is
//     that a caller holding untrusted bytes never has to guard the call.

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.Program.Bounded
open Fuaran.Program.UI

// ─── fixtures ───────────────────────────────────────────────────────

let private btn (id: string) (action: Action<obj>) : Node<obj> =
    Fuaran.button
        id
        { Defaults.button<obj> with
            OnClick = action }

let private dash (children: Node<obj> list) : Node<obj> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<obj> with
            Children = children }

let private jstr (s: string) : Fuaran.Core.JVal = Fuaran.Core.JStr s

/// A tree exercising every client-tier member at once: the three effect arms,
/// all four host-call channels, a written and a read namespace, and a
/// closure-held handler the walk can only report as opaque.
let private richTree: Node<obj> =
    let valueFrom: Binding<Fuaran.Core.JVal> = Binding.State("filters.region", None)

    let select =
        Fuaran.select
            "region"
            { Defaults.select<obj> with
                Label = TextSource.Literal "Region" }

    dash
        [ select
          btn "b1" (Action.Navigate(TextSource.Literal "/a", NavigateTarget.Self))
          btn
              "b2"
              (Action.Chain
                  [ Action.WriteToClipboard(TextSource.Literal "x")
                    Action.ReadFileBody("upload", None, FileReadEncoding.Text, None)
                    Action.Call("/api/orders", None, None)
                    Action.Invoke("cap.print", [])
                    Action.Notify("audit", jstr "n")
                    Action.AiTool("summarise", jstr "s")
                    Action.SetState("cart.total", None, Some valueFrom) ]) ]

/// A server tier naming a function, two channels and three capabilities —
/// already in the canonical order the document promises.
let private serverTier: ServerDemand =
    { Effects = [ "ApplyOps"; "HostCall"; "Notify"; "RunQuery" ]
      Capabilities = [ "ApplyOps"; "Notify"; "RunQuery" ]
      Functions =
        [ { Function = "sendMail"
            Capability = "hostFunction.sendMail" } ]
      Channels =
        [ { Channel = "Notify"; Name = "ops" }
          { Channel = "RunQuery"
            Name = "orders" } ]
      Reach = []
      Replay = []
      Undo = []
      Constraints = [] }

/// A tier that was walked and demands nothing — the document's other server
/// fact, and the one `None` must never be confused with.
let private emptyTier: ServerDemand =
    { Effects = []
      Capabilities = []
      Functions = []
      Channels = []
      Reach = []
      Replay = []
      Undo = []
      Constraints = [] }

/// One posture of each grade, including the `unknown` one whose reasons are the
/// whole reason postures carry reasons at all.
let private postures: ReplayPosture list =
    [ { Handler = "/api/a"
        Safety = "safe"
        Reasons = [] }
      { Handler = "/api/b"
        Safety = "unknown"
        Reasons =
          [ { Stage = 0
              Defect = "non-literal-write" }
            { Stage = 2
              Defect = "relative-addressing" } ] }
      { Handler = "/api/c"
        Safety = "unsafe"
        Reasons =
          [ { Stage = 1
              Defect = "opaque-host-call" } ] } ]

/// What a verb's ops reach — a path, a remote, and the destination class the
/// op witness put the remote under — already in canonical order (Phase 1967).
let private reach: OpReachDemand list =
    [ { Capability = "ApplyOps"
        Argument = "destination"
        Name = "origin" }
      { Capability = "ApplyOps"
        Argument = "path"
        Name = "notes/x.md" }
      { Capability = "ApplyOps"
        Argument = "target"
        Name = "origin" }
      { Capability = "EmitPatch"
        Argument = "target"
        Name = "spinner" } ]

/// The six documents the round trip is claimed over.
let private corpus: (string * DemandedProjection) list =
    [ "the empty projection", Demanded.empty
      "a server tier carrying op reach",
      Demanded.withServer { serverTier with Reach = reach } (Demanded.ofTree richTree)
      "a client tier only", Demanded.ofTree richTree
      "a server tier that walked and found nothing", Demanded.withServer emptyTier (Demanded.ofTree richTree)
      "a server tier with functions, channels and capabilities",
      Demanded.withServer serverTier (Demanded.ofTree richTree)
      "a server tier carrying replay postures",
      Demanded.withServer { serverTier with Replay = postures } (Demanded.ofTree richTree) ]

let private decoded (json: string) : DemandedProjection =
    match Demanded.decode json with
    | Ok projection -> projection
    | Error failure -> failtestf "expected a projection, got %A at '%s'" failure.Defect failure.Field

let private refusal (json: string) : DemandedDecodeFailure =
    match Demanded.decode json with
    | Ok _ -> failtest "expected a refusal, got a projection"
    | Error failure -> failure

/// A canonical current-version document, built from a real emission so a refusal test
/// cannot drift from the envelope by hand-spelling it.
let private emitted: string = Demanded.encode (Demanded.ofTree richTree)

let private replacing (find: string) (replace: string) : string = emitted.Replace(find, replace)

// ─── the round trip, both directions ────────────────────────────────

let private roundTrips: Test list =
    corpus
    |> List.collect (fun (name, projection) ->
        [ test $"encode then decode is the identity on the value — {name}" {
              Expect.equal (decoded (Demanded.encode projection)) projection "the projection survives its own document"
          }

          test $"decode then encode is the identity on the canonical bytes — {name}" {
              let json = Demanded.encode projection
              Expect.equal (Demanded.encode (decoded json)) json "the document survives being read"
          } ])

// ─── what the round trip must keep apart, and what it must refuse ───

let private behaviour: Test list =
    [ test "the two server-tier facts stay distinguishable across the round trip" {
          // The distinction the version numbers exist to protect: `None` says no
          // server walk was performed, an EMPTY tier says one ran and the
          // reachable handlers demand nothing. A reader that collapsed them
          // would read "not asked" as "asked, and the answer was nothing", which
          // is the reading a coverage check must never make — the read side of
          // exactly the argument the `server` key's always-present-ness makes on
          // the write side.
          let notAsked = Demanded.ofTree richTree
          let askedAndEmpty = Demanded.withServer emptyTier notAsked

          Expect.isNone (decoded (Demanded.encode notAsked)).Server "null reads back as 'no walk ran'"

          Expect.equal
              (decoded (Demanded.encode askedAndEmpty)).Server
              (Some emptyTier)
              "an empty tier reads back as 'a walk ran and found nothing'"

          Expect.notEqual
              (decoded (Demanded.encode notAsked))
              (decoded (Demanded.encode askedAndEmpty))
              "and the two documents do not decode to the same value"
      }

      test "an empty posture list and a populated one are both carried" {
          // One level down, the same fact: an ABSENT `replay` says the producer
          // predates the posture, an EMPTY one says the registration was walked
          // and no handler contributed. Version 3 carries the key on every tier,
          // so the read side only has to keep empty from becoming something
          // else.
          let walkedNoPosture = Demanded.withServer serverTier (Demanded.ofTree richTree)

          let joined =
              Demanded.withServer { serverTier with Replay = postures } (Demanded.ofTree richTree)

          Expect.equal
              ((decoded (Demanded.encode walkedNoPosture)).Server |> Option.map _.Replay)
              (Some [])
              "walked, no posture"

          Expect.equal
              ((decoded (Demanded.encode joined)).Server |> Option.map _.Replay)
              (Some postures)
              "the postures, in handler order"
      }

      test "the reasons within one posture keep their STAGE order, which sorting would destroy" {
          let joined =
              Demanded.withServer { serverTier with Replay = postures } (Demanded.ofTree richTree)

          let read =
              (decoded (Demanded.encode joined)).Server
              |> Option.map _.Replay
              |> Option.defaultValue []
              |> List.tryFind (fun p -> p.Handler = "/api/b")

          Expect.equal
              (read |> Option.map _.Reasons)
              (Some
                  [ { Stage = 0
                      Defect = "non-literal-write" }
                    { Stage = 2
                      Defect = "relative-addressing" } ])
              "a sequence through one handler, not a set"
      }

      test "hostile names survive the round trip through their escapes" {
          // The encoder escapes rather than emitting raw JSON; the reader has to
          // unescape exactly what it escaped, or a name comes back a different
          // string and a coverage check compares two things that are not the
          // same name.
          let hostile = "/api/\"q\"\\p\nline\ttab\rcr"

          let projection =
              Demanded.ofTree (dash [ btn "b" (Action.Call(hostile, None, None)) ])

          Expect.equal (decoded (Demanded.encode projection)) projection "the same string on the far side"

          Expect.equal
              ((decoded (Demanded.encode projection)).HostCalls |> List.map _.Name)
              [ hostile ]
              "and it is the name that went in"
      }

      // ── discriminators are carried, not interpreted ────────────

      test "PROBE TARGET — an effect discriminator this reader does not know is CARRIED, not dropped" {
          // A projection is a demand set. A reader that quietly dropped a name it
          // did not recognise would narrow that set, and a coverage check over
          // the narrowed set reports that a host covers something it does not — a
          // false green, in the one place a false green is worst.
          //
          // PROBE (run destructively, 2026-08-22): filtering the decoded
          // `effects` to the known `ClientEffect` discriminators turns this test
          // red, and only this one. The strictly-weaker reader is distinguishable
          // from the correct one.
          let json =
              emitted.Replace("\"effects\":[\"Navigate\"", "\"effects\":[\"AFutureArm\",\"Navigate\"")

          Expect.equal
              (decoded json).Effects
              [ "AFutureArm"; "Navigate"; "ReadFileBody"; "WriteToClipboard" ]
              "the unrecognised name is in the demand set, where a host can see it"
      }

      test "an unrecognised safety word and defect token are carried too" {
          let tier =
              { serverTier with
                  Replay =
                      [ { Handler = "/api/a"
                          Safety = "a-future-grade"
                          Reasons =
                            [ { Stage = 0
                                Defect = "a-future-defect" } ] } ] }

          let projection = Demanded.withServer tier (Demanded.ofTree richTree)
          Expect.equal (decoded (Demanded.encode projection)) projection "the same rule, one level down"
      }

      // ── what is refused ───────────────────────────────────────

      test "PROBE TARGET — a version this reader does not read is REFUSED, naming it" {
          // Not read through the current version's lens. A version-2 document
          // carries a server tier with no `replay` key — and no `constraints`
          // key either — and reading it as current could only either fail on a
          // well-formed document or invent an empty posture and an empty policy.
          // Inventing them collapses "predates the member" into "walked and
          // empty", which is the whole reason each number moved; for the policy
          // it turns "I cannot see the bounds" into "there are none".
          //
          // PROBE (run destructively, 2026-08-22, re-run 2026-09-19): widening
          // `decodableVersions` to include the older numbers turns this test and
          // the one below red — the older document then refuses as a MISSING
          // MEMBER, which is a report about the wrong thing entirely.
          let v2 =
              """{"kind":"demanded","version":2,"effects":[],"hostCalls":[],"stateNamespaces":[],"opaqueHandlers":[],"server":{"effects":[],"capabilities":[],"functions":[],"channels":[]}}"""

          let failure = refusal v2
          Expect.equal failure.Defect DemandedDefect.UnknownVersion "refused for its version"
          Expect.equal failure.Field "version" "and the member is named"
          Expect.equal failure.Version (Some 2) "and so is the version it declared"
      }

      test "PROBE TARGET — a version-1 document is refused for its VERSION, not for its shape" {
          // Version 1 carries no `server` key at all. The version gate runs
          // before the member checks precisely so this reports the fact a
          // producer can act on: "I do not read version 1", not "you are missing
          // a member" of a version it never claimed to be.
          let v1 =
              """{"kind":"demanded","version":1,"effects":[],"hostCalls":[],"stateNamespaces":[],"opaqueHandlers":[]}"""

          let failure = refusal v1
          Expect.equal failure.Defect DemandedDefect.UnknownVersion "the version, not the missing tier"
          Expect.equal failure.Version (Some 1) "named"
      }

      test "the readable versions are DATA, and today that is exactly what is emitted" {
          // The honest reading of "read every version still emitted": this
          // package emits 3 and nothing else, so 3 is what is read. When an
          // encoder for another version exists, this list and the round-trip
          // corpus move together.
          Expect.equal Demanded.decodableVersions [ Demanded.Version ] "one emitter, one reader"
      }

      test "another document's kind is refused" {
          let failure = refusal (replacing "\"kind\":\"demanded\"" "\"kind\":\"manifest\"")
          Expect.equal failure.Defect DemandedDefect.UnknownKind "not this document"
          Expect.equal failure.Field "kind" "named"
      }

      test "bytes that are not JSON are refused rather than thrown" {
          Expect.equal (refusal "{\"kind\":\"demanded\",").Defect DemandedDefect.NotJson "a typed refusal"
      }

      test "a root that is not an object is refused" {
          Expect.equal (refusal "[]").Defect DemandedDefect.NotAnObject "an array is not a document"
      }

      test "a missing member is refused, naming the member and the version" {
          let dropped =
              """{"kind":"demanded","version":6,"effects":[],"hostCalls":[],"stateNamespaces":[],"server":null}"""

          let missing = refusal dropped
          Expect.equal missing.Defect DemandedDefect.MissingMember "absent, not defaulted"
          Expect.equal missing.Field "opaqueHandlers" "the member is named"
          Expect.equal missing.Version (Some Demanded.Version) "and the version it is required by"
      }

      test "a member this version does not declare is refused" {
          let failure = replacing "\"server\":null" "\"tier\":1,\"server\":null" |> refusal

          Expect.equal
              failure.Defect
              DemandedDefect.UndeclaredMember
              "the producer and this reader disagree about version 4"

          Expect.equal failure.Field "tier" "named"
      }

      test "a mistyped member is refused, at its own path" {
          let failure =
              refusal (replacing "\"opaqueHandlers\":[\"region\"]" "\"opaqueHandlers\":\"region\"")

          Expect.equal failure.Defect DemandedDefect.WrongType "an array, not a string"
          Expect.equal failure.Field "opaqueHandlers" "named"
      }

      test "a defect inside a nested element is named by its path, ordinal and all" {
          let failure =
              refusal (replacing "{\"channel\":\"AiTool\",\"name\":\"summarise\"}" "{\"channel\":\"AiTool\"}")

          Expect.equal failure.Defect DemandedDefect.MissingMember "the element's own member"
          Expect.equal failure.Field "hostCalls[0].name" "addressed by position, never by a string the document chose"
      }

      test "a server tier that is neither an object nor absent is refused" {
          let failure = refusal (replacing "\"server\":null" "\"server\":[]")
          Expect.equal failure.Defect DemandedDefect.WrongType "an array is not a tier"
          Expect.equal failure.Field "server" "named"
      }

      test "a current-version document with no server member at all is refused" {
          // The key is present on EVERY document from version 2 on, `null` where
          // no walk ran. A document without it is not a document of this
          // version, and reading it as 'no walk ran' would silently accept a
          // shape this version does not describe.
          let noServer =
              """{"kind":"demanded","version":6,"effects":[],"hostCalls":[],"stateNamespaces":[],"opaqueHandlers":[]}"""

          let failure = refusal noServer
          Expect.equal failure.Defect DemandedDefect.MissingMember "absent is not null"
          Expect.equal failure.Field "server" "named"
      }

      test "a null with no absence to erase to is refused" {
          // A member spelled `null` IS an absent member, which is how the server
          // tier's marker is read. Every other position has nothing to erase to:
          // an array element would silently renumber every later index, and a
          // bare root would make the document vanish.
          Expect.equal
              (refusal (replacing "\"effects\":[\"Navigate\"" "\"effects\":[null,\"Navigate\"")).Defect
              DemandedDefect.NotJson
              "an array element"

          Expect.equal (refusal "null").Defect DemandedDefect.NotJson "a bare root"
      }

      test "a control character in a name survives the round trip rather than breaking the document" {
          // The encoder's escape set covered `\` `"` `\n` `\r` `\t` and nothing
          // else, so any OTHER control character went to the wire raw — and a raw
          // control byte inside a JSON string is invalid JSON. The document this
          // encoder produced was then refused by this decoder, which is the one
          // failure a self-describing projection must not have: the producer and
          // the reader are the same repository.
          //
          // Nothing this encoder writes carries a control character today, so
          // this moved no existing byte. It closes the case where a host names
          // one, which nothing structurally prevents.
          let hostile = dash [ btn "b" (Action.Call("/api/\u0001x\u001F", None, None)) ]
          let json = Demanded.encode (Demanded.ofTree hostile)

          Expect.stringContains json "\\u0001" "U+0001 escapes rather than going raw"
          Expect.stringContains json "\\u001f" "U+001F likewise"

          Expect.isFalse
              (json |> Seq.exists (fun c -> c < ' '))
              "and no control character reaches the wire raw, whatever its name"

          let back = decoded json
          Expect.equal back (Demanded.ofTree hostile) "the document its own reader accepts, unchanged"
      }

      test "a document whose lists are not canonical is refused, naming the list" {
          // Two documents comparing by value is the property this encoding
          // provides, and an unsorted or duplicated list is a document that does
          // not have it. Sorting it here instead would silently repair a producer
          // defect and hand back a value that no longer re-encodes to the bytes
          // it came from.
          let unsorted =
              """{"kind":"demanded","version":6,"effects":["WriteToClipboard","Navigate"],"hostCalls":[],"stateNamespaces":[],"opaqueHandlers":[],"server":null}"""

          let failure = refusal unsorted
          Expect.equal failure.Defect DemandedDefect.NotCanonical "not sorted"
          Expect.equal failure.Field "effects" "named"

          let duplicated =
              """{"kind":"demanded","version":6,"effects":[],"hostCalls":[],"stateNamespaces":[{"namespace":"cart","written":true,"read":false},{"namespace":"cart","written":false,"read":true}],"opaqueHandlers":[],"server":null}"""

          Expect.equal
              (refusal duplicated).Field
              "stateNamespaces"
              "one entry per namespace, or it is not this document"
      }

      test "a non-canonical SERVER list is refused too" {
          let unsorted =
              """{"kind":"demanded","version":6,"effects":[],"hostCalls":[],"stateNamespaces":[],"opaqueHandlers":[],"server":{"effects":["Notify","ApplyOps"],"capabilities":[],"functions":[],"channels":[],"reach":[],"replay":[],"undo":[],"constraints":[]}}"""

          let failure = refusal unsorted
          Expect.equal failure.Defect DemandedDefect.NotCanonical "the tier's lists carry the same promise"
          Expect.equal failure.Field "server" "named"
      }

      // ── totality ──────────────────────────────────────────────

      test "decode is TOTAL — no input throws" {
          // The whole point of a typed failure is that a caller holding untrusted
          // bytes never has to guard the call.
          let hostile =
              [ ""
                " "
                "{"
                "}"
                "[]"
                "null"
                "0"
                "\"demanded\""
                "{}"
                "{\"kind\":null}"
                "{\"kind\":\"demanded\"}"
                "{\"kind\":\"demanded\",\"version\":\"3\"}"
                "{\"kind\":\"demanded\",\"version\":3}"
                "{\"kind\":\"demanded\",\"version\":-1,\"server\":null}"
                "{\"a\":null,\"b\":null,\"c\":null,\"d\":null,\"e\":null,\"f\":null,\"g\":null,\"h\":null,\"i\":null}"
                emitted.Substring(0, emitted.Length / 2)
                emitted + emitted ]

          for input in hostile do
              match Demanded.decode input with
              | Ok _
              | Error _ -> ()
      } ]

// ─── tests ──────────────────────────────────────────────────────────

[<Tests>]
let tests =
    testList "Phase 1001 — the demanded projection round-trips" (roundTrips @ behaviour)
