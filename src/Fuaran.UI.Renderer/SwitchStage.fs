module Fuaran.UI.Renderer.SwitchStage

// ============================================================================
//  Fuaran — the Switch stage: timed advance, transitions, swipe and arrow keys
//  (Phase 1122)
//
//  `SwitchSpec.AutoAdvanceMs` is the ONE thing on the wire here, and it says
//  only "this switch is meant to move on its own, this often". Everything else
//  in this file is renderer-owned under the affordance→op charter: which
//  gesture advances, which key does, how far a finger must travel before it
//  counts, what pauses the timer and what stops it for good. No event name, no
//  threshold and no gesture reaches the vocabulary, so a document says WHAT the
//  switch does and never HOW the reader takes it over.
//
//  ── WCAG 2.2.2 (Pause, Stop, Hide) is the design, not a decoration ────────
//  Content that moves automatically for more than five seconds must be
//  pausable, stoppable or hideable BY THE READER. Three obligations follow,
//  and the asymmetry between the second and the third is the whole rule:
//
//    * PAUSE while pointing, reading or touching. Hover, focus-within and a
//      held touch each suspend the timer and each release it again. This is a
//      courtesy: the reader has not asked for anything, so nothing is decided.
//    * STOP PERMANENTLY on interaction. A reader who swipes, presses an arrow
//      key or clicks inside the stage has taken control, and a carousel that
//      resumes on its own afterwards drags them off whatever they chose to
//      look at. So the stop is a ONE-WAY latch for the life of the mount:
//      there is deliberately no resume path, no timeout back to running, and
//      no "resume after inactivity" heuristic.
//    * NEVER START under `prefers-reduced-motion: reduce`. Stated here rather
//      than left to the stylesheet, because a stylesheet can suppress the
//      TRANSITION and cannot suppress the ADVANCE — the content would still
//      change under the reader, silently, which is the harm the preference is
//      about. The two halves therefore live in two places on purpose: the
//      reduce rule in the reference sheet makes the cross-fade and the slide
//      inert, and this module makes the timer never exist.
//
//  ── Why the decisions are PURE functions ─────────────────────────────────
//  `nextMatch`, `stepMatch`, `stageMode` and `swipeIntent` carry every
//  judgement this file makes, and none of them touches a browser. That is
//  deliberate: the .NET test runner mounts no DOM, so a state machine buried
//  inside a `useEffect` could only ever be asserted about in prose. Pulled out,
//  each obligation above is pinned by a test that can fail — which is what
//  "fixture-proven, not asserted" has to mean for behaviour a headless runner
//  cannot observe. The effect below is then only plumbing: it reads the mode
//  and starts or clears one interval.
//
//  ── Why a function component ─────────────────────────────────────────────
//  The interval and the gesture listeners are EFFECTS with a lifetime, and the
//  composition-based renderer holds no hooks — the `LocalBindings` /
//  `ComboboxControl` / `PopoverSurface` shape, for the same reason.
//
//  ── The hydration contract (Phase 289) ───────────────────────────────────
//  The rendered markup is the child `Render.fs` already built, inside one
//  wrapper `<div>` whose classes are computed from the spec alone. The server
//  emits the same wrapper (see `docs/SSR.md`), so hydration finds the DOM it
//  expects. `data-fuaran-switch-state` is written from render state, not
//  imperatively, and its first value is always `"running"` or `"inert"` —
//  never a value that depends on a gesture the server could not have seen.
// ============================================================================

#nowarn "3261"

open Fable.Core
open Feliz

[<Import("createElement", "react")>]
let private reactCreateElement (componentFn: obj) (props: obj) : ReactElement = jsNative

// ─── The decisions, as pure functions ───────────────────────────────────────

