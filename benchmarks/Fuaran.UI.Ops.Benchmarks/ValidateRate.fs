module Fuaran.UI.Ops.Benchmarks.ValidateRate

open System
open System.Diagnostics
open System.IO
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops

// ============================================================================
//  ValidateRate (Phase 2054) — the cost of the pre-emit validator and the
//  analyses that read the same binding facts, over two trees.
//
//   - `corpus`    — every node fixture of the wire-format corpus that decodes,
//                   as the children of one Box: the vocabulary's whole breadth
//                   in one tree. Read from `../wire-format-fixtures/nodes`
//                   beside this repository (or `FUARAN_WIRE_FIXTURES`); absent
//                   corpus, the scenario is reported as NOT RUN, never timed.
//   - `synthetic` — a generated 5,001-node dashboard: 500 sections of a chart
//                   and a grid over a `Transform` source, a chart naming a
//                   column the pipeline does not produce (so the grounding
//                   findings are exercised), a state-bound metric, the button
//                   that writes its key, and four text leaves.
//
//  Per tree, five measurements: `collectFacts` alone, `validate`, the
//  binding-check report, the affordance-inertness report, and the three
//  facts readers together (`validate` + `bindingChecks` + the wiring graph).
//
//  Stopwatch-based like `DecodeRate`, warm-up first:
//  `dotnet run -c Release -- validate-rate [iterations]`.
// ============================================================================

let private decode (label: string) (text: string) : Node<obj> =
    match JsonDecode.decodeNodeObj text with
    | Ok n -> n
    | Error e -> failwithf "%s does not decode: %s" label (JsonDecode.DecodeError.render e)

/// The corpus root, as the tests find it: `FUARAN_WIRE_FIXTURES`, else the
/// clone beside this repository (or beside the checkout this worktree sits in).
let private corpusNodesDir () : string option =
    let fromEnv =
        match Environment.GetEnvironmentVariable "FUARAN_WIRE_FIXTURES" with
        | Null -> []
        | NonNull "" -> []
        | NonNull v -> [ v ]

    let repo = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))

    fromEnv @ [ Path.Combine(repo, "..", "wire-format-fixtures") ]
    |> List.map (fun r -> Path.Combine(r, "nodes"))
    |> List.tryFind Directory.Exists

/// Every corpus node fixture that decodes, in file-name order.
let corpusFixtures () : (string * Node<obj>) list =
    match corpusNodesDir () with
    | None -> []
    | Some dir ->
        Directory.GetFiles(dir, "*.json")
        |> Array.sort
        |> Array.toList
        |> List.choose (fun path ->
            match JsonDecode.decodeNodeObj (File.ReadAllText path) with
            | Ok n -> Some(string (Path.GetFileNameWithoutExtension path), n)
            | Error _ -> None)

let private boxJson (id: string) (children: string list) : string =
    sprintf
        """{"id":"%s","kind":{"$type":"Box","children":[%s],"layout":{"$type":"Auto"},"role":"Dashboard"}}"""
        id
        (String.concat "," children)

/// A `groupBy` over an inline table whose values are unique to `seed`, so no
/// two readers in the tree carry the same table (a repeated table is a
/// FUARAN107 finding per PAIR, which would make the figures measure that).
let private transformSource (seed: int) =
    sprintf
        """{"$type":"Transform","pipeline":[{"$type":"groupBy","aggs":[{"fn":"sum","name":"total","of":"amount"}],"keys":["dept"]}],"source":{"columns":{"amount":{"validity":[true,true,true],"values":[%d,120,90]},"dept":{"validity":[true,true,true],"values":["eng","eng","sales"]}},"schema":[{"name":"dept","type":"string"},{"name":"amount","type":"int"}]}}"""
        seed

let private section (i: int) : string =
    let chart (id: string) (seed: int) (y: string) =
        sprintf
            """{"id":"%s","kind":{"$type":"Chart","kind":"Bar","source":%s,"title":"Spend","xField":"dept","yFields":["%s"]}}"""
            id
            (transformSource seed)
            y

    let grid =
        sprintf
            """{"id":"grid-%d","kind":{"$type":"DataGrid","columns":[{"field":"dept","kind":{"$type":"Text"},"label":"dept"},{"field":"total","kind":{"$type":"Text"},"label":"total"}],"rowKeyField":"dept","source":%s}}"""
            i
            (transformSource (3 * i + 1))

    boxJson
        (sprintf "section-%d" i)
        [ chart (sprintf "chart-%d" i) (3 * i) "total"
          grid
          chart (sprintf "ungrounded-%d" i) (3 * i + 2) "missing"
          sprintf
              """{"id":"metric-%d","kind":{"$type":"Metric","label":"Count","value":{"$type":"State","key":"k%d"}}}"""
              i
              i
          sprintf
              """{"id":"button-%d","kind":{"$type":"Button","label":"Bump","onClick":{"$type":"SetState","key":"k%d","value":1},"variant":"Secondary"}}"""
              i
              i
          sprintf
              """{"id":"heading-%d","kind":{"$type":"Heading","level":2,"text":"Section %d","variant":"Standard"}}"""
              i
              i
          sprintf """{"id":"markdown-%d","kind":{"$type":"Markdown","text":"Body copy %d."}}""" i i
          sprintf """{"id":"note-%d","kind":{"$type":"Markdown","text":"Footnote %d."}}""" i i
          sprintf """{"id":"aside-%d","kind":{"$type":"Markdown","text":"Aside %d."}}""" i i ]

