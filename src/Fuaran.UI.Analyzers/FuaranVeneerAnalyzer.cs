using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Fuaran.UI.Analyzers;

/// <summary>
/// The compile-time gate for the Fuaran authoring veneer. Analyses the
/// language-neutral <see cref="IOperation"/> tree (so the same analyzer serves C#
/// and — once Phase 315 adds <c>LanguageNames.VisualBasic</c> below — VB), porting
/// the high-value F# validator rules to veneer call sites:
///
///   * FUARAN001 — NodeId uniqueness across a code block (op-target stability).
///   * FUARAN010 — Binding.Query names resolve against the manifest queries list.
///
/// Rules whose author-facing slot the veneer does not surface (FUARAN020
/// Action.Dispatch, FUARAN042 LocalBinding, FUARAN047/048 Tab headers/tags) have
/// no C# call site to walk — the veneer's handlers are wire-opaque no-ops and it
/// surfaces no local-binding / tab-header authoring — so they are intentionally
/// not emitted here (they would fire on nothing). They activate if/when those
/// slots gain an author surface; the manifest reader already carries MsgCases for
/// FUARAN020's future.
/// </summary>
// Phase 315 — LanguageNames.VisualBasic added: the IOperation rules are language-
// neutral by design, so they now serve a VB author who calls the C# FACTORIES
// directly (the VB XML-literal surface is checked separately by FuaranVbXmlAnalyzer).
[DiagnosticAnalyzer(LanguageNames.CSharp, LanguageNames.VisualBasic)]
public sealed class FuaranVeneerAnalyzer : DiagnosticAnalyzer
{
    private const string FactoryContainer = "Fuaran.UI.CSharp.Fuaran";
    private const string NodeReturnType = "Fuaran.UI.CSharp.FuaranNode";
    private const string BindingContainer = "Fuaran.UI.CSharp.Binding";

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.NodeIdUniqueness, DiagnosticDescriptors.QueryResolution);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(compStart =>
        {
            var manifest = Manifest.Load(compStart.Options);

            // FUARAN010 — Binding.Query name resolution (compilation-wide).
            compStart.RegisterOperationAction(ctx => AnalyzeQuery(ctx, manifest), OperationKind.Invocation);

            // FUARAN001 — NodeId uniqueness, scoped to each code block (a tree proxy).
            compStart.RegisterOperationBlockStartAction(blockStart =>
            {
                var ids = new ConcurrentBag<(string Id, Location Loc)>();

                blockStart.RegisterOperationAction(
                    ctx =>
                    {
                        if (TryGetFactoryNodeId((IInvocationOperation)ctx.Operation, out var id, out var loc))
                        {
                            ids.Add((id, loc));
                        }
                    },
                    OperationKind.Invocation);

                blockStart.RegisterOperationBlockEndAction(blockEnd =>
                {
                    foreach (var group in ids.GroupBy(x => x.Id, System.StringComparer.Ordinal))
                    {
                        // The first occurrence is the "original"; every later one is a duplicate.
                        foreach (var dup in group.Skip(1))
                        {
                            blockEnd.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.NodeIdUniqueness, dup.Loc, dup.Id));
                        }
                    }
                });
            });
        });
    }

    private static void AnalyzeQuery(OperationAnalysisContext ctx, Manifest manifest)
    {
        if (!manifest.Present)
        {
            return; // silent when no manifest is wired (matches the F# validator)
        }

        var inv = (IInvocationOperation)ctx.Operation;
        var m = inv.TargetMethod;
        if (m.Name != "Query" || m.ContainingType?.ToDisplayString() != BindingContainer)
        {
            return;
        }

        var nameArg = inv.Arguments.FirstOrDefault(a => a.Parameter?.Name == "name");
        if (nameArg?.Value.ConstantValue is { HasValue: true, Value: string qname }
            && !manifest.Queries.Contains(qname))
        {
            ctx.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.QueryResolution, nameArg.Value.Syntax.GetLocation(), qname));
        }
    }

    /// <summary>
    /// True when <paramref name="op"/> is a veneer factory call (<c>Fuaran.X(new() { Id = "…" })</c>)
    /// with a constant-string <c>Id</c>; yields that id + the location of its literal.
    /// </summary>
    private static bool TryGetFactoryNodeId(IInvocationOperation op, out string id, out Location loc)
    {
        id = string.Empty;
        loc = Location.None;

        var m = op.TargetMethod;
        if (m.ContainingType?.ToDisplayString() != FactoryContainer
            || m.ReturnType?.ToDisplayString() != NodeReturnType)
        {
            return false;
        }

        // The single options argument carries the Id in its object initializer.
        var creation = op.Arguments
            .Select(a => Unwrap(a.Value))
            .OfType<IObjectCreationOperation>()
            .FirstOrDefault();

        var assignment = creation?.Initializer?.Initializers
            .OfType<ISimpleAssignmentOperation>()
            .FirstOrDefault(a => a.Target is IPropertyReferenceOperation { Property.Name: "Id" });

        if (assignment?.Value.ConstantValue is { HasValue: true, Value: string s })
        {
            id = s;
            loc = assignment.Value.Syntax.GetLocation();
            return true;
        }

        return false;
    }

    private static IOperation Unwrap(IOperation op)
    {
        while (op is IConversionOperation c)
        {
            op = c.Operand;
        }

        return op;
    }
}
