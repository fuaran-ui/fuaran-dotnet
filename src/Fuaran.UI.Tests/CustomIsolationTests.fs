module Fuaran.UI.Tests.CustomIsolation

// ============================================================================
//  Phase 783 — the two surfaces that sit OUTSIDE the dispatch gate
//  structurally, so Phase 782's inversion does not reach them.
//
//   A. The custom-renderer registry was one process-wide dictionary keyed on
//      `(moduleId, componentId)` taken straight off the wire, so any decoded
//      tree could invoke any renderer registered anywhere in the process, with
//      attacker-chosen props. A confused deputy: a renderer registered for a
//      privileged admin surface was reachable from a tree rendered on a public
//      one. The key now carries the render SCOPE, and lookup does not fall back
//      across scopes — a fallback would make the scoping advisory.
//
//   B. `ContentHash` did not close it and could not: the tree supplies its own
//      hash record. Two bypasses followed — omit the hash (`NoTreeHash` shared a
//      render branch with `Match` and rendered silently), or declare
//      `AdvisoryWarning` (strictness was read from the tree's own record, so an
//      attacker picked warn-then-render). Strictness is now a HOST floor a tree
//      may only tighten, and under an enforcing floor an unverifiable hash is a
//      refusal.
//
//  All pure .NET — the registry and the classifier are the decision points both
//  renderers drive, so this exercises the shipping logic without a render.
// ============================================================================

open Expecto
open Feliz
open Fuaran.Core
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Runtime

/// A registered renderer that RECORDS its invocation and returns a null element.
///
/// It deliberately does not build a real `ReactElement`: Feliz's .NET shim
/// cannot construct one (`prop.text` is a tuple it cannot cast to
/// `IReactProperty`), and a lookup that throws while building its result is
/// indistinguishable from one that found nothing — which would make every
/// "not reachable" assertion below pass for the wrong reason.
let private recordingRenderer (log: ResizeArray<string>) (label: string) : Map<string, JVal> -> ReactElement =
    fun _ ->
        log.Add label
        Unchecked.defaultof<ReactElement>

let private hash (h: string) (strictness: HashStrictness) : ContentHash =
    { Algorithm = "sha256"
      Hash = h
      Strictness = strictness }

[<Tests>]
let registryScopeTests =
    testList
        "Phase 783 — custom-renderer registry scoping"
        [ test "a root-scope renderer is NOT reachable from another scope" {
              let invoked = ResizeArray<string>()
              let reg = CustomRendererRegistry()
              reg.Register("admin", "danger", recordingRenderer invoked "admin-only")

              Expect.isSome
                  (reg.TryRenderInScope(None, "admin", "danger", Map.empty))
                  "reachable from its own (root) scope"

              Expect.equal (List.ofSeq invoked) [ "admin-only" ] "…and it really was invoked"

              Expect.isNone
                  (reg.TryRenderInScope(Some "public-surface", "admin", "danger", Map.empty))
                  "NOT reachable from a different scope"

              Expect.equal (List.ofSeq invoked) [ "admin-only" ] "the out-of-scope lookup invoked NOTHING"

              Expect.isNone
                  (reg.TryGetInScope(Some "public-surface", "admin", "danger"))
                  "…and the hash-bearing probe agrees"
          }

          test "a scoped renderer is NOT reachable from the root scope" {
              // The other direction matters just as much: a host that scopes its
              // privileged surface must not find that a plain `render` reaches it.
              let invoked = ResizeArray<string>()
              let reg = CustomRendererRegistry()
              reg.RegisterInScope("admin-surface", "admin", "danger", recordingRenderer invoked "admin-only")

              Expect.isSome
                  (reg.TryRenderInScope(Some "admin-surface", "admin", "danger", Map.empty))
                  "reachable from its own scope"

              Expect.isNone
                  (reg.TryRenderInScope(None, "admin", "danger", Map.empty))
                  "NOT reachable from the root scope"

              Expect.isNone (reg.TryRender("admin", "danger", Map.empty)) "…including through the unscoped convenience"
          }

          test "same ids in two scopes are two different renderers" {
              // The registry is keyed by the triple, not shadowed by it — two
              // surfaces may legitimately register the same component id with
              // different implementations.
              let reg = CustomRendererRegistry()
              let invoked = ResizeArray<string>()
              reg.RegisterInScope("a", "m", "c", recordingRenderer invoked "from-a")
              reg.RegisterInScope("b", "m", "c", recordingRenderer invoked "from-b")
              reg.Register("m", "c", recordingRenderer invoked "from-root")

              Expect.equal reg.Count 3 "three distinct registrations, not one overwritten twice"
          }

          test "the runtime surface enforces the same scoping" {
              // Same assertion one layer up: the members the renderer's Custom
              // arm actually calls.
              let runtime = MutableRuntime()
              let invoked = ResizeArray<string>()

              runtime.Registry.RegisterInScope(
                  "admin-surface",
                  "admin",
                  "danger",
                  recordingRenderer invoked "admin-only"
              )

              let rt = runtime :> IFuaranRuntime

              Expect.isSome
                  (rt.TryRenderCustomInScope(Some "admin-surface", "admin", "danger", Map.empty))
                  "its own scope reaches it"

              Expect.isNone
                  (rt.TryRenderCustomInScope(Some "public", "admin", "danger", Map.empty))
                  "another scope does not"

              Expect.isNone (rt.TryRenderCustomInScope(None, "admin", "danger", Map.empty)) "the root scope does not"
          }

          test "an unprivileged guest runtime reaches no renderer in any scope" {
              let host = MutableRuntime.Permissive()
              let invoked = ResizeArray<string>()
              host.Registry.Register("m", "c", recordingRenderer invoked "host-owned")
              host.Registry.RegisterInScope("g", "m", "c", recordingRenderer invoked "guest-scoped")

              let guest = UnprivilegedGuestRuntime(host :> IFuaranRuntime, "g") :> IFuaranRuntime

              Expect.isNone (guest.TryRenderCustomInScope(None, "m", "c", Map.empty)) "root scope unreachable"
              Expect.isNone (guest.TryRenderCustomInScope(Some "g", "m", "c", Map.empty)) "its own scope unreachable"
              Expect.isNone (guest.TryLoadGuest "g") "no nested guest loading"
              Expect.isFalse (guest.CanDispatch(ActionDescriptor.Call "/api/x")) "no dispatch"
              Expect.isEmpty invoked "no registered renderer ran"
          } ]

