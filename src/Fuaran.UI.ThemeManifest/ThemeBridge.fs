module Fuaran.UI.ThemeManifest.ThemeBridge

// ─── ThemeManifest → host-styling bridge emitter (Phase 165) ────
//
// The return leg of the Phase 149 projectors. Where `Project.*` lowers
// an existing brand stylesheet *into* a `ThemeManifest`, this module
// emits the host-styling **bridge stylesheet** back *out* — the
// `--fuaran-tone-*` (+ spacing / typography) contract surface (Phase
// 12.H) re-bound to the host's own tokens. Combined with 149 (ingest)
// and the StyleObserver's invariant verification (Phase 146), a
// brownfield consumer gets a mechanical, verified adoption path with no
// hand-authored mapping file — the single most pervasive friction in
// adopting Fuaran chrome page-by-page inside an existing product.
//
// Three surfaces:
//   - `emitCss` — the bridge stylesheet + a coverage report.
//   - `verify` — the deterministic half of the Phase 146 composition:
//     resolve the manifest's declared contrast floors against the
//     *emitted* values (no browser needed). Usage-budget / motion
//     invariants need rendered area / a runtime, so they are reported
//     as observer-assisted rather than pass/fail here.
//   - `emitAndVerify` — the one-call composition.
//
// **`var()` references, not copied literals (the default).** Each bound
// contract variable is emitted as `--fuaran-tone-brand-fg:
// var(--brand-accent)` — a reference to the host's own token — so the
// host stylesheet stays the single source of truth and the host's
// dark-mode / theming variants flow through automatically. `Literal`
// mode (copy the resolved value) is available for snapshotting and is
// what the ingest→emit→re-project round-trip exercises.
//
// **Targets the 12.H contract, does not extend it.** The emitter only
// ever writes variables already in the host-styling contract; it never
// invents per-component variables (the §6 anti-pattern). `FSharp.Core`-
// only (FGP 2) — the WCAG contrast helpers below are a deliberate small
// re-implementation of the `Fuaran.UI.StyleObserver` formula, because
// that package depends on *this* one (no upward reference is possible).

open Fuaran.UI.Types
open Fuaran.UI.ThemeManifest

// ─── Options ────────────────────────────────────────────────────

/// The 12.H contract families the bridge can emit. A consumer scopes
/// output to the families they actually want to re-brand (a host that
/// only owns colour tokens emits `Tones` alone). `Motion` is carried
/// for completeness — 12.F motion is keyframe-class-based, so there is
/// no motion *variable* surface to bridge yet; selecting it emits
/// nothing and is a no-op until a future phase adds motion variables.
[<RequireQualifiedAccess>]
type ContractFamily =
    | Tones
    | Fonts
    | Spacing
    | Motion

/// How a bound contract variable's right-hand side is written.
[<RequireQualifiedAccess>]
type EmitMode =
    /// `--fuaran-tone-brand-fg: var(--brand-accent)` — a reference to the
    /// host's own token (the default; host stays canonical, dark-mode
    /// inherits).
    | Reference
    /// `--fuaran-tone-brand-fg: #1d4ed8` — the resolved value copied in
    /// (snapshot; exercised by the round-trip golden).
    | Literal

/// Emission options. `Scope` is the selector the `:root`-layer block is
/// written under (`":root"` or a container selector for a scoped page);
/// `Mode` picks reference-vs-literal; `Families` selects which 12.H
/// contract families to emit.
type BridgeOptions =
    { Scope: string
      Mode: EmitMode
      Families: Set<ContractFamily> }

module BridgeOptions =
    /// `:root`, reference mode, all families — the recommended default.
    let defaults: BridgeOptions =
        { Scope = ":root"
          Mode = EmitMode.Reference
          Families =
            Set.ofList
                [ ContractFamily.Tones
                  ContractFamily.Fonts
                  ContractFamily.Spacing
                  ContractFamily.Motion ] }

    /// Tones only — the common brownfield case (the host owns brand
    /// colours but not Fuaran's spacing / type scale).
    let tonesOnly: BridgeOptions =
        { defaults with
            Families = Set.singleton ContractFamily.Tones }

// ─── Coverage report ────────────────────────────────────────────

