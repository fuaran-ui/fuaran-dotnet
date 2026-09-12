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
//
//  And, since Phase 1591, the KIND-INTRINSIC ARIA lock — the declaration and
//  the two per-kind renderer arms held to each other over BOTH pipelines. See
//  the block at the foot of this file for what that lock claims and, more
//  importantly, what it does not.
// ============================================================================

open System
open System.IO
open System.Text.Json
open System.Text.RegularExpressions
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
let obligationVocabulary =
    testList
        "Fuaran.UI.RenderFidelity — obligation vocabulary (Phase 1105)"
        [ testCase "allClaims enumerates every case of the closed DU" (fun () ->
              // The one guard the Fable-safe module cannot state about itself.
              // `claimId` and `claimMeaning` are exhaustive matches, so a new
              // case cannot compile without being NAMED there — but nothing
              // forces it into the ENUMERATION the artefact is emitted from, and
              // a claim missing from `allClaims` would be absent from every
              // host's vocabulary while looking perfectly declared here.
              let cases =
                  Microsoft.FSharp.Reflection.FSharpType.GetUnionCases(typeof<ObligationClaim>)
                  |> Array.length

              Expect.equal
                  (List.length allClaims)
                  cases
                  "RenderFidelity.allClaims does not enumerate every ObligationClaim case — add the new claim to the list, or a host can never report it as unchecked")

          testCase "claim ids are distinct and kebab-cased tokens" (fun () ->
              let ids = allClaims |> List.map claimId

              Expect.equal (List.distinct ids |> List.length) (List.length ids) "two claims share a wire token"

              for id in ids do
                  Expect.isTrue
                      (id |> Seq.forall (fun c -> (c >= 'a' && c <= 'z') || c = '-'))
                      (sprintf
                          "'%s' is not a lower-case kebab token — the id is a wire string a host keys its registry by"
                          id))

          testCase "every declared obligation carries a statement and a spec section" (fun () ->
              // An obligation with no section is an assertion about a host's
              // habit, not about the specification — the thing this vocabulary
              // exists to stop.
              for (kind, o) in allObligations do
                  Expect.isNotEmpty o.Statement (sprintf "%s/%s: no normative statement" kind (claimId o.Claim))

                  Expect.stringContains
                      o.Section
                      "WIRE_FORMAT.md"
                      (sprintf "%s/%s: an obligation must cite the spec section that states it" kind (claimId o.Claim)))

          testCase "no kind declares the same claim twice" (fun () ->
              let dupes =
                  all
                  |> List.collect (fun r -> r.Obligations |> List.map (fun o -> r.Kind, claimId o.Claim))
                  |> List.countBy id
                  |> List.filter (fun (_, n) -> n > 1)
                  |> List.map fst

              Expect.isEmpty dupes "a kind declares one claim twice; a host registry would assert whichever came first")

          testCase "the media wave's three normative obligations are declared" (fun () ->
              // §3.6.6 states three render obligations in prose, and the media
              // parity batch had to port them "as stated, never inferred". They
              // are the reason this vocabulary exists, so their presence is
              // pinned rather than assumed.
              let mediaClaims =
                  tryFind "Media"
                  |> Option.map (fun r -> r.Obligations |> List.map (fun o -> claimId o.Claim))
                  |> Option.defaultValue []

              for expected in [ "accessible-name-always"; "autoplay-muted-pairing"; "no-autoplay-pathway" ] do
                  Expect.contains
                      mediaClaims
                      expected
                      (sprintf "the Media row must declare '%s' — it is normative in WIRE_FORMAT.md 3.6.6" expected))

          testCase "the emitted artefact carries the vocabulary and every row's obligations" (fun () ->
              use doc = JsonDocument.Parse(RenderFidelityArtifact.toJson ())

              let vocabulary =
                  doc.RootElement.GetProperty("obligationVocabulary").EnumerateArray()
                  |> Seq.map (fun e -> e.GetProperty("id").GetString())
                  |> List.ofSeq

              Expect.equal
                  vocabulary
                  (allClaims |> List.map claimId)
                  "the artefact's obligationVocabulary must be the closed set, in declaration order"

              let emitted =
                  doc.RootElement.GetProperty("kinds").EnumerateArray()
                  |> Seq.collect (fun k ->
                      k.GetProperty("obligations").EnumerateArray()
                      |> Seq.map (fun o -> k.GetProperty("kind").GetString(), o.GetProperty("id").GetString()))
                  |> List.ofSeq

              Expect.equal
                  emitted
                  (allObligations |> List.map (fun (kind, o) -> kind, claimId o.Claim))
                  "every declared obligation must reach the artefact — a host reads the artefact, not this table"

              // Every emitted claim id must be IN the vocabulary. A row naming a
              // claim the vocabulary omits would be unresolvable by a host that
              // keys its registry off the vocabulary.
              for (kind, id) in emitted do
                  Expect.contains
                      vocabulary
                      id
                      (sprintf "%s declares '%s', which the vocabulary does not carry" kind id)) ]

