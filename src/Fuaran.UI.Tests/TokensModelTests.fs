module Fuaran.UI.Tests.TokensModel

// ============================================================================
//  The token control's judgements (Phase 1121).
//
//  `RatingModelTests`' reason, and a wider surface. The .NET test runner mounts
//  no DOM and Feliz's .NET `ReactElement` is opaque, so a keyboard model written
//  inline in the React component could only be asserted ABOUT. This control has
//  more model than any other in the vocabulary — every claim below is a claim
//  about a decision the reader can SEE go wrong and nobody can see in prose:
//
//    * an entry that repeats a token   → refused, not appended twice
//    * a closed field, an off-list     → refused, and the refusal SAYS which
//      entry                             token, so the reader is not ignored
//    * an open field                   → the same entry accepted
//    * a label typed instead of a      → the VALUE commits, because that is
//      value                             what submits
//    * Backspace on an empty entry     → the LAST token, not the first
//    * a removal at a stale index      → a no-op, never a throw
//    * a token already chosen          → not offered again in the popup
//    * the SSR comma projection        → round-trips, and loses a comma-bearing
//                                        token (a recorded limit, pinned here so
//                                        it stays a KNOWN one)
//
//  The last is the one worth stating: the limit is asserted rather than merely
//  documented, so a future host that "fixes" it by inventing a quoting grammar
//  fails here first and has to change the spec instead.
// ============================================================================

open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Renderer.TokensModel

let private opt (label: string) (value: string) : SelectOption = { Label = label; Value = value }

let private suggestions =
    [ opt "France" "fra"; opt "Germany" "deu"; opt "Spain" "esp" ]

