namespace Fuaran.UI.ServerDriven

// ============================================================================
//  ClientEffect — the client-only effects the server decides but the shim
//  performs (Phase 152, Track B).
//
//  Not every `IFuaranRuntime` arm runs server-side. The *computational* arms
//  (`Call`, `SetState`, `Notify`, `AiTool`, `Computed`) execute on the server
//  (the server-closure win); the *inherently browser* arms have no
//  DOM-mutation form, so the driver lowers them to a `ClientEffect` the shim
//  performs. A result-bearing effect (`ReadFileBody`) round-trips its result
//  back as a `LiveEvent`.
//
//  Surfaced by a live consumer: a real app's use of `Action.WriteToClipboard`
//  + `window.location.href` proved `DomPatch` alone is insufficient — the
//  server-driven runtime cannot execute browser-only effects server-side, so
//  the wire needs this second instruction channel alongside `DomPatch`. Maps
//  1:1 onto the inherently-client arms of `IFuaranRuntime`.
//
//  Same wire discipline as `DomPatch`: tagged-object camelCase JSON,
//  `FSharp.Core`-only + Fable-clean.
// ============================================================================

/// A client-only effect the shim performs on the server's behalf.
type ClientEffect =
    /// Write `text` to the clipboard (`Action.WriteToClipboard`).
    | WriteToClipboard of text: string
    /// Navigate the browser to `route` (`window.location` — a full page load /
    /// real navigation; the full-SSR mode). Crawlable + no-JS via `Display.Link`.
    | Navigate of route: string
    /// Update the URL bar to `route` WITHOUT a reload (`history.pushState`) — the
    /// in-place navigation mode's URL sync (Phase 157). The tree swap rides the
    /// accompanying `DomPatch`es; this only keeps the address bar + back/forward
    /// in step. The shim listens for `popstate` and round-trips a `popstate`
    /// `LiveEvent` carrying the popped `route` so the server swaps the tree back.
    | PushState of route: string
    /// Move focus to the addressed node.
    | Focus of nodeId: string
    /// Trigger a file download of `url`, suggested filename `name`.
    | Download of url: string * name: string
    /// Read the body of a selected file at the addressed node with the given
    /// encoding (`"Text"` / `"Base64"` / `"DataUrl"`); the body round-trips
    /// back to the server as a `LiveEvent` (`Action.ReadFileBody`).
    | ReadFileBody of nodeId: string * encoding: string

module ClientEffect =

    let private esc (s: string) : string =
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t")

    let private q (s: string) : string = "\"" + esc s + "\""

    /// Stable discriminator string — PascalCase-matched to the DU cases.
    let kind (effect: ClientEffect) : string =
        match effect with
        | WriteToClipboard _ -> "WriteToClipboard"
        | Navigate _ -> "Navigate"
        | PushState _ -> "PushState"
        | Focus _ -> "Focus"
        | Download _ -> "Download"
        | ReadFileBody _ -> "ReadFileBody"

    /// Encode one effect as tagged-object camelCase JSON.
    let encode (effect: ClientEffect) : string =
        match effect with
        | WriteToClipboard text -> $"""{{"kind":"WriteToClipboard","text":{q text}}}"""
        | Navigate route -> $"""{{"kind":"Navigate","route":{q route}}}"""
        | PushState route -> $"""{{"kind":"PushState","route":{q route}}}"""
        | Focus nodeId -> $"""{{"kind":"Focus","nodeId":{q nodeId}}}"""
        | Download(url, name) -> $"""{{"kind":"Download","url":{q url},"name":{q name}}}"""
        | ReadFileBody(nodeId, encoding) -> $"""{{"kind":"ReadFileBody","nodeId":{q nodeId},"encoding":{q encoding}}}"""

    /// Encode an effect list as a JSON array.
    let encodeList (effects: ClientEffect list) : string =
        "[" + (effects |> List.map encode |> String.concat ",") + "]"
