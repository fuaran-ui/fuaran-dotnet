module Fuaran.UI.JsonDecode.Tests.EmittedFamilyTests

// ============================================================================
//  The F# legs of the two families Phase 2381 gave the reference a producer
//  for: `validate/` and the four host-tree `apply/` families.
//
//  Each family is held two ways. The committed file is byte-identical to a fresh
//  emission (so no row is hand-added to an emitted family, 27.8.1), and every
//  committed vector is READ BACK: parsed out of the file, not taken from the
//  emitter's declaration, and run through the reference again, so the emitter is
//  never the only reader of what it writes. The apply families are certified a
//  second time, by `Fuaran.UI.Ops.Tests`' apply corpus suite.
// ============================================================================

open System.IO
open System.Text.Json
open System.Text.RegularExpressions
open Expecto

let private corpusRoot = Corpus.findRoot ()

let private str (e: JsonElement) : string =
    match e.GetString() with
    | null -> failwith "expected a JSON string"
    | s -> s

let private optionalStr (e: JsonElement) (name: string) : string option =
    match e.GetProperty(name).ValueKind with
    | JsonValueKind.Null -> None
    | _ -> Some(str (e.GetProperty name))

let private readCommitted (path: string) (regenerate: string) : string =
    if not (File.Exists path) then
        failwithf "%s is missing from the corpus - %s" path regenerate

    File.ReadAllText path

// ── validate/ ───────────────────────────────────────────────────────────────

let private validatePath =
    Path.Combine(corpusRoot, ValidateArtifact.directory, ValidateArtifact.fileName)

let private regenerateValidate =
    "regenerate with `dotnet run --project src/Fuaran.UI.JsonDecode.Tests -- --emit-validate`"

/// The committed vectors, each with its expected defects, as the file states them.
let private committedValidate () : (ValidateArtifact.Vector * ValidateArtifact.Defect list) list =
    use doc = JsonDocument.Parse(readCommitted validatePath regenerateValidate)

    [ for el in doc.RootElement.GetProperty("vectors").EnumerateArray() ->
          let vector: ValidateArtifact.Vector =
              { Id = str (el.GetProperty "id")
                Description = str (el.GetProperty "description")
                Tree = str (el.GetProperty "tree") }

          let expected: ValidateArtifact.Defect list =
              [ for d in el.GetProperty("expected").EnumerateArray() ->
                    { Code = str (d.GetProperty "code")
                      Severity = str (d.GetProperty "severity")
                      Node = optionalStr d "node"
                      Path = optionalStr d "path" } ]

          vector, expected ]

/// A path in the 29.3 grammar: `$`, then `.name`, `['quoted']` or `[digits]`
/// segments, and no all-digit dotted segment (the `.0` misspelling).
let private wellFormedPath (path: string) : bool =
    Regex.IsMatch(path, @"^\$(\.[A-Za-z_$][A-Za-z0-9_$]*|\[\d+\]|\['(?:[^'\\]|\\.)*'\])*$")

// ── apply/ ──────────────────────────────────────────────────────────────────

let private regenerateApply =
    "regenerate with `dotnet run --project src/Fuaran.UI.JsonDecode.Tests -- --emit-apply`"

let private applyPath (family: ApplyArtifact.Family) =
    Path.Combine(corpusRoot, ApplyArtifact.directory, family.File)

/// The vector count the `apply/` manifest declares for a family.
let private declaredCount (familyId: string) : int =
    use doc =
        JsonDocument.Parse(File.ReadAllText(Path.Combine(corpusRoot, ApplyArtifact.directory, "manifest.json")))

    doc.RootElement.GetProperty("families").EnumerateArray()
    |> Seq.find (fun f -> str (f.GetProperty "id") = familyId)
    |> fun f -> f.GetProperty("vectors").GetInt32()

