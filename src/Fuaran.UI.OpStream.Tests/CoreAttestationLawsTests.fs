module Fuaran.UI.OpStream.Tests.CoreAttestationLawsTests

// Phase 1480 — the attributed and attestation law families over the tier's op-stream, and the
// tier's own account of the attribution claim across its two durable ports.
//
// Three families, and the third is the one that matters commercially. `attributedLaws` certifies
// that provenance rides INSIDE the chained op encoding, so re-attribution is tamper-evident on the
// same footing as op-tampering. `attestationLaws` certifies the signing seam end to end, including
// the case a bare hash chain cannot defend: a forgery that was re-hashed consistently, so
// `verifyChain` re-accepts it. `noAttestationVacuityLaws` certifies that the un-attested default
// verifies NOTHING — an attestation mechanism that cannot fail proves nothing, and a verifier that
// answers `true` for the no-op sink would make every green attestation row above meaningless.
//
// ── WHAT EACH FAMILY IS BOUND TO, STATED BEFORE THE FIRST LAW CALL ────────────────────────────
//
// All three are parameterised (a `StreamWitness`, and for one of them an `IAttestationSink`), so
// each is a statement about THIS tier rather than a re-run of Core's own suite — provided the
// parameters are the tier's real ones. They are:
//
//   * the witness is `PersistenceLawsTests.eqOpSw`, built by fuaran#1477 over the tier's real apply
//     engine (`Ops.Apply.apply`) and its real canonical-JSON op codec. It is REUSED rather than
//     rebuilt: two witnesses over one op algebra could disagree, and then a green law would be a
//     statement about whichever of them the reader happened to open;
//   * the digest is the tier's shipped SHA-256 `StreamEntry.hashFn`, not Core's FNV-1a default —
//     which is what makes the `attestationLaws` falsification branches meaningful, since a
//     re-hashed forgery is cheap under FNV-1a and infeasible under SHA-256;
//   * the sink is `tierClaimSink` below, which is NOT a test double. It mints the tier's real
//     canonical claim payload (`SegmentAttestation.claimPayload`), signs it with the tier's real
//     BCL ECDSA P-256 signer (`EcdsaP256.signer`), and verifies through the tier's real crypto
//     verifier (`EcdsaP256.verifier`) against the tier's real key directory
//     (`KeyDirectory.ofList`). A defect in the claim encoding, the signer or the verifier turns
//     `attestationLaws` red.
//
// ── THE ONE SHAPE MISMATCH, AND HOW IT IS HANDLED HONESTLY ────────────────────────────────────
//
// Core's `IAttestationSink` signs an OPAQUE STRING — a chain head — while the tier's attestation
// vocabulary signs a SEGMENT CLAIM: a descriptor binding algorithm, chain-format version, stream
// id, sequence range and anchor, plus the head. The tier already resolves this in the signing
// direction (`AttestationSigner.ofCoreSink` passes the claim payload to Core's seam rather than a
// bare head); `tierClaimSink` is the same resolution in the other direction. The descriptor it
// binds is a fixed shell for one synthetic stream, so the only field the law varies is `Head` —
// which is exactly the field the falsification branches move. That is stated rather than hidden:
// this adoption certifies the head binding and the signature over the tier's canonical claim, and
// it does NOT certify the range or anchor fields, which have no counterpart in Core's seam and are
// covered directly by `AttestationTests.fs` (`RangeMismatch`, `ChainBroken` off a signed anchor).
//
// `AttestationTests.fs` already covers the tier-shaped attestation claims — clean sign→verify, a
// tampered signature, a re-chained store, a stripped attestation, revocation and adoption tiers —
// so nothing here re-states them. What is added tier-shaped is the ATTRIBUTION claim, which had no
// durable-port coverage: that an entry cannot exist without an actor (a structural fact about the
// record type, not a convention), and that the actor survives the round trip through both stores
// and the by-actor fold over what comes back.

open System
open System.Security.Cryptography
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Tests.PersistenceLawsTests

module CoreConf = Fuaran.Core.Conformance
module CoreStream = Fuaran.Core.OpStream

// ---------------------------------------------------------------------------
//  the tier's claim minting, adapted to Core's IAttestationSink
// ---------------------------------------------------------------------------

/// A fixed clock. Core's seam has no timestamp slot, so `SignedAt` must be reproducible from the
/// head alone for `Verify` to rebuild the same claim payload the signature covers — which is also
/// the tier's discipline that a host supplies the clock rather than the library reading one.
let private signedAt = DateTimeOffset.FromUnixTimeSeconds 1_756_944_000L

/// The claim shell every attestation in this file is taken over. Only `Head` varies — see the file
/// header for why, and for what that does and does not certify.
let private descriptorFor (head: string) : SegmentDescriptor =
    { Algorithm = AttestationAlgorithm.ecdsaP256Sha256V1
      ChainFormatVersion = StreamEntry.chainFormatVersion
      StreamId = "conformance-attested"
      FromSeq = 1
      ToSeq = 1
      PreviousHash = HashChain.genesisPreviousHash
      Head = head }

