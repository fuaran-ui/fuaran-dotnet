module Fuaran.UI.Fragments.Tests.StdlibCertificationTests

// ============================================================================
//  The certification gate for the curated fragment library.
//
//  Every entry in `Stdlib.all` is driven through
//  `Fuaran.UI.Validator.RecipeCertification.certifyFragment` — the Phase 359
//  verification floor — which enumerates or deterministically samples the
//  fragment's value / repeat hole-space and proves the emitted tree is
//  validator-conformant for EVERY covered binding. A fragment that fails
//  reports its `(binding, defect)` counterexample.
//
//  The suite is quantified over `Stdlib.all` rather than written per fragment
//  on purpose: a new entry added to the library is certified by construction,
//  and cannot ship uncertified by being forgotten here. The per-fragment tests
//  below cover the properties the harness does not — that a slot hole has a
//  marker in the template, that the representative reference binds every
//  required hole, and that the declared effect class matches what the
//  materialised tree actually does.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Fragments
open Fuaran.UI.Validator.RecipeCertification

// ─── Local tree walk ─────────────────────────────────────────────────────────
//
// Deliberately local rather than borrowed from the renderer: this project
// depends on `Fuaran.UI` and the validator only, and pulling the renderer in to
// reuse eight lines of container arms would put a React/Feliz dependency under
// a certification suite.

