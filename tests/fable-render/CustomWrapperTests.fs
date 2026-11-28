module FableRender.CustomWrapperTests

// ============================================================================
//  Phase 2885 — a `Custom` node's wrapper, on a mounted tree.
//
//  When a `Custom` node declares exposed ids, or renders past an ADVISORY hash
//  mismatch, the server renderer wraps the registered renderer's body in a
//  `div` carrying the wrapper class and the two data attributes. The client
//  renderer did not, so a page hydrating the server's markup met a structure
//  the client never produced. `RenderParity.customWrapperAttributes` now decides
//  the wrapper for both; the server's half is pinned in the server suite's
//  `SsrParityTests`, and this file is the client's half, which is only
//  observable on a mounted DOM (on .NET the client's elements do not build).
//
//  Each case reads the element the node's own wrapper holds and compares its
//  attributes, as a set of name/value pairs, with the shared definition. The
//  control is the case with neither declaration, which must stay unwrapped.
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
open FableRender.Dom
open FableRender.Expecto

/// An element's attributes as sorted (name, value) pairs.
[<Emit("Array.from($0.attributes).map(a => [a.name, a.value])")>]
let private attributePairs (element: Browser.Types.Element) : (string * string)[] = jsNative

/// An element's first element child, or null.
[<Emit("$0.firstElementChild")>]
let private firstElementChild (element: Browser.Types.Element) : Browser.Types.Element = jsNative

let private attributesOf (element: Browser.Types.Element) : (string * string) list =
    attributePairs element |> List.ofArray |> List.sort

/// The wrapper the shared definition decides, as the client emits it: `class`
/// is the document's spelling of React's `className`.
let private expected
    (moduleId: string)
    (componentId: string)
    (exposed: string list)
    (advisory: bool)
    : (string * string) list option =
    RenderParity.customWrapperAttributes moduleId componentId exposed advisory
    |> Option.map List.sort

let private hash (h: string) : ContentHash =
    { Algorithm = "SHA256"
      Hash = h
      Strictness = HashStrictness.AdvisoryWarning }

/// Render `node` into a fresh mounted root and hand `read` the element the
/// node's own wrapper (`data-fuaran-node-id`) holds first.
let private withBody (runtime: MutableRuntime) (node: Node<obj>) (read: Browser.Types.Element -> unit) : unit =
    let container = newContainer ()
    let root = createRoot container

    try
        let ctx =
            { Render.RenderContext.create BindingResolver.empty ignore node with
                Runtime = runtime }

        flushSync (fun () -> rootRender root (Render.render ctx node))

        let nodeWrapper =
            (unbox<Browser.Types.HTMLElement> container).querySelector (sprintf "[data-fuaran-node-id=\"%s\"]" node.Id)

        if isNull nodeWrapper then
            failtest (sprintf "no element carries the node address %s" node.Id)

        let first = firstElementChild nodeWrapper

        if isNull first then
            failtest (sprintf "the node wrapper of %s holds no element" node.Id)

        read first
    finally
        flushSync (fun () -> rootUnmount root)
        removeContainer container

let private reactRuntime (contentHash: ContentHash option) : MutableRuntime =
    let runtime = MutableRuntime()

    let render =
        fun (_: Map<string, JVal>) -> Html.span [ prop.className "gauge-face"; prop.text "gauge" ]

    match contentHash with
    | Some h -> runtime.Registry.RegisterReact("widgets", "gauge", render, h)
    | None -> runtime.Registry.RegisterReact("widgets", "gauge", render)

    runtime

let private gauge (id: string) (contentHash: ContentHash option) (exposed: string list) : Node<obj> =
    Fuaran.custom id "widgets" "gauge" Map.empty contentHash (exposed |> List.map NodeId)

let private dialMount: CustomDomMount =
    { Mount =
        fun element _ ->
            let face = Browser.Dom.document.createElement "span"
            face.className <- "dial-face"
            element.appendChild face |> ignore

            { Update = ignore
              Dispose = fun () -> element.removeChild face |> ignore }
      Fallback = fun _ -> "Loading" }

let private hasChild (selector: string) (element: Browser.Types.Element) : bool =
    not (isNull (element.querySelector selector))

let tests =
    testList
        "Phase 2885 — a Custom node's wrapper agrees with the server's"
        [ testCase "declared exposed ids wrap the body in the shared wrapper"
          <| fun () ->
              withBody (reactRuntime None) (gauge "g1" None [ "needle"; "scale" ]) (fun body ->
                  Expect.equal
                      (Some(attributesOf body))
                      (expected "widgets" "gauge" [ "needle"; "scale" ] false)
                      "the wrapper's attributes are the shared definition's"

                  Expect.isTrue (hasChild ".gauge-face" body) "and the registered renderer's body is inside it")

          testCase "an advisory hash mismatch wraps the body with the advisory marker"
          <| fun () ->
              CustomHash.clearCustomHashFloorForTests ()

              try
                  CustomHash.installCustomHashFloor HashStrictness.AdvisoryWarning

                  withBody (reactRuntime (Some(hash "registered"))) (gauge "g2" (Some(hash "declared")) []) (fun body ->
                      Expect.equal
                          (Some(attributesOf body))
                          (expected "widgets" "gauge" [] true)
                          "the wrapper carries the advisory marker and no exposed ids"

                      Expect.isTrue (hasChild ".gauge-face" body) "the drifted renderer still renders, inside it")
              finally
                  CustomHash.clearCustomHashFloorForTests ()

          testCase "neither declaration: the body is the node's own child, unwrapped"
          <| fun () ->
              Expect.equal (expected "widgets" "gauge" [] false) None "the shared definition decides no wrapper"

              withBody (reactRuntime None) (gauge "g3" None []) (fun body ->
                  Expect.equal body.className "gauge-face" "the registered element sits directly in the node wrapper")

          testCase "a DOM mount with declared exposed ids is wrapped the same way"
          <| fun () ->
              let runtime = MutableRuntime()
              runtime.Registry.RegisterDomMount("widgets", "dial", dialMount)
              let node = Fuaran.custom "d1" "widgets" "dial" Map.empty None [ NodeId "dial-face" ]

              withBody runtime node (fun body ->
                  Expect.equal
                      (Some(attributesOf body))
                      (expected "widgets" "dial" [ "dial-face" ] false)
                      "the DOM-mount form takes the same wrapper"

                  Expect.isTrue (hasChild ".dial-face" body) "and the widget mounts inside it") ]
#endif
