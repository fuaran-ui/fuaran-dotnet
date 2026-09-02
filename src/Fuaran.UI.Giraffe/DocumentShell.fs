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
type ScriptRef =
    { Src: string
      Module: bool
      Defer: bool
      Async: bool }

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
    }

[<RequireQualifiedAccess>]
module ScriptRef =
    /// A plain `<script src=…>` (no module / defer / async).
    let create (src: string) : ScriptRef =
        { Src = src
          Module = false
          Defer = false
          Async = false }

    /// A `<script type="module" src=…>`.
    let moduleScript (src: string) : ScriptRef = { create src with Module = true }

    /// A deferred `<script defer src=…>`.
    let deferred (src: string) : ScriptRef = { create src with Defer = true }

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
          BodyAttributes = [] }

    /// Pin the document's language to a BCP-47 tag — `withLocale "ar-EG"` emits
    /// `lang="ar-EG" dir="rtl"`. The direction is derived from the tag, never
    /// passed separately, so the two can never disagree.
    let withLocale (tag: string) (shell: DocumentShell) : DocumentShell =
        { shell with
            Locale = LocaleSource.Explicit tag }

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

    let private renderAttrs (attrs: (string * string) list) : string =
        attrs
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
