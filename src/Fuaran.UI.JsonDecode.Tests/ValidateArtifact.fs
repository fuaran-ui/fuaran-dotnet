module Fuaran.UI.JsonDecode.Tests.ValidateArtifact

// ============================================================================
//  The generated pre-emit VALIDATE family (Phase 2381) - `validate/manifest.json`,
//  a self-enumerated family (WIRE_FORMAT 27.7) whose normative sentence is
//  27.8.2's `validate` entry.
//
//  The question it answers: given a document, which pre-emit defects does it
//  carry, and on which node? `validator/` fixes the defect VOCABULARY (codes,
//  severities, message concepts); nothing in the corpus said which of those
//  codes a given document raises, so two hosts implementing the same rule could
//  disagree on a document with every coverage check green.
//
//  Each vector is a tree. Its expected defects are NOT declared: they are what
//  the reference validator reports on the tree the reference decoder reads,
//  each with the node `findingOf` names and the location the validator's own
//  walk gives it (`PreEmitValidate.validateWithPaths`): the JSONPath of the
//  occurrence that raised it, in the vector's canonical bytes and the 29.3
//  grammar (`.name` members, whatever the key, and `[i]` indices). Nothing here
//  looks an id up (27.8.2). So a host that reports a path for a finding can be
//  held to the spelling: its path extends the vector's at a segment boundary,
//  and `$.kind.children.0` (a member named `0`) extends nothing.
//
//  Co-emitted by `Corpus.emit`, and writable alone with `--emit-validate
//  [<dir>]`. `EmittedFamilyTests` asserts byte-equality with the committed file
//  and reads every committed vector back through the reference.
// ============================================================================

open System.IO
open System.Text.Encodings.Web
open System.Text.Json

open Fuaran.UI
open Fuaran.UI.Ops
open Fuaran.UI.OpStream.Abstractions

[<Literal>]
let artifactId = "https://fuaran.dev/wire-format/v1/validate/manifest.json"

/// The family's directory at the corpus root; the family is named by it.
[<Literal>]
let directory = "validate"

[<Literal>]
let fileName = "manifest.json"

/// One vector: a document and why it is in the family.
type Vector =
    { Id: string
      Description: string
      Tree: string }

/// One defect the reference reports: its code and severity, the node it is
/// raised on (absent where the defect names none), and that node's JSONPath.
type Defect =
    { Code: string
      Severity: string
      Node: string option
      Path: string option }

let private v id description tree =
    { Id = id
      Description = description
      Tree = tree }

