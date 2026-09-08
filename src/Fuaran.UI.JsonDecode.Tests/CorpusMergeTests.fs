module Fuaran.UI.JsonDecode.Tests.CorpusMergeTests

// ============================================================================
//  `--emit-corpus` MERGES into the root manifest; it does not rewrite it.
//
//  The corpus is a shared repo with more authors than this emitter. A family
//  may be hand-maintained — the `teleport-*` vectors are, their bundles
//  produced by the reference encoder and checked in — or emitted by some other
//  tool entirely. A regeneration that rewrote `manifest.json` wholesale from
//  this emitter's own fixture list therefore DELETED every such family in
//  passing: silently, leaving the payload directory orphaned on disk and every
//  host that certifies against that family quietly un-certified. It happened
//  twice on 2026-09-07 to the nine `teleport-*` entries, and both times a human
//  put them back by hand.
//
//  So the rule this file pins is: a `kind` the emit did not author is not the
//  emitter's to delete, and it comes back EXACTLY as it was found — original
//  property set, original order, original bytes. Not a re-serialisation through
//  `FixtureEntry`, which models six properties and would quietly drop a seventh.
//
//  The assertions are written so they cannot pass vacuously: each fails when
//  the population it quantifies over is empty, so a merge that preserved
//  nothing — or a source manifest carrying nothing to preserve — is a failure
//  rather than a green run over an empty list.
// ============================================================================

open System.IO
open System.Text.Json
open Expecto

/// The manifest's fixture rows as `(kind, id, rawText)` — the raw text being
/// the whole point: the claim is about bytes, so the probe must read bytes.
let private rowsOf (manifestPath: string) : (string * string * string) list =
    use doc = JsonDocument.Parse(File.ReadAllText manifestPath)

    doc.RootElement.GetProperty("fixtures").EnumerateArray()
    |> Seq.map (fun row ->
        (row.GetProperty("kind").GetString() |> Option.ofObj |> Option.defaultValue ""),
        (row.GetProperty("id").GetString() |> Option.ofObj |> Option.defaultValue ""),
        row.GetRawText())
    |> List.ofSeq

let private withTempDir (name: string) (body: string -> unit) : unit =
    let dir =
        Path.Combine(Path.GetTempPath(), "fuaran-corpus-" + name + "-" + Path.GetRandomFileName())

    Directory.CreateDirectory dir |> ignore

    try
        body dir
    finally
        try
            Directory.Delete(dir, true)
        with _ ->
            ()

