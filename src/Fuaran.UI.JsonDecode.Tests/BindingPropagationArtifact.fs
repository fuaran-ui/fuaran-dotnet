module Fuaran.UI.JsonDecode.Tests.BindingPropagationArtifact

// ============================================================================
//  The generated BINDING-PROPAGATION conformance family (Phase 1764) —
//  `binding-propagation/manifest.json`, a self-enumerated family (WIRE_FORMAT
//  §27.7) whose normative sentence is §27.8.2's `binding-propagation` entry.
//
//  The question it answers: given a UI tree and a write, which bindings does
//  the write make stale? `laws/propagation-laws.json` (emitted by Core) pins
//  the generic dependency graph's closure, but its vectors never pass through a
//  UI tree, so a host whose BINDING WALK forgets a surface — a Transform's live
//  source, a param bound to state, a query's filter dependency — still passes
//  them. These vectors are (tree, write) pairs with the stale readers the write
//  must produce, so the walk itself is what is certified.
//
//  THE PROOF IS HERE. `toJson` decodes each authored tree through the
//  reference decoder, requires the authored bytes to BE the canonical encoding
//  (so a vector's tree is the wire, not an approximation of it), runs
//  `BindingGraph.dirty` over `BindingGraph.ofTree`, and RAISES when the verdict
//  disagrees with the authored expectation. So the family cannot publish a
//  claim the reference host does not meet.
//
//  `Binding.Computed` is not on the wire (its closure has no encoding), so the
//  family's conservative case is the one a tree CAN carry: a `Custom` node,
//  whose host renderer may read any state key.
//
//  Co-emitted by `Corpus.emit`, and writable alone with
//  `--emit-binding-propagation [<dir>]`. `BindingPropagationTests` asserts
//  byte-equality with the committed file, reads the committed vectors back, and
//  shows a perturbed graph failing them.
// ============================================================================

open System
open System.IO
open System.Text.Encodings.Web
open System.Text.Json

open Fuaran.UI.Types
open Fuaran.UI.Ops.JsonDecode
open Fuaran.UI.Renderer.BindingGraph

[<Literal>]
let artifactId =
    "https://fuaran.dev/wire-format/v1/binding-propagation/manifest.json"

/// The family's directory at the corpus root; the family is named by it.
[<Literal>]
let directory = "binding-propagation"

[<Literal>]
let fileName = "manifest.json"

type Verdict =
    | Exact
    | Conservative

type Vector =
    {
        Id: string
        Description: string
        /// The tree, in canonical wire bytes.
        Tree: string
        Write: Input list
        Verdict: Verdict
        /// The stale readers' node ids.
        Readers: string list
        /// The opaque reads a `Conservative` verdict names, in verdict order.
        Because: OpaqueRead list
    }

// ─── authoring helpers: canonical wire spellings ─────────────────────────────

let private bound (binding: string) =
    sprintf """{"$type":"Bound","binding":%s}""" binding

let private badge (id: string) (label: string) =
    sprintf """{"id":"%s","kind":{"$type":"Badge","label":%s,"variant":"Info"}}""" id label

let private stateRead (key: string) =
    sprintf """{"$type":"State","key":"%s"}""" key

let private filterRead (name: string) =
    sprintf """{"$type":"Filter","name":"%s"}""" name

let private selectionRead (nodeId: string) =
    sprintf """{"$type":"Selection","field":"id","nodeId":"%s"}""" nodeId

let private queryRead (name: string) (dependsOn: string list) =
    match dependsOn with
    | [] -> sprintf """{"$type":"Query","name":"%s"}""" name
    | deps ->
        sprintf
            """{"$type":"Query","dependsOn":[%s],"name":"%s"}"""
            (deps |> List.map (sprintf "\"%s\"") |> String.concat ",")
            name

let private countPipeline =
    """[{"$type":"groupBy","aggs":[{"fn":"count","name":"n","of":"a"}],"keys":[]}]"""

/// A `Transform` whose live source is a channel binding.
let private liveTransform (source: string) =
    sprintf """{"$type":"Transform","pipeline":%s,"source":%s}""" countPipeline source

let private embedded =
    """{"columns":{"a":{"validity":[true],"values":[1]}},"schema":[{"name":"a","type":"int"}]}"""

