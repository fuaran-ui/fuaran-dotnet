module Fuaran.UI.Validator.Findings

// ============================================================================
//  Validator findings model.
//
//  A `Finding` is what a check emits. The validator collects findings across
//  every source file in the project and renders them per the §4d AI-recovery
//  shape (`ErrorRender.fs`). Errors fail the FAKE `Validate` target; warnings
//  print to stdout and do not fail the build.
// ============================================================================

open Fuaran.UI

/// A finding's location in source. Line / Column are 1-based per FCS convention.
type Location =
    { File: string; Line: int; Column: int }

/// One emitted check result — the shape the lints share
/// (`Fuaran.UI.PreEmitValidate.Finding`), located in source. `Code` and `Severity`
/// come from the registry entry the check names (`PreEmitValidate.FindingCodes`), so an
/// AI consumer can pattern-match on the failure class; `Message` is human-readable.
/// `AvailableFields` / `Remedy` are the §4d AI-recovery fields — populated when the
/// finding is something a re-emission could fix (unresolved query name → list of
/// registered names + best-guess suggestion), `None` otherwise. Severity is
/// `Fuaran.Core.Severity`.
type Finding = PreEmitValidate.Finding<Location>

/// A finding raised under `code` at `location`, at the code's own severity.
let create (code: PreEmitValidate.FindingCode) (location: Location) (message: string) : Finding =
    PreEmitValidate.Finding.create code location message

let withRecovery (available: string list) (suggestion: string option) (finding: Finding) : Finding =
    { finding with
        AvailableFields = Some available
        Remedy = suggestion }

/// The finding raised at `severity` rather than its code's default (FUARAN062, FUARAN084).
let withSeverity (severity: Fuaran.Core.Severity) (finding: Finding) : Finding =
    PreEmitValidate.Finding.withSeverity severity finding

/// The finding with its remedy — the substitute to write — stated.
let withRemedy (remedy: string) (finding: Finding) : Finding =
    PreEmitValidate.Finding.withRemedy remedy finding

let isError (finding: Finding) = PreEmitValidate.Finding.isError finding

/// Levenshtein distance, for best-guess recovery suggestions. Inputs are
/// identifier-sized, so the plain 2D table is fine.
let private levenshtein (a: string) (b: string) : int =
    let m = a.Length
    let n = b.Length

    if m = 0 then
        n
    elif n = 0 then
        m
    else
        let d = Array2D.create (m + 1) (n + 1) 0

        for i in 0..m do
            d[i, 0] <- i

        for j in 0..n do
            d[0, j] <- j

        for i in 1..m do
            for j in 1..n do
                let cost = if a[i - 1] = b[j - 1] then 0 else 1
                d[i, j] <- List.min [ d[i - 1, j] + 1; d[i, j - 1] + 1; d[i - 1, j - 1] + cost ]

        d[m, n]

/// The registered name closest to `target`, when it is close enough to be a
/// plausible typo of it.
let suggestSimilar (candidates: string seq) (target: string) : string option =
    let best =
        candidates
        |> Seq.map (fun c -> c, levenshtein target c)
        |> Seq.sortBy snd
        |> Seq.tryHead

    match best with
    | Some(name, distance) when distance <= 3 && distance <= max 2 (target.Length / 2) -> Some name
    | _ -> None
