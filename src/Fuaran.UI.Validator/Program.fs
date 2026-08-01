module Fuaran.UI.Validator.Program

// ============================================================================
//  Validator CLI entry.
//
//    Fuaran.UI.Validator <project.fsproj> [--module-pattern SUBSTR]
//                                       [--manifest PATH]
//                                       [--format plain|json]
//
//    Fuaran.UI.Validator emit-manifest <project.fsproj> [--out PATH]
//                                                       [--overrides PATH]
//                                                       [--check]
//
//  Exit codes:
//    0 — no Error-severity findings (validate) / no drift (emit-manifest)
//    1 — at least one Error finding (Warnings do not affect the exit code)
//        / manifest drift under --check
//    2 — malformed CLI arguments / project file not found
// ============================================================================

open System
open Fuaran.UI.Validator
open Fuaran.UI.Validator.Findings

type private ParsedArgs =
    { ProjectPath: string
      ModulePattern: string option
      ManifestPath: string option
      Orchestrated: bool
      Format: ErrorRender.OutputFormat }

type private EmitArgs =
    { ProjectPath: string
      OutPath: string option
      OverridesPath: string option
      Check: bool }

let private printUsage () =
    eprintfn
        "usage: Fuaran.UI.Validator <project.fsproj> [--module-pattern SUBSTR] [--manifest PATH] [--orchestrated] [--format plain|json]"

    eprintfn "       Fuaran.UI.Validator emit-manifest <project.fsproj> [--out PATH] [--overrides PATH] [--check]"

let private parseArgs (argv: string list) : Result<ParsedArgs, string> =
    let rec loop (acc: ParsedArgs) (remaining: string list) =
        match remaining with
        | [] -> Result.Ok acc
        | "--module-pattern" :: value :: rest -> loop { acc with ModulePattern = Some value } rest
        | "--manifest" :: value :: rest -> loop { acc with ManifestPath = Some value } rest
        | "--orchestrated" :: rest -> loop { acc with Orchestrated = true } rest
        | "--format" :: "plain" :: rest -> loop { acc with Format = ErrorRender.Plain } rest
        | "--format" :: "json" :: rest -> loop { acc with Format = ErrorRender.Json } rest
        | "--format" :: other :: _ -> Result.Error(sprintf "unknown --format value: %s (expected plain|json)" other)
        | flag :: _ when flag.StartsWith "--" -> Result.Error(sprintf "unknown or incomplete flag: %s" flag)
        | _ -> Result.Error "trailing positional arguments after project path"

    match argv with
    | [] -> Result.Error "missing project path"
    | projectPath :: rest ->
        loop
            { ProjectPath = projectPath
              ModulePattern = None
              ManifestPath = None
              Orchestrated = false
              Format = ErrorRender.Plain }
            rest

let private parseEmitArgs (argv: string list) : Result<EmitArgs, string> =
    let rec loop (acc: EmitArgs) (remaining: string list) =
        match remaining with
        | [] -> Result.Ok acc
        | "--out" :: value :: rest -> loop { acc with OutPath = Some value } rest
        | "--overrides" :: value :: rest -> loop { acc with OverridesPath = Some value } rest
        | "--check" :: rest -> loop { acc with Check = true } rest
        | flag :: _ when flag.StartsWith "--" -> Result.Error(sprintf "unknown or incomplete flag: %s" flag)
        | _ -> Result.Error "trailing positional arguments after project path"

    match argv with
    | [] -> Result.Error "missing project path"
    | projectPath :: rest ->
        loop
            { ProjectPath = projectPath
              OutPath = None
              OverridesPath = None
              Check = false }
            rest

