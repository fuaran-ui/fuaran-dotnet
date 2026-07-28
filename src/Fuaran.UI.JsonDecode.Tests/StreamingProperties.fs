module Fuaran.UI.JsonDecode.Tests.StreamingProperties

// ============================================================================
//  Phase 104 — streaming / progressive partial-tree replay invariant (FGP 5).
//
//  The load-bearing acceptance criterion: a STREAMED emission and the SAME
//  emission applied as one BATCH must yield a byte-identical op-stream + final
//  tree. This suite proves it deterministically (no browser, no live provider)
//  over (a) the curated wire-format corpus and (b) the FsCheck-generated
//  tree-space the corpus can't reach.
//
//  Each case asserts, for one decoded tree:
//    1. batch lowering + apply rebuilds the tree byte-identically from its
//       skeleton (`encodeNode rebuilt = encodeNode tree`);
//    2. the streaming path (frames → decodeStream → ops) produces an op list
//       whose canonical-JSON wire is byte-identical to the batch lowering, and
//       a byte-identical skeleton;
//    3. streamed apply rebuilds the same tree byte-identically;
//    4. the `OpRecord` SHA-256 hash chain over the streamed ops is byte-identical
//       to the chain over the batch ops (same sequence + timestamp per record)
//       and verifies clean — i.e. the streamed sequence replays identically to
//       the batch result (FGP 5).
//
//  Why this holds: `Streaming.lowerTree` is the single canonical lowering both
//  paths share; streaming only changes WHEN ops are revealed (rendering between
//  them), never WHAT ops are emitted. Identical ops + identical (sequence,
//  timestamp) ⇒ identical hashes. The corpus + generated trees are in the
//  round-trip-faithful subspace, so the per-frame
//  `encodeNode → decodeNode → encodeNode` is byte-stable (WIRE_FORMAT §1).
// ============================================================================

open System
open Expecto
open FsCheck
open FsCheck.FSharp
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions

// ─── Hash-chain builder (mirrors the production OpRecord hash rule) ──────────

/// A fixed timestamp so batch + stream chains are compared on op content alone.
let private fixedTimestamp = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L)

/// Build a genesis-anchored `OpRecord` chain over `ops`, ascending Sequence
/// from 1, using the canonical `HashChain.computeHash` (previousHash ++
/// encodeOp ++ sequence ++ unixSeconds). This is the same rule every op-stream
/// sink applies, so byte-equal chains here mean byte-equal persisted streams.
let private buildChain (ops: TreeOp<obj> list) : OpRecord<obj> list =
    let records, _, _ =
        ops
        |> List.fold
            (fun (records, previousHash, sequence) op ->
                let actor = Actor.Human "test"

                let hash =
                    HashChain.computeHash previousHash op sequence fixedTimestamp actor None OpResultEnvelope.Success

                let record =
                    { StreamId = "stream-invariant"
                      Sequence = sequence
                      PreviousHash = previousHash
                      Hash = hash
                      Op = op
                      PromptId = None
                      Actor = actor
                      Timestamp = fixedTimestamp
                      ResultEnvelope = OpResultEnvelope.Success }

                (record :: records, hash, sequence + 1))
            ([], HashChain.genesisPreviousHash, 1)

    List.rev records

// ─── The replay invariant for one decoded tree ──────────────────────────────

/// Run the full streaming-vs-batch replay check for `tree`. Returns `Ok ()` on
/// a clean invariant, or `Error <reason>` naming the first divergence — usable
/// both as a diagnostic `failtest` message (corpus cases) and as a bool
/// (`Result.isOk`) for the FsCheck generative property.
let private streamingChecks (tree: Node<obj>) : Result<unit, string> =
    let batchOps = Streaming.lowerTree tree
    let skeleton = Streaming.skeletonRoot tree
    let frames = Streaming.framesOf CanonicalJson.encodeNode tree

    match Streaming.decodeStream frames with
    | Error e -> Error(sprintf "decodeStream failed: %A" e)
    | Ok decoded ->
        let batchOpWire = batchOps |> List.map CanonicalJson.encodeOp
        let streamOpWire = decoded.Ops |> List.map CanonicalJson.encodeOp

        if streamOpWire <> batchOpWire then
            Error "streamed ops are NOT byte-identical to the batch lowering"
        elif
            CanonicalJson.encodeNode (WireTree.reify decoded.Skeleton)
            <> CanonicalJson.encodeNode skeleton
        then
            Error "streamed skeleton is NOT byte-identical to the batch skeleton"
        else
            match
                Streaming.applyFold batchOps skeleton, Streaming.applyFold decoded.Ops (WireTree.reify decoded.Skeleton)
            with
            | Error e, _ -> Error(sprintf "batch replay apply failed: %A" e)
            | _, Error e -> Error(sprintf "streamed replay apply failed: %A" e)
            | Ok batchTree, Ok streamTree ->
                let target = CanonicalJson.encodeNode tree

                if CanonicalJson.encodeNode batchTree <> target then
                    Error "batch replay did NOT rebuild the tree byte-identically"
                elif CanonicalJson.encodeNode streamTree <> target then
                    Error "streamed replay did NOT rebuild the tree byte-identically"
                else
                    let batchHashes = buildChain batchOps |> List.map _.Hash
                    let streamChain = buildChain decoded.Ops
                    let streamHashes = streamChain |> List.map _.Hash

                    if batchHashes <> streamHashes then
                        Error "streamed op-stream hash chain differs from the batch chain"
                    else
                        match Verify.chain streamChain with
                        | Ok() -> Ok()
                        | Error v -> Error(sprintf "streamed op-stream hash chain failed verification: %A" v)

