module Fuaran.UI.Ops.TreeOpMap

// ============================================================================
//  TreeOp.mapMsg — retype a decoded `TreeOp<obj>` onto a host's own `'Msg`.
//
//  The decoder returns `TreeOp<obj>`: the wire carries no message type, so
//  every `'Msg` slot in a decoded op holds an erased placeholder. A host that
//  persists its op-stream and reads it back needs the op at ITS `'Msg`, and
//  that mapping is the same structural walk for every host — so it lives here,
//  once, rather than as a private re-typing in each of them.
//
//  ASSEMBLED, NOT INVENTED. `GuestExport.mapOpIds` is the traversal template
//  (the same eleven-case exhaustive walk over a different axis) and
//  `Fuaran.UI.NodeMap` is the inner half: `mapMsg` / `mapState` / `mapKind`
//  already relabel a whole tree's message type and are exhaustive by
//  construction. This module adds only the partiality the node map does not
//  have — a mapper that can DECLINE a payload — and the op-level traversal.
//
//  ── THE PARTIALITY, AND WHY THE CONTRACT HAS TWO HALVES ───────────────────
//
//  `NodeMap.mapMsg` takes a TOTAL `'a -> 'b`; the mapper here is
//  `obj -> 'Msg option`, because a host's message type need not have a case
//  for every payload a decoded op might carry. A payload the mapper cannot
//  type is refused BY NAME — never replaced by a default, which would put a
//  message on the tree that the host never wrote and cannot recognise.
//
//  A `'Msg` sits in a `Node` in one of two ways, and they are not equally
//  reachable:
//
//   * **Eagerly stored** — `Action.Dispatch`'s message is a field. Every such
//     payload is visited BEFORE the mapped op is built (the probe pass below),
//     so a `None` here makes `mapMsg` return `Error` having constructed
//     nothing at all.
//   * **Closure-carried** — `Action.Call`'s `onResult`, `ReadFileBody`'s
//     `onRead`, `Tabs.OnSelect`, a custom cell's render closure. Their
//     payloads do not EXIST until the closure is called, and calling a
//     host-supplied function with a fabricated argument to find out is not
//     something a map may do. They relabel by composition, and a `None` at
//     invocation raises `MapRefusalRaised` carrying the same `MapRefusal` —
//     still by name, at the only moment the payload exists.
//
//  That asymmetry is a property of the type, not a gap in the implementation:
//  a function's result is not data until it is applied. It is stated here, and
//  in `MapRefusal`'s own doc comment, so a host reads it rather than meets it.
//
//  In practice the second half is unreachable for the ops this facility exists
//  to map. `Fuaran.UI.Ops.JsonDecode` replaces every lost closure with a
//  CONSTANT function returning the same `"<closure>"` sentinel it puts in
//  `Action.Dispatch`, so a mapper that is total on that one value is total
//  over every decoded op — which is what makes a host's mapper a one-liner.
//
//  Pure and FSharp.Core-only (FGP 2), Fable-portable like the rest of this
//  package: no IO, no clock, no reflection.
// ============================================================================

open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.Types

/// A payload the host's mapper declined, named by the op it sits in and the
/// slot of that op it was reached through.
///
/// `Slot` is the OP's slot (`"child"`, `"kind"`, `"state"`, `"node"`, and
/// `"ops[i]."`-prefixed inside a `Batch`), not a path into the node's own spec
/// vocabulary — the node map is a structural relabel and carries no path. The
/// refused payloads are rendered instead, distinct and in first-seen order,
/// which is what identifies them to a reader.
type MapRefusal =
    {
        /// The `TreeOp` case the refusal was found in — `"InsertChild"`,
        /// `"EditNode"`, `"UpdateState"`, `"ReplaceRoot"`.
        Op: string
        /// The slot of that op, `"ops[i]."`-prefixed when the op sat inside a
        /// `Batch`.
        Slot: string
        /// The node the op addresses, where the op addresses one.
        Node: string option
        /// Each distinct payload the mapper returned `None` for, rendered and
        /// length-bounded, in the order the walk first met them.
        Payloads: string list
    }

module MapRefusal =

    /// One line naming the op, the slot, the addressed node and every refused
    /// payload — the string a codec surfaces as its decode error.
    let render (refusal: MapRefusal) : string =
        let where =
            match refusal.Node with
            | Some node -> sprintf "%s(%s).%s" refusal.Op node refusal.Slot
            | None -> sprintf "%s.%s" refusal.Op refusal.Slot

        sprintf
            "TreeOp.mapMsg refused %s — the mapper declined %d payload(s): %s"
            where
            (List.length refusal.Payloads)
            (String.concat "; " refusal.Payloads)

