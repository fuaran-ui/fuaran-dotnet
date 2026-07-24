module Fuaran.UI.ThemeManifest.Tests.ThemeBridgeTests

// ─── ThemeManifest → host-styling bridge emitter (Phase 165) ────
//
// Acceptance criteria covered:
//   - A manifest projected from a brand stylesheet emits a bridge that
//     renders Fuaran chrome in the host's brand with zero hand-authoring,
//     using `var()` references so host tokens stay canonical.
//   - The coverage report names every unmapped contract variable + its
//     reference-default fallback, and every unused host token.
//   - The verification hook fails (with the violating role + values) when
//     a mapped token breaks a declared contrast floor; passes when it
//     holds; reports usage budgets as observer-assisted.
//   - Ingest → emit → re-project round-trips to an equivalent manifest
//     (golden-locked, literal mode).

open Expecto
open Fuaran.UI.Types
open Fuaran.UI.ThemeManifest
open Fuaran.UI.ThemeManifest.ThemeBridge

/// A brownfield manifest in the Example-C shape: raw `--color-*` tokens
/// (no inferable role), with the operator's role bindings added —
/// `Tone.Brand` → the brand accent, `page-surface` / `body-text` named
/// roles → the surface + ink. The shape Phase 149 + an operator produce.
let private brownfield: ThemeManifest =
    let m =
        Project.projectFromCssCustomProperties
            """
            :root {
              --color-brand: #2563eb;
              --color-success: #16a34a;
              --color-text: #0f172a;
              --color-surface: #ffffff;
              --color-unused: #abcdef;
            }
            """

    { m with
        Roles =
            [ { Role = ManifestRole.Tone ToneVariant.Brand
                TokenName = "color-brand" }
              { Role = ManifestRole.Tone ToneVariant.Success
                TokenName = "color-success" }
              { Role = ManifestRole.Named "body-text"
                TokenName = "color-text" }
              { Role = ManifestRole.Named "page-surface"
                TokenName = "color-surface" } ] }

