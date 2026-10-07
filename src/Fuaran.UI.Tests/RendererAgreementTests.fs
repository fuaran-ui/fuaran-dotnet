module Fuaran.UI.Tests.RendererAgreement

// ============================================================================
//  Phase 2041 — the two .NET renderers stop disagreeing.
//
//  The client renderer's half of the renderer-agreement fixtures; the server's
//  half is `Fuaran.UI.Renderer.Server.Tests/RendererAgreementTests.fs`, over the
//  same trees. Both halves assert against the ONE definition each renderer now
//  emits from — `FragmentExpansion.namespaceIds` and `RenderParity` in
//  `Fuaran.UI.Renderer.Core` — so a renderer that drifts from it fails its own
//  half, and the two cannot disagree while both pass.
//
//  Feliz's .NET-side `ReactElement` is opaque (the constraint ErrorBoundaryTests
//  and RenderEntrySeamTests document): building an element throws here, so the
//  emitted DOM cannot be read. What the client renderer does BEFORE that point
//  is observable, and every assertion below is against it — the node ids its
//  per-node guard reports, the correlation id it stamps, the warnings it
//  raises, the custom renderers and state-slot builders it calls, and the ones
//  it does not.
//
//  One constraint shapes every tree below: on .NET the first `prop.*` the
//  client evaluates throws, so a container (`Box`) fails before it renders a
//  child. The fixtures therefore descend through the kinds whose arms render a
//  child BEFORE building any element — `Switch`, `ErrorBoundary`, `FragmentRef`
//  and a chart's state slots — which reach the leaf a test is about.
//
//  The depth fixture is new behaviour on this tier (the client had no bound);
//  the others pin behaviour the client already had and the server now matches,
//  plus the shared definitions both tiers now read.
// ============================================================================

open System.Collections.Generic
open Expecto
open Feliz
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Runtime
open Fuaran.UI.Telemetry.Abstractions

type private RecordingSink() =
    let failures = ResizeArray<RenderFailureTelemetry>()

    member _.Failures: IReadOnlyList<RenderFailureTelemetry> = failures :> _

    interface IFuaranTelemetrySink with
        member _.RecordOpApply _ = ()
        member _.RecordDeny _ = ()
        member _.RecordRenderFailure tel = failures.Add tel
        member _.RecordProviderCall _ = ()
        member _.RecordCacheStat _ = ()
        member _.RecordValidateOutcome _ = ()

/// A runtime that records `Warn` and serves the custom renderers registered on
/// an inner `MutableRuntime`.
let private recordingRuntime (warnings: ResizeArray<string>) (inner: MutableRuntime) : IFuaranRuntime =
    let r = inner :> IFuaranRuntime

    { new IFuaranRuntime with
        member _.Call(e, o) = r.Call(e, o)
        member _.Notify(c, p) = r.Notify(c, p)
        member _.Navigate(x) = r.Navigate(x)
        member _.SetState(k, v) = r.SetState(k, v)
        member _.InvokeAiTool(t, a) = r.InvokeAiTool(t, a)
        member _.WriteToClipboard(t) = r.WriteToClipboard(t)
        member _.ReadFileBody(f, e, o) = r.ReadFileBody(f, e, o)
        member _.Warn(m) = warnings.Add m
        member _.LayoutObserver = None
        member _.TryRenderCustom(m, c, p) = r.TryRenderCustom(m, c, p)
        member _.TryGetCustomRenderer(m, c) = r.TryGetCustomRenderer(m, c)
        member _.TryRenderCustomInScope(s, m, c, p) = r.TryRenderCustomInScope(s, m, c, p)
        member _.TryGetCustomRendererInScope(s, m, c) = r.TryGetCustomRendererInScope(s, m, c)
        member _.CanDispatch(a) = r.CanDispatch(a)
        member _.TryLoadGuest(s) = r.TryLoadGuest(s) }

/// A sentinel element; never inspected (see CustomRendererTests).
let private stubElement: ReactElement = Unchecked.defaultof<ReactElement>

type private Harness =
    { Warnings: ResizeArray<string>
      Sink: RecordingSink
      Runtime: MutableRuntime
      Calls: ResizeArray<string> }

