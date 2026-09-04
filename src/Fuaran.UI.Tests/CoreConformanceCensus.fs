module Fuaran.UI.Tests.CoreConformanceCensus

// Phase 1475 — the Core-conformance census in the UI tier.
//
// The 2026-09-03 sweep of the conformance kit found that most of its law families run only in
// Core's own suite: the UI tier, Core's largest consumer, reaches a handful of them and all from
// one file (`CoreAdoptionTests.fs`). That gap was unmeasured until a sweep happened to look at it,
// which is the defect this file exists to close — not the gap itself, but its INVISIBILITY.
//
// So this is a census, in the same shape as Core's own (`SampleAdequacyTests.censusTests`, from
// fuaran-core#121) and deliberately its mirror image. It enumerates every public law family the
// PINNED `Fuaran.Core.Conformance` ships — by reflection, never by a list, because a list is
// exactly what cannot notice a family nobody added to it — and resolves each to either an adopter
// in this repo or a stated reason for not adopting it. A family that is neither fails the suite.
//
// Both directions are checked, and the second one is the load-bearing half:
//
//   * roster -> census: a family the pinned kit ships with no row is red. This is what makes a
//     family Core ADDS arrive here as a failing row rather than silently.
//   * census -> roster: a row naming a family the kit no longer ships is red, because a row for a
//     renamed or removed family reads as coverage while covering nothing.
//   * census -> tests: an `Adopted` row naming a test that no longer exists is red.
//   * tests -> census: a test that runs a Core law family with no `Adopted` row is red. Enrolment
//     by NAME alone is the failure mode Core's own narrowing tripped on 2026-09-03 — a string
//     declaration and the code it describes drift apart silently — so enrolment here is by name
//     AND by reflection, and the two are cross-checked against each other.
//
// The rendered table is committed at `docs/core-conformance.md` so adoption is greppable without
// running anything. The suite regenerates that file and asserts the committed copy already
// matched, so a stale doc is red and a fresh checkout regenerates it.

open System
open System.IO
open System.Reflection
open System.Text
open System.Text.RegularExpressions
open Expecto

// ---------------------------------------------------------------------------
//  the closed classification
// ---------------------------------------------------------------------------

/// How this repo answers for one law family of the pinned kit. The set is CLOSED on purpose: a
/// free-text "unassigned" would let a family be filed away without anyone deciding anything, which
/// is the state this census exists to make impossible.
type Adoption =
    /// A test in this repo runs the family. `test` is the Expecto test-case name (which must still
    /// exist); `port` is the kit entry point through which it is reached — several families are
    /// reached through an aggregate (`Conformance.certify`) rather than by their own name.
    | Adopted of test: string * port: string
    /// The tier does not use the mechanism the family certifies. The reason names the mechanism, so
    /// the next reader can CHECK the classification rather than trust it.
    | NotUsed of mechanism: string
    /// The family belongs to a sibling host of the wire format, not to this one.
    | SiblingHost of host: string
    /// A named roadmap phase carries the enrolment; it flips to `Adopted` when that phase ships.
    | CarriedBy of phase: string

// ---------------------------------------------------------------------------
//  the census
// ---------------------------------------------------------------------------

/// The three test cases in `CoreAdoptionTests.fs` that reach a law family today. Named once so a
/// row and the test it enrols cannot drift apart by a typo.
let private certifyTest =
    "the Fuaran.UI witness certifies end-to-end via the unified Conformance.certify"

let private hashFnTest =
    "the portable SHA-256 certifies under Core's hashFnLaws (the supply-your-own-crypto contract)"

/// The persistence families (fuaran#1477) are enrolled from a DIFFERENT test project —
/// `Fuaran.UI.OpStream.Tests`, where the durable ports they are about live. The census already
/// discovers every `*.Tests` project whose `.fsproj` references the kit, and checks each `Adopted`
/// row against the project that actually runs the family, so no convention needed extending for
/// this; the names are declared here beside the others so all enrolment reads from one place.
let private casTest = "casLaws certifies over the Fuaran.UI op-stream witness"

let private idempotencyTest =
    "idempotencyLaws certifies over the Fuaran.UI op-stream witness"

let private snapshotTest =
    "snapshotLaws and snapshotLawsWith certify over the Fuaran.UI op-stream witness"

/// The multi-writer families adopted by fuaran#1476, which run in
/// `Fuaran.UI.OpStream.Dag.Tests/CoreDagLawTests.fs` over that tier's own tree witness, op
/// algebra and footprint projection. Named once each, for the same reason the two above are:
/// a row and the test it enrols must not drift apart by a typo.
let private dagLawsTest = "the UI op-stream witness certifies under Core's dagLaws"

let private laneFoldTest =
    "N-lane folding is arrival-order-invariant under Core's laneFoldLaws"

let private laneFoldWithTest =
    "lane folding survives the host hash swap under laneFoldLawsWith"

let private footprintTest =
    "the tier's tree footprints are sound, monotone and deterministic (footprintLaws)"

let private mergeConflictTest =
    "merge-conflict reporting is symmetric, deterministic and complete (mergeConflictLaws)"

let private reconcileTest =
    "two-branch reconciliation is order-pinned and conflict-honest (reconcileLaws)"

let private concurrencyTest =
    "independent op pairs interleave confluently (concurrencyLaws)"

let private concurrencyWithTest =
    "the TIER's own footprint projection is confluent (concurrencyLawsWith)"