/// One contract variable that resolved to a host token and was emitted.
type MappedEntry =
    {
        /// The Fuaran contract variable, e.g. `--fuaran-tone-brand-fg`.
        FuaranVar: string
        /// The host token the variable bound to (a `ManifestToken.Name`).
        HostToken: string
        /// The emitted right-hand side (`var(--…)` or the literal value).
        Rhs: string
        Family: ContractFamily
    }

/// One contract variable that bound to no host token and therefore
/// falls back to the reference stylesheet's default (via the cascade —
/// no declaration is emitted for it). The default value is named so an
/// operator knows exactly what the un-bridged surface will render as.
type FallbackEntry =
    { FuaranVar: string
      ReferenceDefault: string
      Family: ContractFamily }

/// What the bridge did, in full: which contract variables resolved to a
/// host token, which fell back to a reference default, and which host
/// tokens were ingested but never bound to any contract variable.
type CoverageReport =
    { Mapped: MappedEntry list
      Fallbacks: FallbackEntry list
      UnusedHostTokens: string list }

/// The emitted bridge stylesheet + its coverage report.
type BridgeResult =
    { Css: string
      Coverage: CoverageReport }

// ─── Contract-variable catalogue (mirrors fuaran-reference.css) ──
//
// The 12.H idle contract surface + its reference-default values. This
// table is kept in sync with
// `Fuaran.UI.Renderer/content/fuaran-reference.css` `:root` by operator
// discipline (the same sync rule the TS byte-copy follows) — a change
// to a reference default there updates the matching entry here. Only the
// idle palette + scales are catalogued; the 12.N interaction-state
// matrix is out of v1 scope (a host re-brands idle tones; the state
// tokens default off the idle ones).

let private toneCss (t: ToneVariant) : string =
    (ManifestRole.toneToString t).ToLowerInvariant()

/// `--fuaran-tone-{tone}-{slot}` reference defaults (21 entries).
let private toneDefaults: (string * string) list =
    [ "tone-default-bg", "#ffffff"
      "tone-default-fg", "#1f2937"
      "tone-default-border", "#e5e7eb"
      "tone-subdued-bg", "#f3f4f6"
      "tone-subdued-fg", "#6b7280"
      "tone-subdued-border", "#d1d5db"
      "tone-brand-bg", "#eff6ff"
      "tone-brand-fg", "#1d4ed8"
      "tone-brand-border", "#93c5fd"
      "tone-success-bg", "#ecfdf5"
      "tone-success-fg", "#047857"
      "tone-success-border", "#6ee7b7"
      "tone-warning-bg", "#fffbeb"
      "tone-warning-fg", "#b45309"
      "tone-warning-border", "#fcd34d"
      "tone-critical-bg", "#fef2f2"
      "tone-critical-fg", "#b91c1c"
      "tone-critical-border", "#fca5a5"
      "tone-info-bg", "#eff6ff"
      "tone-info-fg", "#1e40af"
      "tone-info-border", "#93c5fd" ]

/// Typography scale reference defaults (the `Fonts` family — 14 entries).
let private fontDefaults: (string * string) list =
    [ "text-xs", "12px"
      "text-sm", "13px"
      "text-base", "14px"
      "text-lg", "16px"
      "text-xl", "20px"
      "text-2xl", "24px"
      "text-3xl", "28px"
      "font-weight-regular", "400"
      "font-weight-medium", "500"
      "font-weight-semibold", "600"
      "font-weight-bold", "700"
      "line-height-tight", "1.25"
      "line-height-normal", "1.5"
      "line-height-relaxed", "1.75" ]

/// Spacing + component-dimension reference defaults (the `Spacing`
/// family — 12 entries).
let private spacingDefaults: (string * string) list =
    [ "space-xs", "4px"
      "space-sm", "8px"
      "space-md", "12px"
      "space-lg", "16px"
      "space-xl", "24px"
      "radius-sm", "4px"
      "radius-md", "6px"
      "radius-lg", "8px"
      "radius-full", "9999px"
      "border-width", "1px"
      "button-pad-y", "8px"
      "button-pad-x", "12px" ]

