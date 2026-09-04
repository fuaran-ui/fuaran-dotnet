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
// The tier's side of the same gap was sharper, and it was a finding about the PORT rather than
// about the kit: `IOpStreamSink` offered `Append` / `Replay` / `LatestSequence` / `Streams` and no
// more, so it had no compare-and-append (nothing accepted an expected head, and its one
// append-time guard THREW rather than returning a typed rejection naming the head) and no
// idempotency key (a re-sent append was not recognised as a retry, so there was no receipt to
// return unchanged). Only the snapshot claim had a real tier-shaped counterpart.
//
// ── WHAT fuaran#1485 CHANGED ──────────────────────────────────────────────────────────────────
//
// The two missing contracts now exist, as OPTIONAL extension interfaces beside the base rather
// than as members on it (`IOpStreamCasSink` / `IOpStreamKeyedSink`; the
// `IOpStreamCheckpointSink` shape, which is what keeps the addition additive for an external
// implementor). Both shipped stores implement both, and `ApplyPersist.applyWithSinksKeyed` is the
// wrapper that threads a caller-supplied invocation key. So the three claims that this file could
// once only pin as negatives are asserted below as the contract they are — and the no-expectation
// path is asserted UNCHANGED beside them, because keeping it is a deliberate part of the design
// rather than an omission.
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
//      three claims — bounded replay across a compacted checkpoint, the typed compare-and-append
//      and its stale-head refusal, the keyed append's receipt on a re-send, a store-shaped
//      property run of both in the shape of `casLaws` / `idempotencyLaws`, and the two
//      retry properties of the Phase 124 both-sinks wrapper: after a durability failure, and
//      after a telemetry failure through the keyed entry point.

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

/// The same witness at the equality-bearing op wrapper, for the families that compare records.
///
/// NOT private, and deliberately: `CoreAttestationLawsTests.fs` (fuaran#1480) binds the attributed
/// and attestation families to THIS witness rather than constructing a second one over the same op
/// algebra — two witnesses for one stream could disagree, and then a green law would be a statement
/// about whichever of them the reader happened to open. Same reason for `eqOpStreamGen`, `hashFn`
/// and `assertAllPassed` below.
let eqOpSw: Fuaran.Core.StreamWitness<EqOp, EqNode, ApplyError> =
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

