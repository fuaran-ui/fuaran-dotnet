namespace Fuaran.UI

// ============================================================================
//  Host emission limits (Phase 1817) — a host says how much tree it can take,
//  and the pre-emit validator refuses the rest before emit.
//
//  WHY THIS EXISTS. The decode side is bounded: the depth guard and the other
//  `WireLimits` (Phase 781), the decode-time kind policy (`DecodePolicy`, Phase
//  1020), the render-cost bounds (Phase 790). The authoring side said nothing
//  about how LARGE a tree a given host can sensibly take. An email client, a
//  phone, a card host and a desktop dashboard are not the same budget, and an
//  emitter — an AI one above all — had no way to be told. A `HostLimits` value
//  is that statement, and `PreEmitValidate.validateWithLimits` refuses a tree
//  that exceeds it with a stable code per limit (FUARAN158–FUARAN162), each
//  message naming the limit, the measured value and the first offending node.
//
//  THE RELATIONSHIP TO THE DECODE SIDE — read this before loosening either.
//  Decode guards protect the READER from a HOSTILE document: they are protocol
//  limits (`WIRE_FORMAT.md` §21), the same for every conformant host, and a
//  payload past them is refused whatever its author meant. Emission limits
//  protect the reader from an HONEST one: a well-formed, well-meant tree that is
//  simply more than this particular host renders well. Neither substitutes for
//  the other. A host that declares generous emission limits has not widened
//  what it will DECODE — `WireLimits` still applies at the boundary — and a
//  host that declares none is exactly as protected against hostile input as it
//  was. An emission limit larger than the matching wire limit is inert past the
//  wire limit (the walk stops at `WireLimits.MaxDepth`, and FUARAN091 reports
//  it), never a loosening of it.
//
//  WIRE-NEUTRAL BY DESIGN. Limits are host configuration, with the same
//  standing as `DecodePolicy`: nothing here is carried on the wire, and a tree
//  refused under a host's limits is still a valid wire document. Whether limits
//  should one day travel in capability negotiation (§15.2) is deliberately NOT
//  decided here; an emitter learns them by asking the host's capability report
//  (`Fuaran.UI.AiTools.Capabilities.emissionLimits`), not from the document.
//
//  THE DEFAULT CHANGES NOTHING. Every field is optional, and
//  `HostLimits.unbounded` declares none: validating under it is byte-for-byte
//  the plain `PreEmitValidate.validate`.
//
//  ONE WALK. The four structural limits are measured by `HostLimitMeter`, which
//  rides the validator's own depth-first walk — one `Visit` per node, however
//  many limits are declared. The serialized-size limit is the one that cannot
//  be answered structurally: it is measured by encoding the tree ONCE, and only
//  when that limit is declared.
//
//  Fable-compatible: records, lists and a small class; no reflection, no
//  `System.Text`.
// ============================================================================

open Fuaran.UI.Types

/// A host's declared emission budget. Every limit is an INCLUSIVE maximum and
/// optional: `None` means "this host states no limit here", never zero.
///
/// `Identity` is a short, stable name for the declaration, reported in every
/// refusal so a log line says WHICH budget refused — the same reason
/// `DecodePolicy.Identity` exists.
type HostLimits =
    {
        Identity: string
        /// The most nodes the tree may carry, counted over the nodes the
        /// pre-emit walk visits: structural children, `ErrorBoundary` child and
        /// fallback, every `Switch` case, a `FragmentDecl` body and a node's
        /// declared `fallback` — the same set `NodeId` uniqueness is judged
        /// over. `State` arms are not in that walk; their bytes are covered by
        /// `MaxSerializedBytes`. **FUARAN158.**
        MaxNodes: int option
        /// The deepest nesting level, the root being level 1. **FUARAN159.**
        MaxDepth: int option
        /// The most direct children one container may hold — the `Children`
        /// list of a `Box`, `SplitPanel`, `Tabs`, `Stepper`, `SummaryList`,
        /// `Disclosure`, `Modal` or `ScrollArea`. **FUARAN160.**
        MaxChildren: int option
        /// The most rows one `DataGrid` may carry INLINE — its `staticRows`, or
        /// a `Binding.Static` source. A bound source (`$query`, `$state`, …) is
        /// filled at render time and is the host's to page, so it is not
        /// judged. **FUARAN161.**
        MaxGridRows: int option
        /// The most UTF-8 bytes the tree's canonical JSON encoding may occupy.
        /// **FUARAN162.**
        MaxSerializedBytes: int option
    }

