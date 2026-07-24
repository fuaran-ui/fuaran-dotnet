module Fuaran.UI.Content.Exemplar

// ─── The validated-exemplar seam ─────────────────────────────────────────────
//
// Admit a canonical wire-format JSON string into a `Node` tree, with a build-
// time integrity claim: the exemplar is decoded (Fuaran.UI.Ops.JsonDecode),
// pre-emit-validated (Fuaran.UI.PreEmitValidate), and proven to be a
// decode/encode fixed point under the canonical encoder — three gates, all on
// plain .NET, no client runtime and no SSR host.
//
// This is the host-neutral core that a documentation site, an evaluation-suite seed,
// or a `fuaran-wire` fence pipeline all reach for independently: "take this
// authored exemplar and either give me a first-class `Node` I can splice into a
// larger tree, or tell me — with the decoder's own AI-recovery hints — exactly
// why it is not admissible." A rejected exemplar is an `Error`, so a caller can
// make it a build failure rather than a published invalid example.
//
// The package depends only on `Fuaran.UI` (+ `.Ops` for the decoder, +
// `.OpStream.Abstractions` for the canonical encoder). It carries no Giraffe /
// ASP.NET / routing / markdown dependency — rendering and page assembly stay
// with the host. Graduated out of the fuaran-ui.io docs site once the seam had
// proven its shape; kept host-neutral so any consumer can compose it.

open Fuaran.UI

/// An exemplar rejected at one of the three gates. Carries the typed error so a
/// caller can surface the AI-recovery hints (stable code / JSON path / expected
/// shape) rather than a flattened string.
type ExemplarFailure =
    /// The JSON did not decode to a wire tree (Gate 1 — decode).
    | DecodeFailed of Fuaran.UI.Ops.JsonDecode.DecodeError
    /// The decoded tree failed the pre-emit invariants (Gate 2 — validate).
    | ValidateFailed of PreEmitValidate.PreEmitDefect list
    /// The canonical re-encoding did not survive decode → encode byte-
    /// identically (Gate 3 — round-trip fixed point). Never an authoring error —
    /// this is encoder/decoder skew in the consumed packages, caught here
    /// because a published permalink carries the canonical bytes and MUST stay
    /// decodable by every conformant host.
    | RoundTripFailed of detail: string

/// Decode → pre-emit-validate → round-trip-check a canonical wire exemplar,
/// returning the decoded `Node<obj>` tree and its canonical encoding. The
/// canonical string — not the authored input text — is what a consumer should
/// display or embed in a permalink, so byte-determinism is the encoder's, not
/// the author's. The three gates are the exemplar's integrity claim: a rejected
/// exemplar is an `Error`, never a silently-published invalid tree.
let decodeExemplar (json: string) =
    match Fuaran.UI.Ops.JsonDecode.decodeNodeObj json with
    | Error e -> Error(DecodeFailed e)
    | Ok node ->
        match PreEmitValidate.validate node with
        | Error defects -> Error(ValidateFailed defects)
        | Ok() ->
            let canonical = Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode node

            // Gate 3 — a published permalink carries `canonical` in its fragment;
            // prove it re-decodes and re-encodes byte-identically so a link can
            // never be emitted that a conformant host would reject.
            match Fuaran.UI.Ops.JsonDecode.decodeNodeObj canonical with
            | Error e ->
                Error(RoundTripFailed(sprintf "canonical re-decode failed [%s] at %s: %s" e.Code e.Path e.Message))
            | Ok reDecoded ->
                let reEncoded = Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode reDecoded

                if reEncoded <> canonical then
                    Error(RoundTripFailed "canonical encoding is not a decode/encode fixed point")
                else
                    Ok(node, canonical)

/// Render a failure to a human-readable message that preserves the typed
/// AI-recovery hints (stable code, JSON path, expected shape) — they name the
/// exact repair for a human author just as they do for an AI author.
let describeFailure (failure: ExemplarFailure) : string =
    match failure with
    | DecodeFailed e ->
        let expected =
            match e.ExpectedShape with
            | Some shape -> sprintf " (expected: %s)" shape
            | None -> ""

        sprintf "wire decode failed [%s] at %s: %s%s" e.Code e.Path e.Message expected
    | ValidateFailed defects ->
        defects
        |> List.map (sprintf "%A")
        |> String.concat "; "
        |> sprintf "pre-emit validation failed: %s"
    | RoundTripFailed detail -> sprintf "wire round-trip failed: %s" detail