[<Tests>]
let tests =
    testList
        "Phase 2381 - emitted validate and apply families"
        [ testCase "committed validate/manifest.json is byte-identical to a fresh emission" (fun () ->
              Expect.equal
                  (readCommitted validatePath regenerateValidate)
                  (ValidateArtifact.toJson ())
                  (sprintf "validate/manifest.json is stale - %s" regenerateValidate))

          testCase "every committed validate vector reads back through the reference validator" (fun () ->
              let committed = committedValidate ()
              Expect.isNonEmpty committed "the family holds vectors"

              for vector, expected in committed do
                  Expect.equal
                      (ValidateArtifact.defects vector)
                      expected
                      (sprintf "%s: the reference's defects on the committed tree" vector.Id))

          testCase "every located validate defect carries a well-formed path to its node" (fun () ->
              for vector, expected in committedValidate () do
                  let paths = ValidateArtifact.nodePaths vector.Tree

                  for d in expected do
                      match d.Node, d.Path with
                      | Some node, Some path ->
                          Expect.isTrue
                              (wellFormedPath path)
                              (sprintf "%s: '%s' is in the 29.3 grammar" vector.Id path)

                          Expect.equal
                              (paths |> List.tryFind (fst >> (=) node) |> Option.map snd)
                              (Some path)
                              (sprintf "%s: '%s' is the first node object carrying id '%s'" vector.Id path node)
                      | None, None -> ()
                      | _ -> failtestf "%s: a defect has a node without a path, or a path without a node" vector.Id)

          testCase "the path grammar refuses the dotted-index spelling and admits the indexed one" (fun () ->
              // The go-red half of the check above: the misspelling two hosts write.
              Expect.isFalse (wellFormedPath "$.kind.children.0") "an all-digit dotted segment is malformed"
              Expect.isFalse (wellFormedPath "$.kind.source.params[]") "an empty index is malformed"
              Expect.isTrue (wellFormedPath "$.kind.children[10]") "an index is [digits]"
              Expect.isTrue (wellFormedPath "$.kind.cases[0].child") "a member after an index"

              Expect.equal
                  (ValidateArtifact.nodePaths
                      """{"id":"r","kind":{"$type":"Box","children":[{"id":"a","kind":{"$type":"Markdown","text":"x"}}]}}""")
                  [ "r", "$"; "a", "$.kind.children[0]" ]
                  "node objects are located in document order, indices bracketed")

          testCase "the committed apply families are byte-identical to a fresh emission" (fun () ->
              for family in ApplyArtifact.all do
                  Expect.equal
                      (readCommitted (applyPath family) regenerateApply)
                      (ApplyArtifact.toJson family)
                      (sprintf "apply/%s is stale - %s" family.File regenerateApply))

          testCase "the apply manifest's counts are the emitted files' counts" (fun () ->
              for family in ApplyArtifact.all do
                  Expect.equal
                      (declaredCount family.Id)
                      (List.length family.Vectors)
                      (sprintf "apply/manifest.json's %s count" family.Id))

          testCase "every committed apply vector reads back through the reference apply path" (fun () ->
              for family in ApplyArtifact.all do
                  use doc = JsonDocument.Parse(readCommitted (applyPath family) regenerateApply)

                  for el in doc.RootElement.GetProperty("vectors").EnumerateArray() do
                      let input = el.GetProperty "input"
                      let expected = el.GetProperty "expected"

                      let vector: ApplyArtifact.Vector =
                          { Id = str (el.GetProperty "id")
                            Case = str (el.GetProperty "case")
                            Description = str (el.GetProperty "description")
                            Tree = str (input.GetProperty "tree")
                            Op = str (input.GetProperty "op") }

                      match str (expected.GetProperty "verdict"), ApplyArtifact.verdict vector with
                      | "accept", ApplyArtifact.Accept tree ->
                          match expected.TryGetProperty "tree" with
                          | true, pinned -> Expect.equal tree (str pinned) (sprintf "%s: the applied tree" vector.Id)
                          | _ -> Expect.isFalse family.PinsResult (sprintf "%s: the family pins results" vector.Id)
                      | "reject", ApplyArtifact.Reject code ->
                          Expect.equal code (str (expected.GetProperty "code")) (sprintf "%s: the refusal" vector.Id)
                      | stated, produced ->
                          failtestf "%s: the file says %s, the reference %A" vector.Id stated produced) ]