[<Tests>]
let nodeLevelTraits =
    testList
        "Fuaran.UI.RenderFidelity - node-level traits (Phase 1696)"
        [ testCase "every trait id is the wire path of a member, never a kind name" (fun () ->
              // The dot is what keeps the two SUBJECT populations distinguishable
              // in a host registry keyed by one string. A trait id spelled like a
              // kind would collide silently with that kind's own claims.
              let kindNames = all |> List.map (fun r -> r.Kind) |> Set.ofList

              for t in allTraits do
                  Expect.stringContains
                      t.Trait
                      "."
                      (sprintf
                          "'%s' is not a member path - a trait id is the wire path of the member it governs"
                          t.Trait)

                  Expect.isFalse
                      (Set.contains t.Trait kindNames)
                      (sprintf "'%s' collides with a kind name; a host registry could not tell them apart" t.Trait))

          testCase "no trait is declared twice, and no trait declares one claim twice" (fun () ->
              let ids = allTraits |> List.map (fun t -> t.Trait)
              Expect.equal (List.distinct ids |> List.length) (List.length ids) "a trait is declared twice"

              let dupes =
                  allTraits
                  |> List.collect (fun t -> t.Obligations |> List.map (fun o -> t.Trait, claimId o.Claim))
                  |> List.countBy id
                  |> List.filter (fun (_, n) -> n > 1)
                  |> List.map fst

              Expect.isEmpty
                  dupes
                  "a trait declares one claim twice; a host registry would assert whichever came first")

          testCase "every trait obligation carries a statement, a spec section and a distinct rule" (fun () ->
              for t in allTraits do
                  Expect.isNonEmpty t.Obligations (sprintf "%s declares no claim at all" t.Trait)
                  Expect.isNotEmpty t.Summary (sprintf "%s has no summary" t.Trait)

                  for o in t.Obligations do
                      Expect.isNotEmpty o.Statement (sprintf "%s/%s: no normative statement" t.Trait (claimId o.Claim))

                      Expect.stringContains
                          o.Section
                          "WIRE_FORMAT.md"
                          (sprintf
                              "%s/%s: an obligation must cite the spec section that states it"
                              t.Trait
                              (claimId o.Claim))

                  // Where a section numbers its rules the ordinals must be
                  // distinct: two claims pointing at rule 3 would leave one of
                  // the section's rules unclaimed while the count looked right.
                  let rules = t.Obligations |> List.choose (fun o -> o.Rule)

                  Expect.equal
                      (List.distinct rules |> List.length)
                      (List.length rules)
                      (sprintf "%s: two claims cite the same numbered rule" t.Trait))

          testCase "every trait claim id is in the closed vocabulary" (fun () ->
              let vocabulary = allClaims |> List.map claimId |> Set.ofList

              for t in allTraits do
                  for o in t.Obligations do
                      Expect.isTrue
                          (Set.contains (claimId o.Claim) vocabulary)
                          (sprintf
                              "%s declares '%s', which the closed vocabulary does not carry"
                              t.Trait
                              (claimId o.Claim)))

          testCase "every trait fixture is in the corpus manifest" (fun () ->
              let known = corpusEntries |> List.map (fun e -> e.Id) |> Set.ofList

              let dangling =
                  allTraits
                  |> List.collect (fun t -> t.Fixtures |> List.map (fun f -> t.Trait, f))
                  |> List.filter (fun (_, f) -> not (Set.contains f known))

              Expect.isEmpty
                  dangling
                  "a trait names a corpus fixture that does not exist - a host would have no payload to assert against")

          testCase "the style.direction trait declares all five normative §3.1 rules" (fun () ->
              // The reason this section exists. §3.1 states five numbered render
              // obligations; a trait that declared four would leave one silently
              // unowed on every host, which is the state Phase 1696 closed.
              let direction = allTraits |> List.tryFind (fun t -> t.Trait = "style.direction")

              match direction with
              | None -> failtest "the style.direction trait is not declared"
              | Some t ->
                  Expect.equal
                      (t.Obligations |> List.choose (fun o -> o.Rule) |> List.sort)
                      [ 1; 2; 3; 4; 5 ]
                      "the trait must claim every one of §3.1's five numbered rules"

                  Expect.equal
                      t.Scope
                      TraitScope.AllKinds
                      "style.direction rides the node envelope, so every kind owes it")

          testCase "the emitted artefact carries every trait, in declaration order" (fun () ->
              use doc = JsonDocument.Parse(RenderFidelityArtifact.toJson ())

              let emitted =
                  doc.RootElement.GetProperty("traits").EnumerateArray()
                  |> Seq.map (fun t ->
                      t.GetProperty("trait").GetString(),
                      t.GetProperty("appliesTo").GetProperty("scope").GetString(),
                      t.GetProperty("obligations").EnumerateArray()
                      |> Seq.map (fun o -> o.GetProperty("id").GetString(), o.GetProperty("rule").GetInt32())
                      |> List.ofSeq)
                  |> List.ofSeq

              let declared =
                  allTraits
                  |> List.map (fun t ->
                      t.Trait,
                      (match t.Scope with
                       | TraitScope.AllKinds -> "allKinds"
                       | TraitScope.NamedKinds _ -> "namedKinds"),
                      t.Obligations
                      |> List.map (fun o -> claimId o.Claim, Option.defaultValue 0 o.Rule))

              Expect.equal
                  emitted
                  declared
                  "every declared trait must reach the artefact - a host reads the artefact, not this table") ]

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

