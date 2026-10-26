module Fuaran.UI.OpStream.Dag.Tests.DagLiftTests

// System.Text.Json's GetString() is nullable; the corpus read below parses a
// controlled, committed fixture where every field is present.
#nowarn "3261"

// ============================================================================
//  Phase 2116 — the verified lift from a linear segment to a DAG branch, and
//  the receipt that recomputes it.
//
//  Three go-red proofs: a linear segment with a broken link is REFUSED; a
//  receipt whose DAG head was altered does NOT recompute; a receipt checked
//  against a different linear head does NOT recompute. Then the corpus pin: the
//  lifted address of every `chain/chain-corpus.json` record, side by side with
//  its linear hash, so nobody later assumes the two are equal — through the
//  in-memory and the SQLite DAG sinks.
// ============================================================================

open System
open System.IO
open System.Text.Json
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.InMemory
open Fuaran.UI.OpStream.Dag.Sqlite
open Fuaran.UI.OpStream.TestSupport

let private brand =
    TreeOp.UpdateStyle(
        leftChildId,
        { Defaults.style with
            Tone = ToneVariant.Brand }
    )

let private success =
    TreeOp.UpdateStyle(
        rightChildId,
        { Defaults.style with
            Tone = ToneVariant.Success }
    )

let private critical =
    TreeOp.UpdateStyle(
        rightChildId,
        { Defaults.style with
            Tone = ToneVariant.Critical }
    )

/// A sound three-record linear chain on stream `s`, whose last op is `lastOp`.
let private chainEndingWith (lastOp: TreeOp<TestMsg>) : OpRecord<TestMsg> list =
    let r1 = buildRecord "s" 1 brand None (timestamp 1L)
    let r2 = buildRecord "s" 2 success (Some r1) (timestamp 2L)
    let r3 = buildRecord "s" 3 lastOp (Some r2) (timestamp 3L)
    [ r1; r2; r3 ]

let private chain () =
    chainEndingWith (TreeOp.RemoveNode leftChildId)

