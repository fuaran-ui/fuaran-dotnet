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
                .Source = OptRowSeqBinding(el, "source"),
                .Kind = AsEnum(Of Csharp.ChartKind)(Attr(el, "kind"), Csharp.ChartKind.Line),
                .XField = If(HasAttr(el, "x-field"), Attr(el, "x-field"), ""),
                .YFields = PipeList(Attr(el, "y-fields")),
                .Title = OptText(el, "title"),
                .ValueFormat = ReadValueFormat(el),
                .XTitle = OptText(el, "x-title"),
                .YTitle = OptText(el, "y-title"),
                .Subtitle = OptText(el, "subtitle"),
                .LegendPosition = OptEnum(Of Csharp.ChartLegendPosition)(el, "legend-position"),
                .DataLabels = OptEnum(Of Csharp.ChartDataLabels)(el, "data-labels"),
                .XScale = OptEnum(Of Csharp.ChartXScale)(el, "x-scale"),
                .Annotations = ReadAnnotations(el),
                .Stacked = AttrBool(el, "stacked")})

        d("Table") = Function(el) Csharp.Fuaran.Table(
            New Csharp.TableOptions With {
                .Id = Attr(el, "id"),
                .Headers = ChildTexts(el, "Header"),
                .Rows = ReadRows(el),
                .Sortable = OptBoolAttr(el, "sortable"),
                .DefaultSort = ReadDefaultSort(el)})

        d("Map") = Function(el) Csharp.Fuaran.Map(
            New Csharp.MapOptions With {
                .Id = Attr(el, "id"),
                .Markers = ReadMarkers(el),
                .CentreLatitude = AttrDouble(el, "centre-lat", 0.0),
                .CentreLongitude = AttrDouble(el, "centre-lng", 0.0),
                .Zoom = AttrInt(el, "zoom", 4)})

        ' Phases 861 / 862 / 863, surfaced here by Phase 873. These are DECLARATIONS,
        ' not closures, so unlike the column value accessors below they survive the
        ' XML round-trip intact — the same reason Phase 750's <Tone> children do.
        '
        ' Phase 1473 — `keep-rows-together` / `repeat-header`, the grid's own two
        ' print-break declarations. Same AttrBool route and same absent-is-false
        ' default as <Box>'s pair.
        '
        ' Phase 1125 — `exportable` takes the same route again: the wire omits the
        ' flag at false, so an absent attribute and an explicit "false" are one
        ' declaration, which is what AttrBool already means.
        '
        ' Phase 1123 — `transfer-out-key` / `transfer-in-key`, the two sides of one
        ' shared State key. OptStr rather than AttrBool deliberately: there is no
        ' boolean spelling of "this grid may release rows", because a release with
        ' no key names no counterpart. Two grids naming one key exchange rows; a
        ' grid naming it on one side only is the one-way column.
        '
        ' Phase 1646 — `reorderable` (Phase 934's wire flag) and `row-key-field`
        ' (Phase 425's declarative row identity) were the two attribute-eligible
        ' DataGrid fields this dialect had never spelled; both were declared GAPs
        ' in the authoring-surface pin. `row-key-field` is what makes the transfer
        ' pair above USABLE from here — FUARAN130 asks a transferring grid to name
        ' the field a moved row is identified by, and the pair shipped with no way
        ' to say it.
        d("DataGrid") = Function(el) Csharp.Fuaran.DataGrid(Of Object)(
            New Csharp.DataGridOptions(Of Object) With {
                .Id = Attr(el, "id"),
                .Source = OptObjSeqBinding(el, "source"),
                .ToRow = AddressOf RowCells,
                .Columns = ReadColumns(el),
                .Editable = AttrBool(el, "editable"),
                .SortStateKey = OptStr(el, "sort-state-key"),
                .DefaultSort = ReadDefaultSort(el),
                .PageSize = OptIntAttr(el, "page-size"),
                .PageStateKey = OptStr(el, "page-state-key"),
                .EditStateKey = OptStr(el, "edit-state-key"),
                .KeepRowsTogether = AttrBool(el, "keep-rows-together"),
                .RepeatHeader = AttrBool(el, "repeat-header"),
                .TransferOutKey = OptStr(el, "transfer-out-key"),
                .TransferInKey = OptStr(el, "transfer-in-key"),
                .Exportable = AttrBool(el, "exportable"),
                .Reorderable = AttrBool(el, "reorderable"),
                .RowKeyField = OptStr(el, "row-key-field")})
    End Sub

    ''' fuaran#665 — the required ToRow projection for XML-authored grids, whose row
    ''' type is dynamic Object: a host row that already IS a name→value sequence
    ''' (an F# map, a dictionary) passes through; anything else projects to the
    ''' empty row — the XML tier has no field metadata to do better with, and the
    ''' choice is explicit here rather than silently defaulted in the facade.
    Private Function RowCells(o As Object) As IEnumerable(Of KeyValuePair(Of String, Object))
        Dim cells = TryCast(o, IEnumerable(Of KeyValuePair(Of String, Object)))
        If cells IsNot Nothing Then Return cells
        Return Enumerable.Empty(Of KeyValuePair(Of String, Object))()
    End Function

    Private Function ReadRows(el As XElement) As IEnumerable(Of IEnumerable(Of Csharp.Text))
        Return ChildElements(el, "Row").Select(Function(r) ChildTexts(r, "Cell")).ToList()
    End Function

    ''' <summary>
    ''' Phase 1490 — the chart's data-addressed annotations, authored as
    ''' &lt;ReferenceLine value="0" label="Break-even"/&gt; children.
    ''' </summary>
    ''' <remarks>
    ''' NO children means an ABSENT slot, not an empty list, and the distinction is
    ''' load-bearing rather than pedantic: an absent slot omits the key and produces
    ''' the pre-1490 wire byte for byte, which is what every existing chart must keep
    ''' doing. The XML dialect has no way to spell "an empty list of annotations" and
    ''' does not need one — a chart with no annotations is written by not writing any.
    ''' </remarks>
    ''' <remarks>
    ''' Phase 1491 — &lt;EventMarker category="Q3" label="Repricing"/&gt; and
    ''' &lt;EventMarker date="2026-02-14"/&gt; join it. TWO ATTRIBUTES rather than a
    ''' nested address element, because the XML dialect has no spelling for a union
    ''' and a child of a child would read as a third structural level where there is
    ''' one address; declaring both, or neither, is refused HERE rather than reaching
    ''' the tree, since a silently-dropped marker is the failure this dialect makes
    ''' easiest to write.
    ''' </remarks>
    ''' <remarks>
    ''' Phase 1492 — &lt;RangeBand fromCategory="Q2" toCategory="Q3" label="Freeze"/&gt;
    ''' joins them, in three attribute-pair spellings (see ReadBandRange). It is the
    ''' third and last arm of the union §4l opened; a fourth would be a fourth loop
    ''' here and nothing else, which is what the one-list-over-a-union shape bought.
    ''' </remarks>
    Private Function ReadAnnotations(el As XElement) As IEnumerable(Of Csharp.ChartAnnotation)
        Dim annotations = New List(Of Csharp.ChartAnnotation)

        For Each r In ChildElements(el, "ReferenceLine")
            annotations.Add(Csharp.ChartAnnotation.ReferenceLine(
                AttrDouble(r, "value", 0.0), OptText(r, "label")))
        Next

        For Each m In ChildElements(el, "EventMarker")
            Dim category = Attr(m, "category")
            Dim isoDate = Attr(m, "date")

            If Not String.IsNullOrEmpty(category) AndAlso Not String.IsNullOrEmpty(isoDate) Then
                Throw New ArgumentException(
                    "<EventMarker> carries both 'category' and 'date' — an event marker has ONE x address, in the form its axis uses.")
            ElseIf Not String.IsNullOrEmpty(category) Then
                annotations.Add(Csharp.ChartAnnotation.EventMarker(
                    Csharp.ChartAnnotationX.Category(category), OptText(m, "label")))
            ElseIf Not String.IsNullOrEmpty(isoDate) Then
                annotations.Add(Csharp.ChartAnnotation.EventMarker(
                    Csharp.ChartAnnotationX.Date(isoDate), OptText(m, "label")))
            Else
                Throw New ArgumentException(
                    "<EventMarker> carries neither 'category' nor 'date' — an event marker is drawn AT an address, so it must name one.")
            End If
        Next

        For Each b In ChildElements(el, "RangeBand")
            annotations.Add(Csharp.ChartAnnotation.RangeBand(ReadBandRange(b), OptText(b, "label")))
        Next

        If annotations.Count = 0 Then Return Nothing
        Return annotations
    End Function

    ''' <summary>
    ''' Phase 1492 — a &lt;RangeBand&gt;'s interval, spelled as ONE COMPLETE PAIR of
    ''' attributes: fromValue/toValue, fromCategory/toCategory, or fromDate/toDate.
    ''' </summary>
    ''' <remarks>
    ''' Three pairs rather than a from/to plus an axis attribute, because the axis
    ''' and the address form are one choice in the wire shape and splitting them here
    ''' would let a document say "value axis" and then name two categories — the state
    ''' the union was chosen to make unwritable. Six attributes is the price of that
    ''' in a dialect with no union spelling, and it is the &lt;EventMarker&gt; treatment
    ''' one pair wider.
    '''
    ''' A HALF PAIR IS REFUSED, not completed with a default. AttrDouble takes a
    ''' fallback and a band whose missing end silently read as 0 would draw a region
    ''' reaching the axis origin — a plausible-looking picture nobody described, which
    ''' is exactly the silent failure this dialect makes easiest to write.
    ''' </remarks>
    Private Function ReadBandRange(el As XElement) As Csharp.ChartAnnotationRange
        Dim hasValue = HasAttr(el, "fromValue") OrElse HasAttr(el, "toValue")
        Dim hasCategory = HasAttr(el, "fromCategory") OrElse HasAttr(el, "toCategory")
        Dim hasDate = HasAttr(el, "fromDate") OrElse HasAttr(el, "toDate")

        Dim declared = 0
        If hasValue Then declared += 1
        If hasCategory Then declared += 1
        If hasDate Then declared += 1

        If declared <> 1 Then
            Throw New ArgumentException(
                "<RangeBand> names " & declared.ToString() &
                " address forms — a band spans ONE axis, so it carries exactly one complete pair: fromValue/toValue, fromCategory/toCategory, or fromDate/toDate.")
        End If

        If hasValue Then
            If Not (HasAttr(el, "fromValue") AndAlso HasAttr(el, "toValue")) Then
                Throw New ArgumentException("<RangeBand> carries only one of 'fromValue' and 'toValue' — a band is an interval and needs both ends.")
            End If

            Return Csharp.ChartAnnotationRange.ValueRange(
                AttrDouble(el, "fromValue", 0.0), AttrDouble(el, "toValue", 0.0))
        ElseIf hasCategory Then
            If Not (HasAttr(el, "fromCategory") AndAlso HasAttr(el, "toCategory")) Then
                Throw New ArgumentException("<RangeBand> carries only one of 'fromCategory' and 'toCategory' — a band is an interval and needs both ends.")
            End If

            Return Csharp.ChartAnnotationRange.XRange(
                Csharp.ChartAnnotationX.Category(Attr(el, "fromCategory")),
                Csharp.ChartAnnotationX.Category(Attr(el, "toCategory")))
        Else
            If Not (HasAttr(el, "fromDate") AndAlso HasAttr(el, "toDate")) Then
                Throw New ArgumentException("<RangeBand> carries only one of 'fromDate' and 'toDate' — a band is an interval and needs both ends.")
            End If

            Return Csharp.ChartAnnotationRange.XRange(
                Csharp.ChartAnnotationX.Date(Attr(el, "fromDate")),
                Csharp.ChartAnnotationX.Date(Attr(el, "toDate")))
        End If
    End Function

    Private Function ReadMarkers(el As XElement) As IEnumerable(Of (Latitude As Double, Longitude As Double, Label As String))
        Return ChildElements(el, "Marker").
            Select(Function(m) (AttrDouble(m, "lat", 0.0), AttrDouble(m, "lng", 0.0), Attr(m, "label"))).
            ToList()
    End Function

    Private Function ReadColumns(el As XElement) As IEnumerable(Of Csharp.Column)
        Return ChildElements(el, "Column").Select(AddressOf ReadColumn).ToList()
    End Function

    Private Function ReadColumn(c As XElement) As Csharp.Column
        Dim label = Attr(c, "label")
        Dim col As Csharp.Column
        Select Case Attr(c, "type", "text").ToLowerInvariant()
            Case "numeric"
                col = Csharp.Column.Numeric(label, Function(row) 0.0)
            Case "bool"
                col = Csharp.Column.Bool(label, Function(row) False)
            Case "date"
                col = Csharp.Column.Date(label, Function(row) New DateTimeOffset())
            Case Else
                col = Csharp.Column.Text(label, Function(row) "")
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
            col = col.WithTonedPill(field, map, dflt)
        End If

        ' Phases 861 / 863 — the two per-column NARROWING flags. Absent means
        ' inherit, which is why they read through OptBoolAttr rather than AttrBool:
        ' `False` opts the column out and unstated does not.
        Dim sortable = OptBoolAttr(c, "sortable")
        If sortable.HasValue Then col = col.Sortable(sortable.Value)

        Dim editable = OptBoolAttr(c, "editable")
        If editable.HasValue Then col = col.Editable(editable.Value)

        Return col
    End Function

End Module
