namespace Fuaran.UI.ServerDriven

// ============================================================================
//  DomPatch — the lowered, closure-free, browser-applyable patch vocabulary
//  (Phase 152, Track B).
//
//  The entire instruction set the generic JS shim understands: a `TreeOp` list
//  (Phase 152 Track A) is lowered server-side into `DomPatch`es, sent over the
//  live channel, and the shim applies them to the addressable DOM (each node
//  carries `data-fuaran-node-id`) — targeted, no full re-render, no flash.
//
//  ~7 primitives, deliberately tiny so the shim stays a few KB and Fuaran-
//  agnostic. Structure-adding ops carry pre-rendered HTML *fragments*
//  (produced server-side via `Fuaran.UI.Renderer.Server`, byte-identical to
//  first paint — no visual seam); granular ops carry just the text/attr delta.
//
//  Wire shape: tagged-object camelCase JSON (the same discipline as
//  `LayoutFlag` / `StyleFlag` / `StyleObservation`), so the shim's parser and
//  any host transport read one stable shape. `FSharp.Core`-only + Fable-clean
//  (no renderer dep — the lowering that *produces* these lives in the package's
//  lowering module, which is what pulls `Renderer.Server`).
// ============================================================================

/// The project's ONE JSON string writer (Phase 2071), shared by the `DomPatch`
/// and `ClientEffect` encoders — the two halves of every frame. It lives here
/// because this is the project's first compiled file.
///
/// The three common control characters keep their short escapes; every other
/// character below U+0020 is written as `\u00XX` in lower-case hex. A raw control
/// byte inside a JSON string is invalid JSON, so this is a validity requirement,
/// not a style choice: before 2071 the `DomPatch` half escaped only `\n`, `\r`
/// and `\t`, and a rendered label carrying U+000C failed the client's parse for
/// that whole frame. A string with nothing to escape is returned between quotes
/// as it is, and clean runs are copied whole, so the large HTML fragments a
/// `DomPatch` carries are not rebuilt one character at a time.
///
/// This is a short-escape dialect, so it is not the op-stream's canonical
/// string writer (`CanonicalJson`), and the bytes it emits are this wire's own.
module internal JsonString =
    let private hex = "0123456789abcdef"

    let private needsEscape (c: char) = c = '"' || c = '\\' || c < ' '

    /// `s` as a JSON string literal, quotes included.
    ///
    /// The escaped path assembles its pieces with `String.concat` rather than a
    /// `StringBuilder`: under Fable with nullness on, `StringBuilder.ToString`
    /// lowers to an import the bundled JavaScript library does not export, so a
    /// module using it fails at its first `import` — and the wire types are
    /// Fable-consumed.
    let quote (s: string) : string =
        let mutable first = -1
        let mutable i = 0

        while first < 0 && i < s.Length do
            if needsEscape s.[i] then
                first <- i

            i <- i + 1

        if first < 0 then
            "\"" + s + "\""
        else
            let parts = ResizeArray<string>()
            parts.Add "\""
            parts.Add(s.Substring(0, first))
            let mutable runStart = first

            for j in first .. s.Length - 1 do
                let c = s.[j]

                if needsEscape c then
                    if j > runStart then
                        parts.Add(s.Substring(runStart, j - runStart))

                    parts.Add(
                        match c with
                        | '"' -> "\\\""
                        | '\\' -> "\\\\"
                        | '\n' -> "\\n"
                        | '\r' -> "\\r"
                        | '\t' -> "\\t"
                        | _ -> "\\u00" + hex.Substring(int c >>> 4, 1) + hex.Substring(int c &&& 0xF, 1)
                    )

                    runStart <- j + 1

            if runStart < s.Length then
                parts.Add(s.Substring runStart)

            parts.Add "\""
            String.concat "" parts

/// One browser-applyable DOM patch, addressed by `data-fuaran-node-id`.
type DomPatch =
    /// Set attribute `name` to `value` on the addressed node.
    | SetAttr of nodeId: string * name: string * value: string
    /// Remove attribute `name` from the addressed node.
    | RemoveAttr of nodeId: string * name: string
    /// Replace the addressed node's text content.
    | SetText of nodeId: string * text: string
    /// Replace the addressed node's outer HTML with a pre-rendered fragment
    /// (a kind swap or a wholesale node re-render).
    | ReplaceFragment of nodeId: string * html: string
    /// Insert a pre-rendered HTML fragment under `parentId` at `position`.
    | InsertFragment of parentId: string * position: int * html: string
    /// Remove the addressed node from the DOM.
    | RemoveNode of nodeId: string
    /// Reorder `parentId`'s children to the given id order (DOM move, no
    /// re-render — preserves element identity / focus / scroll).
    | ReorderChildren of parentId: string * orderedIds: string list
    /// Relocate the **existing** addressed element to `newParentId` at
    /// `position` (the shim moves the live DOM node — no re-render — so its
    /// identity / focus / scroll survive the move). The lowering of a
    /// cross-parent `TreeOp.MoveNode`.
    | MoveNode of nodeId: string * newParentId: string * position: int

module DomPatch =
    let private inv = System.Globalization.CultureInfo.InvariantCulture

    let private q = JsonString.quote

    /// Stable discriminator string — PascalCase-matched to the DU cases.
    let kind (patch: DomPatch) : string =
        match patch with
        | SetAttr _ -> "SetAttr"
        | RemoveAttr _ -> "RemoveAttr"
        | SetText _ -> "SetText"
        | ReplaceFragment _ -> "ReplaceFragment"
        | InsertFragment _ -> "InsertFragment"
        | RemoveNode _ -> "RemoveNode"
        | ReorderChildren _ -> "ReorderChildren"
        | MoveNode _ -> "MoveNode"

    /// Encode one patch as tagged-object camelCase JSON.
    let encode (patch: DomPatch) : string =
        match patch with
        | SetAttr(nodeId, name, value) ->
            $"""{{"kind":"SetAttr","nodeId":{q nodeId},"name":{q name},"value":{q value}}}"""
        | RemoveAttr(nodeId, name) -> $"""{{"kind":"RemoveAttr","nodeId":{q nodeId},"name":{q name}}}"""
        | SetText(nodeId, text) -> $"""{{"kind":"SetText","nodeId":{q nodeId},"text":{q text}}}"""
        | ReplaceFragment(nodeId, html) -> $"""{{"kind":"ReplaceFragment","nodeId":{q nodeId},"html":{q html}}}"""
        | InsertFragment(parentId, position, html) ->
            let p = position.ToString(inv)
            $"""{{"kind":"InsertFragment","parentId":{q parentId},"position":{p},"html":{q html}}}"""
        | RemoveNode nodeId -> $"""{{"kind":"RemoveNode","nodeId":{q nodeId}}}"""
        | ReorderChildren(parentId, orderedIds) ->
            let ids = orderedIds |> List.map q |> String.concat ","
            $"""{{"kind":"ReorderChildren","parentId":{q parentId},"orderedIds":[{ids}]}}"""
        | MoveNode(nodeId, newParentId, position) ->
            let p = position.ToString(inv)
            $"""{{"kind":"MoveNode","nodeId":{q nodeId},"newParentId":{q newParentId},"position":{p}}}"""

    /// Encode a patch list as a JSON array.
    let encodeList (patches: DomPatch list) : string =
        "[" + (patches |> List.map encode |> String.concat ",") + "]"
