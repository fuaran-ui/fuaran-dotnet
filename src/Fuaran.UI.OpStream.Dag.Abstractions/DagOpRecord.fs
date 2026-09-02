namespace Fuaran.UI.OpStream.Dag.Abstractions

open System
open System.Globalization
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions

// ============================================================================
//  DagOpRecord — the branching-DAG generalisation of the linear `OpRecord`.
//
//  This DAG is DELIBERATELY sovereign from the minimal `Fuaran.Core.OpStream.Dag`
//  — it owns a merge-outcome node identity (a merge folds the outcome tree hash
//  under a "merge" tag), lexicographically-sorted parents, retention/tombstones,
//  and a tree-aware 3-way merge, none of which the Core DAG models or should.
//  See `docs/DAG_STRATEGY.md` before "unifying" the two — it is a boundary
//  decision, not unfinished convergence.
//
//  The *temporal* graph of the op-stream goes from a linear hash-chain to a
//  branching Merkle-DAG WITHOUT touching the *spatial* graph: the `Node<'Msg>`
//  tree stays a tree and the 10-op `TreeOp` algebra is unchanged. Branch and
//  merge are properties of how op-records *link* (`Parents`), never new
//  `TreeOp` cases.
//
//  This is an additive, opt-in (rung-4) type. The linear `OpRecord` +
//  `IOpStreamSink` (in `Fuaran.UI.OpStream.Abstractions`) are LEFT UNTOUCHED;
//  a linear hash-chain is a valid *degenerate* DAG (one parent per node, one
//  head), so adopting the DAG is a package reference, not a data migration.
//  See `DagOpRecord.ofLinear` for the degenerate embedding.
//
//  Content address (the multi-parent hash). A DELIMITED envelope (Phase 408)
//  over the sorted parents, the op, and the full provenance. Parents are sorted
//  lexicographically so a merge node's identity is parent-order-independent (a
//  merge of {A,B} hashes the same as {B,A}); each parent is a fixed 64-hex hash:
//
//      Hash = SHA-256( {"parents":[<sorted, quoted>]
//                       ,"op":<CanonicalJson.encodeOp(Op)>
//                       ,"ts":<unixSeconds>
//                       ,"actor":<Actor.encode(Actor)>
//                       ,"promptId":<null|..>,"result":<..>} )
//
//  Phase 408 folded `userId` / `promptId` / `resultEnvelope` INTO the hash,
//  closing the F13/Phase-320-class provenance hole the linear chain closed in
//  406/411 (re-attributing a node was previously undetectable), and replaced the
//  raw `encodeOp ++ unixTs` concatenation with the delimited envelope. Phase 1144
//  then TYPED that attribution: the `"userId":<bare string>` member became
//  `"actor":<Actor.encode>`, the same typed `Actor` (Human | Agent) and the same
//  pinned canonical encoding the linear chain has folded since Phase 320 — so the
//  Human/Agent distinction is inside the digest rather than flattened to an id on
//  the way in. Because attribution was already hashed, typing it RE-ADDRESSES
//  EVERY DAG NODE: pre-1144 content addresses do not carry forward. A MERGE
//  node folds the OUTCOME tree hash under a `"merge"` tag instead of the op
//  (M1: two hosts agree iff they reach the same tree) — the SOVEREIGN DAG
//  identity semantic Core's minimal `nodeHash` cannot express (assessment
//  fuaran#408). Deliberately carries NO linear `Sequence` (a DAG has no global
//  sequence), so a single-parent DAG node's hash is NOT byte-identical to the
//  linear `OpRecord.Hash`; the degenerate-equivalence contract is *same
//  resulting tree + a verifiable chain*, not identical bytes (`DagOpRecord.ofLinear`).
//
//  FGP 2 / FGP 6. Depends on `FSharp.Core` + `Fuaran.UI` + `Fuaran.UI.Ops` +
//  the linear `Fuaran.UI.OpStream.Abstractions` only (for `CanonicalJson` /
//  `HashChain` / `OpResultEnvelope`). No orchestration-private dependency; the
//  DAG abstractions stay Apache-2.0-clean alongside the linear ones.
// ============================================================================