let private arbitrationTest =
    "proposal arbitration partitions totally and confluently (arbitrationLaws)"

/// The incremental / propagation families (fuaran#1479), enrolled from a THIRD test project —
/// `Fuaran.UI.ServerDriven.Tests`, where the live-Transform seam they are about lives. All four are
/// SELF-CONTAINED: each takes a `(seed, iterations)` pair and draws Core's own tables, pipelines and
/// edit streams, so a green row is evidence that the PINNED KIT's contract holds here, and says
/// nothing on its own about the tier. Each row's port therefore also names the tier-shaped test that
/// states the same property over the real live path — the two are read together or neither is worth
/// much.
let private incrementalDeltaTest =
    "IncrementalDelta.laws certifies the incremental seam at the kit's shipped row bound"

let private incrementalDeltaWithTest =
    "IncrementalDelta.lawsWith certifies at the tier's own live-grid row bound"

let private incrementalTest =
    "incrementalLaws certifies change-driven and op-driven equivalence at this pin"

let private dirtyPropagationTest =
    "dirtyPropagationLaws certifies the propagation seam's cone at this pin"

/// The artifact-function families adopted by fuaran#1478, which run in
/// `Fuaran.UI.FastPath.Tests/CoreFunctionLawTests.fs`. Named once each for the same reason the
/// rows above are: a row and the test it enrols must not drift apart by a typo.
let private capabilityTest =
    "the invocable-capability contract certifies under Core's capabilityLaws"

let private registryTest =
    "the signature-typed function registry certifies under Core's registryLaws"

let private packLoadingTest =
    "content-pack loading certifies under Core's packLoadingLaws"

let private paramTest =
    "parameterised-query binding certifies under Core's paramLaws"

let private deferredTest =
    "the Deferred value codec certifies under Core's deferredLaws"

let private compositionTest =
    "FastPath artifact-functions compose hygienically under Core's compositionLaws"

let private functionVerifyTest =
    "a sound and a broken FastPath pattern certify under Core's functionVerifyLaws"

let private verifyHonestyTest =
    "verification over FastPath patterns claims structure only (verifyHonestyLaws)"

let private memoTest = "FastPath application memoises soundly under Core's memoLaws"

let private memoSoundnessTest =
    "an under-declared FastPath function is never cached (memoSoundnessLaws)"

let private encoderInjectivityTest =
    "the FastPath memo-key encoder is collision-free (encoderInjectivityLaws)"

/// The attributed / attestation families (fuaran#1480), enrolled from `Fuaran.UI.OpStream.Tests`
/// beside the persistence families above and over the SAME witness those use — the attestation one
/// additionally over the tier's own claim minting and ECDSA keyring, adapted to Core's
/// `IAttestationSink`.
let private attributedTest =
    "attributedLaws certifies over the Fuaran.UI op-stream witness"

let private attestationTest =
    "attestationLaws certifies over the tier's claim minting and ECDSA keyring"

let private vacuityTest =
    "noAttestationVacuityLaws certifies that the un-attested default proves nothing"

/// The columnar families adopted by fuaran#1481, which run in `CoreAdoptionTests.fs` beside the
/// tree/op-stream adoption above — in the appended `Columnar` module, whose header carries the
/// self-contained-vs-tier-shaped split these rows summarise. Named once each for the same reason
/// every block above does: a row and the test it enrols must not drift apart by a typo.
let private columnarOpTest =
    "the columnar op algebra certifies under Core's columnarOpLaws"

let private columnarValidatorTest =
    "the columnar validator certifies under Core's columnarValidatorLaws"

let private aggregateParityTest =
    "aggregate parity certifies under Core's aggregateParityLaws"

let private schemaWalkTest =
    "static output-schema derivation certifies under Core's schemaWalkLaws"

