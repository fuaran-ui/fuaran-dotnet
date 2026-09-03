using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Fuaran.UI.Analyzers.VisualBasic;

namespace Fuaran.UI.Analyzers.VisualBasic.Tests;

// ============================================================================
//  Phase 1104 — the VB authoring surface pinned to the IDL at FIELD level.
//
//  The §11 step-6 forward-coupling rule already had a guard at KIND level, on
//  every one of the three authoring surfaces: the C# factory set and the VB
//  element set are each pinned by reflection against the generated `NodeKind`
//  DU, and the analyzer's `Vocabulary.Kinds` is pinned against the translator's
//  `FuaranXml.KnownElements()`. So a new KIND cannot reach the wire without a
//  factory and an element.
//
//  A new FIELD could, and did. `Vocabulary.Attributes` — the analyzer's
//  per-element allowed-attribute table, 54 rows — was pinned against nothing at
//  all: an element absent from it is allow-any, and an element PRESENT in it
//  with a stale attribute set silently refuses an attribute the wire accepts
//  (FUARAN061) or admits one it does not. Nothing could tell you which. The
//  measured residue when this pin was written was seventeen wire fields with no
//  attribute spelling, none of them recorded anywhere.
//
//  WHY A PIN AND NOT GENERATION. The obvious fix — emit `Attributes` from the
//  IDL — is wrong for this surface, and the fuaran#704 sync-check pattern is
//  the precedent. The VB attribute vocabulary is deliberately NOT the wire's:
//  it is kebab-case where the wire is camelCase, it FLATTENS records the author
//  should not have to nest (`two-way` + `message-shape` for one `channel`
//  record; `min-x` / `min-y` / `width` / `height` for one `viewBox`), it carries
//  sugar families the wire has no field for (`format-currency` and its three
//  siblings for one `CellFormat` union), and it spells several element
//  CONVENIENCES the wire has no kind for at all (`<Card>` and `<Stack>` both
//  emit `Box`). Generating it would either destroy that ergonomics or require a
//  mapping table as large as the thing it generated. So the artefact stays
//  hand-authored and the DIVERGENCE is what gets declared.
//
//  WHAT IS DERIVED ANYWAY. Most of the raw difference is mechanical and needs no
//  declaration: a field whose IDL type is structured (list / record / node / map
//  / closure / opaque) has no attribute spelling BY CONSTRUCTION — it takes a
//  child-element shape, or it is a closure, which no XML attribute can carry.
//  `IsAttributeEligible` encodes that rule, and it collapses ninety raw
//  differences to forty real ones. Only the forty are declared below, each with
//  a reason, and a declaration that stops being true is a DEFECT rather than
//  dead weight — the `spec-annotations.json` posture from the WIRE_FORMAT
//  projection: the whole value of an enumeration is that it is complete, so an
//  entry whose subject has been spelled, renamed or retired fails here rather
//  than quietly outliving it.
// ============================================================================
internal static class AuthoringSurfacePin
{
    /// <summary>Elements in the attribute table that name no wire kind, with why.
    /// Authoring conveniences and structural child elements — the field pin does
    /// not apply to them, and saying so explicitly is what stops a genuinely
    /// unknown element from being read as one of these.</summary>
    private static readonly IReadOnlyDictionary<string, string> NonKindElements = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Dashboard"] = "convenience — emits Box with role Dashboard",
        ["Stack"] = "convenience — emits Box with a Flex layout",
        ["Grid"] = "convenience — emits Box with a Grid layout",
        ["Card"] = "convenience — emits Box with role Card",
        ["Divider"] = "convenience — emits Box with role Separator",
        ["Table"] = "convenience — emits the static-rows DataGrid the retired Table kind expressed",
        ["MultiSelect"] = "convenience — emits Select with multiple set",
        ["Case"] = "structural child of <Switch>",
        ["Option"] = "structural child of <Select> / <Filter>",
        ["Field"] = "structural child of <Form>",
        ["Filter"] = "structural child of <Filters>",
        ["Column"] = "structural child of <DataGrid>",
        ["Tone"] = "structural child of <Column>",
        ["Marker"] = "structural child of <Map>",
        ["Prop"] = "structural child of <Custom>",
        ["Track"] = "structural child of <Media>",
    };

    /// <summary>Attribute-eligible wire fields the VB surface does NOT spell, with
    /// why. Two classes, and they are worth telling apart when reading this table:
    /// a RENAME (the field is authorable under another name) is a closed question;
    /// a GAP is an authoring capability the VB tier does not have, and every one
    /// of the gaps below was undocumented before this pin measured it.</summary>
    private static readonly IReadOnlyDictionary<string, string> UnspelledEligible = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Renames — spelled, under a different attribute name.
        ["Map.centre-latitude"] = "rename — spelled `centre-lat`",
        ["Map.centre-longitude"] = "rename — spelled `centre-lng`",
        ["Switch.on"] = "rename — spelled `state-key`; the generated projection merges the wire's on/stateKey pair into one slot",

        // Gaps — no VB spelling at all.
        ["Button.disabled"] = "GAP — no VB spelling",
        ["Button.icon"] = "GAP — no VB spelling",
        ["Form.disabled"] = "GAP — no VB spelling",
        ["Select.disabled"] = "GAP — no VB spelling",
        ["Select.multiple"] = "GAP — no VB spelling; <MultiSelect> is the multi-select convenience",
        ["FileUpload.disabled"] = "GAP — no VB spelling",
        ["Link.protection"] = "GAP — no VB spelling",
        ["Metric.icon"] = "GAP — no VB spelling",
        ["Metric.subtext"] = "GAP — no VB spelling",
        ["Metric.emphasis"] = "GAP — no VB spelling",
        ["Metric.weight"] = "GAP — no VB spelling",
        ["DataGrid.reorderable"] = "GAP — no VB spelling",
        ["DataGrid.row-key-field"] = "GAP — no VB spelling",
        ["Tabs.active-tag"] = "GAP — no VB spelling",
    };

    /// <summary>Attributes the VB surface admits that are not attribute-eligible
    /// wire fields, with why. Every one is a deliberate ergonomic choice: a record
    /// flattened into its parts, a sugar family, or a narrowed spelling of a field
    /// whose full wire type an attribute could not carry.</summary>
    private static readonly IReadOnlyDictionary<string, string> AttributeOnly = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Record flattenings — one wire record authored as its scalar parts.
        ["Mount.two-way"] = "flattening — with `message-shape`, authors the GuestChannel record",
        ["Mount.message-shape"] = "flattening — with `two-way`, authors the GuestChannel record",
        ["DataGrid.default-sort-column"] = "flattening — with `default-sort-direction`, authors the DefaultSort record",
        ["DataGrid.default-sort-direction"] = "flattening — with `default-sort-column`, authors the DefaultSort record",
        ["Drawing.min-x"] = "flattening — one part of the ViewBox record",
        ["Drawing.min-y"] = "flattening — one part of the ViewBox record",
        ["Drawing.width"] = "flattening — one part of the ViewBox record",
        ["Drawing.height"] = "flattening — one part of the ViewBox record",
        ["Media.kind"] = "flattening — selects the MediaKind variant (video | audio)",
        ["Media.autoplay"] = "flattening — a slot of MediaKind.Video, read only on the video branch",
        ["Media.poster"] = "flattening — a slot of MediaKind.Video, read only on the video branch",

        // Sugar families — several attributes authoring one union-valued field.
        ["Chart.value-format-currency"] = "sugar — one arm of the Format union on `valueFormat`",
        ["Chart.value-format-number"] = "sugar — one arm of the Format union on `valueFormat`",
        ["Chart.value-format-percent"] = "sugar — one arm of the Format union on `valueFormat`",

        // Narrowed spellings — the attribute carries a restricted form of a field
        // whose full wire type is structured.
        ["Chart.source"] = "narrowed — a query name, widened to Binding on translation",
        ["DataGrid.source"] = "narrowed — a query name, widened to Binding on translation",
        ["Sparkline.source"] = "narrowed — a query name, widened to Binding on translation",

        // Renames — the attribute side of an entry in UnspelledEligible above.
        ["Map.centre-lat"] = "rename — spells the wire's `centreLatitude`",
        ["Map.centre-lng"] = "rename — spells the wire's `centreLongitude`",
        ["Chart.y-fields"] = "narrowed — a comma-separated string authoring a list<str>",
        ["Mount.capabilities"] = "narrowed — a comma-separated string authoring a list<str>",
        ["Custom.exposed-node-ids"] = "narrowed — a comma-separated string authoring a list<str>",
        ["FileUpload.accept"] = "narrowed — a comma-separated string authoring a list<str>",
        ["Embed.permissions"] = "narrowed — a pipe-separated string authoring a list<EmbedPermission> (Phase 1111)",
    };

    /// <summary>Wire kinds with NO row in the attribute table, with why. An
    /// element absent from that table is ALLOW-ANY, so this is the direction that
    /// catches a whole new kind: without it, a kind can enter the vocabulary,
    /// reach the analyzer unchecked, and raise nothing here — the attribute legs
    /// quantify over the VB table and a kind that is not in it is invisible to
    /// them.</summary>
    private static readonly IReadOnlyDictionary<string, string> NoAttributeTable = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Box"] = "authored only through its four conveniences (<Dashboard> / <Stack> / <Grid> / <Card>), each of which has its own row",
    };

    /// <summary>Attributes present on every element and owned by the node envelope
    /// rather than by any spec, plus the four-strong CellFormat sugar family that
    /// the table builds with `WithFormat`.</summary>
    private static readonly HashSet<string> EnvelopeOrShared = new(StringComparer.Ordinal)
    {
        "id", "format-currency", "format-number", "format-percent", "format-date",
    };

    /// <summary>Whether a wire field of this IDL type could be authored as an XML
    /// ATTRIBUTE at all. Scalars and enums can; a `TextSource` or a `Binding` over
    /// a scalar can (the translator lifts the string). Everything else — a list, a
    /// record, a node, a map, a closure, an opaque or hosted payload — takes a
    /// child-element shape or is unauthorable, so its absence from the attribute
    /// table is structural rather than an omission.</summary>
    /// <remarks>`internal` rather than private so `StructuralElementPin` re-uses
    /// this rule verbatim instead of re-stating it. The two pins partition the
    /// field space between them — attribute-eligible here, structured there — and
    /// two copies of the boundary would eventually disagree about which pin owns
    /// a field, leaving it checked by neither.</remarks>
    internal static bool IsAttributeEligible(JsonElement type)
    {
        var kind = type.GetProperty("$type").GetString();
        switch (kind)
        {
            case "str":
            case "int":
            case "bool":
            case "float":
            case "enum":
                return true;
            case "union":
                var name = type.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (name == "TextSource")
                {
                    return true;
                }

                if (name == "Binding" && type.TryGetProperty("args", out var args) && args.GetArrayLength() == 1)
                {
                    var inner = args[0].GetProperty("$type").GetString();
                    return inner is "str" or "int" or "bool" or "float";
                }

                return false;
            default:
                return false;
        }
    }

    /// <summary>camelCase wire field name to the kebab-case attribute spelling the
    /// VB surface uses throughout.</summary>
    private static string Kebab(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsUpper(c) && sb.Length > 0)
            {
                sb.Append('-');
            }

            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    /// <summary>Locate the wire-format corpus: `FUARAN_WIRE_CORPUS`, else the
    /// nearest enclosing `wire-format-fixtures/idl.json`. Returns null when the
    /// clone is absent (a single-repo checkout), which the caller reports LOUDLY
    /// rather than passing quietly — "nothing to check" must not read as
    /// "everything checked".</summary>
    internal static string? FindIdl()
    {
        var declared = Environment.GetEnvironmentVariable("FUARAN_WIRE_CORPUS");
        if (!string.IsNullOrWhiteSpace(declared))
        {
            var direct = Path.Combine(declared, "idl.json");
            return File.Exists(direct) ? direct : null;
        }

        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 12 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "wire-format-fixtures", "idl.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        return null;
    }

    public static void Run(Action<string, bool, string?> check)
    {
        var idlPath = FindIdl();
        if (idlPath is null)
        {
            Console.WriteLine(
                "[vb-analyzer-tests] SKIPPING the Phase 1104 authoring-surface field pin — "
                + "wire-format-fixtures/idl.json absent (single-repo checkout). Set FUARAN_WIRE_CORPUS to point at the corpus.");
            return;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(idlPath));

        // tag -> (kebab attribute name -> attribute-eligible?)
        var idlKinds = doc.RootElement.GetProperty("kinds").EnumerateArray().ToDictionary(
            k => k.GetProperty("tag").GetString()!,
            k => k.GetProperty("fields").EnumerateArray()
                .ToDictionary(f => Kebab(f.GetProperty("name").GetString()!), f => IsAttributeEligible(f.GetProperty("type"))),
            StringComparer.Ordinal);

        var liveUnspelled = new HashSet<string>(StringComparer.Ordinal);
        var liveAttributeOnly = new HashSet<string>(StringComparer.Ordinal);
        var undeclaredUnspelled = new List<string>();
        var undeclaredAttributeOnly = new List<string>();
        var unknownElements = new List<string>();

        foreach (var (element, attrs) in Vocabulary.Attributes)
        {
            if (!idlKinds.TryGetValue(element, out var fields))
            {
                // Not a wire kind. It must be a DECLARED convenience or child
                // element — an undeclared one is a vocabulary row nobody can
                // account for, which is exactly what this pin is for.
                if (!NonKindElements.ContainsKey(element))
                {
                    unknownElements.Add(element);
                }

                continue;
            }

            // Direction 1 — an attribute-eligible wire field with no attribute.
            foreach (var (field, eligible) in fields)
            {
                if (!eligible || attrs.Contains(field))
                {
                    continue;
                }

                var key = $"{element}.{field}";
                liveUnspelled.Add(key);
                if (!UnspelledEligible.ContainsKey(key))
                {
                    undeclaredUnspelled.Add(key);
                }
            }

            // Direction 2 — an attribute that is not an attribute-eligible field.
            foreach (var attr in attrs)
            {
                if (EnvelopeOrShared.Contains(attr) || (fields.TryGetValue(attr, out var e) && e))
                {
                    continue;
                }

                var key = $"{element}.{attr}";
                liveAttributeOnly.Add(key);
                if (!AttributeOnly.ContainsKey(key))
                {
                    undeclaredAttributeOnly.Add(key);
                }
            }
        }

        check(
            "authoring-surface pin: every attribute-eligible wire field is spelled or declared unspelled",
            undeclaredUnspelled.Count == 0,
            $"undeclared: [{string.Join(", ", undeclaredUnspelled.OrderBy(x => x, StringComparer.Ordinal))}]. "
            + "Add the attribute to Vocabulary.Attributes (and to the VB Mapping + the C# factory options), "
            + "or declare it in AuthoringSurfacePin.UnspelledEligible with a reason.");

        check(
            "authoring-surface pin: every VB attribute is a wire field or a declared divergence",
            undeclaredAttributeOnly.Count == 0,
            $"undeclared: [{string.Join(", ", undeclaredAttributeOnly.OrderBy(x => x, StringComparer.Ordinal))}]. "
            + "Declare it in AuthoringSurfacePin.AttributeOnly with a reason, or remove it — an attribute "
            + "the wire has no field for is admitted by the analyzer and refused by the translator.");

        check(
            "authoring-surface pin: every attribute-table element is a wire kind or a declared convenience",
            unknownElements.Count == 0,
            $"undeclared: [{string.Join(", ", unknownElements.OrderBy(x => x, StringComparer.Ordinal))}]");

        // Direction 3 — a wire kind with no row at all. The two legs above
        // quantify over the VB table, so a brand-new kind is invisible to both:
        // it has no row, so no attribute of it can be undeclared, and no field of
        // it can be unspelled. This is the leg that sees it.
        var kindsWithNoRow = idlKinds.Keys
            .Where(k => !Vocabulary.Attributes.ContainsKey(k) && !NoAttributeTable.ContainsKey(k))
            .ToList();

        check(
            "authoring-surface pin: every wire kind has an attribute row or a declared exemption",
            kindsWithNoRow.Count == 0,
            $"unrowed: [{string.Join(", ", kindsWithNoRow.OrderBy(x => x, StringComparer.Ordinal))}]. "
            + "An element absent from Vocabulary.Attributes is ALLOW-ANY, so the analyzer checks nothing about it. "
            + "Add its row (and its VB Mapping entry + C# factory), or declare it in AuthoringSurfacePin.NoAttributeTable with a reason.");

        var orphanNoRow = NoAttributeTable.Keys
            .Where(k => !idlKinds.ContainsKey(k) || Vocabulary.Attributes.ContainsKey(k))
            .ToList();

        check(
            "authoring-surface pin: no orphaned no-attribute-table declaration",
            orphanNoRow.Count == 0,
            $"these now have a row, or are no longer wire kinds: [{string.Join(", ", orphanNoRow.OrderBy(x => x, StringComparer.Ordinal))}]");

        // The orphan legs. A declaration that has stopped being true is a defect,
        // not dead weight: it is a claim about the surface that nothing checks any
        // more, and the value of the two tables above is that they are complete.
        var orphanUnspelled = UnspelledEligible.Keys.Where(k => !liveUnspelled.Contains(k)).ToList();
        var orphanAttributeOnly = AttributeOnly.Keys.Where(k => !liveAttributeOnly.Contains(k)).ToList();
        var orphanElements = NonKindElements.Keys
            .Where(k => idlKinds.ContainsKey(k) || !Vocabulary.Attributes.ContainsKey(k))
            .ToList();

        check(
            "authoring-surface pin: no orphaned unspelled-field declaration",
            orphanUnspelled.Count == 0,
            $"these no longer diverge and the declaration is stale: [{string.Join(", ", orphanUnspelled.OrderBy(x => x, StringComparer.Ordinal))}]");

        check(
            "authoring-surface pin: no orphaned attribute-only declaration",
            orphanAttributeOnly.Count == 0,
            $"these no longer diverge and the declaration is stale: [{string.Join(", ", orphanAttributeOnly.OrderBy(x => x, StringComparer.Ordinal))}]");

        check(
            "authoring-surface pin: no orphaned convenience-element declaration",
            orphanElements.Count == 0,
            $"these are now wire kinds, or are no longer in the attribute table: [{string.Join(", ", orphanElements.OrderBy(x => x, StringComparer.Ordinal))}]");

        // The vacuity guard. Every assertion above is a "no undeclared
        // divergence" claim, which a table read from an empty or wrongly-shaped
        // artefact satisfies perfectly. Pin the shape of what was actually read.
        check(
            "authoring-surface pin: the IDL was actually read",
            idlKinds.Count >= 30 && idlKinds.Values.Sum(f => f.Count) >= 150,
            $"read {idlKinds.Count} kinds / {idlKinds.Values.Sum(f => f.Count)} fields from {idlPath} — too few to be the real vocabulary");
    }
}
