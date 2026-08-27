Imports System.Xml.Linq
Imports Csharp = Fuaran.UI.CSharp

' Phase 311 — the remaining Display kinds (Heading/Markdown/Metric ship in the
' foundation).
Friend Module DisplayMapping

    Friend Sub Register(d As Dictionary(Of String, Func(Of XElement, Csharp.FuaranNode)))

        d("Badge") = Function(el) Csharp.Fuaran.Badge(
            New Csharp.BadgeOptions With {
                .Id = Attr(el, "id"),
                .Label = AsText(Attr(el, "label")),
                .Variant = AsEnum(Of Csharp.BadgeVariant)(Attr(el, "variant"), Csharp.BadgeVariant.Neutral)})

        d("Sparkline") = Function(el) Csharp.Fuaran.Sparkline(
            New Csharp.SparklineOptions With {.Id = Attr(el, "id"), .Source = OptDoubleSeqBinding(el, "source")})

        ' Phase 459 — Spacer retired; spacing is a Box layout `gap` attribute, not a node.

        d("Callout") = Function(el) Csharp.Fuaran.Callout(
            New Csharp.CalloutOptions With {
                .Id = Attr(el, "id"),
                .Body = AsText(Attr(el, "body")),
                .Heading = OptText(el, "heading"),
                .Tone = AsEnum(Of Csharp.Tone)(Attr(el, "tone"), Csharp.Tone.Info),
                .Icon = OptStr(el, "icon"),
                .Dismissable = AttrBool(el, "dismissable")})

        d("Progress") = Function(el) Csharp.Fuaran.Progress(
            New Csharp.ProgressOptions With {
                .Id = Attr(el, "id"),
                .Fraction = OptDoubleBinding(el, "fraction"),
                .Label = OptText(el, "label"),
                .Caveat = OptText(el, "caveat"),
                .Indeterminate = AttrBool(el, "indeterminate"),
                .Tone = AsEnum(Of Csharp.Tone)(Attr(el, "tone"), Csharp.Tone.Default)})

        d("Skeleton") = Function(el) Csharp.Fuaran.Skeleton(
            New Csharp.SkeletonOptions With {.Id = Attr(el, "id"), .Rows = AttrInt(el, "rows", 3)})

        d("Icon") = Function(el) Csharp.Fuaran.Icon(
            New Csharp.IconOptions With {
                .Id = Attr(el, "id"),
                .Icon = Attr(el, "icon"),
                .Size = AsEnum(Of Csharp.IconSize)(Attr(el, "size"), Csharp.IconSize.Medium),
                .Tone = AsEnum(Of Csharp.Tone)(Attr(el, "tone"), Csharp.Tone.Default),
                .Label = Attr(el, "label")})

        d("LabelValueRow") = Function(el) Csharp.Fuaran.LabelValueRow(
            New Csharp.LabelValueRowOptions With {
                .Id = Attr(el, "id"),
                .Label = AsText(Attr(el, "label")),
                .Value = AsDoubleBinding(Attr(el, "value")),
                .Format = ReadFormat(el),
                .Emphasis = AttrBool(el, "emphasis"),
                .Help = OptText(el, "help")})

        ' 0.2.0 wave - the labeled TEXT fact tile (Metric's complement).
        d("Fact") = Function(el) Csharp.Fuaran.Fact(
            New Csharp.FactOptions With {
                .Id = Attr(el, "id"),
                .Label = AsText(Attr(el, "label")),
                .Value = AsText(Attr(el, "value")),
                .Icon = OptStr(el, "icon"),
                .Tone = AsEnum(Of Csharp.Tone)(Attr(el, "tone"), Csharp.Tone.Default),
                .Emphasis = AttrBool(el, "emphasis"),
                .Help = OptText(el, "help")})

        d("Link") = Function(el) Csharp.Fuaran.Link(
            New Csharp.LinkOptions With {
                .Id = Attr(el, "id"),
                .Href = AsStringBinding(Attr(el, "href")),
                .Label = AsText(Attr(el, "label")),
                .Rel = OptStr(el, "rel"),
                .Target = OptStr(el, "target"),
                .Download = AttrBool(el, "download")})

        d("Image") = Function(el) Csharp.Fuaran.Image(
            New Csharp.ImageOptions With {
                .Id = Attr(el, "id"),
                .Src = AsStringBinding(Attr(el, "src")),
                .Alt = AsText(Attr(el, "alt")),
                .Variant = AsEnum(Of Csharp.ImageVariant)(Attr(el, "variant"), Csharp.ImageVariant.Default),
                .Fit = AsEnum(Of Csharp.ImageFit)(Attr(el, "fit"), Csharp.ImageFit.Natural),
                .AspectRatio = AsEnum(Of Csharp.ImageAspect)(Attr(el, "aspect-ratio"), Csharp.ImageAspect.Natural),
                .Loading = AsEnum(Of Csharp.ImageLoading)(Attr(el, "loading"), Csharp.ImageLoading.Eager),
                .Caption = OptText(el, "caption"),
                .SrcSet = ChildElements(el, "Source").
                    Select(Function(c) New Csharp.SrcSetEntry(AsStringBinding(Attr(c, "src")), AttrInt(c, "width", 0))).
                    ToList()})

        d("List") = Function(el) Csharp.Fuaran.List(
            New Csharp.ListOptions With {.Id = Attr(el, "id"), .Items = ChildTexts(el, "Item"), .Ordered = AttrBool(el, "ordered")})

        d("Divider") = Function(el) Csharp.Fuaran.Divider(
            New Csharp.DividerOptions With {
                .Id = Attr(el, "id"),
                .Orientation = AsEnum(Of Csharp.Orientation)(Attr(el, "orientation"), Csharp.Orientation.Horizontal),
                .Label = OptText(el, "label")})

        d("Toast") = Function(el) Csharp.Fuaran.Toast(
            New Csharp.ToastOptions With {
                .Id = Attr(el, "id"),
                .Message = AsText(Attr(el, "message")),
                .Tone = AsEnum(Of Csharp.Tone)(Attr(el, "tone"), Csharp.Tone.Info),
                .Open = OptBoolBinding(el, "open"),
                .Dismissable = AttrBool(el, "dismissable", True)})

        d("CodeBlock") = Function(el) Csharp.Fuaran.CodeBlock(
            New Csharp.CodeBlockOptions With {
                .Id = Attr(el, "id"),
                .Code = If(HasAttr(el, "code"), Attr(el, "code"), el.Value),
                .Language = If(HasAttr(el, "language"), Attr(el, "language"), "text"),
                .LineNumbers = AttrBool(el, "line-numbers"),
                .Copyable = AttrBool(el, "copyable", True)})

        d("Math") = Function(el) Csharp.Fuaran.Math(
            New Csharp.MathOptions With {
                .Id = Attr(el, "id"),
                .Source = If(HasAttr(el, "source"), Attr(el, "source"), el.Value),
                .Display = AsEnum(Of Csharp.MathDisplay)(Attr(el, "display"), Csharp.MathDisplay.Block)})

        ' Phase 524 — Drawing. The closed Shape DU is not expressed via flat XML
        ' attributes at this veneer tier; the element authors the viewBox + an
        ' empty draw list (the F# smart ctor / C# DrawingOptions carry shapes).
        d("Drawing") = Function(el) Csharp.Fuaran.Drawing(
            New Csharp.DrawingOptions With {
                .Id = Attr(el, "id"),
                .MinX = AttrDouble(el, "min-x", 0.0),
                .MinY = AttrDouble(el, "min-y", 0.0),
                .Width = AttrDouble(el, "width", 100.0),
                .Height = AttrDouble(el, "height", 100.0),
                .Title = OptText(el, "title"),
                .Description = OptText(el, "description")})
    End Sub

End Module
