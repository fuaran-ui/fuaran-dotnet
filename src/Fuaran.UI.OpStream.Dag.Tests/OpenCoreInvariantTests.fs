module Fuaran.UI.OpStream.Dag.Tests.OpenCoreInvariantTests

open System
open System.IO
open System.Text.RegularExpressions
open Expecto

// ============================================================================
//  Open-core invariant (Phase 331). The Apache-2.0 DAG merge package + the
//  provider-call telemetry abstraction must carry private meaning as OPAQUE data,
//  never as a typed public case or a comment that names the private mechanism. A
//  reader of the public source learns the SHAPE of every seam and nothing about
//  the private orchestrator (human/AI co-authoring, self-correction repair,
//  LLM-as-judge). This test greps the shipped source for that vocabulary and for
//  any merge-side `PromptId` decode, failing on a regression — a public surface is
//  searchable, so a leak here is permanent.
//
//  Note: legitimate generic English ("human-readable", "AI provider") is NOT
//  banned — only the co-authoring / repair / judge mechanism vocabulary.
// ============================================================================

/// Walk up from the test assembly to the repo root (the dir holding Fuaran.sln).
let rec private findRepoRoot (dir: DirectoryInfo | null) : DirectoryInfo =
    match dir with
    | null -> failwith "open-core invariant: repo root (Fuaran.sln) not found above the test assembly"
    | d ->
        if File.Exists(Path.Combine(d.FullName, "Fuaran.sln")) then
            d
        else
            findRepoRoot d.Parent

let private srcDir =
    Path.Combine((findRepoRoot (DirectoryInfo AppContext.BaseDirectory)).FullName, "src")

/// The private-mechanism vocabulary that must never appear in a public type,
/// case, or comment in the in-scope packages. Each is a case-insensitive regex.
let private bannedPatterns =
    [ @"human[-\s]?primac" // human primacy / human-primacy
      @"human[-\s]?pin" // human pin / human-pinned
      @"human[-\s]?author" // human-authored / human authorship
      @"\bKeepHuman\b"
      @"\bKeepAi\b"
      @"MergeAuthor\.Human"
      @"MergeAuthor\.Ai\b"
      @"\bHumanAuthored\b"
      @"\bAiRationale\b"
      @"\|\s*Repair\b" // a public `Repair` DU case
      @"\|\s*Judge\b" // a public `Judge` DU case
      @"ProviderOperation\.(Repair|Judge)"
      @"LLM[-\s]?as[-\s]?judge"
      @"self[-\s]?correction"
      @"co[-\s]?author" ]
    |> List.map (fun p -> p, Regex(p, RegexOptions.IgnoreCase))

/// The merge package's own source files (Apache-2.0, OSS-public).
let private mergeFiles () : string list =
    Directory.GetFiles(Path.Combine(srcDir, "Fuaran.UI.OpStream.Dag.Merge"), "*.fs")
    |> Array.toList

/// The full in-scope set Phase 331 hardened: the DAG merge package + the
/// provider-call telemetry abstraction (the `ProviderOperation` file).
let private inScopeFiles () : string list =
    Path.Combine(srcDir, "Fuaran.UI.Telemetry.Abstractions", "ProviderCallTelemetry.fs")
    :: mergeFiles ()

[<Tests>]
let tests =
    testList
        "OpenCoreInvariant"
        [ test "no in-scope public file names a private mechanism (human/AI authorship, repair, judge)" {
              let files = inScopeFiles ()
              Expect.isNonEmpty files "located the in-scope source files"

              let hits =
                  [ for file in files do
                        let text = File.ReadAllText file

                        for pattern, rx in bannedPatterns do
                            if rx.IsMatch text then
                                yield sprintf "%s :: /%s/" (Path.GetFileName file) pattern ]

              Expect.isEmpty hits (sprintf "private-mechanism vocabulary leaked into the public surface: %A" hits)
          }

          test "the merge package derives nothing from `PromptId` (precedence is host-classified)" {
              // The merge layer must read no record field to decide precedence —
              // the opaque `promptId` id stays on the wire, interpreted only by the
              // host's `recordAuthor` classifier. Zero `PromptId` tokens in the
              // package proves the None/Some authorship decode moved out.
              let offenders =
                  [ for file in mergeFiles () do
                        if Regex.IsMatch(File.ReadAllText file, @"\bPromptId\b") then
                            yield Path.GetFileName file ]

              Expect.isEmpty offenders (sprintf "merge package still references PromptId: %A" offenders)
          } ]
