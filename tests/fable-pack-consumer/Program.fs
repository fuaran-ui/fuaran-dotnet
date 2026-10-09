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
// boundary. `merge3Way` takes and returns `Fuaran.UI` trees; the `DagMerge.merge` binding names the
// DAG abstraction's sink and record types in its signature.

open Fuaran.UI.Types
open Fuaran.UI.OpStream.Dag.Abstractions
open Fuaran.UI.OpStream.Dag.Merge

let private leaf (id: string) (text: string) : Node<obj> =
    { Id = id
      Kind = NodeKind.Markdown({ Text = TextSource.Literal text })
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
        Kind =
            NodeKind.Box(
                { Layout = BoxLayout.Flex(Orientation.Vertical, false, None)
                  Role = BoxRole.Group
                  Heading = None
                  Children = kids
                  KeepTogether = false
                  BreakBefore = false }
            ) }

/// Two branches inserting different children under one root: disjoint, so the merge resolves.
let mergedChildCount: int =
    let baseTree = box "root" [ leaf "a" "base" ]
    let left = box "root" [ leaf "a" "base"; leaf "l" "left" ]
    let right = box "root" [ leaf "a" "base"; leaf "r" "right" ]

    match TreeMerge.merge3Way baseTree left right with
    | Ok(merged: Node<obj>) ->
        match merged.Kind with
        | NodeKind.Box b -> List.length b.Children
        | _ -> -1
    | Error(conflicts: MergeConflict list) -> -List.length conflicts

/// The DAG-level entry point, typed through the packed DAG abstraction.
let dagMerge
    : (DagOpRecord<obj> -> MergeAuthor)
          -> IDagOpStreamSink<obj>
          -> string
          -> Node<obj>
          -> string
          -> string
          -> System.DateTimeOffset
          -> Async<MergeResult<obj>> =
    DagMerge.merge<obj>
