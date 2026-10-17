module Fuaran.UI.Hashing

// ============================================================================
//  Fuaran — the tier's content-hash compositions over Core's SHA-256.
//
//  The tier holds no SHA-256 of its own. From Phase 164 to Phase 2309 this
//  module carried a pure, Fable-safe FIPS 180-4 implementation and a hand UTF-8
//  encoder, written before `Fuaran.Core` had either. Core now owns both
//  (`Fuaran.Core.Hash.sha256Hex` / `Hash.utf8Bytes`, and the chain's
//  `Fuaran.Core.OpStream.sha256Hash`), pinned on both pipelines by Core's own
//  parity vectors, so every digest the tier takes routes through that one
//  primitive and a correctness fix lands once. The two copies had already
//  drifted once — the encoder here took the four-byte path for a lone high
//  surrogate (Phase 2042) after Core had fixed the same defect.
//
//  The swap moved no value: Core's function is byte-identical to the retired
//  one on every input, ill-formed UTF-16 included, so no persisted hash, chain
//  head, checkpoint, teleport, attestation or fragment key changed. The tier's
//  FIPS 180-4 and BCL cross-check vectors stay, as consumer-side pins of Core's
//  function (`Fuaran.UI.Tests`, `CoreHashPinTests`).
// ============================================================================

/// The canonical Phase-134 Custom *body-shape* hash — the one definition the
/// validator's `computeBodyShapeHash` calls too, so a contract-derived hash and a
/// hand-set-then-validated hash agree by construction. `propKeys` is the props
/// schema (keys only); both lists are sorted internally.
///
/// The pre-image joins its fields with `,` and `\n` and is NOT injective: a prop
/// key or exposed id containing a comma, or an id containing a newline, can
/// collide with a different declaration. It is kept byte-for-byte because
/// changing it moves every stored hash; the replacement (`Fuaran.Core`'s
/// `Digest.tryOfFields` over the injective `Hash.canonicalFields`) is a
/// breaking-cut candidate recorded in `CRYPTO.md`.
let customBodyShapeHash
    (moduleId: string)
    (componentId: string)
    (propKeys: string list)
    (exposedNodeIds: string list)
    : string =
    [ "fuaran-custom-body-shape:v1"
      "moduleId=" + moduleId
      "componentId=" + componentId
      "props=" + (propKeys |> List.sort |> String.concat ",")
      "exposed=" + (exposedNodeIds |> List.sort |> String.concat ",") ]
    |> String.concat "\n"
    |> Fuaran.Core.Hash.sha256Hex
