module Fuaran.UI.Tests.IdlFullVocabularyFuzz

open System
open Expecto
open Fuaran.Core.Idl
open Fuaran.UI.Tests.IdlCertification
open Fuaran.UI.Testing

module G = Fuaran.UI.Generated

// ---------------------------------------------------------------------------
//  Phase 1668, certification (3) — generative cross-host conformance over the
//  FULL vocabulary.
//
//  Recovered from
//  `Fuaran-Core@ccead29^:tests/Fuaran.Core.Tests/IdlFullVocabularyFuzzTests.fs`
//  (Phase 698 there), which `fuaran-core#123` deleted with the UI byte-pin it
//  read. Core still runs a generative sweep over its own 8-kind reference
//  vocabulary comparing two legs; what left with the fixture is SCALE — every
//  real kind, the node ENVELOPE included, and a third independently-emitted
//  host leg. That is exactly the half a neutral vocabulary cannot recover, so
//  it comes home to the tier that owns the vocabulary.
//
//  It samples the real vocabulary (every kind, envelope included) and compares
//  THREE legs per vector:
//
//    1. `Encode.encode vocabulary v`  — the schema-driven interpreter, from the
//                                       value. The reference bytes.
//    2. `G.encodeNode`                — the generated F# module, over the value
//                                       the generated `G.decodeNode` reads back
//                                       from leg 1.
//    3. the generated TypeScript      — independently emitted, run under node,
//       module                          from the VALUE (`Gen.typescriptValue`),
//                                       not from leg 1's bytes.
//
//  **What leg 2 does and does not prove, stated plainly.** Its input is derived
//  from leg 1's wire, because `Gen.fsharpValue` cannot emit the real
//  vocabulary's value shapes (records, maps, hosted slots, sentinels), so there
//  is no independent way to CONSTRUCT a generated-F# value for an arbitrary
//  sampled vector. Comparing `decodeNode >> encodeNode` against the
//  interpreter's bytes still catches an encoder that writes a field the
//  interpreter does not, and vice versa — the re-encode simply stops matching.
//  What it cannot catch is a COMPENSATING pair, where the generated decoder's
//  loss is exactly restored by the generated encoder. Leg 3 is value-derived
//  and has no such gap, so the two legs are complementary rather than
//  redundant.
//
//  Reproducibility: every vector is a pure function of (seed, index) —
//  `Sample.sampleNodes` draws from a seeded LCG in index order — so a failure
//  report naming an index is enough to reproduce it, with no captured payload.
//
//  WHAT CHANGED IN THE PORT.
//
//   * The vocabulary is the homed `src/Fuaran.UI.Idl/` declaration through
//     `IdlCertification.vocabulary`, which is `Artifact.canonicalise`d — so the
//     sampler's kind cycle is stable against a reshuffle of `Vocabulary.fs`,
//     and it is the exact shape `Generated.fs` is emitted from, which is what
//     makes legs 1 and 2 comparable by construction rather than by the accident
//     of authored order.
//   * Leg 2 is the TIER'S OWN generated module (`Fuaran.UI.Generated`, committed
//     source inside the shipped `Fuaran.UI`), not a certification snapshot — so
//     the leg now certifies the module a .NET consumer actually compiles.
//   * `Gen.typescriptModule` returns a `Result` since `Fuaran.Core` 0.21.0
//     (Phase 124 there gave the TS backend the refusal channel the F# one
//     always had). Its `Error` is a FAILURE here rather than a skip: a backend
//     that cannot emit this vocabulary is precisely what leg 3 exists to find.
//   * The sampled-vector narrowing the HOST PROJECTIONS necessitate lives beside
//     those projections, in `src/Fuaran.UI.Idl/Support.fs`, rather than in this
//     file — the posture Core took, at the vocabulary's new home. A SECOND pass
//     joins it here, steering a draw out of an omit-default hole in the engine's
//     encoders; that one is a finding about `Fuaran.Core.Idl` rather than about
//     this vocabulary, so it lives with the sweep (`IdlCertificationSupport`)
//     and its scope is pinned by name below.
//   * Leg 2's hosted-FREE refusals are OUT OF DOMAIN rather than failures, and
//     their reasons are pinned by name. Core called such a refusal an outright
//     failure; since then the support document has grown decode REFINES, which
//     are host semantics in the same sense a hosted codec's grammar is. The
//     boundary moved; the tooth moved with it, from a count to a named census.
//   * The go-red is explicit: the divergence detector is proven to CATCH a
//     perturbation before any of its clean verdicts are trusted.
//
//  `Lanes.slow`-marked: the sweep is generative and spawns node, so it belongs
//  to the full (ship) lane. Four cases, ~4000 vectors × three legs.
// ---------------------------------------------------------------------------