/// One row per public law family of the pinned kit. Order here is authoring order (grouped by
/// classification); the report sorts by family key, so this list may be reordered freely.
let census: (string * Adoption) list =
    [
      // ---- adopted today: CoreAdoptionTests.fs ----
      // `Conformance.certify` runs witnessLaws, then (on a well-formed witness) opAlgebra, diffLaws
      // and streamLaws. Only three of those four are roster families — `opAlgebra` is not
      // `*Laws`-suffixed and so is not a family under the shared predicate.
      "Conformance.witnessLaws", Adopted(certifyTest, "Conformance.certify")
      "Conformance.diffLaws", Adopted(certifyTest, "Conformance.certify")
      "Conformance.streamLaws", Adopted(certifyTest, "Conformance.certify (also Conformance.certifyStream)")
      "Conformance.hashFnLaws", Adopted(hashFnTest, "Conformance.hashFnLaws")
      "Conformance.hashFnAdversarialLaws", Adopted(hashFnTest, "Conformance.hashFnAdversarialLaws")

      // ---- adopted by fuaran#1476: Fuaran.UI.OpStream.Dag.Tests/CoreDagLawTests.fs ----
      // Two shapes, and the difference decides what each green row means. `dagLaws` and the two
      // `laneFold` forms are parameterised by a StreamWitness — this tier's `Ops.Apply.apply`
      // reducer and its canonical op codec — driven through CORE's `Dag`; they certify the
      // tier's reducer, codec and footprint under multi-writer folding, and say nothing about
      // `Fuaran.UI.OpStream.Dag.Merge`, which `MergeTests` / `MergeConformanceTests` cover
      // directly. The skeleton-op families are parameterised by the tier's NodeWitness /
      // IdWitness / OpGen and carry no persistence at all, so each runs once rather than
      // per-port; the port question they can pose — does an op survive each sink's codec seam —
      // is answered against the real sinks in the same file.
      "Conformance.dagLaws", Adopted(dagLawsTest, "Conformance.dagLaws")
      "FoldConfluence.laneFoldLaws", Adopted(laneFoldTest, "FoldConfluence.laneFoldLaws")
      // The `With` form is adopted on its own terms rather than as a second spelling: its
      // parameter is the `HashFn`, and it is instantiated with the tier's shipped SHA-256 where
      // the defaulted form takes the kit's FNV-1a. Node ids are content hashes of
      // (parents, actor, op), so the hash decides whether two lanes carrying the same ops stay
      // distinct chains.
      "FoldConfluence.laneFoldLawsWith", Adopted(laneFoldWithTest, "FoldConfluence.laneFoldLawsWith")
      "Conformance.mergeConflictLaws", Adopted(mergeConflictTest, "Conformance.mergeConflictLaws")
      "Conformance.reconcileLaws", Adopted(reconcileTest, "Conformance.reconcileLaws")
      "Conformance.concurrencyLaws", Adopted(concurrencyTest, "Conformance.concurrencyLaws")
      // Likewise not a second spelling: the `With` form takes the footprint projection, and is
      // instantiated with the TIER's own `TreeOp` address-set function rather than Core's. Its
      // reach is the structural five (the law's generator emits skeleton ops); the vertical
      // half of that projection is certified by `laneFoldLaws`, which folds real `TreeOp` lanes.
      "Conformance.concurrencyLawsWith", Adopted(concurrencyWithTest, "Conformance.concurrencyLawsWith")
      "Conformance.arbitrationLaws", Adopted(arbitrationTest, "Conformance.arbitrationLaws")
      // Reassigned to 1476 from 1479 at driver direction. It is a TREE-op law over exactly the
      // NodeWitness / IdWitness / OpGen the DAG phase already constructs, where 1479's subject
      // is the incremental (DataFrame) footprint — a different thing that happens to share a
      // word.
      "Conformance.footprintLaws", Adopted(footprintTest, "Conformance.footprintLaws")

      // ---- fuaran#1477 — persistence laws over the tier's op-stream witness ----
      // All four run in `Fuaran.UI.OpStream.Tests`, beside the durable ports they are about. Note
      // what the ROW claims and what it does not: these families are parameterised over a
      // `StreamWitness` (Apply / Encode / Decode) and Core owns the append, so the adoption is over
      // the tier's reducer, op codec, chain digest and node encoder — NOT over `IOpStreamSink`.
      //
      // fuaran#1485 gave the ports the two contracts these families name (`IOpStreamCasSink` /
      // `IOpStreamKeyedSink`, both stores), so the store side is no longer a gap the same file
      // pins as negatives — it is a set of store-shaped tests, and the two ports below cite them
      // beside the reducer-level run. The distinction still holds and is why both are named: the
      // law certifies Core's append over the tier's witness, and the store-shaped test certifies
      // the tier's own durable ports through the new surface. Neither substitutes for the other.
      "Conformance.casLaws",
      Adopted(
          casTest,
          "Conformance.casLaws — beside the store-shaped 'both durable stores refuse a stale-head append with a typed StaleHead naming the actual head' and 'the store-shaped compare-and-append and keyed-append laws hold over both durable stores' (fuaran#1485)"
      )
      "Conformance.idempotencyLaws",
      Adopted(
          idempotencyTest,
          "Conformance.idempotencyLaws — beside the store-shaped 'both durable stores return the same receipt for a re-sent keyed append and persist nothing the second time' and 'the store-shaped compare-and-append and keyed-append laws hold over both durable stores' (fuaran#1485)"
      )
      "Conformance.snapshotLaws", Adopted(snapshotTest, "Conformance.snapshotLaws")
      "Conformance.snapshotLawsWith", Adopted(snapshotTest, "Conformance.snapshotLawsWith")

      // ---- adopted by fuaran#1478: Fuaran.UI.FastPath.Tests/CoreFunctionLawTests.fs ----
      // Two shapes again, and the difference decides what each green row means — the same
      // distinction the 1476 block draws, arriving here as a sharper one because five of these
      // families are SELF-CONTAINED `(seed, iterations)` entry points that run over Core's own
      // types and cannot see this tier at all.
      //
      // PARAMETERISED — `compositionLaws`, `functionVerifyLaws`, `verifyHonestyLaws`, `memoLaws`,
      // `memoSoundnessLaws` and `encoderInjectivityLaws` are instantiated over the FastPath seam's
      // own artifact algebra (`CoreLawSupport.witness`: a bank `Pattern`'s Core `HoleDecl`s, the
      // hole values bound into it, and the sub-functions composed into its slots), with the tier's
      // OWN egress gate as the validity oracle — `PreEmitValidate.validate`, the check
      // `FastPath.tryInstantiate` runs — and the tier's own memo-key encoder. A defect in the seam
      // fails these.
      //
      // SELF-CONTAINED — `registryLaws`, `capabilityLaws`, `packLoadingLaws`, `paramLaws` and
      // `deferredLaws` certify the PINNED KIT's contract on this machine at this pin, which is
      // real evidence and is not evidence about the tier. The port names each family itself, so
      // the row does not overstate what it reaches. Two of the five certify a mechanism the seam
      // genuinely uses (`FastPath.bank` mints a `Capability` per pattern and registers it in a
      // Core `FunctionRegistry`; `FastPath.find` IS `findBySignature`), and for those the same
      // project asserts the property directly over `SeedCatalogue.defaultBank` rather than
      // wrapping the law to look tier-shaped. The other three have no call site in this tier at
      // all — no content pack, no query parameter, no `Deferred` — and that is said in the test
      // bodies rather than left to be inferred from a green row.
      "Conformance.registryLaws", Adopted(registryTest, "Conformance.registryLaws")
      "Conformance.capabilityLaws", Adopted(capabilityTest, "Conformance.capabilityLaws")
      "Conformance.memoLaws", Adopted(memoTest, "Conformance.memoLaws")
      "Conformance.memoSoundnessLaws", Adopted(memoSoundnessTest, "Conformance.memoSoundnessLaws")
      "Conformance.functionVerifyLaws", Adopted(functionVerifyTest, "Conformance.functionVerifyLaws")
      "Conformance.verifyHonestyLaws", Adopted(verifyHonestyTest, "Conformance.verifyHonestyLaws")
      "Conformance.compositionLaws", Adopted(compositionTest, "Conformance.compositionLaws")
      "Conformance.packLoadingLaws", Adopted(packLoadingTest, "Conformance.packLoadingLaws")
      "Conformance.paramLaws", Adopted(paramTest, "Conformance.paramLaws")
      "Conformance.deferredLaws", Adopted(deferredTest, "Conformance.deferredLaws")
      // Not named by 1478's task list, assigned to it because it is the precondition of the memo
      // families that phase carries: `applyMemo`'s content-addressed key is a tree encoding, and a
      // non-injective encoder makes the cache serve the WRONG tree. Adopted over the encoder the
      // FastPath memo families here actually pass to `applyMemo`, which is the encoder whose
      // injectivity those two rows silently depend on — the same file, the same witness, so the
      // precondition and the thing it conditions cannot drift apart.
      "Conformance.encoderInjectivityLaws", Adopted(encoderInjectivityTest, "Conformance.encoderInjectivityLaws")

      // ---- fuaran#1479 — footprint and delta laws over the live-transform seam ----
      // `Conformance.footprintLaws` was listed here and is adopted by 1476 instead — see the
      // note beside it in that block.
      //
      // All four run in `Fuaran.UI.ServerDriven.Tests/CoreIncrementalLawsTests.fs`, beside the
      // live-Transform seam they are about. Read what these rows claim and what they do NOT.
      // Unlike 1476's and 1477's, these families are SELF-CONTAINED — `(seed, iterations)` and
      // nothing else, over Core's own tables and edit streams — so the green run certifies the
      // PINNED KIT on this machine, not this tier's wiring. That is why each port names a
      // tier-shaped test beside the family: those state the same three properties over the real
      // `LiveTransformStore` path (`Incremental.primeOn` / `refreshOn`), where a defect in the
      // tier is what turns them red. A row that named only the family would be honest about what
      // ran and misleading about what it proved.
      "IncrementalDelta.laws",
      Adopted(
          incrementalDeltaTest,
          "IncrementalDelta.laws — beside the tier-shaped 'the refresh answers what a full evaluation over the changed source answers'"
      )
      // Adopted on its own terms rather than as a second spelling: its parameter is the table-WIDTH
      // bound, run at 12 to span this tier's generated grids (1..12 rows) and the seven recorded
      // corpus vectors (6 rows each), where the shipped bound of 9 spans neither's top end. Its
      // go-red proof is in the same file and is the one the kit's own doc comment names — narrowing
      // the bound to 1, measured red on 30/30 seeds.
      "IncrementalDelta.lawsWith",
      Adopted(
          incrementalDeltaWithTest,
          "IncrementalDelta.lawsWith — beside the tier-shaped 'the refresh evaluates no more rows than a full evaluation, on one scale'"
      )
      "Conformance.dirtyPropagationLaws",
      Adopted(
          dirtyPropagationTest,
          "Conformance.dirtyPropagationLaws — beside the tier-shaped 'the dirty cone over the binding walk is sound and minimal on generated binding sets'"
      )
      // Not named by 1479's task list. The tier does incremental dataframe evaluation through
      // `Incremental.primeOn` / `refreshOn` (`ServerDriven/LiveTransform.fs`), which is the seam
      // 1479 certifies; this family is the equivalence claim over that evaluation, so it belongs
      // with the phase that owns the seam rather than being called unused. Adopted on that reading
      // rather than reclassified.
      "Conformance.incrementalLaws",
      Adopted(
          incrementalTest,
          "Conformance.incrementalLaws — beside the tier-shaped 'the seven corpus vectors obey the one-scale bound, declined ones included'"
      )

      // ---- fuaran#1480 — attributed and attestation laws over the attributed op-stream ----
      // All three run in `Fuaran.UI.OpStream.Tests`, over the witness fuaran#1477 built. Read what
      // each row claims. `attributedLaws` and `noAttestationVacuityLaws` are parameterised by the
      // witness and the digest alone, so they certify the tier's reducer, op codec and SHA-256
      // chain under Core's attribution lift and under the un-attested default. `attestationLaws`
      // additionally takes an `IAttestationSink`, and the one supplied is not a double: it mints the
      // tier's canonical `SegmentAttestation.claimPayload`, signs through the tier's BCL ECDSA
      // P-256 signer, and verifies through the tier's crypto verifier against its key directory.
      // What it does NOT cover is the descriptor's range / anchor fields — Core's seam signs an
      // opaque head, so those are pinned by a fixed claim shell and are covered on their own terms
      // by `AttestationTests.fs` (`RangeMismatch`, `ChainBroken` off a signed anchor).
      "Conformance.attributedLaws", Adopted(attributedTest, "Conformance.attributedLaws")
      "Conformance.attestationLaws", Adopted(attestationTest, "Conformance.attestationLaws")
      "Conformance.noAttestationVacuityLaws", Adopted(vacuityTest, "Conformance.noAttestationVacuityLaws")

      // ---- fuaran#1481 — columnar laws over the tier's Column usage ----
      // All four run in `CoreAdoptionTests.fs` (the `Columnar` module appended
      // there), and ALL FOUR ARE SELF-CONTAINED `(seed, iterations)` in the pinned
      // kit — they build their own Core tables and pipelines and take no consumer
      // witness, so each row claims exactly what the 1478 block's self-contained
      // half claims: the PINNED KIT's contract holds on this machine at this pin.
      // The port names each family itself, so no row overstates its reach.
      //
      // What the tier actually carries, since the phase text assumed otherwise:
      // there is no columnar op-algebra call site here at all and no Core columnar
      // validator registration — and the one production `Column.create` authoring
      // surface is `RetrievalSource.Hit.toTable`. (The schema walk was the third
      // such absence until Phase 1486 gave FUARAN114 and FUARAN086 a call site in
      // `PreEmitValidate.fs`; the row below still enrols the LAW, which is about
      // the pinned kit, not about that call site.)
      // So beside each law run the same file asserts the family's property
      // DIRECTLY over the surface the tier does have, rather than wrapping the law
      // to look tier-shaped: the authoring surface's columns are well-formed
      // against the schema it declares; aggregating a column THAT surface built
      // equals the single-group GroupBy; the tier's own columnar-validation rule
      // (FUARAN114, Phase 1149/1486) is deterministic and reports exactly the
      // ungrounded names; and the schema walk agrees both with that rule's window
      // and with the schema `QueryRefine.refineLocally` produces for the pipelines
      // the rule judges. Those four are tests in their own right, not
      // second spellings of these rows — a row names the law it enrols.
      "Conformance.columnarOpLaws", Adopted(columnarOpTest, "Conformance.columnarOpLaws")
      "Conformance.columnarValidatorLaws", Adopted(columnarValidatorTest, "Conformance.columnarValidatorLaws")
      "Conformance.aggregateParityLaws", Adopted(aggregateParityTest, "Conformance.aggregateParityLaws")
      "Conformance.schemaWalkLaws", Adopted(schemaWalkTest, "Conformance.schemaWalkLaws")

      // ---- a sibling host's family ----
      "Conformance.captureReplayLaws", SiblingHost "fuaran-ts / fuaran-go (fuaran#1482)"

      // ---- mechanisms this tier does not use ----
      "Conformance.propagationEvalLaws",
      NotUsed
          "Fuaran.Core.Propagation's evaluator — the tier's reactivity runs on its own store and subscription channels (StateStore / FilterStore / SelectionStore / QueryStore) and never evaluates a Core propagation graph"
      "Conformance.leaseLaws",
      NotUsed
          "Fuaran.Core.Lease — leases are a coordination-plane mechanism for concurrent writers; the UI tier takes none"
      "Conformance.aiSurfaceLaws",
      NotUsed
          "Fuaran.Core.AiSurface — the tier ships its own runtime introspection surface (Fuaran.UI.AiTools) and consumes no Core AI surface"
      "Conformance.projectionLaws",
      NotUsed "Fuaran.Core.Projection — the tier renders a tree; it maintains no Core projection over an op stream"
      "Conformance.queryLaws",
      NotUsed
          "Fuaran.Core.Query's registry seam — QuerySource.fs is a deliberately thinner UI-facing sibling built on the Column / DataFrame types and explicitly NOT on the Core query registry, which no project here references"
      "Conformance.capabilityPipelineLaws",
      NotUsed "Fuaran.Core.Function's CapabilityPipeline — the tier composes no capability pipeline"
      "Conformance.capabilityPipelineIncrementalLaws",
      NotUsed "Fuaran.Core.Function's CapabilityPipeline — the tier composes no capability pipeline"
      "Conformance.normalizeLaws",
      NotUsed
          "Fuaran.Core.Ops.normalize — Apply.fs delegates only the structural-five APPLY to Core (applyContained); the tier ships no op-script normaliser, so the family's subject has no call site here"
      // fuaran#1478 re-derived this classification while choosing which law families to export as
      // host-neutral vectors for fuaran#1482, and left the row where it is: exporting a parity
      // family run with the reference as its own `under` would publish Core's self-consistency
      // check under this tier's name. The corpus records the absence as a statement in
      // `laws/manifest.json` rather than leaving it a gap.
      "Conformance.transformLaws",
      NotUsed
          "a host dataframe evaluator — QueryRefine consumes Fuaran.Core.DataFrame.evalPipeline as the pinned reference rather than shipping a second evaluator, so the parity laws have no host implementation to compare against the reference"
      "Conformance.canonicalFloatLaws",
      NotUsed
          "Wire.Canon.canonicalFloat — the tier's canonical-JSON encoder carries its own Fable-safe float formatter (CanonicalJson.formatFiniteDouble), and this family is self-contained over Core's encoder rather than taking a host one; cross-host float parity here is gated by the wire-format conformance corpus, a multi-host oracle it cannot replace" ]