// ─── Kind-intrinsic ARIA — the lock (Phase 1591) ─────────────────────────────
//
// The declaration says which roles and live regions the renderer pins for a
// kind WHATEVER the node's `Accessibility` trait says. A declaration nothing
// measures is prose in a record, so this holds it to the two per-kind renderer
// arms — `Fuaran.UI.Renderer/Render.fs` and
// `Fuaran.UI.Renderer.Server/Render.fs`, copied beside the test binary by this
// project's own Content items so the scan reads the sources THIS build
// compiled.
//
// FOUR legs, and they fail in different directions on purpose:
//
//   A. DECLARED ARE EMITTED. Every token a row declares appears in the client
//      arm, and — for an entry declared on both pipelines — in the server arm
//      too. A role deleted from a renderer, or moved out of `Render.fs` into a
//      sibling control module, fails here.
//
//   B. EMITTED ARE DECLARED (the go-red leg). Every role token either arm emits
//      is declared by some row, or is one of the two NON-KIND emissions named
//      and justified below. A kind that gains an intrinsic role without the
//      fact fails here, in the repo that changed it — which is the whole point
//      of the phase.
//
//   C. THE TWO ARMS AGREE. Their role-token sets differ by exactly the recorded
//      difference. A role added to one pipeline alone fails here even when leg
//      B is satisfied, which is what makes this a BOTH-PIPELINES lock rather
//      than two independent ones.
//
//   D. THE SCAN CAN FAIL. Leg B's rule is exercised against a synthetic source
//      line, so the census is known to report an undeclared token rather than
//      being trusted to.
//
// THE HONESTY BOUNDARY, stated because the alternative is a reader assuming
// more: the lock is TOKEN-level, not arm-level. It proves that the set of roles
// the renderers emit is the set the table declares, and that both arms emit the
// same set. It does NOT prove that `role="tree"` is emitted by the `Tree` arm
// specifically — a token moved between two declaring kinds would pass. Locating
// an emission inside its own arm needs the rendered output of both pipelines,
// which this project has neither renderer referenced to produce; the SSR-parity
// suite is where that lives. What this lock buys is that no intrinsic emission
// can appear, vanish, or diverge across the pipelines unnoticed.

