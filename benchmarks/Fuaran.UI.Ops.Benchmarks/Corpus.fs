module Fuaran.UI.Ops.Benchmarks.Corpus

open Fuaran.UI
open Fuaran.UI.Types

// ============================================================================
//  Benchmark corpus (Phase 200) — representative parameterised fragments at
//  three sizes, driving the apply / re-derivation engine under measurement.
//
//  Each scenario is a `pure-deterministic` `ParamFragment` with N value holes
//  and a body of N markdown children, plus the arg-sets that exercise the two
//  distinct paths the Phase 183 engine optimises:
//
//   - a FULL application (`baseArgs`)        — the cold derivation a bare apply
//                                              always pays;
//   - a single-VALUE edit (`oneValueEdit`)   — the incremental path: structural
//                                              tree reused, exactly one hole-
//                                              address recomputed;
//   - a SLOT edit (`slotEdit`)               — the structural-change path: the
//                                              tree genuinely differs, forcing a
//                                              full re-derivation even under the
//                                              cache.
//
//  Sizes are chosen to bracket realistic authoring surfaces: `Small` ≈ a single
//  card, `Medium` ≈ a panel of metrics, `Large` ≈ a dense dashboard region —
//  so the apply-throughput and memo-hit-rate curves can be read as a function
//  of tree size, not a single point estimate.
// ============================================================================

/// Boxed-arg helper — mirrors the FragmentMemoTests / FragmentApplyTests
/// `Unchecked.nonNull` discipline so the `Map<string, obj>` arg sets type-check
/// under nullable reference types.
let private v (x: obj | null) : obj = Unchecked.nonNull x

/// One benchmark scenario: a fragment + the arg-sets exercising each apply path.
type Scenario =
    {
        Name: string
        Fragment: ParamFragment<unit>
        RefId: string
        /// The full initial value arg-set (cold derivation).
        BaseArgs: Map<string, obj>
        /// `BaseArgs` with exactly one value hole changed — drives `Reapply`'s
        /// incremental path (tree reused, one hole-address recomputed).
        OneValueEdit: Map<string, obj>
        /// The slot arg-set for the base derivation.
        SlotArgs: Map<string, Node<unit>>
        /// A changed slot subtree — drives `Reapply`'s structural-change fallback
        /// (full re-derivation).
        SlotEdit: Map<string, Node<unit>>
    }

/// Build a fragment with `holeCount` value holes (`field0`..`fieldN-1`) and a
/// `content` slot, whose body is a dashboard of `holeCount` markdown children.
/// Pure-deterministic, so it is cache-admissible.
let private mkFragment (holeCount: int) : ParamFragment<unit> =
    let children =
        [ for i in 0 .. holeCount - 1 -> Fuaran.markdown $"line-{i}" $"Line {i}" ]

    let body =
        Fuaran.dashboard
            "frag-root"
            { Defaults.dashboard<unit> with
                Children =
                    children
                    @ [ { Id = NodeId "content"
                          Kind =
                            NodeKind.FragmentRef
                                { Name = FragmentId "content"
                                  Args = Map.empty }
                          State = Defaults.stateBehaviour
                          Style = Defaults.style
                          Accessibility = None
                          Motion = None
                          ExtraAttributes = None } ] }

    let valueHoles =
        [ for i in 0 .. holeCount - 1 -> HoleDecl.Value($"field{i}", HoleValueSpace.StringLen(0, 80), None) ]

    { Name = FragmentId $"frag{holeCount}"
      Holes = valueHoles @ [ HoleDecl.Slot("content", None) ]
      Body = body
      Effect = EffectClass.pureDeterministic }

let private baseArgs (holeCount: int) : Map<string, obj> =
    [ for i in 0 .. holeCount - 1 -> $"field{i}", v (box $"value-{i}") ]
    |> Map.ofList

/// `baseArgs` with the LAST hole's value changed — a single disjoint edit, the
/// shape the incremental engine is designed to make cheap.
let private oneValueEdit (holeCount: int) : Map<string, obj> =
    baseArgs holeCount |> Map.add $"field{holeCount - 1}" (v (box "edited-value"))

let private slotArgs (text: string) : Map<string, Node<unit>> =
    Map.ofList [ "content", Fuaran.markdown "body" text ]

let private scenario (name: string) (holeCount: int) : Scenario =
    { Name = name
      Fragment = mkFragment holeCount
      RefId = "ref"
      BaseArgs = baseArgs holeCount
      OneValueEdit = oneValueEdit holeCount
      SlotArgs = slotArgs "initial"
      SlotEdit = slotArgs "restructured" }

/// ≈ a single card: 4 value holes.
let small = scenario "Small" 4

/// ≈ a metric panel: 16 value holes.
let medium = scenario "Medium" 16

/// ≈ a dense dashboard region: 64 value holes.
let large = scenario "Large" 64

/// All three sizes, smallest-first.
let all = [ small; medium; large ]
