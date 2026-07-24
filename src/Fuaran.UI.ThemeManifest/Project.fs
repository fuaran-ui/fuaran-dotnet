module Fuaran.UI.ThemeManifest.Project

// ─── ThemeManifest projectors — ingest existing token surfaces ──
//
// Lower the adoption floor for the manifest: instead of hand-authoring
// an Ella-MAe-grade `tokens.json`, project an app's *existing* token
// surface into a baseline `ThemeManifest` (tokens + the inferable role
// bindings) the operator then enriches with invariants. Even a thin
// projected manifest (tokens only, no invariants) drives Phase 144's
// contrast flags; budget enforcement arrives as the operator adds
// invariants (GP 13 — opt-in by manifest richness).
//
// Three source formats + a merge, covering the heterogeneous real
// portfolio:
//   - `projectFromFuaranToneVars` — the renderer's semantic
//     `--fuaran-tone-{tone}-{bg,fg,border}` contract (role inference is
//     direct: the contract is already semantic).
//   - `projectFromCssCustomProperties` — a generic `:root` (+ a
//     `[data-theme=…]` dark block) custom-property set; roles left
//     unbound (the operator maps bespoke vars), light/dark preserved.
//   - `projectFromDtcg` — a DTCG/`tokens.json` file (values lossless via
//     the Phase 145 decoder; grouping is NOT mined for bindings, per
//     DTCG's "don't infer purpose from grouping" caveat).
//   - `merge` — combine a shell token set + an app override with
//     last-write-wins precedence matching the CSS cascade.
//
// `FSharp.Core`-only: the CSS parser below is a small dependency-light
// declaration scanner (custom-property blocks are flat — no nesting),
// not a full CSS grammar.

open Fuaran.UI.Types
open Fuaran.UI.ThemeManifest

// ─── Lightweight CSS custom-property scanning ───────────────────

/// Strip `/* … */` comments (custom-property blocks rarely carry them,
/// but a copied stylesheet might).
let private stripComments (css: string) : string =
    let sb = System.Text.StringBuilder(css.Length)
    let mutable i = 0
    let n = css.Length

    while i < n do
        if i + 1 < n && css[i] = '/' && css[i + 1] = '*' then
            let close = css.IndexOf("*/", i + 2)
            i <- (if close < 0 then n else close + 2)
        else
            sb.Append css[i] |> ignore
            i <- i + 1

    sb.ToString()

/// One selector block — its selector text + the `--name → value`
/// custom-property declarations inside it.
type CssBlock =
    { Selector: string
      Declarations: (string * string) list }

/// Scan flat `selector { … }` blocks, keeping only custom-property
/// (`--`) declarations. Handles the `:root` / `:root[data-theme=…]`
/// shapes the projectors care about; nested at-rules are not expected
/// in a token block and are scanned naively (split on braces).
let scanCssBlocks (css: string) : CssBlock list =
    let cleaned = stripComments css

    cleaned.Split('}')
    |> Array.choose (fun chunk ->
        match chunk.Split([| '{' |], 2) with
        | [| selector; body |] ->
            let decls =
                body.Split(';')
                |> Array.choose (fun decl ->
                    match decl.Split([| ':' |], 2) with
                    | [| name; value |] when name.Trim().StartsWith("--") -> Some(name.Trim(), value.Trim())
                    | _ -> None)
                |> Array.toList

            if List.isEmpty decls then
                None
            else
                Some
                    { Selector = selector.Trim()
                      Declarations = decls }
        | _ -> None)
    |> Array.toList

// ─── Value-type inference (DTCG `$type`) ────────────────────────

let private inferType (value: string) : string =
    let v = value.Trim().ToLowerInvariant()

    if
        v.StartsWith("#")
        || v.StartsWith("rgb")
        || v.StartsWith("hsl")
        || v.StartsWith("oklch")
        || v.StartsWith("oklab")
        || v.StartsWith("color(")
    then
        "color"
    elif v.EndsWith("px") || v.EndsWith("rem") || v.EndsWith("em") || v.EndsWith("%") then
        "dimension"
    elif v.EndsWith("ms") || v.EndsWith("s") then
        "duration"
    else
        ""

let private token (name: string) (value: string) : ManifestToken =
    { Name = name
      Type = inferType value
      Value = value
      Description = None
      Role = None }

// ─── Fuaran-tone projector ──────────────────────────────────────

let private toneFromCss (s: string) : ToneVariant option =
    match s.ToLowerInvariant() with
    | "default" -> Some ToneVariant.Default
    | "subdued" -> Some ToneVariant.Subdued
    | "brand" -> Some ToneVariant.Brand
    | "success" -> Some ToneVariant.Success
    | "warning" -> Some ToneVariant.Warning
    | "critical" -> Some ToneVariant.Critical
    | "info" -> Some ToneVariant.Info
    | _ -> None

