module Fuaran.UI.Renderer.Css

// ============================================================================
//  Fuaran — the per-node CLASS-STRING vocabulary (Phase 207).
//
//  Every builder below is one `inline` string concatenation producing a class
//  attribute the renderers emit per node per frame. Two reasons they live here
//  rather than at the call sites:
//
//   1. PARITY. The class-name vocabulary is locked across the F# client
//      renderer, the F# SSR renderer, and the other reference-implementation
//      renderers (the SSR class/aria parity corpus pins it). Two renderers that
//      each spell `"fuaran-metric fuaran-metric-" + tone` inline can drift by a
//      space; one shared builder cannot. The vocabulary is now readable in one
//      place — this file IS the class contract.
//
//   2. PERF. These run on the hottest path the renderers have. `sprintf` parses
//      its format string at RUNTIME on every call — under Fable that is a JS
//      format-parse per node per frame, paid in the browser — where `+` lowers
//      to a plain concatenation on both pipelines. The builders are `inline`, so
//      a call site pays no function-call overhead either.
//
//  **This is a perf primitive: do NOT "simplify" any body back to `sprintf` or
//  to a fragment list + `String.concat`.** The output is byte-identical, so no
//  behavioural test can see the regression — the named builder is the guard, and
//  `Fuaran.UI.Tests/HotPathVocabularyTests.fs` asserts the call sites keep using
//  it.
//
//  Every builder takes ALREADY-RESOLVED fragments (`Theme.toneVar spec.Tone`,
//  a variant class, a pre-computed suffix) rather than the typed spec, so this
//  module stays free of the tree vocabulary and both renderers can share it
//  whatever they resolved from.
// ============================================================================

// ─── Layout ────────────────────────────────────────────────────────────────

/// `fuaran-layout-stack <direction><wrapSuffix>` — `wrapSuffix` is `""` or
/// `" fuaran-stack-wrap"`, already spaced by the caller.
let inline layoutStack (direction: string) (wrapSuffix: string) : string =
    "fuaran-layout-stack " + direction + wrapSuffix

/// `fuaran-layout-tabs <orientationClass>`.
let inline layoutTabs (orientationClass: string) : string =
    "fuaran-layout-tabs " + orientationClass

// ─── Display ───────────────────────────────────────────────────────────────

/// `fuaran-heading<variantSuffix>` — `variantSuffix` is `""` or a leading-space
/// modifier (` fuaran-heading-eyebrow` …).
let inline heading (variantSuffix: string) : string = "fuaran-heading" + variantSuffix

/// `fuaran-icon fuaran-icon--<sizeClass> fuaran-icon-<tone>` — the standalone
/// `Icon` display kind's class, emitted once per icon node.
let inline icon (sizeClass: string) (tone: string) : string =
    "fuaran-icon fuaran-icon--" + sizeClass + " fuaran-icon-" + tone

/// `fuaran-badge fuaran-badge-<variantClass>`.
let inline badge (variantClass: string) : string =
    "fuaran-badge fuaran-badge-" + variantClass

/// `fuaran-toast fuaran-toast-<toneClass>`.
let inline toast (toneClass: string) : string =
    "fuaran-toast fuaran-toast-" + toneClass

/// `fuaran-codeblock-code language-<language>`.
let inline codeBlockCode (language: string) : string =
    "fuaran-codeblock-code language-" + language

/// `fuaran-metric fuaran-metric-<tone>`.
let inline metric (tone: string) : string = "fuaran-metric fuaran-metric-" + tone

/// `fuaran-fact fuaran-fact-<tone><emphasisSuffix>` — `emphasisSuffix` is `""`
/// or `" fuaran-fact-emphasis"`.
let inline fact (tone: string) (emphasisSuffix: string) : string =
    "fuaran-fact fuaran-fact-" + tone + emphasisSuffix

/// `fuaran-callout fuaran-callout-<tone>`.
let inline callout (tone: string) : string = "fuaran-callout fuaran-callout-" + tone

/// `fuaran-progress fuaran-progress-<tone><indeterminateSuffix>` —
/// `indeterminateSuffix` is `""` or `" fuaran-progress-indeterminate"`.
let inline progress (tone: string) (indeterminateSuffix: string) : string =
    "fuaran-progress fuaran-progress-" + tone + indeterminateSuffix

/// `fuaran-label-value-row<emphasisSuffix>`.
let inline labelValueRow (emphasisSuffix: string) : string =
    "fuaran-label-value-row" + emphasisSuffix

// ─── Input ─────────────────────────────────────────────────────────────────

/// `fuaran-button fuaran-button-<variantClass>`.
let inline button (variantClass: string) : string =
    "fuaran-button fuaran-button-" + variantClass

/// `fuaran-button fuaran-button-<variantClass> fuaran-button-unwired` — the
/// client-only unwired-action operator cue.
let inline buttonUnwired (variantClass: string) : string =
    "fuaran-button fuaran-button-" + variantClass + " fuaran-button-unwired"

/// `fuaran-filter fuaran-filter-<kindClass>`.
let inline filter (kindClass: string) : string =
    "fuaran-filter fuaran-filter-" + kindClass

// ─── Vis ───────────────────────────────────────────────────────────────────

/// `fuaran-grid-cell-pill fuaran-pill-<tone>` — emitted per CELL, so this is
/// the densest builder here: a 1000-row grid with a pill column calls it 1000
/// times per frame.
let inline gridCellPill (tone: string) : string =
    "fuaran-grid-cell-pill fuaran-pill-" + tone

// ─── Custom (host-extension placements) ────────────────────────────────────
//
//  `moduleId` / `componentId` reach these already sanitised by the caller
//  (`Theme.sanitiseClassFragment`'s discipline) — these builders concatenate,
//  they do not sanitise.

/// `fuaran-kind-custom fuaran-custom-<moduleId>-<componentId>` — the per-node
/// KIND class for a `Custom` node, reached from `Theme.kindClass` on every
/// render of every Custom node.
let inline kindCustom (moduleId: string) (componentId: string) : string =
    "fuaran-kind-custom fuaran-custom-" + moduleId + "-" + componentId

/// `fuaran-kind-custom-placeholder fuaran-custom-<moduleId>-<componentId>`.
let inline customPlaceholder (moduleId: string) (componentId: string) : string =
    "fuaran-kind-custom-placeholder fuaran-custom-" + moduleId + "-" + componentId

/// `fuaran-custom-hash-mismatch fuaran-custom-<moduleId>-<componentId>`.
let inline customHashMismatch (moduleId: string) (componentId: string) : string =
    "fuaran-custom-hash-mismatch fuaran-custom-" + moduleId + "-" + componentId

/// `fuaran-custom-wrapper fuaran-custom-<moduleId>-<componentId>`.
let inline customWrapper (moduleId: string) (componentId: string) : string =
    "fuaran-custom-wrapper fuaran-custom-" + moduleId + "-" + componentId
