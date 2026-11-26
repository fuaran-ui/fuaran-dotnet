module Fuaran.UI.JsonDecode.Tests.ValidateArtifact

// ============================================================================
//  The generated pre-emit VALIDATE family (Phase 2381) - `validate/manifest.json`,
//  a self-enumerated family (WIRE_FORMAT 27.7) whose normative sentence is
//  27.8.2's `validate` entry.
//
//  The question it answers: given a document, which pre-emit defects does it
//  carry, and on which node? `validator/` fixes the defect VOCABULARY (codes,
//  severities, message concepts); nothing in the corpus said which of those
//  codes a given document raises, so two hosts implementing the same rule could
//  disagree on a document with every coverage check green.
//
//  Each vector is a tree. Its expected defects are NOT declared: they are what
//  the reference validator (`PreEmitValidate.validate`) reports on the tree the
//  reference decoder reads, each located at the node `findingOf` names. The
//  node's location is the JSONPath of its object in the vector's canonical
//  bytes, in the 29.3 grammar (`.name` members, whatever the key, and `[i]`
//  indices); where several
//  nodes hold the id it is the first, except that the k-th FUARAN-DUP-ID
//  finding on an id is at the id's (k+1)-th node (27.8.2). So a host that
//  reports a path for a finding can be held to the spelling: its path extends
//  the vector's at a segment boundary, and `$.kind.children.0` extends nothing.
//
//  Co-emitted by `Corpus.emit`, and writable alone with `--emit-validate
//  [<dir>]`. `EmittedFamilyTests` asserts byte-equality with the committed file
//  and reads every committed vector back through the reference.
// ============================================================================

open System.IO
open System.Text.Encodings.Web
open System.Text.Json

open Fuaran.UI
open Fuaran.UI.Ops
open Fuaran.UI.OpStream.Abstractions

[<Literal>]
let artifactId = "https://fuaran.dev/wire-format/v1/validate/manifest.json"

/// The family's directory at the corpus root; the family is named by it.
[<Literal>]
let directory = "validate"

[<Literal>]
let fileName = "manifest.json"

/// One vector: a document and why it is in the family.
type Vector =
    { Id: string
      Description: string
      Tree: string }

/// One defect the reference reports: its code and severity, the node it is
/// raised on (absent where the defect names none), and that node's JSONPath.
type Defect =
    { Code: string
      Severity: string
      Node: string option
      Path: string option }

let private v id description tree =
    { Id = id
      Description = description
      Tree = tree }

