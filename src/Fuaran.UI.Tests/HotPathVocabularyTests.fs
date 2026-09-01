module Fuaran.UI.Tests.HotPathVocabularyTests

open System
open System.IO
open System.Text.RegularExpressions
open Expecto
open Fuaran.UI.Renderer

// ============================================================================
//  Phase 207 — the hot-path allocation regression LOCK.
//
//  Phase 207 replaced the renderers' per-node `sprintf` class/id construction
//  and their list-copying prop and option paths with named `inline` primitives
//  (`Css.*`, `Ids.*`, `StateStore.overlayOnto`, …). Every one of those edits is
//  OUTPUT-IDENTICAL, which is precisely why it needs a lock of this shape: no
//  behavioural test can see a revert. A later "make it more functional" pass
//  that put `sprintf "fuaran-metric fuaran-metric-%s"` back would leave every
//  renderer test, the SSR class/aria parity corpus and the CSS coverage suite
//  green while reinstating a runtime format-string parse per node per frame —
//  paid in the browser under Fable.
//
//  So this module asserts the SHAPE of the call sites, not their output, and it
//  does so by reading the renderer sources the test project copies into its own
//  output (the `ChartProvenanceTests` / `CssCoverageTests` precedent: copied
//  rather than resolved by climbing, so the scan cannot read a different
//  checkout's sources than the ones this build compiled).
//
//  It also pins the new primitives against the exact `sprintf` forms they
//  replaced, so the byte-identity claim is checkable here rather than asserted
//  in a comment.
//
//  ONE EXEMPTION is declared below, in the falsifiable style the estate uses
//  elsewhere: the entry must still be MATCHED by the scan, so it cannot outlive
//  the literal it excuses.
// ============================================================================

// ─── Inputs ────────────────────────────────────────────────────────────────

let private rendererSourceDir: string =
    Path.Combine(AppContext.BaseDirectory, "renderer-sources")

let private sourceText (tier: string) (file: string) : string =
    let path = Path.Combine(rendererSourceDir, tier, file)

    if not (File.Exists path) then
        failwithf
            "renderer source not found at %s — the Fuaran.UI.Tests project copies the renderer sources into its output; check the Content items. A shape scan with no source to scan reports every call site as clean."
            path

    File.ReadAllText path

/// The two renderers whose per-node call sites this phase re-routed, plus the
/// vocabulary modules themselves.
let private scannedSources: (string * string) list =
    [ "client/Render.fs", sourceText "client" "Render.fs"
      "server/Render.fs", sourceText "server" "Render.fs"
      "core/Theme.fs", sourceText "core" "Theme.fs"
      "core/Css.fs", sourceText "core" "Css.fs"
      "core/Ids.fs", sourceText "core" "Ids.fs" ]

let private sourceOf (name: string) : string =
    scannedSources |> List.find (fst >> (=) name) |> snd

/// Line comments are stripped before every shape assertion: the phase's own
/// intent comments quote the forms they forbid ("do NOT simplify this back to
/// sprintf"), and a scan that counted those would fail on the documentation of
/// the rule it enforces.
let private code (text: string) : string = Regex.Replace(text, @"(?m)//.*$", "")

let private stringLiteral =
    Regex("\"((?:[^\"\\\\\r\n]|\\\\.)*)\"", RegexOptions.Compiled)

/// A printf-style specifier: %s, %d, %.2f, %A, %08x, …
let private formatSpecifier =
    Regex(@"%[-+0 #]*[\d.*]*[bscdiouxXeEfFgGMOAat]", RegexOptions.Compiled)

/// The composite-id shapes the renderers mint per node.
let private idFragment = Regex(@"-(tab|panel|opt)-", RegexOptions.Compiled)

