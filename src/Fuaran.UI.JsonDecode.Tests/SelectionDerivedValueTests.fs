module Fuaran.UI.JsonDecode.Tests.SelectionDerivedValue

// ============================================================================
//  `master-detail-preselected-second-row` — the BEHAVIOURAL leg.
//
//  The intent guards in RoundTripTests pin the fixture's SHAPE: the Selection
//  default names the second row, the `note` column is per-row-distinct, the
//  scalar pipeline keeps its `filter -> project -> limit` stages. Those exist
//  because every one of those properties could be "tidied" away without
//  breaking a single round-trip — but they still only prove the bytes say
//  something interesting, never that a host DERIVES the right answer from them.
//
//  That is the whole point of a non-first-row default. Pruning and seeding are
//  indistinguishable when the default is row 0 — an unfiltered pipeline
//  surfaces row 1 anyway, so a host that ignores the Selection looks correct.
//  Naming row 2 makes the two answers differ, and only an assertion on the
//  DERIVED values can see the difference.
//
//  With nothing written to `Selections`, resolution-time defaulting seeds the
//  Selection binding and the Transform param alike, so:
//    - `detail-ticket` resolves to `TCK-2042`      (not `TCK-2041`)
//    - `detail-note`   resolves to "Search index stale"
//                                                   (not "Payment gateway timeout")
//    - `related-grid`  prunes to the single TCK-2042 row  (not all three)
//
//  The evaluator is `BindingResolver` in `Fuaran.UI.Renderer.Core`, which this
//  project already references. It reads the COMMITTED fixture, not the F#
//  fixture values, so it certifies against the same artefact every other host
//  does. The Python host asserts the same pruning (fuaran-py@a7f00ed); the Rust
//  leg (`selection_default_seeds_master_detail`) asserts the equivalent on the
//  first-row variant.
// ============================================================================

open Expecto
open Fuaran.UI.Types
open Fuaran.UI.Ops
open Fuaran.UI.Renderer

let private fixtureId = "master-detail-preselected-second-row"

let private corpus = Corpus.load ()
let private corpusRoot = fst corpus
let private corpusEntries = snd corpus

let private decodedFixture () : Node<obj> =
    match corpusEntries |> List.tryFind (fun e -> e.Id = fixtureId) with
    | None -> failtestf "fixture '%s' is not in the corpus manifest (regenerate with --emit-corpus)" fixtureId
    | Some e ->
        match JsonDecode.decodeNodeObj (Corpus.readPayload corpusRoot e.InputFile) with
        | Ok n -> n
        | Error err -> failtestf "fixture '%s' failed to decode: %A" fixtureId err

let rec private tryFindById (id: string) (n: Node<obj>) : Node<obj> option =
    if n.Id = id then
        Some n
    else
        match n.Kind with
        | NodeKind.Box b -> b.Children |> List.tryPick (tryFindById id)
        | _ -> None

let private nodeById (id: string) : Node<obj> =
    match tryFindById id (decodedFixture ()) with
    | Some n -> n
    | None -> failtestf "the fixture no longer carries a node '%s' — the behavioural leg has lost its subject" id

/// The scalar-slot resolution of a `TextSource.Bound` slot, with no selection
/// written — so what is being read is resolution-time DEFAULTING, not a store.
let private resolveBoundText (label: string) (slot: TextSource) : string =
    match slot with
    | TextSource.Bound binding ->
        match BindingResolver.resolveScalarText BindingResolver.empty binding with
        | BindingResolver.Resolved s -> s
        | other -> failtestf "%s: expected the slot to resolve, got %A" label other
    | other -> failtestf "%s: expected a Bound slot (the derivation is the point), got %A" label other

[<Tests>]
let nonFirstRowSelectionBehaviour =
    testList
        "Fuaran.UI.Ops.JsonDecode — non-first-row Selection fixture (derived values)"
        [ testCase "detail-ticket resolves the Selection default — the SECOND row" (fun () ->
              match (nodeById "detail-ticket").Kind with
              | NodeKind.Fact f ->
                  Expect.equal
                      (resolveBoundText "detail-ticket" f.Value)
                      "TCK-2042"
                      "with no selection written, the Selection defaultValue seeds the read. TCK-2041 here means the default was ignored and the first row surfaced anyway — the masking this fixture exists to break"
              | other -> failtestf "expected 'detail-ticket' to be a Fact, got %A" other)

          testCase "detail-note resolves the note OF THAT ROW, not of the first" (fun () ->
              match (nodeById "detail-note").Kind with
              | NodeKind.Callout c ->
                  Expect.equal
                      (resolveBoundText "detail-note" c.Body)
                      "Search index stale"
                      "the scalar `filter -> project -> limit 1` pipeline is seeded by the same Selection param. 'Payment gateway timeout' is the first row's note — a WRONG row carrying a well-formed value, which is why this asserts the value and not the row count"
              | other -> failtestf "expected 'detail-note' to be a Callout, got %A" other)

          testCase "related-grid prunes to the one selected row" (fun () ->
              match (nodeById "related-grid").Kind with
              | NodeKind.DataGrid g ->
                  match BindingResolver.resolve BindingResolver.empty g.Source with
                  | BindingResolver.Resolved rows ->
                      let rows = List.ofSeq rows

                      Expect.equal
                          (List.length rows)
                          1
                          "the grid's Transform filters on the Selection-seeded param, so it must prune the 3-row source to 1. Three rows means the param was left unbound and the filter step was pruned instead of applied"

                      Expect.equal
                          (BindingResolver.projectRowFieldString rows.[0] "id")
                          "TCK-2042"
                          "pruning to ONE row is not enough — it must be the selected one"
                  | other -> failtestf "expected the grid source to resolve to rows, got %A" other
              | other -> failtestf "expected 'related-grid' to be a DataGrid, got %A" other) ]
