module Fuaran.UI.ServerDriven.Tests.LoweringTests

// ─── Phase 152 Track B: TreeOp → DomPatch lowering ──────────────
//
// The lowering joins the Track-A diff to the Track-B wire. A stub
// `renderFragment` (id → "<f id='…'/>") stands in for the host's
// Renderer.Server-backed renderer, so these assert the op→patch mapping
// without a real HTML renderer; the diff→lower pipeline test ties the
// two tracks end to end.

open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Replay
open Fuaran.UI.ServerDriven

/// Stub renderer — emits a marker carrying the node id so tests can assert
/// "the right node was rendered" without a real HTML renderer.
let private stubRender (n: Node<obj>) : string =
    let s = n.Id
    $"<f id='{s}'/>"

let private dash (children: Node<obj> list) : Node<obj> =
    Fuaran.dashboard
        "dash"
        { Defaults.dashboard<obj> with
            Children = children }

let private tree = dash [ Fuaran.markdown "m" "hello"; Fuaran.markdown "n" "world" ]

[<Tests>]
let tests =
    testList
        "Lowering (TreeOp → DomPatch)"
        [ test "structural ops lower directly (no render)" {
              Expect.equal
                  (Lowering.lower stubRender tree [ TreeOp.RemoveNode(NodeId "m") ])
                  [ DomPatch.RemoveNode "m" ]
                  "RemoveNode"

              Expect.equal
                  (Lowering.lower stubRender tree [ TreeOp.ReorderChildren(NodeId "dash", [ NodeId "n"; NodeId "m" ]) ])
                  [ DomPatch.ReorderChildren("dash", [ "n"; "m" ]) ]
                  "ReorderChildren maps node ids to raw strings"

              // `newTree` must be the POST-apply tree: the DOM index is now derived
              // from where the node actually landed, not carried on the op.
              let afterMove =
                  dash
                      [ Fuaran.markdown "n" "world"
                        Fuaran.dashboard
                            "other"
                            { Defaults.dashboard<obj> with
                                Children = [ Fuaran.markdown "x" "x"; Fuaran.markdown "m" "hello" ] } ]

              Expect.equal
                  (Lowering.lower stubRender afterMove [ TreeOp.MoveNode(NodeId "m", NodeId "other") ])
                  [ DomPatch.MoveNode("m", "other", 1) ]
                  "MoveNode → identity-preserving DomPatch.MoveNode, index derived from the result"
          }

          test "InsertChild renders the inserted subtree into an InsertFragment" {
              let child = Fuaran.markdown "fresh" "new"

              // Post-apply tree: the child appended, so it is last (index 2).
              let afterInsert =
                  dash [ Fuaran.markdown "m" "hello"; Fuaran.markdown "n" "world"; child ]

              Expect.equal
                  (Lowering.lower stubRender afterInsert [ TreeOp.InsertChild(NodeId "dash", child) ])
                  [ DomPatch.InsertFragment("dash", 2, "<f id='fresh'/>") ]
                  "InsertChild → InsertFragment, index derived from the result"
          }

          test "content ops re-render the changed node into a ReplaceFragment" {
              // A content op the prop map does not cover looks the node up in the
              // new tree and re-renders it; Markdown's text is rendered markup.
              let updated = dash [ Fuaran.markdown "m" "CHANGED"; Fuaran.markdown "n" "world" ]

              Expect.equal
                  (Lowering.lower
                      stubRender
                      updated
                      [ TreeOp.UpdateProp(
                            NodeId "m",
                            "Text",
                            PropValue.Native(Unchecked.nonNull (box (TextSource.Literal "CHANGED")))
                        ) ])
                  [ DomPatch.ReplaceFragment("m", "<f id='m'/>") ]
                  "UpdateProp → ReplaceFragment of the changed node (rendered from the new tree)"
          }

          test "a content op whose node is absent from the new tree yields nothing" {
              Expect.equal
                  (Lowering.lower stubRender tree [ TreeOp.UpdateStyle(NodeId "ghost", Defaults.style) ])
                  []
                  "no node, no patch"
          }

          test "Batch flattens to the concatenated lowering of its inner ops" {
              let ops =
                  [ TreeOp.Batch [ TreeOp.RemoveNode(NodeId "m"); TreeOp.MoveNode(NodeId "n", NodeId "dash") ] ]

              // Post-apply: `m` is gone and `n` was re-appended, so dash holds [n]
              // and the derived DOM index is 0.
              let afterBatch = dash [ Fuaran.markdown "n" "world" ]

              Expect.equal
                  (Lowering.lower stubRender afterBatch ops)
                  [ DomPatch.RemoveNode "m"; DomPatch.MoveNode("n", "dash", 0) ]
                  "Batch is flattened in order"
          }

          test "end-to-end: diff → lower produces a targeted patch for a leaf text change" {
              let a = dash [ Fuaran.markdown "m" "before" ]
              let b = dash [ Fuaran.markdown "m" "after" ]

              let patches = TreeOpDiff.diff a b |> Lowering.lower stubRender b

              // The diff localises to the changed leaf (UpdateProp Text); the
              // lowering re-renders just that node.
              Expect.equal
                  patches
                  [ DomPatch.ReplaceFragment("m", "<f id='m'/>") ]
                  "one targeted ReplaceFragment for the changed leaf"
          } ]

