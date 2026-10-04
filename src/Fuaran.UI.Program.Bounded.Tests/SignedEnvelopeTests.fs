module Fuaran.UI.Program.Bounded.Tests.SignedEnvelopeTests

// ─── The signed effect envelope — verifiable by recomputation ────────
//
// `SignedEnvelope.sign` signs the pair (canonical tree hash, demanded document
// bytes) through a host-supplied sink; `SignedEnvelope.verify` recomputes both
// from the tree, compares, and checks the signature over the RECOMPUTED pair
// under a public key. These tests pin four things:
//
//  1. THE ROUND TRIP — sign, encode, decode, verify: the verified projection
//     is the walk's own, and the carried document reads back through
//     `Demanded.decode` unchanged, so a consumer ignoring the signature reads
//     what it read before.
//  2. THE TAMPER CASES, EACH WITH ITS OWN REASON — a tree that gained an
//     effect is drift with the EXCESS named; an envelope that claims what the
//     tree does not is drift with the SHORTFALL named; an envelope that is not
//     a document is drift with the whole recomputation as excess; a tree that
//     moved without moving a demand is a bad signature with the hashes
//     disagreeing; forged bytes are a bad signature with the hashes agreeing;
//     another party's key is a foreign key.
//  3. THAT NO KEY IS A REFUSAL — not a skip, and not a drift report either.
//  4. THAT `None` AND EMPTY SURVIVE — the server tier's two facts round-trip as
//     two facts, and each direction of collapse is reported as drift.
//
// The crypto is the UI tier's own ECDSA P-256 claim verifier, driven by a sink
// this file builds over a BCL key pair — the shape a host fills, exercised for
// real rather than stubbed, so a signature that verifies here verifies under the
// same key entry anywhere.

open System
open System.Security.Cryptography
open System.Text
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.Program.Bounded
open Fuaran.UI.Program

// ─── fixtures ───────────────────────────────────────────────────────

/// A signing party: a P-256 key pair behind the substrate's attestation sink,
/// and the PUBLIC half as the directory entry a verifier is offered.
type private Party =
    { Entry: KeyDirectoryEntry
      Sink: Fuaran.Core.IAttestationSink }

let private party (keyId: string) : Party =
    let key = ECDsa.Create(ECCurve.NamedCurves.nistP256)

    let sign (head: string) : string =
        Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes head, HashAlgorithmName.SHA256))

    let sink =
        { new Fuaran.Core.IAttestationSink with
            member _.Sign head =
                Some(
                    { Head = head
                      KeyId = keyId
                      Signature = sign head }
                    : Fuaran.Core.Attestation
                )

            member _.Verify attestation head =
                attestation.Head = head
                && key.VerifyData(
                    Encoding.UTF8.GetBytes head,
                    Convert.FromBase64String attestation.Signature,
                    HashAlgorithmName.SHA256
                ) }

    { Entry = EcdsaP256.keyEntry keyId key
      Sink = sink }

/// The sink that signs nothing — the named unattested posture.
let private declining: Fuaran.Core.IAttestationSink =
    { new Fuaran.Core.IAttestationSink with
        member _.Sign _ = None
        member _.Verify _ _ = false }

let private btn (id: string) (action: Action<obj>) : Node<obj> =
    Fuaran.button
        id
        { Defaults.button<obj> with
            OnClick = action }

let private dash (children: Node<obj> list) : Node<obj> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<obj> with
            Children = children }

let private navigate: Action<obj> =
    Action.Navigate(TextSource.Literal "/next", NavigateTarget.Self)

/// A tree demanding one effect, with a static caption beside it.
let private tree: Node<obj> =
    dash [ Fuaran.markdown "m" "static"; btn "go" navigate ]

/// The tier a server walk reports for a tree that names no handler.
let private emptyTier: ServerDemand =
    { Effects = []
      Capabilities = []
      Functions = []
      Channels = []
      Reach = []
      Replay = []
      Undo = []
      Constraints = [] }

