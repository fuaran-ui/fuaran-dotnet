module Fuaran.UI.Validator.WireSurvivabilityCheck

// ============================================================================
//  Wire-survivability check (Phase 378) — FUARAN084.
//
//  The build-time, author-facing lens over the wire-survivability boundary
//  (`Fuaran.UI.WireSurvivability`). It flags a `Binding.Computed` — the one
//  whole-case host-only escape that is cleanly detectable from the untyped AST:
//  a single `(BindingContext -> 'T)` closure that erases to the `"<closure>"`
//  sentinel, so a decoded / op-stream-replayed / introspected tree (and the
//  TypeScript / Python hosts) sees an inert placeholder.
//
//  Severity is context-sensitive per the phase's contract:
//    - hand-authored source  -> Warning (advisory; `Binding.Computed` is a
//      sanctioned F#-only escape),
//    - orchestrated / AI-emitted context (`orchestrated = true`) -> Error
//      (an AI author should stay on the wire-survivable substrate).
//
//  Scope boundary: the two other Phase 378 targets — a closure-formatted grid
//  column where `Column.Field` + `CellFormat` would survive, and a non-primitive
//  `Binding.Static` (the `"<opaque>"` FUARAN041 case) — need the runtime value's
//  TYPE to classify precisely, which the untyped AST does not carry (the
//  validator's deliberate v1 boundary — `AstWalker.fs`). Those stay with the
//  runtime `DeadOnDecode` lint (FUARAN080/081), which has the decoded tree in
//  hand; this build-time check owns the syntactically-unambiguous `Computed`.
//
//  The DECODE-SIDE twin is `Fuaran.UI.AffordanceInertness` (Phase 924), and the
//  departure from this module's shape is deliberate rather than an omission.
//  This check parses SOURCE and warns an AUTHOR before an emission exists; that
//  one takes a decoded `Node` tree and tells a CONSUMER which of the affordances
//  it is already holding do nothing. Neither can do the other's job: an author's
//  closures are real, so running this module's judgement over an F#-authored
//  tree is a false accusation, and by the time a tree has crossed the wire there
//  is no source left to parse and nothing left to refuse. Both read
//  `WireSurvivability`; only that table is shared.
// ============================================================================

open Fuaran.UI.Validator.Findings
open Fuaran.UI.Validator.Syntax

let private alternative =
    Fuaran.UI.WireSurvivability.byCase
    |> Map.tryFind "Binding.Computed"
    |> Option.bind (fun c -> c.Alternative)
    |> Option.defaultValue "Binding.State / Binding.Filter / Binding.Transform / Binding.Format"

/// `Binding.Computed f` / `binding.computed f` sites.
let private sites (sources: ParsedSource list) : Location list =
    sources
    |> collectApps (fun file head args ->
        if
            not args.IsEmpty
            && (isQualified "Binding" "Computed" head || isQualified "binding" "computed" head)
        then
            Some(mkLocation file head.Range)
        else
            None)

/// `orchestrated = true` escalates the finding to Error (an AI-emitted tree
/// must stay wire-survivable); hand-authored source gets a Warning (advisory).
let check (orchestrated: bool) (sources: ParsedSource list) : Finding list =
    let severity = if orchestrated then Error else Warning

    let message =
        sprintf
            "Binding.Computed is host-only — it erases to the \"<closure>\" sentinel on the wire, so a decoded / op-stream-replayed / introspected tree (and the TypeScript / Python hosts) sees an inert placeholder. Prefer %s.%s"
            alternative
            (if orchestrated then
                 " (An orchestrated / AI-emitted tree must stay on the wire-survivable substrate.)"
             else
                 " (Advisory: a sanctioned F#-only escape in hand-authored code.)")

    sites sources
    |> List.map (fun loc ->
        { create severity "FUARAN084" loc message with
            Suggestion = Some alternative })
