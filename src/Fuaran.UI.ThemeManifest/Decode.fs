module Fuaran.UI.ThemeManifest.Decode

// ─── Canonical JSON → ThemeManifest ─────────────────────────────
//
// Parses through Core's portable reader (`Fuaran.Core.Json.read`, FSharp.Core-only
// and Fable-clean) into Core's `JVal`, the AST every package in the tier decodes
// from, so decode runs under both the Fable browser pipeline and the pure-.NET
// Expecto runner (FGP 4). The read is BOUNDED (Phase 2310) by `limits` below,
// which are WIRE_FORMAT §21.1's syntactic limits (Phase 2653): a document past
// any bound is refused by name, never a stack overflow.
//
// Two top-level shapes are accepted:
//   1. A Fuaran manifest wrapper — `{ meta, tokens, roles, invariants }`.
//   2. A vanilla DTCG file — the token group tree at top level, no
//      wrapper. Decodes to a manifest with the tokens populated and
//      empty `Roles` / `Invariants` (so a DTCG file is not NIH-rejected).
// Detection: presence of a top-level `tokens` key selects shape (1).
//
// **Tolerant, and total.** An absent member decodes to its default: the
// cross-host byte law OMITS a member at its default value, so a strict decoder
// would refuse every other host's canonical output. Whether the decoded values
// MEAN anything — a contrast floor of 0, a role nothing binds, a budget on an
// undeclared token, a kind this host does not know — is the validation pass's
// question (`ThemeManifest.validate`, Phase 2133), asked after decode.

open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.ThemeManifest

// ─── The bounded read ───────────────────────────────────────────

/// The bounds a manifest read enforces, each checked by Core's reader before the
/// recursion or allocation it guards: WIRE_FORMAT §21.1's SYNTACTIC limits, the
/// same values a document is read under (Phase 2653). A manifest travels between
/// hosts as a string every host decodes (the Phase 2133 byte law), so its bounds
/// are the format's and not this host's: JSON depth 256, strings 1,048,576 code
/// points, arrays and objects 100,000, the document 33,554,432 UTF-8 bytes. The
/// node bounds do not apply; a manifest has no nodes.
let limits: JsonLimits =
    { MaxDepth = Fuaran.UI.WireLimits.MaxJsonDepth
      MaxStringLength = Fuaran.UI.WireLimits.MaxStringLength
      MaxArrayLength = Fuaran.UI.WireLimits.MaxArrayLength
      MaxObjectMembers = Fuaran.UI.WireLimits.MaxArrayLength
      MaxDocumentBytes = Fuaran.UI.WireLimits.MaxDocumentBytes }

/// What a manifest read reads under: `limits`, and a member `null` erased to
/// absence. The decoder reads an absent member as its default, and a `null`
/// member says the same thing; a bare or array-element `null` is refused.
let readOptions: ReadOptions =
    { Null = EraseMemberNull
      Limits = limits }

/// The first member named `name`.
let private field (name: string) (pairs: (string * JVal) list) : JVal option =
    pairs |> List.tryPick (fun (k, v) -> if k = name then Some v else None)

let private asString =
    function
    | JStr s -> Some s
    | _ -> None

let private asArray =
    function
    | JArr a -> Some a
    | _ -> None

let private asObject =
    function
    | JObj o -> Some o
    | _ -> None

// ─── DTCG token-tree walk ───────────────────────────────────────

/// Walk a DTCG group/leaf node, accumulating flat dotted-name tokens.
/// A node is a leaf when it carries a `$value`; otherwise it is a group
/// whose non-`$`-prefixed children are recursed (prefixing names).
let rec private walkTokens (prefix: string) (node: JVal) : ManifestToken list =
    match node with
    | JObj fields ->
        match field "$value" fields with
        | Some _ ->
            let value = field "$value" fields |> Option.bind asString |> Option.defaultValue ""

            let typ = field "$type" fields |> Option.bind asString |> Option.defaultValue ""

            let desc = field "$description" fields |> Option.bind asString

            let role =
                field "$extensions" fields
                |> Option.bind asObject
                |> Option.bind (field "fuaran")
                |> Option.bind asObject
                |> Option.bind (field "role")
                |> Option.bind asString

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

let private parseRole (j: JVal) : ManifestRole =
    match asObject j with
    | None -> ManifestRole.Named ""
    | Some fields ->
        match field "tone" fields |> Option.bind asString with
        | Some s ->
            match ManifestRole.toneOfString s with
            | Some t -> ManifestRole.Tone t
            | None -> ManifestRole.Named s
        | None ->
            field "named" fields
            |> Option.bind asString
            |> Option.defaultValue ""
            |> ManifestRole.Named

let private parseRoleBinding (j: JVal) : RoleBinding option =
    match asObject j with
    | None -> None
    | Some fields ->
        match field "token" fields |> Option.bind asString with
        | Some token ->
            let role =
                field "role" fields
                |> Option.map parseRole
                |> Option.defaultValue (ManifestRole.Named "")

            Some { Role = role; TokenName = token }
        | None -> None

let private parseInvariant (j: JVal) : Invariant option =
    match asObject j with
    | None -> None
    | Some fields ->
        let weight =
            field "weight" fields
            |> Option.bind JVal.asFloat
            |> Option.defaultValue Invariant.DefaultWeight

        let str name =
            field name fields |> Option.bind asString

        let num name =
            field name fields |> Option.bind JVal.asFloat

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
        | Some "ChartSeriesDistinct" ->
            Some
                { Kind = InvariantKind.ChartSeriesDistinct
                  Weight = weight }
        | Some other when other <> "" ->
            // Phase 2133 — a kind this host does not know is KEPT with its
            // members, never dropped: the validation pass reports it unenforced.
            let members = fields |> List.filter (fun (k, _) -> k <> "kind" && k <> "weight")

            Some
                { Kind = InvariantKind.Unknown(other, members)
                  Weight = weight }
        // No non-empty string `kind`: not an invariant of any kind.
        | _ -> None

let private parseMeta (fields: (string * JVal) list) : ManifestMeta =
    { Name = field "name" fields |> Option.bind asString |> Option.defaultValue ""
      Version = field "version" fields |> Option.bind asString |> Option.defaultValue ""
      Description = field "description" fields |> Option.bind asString }

// ─── Top-level ──────────────────────────────────────────────────

/// Build a manifest from a parsed `JVal` AST (exposed for tests/tooling).
let ofJson (root: JVal) : ThemeManifest =
    match asObject root with
    | None -> ThemeManifest.empty
    | Some fields ->
        match field "tokens" fields with
        | Some tokensNode ->
            // Fuaran manifest wrapper.
            let meta =
                field "meta" fields
                |> Option.bind asObject
                |> Option.map parseMeta
                |> Option.defaultValue ManifestMeta.anonymous

            let roles =
                field "roles" fields
                |> Option.bind asArray
                |> Option.defaultValue []
                |> List.choose parseRoleBinding

            let invariants =
                field "invariants" fields
                |> Option.bind asArray
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

/// Decode a manifest from canonical JSON under `readOptions`. `Error` carries
/// the refusal as Core's decode-error sentence: malformed JSON, or a bound of
/// `limits` passed, named with its declared value and its path. Never throws.
let decode (json: string) : Result<ThemeManifest, string> =
    match Json.read readOptions json with
    | Error refusal -> Error(DecodeError.render (DecodeError.ofJsonRefusal refusal))
    | Ok root -> Ok(ofJson root)
