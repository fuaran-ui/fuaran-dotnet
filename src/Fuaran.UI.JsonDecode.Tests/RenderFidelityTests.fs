module Fuaran.UI.JsonDecode.Tests.RenderFidelityTests

// ============================================================================
//  The render-fidelity manifest's completeness rule (Phase 442) — the Phase 430
//  capability-table discipline applied to fidelity.
//
//  Three guards, and the middle one is the whole point:
//
//    1. COMPLETENESS. Every kind in `RenderFidelity.wireKindNames` has exactly
//       one row, and a probe kind with no row FAILS — proven here rather than
//       asserted, so the rule is known to be able to go red.
//
//    2. THE PINNED LIST IS NOT ITSELF A SECOND SOURCE OF TRUTH. `wireKindNames`
//       is measured against the GENERATED `manifest.json` `kinds` array, which
//       is derived from the encoded corpus fixtures rather than from any hand
//       list. A new `NodeKind` that follows the WIRE_FORMAT §11 forward-coupling
//       rule lands there, and this test then names it as missing. Without this
//       leg, guard 1 would only prove the table is consistent with a list the
//       same author wrote — the shape of completeness check that cannot fail.
//
//    3. THE FIXTURE PINS RESOLVE. Every fixture a fidelity-sensitive row names
//       is in the corpus manifest, so a fixture rename cannot leave a badge
//       surface pointing at nothing.
//
//  Plus the stale-artefact guard on `render-fidelity.json`, mirroring the
//  stale-schema guard beside it.
// ============================================================================

open System.IO
open System.Text.Json
open Expecto

open Fuaran.UI
open Fuaran.UI.RenderFidelity

let private corpusRoot, corpusEntries = Corpus.load ()

let private manifestKinds: Set<string> =
    use doc =
        JsonDocument.Parse(File.ReadAllText(Path.Combine(corpusRoot, "manifest.json")))

    match doc.RootElement.TryGetProperty "kinds" with
    | true, arr when arr.ValueKind = JsonValueKind.Array ->
        arr.EnumerateArray()
        |> Seq.choose (fun e -> e.GetString() |> Option.ofObj)
        |> Set.ofSeq
    | _ -> failwith "manifest.json declares no 'kinds' array — regenerate the corpus with --emit-corpus"

/// The completeness rule itself, as a function, so the negative probe below
/// exercises the SAME code the positive case does rather than a paraphrase.
let private kindsWithoutRow (kinds: string list) (table: FidelityRow list) : string list =
    let declared = table |> List.map (fun r -> r.Kind) |> Set.ofList
    kinds |> List.filter (fun k -> not (Set.contains k declared))

[<Tests>]
let completeness =
    testList
        "Fuaran.UI.RenderFidelity — completeness"
        [ testCase "every canonical wire kind has a fidelity row" (fun () ->
              Expect.isEmpty
                  (kindsWithoutRow wireKindNames all)
                  "a NodeKind with no render-fidelity row: add one to Fuaran.UI.RenderFidelity.all declaring its source / fallback / rich tiers (WIRE_FORMAT.md §13, render-fidelity manifest)")

          testCase "a kind with no row FAILS the rule (negative probe)" (fun () ->
              // The rule has to be able to go red. A probe kind — the shape a
              // newly-added NodeKind takes before anyone declares its posture —
              // must be reported by name.
              let probe = "ProbeKindWithNoFidelityRow"

              Expect.equal
                  (kindsWithoutRow (probe :: wireKindNames) all)
                  [ probe ]
                  "the completeness rule must name an undeclared kind — if this is empty the rule cannot fail and guards nothing")

          testCase "no kind is declared twice" (fun () ->
              let dupes =
                  all
                  |> List.countBy (fun r -> r.Kind)
                  |> List.filter (fun (_, n) -> n > 1)
                  |> List.map fst

              Expect.isEmpty dupes "a kind carries two fidelity rows; a consumer would read whichever came first")

          testCase "the pinned kind list matches the generated manifest enumeration" (fun () ->
              // The seam that keeps `wireKindNames` honest. Both directions are
              // named, because a kind the manifest has and the table lacks and a
              // kind the table has and the manifest lacks are different defects
              // with different remedies.
              let pinned = Set.ofList wireKindNames

              Expect.isEmpty
                  (Set.difference manifestKinds pinned |> Set.toList)
                  "canonical wire kinds the fidelity manifest does not declare — add them to RenderFidelity.wireKindNames AND give each a row"

              Expect.isEmpty
                  (Set.difference pinned manifestKinds |> Set.toList)
                  "kinds the fidelity manifest declares that the corpus does not carry — a stale row, or a corpus that needs regenerating with --emit-corpus")

          testCase "wireNameOf agrees with the table's keying for the DataGrid divergence" (fun () ->
              // `Kind.name` tags DataGrid as "Grid"; the wire says "DataGrid".
              // The table is keyed on the WIRE token, so if `wireNameOf` ever
              // stopped adapting, every DataGrid lookup would silently miss.
              let grid =
                  corpusEntries
                  |> List.tryFind (fun e -> e.Id = "grid-1")
                  |> Option.map (fun e ->
                      match
                          Ops.JsonDecode.decodeNodeObj (File.ReadAllText(Path.Combine(corpusRoot, e.InputFile)))
                      with
                      | Ok n -> n.Kind
                      | Error err -> failwithf "grid-1 failed to decode: %s" err.Message)
                  |> Option.defaultWith (fun () -> failwith "grid-1 is not in the corpus manifest")

              Expect.equal (wireNameOf grid) "DataGrid" "the fidelity table is keyed on the wire discriminator"
              Expect.isSome (tryFind (wireNameOf grid)) "the DataGrid row must be reachable from a decoded node") ]

