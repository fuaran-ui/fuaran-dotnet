namespace Fuaran.UI.OpStream.Sqlite

open System
open Microsoft.Data.Sqlite
open Fuaran.Core
open Fuaran.UI.Ops.ActionInvocation

// ============================================================================
//  ActionInvocationSqliteSink (Phase 889) — the durable user-action log.
//
//  Schema (one table, its own; NOT `op_stream`):
//
//      CREATE TABLE IF NOT EXISTS action_invocation (
//          id             INTEGER PRIMARY KEY AUTOINCREMENT,
//          at             INTEGER NOT NULL,
//          action         TEXT    NOT NULL,
//          node_id        TEXT    NULL,
//          event          TEXT    NULL,
//          outcome        TEXT    NOT NULL,
//          outcome_detail TEXT    NULL,
//          provenance     TEXT    NOT NULL,
//          path           TEXT    NOT NULL,
//          interaction_id TEXT    NULL,
//          payload_json   TEXT    NULL
//      );
//
//  ── Why a separate table and not an `OpRecord` ────────────────────────────
//  Phase 866 settled that a trigger points at the `Action` vocabulary and
//  nothing else, so a user action is never a `TreeOp` and cannot ride
//  `IOpStreamSink`'s `OpRecord`. It shares this package because it shares the
//  only thing that matters — a durable local file and its driver — not because
//  it shares the op stream.
//
//  ── Not hash-chained, deliberately ────────────────────────────────────────
//  The op stream is chained because it is the AUTHORING provenance and its
//  integrity is what replay rests on. This log is an append-only record of what
//  a user did; nothing replays it, and a second place in the codebase that
//  computes `PreviousHash` / `Sequence` is exactly how a stream silently
//  mis-chains (the reason `ApplyPersist` keeps one). A host that needs tamper
//  evidence over this table owns that, the way it owns retention.
//
//  ── Where it lands, and what it holds ─────────────────────────────────────
//  A local file the operator chose. There is no default destination, no
//  network surface, and `ActionLogPrivacyTests` asserts the second of those
//  mechanically over this project's own sources rather than in prose.
//
//  What a row holds is whatever the sink's `CaptureMode` permitted. The
//  constructor defaults to `Redacted` — the action CONSTRUCTOR and no payload
//  value — and `PayloadBearing` is an explicit argument a host has to type.
//  **The end user is not the opt-in party**: this is host-side instrumentation,
//  and obtaining a user's consent where the host is user-facing is the host's
//  obligation, which the redaction default does not discharge.
// ============================================================================

