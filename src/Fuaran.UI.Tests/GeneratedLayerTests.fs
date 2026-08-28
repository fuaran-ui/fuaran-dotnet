module Fuaran.UI.Tests.GeneratedLayer

#nowarn "3261" // DirectoryInfo.Parent + JsonElement.GetString() are legitimately nullable here.

// ============================================================================
//  Phase 671 step 2 — the tier-side byte-diff.
//
//  Core already proves the generated encoder equals the *corpus bytes*
//  (`IdlUiGenTests`), and the corpus is the hand-written host's own gate, so
//  generated == hand-written holds transitively. The migration recipe asks for
//  the comparison to be made **direct** on the tier side, which is what this is:
//  for one fixture, build BOTH the generated structural value and the equivalent
//  hand-written `Node<'Msg>`, encode each with its own encoder, and assert all
//  three byte-strings agree.
//
//  What this pins beyond Core's own gate:
//   - the generated module COMPILES and RUNS inside the tier, against the
//     tier's pinned `Fuaran.Core.*` packages (not Core's own project refs);
//   - `Generated.encodeNode` renders through the shared `Fuaran.Core.Canon`, so
//     it inherits the tier's key ordering / escaping / float rules rather than
//     re-implementing them;
//   - the `'Msg`-erasure boundary is real: the generated `Node` carries no
//     message type at all, and still reproduces the wire exactly.
//
//  Scaling this harness to the full 84-fixture corpus is the remainder of step 2.
// ============================================================================

open System.IO
open System.Text.Json
open Expecto

open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.OpStream.Abstractions
open Fuaran.UI.Ops

/// Locate one family of the workspace-root shared corpus by climbing from the
/// test binary — the same idiom `MarkdownCorpusTests` uses.
let private familyDir (family: string) : string option =
    let rec climb (dir: DirectoryInfo option) =
        match dir with
        | None -> None
        | Some d ->
            let candidate = Path.Combine(d.FullName, "wire-format-fixtures", family)

            if Directory.Exists candidate then
                Some candidate
            else
                climb (Option.ofObj d.Parent)

    climb (Some(DirectoryInfo(System.AppContext.BaseDirectory)))

let private corpusDir () : string option = familyDir "nodes"

let private fixture (name: string) : string option =
    corpusDir ()
    |> Option.map (fun d -> Path.Combine(d, name + ".json"))
    |> Option.filter File.Exists
    |> Option.map (File.ReadAllText >> fun s -> s.Trim())

/// Every `<family>/<pattern>` fixture as (file name, trimmed contents).
let private familyFixtures (family: string) (pattern: string) : (string * string) list =
    match familyDir family with
    | None -> []
    | Some d ->
        // `Path.GetFileName` is `string | null` under F# 10 nullness; a path from
        // `GetFiles` always has one, so fall back to the whole path rather than
        // threading an option no caller can act on.
        let fileName (p: string) =
            Path.GetFileName p |> Option.ofObj |> Option.defaultValue p

        Directory.GetFiles(d, pattern)
        |> Array.toList
        |> List.sortBy fileName
        |> List.map (fun p -> fileName p, (File.ReadAllText p).Trim())

/// The corpus root — the directory holding `manifest.json`.
let private corpusRoot () : string option =
    let rec climb (dir: DirectoryInfo option) =
        match dir with
        | None -> None
        | Some d ->
            let candidate = Path.Combine(d.FullName, "wire-format-fixtures")

            if File.Exists(Path.Combine(candidate, "manifest.json")) then
                Some candidate
            else
                climb (Option.ofObj d.Parent)

    climb (Some(DirectoryInfo(System.AppContext.BaseDirectory)))

