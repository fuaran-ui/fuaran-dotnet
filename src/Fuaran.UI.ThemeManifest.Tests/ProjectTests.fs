module Fuaran.UI.ThemeManifest.Tests.ProjectTests

// ─── ThemeManifest projectors (Phase 149) ───────────────────────
//
// Acceptance criteria covered:
//   - A `--fuaran-tone-*` block projects to tokens + ToneVariant role
//     bindings, no hand-authoring.
//   - A bespoke `:root` set (light + dark) projects to tokens with theme
//     pairs preserved and roles left unbound.
//   - A DTCG file projects values losslessly; grouping is not mined for
//     bindings (Roles empty unless explicit fuaran extensions).
//   - A shell+override merge resolves last-write-wins.
//   - A thin projected manifest still resolves a tone (drives Phase 144).

open Expecto
open Fuaran.UI.Types
open Fuaran.UI.ThemeManifest

[<Tests>]
let tests =
    testList
        "ThemeManifestProject"
        [

          // ─── Fuaran-tone projector ───────────────────────────────

          test "projectFromFuaranToneVars produces tokens + ToneVariant role bindings" {
              let css =
                  """
                  :root {
                    --fuaran-tone-brand-bg: #3b5bdb;
                    --fuaran-tone-brand-fg: #ffffff;
                    --fuaran-tone-brand-border: #2f48af;
                    --fuaran-tone-critical-bg: #e03131;
                  }
                  """

              let m = Project.projectFromFuaranToneVars css

              Expect.equal (List.length m.Tokens) 4 "four tone tokens"

              match ThemeManifest.tryGetToken "tone.brand.bg" m with
              | Some t ->
                  Expect.equal t.Value "#3b5bdb" "brand bg value"
                  Expect.equal t.Type "color" "inferred colour type"
              | None -> failtest "tone.brand.bg must project"

              // The bg slot binds the ToneVariant role.
              match ThemeManifest.resolveRole ToneVariant.Brand m with
              | Some t -> Expect.equal t.Name "tone.brand.bg" "Brand binds to its bg token"
              | None -> failtest "Tone.Brand must resolve from the projected bindings"

              match ThemeManifest.resolveRole ToneVariant.Critical m with
              | Some t -> Expect.equal t.Value "#e03131" "Critical resolves to its bg colour"
              | None -> failtest "Tone.Critical must resolve"
          }

          test "projectFromFuaranToneVars: a thin manifest (no invariants) still resolves tones (GP 13)" {
              let m =
                  Project.projectFromFuaranToneVars ":root { --fuaran-tone-brand-bg: #123456; }"

              Expect.isEmpty m.Invariants "no invariants authored"
              Expect.isSome (ThemeManifest.resolveRole ToneVariant.Brand m) "still drives role resolution"
          }

          // ─── Generic CSS custom-property projector ───────────────

          test "projectFromCssCustomProperties preserves light/dark pairs, leaves roles unbound" {
              let css =
                  """
                  :root {
                    --paper: #ffffff;
                    --ink: #1a1a1a;
                    --accent: #3b5bdb;
                  }
                  :root[data-theme=dark] {
                    --paper: #111111;
                    --ink: #f5f5f5;
                  }
                  """

              let m = Project.projectFromCssCustomProperties css

              Expect.equal (ThemeManifest.tryGetToken "paper" m |> Option.map _.Value) (Some "#ffffff") "light paper"

              Expect.equal
                  (ThemeManifest.tryGetToken "paper@dark" m |> Option.map _.Value)
                  (Some "#111111")
                  "dark paper preserved"

              Expect.equal (ThemeManifest.tryGetToken "accent" m |> Option.map _.Value) (Some "#3b5bdb") "accent"

              Expect.isEmpty m.Roles "bespoke vars carry no inferable role — left unbound"
          }

          // ─── DTCG projector ──────────────────────────────────────

          test "projectFromDtcg ingests values losslessly without mining grouping for bindings" {
              let dtcg =
                  """
                  {
                    "color": {
                      "accent": { "$type": "color", "$value": "#7048e8", "$description": "Accent." }
                    }
                  }
                  """

              match Project.projectFromDtcg dtcg with
              | Ok m ->
                  match ThemeManifest.tryGetToken "color.accent" m with
                  | Some t ->
                      Expect.equal t.Value "#7048e8" "value lossless"
                      Expect.equal t.Description (Some "Accent.") "description preserved"
                  | None -> failtest "color.accent must project"

                  Expect.isEmpty m.Roles "grouping is not mined for bindings"
                  Expect.isEmpty m.Invariants "no invariants inferred"
              | Error e -> failtestf "decode failed: %s" e
          }

          // ─── Merge ───────────────────────────────────────────────

          test "merge resolves shell + override with last-write-wins precedence" {
              // Shell defines a neutral surface + brand; the app override
              // rebinds brand (the typical app-override shape) and adds a token.
              let shell =
                  Project.projectFromFuaranToneVars
                      ":root { --fuaran-tone-brand-bg: #0000ff; --fuaran-tone-default-bg: #ffffff; }"

              let appOverride =
                  Project.projectFromFuaranToneVars ":root { --fuaran-tone-brand-bg: #ff6600; }"

              let merged = Project.merge shell appOverride

              // brand bg overridden to the app colour; default surface retained.
              Expect.equal
                  (ThemeManifest.tryGetToken "tone.brand.bg" merged |> Option.map _.Value)
                  (Some "#ff6600")
                  "override wins for brand bg"

              Expect.equal
                  (ThemeManifest.tryGetToken "tone.default.bg" merged |> Option.map _.Value)
                  (Some "#ffffff")
                  "shell default surface retained"

              // The Brand role binding (same Role key in both) resolves to
              // the overridden token's (override-wins) — and only once.
              let brandBindings =
                  merged.Roles
                  |> List.filter (fun r ->
                      match r.Role with
                      | ManifestRole.Tone ToneVariant.Brand -> true
                      | _ -> false)

              Expect.equal (List.length brandBindings) 1 "no duplicate Brand binding after merge"
          }

          test "merge keeps base tokens absent from the override" {
              let baseM =
                  Project.projectFromCssCustomProperties ":root { --a: #111111; --b: #222222; }"

              let over =
                  Project.projectFromCssCustomProperties ":root { --b: #333333; --c: #444444; }"

              let merged = Project.merge baseM over

              Expect.equal
                  (ThemeManifest.tryGetToken "a" merged |> Option.map _.Value)
                  (Some "#111111")
                  "base-only a kept"

              Expect.equal (ThemeManifest.tryGetToken "b" merged |> Option.map _.Value) (Some "#333333") "b overridden"

              Expect.equal
                  (ThemeManifest.tryGetToken "c" merged |> Option.map _.Value)
                  (Some "#444444")
                  "override-only c added"
          }

          // ─── CSS scanner robustness ──────────────────────────────

          test "scanCssBlocks strips comments + ignores non-custom-property declarations" {
              let css =
                  """
                  /* a comment */
                  :root {
                    color: red;            /* not a custom property */
                    --token: #abcdef;
                  }
                  """

              let m = Project.projectFromCssCustomProperties css
              Expect.equal (List.length m.Tokens) 1 "only the custom property is a token"
              Expect.isSome (ThemeManifest.tryGetToken "token" m) "the -- declaration projected"
          } ]