/// One DAG node's worth of apply trace. The content-addressed,
/// multi-parent generalisation of `OpRecord<'Msg>`:
///
///  - `Parents` replaces the linear `PreviousHash`: 0..n parent content
///    hashes, in **author order** (the head of the list is the *primary*
///    parent — the replay spine). `[]` is a genesis (root) node; one parent is
///    a linear step; two-or-more parents is a merge node. Parents are sorted
///    lexicographically only inside the hash, so a merge's identity is
///    parent-order-independent while replay still has a primary spine.
///  - `Hash` is the content address (see the module header for the algorithm).
///  - `OutcomeHash` is `Some` for a **merge node** (`Parents.Length ≥ 2`): the
///    canonical hash of the resulting tree the merge commits to. The merge
///    node's identity folds in this OUTCOME hash, not the op-path that reached
///    it (M1: "two hosts agree iff they reach the same tree"). `None` for an
///    ordinary 0/1-parent node, whose identity folds in its `Op`. The
///    merge node's `Op` still carries the replay delta (the diff from the
///    primary parent's tree to the merged tree) so replay along the primary
///    spine reconstructs the merged tree.
///  - `Actor` is the typed author (Phase 1144) — the same `Human | Agent` DU the
///    linear `OpRecord` carries, replacing the pre-1144 bare `UserId: string`.
///    It is folded into the content address via `Actor.encode`, so the
///    Human/Agent distinction is covered by the digest and survives
///    `ofLinear`. Hosts still threading a bare id lift it with
///    `Actor.ofLegacyString`.
///  - `Tombstoned` marks a record whose payload has been pruned for retention
///    (`Op` reset to a placeholder, the original `Hash` preserved so the chain
///    still links — see `DagRetention`). A live record is `Tombstoned = false`.
///
/// Append-only by content address: a sink rejects a second record with the
/// same `(StreamId, Hash)` UNLESS the new one is an identical re-append
/// (content addressing makes re-adds idempotent).
type DagOpRecord<'Msg> =
    { StreamId: string
      Hash: string
      Parents: string list
      Op: TreeOp<'Msg>
      OutcomeHash: string option
      PromptId: string option
      Actor: Actor
      Timestamp: DateTimeOffset
      ResultEnvelope: OpResultEnvelope
      Tombstoned: bool }

