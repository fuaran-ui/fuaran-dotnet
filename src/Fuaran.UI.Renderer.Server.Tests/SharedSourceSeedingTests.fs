module Fuaran.UI.Renderer.Server.Tests.SharedSourceSeedingTests

// ============================================================================
//  The `Binding.State` SEEDING rule on the render path (Phase 1075).
//
//  The shared-data-source charter's §3.1 pair, executed: a `DataGrid` bound to
//  `$state.members` and carrying the rows on its own `defaultValue`, beside a
//  `Badge` whose `Transform` derives a count over the SAME key with no default
//  of its own. Before this phase that document decoded, rendered, and showed a
//  count of ZERO forever — `defaultValue` was a per-reader fallback, nothing
//  ever wrote the grid's rows into the store, and the badge's live source
//  started from `TransformLive.emptySource`. No decoder refused it and no
//  validator named it.
//
//  These tests pin the whole rule as behaviour rather than as intent:
//
//   * the BEFORE and the AFTER in one assertion, by resolving the same badge
//     binding against unseeded sources (the shipped pre-1075 semantics,
//     reachable exactly by handing the resolver an empty map) and against the
//     seeded ones the renderers now build;
//   * the SSR output, which is what a reader actually sees;
//   * the precedence rule (charter §4) — a host-furnished value wins over a
//     seed, because a seed is the value before anything else has said anything;
//   * order-independence (charter §5) — the badge declared BEFORE the grid
//     reads the same rows, because the pass runs over the whole tree before any
//     binding resolves.
//
//  `BindingWalk.stateSeeds` is called directly rather than through a renderer
//  entry point on purpose: it is the ONE definition both reference renderers
//  call (the client's `Render.withStateSeeds`, the server's `mkContextWith`),
//  so a test that goes through it tests what both hosts do.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server

let private contains (needle: string) (haystack: string) =
    haystack.Contains(needle, System.StringComparison.Ordinal)

/// The charter's §3.1 grid: the rows ride the grid's own `defaultValue` on
/// `$state.members`.
let private gridJson =
    """{"id":"member-grid","kind":{"$type":"DataGrid","columns":[{"field":"team","kind":{"$type":"Text"},"label":"Team"}],"rowKeyField":"team","source":{"$type":"State","defaultValue":[{"team":"Ops"},{"team":"Research"}],"key":"members"}}}"""

/// The charter's §3.1 badge: a `groupBy` + `count` over the SAME key, carrying
/// no data of its own. This is the reader the seeding rule exists for.
///
/// **`"defaultValue":[]` is how "I carry no data" is spelled in a Transform's
/// source slot on the wire today, and the charter did not know it.** §3.1
/// writes the source as a bare `{"$type":"State","key":"members"}` and asserts
/// "no decoder refuses it"; the decoder DOES refuse it (`WRONG_TYPE` at
/// `$.kind.source.source`, pinned by `reject/reject-transform-source-empty-wrapper`
/// and specified in `WIRE_FORMAT.md` §16 — a State wrapper carrying neither
/// `defaultValue` nor `value` is not unwrappable). The bare form is reachable
/// only from a hand-authored F# tree. The empty array is therefore the wire
/// spelling of the charter's pair, and it is exempt from the seeding rules by
/// construction (`BindingWalk.isEmptySeed`) so it neither seeds the slot empty
/// nor conflicts with the grid beside it. Widening the decoder to accept the
/// bare form is recorded as a finding and routed, not taken here: it is
/// authored in the IDL support module of a different repo.
let private badgeJson =
    """{"id":"member-count","kind":{"$type":"Badge","label":{"$type":"Bound","binding":{"$type":"Transform","pipeline":[{"$type":"groupBy","aggs":[{"fn":"count","name":"n","of":"team"}],"keys":[]}],"source":{"$type":"State","defaultValue":[],"key":"members"}}},"variant":"Info"}}"""

let private pairJson (childrenInOrder: string list) =
    sprintf
        """{"id":"shared-source-pair","kind":{"$type":"Box","children":[%s],"heading":"Members","layout":{"$type":"Auto"},"role":"Dashboard"}}"""
        (String.concat "," childrenInOrder)

let private decode (json: string) : Node<obj> =
    match Fuaran.UI.Generated.decodeNode json with
    | Ok node -> node
    | Error e -> failwithf "decode failed: %s" e

let rec private findNode (id: string) (node: Node<obj>) : Node<obj> =
    if node.Id = id then
        node
    else
        let children =
            match node.Kind with
            | NodeKind.Box s -> s.Children
            | _ -> []

        match
            children
            |> List.tryPick (fun c ->
                (try
                    Some(findNode id c)
                 with _ ->
                     None))
        with
        | Some n -> n
        | None -> failwithf "node '%s' not found" id

let private badgeBinding (tree: Node<obj>) : Binding<string> =
    match (findNode "member-count" tree).Kind with
    | NodeKind.Badge spec ->
        match spec.Label with
        | TextSource.Bound b -> b
        | other -> failwithf "unexpected badge label: %A" other
    | k -> failwithf "unexpected kind: %A" k

/// The badge's rendered text under a given `State` map — the exact dispatch the
/// client renderer's `renderText` Bound arm makes.
let private badgeTextUnder (state: Map<string, obj>) (tree: Node<obj>) : string =
    BindingResolver.tryResolveScalarText
        { BindingResolver.empty with
            State = state }
        (badgeBinding tree)
    |> Option.defaultValue "<unresolved>"

