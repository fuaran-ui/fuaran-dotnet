module Fuaran.UI.JsonDecode.Tests.ApplyArtifact

// ============================================================================
//  The host-tree APPLY families (Phase 2381) - `apply/duplicate-ids-apply.json`,
//  `apply/limits-apply.json`, `apply/style-normalise-apply.json` and
//  `apply/update-prop-apply.json`, four entries of the self-enumerated `apply/`
//  manifest (WIRE_FORMAT 27.7) whose normative sentences are 8.1, 21 and 3.4.
//
//  Each vector is (tree, op) and the verdict a conformant apply engine reaches.
//  The inputs and their prose are declared here; the EXPECTATION is never
//  declared. It is what the reference apply path (`Apply.apply`) does with the
//  input, decoded by the reference decoder: the refusal's code, or acceptance
//  with the applied tree's canonical bytes where the family pins the result.
//  So the files carry the reference's verdicts by construction, and a change to
//  the apply path changes them on the next emit rather than leaving a stale
//  claim behind (27.8: a family no program writes drifts from what it pins).
//
//  The `limits` member states the bounds a family's vectors sit at, read from
//  `Fuaran.UI.WireLimits`, never restated.
//
//  `skeletonApply` is NOT written here: Fuaran-Core produces it. The `apply/`
//  manifest is not written either (it carries that row and each host's
//  adoption data); `EmittedFamilyTests` holds its counts to these files.
//
//  Co-emitted by `Corpus.emit`, and writable alone with `--emit-apply [<dir>]`.
//  `EmittedFamilyTests` asserts byte-equality with the committed files.
// ============================================================================

open System.IO
open System.Text.Encodings.Web
open System.Text.Json
open Microsoft.FSharp.Reflection

open Fuaran.UI
open Fuaran.UI.Ops
open Fuaran.UI.Ops.Types
open Fuaran.UI.OpStream.Abstractions

/// The family's directory at the corpus root.
[<Literal>]
let directory = "apply"

/// One vector: what is applied to what. The verdict is the reference's.
type Vector =
    { Id: string
      Case: string
      Description: string
      Tree: string
      Op: string }

/// One family file. `PinsResult` says whether an accepting vector carries the
/// applied tree's canonical bytes; `Limits` names the `WireLimits` bounds its
/// vectors sit at.
type Family =
    { Id: string
      File: string
      Description: string
      Limits: string list
      PinsResult: bool
      Vectors: Vector list }

/// The reference's value of a bound a family names.
let private limit (name: string) : int =
    match name with
    | "maxDepth" -> WireLimits.MaxDepth
    | "maxNodes" -> WireLimits.MaxNodes
    | "maxSkeletonRows" -> WireLimits.MaxSkeletonRows
    | other -> failwithf "apply family names an unknown limit '%s'" other

