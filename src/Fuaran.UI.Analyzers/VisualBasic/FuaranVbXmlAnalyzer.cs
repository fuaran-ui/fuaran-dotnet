using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Fuaran.UI.Analyzers.VisualBasic;

/// <summary>
/// The VB XML-literal compile-time gate. VB's baseline authoring surface is
/// runtime-translated (<c>FuaranXml.Translate</c>), so a typo'd element/attribute is
/// otherwise a runtime error — but Roslyn parses VB XML literals into syntax, so this
/// analyzer restores the safety at compile time:
///
///   * FUARAN060 — unknown element name (with a nearest-match suggestion);
///   * FUARAN061 — unknown attribute for the element's kind;
///   * FUARAN001 — duplicate NodeId across an XML-literal tree;
///   * FUARAN010 — a "$name" binding resolves against the manifest queries list.
///
/// Only literals whose OUTERMOST element is a Fuaran kind are analysed, so a non-Fuaran
/// VB XML literal is never touched.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.VisualBasic)]
public sealed class FuaranVbXmlAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            DiagnosticDescriptors.UnknownElement,
            DiagnosticDescriptors.UnknownAttribute,
            DiagnosticDescriptors.NodeIdUniqueness,
            DiagnosticDescriptors.QueryResolution);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(compStart =>
        {
            var manifest = Manifest.Load(compStart.Options);

            compStart.RegisterSyntaxNodeAction(
                ctx => AnalyzeElement(ctx, manifest),
                SyntaxKind.XmlElement,
                SyntaxKind.XmlEmptyElement);

            compStart.RegisterCodeBlockAction(AnalyzeBlockForDuplicateIds);
        });
    }

    private static void AnalyzeElement(SyntaxNodeAnalysisContext ctx, Manifest manifest)
    {
        if (!IsInsideFuaranLiteral(ctx.Node))
        {
            return;
        }

        var (nameNode, name) = ElementName(ctx.Node);
        if (name is null)
        {
            return;
        }

        // FUARAN060 — unknown element.
        if (!Vocabulary.IsKnownElement(name))
        {
            var suggestion = Vocabulary.Nearest(name);
            var hint = suggestion is null ? "" : $" — did you mean <{suggestion}>?";
            ctx.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.UnknownElement, nameNode!.GetLocation(), name, hint));
            return; // no point attribute-checking an unknown element
        }

        var allowed = Vocabulary.Attributes.TryGetValue(name, out var set) ? set : null;

        foreach (var attr in Attributes(ctx.Node))
        {
            var attrName = AttributeName(attr);
            if (attrName is null)
            {
                continue;
            }

            // FUARAN061 — unknown attribute (only for enumerated elements, to avoid false positives).
            if (allowed is not null && !allowed.Contains(attrName))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.UnknownAttribute, attr.GetLocation(), attrName, name));
            }

            // FUARAN010 — a "$query" binding resolves against the manifest.
            var value = AttributeValue(attr);
            if (manifest.Present && value is not null && value.StartsWith("$") && value.Length > 1)
            {
                var query = value.Substring(1);
                if (!manifest.Queries.Contains(query))
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.QueryResolution, attr.GetLocation(), query));
                }
            }
        }
    }

    private static void AnalyzeBlockForDuplicateIds(CodeBlockAnalysisContext ctx)
    {
        // Group id occurrences per outermost Fuaran XML literal in this block.
        var roots = ctx.CodeBlock.DescendantNodesAndSelf()
            .Where(n => n.IsKind(SyntaxKind.XmlElement) || n.IsKind(SyntaxKind.XmlEmptyElement))
            .Where(IsFuaranRoot);

        foreach (var root in roots)
        {
            var ids = new List<(string Id, Location Loc)>();
            foreach (var el in root.DescendantNodesAndSelf().Where(n => n.IsKind(SyntaxKind.XmlElement) || n.IsKind(SyntaxKind.XmlEmptyElement)))
            {
                var (_, name) = ElementName(el);
                if (name is null || !Vocabulary.Kinds.Contains(name))
                {
                    continue;
                }

                foreach (var attr in Attributes(el))
                {
                    if (AttributeName(attr) == "id" && AttributeValue(attr) is { Length: > 0 } id)
                    {
                        ids.Add((id, attr.GetLocation()));
                    }
                }
            }

            foreach (var group in ids.GroupBy(x => x.Id, System.StringComparer.Ordinal))
            {
                foreach (var dup in group.Skip(1))
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.NodeIdUniqueness, dup.Loc, dup.Id));
                }
            }
        }
    }

    // ── Syntax helpers (handle both <X>…</X> and self-closing <X/>) ─────────────

    private static bool IsFuaranRoot(SyntaxNode el)
    {
        // A Fuaran root is an XML element whose parent is not itself an XML element and whose name is a kind.
        if (el.Parent is not null && (el.Parent.IsKind(SyntaxKind.XmlElement) || el.Parent.IsKind(SyntaxKind.XmlEmptyElement)))
        {
            return false;
        }

        var (_, name) = ElementName(el);
        return name is not null && Vocabulary.Kinds.Contains(name);
    }

    private static bool IsInsideFuaranLiteral(SyntaxNode el)
    {
        SyntaxNode? current = el;
        while (current is not null)
        {
            if ((current.IsKind(SyntaxKind.XmlElement) || current.IsKind(SyntaxKind.XmlEmptyElement)) && IsFuaranRoot(current))
            {
                return true;
            }

            current = current.Parent;
        }

        return false;
    }

    private static (XmlNodeSyntax? NameNode, string? Name) ElementName(SyntaxNode el)
    {
        XmlNodeSyntax? nameNode = el switch
        {
            XmlElementSyntax e => e.StartTag.Name,
            XmlEmptyElementSyntax e => e.Name,
            _ => null,
        };
        return (nameNode, (nameNode as XmlNameSyntax)?.LocalName.ValueText);
    }

    private static IEnumerable<XmlAttributeSyntax> Attributes(SyntaxNode el)
    {
        var list = el switch
        {
            XmlElementSyntax e => e.StartTag.Attributes,
            XmlEmptyElementSyntax e => e.Attributes,
            _ => default,
        };
        return list.OfType<XmlAttributeSyntax>();
    }

    private static string? AttributeName(XmlAttributeSyntax attr) =>
        (attr.Name as XmlNameSyntax)?.LocalName.ValueText;

    private static string? AttributeValue(XmlAttributeSyntax attr) =>
        attr.Value is XmlStringSyntax s ? string.Concat(s.TextTokens.Select(t => t.ValueText)) : null;
}
