module Fuaran.UI.ServerDriven.Tests.DriverTests

// Phase 1152 — `Action.Dispatch` carries the IDL's `inProcessOnly` marking, which
// the generator renders as `[<Obsolete(…, false)>]`: FS0044 at every mention, and
// an error under this repo's `TreatWarningsAsErrors`. File-scoped rather than
// per-declaration because the mentions sit INSIDE `testList` expressions, where a
// lexical directive cannot be placed — this is the tightest form the file can
// express. A suite is not an authoring surface: these uses exist to PIN the marked
// case's behaviour, which is the one use the marking is not addressed to.
#nowarn "44"

// ─── Phase 152 Track C: the per-connection driver ──────────────
//
// End-to-end through the server-side Elmish loop: an inbound LiveEvent is
// validated (G1), its action interpreted, update run, the tree re-rendered,
// diffed and lowered to DomPatches. A counter app (model = int) is the
// fixture; a stub renderer stands in for the host's Render.render so the
// tests assert the op→patch mapping without a real HTML renderer.

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation
open Fuaran.UI.ServerDriven.Driver

type Model = int

type Msg =
    | Inc
    | Noop

let private update (msg: Msg) (m: Model) : Model =
    match msg with
    | Inc -> m + 1
    | Noop -> m

/// A counter: a button that increments, a markdown showing the count (the node
/// that changes on each step), and a button that navigates (client-only arm).
let private view (m: Model) : Node<Msg> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<Msg> with
            Children =
                [ Fuaran.button
                      "inc"
                      { Defaults.button<Msg> with
                          OnClick = Action.Dispatch Inc }
                  Fuaran.markdown "count" (string m)
                  Fuaran.button
                      "nav"
                      { Defaults.button<Msg> with
                          OnClick = Action.Navigate(TextSource.Literal "/next", NavigateTarget.Self) } ] }

let private stubRender (n: Node<Msg>) : string =
    let s = n.Id
    $"<f id='{s}'/>"

let private ev nodeId event =
    { ConnId = "c1"
      NodeId = nodeId
      Event = event
      Payload = Map.empty
      LastSeq = 0 }

let private session () =
    init (DriverServices.createPermissive stubRender) update view 0

