module Samples.GettingStarted.Lesson03Replay

// ============================================================================
//  LESSON 3 — A session is a hash-chained list of ops, and it replays exactly.
//
//  Lesson 2 made an edit addressable. This makes a SESSION reproducible: keep
//  the ops, in order, each carrying the hash of the one before it, and the tree
//  at any point is a fold over a prefix. Three things follow, and each is worth
//  more than it first looks:
//
//    * EXACT REPLAY. Not "renders the same" — the same tree, byte-for-byte
//      under the canonical encoder. That is what makes a bug report reproducible
//      and an audit meaningful.
//    * TIME TRAVEL FOR FREE. Any prefix is a real state, so "what did this look
//      like three edits ago" needs no snapshot machinery.
//    * TAMPER EVIDENCE. Each record's hash covers the op, its position, the
//      actor, the timestamp and the apply outcome. Change any of them after the
//      fact and verification names the first record that broke.
//
//  Note the actor. Every record says whether a human or a model made the edit,
//  and it is inside the hash, so provenance cannot be edited off afterwards.
// ============================================================================

open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Replay

module Canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson

/// Link one op into the chain: the record's hash covers everything about it
/// except which stream it belongs to, so a reordered or edited record fails
/// verification rather than passing quietly.
let private link (streamId: string) (actor: Actor) (previous: OpRecord<obj> option) (op: TreeOp<obj>) =
    let sequence =
        match previous with
        | Some p -> p.Sequence + 1
        | None -> 1

    let previousHash =
        match previous with
        | Some p -> p.Hash
        | None -> HashChain.genesisPreviousHash

    // A fixed timestamp keeps this sample's output stable run to run; a real
    // host passes the clock. The timestamp is inside the hash either way.
    let timestamp = System.DateTimeOffset(2026, 1, 1, 12, 0, 0, System.TimeSpan.Zero)

    { StreamId = streamId
      Sequence = sequence
      PreviousHash = previousHash
      Hash = HashChain.computeHash previousHash op sequence timestamp actor None OpResultEnvelope.Success
      Op = op
      PromptId = None
      Actor = actor
      Timestamp = timestamp
      ResultEnvelope = OpResultEnvelope.Success }

let private chainOf (streamId: string) (steps: (Actor * TreeOp<obj>) list) : OpRecord<obj> list =
    steps
    |> List.fold
        (fun (acc: OpRecord<obj> list) (actor, op) ->
            let previous = List.tryLast acc
            acc @ [ link streamId actor previous op ])
        []

let run () =
    let seed = Lesson01Authoring.dashboard
    let human = Actor.Human "ada"
    let model = Actor.Agent("some-model", "1", "assistant")

    // Two edits: one a person made, one a model made on their behalf.
    let records =
        chainOf
            "getting-started"
            [ human, Lesson02EditByOps.renameRevenue
              model, Lesson02EditByOps.warnOnRevenue ]

    match Replay.applyTo seed records with
    | Error e -> printfn "replay failed: %A" e
    | Ok replayed ->
        printfn "Replayed %d records. Chain:" records.Length

        for r in records do
            printfn "  %d  %-28s  %s…" r.Sequence (Actor.id r.Actor) (r.Hash.Substring(0, 12))

        // Replay is a pure function of (seed, records), so a second run of the
        // same input is the same output. This is the property the whole
        // provenance story rests on.
        match Replay.applyTo seed records with
        | Ok again ->
            printfn ""
            printfn "Replayed twice, byte-identical: %b" (Canon.encodeNode replayed = Canon.encodeNode again)
        | Error _ -> ()

        // Any PREFIX is a real state — time travel with no snapshot machinery.
        match Replay.applyTo seed (List.truncate 1 records) with
        | Ok oneStepBack ->
            printfn
                "State after 1 of 2 ops differs from the final state: %b"
                (Canon.encodeNode oneStepBack <> Canon.encodeNode replayed)
        | Error _ -> ()

    // Tamper with a record's op AFTER it was hashed and the chain no longer
    // verifies. Nothing is applied — the replay refuses the whole segment
    // rather than applying the good prefix and stopping.
    let tampered =
        records
        |> List.mapi (fun i r ->
            if i = 1 then
                { r with
                    Op = TreeOp.UpdateProp(NodeId "sales-revenue", "Tone", PropValue.Wire(Fuaran.Core.JStr "Critical")) }
            else
                r)

    match Replay.applyTo seed tampered with
    | Ok _ -> printfn "the tampered chain unexpectedly replayed"
    | Error e ->
        printfn ""
        printfn "A record edited after the fact breaks the chain:"
        printfn "  %A" e
