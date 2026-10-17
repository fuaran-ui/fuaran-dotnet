module Fuaran.UI.OpStream.Tests.TreeOpDiffEquivalenceTests

// ─── Phase 2304: the diff is Core's `Diff`, and the corpus says so ──────────
//
// `TreeOpDiff.diff` is Fuaran.Core's `Diff.toOpsContainedWith` over the tier's
// witnesses, lowered onto `TreeOp`. This suite holds it to that over the wire
// corpus: every cross-host golden script of `diff/goldens.json` is emitted byte
// for byte; the structural half of every script is Core's, op for op; no
// script re-sends a node the old tree holds (a relocation is a `MoveNode`);
// and the round-trip law holds on every pair the tier's previous diff
// (`TreeOpDiffPre2063`, kept as the regression baseline) round-tripped.
//
// The pairs: every node fixture against itself, against every other fixture of
// the same root kind (re-rooted at its id), and against its cyclic neighbour of
// any kind; every node fixture against each `ops/` fixture that applies to it,
// in both directions; one-field mutations of every fixture, both directions;
// every `diff/goldens.json` before/after pair, in both directions; and a
// synthetic tree built from the corpus's leaves with cross-parent moves, a
// container move, and a nest inversion. A coverage assertion keeps the laws
// from being vacuous: the run must reach every op family the diff can emit.

open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open Expecto
open Microsoft.FSharp.Reflection
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.Ops.Introspect
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Replay

let private corpusRoot = Fuaran.Tests.CorpusRoot.tryFind ()

let private requireCorpus () : string =
    match corpusRoot with
    | Some root -> root
    | None ->
        skiptest
            "wire-format-fixtures/ not found walking up from the test assembly — the Phase 2304 diff laws run over the corpus (set FUARAN_WIRE_FIXTURES in a worktree)"

let private decodeNodeOrFail (what: string) (json: string) : Node<obj> =
    match JsonDecode.decodeNode json with
    | Ok tree -> WireTree.reify tree
    | Error e -> failtestf "%s did not decode: %s at '%s' — %s" what e.Code e.Path e.Message

/// Every `nodes/` fixture, sorted by file name.
let private nodeFixtures () : (string * Node<obj>) list =
    let root = requireCorpus ()

    Directory.GetFiles(Path.Combine(root, "nodes"), "*.json")
    |> Array.sort
    |> Array.toList
    |> List.map (fun path ->
        let name = Path.GetFileName path |> nonNull
        name, decodeNodeOrFail ("nodes/" + name) (File.ReadAllText path))

/// Every `ops/` fixture that decodes (the TreeOpMap laws assert the family
/// decodes in full; here an op is only a perturbation source).
let private opFixtures () : (string * TreeOp<obj>) list =
    let root = requireCorpus ()

    Directory.GetFiles(Path.Combine(root, "ops"), "*.json")
    |> Array.sort
    |> Array.toList
    |> List.choose (fun path ->
        match JsonDecode.decodeOp (File.ReadAllText path) with
        | Ok op -> Some(Path.GetFileName path |> nonNull, op)
        | Error _ -> None)

/// The before/after pairs of both sections of `diff/goldens.json`.
let private goldenPairs () : (string * Node<obj> * Node<obj>) list =
    let root = requireCorpus ()

    use doc =
        JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "diff", "goldens.json")))

    [ for section in [ "goldens"; "divergent" ] do
          for entry in doc.RootElement.GetProperty(section).EnumerateArray() do
              let id = entry.GetProperty("id").GetString() |> nonNull

              let node (field: string) =
                  decodeNodeOrFail $"diff/{id}.{field}" (entry.GetProperty(field).GetRawText())

              yield $"goldens/{id}", node "before", node "after" ]

let private isLeaf (n: Node<obj>) = (getChildren n.Kind).IsNone

