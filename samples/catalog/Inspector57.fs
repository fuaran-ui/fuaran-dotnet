module Fuaran.Samples.Catalog.Inspector57

// ============================================================================
//  Snapshot-diff + op-stream visual inspector.
//
//  Mounted at `?inspector=1`. Demonstrates the operator-facing inspector UI
//  that consumes `Fuaran.UI.OpStream.Replay.TreeDiff` + `StepDiff` output:
//  a timeline scrubber over a curated 3-op segment, a tree-after render at
//  each step, and a side panel showing the OpRecord + telemetry event +
//  per-id diff entries.
//
//  Why the catalog can't import `Fuaran.UI.OpStream.Replay` directly: that
//  package's transitive `Fuaran.UI.Ops` dependency drags in `Apply.fs` +
//  `ErrorRender.fs`, which use `Utf8JsonWriter` / `MemoryStream` /
//  type-testing patterns Fable doesn't transpile (same rationale as the
//  Catalog.fsproj's `JsonShape.fs`-inline comment). This page therefore
//  ships a Fable-safe mirror of the relevant TreeDiff DUs + a hand-baked
//  3-step demo segment. The actual diff computation lives in the .NET
//  TreeDiff module + has Expecto coverage in
//  `Fuaran.UI.OpStream.Tests/TreeDiffTests.fs` (commit
//  fuaran@4905521). What the operator sees here is the inspector UX
//  pattern that consumes those results; the canonical results pipeline
//  produces the same shape on the server.
//
//  The demo segment exercises three op kinds:
//
//   1. RemoveNode 'right'        → Removed entry against id "right"
//   2. InsertChild 'middle'      → Added entry against id "middle" at dash/1
//   3. UpdateProp 'left' Text    → PropChanged + TextChanged against "left"
//
//  Hash-chain integrity: pre-computed `Verified` flag on every record so
//  the inspector's banner state ("Chain verified ✓" vs "Chain integrity
//  failed") is exercised in both shapes. Toggle via the `?inspector-fork=1`
//  query — sets the first record's `Verified` flag to false. Mirrors the
//  StepDiffsError.IntegrityFailed surface in the .NET impl.
// ============================================================================

open Feliz
open Browser.Types
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

// ─── Fable-safe TreeDiff / StepDiff mirror ────────────────────────────────
//
// One-to-one with Fuaran.UI.OpStream.Replay.TreeDiff's surface; renamed
// here only to flag the Fable-side mirror status. New cases added to the
// .NET side need to land here too (and the demo data needs to update) —
// or this page silently misses them.

type DemoNodeChangeKind =
    | Added of parent: string option * position: int
    | Removed of parent: string option
    | Moved of fromParent: string option * fromPosition: int * toParent: string option * toPosition: int
    | KindChanged of fromKind: string * toKind: string
    | PropChanged of fromJson: string * toJson: string
    | TextChanged of fromText: string * toText: string

type DemoNodeChange =
    { NodeId: string
      Change: DemoNodeChangeKind }

type DemoOpRecord =
    {
        Sequence: int
        OpSummary: string
        PromptId: string option
        Timestamp: string
        /// Pre-computed hash-chain integrity flag. `false` simulates
        /// `StepDiffsError.IntegrityFailed` for the inspector's banner
        /// state.
        Verified: bool
        /// Pre-computed telemetry event payload (telemetry-event shape).
        /// Inspector-side this is opaque text; on the server the .NET
        /// inspector would render the actual `OpApplyTelemetryEvent`.
        TelemetryLine: string
    }

type DemoStep =
    {
        Record: DemoOpRecord
        Changes: DemoNodeChange list
        /// Pre-rendered tree-after for this step. The inspector renders
        /// this via the standard `Render.render` path so the diff
        /// highlighting works against the live DOM that the renderer
        /// actually produces.
        TreeAfter: Node<unit>
    }

// ─── Demo trees ───────────────────────────────────────────────────────────

let private startTree: Node<unit> =
    Fuaran.dashboard
        "dash"
        { Defaults.dashboard<unit> with
            Children = [ Fuaran.markdown "left" "Left pane"; Fuaran.markdown "right" "Right pane" ] }

let private afterStep1: Node<unit> =
    // RemoveNode 'right'.
    Fuaran.dashboard
        "dash"
        { Defaults.dashboard<unit> with
            Children = [ Fuaran.markdown "left" "Left pane" ] }

let private afterStep2: Node<unit> =
    // InsertChild 'middle' at dash/1.
    Fuaran.dashboard
        "dash"
        { Defaults.dashboard<unit> with
            Children = [ Fuaran.markdown "left" "Left pane"; Fuaran.markdown "middle" "Middle pane" ] }

let private afterStep3: Node<unit> =
    // UpdateProp 'left' Text → "Left pane (updated)".
    Fuaran.dashboard
        "dash"
        { Defaults.dashboard<unit> with
            Children =
                [ Fuaran.markdown "left" "Left pane (updated)"
                  Fuaran.markdown "middle" "Middle pane" ] }

