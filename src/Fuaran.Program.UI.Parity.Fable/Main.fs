module Fuaran.Program.Parity.Fable.Main

open Fable.Core
open Fable.Core.JsInterop
open Fuaran.Program.Runtime
open Fuaran.Program.UI
open Fuaran.Program.Parity.Runner
open Fuaran.Program.Parity

// ============================================================================
//  Leg (c) — the client placement UNDER FABLE.
//
//  This harness exists because "it compiles under Fable" and "it behaves the
//  same under Fable" are different claims, and only the second one matters. It
//  reads the SAME scenario files the .NET legs read — the conformance corpus's
//  driver-semantics family — runs the SAME runner compiled to JavaScript, and
//  compares against the SAME recorded expectation.
//
//  The only thing that differs from the .NET legs is the loader — node's `fs`
//  instead of `System.IO` — which is the irreducible per-host part. It reads
//  the corpus MANIFEST rather than the directory, for the reason the .NET
//  loader does: a scenario the manifest forgot is a behaviour nobody is
//  required to reproduce, and a directory listing cannot see that.
// ============================================================================

[<Import("readFileSync", "fs")>]
let private readFileSync (path: string, encoding: string) : string = jsNative

[<Import("existsSync", "fs")>]
let private existsSync (path: string) : bool = jsNative

[<Emit("process.argv.slice(2)")>]
let private argv: string array = jsNative

[<Emit("process.exit($0)")>]
let private exit (code: int) : unit = jsNative

/// The family selection (fuaran#2011), read from node's environment — the same
/// variable the .NET legs read, so one gate run selects the same families on
/// every leg.
[<Emit("process.env.FUARAN_PROGRAM_FAMILIES")>]
let private selectionVariable: string = jsNative

let private read (path: string) : string = readFileSync (path, "utf8")

/// The scenario family this leg runs: the specification's UI-vocabulary
/// driver-semantics family.
[<Literal>]
let private uiFamily = "driver-semantics"

let private parseEvents (json: string) : ScriptedEvent list =
    let arr: obj array = JS.JSON.parse json |> unbox

    arr
    |> Array.toList
    |> List.map (fun el ->
        let payload: obj = el?payload

        let keys: string array =
            if isNullOrUndefined payload then
                [||]
            else
                JS.Constructors.Object.keys payload |> Array.ofSeq

        { NodeId = el?nodeId
          Event = el?event
          Payload = keys |> Array.map (fun k -> k, unbox<string> payload?(k)) |> Map.ofArray })

/// Read one recorded denial into this host's own vocabulary — the same
/// obligation the .NET loader has, for the same reason: a harness holding the
/// recorded bytes beside its own would compare strings and assert nothing about
/// whether this host RECOGNISES §5.3's vocabulary.
let private parseDenial (name: string) (index: int) (d: obj) : EffectDenial =
    let stringMember (key: string) : string option =
        let v: obj = d?(key)
        if isNullOrUndefined v then None else Some(unbox<string> v)

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

let private parseExpectation (name: string) (json: string) : StepObservation list =
    let arr: obj array = JS.JSON.parse json |> unbox

    arr
    |> Array.toList
    |> List.mapi (fun index el ->
        let effects: string array = el?effects |> unbox
        let recordedDenials: obj = el?denials

        // The tree is an embedded DOCUMENT. Re-serialising it here hands the
        // decoder the right MEANING, never the right bytes — which is the
        // whole point of the placement-independent format: this loader's JSON
        // writer is not the corpus's, and nothing downstream cares.
        { ResolvedJson = JS.JSON.stringify (el?tree)
          Effects = List.ofArray effects
          Refused = el?refused
          // Absent and empty are different facts (§10.3), which is exactly the
          // distinction `isNullOrUndefined` is here to preserve: an absent
          // member means the seam was unobserved, an empty array means it was
          // observed and declined nothing.
          Denials =
            if isNullOrUndefined recordedDenials then
                None
            else
                let ds: obj array = unbox recordedDenials
                Some(ds |> Array.toList |> List.map (parseDenial name index)) })

