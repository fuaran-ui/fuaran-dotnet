module Fuaran.UI.Catalog.Tests.CatalogRoundTripTests

// Phase 169 — build-time guard for the public component-reference catalog.
//
// Each catalog card displays the canonical wire JSON of the SAME `Node<unit>`
// it renders (`CanonicalJson.encodeNode`). The catalog's value as a by-example
// companion to WIRE_FORMAT.md and the Phase 110 authoring pack depends on that
// JSON being *real* — it must decode back through the canonical decoder. This
// suite encodes every catalog matrix entry across the full
// (Tone × Weight × Emphasis) sweep and asserts the bytes round-trip through
// `Ops.JsonDecode.decodeNode`.
//
// It compile-links the catalog's `Matrix.fs` directly (the same source the
// gallery projects), so the guard tracks the catalog automatically — there is
// no hand-maintained fixture list to drift.

open Expecto
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.Ops
open Fuaran.Samples.Catalog

[<Tests>]
let catalogRoundTripTests =
    testList
        "Catalog wire-JSON round-trip (Phase 169)"
        [ for entry in Matrix.entries ->
              test entry.Id {
                  let failures =
                      [ for tone in Matrix.allTones do
                            for weight in Matrix.allWeights do
                                for emphasis in Matrix.allEmphases do
                                    let node = entry.Build(tone, weight, emphasis)
                                    let json = CanonicalJson.encodeNode node

                                    match JsonDecode.decodeNode json with
                                    | Ok _ -> ()
                                    | Error e ->
                                        yield
                                            sprintf
                                                "[%s/%s/%s] %s at %s — %s"
                                                (Matrix.toneLabel tone)
                                                (Matrix.weightLabel weight)
                                                (Matrix.emphasisLabel emphasis)
                                                e.Code
                                                e.Path
                                                e.Message ]

                  Expect.isEmpty
                      failures
                      (sprintf "%s: catalog wire JSON failed to round-trip through the canonical decoder" entry.Id)
              } ]
