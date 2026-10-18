Imports System.Linq
Imports System.Xml.Linq
Imports Fuaran.UI.VisualBasic

' Phase 1930 — the XML dialect fails closed on enums. The attribute readers used
' [Enum].TryParse, which also accepts a numeric string, a comma-joined list (OR-ing
' the values) and an undefined number. On an <Embed>'s permissions that made
' permissions="0" an AllowScripts grant, and "AllowSameOrigin,AllowForms" (1|2)
' AllowFullscreen. Only a declared name maps now; every other spelling is a
' FuaranXmlException naming the attribute and the accepted names.
Module EnumRefusal

    Private Function Refusal(el As XElement) As String
        Try
            FuaranXml.Encode(el)
            Return Nothing
        Catch ex As FuaranXmlException
            Return ex.Message
        End Try
    End Function

    Private Function Embed(permissions As String) As XElement
        Return <Embed id="frame" src="https://example.com/widget" title="Widget" permissions=<%= permissions %>/>
    End Function

    Sub Run(h As Harness)
        ' ── The embed sandbox: no spelling but the literal name grants script. ──
        For Each spelling In {"0", "9", "AllowSameOrigin,AllowForms", "AllowScripts,AllowForms", "AllowScript", "-1", "1e0"}
            Dim message = Refusal(Embed(spelling))
            h.Check($"vb enum: permissions=""{spelling}"" refuses",
                    message IsNot Nothing AndAlso message.Contains("permissions=") AndAlso message.Contains("AllowScripts, AllowSameOrigin"),
                    If(message, "translated without error"))
        Next

        ' Every declared name still maps, case-insensitively, through the pipe list.
        Dim all = FuaranXml.Encode(Embed("AllowScripts|allowsameorigin|AllowForms| AllowFullscreen "))
        h.Check("vb enum: every declared EmbedPermission name maps",
                {"AllowScripts", "AllowSameOrigin", "AllowForms", "AllowFullscreen"}.All(Function(n) all.Contains(n)), all)

        Dim forms = FuaranXml.Encode(Embed("AllowForms"))
        h.Check("vb enum: a declared AllowForms grants no script",
                forms.Contains("AllowForms") AndAlso Not forms.Contains("AllowScripts"), forms)

        ' ── Every other enum attribute refuses rather than falling to a default. ─
        Dim tone = Refusal(<Metric id="m" label="M" value="1" tone="7"/>)
        h.Check("vb enum: tone=""7"" refuses and names the attribute",
                tone IsNot Nothing AndAlso tone.Contains("tone=""7""") AndAlso tone.Contains("Brand"),
                If(tone, "translated without error"))

        Dim joined = Refusal(<Metric id="m" label="M" value="1" tone="Brand,Info"/>)
        h.Check("vb enum: a comma-joined tone refuses", joined IsNot Nothing, If(joined, "translated without error"))

        ' The optional-enum reader takes the same parse.
        Dim capture = Refusal(<FileUpload id="f" label="F" capture="0"/>)
        h.Check("vb enum: an optional enum (capture=""0"") refuses",
                capture IsNot Nothing AndAlso capture.Contains("capture="),
                If(capture, "translated without error"))

        ' A declared name still maps, in any case.
        Dim brand = FuaranXml.Encode(<Metric id="m" label="M" value="1" tone="brand"/>)
        h.Check("vb enum: a declared name maps case-insensitively", brand.Contains("Brand"), brand)
    End Sub

End Module
