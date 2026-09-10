module Fuaran.UI.Tests.ReconciliationKeyTests

// ============================================================================
//  React reconciles a list of siblings by POSITION unless each carries a key,
//  and position is the wrong identity for anything a reader can re-order.
//
//  Three things are proved here, and they are deliberately different in kind:
//
//    1. THE RULE — `Render.reconciliationKey`: a declared key wins; the two
//       spellings that are `Some` and are still not identities (`""` and the
//       decoded `"<closure>"` placeholder) fall back; the fallback can never
//       collide with a real key.
//    2. THE PROPERTY the rule exists for — a row's key FOLLOWS THE ROW across
//       a re-sort while its index does not. This is the assertion behind "a
//       `Local` input buffer follows its row": what React keeps per position
//       is the component instance, and everything an instance holds that the
//       props do not carry (an uncommitted text buffer, an open combobox, a
//       running timer) goes with the position unless a key moves it.
//    3. THE EMISSION SITES still carry a key. Read off the renderer's own
//       sources — copied into this bin by the test project, the same input
//       `CssCoverageTests` scans — because the property is about DOM the .NET
//       pipeline never produces: a React hook cannot be mounted off a browser,
//       so the alternative to reading the source is asserting nothing at all.
//
//  A note on what (3) is and is not. It is a REGRESSION pin: an arm that loses
//  its key goes red here. It is not a discovery mechanism — a brand-new
//  repeated-children arm is invisible to it until its class is added to the
//  list below, which is exactly the same forward-coupling obligation the CSS
//  coverage scan carries, and is stated rather than implied.
// ============================================================================

open System
open System.IO
open Expecto
open Fuaran.Core
open Fuaran.UI.Renderer

// ── 1. The rule ────────────────────────────────────────────────────────────

[<Tests>]
let ruleTests =
    testList
        "the reconciliation-key rule"
        [ test "a declared key is used verbatim" {
              Expect.equal (Render.reconciliationKey (Some "order-42") 7) "order-42" "the declared key wins"
          }

          test "an absent key falls back to the position, marked so it cannot collide" {
              // The mark matters: without it a positional key of "3" and a
              // declared key of "3" — an id, a value, a field name — would be
              // the same string, and one row would inherit the other's
              // instance. The prefix is not a legal declared key here because
              // a declared key is used verbatim and never gains one.
              Expect.equal (Render.reconciliationKey None 3) "#3" "the position, marked"

              Expect.notEqual
                  (Render.reconciliationKey None 3)
                  (Render.reconciliationKey (Some "3") 9)
                  "and it cannot collide with a declared key that reads as the same number"
          }

          test "the two NON-identities that arrive as `Some` fall back" {
              // The empty string is what a grid with no key contract answers,
              // and `"<closure>"` is what a DECODED `RowKey` projects — a
              // constant, so EVERY row would answer the same key and React
              // would treat four different rows as one. That is strictly worse
              // than positional reconciliation, which is why it is refused
              // rather than passed through.
              Expect.equal (Render.reconciliationKey (Some "") 2) "#2" "an empty key is not an identity"
              Expect.equal (Render.reconciliationKey (Some "<closure>") 2) "#2" "and neither is the decoded placeholder"

              let keysFromPlaceholder =
                  [ 0..3 ] |> List.map (Render.reconciliationKey (Some "<closure>"))

              Expect.equal (List.distinct keysFromPlaceholder).Length 4 "four rows get four keys, not one"
          } ]

// ── 2. The property: a key follows its row across a re-sort ────────────────

/// The grid's own row-key resolution, in the shape `Render` builds it: the
/// closure override wins, else the declared field projects, else there is no
/// identity at all.
let private rowKeyOf (rowKeyField: string option) : (Row -> string) option =
    rowKeyField
    |> Option.map (fun field -> fun (row: Row) -> BindingResolver.projectRowFieldString row field)

