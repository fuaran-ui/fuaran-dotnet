using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using FsApply = Fuaran.UI.Ops.Apply;
using FsApplyError = Fuaran.UI.Ops.Types.ApplyError;
using FsApplyErrorCode = Fuaran.UI.Ops.Types.ApplyErrorCode;
using FsNode = Fuaran.UI.Generated.Node<object>;
using FsNodeId = Fuaran.UI.Types.NodeId;
using FsOp = Fuaran.UI.Ops.Types.TreeOp<object>;

namespace Fuaran.UI.CSharp;

// ============================================================================
//  Phase 559 — C#-native apply ergonomics over the shared F# tree-op engine
//  (`Fuaran.UI.Ops.Apply.apply`). A decoded / constructed `FuaranOp` is applied
//  to a `FuaranNode`; the F# `Result<Node<obj>, ApplyError>` is projected to a
//  C# try-pattern + result object, and the F# `ApplyError` (code + hint) to a
//  C#-native record.
//
//  There is NO C# apply engine — this delegates to the single F# engine, so a
//  C#-applied tree is byte-for-byte what the F# host would produce. The op
//  constructors below build the canonical `TreeOp` DU cases directly (they carry
//  only ids / positions / whole nodes — no spec/binding coercion), so they are a
//  thin data facade, never a re-implementation of apply logic.
// ============================================================================

/// <summary>The canonical apply-error codes (§4d / §4g of the wire format).</summary>
public enum ApplyErrorCode
{
    /// <summary>The addressed node could not be found anywhere in the tree.</summary>
    NodeNotFound,

    /// <summary>The addressed parent could not be found.</summary>
    ParentNotFound,

    /// <summary>The addressed field does not exist on the target node's spec record.</summary>
    FieldNotFound,

    /// <summary>The addressed binding slot does not exist on the target node.</summary>
    SlotNotFound,

    /// <summary>The op targets a node whose kind is incompatible with the op.</summary>
    KindMismatch,

    /// <summary>The addressed kind has no <c>Children</c> field (a leaf kind).</summary>
    ChildlessKind,

    /// <summary>A position is outside the valid range.</summary>
    PositionOutOfRange,

    /// <summary>A reorder's new order is not a permutation of the current child ids.</summary>
    OrderingMismatch,

    /// <summary>An insert would introduce a NodeId already present in the tree.</summary>
    DuplicateNodeId,

    /// <summary>A path violates the §3.4 grammar.</summary>
    PathInvalid,

    /// <summary>A path is grammatical but the target kind has no typed traversal yet.</summary>
    PathNotSupportedYet,

    /// <summary>An inner op of a batch failed; see <see cref="ApplyError.BatchInnerIndex"/>.</summary>
    BatchAborted,

    /// <summary>A code outside the canonical set (forward-compat guard).</summary>
    Unknown,
}

/// <summary>A structured, C#-native apply error — no FSharp.Core result type on the surface.</summary>
public sealed record ApplyError
{
    /// <summary>The canonical code as a C# enum.</summary>
    public required ApplyErrorCode Code { get; init; }

    /// <summary>A human/AI-readable description of the failure.</summary>
    public required string Message { get; init; }

    /// <summary>Short kind name of the target node, when the engine could resolve it.</summary>
    public string? NodeKind { get; init; }

    /// <summary>Supported field / slot / path names on the target kind — the §4d recovery hint.</summary>
    public IReadOnlyList<string> AvailableFields { get; init; } = [];

    /// <summary>A free-text next-step suggestion, when the engine can name a likely fix.</summary>
    public string? Suggestion { get; init; }

    /// <summary>The 0-based index of the failing inner op, when <see cref="Code"/> is <see cref="ApplyErrorCode.BatchAborted"/>.</summary>
    public int? BatchInnerIndex { get; init; }

    internal static ApplyError FromFs(FsApplyError e) =>
        new()
        {
            Code = Classify(e.Code),
            Message = e.Message,
            NodeKind = Fs.ToNullable(e.Hint.NodeKind),
            AvailableFields = e.Hint.AvailableFields.ToList(),
            Suggestion = Fs.ToNullable(e.Hint.Suggestion),
            BatchInnerIndex = e.Code.IsBatchAborted ? ((FsApplyErrorCode.BatchAborted)e.Code).innerIndex : null,
        };

    private static ApplyErrorCode Classify(FsApplyErrorCode c) =>
        c switch
        {
            _ when c.IsNodeNotFound => ApplyErrorCode.NodeNotFound,
            _ when c.IsParentNotFound => ApplyErrorCode.ParentNotFound,
            _ when c.IsFieldNotFound => ApplyErrorCode.FieldNotFound,
            _ when c.IsSlotNotFound => ApplyErrorCode.SlotNotFound,
            _ when c.IsKindMismatch => ApplyErrorCode.KindMismatch,
            _ when c.IsChildlessKind => ApplyErrorCode.ChildlessKind,
            _ when c.IsPositionOutOfRange => ApplyErrorCode.PositionOutOfRange,
            _ when c.IsOrderingMismatch => ApplyErrorCode.OrderingMismatch,
            _ when c.IsDuplicateNodeId => ApplyErrorCode.DuplicateNodeId,
            _ when c.IsPathInvalid => ApplyErrorCode.PathInvalid,
            _ when c.IsPathNotSupportedYet => ApplyErrorCode.PathNotSupportedYet,
            _ when c.IsBatchAborted => ApplyErrorCode.BatchAborted,
            _ => ApplyErrorCode.Unknown,
        };
}

