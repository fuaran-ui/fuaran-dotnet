namespace Fuaran.UI.ServerDriven

open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types

// ============================================================================
//  BoardSnapshot — a hash that reproduces exactly what a board showed
//  (Phase 1885).
//
//  A board renders a tree against the data its host furnishes. Resolution is a
//  pure function of the two (stable sorts, left-to-right float folds, no silent
//  overflow, the clock furnished rather than read), so naming BOTH exactly is
//  enough to render the same data again, anywhere, on any conformant host. The
//  snapshot is that naming, and its hash is the identifier a bug report or an
//  audit trail quotes ("what the board showed at 09:14").
//
//  ── WHAT IS EMBEDDED, AND WHAT IS REFERENCED ──────────────────────────────
//  REFERENCED, by content address:
//   * the TREE — `sha256:` over its canonical wire text. The tree is already a
//     content-addressed artefact its host ships; embedding it would make the
//     record as large as the board, and the reproducer is handed the tree and
//     refuses one whose digest differs.
//   * every LIVE SOURCE — a channel whose value is the fold of a hash-chained op
//     stream — by (stream, length, head). The head names the whole prefix, so a
//     stream that has grown since the board was read still reproduces the value
//     it held then, and a stream that was rewritten is refused rather than read.
//     This is what makes a snapshot cheap: the data volume stays in the stream.
//  EMBEDDED, as canonical values:
//   * every BOUND PARAMETER the tree reads — a filter chip, a selection, a state
//     slot that is not a live source. Small, and with no other durable home.
//   * the pinned `Now` and the ambient locale — the two environment inputs
//     resolution reads, and exactly the ones that differ between two viewings.
//
//  ── WHAT CANNOT BE REPRODUCED — REFUSED, NEVER APPROXIMATED ───────────────
//   * a live source with NO op stream (a feed the host reads and keeps no
//     history of): there is nothing a head could name. `capture` refuses it
//     when the tree reads it (`SnapshotRefusal.Unstreamed`).
//   * a reader the walk cannot see into — a `Binding.Computed` closure (handed
//     the whole state bag) or a `Custom` node's host renderer. The tree's wire
//     form does not carry their code, so the tree hash does not pin what they
//     compute. Refused (`OpaqueReader`).
//   * a channel value with no exact wire form — a host object, a boxed integer,
//     a lowered list or map. Seating its canonical form back would hand the
//     resolver a different value. Refused (`NoWireForm`).
//   * a host capability's result (`Binding.Invoke`). The snapshot carries no
//     capability results, so the reproduced sources answer every invocation
//     with a typed error: a board that read one renders that error, LOUDLY,
//     instead of a plausible value.
//  What the reproducing host still supplies is its own configuration — the
//  renderer, the i18n catalog, the evaluation store — exactly as it supplies the
//  renderer that turns the reproduced data into bytes.
// ============================================================================

/// A channel a board reads its data through. The key is the channel's own
/// name: a state key, a filter name, a selection's target node id, a query
/// name.
[<RequireQualifiedAccess>]
type BoardChannel =
    | State of key: string
    | Filter of name: string
    | Selection of nodeId: string
    | Query of name: string

/// How a host feeds a channel it declares LIVE.
[<RequireQualifiedAccess>]
type LiveSource =
    /// The channel's value is the fold of the hash-chained op stream `stream`,
    /// whose first `length` records end at `head`.
    | Streamed of stream: string * length: int * head: string
    /// A live feed the host keeps no op stream of. Not reproducible.
    | Unstreamed

/// How a channel value is held in the host's store — so the reproducer seats
/// back exactly the value the resolver read.
[<RequireQualifiedAccess>]
type ValueForm =
    /// The store held the `JVal` itself.
    | Wire
    /// The store held a boxed string, boolean or float — the scalar lowering.
    | Scalar

/// One embedded bound parameter.
type BoardParameter =
    { Channel: BoardChannel
      Value: JVal
      Form: ValueForm }

/// One referenced live source.
type BoardSource =
    { Channel: BoardChannel
      Stream: string
      Length: int
      Head: string
      Form: ValueForm }