let private withTempDirValue (name: string) (body: string -> 'T) : 'T =
    let dir =
        Path.Combine(Path.GetTempPath(), "fuaran-corpus-" + name + "-" + Path.GetRandomFileName())

    Directory.CreateDirectory dir |> ignore

    try
        body dir
    finally
        try
            Directory.Delete(dir, true)
        with _ ->
            ()

/// The live corpus's manifest, when this checkout has one. A bare single-repo
/// clone has none and the corpus-backed arms skip — the same posture every
/// other corpus-reading suite in this repo takes.
let private liveManifest () : string option =
    let candidate = Path.Combine(Corpus.findRoot (), "manifest.json")

    if File.Exists candidate then Some candidate else None

/// A foreign row written at the indentation the emitter itself uses (a fixture
/// row sits at 4, its properties at 6), because that is the shape a real
/// manifest presents and the shape preserved bytes are re-inserted at. `id`
/// deliberately comes LAST and `provenance` is a property `FixtureEntry` has
/// never heard of: a merge that round-tripped rows through the emitter's own
/// record would reorder the first and drop the second, and both are silent.
let private foreignRow =
    "{\n"
    + "      \"kind\": \"parlour-trick\",\n"
    + "      \"decoder\": \"parlour\",\n"
    + "      \"inputFile\": \"parlour/one.json\",\n"
    + "      \"provenance\": \"hand-maintained — not built by the reference emitter\",\n"
    + "      \"description\": \"a foreign family, described in a way only its author knows\",\n"
    + "      \"id\": \"parlour-one\"\n"
    + "    }"

[<Tests>]
let corpusMerge =
    testList
        "Corpus --emit-corpus — merge, not rewrite"
        [ testCase "a family the emitter does not author survives verbatim, unknown properties and all" (fun () ->
              withTempDir "merge" (fun dir ->
                  let manifestPath = Path.Combine(dir, "manifest.json")

                  File.WriteAllText(
                      manifestPath,
                      "{\n  \"version\": 1,\n  \"fixtures\": [\n    " + foreignRow + "\n  ]\n}\n"
                  )

                  Corpus.emit dir

                  let rows = rowsOf manifestPath

                  Expect.isGreaterThan
                      (List.length rows)
                      100
                      "the emit produced almost no rows — every assertion below would be about nothing"

                  match rows |> List.filter (fun (kind, _, _) -> kind = "parlour-trick") with
                  | [ (_, id, raw) ] ->
                      Expect.equal id "parlour-one" "the preserved row keeps its id"

                      Expect.equal
                          raw
                          foreignRow
                          "the preserved row's bytes changed. An emit must return a foreign row exactly as it found it — property set, property order and all — because the emitter cannot know what a family it does not author needs to carry."
                  | other ->
                      failtestf
                          "expected exactly one preserved 'parlour-trick' row after the emit, found %d. A regeneration that drops a family it did not author un-certifies every host that reads it."
                          (List.length other)))

          testCase "the live corpus's rows all survive a regeneration, hand-maintained ones byte-for-byte" (fun () ->
              match liveManifest () with
              | None ->
                  skiptest
                      "wire-format-fixtures/manifest.json not found — this arm needs the workspace checkout (skipped in a bare single-repo clone)"
              | Some source ->
                  // Which families this emitter AUTHORS is MEASURED, not pinned
                  // as a list here: a from-scratch emit into an empty directory
                  // has nothing to preserve, so every kind it produces is one it
                  // builds. A family that later moves into the emitter is then
                  // followed rather than re-asserted from a 2026-09 split.
                  let authoredKinds =
                      withTempDirValue "merge-scratch" (fun dir ->
                          Corpus.emit dir

                          rowsOf (Path.Combine(dir, "manifest.json"))
                          |> List.map (fun (k, _, _) -> k)
                          |> Set.ofList)

                  withTempDir "merge-live" (fun dir ->
                      let manifestPath = Path.Combine(dir, "manifest.json")
                      File.Copy(source, manifestPath)

                      let before = rowsOf manifestPath
                      let sourceText = File.ReadAllText manifestPath
                      Expect.isNonEmpty before "the source manifest carried no fixture rows at all"

                      Corpus.emit dir
                      let after = rowsOf manifestPath

                      // The strongest form of the claim, and the one that
                      // catches what the per-row comparisons cannot: the merged
                      // document as a WHOLE. A preserved row can arrive with its
                      // own bytes intact and still land in the wrong place —
                      // `WriteRawValue` supplies no indentation, so an early
                      // draft of this merge produced a well-formed manifest
                      // reading `},{` at every seam, which every row-level probe
                      // called identical. This arm is expected to hold only
                      // while the committed corpus is in step with the emitter;
                      // a genuine fixture change moves it, and the remedy is to
                      // commit the regenerated corpus, not to relax this.
                      Expect.equal
                          (File.ReadAllText manifestPath)
                          sourceText
                          "regenerating the committed corpus changed manifest.json. If the fixtures genuinely moved, commit the regeneration; if they did not, the emit is rewriting something it should have left alone."

                      let lost =
                          before
                          |> List.filter (fun (kind, id, _) ->
                              not (after |> List.exists (fun (k2, id2, _) -> k2 = kind && id2 = id)))

                      Expect.isEmpty
                          lost
                          (sprintf
                              "a regeneration of the live corpus DROPPED %d row(s). Each is a conformance obligation deleted in passing — the payload stays on disk and the hosts reading it stop being certified, with nothing red to say so: %A"
                              (List.length lost)
                              (lost |> List.map (fun (k, id, _) -> k, id)))

                      // The bytes claim, over the families the reference emitter
                      // does not build. `teleport-decode` / `teleport-reject` is
                      // the standing instance — nine rows as this is written —
                      // and the set is read off the corpus, so the next such
                      // family is covered with no edit here.
                      let handMaintained =
                          before |> List.filter (fun (kind, _, _) -> not (authoredKinds.Contains kind))

                      Expect.isNonEmpty
                          handMaintained
                          "the live corpus carries no family the emitter does not author, so the byte assertion below would pass over an empty list. If a hand-maintained family was just adopted by the emitter, that is progress — retire this arm deliberately rather than leaving it vacuous."

                      let changed =
                          handMaintained
                          |> List.filter (fun (kind, id, raw) ->
                              after
                              |> List.exists (fun (k2, id2, raw2) -> k2 = kind && id2 = id && raw2 <> raw))

                      Expect.isEmpty
                          changed
                          (sprintf
                              "%d of the %d row(s) the emitter does not author came back with DIFFERENT bytes. A row it re-authors may legitimately differ; a row it merely preserves may not — the emitter cannot know what that family needs to carry: %A"
                              (List.length changed)
                              (List.length handMaintained)
                              (changed |> List.map (fun (k, id, _) -> k, id)))))

          testCase "every family in the emitted manifest is named in its description" (fun () ->
              // The manifest's `description` is the harness contract a
              // conformant host reads to learn what to DO with each `kind`. The
              // emitter writes it, so a family it merely preserves is a family
              // it must still describe — otherwise the merge keeps the rows and
              // drops the only sentence saying how to run them, which is the
              // same loss one level up.
              match liveManifest () with
              | None -> skiptest "wire-format-fixtures/manifest.json not found — needs the workspace checkout"
              | Some source ->
                  withTempDir "merge-desc" (fun dir ->
                      let manifestPath = Path.Combine(dir, "manifest.json")
                      File.Copy(source, manifestPath)
                      Corpus.emit dir

                      use doc = JsonDocument.Parse(File.ReadAllText manifestPath)

                      let description =
                          doc.RootElement.GetProperty("description").GetString()
                          |> Option.ofObj
                          |> Option.defaultValue ""

                      let kinds =
                          doc.RootElement.GetProperty("fixtures").EnumerateArray()
                          |> Seq.choose (fun r -> r.GetProperty("kind").GetString() |> Option.ofObj)
                          |> Set.ofSeq

                      Expect.isGreaterThan (Set.count kinds) 5 "too few families for this check to mean anything"

                      let undescribed =
                          kinds |> Set.filter (fun k -> not (description.Contains k)) |> Set.toList

                      Expect.isEmpty
                          undescribed
                          (sprintf
                              "these fixture families appear in manifest.json and are named nowhere in its description, so a host reading the manifest is told the rows exist and not how to run them: %A"
                              undescribed))) ]
