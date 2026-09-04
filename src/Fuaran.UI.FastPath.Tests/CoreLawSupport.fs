namespace Fuaran.UI.FastPath.Tests

// ============================================================================
//  Phase 1478 — the witness, fixtures and generators the Core conformance
//  kit's function-registry / capability / memo / verify families are
//  instantiated over in THIS project.
//
//  WHAT THE FASTPATH SEAM ACTUALLY IS, stated first because it decides which
//  of the families this phase carries can be instantiated over the tier and
//  which can only be run as the pinned kit's own self-contained evidence.
//
//  `Fuaran.UI.FastPath` is a signature-searchable pattern bank built on
//  `Fuaran.Core.Function`: a `Pattern` declares Core `HoleDecl`s, `bank`
//  projects each to a Core `Signature`, mints a `Capability`, and registers it
//  in a Core `FunctionRegistry`; `find` is `FunctionRegistry.findBySignature`
//  verbatim. So the tier genuinely OWNS an artifact-function algebra — holes,
//  signatures, capabilities, registry search — and the families that quantify
//  over that algebra are instantiated over it here.
//
//  `PatternFn` is that algebra as the Core `ArtifactWitness` contract sees it:
//  a bank pattern, the hole values bound into it so far, and the sub-functions
//  composed into its declared slot holes. Binding a value hole is currying the
//  pattern (`FastPath.instantiate` takes exactly that `Map<string, string>`);
//  the reduction of a fully-bound function is the real Fuaran tree it builds.
//
//  TWO PROPERTIES OF THE TIER THIS ENCODING MAKES VISIBLE, both deliberate:
//
//   1. `Pattern` is NOT an F# equality type — it carries a `Build` closure —
//      and every law family here compares artifacts with `=`. So `PatternFn`
//      carries the pattern's ID and re-resolves the record through
//      `patternIndex` rather than embedding it. This is the same impedance
//      mismatch `Fuaran.UI.Tests/CoreAdoptionTests.fs` and
//      `Fuaran.UI.OpStream.Dag.Tests/CoreLawSupport.fs` solve with `EqNode`;
//      here the value that cannot carry equality is a function rather than a
//      tree, so the fix is indirection rather than a comparison wrapper.
//
//   2. `Pattern.Build : Map<string, string> -> Node<unit>` cannot receive a
//      TREE, while `FastPath.bank` DOES project a `SlotHole` into the
//      registered signature (`FastPath.sigEntryOf`). So a slot-bearing pattern
//      is registerable and searchable but not instantiable. The seed catalogue
//      declares only value holes today, so nothing enters that gap — and
//      `CoreFunctionLawTests` pins that fact so nothing can enter it silently.
//      The composition fixtures below therefore exercise the seam's SIGNATURE
//      algebra, which is real, and which is what `composeAcross` is about:
//      hygiene, disjoint-slot commutation, the effect join, and
//      apply-after-compose equalling the nested application. Nothing is
//      claimed about a slot-aware builder, because there is not one.
//
//  Fixture patterns live HERE, never in `SeedCatalogue.fs`: growing the public
//  seed is a pricing decision (the sub-estate's FastPath seed mandate), and a
//  law fixture is not a funnel primitive.
// ============================================================================