/// How many fixtures of one `kind` the corpus MANIFEST enumerates.
///
/// The size pins below derive from this rather than naming a literal, because a
/// literal is a forward-coupling trap in the one direction this repo cannot
/// control. The corpus is a SEPARATE repo (`fuaran-ui/fuaran-ui-specification`,
/// cloned to `../wire-format-fixtures/`), so a fixture lands there with no commit
/// in this one — and the pin then goes red in whatever session next runs the
/// gate, not the session that moved the corpus. That is exactly what happened at
/// `wire-format-fixtures@d427a9a`: one new node fixture, 87 -> 88, three tests in
/// this list red, and because they are `testList`-level failures they aborted the
/// whole FAKE `Test` target, so every later suite stopped running too.
/// `manifest.json` is the corpus's own authoritative enumeration and it moves
/// WITH the fixture, in the same corpus commit.
///
/// The pin keeps its original teeth. It exists (per Phase 673, which removed five
/// fixtures while every bucket held steady) to notice the corpus changing size
/// silently — and directory-vs-manifest is a strictly STRONGER form of that check
/// than directory-vs-literal, since it also catches a fixture file added or
/// deleted without the manifest following.
///
/// Deliberately fails loudly rather than returning 0: with the corpus clone
/// absent, both sides of the comparison would be 0 and the assertion would pass
/// while measuring nothing.
let private manifestFamilySize (kind: string) : int =
    match corpusRoot () with
    | None -> failtest "wire-format-fixtures/manifest.json not found — the corpus clone is missing"
    | Some root ->
        use manifest =
            JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")))

        let count =
            manifest.RootElement.GetProperty("fixtures").EnumerateArray()
            |> Seq.filter (fun entry ->
                match entry.TryGetProperty "kind" with
                | true, value -> value.GetString() = kind
                | _ -> false)
            |> Seq.length

        if count = 0 then
            failtestf "manifest.json enumerates no '%s' fixtures — the corpus or the family name is wrong" kind

        count