/// What the stage is doing. Four states and no fifth: the three WCAG
/// obligations above plus the ordinary running case. `Inert` and `Stopped` are
/// distinct even though neither advances — `Inert` is a switch that never had a
/// timer (no interval declared, fewer than two cases, no writable key, or the
/// reader's reduced-motion preference), `Stopped` is one the reader turned off.
/// Collapsing them would make the rendered `data-fuaran-switch-state` unable to
/// say whether a stationary carousel was never running or was stopped, which is
/// exactly the distinction an audit needs to see.
[<RequireQualifiedAccess>]
type StageMode =
    /// The timer is live at the declared interval.
    | Running of intervalMs: int
    /// The timer exists but is suspended — pointer, focus or held touch.
    | Paused
    /// The reader interacted; the timer is gone for the life of this mount.
    | Stopped
    /// No timer was ever started.
    | Inert

[<RequireQualifiedAccess>]
module StageMode =

    /// The stable token rendered as `data-fuaran-switch-state`. Kept beside the
    /// type so the projection cannot drift from the case set.
    let token (m: StageMode) : string =
        match m with
        | StageMode.Running _ -> "running"
        | StageMode.Paused -> "paused"
        | StageMode.Stopped -> "stopped"
        | StageMode.Inert -> "inert"

/// Which case the timer moves to next, in DECLARATION order, wrapping at the
/// end. `None` means there is nothing to advance to and the caller starts no
/// timer.
///
/// Three total-function decisions worth stating because each could have gone
/// another way:
///
///   * A `current` that matches no case — the switch is showing its `Default` —
///     advances to the FIRST case. The alternative, staying on the default
///     forever, would make an auto-advancing carousel whose state key happens
///     to be unset silently do nothing at all.
///   * A single case does not advance. Wrapping from the only case to itself
///     would rewrite the state key on every tick with the value it already
///     holds, which is a re-render loop that changes nothing a reader can see.
///   * Duplicate `Match` values resolve on the FIRST occurrence, matching the
///     renderer's own first-match-wins selection. FUARAN082 already reports the
///     duplicate; disagreeing with the selection rule here would make the
///     advance skip a case for a reason nothing explains.
let nextMatch (matches: string list) (current: string option) : string option =
    match matches with
    | []
    | [ _ ] -> None
    | _ ->
        let idx =
            match current with
            | Some c -> List.tryFindIndex (fun m -> m = c) matches
            | None -> None

        match idx with
        | Some i -> Some(matches[(i + 1) % matches.Length])
        | None -> Some(List.head matches)

/// The `Match` a directed step lands on — `+1` for the next case, `-1` for the
/// previous — wrapping in both directions. Shares `nextMatch`'s three rules and
/// its degenerate cases; a single-case switch steps nowhere in either
/// direction. A `current` outside the case set steps to the FIRST case going
/// forward and the LAST going back, which is what a reader pressing an arrow
/// key on a defaulted switch means by "the one before this".
let stepMatch (matches: string list) (current: string option) (step: int) : string option =
    match matches with
    | []
    | [ _ ] -> None
    | _ ->
        let n = matches.Length

        let idx =
            match current with
            | Some c -> List.tryFindIndex (fun m -> m = c) matches
            | None -> None

        match idx with
        | Some i -> Some(matches[((i + step) % n + n) % n])
        | None -> Some(if step >= 0 then List.head matches else List.last matches)

/// The stage's mode from the five facts that decide it. Ordered by precedence,
/// and the order is the rule rather than an implementation detail:
///
///   1. `stopped` wins over everything. The reader's decision is not overridden
///      by a preference change, a re-render or a pause ending.
///   2. Then the reasons a timer never exists — no declared interval, a
///      non-positive one, nothing to advance to, or
///      `prefers-reduced-motion: reduce`.
///   3. Then `paused`.
///   4. Otherwise it runs.
///
/// `hasNext` folds together "fewer than two cases" and "no writable state key":
/// both mean the tick would have nowhere to go, and the caller computes it once.
let stageMode
    (autoAdvanceMs: int option)
    (hasNext: bool)
    (reducedMotion: bool)
    (stopped: bool)
    (paused: bool)
    : StageMode =
    if stopped then
        StageMode.Stopped
    else
        match autoAdvanceMs with
        | Some ms when ms > 0 && hasNext && not reducedMotion ->
            if paused then StageMode.Paused else StageMode.Running ms
        | _ -> StageMode.Inert

