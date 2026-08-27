using System.Linq;

namespace Fuaran.UI.CSharp.Conformance.Tests;

// Phase 873 — the §11 step-6 follow-up Phase 801 named. The gap-closure wave
// (861–867) added spec-record FIELDS, and Coverage.cs reflects NodeKind CASES, so
// nothing in this suite could fail while the veneer could not author them. That is
// the whole reason these checks are written by hand: they assert what no reflection
// over the kind set can notice.
//
// The assertion is on the ENCODED WIRE SPELLING rather than on a corpus fixture,
// deliberately. The wave's grid fixtures are decoded-path shapes (`field` /
// `rowKeyField`), where a C#-authored grid carries the closure overrides the
// options records take, and the Metric fixture is bare where the smart ctor injects
// ARIA — so byte-parity against those fixtures would be a claim about the fixtures'
// authoring path, not about this veneer's. Every check below carries a negative
// control, so none can pass vacuously.
internal static class GapClosureSlots
{
    public static void Run(Harness h)
    {
        // ── Phase 861 — sortStateKey, bound defaultSort, per-column sortable ──
        var sorted = Fuaran.DataGrid(new DataGridOptions<Row>
        {
            Id = "fleet",
            ToRow = r => r.Cells,
            SortStateKey = "fleet-sort",
            DefaultSort = new DefaultSort { Column = 4, Direction = SortDirection.Asc },
            Columns = [Column.Text("Van", _ => ""), Column.Text("Notes", _ => "").Sortable(false)],
        }).Encode();

        h.Check("861: sortStateKey reaches the wire", sorted.Contains("\"sortStateKey\":\"fleet-sort\""), sorted);
        h.Check("861: bound defaultSort reaches the wire", sorted.Contains("\"defaultSort\":{\"column\":4,\"direction\":\"asc\"}"), sorted);
        h.Check("861: a column narrows itself out of sorting", sorted.Contains("\"sortable\":false"), sorted);

        // Negative control: a grid that declares none of it says none of it. The
        // narrowing flags in particular must be ABSENT rather than `false`, or every
        // unstated column would read as an explicit opt-out.
        var plain = Fuaran.DataGrid(new DataGridOptions<Row>
        {
            Id = "fleet",
            ToRow = r => r.Cells,
            Columns = [Column.Text("Van", _ => "")],
        }).Encode();

        h.Check("861 control: no sortStateKey when undeclared", !plain.Contains("sortStateKey"), plain);
        h.Check("861 control: no sortable when undeclared", !plain.Contains("sortable"), plain);
        h.Check("861 control: no defaultSort when undeclared", !plain.Contains("defaultSort"), plain);

        // ── Phase 862 — pageSize + pageStateKey ───────────────────────────────
        var paged = Fuaran.DataGrid(new DataGridOptions<Row>
        {
            Id = "members",
            ToRow = r => r.Cells,
            PageSize = 20,
            PageStateKey = "members-page",
            Columns = [Column.Text("Month", _ => "")],
        }).Encode();

        h.Check("862: pageSize reaches the wire", paged.Contains("\"pageSize\":20"), paged);
        h.Check("862: pageStateKey reaches the wire", paged.Contains("\"pageStateKey\":\"members-page\""), paged);
        h.Check("862 control: no paging when undeclared", !plain.Contains("pageSize") && !plain.Contains("pageStateKey"), plain);

        // ── Phase 863 — editStateKey + per-column editable ────────────────────
        var editable = Fuaran.DataGrid(new DataGridOptions<Row>
        {
            Id = "stock",
            ToRow = r => r.Cells,
            Editable = true,
            EditStateKey = "stock-adjustments",
            Columns = [Column.Text("Month", _ => ""), Column.Text("Note", _ => "").Editable(false)],
        }).Encode();

        h.Check("863: editStateKey reaches the wire", editable.Contains("\"editStateKey\":\"stock-adjustments\""), editable);
        h.Check("863: a column narrows itself read-only", editable.Contains("\"editable\":false"), editable);
        h.Check("863 control: no editStateKey when undeclared", !plain.Contains("editStateKey"), plain);

        // ── Phase 801 — the static table's declared sort intent ───────────────
        var table = Fuaran.Table(new TableOptions
        {
            Id = "prices",
            Headers = [(Text)"Item", (Text)"Price"],
            Rows = [[(Text)"Widget", (Text)"4.00"]],
            Sortable = true,
            DefaultSort = new DefaultSort { Column = 1, Direction = SortDirection.Desc },
        }).Encode();

        h.Check("801: static sortable reaches the wire", table.Contains("\"sortable\":true"), table);
        h.Check("801: static defaultSort reaches the wire", table.Contains("\"defaultSort\":{\"column\":1,\"direction\":\"desc\"}"), table);

        var bareTable = Fuaran.Table(new TableOptions { Id = "prices", Headers = [(Text)"Item"] }).Encode();
        h.Check("801 control: no sort intent when undeclared", !bareTable.Contains("sortable") && !bareTable.Contains("defaultSort"), bareTable);

        // ── Phase 864 — FormField.rule, all four shapes plus the message ──────
        var form = Fuaran.Form(new FormOptions
        {
            Id = "hire",
            Fields =
            [
                FormField.Text("work-email", "Work email", required: true, rule: new FieldRule { Format = TextFormat.Email }),
                FormField.Text("postcode", "Postcode", rule: new FieldRule
                {
                    Pattern = "[A-Z]{1,2}[0-9][A-Z0-9]?",
                    Message = "Enter a UK postcode",
                }),
                FormField.Text("username", "Username", rule: new FieldRule { MinLength = 3, MaxLength = 24 }),
                FormField.Text("end-date", "End date", rule: new FieldRule
                {
                    Compare = CompareRule.AgainstField("start-date", CompareOp.Gte),
                }),
            ],
        }).Encode();

        h.Check("864: format rule reaches the wire", form.Contains("\"rule\":{\"format\":\"email\"}"), form);
        h.Check("864: pattern + message reach the wire", form.Contains("\"pattern\":\"[A-Z]{1,2}[0-9][A-Z0-9]?\"") && form.Contains("Enter a UK postcode"), form);
        h.Check("864: length bounds reach the wire", form.Contains("\"maxLength\":24") && form.Contains("\"minLength\":3"), form);
        h.Check(
            "864: cross-field compare reaches the wire",
            form.Contains("\"compare\":{\"against\":{\"$type\":\"State\",\"key\":\"start-date\"},\"op\":\"gte\"}"),
            form);

        var ruleless = Fuaran.Form(new FormOptions
        {
            Id = "hire",
            Fields = [FormField.Text("work-email", "Work email", required: true)],
        }).Encode();
        h.Check("864 control: no rule when undeclared", !ruleless.Contains("\"rule\""), ruleless);

        // ── Phase 867 — trendPolarity ─────────────────────────────────────────
        var inverted = Fuaran.Metric(new MetricOptions
        {
            Id = "wait",
            Label = "Avg wait",
            Value = 80.0,
            Trend = -0.0734,
            TrendPolarity = TrendPolarity.LowerIsBetter,
            Tone = Tone.Warning,
        }).Encode();

        h.Check("867: trendPolarity reaches the wire", inverted.Contains("\"trendPolarity\":\"LowerIsBetter\""), inverted);

        // Negative control, and it is the one that matters most here: the DEFAULT is
        // omitted, so a veneer that hard-coded the enum would look identical on a
        // default tile and differ only on the inverted one.
        var ordinary = Fuaran.Metric(new MetricOptions { Id = "rev", Label = "Revenue", Value = 1.0, Trend = 0.07 }).Encode();
        h.Check("867 control: HigherIsBetter is omitted at default", !ordinary.Contains("trendPolarity"), ordinary);

        // And polarity must not disturb `tone` — one slot could never have said both.
        h.Check("867: tone survives an inverted polarity", inverted.Contains("\"tone\":\"Warning\""), inverted);
    }

    /// <summary>A minimal row type for the grid checks — the veneer's REQUIRED
    /// <c>ToRow</c> projection has to be satisfied, and the checks above are about
    /// the behaviour slots rather than the cells.</summary>
    private sealed record Row
    {
        public System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, object>> Cells =>
            Enumerable.Empty<System.Collections.Generic.KeyValuePair<string, object>>();
    }
}
