module Fuaran.UI.Validator.Suppressions

// ============================================================================
//  Source-level suppression pragmas.
//
//  Some source deliberately holds a shape the validator is right to reject in
//  application code. The canonical case is a NEGATIVE TEST: a fixture whose
//  whole purpose is to construct the defect and assert the runtime reports it
//  (`PreEmitValidate` flagging a tabs node whose TabHeaders and Children
//  lengths disagree), or a unit test exercising a constructor in isolation
//  that the build-time rule requires to appear in a wider context. There the
//  finding is correct about the code and wrong about the intent, and no edit
//  to the fixture fixes it without destroying the test.
//
//  Two pragma forms, both ordinary F# comments:
//
//    // fuaran-validator: disable FUARAN044
//    // fuaran-validator: disable FUARAN042, FUARAN044 — negative-test fixture
//
//      File-scoped. Suppresses the listed codes anywhere in the file,
//      wherever the comment appears (convention: near the top, beside the
//      module's doc comment, so a reader meets it before the fixtures).
//
//    // fuaran-validator: disable-next-line FUARAN047
//
//      Suppresses the listed codes on the FOLLOWING source line only — the
//      precise form, for a single exceptional call site in a file that should
//      otherwise stay gated.
//
//  Any text after the codes is free prose, so a pragma can carry its
//  justification on the same line. Codes are matched as `FUARAN` + digits;
//  a pragma naming a code that never fires is silently inert.
//
//  Suppression applies to Warnings as well as Errors, and is keyed on the
//  finding's own (file, line, code) — so it is the reporting layer that is
//  filtered, not the checks, which stay oblivious to it.
// ============================================================================

open System
open System.IO
open System.Text.RegularExpressions
open Fuaran.UI.Validator.Findings

/// Pragma marker. Matched case-insensitively so `// FUARAN-VALIDATOR:` reads
/// the same as the lowercase convention.
let private pragma =
    Regex(@"//.*?fuaran-validator\s*:\s*(disable-next-line|disable)\b(?<rest>.*)$", RegexOptions.IgnoreCase)

let private codeToken = Regex(@"FUARAN\d+", RegexOptions.IgnoreCase)

/// The suppressions declared by one source file.
type FileSuppressions =
    {
        /// Codes suppressed everywhere in the file.
        FileWide: Set<string>
        /// Codes suppressed on a specific 1-based line.
        PerLine: Map<int, Set<string>>
    }

let private emptyFile =
    { FileWide = Set.empty
      PerLine = Map.empty }

let private normalise (path: string) =
    try
        Path.GetFullPath path
    with _ ->
        path

let private codesOn (rest: string) =
    codeToken.Matches rest
    |> Seq.map (fun m -> m.Value.ToUpperInvariant())
    |> Set.ofSeq

/// Parse one file's pragmas. Lines are 1-based to match FCS locations.
let parseLines (lines: string seq) : FileSuppressions =
    lines
    |> Seq.indexed
    |> Seq.fold
        (fun acc (idx, line) ->
            let m = pragma.Match line

            if not m.Success then
                acc
            else
                let codes = codesOn m.Groups["rest"].Value

                if Set.isEmpty codes then
                    // A pragma naming no code suppresses nothing — a bare
                    // `// fuaran-validator: disable` is a typo, not a blanket.
                    acc
                elif m.Groups[1].Value.Equals("disable-next-line", StringComparison.OrdinalIgnoreCase) then
                    // `idx` is 0-based, so the NEXT line is `idx + 2` 1-based.
                    let target = idx + 2

                    let merged =
                        match Map.tryFind target acc.PerLine with
                        | Some existing -> Set.union existing codes
                        | None -> codes

                    { acc with
                        PerLine = Map.add target merged acc.PerLine }
                else
                    { acc with
                        FileWide = Set.union acc.FileWide codes })
        emptyFile

/// Read every supplied source file and index its pragmas by full path.
let collect (files: string list) : Map<string, FileSuppressions> =
    files
    |> List.choose (fun file ->
        try
            Some(normalise file, parseLines (File.ReadLines file))
        with _ ->
            // An unreadable source is the walker's problem to report, not
            // ours — a missing suppression table just means nothing is
            // suppressed for that file.
            None)
    |> Map.ofList

let private suppresses (table: Map<string, FileSuppressions>) (finding: Finding) =
    match Map.tryFind (normalise finding.Location.File) table with
    | None -> false
    | Some fs ->
        let code = finding.Code.ToUpperInvariant()

        Set.contains code fs.FileWide
        || (match Map.tryFind finding.Location.Line fs.PerLine with
            | Some codes -> Set.contains code codes
            | None -> false)

/// Partition findings into those that survive and the count suppressed.
let apply (table: Map<string, FileSuppressions>) (findings: Finding list) : Finding list * int =
    let kept, dropped = findings |> List.partition (suppresses table >> not)
    kept, List.length dropped
