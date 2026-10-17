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
//   | read the trunk     | `Replay(1, max)`                        | `TryGet` along the primary parents from the head |
//   | compaction         | `IOpStreamCompactSink.Compact`          | `Tombstone` the trunk records at or below it     |
//   | checkpoint         | `Checkpoint.create` into the sink       | `DagCheckpoint.create`, held by the caller       |
//   | replay from it     | `CheckpointedReplay.applyFromCheckpoint`| `DagReplay.replayFromCheckpoint`                 |
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
        /// what the write did, and the forged record's address.
        OfferForgedHash: TreeOp<TestMsg> -> TreeOp<TestMsg> -> Outcome * string
        /// Offer a record linking to a predecessor the store does not hold.
        OfferDanglingLink: TreeOp<TestMsg> -> Outcome
        /// Offer the head record again.
        ReofferHead: unit -> Outcome
        /// Read the record at `address` back through the store's read path.
        ReadBack: string -> Outcome
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
            attempt (fun () -> sink.Append forged |> run), forged.Hash
      OfferDanglingLink =
        fun op ->
            let r = linearRecord streamId (latest () + 1) nowhere op (stamp ())
            attempt (fun () -> sink.Append r |> run)
      ReofferHead =
        fun () ->
            let head = sink.Replay(streamId, latest (), latest ()) |> run |> List.exactlyOne
            attempt (fun () -> sink.Append head |> run)
      ReadBack =
        fun address ->
            attempt (fun () ->
                if not (records () |> List.exists (fun r -> r.Hash = address)) then
                    invalidOp (sprintf "no record at %s" address))
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
    let checkpoints = ResizeArray<DagCheckpoint<TestMsg>>()
    let head () = sink.Head streamId |> run

    let getRecord (hash: string) = sink.TryGet(streamId, hash) |> run

    /// The primary-parent spine from the head, genesis first.
    let trunk () : DagOpRecord<TestMsg> list =
        let rec walk (hash: string) (acc: DagOpRecord<TestMsg> list) =
            match getRecord hash with
            | None -> acc
            | Some r ->
                match r.Parents with
                | [] -> r :: acc
                | primary :: _ -> walk primary (r :: acc)

        match head () with
        | None -> []
        | Some h -> walk h []

    let entries () =
        trunk ()
        |> List.mapi (fun i r ->
            { Position = i + 1
              Address = r.Hash
              Op = CanonicalJson.encodeOp r.Op
              Actor = r.Actor
              PromptId = r.PromptId
              Timestamp = r.Timestamp
              Tombstoned = r.Tombstoned })

    let child (parents: string list) (at: DateTimeOffset) (op: TreeOp<TestMsg>) =
        DagOpRecord.create streamId parents op None actor at OpResultEnvelope.Success

    let stamp () =
        timestamp (1_800_000_000L + int64 (List.length (trunk ())))

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
                { child (Option.toList (head ())) (stamp ()) op with
                    Op = other }

            attempt (fun () -> sink.Add forged |> run), forged.Hash
      OfferDanglingLink = fun op -> attempt (fun () -> sink.Add(child [ nowhere ] (stamp ()) op) |> run)
      ReofferHead =
        fun () ->
            let r =
                getRecord (headOrFail ())
                |> Option.defaultWith (fun () -> failwith "the head has no record")

            attempt (fun () -> sink.Add r |> run)
      ReadBack =
        fun address ->
            attempt (fun () ->
                if Option.isNone (getRecord address) then
                    invalidOp (sprintf "no record at %s" address))
      Compact =
        fun k ->
            for r in trunk () |> List.truncate k do
                sink.Tombstone(streamId, r.Hash) |> run |> ignore
      Checkpoint = fun tree -> checkpoints.Add(DagCheckpoint.create streamId (headOrFail ()) tree)
      CheckpointVerifies =
        fun () ->
            checkpoints.Count > 0
            && DagCheckpoint.verifySnapshotHash checkpoints[checkpoints.Count - 1] = Ok()
      ReplayFromCheckpoint =
        fun _ ->
            DagReplay.replayFromCheckpoint getRecord checkpoints[checkpoints.Count - 1] (headOrFail ())
            |> Result.mapError (sprintf "%A")
      ReplayFromGenesis =
        fun initial ->
            DagReplay.replay getRecord initial (headOrFail ())
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