// ---------------------------------------------------------------------------
//  the roster — by reflection over the PINNED kit
// ---------------------------------------------------------------------------

let private conformanceAssembly = typeof<Fuaran.Core.LawResult>.Assembly

/// The modules the kit publishes law entry points from. Mirrors Core's own census exactly, so the
/// two agree about what a "family" is; a module the kit stops shipping fails rather than silently
/// contributing nothing.
let private lawModules =
    [ "Conformance"; "FoldConfluence"; "IncrementalDelta"; "WireNullTolerance" ]

/// Core's roster predicate, character for character. The names are deliberately NOT all
/// `*Laws`-suffixed — `IncrementalDelta.laws`, `FoldConfluence.laneFoldLawsWith` — so a narrower
/// predicate here would silently disagree with Core's about the size of the shipped set.
let private isLawEntry (m: MethodInfo) =
    let n = m.Name

    n.EndsWith("Laws", StringComparison.Ordinal)
    || n.EndsWith("LawsWith", StringComparison.Ordinal)
    || n = "laws"
    || n = "lawsWith"

let private shippedFamilies () : string list =
    [ for moduleName in lawModules do
          match conformanceAssembly.GetType("Fuaran.Core." + moduleName) with
          | Null -> failtestf "the pinned kit no longer ships a module named %s" moduleName
          | NonNull t ->
              for m in t.GetMethods(BindingFlags.Public ||| BindingFlags.Static ||| BindingFlags.DeclaredOnly) do
                  if isLawEntry m then
                      yield moduleName + "." + m.Name ]
    |> List.distinct
    |> List.sort