/// A tree of four groups built from corpus leaves (ids made unique), nested so
/// that `g1` sits inside `g0`: root → [g0 → [g1 → leaves; leaves]; g2; g3].
let private syntheticTrees (leaves: Node<obj> list) : (string * Node<obj> * Node<obj>) list =
    let leaves = leaves |> List.mapi (fun i n -> { n with Id = $"L{i}" })

    let chunk k =
        leaves |> List.indexed |> List.filter (fun (i, _) -> i % 4 = k) |> List.map snd

    let group (id: string) (kids: Node<obj> list) : Node<obj> =
        Fuaran.stack id { Defaults.stack with Children = kids }

    let tree (g0: Node<obj> list) (g1: Node<obj> list) (g2: Node<obj> list) (g3: Node<obj> list) (nest: bool) =
        let g1Node = group "g1" g1

        let roots =
            if nest then
                [ group "g0" (g1Node :: g0); group "g2" g2; group "g3" g3 ]
            else
                [ group "g0" g0; g1Node; group "g2" g2; group "g3" g3 ]

        group "root" roots

    let c0, c1, c2, c3 = chunk 0, chunk 1, chunk 2, chunk 3
    let a = tree c0 c1 c2 c3 true

    let moveFirst (from: Node<obj> list) (into: Node<obj> list) =
        match from with
        | x :: rest -> rest, into @ [ x ]
        | [] -> from, into

    // One leaf crosses from g0 to g3.
    let c0', c3' = moveFirst c0 c3
    let leafMove = tree c0' c1 c2 c3' true
    // g1 leaves g0 for the root (a container move with its subtree).
    let containerMove = tree c0 c1 c2 c3 false
    // g0 moves UNDER g1 while g1 moves to the root (g0 is g1's ancestor in
    // `a`, so the two moves are applyable only with g1's first).
    let cycle =
        let g0Node = group "g0" c0

        group "root" [ group "g1" (c1 @ [ g0Node ]); group "g2" c2; group "g3" c3 ]

    [ "synthetic/leaf-move", a, leafMove
      "synthetic/container-move", a, containerMove
      "synthetic/nest-inversion", a, cycle
      "synthetic/identical", a, a ]

/// The closed enum vocabularies (`enum-tokens.json`): token → the other
/// tokens of the same enum, so a mutation can swap a variant for a sibling.
let private enumSiblings () : Map<string, string list> =
    let root = requireCorpus ()

    use doc =
        JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "enum-tokens.json")))

    [ for e in doc.RootElement.GetProperty("enums").EnumerateArray() do
          let tokens =
              [ for c in e.GetProperty("cases").EnumerateArray() -> c.GetProperty("token").GetString() |> nonNull ]

          for t in tokens -> t, tokens |> List.filter ((<>) t) ]
    |> List.groupBy fst
    |> List.map (fun (t, xs) -> t, xs |> List.collect snd |> List.distinct)
    |> Map.ofList