let private harness () : Harness =
    let runtime = MutableRuntime()
    let calls = ResizeArray<string>()
    runtime.RegisterCustomRenderer("host", "widget", (fun _ -> failwith "host renderer exploded"))

    runtime.RegisterCustomRenderer(
        "host",
        "probe",
        (fun props ->
            match Map.tryFind "tag" props with
            | Some(JStr tag) -> calls.Add tag
            | _ -> calls.Add "?"

            stubElement)
    )

    { Warnings = ResizeArray()
      Sink = RecordingSink()
      Runtime = runtime
      Calls = calls }

/// Render through the client renderer, tolerating the .NET Feliz shim's throw:
/// every seam asserted on is reached before it.
let private renderWith (h: Harness) (sources: BindingResolver.BindingSources) (node: Node<obj>) : unit =
    try
        Render.renderWithSourcesAndSink
            sources
            (recordingRuntime h.Warnings h.Runtime)
            (h.Sink :> IFuaranTelemetrySink)
            ignore
            node
        |> ignore
    with _ ->
        ()

let private md (id: string) (text: string) : Node<obj> = Fuaran.markdown id text

/// A `Switch` that renders `child` (its default; no case matches) and holds
/// `held` in a case that never matches — a position the fragment registry reads
/// and the render walk does not enter.
let private switchTo (id: string) (held: Node<obj> list) (child: Node<obj>) : Node<obj> =
    Fuaran.switch
        id
        { Defaults.switch<obj> with
            Cases =
                held
                |> List.map (fun n ->
                    { Child = n
                      Match = Some "never-selected"
                      When = None })
            Default = child }

let private throwingCustom (id: string) : Node<obj> =
    Fuaran.custom id "host" "widget" Map.empty Option.None []

/// A custom node whose renderer records `tag` when the client renders it.
let private probe (id: string) (tag: string) : Node<obj> =
    Fuaran.custom id "host" "probe" (Map.ofList [ "tag", JStr tag ]) Option.None []

let rec private ids (node: Node<obj>) : string list =
    node.Id
    :: (NodeChildren.children NodeChildren.Reach.kindHeld node |> List.collect ids)

/// Run `f` on a thread with room for a deep client walk — the client's
/// `renderKind` frame is far larger than the server's, so a walk at the wire
/// limit does not fit the default 1 MB test-thread stack in Debug.
let private onBigStack (f: unit -> unit) : unit =
    let mutable failure: exn option = None

    let t =
        System.Threading.Thread(
            (fun () ->
                try
                    f ()
                with ex ->
                    failure <- Some ex),
            256 * 1024 * 1024
        )

    t.Start()
    t.Join()

    match failure with
    | Some ex -> raise ex
    | None -> ()

let private nested (levels: int) (leaf: Node<obj>) : Node<obj> =
    let mutable tree = leaf

    for i in 1..levels do
        tree <- switchTo (sprintf "d%d" i) [] tree

    tree

// The fragment the server half's two-ref fixture expands.
let private fragmentBody: Node<obj> =
    Fuaran.dashboard
        "card"
        { Defaults.dashboard<obj> with
            Children = [ md "btn" "FRAGMENT-TEXT" ] }

/// The fragment-render fixture both halves render: a ref at `refId` to a
/// fragment whose leaf (`btn`) is a custom node whose host renderer throws, so
/// the per-node guard reports the leaf's id as the walk saw it. The server half
/// renders the same tree (`RendererAgreementTests.fs`, "the same fixture").
let private refToThrowingFragment (refId: string) : Node<obj> =
    switchTo
        "root"
        [ Fuaran.fragmentDecl
              "decl"
              { Defaults.fragmentDecl<obj> with
                  Name = "shared"
                  Body = switchTo "card" [] (throwingCustom "btn") } ]
        (Fuaran.fragmentRef refId "shared")

