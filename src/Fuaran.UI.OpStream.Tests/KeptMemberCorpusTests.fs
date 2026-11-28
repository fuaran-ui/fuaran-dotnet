module Fuaran.UI.OpStream.Tests.KeptMemberCorpusTests

// ─── Phase 2880: the reference host keeps an undeclared member ──────────────
//
// WIRE_FORMAT §15.8 (D45, ruled 2026-11-28). This suite certifies the reference host
// against the corpus family `kept-member/` and the diff family `diff/keptMember`, which
// `host-capability-manifest.json` declares covered:
//   - decode: a node round-trips with its kept members at their ordinal places;
//   - ops: a decoded op round-trips with its own slot (`decodeOpKept` / `encodeOpKept`);
//   - apply: each op moves kept members as §15.8 rules (`Apply.applyKept`);
//   - policy: under a narrower policy a kept `$type` value is refused at the member;
//   - reject: the enumerated refusals stay refusals;
//   - validate: each kept member is one FUARAN175 warning, never an error;
//   - diff: the floors, script for script, and the round-trip law.
// Beside the corpus: a property test per op family — apply then encode keeps every
// entry not under a member the op writes — over every `ops/` fixture that applies to a
// `nodes/` fixture carrying a kept member in every object it declares, and a test that a
// kept member does not pass a gate that refuses it declared.

open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open Expecto
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.KindPolicy
open Fuaran.UI.PreEmitValidate
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.OpStream.Replay

let private requireCorpus () : string =
    match Fuaran.Tests.CorpusRoot.tryFind () with
    | Some root -> root
    | None ->
        skiptest
            "wire-format-fixtures/ not found walking up from the test assembly — the kept-member family runs over the corpus (set FUARAN_WIRE_FIXTURES in a worktree)"

let private family (root: string) : JsonElement =
    use doc =
        JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "kept-member", "kept-member-vectors.json")))

    doc.RootElement.Clone()

let private str (e: JsonElement) (name: string) : string =
    e.GetProperty(name).GetString() |> nonNull

let private decodeOrFail (what: string) (json: string) : Node<obj> =
    match JsonDecode.decodeNodeObj json with
    | Ok n -> n
    | Error e -> failtestf "%s did not decode: %s at '%s' — %s" what e.Code e.Path e.Message

let private decodeOpOrFail (what: string) (json: string) : KeptOp<obj> =
    match JsonDecode.decodeOpKept json with
    | Ok op -> op
    | Error e -> failtestf "%s did not decode: %s at '%s' — %s" what e.Code e.Path e.Message

let private policyNamed (name: string) : DecodePolicy =
    match name with
    | "admit-all" -> DecodePolicy.admitAll
    | "closed-no-escape-hatches" -> JsonDecode.Policy.closedProfile
    | other -> failtestf "kept-member names an unknown decode-policy/ policy '%s'" other

let private applyCode (code: ApplyErrorCode) : string = sprintf "%A" code