// ─── Demo step segment ────────────────────────────────────────────────────

let private demoSteps (verifiedChain: bool) : DemoStep list =
    [ { Record =
          { Sequence = 1
            OpSummary = "RemoveNode 'right'"
            PromptId = Some "prompt-7c3a"
            Timestamp = "2026-05-30T07:00:00Z"
            Verified = verifiedChain
            TelemetryLine = "outcome=success durationMs=0.42 streamId=demo userId=operator" }
        Changes =
          [ { NodeId = "right"
              Change = Removed(Some "dash") } ]
        TreeAfter = afterStep1 }

      { Record =
          { Sequence = 2
            OpSummary = "InsertChild 'middle' under 'dash' at position 1"
            PromptId = Some "prompt-7c3a"
            Timestamp = "2026-05-30T07:00:01Z"
            Verified = true
            TelemetryLine = "outcome=success durationMs=0.31 streamId=demo userId=operator" }
        Changes =
          [ { NodeId = "middle"
              Change = Added(Some "dash", 1) } ]
        TreeAfter = afterStep2 }

      { Record =
          { Sequence = 3
            OpSummary = "UpdateProp 'left' Text → \"Left pane (updated)\""
            PromptId = Some "prompt-7c3a"
            Timestamp = "2026-05-30T07:00:02Z"
            Verified = true
            TelemetryLine = "outcome=success durationMs=0.18 streamId=demo userId=operator" }
        Changes =
          [ { NodeId = "left"
              Change =
                PropChanged(
                    "…\"text\":{\"$type\":\"Literal\",\"text\":\"Left pane\"}…",
                    "…\"text\":{\"$type\":\"Literal\",\"text\":\"Left pane (updated)\"}…"
                ) }
            { NodeId = "left"
              Change = TextChanged("Left pane", "Left pane (updated)") } ]
        TreeAfter = afterStep3 } ]

// ─── Model ─────────────────────────────────────────────────────────────────

type Model =
    {
        Steps: DemoStep list
        ActiveStepIndex: int
        /// Mirrors `StepDiffsError.IntegrityFailed` — when `false`, the
        /// inspector banner surfaces the integrity-failure state and the
        /// step-detail panels are dimmed.
        ChainVerified: bool
    }

type Msg = SelectStep of int

let init (verifiedChain: bool) : Model =
    { Steps = demoSteps verifiedChain
      ActiveStepIndex = 0
      ChainVerified = verifiedChain }

let update (msg: Msg) (model: Model) : Model =
    match msg with
    | SelectStep i ->
        let clamped = max 0 (min i (List.length model.Steps - 1))
        { model with ActiveStepIndex = clamped }

// ─── View helpers ─────────────────────────────────────────────────────────

let private changeKindLabel (k: DemoNodeChangeKind) : string =
    match k with
    | Added(_, _) -> "Added"
    | Removed _ -> "Removed"
    | Moved(_, _, _, _) -> "Moved"
    | KindChanged(_, _) -> "KindChanged"
    | PropChanged(_, _) -> "PropChanged"
    | TextChanged(_, _) -> "TextChanged"

let private changeDetail (k: DemoNodeChangeKind) : string =
    match k with
    | Added(parent, position) -> sprintf "parent=%s position=%d" (parent |> Option.defaultValue "<root>") position
    | Removed parent -> sprintf "parent=%s" (parent |> Option.defaultValue "<root>")
    | Moved(fromParent, fromPos, toParent, toPos) ->
        sprintf
            "from=%s/%d → to=%s/%d"
            (fromParent |> Option.defaultValue "<root>")
            fromPos
            (toParent |> Option.defaultValue "<root>")
            toPos
    | KindChanged(fromKind, toKind) -> sprintf "%s → %s" fromKind toKind
    | PropChanged(fromJson, toJson) -> sprintf "from: %s  to: %s" fromJson toJson
    | TextChanged(fromText, toText) -> sprintf "\"%s\" → \"%s\"" fromText toText

let private renderTreeAfter (tree: Node<unit>) : ReactElement =
    let ctx: Render.RenderContext<unit> =
        { Sources = BindingResolver.empty
          Runtime = Runtime.diagnostic
          VisAdapter = VisAdapter.noOp<unit>
          Dispatch = (fun () -> ())
          TelemetrySink = None
          InErrorBoundary = false
          Fragments = Map.empty
          ExpandingFragments = Set.empty
          Scope = None
          SessionContext = Map.empty }

    Render.render ctx tree

