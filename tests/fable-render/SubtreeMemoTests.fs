module FableRender.SubtreeMemoTests

// ============================================================================
//  Phase 2550 — the element half of Phase 2547's subtree memo.
//
//  `src/Fuaran.UI.Tests/SubtreeMemoTests.fs` pins the memo's value-level pieces
//  on .NET: the decision, the table and the generation. It cannot pin what the
//  memo is FOR, because the client renderer's elements are Feliz's and do not
//  build on .NET. Here they do: under Fable, `Render.render` returns real React
//  elements, so the claim is asserted on the elements themselves.
//
//  Four keys, each with its own case: a kept node under the same context and
//  generation is the IDENTICAL element; a store write, a new context and a new
//  node each rebuild; a node under an action sink is never memoised. And one
//  control without which the first case would be vacuous: with the memo off,
//  two renders of one node are two elements.
// ============================================================================

#if FABLE_COMPILER
open Fable.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Ops.ActionInvocation
open FableRender.Expecto

/// Every element reachable from `root` through `props.children`, depth-first.
/// Function components are not entered: what they return is React's to build
/// at commit, and the memo's claim is about the element `render` returns.
[<Emit("""(() => {
  const found = [];
  const walk = (e) => {
    if (e == null || typeof e !== "object") return;
    if (Array.isArray(e)) { e.forEach(walk); return; }
    found.push(e);
    if (e.props) walk(e.props.children);
  };
  walk($0);
  return found;
})()""")>]
let private elementsOf (root: RenderedElement) : obj array = jsNative

[<Emit("($0.props != null ? $0.props[$1] : undefined)")>]
let private propOf (element: obj) (name: string) : obj = jsNative

/// The element carrying `data-fuaran-node-id = id` in `root`'s element tree.
/// Absent is a failure, never a pass: a probe that cannot find its subject has
/// proved nothing.
let private elementFor (id: string) (root: RenderedElement) : obj =
    match
        elementsOf root
        |> Array.tryFind (fun e -> (propOf e "data-fuaran-node-id") = box id)
    with
    | Some e -> e
    | None -> failtest (sprintf "no element in the rendered tree carries data-fuaran-node-id=%s" id)

let private heading (id: string) (text: string) : Node<obj> =
    Fuaran.heading
        id
        { Defaults.heading with
            Text = TextSource.Literal text }

let private root (children: Node<obj> list) : Node<obj> =
    Fuaran.stack
        "memo-root"
        { Defaults.stack with
            Children = children }

let private memoContext (node: Node<obj>) : Render.RenderContext<obj> =
    { Render.RenderContext.create BindingResolver.empty ignore node with
        SubtreeMemo = true }

/// A sink that records nothing: its presence on the context is the subject.
let private quietSink: IActionInvocationSink =
    { new IActionInvocationSink with
        member _.CaptureMode = ActionCaptureMode.Redacted
        member _.RecordActionInvocation _ = () }

let tests =
    testList
        "Phase 2547 — the subtree memo, on the elements"
        [ testCase "control: with the memo off, two renders of one node are two elements"
          <| fun () ->
              let node = heading "h" "memoised text"
              let ctx = Render.RenderContext.create BindingResolver.empty ignore node
              Expect.notSame (box (Render.render ctx node)) (box (Render.render ctx node)) "memo off"

          testCase "a kept node under the same context and generation is the identical element"
          <| fun () ->
              let node = heading "h" "memoised text"
              let ctx = memoContext node
              let first = Render.render ctx node
              Expect.same (box (Render.render ctx node)) (box first) "the second render serves the first element"

          testCase "a kept child under a rebuilt parent is the identical element"
          <| fun () ->
              // The host shape the memo pays for: the parent is rebuilt, the child is kept.
              let kept = heading "kept" "unchanged"
              let first = root [ kept; heading "moving" "one" ]
              let ctx = memoContext first
              let before = elementFor "kept" (Render.render ctx first)

              let after =
                  elementFor "kept" (Render.render ctx (root [ kept; heading "moving" "two" ]))

              Expect.same after before "the kept child's element is reused under a new parent"

          testCase "a store write rebuilds"
          <| fun () ->
              let node = heading "h" "memoised text"
              let ctx = memoContext node
              let first = Render.render ctx node
              StateStore.set "fable-render.memo.write" (box 1.0)
              let second = Render.render ctx node
              Expect.notSame (box second) (box first) "an element built before a write is not served after it"
              Expect.same (box (Render.render ctx node)) (box second) "the rebuilt element is memoised again"
              StateStore.remove "fable-render.memo.write"

          testCase "a new context rebuilds"
          <| fun () ->
              let node = heading "h" "memoised text"
              let ctx = memoContext node
              let first = Render.render ctx node
              let other = { ctx with SubtreeMemo = true }
              Expect.notSame (box (Render.render other node)) (box first) "the context key is identity"

          testCase "a new node rebuilds"
          <| fun () ->
              let node = heading "h" "memoised text"
              let ctx = memoContext node
              let first = Render.render ctx node
              let equal = heading "h" "memoised text"
              Expect.notSame (box (Render.render ctx equal)) (box first) "the node key is identity, not structure"

          testCase "a node under an action sink is never memoised"
          <| fun () ->
              let node = heading "h" "memoised text"

              let ctx =
                  { memoContext node with
                      ActionSink = Some quietSink }

              Expect.notSame
                  (box (Render.render ctx node))
                  (box (Render.render ctx node))
                  "an action sink defeats the memo" ]
#endif