/// The pinned kit's version, read from the assembly rather than hard-coded: the version decides
/// which families exist, so a report that named it from a literal could describe a kit that is not
/// the one the suite ran against.
let private kitVersion () =
    match conformanceAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>() with
    | null -> string (conformanceAssembly.GetName().Version)
    | attr ->
        // `0.18.0+<sha>` — the build metadata moves with every Core build, so it is dropped: the
        // committed report must be stable across rebuilds of the same pinned version.
        attr.InformationalVersion.Split('+')[0]

// ---------------------------------------------------------------------------
//  locating the checkout + reading the test sources
// ---------------------------------------------------------------------------

/// The repo root, found by climbing from the test binary. Identified by two files rather than one,
/// so a directory that merely happens to hold a `run.ps1` cannot be mistaken for it.
let private repoRoot =
    lazy
        (let rec climb (dir: DirectoryInfo option) =
            match dir with
            | None -> None
            | Some d ->
                if
                    File.Exists(Path.Combine(d.FullName, "run.ps1"))
                    && File.Exists(Path.Combine(d.FullName, "Fuaran.sln"))
                then
                    Some d.FullName
                else
                    climb (Option.ofObj d.Parent)

         match climb (Some(DirectoryInfo(AppContext.BaseDirectory))) with
         | Some root -> root
         | None ->
             // Never skip. A census that cannot read the sources it quantifies over would report a
             // fully-enrolled tier, which is the one answer it must never give by accident.
             failwith
                 "CoreConformanceCensus: could not locate the repo root above the test binary — the test sources could not be read, so the enrolment check has proved nothing.")