/// The manifest's driver-semantics enumeration. Reading the index rather than
/// the directory is what makes "every scenario the corpus declares was run" a
/// statement this leg can make.
let private loadScenarios (fixturesRoot: string) : Fixture list =
    let manifestPath = fixturesRoot + "/manifest.json"

    if not (existsSync manifestPath) then
        eprintfn
            "the conformance corpus is not present at %s. It is a sibling clone and a BUILD INPUT to this gate."
            fixturesRoot

        exit 1

    let manifest: obj = JS.JSON.parse (read manifestPath)
    let entries: obj array = manifest?scenarios |> unbox

    // The UI family's entries only: since fuaran#2011 the manifest carries a
    // second scenario family, over a toy witness this UI decoder cannot read
    // (the program repository runs that one under Fable itself).
    entries
    |> Array.toList
    |> List.filter (fun entry -> unbox<string> entry?family = uiFamily)
    |> List.map (fun entry ->
        let files: obj = entry?files
        let name: string = entry?name
        let policy: obj = entry?hostPolicy

        { Name = name
          TreeJson = read (fixturesRoot + "/" + unbox<string> files?tree)
          Events = parseEvents (read (fixturesRoot + "/" + unbox<string> files?events))
          Expected = parseExpectation name (read (fixturesRoot + "/" + unbox<string> files?expectation))
          HostPolicy =
            if isNullOrUndefined policy then
                None
            else
                Some(unbox<string> policy) })

/// The scenario families the corpus manifest declares.
let private declaredFamilies (manifest: obj) : string list =
    let families: obj = manifest?scenarioFamilies

    if isNullOrUndefined families then
        []
    else
        (unbox<obj array> families) |> Array.toList |> List.map unbox<string>

/// Whether this run selects the UI family (fuaran#2011's selection variable,
/// read the way the .NET legs read it): unset or empty selects every declared
/// family; a comma list naming a family the manifest does not declare FAILS,
/// because a selection that quietly emptied a family would run nothing and
/// report green.
let private uiSelected (declared: string list) : bool =
    if isNullOrUndefined selectionVariable || selectionVariable.Trim() = "" then
        true
    else
        let names =
            selectionVariable.Split ','
            |> Array.map (fun s -> s.Trim())
            |> Array.filter ((<>) "")
            |> List.ofArray

        match names |> List.filter (fun n -> not (List.contains n declared)) with
        | [] -> List.contains uiFamily names
        | unknown ->
            eprintfn
                "%s names scenario families the manifest does not declare: %s"
                "FUARAN_PROGRAM_FAMILIES"
                (String.concat ", " unknown)

            exit 1
            false

let private run () =
    let root = if argv.Length > 0 then argv.[0] else "../wire-fixtures"

    if not (existsSync (root + "/manifest.json")) then
        eprintfn
            "the conformance corpus is not present at %s. It is a sibling clone and a BUILD INPUT to this gate."
            root

        exit 1

    let declared = declaredFamilies (JS.JSON.parse (read (root + "/manifest.json")))

    if not (uiSelected declared) then
        printfn "-- %s: SKIPPED - not selected by %s" uiFamily "FUARAN_PROGRAM_FAMILIES"
        exit 0
    else

        let fixtures = loadScenarios root

        // A leg that found no scenarios must FAIL, not pass quietly. The vacuous
        // green is the failure mode a conformance family has to be immune to.
        if List.isEmpty fixtures then
            eprintfn "the corpus at %s enumerates no driver-semantics scenario" root
            exit 1
        else

            let mutable failures = 0

            for fixture in fixtures do
                if List.isEmpty fixture.Expected then
                    eprintfn "%s: no recorded expectation — run --emit-fixtures on the .NET side" fixture.Name
                    failures <- failures + 1
                else
                    // The expectation is brought into THIS host's terms first: it is
                    // a document, not a string of somebody's bytes, so a host with
                    // its own encoder compares its own output on both sides.
                    match normaliseExpectation fixture.Name fixture.Expected with
                    | Error e ->
                        eprintfn "%s: %s" fixture.Name e
                        failures <- failures + 1
                    | Ok expected ->
                        match runClientPlacement fixture with
                        | Error e ->
                            eprintfn "%s: %s" fixture.Name e
                            failures <- failures + 1
                        | Ok observed ->
                            match compare fixture.Name "expected" expected "Runtime/Fable" observed with
                            | None -> printfn "ok   %s (%d step(s))" fixture.Name (List.length observed)
                            | Some divergence ->
                                eprintfn "FAIL %s" (Divergence.describe divergence)
                                failures <- failures + 1

            printfn "%s: %d scenario(s); %d failure(s)" uiFamily (List.length fixtures) failures

            exit (if failures > 0 then 1 else 0)

run ()
