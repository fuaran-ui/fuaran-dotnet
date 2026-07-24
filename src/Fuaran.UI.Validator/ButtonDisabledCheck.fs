module Fuaran.UI.Validator.ButtonDisabledCheck

// ============================================================================
//  Button disabled-binding no-op check (FUARAN064).
//
//  Phase 129 added `ButtonSpec.Disabled : Binding<bool> option` so a button can
//  express "disabled while a calc is in flight" — the universal real-app need
//  a `calculate-button` (`prop.disabled model.Loading`) surfaces.
//  The point of a *bindable* disabled state is that it tracks live state.
//
//  A `Disabled = Some (Binding.Static false)` defeats that point: a constant-
//  false disabled binding never disables the button, so it is exactly
//  equivalent to omitting `Disabled` (whose default is `None`). It is almost
//  always an unfinished binding — the author wired the slot but forgot to point
//  it at the live state — so we surface it as a Warning that steers toward
//  `binding.state`.
//
//  Deliberately narrow to keep false positives near-zero (the Phase 129
//  deferral note's concern):
//   - `Some (Binding.Static true)` — a permanently-disabled placeholder button —
//     is legitimate and NOT flagged.
//   - A non-static binding (`Some (binding.state ...)`, `Some (Binding.Computed ...)`)
//     is the intended shape and NOT flagged.
//   - Only the unambiguous constant-`false` no-op fires.
//
//  Advisory only — emits a Warning (FUARAN064), never an Error, so it does not
//  fail the build and stays safe for incremental adoption.
// ============================================================================

open Fuaran.UI.Validator.AstWalker
open Fuaran.UI.Validator.Findings

let check (calls: FuaranCall list) : Finding list =
    calls
    |> List.choose (fun c ->
        match c.Ctor, c.A11yDetail with
        | "button", Some detail when detail.DisabledBoundToStaticFalse ->
            create
                Warning
                "FUARAN064"
                c.Location
                "Fuaran.button Disabled is bound to Binding.Static false — a constant-false disabled binding never disables the button, so it is equivalent to omitting Disabled (default None). This is almost always an unfinished binding: point Disabled at the live state, e.g. Disabled = Some (binding.state \"loading\" false). A permanently-disabled placeholder uses Binding.Static true and is not flagged."
            |> withRecovery
                []
                (Some
                    "bind Disabled to a Binding.State (e.g. binding.state \"loading\" false), or remove the no-op Disabled = Some (Binding.Static false)")
            |> Some
        | _ -> None)