[<Tests>]
let tests =
    testList
        "TokensModel"
        [ test "an entry is trimmed and otherwise left exactly as typed" {
              Expect.equal (normalise "  urgent  ") "urgent" "Surrounding space is invisible to the reader who typed it"
              // Case is NOT folded, deliberately: a token is a value a document
              // may carry as a literal and a server may match exactly, so a
              // control that rewrote `React` to `react` would make the two
              // disagree about whether a token is a duplicate.
              Expect.equal (normalise "React") "React" "Case is preserved, never folded"
              Expect.equal (normalise "   ") "" "Whitespace alone is nothing"
          }

          test "an empty entry is nothing, not an error" {
              // Enter on an empty box means "nothing". A control that reported a
              // refusal here would scold a reader for a keystroke that asked for
              // nothing at all.
              Expect.equal (tryAdd true None [] "") AddOutcome.Empty "An empty entry"
              Expect.equal (tryAdd true None [] "   ") AddOutcome.Empty "Whitespace is empty too"
          }

          test "an open field accepts free text, appended in reader order" {
              match tryAdd true None [ "alpha" ] "beta" with
              | AddOutcome.Accepted next ->
                  // APPENDED, not sorted and not prepended: the chip order is a
                  // fact the reader can see, and the whole slot is rewritten on
                  // every add precisely so that order survives a decode.
                  Expect.equal next [ "alpha"; "beta" ] "The new token lands last"
              | other -> failtestf "Expected the token to be accepted, got %A" other
          }

          test "a repeated token is refused, whatever the field admits" {
              // Two identical chips are one fact drawn twice, with two remove
              // buttons that do different things. Refused on an OPEN field too —
              // duplication is a property of the list, not of the suggestion set.
              Expect.equal
                  (tryAdd true None [ "alpha" ] "alpha")
                  (AddOutcome.Duplicate "alpha")
                  "An open field still refuses a repeat"

              Expect.equal
                  (tryAdd false (Some suggestions) [ "fra" ] "fra")
                  (AddOutcome.Duplicate "fra")
                  "And so does a closed one"
          }

          test "a closed field refuses an off-list entry, and names it" {
              // The refusal carries the token because the control announces it:
              // a keystroke ignored without a reason reads as a broken control.
              Expect.equal
                  (tryAdd false (Some suggestions) [] "Portugal")
                  (AddOutcome.NotSuggested "Portugal")
                  "Not one of the suggestions"

              // The SAME entry on an open field is accepted — which is the whole
              // difference the flag makes, asserted rather than assumed.
              match tryAdd true (Some suggestions) [] "Portugal" with
              | AddOutcome.Accepted next -> Expect.equal next [ "Portugal" ] "An open field takes it"
              | other -> failtestf "Expected acceptance on an open field, got %A" other
          }

          test "typing a label commits the VALUE" {
              // A reader types what they can see, and what they can see is the
              // label; what submits is the value. Both spellings are admitted
              // and only one is stored.
              match tryAdd false (Some suggestions) [] "France" with
              | AddOutcome.Accepted next -> Expect.equal next [ "fra" ] "The label resolves to its value"
              | other -> failtestf "Expected the label to resolve, got %A" other

              match tryAdd false (Some suggestions) [] "deu" with
              | AddOutcome.Accepted next -> Expect.equal next [ "deu" ] "The value is admitted as itself"
              | other -> failtestf "Expected the value to be admitted, got %A" other
          }

          test "a label that resolves to an already-chosen value is a duplicate" {
              // The resolution happens BEFORE the duplicate check, so typing
              // "France" when `fra` is already a chip is a repeat and not a
              // second token that happens to look the same.
              Expect.equal
                  (tryAdd false (Some suggestions) [ "fra" ] "France")
                  (AddOutcome.Duplicate "fra")
                  "Resolved first, then checked"
          }

          test "Backspace on an empty entry takes the LAST token" {
              Expect.equal (removeLast [ "a"; "b"; "c" ]) [ "a"; "b" ] "The last one, not the first"
              // An empty list is a no-op rather than a throw: the key falls
              // through to the browser rather than being swallowed by a control
              // with nothing to undo.
              Expect.equal (removeLast []) [] "Nothing to take back"
          }

          test "a removal at a stale index is a no-op, never a throw" {
              // The index comes from a DOM event on a row this model does not
              // own. A stale one is a race, and a race must not be a crash.
              Expect.equal (removeAt 1 [ "a"; "b"; "c" ]) [ "a"; "c" ] "The middle one goes"
              Expect.equal (removeAt 9 [ "a" ]) [ "a" ] "Past the end changes nothing"
              Expect.equal (removeAt -1 [ "a" ]) [ "a" ] "Before the start changes nothing"
          }

          test "the popup never offers a token the reader already has" {
              // THE one place this differs from the combobox filter, and the
              // reason the function is here rather than inlined: a single-value
              // combobox may re-offer its committed value, and a multi-token
              // control offering a chip the reader already has is offering a
              // gesture that would be refused.
              let visible = visibleSuggestions suggestions [ "fra" ] ""
              Expect.equal (visible |> List.map (fun o -> o.Value)) [ "deu"; "esp" ] "France is already a chip"
          }

          test "an empty query shows everything, and a query filters on label or value" {
              Expect.equal (visibleSuggestions suggestions [] "" |> List.length) 3 "Browsing the whole set"

              Expect.equal
                  (visibleSuggestions suggestions [] "ger" |> List.map (fun o -> o.Value))
                  [ "deu" ]
                  "Matched on the label, case-insensitively"

              Expect.equal
                  (visibleSuggestions suggestions [] "esp" |> List.map (fun o -> o.Value))
                  [ "esp" ]
                  "Matched on the value"
          }

          test "a chip reads as its label where the set knows it, and as itself otherwise" {
              Expect.equal (chipLabel (Some suggestions) "fra") "France" "The known token reads as its label"
              // Every free-text token takes this branch, and so does every token
              // whose asynchronous suggestion source has not resolved yet. Never
              // blank — a chip with no text is a chip a reader cannot decide to
              // remove.
              Expect.equal (chipLabel (Some suggestions) "urgent") "urgent" "An unknown token reads as itself"
              Expect.equal (chipLabel None "urgent") "urgent" "With no source at all, likewise"
          }

          test "the SSR comma projection round-trips an ordinary token list" {
              let tokens = [ "alpha"; "beta"; "gamma" ]
              Expect.equal (toCommaSeparated tokens) "alpha, beta, gamma" "One separator, defined once"
              Expect.equal (fromCommaSeparated (toCommaSeparated tokens)) tokens "And back, in order"
          }

          test "the floor's inverse drops empties and repeats, keeping the first" {
              Expect.equal (fromCommaSeparated "a, , b,a ,c") [ "a"; "b"; "c" ] "Trimmed, de-duplicated, in order"
              Expect.equal (fromCommaSeparated "") [] "An empty box is no tokens"
          }

          test "RECORDED LIMIT — a comma-bearing token does not survive the SSR floor" {
              // Asserted rather than merely documented, and deliberately so: the
              // limit is stated in `docs/SSR.md` and in the wire specification,
              // and a future host that "fixes" it by inventing a quoting grammar
              // fails HERE first and has to change the specification instead of
              // quietly diverging from every other host's floor.
              let tokens = [ "Smith, John" ]
              Expect.equal (toCommaSeparated tokens) "Smith, John" "The projection is lossy by construction"

              Expect.equal
                  (fromCommaSeparated (toCommaSeparated tokens))
                  [ "Smith"; "John" ]
                  "It re-parses as two — the known limit, pinned"
          } ]
