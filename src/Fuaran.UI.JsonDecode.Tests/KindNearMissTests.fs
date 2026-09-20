module Fuaran.UI.JsonDecode.Tests.KindNearMissTests

// ============================================================================
//  Near-miss diagnostics on the kind discriminator — Phase 1772.
//
//  The 2026-09-03 corpus triage recorded a naming-variance class with four
//  sub-shapes: a near-miss spelling of a shipped kind (`Split`, `Upload`), a
//  foreign-vocabulary leak (`Card`), category-versus-leaf (`TextInput`,
//  `Checkbox`) and mode-versus-kind (`MultiSelect`). It found them in AUTHORED
//  acceptance blocks, which is the point — models reach for the same names.
//
//  WHAT THE REFUSAL USED TO SAY, stated precisely because the shard did not.
//  The phase was written against "the decoder answers each with UNKNOWN_DU_CASE
//  and nothing else". Both halves are wrong on the tree as it stands: the code
//  is WRONG_NODE_KIND, and the refusal already carries `wrongNodeKindHint` — the
//  whole forty-two-kind vocabulary — as its `ExpectedShape`. So an author was
//  never told nothing; they were told everything, which for a token whose repair
//  is already written down is a different defect with the same cost. That is the
//  gap this file pins, and it is a smaller one than the shard claimed.
//
//  Three properties, and the second is the one that keeps the change safe:
//
//    1. Each RECORDED token is refused with its repair in the message.
//    2. A token the table does not carry is refused with a message BYTE-IDENTICAL
//       to the pre-1772 one. Pinned against a literal rather than against the
//       decoder's own format string, because a control built from the code under
//       test cannot fail when that code changes.
//    3. It stays a REFUSAL. No entry admits anything, and `Ok` is a failure here.
//
//  The table's own integrity is pinned in both directions (an entry that became
//  a real kind would be unreachable; a repair naming a kind that does not exist
//  would send an author somewhere there is nothing), and the last test holds the
//  line the corpus rests on: `Checkbox` is a FormFieldKind and still decodes as
//  one, so nothing here reaches the three `nodes/form-*.json` fixtures that carry
//  that token nested inside a `Form`.
// ============================================================================

open Expecto
open Fuaran.UI.Ops.JsonDecode

/// A minimal node carrying `token` as its kind discriminator. Nothing else is
/// present: the discriminator is read before any family decoder runs, so a
/// fuller node would add slots that could fail first and mask the refusal.
let private nodeWithKind (token: string) =
    sprintf """{"id":"probe","kind":{"$type":"%s"}}""" token

let private refusalFor (token: string) =
    match decodeNode (nodeWithKind token) with
    | Ok _ -> failtestf "expected '%s' to be REFUSED; it decoded" token
    | Error e -> e

/// The six tokens with the message each must now carry, written out in full
/// rather than composed from `nodeKindNearMisses`. Reading the repair off the
/// table under test would assert only that the decoder can read its own table —
/// these are the bytes an author actually sees, so they are pinned as bytes.
let private expectedMessages =
    [ "Split", "unknown NodeKind discriminator 'Split' — the shipped kind is 'SplitPanel'"
      "Upload", "unknown NodeKind discriminator 'Upload' — the shipped kind is 'FileUpload'"
      "Card", "unknown NodeKind discriminator 'Card' — the shipped kind is 'Box'"
      "TextInput",
      "unknown NodeKind discriminator 'TextInput' — not a node kind — a single-line text field is a 'Form' field with kind 'Text'"
      "Checkbox",
      "unknown NodeKind discriminator 'Checkbox' — not a node kind — a checkbox is a 'Form' field with kind 'Checkbox'"
      "MultiSelect",
      "unknown NodeKind discriminator 'MultiSelect' — not a node kind — multi-selection is the 'Select' kind with 'multiple' set" ]

[<Tests>]
let recordedTokensCarryTheirRepair =
    expectedMessages
    |> List.map (fun (token, expected) ->
        test (sprintf "'%s' is refused with the shipped name in the message" token) {
            let e = refusalFor token

            Expect.equal e.Code "WRONG_NODE_KIND" "the refusal code is unchanged by the diagnostic"
            Expect.equal e.Path "$.kind.$type" "the refusal still points at the discriminator"
            Expect.equal e.Message expected "the recorded repair rides the message, byte for byte"

            Expect.equal
                e.ExpectedShape
                (Some wrongNodeKindHint)
                "the whole vocabulary is still carried — the repair is added to it, not substituted for it"
        })
    |> testList "Phase 1772 — a recorded near-miss token is answered with its repair"

