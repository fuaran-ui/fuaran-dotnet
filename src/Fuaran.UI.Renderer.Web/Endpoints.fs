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
//  CACHING (Phase 1929). `immutable` is earned by the URL, not by the asset.
//  Every immutable asset answers on TWO paths: the content-hashed one
//  (`fuaran-renderer.<hash>.js`, what `Snippet` emits), cached `immutable` for
//  a year, because a change to the bytes is a change to the URL; and the plain
//  one (`fuaran-renderer.js`, for consumers who wrote it by hand), served
//  `no-cache` with the strong ETag, so a browser revalidates on every load and
//  an unchanged asset costs a 304. The plain path used to carry `immutable`
//  too, on the premise "it changes only when the package version changes" —
//  exactly the case in which its URL does not change, so after an upgrade a
//  returning browser ran the old renderer for up to a year without asking.
//
//  A hashed path whose hash is not the embedded one — a page rendered by the
//  previous build, met mid-rollout or from a cached document — is served the
//  current bytes as the plain path is, `no-cache`, rather than a 404 that would
//  leave that page with no renderer at all. It is never served `immutable`: the
//  bytes do not match the name.
//
//  The FINGERPRINT has only the plain path and is `no-cache` — it is the drift
//  oracle, and an oracle a proxy may answer from last year answers a question
//  about last year.
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

    /// For a content-hashed path whose hash is the embedded one: the URL changes
    /// whenever the bytes do, so nothing here can go stale.
    let private immutableCache = sprintf "public, max-age=%d, immutable" immutableMaxAge

    /// For every path whose name does not pin its bytes. `no-cache` (revalidate
    /// every time), NOT `no-store`: the ETag still makes an unchanged asset a
    /// cheap 304, and a deployment that has not moved should not pay for a body
    /// it already has.
    [<Literal>]
    let private revalidateCache = "no-cache"

    /// The route parameter a hashed path carries its hash in.
    [<Literal>]
    let hashParameter = "hash"

    /// Serve one embedded asset under `cacheControl`, honouring a conditional
    /// request.
    ///
    /// The bytes and the ETag come from `Assets.content`, which reads them out
    /// of the assembly and hashes them once per process. Both used to be
    /// recomputed per request — including on the 304 path, which then sent
    /// neither.
    let private serveAs (cacheControl: string) (asset: Assets.Asset) (ctx: HttpContext) : Threading.Tasks.Task =
        let bytes, tag = Assets.content asset
        let headers = ctx.Response.Headers
        headers.CacheControl <- Microsoft.Extensions.Primitives.StringValues cacheControl
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

    /// Serve an asset on its plain path: always revalidated.
    let serve (asset: Assets.Asset) (ctx: HttpContext) : Threading.Tasks.Task = serveAs revalidateCache asset ctx

    /// Serve an asset on its content-hashed path: `immutable` when the hash is
    /// the embedded one, revalidated when it is another build's (see the
    /// header), and 404 when it is not a hash at all, so the route does not
    /// answer for arbitrary names.
    let serveHashed (asset: Assets.Asset) (ctx: HttpContext) : Threading.Tasks.Task =
        let hash =
            match ctx.Request.RouteValues.TryGetValue hashParameter with
            | true, (:? string as value) -> value
            | _ -> ""

        let isHash =
            hash.Length = 8
            && hash |> Seq.forall (fun c -> (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))

        if hash = Assets.embeddedHash asset then
            serveAs immutableCache asset ctx
        elif isHash then
            serveAs revalidateCache asset ctx
        else
            ctx.Response.StatusCode <- StatusCodes.Status404NotFound
            Threading.Tasks.Task.CompletedTask

[<Extension>]
type FuaranRendererEndpointExtensions =

    /// Map the embedded renderer assets under `prefix` (no trailing slash).
    ///
    /// Serves five assets:
    ///
    ///   `GET {prefix}/fuaran-renderer.js`            the standalone browser renderer
    ///   `GET {prefix}/fuaran-reference.css`          the canonical reference stylesheet
    ///   `GET {prefix}/fuaran-reference-tables.js`    the table-sort enhancement
    ///   `GET {prefix}/fuaran-image-expand.js`        the expandable-image enhancement
    ///   `GET {prefix}/fingerprint.json`              what the embedded bundle IS
    ///
    /// and each of the first four again on its content-hashed path
    /// (`{prefix}/fuaran-renderer.<hash>.js`, Phase 1929) — the path to embed,
    /// and the only one served `immutable`. `Snippet` emits it;
    /// `Assets.versionedPath` names it for a host writing its own tags.
    ///
    /// The two enhancement scripts (Phase 1648) are PROGRESSIVE and a host loads
    /// them or does not: the stylesheet and the markup are correct without them.
    /// They are served because `Fuaran.UI.Renderer`'s own `content/README.md`
    /// documents the `<script src="…">` line and this package served neither, so
    /// the documented integration was unreachable from a package whose claim is
    /// that one call gives the browser everything.
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
    /// output-cache policy) to all of them at once.
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

            if asset.Immutable then
                // `fuaran-renderer.{hash}.js` — a complex segment, so the plain
                // literal route above still wins its own path.
                let template = "/" + Assets.fingerprintedPath asset ("{" + hashParameter + "}")

                group.MapGet(template, Func<HttpContext, Threading.Tasks.Task>(serveHashed asset))
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
