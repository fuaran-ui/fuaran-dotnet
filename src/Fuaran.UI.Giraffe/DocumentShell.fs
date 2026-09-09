namespace Fuaran.UI.Giraffe

// ============================================================================
//  Fuaran — Giraffe SSR host adapter: the typed document shell (Phase 162).
//
//  The Wave-18 SSR stack (`Fuaran.UI.Renderer.Server`) emits the *body
//  fragment* HTML for a `Node` tree; the document shell — `<!DOCTYPE html>` /
//  `<html>` / `<head>` (title, meta, canonical, OG/Twitter cards, JSON-LD,
//  stylesheet + script refs) wrapped around that body — stayed host-*authored*
//  hand-rolled boilerplate in every consumer. This makes the shell
//  library-*shaped*: the host still authors the head *content* (it owns its
//  brand, its SEO copy, its analytics) but stops re-writing the wrapper plumbing.
//
//  Injection safety (SANITIZATION.md discipline):
//   - Text-shaped fields (title, meta description, OG/Twitter content) are
//     emitted through `Feliz.ViewEngine`, which HTML-escapes text + attribute
//     values — a `<script>` substring in a title cannot break out.
//   - URL-shaped fields (canonical, stylesheet hrefs, script srcs) route
//     through `Fuaran.UI.Renderer.Sanitize.sanitizeUrlOrBlank` — a
//     `javascript:` href becomes `about:blank`.
//   - JSON-LD is the ONE sanctioned raw-JSON injection point (host-trusted —
//     structured data must be verbatim JSON). It is `<`/`>`/`&`-escaped for
//     `<script>` embedding exactly like the Phase 143 hydrate payload, so a
//     `</script>` substring inside string data cannot terminate the element;
//     JSON parsers read the escapes back, so the structured data is unchanged.
// ============================================================================

open Feliz.ViewEngine
open Fuaran.UI.Types
open Fuaran.UI.Renderer

/// A `<script>` reference for the document head. `Module` emits
/// `type="module"`; `Defer` / `Async` emit the matching boolean attributes.
///
/// Phase 1523 — `Nonce`, `Integrity` and `CrossOrigin` join them, and the
/// omission they close was not cosmetic: a host serving a nonce-based CSP
/// (`script-src 'nonce-…'`) could not use `Scripts` AT ALL, because every
/// `<script>` this shell emitted lacked the nonce and was therefore blocked by
/// the very policy the host had adopted to be safe. So the shell's script slot
/// was unusable in exactly the deployments that had done the most work to
/// secure themselves, and `Renderer.Web`'s `Snippet.mount` already modelled a
/// nonce — meaning the estate had the concept and only this surface lacked it.
///
/// All three are `option`, so every existing record literal keeps compiling by
/// adding `None` (the `FS0764` cost the draft slot already carries) and every
/// existing emission is byte-identical when they are `None`.
type ScriptRef =
    {
        Src: string
        Module: bool
        Defer: bool
        Async: bool
        /// The CSP nonce for this script — emitted as `nonce="…"`. The host
        /// mints it per response and puts the same value in its
        /// `Content-Security-Policy` header; nothing here generates one, because
        /// a nonce the document could derive is a nonce an attacker can derive.
        Nonce: string option
        /// A Subresource Integrity digest (`sha384-…`) — emitted as
        /// `integrity="…"`. Meaningful on a cross-origin script, which is why
        /// `CrossOrigin` sits beside it: browsers require CORS for SRI on a
        /// cross-origin fetch, so an `Integrity` without a `CrossOrigin` on a
        /// third-party URL fails closed and the script does not load.
        Integrity: string option
        /// The `crossorigin` mode — `"anonymous"` or `"use-credentials"`.
        CrossOrigin: string option
    }