let private row (id: string) (name: string) : Row =
    Map.ofList [ "id", (box id |> Unchecked.nonNull); "name", (box name |> Unchecked.nonNull) ]

[<Tests>]
let resortTests =
    testList
        "a row's key follows the row across a re-sort"
        [ test "the key is invariant under re-ordering; the position is not" {
              let alice = row "u-3" "Alice"
              let bob = row "u-1" "Bob"
              let cleo = row "u-2" "Cleo"

              // The order the reader is looking at, then the order after they
              // click the id column.
              let byName = [ alice; bob; cleo ]
              let byId = [ bob; cleo; alice ]

              let keyOf = rowKeyOf (Some "id")

              let keysOf (rows: Row list) =
                  rows
                  |> List.mapi (fun i r -> Render.reconciliationKey (keyOf |> Option.map (fun k -> k r)) i)

              Expect.equal (keysOf byName) [ "u-3"; "u-1"; "u-2" ] "each row is keyed by its own id"
              Expect.equal (keysOf byId) [ "u-1"; "u-2"; "u-3" ] "and carries that id to its new position"

              // The finding, stated as an identity claim: Alice is the FIRST
              // row before the sort and the LAST after it, and the key React
              // reconciles her by is the same string in both. Her half-typed
              // `Local` buffer moves with her rather than staying at index 0
              // and appearing on Bob's row.
              let aliceKeyBefore = (keysOf byName) |> List.item 0
              let aliceKeyAfter = (keysOf byId) |> List.item 2

              Expect.equal aliceKeyBefore aliceKeyAfter "the same row answers the same key at a different position"

              // The go-red twin: WITHOUT a key contract the identity is the
              // position, so the first row before the sort and the first row
              // after it are "the same" — which is precisely the defect.
              let positional (rows: Row list) =
                  rows |> List.mapi (fun i _ -> Render.reconciliationKey None i)

              Expect.equal
                  (positional byName)
                  (positional byId)
                  "positional identity cannot tell a re-sort from no change at all"
          }

          test "a grid with no key contract is honest about it rather than pretending" {
              // No `RowKey`, no `RowKeyField`: nothing on the wire states an
              // identity, so the renderer must not invent one. It falls back to
              // the position and the buffer-follows-row property genuinely does
              // not hold — which is what the Phase 425 unstable-key validator
              // advice tells an author to fix, in the tree rather than here.
              let keyOf = rowKeyOf None
              Expect.isNone keyOf "no field, no key closure"

              let keys =
                  [ row "u-1" "Bob"; row "u-2" "Cleo" ]
                  |> List.mapi (fun i r -> Render.reconciliationKey (keyOf |> Option.map (fun k -> k r)) i)

              Expect.equal keys [ "#0"; "#1" ] "the position, marked as such"
          } ]

// ── 3. The emission sites still carry a key ────────────────────────────────

/// The renderer sources this build compiled, copied into the bin by the test
/// project rather than resolved by climbing — so the scan cannot read a
/// different checkout's sources than the ones under test.
let private clientRenderSource: string =
    Path.Combine(AppContext.BaseDirectory, "renderer-sources", "client", "Render.fs")