let private liftOk (result: Result<LiftedBranch<'Msg>, LiftRefusal>) : LiftedBranch<'Msg> =
    match result with
    | Ok lifted -> lifted
    | Error refusal -> failtestf "expected a lift, got a refusal: %s" (DagLift.describe refusal)

// ─── The chain corpus, read as linear records ───────────────────────────────

let private corpusRoot = Fuaran.Tests.CorpusRoot.tryFind ()

let private readFixture (rel: string) =
    match corpusRoot with
    | Some r -> File.ReadAllText(Path.Combine(r, rel))
    | None ->
        // Not a skip: this pin is the point of the test, and a pin that skips
        // certifies nothing (set FUARAN_WIRE_FIXTURES in a worktree).
        failtest "wire-format-fixtures/ not found (set FUARAN_WIRE_FIXTURES): the lifted-address pin cannot run"

let private actorOf (e: JsonElement) : Actor =
    match e.GetProperty("kind").GetString() with
    | "human" -> Actor.Human(e.GetProperty("id").GetString())
    | "agent" ->
        Actor.Agent(
            e.GetProperty("model").GetString(),
            e.GetProperty("version").GetString(),
            e.GetProperty("id").GetString()
        )
    | k -> failwithf "unknown actor kind %s" k

let private resultOf (e: JsonElement) : OpResultEnvelope =
    match e.GetProperty("kind").GetString() with
    | "success" -> OpResultEnvelope.Success
    | "failure" -> OpResultEnvelope.Failure(e.GetProperty("code").GetString(), e.GetProperty("message").GetString())
    | k -> failwithf "unknown result kind %s" k

/// The corpus as stored linear records on stream `corpus`, each `Hash` the
/// corpus's own committed value (read, not recomputed — the lift verifies it).
let private corpusRecords () : OpRecord<obj> list =
    let doc = JsonDocument.Parse(readFixture "chain/chain-corpus.json")

    [ for r in doc.RootElement.GetProperty("records").EnumerateArray() do
          let op =
              match Fuaran.UI.Ops.JsonDecode.decodeOp (readFixture (r.GetProperty("opFixture").GetString())) with
              | Ok op -> op
              | Error e -> failtestf "decode failed: %A" e

          let promptId =
              let p = r.GetProperty("promptId")

              if p.ValueKind = JsonValueKind.Null then
                  None
              else
                  Some(p.GetString())

          { StreamId = "corpus"
            Sequence = r.GetProperty("sequence").GetInt32()
            PreviousHash = r.GetProperty("previousHash").GetString()
            Hash = r.GetProperty("hash").GetString()
            Op = op
            PromptId = promptId
            Actor = actorOf (r.GetProperty("actor"))
            Timestamp = DateTimeOffset.FromUnixTimeSeconds(r.GetProperty("timestampUnixSeconds").GetInt64())
            ResultEnvelope = resultOf (r.GetProperty("result")) } ]

/// The pin, one row per corpus record: its committed LINEAR chain hash beside
/// the content address its DAG lift receives (genesis-rooted, `ofLinearVerified`).
/// The two are different digests over different pre-images — the linear one
/// folds the sequence and the previous hash, the DAG one the parents — and they
/// are never equal. A change to either pre-image moves a column here.
let private liftedAddressPin: (string * string) list =
    [ "914d7631416d77f24aeea88a63d5e0fa376073366f390ae718f0910ae45dee0c",
      "d67c35e006e9f2beee6edf58abcc5e95ba6904f94b336931bc4c84212c146355"
      "5b7c55d6668e524aab6e1a6c8d443c54bba9c01deb521bc7460c28d3e1072a05",
      "c51ad58b87f8cb47359a6e1e701febf68e40d2fb0e2841fc7a9305dc722c3a63"
      "2b195656cc9bda16bdf52c4550ffde1c73a2b6ddb6c222aa3d64f19b231e9da4",
      "a37e07d5e1bb9b27bf0f0d607c2c6a61080390742f90f6ff2d24e8c4dc319354" ]

let private sqliteSink (path: string) : IDagOpStreamSink<obj> =
    SqliteDagSink.create<obj> (sprintf "Data Source=%s" path) (OpJsonCodec.canonicalObj ())

/// Store the lifted corpus in `sink`, read every record back, and assert each
/// stored address is the pinned one, recomputes from what the store returned,
/// and differs from the linear hash it was lifted from.
let private assertPinThrough (sink: IDagOpStreamSink<obj>) =
    let linear = corpusRecords ()
    let lifted = liftOk (DagLift.ofLinearVerified linear)

    Expect.equal (List.length lifted.Records) (List.length liftedAddressPin) "one lifted record per corpus record"

    for r in lifted.Records do
        sink.Add r |> Async.RunSynchronously

    for (linearRecord, (linearHash, liftedAddress)), dag in List.zip (List.zip linear liftedAddressPin) lifted.Records do
        Expect.equal linearRecord.Hash linearHash "the corpus linear hash is the pinned one"
        Expect.equal dag.Hash liftedAddress "the lifted address is the pinned one"

        Expect.notEqual
            liftedAddress
            linearHash
            "a lifted DAG address is NOT the linear chain hash; the two pre-images differ"

        match sink.TryGet("corpus", liftedAddress) |> Async.RunSynchronously with
        | None -> failtestf "the store does not return the lifted record %s" liftedAddress
        | Some stored ->
            Expect.equal stored.Hash liftedAddress "the store keeps the lifted address"
            Expect.equal (DagOpRecord.recomputeHash stored) liftedAddress "the stored record recomputes its address"

    Expect.equal lifted.Receipt.LinearHead (List.last liftedAddressPin |> fst) "the receipt's linear head"
    Expect.equal lifted.Receipt.DagHead (List.last liftedAddressPin |> snd) "the receipt's DAG head"

[<Tests>]
let tests =
    testList
        "Dag.Lift (Phase 2116)"
        [ testList
              "the lift"
              [ test "a sound segment lifts to exactly what the unverified embedding produces" {
                    let linear = chain ()
                    let lifted = liftOk (DagLift.ofLinearVerified linear)
                    // Records hold closures in their ops, so compare by content address
                    // and parent links, which is what the embedding decides.
                    let shape (rs: DagOpRecord<TestMsg> list) =
                        rs |> List.map (fun r -> r.StreamId, r.Hash, r.Parents)

                    Expect.equal (shape lifted.Records) (shape (DagOpRecord.ofLinear linear)) "same records as ofLinear"

                    match DagVerify.records lifted.Records with
                    | Ok() -> ()
                    | Error e -> failtestf "the lifted branch must verify as a DAG: %A" e
                }

                test "the receipt names the linear range and the DAG branch it became" {
                    let linear = chain ()
                    let lifted = liftOk (DagLift.ofLinearVerified linear)
                    let receipt = lifted.Receipt
                    Expect.equal receipt.LinearStreamId "s" "linear stream"
                    Expect.equal receipt.ChainFormatVersion StreamEntry.chainFormatVersion "chain format"
                    Expect.equal (receipt.FromSequence, receipt.ToSequence) (1, 3) "range"
                    Expect.equal receipt.LinearPreviousHash HashChain.genesisPreviousHash "anchored at genesis"
                    Expect.equal receipt.LinearHead (List.last linear).Hash "linear head"
                    Expect.equal receipt.DagStreamId "s" "a lift keeps the stream id"
                    Expect.equal receipt.DagHead (List.last lifted.Records).Hash "DAG head"
                    Expect.equal receipt.Anchor None "a genesis-rooted lift has no anchor"
                }

                test "the anchored form hangs the first record off the anchor and steps from there" {
                    let anchorRecord = stepRecord "host" None brand 0L
                    let linear = chain ()
                    let lifted = liftOk (DagLift.ofLinearAnchored anchorRecord.Hash linear)

                    match lifted.Records with
                    | first :: rest ->
                        Expect.equal first.Parents [ anchorRecord.Hash ] "the first record's parent is the anchor"

                        for prior, r in List.pairwise lifted.Records do
                            Expect.equal r.Parents [ prior.Hash ] "every later record is a single-parent step"

                        Expect.equal (List.length rest) 2 "every linear record is lifted"
                    | [] -> failtest "expected a lifted branch"

                    Expect.equal lifted.Receipt.Anchor (Some anchorRecord.Hash) "the receipt names the anchor"

                    // The anchored branch is a guest-fork branch: it loads into a sink
                    // holding the anchor and verifies there.
                    let sink = InMemoryDagSink.create<TestMsg> ()
                    add sink anchorRecord

                    for r in lifted.Records do
                        add sink r

                    Expect.equal
                        (sink.Records "s" |> Async.RunSynchronously |> List.length)
                        3
                        "the branch loads beside its anchor"
                }

                test "an empty segment is refused" {
                    match DagLift.ofLinearVerified ([]: OpRecord<TestMsg> list) with
                    | Error LiftRefusal.EmptySegment -> ()
                    | other -> failtestf "expected EmptySegment, got %A" other
                }

                test "a segment spanning two streams is refused" {
                    let r1 = buildRecord "s" 1 brand None (timestamp 1L)

                    let r2 =
                        { buildRecord "s" 2 success (Some r1) (timestamp 2L) with
                            StreamId = "t" }

                    match DagLift.ofLinearVerified [ r1; r2 ] with
                    | Error(LiftRefusal.MixedStreams("s", "t")) -> ()
                    | other -> failtestf "expected MixedStreams, got %A" other
                }

                // GO-RED 1.
                test "a linear segment with a broken link is refused, in both forms" {
                    match chain () with
                    | [ r1; r2; r3 ] ->
                        let broken =
                            [ r1
                              { r2 with
                                  PreviousHash = String.replicate 64 "f" }
                              r3 ]

                        for result in [ DagLift.ofLinearVerified broken; DagLift.ofLinearAnchored r1.Hash broken ] do
                            match result with
                            | Error(LiftRefusal.BrokenLinearSegment(VerificationError.PreviousHashMismatch(2, _, _))) ->
                                ()
                            | other -> failtestf "expected a refusal naming the broken link at 2, got %A" other
                    | _ -> failtest "expected three records"
                }

                test "a record whose stored hash does not recompute is refused" {
                    match chain () with
                    | [ r1; r2; r3 ] ->
                        match DagLift.ofLinearVerified [ r1; { r2 with Op = critical }; r3 ] with
                        | Error(LiftRefusal.BrokenLinearSegment(VerificationError.HashMismatch(2, _, _))) -> ()
                        | other -> failtestf "expected HashMismatch at 2, got %A" other
                    | _ -> failtest "expected three records"
                } ]

          testList
              "the receipt"
              [ test "a receipt recomputes from its own records, and the verdict names its basis" {
                    let linear = chain ()
                    let lifted = liftOk (DagLift.ofLinearVerified linear)

                    match LiftReceipt.verify lifted.Receipt linear with
                    | LiftVerdict.Recomputes(LiftBasis.RecomputedFromLinearRecords(stream, from, upTo, head)) ->
                        Expect.equal (stream, from, upTo, head) ("s", 1, 3, (List.last linear).Hash) "the basis"
                    | other -> failtestf "expected Recomputes, got %A" other
                }

                test "an anchored receipt recomputes, and moving its anchor moves the DAG head" {
                    let linear = chain ()
                    let anchor = (stepRecord "host" None brand 0L).Hash
                    let lifted = liftOk (DagLift.ofLinearAnchored anchor linear)

                    match LiftReceipt.verify lifted.Receipt linear with
                    | LiftVerdict.Recomputes _ -> ()
                    | other -> failtestf "expected Recomputes, got %A" other

                    let moved =
                        { lifted.Receipt with
                            Anchor = Some(String.replicate 64 "a") }

                    match LiftReceipt.verify moved linear with
                    | LiftVerdict.DoesNotRecompute(LiftMismatch.DagHeadDiffers _) -> ()
                    | other -> failtestf "expected DagHeadDiffers, got %A" other
                }

                // GO-RED 2.
                test "a receipt whose DAG head was altered does not recompute" {
                    let linear = chain ()
                    let lifted = liftOk (DagLift.ofLinearVerified linear)
                    let forged = String.replicate 64 "e"

                    match LiftReceipt.verify { lifted.Receipt with DagHead = forged } linear with
                    | LiftVerdict.DoesNotRecompute(LiftMismatch.DagHeadDiffers(claimed, recomputed)) ->
                        Expect.equal claimed forged "the mismatch names the receipt's head"
                        Expect.equal recomputed lifted.Receipt.DagHead "and the recomputed one"
                    | other -> failtestf "expected DagHeadDiffers, got %A" other
                }

                // GO-RED 3.
                test "a receipt checked against a different linear head does not recompute" {
                    let lifted = liftOk (DagLift.ofLinearVerified (chain ()))
                    // A sound chain over the same range whose last op differs: it re-walks
                    // cleanly, so only the receipt comparison can tell them apart.
                    let other = chainEndingWith critical

                    match LiftReceipt.verify lifted.Receipt other with
                    | LiftVerdict.DoesNotRecompute(LiftMismatch.LinearHeadDiffers(claimed, actual)) ->
                        Expect.equal claimed lifted.Receipt.LinearHead "names the receipt's linear head"
                        Expect.equal actual (List.last other).Hash "and the records' head"
                    | v -> failtestf "expected LinearHeadDiffers, got %A" v
                }

                test "a receipt over a different range or stream does not recompute" {
                    let linear = chain ()
                    let lifted = liftOk (DagLift.ofLinearVerified linear)

                    match LiftReceipt.verify lifted.Receipt (List.take 2 linear) with
                    | LiftVerdict.DoesNotRecompute(LiftMismatch.SequenceRangeDiffers((1, 3), (1, 2))) -> ()
                    | v -> failtestf "expected SequenceRangeDiffers, got %A" v

                    match
                        LiftReceipt.verify
                            { lifted.Receipt with
                                LinearStreamId = "elsewhere" }
                            linear
                    with
                    | LiftVerdict.DoesNotRecompute(LiftMismatch.LinearStreamDiffers("elsewhere", "s")) -> ()
                    | v -> failtestf "expected LinearStreamDiffers, got %A" v
                }

                test "a receipt over records that do not re-walk does not recompute" {
                    match chain () with
                    | [ r1; r2; r3 ] as linear ->
                        let lifted = liftOk (DagLift.ofLinearVerified linear)

                        let broken =
                            [ r1
                              { r2 with
                                  PreviousHash = String.replicate 64 "f" }
                              r3 ]

                        match LiftReceipt.verify lifted.Receipt broken with
                        | LiftVerdict.DoesNotRecompute(LiftMismatch.LinearRecordsRefused(LiftRefusal.BrokenLinearSegment _)) ->
                            ()
                        | v -> failtestf "expected LinearRecordsRefused, got %A" v
                    | _ -> failtest "expected three records"
                }

                test "a receipt naming another chain format is not re-walked" {
                    let linear = chain ()
                    let lifted = liftOk (DagLift.ofLinearVerified linear)

                    match
                        LiftReceipt.verify
                            { lifted.Receipt with
                                ChainFormatVersion = StreamEntry.chainFormatVersion + 1 }
                            linear
                    with
                    | LiftVerdict.DoesNotRecompute(LiftMismatch.UnsupportedChainFormat v) ->
                        Expect.equal v (StreamEntry.chainFormatVersion + 1) "names the version"
                    | v -> failtestf "expected UnsupportedChainFormat, got %A" v
                } ]

          testList
              "the chain-corpus lifted-address pin"
              [ test "in-memory DAG sink" { assertPinThrough (InMemoryDagSink.create<obj> ()) }

                test "SQLite DAG sink" {
                    use db = TempDb.create "dag-lift"
                    assertPinThrough (sqliteSink db.Path)
                } ] ]
