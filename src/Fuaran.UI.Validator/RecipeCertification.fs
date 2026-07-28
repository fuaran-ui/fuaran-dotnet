module Fuaran.UI.Validator.RecipeCertification

// ============================================================================
//  Recipe / fragment certification — lifting UI validation to the FUNCTION
//  level by adopting `Fuaran.Core.Conformance.verifyFunction` /
//  `verifyFunctionSymbolic` (Phase 359).
//
//  The shipped validity checks answer "is *this emitted tree* valid?". A
//  parameterised fragment (`Fuaran.UI.ParamFragment`) is a FUNCTION of its
//  declared holes, so the stronger question is "does this function produce a
//  valid `Node<'Msg>` tree for ALL sampled / symbolic bindings of its holes?".
//  A fragment certified over its bounded value-spaces is a
//  CORRECT-BY-CONSTRUCTION template — e.g. a tabs recipe whose header/child
//  count is driven by a `Repeat(count, IntRange(1, 7))` hole is proven to
//  never violate the 1:1 header/child rule (FUARAN047) at any count in range,
//  before a single instance is emitted.
//
//  How the pieces fit:
//
//   1. THE ORACLE — `uiValidityRegistry`. `verifyFunction` demands a
//      `Fuaran.Core.Validator.Registry<'Node,'Id>` as its VALIDITY ORACLE. We
//      bridge the shipped runtime rule family (`Fuaran.UI.PreEmitValidate`,
//      which walks a `Node<'Msg>` and surfaces the tree-shape invariants —
//      duplicate/empty ids, the tabs 1:1 header/child + tag rules) INTO a Core
//      registry: one `RuleFamily` whose `Run` calls `PreEmitValidate.validate`
//      on the emitted tree and maps each `PreEmitDefect` to a Core `Defect`
//      (FUARAN047/048 → Error, FUARAN049 → Warning, id defects → Error). The
//      Core `verifyCase` keys `Verified` on `Severity.Error`, so a warning-only
//      tree still certifies.
//
//   2. THE WITNESS — the fragment IS the artifact-function. `verifyFunction`
//      reads the fragment's holes off an `ArtifactWitness`, projects each hole's
//      value-space into a symbolic/sampled domain, and for every binding folds
//      `w.Bind` then runs the oracle over the result. Because there is no
//      canonical repeat-EXPANSION engine in the tier (a repeat hole declares a
//      count space but nothing materialises it), the ONE genuinely
//      recipe-specific step — how a binding shapes the emitted tree — is
//      supplied by the recipe author as a `materialize` function (the same
//      "host supplies the one cross-domain step" shape Phase 358 used for
//      `embed`). The witness `Bind` accumulates each bound value into the
//      decl's hole defaults; the oracle's rule family materialises the
//      fully-bound decl via the supplied `materialize` and validates the
//      EMITTED tree. So certification = "for every binding, materialise the
//      recipe and prove the emitted tree is validator-conformant".
//
//   3. THE HONESTY BOUNDARY (Phase 52). `verifyFunction` certifies STRUCTURAL
//      validity over the param space — never output quality, and for a
//      non-deterministic (effecting) fragment never determinism. We surface the
//      fragment's declared effect class on the verdict so a consumer reads a
//      certified non-deterministic recipe as "structure-only certified".
//
//  This module lands in the PUBLIC `Fuaran.UI.Validator` tier: generic fragment
//  certification, no private / orchestration / eval vocabulary. It references
//  `Fuaran.UI` (the runtime tree + `PreEmitValidate` + `Fragment`) and
//  `Fuaran.Core.Conformance` (the `verifyFunction` operator). The validator is
//  a build-time .NET tool (FCS-based), so this code is not Fable-distributed.
// ============================================================================

open Fuaran.UI
open Fuaran.UI.Types

module Conf = Fuaran.Core.Conformance

// ─── The materializer contract ───────────────────────────────────────────────