/// One of the two per-kind renderer arms, as this build compiled it.
let private rendererArm (tier: string) : string =
    let path =
        Path.Combine(AppContext.BaseDirectory, "renderer-sources", tier, "Render.fs")

    if not (File.Exists path) then
        failwithf
            "the %s renderer arm is not beside the test binary (%s) — the `renderer-sources` Content copy in Fuaran.UI.JsonDecode.Tests.fsproj did not run"
            tier
            path

    File.ReadAllText path

/// The arm's EMITTING lines: comment lines dropped, so a role named in prose
/// (`// … same classes + role="dialog" …`) is not read as an emission. The
/// patterns below would not match those anyway; dropping the lines makes the
/// scan's intent legible rather than accidental.
let private emittingLines (source: string) : string list =
    source.Split('\n')
    |> Array.map (fun line -> line.TrimEnd('\r'))
    |> Array.filter (fun line -> not ((line.TrimStart()).StartsWith "//"))
    |> List.ofArray

let private tokensMatching (pattern: string) (lines: string list) : Set<string> =
    let rx = Regex pattern

    lines
    |> List.collect (fun line -> [ for m in rx.Matches line -> m.Groups[1].Value ])
    |> Set.ofList

/// Both spellings a renderer uses for a role: Feliz's typed `prop.role "x"` and
/// the `prop.custom ("role", "x")` escape that the server arm and the
/// conditional client sites take.
let private roleTokens (lines: string list) : Set<string> =
    Set.union
        (tokensMatching "prop\\.role\\s+\"([a-z]+)\"" lines)
        (tokensMatching "\\(\\s*\"role\"\\s*,\\s*\"([a-z]+)\"\\s*\\)" lines)

let private liveTokens (lines: string list) : Set<string> =
    tokensMatching "\\(\\s*\"aria-live\"\\s*,\\s*\"([a-z]+)\"\\s*\\)" lines

/// The roles a renderer emits that are NOT kind-intrinsic, each with the reason
/// no row could carry it. Both are cross-kind: declaring either on all
/// forty-three rows would state something false about every one of them, and on
/// none would leave the census unable to close.
let private nonKindRoles: (string * string) list =
    [ "tooltip",
      "the per-node tooltip hint is emitted for ANY kind whose `Accessibility` trait declares one - trait-DRIVEN, which is the exact opposite of intrinsic"
      "note",
      "the depth-exceeded marker replaces the subtree of ANY kind that breaches `WireLimits.MaxDepth` - a wire-limit refusal, not a kind's own announcement (server arm only; the client arm carries no depth guard)" ]

/// The one recorded difference between the two arms' role sets: the server
/// emits the depth-exceeded marker and the client does not.
let private serverOnlyRoles: Set<string> = Set.ofList [ "note" ]

let private clientLines = emittingLines (rendererArm "client")
let private serverLines = emittingLines (rendererArm "server")

let private declaredRoles: Set<string> =
    allIntrinsics |> List.choose (fun (_, a) -> a.Role) |> Set.ofList

/// Leg B's rule as a function, so the negative probe below exercises the SAME
/// code the positive case does rather than a paraphrase.
let private rolesWithNoRow (emitted: Set<string>) : string list =
    let accounted = Set.union declaredRoles (nonKindRoles |> List.map fst |> Set.ofList)

    Set.difference emitted accounted |> Set.toList