/// The snapshot record. Its identifier is [[BoardSnapshot.hash]].
type BoardSnapshot =
    {
        /// `sha256:` + the hex digest of the tree's canonical wire text.
        Tree: string
        /// The instant `Binding.Now` resolved against — `""` when the host
        /// furnished no clock, which reproduces as no clock.
        Now: string
        /// The ambient locale `Binding.Format` resolved against.
        Locale: string
        /// The embedded parameters, ordered by channel.
        Parameters: BoardParameter list
        /// The referenced live sources, ordered by channel.
        Sources: BoardSource list
    }

/// Why a snapshot could not be taken, read or reproduced.
[<RequireQualifiedAccess>]
type SnapshotRefusal =
    /// The tree reads a live channel the host keeps no op stream of.
    | Unstreamed of channel: BoardChannel
    /// A reader the walk cannot see into (`Computed`, `Custom`).
    | OpaqueReader of reader: string * why: string
    /// A channel value with no exact wire form.
    | NoWireForm of channel: BoardChannel
    /// The tree handed to the reproducer is not the tree the snapshot names.
    | TreeMismatch of expected: string * actual: string
    /// A live source's stream does not reproduce the head the snapshot names.
    | SourceUnreproducible of channel: BoardChannel * stream: string * reason: string
    /// The record text is not a snapshot record.
    | Malformed of reason: string

/// Replays a live source's op stream to the value it held at a head.
type ILiveSourceReplay =
    /// The value the first `length` records of `stream` fold to — refused when
    /// the stream is unknown, shorter than `length`, or its prefix does not end
    /// at `head` (a rewritten history) or does not verify.
    abstract Replay: stream: string * length: int * head: string -> Result<JVal, string>

/// A live source kept as a `Fuaran.Core` op stream folding to a `JVal`.
type CoreLiveStream<'Op, 'Rej> =
    {
        /// The stream's chain configuration, hash and reducer.
        Stream: Stream<'Op, JVal, 'Rej>
        /// The value before the first record.
        Initial: JVal
        /// The records, oldest first. They may extend past any snapshot's head.
        Records: OpRecord<'Op> list
    }

[<RequireQualifiedAccess>]
module BoardChannel =

    /// The channel's wire tag.
    let tag (channel: BoardChannel) : string =
        match channel with
        | BoardChannel.State _ -> "state"
        | BoardChannel.Filter _ -> "filter"
        | BoardChannel.Selection _ -> "selection"
        | BoardChannel.Query _ -> "query"

    /// The channel's key.
    let key (channel: BoardChannel) : string =
        match channel with
        | BoardChannel.State k
        | BoardChannel.Filter k
        | BoardChannel.Selection k
        | BoardChannel.Query k -> k

    /// `state:orders` — the spelling refusals name a channel by.
    let describe (channel: BoardChannel) : string = tag channel + ":" + key channel

    let internal ofTag (tag: string) (key: string) : BoardChannel option =
        match tag with
        | "state" -> Some(BoardChannel.State key)
        | "filter" -> Some(BoardChannel.Filter key)
        | "selection" -> Some(BoardChannel.Selection key)
        | "query" -> Some(BoardChannel.Query key)
        | _ -> None

    /// The value the host's store holds for the channel, if any.
    let internal read (sources: BindingSources) (channel: BoardChannel) : obj option =
        match channel with
        | BoardChannel.State k -> Map.tryFind k sources.State
        | BoardChannel.Filter k -> Map.tryFind k sources.Filters
        | BoardChannel.Selection k -> Map.tryFind (NodeId k) sources.Selections
        | BoardChannel.Query k -> Map.tryFind k sources.QueryResults

    /// The store with the channel written.
    let internal write (channel: BoardChannel) (value: obj) (sources: BindingSources) : BindingSources =
        match channel with
        | BoardChannel.State k ->
            { sources with
                State = Map.add k value sources.State }
        | BoardChannel.Filter k ->
            { sources with
                Filters = Map.add k value sources.Filters }
        | BoardChannel.Selection k ->
            { sources with
                Selections = Map.add (NodeId k) value sources.Selections }
        | BoardChannel.Query k ->
            { sources with
                QueryResults = Map.add k value sources.QueryResults }

