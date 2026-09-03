module Fuaran.UI.Renderer.GridTransfer

// ============================================================================
//  Fuaran — cross-container transfer: moving a row from one grid to another
//  (Phase 1123)
//
//  `DataGridSpec.TransferOutKey` and `.TransferInKey` are the ONLY things on
//  the wire here, and between them they say one thing: THESE TWO GRIDS
//  EXCHANGE ROWS. Which gesture moves a row, what the drag image looks like,
//  which keys do it, what a drop zone looks like while a compatible row is over
//  it — none of that reaches the vocabulary, under the affordance→op charter's
//  governing sentence as Phase 1123 extended it (`docs/VOCABULARY.md`,
//  Appendix A, Interaction / affordance cluster).
//
//  ── The extension, and the completion rule that came with it ─────────────
//  The charter's sentence named "the node that both hosts the gesture and
//  consumes its effect" — SINGULAR. A transfer has two nodes and neither one
//  alone consumes it, so the sentence gained one clause: where a gesture spans
//  two nodes, the wire names the capability on BOTH ENDS as a shared key each
//  declares its own side of, and the effect is ONE record written to that key.
//
//  The record alone would have been a FAKE AFFORDANCE, which is the thing this
//  cluster exists to foreclose: the reader drags, an object appears in State,
//  and no row moves. So the renderer owes the APPLICATION as well as the
//  gesture — on drop it writes the record AND commits each half through that
//  end's own already-shipped write destination (`BindingResolver`'s one
//  `gridWriteDestination` rule: a declared `editStateKey`, else the Phase-663
//  `State`-source floor, else nothing). No second write path is minted, and
//  every write still crosses `writeBackTo`'s tree-state-write gate and
//  host-reserved-key guard.
//
//  Where an end has NO writable destination — a `Transform`-sourced column, a
//  `Query`-sourced one — that half simply is not applied, and the record is
//  still written. That is not a gap: the canonical board's columns are filtered
//  views over one collection, so on the case the feature exists for there is
//  nothing to write and the record is the whole outcome, for the application to
//  act on. One mechanism whose second half degrades honestly.
//
//  ── Why the decisions are PURE functions ─────────────────────────────────
//  Everything this file decides — whether two ends pair, where an item lands,
//  what a key press means, what the reader is told — is a total function of its
//  arguments and touches no browser. The .NET test runner mounts no DOM, so a
//  judgement made inline in an event handler could only ever be ASSERTED about
//  in prose; pulled out, each one is pinned by a test that can fail. The
//  `SwitchStage` shape (Phase 1122), for the same reason.
//
//  ── Accessibility ────────────────────────────────────────────────────────
//  A drag has no keyboard equivalent and none is invented. What is provided is
//  a SECOND ROUTE to the same effect, and it composes with an affordance that
//  already shipped: `Control+X` on a row's handle LIFTS the row, the target
//  grid's own place control puts it at the end of that list, and Phase 934's
//  arrow keys on the handle then move it to any position. Two shipped
//  affordances reach, between them, everywhere the pointer reaches.
//
//  The chord is advertised (`aria-keyshortcuts` on the handle) and every
//  transition is announced through a `role="status"` line, because an
//  undiscoverable shortcut is another fake affordance — the reason the charter
//  declined `KeyboardShortcut` by that name.
// ============================================================================

open Fuaran.Core

/// A row lifted for a keyboard move, or dragged with a pointer. Carries the
/// row VALUE as well as its identity because the target grid performs the
/// insert and never sees the source grid's rows, and a release closure because
/// the target performs the removal too — the source's own destination resolved
/// at the source, so neither end reaches into the other's binding.
///
/// `OutKey` is an option: a drag begun on a grid that declares only
/// `reorderable` carries none, and is consumable by that grid's own reorder
/// path alone.
type Lifted<'Row> =
    { SourceNodeId: string
      SourceIndex: int
      ItemId: string
      OutKey: string option
      Row: 'Row
      Release: (unit -> unit) option }

/// What a key press on a transfer handle means. `Ignore` is the overwhelming
/// majority and is a case rather than an option so a new intent cannot be added
/// without every reader seeing it.
[<RequireQualifiedAccess>]
type TransferIntent =
    /// Take this row out of its list and hold it (`Control+X`).
    | Lift
    /// Put the held row down, unheld (`Escape`).
    | Cancel
    | Ignore

