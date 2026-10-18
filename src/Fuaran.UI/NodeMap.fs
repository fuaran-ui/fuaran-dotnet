module Fuaran.UI.NodeMap

// ============================================================================
//  Node.mapMsg — structural relabel of a Node tree's message type (Phase 268).
//
//  `mapMsg : ('a -> 'b) -> Node<'a> -> Node<'b>` rewrites EVERY `Action<'a>`
//  slot across every `NodeKind` / spec into an `Action<'b>`, recursing through
//  the whole tree. It is the erasure primitive `embed` needs: `mapMsg box`
//  turns a typed child `Node<'sub>` into the `Node<obj>` a `NodeKind.Mount`
//  guest requires (polymorphic recursion is forbidden at the boundary, and an
//  unsafe `box |> unbox<Node<obj>>` cast throws under .NET's reified generics,
//  so a genuine structural map is the only portable option, on both the Fable
//  and .NET pipelines).
//
//  Since Phase 2188 the map is GENERATED: `Generated.mapMsg`, derived from the
//  vocabulary (`Gen.Derivation.MapMsg` in `src/Fuaran.UI.Idl/Support.fs`). It
//  rebuilds every declaration generic in the message parameter, composes every
//  handler with the relabel, and maps the `Switch` projection through the
//  member its declared support supplies. A new kind, field or action case is
//  covered by regeneration, so it cannot be left behind by a hand edit here.
//
//  This module keeps the three entry points the tier's callers name. `mapKind`
//  and `mapState` relabel one part of a node, which the generated layer maps
//  only inside a node, so each is the generated map over a carrier node that
//  holds nothing else: the node mapper maps `Kind` and `State` independently of
//  the rest of the envelope, so the part read back is exactly that part mapped.
// ============================================================================

open Fuaran.UI.Types

/// Relabel a whole `Node<'a>` to `Node<'b>`.
let mapMsg (f: 'a -> 'b) (node: Node<'a>) : Node<'b> = Generated.mapMsg f node

/// A node holding nothing but what the caller puts in it.
let private carrier () : Node<'a> = Generated.mkSkeleton "" 0

/// Relabel a `NodeKind<'a>` to `NodeKind<'b>`.
let mapKind (f: 'a -> 'b) (kind: NodeKind<'a>) : NodeKind<'b> =
    (Generated.mapMsg f { carrier () with Kind = kind }).Kind

/// Relabel a `StateBehaviour<'a>` to `StateBehaviour<'b>`.
let mapState (f: 'a -> 'b) (state: StateBehaviour<'a>) : StateBehaviour<'b> =
    // The node mapper maps a `Some` state to a `Some` state, so `Option.get`
    // reads back the one it was given.
    (Generated.mapMsg f { carrier () with State = Some state }).State |> Option.get