let all: Family list =
    [ { Id = "duplicateIdsApply"
        File = "duplicate-ids-apply.json"
        Description =
          """Apply-time node-id uniqueness (WIRE_FORMAT section 8.1): an op that puts nodes into a tree is checked on its RESULT, and one that leaves an id it installed held by more than one node is refused with the shared code DuplicateNodeId. The decoder judges shape and does not refuse a repeated id; these vectors pin that no op can BUILD such a tree. Each vector carries `input.tree` (a wire node) and `input.op` (a wire op) as canonical JSON STRINGS, decoded by the host's own wire decoder. `expected.verdict` is `reject` or `accept`; a `reject` vector requires apply to refuse with `expected.code`, an `accept` vector requires it to apply (no result bytes are pinned). The check charges an op only for ids IT installed: a duplicate already present elsewhere in the tree before the op is not refused here (`preexisting-duplicate-not-blamed`), the same posture as the limits guard toward a tree already over a limit. Where an op breaches a tree limit as well, LimitExceeded takes precedence (limitsApply's `editnode-repeated-id-past-maxdepth`). A Batch is checked once, on the tree it produces."""
        Limits = []
        PinsResult = false
        Vectors =
          [ { Id = "replaceroot-repeated-id"
              Case = "ReplaceRoot"
              Description =
                """ReplaceRoot installs a tree whose two children share one id: the result would hold the id twice."""
              Tree =
                """{"id":"r","kind":{"$type":"Box","children":[{"id":"a","kind":{"$type":"Box","children":[],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}},{"id":"b","kind":{"$type":"Markdown","text":"b"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}"""
              Op =
                """{"$type":"ReplaceRoot","node":{"id":"r2","kind":{"$type":"Box","children":[{"id":"x","kind":{"$type":"Markdown","text":"x"}},{"id":"x","kind":{"$type":"Markdown","text":"x"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}}""" }
            { Id = "replaceroot-root-id-repeated-below"
              Case = "ReplaceRoot"
              Description = """ReplaceRoot installs a tree whose root id reappears on a descendant."""
              Tree =
                """{"id":"r","kind":{"$type":"Box","children":[{"id":"a","kind":{"$type":"Box","children":[],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}},{"id":"b","kind":{"$type":"Markdown","text":"b"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}"""
              Op =
                """{"$type":"ReplaceRoot","node":{"id":"r","kind":{"$type":"Box","children":[{"id":"r","kind":{"$type":"Markdown","text":"r"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}}""" }
            { Id = "replaceroot-reuses-old-ids"
              Case = "ReplaceRoot"
              Description =
                """ReplaceRoot installs a tree reusing ids of the tree it replaces, each once: the old tree is gone, so nothing collides and the op applies."""
              Tree =
                """{"id":"r","kind":{"$type":"Box","children":[{"id":"a","kind":{"$type":"Box","children":[],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}},{"id":"b","kind":{"$type":"Markdown","text":"b"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}"""
              Op =
                """{"$type":"ReplaceRoot","node":{"id":"r","kind":{"$type":"Box","children":[{"id":"a","kind":{"$type":"Markdown","text":"a"}},{"id":"b","kind":{"$type":"Markdown","text":"b again"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}}""" }
            { Id = "editnode-collides-with-tree"
              Case = "EditNode"
              Description =
                """EditNode gives node a a kind holding a child whose id b is already held by a pre-existing node elsewhere in the tree."""
              Tree =
                """{"id":"r","kind":{"$type":"Box","children":[{"id":"a","kind":{"$type":"Box","children":[],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}},{"id":"b","kind":{"$type":"Markdown","text":"b"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}"""
              Op =
                """{"$type":"EditNode","newKind":{"$type":"Box","children":[{"id":"b","kind":{"$type":"Markdown","text":"b"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"},"target":"a"}""" }
            { Id = "editnode-repeated-within-kind"
              Case = "EditNode"
              Description = """EditNode gives node a a kind whose two children share one id."""
              Tree =
                """{"id":"r","kind":{"$type":"Box","children":[{"id":"a","kind":{"$type":"Box","children":[],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}},{"id":"b","kind":{"$type":"Markdown","text":"b"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}"""
              Op =
                """{"$type":"EditNode","newKind":{"$type":"Box","children":[{"id":"x","kind":{"$type":"Markdown","text":"x"}},{"id":"x","kind":{"$type":"Markdown","text":"x"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"},"target":"a"}""" }
            { Id = "editnode-repeats-own-id"
              Case = "EditNode"
              Description =
                """EditNode gives node a a kind holding a child whose id repeats the edited node's own id."""
              Tree =
                """{"id":"r","kind":{"$type":"Box","children":[{"id":"a","kind":{"$type":"Box","children":[],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}},{"id":"b","kind":{"$type":"Markdown","text":"b"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}"""
              Op =
                """{"$type":"EditNode","newKind":{"$type":"Box","children":[{"id":"a","kind":{"$type":"Markdown","text":"a"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"},"target":"a"}""" }
            { Id = "editnode-restates-children"
              Case = "EditNode"
              Description =
                """EditNode replaces a kind whose child is c with one holding c and d: the old child leaves as the restated one arrives, so c is held once and the op applies."""
              Tree =
                """{"id":"r","kind":{"$type":"Box","children":[{"id":"a","kind":{"$type":"Box","children":[{"id":"c","kind":{"$type":"Markdown","text":"c"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}"""
              Op =
                """{"$type":"EditNode","newKind":{"$type":"Box","children":[{"id":"c","kind":{"$type":"Markdown","text":"c again"}},{"id":"d","kind":{"$type":"Markdown","text":"d"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"},"target":"a"}""" }
            { Id = "updatestate-collides-with-tree"
              Case = "UpdateState"
              Description =
                """UpdateState attaches an onLoading alternative whose id b is already held by a pre-existing node."""
              Tree =
                """{"id":"r","kind":{"$type":"Box","children":[{"id":"a","kind":{"$type":"Box","children":[],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}},{"id":"b","kind":{"$type":"Markdown","text":"b"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}"""
              Op =
                """{"$type":"UpdateState","state":{"onLoading":{"id":"b","kind":{"$type":"Markdown","text":"b"}}},"target":"a"}""" }
            { Id = "updatestate-replaces-own-alternative"
              Case = "UpdateState"
              Description =
                """UpdateState replaces a node's onLoading alternative with one of the same id: the old alternative leaves as the new one arrives, so the op applies."""
              Tree =
                """{"id":"r","kind":{"$type":"Box","children":[{"id":"a","kind":{"$type":"Markdown","text":"a"},"state":{"onLoading":{"id":"l","kind":{"$type":"Markdown","text":"l"}}}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}"""
              Op =
                """{"$type":"UpdateState","state":{"onLoading":{"id":"l","kind":{"$type":"Markdown","text":"loading again"}}},"target":"a"}""" }
            { Id = "insertchild-repeated-within-child"
              Case = "InsertChild"
              Description =
                """InsertChild appends a subtree whose two children share one id, neither held by the tree."""
              Tree =
                """{"id":"r","kind":{"$type":"Box","children":[{"id":"a","kind":{"$type":"Box","children":[],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}},{"id":"b","kind":{"$type":"Markdown","text":"b"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}"""
              Op =
                """{"$type":"InsertChild","child":{"id":"c","kind":{"$type":"Box","children":[{"id":"x","kind":{"$type":"Markdown","text":"x"}},{"id":"x","kind":{"$type":"Markdown","text":"x"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}},"parentId":"r"}""" }
            { Id = "insertchild-collides-with-tree"
              Case = "InsertChild"
              Description = """InsertChild appends a child whose id b is already held by a pre-existing node."""
              Tree =
                """{"id":"r","kind":{"$type":"Box","children":[{"id":"a","kind":{"$type":"Box","children":[],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}},{"id":"b","kind":{"$type":"Markdown","text":"b"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}"""
              Op =
                """{"$type":"InsertChild","child":{"id":"b","kind":{"$type":"Markdown","text":"b"}},"parentId":"r"}""" }
            { Id = "preexisting-duplicate-not-blamed"
              Case = "EditNode"
              Description =
                """The tree already holds x twice. EditNode on z installs only a fresh id w: the check refuses an id the op installed that the result holds twice, so a duplicate the op did not put there is not charged to it, and the op applies."""
              Tree =
                """{"id":"r","kind":{"$type":"Box","children":[{"id":"x","kind":{"$type":"Markdown","text":"x"}},{"id":"y","kind":{"$type":"Box","children":[{"id":"x","kind":{"$type":"Markdown","text":"x"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}},{"id":"z","kind":{"$type":"Box","children":[],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}"""
              Op =
                """{"$type":"EditNode","newKind":{"$type":"Box","children":[{"id":"w","kind":{"$type":"Markdown","text":"w"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"},"target":"z"}""" }
            { Id = "batch-result-duplicates"
              Case = "Batch"
              Description =
                """A Batch whose EditNode installs b beside the pre-existing b and leaves it there: the batch's result holds b twice and the whole batch is refused."""
              Tree =
                """{"id":"r","kind":{"$type":"Box","children":[{"id":"a","kind":{"$type":"Box","children":[],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}},{"id":"s","kind":{"$type":"Box","children":[{"id":"b","kind":{"$type":"Markdown","text":"b"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}"""
              Op =
                """{"$type":"Batch","ops":[{"$type":"EditNode","newKind":{"$type":"Box","children":[{"id":"b","kind":{"$type":"Markdown","text":"b"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"},"target":"a"}]}""" }
            { Id = "batch-intermediate-duplicate-resolved"
              Case = "Batch"
              Description =
                """A Batch whose first EditNode installs b beside the pre-existing b, and whose second EditNode rewrites s without its b: the intermediate state holds b twice but the result holds it once, and the batch applies. The check reads the op's RESULT, as the limits guard does."""
              Tree =
                """{"id":"r","kind":{"$type":"Box","children":[{"id":"a","kind":{"$type":"Box","children":[],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}},{"id":"s","kind":{"$type":"Box","children":[{"id":"b","kind":{"$type":"Markdown","text":"b"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}"""
              Op =
                """{"$type":"Batch","ops":[{"$type":"EditNode","newKind":{"$type":"Box","children":[{"id":"b","kind":{"$type":"Markdown","text":"b"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"},"target":"a"},{"$type":"EditNode","newKind":{"$type":"Box","children":[],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"},"target":"s"}]}""" } ] }
      { Id = "limitsApply"
        File = "limits-apply.json"
        Description =
          """Apply-time WireLimits (WIRE_FORMAT section 21): every op that can grow a tree is checked on its RESULT, and one that takes the tree past MaxDepth (24) or MaxNodes (100000) is refused with the shared code LimitExceeded. The decoder bounds what arrives; these vectors pin that an op assembled from legal parts cannot build what no host could decode. Each vector carries `input.tree` (a wire node) and `input.op` (a wire op) as canonical JSON STRINGS, decoded by the host's own wire decoder. `expected.verdict` is `reject` or `accept`. A `reject` vector requires apply to refuse with `expected.code`; an `accept` vector requires it to apply (no result bytes are pinned: the family is about the limit, and every accepted shape here is already pinned by the node and op corpora). The ops covered are the three the earlier guard missed - EditNode and UpdateState, which put subtrees in, and MoveNode, which can stack two legal depths - plus the repeated-id shape that defeats an id-set derivation. InsertChild and ReplaceRoot are pinned by each host's own suite. MaxNodes is NOT pinned here, deliberately: a vector past it is a 100001-node document, several megabytes of corpus to restate one comparison each host already unit-tests on the same ops."""
        Limits = [ "maxDepth"; "maxNodes" ]
        PinsResult = false
        Vectors =
          [ { Id = "editnode-past-maxdepth"
              Case = "EditNode"
              Description =
                """EditNode gives the deepest node of a MaxDepth-deep chain a kind holding one child: the result nests MaxDepth + 1 levels."""
              Tree =
                """{"id":"n24","kind":{"$type":"Box","children":[{"id":"n23","kind":{"$type":"Box","children":[{"id":"n22","kind":{"$type":"Box","children":[{"id":"n21","kind":{"$type":"Box","children":[{"id":"n20","kind":{"$type":"Box","children":[{"id":"n19","kind":{"$type":"Box","children":[{"id":"n18","kind":{"$type":"Box","children":[{"id":"n17","kind":{"$type":"Box","children":[{"id":"n16","kind":{"$type":"Box","children":[{"id":"n15","kind":{"$type":"Box","children":[{"id":"n14","kind":{"$type":"Box","children":[{"id":"n13","kind":{"$type":"Box","children":[{"id":"n12","kind":{"$type":"Box","children":[{"id":"n11","kind":{"$type":"Box","children":[{"id":"n10","kind":{"$type":"Box","children":[{"id":"n9","kind":{"$type":"Box","children":[{"id":"n8","kind":{"$type":"Box","children":[{"id":"n7","kind":{"$type":"Box","children":[{"id":"n6","kind":{"$type":"Box","children":[{"id":"n5","kind":{"$type":"Box","children":[{"id":"n4","kind":{"$type":"Box","children":[{"id":"n3","kind":{"$type":"Box","children":[{"id":"n2","kind":{"$type":"Box","children":[{"id":"n1","kind":{"$type":"Box","children":[],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}"""
              Op =
                """{"$type":"EditNode","newKind":{"$type":"Box","children":[{"id":"over","kind":{"$type":"Markdown","text":"over"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"},"target":"n1"}""" }
            { Id = "editnode-at-maxdepth"
              Case = "EditNode"
              Description =
                """The same edit one level shallower lands exactly at MaxDepth and applies: the guard refuses past the limit, never at it."""
              Tree =
                """{"id":"n23","kind":{"$type":"Box","children":[{"id":"n22","kind":{"$type":"Box","children":[{"id":"n21","kind":{"$type":"Box","children":[{"id":"n20","kind":{"$type":"Box","children":[{"id":"n19","kind":{"$type":"Box","children":[{"id":"n18","kind":{"$type":"Box","children":[{"id":"n17","kind":{"$type":"Box","children":[{"id":"n16","kind":{"$type":"Box","children":[{"id":"n15","kind":{"$type":"Box","children":[{"id":"n14","kind":{"$type":"Box","children":[{"id":"n13","kind":{"$type":"Box","children":[{"id":"n12","kind":{"$type":"Box","children":[{"id":"n11","kind":{"$type":"Box","children":[{"id":"n10","kind":{"$type":"Box","children":[{"id":"n9","kind":{"$type":"Box","children":[{"id":"n8","kind":{"$type":"Box","children":[{"id":"n7","kind":{"$type":"Box","children":[{"id":"n6","kind":{"$type":"Box","children":[{"id":"n5","kind":{"$type":"Box","children":[{"id":"n4","kind":{"$type":"Box","children":[{"id":"n3","kind":{"$type":"Box","children":[{"id":"n2","kind":{"$type":"Box","children":[{"id":"n1","kind":{"$type":"Box","children":[],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}"""
              Op =
                """{"$type":"EditNode","newKind":{"$type":"Box","children":[{"id":"at-the-limit","kind":{"$type":"Markdown","text":"at-the-limit"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"},"target":"n1"}""" }
            { Id = "editnode-repeated-id-past-maxdepth"
              Case = "EditNode"
              Description =
                """EditNode installs a child whose id REPEATS the edited node's id. The id set does not grow but the tree does, one level past MaxDepth: the guard reads the nodes an op carries, not their ids."""
              Tree =
                """{"id":"n24","kind":{"$type":"Box","children":[{"id":"n23","kind":{"$type":"Box","children":[{"id":"n22","kind":{"$type":"Box","children":[{"id":"n21","kind":{"$type":"Box","children":[{"id":"n20","kind":{"$type":"Box","children":[{"id":"n19","kind":{"$type":"Box","children":[{"id":"n18","kind":{"$type":"Box","children":[{"id":"n17","kind":{"$type":"Box","children":[{"id":"n16","kind":{"$type":"Box","children":[{"id":"n15","kind":{"$type":"Box","children":[{"id":"n14","kind":{"$type":"Box","children":[{"id":"n13","kind":{"$type":"Box","children":[{"id":"n12","kind":{"$type":"Box","children":[{"id":"n11","kind":{"$type":"Box","children":[{"id":"n10","kind":{"$type":"Box","children":[{"id":"n9","kind":{"$type":"Box","children":[{"id":"n8","kind":{"$type":"Box","children":[{"id":"n7","kind":{"$type":"Box","children":[{"id":"n6","kind":{"$type":"Box","children":[{"id":"n5","kind":{"$type":"Box","children":[{"id":"n4","kind":{"$type":"Box","children":[{"id":"n3","kind":{"$type":"Box","children":[{"id":"n2","kind":{"$type":"Box","children":[{"id":"n1","kind":{"$type":"Box","children":[],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}"""
              Op =
                """{"$type":"EditNode","newKind":{"$type":"Box","children":[{"id":"n1","kind":{"$type":"Markdown","text":"n1"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"},"target":"n1"}""" }
            { Id = "updatestate-past-maxdepth"
              Case = "UpdateState"
              Description =
                """UpdateState attaches an onLoading alternative to the deepest node of a MaxDepth-deep chain: a state alternative is a level of the tree as the decoder counts it, so the result nests MaxDepth + 1 levels."""
              Tree =
                """{"id":"n24","kind":{"$type":"Box","children":[{"id":"n23","kind":{"$type":"Box","children":[{"id":"n22","kind":{"$type":"Box","children":[{"id":"n21","kind":{"$type":"Box","children":[{"id":"n20","kind":{"$type":"Box","children":[{"id":"n19","kind":{"$type":"Box","children":[{"id":"n18","kind":{"$type":"Box","children":[{"id":"n17","kind":{"$type":"Box","children":[{"id":"n16","kind":{"$type":"Box","children":[{"id":"n15","kind":{"$type":"Box","children":[{"id":"n14","kind":{"$type":"Box","children":[{"id":"n13","kind":{"$type":"Box","children":[{"id":"n12","kind":{"$type":"Box","children":[{"id":"n11","kind":{"$type":"Box","children":[{"id":"n10","kind":{"$type":"Box","children":[{"id":"n9","kind":{"$type":"Box","children":[{"id":"n8","kind":{"$type":"Box","children":[{"id":"n7","kind":{"$type":"Box","children":[{"id":"n6","kind":{"$type":"Box","children":[{"id":"n5","kind":{"$type":"Box","children":[{"id":"n4","kind":{"$type":"Box","children":[{"id":"n3","kind":{"$type":"Box","children":[{"id":"n2","kind":{"$type":"Box","children":[{"id":"n1","kind":{"$type":"Box","children":[],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}"""
              Op =
                """{"$type":"UpdateState","state":{"onLoading":{"id":"loading","kind":{"$type":"Markdown","text":"loading"}}},"target":"n1"}""" }
            { Id = "updatestate-at-maxdepth"
              Case = "UpdateState"
              Description = """The same state block one level shallower lands exactly at MaxDepth and applies."""
              Tree =
                """{"id":"n23","kind":{"$type":"Box","children":[{"id":"n22","kind":{"$type":"Box","children":[{"id":"n21","kind":{"$type":"Box","children":[{"id":"n20","kind":{"$type":"Box","children":[{"id":"n19","kind":{"$type":"Box","children":[{"id":"n18","kind":{"$type":"Box","children":[{"id":"n17","kind":{"$type":"Box","children":[{"id":"n16","kind":{"$type":"Box","children":[{"id":"n15","kind":{"$type":"Box","children":[{"id":"n14","kind":{"$type":"Box","children":[{"id":"n13","kind":{"$type":"Box","children":[{"id":"n12","kind":{"$type":"Box","children":[{"id":"n11","kind":{"$type":"Box","children":[{"id":"n10","kind":{"$type":"Box","children":[{"id":"n9","kind":{"$type":"Box","children":[{"id":"n8","kind":{"$type":"Box","children":[{"id":"n7","kind":{"$type":"Box","children":[{"id":"n6","kind":{"$type":"Box","children":[{"id":"n5","kind":{"$type":"Box","children":[{"id":"n4","kind":{"$type":"Box","children":[{"id":"n3","kind":{"$type":"Box","children":[{"id":"n2","kind":{"$type":"Box","children":[{"id":"n1","kind":{"$type":"Box","children":[],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}"""
              Op =
                """{"$type":"UpdateState","state":{"onLoading":{"id":"loading","kind":{"$type":"Markdown","text":"loading"}}},"target":"n1"}""" }
            { Id = "movenode-past-maxdepth"
              Case = "MoveNode"
              Description =
                """A root over two branches each MaxDepth / 2 deep is legal; MoveNode puts the top of one branch under the leaf of the other and stacks the two depths to MaxDepth + 1. MoveNode adds no node, but depth is a property of placement."""
              Tree =
                """{"id":"root","kind":{"$type":"Box","children":[{"id":"a12","kind":{"$type":"Box","children":[{"id":"a11","kind":{"$type":"Box","children":[{"id":"a10","kind":{"$type":"Box","children":[{"id":"a9","kind":{"$type":"Box","children":[{"id":"a8","kind":{"$type":"Box","children":[{"id":"a7","kind":{"$type":"Box","children":[{"id":"a6","kind":{"$type":"Box","children":[{"id":"a5","kind":{"$type":"Box","children":[{"id":"a4","kind":{"$type":"Box","children":[{"id":"a3","kind":{"$type":"Box","children":[{"id":"a2","kind":{"$type":"Box","children":[{"id":"a1","kind":{"$type":"Box","children":[],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}},{"id":"b12","kind":{"$type":"Box","children":[{"id":"b11","kind":{"$type":"Box","children":[{"id":"b10","kind":{"$type":"Box","children":[{"id":"b9","kind":{"$type":"Box","children":[{"id":"b8","kind":{"$type":"Box","children":[{"id":"b7","kind":{"$type":"Box","children":[{"id":"b6","kind":{"$type":"Box","children":[{"id":"b5","kind":{"$type":"Box","children":[{"id":"b4","kind":{"$type":"Box","children":[{"id":"b3","kind":{"$type":"Box","children":[{"id":"b2","kind":{"$type":"Box","children":[{"id":"b1","kind":{"$type":"Box","children":[],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}"""
              Op = """{"$type":"MoveNode","newParentId":"a1","target":"b12"}""" }
            { Id = "movenode-at-maxdepth"
              Case = "MoveNode"
              Description =
                """The same move with the moved branch one level shorter lands exactly at MaxDepth and applies."""
              Tree =
                """{"id":"root","kind":{"$type":"Box","children":[{"id":"a12","kind":{"$type":"Box","children":[{"id":"a11","kind":{"$type":"Box","children":[{"id":"a10","kind":{"$type":"Box","children":[{"id":"a9","kind":{"$type":"Box","children":[{"id":"a8","kind":{"$type":"Box","children":[{"id":"a7","kind":{"$type":"Box","children":[{"id":"a6","kind":{"$type":"Box","children":[{"id":"a5","kind":{"$type":"Box","children":[{"id":"a4","kind":{"$type":"Box","children":[{"id":"a3","kind":{"$type":"Box","children":[{"id":"a2","kind":{"$type":"Box","children":[{"id":"a1","kind":{"$type":"Box","children":[],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}},{"id":"b11","kind":{"$type":"Box","children":[{"id":"b10","kind":{"$type":"Box","children":[{"id":"b9","kind":{"$type":"Box","children":[{"id":"b8","kind":{"$type":"Box","children":[{"id":"b7","kind":{"$type":"Box","children":[{"id":"b6","kind":{"$type":"Box","children":[{"id":"b5","kind":{"$type":"Box","children":[{"id":"b4","kind":{"$type":"Box","children":[{"id":"b3","kind":{"$type":"Box","children":[{"id":"b2","kind":{"$type":"Box","children":[{"id":"b1","kind":{"$type":"Box","children":[],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}"""
              Op = """{"$type":"MoveNode","newParentId":"a1","target":"b11"}""" } ] }
      { Id = "styleNormaliseApply"
        File = "style-normalise-apply.json"
        Description =
          """The absent style slot and the all-default SemanticStyle record are ONE value (WIRE_FORMAT section 3.1: `style` is omitted entirely when every member is at its default; section 3.6: each member is omitted at its identity). An apply engine therefore stores a style result equal to the default record as the ABSENT slot, whichever op produced it - UpdateStyle, which writes the whole record, or PatchStyle (section 3.4). A host that stores an empty style object instead encodes the tree as `"style":{}`, so two hosts applying one op to one tree disagree on its canonical bytes, and any equality, merge or footprint computed over those bytes sees a difference where there is none. Each vector carries `input.tree` (a wire node) and `input.op` (a wire op) as canonical JSON STRINGS, decoded by the host's own wire decoder. Every vector is an `accept`: apply must succeed AND the host's canonical re-encoding of the applied tree must equal `expected.tree` byte for byte. Unlike the limitsApply and duplicateIdsApply families, this one pins RESULT bytes, because the bytes are the property under test."""
        Limits = []
        PinsResult = true
        Vectors =
          [ { Id = "updatestyle-default-clears-a-styled-node"
              Case = "UpdateStyle"
              Description =
                """A node carrying all six style members off their defaults is restyled to the all-default record: the result stores the ABSENT slot, so the node encodes with no `style` member at all - never `"style":{}`."""
              Tree =
                """{"id":"m","kind":{"$type":"Markdown","text":"m"},"style":{"direction":"rtl","emphasis":"Loud","role":"Eyebrow","tone":"Success","voice":"Display","weight":"Spacious"}}"""
              Op = """{"$type":"UpdateStyle","style":{},"target":"m"}""" }
            { Id = "updatestyle-default-on-an-unstyled-node"
              Case = "UpdateStyle"
              Description =
                """A node whose style is already absent is restyled to the all-default record: the tree is byte-identical to the input."""
              Tree = """{"id":"m","kind":{"$type":"Markdown","text":"m"}}"""
              Op = """{"$type":"UpdateStyle","style":{},"target":"m"}""" }
            { Id = "updatestyle-nondefault-replaces-the-record"
              Case = "UpdateStyle"
              Description =
                """The control: a styled node restyled to a non-default record stores that record as given. The op replaces the WHOLE record, so the three members the payload omits return to their defaults and are omitted (section 3.6), leaving only the two it names."""
              Tree =
                """{"id":"m","kind":{"$type":"Markdown","text":"m"},"style":{"emphasis":"Loud","tone":"Success","weight":"Spacious"}}"""
              Op = """{"$type":"UpdateStyle","style":{"role":"Eyebrow","voice":"Structural"},"target":"m"}""" }
            { Id = "patchstyle-back-to-default-clears"
              Case = "PatchStyle"
              Description =
                """A PatchStyle that sets every non-default member of a styled node back to its default token leaves the default record, which is stored as the absent slot exactly as UpdateStyle stores it (Phase 2291's rule)."""
              Tree =
                """{"id":"m","kind":{"$type":"Markdown","text":"m"},"style":{"emphasis":"Loud","tone":"Success","weight":"Spacious"}}"""
              Op =
                """{"$type":"PatchStyle","style":{"emphasis":"Normal","tone":"Default","weight":"Standard"},"target":"m"}""" }
            { Id = "updatestyle-default-clears-a-nested-node"
              Case = "UpdateStyle"
              Description =
                """The first vector one level down: a styled child of a layout parent is restyled to the all-default record and stores the absent slot, while its styled sibling is carried through unchanged."""
              Tree =
                """{"id":"r","kind":{"$type":"Box","children":[{"id":"a","kind":{"$type":"Markdown","text":"a"},"style":{"direction":"rtl","emphasis":"Loud","role":"Eyebrow","tone":"Success","voice":"Display","weight":"Spacious"}},{"id":"b","kind":{"$type":"Markdown","text":"b"},"style":{"emphasis":"Loud","tone":"Success","weight":"Spacious"}}],"layout":{"$type":"Flex","direction":"Vertical","wrap":false},"role":"Group"}}"""
              Op = """{"$type":"UpdateStyle","style":{},"target":"a"}""" } ] }
      { Id = "updatePropApply"
        File = "update-prop-apply.json"
        Description =
          """UpdateProp writes a field through the same rules the wire decoder reads it with (WIRE_FORMAT section 3.4): the slot rule for a typed integer (section 7.1), a field's resource bound (section 21.9), and omission at a declared default (section 3.6). The decoder bounds what arrives; these vectors pin that the write path into a tree cannot store what no decode could produce. Each vector carries `input.tree` (a wire node) and `input.op` (a wire op) as JSON STRINGS, decoded by the host's own wire decoder; the op strings are canonical except where a vector says otherwise. `expected.verdict` is `reject` or `accept`. A `reject` vector requires apply to refuse with `expected.code` (`LimitExceeded` for a bound, `KindMismatch` for a value the slot cannot hold); an `accept` vector requires apply to succeed AND the host's canonical re-encoding of the applied tree to equal `expected.tree` byte for byte."""
        Limits = [ "maxSkeletonRows" ]
        PinsResult = true
        Vectors =
          [ { Id = "updateprop-skeleton-rows-past-the-bound"
              Case = "UpdateProp"
              Description =
                """Rows set past MaxSkeletonRows (section 21.9) to the signed 32-bit maximum. The slot rule (section 7.1) admits the value, so the bound refuses it with the shared code a decode of the same tree carries. The value is the 32-bit maximum rather than 10001 for the reason section 21.9 gives for its own reject fixture: only it separates a host that applies the bound from one that reads the bound as a narrowing of the slot's type."""
              Tree = """{"id":"s","kind":{"$type":"Skeleton","rows":3}}"""
              Op = """{"$type":"UpdateProp","path":"Rows","target":"s","value":2147483647}""" }
            { Id = "updateprop-skeleton-rows-at-the-bound"
              Case = "UpdateProp"
              Description =
                """The control: Rows set exactly to MaxSkeletonRows applies, and the applied tree carries it."""
              Tree = """{"id":"s","kind":{"$type":"Skeleton","rows":3}}"""
              Op = """{"$type":"UpdateProp","path":"Rows","target":"s","value":10000}""" }
            { Id = "updateprop-skeleton-rows-outside-32-bit"
              Case = "UpdateProp"
              Description =
                """Rows set to 10^15. The slot rule decides FIRST (section 7.1): the value is outside the signed 32-bit range, so it is a type refusal and never a limit breach - the decoder's order, on the write path. A host that truncated or wrapped the number would store a different count, or loop 10^15 times at render."""
              Tree = """{"id":"s","kind":{"$type":"Skeleton","rows":3}}"""
              Op = """{"$type":"UpdateProp","path":"Rows","target":"s","value":1000000000000000}""" }
            { Id = "updateprop-heading-level-fractional"
              Case = "UpdateProp"
              Description =
                """Level set to 2.5. An integer slot admits no fractional value (section 7.1), so the op is refused; a host that truncated it would store level 2, discarding the author's value."""
              Tree = """{"id":"h","kind":{"$type":"Heading","level":2,"text":"Title"}}"""
              Op = """{"$type":"UpdateProp","path":"Level","target":"h","value":2.5}""" }
            { Id = "updateprop-heading-level-integral-number"
              Case = "UpdateProp"
              Description =
                """Level set to 3.0. The op text spells the number with a fractional part on purpose: its value is integral, which is all section 7.1 asks, so the op applies exactly as a decode of the same number would, and the applied tree carries the canonical 3."""
              Tree = """{"id":"h","kind":{"$type":"Heading","level":2,"text":"Title"}}"""
              Op = """{"$type":"UpdateProp","path":"Level","target":"h","value":3.0}""" }
            { Id = "updateprop-callout-tone-default-is-omitted"
              Case = "UpdateProp"
              Description =
                """Tone set to its declared default, Default (section 3.6). The applied tree encodes byte-identical to the one that never set it: the member is absent, so encode(apply(op)) is a canonical fixed point. A host that stored the string it was handed would emit `"tone":"Default"`, which no decode produces."""
              Tree = """{"id":"c","kind":{"$type":"Callout","body":"Body","tone":"Warning"}}"""
              Op = """{"$type":"UpdateProp","path":"Tone","target":"c","value":"Default"}""" }
            { Id = "updateprop-callout-tone-nondefault"
              Case = "UpdateProp"
              Description = """The control: Tone set to a case other than the default is stored and encoded."""
              Tree = """{"id":"c","kind":{"$type":"Callout","body":"Body","tone":"Warning"}}"""
              Op = """{"$type":"UpdateProp","path":"Tone","target":"c","value":"Success"}""" } ] } ]