/// Which way a horizontal drag meant to go, or `None` when it was not a swipe.
///
/// `dx` is (end − start) in CSS pixels, so a NEGATIVE `dx` is a finger moving
/// left, which advances — the reader is dragging the next panel into view, the
/// same direction every touch carousel has used since the gesture existed.
///
/// The 40px threshold is renderer-owned and stated once here. It is above the
/// ~10px a browser already absorbs as a tap and below the width of a fingertip
/// contact, so an ordinary tap on a control inside the stage does not register
/// as a swipe. It is deliberately NOT on the wire: a threshold is a property of
/// the input device and the reader's hand, not of the document.
let swipeIntent (dx: float) : int option =
    if dx <= -40.0 then Some 1
    elif dx >= 40.0 then Some -1
    else None

// ─── The browser edges ──────────────────────────────────────────────────────

/// The reader's reduced-motion preference. Emit-based because `matchMedia` has
/// no Feliz binding here, and guarded on every side: a host with no `window`,
/// no `matchMedia` (older embedded webviews) or a throwing implementation
/// answers `false`, which is the pre-1122 behaviour and therefore the honest
/// default — "I cannot read the preference" must not silently become "the
/// reader asked for reduced motion".
[<Emit("""(function(){
  try {
    if (typeof window === 'undefined' || !window.matchMedia) return false;
    return !!window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  } catch (e) { return false; }
})()""")>]
let prefersReducedMotion () : bool = jsNative

/// The `clientX` of the first touch in the named `TouchEvent` list, or `None`
/// when the list is empty. Emit-based because `TouchList` is indexed
/// differently on the two pipelines — the .NET Browser typings and Fable's do
/// not agree on the member — and because a swipe is a browser-only act in any
/// case. Guarded like every other edge in this file: an absent or empty list
/// answers `None`, which means "not a swipe", never "a swipe of zero".
[<Emit("""(function(e, which){
  try {
    var l = e && e[which];
    if (!l || !l.length) return undefined;
    return l[0].clientX;
  } catch (err) { return undefined; }
})($0, $1)""")>]
let private firstTouchX (e: obj) (which: string) : float option = jsNative

// ─── The component ──────────────────────────────────────────────────────────

type SwitchStageProps =
    {| nodeId: string
       className: string
       autoAdvanceMs: int option
       matches: string list
       current: string option
       advanceTo: (string -> unit) option
       children: ReactElement list |}