/// SQLite-backed `IActionInvocationSink` — the durable half of the Phase 889
/// user-action record.
///
/// `now` is injected (defaulting to the wall clock) because the INSTANT is the
/// host's to stamp: the record itself carries none, so that the server-driven
/// driver stays deterministic and an exported log stays verifiable. Passing a
/// fixed clock is what makes a round-trip test an equality rather than an
/// approximation.
type ActionInvocationSqliteSink(connectionString: string, captureMode: ActionCaptureMode, now: unit -> DateTimeOffset) =

    let openConnection () : SqliteConnection =
        let conn = new SqliteConnection(connectionString)
        conn.Open()
        conn

    let ensureSchema () =
        use conn = openConnection ()
        use cmd = conn.CreateCommand()

        cmd.CommandText <-
            """CREATE TABLE IF NOT EXISTS action_invocation (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    at             INTEGER NOT NULL,
    action         TEXT    NOT NULL,
    node_id        TEXT    NULL,
    event          TEXT    NULL,
    outcome        TEXT    NOT NULL,
    outcome_detail TEXT    NULL,
    provenance     TEXT    NOT NULL,
    path           TEXT    NOT NULL,
    interaction_id TEXT    NULL,
    payload_json   TEXT    NULL
);"""

        cmd.ExecuteNonQuery() |> ignore

    do ensureSchema ()

    /// Redacted, wall-clock — the shape a host that just wants the log gets.
    new(connectionString: string) =
        ActionInvocationSqliteSink(connectionString, ActionCaptureMode.Redacted, fun () -> DateTimeOffset.UtcNow)

    /// Explicit capture mode, wall-clock. Naming `PayloadBearing` here is the
    /// opt-in; there is no shorter spelling of it on purpose.
    new(connectionString: string, captureMode: ActionCaptureMode) =
        ActionInvocationSqliteSink(connectionString, captureMode, (fun () -> DateTimeOffset.UtcNow))

    /// Every entry the sink holds, oldest first. Not on the sink interface —
    /// reading back is a host / test concern, and `IActionInvocationSink` is
    /// deliberately write-only so a sink cannot become a query surface.
    member _.Read() : ActionInvocationEntry list =
        use conn = openConnection ()
        use cmd = conn.CreateCommand()

        cmd.CommandText <-
            """SELECT at, action, node_id, event, outcome, outcome_detail, provenance, path, interaction_id, payload_json
FROM action_invocation
ORDER BY id;"""

        use reader = cmd.ExecuteReader()
        let results = ResizeArray<ActionInvocationEntry>()

        let optString (ordinal: int) =
            if reader.IsDBNull ordinal then
                None
            else
                Some(reader.GetString ordinal)

        while reader.Read() do
            let detail = optString 5

            let outcome =
                match reader.GetString 4, detail with
                | "dispatched", _ -> ActionOutcome.Dispatched
                | "denied", d -> ActionOutcome.Denied(defaultArg d "")
                | "failed", d -> ActionOutcome.Failed(defaultArg d "")
                | other, _ -> invalidOp (sprintf "ActionInvocationSqliteSink: unknown outcome token '%s'." other)

            let provenance =
                match reader.GetString 6 with
                | "tree-declared" -> AffordanceProvenance.TreeDeclared
                | "renderer-synthesised" -> AffordanceProvenance.RendererSynthesised
                | other -> invalidOp (sprintf "ActionInvocationSqliteSink: unknown provenance token '%s'." other)

            let path =
                match reader.GetString 7 with
                | "client-renderer" -> DispatchPath.ClientRenderer
                | "server-driven" -> DispatchPath.ServerDriven
                | other -> invalidOp (sprintf "ActionInvocationSqliteSink: unknown path token '%s'." other)

            let payload =
                match optString 9 with
                | None -> None
                | Some json ->
                    match Json.parse json with
                    | Ok jv -> Some jv
                    | Error msg -> invalidOp (sprintf "ActionInvocationSqliteSink: undecodable payload JSON: %s" msg)

            results.Add
                { At = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64 0)
                  Invocation =
                    { Action = reader.GetString 1
                      NodeId = optString 2
                      Event = optString 3
                      Outcome = outcome
                      Provenance = provenance
                      Path = path
                      InteractionId = optString 8
                      Payload = payload } }

        List.ofSeq results

    /// Drop every row. For a host draining or resetting the log; the retention
    /// policy itself stays the host's, per the record's own contract.
    member _.Clear() : unit =
        use conn = openConnection ()
        use cmd = conn.CreateCommand()
        cmd.CommandText <- "DELETE FROM action_invocation;"
        cmd.ExecuteNonQuery() |> ignore

    interface IActionInvocationSink with

        member _.CaptureMode = captureMode

        member _.RecordActionInvocation(invocation: ActionInvocation) : unit =
            use conn = openConnection ()
            use cmd = conn.CreateCommand()

            cmd.CommandText <-
                """INSERT INTO action_invocation
    (at, action, node_id, event, outcome, outcome_detail, provenance, path, interaction_id, payload_json)
VALUES
    (@at, @action, @node_id, @event, @outcome, @outcome_detail, @provenance, @path, @interaction_id, @payload_json);"""

            // `upcast`, not `box`: `DBNull.Value` is nullable-typed under F# 10
            // nullness, and boxing it into a non-nullable `obj` is an FS3261.
            // Same spelling `SqliteSink` uses for its own nullable columns.
            let nullable (v: string option) : obj =
                match v with
                | Some s -> upcast s
                | None -> upcast DBNull.Value

            let outcomeToken, outcomeDetail =
                match invocation.Outcome with
                | ActionOutcome.Dispatched -> "dispatched", None
                | ActionOutcome.Denied reason -> "denied", Some reason
                | ActionOutcome.Failed message -> "failed", Some message

            let provenanceToken =
                match invocation.Provenance with
                | AffordanceProvenance.TreeDeclared -> "tree-declared"
                | AffordanceProvenance.RendererSynthesised -> "renderer-synthesised"

            let pathToken =
                match invocation.Path with
                | DispatchPath.ClientRenderer -> "client-renderer"
                | DispatchPath.ServerDriven -> "server-driven"

            cmd.Parameters.AddWithValue("@at", (now ()).ToUnixTimeMilliseconds()) |> ignore
            cmd.Parameters.AddWithValue("@action", invocation.Action) |> ignore
            cmd.Parameters.AddWithValue("@node_id", nullable invocation.NodeId) |> ignore
            cmd.Parameters.AddWithValue("@event", nullable invocation.Event) |> ignore
            cmd.Parameters.AddWithValue("@outcome", outcomeToken) |> ignore
            cmd.Parameters.AddWithValue("@outcome_detail", nullable outcomeDetail) |> ignore
            cmd.Parameters.AddWithValue("@provenance", provenanceToken) |> ignore
            cmd.Parameters.AddWithValue("@path", pathToken) |> ignore

            cmd.Parameters.AddWithValue("@interaction_id", nullable invocation.InteractionId)
            |> ignore

            cmd.Parameters.AddWithValue("@payload_json", nullable (invocation.Payload |> Option.map Json.encode))
            |> ignore

            cmd.ExecuteNonQuery() |> ignore