/// Every node of a tree, the root first.
let rec private allNodes (n: Node<'Msg>) : Node<'Msg> list =
    n :: (Introspect.descendantNodes n |> List.collect allNodes)

/// Every kept entry of a tree, by owning node id.
let private entriesOf (n: Node<'Msg>) : Set<string * PathSegment list * string> =
    allNodes n
    |> List.collect (fun node ->
        Fuaran.UI.HostPrelude.KeptMembers.entries node.KeptMembers
        |> List.map (fun (p, v) -> node.Id, p, Canonical.write Rendering.CrossHost v))
    |> Set.ofList

let private corpusTests =
    testList
        "kept-member corpus family (WIRE_FORMAT 15.8)"
        [ test "decode vectors round-trip with their kept members in place" {
              let fam = family (requireCorpus ())

              for v in fam.GetProperty("decode").EnumerateArray() do
                  let id = str v "id"
                  let node = decodeOrFail $"kept-member decode/{id}" (str v "input")
                  Expect.equal (CanonicalJson.encodeNode node) (str v "canonical") $"decode/{id}"
          }

          test "op vectors round-trip with the op's own kept members" {
              let fam = family (requireCorpus ())

              for v in fam.GetProperty("ops").EnumerateArray() do
                  let id = str v "id"
                  let op = decodeOpOrFail $"kept-member ops/{id}" (str v "input")
                  Expect.equal (CanonicalJson.encodeOpKept op) (str v "canonical") $"ops/{id}"
          }

          test "apply vectors move kept members as 15.8 rules" {
              let fam = family (requireCorpus ())

              for v in fam.GetProperty("apply").EnumerateArray() do
                  let id = str v "id"
                  let tree = decodeOrFail $"apply/{id}.tree" (str v "tree")
                  let op = decodeOpOrFail $"apply/{id}.op" (str v "op")
                  let expected = v.GetProperty("expected")

                  match str expected "verdict", Apply.applyKept op tree with
                  | "reject", Error e -> Expect.equal (applyCode e.Code) (str expected "code") $"apply/{id} code"
                  | "accept", Ok after ->
                      Expect.equal (CanonicalJson.encodeNode after) (str expected "tree") $"apply/{id} tree"
                  | verdict, outcome -> failtestf "apply/%s: expected %s, got %A" id verdict outcome
          }

          test "policy vectors refuse a kept $type value at the member under a narrower policy" {
              let fam = family (requireCorpus ())

              for v in fam.GetProperty("policy").EnumerateArray() do
                  let id = str v "id"

                  match
                      str v "outcome", JsonDecode.decodeNodeObjWithPolicy (policyNamed (str v "policy")) (str v "input")
                  with
                  | "refuse", Error e ->
                      Expect.equal e.Code (str v "expectedErrorCode") $"policy/{id} code"
                      Expect.equal e.Path (str v "expectedPath") $"policy/{id} path"
                      Expect.stringContains e.Message (str v "policy") $"policy/{id}: the refusal names its policy"
                  | "admit", Ok node -> Expect.equal (CanonicalJson.encodeNode node) (str v "canonical") $"policy/{id}"
                  | outcome, result -> failtestf "policy/%s: expected %s, got %A" id outcome result
          }

          test "the enumerated refusals stay refusals" {
              let root = requireCorpus ()
              let fam = family root

              for v in fam.GetProperty("reject").EnumerateArray() do
                  let id = str v "id"
                  let text = File.ReadAllText(Path.Combine(root, "kept-member", str v "document"))

                  let outcome =
                      match v.TryGetProperty("decoder") with
                      | true, d when d.GetString() = "op" -> JsonDecode.decodeOpKept text |> Result.map ignore
                      | _ -> JsonDecode.decodeNodeObj text |> Result.map ignore

                  match outcome with
                  | Error e ->
                      Expect.equal e.Code (str v "expectedErrorCode") $"reject/{id} code"
                      Expect.equal e.Path (str v "expectedPath") $"reject/{id} path"
                  | Ok() -> failtestf "reject/%s was accepted" id
          }

          test "the validator reports each kept member as one FUARAN175 warning" {
              let fam = family (requireCorpus ())

              for v in fam.GetProperty("validate").EnumerateArray() do
                  let id = str v "id"
                  let node = decodeOrFail $"validate/{id}" (str v "input")

                  let defects =
                      match PreEmitValidate.validate node with
                      | Ok() -> []
                      | Error ds -> ds

                  let kept =
                      defects
                      |> List.choose (fun d ->
                          match d with
                          | PreEmitDefect.KeptMember(nodeId, path) ->
                              let code, severity, _ = PreEmitValidate.describe d
                              Expect.equal code "FUARAN175" $"validate/{id}: the one code"
                              Some(sprintf "%A" severity, nodeId, path)
                          | _ -> None)
                      |> List.sort

                  let expected =
                      [ for e in v.GetProperty("expected").EnumerateArray() ->
                            str e "severity", str e "nodeId", str e "path" ]
                      |> List.sort

                  Expect.equal kept expected $"validate/{id}"
          }

          test "diff/keptMember: the floors script for script, and the round-trip law" {
              let root = requireCorpus ()

              use doc =
                  JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "diff", "kept-member-goldens.json")))

              let mutable count = 0

              for g in doc.RootElement.GetProperty("goldens").EnumerateArray() do
                  let id = str g "id"
                  let before = decodeOrFail $"diff/{id}.before" (g.GetProperty("before").GetRawText())
                  let after = decodeOrFail $"diff/{id}.after" (g.GetProperty("after").GetRawText())

                  let expected =
                      [ for op in g.GetProperty("ops").EnumerateArray() ->
                            CanonicalJson.encodeOpKept (decodeOpOrFail $"diff/{id}.ops" (op.GetRawText())) ]

                  let script = TreeOpDiff.diffKept before after
                  Expect.equal (script |> List.map CanonicalJson.encodeOpKept) expected $"diff/{id}: the golden script"

                  let folded =
                      script
                      |> List.fold
                          (fun t op -> t |> Result.bind (Apply.applyKept op))
                          (Ok before: Result<Node<obj>, ApplyError>)

                  match folded with
                  | Ok t -> Expect.equal (CanonicalJson.encodeNode t) (CanonicalJson.encodeNode after) $"diff/{id}: law"
                  | Error e -> failtestf "diff/%s: the script does not apply: %A" id e

                  count <- count + 1

              Expect.isGreaterThan count 4 "the keptMember family is present"
          } ]