[<Tests>]
let tests =
    testList
        "Phase 1772 — the kind near-miss table"
        [
          // The acceptance's second clause, and the reason the repair is appended
          // to the old message rather than replacing it. `Turquoise` is the token
          // the vocabulary-request pipeline's own documentation uses for a
          // textbook unknown-kind sighting, so it is a token with no plausible
          // future claim on a table entry.
          test "a token NOT in the table is refused exactly as before" {
              let e = refusalFor "Turquoise"

              Expect.equal e.Code "WRONG_NODE_KIND" "unchanged"
              Expect.equal e.Path "$.kind.$type" "unchanged"

              Expect.equal
                  e.Message
                  "unknown NodeKind discriminator 'Turquoise'"
                  "an untabled token's message is byte-identical to the pre-1772 one"

              Expect.equal e.ExpectedShape (Some wrongNodeKindHint) "unchanged"
          }

          test "no near-miss token is admitted — the table changes what a refusal says, never whether it refuses" {
              for token, _ in nodeKindNearMisses do
                  match decodeNode (nodeWithKind token) with
                  | Ok _ ->
                      failtestf
                          "'%s' decoded — the table admitted a kind. It is a diagnostic, not an alias: no lenient vector may be added here."
                          token
                  | Error e ->
                      Expect.equal e.Code "WRONG_NODE_KIND" (sprintf "'%s' is still refused as an unknown kind" token)
          }

          // Both directions of the table's integrity. The first is what makes an
          // entry reachable at all; the second is what makes its advice true.
          test "every tabled token is absent from the shipped vocabulary" {
              for token, _ in nodeKindNearMisses do
                  Expect.isFalse
                      (List.contains token knownNodeKinds)
                      (sprintf
                          "'%s' is a SHIPPED kind, so its table entry can never be reached — the discriminator routes to a family decoder long before the fall-through. Remove the entry."
                          token)
          }

          test "every repair names a kind that exists" {
              // A repair's quoted tokens are a mix: some are kinds ('SplitPanel'),
              // some are control or slot names ('Text', 'multiple') that are
              // deliberately NOT kinds. So the rule is that at least one quoted
              // token resolves — a repair pointing at nothing real is the failure
              // worth catching — and that the "the shipped kind is 'X'" form,
              // which makes the strongest claim, resolves on X exactly.
              for token, repair in nodeKindNearMisses do
                  let quoted =
                      repair.Split('\'')
                      |> Array.mapi (fun i part -> i, part)
                      |> Array.filter (fun (i, _) -> i % 2 = 1)
                      |> Array.map snd
                      |> List.ofArray

                  Expect.isNonEmpty quoted (sprintf "'%s' repair names nothing at all" token)

                  Expect.isTrue
                      (quoted |> List.exists (fun q -> List.contains q knownNodeKinds))
                      (sprintf "'%s' repair names no shipped kind: %s" token repair)

                  let prefix = "the shipped kind is '"

                  if repair.StartsWith(prefix, System.StringComparison.Ordinal) then
                      let named = repair.Substring(prefix.Length).TrimEnd('\'')

                      Expect.isTrue
                          (List.contains named knownNodeKinds)
                          (sprintf "'%s' claims the shipped kind is '%s', which is not a kind" token named)
          }

          // The guarantee the wire corpus rests on. `Checkbox` is a FormFieldKind
          // and three committed node fixtures carry it nested inside a `Form`; the
          // table lives at the NODE discriminator's fall-through, which those
          // documents never reach. Asserted rather than argued, because the two
          // vocabularies sharing a token is exactly the kind of fact that reads as
          // safe right up until someone moves the lookup.
          test "'Checkbox' still decodes as a form field — the table is at the node discriminator only" {
              // The committed bytes of `nodes/form-toggle.json`, inline rather than
              // loaded: the claim is about THIS document, and a fixture read through
              // the corpus resolver would make the test skip where the corpus is
              // absent — which is silence exactly where the guarantee is wanted.
              let json =
                  """{"id":"form-toggle","kind":{"$type":"Form","fields":[{"id":"irrigation-running","kind":{"$type":"Toggle"},"label":"Irrigation","required":false},{"id":"accept-terms","kind":{"$type":"Checkbox"},"label":"I accept the terms","required":true}],"onSubmit":{"$type":"Chain","ops":[]},"submitLabel":"Save"}}"""

              match decodeNode json with
              | Ok _ -> ()
              | Error e ->
                  failtestf
                      "a Form carrying a Checkbox field must still decode; got %s at %s: %s"
                      e.Code
                      e.Path
                      e.Message
          } ]
