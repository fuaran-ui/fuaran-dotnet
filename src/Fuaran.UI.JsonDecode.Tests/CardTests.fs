module Fuaran.UI.JsonDecode.Tests.CardTests

// ============================================================================
//  Contract-card conformance (WIRE_FORMAT §25) — corpus-driven, plus the
//  value-level suites the corpus cannot express.
//
//  The corpus half re-proves at test time exactly what the emitter proved: a
//  round-trip fixture decodes and re-encodes byte-identically through its
//  `decoder`-named entry point, and a reject surfaces the manifest's code at the
//  manifest's path. The TypeScript host re-proves the same from the same bytes,
//  which is what makes §25 corpus-certified rather than host-private.
//
//  The value-level half is about what a card MEANS, which no byte comparison
//  reaches: the three-way hash verdict, the withholding rule under a mismatch,
//  card-driven prop validation agreeing with contract-driven prop validation,
//  and the invariant that a summary does not move the content hash.
// ============================================================================

open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.CustomCardJson

let private corpusRoot, corpusEntries = Corpus.load ()

let private roundTripEntries =
    corpusEntries |> List.filter (fun e -> e.Kind = "contract-card-round-trip")

let private rejectEntries =
    corpusEntries |> List.filter (fun e -> e.Kind = "contract-card-reject")

let private roundTripCase (e: Corpus.FixtureEntry) : Test =
    testCase (sprintf "Card — %s (%s) — decode/encode matches corpus" e.Description e.Id) (fun () ->
        let wire = Corpus.readPayload corpusRoot e.InputFile

        match CardFixtures.decodeReencode e.Decoder wire with
        | Ok reencoded -> Expect.equal reencoded wire "round-trip preserves canonical-JSON byte form"
        | Error err -> failtestf "decode failed: %A\n  on input: %s" err wire)

let private rejectCase (e: Corpus.FixtureEntry) : Test =
    testCase (sprintf "Card — %s (%s) — refuses with the canonical error" e.Description e.Id) (fun () ->
        let wire = Corpus.readPayload corpusRoot e.InputFile

        match CardFixtures.decodeReencode e.Decoder wire with
        | Error err ->
            Expect.equal err.Code (Option.defaultValue "" e.ExpectedErrorCode) "reject code matches the manifest"

            Expect.isTrue
                (err.Path.StartsWith(Option.defaultValue "$" e.ExpectedPath))
                "reject path starts with the manifest prefix"
        | Ok _ -> failtest "expected a refusal; decode accepted the artefact")

[<Tests>]
let cardRoundTrips =
    roundTripEntries
    |> List.map roundTripCase
    |> testList "Fuaran.UI.Ops.CustomCardJson — round-trip (corpus, §25)"

[<Tests>]
let cardRejects =
    rejectEntries
    |> List.map rejectCase
    |> testList "Fuaran.UI.Ops.CustomCardJson — reject (corpus, §25)"

[<Tests>]
let cardCoverageGate =
    testList
        "Fuaran.UI.Ops.CustomCardJson — corpus coverage gate"
        [ testCase "round-trip corpus has one entry per CardFixtures.roundTrips fixture" (fun () ->
              Expect.equal
                  (List.length roundTripEntries)
                  (List.length CardFixtures.roundTrips)
                  "contract-card-round-trip corpus count must equal CardFixtures.roundTrips — regenerate with `--emit-corpus`")

          testCase "reject corpus has one entry per CardFixtures.rejects fixture" (fun () ->
              Expect.equal
                  (List.length rejectEntries)
                  (List.length CardFixtures.rejects)
                  "contract-card-reject corpus count must equal CardFixtures.rejects — regenerate with `--emit-corpus`")

          testCase "no card fixture leaked into the node corpus" (fun () ->
              // A card is not a node. If one ever lands in `nodes/`, every host's
              // node-corpus leg starts asserting the node round-trip law over a
              // document that law says nothing about — which fails in a way that
              // reads as a codec defect rather than a misfiled fixture.
              let leaked =
                  corpusEntries
                  |> List.filter (fun e -> e.Kind = "node-round-trip" && e.InputFile.StartsWith "cards/")
                  |> List.map _.Id

              Expect.isEmpty leaked "a contract-card fixture is registered as a node round-trip") ]

// ─── What a card MEANS ───────────────────────────────────────────────────────

