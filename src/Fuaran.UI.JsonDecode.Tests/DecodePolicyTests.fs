module Fuaran.UI.JsonDecode.Tests.DecodePolicyTests

// ============================================================================
//  Host-declared kind admission policy (WIRE_FORMAT.md §23, Phase 1020).
//
//  The corpus family under `decode-policy/` is the oracle: each case pairs a
//  document with a declared policy and the outcome a conformant decoder owes.
//  The family is hand-authored (the `sanitization/` precedent) rather than
//  emitted, so this suite reads its manifest directly rather than through
//  `Corpus.load`, which indexes only the generated root manifest.
//
//  Three classes of assertion, and the middle one is why the family exists:
//
//   1. THE PAIRING. The same bytes admit under one policy and refuse under
//      another. Either half alone proves nothing.
//   2. THE DEFAULT IS UNCHANGED. Every document in the family decodes through
//      the policy-less entry point exactly as it does at `admitAll` — which is
//      §22's "a decoder owes nothing" restated as a test rather than trusted as
//      a property of a diff.
//   3. THE GATE CAN GO RED. A refusal case run under a policy that ADMITS the
//      kind must fail the same check, so the check is known to be able to fail
//      rather than assumed to be.
// ============================================================================

open System.IO
open System.Text.Json
open Expecto

open Fuaran.UI.KindPolicy
open Fuaran.UI.Ops.JsonDecode

// ─── The family manifest ──────────────────────────────────────────────────

type private PolicyDecl =
    { Identity: string
      Admission: string
      Excludes: string list }

type private PolicyCase =
    { Id: string
      Document: string
      Policy: string
      Outcome: string
      ExpectedErrorCode: string option
      ExpectedPath: string option
      RefusedKind: string option
      Description: string }

let private familyDir = Path.Combine(Corpus.findRoot (), "decode-policy")

/// `JsonElement.GetString()` is `string | null` to F# 10's nullness checker.
/// Every call site below reads a key this family's manifest schema requires, so
/// an absent or non-string value is a malformed manifest — a loud failure at the
/// boundary, not a case to model downstream.
let private str (el: JsonElement) : string =
    match el.GetString() with
    | null -> failtest "decode-policy/manifest.json: expected a string value"
    | s -> s

let private optString (el: JsonElement) (name: string) : string option =
    match el.TryGetProperty name with
    | true, v -> Some(str v)
    | _ -> None

let private manifest =
    use doc =
        JsonDocument.Parse(File.ReadAllText(Path.Combine(familyDir, "manifest.json")))

    doc.RootElement.Clone()

let private declaredPolicies: PolicyDecl list =
    [ for p in manifest.GetProperty("policies").EnumerateArray() ->
          { Identity = str (p.GetProperty "identity")
            Admission = str (p.GetProperty "admission")
            Excludes =
              match p.TryGetProperty "excludesFromVocabulary" with
              | true, v -> [ for x in v.EnumerateArray() -> str x ]
              | _ -> [] } ]

let private cases: PolicyCase list =
    [ for c in manifest.GetProperty("cases").EnumerateArray() ->
          { Id = str (c.GetProperty "id")
            Document = str (c.GetProperty "document")
            Policy = str (c.GetProperty "policy")
            Outcome = str (c.GetProperty "outcome")
            ExpectedErrorCode = optString c "expectedErrorCode"
            ExpectedPath = optString c "expectedPath"
            RefusedKind = optString c "refusedKind"
            Description = str (c.GetProperty "description") } ]

/// Build the policy the manifest declares, the way §23 says a host must: an
/// `allowlist` declaration is resolved against the CORPUS vocabulary, not
/// against a list restated in this file. So a kind added to the language reaches
/// this suite through `knownNodeKinds` — which is itself pinned to the root
/// manifest's `kinds` array — rather than through an edit here.
let private resolve (identity: string) : DecodePolicy =
    match declaredPolicies |> List.tryFind (fun p -> p.Identity = identity) with
    | None -> failtestf "decode-policy manifest declares no policy '%s'" identity
    | Some p ->
        match p.Admission with
        | "all" -> DecodePolicy.admitAll
        | "allowlist" -> Policy.excluding p.Identity p.Excludes
        | other -> failtestf "unknown admission '%s' on policy '%s'" other identity