module CoreLawSupport =

    open Expecto
    open Fuaran.Core
    open Fuaran.UI

    module CoreRng = Fuaran.Core.ConfRng
    module UiTree = Fuaran.UI.Fuaran

    // -----------------------------------------------------------------------
    //  the tier's artifact-function
    // -----------------------------------------------------------------------

    /// One node of a FastPath artifact-function. `Open` holds the pattern's
    /// still-unbound declared holes (addresses relative to THIS node); `Bound`
    /// the value holes already bound; `Slots` the sub-functions composed into
    /// declared slot holes, kept sorted by slot address so that composing into
    /// two disjoint slots commutes — the associativity law compares the two
    /// composed artifacts with `=`, and a fill-ordered list would fail it for
    /// a reason that is about this encoding rather than about the tier.
    type PatternFn =
        { Tag: string
          PatternId: string
          ResultType: string
          Open: HoleDecl list
          Bound: Map<string, string>
          Slots: (string * PatternFn) list
          Declared: EffectClass }

    // -----------------------------------------------------------------------
    //  the fixture patterns (this project's, never the public seed's)
    // -----------------------------------------------------------------------

    let private valueOf (values: Map<string, string>) (addr: string) (dflt: string) : string =
        match Map.tryFind addr values with
        | Some s -> s
        | None -> dflt

    let private slotHole (addr: string) (name: string) (kindConstraint: string option) : HoleDecl =
        { Addr = addr
          Name = name
          Kind = SlotHole kindConstraint }

    /// Correct by construction: every value in the declared space `[1, 5]`
    /// lowers to a non-empty node id, so the tier's own egress gate
    /// (`PreEmitValidate`, the check `FastPath.tryInstantiate` runs) passes for
    /// EVERY binding — which is what `functionVerifyLaws` asks a sound
    /// artifact-function to be.
    let soundPattern: FastPath.Pattern =
        { Id = "law-sound"
          Title = "Law fixture — sound"
          Summary = "A pattern whose whole declared hole space yields a valid tree."
          ResultType = "Markdown"
          Holes = [ FastPath.numberHole "n" "n" 1 5 ]
          Build = fun v -> UiTree.markdown ("vs-" + valueOf v "n" "1") "sound" }

    /// The deliberately-too-wide twin. Identical to `soundPattern` except that
    /// its hole admits `0`, which the builder lowers to an EMPTY node id — a
    /// `PreEmitDefect.EmptyNodeId` the tier's egress gate faults. The only
    /// difference the verify laws see is the width of the hole, which is the
    /// point: the counterexample they must surface names the binding.
    let brokenPattern: FastPath.Pattern =
        { Id = "law-broken"
          Title = "Law fixture — broken"
          Summary = "A pattern whose hole admits a value the egress gate faults."
          ResultType = "Markdown"
          Holes = [ FastPath.numberHole "n" "n" 0 5 ]
          Build =
            fun v ->
                let n = valueOf v "n" "1"
                UiTree.markdown (if n = "0" then "" else "vb-" + n) "broken" }

    /// An inner function for the composition laws — one open value hole at
    /// address `oa`, named `x`.
    let innerAPattern: FastPath.Pattern =
        { Id = "law-inner-a"
          Title = "Law fixture — inner A"
          Summary = "A one-hole inner function for cross-slot composition."
          ResultType = "Markdown"
          Holes = [ FastPath.numberHole "oa" "x" 0 9 ]
          Build = fun v -> UiTree.markdown ("ia-" + valueOf v "oa" "0") "inner-a" }

    /// The twin of `innerAPattern` at a DISTINCT address, sharing the hole NAME
    /// `x`. The hygiene law needs two same-named holes that re-root to distinct
    /// absolute addresses; binding one must leave the other open.
    let innerBPattern: FastPath.Pattern =
        { Id = "law-inner-b"
          Title = "Law fixture — inner B"
          Summary = "The address-distinct, name-sharing twin of the inner A fixture."
          ResultType = "Markdown"
          Holes = [ FastPath.numberHole "ob" "x" 0 9 ]
          Build = fun v -> UiTree.markdown ("ib-" + valueOf v "ob" "0") "inner-b" }

    /// An outer function carrying a value hole and TWO independent typed slots.
    /// Its builder ignores the slots — see the header, note 2.
    let outerPattern: FastPath.Pattern =
        { Id = "law-outer"
          Title = "Law fixture — outer"
          Summary = "A two-slot outer function for cross-slot composition."
          ResultType = "Box"
          Holes =
            [ FastPath.numberHole "title" "title" 0 9
              slotHole "slotA" "left" (Some "Markdown")
              slotHole "slotB" "right" (Some "Markdown") ]
          Build = fun v -> UiTree.markdown ("outer-" + valueOf v "title" "0") "outer" }

    /// A one-slot host, for the memo-soundness under-declaration fixture: its
    /// root declares a pure effect while the sub-function in `slot` may declare
    /// an impure one.
    let hostPattern: FastPath.Pattern =
        { Id = "law-host"
          Title = "Law fixture — host"
          Summary = "A one-slot host whose descendant may out-effect its root."
          ResultType = "Box"
          Holes =
            [ FastPath.numberHole "title" "title" 0 9
              slotHole "slot" "body" (Some "Markdown") ]
          Build = fun v -> UiTree.markdown ("host-" + valueOf v "title" "0") "host" }

    /// Every pattern the witness can resolve: the PUBLIC seed catalogue (so the
    /// tier-shaped assertions run over the real bank, not only over fixtures)
    /// plus this project's law fixtures.
    let patternIndex: Map<string, FastPath.Pattern> =
        [ yield! SeedCatalogue.all
          yield soundPattern
          yield brokenPattern
          yield innerAPattern
          yield innerBPattern
          yield outerPattern
          yield hostPattern ]
        |> List.map (fun p -> p.Id, p)
        |> Map.ofList

    /// Unreachable by construction — every `PatternFn` is minted by `fnOf` from
    /// a pattern already in the index — but stated as a failure rather than a
    /// silent default, because a witness that quietly substituted a different
    /// pattern would make every law below certify the wrong artifact.
    let patternById (id: string) : FastPath.Pattern =
        match Map.tryFind id patternIndex with
        | Some p -> p
        | None -> failwithf "CoreLawSupport: no pattern registered under id '%s'" id

    // -----------------------------------------------------------------------
    //  constructors
    // -----------------------------------------------------------------------

    let pureEffect: EffectClass = Effect.pureDeterministic

    let impureEffect: EffectClass =
        { Host = ReadsHost
          Determinism = Random }

    /// A fresh, wholly-unbound artifact-function over a bank pattern.
    let fnOf (tag: string) (declared: EffectClass) (p: FastPath.Pattern) : PatternFn =
        { Tag = tag
          PatternId = p.Id
          ResultType = p.ResultType
          Open = p.Holes
          Bound = Map.empty
          Slots = []
          Declared = declared }

    /// Bind a value hole directly — fixture construction, not the law path.
    let withValue (addr: string) (v: string) (f: PatternFn) : PatternFn =
        { f with
            Open = f.Open |> List.filter (fun h -> h.Addr <> addr)
            Bound = Map.add addr v f.Bound }

    /// Compose a sub-function into a declared slot directly — fixture
    /// construction, not the law path.
    let withSlot (addr: string) (inner: PatternFn) (f: PatternFn) : PatternFn =
        { f with
            Open = f.Open |> List.filter (fun h -> h.Addr <> addr)
            Slots = ((addr, inner) :: f.Slots) |> List.sortBy fst }

    // -----------------------------------------------------------------------
    //  the Core witnesses over the FastPath artifact-function
    // -----------------------------------------------------------------------

    let idw: IdWitness<string> =
        { ToString = id
          OfString = id
          Equals = (=) }

    let nodew: NodeWitness<PatternFn, string> =
        { Id = fun f -> f.Tag
          KindTag = fun f -> f.ResultType
          Children = fun f -> f.Slots |> List.map snd
          ReplaceChildren =
            fun f cs ->
                // A rebuild that changed the arity would silently drop a slot
                // binding, so it is refused by leaving the node alone.
                if List.length cs <> List.length f.Slots then
                    f
                else
                    { f with
                        Slots = List.map2 (fun (k, _) c -> k, c) f.Slots cs } }

    /// Hygiene: an inner function's holes re-root UNDER the absolute address of
    /// the slot it was composed into, so two compositions into distinct slots
    /// can never capture one another even when the inner holes share a name.
    let rec private holesUnder (prefix: string) (f: PatternFn) : HoleDecl list =
        [ for h in f.Open -> { h with Addr = prefix + h.Addr }
          for slot, inner in f.Slots do
              yield! holesUnder (prefix + slot + "/") inner ]

    let rec private bindAt (rel: string) (arg: Arg<PatternFn>) (f: PatternFn) : Result<PatternFn, string> =
        match
            f.Slots
            |> List.tryFind (fun (s, _) -> rel.StartsWith(s + "/", System.StringComparison.Ordinal))
        with
        | Some(slot, inner) ->
            bindAt (rel.Substring(slot.Length + 1)) arg inner
            |> Result.map (fun inner' ->
                { f with
                    Slots = f.Slots |> List.map (fun (k, v) -> if k = slot then k, inner' else k, v) })
        | None ->
            match f.Open |> List.tryFind (fun h -> h.Addr = rel) with
            | None -> Error("no open hole at '" + rel + "'")
            | Some h ->
                let remaining = f.Open |> List.filter (fun x -> x.Addr <> rel)

                match h.Kind, arg with
                | SlotHole _, SlotArg inner ->
                    Ok
                        { f with
                            Open = remaining
                            Slots = ((rel, inner) :: f.Slots) |> List.sortBy fst }
                | (ValueHole _ | RepeatHole _), ValueArg v ->
                    Ok
                        { f with
                            Open = remaining
                            Bound = Map.add rel v f.Bound }
                | _ -> Error("the argument does not match the hole kind at '" + rel + "'")

    /// The tier's artifact witness. `Bind` CLEARS the hole it binds, so
    /// re-deriving `Function.signature` over a curried function narrows for
    /// free — the `signatureExcluding` escape hatch is for witnesses that do
    /// not, and this one does not need it.
    let witness: ArtifactWitness<PatternFn, string> =
        { Tree = nodew
          IdW = idw
          Holes = holesUnder ""
          Effect = fun f -> f.Declared
          Bind = bindAt }

    // -----------------------------------------------------------------------
    //  reduction + the memo-key encoder
    // -----------------------------------------------------------------------

    /// Reduce an artifact-function to the real Fuaran tree its pattern builds
    /// from the values bound so far — the tier's own `FastPath.instantiate`.
    let instantiate (f: PatternFn) : Fuaran.UI.Types.Node<unit> =
        FastPath.instantiate (patternById f.PatternId) f.Bound

    /// Field and item separators for the encoder below: two control bytes no
    /// hole address, name, pattern id or drawn value contains, so a field's
    /// content can never be read as a field boundary.
    let private fieldSep = "\u0001"
    let private itemSep = "\u0002"
    let private partSep = "\u0003"

    let private renderSpace (s: ValueSpace) : string =
        match s with
        | IntRange(lo, hi) -> sprintf "int[%d,%d]" lo hi
        | FloatRange(lo, hi) -> sprintf "float[%f,%f]" lo hi
        | StringLen(lo, hi) -> sprintf "len[%d,%d]" lo hi
        | Enum xs -> "enum[" + String.concat "|" xs + "]"
        | AnyString -> "any"

    let renderEffect (e: EffectClass) : string =
        let host =
            match e.Host with
            | Pure -> "pure"
            | ReadsHost -> "reads"
            | WritesHost -> "writes"

        host + "/" + Effect.determinismTag e.Determinism

    let private renderKind (k: HoleKind) : string =
        match k with
        | ValueHole s -> "value:" + renderSpace s
        | SlotHole c -> "slot:" + (defaultArg c "*")
        | RepeatHole s -> "repeat:" + renderSpace s
        | ActionHole e -> "action:" + renderEffect e

    /// The node-content encoder `Function.applyMemo` hashes into its
    /// content-addressed key. `Tree.encodeHash` joins this over a preorder
    /// walk, so each node's encoding carries its own slot NAMES — hence the
    /// tree's arity and shape — as well as its content; without them two
    /// differently-shaped trees could join to the same string.
    ///
    /// Injectivity over the generator's node space is not assumed here: it is
    /// certified by `Conformance.encoderInjectivityLaws`, which is the silent
    /// precondition of the memo families beside it.
    let encode (f: PatternFn) : string =
        String.concat
            fieldSep
            [ "tag=" + f.Tag
              "pattern=" + f.PatternId
              "kind=" + f.ResultType
              "effect=" + renderEffect f.Declared
              "open="
              + (f.Open
                 |> List.sortBy (fun h -> h.Addr)
                 |> List.map (fun h -> h.Addr + partSep + h.Name + partSep + renderKind h.Kind)
                 |> String.concat itemSep)
              "bound="
              + (f.Bound
                 |> Map.toList
                 |> List.map (fun (a, v) -> a + partSep + v)
                 |> String.concat itemSep)
              "slots=" + (f.Slots |> List.map fst |> String.concat itemSep) ]

    // -----------------------------------------------------------------------
    //  the domain validator — the tier's OWN egress gate, as a Core registry
    // -----------------------------------------------------------------------

    /// `verifyFunction` drives a domain `Validator.Registry` as its validity
    /// oracle. The tier's oracle already exists and is not invented here: it is
    /// `PreEmitValidate.validate`, the FGP-7 egress check
    /// `FastPath.tryInstantiate` runs before a built tree leaves the bank. Each
    /// node of an artifact-function is instantiated and put through it, so the
    /// verdict the laws read is the verdict the shipped seam would give.
    let validatorRegistry: Validator.Registry<PatternFn, string> =
        Validator.empty<PatternFn, string>
        |> Validator.register (
            Validator.perNode "fastpath/pre-emit" (fun _ f ->
                match PreEmitValidate.validate (instantiate f) with
                | Ok() -> []
                | Error defects ->
                    defects
                    |> List.map (fun d ->
                        let code, severity, message = PreEmitValidate.describe d

                        { Code = code
                          Severity =
                            match severity with
                            | PreEmitValidate.DefectSeverity.Error -> Severity.Error
                            | PreEmitValidate.DefectSeverity.Warning -> Severity.Warning
                          Message = message
                          Node = Some f.Tag }))
        )

    // -----------------------------------------------------------------------
    //  generators
    // -----------------------------------------------------------------------

    /// Draw one in-space value from a value space. `None` only where the space
    /// has no inhabitant to draw (an empty `Enum`), which no fixture declares.
    let private drawSpace (space: ValueSpace) (rng: CoreRng.T) : string option * CoreRng.T =
        match space with
        | IntRange(lo, hi) ->
            let k, r = CoreRng.intBelow (max 1 (hi - lo + 1)) rng
            Some(string (lo + k)), r
        | Enum xs ->
            if List.isEmpty xs then
                None, rng
            else
                let k, r = CoreRng.intBelow (List.length xs) rng
                Some(List.item k xs), r
        | StringLen(lo, hi) ->
            let k, r = CoreRng.intBelow (max 1 (hi - lo + 1)) rng
            Some(String.replicate (lo + k) "a"), r
        | AnyString ->
            let k, r = CoreRng.intBelow 1000 rng
            Some("s" + string k), r
        | FloatRange(lo, hi) ->
            let k, r = CoreRng.intBelow 1001 rng
            Some(sprintf "%f" (lo + (hi - lo) * (float k / 1000.0))), r

    /// A full, in-space param-set for every value hole the function still
    /// declares — the `genParams` the verification laws drive. Slot and action
    /// holes are skipped: the first is bound by composition, the second on the
    /// behaviour axis by `bindHandlers`, and `bindArgs` excludes it from the
    /// strict-application demand for exactly that reason.
    let genParams (fn: PatternFn) (rng: CoreRng.T) : Map<string, Arg<PatternFn>> * CoreRng.T =
        let mutable r = rng
        let mutable acc = Map.empty

        for h in witness.Holes fn do
            match h.Kind with
            | ValueHole space
            | RepeatHole space ->
                let drawn, r' = drawSpace space r
                r <- r'

                match drawn with
                | Some v -> acc <- Map.add h.Addr (ValueArg v) acc
                | None -> ()
            | SlotHole _
            | ActionHole _ -> ()

        acc, r

    // -----------------------------------------------------------------------
    //  the law samples
    // -----------------------------------------------------------------------

    /// The composition sample. `Outer` carries two typed slots plus a value
    /// hole; `ClosedInner` is fully bound, so it contributes no holes to the
    /// composed signature; `OpenInnerA` / `OpenInnerB` each carry one open hole
    /// named `x` at distinct addresses.
    let drawComposition (rng: CoreRng.T) : Fuaran.Core.Conformance.CompositionSample<PatternFn, PatternFn> * CoreRng.T =
        let title, r1 = CoreRng.intBelow 10 rng
        let closed, r2 = CoreRng.intBelow 10 r1

        { Outer = outerPattern |> fnOf "outer" pureEffect
          SlotA = "slotA"
          SlotB = "slotB"
          OuterArgs = [ "title", string title ]
          ClosedInner = innerAPattern |> fnOf "closed" pureEffect |> withValue "oa" (string closed)
          OpenInnerA = innerAPattern |> fnOf "open-a" pureEffect
          OpenInnerB = innerBPattern |> fnOf "open-b" pureEffect
          OpenHoleName = "x"
          OpenHoleArg = "3" },
        r2

    /// The memo sample. `PureFn` is memoisable with two distinct full
    /// param-sets, so the "a changed param-set misses" branch has a genuinely
    /// different key; `EffectingFn` is the same artifact declared
    /// `ReadsHost`/`Random`, which the soundness guard must bypass.
    let drawMemo (rng: CoreRng.T) : Fuaran.Core.Conformance.MemoSample<PatternFn> * CoreRng.T =
        let a, r1 = CoreRng.intBelow 5 rng
        let b, r2 = CoreRng.intBelow 5 r1
        let e, r3 = CoreRng.intBelow 5 r2

        // `1..5` is the declared space; the alternate is forced to differ so
        // the sample can never degenerate into the same param-set twice, which
        // would make the miss branch vacuous while still reporting green. The
        // offset is drawn from `1..4` rather than `1..5` on purpose: an offset
        // of five is congruent to zero, so `b = 4` would hand back the original
        // value — which is exactly what the first run of this law caught at
        // iteration 3, and is why the offset is not simply `b + 1`.
        let av = 1 + a
        let bv = 1 + ((a + (b % 4) + 1) % 5)

        { PureFn = soundPattern |> fnOf "memo-pure" pureEffect
          Args = Map.ofList [ "n", ValueArg(string av) ]
          ArgsAlt = Map.ofList [ "n", ValueArg(string bv) ]
          EffectingFn = soundPattern |> fnOf "memo-effecting" impureEffect
          EffectingArgs = Map.ofList [ "n", ValueArg(string (1 + e)) ] },
        r3

    /// The under-declared function the memo-soundness gate must refuse to
    /// cache: its ROOT declares a pure, deterministic effect while the
    /// sub-function composed into its slot declares `ReadsHost`/`Random`. The
    /// `descendantEffect` parameter is what the go-red proof perturbs — declare
    /// the descendant pure (mark a non-memoisable function cacheable) and the
    /// fixture stops being an under-declared case, which is precisely what the
    /// gate law refuses to certify.
    let underDeclaredFn (descendantEffect: EffectClass) : PatternFn =
        hostPattern
        |> fnOf "under-declared" pureEffect
        |> withSlot "slot" (innerAPattern |> fnOf "leak" descendantEffect |> withValue "oa" "4")

    let underDeclaredArgs: Map<string, Arg<PatternFn>> =
        Map.ofList [ "title", ValueArg "2" ]

    /// A tree generator for the encoder-injectivity law: varies the pattern,
    /// the tag, the bound values and whether a sub-function is composed in, so
    /// the sample spans the shapes the memo key must tell apart. Every branch
    /// consumes the same number of draws, so the stream advances at one rate
    /// whichever shape is chosen.
    let genFn (rng: CoreRng.T) : PatternFn * CoreRng.T =
        let which, r1 = CoreRng.intBelow 4 rng
        let v, r2 = CoreRng.intBelow 5 r1
        let tagRoll, r3 = CoreRng.intBelow 3 r2
        let compose, r4 = CoreRng.intBelow 2 r3
        let innerV, r5 = CoreRng.intBelow 10 r4

        let tag = "g" + string tagRoll

        let node =
            match which with
            | 0 -> soundPattern |> fnOf tag pureEffect |> withValue "n" (string (1 + v))
            | 1 -> innerAPattern |> fnOf tag pureEffect |> withValue "oa" (string v)
            | 2 -> innerBPattern |> fnOf tag impureEffect |> withValue "ob" (string v)
            | _ ->
                let host = hostPattern |> fnOf tag pureEffect |> withValue "title" (string v)

                if compose = 1 then
                    host
                    |> withSlot "slot" (innerAPattern |> fnOf "gi" pureEffect |> withValue "oa" (string innerV))
                else
                    host

        node, r5

    // -----------------------------------------------------------------------
    //  the shared assertion
    // -----------------------------------------------------------------------

    /// Mirrors `Fuaran.UI.Tests/CoreAdoptionTests.fs` and the 1476 DAG support:
    /// every law in a family must pass, and a failure prints each law with its
    /// reproducible counterexample.
    let assertAllPassed (context: string) (results: LawResult list) =
        let failures = results |> List.filter (fun r -> not r.Passed)

        if not (List.isEmpty failures) then
            failures
            |> List.map (fun r -> sprintf "  %s — %A" r.Law r.Counterexample)
            |> String.concat "\n"
            |> failtestf "%s failed:\n%s" context