let eqOpStreamGen: Fuaran.Core.StreamGen<EqOp, EqNode> =
    { State0 = baseTree
      Op =
        fun rng ->
            let op, r' = genStreamOp rng
            { Op = op }, r' }

/// The tier's shipped chain digest — the portable SHA-256 `HashFn` the persisted op stream is
/// actually hashed under (Phase 405/406), not Core's FNV-1a default.
let hashFn: Fuaran.Core.HashFn = StreamEntry.hashFn

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

let assertAllPassed (context: string) (results: Fuaran.Core.LawResult list) =
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
/// (Not private, for the reason given at `eqOpSw`: fuaran#1480's attribution-durability test runs
/// over the same two ports through the same shipped codecs.)
let overBothStores (body: string -> IOpStreamCheckpointSink<obj> -> unit) : unit =
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

/// The same two stores, viewed through the Phase 1485 extension interfaces. Constructed from the
/// concrete classes rather than downcast out of `overBothStores`, so what a test here exercises is
/// visible in its type rather than asserted by a cast that would pass on any sink that happened to
/// implement them.
let private overBothPortSinks (body: string -> IOpStreamCasSink<obj> -> IOpStreamKeyedSink<obj> -> unit) : unit =
    let mem = InMemorySink<obj>()
    body "InMemory" mem mem

    let path = freshDbPath ()

    try
        let sq = SqliteSink<obj>(sprintf "Data Source=%s" path, opCodec, nodeCodec)
        body "Sqlite" sq sq
    finally
        try
            File.Delete path
        with _ ->
            ()

/// Build a record that chains onto an EXPLICIT head, which is what a compare-and-append caller
/// has: it holds the head it expects, not the record the head belongs to. (`buildObjRecord` takes
/// the previous record, which a caller resuming from `Head` does not have.)
let private buildAtHead
    (streamId: string)
    (sequence: int)
    (op: TreeOp<obj>)
    (previousHash: string)
    (timestamp: DateTimeOffset)
    : OpRecord<obj> =
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

          testCase "both durable stores refuse a stale-head append with a typed StaleHead naming the actual head"
          <| fun _ ->
              // fuaran#1485 — the positive form of what fuaran#1477 could only pin as a gap. The
              // stores now carry a compare-and-append, so the three claims that were negatives are
              // asserted as the contract:
              //   * `Head` reports the genesis anchor for an empty stream and the chain hash at
              //     `LatestSequence` after that;
              //   * `AppendIf` at a STALE head persists nothing and returns
              //     `StaleHead (expected, actual)` naming the head the store really holds — the
              //     value a retry loop rebuilds against, which a throw could never carry;
              //   * `AppendIf` at the TRUE head is exactly `Append` plus a receipt addressing the
              //     record it wrote.
              // The unchanged no-expectation path is asserted alongside, because the phase KEPT it
              // deliberately: `Append` still takes no head, still throws on a duplicate sequence,
              // and still accepts a mis-chained record that the read path catches. A future change
              // that quietly gave `Append` a head check would break a documented contract, so it is
              // held here rather than left to be assumed.
              overBothPortSinks (fun storeName cas _ ->
                  let streamId = "cas-" + storeName
                  let baseSink = cas :> IOpStreamSink<obj>
                  let op1 = TreeOp.RemoveNode(NodeId "right"): TreeOp<obj>
                  let op2 = TreeOp.RemoveNode(NodeId "left"): TreeOp<obj>
                  let op3 = TreeOp.RemoveNode(NodeId "dash"): TreeOp<obj>

                  Expect.equal
                      (cas.Head streamId |> Async.RunSynchronously)
                      HashChain.genesisPreviousHash
                      (sprintf "%s: an empty stream's head is the genesis anchor" storeName)

                  let r1 =
                      buildAtHead
                          streamId
                          1
                          op1
                          HashChain.genesisPreviousHash
                          (DateTimeOffset.FromUnixTimeSeconds 100L)

                  match cas.AppendIf(r1, HashChain.genesisPreviousHash) |> Async.RunSynchronously with
                  | CasAppendOutcome.Appended receipt ->
                      Expect.equal
                          receipt
                          { StreamId = streamId
                            Sequence = 1
                            Hash = r1.Hash }
                          (sprintf "%s: the receipt addresses the record just written" storeName)
                  | CasAppendOutcome.StaleHead(e, a) ->
                      failtestf "%s: the genesis append was refused as stale (expected %s, actual %s)" storeName e a

                  Expect.equal
                      (cas.Head streamId |> Async.RunSynchronously)
                      r1.Hash
                      (sprintf "%s: the head is now record 1's chain hash" storeName)

                  // (a) a stale-head append at a FRESH sequence — the case fuaran#1477 recorded as
                  //     silently accepted. The expectation is the genesis anchor, which was the
                  //     head one append ago and is not the head now.
                  let staleHeaded =
                      buildAtHead
                          streamId
                          2
                          op2
                          HashChain.genesisPreviousHash
                          (DateTimeOffset.FromUnixTimeSeconds 300L)

                  match
                      cas.AppendIf(staleHeaded, HashChain.genesisPreviousHash)
                      |> Async.RunSynchronously
                  with
                  | CasAppendOutcome.StaleHead(expected, actual) ->
                      Expect.equal
                          expected
                          HashChain.genesisPreviousHash
                          (sprintf "%s: StaleHead echoes the caller's expectation" storeName)

                      Expect.equal actual r1.Hash (sprintf "%s: StaleHead NAMES the actual head" storeName)
                  | CasAppendOutcome.Appended _ ->
                      failtestf "%s: a stale-head compare-and-append was accepted" storeName

                  Expect.equal
                      (baseSink.LatestSequence streamId |> Async.RunSynchronously)
                      1
                      (sprintf "%s: the refused append persisted nothing" storeName)

                  // (b) rebuilt against the head the refusal named, the same op is accepted.
                  let rebuilt =
                      buildAtHead streamId 2 op2 r1.Hash (DateTimeOffset.FromUnixTimeSeconds 400L)

                  match cas.AppendIf(rebuilt, r1.Hash) |> Async.RunSynchronously with
                  | CasAppendOutcome.Appended receipt ->
                      Expect.equal receipt.Sequence 2 (sprintf "%s: the rebuilt record landed at sequence 2" storeName)
                  | CasAppendOutcome.StaleHead(e, a) ->
                      failtestf "%s: the rebuilt append was refused (expected %s, actual %s)" storeName e a

                  // (c) the no-expectation path is UNCHANGED, deliberately: `Append` still throws
                  //     on a duplicate sequence …
                  let clash =
                      buildAtHead streamId 2 op3 rebuilt.Hash (DateTimeOffset.FromUnixTimeSeconds 500L)

                  Expect.throws
                      (fun () -> baseSink.Append clash |> Async.RunSynchronously)
                      (sprintf "%s: a duplicate (StreamId, Sequence) is still refused by throwing" storeName)

                  // … and still accepts a mis-chained record, which the READ path catches.
                  let unchecked_ =
                      buildAtHead
                          streamId
                          3
                          op3
                          HashChain.genesisPreviousHash
                          (DateTimeOffset.FromUnixTimeSeconds 600L)

                  baseSink.Append unchecked_ |> Async.RunSynchronously

                  Expect.throws
                      (fun () -> baseSink.Replay(streamId, 1, 10) |> Async.RunSynchronously |> ignore)
                      (sprintf "%s: the mis-chained segment is refused on read, as it always was" storeName))

          testCase
              "both durable stores return the same receipt for a re-sent keyed append and persist nothing the second time"
          <| fun _ ->
              // fuaran#1485 — the receipt fuaran#1477 recorded as absent. A re-send is recognised by
              // its KEY, so the second call is answered from the key map (InMemory) / the
              // `op_invocation` unique index (Sqlite) and the record it carries is never consulted.
              // The re-sent record here is deliberately DIFFERENT — a later timestamp at the next
              // sequence, exactly what a caller rebuilds after a lost acknowledgement — so a store
              // that merely deduplicated identical bytes would fail this.
              overBothPortSinks (fun storeName _ keyed ->
                  let streamId = "keyed-" + storeName
                  let baseSink = keyed :> IOpStreamSink<obj>
                  let op = TreeOp.RemoveNode(NodeId "right"): TreeOp<obj>
                  let other = TreeOp.RemoveNode(NodeId "left"): TreeOp<obj>

                  let first =
                      buildAtHead streamId 1 op HashChain.genesisPreviousHash (DateTimeOffset.FromUnixTimeSeconds 100L)

                  let firstReceipt =
                      match keyed.AppendKeyed(first, "invocation-A") |> Async.RunSynchronously with
                      | KeyedAppendOutcome.Appended r -> r
                      | KeyedAppendOutcome.Duplicate r ->
                          failtestf "%s: the first append under a fresh key reported a duplicate (%A)" storeName r

                  Expect.equal
                      firstReceipt
                      { StreamId = streamId
                        Sequence = 1
                        Hash = first.Hash }
                      (sprintf "%s: the first receipt addresses the record written" storeName)

                  let resent =
                      buildAtHead streamId 2 op first.Hash (DateTimeOffset.FromUnixTimeSeconds 999L)

                  match keyed.AppendKeyed(resent, "invocation-A") |> Async.RunSynchronously with
                  | KeyedAppendOutcome.Duplicate r ->
                      Expect.equal r firstReceipt (sprintf "%s: the re-send returns the FIRST receipt" storeName)
                  | KeyedAppendOutcome.Appended r ->
                      failtestf "%s: the re-send was appended as fresh work (%A)" storeName r

                  Expect.equal
                      (baseSink.Replay(streamId, 1, 100)
                       |> Async.RunSynchronously
                       |> List.map _.Sequence)
                      [ 1 ]
                      (sprintf "%s: the re-send persisted nothing" storeName)

                  // A DIFFERENT key on the same stream is fresh work, not a retry — otherwise the
                  // contract would be "one record per stream", which is not idempotency.
                  let second =
                      buildAtHead streamId 2 other first.Hash (DateTimeOffset.FromUnixTimeSeconds 200L)

                  match keyed.AppendKeyed(second, "invocation-B") |> Async.RunSynchronously with
                  | KeyedAppendOutcome.Appended r ->
                      Expect.equal r.Sequence 2 (sprintf "%s: a fresh key appends" storeName)
                  | KeyedAppendOutcome.Duplicate r ->
                      failtestf "%s: a fresh key was mistaken for a retry (%A)" storeName r

                  // And the key is scoped to its stream: the same key on another stream is fresh.
                  let elsewhere =
                      buildAtHead
                          (streamId + "-other")
                          1
                          op
                          HashChain.genesisPreviousHash
                          (DateTimeOffset.FromUnixTimeSeconds 300L)

                  match keyed.AppendKeyed(elsewhere, "invocation-A") |> Async.RunSynchronously with
                  | KeyedAppendOutcome.Appended r ->
                      Expect.equal
                          r.StreamId
                          (streamId + "-other")
                          (sprintf "%s: an invocation key is scoped to its stream" storeName)
                  | KeyedAppendOutcome.Duplicate r -> failtestf "%s: a key leaked across streams (%A)" storeName r)

          testCase "the store-shaped compare-and-append and keyed-append laws hold over both durable stores"
          <| fun _ ->
              // The property test the phase owes: `casLaws` / `idempotencyLaws` in shape, but over
              // the STORES rather than over Core's in-memory append, which is what fuaran#1477
              // could not write because the ports carried neither contract. Seeded and bounded, so
              // it is reproducible; the generator is the same accepted/rejected op mix the witness
              // laws above draw from, so the records are the tier's real ops.
              //
              // Three claims, each the store-shaped form of one Core law:
              //   1. at the true head, `AppendIf` == `Append` (the stream grows by exactly one, at
              //      the receipt's address);
              //   2. at a stale head, `AppendIf` is `StaleHead (expected, actual)` with `actual` the
              //      live head and the stream untouched — including the two-writers-off-one-base
              //      case, where exactly one of the pair wins;
              //   3. a seen key converges on the receipt it FIRST produced, and persists nothing.
              overBothPortSinks (fun storeName cas keyed ->
                  let baseSink = cas :> IOpStreamSink<obj>
                  let mutable rng = CoreRng.ofSeed 20260904

                  let nextOp () =
                      let op, r' = genStreamOp rng
                      rng <- r'
                      op

                  // ---- 1 + 2: compare-and-append over a generated chain ----
                  let casStream = "cas-laws-" + storeName
                  let mutable head = HashChain.genesisPreviousHash
                  let mutable staleAttempts = 0

                  for sequence in 1..12 do
                      // A stale expectation before every accepted append, drawn from a head this
                      // stream has genuinely left behind (the genesis anchor after the first
                      // append). The stream must be untouched afterwards.
                      if sequence > 1 then
                          let doomed =
                              buildAtHead
                                  casStream
                                  sequence
                                  (nextOp ())
                                  HashChain.genesisPreviousHash
                                  (DateTimeOffset.FromUnixTimeSeconds(int64 sequence))

                          match cas.AppendIf(doomed, HashChain.genesisPreviousHash) |> Async.RunSynchronously with
                          | CasAppendOutcome.StaleHead(expected, actual) ->
                              staleAttempts <- staleAttempts + 1

                              Expect.equal
                                  expected
                                  HashChain.genesisPreviousHash
                                  (sprintf "%s: the refusal echoes the expectation given" storeName)

                              Expect.equal actual head (sprintf "%s: the refusal names the live head" storeName)
                          | CasAppendOutcome.Appended r ->
                              failtestf "%s: a stale expectation was accepted at sequence %d (%A)" storeName sequence r

                          Expect.equal
                              (baseSink.LatestSequence casStream |> Async.RunSynchronously)
                              (sequence - 1)
                              (sprintf "%s: a refused compare-and-append leaves the stream untouched" storeName)

                      let record =
                          buildAtHead
                              casStream
                              sequence
                              (nextOp ())
                              head
                              (DateTimeOffset.FromUnixTimeSeconds(1000L + int64 sequence))

                      match cas.AppendIf(record, head) |> Async.RunSynchronously with
                      | CasAppendOutcome.Appended receipt ->
                          Expect.equal
                              receipt
                              { StreamId = casStream
                                Sequence = sequence
                                Hash = record.Hash }
                              (sprintf "%s: at the true head the receipt addresses the record" storeName)

                          head <- record.Hash
                      | CasAppendOutcome.StaleHead(e, a) ->
                          failtestf "%s: an append at the true head was refused (expected %s, actual %s)" storeName e a

                      Expect.equal
                          (cas.Head casStream |> Async.RunSynchronously)
                          head
                          (sprintf "%s: Head tracks the accepted appends" storeName)

                  Expect.equal
                      staleAttempts
                      11
                      (sprintf
                          "%s: every stale expectation reached the refusal branch — a law whose adverse branch never ran proves nothing"
                          storeName)

                  // Two writers off ONE base: both build at the same head and sequence; exactly one
                  // wins, and the loser is told the head it lost to.
                  let contested = baseSink.LatestSequence casStream |> Async.RunSynchronously
                  let baseHead = head

                  let writerA =
                      buildAtHead
                          casStream
                          (contested + 1)
                          (nextOp ())
                          baseHead
                          (DateTimeOffset.FromUnixTimeSeconds 7001L)

                  let writerB =
                      buildAtHead
                          casStream
                          (contested + 1)
                          (nextOp ())
                          baseHead
                          (DateTimeOffset.FromUnixTimeSeconds 7002L)

                  let outcomes =
                      [ cas.AppendIf(writerA, baseHead) |> Async.RunSynchronously
                        cas.AppendIf(writerB, baseHead) |> Async.RunSynchronously ]

                  let winners =
                      outcomes
                      |> List.filter (function
                          | CasAppendOutcome.Appended _ -> true
                          | _ -> false)

                  Expect.equal
                      (List.length winners)
                      1
                      (sprintf "%s: two writers off one base admit exactly one winner" storeName)

                  match outcomes with
                  | [ _; CasAppendOutcome.StaleHead(_, actual) ] ->
                      Expect.equal actual writerA.Hash (sprintf "%s: the loser is told the winner's head" storeName)
                  | other -> failtestf "%s: expected the second writer to lose, got %A" storeName other

                  Expect.equal
                      (baseSink.LatestSequence casStream |> Async.RunSynchronously)
                      (contested + 1)
                      (sprintf "%s: exactly one of the contending records is in the stream" storeName)

                  // ---- 3: keyed append over a generated key set ----
                  let keyStream = "idem-laws-" + storeName
                  let mutable keyHead = HashChain.genesisPreviousHash
                  let mutable firstReceipts: Map<string, AppendReceipt> = Map.empty

                  // Keys drawn with replacement from a small alphabet, so the run genuinely mixes
                  // fresh keys with re-sends rather than testing one of each.
                  for step in 1..24 do
                      let pick, r' = CoreRng.intBelow 5 rng
                      rng <- r'
                      let key = sprintf "inv-%d" pick
                      let nextSeq = (baseSink.LatestSequence keyStream |> Async.RunSynchronously) + 1

                      let record =
                          buildAtHead
                              keyStream
                              nextSeq
                              (nextOp ())
                              keyHead
                              (DateTimeOffset.FromUnixTimeSeconds(8000L + int64 step))

                      match
                          keyed.AppendKeyed(record, key) |> Async.RunSynchronously, Map.tryFind key firstReceipts
                      with
                      | KeyedAppendOutcome.Appended receipt, None ->
                          firstReceipts <- Map.add key receipt firstReceipts
                          keyHead <- record.Hash
                      | KeyedAppendOutcome.Duplicate receipt, Some first ->
                          Expect.equal
                              receipt
                              first
                              (sprintf "%s: a seen key converges on the receipt it first produced" storeName)
                      | KeyedAppendOutcome.Appended r, Some first ->
                          failtestf "%s: key %s was re-appended (%A) though it first produced %A" storeName key r first
                      | KeyedAppendOutcome.Duplicate r, None ->
                          failtestf "%s: key %s was refused as a retry on its first use (%A)" storeName key r

                  Expect.equal
                      (baseSink.LatestSequence keyStream |> Async.RunSynchronously)
                      (Map.count firstReceipts)
                      (sprintf "%s: the stream holds exactly one record per DISTINCT key" storeName)

                  Expect.isTrue
                      (Map.count firstReceipts < 24)
                      (sprintf
                          "%s: the run drew at least one re-send — a key set with no collision would leave the duplicate branch unexercised"
                          storeName))

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

          testCase "a retry after a TELEMETRY failure appends exactly one record through the keyed wrapper"
          <| fun _ ->
              // fuaran#1485 — the positive form of the gap fuaran#1477 pinned. A telemetry throw is
              // swallowed AFTER the op-stream append has committed, so a caller that reads the
              // throw as "the call failed" and retries used to append a second record: nothing in
              // an op says which invocation produced it. `applyWithSinksKeyed` takes that missing
              // fact from the caller and hands it to the sink, so the retry is recognised.
              //
              // The unkeyed `applyWithSinks` is asserted in the same test, and NOT as a defect: it
              // still double-appends, because a wrapper that was never given a key cannot invent
              // one — two genuinely distinct user actions may carry the identical op. The contract
              // is that the caller names the invocation, so the two entry points are held side by
              // side and the difference between them is the thing under test.
              overBothPortSinks (fun storeName _ keyed ->
                  let telemetry = ThrowingTelemetry() :> IFuaranTelemetrySink
                  let tree = storeTree ()
                  let op = TreeOp.RemoveNode(NodeId "right"): TreeOp<obj>

                  let keyedStream = "telemetry-retry-keyed-" + storeName
                  let keyedCtx = PersistContext.create keyedStream "tester"

                  for _ in 1..2 do
                      match
                          ApplyPersist.applyWithSinksKeyed keyed telemetry keyedCtx "invocation-1" op tree
                          |> Async.RunSynchronously
                      with
                      | Ok _ -> ()
                      | Error e -> failtestf "%s: a telemetry throw must not break the apply path, got %A" storeName e

                  Expect.equal
                      ((keyed :> IOpStreamSink<obj>).Replay(keyedStream, 1, 100)
                       |> Async.RunSynchronously
                       |> List.map _.Sequence)
                      [ 1 ]
                      (sprintf "%s: exactly one record — the retry was recognised by its key" storeName)

                  let unkeyedStream = "telemetry-retry-unkeyed-" + storeName
                  let unkeyedCtx = PersistContext.create unkeyedStream "tester"

                  for _ in 1..2 do
                      ApplyPersist.applyWithSinks (keyed :> IOpStreamSink<obj>) telemetry unkeyedCtx op tree
                      |> Async.RunSynchronously
                      |> ignore

                  Expect.equal
                      ((keyed :> IOpStreamSink<obj>).Replay(unkeyedStream, 1, 100)
                       |> Async.RunSynchronously
                       |> List.map _.Sequence)
                      [ 1; 2 ]
                      (sprintf
                          "%s: the UNKEYED wrapper still appends twice — the key is the caller's to supply"
                          storeName)) ]