/// Every catalogued contract variable, paired with its family + the
/// reference default. The `stem` is the variable name minus the
/// `--fuaran-` prefix (e.g. `tone-brand-fg`); the emitted variable is
/// `--fuaran-{stem}`. Order here is the emission order.
let private catalogue: (ContractFamily * string * string) list =
    [ for stem, dflt in toneDefaults -> ContractFamily.Tones, stem, dflt
      for stem, dflt in fontDefaults -> ContractFamily.Fonts, stem, dflt
      for stem, dflt in spacingDefaults -> ContractFamily.Spacing, stem, dflt ]

/// The named-role → contract-stem table. A `ManifestRole.Named` role
/// whose name (case-insensitively) matches one of these is bridged to
/// the corresponding contract variable. Bespoke names not in the table
/// are reported as unused rather than guessed (Phase 149's "don't
/// over-claim" posture).
let private namedRoleStems: Map<string, string> =
    Map.ofList
        [ "page-surface", "tone-default-bg"
          "surface", "tone-default-bg"
          "body-text", "tone-default-fg"
          "text", "tone-default-fg"
          "border", "tone-default-border"
          "divider", "tone-default-border"
          "muted", "tone-subdued-bg"
          "muted-surface", "tone-subdued-bg"
          "muted-text", "tone-subdued-fg" ]

// ─── Resolution (manifest → contract stem) ──────────────────────

/// Normalise a token name to dash-form so a structured token name
/// (`tone.brand.bg` or `tone-brand-bg`) matches a contract stem.
let private normaliseName (name: string) : string =
    name.TrimStart('-').Replace('.', '-').ToLowerInvariant()

/// Build the resolution maps once per emit/verify:
///   - `structured` — contract stem → token, from any token whose
///     normalised name *is* a contract stem (the `--fuaran-tone-*`
///     round-trip + conventionally-named spacing/type tokens).
///   - `byRole` — contract stem → token, from the role bindings: a
///     `Tone t` binding fills the tone's `fg` slot (its accent colour);
///     a recognised `Named` role fills its tabled stem.
/// Structured wins over role bindings (so a manifest that already
/// carries `tone.brand.fg` re-emits it verbatim rather than the
/// role-derived accent).
type private Resolver =
    {
        Structured: Map<string, ManifestToken>
        ByRole: Map<string, ManifestToken>
        /// Token names consumed by a binding/structured match, for the
        /// unused-token report.
        Used: Set<string>
    }

let private buildResolver (manifest: ThemeManifest) : Resolver =
    let stems = catalogue |> List.map (fun (_, stem, _) -> stem) |> Set.ofList

    let structured =
        manifest.Tokens
        |> List.choose (fun t ->
            let n = normaliseName t.Name

            if Set.contains n stems then Some(n, t) else None)
        // last-write-wins on stem collision, deterministic.
        |> List.fold (fun acc (n, t) -> Map.add n t acc) Map.empty

    // A binding whose bound token is *itself* a structured contract
    // token is already covered by the structured path in its own slot;
    // re-mapping it to a different slot would double-emit (and break the
    // ingest→emit→re-project round-trip). Role synthesis only fires for
    // bespoke host tokens (`--color-brand` and the like).
    let resolveBespokeToken (name: string) : ManifestToken option =
        manifest.Tokens
        |> List.tryFind (fun t -> t.Name = name)
        |> Option.filter (fun t -> not (Set.contains (normaliseName t.Name) stems))

    let byRole =
        manifest.Roles
        |> List.choose (fun b ->
            match b.Role with
            | ManifestRole.Tone t ->
                resolveBespokeToken b.TokenName
                |> Option.map (fun tok -> $"tone-{toneCss t}-fg", tok)
            | ManifestRole.Named n ->
                match Map.tryFind (n.ToLowerInvariant()) namedRoleStems with
                | Some stem -> resolveBespokeToken b.TokenName |> Option.map (fun tok -> stem, tok)
                | None -> None)
        |> List.fold (fun acc (stem, tok) -> Map.add stem tok acc) Map.empty

    let used =
        [ yield! structured |> Map.toList |> List.map (fun (_, t) -> t.Name)
          yield! byRole |> Map.toList |> List.map (fun (_, t) -> t.Name) ]
        |> Set.ofList

    { Structured = structured
      ByRole = byRole
      Used = used }

/// The host token a contract stem binds to (structured wins over role),
/// or `None` when the stem falls back to its reference default.
let private resolveToken (resolver: Resolver) (stem: string) : ManifestToken option =
    match Map.tryFind stem resolver.Structured with
    | Some t -> Some t
    | None -> Map.tryFind stem resolver.ByRole