[<Tests>]
let hashFloorTests =
    testList
        "Phase 783 — Custom content-hash floor"
        [ test "omitting the hash is a REFUSAL under an enforcing floor" {
              // The cheapest bypass: `NoTreeHash` shared a render branch with
              // `Match`, so skipping verification skipped it silently.
              Expect.equal
                  (CustomHash.classifyUnder
                      HashStrictness.StrictReplay
                      None
                      (Some(hash "abc" HashStrictness.StrictReplay)))
                  CustomHash.CustomHashOutcome.Unverifiable
                  "no tree hash + enforcing floor → refuse"

              Expect.equal
                  (CustomHash.classifyUnder
                      HashStrictness.AdvisoryWarning
                      None
                      (Some(hash "abc" HashStrictness.StrictReplay)))
                  CustomHash.CustomHashOutcome.NoTreeHash
                  "no tree hash + advisory floor → render (the default, unchanged)"
          }

          test "a registry with no recorded hash is equally unverifiable" {
              Expect.equal
                  (CustomHash.classifyUnder HashStrictness.Enforced (Some(hash "abc" HashStrictness.StrictReplay)) None)
                  CustomHash.CustomHashOutcome.Unverifiable
                  "declared but unverifiable + enforcing → refuse"

              Expect.equal
                  (CustomHash.classifyUnder
                      HashStrictness.AdvisoryWarning
                      (Some(hash "abc" HashStrictness.StrictReplay))
                      None)
                  CustomHash.CustomHashOutcome.RegistryNoHash
                  "…and warn-then-render under an advisory floor"
          }

          test "a tree-supplied strictness may only TIGHTEN, never loosen" {
              // The second bypass: strictness was read from the tree's own hash
              // record, so an attacker who declared a hash picked
              // `AdvisoryWarning` and got warn-then-render on a mismatch.
              let treeAdvisory = Some(hash "aaa" HashStrictness.AdvisoryWarning)
              let registered = Some(hash "bbb" HashStrictness.StrictReplay)

              Expect.equal
                  (CustomHash.classifyUnder HashStrictness.StrictReplay treeAdvisory registered)
                  CustomHash.CustomHashOutcome.MismatchStrict
                  "the HOST floor wins over the tree's lenient declaration"

              Expect.equal
                  (CustomHash.classifyUnder HashStrictness.AdvisoryWarning treeAdvisory registered)
                  CustomHash.CustomHashOutcome.MismatchAdvisory
                  "…and an advisory host keeps the advisory outcome"

              // Tightening still works from the tree side.
              Expect.equal
                  (CustomHash.classifyUnder
                      HashStrictness.AdvisoryWarning
                      (Some(hash "aaa" HashStrictness.StrictReplay))
                      registered)
                  CustomHash.CustomHashOutcome.MismatchStrict
                  "a tree may raise the floor"
          }

          test "a genuine match renders under every floor" {
              // The guard must not be so eager that legitimate verified content
              // is refused — otherwise nobody turns it on.
              for floor in
                  [ HashStrictness.AdvisoryWarning
                    HashStrictness.StrictReplay
                    HashStrictness.Enforced ] do
                  Expect.equal
                      (CustomHash.classifyUnder
                          floor
                          (Some(hash "same" HashStrictness.AdvisoryWarning))
                          (Some(hash "same" HashStrictness.StrictReplay)))
                      CustomHash.CustomHashOutcome.Match
                      "a verified hash always renders"
          }

          test "the installed floor defaults to AdvisoryWarning and is restorable" {
              CustomHash.clearCustomHashFloor ()

              Expect.equal
                  (CustomHash.currentCustomHashFloor ())
                  HashStrictness.AdvisoryWarning
                  "the default floor is the pre-0.15.0 behaviour"

              try
                  CustomHash.installCustomHashFloor HashStrictness.StrictReplay

                  Expect.equal
                      (CustomHash.classify None None)
                      CustomHash.CustomHashOutcome.Unverifiable
                      "the installed floor drives the un-parameterised classify"
              finally
                  CustomHash.clearCustomHashFloor ()

              Expect.equal
                  (CustomHash.classify None None)
                  CustomHash.CustomHashOutcome.NoTreeHash
                  "clearing restores the default"
          } ]
