module Fuaran.UI.Renderer.StateSeeds

// ============================================================================
//  Fuaran — the state-seeding pass both renderers run (Phase 1075; one
//  definition since Phase 2067).
//
//  A `Binding.State` carrying a `defaultValue` DECLARES the value of its slot
//  rather than merely falling back to it for itself, so a grid bound to
//  `$state.members` and a `Transform` deriving over the same key read the same
//  rows. The declared values are collected once for the whole tree and laid
//  UNDER whatever the caller already furnished.
//
//  **Precedence, per the shared-data-source charter §4:** a host-furnished
//  value (and, in a scoped render, a value written into the scope's store) wins
//  over a seed. A seed is the value before anything else has said anything,
//  never an override.
//
//  **Order-independent, per §5.** The pass runs over the whole tree before any
//  binding resolves, so document order carries no meaning.
//
//  Both renderers' context builders call `apply`, so the client's and the
//  server's first render read an identical `State` map, which is what keeps
//  hydration mismatch-free (`docs/SSR.md`).
//
//  Phase 2474 — the same pass lays a static table's DESIGNED selection under
//  `Selections` (`applySelections`), so both renderers' first render agree on
//  which row is selected too.
// ============================================================================

open Fuaran.UI.Types

/// Phase 2474 — every STATIC table in `node`, with its id.
let rec private staticTables<'Msg> (node: Node<'Msg>) : (string * DataGridSpec<'Msg> * StaticRows) list =
    let own =
        match node.Kind with
        | NodeKind.DataGrid grid ->
            match grid.StaticRows with
            | Some rows -> [ node.Id, grid, rows ]
            | None -> []
        | _ -> []

    own
    @ (Fuaran.UI.NodeChildren.children Fuaran.UI.NodeChildren.Reach.keyed node
       |> List.collect staticTables)

/// Phase 2474 — lay a static table's DESIGNED selection under
/// `sources.Selections`. A static table is selectable when its `rowKeyField` is
/// one of its declared columns (`GridModel.staticKeyColumn`); a `Selection` on
/// it projecting that field with a `defaultValue` names the selected row by its
/// key (`BindingWalk.selectionSeedsOf`, first declaration wins). The row is in
/// the tree, so the designed selection IS a selection of that row: its record
/// (`GridModel.staticRow`, exactly what a click on it publishes) goes under the
/// table's id, the table marks the row, and every reader of the selection reads
/// the row's own cells. A key matching no row seeds nothing, and every reader
/// keeps its own default (Phase 629).
///
/// A furnished selection wins, as a furnished state value does. A data-bound
/// grid is never seeded: its rows arrive at run time, so nothing in the tree can
/// say which of them a key names.
let private applySelections<'Msg>
    (node: Node<'Msg>)
    (declared: Map<string * string, obj>)
    (sources: BindingResolver.BindingSources)
    : BindingResolver.BindingSources =
    if Map.isEmpty declared then
        sources
    else
        let text = BindingResolver.resolveTextSource sources

        staticTables node
        |> List.fold
            (fun (acc: BindingResolver.BindingSources) (id, grid, sr) ->
                match GridModel.staticKeyColumn grid.Columns grid.RowKeyField, grid.RowKeyField with
                | Some keyAt, Some field when not (Map.containsKey (NodeId id) acc.Selections) ->
                    match Map.tryFind (id, field) declared with
                    | Some value ->
                        // The key as text, through the projection the row-key
                        // floor reads a bound grid's key with.
                        let key =
                            Fuaran.UI.HostPrelude.RowProjection.string_ (Map.ofList [ field, value ]) field

                        sr.Rows
                        |> List.tryPick (fun row ->
                            let texts = row |> List.map text

                            if List.tryItem keyAt texts = Some key then
                                Some(GridModel.staticRow grid.Columns texts)
                            else
                                None)
                        |> function
                            | Some row ->
                                { acc with
                                    Selections = Map.add (NodeId id) (row :> obj) acc.Selections }
                            | None -> acc
                    | None -> acc
                | _ -> acc)
            sources

/// Lay `node`'s declared state seeds under `sources.State`: a furnished value
/// wins over a seed. Zero-cost when the tree declares nothing: the caller's
/// sources come back unchanged, by reference.
///
/// Phase 2474 — and a static table's designed selection under
/// `sources.Selections` (`applySelections`), from the same walk.
let apply<'Msg> (node: Node<'Msg>) (sources: BindingResolver.BindingSources) : BindingResolver.BindingSources =
    let facts = Fuaran.UI.BindingWalk.collect node
    let seeds = Fuaran.UI.BindingWalk.stateSeedsOf facts

    let seeded =
        if Map.isEmpty seeds then
            sources
        else
            { sources with
                State = sources.State |> Map.fold (fun acc k v -> Map.add k v acc) seeds }

    applySelections node (Fuaran.UI.BindingWalk.selectionSeedsOf facts) seeded