// ─── Emission ───────────────────────────────────────────────────

let private referenceRhs (token: ManifestToken) : string = $"var(--{normaliseName token.Name})"

let private rhsFor (mode: EmitMode) (token: ManifestToken) : string =
    match mode with
    | EmitMode.Reference -> referenceRhs token
    | EmitMode.Literal -> token.Value

/// Emit the bridge stylesheet + coverage report for a manifest.
let emitCss (manifest: ThemeManifest) (options: BridgeOptions) : BridgeResult =
    let resolver = buildResolver manifest

    let entries =
        catalogue
        |> List.filter (fun (family, _, _) -> Set.contains family options.Families)
        |> List.map (fun (family, stem, dflt) ->
            let fuaranVar = $"--fuaran-{stem}"

            match resolveToken resolver stem with
            | Some token ->
                Choice1Of2
                    { FuaranVar = fuaranVar
                      HostToken = token.Name
                      Rhs = rhsFor options.Mode token
                      Family = family }
            | None ->
                Choice2Of2
                    { FuaranVar = fuaranVar
                      ReferenceDefault = dflt
                      Family = family })

    let mapped =
        entries
        |> List.choose (function
            | Choice1Of2 m -> Some m
            | _ -> None)

    let fallbacks =
        entries
        |> List.choose (function
            | Choice2Of2 f -> Some f
            | _ -> None)

    let unused =
        manifest.Tokens
        |> List.map _.Name
        |> List.filter (fun name -> not (Set.contains name resolver.Used))
        |> List.distinct

    let css =
        let lines =
            mapped |> List.map (fun m -> $"  {m.FuaranVar}: {m.Rhs};") |> String.concat "\n"

        let body =
            if List.isEmpty mapped then
                "  /* No contract variable resolved to a host token — see the coverage report. */"
            else
                lines

        [ "/* Fuaran host-styling bridge — generated from a ThemeManifest (Phase 165)."
          "   Load AFTER fuaran-reference.css so this :root block overrides the defaults."
          "   Unmapped contract variables inherit their reference defaults via the cascade. */"
          $"{options.Scope} {{"
          body
          "}" ]
        |> String.concat "\n"

    { Css = css
      Coverage =
        { Mapped = mapped
          Fallbacks = fallbacks
          UnusedHostTokens = unused } }

// ─── Coverage rendering ─────────────────────────────────────────

module CoverageReport =

    /// A compact, plain-text summary for a console / log line.
    let toConsole (report: CoverageReport) : string =
        let mappedLines =
            report.Mapped
            |> List.map (fun m -> $"  mapped    {m.FuaranVar} -> {m.HostToken}")

        let fallbackLines =
            report.Fallbacks
            |> List.map (fun f -> $"  fallback  {f.FuaranVar} (reference default {f.ReferenceDefault})")

        let unusedLines = report.UnusedHostTokens |> List.map (fun t -> $"  unused    {t}")

        [ $"Theme-bridge coverage: {List.length report.Mapped} mapped, {List.length report.Fallbacks} fell back to reference defaults, {List.length report.UnusedHostTokens} host tokens unused."
          yield! mappedLines
          yield! fallbackLines
          yield! unusedLines ]
        |> String.concat "\n"

    /// A Markdown report — three tables, CI-artefact friendly.
    let toMarkdown (report: CoverageReport) : string =
        let mappedTable =
            [ "### Mapped contract variables"
              ""
              "| Contract variable | Host token | Right-hand side |"
              "| --- | --- | --- |"
              yield!
                  report.Mapped
                  |> List.map (fun m -> $"| `{m.FuaranVar}` | `{m.HostToken}` | `{m.Rhs}` |") ]

        let fallbackTable =
            [ "### Fell back to reference defaults"
              ""
              "| Contract variable | Reference default |"
              "| --- | --- |"
              yield!
                  report.Fallbacks
                  |> List.map (fun f -> $"| `{f.FuaranVar}` | `{f.ReferenceDefault}` |") ]

        let unusedTable =
            [ "### Unused host tokens"
              ""
              "| Host token |"
              "| --- |"
              yield! report.UnusedHostTokens |> List.map (fun t -> $"| `{t}` |") ]

        [ "## Theme-bridge coverage"
          ""
          $"- **{List.length report.Mapped}** contract variables mapped to host tokens"
          $"- **{List.length report.Fallbacks}** fell back to reference defaults"
          $"- **{List.length report.UnusedHostTokens}** host tokens ingested but unbound"
          ""
          yield! mappedTable
          ""
          yield! fallbackTable
          ""
          yield! unusedTable ]
        |> String.concat "\n"

