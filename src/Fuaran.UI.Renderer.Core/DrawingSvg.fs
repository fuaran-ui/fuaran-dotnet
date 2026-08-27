module Fuaran.UI.Renderer.DrawingSvg

// ============================================================================
//  Phase 525 — the canonical inline-SVG string for a `NodeKind.Drawing`.
//
//  A `Drawing` is resolved static geometry (Phase 524), so — unlike `Sparkline`,
//  whose data is client-resolved — it renders IDENTICALLY on the client and the
//  server. This module is the ONE builder both F# renderers emit (client via
//  `dangerouslySetInnerHTML`, SSR raw), the render-tier analogue of the wire
//  codec's single canonical serialisation: the TypeScript + Python hosts port
//  THIS builder. First-party, zero third-party dependency (generalises the
//  `Sparkline` inline-SVG precedent, D4).
//
//  Class vocabulary (parity-locked across hosts — Lock B extracts these literals
//  from the F# renderer source): `fuaran-drawing` (the <svg>) +
//  `fuaran-drawing-{group,rect,line,polyline,polygon,curve,circle,ellipse,label}`.
//  a11y (R3): `role="img"` + optional `<title>` / `<desc>` on the drawing root,
//  plus (Phase 921) an `aria-label` composing the two whenever a `<desc>` is
//  present — the attribute is what actually gets the description ANNOUNCED
//  under `role="img"`.
//
//  The builder emits raw markup (it rides `dangerouslySetInnerHTML`), so it
//  XML-escapes text/attribute content itself — the renderer's auto-escaping is
//  bypassed on this path.
// ============================================================================

open System.Text
open System.Globalization
open Fuaran.UI.Types

#if FABLE_COMPILER
// Fable does not support the `Double.ToString("R", …)` round-trip specifier used
// below for a fractional coordinate. This is the byte-identical Fable-safe shortest-
// round-trip encoder, ported VERBATIM from
// `Fuaran.UI.OpStream.Abstractions.CanonicalJson.formatFiniteDouble` (itself the
// fuaran-ts `formatFiniteDouble` parity oracle). Both runtimes compute the same
// shortest digits; this re-lays JS's layout into .NET's `"R"` form (fixed iff the
// leading-digit exponent is in [-4, 16], else uppercase-`E` scientific with a
// signed, ≥2-digit zero-padded exponent), so the Fable-client SVG bytes match the
// SSR bytes. KEEP IN SYNC with `CanonicalJson.formatFiniteDouble`.
[<Fable.Core.Emit("$0.toString()")>]
let private jsNumberToString (n: float) : string = Fable.Core.Util.jsNative

let private formatFiniteDouble (n: float) : string =
    if n = 0.0 then
        "0"
    else
        let neg = n < 0.0
        let s = jsNumberToString (abs n)
        let mutable digits = ""
        let mutable exp = 0
        let eIdx = s.IndexOf 'e'

        if eIdx >= 0 then
            let mant = s.Substring(0, eIdx)
            let mantExp = int (s.Substring(eIdx + 1))
            let dot = mant.IndexOf '.'

            if dot < 0 then
                digits <- mant
                exp <- mantExp + (mant.Length - 1)
            else
                digits <- mant.Substring(0, dot) + mant.Substring(dot + 1)
                exp <- mantExp + (dot - 1)
        else
            let dot = s.IndexOf '.'

            if dot < 0 then
                digits <- s
                exp <- s.Length - 1
            else
                let intPart = s.Substring(0, dot)
                let fracPart = s.Substring(dot + 1)

                if intPart = "0" then
                    let trimmed = fracPart.TrimStart('0')
                    let leadingZeros = fracPart.Length - trimmed.Length
                    digits <- fracPart.Substring(leadingZeros)
                    exp <- -(leadingZeros + 1)
                else
                    digits <- intPart + fracPart
                    exp <- intPart.Length - 1

        digits <- digits.TrimEnd('0')

        if digits = "" then
            digits <- "0"

        let out =
            if exp >= -4 && exp <= 16 then
                if exp >= 0 then
                    if digits.Length <= exp + 1 then
                        digits + String.replicate (exp + 1 - digits.Length) "0"
                    else
                        digits.Substring(0, exp + 1) + "." + digits.Substring(exp + 1)
                else
                    "0." + String.replicate (-exp - 1) "0" + digits
            else
                let mantissa =
                    if digits.Length = 1 then
                        digits
                    else
                        string digits[0] + "." + digits.Substring(1)

                let expSign = if exp >= 0 then "+" else "-"
                let expDigits = (abs exp).ToString().PadLeft(2, '0')
                mantissa + "E" + expSign + expDigits

        if neg then "-" + out else out