[<Tests>]
let intrinsicAria =
    testList
        "Fuaran.UI.RenderFidelity — kind-intrinsic ARIA (Phase 1591)"
        [ testCase "every declared entry carries a role or a live region" (fun () ->
              for (kind, a) in allIntrinsics do
                  Expect.isTrue
                      (Option.isSome a.Role || Option.isSome a.Live)
                      (sprintf
                          "%s/%s declares neither a role nor a live region — an entry that announces nothing is not an announcement"
                          kind
                          a.Element)

                  Expect.isNotEmpty a.Element (sprintf "%s: an intrinsic entry with no element" kind)

                  match a.Condition with
                  | Some c ->
                      Expect.isNotEmpty
                          c
                          (sprintf "%s/%s: an empty condition — omit the field to mean 'every instance'" kind a.Element)
                  | None -> ()

                  match a.Tier with
                  | IntrinsicTier.ClientOnly why ->
                      Expect.isNotEmpty
                          why
                          (sprintf
                              "%s/%s is declared client-only with no reason — the asymmetry IS the fact, so it must say why the server floor emits none"
                              kind
                              a.Element)
                  | IntrinsicTier.BothPipelines -> ())

          testCase "no kind declares the same element's role twice" (fun () ->
              let dupes =
                  all
                  |> List.collect (fun r -> r.Intrinsic |> List.map (fun a -> r.Kind, a.Element, a.Role))
                  |> List.countBy id
                  |> List.filter (fun (_, n) -> n > 1)
                  |> List.map fst

              Expect.isEmpty
                  dupes
                  "a kind declares one element's role twice; a consumer would read whichever came first")

          testCase "leg A — every declared role reaches the arms that declare it" (fun () ->
              let clientRoles = roleTokens clientLines
              let serverRoles = roleTokens serverLines

              for (kind, a) in allIntrinsics do
                  match a.Role with
                  | None -> ()
                  | Some role ->
                      Expect.isTrue
                          (Set.contains role clientRoles)
                          (sprintf
                              "%s declares role=\"%s\" on %s, and the CLIENT arm emits no such role — either the renderer dropped it, or the emission moved out of Render.fs into a sibling module and the row must say so"
                              kind
                              role
                              a.Element)

                      match a.Tier with
                      | IntrinsicTier.BothPipelines ->
                          Expect.isTrue
                              (Set.contains role serverRoles)
                              (sprintf
                                  "%s declares role=\"%s\" on %s as emitted by BOTH pipelines, and the SERVER arm emits no such role — fix the floor, or re-declare the entry as IntrinsicTier.ClientOnly with the reason"
                                  kind
                                  role
                                  a.Element)
                      | IntrinsicTier.ClientOnly _ -> ())

          testCase "leg A — every declared live region reaches the arms that declare it" (fun () ->
              let clientLive = liveTokens clientLines
              let serverLive = liveTokens serverLines

              for (kind, a) in allIntrinsics do
                  match a.Live with
                  | None -> ()
                  | Some politeness ->
                      let token = liveRegionToken politeness

                      Expect.isTrue
                          (Set.contains token clientLive)
                          (sprintf
                              "%s declares aria-live=\"%s\" on %s; the CLIENT arm emits no such value"
                              kind
                              token
                              a.Element)

                      match a.Tier with
                      | IntrinsicTier.BothPipelines ->
                          Expect.isTrue
                              (Set.contains token serverLive)
                              (sprintf
                                  "%s declares aria-live=\"%s\" on %s as emitted by BOTH pipelines; the SERVER arm emits no such value"
                                  kind
                                  token
                                  a.Element)
                      | IntrinsicTier.ClientOnly _ -> ())

          testCase "leg B — every role either arm emits is declared, or is named non-kind" (fun () ->
              let emitted = Set.union (roleTokens clientLines) (roleTokens serverLines)

              Expect.isEmpty
                  (rolesWithNoRow emitted)
                  "a renderer emits a role no fidelity row declares: add it to that kind's `Intrinsic` list in Fuaran.UI.RenderFidelity, or — if it is cross-kind rather than a kind's own announcement — to `nonKindRoles` here with the reason no row could carry it")

          testCase "leg B — a role with no row FAILS the rule (negative probe)" (fun () ->
              // The census has to be able to go red. A probe token — the shape a
              // newly-pinned intrinsic role takes before anyone declares it —
              // must be reported by name.
              let probe = "probekindintrinsicrole"

              let emitted =
                  roleTokens (("                  prop.role \"" + probe + "\"") :: clientLines)

              Expect.equal
                  (rolesWithNoRow emitted)
                  [ probe ]
                  "the census must name an undeclared role — if this is empty the rule cannot fail and guards nothing")

          testCase "leg C — the two arms emit the same roles, bar the recorded difference" (fun () ->
              let clientRoles = roleTokens clientLines
              let serverRoles = roleTokens serverLines

              Expect.equal
                  (Set.difference serverRoles clientRoles)
                  serverOnlyRoles
                  "the server arm emits a role the client arm does not — a kind announced on one pipeline and silent on the other is an SSR/CSR divergence; fix the arm, or record the difference in `serverOnlyRoles` here with its reason in `nonKindRoles`"

              Expect.isEmpty
                  (Set.difference clientRoles serverRoles |> Set.toList)
                  "the client arm emits a role the server arm does not — a no-script reader is not announced what a hydrated one is; fix the server floor, or record the difference here")

          testCase "leg C — the two arms emit the same live-region politeness" (fun () ->
              Expect.equal
                  (liveTokens clientLines)
                  (liveTokens serverLines)
                  "the two arms disagree about which live-region politeness they emit; a difference must be fixed or recorded here")

          testCase "the non-kind roles are genuinely undeclared" (fun () ->
              // The exemption list is only honest while it names roles NO row
              // declares. A token appearing in both would silence leg B for a
              // kind that does own it.
              for (role, why) in nonKindRoles do
                  Expect.isFalse
                      (Set.contains role declaredRoles)
                      (sprintf
                          "'%s' is both declared by a fidelity row and exempted as non-kind — remove the exemption, it is now a kind's own announcement"
                          role)

                  Expect.isNotEmpty why (sprintf "'%s' is exempted with no reason" role))

          testCase "the emitted artefact carries every declared intrinsic entry" (fun () ->
              use doc = JsonDocument.Parse(RenderFidelityArtifact.toJson ())

              let emitted =
                  doc.RootElement.GetProperty("kinds").EnumerateArray()
                  |> Seq.collect (fun k ->
                      k.GetProperty("intrinsic").EnumerateArray()
                      |> Seq.map (fun i ->
                          k.GetProperty("kind").GetString(),
                          i.GetProperty("element").GetString(),
                          (match i.TryGetProperty "role" with
                           | true, r -> r.GetString() |> Option.ofObj
                           | _ -> None)))
                  |> List.ofSeq

              Expect.equal
                  emitted
                  (allIntrinsics |> List.map (fun (kind, a) -> kind, a.Element, a.Role))
                  "every declared intrinsic emission must reach the artefact, in table order — a consumer reads the artefact, not this table"

              // Every row carries the key, empty included: an absent array and an
              // empty one would be two spellings of "this kind announces nothing
              // of itself", and a consumer would have to guess which it met.
              for k in doc.RootElement.GetProperty("kinds").EnumerateArray() do
                  Expect.isTrue
                      (fst (k.TryGetProperty "intrinsic"))
                      (sprintf "%s carries no `intrinsic` key at all" (k.GetProperty("kind").GetString()))

              // A client-only entry must say so in the artefact, with its reason
              // — the asymmetry is the part a consumer cannot re-derive.
              let clientOnly =
                  doc.RootElement.GetProperty("kinds").EnumerateArray()
                  |> Seq.collect (fun k -> k.GetProperty("intrinsic").EnumerateArray())
                  |> Seq.filter (fun i -> i.GetProperty("tier").GetString() = "clientOnly")
                  |> List.ofSeq

              Expect.equal
                  (List.length clientOnly)
                  (allIntrinsics
                   |> List.filter (fun (_, a) ->
                       match a.Tier with
                       | IntrinsicTier.ClientOnly _ -> true
                       | IntrinsicTier.BothPipelines -> false)
                   |> List.length)
                  "the artefact must carry every client-only entry as such"

              for i in clientOnly do
                  Expect.isTrue
                      (fst (i.TryGetProperty "tierNote"))
                      "a client-only entry reaches the artefact without the reason the server floor emits none") ]
