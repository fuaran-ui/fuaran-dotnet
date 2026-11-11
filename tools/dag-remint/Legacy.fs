namespace Fuaran.UI.OpStream.Dag.Remint

open System
open System.Globalization
open System.Text.Json
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions

// ============================================================================
//  Legacy — the pre-2645 DAG record, read and re-verified (Phase 2648).
//
//  Phase 2645 moved every stored DAG address onto Core's `Dag.nodeId` and deleted
//  the pre-cut record shape with it: the `OutcomeHash` a merge node committed to,
//  the in-record `Tombstoned` flag, and the two address rules `computeHash` /
//  `computeMergeHash`. This file holds the ONLY surviving copy of that shape and
//  of those rules, so the re-mint can read a pre-cut store and refuse one whose
//  stored addresses do not recompute under the rule that minted them. It is
//  deleted with the tool, in the release after the cut's.
//
//  The pre-cut address (Phase 408, typed actor since Phase 1144), verbatim:
//
//      ordinary: sha256( {"parents":[<sorted>],"op":<CanonicalJson.encodeOp op>
//                         ,"ts":<unix>,"actor":<Actor.encode>,"promptId":<null|"…">
//                         ,"result":<StreamEntry.encodeResult>} )
//      merge:    the same with "merge":"<outcome tree hash>" in place of "op"
//
//  Parents are sorted Ordinal inside the hash only; `Parents` keeps author order
//  (the head is the primary parent). A tombstoned record's op was reset to a
//  placeholder, so its address is carried as it stands and never recomputed.
// ============================================================================

/// One record of a DAG store written before Phase 2645.
type LegacyRecord<'Msg> =
    {
        StreamId: string
        Hash: string
        /// Author order: the head is the primary parent.
        Parents: string list
        Op: TreeOp<'Msg>
        /// `Some` on a merge node: the canonical hash of the tree it committed to.
        OutcomeHash: string option
        PromptId: string option
        Actor: Actor
        Timestamp: DateTimeOffset
        ResultEnvelope: OpResultEnvelope
        /// The payload was pruned; `Op` is a placeholder and `Hash` is carried.
        Tombstoned: bool
    }

module Legacy =

    let private jstr (s: string) : string = Fuaran.Core.JsonString.quote s

    let private parentsJson (parents: string list) : string =
        parents
        |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))
        |> List.map jstr
        |> String.concat ","
        |> sprintf "[%s]"

    let private provenanceJson (r: LegacyRecord<'Msg>) : string =
        ",\"ts\":"
        + r.Timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
        + ",\"actor\":"
        + Actor.encode r.Actor
        + ",\"promptId\":"
        + (match r.PromptId with
           | Some p -> jstr p
           | None -> "null")
        + ",\"result\":"
        + StreamEntry.encodeResult r.ResultEnvelope

    /// The address `r` recomputes to under the pre-cut rule that minted it.
    let address<'Msg> (r: LegacyRecord<'Msg>) : string =
        let body =
            match r.OutcomeHash with
            | Some outcome -> ",\"merge\":" + jstr outcome
            | None -> ",\"op\":" + CanonicalJson.encodeOp r.Op

        Fuaran.Core.Hash.sha256Hex ("{\"parents\":" + parentsJson r.Parents + body + provenanceJson r + "}")

    /// The envelope column / member as the pre-cut writers spelled it.
    let decodeEnvelope (json: string) : Result<OpResultEnvelope, string> =
        try
            use doc = JsonDocument.Parse json
            let root = doc.RootElement

            let text (name: string) =
                match root.TryGetProperty name with
                | true, v when v.ValueKind = JsonValueKind.String -> Option.ofObj (v.GetString())
                | _ -> None

            match text "$type" with
            | Some "Success" -> Ok OpResultEnvelope.Success
            | Some "Failure" ->
                match text "code", text "message" with
                | Some code, Some message -> Ok(OpResultEnvelope.Failure(code, message))
                | _ -> Error(sprintf "a Failure envelope without its code and message: %s" json)
            | _ -> Error(sprintf "not a result envelope: %s" json)
        with :? JsonException as e ->
            Error(sprintf "the result envelope is not JSON (%s): %s" e.Message json)

    let decodeActor (json: string) : Result<Actor, string> =
        match Actor.tryDecode json with
        | Some a -> Ok a
        | None -> Error(sprintf "not a typed actor: %s" json)

    let decodeOp (codec: IOpJsonCodec<'Msg>) (json: string) : Result<TreeOp<'Msg>, string> =
        codec.DecodeOp json |> Result.mapError CodecError.render

    /// One line of a pre-cut wire record set (`DagWire.encodeRecord` before the
    /// cut). A record without `tombstoned` is not a pre-cut one, and is refused
    /// here: the caller decides what a post-cut record means.
    let decodeWire (codec: IOpJsonCodec<'Msg>) (line: string) : Result<LegacyRecord<'Msg>, string> =
        try
            use doc = JsonDocument.Parse line
            let root = doc.RootElement

            let member' (name: string) =
                match root.TryGetProperty name with
                | true, v -> Some v
                | _ -> None

            let text name =
                member' name
                |> Option.bind (fun v ->
                    if v.ValueKind = JsonValueKind.String then
                        Option.ofObj (v.GetString())
                    else
                        None)

            let required name =
                match text name with
                | Some s -> Ok s
                | None -> Error(sprintf "the record has no string '%s'" name)

            match member' "tombstoned" with
            | None -> Error "not a pre-2645 record (it has no 'tombstoned' member)"
            | Some tomb ->
                match
                    required "streamId",
                    required "hash",
                    member' "parents",
                    member' "op",
                    member' "actor",
                    member' "timestamp",
                    member' "resultEnvelope"
                with
                | Ok streamId, Ok hash, Some parents, Some op, Some actor, Some ts, Some env when
                    parents.ValueKind = JsonValueKind.Array && ts.ValueKind = JsonValueKind.Number
                    ->
                    let parentList =
                        [ for p in parents.EnumerateArray() -> p.GetString() |> Option.ofObj |> Option.defaultValue "" ]

                    match
                        decodeOp codec (op.GetRawText()),
                        decodeActor (actor.GetRawText()),
                        decodeEnvelope (env.GetRawText())
                    with
                    | Ok op, Ok actor, Ok envelope ->
                        Ok
                            { StreamId = streamId
                              Hash = hash
                              Parents = parentList
                              Op = op
                              OutcomeHash = text "outcomeHash"
                              PromptId = text "promptId"
                              Actor = actor
                              Timestamp = DateTimeOffset.FromUnixTimeSeconds(ts.GetInt64())
                              ResultEnvelope = envelope
                              Tombstoned = tomb.ValueKind = JsonValueKind.True }
                    | Error e, _, _
                    | _, Error e, _
                    | _, _, Error e -> Error(sprintf "record %s: %s" hash e)
                | _ ->
                    Error "the record is missing one of streamId, hash, parents, op, actor, timestamp, resultEnvelope"
        with :? JsonException as e ->
            Error(sprintf "the line is not JSON: %s" e.Message)
