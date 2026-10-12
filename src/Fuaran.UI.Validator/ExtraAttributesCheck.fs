module Fuaran.UI.Validator.ExtraAttributesCheck

// ============================================================================
//  ExtraAttributes-sanitization check.
//
//  FUARAN060 (Warning) — a `Node.withExtraAttribute "key" "value"` call whose
//  literal `key` argument the one key rule (`Fuaran.UI.ExtraAttributeKey`)
//  refuses: outside the data-* / aria-* allowlist, a known dangerous key
//  (`on*` event handlers, `style`), or an unsafe attribute name. Renderer-
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


open Fuaran.UI.Validator.Findings
open Fuaran.UI.Validator.Syntax

type private ExtraAttrCall =
    { Location: Location
      KeyLiteral: string }

let private scan (sources: ParsedSource list) : ExtraAttrCall list =
    sources
    |> collectApps (fun file head args ->
        match args with
        | keyExpr :: _ when isQualified "Node" "withExtraAttribute" head ->
            match literalString keyExpr with
            | Some key when not (Fuaran.UI.ExtraAttributeKey.isAllowed key) ->
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
                Fuaran.UI.ExtraAttributeKey.refusal call.KeyLiteral
                |> Option.defaultValue "outside the data-* / aria-* allowlist"

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