/// A `Transform` over an embedded table whose filter threshold is a param bound to `from`.
let private paramTransform (from: string) =
    sprintf
        """{"$type":"Transform","params":[{"from":%s,"name":"t"}],"pipeline":[{"$type":"filter","pred":{"$type":"binary","left":{"$type":"col","name":"a"},"op":"gt","right":{"$type":"param","name":"t"}}}],"source":%s}"""
        from
        embedded

let private customNode (id: string) =
    sprintf """{"id":"%s","kind":{"$type":"Custom","componentId":"trend-card","moduleId":"analytics","props":{}}}""" id

let private box (id: string) (children: string list) =
    sprintf
        """{"id":"%s","kind":{"$type":"Box","children":[%s],"layout":{"$type":"Auto"},"role":"Group"}}"""
        id
        (String.concat "," children)

let private dashboard (children: string list) =
    sprintf
        """{"id":"root","kind":{"$type":"Box","children":[%s],"layout":{"$type":"Auto"},"role":"Dashboard"}}"""
        (String.concat "," children)

let private v id description tree write verdict readers because =
    { Id = id
      Description = description
      Tree = tree
      Write = write
      Verdict = verdict
      Readers = readers
      Because = because }

/// The ordinary mixed board most vectors write to: one reader per channel, so
/// every vector's "and no more" has something to be wrong about.
let private board =
    dashboard
        [ badge "state-orders" (bound (stateRead "orders"))
          badge "state-other" (bound (stateRead "notes"))
          badge "live-orders" (bound (liveTransform (stateRead "orders")))
          badge "param-orders" (bound (paramTransform (stateRead "orders")))
          badge "filter-region" (bound (filterRead "region"))
          badge "selection-grid" (bound (selectionRead "orders-grid"))
          badge "query-direct" (bound (queryRead "summary" []))
          badge "query-scoped" (bound (queryRead "totals" [ "region" ]))
          badge "inert" "\"static\"" ]