// ─── Phase 2526: the frame-cost reading ─────────────────────────────────────
//
// `docs/SERVER-DRIVEN-FRAME-COST.md` quotes these figures. They are UTF-8 bytes
// of the SSE frame a session pushes, through the real server renderer, so the
// reading is deterministic and the assertions below hold it to the doc.

let private serverRender (n: Node<obj>) : string =
    Fuaran.UI.Renderer.Server.Render.render Fuaran.UI.Renderer.BindingResolver.empty n

/// The values a write moves on one site.
type private SiteWrite =
    { Panel: string
      Label: string
      Cell: string
      Style: SemanticStyle option }

let private untouched (i: int) : SiteWrite =
    { Panel = $"Panel {i}"
      Label = $"Site {i}"
      Cell = "12.5"
      Style = None }

let private gridRows = 50

/// One generated site: a card holding a label, a chart and a grid.
let private siteOf (i: int) (w: SiteWrite) : Node<obj> =
    let points: Row list =
        [ for p in 1..12 -> Map.ofList [ "x", nonNull (box $"p{p}"); "y", nonNull (box (float (p * (i + 1)))) ] ]

    let site =
        Fuaran.card
            $"site-{i}"
            { Defaults.card<obj> with
                Heading = Some(TextSource.Literal w.Panel)
                Children =
                    [ Fuaran.heading
                          $"site-{i}-label"
                          { Defaults.heading with
                              Text = TextSource.Literal w.Label }
                      Fuaran.chart
                          $"site-{i}-chart"
                          { Defaults.chart<obj> with
                              Kind = ChartKind.Bar
                              Source = Binding.Static(Some(points :> Row seq))
                              XField = "x"
                              YFields = [ "y" ] }
                      Fuaran.table
                          $"site-{i}-rows"
                          { Defaults.table<obj> with
                              Headers = [ "Region"; "Genre"; "Month"; "n" ] |> List.map TextSource.Literal
                              Rows =
                                  [ for r in 1..gridRows ->
                                        [ "north"; "drama"; $"m{r}"; (if r = 7 then w.Cell else string r) ]
                                        |> List.map TextSource.Literal ] } ] }

    { site with Style = w.Style }

/// The 24-site tree, with `write` applied to site 7.
let private boardWith (write: SiteWrite -> SiteWrite) : Node<obj> =
    dash [ for i in 0..23 -> siteOf i (if i = 7 then write (untouched i) else untouched i) ]

let private frameBytes (patches: DomPatch list) : int =
    { Seq = 1
      Patches = patches
      Effects = [] }
    |> FrameWire.encodeSse
    |> System.Text.Encoding.UTF8.GetByteCount