/// The recipe-specific step: given a full binding of the fragment's holes
/// (value / repeat holes keyed by hole NAME), produce the emitted `Node<'Msg>`
/// tree the recipe yields for those bindings. This is the one step the recipe
/// author owns — how a hole binding (e.g. a tabs `count`) shapes the emitted
/// tree (e.g. `count` tab children with `count` matching headers). The
/// certification harness enumerates the bindings and proves the emitted tree is
/// validator-conformant for every one.
///
/// A value hole binds an `int` / `float` / `string` (per its value-space); a
/// repeat hole binds its `int` count. Values arrive already validated against
/// their hole's value-space, boxed as `obj` (the same shape
/// `Fuaran.UI.FragmentApply` seeds value bindings).
type Materialize<'Msg> = Map<string, obj> -> Node<'Msg>

// ─── The certification verdict ───────────────────────────────────────────────

/// One binding + the defect it triggered — the "readable counterexample"
/// (binding, defect) the acceptance calls for.
type Counterexample =
    {
        /// The offending binding, rendered `hole=value, hole=value`.
        Binding: string
        /// The defect(s) the oracle faulted the emitted tree with.
        Defect: string
    }

/// How the fragment's hole-space was covered — carried straight from the Core
/// verdict so a consumer never reads "certified" without knowing whether the
/// whole finite space was enumerated or a bounded sample was drawn.
type Coverage =
    /// The whole finite hole-space was enumerated (N cases).
    | Exhaustive of cases: int
    /// A sample of `drawn` cases was taken from a space of `spaceSize` (`None`
    /// when a hole ranges over an unbounded / genuinely-large space).
    | Sampled of drawn: int * spaceSize: int option

/// The per-recipe certification verdict. `Certified` is `true` iff the recipe
/// emitted a validator-conformant (no `Severity.Error`) tree for every covered
/// binding. `StructureOnly` records the honesty boundary (Phase 52): a
/// non-deterministic fragment is certified for STRUCTURE only — never output
/// determinism or quality.
type CertificationVerdict =
    {
        RecipeName: string
        Certified: bool
        Coverage: Coverage
        /// `true` when the fragment's declared effect is not pure-deterministic —
        /// the verdict then asserts structural validity ONLY.
        StructureOnly: bool
        Counterexample: Counterexample option
    }

// ─── UI ↔ Core effect/space projection (structural mirrors) ──────────────────

let private toCoreSpace (s: HoleValueSpace) : Fuaran.Core.ValueSpace =
    match s with
    | HoleValueSpace.IntRange(lo, hi) -> Fuaran.Core.IntRange(lo, hi)
    | HoleValueSpace.FloatRange(lo, hi) -> Fuaran.Core.FloatRange(lo, hi)
    | HoleValueSpace.StringLen(lo, hi) -> Fuaran.Core.StringLen(lo, hi)
    | HoleValueSpace.Enum choices -> Fuaran.Core.Enum choices
    | HoleValueSpace.AnyString -> Fuaran.Core.AnyString

let private toCoreEffect (e: EffectClass) : Fuaran.Core.EffectClass =
    { Host =
        match e.HostEffect with
        | HostEffect.Pure -> Fuaran.Core.Pure
        | HostEffect.ReadsHost -> Fuaran.Core.ReadsHost
        | HostEffect.WritesHost -> Fuaran.Core.WritesHost
      Determinism =
        match e.Determinism with
        | DeterminismSource.Deterministic -> Fuaran.Core.Deterministic
        | DeterminismSource.Clock -> Fuaran.Core.Clock
        | DeterminismSource.Random -> Fuaran.Core.Random
        | DeterminismSource.Network -> Fuaran.Core.Network }

let private rawId (NodeId s) : string = s

// ─── The validity ORACLE — PreEmitValidate as a Core Validator.Registry ─────