let private readDocument (c: PolicyCase) : string =
    File.ReadAllText(Path.GetFullPath(Path.Combine(familyDir, c.Document)))

// ─── The declaration itself ───────────────────────────────────────────────

[<Tests>]
let declaration =
    testList
        "WIRE_FORMAT §23 — the shipped declarations"
        [ testCase "every hatch kind is a kind the decoder recognises" (fun () ->
              // A misspelt entry in `hatchNodeKinds` is a set difference that
              // removes nothing — so the closed profile would silently ADMIT the
              // hatch it names, and every test below would still pass because
              // they exercise the two spellings that happen to be right. This is
              // the only assertion that catches it.
              Expect.isEmpty
                  (hatchNodeKinds |> List.filter (fun k -> not (List.contains k knownNodeKinds)))
                  "a hatch kind that is not in the wire vocabulary admits itself — check the spelling")

          testCase "the closed profile admits the vocabulary minus the hatches" (fun () ->
              let expected =
                  knownNodeKinds |> List.filter (fun k -> not (List.contains k hatchNodeKinds))

              for k in expected do
                  Expect.isTrue
                      (DecodePolicy.admits Policy.closedProfile k)
                      (sprintf "the closed profile must admit the non-hatch kind '%s'" k)

              for k in hatchNodeKinds do
                  Expect.isFalse
                      (DecodePolicy.admits Policy.closedProfile k)
                      (sprintf "the closed profile must refuse the hatch kind '%s'" k))

          testCase "an exclusion is resolved at construction, not against a live vocabulary" (fun () ->
              // The allow-list shape's whole claim: a kind that did not exist
              // when the policy was declared is NOT admitted by it. Modelled
              // with a name outside the vocabulary, which is what a future kind
              // is from the perspective of today's declaration.
              Expect.isFalse
                  (DecodePolicy.admits (Policy.excluding "probe" [ "Custom" ]) "AKindAddedNextRelease")
                  "a policy declared today must not admit a kind added later")

          testCase "the default policy narrows nothing" (fun () ->
              Expect.isFalse (DecodePolicy.narrows DecodePolicy.admitAll) "admitAll is not a narrowing"
              Expect.isTrue (DecodePolicy.narrows Policy.closedProfile) "the closed profile is a narrowing"

              for k in knownNodeKinds do
                  Expect.isTrue (DecodePolicy.admits DecodePolicy.admitAll k) "admitAll admits every kind") ]

// ─── `wireKindName` against the corpus ────────────────────────────────────

[<Tests>]
let wireProjection =
    testList
        "Fuaran.UI.KindPolicy.wireKindName"
        [ testCase "agrees with every node fixture's own kind.$type" (fun () ->
              // A policy is written in WIRE discriminators, so the authoring-side
              // lint must name a kind the way the wire does. `Kind.name` differs
              // on exactly one kind (`DataGrid` → `"Grid"`) and the adaptation is
              // two lines — which is precisely the kind of thing that is right
              // when written and wrong two kinds later. Pinned against the
              // corpus rather than against a restated table.
              let root, entries = Corpus.load ()

              let mismatches =
                  [ for e in entries do
                        if e.Kind = "node-round-trip" then
                            let text = Corpus.readPayload root e.InputFile

                            match decodeNodeObj text with
                            | Error err -> yield sprintf "%s — did not decode (%s)" e.Id err.Code
                            | Ok node ->
                                // `System.Text.Json` defaults to a reader depth
                                // of 64 and the §21 limit fixtures nest deeper
                                // than that by design. `Corpus.wireJsonOptions`
                                // carries the format's own syntactic bound
                                // (`WireLimits.MaxJsonDepth`), so this harness
                                // follows §21 if it ever moves — a literal here
                                // would be the one depth site left behind.
                                use doc = JsonDocument.Parse(text, Corpus.wireJsonOptions)
                                let onTheWire = str (doc.RootElement.GetProperty("kind").GetProperty "$type")
                                let projected = wireKindName node.Kind

                                if projected <> onTheWire then
                                    yield
                                        sprintf "%s — wire says '%s', wireKindName says '%s'" e.Id onTheWire projected ]

              Expect.isEmpty mismatches "wireKindName must reproduce the wire discriminator for every fixture") ]

// ─── The corpus family ────────────────────────────────────────────────────

