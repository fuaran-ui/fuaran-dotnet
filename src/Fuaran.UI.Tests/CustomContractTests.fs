module Fuaran.UI.Tests.CustomContract

// ============================================================================
//  Phase 164 — typed Custom-payload contracts.
//
//  A `CustomContract<'Props>` is defined once; from it the consumer gets the
//  typed node constructor (`Custom.node`), client + server registration, and a
//  content hash DERIVED from the declared shape. These tests pin the pure half
//  (no DOM): the Fable-safe SHA-256 vs the BCL oracle, the body-shape hash vs
//  the validator's canonical string, hash-derivation stability/sensitivity, the
//  typed constructor's stamping, and the encode→decode round-trip + failure.
//
//  The client `RegisterContract` Ok path is pinned via the registry contract
//  (registration + recorded hash + Some-on-valid-payload). The decode-FAILURE
//  placeholder is rendered through `Html.div`, whose .NET-side ReactElement cast
//  throws (same constraint as CustomRendererTests / AccessibilityTests), so the
//  rendered placeholder is asserted on the SERVER pipeline instead, where the
//  output is a real HTML string (CustomContractServerTests).
// ============================================================================

open Expecto
open Feliz
open Fuaran.UI
open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Runtime

// A representative typed payload for the contract under test.
type private Spark = { Points: string; Label: string }

let private encode (p: Spark) : Map<string, JVal> =
    Map.ofList [ "points", JStr p.Points; "label", JStr p.Label ]

let private decode (bag: Map<string, JVal>) : Result<Spark, CustomDecodeError> =
    match Map.tryFind "points" bag, Map.tryFind "label" bag with
    | Some(JStr pts), Some(JStr lbl) -> Ok { Points = pts; Label = lbl }
    | Some _, Some _ -> Error(CustomDecodeError.forKey "points" "expected string values")
    | None, _ -> Error(CustomDecodeError.forKey "points" "missing required key")
    | _, None -> Error(CustomDecodeError.forKey "label" "missing required key")

let private sample = { Points = "0,1 2,3"; Label = "trend" }

let private contract =
    CustomContract.create "sample" "sparkline" encode decode sample [ NodeId "spark-line" ] HashStrictness.StrictReplay

/// BCL reference SHA-256 (lowercase hex) — the oracle the Fable-safe pure
/// implementation must match byte-for-byte.
let private bclSha256 (s: string) : string =
    use sha = System.Security.Cryptography.SHA256.Create()

    s
    |> System.Text.Encoding.UTF8.GetBytes
    |> sha.ComputeHash
    |> Array.map (fun b -> b.ToString("x2"))
    |> String.concat ""