/// Raised when a CLOSURE-carried payload is declined at invocation time — the
/// half of the contract `mapMsg`'s `Result` structurally cannot cover, since
/// the payload did not exist when the `Result` was produced. See this module's
/// header for why the two halves differ.
exception MapRefusalRaised of MapRefusal

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module TreeOp =

    /// The longest rendering of a refused payload kept in a `MapRefusal`. A
    /// refusal is a diagnostic; a boxed tree rendered whole is not one.
    [<Literal>]
    let private maxPayloadRendering = 120

    let private renderPayload (payload: obj) : string =
        // `%A` renders a null reference as `<null>`, so the absent case needs
        // no branch of its own — and writing one would need a null test `obj`
        // does not support under F# 10's nullness.
        let rendered = sprintf "%A" payload

        if rendered.Length > maxPayloadRendering then
            rendered.Substring(0, maxPayloadRendering) + "…"
        else
            rendered

    /// Run `walk` with a collector in place of the real mapper, returning the
    /// distinct renderings of every EAGERLY-REACHABLE payload the mapper
    /// declined. `walk`'s result is discarded — the pass exists for what the
    /// collector saw, not for the tree it built.
    let private declined (f: obj -> 'Msg option) (walk: (obj -> unit) -> unit) : string list =
        let seen = System.Collections.Generic.HashSet<string>()
        let found = ResizeArray<string>()

        walk (fun payload ->
            match f payload with
            | Some _ -> ()
            | None ->
                let rendered = renderPayload payload

                if seen.Add rendered then
                    found.Add rendered)

        List.ofSeq found

    /// The mapper lifted to the total function `NodeMap` requires. Its `None`
    /// branch is unreachable for eagerly-stored payloads — `declined` has
    /// already proved there are none — and is the closure half of the contract
    /// for the rest.
    let private lift (op: string) (node: string option) (slot: string) (f: obj -> 'Msg option) : obj -> 'Msg =
        fun payload ->
            match f payload with
            | Some msg -> msg
            | None ->
                raise (
                    MapRefusalRaised
                        { Op = op
                          Slot = slot
                          Node = node
                          Payloads = [ renderPayload payload ] }
                )

    /// Probe, then map. The two passes are deliberate: nothing is constructed
    /// at the host's `'Msg` until every payload that CAN be checked has been.
    let private through
        (op: string)
        (node: string option)
        (slot: string)
        (f: obj -> 'Msg option)
        (probe: (obj -> unit) -> unit)
        (build: (obj -> 'Msg) -> 'a)
        : Result<'a, MapRefusal> =
        match declined f probe with
        | [] -> Ok(build (lift op node slot f))
        | payloads ->
            Error
                { Op = op
                  Slot = slot
                  Node = node
                  Payloads = payloads }

    let private addressed (NodeId raw) = Some raw

    /// Retype a decoded `TreeOp<obj>` onto the host's `'Msg`.
    ///
    /// `f` answers, for one erased payload, which host message it is — or
    /// `None`, which refuses the whole op by name rather than substituting a
    /// default. Total over all eleven op cases: the match carries no wildcard,
    /// so a new `TreeOp` case surfaces here as an incomplete-match build
    /// failure rather than escaping the map.
    ///
    /// The `'Msg`-free ops (`UpdateProp`, `ReplaceBinding`, `UpdateStyle`,
    /// `RemoveNode`, `MoveNode`, `ReorderChildren`) are re-tagged and can never
    /// refuse — `PropValue`, `Binding<obj>`, `SemanticStyle` and `NodeId` carry
    /// no message.
    let rec mapMsg (f: obj -> 'Msg option) (op: TreeOp<obj>) : Result<TreeOp<'Msg>, MapRefusal> =
        match op with
        | TreeOp.EditNode(id, kind) ->
            through "EditNode" (addressed id) "kind" f (fun collect -> NodeMap.mapKind collect kind |> ignore) (fun m ->
                TreeOp.EditNode(id, NodeMap.mapKind m kind))

        | TreeOp.UpdateProp(id, path, value) -> Ok(TreeOp.UpdateProp(id, path, value))

        | TreeOp.ReplaceBinding(id, slot, binding) -> Ok(TreeOp.ReplaceBinding(id, slot, binding))

        | TreeOp.UpdateStyle(id, style) -> Ok(TreeOp.UpdateStyle(id, style))

        | TreeOp.UpdateState(id, state) ->
            through
                "UpdateState"
                (addressed id)
                "state"
                f
                (fun collect -> NodeMap.mapState collect state |> ignore)
                (fun m -> TreeOp.UpdateState(id, NodeMap.mapState m state))

        | TreeOp.InsertChild(parentId, child) ->
            through
                "InsertChild"
                (addressed parentId)
                "child"
                f
                (fun collect -> NodeMap.mapMsg collect child |> ignore)
                (fun m -> TreeOp.InsertChild(parentId, NodeMap.mapMsg m child))

        | TreeOp.RemoveNode id -> Ok(TreeOp.RemoveNode id)

        | TreeOp.MoveNode(id, newParentId) -> Ok(TreeOp.MoveNode(id, newParentId))

        | TreeOp.ReorderChildren(parentId, newOrder) -> Ok(TreeOp.ReorderChildren(parentId, newOrder))

        | TreeOp.ReplaceRoot node ->
            through
                "ReplaceRoot"
                (Some node.Id)
                "node"
                f
                (fun collect -> NodeMap.mapMsg collect node |> ignore)
                (fun m -> TreeOp.ReplaceRoot(NodeMap.mapMsg m node))

        | TreeOp.Batch ops ->
            // First refusal wins, and it keeps the INNER op's name: "which op
            // could not be typed" is the question a reader has, and `Batch` is
            // not an answer to it. The index is carried in the slot instead.
            let rec fold index mapped remaining =
                match remaining with
                | [] -> Ok(TreeOp.Batch(List.rev mapped))
                | head :: tail ->
                    match mapMsg f head with
                    | Ok m -> fold (index + 1) (m :: mapped) tail
                    | Error refusal ->
                        Error
                            { refusal with
                                Slot = sprintf "ops[%d].%s" index refusal.Slot }

            fold 0 [] ops