#endif

/// Canonical number form for an SVG coordinate/measure — a whole value drops the
/// decimal (`10`), else the invariant shortest round-trip form (`1.5`).
/// Culture-independent + deterministic, so every host agrees on the bytes.
let formatNum (n: float) : string =
    if System.Double.IsNaN n || System.Double.IsInfinity n then
        "0"
    elif n = System.Math.Floor n && abs n < 1e15 then
        (int64 n).ToString(CultureInfo.InvariantCulture)
    else
#if FABLE_COMPILER
        formatFiniteDouble n
#else
        n.ToString("R", CultureInfo.InvariantCulture)
#endif

/// XML-escape text / attribute content.
let escape (s: string) : string =
    let sb = StringBuilder(s.Length)

    for ch in s do
        match ch with
        | '&' -> sb.Append "&amp;" |> ignore
        | '<' -> sb.Append "&lt;" |> ignore
        | '>' -> sb.Append "&gt;" |> ignore
        | '"' -> sb.Append "&quot;" |> ignore
        | '\'' -> sb.Append "&#39;" |> ignore
        | c -> sb.Append c |> ignore

    sb.ToString()

/// The exact inverse of `escape` for the five entities it emits — a single
/// LEFT-TO-RIGHT pass, so a `&amp;lt;` produced from a literal `&lt;` in the
/// source text decodes back to `&lt;` and is never re-scanned into `<`. A
/// two-pass or replace-per-entity implementation gets that case wrong silently,
/// which is why this is written as a scanner.
///
/// Unrecognised entities are left VERBATIM rather than guessed at: this is the
/// inverse of one known emitter, not a general XML entity resolver, and
/// inventing a decoding for `&nbsp;` here would be a claim the emitter never
/// made.
let unescape (s: string) : string =
    let sb = StringBuilder(s.Length)
    let mutable i = 0

    // Written with `Substring` + structural `=` (ordinal on both runtimes)
    // rather than a positional `String.CompareOrdinal` overload — this file is
    // Fable-compiled, and the narrow BCL overloads are exactly where the two
    // pipelines diverge. The allocation is a recovery-path cost, not a render
    // one.
    let startsAt (needle: string) =
        i + needle.Length <= s.Length && s.Substring(i, needle.Length) = needle

    while i < s.Length do
        if startsAt "&amp;" then
            sb.Append '&' |> ignore
            i <- i + 5
        elif startsAt "&lt;" then
            sb.Append '<' |> ignore
            i <- i + 4
        elif startsAt "&gt;" then
            sb.Append '>' |> ignore
            i <- i + 4
        elif startsAt "&quot;" then
            sb.Append '"' |> ignore
            i <- i + 6
        elif startsAt "&#39;" then
            sb.Append '\'' |> ignore
            i <- i + 5
        else
            sb.Append s[i] |> ignore
            i <- i + 1

    sb.ToString()

// ─── Output budget (Phase 790) ───────────────────────────────────────────────
//
// A `Drawing` is ONE node, so a tree-size budget never sees the size of the
// markup it lowers to: a single shape list can emit an arbitrarily long string.
// The emitter below appends through a budget and STOPS at the ceiling rather
// than building the whole string and measuring it afterwards — measuring after
// the fact is not a bound, it is a post-mortem.

/// Why an SVG emission was refused.
type DrawingRenderError =
    /// The emission reached `limit` characters and was abandoned.
    | OutputTooLarge of limit: int

/// The default emitted-character ceiling. A legible inline SVG is a few tens of
/// kilobytes; a megabyte of markup is a transport and parse cost no viewer
/// benefits from.
[<Literal>]
let defaultMaxOutputChars = 1_000_000

/// A budgeted string emitter. Once the budget is exceeded it latches
/// `Overflowed` and drops every further append, so the caller can abandon the
/// walk without unwinding through exceptions (Fable-portable).
type private Emitter(limit: int) =
    let sb = StringBuilder()
    let mutable overflowed = false

    member _.Overflowed = overflowed

    member _.Add(s: string) : unit =
        if not overflowed then
            if sb.Length + s.Length > limit then
                overflowed <- true
            else
                sb.Append s |> ignore

    member _.Result = sb.ToString()

