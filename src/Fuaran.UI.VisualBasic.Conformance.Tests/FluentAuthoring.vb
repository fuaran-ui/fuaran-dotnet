Imports Fuaran.UI.VisualBasic
Imports Csharp = Fuaran.UI.CSharp
' VB's Imports can name a *type*: shared members of the factory class then bind
' unqualified — Metric(…) — the moral equivalent of C#'s `using static`.
Imports Fuaran.UI.CSharp.Fuaran

' Fluent-from-VB parity — the executable form of the vbnet-authoring.md "Prefer
' fluent?" section. The C# factory surface is plain .NET, so a VB author can drive
' it directly (With-initializers set the init-only options records; string / double
' widen through the facade's implicit conversions). These checks pin the doc's
' sample: the fluent tree and its XML-literal equivalent encode byte-identically.
Module FluentAuthoring

    Sub Run(h As Harness)
        Dim literal = <Card id="insights" heading="Insights">
                          <Metric id="revenue" label="Revenue" value="1234.5"
                              format-currency="GBP" tone="Brand"/>
                      </Card>

        Dim fluent = Csharp.Fuaran.Card(New Csharp.CardOptions With {
            .Id = "insights",
            .Heading = "Insights",
            .Children = {
                Csharp.Fuaran.Metric(New Csharp.MetricOptions With {
                    .Id = "revenue",
                    .Label = "Revenue",
                    .Value = 1234.5,
                    .Format = Csharp.CellFormat.Currency("GBP"),
                    .Tone = Csharp.Tone.Brand})}})

        h.ByteEqual("fluent VB == XML-literal VB (card+metric)",
                    fluent.Encode(), FuaranXml.Encode(literal))

        ' Same tree once more via the type-import style (unqualified factory calls).
        Dim unqualified = Card(New Csharp.CardOptions With {
            .Id = "insights",
            .Heading = "Insights",
            .Children = {
                Metric(New Csharp.MetricOptions With {
                    .Id = "revenue",
                    .Label = "Revenue",
                    .Value = 1234.5,
                    .Format = Csharp.CellFormat.Currency("GBP"),
                    .Tone = Csharp.Tone.Brand})}})

        h.ByteEqual("type-import fluent VB == XML-literal VB",
                    unqualified.Encode(), FuaranXml.Encode(literal))
    End Sub

End Module
