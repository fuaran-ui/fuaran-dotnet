module Fuaran.UI.JsonDecode.Tests.BindingPropagationTests

// ============================================================================
//  Phase 1764 — the reference host's leg of the `binding-propagation` family.
//
//  Three claims. The COMMITTED `binding-propagation/manifest.json` is
//  byte-identical to a fresh emission. Every committed vector, read back from
//  the FILE through the reference decoder, gets its expected verdict from
//  `BindingGraph` (the file, not `BindingPropagationArtifact.all`, because the
//  file is what every other host reads and an emitter must never be its own
//  only reader). And the vectors are LOAD-BEARING: the same read-back over a
//  graph with one edge dropped, or one spurious edge added, goes red — Core's
//  `laws/propagation-laws.json` never passes through a UI tree, so a perturbed
//  `BindingGraph` passes all of those; it must not pass these.
// ============================================================================

open System.IO
open System.Text.Json
open Expecto

open Fuaran.UI.Renderer.BindingGraph
open Fuaran.UI.JsonDecode.Tests.BindingPropagationArtifact

let private corpusRoot = Corpus.findRoot ()

let private artifactPath =
    Path.Combine(corpusRoot, BindingPropagationArtifact.directory, BindingPropagationArtifact.fileName)

let private regenerate =
    "regenerate with `dotnet run --project src/Fuaran.UI.JsonDecode.Tests -- --emit-binding-propagation`"

type private Committed =
    { Id: string
      Tree: string
      Write: Input list
      Expected: Verdict * string list * string list }

/// The committed vectors, read from the file.
let private committed () : Committed list =
    if not (File.Exists artifactPath) then
        failwithf "%s is missing from the corpus — %s" artifactPath regenerate

    use doc = JsonDocument.Parse(File.ReadAllText artifactPath)

    [ for el in doc.RootElement.GetProperty("vectors").EnumerateArray() ->
          let expected = el.GetProperty "expected"

          let verdict =
              match expected.GetProperty("verdict").GetString() with
              | "Exact" -> Exact
              | "Conservative" -> Conservative
              | other -> failwithf "unknown verdict '%s'" other

          { Id = string (el.GetProperty("id").GetString())
            Tree = el.GetProperty("tree").GetRawText()
            Write = [ for w in el.GetProperty("write").EnumerateArray() -> inputOf w ]
            Expected =
              verdict,
              [ for r in expected.GetProperty("readers").EnumerateArray() -> string (r.GetString()) ],
              [ for b in expected.GetProperty("because").EnumerateArray() -> b.GetRawText() ] } ]

/// Run every committed vector through `graphOf` over its decoded tree; the
/// disagreements, one line each.
let private certify (graphOf: Graph -> Graph) (vectors: Committed list) : string list =
    [ for c in vectors do
          let node = decodeTree c.Id c.Tree
          let verdict, readers, because = verdictOf (graphOf (ofTree node)) c.Write
          let produced = verdict, readers, because |> List.map opaqueJson

          if produced <> c.Expected then
              yield sprintf "%s: expected %A, produced %A" c.Id c.Expected produced ]

/// The site ids of `reader` in a graph.
let private siteIdsOf (reader: string) (g: Graph) : string list =
    g.Sites
    |> Map.filter (fun _ s ->
        match s with
        | Site.Reader r
        | Site.Transform(r, _, _, _) -> r = reader)
    |> Map.toList
    |> List.map fst

/// Drop `input` from every site of `reader` — the edge a forgotten surface loses.
let private dropEdge (reader: string) (input: Input) (g: Graph) : Graph =
    let ids = siteIdsOf reader g |> Set.ofList
    let target = inputId input

    { g with
        Dependencies =
            g.Dependencies
            |> Map.map (fun id reads -> if ids.Contains id then Set.remove target reads else reads) }

/// Make every site of `reader` read `input` — a spurious edge.
let private addEdge (reader: string) (input: Input) (g: Graph) : Graph =
    let target = inputId input

    { g with
        Dependencies =
            siteIdsOf reader g
            |> List.fold
                (fun acc id -> Map.add id (Set.add target (Map.tryFind id acc |> Option.defaultValue Set.empty)) acc)
                g.Dependencies }

[<Tests>]
let tests =
    testList
        "WIRE_FORMAT §27.8.2 — the binding-propagation family (reference host)"
        [ testCase "committed binding-propagation/manifest.json is byte-identical to a fresh emission" (fun () ->
              Expect.equal
                  (File.ReadAllText artifactPath)
                  (BindingPropagationArtifact.toJson ())
                  ("wire-format-fixtures/binding-propagation/manifest.json is stale — "
                   + regenerate))

          testCase
              "every committed vector gets its expected verdict from BindingGraph through the reference decoder"
              (fun () ->
                  let vectors = committed ()
                  Expect.isNonEmpty vectors "the family carries no vectors"
                  Expect.isEmpty (certify id vectors) "the reference host disagrees with the committed family")

          testCase "the family reaches every surface it claims to" (fun () ->
              let ids = committed () |> List.map _.Id |> Set.ofList

              for required in
                  [ "state-plain-read"
                    "state-transform-live-source"
                    "state-transform-param"
                    "filter-reaches-direct-and-scoped-query"
                    "selection-read"
                    "query-direct"
                    "multi-site-nested"
                    "write-touches-nothing"
                    "write-empty"
                    "custom-conservative-on-state" ] do
                  Expect.isTrue (ids.Contains required) (sprintf "the family has no '%s' vector" required))

          // GO-RED. Each perturbation is one edge, and each of the three State-channel
          // surfaces is perturbed on its own, so a vector set that certified only the
          // plain read would leave the Transform rows green here.
          testCase "a BindingGraph with one dropped edge fails the vectors (soundness is load-bearing)" (fun () ->
              let vectors = committed ()

              for (reader, input) in
                  [ "state-orders", Input.State "orders"
                    "live-orders", Input.State "orders"
                    "param-orders", Input.State "orders"
                    "t", Input.State "orders"
                    "p", Input.State "orders"
                    "deep", Input.State "orders"
                    "selection-grid", Input.Selection "orders-grid"
                    "filter-region", Input.Filter "region" ] do
                  Expect.isNonEmpty
                      (certify (dropEdge reader input) vectors)
                      (sprintf "dropping %s's edge to %s left every vector green" reader (inputId input)))

          testCase "a BindingGraph whose query forgets its filter dependency fails the vectors" (fun () ->
              let forget (g: Graph) =
                  { g with
                      Dependencies = g.Dependencies |> Map.remove (inputId (Input.Query "totals")) }

              Expect.isNonEmpty
                  (certify forget (committed ()))
                  "a query read through dependsOn is not certified by any vector")

          testCase "a BindingGraph with one spurious edge fails the vectors (minimality is load-bearing)" (fun () ->
              let vectors = committed ()

              for (reader, input) in
                  [ "state-other", Input.State "orders"
                    "filter-region", Input.State "orders"
                    "query-direct", Input.Filter "region"
                    "deep-other", Input.State "orders" ] do
                  Expect.isNonEmpty
                      (certify (addEdge reader input) vectors)
                      (sprintf "an edge from %s to %s left every vector green" reader (inputId input))) ]
