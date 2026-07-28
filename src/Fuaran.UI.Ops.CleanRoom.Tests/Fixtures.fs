module Fuaran.UI.Ops.CleanRoom.Tests.Fixtures

// Shared fixtures + helpers for the structure-only clean-room acceptance set.
// Trees are built from the language-tier smart constructors so node ids are
// stable + addressable; content strings are distinctive sentinels so the
// no-content-survives test can search for them.
//
// FUARAN002 flags a NodeId shared across two trees — worth knowing in ordinary
// code, and precisely the contract here: `standInTree` is id-identical to
// `realTree` by design, because `reattach` re-keys one onto the other by
// NodeId (`Map.find tmpl.Id real`). Every id is meant to appear in both, so
// the warning fires ten times and says nothing a reader of this file does not
// already know.
// fuaran-validator: disable FUARAN002 — the stand-in tree is id-identical to the real one by contract

open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops

type Msg = NoMsg

// ─── Distinctive content sentinels (must NOT survive into a Skeleton) ────────

let secretHeadingA = "ACQUISITION_OF_DIAMETRICAL_BY_ACME"
let secretHeadingB = "INDEMNITY_CAP_TWELVE_MILLION_GBP"
let secretBody = "TOP_SECRET_CLAUSE_BODY_TEXT_NEVER_CROSSES_THE_DIVIDE"
let secretMetric = "MNPI_REVENUE_FIGURE"

let private heading (id: string) (text: string) : Node<Msg> =
    Fuaran.heading
        id
        { Level = 2
          Text = TextSource.Literal text
          Variant = HeadingVariant.Standard }

/// A small privileged "document": a dashboard with two clause cards (each a
/// heading) + a prose markdown + a metric. Ids are opaque structural tokens;
/// content carries the sentinels above.
let realTree: Node<Msg> =
    Fuaran.dashboard
        "doc-root"
        { Defaults.dashboard<Msg> with
            Children =
                [ Fuaran.card
                      "clause-1"
                      { Defaults.card<Msg> with
                          Heading = Some(TextSource.Literal secretHeadingA)
                          Children = [ heading "clause-1-title" secretHeadingA ] }
                  Fuaran.card
                      "clause-2"
                      { Defaults.card<Msg> with
                          Heading = Some(TextSource.Literal secretHeadingB)
                          Children = [ heading "clause-2-title" secretHeadingB ] }
                  Fuaran.markdown "recital" secretBody
                  Fuaran.metric
                      "headline-metric"
                      { Defaults.metric with
                          Label = TextSource.Literal secretMetric } ] }

/// Structurally + id-identical to `realTree`, but with placeholder content —
/// the content-free stand-in an untrusted side would reconstruct from a
/// skeleton. Used by the determinism proof.
let standInTree: Node<Msg> =
    Fuaran.dashboard
        "doc-root"
        { Defaults.dashboard<Msg> with
            Children =
                [ Fuaran.card
                      "clause-1"
                      { Defaults.card<Msg> with
                          Heading = Some(TextSource.Literal "a")
                          Children = [ heading "clause-1-title" "a" ] }
                  Fuaran.card
                      "clause-2"
                      { Defaults.card<Msg> with
                          Heading = Some(TextSource.Literal "b")
                          Children = [ heading "clause-2-title" "b" ] }
                  Fuaran.markdown "recital" "c"
                  Fuaran.metric
                      "headline-metric"
                      { Defaults.metric with
                          Label = TextSource.Literal "d" } ] }

// ─── Apply helpers ──────────────────────────────────────────────────────────

let applyOk (op: Fuaran.UI.Ops.Types.TreeOp<Msg>) (tree: Node<Msg>) : Node<Msg> =
    match Apply.apply op tree with
    | Ok updated -> updated
    | Error err -> failwithf "expected Ok, got Error %A" err

// ─── Determinism-proof helpers (generic over kind via Introspect) ───────────

/// Map every node in `node`'s subtree by its id.
let rec nodesById (node: Node<Msg>) : Map<string, Node<Msg>> =
    let here = Map.add node.Id node Map.empty

    match Introspect.getChildren node.Kind with
    | None -> here
    | Some kids ->
        kids
        |> List.fold (fun acc c -> nodesById c |> Map.fold (fun a k v -> Map.add k v a) acc) here

/// Rebuild `tmpl`'s structure but take each node's identity + content from
/// `real` (keyed by id), keeping `tmpl`'s child arrangement. This is the
/// "re-attach real content by NodeId onto the structurally-rearranged
/// stand-in" operation the determinism proof checks against the real apply.
let rec reattach (real: Map<string, Node<Msg>>) (tmpl: Node<Msg>) : Node<Msg> =
    let realNode = Map.find tmpl.Id real

    match Introspect.getChildren tmpl.Kind with
    | None -> realNode
    | Some kids ->
        let reKids = kids |> List.map (reattach real)

        match Introspect.withChildren realNode.Kind reKids with
        | Some k -> { realNode with Kind = k }
        | None -> realNode
