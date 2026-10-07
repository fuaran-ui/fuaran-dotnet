module Fuaran.UI.Validator.Findings

// ============================================================================
//  Validator findings model.
//
//  A `Finding` is what a check emits. The validator collects findings across
//  every source file in the project and renders them per the §4d AI-recovery
//  shape (`ErrorRender.fs`). Errors fail the FAKE `Validate` target; warnings
//  print to stdout and do not fail the build.
// ============================================================================

type Severity =
    | Error
    | Warning

/// A finding's location in source. Line / Column are 1-based per FCS convention.
type Location =
    { File: string; Line: int; Column: int }

/// One emitted check result. `Code` is a short stable id (e.g. "FUARAN001")
/// so an AI consumer can pattern-match on the failure class; `Message` is
/// human-readable. `AvailableFields` / `Suggestion` are the §4d AI-recovery
/// fields — populated when the finding is something a re-emission could fix
/// (unresolved query name → list of registered names + best-guess
/// suggestion), `None` otherwise.
type Finding =
    { Severity: Severity
      Code: string
      Location: Location
      Message: string
      AvailableFields: string list option
      Suggestion: string option }

let create severity code location message =
    { Severity = severity
      Code = code
      Location = location
      Message = message
      AvailableFields = None
      Suggestion = None }

let withRecovery (available: string list) (suggestion: string option) (finding: Finding) : Finding =
    { finding with
        AvailableFields = Some available
        Suggestion = suggestion }

let isError (finding: Finding) =
    match finding.Severity with
    | Error -> true
    | Warning -> false

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