let private point (p: DrawPoint) : string = formatNum p.X + "," + formatNum p.Y

/// The `points` attribute body, appended through the budget one point at a time
/// so a pathological point list is abandoned at the ceiling rather than
/// materialised first.
let private emitPoints (e: Emitter) (points: DrawPoint list) : unit =
    let mutable rest = points
    let mutable first = true

    while not (List.isEmpty rest) && not e.Overflowed do
        if not first then
            e.Add " "

        e.Add(point (List.head rest))
        first <- false
        rest <- List.tail rest

/// One SVG path `d` command — the typed replacement for a raw authored `d`
/// (there is no `Path` shape, §524/§3).
let private commandD (c: CurveCommand) : string =
    match c with
    | CurveCommand.MoveTo p -> "M" + formatNum p.X + " " + formatNum p.Y
    | CurveCommand.LineTo p -> "L" + formatNum p.X + " " + formatNum p.Y
    | CurveCommand.CubicTo(c1, c2, e) ->
        "C"
        + formatNum c1.X
        + " "
        + formatNum c1.Y
        + " "
        + formatNum c2.X
        + " "
        + formatNum c2.Y
        + " "
        + formatNum e.X
        + " "
        + formatNum e.Y
    | CurveCommand.QuadraticTo(ctrl, e) ->
        "Q"
        + formatNum ctrl.X
        + " "
        + formatNum ctrl.Y
        + " "
        + formatNum e.X
        + " "
        + formatNum e.Y
    | CurveCommand.Close -> "Z"

/// The path `d` body, appended through the budget one command at a time (same
/// reasoning as `emitPoints`).
let private emitPathD (e: Emitter) (commands: CurveCommand list) : unit =
    let mutable rest = commands
    let mutable first = true

    while not (List.isEmpty rest) && not e.Overflowed do
        if not first then
            e.Add " "

        e.Add(commandD (List.head rest))
        first <- false
        rest <- List.tail rest

/// Resolve the `DrawStyle` bindings to SVG presentation attributes (in Ordinal
/// attribute order: fill, opacity, stroke, stroke-width). `defaultFillNone`
/// makes the open shapes (`Polyline` / `Curve`) not fill by default.
let private styleAttrs (sources: BindingResolver.BindingSources) (defaultFillNone: bool) (style: DrawStyle) : string =
    let sb = StringBuilder()

    let fill =
        match style.Fill with
        | Some b -> BindingResolver.tryResolve sources b
        | None -> if defaultFillNone then Some "none" else None

    fill
    |> Option.iter (fun v -> sb.Append(" fill=\"").Append(escape v).Append("\"") |> ignore)

    style.Opacity
    |> Option.bind (BindingResolver.tryResolve sources)
    |> Option.iter (fun v -> sb.Append(" opacity=\"").Append(formatNum v).Append("\"") |> ignore)

    style.Stroke
    |> Option.bind (BindingResolver.tryResolve sources)
    |> Option.iter (fun v -> sb.Append(" stroke=\"").Append(escape v).Append("\"") |> ignore)

    style.StrokeWidth
    |> Option.bind (BindingResolver.tryResolve sources)
    |> Option.iter (fun v -> sb.Append(" stroke-width=\"").Append(formatNum v).Append("\"") |> ignore)

    // Text-only presentation attributes (Phase 528.1) — applied to `<text>`;
    // ignored by SVG on non-text shapes. Fixed emit order for cross-host parity:
    // text-anchor, font-family, font-size, font-weight.
    style.TextAnchor
    |> Option.iter (fun a ->
        let v =
            match a with
            | TextAnchor.Start -> "start"
            | TextAnchor.Middle -> "middle"
            | TextAnchor.End -> "end"

        sb.Append(" text-anchor=\"").Append(v).Append("\"") |> ignore)

    style.FontFamily
    |> Option.iter (fun f -> sb.Append(" font-family=\"").Append(escape f).Append("\"") |> ignore)

    style.FontSize
    |> Option.iter (fun n -> sb.Append(" font-size=\"").Append(formatNum n).Append("px\"") |> ignore)

    style.Emphasis
    |> Option.iter (fun e ->
        let w =
            match e with
            | Emphasis.Quiet -> "300"
            | Emphasis.Normal -> "400"
            | Emphasis.Loud -> "700"

        sb.Append(" font-weight=\"").Append(w).Append("\"") |> ignore)

    // Phase 642 — keyed mark identity: a data-bearing shape's derivation-based
    // id rides into the emitted SVG so marks are addressable (object
    // constancy) — last in the fixed attribute order.
    style.MarkId
    |> Option.iter (fun m -> sb.Append(" data-fuaran-mark=\"").Append(escape m).Append("\"") |> ignore)

    sb.ToString()

