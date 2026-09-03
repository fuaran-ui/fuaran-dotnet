using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Fuaran.UI.Analyzers.VisualBasic;

namespace Fuaran.UI.Analyzers.VisualBasic.Tests;

// ============================================================================
//  The VB authoring surface's CHILD-ELEMENT table, pinned to the IDL.
//
//  `AuthoringSurfacePin` pins `Vocabulary.Attributes` — what an element may
//  carry as an XML ATTRIBUTE — and stops exactly where the attribute stops. A
//  wire field whose type is structured has no attribute spelling by
//  construction; `IsAttributeEligible` says so, and that pin then declares the
//  absence and moves on. But those fields are not unauthorable: most of them
//  are authored as a CHILD ELEMENT — `<Option>` inside `<Select>`, `<Column>`
//  inside `<DataGrid>`, `<Case>` inside `<Switch>` — and the set of element
//  names that carries them is `Vocabulary.Structural`, which was pinned against
//  nothing at all. It is the same hand-maintained-mirror class one level down.
//
//  WHAT GOES WRONG WHEN IT DRIFTS, in both directions:
//
//    - A name MISSING from `Structural` is not merely unchecked. `IsKnownElement`
//      is `Kinds ∪ Structural`, and `AnalyzeElement` reports FUARAN060 and
//      RETURNS on anything outside it — so a structural child the translator
//      happily reads is reported to the author as an unknown element, and the
//      attribute leg for it never runs at all. Found by this pin on its first
//      run: `<Tone>` — read by the translator since Phase 750
//      (`ChildElements(c, "Tone")`), carrying a row in `Vocabulary.Attributes`,
//      declared in `AuthoringSurfacePin.NonKindElements` as a structural child —
//      and absent from `Structural`, so every valid use of it raised FUARAN060.
//    - A name PRESENT in `Structural` whose wire slot has gone is the opposite
//      failure: the analyzer silently admits an element nothing will translate,
//      and the author learns at run time.
//
//  WHY A PIN AND NOT GENERATION, same answer as the attribute table's. The
//  child-element vocabulary is not the wire's: it is singular where the wire
//  field is plural (`<Column>` for `columns`), it renames (`<Source>` for
//  `srcSet`, `<Marker>` for `source`), it spells the ELEMENT of a list that has
//  no field of its own (`<Cell>`, inside a row), and it reaches inside a record
//  and a union arm (`<Header>` / `<Row>` into `StaticRows`, `<Tone>` into the
//  `TonedPill` arm of a column's `kind`). A generator would need a mapping table
//  the size of the thing it generated. So the artefact stays hand-authored, and
//  the CORRESPONDENCE is what gets declared — each element naming the IDL slot
//  it spells, each unspelled structured field naming why it has no element.
//
//  WHAT IS DERIVED ANYWAY, so it needs no declaration:
//
//    - An ATTRIBUTE-ELIGIBLE field is the attribute pin's subject, not this
//      one's. `IsAttributeEligible` is re-used verbatim from there rather than
//      re-stated, so the two pins cannot disagree about where the boundary is.
//    - A CLOSURE (`fn`) is unauthorable in XML in any shape — neither an
//      attribute nor a child element can carry one — so it is out of scope for
//      both pins.
//    - A `children` field of `list<node>` is the GENERIC child list: it is
//      authored as the element's own nested content with no wrapper element at
//      all, which is why eight kinds carry one and none of them has a
//      `<Children>`. That rule collapses eight raw differences; only the
//      remaining thirty-one are declared below.
//
//  As in the attribute pin, every declaration is ORPHAN-REFUSING: a declaration
//  that has stopped being true is a defect rather than dead weight, because the
//  whole value of an enumeration is that it is complete.
// ============================================================================
internal static class StructuralElementPin
{
    /// <summary>Each structural child element and the IDL slot it spells, as
    /// `Kind.field` or `Record.field`. Both forms are resolved against the
    /// artefact, so a declaration naming a slot that has been renamed or retired
    /// fails here. The note says what the correspondence is when it is not the
    /// obvious singular-of-the-field-name.</summary>
    private static readonly IReadOnlyDictionary<string, (string Anchor, string Note)> SpelledSlots =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            // Singular-of-the-field — the ordinary case.
            ["Item"] = ("List.items", "one item of the list"),
            ["Field"] = ("Form.fields", "one FormField"),
            ["Filter"] = ("Filters.items", "one FilterSpec; the element is named for the domain, the field for the shape"),
            ["Column"] = ("DataGrid.columns", "one ColumnErased"),
            ["Case"] = ("Switch.cases", "one SwitchCase"),

