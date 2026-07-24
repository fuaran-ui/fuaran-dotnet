module Fuaran.UI.Validator.Program

// ============================================================================
//  Validator CLI entry.
//
//    Fuaran.UI.Validator <project.fsproj> [--module-pattern SUBSTR]
//                                       [--manifest PATH]
//                                       [--format plain|json]
//
//  Exit codes:
//    0 — no Error-severity findings
//    1 — at least one Error finding (Warnings do not affect the exit code)
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

let private printUsage () =
    eprintfn
        "usage: Fuaran.UI.Validator <project.fsproj> [--module-pattern SUBSTR] [--manifest PATH] [--orchestrated] [--format plain|json]"

let private parseArgs (argv: string array) : Result<ParsedArgs, string> =
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

    match Array.toList argv with
    | [] -> Result.Error "missing project path"
    | projectPath :: rest ->
        loop
            { ProjectPath = projectPath
              ModulePattern = None
              ManifestPath = None
              Orchestrated = false
              Format = ErrorRender.Plain }
            rest

[<EntryPoint>]
let main argv =
    match parseArgs argv with
    | Result.Error message ->
        eprintfn "error: %s" message
        printUsage ()
        2
    | Result.Ok parsed ->
        if not (System.IO.File.Exists parsed.ProjectPath) then
            eprintfn "error: project file not found: %s" parsed.ProjectPath
            2
        else
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

            printfn
                "Fuaran.UI.Validator: %d file(s), %d error(s), %d warning(s), manifest=%s"
                result.FilesWalked
                errorCount
                warningCount
                (result.ManifestPath |> Option.defaultValue "(none)")

            if errorCount > 0 then 1 else 0
