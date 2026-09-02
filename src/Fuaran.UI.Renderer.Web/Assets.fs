module Fuaran.UI.Renderer.Web.Assets

// ============================================================================
//  The embedded static web assets.
//
//  Three files travel INSIDE this assembly rather than beside it: the browser
//  renderer bundle, the reference stylesheet, and the fingerprint sidecar. A
//  consumer adds one PackageReference and gets all three — no npm install, no
//  wwwroot to populate, no build step. The precedent is well-worn (a Swagger UI
//  package embedding its own JS).
//
//  Embedded RESOURCES rather than static web assets, deliberately. The
//  staticwebassets pipeline would put the files on disk beside the app, which
//  means a publish that misses them fails at runtime with a 404 rather than at
//  build time — and it gives a host a copy it can edit, which is the drift the
//  fingerprint beside them exists to prevent. Embedded, the assets and the code
//  that serves them are one artefact and cannot be separated by a deployment.
// ============================================================================

open System.IO
open System.Reflection
open System.Security.Cryptography

/// One servable asset: the resource to read, the URL segment it answers on, and
/// the content type it is served as.
type Asset =
    {
        /// The path segment under the mount prefix, with no leading slash.
        Path: string
        /// The `Content-Type` header value, charset included.
        ContentType: string
        /// The embedded resource's manifest name.
        ResourceName: string
        /// Whether the asset may be cached immutably. FALSE for the fingerprint:
        /// it is the drift ORACLE, and an oracle a proxy may serve from last
        /// year answers a question about last year.
        Immutable: bool
    }

[<Literal>]
let private resourcePrefix = "Fuaran.UI.Renderer.Web.content."

/// The standalone browser renderer — React, the renderer and the canonical
/// decoder in one file, exposing the `FuaranRenderer` global.
let rendererScript =
    { Path = "fuaran-renderer.js"
      ContentType = "text/javascript; charset=utf-8"
      ResourceName = resourcePrefix + "fuaran-renderer.js"
      Immutable = true }

/// The canonical reference stylesheet — the same bytes `Fuaran.UI.Renderer`
/// ships, linked from that project at build time rather than copied, so this
/// package cannot be the fifth tier copy that drifts.
let referenceStylesheet =
    { Path = "fuaran-reference.css"
      ContentType = "text/css; charset=utf-8"
      ResourceName = resourcePrefix + "fuaran-reference.css"
      Immutable = true }

/// The fingerprint sidecar — what the embedded bundle is, served so a
/// deployment can be interrogated from outside rather than only from its logs.
let fingerprintDocument =
    { Path = "fingerprint.json"
      ContentType = "application/json; charset=utf-8"
      ResourceName = resourcePrefix + "fuaran-renderer.json"
      Immutable = false }

/// Every asset this package serves, in the order the endpoints are mapped.
let all = [ rendererScript; referenceStylesheet; fingerprintDocument ]

let private assembly = typeof<Asset>.Assembly

/// Read an embedded asset's bytes.
///
/// Throws rather than returning an option, and does so at the first read. An
/// asset missing from this assembly is a BUILD defect — the `.fsproj` names all
/// three — so there is no runtime recovery to offer, and failing at startup
/// with the resource name is strictly better than serving a 404 that reads like
/// a routing mistake.
let read (asset: Asset) : byte[] =
    use stream =
        match assembly.GetManifestResourceStream asset.ResourceName with
        | null ->
            failwithf
                "Fuaran.UI.Renderer.Web is missing the embedded asset '%s'. This is a packaging defect in the package itself, not a host misconfiguration — the assembly carries: %s"
                asset.ResourceName
                (System.String.Join(", ", assembly.GetManifestResourceNames()))
        | s -> s

    use buffer = new MemoryStream()
    stream.CopyTo buffer
    buffer.ToArray()

/// Read the fingerprint sidecar, parsed.
let fingerprint () : Result<Fingerprint.EmbeddedFingerprint, string> =
    read fingerprintDocument
    |> System.Text.Encoding.UTF8.GetString
    |> Fingerprint.parse

/// A strong ETag over an asset's bytes — the content itself, so a re-deploy of
/// identical assets does not invalidate a client's cache and a changed bundle
/// always does. Quoted per RFC 9110.
let etag (bytes: byte[]) : string =
    "\"" + System.Convert.ToHexString(SHA256.HashData bytes).Substring(0, 32) + "\""
