module Fuaran.UI.Renderer.Web.Fingerprint

// ============================================================================
//  The embedded bundle's fingerprint, and the drift question it answers.
//
//  This package ships a BUILT ARTEFACT from another repository — the
//  `@fuaran-ui/renderer` standalone browser bundle, byte-copied by
//  `scripts/sync-renderer-web.ps1`. A byte copy across a repo boundary can go
//  stale silently, and the failure it produces is the worst kind: the page
//  loads, the tree renders, and one vocabulary the authoring side emits is not
//  one the embedded renderer knows, so a node degrades instead of erroring.
//
//  So the copy carries a sidecar recording WHAT was copied and what it agreed
//  with. Three things read it:
//
//   * `scripts/sync-renderer-web.ps1` writes it, from the sources it copied.
//   * The `Check` gate (`Build.fsproj -- RendererWebCheck`) fails when the
//     sidecar disagrees with the authoring side in this checkout.
//   * The serving endpoint publishes it, and `Snippet.mount` warns at
//     DEVELOPMENT time when it disagrees with the authoring constants compiled
//     into this very assembly.
//
//  The third is not redundant with the second. The gate runs where the repo is;
//  the runtime check runs where the DEPLOYMENT is, against whatever versions of
//  `Fuaran.UI.Renderer.Web` and the authoring packages a consumer actually
//  restored — a pair no gate here ever sees.
// ============================================================================

open System
open System.Text

/// The wire profile this package's authoring surface targets — the major
/// version of the canonical wire format, as a bare string because that is what
/// the embedded bundle stamps.
///
/// PINNED here rather than derived: a profile is a compatibility CLAIM, and a
/// claim computed from whatever happens to be present cannot be wrong, which
/// makes it worthless. It moves when the wire format's major version does, in
/// the same change-set.
[<Literal>]
let AuthoringWireProfile = "1"

/// What the sync recorded about the embedded bundle.
///
/// `VocabularyFingerprint` is the reference stylesheet's class-vocabulary stamp
/// (`Fuaran.UI.Renderer.Theme.vocabularyFingerprint`) as it stood when the
/// assets were synced. It is the axis that actually moves: a renderer emitting
/// a class the shipped stylesheet does not style is the drift a consumer sees
/// as "it renders, but wrong".
type EmbeddedFingerprint =
    {
        /// The npm package the bundle was built from.
        RendererPackage: string
        /// That package's version at sync time.
        RendererVersion: string
        /// The bundle's own `BUNDLE_VERSION` stamp.
        BundleVersion: string
        /// The bundle's own `WIRE_PROFILE` stamp.
        WireProfile: string
        /// The canonical stylesheet's vocabulary fingerprint at sync time.
        VocabularyFingerprint: string
        /// SHA-256 of the embedded bundle bytes, uppercase hex.
        BundleSha256: string
    }

/// One way the embedded assets and the authoring surface disagree. Reported as
/// a list, because two disagreements are two facts and collapsing them to a
/// boolean is how a consumer ends up repairing the wrong one.
type Mismatch =
    /// The bundle decodes a different wire profile than this package authors.
    | WireProfileMismatch of embedded: string * authoring: string
    /// The bundle was synced against a stylesheet vocabulary that this build's
    /// renderer no longer emits.
    | VocabularyMismatch of embedded: string * authoring: string

/// Render a mismatch as the sentence a developer needs — what disagrees, why it
/// matters, and the one command that repairs it.
let describe (m: Mismatch) : string =
    match m with
    | WireProfileMismatch(embedded, authoring) ->
        sprintf
            "the embedded browser renderer decodes wire profile '%s' but this authoring package emits profile '%s' — a tree it cannot decode renders as nothing at all. Re-sync the embedded assets (scripts/sync-renderer-web.ps1) against a renderer built for profile '%s'."
            embedded
            authoring
            authoring
    | VocabularyMismatch(embedded, authoring) ->
        sprintf
            "the embedded assets were synced against class vocabulary '%s' but this build's renderer emits '%s' — the page will render, and some nodes will be styled by rules the shipped stylesheet does not carry, which looks like a design bug rather than a version one. Run `dotnet run --project Build.fsproj -- RendererWeb` to re-sync."
            embedded
            authoring