/// The seed. Any value works; it is pinned so the gate is the SAME vectors on
/// every run and every machine.
let private seed = 20260818

/// The byte the harness uses to separate a vector index from its wire string.
/// Spelled as an escape rather than embedded literally: a raw control character
/// in source is invisible in review and survives a copy-paste only by luck.
let private sep = '\u0001'

/// **The vector budget, measured rather than inherited.**
///
/// `FUARAN_FUZZ_VECTORS` can RAISE the count for a soak run; it deliberately
/// cannot lower it, so the gate cannot be weakened from the environment.
let private vectorBudget =
    // ── What the original bring-up measured (Phase 698) ──────────────────────
    //
    // Two questions were asked, and they gave very different answers.
    //
    // 1. WHERE DID THE REAL DIVERGENCES APPEAR? Both classes bring-up found
    //    showed up almost immediately: the generated TypeScript encoder's
    //    missing Ordinal key sort at vector 0, and the first host-codec
    //    rejection at vector 2. Sizing on that alone would justify a budget of
    //    about fifty, which is the trap — it measures the defects that happened
    //    to be present, not the depth at which a defect could hide.
    //
    // 2. HOW DEEP MUST THE SWEEP GO TO EXERCISE EVERY PLACE ONE COULD HIDE?
    //    The vocabulary declared 428 wire-visible field positions; the index at
    //    which the LAST of them is first sampled present — the saturation index
    //    — was measured over 20,000 vectors on four seeds and ranged 1317-3384,
    //    with the pinned seed at 2097. A single-point defect in the
    //    last-covered position is invisible below its index.
    //
    // The budget answers question 2, because question 1 cannot be asked about a
    // defect that does not exist yet. 4000 cleared the worst of the four seeds
    // with headroom, so a re-seed does not quietly stop covering the tail. It
    // is emphatically NOT the 500 a small-vocabulary sweep uses: that number was
    // never contradicted rather than ever chosen, and 2097 contradicts it here.
    //
    // The vocabulary has grown since (43 kinds), which moves the saturation
    // index UP and never down, so the headroom argument is the conservative one
    // — and `FUARAN_FUZZ_VECTORS` is how a soak run re-measures it.
    let floor' = 4000

    match Int32.TryParse(Environment.GetEnvironmentVariable "FUARAN_FUZZ_VECTORS") with
    | true, n when n > floor' -> n
    | _ -> floor'

/// One leg's disagreement with the interpreter, carrying everything needed to
/// reproduce and read it.
type private Divergence =
    { Index: int
      Leg: string
      Expected: string
      Actual: string }

let private render (d: Divergence) =
    sprintf "vector %d (seed %d) — %s\n    interpreter: %s\n    this leg   : %s" d.Index seed d.Leg d.Expected d.Actual

/// The sampled vectors, moved into the space every leg agrees on by TWO passes
/// with two different authorities.
///
///  1. `narrowNode` — steers a draw out of the `OmitDefault`-on-a-case-mapped-
///     enum blind spot in `Fuaran.Core.Idl`'s three encoders. That is a finding
///     about the packaged engine, not a property of this vocabulary; its whole
///     argument, and why the pass steers rather than drops, is in
///     `IdlCertificationSupport`.
///  2. `VocabularySupport.canonicaliseVector` — the narrowings the HOST
///     PROJECTIONS necessitate (`Switch`'s `on`-XOR-`stateKey`, `SetState`'s
///     `value`-XOR-`valueFrom`), declared beside the projections themselves so
///     retiring a projection retires its narrowing.
///
/// In that order, because (1) is a statement about the engine's encoders and
/// (2) a statement about this host's projection of the wire; (2) may SUPPLY a
/// member (the deterministic compact `stateKey`) and must therefore see the
/// steered shape rather than the raw draw.
let private vectors =
    lazy
        (Sample.sampleNodes vocabulary allKindTags seed vectorBudget
         |> List.map (narrowNode vocabulary >> Fuaran.UI.VocabularySupport.canonicaliseVector))

