namespace Fuaran.UI.Giraffe

// ============================================================================
//  Fuaran — Giraffe SSR host adapter: the page HttpHandlers (Phase 162).
//
//  "Render this `Node` tree as a crawlable page" becomes one `HttpHandler`
//  call. Three entry points over the one tree:
//
//   - `fuaranPage`           — static SSR: a full crawlable document.
//   - `fuaranHydratablePage` — the document + the Phase 143 hydrate payload, so
//                              the client `hydrateRoot` mount attaches.
//   - `fuaranFragment`       — body fragment only (no document shell), for
//                              HTMX-style / host-composed responses.
//
//  Each threads a single `FuaranGiraffeOptions` (binding sources + server
//  Custom registry + optional theme + render cache) registered once by the
//  host. Every response carries the deterministic strong ETag and honours
//  `If-None-Match` → 304; the render cache is consulted before render and
//  populated after.
//
//  The SSR tier is dispatch-less, so trees are `Node<obj>` — the same type
//  `Renderer.Server` consumes (no message handler runs server-side; FGP 3 is
//  satisfied vacuously). The shipped renderer's `Node<obj>` posture is the
//  contract this adapter wraps, not a narrowing of it.
// ============================================================================

open Giraffe
open Feliz.ViewEngine
open Microsoft.AspNetCore.Http
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.OpStream.Abstractions

module Server = Fuaran.UI.Renderer.Server.Render
module ServerHydration = Fuaran.UI.Renderer.Server.Hydration
module ServerRegistry = Fuaran.UI.Renderer.Server.Registry

/// The once-registered options every Fuaran Giraffe handler threads. Build with
/// `FuaranGiraffeOptions.create` and record-`with` the binding sources / Custom
/// registry / theme / cache the host supplies.
type FuaranGiraffeOptions =
    {
        /// Drives binding resolution server-side (`Query` sources, i18n, etc.).
        Sources: BindingResolver.BindingSources
        /// Host server Custom-renderer registry (Phase 141). Empty by default.
        Customs: ServerRegistry.ServerCustomRendererRegistry
        /// Optional theme — when `Some`, its `:root` CSS block is emitted as a
        /// `<style>` ahead of the body fragment (parity with the client
        /// `themeStyleElement`). `None` leaves styling entirely host-owned.
        /// (`Theme` is the language-tier type; `Theme.toCss` the companion module.)
        Theme: Theme option
        /// Render cache (default `RenderCache.none`, a zero-cost pass-through).
        Cache: IFuaranRenderCache
        /// Phase 1026 — the destination policy every `href` / `src` this host
        /// emits is checked against. Defaults to `Sanitize.denyNonLocalEgress`,
        /// so an SSR host that declares nothing serves documents pointing
        /// nowhere but its own origin.
        ///
        /// This is the field a Giraffe host sets to declare its egress:
        ///
        /// ```fsharp
        /// { FuaranGiraffeOptions.create with
        ///     EgressPolicy =
        ///         Sanitize.denyNonLocalEgress
        ///         |> Sanitize.allowOrigin (Sanitize.HostSuffix "cdn.example") [ Sanitize.EgressClass.Media ] }
        /// ```
        ///
        /// It is folded into the ETag (and therefore the render CACHE KEY),
        /// because two policies render the same tree to different documents:
        /// without it, a cache populated under one policy would serve that
        /// document to a request rendering under another.
        EgressPolicy: Sanitize.EgressPolicy
        /// Phase 1532 — the host's own name for the variant of `Sources` this
        /// options record carries, folded into the ETag (and therefore the
        /// render cache key).
        ///
        /// `BindingSources` holds `obj` query results, module state, filters and
        /// selections, plus an i18n resolver and a capability invoker that are
        /// arbitrary code. No adapter can project those into bytes, so nothing
        /// here can compute what makes one host's sources DIFFERENT from
        /// another's — only the host knows whether two source bags render the
        /// same document. Naming the variant is that knowledge:
        ///
        /// ```fsharp
        /// { options with
        ///     Sources = sourcesFor user
        ///     SourcesKey = Some (sprintf "u:%s|rev:%d" user.Id user.DataRevision) }
        /// ```
        ///
        /// The key is a CACHE KEY, not a secret: it is hashed into the ETag,
        /// which is public, so give it a tenant discriminator rather than a
        /// tenant's data.
        ///
        /// `None` is the honest default and means "I have not said". When the
        /// sources are also OPAQUE (`RenderInputs.sourcesOpaque`), the handlers
        /// then emit **no ETag at all** and mark the response `no-store` rather
        /// than mint a validator that cannot separate two users' documents —
        /// and constructing a handler that would ALSO put such a render in a
        /// shared cache is refused outright (`FuaranGiraffeOptions.validate`).
        SourcesKey: string option
    }

