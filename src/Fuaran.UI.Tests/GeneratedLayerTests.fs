module Fuaran.UI.Tests.GeneratedLayer

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
open Expecto

open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.OpStream.Abstractions

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

[<Tests>]
let generatedLayerTests =
    testList
        "Phase 671 — the IDL-generated structural layer, tier-side"
        [ test "the generated encoder reproduces a corpus fixture byte-for-byte" {
              match fixture "heading-1" with
              | None -> skiptest "wire-format-fixtures/nodes/heading-1.json not found"
              | Some expected ->
                  let generated: Generated.Node =
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
                  let generated: Generated.Node =
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

          test "the generated structural value is 'Msg-free by construction" {
              // The erasure boundary, stated as a compile-time fact: `Generated.Node`
              // takes no type parameter, so there is no message type to lose. The
              // closure slots the hand-written tier carries are erased to `unit`
              // (e.g. `Binding.Query of accessor: unit * name: string`), and the
              // encoder emits the sentinel unconditionally.
              let value: Generated.Node =
                  Generated.mkHeading "b" 2 (Generated.TextSource.Literal "x") Generated.HeadingVariant.Standard

              Expect.equal value.Id "b" "a plain structural record — no 'Msg anywhere in the type"
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
              Expect.equal expected.Length 47 "the lenient corpus is the expected 47 canonical fixtures"

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
                      // Phase 671 out-of-scope: `Binding.Transform` embeds a
                      // `Fuaran.Core.DataFrame` pipeline with its own codec.
                      Some("Binding.Transform (out of scope)", name)
                  | Error e when e.Contains "null is not representable" ->
                      // Phase 671 step 6: the wire-`null` operator decision.
                      Some("wire null (undecided)", name)
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

              Expect.equal
                  buckets
                  [ "Binding.Transform (out of scope)", 12
                    "field-set drift", 3
                    "node envelope (unmodelled)", 1
                    // The residual 4 are Phase 596's auto-bind `value` omission, which
                    // is CONTEXT-dependent (it turns on the enclosing field's `id`), so
                    // no local `OmitDefault` in the IDL can express it. Not a flag away
                    // — see the note in Phase 672.
                    "optionality drift", 4
                    "wire null (undecided)", 2 ]
                  (sprintf
                      "the IDL-coverage residue moved (%d of %d lenient-expected fixtures round-trip)"
                      roundTripped
                      expected.Length)
          }

          // ---- Phase 672 task 5: what the generator actually owns, measured ----
          test "the generated layer's coverage of the node corpus is measured, not asserted" {
              // Task 5 exists because "the tax collapsed" is worthless unspecified.
              // This is the number: how much of the real 84-fixture node corpus the
              // generated structural layer can currently decode AND re-encode
              // byte-for-byte, entirely on its own.
              //
              // It is pinned deliberately low-friction in one direction: a phase that
              // widens the IDL raises the count and must update it here, which is the
              // point — the figure quoted in `WIRE_FORMAT.md` §11 and Phase 671 step 5
              // then has a checked-in source rather than a remembered one.
              let corpus = familyFixtures "nodes" "*.json"

              Expect.equal corpus.Length 84 "the node corpus is the expected 84 fixtures"

              let covered =
                  corpus
                  |> List.filter (fun (_, json) ->
                      match Generated.decodeNode json with
                      | Ok node -> Generated.encodeNode node = json
                      | Error _ -> false)
                  |> List.length

              Expect.equal
                  covered
                  62
                  (sprintf
                      "generated-layer corpus coverage moved (%d of %d fixtures decode+re-encode byte-identically)"
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

              Expect.equal rejects.Length 40 "the reject corpus is the expected 40 fixtures"

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
                  40
                  "every reject fixture is accounted for on one side of the seam"

              // All three are refusals the generated layer structurally CANNOT make:
              // an empty-but-well-typed node id is a validator rule, and both `style`
              // cases live in the node envelope, which the IDL deliberately does not
              // model (Phase 671 out-of-scope) — so the generated decoder never looks
              // at those keys and cannot object to their contents.
              Expect.equal
                  policyOwned
                  [ "reject-emptynodeid.json"
                    "reject-unknown-tone.json"
                    "reject-wrongtype-style-tone.json" ]
                  "the policy-owned residue is exactly the shapes structure cannot judge"
          } ]
