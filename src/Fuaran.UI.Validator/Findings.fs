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