/// One-field mutations of a node fixture, made on its JSON so every field of
/// every kind is reached without a per-kind list: each int +1, each bool
/// flipped, each float +0.5, each enum token swapped for a sibling token, each
/// other string extended. Ids and `$type` discriminators are left alone; a
/// variant that no longer decodes, or that changed the root id, is dropped.
let private fieldMutations (siblings: Map<string, string list>) (json: string) : (string * Node<obj>) list =
    // The corpus holds depth-limit fixtures deeper than System.Text.Json's
    // default 64; the decoder's own limits decide those, not this reader.
    let deep = 4096

    let root =
        JsonNode.Parse(json, System.Nullable(), JsonDocumentOptions(MaxDepth = deep))
        |> nonNull

    let writeOptions = JsonSerializerOptions(MaxDepth = deep)

    let rec paths (node: JsonNode) (path: string list) : (string list * JsonValue) list =
        match node with
        | :? JsonObject as o ->
            [ for KeyValue(k, v) in o do
                  // `Null` / `NonNull` rather than a `null` arm and `nonNull`: whether a `null`
                  // arm narrows the arms after it differs between F# compiler versions (one
                  // reports FS3261 without the assertion, a later one FS3262 with it), and the
                  // active patterns narrow on both.
                  match v with
                  | Null -> ()
                  | NonNull _ when k = "id" || k = "$type" -> ()
                  | NonNull v -> yield! paths v (path @ [ k ]) ]
        | :? JsonArray as a ->
            [ for i in 0 .. a.Count - 1 do
                  match a.[i] with
                  | Null -> ()
                  | NonNull v -> yield! paths v (path @ [ string i ]) ]
        | :? JsonValue as v -> [ path, v ]
        | _ -> []

    // Each replacement is raw JSON text, parsed into a fresh node per use.
    let replacements (v: JsonValue) : string list =
        let raw = v.ToJsonString()

        match v.GetValueKind() with
        | JsonValueKind.Number when raw |> Seq.forall (fun c -> System.Char.IsDigit c || c = '-') ->
            [ string (System.Int64.Parse raw + 1L) ]
        | JsonValueKind.Number ->
            [ (System.Double.Parse(raw, System.Globalization.CultureInfo.InvariantCulture)
               + 0.5)
                  .ToString("R", System.Globalization.CultureInfo.InvariantCulture) ]
        | JsonValueKind.True -> [ "false" ]
        | JsonValueKind.False -> [ "true" ]
        | JsonValueKind.String ->
            let s = v.GetValue<string>()

            match Map.tryFind s siblings with
            | Some others -> others |> List.truncate 2 |> List.map JsonSerializer.Serialize
            | None -> [ JsonSerializer.Serialize(s + "~") ]
        | _ -> []

    let setAt (tree: JsonNode) (path: string list) (value: JsonNode) =
        let parent =
            path
            |> List.take (path.Length - 1)
            |> List.fold
                (fun (n: JsonNode) k ->
                    match n with
                    | :? JsonArray as a -> a.[int k] |> nonNull
                    | o -> o.[k] |> nonNull)
                tree

        let last = List.last path

        match parent with
        | :? JsonArray as a -> a.[int last] <- value
        | p -> p.[last] <- value

    let rootId =
        root.["id"] |> Option.ofObj |> Option.map (fun n -> n.GetValue<string>())

    [ for path, v in paths root [] do
          for replacement in replacements v do
              let copy = root.DeepClone()
              setAt copy path (JsonNode.Parse replacement |> nonNull)

              match JsonDecode.decodeNode (copy.ToJsonString writeOptions) with
              | Ok tree ->
                  let n = WireTree.reify tree

                  if Some n.Id = rootId then
                      yield String.concat "." path, n
              | Error _ -> () ]