/// A server-placement walk over a tree naming no handler: the client tier plus
/// an EMPTY server tier — "walked, found nothing".
let private serverWalk (root: Node<obj>) : DemandedProjection =
    Demanded.ofTree root |> Demanded.withServer emptyTier

let private signedBy (p: Party) : SignedEnvelope =
    match SignedEnvelope.sign p.Sink Demanded.ofTree tree with
    | Ok signed -> signed
    | Error refusal -> failtest $"sign refused: %A{refusal}"

let private verifyWith
    (p: Party option)
    (project: Node<obj> -> DemandedProjection)
    (root: Node<obj>)
    (signed: SignedEnvelope)
    : Result<VerifiedEnvelope, VerifyRefusal> =
    SignedEnvelope.verify EcdsaP256.claimVerifier (p |> Option.map _.Entry) project root signed
    |> Async.RunSynchronously

let private refusalOf (result: Result<VerifiedEnvelope, VerifyRefusal>) : VerifyRefusal =
    match result with
    | Error refusal -> refusal
    | Ok verified -> failtest $"expected a refusal, verified %A{verified}"

let private driftOf (result: Result<VerifiedEnvelope, VerifyRefusal>) : EnvelopeDrift =
    match refusalOf result with
    | VerifyRefusal.EnvelopeDrift drift -> drift
    | other -> failtest $"expected drift, got %A{other}"

// ─── tests ──────────────────────────────────────────────────────────