/// A literal BUILDS a Fuaran class or an ARIA id by interpolation when one of
/// its WHITESPACE-SEPARATED TOKENS both carries a format specifier and is
/// shaped like a class name or a composite id.
///
/// Tokenising rather than searching the whole literal is what keeps the guard
/// honest, and each exclusion below is a real near-miss that a whole-literal
/// `Contains "fuaran-"` reported:
///
///  - `--fuaran-tone-%s-bg` — a CSS VARIABLE name, projected once per theme,
///    not per node. The token starts with `--`, not `fuaran-`.
///  - `[data-fuaran-node-id=\"%s\"]` — a DOM SELECTOR built for a one-off
///    verification query. The token is a bracketed attribute selector.
///  - "…the served stylesheet is stamped with class vocabulary %s…" — a
///    diagnostic MESSAGE. Its `fuaran-`-shaped words carry no specifier and its
///    specifier-bearing words are not class-shaped.
///
/// A guard that fired on all three would be turned off within a week, which is
/// worse than not having it.
let private classToken = Regex(@"^fuaran-[a-zA-Z0-9%._*-]+$", RegexOptions.Compiled)

let private isVocabularyToken (token: string) : bool =
    formatSpecifier.IsMatch token
    && (classToken.IsMatch token || idFragment.IsMatch token)

let private isInterpolatedVocabulary (literal: string) : bool =
    literal.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)
    |> Array.exists isVocabularyToken

/// Every such literal in a source, in file order.
let private interpolatedVocabularyLiterals (text: string) : string list =
    stringLiteral.Matches(code text)
    |> Seq.cast<Match>
    |> Seq.map (fun m -> m.Groups[1].Value)
    |> Seq.filter isInterpolatedVocabulary
    |> List.ofSeq

// ─── The declared exemption ────────────────────────────────────────────────

/// A literal that matches the shape above and is legitimately NOT a per-node
/// render emission. Declared with its reason, and asserted to still be present:
/// an exemption whose literal has gone FAILS rather than accumulating.
let private declaredExemptions: (string * string * string) list =
    [ "client/Render.fs",
      "fuaran-commit-local-%s",
      "A DOM EVENT NAME dispatched from `Action.CommitLocal`, not a class and not an id. It is minted once per user \
       interaction — a click — never per node per frame, so it is off the render hot path entirely and carries none \
       of the cost this phase removed." ]

// ─── Helpers ───────────────────────────────────────────────────────────────

/// Boxed-value helper — the `FragmentMemoTests` / `FragmentApplyTests`
/// `Unchecked.nonNull` discipline, so the `obj`-typed store values type-check
/// under nullable reference types.
let private v (x: obj | null) : obj = Unchecked.nonNull x

let private occurrences (needle: string) (haystack: string) : int =
    Regex.Matches(haystack, Regex.Escape needle).Count

