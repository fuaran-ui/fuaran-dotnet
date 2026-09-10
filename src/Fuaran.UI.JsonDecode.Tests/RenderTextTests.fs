module Fuaran.UI.JsonDecode.Tests.RenderTextTests

// ============================================================================
//  The reference host's certification against the render-TEXT family
//  (Phase 1663) — `render-text.json`, WIRE_FORMAT.md §13.
//
//  This is the leg that makes the family SATISFIABLE rather than merely
//  declared. `RenderTextArtifact` proves each vector on the way out; this
//  suite proves it on the way IN — from the COMMITTED artefact's bytes, the
//  way every other host reads it, so the family is certified by the same path
//  it is published on rather than by the values it was authored from.
//
//  Four guards:
//
//    1. EVERY COMMITTED VECTOR RE-RESOLVES. Read `render-text.json`, and for
//       each vector decode its named fixture, find its named node, resolve its
//       named slot under its PINNED sources, and assert byte-equality with the
//       committed `expectedText`.
//
//    2. THE GO-RED PROPERTY IS PROVEN. A vector whose expectation is perturbed
//       by one byte must fail — asserted here, against the same comparison
//       guard 1 uses, so "green" is known to be an answer and not a vacuum.
//
//    3. NON-VACUITY. The committed artefact carries vectors, and it carries at
//       least one that pins a `Binding.Now` grain and one that pins a
//       `Format.Since` — the two arms the phase exists to make checkable. An
//       artefact that lost them would otherwise pass guards 1 and 2 by
//       asserting nothing.
//
//    4. STALE-ARTEFACT. The committed file is byte-identical to a fresh
//       emission, naming the regeneration command — the stale-schema guard's
//       discipline, and what makes a fixture edit that moves a pinned slot fail
//       for the author who made it.
// ============================================================================

open System
open System.IO
open System.Text.Json
open Expecto

/// One vector as the COMMITTED artefact declares it. Deliberately a separate
/// type from `RenderTextFixtures.Vector`: this one is read from the file, and a
/// suite that reused the authoring type would be free to skip the file
/// altogether — which is precisely the reading path every other host takes.
type private CommittedVector =
    { Id: string
      Fixture: string
      NodeId: string
      Slot: string
      Now: string
      Locale: string
      ExpectedText: string }

let private corpusRoot = Corpus.findRoot ()

let private artifactPath = Path.Combine(corpusRoot, RenderTextArtifact.fileName)

let private committed: CommittedVector list =
    if not (File.Exists artifactPath) then
        failwithf
            "%s is missing from the corpus — regenerate with `dotnet run --project src/Fuaran.UI.JsonDecode.Tests -- --emit-render-text`"
            RenderTextArtifact.fileName

    use doc = JsonDocument.Parse(File.ReadAllText artifactPath)

    let str (el: JsonElement) (name: string) : string =
        match el.TryGetProperty name with
        | true, v ->
            match v.GetString() with
            | null -> failwithf "render-text.json: vector property '%s' is null" name
            | s -> s
        | _ -> failwithf "render-text.json: vector is missing required property '%s'" name

    doc.RootElement.GetProperty("vectors").EnumerateArray()
    |> Seq.map (fun v ->
        let sources = v.GetProperty "sources"

        { Id = str v "id"
          Fixture = str v "fixture"
          NodeId = str v "nodeId"
          Slot = str v "slot"
          Now = str sources "now"
          Locale = str sources "locale"
          ExpectedText = str v "expectedText" })
    |> List.ofSeq

/// Resolve one committed vector the way a conformant host does: from the
/// artefact's own fields, never from the authoring values.
let private resolve (v: CommittedVector) : string =
    RenderTextArtifact.resolvedText
        corpusRoot
        { Id = v.Id
          Fixture = v.Fixture
          NodeId = v.NodeId
          Slot = v.Slot
          Sources = { Now = v.Now; Locale = v.Locale }
          ExpectedText = v.ExpectedText
          Description = "" }

[<Tests>]
let referenceCertification =
    testList
        "WIRE_FORMAT §13 — render-text family (reference host)"
        [ testCase "every committed vector re-resolves to its expected text" (fun () ->
              let mismatches =
                  committed
                  |> List.choose (fun v ->
                      let produced = resolve v

                      if produced = v.ExpectedText then
                          None
                      else
                          Some(sprintf "%s: expected %A, produced %A" v.Id v.ExpectedText produced))

              Expect.isEmpty
                  mismatches
                  "a committed render-text vector the reference host does not reproduce — the family declares a text no host can be held to")

          testCase "the comparison can go red (a perturbed expectation fails)" (fun () ->
              // The shape a divergence takes on the day it lands. Without this,
              // guard 1 above would pass identically over an empty comparison.
              let probe =
                  match committed with
                  | v :: _ ->
                      { v with
                          ExpectedText = v.ExpectedText + "!" }
                  | [] -> failwith "no committed vectors to probe"

              Expect.notEqual
                  (resolve probe)
                  probe.ExpectedText
                  "the render-text comparison accepted a perturbed expectation — the certification is vacuous")

          testCase "the family pins the two arms it exists for" (fun () ->
              Expect.isNonEmpty committed "render-text.json carries no vectors"

              let carries (predicate: CommittedVector -> bool) : bool = committed |> List.exists predicate

              Expect.isTrue
                  (carries (fun v -> v.Id.StartsWith("now-", StringComparison.Ordinal)))
                  "no `Binding.Now` vector — the family cannot show a host resolves the instant"

              Expect.isTrue
                  (carries (fun v -> v.Id.StartsWith("since-", StringComparison.Ordinal)))
                  "no `Format.Since` vector — the family cannot show a host renders a relative time"

              Expect.isTrue
                  (carries (fun v -> v.Now = "" && v.ExpectedText = ""))
                  "no no-host-clock vector — the family does not pin what an unresolvable instant renders")

          testCase "every excluded slot names a corpus site that carries it" (fun () ->
              // An exclusion a reader cannot inspect is an assertion about the
              // locale database rather than about this corpus.
              let dangling =
                  RenderTextFixtures.excluded
                  |> List.filter (fun e ->
                      not (File.Exists(Path.Combine(corpusRoot, e.Fixture.Replace('/', Path.DirectorySeparatorChar)))))
                  |> List.map (fun e -> e.Slot + " → " + e.Fixture)

              Expect.isEmpty dangling "an excluded slot names a fixture that is not in the corpus") ]

[<Tests>]
let renderTextStaleArtifactGuard =
    testList
        "WIRE_FORMAT §13 — render-text stale-artefact guard"
        [ testCase "committed render-text.json is byte-identical to a fresh emission" (fun () ->
              Expect.equal
                  (File.ReadAllText artifactPath)
                  (RenderTextArtifact.toJson corpusRoot)
                  "wire-format-fixtures/render-text.json is stale — regenerate with `dotnet run --project src/Fuaran.UI.JsonDecode.Tests -- --emit-render-text`")

          testCase "the manifest points at the artefact" (fun () ->
              // The pointer is the family's discovery affordance: a host that
              // reads the manifest must reach it without being told the name.
              use doc =
                  JsonDocument.Parse(File.ReadAllText(Path.Combine(corpusRoot, "manifest.json")))

              match doc.RootElement.TryGetProperty "renderText" with
              | true, v ->
                  Expect.equal
                      (v.GetString())
                      RenderTextArtifact.fileName
                      "manifest.json's `renderText` pointer names the wrong file"
              | _ ->
                  failwith "manifest.json carries no `renderText` pointer — regenerate the corpus with --emit-corpus") ]