/// Phase 875 — round line joins + caps on a STROKED path shape (`Polyline` /
/// `Polygon` / `Curve`). A RENDERER default, not a wire field: `DrawStyle` gains
/// nothing, no fixture changes shape, and every host emits the same two
/// attributes from its own builder. SVG's initial `stroke-linejoin` is `miter`,
/// which spikes at the acute vertices a data polyline routinely has — a visible
/// artefact that carries no data.
///
/// Emitted only when the shape actually strokes, so a fill-only polygon (an area
/// band) keeps its minimal attribute set. `Line` is deliberately excluded: a
/// round cap on the axis and gridline rules would overhang each end by half the
/// stroke width, lengthening chrome that is positioned exactly.
let private strokeJoinAttrs (sources: BindingResolver.BindingSources) (style: DrawStyle) : string =
    match style.Stroke |> Option.bind (BindingResolver.tryResolve sources) with
    | Some _ -> " stroke-linejoin=\"round\" stroke-linecap=\"round\""
    | None -> ""

// ─── Hover readout (Phase 883) ───────────────────────────────────────────────
//
// `DrawStyle.Tip` emits as an SVG `<title>` CHILD of the shape's own element.
// That single element is both the native browser tooltip (no script, so it
// works in a statically-served SSR page exactly as it does client-side) and the
// element's ACCESSIBLE NAME, so a screen reader traversing the SVG reads the
// same string a mouse user hovers. `<title>` must be the FIRST child to be the
// accessible name, which is why every arm below emits it before any other
// content.
//
// A tip is the one `DrawStyle` field the emitter honours on every shape rather
// than only on `Label`: the marks a reader hovers are the bars, wedges and
// points, and a `<title>` is inert geometry-wise on all of them (unlike
// `Rotation`, whose off-`Label` emission would move geometry).
//
// The structural consequence: a tipped shape cannot stay self-closing —
// `<rect …/>` becomes `<rect …><title>…</title></rect>`. An UNTIPPED shape is
// emitted exactly as before, byte-for-byte, so the whole pre-883 corpus and
// every untipped drawing are unchanged.
//
// The text is XML-escaped through the same `escape` the label text and the
// drawing `<title>` / `<desc>` already use — the builder emits raw markup (it
// rides `dangerouslySetInnerHTML`), so escaping here is the whole defence, and
// the chart lowering feeds it UNTRUSTED series/category strings straight off
// the data feed.
let private emitTip (e: Emitter) (textOf: TextSource -> string) (style: DrawStyle) : unit =
    style.Tip
    |> Option.iter (fun t ->
        e.Add "<title>"
        e.Add(escape (textOf t))
        e.Add "</title>")

/// The tail of a shape element that carries no child content of its own:
/// self-closing when untipped, an open/close pair wrapping the `<title>` when
/// tipped.
let private emitSelfClosing
    (e: Emitter)
    (textOf: TextSource -> string)
    (style: DrawStyle)
    (elementName: string)
    : unit =
    match style.Tip with
    | None -> e.Add "/>"
    | Some _ ->
        e.Add ">"
        emitTip e textOf style
        e.Add "</"
        e.Add elementName
        e.Add ">"