let private runValidate (parsed: ParsedArgs) =
    let result =
        Validator.run
            { ProjectPath = parsed.ProjectPath
              ModulePattern = parsed.ModulePattern
              ManifestPath = parsed.ManifestPath
              Orchestrated = parsed.Orchestrated }
        |> Async.RunSynchronously

    let rendered = ErrorRender.renderAll parsed.Format result.Findings

    if not (String.IsNullOrEmpty rendered) then
        printfn "%s" rendered

    let errorCount = result.Findings |> List.filter isError |> List.length

    let warningCount =
        result.Findings |> List.filter (fun f -> not (isError f)) |> List.length

    // Suppressed findings are counted in the summary, never hidden — a
    // silenced rule should still be visible in the build log.
    let suppressedNote =
        if result.Suppressed > 0 then
            sprintf ", %d suppressed" result.Suppressed
        else
            ""

    printfn
        "Fuaran.UI.Validator: %d file(s), %d error(s), %d warning(s)%s, manifest=%s"
        result.FilesWalked
        errorCount
        warningCount
        suppressedNote
        (result.ManifestPath |> Option.defaultValue "(none)")

    if errorCount > 0 then 1 else 0

/// Report what the derivation could and could not establish. A row-type
/// conflict is the one finding a caller must act on: the emitter deliberately
/// leaves that query unentered rather than picking a winner, so the query
/// stays unverifiable until the source is fixed or the override tier asserts
/// the row type.
let private reportDerivation (outcome: ManifestEmitter.EmitOutcome) =
    let derivation = outcome.Derivation

    printfn
        "Fuaran.UI.Validator emit-manifest: %d file(s), %d quer(ies), %d Msg case(s), %d row type(s)"
        derivation.FilesWalked
        (Set.count outcome.Merged.Queries)
        (Set.count outcome.Merged.MsgCases)
        (Map.count outcome.Merged.QueryRowTypes)

    for case in derivation.MsgCases do
        match case.PayloadTypes with
        | [] -> ()
        | payload -> printfn "  msg %s of %s" case.Case (String.Join(" * ", payload))

    for KeyValue(query, annotations) in derivation.RowTypeConflicts do
        eprintfn
            "  warning: query \"%s\" is read by grids annotating conflicting row types (%s) — no queryRowTypes entry emitted; fix the source or assert the row type in %s"
            query
            (String.Join(", ", annotations))
            Manifest.overridesFileName

    match outcome.OverridesPath with
    | Some path -> printfn "  overrides: %s" path
    | None -> ()

    for redundant in outcome.Provenance.RedundantOverrides do
        printfn "  note: override %s is now derived from source — the override entry is redundant" redundant

let private runEmit (parsed: EmitArgs) =
    let outcome =
        ManifestEmitter.run
            { ProjectPath = parsed.ProjectPath
              OutPath = parsed.OutPath
              OverridesPath = parsed.OverridesPath }
        |> Async.RunSynchronously

    reportDerivation outcome

    if parsed.Check then
        if not outcome.CommittedExists then
            eprintfn "error: no manifest at %s — run emit-manifest without --check to generate it" outcome.OutPath
            1
        elif not (List.isEmpty outcome.Drift) then
            eprintfn "error: manifest drift — %s no longer describes the source:" outcome.OutPath

            for drift in outcome.Drift do
                eprintfn "  %s" (ManifestEmitter.describeDrift drift)

            eprintfn "regenerate with: Fuaran.UI.Validator emit-manifest %s" parsed.ProjectPath
            1
        else
            // Formatting-only difference is advisory, never a gate failure: the
            // contract still describes the app, which is all CI must enforce.
            if outcome.FormattingDrift then
                printfn
                    "  note: %s is semantically current but not in canonical form — regenerate to normalise"
                    outcome.OutPath

            printfn "  manifest is current"
            0
    else
        ManifestEmitter.write outcome
        printfn "  wrote %s" outcome.OutPath
        0

[<EntryPoint>]
let main argv =
    match Array.toList argv with
    | "emit-manifest" :: rest ->
        match parseEmitArgs rest with
        | Result.Error message ->
            eprintfn "error: %s" message
            printUsage ()
            2
        | Result.Ok parsed ->
            if not (System.IO.File.Exists parsed.ProjectPath) then
                eprintfn "error: project file not found: %s" parsed.ProjectPath
                2
            else
                runEmit parsed
    | args ->
        match parseArgs args with
        | Result.Error message ->
            eprintfn "error: %s" message
            printUsage ()
            2
        | Result.Ok parsed ->
            if not (System.IO.File.Exists parsed.ProjectPath) then
                eprintfn "error: project file not found: %s" parsed.ProjectPath
                2
            else
                runValidate parsed