/// Every (label, a, b) pair the law runs over.
let private corpusPairs () : (string * Node<obj> * Node<obj>) list =
    let nodes = nodeFixtures ()
    let arr = nodes |> Array.ofList
    let ops = opFixtures ()

    let reroot (target: Node<obj>) (n: Node<obj>) = { n with Id = target.Id }

    let selfPairs = [ for name, n in nodes -> $"self/{name}", n, n ]

    let sameKind =
        [ for i in 0 .. arr.Length - 1 do
              for j in 0 .. arr.Length - 1 do
                  let (ni, a), (nj, b) = arr.[i], arr.[j]

                  if i <> j && kindName a.Kind = kindName b.Kind then
                      yield $"kind/{ni}->{nj}", a, reroot a b ]

    let neighbours =
        [ for i in 0 .. arr.Length - 1 do
              let (ni, a), (nj, b) = arr.[i], arr.[(i + 1) % arr.Length]
              yield $"next/{ni}->{nj}", a, reroot a b ]

    let perturbed =
        [ for name, n in nodes do
              for opName, op in ops do
                  match Apply.apply op n with
                  | Ok n' when n'.Id = n.Id ->
                      yield $"op/{name}+{opName}", n, n'
                      yield $"op/{name}-{opName}", n', n
                  | _ -> () ]

    let goldens =
        [ for label, a, b in goldenPairs () do
              yield label, a, b
              yield label + "/reversed", b, a ]

    let mutated =
        let root = requireCorpus ()
        let siblings = enumSiblings ()

        [ for name, n in nodes do
              let json = File.ReadAllText(Path.Combine(root, "nodes", name))

              for path, n' in fieldMutations siblings json do
                  yield $"field/{name}@{path}", n, n'
                  yield $"field/{name}@{path}/reversed", n', n ]

    let synthetic = syntheticTrees (nodes |> List.map snd |> List.filter isLeaf)

    selfPairs @ sameKind @ neighbours @ perturbed @ mutated @ goldens @ synthetic

let private encodeOps (ops: TreeOp<obj> list) : string =
    ops |> List.map CanonicalJson.encodeOp |> String.concat "\n"

/// The op's union case name (reflection over the case tag — `%A` would render
/// every node an op carries, which dominated this suite's run time).
let private opFamily: TreeOp<obj> -> string =
    let cases = FSharpType.GetUnionCases(typeof<TreeOp<obj>>) |> Array.map _.Name
    let tag = FSharpValue.PreComputeUnionTagReader(typeof<TreeOp<obj>>)
    fun op -> cases.[tag (box op)]

let private roundTrips (a: Node<obj>) (b: Node<obj>) (ops: TreeOp<obj> list) : bool =
    let folded =
        ops
        |> List.fold
            (fun (t: Node<obj> option) op -> t |> Option.bind (fun t -> Apply.apply op t |> Result.toOption))
            (Some a)

    match folded with
    | Some t -> CanonicalJson.encodeNode t = CanonicalJson.encodeNode b
    | None -> false

let rec private flatten (op: TreeOp<obj>) : TreeOp<obj> list =
    match op with
    | TreeOp.Batch inner -> inner |> List.collect flatten
    | other -> [ other ]

/// The golden scripts of the cross-host section of `diff/goldens.json`, each
/// op re-encoded canonically from the corpus bytes.
let private goldenScripts () : (string * Node<obj> * Node<obj> * string list) list =
    let root = requireCorpus ()

    use doc =
        JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "diff", "goldens.json")))

    [ for entry in doc.RootElement.GetProperty("goldens").EnumerateArray() do
          let id = entry.GetProperty("id").GetString() |> nonNull

          let node (field: string) =
              decodeNodeOrFail $"diff/{id}.{field}" (entry.GetProperty(field).GetRawText())

          let ops =
              [ for op in entry.GetProperty("ops").EnumerateArray() ->
                    match JsonDecode.decodeOp (op.GetRawText()) with
                    | Ok decoded -> CanonicalJson.encodeOp decoded
                    | Error e -> failtestf "diff/%s ops do not decode: %A" id e ]

          yield id, node "before", node "after", ops ]

/// The structural half of a script: every op but the inserts and the content
/// ops, as canonical bytes, in order.
// ─── generated pairs ────────────────────────────────────────────────────────