let rec private emitShape
    (e: Emitter)
    (sources: BindingResolver.BindingSources)
    (textOf: TextSource -> string)
    (shape: Shape)
    : unit =
    if not e.Overflowed then
        match shape with
        | Shape.Group(children, style) ->
            e.Add "<g class=\"fuaran-drawing-group\""
            e.Add(styleAttrs sources false style)
            e.Add ">"
            emitTip e textOf style

            let mutable rest = children

            while not (List.isEmpty rest) && not e.Overflowed do
                emitShape e sources textOf (List.head rest)
                rest <- List.tail rest

            e.Add "</g>"
        | Shape.Rectangle(x, y, w, h, cornerRadius, style) ->
            let rx =
                match cornerRadius with
                | Some r -> " rx=\"" + formatNum r + "\""
                | None -> ""

            e.Add "<rect class=\"fuaran-drawing-rect\" x=\""
            e.Add(formatNum x)
            e.Add "\" y=\""
            e.Add(formatNum y)
            e.Add "\" width=\""
            e.Add(formatNum w)
            e.Add "\" height=\""
            e.Add(formatNum h)
            e.Add "\""
            e.Add rx
            e.Add(styleAttrs sources false style)
            emitSelfClosing e textOf style "rect"
        | Shape.Line(x1, y1, x2, y2, style) ->
            e.Add "<line class=\"fuaran-drawing-line\" x1=\""
            e.Add(formatNum x1)
            e.Add "\" y1=\""
            e.Add(formatNum y1)
            e.Add "\" x2=\""
            e.Add(formatNum x2)
            e.Add "\" y2=\""
            e.Add(formatNum y2)
            e.Add "\""
            e.Add(styleAttrs sources false style)
            emitSelfClosing e textOf style "line"
        | Shape.Polyline(points, style) ->
            e.Add "<polyline class=\"fuaran-drawing-polyline\" points=\""
            emitPoints e points
            e.Add "\""
            e.Add(styleAttrs sources true style)
            e.Add(strokeJoinAttrs sources style)
            emitSelfClosing e textOf style "polyline"
        | Shape.Polygon(points, style) ->
            e.Add "<polygon class=\"fuaran-drawing-polygon\" points=\""
            emitPoints e points
            e.Add "\""
            e.Add(styleAttrs sources false style)
            e.Add(strokeJoinAttrs sources style)
            emitSelfClosing e textOf style "polygon"
        | Shape.Curve(commands, style) ->
            e.Add "<path class=\"fuaran-drawing-curve\" d=\""
            emitPathD e commands
            e.Add "\""
            e.Add(styleAttrs sources true style)
            e.Add(strokeJoinAttrs sources style)
            emitSelfClosing e textOf style "path"
        | Shape.Circle(cx, cy, r, style) ->
            e.Add "<circle class=\"fuaran-drawing-circle\" cx=\""
            e.Add(formatNum cx)
            e.Add "\" cy=\""
            e.Add(formatNum cy)
            e.Add "\" r=\""
            e.Add(formatNum r)
            e.Add "\""
            e.Add(styleAttrs sources false style)
            emitSelfClosing e textOf style "circle"
        | Shape.Ellipse(cx, cy, rx, ry, style) ->
            e.Add "<ellipse class=\"fuaran-drawing-ellipse\" cx=\""
            e.Add(formatNum cx)
            e.Add "\" cy=\""
            e.Add(formatNum cy)
            e.Add "\" rx=\""
            e.Add(formatNum rx)
            e.Add "\" ry=\""
            e.Add(formatNum ry)
            e.Add "\""
            e.Add(styleAttrs sources false style)
            emitSelfClosing e textOf style "ellipse"
        | Shape.Label(x, y, text, style) ->
            e.Add "<text class=\"fuaran-drawing-label\" x=\""
            e.Add(formatNum x)
            e.Add "\" y=\""
            e.Add(formatNum y)
            e.Add "\""

            // Phase 877 — text rotation. Emitted HERE rather than in
            // `styleAttrs` because the pivot is the label's own anchor point,
            // which the style record does not know; `styleAttrs` is shared by
            // every shape and stays position-free. Anchoring at (x, y) is what
            // makes the rotation compose with `TextAnchor`: the text turns
            // about the point it is aligned to, so a `Middle`-anchored tilted
            // category label stays centred under its band and an `End`-anchored
            // one still ends at the axis. Degrees, clockwise (SVG's own
            // convention), so no sign conversion is needed on any host.
            //
            // A rotation on any OTHER shape is deliberately not emitted — the
            // Phase 528.1 text fields are documented as ignored off `Label`,
            // and SVG's `transform` would otherwise *move geometry*, making the
            // one text field with side-effects elsewhere.
            style.Rotation
            |> Option.iter (fun deg ->
                e.Add " transform=\"rotate("
                e.Add(formatNum deg)
                e.Add " "
                e.Add(formatNum x)
                e.Add " "
                e.Add(formatNum y)
                e.Add ")\"")

            e.Add(styleAttrs sources false style)
            e.Add ">"
            // Before the visible run: `<title>` is the accessible name only as
            // the FIRST child, and SVG does not render it either way.
            emitTip e textOf style
            e.Add(escape (textOf text))
            e.Add "</text>"