/// This file declares every family name as a string, so scanning it would find a "reference" to
/// each one. It is the declaration, not an adopter; excluded by name.
let private censusFileName = "CoreConformanceCensus.fs"

/// `//` line comments are stripped before scanning. Prose routinely names a law family
/// (`Conformance.hashFnLaws` in a header comment) and a mention is not a run; matching on comments
/// would make a doc edit fail the suite.
let private stripLineComments (text: string) =
    text.Split('\n')
    |> Array.map (fun line ->
        match line.IndexOf("//", StringComparison.Ordinal) with
        | -1 -> line
        | i -> line.Substring(0, i))
    |> String.concat "\n"

/// Every test project that references the conformance kit, as `(projectDir, [ file, source ])`.
/// Discovered from the `.fsproj` files rather than named here, so a project that adopts a law
/// family in a later phase enters this scan with no edit.
let private conformanceTestProjects () =
    let src = Path.Combine(repoRoot.Value, "src")

    // `Path.GetFileName` is `string | null` under F# 10 nullness; a path from `GetDirectories`
    // always has one, so falling back to the whole path is unreachable rather than lenient.
    let leaf (p: string) =
        Path.GetFileName p |> Option.ofObj |> Option.defaultValue p

    Directory.GetDirectories(src)
    |> Array.filter (fun dir ->
        (leaf dir).EndsWith(".Tests", StringComparison.Ordinal)
        && Directory.GetFiles(dir, "*.fsproj")
           |> Array.exists (fun p -> File.ReadAllText(p).Contains "Fuaran.Core.Conformance"))
    |> Array.map (fun dir ->
        let sources =
            Directory.GetFiles(dir, "*.fs")
            |> Array.filter (fun p -> leaf p <> censusFileName)
            |> Array.map (fun p -> leaf p, stripLineComments (File.ReadAllText p))

        leaf dir, sources)

