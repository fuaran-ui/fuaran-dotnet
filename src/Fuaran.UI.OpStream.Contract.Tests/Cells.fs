module Fuaran.UI.OpStream.Contract.Tests.Cells

open System
open System.IO
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.InMemory
open Fuaran.UI.OpStream.Sqlite
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.InMemory
open Fuaran.UI.OpStream.Dag.Sqlite
open Fuaran.UI.OpStream.Dag.Merge
open Fuaran.UI.OpStream.TestSupport

// ============================================================================
//  The four cells of the sink contract (Phase 2066): {in-memory, SQLite} x
//  {linear, DAG}.
//
//  A `Harness` drives ONE stream in ONE store through the operations both
//  shapes offer, in the terms both shapes share — a trunk of records read
//  genesis first, a head, a compare-and-append, compaction, a checkpoint and
//  replay from it. The contract cases are written once against it, so a case
//  cannot quietly test one store harder than another.
//
//  How each shape realises an operation:
//
//   | operation          | linear                                  | DAG                                             |
//   |--------------------|-----------------------------------------|-------------------------------------------------|
//   | append             | `Append` at `LatestSequence + 1`        | `Add` a child of the head, then `TryAdvanceHead` |
//   | compare-and-append | `IOpStreamCasSink.AppendIf`             | `Add`, then `TryAdvanceHead` from the expectation |
//   | read the trunk     | `Replay(1, max)`                        | `TryGet` / `Skeleton` along the primary parents   |
//   | compaction         | `IOpStreamCompactSink.Compact`          | `Tombstone` the trunk records at or below it,     |
//   |                    |                                         | a retained checkpoint's node kept (Core's rule)   |
//   | checkpoint         | `Checkpoint.create` into the sink       | `DagFold.sealAt` (Core), held by the caller      |
//   | replay from it     | `CheckpointedReplay.applyFromCheckpoint`| `DagFold.replayFrom` (Core)                      |
//
//  Since Phase 2645 a pruned DAG node leaves the live set for the sink's
//  skeleton: the trunk walk reads it back from there, address and links only.
// ============================================================================

type Store =
    | InMemory
    | Sqlite

type Shape =
    | Linear
    | Dag

/// One trunk record as a reader gets it back.
type Entry =
    {
        /// 1-based depth on the trunk (the linear sequence; the DAG spine depth).
        Position: int
        /// The linear chain hash; the DAG content address.
        Address: string
        /// The op, canonically encoded — what the store round-tripped.
        Op: string
        Actor: Actor
        PromptId: string option
        Timestamp: DateTimeOffset
        Tombstoned: bool
    }

/// What a store did with an offered record, or with a read.
[<RequireQualifiedAccess>]
type Outcome =
    | Accepted
    | Refused of message: string

/// One stream in one store.
type Harness =
    {
        Shape: Shape
        Store: Store
        StreamId: string
        /// Append `op` as the next trunk record, stamped `at`, advancing the head;
        /// returns its address.
        Append: DateTimeOffset -> TreeOp<TestMsg> -> string
        Head: unit -> string option
        /// Append `op` built against `expected`, only if `expected` is still the
        /// head. `true` when it appended.
        AppendIf: string option -> TreeOp<TestMsg> -> bool
        /// The trunk, genesis first.
        Read: unit -> Entry list
        /// Every record the store holds for the stream, trunk or not.
        RecordCount: unit -> int
        /// Offer the next trunk record with its op swapped AFTER its hash was
        /// computed, so the hash does not recompute from its content. Returns
        /// what the write did.
        OfferForgedHash: TreeOp<TestMsg> -> TreeOp<TestMsg> -> Outcome
        /// Offer a record linking to a predecessor the store does not hold.
        OfferDanglingLink: TreeOp<TestMsg> -> Outcome
        /// Offer the head record again.
        ReofferHead: unit -> Outcome
        /// Offer `ops` as the next trunk records in ONE atomic batch (linear
        /// `IOpStreamBatchSink.AppendAll`; DAG `IDagOpStreamBatchSink.AddAll`,
        /// then the head advanced to the last), with the last record's op swapped
        /// after its hash was computed when `forgeLast` (Phase 2117).
        OfferBatch: bool -> TreeOp<TestMsg> list -> Outcome
        /// Compact the trunk through position `k` (the head is never compacted).
        Compact: int -> unit
        /// Checkpoint `tree` as the state at the current head.
        Checkpoint: Node<TestMsg> -> unit
        /// The latest checkpoint, as the store hands it back, verifies.
        CheckpointVerifies: unit -> bool
        ReplayFromCheckpoint: Node<TestMsg> -> Result<Node<TestMsg>, string>
        ReplayFromGenesis: Node<TestMsg> -> Result<Node<TestMsg>, string>
        Streams: unit -> string list
        /// A second sink over the same storage (SQLite only — an in-memory
        /// store is not durable, by design).
        Reopen: (unit -> Harness) option
    }

