module Fuaran.UI.ThemeManifest.Decode

// ─── Canonical JSON → ThemeManifest ─────────────────────────────
//
// Parses with the portable `Json` module (`Json.fs`) — no external JSON
// dependency, so decode runs under both the Fable browser pipeline and
// the pure-.NET Expecto runner (FGP 4).
//
// Two top-level shapes are accepted:
//   1. A Fuaran manifest wrapper — `{ meta, tokens, roles, invariants }`.
//   2. A vanilla DTCG file — the token group tree at top level, no
//      wrapper. Decodes to a manifest with the tokens populated and
//      empty `Roles` / `Invariants` (so a DTCG file is not NIH-rejected).
// Detection: presence of a top-level `tokens` key selects shape (1).

open Fuaran.UI.Types
open Fuaran.UI.ThemeManifest

// ─── DTCG token-tree walk ───────────────────────────────────────

/// Walk a DTCG group/leaf node, accumulating flat dotted-name tokens.
/// A node is a leaf when it carries a `$value`; otherwise it is a group
/// whose non-`$`-prefixed children are recursed (prefixing names).
let rec private walkTokens (prefix: string) (node: JsonValue) : ManifestToken list =
    match node with
    | JObj fields ->
        match Json.field "$value" fields with
        | Some _ ->
            let value =
                Json.field "$value" fields
                |> Option.bind Json.asString
                |> Option.defaultValue ""

            let typ =
                Json.field "$type" fields |> Option.bind Json.asString |> Option.defaultValue ""

            let desc = Json.field "$description" fields |> Option.bind Json.asString

            let role =
                Json.field "$extensions" fields
                |> Option.bind Json.asObject
                |> Option.bind (Json.field "fuaran")
                |> Option.bind Json.asObject
                |> Option.bind (Json.field "role")
                |> Option.bind Json.asString

            [ { Name = prefix
                Type = typ
                Value = value
                Description = desc
                Role = role } ]
        | None ->
            fields
            |> List.filter (fun (k, _) -> not (k.StartsWith("$")))
            |> List.collect (fun (k, child) ->
                let childPrefix = if prefix = "" then k else prefix + "." + k
                walkTokens childPrefix child)
    | _ -> []

// ─── roles + invariants ─────────────────────────────────────────

let private parseRole (j: JsonValue) : ManifestRole =
    match Json.asObject j with
    | None -> ManifestRole.Named ""
    | Some fields ->
        match Json.field "tone" fields |> Option.bind Json.asString with
        | Some s ->
            match ManifestRole.toneOfString s with
            | Some t -> ManifestRole.Tone t
            | None -> ManifestRole.Named s
        | None ->
            Json.field "named" fields
            |> Option.bind Json.asString
            |> Option.defaultValue ""
            |> ManifestRole.Named

let private parseRoleBinding (j: JsonValue) : RoleBinding option =
    match Json.asObject j with
    | None -> None
    | Some fields ->
        match Json.field "token" fields |> Option.bind Json.asString with
        | Some token ->
            let role =
                Json.field "role" fields
                |> Option.map parseRole
                |> Option.defaultValue (ManifestRole.Named "")

            Some { Role = role; TokenName = token }
        | None -> None

let private parseInvariant (j: JsonValue) : Invariant option =
    match Json.asObject j with
    | None -> None
    | Some fields ->
        let weight =
            Json.field "weight" fields
            |> Option.bind Json.asNumber
            |> Option.defaultValue Invariant.DefaultWeight

        let str name =
            Json.field name fields |> Option.bind Json.asString

        let num name =
            Json.field name fields |> Option.bind Json.asNumber

        match str "kind" with
        | Some "ContrastFloor" ->
            let role = str "role" |> Option.defaultValue ""
            let minRatio = num "minRatio" |> Option.defaultValue 0.0

            Some
                { Kind = InvariantKind.ContrastFloor(role, minRatio)
                  Weight = weight }
        | Some "UsageBudget" ->
            let token = str "token" |> Option.defaultValue ""
            let target = num "targetPct" |> Option.defaultValue 0.0
            let tol = num "tolerancePct" |> Option.defaultValue 0.0

            Some
                { Kind = InvariantKind.UsageBudget(token, target, tol)
                  Weight = weight }
        | Some "MotionVoice" ->
            let maxDur = num "maxDurationMs" |> Option.map int |> Option.defaultValue 0
            let easing = str "easing"

            Some
                { Kind =
                    InvariantKind.MotionVoice
                        { MaxDurationMs = maxDur
                          Easing = easing }
                  Weight = weight }
        | _ -> None

let private parseMeta (fields: (string * JsonValue) list) : ManifestMeta =
    { Name = Json.field "name" fields |> Option.bind Json.asString |> Option.defaultValue ""
      Version =
        Json.field "version" fields
        |> Option.bind Json.asString
        |> Option.defaultValue ""
      Description = Json.field "description" fields |> Option.bind Json.asString }

// ─── Top-level ──────────────────────────────────────────────────

/// Build a manifest from a parsed `JsonValue` AST (exposed for tests/tooling).
let ofJson (root: JsonValue) : ThemeManifest =
    match Json.asObject root with
    | None -> ThemeManifest.empty
    | Some fields ->
        match Json.field "tokens" fields with
        | Some tokensNode ->
            // Fuaran manifest wrapper.
            let meta =
                Json.field "meta" fields
                |> Option.bind Json.asObject
                |> Option.map parseMeta
                |> Option.defaultValue ManifestMeta.anonymous

            let roles =
                Json.field "roles" fields
                |> Option.bind Json.asArray
                |> Option.defaultValue []
                |> List.choose parseRoleBinding

            let invariants =
                Json.field "invariants" fields
                |> Option.bind Json.asArray
                |> Option.defaultValue []
                |> List.choose parseInvariant

            { Meta = meta
              Tokens = walkTokens "" tokensNode
              Roles = roles
              Invariants = invariants }
        | None ->
            // Vanilla DTCG file — the whole object is the token tree.
            { ThemeManifest.empty with
                Tokens = walkTokens "" root }

/// Decode a manifest from canonical JSON. `Error` carries the parse /
/// shape failure message; never throws.
let decode (json: string) : Result<ThemeManifest, string> =
    match Json.parse json with
    | Error e -> Error e
    | Ok root -> Ok(ofJson root)
