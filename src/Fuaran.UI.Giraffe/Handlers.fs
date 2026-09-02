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
          EgressPolicy = Sanitize.denyNonLocalEgress }

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

    /// The deterministic OPTIONS contribution to the ETag: the theme CSS, plus
    /// the canonical projection of the destination policy (Phase 1026).
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
    let private optionsSig (opts: FuaranGiraffeOptions) : string =
        // The ambient locale is part of the signature since Phase 1114: it is
        // now an INPUT to the rendered document (`<html lang dir>`), so two
        // option sets differing only in locale must not share a cache entry.
        themeCss opts
        + "|"
        + Sanitize.encodeEgressPolicy opts.EgressPolicy
        + "|"
        + opts.Sources.Locale

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
    let private respond
        (etag: string)
        (contentType: string)
        (render: unit -> string)
        (cache: IFuaranRenderCache)
        : HttpHandler =
        fun (next: HttpFunc) (ctx: HttpContext) ->
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

    /// Render a Fuaran tree as a full crawlable document — static SSR, no
    /// client runtime. Host-authored `<head>` from the shell; zero hand-written
    /// shell HTML.
    let fuaranPage (opts: FuaranGiraffeOptions) (shell: DocumentShell) (node: Node<obj>) : HttpHandler =
        let etag =
            Etag.compute (CanonicalJson.encodeNode node) (optionsSig opts) (shellSig Static (Some shell))

        let render () =
            Document.renderWithLocale opts.Sources.Locale shell (themeBlock opts + bodyFor opts Static node)

        respond etag "text/html; charset=utf-8" render opts.Cache

    /// Render a Fuaran tree as a full document plus the Phase 143 hydrate
    /// payload — server render for first paint + SEO, then `hydrateRoot` for
    /// interactivity. Parity (Phase 142) keeps the mount mismatch-free.
    let fuaranHydratablePage (opts: FuaranGiraffeOptions) (shell: DocumentShell) (node: Node<obj>) : HttpHandler =
        let etag =
            Etag.compute (CanonicalJson.encodeNode node) (optionsSig opts) (shellSig Hydratable (Some shell))

        let render () =
            Document.renderWithLocale opts.Sources.Locale shell (themeBlock opts + bodyFor opts Hydratable node)

        respond etag "text/html; charset=utf-8" render opts.Cache

    /// Render a Fuaran tree as a static document with **islands** (Phase 163):
    /// per-`Node.asIsland` subtree, a `data-fuaran-island` boundary wrapper + an
    /// embedded hydrate `<script>`; everything else stays inert static HTML. The
    /// islands-aware variant of `fuaranHydratablePage` — the client mounts
    /// `Renderer.Hydration.hydrateIslands` to attach exactly those subtrees.
    let fuaranIslandsPage (opts: FuaranGiraffeOptions) (shell: DocumentShell) (node: Node<obj>) : HttpHandler =
        let etag =
            Etag.compute (CanonicalJson.encodeNode node) (optionsSig opts) (shellSig Islands (Some shell))

        let render () =
            Document.renderWithLocale opts.Sources.Locale shell (themeBlock opts + bodyFor opts Islands node)

        respond etag "text/html; charset=utf-8" render opts.Cache

    /// Render a Fuaran tree as a body fragment only (no document shell) — for
    /// HTMX-style swaps or a host that composes its own document. ETag + 304 +
    /// cache apply identically.
    let fuaranFragment (opts: FuaranGiraffeOptions) (node: Node<obj>) : HttpHandler =
        let etag =
            Etag.compute (CanonicalJson.encodeNode node) (optionsSig opts) (shellSig Fragment None)

        let render () = bodyFor opts Fragment node

        respond etag "text/html; charset=utf-8" render opts.Cache
