module Fuaran.UI.Renderer.Server.Tests.EgressRenderTests

// ============================================================================
//  The ambient destination policy, at the render seam (Phase 1026).
//
//  Phase 897 shipped `Sanitize.EgressPolicy` and its seam functions, and left
//  every renderer emission site calling `sanitizeUrlOrBlank` — so the policy was
//  AVAILABLE and not AMBIENT: a decoded tree's egress was checked only where a
//  host had remembered to ask. This corpus is the executable form of the claim
//  that it no longer is.
//
//  The acceptance property, stated once because everything below is a case of
//  it: a tree containing a non-local destination renders the REFUSAL under the
//  DEFAULT context, with no caller opt-in anywhere on the path. Every test here
//  therefore renders through an ORDINARY entry point — `Render.render`,
//  `Render.renderStatic` — never one that takes a policy. A test that passed a
//  policy in would be testing the seam Phase 897 already shipped, and would keep
//  passing on the day someone removed the default.
//
//  Two disciplines this corpus keeps deliberately:
//
//   1. **Every refusal test has an ALLOW twin.** A gate that refuses everything
//      passes every refusal assertion ever written, so a corpus of refusals
//      alone cannot distinguish "the policy works" from "the renderer is
//      broken". Each case below pins what still renders as well as what does
//      not.
//
//   2. **The go-red self-test.** `sanitizeUrlForEgress` under `permissiveEgress`
//      must EMIT the destination the default refuses. Without it, a bug that
//      made every href `about:blank#…` for an unrelated reason would read here
//      as a policy triumph.
//
//  Client/server parity is covered structurally rather than by byte-diff: the
//  F# Feliz client renderer cannot render to an HTML string on .NET (see the
//  header of `SsrParityTests.fs`), so the shared property asserted here is that
//  BOTH tiers call the same seam function with the same `EgressClass` for the
//  same node kind — pinned by `Sanitize.sanitizeUrlForEgress` being the single
//  emission path and by the class table below.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server

let private contains (needle: string) (haystack: string) =
    haystack.Contains(needle, System.StringComparison.Ordinal)

/// A destination that is entirely SAFE by the scheme floor — allowlisted
/// scheme, well-formed host, no script anywhere in it — and entirely
/// undeclared. This is the string the whole phase is about: every check that
/// existed before 1026 passes it.
let private undeclared = "https://collector.example/collect?s=SECRETVALUE"

// ─── The refusal vocabulary ─────────────────────────────────────────────────

