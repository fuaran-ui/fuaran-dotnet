module Fuaran.Program.Server.Tests.ProgramWireConformanceTests

// ─── Certification against the program wire conformance corpus ───────────────
//
// The corpus is the oracle, not this suite. Every assertion below is driven by
// `manifest.json`: which vectors exist, which document each is, what a reject
// vector must be refused FOR, and what derived classification a handler vector
// carries. Nothing here enumerates a fixture by hand, because a hand-kept list
// is exactly how a corpus and a host quietly stop describing the same thing.
//
// Five properties, and the last two are the ones that make the first three mean
// something:
//
//   1. every `round-trip` vector decodes and re-encodes BYTE-IDENTICALLY;
//   2. every `reject` vector is refused FOR THE CLASS the manifest names — a
//      refusal for some other reason is not a pass, because "my parser threw"
//      and "my reader applied the rule" are different facts;
//   3. every derived value a vector declares is RECOMPUTED — the `replaySafety`
//      of §7.4 and the `replayReasons` of §7.5, the second of which is what
//      discriminates an arm rather than a grade;
//   4. the number of vectors this suite ran equals the number the manifest
//      enumerates — a harness that silently skipped a family reports the same
//      green as one that passed it;
//   5. a mutated fixture makes this harness go red. A conformance harness is
//      exactly the kind of code that passes by doing nothing, so its ability to
//      fail is asserted rather than assumed.
//
// ── The coverage pin, and why HALF of it lives here ──────────────────────────
// `ReplayDefect` is a closed vocabulary, so "closed" is a claim something can be
// measured against: an arm no expectation exhibits is one this host can classify
// wrongly while every gate stays green. The measurement is split, and neither
// half can do the other's job.
//
// The CORPUS checker enforces that every DOCUMENT-REACHABLE arm has a vector
// discriminating it, because the corpus is what a third-party host certifies
// against and an uncovered arm there is uncovered for everyone. It cannot
// enumerate this DU — it has never seen an F# type.
//
// THIS suite enforces the other direction: every arm of the DU is discriminated
// by something, whether a corpus vector or a case constructed here. It is the
// only place the DU can be enumerated at all, so it is the only place a SEVENTH
// arm can be made to arrive with its coverage or turn the gate red. And it is
// the only place `unencodable-op` can be reached: that arm names a defect in
// this reader's own rendering of a referenced position, and no conformant
// document produces it — an op that does not decode is refused before any
// classification runs. §7.5 records that asymmetry rather than leaving it to be
// rediscovered as a gap.
//
// ── The corpus is a BUILD INPUT, and its absence is a failure ────────────────
// The corpus lives in a sibling clone. A suite that skipped when it was missing
// would be the "passes by doing nothing" shape the corpus itself forbids, so an
// absent corpus fails loudly and names both the expected path and the override.

open System
open System.IO
open FSharp.Reflection
open Expecto
open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.Program.Bounded
open Fuaran.Program.UI
open Fuaran.Program.Server
open Fuaran.Program.Server.UI

/// Where the corpus is. Resolved from THIS source file rather than the working
/// directory, for the same reason the parity leg's fixture root is: a test's
/// cwd is whatever the runner chose, and a path that depends on it is a path
/// that works on one machine.
let private corpusRoot: string =
    match Environment.GetEnvironmentVariable "FUARAN_PROGRAM_SPEC" with
    | null
    | "" ->
        Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "..", "fuaran-program-spec", "wire-fixtures")
        |> Path.GetFullPath
    | declared -> Path.Combine(declared, "wire-fixtures") |> Path.GetFullPath

/// One vector, as the manifest declares it.
type private Vector =
    {
        Id: string
        Kind: string
        Document: string
        File: string
        Reject: string option
        /// The subject the vector is at (§10.7). `None` is the REFERENCED
        /// subject — the tree wire specification's vocabulary — which is the
        /// only one this suite's witness reads.
        Subject: string option
        ReplaySafety: string option
        /// §7.5, in the manifest's own spelling: a stage ORDINAL and a defect
        /// token. `None` where the vector declares none — which is every vector
        /// outside the handler round-trip family, and is not the same fact as an
        /// empty list (a handler with nothing to report).
        ReplayReasons: (int * string) list option
    }

