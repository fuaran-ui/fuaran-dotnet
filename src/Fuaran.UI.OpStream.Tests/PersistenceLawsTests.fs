module Fuaran.UI.OpStream.Tests.PersistenceLawsTests

// Phase 1477 — the persistence law families over the tier's op-stream witness, and the two durable
// ports' own account of the same three claims.
//
// The phase names three contracts a durable port promises a consumer: a stale-head append is
// refused naming the current head (`Conformance.casLaws`, fuaran-core#79), a repeated append is a
// no-op returning the same receipt (`Conformance.idempotencyLaws`, #82), and a snapshot replays to
// the same state as the stream it summarises (`Conformance.snapshotLaws` / `snapshotLawsWith`).
//
// ── WHAT THE KIT'S SHAPE ALLOWS, STATED BEFORE THE FIRST LAW CALL ─────────────────────────────
//
// All three families are parameterised over a `StreamWitness<'Op,'State,'Rej>`, and that witness is
// exactly three functions: `Apply` (the domain reducer), `Encode` and `Decode` (the domain op
// codec). The APPEND is Core's own — `OpStream.appendIf`, `OpStream.appendIdempotent`,
// `OpStream.compact` — over an immutable `OpRecord<'Op> list`. There is no port, sink or store seam
// anywhere in the three signatures. So a law here cannot be made to exercise `IOpStreamSink.Append`
// without a wrapper that pretends the law took a store it never took, and the phase's discipline
// forbids exactly that.
//
// The tier's side of the same gap is sharper, and it is a finding about the PORT rather than about
// the kit. `IOpStreamSink` offers `Append` / `Replay` / `LatestSequence` / `Streams`:
//
//   * it has NO compare-and-append — nothing accepts an expected head, and nothing can report the
//     current head on refusing one. Its only append-time guard is duplicate `(StreamId, Sequence)`,
//     and that guard THROWS rather than returning a typed rejection naming the head;
//   * it has NO idempotency key — a re-sent append is not recognised as a retry at all, so there is
//     no receipt to return unchanged;
//   * it DOES have the snapshot surface (`IOpStreamCheckpointSink` + `CheckpointedReplay` +
//     `Compaction`), which is why the third claim has a real tier-shaped counterpart below and the
//     first two have honest negative ones.
//
// So this file is in two halves, and neither is dressed up as the other:
//
//   A. the three families instantiated over the tier's REAL witness — `Ops.Apply.apply` as the
//      reducer, the shipped canonical-JSON op codec, the shipped SHA-256 chain `HashFn`
//      (`StreamEntry.hashFn`), the shipped node encoder as `stateEncode`. A defect in any of those
//      turns the laws red, which is what makes this adoption rather than a re-run of Core's suite.
//      `snapshotLawsWith` is a second, genuinely different instantiation: the same laws over the
//      tier's shipped provenance envelope (`StreamEntry` + `StreamEntry.encode`), the `'Op` the
//      persisted chain is actually hashed over.
//
//   B. the two durable ports, over both stores, asserting what they GENUINELY do about the same
//      three claims — bounded replay across a compacted checkpoint (the claim they honour), the
//      duplicate-sequence refusal and the absent head check (the claim they do not), and the
//      retry-after-append-failure property of the Phase 124 both-sinks wrapper together with the
//      pinned gap that a retry after a TELEMETRY failure double-appends.

open System
open System.IO
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.InMemory
open Fuaran.UI.OpStream.Sqlite
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.Telemetry.Abstractions

module UiApply = Fuaran.UI.Ops.Apply
module Introspect = Fuaran.UI.Ops.Introspect
module JsonDecode = Fuaran.UI.Ops.JsonDecode
module CoreConf = Fuaran.Core.Conformance
module CoreStream = Fuaran.Core.OpStream
module CoreRng = Fuaran.Core.ConfRng

// ---------------------------------------------------------------------------
//  A — the witness the law families run over
// ---------------------------------------------------------------------------