let private renderStage (props: SwitchStageProps) : ReactElement =
    let stopped, setStopped = React.useState false
    let paused, setPaused = React.useState false
    let reduced, setReduced = React.useState false

    // Read the preference after mount, never during render: the server has no
    // media query, so consulting it in the render body would make the first
    // client render differ from the SSR floor and break hydration. The initial
    // `false` reproduces the server's markup exactly; the effect then corrects
    // it in the commit that follows, which at worst leaves a reduced-motion
    // reader with one interval that is cleared before it can fire.
    React.useEffect ((fun () -> setReduced (prefersReducedMotion ())), [| box props.nodeId |])

    let canWrite = Option.isSome props.advanceTo
    let hasNext = canWrite && (nextMatch props.matches props.current |> Option.isSome)
    let mode = stageMode props.autoAdvanceMs hasNext reduced stopped paused

    // The one interval. Re-created whenever anything the mode depends on
    // changes, and cleared on every teardown — so a stop, a pause, a
    // preference read or an advance leaves no orphan timer behind.
    React.useEffect (
        (fun () ->
            match mode, props.advanceTo with
            | StageMode.Running ms, Some write ->
                let handle =
                    Browser.Dom.window.setInterval (
                        (fun () -> nextMatch props.matches props.current |> Option.iter write),
                        ms
                    )

                fun () -> Browser.Dom.window.clearInterval handle
            | _ -> id),
        [| box (StageMode.token mode)
           box (
               match mode with
               | StageMode.Running ms -> ms
               | _ -> 0
           )
           box props.current
           box (String.concat " " props.matches) |]
    )

    // The reader's take-over. One helper because the latch and the step are the
    // same act however it arrived — a swipe, an arrow key or a press inside the
    // stage all mean "I am driving now".
    let takeOver (step: int option) : unit =
        setStopped true

        match step, props.advanceTo with
        | Some s, Some write -> stepMatch props.matches props.current s |> Option.iter write
        | _ -> ()

    let touchStartX = React.useRef (None: float option)

    let onTouchStart (e: Browser.Types.TouchEvent) : unit =
        setPaused true
        touchStartX.current <- firstTouchX (box e) "touches"

    let onTouchEnd (e: Browser.Types.TouchEvent) : unit =
        setPaused false

        match touchStartX.current, firstTouchX (box e) "changedTouches" with
        | Some startX, Some endX ->
            touchStartX.current <- None

            match swipeIntent (endX - startX) with
            | Some step -> takeOver (Some step)
            | None -> ()
        | _ -> touchStartX.current <- None

    let onKeyDown (e: Browser.Types.KeyboardEvent) : unit =
        // Arrow keys are the keyboard equivalent of the swipe, per the phase's
        // affordance ruling — the same act, so the same latch. Nothing else is
        // claimed: `Tab` still moves out of the stage, and no key is swallowed
        // that the reader might have meant for a control inside it.
        match e.key with
        | "ArrowRight" ->
            takeOver (Some 1)
            e.preventDefault ()
        | "ArrowLeft" ->
            takeOver (Some -1)
            e.preventDefault ()
        | _ -> ()

    Html.div
        [ prop.className props.className
          prop.custom ("data-fuaran-switch-state", StageMode.token mode)
          match props.autoAdvanceMs with
          | Some ms -> prop.custom ("data-fuaran-switch-auto-advance-ms", string ms)
          | None -> ()
          // A tab stop ONLY where the arrow keys do something. A focusable
          // wrapper that responds to nothing is a stop on the reader's tab
          // order that buys them nothing, and a static switch is exactly the
          // pre-1122 element.
          if hasNext then
              prop.tabIndex 0
              prop.role "group"
              prop.onKeyDown onKeyDown
              // The stop latch on a pointer press with no step: the reader
              // pressed a control inside the stage, so the timer must not drag
              // it out from under them, but nothing advances.
              prop.onMouseDown (fun _ -> takeOver None)
              prop.onMouseEnter (fun _ -> setPaused true)
              prop.onMouseLeave (fun _ -> setPaused false)
              // Focus-WITHIN. React's synthetic `onFocus` / `onBlur` are
              // `focusin` / `focusout` and therefore BUBBLE, unlike the DOM
              // events of the same name — so focus landing on any control
              // inside the stage pauses it. That is the case WCAG 2.2.2 is
              // actually about: a reader tabbing through the panel's links
              // while it slides away underneath them.
              prop.onFocus (fun _ -> setPaused true)
              prop.onBlur (fun _ -> setPaused false)
              prop.onTouchStart onTouchStart
              prop.onTouchEnd onTouchEnd
          prop.children props.children ]

/// The public surface — `Render.fs` invokes this for a `Switch` and renders the
/// selected child itself, so there is exactly one definition of the markup and
/// this file adds only the behaviour around it.
let stage (props: SwitchStageProps) : ReactElement =
    reactCreateElement (box renderStage) (box props)
