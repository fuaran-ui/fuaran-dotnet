using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Fuaran.UI.Analyzers.VisualBasic;

// Phase 315 — the VB XML-literal vocabulary the analyzer checks against. This is the
// analyzer's copy of the translator's mapping table (the runtime source of truth is
// Fuaran.UI.VisualBasic.FuaranXml.KnownElements). Because a netstandard2.0 analyzer
// cannot load the net10 veneer, the element set is embedded here and pinned to the
// translator by a test (Fuaran.UI.Analyzers.VisualBasic.Tests asserts Kinds ==
// FuaranXml.KnownElements()), so drift fails CI rather than silently disagreeing.
internal static class Vocabulary
{
    /// <summary>The node-kind element names (element = kind name; GridLayout is authored as Grid).</summary>
    public static readonly ImmutableHashSet<string> Kinds = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        // Layout (Phase 390: Box is the unified container; Dashboard/Stack/Grid/Card remain as Box-emitting element conveniences)
        "Box", "Dashboard", "Stack", "Grid", "SplitPanel", "Tabs", "Card", "Stepper", "SummaryList", "Disclosure", "Modal", "ScrollArea",
        // Display (Phase 459: Spacer retired → Box layout `gap`; Divider stays as a Box `Separator`-emitting element convenience)
        "Heading", "Markdown", "Metric", "Fact", "Badge", "Sparkline", "Callout", "Progress", "Skeleton", "LabelValueRow", "Link", "Image", "List", "Divider", "Toast", "CodeBlock", "Math", "Drawing",
        // Input
        "Form", "Filters", "Button", "FileUpload", "Select", "MultiSelect",
        // Visualisation
        "DataGrid", "Chart", "Table", "Map",
        // Structural (Phase 392: Switch is the state-bound conditional region)
        "Custom", "ErrorBoundary", "FragmentDecl", "FragmentRef", "Mount", "Switch");

    /// <summary>Structural sub-elements (children of a kind, not kinds themselves) — recognised so
    /// the analyzer does not flag them as unknown.</summary>
    public static readonly ImmutableHashSet<string> Structural = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Item", "Option", "Field", "Filter", "Column", "Header", "Row", "Cell", "Marker", "Prop", "Child", "Fallback", "Body", "Case", "Default");

    public static bool IsKnownElement(string name) => Kinds.Contains(name) || Structural.Contains(name);

    private static readonly ImmutableHashSet<string> Format =
        ImmutableHashSet.Create(StringComparer.Ordinal, "format-currency", "format-number", "format-percent", "format-date");

    /// <summary>Per-element allowed attributes. An element absent from this table is not
    /// attribute-checked (allow-any), so FUARAN061 never false-positives on a kind we
    /// have not enumerated.</summary>
    public static readonly ImmutableDictionary<string, ImmutableHashSet<string>> Attributes = BuildAttributes();

    private static ImmutableDictionary<string, ImmutableHashSet<string>> BuildAttributes()
    {
        var b = ImmutableDictionary.CreateBuilder<string, ImmutableHashSet<string>>(StringComparer.Ordinal);
        void Add(string el, params string[] attrs) => b[el] = ImmutableHashSet.Create(StringComparer.Ordinal, attrs);
        ImmutableHashSet<string> WithFormat(params string[] attrs) => Format.Union(attrs);

        Add("Dashboard", "id");
        Add("Stack", "id", "orientation", "wrap");
        Add("Grid", "id", "cols");
        Add("Card", "id", "heading");
        Add("SplitPanel", "id", "weight");
        // TRIPWIRE: no explicit tab-header/tag attributes here (labels are child-inferred).
        // If <Tabs> gains header/tag authoring, add those attributes AND port FUARAN047/048
        // (header/tag-vs-children count) into FuaranVbXmlAnalyzer + the C# leg — deferred in
        // Phase 314/315 only because this surface is absent.
        Add("Tabs", "id", "orientation", "active-index");
        Add("Stepper", "id", "active-step");
        Add("SummaryList", "id", "heading");
        Add("Disclosure", "id", "heading", "open", "default-open");
        Add("Modal", "id", "open", "heading", "dismissable");
        Add("ScrollArea", "id", "orientation", "max-height", "max-width");
        Add("Heading", "id", "text", "level", "variant");
        Add("Markdown", "id", "text");
        b["Metric"] = WithFormat("id", "label", "value", "tone");
        Add("Badge", "id", "label", "variant");
        Add("Sparkline", "id", "source");
        Add("Spacer", "id", "size");
        Add("Callout", "id", "body", "heading", "tone", "icon", "dismissable");
        Add("Progress", "id", "fraction", "label", "caveat", "indeterminate", "tone");
        Add("Skeleton", "id", "rows");
        b["LabelValueRow"] = WithFormat("id", "label", "value", "emphasis", "help");
        Add("Fact", "id", "label", "value", "icon", "tone", "emphasis", "help");
        Add("Link", "id", "href", "label", "rel", "target", "download");
        Add("Image", "id", "src", "alt", "variant");
        Add("List", "id", "ordered");
        Add("Divider", "id", "orientation", "label");
        Add("Toast", "id", "message", "tone", "open", "dismissable");
        Add("CodeBlock", "id", "code", "language", "line-numbers", "copyable");
        Add("Math", "id", "source", "display");
        Add("Drawing", "id", "min-x", "min-y", "width", "height", "title", "description");
        Add("Button", "id", "label", "variant");
        Add("Form", "id", "submit-label");
        Add("Select", "id", "label", "value", "placeholder");
        Add("MultiSelect", "id", "label", "values");
        Add("Filters", "id");
        Add("FileUpload", "id", "label", "accept", "multiple");
        Add("Chart", "id", "source", "kind", "x-field", "y-fields", "title", "stacked");
        Add("Table", "id");
        Add("Map", "id", "centre-lat", "centre-lng", "zoom");
        Add("DataGrid", "id", "source", "editable");
        Add("Custom", "id", "module-id", "component-id", "exposed-node-ids");
        Add("ErrorBoundary", "id");
        Add("FragmentDecl", "id", "name");
        Add("FragmentRef", "id", "name");
        Add("Mount", "id", "scope-id", "two-way", "message-shape", "capabilities");
        Add("Switch", "id", "state-key");
        // Structural sub-elements.
        Add("Case", "match");
        Add("Option", "value", "label");
        Add("Field", "kind", "id", "label", "required", "initial", "help", "selected", "rows");
        Add("Filter", "kind", "name", "label");
        Add("Column", "type", "label", "field");
        Add("Marker", "lat", "lng", "label");
        Add("Prop", "name", "value");
        return b.ToImmutable();
    }

    /// <summary>The nearest known element to a misspelling (case-insensitive exact, else min edit distance ≤ 3).</summary>
    public static string? Nearest(string name)
    {
        foreach (var k in Kinds)
        {
            if (string.Equals(k, name, StringComparison.OrdinalIgnoreCase))
            {
                return k;
            }
        }

        string? best = null;
        int bestDist = int.MaxValue;
        foreach (var k in Kinds)
        {
            int dist = EditDistance(name.ToLowerInvariant(), k.ToLowerInvariant());
            if (dist < bestDist)
            {
                bestDist = dist;
                best = k;
            }
        }

        return bestDist <= 3 ? best : null;
    }

    private static int EditDistance(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++)
        {
            prev[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }

            Array.Copy(curr, prev, b.Length + 1);
        }

        return prev[b.Length];
    }
}