/// The host-authored document shell composed around the Fuaran body fragment.
/// Build with `DocumentShell.create "<title>"` then record-`with` the fields the
/// page needs — the defaults emit a minimal, valid, crawlable document.
type DocumentShell =
    {
        /// `<title>` text (HTML-escaped).
        Title: string
        /// `<meta name="description">` — `None` omits the tag.
        MetaDescription: string option
        /// `<link rel="canonical">` — `None` omits it; the URL is sanitized.
        Canonical: string option
        /// Open Graph `(property, content)` pairs, e.g.
        /// `("og:title", "…")` → `<meta property="og:title" content="…">`.
        OpenGraph: (string * string) list
        /// Twitter-card `(name, content)` pairs, e.g.
        /// `("twitter:card", "summary")` → `<meta name="…" content="…">`.
        TwitterCard: (string * string) list
        /// Raw JSON-LD payloads — each emitted as its own
        /// `<script type="application/ld+json">` (host-trusted, script-escaped).
        JsonLd: string list
        /// Stylesheet hrefs → `<link rel="stylesheet">` (URL-sanitized).
        Stylesheets: string list
        /// Script refs (URL-sanitized).
        Scripts: ScriptRef list
        /// The document's language (Phase 1114). `lang` and `dir` on `<html>`
        /// are DERIVED from this, never hand-written:
        ///
        ///  - `LocaleSource.Explicit "ar-EG"` → `lang="ar-EG" dir="rtl"`;
        ///  - `LocaleSource.Ambient` → the host's own locale, supplied at render
        ///    time (`Document.renderWithLocale`, which is what the handlers in
        ///    `Handlers.fs` call with `FuaranGiraffeOptions.Sources.Locale`).
        ///
        /// A resolved tag that is EMPTY emits neither attribute. That is
        /// deliberate: the shell used to hardcode `lang="en"`, which is an
        /// assertion about a page nobody had made a statement about, and it was
        /// wrong for every document this phase exists to make renderable. A host
        /// that wants a language declared says which one — `DocumentShell.withLocale`
        /// is one call — and an explicit `("lang", …)` in `HtmlAttributes` still
        /// wins, so the escape hatch is unchanged.
        Locale: LocaleSource
        /// `<html>` attributes (values escaped). A `lang` / `dir` pair here
        /// OVERRIDES the `Locale`-derived one — the host's own word is final.
        HtmlAttributes: (string * string) list
        /// `<body>` attributes (values escaped).
        BodyAttributes: (string * string) list
        /// Phase 1545 — this response's CSP nonce, when the host is serving the
        /// document under a nonce-based policy.
        ///
        /// The shell does not generate one and never will, for the reason
        /// `ScriptRef.Nonce` gives: a nonce the document could derive is a nonce
        /// an attacker can derive. The host mints it per response, puts the same
        /// value in its `Content-Security-Policy` header
        /// (`Csp.styleSrcDirective` writes the style half), and hands it here.
        ///
        /// What the shell does with it: every `ScriptRef` that declares no nonce
        /// of its own inherits this one, so a host adopting a nonce policy does
        /// not have to remember it per script. The `<style>` elements — the
        /// theme block and the collected strict-mode stylesheet — are emitted by
        /// `Renderer.Server` rather than here, and take the same nonce through
        /// `Render.themeStyleElementWithCsp` / `Render.renderWithCsp`; so one
        /// value reaches all three emitters and a host declares it once.
        ///
        /// `None` (the default) emits exactly what this shell emitted before.
        Nonce: string option
    }

[<RequireQualifiedAccess>]
module ScriptRef =
    /// A plain `<script src=…>` (no module / defer / async).
    let create (src: string) : ScriptRef =
        { Src = src
          Module = false
          Defer = false
          Async = false
          Nonce = None
          Integrity = None
          CrossOrigin = None }

    /// A `<script type="module" src=…>`.
    let moduleScript (src: string) : ScriptRef = { create src with Module = true }

    /// A deferred `<script defer src=…>`.
    let deferred (src: string) : ScriptRef = { create src with Defer = true }

    /// Carry the host's per-response CSP nonce (Phase 1523). The host mints the
    /// value and puts the SAME one in its `Content-Security-Policy` header;
    /// nothing here generates one, because a nonce the document could derive is
    /// a nonce an attacker can derive.
    let withNonce (nonce: string) (script: ScriptRef) : ScriptRef = { script with Nonce = Some nonce }

    /// Carry a Subresource Integrity digest and the CORS mode it needs
    /// (Phase 1523).
    ///
    /// The two are set TOGETHER rather than by two functions, because a browser
    /// requires CORS for SRI on a cross-origin fetch: an `integrity` without a
    /// `crossorigin` on a third-party URL fails closed and the script silently
    /// does not load. Pairing them makes the working combination the easy one to
    /// write. `"anonymous"` is the mode a public CDN wants; a host needing
    /// `"use-credentials"` passes it.
    let withIntegrity (digest: string) (crossOrigin: string) (script: ScriptRef) : ScriptRef =
        { script with
            Integrity = Some digest
            CrossOrigin = Some crossOrigin }

