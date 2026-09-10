module Fuaran.UI.JsonDecode.Tests.RenderTextFixtures

// ============================================================================
//  The render-TEXT conformance family (Phase 1663) — the authored vectors.
//
//  WHY IT EXISTS. The corpus certifies codec BYTES, and `render-fidelity.json`
//  (§13) declares render OBLIGATIONS — what a kind owes the reader, in
//  sentences a machine can name but not evaluate. Neither says what a slot
//  actually READS. So "N renderers agree on the text" was never mechanically
//  checkable, and five consecutive phases deferred a cross-host parity task for
//  want of a family to check it against.
//
//  A vector is `(fixture, pinned sources, expected text)`:
//    - `Fixture` + `NodeId` + `Slot` name an EXISTING corpus node fixture and
//      the text slot inside it. Naming a committed fixture rather than minting a
//      payload is deliberate: the document is already under byte-parity
//      certification on every host, so this family adds a render claim over
//      bytes that are already agreed rather than a second document to keep in
//      step.
//    - `Sources` is PINNED — the host instant and the ambient locale are data in
//      the vector, never the machine's clock or locale. That is the whole reason
//      the family is checkable: a `Binding.Now` slot rendered against the real
//      clock has no expected text at all.
//    - `ExpectedText` is the FALLBACK-tier render (§13 tier 2): the
//      deterministic, non-`Intl` text a no-JS reader, a crawler, an email
//      client or a non-browser host receives.
//
//  The vectors are AUTHORED here and PROVEN by the emitter (see
//  `RenderTextArtifact`): it decodes each named fixture, resolves the named
//  slot through the reference resolver under the pinned sources, and REFUSES to
//  write the artefact when the produced text differs from the authored
//  expectation. Same discipline as `LenientFixtures` (§16's normalisation law)
//  and `EnvelopeFixtures` (§15's tolerance law) — the reference host
//  demonstrates the law before every other host is held to it.
// ============================================================================

/// The host-side sources a vector pins. The three members of the widened
/// `BindingSources` (Phase 1663's decision): the identity-keyed value map, the
/// host instant, and the ambient locale.
///
/// `Values` is deliberately absent as a field: every seeded vector pins NO host
/// values (each source is `Static` or `Now`), and the artefact writes `values`
/// as an empty object so a reading host's shape is complete. A future vector
/// that needs a host value declares its identity key, and the artefact gains
/// entries there under the §4b lookup rule.
type PinnedSources =
    {
        /// The host instant as an ISO-8601 UTC string, or `""` for "this host
        /// furnishes no clock" — the identity default, which resolves the slot
        /// to absence rather than to a plausible wrong date.
        Now: string
        /// The ambient BCP-47 locale tag a `LocaleSource.Ambient` reads, or `""`
        /// for the runtime default.
        Locale: string
    }

/// One `(fixture, pinned sources, expected text)` vector.
type Vector =
    {
        Id: string
        /// Corpus-relative path of the node fixture this vector reads, e.g.
        /// `nodes/now-grain.json`.
        Fixture: string
        /// The `id` of the node INSIDE that fixture whose slot is read. The
        /// fixture root itself is admissible (its own id).
        NodeId: string
        /// `<kind>.<field>`, from the family's closed slot vocabulary — see
        /// `RenderTextArtifact.slotVocabulary`. A slot the vocabulary does not
        /// carry is refused rather than guessed at.
        Slot: string
        Sources: PinnedSources
        ExpectedText: string
        Description: string
    }

/// A slot the family deliberately does NOT pin, with the reason. This is the
/// §13 "legitimately-differing tier" made explicit: an `Intl`-backed host and a
/// stdlib-only host produce different bytes for the same input and BOTH are
/// correct, so a cross-host text comparison over these slots would measure the
/// locale database rather than the contract.
type Excluded =
    {
        Slot: string
        /// A corpus site that carries the slot, so a reader can see the shape
        /// the family is declining to pin rather than take the exclusion on
        /// trust.
        Fixture: string
        NodeId: string
        Reason: string
    }

/// The pinned instant the `Binding.Now` vectors share. A fixed literal, not
/// `DateTime.UtcNow` — see the header.
[<Literal>]
let private nowInstant = "2026-08-02T06:59:24Z"

/// `nodes/format-since.json`'s two `Since` bindings both read a `Static`
/// source of 1700000000 epoch seconds — `2023-11-14T22:13:20Z`. Every `Since`
/// vector below pins its own `now` relative to THAT instant, so the delta each
/// one exercises is readable from the pair.
[<Literal>]
let private sinceSourceInstant = "2023-11-14T22:13:20Z"

let private noClock = { Now = ""; Locale = "" }