/// Adapt the tier's real claim minting + real ECDSA verification + real key directory to Core's
/// `IAttestationSink`, so `Conformance.attestationLaws` drives the tier's crypto path rather than a
/// stand-in.
///
/// `Verify` deliberately re-derives the claim from the head Core PASSES IT, never from the head the
/// attestation carries. That is the whole mechanism: a forged chain has a different head, so the
/// rebuilt payload differs from the signed one and the signature fails. A sink that trusted
/// `attestation.Head` would verify every forgery — which is one of the three go-red perturbations
/// recorded in this phase's outcome.
let private tierClaimSink (keyId: string) (key: ECDsa) : Fuaran.Core.IAttestationSink =
    let signer = EcdsaP256.signer (fun () -> signedAt) keyId key
    let directory = KeyDirectory.ofList [ EcdsaP256.keyEntry keyId key ]

    { new Fuaran.Core.IAttestationSink with
        member _.Sign(head: string) : Fuaran.Core.Attestation option =
            signer.SignSegment(descriptorFor head)
            |> Async.RunSynchronously
            |> Option.map (fun (a: SegmentAttestation) ->
                { Head = head
                  KeyId = a.KeyId
                  Signature = a.Signature })

        member _.Verify (attestation: Fuaran.Core.Attestation) (head: string) : bool =
            let claim: SegmentAttestation =
                { Descriptor = descriptorFor head
                  KeyId = attestation.KeyId
                  SignedAt = signedAt
                  Adopted = false
                  Signature = attestation.Signature }

            match directory.ResolveKey attestation.KeyId |> Async.RunSynchronously with
            | None -> false
            | Some entry -> EcdsaP256.verifier.VerifySignature claim entry |> Async.RunSynchronously }

// ---------------------------------------------------------------------------
//  the tier's own attribution claim, over both durable ports
// ---------------------------------------------------------------------------

/// A record at an explicit actor. `PersistenceLawsTests.buildObjRecord` pins `Human "tester"`, and
/// the actor is precisely what varies here, so this takes it as a parameter rather than being a
/// second copy of that helper. The hash authority is the same single one (`HashChain.computeHash`),
/// which folds the actor into the digest (Phase 320).
let private recordAt
    (streamId: string)
    (sequence: int)
    (actor: Actor)
    (op: TreeOp<obj>)
    (previous: OpRecord<obj> option)
    : OpRecord<obj> =
    let previousHash =
        match previous with
        | None -> HashChain.genesisPreviousHash
        | Some prev -> prev.Hash

    let timestamp = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L + int64 sequence)

    let hash =
        HashChain.computeHash previousHash op sequence timestamp actor None OpResultEnvelope.Success

    { StreamId = streamId
      Sequence = sequence
      PreviousHash = previousHash
      Hash = hash
      Op = op
      PromptId = None
      Actor = actor
      Timestamp = timestamp
      ResultEnvelope = OpResultEnvelope.Success }

/// Four appends by three distinct authors, one of them an agent — enough that a by-actor fold has
/// something to partition and that within-actor stream order is observable.
let private authored: (Actor * TreeOp<obj>) list =
    [ Actor.Human "ada", TreeOp.RemoveNode(NodeId "a")
      Actor.Agent("claude", "opus-5", "agent-1"), TreeOp.RemoveNode(NodeId "b")
      Actor.Human "grace", TreeOp.RemoveNode(NodeId "c")
      Actor.Human "ada", TreeOp.RemoveNode(NodeId "d") ]

// ---------------------------------------------------------------------------
//  the tests
// ---------------------------------------------------------------------------

