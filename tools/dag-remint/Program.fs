module Fuaran.UI.OpStream.Dag.Remint.Program

open System
open System.IO
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Merge

// ============================================================================
//  dag-remint — the one-release re-mint of a DAG store written before
//  Phase 2645 (see docs/migrations/2648-core-dag-identity.md).
//
//      dag-remint sqlite <db> [--initial <node.json>] [--precedence a|b] [--receipt <file>]
//      dag-remint wire <in.jsonl> <out.jsonl> [--initial <node.json>] [--precedence a|b] [--receipt <file>]
//
//  Ops are decoded through the tier's canonical codec (`OpJsonCodec.canonicalObj`):
//  the address is over bytes, and every pre-cut address is recomputed before
//  anything moves, so an op the codec does not round-trip is refused by name
//  rather than re-minted to a different address. A host whose ops carry its own
//  `'Msg` calls `SqliteRemint.remint` / `WireRemint.remint` with its own codec.
//
//  Exit 0: re-minted, or already current (the receipt says which). Exit 1: refused
//  (the store is untouched). Exit 2: usage.
// ============================================================================

let private usage () =
    eprintfn "usage: dag-remint sqlite <db> [--initial <node.json>] [--precedence a|b] [--receipt <file>]"

    eprintfn
        "       dag-remint wire <in.jsonl> <out.jsonl> [--initial <node.json>] [--precedence a|b] [--receipt <file>]"

    2

let rec private flags (acc: Map<string, string>) (args: string list) : Result<Map<string, string>, string> =
    match args with
    | [] -> Ok acc
    | ("--initial" | "--precedence" | "--receipt" as k) :: v :: rest -> flags (Map.add k v acc) rest
    | other :: _ -> Error(sprintf "unknown argument '%s'" other)

let private optionsOf (flagMap: Map<string, string>) : Result<RemintOptions<obj>, string> =
    let initial =
        match Map.tryFind "--initial" flagMap with
        | None -> Ok None
        | Some path ->
            match Fuaran.UI.Ops.JsonDecode.decodeNodeObj (File.ReadAllText path) with
            | Ok node -> Ok(Some node)
            | Error e -> Error(sprintf "--initial %s: %s" path (Fuaran.UI.Ops.JsonDecode.DecodeError.render e))

    let precedence =
        match Map.tryFind "--precedence" flagMap |> Option.map (fun s -> s.ToLowerInvariant()) with
        | None -> Ok None
        | Some "a" -> Ok(Some ReconcileSide.A)
        | Some "b" -> Ok(Some ReconcileSide.B)
        | Some other -> Error(sprintf "--precedence takes a or b, not '%s'" other)

    match initial, precedence with
    | Ok i, Ok p -> Ok { Initial = i; Precedence = p }
    | Error e, _
    | _, Error e -> Error e

let private report (flagMap: Map<string, string>) (receipt: RemintReceipt) =
    let json = RemintReceipt.encode receipt

    match Map.tryFind "--receipt" flagMap with
    | Some path -> File.WriteAllText(path, json)
    | None -> ()

    printfn "%s" json

    for s in receipt.Streams do
        for (o, n) in s.Heads do
            eprintfn "%s: %s -> %s (%d nodes, %d merges, %d pruned)" s.StreamId o n s.Nodes s.Merges s.Skeleton

    if receipt.AlreadyCurrent then
        eprintfn "already current: nothing was written"

[<EntryPoint>]
let main argv =
    let codec = OpJsonCodec.canonicalObj ()

    let run (rest: string list) (act: Map<string, string> -> RemintOptions<obj> -> Result<RemintReceipt, StoreFault>) =
        match flags Map.empty rest with
        | Error e ->
            eprintfn "%s" e
            usage ()
        | Ok flagMap ->
            match optionsOf flagMap with
            | Error e ->
                eprintfn "%s" e
                2
            | Ok options ->
                match act flagMap options with
                | Ok receipt ->
                    report flagMap receipt
                    0
                | Error fault ->
                    eprintfn "dag-remint: refused: %s" (StoreFault.describe fault)
                    1

    match List.ofArray argv with
    | "sqlite" :: db :: rest -> run rest (fun _ options -> SqliteRemint.remint options codec db)
    | "wire" :: input :: output :: rest ->
        run rest (fun _ options ->
            WireRemint.remint options codec (File.ReadAllLines input |> List.ofArray)
            |> Result.map (fun (lines, reminted) ->
                if not reminted.Receipt.AlreadyCurrent || input <> output then
                    File.WriteAllLines(output, lines)

                reminted.Receipt))
    | _ -> usage ()