/// `Node<'Msg>` embeds message-handler closures, so it is not an F# equality type, and the law
/// families compare `'State` with `=`. The comparison seam stays wholly on the tier's side: two
/// trees are equal iff they serialise identically through the canonical wire encoding — the same
/// notion the wire-format corpus uses. (The construction mirrors `CoreAdoptionTests.fs`; test
/// projects cannot reference each other, so the minimal pieces are re-stated here rather than a
/// second, differently-shaped witness being invented.)
[<CustomEquality; NoComparison>]
type EqNode =
    { Node: Node<obj> }

    override this.Equals(o: obj) =
        match o with
        | :? EqNode as other -> CanonicalJson.encodeNode this.Node = CanonicalJson.encodeNode other.Node
        | _ -> false

    override this.GetHashCode() =
        (CanonicalJson.encodeNode this.Node).GetHashCode()

let private wrap (n: Node<obj>) : EqNode = { Node = n }
let private unwrap (e: EqNode) : Node<obj> = e.Node

let private mkStack (id: string) (kids: EqNode list) : EqNode =
    wrap
        { Id = id
          Kind =
            NodeKind.Box(
                { Layout = LayoutMode.Flex(Orientation.Vertical, false, None)
                  Role = BoxRole.Group
                  Heading = None
                  Children = kids |> List.map unwrap
                  KeepTogether = false
                  BreakBefore = false }
            )
          State = None
          Style = None
          Accessibility = Option.None
          Motion = Option.None
          ExtraAttributes = Option.None
          Tooltip = None }

let private mkLeaf (id: string) : EqNode =
    wrap
        { Id = id
          Kind = NodeKind.Markdown({ Text = TextSource.Literal "" })
          State = None
          Style = None
          Accessibility = Option.None
          Motion = Option.None
          ExtraAttributes = Option.None
          Tooltip = None }

/// `TreeOp<'Msg>` carries handler closures in its node payloads for exactly the reason `Node<'Msg>`
/// does, so it is not an equality type either — and `casLaws` / `idempotencyLaws` compare whole
/// `OpRecord<'Op>` lists (chain-identity between `appendIf`/`appendIdempotent` and `append`). The
/// same seam answers it: two ops are equal iff they encode identically through the canonical op
/// encoder, which is the notion the wire corpus and the chain pre-image already use.
[<CustomEquality; NoComparison>]
type EqOp =
    { Op: TreeOp<obj> }

    override this.Equals(o: obj) =
        match o with
        | :? EqOp as other -> CanonicalJson.encodeOp this.Op = CanonicalJson.encodeOp other.Op
        | _ -> false

    override this.GetHashCode() =
        (CanonicalJson.encodeOp this.Op).GetHashCode()

/// The two-seam witness: the tier's real apply engine + its real canonical-JSON op codec. Every law
/// below is bound to these, so perturbing either turns the families red — the go-red proof the
/// phase owes for a parameterised family.
let private coreSw: Fuaran.Core.StreamWitness<TreeOp<obj>, EqNode, ApplyError> =
    { Apply = fun op e -> UiApply.apply op e.Node |> Result.map wrap
      Encode = CanonicalJson.encodeOp
      Decode = fun s -> JsonDecode.decodeOp s |> Result.mapError (sprintf "%A") }

/// The same witness at the equality-bearing op wrapper, for the two families that compare records.
let private eqOpSw: Fuaran.Core.StreamWitness<EqOp, EqNode, ApplyError> =
    { Apply = fun eop e -> UiApply.apply eop.Op e.Node |> Result.map wrap
      Encode = fun eop -> CanonicalJson.encodeOp eop.Op
      Decode =
        fun s ->
            JsonDecode.decodeOp s
            |> Result.map (fun op -> { Op = op })
            |> Result.mapError (sprintf "%A") }

let private baseTree = mkStack "root" [ mkLeaf "s0"; mkLeaf "s1" ]

