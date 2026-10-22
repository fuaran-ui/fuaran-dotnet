module Fuaran.UI.Ops.Benchmarks.DecodeRate

open System
open System.Diagnostics
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions

// ============================================================================
//  DecodeRate (Phase 2059) — the cost of the wire decoder over a large tree and
//  an op history, per entry route.
//
//  Two inputs, both encoded once by the canonical encoder:
//
//   - the 2,001-node tree `TreeDiffRate` diffs (a dashboard over 40 stacks of
//     49 headings, markdown and badges);
//   - an op history of 200 ops in rotation: a string `UpdateProp`, a numeric
//     `UpdateProp`, an `InsertChild` carrying a markdown leaf, and a `Batch` of
//     sixteen `UpdateProp`s.
//
//  Each is decoded by every route a caller takes today:
//
//   - `string`         — `decodeNode` / `decodeOp` over the encoded text: the
//                        primary entry, the one every corpus fixture meets.
//   - `render_reparse` — the route a secondary reader takes when it already holds
//                        the value as a Core `JVal` (a teleport bundle's tree and
//                        history, an elicitation envelope's tree, an attested
//                        document): `Canonical.write Rendering.CrossHost` the value, then decode the
//                        text. The `JVal` is parsed once, outside the timing.
//   - `jval`           — the `JVal` entry points, `decodeNodeObjJVal` /
//                        `decodeOpJVal`, over the same parsed value.
//
//  Stopwatch-based like `TreeDiffRate`, warm-up first:
//  `dotnet run -c Release -- decode-rate [iterations]`.
// ============================================================================

let private textOf (n: Node<unit>) : string = CanonicalJson.encodeNode n

let private jvalOf (text: string) : JVal =
    match Json.parse text with
    | Ok jv -> jv
    | Error e -> failwithf "the benchmark input does not parse: %s" e

/// `PropValue.Native` wraps a non-null `obj`; `box` is nullable under nullness checking, so the
/// payload is laundered through `Unchecked.nonNull` exactly as `OpCorpus.jv` does.
let private native (x: obj | null) : PropValue = PropValue.Native(Unchecked.nonNull x)

/// The 200-op history, in rotation over four shapes.
let private history: TreeOp<unit> list =
    [ for i in 0..199 ->
          match i % 4 with
          | 0 -> TreeOp.UpdateProp(NodeId $"leaf-{i % 40}-{i % 49}", "text", native (box $"Edited text {i}"))
          | 1 -> TreeOp.UpdateProp(NodeId $"leaf-{i % 40}-{i % 49}", "value", native (box (float i + 0.25)))
          | 2 ->
              TreeOp.InsertChild(NodeId $"stack-{i % 40}", Fuaran.markdown $"inserted-{i}" $"Inserted body copy {i}.")
          | _ ->
              TreeOp.Batch
                  [ for k in 0..15 ->
                        TreeOp.UpdateProp(NodeId $"leaf-{k}-{i % 49}", "text", native (box $"Batch {i}.{k}")) ] ]

let private treeText = textOf (TreeDiffRate.tree ())
let private treeJVal = jvalOf treeText
let private opTexts = history |> List.map CanonicalJson.encodeOp
let private opJVals = opTexts |> List.map jvalOf

let private expectOk (label: string) (r: Result<'a, JsonDecode.DecodeError>) : unit =
    match r with
    | Ok _ -> ()
    | Error e -> failwithf "%s did not decode: %s" label (JsonDecode.DecodeError.render e)

type Scenario = { Name: string; Run: unit -> unit }

let all: Scenario list =
    [ { Name = "node.string"
        Run = fun () -> JsonDecode.decodeNodeObj treeText |> expectOk "node.string" }
      { Name = "node.render_reparse"
        Run =
          fun () ->
              JsonDecode.decodeNodeObj (Canonical.write Rendering.CrossHost treeJVal)
              |> expectOk "node.render_reparse" }
      { Name = "node.jval"
        Run = fun () -> JsonDecode.decodeNodeObjJVal treeJVal |> expectOk "node.jval" }
      { Name = "ops.string"
        Run = fun () -> opTexts |> List.iter (JsonDecode.decodeOp >> expectOk "ops.string") }
      { Name = "ops.render_reparse"
        Run =
          fun () ->
              opJVals
              |> List.iter (fun jv ->
                  JsonDecode.decodeOp (Canonical.write Rendering.CrossHost jv)
                  |> expectOk "ops.render_reparse") }
      { Name = "ops.jval"
        Run = fun () -> opJVals |> List.iter (JsonDecode.decodeOpJVal >> expectOk "ops.jval") } ]

/// The sizes the figures are over: the tree's node count and encoded length,
/// and the history's op count and total encoded length.
let shape () : int * int * int * int =
    TreeDiffRate.nodeCount (), treeText.Length, List.length history, opTexts |> List.sumBy String.length

/// Mean wall-time (µs) and allocation (bytes) per run of `scenario`.
let measure (scenario: Scenario) (iterations: int) : float * float =
    for _ in 1 .. max 1 (iterations / 10) do
        scenario.Run()

    GC.Collect()
    let allocBefore = GC.GetAllocatedBytesForCurrentThread()
    let sw = Stopwatch.StartNew()

    for _ in 1..iterations do
        scenario.Run()

    sw.Stop()
    let allocAfter = GC.GetAllocatedBytesForCurrentThread()
    let meanUs = sw.Elapsed.TotalMilliseconds * 1000.0 / float iterations
    let allocB = float (allocAfter - allocBefore) / float iterations
    meanUs, allocB