/// A drawn tree of boxes and leaves, and a drawn edit of it: survivors moved
/// across parents (into new boxes too), removed regions, new regions, reorders,
/// text edits, and kind swaps in both directions between leaf and container —
/// so a survivor can become a container that receives moved and new children,
/// or a leaf whose children leave first. Ids are unique per tree.
let private generatedPairs (seed: int) (count: int) : (string * Node<obj> * Node<obj>) list =
    let rng = System.Random(seed)
    let leaf (id: string) (text: string) : Node<obj> = Fuaran.markdown id text

    let box (id: string) (kids: Node<obj> list) : Node<obj> =
        Fuaran.stack id { Defaults.stack with Children = kids }

    let rec draw (prefix: string) (depth: int) : Node<obj> =
        if depth = 0 || rng.Next 3 = 0 then
            leaf prefix $"text {prefix}"
        else
            box prefix [ for i in 0 .. rng.Next 4 -> draw $"{prefix}.{i}" (depth - 1) ]

    let kids (n: Node<obj>) =
        Introspect.getChildren n.Kind |> Option.defaultValue []

    let rec flat (n: Node<obj>) : Node<obj> list = n :: (kids n |> List.collect flat)

    /// Rebuild `after` from `before`'s nodes: every non-root node is kept, dropped,
    /// or re-homed under a drawn container; then each container's list is drawn
    /// in a new order, a few new nodes join, and some nodes change content.
    let edit (before: Node<obj>) (round: int) : Node<obj> =
        let nodes = flat before |> List.tail
        let mutable fresh = 0

        let newId () =
            fresh <- fresh + 1
            $"new{round}.{fresh}"

        // Drawn shape of the new tree: a parent for every kept node, by id.
        let kept = nodes |> List.filter (fun _ -> rng.Next 6 <> 0)

        let containers =
            before.Id :: (kept |> List.filter (fun n -> rng.Next 3 = 0) |> List.map _.Id)
            |> Array.ofList

        // A node never lands under itself or its own new descendant: place each
        // kept node under a container drawn from those placed before it.
        let placed = System.Collections.Generic.List<string * string>()
        let order = kept |> List.sortBy (fun _ -> rng.Next())
        let homes = System.Collections.Generic.Dictionary<string, string>()
        let seen = System.Collections.Generic.HashSet<string>([ before.Id ])

        for n in order do
            let candidates = containers |> Array.filter seen.Contains
            let home = candidates.[rng.Next candidates.Length]
            homes.[n.Id] <- home
            placed.Add((n.Id, home))
            seen.Add n.Id |> ignore

        let byId = nodes |> List.map (fun n -> n.Id, n) |> Map.ofList
        let isContainer (id: string) = Array.contains id containers

        let rec build (id: string) : Node<obj> =
            let under =
                [ for KeyValue(child, home) in homes do
                      if home = id then
                          yield child ]
                |> List.sortBy (fun _ -> rng.Next())
                |> List.map build

            let extra =
                [ for _ in 1 .. (if rng.Next 4 = 0 then 1 + rng.Next 2 else 0) -> draw (newId ()) 1 ]

            let content =
                match Map.tryFind id byId with
                | Some _ when rng.Next 5 = 0 -> $"edited {id}"
                | _ -> $"text {id}"

            if id = before.Id then box id (under @ extra)
            elif isContainer id then box id (under @ extra)
            else leaf id content

        build before.Id

    [ for i in 1..count do
          let before = box "root" [ for j in 0 .. rng.Next 5 -> draw $"r{j}" 3 ]
          yield $"generated/{seed}/{i}", before, edit before i ]

let private relocations (ops: TreeOp<obj> list) : string list =
    ops
    |> List.collect flatten
    |> List.filter (function
        | TreeOp.MoveNode _
        | TreeOp.RemoveNode _
        | TreeOp.ReorderChildren _ -> true
        | _ -> false)
    |> List.map CanonicalJson.encodeOp