/// Every repeated-children arm the client renderer emits, named for the reader
/// and anchored on a token that appears in THAT element's own props list.
///
/// An anchor rather than a class name, because two of the arms build their
/// class inside a conditional expression (a selected row, an active step) and
/// one class is shared by a repeated child and a lone one (a grid button cell
/// versus a button GROUP's members). Each anchor below is unique in the file
/// and sits on the element it identifies; the scan climbs from it to the
/// `Html.<tag>` that opened the props list and asks whether a key was stated in
/// between. `MISSING ANCHOR` is reported as loudly as a missing key: a scan
/// that silently matches nothing is the vacuous-pass failure a source-reading
/// test has and an ordinary one does not.
let private repeatedChildArms =
    [ "grid row", "gridRowSelected (runAction ctx) parentNodeId spec row)"
      "grid cell", "prop.className \"fuaran-grid-cell\""
      "grid header", "prop.className \"fuaran-grid-header\""
      "grid button-group member", "match item.OnClick with"
      "tab", "prop.role \"tab\""
      "stepper step", "\"fuaran-stepper-step\""
      "tree item", "prop.className \"fuaran-tree-item\""
      "list item", "prop.className \"fuaran-list-item\""
      "skeleton row", "prop.className \"fuaran-skeleton-row\""
      "form field", "prop.className \"fuaran-form-field\""
      // Phase 1648 — the chip's class became `Css.filter (Theme.filterKindClass
      // spec.Kind)`, because this renderer had been emitting a bare
      // `fuaran-filter` where the server emits the kind suffix too. The anchor
      // moves with it; the MISSING ANCHOR report is what caught the rename,
      // which is the whole reason it is reported as loudly as a missing key.
      "filter chip", "Css.filter (Theme.filterKindClass spec.Kind)"
      "segmented option", "prop.className \"fuaran-segmented-option\""
      "segmented row", "prop.className \"fuaran-segmented-row\""
      "select option", "prop.text option.Label"
      "static table header", "prop.className \"fuaran-table-header\""
      "static table row", "prop.className \"fuaran-table-row\""
      "static table cell", "prop.className \"fuaran-table-cell\""
      "map marker", "prop.className \"fuaran-map-marker\"" ]

/// Scan upward from an emission site to the `Html.<element>` that opens it, and
/// report whether a `prop.key` sits between the two. Upward rather than
/// downward because a key is written first in a props list, and bounded by the
/// element constructor rather than by a line count so re-formatting cannot
/// change the answer.
let private keyedAtSite (lines: string array) (siteIndex: int) : bool =
    let rec climb i =
        if i < 0 then false
        elif lines[i].Contains "prop.key" then true
        elif lines[i].TrimStart().StartsWith "Html." then false
        else climb (i - 1)

    climb siteIndex

[<Tests>]
let emissionSiteTests =
    testList
        "every repeated-children arm states an identity"
        [ test "the renderer sources are where this build's are" {
              // Guard against the scan passing vacuously on a bin that never
              // received the copy — the failure mode a source-reading test has
              // that an ordinary one does not.
              Expect.isTrue
                  (File.Exists clientRenderSource)
                  (sprintf "renderer source copied to the bin: %s" clientRenderSource)
          }

          test "each repeated child carries `prop.key` at its emission site" {
              let lines = File.ReadAllLines clientRenderSource

              let missing =
                  [ for (name, anchor) in repeatedChildArms do
                        let sites =
                            [ for i in 0 .. lines.Length - 1 do
                                  if lines[i].Contains anchor then
                                      yield i ]

                        if List.isEmpty sites then
                            yield sprintf "%s — MISSING ANCHOR '%s' (renamed or removed?)" name anchor
                        else
                            for site in sites do
                                if not (keyedAtSite lines site) then
                                    yield sprintf "%s — line %d has no prop.key in its props list" name (site + 1) ]

              Expect.isEmpty missing (sprintf "unkeyed repeated children:\n  %s" (String.Join("\n  ", missing)))
          }

          test "GO-RED TWIN: the scan really does detect an unkeyed site" {
              // Without this the assertion above could pass because
              // `keyedAtSite` answers `true` for everything. A synthetic
              // props list with no key must be reported as unkeyed, and the
              // same list with one must not.
              let unkeyed =
                  [| "Html.tr ("
                     "    [ prop.className \"fuaran-grid-row\""
                     "      prop.onClick ignore ]" |]

              let keyed =
                  [| "Html.tr ("
                     "    [ prop.key \"x\""
                     "      prop.className \"fuaran-grid-row\""
                     "      prop.onClick ignore ]" |]

              Expect.isFalse (keyedAtSite unkeyed 1) "an unkeyed props list is reported unkeyed"
              Expect.isTrue (keyedAtSite keyed 2) "and a keyed one is not"
          } ]
