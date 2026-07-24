Imports System.Collections.Generic
Imports System.Linq
Imports System.Xml.Linq
Imports Csharp = Fuaran.UI.CSharp

' Phase 311 — the structural NodeKind cases. Custom carries <Prop> children;
' ErrorBoundary / FragmentDecl wrap their node payloads in named child elements
' (<Child>/<Fallback>/<Body>).
Friend Module StructuralMapping

    Friend Sub Register(d As Dictionary(Of String, Func(Of XElement, Csharp.FuaranNode)))

        d("Custom") = Function(el) Csharp.Fuaran.Custom(
            New Csharp.CustomOptions With {
                .Id = Attr(el, "id"),
                .ModuleId = Attr(el, "module-id"),
                .ComponentId = Attr(el, "component-id"),
                .Props = ReadProps(el),
                .ExposedNodeIds = PipeList(Attr(el, "exposed-node-ids"))})

        d("ErrorBoundary") = Function(el) Csharp.Fuaran.ErrorBoundary(
            New Csharp.ErrorBoundaryOptions With {
                .Id = Attr(el, "id"),
                .Child = TranslateNamed(el, "Child"),
                .Fallback = TranslateNamed(el, "Fallback")})

        d("FragmentDecl") = Function(el) Csharp.Fuaran.FragmentDecl(
            New Csharp.FragmentDeclOptions With {
                .Id = Attr(el, "id"),
                .Name = Attr(el, "name"),
                .Body = TranslateNamed(el, "Body")})

        d("FragmentRef") = Function(el) Csharp.Fuaran.FragmentRef(
            New Csharp.FragmentRefOptions With {.Id = Attr(el, "id"), .Name = Attr(el, "name")})

        ' Phase 265 (§4o) — isolation/embedding boundary. Wire-observable attributes:
        ' scope-id (required), two-way (default False = out-only), message-shape
        ' (optional), capabilities (pipe-list, empty = default-deny). OnBubble is
        ' opaque to the wire and authors as a no-op; typed guest Inputs stay F#-side.
        d("Mount") = Function(el) Csharp.Fuaran.Mount(
            New Csharp.MountOptions With {
                .Id = Attr(el, "id"),
                .ScopeId = Attr(el, "scope-id"),
                .TwoWay = AttrBool(el, "two-way"),
                .MessageShape = OptStr(el, "message-shape"),
                .Capabilities = PipeList(Attr(el, "capabilities"))})

        ' Phase 392 — state-bound conditional region. state-key names the reactive
        ' StateStore key; each <Case match="…"> wraps the single node rendered when
        ' the state's string form equals its match (first-match-wins); <Default>
        ' wraps the no-match child (the SSR/first-paint surface).
        d("Switch") = Function(el) Csharp.Fuaran.Switch(
            New Csharp.SwitchOptions With {
                .Id = Attr(el, "id"),
                .StateKey = Attr(el, "state-key"),
                .Cases = ChildElements(el, "Case").Select(AddressOf ReadCase).ToList(),
                .Default = TranslateNamed(el, "Default")})
    End Sub

    Private Function ReadCase(c As XElement) As Csharp.SwitchCase
        Dim inner = c.Elements().FirstOrDefault()
        If inner Is Nothing Then
            Throw New FuaranXmlException("<Case> must contain exactly one node element.")
        End If
        Return New Csharp.SwitchCase With {.Match = Attr(c, "match"), .Child = Translate(inner)}
    End Function

    Private Function ReadProps(el As XElement) As IReadOnlyDictionary(Of String, String)
        Dim result As New Dictionary(Of String, String)(StringComparer.Ordinal)
        For Each p In ChildElements(el, "Prop")
            result(Attr(p, "name")) = Attr(p, "value")
        Next
        Return result
    End Function

End Module
