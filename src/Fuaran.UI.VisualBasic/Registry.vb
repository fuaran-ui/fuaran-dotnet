Imports System.Xml.Linq
Imports Csharp = Fuaran.UI.CSharp

' Phase 310 foundation vocabulary — a representative element per category (Layout /
' Display / Input) proving the XElement -> factory -> Node(Of Object) path + the
' attribute conventions. Phase 311 registers the remaining ~40 kinds under Mapping/.
'
' Each builder reads the element's attributes into a C# options record and calls the
' Wave 45 factory, so the node inherits ARIA-by-construction + the shared encoder.
Friend Module CoreMapping

    Friend Sub Register(d As Dictionary(Of String, Func(Of XElement, Csharp.FuaranNode)))

        ' ── Layout ────────────────────────────────────────────────────────────
        d("Dashboard") = Function(el) Csharp.Fuaran.Dashboard(
            New Csharp.DashboardOptions With {.Id = Attr(el, "id"), .Children = Kids(el)})

        d("Stack") = Function(el) Csharp.Fuaran.Stack(
            New Csharp.StackOptions With {
                .Id = Attr(el, "id"),
                .Orientation = AsEnum(Of Csharp.Orientation)(Attr(el, "orientation"), Csharp.Orientation.Vertical),
                .Wrap = AttrBool(el, "wrap"),
                .Children = Kids(el)})

        d("Grid") = Function(el) Csharp.Fuaran.Grid(
            New Csharp.GridOptions With {.Id = Attr(el, "id"), .Cols = AttrInt(el, "cols", 12), .Children = Kids(el)})

        d("Card") = Function(el) Csharp.Fuaran.Card(
            New Csharp.CardOptions With {.Id = Attr(el, "id"), .Heading = OptText(el, "heading"), .Children = Kids(el)})

        ' Phase 390 — the unified container. <Box layout="flex|grid|auto"
        ' role="group|card|dashboard|separator" .../>. Dashboard/Stack/Grid/Card
        ' elements above remain as Box-emitting conveniences.
        d("Box") = Function(el) Csharp.Fuaran.Box(
            New Csharp.BoxOptions With {
                .Id = Attr(el, "id"),
                .Layout = AsEnum(Of Csharp.BoxLayoutMode)(Attr(el, "layout"), Csharp.BoxLayoutMode.Flex),
                .Role = AsEnum(Of Csharp.BoxRoleKind)(Attr(el, "role"), Csharp.BoxRoleKind.Group),
                .Orientation = AsEnum(Of Csharp.Orientation)(Attr(el, "orientation"), Csharp.Orientation.Vertical),
                .Wrap = AttrBool(el, "wrap"),
                .Cols = AttrInt(el, "cols", 12),
                .Heading = OptText(el, "heading"),
                .Children = Kids(el)})

        ' ── Display ───────────────────────────────────────────────────────────
        ' Phase 867's `trend-polarity`, surfaced here by Phase 873 — together with
        ' `trend`, which this tier had never carried. Polarity alone would have been a
        ' fake affordance: it is a statement ABOUT a trend, so on a tile with no trend
        ' it declares a sentiment for a quantity the tile never shows.
        d("Metric") = Function(el) Csharp.Fuaran.Metric(
            New Csharp.MetricOptions With {
                .Id = Attr(el, "id"),
                .Label = AsText(Attr(el, "label")),
                .Value = AsDoubleBinding(Attr(el, "value")),
                .Format = ReadFormat(el),
                .Tone = AsEnum(Of Csharp.Tone)(Attr(el, "tone"), Csharp.Tone.Default),
                .Trend = OptDoubleBinding(el, "trend"),
                .TrendPolarity = AsEnum(Of Csharp.TrendPolarity)(
                    Attr(el, "trend-polarity"), Csharp.TrendPolarity.HigherIsBetter)})

        d("Heading") = Function(el) Csharp.Fuaran.Heading(
            New Csharp.HeadingOptions With {
                .Id = Attr(el, "id"),
                .Text = AsText(Attr(el, "text")),
                .Level = AttrInt(el, "level", 2),
                .Variant = AsEnum(Of Csharp.HeadingVariant)(Attr(el, "variant"), Csharp.HeadingVariant.Standard)})

        d("Markdown") = Function(el) Csharp.Fuaran.Markdown(
            New Csharp.MarkdownOptions With {.Id = Attr(el, "id"), .Text = AsText(MarkdownText(el))})

        ' ── Input ─────────────────────────────────────────────────────────────
        d("Button") = Function(el) Csharp.Fuaran.Button(
            New Csharp.ButtonOptions With {
                .Id = Attr(el, "id"),
                .Label = AsText(Attr(el, "label")),
                .Variant = AsEnum(Of Csharp.ButtonVariant)(Attr(el, "variant"), Csharp.ButtonVariant.Secondary)})
    End Sub

    ''' <summary>Markdown source is either a `text` attribute or the element's inner text.</summary>
    Private Function MarkdownText(el As XElement) As String
        If HasAttr(el, "text") Then Return Attr(el, "text")
        Return el.Value
    End Function

End Module