[<Tests>]
let generatedLayerTests =
    testList
        "Phase 671 — the IDL-generated structural layer, tier-side"
        [ test "the generated encoder reproduces a corpus fixture byte-for-byte" {
              match fixture "heading-1" with
              | None -> skiptest "wire-format-fixtures/nodes/heading-1.json not found"
              | Some expected ->
                  let generated: Generated.Node<unit> =
                      Generated.mkHeading
                          "heading-1"
                          2
                          (Generated.TextSource.Literal "Channel performance")
                          Generated.HeadingVariant.Standard

                  Expect.equal (Generated.encodeNode generated) expected "generated encoder == corpus bytes"
          }

          test "generated and hand-written encoders agree DIRECTLY on the same fixture" {
              // The migration recipe's step 2, made direct rather than transitive.
              match fixture "heading-1" with
              | None -> skiptest "wire-format-fixtures/nodes/heading-1.json not found"
              | Some expected ->
                  let generated: Generated.Node<unit> =
                      Generated.mkHeading
                          "heading-1"
                          2
                          (Generated.TextSource.Literal "Channel performance")
                          Generated.HeadingVariant.Standard

                  let handWritten: Node<unit> =
                      Fuaran.heading
                          "heading-1"
                          { Defaults.heading with
                              Text = TextSource.Literal "Channel performance" }

                  let fromGenerated = Generated.encodeNode generated
                  let fromHandWritten = CanonicalJson.encodeNode handWritten

                  Expect.equal fromGenerated expected "generated == corpus"
                  Expect.equal fromHandWritten expected "hand-written == corpus"
                  Expect.equal fromGenerated fromHandWritten "generated == hand-written (the direct diff)"
          }

          test "the generated value carries 'Msg where the tier does, and nowhere else" {
              // This test asserted the opposite until Phase 691: that `Generated.Node`
              // took no type parameter, so there was no message type to lose. That
              // erasure was a convenience of the first generator, not a property of the
              // wire — the encoder emits `"<closure>"` without reading the slot, so the
              // slot's HOST type was always free to declare (D2).
              //
              // What replaces it is the sharper claim: `'Msg` reaches exactly the types
              // that genuinely dispatch. `Binding<'T>` is the guard — it holds closures
              // (`Computed`, `Local`'s format/parse) but none of them produce a message,
              // so it must stay msg-free, matching the hand-written tier, which
              // obj-erases in the same places for the same reason.
              let value: Generated.Node<unit> =
                  Generated.mkHeading "b" 2 (Generated.TextSource.Literal "x") Generated.HeadingVariant.Standard

              Expect.equal value.Id "b" "the tree is generic in 'Msg and still constructs plainly"

              // A `Binding<float>` with no `'Msg` in sight — if the fixpoint over-reached,
              // this line stops compiling.
              let binding: Generated.Binding<float> = Generated.Binding.Static(Some 1.0)

              Expect.equal
                  (Generated.encodeNode value)
                  (Generated.encodeNode { value with Style = None })
                  "the envelope is absent either way"

              match binding with
              | Generated.Binding.Static v -> Expect.equal v (Some 1.0) "Binding<'T> carries no message type"
              | _ -> failtest "expected a Static binding"
          }

          // ================================================================
          //  Phase 671 step 2 — the byte-diff, scaled to the whole corpus.
          //
          //  The step was written when the generated layer was ENCODER-ONLY, so
          //  its note says each fixture "needs its equivalent hand-written `Node`
          //  constructed" — 84 of them, by hand. Phase 672 shipped the generated
          //  DECODER and that cost collapsed: each side can now build its own
          //  value from the same corpus bytes, so the comparison is a loop.
          //
          //   hand-written:  JsonDecode.decodeNodeObj  >> CanonicalJson.encodeNode
          //   generated:     Generated.decodeNode      >> Generated.encodeNode
          //
          //  Both are asserted against the corpus AND against each other, so a
          //  compensating decode+encode bug on either side cannot hide: it would
          //  have to reproduce the corpus bytes exactly to pass.
          // ================================================================

          test "hand-written encoder reproduces every corpus fixture" {
              // The control leg. It needs no generated layer, so it covers the
              // whole corpus even where the IDL does not yet model a fixture —
              // which is what makes the generated leg's shortfall attributable
              // rather than ambiguous.
              let corpus = familyFixtures "nodes" "*.json"

              Expect.equal
                  corpus.Length
                  (manifestFamilySize "node-round-trip")
                  "the nodes/ directory and the corpus manifest enumerate the same fixture set"

              let failures =
                  corpus
                  |> List.choose (fun (name, json) ->
                      match JsonDecode.decodeNodeObj json with
                      | Error e -> Some(name, sprintf "hand-written decode failed: %s at %s" e.Code e.Path)
                      | Ok node when CanonicalJson.encodeNode node <> json ->
                          Some(name, "hand-written re-encode differs")
                      | Ok _ -> None)

              Expect.isEmpty failures (sprintf "hand-written round-trip is not the identity for: %A" failures)
          }

          test "generated and hand-written encoders agree byte-for-byte across the corpus" {
              // Phase 671 step 2 proper: the DIRECT diff, every fixture the
              // generated layer can express. Three-way — each side against the
              // corpus, and the two against each other.
              let corpus = familyFixtures "nodes" "*.json"

              let compared, disagreed =
                  corpus
                  |> List.fold
                      (fun (n, bad) (name, json) ->
                          match Generated.decodeNode json, JsonDecode.decodeNodeObj json with
                          | Ok g, Ok h ->
                              let fromGenerated = Generated.encodeNode g
                              let fromHandWritten = CanonicalJson.encodeNode h

                              if
                                  fromGenerated = json
                                  && fromHandWritten = json
                                  && fromGenerated = fromHandWritten
                              then
                                  (n + 1, bad)
                              else
                                  (n + 1, (name, fromGenerated, fromHandWritten) :: bad)
                          // Not comparable: the generated layer cannot express this
                          // fixture yet. Counted by the coverage test below, which
                          // names the causes; not a failure of the diff itself.
                          | _ -> (n, bad))
                      (0, [])

              // The residue is NAMED, not merely counted. Every disagreement here is
              // the generated layer decoding a fixture and then losing information
              // on the way back out — a different and more actionable set than
              // "cannot decode", and the reason this step earns its place over the
              // transitive argument (generated == corpus == hand-written).
              //
              // It found five real IDL defects, four of them silent drops and one
              // actively wrong: `Binding.Query` declared an `accessor` the wire
              // dropped at 0.2.0, so the generated encoder emitted a field that
              // does not exist. Also missing: `Query.dependsOn`, `Tabs.onSelectTag`,
              // `Disclosure.onToggle`, `Select.onChangeMulti`.
              //
              // The node envelope was the last one, and Phase 690 closed it: the IDL
              // now declares `state` / `style` / `accessibility`, so the generated
              // encoder no longer drops `style-role-voice-1`'s `{role,voice}` on the
              // way back out. **Empty** is the interesting state for this list — the
              // generated encoder now agrees with the hand-written one on every one
              // of the fixtures it can decode.
              Expect.equal
                  (disagreed |> List.map (fun (n, _, _) -> n))
                  []
                  "the generated and hand-written encoders now agree on every decodable fixture"

              // The claim is "no residue" — EVERY fixture is directly comparable —
              // so it is stated against the corpus size, not a literal. That keeps
              // the assertion's teeth exactly where they were: a new fixture the
              // generated layer cannot decode leaves `compared` short and fails
              // here, naming both numbers. What it stops doing is failing when the
              // generated layer handles the new fixture perfectly well and only the
              // literal was stale.
              //
              // History the number used to carry: 85 → 87 at the 692–694 landing,
              // when the two `DateRange` node fixtures became comparable as
              // `FormFieldKind.DateRange` landed in the IDL (Fuaran-Core `5ddf06d`)
              // and the generated layer was re-synced. Phase 725's dip to 85 was
              // exactly the "a UI vocabulary addition lands here first, the
              // generated layer follows" lag it documented, and it closed the way it
              // said it would — by re-sync, not by weakening the diff.
              Expect.equal
                  compared
                  corpus.Length
                  (sprintf "the directly-compared set is not the whole corpus (%d of %d)" compared corpus.Length)
          }

          // ================================================================
          //  Phase 672 task 3 — the policy layer is a SEAM, not a rewrite.
          //
          //  The generated decoder covers structure only. Diagnostics (six
          //  `DecodeErrorCode`s with `$`-rooted paths), §16 lenient-accept and
          //  the reject set are judgement rather than shape, and stay
          //  hand-written ABOVE the generated layer. These two tests state what
          //  "above" has to mean for that composition to actually hold.
          // ================================================================

          test "§16 normalisation output lands in the generated decoder's domain" {
              // Each `lenient/*.expected.json` is what the hand-written policy
              // layer produces AFTER normalising its shorthand input. Round-tripping
              // those through the generated structural decoder is the seam claim:
              // policy normalises, then hands off to generated structure.
              //
              // It holds for the vocabulary the IDL models — and measuring it is how
              // we learned the IDL does NOT yet model all of it. The residue below is
              // classified by cause and pinned, because the interesting regression is
              // a fixture MOVING between buckets, which a bare pass/fail hides.
              // Feeds Phase 672 task 5 / Phase 671 step 5.
              //
              // NOT a case for retired vocabulary. Every fixture here is a `.expected`
              // file — the CANONICAL form §16 normalises *to* — so the retired kind
              // names (`Card` / `Dashboard` / `GridLayout` / `Stack` / `Table`) appear
              // only in the shorthand inputs, never in one of these. The IDL is right
              // not to model them. The residue is entirely CURRENT vocabulary (`Fact`,
              // `Action.Call`, `Action.Navigate`) plus two already-filed deferrals.
              let expected = familyFixtures "lenient" "*.expected.json"

              // Pin the SIZE too, not just the buckets. Phase 673 removed five
              // fixtures that were in the round-tripping set, so every bucket held
              // steady and this test passed without noticing the corpus had shrunk.
              // A count that only tracks failures is blind to the corpus itself.
              //
              // Against the MANIFEST rather than a literal — see `manifestFamilySize`.
              // This pin is the reason: it read 47 → 52 at Phase 719 (the five
              // compact authoring twins) and 52 → 54 at Phase 725 (the two
              // `DateRange` pair shorthands), and the 719 bump was never applied
              // here, so the pin sat red against the published corpus until 725
              // corrected it — a size pin caught one release late by the very
              // hand-maintenance it exists to replace.
              Expect.equal
                  expected.Length
                  (manifestFamilySize "lenient-accept")
                  "the lenient/ canonical fixtures and the corpus manifest enumerate the same set"

              // A node-envelope key present on the input but dropped on re-encode is
              // its own cause, not generic field drift: the IDL models no envelope.
              let carriesEnvelope (json: string) =
                  [ "\"state\":"; "\"style\":"; "\"accessibility\":" ]
                  |> List.exists json.Contains

              let classify (name: string, json: string) =
                  match Generated.decodeNode json with
                  | Ok node when Generated.encodeNode node = json -> None
                  | Ok node when carriesEnvelope json && not (carriesEnvelope (Generated.encodeNode node)) ->
                      // Phase 671 scoped the envelope out on the stated grounds that
                      // "no corpus Node fixture carries it". This fixture does, so that
                      // rationale is falsified — the exclusion may still be right, but
                      // it needs a better reason. Fed back to 671.
                      Some("node envelope (unmodelled)", name)
                  | Ok _ -> Some("field-set drift", name)
                  | Error e when e.Contains "unknown Binding case: Transform" ->
                      // Phase 692 modelled Transform (THosted → Core's own codecs), so
                      // this can no longer fire. Kept as a tripwire, like "wire null":
                      // if it ever does, the Transform case has fallen out of the IDL.
                      Some("Binding.Transform (REGRESSION — case lost)", name)
                  | Error e when e.Contains "null is not representable" ->
                      // Phase 677 removed null from the wire entirely, so this can no
                      // longer fire. Kept as a tripwire: if it ever does, a null has
                      // crept back into a canonical fixture.
                      Some("wire null (REGRESSION — null is back)", name)
                  | Error e when e.Contains "unknown node kind" -> Some("node kind absent from the IDL", name)
                  | Error e when e.Contains "unknown Action case" -> Some("Action case absent from the IDL", name)
                  | Error e when e.Contains "missing required field" -> Some("optionality drift", name)
                  | Error e -> Some("unclassified: " + e, name)

              let buckets =
                  expected
                  |> List.choose classify
                  |> List.groupBy fst
                  |> List.map (fun (cause, xs) -> cause, List.length xs)
                  |> List.sortBy fst

              let roundTripped = expected.Length - (buckets |> List.sumBy snd)

              // The residue is EMPTY as of the Phase 692 gap-closure. The three
              // buckets it last held all resolved by modelling, not by loosening:
              //  - "Binding.Transform (out of scope)" ×12 — Transform is modelled via
              //    THosted slots delegating to Core's own codecs;
              //  - "field-set drift" ×2 — the Phase 425 declarative grid vocabulary
              //    (`ColumnErased.field`, optional `value`, `rowKeyField`);
              //  - "optionality drift" ×4 — Phase 596 auto-bind: every control `value`
              //    slot is Optional (absence is legal canonical wire), and the
              //    context-dependent SYNTHESIS stays policy above this layer, so the
              //    earlier "no local OmitDefault can express it" note stands — absence
              //    just no longer needs expressing to round-trip.
              //
              // Phase 725 reopened it with ONE bucket of two: the `DateRange` pair
              // shorthands, whose canonical output the IDL could not yet decode
              // (`unknown FormFieldKind case: DateRange`). That bucket is CLOSED as of
              // the 692–694 landing — `DateRange` is in the IDL (Fuaran-Core
              // `5ddf06d`) and the generated layer is re-synced — so the residue is
              // empty again, closed by modelling exactly like the three before it.
              Expect.equal
                  buckets
                  []
                  (sprintf
                      "the IDL-coverage residue moved (%d of %d lenient-expected fixtures round-trip)"
                      roundTripped
                      expected.Length)
          }

          // ---- Phase 672 task 5: what the generator actually owns, measured ----
          test "the generated layer's coverage of the node corpus is measured, not asserted" {
              // Task 5 exists because "the tax collapsed" is worthless unspecified.
              // This is the number: how much of the real node corpus the generated
              // structural layer can currently decode AND re-encode byte-for-byte,
              // entirely on its own.
              let corpus = familyFixtures "nodes" "*.json"

              Expect.equal
                  corpus.Length
                  (manifestFamilySize "node-round-trip")
                  "the nodes/ directory and the corpus manifest enumerate the same fixture set"

              let isCovered (json: string) =
                  match Generated.decodeNode json with
                  | Ok node -> Generated.encodeNode node = json
                  | Error _ -> false

              let uncovered, coveredFixtures =
                  corpus |> List.partition (fun (_, json) -> not (isCovered json))

              let covered = List.length coveredFixtures

              // The count says how far; the NAMES say what is left, which is what a
              // later phase needs. Printed rather than pinned: pinning both a count
              // and a list makes every change fail twice with the same information.
              printfn "── generated-layer residue: %d of %d uncovered ──" uncovered.Length corpus.Length

              for name, _ in uncovered do
                  printfn "   %s" name

              // The assertion is NO UNCOVERED RESIDUE, so it is stated against the
              // corpus size rather than a literal. It keeps every tooth it had: a
              // fixture the IDL cannot yet express fails here, with the residue
              // NAMED by the printout above. It is precisely the shape a literal
              // could not hold — the corpus is a separate repo, so the number is not
              // this repo's to keep current, and the figure quoted in
              // `WIRE_FORMAT.md` §11 and Phase 671 step 5 has a checked-in SOURCE
              // (the manifest) rather than a remembered one.
              //
              // History the number used to carry: 75 → 76 at Phase 690 (the node
              // envelope; the gain was `style-role-voice-1`). 76 → 85 at the Phase
              // 692 gap-closure, where the 9-fixture residue fell to
              // Binding.Transform/Selection (THosted + host-only accessors), the
              // Phase 596 optional `value` slots, the Phase 425 grid
              // `field`/`rowKeyField` vocabulary, and Modal's Phase 426 optional
              // `onDismiss`. 85 of 87 at Phase 725 — the residue was exactly the two
              // `DateRange` node fixtures, the honest reading of a UI vocabulary
              // addition landing here before the IDL — then 85 → 87 at the 692–694
              // landing when `DateRange` reached the IDL (Fuaran-Core `5ddf06d`) and
              // the generated layer was re-synced. The whole node corpus has
              // round-tripped through the generated layer alone ever since.
              Expect.equal
                  covered
                  corpus.Length
                  (sprintf
                      "generated-layer coverage is not the whole corpus (%d of %d fixtures decode+re-encode byte-identically)"
                      covered
                      corpus.Length)
          }

          test "the generated structural decoder does not widen the accept set" {
              // Every reject fixture must still be refused SOMEWHERE. Structure
              // catches the malformed ones; the rest are refused by policy the
              // generated layer deliberately does not model (empty node ids, the
              // node envelope, validator-level constraints). Both counts are
              // pinned: if a future generator change makes the structural layer
              // start ACCEPTING something the corpus says must be refused, the
              // split moves and this fails.
              let rejects = familyFixtures "reject" "*.json"

              Expect.equal
                  rejects.Length
                  (manifestFamilySize "reject")
                  "the reject/ directory and the corpus manifest enumerate the same fixture set"

              let refusedByStructure, acceptedByStructure =
                  rejects
                  |> List.partition (fun (_, json) ->
                      match Generated.decodeNode json with
                      | Error _ -> true
                      | Ok _ -> false)

              // Named, not just counted — a change of *which* fixture falls on
              // which side is the interesting regression, and a bare count hides it.
              let policyOwned = acceptedByStructure |> List.map fst

              Expect.equal
                  (List.length refusedByStructure + List.length policyOwned)
                  rejects.Length
                  "every reject fixture is accounted for on one side of the seam"

              // Three became one at Phase 690, and the two that moved did so for the
              // right reason: `reject-unknown-tone` and `reject-wrongtype-style-tone`
              // are both *inside* the node envelope, which the IDL now models — so the
              // generated decoder reads `style` and refuses a bad `tone` on structure
              // alone, where before it never looked at the key.
              //
              // What is left are the refusals structure genuinely cannot make:
              //  - an empty-but-well-typed node id is a validator rule, not a shape;
              //  - a `DateRange` pair whose `from` sorts after its `to` is a RELATION
              //    between two well-typed sibling values (Phase 725). It joined this
              //    list at the 692–694 landing, when the generated decoder learned to
              //    decode `DateRange` at all — the same reason Phase 725 could not
              //    express the rule in JSON Schema either (Draft 2020-12 has no
              //    keyword relating two sibling property values), which is why
              //    `schemaInexpressibleRejects` pins it as schema-VALID.
              //  - a `staticRows.defaultSort.column` of -1 is a VALUE BOUND on a
              //    well-typed integer (Phase 801). The IDL has no refined-integer
              //    type, so the generated decoder reads it as an ordinary `int` and
              //    accepts; the policy decoder refuses it. Note this one differs from
              //    the two above in that the SCHEMA *can* express it (`minimum: 0`),
              //    so it is schema-invalid and stays out of `schemaInexpressibleRejects`
              //    — structure-inexpressible and schema-inexpressible are not the
              //    same set, and this fixture is the first to show the difference.
              //  - a grid `pageSize` of 0 is the SAME class as the one above
              //    (Phase 862): a value bound on a well-typed integer, which the
              //    IDL cannot refine, so the generated decoder reads a plain
              //    `int` and accepts while the policy decoder refuses. The
              //    schema expresses it as `minimum: 1`, so it too is
              //    schema-invalid and stays out of `schemaInexpressibleRejects`.
              //    That it lands here was predicted rather than discovered: 862
              //    mirrored 801's split deliberately, and this guard is what
              //    confirms the mirror held.
              //  - a bound-grid `defaultSort.column` of -1 is the SAME defect as
              //    the `staticRows` one two entries up, because Phase 861 reuses
              //    that record rather than minting a twin. Its landing here is
              //    the clearest evidence the reuse is real: a twin record would
              //    have needed its own entry for its own reason.
              //  - the four `nearmiss` fixtures (Phase 863) are a THIRD class,
              //    distinct from both of the above. They are not a value bound
              //    and not a sibling relation: they are an ENUMERATED refusal of
              //    particular property NAMES, and the generated layer has no
              //    notion of a name it should refuse — it decodes by field
              //    lookup and an unread key is simply invisible to it. The
              //    policy decoder refuses them didactically, naming the
              //    canonical field. Note they are NOT schema-inexpressible: the
              //    published schema forbids each name with
              //    `not: { required: [...] }`, so they stay out of
              //    `schemaInexpressibleRejects` — which is why this list and
              //    that one keep drifting apart, and why both are named rather
              //    than counted.
              //  - a `srcSet` entry `width` of 0 (Phase 1080) is the SAME class
              //    as the `pageSize` and `defaultSort.column` entries above: a
              //    value bound on a well-typed integer the IDL cannot refine, so
              //    the generated decoder reads a plain `int` and accepts while
              //    the policy decoder refuses. The schema expresses it as
              //    `minimum: 1`, so it stays out of
              //    `schemaInexpressibleRejects` too — a third instance of the
              //    same split, and like `pageSize` its landing here was
              //    predicted rather than discovered.
              //    Note its SIBLING, `reject-image-srcset-null`, is deliberately
              //    NOT here: `srcSet: null` is a shape failure the generated
              //    list decoder catches unaided, so the two fixtures land on
              //    opposite sides of this line. That they split is what shows
              //    the missing-list-field rule has a structural half and a
              //    policy half rather than being one rule.
              //  - a node tree nested past the §21 node-depth ceiling
              //    (`reject-limit-node-depth`, corpus bc5fcc0) is a GLOBAL
              //    bound over the whole tree, not a property of any node the
              //    generated decoder reads: each level is individually
              //    well-shaped, and the generated layer has no depth counter.
              //    The policy decoder's §21 shape-limit enforcement refuses
              //    it. (Its sibling `reject-limit-json-depth*` fixtures stay
              //    on the structural side — they exceed the JSON reader's own
              //    depth ceiling, so the parse itself fails.)
              //  - the three `FieldRule` fixtures (Phase 864) land here for
              //    reasons ALREADY on this list, which is the interesting part:
              //    the rule slot minted no new class of policy defect. An
              //    inverted `minLength`/`maxLength` pair is the `DateRange`
              //    sibling-relation class, and `validation` on a `FormField` is
              //    the `nearmiss` enumerated-name class. Only
              //    `reject-fieldrule-empty` is a shade of its own — "at least
              //    one of these six keys is present" is a relation over the
              //    ABSENCE of siblings rather than their values — and note the
              //    SCHEMA can state it (`anyOf` over the five constraint slots),
              //    so like the near misses it is structure-inexpressible and
              //    schema-expressible, and stays out of
              //    `schemaInexpressibleRejects`. Its length-pair sibling goes
              //    the other way and joins that list beside `DateRange`, which
              //    is the two sets pulling apart on one phase's three fixtures.
              //  - the four `nearmiss-a11y` fixtures (Phase 959) are the SAME
              //    enumerated-name class as the grid's, at the §3.1
              //    accessibility trait. Their landing here was predicted rather
              //    than discovered, for the reason the class was named in the
              //    first place: the generated layer decodes by field lookup, so
              //    an unread key is invisible to it whatever record it sits on.
              //    Like the grid's they are schema-EXPRESSIBLE
              //    (`not: { required: [...] }` on `Accessibility`) and so stay
              //    out of `schemaInexpressibleRejects`.
              Expect.equal
                  policyOwned
                  [ "reject-daterange-unordered.json"
                    "reject-emptynodeid.json"
                    "reject-fieldrule-empty.json"
                    "reject-fieldrule-length-unordered.json"
                    "reject-formfield-near-miss-validation.json"
                    "reject-image-srcset-nonpositive-width.json"
                    "reject-limit-node-depth.json"
                    "reject-nearmiss-a11y-aria-hidden.json"
                    "reject-nearmiss-a11y-arialabel.json"
                    "reject-nearmiss-a11y-live.json"
                    "reject-nearmiss-a11y-liveregion-case.json"
                    "reject-nearmiss-column-readonly.json"
                    "reject-nearmiss-grid-behaviour-record.json"
                    "reject-nearmiss-grid-current-page.json"
                    "reject-nearmiss-grid-sortable.json"
                    "reject-wrongtype-grid-default-sort-column.json"
                    "reject-wrongtype-grid-page-size-zero.json"
                    "reject-wrongtype-static-sort-column.json" ]
                  "the policy-owned residue is exactly the shapes structure cannot judge"
          } ]
