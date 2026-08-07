module Fuaran.UI.ServerDriven.Tests.BoundedDriverTests

// ─── Phase 153 (Wave 20): the no-`'Msg` bounded driver ─────────────
//
// Drives an AI-emitted, wire-decoded tree with NO hand-authored update / view.
// Fixture: a dashboard with a button (OnClick = SetState) + a Markdown whose
// text is `TextSource.Bound(Binding.State "msg")`. Clicking the button mutates
// the store; the bounded driver re-resolves the FIXED tree's bindings to
// Static, diffs, and patches only the changed node. Tests cover re-resolution,
// the binding-blind-diff fix, G1 rejection, G2 budgets, client effects, the
// op-stream sink, and dual-host determinism.

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops.Introspect
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation
open Fuaran.UI.ServerDriven.BoundedDriver
open Fuaran.UI.Ops
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.Renderer.BindingResolver

let private o (v: 'T) : obj = box v |> Unchecked.nonNull

/// Scalar → `JVal` for test payloads (same shape as BoundedActionsTests.jv).
let private jv (v: obj) : Fuaran.Core.JVal =
    match v with
    | :? int as i -> Fuaran.Core.JInt i
    | :? string as s -> Fuaran.Core.JStr s
    | :? bool as b -> Fuaran.Core.JBool b
    | :? float as f -> Fuaran.Core.JFloat f
    | other -> failwith (sprintf "jv: unsupported test payload %A" other)

/// A Markdown node whose text is bound to a `State` key (the reactive node).
let private boundMarkdown (id: string) (key: string) (dflt: string) : Node<obj> =
    let n = Fuaran.markdown id "placeholder"

    { n with
        Kind = NodeKind.Markdown({ Text = TextSource.Bound(Binding.State(key, Some dflt)) }) }

/// A dashboard: a button (OnClick supplied) + the reactive Markdown bound to
/// state key "msg" (default "init").
// Bounded sessions consume a `WireTree`; these authored test trees stand in for
// decoded ones (their closures are equally inert to the bounded driver, which
// never invokes them — the SAFETY tests below pin exactly that). `mkTreeNode`
// exposes the raw authored `Node<obj>` for the encode / resolveTree sites.
let private mkTreeNode (onClick: Action<obj>) : Node<obj> =
    Fuaran.dashboard
        "root"
        { Defaults.dashboard<obj> with
            Children =
                [ Fuaran.button
                      "set"
                      { Defaults.button<obj> with
                          OnClick = onClick }
                  boundMarkdown "count" "msg" "init" ] }

let private mkTree (onClick: Action<obj>) : WireTree = WireTree.ofDecoded (mkTreeNode onClick)

let private stubRender (n: Node<obj>) : string =
    let s = n.Id
    $"<f id='{s}'/>"

let private clickEv (nodeId: string) : LiveEvent =
    { ConnId = "c1"
      NodeId = nodeId
      Event = "click"
      Payload = Map.empty
      LastSeq = 0 }

/// The resolved Markdown text at `id`, as the resolved `Literal` string (the
/// dual-host-parity probe). `None` if absent or still a non-Literal TextSource
/// (TextSource carries closures so it has no equality — compare the string).
let private markdownLiteral (tree: Node<obj>) (id: string) : string option =
    match findNode (NodeId id) tree with
    | Some node ->
        match node.Kind with
        | NodeKind.Markdown({ Text = TextSource.Literal s }) -> Some s
        | _ -> None
    | None -> None

let private isReplaceOf (id: string) (p: DomPatch) : bool =
    match p with
    | DomPatch.ReplaceFragment(i, _) -> i = id
    | _ -> false

[<Tests>]
let tests =
    testList
        "Phase 153 — bounded driver"
        [ test "init re-resolves a State-bound TextSource to its default Literal" {
              let session =
                  BoundedDriver.init
                      (BoundedServices.createPermissive stubRender)
                      empty
                      (mkTree (Action.SetState("msg", jv "x")))

              Expect.equal (markdownLiteral session.Resolved "count") (Some "init") "default resolved"
          }

          test "a SetState click re-resolves the bound node and patches only it" {
              let session =
                  BoundedDriver.init
                      (BoundedServices.createPermissive stubRender)
                      empty
                      (mkTree (Action.SetState("msg", jv "updated")))

              let session2, out = BoundedDriver.step session (clickEv "set")

              Expect.isNone out.Rejected "not rejected"
              Expect.equal session2.Store.State (Map.ofList [ "msg", o "updated" ]) "store wrote msg=updated"
              Expect.equal (markdownLiteral session2.Resolved "count") (Some "updated") "bound node re-resolved"

              Expect.isTrue (out.Patches |> List.exists (isReplaceOf "count")) "the bound Markdown was patched"
              Expect.isFalse (out.Patches |> List.exists (isReplaceOf "set")) "the unrelated button was NOT patched"
          }

          test "binding-blind-diff fix: a SetState that does not change the resolved value yields no patch" {
              // Default is already "init"; setting "msg" -> "init" leaves the
              // resolved tree identical, so the diff is empty (the resolve-to-
              // Static pass is what lets the diff see *real* value changes).
              let session =
                  BoundedDriver.init
                      (BoundedServices.createPermissive stubRender)
                      empty
                      (mkTree (Action.SetState("msg", jv "init")))

              let _, out = BoundedDriver.step session (clickEv "set")
              Expect.isNone out.Rejected "not rejected"
              Expect.isEmpty out.Patches "no patch when the resolved value is unchanged"
          }

          test "Navigate click → ClientEffect, store + tree unchanged" {
              let session =
                  BoundedDriver.init
                      (BoundedServices.createPermissive stubRender)
                      empty
                      (mkTree (Action.Navigate "/next"))

              let session2, out = BoundedDriver.step session (clickEv "set")
              Expect.equal out.Effects [ ClientEffect.Navigate "/next" ] "navigate effect shipped"
              Expect.isEmpty out.Patches "no DOM patch for a pure client effect"
              Expect.equal session2.Store.State empty.State "store unchanged"
          }

          test "G1: an unknown node id is rejected; store unchanged, no patch" {
              let session =
                  BoundedDriver.init
                      (BoundedServices.createPermissive stubRender)
                      empty
                      (mkTree (Action.SetState("msg", jv "x")))

              let session2, out = BoundedDriver.step session (clickEv "ghost")

              match out.Rejected with
              | Some(Gate(RejectReason.UnknownNode "ghost")) -> ()
              | other -> failtestf "expected Gate(UnknownNode 'ghost'), got %A" other

              Expect.isEmpty out.Patches "no patch on G1 reject"
              Expect.equal session2.Store.State empty.State "store unchanged on reject"
          }

          test "G2: a bounded-action cascade over MaxActions is rejected (no hang, no mutation)" {
              // A pathological 200-action Chain against the default budget (64).
              let bigChain =
                  Action.Chain [ for i in 1..200 -> Action.SetState(sprintf "k%d" i, jv i) ]

              let session =
                  BoundedDriver.init (BoundedServices.createPermissive stubRender) empty (mkTree bigChain)

              let session2, out = BoundedDriver.step session (clickEv "set")

              match out.Rejected with
              | Some(BudgetExceeded _) -> ()
              | other -> failtestf "expected BudgetExceeded, got %A" other

              Expect.isEmpty out.Patches "no patch on budget breach"
              Expect.equal session2.Store.State empty.State "store unchanged on budget breach"
          }

          test "G2: a tree larger than MaxNodes is rejected" {
              let services =
                  { BoundedServices.createPermissive stubRender with
                      Budget =
                          { InteractionBudget.defaults with
                              MaxNodes = 1 } }

              // The fixture tree has 3 nodes (dashboard + button + markdown) > 1.
              let session =
                  BoundedDriver.init services empty (mkTree (Action.SetState("msg", jv "x")))

              let _, out = BoundedDriver.step session (clickEv "set")

              match out.Rejected with
              | Some(BudgetExceeded _) -> ()
              | other -> failtestf "expected BudgetExceeded (MaxNodes), got %A" other
          }

          // ── Phase 790: the budget is COST-aware, not node-count-blind ───────
          //
          // A Chart is ONE node, so a node count prices a chart carrying ten
          // thousand points the same as an empty one — the shape that let a
          // bounded-looking tree carry unbounded render work. The budget now
          // weights a data-bearing node by the data it carries.

          test "G2 (Phase 790): a data-bearing node is priced by its payload, not as one node" {
              let rows: Fuaran.Core.Row seq =
                  Seq.ofList [ for i in 1..200 -> Map.ofList [ "x", o (sprintf "c%d" i); "y", o (float i) ] ]

              let chart: Node<obj> =
                  { Fuaran.markdown "chart" "placeholder" with
                      Kind =
                          NodeKind.Chart(
                              { Kind = ChartKind.Bar
                                Source = Binding.Static(Some rows)
                                Stacked = false
                                XField = "x"
                                YFields = [ "y" ]
                                Title = None
                                OnPointClick = None }
                          ) }

              let tree =
                  WireTree.ofDecoded (
                      Fuaran.dashboard
                          "root"
                          { Defaults.dashboard<obj> with
                              Children =
                                  [ Fuaran.button
                                        "set"
                                        { Defaults.button<obj> with
                                            OnClick = Action.SetState("msg", jv "x") }
                                    chart ] }
                  )

              // Three NODES, but 200 points of render cost. A budget of 50 is
              // far above the node count and far below the cost.
              let services =
                  { BoundedServices.createPermissive stubRender with
                      Budget =
                          { InteractionBudget.defaults with
                              MaxNodes = 50 } }

              let session = BoundedDriver.init services empty tree

              Expect.isGreaterThan
                  session.NodeCount
                  50
                  "the chart's payload is priced into the tree cost (a bare node count would be 3)"

              let _, out = BoundedDriver.step session (clickEv "set")

              match out.Rejected with
              | Some(BudgetExceeded _) -> ()
              | other -> failtestf "expected BudgetExceeded on render cost, got %A" other
          }

          test "G2 (Phase 790): a data-free tree still costs exactly its node count" {
              let session =
                  BoundedDriver.init
                      (BoundedServices.createPermissive stubRender)
                      empty
                      (mkTree (Action.SetState("msg", jv "x")))

              Expect.equal session.NodeCount 3 "dashboard + button + markdown — unchanged from the pre-790 count"
          }

          test "FGP 5: applied ops are emitted to the OnApply sink" {
              let captured = ResizeArray<TreeOp<obj> list>()

              let services =
                  { BoundedServices.createPermissive stubRender with
                      OnApply = captured.Add }

              let session =
                  BoundedDriver.init services empty (mkTree (Action.SetState("msg", jv "updated")))

              let _, _ = BoundedDriver.step session (clickEv "set")
              Expect.equal captured.Count 1 "OnApply fired once"
              Expect.isNonEmpty (List.head (List.ofSeq captured)) "the applied op list is non-empty"
          }

          test "dual-host parity: resolveTree is deterministic and matches direct resolution" {
              let store =
                  { empty with
                      State = Map.ofList [ "msg", o "deterministic" ] }

              let tree = mkTreeNode (Action.SetState("msg", jv "x"))
              let r1 = BoundedDriver.resolveTree store tree
              let r2 = BoundedDriver.resolveTree store tree
              Expect.equal (markdownLiteral r1 "count") (markdownLiteral r2 "count") "resolveTree is deterministic"
              Expect.equal (markdownLiteral r1 "count") (Some "deterministic") "matches the store value"
          }

          // ── End-to-end: real wire JSON → decode → bounded driver ──────────
          test "end-to-end: a decoded wire tree drives a SetState click" {
              let authored = mkTreeNode (Action.SetState("msg", jv "wire"))
              let json = CanonicalJson.encodeNode authored

              match JsonDecode.decodeNode json with
              | Error e -> failtestf "decode failed: %A" e
              | Ok decoded ->
                  let session =
                      BoundedDriver.init (BoundedServices.createPermissive stubRender) empty decoded

                  let session2, out = BoundedDriver.step session (clickEv "set")
                  Expect.isNone out.Rejected "not rejected"
                  Expect.equal (Map.tryFind "msg" session2.Store.State) (Some(o "wire")) "wire-decoded SetState applied"
                  Expect.isTrue (out.Patches |> List.exists (isReplaceOf "count")) "bound node patched from wire input"
          }

          test "SAFETY end-to-end: an authored closure cannot cross the wire and never executes" {
              let mutable invoked = false

              let throwing (_: obj) : obj =
                  invoked <- true
                  o "boom"

              // Author a Call with a real (side-effecting) onResult, then send it
              // over the wire: the canonical encoder erases the closure to the
              // "<closure>" sentinel, the decoder reads back an inert placeholder,
              // and the bounded driver no-ops the Call — the authored closure is
              // unreachable end-to-end (decode → drive), the invariant Phase 154
              // multi-tenant hosting rests on.
              let authored = mkTreeNode (Action.Call("https://x", Some throwing, None))

              let json = CanonicalJson.encodeNode authored

              match JsonDecode.decodeNode json with
              | Error e -> failtestf "decode failed: %A" e
              | Ok decoded ->
                  let session =
                      BoundedDriver.init (BoundedServices.createPermissive stubRender) empty decoded

                  let _, out = BoundedDriver.step session (clickEv "set")
                  Expect.isFalse invoked "the authored closure was erased at the wire boundary and never ran"
                  Expect.isNone out.Rejected "the decoded inert Call drives without error"

                  // Phase 212 — the no-op is observable, not a silent dead end:
                  // the step output carries a readable diagnostic naming the
                  // inert Call.
                  match out.Diagnostics with
                  | [ BoundedDiagnostic.UnsupportedOnBoundedPath(nodeId, action) ] ->
                      Expect.equal nodeId "set" "diagnostic names the originating node"
                      Expect.stringContains action "Call" "diagnostic names the inert action"
                  | other -> failtestf "expected one UnsupportedOnBoundedPath diagnostic, got %A" other
          }

          // ── Phase 781: the budget is charged BEFORE the walk it bounds ──────
          //
          // `init` used to price the whole tree and then resolve the whole tree,
          // with nothing comparing the cost against `MaxNodes` until the first
          // `step`. So the construction whose purpose is to refuse an oversized
          // tree walked it twice, in full, and only then declared it too
          // expensive. The cost walk is now iterative (it cannot overflow) and
          // stops the moment it passes the budget, and `resolveTree` is skipped
          // entirely for a tree that is already over.

          test "G2 ordering: the cost walk STOPS at the budget instead of pricing the whole tree" {
              // A wide tree whose true cost is far above the budget. If the walk
              // ran to completion the reported cost would be the full node count;
              // stopping early, it can only be a little past the ceiling.
              let children = [ for i in 1..400 -> Fuaran.markdown (sprintf "m%d" i) "x" ]

              let tree =
                  WireTree.ofDecoded (
                      Fuaran.dashboard
                          "root"
                          { Defaults.dashboard<obj> with
                              Children = children }
                  )

              let services =
                  { BoundedServices.createPermissive stubRender with
                      Budget =
                          { InteractionBudget.defaults with
                              MaxNodes = 5 } }

              let session = BoundedDriver.init services empty tree

              Expect.isGreaterThan session.NodeCount 5 "the tree is genuinely over budget"

              // The full cost is 401. Anything near that means the walk did not
              // stop — which is the defect, restated as a number.
              Expect.isLessThan
                  session.NodeCount
                  50
                  (sprintf
                      "the cost walk should stop just past MaxNodes = 5, not price all 401 nodes (got %d)"
                      session.NodeCount)
          }

          test "G2 ordering: an over-budget tree is still refused at step, unchanged" {
              // The observable contract must not have moved: `init` stays total,
              // and the refusal still arrives from `step`. The button is needed
              // because G1 (does this node exist and accept this event?) runs
              // ahead of G2 — an event naming an absent node is refused as
              // `UnknownNode` and never reaches the budget at all.
              let children =
                  Fuaran.button
                      "set"
                      { Defaults.button<obj> with
                          OnClick = Action.SetState("msg", jv "x") }
                  :: [ for i in 1..400 -> Fuaran.markdown (sprintf "m%d" i) "x" ]

              let tree =
                  WireTree.ofDecoded (
                      Fuaran.dashboard
                          "root"
                          { Defaults.dashboard<obj> with
                              Children = children }
                  )

              let services =
                  { BoundedServices.createPermissive stubRender with
                      Budget =
                          { InteractionBudget.defaults with
                              MaxNodes = 5 } }

              let session = BoundedDriver.init services empty tree
              let _, out = BoundedDriver.step session (clickEv "set")

              match out.Rejected with
              | Some(BudgetExceeded _) -> ()
              | other -> failtestf "expected BudgetExceeded, got %A" other
          }

          test "G2 ordering: a within-budget tree is priced exactly and still resolves" {
              // The other half — early exit must not corrupt the ordinary case.
              let session =
                  BoundedDriver.init
                      (BoundedServices.createPermissive stubRender)
                      empty
                      (mkTree (Action.SetState("msg", jv "x")))

              Expect.equal session.NodeCount 3 "dashboard + button + markdown, priced exactly"

              let _, out = BoundedDriver.step session (clickEv "set")
              Expect.isNone out.Rejected "a within-budget tree drives normally"
              Expect.isNonEmpty out.Patches "and still produces patches, so resolveTree ran"
          } ]