/// Compare an embedded fingerprint against this build's authoring surface.
/// `[]` means they agree.
///
/// Pure, and takes the authoring vocabulary as an ARGUMENT rather than reading
/// `Theme.vocabularyFingerprint` itself: the whole point is to compare two
/// independently-moving values, and a function that sourced one of them would
/// be comparing a constant with itself in exactly the case a test needs to
/// exercise.
let check (authoringVocabulary: string) (fp: EmbeddedFingerprint) : Mismatch list =
    [ if fp.WireProfile <> AuthoringWireProfile then
          WireProfileMismatch(fp.WireProfile, AuthoringWireProfile)
      if fp.VocabularyFingerprint <> authoringVocabulary then
          VocabularyMismatch(fp.VocabularyFingerprint, authoringVocabulary) ]

// ─── The sidecar's wire form ──────────────────────────────────────────────
//
// A flat object of six string fields, written and read here. Hand-rolled rather
// than `System.Text.Json`, for the reason the canonical encoder is: the parse
// target is a file this package GENERATED, six keys wide, and the dependency
// set stays at the shared framework.

let private jsonEscape (s: string) : string =
    let sb = StringBuilder()

    for ch in s do
        match ch with
        | '"' -> sb.Append "\\\"" |> ignore
        | '\\' -> sb.Append "\\\\" |> ignore
        | c when c < ' ' -> sb.Append(sprintf "\\u%04x" (int c)) |> ignore
        | c -> sb.Append c |> ignore

    sb.ToString()

/// The sidecar's canonical text — keys in a fixed order, so a sync that changed
/// nothing rewrites identical bytes and a diff shows only what moved.
let toJson (fp: EmbeddedFingerprint) : string =
    let field (k: string) (v: string) =
        sprintf "  \"%s\": \"%s\"" (jsonEscape k) (jsonEscape v)

    let body =
        String.Join(
            ",\n",
            [ field "rendererPackage" fp.RendererPackage
              field "rendererVersion" fp.RendererVersion
              field "bundleVersion" fp.BundleVersion
              field "wireProfile" fp.WireProfile
              field "vocabularyFingerprint" fp.VocabularyFingerprint
              field "bundleSha256" fp.BundleSha256 ]
        )

    "{\n" + body + "\n}\n"

/// Read one `"key": "value"` pair out of the sidecar. Deliberately narrow — it
/// reads a file this package wrote, in the shape above, and anything else is a
/// tampered or hand-edited sidecar that should fail rather than half-parse.
let private readField (json: string) (key: string) : string option =
    let marker = "\"" + key + "\""
    let ki = json.IndexOf(marker, StringComparison.Ordinal)

    if ki < 0 then
        None
    else
        let ci = json.IndexOf(':', ki + marker.Length)

        if ci < 0 then
            None
        else
            let oq = json.IndexOf('"', ci + 1)

            if oq < 0 then
                None
            else
                let sb = StringBuilder()
                let mutable i = oq + 1
                let mutable closed = false

                while not closed && i < json.Length do
                    match json[i] with
                    | '"' -> closed <- true
                    | '\\' when i + 1 < json.Length ->
                        (match json[i + 1] with
                         | 'n' -> sb.Append '\n' |> ignore
                         | 't' -> sb.Append '\t' |> ignore
                         | c -> sb.Append c |> ignore)

                        i <- i + 1
                    | c -> sb.Append c |> ignore

                    i <- i + 1

                if closed then Some(sb.ToString()) else None

/// Parse a sidecar. `Error` names the FIRST missing key rather than failing
/// generically: a sidecar is generated, so a missing key means the generator
/// and this reader have drifted, and which key is the whole diagnosis.
let parse (json: string) : Result<EmbeddedFingerprint, string> =
    let missing =
        [ "rendererPackage"
          "rendererVersion"
          "bundleVersion"
          "wireProfile"
          "vocabularyFingerprint"
          "bundleSha256" ]
        |> List.tryFind (fun k -> (readField json k).IsNone)

    match missing with
    | Some k ->
        Error(
            sprintf
                "the embedded renderer fingerprint is missing the '%s' field — it is generated by scripts/sync-renderer-web.ps1, so this means the generator and the reader have drifted"
                k
        )
    | None ->
        let get k = (readField json k).Value

        Ok
            { RendererPackage = get "rendererPackage"
              RendererVersion = get "rendererVersion"
              BundleVersion = get "bundleVersion"
              WireProfile = get "wireProfile"
              VocabularyFingerprint = get "vocabularyFingerprint"
              BundleSha256 = get "bundleSha256" }