let all: Vector list =
    [ v
          "validate-clean-nested"
          "The control: nested Boxes holding Markdown report no defect, so a host that raises anything here disagrees."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"intro","kind":{"$type":"Markdown","text":"Intro"}},{"id":"inner","kind":{"$type":"Box","children":[{"id":"body","kind":{"$type":"Markdown","text":"Body"}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""
      v
          "validate-switch-empty-statekey-at-index-1"
          "A Switch with an empty stateKey (FUARAN083) as the second child of a Box. The node's path is $.kind.children[1]: an array index is spelled [i] in a finding's location, as in a decode path (sections 6 and 29.3)."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"intro","kind":{"$type":"Markdown","text":"Intro"}},{"id":"sw","kind":{"$type":"Switch","cases":[{"child":{"id":"sw-a","kind":{"$type":"Markdown","text":"A"}},"match":"a"}],"default":{"id":"sw-d","kind":{"$type":"Markdown","text":"D"}},"stateKey":""}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""
      v
          "validate-switch-empty-statekey-at-index-10"
          "The same defect on the eleventh child, so the index has two digits: $.kind.children[10]. A host that orders or compares paths as strings, or spells the index as a dotted segment, disagrees here."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"m0","kind":{"$type":"Markdown","text":"0"}},{"id":"m1","kind":{"$type":"Markdown","text":"1"}},{"id":"m2","kind":{"$type":"Markdown","text":"2"}},{"id":"m3","kind":{"$type":"Markdown","text":"3"}},{"id":"m4","kind":{"$type":"Markdown","text":"4"}},{"id":"m5","kind":{"$type":"Markdown","text":"5"}},{"id":"m6","kind":{"$type":"Markdown","text":"6"}},{"id":"m7","kind":{"$type":"Markdown","text":"7"}},{"id":"m8","kind":{"$type":"Markdown","text":"8"}},{"id":"m9","kind":{"$type":"Markdown","text":"9"}},{"id":"sw","kind":{"$type":"Switch","cases":[{"child":{"id":"sw-a","kind":{"$type":"Markdown","text":"A"}},"match":"a"}],"default":{"id":"sw-d","kind":{"$type":"Markdown","text":"D"}},"stateKey":""}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""
      v
          "validate-switch-duplicate-match-in-a-case-arm"
          "A Switch whose two cases match the same value (FUARAN082), held in the first case arm of an outer Switch. The node's path is $.kind.cases[0].child: an arm of an ordered list of records carries its index too. Neither state key has a writer in the tree, so the reference also warns FUARAN103 on both Switches, the outer one at the root path $."
          """{"id":"outer","kind":{"$type":"Switch","cases":[{"child":{"id":"inner","kind":{"$type":"Switch","cases":[{"child":{"id":"inner-a","kind":{"$type":"Markdown","text":"A"}},"match":"x"},{"child":{"id":"inner-b","kind":{"$type":"Markdown","text":"B"}},"match":"x"}],"default":{"id":"inner-d","kind":{"$type":"Markdown","text":"D"}},"stateKey":"inner"}},"match":"one"}],"default":{"id":"outer-d","kind":{"$type":"Markdown","text":"D"}},"stateKey":"outer"}}"""
      v
          "validate-duplicate-id-across-default-and-child"
          "One id held by a Box's child and by a Switch's default arm (FUARAN-DUP-ID). The defect is located at the id's SECOND node in document order, $.kind.children[1].kind.default: the node to change is the later one. The Switch's state key has no writer, so the reference also warns FUARAN103 on it."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"twice","kind":{"$type":"Markdown","text":"First"}},{"id":"sw","kind":{"$type":"Switch","cases":[{"child":{"id":"sw-a","kind":{"$type":"Markdown","text":"A"}},"match":"a"}],"default":{"id":"twice","kind":{"$type":"Markdown","text":"Second"}},"stateKey":"view"}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""
      v
          "validate-duplicate-id-three-times"
          "One id held by three nodes, the third one level down (FUARAN-DUP-ID). Three occurrences are TWO findings, one at each occurrence after the first, in document order: $.kind.children[1] and $.kind.children[2].kind.children[0]. The count of findings is the count of nodes to change."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"thrice","kind":{"$type":"Markdown","text":"One"}},{"id":"thrice","kind":{"$type":"Markdown","text":"Two"}},{"id":"inner","kind":{"$type":"Box","children":[{"id":"thrice","kind":{"$type":"Markdown","text":"Three"}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""
      v
          "validate-duplicate-id-in-a-list-of-twelve"
          "One id held by the third and the eleventh of twelve children (FUARAN-DUP-ID). Document order puts children[2] before children[10], so the one finding is at $.kind.children[10]. A host that walks or orders positions as strings meets children[10] first and reports children[2]."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"m0","kind":{"$type":"Markdown","text":"0"}},{"id":"m1","kind":{"$type":"Markdown","text":"1"}},{"id":"twice","kind":{"$type":"Markdown","text":"2"}},{"id":"m3","kind":{"$type":"Markdown","text":"3"}},{"id":"m4","kind":{"$type":"Markdown","text":"4"}},{"id":"m5","kind":{"$type":"Markdown","text":"5"}},{"id":"m6","kind":{"$type":"Markdown","text":"6"}},{"id":"m7","kind":{"$type":"Markdown","text":"7"}},{"id":"m8","kind":{"$type":"Markdown","text":"8"}},{"id":"m9","kind":{"$type":"Markdown","text":"9"}},{"id":"twice","kind":{"$type":"Markdown","text":"10"}},{"id":"m11","kind":{"$type":"Markdown","text":"11"}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""
      v
          "validate-duplicate-id-in-the-node-envelope"
          "One id held by the root's envelope fallback and by its child (FUARAN-DUP-ID). The fallback is walked (section 8.1), and the canonical document lists it before the kind (member order), so the fallback is the first occurrence and the finding is at $.kind.children[0]. A host that walks the kind before the envelope reports $.fallback."
          """{"fallback":{"id":"twice","kind":{"$type":"Markdown","text":"Fallback"}},"id":"root","kind":{"$type":"Box","children":[{"id":"twice","kind":{"$type":"Markdown","text":"Child"}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""
      v
          "validate-duplicate-id-in-the-state-alternatives"
          "One id held by the root's child and by both of the root's state alternatives (FUARAN-DUP-ID). A state alternative is a member of the node object, so it is part of the document (section 8.1): three occurrences are two findings, at $.state.onEmpty and $.state.onLoading. A host that does not walk the state alternatives reports none."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"X","kind":{"$type":"Markdown","text":"t"}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"},"state":{"onEmpty":{"id":"X","kind":{"$type":"Markdown","text":"t"}},"onLoading":{"id":"X","kind":{"$type":"Markdown","text":"t"}}}}"""
      v
          "validate-duplicate-id-in-the-envelope-kind-and-state"
          "One id held by the root's fallback, by its first child and by both state alternatives of its second child (FUARAN-DUP-ID). Document order puts a node's fallback before its kind and its kind before its state, so the fallback is the first occurrence and the three findings are at $.kind.children[0], $.kind.children[1].state.onEmpty and $.kind.children[1].state.onLoading."
          """{"fallback":{"id":"X","kind":{"$type":"Markdown","text":"t"}},"id":"root","kind":{"$type":"Box","children":[{"id":"X","kind":{"$type":"Markdown","text":"t"}},{"id":"c1","kind":{"$type":"Markdown","text":"t"},"state":{"onEmpty":{"id":"X","kind":{"$type":"Markdown","text":"t"}},"onLoading":{"id":"X","kind":{"$type":"Markdown","text":"t"}}}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""
      v
          "validate-duplicate-id-across-a-case-the-default-and-the-state"
          "One id held by a Switch's case arm, its default and its own state.onEmpty alternative (FUARAN-DUP-ID): findings at $.kind.default and $.state.onEmpty. The Switch's state key has no writer, so the reference also warns FUARAN103 on it."
          """{"id":"root","kind":{"$type":"Switch","cases":[{"child":{"id":"X","kind":{"$type":"Markdown","text":"t"}},"match":"a"}],"default":{"id":"X","kind":{"$type":"Markdown","text":"t"}},"stateKey":"k"},"state":{"onEmpty":{"id":"X","kind":{"$type":"Markdown","text":"t"}}}}"""
      v
          "validate-duplicate-id-in-a-state-alternative-then-a-sibling"
          "One id held by the onEmpty alternative of the first child and by the second child (FUARAN-DUP-ID). The alternative comes first in document order, so the one finding is at $.kind.children[1]."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"c0","kind":{"$type":"Markdown","text":"t"},"state":{"onEmpty":{"id":"X","kind":{"$type":"Markdown","text":"t"}}}},{"id":"X","kind":{"$type":"Markdown","text":"t"}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""
      v
          "validate-switch-empty-statekey-in-a-state-alternative"
          "A Switch with an empty stateKey (FUARAN083) held as the onLoading alternative of a child. Every rule judges a node in a state alternative as it judges one in the tree, so the finding is at $.kind.children[0].state.onLoading."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"c0","kind":{"$type":"Markdown","text":"t"},"state":{"onLoading":{"id":"s","kind":{"$type":"Switch","cases":[{"child":{"id":"sa","kind":{"$type":"Markdown","text":"t"}},"match":"a"}],"default":{"id":"sd","kind":{"$type":"Markdown","text":"t"}},"stateKey":""}}}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""
      v
          "validate-defect-on-the-second-of-two-nodes-sharing-an-id"
          "Two Switches share one id and only the SECOND has an empty stateKey (FUARAN083). A finding is located at the occurrence that raised it, so FUARAN083 is at $.kind.children[1], not at the first node holding the id. The first Switch's state key has no writer: FUARAN103, though judged across the whole tree, is raised by the Switch whose key it names, so it is at $.kind.children[0]."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"sw","kind":{"$type":"Switch","cases":[{"child":{"id":"a1","kind":{"$type":"Markdown","text":"t"}},"match":"a"}],"default":{"id":"d1","kind":{"$type":"Markdown","text":"t"}},"stateKey":"k1"}},{"id":"sw","kind":{"$type":"Switch","cases":[{"child":{"id":"a2","kind":{"$type":"Markdown","text":"t"}},"match":"a"}],"default":{"id":"d2","kind":{"$type":"Markdown","text":"t"}},"stateKey":""}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""
      v
          "validate-duplicate-id-two-ids-each-twice"
          "Two ids each held twice, in the order a, a, b, b (FUARAN-DUP-ID). One finding at each occurrence after the first: two findings, at $.kind.children[1] and $.kind.children[3]."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"a","kind":{"$type":"Markdown","text":"1"}},{"id":"a","kind":{"$type":"Markdown","text":"2"}},{"id":"b","kind":{"$type":"Markdown","text":"3"}},{"id":"b","kind":{"$type":"Markdown","text":"4"}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""
      v
          "validate-switch-match-repeated-three-times"
          "A Switch whose three cases match one value (FUARAN082). A finding whose subject is a repetition is one finding at each occurrence after the first, so x, x, x is two findings, both on the Switch at $: one per dead case. The state key has no writer, so the reference also warns FUARAN103."
          """{"id":"root","kind":{"$type":"Switch","cases":[{"child":{"id":"a","kind":{"$type":"Markdown","text":"t"}},"match":"x"},{"child":{"id":"b","kind":{"$type":"Markdown","text":"t"}},"match":"x"},{"child":{"id":"c","kind":{"$type":"Markdown","text":"t"}},"match":"x"}],"default":{"id":"d","kind":{"$type":"Markdown","text":"t"}},"stateKey":"k"}}"""
      v
          "validate-switch-two-matches-each-repeated"
          "A Switch whose cases match x, x, y, y (FUARAN082): two findings, one at each case after the first that repeats a value, not one per repeated value. The state key has no writer, so the reference also warns FUARAN103."
          """{"id":"root","kind":{"$type":"Switch","cases":[{"child":{"id":"a","kind":{"$type":"Markdown","text":"t"}},"match":"x"},{"child":{"id":"b","kind":{"$type":"Markdown","text":"t"}},"match":"x"},{"child":{"id":"c","kind":{"$type":"Markdown","text":"t"}},"match":"y"},{"child":{"id":"d","kind":{"$type":"Markdown","text":"t"}},"match":"y"}],"default":{"id":"e","kind":{"$type":"Markdown","text":"t"}},"stateKey":"k"}}"""
      v
          "validate-tree-row-id-repeated-three-times"
          "A Tree whose three rows share one id (FUARAN126): x, x, x is two findings, both on the Tree at $."
          """{"id":"t","kind":{"$type":"Tree","items":[{"id":"x","label":"Row 0"},{"id":"x","label":"Row 1"},{"id":"x","label":"Row 2"}]}}"""
      v
          "validate-tree-row-ids-each-repeated"
          "A Tree whose rows carry the ids x, x, y, y (FUARAN126): two findings, one at each row after the first that repeats an id."
          """{"id":"t","kind":{"$type":"Tree","items":[{"id":"x","label":"Row 0"},{"id":"x","label":"Row 1"},{"id":"y","label":"Row 2"},{"id":"y","label":"Row 3"}]}}"""
      v
          "validate-static-token-repeated-three-times"
          "A Tokens field whose static value lists one token three times (FUARAN136): two findings, both on the Form at $. The field has no handler and no writable binding, so the reference also warns FUARAN069."
          """{"id":"f","kind":{"$type":"Form","fields":[{"id":"labels","kind":{"$type":"Tokens","value":{"$type":"Static","value":["x","x","x"]}},"label":"Labels"}],"onSubmit":{"$type":"Chain","ops":[]},"submitLabel":"Save"}}"""
      v
          "validate-static-tokens-each-repeated"
          "A Tokens field whose static value is x, x, y, y (FUARAN136): two findings, one at each token after the first that repeats one. The reference also warns FUARAN069."
          """{"id":"f","kind":{"$type":"Form","fields":[{"id":"labels","kind":{"$type":"Tokens","value":{"$type":"Static","value":["x","x","y","y"]}},"label":"Labels"}],"onSubmit":{"$type":"Chain","ops":[]},"submitLabel":"Save"}}"""
      v
          "validate-write-back-key-repeated-three-times"
          "Three Forms whose handler-free fields all write back to the state key email (FUARAN085): x, x, x is two findings, at the second and the third writer, each located at the Form that holds it ($.kind.children[1], $.kind.children[2])."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"f1","kind":{"$type":"Form","fields":[{"id":"email","kind":{"$type":"Text","value":{"$type":"State","key":"email"}},"label":"email"}],"onSubmit":{"$type":"Chain","ops":[]},"submitLabel":"Send"}},{"id":"f2","kind":{"$type":"Form","fields":[{"id":"email","kind":{"$type":"Text","value":{"$type":"State","key":"email"}},"label":"email"}],"onSubmit":{"$type":"Chain","ops":[]},"submitLabel":"Send"}},{"id":"f3","kind":{"$type":"Form","fields":[{"id":"email","kind":{"$type":"Text","value":{"$type":"State","key":"email"}},"label":"email"}],"onSubmit":{"$type":"Chain","ops":[]},"submitLabel":"Send"}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""
      v
          "validate-write-back-keys-each-repeated"
          "Two Forms whose handler-free fields write back to the keys email and phone, each twice (FUARAN085): x, x, y, y is two findings, one at each writer after the first of its key; both writers after the first are in the second Form, $.kind.children[1]."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"f1","kind":{"$type":"Form","fields":[{"id":"email","kind":{"$type":"Text","value":{"$type":"State","key":"email"}},"label":"email"},{"id":"phone","kind":{"$type":"Text","value":{"$type":"State","key":"phone"}},"label":"phone"}],"onSubmit":{"$type":"Chain","ops":[]},"submitLabel":"Send"}},{"id":"f2","kind":{"$type":"Form","fields":[{"id":"email","kind":{"$type":"Text","value":{"$type":"State","key":"email"}},"label":"email"},{"id":"phone","kind":{"$type":"Text","value":{"$type":"State","key":"phone"}},"label":"phone"}],"onSubmit":{"$type":"Chain","ops":[]},"submitLabel":"Send"}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""
      v
          "validate-write-back-key-repeated-on-a-repeated-id"
          "The second writer of the key email is the second of two Forms sharing the id f2 (FUARAN085): the finding is at the occurrence that holds the writer, $.kind.children[2], not at the first node holding f2."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"f1","kind":{"$type":"Form","fields":[{"id":"email","kind":{"$type":"Text","value":{"$type":"State","key":"email"}},"label":"email"}],"onSubmit":{"$type":"Chain","ops":[]},"submitLabel":"Send"}},{"id":"f2","kind":{"$type":"Form","fields":[{"id":"phone","kind":{"$type":"Text","value":{"$type":"State","key":"phone"}},"label":"phone"}],"onSubmit":{"$type":"Chain","ops":[]},"submitLabel":"Send"}},{"id":"f2","kind":{"$type":"Form","fields":[{"id":"email","kind":{"$type":"Text","value":{"$type":"State","key":"email"}},"label":"email"}],"onSubmit":{"$type":"Chain","ops":[]},"submitLabel":"Send"}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""
      v
          "validate-state-seed-declared-three-times"
          "Three Metrics declare the state key k with the defaults 1, 2 and 3 (FUARAN106): the first declaration is the one that takes effect, so x, x, x is two findings, at the second and the third declaration ($.kind.children[1], $.kind.children[2])."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"m1","kind":{"$type":"Metric","label":"m1","value":{"$type":"State","defaultValue":1,"key":"k"}}},{"id":"m2","kind":{"$type":"Metric","label":"m2","value":{"$type":"State","defaultValue":2,"key":"k"}}},{"id":"m3","kind":{"$type":"Metric","label":"m3","value":{"$type":"State","defaultValue":3,"key":"k"}}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""
      v
          "validate-state-seeds-each-declared-twice"
          "The keys k and j are each declared twice with different defaults (FUARAN106): x, x, y, y is two findings, one at the second declaration of each key ($.kind.children[1], $.kind.children[3])."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"m1","kind":{"$type":"Metric","label":"m1","value":{"$type":"State","defaultValue":1,"key":"k"}}},{"id":"m2","kind":{"$type":"Metric","label":"m2","value":{"$type":"State","defaultValue":2,"key":"k"}}},{"id":"m3","kind":{"$type":"Metric","label":"m3","value":{"$type":"State","defaultValue":1,"key":"j"}}},{"id":"m4","kind":{"$type":"Metric","label":"m4","value":{"$type":"State","defaultValue":2,"key":"j"}}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""
      v
          "validate-state-seed-agreeing-then-disagreeing"
          "The key k is declared 1, 1 and 2 (FUARAN106): a later declaration that agrees with the first changes nothing, so the one finding is at the third, $.kind.children[2]."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"m1","kind":{"$type":"Metric","label":"m1","value":{"$type":"State","defaultValue":1,"key":"k"}}},{"id":"m2","kind":{"$type":"Metric","label":"m2","value":{"$type":"State","defaultValue":1,"key":"k"}}},{"id":"m3","kind":{"$type":"Metric","label":"m3","value":{"$type":"State","defaultValue":2,"key":"k"}}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""
      v
          "validate-state-seed-disagreeing-on-a-repeated-id"
          "The key k is declared 1 by m1 and by the first of two Metrics sharing the id m2, and 2 by the second (FUARAN106): the finding is at the declaration that disagrees, $.kind.children[2], not at the first node holding m2."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"m1","kind":{"$type":"Metric","label":"m1","value":{"$type":"State","defaultValue":1,"key":"k"}}},{"id":"m2","kind":{"$type":"Metric","label":"m2","value":{"$type":"State","defaultValue":1,"key":"k"}}},{"id":"m2","kind":{"$type":"Metric","label":"m2","value":{"$type":"State","defaultValue":2,"key":"k"}}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""
      v
          "validate-state-seed-in-a-child-then-in-the-own-state-alternative"
          "The key k is declared 1 by the root's child and 2 by the root's own state.onEmpty alternative (FUARAN106). Document order puts a node's kind before its state, so the child's declaration is the first and the finding is at the alternative, $.state.onEmpty."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"a1","kind":{"$type":"Metric","label":"a1","value":{"$type":"State","defaultValue":1,"key":"k"}}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"},"state":{"onEmpty":{"id":"a2","kind":{"$type":"Metric","label":"a2","value":{"$type":"State","defaultValue":2,"key":"k"}}}}}"""
      v
          "validate-state-seed-in-the-fallback-then-in-the-own-state-alternative"
          "The key k is declared 1 by the root's envelope fallback and 2 by its own state.onEmpty alternative (FUARAN106). The fallback comes first in document order, so the finding is at the alternative, $.state.onEmpty."
          """{"fallback":{"id":"a1","kind":{"$type":"Metric","label":"a1","value":{"$type":"State","defaultValue":1,"key":"k"}}},"id":"root","kind":{"$type":"Box","children":[{"id":"c","kind":{"$type":"Markdown","text":"t"}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"},"state":{"onEmpty":{"id":"a2","kind":{"$type":"Metric","label":"a2","value":{"$type":"State","defaultValue":2,"key":"k"}}}}}"""
      v
          "validate-inline-table-in-a-child-then-in-the-own-state-alternative"
          "The root's child and the root's own state.onEmpty alternative carry the same inline table (FUARAN107). Document order puts a node's kind before its state, so the copy is the alternative, $.state.onEmpty."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"g1","kind":{"$type":"DataGrid","columns":[{"field":"a","kind":{"$type":"Text"},"label":"a"}],"rowKeyField":"a","source":{"$type":"Static","value":[{"a":1},{"a":2}]}}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"},"state":{"onEmpty":{"id":"g2","kind":{"$type":"DataGrid","columns":[{"field":"a","kind":{"$type":"Text"},"label":"a"}],"rowKeyField":"a","source":{"$type":"Static","value":[{"a":1},{"a":2}]}}}}}"""
      v
          "validate-inline-table-in-the-fallback-then-in-the-own-state-alternative"
          "The root's envelope fallback and its own state.onEmpty alternative carry the same inline table (FUARAN107). The fallback comes first in document order, so the copy is the alternative, $.state.onEmpty."
          """{"fallback":{"id":"g1","kind":{"$type":"DataGrid","columns":[{"field":"a","kind":{"$type":"Text"},"label":"a"}],"rowKeyField":"a","source":{"$type":"Static","value":[{"a":1},{"a":2}]}}},"id":"root","kind":{"$type":"Box","children":[{"id":"c","kind":{"$type":"Markdown","text":"t"}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"},"state":{"onEmpty":{"id":"g2","kind":{"$type":"DataGrid","columns":[{"field":"a","kind":{"$type":"Text"},"label":"a"}],"rowKeyField":"a","source":{"$type":"Static","value":[{"a":1},{"a":2}]}}}}}"""
      v
          "validate-filter-edge-in-the-fallback"
          "A Metric held by the root's envelope fallback depends on the filter region, which no Filters chip declares (FUARAN075). Every node of the document is judged (section 8.1), the envelope included, so the finding is at the fallback, $.fallback."
          """{"fallback":{"id":"q","kind":{"$type":"Metric","label":"Orders","value":{"$type":"Query","dependsOn":["region"],"name":"orders"}}},"id":"root","kind":{"$type":"Box","children":[{"id":"c","kind":{"$type":"Markdown","text":"t"}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""
      v
          "validate-filter-edge-in-a-state-alternative"
          "A Metric held by the root's state.onEmpty alternative depends on the filter region, which no Filters chip declares (FUARAN075): the finding is at the alternative, $.state.onEmpty."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"c","kind":{"$type":"Markdown","text":"t"}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"},"state":{"onEmpty":{"id":"q","kind":{"$type":"Metric","label":"Orders","value":{"$type":"Query","dependsOn":["region"],"name":"orders"}}}}}"""
      v
          "validate-filter-edge-in-a-nested-state-alternative"
          "A Metric held by the state.onLoading alternative of a child's own state.onEmpty alternative depends on an undeclared filter (FUARAN075): the finding is at $.kind.children[0].state.onEmpty.state.onLoading."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"c0","kind":{"$type":"Markdown","text":"t"},"state":{"onEmpty":{"id":"e","kind":{"$type":"Markdown","text":"t"},"state":{"onLoading":{"id":"q","kind":{"$type":"Metric","label":"Orders","value":{"$type":"Query","dependsOn":["region"],"name":"orders"}}}}}}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""
      v
          "validate-filter-chip-in-a-state-alternative-grounds-an-edge"
          "The Filters chip region is declared by a node in the root's state.onEmpty alternative, and a Metric in the root's kind depends on it. The declaration is part of the document, so the edge is grounded and there is no FUARAN075."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"q","kind":{"$type":"Metric","label":"Orders","value":{"$type":"Query","dependsOn":["region"],"name":"orders"}}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"},"state":{"onEmpty":{"id":"chips","kind":{"$type":"Filters","items":[{"kind":{"$type":"Choice","options":{"$type":"Static","value":[{"label":"EMEA","value":"emea"}]}},"label":"Region","name":"region"}]}}}}"""
      v
          "validate-form-field-finding-is-located-at-the-form"
          "A Checkbox field carries a format rule (FUARAN100). A form field carries a string id and an object kind, but it sits at no position the vocabulary declares to hold a node, so it is not a node: the finding is located at the Form holding it, $.kind.children[1]."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"c","kind":{"$type":"Markdown","text":"t"}},{"id":"signup","kind":{"$type":"Form","fields":[{"id":"f","kind":{"$type":"Checkbox","value":{"$type":"State","key":"f"}},"label":"F","rule":{"format":"email"}}],"onSubmit":{"$type":"Chain","ops":[]},"submitLabel":"Go"}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""
      v
          "validate-inline-table-repeated-three-times"
          "Three DataGrids carry the same inline table (FUARAN107): x, x, x is two findings, at the second and the third copy ($.kind.children[1], $.kind.children[2]), each naming the first copy; never one per pair."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"g1","kind":{"$type":"DataGrid","columns":[{"field":"a","kind":{"$type":"Text"},"label":"a"}],"rowKeyField":"a","source":{"$type":"Static","value":[{"a":1},{"a":2}]}}},{"id":"g2","kind":{"$type":"DataGrid","columns":[{"field":"a","kind":{"$type":"Text"},"label":"a"}],"rowKeyField":"a","source":{"$type":"Static","value":[{"a":1},{"a":2}]}}},{"id":"g3","kind":{"$type":"DataGrid","columns":[{"field":"a","kind":{"$type":"Text"},"label":"a"}],"rowKeyField":"a","source":{"$type":"Static","value":[{"a":1},{"a":2}]}}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""
      v
          "validate-inline-tables-each-repeated"
          "Two inline tables are each carried twice (FUARAN107): x, x, y, y is two findings, one at the second copy of each ($.kind.children[1], $.kind.children[3])."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"g1","kind":{"$type":"DataGrid","columns":[{"field":"a","kind":{"$type":"Text"},"label":"a"}],"rowKeyField":"a","source":{"$type":"Static","value":[{"a":1},{"a":2}]}}},{"id":"g2","kind":{"$type":"DataGrid","columns":[{"field":"a","kind":{"$type":"Text"},"label":"a"}],"rowKeyField":"a","source":{"$type":"Static","value":[{"a":1},{"a":2}]}}},{"id":"g3","kind":{"$type":"DataGrid","columns":[{"field":"a","kind":{"$type":"Text"},"label":"a"}],"rowKeyField":"a","source":{"$type":"Static","value":[{"a":3},{"a":4}]}}},{"id":"g4","kind":{"$type":"DataGrid","columns":[{"field":"a","kind":{"$type":"Text"},"label":"a"}],"rowKeyField":"a","source":{"$type":"Static","value":[{"a":3},{"a":4}]}}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""
      v
          "validate-inline-table-repeated-on-a-repeated-id"
          "The second copy of a table is the second of two DataGrids sharing the id g2 (FUARAN107): the finding is at that copy, $.kind.children[2], not at the first node holding g2."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"g1","kind":{"$type":"DataGrid","columns":[{"field":"a","kind":{"$type":"Text"},"label":"a"}],"rowKeyField":"a","source":{"$type":"Static","value":[{"a":1},{"a":2}]}}},{"id":"g2","kind":{"$type":"DataGrid","columns":[{"field":"a","kind":{"$type":"Text"},"label":"a"}],"rowKeyField":"a","source":{"$type":"Static","value":[{"a":3},{"a":4}]}}},{"id":"g2","kind":{"$type":"DataGrid","columns":[{"field":"a","kind":{"$type":"Text"},"label":"a"}],"rowKeyField":"a","source":{"$type":"Static","value":[{"a":1},{"a":2}]}}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""
      v
          "validate-cross-tree-finding-on-the-second-of-two-nodes-sharing-an-id"
          "Two Switches share one id; the first selects on a key a form field writes, the second on a key nothing writes (FUARAN103). A finding judged across the whole tree is raised by the node whose binding it names, so it is located there, $.kind.children[2], though another node holds the same id."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"f","kind":{"$type":"Form","fields":[{"id":"k1","kind":{"$type":"Text","value":{"$type":"State","key":"k1"}},"label":"k1"}],"onSubmit":{"$type":"Chain","ops":[]},"submitLabel":"Send"}},{"id":"sw","kind":{"$type":"Switch","cases":[{"child":{"id":"a1","kind":{"$type":"Markdown","text":"t"}},"match":"a"}],"default":{"id":"d1","kind":{"$type":"Markdown","text":"t"}},"stateKey":"k1"}},{"id":"sw","kind":{"$type":"Switch","cases":[{"child":{"id":"a2","kind":{"$type":"Markdown","text":"t"}},"match":"a"}],"default":{"id":"d2","kind":{"$type":"Markdown","text":"t"}},"stateKey":"k2"}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}"""
      v
          "validate-switch-empty-statekey-in-a-list-of-twelve"
          "Two Switches with an empty stateKey (FUARAN083) at children[2] and children[10] of twelve. The expected findings are listed in document order, so children[2] comes before children[10]: positions compare as section 29.3 orders paths, never as strings."
          """{"id":"root","kind":{"$type":"Box","children":[{"id":"m0","kind":{"$type":"Markdown","text":"0"}},{"id":"m1","kind":{"$type":"Markdown","text":"1"}},{"id":"sw2","kind":{"$type":"Switch","cases":[{"child":{"id":"a2","kind":{"$type":"Markdown","text":"t"}},"match":"a"}],"default":{"id":"d2","kind":{"$type":"Markdown","text":"t"}},"stateKey":""}},{"id":"m3","kind":{"$type":"Markdown","text":"3"}},{"id":"m4","kind":{"$type":"Markdown","text":"4"}},{"id":"m5","kind":{"$type":"Markdown","text":"5"}},{"id":"m6","kind":{"$type":"Markdown","text":"6"}},{"id":"m7","kind":{"$type":"Markdown","text":"7"}},{"id":"m8","kind":{"$type":"Markdown","text":"8"}},{"id":"m9","kind":{"$type":"Markdown","text":"9"}},{"id":"sw10","kind":{"$type":"Switch","cases":[{"child":{"id":"a10","kind":{"$type":"Markdown","text":"t"}},"match":"a"}],"default":{"id":"d10","kind":{"$type":"Markdown","text":"t"}},"stateKey":""}},{"id":"m11","kind":{"$type":"Markdown","text":"11"}}],"layout":{"$type":"Flex","direction":"Vertical"},"role":"Group"}}""" ]

/// Every node object in a canonical document, in document order, with its
/// JSONPath. A node is the root or an object at a position that holds one
/// (section 8.1, the IDL's node fields): the envelope `fallback` and the state
/// alternatives, and in the kind `children[i]`, `child`, `fallback`, `body`,
/// `default`, `cases[i].child` and a slot argument's or input's `tree`. An
/// object elsewhere that merely carries an `id` and a `kind` (a form field) is
/// not a node. Read from the bytes, not from the reference's walk, so it is an
/// independent oracle for the paths the walk reports.
let nodePaths (canonical: string) : (string * string) list =
    use doc = JsonDocument.Parse canonical
    let found = ResizeArray<string * string>()

    let member' (e: JsonElement) (name: string) : JsonElement option =
        match e.ValueKind, e.TryGetProperty name with
        | JsonValueKind.Object, (true, v) when v.ValueKind = JsonValueKind.Object -> Some v
        | _ -> None

    let rec node (path: string) (e: JsonElement) =
        match e.TryGetProperty "id" with
        | true, id when id.ValueKind = JsonValueKind.String ->
            found.Add((id.GetString() |> Option.ofObj |> Option.defaultValue "", path))
        | _ -> ()

        // Members in canonical (Ordinal) order, so the walk is document order.
        let held =
            [ for p in e.EnumerateObject() do
                  match p.Name with
                  | "fallback" -> yield path + ".fallback", p.Value
                  | "kind" ->
                      for k in p.Value.EnumerateObject() do
                          let at = path + ".kind." + k.Name

                          match k.Name, k.Value.ValueKind with
                          | "children", JsonValueKind.Array ->
                              yield! k.Value.EnumerateArray() |> Seq.mapi (fun i c -> sprintf "%s[%d]" at i, c)
                          | "cases", JsonValueKind.Array ->
                              for i, c in k.Value.EnumerateArray() |> Seq.indexed do
                                  match member' c "child" with
                                  | Some child -> yield sprintf "%s[%d].child" at i, child
                                  | None -> ()
                          | ("child" | "fallback" | "body" | "default"), JsonValueKind.Object -> yield at, k.Value
                          | ("args" | "inputs"), JsonValueKind.Object ->
                              for a in k.Value.EnumerateObject() do
                                  match member' a.Value "tree" with
                                  | Some tree -> yield at + "." + a.Name + ".tree", tree
                                  | None -> ()
                          | _ -> ()
                  | "state" ->
                      for alt in [ "onEmpty"; "onLoading" ] do
                          match member' p.Value alt with
                          | Some held -> yield path + ".state." + alt, held
                          | None -> ()
                  | _ -> () ]

        for at, held in held do
            if held.ValueKind = JsonValueKind.Object then
                node at held

    node "$" doc.RootElement
    List.ofSeq found

/// The tree as the reference decodes it, and its canonical bytes. The vector
/// must already BE those bytes: a host decodes exactly what the file carries,
/// so a path into anything else would point at nothing.
let private decoded (vector: Vector) =
    match JsonDecode.decodeNodeObj vector.Tree with
    | Error e -> failwithf "validate vector '%s': the tree does not decode (%s at %s)" vector.Id e.Code e.Path
    | Ok tree ->
        let canonical = CanonicalJson.encodeNode tree

        if canonical <> vector.Tree then
            failwithf
                "validate vector '%s' is not in canonical form:\n  declared  -> %s\n  canonical -> %s"
                vector.Id
                vector.Tree
                canonical

        tree

/// The code of the finding that is about a repeated id itself.
[<Literal>]
let duplicateIdCode = "FUARAN-DUP-ID"

/// A path's place in document order (27.8.2): its segments in turn, a member
/// by its name (F# compares strings Ordinally, the canonical member order) and
/// an array index by its number, so `children[2]` precedes `children[10]` and a
/// node precedes what it holds. An absent path sorts first.
let private documentOrder (path: string option) : (int * string * int) list option =
    path
    |> Option.map (fun p ->
        [ for m in System.Text.RegularExpressions.Regex.Matches(p, @"\.([^.\[\]]+)|\[(\d+)\]") ->
              if m.Groups[2].Success then
                  1, "", int m.Groups[2].Value
              else
                  0, m.Groups[1].Value, 0 ])

/// The defects the reference validator reports on the vector, each located by
/// the validator's own walk, in document order of their paths (an absent path
/// first), then by code, then node, so the file is deterministic. Two findings
/// equal as data are two findings: each is a thing to change.
let defects (vector: Vector) : Defect list =
    decoded vector
    |> PreEmitValidate.validateWithPaths
    |> List.map (fun (d, path) ->
        let finding = PreEmitValidate.findingOf d

        { Code = finding.Code
          Severity = sprintf "%A" finding.Severity
          Node = finding.Location
          Path = path })
    |> List.sortBy (fun d -> documentOrder d.Path, d.Code, d.Node)

let private description =
    "The Fuaran pre-emit VALIDATE conformance family (WIRE_FORMAT.md 27.8.2). Each vector is a "
    + "document (`tree`, canonical wire bytes) and the pre-emit defects it carries (`expected`): each "
    + "defect's `code` and `severity` from validator/defect-vocabulary.json, the `node` id it is "
    + "raised on (null where the defect names none), and `path`, the JSONPath of that node's object "
    + "in the tree in the section 29.3 grammar: the occurrence that raised it, a finding judged across "
    + "the whole tree included (null only where no node of the document raised it). A finding whose "
    + "subject is a repetition is reported once at each occurrence after the first, located at that "
    + "occurrence. The expected defects are listed in document order. A host that implements the "
    + "pre-emit tier validates each tree and asserts that its findings, restricted to the codes its "
    + "validator-coverage.json declares implemented, are exactly the expected defects restricted to "
    + "the same codes, each on the stated node; a host whose finding carries a path asserts that "
    + "the path is the stated one or extends it at a segment boundary. Generated from the reference "
    + "host's own validator, through its decoder."

/// Render the artefact.
let toJson () : string =
    let opts =
        JsonWriterOptions(Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping)

    use stream = new MemoryStream()
    use w = new Utf8JsonWriter(stream, opts)

    let optional (name: string) (value: string option) =
        match value with
        | Some s -> w.WriteString(name, s)
        | None -> w.WriteNull name

    w.WriteStartObject()
    w.WriteNumber("version", 1)
    w.WriteString("$id", artifactId)
    w.WriteString("description", description)
    w.WriteStartArray("vectors")

    for vector in all do
        w.WriteStartObject()
        w.WriteString("id", vector.Id)
        w.WriteString("description", vector.Description)
        w.WriteString("tree", vector.Tree)
        w.WriteStartArray("expected")

        for d in defects vector do
            w.WriteStartObject()
            w.WriteString("code", d.Code)
            w.WriteString("severity", d.Severity)
            optional "node" d.Node
            optional "path" d.Path
            w.WriteEndObject()

        w.WriteEndArray()
        w.WriteEndObject()

    w.WriteEndArray()
    w.WriteEndObject()
    w.Flush()

    System.Text.Encoding.UTF8.GetString(stream.ToArray()) + "\n"

/// Write the family into a corpus directory.
let write (corpusRoot: string) : string =
    let dir = Path.Combine(corpusRoot, directory)
    Directory.CreateDirectory dir |> ignore
    let path = Path.Combine(dir, fileName)
    File.WriteAllText(path, toJson ())
    path