// ─── the property: apply then encode keeps every entry not under a written member ──

/// Add a member `zk<n>` to every object of a node fixture's JSON, so each object the
/// fixture declares carries one kept member (a payload object carries it as data, and a
/// closed position refuses the document, which is then skipped).
let private seeded (json: string) : string =
    let mutable n = 0

    let rec seed (node: JsonNode | null) =
        match node with
        | :? JsonObject as o ->
            for KeyValue(_, child) in List.ofSeq o do
                seed child

            n <- n + 1
            o.Add($"zk{n}", JsonValue.Create(n))
        | :? JsonArray as a ->
            for child in List.ofSeq a do
                seed child
        | _ -> ()

    let root =
        JsonNode.Parse(json, documentOptions = JsonDocumentOptions(MaxDepth = 1024))

    seed root
    (nonNull root).ToJsonString(JsonSerializerOptions(MaxDepth = 1024))

/// What `op` may legitimately take away: a predicate over (owning node id, entry path).
let rec private mayDrop (tree: Node<obj>) (op: TreeOp<obj>) : string -> PathSegment list -> bool =
    let under (prefix: PathSegment list) (p: PathSegment list) =
        List.length p >= List.length prefix && List.truncate prefix.Length p = prefix

    let onTarget (NodeId target) (test: PathSegment list -> bool) =
        fun (id: string) (p: PathSegment list) -> id = target && test p

    let lower (s: string) =
        if s = "" then
            s
        else
            string (System.Char.ToLowerInvariant s[0]) + s.Substring 1

    match op with
    | TreeOp.EditNode(target, _) -> onTarget target (under [ PathSegment.Key "kind" ])
    | TreeOp.UpdateProp(target, path, _) ->
        // The written member and everything below it (the path's first segment, in the
        // wire's spelling, inside the kind).
        let head = path.Split([| '.'; '[' |]).[0] |> lower

        let head =
            if head = "kind" && path.Contains "." then
                path.Split('.').[1].Split('[').[0] |> lower
            else
                head

        onTarget target (fun p ->
            match p with
            | PathSegment.Key "kind" :: PathSegment.Key k :: _ ->
                System.String.Equals(k, head, System.StringComparison.OrdinalIgnoreCase)
            | _ -> false)
    | TreeOp.ReplaceBinding(target, slot, _) ->
        onTarget target (fun p ->
            match p with
            | PathSegment.Key "kind" :: PathSegment.Key k :: _ ->
                System.String.Equals(k, slot, System.StringComparison.OrdinalIgnoreCase)
            | _ -> false)
    | TreeOp.UpdateStyle(target, _) -> onTarget target (under [ PathSegment.Key "style" ])
    | TreeOp.UpdateState(target, _) ->
        // The state arms are nodes: replacing the state replaces them, with their entries.
        let (NodeId t) = target

        let arms =
            Introspect.findNode target tree
            |> Option.bind _.State
            |> Option.map (fun s -> [ s.OnLoading; s.OnEmpty ] |> List.choose id |> List.collect allNodes)
            |> Option.defaultValue []
            |> List.map _.Id
            |> Set.ofList

        fun id p -> (id = t && under [ PathSegment.Key "state" ] p) || arms.Contains id
    | TreeOp.UpdateAccessibility(target, _) -> onTarget target (under [ PathSegment.Key "accessibility" ])
    | TreeOp.UpdateTooltip(target, _) -> onTarget target (under [ PathSegment.Key "tooltip" ])
    | TreeOp.RemoveNode target ->
        let gone =
            Introspect.findNode target tree
            |> Option.map (allNodes >> List.map _.Id >> Set.ofList)
            |> Option.defaultValue Set.empty

        fun id _ -> gone.Contains id
    | TreeOp.ReplaceRoot _ -> fun _ _ -> true
    | TreeOp.Batch inner ->
        // Each inner op judged against the tree it met.
        let rec judges (t: Node<obj>) (ops: TreeOp<obj> list) acc =
            match ops with
            | [] -> acc
            | o :: rest ->
                let acc = mayDrop t o :: acc

                match Apply.apply o t with
                | Ok t' -> judges t' rest acc
                | Error _ -> acc

        let all = judges tree inner []
        fun id p -> all |> List.exists (fun f -> f id p)
    | TreeOp.PatchStyle _
    | TreeOp.InsertChild _
    | TreeOp.MoveNode _
    | TreeOp.ReorderChildren _ -> fun _ _ -> false

