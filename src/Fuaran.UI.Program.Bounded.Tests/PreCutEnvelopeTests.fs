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
///
/// fuaran#2203 is the next: Fuaran.Program 0.9.0 moved the document to version
/// 11 (version 9 states values, Program Phase 2186; version 10 the staged-query
/// defect, Phase 2187; version 11 the `Report` effect, Phase 2195), so the
/// version-8 pin reported `Unreadable` drift, and the envelope below was signed
/// afresh by the 0.9.0 code over the SAME tree with a new key whose private half
/// was never kept. The tree hash is still the pre-cut value.
///
/// fuaran#2300 is the first move of a different kind: not the document's version
/// but the TREE's canonical bytes. Rule 1 of that phase omits a field holding its
/// declared default from the canonical form, and this tree's button carries
/// `variant = Secondary`, the declared default, so `"variant":"Secondary"` left
/// its wire and the content address moved with it — the hash below is the one the
/// 0.97.0 code computes over the SAME tree, and the envelope was signed afresh
/// over it with a new key whose private half was never kept. What the first test
/// pins is therefore no longer "the pre-cut value" but the address of these
/// canonical bytes under Core's hash; the falsifier it guards (the two hashes
/// disagreeing over one byte string) is unchanged, and the old address is in this
/// file's history should the ruling ever be read back.
///
/// fuaran#2432 is a version move again: Fuaran.Program 0.13.0 moved the document
/// to version 14 (version 12 the windowed clauses, Program Phase 2260; version 13
/// the declared quantities, Phase 2261; version 14 the floor and Required
/// clauses, Phase 2262), so the version-11 pin reported `Unreadable` drift, and
/// the envelope below was signed afresh by the 0.13.0 code over the SAME tree
/// with a new key whose private half was never kept. The tree hash is unchanged.
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

/// The tree's content address under the fuaran#2300 canonical form (the pre-2300
/// address was `sha256:2d221262…6a542`; see the header).
[<Literal>]
let private PreCutTreeHash =
    "sha256:fd89e42469a02b9f0dc8cf4c29fd2267d24f0b82426e9268ac907d3eedaf4e87"

/// The signer's PUBLIC key (SubjectPublicKeyInfo, P-256), as the fuaran#2432
/// re-pin exported it. The private half was never kept.
[<Literal>]
let private PublicKey =
    "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEv575AzQ3tSHMQi1jB67OnDMzM3SxoW/FGdxt06V/FgZNPXg/Qir9d1u8sxxNNMYpeyZ2yWMGFwyjILF1tyDL8A=="

/// The signed envelope, byte for byte: the tree hash above, the version-14
/// demanded document, and the fuaran#2432 signature over the two.
[<Literal>]
let private PreCutEnvelope =
    """{"kind":"signed-demanded","version":1,"treeHash":"sha256:fd89e42469a02b9f0dc8cf4c29fd2267d24f0b82426e9268ac907d3eedaf4e87","envelope":"{\"kind\":\"demanded\",\"version\":14,\"effects\":[\"Navigate\"],\"hostCalls\":[],\"stateNamespaces\":[],\"opaqueHandlers\":[],\"iterations\":[],\"opaqueLeaves\":[],\"values\":[],\"quantities\":[],\"server\":null}","keyId":"k7-2432","signature":"JNhvL1m6wMjaGcDUAHX6OBRybHPIMEDlcgNI5f99TvWHtsLHNqa1aLis9OQK1yoBTfMabPMLbTrgqGOtIDG5fw=="}"""

let private publicEntry () : KeyDirectoryEntry =
    let key = ECDsa.Create()
    key.ImportSubjectPublicKeyInfo(Convert.FromBase64String PublicKey) |> ignore
    EcdsaP256.keyEntry "k7-2432" key

[<Tests>]
let tests =
    testList
        "K7 — an envelope signed before the hash swap still verifies after it"
        [ test "Core's tree hash is the pinned address of the tree's canonical bytes, byte for byte" {
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