/// Render a `PreEmitValidate.PreEmitDefect` as a stable (code, severity,
/// message) triple. The projection itself lives beside the DU
/// (`PreEmitValidate.describe`, Phase 664) so every consumer — this oracle,
/// certification counterexamples, Fable-side hosts — shares one vocabulary.
let private renderPreEmitDefect (d: PreEmitValidate.PreEmitDefect) : string * Fuaran.Core.Severity * string =
    let code, severity, message = PreEmitValidate.describe d

    let coreSeverity =
        match severity with
        | PreEmitValidate.DefectSeverity.Error -> Fuaran.Core.Severity.Error
        | PreEmitValidate.DefectSeverity.Warning -> Fuaran.Core.Severity.Warning

    code, coreSeverity, message

/// The UI validity oracle as a `Fuaran.Core.Validator.Registry<Node<'Msg>,
/// NodeId>`: one rule family that runs the shipped `PreEmitValidate` tree walk
/// over the emitted tree and lowers each `PreEmitDefect` into a Core `Defect`.
/// This is the oracle `verifyFunction` drives — the same rule bodies that gate
/// a single emitted tree, now the per-binding validity check.
let uiValidityRegistry<'Msg> : Fuaran.Core.Validator.Registry<Node<'Msg>, NodeId> =
    let family: Fuaran.Core.RuleFamily<Node<'Msg>, NodeId> =
        { Id = "Fuaran.UI.PreEmitValidate"
          Run =
            fun _w root ->
                match PreEmitValidate.validate root with
                | Ok() -> []
                | Error defects ->
                    defects
                    |> List.map (fun d ->
                        let code, sev, msg = renderPreEmitDefect d

                        { Code = code
                          Severity = sev
                          Message = msg
                          Node = None }) }

    { Families = [ family ] }

// ─── The certification witness ───────────────────────────────────────────────
//
// `verifyFunctionSymbolic` reads holes off the witness, projects each value /
// repeat hole's space into a symbolic domain, and folds `w.Bind` per hole. Our
// witness accumulates each bound value into the decl's hole DEFAULTS; the oracle
// rule family then materialises the fully-bound decl (via the supplied
// `materialize`) and validates the EMITTED tree. The witness `Tree` accessors
// route through `Fuaran.UI` container access so the module takes no renderer /
// Ops dependency.

let private getChildren<'Msg> (node: Node<'Msg>) : Node<'Msg> list option =
    match node.Kind with
    | NodeKind.Box s -> Some s.Children
    | NodeKind.SplitPanel s -> Some s.Children
    | NodeKind.Tabs s -> Some s.Children
    | NodeKind.Stepper s -> Some s.Children
    | NodeKind.SummaryList s -> Some s.Children
    | NodeKind.Disclosure s -> Some s.Children
    | NodeKind.Modal s -> Some s.Children
    | NodeKind.ScrollArea s -> Some s.Children
    | _ -> None

let private kindTag<'Msg> (node: Node<'Msg>) : string =
    // Phase 692 — the four families reported their CATEGORY here, the structural
    // six their case name. `Kind.category` preserves that surface exactly.
    match node.Kind with
    | NodeKind.Custom _ -> "Custom"
    | NodeKind.ErrorBoundary _ -> "ErrorBoundary"
    | NodeKind.Switch _ -> "Switch"
    | NodeKind.FragmentDecl _ -> "FragmentDecl"
    | NodeKind.FragmentRef _ -> "FragmentRef"
    | NodeKind.Mount _ -> "Mount"
    | k ->
        match Kind.category k with
        | NodeCategory.Layout -> "Layout"
        | NodeCategory.Display -> "Display"
        | NodeCategory.Input -> "Input"
        | NodeCategory.Visualisation -> "Visualisation"
        | NodeCategory.Structural -> "Structural"

