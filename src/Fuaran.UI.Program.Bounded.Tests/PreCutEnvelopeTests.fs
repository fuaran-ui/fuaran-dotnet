/// K7 (docs/generic-tier.md §3.7), pinned BEFORE the hash swap it licenses.
///
/// Phase 1896 replaced the UI tier's `Hashing.sha256Hex` with Core's
/// `Hash.sha256Hex` in the signed envelope's tree hash, on the assumption that
/// the two are byte-identical (SHA-256 over the UTF-8 bytes, lowercase hex).
/// The falsifier the design note names is an envelope signed before the cut
/// that fails to verify after it. The TREE HASH half of that pin is the first
/// test below, and it is the pre-cut value, byte for byte, still.
///
/// The SIGNATURE half was re-pinned by Phase 1967, and the reason is a fact
/// about the design worth having written down: a signed envelope is verified
/// by RECOMPUTATION, so its signature is bound to the demanded document's
/// version as well as to the tree, and when the document moved to version 5
/// (the op reach) every envelope signed under version 4 — this one included —
/// began reporting `Unreadable` drift naming the version, which is the honest
/// refusal the versioning exists to give. The envelope below was therefore
/// signed afresh by the 0.7.0 code over the SAME tree, with a new key whose
/// private half was never kept, and the test keeps doing the job the pin was
/// for: if the hash, the canonical encoding, the projection's bytes or the
/// preimage ever drift again, the recomputed preimage stops matching and this
/// goes red — until the next deliberate version move re-pins it, stated here.
///
/// Phase 1977 was that next move: the document went to version 6 (the undo
/// posture), every envelope signed under version 5 — the 1967 pin included —
/// began reporting `Unreadable` drift naming the version, and the envelope
/// below was signed afresh by the 0.7.0 code over the SAME tree with a new
/// key, exactly as before. The tree hash is still the pre-cut value.
///
/// fuaran#2191 is the next: Fuaran.Program 0.8.0 moved the document to version
/// 8 (version 7 states iterations, Program Phase 1991; version 8 opaque leaves,
/// Program Phase 2130), so the version-6 pin reported `Unreadable` drift, and
/// the envelope below was signed afresh by the 0.8.0 code over the SAME tree
/// with a new key whose private half was never kept. The tree hash is still the
/// pre-cut value.
module Fuaran.UI.Program.Bounded.Tests.PreCutEnvelopeTests

open System
open System.Security.Cryptography
open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.Program.Bounded
open Fuaran.UI.Program

/// The tree the pre-cut code signed: a static caption beside a button that
/// navigates.
let private tree: Node<obj> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<obj> with
            Children =
                [ Fuaran.markdown "m" "static"
                  Fuaran.button
                      "go"
                      { Defaults.button<obj> with
                          OnClick = Action.Navigate(TextSource.Literal "/next", NavigateTarget.Self) } ] }

/// The tree hash the pre-cut code computed, with the UI tier's hash.
[<Literal>]
let private PreCutTreeHash =
    "sha256:2d221262c858f3488cb0a61e17e3e5472fc17399fb057c2332c0f42a9a16a542"

/// The signer's PUBLIC key (SubjectPublicKeyInfo, P-256), as the fuaran#2191
/// re-pin exported it. The private half was never kept.
[<Literal>]
let private PublicKey =
    "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEI4kZvn0FedTejheIgIHRTOx+hnBeTGm4tTmn+2Up1VPAXNC8LckuVfIvPpsuV242fX6bIm+B3+tkqCQnDZKmug=="

/// The signed envelope, byte for byte: the pre-cut tree hash, the version-8
/// demanded document, and the fuaran#2191 signature over the two.
[<Literal>]
let private PreCutEnvelope =
    """{"kind":"signed-demanded","version":1,"treeHash":"sha256:2d221262c858f3488cb0a61e17e3e5472fc17399fb057c2332c0f42a9a16a542","envelope":"{\"kind\":\"demanded\",\"version\":8,\"effects\":[\"Navigate\"],\"hostCalls\":[],\"stateNamespaces\":[],\"opaqueHandlers\":[],\"iterations\":[],\"opaqueLeaves\":[],\"server\":null}","keyId":"k7-2191","signature":"aZIElpDDpSYlUIpsKcx+lj5HuK3DWI10s1n1vGkIs7klysRY/XxQGJ713CYONYS0gQPeRZIxUheTFGEgEb88kg=="}"""

let private publicEntry () : KeyDirectoryEntry =
    let key = ECDsa.Create()
    key.ImportSubjectPublicKeyInfo(Convert.FromBase64String PublicKey) |> ignore
    EcdsaP256.keyEntry "k7-2191" key

[<Tests>]
let tests =
    testList
        "K7 — an envelope signed before the hash swap still verifies after it"
        [ test "Core's tree hash is the pre-cut hash, byte for byte" {
              Expect.equal (SignedEnvelope.treeHash tree) PreCutTreeHash "the content address is unchanged"
          }

          test "the pinned signed envelope verifies under the current code" {
              let signed =
                  match SignedEnvelope.decode PreCutEnvelope with
                  | Ok signed -> signed
                  | Error failure -> failtestf "the committed envelope does not decode: %A" failure

              match
                  SignedEnvelope.verify EcdsaP256.claimVerifier (Some(publicEntry ())) Demanded.ofTree tree signed
                  |> Async.RunSynchronously
              with
              | Ok verified -> Expect.equal verified.TreeHash PreCutTreeHash "verified over the same content address"
              | Error refusal -> failtestf "the pinned envelope no longer verifies: %A" refusal
          }

          test "and the check bites: a one-byte edit to the tree is refused" {
              let edited =
                  Fuaran.dashboard
                      "root"
                      { Defaults.dashboard<obj> with
                          Children = [ Fuaran.markdown "m" "Static" ] }

              let signed =
                  match SignedEnvelope.decode PreCutEnvelope with
                  | Ok signed -> signed
                  | Error failure -> failtestf "the committed envelope does not decode: %A" failure

              let result =
                  SignedEnvelope.verify EcdsaP256.claimVerifier (Some(publicEntry ())) Demanded.ofTree edited signed
                  |> Async.RunSynchronously

              Expect.isError result "a tree other than the signed one does not verify"
          } ]
