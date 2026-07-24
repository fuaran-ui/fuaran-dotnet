namespace Fuaran.UI.Telemetry.Abstractions

open Fuaran.UI.Ops.Types

// ============================================================================
//  OpKind — case-name projection of `TreeOp<'Msg>` for telemetry aggregation.
//
//  Telemetry records carry the op KIND (a flat enum) rather than the typed
//  `TreeOp<'Msg>` value itself: aggregates ("success rate by op kind",
//  "p95 latency by op kind", "top decoder-rejection reasons by op kind")
//  index on the kind discriminator, and the typed payload is irrelevant
//  for the metric. Decoupling the telemetry shape from `'Msg` also lets
//  hosts ship one metric pipeline across multiple apps regardless of
//  each app's `'Msg` type.
//
//  Mirrors the ten `TreeOp` cases verbatim (§4g lines 908–920); when a new
//  `TreeOp` case lands, `OpKind` gains the matching case AND `ofTreeOp`
//  gains the matching arm. Exhaustiveness in `ofTreeOp` is enforced via
//  `TreatWarningsAsErrors` — adding a `TreeOp` case without updating
//  `ofTreeOp` fails the build.
// ============================================================================

[<RequireQualifiedAccess>]
type OpKind =
    | EditNode
    | UpdateProp
    | ReplaceBinding
    | UpdateStyle
    | UpdateState
    | InsertChild
    | RemoveNode
    | MoveNode
    | ReorderChildren
    | ReplaceRoot
    | Batch

[<RequireQualifiedAccess>]
module OpKind =
    /// Project a typed `TreeOp<'Msg>` to its flat `OpKind` discriminator.
    /// Exhaustive across the ten §4g cases.
    let ofTreeOp (op: TreeOp<'Msg>) : OpKind =
        match op with
        | TreeOp.EditNode _ -> OpKind.EditNode
        | TreeOp.UpdateProp _ -> OpKind.UpdateProp
        | TreeOp.ReplaceBinding _ -> OpKind.ReplaceBinding
        | TreeOp.UpdateStyle _ -> OpKind.UpdateStyle
        | TreeOp.UpdateState _ -> OpKind.UpdateState
        | TreeOp.InsertChild _ -> OpKind.InsertChild
        | TreeOp.RemoveNode _ -> OpKind.RemoveNode
        | TreeOp.MoveNode _ -> OpKind.MoveNode
        | TreeOp.ReorderChildren _ -> OpKind.ReorderChildren
        | TreeOp.ReplaceRoot _ -> OpKind.ReplaceRoot
        | TreeOp.Batch _ -> OpKind.Batch

    /// Stable short name for the kind — used as a string key in the drift
    /// detector's aggregate maps and as a `printfn` formatter. Matches the
    /// `$type` discriminator the canonical-JSON encoder emits for the
    /// corresponding `TreeOp` case.
    let name (kind: OpKind) : string =
        match kind with
        | OpKind.EditNode -> "EditNode"
        | OpKind.UpdateProp -> "UpdateProp"
        | OpKind.ReplaceBinding -> "ReplaceBinding"
        | OpKind.UpdateStyle -> "UpdateStyle"
        | OpKind.UpdateState -> "UpdateState"
        | OpKind.InsertChild -> "InsertChild"
        | OpKind.RemoveNode -> "RemoveNode"
        | OpKind.MoveNode -> "MoveNode"
        | OpKind.ReorderChildren -> "ReorderChildren"
        | OpKind.ReplaceRoot -> "ReplaceRoot"
        | OpKind.Batch -> "Batch"
