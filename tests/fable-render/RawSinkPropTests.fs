module FableRender.RawSinkPropTests

// ============================================================================
//  Phase 2550 — Phase 2069's raw-sink claim, on elements and on a mounted DOM.
//
//  React 19 assigns `innerHTML` whenever a `dangerouslySetInnerHTML` prop is a
//  new OBJECT, without comparing `__html`. So the renderer keeps one prop object
//  per markup string, and an unchanged markdown, chart or drawing sink is not
//  reparsed on a host re-render. Two halves, both unreachable on .NET (where
//  Feliz's property is a tuple and the element never reaches React):
//
//    - on the elements: two emits of one markup share the prop object, and a
//      different markup does not;
//    - on a mounted tree: a host re-render that changes a heading and keeps a
//      markdown sink's markup records exactly the heading's mutation and no
//      `childList` mutation at all. Its control re-renders with CHANGED markup
//      and requires the reparse to be seen, so the zero is a measurement.
//
//  Every render here runs with the subtree memo OFF and builds new node values,
//  so what keeps the sink is the prop cache and nothing else.
// ============================================================================

#if FABLE_COMPILER
open Fable.Core
open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open FableRender.Dom
open FableRender.Expecto

/// The first `dangerouslySetInnerHTML` object in `root`'s element tree, or null.
[<Emit("""(() => {
  const walk = (e) => {
    if (e == null || typeof e !== "object") return null;
    if (Array.isArray(e)) { for (const c of e) { const f = walk(c); if (f) return f; } return null; }
    if (e.props && e.props.dangerouslySetInnerHTML) return e.props.dangerouslySetInnerHTML;
    return e.props ? walk(e.props.children) : null;
  };
  return walk($0);
})()""")>]
let private sinkPropOf (root: ReactElement) : obj = jsNative

[<Emit("(() => { const o = new MutationObserver(() => {}); o.observe($0, { childList: true, subtree: true, characterData: true, attributes: true }); return o; })()")>]
let private observe (container: obj) : obj = jsNative

/// The records taken so far, tallied by kind: `[| childList; characterData; attributes |]`.
[<Emit("""(() => {
  const records = $0.takeRecords();
  $0.disconnect();
  const n = (t) => records.filter((r) => r.type === t).length;
  return [n("childList"), n("characterData"), n("attributes")];
})()""")>]
let private tally (observer: obj) : int array = jsNative

[<Emit("($0.querySelector('.' + $1) != null)")>]
let private hasClass (container: obj) (className: string) : bool = jsNative

let private markdown (id: string) (body: string) : Node<obj> = Fuaran.markdown id body

let private surface (title: string) (body: string) : Node<obj> =
    Fuaran.stack
        "sink-root"
        { Defaults.stack with
            Children =
                [ Fuaran.heading
                      "sink-heading"
                      { Defaults.heading with
                          Text = TextSource.Literal title }
                  markdown "sink-body" body ] }

let private renderFresh (node: Node<obj>) : ReactElement =
    Render.render (Render.RenderContext.create BindingResolver.empty ignore node) node

/// Mount `first`, re-render with `second`, and return the mutation tally of the
/// re-render alone.
let private rerenderTally (first: Node<obj>) (second: Node<obj>) : int array =
    let container = newContainer ()
    let root = createRoot container

    try
        flushSync (fun () -> rootRender root (renderFresh first))

        if not (hasClass container Css.Class.Markdown) then
            failtest "the mounted tree holds no markdown sink - the probe has no subject"

        let observer = observe container
        flushSync (fun () -> rootRender root (renderFresh second))
        tally observer
    finally
        flushSync (fun () -> rootUnmount root)
        removeContainer container

let tests =
    testList
        "Phase 2069 — one dangerouslySetInnerHTML object per markup"
        [ testCase "two emits of one markup share the prop object"
          <| fun () ->
              let first = sinkPropOf (renderFresh (markdown "a" "**shared** markup"))
              let second = sinkPropOf (renderFresh (markdown "b" "**shared** markup"))
              Expect.isFalse (isNull first) "the markdown element carries a dangerouslySetInnerHTML prop"
              Expect.same second first "the second emit reuses the first emit's prop object"

          testCase "a different markup gets its own prop object"
          <| fun () ->
              let first = sinkPropOf (renderFresh (markdown "a" "one markup"))
              let second = sinkPropOf (renderFresh (markdown "b" "another markup"))
              Expect.isFalse (isNull first || isNull second) "both markdown elements carry the prop"
              Expect.notSame second first "a changed markup is a new prop object"

          testCase "control: a re-render with changed markup reparses the sink"
          <| fun () ->
              let counts =
                  rerenderTally (surface "title" "first body") (surface "title" "second body")

              Expect.isGreaterThan counts[0] 0 "the observer sees a sink reparse as childList mutations"

          testCase "a re-render with unchanged markup records no childList mutation"
          <| fun () ->
              let counts =
                  rerenderTally (surface "first title" "the **same** body") (surface "second title" "the **same** body")

              Expect.equal counts[0] 0 "childList mutations on the re-render"
              // The heading's change is what proves the re-render committed at all.
              Expect.equal counts[1] 1 "characterData mutations (the changed heading)"
              Expect.equal counts[2] 0 "attribute mutations" ]
#endif