/// The authored vectors. Ordinal-sorted by id on the way out, so an addition
/// lands as one clean insert.
let all: Vector list =
    [ v
          "state-all-three-surfaces"
          "a write to a state key makes stale every reader of it on all three State-channel surfaces: a plain State read, a Transform whose live source is the key, and a Transform param bound to it"
          board
          [ Input.State "orders" ]
          Exact
          [ "live-orders"; "param-orders"; "state-orders" ]
          []
      v
          "state-other-key"
          "a write to a different state key reaches only its own reader"
          board
          [ Input.State "notes" ]
          Exact
          [ "state-other" ]
          []
      v
          "state-plain-read"
          "a plain State read alone: the reader is stale and the inert sibling is not"
          (dashboard [ badge "a" (bound (stateRead "orders")); badge "b" "\"static\"" ])
          [ Input.State "orders" ]
          Exact
          [ "a" ]
          []
      v
          "state-transform-live-source"
          "a Transform's live source slot alone: the reader whose source is the written key is stale"
          (dashboard
              [ badge "t" (bound (liveTransform (stateRead "orders")))
                badge "u" (bound (stateRead "x")) ])
          [ Input.State "orders" ]
          Exact
          [ "t" ]
          []
      v
          "state-transform-param"
          "a Transform param bound to state alone: the source is embedded, so only the param carries the read"
          (dashboard
              [ badge "p" (bound (paramTransform (stateRead "orders")))
                badge "u" (bound (stateRead "x")) ])
          [ Input.State "orders" ]
          Exact
          [ "p" ]
          []
      v
          "filter-reaches-direct-and-scoped-query"
          "a filter write makes stale its direct reader and the reader of a query whose dependsOn names the filter; a query that does not depend on it stays clean"
          board
          [ Input.Filter "region" ]
          Exact
          [ "filter-region"; "query-scoped" ]
          []
      v
          "filter-param-source"
          "a Transform param bound to a filter is stale on that filter's write"
          (dashboard
              [ badge "p" (bound (paramTransform (filterRead "dept")))
                badge "q" (bound (filterRead "other")) ])
          [ Input.Filter "dept" ]
          Exact
          [ "p" ]
          []
      v
          "selection-read"
          "a selection write makes stale the readers of that producer's selection"
          board
          [ Input.Selection "orders-grid" ]
          Exact
          [ "selection-grid" ]
          []
      v
          "selection-other-producer"
          "a selection published by another producer reaches nothing"
          board
          [ Input.Selection "another-grid" ]
          Exact
          []
          []
      v
          "query-direct"
          "a query result write makes stale its reader and no other query's"
          board
          [ Input.Query "summary" ]
          Exact
          [ "query-direct" ]
          []
      v
          "write-touches-nothing"
          "a write to a key no binding reads makes nothing stale"
          board
          [ Input.State "unread" ]
          Exact
          []
          []
      v "write-empty" "an empty write makes nothing stale" board [] Exact [] []
      v
          "write-several-channels"
          "a write naming several channels makes stale the union of their readers"
          board
          [ Input.State "notes"; Input.Filter "region"; Input.Selection "orders-grid" ]
          Exact
          [ "filter-region"; "query-scoped"; "selection-grid"; "state-other" ]
          []
      v
          "multi-site-nested"
          "readers of one key at several depths are all stale, wherever they sit; readers of another key at the same depths are not"
          (dashboard
              [ badge "top" (bound (stateRead "orders"))
                box
                    "panel"
                    [ badge "mid" (bound (liveTransform (stateRead "orders")))
                      box
                          "inner"
                          [ badge "deep" (bound (stateRead "orders"))
                            badge "deep-other" (bound (stateRead "x")) ] ]
                badge "top-other" (bound (stateRead "x")) ])
          [ Input.State "orders" ]
          Exact
          [ "deep"; "mid"; "top" ]
          []
      v
          "custom-conservative-on-state"
          "a Custom node's host renderer may read any state key, so every state write makes it stale, and the verdict is Conservative naming it"
          (dashboard
              [ badge "a" (bound (stateRead "orders"))
                customNode "c"
                badge "b" (bound (stateRead "x")) ])
          [ Input.State "orders" ]
          Conservative
          [ "a"; "c" ]
          [ OpaqueRead.Custom "c" ]
      v
          "custom-conservative-unread-key"
          "a state write no binding reads still reaches a Custom node"
          (dashboard [ badge "a" (bound (stateRead "orders")); customNode "c" ])
          [ Input.State "unread" ]
          Conservative
          [ "c" ]
          [ OpaqueRead.Custom "c" ]
      v
          "custom-clean-on-filter"
          "a filter write is not a state write: a Custom node stays clean and the verdict is Exact"
          (dashboard [ badge "f" (bound (filterRead "region")); customNode "c" ])
          [ Input.Filter "region" ]
          Exact
          [ "f" ]
          [] ]
    |> List.sortWith (fun a b -> String.CompareOrdinal(a.Id, b.Id))

// ─── the wire spelling of writes and opaque reads ────────────────────────────

let inputJson (input: Input) : string =
    match input with
    | Input.State key -> sprintf """{"$type":"State","key":"%s"}""" key
    | Input.Filter name -> sprintf """{"$type":"Filter","name":"%s"}""" name
    | Input.Selection nodeId -> sprintf """{"$type":"Selection","nodeId":"%s"}""" nodeId
    | Input.Query name -> sprintf """{"$type":"Query","name":"%s"}""" name

let opaqueJson (read: OpaqueRead) : string =
    match read with
    | OpaqueRead.Custom nodeId -> sprintf """{"$type":"Custom","nodeId":"%s"}""" nodeId
    | OpaqueRead.Computed reader -> sprintf """{"$type":"Computed","reader":"%s"}""" reader
    | OpaqueRead.LiveSourceUnnamed reader -> sprintf """{"$type":"LiveSourceUnnamed","reader":"%s"}""" reader
    | OpaqueRead.Unlocated -> """{"$type":"Unlocated"}"""

let private str (el: JsonElement) (name: string) =
    match el.GetProperty(name).GetString() with
    | null -> failwithf "'%s' is null" name
    | s -> s

/// Read a write back from its wire spelling.
let inputOf (el: JsonElement) : Input =
    match str el "$type" with
    | "State" -> Input.State(str el "key")
    | "Filter" -> Input.Filter(str el "name")
    | "Selection" -> Input.Selection(str el "nodeId")
    | "Query" -> Input.Query(str el "name")
    | other -> failwithf "unknown write channel '%s'" other