// ─── The root's announced accessible name (Phase 921) ────────────────────────
//
// `role="img"` (Phase 532's R3) presents the drawing as ONE graphic, and that is
// the posture the operator confirmed on 2026-08-18. What it does NOT do on its
// own is get `<desc>` announced. The SVG accessible-name mapping puts `<title>`
// on the name reliably; `<desc>` maps to the accessible DESCRIPTION, which is
// (a) not implemented uniformly — Chromium has never exposed it — and (b) a
// verbosity-gated announcement even where it is. So the value the markup has
// carried since Phase 525 is one a reader cannot reach, which is exactly what
// the 2026-08-18 CONFLICTS-AND-GAPS entry recorded.
//
// The fix is `aria-label` on the root, carrying the TITLE and the DESCRIPTION
// composed into one string. It is the accessible NAME, which every assistive
// technology announces unconditionally for a `role="img"` element, and it is the
// pattern the renderer already uses for a labelled `Display.Icon`.
//
// WHY NOT `aria-labelledby` / `aria-describedby` — the textbook answer. Both
// reference elements BY ID, and this builder has no id to give: its whole input
// is a `DrawingSpec`, several drawings routinely share one document, and any id
// it minted would have to be both unique per page and byte-identical across five
// hosts. A counter is not deterministic, a content hash is not free, and
// widening the signature to thread the node id is a public-API and cross-host
// change on every emitter for an attribute `aria-label` already discharges.
//
// The composition is TITLE first, then DESCRIPTION, because that is the order
// the two artefacts are meant to be read in — and it is what lets the chart
// lowering's generated summary (Phase 921) stay title-free while the announced
// string still opens with the chart's name. The title is terminated with a
// period unless it already ends in sentence punctuation, so the reader hears two
// sentences rather than one run-on.
//
// Emitted ONLY when a `Description` is present: a drawing carrying just a
// `<title>` is already named by it, so every pre-921 title-only drawing is
// byte-identical. The `<title>` and `<desc>` children are unchanged and still
// emitted — `<title>` remains the native hover tooltip, and `<desc>` remains the
// SVG-native artefact for the hosts and tools that do read it.
let private terminateTitle (t: string) : string =
    if t = "" then
        ""
    else
        match t.[t.Length - 1] with
        | '.'
        | '!'
        | '?' -> t
        | _ -> t + "."

/// The composed accessible name, or `""` when the root emits no `aria-label`.
let private rootAriaLabel (textOf: TextSource -> string) (title: TextSource option) (description: TextSource option) =
    match description with
    | None -> ""
    | Some d ->
        let descText = textOf d

        let composed =
            match title |> Option.map (textOf >> terminateTitle) with
            | Some t when t <> "" -> t + " " + descText
            | _ -> descText

        " aria-label=\"" + escape composed + "\""

let private viewBoxAttr (vb: ViewBox) : string =
    formatNum vb.MinX
    + " "
    + formatNum vb.MinY
    + " "
    + formatNum vb.Width
    + " "
    + formatNum vb.Height