[<Tests>]
let tests =
    testList
        "Driver (per-connection server loop)"
        [ test "a button click runs update and patches the changed node" {
              let s2, out = step (session ()) (ev "inc" "click")

              Expect.equal s2.Model 1 "model incremented"
              Expect.isNone out.Rejected "not rejected"
              // Only the count markdown changed → one targeted ReplaceFragment.
              Expect.equal
                  out.Patches
                  [ DomPatch.ReplaceFragment("count", "<f id='count'/>") ]
                  "one targeted patch for the changed leaf"

              Expect.isEmpty out.Effects "no client effects"
          }

          test "stepping twice accumulates model state across the connection" {
              let s1, _ = step (session ()) (ev "inc" "click")
              let s2, _ = step s1 (ev "inc" "click")
              Expect.equal s2.Model 2 "state persists in the session"
          }

          test "a client-only arm lowers to a ClientEffect with no model change" {
              let s2, out = step (session ()) (ev "nav" "click")

              Expect.equal s2.Model 0 "navigate does not touch the model"

              Expect.equal
                  out.Effects
                  [ ClientEffect.Navigate("/next", NavigateTarget.Self) ]
                  "navigate lowered to a ClientEffect"

              Expect.isEmpty out.Patches "no tree change → no patches"
          }

          // ─── Phase 782 — the gate default inverted on this path too ────────
          //
          // `DriverServices.create` returned `CanDispatch = fun _ -> true` until
          // Phase 782, so an unconfigured server-driven host validated every
          // inbound LiveEvent against a policy that permitted everything. Every
          // OTHER test in this file now names `createPermissive` deliberately;
          // this one pins what plain `create` does.
          test "DriverServices.create DENIES by default; createPermissive is the named opt-in" {
              let defaultServices: DriverServices<Msg> = DriverServices.create stubRender
              Expect.isFalse (defaultServices.CanDispatch(Action.Dispatch Inc)) "the default gate refuses"

              let permissiveServices: DriverServices<Msg> =
                  DriverServices.createPermissive stubRender

              Expect.isTrue (permissiveServices.CanDispatch(Action.Dispatch Inc)) "the named opt-in allows"

              // …and the refusal is a real refusal end-to-end, not just a flag.
              let s0 = init defaultServices update view 0
              let s2, out = step s0 (ev "inc" "click")
              Expect.equal s2.Model 0 "an unconfigured host mutates nothing"

              match out.Rejected with
              | Some(RejectReason.DispatchDenied("inc", _)) -> ()
              | other -> failtestf "expected DispatchDenied from the default gate, got %A" other
          }

          test "a denied action leaves the session unchanged (default-deny)" {
              let denyServices =
                  { DriverServices.createPermissive stubRender with
                      CanDispatch = fun _ -> false }

              let s0 = init denyServices update view 0
              let s2, out = step s0 (ev "inc" "click")

              Expect.equal s2.Model 0 "no state mutation on reject"
              Expect.isEmpty out.Patches "no patches on reject"

              match out.Rejected with
              | Some(RejectReason.DispatchDenied("inc", _)) -> ()
              | other -> failtestf "expected DispatchDenied, got %A" other
          }

          test "an unknown node is rejected with no state change" {
              let s2, out = step (session ()) (ev "ghost" "click")
              Expect.equal s2.Model 0 "unchanged"

              match out.Rejected with
              | Some(RejectReason.UnknownNode "ghost") -> ()
              | other -> failtestf "expected UnknownNode, got %A" other
          }

          test "OnApply receives the ops applied this step (FGP 5)" {
              let captured = ResizeArray<TreeOp<Msg>>()

              let services =
                  { DriverServices.createPermissive stubRender with
                      OnApply = fun ops -> captured.AddRange ops }

              let s0 = init services update view 0
              let _, _ = step s0 (ev "inc" "click")

              Expect.isNonEmpty (List.ofSeq captured) "ops were emitted to the sink hook"
          } ]

// ─── Phase 2523: query-bound sources re-resolve on a bounded action ─────────
//
// A stored page has no hand-authored `update`: its selectors are `SetState`
// actions and declarative filter chips. Three metrics read three sources; `day`
// feeds two of them (`a`, `b`) and `site` feeds the third (`c`).

let private nn (x: float) : obj = Unchecked.nonNull (box x)

type private PageModel = { Sources: BindingSources }

type private PageMsg =
    | Resolved of SourceOutcome list
    | Idle

let private pageDependents =
    Map.ofList [ "day", Set.ofList [ "a"; "b" ]; "site", Set.ofList [ "c" ] ]

let private pageSources =
    { BindingSources.empty with
        QueryResults = Map.ofList [ "a", nn 1.0; "b", nn 2.0; "c", nn 3.0 ]
        ParameterDependents = pageDependents }

let private metricOf (sources: BindingSources) (name: string) : Node<PageMsg> =
    let binding = Binding.Query(name, (fun (o: obj) -> unbox<float> o), None)

    // The label shows what the source resolves to, so a re-resolution (or a
    // refusal) is a visible, patchable change to exactly this node.
    let label =
        match Fuaran.UI.Renderer.BindingResolver.resolve sources binding with
        | Fuaran.UI.Renderer.BindingResolver.Resolution.Resolved v -> $"{name}={v}"
        | Fuaran.UI.Renderer.BindingResolver.Resolution.Errored m -> $"{name}!{m}"
        | _ -> $"{name}?"

    Fuaran.metric
        name
        { Defaults.metric with
            Label = TextSource.Literal label
            Value = binding }