/// The reference bytes — leg 1.
let private interpreter =
    lazy
        (vectors.Value
         |> List.mapi (fun i v ->
             match Encode.encode vocabulary v with
             | Ok w -> w
             | Error m -> failtestf "interpreter encode failed on vector %d (seed %d): %s" i seed m))

/// The comparison ONE leg's output is held to, factored out so the go-red below
/// can prove it discriminates. `None` is agreement.
let private disagreement (leg: string) (index: int) (expected: string) (actual: string) : Divergence option =
    if actual = expected then
        None
    else
        Some
            { Index = index
              Leg = leg
              Expected = expected
              Actual = actual }

[<Tests>]
let tests =
    Lanes.slow
    <| testList
        "Phase 1668 (3) — generative cross-host conformance over the full vocabulary"
        [ testCase "GO-RED — the divergence detector catches a perturbation" (fun _ ->
              // Every clean verdict below is a NEGATIVE result read off
              // `disagreement`, so a comparison that could never report one
              // would make the whole sweep vacuously green — three legs, four
              // thousand vectors, and nothing measured. Proven on a real
              // interpreter emission rather than a literal, so the perturbation
              // is over the bytes the sweep actually compares.
              let reference = List.head interpreter.Value

              Expect.isNone
                  (disagreement "probe" 0 reference reference)
                  "identical bytes must not be reported as a divergence"

              let perturbed = reference.Replace("\"id\":\"", "\"id\":\"x")

              Expect.notEqual perturbed reference "the perturbation must actually change the bytes"

              match disagreement "probe" 7 reference perturbed with
              | None ->
                  failtest "the divergence detector accepted perturbed bytes — every verdict in this sweep is vacuous"
              | Some d ->
                  Expect.equal d.Index 7 "the report carries the vector index, which is what reproduces it"
                  Expect.stringContains (render d) "seed" "the rendered report names the seed")

          testCase "the engine's omit-default blind spot is exactly one field, and it is named" (fun _ ->
              // The SCOPE of the finding the steering pass exists for, derived
              // from the vocabulary rather than remembered. Two directions of
              // failure, both worth catching:
              //
              //  * a SECOND field arrives in the blind spot — a new
              //    `Declare.enumWith` enum gaining an `OmitDefault` — and the
              //    steering silently starts covering for it as well. Naming the
              //    set makes that a conversation rather than a no-op.
              //  * the set becomes EMPTY, which happens either because the
              //    declaration moved or because `Fuaran.Core` resolved the
              //    alphabet at its two wire-comparison sites. In the second case
              //    the steering pass is dead code and should be retired with the
              //    engine bump that fixes it.
              Expect.equal
                  (blindSpotFields vocabulary)
                  [ "SemanticStyle.direction" ]
                  "the OmitDefault-on-a-case-mapped-enum set moved — see the finding in IdlCertificationSupport before adjusting this list")

          testCase "the sampler draws node envelopes on every presence polarity" (fun _ ->
              // The sampler leg on its own, so a regression that stopped
              // sampling envelopes shows up HERE as a precise failure rather
              // than as the three-way sweep quietly going vacuous — that sweep
              // would still pass, green and meaningless, with every vector
              // envelope-free.
              let vs = vectors.Value

              let enveloped =
                  vs
                  |> List.sumBy (function
                      | VNodeEnv _ -> 1
                      | _ -> 0)

              let bare =
                  vs
                  |> List.sumBy (function
                      | VNode _ -> 1
                      | _ -> 0)

              Expect.isGreaterThan enveloped 0 "some vectors carry an envelope"
              Expect.isGreaterThan bare 0 "some vectors carry none (absence is sampled too)"

              // Every wire-visible envelope field must be drawn PRESENT
              // somewhere: a field never sampled present is a field this sweep
              // does not cover.
              let wireEnvelope =
                  vocabulary.NodeFields
                  |> List.filter (fun f -> f.Opt <> HostOnly)
                  |> List.map (fun f -> f.Name)

              let seenPresent =
                  vs
                  |> List.collect (function
                      | VNodeEnv(_, env, _, _) -> env |> List.map fst
                      | _ -> [])
                  |> Set.ofList

              for name in wireEnvelope do
                  Expect.isTrue
                      (seenPresent.Contains name)
                      (sprintf "envelope field '%s' was never sampled present" name)

              // A host-only envelope field has no wire projection, so it must
              // never be drawn present — the third polarity, and the one whose
              // failure would leak a host field onto the wire.
              for f in vocabulary.NodeFields |> List.filter (fun f -> f.Opt = HostOnly) do
                  Expect.isFalse
                      (seenPresent.Contains f.Name)
                      (sprintf "host-only envelope field '%s' was sampled onto the wire" f.Name))

          testCase "three-way: interpreter, generated F# and generated TypeScript agree on every vector" (fun _ ->
              let vs = vectors.Value

              Expect.equal (List.length vs) vectorBudget "the sampler produced the requested vectors"

              let reference = interpreter.Value

              // ---- leg 2: the generated F# module, in-process ----
              //
              // The generated decoder calls the HOST codecs for every `THosted`
              // slot, and a hosted slot's content grammar is the host's, not the
              // IDL's — the IDL states only that the position carries verbatim
              // JSON. The sampler therefore cannot draw content those codecs are
              // obliged to accept, and the real ones are strict (an aria role is
              // a string; a row feed is an array of row objects or the legacy
              // sentinel; a `DataSource` carries `columns`). A vector that
              // POPULATES a hosted slot is consequently outside leg 2's domain,
              // and is recorded as such — a stated boundary, not a swallowed
              // failure.
              //
              // A refusal on a hosted-FREE vector is the same KIND of boundary
              // reached from a second direction, and this is where the port
              // parts company with Core's version, which called it an outright
              // failure. Since then the host support document has grown DECODE
              // REFINES — cross-field rules and value bounds the IDL's type
              // language cannot state at a field, so the sampler cannot avoid
              // violating them: `Switch.autoAdvanceMs` must be positive,
              // `Rating.max` at least 1, a `Tokens` field that forbids free text
              // must offer a suggestion source. Those are host semantics, in the
              // same sense a hosted codec's grammar is.
              //
              // So a hosted-free refusal is out of domain too — but its REASON
              // is PINNED BY NAME below rather than merely counted, which is the
              // posture `GeneratedLayerTests` takes with its policy-owned
              // residue and for the same reason: the interesting regression is a
              // change in WHICH refusals occur (a `Binding` case falling out of
              // the generated decoder would land here silently as one more
              // refusal), and a bare count hides it.
              let hostedVectors = vs |> List.map (Gen.usesHosted vocabulary)

              let mutable compared = 0
              let mutable outOfDomainHosted = 0
              let refinedAway = System.Collections.Generic.HashSet<string>()

              /// A refusal reason with its sampled numbers erased, so the census
              /// is a set of REASONS rather than of draws.
              let reasonShape (e: string) =
                  System.Text.RegularExpressions.Regex.Replace(e, @"-?\d+", "N")

              let fsharpDivergences =
                  List.zip reference hostedVectors
                  |> List.mapi (fun i (w, isHosted) ->
                      match (G.decodeNode w: Result<G.Node<obj>, string>) with
                      | Error e ->
                          if isHosted then
                              outOfDomainHosted <- outOfDomainHosted + 1
                          else
                              refinedAway.Add(reasonShape e) |> ignore

                          None
                      | Ok node ->
                          compared <- compared + 1
                          disagreement "generated-F#" i w (G.encodeNode node))
                  |> List.choose id

              // ---- leg 3: the generated TypeScript module, under node ----
              let tsModule =
                  match Gen.typescriptModule vocabulary allKindTags with
                  | Ok s -> s
                  // A FAILURE, never a skip: the TypeScript backend gained a
                  // refusal channel in Fuaran.Core 0.21.0, and a backend that
                  // cannot emit this vocabulary is exactly what this leg exists
                  // to find.
                  | Error e -> failtestf "the TypeScript backend refused this vocabulary: %A" e

              let vectorsJs =
                  vs
                  |> List.mapi (fun i v -> sprintf "  [%d, %s]," i (Gen.typescriptValue v))
                  |> String.concat "\n"

              let harness =
                  tsModule
                  + "\n\nconst __vectors = [\n"
                  + vectorsJs
                  + "\n];\n"
                  // Fault-isolate per vector: one throwing vector must not cost
                  // the report for all the others, and the index has to survive
                  // into it.
                  + "for (const [i, node] of __vectors) {\n"
                  + "  try { console.log(i + '\\u0001' + encodeNode(node)); }\n"
                  + "  catch (e) { console.log(i + '\\u0001' + 'TS-THREW: ' + (e && e.stack ? String(e.stack).split('\\n').slice(0,4).join(' >> ') : e)); }\n"
                  + "}\n"

              let tsDivergences =
                  match runNode harness with
                  | None -> None // node absent — leg 3 skipped, reported below
                  | Some stdout ->
                      let got =
                          stdout.Replace("\r\n", "\n").Split('\n')
                          |> Array.filter (fun l -> l <> "")
                          |> Array.map (fun l ->
                              let parts = l.Split(sep)
                              int parts[0], parts[1])
                          |> Map.ofArray

                      reference
                      |> List.mapi (fun i w ->
                          match Map.tryFind i got with
                          | None -> disagreement "generated-TS" i w "(no TS output)"
                          | Some actual -> disagreement "generated-TS" i w actual)
                      |> List.choose id
                      |> Some

              let all = fsharpDivergences @ (defaultArg tsDivergences [])

              if not (List.isEmpty all) then
                  // Report the FIRST divergence per leg in full, plus the index
                  // census — so a bring-up (or a soak) run measures the budget
                  // in one pass instead of one failure at a time.
                  let firstPerLeg =
                      all
                      |> List.groupBy (fun d -> d.Leg)
                      |> List.map (fun (_, ds) -> ds |> List.minBy (fun d -> d.Index) |> render)
                      |> String.concat "\n"

                  let census =
                      all
                      |> List.groupBy (fun d -> d.Leg)
                      |> List.map (fun (leg, ds) ->
                          sprintf
                              "%s: %d of %d diverged; first indices %A"
                              leg
                              (List.length ds)
                              vectorBudget
                              (ds |> List.map (fun d -> d.Index) |> List.truncate 12))
                      |> String.concat "\n"

                  failtestf "cross-host divergence over the full vocabulary\n%s\n\n%s" firstPerLeg census

              // Leg 2 must not quietly become vacuous. A floor well under the
              // observed comparable share catches a change that collapses the
              // set without failing on ordinary sampling variance.
              Expect.isGreaterThan
                  compared
                  (vectorBudget / 6)
                  (sprintf
                      "the generated-F# leg compared too few vectors to mean anything (compared %d, hosted-slot vectors out of its domain %d, of %d)"
                      compared
                      outOfDomainHosted
                      vectorBudget)

              // Leg 2's hosted-FREE refusals, PINNED BY NAME — the set of
              // reasons, with sampled numbers erased, not a count. Every entry
              // is a DECODE REFINE declared in this vocabulary's own support
              // document (`src/Fuaran.UI.Idl/Support.fs`): a rule relating two
              // members, or bounding a well-typed value, which the IDL's type
              // language cannot state at a field and the sampler therefore
              // cannot avoid violating.
              //
              // What this pin is FOR is the arrival of a reason that is not one
              // of those. A `Binding` case falling out of the generated decoder,
              // a splice that stops compiling a member, a projection that starts
              // refusing a shape it used to accept — each would land here as one
              // more refusal and would be invisible to a count. It also fails
              // when a refine's PROSE changes, which is the cheap price of
              // reading the reason rather than tallying it.
              Expect.equal
                  (refinedAway |> Seq.sort |> List.ofSeq)
                  [ "Rating 'max' must be at least N — a scale with N positions cannot be rendered or announced"
                    "Tokens declares 'allowFreeText' false and no 'suggestions' source — the field could admit no token by any gesture. Give it a suggestion source, or leave 'allowFreeText' at its default of true"
                    "autoAdvanceMs must be a positive integer" ]
                  "the hosted-free refusal reasons are exactly the host refines declared in src/Fuaran.UI.Idl/Support.fs"

              match tsDivergences with
              | None -> skiptest "node not on PATH — the generated-TypeScript leg was skipped (legs 1+2 ran)"
              | Some _ -> ()) ]
