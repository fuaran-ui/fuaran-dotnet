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
    /// Phase 1647 generalised this file's own override into the ONE resolver at
    /// `tests/corpus-root/CorpusRoot.fs`; this is that same seam, shared.
    let private corpusDir =
        Fuaran.Tests.CorpusRoot.tryFind ()
        |> Option.defaultValue (Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "..", "wire-format-fixtures"))

    /// A capability to key against — the key reads only its id.
    let private capabilityWithId (id: string) : Capability =
        let declaration =
            "{\"$type\":\"capability\",\"determinism\":\"random\",\"id\":\""
            + id
            + "\",\"placement\":{\"$type\":\"server\"},\"signature\":{\"effect\":{\"determinism\":\"random\",\"host\":\"readsHost\"},\"holes\":[],\"name\":\"cap\"}}"

        match CapabilityCodec.decode declaration with
        | Ok c -> c
        | Error e -> failwithf "the key fixture did not decode: %s" e

    /// A declaration carrying an explicit `"slotTree"` space (fuaran-core#229): a slot whose space
    /// disagrees with its constraint, and a value hole ranging over trees of one kind. The TS and Go
    /// ports pin these exact bytes.
    let private slotTreeDecl =
        "{\"$type\":\"capability\",\"determinism\":\"random\",\"id\":\"cap-tree\",\"placement\":{\"$type\":\"server\"},"
        + "\"signature\":{\"effect\":{\"determinism\":\"random\",\"host\":\"readsHost\"},\"holes\":["
        + "{\"addr\":\"body\",\"kind\":\"slot\",\"name\":\"body\",\"required\":true,\"slotKind\":\"Layout\",\"space\":{\"$type\":\"slotTree\"}},"
        + "{\"addr\":\"chart\",\"kind\":\"value\",\"name\":\"chart\",\"required\":false,\"space\":{\"$type\":\"slotTree\",\"slotKind\":\"Chart\"}}"
        + "],\"name\":\"tree\"}}"

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
                  // THE STATEMENT OF THE CONTRACT, not an observation about
                  // today's catalogue. `Pattern.Build` takes
                  // `Map<string, string>`, so a value hole is the only hole a
                  // caller can bind; a pattern declaring any other is declaring
                  // something nothing honours. This is the guard that keeps such
                  // a pattern from being authored, and the seam's own narrowing
                  // (below) is what keeps the registered signature honest if one
                  // ever is. See `CoreLawSupport.fs`, header note 2.
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

              testCase "the registered signature does not advertise a slot hole the builder cannot receive"
              <| fun _ ->
                  // The other half of the same contract, and the go-red for the
                  // narrowing. A `SlotHole`'s argument is a TREE; `Pattern.Build`
                  // takes scalars. Projected as a REQUIRED `"slot"` entry — which
                  // it was — such a pattern is findable by `findBySignature` and
                  // then uninstantiable: the caller who offered the slot has
                  // nothing to pass it through. The seam is narrowed rather than
                  // the builder widened, so a REGISTERED signature states exactly
                  // what a caller can influence. The QUERY side is untouched —
                  // a caller's declaration of their own context is faithful, and
                  // is what the shared cross-host goldens certify.
                  let slotBearing: FastPath.Pattern =
                      { Id = "test.slot-bearing"
                        Title = "a pattern declaring a hole the builder cannot receive"
                        Summary = "fixture only — never a seed"
                        ResultType = "Markdown"
                        Holes =
                          [ FastPath.textHole "body" "body"
                            { Addr = "inner"
                              Name = "inner"
                              Kind = SlotHole(Some "Markdown") } ]
                        Build = fun _ -> Fuaran.markdown "md" "hello" }

                  let b = FastPath.bank [ slotBearing ]

                  let holes =
                      FunctionRegistry.enumerate b.Registry
                      |> List.collect (fun e -> e.Capability.Signature.Holes)

                  Expect.equal
                      (holes |> List.map (fun h -> h.Addr))
                      [ "body" ]
                      "only the hole the builder can receive reaches the registered signature"

                  Expect.isEmpty
                      (holes |> List.filter (fun h -> h.Kind = "slot"))
                      "no signature entry advertises a slot"

                  // And the consequence that is the whole point. A caller
                  // offering exactly what the builder can receive now finds the
                  // pattern; before the narrowing it did not, because the
                  // signature also demanded a slot — so the ONLY caller who could
                  // find this pattern was one offering a slot they then had no
                  // way to pass in.
                  let byValue =
                      FastPath.findRunnable (FastPath.query [ FastPath.textHole "body" "body" ] None) b

                  Expect.equal
                      (byValue |> List.map (fun p -> p.Id))
                      [ "test.slot-bearing" ]
                      "the pattern is found on exactly the holes the builder can receive"

              // ---- Core's capabilityLaws vectors, read by this tier (fuaran#1482; fuaran-core Phase 235) -----

              testCase "the capabilityLaws draw at the declared seed gets the verdicts the law demands"
              <| fun _ ->
                  // The law over the seed Core's file declares, run over the
                  // PINNED kit: the sample is a sample of a passing run. Each
                  // draw's verdict is then asserted directly against the law's
                  // demand — pin evidence, independent of any file.
                  CoreConf.capabilityLaws LawVectorExport.seed LawVectorExport.iterations
                  |> assertAllPassed "capabilityLaws over the declared seed"

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

              testCase "Core's laws/capability-laws.json in the corpus certifies the pinned kit"
              <| fun _ ->
                  // Core emits this file (fuaran-core Phase 235); this tier emits
                  // no law set. So the file is READ, the way every other host
                  // reads it: each vector is recomputed by calling the pinned
                  // kit, never trusted.
                  if not (Directory.Exists corpusDir) then
                      skiptest
                          "wire-format-fixtures/ absent (single-repo or worktree checkout) — set FUARAN_WIRE_FIXTURES to check it"
                  else
                      let path = LawVectorExport.capabilityPath corpusDir

                      if not (File.Exists path) then
                          failtestf
                              "laws/capability-laws.json is missing from the corpus — it is Core's; re-emit %s"
                              LawVectorExport.emitCommand

                      let doc =
                          match Json.parse (File.ReadAllText path) with
                          | Ok d -> d
                          | Error m -> failtestf "laws/capability-laws.json did not parse: %s" m

                      let stamp =
                          match LawVectorExport.field "kitVersion" doc with
                          | Some(JStr s) -> s
                          | _ -> failtest "laws/capability-laws.json carries no kitVersion"

                      Expect.equal
                          (LawVectorExport.field "seed" doc, LawVectorExport.field "iterations" doc)
                          (Some(JInt LawVectorExport.seed), Some(JInt LawVectorExport.iterations))
                          "the file declares the seed and sample size this tier reproduces the draw from"

                      if stamp <> LawVectorExport.kitVersion () then
                          // The cut-to-raise window: the copy describes a Core this
                          // repository does not pin yet. A host is correct to certify
                          // against what it pins, so the gap is reported, loudly and
                          // by name, never read as a pass — and it closes at the raise.
                          skiptest (
                              sprintf
                                  "CAPABILITY VECTORS NOT CERTIFIED: the corpus copy is stamped for Core %s and this repository pins %s — raise the Core pin to certify against it"
                                  stamp
                                  (LawVectorExport.kitVersion ())
                          )
                      else
                          let vectors =
                              match LawVectorExport.field "vectors" doc with
                              | Some(JArr items) -> items
                              | _ -> failtest "laws/capability-laws.json carries no `vectors` array"

                          Expect.equal
                              (List.length vectors)
                              (6 * LawVectorExport.iterations)
                              "six vectors per declared iteration"

                          let failures = vectors |> List.choose LawVectorExport.checkVector

                          Expect.isEmpty
                              failures
                              (sprintf "Core's capability vectors disagree with the pinned kit: %A" failures)

                          let captured =
                              vectors
                              |> List.choose (fun v ->
                                  match LawVectorExport.field "case" v, LawVectorExport.field "expected" v with
                                  | Some(JStr "invocationKey"), Some e ->
                                      match LawVectorExport.field "capturedValue" e with
                                      | Some(JInt n) -> Some n
                                      | _ -> None
                                  | _ -> None)

                          Expect.equal
                              captured
                              (LawVectorExport.draws () |> List.map (fun d -> d.Realized))
                              "each invocation-key vector carries its iteration's drawn capture value"

              testCase "the capability vector checker names a perturbed vector — the certification can go red"
              <| fun _ ->
                  let d = List.head (LawVectorExport.draws ())
                  let declaration = CapabilityCodec.encode d.Cap

                  let vector (verdict: string) =
                      JObj
                          [ "id", JStr "capability-0-accept"
                            "case", JStr "validateArgs"
                            "input",
                            JObj
                                [ "capability", JStr declaration
                                  "args", JArr [ JObj [ "addr", JStr "h0"; "value", JStr(string d.Lo) ] ] ]
                            "expected", JObj [ "verdict", JStr verdict ] ]

                  Expect.isNone (LawVectorExport.checkVector (vector "accept")) "the true vector passes"

                  match LawVectorExport.checkVector (vector "reject") with
                  | Some m -> Expect.stringContains m "capability-0-accept" "the perturbed vector is named by its id"
                  | None -> failtest "a perturbed verdict was not seen"

              // ---- Phase 1860: the invocation key's canonical form, pinned for every host --------
              //
              // The key used to hash the addr-sorted `addr=value` pairs joined with no separator,
              // so [a="1b=2"] and [a="1"; b="2"] shared one pre-image and one key. The pinned kit
              // (fuaran-core#225) builds the pre-image through `Hash.canonicalFields` instead. The
              // literals below are this kit's own values, and the TS and Go ports pin the same ones.

              testCase "the capability invocation key keys the formerly colliding pair apart"
              <| fun _ ->
                  let cap = capabilityWithId "cap"
                  let one = Capability.invocationKey cap [ "a", "1b=2" ]
                  let two = Capability.invocationKey cap [ "a", "1"; "b", "2" ]
                  Expect.notEqual one two "[a=\"1b=2\"] and [a=\"1\"; b=\"2\"] share a key"
                  Expect.equal one "cap#4ad0d41a" "the key every host pins for [a=\"1b=2\"]"
                  Expect.equal two "cap#53c281a5" "the key every host pins for [a=\"1\"; b=\"2\"]"

                  // The pre-225 pre-image, pinned: both argument sets joined to "a=1b=2", so both
                  // keyed to cap#73fad033 — the value the TS and Go ports produced before this phase.
                  let oldKey (args: (string * string) list) =
                      "cap#"
                      + Fuaran.Core.Hash.fnv1a (
                          args
                          |> List.sortBy fst
                          |> List.map (fun (a, v) -> a + "=" + v)
                          |> String.concat ""
                      )

                  Expect.equal (oldKey [ "a", "1b=2" ]) "cap#73fad033" "the old form's key"
                  Expect.equal (oldKey [ "a", "1"; "b", "2" ]) "cap#73fad033" "the old form collided"

              testCase "the capability invocation key matches the literals every host pins"
              <| fun _ ->
                  [ "cap-0", [ "h0", "13" ], "cap-0#70fcefc7" // the published vector capability-0
                    "cap", [], "cap#811c9dc5" // the empty pre-image: the FNV-1a offset basis
                    "cap", [ "a", "x\u0001y"; "b", "\u0010" ], "cap#3d801624" // escaped value fields
                    "cap", [ "a", "x"; "\u0001y", "\u0010" ], "cap#46a6fdda" // escaped address field
                    "cap", [ "", "1"; "\U0001F600", "2" ], "cap#e3651ae3" // UTF-16 ordinal sort
                    "cap", [ "a", "2"; "a", "1" ], "cap#1d4e4ee6" // a stable sort
                    "cap", [ "a", "1"; "a", "2" ], "cap#c79fd87e" ]
                  |> List.iter (fun (id, args, want) ->
                      Expect.equal (Capability.invocationKey (capabilityWithId id) args) want (sprintf "%s %A" id args))

              testCase "an explicit slotTree space round-trips and validates — the declaration every host pins"
              <| fun _ ->
                  match CapabilityCodec.decode slotTreeDecl with
                  | Error e -> failtestf "the kit refused the pinned slotTree declaration: %s" e
                  | Ok cap ->
                      Expect.equal
                          (CapabilityCodec.encode cap)
                          slotTreeDecl
                          "the pinned declaration is the kit's own canonical encoding"

                      let verdict args =
                          match Capability.validateArgs cap args with
                          | Ok() -> "accept"
                          | Error(UninvocableArg _) -> "UninvocableArg"
                          | Error(ArgOutOfSpace _) -> "ArgOutOfSpace"
                          | Error e -> sprintf "%A" e

                      [ [ "body", "{\"kind\":\"Text\"}" ], "accept"
                        [ "body", "13" ], "UninvocableArg"
                        [ "body", "{\"kind\":\"Text\"}"; "chart", "{\"kind\":\"Chart\"}" ], "accept"
                        [ "body", "{\"kind\":\"Text\"}"; "chart", "{\"kind\":\"Text\"}" ], "ArgOutOfSpace"
                        [ "body", "{\"kind\":" ], "UninvocableArg"
                        [ "body", "{\"kind\":1}" ], "UninvocableArg" ]
                      |> List.iter (fun (args, want) -> Expect.equal (verdict args) want (sprintf "%A" args))

              testCase "laws/manifest.json's capabilityLaws row describes Core's file"
              <| fun _ ->
                  if not (Directory.Exists corpusDir) then
                      skiptest
                          "wire-format-fixtures/ absent (single-repo or worktree checkout) — set FUARAN_WIRE_FIXTURES to check it"
                  else
                      // The index is HAND-CURATED: it spans every family in
                      // `laws/`, so it is read structurally, and only this
                      // family's row is asserted — against the file it indexes,
                      // not against this repository's pin.
                      let manifestPath = LawVectorExport.manifestPath corpusDir

                      if not (File.Exists manifestPath) then
                          failtest "laws/manifest.json is missing from the corpus — it is curated by hand, not emitted"

                      let parse (path: string) =
                          match Json.parse (File.ReadAllText path) with
                          | Ok doc -> doc
                          | Error m -> failtestf "%s did not parse: %s" path m

                      let manifest = parse manifestPath
                      let file = parse (LawVectorExport.capabilityPath corpusDir)
                      let field = LawVectorExport.field

                      let entry =
                          match field "families" manifest with
                          | Some(JArr families) ->
                              match families |> List.tryFind (fun f -> field "id" f = Some(JStr "capabilityLaws")) with
                              | Some e -> e
                              | None -> failtest "laws/manifest.json lists no `families` entry with id `capabilityLaws`"
                          | _ -> failtest "laws/manifest.json carries no `families` array"

                      let indexed (name: string) (expected: JVal option) =
                          Expect.equal
                              (field name entry)
                              expected
                              (sprintf
                                  "laws/manifest.json's capabilityLaws `%s` disagrees with laws/capability-laws.json — the manifest is curated by hand, so correct it there"
                                  name)

                      indexed "file" (Some(JStr LawVectorExport.capabilityFileName))
                      indexed "kitVersion" (field "kitVersion" file)
                      indexed "seed" (field "seed" file)
                      indexed "iterations" (field "iterations" file)

                      indexed
                          "vectors"
                          (match field "vectors" file with
                           | Some(JArr items) -> Some(JInt(List.length items))
                           | _ -> None) ]