// ─── Self-describing output (Phase 643) ──────────────────────────────────────
//
// An emitted drawing can carry ONE opaque canonical-JSON document in an SVG
// `<metadata>` element, so an exported figure is recoverable to whatever
// produced it. This module stays SPEC-AGNOSTIC about that document: it is handed
// a string, it escapes and splices it, and it hands the same string back. The
// chart tier owns what the document MEANS (`Fuaran.UI.Charts` — the provenance
// stamp + the embedded `ChartSpec` wire JSON); the drawing builder owns only
// that it survives the SVG intact.
//
// THREE decisions, each load-bearing:
//
//  1. **Entity escaping, never CDATA.** The document is XML TEXT CONTENT run
//     through the same `escape` every label and `<title>` already uses. CDATA
//     was the obvious alternative and is wrong here: a CDATA section terminates
//     at the first `]]>`, and a JSON payload carrying user data can contain that
//     sequence — at which point the SVG is malformed and the failure is a parse
//     error a long way from its cause. Entity escaping has no such sequence, and
//     `&quot;` / `&#39;` in text content are ordinary XML that any conformant
//     parser resolves, so a third-party tool recovers the document without
//     knowing anything about this emitter.
//
//     The escaping is also what makes the RECOVERY scan unambiguous: after
//     escaping, no `<` survives in the payload, so the first `</metadata>` after
//     the opening tag is necessarily the real terminator. A canonical JSON
//     document additionally carries no raw control character (the canonical
//     encoder escapes them inside strings and emits none outside), so the text
//     is XML-safe once the five entities are handled.
//
//  2. **Position: AFTER `<title>` / `<desc>`, before the shapes.** `<title>` is
//     the accessible name only as the FIRST child (see the a11y note above), so
//     the metadata cannot lead. Putting it before the shapes rather than last
//     keeps the recovery scan cheap on a large drawing and keeps the provenance
//     adjacent to the two other descriptive elements.
//
//  3. **It rides the SAME output budget as everything else.** An embedded typed
//     row table is the one part of this markup with no bound of its own, so it
//     is appended through the `Emitter` and abandoned at the ceiling like any
//     other content — a refusal, not a megabyte. That is the size guard: a
//     spec+data emission that refuses can be retried spec-only, which is why the
//     chart tier's option distinguishes the two rather than being a bool.

/// The marker attribute identifying a `<metadata>` element as OURS — SVG's own
/// metadata element is general-purpose, so an authoring tool's is correctly not
/// read as a provenance document.
///
/// A `data-` ATTRIBUTE rather than an `id`, for two reasons. Several drawings
/// routinely share one document, so an id would be DUPLICATED across every
/// stamped chart on a page — invalid HTML, and exactly the reasoning that kept
/// the a11y wiring off `aria-labelledby` (see the note above). And it matches
/// the `data-fuaran-*` convention every other non-class markup hook in this
/// renderer already uses (`data-fuaran-mark`, `data-fuaran-node-id`).
[<Literal>]
let metadataMarkerAttribute = "data-fuaran-provenance"

/// The embedded document's shape version, carried as the marker's VALUE. A later
/// document shape is a different version here rather than a silent
/// reinterpretation of this one.
[<Literal>]
let metadataDocumentVersion = "v1"

let private metadataOpen =
    "<metadata " + metadataMarkerAttribute + "=\"" + metadataDocumentVersion + "\">"

[<Literal>]
let private metadataClose = "</metadata>"

/// The full canonical inline-SVG string for a `Drawing`, emitted under an
/// explicit character ceiling (Phase 790), optionally carrying an embedded
/// canonical-JSON provenance document (Phase 643). `role="img"` + optional
/// `<title>` / `<desc>` (a11y, R3); `viewBox` from the spec; the root `Style`
/// applies to `<svg>` and is inherited by shapes that omit their own. `textOf`
/// resolves label / title / desc `TextSource`s (each renderer passes its own
/// text resolver, so I18n / bound text resolve exactly as the rest of the tree).
///
/// `metadata = None` emits BYTE-IDENTICAL markup to every pre-643 drawing — the
/// `<metadata>` element is not emitted empty.
///
/// Over-budget emission is ABANDONED at the ceiling and reported as
/// `OutputTooLarge` — the partial markup is never returned.
let tryRenderWithMetadataAndLimit
    (limit: int)
    (metadata: string option)
    (sources: BindingResolver.BindingSources)
    (textOf: TextSource -> string)
    (spec: DrawingSpec)
    : Result<string, DrawingRenderError> =
    let e = Emitter(limit)

    e.Add "<svg class=\"fuaran-drawing\" role=\"img\" viewBox=\""
    e.Add(viewBoxAttr spec.ViewBox)
    e.Add "\""
    e.Add(rootAriaLabel textOf spec.Title spec.Description)
    e.Add(styleAttrs sources false spec.Style)
    e.Add ">"

    match spec.Title with
    | Some t ->
        e.Add "<title>"
        e.Add(escape (textOf t))
        e.Add "</title>"
    | None -> ()

    match spec.Description with
    | Some d ->
        e.Add "<desc>"
        e.Add(escape (textOf d))
        e.Add "</desc>"
    | None -> ()

    match metadata with
    | Some doc ->
        e.Add metadataOpen
        e.Add(escape doc)
        e.Add metadataClose
    | None -> ()

    let mutable rest = spec.Shapes

    while not (List.isEmpty rest) && not e.Overflowed do
        emitShape e sources textOf (List.head rest)
        rest <- List.tail rest

    e.Add "</svg>"

    if e.Overflowed then
        Error(OutputTooLarge limit)
    else
        Ok e.Result

