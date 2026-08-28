Imports System.IO
Imports System.Text.Json
Imports Fuaran.UI.VisualBasic
Imports Csharp = Fuaran.UI.CSharp

' Phase 312 — corpus conformance by construction (the VB analogue of Phase 306).
' Drives the full shared wire-format-fixtures corpus through the veneer:
'
'   * every node / op round-trip fixture decodes + re-encodes byte-identically
'     through the C#-native Decode facade the VB veneer reuses (no VB codec);
'   * every reject fixture surfaces the canonical code + path;
'   * coverage-vs-corpus: every node fixture's kind.$type has a VB XML element.
'
' ARIA reconciliation (Phase 313, same decision as C# Phase 307): the corpus is bare;
' the factories inherit ARIA, so authoring a fixture and byte-comparing to the bare
' file only lands for the ARIA-free kinds (see Authoring.vb). The decode round-trip
' proves byte-fidelity ARIA-agnostically. No corpus regen, no fourth CI leg.
Module Conformance

    Sub Run(h As Harness)
        If Not Corpus.Available Then
            Console.WriteLine("[conformance] wire-format-fixtures corpus absent — skipping full corpus run.")
            Return
        End If

        Using doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Corpus.Root, "manifest.json")))
            For Each f In doc.RootElement.GetProperty("fixtures").EnumerateArray()
                Dim id = f.GetProperty("id").GetString()
                Dim kind = f.GetProperty("kind").GetString()
                Dim decoder = If(GetStr(f, "decoder"), "node")
                Dim input = Corpus.ReadFixture(f.GetProperty("inputFile").GetString())

                Select Case kind
                    Case "node-round-trip"
                        h.Check($"node round-trip {id}", Csharp.Decode.NodeRoundTrips(input),
                                "decode/re-encode not byte-identical")
                        Dim kt = KindType(input)
                        h.Check($"coverage-vs-corpus: <{Element(kt)}> ({id})",
                                kt IsNot Nothing AndAlso FuaranXml.IsKnownElement(Element(kt)),
                                $"no VB element authors kind {kt}")
                    Case "op-round-trip"
                        h.Check($"op round-trip {id}", Csharp.Decode.OpRoundTrips(input),
                                "decode/re-encode not byte-identical")
                    Case "reject"
                        RunReject(h, id, decoder, input, GetStr(f, "expectedErrorCode"), GetStr(f, "expectedPath"))
                End Select
            Next
        End Using
    End Sub

    Private Sub RunReject(h As Harness, id As String, decoder As String, input As String,
                          expectedCode As String, expectedPath As String)
        Dim err = If(decoder = "op", Csharp.Decode.Op(input).Error, Csharp.Decode.Node(input).Error)
        If err Is Nothing Then
            h.Check($"reject {id}", False, "decoded ok — expected a reject")
            Return
        End If
        h.Check($"reject {id}: code {expectedCode}", err.Code = expectedCode, $"got {err.Code}")
        h.Check($"reject {id}: path {expectedPath}",
                expectedPath Is Nothing OrElse err.Path.StartsWith(expectedPath), $"got {err.Path}")
    End Sub

    ' LayoutKind.GridLayout is authored as <Grid>; every other kind name = element name.
    Private Function Element(kindType As String) As String
        Return If(kindType = "GridLayout", "Grid", kindType)
    End Function

    ' Parse corpus payloads at the wire format's own syntactic bound, not
    ' System.Text.Json's 64-level default. A node level costs several JSON
    ' levels, so the §21 accept fixture at exactly MaxDepth node levels is 72
    ' JSON levels — well inside what the decoder accepts and past what the
    ' default reader will read. On the default this harness did not fail a
    ' fixture, it THREW, killing the whole conformance run at the first deep
    ' payload.
    Private ReadOnly WireJson As New JsonDocumentOptions With {
        .MaxDepth = Global.Fuaran.UI.WireLimits.MaxJsonDepth
    }

    Private Function KindType(nodeJson As String) As String
        Using doc = JsonDocument.Parse(nodeJson, WireJson)
            Dim kind As JsonElement
            If Not doc.RootElement.TryGetProperty("kind", kind) Then Return Nothing
            Dim t As JsonElement
            If Not kind.TryGetProperty("$type", t) Then Return Nothing
            Return t.GetString()
        End Using
    End Function

    Private Function GetStr(el As JsonElement, name As String) As String
        Dim v As JsonElement
        If el.TryGetProperty(name, v) Then Return v.GetString()
        Return Nothing
    End Function

End Module
