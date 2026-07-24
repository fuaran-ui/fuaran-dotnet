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
        Select Case Attr(c, "type", "text").ToLowerInvariant()
            Case "numeric"
                Return Csharp.Column(Of Object).Numeric(label, Function(row) 0.0)
            Case "bool"
                Return Csharp.Column(Of Object).Bool(label, Function(row) False)
            Case "date"
                Return Csharp.Column(Of Object).Date(label, Function(row) New DateTimeOffset())
            Case Else
                Return Csharp.Column(Of Object).Text(label, Function(row) "")
        End Select
    End Function

End Module
