module Fuaran.UI.Tests.CensusSpellingTeaching

// ============================================================================
//  Phase 1799 — the two spellings a flip census put ahead of every other, and
//  the guard that keeps their teaching honest.
//
//  An offline census over the stored emissions (Phase 1782) counted 51
//  `project` steps naming their member `columns` and 10 `Box` nodes carrying no
//  `layout`. Both refuse at decode and a refusal costs the WHOLE document, so
//  the teaching landed beside these assertions rather than on its own.
//
//  WHAT THIS FILE IS FOR, and it is not a decoder test. Every fragment the
//  teaching tells an author to WRITE is pinned as decoding, and every shape it
//  warns against is pinned as REFUSED. Only the pair is evidence: a warning
//  about a shape the decoder happily accepts is a superstition, and teaching
//  one costs an author a member for nothing. So if the wire ever admits these,
//  this file goes red and the prose is corrected by whoever moved the wire —
//  which is the point, since nothing else reads that prose at all.
//
//  The `project` fragments are decoded inside a HOST DOCUMENT rather than on
//  their own. A pipeline step is not a node, so the only honest way to ask
//  whether the taught shape decodes is to put it where an emission would put
//  it: in a `Transform` binding, under a node, through `decodeNode`.
// ============================================================================

open Expecto

/// A one-node document whose `Callout` body is a `Transform` over a two-column
/// embedded table, with `steps` spliced in as the pipeline. Shaped after the
/// pack's own scalar-transform example, which is the context a `project` step
/// is actually taught in.
let private transformDoc (steps: string) =
    """{"id":"project-probe","kind":{"$type":"Callout","body":{"$type":"Bound","binding":{"$type":"Transform","pipeline":["""
    + steps
    + """],"source":{"columns":{"alert":["SLA breach imminent"],"dept":["eng"]}}}},"heading":"Alert","tone":"Warning"}}"""

let private decodes (what: string) (json: string) =
    match Fuaran.UI.Generated.decodeNode json with
    | Error e -> failtestf "%s does not decode: %s" what e
    | Ok _ -> ()

let private refused (what: string) (json: string) =
    match Fuaran.UI.Generated.decodeNode json with
    | Ok _ -> failtestf "%s DECODED — the teaching's warning about it is stale" what
    | Error _ -> ()

[<Tests>]
let tests =
    testList
        "Phase 1799 — the two census spellings the pack teaches"
        [
          // ── `project` takes `cols`, and its entries are rename pairs ────────
          test "the taught identity projection decodes" {
              // The shape the teaching gives for "keep this column as it is":
              // the same string twice.
              transformDoc """{"$type":"project","cols":[{"a":"dept","b":"dept"}]}"""
              |> decodes "the taught identity projection"
          }

          test "the taught renaming projection decodes" {
              // The pair form carrying an actual rename, verbatim from the
              // teaching's example block.
              transformDoc """{"$type":"project","cols":[{"a":"alert","b":"alert"},{"a":"dept","b":"Assigned to"}]}"""
              |> decodes "the taught renaming projection"
          }

          test "the census's own failing spelling — `columns` — is REFUSED" {
              // 51 sites across the stored cohorts. The bare-string list is the
              // shape those emissions actually carried, so this is the census's
              // input and not a constructed one.
              transformDoc """{"$type":"project","columns":["dept","alert"]}"""
              |> refused "a `project` step spelling its member `columns`"
          }

          test "the member's name alone is not the fix — `cols` of BARE STRINGS is REFUSED" {
              // The half a reader is most likely to stop at, and the reason the
              // teaching shows the member's CONTENTS rather than only its name:
              // renaming `columns` to `cols` still refuses.
              transformDoc """{"$type":"project","cols":["dept","alert"]}"""
              |> refused "a `project` step whose `cols` holds bare strings"
          }

          // ── Every `Box` states its `layout` ─────────────────────────────────
          test "the taught `Auto`-layout Box decodes" {
              // Verbatim from the teaching's example block.
              """{"id":"reports","kind":{"$type":"Box","children":[{"id":"reports-note","kind":{"$type":"Markdown","text":"No reports yet."}}],"layout":{"$type":"Auto"},"role":"Group"}}"""
              |> decodes "the taught `Auto`-layout Box"
          }

          test "a `Box` with no `layout` is REFUSED" {
              // 10 sites in the census, 5 of them one-to-one recoverable as
              // `Auto` — which is what makes the omission worth naming rather
              // than tolerating: the author meant something the wire can say.
              """{"id":"reports","kind":{"$type":"Box","children":[{"id":"reports-note","kind":{"$type":"Markdown","text":"No reports yet."}}],"role":"Group"}}"""
              |> refused "a `Box` carrying no `layout`"
          }

          test "a `Box` with no `role` is REFUSED too" {
              // The teaching names `role` in the same breath as `layout`, so the
              // claim is pinned in the same breath. Dropping it would leave the
              // prose asserting something no test reads.
              """{"id":"reports","kind":{"$type":"Box","children":[{"id":"reports-note","kind":{"$type":"Markdown","text":"No reports yet."}}],"layout":{"$type":"Auto"}}}"""
              |> refused "a `Box` carrying no `role`"
          }

          // ── The two `cols` are different members, which the teaching says ───
          test "a `Grid` layout's `cols` is an INTEGER, and the pair form is refused there" {
              """{"id":"tiles","kind":{"$type":"Box","children":[],"layout":{"$type":"Grid","cols":3},"role":"Dashboard"}}"""
              |> decodes "a `Grid` layout with an integer `cols`"

              """{"id":"tiles","kind":{"$type":"Box","children":[],"layout":{"$type":"Grid","cols":[{"a":"dept","b":"dept"}]},"role":"Dashboard"}}"""
              |> refused "a `Grid` layout whose `cols` holds rename pairs"
          } ]