let private pageView (m: PageModel) : Node<PageMsg> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<PageMsg> with
            Children =
                [ Fuaran.button
                      "pick-day"
                      { Defaults.button<PageMsg> with
                          OnClick = Action.SetState("day", Some(Fuaran.Core.JStr "d2"), None) }
                  Fuaran.button
                      "pick-site"
                      { Defaults.button<PageMsg> with
                          OnClick = Action.SetState("site", Some(Fuaran.Core.JStr "s2"), None) }
                  Fuaran.button
                      "pick-other"
                      { Defaults.button<PageMsg> with
                          OnClick = Action.SetState("unrelated", Some(Fuaran.Core.JStr "x"), None) }
                  Fuaran.filters
                      "chips"
                      [ { Name = "day"
                          Label = TextSource.Literal "Day"
                          Kind = FormFieldKind.Text(Some(FieldValue.ofText (Binding.Static(Some ""))), None) } ]
                  metricOf m.Sources "a"
                  metricOf m.Sources "b"
                  metricOf m.Sources "c" ] }

let private pageUpdate (msg: PageMsg) (m: PageModel) : PageModel =
    match msg with
    | Resolved outcomes ->
        { m with
            Sources = BindingSources.applyOutcomes m.Sources outcomes }
    | Idle -> m

let private pageRender (n: Node<PageMsg>) : string =
    match n.Kind with
    | NodeKind.Metric spec ->
        match spec.Label with
        | TextSource.Literal t -> $"<m id=\"{n.Id}\">{t}</m>"
        | _ -> $"<m id=\"{n.Id}\"/>"
    | _ -> $"<f id=\"{n.Id}\"/>"

/// A page session whose resolver logs every source it is asked for and answers
/// per `answer`, and whose gate logs every action put to it.
let private pageSession
    (answer: string -> Result<ResolvedSource, string>)
    (asked: ResizeArray<string>)
    (gate: ResizeArray<string>)
    =
    let model = { Sources = pageSources }

    let reResolution: SourceReResolution<PageMsg> =
        { Sources = fun () -> model.Sources
          Resolve =
            fun _ name ->
                asked.Add name
                answer name
          Accept = fun outcomes -> Some(Resolved outcomes) }

    let services =
        { DriverServices.createPermissive pageRender with
            CanDispatch =
                fun a ->
                    gate.Add(Validation.describeAction a)
                    true
            ReResolution = Some reResolution }

    init services pageUpdate pageView model

let private fresh (v: float) name : Result<ResolvedSource, string> =
    Ok
        { Value = nn v
          Schema = Some [ Fuaran.Core.Field.create name Fuaran.Core.FloatType ] }

