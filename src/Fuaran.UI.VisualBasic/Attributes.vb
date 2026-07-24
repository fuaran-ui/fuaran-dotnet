Imports System.Globalization
Imports System.Linq
Imports System.Xml.Linq
Imports Csharp = Fuaran.UI.CSharp

' Attribute + value reading helpers shared by every element mapping. These define
' the XML authoring conventions:
'
'   * a scalar attribute is a literal; a value prefixed "$" is a bound query
'     (source="$revenue" -> Binding.Query), so authored data flows without a helper;
'   * text slots read a string literal or a "$"-bound query;
'   * the format-* family (format-currency / format-number / format-percent /
'     format-date) maps to the bounded CellFormat vocabulary;
'   * enums parse by name (case-insensitive) to the C# facade enums.
'
' All conversions route through the Wave 45 C# facade value types (Text / Binding /
' CellFormat), so no FSharp.Core type is ever named here.
Friend Module Attributes

    ''' <summary>The raw attribute value, or <paramref name="dflt"/> when absent.</summary>
    Friend Function Attr(el As XElement, name As String, Optional dflt As String = Nothing) As String
        Dim a = el.Attribute(name)
        Return If(a IsNot Nothing, a.Value, dflt)
    End Function

    Friend Function HasAttr(el As XElement, name As String) As Boolean
        Return el.Attribute(name) IsNot Nothing
    End Function

    ''' <summary>A text source — a literal, or a "$name" bound query.</summary>
    Friend Function AsText(value As String) As Csharp.Text
        If value Is Nothing Then Return CType("", Csharp.Text)
        If value.StartsWith("$", StringComparison.Ordinal) Then
            Return CType(Csharp.Binding.Query(Of String)(value.Substring(1)), Csharp.Text)
        End If
        Return CType(value, Csharp.Text)
    End Function

    ''' <summary>An optional text source — Nothing when the attribute is absent.</summary>
    Friend Function OptText(el As XElement, name As String) As Csharp.Text?
        If Not HasAttr(el, name) Then Return Nothing
        Return AsText(Attr(el, name))
    End Function

    ''' <summary>A double binding — a static literal, or a "$name" bound query.</summary>
    Friend Function AsDoubleBinding(value As String) As Csharp.Binding(Of Double)
        If value Is Nothing Then Return CType(0.0, Csharp.Binding(Of Double))
        If value.StartsWith("$", StringComparison.Ordinal) Then
            Return Csharp.Binding.Query(Of Double)(value.Substring(1))
        End If
        Return CType(Double.Parse(value, CultureInfo.InvariantCulture), Csharp.Binding(Of Double))
    End Function

    ''' <summary>A string binding — a static literal, or a "$name" bound query.</summary>
    Friend Function AsStringBinding(value As String) As Csharp.Binding(Of String)
        If value Is Nothing Then Return CType("", Csharp.Binding(Of String))
        If value.StartsWith("$", StringComparison.Ordinal) Then
            Return Csharp.Binding.Query(Of String)(value.Substring(1))
        End If
        Return CType(value, Csharp.Binding(Of String))
    End Function

    ''' <summary>A bool binding — a static literal, or a "$name" bound query.</summary>
    Friend Function AsBoolBinding(value As String) As Csharp.Binding(Of Boolean)
        If value Is Nothing Then Return CType(False, Csharp.Binding(Of Boolean))
        If value.StartsWith("$", StringComparison.Ordinal) Then
            Return Csharp.Binding.Query(Of Boolean)(value.Substring(1))
        End If
        Return CType(ParseBool(value), Csharp.Binding(Of Boolean))
    End Function

    ''' <summary>An int binding — a static literal, or a "$name" bound query.</summary>
    Friend Function AsIntBinding(value As String) As Csharp.Binding(Of Integer)
        If value Is Nothing Then Return CType(0, Csharp.Binding(Of Integer))
        If value.StartsWith("$", StringComparison.Ordinal) Then
            Return Csharp.Binding.Query(Of Integer)(value.Substring(1))
        End If
        Return CType(Integer.Parse(value, CultureInfo.InvariantCulture), Csharp.Binding(Of Integer))
    End Function

    Friend Function ParseBool(value As String) As Boolean
        Return value IsNot Nothing AndAlso
               (value.Equals("true", StringComparison.OrdinalIgnoreCase) OrElse value = "1")
    End Function

    Friend Function AttrBool(el As XElement, name As String, Optional dflt As Boolean = False) As Boolean
        If Not HasAttr(el, name) Then Return dflt
        Return ParseBool(Attr(el, name))
    End Function

    Friend Function AttrInt(el As XElement, name As String, dflt As Integer) As Integer
        Dim v = Attr(el, name)
        If String.IsNullOrEmpty(v) Then Return dflt
        Return Integer.Parse(v, CultureInfo.InvariantCulture)
    End Function

    Friend Function AttrDouble(el As XElement, name As String, dflt As Double) As Double
        Dim v = Attr(el, name)
        If String.IsNullOrEmpty(v) Then Return dflt
        Return Double.Parse(v, CultureInfo.InvariantCulture)
    End Function

    ''' <summary>Parse a facade enum by name (case-insensitive); a bad value is an authoring error.</summary>
    Friend Function AsEnum(Of T As Structure)(value As String, dflt As T) As T
        If String.IsNullOrEmpty(value) Then Return dflt
        Dim result As T = Nothing
        If [Enum].TryParse(Of T)(value, ignoreCase:=True, result) Then Return result
        Throw New FuaranXmlException($"'{value}' is not a valid {GetType(T).Name} value.")
    End Function

    ''' <summary>Read the bounded cell-format from the format-* attribute family.</summary>
    Friend Function ReadFormat(el As XElement) As Csharp.CellFormat
        If HasAttr(el, "format-currency") Then Return Csharp.CellFormat.Currency(Attr(el, "format-currency"))
        If HasAttr(el, "format-number") Then Return Csharp.CellFormat.Number(OptInt(Attr(el, "format-number")))
        If HasAttr(el, "format-percent") Then Return Csharp.CellFormat.Percent(OptInt(Attr(el, "format-percent")))
        If HasAttr(el, "format-date") Then Return Csharp.CellFormat.Date(Attr(el, "format-date"))
        Return Csharp.CellFormat.None
    End Function

    ''' <summary>Parse an optional integer (Nothing when the string is empty).</summary>
    Friend Function OptInt(value As String) As Integer?
        If String.IsNullOrEmpty(value) Then Return Nothing
        Return Integer.Parse(value, CultureInfo.InvariantCulture)
    End Function

    ''' <summary>An optional pixel/int attribute (Nothing when absent).</summary>
    Friend Function OptIntAttr(el As XElement, name As String) As Integer?
        If Not HasAttr(el, name) Then Return Nothing
        Return Integer.Parse(Attr(el, name), CultureInfo.InvariantCulture)
    End Function

    ''' <summary>An optional string attribute (Nothing when absent).</summary>
    Friend Function OptStr(el As XElement, name As String) As String
        If Not HasAttr(el, name) Then Return Nothing
        Return Attr(el, name)
    End Function

    ' Optional bindings — Nothing (null) when the attribute is absent, matching the
    ' C# options records' nullable binding slots.
    Friend Function OptBoolBinding(el As XElement, name As String) As Csharp.Binding(Of Boolean)
        If Not HasAttr(el, name) Then Return Nothing
        Return AsBoolBinding(Attr(el, name))
    End Function

    Friend Function OptIntBinding(el As XElement, name As String) As Csharp.Binding(Of Integer)
        If Not HasAttr(el, name) Then Return Nothing
        Return AsIntBinding(Attr(el, name))
    End Function

    Friend Function OptDoubleBinding(el As XElement, name As String) As Csharp.Binding(Of Double)
        If Not HasAttr(el, name) Then Return Nothing
        Return AsDoubleBinding(Attr(el, name))
    End Function

    ''' <summary>Split a pipe-separated attribute (e.g. y-fields="a|b") into its parts.</summary>
    Friend Function PipeList(value As String) As IEnumerable(Of String)
        If String.IsNullOrEmpty(value) Then Return Array.Empty(Of String)()
        Return value.Split("|"c)
    End Function

    ''' <summary>The inner-text of a child element by local name (e.g. &lt;Item&gt;text&lt;/Item&gt;).</summary>
    Friend Function ChildTexts(el As XElement, childName As String) As IEnumerable(Of Csharp.Text)
        Return el.Elements().
            Where(Function(c) c.Name.LocalName = childName).
            Select(Function(c) AsText(c.Value)).
            ToList()
    End Function

    Friend Function ChildElements(el As XElement, childName As String) As IEnumerable(Of XElement)
        Return el.Elements().Where(Function(c) c.Name.LocalName = childName)
    End Function

    ''' <summary>An optional obj-seq binding for data-bearing sources — only "$name" is meaningful.</summary>
    Friend Function OptObjSeqBinding(el As XElement, name As String) As Csharp.Binding(Of IEnumerable(Of Object))
        Dim v = Attr(el, name)
        If v Is Nothing OrElse Not v.StartsWith("$", StringComparison.Ordinal) Then Return Nothing
        Return Csharp.Binding.Query(Of IEnumerable(Of Object))(v.Substring(1))
    End Function

    ''' <summary>An optional double-seq binding (sparkline source) — only "$name" is meaningful.</summary>
    Friend Function OptDoubleSeqBinding(el As XElement, name As String) As Csharp.Binding(Of IEnumerable(Of Double))
        Dim v = Attr(el, name)
        If v Is Nothing OrElse Not v.StartsWith("$", StringComparison.Ordinal) Then Return Nothing
        Return Csharp.Binding.Query(Of IEnumerable(Of Double))(v.Substring(1))
    End Function

    ''' <summary>Read &lt;Option value=".." label=".."/&gt; children as (value, label) pairs.</summary>
    Friend Function ReadOptions(el As XElement) As IEnumerable(Of (Value As String, Label As String))
        Return ChildElements(el, "Option").
            Select(Function(o) (Attr(o, "value"), Attr(o, "label"))).
            ToList()
    End Function

End Module