/// A generator mixing accepted ops with typed rejections (a duplicate-id insert, a remove of an id
/// already gone, a remove of an id that never existed) so the CAS's "a domain reject is not a CAS
/// outcome" branch and the idempotency laws' rejection-forwarding branch both see real input.
let private genStreamOp (rng: CoreRng.T) : TreeOp<obj> * CoreRng.T =
    let pick, r1 = CoreRng.intBelow 3 rng

    match pick with
    | 0 ->
        let v, r2 = CoreRng.next r1
        TreeOp.InsertChild(NodeId "root", unwrap (mkLeaf (sprintf "g%d" (v % 50)))), r2
    | 1 -> TreeOp.RemoveNode(NodeId "s0"), r1
    | _ -> TreeOp.RemoveNode(NodeId "ghost"), r1

let private streamGen: Fuaran.Core.StreamGen<TreeOp<obj>, EqNode> =
    { State0 = baseTree; Op = genStreamOp }

let private eqOpStreamGen: Fuaran.Core.StreamGen<EqOp, EqNode> =
    { State0 = baseTree
      Op =
        fun rng ->
            let op, r' = genStreamOp rng
            { Op = op }, r' }

/// The tier's shipped chain digest — the portable SHA-256 `HashFn` the persisted op stream is
/// actually hashed under (Phase 405/406), not Core's FNV-1a default.
let private hashFn: Fuaran.Core.HashFn = StreamEntry.hashFn

/// The tier's shipped canonical node encoder, as the snapshot laws' `stateEncode`. The snapshot's
/// integrity claim is only as good as this encoder, which is the same one `Checkpoint.SnapshotHash`
/// is computed over — so the law and the durable port hash the same bytes.
let private stateEncode (e: EqNode) : string = CanonicalJson.encodeNode e.Node

/// The op → invocation-key projection `idempotencyLaws` is parameterised by. The tier ships no
/// invocation-key surface of its own (that is the gap this file records), so the honest projection
/// is the canonical op encoding: two byte-identical ops are retries of one invocation. Core's
/// contract for `keyOf` is explicit that a colliding projection is certified AS GIVEN — the laws
/// only exercise keys this function yields — so this is a statement about the tier's ops, not a
/// borrowed guarantee.
let private keyOf (eop: EqOp) : string = CanonicalJson.encodeOp eop.Op

let private assertAllPassed (context: string) (results: Fuaran.Core.LawResult list) =
    let failures = results |> List.filter (fun r -> not r.Passed)

    if not (List.isEmpty failures) then
        failures
        |> List.map (fun r -> sprintf "  %s — %A" r.Law r.Counterexample)
        |> String.concat "\n"
        |> failtestf "%s failed:\n%s" context

// ---- the StreamEntry instantiation (the `'Op` the persisted chain is hashed over) ----

let private entryOf (op: TreeOp<obj>) : StreamEntry<obj> =
    { Op = op
      Timestamp = DateTimeOffset.FromUnixTimeSeconds 1_700_000_000L
      PromptId = None
      ResultEnvelope = OpResultEnvelope.Success }

let private entrySw: Fuaran.Core.StreamWitness<StreamEntry<obj>, EqNode, ApplyError> =
    { Apply = fun entry e -> UiApply.apply entry.Op e.Node |> Result.map wrap
      Encode = StreamEntry.encode
      Decode = StreamEntry.decode (fun raw -> JsonDecode.decodeOp raw |> Result.mapError (sprintf "%A")) }

let private entryStreamGen: Fuaran.Core.StreamGen<StreamEntry<obj>, EqNode> =
    { State0 = baseTree
      Op =
        fun rng ->
            let op, r' = genStreamOp rng
            entryOf op, r' }

// ---------------------------------------------------------------------------
//  B — the two durable ports
// ---------------------------------------------------------------------------

/// The tier's real shipped codecs at the erased `'Msg`, so the SQLite round-trip below exercises
/// `CanonicalJson` + `JsonDecode` rather than a test-only format. (`TestSupport.testCodec` covers
/// only four closure-free op shapes and cannot decode a node at all, which is why the existing
/// checkpoint suite could not read a snapshot back out of SQLite.)
let private opCodec: IOpJsonCodec<obj> =
    { new IOpJsonCodec<obj> with
        member _.EncodeOp op = CanonicalJson.encodeOp op

        member _.DecodeOp json =
            JsonDecode.decodeOp json |> Result.mapError (sprintf "%A") }

let private nodeCodec: INodeJsonCodec<obj> =
    { new INodeJsonCodec<obj> with
        member _.EncodeNode node = CanonicalJson.encodeNode node

        member _.DecodeNode json =
            JsonDecode.decodeNodeObj json |> Result.mapError (sprintf "%A") }

let private freshDbPath () : string =
    Path.Combine(Path.GetTempPath(), sprintf "fuaran-persistence-laws-%s.db" (Guid.NewGuid().ToString("N")))

/// Run `body` against each durable port in turn. The SQLite leg owns a temp database per run,
/// deleted in `finally` (best-effort: Microsoft.Data.Sqlite holds its pool open, so deletion may
/// race — the file is small and the name is unique per run).
let private overBothStores (body: string -> IOpStreamCheckpointSink<obj> -> unit) : unit =
    body "InMemory" (InMemorySink.createWithCheckpoints<obj> ())

    let path = freshDbPath ()

    try
        body "Sqlite" (SqliteSink.createWithCheckpoints<obj> (sprintf "Data Source=%s" path) opCodec nodeCodec)
    finally
        try
            File.Delete path
        with _ ->
            ()

let private storeTree () : Node<obj> =
    unwrap (mkStack "dash" [ mkLeaf "left"; mkLeaf "right" ])

/// `TestSupport.buildRecord` is fixed at `TestMsg`; the store legs below run at the erased `'Msg`
/// so they can use the tier's shipped codecs. Same construction, same single hash authority
/// (`HashChain.computeHash`) — only the message type differs.
let private buildObjRecord
    (streamId: string)
    (sequence: int)
    (op: TreeOp<obj>)
    (previous: OpRecord<obj> option)
    (timestamp: DateTimeOffset)
    : OpRecord<obj> =
    let previousHash =
        match previous with
        | None -> HashChain.genesisPreviousHash
        | Some prev -> prev.Hash

    let actor = Actor.Human "tester"

    let hash =
        HashChain.computeHash previousHash op sequence timestamp actor None OpResultEnvelope.Success

    { StreamId = streamId
      Sequence = sequence
      PreviousHash = previousHash
      Hash = hash
      Op = op
      PromptId = None
      Actor = actor
      Timestamp = timestamp
      ResultEnvelope = OpResultEnvelope.Success }

let private storeOps: TreeOp<obj> list =
    [ TreeOp.RemoveNode(NodeId "right")
      TreeOp.InsertChild(NodeId "dash", unwrap (mkLeaf "middle"))
      TreeOp.InsertChild(NodeId "dash", unwrap (mkLeaf "header"))
      TreeOp.RemoveNode(NodeId "left")
      TreeOp.InsertChild(NodeId "dash", unwrap (mkLeaf "footer")) ]

/// A telemetry sink that records nothing and never throws — the neutral second sink for the
/// `applyWithSinks` fan-out, where the op-stream half is what is under test.
type private QuietTelemetrySink() =
    interface IFuaranTelemetrySink with
        member _.RecordOpApply _ = ()
        member _.RecordDeny _ = ()
        member _.RecordRenderFailure _ = ()
        member _.RecordProviderCall _ = ()
        member _.RecordCacheStat _ = ()
        member _.RecordValidateOutcome _ = ()

/// A telemetry sink whose `RecordOpApply` throws — the failure `applyWithSinks` swallows.
type private ThrowingTelemetry() =
    interface IFuaranTelemetrySink with
        member _.RecordOpApply _ =
            invalidOp "ThrowingTelemetry: simulated telemetry failure."

        member _.RecordDeny _ = ()
        member _.RecordRenderFailure _ = ()
        member _.RecordProviderCall _ = ()
        member _.RecordCacheStat _ = ()
        member _.RecordValidateOutcome _ = ()

/// A durable sink whose next `Append` throws once, then delegates. The transient-durability
/// failure a retry loop is written for; every other member delegates unchanged, so
/// `LatestSequence` reports what the inner store actually holds.
type private FailNextAppend<'Msg>(inner: IOpStreamSink<'Msg>) =
    let mutable armed = false

    member _.Arm() = armed <- true

    interface IOpStreamSink<'Msg> with
        member _.Append(record: OpRecord<'Msg>) : Async<unit> =
            async {
                if armed then
                    armed <- false
                    invalidOp "FailNextAppend: simulated transient durability failure."

                return! inner.Append record
            }

        member _.Replay(streamId: string, fromSequence: int, toSequence: int) =
            inner.Replay(streamId, fromSequence, toSequence)

        member _.LatestSequence(streamId: string) = inner.LatestSequence streamId
        member _.Streams() = inner.Streams()

// ---------------------------------------------------------------------------
//  the tests
// ---------------------------------------------------------------------------

[<Tests>]
let tests =
    testList
        "Fuaran.UI.OpStream — persistence laws (Core conformance) + the durable ports"
        [

          // ---- A. the kit's families over the tier's witness ----

          testCase "casLaws certifies over the Fuaran.UI op-stream witness"
          <| fun _ ->
              // Compare-and-append (fuaran-core#79): `appendIf` at the true head is exactly
              // `append`; at a stale head it is `StaleHead (expected, actual)` naming the real head
              // with the stream untouched; and two writers off one base admit exactly one winner
              // under either serialisation. Bound to the tier by the witness — its reducer, its op
              // encoding, its SHA-256 chain digest.
              CoreConf.casLaws eqOpSw eqOpStreamGen hashFn 20260904 100
              |> assertAllPassed "casLaws over the Fuaran.UI apply/codec witness"

          testCase "idempotencyLaws certifies over the Fuaran.UI op-stream witness"
          <| fun _ ->
              // Idempotent append (fuaran-core#82): a fresh key is chain-identical to `append`, a
              // seen key converges on `Duplicate` naming the entry it FIRST produced, the rebuilt
              // key index agrees with the incrementally-maintained one, and idempotency precedes
              // the CAS so a lost-ack retry terminates even under a stale head.
              CoreConf.idempotencyLaws keyOf eqOpSw eqOpStreamGen hashFn 20260904 100
              |> assertAllPassed "idempotencyLaws over the Fuaran.UI apply/codec witness"

          testCase "snapshotLaws and snapshotLawsWith certify over the Fuaran.UI op-stream witness"
          <| fun _ ->
              // Bounded replay: `replayFrom` a compacted checkpoint equals `replay` from origin,
              // and `verifyAcross` accepts an intact (snapshot, tail) boundary. `stateEncode` is
              // the tier's shipped canonical node encoder — the same bytes `Checkpoint.SnapshotHash`
              // is taken over, so the law and the durable port below agree about what a snapshot is.
              CoreConf.snapshotLaws coreSw streamGen stateEncode hashFn 20260904 100
              |> assertAllPassed "snapshotLaws over the Fuaran.UI apply/codec witness"

              // A second, genuinely different instantiation rather than a re-run under a renamed
              // entry point: the `'Op` here is the tier's shipped provenance envelope, which is what
              // the persisted chain is actually hashed over (`StreamEntry.encode`, chain format v2),
              // and the config is named explicitly as the canonical `{seq,actor,op}` binding the
              // tier's `HashChain.computeHash` composes.
              CoreConf.snapshotLawsWith
                  CoreStream.canonicalConfig
                  entrySw
                  entryStreamGen
                  stateEncode
                  hashFn
                  20260904
                  100
              |> assertAllPassed "snapshotLawsWith over the Fuaran.UI StreamEntry provenance envelope"

          // ---- B. the durable ports' own account of the same three claims ----

          testCase "bounded replay holds over both durable stores (snapshot resumes to the genesis-replay state)"
          <| fun _ ->
              // The snapshot claim, tier-shaped: a checkpoint materialised through the real port,
              // the real retention policy applied so the prefix is genuinely gone, and
              // replay-from-checkpoint compared against the replay-from-genesis state captured
              // before compaction. Runs over BOTH stores; the SQLite leg round-trips the snapshot
              // through the shipped node codec, which is what makes it a durability claim rather
              // than an in-process one.
              overBothStores (fun storeName sink ->
                  let streamId = "bounded-" + storeName
                  let baseSink = sink :> IOpStreamSink<obj>
                  let ctx = PersistContext.create streamId "tester"
                  let mutable tree = storeTree ()
                  let mutable applied = 0

                  for op in storeOps do
                      match ApplyPersist.applyAndPersist baseSink ctx op tree |> Async.RunSynchronously with
                      | Ok updated ->
                          tree <- updated
                          applied <- applied + 1

                          // Two checkpoints, so `keep 1` has something to drop and the truncation
                          // below is a real compaction rather than a no-op.
                          if applied = 2 || applied = 4 then
                              Checkpoint.create sink streamId tree |> Async.RunSynchronously |> ignore
                      | Error e -> failtestf "%s: apply+persist failed on %A: %A" storeName op e

                  let fromGenesis = CanonicalJson.encodeNode tree

                  let truncated =
                      Compaction.applyPolicy sink streamId (CompactionPolicy.keep 1)
                      |> Async.RunSynchronously

                  Expect.equal
                      truncated
                      4
                      (sprintf "%s: compaction truncated the ops collapsed into the retained checkpoint" storeName)

                  match
                      CheckpointedReplay.applyFromCheckpoint sink (storeTree ()) streamId 5
                      |> Async.RunSynchronously
                  with
                  | Ok resumed ->
                      Expect.equal
                          (CanonicalJson.encodeNode resumed)
                          fromGenesis
                          (sprintf "%s: replay from the retained checkpoint == replay from genesis" storeName)
                  | Error e -> failtestf "%s: replay from checkpoint failed: %A" storeName e)

          testCase "neither durable store offers a compare-and-append — the head check is a READ-path check"
          <| fun _ ->
              // The honest negative, and the reason `casLaws` above could not be pointed at the
              // stores. `IOpStreamSink.Append` takes no expected head and returns no receipt, so:
              //   * a duplicate (StreamId, Sequence) is refused by THROWING — a structural defect,
              //     not a typed `StaleHead (expected, actual)` a caller could act on; and
              //   * a record whose `PreviousHash` does not link to the current head is ACCEPTED at
              //     append time. The mis-chain surfaces later, on the read path, where
              //     `LoadVerification.Full` re-verifies the segment being handed out.
              // Both halves are asserted so that giving either store a real CAS arrives here as a
              // failing test rather than as silently unexercised new surface.
              overBothStores (fun storeName sink ->
                  let streamId = "cas-" + storeName
                  let baseSink = sink :> IOpStreamSink<obj>
                  let op1 = TreeOp.RemoveNode(NodeId "right"): TreeOp<obj>
                  let op2 = TreeOp.RemoveNode(NodeId "left"): TreeOp<obj>

                  let r1 =
                      buildObjRecord streamId 1 op1 None (DateTimeOffset.FromUnixTimeSeconds 100L)

                  baseSink.Append r1 |> Async.RunSynchronously

                  // (a) a second record at a sequence the store already holds is refused — the only
                  //     append-time guard the port has, and it throws.
                  let clash =
                      buildObjRecord streamId 1 op2 None (DateTimeOffset.FromUnixTimeSeconds 200L)

                  Expect.throws
                      (fun () -> baseSink.Append clash |> Async.RunSynchronously)
                      (sprintf "%s: a duplicate (StreamId, Sequence) is refused" storeName)

                  // (b) a stale-head record at a FRESH sequence is accepted: the store never looks
                  //     at `PreviousHash` on append. `None` chains it to the genesis hash, which is
                  //     not record 1's hash, so the link is wrong by construction.
                  let staleHeaded =
                      buildObjRecord streamId 2 op2 None (DateTimeOffset.FromUnixTimeSeconds 300L)

                  baseSink.Append staleHeaded |> Async.RunSynchronously

                  Expect.equal
                      (baseSink.LatestSequence streamId |> Async.RunSynchronously)
                      2
                      (sprintf "%s: the stale-head append was accepted (no append-time head check)" storeName)

                  // (c) and the read path is where it is caught.
                  Expect.throws
                      (fun () -> baseSink.Replay(streamId, 1, 10) |> Async.RunSynchronously |> ignore)
                      (sprintf "%s: the mis-chained segment is refused on read, not on append" storeName))

          testCase "a retry after a durability failure does not double-append through the both-sinks wrapper"
          <| fun _ ->
              // The Phase 124 wrapper's half of the idempotency claim, and the only half its shape
              // admits. `applyWithSinks` derives the sequence from `LatestSequence + 1` and swallows
              // an `Append` throw (durability is best-effort by contract), so a failed append leaves
              // the sequence unadvanced and the retry lands exactly one record. That is a real
              // no-double-append property — but it holds BECAUSE the first attempt persisted
              // nothing, never because a retry was recognised as one.
              overBothStores (fun storeName inner ->
                  let streamId = "retry-" + storeName
                  let flaky = FailNextAppend<obj>(inner :> IOpStreamSink<obj>)
                  let sink = flaky :> IOpStreamSink<obj>
                  let telemetry = QuietTelemetrySink() :> IFuaranTelemetrySink
                  let ctx = PersistContext.create streamId "tester"
                  let tree = storeTree ()
                  let op = TreeOp.RemoveNode(NodeId "right"): TreeOp<obj>

                  flaky.Arm()

                  match ApplyPersist.applyWithSinks sink telemetry ctx op tree |> Async.RunSynchronously with
                  | Ok _ -> ()
                  | Error e -> failtestf "%s: the apply path must survive a sink failure, got %A" storeName e

                  Expect.equal
                      (sink.LatestSequence streamId |> Async.RunSynchronously)
                      0
                      (sprintf "%s: the failed append persisted nothing" storeName)

                  match ApplyPersist.applyWithSinks sink telemetry ctx op tree |> Async.RunSynchronously with
                  | Ok _ -> ()
                  | Error e -> failtestf "%s: the retry failed: %A" storeName e

                  let records = sink.Replay(streamId, 1, 100) |> Async.RunSynchronously

                  Expect.equal
                      (records |> List.map _.Sequence)
                      [ 1 ]
                      (sprintf "%s: the retry appended exactly one record, at sequence 1" storeName))

          testCase "a retry after a TELEMETRY failure DOES double-append — the wrapper carries no invocation key"
          <| fun _ ->
              // The gap `idempotencyLaws` names, pinned rather than left to be rediscovered. A
              // telemetry throw is swallowed AFTER the op-stream append has already committed, so a
              // caller that reads the throw as "the call failed" and retries appends a second time:
              // the wrapper has no invocation key and cannot tell a retry from a fresh op. This
              // asserts the CURRENT behaviour, so giving the wrapper a key turns it red at the
              // moment the behaviour changes rather than months later.
              overBothStores (fun storeName sink ->
                  let streamId = "telemetry-retry-" + storeName
                  let telemetry = ThrowingTelemetry() :> IFuaranTelemetrySink
                  let ctx = PersistContext.create streamId "tester"
                  let tree = storeTree ()
                  let op = TreeOp.RemoveNode(NodeId "right"): TreeOp<obj>

                  for _ in 1..2 do
                      match
                          ApplyPersist.applyWithSinks (sink :> IOpStreamSink<obj>) telemetry ctx op tree
                          |> Async.RunSynchronously
                      with
                      | Ok _ -> ()
                      | Error e -> failtestf "%s: a telemetry throw must not break the apply path, got %A" storeName e

                  let records =
                      (sink :> IOpStreamSink<obj>).Replay(streamId, 1, 100) |> Async.RunSynchronously

                  Expect.equal
                      (records |> List.map _.Sequence)
                      [ 1; 2 ]
                      (sprintf
                          "%s: two records — the second call was not recognised as a retry (no invocation key)"
                          storeName)) ]