let private childrenOf (node: Node<'Msg>) : Node<'Msg> list =
    match node.Kind with
    | NodeKind.Box s -> s.Children
    | NodeKind.SplitPanel s -> s.Children
    | NodeKind.Tabs s -> s.Children
    | NodeKind.Stepper s -> s.Children
    | NodeKind.SummaryList s -> s.Children
    | NodeKind.Disclosure s -> s.Children
    | NodeKind.Modal s -> s.Children
    | NodeKind.ScrollArea s -> s.Children
    | NodeKind.ErrorBoundary s -> [ s.Child; s.Fallback ]
    | _ -> []

let rec private descendants (node: Node<'Msg>) : Node<'Msg> list =
    node :: (childrenOf node |> List.collect descendants)

/// The slot names a template body marks with an unbound `FragmentRef`.
let private slotMarkersIn (body: Node<'Msg>) : string list =
    descendants body
    |> List.choose (fun n ->
        match n.Kind with
        | NodeKind.FragmentRef spec -> Some spec.Name
        | _ -> None)

let private declSpecOf (f: StdlibFragment<'Msg>) : FragmentDeclSpec<'Msg> =
    match f.Decl.Kind with
    | NodeKind.FragmentDecl spec -> spec
    | other -> failtestf "fragment '%s' declaration is a %A, not a FragmentDecl" f.Name other

let private refSpecOf (f: StdlibFragment<'Msg>) : FragmentRefSpec<'Msg> =
    match f.Example.Kind with
    | NodeKind.FragmentRef spec -> spec
    | other -> failtestf "fragment '%s' example is a %A, not a FragmentRef" f.Name other

/// `validateValueArg` takes a non-null `obj`; `box` yields `objnull` under F#
/// 10 nullable reference types, so the boxing is narrowed once here rather than
/// at four call sites.
let private boxed (v: 'T) : obj = box v |> Unchecked.nonNull

let private isPureDeterministic (e: EffectClass) : bool =
    e.HostEffect = HostEffect.Pure
    && e.Determinism = DeterminismSource.Deterministic

// The certification budget. 256 bindings is comfortably above the largest
// finite hole-space in the set and low enough that the whole suite stays inside
// a normal test run; the seed is fixed so a counterexample reproduces.
[<Literal>]
let private MaxCases = 256

[<Literal>]
let private Seed = 20260826

let private library: StdlibFragment<unit> list = Stdlib.all

// ─── Tests ───────────────────────────────────────────────────────────────────

[<Tests>]
let tests =
    testList
        "Phase 380 — the certified fragment library"
        [ test "the library is non-empty and the curated set is the size the charter claims" {
              Expect.isGreaterThanOrEqual
                  (List.length library)
                  6
                  "the charter promises a curated set of 6-10 fragments; fewer is not a library"

              Expect.isLessThanOrEqual
                  (List.length library)
                  10
                  "the charter promises a curated set of 6-10 fragments; more wants a second look at whether every entry earns its place"
          }

          test "fragment names and declaration ids are unique" {
              let names = library |> List.map (fun f -> f.Name)
              Expect.equal (List.distinct names |> List.length) (List.length names) "two fragments share a name"

              let ids = library |> List.map (fun f -> f.Decl.Id)

              Expect.equal
                  (List.distinct ids |> List.length)
                  (List.length ids)
                  "two declarations share a node id — the id is also the hole-address prefix, so a collision is not cosmetic"

              for f in library do
                  Expect.isNotEmpty f.Name "a fragment name is empty"
                  Expect.isNotEmpty f.Summary (sprintf "fragment '%s' carries no summary" f.Name)
          }

          testList
              "every fragment certifies valid-for-all-bindings"
              [ for f in library ->
                    test f.Name {
                        let verdict = certifyFragment f.Name f.Decl f.Materialize MaxCases Seed

                        Expect.isTrue
                            verdict.Certified
                            (sprintf "fragment '%s' failed certification: %s" f.Name (renderVerdict verdict))

                        Expect.isNone
                            verdict.Counterexample
                            (sprintf "a certified fragment carries no counterexample (%s)" f.Name)

                        // A verdict over ZERO bindings is vacuously `Certified`
                        // and says nothing at all. Pin the coverage so an
                        // enumeration that silently stops producing cases
                        // surfaces as a failure rather than as a green run.
                        let covered =
                            match verdict.Coverage with
                            | Exhaustive n -> n
                            | Sampled(drawn, _) -> drawn

                        Expect.isGreaterThan
                            covered
                            0
                            (sprintf
                                "fragment '%s' certified over ZERO bindings — a vacuous verdict, not a proof (%s)"
                                f.Name
                                (renderVerdict verdict))
                    } ]

          testList
              "the honesty boundary — an effecting fragment is certified for STRUCTURE only"
              [ for f in library ->
                    test f.Name {
                        let spec = declSpecOf f
                        let effect = spec.Effect |> Option.defaultValue EffectClass.pureDeterministic
                        let verdict = certifyFragment f.Name f.Decl f.Materialize MaxCases Seed

                        Expect.equal
                            verdict.StructureOnly
                            (not (isPureDeterministic effect))
                            (sprintf
                                "fragment '%s' declares %A but its verdict's StructureOnly flag disagrees — the flag IS the claim a consumer reads"
                                f.Name
                                effect)
                    } ]

          test "the set carries at least one effecting fragment, so the boundary is exercised and not merely stated" {
              let effecting =
                  library
                  |> List.filter (fun f ->
                      declSpecOf f
                      |> fun s -> s.Effect |> Option.defaultValue EffectClass.pureDeterministic
                      |> isPureDeterministic
                      |> not)

              Expect.isNonEmpty
                  effecting
                  "an all-pure library never exercises the structure-only path, so a regression there would ship green"
          }

          testList
              "the declaration is in the CANONICAL wire shape — a pure-deterministic effect is OMITTED"
              [ for f in library ->
                    test f.Name {
                        // `WIRE_FORMAT.md`, "Parameterised fragments": `effect`
                        // is "Omitted when pure-deterministic". The F# type
                        // carries it as an option and encodes `Some x` verbatim,
                        // so the redundant explicit default is expressible here
                        // and round-trips through THIS host without complaint —
                        // which is exactly why it needs a test rather than care.
                        // A host that normalises to the specified form
                        // re-encodes the default away and its corpus
                        // byte-comparison fails; the first cut of these fixtures
                        // did precisely that to the Rust host while every F#
                        // suite stayed green, because F# is the encoder that
                        // produced the bytes it was checking against.
                        let spec = declSpecOf f

                        match spec.Effect with
                        | Some e ->
                            Expect.isFalse
                                (isPureDeterministic e)
                                (sprintf
                                    "fragment '%s' declares an EXPLICIT pure-deterministic effect; the canonical wire form omits it (WIRE_FORMAT, Parameterised fragments)"
                                    f.Name)
                        | Option.None -> ()

                        // The same rule from the other side: a zero-hole decl
                        // omits `holes` rather than carrying an empty list.
                        match spec.Holes with
                        | Some hs ->
                            Expect.isNonEmpty
                                hs
                                (sprintf "fragment '%s' carries an empty hole list; the canonical form omits it" f.Name)
                        | Option.None -> ()
                    } ]

          testList
              "TOTALITY (invariant 1) — every repeat count is bounded"
              [ for f in library ->
                    test f.Name {
                        Expect.isTrue
                            (Fragment.isTotal (declSpecOf f))
                            (sprintf
                                "fragment '%s' declares an unbounded Repeat count — binding it could expand without limit"
                                f.Name)
                    } ]

          testList
              "every declared slot hole has a marker in the template body"
              [ for f in library ->
                    test f.Name {
                        let spec = declSpecOf f

                        let declaredSlots =
                            spec.Holes
                            |> Option.defaultValue []
                            |> List.choose (fun h ->
                                match h with
                                | HoleDecl.Slot(n, _) -> Some n
                                | _ -> None)

                        let markers = slotMarkersIn spec.Body

                        for slot in declaredSlots do
                            Expect.contains
                                markers
                                slot
                                (sprintf
                                    "fragment '%s' declares slot '%s' but its body carries no unbound FragmentRef marker for it — the apply would silently bind nothing"
                                    f.Name
                                    slot)

                        for marker in markers do
                            Expect.contains
                                declaredSlots
                                marker
                                (sprintf
                                    "fragment '%s' body marks slot '%s' which it does not declare — the marker would survive into the rendered tree as a dangling ref"
                                    f.Name
                                    marker)
                    } ]

          testList
              "the representative reference targets its own fragment and binds every required hole"
              [ for f in library ->
                    test f.Name {
                        let spec = declSpecOf f
                        let refSpec = refSpecOf f

                        Expect.equal
                            refSpec.Name
                            f.Name
                            (sprintf "fragment '%s' example targets a different fragment" f.Name)

                        let bound =
                            refSpec.Args |> Option.defaultValue Map.empty |> Map.toList |> List.map fst

                        for hole in Fragment.requiredHoles spec do
                            Expect.contains
                                bound
                                hole
                                (sprintf
                                    "fragment '%s' example leaves required hole '%s' unbound — the apply would refuse it"
                                    f.Name
                                    hole)

                        for name in bound do
                            let known =
                                spec.Holes
                                |> Option.defaultValue []
                                |> List.exists (fun h -> HoleDecl.name h = name)

                            Expect.isTrue
                                known
                                (sprintf
                                    "fragment '%s' example binds '%s', which the declaration has no hole for"
                                    f.Name
                                    name)
                    } ]

          testList
              "every value argument in the representative reference satisfies its hole's value-space"
              [ for f in library ->
                    test f.Name {
                        let spec = declSpecOf f
                        let refSpec = refSpecOf f

                        for (name, arg) in refSpec.Args |> Option.defaultValue Map.empty |> Map.toList do
                            match arg with
                            | FragmentArg.SlotArg _ -> () // a slot's shape is checked by the apply, not a value-space
                            | FragmentArg.Int v ->
                                Expect.isOk
                                    (Fragment.validateValueArg spec name (boxed v))
                                    (sprintf
                                        "fragment '%s': argument '%s' = %d is outside its hole's space"
                                        f.Name
                                        name
                                        v)
                            | FragmentArg.Float v ->
                                Expect.isOk
                                    (Fragment.validateValueArg spec name (boxed v))
                                    (sprintf
                                        "fragment '%s': argument '%s' = %g is outside its hole's space"
                                        f.Name
                                        name
                                        v)
                            | FragmentArg.Bool v ->
                                Expect.isOk
                                    (Fragment.validateValueArg spec name (boxed v))
                                    (sprintf
                                        "fragment '%s': argument '%s' = %b is outside its hole's space"
                                        f.Name
                                        name
                                        v)
                            | FragmentArg.Str v ->
                                Expect.isOk
                                    (Fragment.validateValueArg spec name (boxed v))
                                    (sprintf
                                        "fragment '%s': argument '%s' = '%s' is outside its hole's space"
                                        f.Name
                                        name
                                        v)
                    } ]

          test "lookup by name finds every fragment and refuses one that is not there" {
              for f in library do
                  match Stdlib.tryFind<unit> f.Name with
                  | Some found -> Expect.equal found.Name f.Name "tryFind returned a different fragment"
                  | None -> failtestf "tryFind could not find '%s', which is in `all`" f.Name

              Expect.isNone (Stdlib.tryFind<unit> "no-such-fragment") "tryFind invented a fragment"
          }

          test "the derived signatures cover the library and carry each fragment's declared effect" {
              let signatures = Stdlib.signatures<unit> ()

              Expect.equal (List.length signatures) (List.length library) "one signature per fragment"

              for (f, s) in List.zip library signatures do
                  Expect.equal s.Name f.Name "signature order tracks the library"

                  let declared =
                      (declSpecOf f).Effect |> Option.defaultValue EffectClass.pureDeterministic

                  Expect.equal s.Effect declared (sprintf "signature for '%s' reports a different effect class" f.Name)

                  let holeCount = (declSpecOf f).Holes |> Option.defaultValue [] |> List.length
                  Expect.equal (List.length s.Holes) holeCount (sprintf "signature for '%s' lost a hole" f.Name)
          }

          test "the certification harness can still go red — a deliberately broken materialiser is REJECTED" {
              // The go-red self-test. Every assertion above is a green one, and a
              // suite of green assertions cannot distinguish "the fragments are
              // correct" from "the harness certifies anything". Feed it a
              // materialiser that emits two nodes with the same id — the
              // duplicate-id rule is an Error-severity defect at every binding —
              // and require the rejection.
              let f = List.head library

              let broken: Map<string, obj> -> Node<unit> =
                  fun _ ->
                      Fuaran.stack
                          "collide"
                          { Defaults.stack with
                              Children = [ Fuaran.markdown "same-id" "first"; Fuaran.markdown "same-id" "second" ] }

              let verdict = certifyFragment f.Name f.Decl broken MaxCases Seed

              Expect.isFalse
                  verdict.Certified
                  "a materialiser emitting duplicate node ids must be REJECTED; if it certifies, the harness is not measuring what this suite claims"

              Expect.isSome verdict.Counterexample "a rejected fragment must surface a (binding, defect) counterexample"
          } ]
