namespace Fuaran.UI.StyleObserver

open System.Text
open Fuaran.UI.ThemeManifest

// ─── Manifest-aware flag derivation (Phase 146) ─────────────────
//
// The render-time enforcement of a declared aesthetic-semantic budget,
// composing Phase 144's resolved fills + Phase 145's `ThemeManifest`
// (+ `LayoutObserver` areas for the budget). Deterministic — no VLM in
// the verify path, so it is CI-gateable, reproducible, cheap. Lives in
// the concrete package (not Abstractions) so the manifest-free
// Abstractions stays `FSharp.Core`-only; this module carries the
// `ThemeManifest` dependency.
//
// Two surfaces:
//   - `perNodeFlags` — per-NodeId fidelity checks (token resolution,
//     palette membership, declared contrast floor). Appended to each
//     observation by an observer that has a manifest wired.
//   - `verifyUsageBudgets` — the tree-level area-weighted colour-budget
//     check (the 60-30-10 enforcement). Needs both observers: the
//     caller joins `StyleObservation` fills with `LayoutObservation`
//     areas per NodeId and passes `(observation, areaPx²)` pairs.
//
// **Custom-subtree policy: EXEMPT.** `OffPaletteColour` (and every
// per-node manifest check) fires only for *toned* nodes — those that
// carry an `EmittedTone`. Custom / domain-SVG content never carries a
// Fuaran `data-fuaran-tone`, so it is exempt by construction; the
// palette check cannot spuriously fire on a chart's brand-coloured
// series or a logo's gradients.

module ManifestFlags =

    let private ri (v: float) : int = int (System.Math.Round v)

    let private rgbString (c: Rgba) : string =
        sprintf "rgb(%d, %d, %d)" (ri c.R) (ri c.G) (ri c.B)

    /// Canonical token-path order — the order palette ATTRIBUTION iterates in
    /// (Phase 1727; the rule is stated once, in `docs/THEME-BRIDGE-GUIDE.md`
    /// under "Palette attribution order", and pinned by the corpus's
    /// `style-observer/budget-same-valued-tokens-*` vectors). Paths compare
    /// segment by segment, a shorter prefix first, each segment by Unicode
    /// code point: the order a depth-first walk of the DTCG tree yields when it
    /// visits every group's members in ascending key order, which is what the
    /// Go and Rust decoders emit directly. This host's decoder preserves
    /// DOCUMENT order — a projection consumer may rely on that — so the
    /// ordering lives here, at the one site where order is a contract.
    let private comparePaths (a: string) (b: string) : int =
        let compareSegment (x: string) (y: string) =
            Seq.compareWith
                (fun (p: Rune) (q: Rune) -> compare p.Value q.Value)
                (x.EnumerateRunes())
                (y.EnumerateRunes())

        Seq.compareWith compareSegment (a.Split '.') (b.Split '.')

    /// The manifest's colour palette, parsed to `Rgba` + paired with the
    /// declaring token name, in canonical token-path order. Non-colour tokens
    /// + malformed hex are dropped. The first entry whose colour matches a
    /// rendered fill is the token the fill is ATTRIBUTED to, so two
    /// same-valued tokens attribute to the path-first one on every host.
    let private paletteRgba (manifest: ThemeManifest) : (Rgba * string) list =
        manifest.Tokens
        |> List.filter (fun t -> t.Type = "color")
        |> List.choose (fun t -> Rgba.tryParseHex t.Value |> Option.map (fun c -> c, t.Name))
        |> List.sortWith (fun (_, a) (_, b) -> comparePaths a b)

    /// Resolve an emitted slot (a tone name or a named role) to its
    /// declared token, if the manifest binds it.
    let private resolveSlot (manifest: ThemeManifest) (slot: string) : ManifestToken option =
        match ManifestRole.toneOfString slot with
        | Some tone -> ThemeManifest.resolveRole tone manifest
        | None -> ThemeManifest.resolveNamedRole slot manifest

    /// Per-node manifest-aware flags for one observation. Empty for
    /// untoned nodes (the Custom/SVG exemption). Order is deterministic:
    /// resolution, palette, contrast.
    let perNodeFlags (manifest: ThemeManifest) (obs: StyleObservation) : StyleFlag list =
        match obs.EmittedTone with
        | None -> []
        | Some slot ->
            let resolved = resolveSlot manifest slot
            let tokenFailed = Option.isNone resolved

            // TokenResolutionFailed — the emitted slot binds to no token.
            let tokenFlag =
                if tokenFailed then
                    [ StyleFlag.TokenResolutionFailed slot ]
                else
                    []

            // OffPaletteColour — the token resolved, but the rendered
            // surface isn't in the palette (host CSS produced an
            // off-palette colour). Suppressed when resolution already
            // failed (TokenResolutionFailed owns that node).
            let offPaletteFlag =
                if tokenFailed then
                    []
                else
                    let onPalette =
                        paletteRgba manifest
                        |> List.exists (fun (c, _) -> Rgba.sameRgb c obs.EffectiveBackground)

                    if onPalette then
                        []
                    else
                        [ StyleFlag.OffPaletteColour(rgbString obs.EffectiveBackground) ]

            // ContrastBelowDeclaredFloor — a per-role floor (matched to
            // the emitted slot name) stricter than the manifest-free AA
            // default the node already passed.
            let contrastFlags =
                manifest.Invariants
                |> List.choose (fun inv ->
                    match inv.Kind with
                    | InvariantKind.ContrastFloor(role, floor) when role = slot && obs.ContrastRatio < floor ->
                        Some(StyleFlag.ContrastBelowDeclaredFloor(role, obs.ContrastRatio, floor))
                    | _ -> None)

            tokenFlag @ offPaletteFlag @ contrastFlags

    /// Tree-level area-weighted usage-budget verification. `nodes` pairs
    /// each observation with its rendered area (px²) — the caller joins
    /// `StyleObservation` with `LayoutObservation.Width × Height` per
    /// NodeId. Each node's area is attributed to the manifest token its
    /// `EffectiveBackground` matches; per-token area share is compared to
    /// the `UsageBudget` target ± tolerance. Empty when no area is
    /// available (graceful degradation when the layout observer is
    /// absent). Deterministic — identical inputs → identical flags.
    let verifyUsageBudgets (manifest: ThemeManifest) (nodes: (StyleObservation * float) list) : StyleFlag list =
        let totalArea = nodes |> List.sumBy snd

        if totalArea <= 0.0 then
            []
        else
            let palette = paletteRgba manifest

            let areaByToken =
                nodes
                |> List.choose (fun (obs, area) ->
                    palette
                    |> List.tryPick (fun (c, name) ->
                        if Rgba.sameRgb c obs.EffectiveBackground then
                            Some(name, area)
                        else
                            None))
                |> List.groupBy fst
                |> List.map (fun (name, xs) -> name, xs |> List.sumBy snd)
                |> Map.ofList

            manifest.Invariants
            |> List.choose (fun inv ->
                match inv.Kind with
                | InvariantKind.UsageBudget(token, target, tol) ->
                    let tokenArea = Map.tryFind token areaByToken |> Option.defaultValue 0.0
                    let observedPct = 100.0 * tokenArea / totalArea

                    if abs (observedPct - target) > tol then
                        Some(StyleFlag.UsageBudgetExceeded(token, target, observedPct))
                    else
                        None
                | _ -> None)
