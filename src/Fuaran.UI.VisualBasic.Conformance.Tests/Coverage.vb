Imports System.Linq
Imports Microsoft.FSharp.Reflection
Imports Fuaran.UI.VisualBasic
Imports FsTypes = Fuaran.UI.Types

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
    Private ReadOnly ContainerCases As String() = {"Layout", "Display", "Input", "Visualisation"}

    Sub Run(h As Harness)
        Dim structuralCases =
            UnionCaseNames(GetType(FsTypes.NodeKind(Of Object))).
            Where(Function(c) Not ContainerCases.Contains(c))

        Dim kindCaseNames =
            UnionCaseNames(GetType(FsTypes.LayoutKind(Of Object))).
            Concat(UnionCaseNames(GetType(FsTypes.DisplayKind(Of Object)))).
            Concat(UnionCaseNames(GetType(FsTypes.InputKind(Of Object)))).
            Concat(UnionCaseNames(GetType(FsTypes.VisKind(Of Object)))).
            Concat(structuralCases)

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
