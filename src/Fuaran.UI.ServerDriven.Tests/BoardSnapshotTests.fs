module Fuaran.UI.ServerDriven.Tests.BoardSnapshotTests

open System.IO
open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.ServerDriven

// ============================================================================
//  Phase 1885 — reproducible board snapshots, the .NET leg.
//
//  THE LAW. For a board (tree, sources) whose live channels are op streams:
//  capture → encode → decode → reproduce yields sources under which the board's
//  RENDERED DATA — every row feed and every bound text, resolved through the
//  renderer's own resolver and encoded canonically — is byte-identical to what
//  the original sources render. It is stated over the corpus's dashboard trees
//  and a board whose orders table is a live, hash-chained op stream that keeps
//  growing after the snapshot is taken.
//
//  The Fable leg of the same law is `tests/fable-laws` (law 4): the same board,
//  the same stream, on both pipelines, byte-compared.
//
//  THE REFUSALS. Each irreproducible input is refused by name — never
//  approximated.
// ============================================================================

// ---------------------------------------------------------------------------
//  the rendered data of a board
// ---------------------------------------------------------------------------

let private resolutionText (r: BindingResolver.Resolution<Row seq>) : string =
    match r with
    | BindingResolver.Resolved rows -> Canonical.write Rendering.CrossHost (RowCodec.encodeRows rows)
    | BindingResolver.NotResolved -> "<not-resolved>"
    | BindingResolver.Errored m -> "<errored:" + m + ">"
    | BindingResolver.I18nUnresolved k -> "<i18n:" + k + ">"

