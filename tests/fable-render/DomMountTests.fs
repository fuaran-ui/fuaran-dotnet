module FableRender.DomMountTests

// ============================================================================
//  Phase 2513 — a `Custom` node whose renderer is a DOM mount, on a mounted tree.
//
//  The widget below is framework-free: it calls the DOM and nothing else, and
//  the renderer hands it an element, its props, and later the changed props and
//  the dispose. Every claim needs a commit and effects, so it is unreachable on
//  .NET, where the client renderer's elements do not build.
//
//  Each claim has its control beside it: an update is counted against a
//  re-render that must NOT update, a boundary fallback against a boundary whose
//  mount succeeds, and the React form against the same arm serving a DOM mount.
// ============================================================================

#if FABLE_COMPILER
open Fable.Core
open Feliz
open Fuaran.UI.Renderer.Feliz
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Runtime
open Fuaran.UI.Telemetry.Abstractions
open FableRender.Dom
open FableRender.Expecto

let private title (props: Map<string, JVal>) : string =
    match Map.tryFind "title" props with
    | Some(JStr t) -> t
    | _ -> ""

/// A widget written against the DOM alone. `log` records every call the
/// renderer makes into it, in order.
let private dial (log: ResizeArray<string>) : CustomDomMount =
    { Mount =
        fun element props ->
            log.Add("mount, element held '" + element.textContent + "'")
            let face = Browser.Dom.document.createElement "span"
            face.className <- "dial-face"
            face.textContent <- title props
            element.appendChild face |> ignore

            { Update =
                fun next ->
                    log.Add("update " + title next)
                    face.textContent <- title next
              Dispose =
                fun () ->
                    log.Add "dispose"
                    element.removeChild face |> ignore }
      Fallback = fun props -> "Loading " + title props }

let private throwingIn (stage: string) (log: ResizeArray<string>) : CustomDomMount =
    let inner = dial log

    { inner with
        Mount =
            fun element props ->
                if stage = "mount" then
                    failwith "the widget could not start"

                let handle = inner.Mount element props

                { handle with
                    Update =
                        fun next ->
                            if stage = "update" then
                                failwith "the widget could not take the new props"

                            handle.Update next
                    Dispose =
                        fun () ->
                            handle.Dispose()

                            if stage = "dispose" then
                                failwith "the widget could not let go" } }

let private dialNode (t: string) : Node<obj> =
    Fuaran.custom "dial" "widgets" "dial" (Map.ofList [ "title", JStr t ]) None []

let private heading (id: string) (text: string) : Node<obj> =
    Fuaran.heading
        id
        { Defaults.heading with
            Text = TextSource.Literal text }

let private guarded (child: Node<obj>) : Node<obj> =
    Fuaran.errorBoundary
        "guard"
        { Defaults.errorBoundary with
            Child = child
            Fallback = heading "guard-fallback" "the dial is unavailable" }

/// Every render failure reported, in order.
type private Failures() =
    let records = ResizeArray<RenderFailureTelemetry>()
    member _.All = List.ofSeq records

    interface IFuaranTelemetrySink with
        member _.RecordOpApply _ = ()
        member _.RecordDeny _ = ()
        member _.RecordRenderFailure t = records.Add t
        member _.RecordProviderCall _ = ()
        member _.RecordCacheStat _ = ()
        member _.RecordValidateOutcome _ = ()

/// A host: one runtime and one telemetry sink, rendering successive trees into
/// one mounted root, each commit synchronous.
type private Host(mount: CustomDomMount) =
    let runtime = MutableRuntime()
    let failures = Failures()
    let container = newContainer ()
    let root = createRoot container
    do runtime.Registry.RegisterDomMount("widgets", "dial", mount)

    member _.Runtime = runtime
    member _.Failures = failures.All

    member _.Show(node: Node<obj>) : unit =
        let ctx =
            { Render.RenderContext.create BindingResolver.empty ignore node with
                Runtime = runtime
                TelemetrySink = Some(failures :> IFuaranTelemetrySink) }

        flushSync (fun () -> rootRender root (Render.render ctx node))

    member _.Text: string = (unbox<Browser.Types.HTMLElement> container).textContent

    member _.Has(selector: string) : bool =
        not (isNull ((unbox<Browser.Types.HTMLElement> container).querySelector selector))

    member _.Close() : unit =
        flushSync (fun () -> rootUnmount root)
        removeContainer container

/// True when `target` is, by reference, an element reachable from `root`
/// through `props.children`.
[<Emit("""(() => {
  const walk = (e) => {
    if (e === $1) return true;
    if (e == null || typeof e !== "object") return false;
    if (Array.isArray(e)) return e.some(walk);
    return e.props ? walk(e.props.children) : false;
  };
  return walk($0);
})()""")>]
let private holds (root: RenderedElement) (target: RenderedElement) : bool = jsNative

let private seq (log: ResizeArray<string>) : string list = List.ofSeq log

