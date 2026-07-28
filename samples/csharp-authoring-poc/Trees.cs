using static Fuaran.UI.CSharp.Poc.Fui;
using static Fuaran.UI.Types;

// Stage-3 swap: these vocabulary DUs are IDL-generated now; the Types re-exports
// are erased abbreviations C# cannot see (same aliasing as Builders.cs).
using ToneVariant = Fuaran.UI.Generated.ToneVariant;
using BadgeVariant = Fuaran.UI.Generated.BadgeVariant;
using ButtonVariant = Fuaran.UI.Generated.ButtonVariant;
using ChartKind = Fuaran.UI.Generated.ChartKind;

namespace Fuaran.UI.CSharp.Poc;

// ============================================================================
//  The representative PoC tree set — each authored in idiomatic C# to be
//  WIRE-IDENTICAL to the canonical corpus fixture of the same name under
//  wire-format-fixtures/nodes/. Together they exercise: layout nesting (Card /
//  Stack / Grid / Dashboard), display (Heading / Metric-KPI / Badge / Markdown),
//  a Form field family, Button + chained actions, and one Chart.
//
//  The named-tuple `(name, builder)` is the byte-compare unit the harness runs.
// ============================================================================

internal static class Trees
{
    // Reusable leaves (nested into the layout fixtures, exactly as in Fixtures.fs).
    private static MetricBuilder Metric1() =>
        Metric("metric-1")
            .Label("Revenue")
            .Source(1234.5)
            .Format(Fmt.Currency("GBP"))
            .Tone(ToneVariant.Brand)
            .Trend(0.07)
            .TrendFormat(Fmt.Percent(1))
            .Icon("trending-up")
            .Subtext("vs last month");

    private static MarkdownBuilder Markdown1() =>
        Markdown("markdown-1").Text("Updated hourly.");

    public static (string Name, NodeBuilder Builder)[] All() => new (string, NodeBuilder)[]
    {
        // ─── Display ─────────────────────────────────────────────────────
        ("metric-1", Metric1()),
        ("markdown-1", Markdown1()),
        ("heading-1", Heading("heading-1").Level(2).Text("Channel performance")),
        ("badge-1", Badge("badge-1").Label("Beta").Variant(BadgeVariant.Info)),

        // ─── Layout (nesting) ────────────────────────────────────────────
        ("card-1", Card("card-1").Heading("Insights").Children(Metric1())),
        ("stack-1", Stack("stack-1").Children(Metric1(), Markdown1())),
        ("glayout-1", Grid("glayout-1").Cols(12).Children(Metric1())),
        ("dash-empty", Dashboard("dash-empty")),

        // ─── Input (button + actions, form field family) ─────────────────
        ("btn-1", Button("btn-1")
            .Label("Refresh")
            .OnClick(Act.Chain())
            .Variant(ButtonVariant.Primary)
            .Icon("refresh")
            .DisabledWhen("loading")),

        ("btn-copy-link", Button("btn-copy-link")
            .Label("Copy share link")
            .OnClick(Act.Chain(
                Act.WriteToClipboard("https://example.com/share/abc123"),
                Act.Dispatch("ClipboardCopied")))
            .Variant(ButtonVariant.Secondary)),

        ("form-1", Form("form-1")
            .Fields(
                FieldBuilder.Text("name", "Name").Required().Help("Full legal name"),
                FieldBuilder.Number("age", "Age"),
                FieldBuilder.Checkbox("agree", "I agree").Required(),
                FieldBuilder.Choice("tier", "Tier", "basic", ("basic", "Basic"), ("pro", "Pro")),
                FieldBuilder.TextArea("notes", "Notes", 5))
            .SubmitLabel("Save")
            .DisabledWhen("formBusy")),

        // ─── Visualisation ───────────────────────────────────────────────
        ("chart-1", Chart("chart-1")
            .Kind(ChartKind.Line)
            .XField("month")
            .YFields("revenue", "cost")
            .Title("Channel mix")
            .Stacked()),
    };
}