[<Tests>]
let reResolutionTests =
    testList
        "Driver (query-bound sources re-resolve, Phase 2523)"
        [ test "changing the day re-resolves exactly its two sources and patches only them" {
              let asked = ResizeArray()
              let s0 = pageSession (fun n -> fresh 10.0 n) asked (ResizeArray())
              let s1, out = step s0 (ev "pick-day" "click")

              Expect.equal (List.ofSeq asked) [ "a"; "b" ] "exactly the sources that depend on day"
              Expect.equal s1.Model.Sources.QueryResults["a"] (nn 10.0) "a is fresh"
              Expect.equal s1.Model.Sources.QueryResults["b"] (nn 10.0) "b is fresh"
              Expect.equal s1.Model.Sources.QueryResults["c"] (nn 3.0) "c is untouched"

              Expect.equal
                  out.Patches
                  [ DomPatch.ReplaceFragment("a", "<m id=\"a\">a=10</m>")
                    DomPatch.ReplaceFragment("b", "<m id=\"b\">b=10</m>") ]
                  "only the affected subtrees are re-lowered"
          }

          test "changing the site re-resolves only the one source that depends on it" {
              let asked = ResizeArray()
              let s0 = pageSession (fun n -> fresh 7.0 n) asked (ResizeArray())
              let _, out = step s0 (ev "pick-site" "click")

              Expect.equal (List.ofSeq asked) [ "c" ] "only c"
              Expect.equal (List.length out.Patches) 1 "one patch"
          }

          test "a parameter no source depends on re-resolves nothing" {
              let asked = ResizeArray()
              let s0 = pageSession (fun n -> fresh 7.0 n) asked (ResizeArray())
              let _, out = step s0 (ev "pick-other" "click")

              Expect.isEmpty asked "nothing was asked"
              Expect.isEmpty out.Patches "nothing changed"
          }

          test "a refusing resolver renders the source's named refusal, never the stale value" {
              let answer name =
                  if name = "a" then
                      Error "upstream said no"
                  else
                      fresh 10.0 name

              let s1, out =
                  step (pageSession answer (ResizeArray()) (ResizeArray())) (ev "pick-day" "click")

              match s1.Model.Sources.QueryResults["a"] with
              | :? SourceRefusal as r ->
                  Expect.equal
                      r
                      { Source = "a"
                        Reason = "upstream said no" }
                      "the refusal is stored in place of the value"
              | other -> failtestf "expected a SourceRefusal, got %A" other

              let patchA =
                  out.Patches
                  |> List.pick (function
                      | DomPatch.ReplaceFragment("a", html) -> Some html
                      | _ -> None)

              Expect.stringContains patchA "refused: upstream said no" "the block names the source's refusal"
              Expect.isFalse (patchA.Contains "a=1") "the stale value is not shown"
              Expect.equal s1.Model.Sources.QueryResults["b"] (nn 10.0) "the other dependent still refreshed"
          }

          test "a result that fails the typed check is refused, not rendered" {
              // A string column bound into a numeric Metric: FUARAN066.
              let wrong name : Result<ResolvedSource, string> =
                  Ok
                      { Value = nn 9.0
                        Schema = Some [ Fuaran.Core.Field.create name Fuaran.Core.StringType ] }

              let s1, _ =
                  step (pageSession wrong (ResizeArray()) (ResizeArray())) (ev "pick-day" "click")

              match s1.Model.Sources.QueryResults["a"] with
              | :? SourceRefusal -> ()
              | other -> failtestf "expected the typed check to refuse, got %A" other
          }

          test "CanDispatch is consulted exactly as before: once, for the action itself" {
              let gateWith = ResizeArray()
              let gateWithout = ResizeArray()
              let withRr = pageSession (fun n -> fresh 1.0 n) (ResizeArray()) gateWith

              let withoutRr =
                  let s = pageSession (fun n -> fresh 1.0 n) (ResizeArray()) gateWithout

                  { s with
                      Services = { s.Services with ReResolution = None } }

              step withRr (ev "pick-day" "click") |> ignore
              step withoutRr (ev "pick-day" "click") |> ignore

              Expect.equal (List.ofSeq gateWith) (List.ofSeq gateWithout) "the gate sees the same questions"
              Expect.equal gateWith.Count 1 "one question"
          }

          test "a declarative filter chip write re-resolves its dependents with no action dispatched" {
              let asked = ResizeArray()
              let gate = ResizeArray()
              let s0 = pageSession (fun n -> fresh 5.0 n) asked gate

              let chip =
                  { ev "chips" "change" with
                      Payload = Map.ofList [ "name", LiveValue.Str "day"; "value", LiveValue.Str "d9" ] }

              let _, out = step s0 chip

              Expect.equal (List.ofSeq asked) [ "a"; "b" ] "the chip names the day parameter"
              Expect.isNone out.Rejected "admitted"
              Expect.isEmpty gate "a chip has no action, so the gate is not asked"
          }

          test "a denied SetState re-resolves nothing" {
              let asked = ResizeArray()
              let s0 = pageSession (fun n -> fresh 1.0 n) asked (ResizeArray())

              let denied =
                  { s0 with
                      Services =
                          { s0.Services with
                              CanDispatch = fun _ -> false } }

              step denied (ev "pick-day" "click") |> ignore
              Expect.isEmpty asked "a refused action is never a re-resolution"
          } ]
