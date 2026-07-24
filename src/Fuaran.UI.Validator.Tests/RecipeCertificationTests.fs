module Fuaran.UI.Validator.Tests.RecipeCertificationTests

// ============================================================================
//  Phase 359 — recipe / fragment certification tests.
//
//  Exercises `Fuaran.UI.Validator.RecipeCertification.certifyFragment`, the
//  adoption of `Fuaran.Core.Conformance.verifyFunction` as a UI recipe-
//  verification floor:
//
//   (a) a parameterised fragment valid for ALL bindings certifies
//       `Certified = true` (the tabs recipe whose header count tracks the
//       child count — the correct-by-construction template);
//   (b) a fragment that violates a rule at SOME binding (the tabs recipe whose
//       header count is fixed while the child count varies — breaking the
//       FUARAN047 1:1 header/child rule beyond count 1) returns
//       `Certified = false` with a readable `(binding, defect)` counterexample;
//   (c) the honesty boundary (Phase 52) — a non-deterministic (effecting)
//       fragment is certified for STRUCTURE only (`StructureOnly = true`), never
//       output determinism / quality.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Validator.RecipeCertification

// ─── Fragment + recipe builders ──────────────────────────────────────────────

/// A tabs body child — a minimal heading leaf, uniquely id'd by index.
let private tabChild (i: int) : Node<unit> =
    Fuaran.heading
        (sprintf "tab-body-%d" i)
        { Level = 2
          Text = TextSource.Literal(sprintf "Panel %d" i)
          Variant = HeadingVariant.Standard }

/// A tab header, one per child.
let private header (i: int) : TabHeader =
    { Label = TextSource.Literal(sprintf "Tab %d" i)
      Icon = Option.None
      Disabled = Option.None }

/// The declared fragment for a tabs recipe with a bounded `count` repeat hole
/// over `IntRange(1, 7)` and the given declared effect. The decl body is a
/// placeholder tabs node (unused — certification materialises the emitted tree
/// via the supplied `materialize`); what matters is the declared hole space.
let private tabsFragment (declId: string) (effect: EffectClass) : Node<unit> =
    let placeholderBody = Fuaran.tabs "tabs-body" { Defaults.tabs with Children = [] }

    Fuaran.fragmentDecl
        declId
        { Name = FragmentId declId
          Body = placeholderBody
          Holes = [ HoleDecl.Repeat("count", HoleValueSpace.IntRange(1, 7)) ]
          Effect = effect }

/// Read the bound `count` off the binding map (defaulting to 1 when the harness
/// has not bound it — never happens for a required repeat hole, but total).
let private boundCount (binding: Map<string, obj>) : int =
    match Map.tryFind "count" binding with
    | Some(:? int as n) -> n
    | _ -> 1

/// The CORRECT tabs recipe — headers track the child count 1:1, so FUARAN047
/// holds at every count. A correct-by-construction template.
let private correctTabs: Materialize<unit> =
    fun binding ->
        let n = boundCount binding
        let children = [ for i in 1..n -> tabChild i ]
        let headers = [ for i in 1..n -> header i ]

        Fuaran.tabs
            "tabs-recipe"
            { Defaults.tabs with
                Children = children
                TabHeaders = Some headers }

/// The BROKEN tabs recipe — always emits exactly ONE header regardless of the
/// child count, so FUARAN047 (headers must align 1:1 with children) holds at
/// count 1 but is violated at counts 2..7.
let private brokenTabs: Materialize<unit> =
    fun binding ->
        let n = boundCount binding
        let children = [ for i in 1..n -> tabChild i ]
        let headers = [ header 1 ] // always one header — the bug

        Fuaran.tabs
            "tabs-recipe"
            { Defaults.tabs with
                Children = children
                TabHeaders = Some headers }

let private pureDeterministic: EffectClass =
    { HostEffect = HostEffect.Pure
      Determinism = DeterminismSource.Deterministic }

let private nonDeterministic: EffectClass =
    { HostEffect = HostEffect.ReadsHost
      Determinism = DeterminismSource.Network }

// ─── Tests ───────────────────────────────────────────────────────────────────

[<Tests>]
let tests =
    testList
        "Phase 359 — recipe certification floor (verifyFunction)"
        [ test "(a) a correct-by-construction recipe certifies valid for ALL bindings" {
              let fragment = tabsFragment "tabs-count" pureDeterministic
              let verdict = certifyFragment "tabs-count" fragment correctTabs 64 1

              Expect.isTrue verdict.Certified (sprintf "expected certified; got %s" (renderVerdict verdict))
              Expect.isNone verdict.Counterexample "a certified recipe carries no counterexample"

              // The `count` space is IntRange(1,7) — 7 finite cases, exhaustively
              // enumerated (well under maxCases), so coverage is Exhaustive.
              match verdict.Coverage with
              | Exhaustive n -> Expect.equal n 7 "expected all 7 count-bindings enumerated exhaustively"
              | Sampled _ -> failtest "expected exhaustive coverage over the finite count space"
          }

          test "(b) a recipe that violates FUARAN047 at some binding is REJECTED with a readable counterexample" {
              let fragment = tabsFragment "tabs-count-broken" pureDeterministic
              let verdict = certifyFragment "tabs-count-broken" fragment brokenTabs 64 1

              Expect.isFalse verdict.Certified "expected rejection — the header count does not track the child count"

              match verdict.Counterexample with
              | Some cx ->
                  // The binding is readable (`count=<n>`) and the defect names FUARAN047.
                  Expect.stringContains cx.Binding "count=" "counterexample binding names the offending hole value"
                  Expect.stringContains cx.Defect "FUARAN047" "counterexample defect is the 1:1 header/child rule"
              | None -> failtest "a rejected recipe must surface a (binding, defect) counterexample"
          }

          test "(b') the counterexample reproduces — the same seed yields the same verdict" {
              let fragment = tabsFragment "tabs-count-broken" pureDeterministic
              let v1 = certifyFragment "tabs-count-broken" fragment brokenTabs 64 1
              let v2 = certifyFragment "tabs-count-broken" fragment brokenTabs 64 1
              Expect.equal v1.Certified v2.Certified "verdict is deterministic"

              match v1.Counterexample, v2.Counterexample with
              | Some a, Some b ->
                  Expect.equal a.Binding b.Binding "same seed reproduces the same counterexample binding"
              | _ -> failtest "both runs must produce a counterexample"
          }

          test "(c) honesty boundary — a non-deterministic recipe is certified for STRUCTURE only" {
              // A recipe whose declared effect is Network/ReadsHost. Its emitted
              // structure is still certifiable (the tree shape is valid for all
              // bindings); the verdict flags StructureOnly so a consumer never
              // reads it as an output-determinism / quality guarantee.
              let fragment = tabsFragment "tabs-live" nonDeterministic
              let verdict = certifyFragment "tabs-live" fragment correctTabs 64 1

              Expect.isTrue verdict.Certified "structural validity still certifies for a non-deterministic recipe"

              Expect.isTrue
                  verdict.StructureOnly
                  "a non-deterministic recipe is certified for structure ONLY (Phase 52)"

              Expect.stringContains
                  (renderVerdict verdict)
                  "structure-only"
                  "the rendered verdict surfaces the honesty boundary"
          }

          test "a pure-deterministic recipe is NOT flagged structure-only" {
              let fragment = tabsFragment "tabs-count" pureDeterministic
              let verdict = certifyFragment "tabs-count" fragment correctTabs 64 1
              Expect.isFalse verdict.StructureOnly "a pure-deterministic recipe certifies structure AND determinism"
          } ]