/// The Phase 152 floor for one op: the changed node, re-rendered.
let private floorOf (after: Node<obj>) (op: TreeOp<obj>) : DomPatch list =
    let id =
        match op with
        | TreeOp.UpdateProp(NodeId id, _, _)
        | TreeOp.EditNode(NodeId id, _)
        | TreeOp.UpdateState(NodeId id, _)
        | TreeOp.UpdateStyle(NodeId id, _)
        | TreeOp.PatchStyle(NodeId id, _) -> id
        | other -> failtestf "the reading drew an op it does not price: %A" other

    match Fuaran.UI.Ops.Introspect.findNode (NodeId id) after with
    | Some node -> [ DomPatch.ReplaceFragment(id, serverRender node) ]
    | None -> failtestf "the reading's op names no node: %s" id

type private FrameReading =
    { Lowered: DomPatch list
      FloorBytes: int
      LoweredBytes: int }

let private readWrite (name: string) (write: SiteWrite -> SiteWrite) : FrameReading =
    let before = boardWith id
    let after = boardWith write
    let ops = TreeOpDiff.diff before after
    let floor = ops |> List.collect (floorOf after)
    let lowered = Lowering.lowerScript serverRender before ops

    let reading =
        { Lowered = lowered
          FloorBytes = frameBytes floor
          LoweredBytes = frameBytes lowered }

    printfn
        "Phase 2526 frame cost - %s: %d op(s); floor %d patch(es) %d bytes; lowered %d patch(es) [%s] %d bytes"
        name
        ops.Length
        floor.Length
        reading.FloorBytes
        lowered.Length
        (lowered |> List.map DomPatch.kind |> String.concat ",")
        reading.LoweredBytes

    reading

[<Tests>]
let frameCostTests =
    testList
        "Phase 2526 - the frame-cost reading"
        [ test "bytes and patches per write on the generated 24-site tree" {
              printfn
                  "Phase 2526 frame cost - first paint: %d bytes; empty frame: %d bytes"
                  (System.Text.Encoding.UTF8.GetByteCount(serverRender (boardWith id)))
                  (frameBytes [])

              let label = readWrite "label" (fun w -> { w with Label = "Site seven, renamed" })

              Expect.equal
                  label.Lowered
                  [ DomPatch.SetText("site-7-label", "Site seven, renamed") ]
                  "a label change arrives as one SetText"

              Expect.isLessThan label.LoweredBytes label.FloorBytes "and costs less than the fragment it replaces"

              let style =
                  readWrite "style" (fun w ->
                      { w with
                          Style =
                              Some
                                  { Defaults.style with
                                      Direction = TextDirection.Rtl } })

              Expect.equal
                  (style.Lowered |> List.map DomPatch.kind)
                  [ "ReplaceFragment" ]
                  "a style change keeps the floor"

              let row = readWrite "row" (fun w -> { w with Cell = "99.5" })

              Expect.equal
                  (row.Lowered |> List.map DomPatch.kind)
                  [ "ReplaceFragment" ]
                  "a row change is one re-render, not the diff's two"

              Expect.isLessThan row.LoweredBytes row.FloorBytes "which is what the frame stops paying for"

              let panel = readWrite "card heading" (fun w -> { w with Panel = "Renamed panel" })

              Expect.equal
                  (panel.Lowered |> List.map DomPatch.kind)
                  [ "ReplaceFragment" ]
                  "a container's heading keeps the floor: it is not the node's body"
          } ]

// ─── Phase 2526: the prop map and the DOM-equivalence law ───────────────────
//
// The law: for generated trees and op scripts, applying the lowered patches to
// the old tree's DOM yields the DOM of the new tree, as the real server renderer
// writes both. `Dom` models the shim's applier; the shim itself runs the same
// patch kinds in the content-js harness.