[<Tests>]
let sharedSourceSeedingTests =
    testList
        "Binding.State seeding (Phase 1075 — the shared-data-source charter's O1)"
        [ test "the charter's §3.1 pair: the derived badge moves from zero to the count" {
              let tree = decode (pairJson [ gridJson; badgeJson ])

              // BEFORE — the shipped pre-1075 semantics, reachable exactly by
              // handing the resolver an unseeded map: the grid's default never
              // reached the store, so the badge's live source started from
              // `TransformLive.emptySource` and the derivation produced NOTHING.
              //
              // MEASURED, and it corrects the charter in one detail worth
              // keeping. §3.1 says the pair "renders a count of zero"; what it
              // actually renders on this shape is an EMPTY badge, because an
              // empty table has no columns at all and Core refuses to group by
              // one that is not there. The zero the charter sighted belongs to
              // the OTHER half of the same defect — a badge deriving over its
              // own separate inline copy. Silent either way, and wrong either
              // way, which is the claim that mattered.
              Expect.equal
                  (badgeTextUnder Map.empty tree)
                  "<unresolved>"
                  "unseeded, the derived value is not there at all"

              // AFTER — the seeding pass puts the grid's declared rows under
              // `$state.members`, and the badge's Transform derives over them.
              let seeds = BindingWalk.stateSeeds tree

              Expect.equal (seeds |> Map.toList |> List.map fst) [ "members" ] "one key is seeded, by the grid"

              Expect.equal (badgeTextUnder seeds tree) "2" "seeded, the badge counts the grid's two rows"
          }

          test "the SSR frame renders the seeded value, not the zero" {
              // What a reader actually sees. The server tier seeds at its single
              // context choke point, so every entry point below it renders the
              // same first frame the client will hydrate against.
              let tree = decode (pairJson [ gridJson; badgeJson ])
              let html = Render.render BindingResolver.empty tree

              Expect.isTrue
                  (contains "fuaran-badge-info\">2<" html)
                  "the badge carries the count derived from the seeded slot"

              // The grid reads the SAME slot and reports the same two rows. The
              // server tier emits a hydration placeholder for a grid rather than
              // its cells, so the row COUNT is what it publishes — which is
              // exactly the number under test.
              Expect.isTrue (contains "data-fuaran-row-count=\"2\"" html) "the grid resolves the same two rows"

              // The before-state, for contrast: the badge alone, with nothing to
              // seed the slot, renders an empty span.
              let unseededHtml = Render.render BindingResolver.empty (decode badgeJson)

              Expect.isTrue
                  (contains "fuaran-badge-info\"></span>" unseededHtml)
                  "unseeded, the derived value renders as nothing"
          }

          test "order-independent: the badge declared BEFORE the grid reads the same rows" {
              // Charter §5. Decode produces a tree and resolution happens at
              // render, so the seeding pass runs over the WHOLE tree before any
              // binding resolves — a forward reference is not a special case.
              let forward = decode (pairJson [ badgeJson; gridJson ])

              Expect.equal
                  (badgeTextUnder (BindingWalk.stateSeeds forward) forward)
                  "2"
                  "document order carries no meaning"
          }

          test "precedence: a host-furnished value wins over the seed" {
              // Charter §4 — the seed is the value before anything else has said
              // anything, never an override. This is the only reading consistent
              // with the wire's standing posture that the host owns named data.
              let tree = decode (pairJson [ gridJson; badgeJson ])

              let hostRows: obj =
                  box (Seq.ofList [ (Map.ofList [ "team", Unchecked.nonNull (box "Ops") ]: Fuaran.Core.Row) ])

              let seeded = BindingWalk.stateSeeds tree
              let hostWins = seeded |> Map.add "members" hostRows

              Expect.equal (badgeTextUnder hostWins tree) "1" "the host's one row, not the tree's two"

              // And the server renderer applies that precedence itself.
              let html =
                  Render.render
                      { BindingResolver.empty with
                          State = Map.ofList [ "members", hostRows ] }
                      tree

              Expect.isTrue (contains ">1<" html) "SSR reads the host's value over the seed"
          }

          test "a host-reserved key is never seeded (Phase 782's floor holds)" {
              // A seed is a tree-originated write. Letting one land in the
              // host-reserved namespace would give the wire a way around a
              // deliberate floor, so the pass refuses it — the same refusal
              // `Action.SetState` meets on every path.
              let reservedGrid =
                  gridJson.Replace(
                      "\"key\":\"members\"",
                      "\"key\":\"" + StateKeyPolicy.HostReservedPrefix + "members\""
                  )

              let tree = decode (pairJson [ reservedGrid ])

              Expect.isEmpty (BindingWalk.stateSeeds tree |> Map.toList) "the tree cannot seed a host slot"
          }

          test "an unseeded, unpolarised tree is untouched" {
              // The zero-cost claim, made falsifiable. A tree that declares no
              // default seeds nothing, and the sources it renders against are
              // the caller's own map by identity.
              let tree = decode badgeJson

              Expect.isEmpty (BindingWalk.stateSeeds tree |> Map.toList) "nothing declared, nothing seeded"
          } ]
