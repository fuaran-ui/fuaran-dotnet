module Fuaran.Program.Parity.Tests.ProofOracleTests

// ============================================================================
//  Phase 1715 — the differential host for the proved bounded fold.
//
//  `proofs/BoundedFold.fst` is a model of `BoundedActions.runBoundedActionWith`
//  and four theorems about it. A model is a claim about the code only if
//  something runs the two side by side, and this is that something: it runs the
//  EXTRACTION of the model (`proofs/oracle/BoundedFold.fs`, byte-identical to
//  what the prover emitted — `proofs/check.ps1` step 4) beside production over
//  two corpora, comparing the store, the effect list and the diagnostics at
//  once, and reporting the FIRST divergence with the case that produced it.
//
//  What is being compared, exactly. The model axiomatises the binding
//  resolver, the URL floor, the host-reserved predicate and the log-safe route
//  projection: they are total arrows it takes as parameters. This host supplies
//  those arrows by calling PRODUCTION's own implementations, so the only thing
//  that can disagree here is the FOLD — which is what the theorems are about.
//  An axiom wired to a second implementation would be certifying the wrong
//  half.
//
//  The two corpora answer different questions.
//
//    * The conformance corpus's driver-semantics family is the documents this
//      repository is certified against. Every scripted event is resolved to the
//      `Action` the trust boundary would hand the fold, and each is run through
//      both, threading the store from step to step. It answers "does the model
//      agree on the actions real documents contain".
//    * The arm-complete corpus names every arm of the closed union and every
//      refusal path inside the three arms that have one. It answers "does the
//      model agree on the actions that are HARD", which the first corpus has no
//      reason to contain and mostly does not.
//
//  And the go-red case is what says the comparison can lose at all. It commits
//  a fold that INVOKES a carried closure — the one defect the model's law 2
//  rules out and the one a bounded interpreter must never have — and requires
//  the comparison against the oracle to fail on it. Without that, a comparison
//  that silently agreed with everything would report the same green.
// ============================================================================

module ProvedBudget = Budget

open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation
open Fuaran.UI.Renderer.BindingResolver
open Fuaran.Program.Bounded
open Fuaran.Program.UI
open Fuaran.Program.Parity