/// `Control+X` (or `Command+X` on a platform that reports `metaKey`) lifts;
/// `Escape` cancels, with no modifier, because a reader who wants out of a
/// state should not have to remember a chord to get there.
///
/// Deliberately NOT `x` alone: a bare letter would be captured from anything
/// focusable and would collide with type-ahead in the surrounding page.
let keyIntent (key: string) (modifier: bool) : TransferIntent =
    match key, modifier with
    | ("x" | "X"), true -> TransferIntent.Lift
    | "Escape", _ -> TransferIntent.Cancel
    | _ -> TransferIntent.Ignore

/// Do two declarations name the same transfer key? Absence on either side is
/// NOT a match: a grid that declares nothing releases to nothing and accepts
/// nothing, which is every grid authored before this release.
let accepts (outKey: string option) (inKey: string option) : bool =
    match outKey, inKey with
    | Some o, Some i -> o = i && o <> ""
    | _ -> false

/// May this target receive that source's lifted row? The key must pair AND the
/// two ends must be different nodes: a grid dropping onto itself is a REORDER,
/// which Phase 934 already owns, and routing it through the transfer path would
/// give one gesture two implementations.
let canReceive (lifted: Lifted<'Row>) (targetNodeId: string) (targetInKey: string option) : bool =
    lifted.SourceNodeId <> targetNodeId && accepts lifted.OutKey targetInKey

/// Where an incoming row lands in the target's full set. Clamped rather than
/// refused: an index past the end is an append, which is what a drop below the
/// last row means, and a negative one is the top.
let clampInsert (index: int) (count: int) : int =
    if index < 0 then 0
    elif index > count then count
    else index

/// Insert `item` at `index` (already clamped by the caller, and clamped again
/// here so the function is total on its own).
let insertAt (index: int) (item: 'a) (xs: 'a list) : 'a list =
    let i = clampInsert index (List.length xs)
    (List.truncate i xs) @ (item :: List.skip i xs)

/// Remove the item at `index`. Out of range returns the list UNCHANGED — the
/// caller writes the result back wholesale, so "invalid removal writes nothing
/// new" and "invalid removal is refused" are the same behaviour, with no
/// partial state in between. `BindingResolver.moveRow`'s rule, restated for the
/// one-sided operation.
let removeAt (index: int) (xs: 'a list) : 'a list =
    if index < 0 || index >= List.length xs then
        xs
    else
        List.mapi (fun i x -> i, x) xs
        |> List.filter (fun (i, _) -> i <> index)
        |> List.map snd

/// The transfer record, exactly as `WIRE_FORMAT.md` §3.6.11 specifies it:
/// `{"itemId","from","to","index"}`, all four always present. Built here rather
/// than at the call site so the one shape the specification fixes has one
/// construction in this host.
///
/// `from` / `to` are NODE IDS — identity within the tree, which every rendered
/// surface already carries — and never store addresses. `index` is the 0-based
/// position the row took in the target's full set.
let record (itemId: string) (fromNodeId: string) (toNodeId: string) (index: int) : JVal =
    JObj
        [ "itemId", JStr itemId
          "from", JStr fromNodeId
          "to", JStr toNodeId
          "index", JInt index ]

/// What the reader is told when a row is lifted. Names the row and the route
/// out, because a lift with no announcement is a mode change a screen-reader
/// user cannot detect.
let liftAnnouncement (itemId: string) : string =
    sprintf
        "Lifted %s. Move to the list you want it in and activate its place control, or press Escape to cancel."
        itemId

/// What the reader is told when a row lands. Names the position as well as the
/// list, because "moved" without a position leaves the reader to go and look.
let placeAnnouncement (itemId: string) (index: int) (count: int) : string =
    sprintf "Moved %s to position %d of %d." itemId (index + 1) count

/// What the reader is told when a place control is activated with nothing
/// lifted. A refusal that says how to succeed, rather than silence — the
/// control is a real affordance whose precondition was not met, and silence
/// would make it read as broken.
let nothingLiftedAnnouncement: string =
    "Nothing is lifted. Press Control+X on a row's reorder handle first."

/// What the reader is told when a lift is cancelled.
let cancelAnnouncement: string = "Cancelled. The row stayed where it was."
