module Fuaran.UI.Program.Parity.FixtureIo

// .NET-only: System.IO + System.Text.Json. The Fable leg has its own loader
// over node's fs and reads the SAME files, so this file is excluded from the
// Fable compilation rather than shimmed.
#if !FABLE_COMPILER

open System
open System.IO
open System.Text.Json
open Fuaran.Program.Runtime
open Fuaran.UI.Program
open Fuaran.UI.Program.Parity.Runner

// ============================================================================
//  Scenario IO for the .NET legs.
//
//  Deliberately NOT in `Parity.fs`: reading a directory is the one part of this
//  family that genuinely differs per host, so it is the one part that is
//  host-specific. The Fable leg has its own loader over node's `fs` and reads
//  the SAME files — which is the point. A scenario that only one host can read
//  would not be a parity scenario.
//
//  ── The scenarios come from the conformance corpus ──────────────────────────
//  They are the corpus's DRIVER-SEMANTICS family. The corpus is a sibling clone
//  and a BUILD INPUT to this gate, exactly as it is for the codec suite: its
//  absence FAILS rather than skips, because a conformance check that passes
//  when its oracle is missing is worse than no check.
//
//  The MANIFEST is what is enumerated, never the directory listing. A scenario
//  present on disk but absent from the manifest is not a harmless extra: it is
//  a behaviour nobody is required to reproduce, and every host still reports
//  full conformance. Reading the directory would hide exactly that.
//
//  On-disk shape, one directory per scenario:
//
//    <name>/tree.json         the wire tree the scenario starts from
//    <name>/events.json       [ { "nodeId", "event", "payload" } ]
//    <name>/expectation.json  [ { "tree", "effects", "refused" } ]
//
//  `expectation.json` carries one entry per STEP, index 0 being the state
//  before any event — so a placement that resolved the initial tree differently
//  fails at step 0 rather than looking like a fold bug later.
// ============================================================================

/// The conformance corpus, resolved from THIS source file rather than the
/// working directory — a suite that only passes when invoked from one directory
/// is not a gate, it is a coincidence. `FUARAN_PROGRAM_SPEC` overrides the path.
let corpusRoot: string =
    match Environment.GetEnvironmentVariable "FUARAN_PROGRAM_SPEC" with
    | null
    | "" ->
        Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "..", "fuaran-program-spec")
        |> Path.GetFullPath
    | declared -> Path.GetFullPath declared

/// The corpus's fixture root — the directory the manifest sits in.
let fixturesRoot: string = Path.Combine(corpusRoot, "wire-fixtures")

/// The driver-semantics family's own subtree, which is where a regenerated
/// scenario is written.
let scenarioFamily: string = "driver-semantics"

let private missing (path: string) : 'a =
    failwithf
        "the conformance corpus is not present at '%s'. It is a sibling clone and a BUILD INPUT to this gate, \
         not an optional extra — clone it beside this repository, or point FUARAN_PROGRAM_SPEC at it. This \
         suite fails rather than skipping, deliberately: a conformance check that passes when its oracle is \
         missing is worse than no check."
        path

/// One payload value off the event script, in the portable subset a surface
/// sends — a string, a number, a boolean, or the absent value. A boolean is a
/// boolean: the confirm answer's `confirmAccepted` is one (Phase 2106), and
/// reading it as the string "true" would be a different event.
let private liveValueOf (v: JsonElement) : Fuaran.UI.ServerDriven.Validation.LiveValue =
    match v.ValueKind with
    | JsonValueKind.String -> Fuaran.UI.ServerDriven.Validation.LiveValue.Str(v.GetString())
    | JsonValueKind.True -> Fuaran.UI.ServerDriven.Validation.LiveValue.Bool true
    | JsonValueKind.False -> Fuaran.UI.ServerDriven.Validation.LiveValue.Bool false
    | JsonValueKind.Number -> Fuaran.UI.ServerDriven.Validation.LiveValue.Num(v.GetDouble())
    | _ -> Fuaran.UI.ServerDriven.Validation.LiveValue.Null