/// Project a `--fuaran-tone-{tone}-{slot}` custom-property set into a
/// manifest. Token names are `tone.{tone}.{slot}`; each tone whose
/// background (`bg`) slot is present gets a `ToneVariant` role binding to
/// that token (the tone's primary surface colour). Role inference is
/// direct — the `--fuaran-tone-*` contract is already semantic (Phase 12.H).
let projectFromFuaranToneVars (css: string) : ThemeManifest =
    let decls = scanCssBlocks css |> List.collect _.Declarations

    let tokens =
        decls
        |> List.choose (fun (name, value) ->
            // --fuaran-tone-{tone}-{slot}
            let stripped = name.TrimStart('-')

            if stripped.StartsWith("fuaran-tone-") then
                let rest = stripped.Substring("fuaran-tone-".Length)

                match rest.Split('-') with
                | [| tone; slot |] when (toneFromCss tone).IsSome ->
                    Some(tone.ToLowerInvariant(), slot.ToLowerInvariant(), value)
                | _ -> None
            else
                None)
        |> List.map (fun (tone, slot, value) -> token $"tone.{tone}.{slot}" value)
        // dedupe by name, last-write-wins
        |> List.fold (fun acc t -> (acc |> List.filter (fun (x: ManifestToken) -> x.Name <> t.Name)) @ [ t ]) []

    let roles =
        tokens
        |> List.choose (fun t ->
            // bind each tone's `bg` token to its ToneVariant
            match t.Name.Split('.') with
            | [| "tone"; toneStr; "bg" |] ->
                toneFromCss toneStr
                |> Option.map (fun tone ->
                    { Role = ManifestRole.Tone tone
                      TokenName = t.Name })
            | _ -> None)

    { ThemeManifest.empty with
        Tokens = tokens
        Roles = roles }

// ─── Generic CSS custom-property projector ──────────────────────

let private isDarkSelector (selector: string) : bool =
    let s = selector.ToLowerInvariant()

    s.Contains("data-theme=dark")
    || s.Contains("data-theme=\"dark\"")
    || s.Contains(".dark")

/// Project a generic `:root` custom-property block (plus an optional
/// dark-theme block) into manifest tokens. Light values keep the bare
/// var name (minus the leading `--`); a dark counterpart is emitted as
/// `{name}@dark` so both values round-trip without a model change. Roles
/// are left **unbound** — bespoke var names carry no inferable semantic
/// role, so the operator maps them.
let projectFromCssCustomProperties (css: string) : ThemeManifest =
    let blocks = scanCssBlocks css

    let lightTokens =
        blocks
        |> List.filter (fun b -> not (isDarkSelector b.Selector))
        |> List.collect _.Declarations
        |> List.map (fun (name, value) -> token (name.TrimStart('-')) value)

    let darkTokens =
        blocks
        |> List.filter (fun b -> isDarkSelector b.Selector)
        |> List.collect _.Declarations
        |> List.map (fun (name, value) -> token (name.TrimStart('-') + "@dark") value)

    let dedupe (ts: ManifestToken list) =
        ts
        |> List.fold (fun acc t -> (acc |> List.filter (fun (x: ManifestToken) -> x.Name <> t.Name)) @ [ t ]) []

    { ThemeManifest.empty with
        Tokens = dedupe (lightTokens @ darkTokens) }

// ─── DTCG projector ─────────────────────────────────────────────

/// Project a DTCG / `tokens.json` file into a manifest. Values + any
/// explicit `$extensions.fuaran.role` tags round-trip via the Phase 145
/// decoder; grouping is deliberately NOT mined for role bindings (DTCG's
/// "don't infer purpose from grouping" caveat) — `Roles` stays empty
/// unless the file declares fuaran role extensions, so nothing is
/// over-claimed. The operator confirms bindings afterward.
let projectFromDtcg (json: string) : Result<ThemeManifest, string> = Decode.decode json

// ─── Merge / normalise ──────────────────────────────────────────

/// Merge an override manifest onto a base with last-write-wins
/// precedence matching the CSS cascade — the shape produced by an app
/// that layers a rebound `--fuaran-tone-*` set over a shell `--color-*`
/// set. Tokens + role bindings from `over` replace same-keyed entries in
/// `baseM`; invariants are unioned (override-first, de-duplicated).
let merge (baseM: ThemeManifest) (over: ThemeManifest) : ThemeManifest =
    let overTokenNames = over.Tokens |> List.map _.Name |> Set.ofList

    let tokens =
        (baseM.Tokens |> List.filter (fun t -> not (Set.contains t.Name overTokenNames)))
        @ over.Tokens

    let overRoles = over.Roles |> List.map _.Role |> Set.ofList

    let roles =
        (baseM.Roles |> List.filter (fun r -> not (Set.contains r.Role overRoles)))
        @ over.Roles

    let invariants = (over.Invariants @ baseM.Invariants) |> List.distinct

    let meta =
        if over.Meta <> ManifestMeta.anonymous then
            over.Meta
        else
            baseM.Meta

    { Meta = meta
      Tokens = tokens
      Roles = roles
      Invariants = invariants }