[<Tests>]
let tests =
    testList
        "Fuaran.UI.OpStream — attributed + attestation laws (Core conformance)"
        [

          testCase "attributedLaws certifies over the Fuaran.UI op-stream witness"
          <| fun _ ->
              // fuaran-core#81: `Attributed.liftWitness` wraps the tier's op codec in an attribution
              // envelope, so the EXISTING chain covers "who did what" with no new witness field.
              // Three claims, all bound to the tier: the lifted stream replays to exactly the state
              // the bare ops replay to (attribution is provenance, never state); re-attributing a
              // chained op breaks `verifyChain` under the tier's SHA-256 digest; and the attributed
              // chain survives `toJsonl` → `fromJsonl` byte-for-byte, which runs the tier's real
              // `CanonicalJson.encodeOp` / `JsonDecode.decodeOp` pair through the persistence path.
              CoreConf.attributedLaws eqOpSw eqOpStreamGen hashFn 20260904 100
              |> assertAllPassed "attributedLaws over the Fuaran.UI apply/codec witness"

          testCase "attestationLaws certifies over the tier's claim minting and ECDSA keyring"
          <| fun _ ->
              // The sink is the tier's real claim payload + BCL P-256 signer + crypto verifier +
              // key directory (see `tierClaimSink`). The two falsification branches are what a bare
              // hash chain cannot do: each forges a record AND re-hashes the whole chain under the
              // same `HashFn`, so `verifyChain` re-accepts the forgery — and the signature, which
              // covers only the original head, must still reject it. One branch tampers with an op;
              // the other only RE-ATTRIBUTES a record, which moves the head solely because the
              // actor is inside the digest (Phase 320).
              use key = ECDsa.Create ECCurve.NamedCurves.nistP256

              CoreConf.attestationLaws eqOpSw eqOpStreamGen (tierClaimSink "conformance-key" key) hashFn 20260904 100
              |> assertAllPassed "attestationLaws over the tier's SegmentAttestation claim path"

          testCase "noAttestationVacuityLaws certifies that the un-attested default proves nothing"
          <| fun _ ->
              // The commercially load-bearing one, and the reason it is not merely a re-run of
              // Core's suite: it is parameterised over the tier's witness and digest, so it asserts
              // three things about THIS tier's streams. That `noAttestation` issues nothing; that
              // it verifies nothing — including a well-formed attestation over the real head, so a
              // host that forgot to plug a sink in gets a refusal rather than a free `true`; and
              // that attesting is a read-only side-band, leaving the chain's head and its
              // `verifyChain` verdict untouched. The third is what makes adoption free: an
              // un-attested store is honestly un-attested, never subtly different.
              CoreConf.noAttestationVacuityLaws eqOpSw eqOpStreamGen hashFn 20260904 100
              |> assertAllPassed "noAttestationVacuityLaws over the Fuaran.UI apply/codec witness"

          testCase "an unattributed entry is unrepresentable — Actor is a required field of a closed union"
          <| fun _ ->
              // "An unattributed append is refused as data", checked as a fact about the TYPE
              // rather than as a convention a caller could forget. `OpRecord.Actor` is `Actor`, not
              // `Actor option` and not a string, and `Actor` is a closed union of exactly Human and
              // Agent — so there is no value of the record with no author, and no anonymous case to
              // reach for. Adding an `Anonymous` case, or relaxing the field to an option, turns
              // this red at the moment the change is made rather than when something downstream
              // silently reads an empty attribution.
              let field =
                  typeof<OpRecord<obj>>.GetProperty "Actor"
                  |> Option.ofObj
                  |> Option.defaultWith (fun () -> failtest "OpRecord no longer carries an Actor property")

              Expect.equal
                  field.PropertyType
                  typeof<Actor>
                  "OpRecord.Actor is the typed Actor, not an option and not a string"

              let cases =
                  Reflection.FSharpType.GetUnionCases typeof<Actor>
                  |> Array.map _.Name
                  |> Array.sort

              Expect.equal
                  cases
                  [| "Agent"; "Human" |]
                  "Actor is closed over Human and Agent — there is no unattributed case"

          testCase "attribution survives the durable round-trip and the by-actor fold on both stores"
          <| fun _ ->
              // The tier-shaped half of the attribution claim, which had no port coverage: the
              // actor is not merely hashed, it is PERSISTED and comes back intact through each
              // store's own record encoding — including the Agent case's model/version, which a
              // store that flattened the actor to its id would lose while still round-tripping the
              // "author". Then Core's `Attributed.bySession`-shaped fold is applied to what came
              // back (grouped by `Actor.id`), so the partition is over replayed records rather than
              // over the ones still in hand.
              overBothStores (fun storeName sink ->
                  let streamId = "attributed-" + storeName
                  let baseSink = sink :> IOpStreamSink<obj>

                  let appended =
                      (([], None), authored |> List.indexed)
                      ||> List.fold (fun (acc, previous) (i, (actor, op)) ->
                          let r = recordAt streamId (i + 1) actor op previous
                          baseSink.Append r |> Async.RunSynchronously
                          acc @ [ r ], Some r)
                      |> fst

                  let replayed = baseSink.Replay(streamId, 1, 100) |> Async.RunSynchronously

                  Expect.equal
                      (replayed |> List.map _.Actor)
                      (appended |> List.map _.Actor)
                      (sprintf "%s: every replayed entry carries the actor it was appended under" storeName)

                  Expect.equal
                      (replayed |> List.map (fun r -> Actor.encode r.Actor))
                      (appended |> List.map (fun r -> Actor.encode r.Actor))
                      (sprintf "%s: the canonical actor bytes survive the store's record encoding" storeName)

                  // The round trip preserved enough to re-derive the chain: had the actor changed,
                  // the recomputed digest would move, because attribution is inside it.
                  Expect.isOk
                      (Verify.chain replayed |> Result.mapError (sprintf "%A"))
                      (sprintf "%s: the replayed chain still verifies under its persisted attribution" storeName)

                  let byAuthor =
                      replayed
                      |> List.groupBy (fun r -> Actor.id r.Actor)
                      |> List.map (fun (author, rs) -> author, rs |> List.map _.Sequence)
                      |> List.sortBy fst

                  Expect.equal
                      byAuthor
                      [ "ada", [ 1; 4 ]; "agent-1", [ 2 ]; "grace", [ 3 ] ]
                      (sprintf "%s: the by-actor fold partitions the replayed stream in stream order" storeName)) ]