/// The verdict the reference apply path reaches on one vector.
type Verdict =
    | Accept of tree: string
    | Reject of code: string

/// The refusal's code as the corpus spells it: the `ApplyErrorCode` case name.
let codeToken (code: ApplyErrorCode) : string =
    let case, _ = FSharpValue.GetUnionFields(code, typeof<ApplyErrorCode>)
    case.Name

/// Decode the vector's inputs with the reference decoder and apply the op.
/// An input the decoder refuses is an authoring error, never a vector.
let verdict (v: Vector) : Verdict =
    let tree =
        match JsonDecode.decodeNodeObj v.Tree with
        | Ok t -> t
        | Error e -> failwithf "apply vector '%s': the tree does not decode (%s at %s)" v.Id e.Code e.Path

    let op =
        match JsonDecode.decodeOp v.Op with
        | Ok o -> o
        | Error e -> failwithf "apply vector '%s': the op does not decode (%s at %s)" v.Id e.Code e.Path

    match Apply.apply op tree with
    | Ok applied -> Accept(CanonicalJson.encodeNode applied)
    | Error err -> Reject(codeToken err.Code)

/// Render one family file.
let toJson (family: Family) : string =
    let opts =
        JsonWriterOptions(Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping)

    use stream = new MemoryStream()
    use w = new Utf8JsonWriter(stream, opts)

    w.WriteStartObject()
    w.WriteString("family", family.Id)
    w.WriteString("description", family.Description)

    if not family.Limits.IsEmpty then
        w.WriteStartObject("limits")

        for name in family.Limits do
            w.WriteNumber(name, limit name)

        w.WriteEndObject()

    w.WriteStartArray("vectors")

    for v in family.Vectors do
        w.WriteStartObject()
        w.WriteString("id", v.Id)
        w.WriteString("case", v.Case)
        w.WriteString("description", v.Description)
        w.WriteStartObject("input")
        w.WriteString("tree", v.Tree)
        w.WriteString("op", v.Op)
        w.WriteEndObject()
        w.WriteStartObject("expected")

        match verdict v with
        | Accept tree ->
            w.WriteString("verdict", "accept")

            if family.PinsResult then
                w.WriteString("tree", tree)
        | Reject code ->
            w.WriteString("verdict", "reject")
            w.WriteString("code", code)

        w.WriteEndObject()
        w.WriteEndObject()

    w.WriteEndArray()
    w.WriteEndObject()
    w.Flush()

    System.Text.Encoding.UTF8.GetString(stream.ToArray()) + "\n"

/// Write the four families into a corpus directory; returns the paths written.
let write (corpusRoot: string) : string list =
    let dir = Path.Combine(corpusRoot, directory)
    Directory.CreateDirectory dir |> ignore

    [ for family in all do
          let path = Path.Combine(dir, family.File)
          File.WriteAllText(path, toJson family)
          path ]
