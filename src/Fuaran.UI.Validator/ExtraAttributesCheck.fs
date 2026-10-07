module Fuaran.UI.Validator.ExtraAttributesCheck

// ============================================================================
//  ExtraAttributes-sanitization check.
//
//  FUARAN060 (Warning) — a `Node.withExtraAttribute "key" "value"` call whose
//  literal `key` argument violates the data-* / aria-* allowlist OR matches
//  a known dangerous key prefix (`on*` event handlers, `style`). Renderer-
//  side `Sanitize.sanitizeExtraAttributes` is the runtime floor, but the
//  build-time signal catches the author mistake before it ships.
//
//  The rule walks the untyped F# AST. It detects
//  `Node.withExtraAttribute <literal-key> <literal-value>` shapes
//  specifically — non-literal key expressions (e.g. a computed key from a
//  let binding) are silenced; the renderer-time floor is the only gate for
//  those.
//
//  A visitor over the shared parse (`Syntax`), like every other check.
// ============================================================================


open System
open Fuaran.UI.Validator.Findings
open Fuaran.UI.Validator.Syntax

type private ExtraAttrCall =
    { Location: Location
      KeyLiteral: string }

let private isAllowedKey (key: string) : bool =
    if String.IsNullOrEmpty key then
        false
    else
        let trimmed = key.Trim()

        if trimmed.StartsWith("on", StringComparison.OrdinalIgnoreCase) then
            false
        elif trimmed.Equals("style", StringComparison.OrdinalIgnoreCase) then
            false
        else
            trimmed.StartsWith("data-", StringComparison.Ordinal)
            || trimmed.StartsWith("aria-", StringComparison.Ordinal)


let private scan (sources: ParsedSource list) : ExtraAttrCall list =
    sources
    |> collectApps (fun file head args ->
        match args with
        | keyExpr :: _ when isQualified "Node" "withExtraAttribute" head ->
            match literalString keyExpr with
            | Some key when not (isAllowedKey key) ->
                Some
                    { Location = mkLocation file keyExpr.Range
                      KeyLiteral = key }
            | _ -> None
        | _ -> None)

let check (sources: ParsedSource list) : Finding list =
    let allCalls = scan sources

    let findings =
        allCalls
        |> List.map (fun call ->
            let reason =
                let trimmed = call.KeyLiteral.Trim()

                if trimmed.StartsWith("on", StringComparison.OrdinalIgnoreCase) then
                    "event-handler attribute (on*) — would inject inline script if reached the DOM"
                elif trimmed.Equals("style", StringComparison.OrdinalIgnoreCase) then
                    "raw CSS sink — vector for content-spoofing and legacy expression() injection"
                else
                    "outside the data-* / aria-* allowlist"

            let base' =
                create
                    Warning
                    "FUARAN060"
                    call.Location
                    (sprintf
                        "Node.withExtraAttribute key \"%s\" is %s. The render-time Sanitize.sanitizeExtraAttributes floor will drop this entry, but the build-time signal catches it earlier. Use a data-* or aria-* key, or move the behaviour into a typed Action / Accessibility field."
                        call.KeyLiteral
                        reason)

            withRecovery
                [ "data-<custom-name>"; "aria-<standard-name>" ]
                (Some "rename the key to a data-* test hook or aria-* accessibility attribute")
                base')


    findings