            // Renames — the element name and the wire field name differ.
            ["Source"] = ("Image.srcSet", "rename — one SrcSetEntry of the srcSet list (Phase 1080)"),
            ["Track"] = ("Media.tracks", "singular-of-the-field — one TrackEntry of the tracks list (Phase 1110)"),
            ["Option"] = ("Select.source", "rename — one SelectOption; the literal children build the Binding the `source` field carries"),
            ["Marker"] = ("Map.source", "rename — one MapMarker; the literal children build the Binding the `source` field carries"),
            ["Prop"] = ("Custom.props", "rename — one entry of the props map, authored as name/value"),

            // Node-valued slots — the child element names the slot rather than a
            // repetition of it, which is why these are singular in the wire too.
            ["Child"] = ("ErrorBoundary.child", "the guarded subtree"),
            ["Fallback"] = ("ErrorBoundary.fallback", "the subtree shown when the guard trips"),
            ["Body"] = ("FragmentDecl.body", "the declared fragment's subtree"),
            ["Default"] = ("Switch.default", "the subtree taken when no case matches"),

            // Slots that live inside a RECORD reached from a kind field, so the
            // anchor is the record's field rather than the kind's.
            ["Header"] = ("StaticRows.headers", "one header cell of the <Table> convenience's StaticRows record"),
            ["Row"] = ("StaticRows.rows", "one row of the <Table> convenience's StaticRows record"),
            ["Cell"] = (
                "StaticRows.rows",
                "the INNER element of the rows list — a row IS a list of cells, so the wire has no separate field to anchor and this shares <Row>'s"),
            ["Tone"] = (
                "ColumnErased.kind",
                "one entry of the `map` on the TonedPill arm of a column's kind union (Phase 750) — the deepest anchor here, and the one whose absence from Structural this pin found"),
        };

    /// <summary>Structured wire fields with NO child-element spelling, and why.
    /// Three classes, worth telling apart: a FLATTENING or a NARROWING or a SUGAR
    /// family is authorable, just as attributes — those entries cross-reference
    /// the attribute pin's tables; a GAP is an authoring capability the VB tier
    /// does not have at all.</summary>
    private static readonly IReadOnlyDictionary<string, string> NotChildAuthorable =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Authorable as attributes — see AuthoringSurfacePin.AttributeOnly,
            // which declares the attribute side of each of these.
            ["Box.layout"] = "flattening — the convenience elements' `orientation` / `wrap` / `cols` attributes",
            ["Chart.source"] = "narrowed — the `source` attribute carries a query name, widened to a Binding on translation",
            ["Chart.yFields"] = "narrowed — `y-fields`, a comma-separated string",
            ["Chart.valueFormat"] = "sugar — the `value-format-*` attribute family",
            ["Custom.exposedNodeIds"] = "narrowed — `exposed-node-ids`, a comma-separated string",
            ["DataGrid.defaultSort"] = "flattening — `default-sort-column` + `default-sort-direction`",
            ["DataGrid.source"] = "narrowed — the `source` attribute carries a query name",
            ["DataGrid.staticRows"] = "spelled INDIRECTLY — the <Table> convenience builds this record, and its parts are the <Header> / <Row> / <Cell> elements, whose anchors sit inside it",
            ["Drawing.viewBox"] = "flattening — `min-x` / `min-y` / `width` / `height`",
            ["FileUpload.accept"] = "narrowed — `accept`, a comma-separated string",
            ["LabelValueRow.format"] = "sugar — the shared `format-*` attribute family",
            ["Media.kind"] = "flattening — `kind` selects the variant; `autoplay` / `poster` are its video-branch slots",
            ["Metric.format"] = "sugar — the shared `format-*` attribute family",
            ["Metric.trendFormat"] = "sugar — the shared `format-*` attribute family",
            ["Mount.capabilities"] = "narrowed — `capabilities`, a comma-separated string",
            ["Embed.permissions"] = "narrowed — `permissions`, a pipe-separated string of relaxation names; a list of bare enum tokens does not earn a child element (Phase 1111)",
            ["Mount.channel"] = "flattening — `two-way` + `message-shape`",
            ["Select.values"] = "narrowed — the <MultiSelect> convenience's `values` attribute",
            ["Sparkline.source"] = "narrowed — the `source` attribute carries a query name",

            // Gaps — no VB spelling at all, attribute or element. Every one of
            // these was undocumented before this pin measured it.
            ["Button.onClick"] = "GAP — the VB tier spells no Action vocabulary, in either shape",
            ["Form.onSubmit"] = "GAP — the VB tier spells no Action vocabulary, in either shape",
            ["Modal.onDismiss"] = "GAP — the VB tier spells no Action vocabulary, in either shape",
            ["CodeBlock.highlightLines"] = "GAP — no VB spelling",
            ["Custom.contentHash"] = "GAP — no VB spelling",
            ["Drawing.shapes"] = "GAP — no VB spelling; a Drawing's shapes are unauthorable from the XML dialect, which spells only its viewBox and titling",
            ["Drawing.style"] = "GAP — no VB spelling",
            ["FragmentDecl.holes"] = "GAP — no VB spelling",
            ["FragmentDecl.effect"] = "GAP — no VB spelling",
            ["FragmentRef.args"] = "GAP — no VB spelling",
            ["Mount.inputs"] = "GAP — no VB spelling",
            ["Tabs.tabHeaders"] = "GAP — labels are child-inferred; this is the surface the TRIPWIRE comment in Vocabulary.Attributes reserves",
            ["Tabs.tabTags"] = "GAP — no VB spelling; the tag half of the same TRIPWIRE",
        };

    /// <summary>Whether this field is the generic node-child list — authored as
    /// the element's own nested content, with no wrapper element by design.</summary>
    private static bool IsPlainNodeChildren(string field, JsonElement type) =>
        field == "children"
        && type.GetProperty("$type").GetString() == "list"
        && type.GetProperty("of").GetProperty("$type").GetString() == "node";

    /// <summary>Whether the field is a closure — unauthorable in XML in any
    /// shape, so neither pin quantifies over it.</summary>
    private static bool IsClosure(JsonElement type) => type.GetProperty("$type").GetString() == "fn";

    public static void Run(Action<string, bool, string?> check, string? idlPath)
    {
        if (idlPath is null)
        {
            // The caller has already reported the skip for the attribute pin;
            // saying it twice adds nothing.
            return;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(idlPath));
        var root = doc.RootElement;

        // Slot index: "Kind.field" and "Record.field" alike, mapped to the field's
        // type element, so an anchor resolves the same way whichever it names.
        var slots = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var kindTags = new HashSet<string>(StringComparer.Ordinal);

        foreach (var k in root.GetProperty("kinds").EnumerateArray())
        {
            var tag = k.GetProperty("tag").GetString()!;
            kindTags.Add(tag);
            foreach (var f in k.GetProperty("fields").EnumerateArray())
            {
                slots[$"{tag}.{f.GetProperty("name").GetString()}"] = f.GetProperty("type");
            }
        }

        foreach (var r in root.GetProperty("records").EnumerateArray())
        {
            var name = r.GetProperty("name").GetString()!;
            foreach (var f in r.GetProperty("fields").EnumerateArray())
            {
                slots[$"{name}.{f.GetProperty("name").GetString()}"] = f.GetProperty("type");
            }
        }

        // ── Direction 1 — every recognised structural element declares a slot.
        var undeclaredElements = Vocabulary.Structural
            .Where(e => !SpelledSlots.ContainsKey(e))
            .ToList();

        check(
            "structural pin: every structural element declares the IDL slot it spells",
            undeclaredElements.Count == 0,
            $"undeclared: [{string.Join(", ", undeclaredElements.OrderBy(x => x, StringComparer.Ordinal))}]. "
            + "Add it to StructuralElementPin.SpelledSlots naming the `Kind.field` or `Record.field` it authors, "
            + "or remove it from Vocabulary.Structural — an element in that set is one the analyzer admits without checking anything.");

        // ── Direction 1, orphan legs — the declaration is still true.
        var danglingAnchors = SpelledSlots
            .Where(kv => !slots.ContainsKey(kv.Value.Anchor))
            .Select(kv => $"{kv.Key} -> {kv.Value.Anchor}")
            .ToList();

        check(
            "structural pin: every declared anchor still resolves in the IDL",
            danglingAnchors.Count == 0,
            $"these name a slot the vocabulary no longer has: [{string.Join(", ", danglingAnchors.OrderBy(x => x, StringComparer.Ordinal))}]");

        var orphanDeclarations = SpelledSlots.Keys
            .Where(e => !Vocabulary.Structural.Contains(e))
            .ToList();

        check(
            "structural pin: no declaration for an element the analyzer does not recognise",
            orphanDeclarations.Count == 0,
            $"declared here and absent from Vocabulary.Structural: [{string.Join(", ", orphanDeclarations.OrderBy(x => x, StringComparer.Ordinal))}]");

        // ── Direction 2 — every structured wire field is spelled or declared.
        //    This is the leg that sees a NEW field arrive: the legs above
        //    quantify over the VB table, so a field with no element is invisible
        //    to them.
        var anchored = new HashSet<string>(SpelledSlots.Values.Select(v => v.Anchor), StringComparer.Ordinal);
        var undeclaredFields = new List<string>();
        var liveUnspelled = new HashSet<string>(StringComparer.Ordinal);

        foreach (var k in root.GetProperty("kinds").EnumerateArray())
        {
            var tag = k.GetProperty("tag").GetString()!;
            foreach (var f in k.GetProperty("fields").EnumerateArray())
            {
                var field = f.GetProperty("name").GetString()!;
                var type = f.GetProperty("type");

                if (AuthoringSurfacePin.IsAttributeEligible(type)
                    || IsClosure(type)
                    || IsPlainNodeChildren(field, type))
                {
                    continue;
                }

                var key = $"{tag}.{field}";
                if (anchored.Contains(key))
                {
                    continue;
                }

                liveUnspelled.Add(key);
                if (!NotChildAuthorable.ContainsKey(key))
                {
                    undeclaredFields.Add(key);
                }
            }
        }

        check(
            "structural pin: every structured wire field has a child element or a declared divergence",
            undeclaredFields.Count == 0,
            $"undeclared: [{string.Join(", ", undeclaredFields.OrderBy(x => x, StringComparer.Ordinal))}]. "
            + "Give it a child element (Vocabulary.Structural + the VB Mapping + the C# factory) and declare the "
            + "correspondence in StructuralElementPin.SpelledSlots, or declare in NotChildAuthorable why it has none.");

        var orphanDivergences = NotChildAuthorable.Keys
            .Where(k => !liveUnspelled.Contains(k))
            .ToList();

        check(
            "structural pin: no orphaned not-child-authorable declaration",
            orphanDivergences.Count == 0,
            $"these are now spelled, or are no longer structured wire fields: [{string.Join(", ", orphanDivergences.OrderBy(x => x, StringComparer.Ordinal))}]");

        // ── Direction 3 — the RECOGNITION leg, and the reason `Tone` was found.
        //    `AnalyzeElement` reports FUARAN060 and RETURNS on any element outside
        //    `Kinds ∪ Structural`, so an attribute row for an unrecognised element
        //    is unreachable code AND a false positive on valid authoring. Neither
        //    the attribute pin nor the two directions above can see it: the
        //    attribute pin quantifies over the same table, and the IDL knows
        //    nothing about which elements the analyzer admits.
        var unreachableRows = Vocabulary.Attributes.Keys
            .Where(e => !Vocabulary.IsKnownElement(e))
            .ToList();

        check(
            "structural pin: every attribute-table element is one the analyzer recognises",
            unreachableRows.Count == 0,
            $"unrecognised: [{string.Join(", ", unreachableRows.OrderBy(x => x, StringComparer.Ordinal))}]. "
            + "FUARAN060 fires on these and the attribute row is never reached — add the name to Vocabulary.Structural "
            + "if the translator reads it as a child element, or drop the row if the element is retired.");

        // ── The vacuity guard. Every assertion above is a "no undeclared
        //    divergence" claim, which an empty or wrongly-shaped artefact
        //    satisfies perfectly. Pin the shape of what was actually read.
        check(
            "structural pin: the IDL was actually read",
            kindTags.Count >= 30 && slots.Count >= 150 && Vocabulary.Structural.Count >= 10,
            $"read {kindTags.Count} kinds / {slots.Count} slots from {idlPath}, against {Vocabulary.Structural.Count} structural elements — too few to be the real vocabulary");
    }
}
