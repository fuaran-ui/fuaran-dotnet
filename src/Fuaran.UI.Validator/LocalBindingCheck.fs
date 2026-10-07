module Fuaran.UI.Validator.LocalBindingCheck

// ============================================================================
//  Local-binding check.
//
//  Three defect codes — FUARAN042 / FUARAN043 / FUARAN044 — guard the typed
//  contract for `binding.local`:
//
//   - FUARAN042 LocalBindingMissingFormatParse  (Error): the `format`
//     argument of `binding.local` is `None` (statically detected). The
//     renderer's default `string<'T>` fallback works for trivial cases but
//     the canonical Salary-style use needs a thousands-separated formatter;
//     the anti-pattern "Don't bypass Parse errors silently" pairs
//     with this rule.
//
//   - FUARAN043 LocalBindingFlushOnNeverCommits (Warning): a `binding.local`
//     with `flushOn = OnCommitAction` declared on a field whose `Id`
//     never appears as the payload of an `Action.CommitLocal` in the same
//     module. Without the explicit-commit-action partner, the buffer
//     never drains.
//
//   - FUARAN044 LocalBindingOnNonInputField     (Error): `binding.local`
//     appears outside a `FormFieldKind.Text(...)` / `FormFieldKind.Number(...)`
//     / `FormFieldKind.RangedNumber(...)` enclosing position (e.g. assigned
//     to a `Metric.Source` slot). The renderer's useState-slot wiring is only
//     active for those three kinds; anywhere else the binding silently
//     resolves to its InitialFrom-side and the local-buffer semantics are
//     lost. The recognised set is `localHostingKinds` below and must track
//     `Render.fs`'s `Binding.Local` arms — omitting a kind the renderer does
//     support turns this Error into a false positive on correct code.
//
//  All three rules walk the untyped F# AST per the validator's scope —
//  they don't consult the typed type-universe. A `binding.local` call
//  enclosing context is detected lexically (the nearest enclosing
//  local-hosting `FormFieldKind` application).
// ============================================================================


open FSharp.Compiler.Syntax
open Fuaran.UI.Validator.Findings
open Fuaran.UI.Validator.Syntax

/// The field kinds whose renderer arm mounts the per-NodeId `useState`
/// buffer — i.e. the positions where a `Binding.Local` actually behaves as a
/// local buffer. Kept in step with `Render.fs`'s `Binding.Local` arms:
/// `FormFieldKind.Text`, `.Number` and `.RangedNumber` (the last renders
/// "exactly like the plain Number arm, Local-bound variant included") — the DU
/// cases, and the `FormFieldKind` smart constructors that lower to them.
let private localHostingKinds =
    Set.ofList
        [ "Text"
          "Number"
          "RangedNumber"
          "textDeclarative"
          "numberDeclarative"
          "numberStepped"
          "rangedNumber"
          "rangedNumberDeclarative" ]

type private LocalBindingCall =
    {
        Location: Location
        /// `format` (the 4th argument of `binding.local`) written as `None`.
        FormatExplicitNone: bool
        /// `flushOn` (the 2nd argument) written as `OnCommitAction`.
        FlushOnIsCommitAction: bool
        /// The nearest enclosing local-hosting `FormFieldKind`, if any.
        EnclosingKind: string option
    }

let private isOnCommitAction (expr: SynExpr) =
    match leafIdent (unwrap expr) with
    | Some(_, "OnCommitAction", _) -> true
    | _ -> false

let private hostingKind (head: SynExpr) =
    match leafIdent head with
    | Some(prefix, leaf, _) when
        not prefix.IsEmpty
        && List.last prefix = "FormFieldKind"
        && localHostingKinds.Contains leaf
        ->
        Some leaf
    | _ -> None

/// The `binding.local` sites and whether any `Action.CommitLocal` appears.
let private scan (sources: ParsedSource list) =
    let calls = ResizeArray<LocalBindingCall>()
    let mutable anyCommitLocal = false

    for source in sources do
        let rec visit (enclosing: string option) (expr: SynExpr) =
            let head, args = flattenApp expr

            if isQualified "Action" "CommitLocal" head && not args.IsEmpty then
                anyCommitLocal <- true

            if isQualified "binding" "local" head && not args.IsEmpty then
                calls.Add
                    { Location = mkLocation source.File head.Range
                      FormatExplicitNone =
                        args |> List.tryItem 3 |> Option.map isExplicitNone |> Option.defaultValue false
                      FlushOnIsCommitAction =
                        args
                        |> List.tryItem 1
                        |> Option.map isOnCommitAction
                        |> Option.defaultValue false
                      EnclosingKind = enclosing }

                args |> List.iter (iterExpr (visit enclosing))
                false
            else
                match hostingKind head with
                | Some kind when not args.IsEmpty ->
                    args |> List.iter (iterExpr (visit (Some kind)))
                    false
                | _ -> true

        for expr in topLevelExprs source do
            iterExpr (visit None) expr

    List.ofSeq calls, anyCommitLocal

let check (sources: ParsedSource list) : Finding list =
    let calls, anyCommitLocalRef = scan sources

    calls
    |> List.collect (fun call ->
        [ if call.FormatExplicitNone then
              create
                  Error
                  "FUARAN042"
                  call.Location
                  "binding.local was given `format = None` — a Local-bound text/number input needs a typed display formatter to round-trip the buffer (the Salary-style canonical shape uses thousands-separator formatting). Pass `Some <formatter>` or use `binding.state` instead if no formatting is needed."

          // FUARAN043: module-wide rather than tree-local — accurate per-NodeId
          // scoping needs the form's field ids in scope, which the walk does
          // not extract.
          if call.FlushOnIsCommitAction && not anyCommitLocalRef then
              create
                  Warning
                  "FUARAN043"
                  call.Location
                  "binding.local was declared with FlushOn = OnCommitAction, but no `Action.CommitLocal _` reference was found in the project. The buffer will never drain — wire an inline button or external commit trigger that dispatches Action.CommitLocal with this field's id, or pick a different FlushOn (OnBlur for free text, OnSubmit for form-scoped commit)."

          if call.EnclosingKind.IsNone then
              create
                  Error
                  "FUARAN044"
                  call.Location
                  "binding.local was used outside an enclosing FormFieldKind.Text(...) / FormFieldKind.Number(...) / FormFieldKind.RangedNumber(...) constructor — the renderer's useState slot is only mounted for those three field kinds. Move the Local binding into a form-field Text/Number/RangedNumber value, or use binding.state / binding.query for read-side bindings." ])