let private timelineStep (model: Model) (dispatch: Msg -> unit) (index: int) (step: DemoStep) : ReactElement =
    let isActive = index = model.ActiveStepIndex

    let baseStyle =
        [ style.padding 8
          style.marginRight 8
          style.cursor "pointer"
          style.borderWidth 1
          style.borderStyle.solid
          style.borderRadius 4 ]

    let activeStyle =
        if isActive then
            [ style.backgroundColor "var(--fuaran-tone-brand-background)"
              style.color "var(--fuaran-tone-brand-foreground)" ]
        else
            [ style.backgroundColor "var(--fuaran-tone-subdued-background)" ]

    Html.button
        [ prop.testId (sprintf "inspector-step-%d" step.Record.Sequence)
          prop.style (baseStyle @ activeStyle)
          prop.onClick (fun _ -> dispatch (SelectStep index))
          prop.children
              [ Html.div
                    [ prop.style [ style.fontWeight 600 ]
                      prop.text (sprintf "Seq %d" step.Record.Sequence) ]
                Html.div
                    [ prop.style [ style.fontSize 12; style.opacity 0.8 ]
                      prop.text step.Record.OpSummary ] ] ]

let private renderChangeEntry (change: DemoNodeChange) : ReactElement =
    Html.li
        [ prop.style [ style.fontFamily "monospace"; style.fontSize 13 ]
          prop.children
              [ Html.strong [ prop.text (changeKindLabel change.Change) ]
                Html.text (sprintf " %s — " change.NodeId)
                Html.text (changeDetail change.Change) ] ]

let private sidePanel (step: DemoStep) : ReactElement =
    let entries = step.Changes |> List.map renderChangeEntry

    let opRecordDl =
        Html.dl
            [ prop.style [ style.fontFamily "monospace"; style.fontSize 13 ]
              prop.children
                  [ Html.dt [ prop.text "op" ]
                    Html.dd [ prop.text step.Record.OpSummary ]
                    Html.dt [ prop.text "promptId" ]
                    Html.dd [ prop.text (step.Record.PromptId |> Option.defaultValue "<none>") ]
                    Html.dt [ prop.text "timestamp" ]
                    Html.dd [ prop.text step.Record.Timestamp ]
                    Html.dt [ prop.text "hashChainVerified" ]
                    Html.dd [ prop.text (if step.Record.Verified then "✓" else "✗ INTEGRITY FAIL") ]
                    Html.dt [ prop.text "telemetry" ]
                    Html.dd [ prop.text step.Record.TelemetryLine ] ] ]

    Html.section
        [ prop.testId "inspector-side-panel"
          prop.style [ style.padding 12; style.marginTop 16 ]
          prop.children
              [ Html.h3 [ prop.text (sprintf "Step %d — op record" step.Record.Sequence) ]
                opRecordDl
                Html.h3 [ prop.text "Diff entries" ]
                Html.ul [ prop.testId "inspector-diff-entries"; prop.children entries ] ] ]

// ─── Root view ────────────────────────────────────────────────────────────

let view (model: Model) (dispatch: Msg -> unit) : ReactElement =
    let activeStep = model.Steps[model.ActiveStepIndex]

    let banner =
        if model.ChainVerified then
            Html.div
                [ prop.testId "inspector-chain-banner"
                  prop.style
                      [ style.padding 8
                        style.backgroundColor "var(--fuaran-tone-success-background)"
                        style.color "var(--fuaran-tone-success-foreground)" ]
                  prop.text "Chain verified ✓ — replay is faithful to the recorded segment." ]
        else
            Html.div
                [ prop.testId "inspector-chain-banner"
                  prop.style
                      [ style.padding 8
                        style.backgroundColor "var(--fuaran-tone-critical-background)"
                        style.color "var(--fuaran-tone-critical-foreground)" ]
                  prop.text
                      "Chain integrity failed ✗ — refusing to render diff against corrupt records. Surface from StepDiffsError.IntegrityFailed." ]

    React.Fragment
        [ Render.themeStyleElement Defaults.theme
          Html.div
              [ prop.id "inspector-page"
                prop.className "catalog-inspector"
                prop.style [ style.padding 24; style.maxWidth 960 ]
                prop.children
                    [ Html.h1 [ prop.text "Op-stream inspector" ]
                      Html.p
                          [ prop.text
                                "Per-step replay-diff over a curated three-op segment. Scrub the timeline to see what each op changed; the side panel shows the OpRecord + telemetry event + per-id diff entries. Append ?inspector-fork=1 to simulate a forged hash chain." ]
                      banner
                      Html.section
                          [ prop.style [ style.marginTop 16 ]
                            prop.children
                                [ Html.h2 [ prop.text "Timeline" ]
                                  Html.div
                                      [ prop.testId "inspector-timeline"
                                        prop.style [ style.display.flex; style.flexWrap.wrap ]
                                        prop.children (model.Steps |> List.mapi (timelineStep model dispatch)) ] ] ]
                      Html.section
                          [ prop.style [ style.marginTop 24 ]
                            prop.children
                                [ Html.h2 [ prop.text "Tree after this step" ]
                                  Html.div
                                      [ prop.testId "inspector-tree-after"
                                        prop.style
                                            [ style.padding 12
                                              style.borderWidth 1
                                              style.borderStyle.solid
                                              style.borderColor "var(--fuaran-tone-default-border)" ]
                                        prop.children [ renderTreeAfter activeStep.TreeAfter ] ] ] ]
                      sidePanel activeStep ] ] ]