/// The module aliases a source binds to the kit's law modules — `module CoreConf =
/// Fuaran.Core.Conformance` — plus the plain and fully-qualified spellings. Resolving aliases is
/// what keeps the scan from depending on any one file's naming habit.
let private aliasesFor (source: string) =
    let map = System.Collections.Generic.Dictionary<string, string>()

    for m in lawModules do
        map[m] <- m
        map["Fuaran.Core." + m] <- m

    for m in Regex.Matches(source, @"module\s+([A-Za-z_][\w']*)\s*=\s*Fuaran\.Core\.([A-Za-z_][\w']*)") do
        let alias = m.Groups[1].Value
        let target = m.Groups[2].Value

        if List.contains target lawModules then
            map[alias] <- target

    map

/// Every `Module.fn` token in a source that resolves, through that source's aliases, to a public
/// entry point of the kit. Returns the canonical `Module.fn` spelling.
let private kitReferences (source: string) : Set<string> =
    let aliases = aliasesFor source

    Regex.Matches(source, @"\b([A-Za-z_][\w']*(?:\.[A-Za-z_][\w']*)*)\.([a-z][\w']*)\b")
    |> Seq.choose (fun m ->
        let qualifier = m.Groups[1].Value
        let fn = m.Groups[2].Value

        match aliases.TryGetValue qualifier with
        | true, target -> Some(target + "." + fn)
        | _ -> None)
    |> Set.ofSeq

// ---------------------------------------------------------------------------
//  the report
// ---------------------------------------------------------------------------

let private reportPath () =
    Path.Combine(repoRoot.Value, "docs", "core-conformance.md")

let private statusOf =
    function
    | Adopted _ -> "Adopted"
    | NotUsed _ -> "Not used"
    | SiblingHost _ -> "Sibling host"
    | CarriedBy _ -> "Carried by phase"

let private detailOf =
    function
    | Adopted(test, port) -> sprintf "`%s` — via `%s`" test port
    | NotUsed mechanism -> mechanism
    | SiblingHost host -> host
    | CarriedBy phase -> phase

/// The committed table. Deterministic by construction: rows sorted by family key, LF endings, and
/// no timestamp — a report that changed on every run could never be a drift guard.
let render (rows: (string * Adoption) list) : string =
    let sb = StringBuilder()
    let line (s: string) = sb.Append(s).Append('\n') |> ignore

    line "# Core-conformance census — the UI tier"
    line ""
    line "<!-- GENERATED by src/Fuaran.UI.Tests/CoreConformanceCensus.fs. Do not hand-edit: the"
    line "     suite regenerates this file and fails when the committed copy has drifted. -->"
    line ""

    line (
        sprintf
            "Every public law family the pinned `Fuaran.Core.Conformance` **%s** ships, and how this repo answers for it."
            (kitVersion ())
    )

    line ""
    line "A family with no row fails the suite, and so does a row naming a family the pinned kit no"
    line "longer ships — enrolment is by name AND by reflection, and the two are checked against"
    line "each other. `Carried by phase` is an enrolment a named roadmap phase will flip to"
    line "`Adopted` when it ships."
    line ""
    line "| Family | Status | Detail |"
    line "|---|---|---|"

    for family, adoption in rows |> List.sortBy fst do
        line (sprintf "| `%s` | %s | %s |" family (statusOf adoption) (detailOf adoption))

    line ""
    line "## Summary"
    line ""
    line "| Status | Families |"
    line "|---|---|"

    for status in [ "Adopted"; "Carried by phase"; "Not used"; "Sibling host" ] do
        let n = rows |> List.filter (fun (_, a) -> statusOf a = status) |> List.length
        line (sprintf "| %s | %d |" status n)

    line (sprintf "| **Total** | **%d** |" (List.length rows))

    sb.ToString()

// ---------------------------------------------------------------------------
//  the census tests
// ---------------------------------------------------------------------------

let private normaliseEol (s: string) = s.Replace("\r\n", "\n")