let private familyOf (op: TreeOp<obj>) : string =
    (sprintf "%A" op).Split([| ' '; '\n'; '(' |]).[0]

let private propertyTests =
    testList
        "apply then encode keeps every kept entry not under a written member"
        [ test "every ops/ fixture over every seeded nodes/ fixture it applies to" {
              let root = requireCorpus ()

              let trees =
                  Directory.GetFiles(Path.Combine(root, "nodes"), "*.json")
                  |> Array.sort
                  |> Array.choose (fun f ->
                      match JsonDecode.decodeNodeObj (seeded (File.ReadAllText f)) with
                      | Ok n when not (entriesOf n).IsEmpty -> Some(Path.GetFileName f, n)
                      | _ -> None)

              Expect.isGreaterThan trees.Length 100 "most node fixtures seed cleanly"

              let ops =
                  Directory.GetFiles(Path.Combine(root, "ops"), "*.json")
                  |> Array.sort
                  |> Array.choose (fun f ->
                      match JsonDecode.decodeOpKept (File.ReadAllText f) with
                      | Ok op -> Some(Path.GetFileName f, op)
                      | Error _ -> None)

              let mutable families = Set.empty

              for treeName, tree in trees do
                  let before = entriesOf tree

                  for opName, op in ops do
                      match Apply.applyKept op tree with
                      | Error _ -> ()
                      | Ok after ->
                          families <- Set.add (familyOf op.Op) families
                          // Through the bytes: what the encoder writes is what a reader keeps.
                          let reread =
                              decodeOrFail $"{opName} over {treeName}" (CanonicalJson.encodeNode after)

                          let kept = entriesOf reread
                          let drop = mayDrop tree op.Op

                          for id, path, value in before do
                              if not (drop id path) then
                                  Expect.isTrue
                                      (kept.Contains(id, path, value))
                                      $"{opName} over {treeName}: node '{id}' lost its kept member at {path}"

              for f in [ "EditNode"; "UpdateProp"; "InsertChild"; "RemoveNode"; "Batch" ] do
                  Expect.isTrue (families.Contains f) $"the run reached {f} (families seen: {families})"
          }

          test "the families the corpus ops do not reach, by hand" {
              let tree =
                  decodeOrFail
                      "a seeded box"
                      """{"accessibility":{"role":"status","zk1":1},"id":"card","kind":{"$type":"Box","children":[{"id":"h","kind":{"$type":"Heading","level":1,"text":"Sales","zk2":2},"zk3":3},{"id":"m","kind":{"$type":"Metric","label":"Revenue","value":{"$type":"Static","value":1},"zk4":4}}],"layout":{"$type":"Flex","direction":"Vertical","zk5":5},"role":"Card","zk6":6},"state":{"onEmpty":{"id":"e","kind":{"$type":"Heading","level":2,"text":"None","zk9":9}},"zk10":10},"style":{"tone":"Brand","zk7":7},"tooltip":"Tip","zk8":8}"""

              let before = entriesOf tree
              Expect.equal before.Count 10 "every seeded member is kept"

              let ops =
                  [ """{"$type":"MoveNode","newParentId":"card","target":"h"}"""
                    """{"$type":"ReorderChildren","newOrder":["m","h"],"parentId":"card"}"""
                    """{"$type":"UpdateStyle","style":{"tone":"Info"},"target":"card"}"""
                    """{"$type":"PatchStyle","style":{"tone":"Info"},"target":"card"}"""
                    """{"$type":"UpdateState","state":{},"target":"card"}"""
                    """{"$type":"UpdateAccessibility","accessibility":{"role":"note"},"target":"card"}"""
                    """{"$type":"UpdateTooltip","target":"card","tooltip":"Other"}"""
                    """{"$type":"ReplaceBinding","binding":{"$type":"Static","value":2},"slot":"Value","target":"m"}"""
                    """{"$type":"UpdateProp","path":"Label","target":"m","value":"Cost"}"""
                    """{"$type":"ReplaceRoot","node":{"id":"r","kind":{"$type":"Heading","level":1,"text":"R"}}}""" ]

              for text in ops do
                  let op = decodeOpOrFail text text

                  match Apply.applyKept op tree with
                  | Error e -> failtestf "%s did not apply: %A" text e
                  | Ok after ->
                      let kept = entriesOf (decodeOrFail text (CanonicalJson.encodeNode after))
                      let drop = mayDrop tree op.Op

                      for id, path, value in before do
                          if not (drop id path) then
                              Expect.isTrue (kept.Contains(id, path, value)) $"{text}: node '{id}' lost {path}"
                          else
                              Expect.isFalse
                                  (kept.Contains(id, path, value))
                                  $"{text}: node '{id}' kept {path}, under a written member"
          } ]