[<Tests>]
let tests =
    testList
        "TreeOpDiff is Core's Diff, lowered (Phase 2304)"
        [ test "the diff emits every cross-host golden script byte for byte" {
              // `diff/goldens.json` pins the scripts every conformant host's diff
              // emits for these pairs; the style entries (Phase 2314) are the
              // field-level style edit's contract on every host.
              let scripts = goldenScripts ()
              Expect.isGreaterThan (List.length scripts) 9 "the cross-host section is present"

              for id, before, after, expected in scripts do
                  Expect.equal
                      (TreeOpDiff.diff before after |> List.map CanonicalJson.encodeOp)
                      expected
                      $"diff/{id}: the emitted script is the golden script"
          }

          test "the structural script is Core's toOpsContainedWith, op for op" {
              // The lowering adds nothing structural and drops nothing: every
              // move, removal and reorder is the one Core's diff emits over the
              // tier's witness, in Core's order.
              let mutable compared = 0

              let mismatches =
                  [ for label, a, b in corpusPairs () do
                        if a.Id = b.Id then
                            match
                                Fuaran.Core.Diff.toOpsContainedWith
                                    NodeIndex.holdsList
                                    CanonicalJson.encodeNode
                                    NodeIndex.witness<obj>
                                    Introspect.idWitness
                                    a
                                    b
                            with
                            | Ok script ->
                                compared <- compared + 1

                                let core =
                                    script
                                    |> List.choose (function
                                        | Fuaran.Core.SkeletonOp.MoveNode(t, p) ->
                                            Some(CanonicalJson.encodeOp (TreeOp.MoveNode(t, p)))
                                        | Fuaran.Core.SkeletonOp.RemoveNode t ->
                                            Some(CanonicalJson.encodeOp (TreeOp.RemoveNode t))
                                        | Fuaran.Core.SkeletonOp.ReorderChildren(p, o) ->
                                            Some(CanonicalJson.encodeOp (TreeOp.ReorderChildren(p, o)))
                                        | _ -> None)

                                if relocations (TreeOpDiff.diff a b) <> core then
                                    yield label
                            | Error _ -> () ]

              Expect.isEmpty mismatches $"every pair's structure is Core's ({List.length mismatches} differ)"
              Expect.isGreaterThan compared 500 $"at least 500 pairs compared ({compared})"
          }

          test "a relocation is a MoveNode: no script re-sends a node the old tree holds" {
              // The identity-preserving property Core's diff carries: a survivor
              // is moved, never removed and inserted again, so no `InsertChild`
              // payload carries an id of the old tree.
              let resent =
                  [ for label, a, b in corpusPairs () do
                        if a.Id = b.Id then
                            let held = Introspect.allNodeIds a |> Set.ofList

                            let inserted =
                                TreeOpDiff.diff a b
                                |> List.collect flatten
                                |> List.collect (function
                                    | TreeOp.InsertChild(_, child) -> Introspect.allNodeIds child
                                    | _ -> [])

                            if inserted |> List.exists held.Contains then
                                yield label ]

              Expect.isEmpty resent (sprintf "no pair re-sends a survivor: %A" (List.truncate 20 resent))
          }

          test "the applicability law over generated pairs: every script lands on its after tree, moving every survivor" {
              // The law Core proves for `toOpsContainedWith`, instantiated over the tier's
              // witnesses and its lowering, on drawn pairs the corpus does not hold: survivors
              // re-homed under new and changed containers, leaves becoming containers and
              // back, removed regions whose survivors leave first.
              let pairs = generatedPairs 20262304 400

              let failures =
                  [ for label, a, b in pairs do
                        let ops = TreeOpDiff.diff a b
                        let held = Introspect.allNodeIds a |> Set.ofList

                        let resent =
                            ops
                            |> List.collect flatten
                            |> List.exists (function
                                | TreeOp.InsertChild(_, child) ->
                                    Introspect.allNodeIds child |> List.exists held.Contains
                                | _ -> false)

                        if not (roundTrips a b ops) || resent then
                            yield label ]

              Expect.isEmpty failures (sprintf "every generated pair: %A" (List.truncate 20 failures))

              let moves =
                  pairs
                  |> List.sumBy (fun (_, a, b) ->
                      TreeOpDiff.diff a b
                      |> List.filter (function
                          | TreeOp.MoveNode _ -> true
                          | _ -> false)
                      |> List.length)

              Expect.isGreaterThan moves 400 $"the pairs relocate survivors ({moves} moves)"
          }

          test "the round-trip law holds over every pair the tier's previous diff round-tripped" {
              // `diff` cannot express an accessibility edit and some re-rooted
              // pairs collide ids; the law is that Core's script lost no
              // round-trip the tier's own algorithm had.
              let mutable trips = 0

              let regressions =
                  [ for label, a, b in corpusPairs () do
                        let shipped = roundTrips a b (TreeOpDiff.diff a b)

                        if shipped then
                            trips <- trips + 1

                        if roundTrips a b (TreeOpDiffPre2063.diff a b) && not shipped then
                            yield label ]

              Expect.isEmpty regressions (sprintf "no pair regresses: %A" (List.truncate 20 regressions))
              Expect.isGreaterThan trips 10000 $"the law is not vacuous ({trips} pairs round-trip)"
          }

          test "the corpus reaches every op family the diff emits" {
              let pairs = corpusPairs ()
              let mutable families = Set.empty
              let mutable nonEmpty = 0

              for _, a, b in pairs do
                  let ops = TreeOpDiff.diff a b
                  families <- ops |> List.map opFamily |> Set.ofList |> Set.union families

                  if not ops.IsEmpty then
                      nonEmpty <- nonEmpty + 1

              for family in
                  [ "UpdateProp"
                    "ReplaceBinding"
                    "EditNode"
                    "UpdateState"
                    // Phase 2291 - a style change is field-level; the whole-record
                    // `UpdateStyle` only when all six fields move, which the
                    // `style-all-fields` golden pins (Phase 2314).
                    "PatchStyle"
                    "UpdateStyle"
                    "InsertChild"
                    "RemoveNode"
                    "ReorderChildren"
                    "MoveNode"
                    // A changed root id: the cross-host goldens' `ReplaceRoot`.
                    "ReplaceRoot" ] do
                  Expect.isTrue (families.Contains family) $"the run reached a {family} op (families seen: {families})"

              Expect.isGreaterThan nonEmpty 500 $"at least 500 pairs carry a change ({nonEmpty} of {List.length pairs})"
          }

          test "a nest inversion is two moves, each applyable in turn" {
              // `g0` moves UNDER `g1` while `g1` leaves `g0` for the root. The
              // tier's own diff could only move `g1` and re-sent `g0`; Core moves
              // `g1` out first, so `g0`'s move no longer nests it under itself.
              let leaves = nodeFixtures () |> List.map snd |> List.filter isLeaf

              let _, a, b =
                  syntheticTrees leaves
                  |> List.find (fun (l, _, _) -> l = "synthetic/nest-inversion")

              let ops = TreeOpDiff.diff a b

              let moves =
                  ops
                  |> List.choose (function
                      | TreeOp.MoveNode(NodeId id, NodeId parent) -> Some(id, parent)
                      | _ -> None)

              Expect.equal moves [ "g1", "root"; "g0", "g1" ] "g1 moves to the root, then g0 under g1"
              Expect.isTrue (roundTrips a b ops) "and the script lands on the new tree"
          }

          test "the structural five undo through Core's Ops.invert: apply (inverse op pre) (apply op pre) = pre" {
              let isStructural =
                  function
                  | TreeOp.InsertChild _
                  | TreeOp.RemoveNode _
                  | TreeOp.MoveNode _
                  | TreeOp.ReorderChildren _ -> true
                  | _ -> false

              let mutable undone = 0

              let failures =
                  [ for name, n in nodeFixtures () do
                        for opName, op in opFixtures () do
                            if isStructural op then
                                match Apply.apply op n with
                                | Ok post ->
                                    undone <- undone + 1

                                    if not (roundTrips post n (TreeOpDiff.inverse op n)) then
                                        yield $"{name}+{opName}"
                                | Error _ -> () ]

              Expect.isEmpty failures (sprintf "every structural op is undone: %A" (List.truncate 20 failures))
              Expect.isGreaterThan undone 0 $"the law is not vacuous ({undone} structural ops undone)"

              // The inverse is Core's, not a diff back: removing a child that is not
              // last restores it by Core's one `Batch` of the re-insert and the order.
              let tree =
                  Fuaran.stack
                      "s"
                      { Defaults.stack with
                          Children = [ Fuaran.markdown "x" "X"; Fuaran.markdown "y" "Y" ] }

              match TreeOpDiff.inverse (TreeOp.RemoveNode(NodeId "x")) tree with
              | [ TreeOp.Batch [ TreeOp.InsertChild(NodeId "s", restored); TreeOp.ReorderChildren(NodeId "s", order) ] ] ->
                  Expect.equal restored.Id "x" "the removed subtree is re-inserted"
                  Expect.equal order [ NodeId "x"; NodeId "y" ] "and put back where it stood"
              | other -> failtestf "expected Core's Batch inverse, got %A" (other |> List.map CanonicalJson.encodeOp)
          } ]
