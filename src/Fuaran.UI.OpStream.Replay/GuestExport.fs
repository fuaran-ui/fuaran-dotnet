namespace Fuaran.UI.OpStream.Replay

open System
open System.Globalization
open System.Text
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions

// ============================================================================
//  Portable guest export (Phase 274, §4o) — "the iframe you can serialise".
//
//  A mounted guest (`NodeKind.Mount`, Phase 265) is already tree + op-stream +
//  lineage, and all three are exportable under the no-lock-in principle. This
//  module makes an isolated guest a SELF-DESCRIBING, PORTABLE unit:
//
//    * `OpRecordWire`  — the canonical wire form of the LINEAR `OpRecord`
//      (the DAG sibling is `DagWire`; this mirrors its envelope discipline:
//      Ordinal-sorted keys, no whitespace, the nested op is
//      `CanonicalJson.encodeOp` verbatim, the actor is `Actor.encode`
//      verbatim). One record per line is the bundle's op-stream JSONL.
//    * `GuestExportBundle` — { scope id, initial tree (canonical wire JSON),
//      the guest's `guest-<scopeId>` op-stream, } with the full per-op
//      lineage (prompt ids, actor attribution, timestamps, hash chain)
//      carried verbatim — provenance survives the export (FGP 5).
//    * `GuestExport`   — read a guest's stream out of any `IOpStreamSink` and
//      encode the bundle to one canonical JSON document (or the ops alone as
//      JSONL).
//    * `GuestImport`   — the re-mount half: rebase the bundle into an
//      importing host (fresh scope id + `NodeId` rewrite so nothing collides
//      with the host or sibling guests; the hash chain is re-derived because
//      the rewrite changes the hashed pre-image, while PromptId / Actor /
//      Timestamp / result envelopes — the lineage — carry over verbatim),
//      verify the chain, and replay the op-stream to reconstruct the guest's
//      state on arrival (the Phase 177 resume posture: the op-stream, not a
//      snapshot, is the source of truth).
//
//  The identity remap walks children through `Introspect.getChildren` /
//  `withChildren` (the single shape every structural op traverses), so it
//  needs no per-NodeKind case table and stays correct as kinds are added.
//  Action closures inside specs are NOT remapped — they are `"<closure>"`
//  sentinels on the wire and decode to no-ops, so there is nothing to rebase.
// ============================================================================