[<RequireQualifiedAccess>]
module DocumentShell =
    /// A minimal shell — just a `<title>`, with the document language deferred
    /// to the host (`LocaleSource.Ambient`). Build up the SEO fields with
    /// record-`with` syntax, and declare the language with `withLocale`.
    let create (title: string) : DocumentShell =
        { Title = title
          MetaDescription = None
          Canonical = None
          OpenGraph = []
          TwitterCard = []
          JsonLd = []
          Stylesheets = []
          Scripts = []
          Locale = LocaleSource.Ambient
          HtmlAttributes = []
          BodyAttributes = []
          Nonce = None }

    /// Pin the document's language to a BCP-47 tag — `withLocale "ar-EG"` emits
    /// `lang="ar-EG" dir="rtl"`. The direction is derived from the tag, never
    /// passed separately, so the two can never disagree.
    let withLocale (tag: string) (shell: DocumentShell) : DocumentShell =
        { shell with
            Locale = LocaleSource.Explicit tag }

    /// Declare this response's CSP nonce (Phase 1545). Every script this shell
    /// emits that has not declared its own nonce inherits it, and the host
    /// passes the SAME value to `Render.renderWithCsp` /
    /// `Render.themeStyleElementWithCsp` so the two `<style>` elements carry it
    /// too — one declaration, one directive
    /// (`Csp.styleSrcDirective`), no `'unsafe-inline'`.
    let withNonce (nonceValue: string) (shell: DocumentShell) : DocumentShell = { shell with Nonce = Some nonceValue }