/// <summary>The outcome of an apply — exactly one of <see cref="Value"/> / <see cref="Error"/> is set.</summary>
public sealed class ApplyResult
{
    private ApplyResult(FuaranNode? value, ApplyError? error)
    {
        Value = value;
        Error = error;
    }

    /// <summary>Whether the apply succeeded.</summary>
    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsOk => Error is null;

    /// <summary>The new tree, or null on failure. On any error the input tree is unchanged (§4g).</summary>
    public FuaranNode? Value { get; }

    /// <summary>The apply error, or null on success.</summary>
    public ApplyError? Error { get; }

    internal static ApplyResult Ok(FuaranNode value) => new(value, null);

    internal static ApplyResult Fail(ApplyError error) => new(null, error);
}

/// <summary>
/// Canonical tree-op constructors — thin wrappers over the F# <c>TreeOp</c> DU
/// (structural cases only: ids, positions, whole nodes — no spec/binding
/// coercion). For field-level edits (<c>UpdateProp</c> / <c>ReplaceBinding</c>)
/// decode a canonical op from wire JSON with <see cref="Decode.Op"/> — that is
/// the shape an AI orchestrator emits, and it applies through the same engine.
/// </summary>
public static class Ops
{
    private static FsNodeId Id(string id) => FsNodeId.NewNodeId(id);

    /// <summary>Wholesale kind swap of the addressed node, taking the kind of <paramref name="replacement"/>.</summary>
    public static FuaranOp EditNode(string nodeId, FuaranNode replacement) =>
        new(FsOp.NewEditNode(Id(nodeId), replacement.Inner.Kind));

    /// <summary>Append <paramref name="child"/> under <paramref name="parentId"/>. To place it
    /// elsewhere, follow with <see cref="ReorderChildren"/> in a batch — membership and order are
    /// separate ops.</summary>
    public static FuaranOp InsertChild(string parentId, FuaranNode child) =>
        new(FsOp.NewInsertChild(Id(parentId), child.Inner));

    /// <summary>Remove the addressed node from its parent. Cannot remove the root.</summary>
    public static FuaranOp RemoveNode(string nodeId) => new(FsOp.NewRemoveNode(Id(nodeId)));

    /// <summary>Move the addressed node under a new parent, appending. To place it elsewhere,
    /// follow with <see cref="ReorderChildren"/> in a batch.</summary>
    public static FuaranOp MoveNode(string nodeId, string newParentId) =>
        new(FsOp.NewMoveNode(Id(nodeId), Id(newParentId)));

    /// <summary>Reorder a parent's children — <paramref name="newOrder"/> must be a permutation of the current child ids.</summary>
    public static FuaranOp ReorderChildren(string parentId, IEnumerable<string> newOrder) =>
        new(FsOp.NewReorderChildren(Id(parentId), Fs.List(newOrder.Select(Id))));

    /// <summary>Replace the entire tree with <paramref name="node"/> — the only op that legally changes the root.</summary>
    public static FuaranOp ReplaceRoot(FuaranNode node) => new(FsOp.NewReplaceRoot(node.Inner));

    /// <summary>All-or-nothing apply of an inner op list — the first failing inner op aborts the batch.</summary>
    public static FuaranOp Batch(IEnumerable<FuaranOp> ops) =>
        new(FsOp.NewBatch(Fs.List(ops.Select(o => o.Inner))));

    /// <summary>Decode a canonical wire-JSON tree op (alias for <see cref="Decode.Op"/>).</summary>
    public static DecodeResult<FuaranOp> FromJson(string json) => Decode.Op(json);
}

public static partial class Fuaran
{
    // ─── Apply (Phase 559) ──────────────────────────────────────────────────

    /// <summary>Apply a tree op to a node via the shared F# engine, returning the new tree
    /// or a structured error. The input <paramref name="node"/> is never mutated — on any
    /// error the engine returns the pre-op tree (§4g "revert is implicit").</summary>
    public static ApplyResult Apply(FuaranNode node, FuaranOp op)
    {
        var result = FsApply.apply(op.Inner, node.Inner);
        return result.IsOk
            ? ApplyResult.Ok(new FuaranNode(result.ResultValue))
            : ApplyResult.Fail(ApplyError.FromFs(result.ErrorValue));
    }

    /// <summary>Try-pattern apply: returns true + <paramref name="result"/> on success,
    /// false + <paramref name="error"/> on failure.</summary>
    public static bool TryApply(
        FuaranNode node,
        FuaranOp op,
        [NotNullWhen(true)] out FuaranNode? result,
        [NotNullWhen(false)] out ApplyError? error)
    {
        var r = Apply(node, op);
        result = r.Value;
        error = r.Error;
        return r.IsOk;
    }
}
