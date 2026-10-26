module Fuaran.UI.Renderer.GridModel

// ============================================================================
//  Fuaran — the data-bound grid's MODEL (Phase 2070): sort order, pages, the
//  row window and the viewport that writes it, the toned-pill lowering, the
//  whole-rows write destinations, and the row move.
//
//  None of this is binding resolution, which is why it no longer lives in
//  `BindingResolver`: every function here is a total function of values a
//  caller already holds — a descriptor `JVal`, a row list, a spec — and reads
//  no store. The store READS (`readSortDescriptor`, `readPageDescriptor`,
//  `readWindowDescriptor`, `resolveRowTotal`, `gridPage`, `gridWindow`) stay in
//  `BindingResolver`, where reading a `State` key is the job, and validate what
//  they read through the decoders here.
//
//  One implementation serves the client renderer, the SSR renderer and the
//  grid adapter, so two surfaces cannot disagree about which rows are on page 3
//  or where a moved row lands. Compiled ahead of `BindingResolver`, which reads
//  the State keys that feed it.
// ============================================================================

open Fuaran.UI.Types
open Fuaran.Core

// ─── Data-bound grid sort (Phase 818 — `sortStateKey`) ───────────────────────
//
// A data-bound grid whose spec names a `sortStateKey` sorts its RESOLVED rows
// by the state-carried descriptor `{"column": <header index>, "direction":
// "asc" | "desc"}` before rendering — runtime-side sort, the author wires no
// Transform. One implementation serves the client renderer and any SSR host
// that seeds the key, so two surfaces cannot disagree about ordering rules
// (the `tonedPillOf` single-definition rationale). The rules mirror the
// Phase-801 reference table enhancement where they overlap: empty cells sort
// LAST in both directions (unmeasured is not zero), ties keep their authored
// relative order (`List.sortWith` is stable).

// `SortDirection` is spelled `Fuaran.UI.Types.SortDirection` throughout this section: this file opens
// `Fuaran.Core` AFTER `Fuaran.UI.Types`, and from Core 0.36.0 `Fuaran.Core.Query` declares a
// `SortDirection` of its own (Phase 398), which a Fable program compiling this source beside that
// package would otherwise resolve here.

/// Validate a sort descriptor value: `{"column": <header index>, "direction":
/// "asc" | "desc"}`. Every part is validated rather than trusted — a malformed
/// descriptor reads as "no sort" so the authored order stands (never an
/// arbitrary one).
let sortDescriptorOfJVal (jv: JVal) : (int * Fuaran.UI.Types.SortDirection) option =
    match jv with
    | JObj fields ->
        let col =
            fields
            |> List.tryPick (function
                | ("column", JInt i) -> Some i
                | ("column", JFloat f) when floor f = f -> Some(int f)
                | _ -> None)

        let dir =
            fields
            |> List.tryPick (function
                | ("direction", JStr "asc") -> Some Fuaran.UI.Types.SortDirection.Asc
                | ("direction", JStr "desc") -> Some Fuaran.UI.Types.SortDirection.Desc
                | _ -> None)

        (match col, dir with
         | Some c, Some d when c >= 0 -> Some(c, d)
         | _ -> None)
    | _ -> None

// A decimal ranks beside the floats, after them: a column is one type in practice, and
// ordering the two populations against each other would need a float, which is the one
// thing a decimal's order must never go through.
let private cellSortRank (v: CellValue) : int =
    match v with
    | CellValue.Numeric _ -> 0
    | CellValue.Decimal _ -> 1
    | CellValue.Bool _ -> 2
    | CellValue.Date _ -> 3
    | CellValue.Text _ -> 4
    | CellValue.Empty -> 5

let private compareCells (a: CellValue) (b: CellValue) : int =
    match a, b with
    | CellValue.Numeric x, CellValue.Numeric y -> compare x y
    // Phase 2006: exact numeric order over the canonical text — never lexical ("10" after
    // "9"), never via a float. A text outside the decimal grammar cannot reach here from the
    // typed path; should a host build one by hand, it orders ordinally rather than throwing.
    | CellValue.Decimal x, CellValue.Decimal y ->
        Fuaran.Core.DecimalText.compare x y
        |> Option.defaultWith (fun () -> System.String.CompareOrdinal(x, y))
    | CellValue.Bool x, CellValue.Bool y -> compare x y
    | CellValue.Date x, CellValue.Date y -> compare x y
    | CellValue.Text x, CellValue.Text y -> System.String.CompareOrdinal(x.ToLowerInvariant(), y.ToLowerInvariant())
    | _ -> compare (cellSortRank a) (cellSortRank b)