let private readEvents (json: string) : ScriptedEvent list =
    use doc = JsonDocument.Parse json

    [ for el in doc.RootElement.EnumerateArray() ->
          { NodeId = el.GetProperty("nodeId").GetString()
            Event = el.GetProperty("event").GetString()
            Payload =
              match el.TryGetProperty "payload" with
              | true, p ->
                  [ for prop in p.EnumerateObject() -> prop.Name, liveValueOf prop.Value ]
                  |> Map.ofList
              | _ -> Map.empty } ]

/// Read one recorded denial into this host's own vocabulary.
///
/// **Decoded, never carried as an opaque value.** A harness that held the
/// recorded bytes beside its own would compare two strings and assert nothing
/// about whether this host RECOGNISES §5.3 — so an arm outside the vocabulary,
/// or an `origin` on one that never consulted a destination, fails to load here
/// rather than travelling on as something somebody expected to be honoured.
let private readDenial (name: string) (index: int) (el: JsonElement) : EffectDenial =
    let stringMember (key: string) =
        match el.TryGetProperty key with
        | true, v when v.ValueKind = JsonValueKind.String -> Some(v.GetString())
        | true, _ -> failwithf "%s: step %d records a denial whose '%s' is not a string" name index key
        | _ -> None

    let arm =
        match stringMember "$type" with
        | Some t -> t
        | None -> failwithf "%s: step %d records a denial with no '$type'" name index

    let capability =
        match stringMember "capability" with
        | Some c -> c
        | None -> failwithf "%s: step %d records a denial with no 'capability'" name index

    match EffectDenial.ofWire arm capability (stringMember "origin") with
    | Ok denial -> denial
    | Error message -> failwithf "%s: step %d: %s" name index message

let private readExpectation (name: string) (json: string) : StepObservation list =
    use doc = JsonDocument.Parse json

    [ for index, el in doc.RootElement.EnumerateArray() |> Seq.indexed ->
          // The tree is an embedded DOCUMENT, not a string of somebody's bytes.
          // Taking its raw text hands the decoder a document with the right
          // MEANING; `normaliseExpectation` is what puts it into this host's own
          // bytes before anything is compared.
          { ResolvedJson = el.GetProperty("tree").GetRawText()
            Effects = [ for e in el.GetProperty("effects").EnumerateArray() -> e.GetString() ]
            Refused = el.GetProperty("refused").GetBoolean()
            // ABSENT and EMPTY are different facts here (§10.3): absent means
            // the seam was not observed, empty means it was and declined
            // nothing. So the member is read as an option rather than defaulted
            // to a list, which is the one place a defaulting reader would turn
            // a silence into a claim.
            Denials =
              match el.TryGetProperty "denials" with
              | true, arr -> Some [ for d in arr.EnumerateArray() -> readDenial name index d ]
              | _ -> None } ]

/// One scenario as the corpus manifest declares it. The host reads the
/// manifest's enumeration and nothing else.
type ScenarioEntry =
    {
        Name: string
        Dir: string
        Tree: string
        Events: string
        Expectation: string
        Steps: int
        /// The §10.3 host-policy NAME, where the scenario declares one. Read from
        /// the manifest because that is where the corpus puts it — a policy is a
        /// fact about a host, so the index names it and every host constructs what
        /// the name denotes.
        HostPolicy: string option
    }

/// The scenario families the manifest declares.
let declaredFamilies (fixturesRoot: string) : string list =
    let manifestPath = Path.Combine(fixturesRoot, "manifest.json")

    if not (File.Exists manifestPath) then
        missing fixturesRoot

    use doc = JsonDocument.Parse(File.ReadAllText manifestPath)

    match doc.RootElement.TryGetProperty "scenarioFamilies" with
    | true, families -> [ for f in families.EnumerateArray() -> f.GetString() ]
    | _ -> []

/// The environment variable a gate run SELECTS scenario families with
/// (fuaran#2011): unset or empty selects every declared family, otherwise a
/// comma list of declared family names. The same variable, read the same way,
/// as the toy family's harness reads it.
[<Literal>]
let SelectionVariable = "FUARAN_PROGRAM_FAMILIES"

