Imports System.Linq
Imports System.Xml.Linq
Imports Csharp = Fuaran.UI.CSharp

''' <summary>Raised when an XML-literal tree cannot be translated (unknown element,
''' invalid attribute value, …). A runtime authoring error — the Phase 315 analyzer
''' lifts these to compile time.</summary>
Public NotInheritable Class FuaranXmlException
    Inherits Exception

    Public Sub New(message As String)
        MyBase.New(message)
    End Sub
End Class

''' <summary>
''' The VB XML-literal authoring surface. Author a Fuaran UI tree with first-class
''' XML literals and translate it to a wire-faithful node:
''' <code>
''' Dim tree = &lt;Card id="c" heading="Insights"&gt;
'''                &lt;Metric id="rev" label="Revenue" value="1234.5" format-currency="GBP" tone="Brand"/&gt;
'''            &lt;/Card&gt;
''' Dim json = FuaranXml.Encode(tree)
''' </code>
''' The translator drives the shared C# factory surface, so the tree inherits
''' per-component ARIA and encodes through the same F# codec every host uses — no VB
''' codec, wire-faithful by construction.
''' </summary>
Public Module FuaranXml

    Private ReadOnly Builders As Dictionary(Of String, Func(Of XElement, Csharp.FuaranNode)) = BuildRegistry()

    Private Function BuildRegistry() As Dictionary(Of String, Func(Of XElement, Csharp.FuaranNode))
        Dim d As New Dictionary(Of String, Func(Of XElement, Csharp.FuaranNode))(StringComparer.Ordinal)
        CoreMapping.Register(d)
        ' Phase 311 — the remaining vocabulary (Mapping/*.vb).
        LayoutMapping.Register(d)
        DisplayMapping.Register(d)
        InputMapping.Register(d)
        VisualisationMapping.Register(d)
        StructuralMapping.Register(d)
        Return d
    End Function

    ''' <summary>The set of recognised element names (the vocabulary — one source of
    ''' truth for the completeness guard and the analyzer).</summary>
    Public Function KnownElements() As IReadOnlyCollection(Of String)
        Return Builders.Keys.OrderBy(Function(k) k, StringComparer.Ordinal).ToList()
    End Function

    ''' <summary>Whether <paramref name="elementName"/> maps to a Fuaran node kind.</summary>
    Public Function IsKnownElement(elementName As String) As Boolean
        Return Builders.ContainsKey(elementName)
    End Function

    ' ── The binding spellings (Phase 1154) ──────────────────────────────────────
    '
    ' The dialect has exactly two, and they differ in DIRECTION, which is why one
    ' prefix could never have served both:
    '
    '   value="$revenue"           a host-fed QUERY binding — read-only;
    '   open="$state.panelOpen"    a writable STATE slot — the control write-back
    '                              default writes the changed value straight to it.
    '
    ' The `$state.<key>` spelling is not invented here: it is how the wire format,
    ' the authoring guide and the dead-on-decode lint have named a State slot since
    ' the write-back default shipped. The dialect adopts that name rather than
    ' minting a second one.
    '
    ' These two functions are Public for the same reason `KnownElements` is: the
    ' Roslyn analyzer cannot load this assembly (netstandard2.0 against net10) and
    ' so carries its own copy, pinned against these by a test.

    ''' <summary>The value prefix that spells a writable state binding.</summary>
    Public Const StateBindingPrefix As String = "$state"

    ''' <summary>Whether <paramref name="value"/> uses the state spelling at all — True
    ''' for a well-formed <c>"$state.&lt;key&gt;"</c> AND for a malformed <c>"$state"</c> /
    ''' <c>"$state."</c>, False for everything else. A query whose name merely starts with
    ''' the same letters (<c>"$stateful"</c>) is NOT the state spelling and keeps its
    ''' meaning.</summary>
    Public Function IsStateBinding(value As String) As Boolean
        If value Is Nothing Then Return False
        Return String.Equals(value, StateBindingPrefix, StringComparison.Ordinal) OrElse
               value.StartsWith(StateBindingPrefix & ".", StringComparison.Ordinal)
    End Function

    ''' <summary>The state key inside a <c>"$state.&lt;key&gt;"</c> value. Nothing when the
    ''' value is not the state spelling, and Nothing when it is a malformed one (the prefix
    ''' with no key) — so a caller that has already established <see cref="IsStateBinding"/>
    ''' reads Nothing as "malformed".</summary>
    Public Function StateBindingKey(value As String) As String
        If Not IsStateBinding(value) Then Return Nothing
        If value.Length <= StateBindingPrefix.Length + 1 Then Return Nothing
        Return value.Substring(StateBindingPrefix.Length + 1)
    End Function

    ''' <summary>Translate an XML-literal element into a wire-faithful node handle.</summary>
    Public Function Translate(el As XElement) As Csharp.FuaranNode
        If el Is Nothing Then Throw New ArgumentNullException(NameOf(el))
        Dim builder As Func(Of XElement, Csharp.FuaranNode) = Nothing
        If Not Builders.TryGetValue(el.Name.LocalName, builder) Then
            Dim suggestion = Nearest(el.Name.LocalName)
            Dim hint = If(suggestion Is Nothing, "", $" Did you mean <{suggestion}>?")
            Throw New FuaranXmlException(
                $"Unknown Fuaran element <{el.Name.LocalName}> — not a recognised node kind.{hint} " &
                $"Known elements: {String.Join(", ", KnownElements())}.")
        End If
        Return ApplyNodeTraits(el, builder(el))
    End Function

    ''' <summary>Apply the UNIVERSAL node-level traits — the ones that belong to the
    ''' node envelope rather than to any kind — after the per-kind builder has run.
    '''
    ''' One site, deliberately. A trait on the envelope is spelled the same way on
    ''' every element, so threading it through forty-odd builders would be forty-odd
    ''' chances to spell it differently, and a builder that forgot would fail
    ''' silently: the attribute is admitted by the vocabulary, so no analyzer would
    ''' report it and the hint would simply never appear.
    '''
    ''' <c>tooltip</c> (Fuaran-UI Phase 1112) is the first, and takes the same
    ''' <c>$name</c> bound-query spelling every other text attribute here takes.</summary>
    Private Function ApplyNodeTraits(el As XElement, node As Csharp.FuaranNode) As Csharp.FuaranNode
        Dim hint = OptText(el, "tooltip")
        If hint Is Nothing Then Return node
        Return node.WithTooltip(hint.Value)
    End Function

    ''' <summary>Translate then encode an XML-literal element to its canonical wire JSON.</summary>
    Public Function Encode(el As XElement) As String
        Return Translate(el).Encode()
    End Function

    ''' <summary>The nearest known element name to a misspelling (case-insensitive
    ''' exact match, else the closest by edit distance within 3), or Nothing.</summary>
    Private Function Nearest(name As String) As String
        Dim ciExact = Builders.Keys.FirstOrDefault(Function(k) String.Equals(k, name, StringComparison.OrdinalIgnoreCase))
        If ciExact IsNot Nothing Then Return ciExact

        Dim best As String = Nothing
        Dim bestDist = Integer.MaxValue
        For Each k In Builders.Keys
            Dim dist = EditDistance(name.ToLowerInvariant(), k.ToLowerInvariant())
            If dist < bestDist Then
                bestDist = dist
                best = k
            End If
        Next
        Return If(bestDist <= 3, best, Nothing)
    End Function

    Private Function EditDistance(a As String, b As String) As Integer
        Dim prev(b.Length) As Integer
        Dim curr(b.Length) As Integer
        For j = 0 To b.Length
            prev(j) = j
        Next
        For i = 1 To a.Length
            curr(0) = i
            For j = 1 To b.Length
                Dim cost = If(a(i - 1) = b(j - 1), 0, 1)
                curr(j) = Math.Min(Math.Min(curr(j - 1) + 1, prev(j) + 1), prev(j - 1) + cost)
            Next
            Array.Copy(curr, prev, b.Length + 1)
        Next
        Return prev(b.Length)
    End Function

    ''' <summary>Translate every child element as a node (the children of a layout kind).</summary>
    Friend Function Kids(el As XElement) As IEnumerable(Of Csharp.FuaranNode)
        Return el.Elements().Select(Function(child) Translate(child)).ToList()
    End Function

    ''' <summary>Translate the single node inside a named wrapper element
    ''' (e.g. the &lt;Child&gt;/&lt;Fallback&gt; of an ErrorBoundary, the &lt;Body&gt; of a FragmentDecl).</summary>
    Friend Function TranslateNamed(el As XElement, wrapperName As String) As Csharp.FuaranNode
        Dim wrapper = el.Elements().FirstOrDefault(Function(c) c.Name.LocalName = wrapperName)
        If wrapper Is Nothing Then
            Throw New FuaranXmlException($"<{el.Name.LocalName}> requires a <{wrapperName}> child element.")
        End If
        Dim inner = wrapper.Elements().FirstOrDefault()
        If inner Is Nothing Then
            Throw New FuaranXmlException($"<{wrapperName}> must contain exactly one node element.")
        End If
        Return Translate(inner)
    End Function

End Module
