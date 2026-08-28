module Fuaran.UI.JsonDecode.Tests.CardFixtures

open Fuaran.UI
open Fuaran.UI.Ops.CustomCardJson

// ============================================================================
//  The contract-card fixture family (Phase 1108; WIRE_FORMAT.md §25).
//
//  ITS OWN FAMILY, NOT THE NODE CORPUS. A card is not a node: it never appears
//  inside a tree, it is not addressed by a `NodeId`, and a host that decodes
//  every node fixture in the corpus can still be unable to read one. Folding
//  these into `nodes/` would have made the node family's own claims false — the
//  round-trip law there is stated over `CanonicalJson.encodeNode`, which has
//  nothing to say about this document — and would have quietly changed what
//  every host's node-corpus leg was asserting.
//
//  THE ACCEPT FIXTURES ARE EMITTED, NOT TYPED OUT. Each round-trip payload is
//  produced by this repo's own encoder from a typed card value, so a
//  hand-transcription error cannot make the corpus disagree with the encoder it
//  is supposed to pin. The REJECT payloads are hand-written, necessarily: their
//  whole content is a document the encoder would never produce.
// ============================================================================

/// What the corpus asserts about a fixture.
type CardExpect =
    /// Decode, re-encode, and the bytes must be identical.
    | RoundTrip
    /// Decode must fail with exactly this code at a path starting with this
    /// prefix — the same shape every other reject family in this corpus uses.
    | Refuse of code: string * path: string

type CardFixture =
    {
        /// Corpus filename stem (`cards/<id>.json`) + manifest id.
        Id: string
        /// `"contract-card"` ⇒ the single-card codec, `"contract-card-bundle"` ⇒
        /// the bundle codec. Named on the fixture because the two documents are
        /// different shapes and a host must be told which entry point to use.
        Decoder: string
        Wire: string
        Expect: CardExpect
        Description: string
    }

/// The decoder-named round-trip, for the emitter's own proof and for a host
/// driving the family.
let decodeReencode (decoder: string) (wire: string) =
    match decoder with
    | "contract-card" -> decodeCardJson wire |> Result.map encodeCardJson
    | "contract-card-bundle" -> decodeBundleJson wire |> Result.map encodeBundleJson
    | other -> failwithf "unknown card decoder '%s'" other

// ─── The typed cards the accept fixtures are emitted from ────────────────────

let private prop name typeTag required language gate : CustomPropCard =
    { Name = name
      Type = typeTag
      Required = required
      PayloadLanguage = language
      PayloadGate = gate }

/// The smallest legal card: identity + hash, no props, no summary.
///
/// Summary-less is deliberately the MINIMAL case rather than an edge one. Every
/// contract authored before the card artefact existed declares no summary, so
/// this is the shape most real cards take on the day a deployment first
/// publishes them — and a host that renders identity alone from it is behaving
/// correctly, not degrading twice.
let private minimalCard: CustomKindCard =
    { ModuleId = "analytics"
      ComponentId = "trend-card"
      Props = []
      Hash =
        { Algorithm = "SHA256"
          Hash = "3f786850e387550fdab836ed7e6dc881de23001b" }
      Summary = Option.None }

/// Every declared feature at once: a summary, a required prop, an optional one,
/// an enum, a GATED payload declaration and an UNGATED one — the two payload
/// states being different claims (Phase 1107), so a fixture carrying only the
/// gated one would leave the ungated encoding unpinned.
let private fullCard: CustomKindCard =
    { ModuleId = "analytics"
      ComponentId = "sparkline"
      Props =
        [ prop "series" "string" true (Some "chartspec") (Some "chartspec-gate:1.2")
          prop "annotations" "string" false (Some "annotationdsl") Option.None
          prop "period" "enum(day|week|month)" true Option.None Option.None
          prop "points" "array" true Option.None Option.None
          prop "title" "string" false Option.None Option.None ]
      Hash =
        { Algorithm = "SHA256"
          Hash = "a94a8fe5ccb19ba61c4c0873d391e987982fbbd3" }
      Summary = Some "A compact trend line with a period-over-period delta." }

let private bundleCards = [ fullCard; minimalCard ]

// ─── Accept fixtures ─────────────────────────────────────────────────────────

let roundTrips: CardFixture list =
    [ { Id = "card-minimal"
        Decoder = "contract-card"
        Wire = encodeCardJson minimalCard
        Expect = RoundTrip
        Description = "identity + content hash, no props and no summary (WIRE_FORMAT 25.1)" }

      { Id = "card-full"
        Decoder = "contract-card"
        Wire = encodeCardJson fullCard
        Expect = RoundTrip
        Description =
          "summary, required + optional props, an enum type tag, and both payload states — gated and ungated (WIRE_FORMAT 25.1)" }

      { Id = "card-bundle"
        Decoder = "contract-card-bundle"
        Wire = encodeBundleJson bundleCards
        Expect = RoundTrip
        Description =
          "a two-card bundle, emitted sorted by (moduleId, componentId) whatever order the cards were supplied in (WIRE_FORMAT 25.2)" } ]

// ─── Reject fixtures ─────────────────────────────────────────────────────────
//
// Hand-written, because each one is a document the encoder cannot produce. Keys
// are written in canonical order so a reader diffing a reject against an accept
// sees only the defect.

