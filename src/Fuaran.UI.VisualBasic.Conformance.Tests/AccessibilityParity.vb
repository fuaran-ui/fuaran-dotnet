Imports Fuaran.UI.VisualBasic
Imports Csharp = Fuaran.UI.CSharp

' Phase 313 — mirror-parity test. Asserts a VB XML-authored node's `accessibility`
' shape equals the smart-ctor host's, per kind. Because the translator routes each
' element through the C# factory (which calls the F# smart ctor), this holds by
' construction — the test is the DRIFT GUARD: if the translator ever stopped routing
' through the factory (an independent VB ARIA table, a bare build), it fails here.
' The C# Phase 307 parity test already pins C# ≡ F#, so VB ≡ C# ≡ F#.
Module AccessibilityParity

    Sub Run(h As Harness)
        Parity(h, "metric",
               <Metric id="m" label="x" value="0"/>,
               Csharp.Fuaran.Metric(New Csharp.MetricOptions With {.Id = "m", .Label = "x", .Value = 0.0}))
        Parity(h, "card",
               <Card id="c"/>,
               Csharp.Fuaran.Card(New Csharp.CardOptions With {.Id = "c"}))
        Parity(h, "dashboard",
               <Dashboard id="d"/>,
               Csharp.Fuaran.Dashboard(New Csharp.DashboardOptions With {.Id = "d"}))
        Parity(h, "button",
               <Button id="b" label="x"/>,
               Csharp.Fuaran.Button(New Csharp.ButtonOptions With {.Id = "b", .Label = "x"}))
        Parity(h, "callout",
               <Callout id="x" body="y"/>,
               Csharp.Fuaran.Callout(New Csharp.CalloutOptions With {.Id = "x", .Body = "y"}))

        ' Sanity: an ARIA-bearing kind genuinely carries ARIA; an ARIA-free one does not.
        Dim metricEl = <Metric id="m" label="x" value="0"/>
        h.Check("metric carries ARIA", Accessibility.AriaJson(metricEl) IsNot Nothing)
        Dim headingEl = <Heading id="h" text="x"/>
        h.Check("heading carries no ARIA", Accessibility.AriaJson(headingEl) Is Nothing)
    End Sub

    Private Sub Parity(h As Harness, kind As String, vbEl As System.Xml.Linq.XElement, csNode As Csharp.FuaranNode)
        Dim vbAria = Accessibility.AriaJson(vbEl)
        Dim csAria = Csharp.Accessibility.AriaJson(csNode)
        h.Check($"a11y parity: VB <{kind}> mirrors the smart-ctor host",
                String.Equals(vbAria, csAria), $"VB={vbAria} host={csAria}")
    End Sub

End Module