[<Tests>]
let tests =
    testList
        "Phase 164 — typed Custom-payload contracts"
        [
          // ── Pure SHA-256 parity (the load-bearing claim) ────────────────────
          test "sha256Hex matches the BCL byte-for-byte across a corpus" {
              let corpus =
                  [ ""
                    "a"
                    "abc"
                    "fuaran-custom-body-shape:v1"
                    String.replicate 200 "x" // spans multiple 64-byte blocks
                    "héllo wörld — ünïcôde"
                    "emoji 😀 surrogate-pair" ]

              for input in corpus do
                  Expect.equal (Hashing.sha256Hex input) (bclSha256 input) (sprintf "sha256 parity for %A" input)
          }

          // ── NIST CAVP / FIPS 180-4 known-answer vectors ─────────────────────
          // The published standard vectors — the gold-standard proof the
          // dependency-free implementation IS SHA-256, independent of the BCL
          // (which the corpus test above already cross-checks). These are the
          // canonical FIPS 180-2/4 examples + the NIST empty / one-block cases.
          test "sha256Hex reproduces the NIST FIPS 180-4 known-answer vectors" {
              let vectors =
                  [ "", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
                    "abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
                    "abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq",
                    "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1"
                    // 448-bit (56 chars): exercises the pad-into-a-second-block path.
                    "abcdefghbcdefghicdefghijdefghijkefghijklfghijklmghijklmnhijklmnoijklmnopjklmnopqklmnopqrlmnopqrsmnopqrstnopqrstu",
                    "cf5b16a778af8380036ce59e7b0492370b249b11e8f07a51afac45037afee9d1"
                    // The classic one-million-'a' vector (many blocks, exact bit length).
                    String.replicate 1000000 "a", "cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0" ]

              for (input, expected) in vectors do
                  Expect.equal
                      (Hashing.sha256Hex input)
                      expected
                      (sprintf "NIST vector for a %d-char input" input.Length)
          }

          test "customBodyShapeHash equals the canonical-string BCL SHA-256 (validator parity)" {
              // Same canonical string the validator's computeBodyShapeHash builds.
              let canonical =
                  String.concat
                      "\n"
                      [ "fuaran-custom-body-shape:v1"
                        "moduleId=sample"
                        "componentId=sparkline"
                        "props=label,points" // sorted
                        "exposed=spark-line" ]

              Expect.equal
                  (Hashing.customBodyShapeHash "sample" "sparkline" [ "points"; "label" ] [ "spark-line" ])
                  (bclSha256 canonical)
                  "contract-derived hash agrees with a hand-set-then-validated hash"
          }

          test "customBodyShapeHash is order-insensitive on prop keys + exposed ids" {
              let a = Hashing.customBodyShapeHash "m" "c" [ "x"; "y"; "z" ] [ "p"; "q" ]
              let b = Hashing.customBodyShapeHash "m" "c" [ "z"; "x"; "y" ] [ "q"; "p" ]
              Expect.equal a b "both lists are sorted internally — author order does not change the digest"
          }

          // ── Contract hash derivation ────────────────────────────────────────
          test "contract derives a SHA256 hash from the declared shape (not hand-typed)" {
              Expect.equal contract.Hash.Algorithm "SHA256" "algorithm"
              Expect.equal contract.Hash.Strictness HashStrictness.StrictReplay "strictness carried onto the hash"

              Expect.equal
                  contract.Hash.Hash
                  (Hashing.customBodyShapeHash "sample" "sparkline" [ "points"; "label" ] [ "spark-line" ])
                  "derived from module/component + the encoder's prop-key set + exposed ids"
          }

          test "derived hash is stable for an unchanged contract and changes when the shape changes" {
              let again =
                  CustomContract.create
                      "sample"
                      "sparkline"
                      encode
                      decode
                      sample
                      [ NodeId "spark-line" ]
                      HashStrictness.StrictReplay

              Expect.equal contract.Hash.Hash again.Hash.Hash "same shape → same hash (cross-run/machine stable)"

              // A wider prop-key set (encoder emits an extra key) → different hash.
              let encodeWider (p: Spark) = encode p |> Map.add "extra" (JInt 1)

              let widerShape =
                  CustomContract.create
                      "sample"
                      "sparkline"
                      encodeWider
                      decode
                      sample
                      [ NodeId "spark-line" ]
                      HashStrictness.StrictReplay

              Expect.notEqual
                  contract.Hash.Hash
                  widerShape.Hash.Hash
                  "added prop key changes the declared shape → different hash"

              // A different exposed-id set → different hash.
              let exposedChanged =
                  CustomContract.create
                      "sample"
                      "sparkline"
                      encode
                      decode
                      sample
                      [ NodeId "other" ]
                      HashStrictness.StrictReplay

              Expect.notEqual contract.Hash.Hash exposedChanged.Hash.Hash "changed exposed id → different hash"
          }

          // ── Typed node constructor ──────────────────────────────────────────
          test "Custom.node encodes props + stamps the derived hash + exposed ids" {
              let node: Node<obj> = Custom.node "spark-1" contract sample
              Expect.equal node.Id (NodeId "spark-1") "node id"

              match node.Kind with
              | NodeKind.Custom(m, c, props, hash, exposed) ->
                  Expect.equal m "sample" "module id"
                  Expect.equal c "sparkline" "component id"
                  Expect.equal props (encode sample) "encoded prop bag (can never be malformed)"
                  Expect.equal hash (Some contract.Hash) "stamps the contract's derived hash"
                  Expect.equal exposed [ NodeId "spark-line" ] "stamps the contract's exposed ids"
              | other -> failtestf "expected NodeKind.Custom, got %A" other
          }

          // ── Decode round-trip + failure ─────────────────────────────────────
          test "encode → decode round-trips losslessly for the contract's 'Props" {
              match contract.Decode(contract.Encode sample) with
              | Ok p -> Expect.equal p sample "round-trip is lossless"
              | Error e -> failtestf "expected Ok, got Error %A" e
          }

          test "a tampered prop bag decodes to Error naming the failing key" {
              let tampered = contract.Encode sample |> Map.remove "points"

              match contract.Decode tampered with
              | Error e -> Expect.equal e.Key "points" "the failing key is named (debuggable, not a blank box)"
              | Ok p -> failtestf "expected Error, got Ok %A" p
          }

          // ── Client dual-registration (Ok path; .NET ReactElement is opaque) ──
          test "client RegisterContract records the derived hash + serves the Ok path" {
              let registry = CustomRendererRegistry()
              let sentinel: ReactElement = Unchecked.defaultof<ReactElement>
              registry.RegisterContract(contract, (fun (_p: Spark) -> sentinel))

              match registry.TryGet(contract.ModuleId, contract.ComponentId) with
              | Some(_, Some h) -> Expect.equal h contract.Hash "registered with the contract's derived hash"
              | Some(_, None) -> failtest "expected the contract hash to be recorded on the client registry"
              | None -> failtest "expected the contract to be registered on the client registry"

              // Ok path: a valid bag decodes and routes to the user render fn.
              let rendered =
                  registry.TryRender(contract.ModuleId, contract.ComponentId, contract.Encode sample)

              Expect.isSome rendered "a valid payload routes through the contract decode to the renderer"
          } ]
