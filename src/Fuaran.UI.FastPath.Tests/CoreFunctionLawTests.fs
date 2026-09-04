namespace Fuaran.UI.FastPath.Tests

// ============================================================================
//  Phase 1478 — the Core conformance kit's function-registry, capability,
//  composition, verification and memo families, run in this tier's suite.
//
//  The families come in TWO SHAPES, and the difference decides what each green
//  row below means. Saying which is which is the whole point of running them
//  here rather than trusting a name:
//
//   * PARAMETERISED — `compositionLaws`, `functionVerifyLaws`,
//     `verifyHonestyLaws`, `memoLaws`, `memoSoundnessLaws`,
//     `encoderInjectivityLaws`. Each is instantiated with THE TIER'S OWN
//     artifact witness (`CoreLawSupport.witness` over the FastPath pattern
//     bank), its own egress gate as the validity oracle
//     (`PreEmitValidate.validate`, the check `FastPath.tryInstantiate` runs),
//     its own memo-key encoder and its own generators. A defect in the tier
//     fails these.
//
//   * SELF-CONTAINED — `registryLaws`, `capabilityLaws`, `packLoadingLaws`,
//     `paramLaws`, `deferredLaws`. Each takes only `(seed, iterations)` and
//     runs over Core's OWN types; they cannot see this tier's code. Running
//     them here is real evidence — that the PINNED kit's contract holds on
//     this machine at this pin — and it is evidence about the pin, not about
//     the tier. Two of them (`registryLaws`, `capabilityLaws`) certify a
//     mechanism the FastPath seam genuinely uses, so a SEPARATE tier-shaped
//     test asserts the same property directly over `SeedCatalogue.defaultBank`
//     rather than a wrapper pretending the law took the tier's registry. The
//     other three (`packLoadingLaws`, `paramLaws`, `deferredLaws`) certify
//     mechanisms the tier has no call site for — content packs, query
//     parameters, deferred values — so they stand as pin evidence alone, and
//     this comment is where that is said rather than left to be inferred from
//     a green row.
//
//  Read `CoreLawSupport.fs` first: it carries what the tier's artifact-
//  function algebra is, and the two properties of the seam the encoding makes
//  visible.
// ============================================================================