[<RequireQualifiedAccess>]
module FuaranGiraffeOptions =
    /// Default options — empty binding sources, no Custom renderers, no theme,
    /// no cache, and no declared egress. Record-`with` the fields the host
    /// supplies.
    let create: FuaranGiraffeOptions =
        { Sources = BindingResolver.empty
          Customs = ServerRegistry.empty
          Theme = None
          Cache = RenderCache.none
          EgressPolicy = Sanitize.denyNonLocalEgress
          SourcesKey = None }

    /// Does this options record ask for a rendered document to be CACHED?
    ///
    /// Reference identity against the shipped no-op, which is the only question
    /// available: `IFuaranRenderCache` is a host-supplied interface with no
    /// member that could answer it. `RenderCache.none` is a module-level value,
    /// so every read of it is the same instance; a host that hands back its own
    /// never-hitting implementation reads as "caching" and is held to the
    /// stricter rule, which is the safe direction to be wrong in.
    let cacheConfigured (opts: FuaranGiraffeOptions) : bool =
        not (System.Object.ReferenceEquals(opts.Cache, RenderCache.none))

    /// The ETag / cache-key identity of the binding sources, or `None` when the
    /// sources carry host data no adapter can project and the host has not
    /// named the variant (Phase 1532).
    let sourcesIdentity (opts: FuaranGiraffeOptions) : string option =
        let canonical = RenderInputs.sourcesCanonical opts.Sources

        match opts.SourcesKey with
        | Some key -> Some(canonical + string RenderInputs.Separator + "key:" + key)
        | None when RenderInputs.sourcesOpaque opts.Sources -> None
        | None -> Some(canonical + string RenderInputs.Separator + "key:<none>")

    /// Refuse an options record that would put a source-blind render into a
    /// SHARED cache: opaque sources, no declared `SourcesKey`, and a cache the
    /// host configured. Raised where the handler is CONSTRUCTED, so a host that
    /// builds its options per request meets it on the first request rather than
    /// on the first collision.
    ///
    /// Deliberately not silent: dropping the cache instead would leave a host
    /// believing it had one. The remedy is one field.
    let validate (opts: FuaranGiraffeOptions) : unit =
        if cacheConfigured opts && (sourcesIdentity opts).IsNone then
            invalidOp
                "Fuaran.UI.Giraffe: this FuaranGiraffeOptions configures a render Cache over binding sources that carry host data (query results / state / filters / selections / computed context, or a replaced i18n resolver or capability invoker) without declaring SourcesKey. The ETag cannot separate two source bags, so the cache would serve one request's document to another's. Set SourcesKey to a token that varies with the sources (a tenant or user discriminator plus a data revision), or leave Cache = RenderCache.none."