let private str (name: string) (value: JVal) : string =
    match ProgramWire.tryString name value with
    | Some s -> s
    | None -> failwithf "manifest vector has no '%s'" name

/// The declared reasons of one vector, in order. A malformed entry FAILS rather
/// than being dropped: a reason this reader could not parse would otherwise
/// shrink the expectation it is supposed to raise.
let private reasonsOf (entry: JVal) : (int * string) list option =
    match ProgramWire.tryMember "replayReasons" entry with
    | None -> None
    | Some(JArr items) ->
        items
        |> List.map (fun item ->
            match ProgramWire.tryMember "stage" item, ProgramWire.tryString "defect" item with
            | Some(JInt stage), Some defect -> stage, defect
            | _ -> failwithf "a manifest reason is not a stage ordinal and a defect token")
        |> Some
    | Some _ -> failwith "a manifest vector's replayReasons is not an array"

/// The reasons this host derives for a handler, in the manifest's spelling.
let private derivedReasons (handler: Handler) : (int * string) list =
    HandlerWire.replayReasons handler
    |> List.map (fun reason -> reason.Stage, ProgramWire.replayDefectTag reason.Defect)

/// Every token `ReplayDefect` spells, enumerated FROM THE TYPE.
///
/// A list written out beside the DU would be a second copy of the vocabulary,
/// and a seventh arm would arrive without disturbing it — which is the whole
/// failure this pin exists to prevent, reintroduced inside the pin.
let private allDefectTokens: string list =
    FSharpType.GetUnionCases typeof<ReplayDefect>
    |> Array.map (fun case ->
        FSharpValue.MakeUnion(case, [||]) :?> ReplayDefect
        |> ProgramWire.replayDefectTag)
    |> Array.toList

/// The arms a covered-set leaves unaccounted for. A pure function so its
/// ability to report a missing arm can be demonstrated rather than trusted.
let private uncoveredArms (covered: Set<string>) : string list =
    allDefectTokens |> List.filter (covered.Contains >> not)

/// Arms reachable only from a value a host built itself, with the handler that
/// reaches each.
///
/// `unencodable-op` fires when this reader cannot put an op back on the wire.
/// The op below is the hazard `PropValue.Native`'s own documentation names — a
/// boxed `None` is a CLR null, which renders as the JSON token `null`, which
/// the canonical parser refuses. No document can carry it: a wire `null` is
/// refused at parse, long before anything is classified.
let private hostConstructedCases: (string * Handler) list =
    [ "unencodable-op",
      { Name = "orders.snapshot"
        Stages =
          [ Effect(
                ServerEffect.ApplyOps [ TreeOp.UpdateProp(NodeId "orders-total", "Label", PropValue.Native(box None)) ]
            ) ] } ]

let private everyVector () : Vector list =
    let manifestPath = Path.Combine(corpusRoot, "manifest.json")

    if not (File.Exists manifestPath) then
        failwithf
            "the conformance corpus is not present at '%s'. It is a sibling clone and a BUILD INPUT to this gate, \
             not an optional extra — clone it beside this repository, or point FUARAN_PROGRAM_SPEC at it. \
             This suite fails rather than skipping, deliberately: a conformance check that passes when its \
             oracle is missing is worse than no check."
            corpusRoot

    match Json.parse (File.ReadAllText manifestPath) with
    | Error message -> failwithf "the corpus manifest does not parse: %s" message
    | Ok manifest ->
        match ProgramWire.tryMember "vectors" manifest with
        | Some(JArr entries) ->
            entries
            |> List.map (fun entry ->
                { Id = str "id" entry
                  Kind = str "kind" entry
                  Document = str "document" entry
                  File = str "file" entry
                  Reject = ProgramWire.tryString "reject" entry
                  Subject = ProgramWire.tryString "subject" entry
                  ReplaySafety = ProgramWire.tryString "replaySafety" entry
                  ReplayReasons = reasonsOf entry })
        | _ -> failwith "the corpus manifest declares no vector array"