/// The conformance rule as a FUNCTION, so the negative probe below exercises the
/// same code the positive cases do rather than a paraphrase of it. Returns the
/// complaints; empty means the case held.
let private violations (c: PolicyCase) (policy: DecodePolicy) : string list =
    let decoded = decodeNodeWithPolicy policy (readDocument c)

    match c.Outcome, decoded with
    | "admit", Ok _ -> []
    | "admit", Error err -> [ sprintf "expected admission, got %s at %s: %s" err.Code err.Path err.Message ]
    | "refuse", Ok _ -> [ "expected a refusal, the document decoded" ]
    | "refuse", Error err ->
        [ match c.ExpectedErrorCode with
          | Some code when err.Code <> code -> yield sprintf "expected code %s, got %s" code err.Code
          | None -> yield "a refuse case with no expectedErrorCode in the manifest"
          | _ -> ()

          match c.ExpectedPath with
          | Some p when err.Path <> p -> yield sprintf "expected path %s, got %s" p err.Path
          | None -> yield "a refuse case with no expectedPath in the manifest"
          | _ -> ()

          match c.RefusedKind with
          | Some k when not (err.Message.Contains k) ->
              yield sprintf "the refusal message must name the refused kind '%s': %s" k err.Message
          | _ -> ()

          // A refusal a host cannot act on is a failure of the surface even when
          // the code is right: the author has to learn WHICH declaration refused.
          if err.Code = "KIND_NOT_ADMITTED" then
              if not (err.Message.Contains policy.Identity) then
                  yield sprintf "the refusal must name the policy '%s': %s" policy.Identity err.Message

              if Option.isNone err.ExpectedShape then
                  yield "a KIND_NOT_ADMITTED refusal must carry the admitted vocabulary as ExpectedShape" ]
    | other, _ -> [ sprintf "unknown outcome '%s'" other ]

[<Tests>]
let family =
    testList
        "WIRE_FORMAT §23 — the decode-policy corpus family"
        [ yield
              testCase "the family is not empty" (fun () ->
                  Expect.isGreaterThan (List.length cases) 0 "decode-policy/manifest.json declares no cases")

          // The SHIPPED named profile against the corpus declaration. Every
          // case below resolves its policy from the manifest, which is right —
          // the corpus is the oracle — but it means the family says nothing
          // about `Policy.closedProfile`, the value a HOST actually consumes.
          // Found by perturbation: emptying `hatchNodeKinds` left the whole
          // family green while the shipped profile admitted both hatches, and
          // the two declaration tests above are self-referential and cannot see
          // it (they compute their expectation from the same list). This is the
          // assertion that ties the two together.
          yield
              testCase "the SHIPPED closed profile is the one the corpus declares" (fun () ->
                  let declared = resolve "closed-no-escape-hatches"

                  Expect.equal Policy.closedProfile.Identity declared.Identity "policy identity"

                  Expect.equal
                      Policy.closedProfile.Admission
                      declared.Admission
                      "the shipped closed profile must admit exactly what decode-policy/manifest.json declares")

          for c in cases do
              yield
                  testCase (sprintf "%s — %s" c.Id c.Description) (fun () ->
                      Expect.isEmpty (violations c (resolve c.Policy)) (sprintf "case %s" c.Id))

          // ── The go-red probe ──
          //
          // Every refusal above is caused by the policy or by something else,
          // and the passing test cannot tell you which. Re-running each refusal
          // case under a policy that ADMITS the refused kind must break it: if
          // the case still "passes", the refusal was never the policy's doing.
          yield
              testCase "a refusal case run under an admitting policy FAILS (negative probe)" (fun () ->
                  let refusals =
                      cases
                      |> List.filter (fun c -> c.Outcome = "refuse" && c.ExpectedErrorCode = Some "KIND_NOT_ADMITTED")

                  Expect.isGreaterThan (List.length refusals) 0 "no KIND_NOT_ADMITTED case to probe"

                  for c in refusals do
                      Expect.isNonEmpty
                          (violations c DecodePolicy.admitAll)
                          (sprintf
                              "case %s still reports a policy refusal at admitAll — the refusal is not caused by the policy"
                              c.Id))

          // ── The default is unchanged ──
          yield
              testCase "every family document decodes identically with no policy and at admitAll" (fun () ->
                  let disagreements =
                      [ for c in cases |> List.distinctBy (fun c -> c.Document) do
                            let text = readDocument c

                            match decodeNode text, decodeNodeWithPolicy DecodePolicy.admitAll text with
                            | Ok _, Ok _ -> ()
                            | Error a, Error b when a = b -> ()
                            | a, b -> yield sprintf "%s — %A vs %A" c.Document a b ]

                  Expect.isEmpty disagreements "the policy-less entry point must be admitAll exactly") ]