let tests =
    testList
        "Phase 2513 — a Custom node mounts through a DOM mount"
        [ testCase "the widget mounts once, into an emptied element, with the node's props"
          <| fun () ->
              let log = ResizeArray()
              let host = Host(dial log)

              try
                  host.Show(dialNode "pressure")
                  Expect.equal (seq log) [ "mount, element held ''" ] "one mount, after the fallback was cleared"
                  Expect.isTrue (host.Has ".dial-face") "the widget's own element is in the document"
                  Expect.equal host.Text "pressure" "the widget's text, and no fallback beside it"
              finally
                  host.Close()

          testCase "changed props are delivered through Update; equal props are not"
          <| fun () ->
              let log = ResizeArray()
              let host = Host(dial log)

              try
                  host.Show(dialNode "pressure")
                  // A new node value with equal props: the control for the next step.
                  host.Show(dialNode "pressure")
                  Expect.equal (seq log) [ "mount, element held ''" ] "an equal prop bag is not an update"
                  host.Show(dialNode "flow")

                  Expect.equal (seq log) [ "mount, element held ''"; "update flow" ] "one update, and no second mount"

                  Expect.equal host.Text "flow" "the widget shows the new props"
              finally
                  host.Close()

          testCase "the widget is disposed when its node leaves the tree"
          <| fun () ->
              let log = ResizeArray()
              let host = Host(dial log)

              try
                  host.Show(dialNode "pressure")
                  host.Show(heading "dial" "no widget here")
                  Expect.equal (seq log) [ "mount, element held ''"; "dispose" ] "disposed once"
                  Expect.isFalse (host.Has ".dial-face") "and gone from the document"
              finally
                  host.Close()

          testCase "the widget is disposed when the root unmounts"
          <| fun () ->
              let log = ResizeArray()
              let host = Host(dial log)
              host.Show(dialNode "pressure")
              host.Close()
              Expect.equal (seq log) [ "mount, element held ''"; "dispose" ] "disposed once"

          testCase "a mount that throws is reported and shows the failure placeholder"
          <| fun () ->
              let log = ResizeArray()
              let host = Host(throwingIn "mount" log)

              try
                  host.Show(dialNode "pressure")
                  Expect.isTrue (host.Has "[data-fuaran-render-failed]") "the labelled failure placeholder"

                  match host.Failures with
                  | [ failure ] ->
                      Expect.equal failure.NodeId "dial" "reported against the Custom node"
                      Expect.equal failure.CaughtBy RenderFailureSource.PerNodeGuard "as the per-node guard reports"

                      Expect.isTrue
                          (failure.ErrorMessage.Contains "the widget could not start")
                          "with the widget's own message"
                  | other -> failtest (sprintf "expected one reported failure, got %d" other.Length)
              finally
                  host.Close()

          testCase "control: under an ErrorBoundary, a mount that succeeds shows the widget"
          <| fun () ->
              let log = ResizeArray()
              let host = Host(dial log)

              try
                  host.Show(guarded (dialNode "pressure"))
                  Expect.isTrue (host.Has ".dial-face") "the widget"
                  Expect.isFalse (host.Text.Contains "unavailable") "not the boundary's fallback"
                  Expect.equal host.Failures [] "nothing reported"
              finally
                  host.Close()

          testCase "under an ErrorBoundary, a mount that throws shows the boundary's fallback"
          <| fun () ->
              let log = ResizeArray()
              let host = Host(throwingIn "mount" log)

              try
                  host.Show(guarded (dialNode "pressure"))
                  Expect.isTrue (host.Text.Contains "the dial is unavailable") "the boundary's fallback"
                  Expect.isFalse (host.Has "[data-fuaran-render-failed]") "not the per-node placeholder"

                  match host.Failures with
                  | [ failure ] ->
                      Expect.equal failure.CaughtBy RenderFailureSource.ErrorBoundary "caught by the boundary"
                  | other -> failtest (sprintf "expected one reported failure, got %d" other.Length)
              finally
                  host.Close()

          testCase "an update that throws disposes the widget and fails the node"
          <| fun () ->
              let log = ResizeArray()
              let host = Host(throwingIn "update" log)

              try
                  host.Show(dialNode "pressure")
                  host.Show(dialNode "flow")
                  Expect.equal (seq log) [ "mount, element held ''"; "dispose" ] "the widget was released"
                  Expect.isTrue (host.Has "[data-fuaran-render-failed]") "the failure placeholder"
                  Expect.isFalse (host.Has ".dial-face") "the widget is gone"
              finally
                  host.Close()

          testCase "a dispose that throws does not escape the renderer"
          <| fun () ->
              let log = ResizeArray()
              let host = Host(throwingIn "dispose" log)
              host.Show(dialNode "pressure")
              host.Show(heading "dial" "no widget here")
              Expect.equal (seq log) [ "mount, element held ''"; "dispose" ] "the dispose ran"
              Expect.isTrue (host.Text.Contains "no widget here") "and the next tree rendered"
              host.Close()

          testCase "a React-form renderer's element is served unchanged"
          <| fun () ->
              let element = Html.span [ prop.className "react-form"; prop.text "react" ]
              let runtime = MutableRuntime()
              runtime.Registry.RegisterReact("widgets", "card", (fun _ -> element))
              let node = Fuaran.custom "card" "widgets" "card" Map.empty None []

              let ctx =
                  { Render.RenderContext.create BindingResolver.empty ignore node with
                      Runtime = runtime }

              Expect.isTrue (holds (Render.render ctx node) (Rendered.ofReact element)) "the registered element itself" ]
#endif