/// A contract whose props exercise both payload states and a typed enum.
let private sampleContract =
    CustomContract.createWithSchema
        "analytics"
        "sparkline"
        [ { Defaults.propDecl with
              Name = "series"
              Type = PropType.PString
              Required = true
              PayloadLanguage = Some(PayloadLanguage.gated "chartspec" "chartspec-gate" "1.2") }
          { Defaults.propDecl with
              Name = "period"
              Type = PropType.PEnum [ "day"; "week"; "month" ]
              Required = true }
          { Defaults.propDecl with
              Name = "title"
              Type = PropType.PString
              Required = false } ]
        (fun (m: Map<string, JVal>) -> m)
        (fun m -> Ok m)
        (Map.ofList [ "series", JStr ""; "period", JStr "day"; "title", JStr "" ])
        []
        HashStrictness.AdvisoryWarning
    |> function
        | Ok c -> c
        | Error e -> failwithf "sample contract did not construct: %s" e

let private describedContract =
    sampleContract |> CustomContract.withSummary "A compact trend line."

let private registry = CustomRegistry.Empty.Register describedContract

let private card = registry.DescribeForAi() |> List.head

/// A node's declared content-identity envelope. Annotated because
/// `CardContentHash` shares two of its three field names, and the last matching
/// record type wins inference — the ambiguity is worth naming once here rather
/// than at every construction site.
let private declaredHash (algorithm: string) (hash: string) : ContentHash =
    { Algorithm = algorithm
      Hash = hash
      Strictness = HashStrictness.AdvisoryWarning }

let private conformingProps =
    Map.ofList [ "series", JStr "{\"points\":[1,2,3]}"; "period", JStr "week" ]

