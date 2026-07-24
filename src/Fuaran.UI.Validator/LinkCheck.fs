module Fuaran.UI.Validator.LinkCheck

// ============================================================================
//  Link href check (Phase 139).
//
//  A `DisplayKind.Link` renders a real `<a href>`. A blank href (`""` or
//  whitespace) renders an anchor that navigates to the *current* page — almost
//  always an authoring mistake (a forgotten URL, or a stateful gesture that
//  should be a `Button` + `Action.Navigate` rather than a crawlable link).
//
//  Advisory only — emits a Warning (FUARAN063), never an Error, so it does not
//  fail the build and stays safe for incremental adoption. Consistent with the
//  other advisory rules (FUARAN045/046/050/053–058).
//
//  Only fires on a statically-knowable href literal — the positional
//  `Fuaran.link "id" "href" "label"` 2nd argument, or `Href = Binding.Static
//  "..."` in the `Fuaran.linkSpec` record form. An href bound to a query /
//  state / computed source carries no compile-time value and is left alone.
//
//  Link-vs-Navigate steer: when the author wants an in-app routing *gesture*
//  with no real URL (a button that triggers client-side navigation), that is a
//  `Button` + `Action.Navigate`, not a `Link` — see the "Links and navigation"
//  section of `docs/AI_AUTHORING_GUIDE.md`. The renderer collapses a rejected
//  or empty href to `about:blank` at runtime; this rule surfaces the blank at
//  build time so it is caught before it ships.
// ============================================================================

open Fuaran.UI.Validator.AstWalker
open Fuaran.UI.Validator.Findings

let check (calls: FuaranCall list) : Finding list =
    calls
    |> List.choose (fun c ->
        match c.LinkDetail with
        | Some { HrefLiteral = Some href } when System.String.IsNullOrWhiteSpace href ->
            let base' =
                create
                    Warning
                    "FUARAN063"
                    c.Location
                    "DisplayKind.Link has a blank Href. A link with an empty href renders an <a> that navigates to the current page — provide a real destination URL. If you meant an in-app routing gesture (no crawlable URL), use a Button + Action.Navigate instead."

            Some(
                withRecovery
                    []
                    (Some
                        "set Href to a non-empty destination URL, or use Fuaran.button + Action.Navigate for a stateful in-app gesture")
                    base'
            )
        | _ -> None)