let all: Vector list =
    [
      // ── Binding.Now, one vector per grain (§4b) ────────────────────────────
      { Id = "now-grain-second"
        Fixture = "nodes/now-environment-binding.json"
        NodeId = "today-fact"
        Slot = "Fact.value"
        Sources = { Now = nowInstant; Locale = "" }
        ExpectedText = nowInstant
        Description =
          "a grain-less `Binding.Now` reads the host instant verbatim — `Second` is the identity grain, so no truncation" }
      { Id = "now-grain-minute"
        Fixture = "nodes/now-grain.json"
        NodeId = "asof-minute"
        Slot = "Fact.value"
        Sources = { Now = nowInstant; Locale = "" }
        ExpectedText = "2026-08-02T06:59:00Z"
        Description = "grain `Minute` truncates the seconds field to `00` and keeps the `Z`" }
      { Id = "now-grain-hour"
        Fixture = "nodes/now-grain.json"
        NodeId = "asof-hour"
        Slot = "Fact.value"
        Sources = { Now = nowInstant; Locale = "" }
        ExpectedText = "2026-08-02T06:00:00Z"
        Description = "grain `Hour` truncates minutes and seconds" }
      { Id = "now-grain-day"
        Fixture = "nodes/now-grain.json"
        NodeId = "asof-day"
        Slot = "Fact.value"
        Sources = { Now = nowInstant; Locale = "" }
        ExpectedText = "2026-08-02"
        Description = "grain `Day` truncates to the bare civil date — the form a day-delta over the instant reads" }
      { Id = "now-no-host-clock"
        Fixture = "nodes/now-environment-binding.json"
        NodeId = "today-fact"
        Slot = "Fact.value"
        Sources = noClock
        ExpectedText = ""
        Description =
          "a host that furnishes no instant resolves the slot to ABSENCE, which a text slot renders as the empty string — never an invented date" }

      // ── Format.Since (§4b), the fallback-tier phrasing ────────────────────
      { Id = "since-auto-unit-past"
        Fixture = "nodes/format-since.json"
        NodeId = "since-auto"
        Slot = "Markdown.text"
        Sources =
          { Now = "2023-11-15T01:13:20Z"
            Locale = "" }
        ExpectedText = "3 hours ago"
        Description =
          "`Since` with no declared unit auto-selects from the §4b threshold ladder: a 10800-second past delta lands on `Hour`, and the count truncates toward zero" }
      { Id = "since-auto-unit-future"
        Fixture = "nodes/format-since.json"
        NodeId = "since-auto"
        Slot = "Markdown.text"
        Sources =
          { Now = "2023-11-14T21:13:20Z"
            Locale = "" }
        ExpectedText = "in 1 hour"
        Description =
          "a source AHEAD of the host instant is a positive delta and reads forward; the sign convention is `Intl.RelativeTimeFormat`'s, so negative is the past" }
      { Id = "since-auto-unit-zero"
        Fixture = "nodes/format-since.json"
        NodeId = "since-auto"
        Slot = "Markdown.text"
        Sources =
          { Now = sinceSourceInstant
            Locale = "" }
        ExpectedText = "this second"
        Description =
          "a zero delta is `this second` at the auto-selected `Second` unit — a distinct rendering from absence, which is the point of pinning it" }
      { Id = "since-declared-unit"
        Fixture = "nodes/format-since.json"
        NodeId = "since-declared-hour"
        Slot = "Markdown.text"
        Sources =
          { Now = "2023-11-16T22:13:20Z"
            Locale = "" }
        ExpectedText = "48 hours ago"
        Description =
          "a DECLARED unit overrides the ladder: two days at unit `Hour` is 48 hours, not 2 days. The binding's `LocaleSource.Explicit \"en-GB\"` is pinned by the fixture and does not change the fallback text, which is exactly what makes this vector locale-independent" }
      { Id = "since-no-host-clock"
        Fixture = "nodes/format-since.json"
        NodeId = "since-auto"
        Slot = "Markdown.text"
        Sources = noClock
        ExpectedText = ""
        Description =
          "no host instant means no delta to render, so the slot resolves to ABSENCE — never the raw epoch source, which reads as a confidently wrong answer" }

      // ── Format.RelativeTime (§4b) — the same fallback tier as `Since`, and
      //    the vector that shows the tier is about the PHRASING step and not
      //    about the host clock: this one reads its signed count straight off
      //    the source and consults no instant at all.
      { Id = "relative-time-declared-unit"
        Fixture = "nodes/format-bindings.json"
        NodeId = "fmt-relative"
        Slot = "Markdown.text"
        Sources = noClock
        ExpectedText = "3 days ago"
        Description =
          "`RelativeTime` reads a signed count of its declared unit directly from the source, so it needs no host instant; the fixture's `LocaleSource.Explicit \"en-US\"` does not change the fallback text" } ]
    |> List.sortWith (fun a b -> System.String.CompareOrdinal(a.Id, b.Id))

let excluded: Excluded list =
    [ { Slot = "Format.Number"
        Fixture = "nodes/format-bindings.json"
        NodeId = "fmt-number"
        Reason =
          "the digit grouping and decimal separator come from the locale database: `Intl.NumberFormat` on a browser host, `CultureInfo` on .NET, a hand-rolled fixed-point form on a stdlib-only host. All three are correct for their target and none is canonical" }
      { Slot = "Format.Currency"
        Fixture = "nodes/format-bindings.json"
        NodeId = "fmt-currency"
        Reason =
          "the symbol, its placement and the negative form are all CLDR data; a stdlib-only host can only emit the ISO code beside the amount" }
      { Slot = "Format.Percent"
        Fixture = "nodes/format-bindings.json"
        NodeId = "fmt-percent"
        Reason =
          "same locale database as `Format.Number`, plus the locale-specific position of the percent sign and the space before it" }
      { Slot = "Format.Date"
        Fixture = "nodes/format-bindings.json"
        NodeId = "fmt-date"
        Reason =
          "a date STYLE (`Short` / `Medium` / `Long` / `Full`) names a locale's own pattern, so the rendered text is the locale database's answer by construction" } ]
    |> List.sortWith (fun a b -> System.String.CompareOrdinal(a.Slot, b.Slot))