// ─── Id uniquification ───────────────────────────────────────────────────────
//
// Re-label every structurally-reachable NodeId in pre-order so the §4g
// id-uniqueness contract holds for the tree under test. Two purposes:
//   - the FsCheck generators fuzz value-spaces, not id-uniqueness;
//   - a couple of curated corpus fixtures (e.g. `step-1.json`) reuse an id
//     across subtrees — legal for a round-trip example (round-trip never
//     applies ops) but not a deployable tree, and `Apply.InsertChild`
//     correctly rejects the duplicate. Uniquifying first exercises the
//     streaming *invariant* on the real corpus *structure* regardless of
//     fixture id-hygiene.
// Interior ids of non-decomposable leaf kinds (ErrorBoundary / Custom
// children) are not structural children and never participate in InsertChild's
// dup-check, so relabelling the getChildren-reachable nodes is sufficient.

let private uniquifyIds (root: Node<obj>) : Node<obj> =
    let mutable counter = 0

    let rec go (node: Node<obj>) =
        let id = NodeId(sprintf "n%d" counter)
        counter <- counter + 1

        match Introspect.getChildren node.Kind with
        | Some children ->
            let relabelled = children |> List.map go

            match Introspect.withChildren node.Kind relabelled with
            | Some newKind -> { node with Id = id; Kind = newKind }
            | None -> { node with Id = id }
        | None -> { node with Id = id }

    go root

// ─── Corpus-driven cases ─────────────────────────────────────────────────────

let private corpus = Corpus.load ()
let private corpusRoot = fst corpus
let private corpusEntries = snd corpus

let private nodeEntries =
    corpusEntries |> List.filter (fun e -> e.Kind = "node-round-trip")

let private replayCase (e: Corpus.FixtureEntry) : Test =
    testCase (sprintf "Node — %s (%s) — streamed replay byte-identical to batch" e.Description e.Id) (fun () ->
        let wire = Corpus.readPayload corpusRoot e.InputFile

        match JsonDecode.decodeNodeObj wire with
        | Error err -> failtestf "decode failed: %A\n  on input: %s" err wire
        | Ok tree ->
            match streamingChecks (uniquifyIds tree) with
            | Ok() -> ()
            | Error reason -> failtestf "FGP 5 replay invariant broken: %s" reason)

[<Tests>]
let corpusReplay =
    nodeEntries
    |> List.map replayCase
    |> testList "Fuaran.UI.Ops.Streaming — FGP 5 replay invariant (corpus)"

// ─── Generative coverage ─────────────────────────────────────────────────────

let private config1000 = Config.QuickThrowOnFailure.WithMaxTest(1000)

let private generativeInvariant (n: Node<obj>) : bool =
    streamingChecks (uniquifyIds n) |> Result.isOk

[<Tests>]
let generativeReplay =
    testList
        "Fuaran.UI.Ops.Streaming — FGP 5 replay invariant (generative, Phase 104)"
        [ testCase "streamed replay is byte-identical to batch over 1000 generated trees" (fun () ->
              Check.One(config1000, Prop.forAll Generators.nodeArb generativeInvariant)) ]

// ─── Targeted unit cases ─────────────────────────────────────────────────────

/// A hand-built three-level tree exercising mixed decomposable / leaf children.
let private sampleTree () : Node<obj> =
    let leaf = Fuaran.markdown "h" "Hello"

    let inner =
        Fuaran.stack
            "inner"
            { Defaults.stack<obj> with
                Children = [ leaf ] }

    let badge = Fuaran.skeleton "b" 3

    Fuaran.stack
        "root"
        { Defaults.stack<obj> with
            Children = [ inner; badge ] }