/// Canonical wire form of the linear `OpRecord` (the Phase 274 bundle's
/// op-stream lines). Mirrors `DagWire`'s envelope discipline for the DAG
/// record: Ordinal-sorted keys, `promptId` omitted when `None`, the nested
/// `op` / `actor` reuse their canonical encoders verbatim.
[<RequireQualifiedAccess>]
module OpRecordWire =

    let private escapeInto (sb: StringBuilder) (s: string) : unit =
        sb.Append '"' |> ignore

        for ch in s do
            match ch with
            | '"' -> sb.Append "\\\"" |> ignore
            | '\\' -> sb.Append "\\\\" |> ignore
            | c when c < ' ' -> sb.Append(sprintf "\\u%04x" (int c)) |> ignore
            | c -> sb.Append c |> ignore

        sb.Append '"' |> ignore

    let private encodeEnvelope (sb: StringBuilder) (envelope: OpResultEnvelope) : unit =
        match envelope with
        | OpResultEnvelope.Success -> sb.Append "{\"$type\":\"Success\"}" |> ignore
        | OpResultEnvelope.Failure(code, message) ->
            sb.Append "{\"$type\":\"Failure\",\"code\":" |> ignore
            escapeInto sb code
            sb.Append ",\"message\":" |> ignore
            escapeInto sb message
            sb.Append '}' |> ignore

    /// Encode one linear `OpRecord` to its canonical JSON wire form. Keys in
    /// Ordinal order: actor < hash < op < previousHash < promptId <
    /// resultEnvelope < sequence < streamId < timestamp. `promptId` is
    /// omitted when `None`; `op` nests `CanonicalJson.encodeOp` and `actor`
    /// nests `Actor.encode` verbatim (the same bytes the hash chain folds).
    let encodeRecord<'Msg> (record: OpRecord<'Msg>) : string =
        let sb = StringBuilder()
        sb.Append '{' |> ignore

        sb.Append "\"actor\":" |> ignore
        sb.Append(Actor.encode record.Actor) |> ignore

        sb.Append ",\"hash\":" |> ignore
        escapeInto sb record.Hash

        sb.Append ",\"op\":" |> ignore
        sb.Append(CanonicalJson.encodeOp record.Op) |> ignore

        sb.Append ",\"previousHash\":" |> ignore
        escapeInto sb record.PreviousHash

        match record.PromptId with
        | Some p ->
            sb.Append ",\"promptId\":" |> ignore
            escapeInto sb p
        | None -> ()

        sb.Append ",\"resultEnvelope\":" |> ignore
        encodeEnvelope sb record.ResultEnvelope

        sb.Append ",\"sequence\":" |> ignore
        sb.Append(record.Sequence.ToString(CultureInfo.InvariantCulture)) |> ignore

        sb.Append ",\"streamId\":" |> ignore
        escapeInto sb record.StreamId

        sb.Append ",\"timestamp\":" |> ignore

        sb.Append(record.Timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture))
        |> ignore

        sb.Append '}' |> ignore
        sb.ToString()

    // ── Decode — the DagWire scanner, over the linear envelope ────────────

    /// Scan one JSON value starting at `s[i]`; return the index just past it.
    let rec internal scanValue (s: string) (i: int) : int =
        match s[i] with
        | '"' -> scanString s i
        | '{' -> scanBracketed s i '{' '}'
        | '[' -> scanBracketed s i '[' ']'
        | _ ->
            let mutable j = i

            while j < s.Length && s[j] <> ',' && s[j] <> '}' && s[j] <> ']' do
                j <- j + 1

            j

    and internal scanString (s: string) (i: int) : int =
        let mutable j = i + 1
        let mutable fin = false

        while not fin && j < s.Length do
            match s[j] with
            | '\\' -> j <- j + 2
            | '"' ->
                fin <- true
                j <- j + 1
            | _ -> j <- j + 1

        j

    and internal scanBracketed (s: string) (i: int) (openCh: char) (closeCh: char) : int =
        let mutable j = i + 1
        let mutable depth = 1

        while depth > 0 && j < s.Length do
            match s[j] with
            | '"' -> j <- scanString s j
            | c when c = openCh ->
                depth <- depth + 1
                j <- j + 1
            | c when c = closeCh ->
                depth <- depth - 1
                j <- j + 1
            | _ -> j <- j + 1

        j

    /// Unescape a raw JSON string substring (including its surrounding quotes).
    let internal unquote (raw: string) : string =
        let inner = raw.Substring(1, raw.Length - 2)
        let sb = StringBuilder()
        let mutable i = 0

        while i < inner.Length do
            if inner[i] = '\\' && i + 1 < inner.Length then
                match inner[i + 1] with
                | '"' ->
                    sb.Append '"' |> ignore
                    i <- i + 2
                | '\\' ->
                    sb.Append '\\' |> ignore
                    i <- i + 2
                | 'n' ->
                    sb.Append '\n' |> ignore
                    i <- i + 2
                | 't' ->
                    sb.Append '\t' |> ignore
                    i <- i + 2
                | 'u' when i + 5 < inner.Length ->
                    let code = Convert.ToInt32(inner.Substring(i + 2, 4), 16)
                    sb.Append(char code) |> ignore
                    i <- i + 6
                | other ->
                    sb.Append other |> ignore
                    i <- i + 2
            else
                sb.Append inner[i] |> ignore
                i <- i + 1

        sb.ToString()

    /// Map of top-level key → raw value substring for a canonical envelope.
    let internal topLevelFields (json: string) : Map<string, string> =
        let s = json.Trim()
        let mutable i = 1
        let mutable fields = Map.empty

        while i < s.Length && s[i] <> '}' do
            let keyEnd = scanString s i
            let key = unquote (s.Substring(i, keyEnd - i))
            let valStart = keyEnd + 1
            let valEnd = scanValue s valStart
            fields <- Map.add key (s.Substring(valStart, valEnd - valStart)) fields

            i <-
                if valEnd < s.Length && s[valEnd] = ',' then
                    valEnd + 1
                else
                    valEnd

        fields

    let private parseEnvelope (raw: string) : OpResultEnvelope =
        if raw.Contains "\"Success\"" then
            OpResultEnvelope.Success
        else
            let f = topLevelFields raw

            let get k =
                f |> Map.tryFind k |> Option.map unquote |> Option.defaultValue ""

            OpResultEnvelope.Failure(get "code", get "message")

    /// Decode one canonical linear-record envelope. `decodeOp` is the host's
    /// op decoder for the nested `op` object (the `'Msg` shape is host-owned,
    /// exactly as for the sinks' `IOpJsonCodec`).
    let decodeRecord<'Msg>
        (decodeOp: string -> Result<TreeOp<'Msg>, string>)
        (json: string)
        : Result<OpRecord<'Msg>, string> =
        try
            let f = topLevelFields json
            let req k = Map.tryFind k f

            match
                req "actor", req "hash", req "op", req "previousHash", req "sequence", req "streamId", req "timestamp"
            with
            | Some actorRaw, Some hashRaw, Some opRaw, Some prevRaw, Some seqRaw, Some streamRaw, Some tsRaw ->
                match decodeOp opRaw with
                | Error e -> Error(sprintf "OpRecordWire.decodeRecord: op decode failed: %s" e)
                | Ok op ->
                    match Actor.tryDecode actorRaw with
                    | None -> Error "OpRecordWire.decodeRecord: malformed actor payload"
                    | Some actor ->
                        let envelope =
                            f
                            |> Map.tryFind "resultEnvelope"
                            |> Option.map parseEnvelope
                            |> Option.defaultValue OpResultEnvelope.Success

                        Ok
                            { StreamId = unquote streamRaw
                              Sequence = Int32.Parse(seqRaw, CultureInfo.InvariantCulture)
                              PreviousHash = unquote prevRaw
                              Hash = unquote hashRaw
                              Op = op
                              PromptId = f |> Map.tryFind "promptId" |> Option.map unquote
                              Actor = actor
                              Timestamp =
                                DateTimeOffset.FromUnixTimeSeconds(Int64.Parse(tsRaw, CultureInfo.InvariantCulture))
                              ResultEnvelope = envelope }
            | _ ->
                Error
                    "OpRecordWire.decodeRecord: missing one of the required fields (actor/hash/op/previousHash/sequence/streamId/timestamp)"
        with ex ->
            Error(sprintf "OpRecordWire.decodeRecord: %s" ex.Message)

    /// The op-stream as JSONL — one canonical record envelope per line, in
    /// sequence order (the bundle's standalone op-stream artefact).
    let toJsonl<'Msg> (records: OpRecord<'Msg> list) : string =
        records |> List.map encodeRecord |> String.concat "\n"

    /// Parse a JSONL op-stream back to records (blank lines skipped).
    let ofJsonl<'Msg>
        (decodeOp: string -> Result<TreeOp<'Msg>, string>)
        (jsonl: string)
        : Result<OpRecord<'Msg> list, string> =
        let lines =
            jsonl.Split('\n')
            |> Array.map (fun l -> l.Trim())
            |> Array.filter (fun l -> l <> "")

        (Ok [], lines)
        ||> Array.fold (fun acc line ->
            match acc with
            | Error e -> Error e
            | Ok records ->
                match decodeRecord decodeOp line with
                | Ok r -> Ok(r :: records)
                | Error e -> Error e)
        |> Result.map List.rev

/// A guest scope as a self-describing portable unit (Phase 274): the scope id,
/// the guest's initial tree (the state at mount, before its op-stream), and
/// its full `guest-<scopeId>` op-stream with lineage. `FormatVersion` guards
/// forward evolution of the envelope.
type GuestExportBundle =
    { FormatVersion: int
      ScopeId: string
      InitialTree: Node<obj>
      Records: OpRecord<obj> list }

[<RequireQualifiedAccess>]
module GuestExport =

    [<Literal>]
    let FormatVersion = 1

    /// The obj-typed op decoder the bundle codec uses — the language tier's
    /// wire decoder with its structured error flattened to a string (the
    /// shape `OpRecordWire.decodeRecord` / `ofJsonl` take).
    let decodeOpString (json: string) : Result<TreeOp<obj>, string> =
        JsonDecode.decodeOp json
        |> Result.mapError (fun e -> sprintf "%s at %s: %s" e.Code e.Path e.Message)

    /// The obj-typed node decoder with the same flattening. Guest export is a
    /// persistence / re-encode boundary, so it takes the raw `Node<obj>`
    /// (`decodeNodeObj`) rather than a `WireTree` — the bundle it assembles is
    /// re-serialised, never handed to a live renderer.
    let decodeNodeString (json: string) : Result<Node<obj>, string> =
        JsonDecode.decodeNodeObj json
        |> Result.mapError (fun e -> sprintf "%s at %s: %s" e.Code e.Path e.Message)

    /// Assemble a bundle from parts already in hand (the actor-mirror shape:
    /// the host holds the initial tree and the journalled records).
    let bundle (scopeId: string) (initialTree: Node<obj>) (records: OpRecord<obj> list) : GuestExportBundle =
        { FormatVersion = FormatVersion
          ScopeId = scopeId
          InitialTree = initialTree
          Records = records }

    /// Export a guest scope from a sink: read its whole `guest-<scopeId>`
    /// stream (sequence 1..latest) into a bundle. `initialTree` is the guest's
    /// tree at mount time (the replay base — the host that mounted the guest
    /// holds it; for an actor-hosted guest it is the actor's `InitialTree`).
    let export (sink: IOpStreamSink<obj>) (scopeId: string) (initialTree: Node<obj>) : Async<GuestExportBundle> =
        async {
            let streamId = GuestStream.streamId scopeId
            let! latest = sink.LatestSequence streamId

            let! records =
                if latest <= 0 then
                    async.Return []
                else
                    sink.Replay(streamId, 1, latest)

            return bundle scopeId initialTree records
        }

    /// The tree the bundle reconstructs — the initial tree folded through the
    /// op-stream. This is the guest's exported state; import re-derives it and
    /// the canonical encodings must agree byte-for-byte.
    let finalTree (b: GuestExportBundle) : Result<Node<obj>, ReplayError> = Replay.applyTo b.InitialTree b.Records

    /// The bundle's op-stream as standalone JSONL (one record per line).
    let opsJsonl (b: GuestExportBundle) : string = OpRecordWire.toJsonl b.Records

    /// Encode the bundle as one canonical JSON document. Keys in Ordinal
    /// order: formatVersion < initialTree < ops < scopeId; `initialTree` is
    /// `CanonicalJson.encodeNode` verbatim, `ops` the record envelopes in
    /// sequence order.
    let encode (b: GuestExportBundle) : string =
        let sb = StringBuilder()
        sb.Append "{\"formatVersion\":" |> ignore
        sb.Append(b.FormatVersion.ToString(CultureInfo.InvariantCulture)) |> ignore
        sb.Append ",\"initialTree\":" |> ignore
        sb.Append(CanonicalJson.encodeNode b.InitialTree) |> ignore
        sb.Append ",\"ops\":[" |> ignore

        b.Records
        |> List.iteri (fun i r ->
            if i > 0 then
                sb.Append ',' |> ignore

            sb.Append(OpRecordWire.encodeRecord r) |> ignore)

        sb.Append "],\"scopeId\":" |> ignore
        sb.Append('"') |> ignore

        for ch in b.ScopeId do
            match ch with
            | '"' -> sb.Append "\\\"" |> ignore
            | '\\' -> sb.Append "\\\\" |> ignore
            | c when c < ' ' -> sb.Append(sprintf "\\u%04x" (int c)) |> ignore
            | c -> sb.Append c |> ignore

        sb.Append "\"}" |> ignore
        sb.ToString()

    /// Split a raw JSON array's elements (objects/strings/numbers) using the
    /// wire scanner.
    let private arrayElements (raw: string) : string list =
        let s = raw.Trim()
        let inner = s.Substring(1, s.Length - 2)
        let result = ResizeArray<string>()
        let mutable i = 0

        while i < inner.Length do
            match inner[i] with
            | ',' -> i <- i + 1
            | c when c = ' ' || c = '\n' || c = '\r' || c = '\t' -> i <- i + 1
            | _ ->
                let e = OpRecordWire.scanValue inner i
                result.Add(inner.Substring(i, e - i))
                i <- e

        List.ofSeq result

    /// Decode a bundle document produced by `encode`. Rejects an unknown
    /// `formatVersion` (forward-compat guard) and surfaces the first
    /// tree / record decode failure verbatim.
    let decode (json: string) : Result<GuestExportBundle, string> =
        try
            let f = OpRecordWire.topLevelFields json

            match
                Map.tryFind "formatVersion" f, Map.tryFind "initialTree" f, Map.tryFind "ops" f, Map.tryFind "scopeId" f
            with
            | Some versionRaw, Some treeRaw, Some opsRaw, Some scopeRaw ->
                let version = Int32.Parse(versionRaw.Trim(), CultureInfo.InvariantCulture)

                if version <> FormatVersion then
                    Error(
                        sprintf "GuestExport.decode: unsupported formatVersion %d (supported: %d)" version FormatVersion
                    )
                else
                    match decodeNodeString treeRaw with
                    | Error e -> Error(sprintf "GuestExport.decode: initialTree decode failed: %s" e)
                    | Ok initialTree ->
                        let recordResults =
                            arrayElements opsRaw |> List.map (OpRecordWire.decodeRecord decodeOpString)

                        let firstError =
                            recordResults
                            |> List.tryPick (function
                                | Error e -> Some e
                                | Ok _ -> None)

                        match firstError with
                        | Some e -> Error(sprintf "GuestExport.decode: %s" e)
                        | None ->
                            let records =
                                recordResults
                                |> List.choose (function
                                    | Ok r -> Some r
                                    | Error _ -> None)

                            Ok
                                { FormatVersion = version
                                  ScopeId = OpRecordWire.unquote scopeRaw
                                  InitialTree = initialTree
                                  Records = records }
            | _ ->
                Error "GuestExport.decode: missing one of the required fields (formatVersion/initialTree/ops/scopeId)"
        with ex ->
            Error(sprintf "GuestExport.decode: %s" ex.Message)

[<RequireQualifiedAccess>]
module GuestImport =

    /// Rewrite every `NodeId` in a tree (the node's own id + its whole
    /// interior, walked through the `getChildren` / `withChildren` shape every
    /// structural op traverses — including a `FragmentDecl` body). Kinds
    /// without a children shape are leaves; their spec records carry no
    /// `NodeId`s.
    let rec mapTreeIds (f: NodeId -> NodeId) (node: Node<'Msg>) : Node<'Msg> =
        let kind =
            match Introspect.getChildren node.Kind with
            | Some children ->
                match Introspect.withChildren node.Kind (children |> List.map (mapTreeIds f)) with
                | Some rebuilt -> rebuilt
                | None -> node.Kind
            | None -> node.Kind

        let (NodeId mapped) = f (NodeId node.Id)

        { node with Id = mapped; Kind = kind }

    /// Rewrite a kind's interior ids by walking its children shape (used for
    /// the subtree an `EditNode` carries).
    let private mapKindIds (f: NodeId -> NodeId) (kind: NodeKind<'Msg>) : NodeKind<'Msg> =
        match Introspect.getChildren kind with
        | Some children ->
            match Introspect.withChildren kind (children |> List.map (mapTreeIds f)) with
            | Some rebuilt -> rebuilt
            | None -> kind
        | None -> kind

    /// Rewrite every `NodeId` a `TreeOp` carries — addressed targets AND the
    /// interiors of any subtree it introduces (`InsertChild` child,
    /// `ReplaceRoot` node, `EditNode`'s replacement kind).
    let rec mapOpIds (f: NodeId -> NodeId) (op: TreeOp<'Msg>) : TreeOp<'Msg> =
        match op with
        | TreeOp.EditNode(id, kind) -> TreeOp.EditNode(f id, mapKindIds f kind)
        | TreeOp.UpdateProp(id, path, value) -> TreeOp.UpdateProp(f id, path, value)
        | TreeOp.ReplaceBinding(id, slot, binding) -> TreeOp.ReplaceBinding(f id, slot, binding)
        | TreeOp.UpdateStyle(id, style) -> TreeOp.UpdateStyle(f id, style)
        | TreeOp.UpdateState(id, state) -> TreeOp.UpdateState(f id, state)
        | TreeOp.InsertChild(parentId, child) -> TreeOp.InsertChild(f parentId, mapTreeIds f child)
        | TreeOp.RemoveNode id -> TreeOp.RemoveNode(f id)
        | TreeOp.MoveNode(id, newParentId) -> TreeOp.MoveNode(f id, f newParentId)
        | TreeOp.ReorderChildren(parentId, newOrder) -> TreeOp.ReorderChildren(f parentId, newOrder |> List.map f)
        | TreeOp.ReplaceRoot node -> TreeOp.ReplaceRoot(mapTreeIds f node)
        | TreeOp.Batch ops -> TreeOp.Batch(ops |> List.map (mapOpIds f))

    /// The default collision-proof id rebase: namespace every guest id under
    /// the importing scope (`"<newScopeId>.<id>"`). Deterministic, order-
    /// preserving, and disjoint from any host id set that doesn't use the
    /// fresh scope id as a prefix.
    let prefixIds (newScopeId: string) : NodeId -> NodeId =
        fun (NodeId raw) -> NodeId(newScopeId + "." + raw)

    /// Rebase a bundle into an importing host: a fresh scope id (the stream
    /// re-keys to `guest-<newScopeId>`), every `NodeId` rewritten via `mapId`,
    /// and the hash chain RE-DERIVED (the rewrite changes the hashed pre-image
    /// and the chain is per-stream). The lineage — `PromptId`, `Actor`,
    /// `Timestamp`, result envelopes, sequence order — carries over verbatim,
    /// so "which prompts/agent produced this guest?" survives the rebase; the
    /// ORIGINAL chain remains intact in the source bundle artefact.
    let remap (newScopeId: string) (mapId: NodeId -> NodeId) (b: GuestExportBundle) : GuestExportBundle =
        let streamId = GuestStream.streamId newScopeId

        let records =
            (([], HashChain.genesisPreviousHash), b.Records)
            ||> List.fold (fun (acc, previousHash) record ->
                let op = mapOpIds mapId record.Op

                let hash =
                    HashChain.computeHash
                        previousHash
                        op
                        record.Sequence
                        record.Timestamp
                        record.Actor
                        record.PromptId
                        record.ResultEnvelope

                let rebased =
                    { record with
                        StreamId = streamId
                        PreviousHash = previousHash
                        Hash = hash
                        Op = op }

                rebased :: acc, hash)
            |> fst
            |> List.rev

        { b with
            ScopeId = newScopeId
            InitialTree = mapTreeIds mapId b.InitialTree
            Records = records }

    /// Reconstruct the guest's state from the bundle alone: verify the hash
    /// chain (tamper-evidence on arrival), then replay the op-stream over the
    /// initial tree (Phase 177 posture — the op-stream is the source of
    /// truth, no snapshot trusted).
    let reconstruct (b: GuestExportBundle) : Result<Node<obj>, string> =
        match Verify.chain b.Records with
        | Error e -> Error(sprintf "GuestImport.reconstruct: hash-chain verification failed: %A" e)
        | Ok() ->
            match Replay.applyTo b.InitialTree b.Records with
            | Error(ReplayError.ApplyFailed(sequence, applyError)) ->
                Error(sprintf "GuestImport.reconstruct: replay diverged at record %d: %s" sequence applyError.Message)
            | Ok tree -> Ok tree

    /// Load a bundle into an importing host's sink: refuse a scope whose
    /// `guest-<scopeId>` stream already has records (collision guard — remap
    /// to a fresh scope first), reconstruct via chain-verify + replay, then
    /// append the records so the guest's provenance lives in the new host's
    /// journal too. Returns the reconstructed tree — ready to register as the
    /// mounted guest's view.
    let load (sink: IOpStreamSink<obj>) (b: GuestExportBundle) : Async<Result<Node<obj>, string>> =
        async {
            let streamId = GuestStream.streamId b.ScopeId
            let! latest = sink.LatestSequence streamId

            if latest > 0 then
                return
                    Error(
                        sprintf
                            "GuestImport.load: stream '%s' already holds %d record(s) — remap the bundle to a fresh scope id before loading"
                            streamId
                            latest
                    )
            else
                match reconstruct b with
                | Error e -> return Error e
                | Ok tree ->
                    for record in b.Records do
                        do! sink.Append record

                    return Ok tree
        }