[<RequireQualifiedAccess>]
module Document =

    /// Escape raw JSON for safe embedding inside a `<script>` element. JSON
    /// parsers decode `<` etc. back to the original characters, so the
    /// structured data round-trips unchanged (same scheme as `Hydration`).
    let private escapeForScript (json: string) : string =
        json.Replace("<", "\\u003c").Replace(">", "\\u003e").Replace("&", "\\u0026")

    /// Escape an HTML attribute value for the hand-emitted `<html>` / `<body>`
    /// open tags (the `<head>` content escapes via ViewEngine; these two tags
    /// are concatenated as strings so they escape here).
    let private attrEscape (s: string) : string =
        s.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;")

    /// Render an attribute list into the hand-emitted `<html>` / `<body>` open
    /// tags, DROPPING any entry whose NAME is not a safe attribute name
    /// (Phase 1523, the Phase 788 class ported here).
    ///
    /// The value is escaped above; the NAME was written verbatim, and HTML has
    /// no escape for an illegal character in an attribute name — a space inside
    /// one simply starts a NEW attribute and an `=` starts its value. So a
    /// `HtmlAttributes` / `BodyAttributes` key of `data-x=1 onload=alert(1) z`
    /// was not a mangled name; it was three attributes, one of them a live event
    /// handler, on the document's own `<html>` element. These two tags are
    /// concatenated as strings rather than built through ViewEngine, which is
    /// precisely why the gate the rest of the renderer gets for free had to be
    /// applied by hand — and was not.
    ///
    /// Dropping rather than escaping is the only correct response, for the
    /// reason `Sanitize.isSafeAttributeName` gives: there is nothing to escape
    /// to. A dropped attribute is a missing attribute, which is visible; a
    /// mangled one would be a different attribute, which is not.
    let private renderAttrs (attrs: (string * string) list) : string =
        attrs
        |> List.filter (fun (k, _) -> Sanitize.isSafeAttributeName k)
        |> List.map (fun (k, v) -> sprintf " %s=\"%s\"" k (attrEscape v))
        |> String.concat ""

    /// The `<head>` element built via ViewEngine (text + attribute escaping for
    /// free); URL fields sanitized, JSON-LD script-escaped.
    let private headElement (shell: DocumentShell) : ReactElement =
        Html.head
            [ Html.meta [ prop.custom ("charset", "utf-8") ]
              Html.meta
                  [ prop.custom ("name", "viewport")
                    prop.custom ("content", "width=device-width, initial-scale=1") ]
              Html.title [ prop.text shell.Title ]
              match shell.MetaDescription with
              | Some d -> Html.meta [ prop.custom ("name", "description"); prop.custom ("content", d) ]
              | None -> Html.none
              match shell.Canonical with
              | Some url -> Html.link [ prop.rel "canonical"; prop.href (Sanitize.sanitizeUrlOrBlank url) ]
              | None -> Html.none
              for (property, content) in shell.OpenGraph do
                  Html.meta [ prop.custom ("property", property); prop.custom ("content", content) ]
              for (name, content) in shell.TwitterCard do
                  Html.meta [ prop.custom ("name", name); prop.custom ("content", content) ]
              for href in shell.Stylesheets do
                  Html.link [ prop.rel "stylesheet"; prop.href (Sanitize.sanitizeUrlOrBlank href) ]
              for s in shell.Scripts do
                  Html.script (
                      [ prop.src (Sanitize.sanitizeUrlOrBlank s.Src) ]
                      @ (if s.Module then [ prop.custom ("type", "module") ] else [])
                      @ (if s.Defer then [ prop.custom ("defer", "") ] else [])
                      @ (if s.Async then [ prop.custom ("async", "") ] else [])
                      // Phase 1523 — emitted only when set, so a shell that
                      // declares none is byte-identical to the pre-1523
                      // emission. ViewEngine escapes these attribute VALUES, so
                      // the host's own nonce / digest strings need no handling
                      // here beyond being placed.
                      //
                      // Phase 1545 — the script's OWN nonce wins; the shell's is
                      // the default for scripts that declare none, so a host
                      // adopting a nonce policy declares the value once
                      // (`DocumentShell.withNonce`) rather than per script. A
                      // shell with no nonce is the pre-1545 emission exactly.
                      @ (match s.Nonce, shell.Nonce with
                         | Some n, _
                         | None, Some n -> [ prop.custom ("nonce", n) ]
                         | None, None -> [])
                      @ (match s.Integrity with
                         | Some i -> [ prop.custom ("integrity", i) ]
                         | None -> [])
                      @ (match s.CrossOrigin with
                         | Some c -> [ prop.custom ("crossorigin", c) ]
                         | None -> [])
                  )
              for jsonLd in shell.JsonLd do
                  Html.script
                      [ prop.custom ("type", "application/ld+json")
                        prop.dangerouslySetInnerHTML (escapeForScript jsonLd) ] ]

    /// The `<html>` attribute list with the locale-derived `lang` / `dir` pair
    /// prepended (Phase 1114).
    ///
    /// `ambientTag` resolves `LocaleSource.Ambient` — the host's own configured
    /// locale, which for the Giraffe handlers is
    /// `FuaranGiraffeOptions.Sources.Locale`, the same string a
    /// `Binding.Format` with an ambient locale formats against. So a page's
    /// numbers and its writing direction come from one declaration rather than
    /// two that can disagree.
    ///
    /// A host-authored `lang` / `dir` in `HtmlAttributes` WINS: the derived pair
    /// is prepended and then any key the host also set is dropped from the
    /// derived half, so the host's value is the one emitted and it is emitted
    /// once.
    let private htmlAttributes (ambientTag: string) (shell: DocumentShell) : (string * string) list =
        let tag =
            match shell.Locale with
            | LocaleSource.Explicit t -> t
            | LocaleSource.Ambient -> ambientTag

        let derived =
            if System.String.IsNullOrWhiteSpace tag then
                []
            else
                [ "lang", tag; "dir", Formatting.textDirection tag ]

        let authored = shell.HtmlAttributes |> List.map fst |> Set.ofList

        (derived |> List.filter (fun (k, _) -> not (authored.Contains k)))
        @ shell.HtmlAttributes

    /// Render a full `<!DOCTYPE html>` document around a body-fragment HTML
    /// string, resolving `LocaleSource.Ambient` against `ambientTag`. The
    /// `<head>` is the escaping-safe ViewEngine emission; the body fragment
    /// (already-safe HTML from `Renderer.Server`) is injected verbatim inside
    /// `<body>` with no wrapper element (so a hydration root keeps its own `id`
    /// / `data-fuaran-node-id`).
    let renderWithLocale (ambientTag: string) (shell: DocumentShell) (bodyHtml: string) : string =
        let head = Render.htmlView (headElement shell)

        sprintf
            "<!DOCTYPE html>\n<html%s>%s<body%s>%s</body></html>"
            (renderAttrs (htmlAttributes ambientTag shell))
            head
            (renderAttrs shell.BodyAttributes)
            bodyHtml

    /// `renderWithLocale` with no ambient locale — so a shell whose `Locale` is
    /// `Ambient` declares no language at all. Kept as the two-argument entry
    /// point every existing caller uses; a host with a configured locale wants
    /// `renderWithLocale`, which is what the handlers call.
    let render (shell: DocumentShell) (bodyHtml: string) : string = renderWithLocale "" shell bodyHtml
