namespace Fuaran.UI.ThemeManifest

open Fuaran.UI.Types

// ─── ThemeManifest — the declared token contract ────────────────
//
// A machine-readable theme contract the AI can *reason against* and
// the observer (Phase 146) can *verify against*. DTCG carries token
// values + a free-form `$description` and nothing else; this model is
// DTCG-compatible (so it is not NIH — a vanilla DTCG file decodes
// cleanly) **extended** with the two things DTCG lacks:
//
//   1. A per-token **role → `ToneVariant` mapping** (`RoleBinding`), so
//      `Tone.Brand` is known to resolve to the manifest's brand token.
//      The dual-field token shape (a quantified value + a semantic tag)
//      borrows SpecifyUI's SPEC representation (`arXiv:2509.07334`).
//   2. An **invariant block** (`Invariant list`) — contrast floors,
//      colour-usage budgets, motion voice — soft-weighted per Draco.
//
// **The manifest is a host/theme artefact, NOT part of the `Node`
// tree.** It travels *alongside* the tree, never inside it — preserving
// the "semantic, not CSS" tree posture (FGP 1). The package depends
// only on `Fuaran.UI` (for `ToneVariant`, itself `FSharp.Core`-only +
// Fable-portable) — no Browser, no renderer (FGP 2).

/// Manifest metadata — the `meta` block. `Name` / `Version` identify
/// the theme; `Description` is free-form prose.
type ManifestMeta =
    { Name: string
      Version: string
      Description: string option }

module ManifestMeta =
    /// A minimal metadata block — useful when decoding a vanilla DTCG
    /// file that carries no `meta`.
    let anonymous: ManifestMeta =
        { Name = ""
          Version = ""
          Description = None }

/// One token entry — DTCG-compatible (`Type`/`Value`/`Description`
/// round-trip the DTCG `$type`/`$value`/`$description`) plus the SPEC
/// dual-field `Role` semantic tag. `Name` is the token's dotted path
/// (`"color.brand.base"`), the flattened form of the DTCG group tree.
/// `Value` stays a string so any DTCG value kind (colour hex,
/// dimension, duration) round-trips losslessly.
type ManifestToken =
    { Name: string
      Type: string
      Value: string
      Description: string option
      Role: string option }

/// The role a `RoleBinding` binds. `Tone` covers the canonical
/// `ToneVariant` palette (FGP 1 — the role-mapping target); `Named`
/// covers the broader semantic-role vocabulary (page surface, body
/// text, divider…) that Phase 147 expands.
[<RequireQualifiedAccess>]
type ManifestRole =
    | Tone of ToneVariant
    | Named of string

module ManifestRole =
    /// Stable wire string for a `ToneVariant` — PascalCase-matched to
    /// the `Fuaran.UI` DU cases so the manifest's role keys read the
    /// same as the typed surface. Shared by encode + decode.
    let toneToString (tone: ToneVariant) : string =
        match tone with
        | ToneVariant.Default -> "Default"
        | ToneVariant.Subdued -> "Subdued"
        | ToneVariant.Brand -> "Brand"
        | ToneVariant.Success -> "Success"
        | ToneVariant.Warning -> "Warning"
        | ToneVariant.Critical -> "Critical"
        | ToneVariant.Info -> "Info"

    /// Parse a wire string back to a `ToneVariant`. `None` for an
    /// unrecognised token (the decoder treats it as a `Named` role).
    let toneOfString (s: string) : ToneVariant option =
        match s with
        | "Default" -> Some ToneVariant.Default
        | "Subdued" -> Some ToneVariant.Subdued
        | "Brand" -> Some ToneVariant.Brand
        | "Success" -> Some ToneVariant.Success
        | "Warning" -> Some ToneVariant.Warning
        | "Critical" -> Some ToneVariant.Critical
        | "Info" -> Some ToneVariant.Info
        | _ -> None

/// Binds a role onto a manifest token by name. The observer resolves
/// `Tone.Brand` to a token through this binding, then reads the token's
/// value — closing the "what colour did the AI's `Tone.Brand` actually
/// become?" question against a declared contract rather than a guess.
type RoleBinding =
    { Role: ManifestRole
      TokenName: string }

/// The declared theme contract: metadata + tokens + role bindings +
/// invariants. A vanilla DTCG file decodes to a manifest with populated
/// `Tokens` and empty `Roles` / `Invariants`.
type ThemeManifest =
    { Meta: ManifestMeta
      Tokens: ManifestToken list
      Roles: RoleBinding list
      Invariants: Invariant list }