[<Tests>]
let egressRenderTests =
    testList
        "Phase 1026 — ambient destination policy at the render seam"
        [

          test "a decoded tree's non-local Link href renders the REFUSAL under the default context" {
              // `Render.render` is the ordinary entry point. Nothing here
              // declares a policy; the refusal comes from the default alone,
              // which is the entire acceptance criterion.
              let html =
                  Render.render BindingResolver.empty (Fuaran.link "lk" undeclared "Click me")

              Expect.isTrue (contains Sanitize.egressRefusalUrl html) "the href is the refusal URL, not the destination"

              Expect.isFalse (contains "collector.example/collect" html) "the destination is not emitted"
              Expect.isFalse (contains "SECRETVALUE" html) "and neither is the query string"
          }

          test "the refusal is MARKED, naming the class and host — and nothing else" {
              let html =
                  Render.render BindingResolver.empty (Fuaran.link "lk" undeclared "Click me")

              Expect.isTrue
                  (contains Sanitize.egressRefusalAttribute html)
                  "the refusal carries its data attribute, so it is visible in the document"

              Expect.isTrue (contains "hyperlink:collector.example" html) "the marker names the class and the host"

              // The bound on what a refusal record carries is the same one every
              // other denial in this codebase keeps: the query string of a
              // refused exfiltration attempt IS the payload.
              Expect.isFalse (contains "SECRETVALUE" html) "the marker carries no query string"
              Expect.isFalse (contains "/collect" html) "and no path"
          }

          test "an Image src is refused too, and it is the class that matters most" {
              // A `Link` needs a click. An `Image` does not: rendering IS the
              // request, so this is the arm that exfiltrates on sight.
              let html = Render.render BindingResolver.empty (Fuaran.image "img" undeclared "A")

              Expect.isTrue (contains Sanitize.egressRefusalUrl html) "the src is the refusal URL"
              Expect.isTrue (contains "media:collector.example" html) "marked with the MEDIA class, not hyperlink"
              Expect.isFalse (contains "SECRETVALUE" html) "no query string reaches the document"
          }

          // ─── The allow twins ────────────────────────────────────────────────

          test "ALLOW twin — a same-origin href renders untouched under the same default" {
              // `AllowLocal = true`: the default denies LEAVING, not linking. If
              // this ever fails, the default has become unusable rather than
              // strict, and every in-app link in the estate is broken.
              let html =
                  Render.render BindingResolver.empty (Fuaran.link "lk" "/reports/42" "Report")

              Expect.isTrue (contains "href=\"/reports/42\"" html) "an ordinary in-app link is unchanged"
              Expect.isFalse (contains Sanitize.egressRefusalUrl html) "and carries no refusal"
              Expect.isFalse (contains Sanitize.egressRefusalAttribute html) "and no refusal marker"
          }

          test "ALLOW twin — a DECLARED origin renders untouched" {
              // The point of an allowlist is that declaring works. Rendered
              // through the policy-taking entry point, because declaring is
              // precisely the host act that entry point exists for.
              let policy =
                  Sanitize.denyNonLocalEgress
                  |> Sanitize.allowOrigin (Sanitize.ExactHost "cdn.example") [ Sanitize.EgressClass.Media ]

              let img = Fuaran.image "img" "https://cdn.example/logo.png" "Logo"

              let html = Render.renderWithEgress policy Registry.empty BindingResolver.empty img

              Expect.isTrue (contains "src=\"https://cdn.example/logo.png\"" html) "a declared origin is emitted"
              Expect.isFalse (contains Sanitize.egressRefusalUrl html) "and is not refused"
          }

          test "a declaration is scoped to its CLASS — the same host is refused for a class it was not declared for" {
              // `cdn.example` is declared for Media only. A hyperlink to it is
              // still refused: the class scoping is what stops "I need images
              // from here" from silently meaning "and links, downloads and
              // navigations too".
              let policy =
                  Sanitize.denyNonLocalEgress
                  |> Sanitize.allowOrigin (Sanitize.ExactHost "cdn.example") [ Sanitize.EgressClass.Media ]

              let html =
                  Render.renderWithEgress
                      policy
                      Registry.empty
                      BindingResolver.empty
                      (Fuaran.link "lk" "https://cdn.example/thing" "Thing")

              Expect.isTrue (contains Sanitize.egressRefusalUrl html) "declared for Media does not admit a Hyperlink"
              Expect.isTrue (contains "hyperlink:cdn.example" html) "and the marker says which class refused it"
          }

          test "a suffix rule matches at a LABEL BOUNDARY — `notexample.com` is not `example.com`" {
              let policy =
                  Sanitize.denyNonLocalEgress
                  |> Sanitize.allowOrigin (Sanitize.HostSuffix "example.com") [ Sanitize.EgressClass.Hyperlink ]

              let render (url: string) =
                  Render.renderWithEgress policy Registry.empty BindingResolver.empty (Fuaran.link "lk" url "L")

              let sub = render "https://a.b.example.com/x"
              Expect.isTrue (contains "a.b.example.com/x" sub) "a genuine subdomain is admitted"

              let lookalike = render "https://notexample.com/x"
              Expect.isTrue (contains Sanitize.egressRefusalUrl lookalike) "a suffix is not a substring"
              Expect.isFalse (contains "notexample.com/x" lookalike) "the lookalike host is not emitted"
          }

          test "credential confusion — `good.example@evil.example` is refused as the request to evil.example it is" {
              // The classic spelling an allowlist exists to refuse. A naive
              // first-`@` split reads this as a request to `good.example`; the
              // browser reads it as `evil.example`, and so must the policy.
              let policy =
                  Sanitize.denyNonLocalEgress
                  |> Sanitize.allowOrigin (Sanitize.ExactHost "good.example") [ Sanitize.EgressClass.Hyperlink ]

              let html =
                  Render.renderWithEgress
                      policy
                      Registry.empty
                      BindingResolver.empty
                      (Fuaran.link "lk" "https://good.example@evil.example/x" "L")

              Expect.isTrue (contains Sanitize.egressRefusalUrl html) "the userinfo does not launder the host"
              Expect.isTrue (contains "evil.example" html) "and the marker names the host actually addressed"
          }

          test "`mailto:` is refused by the DEFAULT — the consequence most likely to surprise a host" {
              // Recorded as a test rather than only in a migration note, because
              // it is the one behaviour change an adopting host meets without
              // having done anything wrong. `AllowNonNetwork = false`: a
              // `mailto:` body parameter carries arbitrary text off the machine
              // and has no host for a rule to name, so it can only be permitted
              // wholesale.
              let html =
                  Render.render BindingResolver.empty (Fuaran.link "lk" "mailto:user@example.com" "Email us")

              Expect.isTrue (contains Sanitize.egressRefusalUrl html) "an undeclared mailto is refused by default"
              Expect.isTrue (contains "hyperlink:mailto" html) "the marker names the scheme that had no origin"

              // …and the documented remedy actually works.
              let permitting =
                  { Sanitize.denyNonLocalEgress with
                      AllowNonNetwork = true }

              let allowed =
                  Render.renderWithEgress
                      permitting
                      Registry.empty
                      BindingResolver.empty
                      (Fuaran.link "lk" "mailto:user@example.com" "Email us")

              Expect.isTrue
                  (contains "href=\"mailto:user@example.com\"" allowed)
                  "AllowNonNetwork = true is the narrow, documented remedy"
          }

          // ─── The go-red self-test ───────────────────────────────────────────

          test "go-red self-test: the SAME tree renders the destination under permissiveEgress" {
              // Without this, a bug that blanked every href for an unrelated
              // reason would read as a policy success throughout the corpus
              // above. This proves the refusals are caused by the POLICY.
              let node = Fuaran.link "lk" undeclared "Click me"

              let refused = Render.render BindingResolver.empty node

              let permitted =
                  Render.renderWithEgress Sanitize.permissiveEgress Registry.empty BindingResolver.empty node

              Expect.isTrue (contains Sanitize.egressRefusalUrl refused) "default refuses"

              Expect.isTrue
                  (contains "collector.example/collect" permitted)
                  "permissive emits the very destination the default refused"

              Expect.isFalse
                  (contains Sanitize.egressRefusalUrl permitted)
                  "and carries no refusal marker, so the two outcomes are genuinely different"
          }

          test "the empty allowlist is NOT the permissive one" {
              // `denyNonLocalEgress` has no rules and `AllowAnyOrigin = false`.
              // A half-built policy and a decision not to have one must not be
              // the same value, and this is where that would bite.
              Expect.isFalse
                  (Sanitize.hasNonLocalEgress Sanitize.denyNonLocalEgress)
                  "declaring nothing permits nothing beyond the origin"

              Expect.isTrue
                  (Sanitize.hasNonLocalEgress Sanitize.permissiveEgress)
                  "and the permissive policy is distinguishable from it"
          }

          // ─── Ambience: the default reaches paths nobody wired by hand ────────

          test "the default reaches renderStatic — an entry point that mentions no policy at all" {
              // `renderStatic` bakes in `BindingResolver.empty` and takes only a
              // node. If the policy were a parameter rather than a field on the
              // context, this path is exactly the one that would have been
              // missed.
              let html = Render.renderStatic (Fuaran.link "lk" undeclared "Click me")

              Expect.isTrue (contains Sanitize.egressRefusalUrl html) "ambient means ambient"
          }

          test "the default reaches a NESTED destination, not just a root node" {
              // The policy travels on the render context, so depth is free — but
              // a call site wired only at the top level would pass every test
              // above and fail this one.
              let tree =
                  Fuaran.card
                      "crd"
                      { Defaults.card<obj> with
                          Children =
                              [ Fuaran.stack
                                    "stk"
                                    { Defaults.stack<obj> with
                                        Children = [ Fuaran.link "deep" undeclared "Deep" ] } ] }

              let html = Render.render BindingResolver.empty tree

              Expect.isTrue (contains Sanitize.egressRefusalUrl html) "a nested href is refused too"
              Expect.isFalse (contains "SECRETVALUE" html) "and leaks nothing"
          } ]
