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
        "Heading", "Markdown", "Metric", "Fact", "Badge", "Sparkline", "Callout", "Progress", "Skeleton", "Icon", "LabelValueRow", "Link", "Image", "Media", "Embed", "List", "Divider", "Toast", "CodeBlock", "Math", "Drawing",
        // Input
        "Form", "Filters", "Button", "FileUpload", "Select", "MultiSelect",
        // Visualisation
        "DataGrid", "Chart", "Table", "Map",
        // Structural (Phase 392: Switch is the state-bound conditional region)
        "Custom", "ErrorBoundary", "FragmentDecl", "FragmentRef", "Mount", "Switch");

    /// <summary>Structural sub-elements (children of a kind, not kinds themselves) — recognised so
    /// the analyzer does not flag them as unknown. Membership here is not cosmetic:
    /// `IsKnownElement` is `Kinds ∪ Structural`, and the analyzer reports FUARAN060 and
    /// RETURNS on anything outside it, so an omission is a false positive on valid
    /// authoring AND silently skips that element's attribute check. Pinned to the IDL
    /// slot each name spells by `StructuralElementPin` in the analyzer test project.</summary>
    public static readonly ImmutableHashSet<string> Structural = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        // Phase 1080 — `Source` is an `<Image>` child carrying one srcSet candidate
        // (`src` + `width`). A repeated STRUCTURED slot has no attribute spelling,
        // so it takes the child-element shape `Item` / `Option` / `Marker` already use.
        //
        // `Tone` is a `<Column>` child carrying one entry of the TonedPill value map
        // (Phase 750). It was read by the translator and admitted by the attribute
        // table below from the day it landed, but omitted HERE — so `<Tone>` raised
        // FUARAN060 on every valid use until `StructuralElementPin` measured the set.
        "Item", "Option", "Field", "Filter", "Column", "Header", "Row", "Cell", "Marker", "Prop", "Child", "Fallback", "Body", "Case", "Default", "Source", "Tone", "Track");

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
        // Phase 867 / 873 — `trend` and `trend-polarity` arrive together. Polarity is
        // a statement ABOUT a trend, so a tier that could spell one and not the other
        // would let an author declare a sentiment for a quantity the tile never shows.
        b["Metric"] = WithFormat("id", "label", "value", "tone", "trend", "trend-polarity");
        Add("Badge", "id", "label", "variant");
        Add("Sparkline", "id", "source");
        // (No `Spacer` row. The kind was retired in Phase 459 — spacing is the
        // enclosing Box's `gap` — and the element left neither a `Kinds` entry
        // nor a translator mapping, so `<Spacer>` is refused by FUARAN060 before
        // any attribute is looked at. The row that outlived it was unreachable
        // code that also mis-stated the surface; `StructuralElementPin`'s
        // recognition leg is what named it.)
        Add("Callout", "id", "body", "heading", "tone", "icon", "dismissable");
        Add("Progress", "id", "fraction", "label", "caveat", "indeterminate", "tone");
        Add("Skeleton", "id", "rows");
        Add("Icon", "id", "icon", "size", "tone", "label");
        b["LabelValueRow"] = WithFormat("id", "label", "value", "emphasis", "help");
        Add("Fact", "id", "label", "value", "icon", "tone", "emphasis", "help");
        Add("Link", "id", "href", "label", "rel", "target", "download");
        Add("Image", "id", "src", "alt", "variant", "fit", "aspect-ratio", "loading", "caption", "expandable");
        // Phase 1076 — ONE element for the one wire kind; `kind` selects the
        // variant ("video" | "audio"). `autoplay` and `poster` are listed because
        // the element admits them; they are READ only on the video branch, which
        // is a translator rule rather than an attribute-vocabulary one.
        // Phase 1110 — `transcript` is the only new ATTRIBUTE: `tracks` is a repeated
        // structured slot, so it takes the <Track> child-element shape instead.
        Add("Media", "id", "src", "label", "kind", "controls", "loop", "autoplay", "poster", "transcript");
        // Phase 1111 - `permissions` is the narrowed spelling of a `list<enum>`, the
        // `Mount.capabilities` shape: a list of bare tokens does not earn a child
        // element. `aspect-ratio` spells the slot that reuses `ImageAspect`.
        Add("Embed", "id", "src", "title", "aspect-ratio", "permissions");
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
        // Phase 876 — the value-format-* family carries the value axis's number
        // format (the `Format` vocabulary), the chart-side sibling of `format-*`.
        // Phase 878 — the axis names + the subtitle. Absent x-title / y-title
        // fall back to the capitalised field name, so they are optional in the
        // ordinary case rather than an opt-in.
        // Phase 880 — `legend-position`: Top | Right | Bottom | None. Absent
        // takes the host style's default (Right), so it is an override rather
        // than a required declaration.
        // Phase 881 — `data-labels`: Off | Ends. Absent means Off, which is
        // also the default; `Ends` labels bar caps and line endpoints only, and
        // there is deliberately no all-points value to spell.
        // Phase 882 — `x-scale`: Category | Temporal. Absent means Category.
        // `Temporal` declares ISO-8601 date cells and a continuous day-scale;
        // the pre-emit validator refuses it over a non-date column (FUARAN097).
        Add("Chart", "id", "source", "kind", "x-field", "y-fields", "title", "stacked",
            "value-format-currency", "value-format-number", "value-format-percent",
            "x-title", "y-title", "subtitle", "legend-position", "data-labels", "x-scale");
        // Phase 801 / 873 — the static table's declared sort intent. `default-sort-column`
        // indexes the <Header> children; a direction with no column names no order, so the
        // pair is read as one.
        Add("Table", "id", "sortable", "default-sort-column", "default-sort-direction");
        Add("Map", "id", "centre-lat", "centre-lng", "zoom");
        // Phases 861 / 862 / 863 / 873 — the grid's behaviour declarations. Each names a
        // State key the grid both writes and reads (Phase 860's charter rule), which is why
        // there is no `sortable` / `pageable` boolean here: the KEY is the affordance, and a
        // flag with no key behind it is the decorative-pager shape the charter refuses.
        Add("DataGrid", "id", "source", "editable",
            "sort-state-key", "default-sort-column", "default-sort-direction",
            "page-size", "page-state-key", "edit-state-key");
        Add("Custom", "id", "module-id", "component-id", "exposed-node-ids");
        Add("ErrorBoundary", "id");
        Add("FragmentDecl", "id", "name");
        Add("FragmentRef", "id", "name");
        Add("Mount", "id", "scope-id", "two-way", "message-shape", "capabilities");
        Add("Switch", "id", "state-key");
        // Structural sub-elements.
        Add("Case", "match");
        Add("Option", "value", "label");
        // Phase 864 / 873 — the `rule-*` family carries the field's DECLARED constraint.
        // Every slot is optional and an entirely empty rule is refused by the wire, so the
        // translator emits none unless at least one slot is stated.
        Add("Field", "kind", "id", "label", "required", "initial", "help", "selected", "rows",
            "rule-format", "rule-pattern", "rule-min-length", "rule-max-length",
            "rule-compare-field", "rule-compare-op", "rule-message");
        Add("Filter", "kind", "name", "label");
        // Phase 750 — `tone-field` / `default-tone` accompany <Tone> children and turn the
        // column into a declarative TonedPill; `tone-field` is only needed when the tone is
        // driven by a DIFFERENT row property than the column displays.
        // Phases 861 / 863 / 873 — `sortable` / `editable` on a COLUMN narrow the grid's
        // behaviour and never widen it; absent means inherit, which is why neither has a
        // default here.
        Add("Column", "type", "label", "field", "tone-field", "default-tone", "sortable", "editable");
        Add("Tone", "value", "tone");
        // Phase 1110 — `srclang` is spelled as the HTML attribute is, not as the wire's
        // camelCase `srcLang`: this dialect is authored by people who know the element.
        Add("Track", "kind", "src", "srclang", "label", "default");
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