[<Tests>]
let tests =
    testList
        "Phase 2041 — the client renderer agrees with the server"
        [ testList
              "fragment ids"
              [ test "the client's expansion IS the shared walk" {
                    for prefix in [ "r1."; "r2." ] do
                        Expect.equal
                            (ids (Render.expandFragment prefix fragmentBody))
                            (ids (FragmentExpansion.namespaceIds prefix fragmentBody))
                            "cached expansion = the shared walk"

                        Expect.equal
                            (ids (Render.expandFragmentUncached prefix fragmentBody))
                            (ids (FragmentExpansion.namespaceIds prefix fragmentBody))
                            "uncached expansion = the shared walk"

                    Expect.equal
                        (ids (FragmentExpansion.namespaceIds (FragmentExpansion.refPrefix "r1") fragmentBody))
                        [ "r1.card"; "r1.btn" ]
                        "interior ids are namespaced under the ref's id"
                }

                test "the walk reaches exactly the positions the client's own walk did" {
                    // The client's walk before Phase 2041 rewrote ordered lists, a
                    // FragmentDecl body, both ErrorBoundary arms and every Switch
                    // arm, and left envelope alternatives (`state`) and slot
                    // arguments alone. The shared walk must keep that answer.
                    let body: Node<obj> =
                        Fuaran.dashboard
                            "outer"
                            { Defaults.dashboard<obj> with
                                Children =
                                    [ Fuaran.errorBoundary
                                          "eb"
                                          { Child = md "child" "c"
                                            Fallback = md "fb" "f" }
                                      Fuaran.switch
                                          "sw"
                                          { Defaults.switch<obj> with
                                              Cases =
                                                  [ { Child = md "case" "x"
                                                      Match = Some "a"
                                                      When = None } ]
                                              Default = md "dflt" "d" }
                                      { md "withState" "s" with
                                          State =
                                              Some
                                                  { Defaults.stateBehaviour<obj> with
                                                      OnLoading = Some(md "loading" "l") } } ] }

                    let out = FragmentExpansion.namespaceIds "p." body

                    Expect.equal
                        (ids out)
                        [ "p.outer"
                          "p.eb"
                          "p.child"
                          "p.fb"
                          "p.sw"
                          "p.case"
                          "p.dflt"
                          "p.withState" ]
                        "every kind-held position is namespaced"

                    let withState = (NodeChildren.children NodeChildren.Reach.kindHeld out |> List.last)

                    Expect.equal
                        (withState.State |> Option.bind _.OnLoading |> Option.map _.Id)
                        (Some "loading")
                        "an envelope alternative keeps its id, as it always did"
                }

                test "each ref renders the fragment's leaf under its own namespaced id" {
                    for refId in [ "r1"; "r2" ] do
                        let h = harness ()
                        renderWith h BindingResolver.empty (refToThrowingFragment refId)
                        let first = h.Sink.Failures.[0]
                        let leaf = refId + ".btn"

                        Expect.equal first.NodeId leaf "the leaf is reported under the ref-namespaced id"

                        Expect.equal
                            first.CorrelationId
                            (RenderParity.renderFailureCorrelationId
                                leaf
                                (RenderParity.nodeKindName (throwingCustom leaf).Kind))
                            "with the correlation id the server stamps on the same leaf"
                }

                test "a reference cycle is refused with the client's warning, not by recursion" {
                    let h = harness ()

                    let tree =
                        switchTo
                            "root"
                            // fuaran-validator: disable-next-line FUARAN058 — negative fixture: the cycle is the defect under test
                            [ Fuaran.fragmentDecl
                                  "decl"
                                  { Defaults.fragmentDecl<obj> with
                                      Name = "loop"
                                      Body = switchTo "inner" [] (Fuaran.fragmentRef "again" "loop") } ]
                            (Fuaran.fragmentRef "r" "loop")

                    renderWith h BindingResolver.empty tree

                    Expect.exists
                        h.Warnings
                        (fun w -> w.Contains("cycle detected") && w.Contains("'r.again'") && w.Contains("'loop'"))
                        "the re-entering ref is reported, under its namespaced id"

                    Expect.isFalse
                        (h.Warnings |> Seq.exists (fun w -> w.Contains RenderParity.depthExceededText))
                        "the depth limit never fired"

                    Expect.equal
                        (RenderParity.fragmentCycleText "loop")
                        "[fuaran:fragment cycle 'loop']"
                        "the shared text"
                } ]

          testList
              "host closures"
              [ test "a throwing custom renderer is caught by the per-node guard with the shared correlation id" {
                    let h = harness ()
                    renderWith h BindingResolver.empty (switchTo "root" [] (throwingCustom "c1"))
                    let first = h.Sink.Failures.[0]
                    let kindName = RenderParity.nodeKindName (throwingCustom "c1").Kind

                    Expect.equal first.NodeId "c1" "the custom node is the one that failed"
                    Expect.equal first.CaughtBy RenderFailureSource.PerNodeGuard "caught by the per-node guard"
                    Expect.equal first.ErrorMessage "host renderer exploded" "with the host's message"
                    Expect.equal first.NodeKindName kindName "under the shared kind name"

                    Expect.equal
                        first.CorrelationId
                        (RenderParity.renderFailureCorrelationId "c1" kindName)
                        "and the correlation id the server stamps on the same failure"
                }

                test "inside an ErrorBoundary the throw reaches the boundary, not the per-node guard" {
                    let h = harness ()

                    let tree =
                        Fuaran.errorBoundary
                            "eb"
                            { Child = switchTo "inside" [] (throwingCustom "c2")
                              Fallback = probe "fb" "fallback-rendered" }

                    renderWith h BindingResolver.empty tree
                    let first = h.Sink.Failures.[0]

                    Expect.isFalse
                        (h.Sink.Failures
                         |> Seq.exists (fun f -> f.CaughtBy = RenderFailureSource.PerNodeGuard && f.NodeId = "c2"))
                        "the per-node guard is suspended under the boundary"

                    Expect.equal first.NodeId "eb" "the boundary caught it"
                    Expect.equal first.CaughtBy RenderFailureSource.ErrorBoundary "as the boundary"
                    Expect.equal first.ErrorMessage "host renderer exploded" "the host's throw"
                    Expect.contains h.Calls "fallback-rendered" "and rendered its fallback"
                } ]

          testList
              "chart state slots"
              [ test "an unresolved chart source renders the OnLoading node" {
                    let h = harness ()

                    let chart =
                        { Fuaran.chart
                              "ch"
                              { Defaults.chart<obj> with
                                  Source = Binding.State("rows", None)
                                  XField = "x"
                                  YFields = [ "y" ] } with
                            State =
                                Some
                                    { Defaults.stateBehaviour<obj> with
                                        OnLoading = Some(probe "ld" "loading-rendered") } }

                    renderWith h BindingResolver.empty chart
                    Expect.contains h.Calls "loading-rendered" "the loading slot renders"
                }

                test "an errored chart source builds OnError from the payload the server passes" {
                    let h = harness ()
                    let payloads = ResizeArray<ErrorPayload>()

                    let chart =
                        { Fuaran.chart
                              "ch"
                              { Defaults.chart<obj> with
                                  Source = Binding.State("rows", None)
                                  XField = "x"
                                  YFields = [ "y" ] } with
                            State =
                                Some
                                    { Defaults.stateBehaviour<obj> with
                                        OnError =
                                            Some(fun e ->
                                                payloads.Add e
                                                probe "er" "error-rendered") } }

                    let sources =
                        { BindingResolver.empty with
                            State = Map.ofList [ "rows", Unchecked.nonNull (box 42) ] }

                    renderWith h sources chart

                    Expect.equal payloads.Count 1 "the error slot is built once"
                    Expect.equal payloads.[0].Kind ErrorKind.BindingResolution "as a binding-resolution error"

                    Expect.equal
                        payloads.[0].CorrelationId
                        (Ids.deterministicCorrelationId "ch")
                        "with the correlation id the server passes"

                    Expect.contains h.Calls "error-rendered" "and the slot renders"
                } ]

          testList
              "depth"
              [ test "a subtree past MaxDepth is omitted with the server's limit and message" {
                    onBigStack (fun () ->
                        let h = harness ()
                        renderWith h BindingResolver.empty (nested WireLimits.MaxDepth (probe "leaf" "deep-leaf"))

                        Expect.isFalse (h.Calls.Contains "deep-leaf") "the content past the limit is not rendered"

                        Expect.exists
                            h.Warnings
                            (fun w -> w.Contains RenderParity.depthExceededText && w.Contains "'leaf'")
                            "the omission is reported with the server's marker text, on the node past the limit")
                }

                test "a tree exactly at MaxDepth renders in full, and a failed walk leaves no depth behind" {
                    onBigStack (fun () ->
                        let h = harness ()
                        // A walk that throws out of the renderer (the .NET Feliz shim
                        // does, every time) must restore the depth counter, or the
                        // next render on this thread starts deeper than its root.
                        for _ in 1..3 do
                            renderWith
                                h
                                BindingResolver.empty
                                (nested (WireLimits.MaxDepth - 1) (probe "leaf" "at-limit"))

                        Expect.equal
                            (h.Calls |> Seq.filter ((=) "at-limit") |> Seq.length)
                            3
                            "every render reaches the leaf"

                        Expect.isFalse
                            (h.Warnings |> Seq.exists (fun w -> w.Contains RenderParity.depthExceededText))
                            "no truncation at the limit")
                } ] ]
