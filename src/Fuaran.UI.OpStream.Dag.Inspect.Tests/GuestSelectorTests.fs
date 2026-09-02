module Fuaran.UI.OpStream.Dag.Inspect.Tests.GuestSelectorTests

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.Inspect
open Fuaran.UI.OpStream.Dag.Inspect.Tests.InspectCorpus

// ============================================================================
//  DagGuestSelector — the guest selector over the inspector (Phase 270, §4o).
//
//  Corpus: a host stream `s` with a genesis + one step, and two mounted
//  guests forked off the host genesis per the guest-fork anchor contract —
//  guest `regionA` with two ops, guest `regionB` with one. The selector must
//  enumerate exactly the two guests from the stream keys, and a guest-scoped
//  graph must hold that guest's records only (the phase acceptance: guest A's
//  stream renders without guest B's records).
// ============================================================================

let private restyle (id: string) (tone: ToneVariant) : TreeOp<TestMsg> =
    TreeOp.UpdateStyle(NodeId id, { Defaults.style with Tone = tone })

/// Host genesis + host step + guest branches for `regionA` (2 ops) and
/// `regionB` (1 op), both anchored on the host genesis (the Mount op stand-in).
let private buildForest () =
    let hostGenesis =
        DagOpRecord.create
            "s"
            []
            (restyle "left" ToneVariant.Brand)
            None
            (Actor.Human "human")
            (ts 1L)
            OpResultEnvelope.Success

    let hostStep =
        DagOpRecord.create
            "s"
            [ hostGenesis.Hash ]
            (restyle "right" ToneVariant.Success)
            None
            (Actor.Human "human")
            (ts 2L)
            OpResultEnvelope.Success

    let guestA1 =
        GuestFork.genesis
            "regionA"
            hostGenesis.Hash
            (restyle "left" ToneVariant.Critical)
            (Some "prompt-a")
            (Actor.Agent("claude", "4.8", "planner"))
            (ts 3L)
            OpResultEnvelope.Success

    let guestA2 =
        GuestFork.step
            "regionA"
            guestA1.Hash
            (restyle "right" ToneVariant.Brand)
            (Some "prompt-a")
            (Actor.Agent("claude", "4.8", "planner"))
            (ts 4L)
            OpResultEnvelope.Success

    let guestB1 =
        GuestFork.genesis
            "regionB"
            hostGenesis.Hash
            (restyle "left" ToneVariant.Success)
            (Some "prompt-b")
            (Actor.Agent("claude", "4.8", "planner"))
            (ts 5L)
            OpResultEnvelope.Success

    hostGenesis, hostStep, guestA1, guestA2, guestB1

[<Tests>]
let guestSelectorTests =
    testList
        "Phase270.DagGuestSelector"
        [ test "guests are enumerated from the guest stream keys, sorted and distinct" {
              let hostGenesis, hostStep, guestA1, guestA2, guestB1 = buildForest ()
              let records = [ hostGenesis; hostStep; guestA1; guestA2; guestB1 ]

              Expect.equal
                  (DagGuestSelector.guests records)
                  [ "regionA"; "regionB" ]
                  "both guests surface from their stream keys; the host stream contributes none"

              Expect.isEmpty
                  (DagGuestSelector.guests [ hostGenesis; hostStep ])
                  "a forest with no guest streams enumerates no guests"
          }

          test "a guest-scoped graph holds that guest's records only (no sibling, no host)" {
              let hostGenesis, hostStep, guestA1, guestA2, guestB1 = buildForest ()
              let records = [ hostGenesis; hostStep; guestA1; guestA2; guestB1 ]

              let graph = DagGuestSelector.graphFor (DagGuestSelection.Guest "regionA") records

              Expect.equal graph.StreamId "guest-regionA" "the graph is labelled with the guest's stream key"

              Expect.equal
                  (graph.Nodes |> List.map _.Hash |> List.sort)
                  ([ guestA1.Hash; guestA2.Hash ] |> List.sort)
                  "guest A's branch renders without guest B's or the host's records"

              // The guest genesis is anchored on a host-stream op that the guest
              // view deliberately excludes — the documented missing-parent shape.
              let genesisNode = graph.Nodes |> List.find (fun n -> n.Hash = guestA1.Hash)
              Expect.equal genesisNode.Depth 0 "the anchored genesis sits at depth 0 in the guest-scoped view"
              Expect.equal graph.Leaves [ guestA2.Hash ] "the guest's head is the sole leaf"
          }

          test "the host view excludes every guest stream; the rollup includes everything" {
              let hostGenesis, hostStep, guestA1, guestA2, guestB1 = buildForest ()
              let records = [ hostGenesis; hostStep; guestA1; guestA2; guestB1 ]

              let host = DagGuestSelector.graphFor DagGuestSelection.Host records

              Expect.equal host.StreamId "s" "a single-stream host slice is labelled with its own stream id"

              Expect.equal
                  (host.Nodes |> List.map _.Hash |> List.sort)
                  ([ hostGenesis.Hash; hostStep.Hash ] |> List.sort)
                  "the host view holds no guest records"

              let rollup = DagGuestSelector.graphFor DagGuestSelection.Rollup records
              Expect.equal rollup.StreamId "rollup" "the aggregate is labelled rollup"
              Expect.hasLength rollup.Nodes 5 "the opt-in rollup renders the whole forest at once"
          } ]