/// `tryRenderWithMetadataAndLimit` with no embedded metadata — the pre-643
/// signature, byte-identical output.
let tryRenderWithLimit
    (limit: int)
    (sources: BindingResolver.BindingSources)
    (textOf: TextSource -> string)
    (spec: DrawingSpec)
    : Result<string, DrawingRenderError> =
    tryRenderWithMetadataAndLimit limit None sources textOf spec

/// `tryRenderWithLimit` at the default ceiling (`defaultMaxOutputChars`).
let tryRender
    (sources: BindingResolver.BindingSources)
    (textOf: TextSource -> string)
    (spec: DrawingSpec)
    : Result<string, DrawingRenderError> =
    tryRenderWithLimit defaultMaxOutputChars sources textOf spec

/// `tryRenderWithMetadataAndLimit` at the default ceiling.
let tryRenderWithMetadata
    (metadata: string option)
    (sources: BindingResolver.BindingSources)
    (textOf: TextSource -> string)
    (spec: DrawingSpec)
    : Result<string, DrawingRenderError> =
    tryRenderWithMetadataAndLimit defaultMaxOutputChars metadata sources textOf spec

/// The canonical inline-SVG string for a `Drawing` at the default output
/// ceiling, optionally self-describing. An over-budget drawing renders as a
/// bounded refusal SVG — the same canvas, no shapes, and a `<desc>` saying why —
/// rather than an unbounded string; `tryRenderWithMetadata` is the typed form
/// for a caller that wants to handle the refusal itself (and, for an embedded
/// row table, retry spec-only).
///
/// The refusal carries NO metadata: it is the statement that this drawing was
/// not emitted, and stamping provenance onto a picture that was not drawn would
/// be a claim about an artefact that does not exist.
let renderWithMetadata
    (metadata: string option)
    (sources: BindingResolver.BindingSources)
    (textOf: TextSource -> string)
    (spec: DrawingSpec)
    : string =
    match tryRenderWithMetadata metadata sources textOf spec with
    | Ok svg -> svg
    | Error(OutputTooLarge limit) ->
        let reason =
            "Drawing not rendered: emitted markup exceeds the limit of "
            + string limit
            + " characters."

        // Phase 921 — the refusal is the one thing this markup says, so it takes
        // the same `aria-label` wiring the ordinary root does. A refusal a
        // reader cannot hear is an empty picture with no explanation.
        "<svg class=\"fuaran-drawing\" role=\"img\" viewBox=\""
        + viewBoxAttr spec.ViewBox
        + "\" aria-label=\""
        + escape reason
        + "\"><desc>"
        + escape reason
        + "</desc></svg>"

/// `renderWithMetadata` with no embedded metadata — the pre-643 signature,
/// byte-identical output.
let render (sources: BindingResolver.BindingSources) (textOf: TextSource -> string) (spec: DrawingSpec) : string =
    renderWithMetadata None sources textOf spec

/// Recover the embedded canonical-JSON provenance document from a
/// self-describing SVG, or `None` when the markup carries none.
///
/// A deliberate SCAN rather than an XML parse: the emitter above is the only
/// thing that writes this element, it writes exactly one, and it escapes every
/// `<` in the payload — so the first `</metadata>` after the opening tag is
/// necessarily the terminator, and no dependency on an XML reader (which Fable
/// has no portable equivalent of) is needed to read our own output back.
///
/// The tag match is on the FULL opening tag including the versioned id, so a
/// foreign `<metadata>` an authoring tool added — SVG's own metadata element is
/// general-purpose — is correctly not read as ours.
let tryRecoverMetadata (svg: string) : string option =
    let start = svg.IndexOf(metadataOpen, System.StringComparison.Ordinal)

    if start < 0 then
        None
    else
        let bodyStart = start + metadataOpen.Length
        let bodyEnd = svg.IndexOf(metadataClose, bodyStart, System.StringComparison.Ordinal)

        if bodyEnd < 0 then
            // An opening tag with no terminator is truncated markup, not a
            // document — recovering a prefix of a JSON payload would hand the
            // caller something that parses as nothing and reads as data loss.
            None
        else
            Some(unescape (svg.Substring(bodyStart, bodyEnd - bodyStart)))