[<Tests>]
let tests =
    testList
        "ThemeBridge"
        [

          // ─── Emission (reference mode — the default) ──────────────

          test "emitCss reference mode binds contract variables to host tokens via var()" {
              let result = ThemeBridge.emitCss brownfield BridgeOptions.tonesOnly

              Expect.stringContains result.Css "--fuaran-tone-brand-fg: var(--color-brand);" "brand accent → fg"
              Expect.stringContains result.Css "--fuaran-tone-success-fg: var(--color-success);" "success accent → fg"
              Expect.stringContains result.Css "--fuaran-tone-default-fg: var(--color-text);" "body-text → default fg"

              Expect.stringContains
                  result.Css
                  "--fuaran-tone-default-bg: var(--color-surface);"
                  "page-surface → default bg"

              Expect.stringContains result.Css ":root {" "default scope"
          }

          test "emitCss literal mode copies the resolved value instead of a var() reference" {
              let result =
                  ThemeBridge.emitCss
                      brownfield
                      { BridgeOptions.tonesOnly with
                          Mode = EmitMode.Literal }

              Expect.stringContains result.Css "--fuaran-tone-brand-fg: #2563eb;" "brand accent copied literally"
              Expect.isFalse (result.Css.Contains "var(--color-brand)") "no var() reference in literal mode"
          }

          test "emitCss honours the scope selector" {
              let result =
                  ThemeBridge.emitCss
                      brownfield
                      { BridgeOptions.tonesOnly with
                          Scope = ".themed-region" }

              Expect.stringContains result.Css ".themed-region {" "container scope emitted"
              Expect.isFalse (result.Css.Contains ":root {") "no :root when scoped to a container"
          }

          // ─── Coverage report ──────────────────────────────────────

          test "coverage report names mapped roles, fallbacks, and unused host tokens" {
              let result = ThemeBridge.emitCss brownfield BridgeOptions.tonesOnly
              let cov = result.Coverage

              // brand-fg, success-fg, default-fg, default-bg mapped.
              Expect.isTrue
                  (cov.Mapped |> List.exists (fun m -> m.FuaranVar = "--fuaran-tone-brand-fg"))
                  "brand fg is a mapped entry"

              Expect.isTrue
                  (cov.Mapped
                   |> List.exists (fun m -> m.FuaranVar = "--fuaran-tone-default-bg" && m.HostToken = "color-surface"))
                  "default bg maps to the surface host token"

              // warning fg was never bound → fell back to its reference default.
              match
                  cov.Fallbacks
                  |> List.tryFind (fun f -> f.FuaranVar = "--fuaran-tone-warning-fg")
              with
              | Some f -> Expect.equal f.ReferenceDefault "#b45309" "warning fg falls back to its reference default"
              | None -> failtest "warning fg must be reported as a fallback"

              // brand bg/border were never bound either.
              Expect.isTrue
                  (cov.Fallbacks |> List.exists (fun f -> f.FuaranVar = "--fuaran-tone-brand-bg"))
                  "brand bg fell back (only the accent fg was bound)"

              // color-unused bound to no contract variable.
              Expect.contains cov.UnusedHostTokens "color-unused" "the unbound host token is reported unused"
              Expect.isFalse (List.contains "color-brand" cov.UnusedHostTokens) "color-brand was used, not unused"
          }

          test "coverage report renders to console + Markdown" {
              let result = ThemeBridge.emitCss brownfield BridgeOptions.tonesOnly
              let console = ThemeBridge.CoverageReport.toConsole result.Coverage
              let md = ThemeBridge.CoverageReport.toMarkdown result.Coverage

              Expect.stringContains console "Theme-bridge coverage:" "console summary present"
              Expect.stringContains console "color-unused" "console lists the unused token"
              Expect.stringContains md "## Theme-bridge coverage" "markdown heading present"
              Expect.stringContains md "| `--fuaran-tone-brand-fg` | `color-brand` |" "markdown maps brand fg"
          }

          test "family selection scopes emission (Tones excluded → no tone vars)" {
              let result =
                  ThemeBridge.emitCss
                      brownfield
                      { BridgeOptions.defaults with
                          Families = Set.singleton ContractFamily.Spacing }

              Expect.isFalse (result.Css.Contains "--fuaran-tone-") "no tone variables when only Spacing is selected"
          }

          // ─── Verification hook (Phase 146 composition) ────────────

          test "verify fails a contrast floor a mapped token breaks, naming the role + values" {
              let manifest =
                  { ThemeManifest.empty with
                      Tokens =
                          [ { Name = "ink"
                              Type = "color"
                              Value = "#cccccc" // far too light for 7:1 on white
                              Description = None
                              Role = None }
                            { Name = "paper"
                              Type = "color"
                              Value = "#ffffff"
                              Description = None
                              Role = None } ]
                      Roles =
                          [ { Role = ManifestRole.Named "body-text"
                              TokenName = "ink" }
                            { Role = ManifestRole.Named "page-surface"
                              TokenName = "paper" } ]
                      Invariants = [ Invariant.create (InvariantKind.ContrastFloor("body-text", 7.0)) ] }

              let result = ThemeBridge.verify manifest
              let violations = ThemeBridge.VerificationResult.violations result

              Expect.equal (List.length violations) 1 "one contrast violation"
              let v = List.head violations
              Expect.equal v.Role "body-text" "the violating role is named"
              Expect.equal v.Foreground (Some "#cccccc") "the resolved foreground is carried"
              Expect.equal v.Background (Some "#ffffff") "the resolved background is carried"

              match v.Ratio with
              | Some r -> Expect.isLessThan r 7.0 "the observed ratio is below the floor"
              | None -> failtest "a violation must carry the observed ratio"

              Expect.isFalse (ThemeBridge.VerificationResult.passed result) "the gate fails"
          }

          test "verify passes a contrast floor that holds (reference-default tone surfaces)" {
              // No tokens → the Default tone resolves to its reference
              // defaults (#1f2937 on #ffffff), which clears 4.5:1.
              let manifest =
                  { ThemeManifest.empty with
                      Invariants = [ Invariant.create (InvariantKind.ContrastFloor("Default", 4.5)) ] }

              let result = ThemeBridge.verify manifest

              Expect.isTrue (ThemeBridge.VerificationResult.passed result) "the floor holds against reference defaults"

              match result.ContrastChecks with
              | [ c ] -> Expect.equal c.Status CheckStatus.Passed "the single check passed"
              | _ -> failtest "exactly one contrast check expected"
          }

          test "verify reports usage-budget + motion invariants as observer-assisted (deferred)" {
              let manifest =
                  { ThemeManifest.empty with
                      Invariants =
                          [ Invariant.create (InvariantKind.UsageBudget("color.brand", 9.0, 3.0))
                            Invariant.create (
                                InvariantKind.MotionVoice
                                    { MaxDurationMs = 240
                                      Easing = Some "ease-out" }
                            ) ] }

              let result = ThemeBridge.verify manifest

              Expect.isEmpty result.ContrastChecks "no contrast checks for a budget/motion-only manifest"
              Expect.equal (List.length result.Deferred) 2 "both non-contrast invariants are deferred"

              Expect.isTrue
                  (result.Deferred |> List.exists (fun d -> d.Description.Contains "UsageBudget"))
                  "the usage budget is deferred"
          }

          // ─── Round-trip golden (ingest → emit → re-project) ───────

          test "round-trip: ingest --fuaran-tone-* → emit (literal) → re-project agrees" {
              let css =
                  """
                  :root {
                    --fuaran-tone-brand-bg: #3b5bdb;
                    --fuaran-tone-brand-fg: #ffffff;
                    --fuaran-tone-brand-border: #2f48af;
                    --fuaran-tone-success-bg: #2f9e44;
                    --fuaran-tone-critical-bg: #e03131;
                  }
                  """

              let m1 = Project.projectFromFuaranToneVars css

              let emitted =
                  ThemeBridge.emitCss
                      m1
                      { BridgeOptions.tonesOnly with
                          Mode = EmitMode.Literal }

              let m2 = Project.projectFromFuaranToneVars emitted.Css

              // Manifests agree: same token name→value map, same role set.
              let tokenMap (m: ThemeManifest) =
                  m.Tokens |> List.map (fun t -> t.Name, t.Value) |> Map.ofList

              Expect.equal (tokenMap m2) (tokenMap m1) "token name→value maps agree after the round-trip"
              Expect.equal (Set.ofList m2.Roles) (Set.ofList m1.Roles) "role bindings agree after the round-trip"
          }

          // ─── One-call composition ─────────────────────────────────

          test "emitAndVerify returns both the bridge and the verification" {
              let bridge, verification =
                  ThemeBridge.emitAndVerify brownfield BridgeOptions.tonesOnly

              Expect.stringContains bridge.Css "--fuaran-tone-brand-fg" "the bridge was emitted"
              Expect.isTrue (ThemeBridge.VerificationResult.passed verification) "no declared floors to break"
          } ]
