namespace Fuaran.UI.ServerDriven

open Fuaran.UI.Types

// ============================================================================
//  ConfirmAnswer — the server-driven round trip behind `Action.Confirm`
//  (Phase 1537).
//
//  A confirm is the only action in the language whose interpretation PAUSES:
//  the server decides to ask, the reader answers on the client, and the server
//  then decides what the answer means. Three properties are wanted of that
//  round trip and this module is what holds them.
//
//  1. **The continuations never leave the server.** `ClientEffect.Confirm`
//     carries the prompt and a token, and nothing about what a yes will do. A
//     shim that ships the branches is a shim that can perform them.
//
//  2. **No server-side pending state.** The token is the confirm's STRUCTURAL
//     PATH inside the resolved action, so the ask and the answer agree by
//     construction. A pending map would need a lifetime, an eviction policy and
//     a memory bound, and every one of those is a way for an untrusted client
//     to make the server hold something. The tree is the state — the
//     `PushState` / `popstate` shape this channel already uses.
//
//  3. **The answer is re-validated exactly like the ask.** It arrives as the
//     ORIGINATING event re-delivered, carrying two extra payload members, so it
//     passes through the same node-exists / event-legitimate / payload-in-bounds
//     / dispatch-gate boundary the first delivery did. No new event name is
//     admitted, which is what keeps `Validation.legitimateEvents` — the
//     allow-list keyed by the node that RECEIVES an event — untouched.
//
//  **What this does NOT claim, stated here because it is the thing most likely
//  to be assumed.** A confirmation is a courtesy to the reader, never an
//  authorisation. The answer comes from the client, and a hostile client
//  answers yes without showing anyone a dialogue. Anything that must not happen
//  without permission is refused by the dispatch gate — which the continuation
//  meets on its own, in `Driver.step` — and never by the question.
// ============================================================================

/// The structural address of one `Confirm` inside a resolved action: a
/// dot-joined path of `Chain` positions and continuation names, with the empty
/// string naming the action itself.
[<RequireQualifiedAccess>]
module ConfirmPath =

    /// The path of the resolved action itself.
    let root: string = ""

    /// The path of the `i`th member of a `Chain` at `path`.
    let child (path: string) (i: int) : string =
        if path = "" then string i else path + "." + string i

    /// The path of a named continuation of the `Confirm` at `path`.
    let branch (path: string) (name: string) : string =
        if path = "" then name else path + "." + name

    /// The token a `ClientEffect.Confirm` carries: the originating node and the
    /// confirm's path within that node's resolved action. The node id is
    /// included so a token minted for one node cannot address another's action
    /// — the shim chooses what it sends back, and a token is a payload value
    /// like any other.
    let token (nodeId: string) (path: string) : string = nodeId + "#" + path

/// What an inbound confirm answer resolved to.
[<RequireQualifiedAccess>]
type ConfirmResolution<'Msg> =
    /// The token addresses a `Confirm` and the answer selects `Action` to run.
    | Branch of Action<'Msg>
    /// The token addresses a `Confirm`, the reader declined, and the author
    /// declared no `onCancel`. Legitimate, and nothing happens.
    | Nothing
    /// The token addresses no `Confirm` in this node's action. Either the tree
    /// changed under the reader, or the token was forged.
    | Unaddressed

[<RequireQualifiedAccess>]
module ConfirmAnswer =

    /// The two payload members a confirm answer carries. Named here rather than
    /// spelled at each reader so the shim contract has one home.
    [<Literal>]
    let TokenKey = "confirmToken"

    [<Literal>]
    let AcceptedKey = "confirmAccepted"

    /// Follow a path through an action to the `Confirm` it addresses.
    ///
    /// Deliberately TOTAL and forgiving of a wrong path: every mismatch answers
    /// `None` rather than throwing. The path arrives from the client, so a
    /// segment naming a `Chain` position that no longer exists is an ordinary
    /// consequence of a tree that moved on, not an exceptional condition.
    let rec private navigate (segments: string list) (action: Action<'Msg>) : Action<'Msg> option =
        match segments, action with
        | [], Action.Confirm _ -> Some action
        | [], _ -> None
        | seg :: rest, Action.Chain ops ->
            match System.Int32.TryParse seg with
            | true, i when i >= 0 && i < List.length ops -> navigate rest (List.item i ops)
            | _ -> None
        // A host-authored tree may nest a confirm (the wire cannot — the policy
        // decoder refuses it at depth one), so the continuation names are
        // addressable. An inner dialogue is reached only after the outer one is
        // answered, and its own token carries the outer's path as a prefix.
        | "onConfirm" :: rest, Action.Confirm(_, onConfirm, _) -> navigate rest onConfirm
        | "onCancel" :: rest, Action.Confirm(_, _, Some onCancel) -> navigate rest onCancel
        | _ -> None

    /// Resolve an inbound answer against the action the node resolves to NOW.
    ///
    /// The token's node half must match the event's node: the shim chooses what
    /// it sends back, so a token is untrusted payload like any other, and one
    /// minted for a delete button must not address a save button's action.
    let resolve (nodeId: string) (token: string) (accepted: bool) (action: Action<'Msg>) : ConfirmResolution<'Msg> =
        let prefix = nodeId + "#"

        if not (token.StartsWith(prefix, System.StringComparison.Ordinal)) then
            ConfirmResolution.Unaddressed
        else
            let path = token.Substring prefix.Length

            let segments = if path = "" then [] else path.Split '.' |> List.ofArray

            match navigate segments action with
            | Some(Action.Confirm(_, onConfirm, onCancel)) ->
                if accepted then
                    ConfirmResolution.Branch onConfirm
                else
                    match onCancel with
                    | Some c -> ConfirmResolution.Branch c
                    | None -> ConfirmResolution.Nothing
            | _ -> ConfirmResolution.Unaddressed