module private Dom =
    type Element =
        | Element of tag: string * attrs: Map<string, string> * children: Element list
        | Text of string

    let private decode (s: string) : string =
        nonNull (System.Net.WebUtility.HtmlDecode s)

    /// The renderer's own output, which is all this has to read: every element
    /// closed or void, every attribute value double-quoted.
    let parse (html: string) : Element list =
        let mutable i = 0

        let until (c: char) =
            let start = i

            while html[i] <> c do
                i <- i + 1

            html.Substring(start, i - start)

        let attribute () =
            let start = i

            while html[i] <> '=' && html[i] <> ' ' && html[i] <> '>' && html[i] <> '/' do
                i <- i + 1

            let name = html.Substring(start, i - start)

            if html[i] = '=' then
                i <- i + 2
                let value = until '"'
                i <- i + 1
                name, decode value
            else
                name, ""

        let rec children () : Element list =
            if i >= html.Length then
                []
            elif html[i] = '<' && html[i + 1] = '/' then
                until '>' |> ignore
                i <- i + 1
                []
            elif html[i] = '<' then
                i <- i + 1
                let start = i

                while html[i] <> ' ' && html[i] <> '>' && html[i] <> '/' do
                    i <- i + 1

                let tag = html.Substring(start, i - start)
                let mutable attrs = Map.empty

                while html[i] <> '>' && html[i] <> '/' do
                    if html[i] = ' ' then
                        i <- i + 1
                    else
                        attrs <- attrs.Add(attribute ())

                if html[i] = '/' then
                    i <- i + 1

                i <- i + 1

                let kids =
                    if Fuaran.UI.Renderer.ElementPlan.voidTags.Contains tag then
                        []
                    else
                        children ()

                Element(tag, attrs, kids) :: children ()
            else
                let start = i

                while i < html.Length && html[i] <> '<' do
                    i <- i + 1

                Text(decode (html.Substring(start, i - start))) :: children ()

        children ()

    /// Where the applier lands a granular patch: `true` is the shim's `bodyOf`.
    let private onBody (toBody: bool) (f: Element -> Element) (wrapper: Element) : Element =
        match wrapper with
        | Element(tag, attrs, children) when toBody ->
            let index =
                children
                |> List.findIndex (function
                    | Element _ -> true
                    | Text _ -> false)

            Element(tag, attrs, children |> List.mapi (fun i c -> if i = index then f c else c))
        | other -> f other

    let rec private rewrite (id: string) (f: Element -> Element list) (el: Element) : Element list =
        match el with
        | Element(_, attrs, _) when attrs.TryFind "data-fuaran-node-id" = Some id -> f el
        | Element(tag, attrs, children) -> [ Element(tag, attrs, children |> List.collect (rewrite id f)) ]
        | Text _ -> [ el ]

    /// One patch, by the shim's `applyPatch`.
    let apply (toBody: bool) (dom: Element list) (patch: DomPatch) : Element list =
        let onNode id f =
            dom |> List.collect (rewrite id (fun el -> [ onBody toBody f el ]))

        let withAttrs (edit: Map<string, string> -> Map<string, string>) (el: Element) =
            match el with
            | Element(tag, attrs, children) -> Element(tag, edit attrs, children)
            | Text _ -> failtest "an attribute patch reached a text node"

        match patch with
        | DomPatch.SetAttr(id, name, value) -> onNode id (withAttrs (Map.add name value))
        | DomPatch.RemoveAttr(id, name) -> onNode id (withAttrs (Map.remove name))
        | DomPatch.SetText(id, text) ->
            onNode id (fun el ->
                match el with
                | Element(tag, attrs, _) -> Element(tag, attrs, (if text = "" then [] else [ Text text ]))
                | Text _ -> failtest "a text patch reached a text node")
        | DomPatch.ReplaceFragment(id, html) -> dom |> List.collect (rewrite id (fun _ -> parse html))
        | other -> failtestf "the law drew a patch the applier does not model: %A" other

type private Rng = { mutable State: uint64 }

let private rngOf (seed: int) : Rng =
    { State = uint64 seed * 6364136223846793005UL + 1442695040888963407UL }

let private below (bound: int) (r: Rng) : int =
    r.State <- r.State * 6364136223846793005UL + 1442695040888963407UL
    int ((r.State >>> 33) % uint64 bound)