[<Tests>]
let unitCases =
    testList
        "Fuaran.UI.Ops.Streaming — unit"
        [ testCase "lowerTree pre-order rebuilds a nested tree from its skeleton" (fun () ->
              let tree = sampleTree ()
              let ops = Streaming.lowerTree tree
              // root has 2 children (inner, badge); inner has 1 child (heading) ⇒ 3 InsertChild ops.
              Expect.equal (List.length ops) 3 "three InsertChild ops for a 4-node tree (root is the skeleton)"

              match Streaming.applyFold ops (Streaming.skeletonRoot tree) with
              | Ok rebuilt ->
                  Expect.equal
                      (CanonicalJson.encodeNode rebuilt)
                      (CanonicalJson.encodeNode tree)
                      "applying the lowering to the skeleton rebuilds the tree"
              | Error e -> failtestf "apply failed: %A" e)

          testCase "a leaf tree lowers to no ops (skeleton IS the whole tree)" (fun () ->
              let leaf = Fuaran.markdown "h" "Solo"
              Expect.isEmpty (Streaming.lowerTree leaf) "a childless node streams nothing"

              Expect.equal
                  (CanonicalJson.encodeNode (Streaming.skeletonRoot leaf))
                  (CanonicalJson.encodeNode leaf)
                  "the skeleton of a leaf is the leaf itself")

          testCase "framesOf/decodeStream round-trips the lowering" (fun () ->
              let tree = sampleTree ()
              let frames = Streaming.framesOf CanonicalJson.encodeNode tree
              // skeleton frame + one frame per InsertChild.
              Expect.equal (List.length frames) 4 "skeleton frame plus three child frames"
              Expect.isNone (List.head frames).ParentId "the first frame is the skeleton root (no parent)"

              match Streaming.decodeStream frames with
              | Ok decoded ->
                  Expect.equal
                      (decoded.Ops |> List.map CanonicalJson.encodeOp)
                      (Streaming.lowerTree tree |> List.map CanonicalJson.encodeOp)
                      "decoded ops match the lowering byte-for-byte"
              | Error e -> failtestf "decodeStream failed: %A" e)

          testCase "decodeStream rejects an empty frame sequence" (fun () ->
              match Streaming.decodeStream [] with
              | Error e -> Expect.equal e.Code "STREAM_EMPTY" "empty stream is a protocol error"
              | Ok _ -> failtest "expected an empty-stream error")

          testCase "decodeStream rejects a missing skeleton-root frame" (fun () ->
              let orphan =
                  { Streaming.ParentId = Some(NodeId "p")
                    Streaming.Position = 0
                    Streaming.NodeJson = CanonicalJson.encodeNode (Fuaran.markdown "h" "x") }

              match Streaming.decodeStream [ orphan ] with
              | Error e -> Expect.equal e.Code "STREAM_NO_SKELETON" "first frame must be the skeleton root"
              | Ok _ -> failtest "expected a missing-skeleton error")

          testCase "pendingPlaceholder is an ErrorBoundary with a PulseDuringLoad skeleton, keeping the id" (fun () ->
              let placeholder = Streaming.pendingPlaceholder<obj> (NodeId "target")

              Expect.equal
                  placeholder.Id
                  (NodeId "target")
                  "the placeholder keeps the pending node's id for substitution"

              match placeholder.Kind with
              | NodeKind.ErrorBoundary spec ->
                  Expect.equal spec.Child.Motion (Some Motion.PulseDuringLoad) "the loading child pulses while filling"

                  match spec.Child.Kind with
                  | NodeKind.Skeleton( _) -> ()
                  | other -> failtestf "expected a Skeleton loading child, got %A" other
              | other -> failtestf "expected an ErrorBoundary placeholder, got %A" other)

          testCase "withSkeletons substitutes a pending child in place" (fun () ->
              let tree = sampleTree ()
              let substituted = Streaming.withSkeletons (Set.ofList [ NodeId "inner" ]) tree

              match Introspect.findNode (NodeId "inner") substituted with
              | Some n ->
                  match n.Kind with
                  | NodeKind.ErrorBoundary _ -> ()
                  | other -> failtestf "pending 'inner' should render as a placeholder, got %A" other
              | None -> failtest "the substituted 'inner' placeholder kept its id and should be findable")

          testCase "decodeBatch is the unchanged whole-tree decode (no-regression seam)" (fun () ->
              let wire = CanonicalJson.encodeNode (sampleTree ())

              // Both paths now yield a `WireTree`; reify to compare the encoded trees.
              match Streaming.decodeBatch wire, JsonDecode.decodeNode wire with
              | Ok a, Ok b ->
                  Expect.equal
                      (CanonicalJson.encodeNode (WireTree.reify a))
                      (CanonicalJson.encodeNode (WireTree.reify b))
                      "StreamingMode.Batch decodes identically to the established whole-tree path"
              | _ -> failtest "both decode paths should succeed on canonical wire") ]