// ─── Ops carry kinds too ──────────────────────────────────────────────────

[<Tests>]
let opGating =
    testList
        "WIRE_FORMAT §23 — the op decoder"
        [ // A tree admitted under a policy and then EDITED into a refused kind
          // would make the policy a property of the first decode only, which is
          // not a closure at all. Both routes a kind takes into an op are gated.
          testCase "EditNode's replacement kind is gated" (fun () ->
              let op =
                  """{"$type":"EditNode","newKind":{"$type":"Custom","componentId":"c","moduleId":"m","props":{}},"target":"n1"}"""

              match decodeOpWithPolicy Policy.closedProfile op with
              | Ok _ -> failtest "an EditNode replacing a node with a Custom kind must be refused"
              | Error err ->
                  Expect.equal err.Code "KIND_NOT_ADMITTED" "code"
                  Expect.stringContains err.Path "$type" "the path names the discriminator"

              Expect.isTrue (Result.isOk (decodeOp op)) "and the same bytes decode with no policy declared")

          testCase "an inserted child's kind is gated" (fun () ->
              let op =
                  """{"$type":"InsertChild","child":{"id":"c1","kind":{"$type":"Custom","componentId":"c","moduleId":"m","props":{}}},"parentId":"p"}"""

              match decodeOpWithPolicy Policy.closedProfile op with
              | Ok _ -> failtest "an InsertChild carrying a Custom node must be refused"
              | Error err -> Expect.equal err.Code "KIND_NOT_ADMITTED" "code"

              Expect.isTrue (Result.isOk (decodeOp op)) "and the same bytes decode with no policy declared") ]

// ─── The authoring-side mirror ────────────────────────────────────────────

[<Tests>]
let preEmitMirror =
    testList
        "PreEmitValidate — FUARAN104"
        [ testCase "the lint reports what the decoder would refuse" (fun () ->
              // Same subject, both ends: the document the corpus family refuses
              // at decode, decoded (at admitAll) into the typed tree an authoring
              // host would hold, then linted against the same policy.
              let tree =
                  match decodeNodeObj (File.ReadAllText(Path.Combine(familyDir, "nested-custom.json"))) with
                  | Ok t -> t
                  | Error e -> failtestf "the family's nested fixture must decode at admitAll: %s" e.Code

              match Fuaran.UI.PreEmitValidate.validateWithPolicy Policy.closedProfile tree with
              | Ok() -> failtest "the closed profile must report the nested Custom"
              | Error defects ->
                  let reported =
                      defects
                      |> List.choose (function
                          | Fuaran.UI.PreEmitValidate.PreEmitDefect.KindNotAdmitted(nodeId, kind, policy) ->
                              Some(nodeId, kind, policy)
                          | _ -> None)

                  Expect.equal
                      reported
                      [ "policy-nested-custom-child", "Custom", "closed-no-escape-hatches" ]
                      "one defect, naming the node, the wire kind and the policy"

                  let code, severity, _ =
                      Fuaran.UI.PreEmitValidate.describe (
                          Fuaran.UI.PreEmitValidate.PreEmitDefect.KindNotAdmitted("n", "Custom", "p")
                      )

                  Expect.equal code "FUARAN104" "stable code"

                  // Advisory, deliberately: an authoring host may build a tree
                  // for a different deployment under a different policy, and the
                  // decode boundary is where a policy is enforced.
                  Expect.equal severity Fuaran.UI.PreEmitValidate.DefectSeverity.Warning "advisory severity")

          testCase "the plain walk is unchanged" (fun () ->
              let tree =
                  match decodeNodeObj (File.ReadAllText(Path.Combine(familyDir, "nested-custom.json"))) with
                  | Ok t -> t
                  | Error e -> failtestf "fixture must decode: %s" e.Code

              Expect.isTrue
                  (Result.isOk (Fuaran.UI.PreEmitValidate.validate tree))
                  "`validate` declares no policy, so FUARAN104 is unreachable through it") ]