/// The verdict a graph gives a write, in the family's terms: the verdict tag,
/// the stale readers ordinal-sorted, and the opaque reads it named.
let verdictOf (graph: Graph) (write: Input list) : Verdict * string list * OpaqueRead list =
    let readers (sites: Set<Site>) =
        readersOf sites
        |> Set.toList
        |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))

    match dirty graph write with
    | Dirty.Exact sites -> Exact, readers sites, []
    | Dirty.Conservative(sites, because) -> Conservative, readers sites, because

/// Decode a tree through the reference decoder.
let decodeTree (id: string) (json: string) : Node<obj> =
    match decodeNodeObj json with
    | Ok node -> node
    | Error e -> failwithf "binding-propagation vector '%s': the tree does not decode — %s" id e.Message

/// THE PROOF for one vector: the tree is canonical, and the reference graph's
/// verdict is the authored one.
let prove (vector: Vector) : unit =
    let node = decodeTree vector.Id vector.Tree
    let reencoded = Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode node

    if reencoded <> vector.Tree then
        failwithf
            "binding-propagation vector '%s': the authored tree is not canonical\n  authored → %s\n  canonical → %s"
            vector.Id
            vector.Tree
            reencoded

    let produced = verdictOf (ofTree node) vector.Write
    let authored = vector.Verdict, vector.Readers, vector.Because

    if produced <> authored then
        failwithf
            "binding-propagation vector '%s' violates its own expectation:\n  expected → %A\n  produced → %A"
            vector.Id
            authored
            produced

let private description =
    "The Fuaran BINDING-PROPAGATION conformance family (WIRE_FORMAT.md 27.8.2): which bindings a "
    + "write makes stale. Each vector is (tree, write, expected): `tree` is a canonical node, `write` "
    + "the channels a write changed (`State` key, `Filter` name, the `Selection` a producer node "
    + "publishes, a `Query` result), and `expected` the stale set. A host decodes the tree with its own "
    + "decoder, computes the stale set with its own binding walk, and asserts `expected.readers` - the "
    + "node ids whose spec holds a binding reading a written channel, directly or through a query whose "
    + "`dependsOn` names a written filter, ordinal-sorted - and `expected.verdict`: `Exact` when no "
    + "opaque read took part (the set is then minimal), `Conservative` naming each opaque read in "
    + "`expected.because` (a `Custom` node is stale on every state write). Generated from the reference "
    + "host's BindingGraph, which PROVES every vector, through its decoder, before this file is written."

/// Render the artefact.
let toJson () : string =
    let opts =
        JsonWriterOptions(Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping)

    use stream = new MemoryStream()
    use w = new Utf8JsonWriter(stream, opts)

    w.WriteStartObject()
    w.WriteNumber("version", 1)
    w.WriteString("$id", artifactId)
    w.WriteString("description", description)
    w.WriteStartArray("vectors")

    for vector in all do
        // THE PROOF. Nothing is written unless the reference host agrees.
        prove vector

        w.WriteStartObject()
        w.WriteString("id", vector.Id)
        w.WriteString("description", vector.Description)
        w.WritePropertyName("tree")
        w.WriteRawValue(vector.Tree)
        w.WriteStartArray("write")

        for input in vector.Write do
            w.WriteRawValue(inputJson input)

        w.WriteEndArray()
        w.WriteStartObject("expected")

        w.WriteString(
            "verdict",
            (match vector.Verdict with
             | Exact -> "Exact"
             | Conservative -> "Conservative")
        )

        w.WriteStartArray("readers")

        for r in vector.Readers do
            w.WriteStringValue r

        w.WriteEndArray()
        w.WriteStartArray("because")

        for b in vector.Because do
            w.WriteRawValue(opaqueJson b)

        w.WriteEndArray()
        w.WriteEndObject()
        w.WriteEndObject()

    w.WriteEndArray()
    w.WriteEndObject()
    w.Flush()

    Text.Encoding.UTF8.GetString(stream.ToArray()) + "\n"

/// Write the family into a corpus directory.
let write (corpusRoot: string) : string =
    let dir = Path.Combine(corpusRoot, directory)
    Directory.CreateDirectory dir |> ignore
    let path = Path.Combine(dir, fileName)
    File.WriteAllText(path, toJson ())
    path
