module Fuaran.UI.Program.Runtime.Tests.ProgramTests

// ─── The client placement of the bounded program loop ───────────────
//
// Runs an emitted, wire-decoded tree with NO hand-authored update / view and no
// server. The fixture mirrors the server placement's: a dashboard with a button
// (OnClick = SetState) + a Markdown whose text is
// `TextSource.Bound(Binding.State "msg")`. Clicking mutates the store; the loop
// re-resolves the FIXED tree's bindings and hands the resolved tree to the
// injected renderer. Tests cover re-resolution, the G1 gate, the G2 budgets,
// client effects, the op journal, the live-channel seam — and the SAFETY
// property, which is the shared interpreter's and therefore holds here without
// a second implementation.

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops.Introspect
open Fuaran.UI.ServerDriven
open Fuaran.UI.ServerDriven.Validation
open Fuaran.UI.Renderer.BindingResolver
open Fuaran.Program.Bounded
open Fuaran.UI.Program.BoundedDriver
open Fuaran.Program.Runtime
open Fuaran.UI.Program

let private o (v: 'T) : obj = box v |> Unchecked.nonNull

let private jv (v: obj) : Fuaran.Core.JVal =
    match v with
    | :? int as i -> Fuaran.Core.JInt i
    | :? string as s -> Fuaran.Core.JStr s
    | :? bool as b -> Fuaran.Core.JBool b
    | other -> failwith (sprintf "jv: unsupported test payload %A" other)

let private boundMarkdown (id: string) (key: string) (dflt: string) : Node<obj> =
    let n = Fuaran.markdown id "placeholder"

    { n with
        Kind = NodeKind.Markdown({ Text = TextSource.Bound(Binding.State(key, Some dflt)) }) }

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

let private clickEv (nodeId: string) : LiveEvent =
    { ConnId = "c1"
      NodeId = nodeId
      Event = "click"
      Payload = Map.empty
      LastSeq = 0 }

let private markdownLiteral (tree: Node<obj>) (id: string) : string option =
    match findNode (NodeId id) tree with
    | Some node ->
        match node.Kind with
        | NodeKind.Markdown({ Text = TextSource.Literal s }) -> Some s
        | _ -> None
    | None -> None

/// A recording renderer — the headless stand-in for the browser host. Every
/// injected seam in this loop is recordable, which is exactly why the same loop
/// is testable without a DOM.
type private Recorder() =
    let renders = ResizeArray<Node<obj>>()
    let effects = ResizeArray<ClientEffect>()
    let journal = ResizeArray<TreeOp<obj> list>()
    let denials = ResizeArray<EffectDenial>()
    member _.Renders = List.ofSeq renders
    member _.Effects = List.ofSeq effects
    member _.Journal = List.ofSeq journal
    member _.Denials = List.ofSeq denials
    member _.OnDenied(d: EffectDenial) = denials.Add d
    member _.Render(n: Node<obj>) = renders.Add n
    member _.Perform(e: ClientEffect) = effects.Add e
    member _.OnApply(ops: TreeOp<obj> list) = journal.Add ops

/// A registry that performs every built-in arm into the recorder. The registry
/// is CLOSED by registration, so this enumerates exactly what the host offers.
let private recordingRegistry (r: Recorder) : EffectRegistry =
    [ "WriteToClipboard"
      "Navigate"
      "PushState"
      "Focus"
      "Download"
      "ReadFileBody" ]
    |> List.fold (fun reg name -> EffectRegistry.register name r.Perform reg) EffectRegistry.denyAll
    |> EffectRegistry.permissive
    |> EffectRegistry.onDenied r.OnDenied

let private permissive (r: Recorder) : ProgramServices =
    { ProgramServices.createPermissive r.Render with
        Effects = recordingRegistry r
        OnApply = r.OnApply }

[<Tests>]
let tests =
    testList
        "client bounded program loop"
        [ test "mkBounded resolves the State-bound TextSource to its default and renders once" {
              let r = Recorder()

              let program =
                  Program.mkBounded (permissive r) empty (mkTree (Action.SetState("msg", Some(jv "x"), None)))

              Expect.equal (markdownLiteral program.Resolved "count") (Some "init") "default resolved"
              Expect.equal r.Renders.Length 1 "rendered exactly once at init"
          }

          test "a SetState event re-resolves the bound node and re-renders" {
              let r = Recorder()

              let program =
                  Program.mkBounded (permissive r) empty (mkTree (Action.SetState("msg", Some(jv "updated"), None)))

              let program2, out = Program.handleEvent program (clickEv "set")

              Expect.isNone out.Rejected "not rejected"
              Expect.equal program2.Store.State (Map.ofList [ "msg", o "updated" ]) "store wrote msg=updated"
              Expect.equal (markdownLiteral out.Resolved "count") (Some "updated") "bound node re-resolved"
              Expect.equal r.Renders.Length 2 "rendered again after the event"
          }

          test "G1: an unknown node id is rejected — no render, store unchanged" {
              let r = Recorder()

              let program =
                  Program.mkBounded (permissive r) empty (mkTree (Action.SetState("msg", Some(jv "x"), None)))

              let program2, out = Program.handleEvent program (clickEv "ghost")

              match out.Rejected with
              | Some(Gate(RejectReason.UnknownNode "ghost")) -> ()
              | other -> failtestf "expected Gate(UnknownNode 'ghost'), got %A" other

              Expect.equal r.Renders.Length 1 "no re-render on a G1 reject"
              Expect.equal program2.Store.State empty.State "store unchanged on reject"
          }

          test "the gate is DEFAULT-DENY: the non-permissive services refuse a dispatch" {
              let r = Recorder()

              let program =
                  Program.mkBounded
                      (ProgramServices.create r.Render)
                      empty
                      (mkTree (Action.SetState("msg", Some(jv "x"), None)))

              let _, out = Program.handleEvent program (clickEv "set")

              match out.Rejected with
              | Some(Gate(RejectReason.DispatchDenied _)) -> ()
              | other -> failtestf "expected Gate(DispatchDenied), got %A" other
          }

          test "G2: a cascade over MaxActions is rejected (no hang, no mutation)" {
              let r = Recorder()

              let bigChain =
                  Action.Chain [ for i in 1..200 -> Action.SetState(sprintf "k%d" i, Some(jv i), None) ]

              let program = Program.mkBounded (permissive r) empty (mkTree bigChain)
              let program2, out = Program.handleEvent program (clickEv "set")

              match out.Rejected with
              | Some(BudgetExceeded _) -> ()
              | other -> failtestf "expected BudgetExceeded, got %A" other

              Expect.equal program2.Store.State empty.State "store unchanged on a budget breach"
          }

          test "G2: a tree over MaxNodes is rejected on the first event" {
              let r = Recorder()

              let services =
                  { permissive r with
                      Budget =
                          { InteractionBudget.defaults with
                              MaxNodes = 1 } }

              let program =
                  Program.mkBounded services empty (mkTree (Action.SetState("msg", Some(jv "x"), None)))

              let _, out = Program.handleEvent program (clickEv "set")

              match out.Rejected with
              | Some(BudgetExceeded _) -> ()
              | other -> failtestf "expected BudgetExceeded (MaxNodes), got %A" other
          }

          test "a closure-free effect is PERFORMED and reported; store and tree unchanged" {
              let r = Recorder()

              let program =
                  Program.mkBounded
                      (permissive r)
                      empty
                      (mkTree (Action.Navigate(TextSource.Literal "/next", NavigateTarget.Self)))

              let program2, out = Program.handleEvent program (clickEv "set")

              Expect.equal out.Effects [ ClientEffect.Navigate("/next", NavigateTarget.Self) ] "effect reported"

              Expect.equal
                  r.Effects
                  [ ClientEffect.Navigate("/next", NavigateTarget.Self) ]
                  "effect performed through the seam"

              Expect.equal program2.Store.State empty.State "store unchanged"
          }

          test "effect seam: an UNREGISTERED effect is refused at the gate and the denial is recorded" {
              // The registry is closed by REGISTRATION, not by policy: a host
              // that never offered Navigate cannot perform one however
              // permissive its gate is.
              let r = Recorder()

              let services =
                  { permissive r with
                      Effects =
                          EffectRegistry.denyAll
                          |> EffectRegistry.permissive
                          |> EffectRegistry.onDenied r.OnDenied }

              let program =
                  Program.mkBounded
                      services
                      empty
                      (mkTree (Action.Navigate(TextSource.Literal "/next", NavigateTarget.Self)))

              let _, out = Program.handleEvent program (clickEv "set")

              Expect.equal
                  out.Effects
                  [ ClientEffect.Navigate("/next", NavigateTarget.Self) ]
                  "the loop still EMITTED the effect"

              Expect.isEmpty r.Effects "but nothing was performed"
              Expect.equal r.Denials [ EffectDenial.Unregistered "Navigate" ] "the denial was recorded, not dropped"
          }

          test "effect seam: a REGISTERED effect the gate refuses is recorded as a gate refusal" {
              let r = Recorder()

              let services =
                  { permissive r with
                      Effects =
                          EffectRegistry.denyAll
                          |> EffectRegistry.register "Navigate" r.Perform
                          |> EffectRegistry.withGate (fun name -> name <> "Navigate")
                          |> EffectRegistry.onDenied r.OnDenied }

              let program =
                  Program.mkBounded
                      services
                      empty
                      (mkTree (Action.Navigate(TextSource.Literal "/next", NavigateTarget.Self)))

              let _, _ = Program.handleEvent program (clickEv "set")

              Expect.isEmpty r.Effects "the performer did not run"

              Expect.equal
                  r.Denials
                  [ EffectDenial.GateRefused "Navigate" ]
                  "recorded as a GATE refusal, not as unregistered"
          }

          test "effect seam: the default registry performs nothing at all" {
              let r = Recorder()

              let services =
                  { ProgramServices.createPermissive r.Render with
                      Effects = EffectRegistry.denyAll |> EffectRegistry.onDenied r.OnDenied }

              let program =
                  Program.mkBounded
                      services
                      empty
                      (mkTree (Action.Navigate(TextSource.Literal "/next", NavigateTarget.Self)))

              let _, _ = Program.handleEvent program (clickEv "set")

              Expect.isEmpty r.Effects "a host that wires nothing performs nothing"
              Expect.isEmpty (EffectRegistry.registered EffectRegistry.denyAll) "and offers no capability"
              Expect.isNonEmpty r.Denials "the refusal is still observable"
          }

          test "an unsafe navigate route is refused before the effect is performed" {
              let r = Recorder()

              let program =
                  Program.mkBounded
                      (permissive r)
                      empty
                      (mkTree (Action.Navigate(TextSource.Literal "javascript:alert(1)", NavigateTarget.Self)))

              let _, out = Program.handleEvent program (clickEv "set")

              Expect.isEmpty out.Effects "no effect emitted for an unsafe route"
              Expect.isEmpty r.Effects "nothing performed"
              Expect.isNonEmpty out.Diagnostics "the refusal is diagnosed, not silent"
          }

          test "the op journal records the ops for an applied action" {
              let r = Recorder()

              let program =
                  Program.mkBounded (permissive r) empty (mkTree (Action.SetState("msg", Some(jv "updated"), None)))

              let _, _ = Program.handleEvent program (clickEv "set")

              Expect.equal r.Journal.Length 1 "one journal entry for one applied action"
              Expect.isNonEmpty (List.head r.Journal) "the entry carries the ops the state change produced"
          }

          test "SAFETY: a Call's onResult closure is never invoked by the client loop" {
              let r = Recorder()
              let mutable invoked = false

              let action =
                  Action.Call(
                      "/api/thing",
                      Some(fun _ ->
                          invoked <- true
                          box "should never run" |> Unchecked.nonNull),
                      None
                  )

              let program = Program.mkBounded (permissive r) empty (mkTree action)
              let _, out = Program.handleEvent program (clickEv "set")

              Expect.isFalse invoked "the tree's closure was NOT invoked"
              Expect.isNonEmpty out.Diagnostics "the inert action is diagnosed, not silent"
          }

          test "parity seed: the client loop resolves the same tree as the server placement" {
              // The full parity family is its own suite; this pins the property
              // at the loop level so a divergence cannot land unnoticed here.
              let r = Recorder()
              let wire = mkTree (Action.SetState("msg", Some(jv "shared"), None))
              let program = Program.mkBounded (permissive r) empty wire
              let _, clientOut = Program.handleEvent program (clickEv "set")

              let stubRender (n: Node<obj>) : string = $"<f id='{n.Id}'/>"

              let session =
                  BoundedDriver.init (BoundedServices.createPermissive stubRender) empty wire

              let session2, _ = BoundedDriver.step session (clickEv "set")

              Expect.equal
                  (markdownLiteral clientOut.Resolved "count")
                  (markdownLiteral session2.Resolved "count")
                  "both placements resolved the bound node identically"
          }

          test "the live-channel seam applies pushed ops and dispose releases the subscription" {
              let r = Recorder()
              let mutable disposed = false
              let mutable push: (TreeOp<obj> list -> unit) option = None

              let channel =
                  { new IClientLiveChannel with
                      member _.Subscribe(handler) =
                          push <- Some handler

                          { new System.IDisposable with
                              member _.Dispose() = disposed <- true } }

              let services =
                  { permissive r with
                      Channel = Some channel }

              let program =
                  Program.mkBounded services empty (mkTree (Action.SetState("msg", Some(jv "x"), None)))
                  |> Program.subscribe ignore

              Expect.isTrue push.IsSome "the host's channel was subscribed"

              let program2 = Program.dispose program
              Expect.isTrue disposed "dispose released the subscription"
              Expect.isNone program2.Subscription "the subscription slot is cleared"
          }

          test "no channel is the default: subscribe is a no-op and dispose is safe" {
              let r = Recorder()

              let program =
                  Program.mkBounded (permissive r) empty (mkTree (Action.SetState("msg", Some(jv "x"), None)))
                  |> Program.subscribe ignore

              Expect.isNone program.Subscription "pure client-only operation holds no subscription"
              let program2 = Program.dispose program
              Expect.isNone program2.Subscription "dispose is safe with no subscription"
          } ]
