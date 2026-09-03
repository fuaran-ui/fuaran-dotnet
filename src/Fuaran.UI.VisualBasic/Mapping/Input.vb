Imports System.Linq
Imports System.Xml.Linq
Imports Csharp = Fuaran.UI.CSharp

' Phase 311 — the Input kinds (Button ships in the foundation). Form fields, filter
' chips, and select options are authored as child elements.
Friend Module InputMapping

    Friend Sub Register(d As Dictionary(Of String, Func(Of XElement, Csharp.FuaranNode)))

        d("Form") = Function(el) Csharp.Fuaran.Form(
            New Csharp.FormOptions With {
                .Id = Attr(el, "id"),
                .Fields = ReadFields(el),
                .SubmitLabel = If(HasAttr(el, "submit-label"), AsText(Attr(el, "submit-label")), CType("Submit", Csharp.Text))})

        d("Select") = Function(el) Csharp.Fuaran.Select(
            New Csharp.SelectOptions With {
                .Id = Attr(el, "id"),
                .Label = AsText(Attr(el, "label")),
                .Options = ReadOptions(el),
                .Value = OptStr(el, "value"),
                .Placeholder = OptText(el, "placeholder")})

        d("MultiSelect") = Function(el) Csharp.Fuaran.MultiSelect(
            New Csharp.MultiSelectOptions With {
                .Id = Attr(el, "id"),
                .Label = AsText(Attr(el, "label")),
                .Options = ReadOptions(el),
                .Values = PipeList(Attr(el, "values"))})

        d("Filters") = Function(el) Csharp.Fuaran.Filters(
            New Csharp.FiltersOptions With {.Id = Attr(el, "id"), .Filters = ReadFilters(el)})

        d("FileUpload") = Function(el) Csharp.Fuaran.FileUpload(
            New Csharp.FileUploadOptions With {
                .Id = Attr(el, "id"),
                .Label = AsText(Attr(el, "label")),
                .Accept = PipeList(Attr(el, "accept")),
                .Multiple = AttrBool(el, "multiple")})
    End Sub

    Private Function ReadFields(el As XElement) As IEnumerable(Of Csharp.FormField)
        Return ChildElements(el, "Field").Select(AddressOf ReadField).ToList()
    End Function

    ''' Phase 864's declared field rule, surfaced here by Phase 873. The rule is the
    ''' language's answer to restating the constraint as help text — which is what
    ''' every sighted emission did — so an XML author gets the same slot the wire has.
    Private Function ReadField(f As XElement) As Csharp.FormField
        Dim id = Attr(f, "id")
        Dim label = AsText(Attr(f, "label"))
        Dim required = AttrBool(f, "required")
        Dim help = OptText(f, "help")
        Dim rule = ReadFieldRule(f)
        Select Case Attr(f, "kind", "text").ToLowerInvariant()
            Case "number"
                Return Csharp.FormField.Number(id, label, AttrDouble(f, "initial", 0.0), required, help, rule)
            Case "checkbox"
                Return Csharp.FormField.Checkbox(id, label, AttrBool(f, "initial"), required, help, rule)
            Case "textarea"
                Return Csharp.FormField.TextArea(id, label, AttrInt(f, "rows", 4), OptStr(f, "initial"), required, help, rule)
            Case "choice"
                Return Csharp.FormField.Choice(id, label, OptStr(f, "selected"), ReadOptions(f), required, help, rule)
            ' Phase 1113 — the typeahead field. `allowFreeText` defaults to False, so
            ' the shortest spelling is the constrained one, exactly as on the wire.
            Case "combobox"
                Return Csharp.FormField.Combobox(id, label, OptStr(f, "selected"), ReadOptions(f), AttrBool(f, "allowFreeText"), required, help, rule)
            Case Else
                Return Csharp.FormField.Text(id, label, If(HasAttr(f, "initial"), Attr(f, "initial"), ""), required, help, rule)
        End Select
    End Function

    Private Function ReadFilters(el As XElement) As IEnumerable(Of Csharp.Filter)
        Return ChildElements(el, "Filter").Select(AddressOf ReadFilter).ToList()
    End Function

    Private Function ReadFilter(f As XElement) As Csharp.Filter
        Dim name = Attr(f, "name")
        Dim label = AsText(Attr(f, "label"))
        Select Case Attr(f, "kind", "text").ToLowerInvariant()
            Case "choice"
                Return Csharp.Filter.Choice(name, label, ReadOptions(f))
            Case "combobox"
                Return Csharp.Filter.Combobox(name, label, ReadOptions(f), AttrBool(f, "allowFreeText"))
            Case Else
                Return Csharp.Filter.Text(name, label)
        End Select
    End Function

End Module