/// Whether this run selects the UI family. A selection naming a family the
/// manifest does not declare FAILS rather than matching nothing — a selection
/// that quietly emptied a family would run nothing and report green.
let selected (fixturesRoot: string) : bool =
    let declared = declaredFamilies fixturesRoot

    match Environment.GetEnvironmentVariable SelectionVariable with
    | null -> true
    | raw when raw.Trim() = "" -> true
    | raw ->
        let names =
            raw.Split ','
            |> Array.map (fun s -> s.Trim())
            |> Array.filter ((<>) "")
            |> List.ofArray

        match names |> List.filter (fun n -> not (List.contains n declared)) with
        | [] -> List.contains scenarioFamily names
        | unknown ->
            failwithf
                "%s names scenario famil%s the manifest does not declare: %s"
                SelectionVariable
                (if List.length unknown = 1 then "y" else "ies")
                (String.Join(", ", unknown))

/// The manifest's driver-semantics enumeration, in declared order — the UI
/// family's entries only. The manifest carries a second scenario family since
/// fuaran#2011, over a toy witness whose trees this loader's UI decoder cannot
/// read, and a loader that took every entry would hand it one.
let scenarios (fixturesRoot: string) : ScenarioEntry list =
    let manifestPath = Path.Combine(fixturesRoot, "manifest.json")

    if not (File.Exists manifestPath) then
        missing fixturesRoot

    use doc = JsonDocument.Parse(File.ReadAllText manifestPath)

    match doc.RootElement.TryGetProperty "scenarios" with
    | false, _ -> failwith "the corpus manifest declares no scenario array"
    | true, entries ->
        [ for el in entries.EnumerateArray() do
              if el.GetProperty("family").GetString() = scenarioFamily then
                  let files = el.GetProperty "files"

                  yield
                      { Name = el.GetProperty("name").GetString()
                        Dir = el.GetProperty("dir").GetString()
                        Tree = files.GetProperty("tree").GetString()
                        Events = files.GetProperty("events").GetString()
                        Expectation = files.GetProperty("expectation").GetString()
                        Steps = el.GetProperty("steps").GetInt32()
                        HostPolicy =
                          match el.TryGetProperty "hostPolicy" with
                          | true, p -> Some(p.GetString())
                          | _ -> None } ]

/// Load every scenario the manifest enumerates. A file the manifest names but
/// the tree does not carry is a failure, not an omission.
let load (fixturesRoot: string) : Fixture list =
    scenarios fixturesRoot
    |> List.map (fun entry ->
        let read (relative: string) =
            let path = Path.Combine(fixturesRoot, relative)

            if not (File.Exists path) then
                failwithf "the corpus enumerates '%s', which is not present at '%s'" relative path

            File.ReadAllText path

        { Name = entry.Name
          TreeJson = read entry.Tree
          Events = readEvents (read entry.Events)
          Expected = readExpectation entry.Name (read entry.Expectation)
          HostPolicy = entry.HostPolicy })

// ─── Writing: the `--emit-fixtures` path ─────────────────────────────────────

/// §2.7's escaping, applied to a string this host writes into the corpus:
/// `"`, `\` and the control range, and nothing else. Deliberately NOT
/// `JsonSerializer.Serialize`, whose default escaping spells an ordinary quote
/// `"` — valid, and unreadable in a corpus a human is meant to review.
let private quoted (s: string) : string =
    let sb = Text.StringBuilder()
    sb.Append '"' |> ignore

    for ch in s do
        if ch = '"' then
            sb.Append "\\\"" |> ignore
        elif ch = '\\' then
            sb.Append "\\\\" |> ignore
        elif ch < ' ' then
            sb.AppendFormat("\\u{0:x4}", int ch) |> ignore
        else
            sb.Append ch |> ignore

    sb.Append '"' |> ignore
    sb.ToString()