/// The five limits a `HostLimits` can declare, one per record field. Closed on
/// purpose: it enumerates the RECORD, and a new limit is a new field, a new
/// code and a new case in the same change. The presets are data, not cases.
[<RequireQualifiedAccess>]
type HostLimitKind =
    | Nodes
    | Depth
    | Children
    | GridRows
    | SerializedBytes

/// One limit exceeded: which, the node the repair starts at, the declared
/// limit, and what was measured there.
///
/// `NodeId` is the FIRST offender — for `Nodes`, the first node past the
/// budget in depth-first pre-order; for `Depth`, the first node below the
/// limit; for `Children` / `GridRows`, the container or grid itself; for
/// `SerializedBytes`, the root, since the payload is the whole tree.
/// `Measured` is the tree's total for `Nodes`, the deepest level walked for
/// `Depth`, the offender's own count for `Children` / `GridRows`, and the
/// payload's size for `SerializedBytes`.
type HostLimitBreach =
    { Kind: HostLimitKind
      NodeId: string
      Limit: int
      Measured: int }

/// What one metered walk saw — the usage side of the budget, so an emitter can
/// be told how close it came as well as where it went over.
type HostLimitMeasurement =
    {
        Nodes: int
        Depth: int
        /// The largest direct-child count of any container walked.
        WidestContainer: int
        /// The largest inline row count of any `DataGrid` walked.
        LargestGrid: int
        /// `None` unless `MaxSerializedBytes` was declared, because measuring it
        /// costs an encode.
        SerializedBytes: int option
    }