[<RequireQualifiedAccess>]
module SnapshotRefusal =

    /// One sentence per refusal.
    let describe (refusal: SnapshotRefusal) : string =
        match refusal with
        | SnapshotRefusal.Unstreamed channel ->
            "the board reads "
            + BoardChannel.describe channel
            + ", a live source with no op stream: no head can name the value it showed"
        | SnapshotRefusal.OpaqueReader(reader, why) ->
            "node '"
            + reader
            + "' reads through "
            + why
            + ", which the tree's wire form does not carry"
        | SnapshotRefusal.NoWireForm channel ->
            BoardChannel.describe channel
            + " holds a value with no exact wire form (a host object, an integer, or a lowered list or map)"
        | SnapshotRefusal.TreeMismatch(expected, actual) ->
            "the tree is " + actual + ", but the snapshot names " + expected
        | SnapshotRefusal.SourceUnreproducible(channel, stream, reason) ->
            BoardChannel.describe channel
            + " (stream '"
            + stream
            + "') does not reproduce: "
            + reason
        | SnapshotRefusal.Malformed reason -> "not a board snapshot record: " + reason

[<RequireQualifiedAccess>]
module ValueForm =

    let internal tag (form: ValueForm) : string =
        match form with
        | ValueForm.Wire -> "wire"
        | ValueForm.Scalar -> "scalar"

    /// The exact wire form of a store value, or `None` when it has none.
    let internal ofStored (v: obj) : (JVal * ValueForm) option =
        let checkedWire (jv: JVal) (form: ValueForm) =
            match Canonical.tryWrite Rendering.CrossHost jv with
            | Ok _ -> Some(jv, form)
            | Error _ -> None

        match v with
        | :? JVal as jv -> checkedWire jv ValueForm.Wire
        | :? string as s -> checkedWire (JStr s) ValueForm.Scalar
        | :? bool as b -> Some(JBool b, ValueForm.Scalar)
        | :? float as f -> checkedWire (JFloat f) ValueForm.Scalar
        | _ -> None

    /// The store value a wire value seats back as.
    let internal seat (form: ValueForm) (jv: JVal) : Result<obj, string> =
        match form, jv with
        | ValueForm.Wire, _ -> Ok(Unchecked.nonNull (box jv))
        | ValueForm.Scalar, JStr s -> Ok(Unchecked.nonNull (box s))
        | ValueForm.Scalar, JBool b -> Ok(Unchecked.nonNull (box b))
        | ValueForm.Scalar, JFloat f -> Ok(Unchecked.nonNull (box f))
        | ValueForm.Scalar, JInt i -> Ok(Unchecked.nonNull (box (float i)))
        | ValueForm.Scalar, _ -> Error "a scalar-form value is not a string, boolean or number"

[<RequireQualifiedAccess>]
module LiveSourceReplay =

    /// Replay a Core-kept live source to the value it held at (`length`, `head`).
    let replayCore (source: CoreLiveStream<'Op, 'Rej>) (length: int) (head: string) : Result<JVal, string> =
        if length < 0 || length > List.length source.Records then
            Error(
                sprintf
                    "the stream holds %d records and the snapshot names a prefix of %d"
                    (List.length source.Records)
                    length
            )
        else
            let prefix = List.truncate length source.Records

            match OpStream.firstChainBreak source.Stream prefix with
            | Some b ->
                Error(
                    sprintf "the stream's chain breaks at record %d (%s)" b.Index (ChainBreakReason.toString b.Reason)
                )
            | None ->
                let actual = OpStream.head source.Stream.Config prefix

                if actual <> head then
                    Error("the stream's prefix ends at " + actual + ", not at the snapshot's head " + head)
                else
                    match OpStream.replay source.Stream.Witness source.Initial prefix with
                    | Ok v -> Ok v
                    | Error(i, _) -> Error(sprintf "the source's reducer refuses record %d" i)

    /// The head a Core-kept live source stands at now — what a host declares
    /// at capture.
    let headOf (stream: string) (source: CoreLiveStream<'Op, 'Rej>) : LiveSource =
        LiveSource.Streamed(stream, List.length source.Records, OpStream.head source.Stream.Config source.Records)

    /// A replay over named Core-kept live sources of one op type.
    let ofCore (streams: Map<string, CoreLiveStream<'Op, 'Rej>>) : ILiveSourceReplay =
        { new ILiveSourceReplay with
            member _.Replay(stream, length, head) =
                match Map.tryFind stream streams with
                | Some s -> replayCore s length head
                | None -> Error "the reproducing host holds no stream of that name" }

    /// Two replays tried in order — the first that knows the stream answers.
    /// For a host whose live sources are of more than one op type.
    let orElse (first: ILiveSourceReplay) (second: ILiveSourceReplay) : ILiveSourceReplay =
        { new ILiveSourceReplay with
            member _.Replay(stream, length, head) =
                match first.Replay(stream, length, head) with
                | Ok v -> Ok v
                | Error _ -> second.Replay(stream, length, head) }

