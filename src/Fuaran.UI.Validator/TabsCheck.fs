module Fuaran.UI.Validator.TabsCheck

// ============================================================================
//  Tabs-shape check.
//
//  Three rules, all keyed on the `TabsDetail` snapshot AstWalker captures for
//  `Fuaran.tabs` / `Fuaran.tabsTagged` calls. Mirrors the runtime
//  `PreEmitValidate.fs` rules byte-for-byte so build-time and runtime defects
//  agree:
//
//   - FUARAN047 (Error): `TabHeaders = Some hs` whose length differs from the
//     `Children` list length. The renderer aligns headers 1:1 with children
//     by index; mismatched lengths leave tabs without labels or labels
//     without targets.
//
//   - FUARAN048 (Error): `TabTags = Some ts` whose length differs from the
//     `Children` list length. Same rule for the typed tag overlay — the
//     `tag → index` round-trip relies on parity.
//
//   - FUARAN049 (Warning): `ActiveTag = Some _` but `TabTags = None` (or
//     the `TabTags` field absent). The tag binding has nothing to resolve
//     against; the renderer silently falls back to `ActiveIndex`, which is
//     rarely what the author intended.
//
//  Conservative when the count isn't statically resolvable: `Children` /
//  `TabHeaders` / `TabTags` bound to a let-variable or a function call leave
//  the corresponding `*ListLength` slot `None`, and the count-mismatch rules
//  do not fire. Catching those would require typed-AST evaluation, which is
//  out of scope per the validator's anti-pattern boundary.
// ============================================================================

open Fuaran.UI.Validator.AstWalker
open Fuaran.UI.Validator.Findings

let private nodeIdQuoted (call: FuaranCall) =
    match call.NodeIdLiteral with
    | Some id -> sprintf "\"%s\"" id
    | None -> "<no id>"

let check (calls: FuaranCall list) : Finding list =
    calls
    |> List.collect (fun call ->
        match call.Ctor, call.TabsDetail with
        | "tabs", Some detail ->
            let id = nodeIdQuoted call

            let headerMismatch =
                match detail.ChildrenListLength, detail.TabHeadersListLength with
                | Some childCount, Some headerCount when headerCount <> childCount ->
                    [ create
                          Error
                          "FUARAN047"
                          call.Location
                          (sprintf
                              "Fuaran.tabs %s declares TabHeaders with %d entries but %d Children — the renderer aligns headers 1:1 with children by index; mismatched lengths leave tabs without labels or labels without targets."
                              id
                              headerCount
                              childCount) ]
                | _ -> []

            let tagMismatch =
                match detail.ChildrenListLength, detail.TabTagsListLength with
                | Some childCount, Some tagCount when tagCount <> childCount ->
                    [ create
                          Error
                          "FUARAN048"
                          call.Location
                          (sprintf
                              "Fuaran.tabs %s declares TabTags with %d entries but %d Children — the typed tag overlay maps tags to children by index; mismatched lengths break the tag → index round-trip."
                              id
                              tagCount
                              childCount) ]
                | _ -> []

            let activeTagOrphan =
                if detail.ActiveTagSome && not detail.TabTagsSome then
                    [ create
                          Warning
                          "FUARAN049"
                          call.Location
                          (sprintf
                              "Fuaran.tabs %s sets ActiveTag = Some _ but TabTags = None — the tag binding has nothing to resolve against; the renderer silently falls back to ActiveIndex. Populate TabTags alongside ActiveTag, or drop ActiveTag and rely on the integer-indexed ActiveIndex / OnSelect."
                              id) ]
                else
                    []

            headerMismatch @ tagMismatch @ activeTagOrphan
        | _ -> [])