// ─── WCAG contrast (deliberate small re-implementation) ─────────
//
// `Fuaran.UI.StyleObserver` carries the canonical compositing + WCAG
// formula, but it depends on this package, so we cannot reference it
// upward. These three helpers are a faithful copy of that formula
// (`Color.tryParseHex` + `Flags.relativeLuminance` + `Flags.contrastRatio`)
// operating on the manifest's opaque hex token values. Resolved manifest
// colours are opaque, so no alpha compositing is needed here.

let private tryParseHex (raw: string) : (float * float * float) option =
    if isNull (box raw) then
        None
    else
        let s = raw.Trim().TrimStart('#')

        let hex2 (i: int) =
            match
                System.Int32.TryParse(
                    s.Substring(i, 2),
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture
                )
            with
            | true, v -> Some(float v)
            | _ -> None

        let hex1 (i: int) =
            match
                System.Int32.TryParse(
                    string s[i],
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture
                )
            with
            | true, v -> Some(float (v * 16 + v))
            | _ -> None

        match s.Length with
        | 3 ->
            match hex1 0, hex1 1, hex1 2 with
            | Some r, Some g, Some b -> Some(r, g, b)
            | _ -> None
        | 6
        | 8 ->
            match hex2 0, hex2 2, hex2 4 with
            | Some r, Some g, Some b -> Some(r, g, b)
            | _ -> None
        | _ -> None

let private relativeLuminance (r: float, g: float, b: float) : float =
    let channel (v: float) =
        let s = v / 255.0

        if s <= 0.03928 then
            s / 12.92
        else
            ((s + 0.055) / 1.055) ** 2.4

    0.2126 * channel r + 0.7152 * channel g + 0.0722 * channel b

let private contrastRatio (fg: float * float * float) (bg: float * float * float) : float =
    let lf = relativeLuminance fg
    let lb = relativeLuminance bg
    let lighter = max lf lb
    let darker = min lf lb
    (lighter + 0.05) / (darker + 0.05)

// ─── Verification (the deterministic Phase 146 half) ────────────

/// The outcome of checking one declared invariant against the emitted
/// values.
[<RequireQualifiedAccess>]
type CheckStatus =
    /// The invariant resolved to concrete values and holds.
    | Passed
    /// The invariant resolved to concrete values and is breached.
    | Violated
    /// The invariant could not be resolved to concrete values from the
    /// manifest alone (an unbound role, an unparseable colour) — no
    /// verdict either way.
    | Indeterminate of reason: string

/// One verified contrast floor. `Foreground` / `Background` are the
/// resolved values the ratio was computed from (named so a failure is
/// self-explaining); `Ratio` is `None` when indeterminate.
type ContrastCheck =
    { Role: string
      Floor: float
      Foreground: string option
      Background: string option
      Ratio: float option
      Status: CheckStatus }

/// An invariant that cannot be settled deterministically from the
/// manifest — it needs the StyleObserver (Phase 146) with rendered area
/// (usage budgets) or a runtime (motion). Carried so the verification
/// result is exhaustive over the manifest's invariants.
type DeferredCheck = { Description: string; Reason: string }

/// The result of running the manifest's declared invariants against the
/// emitted bridge values. `Violations` is the breached subset of
/// `ContrastChecks`, surfaced for a quick `isEmpty` gate.
type VerificationResult =
    { ContrastChecks: ContrastCheck list
      Deferred: DeferredCheck list }

module VerificationResult =
    /// The breached contrast checks — a non-empty list is a build-time
    /// failure.
    let violations (result: VerificationResult) : ContrastCheck list =
        result.ContrastChecks |> List.filter (fun c -> c.Status = CheckStatus.Violated)

    /// `true` when every resolvable invariant holds (indeterminate +
    /// deferred checks do not fail the gate).
    let passed (result: VerificationResult) : bool = violations result |> List.isEmpty

