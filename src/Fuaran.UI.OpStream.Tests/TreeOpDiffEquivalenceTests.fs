module Fuaran.UI.OpStream.Tests.TreeOpDiffEquivalenceTests

// ─── Phase 2063: the diff encodes each node once, and emits the same bytes ───
//
// Phase 2063 changed what `TreeOpDiff.diff` COSTS — one childless-shell encode
// per node instead of whole-subtree encodes per candidate field, structural
// comparison for closure-free injective fields, a shared node index for the
// move pre-pass — and nothing about what it EMITS. This suite holds it to that:
// over the wire corpus it runs the shipped diff and the pre-2063 oracle
// (`TreeOpDiffPre2063`, a verbatim copy) on the same pairs and asserts the
// canonical encodings of the two op lists are byte-identical, and that each
// list still round-trips through the apply engine.
//
// The pairs: every node fixture against itself, against every other fixture of
// the same root kind (re-rooted at its id), and against its cyclic neighbour of
// any kind; every node fixture against each `ops/` fixture that applies to it,
// in both directions; every `diff/goldens.json` before/after pair, in both
// directions; and a synthetic tree built from the corpus's leaves with
// cross-parent moves, a container move, and a move the cycle check must refuse.
// A coverage assertion keeps the law from being vacuous: the run must reach
// every op family the diff can emit.

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
            "wire-format-fixtures/ not found walking up from the test assembly — the Phase 2063 equivalence law runs over the corpus (set FUARAN_WIRE_FIXTURES in a worktree)"

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
    // g0 moves UNDER g1 while g1 moves to the root: the cycle check must refuse
    // g0's move (g0 is g1's ancestor in `a`) and admit g1's.
    let cycle =
        let g0Node = group "g0" c0

        group "root" [ group "g1" (c1 @ [ g0Node ]); group "g2" c2; group "g3" c3 ]

    [ "synthetic/leaf-move", a, leafMove
      "synthetic/container-move", a, containerMove
      "synthetic/cycle-refused", a, cycle
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
                  match v with
                  | null -> ()
                  | v when k = "id" || k = "$type" -> ()
                  | v -> yield! paths (nonNull v) (path @ [ k ]) ]
        | :? JsonArray as a ->
            [ for i in 0 .. a.Count - 1 do
                  match a.[i] with
                  | null -> ()
                  | v -> yield! paths (nonNull v) (path @ [ string i ]) ]
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

[<Tests>]
let tests =
    testList
        "TreeOpDiff encodes each node once (Phase 2063)"
        [ test "the shipped diff and the pre-2063 oracle emit byte-identical op lists over the corpus" {
              let pairs = corpusPairs ()
              let mutable families = Set.empty
              let mutable nonEmpty = 0

              let mismatches =
                  [ for label, a, b in pairs do
                        let shipped = TreeOpDiff.diff a b
                        let oracle = TreeOpDiffPre2063.diff a b
                        families <- shipped |> List.map opFamily |> Set.ofList |> Set.union families

                        if not shipped.IsEmpty then
                            nonEmpty <- nonEmpty + 1

                        if encodeOps shipped <> encodeOps oracle then
                            yield label ]

              Expect.isEmpty
                  mismatches
                  $"every pair diffs to the same bytes as the pre-2063 oracle ({List.length mismatches} of {List.length pairs} differ)"

              // Not vacuous: the corpus reaches every op family the diff emits.
              for family in
                  [ "UpdateProp"
                    "ReplaceBinding"
                    "EditNode"
                    "UpdateState"
                    "UpdateStyle"
                    "InsertChild"
                    "RemoveNode"
                    "ReorderChildren"
                    "MoveNode" ] do
                  Expect.isTrue
                      (families.Contains family)
                      $"the equivalence run reached a {family} op (families seen: {families})"

              Expect.isGreaterThan nonEmpty 500 $"at least 500 pairs carry a change ({nonEmpty} of {List.length pairs})"
          }

          test "the round-trip law holds over the same pairs where the oracle's did" {
              // `diff` cannot express an accessibility edit and some re-rooted
              // pairs collide ids; the law is that 2063 lost no round-trip the
              // oracle had — not that every pair round-trips.
              let regressions =
                  [ for label, a, b in corpusPairs () do
                        if
                            roundTrips a b (TreeOpDiffPre2063.diff a b)
                            && not (roundTrips a b (TreeOpDiff.diff a b))
                        then
                            yield label ]

              Expect.isEmpty regressions "no pair that round-tripped before Phase 2063 fails to round-trip after it"
          }

          test "the cycle check admits the safe move and refuses the cyclic one" {
              let leaves = nodeFixtures () |> List.map snd |> List.filter isLeaf

              let _, a, b =
                  syntheticTrees leaves
                  |> List.find (fun (l, _, _) -> l = "synthetic/cycle-refused")

              let moves =
                  TreeOpDiff.diff a b
                  |> List.choose (function
                      | TreeOp.MoveNode(NodeId id, NodeId parent) -> Some(id, parent)
                      | _ -> None)

              Expect.equal moves [ "g1", "root" ] "g1 moves to the root; g0's move under its own descendant is refused"
          } ]