/// One payload value as the event script spells it — `liveValueOf` read back.
let private liveValueText (v: Fuaran.UI.ServerDriven.Validation.LiveValue) : string =
    match v with
    | Fuaran.UI.ServerDriven.Validation.LiveValue.Str s -> quoted s
    | Fuaran.UI.ServerDriven.Validation.LiveValue.Bool b -> if b then "true" else "false"
    | Fuaran.UI.ServerDriven.Validation.LiveValue.Num n -> n.ToString("R", Globalization.CultureInfo.InvariantCulture)
    | Fuaran.UI.ServerDriven.Validation.LiveValue.Null -> "null"

/// Every scenario file is written WITHOUT a trailing newline, for the reason the
/// corpus applies to its wire vectors: the manifest digests the file, and a
/// trailing newline is a byte like any other.
let private writeFile (path: string) (text: string) : unit =
    File.WriteAllText(path, text, Text.UTF8Encoding false)

let private scenarioDir (fixturesRoot: string) (name: string) : string =
    let dir = Path.Combine(fixturesRoot, scenarioFamily, name)
    Directory.CreateDirectory dir |> ignore
    dir

/// Write a scenario's tree + event script. Together with `writeExpectation` this
/// is the whole on-disk artefact — the thing every leg reads, including the ones
/// that cannot run F#.
let writeTree (fixturesRoot: string) (fixture: Fixture) : unit =
    let dir = scenarioDir fixturesRoot fixture.Name
    writeFile (Path.Combine(dir, "tree.json")) fixture.TreeJson

    let events =
        fixture.Events
        |> List.map (fun ev ->
            let payload =
                ev.Payload
                |> Map.toList
                |> List.map (fun (k, v) -> sprintf "%s: %s" (quoted k) (liveValueText v))
                |> String.concat ", "

            sprintf
                "  { \"nodeId\": %s, \"event\": %s, \"payload\": {%s} }"
                (quoted ev.NodeId)
                (quoted ev.Event)
                payload)
        |> String.concat ",\n"

    writeFile (Path.Combine(dir, "events.json")) ("[\n" + events + "\n]")

/// Write a scenario's expectation from an observed run — the `--emit-fixtures`
/// path, mirroring the corpus's own emit convention. Emitting is a DELIBERATE
/// act: it records what the placements currently agree on, so it must only ever
/// run when they DO agree, which the caller checks first.
///
/// The tree is spliced in as a DOCUMENT rather than as an escaped string of this
/// host's bytes: that is what makes the expectation placement-independent, since
/// a reader decodes it with its own decoder rather than diffing it against its
/// own output. The effects beside it stay escaped strings on purpose — their
/// bytes are the specification's enumerated envelope exception, and embedding
/// them as objects would licence any JSON writer to reorder their members and
/// erase it.
let writeExpectation (fixturesRoot: string) (fixture: Fixture) (observations: StepObservation list) : unit =
    let dir = scenarioDir fixturesRoot fixture.Name

    let body =
        observations
        |> List.map (fun o ->
            let effects = o.Effects |> List.map quoted |> String.concat ", "

            // The denials are spliced in as DOCUMENTS, unlike the effects beside
            // them and for the opposite reason: this specification owns their
            // envelope outright, so there is no exception a JSON writer could
            // erase and nothing to protect by carrying them as strings. An
            // OMITTED member and an empty array are different facts (§10.3), so
            // `None` writes no member at all rather than `[]`.
            let denials =
                match o.Denials with
                | None -> []
                | Some list ->
                    [ sprintf "    \"denials\": [%s]" (list |> List.map EffectDenial.encodeWire |> String.concat ", ") ]

            String.concat
                "\n"
                ([ "  {"
                   sprintf "    \"tree\": %s," o.ResolvedJson
                   sprintf "    \"effects\": [%s]," effects
                   sprintf
                       "    \"refused\": %s%s"
                       (if o.Refused then "true" else "false")
                       (if denials.IsEmpty then "" else ",") ]
                 @ denials
                 @ [ "  }" ]))
        |> String.concat ",\n"

    writeFile (Path.Combine(dir, "expectation.json")) ("[\n" + body + "\n]")
#endif
