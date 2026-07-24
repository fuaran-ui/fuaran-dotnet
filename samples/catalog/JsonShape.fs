module Fuaran.Samples.Catalog.JsonShape

// ============================================================================
//  Wire-shape rendering for the gallery.
//
//  Each catalog card shows the §4d-shaped canonical JSON of its rendered
//  `Node<unit>` alongside the live render. The projection is the canonical
//  byte-for-byte encoder
//  `Fuaran.UI.OpStream.Abstractions.CanonicalJson.encodeNode` directly —
//  Phase 191/192 made `Fuaran.UI.Ops` + `CanonicalJson.encodeNode`
//  Fable-portable, so the catalog no longer ships its own hand-rolled
//  Fable-safe encoder fork (which had drifted from canonical kind
//  coverage). See the dedup note in Catalog.fsproj.
//
//  The inspector now shows the TRUE canonical wire shape — `$type`-hoisted
//  flat kinds, Ordinal-sorted keys, the pinned float layout — not an
//  illustrative approximation. `prettyPrint` below is a display-only
//  re-indenter over that compact canonical string.
// ============================================================================

open Fuaran.UI.Types
open Fuaran.UI.OpStream.Abstractions

// ─── Spec → JSON projection ──────────────────────────────────────────────

/// Project a `Node<unit>` to its canonical §4d-shaped JSON string via the
/// shared `CanonicalJson.encodeNode` encoder (generic over `'Msg`).
let toCanonical (node: Node<unit>) : string = CanonicalJson.encodeNode node

// ─── Pretty-printer (single-pass, Fable-safe) ────────────────────────────

let prettyPrint (indent: int) (json: string) : string =
    let sb = System.Text.StringBuilder()
    let pad = System.String(' ', indent)
    let mutable depth = 0
    let mutable inString = false
    let mutable escaped = false

    let newlinePad (d: int) =
        sb.Append('\n') |> ignore

        for _ in 1..d do
            sb.Append(pad) |> ignore

    for ch in json do
        if escaped then
            sb.Append(ch) |> ignore
            escaped <- false
        elif inString then
            sb.Append(ch) |> ignore

            if ch = '\\' then
                escaped <- true
            elif ch = '"' then
                inString <- false
        else
            match ch with
            | '"' ->
                sb.Append(ch) |> ignore
                inString <- true
            | '{'
            | '[' ->
                sb.Append(ch) |> ignore
                depth <- depth + 1
                newlinePad depth
            | '}'
            | ']' ->
                depth <- depth - 1
                newlinePad depth
                sb.Append(ch) |> ignore
            | ',' ->
                sb.Append(ch) |> ignore
                newlinePad depth
            | ':' ->
                sb.Append(ch) |> ignore
                sb.Append(' ') |> ignore
            | _ -> sb.Append(ch) |> ignore

    sb.ToString()
