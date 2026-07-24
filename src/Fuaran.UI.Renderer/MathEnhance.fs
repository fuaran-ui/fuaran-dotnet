module Fuaran.UI.Renderer.MathEnhance

// ============================================================================
//  Phase 293 — a native pure-F# (Fable) twin of the TS `@fuaran-ui/renderer`
//  `enhanceMath`: the client-only, post-hydration KaTeX pass.
//
//  Both renderers emit the SAME deterministic math output (Phase 658) — a `Math`
//  node → a `.fuaran-math-{block,inline}` container holding native MathML for the
//  closed subset (real superscripts, no JS) or a `<span class="fuaran-math-source">`
//  (raw escaped LaTeX) for out-of-subset input, with the original LaTeX in
//  `data-fuaran-math-src`; rendered markdown leaves inline `$…$` / `$$…$$` as
//  literal text. That deterministic output is the ONLY thing the cross-host +
//  SSR↔CSR parity gate compares.
//
//  `enhance` is the separate, opt-in, client-only layer that KaTeX-renders that
//  fallback IN PLACE after hydration. Because it runs after hydration and is
//  never part of the renderer's output, it can never cause a hydration mismatch
//  or a cross-host divergence; KaTeX is a client dependency, not a parity-path
//  one. It is idempotent (a processed node is marked `data-fuaran-math-done`).
//
//  This native module exists for Fable apps that do NOT carry the
//  `@fuaran-ui/*` npm packages (those should reuse the canonical TS enhancer via
//  `@fuaran-ui/renderer/enhance-math`, one implementation, zero divergence). The
//  host must also load `katex/dist/katex.min.css` and have `katex` resolvable.
//  The logic mirrors the unit-tested TS `enhanceMath` exactly.
// ============================================================================

/// A segment of a text run: literal `Text`, inline `$…$` math, or display
/// `$$…$$` math. The pure `parseSegments` below produces these; it carries no
/// browser dependency, so it compiles + unit-tests on .NET as well as Fable.
type SegKind =
    | Text
    | Inline
    | Display

/// Split a text run into literal-text / inline-`$…$` / display-`$$…$$` segments.
/// `\$` is a literal dollar; an unterminated `$`/`$$` stays literal text (the
/// conservative CommonMark-ish reading). Pure — mirrors the TS `parseMathSegments`.
let parseSegments (input: string) : (SegKind * string) list =
    let out = ResizeArray<SegKind * string>()
    let buf = System.Text.StringBuilder()
    // sentinel '\000' past the end — mirrors the TS `charAt` (avoids bounds noise)
    let charAt idx =
        if idx >= 0 && idx < input.Length then
            input[idx]
        else
            '\000'

    let flush () =
        if buf.Length > 0 then
            out.Add(Text, buf.ToString())
            buf.Clear() |> ignore

    let mutable i = 0

    while i < input.Length do
        let ch = input[i]

        if ch = '\\' && charAt (i + 1) = '$' then
            buf.Append '$' |> ignore
            i <- i + 2
        elif ch = '$' then
            let display = charAt (i + 1) = '$'
            let delimLen = if display then 2 else 1
            let start = i + delimLen
            let mutable j = start
            let mutable close = -1

            while j < input.Length && close < 0 do
                if charAt j = '\\' then
                    j <- j + 2
                elif
                    (if display then
                         charAt j = '$' && charAt (j + 1) = '$'
                     else
                         charAt j = '$')
                then
                    close <- j
                else
                    j <- j + 1

            if close > start then
                flush ()
                out.Add((if display then Display else Inline), input.Substring(start, close - start))
                i <- close + delimLen
            else
                buf.Append ch |> ignore
                i <- i + 1
        else
            buf.Append ch |> ignore
            i <- i + 1

    flush ()
    List.ofSeq out

#if FABLE_COMPILER
open Fable.Core
open Fable.Core.JsInterop
open Browser.Types

[<ImportDefault("katex")>]
let private katex: obj = jsNative

[<Literal>]
let private doneAttr = "data-fuaran-math-done"

// Phase 658 — the deterministic Math container now carries the original LaTeX in
// `data-fuaran-math-src` (its content is native MathML for in-subset input, whose
// text is NOT valid LaTeX, or the raw-source span otherwise). The enhancement
// reads the source from here.
[<Literal>]
let private srcAttr = "data-fuaran-math-src"

/// Tags whose text content is verbatim and must never be scanned for `$…$`.
let private skipTags = set [ "CODE"; "PRE"; "SCRIPT"; "STYLE"; "TEXTAREA" ]

/// KaTeX-render `tex` into `el`. On any failure, leave the deterministic source
/// fallback in place (the reader still sees the raw LaTeX, never a blank).
let private renderInto (el: Element) (tex: string) (displayMode: bool) : unit =
    try
        let opts =
            createObj
                [ "displayMode" ==> displayMode
                  "throwOnError" ==> false
                  "output" ==> "htmlAndMathml" ]

        el.innerHTML <- unbox<string> (katex?renderToString (tex, opts))
    with _ ->
        ()

