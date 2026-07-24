namespace Fuaran.UI.OpStream.Dag.Merge

open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops.Introspect
open Fuaran.UI.OpStream.Replay

// ============================================================================
//  ReassertPin — the R1 migration for `KindSwapOrphansPin` (Phase 179).
//
//  When a `Secondary`-side `EditNode` swaps a node's kind out from under a
//  primacy-pinned field, the pin is *destroyed* (the field has no home on the
//  new kind) rather than overwritten. `TreeMerge` surfaces this as a
//  `KindSwapOrphansPin` conflict (never a silent loss). The R1 resolution —
//  `ReassertPinOntoNewKind` — re-stamps the primary side's pinned value onto the
//  new kind WHEN the new kind has a name+type-compatible field.
//
//  The migration reuses existing, already-conformant machinery rather than a
//  bespoke field-by-field copier:
//
//   1. `TreeOpDiff.diff (kindOnly base) (kindOnly primary)` expresses the
//      primary side's kind-own field edits as GRANULAR ops. For a same-
//      discriminator edit the diff emits `UpdateProp` / `ReplaceBinding` (it only
//      falls back to a wholesale `EditNode` when it cannot express the change
//      granularly — which is exactly the "no migratable field" case we drop).
//   2. Each granular field op is re-applied onto a node bearing the secondary
//      side's NEW kind. The apply engine accepts an op iff the new kind has a field of that
//      name + compatible type, and rejects (drops) the rest — so name+type
//      compatibility falls out of the apply engine, no separate type oracle.
//
//  Returns the migrated childless kind only when ≥1 pinned field actually
//  migrated; `None` otherwise (the caller then offers `KeepOldKind`).
// ============================================================================

module ReassertPin =

    /// The childless kind (children neutralised) — kind-own fields only.
    let private childlessKind<'Msg> (n: Node<'Msg>) : NodeKind<'Msg> =
        match withChildren n.Kind [] with
        | Some k -> k
        | None -> n.Kind

    /// A node reduced to its kind-own fields: children, style, state and
    /// accessibility neutralised, so a base→primary diff isolates the primary
    /// side's kind-field edits (the pin) and a secondary shell carries only the new kind.
    let private kindOnly<'Msg> (n: Node<'Msg>) : Node<'Msg> =
        { n with
            Kind = childlessKind n
            Style = Defaults.style
            State = Defaults.stateBehaviour
            Accessibility = None }

    /// Re-stamp the primary side's pinned kind-own field values onto the
    /// secondary side's swapped kind. `base`/`primary` share a discriminator (the
    /// primary side kept the kind and edited a field — the pin); `secondary`
    /// carries the swapped kind. Returns the migrated **childless kind** when ≥1
    /// pinned field is name+type-compatible with the new kind (and so migrated),
    /// else `None`.
    let tryReassert<'Msg>
        (baseNode: Node<'Msg>)
        (primaryNode: Node<'Msg>)
        (secondaryNode: Node<'Msg>)
        : NodeKind<'Msg> option =
        // The primary side's kind-own field edits, as granular field ops only. A
        // wholesale EditNode floor (uncovered kind) carries no migratable field.
        let fieldOps =
            TreeOpDiff.diff (kindOnly baseNode) (kindOnly primaryNode)
            |> List.filter (fun op ->
                match op with
                | TreeOp.UpdateProp _
                | TreeOp.ReplaceBinding _ -> true
                | _ -> false)

        let migrated, anyApplied =
            fieldOps
            |> List.fold
                (fun (t: Node<'Msg>, applied: bool) op ->
                    match Fuaran.UI.Ops.Apply.apply op t with
                    | Ok t' -> t', true
                    | Error _ -> t, applied)
                (kindOnly secondaryNode, false)

        if anyApplied then Some(childlessKind migrated) else None