let rejects: CardFixture list =
    [ { Id = "card-reject-bad-version"
        Decoder = "contract-card"
        Wire =
          """{"$card":"2","componentId":"trend-card","contentHash":{"algorithm":"SHA256","hash":"3f78"},"moduleId":"analytics","props":[]}"""
        Expect = Refuse("UNSUPPORTED_VERSION", "$.$card")
        Description = "a card format version this decoder does not implement (WIRE_FORMAT 25.1)" }

      { Id = "card-reject-stray-key"
        Decoder = "contract-card"
        Wire =
          """{"$card":"1","componentId":"trend-card","contentHash":{"algorithm":"SHA256","hash":"3f78"},"moduleId":"analytics","props":[],"renderer":"./trend-card.js"}"""
        Expect = Refuse("UNDECLARED_FIELD", "$.renderer")
        Description =
          "default-deny by shape: an undeclared key is refused, never ignored — a card is a protocol artefact and its evolution is the version tag (WIRE_FORMAT 25.3)" }

      { Id = "card-reject-missing-module"
        Decoder = "contract-card"
        Wire =
          """{"$card":"1","componentId":"trend-card","contentHash":{"algorithm":"SHA256","hash":"3f78"},"props":[]}"""
        Expect = Refuse("MISSING_FIELD", "$.moduleId")
        Description = "half an identity is not an identity (WIRE_FORMAT 25.1)" }

      { Id = "card-reject-missing-hash"
        Decoder = "contract-card"
        Wire = """{"$card":"1","componentId":"trend-card","moduleId":"analytics","props":[]}"""
        Expect = Refuse("MISSING_FIELD", "$.contentHash")
        Description =
          "the content hash is REQUIRED — without it a card can never be more than a name match, and the three-way verdict collapses to its weakest case for every node (WIRE_FORMAT 25.4)" }

      { Id = "card-reject-hash-not-object"
        Decoder = "contract-card"
        Wire = """{"$card":"1","componentId":"trend-card","contentHash":"3f78","moduleId":"analytics","props":[]}"""
        Expect = Refuse("WRONG_TYPE", "$.contentHash")
        Description =
          "the digest is carried WITH its algorithm; a bare string would make two digests under different algorithms comparable (WIRE_FORMAT 25.1)" }

      { Id = "card-reject-unknown-prop-type"
        Decoder = "contract-card"
        Wire =
          """{"$card":"1","componentId":"trend-card","contentHash":{"algorithm":"SHA256","hash":"3f78"},"moduleId":"analytics","props":[{"name":"series","required":true,"type":"timeseries"}]}"""
        Expect = Refuse("UNKNOWN_DU_CASE", "$.props[0].type")
        Description =
          "a prop type tag this build cannot resolve is refused at the boundary rather than read as permissive — guessing would turn a check into a pass (WIRE_FORMAT 25.3)" }

      { Id = "card-reject-empty-enum"
        Decoder = "contract-card"
        Wire =
          """{"$card":"1","componentId":"trend-card","contentHash":{"algorithm":"SHA256","hash":"3f78"},"moduleId":"analytics","props":[{"name":"period","required":true,"type":"enum()"}]}"""
        Expect = Refuse("UNKNOWN_DU_CASE", "$.props[0].type")
        Description =
          "`enum()` is unrepresentable in the tag spelling — an enum admitting nothing, spelled as though it admitted one empty choice (WIRE_FORMAT 25.1)" }

      { Id = "card-reject-required-not-bool"
        Decoder = "contract-card"
        Wire =
          """{"$card":"1","componentId":"trend-card","contentHash":{"algorithm":"SHA256","hash":"3f78"},"moduleId":"analytics","props":[{"name":"series","required":"yes","type":"string"}]}"""
        Expect = Refuse("WRONG_TYPE", "$.props[0].required")
        Description = "requiredness is a boolean, not a spelling (WIRE_FORMAT 25.1)" }

      { Id = "card-reject-payload-no-language"
        Decoder = "contract-card"
        Wire =
          """{"$card":"1","componentId":"trend-card","contentHash":{"algorithm":"SHA256","hash":"3f78"},"moduleId":"analytics","props":[{"name":"series","payload":{"gate":"chartspec-gate:1.2"},"required":true,"type":"string"}]}"""
        Expect = Refuse("MISSING_FIELD", "$.props[0].payload.language")
        Description =
          "a gate with no language names a judge for nothing; the language is the declaration and the gate is the annotation on it (WIRE_FORMAT 25.1)" }

      { Id = "card-reject-bundle-bad-version"
        Decoder = "contract-card-bundle"
        Wire = """{"$cards":"2","cards":[]}"""
        Expect = Refuse("UNSUPPORTED_VERSION", "$.$cards")
        Description = "a bundle format version this decoder does not implement (WIRE_FORMAT 25.2)" }

      { Id = "card-reject-bundle-duplicate"
        Decoder = "contract-card-bundle"
        Wire =
          """{"$cards":"1","cards":[{"$card":"1","componentId":"trend-card","contentHash":{"algorithm":"SHA256","hash":"3f78"},"moduleId":"analytics","props":[]},{"$card":"1","componentId":"trend-card","contentHash":{"algorithm":"SHA256","hash":"aa01"},"moduleId":"analytics","props":[]}]}"""
        Expect = Refuse("DUPLICATE_CARD", "$.cards[1]")
        Description =
          "two cards for one identity: a document with no order to appeal to, so which description a reader gets would depend on decoder implementation detail (WIRE_FORMAT 25.2)" } ]

let all: CardFixture list = roundTrips @ rejects