/// Upgrade the explicit `Math` nodes (Phase 658: the `.fuaran-math` CONTAINER,
/// MathML or source variant alike). Reads the LaTeX from `data-fuaran-math-src`
/// (falling back to `textContent`) and replaces the container content wholesale
/// with KaTeX output.
///
/// Idempotence is CONTENT-AWARE, not marker-alone: React can restore the
/// deterministic children while the imperatively-set `doneAttr` survives on the
/// same element, leaving a marked container with no KaTeX inside (the rung-1
/// race the 2026-07-24 tidy-up names). A container counts as done only when the
/// KaTeX output is actually present; the marker remains as a cheap tie-breaker
/// for the failure path (renderInto caught → deterministic content kept).
let private enhanceMathNodes (root: Element) : unit =
    let nodes = root.querySelectorAll ".fuaran-math"

    for i in 0 .. nodes.length - 1 do
        let el = nodes[i] :?> HTMLElement

        if isNull (el.querySelector ".katex") then
            let fromAttr = el.getAttribute srcAttr
            let tex = if isNull fromAttr then el.textContent else fromAttr
            let displayMode = el.classList.contains "fuaran-math-block"
            renderInto el tex displayMode
            el.setAttribute (doneAttr, "")

/// Replace inline `$…$` / `$$…$$` in a single text node with KaTeX spans.
let private enhanceTextNode (node: Text) : unit =
    let raw = node.nodeValue

    if not (isNull raw) && raw.Contains "$" then
        match parseSegments raw with
        | [ (Text, _) ] -> () // no math
        | segs ->
            let doc = node.ownerDocument
            let frag = doc.createDocumentFragment ()

            for (kind, value) in segs do
                match kind with
                | Text -> frag.appendChild (doc.createTextNode value) |> ignore
                | Inline
                | Display ->
                    let display = (kind = Display)
                    let span = doc.createElement "span"

                    span.className <-
                        if display then
                            "fuaran-math fuaran-math-block"
                        else
                            "fuaran-math fuaran-math-inline"

                    span.setAttribute ("data-math-display", (if display then "block" else "inline"))
                    span.setAttribute (doneAttr, "")
                    renderInto span value display
                    frag.appendChild span |> ignore

            node.parentNode.replaceChild (frag, node) |> ignore

/// Collect every scannable text node under `node` (skipping code/pre/etc. and
/// already-rendered KaTeX subtrees).
let rec private collectTextNodes (node: Node) (acc: ResizeArray<Text>) : unit =
    let children = node.childNodes

    for i in 0 .. children.length - 1 do
        let child = children[i]

        if child.nodeType = 3 then // TEXT_NODE
            acc.Add(child :?> Text)
        elif child.nodeType = 1 then // ELEMENT_NODE
            let el = child :?> HTMLElement

            if not (skipTags.Contains el.tagName) && not (el.classList.contains "katex") then
                collectTextNodes child acc

/// Upgrade inline `$…$` spans inside rendered-markdown blocks. Same
/// content-aware idempotence as the Math containers: a done-marked block whose
/// KaTeX spans were wiped by a React restore (no `.katex` inside, but `$` text
/// back in the DOM) is re-scanned; a done-marked block whose enhancement
/// survived — or that never had math — is skipped.
let private enhanceMarkdownInline (root: Element) : unit =
    let blocks = root.querySelectorAll ".fuaran-markdown"

    for i in 0 .. blocks.length - 1 do
        let block = blocks[i] :?> HTMLElement

        let needsScan =
            if isNull (block.getAttribute doneAttr) then
                true
            else
                isNull (block.querySelector ".katex")
                && not (isNull block.textContent)
                && block.textContent.Contains "$"

        if needsScan then
            let acc = ResizeArray<Text>()
            collectTextNodes block acc

            for t in acc do
                enhanceTextNode t

            block.setAttribute (doneAttr, "")

/// KaTeX-render the deterministic math fallback inside `root`. Idempotent +
/// client-only — call once after each render/hydration. The host must load
/// `katex/dist/katex.min.css`. The pure-F# twin of `@fuaran-ui/renderer`
/// `enhanceMath`.
let enhance (root: Element) : unit =
    enhanceMathNodes root
    enhanceMarkdownInline root

/// Convenience: enhance the whole document body.
let enhanceDocument () : unit =
    let body = Browser.Dom.document.body

    if not (isNull (box body)) then
        enhance (body :> Element)

#else

/// .NET no-op — the KaTeX enhancement is a browser-only (Fable) concern. The
/// deterministic source fallback renders correctly without it server-side, so
/// there is nothing to do on the .NET tier.
let enhance (_root: Browser.Types.Element) : unit = ()

/// .NET no-op (see `enhance`).
let enhanceDocument () : unit = ()

#endif