module CoreFunctionLawTests =

    open System.IO
    open Expecto
    open Fuaran.Core
    open Fuaran.UI
    open Fuaran.UI.FastPath.Tests.CoreLawSupport

    module CoreConf = Fuaran.Core.Conformance

    /// One seed for every family here, so a failure anywhere reproduces from a
    /// single number.
    let private lawSeed = 20260904

    /// `verifyFunction` is run eight times inside `verifyHonestyLaws` (four
    /// determinism axes, sound and broken), each for `iterations` draws, so the
    /// verification families take a smaller sample than the rest. It is still
    /// far more than the ~1-in-6 chance per draw the broken fixture needs.
    let private verifySeedIterations = 60

    // -----------------------------------------------------------------------
    //  the shared corpus (a sibling repo)
    // -----------------------------------------------------------------------

    /// The shared wire-format corpus, normally a sibling of this repo. It is
    /// absent in a single-repo checkout, and also in a git worktree checked out
    /// away from the workspace, so `FUARAN_WIRE_FIXTURES` overrides the path —
    /// the same override shape the estate's other corpus consumers take.
    let private corpusDir =
        System.Environment.GetEnvironmentVariable "FUARAN_WIRE_FIXTURES"
        |> Option.ofObj
        |> Option.filter (fun s -> s <> "")
        |> Option.defaultValue (Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "..", "wire-format-fixtures"))

    let private regenCommand =
        "dotnet run --project src/Fuaran.UI.FastPath.Tests -- --emit-laws ..\\wire-format-fixtures"

    // -----------------------------------------------------------------------
    //  the tests
    // -----------------------------------------------------------------------

    [<Tests>]
    let tests =
        testList
            "Core function laws (Fuaran.UI.FastPath)"
            [

              // ---- self-contained: the pinned kit's own contract -----------

              testCase "the invocable-capability contract certifies under Core's capabilityLaws"
              <| fun _ ->
                  CoreConf.capabilityLaws lawSeed 100
                  |> assertAllPassed "capabilityLaws over the pinned Fuaran.Core.Function"

              testCase "the signature-typed function registry certifies under Core's registryLaws"
              <| fun _ ->
                  CoreConf.registryLaws lawSeed 100
                  |> assertAllPassed "registryLaws over the pinned FunctionRegistry"

              testCase "content-pack loading certifies under Core's packLoadingLaws"
              <| fun _ ->
                  // The tier loads no content pack; this is evidence about the
                  // PIN, recorded as such in the census row's port.
                  CoreConf.packLoadingLaws lawSeed 100
                  |> assertAllPassed "packLoadingLaws over the pinned Fuaran.Core.Function"

              testCase "parameterised-query binding certifies under Core's paramLaws"
              <| fun _ ->
                  // Likewise: the FastPath seam binds hole VALUES, not query
                  // parameters. Pin evidence.
                  CoreConf.paramLaws lawSeed 100
                  |> assertAllPassed "paramLaws over the pinned Fuaran.Core.Function"

              testCase "the Deferred value codec certifies under Core's deferredLaws"
              <| fun _ ->
                  // Likewise: nothing in `Fuaran.UI.FastPath` constructs a
                  // `Deferred`. Pin evidence.
                  CoreConf.deferredLaws lawSeed 100
                  |> assertAllPassed "deferredLaws over the pinned Fuaran.Core.Function"

              // ---- parameterised over the tier's own artifact algebra ------

              testCase "FastPath artifact-functions compose hygienically under Core's compositionLaws"
              <| fun _ ->
                  // One witness on both sides of the boundary, so `embed` is
                  // the identity — the shape Core's own doc names for a domain
                  // that composes within a single witness. What is certified is
                  // the tier's hole algebra: apply-after-compose equals the
                  // nested application, disjoint slots commute, two same-named
                  // inner holes re-root to distinct addresses and binding one
                  // never captures the other, and the composed effect is the
                  // componentwise join.
                  CoreConf.compositionLaws witness witness id drawComposition lawSeed 100
                  |> assertAllPassed "compositionLaws over the FastPath signature algebra"

              testCase "a sound and a broken FastPath pattern certify under Core's functionVerifyLaws"
              <| fun _ ->
                  // The validity oracle is the tier's own egress gate, so the
                  // verdict these laws read is the verdict the shipped seam
                  // gives: `soundPattern` emits a gate-clean tree for every
                  // binding in its declared space, and `brokenPattern`'s wider
                  // hole admits one the gate faults.
                  let sound = soundPattern |> fnOf "verify-sound" pureEffect
                  let broken = brokenPattern |> fnOf "verify-broken" pureEffect

                  CoreConf.functionVerifyLaws
                      witness
                      sound
                      broken
                      validatorRegistry
                      genParams
                      lawSeed
                      verifySeedIterations
                  |> assertAllPassed "functionVerifyLaws over the FastPath egress gate"

              testCase "verification over FastPath patterns claims structure only (verifyHonestyLaws)"
              <| fun _ ->
                  let mkSound (d: DeterminismSource) =
                      soundPattern |> fnOf "honest-sound" { Host = Pure; Determinism = d }

                  let mkBroken (d: DeterminismSource) =
                      brokenPattern |> fnOf "honest-broken" { Host = Pure; Determinism = d }

                  CoreConf.verifyHonestyLaws
                      witness
                      mkSound
                      mkBroken
                      validatorRegistry
                      genParams
                      lawSeed
                      verifySeedIterations
                  |> assertAllPassed "verifyHonestyLaws over the FastPath egress gate"

              testCase "FastPath application memoises soundly under Core's memoLaws"
              <| fun _ ->
                  CoreConf.memoLaws witness encode drawMemo OpStream.defaultHash lawSeed 100
                  |> assertAllPassed "memoLaws over the FastPath artifact-function"

              testCase "an under-declared FastPath function is never cached (memoSoundnessLaws)"
              <| fun _ ->
                  // The fixture's ROOT declares pure/deterministic while the
                  // sub-function composed into its slot declares
                  // `ReadsHost`/`Random`, so the pre-Phase-53 declared-root
                  // check would have cached it and the audited gate must not.
                  // The law ignores its iteration count (the evidence is BUILT,
                  // not drawn), hence 1.
                  CoreConf.memoSoundnessLaws witness encode (underDeclaredFn impureEffect) underDeclaredArgs lawSeed 1
                  |> assertAllPassed "memoSoundnessLaws over the FastPath artifact-function"

              testCase "the FastPath memo-key encoder is collision-free (encoderInjectivityLaws)"
              <| fun _ ->
                  // The silent precondition of the two memo families above: the
                  // memo key is `Tree.encodeHash w.Tree encode node`, so a
                  // lossy encoder would let the cache serve the WRONG artifact.
                  CoreConf.encoderInjectivityLaws witness encode genFn lawSeed 200
                  |> assertAllPassed "encoderInjectivityLaws over the FastPath memo-key encoder"

              // ---- tier-shaped: the same properties, over the real bank ----

              testCase "every seed pattern's capability accepts an in-space arg and refuses the rest"
              <| fun _ ->
                  // The tier-shaped twin of `capabilityLaws`, which is
                  // self-contained and so cannot see this bank. Every seed
                  // pattern is a real `Capability` in a real registry, so the
                  // arg-validation and codec-round-trip properties are asserted
                  // here directly.
                  for entry in FunctionRegistry.enumerate SeedCatalogue.defaultBank.Registry do
                      let cap = entry.Capability

                      let inSpace =
                          cap.Signature.Holes
                          |> List.choose (fun h ->
                              match h.Space with
                              | Some(IntRange(lo, _)) -> Some(h.Addr, string lo)
                              | Some AnyString -> Some(h.Addr, "x")
                              | _ -> None)

                      Expect.equal
                          (Capability.validateArgs cap inSpace)
                          (Ok())
                          (sprintf "%s: a fully in-space arg set is accepted" cap.Id)

                      match Capability.validateArgs cap (("no-such-hole", "x") :: inSpace) with
                      | Error(UnknownArg _) -> ()
                      | other -> failtestf "%s: an unknown arg was not refused (%A)" cap.Id other

                      match
                          cap.Signature.Holes
                          |> List.tryPick (fun h -> h.Space |> Option.map (fun s -> h.Addr, s))
                      with
                      | Some(addr, IntRange(_, hi)) ->
                          match Capability.validateArgs cap [ addr, string (hi + 1) ] with
                          | Error(ArgOutOfSpace _) -> ()
                          | other -> failtestf "%s: an out-of-space arg was not refused (%A)" cap.Id other
                      | _ ->
                          // Every seed pattern declares at least one bounded
                          // numeric hole OR only unbounded string holes; the
                          // latter has no out-of-space value to offer.
                          ()

                      match CapabilityCodec.decode (CapabilityCodec.encode cap) with
                      | Ok back -> Expect.equal back cap (sprintf "%s: the declaration round-trips the codec" cap.Id)
                      | Error m -> failtestf "%s: the declaration did not decode (%s)" cap.Id m

              testCase "the seed bank's registry enumerates id-stably and refuses a duplicate id"
              <| fun _ ->
                  // The tier-shaped twin of `registryLaws`. `FastPath.bank`
                  // SKIPS a duplicate rather than failing, and that posture is
                  // only safe because the registry refuses the duplicate
                  // underneath — asserted here rather than assumed.
                  let registry = SeedCatalogue.defaultBank.Registry
                  let ids = FunctionRegistry.enumerate registry |> List.map (fun e -> e.Capability.Id)

                  Expect.equal ids (List.sort ids) "the seed registry enumerates in id order"
                  Expect.equal (List.length ids) (List.length SeedCatalogue.all) "every seed pattern registered"

                  let first = List.head (FunctionRegistry.enumerate registry)

                  match FunctionRegistry.register first registry with
                  | Error(DuplicateCapability id) -> Expect.equal id first.Capability.Id "the duplicate is named"
                  | other -> failtestf "re-registering an existing id was not refused (%A)" other

              testCase "every seed pattern declares value holes only (the builder cannot receive a tree)"
              <| fun _ ->
                  // `FastPath.bank` DOES project a `SlotHole` into the
                  // registered signature, but `Pattern.Build` takes only
                  // `Map<string, string>` — so a slot-bearing seed pattern
                  // would be searchable and not instantiable. Nothing enters
                  // that gap today; this is what keeps it from being entered
                  // silently. See `CoreLawSupport.fs`, header note 2.
                  for p in SeedCatalogue.all do
                      for h in p.Holes do
                          match h.Kind with
                          | ValueHole _ -> ()
                          | other ->
                              failtestf
                                  "seed pattern '%s' declares a non-value hole at '%s' (%A) — FastPath.Pattern.Build cannot receive one"
                                  p.Id
                                  h.Addr
                                  other

              // ---- the vector export for the other hosts (fuaran#1482) -----

              testCase "the exported capabilityLaws vectors are the verdicts the law demands"
              <| fun _ ->
                  // The export reproduces `capabilityLaws`' own draw and
                  // computes each expectation by CALLING the kit. This asserts
                  // every computed expectation is the one the law demands, so a
                  // vector that disagreed with the law could never be published.
                  // The law over the same seed is run too: the exported sample
                  // is a sample of a passing run, not merely of a reproducible
                  // one.
                  CoreConf.capabilityLaws LawVectorExport.seed LawVectorExport.iterations
                  |> assertAllPassed "capabilityLaws over the exported seed"

                  for d in LawVectorExport.draws () do
                      Expect.equal
                          (Capability.validateArgs d.Cap [ "h0", string d.Lo ])
                          (Ok())
                          (sprintf "iteration %d: the in-space arg is accepted" d.Iteration)

                      match Capability.validateArgs d.Cap [ "h0", string (d.Hi + 1) ] with
                      | Error(ArgOutOfSpace _) -> ()
                      | other -> failtestf "iteration %d: out-of-space was not ArgOutOfSpace (%A)" d.Iteration other

                      match Capability.validateArgs d.Cap [ "nope", string d.Lo ] with
                      | Error(UnknownArg _) -> ()
                      | other -> failtestf "iteration %d: an unknown arg was not UnknownArg (%A)" d.Iteration other

                      match CapabilityCodec.decode (CapabilityCodec.encode d.Cap) with
                      | Ok back ->
                          Expect.equal back d.Cap (sprintf "iteration %d: the declaration round-trips" d.Iteration)
                      | Error m -> failtestf "iteration %d: the declaration did not decode (%s)" d.Iteration m

                      match
                          Registry.empty
                          |> Registry.register d.Cap
                          |> Result.bind (Registry.register d.CapB)
                      with
                      | Ok r ->
                          let ids = Registry.enumerate r |> List.map (fun c -> c.Id)

                          Expect.equal
                              ids
                              (List.sort ids)
                              (sprintf "iteration %d: enumeration is id-sorted" d.Iteration)
                      | Error e -> failtestf "iteration %d: registration failed (%A)" d.Iteration e

                  let rendered = LawVectorExport.renderCapabilityVectors ()

                  Expect.isFalse
                      (rendered.Contains "\"unexpected\"")
                      "a vector carried a refusal outside the two the law distinguishes"

              testCase "the committed laws/ corpus matches the exported vectors"
              <| fun _ ->
                  if not (Directory.Exists corpusDir) then
                      skiptest
                          "wire-format-fixtures/ absent (single-repo or worktree checkout) — set FUARAN_WIRE_FIXTURES to check it"
                  else
                      let check (path: string) (expected: string) (what: string) =
                          if not (File.Exists path) then
                              failtestf "%s is missing from the corpus — regenerate with `%s`" what regenCommand
                          else
                              Expect.equal
                                  (File.ReadAllText(path).Replace("\r\n", "\n"))
                                  expected
                                  (sprintf
                                      "%s is stale relative to LawVectorExport — regenerate with `%s`"
                                      what
                                      regenCommand)

                      check
                          (LawVectorExport.capabilityPath corpusDir)
                          (LawVectorExport.renderCapabilityVectors ())
                          "laws/capability-laws.json"

                      check
                          (LawVectorExport.manifestPath corpusDir)
                          (LawVectorExport.renderManifest ())
                          "laws/manifest.json" ]