module DagOpRecord =

    /// Parents sorted lexicographically (Ordinal) — the canonical parent order
    /// the content hash folds over. Order-independent merge identity depends on
    /// this being applied at both hash time and verify time.
    let sortedParents (parents: string list) : string list =
        parents |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))

    // ─── Content-address pre-image (Phase 408) ────────────────────────────────
    // Portable on BOTH pipelines: the digest routes through `HashChain.sha256Hex`
    // — the pure, Fable-safe FIPS 180-4 SHA-256 the language tier ships since
    // Phase 405 — and the pre-image uses only `StringBuilder` / `CultureInfo` /
    // `DateTimeOffset.ToUnixTimeSeconds` / `CanonicalJson.encodeOp` / `StreamEntry`,
    // all Fable-clean. The pre-405 `#if !FABLE_COMPILER` guard here was stale
    // over-caution (crypto used to be BCL-only); un-guarded so `createMerge` (and
    // the rest) are Fable-visible, which is what lets the Dag.Merge 3-way-merge
    // engine compile into a browser bundle (fuaran#501).
    // The DAG carried the SAME provenance hole the linear chain closed in Phase
    // 406/411: `userId` / `promptId` / `resultEnvelope` were OUTSIDE the content
    // hash, so re-attributing a node was undetectable ("folding the actor into
    // the DAG hash" was flagged in-code as a follow-on — this is it). The
    // pre-image is now a DELIMITED envelope that folds them all, closing the
    // hole and removing the raw `encodeOp ++ unixTs` concatenation smell. The
    // DAG's SOVEREIGN identity semantics are preserved verbatim: parents stay
    // sorted (order-independent merge id) and a merge node folds the OUTCOME
    // tree hash under a `"merge"` tag, NOT the op-path (M1 — two hosts agree iff
    // they reach the same tree). The assessment (fuaran#408) keeps UI's DAG
    // identity sovereign vs `Core.OpStream.Dag`; this only hardens its pre-image.
    //
    // ── THE PRE-IMAGE CHANGE (Phase 1144) ────────────────────────────────────
    // The envelope's attribution member moved from an untyped id to the typed
    // actor, at the SAME position in the delimited envelope:
    //
    //     …,"ts":<unix>,"userId":"alice",  "promptId":…,"result":…     (408)
    //     …,"ts":<unix>,"actor":{"kind":"human","id":"alice"},"promptId":…,…  (1144)
    //
    // The bytes come from `Actor.encode`, the encoding the linear chain has
    // pinned since Phase 320 and which the TS + Core hosts reproduce
    // byte-for-byte — one canonical actor encoding across the estate, reused
    // rather than a second one invented here. `Actor.encode` emits its members
    // in a PINNED order (`kind` first, then the case fields), not Ordinal-sorted;
    // that is the whole point of it being pinned, and the nested value is
    // embedded verbatim exactly as `CanonicalJson.encodeOp` is.
    //
    // THIS RE-ADDRESSES EVERY DAG NODE. A pre-1144 record's stored `Hash` will
    // not reproduce under `recomputeHash`, and a pre-1144 hash is not a valid
    // parent link for a post-1144 node — DAG content addresses do NOT carry
    // forward, and there is no in-place upgrade for a persisted DAG. See
    // `docs/migrations/1144-typed-actor-dag-fold.md`. It is deliberately not
    // dual-read: a lenient lift of a bare id would mint a record whose stored
    // hash silently fails verification, which is a worse failure than a refusal
    // that names the cause (`DagWire.decodeRecord` refuses a `userId` envelope
    // for exactly this reason).
    //
    // The DAG pre-image carries NO format-version tag (the linear chain's `"v"`
    // has no DAG counterpart — see the STABILITY.md note that "a DAG version tag
    // is tracked separately"). Introducing one is a separate design act with its
    // own cross-host concept, not a side-effect of typing the actor.

    /// Canonical JSON string escaping — `"` / `\` / control chars, matching the
    /// linear `StreamEntry` / `CanonicalJson` escaper so bytes align across hosts.
    let private jstr (s: string) : string =
        let sb = System.Text.StringBuilder()
        sb.Append '"' |> ignore

        for ch in s do
            match ch with
            | '"' -> sb.Append "\\\"" |> ignore
            | '\\' -> sb.Append "\\\\" |> ignore
            | c when c < ' ' -> sb.Append(sprintf "\\u%04x" (int c)) |> ignore
            | c -> sb.Append c |> ignore

        sb.Append '"' |> ignore
        sb.ToString()

    let private parentsJson (parents: string list) : string =
        sortedParents parents |> List.map jstr |> String.concat "," |> sprintf "[%s]"

    let private provenanceJson
        (ts: DateTimeOffset)
        (actor: Actor)
        (promptId: string option)
        (result: OpResultEnvelope)
        : string =
        ",\"ts\":"
        + ts.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
        + ",\"actor\":"
        + Actor.encode actor
        + ",\"promptId\":"
        + (match promptId with
           | Some p -> jstr p
           | None -> "null")
        + ",\"result\":"
        + StreamEntry.encodeResult result

    /// Content address for an ORDINARY (0/1-parent) DAG node: a delimited
    /// envelope over the sorted parents, the op, and the full provenance
    /// (Phase 408 — provenance folded in). Parent hashes are fixed 64-hex so a
    /// JSON array of them is unambiguous.
    let computeHash<'Msg>
        (parents: string list)
        (op: TreeOp<'Msg>)
        (timestamp: DateTimeOffset)
        (actor: Actor)
        (promptId: string option)
        (resultEnvelope: OpResultEnvelope)
        : string =
        let payload =
            "{\"parents\":"
            + parentsJson parents
            + ",\"op\":"
            + CanonicalJson.encodeOp op
            + provenanceJson timestamp actor promptId resultEnvelope
            + "}"

        HashChain.sha256Hex payload

    /// Content address for a MERGE node: folds in the canonical OUTCOME tree
    /// hash under a `"merge"` tag, NOT the op-path (M1: two hosts agree iff they
    /// reach the same tree — the sovereign DAG identity semantic). Provenance is
    /// folded in as for an ordinary node (Phase 408). `outcomeHash` is
    /// `CanonicalJson.encodeNode merged |> HashChain.sha256Hex`.
    let computeMergeHash
        (parents: string list)
        (outcomeHash: string)
        (timestamp: DateTimeOffset)
        (actor: Actor)
        (promptId: string option)
        (resultEnvelope: OpResultEnvelope)
        : string =
        let payload =
            "{\"parents\":"
            + parentsJson parents
            + ",\"merge\":"
            + jstr outcomeHash
            + provenanceJson timestamp actor promptId resultEnvelope
            + "}"

        HashChain.sha256Hex payload

    /// Recompute the stored content address of `record` — picks the op-hash or
    /// the merge-hash rule by whether `OutcomeHash` is populated. Used by
    /// `DagVerify`.
    let recomputeHash<'Msg> (record: DagOpRecord<'Msg>) : string =
        match record.OutcomeHash with
        | Some outcome ->
            computeMergeHash record.Parents outcome record.Timestamp record.Actor record.PromptId record.ResultEnvelope
        | None ->
            computeHash record.Parents record.Op record.Timestamp record.Actor record.PromptId record.ResultEnvelope

    /// Assemble a live (`Tombstoned = false`) ordinary DAG record with its
    /// content hash computed from `parents` / `op` / `timestamp`.
    let create<'Msg>
        (streamId: string)
        (parents: string list)
        (op: TreeOp<'Msg>)
        (promptId: string option)
        (actor: Actor)
        (timestamp: DateTimeOffset)
        (resultEnvelope: OpResultEnvelope)
        : DagOpRecord<'Msg> =
        { StreamId = streamId
          Hash = computeHash parents op timestamp actor promptId resultEnvelope
          Parents = parents
          Op = op
          OutcomeHash = None
          PromptId = promptId
          Actor = actor
          Timestamp = timestamp
          ResultEnvelope = resultEnvelope
          Tombstoned = false }

    /// Assemble a live MERGE record: `Parents` in author order (primary first),
    /// `Op` the replay delta from the primary parent's tree to the merged tree,
    /// `OutcomeHash` the canonical hash of that merged tree, and the content
    /// `Hash` folded over the outcome (not the op).
    let createMerge<'Msg>
        (streamId: string)
        (parents: string list)
        (replayDelta: TreeOp<'Msg>)
        (outcomeHash: string)
        (promptId: string option)
        (actor: Actor)
        (timestamp: DateTimeOffset)
        (resultEnvelope: OpResultEnvelope)
        : DagOpRecord<'Msg> =
        { StreamId = streamId
          Hash = computeMergeHash parents outcomeHash timestamp actor promptId resultEnvelope
          Parents = parents
          Op = replayDelta
          OutcomeHash = Some outcomeHash
          PromptId = promptId
          Actor = actor
          Timestamp = timestamp
          ResultEnvelope = resultEnvelope
          Tombstoned = false }

    /// Embed a *linear* `OpRecord<'Msg>` history as a single-parent DAG: each
    /// record becomes a one-parent DAG node whose parent is the prior DAG
    /// node's content hash (`[]` for the first). The op payloads + timestamps
    /// carry over verbatim, so replaying the resulting DAG chain reconstructs
    /// the SAME tree as replaying the linear chain (the degenerate-equivalence
    /// contract). The DAG hashes are freshly content-addressed (they do not
    /// equal the linear `OpRecord.Hash` values — the linear hash folds in
    /// `Sequence`, which the DAG omits), but the chain is fully verifiable via
    /// `DagVerify.chain`.
    let ofLinear<'Msg> (records: OpRecord<'Msg> list) : DagOpRecord<'Msg> list =
        records
        |> List.fold
            (fun (acc: DagOpRecord<'Msg> list) (r: OpRecord<'Msg>) ->
                let parents =
                    match acc with
                    | [] -> []
                    | head :: _ -> [ head.Hash ]

                let dag =
                    // The linear record's TYPED actor carries over WHOLE (Phase 1144).
                    // Until then the DAG held a bare-string `UserId`, so this call
                    // projected the actor down to `Actor.id` and the Human/Agent
                    // distinction was lost in the embedding — a lossy step in the one
                    // direction the degenerate-equivalence contract is supposed to be
                    // faithful. Both records now fold the same `Actor.encode` bytes,
                    // so the embedding preserves attribution exactly.
                    create r.StreamId parents r.Op r.PromptId r.Actor r.Timestamp r.ResultEnvelope

                dag :: acc)
            []
        |> List.rev