/// Non-null box (F# 10 nullness: `box` yields `objnull`; the store's
/// `Map<string, obj>` wants non-null — the same `o` posture the bounded
/// interpreter's own suite uses).
let private o (v: 'T) : obj = box v |> Unchecked.nonNull

// ─── Translation: production ⇄ the model ────────────────────────────────────
//
// The model owns small closed types of its own so its extraction references
// `Prims` and nothing else (`proofs/oracle/Prims.fs` is that whole runtime), so
// a translation is unavoidable. It is deliberately DUMB — a constructor for a
// constructor, with no decision in it — because a translation that decided
// anything would be a third implementation of the fold, sitting between the two
// this file exists to compare.

/// Aliases, and not for brevity: `unbox<Binding<string>>` closes two angle
/// brackets in a row and `unbox<Map<string, JVal>>` closes two of its own, both
/// of which F# lexes as the shift and composition operators before it lexes
/// them as type arguments.
type private TextBinding = Binding<string>
type private JValBinding = Binding<JVal>
type private I18nArgs = Map<string, Binding<JVal>>

let private modelOpt (x: 'a option) : BoundedFold.opt<'a> =
    match x with
    | Some v -> BoundedFold.OSome v
    | None -> BoundedFold.ONone

let private modelTarget (t: NavigateTarget) : BoundedFold.nav_target =
    match t with
    | NavigateTarget.Self -> BoundedFold.NSelf
    | NavigateTarget.Blank -> BoundedFold.NBlank

let private prodTarget (t: BoundedFold.nav_target) : NavigateTarget =
    match t with
    | BoundedFold.NSelf -> NavigateTarget.Self
    | BoundedFold.NBlank -> NavigateTarget.Blank

let private modelEncoding (e: FileReadEncoding) : BoundedFold.file_encoding =
    match e with
    | FileReadEncoding.Text -> BoundedFold.FText
    | FileReadEncoding.Base64 -> BoundedFold.FBase64
    | FileReadEncoding.DataUrl -> BoundedFold.FDataUrl

let private modelText (t: TextSource) : BoundedFold.text_source<obj> =
    match t with
    | TextSource.Literal text -> BoundedFold.TLiteral text
    | TextSource.Bound binding -> BoundedFold.TBound(o binding)
    | TextSource.I18n(key, args) -> BoundedFold.TI18n(key, o args)

let private prodText (t: BoundedFold.text_source<obj>) : TextSource =
    match t with
    | BoundedFold.TLiteral text -> TextSource.Literal text
    | BoundedFold.TBound binding -> TextSource.Bound(unbox<TextBinding> binding)
    | BoundedFold.TI18n(key, args) -> TextSource.I18n(key, unbox<I18nArgs> args)

let private modelCallTarget (t: CallResultTarget) : BoundedFold.call_target =
    match t with
    | CallResultTarget.State key -> BoundedFold.CTState key
    | CallResultTarget.Query name -> BoundedFold.CTQuery name

// `Action.Dispatch` is marked in-process-only upstream, so mentioning it raises
// FS0044. The translation below is a TOTAL analysis of the closed union: it
// must name every case that exists, and naming one is not authoring one.
// Scoped to the one declaration, and reopened immediately after it.
#nowarn "44"

let rec private modelAction (a: Action<obj>) : BoundedFold.action<obj, obj, obj> =
    match a with
    | Action.Chain ops -> BoundedFold.AChain(ops |> List.map modelAction)
    | Action.WriteToClipboard text -> BoundedFold.AWriteToClipboard(modelText text)
    | Action.Dispatch msg -> BoundedFold.ADispatch(o msg)
    | Action.Invoke(capabilityId, args) -> BoundedFold.AInvoke(capabilityId, o args)
    | Action.ReadFileBody(fileRef, fileHandle, encoding, onRead) ->
        BoundedFold.AReadFileBody(fileRef, o fileHandle, modelEncoding encoding, o onRead)
    | Action.Call(endpoint, onResult, into) ->
        BoundedFold.ACall(endpoint, o onResult, modelOpt (into |> Option.map modelCallTarget))
    | Action.Navigate(route, target) -> BoundedFold.ANavigate(modelText route, modelTarget target)
    | Action.CommitLocal nodeId -> BoundedFold.ACommitLocal nodeId
    | Action.Notify(channel, payload) -> BoundedFold.ANotify(channel, o payload)
    | Action.SetState(key, value, valueFrom) ->
        // `JValObj.toObj` is the model's lowering AXIOM, applied here rather
        // than modelled — the literal payload reaches the model already lowered,
        // exactly as the resolver arrow's resolved value does.
        BoundedFold.ASetState(
            key,
            modelOpt (value |> Option.map (fun jv -> JValObj.toObj jv)),
            modelOpt (valueFrom |> Option.map o)
        )
    | Action.AiTool(toolName, args) -> BoundedFold.AAiTool(toolName, o args)
    | Action.Print -> BoundedFold.APrint
    | Action.Confirm(prompt, onConfirm, onCancel) ->
        BoundedFold.AConfirm(modelText prompt, modelAction onConfirm, modelOpt (onCancel |> Option.map modelAction))
    | Action.Focus nodeId -> BoundedFold.AFocus nodeId

#warnon "44"

let private modelEffect (e: ClientEffect) : BoundedFold.client_effect =
    match e with
    | ClientEffect.Navigate(route, target) -> BoundedFold.ENavigate(route, modelTarget target)
    | ClientEffect.WriteToClipboard text -> BoundedFold.EClipboard text
    | ClientEffect.Print -> BoundedFold.EPrint
    | ClientEffect.Focus nodeId -> BoundedFold.EFocus nodeId
    | ClientEffect.ReadFileBody(nodeId, encoding) -> BoundedFold.EReadFileBody(nodeId, encoding)
    | other ->
        // The model's effect union carries exactly the arms the bounded fold
        // emits, which is a CLAIM about the fold rather than a convenience —
        // and this is where it is checked. A `PushState`, a `Download` or a
        // `Confirm` reaching here means the fold gained a reach the model does
        // not describe, and failing loudly is the only honest answer: mapping
        // it to something would make the model agree by discarding the
        // disagreement.
        failwithf
            "the bounded fold emitted %A, which proofs/BoundedFold.fst's effect union does not carry. Either the fold gained an arm or the model is stale — do not widen this match without widening the model."
            other

let private prodEffect (e: BoundedFold.client_effect) : ClientEffect =
    match e with
    | BoundedFold.ENavigate(route, target) -> ClientEffect.Navigate(route, prodTarget target)
    | BoundedFold.EClipboard text -> ClientEffect.WriteToClipboard text
    | BoundedFold.EPrint -> ClientEffect.Print
    | BoundedFold.EFocus nodeId -> ClientEffect.Focus nodeId
    | BoundedFold.EReadFileBody(nodeId, encoding) -> ClientEffect.ReadFileBody(nodeId, encoding)

let private modelDiagnostic (d: BoundedDiagnostic) : BoundedFold.diagnostic =
    match d with
    | BoundedDiagnostic.UnsupportedOnBoundedPath(nodeId, action) -> BoundedFold.DUnsupported(nodeId, action)
    | BoundedDiagnostic.Refused(nodeId, action, reason) -> BoundedFold.DRefused(nodeId, action, reason)

let private prodDiagnostic (d: BoundedFold.diagnostic) : BoundedDiagnostic =
    match d with
    | BoundedFold.DUnsupported(nodeId, action) -> BoundedDiagnostic.UnsupportedOnBoundedPath(nodeId, action)
    | BoundedFold.DRefused(nodeId, action, reason) -> BoundedDiagnostic.Refused(nodeId, action, reason)

/// The model's store is the `State` channel alone, as an association list. The
/// other channels are host context the fold never writes — they reach the model
/// only through the axioms below, which rebuild the real `BindingSources` from
/// the step's own store.
let private modelStore (s: BoundedStore) : BoundedFold.store<obj> = s.State |> Map.toList

let private prodStore (template: BoundedStore) (ms: BoundedFold.store<obj>) : BoundedStore =
    { template with State = Map.ofList ms }

// ─── The axioms, wired to production ────────────────────────────────────────

let private axiomsFor (template: BoundedStore) : BoundedFold.axioms<obj, obj> =
    { is_reserved = Fuaran.UI.Renderer.StateKeys.isHostReserved
      reserved_prefix = Fuaran.UI.Renderer.StateKeys.HostReservedPrefix
      resolve_jval =
        fun ms binding ->
            match resolveJVal (prodStore template ms) (unbox<JValBinding> binding) with
            | Resolved jv -> BoundedFold.JResolved(JValObj.toObj jv)
            | NotResolved -> BoundedFold.JNotResolved
            | Errored m -> BoundedFold.JErrored m
            | I18nUnresolved k -> BoundedFold.JI18nUnresolved k
      resolve_scalar =
        fun ms binding ->
            match resolveScalarText (prodStore template ms) (unbox<TextBinding> binding) with
            // The resolved-but-NULL value is the unwritten-`State` steady state,
            // and the two `TextSource` arms treat it differently — so it is
            // carried into the model rather than collapsed here.
            | Resolved value ->
                BoundedFold.SResolved(
                    if isNull (box value) then
                        BoundedFold.ONone
                    else
                        BoundedFold.OSome value
                )
            | NotResolved -> BoundedFold.SNotResolved
            | Errored m -> BoundedFold.SErrored m
            | I18nUnresolved k -> BoundedFold.SI18nUnresolved k
      i18n_has = fun ms key -> Map.containsKey key (prodStore template ms).I18n
      resolve_text = fun ms text -> resolveTextSource (prodStore template ms) (prodText text)
      sanitize_url =
        fun url ->
            match Fuaran.UI.Renderer.Sanitize.sanitizeUrl url with
            | Some safe -> BoundedFold.OSome safe
            | None -> BoundedFold.ONone
      route_path = Fuaran.UI.Ops.ActionInvocation.ActionInvocation.routePath }

let private modelArm (template: BoundedStore) (h: HandlerArm<obj>) : BoundedFold.arm<obj, obj> =
    { answer =
        fun nodeId endpoint ms placement ->
            match h.Answer nodeId endpoint (prodStore template ms) placement with
            | None -> BoundedFold.ONone
            | Some answer ->
                BoundedFold.OSome
                    { h_store = modelStore answer.Store
                      h_effects = answer.Effects |> List.map modelEffect
                      h_diagnostics = answer.Diagnostics |> List.map modelDiagnostic
                      h_placement = answer.Placement } }

// ─── The comparison ─────────────────────────────────────────────────────────

/// The fold under comparison. Production is one; the go-red case below is
/// another, and it is deliberately wrong.
type private Fold = HandlerArm<obj> -> string -> Action<obj> -> BoundedStore -> obj -> BoundedOutcome * obj

let private production: Fold = BoundedActions.runBoundedActionWith

/// The store, the effects and the diagnostics at once — a fold that got the
/// store right and the diagnostics wrong is still a fold that disagrees, and a
/// comparison that looked at one field would not say so.
let private divergence (where: string) (prod: BoundedOutcome) (model: BoundedFold.outcome<obj>) : string option =
    let prodState = prod.Store.State |> Map.toList
    let modelState = model.o_store |> List.sortBy fst
    let modelEffects = model.o_effects |> List.map prodEffect
    let modelDiagnostics = model.o_diagnostics |> List.map prodDiagnostic

    if prodState <> modelState then
        Some(sprintf "%s: STORE\n  production: %A\n  oracle:     %A" where prodState modelState)
    elif prod.Effects <> modelEffects then
        Some(sprintf "%s: EFFECTS\n  production: %A\n  oracle:     %A" where prod.Effects modelEffects)
    elif prod.Diagnostics <> modelDiagnostics then
        Some(sprintf "%s: DIAGNOSTICS\n  production: %A\n  oracle:     %A" where prod.Diagnostics modelDiagnostics)
    elif prod.Halted <> model.o_halted then
        Some(sprintf "%s: HALTED\n  production: %b\n  oracle:     %b" where prod.Halted model.o_halted)
    else
        None

/// Run one action through both folds against the same store, and report the
/// divergence if there is one. Returns the store production produced, so a
/// caller can thread a script of events through it.
let private step
    (fold: Fold)
    (arm: HandlerArm<obj>)
    (where: string)
    (nodeId: string)
    (action: Action<obj>)
    (s: BoundedStore)
    : BoundedStore * string option =
    let prod, _ = fold arm nodeId action s (o ())

    let modelOutcome, _ =
        BoundedFold.run (axiomsFor s) (modelArm s arm) nodeId (modelAction action) (modelStore s) (o ())

    prod.Store, divergence where prod modelOutcome

let private runScript
    (fold: Fold)
    (arm: HandlerArm<obj>)
    (cases: (string * string * Action<obj>) list)
    (initial: BoundedStore)
    : string list =
    cases
    |> List.fold
        (fun (s, found) (where, nodeId, action) ->
            let next, d = step fold arm where nodeId action s

            next,
            (match d with
             | Some m -> m :: found
             | None -> found))
        (initial, [])
    |> snd
    |> List.rev

// ─── Corpus A: the conformance corpus's driver-semantics family ─────────────

/// Every scripted event of every driver-semantics scenario, resolved to the
/// `Action` the trust boundary would hand the fold. The gate is opened (`fun _
/// -> true`) deliberately: the dispatch policy decides WHETHER an action is
/// offered to the fold, and this family is about what the fold does with one.
let private corpusCases () : (string * string * Action<obj>) list =
    FixtureIo.load FixtureIo.fixturesRoot
    |> List.collect (fun fixture ->
        match JsonDecode.decodeNode fixture.TreeJson with
        | Error err -> failwithf "%s: the corpus's tree did not decode: %A" fixture.Name err
        | Ok wire ->
            let tree = WireTree.reify wire

            fixture.Events
            |> List.mapi (fun index ev ->
                let live: LiveEvent =
                    { ConnId = "proof-oracle"
                      NodeId = ev.NodeId
                      Event = ev.Event
                      Payload = ev.Payload |> Map.map (fun _ v -> LiveValue.Str v)
                      LastSeq = index }

                match Validation.validate (fun _ -> true) tree live with
                | Ok validated ->
                    validated.Action
                    |> Option.map (fun action ->
                        (sprintf "%s step %d (%s)" fixture.Name index ev.NodeId, ev.NodeId, action))
                | Error _ -> None)
            |> List.choose id)

// ─── Corpus B: every arm, and every refusal path inside one ─────────────────

let private jstr (s: string) = JStr s

/// A store with something in every channel the axioms read, so a binding that
/// resolves and a binding that does not are both reachable.
let private seeded: BoundedStore =
    { empty with
        State = Map.ofList [ "greeting", o "hello"; "route", o "/orders/7" ]
        I18n = Map.ofList [ "title", "Orders" ] }

let private armCompleteCases: (string * string * Action<obj>) list =
    let case name action = (name, "n1", action)

    [ // The one mutation, and its three refusal paths.
      case "SetState literal" (Action.SetState("written", Some(jstr "v"), None))
      case "SetState overwrite" (Action.SetState("greeting", Some(jstr "bonjour"), None))
      case
          "SetState host-reserved key"
          (Action.SetState(Fuaran.UI.Renderer.StateKeys.HostReservedPrefix + "secret", Some(jstr "v"), None))
      case
          "SetState valueFrom resolved"
          (Action.SetState("copy", None, Some(Binding.State("greeting", Some(jstr "x")))))
      case "SetState valueFrom unresolved" (Action.SetState("copy", None, Some(Binding.Filter("absent", None))))
      case "SetState no payload at all" (Action.SetState("copy", None, None))

      // Navigate: literal, bound, i18n, and the floor.
      case "Navigate literal" (Action.Navigate(TextSource.Literal "/orders", NavigateTarget.Self))
      case "Navigate literal blank" (Action.Navigate(TextSource.Literal "/orders", NavigateTarget.Blank))
      case "Navigate unsafe scheme" (Action.Navigate(TextSource.Literal "javascript:alert(1)", NavigateTarget.Self))
      case
          "Navigate bound resolved"
          (Action.Navigate(TextSource.Bound(Binding.State("route", Some "/fallback")), NavigateTarget.Self))
      case
          "Navigate bound unresolved"
          (Action.Navigate(TextSource.Bound(Binding.Filter("absent", None)), NavigateTarget.Self))
      case "Navigate i18n present" (Action.Navigate(TextSource.I18n("title", Map.empty), NavigateTarget.Self))
      case "Navigate i18n absent" (Action.Navigate(TextSource.I18n("missing", Map.empty), NavigateTarget.Self))

      // Clipboard: the same three sources, with the opposite answer for a
      // resolved-but-empty value.
      case "Clipboard literal" (Action.WriteToClipboard(TextSource.Literal "copied"))
      case "Clipboard bound resolved" (Action.WriteToClipboard(TextSource.Bound(Binding.State("greeting", Some "d"))))
      case
          "Clipboard bound unwritten state"
          (Action.WriteToClipboard(TextSource.Bound(Binding.State("never-written", None))))
      case "Clipboard bound unresolved" (Action.WriteToClipboard(TextSource.Bound(Binding.Filter("absent", None))))
      case "Clipboard i18n present" (Action.WriteToClipboard(TextSource.I18n("title", Map.empty)))
      case "Clipboard i18n absent" (Action.WriteToClipboard(TextSource.I18n("missing", Map.empty)))

      // The payload-free and node-addressed arms.
      case "Print" Action.Print
      case "Focus" (Action.Focus "target-node")
      case "ReadFileBody Text" (Action.ReadFileBody("f", None, FileReadEncoding.Text, None))
      case "ReadFileBody Base64" (Action.ReadFileBody("f", None, FileReadEncoding.Base64, Some(fun _ -> o "never")))
      case "ReadFileBody DataUrl" (Action.ReadFileBody("f", None, FileReadEncoding.DataUrl, None))

      // The documented no-ops.
      case "Notify" (Action.Notify("audit", jstr "p"))
      case "AiTool" (Action.AiTool("summarise", jstr "p"))
      case "Invoke" (Action.Invoke("cap", []))
      case "CommitLocal" (Action.CommitLocal "field")
      case
          "Confirm"
          (Action.Confirm(TextSource.Literal "sure?", Action.SetState("confirmed", Some(jstr "y"), None), None))

      // The call arms: declined, and refused for declaring its own target.
      case "Call declined" (Action.Call("/api/x", None, None))
      case "Call with closure declined" (Action.Call("/api/x", Some(fun _ -> o "never"), None))
      case "Call with result target" (Action.Call("/api/x", None, Some(CallResultTarget.State "slot")))

      // Composition, including the nesting a chain is for.
      case
          "Chain of two writes"
          (Action.Chain
              [ Action.SetState("a", Some(jstr "1"), None)
                Action.SetState("b", Some(jstr "2"), None) ])
      case
          "Chain splicing a write between two effects"
          (Action.Chain
              [ Action.Navigate(TextSource.Literal "/first", NavigateTarget.Self)
                Action.SetState("spliced", Some(jstr "mid"), None)
                Action.WriteToClipboard(TextSource.Bound(Binding.State("spliced", Some "d"))) ])
      case
          "Chain carrying closures at depth"
          (Action.Chain
              [ Action.Call("/api/y", Some(fun _ -> o "never"), None)
                Action.Chain [ Action.ReadFileBody("f", None, FileReadEncoding.Text, Some(fun _ -> o "never")) ] ])
      case "Empty chain" (Action.Chain []) ]

/// The `Dispatch` arm needs its own binding: constructing one raises FS0044,
/// and the suppression is scoped to the declaration that needs it rather than
/// to the whole corpus above.
#nowarn "44"

let private dispatchCase: string * string * Action<obj> =
    ("Dispatch", "n1", Action.Dispatch(o "a host message"))

#warnon "44"

// ─── A placement that ANSWERS, so the seam is exercised rather than assumed ──

/// An arm that answers one endpoint by writing into the store and emitting an
/// effect, and declines everything else. The fold threads its answer in place,
/// which is what makes a call inside a chain see the writes before it — so an
/// arm that answers is the only way the chain arm's interesting case is
/// reached at all.
let private answeringArm: HandlerArm<obj> =
    { Answer =
        fun _ endpoint s placement ->
            if endpoint = "/api/answered" then
                Some
                    { Store =
                        { s with
                            State = Map.add "answered" (o "yes") s.State }
                      Effects = [ ClientEffect.WriteToClipboard "from the handler" ]
                      Diagnostics =
                        [ BoundedDiagnostic.Refused("n1", "Call(/api/answered)", "a reason the handler chose") ]
                      Placement = placement }
            else
                None }

let private answeredCases: (string * string * Action<obj>) list =
    [ ("Call answered", "n1", Action.Call("/api/answered", None, None))
      ("Call declined by an arm that answers something else", "n1", Action.Call("/api/other", None, None))
      ("Answered call spliced inside a chain",
       "n1",
       Action.Chain
           [ Action.SetState("before", Some(jstr "1"), None)
             Action.Call("/api/answered", Some(fun _ -> o "never"), None)
             Action.WriteToClipboard(TextSource.Bound(Binding.State("answered", Some "d"))) ]) ]

// ─── The go-red case ────────────────────────────────────────────────────────

/// A fold that INVOKES the closure a `Call` carries and lets its answer reach
/// the store. This is precisely what `run_no_closure` rules out and what the
/// wire decoder's inert sentinels exist to make harmless, and it is committed
/// here on purpose: a differential that cannot be made to fail is not evidence
/// of anything. Everything else defers to production, so the ONLY difference
/// between this fold and the real one is the defect.
let private closureInvoking: Fold =
    fun arm nodeId action s placement ->
        match action with
        | Action.Call(_, Some onResult, None) ->
            let produced = onResult (o "the handler's answer")

            { Store =
                { s with
                    State = Map.add "invoked" (Unchecked.nonNull produced) s.State }
              Effects = []
              Diagnostics = []
              Halted = false },
            placement
        | _ -> BoundedActions.runBoundedActionWith arm nodeId action s placement

// ─── The tests ──────────────────────────────────────────────────────────────

// ============================================================================
//  The UI witness's flow shapes (Phase 1976).
//
//  Until fuaran#2012 this test sat in a list beside the GENERIC fold's
//  comparison at the toy witness. That comparison is about the core, and it is
//  hosted where the core is (the program repository's toy-witness bounded-fold
//  list); what stays here is the one claim in it that is about the UI witness.
// ============================================================================

let private uiFlowShapeTests =
    testList
        "the UI witness's flow shapes (Phase 1976)"
        [ test "the UI witness views no action as a branch or a repeat, at any depth (Phase 1976)" {
              // The UI tier branches in the TREE and repeats through data
              // binding, so its handlers are straight-line: `ui_view_no_flow`
              // in the model, checked here over the arm-complete corpus. If a
              // UI handler ever genuinely needed either shape, the finding
              // would land here first, as a failure of this test.
              let view = UiWitness.witness.Dispatch.Action.View

              let rec flowFree (action: Action<obj>) : bool =
                  match view action with
                  | ActionView.Choose _
                  | ActionView.Repeat _
                  | ActionView.Each _ -> false
                  | ActionView.Sequence members -> members |> List.forall flowFree
                  | ActionView.Assign _
                  | ActionView.Call _
                  | ActionView.Require _
                  | ActionView.Leaf _ -> true

              for (label, _, action) in armCompleteCases do
                  Expect.isTrue (flowFree action) (sprintf "%s views as neither shape" label)

              Expect.isTrue
                  (flowFree (Action.Chain [ Action.Chain [ Action.Print ]; Action.Print ]))
                  "nor does a nested chain"
          } ]

[<Tests>]
let tests =
    // fuaran#2011 — a run that selects the toy family alone leaves the UI
    // family's corpus unread, and its two cases say so by name.
    let uiSelected = FixtureIo.selected FixtureIo.fixturesRoot
    let corpus = if uiSelected then corpusCases () else []

    testList
        "Phase 1715 - the proved bounded fold as oracle"
        [ test "the driver-semantics family yields actions to compare" {
              if not uiSelected then
                  skiptestf "%s is not selected by %s" FixtureIo.scenarioFamily FixtureIo.SelectionVariable

              // A corpus that silently resolved to nothing would report the
              // same green as one that ran every scenario. The floor is the
              // number of scenarios the family declares, since every scenario
              // scripts at least one event.
              let declared = FixtureIo.scenarios FixtureIo.fixturesRoot

              Expect.isNonEmpty
                  declared
                  $"the corpus enumerates no driver-semantics scenario under {FixtureIo.fixturesRoot}"

              Expect.isGreaterThanOrEqual
                  corpus.Length
                  declared.Length
                  "every declared scenario contributed at least one action for the oracle to be compared over"
          }

          test "the oracle agrees with production over the driver-semantics family" {
              if not uiSelected then
                  skiptestf "%s is not selected by %s" FixtureIo.scenarioFamily FixtureIo.SelectionVariable

              let divergences = runScript production HandlerArm.inert corpus empty

              match divergences with
              | [] -> ()
              | first :: rest ->
                  failtestf
                      "the extracted model and production disagree on %d of %d corpus action(s). First divergence:\n%s"
                      (List.length rest + 1)
                      corpus.Length
                      first
          }

          test "the oracle agrees with production over every arm and every refusal path" {
              let cases = armCompleteCases @ [ dispatchCase ]
              let divergences = runScript production HandlerArm.inert cases seeded

              // The union has fourteen arms and this corpus must name all of
              // them, or the "arm-complete" claim in its name is false. Counted
              // rather than asserted in prose: a case deleted in a later edit
              // would otherwise leave the name standing over a corpus that no
              // longer earns it.
              let armsCovered =
                  cases
                  |> List.map (fun (_, _, a) -> Fuaran.UI.Ops.ActionInvocation.ActionInvocation.describe a)
                  |> List.map (fun name ->
                      match name.IndexOf '(' with
                      | -1 -> name
                      | i -> name.Substring(0, i))
                  |> List.distinct

              Expect.equal
                  armsCovered.Length
                  14
                  $"every arm of the closed action union is exercised (covered: %A{List.sort armsCovered})"

              match divergences with
              | [] -> ()
              | first :: rest ->
                  failtestf
                      "the extracted model and production disagree on %d of %d arm-complete case(s). First divergence:\n%s"
                      (List.length rest + 1)
                      cases.Length
                      first
          }

          test "the oracle agrees with production when the placement ANSWERS a call" {
              let divergences = runScript production answeringArm answeredCases seeded

              match divergences with
              | [] -> ()
              | first :: _ ->
                  failtestf "the extracted model and production disagree on the answered-call seam:\n%s" first
          }

          test "GO RED: a fold that invokes a carried closure loses the comparison" {
              // The oracle CANNOT invoke a closure — the model's closure slots
              // have an abstract type with no elimination form, which is what
              // `run_no_closure` turns into a theorem. So a fold that does
              // invoke one must diverge from it, and if it does not, this whole
              // file is comparing nothing.
              let carrying =
                  [ ("go-red", "n1", Action.Call("/api/x", Some(fun _ -> o "the closure ran"), None)) ]

              let honest = runScript production HandlerArm.inert carrying seeded

              Expect.isEmpty honest "production agrees with the oracle on the very case the defect is committed against"

              let defective = runScript closureInvoking HandlerArm.inert carrying seeded

              Expect.isNonEmpty
                  defective
                  "a fold that invokes the closure a Call carries MUST diverge from the proved model — a comparison that cannot lose is not evidence"
          }

          uiFlowShapeTests ]

// ============================================================================
//  Phase 1716 — the differential host for the proved interaction budget.
//
//  `proofs/Budget.fst` is a model of `Budget.actionCascadeCost`,
//  `Budget.treeCost` and the G2 stage of `BoundedDriver.step`, and five
//  theorems about them. Everything the header of the 1715 host above says
//  about what a differential is for applies here unchanged; what differs is
//  what is compared and what the corpora are.
//
//  What is compared is an INTEGER, and that is a stronger comparison than it
//  looks. Above the ceiling `treeCost` stops walking, so what it returns
//  depends on which nodes it happened to visit first — which means the model
//  has to keep production's explicit stack, in production's push order, or the
//  two agree only on the verdict and not on the number. Comparing the number
//  is what says the model kept it.
//
//  The three corpora answer different questions.
//
//    * The trees the bounded driver's own suite drives (`BoundedDriverTests`)
//      are the shapes the loop is known to be exercised on. They answer "does
//      the model agree on the documents that already exist".
//    * The generated trees are run at every ceiling AROUND their own exact
//      cost — below it, at it, one either side — because the interesting
//      behaviour of this function is entirely at that boundary and no authored
//      tree lands there by accident.
//    * The G2 corpus runs the gate itself: `BoundedDriver.step` against the
//      model's `step`, comparing the refusal's reason VERBATIM and requiring
//      the session the driver hands back to be the one it was given.
//
//  And the go-red case is what says the comparison can lose. It commits a walk
//  that accumulates with .NET's ordinary wrapping `+` instead of the
//  saturating add — the one defect `Budget.fs`'s own comment names, where a
//  sum that wrapped negative "would read as cheap and admit the very tree the
//  budget exists to refuse" — and requires the ceiling comparison to come out
//  the wrong way. Without it, a comparison that silently agreed with
//  everything would report the same green.
// ============================================================================

// ─── Translation: production ⇄ the model ────────────────────────────────────
//
// The model takes the saturation bound as a parameter and owns the counting
// cap; production hardwires the first and keeps the second private. So the
// bound is supplied here from `Int32.MaxValue` — the value production's
// `satAdd` / `satMul` compare against — and the cap is read back OUT of the
// extraction, which is what makes a divergence from production's own private
// literal something this host reports rather than something it papers over.

let private satBound: bigint = bigint System.Int32.MaxValue

let private countedCap: bigint = ProvedBudget.max_counted_rows

let private rowCap: int = int countedCap

/// F#: `Budget.staticSeqCount`'s projection of a `Binding`. The payload
/// reaches the model as a list of units: only its LENGTH is ever read, and
/// truncating at the model's own cap is what makes a lazy — or endless —
/// production payload expressible as the finite list the assumed rung says it
/// is modelled by.
let private seqPayload (binding: Binding<'t seq>) : ProvedBudget.payload =
    match binding with
    | Binding.Static(Some items) -> ProvedBudget.PRows(items |> Seq.truncate rowCap |> Seq.map ignore |> List.ofSeq)
    | _ -> ProvedBudget.PAbsent

/// F#: `Budget.staticListCount`'s projection of the same.
let private listPayload (binding: Binding<'t list>) : ProvedBudget.payload =
    match binding with
    | Binding.Static(Some items) -> ProvedBudget.PRows(items |> List.truncate rowCap |> List.map ignore)
    | _ -> ProvedBudget.PAbsent

/// Which of `nodeCost`'s four data-bearing arms a kind is, and the two numbers
/// that arm reads. This is the one place the translation DECIDES anything, and
/// it decides it because the model cannot: `NodeKind` has scores of arms and a
/// model that carried them would be modelling the tree vocabulary. The
/// consequence is worth stating plainly — a production change that started
/// weighting a fifth kind would be invisible to this host until this match
/// gained the kind too, whereas a change to the ARITHMETIC or to the WALK is
/// caught, and those are what the theorems are about.
let private modelShape (kind: NodeKind<obj>) : ProvedBudget.cost_shape =
    match kind with
    | NodeKind.Chart spec -> ProvedBudget.SWeighted(seqPayload spec.Source, bigint (List.length spec.YFields))
    | NodeKind.DataGrid spec -> ProvedBudget.SWeighted(seqPayload spec.Source, bigint (List.length spec.Columns))
    | NodeKind.Map spec -> ProvedBudget.SRows(listPayload spec.Source)
    | NodeKind.Sparkline spec -> ProvedBudget.SRows(listPayload spec.Source)
    | _ -> ProvedBudget.SPlain

/// The children are production's own — `Introspect.getChildren`, the same
/// function `treeCost` walks with — so the model and production never disagree
/// about the SHAPE of the tree, only ever about what the walk does with it.
let rec private modelNode (node: Node<obj>) : ProvedBudget.nd =
    ProvedBudget.Nd(
        modelShape node.Kind,
        (match Introspect.getChildren node.Kind with
         | Some kids -> kids |> List.map modelNode
         | None -> [])
    )

/// F#: `Action<'Msg>`, projected onto the one distinction
/// `actionCascadeCost` makes. The wildcard here is production's wildcard: the
/// fourteen arms `BoundedFold.fst` carries one for one are not this model's
/// subject, because the function being modelled cannot tell them apart.
let rec private modelCascade (a: Action<obj>) : ProvedBudget.act =
    match a with
    | Action.Chain ops -> ProvedBudget.AChain(ops |> List.map modelCascade)
    | _ -> ProvedBudget.ALeaf

// ─── The comparison ─────────────────────────────────────────────────────────

/// The cost function under comparison. Production is one; the go-red case
/// below is another, and it is deliberately wrong.
type private TreeCost = int -> Node<obj> -> int

let private productionCost: TreeCost = Budget.treeCost

let private oracleTreeCost (ceiling: int) (node: Node<obj>) : int =
    int (ProvedBudget.tree_cost satBound countedCap (bigint ceiling) (modelNode node))

let private oracleCascadeCost (action: Action<obj>) : int =
    int (ProvedBudget.action_cascade_cost (modelCascade action))

let private costDivergence (cost: TreeCost) (where: string) (ceiling: int) (node: Node<obj>) : string option =
    let prod = cost ceiling node
    let oracle = oracleTreeCost ceiling node

    if prod <> oracle then
        Some(sprintf "%s at ceiling %d\n  production: %d\n  oracle:     %d" where ceiling prod oracle)
    else
        None

/// The exact cost, which is what a ceiling is chosen AROUND below. Priced at
/// the saturation bound, where the walk cannot stop early — so this is the
/// number `treecost_exact_below_ceiling` says a within-budget answer equals.
let private exactCost (node: Node<obj>) : int =
    Budget.treeCost System.Int32.MaxValue node

// ─── Corpus A: the trees the bounded driver's own suite drives ──────────────

let private stubRender (n: Node<obj>) : string = "<f id='" + n.Id + "'/>"

let private boundMarkdown (id: string) (key: string) : Node<obj> =
    let n = Fuaran.markdown id "placeholder"

    { n with
        Kind = NodeKind.Markdown({ Text = TextSource.Bound(Binding.State(key, Some "init")) }) }

let private container (id: string) (children: Node<obj> list) : Node<obj> =
    Fuaran.dashboard
        id
        { Defaults.dashboard<obj> with
            Children = children }

/// `BoundedDriverTests`' own fixture: a dashboard carrying a button and a
/// state-bound Markdown. Rebuilt here rather than imported because that suite
/// keeps its builders private — which is also why the SHAPE is what is copied
/// and not the code.
let private driverFixture (onClick: Action<obj>) : Node<obj> =
    container
        "root"
        [ Fuaran.button
              "set"
              { Defaults.button<obj> with
                  OnClick = onClick }
          boundMarkdown "count" "msg" ]

// ─── Corpus B: generated trees, and the data-bearing kinds ──────────────────

let private chartNode (id: string) (rows: int) (fields: int) : Node<obj> =
    let n = Fuaran.markdown id "x"

    { n with
        Kind =
            NodeKind.Chart(
                { Defaults.chart<obj> with
                    Source = Binding.Static(Some(Seq.replicate rows (Map.empty: Row)))
                    YFields = List.replicate fields "y" }
            ) }

/// A grid with rows and NO declared columns — the `max 1 (List.length ...)`
/// clause, which is the only place the weighted arms can be asked to multiply
/// by nothing.
let private gridNode (id: string) (rows: int) : Node<obj> =
    let n =
        Fuaran.table
            id
            { Defaults.table<obj> with
                Headers = []
                Rows = [] }

    match n.Kind with
    | NodeKind.DataGrid spec ->
        { n with
            Kind =
                NodeKind.DataGrid(
                    { spec with
                        Source = Binding.Static(Some(Seq.replicate rows (Map.empty: Row))) }
                ) }
    | other -> failwithf "Fuaran.table no longer builds a DataGrid; it builds %A" other

let private sparklineNode (id: string) (points: int) : Node<obj> =
    Fuaran.sparkline
        id
        { Defaults.sparkline with
            Source = Binding.Static(Some(List.replicate points 1.0)) }

let private mapNode (id: string) (markers: int) : Node<obj> =
    Fuaran.map
        id
        { Defaults.map<obj> with
            Source =
                Binding.Static(
                    Some(
                        List.replicate
                            markers
                            { Label = "m"
                              Latitude = 0.0
                              Longitude = 0.0 }
                    )
                ) }

/// A chain of single-child containers, `depth` deep. The stack walk visits it
/// one node at a time, so it is the shape that distinguishes an iterative
/// walk's order from a recursive one's.
let rec private deepChain (depth: int) : Node<obj> =
    if depth <= 1 then
        Fuaran.markdown "leaf" "x"
    else
        container ("d" + string depth) [ deepChain (depth - 1) ]

let private generatedTrees: (string * Node<obj>) list =
    [ "a bare leaf", Fuaran.markdown "m" "x"
      "an empty container", container "root" []
      "a fan of seven leaves", container "root" (List.init 7 (fun i -> Fuaran.markdown ("m" + string i) "x"))
      "a chain twelve deep", deepChain 12
      "a fan of fans",
      container
          "root"
          (List.init 3 (fun i ->
              container ("c" + string i) (List.init 4 (fun j -> Fuaran.markdown ("m" + string i + "-" + string j) "x"))))
      "an unbound chart (no static payload)", container "root" [ chartNode "ch" 0 3 ]
      "a chart, five rows by three fields", container "root" [ chartNode "ch" 5 3 ]
      "a chart with no declared fields", container "root" [ chartNode "ch" 5 0 ]
      "a grid with rows and no columns", container "root" [ gridNode "g" 9 ]
      "a sparkline of forty points", container "root" [ sparklineNode "s" 40 ]
      "a map of six markers", container "root" [ mapNode "mp" 6 ]
      "every data-bearing kind at once",
      container
          "root"
          [ chartNode "ch" 11 2
            gridNode "g" 7
            sparklineNode "s" 13
            mapNode "mp" 3
            Fuaran.markdown "m" "x" ]
      "a payload AT the counting cap", container "root" [ sparklineNode "s" rowCap ]
      "a payload PAST the counting cap", container "root" [ sparklineNode "s" (rowCap + 1) ]
      "a chart whose multiply saturates", container "root" [ chartNode "ch" rowCap 30000 ] ]

/// Every ceiling worth asking about for one tree: the degenerate ones, the
/// exact cost, and one either side of it. The boundary is where the early stop
/// lives, so a corpus that never lands on it is a corpus that never tests it.
let private ceilingsAround (exact: int) : int list =
    [ 0
      1
      2
      exact - 2
      exact - 1
      exact
      exact + 1
      exact + 2
      System.Int32.MaxValue ]
    |> List.filter (fun c -> c >= 0)
    |> List.distinct

// ─── Corpus C: the cascade cost ─────────────────────────────────────────────

let private cascadeCases: (string * Action<obj>) list =
    let leaf = Action.Print

    let rec nest depth =
        if depth <= 0 then
            leaf
        else
            Action.Chain [ nest (depth - 1); leaf ]

    [ "a leaf", leaf
      "an empty chain", Action.Chain []
      "a flat chain of three",
      Action.Chain [ Action.Print; Action.Focus "n"; Action.SetState("k", Some(JStr "v"), None) ]
      "a chain of empty chains", Action.Chain [ Action.Chain []; Action.Chain [] ]
      "a chain nesting a chain", Action.Chain [ Action.Chain [ Action.Print; Action.Print ]; Action.Print ]
      "a chain nested twenty deep", nest 20
      "a wide flat chain", Action.Chain(List.replicate 70 leaf) ]

// ─── The G2 gate ────────────────────────────────────────────────────────────

let private clickEvent: LiveEvent =
    { ConnId = "proof-budget"
      NodeId = "set"
      Event = "click"
      Payload = Map.empty
      LastSeq = 0 }

/// The model's admitted branch. It is never expected to run on a breached step
/// — that is half of what `breach_pure` says — so it returns a sentinel the
/// comparison reads as "the gate let this through", which is the only thing
/// about the admitted case this host compares. What the driver DOES with an
/// admitted event is Phase 1715's subject and the renderer's.
let private admittedBranch: ProvedBudget.admitted_branch<obj> =
    { run =
        fun sess _ _ ->
            sess,
            ({ so_patches = bigint 0
               so_effects = bigint 0
               so_rejected = ProvedBudget.ONone }
            : ProvedBudget.step_output) }

/// One event through the driver and through the model's gate, comparing the
/// verdict, the refusal's reason VERBATIM, and — on a breach — that the
/// session came back untouched.
let private gateDivergence (where: string) (budget: InteractionBudget) (onClick: Action<obj>) : string option =
    let services =
        { BoundedDriver.BoundedServices.createPermissive stubRender with
            Budget = budget }

    let session =
        BoundedDriver.init services empty (WireTree.ofDecoded (driverFixture onClick))

    let resolved =
        match Validation.validate (fun _ -> true) session.Resolved clickEvent with
        | Ok validated -> validated.Action
        | Error reason -> failwithf "%s: the fixture's click did not validate: %A" where reason

    match resolved with
    | None -> Some(sprintf "%s: the fixture's click resolved to no action, so the gate was never asked" where)
    | Some action ->
        let after, output = BoundedDriver.step session clickEvent

        let modelSession: ProvedBudget.session<obj> =
            { se_store = o ()
              se_node_count = bigint session.NodeCount }

        let modelBudget: ProvedBudget.budget =
            { b_max_actions = bigint budget.MaxActions
              b_max_nodes = bigint budget.MaxNodes }

        let modelAfter, modelOutput =
            ProvedBudget.step modelBudget admittedBranch modelSession clickEvent.NodeId (modelCascade action)

        let prodRefusal =
            match output.Rejected with
            | Some(BoundedDriver.BudgetExceeded reason) -> Some reason
            | Some other -> Some(sprintf "<not a budget refusal: %A>" other)
            | None -> None

        let modelRefusal =
            match modelOutput.so_rejected with
            | ProvedBudget.OSome reason -> Some reason
            | ProvedBudget.ONone -> None

        if prodRefusal <> modelRefusal then
            Some(sprintf "%s: REFUSAL\n  production: %A\n  oracle:     %A" where prodRefusal modelRefusal)
        elif
            prodRefusal.IsSome
            && not (List.isEmpty output.Patches && List.isEmpty output.Effects)
        then
            Some(
                sprintf
                    "%s: a breached step emitted %d patch(es) and %d effect(s); breach_pure says neither"
                    where
                    (List.length output.Patches)
                    (List.length output.Effects)
            )
        elif prodRefusal.IsSome && not (obj.ReferenceEquals(after, session)) then
            // REFERENCE equality, which is `breach_pure`'s `sess' == sess`
            // literally: the theorem does not say the driver hands back an
            // equal session, it says it hands back THE session. A structural
            // comparison would also be unavailable here — `BoundedStore`
            // carries `obj` payloads and so supports no equality constraint —
            // but that is a second reason and not the first one.
            Some(sprintf "%s: a breached step changed the session; breach_pure says it is handed back untouched" where)
        elif prodRefusal.IsSome && modelAfter.se_node_count <> modelSession.se_node_count then
            Some(sprintf "%s: the MODEL's breached step changed its session, which breach_pure forbids" where)
        else
            None

// ─── The go-red case ────────────────────────────────────────────────────────

/// The proved walk with ONE line changed: the accumulator adds with .NET's
/// ordinary `int`, which wraps, instead of with the saturating add. Everything
/// else — the node cost, the push order, the guards — is the oracle's own,
/// called by name, so the only difference between this walk and the proved one
/// is the defect.
///
/// It is committed on purpose. `Budget.fs` says an overflow that wrapped
/// negative "would read as cheap and admit the very tree the budget exists to
/// refuse"; a differential that cannot be made to say so is not evidence of
/// anything.
let rec private wrappingWalk (ceiling: int) (pending: ProvedBudget.nd list) (running: int) : int =
    if running > ceiling then
        running
    else
        match pending with
        | [] -> running
        | cur :: rest ->
            wrappingWalk
                ceiling
                (ProvedBudget.push_all (ProvedBudget.kids cur) rest)
                (running + int (ProvedBudget.node_cost satBound countedCap cur))

let private wrappingTreeCost (ceiling: int) (node: Node<obj>) : int =
    wrappingWalk ceiling [ modelNode node ] 0

/// Two charts whose costs sum past `Int32.MaxValue` while each sits inside it,
/// under a ceiling both of them individually clear. The saturating add answers
/// the bound and the tree is refused; a wrapping add answers a negative number
/// and the tree is admitted.
let private overflowingTree: Node<obj> =
    let fields = List.replicate 400_000 "y"

    let chart (id: string) : Node<obj> =
        let n = Fuaran.markdown id "x"

        { n with
            Kind =
                NodeKind.Chart(
                    { Defaults.chart<obj> with
                        Source = Binding.Static(Some(Seq.replicate 4_000 (Map.empty: Row)))
                        YFields = fields }
                ) }

    container "root" [ chart "a"; chart "b" ]

let private overflowCeiling = 2_000_000_000

// ─── The tests ──────────────────────────────────────────────────────────────

[<Tests>]
let budgetTests =
    testList
        "Phase 1716 - the proved budget as oracle"
        [ test "the generated corpus straddles the ceiling in both directions" {
              // A corpus every ceiling admitted — or every ceiling refused —
              // would report the same green while testing none of the
              // behaviour that lives at the boundary. The floor is that both
              // answers occur, and that the corpus prices something at all.
              let verdicts =
                  generatedTrees
                  |> List.collect (fun (_, tree) ->
                      let exact = exactCost tree

                      ceilingsAround exact
                      |> List.map (fun ceiling -> productionCost ceiling tree > ceiling))

              Expect.isNonEmpty generatedTrees "the generated corpus is empty"

              Expect.isTrue
                  (List.contains true verdicts)
                  "no generated tree is over its ceiling at any of the ceilings tried, so the refusal path is never priced"

              Expect.isTrue
                  (List.contains false verdicts)
                  "no generated tree is within its ceiling at any of the ceilings tried, so the exact path is never priced"

              // `treecost_exact_below_ceiling` is a statement about a number,
              // not a verdict, so the corpus must contain a tree whose cost is
              // bigger than one. A corpus of leaves would satisfy every
              // comparison below while exercising no arithmetic at all.
              Expect.isTrue
                  (generatedTrees |> List.exists (fun (_, tree) -> exactCost tree > 1))
                  "every generated tree costs one, so nothing here prices a subtree or a payload"
          }

          test "the oracle agrees with production on the bounded driver's own trees" {
              let trees =
                  [ "the driver fixture", driverFixture (Action.SetState("msg", Some(JStr "x"), None))
                    "the driver fixture with a chained click",
                    driverFixture (Action.Chain [ Action.Print; Action.SetState("msg", Some(JStr "x"), None) ])
                    "the driver fixture with no click", driverFixture (Action.Chain []) ]

              let divergences =
                  trees
                  |> List.collect (fun (name, tree) ->
                      ceilingsAround (exactCost tree)
                      |> List.choose (fun ceiling -> costDivergence productionCost name ceiling tree))

              match divergences with
              | [] -> ()
              | first :: rest ->
                  failtestf
                      "the extracted model and production disagree on %d priced tree(s). First divergence:\n%s"
                      (List.length rest + 1)
                      first
          }

          test "the oracle agrees with production over generated trees at every ceiling around their exact cost" {
              let priced =
                  generatedTrees
                  |> List.collect (fun (name, tree) ->
                      ceilingsAround (exactCost tree) |> List.map (fun ceiling -> name, ceiling, tree))

              let divergences =
                  priced
                  |> List.choose (fun (name, ceiling, tree) -> costDivergence productionCost name ceiling tree)

              match divergences with
              | [] -> ()
              | first :: rest ->
                  failtestf
                      "the extracted model and production disagree on %d of %d (tree, ceiling) pair(s). First divergence:\n%s"
                      (List.length rest + 1)
                      priced.Length
                      first
          }

          test "the oracle agrees with production on the action cascade cost" {
              let divergences =
                  cascadeCases
                  |> List.choose (fun (name, action) ->
                      let prod = Budget.actionCascadeCost action
                      let oracle = oracleCascadeCost action

                      if prod <> oracle then
                          Some(sprintf "%s\n  production: %d\n  oracle:     %d" name prod oracle)
                      else
                          None)

              match divergences with
              | [] -> ()
              | first :: _ -> failtestf "the extracted model and production disagree on a cascade cost:\n%s" first
          }

          test "the oracle agrees with the driver's G2 gate, and a breach mutates nothing" {
              let overActions =
                  Action.Chain(List.replicate (InteractionBudget.defaults.MaxActions + 1) Action.Print)

              let withinActions = Action.SetState("msg", Some(JStr "x"), None)

              let cases =
                  [ "a cascade over MaxActions", InteractionBudget.defaults, overActions
                    "a cascade at MaxActions",
                    InteractionBudget.defaults,
                    Action.Chain(List.replicate InteractionBudget.defaults.MaxActions Action.Print)
                    "a tree over MaxNodes",
                    { InteractionBudget.defaults with
                        MaxNodes = 1 },
                    withinActions
                    "a tree at MaxNodes",
                    { InteractionBudget.defaults with
                        MaxNodes = 3 },
                    withinActions
                    "both caps breached at once",
                    { InteractionBudget.defaults with
                        MaxNodes = 1 },
                    overActions
                    "nothing breached", InteractionBudget.defaults, withinActions ]

              // The gate is only being compared if it actually refused
              // something: a run in which every case was admitted would agree
              // with the model for a reason that has nothing to do with the
              // budget.
              let refusedSomething =
                  let services =
                      { BoundedDriver.BoundedServices.createPermissive stubRender with
                          Budget = InteractionBudget.defaults }

                  let session =
                      BoundedDriver.init services empty (WireTree.ofDecoded (driverFixture overActions))

                  (snd (BoundedDriver.step session clickEvent)).Rejected

              Expect.isSome refusedSomething "the G2 corpus refused nothing, so the gate was never compared on a breach"

              let divergences =
                  cases
                  |> List.choose (fun (name, budget, onClick) -> gateDivergence name budget onClick)

              match divergences with
              | [] -> ()
              | first :: rest ->
                  failtestf
                      "the extracted model and production disagree on %d gate case(s). First divergence:\n%s"
                      (List.length rest + 1)
                      first
          }

          test "GO RED: an accumulator that wraps loses the ceiling comparison" {
              // The honest run first: the defect is committed against a tree
              // production and the oracle agree on, so what the wrapping walk
              // loses is the defect and not the fixture.
              Expect.isNone
                  (costDivergence productionCost "the overflowing tree" overflowCeiling overflowingTree)
                  "production disagrees with the oracle on the very tree the defect is committed against"

              // Then the defect, through the SAME comparison the cases above
              // run on. Comparing the two integers directly would show that
              // they differ; putting the defective walk where production sits
              // shows that the harness REPORTS it, which is the thing every
              // green above rests on.
              Expect.isSome
                  (costDivergence wrappingTreeCost "the overflowing tree" overflowCeiling overflowingTree)
                  "the comparison harness did not report a walk whose accumulator wraps — a harness that cannot lose is not evidence"

              let proved = oracleTreeCost overflowCeiling overflowingTree
              let wrapped = wrappingTreeCost overflowCeiling overflowingTree

              Expect.notEqual
                  wrapped
                  proved
                  "a walk whose accumulator wraps MUST diverge from the proved model — a comparison that cannot lose is not evidence"

              // And the divergence is the one that matters. Not "a different
              // number" — the ceiling comparison coming out the other way,
              // which is the tree being ADMITTED rather than refused.
              Expect.isTrue
                  (proved > overflowCeiling)
                  (sprintf
                      "the proved cost %d should be over the ceiling %d; the fixture no longer overflows"
                      proved
                      overflowCeiling)

              Expect.isTrue
                  (wrapped <= overflowCeiling)
                  (sprintf
                      "the wrapping cost %d should read as WITHIN the ceiling %d — that is the whole defect"
                      wrapped
                      overflowCeiling)
          } ]