/// Resolve the value a contract stem renders as: the bound host token's
/// value if mapped, else the reference default.
let private resolvedValueOf (resolver: Resolver) (stem: string) : string option =
    match resolveToken resolver stem with
    | Some t -> Some t.Value
    | None ->
        catalogue
        |> List.tryPick (fun (_, s, dflt) -> if s = stem then Some dflt else None)

/// Resolve the (foreground, background) pair a contrast floor is
/// measured over. A tone role measures its `fg` slot over its `bg`
/// slot; a named role measures its bound value over the page surface
/// (`page-surface` / `surface` / the Default tone's background, else
/// white).
let private contrastPairFor
    (manifest: ThemeManifest)
    (resolver: Resolver)
    (role: string)
    : (string option * string option) =
    match ManifestRole.toneOfString role with
    | Some tone ->
        let tc = toneCss tone
        resolvedValueOf resolver $"tone-{tc}-fg", resolvedValueOf resolver $"tone-{tc}-bg"
    | None ->
        let fg = ThemeManifest.resolveNamedRole role manifest |> Option.map _.Value

        let bg =
            [ ThemeManifest.resolveNamedRole "page-surface" manifest
              ThemeManifest.resolveNamedRole "surface" manifest ]
            |> List.tryPick id
            |> Option.map _.Value
            |> Option.orElse (resolvedValueOf resolver "tone-default-bg")

        fg, bg

/// Run the manifest's declared invariants against the emitted values.
/// Deterministic: contrast floors are resolved from the manifest's
/// resolved token values (no browser). Usage-budget + motion invariants
/// are returned as `Deferred` — they need the StyleObserver's
/// area-weighted pass / a runtime (observer-assisted).
let verify (manifest: ThemeManifest) : VerificationResult =
    let resolver = buildResolver manifest

    let contrastChecks =
        manifest.Invariants
        |> List.choose (fun inv ->
            match inv.Kind with
            | InvariantKind.ContrastFloor(role, floor) ->
                let fg, bg = contrastPairFor manifest resolver role

                let check =
                    match fg, bg with
                    | Some fgVal, Some bgVal ->
                        match tryParseHex fgVal, tryParseHex bgVal with
                        | Some fgc, Some bgc ->
                            let ratio = contrastRatio fgc bgc

                            { Role = role
                              Floor = floor
                              Foreground = Some fgVal
                              Background = Some bgVal
                              Ratio = Some ratio
                              Status =
                                (if ratio >= floor then
                                     CheckStatus.Passed
                                 else
                                     CheckStatus.Violated) }
                        | _ ->
                            { Role = role
                              Floor = floor
                              Foreground = Some fgVal
                              Background = Some bgVal
                              Ratio = None
                              Status =
                                CheckStatus.Indeterminate "foreground or background is not a parseable hex colour" }
                    | _ ->
                        { Role = role
                          Floor = floor
                          Foreground = fg
                          Background = bg
                          Ratio = None
                          Status =
                            CheckStatus.Indeterminate "role does not resolve to both a foreground and a background" }

                Some check
            | _ -> None)

    let deferred =
        manifest.Invariants
        |> List.choose (fun inv ->
            match inv.Kind with
            | InvariantKind.UsageBudget(token, target, tol) ->
                Some
                    { Description = $"UsageBudget {token} {target}±{tol}%%"
                      Reason = "needs rendered area — verify via the StyleObserver (Phase 146) area-weighted pass" }
            | InvariantKind.MotionVoice mb ->
                Some
                    { Description = $"MotionVoice maxDurationMs={mb.MaxDurationMs}"
                      Reason = "needs a runtime — motion is keyframe-class-based, not a bridged variable" }
            | InvariantKind.ContrastFloor _ -> None)

    { ContrastChecks = contrastChecks
      Deferred = deferred }

// ─── One-call composition ───────────────────────────────────────

/// Emit the bridge AND verify the manifest's invariants against the
/// emitted values in one call — the mechanical "project → emit → verify"
/// close-out. The bridge's mapped values and the verification's resolved
/// values come from the same resolver, so a contrast violation names the
/// exact role + values the emitted bridge would render.
let emitAndVerify (manifest: ThemeManifest) (options: BridgeOptions) : BridgeResult * VerificationResult =
    emitCss manifest options, verify manifest