/// The generated 5,001-node tree.
let synthetic () : Node<obj> =
    decode "synthetic" (boxJson "root" [ for i in 0..499 -> section i ])

/// Every corpus fixture under one Box, or `None` without the corpus.
let corpus () : Node<obj> option =
    match corpusFixtures () with
    | [] -> None
    | fixtures ->
        Some(
            Fuaran.box
                "corpus-root"
                { Defaults.box with
                    Children = fixtures |> List.map snd }
        )

let rec nodeCount (n: Node<'Msg>) : int =
    1 + (NodeChildren.slots n |> List.sumBy (fun s -> nodeCount s.Node))

type Scenario =
    { Name: string; Run: Node<obj> -> unit }

let private ignoreResult (_: Result<unit, PreEmitValidate.PreEmitDefect list>) = ()

let all: Scenario list =
    [ { Name = "collect_facts"
        Run = fun t -> BindingWalk.collectFacts t |> ignore }
      { Name = "validate"
        Run = fun t -> PreEmitValidate.validate t |> ignoreResult }
      { Name = "binding_checks"
        Run = fun t -> PreEmitValidate.bindingChecks t |> ignore }
      { Name = "affordance_inertness"
        Run = fun t -> AffordanceInertness.report t |> ignore }
      { Name = "facts_readers"
        Run =
          fun t ->
              PreEmitValidate.validate t |> ignoreResult
              PreEmitValidate.bindingChecks t |> ignore
              WiringGraph.project t |> ignore } ]

/// Mean wall-time (µs) and allocation (bytes) per run of `scenario` over `tree`.
let measure (scenario: Scenario) (tree: Node<obj>) (iterations: int) : float * float =
    for _ in 1 .. max 1 (iterations / 10) do
        scenario.Run tree

    GC.Collect()
    let allocBefore = GC.GetAllocatedBytesForCurrentThread()
    let sw = Stopwatch.StartNew()

    for _ in 1..iterations do
        scenario.Run tree

    sw.Stop()
    let allocAfter = GC.GetAllocatedBytesForCurrentThread()
    let meanUs = sw.Elapsed.TotalMilliseconds * 1000.0 / float iterations
    let allocB = float (allocAfter - allocBefore) / float iterations
    meanUs, allocB

// ── the identity snapshot ───────────────────────────────────────────────────
//
// What every reader of the facts SAYS about each tree, rendered one item per
// line so a before/after diff names the item that moved. Phase 2054's
// acceptance is that this is byte-identical across the change.

let private line (w: TextWriter) (fmt: Printf.TextWriterFormat<'a>) : 'a = fprintfn w fmt

let private snapshotTree (w: TextWriter) (name: string) (tree: Node<obj>) =
    line w "### %s" name
    let facts = BindingWalk.collectFacts tree
    let b = facts.Bindings

    for u in b.Uses do
        line w "use %s %A" u.Reader u.Use

    for f in b.DeclaredFilters do
        line w "filter %A" f

    for c in b.Calls do
        line w "call %A" c

    for c in b.Closures do
        line w "closure %A" c

    for KeyValue(k, v) in b.Nodes do
        line w "node %s %b" k v

    let s = b.StateKeys

    for x in s.Writes do
        line w "write %A" x

    for x in s.Reads do
        line w "read %s" x

    line w "opaqueReader %b opaqueWriter %b" s.OpaqueReader s.OpaqueWriter

    for x in s.OpaqueReaders do
        line w "opaqueAt %A" x

    for x in s.WriteKeys do
        line w "writeKey %s" x

    for x in s.SwitchSelectors do
        line w "selector %A" x

    for x in s.TransformInertSources do
        line w "inert %A" x

    for x in s.VisibleStateSources do
        line w "visible %A" x

    for x in s.Seeds do
        line w "seed %s %s %A" x.Reader x.Key x.Fingerprint

    for x in s.InlineTables do
        line w "inline %s %A" x.Reader x.SeedKey

    for KeyValue(k, ds) in s.LiveTransformSites do
        line w "live %s %d" k (List.length ds)

    for x in b.TransformSites do
        line w "site %s %A %b" x.Reader x.Site.Slot x.Site.IsLive

    for x in facts.FilterWrites do
        line w "filterWrite %A" x

    match PreEmitValidate.validate tree with
    | Ok() -> line w "validate ok"
    | Error defects ->
        for d in defects do
            let code, sev, msg = PreEmitValidate.describe d
            line w "defect %s %A %s" code sev msg

    for c in PreEmitValidate.bindingChecks tree do
        line w "check %s %s %s %A" c.NodeId c.Reader c.Path c.Grade

        for p in c.Produced do
            line w "  produced %s %A" p.Name p.Type

        for d in c.Diagnostics do
            line w "  diag %s %s %s" d.Code d.Path d.Message

    for f in AffordanceInertness.report tree do
        line w "inert-affordance %s %s %A %A" f.Node f.Subject f.Verdict f.Alternative

    line w "%s" (WiringGraph.render (WiringGraph.project tree))

/// Write the snapshot of every corpus fixture and the synthetic tree to `path`.
let writeSnapshot (path: string) =
    use w = new StreamWriter(path)

    for name, tree in corpusFixtures () do
        snapshotTree w name tree

    snapshotTree w "synthetic" (synthetic ())
