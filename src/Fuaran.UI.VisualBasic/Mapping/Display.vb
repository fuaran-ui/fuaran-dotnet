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
                .Expandable = AttrBool(el, "expandable"),
                .SrcSet = ChildElements(el, "Source").
                    Select(Function(c) New Csharp.SrcSetEntry(AsStringBinding(Attr(c, "src")), AttrInt(c, "width", 0))).
                    ToList()})

        ' Phase 1076 — ONE element for one wire kind, with `kind` selecting the
        ' variant, mirroring the wire's shape rather than inventing two elements
        ' the codec has no cases for. `controls` defaults TRUE here as it does
        ' everywhere: the accessible value is what an author gets for free.
        '
        ' `autoplay` and `poster` are read only on the Video branch. An author
        ' who writes `kind="audio" autoplay="true"` gets an audio element that
        ' does not autoplay, which is the same answer the decoder gives the same
        ' document — the slot does not exist on that case, so the value has
        ' nowhere to land.
        d("Media") = Function(el) If(
            String.Equals(Attr(el, "kind", "video"), "audio", StringComparison.OrdinalIgnoreCase),
            Csharp.Fuaran.Audio(
                New Csharp.AudioOptions With {
                    .Id = Attr(el, "id"),
                    .Src = AsStringBinding(Attr(el, "src")),
                    .Label = AsText(Attr(el, "label")),
                    .Controls = AttrBool(el, "controls", True),
                    .Loop = AttrBool(el, "loop"),
                    .Transcript = OptText(el, "transcript"),
                    .Tracks = MediaTracks(el)}),
            Csharp.Fuaran.Video(
                New Csharp.VideoOptions With {
                    .Id = Attr(el, "id"),
                    .Src = AsStringBinding(Attr(el, "src")),
                    .Label = AsText(Attr(el, "label")),
                    .Controls = AttrBool(el, "controls", True),
                    .Loop = AttrBool(el, "loop"),
                    .Autoplay = AttrBool(el, "autoplay"),
                    .Poster = If(HasAttr(el, "poster"), AsStringBinding(Attr(el, "poster")), Nothing),
                    .Transcript = OptText(el, "transcript"),
                    .Tracks = MediaTracks(el)}))

        ' Phase 1111 - the sandboxed third-party embed. `permissions` is a
        ' pipe-separated list of relaxation names, the <Mount> `capabilities`
        ' shape: a list of bare tokens does not earn a child element. Absent, the
        ' frame is fully sandboxed, which is the whole design of the slot - an
        ' author who writes no `permissions` gets total denial rather than a
        ' default somebody chose.
        d("Embed") = Function(el) Csharp.Fuaran.Embed(
            New Csharp.EmbedOptions With {
                .Id = Attr(el, "id"),
                .Src = AsStringBinding(Attr(el, "src")),
                .Title = AsText(Attr(el, "title")),
                .AspectRatio = AsEnum(Of Csharp.ImageAspect)(Attr(el, "aspect-ratio"), Csharp.ImageAspect.Natural),
                .Permissions = EmbedPermissions(el)})

        ' Phase 1120 - the tree. Its rows are <TreeItem> children, RECURSIVELY:
        ' the XML dialect nests natively, so a hierarchy is authored as a
        ' hierarchy and needs no flattening convention. A distinct element name
        ' rather than an overloaded <Item>, because the attribute table is keyed
        ' by element name and is global - <Item> carries TEXT inside a <List> and
        ' would silently accept `id`/`label` there. `expanded-state-key` and
        ' `selection-state-key` are the two affordances; omit both and the tree
        ' renders fully expanded and static.
        d("Tree") = Function(el) Csharp.Fuaran.Tree(
            New Csharp.TreeOptions With {
                .Id = Attr(el, "id"),
                .Items = TreeItems(el),
                .ExpandedStateKey = Attr(el, "expanded-state-key"),
                .SelectionStateKey = Attr(el, "selection-state-key")})

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

    ''' <summary>Phase 1110 — the &lt;Track&gt; children of a &lt;Media&gt;, in AUTHORED
    ''' order. Unlike an &lt;Image&gt;'s &lt;Source&gt; children, whose order the renderers
    ''' normalise, the order written here is the order a reader sees in the user
    ''' agent's track menu, so the translator preserves it exactly.
    '''
    ''' &lt;c&gt;srclang&lt;/c&gt; is spelled all-lowercase, matching the HTML attribute rather
    ''' than the wire's camelCase &lt;c&gt;srcLang&lt;/c&gt;: this dialect is authored by people
    ''' who know the element, and the analyzer's attribute table carries the same
    ''' spelling.&lt;/summary&gt;
    ''' <summary>Phase 1111 - the pipe-separated permissions attribute of an
    ''' &lt;Embed&gt;, lowered onto the closed enum.
    '''
    ''' Blank tokens are dropped BEFORE the enum parse rather than after, and the
    ''' ordering is load-bearing rather than tidy: AsEnum returns its DEFAULT on an
    ''' empty string, so a stray double separator would otherwise GRANT a permission
    ''' the author never wrote. With the filter first the default is unreachable and
    ''' every surviving token either names a relaxation or throws.</summary>
    Private Function EmbedPermissions(el As XElement) As IReadOnlyList(Of Csharp.EmbedPermission)
        Return PipeList(Attr(el, "permissions")).
            Select(Function(p) p.Trim()).
            Where(Function(p) p.Length > 0).
            Select(Function(p) AsEnum(Of Csharp.EmbedPermission)(p, Csharp.EmbedPermission.AllowScripts)).
            ToList()
    End Function

    ''' <summary>Phase 1120 - the &lt;TreeItem&gt; children of a &lt;Tree&gt; (or of
    ''' another &lt;TreeItem&gt;), lowered recursively onto the row options.
    '''
    ''' The recursion is the whole point of using this dialect for a tree: the
    ''' authored XML has the shape of the thing it describes, so there is no
    ''' parent-id convention to get wrong and no flat list to reassemble.</summary>
    Private Function TreeItems(el As XElement) As IReadOnlyList(Of Csharp.TreeItemOptions)
        Return el.Elements().
            Where(Function(c) c.Name.LocalName = "TreeItem").
            Select(Function(c) New Csharp.TreeItemOptions With {
                .Id = Attr(c, "id"),
                .Label = AsText(Attr(c, "label")),
                .Icon = Attr(c, "icon"),
                .Children = TreeItems(c)}).
            ToList()
    End Function

    Private Function MediaTracks(el As XElement) As IReadOnlyList(Of Csharp.TrackEntry)
        Return ChildElements(el, "Track").
            Select(Function(c) New Csharp.TrackEntry(
                AsEnum(Of Csharp.TrackKind)(Attr(c, "kind"), Csharp.TrackKind.Captions),
                AsStringBinding(Attr(c, "src")),
                Attr(c, "srclang"),
                AsText(Attr(c, "label")),
                AttrBool(c, "default"))).
            ToList()
    End Function

End Module
