module Fuaran.UI.Renderer.TreeModel

// ============================================================================
//  Fuaran — the `Tree` kind's MODEL (Phase 2070): which rows are open, which
//  row is selected, which rows a reader can see, and which row carries the
//  widget's one tab stop.
//
//  Pure: every function is a total function of values the caller holds. The
//  two State READS (`readExpandedItems`, `readSelectedItem`) stay in
//  `BindingResolver`, where reading a key is the job, and decode what they read
//  through the two decoders here; `BindingResolver` also re-exports every name
//  that moved, for one release. Both render legs call this module, and they
//  must answer identically or a hydrating client would replace the server's
//  rows. Compiled ahead of `BindingResolver` so that module can re-export it.
// ============================================================================

open Fuaran.UI.Types
open Fuaran.Core

/// Phase 1120 — the rows a `Tree` currently has OPEN, read off the named State
/// key. The shape the specification fixes for that slot is a JSON ARRAY OF ROW
/// IDS, and this is the one reader for it, shared by both render legs so a
/// server rendering and a client rendering can never disagree about which rows
/// are open.
///
/// An array and not a per-row map, because the question the host asks is set
/// membership over a hierarchy it walks, and a set has one spelling where a map
/// of booleans has two for "closed" (absent, and present-and-false) that a
/// reader would then have to be told apart.
///
/// Every non-conforming shape reads as the EMPTY SET rather than as an error:
/// a key holding a number, an object, or an array with non-string members is a
/// host's state slot, not a wire document, so there is nothing here to refuse
/// and refusing it would blank a tree over a value the reader never authored.
/// Non-string members are dropped individually for the same reason — a slot
/// holding `["a", 3, "b"]` still says two true things.
let expandedOfJVal (jv: JVal) : Set<string> =
    match jv with
    | JArr items ->
        items
        |> List.choose (function
            | JStr s -> Some s
            | _ -> None)
        |> Set.ofList
    | _ -> Set.empty

/// Phase 1120 — the row a `Tree` currently has SELECTED, read off the named
/// State key. The fixed shape is a bare row-id STRING, so `None` covers both an
/// unset key and a key holding anything else, on `expandedOfJVal`'s reasoning.
///
/// Single-selection only in this cut. A multi-selection tree would hold an
/// array here and be indistinguishable on the wire from the expansion slot,
/// which is exactly the ambiguity a fixed shape exists to prevent; admitting it
/// is a separate decision with its own key.
let selectedOfJVal (jv: JVal) : string option =
    match jv with
    | JStr s when s <> "" -> Some s
    | _ -> None

/// Phase 1120 — is this row's SUBTREE shown? The two callers are the two render
/// legs, and they must answer identically or a hydrating client would replace
/// the server's rows.
///
/// A tree naming NO expansion key renders FULLY EXPANDED, and that is the
/// grid-behaviour rule read straight across rather than a special case: a grid
/// with no sort state key still honours its declared order, because an initial
/// presentation without interactive re-ordering is a legitimate shape. The same
/// sentence with "expansion" in it gives a static, fully-visible hierarchy —
/// which is what a document that never asked for a toggle means, and is the
/// only reading under which such a tree shows its content at all.
let treeItemExpanded (expandedKeyNamed: bool) (expanded: Set<string>) (item: TreeItem) : bool =
    not (List.isEmpty item.Children)
    && (not expandedKeyNamed || Set.contains item.Id expanded)

/// Phase 1120 — every row a reader can currently see, in document order: the
/// roots, and the descendants of every open row. Shared so the roving tabindex
/// below and each leg's DOM walk agree on what "the next row" means.
let rec visibleTreeItems (expandedKeyNamed: bool) (expanded: Set<string>) (items: TreeItem list) : TreeItem list =
    items
    |> List.collect (fun item ->
        item
        :: (if treeItemExpanded expandedKeyNamed expanded item then
                visibleTreeItems expandedKeyNamed expanded item.Children
            else
                []))

/// Phase 1120 — which row carries `tabindex="0"`.
///
/// The WAI-ARIA tree pattern is ONE tab stop, not one per row, so exactly one
/// visible row is in the sequential focus order and the arrow keys move within
/// the widget. That is the property no `List` + `Disclosure` composition has,
/// and it is computed HERE, off state alone, so the server's rendering and the
/// client's first frame put it on the same row.
///
/// The selected row wins when it is visible; otherwise the first visible row
/// does. A selection scrolled out of view by its parent being closed is not a
/// focus target — tabbing into the widget must land somewhere the reader can
/// see, and the first row is where the pattern starts.
let focusableTreeItem
    (expandedKeyNamed: bool)
    (expanded: Set<string>)
    (selected: string option)
    (items: TreeItem list)
    : string option =
    let visible = visibleTreeItems expandedKeyNamed expanded items

    match selected with
    | Some s when visible |> List.exists (fun i -> i.Id = s) -> Some s
    | _ -> visible |> List.tryHead |> Option.map _.Id
