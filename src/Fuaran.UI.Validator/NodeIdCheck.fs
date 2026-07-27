module Fuaran.UI.Validator.NodeIdCheck

// ============================================================================
//  NodeId uniqueness check.
//
//  Per-tree duplicate NodeId is an Error (breaks tree-op apply targeting per
//  §4g). Cross-tree duplicate NodeId is a Warning (legitimate when two
//  modules independently use the same id, but worth flagging when it might
//  surprise the AI's reasoning).
//
//  A "tree" is the call sub-graph rooted at a `Fuaran.dashboard` INVOCATION
//  (see AstWalker.treeRoots) — identified by `TreeRootKey`, the call site,
//  not by the root's NodeId. Two invocations rooting `"doc"` are two trees:
//  a template and its stand-in, or two independent fixtures, legitimately
//  share every id, and grouping on the id string would report each of those
//  shared ids as an intra-tree duplicate. Calls without a tree root (loose
//  components) are pooled into a synthetic `__loose__` bucket for cross-tree
//  comparison only — they emit no per-tree duplicates.
// ============================================================================

open Fuaran.UI.Validator.AstWalker
open Fuaran.UI.Validator.Findings

let private looseBucket = "__loose__"

let check (calls: FuaranCall list) : Finding list =
    let withIds =
        calls
        |> List.choose (fun c ->
            match c.NodeIdLiteral with
            | Some id ->
                Some
                    {| Id = id
                       // Identity for grouping — the root CALL SITE.
                       Tree = c.TreeRootKey |> Option.defaultValue looseBucket
                       // The root's NodeId — for the human-readable message only.
                       TreeName = c.TreeRoot |> Option.defaultValue looseBucket
                       Call = c |}
            | None -> None)

    let perTreeErrors =
        withIds
        |> List.groupBy _.Tree
        |> List.collect (fun (tree, items) ->
            if tree = looseBucket then
                []
            else
                items
                |> List.groupBy _.Id
                |> List.collect (fun (id, dups) ->
                    if List.length dups < 2 then
                        []
                    else
                        dups
                        |> List.map (fun w ->
                            create
                                Error
                                "FUARAN001"
                                w.Call.Location
                                (sprintf
                                    "Duplicate NodeId \"%s\" within tree \"%s\" — every NodeId inside one Fuaran.dashboard subtree must be unique (§4g op-target stability)."
                                    id
                                    w.TreeName))))

    let crossTreeWarnings =
        withIds
        |> List.groupBy _.Id
        |> List.collect (fun (id, items) ->
            let distinctTrees = items |> List.map _.Tree |> List.distinct

            if List.length distinctTrees < 2 then
                []
            else
                let names = items |> List.map _.TreeName |> List.distinct |> List.sort

                items
                |> List.map (fun w ->
                    create
                        Warning
                        "FUARAN002"
                        w.Call.Location
                        (sprintf
                            "NodeId \"%s\" appears across multiple trees (%s) — legitimate when modules share a stable id, but worth flagging."
                            id
                            (names |> String.concat ", "))))

    perTreeErrors @ crossTreeWarnings
