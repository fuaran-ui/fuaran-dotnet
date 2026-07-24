module Fuaran.UI.Renderer.DrawingSvg

// ============================================================================
//  Phase 525 — the canonical inline-SVG string for a `DisplayKind.Drawing`.
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

let private point (p: DrawPoint) : string = formatNum p.X + "," + formatNum p.Y

let private pointsAttr (points: DrawPoint list) : string =
    points |> List.map point |> String.concat " "

/// Build the SVG path `d` string from the typed `CurveCommand` list — the typed
/// replacement for a raw authored `d` (there is no `Path` shape, §524/§3).
let private pathD (commands: CurveCommand list) : string =
    commands
    |> List.map (fun c ->
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
        | CurveCommand.Close -> "Z")
    |> String.concat " "

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

let rec private shapeSvg
    (sources: BindingResolver.BindingSources)
    (textOf: TextSource -> string)
    (shape: Shape)
    : string =
    match shape with
    | Shape.Group(children, style) ->
        let inner = children |> List.map (shapeSvg sources textOf) |> String.concat ""

        "<g class=\"fuaran-drawing-group\""
        + styleAttrs sources false style
        + ">"
        + inner
        + "</g>"
    | Shape.Rectangle(x, y, w, h, cornerRadius, style) ->
        let rx =
            match cornerRadius with
            | Some r -> " rx=\"" + formatNum r + "\""
            | None -> ""

        "<rect class=\"fuaran-drawing-rect\" x=\""
        + formatNum x
        + "\" y=\""
        + formatNum y
        + "\" width=\""
        + formatNum w
        + "\" height=\""
        + formatNum h
        + "\""
        + rx
        + styleAttrs sources false style
        + "/>"
    | Shape.Line(x1, y1, x2, y2, style) ->
        "<line class=\"fuaran-drawing-line\" x1=\""
        + formatNum x1
        + "\" y1=\""
        + formatNum y1
        + "\" x2=\""
        + formatNum x2
        + "\" y2=\""
        + formatNum y2
        + "\""
        + styleAttrs sources false style
        + "/>"
    | Shape.Polyline(points, style) ->
        "<polyline class=\"fuaran-drawing-polyline\" points=\""
        + pointsAttr points
        + "\""
        + styleAttrs sources true style
        + "/>"
    | Shape.Polygon(points, style) ->
        "<polygon class=\"fuaran-drawing-polygon\" points=\""
        + pointsAttr points
        + "\""
        + styleAttrs sources false style
        + "/>"
    | Shape.Curve(commands, style) ->
        "<path class=\"fuaran-drawing-curve\" d=\""
        + pathD commands
        + "\""
        + styleAttrs sources true style
        + "/>"
    | Shape.Circle(cx, cy, r, style) ->
        "<circle class=\"fuaran-drawing-circle\" cx=\""
        + formatNum cx
        + "\" cy=\""
        + formatNum cy
        + "\" r=\""
        + formatNum r
        + "\""
        + styleAttrs sources false style
        + "/>"
    | Shape.Ellipse(cx, cy, rx, ry, style) ->
        "<ellipse class=\"fuaran-drawing-ellipse\" cx=\""
        + formatNum cx
        + "\" cy=\""
        + formatNum cy
        + "\" rx=\""
        + formatNum rx
        + "\" ry=\""
        + formatNum ry
        + "\""
        + styleAttrs sources false style
        + "/>"
    | Shape.Label(x, y, text, style) ->
        "<text class=\"fuaran-drawing-label\" x=\""
        + formatNum x
        + "\" y=\""
        + formatNum y
        + "\""
        + styleAttrs sources false style
        + ">"
        + escape (textOf text)
        + "</text>"

/// The full canonical inline-SVG string for a `Drawing`. `role="img"` + optional
/// `<title>` / `<desc>` (a11y, R3); `viewBox` from the spec; the root `Style`
/// applies to `<svg>` and is inherited by shapes that omit their own. `textOf`
/// resolves label / title / desc `TextSource`s (each renderer passes its own
/// text resolver, so I18n / bound text resolve exactly as the rest of the tree).
let render (sources: BindingResolver.BindingSources) (textOf: TextSource -> string) (spec: DrawingSpec) : string =
    let vb = spec.ViewBox

    let viewBox =
        formatNum vb.MinX
        + " "
        + formatNum vb.MinY
        + " "
        + formatNum vb.Width
        + " "
        + formatNum vb.Height

    let title =
        match spec.Title with
        | Some t -> "<title>" + escape (textOf t) + "</title>"
        | None -> ""

    let desc =
        match spec.Description with
        | Some d -> "<desc>" + escape (textOf d) + "</desc>"
        | None -> ""

    let body = spec.Shapes |> List.map (shapeSvg sources textOf) |> String.concat ""

    "<svg class=\"fuaran-drawing\" role=\"img\" viewBox=\""
    + viewBox
    + "\""
    + styleAttrs sources false spec.Style
    + ">"
    + title
    + desc
    + body
    + "</svg>"
