namespace Fuaran.UI

open Fuaran.UI.Types

// ============================================================================
//  Fragment — the artifact-function abstraction (Phase 180).
//
//  A saved typed tree behaves as a FUNCTION of declared holes: lambda
//  abstraction over the artifact substrate. This is the spatial-graph
//  counterpart to the temporal-graph op-stream DAG (Phase 178/179). It proves
//  the abstraction in the shipped UI tier (the lowest-cost first instance)
//  before any `Fuaran.Core.Function` extraction (Phase 181, rule-of-three-
//  gated).
//
//  A `ParamFragment` declares typed HOLES — value parameters (a name + a value-
//  space + an optional default) and tree-typed SLOTS (a name + an optional kind
//  constraint). A consumer APPLIES it by binding holes to arguments; binding a
//  SUBSET of holes is partial application (`curry`), yielding a narrower
//  fragment — the formal basis for content packs. A zero-hole fragment is
//  exactly today's fixed-body fragment (the degenerate case).
//
//  The three decide-now invariants from the hypothesis (§5) are stamped here so
//  they never need retrofitting:
//   1. TOTALITY — a repeat/iteration count is a literal or a validated-range
//      parameter only; binding can never produce unbounded expansion.
//   2. HYGIENE — capture-avoiding application by lexical hole-addressing
//      (`<refId>.<holeName>`); two refs binding the same fragment with different
//      args cannot collide (enforced by the renderer-side apply).
//   3. EFFECT SIGNATURE — a total, checked two-axis effect class (host-effect ×
//      determinism-source), joined componentwise through composition.
//
//  FGP 2: `FSharp.Core` + `Fuaran.UI.Types` only — Fable-clean, no renderer dep.
//  The tree-substituting apply (slot binding, hygienic id namespacing, totality
//  budget) lives renderer-side; this module owns the type surface + the pure
//  laws (signature derivation, arg validation, currying, the effect join).
// ============================================================================

// NOTE (Phase 180 wire integration): the hole + effect TYPES (`HoleValueSpace`,
// `HoleDecl`, `HostEffect`, `DeterminismSource`, `EffectClass`) and their
// companion modules now live in `Fuaran.UI.Types` (ahead of the `Node` chain) so
// the wire-coupled `FragmentDeclSpec` / `FragmentRefSpec` can carry them. This
// module owns the alias + the pure laws below.

/// A parameterised fragment — the first concrete artifact-function. This is an
/// alias for the wire-coupled `FragmentDeclSpec<'Msg>`: a saved tree IS a
/// function of its declared `Holes`. `Body` is the template (hole sites are
/// marked by convention — value holes via `Binding.State`/`TextSource` keyed by
/// the hole name, slots via an unbound `FragmentRef` named for the slot — bound
/// by the renderer-side apply). A zero-hole fragment is the degenerate
/// fixed-body case. `Effect` is the declared two-axis class.
type ParamFragment<'Msg> = FragmentDeclSpec<'Msg>

/// One entry in a derivable signature — the introspection surface Phase 182
/// projects into the AiTools tool-catalogue.
type HoleSignatureEntry =
    { Name: string
      Kind: string // "value" | "slot" | "repeat"
      Space: HoleValueSpace option
      Required: bool }

/// The derivable signature of a parameterised fragment: its holes (names +
/// value-spaces + optionality) and its effect class. A pure function of the
/// fragment.
type FragmentSignature =
    { Name: string
      Holes: HoleSignatureEntry list
      Effect: EffectClass }

