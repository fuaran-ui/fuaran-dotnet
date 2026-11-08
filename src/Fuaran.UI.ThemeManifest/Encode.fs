module Fuaran.UI.ThemeManifest.Encode

// ─── ThemeManifest → canonical JSON ─────────────────────────────
//
// Builds Core's `JVal` and renders it through Core's cross-host canonical
// writer (`Fuaran.Core.Wire`, FSharp.Core-only and Fable-clean), so encode
// runs byte-identically under Fable + .NET. The `tokens` block is emitted as a genuine DTCG group
// tree (nested `$type`/`$value`/`$description` leaves, the SPEC `role`
// tag under `$extensions.fuaran`), so a downstream DTCG tool reads the
// values and a vanilla DTCG file decodes back through `Decode`. `roles`
// + `invariants` are the Fuaran extensions DTCG lacks.
//
// **The cross-host byte law (Phase 2133).** The bytes are the canonical
// manifest every manifest host writes: object keys Ordinal-sorted (UTF-16
// code-unit order, WIRE_FORMAT §2 rule 2), no whitespace, and every member
// the decoder tolerates the absence of OMITTED at its default — an empty
// `$type`, `meta` name or version, an anonymous role, a zero number, a
// default weight, an empty `meta` / `roles` / `invariants` block. `tokens` is
// never omitted: its presence is what selects the wrapper shape at decode.
// Omitting a member is not a weaker statement than writing its default; what
// the decoded values MEAN is the validation pass's question
// (`ThemeManifest.validate`), not the encoder's.

open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.ThemeManifest

/// A member written only when it differs from the value the decoder supplies
/// in its absence.
let private str (key: string) (value: string) : (string * JVal) option =
    if value = "" then None else Some(key, JStr value)

let private num (key: string) (value: float) (dflt: float) : (string * JVal) option =
    if value = dflt then None else Some(key, JFloat value)

let private metaJson (meta: ManifestMeta) : JVal option =
    if meta = ManifestMeta.anonymous then
        None
    else
        Some(
            JObj(
                [ str "name" meta.Name
                  str "version" meta.Version
                  meta.Description |> Option.map (fun d -> "description", JStr d) ]
                |> List.choose id
            )
        )

// ─── DTCG token-tree emission ───────────────────────────────────

/// One DTCG leaf. `$value` is the leaf discriminator the decoder's walk stops
/// on, so it is written even when empty.
let private leafJson (t: ManifestToken) : JVal =
    JObj(
        [ str "$type" t.Type
          Some("$value", JStr t.Value)
          t.Description |> Option.map (fun d -> "$description", JStr d)
          t.Role
          |> Option.map (fun r -> "$extensions", JObj [ "fuaran", JObj [ "role", JStr r ] ]) ]
        |> List.choose id
    )

/// Rebuild the nested DTCG group tree from the flat dotted-name token
/// list. Token names are assumed prefix-collision-free (DTCG forbids a
/// group and a token sharing a path).
let rec private buildGroup (entries: (string list * ManifestToken) list) : JVal =
    let heads = entries |> List.map (fst >> List.head) |> List.distinct

    JObj
        [ for head in heads do
              let members = entries |> List.filter (fun (segs, _) -> List.head segs = head)

              match members with
              | [ ([ _ ], token) ] -> head, leafJson token
              | _ ->
                  let deeper = members |> List.map (fun (segs, t) -> List.tail segs, t)
                  head, buildGroup deeper ]

let private tokensJson (tokens: ManifestToken list) : JVal =
    if List.isEmpty tokens then
        JObj []
    else
        tokens |> List.map (fun t -> t.Name.Split('.') |> Array.toList, t) |> buildGroup

// ─── roles + invariants ─────────────────────────────────────────

let private roleJson (role: ManifestRole) : JVal =
    match role with
    | ManifestRole.Tone t -> JObj [ "tone", JStr(ManifestRole.toneToString t) ]
    | ManifestRole.Named n -> JObj [ "named", JStr n ]

/// `token` is required (a binding without one is dropped at decode); an absent
/// `role` decodes to `Named ""`, so that one role is omitted.
let private roleBindingJson (b: RoleBinding) : JVal =
    JObj(
        [ (if b.Role = ManifestRole.Named "" then
               None
           else
               Some("role", roleJson b.Role))
          Some("token", JStr b.TokenName) ]
        |> List.choose id
    )

/// `kind` is the discriminator and is always written; every other member is
/// omitted at the default the decoder supplies for it.
let private invariantJson (inv: Invariant) : JVal =
    let kind = Some("kind", JStr(Invariant.kind inv))
    let weight = num "weight" inv.Weight Invariant.DefaultWeight

    let members =
        match inv.Kind with
        | InvariantKind.ContrastFloor(role, minRatio) -> [ str "role" role; num "minRatio" minRatio 0.0 ]
        | InvariantKind.UsageBudget(token, target, tol) ->
            [ str "token" token; num "targetPct" target 0.0; num "tolerancePct" tol 0.0 ]
        | InvariantKind.MotionVoice mb ->
            [ num "maxDurationMs" (float mb.MaxDurationMs) 0.0
              mb.Easing |> Option.map (fun e -> "easing", JStr e) ]
        | InvariantKind.ChartSeriesDistinct -> []
        | InvariantKind.Unknown(_, raw) -> raw |> List.map Some

    JObj(kind :: members @ [ weight ] |> List.choose id)

// ─── Top-level ──────────────────────────────────────────────────

/// Build the Core `JVal` for a manifest (exposed for tests/tooling), members in
/// authored order; `encode` writes it under `Rendering.CrossHost`, which sorts
/// every object's keys.
let toJson (m: ThemeManifest) : JVal =
    let block key items =
        if List.isEmpty items then None else Some(key, JArr items)

    JObj(
        [ metaJson m.Meta |> Option.map (fun j -> "meta", j)
          Some("tokens", tokensJson m.Tokens)
          block "roles" (m.Roles |> List.map roleBindingJson)
          block "invariants" (m.Invariants |> List.map invariantJson) ]
        |> List.choose id
    )

/// Encode a manifest to canonical JSON — the bytes every manifest host writes
/// for the same manifest. `decode (encode m)` returns `m` for a manifest whose
/// tokens are in canonical path order (every decoded canonical document is
/// one); for any other, `encode` is a fixpoint through decode:
/// `encode (decode (encode m)) = encode m`.
///
/// The bytes are Core's cross-host rendering (Phase 2310): Ordinal-sorted keys at
/// every depth, every control character as `\u00xx`, and the canonical float
/// layout, the renderer the other manifest hosts write through.
let encode (m: ThemeManifest) : string =
    Canonical.write Rendering.CrossHost (toJson m)