[<Tests>]
let tests =
    testList
        "HotPathVocabulary (Phase 207)"
        [

          // ══ The probe before the verdict ═══════════════════════════════════
          //  A scan that read nothing would report every call site clean, and a
          //  green result is exactly what that looks like. Pin the floor first.

          test "the scan actually reads the renderer sources" {
              for name, text in scannedSources do
                  Expect.isGreaterThan text.Length 500 $"{name} was read and is not a stub"

              Expect.isGreaterThan
                  (sourceOf "client/Render.fs").Length
                  100_000
                  "the client renderer is the whole file, not a fragment"
          }

          test "the shape predicate can go red — it matches the forms this phase removed" {
              // Falsify the detector itself: the exact literals the pre-207 call
              // sites carried must be recognised, or every assertion below is
              // vacuous.
              Expect.isTrue
                  (isInterpolatedVocabulary "fuaran-metric fuaran-metric-%s")
                  "the pre-207 Metric class format string is detected"

              Expect.isTrue (isInterpolatedVocabulary "%s-tab-%d") "the pre-207 tab-id format string is detected"

              Expect.isTrue
                  (isInterpolatedVocabulary "fuaran-kind-custom fuaran-custom-%s-%s")
                  "the pre-207 Custom kind-class format string is detected"

              Expect.isTrue
                  (isInterpolatedVocabulary "fuaran-icon fuaran-icon--%s fuaran-icon-%s")
                  "the pre-207 Icon class format string is detected"

              Expect.isFalse
                  (isInterpolatedVocabulary "fuaran-metric fuaran-metric-")
                  "a concatenation root is not an interpolation"

              Expect.isFalse
                  (isInterpolatedVocabulary "[fuaran:fragment cycle]")
                  "a diagnostic message carrying no specifier is not a class build"

              // The three near-misses the token rule exists to let through.
              Expect.isFalse (isInterpolatedVocabulary "--fuaran-tone-%s-bg") "a CSS variable name is not a class"

              Expect.isFalse
                  (isInterpolatedVocabulary "[data-fuaran-node-id=\"%s\"]")
                  "a DOM attribute selector is not a class"

              Expect.isFalse
                  (isInterpolatedVocabulary
                      "Fuaran stylesheet version skew: the served stylesheet is stamped with class vocabulary %s, but this renderer emits %s.")
                  "a diagnostic message is not a class build"
          }

          // ══ The verdict ════════════════════════════════════════════════════

          test "no class or ARIA id is built by string interpolation in a renderer source" {
              let exempted =
                  declaredExemptions
                  |> List.map (fun (file, literal, _) -> file, literal)
                  |> Set.ofList

              let offenders =
                  [ for name, text in scannedSources do
                        for literal in interpolatedVocabularyLiterals text do
                            if not (Set.contains (name, literal) exempted) then
                                yield name + ": " + literal ]

              Expect.equal
                  offenders
                  []
                  "a Fuaran class name or composite ARIA id is being built with a runtime format string on the render \
                   path. Route it through the named Css / Ids builder in Fuaran.UI.Renderer.Core instead (Phase 207) — \
                   or, if it is genuinely not a per-node render emission, declare it in declaredExemptions with its \
                   reason"
          }

          test "every declared exemption still names a literal the scan finds" {
              for file, literal, reason in declaredExemptions do
                  let found = interpolatedVocabularyLiterals (sourceOf file) |> List.contains literal

                  Expect.isTrue
                      found
                      ("the exemption for '"
                       + literal
                       + "' in "
                       + file
                       + " no longer matches anything — delete it rather than leaving a mute-list entry that only has \
                          to be plausible. Reason on file: "
                       + reason)
          }

          test "no renderer call site calls List.append" {
              // The node wrapper's 4-way `List.append` was the single most-executed
              // allocation in the renderers; nothing on the render path should be
              // reaching for it again.
              for name in [ "client/Render.fs"; "server/Render.fs" ] do
                  Expect.equal
                      (occurrences "List.append" (code (sourceOf name)))
                      0
                      (name + " builds its prop lists without List.append")
          }

          test "the class/id vocabulary modules carry no sprintf beyond the one declared value format" {
              // `Ids.deterministicCorrelationId` legitimately formats a hash as
              // hex — that is a VALUE, minted once per FAILING node, not part of
              // the per-node class/id vocabulary. Everything else concatenates.
              Expect.equal
                  (occurrences "sprintf" (code (sourceOf "core/Css.fs")))
                  0
                  "Css builders concatenate, never format"

              Expect.equal
                  (occurrences "sprintf" (code (sourceOf "core/Ids.fs")))
                  1
                  "Ids carries exactly one sprintf: the correlation-id hex format"
          }

          test "the renderers actually USE the vocabulary (not merely ship it)" {
              // A guard that only forbade the old shape would pass on a renderer
              // that had stopped emitting classes altogether.
              for name in [ "client/Render.fs"; "server/Render.fs" ] do
                  let text = code (sourceOf name)
                  Expect.isGreaterThan (occurrences "Css." text) 8 (name + " routes its class strings through Css")
                  Expect.isGreaterThan (occurrences "Ids." text) 2 (name + " routes its composite ids through Ids")
          }

          // ══ Byte-identity against the forms they replaced ══════════════════
          //
          //  "The output is unchanged" is a checkable claim, so check it here
          //  rather than asserting it in a comment. Each case carries the
          //  pre-207 `sprintf` on the right-hand side.

          test "every Css builder is byte-identical to the sprintf it replaced" {
              Expect.equal
                  (Css.layoutStack "fuaran-stack-vertical" " fuaran-stack-wrap")
                  (sprintf "fuaran-layout-stack %s%s" "fuaran-stack-vertical" " fuaran-stack-wrap")
                  "layoutStack"

              Expect.equal
                  (Css.layoutTabs "fuaran-tabs-vertical")
                  (sprintf "fuaran-layout-tabs %s" "fuaran-tabs-vertical")
                  "layoutTabs"

              Expect.equal (Css.heading "") (sprintf "fuaran-heading%s" "") "heading (default variant)"

              Expect.equal
                  (Css.heading " fuaran-heading-lead")
                  (sprintf "fuaran-heading%s" " fuaran-heading-lead")
                  "heading (variant)"

              Expect.equal (Css.badge "success") (sprintf "fuaran-badge fuaran-badge-%s" "success") "badge"
              Expect.equal (Css.toast "critical") (sprintf "fuaran-toast fuaran-toast-%s" "critical") "toast"

              Expect.equal
                  (Css.codeBlockCode "fsharp")
                  (sprintf "fuaran-codeblock-code language-%s" "fsharp")
                  "codeBlockCode"

              Expect.equal (Css.metric "brand") (sprintf "fuaran-metric fuaran-metric-%s" "brand") "metric"

              Expect.equal
                  (Css.fact "info" " fuaran-fact-emphasis")
                  (sprintf "fuaran-fact fuaran-fact-%s%s" "info" " fuaran-fact-emphasis")
                  "fact"

              Expect.equal (Css.callout "warning") (sprintf "fuaran-callout fuaran-callout-%s" "warning") "callout"

              Expect.equal
                  (Css.progress "default" " fuaran-progress-indeterminate")
                  (sprintf "fuaran-progress fuaran-progress-%s%s" "default" " fuaran-progress-indeterminate")
                  "progress"

              Expect.equal
                  (Css.labelValueRow " fuaran-label-value-row-emphasis")
                  (sprintf "fuaran-label-value-row%s" " fuaran-label-value-row-emphasis")
                  "labelValueRow"

              Expect.equal (Css.button "primary") (sprintf "fuaran-button fuaran-button-%s" "primary") "button"

              Expect.equal
                  (Css.buttonUnwired "ghost")
                  (sprintf "fuaran-button fuaran-button-%s fuaran-button-unwired" "ghost")
                  "buttonUnwired"

              Expect.equal (Css.filter "choice") (sprintf "fuaran-filter fuaran-filter-%s" "choice") "filter"

              Expect.equal
                  (Css.gridCellPill "subdued")
                  (sprintf "fuaran-grid-cell-pill fuaran-pill-%s" "subdued")
                  "gridCellPill"

              Expect.equal
                  (Css.kindCustom "charts" "gantt")
                  (sprintf "fuaran-kind-custom fuaran-custom-%s-%s" "charts" "gantt")
                  "kindCustom"

              Expect.equal
                  (Css.customPlaceholder "charts" "gantt")
                  (sprintf "fuaran-kind-custom-placeholder fuaran-custom-%s-%s" "charts" "gantt")
                  "customPlaceholder"

              Expect.equal
                  (Css.customHashMismatch "charts" "gantt")
                  (sprintf "fuaran-custom-hash-mismatch fuaran-custom-%s-%s" "charts" "gantt")
                  "customHashMismatch"

              Expect.equal
                  (Css.customWrapper "charts" "gantt")
                  (sprintf "fuaran-custom-wrapper fuaran-custom-%s-%s" "charts" "gantt")
                  "customWrapper"
          }

          test "every Ids builder is byte-identical to the sprintf it replaced" {
              Expect.equal (Ids.tab "panel.tabs" 0) (sprintf "%s-tab-%d" "panel.tabs" 0) "tab (index 0)"
              Expect.equal (Ids.tab "panel.tabs" 12) (sprintf "%s-tab-%d" "panel.tabs" 12) "tab (multi-digit)"
              Expect.equal (Ids.panel "panel.tabs" 3) (sprintf "%s-panel-%d" "panel.tabs" 3) "panel"
              Expect.equal (Ids.optionId "form.choice" 7) (sprintf "%s-opt-%d" "form.choice" 7) "optionId"
          }

          // ══ The state read view ════════════════════════════════════════════
          //
          //  These run against a PRIVATE `StateStoreInstance`, never the
          //  process-global default. Expecto runs this suite in parallel and the
          //  default store is one process-wide singleton with one subscriber list
          //  (see the single-process assumption in `StateStore.fs`), so an
          //  assertion about exactly what a store holds cannot use it — and a
          //  `reset ()` to make one possible would clear a concurrently-running
          //  test's state out from under it.

          test "the read view is the snapshot-then-fold it replaced, without the intermediate map" {
              let store = StateStore.StateStoreInstance "fuaran.test.phase207.overlay."
              store.Set("terms", v (box "real"))
              store.Set("locale", v (box "en-GB"))

              // One key the store overrides, one it does not — so both halves of
              // "store wins" are exercised.
              let target: Map<string, obj> =
                  Map.ofList [ "terms", v (box "cash"); "theme", v (box "dark") ]

              let reference =
                  store.Snapshot() |> Map.fold (fun acc k value -> Map.add k value acc) target

              Expect.equal (store.OverlayOnto(target, id)) reference "the read view agrees with the pre-207 merge"

              Expect.equal
                  (store.OverlayOnto(target, id) |> Map.find "terms")
                  (v (box "real"))
                  "the store wins on a collision"

              Expect.equal
                  (store.OverlayOnto(target, id) |> Map.find "theme")
                  (v (box "dark"))
                  "an untouched host key survives"
          }

          test "an empty store returns the caller's map unchanged, allocating nothing" {
              let store = StateStore.StateStoreInstance "fuaran.test.phase207.empty."
              let target: Map<string, obj> = Map.ofList [ "theme", v (box "dark") ]

              Expect.isTrue store.IsEmpty "a fresh instance holds nothing"

              Expect.isTrue
                  (Object.ReferenceEquals(store.OverlayOnto(target, id), target))
                  "an empty store hands back the very same map — a state-free tree allocates nothing here"
          }

          test "the read view re-keys through the mapper it is handed" {
              // The Selection channel's shape: `BindingSources.Selections` is
              // keyed by the wrapped id, the store by the raw string.
              let store = StateStore.StateStoreInstance "fuaran.test.phase207.keys."
              store.Set("grid-1", v (box "row-7"))

              let overlaid =
                  store.OverlayOnto((Map.empty: Map<string, obj>), (fun k -> "wrapped:" + k))

              Expect.equal
                  (Map.toList overlaid)
                  [ "wrapped:grid-1", v (box "row-7") ]
                  "each raw key is re-keyed exactly once"
          }

          test "the module facades are wired to their own default stores" {
              // The instance tests above cannot see a facade pointed at the wrong
              // instance. This one only asserts the PRESENCE of its own uniquely
              // named key, so it stays safe beside a parallel test writing others.
              try
                  StateStore.set "phase207.wiring" (v (box "state"))
                  FilterStore.set "phase207.wiring" (v (box "filter"))
                  QueryStore.set "phase207.wiring" (v (box "query"))
                  SelectionStore.set "phase207.wiring" (v (box "selection"))

                  Expect.equal
                      (StateStore.overlayOnto Map.empty |> Map.tryFind "phase207.wiring")
                      (Some(v (box "state")))
                      "StateStore.overlayOnto reads the default state store"

                  Expect.equal
                      (FilterStore.overlayOnto Map.empty |> Map.tryFind "phase207.wiring")
                      (Some(v (box "filter")))
                      "FilterStore.overlayOnto reads the default filter store"

                  Expect.equal
                      (QueryStore.overlayOnto Map.empty |> Map.tryFind "phase207.wiring")
                      (Some(v (box "query")))
                      "QueryStore.overlayOnto reads the default query store"

                  Expect.equal
                      (SelectionStore.overlayOntoBy id Map.empty |> Map.tryFind "phase207.wiring")
                      (Some(v (box "selection")))
                      "SelectionStore.overlayOntoBy reads the default selection store"

                  Expect.isFalse (StateStore.isEmpty ()) "a written default store does not report itself empty"
              finally
                  StateStore.remove "phase207.wiring"
                  FilterStore.clear "phase207.wiring"
                  QueryStore.clear "phase207.wiring"
                  SelectionStore.clear "phase207.wiring"
          } ]
