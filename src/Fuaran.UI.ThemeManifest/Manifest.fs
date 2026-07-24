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

    /// Every colour value declared in the palette — the membership set
    /// Phase 146's `OffPaletteColour` check tests resolved fills against.
    let paletteColours (manifest: ThemeManifest) : Set<string> =
        manifest.Tokens
        |> List.filter (fun t -> t.Type = "color")
        |> List.map _.Value
        |> Set.ofList
