Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.Xml.Linq
Imports Csharp = Fuaran.UI.CSharp

' Phase 311 — the Visualisation kinds. The data grid is authored with <Column> child
' elements; its row type is `Object` (XML-authored rows are dynamic), and the column
' value accessors are wire-opaque closures (the field= attribute is runtime metadata,
' not on the wire), so a placeholder accessor is byte-faithful.
Friend Module VisualisationMapping

    Friend Sub Register(d As Dictionary(Of String, Func(Of XElement, Csharp.FuaranNode)))

        d("Chart") = Function(el) Csharp.Fuaran.Chart(
            New Csharp.ChartOptions With {
                .Id = Attr(el, "id"),
                .Source = OptObjSeqBinding(el, "source"),
                .Kind = AsEnum(Of Csharp.ChartKind)(Attr(el, "kind"), Csharp.ChartKind.Line),
                .XField = If(HasAttr(el, "x-field"), Attr(el, "x-field"), ""),
                .YFields = PipeList(Attr(el, "y-fields")),
                .Title = OptText(el, "title"),
                .Stacked = AttrBool(el, "stacked")})

        d("Table") = Function(el) Csharp.Fuaran.Table(
            New Csharp.TableOptions With {
                .Id = Attr(el, "id"),
                .Headers = ChildTexts(el, "Header"),
                .Rows = ReadRows(el)})

        d("Map") = Function(el) Csharp.Fuaran.Map(
            New Csharp.MapOptions With {
                .Id = Attr(el, "id"),
                .Markers = ReadMarkers(el),
                .CentreLatitude = AttrDouble(el, "centre-lat", 0.0),
                .CentreLongitude = AttrDouble(el, "centre-lng", 0.0),
                .Zoom = AttrInt(el, "zoom", 4)})

        d("DataGrid") = Function(el) Csharp.Fuaran.DataGrid(Of Object)(
            New Csharp.DataGridOptions(Of Object) With {
                .Id = Attr(el, "id"),
                .Source = OptObjSeqBinding(el, "source"),
                .Columns = ReadColumns(el),
                .Editable = AttrBool(el, "editable")})
    End Sub

    Private Function ReadRows(el As XElement) As IEnumerable(Of IEnumerable(Of Csharp.Text))
        Return ChildElements(el, "Row").Select(Function(r) ChildTexts(r, "Cell")).ToList()
    End Function

    Private Function ReadMarkers(el As XElement) As IEnumerable(Of (Latitude As Double, Longitude As Double, Label As String))
        Return ChildElements(el, "Marker").
            Select(Function(m) (AttrDouble(m, "lat", 0.0), AttrDouble(m, "lng", 0.0), Attr(m, "label"))).
            ToList()
    End Function

    Private Function ReadColumns(el As XElement) As IEnumerable(Of Csharp.Column(Of Object))
        Return ChildElements(el, "Column").Select(AddressOf ReadColumn).ToList()
    End Function

    Private Function ReadColumn(c As XElement) As Csharp.Column(Of Object)
        Dim label = Attr(c, "label")
        Dim col As Csharp.Column(Of Object)
        Select Case Attr(c, "type", "text").ToLowerInvariant()
            Case "numeric"
                col = Csharp.Column(Of Object).Numeric(label, Function(row) 0.0)
            Case "bool"
                col = Csharp.Column(Of Object).Bool(label, Function(row) False)
            Case "date"
                col = Csharp.Column(Of Object).Date(label, Function(row) New DateTimeOffset())
            Case Else
                col = Csharp.Column(Of Object).Text(label, Function(row) "")
        End Select

        ' Phase 750 — a column carrying <Tone> children becomes a declarative TonedPill.
        ' The children ARE the wire's value→tone map, so unlike every other cell kind
        ' this one loses nothing in the XML round-trip: the tone rule is data, not a
        ' closure the mapping would have to stub out.
        Dim tones = ChildElements(c, "Tone").ToList()
        If tones.Count > 0 Then
            Dim map = New Dictionary(Of String, Csharp.Tone)()
            For Each t In tones
                map(Attr(t, "value")) = AsEnum(Of Csharp.Tone)(Attr(t, "tone"), Csharp.Tone.Default)
            Next

            ' `field` defaults to the column's own field — the overwhelmingly common case
            ' is "tone this column by its own value"; `tone-field` overrides for the rarer
            ' "tone this column by a DIFFERENT row property".
            Dim field = If(HasAttr(c, "tone-field"), Attr(c, "tone-field"), Attr(c, "field"))
            Dim dflt = AsEnum(Of Csharp.Tone)(Attr(c, "default-tone"), Csharp.Tone.Default)
            Return col.WithTonedPill(field, map, dflt)
        End If

        Return col
    End Function

End Module