let private pick (values: 'a list) (r: Rng) : 'a = values[below values.Length r]

let private native (v: 'a) : PropValue = PropValue.Native(nonNull (box v))

let private texts =
    [ "plain"
      "with <angle> & ampersand"
      "\"quoted\" 'text'"
      "  spaced  "
      "ünï çødé"
      "" ]

let private text (r: Rng) : PropValue = PropValue.Wire(JStr(pick texts r))

/// A cleared option: the wire has no null, so a clear is the typed `None`.
let private cleared: PropValue = PropValue.Native(Unchecked.defaultof<obj>)

let private optional (values: string list) (r: Rng) : PropValue =
    match below 3 r with
    | 0 -> cleared
    | _ -> PropValue.Wire(JStr(pick values r))

let private boundText: TextSource = TextSource.I18n("a.key", Map.empty)

/// A mapped-kind leaf, with a tooltip on some of them so a hint sits behind the body.
let private leafOf (id: string) (r: Rng) : Node<obj> =
    let label = TextSource.Literal(pick texts r)

    let node: Node<obj> =
        match below 7 r with
        | 0 ->
            Fuaran.heading
                id
                { Defaults.heading with
                    Text = label
                    Level = 1 + below 6 r }
        | 1 -> Fuaran.badge id { Defaults.badge with Label = label }
        | 2 ->
            Fuaran.button
                id
                { Defaults.button<obj> with
                    Label = label }
        | 3 ->
            Fuaran.button
                id
                { Defaults.button<obj> with
                    Label = label
                    Icon = Some "star" }
        | 4 -> Fuaran.iconSpec id { Defaults.icon with Icon = "star" }
        | 5 ->
            Fuaran.linkSpec
                id
                { Defaults.link with
                    Href = Binding.Static(Some "https://example.test/a")
                    Label = label
                    Rel = Some "nofollow"
                    Target = Some "_blank" }
        | _ -> Fuaran.markdown id "a *body*"

    if below 3 r = 0 then
        { node with
            Tooltip = Some(TextSource.Literal "a hint") }
    else
        node

/// One drawn write to `node`, mapped or not: the ops carry the values every
/// host's wire can, a cleared option included.
let private opFor (node: Node<obj>) (r: Rng) : TreeOp<obj> =
    let id = NodeId node.Id
    let prop field value = TreeOp.UpdateProp(id, field, value)

    match node.Kind with
    | NodeKind.Heading _ ->
        match below 5 r with
        | 0 -> prop "Variant" (native (pick [ HeadingVariant.Standard; HeadingVariant.Eyebrow; HeadingVariant.Lead ] r))
        | 1 -> prop "Level" (PropValue.Wire(JInt(1 + below 6 r)))
        | 2 -> prop "Text" (native boundText)
        | 3 ->
            TreeOp.EditNode(
                id,
                NodeKind.Heading
                    { Defaults.heading with
                        Text = TextSource.Literal(pick texts r) }
            )
        | _ -> prop "Text" (text r)
    | NodeKind.Badge _ ->
        match below 3 r with
        | 0 -> prop "Variant" (native (pick [ BadgeVariant.Neutral; BadgeVariant.Critical; BadgeVariant.Info ] r))
        | 1 -> TreeOp.UpdateState(id, Defaults.stateBehaviour<obj>)
        | _ -> prop "Label" (text r)
    | NodeKind.Button _ ->
        match below 2 r with
        | 0 -> prop "Variant" (native (pick [ ButtonVariant.Primary; ButtonVariant.Destructive ] r))
        | _ -> prop "Label" (text r)
    | NodeKind.Icon _ ->
        match below 4 r with
        | 0 -> prop "Icon" (PropValue.Wire(JStr "moon"))
        | 1 -> prop "Size" (native (pick [ IconSize.Small; IconSize.Large ] r))
        | 2 -> prop "Tone" (native (pick [ ToneVariant.Brand; ToneVariant.Critical ] r))
        | _ -> prop "Label" (optional [ "A moon"; "<A> star" ] r)
    | NodeKind.Link _ ->
        match below 4 r with
        | 0 -> prop "Rel" (optional [ "external"; "noopener" ] r)
        | 1 -> prop "Target" (optional [ "_blank"; "_self"; "named" ] r)
        | 2 -> prop "Download" (PropValue.Wire(JBool(below 2 r = 0)))
        | _ -> prop "Label" (text r)
    | _ -> prop "Text" (text r)

/// One generated (tree, op script).
let private drawn (seed: int) : Node<obj> * TreeOp<obj> list =
    let r = rngOf seed
    let leaves = [ for i in 1 .. 2 + below 5 r -> leafOf $"leaf-{i}" r ]
    let ops = [ for _ in 1 .. 1 + below 6 r -> opFor (pick leaves r) r ]
    dash leaves, ops

/// The law on one (tree, script): `None`, or the patches and the two DOMs.
let private divergence
    (slotOf: NodeKind<obj> -> string -> Lowering.PropSlot option)
    (toBody: bool)
    (tree: Node<obj>, ops: TreeOp<obj> list)
    : (DomPatch list * string) option =
    let after =
        ops
        |> List.fold
            (fun t op ->
                match Fuaran.UI.Ops.Apply.apply op t with
                | Ok t' -> t'
                | Error e -> failtestf "a drawn op did not apply: %A -> %A" op e)
            tree

    let patches = Lowering.lowerScriptWith slotOf serverRender tree ops

    let patched =
        patches |> List.fold (Dom.apply toBody) (Dom.parse (serverRender tree))

    let rendered = Dom.parse (serverRender after)

    if patched = rendered then
        None
    else
        Some(patches, sprintf "patched: %A\nrendered: %A" patched rendered)

let private lawSeeds = [ 1..400 ]

let private lawHolds (slotOf: NodeKind<obj> -> string -> Lowering.PropSlot option) (toBody: bool) : bool =
    lawSeeds
    |> List.forall (fun seed -> (divergence slotOf toBody (drawn seed)).IsNone)

/// Every mapped prop, on a node that bears it.
let private mapped: (Node<obj> * string) list =
    let heading = Fuaran.heading "x" { Defaults.heading with Level = 3 }
    let badge = Fuaran.badge "x" Defaults.badge
    let button = Fuaran.button "x" Defaults.button<obj>
    let icon = Fuaran.iconSpec "x" { Defaults.icon with Icon = "star" }

    let link =
        Fuaran.linkSpec
            "x"
            { Defaults.link with
                Rel = Some "external"
                Target = Some "_blank"
                Download = true }

    [ heading, "Text"
      heading, "Variant"
      badge, "Label"
      badge, "Variant"
      button, "Label"
      button, "Variant"
      icon, "Icon"
      icon, "Size"
      icon, "Tone"
      icon, "Label"
      link, "Label"
      link, "Rel"
      link, "Target"
      link, "Download" ]

[<Tests>]
let granularTests =
    testList
        "Phase 2526 - the granular lowering"
        [ test "every mapped prop renders to exactly that attribute, on the node's body element" {
              for node, field in mapped do
                  let context =
                      sprintf "%s.%s" (Fuaran.UI.Renderer.RenderParity.nodeKindName node.Kind) field

                  let slot =
                      match Lowering.PropSlot.slotOf node.Kind field with
                      | Some slot -> slot
                      | None -> failtestf "%s is not in the map" context

                  let attrs, children =
                      match Dom.parse (serverRender node) with
                      | [ Dom.Element(_, _, (Dom.Element(_, attrs, children) :: _)) ] -> attrs, children
                      | other -> failtestf "%s: the wrapper holds no body element: %A" context other

                  match Lowering.PropSlot.patches node slot with
                  | None -> failtestf "%s: the plan does not bear the slot out" context
                  | Some patches ->
                      for patch in patches do
                          match patch with
                          | DomPatch.SetText(_, text) ->
                              Expect.equal
                                  children
                                  (if text = "" then [] else [ Dom.Text text ])
                                  $"{context}: the text is the body's whole content"
                          | DomPatch.SetAttr(_, name, value) ->
                              Expect.equal
                                  (attrs.TryFind name)
                                  (Some value)
                                  $"{context}: the renderer wrote {name} on the body"
                          | DomPatch.RemoveAttr(_, name) ->
                              Expect.isNone (attrs.TryFind name) $"{context}: the renderer wrote no {name}"
                          | other -> failtestf "%s: the map produced %A" context other
          }

          test "a field the map does not cover keeps the floor" {
              let heading = Fuaran.heading "x" { Defaults.heading with Level = 3 }
              let markdown = Fuaran.markdown "x" "body"

              Expect.isNone (Lowering.PropSlot.slotOf heading.Kind "Level") "Level moves the tag"
              Expect.isNone (Lowering.PropSlot.slotOf markdown.Kind "Text") "Markdown's text is rendered markup"

              let protectedLink =
                  Fuaran.linkSpec
                      "x"
                      { Defaults.link with
                          Protection = Some LinkProtection.Email }

              Expect.isNone (Lowering.PropSlot.slotOf protectedLink.Kind "Label") "a protected anchor is pre-built"

              let iconButton =
                  Fuaran.button
                      "x"
                      { Defaults.button<obj> with
                          Icon = Some "star" }

              Expect.isNone
                  (Lowering.PropSlot.slotOf iconButton.Kind "Label")
                  "an icon makes the label one child of two"
          }

          test "a bound text keeps the floor: its value is the host's to resolve" {
              let node =
                  Fuaran.heading
                      "x"
                      { Defaults.heading with
                          Text = boundText }

              Expect.equal
                  (Lowering.lower serverRender node [ TreeOp.UpdateProp(NodeId "x", "Text", native boundText) ])
                  [ DomPatch.ReplaceFragment("x", serverRender node) ]
                  "no SetText carries a value this core cannot resolve"
          }

          test "applying the lowered patches to the old DOM yields the re-rendered DOM" {
              for seed in lawSeeds do
                  match divergence Lowering.PropSlot.slotOf true (drawn seed) with
                  | None -> ()
                  | Some(patches, doms) -> failtestf "seed %d: %A\n%s" seed patches doms

              // Adequacy: a generator that never reached an arm certified nothing about it.
              let kinds =
                  lawSeeds
                  |> List.collect (fun seed ->
                      let tree, ops = drawn seed
                      Lowering.lowerScript serverRender tree ops |> List.map DomPatch.kind)
                  |> Set.ofList

              Expect.equal
                  kinds
                  (set [ "SetText"; "SetAttr"; "RemoveAttr"; "ReplaceFragment" ])
                  "the generated scripts reach every arm"
          }

          test "the law goes red when the map or the addressing is perturbed" {
              let slotOf = Lowering.PropSlot.slotOf

              let wrongAttribute (kind: NodeKind<obj>) (field: string) =
                  match slotOf kind field with
                  | Some(Lowering.PropSlot.Attrs [ "class" ]) -> Some(Lowering.PropSlot.Attrs [ "data-class" ])
                  | slot -> slot

              let claimsTheTag (kind: NodeKind<obj>) (field: string) =
                  match kind, field with
                  | NodeKind.Heading _, "Level" -> Some(Lowering.PropSlot.Attrs [ "class" ])
                  | _ -> slotOf kind field

              let dropsTheRel (kind: NodeKind<obj>) (field: string) =
                  match kind, field with
                  | NodeKind.Link _, "Target" -> Some(Lowering.PropSlot.Attrs [ "target" ])
                  | _ -> slotOf kind field

              Expect.isTrue (lawHolds slotOf true) "the unperturbed law holds"
              Expect.isFalse (lawHolds wrongAttribute true) "an attribute named wrongly"
              Expect.isFalse (lawHolds claimsTheTag true) "a prop that moves the tag, claimed as an attribute"
              Expect.isFalse (lawHolds dropsTheRel true) "an attribute a prop moves, left out"
              Expect.isFalse (lawHolds slotOf false) "a granular patch landing on the wrapper"
          } ]