module Fragment =
    // Since the swap the generated `FragmentDeclSpec` carries `Holes` /
    // `Effect` as OPTIONS (`None` ≡ the old `[]` / pure-deterministic
    // degenerate shape) and `Name` as a bare string — the laws below
    // materialise the degenerate defaults locally so their surface types are
    // unchanged.
    let private holesOf (pf: ParamFragment<'Msg>) : HoleDecl list = pf.Holes |> Option.defaultValue []

    let private entryOf (h: HoleDecl) : HoleSignatureEntry =
        match h with
        | HoleDecl.Value(n, space, def) ->
            { Name = n
              Kind = "value"
              Space = Some space
              Required = Option.isNone def }
        | HoleDecl.Slot(n, _) ->
            { Name = n
              Kind = "slot"
              Space = None
              Required = true }
        | HoleDecl.Repeat(n, space) ->
            { Name = n
              Kind = "repeat"
              Space = Some space
              Required = true }

    /// Derive the signature (Phase 182 projects this into the tool catalogue).
    let signature (pf: ParamFragment<'Msg>) : FragmentSignature =
        { Name = pf.Name
          Holes = holesOf pf |> List.map entryOf
          Effect = pf.Effect |> Option.defaultValue EffectClass.pureDeterministic }

    /// TOTALITY (invariant 1) over the whole fragment: every `Repeat` hole's
    /// count value-space is bounded.
    let isTotal (pf: ParamFragment<'Msg>) : bool =
        holesOf pf |> List.forall HoleDecl.isTotal

    /// The required (no-default) holes that a COMPLETE application must bind.
    let requiredHoles (pf: ParamFragment<'Msg>) : string list =
        holesOf pf |> List.filter HoleDecl.isRequired |> List.map HoleDecl.name

    /// Validate a value argument against the named hole's value-space. A slot /
    /// repeat hole is not value-validatable here (the renderer-side apply binds
    /// its subtree); an unknown hole is a defect.
    let validateValueArg (pf: ParamFragment<'Msg>) (holeName: string) (arg: obj) : Result<obj, string> =
        match holesOf pf |> List.tryFind (fun h -> HoleDecl.name h = holeName) with
        | Some(HoleDecl.Value(_, space, _)) -> HoleValueSpace.validate space arg
        | Some(HoleDecl.Repeat(_, space)) -> HoleValueSpace.validate space arg
        | Some(HoleDecl.Slot _) -> Error(sprintf "hole '%s' is a tree slot, not a value" holeName)
        | None -> Error(sprintf "no hole named '%s'" holeName)

    /// PARTIAL APPLICATION (currying): bind a SUBSET of value holes, returning a
    /// NARROWER fragment with those holes removed (their bound values become the
    /// new defaults) — the content-pack formalism. Each bound value is validated
    /// against its hole's space; an unknown / non-value hole, or a value-space
    /// violation, fails. Unbound holes remain open (not an error — that is the
    /// whole point of currying).
    let curry (pf: ParamFragment<'Msg>) (boundValues: Map<string, obj>) : Result<ParamFragment<'Msg>, string> =
        let rec go (holes: HoleDecl list) (acc: HoleDecl list) : Result<HoleDecl list, string> =
            match holes with
            | [] -> Ok(List.rev acc)
            | h :: rest ->
                match h with
                | HoleDecl.Value(n, space, _) when Map.containsKey n boundValues ->
                    match HoleValueSpace.validate space (Map.find n boundValues) with
                    | Ok v ->
                        // The default slot is a typed `Scalar` since the swap; the
                        // validated arg is one of the scalar shapes `validate`
                        // admits (int/float/string — bool for completeness).
                        let scalar =
                            match v with
                            | :? int as i -> Some(Scalar.Int i)
                            | :? float as fl -> Some(Scalar.Float fl)
                            | :? bool as b -> Some(Scalar.Bool b)
                            | :? string as s -> Some(Scalar.Str s)
                            | _ -> None

                        go rest (HoleDecl.Value(n, space, scalar) :: acc)
                    | Error e -> Error(sprintf "binding '%s': %s" n e)
                | _ -> go rest (h :: acc)

        // Reject binding a name that is not a value hole.
        let badName =
            boundValues
            |> Map.toList
            |> List.tryFind (fun (n, _) ->
                match holesOf pf |> List.tryFind (fun h -> HoleDecl.name h = n) with
                | Some(HoleDecl.Value _) -> false
                | _ -> true)

        match badName with
        | Some(n, _) -> Error(sprintf "'%s' is not a value hole" n)
        | None ->
            go (holesOf pf) []
            |> Result.map (fun holes ->
                { pf with
                    // Preserve the degenerate wire shape: an empty hole list
                    // stays `None` (omitted), never `Some []`.
                    Holes = (if List.isEmpty holes then Option.None else Some holes) })