[<Tests>]
let cardSemantics =
    testList
        "Fuaran.UI.CustomCard — what a card means (§25)"
        [

          // ── The hash does not move ────────────────────────────────────────
          testCase "declaring a summary does not move the content hash" (fun () ->
              // Asserted rather than left to inspection. The hash folds the
              // declared SHAPE; a hash that moved on a reworded sentence would
              // invalidate every StrictReplay consumer of a component whose
              // emitted shape did not change at all.
              Expect.equal
                  describedContract.Hash.Hash
                  sampleContract.Hash.Hash
                  "a summary is a description, not part of the declared shape")

          // ── The registry exports the card ─────────────────────────────────
          testCase "the registry projects the contract into a card that round-trips" (fun () ->
              let wire = encodeCardJson card

              match decodeCardJson wire with
              | Ok back -> Expect.equal back card "a registry-projected card survives the wire unchanged"
              | Error e -> failtestf "a registry-projected card did not decode: %s at %s" e.Code e.Path)

          testCase "the bundle export carries every registered card" (fun () ->
              match decodeBundleJson (exportBundleJson registry) with
              | Ok cards -> Expect.equal cards [ card ] "exportBundleJson publishes the registry's own cards"
              | Error e -> failtestf "the exported bundle did not decode: %s at %s" e.Code e.Path)

          // ── The three-way verdict ─────────────────────────────────────────
          testCase "a matching declared hash yields Matches, and the description is shown" (fun () ->
              let declared = Some(declaredHash card.Hash.Algorithm card.Hash.Hash)

              let described = CustomCard.describe declared conformingProps card

              Expect.equal described.HashVerdict CardHashVerdict.Matches "the digests are equal"
              Expect.equal (CustomCard.verdictMarker described.HashVerdict) "described" "the marker names the verdict"
              Expect.equal described.Summary (Some "A compact trend line.") "the summary is shown"
              Expect.isNonEmpty described.PropLines "the declared prop rows are shown")

          testCase "no declared hash yields Unverified — shown, and SAID to be unverified" (fun () ->
              // Degrading to identity-only here would throw away the common case
              // for no gain: most nodes declare no hash, and a card that matches
              // by name is still the best description anyone has.
              let described = CustomCard.describe None conformingProps card

              match described.HashVerdict with
              | CardHashVerdict.Unverified reason ->
                  Expect.stringContains reason "no content hash" "the reason names what was missing"
              | other -> failtestf "expected Unverified, got %A" other

              Expect.equal (CustomCard.verdictMarker described.HashVerdict) "unverified" "the marker names the verdict"
              Expect.equal described.Summary (Some "A compact trend line.") "the description is still shown")

          testCase "a differing algorithm yields Unverified, not Mismatch" (fun () ->
              // Two digests under different algorithms are not equal and not
              // unequal — they are incomparable, and reporting a MISMATCH would
              // withhold a perfectly good description on the strength of a
              // comparison that was never made.
              let declared = Some(declaredHash "BLAKE3" card.Hash.Hash)

              match (CustomCard.describe declared conformingProps card).HashVerdict with
              | CardHashVerdict.Unverified reason ->
                  Expect.stringContains reason "cannot be compared" "the reason names the incomparability"
              | other -> failtestf "expected Unverified, got %A" other)

          testCase "a differing hash yields Mismatch, and the description is WITHHELD" (fun () ->
              // The load-bearing case. A card that describes a different shape at
              // the same address is not a description of this node, and printing
              // its summary would be precisely the guess §25.4 forbids — a
              // confident wrong description being worse than none.
              let declared =
                  Some(declaredHash card.Hash.Algorithm "0000000000000000000000000000000000000000")

              let described = CustomCard.describe declared conformingProps card

              Expect.equal described.HashVerdict CardHashVerdict.Mismatch "the digests differ"

              Expect.equal
                  (CustomCard.verdictMarker described.HashVerdict)
                  "hash-mismatch"
                  "the marker names the verdict"

              Expect.isNone described.Summary "the summary describes a different shape and is withheld"
              Expect.isEmpty described.PropLines "so do the prop rows"

              Expect.isEmpty
                  described.Validation.Defects
                  "and no validation verdict is offered against a schema that is not this node's"

              // What is NOT withheld: the identity, and the fact of the mismatch.
              // Hiding those would leave a reader with less than the uncarded
              // placeholder gave them.
              Expect.equal described.Label "[fuaran:custom analytics.sparkline]" "the identity is still emitted")

          // ── Card-driven prop validation ───────────────────────────────────
          testCase "a card reaches the same verdict as the contract it describes" (fun () ->
              // The claim a card makes is that it says what the contract says. If
              // the two validators could disagree, that claim would be false —
              // which is why `validateSchema` was lifted out of the registry
              // rather than reimplemented here.
              let malformed = Map.ofList [ "period", JStr "fortnight" ]

              let fromContract =
                  registry.ValidatePropsDetailed("analytics", "sparkline", malformed)

              let fromCard = CustomCard.validate card malformed

              Expect.equal fromCard.Defects fromContract.Defects "identical defects"
              Expect.equal fromCard.Obligations fromContract.Obligations "identical obligations"

              Expect.isNonEmpty fromCard.Defects "…and the malformed bag genuinely is refused")

          testCase "a card surfaces the payload obligation the contract declares" (fun () ->
              let validation = CustomCard.validate card conformingProps

              Expect.isEmpty validation.Defects "the bag satisfies the declared schema"

              match validation.Obligations with
              | [ o ] ->
                  Expect.equal o.Key "series" "the payload prop"
                  Expect.equal o.Kind PayloadObligationKind.GateOwed "a gate is named and did not run here"
              | other -> failtestf "expected exactly one payload obligation, got %A" other)

          testCase "a prop type tag this build cannot resolve is REPORTED, never assumed permissive" (fun () ->
              // A decoded card cannot carry an unknown tag (the decoder refuses
              // it), so this reaches the validator only through a card
              // constructed in-process — which is exactly how a NEWER producer's
              // card would arrive at a consumer that shipped before the tag
              // existed. Resolving it to "anything goes" would silently turn a
              // check into a pass.
              let fromFuture =
                  { card with
                      Props =
                          [ { Name = "series"
                              Type = "timeseries"
                              Required = true
                              PayloadLanguage = None
                              PayloadGate = None } ] }

              let validation = CustomCard.validate fromFuture conformingProps

              Expect.equal validation.Unresolvable [ "series" ] "the unreadable row is named"
              Expect.isEmpty validation.Defects "and no verdict is offered on a row this build cannot read")

          // ── The prop lines say what both hosts must say ───────────────────
          testCase "prop lines carry the type, requiredness and payload tag — and never a value" (fun () ->
              let described = CustomCard.describe None conformingProps card
              let lines = described.PropLines

              Expect.contains
                  lines
                  "series: string (required) [chartspec (gate chartspec-gate:1.2)]"
                  "the gated payload row"

              Expect.contains lines "period: enum(day|week|month) (required)" "the enum row"
              Expect.contains lines "title: string" "an optional row carries no requiredness marker"

              // The node's props are data this host was not asked to interpret;
              // spilling them into a placeholder is an information leak with no
              // legibility gain.
              for line in lines do
                  Expect.isFalse (line.Contains "points") "no prop VALUE reaches a placeholder line")

          testCase "an ungated payload declaration says so loudly" (fun () ->
              Expect.equal
                  (CustomCard.payloadTag (Some "annotationdsl") None)
                  (Some "annotationdsl (NO GATE)")
                  "a reader must not have to notice a missing parenthetical to learn nothing judges the payload")

          // ── The store ─────────────────────────────────────────────────────
          testCase "a store answers only for identities it holds" (fun () ->
              let store = CustomCardStore.ofCards [ card ]

              Expect.isSome (store.TryFind("analytics", "sparkline")) "the held identity"
              Expect.isNone (store.TryFind("analytics", "trend-card")) "an identity it does not hold"

              Expect.isNone
                  (CustomCardStore.Empty.TryFind("analytics", "sparkline"))
                  "and an empty store answers for nothing, which is what keeps the uncarded path unchanged") ]
