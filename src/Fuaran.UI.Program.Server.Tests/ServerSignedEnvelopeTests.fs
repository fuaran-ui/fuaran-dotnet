module Fuaran.UI.Program.Server.Tests.ServerSignedEnvelopeTests

// ─── The signed effect envelope at the SERVER placement ──────────────
//
// `ServerDemanded.sign` / `verify` are `SignedEnvelope.sign` / `verify` with
// this placement's two-tier walk. What is specific to this placement, and so
// pinned here rather than in the client tier's suite:
//
//  1. THE REGISTRATION IS RECOMPUTED TOO — a handler that gained a stage since
//     the envelope was signed is drift with the excess in the SERVER tier, on
//     the same terms a tree that gained an effect is drift in the client tier.
//  2. `None` VERSUS EMPTY, ACROSS WALKS — a record signed here for a tree that
//     names no handler carries an EMPTY tier, and verifying it through the
//     client-tier walk reports that tier as shortfall rather than reading
//     "walked, found nothing" as "never walked".

open System
open System.Security.Cryptography
open System.Text
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.Program.Bounded
open Fuaran.UI.Program
open Fuaran.Program.Server
open Fuaran.UI.Program.Server

// ─── fixtures ────────────────────────────────────────────────────────

let private jstr (s: string) = Fuaran.Core.JStr s

let private endpoint = "/handlers/work"

let private treeCalling (target: string) : Node<obj> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<obj> with
            Children =
                [ Fuaran.button
                      "call"
                      { Defaults.button<obj> with
                          Label = TextSource.Literal "call"
                          OnClick = Action.Call(target, None, None) } ] }

let private registrationOf (stages: HandlerStage list) : Map<string, Handler> =
    Map.ofList [ endpoint, { Name = "work"; Stages = stages } ]

let private sendMail: HandlerStage =
    Effect(ServerEffect.HostCall("sendMail", jstr "x", None))

let private notify: HandlerStage = Effect(ServerEffect.Notify("ops", jstr "n"))

/// A P-256 signing party over the substrate's attestation sink, with the
/// PUBLIC half as the entry a verifier is offered.
type private Party =
    { Entry: KeyDirectoryEntry
      Sink: Fuaran.Core.IAttestationSink }

let private party (keyId: string) : Party =
    let key = ECDsa.Create(ECCurve.NamedCurves.nistP256)

    let sink =
        { new Fuaran.Core.IAttestationSink with
            member _.Sign head =
                Some(
                    { Head = head
                      KeyId = keyId
                      Signature =
                        Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes head, HashAlgorithmName.SHA256)) }
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

let private signedWith (p: Party) (handlers: Map<string, Handler>) (root: Node<obj>) : SignedEnvelope =
    match ServerDemanded.sign p.Sink handlers root with
    | Ok signed -> signed
    | Error refusal -> failtest $"sign refused: %A{refusal}"

let private verifyWith (p: Party) (handlers: Map<string, Handler>) (root: Node<obj>) (signed: SignedEnvelope) =
    ServerDemanded.verify EcdsaP256.claimVerifier (Some p.Entry) handlers root signed
    |> Async.RunSynchronously

let private driftOf (result: Result<VerifiedEnvelope, VerifyRefusal>) : EnvelopeDrift =
    match result with
    | Error(VerifyRefusal.EnvelopeDrift drift) -> drift
    | other -> failtest $"expected drift, got %A{other}"

// ─── tests ───────────────────────────────────────────────────────────

[<Tests>]
let tests =
    let alice = party "alice"

    testList
        "Phase 1744 — the signed effect envelope, server tier"
        [

          test "a two-tier pair verifies by recomputation against the same registration" {
              let handlers = registrationOf [ sendMail ]
              let tree = treeCalling endpoint
              let signed = signedWith alice handlers tree

              match verifyWith alice handlers tree signed with
              | Ok verified ->
                  Expect.equal
                      verified.Projection
                      (ServerDemanded.ofTreeAndHandlers handlers tree)
                      "the walk's own two-tier document"

                  Expect.equal
                      (verified.Projection.Server
                       |> Option.map _.Functions
                       |> Option.map (List.map _.Function))
                      (Some [ "sendMail" ])
                      "with the reachable handler's host function in the server tier"
              | Error refusal -> failtest $"did not verify: %A{refusal}"
          }

          test "a registration that gained a stage is DRIFT, with the excess in the server tier" {
              let tree = treeCalling endpoint
              let signed = signedWith alice (registrationOf [ sendMail ]) tree

              let drift =
                  driftOf (verifyWith alice (registrationOf [ sendMail; notify ]) tree signed)

              Expect.equal
                  (drift.Excess.Server |> Option.map _.Effects)
                  (Some [ "Notify" ])
                  "the effect the handler can now emit that the envelope never named"

              Expect.equal drift.Excess.Effects [] "and nothing moved in the client tier"
          }

          test "a tree naming no handler signs an EMPTY tier, and the client-tier walk reports it as shortfall" {
              let handlers = registrationOf [ sendMail ]
              let tree = treeCalling "/handlers/nobody"
              let signed = signedWith alice handlers tree

              // Verified with this placement's walk: an empty tier, as signed.
              match verifyWith alice handlers tree signed with
              | Ok verified ->
                  Expect.equal
                      (verified.Projection.Server |> Option.map _.Functions)
                      (Some [])
                      "walked, and no reachable handler demanded anything"
              | Error refusal -> failtest $"did not verify: %A{refusal}"

              // Verified with the CLIENT walk: the tier the envelope carries is
              // one this walk never produced — shortfall, never silence.
              let drift =
                  driftOf (
                      SignedEnvelope.verify EcdsaP256.claimVerifier (Some alice.Entry) Demanded.ofTree tree signed
                      |> Async.RunSynchronously
                  )

              Expect.isSome drift.Shortfall.Server "the envelope's empty tier is named as shortfall"
              Expect.isNone drift.Excess.Server "and the client walk has no tier to exceed it with"
          } ]
