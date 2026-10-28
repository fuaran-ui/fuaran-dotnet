module FableLaws.BoardSnapshotLaw

// ============================================================================
//  Law 8 (Phase 1885) — a board snapshot reproduces the same data, on both
//  pipelines.
//
//  A board whose orders table is a live, hash-chained op stream is read, its
//  snapshot taken, the record encoded and decoded, the stream grown past the
//  snapshot's head, and the board reproduced from the record and the grown
//  stream. Printed: the record's canonical text, its hash, the board's rendered
//  data before and after, and the refusal of a rewritten stream.
//
//  The claim the byte comparison carries is the cross-host one: the SAME
//  record, the SAME hash and the SAME rendered bytes come out of .NET and out of
//  the browser. A snapshot taken on a server and reproduced in a client (or the
//  reverse) is only sound if those agree, and neither pipeline's own suite can
//  see the other's answer. The .NET leg's own law and refusal suite is
//  `Fuaran.UI.ServerDriven.Tests/BoardSnapshotTests.fs`.
// ============================================================================

open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.ServerDriven

let private resolutionText (r: BindingResolver.Resolution<Row seq>) : string =
    match r with
    | BindingResolver.Resolved rows -> Canonical.write Rendering.CrossHost (RowCodec.encodeRows rows)
    | BindingResolver.NotResolved -> "<not-resolved>"
    | BindingResolver.Errored m -> "<errored:" + m + ">"
    | BindingResolver.I18nUnresolved k -> "<i18n:" + k + ">"

let rec private readings (sources: BindingSources) (node: Node<'Msg>) : string list =
    let own =
        match node.Kind with
        | NodeKind.DataGrid g -> [ node.Id + ".source=" + resolutionText (BindingResolver.resolve sources g.Source) ]
        | NodeKind.Fact f -> [ node.Id + ".value=" + BindingResolver.resolveTextSource sources f.Value ]
        | NodeKind.Badge b -> [ node.Id + ".label=" + BindingResolver.resolveTextSource sources b.Label ]
        | _ -> []

    own @ (StructuralQuery.children node |> List.collect (readings sources))

let private boardJson =
    """{"id":"board","kind":{"$type":"Box","role":"Dashboard","layout":{"$type":"Auto"},"children":[{"id":"today","kind":{"$type":"Fact","label":"Today","value":{"$type":"Bound","binding":{"$type":"Now"}}}},{"id":"orders-grid","kind":{"$type":"DataGrid","rowKeyField":"id","columns":[{"field":"id","kind":{"$type":"Text"},"label":"Order"}],"source":{"$type":"Transform","params":[{"from":{"$type":"Filter","name":"region"},"name":"region"}],"pipeline":[{"$type":"filter","pred":{"$type":"binary","left":{"$type":"col","name":"region"},"op":"eq","right":{"$type":"param","name":"region"}}}],"source":{"$type":"State","key":"orders","defaultValue":[{"id":"o0","region":"emea","amount":0}]}}}},{"id":"orders-count","kind":{"$type":"Badge","variant":"Info","label":{"$type":"Bound","binding":{"$type":"Transform","pipeline":[{"$type":"groupBy","aggs":[{"fn":"count","name":"n","of":"id"}],"keys":[]}],"source":{"$type":"State","key":"orders","defaultValue":[{"id":"o0","region":"emea","amount":0}]}}}}}]}}"""

let private ordersStream: Stream<JVal, JVal, string> =
    OpStream.canonical
        OpStream.sha256Hash
        { Apply =
            fun op state ->
                match state with
                | JArr rows -> Ok(JArr(rows @ [ op ]))
                | _ -> Error "the orders state is not an array"
          Encode = Canonical.write Rendering.CrossHost
          Decode = Json.parse }

let private order (i: int) : JVal =
    JObj
        [ "id", JStr("o" + string i)
          "region", JStr(if i % 3 = 0 then "amer" else "emea")
          "amount", JInt(10 * i) ]

let private ordersAfter (first: int) (n: int) : CoreLiveStream<JVal, string> =
    let records =
        match
            OpStream.appendMany
                ordersStream
                (Fuaran.Core.Actor.Human "till")
                [ for i in first .. first + n - 1 -> order i ]
                (JArr [])
                []
        with
        | Ok(_, records) -> records
        | Error _ -> []

    { Stream = ordersStream
      Initial = JArr []
      Records = records }

/// The law's printed lines and its violation count.
type BoardSnapshotOutcome = { Lines: string list; Violations: int }

let boardSnapshotLaw () : BoardSnapshotOutcome =
    match Fuaran.UI.Ops.JsonDecode.decodeNodeObj boardJson with
    | Error e ->
        { Lines = [ "BOARD-SNAPSHOT decode-failed " + e.Code ]
          Violations = 1 }
    | Ok tree ->
        let live = ordersAfter 1 7

        let state =
            match OpStream.replay ordersStream.Witness live.Initial live.Records with
            | Ok v -> v
            | Error _ -> JArr []

        let sources =
            { BindingSources.empty with
                State = Map.ofList [ "orders", box state ]
                Filters = Map.ofList [ "region", box "emea" ]
                Now = "2026-10-28T09:14:00Z"
                Locale = "en-GB" }

        let shown = readings sources tree |> String.concat "|"

        match
            BoardSnapshot.capture
                tree
                sources
                (Map.ofList [ BoardChannel.State "orders", LiveSourceReplay.headOf "orders" live ])
        with
        | Error rs ->
            { Lines =
                [ "BOARD-SNAPSHOT capture-refused "
                  + (rs |> List.map SnapshotRefusal.describe |> String.concat "; ") ]
              Violations = 1 }
        | Ok snapshot ->
            let text = BoardSnapshot.encode snapshot
            let grown = LiveSourceReplay.ofCore (Map.ofList [ "orders", ordersAfter 1 12 ])

            let reproduced =
                match BoardSnapshot.decode text with
                | Error r -> "<decode-refused:" + SnapshotRefusal.describe r + ">"
                | Ok decoded ->
                    match BoardSnapshot.reproduce BindingSources.empty tree decoded grown with
                    | Ok again -> readings again tree |> String.concat "|"
                    | Error rs ->
                        "<refused:"
                        + (rs |> List.map SnapshotRefusal.describe |> String.concat "; ")
                        + ">"

            let rewritten = LiveSourceReplay.ofCore (Map.ofList [ "orders", ordersAfter 11 7 ])

            let rewrittenRefused =
                match BoardSnapshot.reproduce BindingSources.empty tree snapshot rewritten with
                | Error [ SnapshotRefusal.SourceUnreproducible(BoardChannel.State "orders", "orders", _) ] -> true
                | _ -> false

            let identical = reproduced = shown

            { Lines =
                [ "BOARD-SNAPSHOT record " + text
                  "BOARD-SNAPSHOT hash " + BoardSnapshot.hash snapshot
                  "BOARD-SNAPSHOT shown " + shown
                  "BOARD-SNAPSHOT reproduced " + reproduced
                  sprintf "BOARD-SNAPSHOT identical=%b rewritten-refused=%b" identical rewrittenRefused ]
              Violations = (if identical then 0 else 1) + (if rewrittenRefused then 0 else 1) }
