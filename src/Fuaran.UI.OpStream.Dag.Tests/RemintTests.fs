module Fuaran.UI.OpStream.Dag.Tests.RemintTests

open System
open System.IO
open System.Security.Cryptography
open Expecto
open Microsoft.Data.Sqlite
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.Sqlite
open Fuaran.UI.OpStream.Dag.Remint

// ============================================================================
//  The one-release re-mint (Phase 2648), against a store minted BEFORE the cut.
//
//  `remint/pre-2645-store.db` and `remint/pre-2645-records.jsonl` were written by
//  the pre-cut engine itself (fuaran-dotnet at 7b55fc9a, the tree Phase 2645 was
//  cut from): its `SqliteDagSink`, its `DagMerge.merge` (an outcome-hash merge
//  node) and its `Tombstone` (two pruned records), then its `DagWire.encodeRecord`
//  over the same records. They are the oracle: nothing in this repository can
//  write them any more. `remint/initial.json` is the tree the merge replayed from.
//
//  Stream `main`: genesis, a step, two branches (one agent-authored, with a prompt
//  and a recorded failure), the merge, a step after it. Stream `pruned`: four
//  linear records, the first two tombstoned. The pins below are the cut's
//  addresses for them; the old ones are the fixture's.
// ============================================================================

let private dir = Path.Combine(AppContext.BaseDirectory, "remint")
let private fixture (name: string) = Path.Combine(dir, name)
let private codec = OpJsonCodec.canonicalObj ()

let private initial () : Node<obj> =
    match Fuaran.UI.Ops.JsonDecode.decodeNodeObj (File.ReadAllText(fixture "initial.json")) with
    | Ok n -> n
    | Error e -> failtestf "initial.json: %A" e

let private options () =
    { RemintOptions.defaults<obj> with
        Initial = Some(initial ()) }

let private oldMainHead =
    "4052d5bed192eccd669078cb289697c7bc18d9c0909032cd337e24ce3bab1e20"

let private oldPrunedHead =
    "fc26e2a950655318e811e15851b88755a6f74d770428af028a4122b8d9524c21"

let private oldMerge =
    "a0fc32791701943f91491531b38744541383f9b1b78a82aba04b4f4daaffb80a"

/// The cut's addresses for the fixture: pinned, so a change to `Dag.nodeId`'s
/// pre-image, the envelope encoding or the merge script moves a line here.
let private newMainHead =
    "d4d50cfdd0f9ec69c3c5752a509679be655ae6170cbed3764df60c13f869a353"

let private newPrunedHead =
    "119de82df785ba047e7ecf62cad1df1898c9de80d761e4b6c27f29f38461e906"

let private newMerge =
    "6015b4f02cedcb193d42680a3ab2928f9cfd059cac7113f44640238ab229450c"

let private pruned =
    set
        [ "f1f324ef8522063f31ff3912fa09764fb4d81619397be51c3c19c443759cf5ac"
          "10b1dbe1e3379d2a76a203789ae7ab0023fae6c4dcff8f202877843b0e828400" ]

let private sha (path: string) =
    File.ReadAllBytes path |> SHA256.HashData |> Convert.ToHexString

let private copyStore (target: string) =
    File.Copy(fixture "pre-2645-store.db", target, true)