let all: Vector list =
    [ v
          "validate-clean-nested"
          "The control: nested Boxes holding Markdown report no defect, so a host that raises anything here disagrees."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"intro","kind":{"$type":"Markdown","text":"Intro"}},{"id":"inner","kind":{"$type":"Box","children":[{"id":"body","kind":{"$type":"Markdown","text":"Body"}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""
      v
          "validate-switch-empty-statekey-at-index-1"
          "A Switch with an empty stateKey (FUARAN083) as the second child of a Box. The node's path is $.kind.children[1]: an array index is spelled [i] in a finding's location, as in a decode path (sections 6 and 29.3)."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"intro","kind":{"$type":"Markdown","text":"Intro"}},{"id":"sw","kind":{"$type":"Switch","cases":[{"child":{"id":"sw-a","kind":{"$type":"Markdown","text":"A"}},"match":"a"}],"default":{"id":"sw-d","kind":{"$type":"Markdown","text":"D"}},"stateKey":""}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""
      v
          "validate-switch-empty-statekey-at-index-10"
          "The same defect on the eleventh child, so the index has two digits: $.kind.children[10]. A host that orders or compares paths as strings, or spells the index as a dotted segment, disagrees here."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"m0","kind":{"$type":"Markdown","text":"0"}},{"id":"m1","kind":{"$type":"Markdown","text":"1"}},{"id":"m2","kind":{"$type":"Markdown","text":"2"}},{"id":"m3","kind":{"$type":"Markdown","text":"3"}},{"id":"m4","kind":{"$type":"Markdown","text":"4"}},{"id":"m5","kind":{"$type":"Markdown","text":"5"}},{"id":"m6","kind":{"$type":"Markdown","text":"6"}},{"id":"m7","kind":{"$type":"Markdown","text":"7"}},{"id":"m8","kind":{"$type":"Markdown","text":"8"}},{"id":"m9","kind":{"$type":"Markdown","text":"9"}},{"id":"sw","kind":{"$type":"Switch","cases":[{"child":{"id":"sw-a","kind":{"$type":"Markdown","text":"A"}},"match":"a"}],"default":{"id":"sw-d","kind":{"$type":"Markdown","text":"D"}},"stateKey":""}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""
      v
          "validate-switch-duplicate-match-in-a-case-arm"
          "A Switch whose two cases match the same value (FUARAN082), held in the first case arm of an outer Switch. The node's path is $.kind.cases[0].child: an arm of an ordered list of records carries its index too. Neither state key has a writer in the tree, so the reference also warns FUARAN103 on both Switches, the outer one at the root path $."
          """{"id":"outer","kind":{"$type":"Switch","cases":[{"child":{"id":"inner","kind":{"$type":"Switch","cases":[{"child":{"id":"inner-a","kind":{"$type":"Markdown","text":"A"}},"match":"x"},{"child":{"id":"inner-b","kind":{"$type":"Markdown","text":"B"}},"match":"x"}],"default":{"id":"inner-d","kind":{"$type":"Markdown","text":"D"}},"stateKey":"inner"}},"match":"one"}],"default":{"id":"outer-d","kind":{"$type":"Markdown","text":"D"}},"stateKey":"outer"}}"""
      v
          "validate-duplicate-id-across-default-and-child"
          "One id held by a Box's child and by a Switch's default arm (FUARAN-DUP-ID). The defect is located at the id's SECOND node in document order, $.kind.children[1].kind.default: the node to change is the later one. The Switch's state key has no writer, so the reference also warns FUARAN103 on it."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"twice","kind":{"$type":"Markdown","text":"First"}},{"id":"sw","kind":{"$type":"Switch","cases":[{"child":{"id":"sw-a","kind":{"$type":"Markdown","text":"A"}},"match":"a"}],"default":{"id":"twice","kind":{"$type":"Markdown","text":"Second"}},"stateKey":"view"}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""
      v
          "validate-duplicate-id-three-times"
          "One id held by three nodes, the third one level down (FUARAN-DUP-ID). Three occurrences are TWO findings, one at each occurrence after the first, in document order: $.kind.children[1] and $.kind.children[2].kind.children[0]. The count of findings is the count of nodes to change."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"thrice","kind":{"$type":"Markdown","text":"One"}},{"id":"thrice","kind":{"$type":"Markdown","text":"Two"}},{"id":"inner","kind":{"$type":"Box","children":[{"id":"thrice","kind":{"$type":"Markdown","text":"Three"}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""
      v
          "validate-duplicate-id-in-a-list-of-twelve"
          "One id held by the third and the eleventh of twelve children (FUARAN-DUP-ID). Document order puts children[2] before children[10], so the one finding is at $.kind.children[10]. A host that walks or orders positions as strings meets children[10] first and reports children[2]."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"m0","kind":{"$type":"Markdown","text":"0"}},{"id":"m1","kind":{"$type":"Markdown","text":"1"}},{"id":"twice","kind":{"$type":"Markdown","text":"2"}},{"id":"m3","kind":{"$type":"Markdown","text":"3"}},{"id":"m4","kind":{"$type":"Markdown","text":"4"}},{"id":"m5","kind":{"$type":"Markdown","text":"5"}},{"id":"m6","kind":{"$type":"Markdown","text":"6"}},{"id":"m7","kind":{"$type":"Markdown","text":"7"}},{"id":"m8","kind":{"$type":"Markdown","text":"8"}},{"id":"m9","kind":{"$type":"Markdown","text":"9"}},{"id":"twice","kind":{"$type":"Markdown","text":"10"}},{"id":"m11","kind":{"$type":"Markdown","text":"11"}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""
      v
          "validate-duplicate-id-in-the-node-envelope"
          "One id held by the root's envelope fallback and by its child (FUARAN-DUP-ID). The fallback is walked (section 8.1), and the canonical document lists it before the kind (member order), so the fallback is the first occurrence and the finding is at $.kind.children[0]. A host that walks the kind before the envelope reports $.fallback."
          """{"fallback":{"id":"twice","kind":{"$type":"Markdown","text":"Fallback"}},"id":"root","kind":{"$type":"Box","children":[{"id":"twice","kind":{"$type":"Markdown","text":"Child"}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}""" ]

/// Every node object in a canonical document, in document order, with its
/// JSONPath: an object carrying a string `id` and an object `kind`.
let nodePaths (canonical: string) : (string * string) list =
    use doc = JsonDocument.Parse canonical
    let found = ResizeArray<string * string>()

    let rec walk (path: string) (e: JsonElement) =
        match e.ValueKind with
        | JsonValueKind.Object ->
            match e.TryGetProperty "id", e.TryGetProperty "kind" with
            | (true, id), (true, kind) when id.ValueKind = JsonValueKind.String && kind.ValueKind = JsonValueKind.Object ->
                found.Add((id.GetString() |> Option.ofObj |> Option.defaultValue "", path))
            | _ -> ()

            for p in e.EnumerateObject() do
                walk (path + "." + p.Name) p.Value
        | JsonValueKind.Array -> e.EnumerateArray() |> Seq.iteri (fun i x -> walk (sprintf "%s[%d]" path i) x)
        | _ -> ()

    walk "$" doc.RootElement
    List.ofSeq found

/// The tree as the reference decodes it, and its canonical bytes. The vector
/// must already BE those bytes: a host decodes exactly what the file carries,
/// so a path into anything else would point at nothing.
let private decoded (vector: Vector) =
    match JsonDecode.decodeNodeObj vector.Tree with
    | Error e -> failwithf "validate vector '%s': the tree does not decode (%s at %s)" vector.Id e.Code e.Path
    | Ok tree ->
        let canonical = CanonicalJson.encodeNode tree

        if canonical <> vector.Tree then
            failwithf
                "validate vector '%s' is not in canonical form:\n  declared  -> %s\n  canonical -> %s"
                vector.Id
                vector.Tree
                canonical

        tree

/// The code of the finding that is about a repeated id itself.
[<Literal>]
let duplicateIdCode = "FUARAN-DUP-ID"

/// The defects the reference validator reports on the vector, ordered by path
/// (an absent path first), then code, then node, so the file is deterministic.
///
/// A finding names its node by id, so an id several nodes hold needs a rule.
/// The reference reports one FUARAN-DUP-ID finding per occurrence after the
/// first, and those are about the LATER nodes (the ones to change), so the k-th
/// such finding on an id is located at the id's (k+1)-th node in document
/// order. Any other finding is located at the id's first node.
let defects (vector: Vector) : Defect list =
    let tree = decoded vector
    let paths = nodePaths vector.Tree

    let occurrences (id: string) =
        paths |> List.filter (fst >> (=) id) |> List.map snd

    match PreEmitValidate.validate tree with
    | Ok() -> []
    | Error found ->
        found
        |> List.map PreEmitValidate.findingOf
        |> List.mapFold
            (fun (repeats: Map<string, int>) finding ->
                let ordinal, repeats =
                    match finding.Location with
                    | Some id when finding.Code = duplicateIdCode ->
                        let k = Map.tryFind id repeats |> Option.defaultValue 0
                        k + 1, Map.add id (k + 1) repeats
                    | _ -> 0, repeats

                { Code = finding.Code
                  Severity = sprintf "%A" finding.Severity
                  Node = finding.Location
                  Path = finding.Location |> Option.bind (occurrences >> List.tryItem ordinal) },
                repeats)
            Map.empty
        |> fst
        |> List.distinct
        |> List.sortBy (fun d -> d.Path, d.Code, d.Node)

let private description =
    "The Fuaran pre-emit VALIDATE conformance family (WIRE_FORMAT.md 27.8.2). Each vector is a "
    + "document (`tree`, canonical wire bytes) and the pre-emit defects it carries (`expected`): each "
    + "defect's `code` and `severity` from validator/defect-vocabulary.json, the `node` id it is "
    + "raised on (null where the defect names none), and `path`, the JSONPath of that node's object "
    + "in the tree in the section 29.3 grammar: where several nodes hold the id, its first "
    + "occurrence, except that FUARAN-DUP-ID is reported once at each occurrence after the first, in "
    + "document order. A host that implements the "
    + "pre-emit tier validates each tree and asserts that its findings, restricted to the codes its "
    + "validator-coverage.json declares implemented, are exactly the expected defects restricted to "
    + "the same codes, each on the stated node; a host whose finding carries a path asserts that "
    + "the path is the stated one or extends it at a segment boundary. Generated from the reference "
    + "host's own validator, through its decoder."

/// Render the artefact.
let toJson () : string =
    let opts =
        JsonWriterOptions(Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping)

    use stream = new MemoryStream()
    use w = new Utf8JsonWriter(stream, opts)

    let optional (name: string) (value: string option) =
        match value with
        | Some s -> w.WriteString(name, s)
        | None -> w.WriteNull name

    w.WriteStartObject()
    w.WriteNumber("version", 1)
    w.WriteString("$id", artifactId)
    w.WriteString("description", description)
    w.WriteStartArray("vectors")

    for vector in all do
        w.WriteStartObject()
        w.WriteString("id", vector.Id)
        w.WriteString("description", vector.Description)
        w.WriteString("tree", vector.Tree)
        w.WriteStartArray("expected")

        for d in defects vector do
            w.WriteStartObject()
            w.WriteString("code", d.Code)
            w.WriteString("severity", d.Severity)
            optional "node" d.Node
            optional "path" d.Path
            w.WriteEndObject()

        w.WriteEndArray()
        w.WriteEndObject()

    w.WriteEndArray()
    w.WriteEndObject()
    w.Flush()

    System.Text.Encoding.UTF8.GetString(stream.ToArray()) + "\n"

/// Write the family into a corpus directory.
let write (corpusRoot: string) : string =
    let dir = Path.Combine(corpusRoot, directory)
    Directory.CreateDirectory dir |> ignore
    let path = Path.Combine(dir, fileName)
    File.WriteAllText(path, toJson ())
    path