[<Tests>]
let tests =
    testList
        "Core conformance census (Fuaran.UI)"
        [

          testCase "every law family the pinned kit ships has exactly one census row"
          <| fun _ ->
              // The half a declaration structurally cannot do for itself: a declaration quantifies
              // over what it names, so a family nobody enrolled produces no finding at any grade.
              // Reflection is what closes it — a family Core ADDS arrives here as a failing row.
              let declared = census |> List.map fst |> Set.ofList
              let shipped = shippedFamilies ()

              let unclassified = shipped |> List.filter (fun f -> not (Set.contains f declared))

              Expect.isEmpty
                  unclassified
                  (sprintf
                      "these law families of the pinned kit have no census row — classify each as Adopted / NotUsed / SiblingHost / CarriedBy in CoreConformanceCensus.fs: %A"
                      unclassified)

          testCase "the census names no family the pinned kit no longer ships"
          <| fun _ ->
              // The other direction, and it matters for the same reason: a row for a family that
              // was renamed or removed reads as coverage while covering nothing.
              let shipped = shippedFamilies () |> Set.ofList

              let stale =
                  census |> List.map fst |> List.filter (fun f -> not (Set.contains f shipped))

              Expect.isEmpty stale (sprintf "these census rows name no family the pinned kit ships: %A" stale)

          testCase "the census carries no duplicate row and no empty reason"
          <| fun _ ->
              let names = census |> List.map fst

              Expect.equal
                  (List.length (List.distinct names))
                  (List.length names)
                  "a family classified twice could be classified two ways"

              for family, adoption in census do
                  let reason =
                      match adoption with
                      | Adopted(test, port) -> test + port
                      | NotUsed mechanism -> mechanism
                      | SiblingHost host -> host
                      | CarriedBy phase -> phase

                  Expect.isTrue
                      (reason.Trim().Length > 6)
                      (sprintf
                          "%s carries no usable reason — the reason is what lets the next reader CHECK the classification rather than trust it"
                          family)

          testCase "the reflected roster agrees with the kit's own SampleAdequacy census"
          <| fun _ ->
              // Two independent enumerations of the same set: this file's reflection, and the kit's
              // own shipped `(family, adequacy)` declaration. They are built by different people for
              // different purposes, so a disagreement means one of them is describing a kit that is
              // not the pinned one — a finding, never something to paper over.
              let reflected = shippedFamilies () |> Set.ofList

              let kitDeclared = Fuaran.Core.SampleAdequacy.census |> List.map fst |> Set.ofList

              Expect.isEmpty
                  (Set.difference reflected kitDeclared |> Set.toList)
                  "families found by reflection that the kit's own SampleAdequacy census omits"

              Expect.isEmpty
                  (Set.difference kitDeclared reflected |> Set.toList)
                  "families the kit's own SampleAdequacy census names that reflection does not find"

          testCase "every Adopted row names a test that still exists, reached through a referenced entry point"
          <| fun _ ->
              // A row naming a retired test is the census failing quietly: it reads as coverage,
              // and nothing runs. Both halves are checked — the test name, and the kit entry point
              // the row claims reaches it (several families are reached through `certify` rather
              // than by their own name, and that claim is checkable too).
              let projects = conformanceTestProjects ()

              for family, adoption in census do
                  match adoption with
                  | Adopted(test, port) ->
                      let hosting =
                          projects
                          |> Array.filter (fun (_, sources) ->
                              sources |> Array.exists (fun (_, text) -> text.Contains test))

                      Expect.isNonEmpty
                          hosting
                          (sprintf
                              "%s is enrolled against a test named \"%s\", which no source in a conformance-referencing test project contains"
                              family
                              test)

                      let portEntries =
                          Regex.Matches(port, @"\b([A-Za-z_][\w']*)\.([a-z][\w']*)\b")
                          |> Seq.map (fun m -> m.Groups[1].Value + "." + m.Groups[2].Value)
                          |> Set.ofSeq

                      let reachedHere =
                          hosting
                          |> Array.exists (fun (_, sources) ->
                              sources
                              |> Array.exists (fun (_, text) ->
                                  Set.intersect (kitReferences text) portEntries |> Set.isEmpty |> not))

                      Expect.isTrue
                          reachedHere
                          (sprintf
                              "%s is enrolled as reached through %s, but no source in the project holding \"%s\" references that entry point"
                              family
                              port
                              test)
                  | _ -> ()

          testCase "a test that runs a Core law family has an Adopted row for it in the same project"
          <| fun _ ->
              // The second direction, done mechanically rather than by convention. Enrolling by
              // string alone is exactly the class Core's own narrowing tripped on 2026-09-03: the
              // declaration and the code it describes drift apart in silence. So the sources are
              // read, the kit aliases resolved, and every law family a test project actually
              // references must carry an `Adopted` row naming a test in THAT project.
              let familySet = shippedFamilies () |> Set.ofList

              let adoptedTests =
                  census
                  |> List.choose (fun (family, adoption) ->
                      match adoption with
                      | Adopted(test, _) -> Some(family, test)
                      | _ -> None)
                  |> Map.ofList

              for projectName, sources in conformanceTestProjects () do
                  let referenced =
                      sources
                      |> Array.map (fun (_, text) -> kitReferences text)
                      |> Array.fold Set.union Set.empty
                      |> Set.filter (fun r -> Set.contains r familySet)

                  for family in referenced do
                      match Map.tryFind family adoptedTests with
                      | None ->
                          failtestf
                              "%s runs the law family %s but the census does not classify it as Adopted — enrol it with the test name, or the census is describing a tier it has not read"
                              projectName
                              family
                      | Some test ->
                          Expect.isTrue
                              (sources |> Array.exists (fun (_, text) -> text.Contains test))
                              (sprintf
                                  "%s runs %s, but its Adopted row names the test \"%s\", which lives in a different project"
                                  projectName
                                  family
                                  test)

          testCase "the committed docs/core-conformance.md matches the rendered census"
          <| fun _ ->
              // The report is regenerated here and the committed copy asserted to have already
              // matched, so a stale doc is red and a fresh checkout that runs the suite recovers
              // it. Regenerating BEFORE asserting is deliberate: the repair for the red is already
              // on disk by the time the failure is read.
              let path = reportPath ()
              let rendered = render census

              let committed =
                  if File.Exists path then
                      Some(normaliseEol (File.ReadAllText path))
                  else
                      None

              Path.GetDirectoryName path
              |> Option.ofObj
              |> Option.iter (Directory.CreateDirectory >> ignore)

              File.WriteAllText(path, rendered)

              match committed with
              | None ->
                  failtestf "docs/core-conformance.md did not exist and has just been generated at %s — commit it" path
              | Some prior ->
                  Expect.equal
                      prior
                      (normaliseEol rendered)
                      "docs/core-conformance.md had drifted from the census; it has been regenerated in place — review and commit it" ]