let private ok (r: Result<'a, StoreFault>) : 'a =
    match r with
    | Ok v -> v
    | Error f -> failtestf "refused: %s" (StoreFault.describe f)

let private wireLines () =
    File.ReadAllLines(fixture "pre-2645-records.jsonl") |> List.ofArray

let private legacyRecords () : LegacyRecord<obj> list =
    wireLines ()
    |> List.map (fun l ->
        match Legacy.decodeWire codec l with
        | Ok r -> r
        | Error e -> failtestf "fixture line: %s" e)

let private heads (receipt: RemintReceipt) =
    receipt.Streams |> List.collect (fun s -> s.Heads) |> Map.ofList

let private refusedWith (r: Result<'a, RemintFault>) : RemintFault =
    match r with
    | Error f -> f
    | Ok _ -> failtest "expected a refusal"

[<Tests>]
let tests =
    testList
        "Dag.Remint (Phase 2648)"
        [ test
              "the fixture is pre-cut: the cut's sink refuses it by name, and its addresses recompute under the pre-cut rule" {
              TempDb.withTempDb "remint-precut" (fun path ->
                  copyStore path

                  Expect.throws
                      (fun () ->
                          SqliteDagSink.create<obj> (sprintf "Data Source=%s;Pooling=False" path) codec
                          |> ignore)
                      "the cut's sink refuses a pre-cut database"

                  for r in legacyRecords () do
                      if not r.Tombstoned then
                          Expect.equal
                              (Legacy.address r)
                              r.Hash
                              "the pre-cut rule recomputes the fixture's own addresses")
          }

          test "a store minted before the cut re-mints in place: every node verifies under Dag.nodeId" {
              TempDb.withTempDb "remint-sqlite" (fun path ->
                  copyStore path
                  let receipt = SqliteRemint.remint (options ()) codec path |> ok

                  Expect.isFalse receipt.AlreadyCurrent "a pre-cut store is re-minted"

                  Expect.equal
                      (heads receipt)
                      (Map.ofList [ oldMainHead, newMainHead; oldPrunedHead, newPrunedHead ])
                      "old head -> new head, pinned"

                  Expect.equal
                      (Map.find oldMerge receipt.Addresses)
                      newMerge
                      "the outcome-hash merge is re-minted as a script node"

                  for p in pruned do
                      Expect.equal (Map.find p receipt.Addresses) p "a pruned record keeps its address"

                  Expect.isTrue (File.Exists(path + ".pre-2645")) "the original is kept beside it for rollback"

                  Expect.equal
                      (sha (path + ".pre-2645"))
                      (sha (fixture "pre-2645-store.db"))
                      "the backup is the original, byte for byte"

                  let sink =
                      SqliteDagSink.createWith<obj>
                          LoadVerification.Full
                          (sprintf "Data Source=%s;Pooling=False" path)
                          codec

                  let main = sink.Records "main" |> Async.RunSynchronously
                  let prunedLive = sink.Records "pruned" |> Async.RunSynchronously
                  Expect.equal (List.length main) 6 "every live record of main"
                  Expect.equal (List.length prunedLive) 2 "the two live records of pruned"

                  for r in main @ prunedLive do
                      Expect.equal (DagOpRecord.recomputeHash r) r.Hash "the node verifies under Dag.nodeId"

                  let skeleton = sink.Skeleton "pruned" |> Async.RunSynchronously

                  Expect.equal
                      (skeleton |> List.map (fun n -> n.Hash) |> Set.ofList)
                      pruned
                      "the tombstoned rows are the skeleton"

                  Expect.equal (sink.Head "main" |> Async.RunSynchronously) (Some newMainHead) "the head moved with it"

                  Expect.equal
                      (sink.Head "pruned" |> Async.RunSynchronously)
                      (Some newPrunedHead)
                      "the head moved with it"

                  let merge = main |> List.find (fun r -> r.Hash = newMerge)
                  Expect.equal (List.length merge.Parents) 2 "the merge keeps two parents"

                  match merge.Op with
                  | TreeOp.Batch _ -> ()
                  | other -> failtestf "a merge node's op is its script, a Batch: %A" other

                  Expect.equal
                      (merge.Actor, merge.Timestamp)
                      (Actor.Human "merge", DateTimeOffset.FromUnixTimeSeconds 1004L)
                      "attributed and timed as the record was"

                  // The re-minted merge reaches the tree the pre-cut node committed to.
                  let dag = DagOpRecord.toDag main

                  match Fuaran.UI.OpStream.Dag.Merge.DagFold.replayTo (initial ()) dag newMerge with
                  | Ok tree ->
                      let legacy = legacyRecords () |> List.find (fun r -> r.Hash = oldMerge)

                      Expect.equal
                          (Some(CanonicalJson.encodeNode tree |> Fuaran.Core.Hash.sha256Hex))
                          legacy.OutcomeHash
                          "Core's fold of the re-minted merge reaches the committed outcome"
                  | Error e -> failtestf "replay: %A" e)
          }

          test "the re-mint is idempotent: a second run is already current and writes nothing" {
              TempDb.withTempDb "remint-idempotent" (fun path ->
                  copyStore path
                  SqliteRemint.remint (options ()) codec path |> ok |> ignore
                  let before = sha path
                  let again = SqliteRemint.remint (options ()) codec path |> ok
                  Expect.isTrue again.AlreadyCurrent "the second run finds the cut's schema"

                  Expect.equal
                      (heads again)
                      (Map.ofList [ newMainHead, newMainHead; newPrunedHead, newPrunedHead ])
                      "heads unchanged"

                  Expect.equal (sha path) before "not one byte written")
          }

          test "the wire record set re-mints to the same addresses as the store, idempotently" {
              let lines, reminted = WireRemint.remint (options ()) codec (wireLines ()) |> ok

              TempDb.withTempDb "remint-wire" (fun path ->
                  copyStore path
                  let store = SqliteRemint.remint (options ()) codec path |> ok
                  Expect.equal reminted.Receipt.Addresses store.Addresses "one re-mint, two store forms")

              Expect.equal (List.length lines) 8 "the live records, in the cut's wire form"

              Expect.equal
                  (reminted.Skeleton |> List.map (fun n -> n.Hash) |> Set.ofList)
                  pruned
                  "the skeleton is reported"

              let again, second = WireRemint.remint (options ()) codec lines |> ok
              Expect.isTrue second.Receipt.AlreadyCurrent "a post-cut set is already current"
              Expect.equal again lines "and comes back unchanged"
          }

          testList
              "refusals — the store is untouched"
              [ test "a store whose pre-cut address does not recompute is refused, not laundered" {
                    TempDb.withTempDb "remint-broken" (fun path ->
                        copyStore path

                        do
                            use conn = new SqliteConnection(sprintf "Data Source=%s;Pooling=False" path)
                            conn.Open()
                            use cmd = conn.CreateCommand()

                            cmd.CommandText <-
                                "UPDATE dag_op_record SET prompt_id = 'forged' WHERE hash = '9ee22330c33e8c774b05d3ce2e47b425ca069e7b2e39da91748f9e88be7ad7fd';"

                            cmd.ExecuteNonQuery() |> ignore

                        let before = sha path

                        match SqliteRemint.remint (options ()) codec path with
                        | Error(StoreFault.Remint(RemintFault.LegacyAddressBroken(_, stored, _))) ->
                            Expect.equal
                                stored
                                "9ee22330c33e8c774b05d3ce2e47b425ca069e7b2e39da91748f9e88be7ad7fd"
                                "names the record"
                        | other -> failtestf "expected LegacyAddressBroken: %A" other

                        Expect.equal (sha path) before "the store is untouched"
                        Expect.isFalse (File.Exists(path + ".pre-2645")) "no backup: nothing was replaced")
                }

                test "a merge with no initial tree to replay from is refused by name" {
                    match Remint.records RemintOptions.defaults Map.empty (legacyRecords ()) with
                    | Error(RemintFault.MergeNeedsInitial(_, h)) -> Expect.equal h oldMerge "names the merge"
                    | other -> failtestf "expected MergeNeedsInitial: %A" other
                }

                test "a merge whose re-run reaches another tree is refused, not re-minted to a different state" {
                    let other =
                        match
                            Fuaran.UI.Ops.JsonDecode.decodeNodeObj (
                                File.ReadAllText(fixture "initial.json").Replace("Pane 0", "Pane zero")
                            )
                        with
                        | Ok n -> n
                        | Error e -> failtestf "%A" e

                    match
                        Remint.records
                            { RemintOptions.defaults with
                                Initial = Some other }
                            Map.empty
                            (legacyRecords ())
                    with
                    | Error(RemintFault.MergeOutcomeMoved(_, h, committed, reached)) ->
                        Expect.equal h oldMerge "names the merge"
                        Expect.notEqual committed reached "the two trees"
                    | other -> failtestf "expected MergeOutcomeMoved: %A" other
                }

                test "a record naming a parent the set does not hold is refused" {
                    let set =
                        legacyRecords ()
                        |> List.filter (fun r ->
                            r.Hash <> "6e6e9aae633363f22098228005c506e9a1e4646c98a1eaf80e33351adc6b59b8")

                    match refusedWith (Remint.records (options ()) Map.empty set) with
                    | RemintFault.UnknownParent(_, _, p) ->
                        Expect.equal
                            p
                            "6e6e9aae633363f22098228005c506e9a1e4646c98a1eaf80e33351adc6b59b8"
                            "names the parent"
                    | other -> failtestf "expected UnknownParent: %A" other
                }

                test "a wire set mixing pre-cut and post-cut records is refused" {
                    let current, _ = WireRemint.remint (options ()) codec (wireLines ()) |> ok

                    match WireRemint.remint (options ()) codec (List.head (wireLines ()) :: current) with
                    | Error(StoreFault.MixedGeneration _) -> ()
                    | other -> failtestf "expected MixedGeneration: %A" other
                } ] ]