// ─── a kept member does not pass a gate that refuses it declared ────────────

let private gateTests =
    testList
        "a kept member passes no gate that refuses it declared"
        [ test "a kind the closed profile refuses, held in a kept member, is refused" {
              // Declared, the Mount child is refused by its kind...
              let declared =
                  """{"id":"card","kind":{"$type":"Box","children":[{"id":"x","kind":{"$type":"Mount"}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Card"}}"""

              match JsonDecode.decodeNodeObjWithPolicy JsonDecode.Policy.closedProfile declared with
              | Error e -> Expect.equal e.Code "KIND_NOT_ADMITTED" "declared: refused"
              | Ok _ -> failtest "the closed profile admitted a Mount"

              // ...and kept, the same value is refused at the member, not admitted unseen.
              let kept =
                  """{"id":"card","kind":{"$type":"Box","children":[],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Card","zzz":[{"id":"x","kind":{"$type":"Mount"}}]}}"""

              match JsonDecode.decodeNodeObjWithPolicy JsonDecode.Policy.closedProfile kept with
              | Error e ->
                  Expect.equal e.Code "KIND_NOT_ADMITTED" "kept: refused"
                  Expect.equal e.Path "$.kind.zzz" "at the member"
              | Ok _ -> failtest "a kept Mount passed the closed profile"

              // An op's own kept member is gated the same way.
              match
                  JsonDecode.decodeOpKeptWithPolicy
                      JsonDecode.Policy.closedProfile
                      """{"$type":"RemoveNode","target":"x","zzz":{"$type":"Mount"}}"""
              with
              | Error e -> Expect.equal e.Path "$.zzz" "an op's kept member, at the member"
              | Ok _ -> failtest "a kept Mount on an op passed the closed profile"
          }

          test "the default decoder keeps and never refuses an undeclared member" {
              match
                  JsonDecode.decodeNodeObj
                      """{"id":"a","kind":{"$type":"Heading","level":2,"text":"Hi","zzz":{"$type":"Mount"}}}"""
              with
              | Ok n -> Expect.isFalse (Fuaran.UI.HostPrelude.KeptMembers.isEmpty n.KeptMembers) "kept"
              | Error e -> failtestf "refused: %A" e
          }

          test "no op names a kept member, and introspection never lists one" {
              let tree =
                  decodeOrFail "heading" """{"id":"a","kind":{"$type":"Heading","level":2,"text":"Hi","zzz":1}}"""

              Expect.isFalse
                  (Introspect.availableFields tree.Kind
                   |> List.exists (fun f -> f.ToLowerInvariant() = "zzz"))
                  "not a field"

              match Apply.apply (TreeOp.UpdateProp(NodeId "a", "zzz", PropValue.Wire(JInt 2))) tree with
              | Error e -> Expect.equal (applyCode e.Code) "FieldNotFound" "refused by name"
              | Ok _ -> failtest "an op wrote a kept member"
          } ]

[<Tests>]
let tests =
    testList "Phase 2880 kept members" [ corpusTests; propertyTests; gateTests ]