[<AutoOpen>]
module Handlers =

    /// Body emission mode (folded into the ETag so a static and a hydratable
    /// render of the same tree get distinct cache keys).
    type private BodyMode =
        | Static
        | Hydratable
        | Islands
        | Fragment

    let private bodyModeTag =
        function
        | Static -> "static"
        | Hydratable -> "hydratable"
        | Islands -> "islands"
        | Fragment -> "fragment"

    /// The theme `<style>` block (parity with `Renderer.Server.themeStyleElement`),
    /// or empty when no theme is configured.
    let private themeBlock (opts: FuaranGiraffeOptions) : string =
        match opts.Theme with
        | Some theme -> Render.htmlView (Server.themeStyleElement theme)
        | None -> ""

    /// Deterministic theme-CSS contribution to the ETag (empty when no theme).
    let private themeCss (opts: FuaranGiraffeOptions) : string =
        match opts.Theme with
        | Some theme -> Theme.toCss theme
        | None -> ""

    /// The deterministic OPTIONS contribution to the ETag: the theme CSS, the
    /// canonical projection of the destination policy (Phase 1026), the ambient
    /// locale, and — since Phase 1532 — the identity of the binding sources and
    /// of the server `Custom` registry.
    ///
    /// Returns `None` exactly when the sources have no identity (opaque and
    /// unnamed), which is the caller's signal to emit no validator at all.
    ///
    /// The policy belongs in the cache key because it changes the OUTPUT: the
    /// same tree renders a live `href` under one policy and
    /// `about:blank#fuaran-egress-refused` under another. Omitting it would let
    /// a document rendered under a permissive policy be served, from cache, to a
    /// request whose host had since narrowed it — a stale-cache bug that
    /// presents as an egress-policy failure, which is the worst way to meet one.
    ///
    /// `Sanitize.encodeEgressPolicy` is canonical and sorted, so it is stable
    /// across runs for the same policy — an ETag input has to be, or every
    /// process restart invalidates every cached page.
    let private optionsSig (opts: FuaranGiraffeOptions) : string option =
        // The ambient locale is part of the signature since Phase 1114: it is
        // now an INPUT to the rendered document (`<html lang dir>`), so two
        // option sets differing only in locale must not share a cache entry. It
        // now arrives inside the sources identity, which is where it lives.
        //
        // NUL-joined, like the ETag's own three fields: theme CSS and a policy
        // projection both contain `|` freely, so the old separator could not
        // keep the fields apart.
        FuaranGiraffeOptions.sourcesIdentity opts
        |> Option.map (fun sources ->
            String.concat
                (string RenderInputs.Separator)
                [ themeCss opts
                  Sanitize.encodeEgressPolicy opts.EgressPolicy
                  sources
                  RenderInputs.customsIdentity opts.Customs ])

    /// A deterministic signature of the shell + render mode (`sprintf "%A"` over
    /// the shell record is stable across runs for the same value). `Fragment`
    /// renders carry no shell.
    let private shellSig (mode: BodyMode) (shell: DocumentShell option) : string =
        let shellPart =
            match shell with
            | Some s -> sprintf "%A" s
            | None -> "<no-shell>"

        sprintf "%s|%s" (bodyModeTag mode) shellPart

    /// Emit the body fragment for a render mode.
    let private bodyFor (opts: FuaranGiraffeOptions) (mode: BodyMode) (node: Node<obj>) : string =
        let fragment =
            Server.renderWithEgress opts.EgressPolicy opts.Customs opts.Sources node

        match mode with
        | Static
        | Fragment -> fragment
        | Hydratable ->
            // Body fragment + the Phase 143 embedded wire-tree `<script>` the
            // client hydrate mount decodes. (renderWith already emitted the
            // fragment with the Custom registry; append the hydrate payload.)
            fragment + Render.htmlView (ServerHydration.embeddedTreeElement node)
        | Islands ->
            // Static page with per-island boundary wrappers + one embedded
            // hydrate `<script>` per `Node.asIsland` subtree (Phase 163). Zero
            // islands ⇒ inert static HTML, no hydrate script.
            ServerHydration.renderWithIslandsAndEgress opts.EgressPolicy opts.Sources node

    /// Write the document/fragment with the strong ETag + `If-None-Match` → 304;
    /// the render cache is consulted before render and populated after.
    ///
    /// `etag` is `None` when the render inputs have no computable identity —
    /// opaque binding sources the host has not named (Phase 1532). Then there is
    /// no validator to emit and no key to cache under: the response carries
    /// `Cache-Control: no-store` and is rendered fresh. Emitting a validator
    /// that cannot separate two users' documents would be worse than emitting
    /// none, because a browser honours it.
    let private respond
        (etag: string option)
        (contentType: string)
        (render: unit -> string)
        (cache: IFuaranRenderCache)
        : HttpHandler =
        fun (next: HttpFunc) (ctx: HttpContext) ->
            match etag with
            | None ->
                (setHttpHeader "Cache-Control" "no-store"
                 >=> setHttpHeader "Content-Type" contentType
                 >=> setBodyFromString (render ()))
                    next
                    ctx
            | Some etag ->
                let ifNoneMatch = ctx.Request.Headers.IfNoneMatch

                let notModified =
                    ifNoneMatch.Count > 0 && (ifNoneMatch |> Seq.exists (fun v -> v = etag))

                if notModified then
                    (setStatusCode 304 >=> setHttpHeader "ETag" etag) next ctx
                else
                    let html =
                        match cache.TryGet etag with
                        | Some cached -> cached
                        | None ->
                            let produced = render ()
                            cache.Set(etag, produced)
                            produced

                    (setHttpHeader "ETag" etag
                     >=> setHttpHeader "Content-Type" contentType
                     >=> setBodyFromString html)
                        next
                        ctx

    /// The response validator for one render, or `None` when the inputs have no
    /// identity. Every handler goes through here, so the refusal and the
    /// no-validator fallback are stated once.
    let private etagFor
        (opts: FuaranGiraffeOptions)
        (mode: BodyMode)
        (shell: DocumentShell option)
        (node: Node<obj>)
        : string option =
        FuaranGiraffeOptions.validate opts

        optionsSig opts
        |> Option.map (fun sig' -> Etag.compute (CanonicalJson.encodeNode node) sig' (shellSig mode shell))

    /// Render a Fuaran tree as a full crawlable document — static SSR, no
    /// client runtime. Host-authored `<head>` from the shell; zero hand-written
    /// shell HTML.
    let fuaranPage (opts: FuaranGiraffeOptions) (shell: DocumentShell) (node: Node<obj>) : HttpHandler =
        let etag = etagFor opts Static (Some shell) node

        let render () =
            Document.renderWithLocale opts.Sources.Locale shell (themeBlock opts + bodyFor opts Static node)

        respond etag "text/html; charset=utf-8" render opts.Cache

    /// Render a Fuaran tree as a full document plus the Phase 143 hydrate
    /// payload — server render for first paint + SEO, then `hydrateRoot` for
    /// interactivity. Parity (Phase 142) keeps the mount mismatch-free.
    let fuaranHydratablePage (opts: FuaranGiraffeOptions) (shell: DocumentShell) (node: Node<obj>) : HttpHandler =
        let etag = etagFor opts Hydratable (Some shell) node

        let render () =
            Document.renderWithLocale opts.Sources.Locale shell (themeBlock opts + bodyFor opts Hydratable node)

        respond etag "text/html; charset=utf-8" render opts.Cache

    /// Render a Fuaran tree as a static document with **islands** (Phase 163):
    /// per-`Node.asIsland` subtree, a `data-fuaran-island` boundary wrapper + an
    /// embedded hydrate `<script>`; everything else stays inert static HTML. The
    /// islands-aware variant of `fuaranHydratablePage` — the client mounts
    /// `Renderer.Hydration.hydrateIslands` to attach exactly those subtrees.
    let fuaranIslandsPage (opts: FuaranGiraffeOptions) (shell: DocumentShell) (node: Node<obj>) : HttpHandler =
        let etag = etagFor opts Islands (Some shell) node

        let render () =
            Document.renderWithLocale opts.Sources.Locale shell (themeBlock opts + bodyFor opts Islands node)

        respond etag "text/html; charset=utf-8" render opts.Cache

    /// Render a Fuaran tree as a body fragment only (no document shell) — for
    /// HTMX-style swaps or a host that composes its own document. ETag + 304 +
    /// cache apply identically.
    let fuaranFragment (opts: FuaranGiraffeOptions) (node: Node<obj>) : HttpHandler =
        let etag = etagFor opts Fragment None node

        let render () = bodyFor opts Fragment node

        respond etag "text/html; charset=utf-8" render opts.Cache