[<RequireQualifiedAccess>]
module HostLimits =

    /// Declares nothing. The shipped default: validating under it is
    /// byte-for-byte the plain `PreEmitValidate.validate`.
    let unbounded: HostLimits =
        { Identity = "unbounded"
          MaxNodes = None
          MaxDepth = None
          MaxChildren = None
          MaxGridRows = None
          MaxSerializedBytes = None }

    /// A named declaration with no limits yet — the starting point for a
    /// host's own budget: `{ HostLimits.named "kiosk" with MaxNodes = Some 800 }`.
    let named (identity: string) : HostLimits = { unbounded with Identity = identity }

    // ── Presets — STARTING POINTS, never authority ────────────────────────
    //
    // Each figure says where it came from. "Judgement" means exactly that: a
    // round number chosen to be comfortably inside what the host class is known
    // to handle, not a measurement. A host that knows its own budget declares
    // it; these exist so that one that does not can start somewhere better than
    // nothing, and so that the numbers are data a host copies and edits rather
    // than cases a host is locked into.

    /// A tree bound for an email body (the HTML the email projection renders).
    ///
    /// - `MaxSerializedBytes = 65536` — Gmail clips a message body past roughly
    ///   102 KB of HTML (its documented clipping threshold at the time of
    ///   writing, 2026; re-check before relying on it). The rendered HTML runs larger than the tree's JSON, so the
    ///   wire budget sits well under that; the expansion ratio is a judgement,
    ///   not a measurement.
    /// - `MaxDepth = 12` — judgement: email layouts nest a table per level, and
    ///   older desktop mail engines degrade on deep table nesting.
    /// - `MaxNodes = 400`, `MaxChildren = 50`, `MaxGridRows = 100` — judgement:
    ///   an email is a digest a reader scrolls once, not an application.
    let email: HostLimits =
        { Identity = "email"
          MaxNodes = Some 400
          MaxDepth = Some 12
          MaxChildren = Some 50
          MaxGridRows = Some 100
          MaxSerializedBytes = Some 65536 }

    /// A tree rendered on a phone.
    ///
    /// - `MaxDepth = 16` — the deepest deliberately-deep application tree the
    ///   `WireLimits` measurement notes (dashboard > grid > card > … > field)
    ///   reaches about 16; a phone gains nothing from more.
    /// - `MaxNodes = 1500`, `MaxChildren = 100`, `MaxGridRows = 200`,
    ///   `MaxSerializedBytes = 262144` — judgement: a single-column screen, a
    ///   cellular payload, and a grid a thumb can page through.
    let mobile: HostLimits =
        { Identity = "mobile"
          MaxNodes = Some 1500
          MaxDepth = Some 16
          MaxChildren = Some 100
          MaxGridRows = Some 200
          MaxSerializedBytes = Some 262144 }

    /// A tree projected to a card host (the Adaptive-Card-shaped projection).
    ///
    /// - `MaxSerializedBytes = 28000` — Microsoft Teams, the host Adaptive Cards
    ///   are best known on, caps a bot message, card included, at about 28 KB
    ///   (its documented message-size limit at the time of writing, 2026;
    ///   re-check before relying on it). The projection's
    ///   JSON and this tree's JSON are not the same bytes; the figure is the
    ///   host's ceiling, applied to the tree as the nearest measurable proxy.
    /// - `MaxDepth = 8`, `MaxNodes = 150`, `MaxChildren = 25`,
    ///   `MaxGridRows = 30` — judgement: a card is glanced at, not read.
    let card: HostLimits =
        { Identity = "card"
          MaxNodes = Some 150
          MaxDepth = Some 8
          MaxChildren = Some 25
          MaxGridRows = Some 30
          MaxSerializedBytes = Some 28000 }

    /// The declared value of one limit.
    let limitOf (kind: HostLimitKind) (limits: HostLimits) : int option =
        match kind with
        | HostLimitKind.Nodes -> limits.MaxNodes
        | HostLimitKind.Depth -> limits.MaxDepth
        | HostLimitKind.Children -> limits.MaxChildren
        | HostLimitKind.GridRows -> limits.MaxGridRows
        | HostLimitKind.SerializedBytes -> limits.MaxSerializedBytes

    /// Every limit kind, in record order.
    let kinds: HostLimitKind list =
        [ HostLimitKind.Nodes
          HostLimitKind.Depth
          HostLimitKind.Children
          HostLimitKind.GridRows
          HostLimitKind.SerializedBytes ]

    /// The limit's stable name — the record field's name in camelCase, as the
    /// capability report and the refusal messages spell it.
    let name (kind: HostLimitKind) : string =
        match kind with
        | HostLimitKind.Nodes -> "maxNodes"
        | HostLimitKind.Depth -> "maxDepth"
        | HostLimitKind.Children -> "maxChildren"
        | HostLimitKind.GridRows -> "maxGridRows"
        | HostLimitKind.SerializedBytes -> "maxSerializedBytes"

    /// The declared limits, in record order, undeclared ones omitted.
    let declared (limits: HostLimits) : (HostLimitKind * int) list =
        kinds |> List.choose (fun k -> limitOf k limits |> Option.map (fun v -> k, v))

    /// Does this declaration state any limit at all?
    let declaresAny (limits: HostLimits) : bool = not (List.isEmpty (declared limits))

    /// The direct-child count of a container kind, `None` for a kind that is not
    /// a `Children`-list container. `ErrorBoundary`, `Switch` and `FragmentDecl`
    /// hold fixed arms or ALTERNATIVES rather than a list rendered side by side,
    /// so they are not containers for this limit (their nodes still count
    /// toward `MaxNodes`).
    ///
    /// FORWARD-COUPLING: exhaustive on purpose, no wildcard. A new `NodeKind`
    /// case declares here whether it is a container, or the build fails rather
    /// than the limit silently skipping it.
    let childCount (kind: NodeKind<'Msg>) : int option =
        match kind with
        | NodeKind.Box s -> Some(List.length s.Children)
        | NodeKind.SplitPanel s -> Some(List.length s.Children)
        | NodeKind.Tabs s -> Some(List.length s.Children)
        | NodeKind.Stepper s -> Some(List.length s.Children)
        | NodeKind.SummaryList s -> Some(List.length s.Children)
        | NodeKind.Disclosure s -> Some(List.length s.Children)
        | NodeKind.Modal s -> Some(List.length s.Children)
        | NodeKind.ScrollArea s -> Some(List.length s.Children)
        | NodeKind.ErrorBoundary _
        | NodeKind.Switch _
        | NodeKind.FragmentDecl _
        | NodeKind.FragmentRef _
        | NodeKind.Mount _
        | NodeKind.Heading _
        | NodeKind.Markdown _
        | NodeKind.Metric _
        | NodeKind.Badge _
        | NodeKind.Sparkline _
        | NodeKind.Callout _
        | NodeKind.Progress _
        | NodeKind.Skeleton _
        | NodeKind.Icon _
        | NodeKind.LabelValueRow _
        | NodeKind.Fact _
        | NodeKind.Link _
        | NodeKind.Image _
        | NodeKind.Media _
        | NodeKind.Embed _
        | NodeKind.List _
        | NodeKind.Tree _
        | NodeKind.Toast _
        | NodeKind.CodeBlock _
        | NodeKind.Math _
        | NodeKind.Drawing _
        | NodeKind.Form _
        | NodeKind.Filters _
        | NodeKind.Button _
        | NodeKind.FileUpload _
        | NodeKind.Select _
        | NodeKind.DataGrid _
        | NodeKind.Chart _
        | NodeKind.Map _
        | NodeKind.Custom _ -> None

    /// The inline row count of a `DataGrid`: its `staticRows`, or a
    /// `Binding.Static` source, whichever is larger. `None` for any other kind.
    ///
    /// A static source is a `seq`, so it is counted through `Seq.truncate` at
    /// one past the wire's own array ceiling — the count stays bounded whatever
    /// the host put there, and any figure that large is over every budget.
    let gridRowCount (kind: NodeKind<'Msg>) : int option =
        match kind with
        | NodeKind.DataGrid spec ->
            let fromStaticRows =
                match spec.StaticRows with
                | Some rows -> List.length rows.Rows
                | None -> 0

            let fromSource =
                match spec.Source with
                | Binding.Static(Some rows) -> rows |> Seq.truncate (WireLimits.MaxArrayLength + 1) |> Seq.length
                | _ -> 0

            Some(max fromStaticRows fromSource)
        | _ -> None

    /// The UTF-8 byte length of `s`, counted without `System.Text` so the same
    /// figure is produced under Fable. A lone surrogate counts as the three
    /// bytes of the replacement character an encoder writes for it.
    let utf8Length (s: string) : int =
        let mutable n = 0
        let mutable i = 0

        while i < s.Length do
            let c = int s.[i]

            if c < 0x80 then
                n <- n + 1
            elif c < 0x800 then
                n <- n + 2
            elif
                c >= 0xD800
                && c <= 0xDBFF
                && i + 1 < s.Length
                && int s.[i + 1] >= 0xDC00
                && int s.[i + 1] <= 0xDFFF
            then
                n <- n + 4
                i <- i + 1
            else
                n <- n + 3

            i <- i + 1

        n

    /// The tree's serialized size: the UTF-8 length of the generated canonical
    /// encoder's output.
    ///
    /// That encoder is the one the op-stream's canonical form delegates to,
    /// AFTER a normalisation that only ever REMOVES bytes (it drops a `State`
    /// block with no arms and a `Style` equal to the default). So this is the
    /// canonical size exactly, except on those two non-canonical spellings,
    /// where it is an over-count — a refusal there is repaired by dropping the
    /// empty block, which the message's figure makes visible.
    let serializedBytes (node: Node<'Msg>) : int = utf8Length (Generated.encodeNode node)

/// Measures one tree against one `HostLimits` as a walker visits it.
///
/// The meter does not walk. It is fed by whoever does — `PreEmitValidate`'s
/// own depth-first walk, so validating under limits costs no second traversal —
/// through `Visit`, once per node, with that node's nesting level (root = 1).
/// `Finish` closes the measurement; `Breaches` and `Measurement` read it. One
/// meter measures one tree: create a fresh one per validation.
[<Sealed>]
type HostLimitMeter(limits: HostLimits) =
    let mutable visits = 0
    let mutable deepest = 0
    let mutable widest = 0
    let mutable largestGrid = 0
    let mutable nodeOffender: string option = None
    let mutable depthOffender: string option = None
    let wide = ResizeArray<string * int>()
    let tall = ResizeArray<string * int>()
    let mutable bytes: (string * int) option = None

    /// The declaration this meter measures against.
    member _.Limits = limits

    /// How many nodes have been visited — equal to the tree's walked node count
    /// after a validation, which is what pins "one walk, not one per limit".
    member _.Visits = visits

    /// Record one node at nesting level `depth` (the root is 1).
    member _.Visit(node: Node<'Msg>, depth: int) : unit =
        visits <- visits + 1

        match limits.MaxNodes with
        | Some max when visits > max && nodeOffender.IsNone -> nodeOffender <- Some node.Id
        | _ -> ()

        if depth > deepest then
            deepest <- depth

        match limits.MaxDepth with
        | Some max when depth > max && depthOffender.IsNone -> depthOffender <- Some node.Id
        | _ -> ()

        match HostLimits.childCount node.Kind with
        | Some count ->
            if count > widest then
                widest <- count

            match limits.MaxChildren with
            | Some max when count > max -> wide.Add(node.Id, count)
            | _ -> ()
        | None -> ()

        match HostLimits.gridRowCount node.Kind with
        | Some rows ->
            if rows > largestGrid then
                largestGrid <- rows

            match limits.MaxGridRows with
            | Some max when rows > max -> tall.Add(node.Id, rows)
            | _ -> ()
        | None -> ()

    /// Close the measurement over `root`. The serialized size is measured here,
    /// once, and only when `MaxSerializedBytes` is declared AND `walkComplete`:
    /// a tree the walk abandoned (past `WireLimits.MaxDepth`) is not encoded,
    /// because the encoder recurses and the depth refusal has already said what
    /// is wrong.
    member _.Finish(root: Node<'Msg>, walkComplete: bool) : unit =
        match limits.MaxSerializedBytes with
        | Some _ when walkComplete -> bytes <- Some(root.Id, HostLimits.serializedBytes root)
        | _ -> ()

    /// Every limit exceeded, in limit order: at most one `Nodes`, one `Depth`
    /// and one `SerializedBytes` breach, and one `Children` / `GridRows` breach
    /// per offending container or grid, in walk order — so an emitter repairs
    /// every one in a single pass.
    member _.Breaches: HostLimitBreach list =
        [ match limits.MaxNodes, nodeOffender with
          | Some max, Some id ->
              { Kind = HostLimitKind.Nodes
                NodeId = id
                Limit = max
                Measured = visits }
          | _ -> ()

          match limits.MaxDepth, depthOffender with
          | Some max, Some id ->
              { Kind = HostLimitKind.Depth
                NodeId = id
                Limit = max
                Measured = deepest }
          | _ -> ()

          match limits.MaxChildren with
          | Some max ->
              for (id, count) in wide do
                  { Kind = HostLimitKind.Children
                    NodeId = id
                    Limit = max
                    Measured = count }
          | None -> ()

          match limits.MaxGridRows with
          | Some max ->
              for (id, rows) in tall do
                  { Kind = HostLimitKind.GridRows
                    NodeId = id
                    Limit = max
                    Measured = rows }
          | None -> ()

          match limits.MaxSerializedBytes, bytes with
          | Some max, Some(id, size) when size > max ->
              { Kind = HostLimitKind.SerializedBytes
                NodeId = id
                Limit = max
                Measured = size }
          | _ -> () ]

    /// What the walk measured, whether or not anything was exceeded.
    member _.Measurement: HostLimitMeasurement =
        { Nodes = visits
          Depth = deepest
          WidestContainer = widest
          LargestGrid = largestGrid
          SerializedBytes = bytes |> Option.map snd }