[<RequireQualifiedAccess>]
module BoardSnapshot =

    /// The record's format marker.
    [<Literal>]
    let Format = "fuaran-board-snapshot@1"

    /// The media type a host publishes a snapshot under in its page
    /// ([[publication]]).
    [<Literal>]
    let MediaType = "application/vnd.fuaran.board-snapshot+json"

    /// `sha256:` + hex over UTF-8 text.
    let private digest (text: string) : string = "sha256:" + Hash.sha256Hex text

    /// The tree's content address: `sha256:` over its canonical wire text.
    let treeDigest (tree: Node<'Msg>) : string = digest (Generated.encodeNode tree)

    // ── the channels a tree reads ────────────────────────────────────────────

    let private readChannels (facts: BindingWalk.TreeFacts) : BoardChannel list =
        let ofUse (u: BindingWalk.BindingUse) : BoardChannel list =
            match u with
            | BindingWalk.BindingUse.State k
            | BindingWalk.BindingUse.TransformStateSource(k, _) -> [ BoardChannel.State k ]
            | BindingWalk.BindingUse.Filter n
            | BindingWalk.BindingUse.TransformParamFilter n -> [ BoardChannel.Filter n ]
            | BindingWalk.BindingUse.Selection t -> [ BoardChannel.Selection t ]
            | BindingWalk.BindingUse.Query(n, dependsOn) ->
                BoardChannel.Query n :: (dependsOn |> List.map BoardChannel.Filter)
            | _ -> []

        let fromUses = facts.Bindings.Uses |> List.collect (fun u -> ofUse u.Use)

        let fromStateReads =
            facts.Bindings.StateKeys.Reads |> Set.toList |> List.map BoardChannel.State

        (fromUses @ fromStateReads) |> List.distinct

    let private opaqueRefusals (facts: BindingWalk.TreeFacts) : SnapshotRefusal list =
        facts.Bindings.StateKeys.OpaqueReaders
        |> List.map (fun o ->
            match o with
            | BindingWalk.OpaqueReaderAt.Computed reader -> SnapshotRefusal.OpaqueReader(reader, "a Computed closure")
            | BindingWalk.OpaqueReaderAt.Custom nodeId ->
                SnapshotRefusal.OpaqueReader(nodeId, "a Custom node's host renderer"))
        |> List.distinct

    let private channelOrder (channel: BoardChannel) : string * string =
        BoardChannel.tag channel, BoardChannel.key channel

    // ── capture ──────────────────────────────────────────────────────────────

    /// Take the snapshot of `tree` rendered against `sources`.
    ///
    /// `live` declares which channels are live sources and how each is fed; a
    /// channel it does not name is a bound parameter, embedded by value. Only
    /// the channels the tree READS are recorded, and only those can refuse. The
    /// host's obligation is that a `Streamed` head is the head whose fold is the
    /// value `sources` holds for the channel — the head it read the value at.
    let capture
        (tree: Node<'Msg>)
        (sources: BindingSources)
        (live: Map<BoardChannel, LiveSource>)
        : Result<BoardSnapshot, SnapshotRefusal list> =
        let facts = BindingWalk.collectFacts tree
        let opaque = opaqueRefusals facts

        let outcomes =
            readChannels facts
            |> List.sortBy channelOrder
            |> List.choose (fun channel ->
                match Map.tryFind channel live with
                | Some LiveSource.Unstreamed -> Some(Error(SnapshotRefusal.Unstreamed channel))
                | Some(LiveSource.Streamed(stream, length, head)) ->
                    // The form is read off the value the board rendered; a live
                    // channel the store does not hold yet is seated as the
                    // replayed `JVal`.
                    let form =
                        match BoardChannel.read sources channel with
                        | None -> Ok ValueForm.Wire
                        | Some v ->
                            match ValueForm.ofStored v with
                            | Some(_, form) -> Ok form
                            | None -> Error(SnapshotRefusal.NoWireForm channel)

                    form
                    |> Result.map (fun form ->
                        Choice2Of2
                            { Channel = channel
                              Stream = stream
                              Length = length
                              Head = head
                              Form = form })
                    |> Some
                | None ->
                    match BoardChannel.read sources channel with
                    | None -> None
                    | Some v ->
                        match ValueForm.ofStored v with
                        | Some(jv, form) ->
                            Some(
                                Ok(
                                    Choice1Of2
                                        { Channel = channel
                                          Value = jv
                                          Form = form }
                                )
                            )
                        | None -> Some(Error(SnapshotRefusal.NoWireForm channel)))

        let refusals =
            opaque
            @ (outcomes
               |> List.choose (fun o ->
                   match o with
                   | Error r -> Some r
                   | Ok _ -> None))

        if not refusals.IsEmpty then
            Error refusals
        else
            let parameters =
                outcomes
                |> List.choose (fun o ->
                    match o with
                    | Ok(Choice1Of2 p) -> Some p
                    | _ -> None)

            let liveSources =
                outcomes
                |> List.choose (fun o ->
                    match o with
                    | Ok(Choice2Of2 s) -> Some s
                    | _ -> None)

            Ok
                { Tree = treeDigest tree
                  Now = sources.Now
                  Locale = sources.Locale
                  Parameters = parameters
                  Sources = liveSources }

    // ── the record's canonical form ──────────────────────────────────────────

    /// The record as a wire value. Members sort under the cross-host rendering;
    /// the two arrays keep the record's channel order.
    let toJson (snapshot: BoardSnapshot) : JVal =
        let channelFields (channel: BoardChannel) =
            [ "channel", JStr(BoardChannel.tag channel)
              "key", JStr(BoardChannel.key channel) ]

        JObj
            [ "board", JStr Format
              "tree", JStr snapshot.Tree
              "now", JStr snapshot.Now
              "locale", JStr snapshot.Locale
              "parameters",
              JArr(
                  snapshot.Parameters
                  |> List.map (fun p ->
                      JObj(
                          channelFields p.Channel
                          @ [ "value", p.Value; "form", JStr(ValueForm.tag p.Form) ]
                      ))
              )
              "sources",
              JArr(
                  snapshot.Sources
                  |> List.map (fun s ->
                      JObj(
                          channelFields s.Channel
                          @ [ "stream", JStr s.Stream
                              "length", JInt s.Length
                              "head", JStr s.Head
                              "form", JStr(ValueForm.tag s.Form) ]
                      ))
              ) ]

    /// The record's canonical text — the bytes its hash is taken over.
    let encode (snapshot: BoardSnapshot) : string =
        Canonical.write Rendering.CrossHost (toJson snapshot)

    /// The snapshot's identifier: `sha256:` over its canonical text.
    let hash (snapshot: BoardSnapshot) : string = digest (encode snapshot)

    let private field (name: string) (fields: (string * JVal) list) : Result<JVal, string> =
        match fields |> List.filter (fun (k, _) -> k = name) with
        | [ _, v ] -> Ok v
        | [] -> Error("member '" + name + "' is missing")
        | _ -> Error("member '" + name + "' is repeated")

    let private str (name: string) (fields: (string * JVal) list) : Result<string, string> =
        field name fields
        |> Result.bind (fun v ->
            match v with
            | JStr s -> Ok s
            | _ -> Error("member '" + name + "' is not a string"))

    let private formOf (fields: (string * JVal) list) : Result<ValueForm, string> =
        str "form" fields
        |> Result.bind (fun f ->
            match f with
            | "wire" -> Ok ValueForm.Wire
            | "scalar" -> Ok ValueForm.Scalar
            | other -> Error("form '" + other + "' is not wire or scalar"))

    let private channelOf (fields: (string * JVal) list) : Result<BoardChannel, string> =
        str "channel" fields
        |> Result.bind (fun tag ->
            str "key" fields
            |> Result.bind (fun key ->
                match BoardChannel.ofTag tag key with
                | Some c -> Ok c
                | None -> Error("channel '" + tag + "' is not state, filter, selection or query")))

    let private allOk (items: Result<'T, string> list) : Result<'T list, string> =
        List.foldBack (fun r acc -> Result.bind (fun xs -> Result.map (fun x -> x :: xs) r) acc) items (Ok [])

    /// The members must be exactly `expected`: a member this format does not
    /// define would be carried into the published hash and dropped by every
    /// reader that re-encodes the record, so it is refused rather than ignored.
    let private membersAre (expected: string list) (fields: (string * JVal) list) : Result<unit, string> =
        match fields |> List.map fst |> List.tryFind (fun k -> not (List.contains k expected)) with
        | Some k -> Error("member '" + k + "' is not a record member")
        | None -> Ok()

    let private entries
        (name: string)
        (expected: string list)
        (fields: (string * JVal) list)
        : Result<(string * JVal) list list, string> =
        field name fields
        |> Result.bind (fun v ->
            match v with
            | JArr items ->
                items
                |> List.map (fun item ->
                    match item with
                    | JObj fs -> membersAre expected fs |> Result.map (fun () -> fs)
                    | _ -> Error("an entry of '" + name + "' is not an object"))
                |> allOk
            | _ -> Error("member '" + name + "' is not an array"))

    /// Read a record from its wire value. Refuses an unknown format marker,
    /// a missing, repeated or ill-typed member, and an unknown channel or form.
    let ofJson (v: JVal) : Result<BoardSnapshot, SnapshotRefusal> =
        let read () =
            match v with
            | JObj fields ->
                membersAre [ "board"; "tree"; "now"; "locale"; "parameters"; "sources" ] fields
                |> Result.bind (fun () -> str "board" fields)
                |> Result.bind (fun marker ->
                    if marker <> Format then
                        Error("format '" + marker + "' is not " + Format)
                    else
                        Ok())
                |> Result.bind (fun () -> str "tree" fields)
                |> Result.bind (fun tree ->
                    str "now" fields
                    |> Result.bind (fun now ->
                        str "locale" fields
                        |> Result.bind (fun locale ->
                            entries "parameters" [ "channel"; "key"; "value"; "form" ] fields
                            |> Result.bind (fun ps ->
                                ps
                                |> List.map (fun p ->
                                    channelOf p
                                    |> Result.bind (fun channel ->
                                        field "value" p
                                        |> Result.bind (fun value ->
                                            formOf p
                                            |> Result.map (fun form ->
                                                { Channel = channel
                                                  Value = value
                                                  Form = form }))))
                                |> allOk)
                            |> Result.bind (fun parameters ->
                                entries "sources" [ "channel"; "key"; "stream"; "length"; "head"; "form" ] fields
                                |> Result.bind (fun ss ->
                                    ss
                                    |> List.map (fun s ->
                                        channelOf s
                                        |> Result.bind (fun channel ->
                                            str "stream" s
                                            |> Result.bind (fun stream ->
                                                str "head" s
                                                |> Result.bind (fun head ->
                                                    field "length" s
                                                    |> Result.bind (fun l ->
                                                        match l with
                                                        | JInt n when n >= 0 -> Ok n
                                                        | _ ->
                                                            Error
                                                                "a source's length is not a non-negative integer")
                                                    |> Result.bind (fun length ->
                                                        formOf s
                                                        |> Result.map (fun form ->
                                                            { Channel = channel
                                                              Stream = stream
                                                              Length = length
                                                              Head = head
                                                              Form = form }))))))
                                    |> allOk)
                                |> Result.map (fun sources ->
                                    { Tree = tree
                                      Now = now
                                      Locale = locale
                                      Parameters = parameters
                                      Sources = sources })))))
            | _ -> Error "the record is not an object"

        read () |> Result.mapError SnapshotRefusal.Malformed

    /// Read a record from text.
    let decode (text: string) : Result<BoardSnapshot, SnapshotRefusal> =
        match Json.parse text with
        | Ok v -> ofJson v
        | Error e -> Error(SnapshotRefusal.Malformed e)

    // ── reproduce ────────────────────────────────────────────────────────────

    /// The invoker the reproduced sources carry: the snapshot holds no
    /// capability results, so every invocation is a typed error rather than a
    /// value.
    let private refusingInvoker (capabilityId: string) (_: (string * string) list) : Deferred<obj> =
        Deferred.Error(
            "capability '"
            + capabilityId
            + "' is not reproducible from a board snapshot: the snapshot holds no capability results"
        )

    /// The sources that reproduce what the board showed.
    ///
    /// `host` is the REPRODUCING host's configuration — its i18n catalog and
    /// resolver, its evaluation store. Every data channel, the clock and the
    /// locale come from the snapshot alone: anything `host` holds in a channel
    /// is discarded. `tree` must be the tree the snapshot names.
    let reproduce
        (host: BindingSources)
        (tree: Node<'Msg>)
        (snapshot: BoardSnapshot)
        (replay: ILiveSourceReplay)
        : Result<BindingSources, SnapshotRefusal list> =
        let actual = treeDigest tree

        if actual <> snapshot.Tree then
            Error [ SnapshotRefusal.TreeMismatch(snapshot.Tree, actual) ]
        else
            let blank =
                { host with
                    QueryResults = Map.empty
                    State = Map.empty
                    Filters = Map.empty
                    Selections = Map.empty
                    Now = snapshot.Now
                    Locale = snapshot.Locale
                    CapabilityInvoker = refusingInvoker }

            let parameterSeats =
                snapshot.Parameters
                |> List.map (fun p ->
                    ValueForm.seat p.Form p.Value
                    |> Result.map (fun v -> p.Channel, v)
                    |> Result.mapError (fun _ -> SnapshotRefusal.NoWireForm p.Channel))

            let sourceSeats =
                snapshot.Sources
                |> List.map (fun s ->
                    replay.Replay(s.Stream, s.Length, s.Head)
                    |> Result.bind (ValueForm.seat s.Form)
                    |> Result.map (fun v -> s.Channel, v)
                    |> Result.mapError (fun reason ->
                        SnapshotRefusal.SourceUnreproducible(s.Channel, s.Stream, reason)))

            let seats = parameterSeats @ sourceSeats

            let refusals =
                seats
                |> List.choose (fun r ->
                    match r with
                    | Error e -> Some e
                    | Ok _ -> None)

            if not refusals.IsEmpty then
                Error refusals
            else
                seats
                |> List.fold
                    (fun acc r ->
                        match r with
                        | Ok(channel, v) -> BoardChannel.write channel v acc
                        | Error _ -> acc)
                    blank
                |> Ok

    // ── publication ──────────────────────────────────────────────────────────

    /// The snapshot as an inert page element a host places beside the board,
    /// for an inspector to read without asking the host anything. The body is
    /// the canonical record with every `<` spelled `<`, so no string in it
    /// can close the element; the attribute carries the hash, so a reader can
    /// tell a published record from a tampered or stale one.
    let publication (snapshot: BoardSnapshot) : string =
        "<script type=\""
        + MediaType
        + "\" data-fuaran-board-snapshot=\""
        + hash snapshot
        + "\">"
        + (encode snapshot).Replace("<", "\\u003c")
        + "</script>"
