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
//  a11y (R3): `role="img"` + optional `<title>` / `<desc>` on the drawing root.
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
            e.Add "/>"
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
            e.Add "/>"
        | Shape.Polyline(points, style) ->
            e.Add "<polyline class=\"fuaran-drawing-polyline\" points=\""
            emitPoints e points
            e.Add "\""
            e.Add(styleAttrs sources true style)
            e.Add(strokeJoinAttrs sources style)
            e.Add "/>"
        | Shape.Polygon(points, style) ->
            e.Add "<polygon class=\"fuaran-drawing-polygon\" points=\""
            emitPoints e points
            e.Add "\""
            e.Add(styleAttrs sources false style)
            e.Add(strokeJoinAttrs sources style)
            e.Add "/>"
        | Shape.Curve(commands, style) ->
            e.Add "<path class=\"fuaran-drawing-curve\" d=\""
            emitPathD e commands
            e.Add "\""
            e.Add(styleAttrs sources true style)
            e.Add(strokeJoinAttrs sources style)
            e.Add "/>"
        | Shape.Circle(cx, cy, r, style) ->
            e.Add "<circle class=\"fuaran-drawing-circle\" cx=\""
            e.Add(formatNum cx)
            e.Add "\" cy=\""
            e.Add(formatNum cy)
            e.Add "\" r=\""
            e.Add(formatNum r)
            e.Add "\""
            e.Add(styleAttrs sources false style)
            e.Add "/>"
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
            e.Add "/>"
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
            e.Add(escape (textOf text))
            e.Add "</text>"

let private viewBoxAttr (vb: ViewBox) : string =
    formatNum vb.MinX
    + " "
    + formatNum vb.MinY
    + " "
    + formatNum vb.Width
    + " "
    + formatNum vb.Height

/// The full canonical inline-SVG string for a `Drawing`, emitted under an
/// explicit character ceiling (Phase 790). `role="img"` + optional `<title>` /
/// `<desc>` (a11y, R3); `viewBox` from the spec; the root `Style` applies to
/// `<svg>` and is inherited by shapes that omit their own. `textOf` resolves
/// label / title / desc `TextSource`s (each renderer passes its own text
/// resolver, so I18n / bound text resolve exactly as the rest of the tree).
///
/// Over-budget emission is ABANDONED at the ceiling and reported as
/// `OutputTooLarge` — the partial markup is never returned.
let tryRenderWithLimit
    (limit: int)
    (sources: BindingResolver.BindingSources)
    (textOf: TextSource -> string)
    (spec: DrawingSpec)
    : Result<string, DrawingRenderError> =
    let e = Emitter(limit)

    e.Add "<svg class=\"fuaran-drawing\" role=\"img\" viewBox=\""
    e.Add(viewBoxAttr spec.ViewBox)
    e.Add "\""
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

    let mutable rest = spec.Shapes

    while not (List.isEmpty rest) && not e.Overflowed do
        emitShape e sources textOf (List.head rest)
        rest <- List.tail rest

    e.Add "</svg>"

    if e.Overflowed then
        Error(OutputTooLarge limit)
    else
        Ok e.Result

/// `tryRenderWithLimit` at the default ceiling (`defaultMaxOutputChars`).
let tryRender
    (sources: BindingResolver.BindingSources)
    (textOf: TextSource -> string)
    (spec: DrawingSpec)
    : Result<string, DrawingRenderError> =
    tryRenderWithLimit defaultMaxOutputChars sources textOf spec

/// The canonical inline-SVG string for a `Drawing` at the default output
/// ceiling. An over-budget drawing renders as a bounded refusal SVG — the same
/// canvas, no shapes, and a `<desc>` saying why — rather than an unbounded
/// string; `tryRender` is the typed form for a caller that wants to handle the
/// refusal itself.
let render (sources: BindingResolver.BindingSources) (textOf: TextSource -> string) (spec: DrawingSpec) : string =
    match tryRender sources textOf spec with
    | Ok svg -> svg
    | Error(OutputTooLarge limit) ->
        "<svg class=\"fuaran-drawing\" role=\"img\" viewBox=\""
        + viewBoxAttr spec.ViewBox
        + "\"><desc>"
        + escape (
            "Drawing not rendered: emitted markup exceeds the limit of "
            + string limit
            + " characters."
        )
        + "</desc></svg>"
