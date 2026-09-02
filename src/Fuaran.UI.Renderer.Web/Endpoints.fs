namespace Fuaran.UI.Renderer.Web

// ============================================================================
//  `MapFuaranRenderer` — the one call a host makes to serve the embedded
//  browser renderer, the reference stylesheet, and the fingerprint.
//
//  Endpoint routing rather than middleware. A middleware would sit in front of
//  every request in the pipeline to answer three URLs; an endpoint is matched by
//  the router that already exists, is visible to `dotnet run --list-endpoints`
//  and to a host's own endpoint inspection, and can carry per-route metadata
//  (auth, CORS, output caching) a consumer wants to set. There is nothing here
//  a middleware would do better.
//
//  CACHING, and why the fingerprint is exempt. The bundle and the stylesheet are
//  `immutable` for a year with a strong content ETag: they change only when the
//  package version changes, and the ETag means a redeploy of identical bytes
//  does not invalidate anyone's cache. The FINGERPRINT is `no-cache` — it is the
//  drift oracle, and an oracle a proxy may answer from last year answers a
//  question about last year.
// ============================================================================

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open System.Runtime.CompilerServices

[<AutoOpen>]
module private Serving =

    /// One year, the conventional maximum for an immutable asset.
    [<Literal>]
    let immutableMaxAge = 31536000

    let private cacheHeader (asset: Assets.Asset) =
        if asset.Immutable then
            sprintf "public, max-age=%d, immutable" immutableMaxAge
        else
            // `no-cache` (revalidate every time), NOT `no-store`: the ETag still
            // makes an unchanged fingerprint a cheap 304, and a deployment that
            // has not moved should not pay for a body it already has.
            "no-cache"

    /// Serve one embedded asset, honouring a conditional request.
    let serve (asset: Assets.Asset) (ctx: HttpContext) : Threading.Tasks.Task =
        let bytes = Assets.read asset
        let tag = Assets.etag bytes
        let headers = ctx.Response.Headers
        headers.CacheControl <- Microsoft.Extensions.Primitives.StringValues(cacheHeader asset)
        headers.ETag <- Microsoft.Extensions.Primitives.StringValues tag

        // A JS/CSS/JSON body a browser is allowed to sniff into something else
        // is a well-known injection surface, and these bodies are attacker-
        // influenced only in the sense that a host could serve a tampered
        // package — but the header costs nothing and its absence is what a
        // security review asks about.
        headers["X-Content-Type-Options"] <- Microsoft.Extensions.Primitives.StringValues "nosniff"

        let ifNoneMatch = ctx.Request.Headers.IfNoneMatch.ToString()

        if not (String.IsNullOrEmpty ifNoneMatch) && ifNoneMatch.Contains tag then
            ctx.Response.StatusCode <- StatusCodes.Status304NotModified
            Threading.Tasks.Task.CompletedTask
        else
            ctx.Response.ContentType <- asset.ContentType
            ctx.Response.ContentLength <- Nullable(int64 bytes.Length)
            ctx.Response.Body.WriteAsync(ReadOnlyMemory bytes).AsTask()

[<Extension>]
type FuaranRendererEndpointExtensions =

    /// Map the embedded renderer assets under `prefix` (no trailing slash).
    ///
    /// Serves three routes:
    ///
    ///   `GET {prefix}/fuaran-renderer.js`      the standalone browser renderer
    ///   `GET {prefix}/fuaran-reference.css`    the canonical reference stylesheet
    ///   `GET {prefix}/fingerprint.json`        what the embedded bundle IS
    ///
    /// The fingerprint route is not optional and has no switch. A deployment
    /// that can be asked what it is serving can be diagnosed from outside; one
    /// that cannot leaves a version question answerable only by whoever can
    /// reach the build server. It publishes package and version identifiers and
    /// a content digest — no host configuration, no request data, nothing about
    /// the trees the app renders.
    ///
    /// Returns the endpoint convention builder for the group, so a host can
    /// apply its own metadata (`.RequireAuthorization()`, a CORS policy, an
    /// output-cache policy) to all three at once.
    [<Extension>]
    static member MapFuaranRenderer(endpoints: IEndpointRouteBuilder, prefix: string) : IEndpointConventionBuilder =
        let trimmed =
            if prefix.EndsWith("/", StringComparison.Ordinal) then
                prefix.TrimEnd '/'
            else
                prefix

        let group = endpoints.MapGroup trimmed

        for asset in Assets.all do
            group.MapGet("/" + asset.Path, Func<HttpContext, Threading.Tasks.Task>(serve asset))
            |> ignore

        group :> IEndpointConventionBuilder

    /// `MapFuaranRenderer` at the conventional `/_fuaran` prefix.
    ///
    /// The underscore is deliberate: it marks the path as infrastructure rather
    /// than application, and it keeps the mount clear of a route an app would
    /// plausibly want (`/fuaran` is a name someone's app might legitimately use;
    /// `/_fuaran` is not).
    [<Extension>]
    static member MapFuaranRenderer(endpoints: IEndpointRouteBuilder) : IEndpointConventionBuilder =
        FuaranRendererEndpointExtensions.MapFuaranRenderer(endpoints, "/_fuaran")