/// Sort resolved grid rows by a sort descriptor over the column set. Sorting
/// keys off the addressed column's declared `field` (the row property it
/// displays); a column index outside the set or a field-less closure column
/// leaves the authored order standing — a declaration that cannot be honoured
/// is ignored, not guessed at. Empty cells sort last in BOTH directions; the
/// sort is stable.
let sortRowsByDescriptor
    (columns: ColumnErased<'Msg> list)
    (descriptor: (int * Fuaran.UI.Types.SortDirection) option)
    (rows: Row list)
    : Row list =
    match descriptor with
    | None -> rows
    | Some(colIndex, direction) ->
        // Phase 1909 — `GridColumn.dataField`: an action column (Button /
        // ButtonGroup) sorts nothing even where a pre-existing tree still
        // declares a field on it, so a descriptor naming one is ignored.
        match List.tryItem colIndex columns |> Option.bind Fuaran.UI.GridColumn.dataField with
        | None -> rows
        | Some field ->
            rows
            |> List.map (fun r -> Fuaran.UI.HostPrelude.RowProjection.value r field, r)
            |> List.sortWith (fun (ka, _) (kb, _) ->
                match ka, kb with
                | CellValue.Empty, CellValue.Empty -> 0
                | CellValue.Empty, _ -> 1
                | _, CellValue.Empty -> -1
                | _ ->
                    let c = compareCells ka kb

                    (match direction with
                     | Fuaran.UI.Types.SortDirection.Asc -> c
                     | Fuaran.UI.Types.SortDirection.Desc -> -c))
            |> List.map snd

// ─── Data-bound grid pagination (Phase 862 — `pageStateKey` / `pageSize`) ────
//
// The second instance of the Phase-860 grid-behaviour rule, and it mirrors the
// sort machinery above deliberately: the grid names a State key, the runtime
// reads a validated descriptor from it, and the affordance that writes the key
// is renderer-owned. One implementation serves the client renderer and any SSR
// host, so two surfaces cannot disagree about which rows are on page 3.
//
// Who slices is decided by the SOURCE shape, never by a second declaration. A
// `Binding.Query` whose `dependsOn` names the page key re-runs host-side on a
// page change and already returns the page, so the grid must NOT slice again;
// any other source resolves to the whole set and the grid slices it.

/// Validate a page descriptor value: `{"page": N}`, 1-based. `None` for a
/// malformed value, which the store read turns into page 1 — the honest default
/// (never an arbitrary offset).
let pageOfJVal (jv: JVal) : int option =
    match jv with
    | JObj fields ->
        fields
        |> List.tryPick (function
            | ("page", JInt i) when i >= 1 -> Some i
            | ("page", JFloat f) when floor f = f && f >= 1.0 -> Some(int f)
            | _ -> None)
    | _ -> None

/// Does this source page HOST-side for the given page key? True when the source
/// is a `Query` declaring a `dependsOn` on that key — the query re-runs on a
/// page change and hands back the page itself, so a client-side slice on top
/// would page the page (FUARAN096 warns pre-emit).
let sourceHostPagesOn (source: Binding<'T>) (pageKey: string) : bool =
    match source with
    | Binding.Query(_, _, Some deps) -> deps |> List.contains pageKey
    | _ -> false

/// How many pages a row count divides into at this page size. Always at least
/// one, so an empty grid still reads as "page 1 of 1" rather than "of 0".
let pageCountOf (pageSize: int) (rowCount: int) : int =
    if pageSize <= 0 then
        1
    else
        max 1 ((rowCount + pageSize - 1) / pageSize)

/// The page actually shown, given where the user left the position and how many
/// rows there now are. A page past the end clamps to the LAST page rather than
/// rendering empty: the row count can shrink under a filter while the position
/// stays put, and showing nothing there reads as data loss rather than as the
/// end of the list. Single-sourced because the slice and the row-index offset
/// the write-back path uses must agree — an off-by-one page between them would
/// commit an edit to the wrong row.
let clampPage (pageSize: int) (page: int) (rowCount: int) : int =
    min (max 1 page) (pageCountOf pageSize rowCount)

/// The rows on `page` (1-based) at `pageSize`, after clamping.
let sliceRowsToPage (pageSize: int) (page: int) (rows: Row list) : Row list =
    if pageSize <= 0 then
        rows
    else
        let count = List.length rows
        let clamped = clampPage pageSize page count

        rows
        |> List.skip (min count ((clamped - 1) * pageSize))
        |> List.truncate pageSize

// ─── The row window (Phase 1892 — `windowStateKey` / `rowTotal`) ─────────────
//
// The fourth instance of the Phase-860 grid-behaviour rule, built on the page
// machinery above and deliberately shaped like it: the grid names a State key,
// the runtime reads a VALIDATED descriptor from it, the renderer writes it as
// the viewport moves, and one implementation serves every render leg so a
// server rendering and a client rendering cannot disagree about which rows are
// in the window.
//
// Who slices extends the page rule by one key. A `Query` whose `dependsOn`
// names the window key returns the window itself — the grid slices nothing, and
// the total is the one the document DECLARES through `rowTotal`, since the host
// alone knows how many rows its query matched. Otherwise the grid holds the rows
// it presents (the whole set, or the page a host-paged query returned) and
// windows them, and its total is their count.

/// A usable window descriptor: the 0-based offset of the first row and how many
/// rows the window holds.
type RowWindow = { Offset: int; Count: int }

/// What a grid presents once the window is applied: the rows, the index of the
/// first of them in the range the window moves over, that range's size where it
/// is known, and whether a window is in effect at all (which is what decides
/// whether the grid annotates its rows as a slice).
type PresentedWindow<'row> =
    { Rows: 'row list
      Offset: int
      Total: int option
      Windowed: bool }

/// A JSON integer, from either spelling a store can hold: an int, or a float
/// with no fractional part (`2.0` is `2`). Anything else is not an integer.
let jvalInt (jv: JVal) : int option =
    match jv with
    | JInt i -> Some i
    | JFloat f when
        floor f = f
        && f >= float System.Int32.MinValue
        && f <= float System.Int32.MaxValue
        ->
        Some(int f)
    | _ -> None

/// Validate a window descriptor value: usable only as an object whose `offset`
/// is an integer >= 0 and whose `count` is an integer >= 1. Every other shape is
/// `None` — NO window — which is the honest default: the grid presents every row
/// it would have presented without the field, so no malformed value can hide a
/// row. Split from the store read so the corpus's behaviour vectors, which carry
/// the raw descriptor, run through the same validation the renderer does.
let windowOfJVal (jv: JVal) : RowWindow option =
    match jv with
    | JObj fields ->
        let memberInt name =
            fields |> List.tryPick (fun (k, v) -> if k = name then jvalInt v else None)

        match memberInt "offset", memberInt "count" with
        | Some offset, Some count when offset >= 0 && count >= 1 -> Some { Offset = offset; Count = count }
        | _ -> None
    | _ -> None


/// Does this source window HOST-side for the given window key? The page rule's
/// test, on the other key: a `Query` whose `dependsOn` names it re-runs on a
/// window change and returns the window itself.
let sourceHostWindowsOn (source: Binding<'T>) (windowKey: string) : bool = sourceHostPagesOn source windowKey

/// The window a grid presents over `range` — the rows it holds after sort and
/// page. Pure, and the one function every render leg and the corpus's
/// `grid-window/` vectors run through.
///
/// A host-windowed grid slices nothing: `range` IS the window the host returned,
/// its position is the descriptor's offset (0 where there is none yet), and its
/// total is the declared one. Otherwise the offset CLAMPS to
/// `min(offset, max(0, n - count))`, so a window past the end — a filter that
/// shrank the set under a scrolled viewport — presents the last full window
/// rather than an empty grid, exactly as a page past the end clamps to the last
/// page; and the total is the range's own row count, which is exact.
let presentWindow
    (hostWindows: bool)
    (declaredTotal: int option)
    (window: RowWindow option)
    (range: 'row list)
    : PresentedWindow<'row> =
    if hostWindows then
        { Rows = range
          Offset = window |> Option.map _.Offset |> Option.defaultValue 0
          Total = declaredTotal
          Windowed = true }
    else
        let n = List.length range

        match window with
        | None ->
            { Rows = range
              Offset = 0
              Total = Some n
              Windowed = false }
        | Some w ->
            let offset = min w.Offset (max 0 (n - w.Count))

            { Rows = range |> List.skip offset |> List.truncate w.Count
              Offset = offset
              Total = Some n
              Windowed = true }

/// Phase 1892 — the page count a HOST-paged grid can state once its total is
/// declared: `None` where it is not, so the pager keeps to previous/next.
let declaredPageCount (pageSize: int) (declaredTotal: int option) : int option =
    declaredTotal |> Option.map (pageCountOf pageSize)

/// Phase 1892 — does this grid's HOST return the window? Then the grid slices
/// nothing, neither a page nor a window.
let gridHostWindows (spec: DataGridSpec<'Msg>) : bool =
    spec.WindowStateKey |> Option.exists (sourceHostWindowsOn spec.Source)

/// Phase 1892 — the ARIA row annotations a windowed grid carries: the table's
/// `aria-rowcount` (the total plus the header row, `-1` where the total is
/// unknown) and each presented row's `aria-rowindex` (its 0-based index in the
/// range plus 2, the header being row 1). `None` where no window is in effect,
/// so an unwindowed grid's markup is unchanged.
let windowRowCount (window: PresentedWindow<'row> option) : int option =
    window
    |> Option.filter _.Windowed
    |> Option.map (fun w ->
        match w.Total with
        | Some t -> t + 1
        | None -> -1)

/// The `aria-rowindex` of the presented row at `rowIndex` (0-based within the
/// presented rows), or `None` where no window is in effect.
let windowRowIndex (window: PresentedWindow<'row> option) (rowIndex: int) : int option =
    window
    |> Option.filter _.Windowed
    |> Option.map (fun w -> w.Offset + rowIndex + 2)

// ─── Phase 1911 — the window a scrolling viewport writes ─────────────────────
//
// The WRITE half of `windowStateKey`, which Phase 1892 left to the interactive
// hosts: as the grid's scroll viewport moves, the renderer measures which rows
// are visible and writes `{"offset", "count"}` to the key through the same
// `SetState` route the pager's `{"page": N}` takes. The measurement and the
// write decision live HERE, pure, so the Fable viewport in `Render.fs` and the
// .NET tests run one function rather than a copy each.
//
// Parity-locked with the TS renderer's `GridWindowViewport`
// (`packages/renderer/src/render/Visualisation.tsx`): the same two constants,
// the same arithmetic, and the same dedupe — a window equal to the one last
// written, or to the one State already holds, is never written, so a write that
// re-renders the grid cannot loop.
//
// Phase 1922 — the parity is CERTIFIED, not just asserted: the corpus's
// `grid-window-writer/` vectors (scripted measurements, the expected write at
// every step) run through `stepWindowWriter` here and through the TS
// renderer's own `stepWindowWriter`, which `GridWindowViewport` now calls.

/// The row height assumed until a rendered row can be measured (the TS
/// renderer's `DEFAULT_WINDOW_ROW_HEIGHT_PX`).
let defaultWindowRowHeightPx: float = 32.0

/// The viewport's height bound, and the height assumed where none is measurable
/// (the TS renderer's `WINDOW_VIEWPORT_MAX_HEIGHT_PX`).
let windowViewportMaxHeightPx: float = 480.0

/// What a grid's scroll viewport measured on one scroll, resize or render, in
/// CSS pixels. A figure `<= 0` is "not measurable" — no row rendered yet, a
/// container with no layout (a test DOM) — and falls back as the TS viewport's
/// does: the row height to [[defaultWindowRowHeightPx]], the viewport height to
/// [[windowViewportMaxHeightPx]]. `HeaderHeight` is the table head's height,
/// `0` where there is none; `ScrollTop` is the container's scroll offset.
type ViewportMeasure =
    { ScrollTop: float
      HeaderHeight: float
      RowHeight: float
      ViewportHeight: float }

/// The window a viewport shows: the first row whose top edge is at or above the
/// scroll offset (below the header), and how many rows the viewport's height
/// holds, rounded up so a partly visible last row is in the window. The offset
/// is never negative and the count never below 1, so the result is always a
/// USABLE descriptor ([[windowOfJVal]] reads it back unchanged).
let measureWindow (m: ViewportMeasure) : RowWindow =
    let rowHeight =
        if m.RowHeight > 0.0 then
            m.RowHeight
        else
            defaultWindowRowHeightPx

    let viewport =
        if m.ViewportHeight > 0.0 then
            m.ViewportHeight
        else
            windowViewportMaxHeightPx

    { Offset = max 0 (int (floor ((m.ScrollTop - m.HeaderHeight) / rowHeight)))
      Count = max 1 (int (ceil (viewport / rowHeight))) }

/// The descriptor a window is written as: `{"offset": n, "count": n}`, the
/// shape [[windowOfJVal]] validates.
let windowDescriptorJVal (w: RowWindow) : JVal =
    JObj [ "offset", JInt w.Offset; "count", JInt w.Count ]

/// One step of a grid's window writer. Given the grid's window key (`None` for a
/// grid declaring no `windowStateKey`), the window State currently holds at it,
/// the window this viewport last wrote, and a fresh measurement, returns the
/// window the viewport has now written and the write it owes — `None` where the
/// grid names no key (a grid without `windowStateKey` NEVER writes), and `None`
/// where the measured window equals the one last written or the one State
/// already holds (an unchanged window is never written).
let stepWindowWriter
    (windowKey: string option)
    (current: RowWindow option)
    (lastWritten: RowWindow option)
    (measure: ViewportMeasure)
    : RowWindow option * (string * JVal) option =
    match windowKey with
    | None -> lastWritten, None
    | Some key ->
        let next = measureWindow measure

        if lastWritten = Some next || current = Some next then
            lastWritten, None
        else
            Some next, Some(key, windowDescriptorJVal next)

/// Phase 750 — lower a `CellKindErased.TonedPill` for one row: the named field's
/// text IS the pill's label, and its tone is the map's entry for that text, or
/// `defaultTone` for a value the map does not mention.
///
/// This is the whole of the declarative pill's semantics, in ONE place, because
/// three surfaces render it (the simple-table cell, the AG Grid cell renderer, and
/// the TS tier's two renderers) and a per-surface copy of a lookup-with-fallback is
/// exactly how two hosts come to disagree about an unmapped value. Keyed on the
/// same row projection the row-key floor uses (`BindingResolver.projectRowFieldString`), so a numeric or boolean
/// field maps by its canonical text rather than by a second coercion rule.
/// Parity-locked with the TS renderers' `tonedPillOf`.
let tonedPillOf
    (row: Row)
    (field: string)
    (toneMap: Map<string, ToneVariant>)
    (defaultTone: ToneVariant)
    : string * ToneVariant =
    let label = Fuaran.UI.HostPrelude.RowProjection.string_ row field
    let tone = toneMap |> Map.tryFind label |> Option.defaultValue defaultTone
    label, tone

/// Phase 934 / Phase 863 — where a WHOLE-ROWS write commits. Stated ONCE
/// because a grid's two whole-rows writers — an edited cell and a reordered
/// row — are writes of the same collection, and two copies of this rule is
/// exactly how one grid comes to have two destinations:
///
///   1. a declared `editStateKey` wins. Phase 863 added it so a *decoded*
///      grid could say where its writes land at all (the only previous
///      spelling was a host closure, which crosses the wire as `"<closure>"`);
///   2. else the Phase-663 floor: the grid's own `source` when that source is
///      a DIRECT `Binding.State` (the only source shape with a writable slot);
///   3. else NONE — and the caller must not draw the affordance at all. A
///      Transform pipeline is not invertible and Static/Query rows are host
///      data, so an input or a drag handle over them would be a gesture with
///      no destination: exactly the fake-affordance class Phase 866 charters
///      against, and the reason this returns an option rather than a no-op
///      writer.
let gridWriteDestination (editStateKey: string option) (source: Binding<Row seq>) : Binding<Row seq> option =
    match editStateKey with
    | Some key -> Some(Binding.State(key, None))
    | None ->
        match source with
        | Binding.State _ -> Some source
        | _ -> None

/// Phase 934 — where a REORDER commits: the shared grid destination above,
/// drawn only where the grid declares itself reorderable.
let reorderDestination
    (reorderable: bool)
    (editStateKey: string option)
    (source: Binding<Row seq>)
    : Binding<Row seq> option =
    if reorderable then
        gridWriteDestination editStateKey source
    else
        None

/// Phase 1123 — where a CROSS-CONTAINER TRANSFER commits at one end: the same
/// shared grid destination, drawn only where that end declares its side of the
/// pair (`transferOutKey` on the releasing grid, `transferInKey` on the
/// receiving one). A third caller of one rule rather than a third rule: a
/// transfer IS a write of each end's whole rows value, so it mints no
/// destination of its own — the `reorderable` ruling one field over, extended
/// across two nodes.
///
/// `None` does NOT mean the declaration is inert. The record is still written
/// to the shared key, because a column that is a filtered view over one
/// collection has no writable slot and is exactly the case the transfer
/// vocabulary exists for; what `None` means is that THIS host cannot apply that
/// half, and the application reading the key does.
let transferDestination
    (declared: string option)
    (editStateKey: string option)
    (source: Binding<Row seq>)
    : Binding<Row seq> option =
    match declared with
    | Some _ -> gridWriteDestination editStateKey source
    | None -> None

/// Phase 863 — where an EDITED CELL commits: the shared grid destination
/// above, drawn only where the grid declares itself editable.
///
/// Before this, the renderer resolved the edit destination inline as "the
/// grid's own source, when that source is `State`" — the Phase-663 floor and
/// nothing else. So a grid that *declared* `editStateKey` over a `Query`
/// source decoded, passed pre-emit validation (FUARAN090 was widened by 863
/// precisely because a declared destination is a real one) and then rendered
/// with no inputs at all: a declaration that reads as live everywhere except
/// where it is supposed to act. Routing both writers through one function is
/// what keeps that from recurring per-affordance.
let editDestination
    (editable: bool)
    (editStateKey: string option)
    (source: Binding<Row seq>)
    : Binding<Row seq> option =
    if editable then
        gridWriteDestination editStateKey source
    else
        None

// ─── The row edits: remove, insert, move (Phase 2070) ────────────────────────
//
// THE OUT-OF-RANGE RULE, stated once for all three: **an index outside the
// range the operation addresses is REFUSED, and the list comes back as the SAME
// instance.** The callers write the result back wholesale and compare by
// reference, so "an invalid edit writes nothing new" and "an invalid edit is
// refused" are one behaviour, with no partial state between them.
//
//   • a REMOVAL addresses an item: `0 .. length - 1`;
//   • an INSERTION addresses a gap: `0 .. length` (the length is the append);
//   • a MOVE is a removal followed by an insertion into what remains, so its
//     destination addresses `0 .. length - 1` of the full set — the gaps of the
//     shortened list — and either half refusing refuses the whole move.
//
// Clamping is NOT part of the rule. A drop below the last row of a transfer
// target is an append, and that is a POLICY the drop site applies by name
// (`GridTransfer.clampInsert`) before it inserts — one decision visible where it
// is made, rather than a second rule hidden inside a primitive. `moveRow` used
// to re-implement both halves inline while the transfer path's insert clamped,
// so the two disagreed about the same out-of-range index.

/// Remove the item at `index`, returning it and the list without it, or `None`
/// when `index` is not an item (`0 .. length - 1`).
let tryRemoveAt (index: int) (xs: 'a list) : ('a * 'a list) option =
    if index < 0 || index >= List.length xs then
        None
    else
        Some(List.item index xs, List.truncate index xs @ List.skip (index + 1) xs)

/// Insert `item` BEFORE the item at `index`, or `None` when `index` is not a gap
/// (`0 .. length`; the length appends).
let tryInsertAt (index: int) (item: 'a) (xs: 'a list) : 'a list option =
    if index < 0 || index > List.length xs then
        None
    else
        Some(List.truncate index xs @ (item :: List.skip index xs))

/// Remove the item at `index`; out of range returns the SAME list instance.
let removeAt (index: int) (xs: 'a list) : 'a list =
    tryRemoveAt index xs |> Option.map snd |> Option.defaultValue xs

/// Insert `item` at the gap `index`; out of range returns the SAME list
/// instance (the item is not inserted). A caller that means "clamp" clamps
/// first, by name.
let insertAt (index: int) (item: 'a) (xs: 'a list) : 'a list =
    tryInsertAt index item xs |> Option.defaultValue xs

/// Phase 934 — move the row at `fromIndex` to `toIndex` (both absolute in the
/// full set): the removal of `fromIndex`, then the insertion of that row at
/// `toIndex` into what remains. A no-move, or either index out of range, returns
/// the SAME list instance — the rule above, with no third copy of it here.
let moveRow (fromIndex: int) (toIndex: int) (rows: 'a list) : 'a list =
    if fromIndex = toIndex then
        rows
    else
        tryRemoveAt fromIndex rows
        |> Option.bind (fun (item, without) -> tryInsertAt toIndex item without)
        |> Option.defaultValue rows
