using System.Text.Json;

namespace Fuaran.UI.CSharp;

// ============================================================================
//  Phase 307 — accessibility-defaults posture. MIRROR F# BY CONSTRUCTION.
//
//  The veneer maintains NO independent C# ARIA-defaults table. Because the
//  factories couple to the F# smart constructors (Phase 304), a C#-authored node
//  inherits exactly the per-component `accessibility` shape the F# host emits —
//  `Defaults.Accessibility.X` flows through the smart-ctor call. This file is
//  therefore thin: a read-only projection of the inherited ARIA (for inspection)
//  plus the documented reconciliation decision. There is nothing here to keep in
//  sync with F# — the values ARE F#'s.
//
//  ── Corpus reconciliation decision (cross-host) ─────────────────────────────
//  The shared wire-format-fixtures corpus is authored BARE: every node fixture is
//  built via the F# test `node` helper with `Accessibility = None`, so zero
//  fixtures carry an `accessibility` key. The SMART CONSTRUCTORS, by contrast,
//  inject per-component ARIA (`metric` → liveRegion polite, `card`/`dashboard` →
//  role region/main, `button` → role button, …). So a tree authored via any
//  smart-ctor host (F#, and now the C# veneer) diverges from the bare fixture by
//  exactly that injected ARIA.
//
//  DECISION: keep the corpus bare — do NOT regenerate it. Regenerating to carry
//  ARIA is a coordinated F#/TS/Python change with no wire-contract benefit (ARIA
//  is render-visible metadata, already covered by the a11y-contract corpus). The
//  veneer's byte-identity target is therefore the F# SMART-CTOR HOST, asserted by
//  the mirror-parity test (Conformance suite), not the bare file. Corpus-driven
//  conformance uses the decode round-trip (bare in → bare out), which is
//  ARIA-agnostic. See Phase 306 / roadmap phase 307.
// ============================================================================

/// <summary>
/// A read-only C#-native projection of the ARIA a node carries — inherited by
/// construction from the F# smart constructors (there is no independent C# table).
/// </summary>
public sealed record NodeAccessibility
{
    /// <summary>The ARIA role, if any (e.g. "region", "button", "main").</summary>
    public string? Role { get; init; }

    /// <summary>The <c>aria-live</c> politeness, if any ("polite" / "assertive" / "off").</summary>
    public string? LiveRegion { get; init; }
}

/// <summary>Inspection helpers for the ARIA a node inherited from the F# smart constructors.</summary>
public static class Accessibility
{
    /// <summary>
    /// The canonical <c>accessibility</c> JSON fragment the node carries (as it
    /// appears on the wire), or null when the node emits no ARIA. Read from the
    /// node's own encoding, so it is exactly what a conformant host sees.
    /// </summary>
    public static string? AriaJson(FuaranNode node)
    {
        using var doc = JsonDocument.Parse(node.Encode());
        return doc.RootElement.TryGetProperty("accessibility", out var a) ? a.GetRawText() : null;
    }

    /// <summary>A structured projection of the node's inherited ARIA, or null when it emits none.</summary>
    public static NodeAccessibility? Describe(FuaranNode node)
    {
        using var doc = JsonDocument.Parse(node.Encode());
        if (!doc.RootElement.TryGetProperty("accessibility", out var a))
        {
            return null;
        }

        return new NodeAccessibility
        {
            Role = a.TryGetProperty("role", out var r) ? r.GetString() : null,
            LiveRegion = a.TryGetProperty("liveRegion", out var l) ? l.GetString() : null,
        };
    }
}