/// Every row feed and bound text a board renders, in walk order, as text.
let rec private readings (sources: BindingSources) (node: Node<'Msg>) : string list =
    let own =
        match node.Kind with
        | NodeKind.DataGrid g -> [ node.Id + ".source=" + resolutionText (BindingResolver.resolve sources g.Source) ]
        | NodeKind.Chart c -> [ node.Id + ".source=" + resolutionText (BindingResolver.resolve sources c.Source) ]
        | NodeKind.Fact f -> [ node.Id + ".value=" + BindingResolver.resolveTextSource sources f.Value ]
        | NodeKind.Badge b -> [ node.Id + ".label=" + BindingResolver.resolveTextSource sources b.Label ]
        | _ -> []

    own @ (StructuralQuery.children node |> List.collect (readings sources))

let private rendered (sources: BindingSources) (node: Node<'Msg>) : string =
    readings sources node |> String.concat "\n"

/// A store value: boxed, and never null.
let private stored (v: 'a) : obj = nonNull (box v)

let private decodeTree (json: string) : Node<obj> =
    match Fuaran.UI.Ops.JsonDecode.decodeNodeObj json with
    | Ok n -> n
    | Error e -> failtestf "the board failed to decode: %s at %s" e.Code e.Path

// ---------------------------------------------------------------------------
//  the board: an orders table that is a live op stream
// ---------------------------------------------------------------------------

let private boardJson =
    """{"id":"board","kind":{"$type":"Box","role":"Dashboard","layout":{"$type":"Auto"},"children":[
{"id":"today","kind":{"$type":"Fact","label":"Today","value":{"$type":"Bound","binding":{"$type":"Now"}}}},
{"id":"orders-grid","kind":{"$type":"DataGrid","rowKeyField":"id","columns":[{"field":"id","kind":{"$type":"Text"},"label":"Order"},{"field":"amount","kind":{"$type":"Text"},"label":"Amount"}],
 "source":{"$type":"Transform","params":[{"from":{"$type":"Filter","name":"region"},"name":"region"}],
  "pipeline":[{"$type":"filter","pred":{"$type":"binary","left":{"$type":"col","name":"region"},"op":"eq","right":{"$type":"param","name":"region"}}}],
  "source":{"$type":"State","key":"orders","defaultValue":[{"id":"o0","region":"emea","amount":0}]}}}},
{"id":"orders-count","kind":{"$type":"Badge","variant":"Info","label":{"$type":"Bound","binding":{"$type":"Transform","pipeline":[{"$type":"groupBy","aggs":[{"fn":"count","name":"n","of":"id"}],"keys":[]}],
  "source":{"$type":"State","key":"orders","defaultValue":[{"id":"o0","region":"emea","amount":0}]}}}}}]}}"""

/// The orders stream: each op appends one row object.
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
        [ "id", JStr(sprintf "o%d" i)
          "region", JStr(if i % 3 = 0 then "amer" else "emea")
          "amount", JInt(10 * i) ]

let private ordersAfter (n: int) : CoreLiveStream<JVal, string> =
    let records =
        match OpStream.appendMany ordersStream (Actor.Human "till") [ for i in 1..n -> order i ] (JArr []) [] with
        | Ok(_, records) -> records
        | Error(i, e) -> failtestf "order %d refused: %s" i e

    { Stream = ordersStream
      Initial = JArr []
      Records = records }

let private foldOf (s: CoreLiveStream<JVal, string>) : JVal =
    match OpStream.replay s.Stream.Witness s.Initial s.Records with
    | Ok v -> v
    | Error(i, e) -> failtestf "replay refused %d: %s" i e

/// The board's sources as its host holds them after `n` orders.
let private boardSources (n: int) : BindingSources * Map<BoardChannel, LiveSource> =
    let live = ordersAfter n

    { BindingSources.empty with
        State = Map.ofList [ "orders", stored (foldOf live) ]
        Filters = Map.ofList [ "region", stored "emea" ]
        Now = "2026-10-28T09:14:00Z"
        Locale = "en-GB" },
    Map.ofList [ BoardChannel.State "orders", LiveSourceReplay.headOf "orders" live ]

let private capture tree sources live =
    match BoardSnapshot.capture tree sources live with
    | Ok s -> s
    | Error rs -> failtestf "capture refused: %s" (rs |> List.map SnapshotRefusal.describe |> String.concat "; ")

let private reproduce tree snapshot replay =
    match BoardSnapshot.reproduce BindingSources.empty tree snapshot replay with
    | Ok s -> s
    | Error rs -> failtestf "reproduce refused: %s" (rs |> List.map SnapshotRefusal.describe |> String.concat "; ")

let private noStreams: ILiveSourceReplay =
    LiveSourceReplay.ofCore Map.empty<string, CoreLiveStream<JVal, string>>

let private refusalsOf (r: Result<'a, SnapshotRefusal list>) : SnapshotRefusal list =
    match r with
    | Ok _ -> failtest "expected a refusal"
    | Error rs -> rs

// ---------------------------------------------------------------------------
//  the law
// ---------------------------------------------------------------------------

[<Tests>]
let roundTrip =
    testList
        "Phase 1885 — snapshot → reproduce → identical rendered data"
        [ testCase "a live board reproduces after its stream has grown"
          <| fun _ ->
              let tree = decodeTree boardJson
              let sources, live = boardSources 7
              let shown = rendered sources tree
              let snapshot = capture tree sources live

              // The text crosses a process boundary: decode it, never reuse the value.
              let text = BoardSnapshot.encode snapshot

              let decoded =
                  match BoardSnapshot.decode text with
                  | Ok s -> s
                  | Error r -> failtestf "decode refused: %s" (SnapshotRefusal.describe r)

              Expect.equal decoded snapshot "the record round-trips through its text"
              Expect.equal (BoardSnapshot.hash decoded) (BoardSnapshot.hash snapshot) "the hash is the text's"

              // The orders stream has moved on since the board was read.
              let grown = ordersAfter 12
              let replay = LiveSourceReplay.ofCore (Map.ofList [ "orders", grown ])
              let again = reproduce tree decoded replay

              Expect.equal (rendered again tree) shown "the reproduced board renders the same bytes"
              Expect.notEqual (rendered (fst (boardSources 12)) tree) shown "and the live board has moved on"

              Expect.stringContains shown "today.value=2026-10-28T09:14:00Z" "the clock is the pinned one"
              Expect.stringContains shown "orders-count.label=7" "the count is over the seven orders read"

          testCase "the record embeds the parameters and references the stream"
          <| fun _ ->
              let tree = decodeTree boardJson
              let sources, live = boardSources 3
              let snapshot = capture tree sources live

              Expect.equal
                  snapshot.Parameters
                  [ { Channel = BoardChannel.Filter "region"
                      Value = JStr "emea"
                      Form = ValueForm.Scalar } ]
                  "the chip is embedded"

              Expect.equal
                  (snapshot.Sources |> List.map (fun s -> s.Stream, s.Length))
                  [ "orders", 3 ]
                  "the stream is referenced"

              Expect.equal snapshot.Tree (BoardSnapshot.treeDigest tree) "the tree is referenced by digest"

              Expect.stringStarts
                  (BoardSnapshot.encode snapshot)
                  """{"board":"fuaran-board-snapshot@1","locale":"en-GB","now":"2026-10-28T09:14:00Z","parameters":[{"channel":"filter","form":"scalar","key":"region","value":"emea"}],"sources":[{"channel":"state","form":"wire","head":"""
                  "the canonical text sorts its members"

          testCase "the corpus's dashboard trees reproduce"
          <| fun _ ->
              match Fuaran.Tests.CorpusRoot.tryFind () with
              | None -> skiptest Fuaran.Tests.CorpusRoot.AbsentSkipReason
              | Some root ->
                  let cases =
                      [ "filterable-static-dashboard", Map.ofList [ "region", stored "emea"; "genre", stored "drama" ]
                        "grid-transform-param", Map.ofList [ "dept", stored "sales" ]
                        "now-environment-binding", Map.empty ]

                  for name, chips in cases do
                      let tree =
                          decodeTree (File.ReadAllText(Path.Combine(root, "nodes", name + ".json")))

                      let sources =
                          { BindingSources.empty with
                              Filters = chips
                              Now = "2026-08-02T06:59:24Z" }

                      let snapshot = capture tree sources Map.empty

                      let again =
                          match BoardSnapshot.decode (BoardSnapshot.encode snapshot) with
                          | Ok s -> reproduce tree s noStreams
                          | Error r -> failtestf "%s: %s" name (SnapshotRefusal.describe r)

                      Expect.equal (rendered again tree) (rendered sources tree) (name + " renders the same bytes")

                      Expect.isFalse
                          ((rendered sources tree).Contains "<not-resolved>")
                          (name + " renders data rather than placeholders") ]

// ---------------------------------------------------------------------------
//  the refusals
// ---------------------------------------------------------------------------

[<Tests>]
let refusals =
    testList
        "Phase 1885 — what cannot be reproduced is refused"
        [ testCase "a live source with no op stream"
          <| fun _ ->
              let tree = decodeTree boardJson
              let sources, _ = boardSources 2

              let rs =
                  BoardSnapshot.capture tree sources (Map.ofList [ BoardChannel.State "orders", LiveSource.Unstreamed ])
                  |> refusalsOf

              Expect.equal rs [ SnapshotRefusal.Unstreamed(BoardChannel.State "orders") ] "refused by channel"

          testCase "an unstreamed feed the board does not read is no obstacle"
          <| fun _ ->
              let tree = decodeTree boardJson
              let sources, live = boardSources 2

              let live = live |> Map.add (BoardChannel.State "ticker") LiveSource.Unstreamed

              Expect.isOk (BoardSnapshot.capture tree sources live) "only what the tree reads is recorded"

          testCase "a Computed reader"
          <| fun _ ->
              let tree =
                  decodeTree
                      """{"id":"c","kind":{"$type":"Fact","label":"X","value":{"$type":"Bound","binding":{"$type":"Computed","fn":"<closure>"}}}}"""

              let rs = BoardSnapshot.capture tree BindingSources.empty Map.empty |> refusalsOf

              Expect.equal rs [ SnapshotRefusal.OpaqueReader("c", "a Computed closure") ] "refused by reader"

          testCase "a value with no exact wire form"
          <| fun _ ->
              let tree = decodeTree boardJson
              let sources, live = boardSources 2

              let rs =
                  BoardSnapshot.capture
                      tree
                      { sources with
                          Filters = Map.ofList [ "region", stored 3 ] }
                      live
                  |> refusalsOf

              Expect.equal rs [ SnapshotRefusal.NoWireForm(BoardChannel.Filter "region") ] "a boxed integer is refused"

          testCase "a different tree"
          <| fun _ ->
              let tree = decodeTree boardJson
              let sources, live = boardSources 2
              let snapshot = capture tree sources live
              let other = decodeTree (boardJson.Replace("Today", "Now"))

              match
                  BoardSnapshot.reproduce BindingSources.empty other snapshot noStreams
                  |> refusalsOf
              with
              | [ SnapshotRefusal.TreeMismatch(expected, _) ] -> Expect.equal expected snapshot.Tree "names the tree"
              | rs -> failtestf "unexpected %A" rs

          testCase "a rewritten stream, a short stream, an absent stream"
          <| fun _ ->
              let tree = decodeTree boardJson
              let sources, live = boardSources 4
              let snapshot = capture tree sources live

              let rewritten =
                  let s = ordersAfter 4

                  { s with
                      Records =
                          s.Records
                          |> List.mapi (fun i r -> if i = 1 then { r with Op = order 99 } else r) }

              let cases =
                  [ "rewritten", LiveSourceReplay.ofCore (Map.ofList [ "orders", rewritten ])
                    "short", LiveSourceReplay.ofCore (Map.ofList [ "orders", ordersAfter 3 ])
                    "absent", noStreams ]

              for name, replay in cases do
                  match BoardSnapshot.reproduce BindingSources.empty tree snapshot replay |> refusalsOf with
                  | [ SnapshotRefusal.SourceUnreproducible(BoardChannel.State "orders", "orders", _) ] -> ()
                  | rs -> failtestf "%s: unexpected %A" name rs

          testCase "a rewritten history whose chain still verifies is refused at the head"
          <| fun _ ->
              let tree = decodeTree boardJson
              let sources, live = boardSources 4
              let snapshot = capture tree sources live

              // A writer free to re-chain: different ops, an intact chain, a different head.
              let other =
                  match
                      OpStream.appendMany ordersStream (Actor.Human "till") [ for i in 11..14 -> order i ] (JArr []) []
                  with
                  | Ok(_, records) -> records
                  | Error _ -> failtest "append refused"

              let replay =
                  LiveSourceReplay.ofCore (
                      Map.ofList
                          [ "orders",
                            { Stream = ordersStream
                              Initial = JArr []
                              Records = other } ]
                  )

              match BoardSnapshot.reproduce BindingSources.empty tree snapshot replay |> refusalsOf with
              | [ SnapshotRefusal.SourceUnreproducible(_, _, reason) ] ->
                  Expect.stringContains reason "not at the snapshot's head" "names the head"
              | rs -> failtestf "unexpected %A" rs

          testCase "a capability result reproduces as a typed error, never a value"
          <| fun _ ->
              let tree = decodeTree boardJson
              let sources, live = boardSources 1
              let snapshot = capture tree sources live

              let again =
                  reproduce tree snapshot (LiveSourceReplay.ofCore (Map.ofList [ "orders", ordersAfter 1 ]))

              match again.CapabilityInvoker "fx.rate" [] with
              | Deferred.Error m -> Expect.stringContains m "not reproducible" "says why"
              | _ -> failtest "the reproduced invoker answered"

          testCase "a malformed record"
          <| fun _ ->
              for text in
                  [ "[]"
                    """{"board":"fuaran-board-snapshot@2","tree":"","now":"","locale":"","parameters":[],"sources":[]}"""
                    """{"board":"fuaran-board-snapshot@1","tree":"","now":"","locale":"","parameters":[{"channel":"cookie","key":"k","value":1,"form":"wire"}],"sources":[]}"""
                    """{"board":"fuaran-board-snapshot@1","tree":"","now":"","locale":"","parameters":[],"sources":[],"note":"x"}"""
                    """{"board":"fuaran-board-snapshot@1","tree":"","now":"","locale":"","parameters":[],"sources":[{"channel":"state","key":"k","stream":"s","length":-1,"head":"h","form":"wire"}]}"""
                    "{" ] do
                  match BoardSnapshot.decode text with
                  | Error(SnapshotRefusal.Malformed _) -> ()
                  | other -> failtestf "accepted %s as %A" text other

          testCase "the publication carries the hash and cannot close its element"
          <| fun _ ->
              let tree = decodeTree boardJson
              let sources, live = boardSources 1

              let snapshot =
                  capture
                      tree
                      { sources with
                          Filters = Map.ofList [ "region", stored "</script>" ] }
                      live

              let published = BoardSnapshot.publication snapshot

              Expect.stringContains published (BoardSnapshot.hash snapshot) "the hash is the attribute"
              Expect.equal (published.IndexOf "</script>") (published.Length - 9) "only the closing tag closes it" ]
