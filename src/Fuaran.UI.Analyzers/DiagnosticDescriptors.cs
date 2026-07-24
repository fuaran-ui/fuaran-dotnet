using Microsoft.CodeAnalysis;

namespace Fuaran.UI.Analyzers;

/// <summary>
/// The FUARAN* diagnostic descriptors — the same code numbers the F# validator
/// uses, so a diagnostic reads identically whether the tree was authored in F#,
/// C#, or (Phase 315) VB. Only the rules with a real veneer call-site surface are
/// ported; see each descriptor's remark for the F# provenance.
/// </summary>
internal static class DiagnosticDescriptors
{
    private const string Category = "Fuaran.UI";

    /// <summary>FUARAN001 — NodeId uniqueness (Error). Mirrors <c>NodeIdCheck.fs</c>.</summary>
    public static readonly DiagnosticDescriptor NodeIdUniqueness = new(
        id: "FUARAN001",
        title: "Duplicate NodeId",
        messageFormat: "Duplicate NodeId \"{0}\" — every NodeId in a Fuaran tree must be unique (§4g op-target stability)",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Every NodeId inside one Fuaran tree must be unique so tree-ops can target nodes stably (§4g).");

    /// <summary>FUARAN060 — unknown Fuaran XML element (VB XML-literal authoring, Phase 315).</summary>
    public static readonly DiagnosticDescriptor UnknownElement = new(
        id: "FUARAN060",
        title: "Unknown Fuaran element",
        messageFormat: "Unknown Fuaran element <{0}> — not a recognised node kind{1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A VB XML-literal element name must map to a shipped Fuaran node kind.");

    /// <summary>FUARAN061 — unknown attribute on a Fuaran XML element (VB XML-literal authoring, Phase 315).</summary>
    public static readonly DiagnosticDescriptor UnknownAttribute = new(
        id: "FUARAN061",
        title: "Unknown Fuaran attribute",
        messageFormat: "Attribute '{0}' is not recognised on a Fuaran <{1}> element",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A VB XML-literal attribute should map to a spec field of its element's kind.");

    /// <summary>FUARAN010 — Binding.Query name resolution (Warning). Mirrors <c>BindingResolution.fs</c>.</summary>
    public static readonly DiagnosticDescriptor QueryResolution = new(
        id: "FUARAN010",
        title: "Unresolved Binding.Query",
        messageFormat: "Unresolved Binding.Query \"{0}\" — name is not in the manifest queries list",
        category: Category,
        // Warning (not Error): the F# rule is silent when the manifest is absent; a
        // warning avoids failing a build that simply hasn't wired a manifest yet.
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A Binding.Query name should resolve against the module's fuaran-validator.manifest.json queries list.");
}
