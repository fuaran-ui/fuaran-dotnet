Imports System.IO

''' <summary>A tiny assertion runner — the console-Exe shape the other suites use.</summary>
Public Class Harness
    Private _passed As Integer = 0
    Private ReadOnly _failures As New List(Of String)

    Public Sub Check(name As String, condition As Boolean, Optional detail As String = Nothing)
        If condition Then
            _passed += 1
        Else
            _failures.Add(If(detail Is Nothing, name, $"{name}: {detail}"))
        End If
    End Sub

    Public Sub ByteEqual(name As String, actual As String, expected As String)
        If actual = expected Then
            _passed += 1
            Return
        End If

        Dim i = 0
        Dim min = Math.Min(actual.Length, expected.Length)
        While i < min AndAlso actual(i) = expected(i)
            i += 1
        End While

        _failures.Add($"{name}: first diff at char {i}" & Environment.NewLine &
                      $"    expected: {Excerpt(expected, i)}" & Environment.NewLine &
                      $"    actual:   {Excerpt(actual, i)}")
    End Sub

    Private Shared Function Excerpt(s As String, at As Integer) As String
        Dim start = Math.Max(0, at - 20)
        Dim len = Math.Min(60, s.Length - start)
        Return If(start > 0, "…", "") & s.Substring(start, len) & If(start + len < s.Length, "…", "")
    End Function

    Public Function Report(suite As String) As Integer
        Console.WriteLine($"[{suite}] {_passed} passed, {_failures.Count} failed.")
        For Each f In _failures
            Console.WriteLine($"  FAIL {f}")
        Next
        Return If(_failures.Count = 0, 0, 1)
    End Function
End Class

''' <summary>Locates + reads the shared wire-format-fixtures corpus, walking up from
''' the test assembly so the lookup is working-directory-independent.</summary>
Public Module Corpus
    Private ReadOnly _root As String = Locate()

    Public ReadOnly Property Root As String
        Get
            Return _root
        End Get
    End Property

    Public ReadOnly Property Available As Boolean
        Get
            Return _root IsNot Nothing
        End Get
    End Property

    Private Function Locate() As String
        Dim dir = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate = Path.Combine(dir.FullName, "wire-format-fixtures")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "manifest.json")) Then
                Return candidate
            End If
            dir = dir.Parent
        End While
        Return Nothing
    End Function

    Public Function ReadFixture(relativePath As String) As String
        Return File.ReadAllText(Path.Combine(_root, relativePath))
    End Function
End Module
