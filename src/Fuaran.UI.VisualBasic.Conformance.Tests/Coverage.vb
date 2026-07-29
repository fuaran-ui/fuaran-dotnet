Imports System.Linq
Imports Microsoft.FSharp.Reflection
Imports Fuaran.UI.VisualBasic
' `Fuaran.UI.Types.NodeKind` is a type ABBREVIATION since the 692 swap — erased
' in metadata, so VB reflects the generated declaration directly.
Imports FsTypes = Fuaran.UI.Generated

' Phase 311 mapping-completeness guard — the §11 forward-coupling reminder for the VB
' translator: enumerate every shipped NodeKind/spec case by reflecting the F# DUs and
' assert a VB XML element maps each. A new F# kind with no VB mapping fails here.
Module Coverage

    ' The four NodeKind cases that wrap a sub-kind DU (Layout of LayoutKind, …);
    ' their leaf kinds are enumerated from those sub-DUs. Every OTHER top-level
    ' NodeKind case (Custom / ErrorBoundary / FragmentDecl / FragmentRef / Mount /
    ' any future one) authors under its own case name — derived by reflection so a
    ' new top-level NodeKind case can never silently slip past this guard (the
    ' Phase-265 Mount omission a hardcoded list allowed).
    Sub Run(h As Harness)
        ' Phase 692 - NodeKind is flat: every kind and the six structural cases
        ' enumerate off the one DU, so the pin is total by reflection alone.
        Dim kindCaseNames = UnionCaseNames(GetType(FsTypes.NodeKind(Of Object)))

        For Each caseName In kindCaseNames
            ' LayoutKind.GridLayout is authored as <Grid> (DataGrid keeps its name).
            Dim expected = If(caseName = "GridLayout", "Grid", caseName)
            h.Check($"coverage: <{expected}> maps kind {caseName}",
                    FuaranXml.IsKnownElement(expected),
                    $"no VB XML element maps kind {caseName}")
        Next
    End Sub

    Private Function UnionCaseNames(duType As Type) As IEnumerable(Of String)
        Return FSharpType.GetUnionCases(duType, Nothing).Select(Function(c) c.Name)
    End Function

End Module
