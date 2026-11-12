/// The packed-consumer leg (Phase 2128): a Fable consumer of the published telemetry packages.
///
/// Every call below hands a sink built by `Fuaran.UI.Telemetry.Default` to code typed against
/// `Fuaran.UI.Telemetry.Abstractions`. Before Phase 2128 the Default package shipped no Fable
/// sources, so Fable read it as a compiled assembly whose types point at the Abstractions ASSEMBLY,
/// which Fable had replaced with that package's sources: each crossing below was FS0074
/// ("IFuaranTelemetrySink is defined in an assembly that is not referenced").
module FablePackConsumer.Program

open Fuaran.UI.Telemetry.Abstractions
open Fuaran.UI.Telemetry.Default

/// A host seam typed against the abstraction, as a hosting tier installs a sink.
let install (sink: IFuaranTelemetrySink) : IFuaranTelemetrySink = sink

let sinks: IFuaranTelemetrySink list =
    [ install (NoOpSink.create ())
      install (InMemorySink() :> IFuaranTelemetrySink)
      install (ConsoleDevToolsSink.create ()) ]

let sinkCount = List.length sinks

// ---------------------------------------------------------------------------
//  Phase 2139 — the merge engine, through the PACKED `Fuaran.UI.OpStream.Dag.Merge`
// ---------------------------------------------------------------------------
//
// Until Phase 2139 the merge package shipped no Fable sources, so Fable read it as a compiled
// assembly whose signatures point at the `Fuaran.UI` and `Fuaran.UI.OpStream.Dag.Abstractions`
// ASSEMBLIES, which Fable had replaced with those packages' sources: every call below crossed that
// boundary. `DagReconcile.reconcile` takes and returns `Fuaran.UI` trees and ops (since Phase 2645 the
// DAG's merge is that policy over Core's fold); the `DagMerge.merge` binding names the DAG
// abstraction's sink and record types in its signature.

open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.Merge

let private leaf (id: string) (text: string) : Node<obj> =
    { Id = id
      Kind =
        NodeKind.Markdown(
            { Citations = []
              Text = TextSource.Literal text }
        )
      State = None
      Style = None
      Accessibility = None
      Motion = None
      Fallback = None
      ExtraAttributes = None
      Tooltip = None
      Visible = None }

let private box (id: string) (kids: Node<obj> list) : Node<obj> =
    { leaf id "" with
        Kind = NodeKind.Box({ Defaults.box with Children = kids }) }

/// Two branches inserting different children under one root: an interleave, so the merge resolves.
let mergedChildCount: int =
    let baseTree = box "root" [ leaf "a" "base" ]

    let left =
        [ Fuaran.UI.Ops.Types.TreeOp.InsertChild(NodeId "root", leaf "l" "left") ]

    let right =
        [ Fuaran.UI.Ops.Types.TreeOp.InsertChild(NodeId "root", leaf "r" "right") ]

    match DagReconcile.reconcile baseTree left right with
    | Ok(merged: Reconciled<obj>) ->
        match merged.Tree.Kind with
        | NodeKind.Box b -> List.length b.Children
        | _ -> -1
    | Error(_: ReconcileFault<obj>) -> -1

/// The DAG-level entry point, typed through the packed DAG abstraction.
let dagMerge
    : IDagOpStreamSink<obj>
          -> string
          -> Node<obj>
          -> string
          -> string
          -> System.DateTimeOffset
          -> Async<MergeResult<obj>> =
    DagMerge.merge<obj>