/// The vectors this suite certifies: the REFERENCED subject's, which are the
/// ones naming no subject (§10.7). A vector at another subject carries another
/// vocabulary in its referenced positions, which this suite's witness does not
/// read; it is certified by a suite at that subject's witness instead.
let private vectors () : Vector list =
    everyVector () |> List.filter (fun v -> v.Subject.IsNone)

/// Decode a document of the named kind and re-encode it. `Ok` carries the bytes
/// a conformant host emits; `Error` carries the refusal class.
///
/// The dispatch is on the vector's DOCUMENT, never on its family: `cross-layer`
/// deliberately carries a handler document, because a rule about a call action
/// can only be demonstrated by a document that contains one.
let private roundTrip (document: string) (bytes: string) : Result<string, WireRefusal> =
    match document with
    | "client-effect" ->
        // The one family whose bytes come from the SHIPPED emitter rather than
        // from the canonical renderer — which is the specification's enumerated
        // envelope exception, and the reason this arm looks different.
        ProgramWire.parseDocument bytes
        |> Result.bind ProgramWire.decodeClientEffect
        |> Result.map ProgramWire.encodeClientEffect
    | _ ->
        ProgramWire.parseDocument bytes
        |> Result.bind (fun value ->
            match document with
            | "handler" -> HandlerWire.decodeHandlerJson value |> Result.bind HandlerWire.encodeHandlerJson
            | "server-effect" -> HandlerWire.decodeEffect value |> Result.bind HandlerWire.encodeEffect
            | "invocation" -> ProgramWire.decodeInvocation value |> Result.map ProgramWire.encodeInvocation
            | "outcome" -> HandlerWire.decodeReportJson value |> Result.bind HandlerWire.encodeReportJson
            | "logic-tree-ref" ->
                ProgramWire.decodeLogicTreeRef value
                |> Result.map ProgramWire.encodeLogicTreeRef
            | other -> failwithf "the manifest names document kind '%s', which this host does not implement" other)
        |> Result.map ProgramWire.render

let private read (vector: Vector) : string =
    File.ReadAllText(Path.Combine(corpusRoot, vector.File))

