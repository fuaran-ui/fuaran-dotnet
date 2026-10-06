module Fuaran.UI.Validator.Validator

// ============================================================================
//  Validator orchestration.
//
//  Loads the manifest, parses every F# source under the target project ONCE,
//  runs each check as a visitor over the parsed sources, and returns the
//  merged finding list.
// ============================================================================

open System.IO
open FSharp.Compiler.CodeAnalysis

open Fuaran.UI.Validator.Findings
open Fuaran.UI.Validator.Manifest

/// Configuration for one validator run.
type RunOptions =
    {
        /// Path to a .fsproj.
        ProjectPath: string
        /// Optional substring filter on F# source file paths — only files
        /// containing this substring participate. `None` = all files.
        ModulePattern: string option
        /// Explicit manifest path. `None` triggers convention-based discovery
        /// (sibling `fuaran-validator.manifest.json` of the .fsproj).
        ManifestPath: string option
        /// Orchestrated / AI-emitted context. Escalates the wire-survivability
        /// advisories (FUARAN084) from Warning to Error — an AI author must stay
        /// on the wire-survivable substrate. Default `false` (hand-authored).
        Orchestrated: bool
    }

type RunResult =
    {
        Findings: Finding list
        ManifestPath: string option
        ManifestLoaded: bool
        FilesWalked: int
        /// Findings dropped by a source-level `// fuaran-validator: disable`
        /// pragma. Reported so a suppression is visible in the run summary
        /// rather than silently shrinking the finding count.
        Suppressed: int
    }

let private projectDirectory (projectPath: string) =
    let full = Path.GetFullPath projectPath

    match Path.GetDirectoryName full with
    | null -> full
    | dir -> dir

let private resolveManifest (options: RunOptions) =
    match options.ManifestPath with
    | Some explicit when File.Exists explicit -> Some explicit
    | Some _ -> None
    | None -> options.ProjectPath |> projectDirectory |> discover

/// Run every check with an explicit parser; return a `RunResult` so callers
/// can decide formatting + exit-code policy. Each source file is parsed
/// exactly ONCE, by `parse`, and every check is a visitor over that one
/// parsed-input list — `parse` is a parameter so the once-per-file property is
/// observable (a test counts the calls). Does not call `Environment.Exit`.
let runWith (parse: Syntax.Parser) (options: RunOptions) : Async<RunResult> =
    async {
        let projectDir = projectDirectory options.ProjectPath
        let manifestPath = resolveManifest options

        let manifest =
            match manifestPath with
            | Some path -> load path
            | None -> empty

        let sourceFiles =
            projectDir
            |> Syntax.discoverSourceFiles
            |> List.filter (fun file ->
                match options.ModulePattern with
                | Some pattern -> file.Contains pattern
                | None -> true)

        let! sources = Syntax.parseSources parse sourceFiles
        let calls = AstWalker.calls sources

        let findings =
            [ // Over the Fuaran.X call-site model.
              yield! NodeIdCheck.check calls
              yield! BindingResolution.check manifest calls
              yield! MsgPayloadCheck.check manifest calls
              yield! RowTypeCheck.check manifest calls
              yield! ButtonDisabledCheck.check calls
              yield! ScalarRangeCheck.check calls
              yield! LinkCheck.check calls
              // Over the parsed sources directly.
              yield! LocalBindingCheck.check sources // FUARAN042 / 043 / 044
              yield! NumberFieldRangeCheck.check sources // FUARAN051
              yield! SegmentedChoiceCheck.check sources // FUARAN045
              yield! ExtraAttributesCheck.check sources // FUARAN060
              yield! CustomHealthCheck.check manifest calls sources // FUARAN053 / 054 / 055
              yield! CustomContentHashCheck.check sources // FUARAN062
              yield! FragmentCheck.check sources // FUARAN056 / 057 / 058 / 059 / 065
              yield! GridTemplateColumnsCheck.check sources // FUARAN046
              yield! FormatCoherenceCheck.check sources // FUARAN061
              yield! WireSurvivabilityCheck.check options.Orchestrated sources ] // FUARAN084

        let preamble =
            match manifestPath with
            | None ->
                [ { Severity = Warning
                    Code = "FUARAN900"
                    Location =
                      { File = options.ProjectPath
                        Line = 1
                        Column = 1 }
                    Message =
                      "No fuaran-validator.manifest.json found next to the .fsproj — schema-coupled checks (Binding.Query name resolution, Action.Dispatch case names, RowType match) are silenced. See Fuaran/docs/VALIDATOR-MANIFEST.md."
                    AvailableFields = None
                    Suggestion = None } ]
            | Some _ -> []

        // Source-level pragmas filter the REPORTING layer — every check ran in
        // full and stayed oblivious to them. Applied to the preamble too, so a
        // project-wide code can be silenced from any of its own sources.
        let kept, suppressed =
            Suppressions.apply (Suppressions.collect sourceFiles) (preamble @ findings)

        return
            { Findings = kept
              ManifestPath = manifestPath
              ManifestLoaded = manifestPath.IsSome
              FilesWalked = sourceFiles.Length
              Suppressed = suppressed }
    }

/// Run every check with the default parser.
let run (options: RunOptions) : Async<RunResult> =
    runWith (Syntax.parser (FSharpChecker.Create())) options