[<Tests>]
let fixturePins =
    testList
        "Fuaran.UI.RenderFidelity — fixture pins"
        [ testCase "every named fixture is in the corpus manifest" (fun () ->
              let known = corpusEntries |> List.map (fun e -> e.Id) |> Set.ofList

              let dangling =
                  all
                  |> List.collect (fun r -> r.Fixtures |> List.map (fun f -> r.Kind, f))
                  |> List.filter (fun (_, f) -> not (Set.contains f known))

              Expect.isEmpty
                  dangling
                  "a fidelity row names a corpus fixture that does not exist — a badge surface would link to nothing")

          testCase "every fidelity-sensitive kind names at least one pinning fixture" (fun () ->
              let unpinned =
                  all
                  |> List.filter (fun r -> r.Sensitive && List.isEmpty r.Fixtures)
                  |> List.map (fun r -> r.Kind)

              Expect.isEmpty
                  unpinned
                  "a kind declared fidelity-sensitive with no fixture pinning its fallback — the claim is unfalsifiable") ]

[<Tests>]
let staleArtifactGuard =
    testList
        "Fuaran.UI.RenderFidelity — stale-artefact guard"
        [ testCase "committed render-fidelity.json is byte-identical to the generated artefact" (fun () ->
              let path = Path.Combine(corpusRoot, RenderFidelityArtifact.fileName)

              Expect.isTrue
                  (File.Exists path)
                  (sprintf
                      "%s is missing from the corpus — regenerate with `dotnet run --project src/Fuaran.UI.JsonDecode.Tests -- --emit-fidelity ..\\wire-format-fixtures`"
                      RenderFidelityArtifact.fileName)

              Expect.equal
                  (File.ReadAllText path)
                  (RenderFidelityArtifact.toJson ())
                  "wire-format-fixtures/render-fidelity.json is stale relative to Fuaran.UI.RenderFidelity — regenerate with `dotnet run --project src/Fuaran.UI.JsonDecode.Tests -- --emit-fidelity ..\\wire-format-fixtures`")

          testCase "the artefact is parseable and declares one entry per row" (fun () ->
              use doc = JsonDocument.Parse(RenderFidelityArtifact.toJson ())
              let kinds = doc.RootElement.GetProperty "kinds"

              Expect.equal
                  (kinds.GetArrayLength())
                  (List.length all)
                  "the emitted artefact must carry every declared row") ]

[<Tests>]
let badgeDerivation =
    testList
        "Fuaran.UI.RenderFidelity — badge derivation"
        [ testCase "every row derives a three-segment badge" (fun () ->
              for r in all do
                  let segments = badge r

                  Expect.equal
                      (segments |> List.map (fun s -> s.Tier))
                      [ "source"; "fallback"; "rich" ]
                      (sprintf "%s: the badge is source / fallback / rich, in that order" r.Kind)

                  for s in segments do
                      Expect.isNotEmpty s.Detail (sprintf "%s/%s: a badge segment with no detail" r.Kind s.Tier))

          testCase "the rich segment is absent exactly when no client-only tier is declared" (fun () ->
              for r in all do
                  let richSegment = badge r |> List.find (fun s -> s.Tier = "rich")

                  let expected =
                      match r.Rich with
                      | RichTier.None -> false
                      | _ -> true

                  Expect.equal
                      richSegment.Present
                      expected
                      (sprintf "%s: the rich segment's presence must follow the declared tier" r.Kind))

          testCase "the five shipped fidelity contracts are represented" (fun () ->
              // Phases 289 (overlay/scroll) / 290 (CodeBlock) / 292 (Markdown) /
              // 293 (Math) / 1079 (Image): the rows this phase exists to
              // transcribe. Each must be sensitive, and each must be pinned by a
              // fixture.
              for kind in [ "Modal"; "Toast"; "ScrollArea"; "CodeBlock"; "Markdown"; "Math"; "Image" ] do
                  match tryFind kind with
                  | None -> failtestf "%s has no fidelity row" kind
                  | Some r ->
                      Expect.isTrue r.Sensitive (sprintf "%s carries a shipped fidelity contract" kind)
                      Expect.isNonEmpty r.Fixtures (sprintf "%s must name the fixture pinning its fallback" kind)

              // The four kinds whose rich tier is a client-only DOM change.
              // `Image` joins them at Phase 1079: the overlay is appended to the
              // document by an enhancement pass and is emitted by no renderer,
              // so it sits on exactly the side of the line KaTeX and syntax
              // highlighting sit on.
              for kind in [ "CodeBlock"; "Markdown"; "Math"; "Image" ] do
                  match tryFind kind |> Option.map (fun r -> r.Rich) with
                  | Some(RichTier.ClientOnly _) -> ()
                  | other -> failtestf "%s must declare a ClientOnly rich tier, got %A" kind other

              // The overlay contract's enhancement is BEHAVIOUR, not DOM — that
              // distinction is the contract (a portal would be a DOM change and
              // is refused).
              match tryFind "Modal" |> Option.map (fun r -> r.Rich) with
              | Some(RichTier.Behavioural _) -> ()
              | other -> failtestf "Modal's focus management is behavioural, got %A" other

              // ScrollArea and Toast declare NO client-only tier at all: the
              // parity-checked render is the whole render.
              for kind in [ "ScrollArea"; "Toast" ] do
                  match tryFind kind |> Option.map (fun r -> r.Rich) with
                  | Some RichTier.None -> ()
                  | other -> failtestf "%s declares no client-only tier, got %A" kind other) ]
