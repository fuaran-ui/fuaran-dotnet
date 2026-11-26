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

open Fuaran.UI.Ops
open Fuaran.UI.OpStream.Abstractions

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

/// A path in the 29.3 grammar: `$`, then `.name` segments (a member however
/// its key is spelled, running to the next `.` or `[`) and `[digits]` indices.
/// There is no quoted form, and an array element is always bracketed, so a
/// dotted all-digit segment is a member (a map key `404`), never an index.
let private wellFormedPath (path: string) : bool =
    Regex.IsMatch(path, @"^\$(\.[^.\[\]]+|\[\d+\])*$")

/// A path's position in document order (27.8.2): its segments, a member by
/// its name (compared Ordinally, the canonical member order) and an index by
/// its number, so `children[2]` precedes `children[10]` and a prefix precedes
/// what extends it. An absent path sorts first.
let private documentPosition (path: string option) : (int * string * int) list option =
    path
    |> Option.map (fun p ->
        [ for m in Regex.Matches(p, @"\.([^.\[\]]+)|\[(\d+)\]") ->
              if m.Groups[2].Success then
                  (1, "", int m.Groups[2].Value)
              else
                  (0, m.Groups[1].Value, 0) ])

let private inDocumentOrder (a: string option) (b: string option) : bool =
    let rec le (x: (int * string * int) list) (y: (int * string * int) list) =
        match x, y with
        | [], _ -> true
        | _, [] -> false
        | (kx, nx, ix) :: xs, (ky, ny, iy) :: ys ->
            if kx <> ky then
                kx < ky
            elif kx = 0 && nx <> ny then
                System.String.CompareOrdinal(nx, ny) < 0
            elif kx = 1 && ix <> iy then
                ix < iy
            else
                le xs ys

    match documentPosition a, documentPosition b with
    | None, _ -> true
    | Some _, None -> false
    | Some x, Some y -> le x y

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

                  let occurrences node =
                      paths |> List.filter (fst >> (=) node) |> List.map snd

                  for d in expected do
                      match d.Node, d.Path with
                      | Some node, Some path ->
                          Expect.isTrue
                              (wellFormedPath path)
                              (sprintf "%s: '%s' is in the 29.3 grammar" vector.Id path)

                          // 27.8.2: a finding is at the occurrence that raised it, and
                          // FUARAN-DUP-ID at an occurrence AFTER the first.
                          let admitted =
                              match occurrences node with
                              | _ :: later when d.Code = ValidateArtifact.duplicateIdCode -> later
                              | all -> all

                          Expect.contains
                              admitted
                              path
                              (sprintf
                                  "%s: '%s' is a node object carrying id '%s' that 27.8.2 admits"
                                  vector.Id
                                  path
                                  node)
                      | None, None -> ()
                      // A finding that names no node is still located where it was
                      // raised; one that names a node is never without a path.
                      | None, Some path ->
                          Expect.isTrue
                              (wellFormedPath path)
                              (sprintf "%s: '%s' is in the 29.3 grammar" vector.Id path)
                      | Some node, None ->
                          failtestf "%s: %s on '%s' carries no path, though a node raised it" vector.Id d.Code node

                  // One FUARAN-DUP-ID finding per occurrence after the first.
                  for node, held in paths |> List.countBy fst do
                      Expect.equal
                          (expected
                           |> List.filter (fun d -> d.Code = ValidateArtifact.duplicateIdCode && d.Node = Some node)
                           |> List.length)
                          (held - 1)
                          (sprintf "%s: id '%s' is held %d times" vector.Id node held))

          testCase "FUARAN-DUP-ID is one finding at each occurrence after the first, in document order" (fun () ->
              // Two occurrences are one finding at the second; three are two, at the
              // second and the third. The count of findings is the count of nodes to
              // change, and the node to change is the later one (27.8.2).
              let duplicates id =
                  ValidateArtifact.all
                  |> List.find (fun v -> v.Id = id)
                  |> ValidateArtifact.defects
                  |> List.filter (fun d -> d.Code = ValidateArtifact.duplicateIdCode)
                  |> List.map (fun d -> d.Node, d.Path)

              Expect.equal
                  (duplicates "validate-duplicate-id-across-default-and-child")
                  [ Some "twice", Some "$.kind.children[1].kind.default" ]
                  "two occurrences: one finding, at the second"

              Expect.equal
                  (duplicates "validate-duplicate-id-three-times")
                  [ Some "thrice", Some "$.kind.children[1]"
                    Some "thrice", Some "$.kind.children[2].kind.children[0]" ]
                  "three occurrences: two findings, at the second and the third")

          testCase "a finding is located at the occurrence that raised it" (fun () ->
              // Two Switches share an id and only the second is defective: the
              // finding is at the second, never looked up by its id (27.8.2).
              let located =
                  ValidateArtifact.all
                  |> List.find (fun v -> v.Id = "validate-defect-on-the-second-of-two-nodes-sharing-an-id")
                  |> ValidateArtifact.defects
                  |> List.filter (fun d -> d.Code = "FUARAN083")
                  |> List.map (fun d -> d.Node, d.Path)

              Expect.equal located [ Some "sw", Some "$.kind.children[1]" ] "the defective occurrence")

          testCase "a finding whose subject is a repetition is one at each occurrence after the first" (fun () ->
              // x,x,x is two findings and x,x,y,y is two (27.8.2), for every code
              // whose subject is a repeated thing.
              let count id code =
                  ValidateArtifact.all
                  |> List.find (fun v -> v.Id = id)
                  |> ValidateArtifact.defects
                  |> List.filter (fun d -> d.Code = code)
                  |> List.length

              for id, code in
                  [ "validate-duplicate-id-three-times", ValidateArtifact.duplicateIdCode
                    "validate-duplicate-id-two-ids-each-twice", ValidateArtifact.duplicateIdCode
                    "validate-switch-match-repeated-three-times", "FUARAN082"
                    "validate-switch-two-matches-each-repeated", "FUARAN082"
                    "validate-tree-row-id-repeated-three-times", "FUARAN126"
                    "validate-tree-row-ids-each-repeated", "FUARAN126"
                    "validate-static-token-repeated-three-times", "FUARAN136"
                    "validate-static-tokens-each-repeated", "FUARAN136"
                    "validate-write-back-key-repeated-three-times", "FUARAN085"
                    "validate-write-back-keys-each-repeated", "FUARAN085"
                    "validate-state-seed-declared-three-times", "FUARAN106"
                    "validate-state-seeds-each-declared-twice", "FUARAN106"
                    "validate-inline-table-repeated-three-times", "FUARAN107"
                    "validate-inline-tables-each-repeated", "FUARAN107" ] do
                  Expect.equal (count id code) 2 (sprintf "%s: %s is two findings" id code))

          testCase "a cross-tree finding is located at the occurrence that raised it" (fun () ->
              // FUARAN085, FUARAN106 and FUARAN107 are judged across the tree and are
              // still one finding at each later writer, declaration or copy, at the
              // node holding it; FUARAN103 is at the Switch whose key nothing
              // writes. On a repeated id too: never looked up by the id (27.8.2).
              let located id code =
                  ValidateArtifact.all
                  |> List.find (fun v -> v.Id = id)
                  |> ValidateArtifact.defects
                  |> List.filter (fun d -> d.Code = code)
                  |> List.map (fun d -> d.Node, d.Path)

              let at node path = Some node, Some path

              for id, code, expected in
                  [ "validate-write-back-key-repeated-three-times",
                    "FUARAN085",
                    [ at "f2" "$.kind.children[1]"; at "f3" "$.kind.children[2]" ]
                    "validate-write-back-keys-each-repeated",
                    "FUARAN085",
                    [ at "f2" "$.kind.children[1]"; at "f2" "$.kind.children[1]" ]
                    "validate-write-back-key-repeated-on-a-repeated-id", "FUARAN085", [ at "f2" "$.kind.children[2]" ]
                    "validate-state-seed-declared-three-times",
                    "FUARAN106",
                    [ at "m2" "$.kind.children[1]"; at "m3" "$.kind.children[2]" ]
                    "validate-state-seeds-each-declared-twice",
                    "FUARAN106",
                    [ at "m2" "$.kind.children[1]"; at "m4" "$.kind.children[3]" ]
                    "validate-state-seed-agreeing-then-disagreeing", "FUARAN106", [ at "m3" "$.kind.children[2]" ]
                    "validate-state-seed-disagreeing-on-a-repeated-id", "FUARAN106", [ at "m2" "$.kind.children[2]" ]
                    "validate-inline-table-repeated-three-times",
                    "FUARAN107",
                    [ at "g2" "$.kind.children[1]"; at "g3" "$.kind.children[2]" ]
                    "validate-inline-tables-each-repeated",
                    "FUARAN107",
                    [ at "g2" "$.kind.children[1]"; at "g4" "$.kind.children[3]" ]
                    "validate-inline-table-repeated-on-a-repeated-id", "FUARAN107", [ at "g2" "$.kind.children[2]" ]
                    // "First" is the document's first (27.8.2): a node's fallback, then
                    // its kind, then its state, whatever order a walk visits them in.
                    "validate-state-seed-in-a-child-then-in-the-own-state-alternative",
                    "FUARAN106",
                    [ at "a2" "$.state.onEmpty" ]
                    "validate-state-seed-in-the-fallback-then-in-the-own-state-alternative",
                    "FUARAN106",
                    [ at "a2" "$.state.onEmpty" ]
                    "validate-inline-table-in-a-child-then-in-the-own-state-alternative",
                    "FUARAN107",
                    [ at "g2" "$.state.onEmpty" ]
                    "validate-inline-table-in-the-fallback-then-in-the-own-state-alternative",
                    "FUARAN107",
                    [ at "g2" "$.state.onEmpty" ]
                    "validate-defect-on-the-second-of-two-nodes-sharing-an-id",
                    "FUARAN103",
                    [ at "sw" "$.kind.children[0]" ]
                    "validate-cross-tree-finding-on-the-second-of-two-nodes-sharing-an-id",
                    "FUARAN103",
                    [ at "sw" "$.kind.children[2]" ] ] do
                  Expect.equal (located id code) expected (sprintf "%s: %s where it was raised" id code))

          testCase "every vector's expected defects are listed in document order" (fun () ->
              // 27.8.2 defines document order and the file lists findings in it, so
              // children[2] precedes children[10]: never a string order.
              for vector in ValidateArtifact.all do
                  let paths = ValidateArtifact.defects vector |> List.map (fun d -> d.Path)

                  for a, b in List.pairwise paths do
                      Expect.isTrue (inDocumentOrder a b) (sprintf "%s: %A is listed before %A" vector.Id a b))

          testCase "the path grammar refuses the quoted form and admits every member spelling" (fun () ->
              // The go-red half of the check above.
              Expect.isFalse (wellFormedPath "$.kind.args['my-slot'].tree") "there is no quoted form"
              Expect.isFalse (wellFormedPath "$.kind.source.params[]") "an empty index is malformed"
              Expect.isTrue (wellFormedPath "$.accessibility.aria-hidden") "a member that is not an identifier"
              Expect.isTrue (wellFormedPath "$.kind.columns[2].kind.map.On time") "a map key with a space"
              Expect.isTrue (wellFormedPath "$.kind.columns[2].kind.map.404") "an all-digit map key is a member"
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

          testCase "an apply input tree is decoded, never byte-compared, so an explicit identity default is admitted"
          <| fun () ->
              // WIRE_FORMAT 3.6's scope note: an input carrying an explicit identity
              // default still decodes (read-compat) and re-encodes to the omitted form.
              // An apply vector's verdict is a function of the DECODED tree, and only a
              // pinned RESULT is canonical bytes, so an input need not be canonical. The
              // authored inputs spell a Flex layout's default `"wrap":false`; that member,
              // and nothing else, is what their canonical form drops.
              let explicitDefault = ",\"wrap\":false"
              let mutable nonCanonical = 0

              for family in ApplyArtifact.all do
                  for v in family.Vectors do
                      match JsonDecode.decodeNodeObj v.Tree with
                      | Error e -> failtestf "%s: the input tree does not decode (%s at %s)" v.Id e.Code e.Path
                      | Ok tree ->
                          let canonical = CanonicalJson.encodeNode tree

                          if canonical <> v.Tree then
                              nonCanonical <- nonCanonical + 1

                              Expect.equal
                                  canonical
                                  (v.Tree.Replace(explicitDefault, ""))
                                  (sprintf "%s: the input differs from its canonical form only by `wrap:false`" v.Id)

              Expect.isGreaterThan nonCanonical 0 "the families hold inputs that are not canonical bytes, by design"

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