module ThemeManifest =
    /// The empty manifest — no tokens, roles, or invariants.
    let empty: ThemeManifest =
        { Meta = ManifestMeta.anonymous
          Tokens = []
          Roles = []
          Invariants = [] }

    /// Look up a token by its dotted name.
    let tryGetToken (name: string) (manifest: ThemeManifest) : ManifestToken option =
        manifest.Tokens |> List.tryFind (fun t -> t.Name = name)

    /// Resolve a `ToneVariant` to its declared manifest token — the
    /// lookup Phase 146's observer consumes. Returns `None` when no role
    /// binding exists for the tone, or the bound token name is absent
    /// from `Tokens` (a dangling binding).
    let resolveRole (tone: ToneVariant) (manifest: ThemeManifest) : ManifestToken option =
        manifest.Roles
        |> List.tryPick (fun b ->
            match b.Role with
            | ManifestRole.Tone t when t = tone -> Some b.TokenName
            | _ -> None)
        |> Option.bind (fun name -> tryGetToken name manifest)

    /// Resolve a named (non-tone) role to its declared manifest token.
    /// The named-role counterpart of `resolveRole`; Phase 147 expands
    /// the named-role vocabulary the host can bind.
    let resolveNamedRole (role: string) (manifest: ThemeManifest) : ManifestToken option =
        manifest.Roles
        |> List.tryPick (fun b ->
            match b.Role with
            | ManifestRole.Named n when n = role -> Some b.TokenName
            | _ -> None)
        |> Option.bind (fun name -> tryGetToken name manifest)

    /// Phase 2482 — the token-name stem the chart series palette uses:
    /// `chart.series.1` … `chart.series.N`, one token per slot.
    [<Literal>]
    let ChartSeriesPrefix = "chart.series."

    /// The manifest's chart series tokens in SLOT order (by the numeric
    /// suffix, not by declaration order — the gates are measured over adjacent
    /// slots, so the order is part of the palette). A token whose suffix is not
    /// a positive integer is not a series slot.
    let chartSeries (manifest: ThemeManifest) : ManifestToken list =
        manifest.Tokens
        |> List.choose (fun t ->
            if t.Name.StartsWith ChartSeriesPrefix then
                let suffix = t.Name.Substring ChartSeriesPrefix.Length

                if suffix.Length > 0 && suffix |> Seq.forall System.Char.IsDigit then
                    let n = int suffix
                    if n > 0 then Some(n, t) else None
                else
                    None
            else
                None)
        |> List.sortBy fst
        |> List.map snd

    /// Phase 2482 — the chart token group a manifest declares, ready to set on
    /// a `Theme` (`{ theme with Charts = … }`): the series slots in slot order
    /// and the status triple from `chart.positive` / `chart.negative` /
    /// `chart.neutral`, each falling back to the shipped default when absent.
    /// `None` when the manifest declares no series slot at all — a theme file
    /// that says nothing about charts leaves the default in place. Values are
    /// carried as written; the palette gate decides whether they can be drawn.
    let chartTokens (manifest: ThemeManifest) : ChartTokens option =
        match chartSeries manifest with
        | [] -> None
        | series ->
            let status name (fallback: ColorVar) =
                tryGetToken ("chart." + name) manifest
                |> Option.map (fun t -> ColorVar.Hex t.Value)
                |> Option.defaultValue fallback

            Some
                { Series = series |> List.map (fun t -> ColorVar.Hex t.Value)
                  Positive = status "positive" Fuaran.UI.Defaults.charts.Positive
                  Negative = status "negative" Fuaran.UI.Defaults.charts.Negative
                  Neutral = status "neutral" Fuaran.UI.Defaults.charts.Neutral }

    /// Phase 2482 — the `ChartSeriesDistinct` invariant, evaluated: every
    /// palette finding against the manifest's chart tokens, each naming its
    /// slot or pair (`Fuaran.UI.ChartPalette.describe`). `[]` when the palette
    /// passes, or when the manifest declares no chart series at all. Evaluated
    /// whether or not the invariant is declared, because the install path
    /// refuses a failing palette either way.
    let verifyChartPalette (manifest: ThemeManifest) : Fuaran.UI.ChartPalette.PaletteFinding list =
        match chartTokens manifest with
        | Some tokens -> Fuaran.UI.ChartPalette.validate tokens
        | None -> []

    /// Every colour value declared in the palette — the membership set
    /// Phase 146's `OffPaletteColour` check tests resolved fills against.
    let paletteColours (manifest: ThemeManifest) : Set<string> =
        manifest.Tokens
        |> List.filter (fun t -> t.Type = "color")
        |> List.map _.Value
        |> Set.ofList