/// Parse a Core value-arg string back into the boxed `obj` a UI value/repeat
/// hole binds (mirrors `HoleValueSpace.validate`'s expected boxings) — the
/// witness receives Core `ValueArg` strings and must reconstitute the typed
/// binding the materialiser reads.
let private parseArg (space: HoleValueSpace) (raw: string) : Scalar =
    // Typed `Scalar` since the swap (the HoleDecl default payload).
    match space with
    | HoleValueSpace.IntRange _ ->
        match System.Int32.TryParse raw with
        | true, n -> Scalar.Int n
        | _ -> Scalar.Str raw
    | HoleValueSpace.FloatRange _ ->
        match
            System.Double.TryParse(
                raw,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture
            )
        with
        | true, f -> Scalar.Float f
        | _ -> Scalar.Str raw
    | HoleValueSpace.StringLen _
    | HoleValueSpace.Enum _
    | HoleValueSpace.AnyString -> Scalar.Str raw

/// The absolute lexical address of a fragment-decl hole: `<declId>.<holeName>`.
let private holeAddr (declId: string) (holeName: string) : string = declId + "." + holeName

/// Build the certification witness for a fragment decl node, closing over the
/// recipe's `materialize`. `Holes` projects the decl's value / repeat holes at
/// their absolute address; `Effect` reads the decl's declared class; `Bind`
/// records a value binding into the decl's matching hole default (so after a
/// full fold the decl carries every binding as a default the materialiser reads
/// off it). Slot holes are not certified through this witness (a slot's subtree
/// is not a value-space to enumerate) — they are held as the fragment's fixed
/// body; certification varies only the value / repeat holes.
let private certifyWitness<'Msg> (materialize: Materialize<'Msg>) : Fuaran.Core.ArtifactWitness<Node<'Msg>, NodeId> =
    let holesOf (node: Node<'Msg>) : Fuaran.Core.HoleDecl list =
        match node.Kind with
        | NodeKind.FragmentDecl spec ->
            let (NodeId declId) = node.Id

            spec.Holes
            |> List.choose (fun h ->
                match h with
                | HoleDecl.Value(n, space, _) ->
                    Some
                        { Addr = holeAddr declId n
                          Name = n
                          Kind = Fuaran.Core.ValueHole(toCoreSpace space) }
                | HoleDecl.Repeat(n, space) ->
                    Some
                        { Addr = holeAddr declId n
                          Name = n
                          Kind = Fuaran.Core.RepeatHole(toCoreSpace space) }
                | HoleDecl.Slot _ -> None)
        | _ -> []

    let effectOf (node: Node<'Msg>) : Fuaran.Core.EffectClass =
        match node.Kind with
        | NodeKind.FragmentDecl spec -> toCoreEffect spec.Effect
        | _ -> Fuaran.Core.Effect.pureDeterministic

    // Record hole@addr := value into the decl's matching hole default.
    let bind (addr: string) (arg: Fuaran.Core.Arg<Node<'Msg>>) (node: Node<'Msg>) : Result<Node<'Msg>, string> =
        match node.Kind with
        | NodeKind.FragmentDecl spec ->
            let (NodeId declId) = node.Id
            let prefix = declId + "."

            if not (addr.StartsWith prefix) then
                Error(sprintf "hole address '%s' is not under fragment decl '%s'" addr declId)
            else
                let holeName = addr.Substring prefix.Length

                match arg with
                | Fuaran.Core.ValueArg raw ->
                    match spec.Holes |> List.tryFind (fun h -> HoleDecl.name h = holeName) with
                    | Some(HoleDecl.Value(n, space, _)) ->
                        let holes' =
                            spec.Holes
                            |> List.map (fun h ->
                                if HoleDecl.name h = holeName then
                                    HoleDecl.Value(n, space, Some(parseArg space raw))
                                else
                                    h)

                        Ok
                            { node with
                                Kind = NodeKind.FragmentDecl { spec with Holes = holes' } }
                    | Some(HoleDecl.Repeat(n, space)) ->
                        // A repeat count is recorded the same way — as a bound default the
                        // materialiser reads (there is no in-tree Repeat marker to expand here).
                        let holes' =
                            spec.Holes
                            |> List.map (fun h ->
                                if HoleDecl.name h = holeName then
                                    HoleDecl.Value(n, space, Some(parseArg space raw))
                                else
                                    h)

                        Ok
                            { node with
                                Kind = NodeKind.FragmentDecl { spec with Holes = holes' } }
                    | Some(HoleDecl.Slot _) -> Error(sprintf "hole '%s' is a slot, not a value/repeat hole" holeName)
                    | None -> Error(sprintf "no hole named '%s' in fragment decl '%s'" holeName declId)
                | Fuaran.Core.SlotArg _ ->
                    Error(sprintf "hole '%s' bound with a slot arg through the value-certification seam" holeName)
        | _ -> Error "certification binding targets a FragmentDecl node"

    { Tree =
        { Id = fun n -> n.Id
          KindTag = kindTag
          Children = fun n -> getChildren n |> Option.defaultValue []
          ReplaceChildren = fun n _ -> n } // certification does not rewrite children structurally
      IdW =
        { ToString = rawId
          OfString = NodeId
          Equals = (=) }
      Holes = holesOf
      Effect = effectOf
      Bind = bind }

// ─── The public entry point ──────────────────────────────────────────────────

/// Collect the value / repeat holes' current bound defaults off a fully-bound
/// decl, keyed by hole NAME — the binding the `materialize` closure reads.
let private boundValues<'Msg> (node: Node<'Msg>) : Map<string, obj> =
    // The bound-value bag stays obj-typed (the materialise seam reads it);
    // the typed Scalar default unboxes at this boundary.
    let scalarObj (s: Scalar) : obj =
        match s with
        | Scalar.Int i -> box i |> Unchecked.nonNull
        | Scalar.Float f -> box f |> Unchecked.nonNull
        | Scalar.Bool b -> box b |> Unchecked.nonNull
        | Scalar.Str st -> box st |> Unchecked.nonNull

    match node.Kind with
    | NodeKind.FragmentDecl spec ->
        spec.Holes
        |> List.choose (fun h ->
            match h with
            | HoleDecl.Value(n, _, Some v) -> Some(n, scalarObj v)
            | _ -> None)
        |> Map.ofList
    | _ -> Map.empty

/// Render a Core counterexample into the readable `(binding, defect)` pair,
/// materialising the offending binding's emitted tree only to report it.
let private toCounterexample<'Msg>
    (materialize: Materialize<'Msg>)
    (fragment: Node<'Msg>)
    (cx: Conf.VerifyCounterexample<Node<'Msg>, NodeId>)
    : Counterexample =
    let (NodeId declId) = fragment.Id
    let prefix = declId + "."

    let binding =
        cx.ParamSet
        |> List.map (fun (addr, arg) ->
            let name =
                if addr.StartsWith prefix then
                    addr.Substring prefix.Length
                else
                    addr

            match arg with
            | Fuaran.Core.ValueArg v -> name + "=" + v
            | Fuaran.Core.SlotArg _ -> name + "=<slot>")
        |> String.concat ", "

    let defect =
        match cx.Defect with
        | Conf.DidNotApply e -> sprintf "binding did not apply (%A)" e
        | Conf.ValidatorRejected ds -> ds |> List.map (fun d -> d.Code + ": " + d.Message) |> String.concat "; "
        | Conf.EffectObserved(declared, observed) -> sprintf "effect leak — declared %A, observed %A" declared observed

    { Binding = binding; Defect = defect }

/// The certification harness. `verifyFunctionSymbolic` (the bounded / symbolic
/// mode — preferred for finite hole-spaces) enumerates the fragment's value /
/// repeat hole-space exhaustively when it is small (≤ `maxCases`) and samples
/// otherwise, running the `uiValidityRegistry` oracle over each binding's
/// EMITTED tree. Returns a per-recipe verdict + a readable `(binding, defect)`
/// counterexample on the first failing binding. Deterministic — the same seed
/// reproduces the verdict.
///
/// `materialize` is the recipe-specific step: how a hole binding shapes the
/// emitted `Node<'Msg>` tree (the oracle validates that emitted tree, so the
/// materialiser must produce the tree the recipe yields — headers aligned to
/// children, etc.). The harness proves the recipe emits a conformant tree for
/// EVERY binding, not just a sampled instance.
///
/// **Honesty boundary (Phase 52).** A fragment whose declared effect is not
/// pure-deterministic is certified for STRUCTURE only — the verdict's
/// `StructureOnly` flag is set; certification never asserts output determinism
/// or quality for such a recipe.
let certifyFragment<'Msg>
    (recipeName: string)
    (fragment: Node<'Msg>)
    (materialize: Materialize<'Msg>)
    (maxCases: int)
    (seed: int)
    : CertificationVerdict =
    // The oracle rule family must materialise the fully-bound decl before
    // validating — wrap `uiValidityRegistry`'s family so it swaps the (bound)
    // decl node for its emitted tree first.
    let materialisingRegistry: Fuaran.Core.Validator.Registry<Node<'Msg>, NodeId> =
        let inner = uiValidityRegistry<'Msg>

        let wrap (fam: Fuaran.Core.RuleFamily<Node<'Msg>, NodeId>) : Fuaran.Core.RuleFamily<Node<'Msg>, NodeId> =
            { fam with
                Run =
                    fun w node ->
                        // `node` is the bound decl (post-fold); materialise it.
                        let emitted =
                            match node.Kind with
                            | NodeKind.FragmentDecl _ -> materialize (boundValues node)
                            | _ -> node

                        fam.Run w emitted }

        { Families = inner.Families |> List.map wrap }

    let witness = certifyWitness materialize

    // Slot holes are held as the fixed body; symbolic verification varies only
    // value / repeat holes, so `fixedArgs` is empty (no slot to pin).
    let report =
        Conf.verifyFunctionSymbolic witness fragment materialisingRegistry Map.empty maxCases seed

    let coverage =
        match report.Coverage with
        | Conf.Exhaustive n -> Exhaustive n
        | Conf.Sampled(drawn, size) -> Sampled(drawn, size)

    let structureOnly =
        match fragment.Kind with
        | NodeKind.FragmentDecl spec ->
            let e = spec.Effect

            not (
                e.HostEffect = HostEffect.Pure
                && e.Determinism = DeterminismSource.Deterministic
            )
        | _ -> false

    { RecipeName = recipeName
      Certified = report.Verified
      Coverage = coverage
      StructureOnly = structureOnly
      Counterexample = report.Counterexample |> Option.map (toCounterexample materialize fragment) }

/// Render a verdict as one readable line — the per-recipe certification report a
/// publish gate / eval floor prints. A certified recipe reports its coverage; a
/// failing one reports the `(binding, defect)` counterexample.
let renderVerdict (v: CertificationVerdict) : string =
    let coverage =
        match v.Coverage with
        | Exhaustive n -> sprintf "exhaustive over %d bindings" n
        | Sampled(drawn, Some size) -> sprintf "sampled %d of %d bindings" drawn size
        | Sampled(drawn, None) -> sprintf "sampled %d bindings (unbounded space)" drawn

    let honesty =
        if v.StructureOnly then
            " [structure-only — non-deterministic recipe]"
        else
            ""

    match v.Certified, v.Counterexample with
    | true, _ -> sprintf "CERTIFIED  %s — %s%s" v.RecipeName coverage honesty
    | false, Some cx -> sprintf "REJECTED   %s — { %s } ⇒ %s" v.RecipeName cx.Binding cx.Defect
    | false, None -> sprintf "REJECTED   %s — %s (no counterexample captured)" v.RecipeName coverage
