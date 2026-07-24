module Fuaran.UI.ThemeManifest.Encode

// ─── ThemeManifest → canonical JSON ─────────────────────────────
//
// Builds the portable `JsonValue` AST (`Json.fs`) and serialises it —
// no external JSON dependency, so encode runs byte-identically under
// Fable + .NET. The `tokens` block is emitted as a genuine DTCG group
// tree (nested `$type`/`$value`/`$description` leaves, the SPEC `role`
// tag under `$extensions.fuaran`), so a downstream DTCG tool reads the
// values and a vanilla DTCG file decodes back through `Decode`. `roles`
// + `invariants` are the Fuaran extensions DTCG lacks.
//
// `JObj` is an ordered pair list, so key order is the order written
// here — encode is a pure, stable function of the manifest value.

open Fuaran.UI.Types
open Fuaran.UI.ThemeManifest

let private metaJson (meta: ManifestMeta) : JsonValue =
    JObj(
        [ Some("name", JStr meta.Name)
          Some("version", JStr meta.Version)
          meta.Description |> Option.map (fun d -> "description", JStr d) ]
        |> List.choose id
    )

// ─── DTCG token-tree emission ───────────────────────────────────

let private leafJson (t: ManifestToken) : JsonValue =
    JObj(
        [ Some("$type", JStr t.Type)
          Some("$value", JStr t.Value)
          t.Description |> Option.map (fun d -> "$description", JStr d)
          t.Role
          |> Option.map (fun r -> "$extensions", JObj [ "fuaran", JObj [ "role", JStr r ] ]) ]
        |> List.choose id
    )

/// Rebuild the nested DTCG group tree from the flat dotted-name token
/// list. Token names are assumed prefix-collision-free (DTCG forbids a
/// group and a token sharing a path).
let rec private buildGroup (entries: (string list * ManifestToken) list) : JsonValue =
    let heads = entries |> List.map (fst >> List.head) |> List.distinct

    JObj
        [ for head in heads do
              let members = entries |> List.filter (fun (segs, _) -> List.head segs = head)

              match members with
              | [ ([ _ ], token) ] -> head, leafJson token
              | _ ->
                  let deeper = members |> List.map (fun (segs, t) -> List.tail segs, t)
                  head, buildGroup deeper ]

let private tokensJson (tokens: ManifestToken list) : JsonValue =
    if List.isEmpty tokens then
        JObj []
    else
        tokens |> List.map (fun t -> t.Name.Split('.') |> Array.toList, t) |> buildGroup

// ─── roles + invariants ─────────────────────────────────────────

let private roleJson (role: ManifestRole) : JsonValue =
    match role with
    | ManifestRole.Tone t -> JObj [ "tone", JStr(ManifestRole.toneToString t) ]
    | ManifestRole.Named n -> JObj [ "named", JStr n ]

let private roleBindingJson (b: RoleBinding) : JsonValue =
    JObj [ "role", roleJson b.Role; "token", JStr b.TokenName ]

let private invariantJson (inv: Invariant) : JsonValue =
    let weight = "weight", JNum inv.Weight

    match inv.Kind with
    | InvariantKind.ContrastFloor(role, minRatio) ->
        JObj
            [ "kind", JStr "ContrastFloor"
              "role", JStr role
              "minRatio", JNum minRatio
              weight ]
    | InvariantKind.UsageBudget(token, target, tol) ->
        JObj
            [ "kind", JStr "UsageBudget"
              "token", JStr token
              "targetPct", JNum target
              "tolerancePct", JNum tol
              weight ]
    | InvariantKind.MotionVoice mb ->
        JObj(
            [ Some("kind", JStr "MotionVoice")
              Some("maxDurationMs", JNum(float mb.MaxDurationMs))
              mb.Easing |> Option.map (fun e -> "easing", JStr e)
              Some weight ]
            |> List.choose id
        )

// ─── Top-level ──────────────────────────────────────────────────

/// Build the `JsonValue` AST for a manifest (exposed for tests/tooling).
let toJson (m: ThemeManifest) : JsonValue =
    JObj
        [ "meta", metaJson m.Meta
          "tokens", tokensJson m.Tokens
          "roles", JArr(m.Roles |> List.map roleBindingJson)
          "invariants", JArr(m.Invariants |> List.map invariantJson) ]

/// Encode a manifest to canonical JSON.
let encode (m: ThemeManifest) : string = Json.serialize (toJson m)