let private run (a: Async<'T>) : 'T = Async.RunSynchronously a

let private attempt (write: unit -> unit) : Outcome =
    try
        write ()
        Outcome.Accepted
    with ex ->
        Outcome.Refused ex.Message

let private actor = Actor.Human "tester"

/// The hash of nothing any store holds — the predecessor of a dangling record.
let private nowhere = Fuaran.Core.Hash.sha256Hex "a predecessor no store holds"

// ─── The linear shape ───────────────────────────────────────────────────────

let rec private linearHarness
    (store: Store)
    (openSink: unit -> IOpStreamCheckpointSink<TestMsg>)
    (durable: bool)
    (streamId: string)
    : Harness =
    let sink = openSink ()
    let baseSink = sink :> IOpStreamSink<TestMsg>

    let cas =
        SinkCapabilities.tryCas baseSink
        |> Option.defaultWith (fun () -> failwithf "%A linear sink has no compare-and-append" store)

    let compact =
        SinkCapabilities.tryCompact baseSink
        |> Option.defaultWith (fun () -> failwithf "%A linear sink has no compaction" store)

    let batch =
        SinkCapabilities.tryBatch baseSink
        |> Option.defaultWith (fun () -> failwithf "%A linear sink has no atomic batch append" store)

    let latest () = sink.LatestSequence streamId |> run

    let records () =
        sink.Replay(streamId, 1, Int32.MaxValue) |> run

    let entryOf (r: OpRecord<TestMsg>) : Entry =
        { Position = r.Sequence
          Address = r.Hash
          Op = CanonicalJson.encodeOp r.Op
          Actor = r.Actor
          PromptId = r.PromptId
          Timestamp = r.Timestamp
          Tombstoned = false }

    let next (at: DateTimeOffset) (op: TreeOp<TestMsg>) : OpRecord<TestMsg> =
        linearRecord streamId (latest () + 1) (cas.Head streamId |> run) op at

    let stamp () =
        timestamp (1_800_000_000L + int64 (latest ()))

    { Shape = Linear
      Store = store
      StreamId = streamId
      Append =
        fun at op ->
            let r = next at op
            sink.Append r |> run
            r.Hash
      Head =
        fun () ->
            if latest () = 0 then
                None
            else
                Some(cas.Head streamId |> run)
      AppendIf =
        fun expected op ->
            let previous = defaultArg expected HashChain.genesisPreviousHash

            let sequence =
                match expected with
                | None -> 1
                | Some hash ->
                    match records () |> List.tryFind (fun r -> r.Hash = hash) with
                    | Some r -> r.Sequence + 1
                    | None -> failwithf "AppendIf: %s is not on the trunk" hash

            match
                cas.AppendIf(linearRecord streamId sequence previous op (stamp ()), previous)
                |> run
            with
            | CasAppendOutcome.Appended _ -> true
            | CasAppendOutcome.StaleHead _ -> false
      Read = fun () -> records () |> List.map entryOf
      RecordCount = fun () -> records () |> List.length
      OfferForgedHash =
        fun op other ->
            let forged = { next (stamp ()) op with Op = other }
            attempt (fun () -> sink.Append forged |> run)
      OfferDanglingLink =
        fun op ->
            let r = linearRecord streamId (latest () + 1) nowhere op (stamp ())
            attempt (fun () -> sink.Append r |> run)
      ReofferHead =
        fun () ->
            let head = sink.Replay(streamId, latest (), latest ()) |> run |> List.exactlyOne
            attempt (fun () -> sink.Append head |> run)
      OfferBatch =
        fun forgeLast batchOps ->
            let start = latest ()

            let records =
                batchOps
                |> List.mapi (fun i op -> i, op)
                |> List.fold
                    (fun (acc: OpRecord<TestMsg> list) (i, op) ->
                        let previous =
                            match acc with
                            | r :: _ -> r.Hash
                            | [] -> cas.Head streamId |> run

                        let at = timestamp (1_800_000_000L + int64 (start + i))
                        linearRecord streamId (start + i + 1) previous op at :: acc)
                    []
                |> List.rev

            let offered =
                match List.rev records, forgeLast with
                | last :: rest, true -> List.rev ({ last with Op = List.head batchOps } :: rest)
                | _ -> records

            attempt (fun () -> batch.AppendAll offered |> run)
      Compact = fun k -> compact.Compact(streamId, k) |> run |> ignore
      Checkpoint = fun tree -> Checkpoint.create sink streamId tree |> run |> ignore
      CheckpointVerifies =
        fun () ->
            match sink.ListCheckpoints streamId |> run |> List.tryLast with
            | Some cp -> Checkpoint.verifySnapshotHash cp = Ok()
            | None -> false
      ReplayFromCheckpoint =
        fun initial ->
            CheckpointedReplay.applyFromCheckpoint sink initial streamId (latest ())
            |> run
            |> Result.mapError (sprintf "%A")
      ReplayFromGenesis = fun initial -> Replay.applyTo initial (records ()) |> Result.mapError (sprintf "%A")
      Streams = fun () -> baseSink.Streams() |> run
      Reopen =
        if durable then
            Some(fun () -> linearHarness store openSink durable streamId)
        else
            None }

// ─── The DAG shape ──────────────────────────────────────────────────────────

let rec private dagHarness
    (store: Store)
    (openSink: unit -> IDagOpStreamSink<TestMsg>)
    (durable: bool)
    (streamId: string)
    : Harness =
    let sink = openSink ()
    let checkpoints = ResizeArray<Fuaran.Core.Dag.Checkpoint<Node<TestMsg>>>()
    let head () = sink.Head streamId |> run

    let getRecord (hash: string) = sink.TryGet(streamId, hash) |> run

    /// The stream's live records as a Core DAG.
    let dag () =
        sink.Records streamId |> run |> DagOpRecord.toDag

    /// The primary-parent spine from the head, genesis first: each step a live
    /// record, or the address and parents of a pruned one from the skeleton
    /// (Phase 2645).
    let spine () : Choice<DagOpRecord<TestMsg>, DagSkeletonNode> list =
        let skeleton =
            sink.Skeleton streamId |> run |> List.map (fun k -> k.Hash, k) |> Map.ofList

        let rec walk (hash: string) acc =
            let step parents node =
                match parents with
                | [] -> node :: acc
                | primary :: _ -> walk primary (node :: acc)

            match getRecord hash with
            | Some r -> step r.Parents (Choice1Of2 r)
            | None ->
                match Map.tryFind hash skeleton with
                | Some k -> step k.Parents (Choice2Of2 k)
                | None -> acc

        match head () with
        | None -> []
        | Some h -> walk h []

    /// The live records on the spine.
    let trunk () : DagOpRecord<TestMsg> list =
        spine ()
        |> List.choose (function
            | Choice1Of2 r -> Some r
            | Choice2Of2 _ -> None)

    let entries () =
        spine ()
        |> List.mapi (fun i node ->
            match node with
            | Choice1Of2 r ->
                { Position = i + 1
                  Address = r.Hash
                  Op = CanonicalJson.encodeOp r.Op
                  Actor = r.Actor
                  PromptId = r.PromptId
                  Timestamp = r.Timestamp
                  Tombstoned = false }
            // A pruned node keeps its address and its links in the skeleton and
            // nothing else: it reads back with no payload.
            | Choice2Of2 k ->
                { Position = i + 1
                  Address = k.Hash
                  Op = ""
                  Actor = actor
                  PromptId = None
                  Timestamp = DateTimeOffset.MinValue
                  Tombstoned = true })

    let child (parents: string list) (at: DateTimeOffset) (op: TreeOp<TestMsg>) =
        DagOpRecord.create streamId parents op None actor at OpResultEnvelope.Success

    let stamp () =
        timestamp (1_800_000_000L + int64 (List.length (spine ())))

    let headOrFail () =
        head () |> Option.defaultWith (fun () -> failwith "the stream has no head")

    { Shape = Dag
      Store = store
      StreamId = streamId
      Append =
        fun at op ->
            let current = head ()
            let r = child (Option.toList current) at op
            sink.Add r |> run

            if not (sink.TryAdvanceHead(streamId, current, r.Hash) |> run) then
                failwith "Append: the head moved under a single-threaded test"

            r.Hash
      Head = head
      AppendIf =
        fun expected op ->
            let r = child (Option.toList expected) (stamp ()) op
            sink.Add r |> run
            sink.TryAdvanceHead(streamId, expected, r.Hash) |> run
      Read = entries
      RecordCount = fun () -> sink.Records streamId |> run |> List.length
      OfferForgedHash =
        fun op other ->
            let forged =
                let r = child (Option.toList (head ())) (stamp ()) op

                { r with
                    Node =
                        { r.Node with
                            Op = { r.Entry with Op = other } } }

            attempt (fun () -> sink.Add forged |> run)
      OfferDanglingLink = fun op -> attempt (fun () -> sink.Add(child [ nowhere ] (stamp ()) op) |> run)
      ReofferHead =
        fun () ->
            let r =
                getRecord (headOrFail ())
                |> Option.defaultWith (fun () -> failwith "the head has no record")

            attempt (fun () -> sink.Add r |> run)
      OfferBatch =
        fun forgeLast batchOps ->
            let addAll =
                DagSinkCapabilities.tryBatch sink
                |> Option.defaultWith (fun () -> failwithf "%A DAG sink has no atomic batch add" store)

            let current = head ()
            let depth = List.length (spine ())

            let records =
                batchOps
                |> List.mapi (fun i op -> i, op)
                |> List.fold
                    (fun (acc: DagOpRecord<TestMsg> list) (i, op) ->
                        let parent =
                            match acc with
                            | previous :: _ -> Some previous.Hash
                            | [] -> current

                        child (Option.toList parent) (timestamp (1_800_000_000L + int64 (depth + i))) op
                        :: acc)
                    []
                |> List.rev

            let offered =
                match List.rev records, forgeLast with
                | last :: rest, true ->
                    let forged =
                        { last with
                            Node =
                                { last.Node with
                                    Op =
                                        { last.Entry with
                                            Op = List.head batchOps } } }

                    List.rev (forged :: rest)
                | _ -> records

            attempt (fun () ->
                addAll.AddAll offered |> run

                match List.tryLast offered with
                | Some last when not (sink.TryAdvanceHead(streamId, current, last.Hash) |> run) ->
                    failwith "OfferBatch: the head moved under a single-threaded test"
                | _ -> ())
      // Core's compaction (`Dag.compactAt`): the history behind a retained
      // checkpoint goes and the checkpoint's own node is KEPT, so the replay
      // above it and its seal still resolve; every other covered node is pruned
      // to the skeleton.
      Compact =
        fun k ->
            let kept = checkpoints |> Seq.map _.Node |> Set.ofSeq

            for r in trunk () |> List.truncate k do
                if not (kept.Contains r.Hash) && not (sink.Tombstone(streamId, r.Hash) |> run) then
                    failwithf "Compact: the %A DAG sink did not prune trunk node %s" store r.Hash
      Checkpoint = fun tree -> checkpoints.Add(DagFold.sealAt (headOrFail ()) tree)
      CheckpointVerifies =
        fun () ->
            checkpoints.Count > 0
            && DagFold.verifyCheckpoint checkpoints[checkpoints.Count - 1] (dag ())
               |> Result.isOk
      ReplayFromCheckpoint =
        fun _ ->
            DagFold.replayFrom checkpoints[checkpoints.Count - 1] (dag ()) (headOrFail ())
            |> Result.mapError (sprintf "%A")
      ReplayFromGenesis =
        fun initial ->
            DagFold.replayTo initial (dag ()) (headOrFail ())
            |> Result.mapError (sprintf "%A")
      Streams = fun () -> sink.Streams() |> run
      Reopen =
        if durable then
            Some(fun () -> dagHarness store openSink durable streamId)
        else
            None }

// ─── The cells ──────────────────────────────────────────────────────────────

type Cell =
    { Shape: Shape
      Store: Store }

    member this.Name =
        sprintf
            "%s/%s"
            (if this.Shape = Linear then "linear" else "DAG")
            (if this.Store = InMemory then "in-memory" else "SQLite")

let cells: Cell list =
    [ for shape in [ Linear; Dag ] do
          for store in [ InMemory; Sqlite ] do
              { Shape = shape; Store = store } ]

/// Open a fresh store for `cell`, hand `body` a harness over stream `streamId`
/// in it, and remove the store's file afterwards. Every case gets its own store.
let withHarness (cell: Cell) (streamId: string) (body: Harness -> unit) : unit =
    let path =
        Path.Combine(Path.GetTempPath(), sprintf "fuaran-sink-contract-%s.db" (Guid.NewGuid().ToString("N")))

    // `Pooling=False` so the file is released when the last connection closes.
    let connectionString = sprintf "Data Source=%s;Pooling=False" path

    let harness =
        match cell.Shape, cell.Store with
        | Linear, InMemory ->
            let sink = InMemorySink.createWithCheckpoints<TestMsg> ()
            linearHarness InMemory (fun () -> sink) false streamId
        | Linear, Sqlite ->
            linearHarness
                Sqlite
                (fun () -> SqliteSink.createWithCheckpoints connectionString testCodec testNodeCodec)
                true
                streamId
        | Dag, InMemory ->
            let sink = InMemoryDagSink.create<TestMsg> ()
            dagHarness InMemory (fun () -> sink) false streamId
        | Dag, Sqlite -> dagHarness Sqlite (fun () -> SqliteDagSink.create connectionString testCodec) true streamId

    try
        body harness
    finally
        for file in [ path; path + "-wal"; path + "-shm" ] do
            try
                if File.Exists file then
                    File.Delete file
            with _ ->
                ()