[<Tests>]
let tests =
    let alice = party "alice"
    let mallory = party "mallory"

    testList
        "Phase 1744 — the signed effect envelope"
        [

          // ── the round trip ────────────────────────────────────────

          test "sign, encode, decode, verify: the verified projection is the walk's own" {
              let signed = signedBy alice
              let wire = SignedEnvelope.encode signed

              let back =
                  match SignedEnvelope.decode wire with
                  | Ok r -> r
                  | Error e -> failtest $"decode refused its own bytes: %A{e}"

              Expect.equal back signed "the record reads back as the value that produced it"
              Expect.equal (SignedEnvelope.encode back) wire "…and re-encodes to the same bytes"

              match verifyWith (Some alice) Demanded.ofTree tree back with
              | Ok verified ->
                  Expect.equal verified.Projection (Demanded.ofTree tree) "recomputed from the tree"
                  Expect.equal verified.TreeHash (SignedEnvelope.treeHash tree) "the tree's content address"
                  Expect.equal verified.KeyId "alice" "under the offered key"
              | Error refusal -> failtest $"a signed pair did not verify: %A{refusal}"
          }

          test "the demanded wire is unchanged — a consumer ignoring the signature reads what it read before" {
              let signed = signedBy alice

              Expect.equal
                  signed.Envelope
                  (Demanded.encode (Demanded.ofTree tree))
                  "the bytes are Demanded.encode's, verbatim"

              Expect.equal
                  (Demanded.decode signed.Envelope)
                  (Ok(Demanded.ofTree tree))
                  "and Demanded.decode reads them with no knowledge of the signature"
          }

          test "the tree hash is the tier's content address over the tier's canonical encoding" {
              let signed = signedBy alice
              Expect.isTrue (ProgramWire.isContentAddress signed.TreeHash) "well formed"

              Expect.equal
                  signed.TreeHash
                  ("sha256:" + Fuaran.UI.Hashing.sha256Hex (CanonicalJson.encodeNode tree))
                  "sha256 over CanonicalJson.encodeNode"
          }

          // ── the tamper cases, each with its own reason ───────────

          test "a tree that gained an effect is DRIFT, with the excess named" {
              let signed = signedBy alice

              let grown =
                  dash
                      [ Fuaran.markdown "m" "static"
                        btn "go" navigate
                        btn "clip" (Action.WriteToClipboard(TextSource.Literal "x")) ]

              let drift = driftOf (verifyWith (Some alice) Demanded.ofTree grown signed)
              Expect.equal drift.Excess.Effects [ "WriteToClipboard" ] "the effect beyond the envelope"
              Expect.equal drift.Shortfall Demanded.empty "and nothing the envelope over-claims"
              Expect.isNone drift.Unreadable "the envelope itself was readable"
          }

          test "an envelope that claims what the tree does not is DRIFT, with the shortfall named" {
              let signed = signedBy alice

              let inflated =
                  Demanded.union [ Demanded.ofTree tree; Demanded.ofTree (dash [ btn "p" Action.Print ]) ]

              let tampered =
                  { signed with
                      Envelope = Demanded.encode inflated }

              let drift = driftOf (verifyWith (Some alice) Demanded.ofTree tree tampered)
              Expect.equal drift.Shortfall.Effects [ "Print" ] "the claim the tree does not make"
              Expect.equal drift.Excess Demanded.empty "and nothing the tree demands beyond it"
          }

          test "an envelope that is not a document is DRIFT, with the whole recomputation as excess" {
              let signed = signedBy alice

              let tampered =
                  { signed with
                      Envelope = "not a demanded document" }

              let drift = driftOf (verifyWith (Some alice) Demanded.ofTree tree tampered)
              Expect.isSome drift.Unreadable "named as unreadable"
              Expect.equal drift.Excess (Demanded.ofTree tree) "everything the tree demands is beyond it"
              Expect.equal drift.Shortfall Demanded.empty "nothing could be read to fall short"
          }

          test "a tree that moved without moving a demand is a BAD SIGNATURE, hashes disagreeing" {
              // The envelope still describes this tree exactly — the caption is
              // not a demand — so drift has nothing to say. What the signature
              // attests is the PAIRING, and this is not the tree that was
              // paired.
              let signed = signedBy alice
              let edited = dash [ Fuaran.markdown "m" "edited"; btn "go" navigate ]

              match refusalOf (verifyWith (Some alice) Demanded.ofTree edited signed) with
              | VerifyRefusal.BadSignature(carried, recomputed) ->
                  Expect.equal carried signed.TreeHash "the hash the record carries"
                  Expect.equal recomputed (SignedEnvelope.treeHash edited) "the hash of the tree in hand"
                  Expect.notEqual carried recomputed "and they disagree: the tree moved"
              | other -> failtest $"expected a bad signature, got %A{other}"
          }

          test "forged signature bytes are a BAD SIGNATURE, hashes agreeing" {
              let signed = signedBy alice

              let forged =
                  { signed with
                      Signature = Convert.ToBase64String(Array.zeroCreate<byte> 64) }

              match refusalOf (verifyWith (Some alice) Demanded.ofTree tree forged) with
              | VerifyRefusal.BadSignature(carried, recomputed) ->
                  Expect.equal carried recomputed "the tree is the one that was paired; the bytes are not its signature"
              | other -> failtest $"expected a bad signature, got %A{other}"
          }

          test "another party's key is a FOREIGN KEY, and nothing is recomputed under it" {
              let signed = signedBy alice

              Expect.equal
                  (refusalOf (verifyWith (Some mallory) Demanded.ofTree tree signed))
                  (VerifyRefusal.ForeignKey("alice", "mallory"))
                  "signed by alice, offered mallory"

              // Even a drifted tree is not diagnosed under a key the record was
              // not signed with: the answer would be about a document nobody
              // vouched for.
              let grown = dash [ btn "p" Action.Print ]

              Expect.equal
                  (refusalOf (verifyWith (Some mallory) Demanded.ofTree grown signed))
                  (VerifyRefusal.ForeignKey("alice", "mallory"))
                  "foreign key first"
          }

          test "a key with the signer's ID but not the signer's material is a BAD SIGNATURE" {
              // Impersonation by key id: the id matches, so it is not foreign,
              // and the crypto is what catches it.
              let signed = signedBy alice
              let impostor = party "alice"

              match refusalOf (verifyWith (Some impostor) Demanded.ofTree tree signed) with
              | VerifyRefusal.BadSignature _ -> ()
              | other -> failtest $"expected a bad signature, got %A{other}"
          }

          // ── no key is a refusal ──────────────────────────────────

          test "a verify with no key REFUSES — it does not skip the signature check" {
              let signed = signedBy alice

              Expect.equal
                  (refusalOf (verifyWith None Demanded.ofTree tree signed))
                  VerifyRefusal.NoKey
                  "a valid pair with no key is not verified"

              // Nor is the envelope diagnosed in the key's absence: a drift
              // report here would be the check quietly running without its
              // signature half.
              let grown = dash [ btn "p" Action.Print ]

              Expect.equal
                  (refusalOf (verifyWith None Demanded.ofTree grown signed))
                  VerifyRefusal.NoKey
                  "no key, no verdict of any kind"
          }

          test "a declining sink signs nothing, and says so" {
              Expect.equal
                  (SignedEnvelope.sign declining Demanded.ofTree tree)
                  (Error SignRefusal.SinkDeclined)
                  "the unattested posture is a named refusal, not an empty signature"
          }

          // ── None versus empty ────────────────────────────────────

          test "the server tier's None-versus-empty distinction survives the round trip" {
              let clientSigned = signedBy alice

              let serverSigned =
                  match SignedEnvelope.sign alice.Sink serverWalk tree with
                  | Ok s -> s
                  | Error r -> failtest $"sign refused: %A{r}"

              let roundTrip (signed: SignedEnvelope) : SignedEnvelope =
                  match SignedEnvelope.decode (SignedEnvelope.encode signed) with
                  | Ok r -> r
                  | Error e -> failtest $"decode refused: %A{e}"

              match verifyWith (Some alice) Demanded.ofTree tree (roundTrip clientSigned) with
              | Ok v -> Expect.isNone v.Projection.Server "signed with no server walk: None"
              | Error r -> failtest $"client pair did not verify: %A{r}"

              match verifyWith (Some alice) serverWalk tree (roundTrip serverSigned) with
              | Ok v -> Expect.equal v.Projection.Server (Some emptyTier) "signed with an empty tier: Some empty"
              | Error r -> failtest $"server pair did not verify: %A{r}"

              // And each collapse is drift, in the direction it happened.
              let drift = driftOf (verifyWith (Some alice) Demanded.ofTree tree serverSigned)

              Expect.equal
                  drift.Shortfall.Server
                  (Some emptyTier)
                  "the envelope says walked-and-empty; this walk never asked"

              Expect.isNone drift.Excess.Server "nothing beyond it on the client side"

              let drift = driftOf (verifyWith (Some alice) serverWalk tree clientSigned)

              Expect.equal
                  drift.Excess.Server
                  (Some emptyTier)
                  "this walk asked and found nothing; the envelope says never asked"

              Expect.isNone drift.Shortfall.Server "nothing the envelope over-claims"
          }

          // ── the record as a wire document ────────────────────────

          test "the record's reader refuses another kind, another version and an undeclared member" {
              let signed = signedBy alice
              let wire = SignedEnvelope.encode signed

              let defectOf (json: string) : DemandedDefect =
                  match SignedEnvelope.decode json with
                  | Error e -> e.Defect
                  | Ok r -> failtest $"expected a refusal, read %A{r}"

              Expect.equal
                  (defectOf (Demanded.encode Demanded.empty))
                  DemandedDefect.UnknownKind
                  "a demanded document is not a signed record"

              Expect.equal
                  (defectOf (wire.Replace("\"version\":1", "\"version\":2")))
                  DemandedDefect.UnknownVersion
                  "a version this reader does not read"

              Expect.equal
                  (defectOf (wire.Insert(wire.Length - 1, ",\"extra\":true")))
                  DemandedDefect.UndeclaredMember
                  "a member this version does not declare"

              Expect.equal
                  (defectOf (wire.Replace(signed.TreeHash, "not-an-address")))
                  DemandedDefect.WrongType
                  "a tree hash that is not a content address"

              Expect.equal (defectOf "{}") DemandedDefect.MissingMember "an empty object"
          } ]
