namespace Fuaran.UI.ServerDriven

// Phase 1536 — `ClientEffect.Navigate` carries a `NavigateTarget`, the same
// closed enum `Action.Navigate` does.
open Fuaran.UI.Types

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
    ///
    /// Phase 1536 — `target` names the browsing context, mirroring
    /// `Action.Navigate`'s own. It is OMITTED from the encoding at `Self`, so
    /// every `{"kind":"Navigate","route":…}` a shim has ever received is
    /// byte-identical and the pre-1536 shim keeps working unchanged for the
    /// case it already handled.
    ///
    /// A FIELD rather than a second effect arm, deliberately: `Blank` modifies
    /// the same act — go here — rather than naming a different one, and a
    /// sibling `OpenInNewTab` would be the near-synonym pair the vocabulary
    /// charter forbids one layer down. The shim opens a `Blank` target with
    /// `noopener,noreferrer`, which is where that security property is
    /// discharged on this path exactly as the renderer discharges it on the
    /// client path.
    | Navigate of route: string * target: NavigateTarget
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
    /// Open the reader's own print dialogue (`Action.Print`, Phase 1124). The
    /// FIRST payload-free effect in this channel, and the emptiness is the
    /// point: the paged medium belongs to the host and every parameter of the
    /// printing to the reader, so there is no page size, sheet range or target
    /// subtree for the server to name. `{"kind":"Print"}` is the whole
    /// instruction.
    ///
    /// It lowers rather than executing server-side for the reason this whole
    /// channel exists — a server has no printer — and it round-trips nothing
    /// back: `window.print()` reports neither whether the reader printed nor
    /// what they chose, so unlike `ReadFileBody` there is no result `LiveEvent`.
    | Print
    /// Ask the reader `prompt` and send the answer back (`Action.Confirm`,
    /// Phase 1537). `token` names WHICH confirm in the originating gesture is
    /// being answered, so a `Chain` raising two of them is unambiguous.
    ///
    /// **The continuations are deliberately NOT in this instruction.** The shim
    /// is told what to ask and nothing about what will happen; it answers yes or
    /// no, and the server — which holds the tree, the gate and the egress policy
    /// — decides what that answer means. Shipping the branches would move the
    /// decision to the least trusted party in the system.
    ///
    /// The answer round-trips as the ORIGINATING event re-delivered with
    /// `confirmToken` / `confirmAccepted` in its payload — the `PushState` /
    /// `popstate` shape, with no new event name and no server-side pending
    /// state. It is re-validated in full on arrival, exactly as the first
    /// delivery was.
    | Confirm of prompt: string * token: string

module ClientEffect =

    // The three common control characters keep their short escapes; every other
    // control character (U+0000–U+001F) is escaped as \u00XX — a raw control byte
    // inside a JSON string is invalid JSON, so this is a validity requirement,
    // not a style choice.
    let private esc (s: string) : string =
        let sb = System.Text.StringBuilder(s.Length)

        for ch in s do
            match ch with
            | '\\' -> sb.Append "\\\\" |> ignore
            | '"' -> sb.Append "\\\"" |> ignore
            | '\n' -> sb.Append "\\n" |> ignore
            | '\r' -> sb.Append "\\r" |> ignore
            | '\t' -> sb.Append "\\t" |> ignore
            | c when c < ' ' -> sb.Append(sprintf "\\u%04x" (int c)) |> ignore
            | c -> sb.Append c |> ignore

        sb.ToString()

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
        | Print -> "Print"
        | Confirm _ -> "Confirm"

    /// Encode one effect as tagged-object camelCase JSON.
    let encode (effect: ClientEffect) : string =
        match effect with
        | WriteToClipboard text -> $"""{{"kind":"WriteToClipboard","text":{q text}}}"""
        | Navigate(route, target) ->
            // Phase 1536 — `target` rides only when it is not `Self`, so the
            // pre-1536 bytes are unchanged for the pre-1536 meaning.
            match target with
            | NavigateTarget.Self -> $"""{{"kind":"Navigate","route":{q route}}}"""
            | NavigateTarget.Blank -> $"""{{"kind":"Navigate","route":{q route},"target":"Blank"}}"""
        | PushState route -> $"""{{"kind":"PushState","route":{q route}}}"""
        | Focus nodeId -> $"""{{"kind":"Focus","nodeId":{q nodeId}}}"""
        | Download(url, name) -> $"""{{"kind":"Download","url":{q url},"name":{q name}}}"""
        | ReadFileBody(nodeId, encoding) -> $"""{{"kind":"ReadFileBody","nodeId":{q nodeId},"encoding":{q encoding}}}"""
        | Print -> """{"kind":"Print"}"""
        | Confirm(prompt, token) -> $"""{{"kind":"Confirm","prompt":{q prompt},"token":{q token}}}"""

    /// Encode an effect list as a JSON array.
    let encodeList (effects: ClientEffect list) : string =
        "[" + (effects |> List.map encode |> String.concat ",") + "]"