[<Tests>]
let tests =
    let all = vectors ()

    testList
        "program wire conformance"
        [ test "the corpus is present and enumerates vectors" {
              Expect.isTrue (Directory.Exists corpusRoot) $"the corpus is at {corpusRoot}"
              Expect.isNonEmpty all "the manifest enumerates vectors"
          }

          test "every round-trip vector re-encodes byte-identically" {
              let ran =
                  all
                  |> List.filter (fun v -> v.Kind = "round-trip")
                  |> List.map (fun v ->
                      let committed = read v

                      match roundTrip v.Document committed with
                      | Error refusal -> failtestf "%s was refused (%s: %s)" v.Id refusal.Class refusal.Detail
                      | Ok emitted ->
                          Expect.equal emitted committed $"{v.Id} re-encodes to its committed bytes"
                          v.Id)

              // Property 3, on this half: the count is asserted rather than
              // hoped for, so a filter that matched nothing is a failure rather
              // than a silent success.
              Expect.equal
                  ran.Length
                  (all |> List.filter (fun v -> v.Kind = "round-trip") |> List.length)
                  "every enumerated round-trip vector was run"

              Expect.isNonEmpty ran "…and there were some to run"
          }

          test "every reject vector is refused for the class the manifest names" {
              let rejects = all |> List.filter (fun v -> v.Kind = "reject")

              let ran =
                  rejects
                  |> List.map (fun v ->
                      let expected =
                          match v.Reject with
                          | Some c -> c
                          | None -> failtestf "%s is a reject vector naming no class" v.Id

                      match roundTrip v.Document (read v) with
                      | Ok _ -> failtestf "%s was ACCEPTED; it must be refused for '%s'" v.Id expected
                      | Error refusal ->
                          // The class, not merely the fact of refusal. Refusing
                          // for the wrong reason would let this host certify by
                          // being broken in a convenient way.
                          Expect.equal refusal.Class expected $"{v.Id} is refused for the right class"
                          v.Id)

              Expect.equal ran.Length rejects.Length "every enumerated reject vector was run"
              Expect.isNonEmpty ran "…and there were some to run"
          }

          test "every handler vector's declared replay safety is reproduced" {
              let expectations = all |> List.filter (fun v -> v.ReplaySafety.IsSome)

              let ran =
                  expectations
                  |> List.map (fun v ->
                      match ProgramWire.parseDocument (read v) |> Result.bind HandlerWire.decodeHandlerJson with
                      | Error refusal -> failtestf "%s was refused (%s)" v.Id refusal.Class
                      | Ok handler ->
                          // RECOMPUTED from the decoded document. Reading the
                          // manifest's value back at it would certify nothing:
                          // a derived value nobody re-derives is a constant with
                          // a longer name.
                          let derived = ProgramWire.replaySafetyTag (HandlerWire.replaySafety handler)
                          Expect.equal derived v.ReplaySafety.Value $"{v.Id} classifies as declared"
                          v.Id)

              Expect.isNonEmpty ran "the manifest declares replay-safety expectations"

              // All three values appear, so a classifier that returned one
              // constant could not pass this suite.
              let distinct =
                  expectations |> List.choose _.ReplaySafety |> List.distinct |> List.sort

              Expect.equal distinct [ "safe"; "unknown"; "unsafe" ] "and the expectations span all three values"
          }

          test "every handler vector's declared replay reasons are reproduced" {
              // §7.5. The finer of the two derived expectations, and the one
              // that discriminates an ARM rather than a grade: two defects of
              // one grade produce one `replaySafety`, so the test above cannot
              // tell `relative-addressing` from `non-literal-write` however it
              // is written.
              let expectations = all |> List.filter (fun v -> v.ReplayReasons.IsSome)

              let ran =
                  expectations
                  |> List.map (fun v ->
                      match ProgramWire.parseDocument (read v) |> Result.bind HandlerWire.decodeHandlerJson with
                      | Error refusal -> failtestf "%s was refused (%s)" v.Id refusal.Class
                      | Ok handler ->
                          // Compared as an ORDERED SEQUENCE, not a set: the
                          // stage ordinal is half of a reason, and a defect
                          // attributed to the wrong stage points a reader at
                          // the wrong place while the verdict stays right.
                          Expect.equal
                              (derivedReasons handler)
                              v.ReplayReasons.Value
                              $"{v.Id} reports the reasons it declares, in order"

                          v.Id)

              Expect.isNonEmpty ran "the manifest declares replay-reason expectations"

              // Every vector carrying a classification carries reasons too:
              // the two are one fact stated at two grains, and a vector with
              // only the coarse one is a vector this suite cannot discriminate.
              let missing =
                  all
                  |> List.filter (fun v -> v.ReplaySafety.IsSome && v.ReplayReasons.IsNone)
                  |> List.map _.Id

              Expect.isEmpty missing "every vector declaring a classification declares its reasons"
          }

          test "an arm reachable only from a host-built value is discriminated here" {
              // The corpus structurally cannot carry these, so the coverage pin
              // below would otherwise be satisfied by a claim rather than by a
              // computation. Each case is RUN, and its reasons must name its
              // token and nothing else.
              for token, handler in hostConstructedCases do
                  Expect.equal
                      (derivedReasons handler)
                      [ 0, token ]
                      $"the host-constructed case for {token} reports exactly that reason"

                  Expect.equal
                      (ProgramWire.replaySafetyTag (HandlerWire.replaySafety handler))
                      "unknown"
                      $"…and the verdict it carries is the one {token} forces"
          }

          test "every arm of the defect vocabulary is discriminated by something" {
              // The coverage pin. Enumerated from the DU, so a seventh arm
              // arrives with its discriminating expectation or this goes red
              // naming it — which is the one thing no corpus checker can do,
              // never having seen an F# type.
              //
              // DISCRIMINATED, not merely present: a vector whose reasons name
              // two tokens covers neither, because a walk that confused one of
              // them for the other would leave that vector's expectation
              // untouched.
              let discriminatedByCorpus =
                  all
                  |> List.choose _.ReplayReasons
                  |> List.choose (fun reasons ->
                      match reasons |> List.map snd |> List.distinct with
                      | [ single ] -> Some single
                      | _ -> None)
                  |> Set.ofList

              let discriminatedByHost = hostConstructedCases |> List.map fst |> Set.ofList
              let covered = Set.union discriminatedByCorpus discriminatedByHost

              Expect.isEmpty
                  (uncoveredArms covered)
                  "every ReplayDefect arm has an expectation that fails when it alone is mis-classified"

              // The asymmetry §7.5 records, checked rather than commented: this
              // arm is not document-reachable, so finding it covered BY THE
              // CORPUS would mean either the corpus or the specification had
              // moved and someone should look at which.
              Expect.isFalse
                  (discriminatedByCorpus.Contains "unencodable-op")
                  "no corpus vector reaches the arm §7.5 says a document cannot produce"

              // …and the pin can report a gap. A coverage check that could not
              // name a missing arm would be the "passes by doing nothing" shape
              // the corpus forbids of a harness, applied to the harness's own
              // coverage claim.
              for token in allDefectTokens do
                  Expect.equal
                      (uncoveredArms (Set.remove token covered))
                      [ token ]
                      $"hiding {token} makes the pin name it, and name only it"
          }

          test "the harness can go red: a mutated fixture is not accepted unchanged" {
              // Property 4, and the reason the three tests above mean anything.
              // The mutation happens in memory; the committed corpus is never
              // touched.
              let subject = all |> List.find (fun v -> v.Id = "handler/read-compute-write")

              let committed = read subject
              let mutated = committed.Replace("orders.refresh", "orders.refreshed")

              Expect.notEqual mutated committed "the mutation actually changed the bytes"

              match roundTrip subject.Document mutated with
              | Error _ -> () // refused outright is also red
              | Ok emitted ->
                  Expect.notEqual emitted committed "a mutated fixture does not re-encode to the committed bytes"
          }

          test "the manifest's vector count is what this suite ran in total" {
              // The whole-corpus assertion, separate from the per-kind ones
              // above so that a vector of a kind this suite does not handle
              // would surface here rather than vanish between two filters.
              let handled =
                  all
                  |> List.filter (fun v -> v.Kind = "round-trip" || v.Kind = "reject")
                  |> List.length

              Expect.equal handled all.Length "every enumerated vector is of a kind this suite runs"
          }

          test "every vector this suite does not run is at a subject the specification names" {
              // §10.1 point 6 counts per role AND subject, so the count above is
              // the referenced subject's. What that filter leaves out must be
              // accounted for rather than silently dropped: every excluded vector
              // names the toy subject, and the two together are the whole
              // manifest — a vector naming a subject nobody recognises would
              // otherwise be one no host ran while every host reported green.
              let every = everyVector ()
              let excluded = every |> List.filter (fun v -> v.Subject.IsSome)

              Expect.equal
                  (excluded |> List.choose _.Subject |> List.distinct)
                  [ "toy" ]
                  "the only other subject is the toy (§10.7)"

              Expect.equal (all.Length + excluded.Length) every.Length "the two subjects partition the manifest"
          } ]
